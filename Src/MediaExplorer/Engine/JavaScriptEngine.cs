using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Globalization;
using Newtonsoft.Json;
using Math = System.Math;
using NiL.JS.Core;
using NiL.JS.BaseLibrary;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.Storage;

namespace BrowserCore.Engine
{
    /// <summary>
    /// JavaScriptEngine
    /// - Default: JS-0 (tiny allowlist for inline handlers). Always available.
    /// - Optional: Full engine via NiL.JS when compiled with USE_NILJS + NuGet "NiL.JS".
    ///   Exposes window/document/location/console/setTimeout/clearTimeout/XMLHttpRequest/localStorage,
    ///   runs inline & external <script> in DOM order (no 'defer'/'async' yet).
    /// </summary>
    public sealed class JavaScriptEngine
    {
        // Log first-chance exceptions once per exception type to help diagnose missing files/deps
        private static readonly ConcurrentDictionary<string, bool> _firstChanceLogged = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        public Func<Uri, Task<string>> FetchOverride { get; set; }

        private GlobalContext _nil;
        private Context _evalContext;
        private bool _niljsSafeEvalFailed;
        // NiL.JS concurrency & failure guards
        private readonly object _nilEvalLock = new object();

        // Detected character encoding for the current page (set by DecodeBytes or ResourceManager)
        public string DetectedCharset { get; set; } = "UTF-8";

        // ---- Новый формат: BadHashRecord ----
        private class BadHashRecord
        {
            public string Hash { get; set; }        // base64 либо hex строки
            public string Preview { get; set; }     // начало сорца скрипта
            public int Count { get; set; }          // сколько раз пойман
            public DateTime FirstSeen { get; set; }
            public DateTime LastSeen { get; set; }
            // Optional diagnostic fields: populated when a failure is observed.
            // These are best-effort and may be truncated to avoid huge files.
            public string LastExceptionType { get; set; }
            public string LastExceptionMessage { get; set; }
            public string LastExceptionStack { get; set; }
            public DateTime? LastExceptionTime { get; set; }
            // Source/context hint (e.g. page preview or URL) — optional
            public string LastExceptionContext { get; set; }
        }
        // Коллекция актуальных bad-хэшей
        private readonly ConcurrentDictionary<string, BadHashRecord> _badHashRecords = new ConcurrentDictionary<string, BadHashRecord>(StringComparer.OrdinalIgnoreCase);
        private int _safeEvalErrorCount = 0;
        private const int SafeEvalErrorThreshold = 5; // per-page; above this we stop executing inline scripts
        private volatile bool _skipInlineScriptsForPage = false;
        // Phase DIAG (issue D): counters for SafeEval/JSException per SetDom call.
        // Reset in SetDom right before scripts are executed, dumped at the end
        // of RunScriptsAsync / SetDom block. Use long-lived statics to survive
        // across multiple engine instances (the engine is a single instance per app).
        private static int _diagSafeEvalCalls;
        private static int _diagSafeEvalFails;
        private static int _diagJSException;
        // Per-page diagnostic: number of inline scripts skipped due to pre-skip or threshold
        private static int _diagSkippedInlineScripts;
        // Per-hash failure debounce counter: record failures per script hash and only
        // mark as "bad" after reaching BadHashRecordThreshold to avoid false positives.
        private static readonly Dictionary<string, object> _storedData = new Dictionary<string, object>();
        public string GetStoredData(string key) { object v; return _storedData.TryGetValue(key, out v) ? v as string : null; }
        public void ClearStoredData() { _storedData.Clear(); }
        private const int BadHashRecordThreshold = 2;
        // Persist observed bad hashes between runs (теперь json!):
        private const string BadHashStoreFile = "js_bad_hashes.json";
        private readonly SemaphoreSlim _badHashStoreLock = new SemaphoreSlim(1, 1);
        // Debounce timer + lock for coalescing saves to disk
        private readonly object _badHashSaveTimerLock = new object();
        private System.Threading.Timer _badHashSaveTimer;
        private const int BadHashSaveDebounceMs = 2000; // 2s debounce
        private readonly IJsHost _host;
        // MiniJS interpreter (removed — DISABLED since migration to MiniRunner)
        private JsContext _ctx;

        // timers
        private readonly Dictionary<int, System.Threading.Timer> _timers = new Dictionary<int, System.Threading.Timer>();
        private int _nextTimerId = 0;

        // flags
        private bool _allowExternalScripts;
        private bool _executeInlineScriptsOnInnerHTML;
        private SandboxPolicy _sandbox = SandboxPolicy.AllowAll;

        public SandboxPolicy Sandbox
        {
            get { return _sandbox; }
            set { _sandbox = value ?? SandboxPolicy.AllowAll; }
        }

        public bool AllowExternalScripts
        {
            get { return _allowExternalScripts && _sandbox.Allows(SandboxFeature.ExternalScripts); }
            set { _allowExternalScripts = value; }
        }

        public bool ExecuteInlineScriptsOnInnerHTML
        {
            get { return _executeInlineScriptsOnInnerHTML && _sandbox.Allows(SandboxFeature.InlineScripts); }
            set { _executeInlineScriptsOnInnerHTML = value; }
        }

        public bool UseMiniPrattEngine { get; set; } = true;
        
        // Document ready state (for document.readyState property)
        private string _readyState = "loading";
        public JSValue OnPopState { get; set; }
        public JSValue OnHashChange { get; set; }
        
        // Subresource validation delegate (optional)
        public Func<Uri, string, bool> SubresourceAllowed { get; set; }
        private readonly JavaScriptEngine _e;

        internal bool SandboxAllows(SandboxFeature feature, string detail = null)
        {
            if (_sandbox.Allows(feature)) return true;
            RecordSandboxBlock(feature, detail);
            return false;
        }

        private void RecordSandboxBlock(SandboxFeature feature, string detail)
        {
            var messageDetail = detail ?? string.Empty;
            try { TraceFeatureGap("Sandbox", feature.ToString(), messageDetail); } catch { /* swallow */ }
            try
            {
                var status = string.IsNullOrWhiteSpace(messageDetail)
                    ? "[Sandbox] Blocked " + feature
                    : "[Sandbox] Blocked " + feature + " : " + messageDetail;
                _host?.SetStatus(status);
            }
            catch { /* swallow */ }

            lock (_sandboxLogLock)
            {
                if (_sandboxBlocks.Count >= SandboxBlockCapacity) _sandboxBlocks.Dequeue();
                _sandboxBlocks.Enqueue(new SandboxBlockRecord
                {
                    Feature = feature,
                    Detail = messageDetail,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        public SandboxBlockRecord[] GetSandboxBlocksSnapshot()
        {
            lock (_sandboxLogLock) { return _sandboxBlocks.ToArray(); }
        }

        public void ClearSandboxBlockLog()
        {
            lock (_sandboxLogLock) { _sandboxBlocks.Clear(); }
        }

        // in JavaScriptEngine fields
        private readonly HashSet<string> _script404 = new HashSet<string>(StringComparer.Ordinal);
        private readonly object _script404Lock = new object();

        // element-level listeners: id -> (event -> [fnName])
        private readonly Dictionary<string, Dictionary<string, List<string>>> _evtEl =
            new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);

        // Optional bridge supplied by host to provide a CookieContainer for managed HttpClient fallbacks.
        public Func<Uri, System.Net.CookieContainer> CookieBridge { get; set; }
        
        // --- lightweight fields that may be missing in some merge states ---
        // DOM visual registry
        private static readonly System.Collections.Generic.Dictionary<LiteElement, System.WeakReference> _visualMap =
            new System.Collections.Generic.Dictionary<LiteElement, System.WeakReference>(System.Collections.Generic.EqualityComparer<LiteElement>.Default);
        private static System.WeakReference _visualRoot;

        // DOM root exposed to the engine
        private LiteElement _domRoot;
        private string _pageTitle = string.Empty;

        // Computed CSS styles per element — set by DomBasicRenderer after cascade
        internal Dictionary<LiteElement, CssComputed> _computedStyles;

        private readonly object _sandboxLogLock = new object();
        private readonly Queue<SandboxBlockRecord> _sandboxBlocks = new Queue<SandboxBlockRecord>();
        private const int SandboxBlockCapacity = 32;

        public struct SandboxBlockRecord
        {
            public SandboxFeature Feature;
            public string Detail;
            public DateTime Timestamp;
        }

    // Microtask queue
    private readonly System.Collections.Generic.Queue<System.Action> _microtasks = new System.Collections.Generic.Queue<System.Action>();
    private readonly object _microtaskLock = new object();
    private bool _microtaskPumpScheduled = false;

    // Macro-task queue (setTimeout, setInterval, etc.)
    private readonly System.Collections.Generic.Queue<System.Action> _macroTasks = new System.Collections.Generic.Queue<System.Action>();
    private readonly object _macroTaskLock = new object();
    private bool _macroPumpScheduled = false;
    private int _macroExecuting = 0;

    // Feature gap tracing throttling
    private readonly object _featureTraceLock = new object();
    private string _lastFeatureTraceKey;
    private System.DateTime _lastFeatureTraceTime = System.DateTime.MinValue;

        // Response registry (tokenized large response bodies)
        private readonly System.Collections.Generic.Dictionary<string, JavaScriptEngine.ResponseEntry> _responseRegistry =
            new System.Collections.Generic.Dictionary<string, JavaScriptEngine.ResponseEntry>(System.StringComparer.Ordinal);
        private readonly System.Collections.Generic.LinkedList<string> _responseLru = new System.Collections.Generic.LinkedList<string>();
        private readonly object _responseLock = new object();
        private System.TimeSpan _responseTtl = System.TimeSpan.FromMinutes(5);
        private int _responseCapacity = 64;
        private int _responseCounter = 0;
        private volatile bool _responseCleanupRunning = false;

        // Inline thresholds / repaint flags
        private int _inlineThreshold = 1024;
        private volatile bool _repaintRequested = false;

        // --- Mobile-oriented JS limits ---
        // Soft cap on total bytes of script source executed per page. This is
        // intended to keep mobile-class devices responsive by skipping
        // extremely large desktop-style bundles while still running typical
        // mobile-sized scripts.
        private int _pageScriptByteBudget = 10 * 1024 * 1024; // 10 MB default for modern sites
        private int _pageScriptBytesUsed = 0;

        // Small allowance for very tiny inline handlers (e.g., "return false")
        // that should work even when the main script budget is exhausted.
        private const int TinyInlineFreeThreshold = 256;

        // Optional external script fetcher (e.g., wired to ResourceManager.FetchTextAsync)
        // Signature: (uri, referer) => script text or null.
        public Func<Uri, Uri, Task<string>> ExternalScriptFetcher { get; set; }

        // Small in-memory LRU cache for script text, keyed by absolute URL.
        private sealed class ScriptCacheEntry { public string Body; }
        private readonly Dictionary<string, LinkedListNode<Tuple<string, ScriptCacheEntry>>> _scriptMap =
            new Dictionary<string, LinkedListNode<Tuple<string, ScriptCacheEntry>>>(StringComparer.Ordinal);
        private readonly LinkedList<Tuple<string, ScriptCacheEntry>> _scriptLru =
            new LinkedList<Tuple<string, ScriptCacheEntry>>();
        private readonly int _scriptCap = 64;

        /// <summary>
        /// Gets or sets the approximate per-page script byte budget. When the
        /// total executed script source exceeds this value, subsequent
        /// external/inline scripts are skipped for performance. Set to 0 or a
        /// negative value to disable the budget.
        /// </summary>
        public int PageScriptByteBudget
        {
            get { return _pageScriptByteBudget; }
            set { _pageScriptByteBudget = value; }
        }

        // ECMAScript modules
        private ModuleLoader _moduleLoader;

        // Mutation observers / pending mutations
        private readonly object _mutationLock = new object();
        private readonly System.Collections.Generic.List<HostMutationObserver> _activeObservers = new System.Collections.Generic.List<HostMutationObserver>();
        private readonly System.Collections.Generic.List<InternalMutationRecord> _pendingMutations = new System.Collections.Generic.List<InternalMutationRecord>();
        private System.Collections.Generic.List<InternalMutationRecord> _pendingRendererMutations;
        private string _docTitle = string.Empty;
        
        // --- DOM visual registry for approximate layout metrics ---
        public static void RegisterDomVisual(LiteElement node, Windows.UI.Xaml.FrameworkElement fe)
        {
            try { if (node == null || fe == null) return; lock (_visualMap) _visualMap[node] = new System.WeakReference(fe); }
            catch { /* swallow */ }
        }

        private static bool TryGetVisualRect(LiteElement node, out double x, out double y, out double w, out double h)
        {
            x = y = 0; w = h = 0;
            try
            {
                System.WeakReference wr; Windows.UI.Xaml.FrameworkElement fe = null;
                lock (_visualMap)
                {
                    if (!_visualMap.TryGetValue(node, out wr)) return false;
                    fe = wr != null ? wr.Target as Windows.UI.Xaml.FrameworkElement : null;
                }
                if (fe == null) return false;
                w = fe.ActualWidth; h = fe.ActualHeight;
                Windows.UI.Xaml.UIElement root = null;
                try { var wrs = _visualRoot; if (wrs != null) root = wrs.Target as Windows.UI.Xaml.UIElement; } catch { /* swallow */ }
                if (root == null)
                    root = Windows.UI.Xaml.Window.Current != null ? Windows.UI.Xaml.Window.Current.Content as Windows.UI.Xaml.UIElement : null;
                if (root != null)
                {
                    var p = fe.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0));
                    x = p.X; y = p.Y; return true;
                }
            }
            catch { /* swallow */ }
            return false;
        }
        public static void RegisterVisualRoot(Windows.UI.Xaml.UIElement root)
        {
            try { _visualRoot = (root != null ? new System.WeakReference(root) : null); } catch { /* swallow */ }
        }

        // Helper to parse numeric CSS values (e.g., "12px", "0.5em")
        private static bool TryParseNumeric(string s, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            var m = System.Text.RegularExpressions.Regex.Match(s, @"[-+]?[0-9]*\.?[0-9]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            return double.TryParse(m.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value);
        }
        // ---- Phase 1/2/3 state ----
        private readonly Dictionary<string, List<string>> _evtDoc = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _evtWin = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        // JSValue-based event listeners (NiL.JS path) — store actual callback objects
        private readonly System.Collections.Generic.List<JSValue> _evtDocCallbacks = new System.Collections.Generic.List<JSValue>();
        private readonly System.Collections.Generic.List<JSValue> _evtWinCallbacks = new System.Collections.Generic.List<JSValue>();
        private readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<JSValue>> _evtDocCallbacksByType = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<JSValue>>(StringComparer.OrdinalIgnoreCase);
        private readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<JSValue>> _evtWinCallbacksByType = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<JSValue>>(StringComparer.OrdinalIgnoreCase);
        // (MiniJs event listeners removed — DISABLED since migration to MiniRunner)

    // Track stopPropagation requests inside a single handler execution (JS-0 allowlist)
    private volatile bool _stopPropagationRequested;
    // Track preventDefault requests surfaced via runtime event wrappers
    private volatile bool _preventDefaultRequested;

        private void RegisterListener(Dictionary<string, List<string>> bag, string evt, string fnName)
        {
            if (string.IsNullOrWhiteSpace(evt) || string.IsNullOrWhiteSpace(fnName)) return;
            List<string> list;
            if (!bag.TryGetValue(evt, out list) || list == null) { list = new List<string>(); bag[evt] = list; }
            if (!list.Contains(fnName)) list.Add(fnName);
        }

        private void RemoveListener(Dictionary<string, List<string>> bag, string evt, string fnName)
        {
            List<string> list; if (!bag.TryGetValue(evt, out list) || list == null) return;
            list.Remove(fnName);
        }

        private void FireDocumentEvent(string evt)
        {
            try
            {
                // Fire string-based JS-0 callbacks
                List<string> list; if (_evtDoc.TryGetValue(evt, out list) && list != null)
                {
                    foreach (var fn in list.ToArray())
                    {
                        EnqueueMicrotask(() => { try { RunInline(fn + "({ type:'" + evt + "', target:'document' })", _ctx, evt, "document"); } catch { /* swallow */ } });
                    }
                }
                // Fire JSValue-based callbacks (NiL.JS path)
                System.Collections.Generic.List<JSValue> cbList;
                if (_evtDocCallbacksByType.TryGetValue(evt, out cbList) && cbList != null)
                {
                    foreach (var cb in cbList.ToArray())
                    {
                        EnqueueMicrotask(() => { try { InvokeJsCallback(cb, evt, "document"); } catch { } });
                    }
                }
            }
            catch { /* swallow */ }

        }

        private void FireWindowEvent(string evt)
        {
            try
            {
                // Fire string-based JS-0 callbacks
                List<string> list; if (_evtWin.TryGetValue(evt, out list) && list != null)
                {
                    foreach (var fn in list.ToArray())
                    {
                        EnqueueMicrotask(() => { try { RunInline(fn + "({ type:'" + evt + "', target:'window' })", _ctx, evt, "window"); } catch { /* swallow */ } });
                    }
                }
                // Fire JSValue-based callbacks (NiL.JS path)
                System.Collections.Generic.List<JSValue> cbList;
                if (_evtWinCallbacksByType.TryGetValue(evt, out cbList) && cbList != null)
                {
                    foreach (var cb in cbList.ToArray())
                    {
                        EnqueueMicrotask(() => { try { InvokeJsCallback(cb, evt, "window"); } catch { } });
                    }
                }
            }
            catch { /* swallow */ }
        }

        /// <summary>Invoke a JSValue callback with a serialized event object. Casts to NiL.JS Function and calls it.</summary>
        private void InvokeJsCallback(JSValue callback, string eventType, string target)
        {
            if (callback == null || callback.IsNull) return;
            try
            {
                var fn = callback as NiL.JS.BaseLibrary.Function;
                if (fn != null)
                {
                    var evtObj = JSValue.Marshal(new { type = eventType, target = target, currentTarget = target, preventDefault = new Func<Arguments, JSValue>(a => JSValue.Undefined), stopPropagation = new Func<Arguments, JSValue>(a => JSValue.Undefined) });
                    fn.Call(evtObj, new Arguments { evtObj });
                }
            }
            catch { }
        }

        // intervals & rAF
        private readonly Dictionary<int, Timer> _intervals = new Dictionary<int, Timer>();
        private int _nextIntervalId;
        private readonly Dictionary<int, Timer> _rafs = new Dictionary<int, Timer>();
        private int _nextRafId;

        // storage (origin-scoped, in-memory)
        private readonly Dictionary<string, Dictionary<string, string>> _localStorageMap =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, string>> _sessionStorageMap =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private readonly object _storageLock = new object();
        private const string LocalStorageFile = "lb_localstorage.txt"; // tab-separated: origin\tkey\tvalue per line
        private int _cbCounter;

        // loader/event firing
        private int _pendingAsyncScripts = 0;
        private volatile bool _domContentLoadedFired = false;
        private volatile bool _windowLoadFired = false;

        // lightweight navigation history
        private readonly List<Uri> _history = new List<Uri>();
        private int _historyIndex = -1;

        // Enqueue a microtask to run after current synchronous work
        private void EnqueueMicrotask(Action a)
        {
            EnqueueMicrotaskInternal(a);
        }
        private void RegisterElementListener(string id, string evt, string fnName)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(evt) || string.IsNullOrWhiteSpace(fnName)) return;
            Dictionary<string, List<string>> byEvt;
            if (!_evtEl.TryGetValue(id, out byEvt))
            {
                byEvt = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                _evtEl[id] = byEvt;
            }
            List<string> list;
            if (!byEvt.TryGetValue(evt, out list)) { list = new List<string>(); byEvt[evt] = list; }
            if (!list.Contains(fnName)) list.Add(fnName);
        }

        private void RemoveElementListener(string id, string evt, string fnName)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(evt) || string.IsNullOrWhiteSpace(fnName))
                return;

            try
            {
                Dictionary<string, List<string>> byEvt;
                if (!_evtEl.TryGetValue(id, out byEvt) || byEvt == null)
                    return;

                List<string> list;
                if (!byEvt.TryGetValue(evt, out list) || list == null)
                    return;

                list.Remove(fnName);

                // cleanup empty collections to keep the structure tidy
                if (list.Count == 0)
                    byEvt.Remove(evt);

                if (byEvt.Count == 0)
                    _evtEl.Remove(id);
            }
            catch
            {
                // swallow errors to match existing style
            }
        }


        /// <summary>
        /// Raise an event on an element (asynchronous, DOM-triggered).
        /// Supports optional value and checked state for form controls.
        /// </summary>
        public void RaiseElementEvent(string id, string evt, string value = null, bool? isChecked = null)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(evt)) return;
            try
            {
                // For JS-0 style inline handlers (avoid C# 7 'out var' for WP8.1 toolchain)
                List<string> list = null; Dictionary<string, List<string>> byEvt = null;
                if (_evtEl.TryGetValue(id, out byEvt) && byEvt != null && byEvt.TryGetValue(evt, out list) && list != null)
                {
                    foreach (var fn in list.ToArray())
                    {
                        try
                        {
                            RunInline(fn + "({ type:'" + evt + "', target:'" + id + "' })", _ctx, evt, id);
                        }
                        catch { /* swallow */ }
                    }
                }
            }
            catch { /* swallow */ }

        }

        /// <summary>
        /// Raise an event on an element synchronously (returns bool for preventDefault check).
        /// Supports additional optional parameters for position/properties.
        /// </summary>
        public bool RaiseElementEventSync(string id, string evt, string value = null, bool? isChecked = null, double? posX = null, double? posY = null)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(evt)) return false;
            _stopPropagationRequested = false;
            try
            {
                // JS-0 style handlers (avoid C# 7 'out var')
                List<string> list = null; Dictionary<string, List<string>> byEvt = null;
                if (_evtEl.TryGetValue(id, out byEvt) && byEvt != null && byEvt.TryGetValue(evt, out list) && list != null)
                {
                    foreach (var fnName in list.ToArray())
                    {
                        try
                        {
                            RunInline(fnName + "({ type:'" + evt + "', target:'" + id + "' })", _ctx, evt, id);
                            if (_stopPropagationRequested) return true;
                        }
                        catch { /* swallow */ }
                    }
                }
            }
            catch { /* swallow */ }
            return _stopPropagationRequested;
        }

        /// <summary>
        /// Raise an event by element ID (alternative entry point).
        /// </summary>
        public void RaiseElementEventById(string id, string evt)
        {
            RaiseElementEvent(id, evt);
        }




        // ---------------- Intervals ----------------
        private int ScheduleInterval(string codeOrFn, int ms, bool isFnName)
        {
            if (ms < 0) ms = 0;
            var id = Interlocked.Increment(ref _nextIntervalId);
            Timer t = null;
            var repaintHost = _host as IJsHostRepaint;
            TimerCallback tick = _ =>
            {
                try
                {
                    Action run = () => {
                        try
                        {
                            if (isFnName) RunInline(codeOrFn + "()", _ctx);
                            else RunInline(codeOrFn, _ctx);
                        }
                        catch { /* swallow */ }
                    };
                    if (repaintHost != null) repaintHost.InvokeOnUiThread(run); else run();
                }
                catch { /* swallow */ }
            };
            t = new Timer(tick, null, ms, ms <= 0 ? 1 : ms);
            lock (_intervals) _intervals[id] = t;
            return id;
        }

        private void ClearInterval(int id)
        {
            lock (_intervals)
            {
                Timer t; if (_intervals.TryGetValue(id, out t))
                {
                    try { t.Dispose(); } catch { /* swallow */ }
                    _intervals.Remove(id);
                }
            }
        }

        // ---------------- rAF (approx 60 FPS using timeout ~16ms) ----------------
        private int RequestAnimationFrame(string fnName)
        {
            var id = Interlocked.Increment(ref _nextRafId);
            var repaintHost = _host as IJsHostRepaint;
            Timer t = new Timer(_ =>
            {
                try
                {
                    Action run = () => { try { RunInline(fnName + "(Date.now&&Date.now()||0)", _ctx); } catch { /* swallow */ } };
                    if (repaintHost != null) repaintHost.InvokeOnUiThread(run); else run();
                }
                catch { /* swallow */ }
                finally { CancelAnimationFrame(id); }
            }, null, 16, Timeout.Infinite);
            lock (_rafs) _rafs[id] = t;
            return id;
        }

        private void CancelAnimationFrame(int id)
        {
            lock (_rafs)
            {
                Timer t; if (_rafs.TryGetValue(id, out t))
                {
                    try { t.Dispose(); } catch { /* swallow */ }
                    _rafs.Remove(id);
                }
            }
        }

        // ---------------- Storage ----------------
        private static string OriginKey(Uri u)
        {
            if (u == null) return "null://";
            var port = u.IsDefaultPort ? "" : (":" + u.Port);
            return (u.Scheme ?? "http") + "://" + (u.Host ?? "localhost") + port;
        }

        private Dictionary<string, string> GetLocalStorageFor(Uri baseUri)
        {
            var key = OriginKey(baseUri);
            lock (_storageLock)
            {
                Dictionary<string, string> bag;
                if (!_localStorageMap.TryGetValue(key, out bag))
                {
                    bag = new Dictionary<string, string>(StringComparer.Ordinal);
                    _localStorageMap[key] = bag;
                }
                return bag;
            }
        }

        private Dictionary<string, string> GetSessionStorageFor(Uri baseUri)
        {
            var key = OriginKey(baseUri);
            lock (_storageLock)
            {
                Dictionary<string, string> bag;
                if (!_sessionStorageMap.TryGetValue(key, out bag))
                {
                    bag = new Dictionary<string, string>(StringComparer.Ordinal);
                    _sessionStorageMap[key] = bag;
                }
                return bag;
            }
        }

        // Persist localStorage to disk (best-effort)
        private async Task SaveLocalStorageAsync()
        {
            try
            {
                var folder = Windows.Storage.ApplicationData.Current.LocalFolder;
                var file = await folder.CreateFileAsync(LocalStorageFile, Windows.Storage.CreationCollisionOption.ReplaceExisting);
                var sb = new StringBuilder();
                lock (_storageLock)
                {
                    foreach (var origin in _localStorageMap)
                    {
                        if (origin.Value == null) continue;
                        foreach (var kv in origin.Value)
                        {
                            var line = (origin.Key ?? "") + "\t" + (kv.Key ?? "") + "\t" + (kv.Value ?? "");
                            sb.AppendLine(line);
                        }
                    }
                }
                await Windows.Storage.FileIO.WriteTextAsync(file, sb.ToString());
            }
            catch { /* swallow */ }
        }

        private async Task RestoreLocalStorageAsync()
        {
            try
            {
                var folder = Windows.Storage.ApplicationData.Current.LocalFolder;
                // Avoid first-chance FileNotFound by probing existence first
                var item = await folder.GetItemAsync(LocalStorageFile) as Windows.Storage.StorageFile;
                if (item == null) return;
                var text = await Windows.Storage.FileIO.ReadTextAsync(item);
                if (string.IsNullOrWhiteSpace(text)) return;
                lock (_storageLock)
                {
                    foreach (var ln in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        try
                        {
                            var parts = ln.Split('\t');
                            if (parts.Length < 3) continue;
                            Dictionary<string, string> bag;
                            if (!_localStorageMap.TryGetValue(parts[0], out bag) || bag == null)
                            {
                                bag = new Dictionary<string, string>(StringComparer.Ordinal);
                                _localStorageMap[parts[0]] = bag;
                            }
                            bag[parts[1]] = parts[2];
                        }
                        catch { /* swallow */ }
                    }
                }
            }
            catch { /* swallow */ }
        }

        // Load persisted bad-hash store from local folder (JSON format).
        private async Task LoadBadHashStoreAsync()
        {
            try
            {
                await _badHashStoreLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    // First try the app LocalFolder (default behavior)
                    var folder = Windows.Storage.ApplicationData.Current.LocalFolder;
                    Windows.Storage.StorageFile file = null;
                    try { file = await folder.GetItemAsync(BadHashStoreFile) as Windows.Storage.StorageFile; } catch { file = null; }

                    // If not found in LocalFolder, attempt to read a copy from Pictures/MediaExplorer
                    if (file == null)
                    {
                        try
                        {
                            var pics = Windows.Storage.KnownFolders.PicturesLibrary;
                            var picsFolder = await pics.GetFolderAsync("MediaExplorer").AsTask().ConfigureAwait(false);
                            if (picsFolder != null)
                            {
                                try { file = await picsFolder.GetFileAsync(BadHashStoreFile).AsTask().ConfigureAwait(false); } catch { file = null; }
                            }
                        }
                        catch { /* swallow - fallback only */ }
                    }

                    if (file == null) return;

                    var text = await Windows.Storage.FileIO.ReadTextAsync(file).AsTask().ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(text)) return;
                    List<BadHashRecord> list = null;
                    try { list = JsonConvert.DeserializeObject<List<BadHashRecord>>(text); } catch { list = null; }
                    if (list == null) return;
                    foreach (var r in list)
                    {
                        try
                        {
                            if (r == null || string.IsNullOrWhiteSpace(r.Hash)) continue;
                            if (!string.IsNullOrWhiteSpace(r.Preview) && r.Preview.Length > 160) r.Preview = r.Preview.Substring(0, 160);
                            _badHashRecords[r.Hash] = r;
                        }
                        catch { /* swallow */ }
                    }
                }
                catch { /* swallow */ }
            }
            finally { try { _badHashStoreLock.Release(); } catch { } }
        }

        // Save current bad-hash store to local folder (JSON format). Best-effort; called async/fire-and-forget.
        private async Task SaveBadHashStoreAsync()
        {
            try
            {
                await _badHashStoreLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    // Persist into app LocalFolder first (default)
                    var folder = Windows.Storage.ApplicationData.Current.LocalFolder;
                    var file = await folder.CreateFileAsync(BadHashStoreFile, Windows.Storage.CreationCollisionOption.ReplaceExisting).AsTask().ConfigureAwait(false);
                    var records = _badHashRecords.Values.OrderByDescending(r => r.Count).ToList();
                    var json = JsonConvert.SerializeObject(records, Formatting.Indented);
                    await Windows.Storage.FileIO.WriteTextAsync(file, json).AsTask().ConfigureAwait(false);

                    // Additionally, try to write a copy into Pictures/MediaExplorer when available.
                    // This is helpful for out-of-process test runners which can access the Pictures library.
                    try
                    {
                        var pics = Windows.Storage.KnownFolders.PicturesLibrary;
                        var picsFolder = await pics.CreateFolderAsync("MediaExplorer", Windows.Storage.CreationCollisionOption.OpenIfExists).AsTask().ConfigureAwait(false);
                        if (picsFolder != null)
                        {
                            try
                            {
                                var picsFile = await picsFolder.CreateFileAsync(BadHashStoreFile, Windows.Storage.CreationCollisionOption.ReplaceExisting).AsTask().ConfigureAwait(false);
                                if (picsFile != null) await Windows.Storage.FileIO.WriteTextAsync(picsFile, json).AsTask().ConfigureAwait(false);
                            }
                            catch { /* swallow copy failure */ }
                        }
                    }
                    catch { /* swallow - pictures library may be unavailable */ }
                }
                catch { /* swallow */ }
            }
            finally { try { _badHashStoreLock.Release(); } catch { } }
        }

        // Coalesced save: schedule a debounced save; multiple calls within debounce window coalesce into one write.
        private void ScheduleSaveBadHashStoreDebounced()
        {
            try
            {
                lock (_badHashSaveTimerLock)
                {
                    if (_badHashSaveTimer != null)
                    {
                        try { _badHashSaveTimer.Change(BadHashSaveDebounceMs, System.Threading.Timeout.Infinite); } catch { }
                    }
                    else
                    {
                        _badHashSaveTimer = new System.Threading.Timer(_ =>
                        {
                            try { var _r = SaveBadHashStoreAsync(); } catch { }
                            try { lock (_badHashSaveTimerLock) { _badHashSaveTimer.Dispose(); _badHashSaveTimer = null; } } catch { }
                        }, null, BadHashSaveDebounceMs, System.Threading.Timeout.Infinite);
                    }
                }
            }
            catch { /* swallow */ }
        }

        // Dump top-N bad-hash summary to Debug and host status (best-effort)
        public void DumpBadHashSummary(int topN = 10)
        {
            try
            {
                var list = _badHashRecords.Values.OrderByDescending(r => r.Count).Take(topN).ToList();
                try { System.Diagnostics.Debug.WriteLine("[DIAG:JS-SUMMARY] bad-hash-top=" + list.Count); } catch { }
                for (int i = 0; i < list.Count; i++)
                {
                    var r = list[i];
                    var prev = r.Preview ?? "";
                    prev = prev.Replace("\r", " ").Replace("\n", " ");
                    if (prev.Length > 80) prev = prev.Substring(0, 80) + "…";
                    try { System.Diagnostics.Debug.WriteLine("[DIAG:JS-SUMMARY] #" + (i + 1) + " hash=" + r.Hash + " count=" + r.Count + " first=" + r.FirstSeen.ToString("o") + " last=" + r.LastSeen.ToString("o") + " preview=\"" + prev + "\""); } catch { }
                }
            }
            catch { /* swallow */ }
        }

        // Export current bad-hash store as JSON string (best-effort)
        public async Task<string> GetBadHashStoreJsonAsync()
        {
            try
            {
                await _badHashStoreLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    var records = _badHashRecords.Values.OrderByDescending(r => r.Count).ToList();
                    return JsonConvert.SerializeObject(records, Formatting.Indented);
                }
                catch { return null; }
                finally { try { _badHashStoreLock.Release(); } catch { } }
            }
            catch { return null; }
        }

        // Restore bad-hash store from a JSON string (best-effort) and persist.
        public async Task<bool> RestoreBadHashStoreFromJsonAsync(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                List<BadHashRecord> list = null;
                try { list = JsonConvert.DeserializeObject<List<BadHashRecord>>(json); } catch { list = null; }
                if (list == null) return false;
                await _badHashStoreLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    _badHashRecords.Clear();
                    foreach (var r in list)
                    {
                        if (r == null || string.IsNullOrWhiteSpace(r.Hash)) continue;
                        if (!string.IsNullOrWhiteSpace(r.Preview) && r.Preview.Length > 160) r.Preview = r.Preview.Substring(0, 160);
                        _badHashRecords[r.Hash] = r;
                    }
                }
                finally { try { _badHashStoreLock.Release(); } catch { } }
                // Persist
                try { ScheduleSaveBadHashStoreDebounced(); } catch { }
                return true;
            }
            catch { return false; }
        }

        // Clear bad-hash store (in-memory + remove persisted file)
        public async Task ClearBadHashStoreAsync()
        {
            try
            {
                await _badHashStoreLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    _badHashRecords.Clear();
                    try
                    {
                        var folder = Windows.Storage.ApplicationData.Current.LocalFolder;
                        var item = await folder.TryGetItemAsync(BadHashStoreFile) as Windows.Storage.StorageFile;
                        if (item != null) await item.DeleteAsync();

                        // Also attempt to remove copy from Pictures/MediaExplorer if present
                        try
                        {
                            var pics = Windows.Storage.KnownFolders.PicturesLibrary;
                            var picsFolder = await pics.GetFolderAsync("MediaExplorer").AsTask().ConfigureAwait(false);
                            if (picsFolder != null)
                            {
                                try
                                {
                                    var picsItem = await picsFolder.TryGetItemAsync(BadHashStoreFile) as Windows.Storage.StorageFile;
                                    if (picsItem != null) await picsItem.DeleteAsync();
                                }
                                catch { /* swallow */ }
                            }
                        }
                        catch { /* swallow */ }
                    }
                    catch { }
                }
                finally { try { _badHashStoreLock.Release(); } catch { } }
            }
            catch { /* swallow */ }
        }

        // ---------------- Cookies (best-effort via CookieBridge) ----------------
        private void SetCookieString(Uri scope, string cookieString)
        {
            if (!SandboxAllows(SandboxFeature.Storage, "document.cookie set")) return;
            try
            {
                if (CookieBridge == null || scope == null || string.IsNullOrWhiteSpace(cookieString)) return;
                var jar = CookieBridge(scope);
                if (jar == null) return;
                jar.SetCookies(scope, cookieString);
            }
            catch { /* swallow */ }
        }

        private string GetCookieString(Uri scope)
        {
            if (!SandboxAllows(SandboxFeature.Storage, "document.cookie get")) return string.Empty;
            try
            {
                if (CookieBridge == null || scope == null) return "";
                var jar = CookieBridge(scope);
                if (jar == null) return "";
                var coll = jar.GetCookies(scope);
                if (coll == null || coll.Count == 0) return "";

                var sb = new StringBuilder();
                bool first = true;

                foreach (System.Net.Cookie cookie in coll)
                {
                    if (!first) sb.Append("; ");
                    first = false;

                    sb.Append(cookie.Name)
                      .Append('=')
                      .Append(cookie.Value ?? string.Empty);
                }

                return sb.ToString();

            }
            catch { return ""; }
        }

        // ---------------- History ----------------
        private static string BaseWithoutFragment(Uri u)
        {
            try
            {
                if (u == null) return null;
                return u.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped);
            }
            catch { return u != null ? ((u.Scheme ?? "") + "://" + (u.Host ?? "") + (u.PathAndQuery ?? "")) : null; }
        }

        private void HistoryPush(Uri u)
        {
            if (u == null) return;
            if (!SandboxAllows(SandboxFeature.Navigation, "history.pushState -> " + (u?.AbsoluteUri ?? ""))) return;
            Uri prev = null; if (_historyIndex >= 0 && _historyIndex < _history.Count) prev = _history[_historyIndex];
            if (_historyIndex >= 0 && _historyIndex < _history.Count - 1)
                _history.RemoveRange(_historyIndex + 1, _history.Count - (_historyIndex + 1));
            _history.Add(u);
            _historyIndex = _history.Count - 1;
            try { if (prev != null && string.Equals(BaseWithoutFragment(prev), BaseWithoutFragment(u), StringComparison.OrdinalIgnoreCase) && !string.Equals(prev.Fragment ?? "", u.Fragment ?? "", StringComparison.Ordinal)) FireWindowEvent("hashchange"); } catch { /* swallow */ }
        }

        private void HistoryReplace(Uri u)
        {
            if (u == null) return;
            if (!SandboxAllows(SandboxFeature.Navigation, "history.replaceState -> " + (u?.AbsoluteUri ?? ""))) return;
            Uri prev = null; if (_historyIndex >= 0 && _historyIndex < _history.Count) prev = _history[_historyIndex];
            if (_historyIndex < 0) { _history.Add(u); _historyIndex = _history.Count - 1; }
            else _history[_historyIndex] = u;
            try { if (prev != null && string.Equals(BaseWithoutFragment(prev), BaseWithoutFragment(u), StringComparison.OrdinalIgnoreCase) && !string.Equals(prev.Fragment ?? "", u.Fragment ?? "", StringComparison.Ordinal)) FireWindowEvent("hashchange"); } catch { /* swallow */ }
        }

        private void HistoryGo(int delta)
        {
            var target = _historyIndex + delta;
            if (target < 0 || target >= _history.Count) return;
            if (!SandboxAllows(SandboxFeature.Navigation, "history.go(" + delta + ")")) return;
            _historyIndex = target;
            try { _host.Navigate(_history[_historyIndex]); } catch { /* swallow */ }
            FireWindowEvent("popstate");
        }
        // Extracts the first identifier token from a JS line (starting at any non-whitespace position)
        private static string GetFirstToken(string line)
        {
            int pos = 0;
            while (pos < line.Length && char.IsWhiteSpace(line[pos])) pos++;
            if (pos >= line.Length || (!char.IsLetter(line[pos]) && line[pos] != '_' && line[pos] != '$')) return null;
            int start = pos++;
            while (pos < line.Length && (char.IsLetterOrDigit(line[pos]) || line[pos] == '_' || line[pos] == '$')) pos++;
            return line.Substring(start, pos - start);
        }

        // Fast-path dispatch for JS-0 patterns — tries only the subset relevant to the first token.
        // Returns true if the line was handled (caller should continue to next line).
        private bool TryPatternsByToken(string line, string token, JsContext ctx)
        {
            switch (token)
            {
                case "setTimeout":
                    return TrySetTimeoutPatterns(line, ctx);
                case "clearTimeout":
                    return TryClearTimeoutPatterns(line);
                case "setInterval":
                    return TrySetIntervalPatterns(line, ctx);
                case "clearInterval":
                    return TryClearIntervalPatterns(line);
                case "event":
                    return TryEventPatterns(line);
                case "console":
                    return TryConsolePatterns(line);
                case "location":
                    return TryLocationPatterns(line, ctx);
                case "return":
                    return false;
                case "alert":
                    return TryAlertPatterns(line);
                case "void":
                    return TryVoidPatterns(line);
                case "fetch":
                    return TryFetchPatterns(line, ctx);
                case "fetchText":
                    return TryFetchTextPatterns(line, ctx);
                case "requestAnimationFrame":
                    return TryRafPatterns(line);
                case "cancelAnimationFrame":
                    return TryCancelRafPatterns(line);
                case "navigator":
                    return TryNavigatorPatterns(line);
                case "Promise":
                    return TryPromisePatterns(line, ctx);
                case "new":
                    return TryNewPatterns(line, ctx);
                case "__xhr_new":
                case "__xhr_open":
                case "__xhr_setRequestHeader":
                case "__xhr_send":
                case "__xhr_deliver":
                    return TryXhrPatterns(line, ctx);
                case "__getCookie":
                    return TryGetCookiePatterns(line, ctx);
                case "__hostResolveToken":
                    return TryHostResolveToken(line);
                case "__hostResolveText":
                    return TryHostResolveText(line, ctx);
                case "__enqueueMicrotask":
                    return TryEnqueueMicrotask(line, ctx);
                case "document":
                    return TryDocumentPatterns(line, ctx);
                case "window":
                    return TryWindowPatterns(line, ctx);
                default:
                    return false;
            }
        }

        // --- Fast-path Try* handlers for common JS-0 tokens ---

        private bool TrySetTimeoutPatterns(string line, JsContext ctx)
        {
            var mTO = RxSetTimeout.Match(line);
            if (mTO.Success)
            {
                var code = mTO.Groups["code"].Value;
                int ms; if (!int.TryParse(mTO.Groups["ms"].Value, out ms)) ms = 0;
                var id = ScheduleTimeout(code, ms);
                try { _host.SetStatus("setTimeout id=" + id); } catch { /* swallow */ }
                return true;
            }
            var mTO2 = RxSetTimeoutFn.Match(line);
            if (mTO2.Success)
            {
                int ms2 = 0; int.TryParse(mTO2.Groups["ms"].Value, out ms2);
                var fn = mTO2.Groups["fn"].Value;
                var id2 = ScheduleTimeout(fn + "()", ms2);
                try { _host.SetStatus("setTimeout id=" + id2); } catch { /* swallow */ }
                return true;
            }
            return false;
        }

        private bool TryClearTimeoutPatterns(string line)
        {
            var mClear = RxClearTimeout.Match(line);
            if (mClear.Success) { int id; if (int.TryParse(mClear.Groups["id"].Value, out id)) ClearTimeout(id); return true; }
            return false;
        }

        private bool TrySetIntervalPatterns(string line, JsContext ctx)
        {
            var mSI = RxSetInterval.Match(line);
            if (mSI.Success)
            {
                var code = mSI.Groups["code"].Value; int ms; if (!int.TryParse(mSI.Groups["ms"].Value, out ms)) ms = 0;
                var id = ScheduleInterval(code, ms);
                try { _host.SetStatus("setInterval id=" + id); } catch { /* swallow */ }
                return true;
            }
            var mSIF = RxSetIntervalFn.Match(line);
            if (mSIF.Success)
            {
                int ms = 0; int.TryParse(mSIF.Groups["ms"].Value, out ms);
                var fn = mSIF.Groups["fn"].Value;
                var id = ScheduleInterval(fn + "()", ms);
                try { _host.SetStatus("setInterval id=" + id); } catch { /* swallow */ }
                return true;
            }
            return false;
        }

        private bool TryClearIntervalPatterns(string line)
        {
            var mCI = RxClearInterval.Match(line);
            if (mCI.Success) { int id; if (int.TryParse(mCI.Groups["id"].Value, out id)) ClearTimeout(id); return true; }
            return false;
        }

        private bool TryConsolePatterns(string line)
        {
            var mLog = RxConsoleLog.Match(line);
            if (mLog.Success) { try { _host.SetStatus(mLog.Groups["msg"].Value ?? ""); } catch { /* swallow */ } return true; }
            return false;
        }

        private bool TryLocationPatterns(string line, JsContext ctx)
        {
            var mHref = RxHrefAssign.Match(line);
            if (mHref.Success) { Navigate(mHref.Groups["url"].Value); return true; }
            var mAssign = RxAssignCall.Match(line);
            if (mAssign.Success) { Navigate(mAssign.Groups["url"].Value); return true; }
            var mReplace = RxReplaceCall.Match(line);
            if (mReplace.Success) { Navigate(mReplace.Groups["url"].Value); return true; }
            if (RxLocationReload.IsMatch(line)) { try { if (ctx?.BaseUri != null) _host.Navigate(ctx.BaseUri); else RequestRepaint(); } catch { /* swallow */ } return true; }
            return false;
        }

        private bool TryAlertPatterns(string line)
        {
            var mAlert = RxAlert.Match(line);
            if (mAlert.Success) { _host.SetStatus(mAlert.Groups["msg"].Value ?? ""); return true; }
            return false;
        }

        private bool TryVoidPatterns(string line)
        {
            if (RxVoidZero.IsMatch(line)) return true;
            return false;
        }

        private bool TryWindowPatterns(string line, JsContext ctx)
        {
            var mW = RxWindowLoc.Match(line);
            if (mW.Success) { Navigate(mW.Groups["url"].Value); return true; }
            var mOpen = RxWindowOpen.Match(line);
            if (mOpen.Success) { Navigate(mOpen.Groups["url"].Value); return true; }
            if (RxLocationReload.IsMatch(line)) { try { if (ctx?.BaseUri != null) _host.Navigate(ctx.BaseUri); else RequestRepaint(); } catch { /* swallow */ } return true; }
            return false;
        }

        private bool TryDocumentPatterns(string line, JsContext ctx)
        {
            var mHtmlAssign = System.Text.RegularExpressions.Regex.Match(line,
                @"^\s*document\s*\.\s*getElementById\s*\(\s*(['""])(?<id>.+?)\1\s*\)\s*\.\s*innerHTML\s*=\s*(['""])(?<val>.*?)\3\s*;?\s*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (mHtmlAssign.Success && _domRoot != null)
            {
                try
                {
                    var id = mHtmlAssign.Groups["id"].Value;
                    var val = mHtmlAssign.Groups["val"].Value;
                    var doc = new JsDocument(this, _domRoot);
                    var el = doc.getElementById(id) as JsDomElement;
                    if (el != null) { el.innerHTML = val; RequestRepaint(); }
                }
                catch { /* swallow */ }
                return true;
            }
            var mSetHtml = System.Text.RegularExpressions.Regex.Match(line,
                @"^\s*document\s*\.\s*getElementById\s*\(\s*(['""])(?<id>.+?)\1\s*\)\s*\.\s*setInnerHTML\s*\(\s*(['""])(?<val>.*?)\3\s*,\s*(?<flag>true|false)\s*\)\s*;?\s*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (mSetHtml.Success && _domRoot != null)
            {
                try
                {
                    var id = mSetHtml.Groups["id"].Value;
                    var val = mSetHtml.Groups["val"].Value;
                    var flag = string.Equals(mSetHtml.Groups["flag"].Value, "true", StringComparison.OrdinalIgnoreCase);
                    var doc = new JsDocument(this, _domRoot);
                    var el = doc.getElementById(id) as JsDomElement;
                    if (el != null) { el.setInnerHTML(val, flag); RequestRepaint(); }
                }
                catch { /* swallow */ }
                return true;
            }
            var mClick = System.Text.RegularExpressions.Regex.Match(line,
                @"^\s*document\s*\.\s*getElementById\s*\(\s*(['""])(?<id>[^'""]+)\1\s*\)\s*\.\s*click\s*\(\s*\)\s*;?\s*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (mClick.Success)
            {
                try { RaiseElementEvent(mClick.Groups["id"].Value, "click"); } catch { /* swallow */ }
                return true;
            }
            var mSetText = RxGetByIdInnerTextAssign.Match(line);
            if (mSetText.Success)
            {
                var id = mSetText.Groups["id"].Value;
                var val = mSetText.Groups["val"].Value;
                if (_domRoot != null)
                {
                    var doc = new JsDocument(this, _domRoot);
                    var el = doc.getElementById(id) as JsDomElement;
                    if (el != null) el.innerText = val;
                }
                return true;
            }
            var mSetAttr = RxGetByIdSetAttr.Match(line);
            if (mSetAttr.Success)
            {
                var id = mSetAttr.Groups["id"].Value;
                var an = mSetAttr.Groups["an"].Value;
                var av = mSetAttr.Groups["av"].Value;
                if (_domRoot != null)
                {
                    var doc = new JsDocument(this, _domRoot);
                    var el = doc.getElementById(id) as JsDomElement;
                    if (el != null) el.setAttribute(an, av);
                }
                return true;
            }
            var mStyle = RxElStyleSet.Match(line);
            if (mStyle.Success)
            {
                var id = mStyle.Groups["id"].Value;
                var prop = (mStyle.Groups["prop"].Value ?? "").Trim().ToLowerInvariant();
                var val = mStyle.Groups["val"].Value ?? "";
                TryUpdateInlineStyle(id, prop, val);
                return true;
            }
            var mCls = RxElClassListOp.Match(line);
            if (mCls.Success)
            {
                var id = mCls.Groups["id"].Value; var op = mCls.Groups["op"].Value.ToLowerInvariant(); var cls = mCls.Groups["cls"].Value;
                TryUpdateClassList(id, op, cls);
                return true;
            }
            var mOn = RxElOnAssign.Match(line);
            if (mOn.Success)
            {
                var id = mOn.Groups["id"].Value; var evt = mOn.Groups["evt"].Value.ToLowerInvariant(); var fn = mOn.Groups["fn"].Value;
                var evtName = evt == "click" ? "click" : evt == "input" ? "input" : evt == "change" ? "change" : evt;
                RegisterElementListener(id, evtName, fn);
                return true;
            }
            return false;
        }

        private bool TryEventPatterns(string line)
        {
            if (RxEventStopPropagation.IsMatch(line)) { _stopPropagationRequested = true; return true; }
            return false;
        }

        private bool TryPromisePatterns(string line, JsContext ctx)
        {
            var mPThenFn = RxPromiseThenFunc.Match(line);
            if (mPThenFn.Success)
            {
                var fn = mPThenFn.Groups["fn"].Value;
                EnqueueMicrotask(() => { try { RunInline(fn + "()", _ctx); } catch { /* swallow */ } });
                return true;
            }
            if (line.StartsWith("Promise.resolve().then(", StringComparison.Ordinal))
            {
                try
                {
                    var inside = line.Substring("Promise.resolve().then(".Length).Trim();
                    if (inside.EndsWith(")")) inside = inside.Substring(0, inside.Length - 1).Trim();
                    if (inside.StartsWith("\"") && inside.EndsWith("\""))
                    {
                        var code = inside.Substring(1, inside.Length - 2);
                        EnqueueMicrotask(() => { try { RunInline(code, ctx); } catch { /* swallow */ } });
                    }
                }
                catch { /* swallow */ }
                return true;
            }
            return false;
        }

        private bool TryNewPatterns(string line, JsContext ctx)
        {
            return false;
        }

        private bool TryNavigatorPatterns(string line)
        {
            if (Regex.IsMatch(line, @"^\s*navigator\s*\.\s*serviceWorker\s*\.\s*register\s*\(", RegexOptions.IgnoreCase))
            {
                try { _host.SetStatus("serviceWorker.register: no-op"); } catch { /* swallow */ }
                return true;
            }
            return false;
        }

        private bool TryFetchPatterns(string line, JsContext ctx)
        {
            var mFThen = RxFetchThen.Match(line);
            if (mFThen.Success)
            {
                var url = mFThen.Groups["url"].Value;
                var code = mFThen.Groups["code"].Value;
                var resolved = Resolve(ctx?.BaseUri ?? null, url);
                if (resolved != null)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            string txt = null;
                            if (FetchOverride != null) txt = await FetchOverride(resolved).ConfigureAwait(false);
                            else txt = await FetchScriptStringAsync(resolved, ctx?.BaseUri).ConfigureAwait(false);
                            var exec = code;
                            if (!string.IsNullOrEmpty(exec) && exec.Contains("%s")) exec = exec.Replace("%s", txt ?? "");
                            try { RunInline(exec, _ctx); } catch { /* swallow */ }
                        }
                        catch { /* swallow */ }
                    });
                }
                return true;
            }
            var mFThenFn = RxFetchThenFunc.Match(line);
            if (mFThenFn.Success)
            {
                var url = mFThenFn.Groups["url"].Value;
                var fn = mFThenFn.Groups["fn"].Value;
                var resolved = Resolve(ctx?.BaseUri ?? null, url);
                if (resolved != null)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            string txt = null;
                            if (FetchOverride != null) txt = await FetchOverride(resolved).ConfigureAwait(false);
                            else txt = await FetchScriptStringAsync(resolved, ctx?.BaseUri).ConfigureAwait(false);
                            var sr = new SimpleResponse(txt);
                            var respExpr = sr.EmitResponseObject(RegisterResponseBody, () => _inlineThreshold);
                            EnqueueMicrotask(() => { try { RunInline(fn + "(" + respExpr + ")", _ctx); } catch { /* swallow */ } });
                        }
                        catch { /* swallow */ }
                    });
                }
                return true;
            }
            return false;
        }

        private bool TryFetchTextPatterns(string line, JsContext ctx)
        {
            var mFetch = RxFetchText.Match(line);
            if (mFetch.Success)
            {
                var url = mFetch.Groups["url"].Value;
                var id = mFetch.Groups["id"].Value;
                var resolved = Resolve(ctx?.BaseUri ?? null, url);
                if (resolved != null && _domRoot != null)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            string txt;
                            if (FetchOverride != null) txt = await FetchOverride(resolved).ConfigureAwait(false);
                            else txt = await _http.GetStringAsync(resolved).ConfigureAwait(false);
                            var doc = new JsDocument(this, _domRoot);
                            var el = doc.getElementById(id) as JsDomElement;
                            if (el != null) { el.innerText = txt; RequestRepaint(); }
                        }
                        catch (Exception ex) { try { _host.SetStatus("fetchText failed: " + ex.Message); } catch { /* swallow */ } }
                    });
                }
                return true;
            }
            return false;
        }

        private bool TryRafPatterns(string line)
        {
            var mRaf = Regex.Match(line, @"^\s*requestAnimationFrame\s*\(\s*(?<fn>[A-Za-z_$][A-Za-z0-9_$]*)\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mRaf.Success) { var id = RequestAnimationFrame(mRaf.Groups["fn"].Value); try { _host.SetStatus("rAF id=" + id); } catch { /* swallow */ } return true; }
            return false;
        }

        private bool TryCancelRafPatterns(string line)
        {
            var mCRaf = Regex.Match(line, @"^\s*cancelAnimationFrame\s*\(\s*(?<id>\d+)\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mCRaf.Success) { int id; if (int.TryParse(mCRaf.Groups["id"].Value, out id)) CancelAnimationFrame(id); return true; }
            return false;
        }

        private bool TryXhrPatterns(string line, JsContext ctx)
        {
            var mXnew = RxXhrNew.Match(line);
            if (mXnew.Success) { XhrNew(mXnew.Groups["id"].Value); return true; }
            var mXopen = RxXhrOpen.Match(line);
            if (mXopen.Success) { XhrOpen(mXopen.Groups["id"].Value, mXopen.Groups["m"].Value, mXopen.Groups["url"].Value, ctx); return true; }
            var mXhdr = RxXhrSetHdr.Match(line);
            if (mXhdr.Success) { XhrSetHeader(mXhdr.Groups["id"].Value, mXhdr.Groups["n"].Value, mXhdr.Groups["v"].Value); return true; }
            var mXsend = RxXhrSend.Match(line);
            if (mXsend.Success)
            {
                var id = mXsend.Groups["id"].Value;
                var body = mXsend.Groups["body"].Success ? mXsend.Groups["body"].Value : null;
                var onload = mXsend.Groups["onload"].Value;
                var onerr = mXsend.Groups["onerror"].Success ? mXsend.Groups["onerror"].Value : null;
                XhrSend(id, body, onload, onerr, ctx);
                return true;
            }
            return false;
        }

        private bool TryGetCookiePatterns(string line, JsContext ctx)
        {
            var mCget = Regex.Match(line, @"^\s*__getCookie\s*\(\s*(?<fn>[A-Za-z_$][A-Za-z0-9_$]*)\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mCget.Success)
            {
                var s = GetCookieString(ctx?.BaseUri ?? _ctx?.BaseUri);
                var esc = JsEscape(s ?? "", '\'');
                EnqueueMicrotask(() => { try { RunInline(mCget.Groups["fn"].Value + "('" + esc + "')", _ctx); } catch { /* swallow */ } });
                return true;
            }
            return false;
        }

        private bool TryHostResolveToken(string line)
        {
            if (line.StartsWith("__hostResolveToken(", StringComparison.Ordinal))
            {
                try
                {
                    var inside = line.Substring("__hostResolveToken(".Length).Trim();
                    if (inside.EndsWith(")")) inside = inside.Substring(0, inside.Length - 1).Trim();
                    var comma = inside.IndexOf(',');
                    if (comma > 0)
                    {
                        var tokenPart = inside.Substring(0, comma).Trim();
                        var fnPart = inside.Substring(comma + 1).Trim();
                        if ((tokenPart.StartsWith("\"") && tokenPart.EndsWith("\"")) || (tokenPart.StartsWith("'") && tokenPart.EndsWith("'")))
                            tokenPart = tokenPart.Substring(1, tokenPart.Length - 2);
                        if ((fnPart.StartsWith("\"") && fnPart.EndsWith("\"")) || (fnPart.StartsWith("'") && fnPart.EndsWith("'")))
                            fnPart = fnPart.Substring(1, fnPart.Length - 2);
                        string body = null;
                        lock (_responseLock)
                        {
                            ResponseEntry pair;
                            if (_responseRegistry.TryGetValue(tokenPart, out pair))
                            {
                                var now = DateTime.UtcNow;
                                if (now - pair.Ts > _responseTtl)
                                {
                                    _responseRegistry.Remove(tokenPart);
                                    _responseLru.Remove(tokenPart);
                                }
                                else
                                {
                                    body = pair.Body;
                                    _responseRegistry[tokenPart] = new ResponseEntry(pair.Body, now);
                                    _responseLru.Remove(tokenPart);
                                    _responseLru.AddFirst(tokenPart);
                                }
                            }
                        }
                        if (body != null)
                        {
                            if (fnPart.EndsWith(".json_callback", StringComparison.Ordinal))
                            {
                                try
                                {
                                    var parsed = Windows.Data.Json.JsonValue.Parse(body) as Windows.Data.Json.IJsonValue;
                                    var literal = JsonToJsLiteral(parsed);
                                    EnqueueMicrotask(() => { try { RunInline(fnPart.Substring(0, fnPart.Length - ".json_callback".Length) + "(" + literal + ")", _ctx); } catch { /* swallow */ } });
                                }
                                catch
                                {
                                    var escErr = JsEscape(body, '\'');
                                    EnqueueMicrotask(() => { try { RunInline(fnPart + "('" + escErr + "')", _ctx); } catch { /* swallow */ } });
                                }
                            }
                            else
                            {
                                var esc = JsEscape(body, '\'');
                                EnqueueMicrotask(() => { try { RunInline(fnPart + "('" + esc + "')", _ctx); } catch { /* swallow */ } });
                            }
                        }
                    }
                }
                catch { /* swallow */ }
                return true;
            }
            return false;
        }

        private bool TryHostResolveText(string line, JsContext ctx)
        {
            if (line.StartsWith("__hostResolveText(", StringComparison.Ordinal))
            {
                try
                {
                    var inside = line.Substring("__hostResolveText(".Length).Trim();
                    if (inside.EndsWith(")")) inside = inside.Substring(0, inside.Length - 1).Trim();
                    var comma = inside.LastIndexOf(',');
                    if (comma > 0)
                    {
                        var bodyPart = inside.Substring(0, comma).Trim();
                        var fnPart = inside.Substring(comma + 1).Trim();
                        if ((fnPart.StartsWith("\"") && fnPart.EndsWith("\"")) || (fnPart.StartsWith("'") && fnPart.EndsWith("'")))
                            fnPart = fnPart.Substring(1, fnPart.Length - 2);
                        string body = bodyPart;
                        if ((body.StartsWith("\"") && body.EndsWith("\"")) || (body.StartsWith("'") && body.EndsWith("'")))
                            body = body.Substring(1, body.Length - 2);
                        EnqueueMicrotask(() => { try { RunInline(fnPart + "('" + body + "')", _ctx); } catch { /* swallow */ } });
                    }
                }
                catch { /* swallow */ }
                return true;
            }
            return false;
        }

        private bool TryEnqueueMicrotask(string line, JsContext ctx)
        {
            if (line.StartsWith("__enqueueMicrotask(", StringComparison.Ordinal))
            {
                var arg = line.Substring("__enqueueMicrotask(".Length).Trim();
                if (arg.EndsWith(")")) arg = arg.Substring(0, arg.Length - 1).Trim();
                if (arg.StartsWith("\"") && arg.EndsWith("\"")) arg = arg.Substring(1, arg.Length - 2);
                var codeToRun = arg;
                EnqueueMicrotask(() => { try { RunInline(codeToRun, ctx); } catch { /* swallow */ } });
                return true;
            }
            return false;
        }

        // Handles Phase 1/2/3 builtins; returns true if the line was handled.
        private bool HandlePhase123Builtins(string line, JsContext ctx)
        {
            // ---------- addEventListener / removeEventListener ----------
            var mAddEvt = Regex.Match(line, @"^(?<tgt>document|window)\s*\.addEventListener\s*\(\s*['""](?<evt>[^'""]+)['""]\s*,\s*(?<fn>[A-Za-z_$][A-Za-z0-9_$]*)\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mAddEvt.Success)
            {
                var tgt = mAddEvt.Groups["tgt"].Value.ToLowerInvariant();
                var evt = mAddEvt.Groups["evt"].Value;
                var fn = mAddEvt.Groups["fn"].Value;
                if (tgt == "document") RegisterListener(_evtDoc, evt, fn); else RegisterListener(_evtWin, evt, fn);
                return true;
            }
            var mRemEvt = Regex.Match(line, @"^(?<tgt>document|window)\s*\.removeEventListener\s*\(\s*['""](?<evt>[^'""]+)['""]\s*,\s*(?<fn>[A-Za-z_$][A-Za-z0-9_$]*)\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mRemEvt.Success)
            {
                var tgt = mRemEvt.Groups["tgt"].Value.ToLowerInvariant();
                var evt = mRemEvt.Groups["evt"].Value;
                var fn = mRemEvt.Groups["fn"].Value;
                if (tgt == "document") RemoveListener(_evtDoc, evt, fn); else RemoveListener(_evtWin, evt, fn);
                return true;
            }

            // ---------- setInterval / clearInterval ----------
            var mSI = Regex.Match(line, @"^\s*setInterval\s*\(\s*(?:['""](?<code>.*?)['""]|(?<fn>[A-Za-z_$][A-Za-z0-9_$]*))\s*,\s*(?<ms>\d+)\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mSI.Success)
            {
                var ms = 0; int.TryParse(mSI.Groups["ms"].Value, out ms);
                var code = mSI.Groups["code"].Success ? mSI.Groups["code"].Value : null;
                var fn = mSI.Groups["fn"].Success ? mSI.Groups["fn"].Value : null;
                var id = ScheduleInterval(code ?? fn, ms, isFnName: fn != null);
                try { _host.SetStatus("setInterval id=" + id); } catch { /* swallow */ }
                return true;
            }
            var mCI = Regex.Match(line, @"^\s*clearInterval\s*\(\s*(?<id>\d+)\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mCI.Success) { int id; if (int.TryParse(mCI.Groups["id"].Value, out id)) ClearInterval(id); return true; }

            // ---------- requestAnimationFrame / cancelAnimationFrame ----------
            var mRaf = Regex.Match(line, @"^\s*requestAnimationFrame\s*\(\s*(?<fn>[A-Za-z_$][A-Za-z0-9_$]*)\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mRaf.Success) { var id = RequestAnimationFrame(mRaf.Groups["fn"].Value); try { _host.SetStatus("rAF id=" + id); } catch { /* swallow */ } return true; }
            var mCRaf = Regex.Match(line, @"^\s*cancelAnimationFrame\s*\(\s*(?<id>\d+)\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mCRaf.Success) { int id; if (int.TryParse(mCRaf.Groups["id"].Value, out id)) CancelAnimationFrame(id); return true; }

            // ---------- localStorage ----------
            var mLSset = Regex.Match(line, @"^\s*localStorage\s*\.setItem\s*\(\s*['""](?<k>.+?)['""]\s*,\s*['""](?<v>.*?)['""]\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mLSset.Success)
            {
                var bag = GetLocalStorageFor(ctx?.BaseUri ?? _ctx?.BaseUri);
                lock (_storageLock) bag[mLSset.Groups["k"].Value] = mLSset.Groups["v"].Value;
                return true;
            }
            var mLSgetCb = Regex.Match(line, @"^\s*localStorage\s*\.getItem\s*\(\s*['""](?<k>.+?)['""]\s*,\s*(?<fn>[A-Za-z_$][A-Za-z0-9_$]*)\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mLSgetCb.Success)
            {
                var bag = GetLocalStorageFor(ctx?.BaseUri ?? _ctx?.BaseUri);
                string val = null; lock (_storageLock) bag.TryGetValue(mLSgetCb.Groups["k"].Value, out val);
                var esc = JsEscape(val ?? "", '\''); EnqueueMicrotask(() => { try { RunInline(mLSgetCb.Groups["fn"].Value + "('" + esc + "')", _ctx); } catch { /* swallow */ } });
                return true;
            }
            var mLSrem = Regex.Match(line, @"^\s*localStorage\s*\.removeItem\s*\(\s*['""](?<k>.+?)['""]\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mLSrem.Success)
            {
                var bag = GetLocalStorageFor(ctx?.BaseUri ?? _ctx?.BaseUri);
                lock (_storageLock) bag.Remove(mLSrem.Groups["k"].Value);
                return true;
            }
            if (Regex.IsMatch(line, @"^\s*localStorage\s*\.clear\s*\(\s*\)\s*;?$", RegexOptions.IgnoreCase))
            {
                var bag = GetLocalStorageFor(ctx?.BaseUri ?? _ctx?.BaseUri);
                lock (_storageLock) bag.Clear();
                try { var _ = SaveLocalStorageAsync(); } catch { /* swallow */ }
                return true;
            }

            // ---------- sessionStorage (in-memory only) ----------
            var mSSset = Regex.Match(line, @"^\s*sessionStorage\s*\.setItem\s*\(\s*['\""](?<k>.+?)['\""]\s*,\s*['\""](?<v>.*?)['\""]\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mSSset.Success)
            {
                var bagSS = GetSessionStorageFor(ctx?.BaseUri ?? _ctx?.BaseUri);
                lock (_storageLock) bagSS[mSSset.Groups["k"].Value] = mSSset.Groups["v"].Value;
                return true;
            }
            var mSSget = Regex.Match(line, @"^\s*sessionStorage\s*\.getItem\s*\(\s*['\""](?<k>.+?)['\""]\s*,\s*(?<fn>[A-Za-z_$][A-Za-z0-9_$]*)\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mSSget.Success)
            {
                var bagSS = GetSessionStorageFor(ctx?.BaseUri ?? _ctx?.BaseUri);
                string vSS = null; lock (_storageLock) bagSS.TryGetValue(mSSget.Groups["k"].Value, out vSS);
                var escSS = JsEscape(vSS ?? "", '\''); EnqueueMicrotask(() => { try { RunInline(mSSget.Groups["fn"].Value + "('" + escSS + "')", _ctx); } catch { /* swallow */ } });
                return true;
            }
            var mSSrem = Regex.Match(line, @"^\s*sessionStorage\s*\.removeItem\s*\(\s*['\""](?<k>.+?)['\""]\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mSSrem.Success)
            {
                var bagSS = GetSessionStorageFor(ctx?.BaseUri ?? _ctx?.BaseUri);
                lock (_storageLock) bagSS.Remove(mSSrem.Groups["k"].Value);
                return true;
            }
            if (Regex.IsMatch(line, @"^\s*sessionStorage\s*\.clear\s*\(\s*\)\s*;?$", RegexOptions.IgnoreCase))
            {
                var bagSS = GetSessionStorageFor(ctx?.BaseUri ?? _ctx?.BaseUri);
                lock (_storageLock) bagSS.Clear();
                return true;
            }

            // ---------- document.cookie ----------
            var mCset = Regex.Match(line, @"^\s*document\s*\.cookie\s*=\s*['""](?<c>.+?)['""]\s*;?$", RegexOptions.IgnoreCase);
            if (mCset.Success) { SetCookieString(ctx?.BaseUri ?? _ctx?.BaseUri, mCset.Groups["c"].Value); return true; }
            var mCget = Regex.Match(line, @"^\s*__getCookie\s*\(\s*(?<fn>[A-Za-z_$][A-Za-z0-9_$]*)\s*\)\s*;?$", RegexOptions.IgnoreCase); // host helper
            if (mCget.Success)
            {
                var s = GetCookieString(ctx?.BaseUri ?? _ctx?.BaseUri);
                var esc = JsEscape(s ?? "", '\'');
                EnqueueMicrotask(() => { try { RunInline(mCget.Groups["fn"].Value + "('" + esc + "')", _ctx); } catch { /* swallow */ } });
                return true;
            }

            // ---------- classList on element by id ----------
            var mCls = Regex.Match(line, @"^\s*document\s*\.\s*getElementById\s*\(\s*['""](?<id>.+?)['""]\s*\)\s*\.classList\s*\.(?<op>add|remove|toggle)\s*\(\s*['""](?<cls>.+?)['""]\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mCls.Success && _domRoot != null)
            {
                var id = mCls.Groups["id"].Value; var op = mCls.Groups["op"].Value; var cls = mCls.Groups["cls"].Value;
                var doc = new JsDocument(this, _domRoot);
                var el = doc.getElementById(id) as JsDomElement;
                if (el != null)
                {
                    var classes = el.getAttribute("class") ?? "";
                    var set = new HashSet<string>((classes ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
                    if (op == "add") set.Add(cls);
                    else if (op == "remove") set.Remove(cls);
                    else if (op == "toggle") { if (!set.Add(cls)) set.Remove(cls); }
                    el.setAttribute("class", string.Join(" ", set.ToArray()));
                }
                return true;
            }

            // ---------- history ----------
            var mPush = Regex.Match(line, @"^\s*history\s*\.pushState\s*\(\s*.*?,\s*['""](?<url>.*?)['""]\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mPush.Success)
            {
                var u = Resolve(ctx?.BaseUri ?? _ctx?.BaseUri, mPush.Groups["url"].Value);
                if (u != null) { HistoryPush(u); try { _host.SetStatus("pushState -> " + u); } catch { /* swallow */ } }
                return true;
            }
            var mRep = Regex.Match(line, @"^\s*history\s*\.replaceState\s*\(\s*.*?,\s*['""](?<url>.*?)['""]\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mRep.Success)
            {
                var u = Resolve(ctx?.BaseUri ?? _ctx?.BaseUri, mRep.Groups["url"].Value);
                if (u != null) { HistoryReplace(u); try { _host.SetStatus("replaceState -> " + u); } catch { /* swallow */ } }
                return true;
            }
            if (Regex.IsMatch(line, @"^\s*history\s*\.back\s*\(\s*\)\s*;?$", RegexOptions.IgnoreCase)) { HistoryGo(-1); return true; }
            if (Regex.IsMatch(line, @"^\s*history\s*\.forward\s*\(\s*\)\s*;?$", RegexOptions.IgnoreCase)) { HistoryGo(1); return true; }
            var mGo = Regex.Match(line, @"^\s*history\s*\.go\s*\(\s*(?<n>-?\d+)\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mGo.Success) { int n; if (int.TryParse(mGo.Groups["n"].Value, out n)) HistoryGo(n); return true; }

            // ---------- new Audio("url").play() stub (no-op) ----------
            var mAudioNewPlay = System.Text.RegularExpressions.Regex.Match(
                line,
                "^\\s*new\\s+Audio\\s*\\(\\s*(['\"'])(?<url>.*?)\\1\\s*\\)\\s*\\.\\s*play\\s*\\(\\s*\\)\\s*;?\\s*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (mAudioNewPlay.Success)
            {
                try
                {
                    TraceFeatureGap("Audio", "new Audio().play", mAudioNewPlay.Groups["url"].Value ?? string.Empty);
                }
                catch { /* swallow */ }
                return true;
            }

            // ---------- atob / btoa for innerText ----------
            var mAtob = Regex.Match(line, @"^\s*document\s*\.\s*getElementById\s*\(\s*['""](?<id>.+?)['""]\s*\)\s*\.innerText\s*=\s*atob\s*\(\s*['""](?<b64>.*?)['""]\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mAtob.Success && _domRoot != null)
            {
                try
                {
                    var id = mAtob.Groups["id"].Value; var b = mAtob.Groups["b64"].Value;
                    var bytes = Convert.FromBase64String(b);
                    var txt = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
                    var doc = new JsDocument(this, _domRoot);
                    var el = doc.getElementById(id) as JsDomElement;
                    if (el != null) { el.innerText = txt; RequestRepaint(); }
                }
                catch { /* swallow */ }
                return true;
            }
            // document.getElementById('id').addEventListener('evt', fn)
            var mElAdd = Regex.Match(line,
                @"^\s*document\s*\.getElementById\s*\(\s*['""](?<id>.+?)['""]\s*\)\s*\.addEventListener\s*\(\s*['""](?<evt>[^'""]+)['""]\s*,\s*(?<fn>[A-Za-z_$][A-Za-z0-9_$]*)\s*\)\s*;?$",
                RegexOptions.IgnoreCase);
            if (mElAdd.Success)
            {
                RegisterElementListener(mElAdd.Groups["id"].Value, mElAdd.Groups["evt"].Value, mElAdd.Groups["fn"].Value);
                return true;
            }

            // document.getElementById('id').removeEventListener('evt', fn)
            var mElRem = Regex.Match(line,
                @"^\s*document\s*\.getElementById\s*\(\s*['""](?<id>.+?)['""]\s*\)\s*\.removeEventListener\s*\(\s*['""](?<evt>[^'""]+)['""]\s*,\s*(?<fn>[A-Za-z_$][A-Za-z0-9_$]*)\s*\)\s*;?$",
                RegexOptions.IgnoreCase);
            if (mElRem.Success)
            {
                RemoveElementListener(mElRem.Groups["id"].Value, mElRem.Groups["evt"].Value, mElRem.Groups["fn"].Value);
                return true;
            }
            var mBtoa = Regex.Match(line, @"^\s*document\s*\.\s*getElementById\s*\(\s*['""](?<id>.+?)['""]\s*\)\s*\.innerText\s*=\s*btoa\s*\(\s*['""](?<txt>.*?)['""]\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mBtoa.Success && _domRoot != null)
            {
                try
                {
                    var id = mBtoa.Groups["id"].Value; var t = mBtoa.Groups["txt"].Value;
                    var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(t ?? ""));
                    var doc = new JsDocument(this, _domRoot);
                    var el = doc.getElementById(id) as JsDomElement;
                    if (el != null) { el.innerText = b64; RequestRepaint(); }
                }
                catch { /* swallow */ }
                return true;
            }

            

// ---------- setTimeout / clearTimeout ----------
var mST = System.Text.RegularExpressions.Regex.Match(line, @"^\s*setTimeout\s*\(\s*(?:['""](?<code>.*?)['""]|(?<fn>[A-Za-z_$][A-Za-z0-9_$]*))\s*,\s*(?<ms>\d+)\s*\)\s*;?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
if (mST.Success)
{
    var ms = 0; int.TryParse(mST.Groups["ms"].Value, out ms);
    var code = mST.Groups["code"].Success ? mST.Groups["code"].Value : null;
    var fn = mST.Groups["fn"].Success ? mST.Groups["fn"].Value : null;
    var id = System.Threading.Interlocked.Increment(ref _nextTimerId);
    System.Threading.Timer t = null;
    var repaintHost = _host as IJsHostRepaint;
    System.Threading.TimerCallback fire = _ =>
    {
        try
        {
            System.Action run = () => {
                try
                {
                    if (fn != null) RunInline(fn + "()", _ctx);
                    else if (!string.IsNullOrEmpty(code)) RunInline(code, _ctx);
                }
                catch { /* swallow */ }
            };
            if (repaintHost != null) repaintHost.InvokeOnUiThread(run); else run();
        }
        catch { /* swallow */ }
        finally
        {
            lock (_timers) { try { if (_timers.ContainsKey(id)) { _timers[id].Dispose(); } } catch { /* swallow */ } _timers.Remove(id); }
        }
    };
    t = new System.Threading.Timer(fire, null, ms, System.Threading.Timeout.Infinite);
    lock (_timers) _timers[id] = t;
    try { _host.SetStatus("setTimeout id=" + id); } catch { /* swallow */ }
    return true;
}
var mCT = System.Text.RegularExpressions.Regex.Match(line, @"^\s*clearTimeout\s*\(\s*(?<id>\d+)\s*\)\s*;?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
if (mCT.Success)
{
    int id; if (int.TryParse(mCT.Groups["id"].Value, out id))
    {
        lock (_timers)
        {
            System.Threading.Timer t; if (_timers.TryGetValue(id, out t)) { try { t.Dispose(); } catch { /* swallow */ } _timers.Remove(id); }
        }
    }
    return true;
}

// ---------- console.log / console.error ----------
var mLog = System.Text.RegularExpressions.Regex.Match(line, @"^\s*console\s*\.\s*(?<kind>log|error|warn)\s*\(\s*['""](?<msg>.*?)['""]\s*\)\s*;?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
if (mLog.Success)
{
    var kind = mLog.Groups["kind"].Value.ToLowerInvariant();
    var msg = mLog.Groups["msg"].Value;
    try { _host.SetStatus(kind + ": " + msg); } catch { /* swallow */ }
    try { BrowserCore.Engine.DevToolsLogger.Log("[JS:" + kind.ToUpperInvariant() + "] " + msg); } catch { }
    return true;
}

// ---------- location.assign / replace / href= ----------
var mAssign = System.Text.RegularExpressions.Regex.Match(line, @"^\s*location\s*\.\s*assign\s*\(\s*['""](?<u>.+?)['""]\s*\)\s*;?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
if (mAssign.Success)
{
    var u = Resolve(ctx?.BaseUri ?? _ctx?.BaseUri, mAssign.Groups["u"].Value);
    if (u != null) { try { _host.Navigate(u); } catch { /* swallow */ } }
    return true;
}
var mReplace = System.Text.RegularExpressions.Regex.Match(line, @"^\s*location\s*\.\s*replace\s*\(\s*['""](?<u>.+?)['""]\s*\)\s*;?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
if (mReplace.Success)
{
    var u = Resolve(ctx?.BaseUri ?? _ctx?.BaseUri, mReplace.Groups["u"].Value);
    if (u != null) { try { _host.Navigate(u); } catch { /* swallow */ } }
    return true;
}
var mHrefSet = System.Text.RegularExpressions.Regex.Match(line, @"^\s*location\s*\.\s*href\s*=\s*['""](?<u>.+?)['""]\s*;?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
if (mHrefSet.Success)
{
    var u = Resolve(ctx?.BaseUri ?? _ctx?.BaseUri, mHrefSet.Groups["u"].Value);
    if (u != null) { try { _host.Navigate(u); } catch { /* swallow */ } }
    return true;
}

            // ---------- document.title = "..." ----------
            var mTitle = System.Text.RegularExpressions.Regex.Match(
                line,
                @"^\s*document\s*\.\s*title\s*=\s*['""](?<t>.*?)['""]\s*;?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (mTitle.Success)
            {
                try
                {
                    string tval = "";
                    if (mTitle.Groups["t"] != null && mTitle.Groups["t"].Value != null)
                        tval = mTitle.Groups["t"].Value;
                    _host.SetTitle(tval);
                }
                catch { /* swallow */ }
                return true;
            }

            return false; // not handled here
        }

        // ----------- JS-0 (always on) patterns (no RegexOptions.Compiled for WP/UWP AOT) -----------
        private static readonly Regex RxXhrNew = new Regex(
            @"^\s*__xhr_new\s*\(\s*['""](?<id>.+?)['""]\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxXhrOpen = new Regex(
            @"^\s*__xhr_open\s*\(\s*['""](?<id>.+?)['""]\s*,\s*['""](?<m>.+?)['""]\s*,\s*['""](?<url>.+?)['""]\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxXhrSetHdr = new Regex(
            @"^\s*__xhr_setRequestHeader\s*\(\s*['""](?<id>.+?)['""]\s*,\s*['""](?<n>.+?)['""]\s*,\s*['""](?<v>.*?)['""]\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxXhrSend = new Regex(
            @"^\s*__xhr_send\s*\(\s*['""](?<id>.+?)['""](?:\s*,\s*['""](?<body>.*?)['""])?\s*,\s*(?<onload>[A-Za-z_$][A-Za-z0-9_$]*)\s*(?:\s*,\s*(?<onerror>[A-Za-z_$][A-Za-z0-9_$]*))?\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxXhrDeliver = new Regex(
            @"^\s*__xhr_deliver\s*\(\s*['""](?<token>.+?)['""]\s*,\s*(?<fn>[A-Za-z_$][A-Za-z0-9_$]*)\s*,\s*(?<status>-?\d+)\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxHrefAssign = new Regex(
            @"^\s*location\s*\.\s*href\s*=\s*(['""])(?<url>.+?)\1\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxAssignCall = new Regex(
            @"^\s*location\s*\.\s*assign\s*\(\s*(['""])(?<url>.+?)\1\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxReplaceCall = new Regex(
            @"^\s*location\s*\.\s*replace\s*\(\s*(['""])(?<url>.+?)\1\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxWindowLoc = new Regex(
            @"^\s*window\s*\.\s*location\s*(?:\.href)?\s*=\s*(['""])(?<url>.+?)\1\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxAlert = new Regex(
            @"^\s*alert\s*\(\s*(['""])(?<msg>.*?)\1\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxConsoleLog = new Regex(
            @"^\s*console\s*\.\s*log\s*\(\s*(['""])(?<msg>.*?)\1\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxGetByIdInnerTextAssign = new Regex(
            @"^\s*document\s*\.\s*getElementById\s*\(\s*(['""])(?<id>.+?)\1\s*\)\s*\.\s*innerText\s*=\s*(['""])(?<val>.*?)\3\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxGetByIdSetAttr = new Regex(
            @"^\s*document\s*\.\s*getElementById\s*\(\s*(['""])(?<id>.+?)\1\s*\)\s*\.\s*setAttribute\s*\(\s*(['""])(?<an>.+?)\3\s*,\s*(['""])(?<av>.*?)\5\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxReturnFalse = new Regex(
            @"^\s*return\s+false\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxEventPreventDefault = new Regex(
            @"^\s*event\s*\.\s*preventDefault\s*\(\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);
        private static readonly Regex RxEventStopPropagation = new Regex(
            @"^\s*event\s*\.\s*stopPropagation\s*\(\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxSetTimeout = new Regex(
            @"^\s*setTimeout\s*\(\s*(['""])(?<code>.*?)\1\s*,\s*(?<ms>\d+)\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxClearTimeout = new Regex(
            @"^\s*clearTimeout\s*\(\s*(?<id>\d+)\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxSetTimeoutFn = new Regex(
            @"^\s*setTimeout\s*\(\s*(?<fn>[A-Za-z_$][A-Za-z0-9_$]*)\s*,\s*(?<ms>\d+)\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);

        // setInterval/clearInterval (mirror setTimeout patterns)
        private static readonly Regex RxSetInterval = new Regex(
            @"^\s*setInterval\s*\(\s*(['""])(?<code>.*?)\1\s*,\s*(?<ms>\d+)\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);
        private static readonly Regex RxSetIntervalFn = new Regex(
            @"^\s*setInterval\s*\(\s*(?<fn>[A-Za-z_$][A-Za-z0-9_$]*)\s*,\s*(?<ms>\d+)\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);
        private static readonly Regex RxClearInterval = new Regex(
            @"^\s*clearInterval\s*\(\s*(?<id>\d+)\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxWindowOpen = new Regex(
            @"^\s*window\s*\.\s*open\s*\(\s*([""'])(?<url>.+?)\1(?:\s*,\s*([""'])(?<target>.+?)\3)?\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase
        );

        private static readonly Regex RxLocationReload = new Regex(
            @"^\s*(?:window\s*\.\s*)?location\s*\.\s*reload\s*\(\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxVoidZero = new Regex(
            @"^\s*void\s*\(\s*0\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);

        // Match: document.getElementById("id").style.property = "value";
        private static readonly Regex RxElStyleSet = new Regex(
            @"^\s*document\s*\.\s*getElementById\s*\(\s*([""'])(?<id>.+?)\1\s*\)\s*\.\s*style\s*\.\s*(?<prop>[A-Za-z\-]+)\s*=\s*([""'])(?<val>.*?)\5\s*;?\s*$",
            RegexOptions.IgnoreCase
        );

        // Match: document.getElementById("id").classList.add/remove/toggle("class");
        private static readonly Regex RxElClassListOp = new Regex(
            @"^\s*document\s*\.\s*getElementById\s*\(\s*([""'])(?<id>.+?)\1\s*\)\s*\.\s*classList\s*\.\s*(?<op>add|remove|toggle)\s*\(\s*([""'])(?<cls>.+?)\5\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase
        );

        // Match: document.getElementById("id").onclick = handlerFn;
        private static readonly Regex RxElOnAssign = new Regex(
            @"^\s*document\s*\.\s*getElementById\s*\(\s*([""'])(?<id>.+?)\1\s*\)\s*\.\s*on(?<evt>[a-z]+)\s*=\s*(?<fn>[A-Za-z_$][A-Za-z0-9_$]*)\s*;?\s*$",
            RegexOptions.IgnoreCase
        );

        // Match: history.pushState(anything, "title", "url");
        private static readonly Regex RxHistoryPush = new Regex(
            @"^\s*history\s*\.\s*pushState\s*\(.*?,\s*(?:([""']).*?\1\s*,\s*)?([""'])(?<url>.+?)\2\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase
        );

        // Match: history.replaceState(anything, "title", "url");
        private static readonly Regex RxHistoryReplace = new Regex(
            @"^\s*history\s*\.\s*replaceState\s*\(.*?,\s*(?:([""']).*?\1\s*,\s*)?([""'])(?<url>.+?)\2\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase
        );

        private static readonly Regex RxFetchText = new Regex(
            @"^\s*fetchText\s*\(\s*(['""])(?<url>.*?)\1\s*,\s*(['""])(?<id>.*?)\3\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);
        private static readonly Regex RxFetchThen = new Regex(
            @"^\s*fetch\s*\(\s*(['""])(?<url>.*?)\1\s*\)\s*\.then\s*\(\s*(['""])(?<code>.*?)\3\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);
        private static readonly Regex RxFetchThenFunc = new Regex(
            @"^\s*fetch\s*\(\s*(['""])(?<url>.*?)\1\s*\)\s*\.then\s*\(\s*(?<fn>[A-Za-z_$][A-Za-z0-9_$]*)\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private static readonly Regex RxPromiseThenFunc = new Regex(
            @"^\s*Promise\s*\.\s*resolve\s*\(\s*\)\s*\.then\s*\(\s*(?<fn>[A-Za-z_$][A-Za-z0-9_$]*)\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase);

        private void XhrNew(string id)
        {
            if (!SandboxAllows(SandboxFeature.Network, "XMLHttpRequest")) return;
            if (string.IsNullOrWhiteSpace(id)) return;
            lock (_xhrLock) _xhr[id] = new XhrState { Id = id, Method = "GET" };
        }

        private void XhrOpen(string id, string method, string url, JsContext ctx)
        {
            if (!SandboxAllows(SandboxFeature.Network, "XMLHttpRequest.open")) return;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(url)) return;
            Uri resolved = Resolve(ctx?.BaseUri ?? _ctx?.BaseUri, url);
            if (resolved == null) return;
            lock (_xhrLock)
            {
                XhrState st;
                if (!_xhr.TryGetValue(id, out st)) { st = new XhrState { Id = id }; _xhr[id] = st; }
                st.Method = method.ToUpperInvariant();
                st.Url = resolved;
            }
        }

        private void XhrSetHeader(string id, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) return;
            lock (_xhrLock)
            {
                XhrState st; if (!_xhr.TryGetValue(id, out st)) return;
                st.Headers[name] = value ?? "";
            }
        }

        private void XhrSend(string id, string body, string onloadFn, string onerrorFn, JsContext ctx)
        {
            if (!SandboxAllows(SandboxFeature.Network, "XMLHttpRequest.send")) return;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(onloadFn)) return;

            XhrState st;
            lock (_xhrLock)
            {
                if (!_xhr.TryGetValue(id, out st)) { return; }
                st.Body = body;
                st.OnLoadFn = onloadFn;
                st.OnErrorFn = onerrorFn;
            }

            if (st.Url == null) return;

            Task.Run(async () =>
            {
                int status = -1;
                string text = null;
                Exception error = null;

                try
                {
                    var handler = CreateManagedHandler(st.Url);
                    using (var client = new System.Net.Http.HttpClient(handler))
                    {
                        var req = new HttpRequestMessage(new HttpMethod(st.Method ?? "GET"), st.Url);
                        BuildSafeSubresourceHeaders(req, ctx?.BaseUri ?? _ctx?.BaseUri);

                        foreach (var kv in st.Headers)
                            try { req.Headers.TryAddWithoutValidation(kv.Key, kv.Value); } catch { /* swallow */ }

                        if (!string.IsNullOrEmpty(st.Body) && st.Method != null &&
                           (string.Equals(st.Method, "POST", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(st.Method, "PUT", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(st.Method, "PATCH", StringComparison.OrdinalIgnoreCase)))
                        {
                            string ct = null; st.Headers.TryGetValue("Content-Type", out ct);
                            req.Content = new StringContent(st.Body, Encoding.UTF8,
                                string.IsNullOrEmpty(ct) ? "application/x-www-form-urlencoded; charset=UTF-8" : ct);
                        }

                        var resp = await client.SendAsync(req).ConfigureAwait(false);
                        status = (int)resp.StatusCode;

                        var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        string enc = null; try { enc = string.Join(",", resp.Content.Headers.ContentEncoding); } catch { /* swallow */ }
                        text = DecodeBytes(bytes, enc);
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                    status = -1;
                    text = ex.Message ?? "Network error";
                }

                try
                {
                    if (error != null && !string.IsNullOrWhiteSpace(st.OnErrorFn))
                    {
                        var escErr = JsEscape(text ?? "", '\'');
                        EnqueueMicrotask(() => { try { RunInline(st.OnErrorFn + "(" + status + ",'" + escErr + "')", _ctx); } catch { /* swallow */ } });
                        return;
                    }

                    var bodyOut = text ?? "";
                    if (bodyOut.Length <= _inlineThreshold)
                    {
                        var esc = JsEscape(bodyOut, '\'');
                        EnqueueMicrotask(() => { try { RunInline(st.OnLoadFn + "(" + status + ",'" + esc + "')", _ctx); } catch { /* swallow */ } });
                    }
                    else
                    {
                        var token = RegisterResponseBody(bodyOut);
                        EnqueueMicrotask(() => { try { RunInline("__xhr_deliver('" + token + "'," + st.OnLoadFn + "," + status + ")", _ctx); } catch { /* swallow */ } });
                    }
                }
                catch { /* swallow */ }
                finally
                {
                    try { lock (_xhrLock) _xhr.Remove(id); } catch { /* swallow */ }
                }
            });
        }


        // No full JS engine bundled here; we keep a tiny allowlist (JS-0) for inline handlers.
        public JavaScriptEngine(IJsHost host)
        {
            _host = host;

            // ***

            try
            {
                // AppDomain.CurrentDomain is not available on UWP (no .NET AppDomain)
                // First-chance exception logging is handled by App.UnhandledException
            }
            catch { }

            // ***
            _e = this;
            try { _http = new System.Net.Http.HttpClient(CreateManagedHandler(null)); } catch { _http = new System.Net.Http.HttpClient(); }
            try { var _ = RestoreLocalStorageAsync(); } catch { /* swallow */ }
            try
            {
                // NiL.JS context initialized here
                _nilInit();
                _moduleLoader = new ModuleLoader(this, _nil);
            }
            catch { /* swallow */ }
            try { var _ = LoadBadHashStoreAsync(); } catch { }
        }







        // Legacy JSON-based localStorage persistence (no longer used; kept for compatibility reference)
        // The engine now uses a simple line-based format via SaveLocalStorageAsync/RestoreLocalStorageAsync
        // against the _localStorageMap dictionary. This stub remains to avoid breaking older call sites.
        private async void PersistLocalStorage()
        {
            try
            {
                await SaveLocalStorageAsync().ConfigureAwait(false);
            }
            catch { /* swallow */ }
        }

    public void LocalStorageSet(string key, string value, JsContext ctx) { try { if (!SandboxAllows(SandboxFeature.Storage, "localStorage.setItem")) return; var bag = GetLocalStorageFor(ctx?.BaseUri ?? _ctx?.BaseUri); lock(_storageLock) bag[key] = value ?? ""; PersistLocalStorage(); } catch { /* swallow */ } }
    public string LocalStorageGet(string key, JsContext ctx) { try { if (!SandboxAllows(SandboxFeature.Storage, "localStorage.getItem")) return null; var bag = GetLocalStorageFor(ctx?.BaseUri ?? _ctx?.BaseUri); string v=null; lock(_storageLock) bag.TryGetValue(key, out v); return v; } catch { return null; } }
    public void LocalStorageRemove(string key, JsContext ctx) { try { if (!SandboxAllows(SandboxFeature.Storage, "localStorage.removeItem")) return; var bag = GetLocalStorageFor(ctx?.BaseUri ?? _ctx?.BaseUri); lock(_storageLock) bag.Remove(key); PersistLocalStorage(); } catch { /* swallow */ } }
    public void LocalStorageClear(JsContext ctx) { try { if (!SandboxAllows(SandboxFeature.Storage, "localStorage.clear")) return; var bag = GetLocalStorageFor(ctx?.BaseUri ?? _ctx?.BaseUri); lock(_storageLock) bag.Clear(); PersistLocalStorage(); } catch { /* swallow */ } }

    public void Reset(JsContext ctx)
        {
            _ctx = ctx ?? new JsContext();
            ClearSandboxBlockLog();
        }

        private string FetchTextSync(Uri uri)
        {
            try
            {
                using (var hc = new HttpClient())
                {
                    try { hc.DefaultRequestHeaders.UserAgent.ParseAdd("MiniJs/1.0"); } catch { /* swallow */ }
                    return hc.GetStringAsync(uri).GetAwaiter().GetResult();
                }
            }
            catch { return ""; }
        }

        // Request the host to re-render if available (non-breaking: optional interface)
        private void RequestRepaint()
        {
            if (_repaintRequested) return;
            _repaintRequested = true;

            // Accumulate mutations for incremental renderer
            lock (_mutationLock)
            {
                if (_pendingRendererMutations == null)
                    _pendingRendererMutations = _pendingMutations.ToList();
                else
                    _pendingRendererMutations.AddRange(_pendingMutations);
            }

            var repaint = _host as IJsHostRepaint;
            if (repaint != null)
            {
                try { repaint.RequestRender(); }
                catch { }
            }
            else
            {
                try { _host.SetStatus("[DOM mutated]"); } catch { }
            }
            // Schedule MutationObserver callbacks with the records
            try { InvokeMutationObservers(); } catch { }
            _repaintRequested = false;
        }

        public System.Collections.Generic.List<InternalMutationRecord> DrainRendererMutations()
        {
            var result = _pendingRendererMutations;
            _pendingRendererMutations = null;
            if (result != null && result.Count == 0) return null;
            return result;
        }

        // When the DOM changes, invoke any registered MutationObserver callbacks as microtasks
        private void InvokeMutationObservers()
        {
            try
            {
                List<HostMutationObserver> copy;
                List<InternalMutationRecord> records;
                lock (_mutationLock)
                {
                    copy = _activeObservers.ToList();
                    records = _pendingMutations.ToList();
                    _pendingMutations.Clear();
                }
                if (records.Count == 0 || copy.Count == 0) return;

                foreach (var obs in copy)
                {
                    var cb = obs.Callback as Function;
                    if (cb == null) continue;
                    var arr = InternalRecordsToJSArray(records);
                    EnqueueMicrotask(() =>
                    {
                        try { cb.Call(JSValue.Undefined, new Arguments { arr, JSValue.Marshal(obs) }); }
                        catch { /* swallow */ }
                    });
                }
            }
            catch { /* swallow */ }
        }

        private JSValue InternalRecordsToJSArray(List<InternalMutationRecord> records)
        {
            try
            {
                var arr = SafeEval("[]");
                for (int i = 0; i < records.Count; i++)
                {
                    var r = records[i];
                    var rec = SafeEval("({})");
                    rec["type"] = JSValue.Marshal(r.Type ?? "");
                    rec["target"] = JSValue.Marshal(TargetDescriptor(r.Target));
                    if (r.Type == "childList")
                    {
                        var added = SafeEval("[]");
                        if (r.Added != null)
                            for (int a = 0; a < r.Added.Count; a++)
                                added[(string)a.ToString()] = JSValue.Marshal(TargetDescriptor(r.Added[a]));
                        rec["addedNodes"] = added;
                        var removed = SafeEval("[]");
                        if (r.Removed != null)
                            for (int rm = 0; rm < r.Removed.Count; rm++)
                                removed[(string)rm.ToString()] = JSValue.Marshal(TargetDescriptor(r.Removed[rm]));
                        rec["removedNodes"] = removed;
                    }
                    if (r.Type == "attributes")
                    {
                        rec["attributeName"] = JSValue.Marshal(r.AttributeName ?? "");
                    }
                    arr[(string)i.ToString()] = rec;
                }
                return arr;
            }
            catch { return SafeEval("[]"); }
        }

        private static string TargetDescriptor(LiteElement el)
        {
            if (el == null) return "unknown";
            if (el.Tag == "#text") return "#text";
            string id;
            if (el.Attr != null && el.Attr.TryGetValue("id", out id) && !string.IsNullOrWhiteSpace(id))
                return el.Tag + "#" + id;
            string cls;
            if (el.Attr != null && el.Attr.TryGetValue("class", out cls) && !string.IsNullOrWhiteSpace(cls))
                return el.Tag + "." + cls.Split(' ')[0];
            return el.Tag ?? "unknown";
        }

        // ---- XHR shim state ----
        private sealed class XhrState
        {
            public string Id;
            public string Method;
            public Uri Url;
            public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string Body;
            public string OnLoadFn;
            public string OnErrorFn;
        }

        private readonly Dictionary<string, XhrState> _xhr =
            new Dictionary<string, XhrState>(StringComparer.Ordinal);
        private readonly object _xhrLock = new object();

        // Convert a Windows.Data.Json.IJsonValue to a JS literal string (no whitespace) - limited support for objects/arrays/primitives
        private static string JsonToJsLiteral(Windows.Data.Json.IJsonValue v)
        {
            if (v == null) return "null";
            try
            {
                var vt = v.ValueType;
                if (vt == Windows.Data.Json.JsonValueType.Object)
                {
                    var jo = v.GetObject();
                    var sb = new StringBuilder(); sb.Append('{'); bool first = true;
                    var keys = jo.Keys; foreach (var key in keys)
                    {
                        if (!first) sb.Append(','); first = false;
                        sb.Append('"'); sb.Append(key); sb.Append('"'); sb.Append(':'); sb.Append(JsonToJsLiteral(jo.GetNamedValue(key)));
                    }
                    sb.Append('}'); return sb.ToString();
                }
                if (vt == Windows.Data.Json.JsonValueType.Array)
                {
                    var ja = v.GetArray(); var sba = new StringBuilder(); sba.Append('['); bool f2 = true;
                    foreach (var it in ja)
                    {
                        if (!f2) sba.Append(','); f2 = false; sba.Append(JsonToJsLiteral(it));
                    }
                    sba.Append(']'); return sba.ToString();
                }
                if (vt == Windows.Data.Json.JsonValueType.String)
                {
                    return '"' + JsEscape(v.GetString() ?? "", '"') + '"';
                }
                if (vt == Windows.Data.Json.JsonValueType.Number)
                {
                    return v.GetNumber().ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                if (vt == Windows.Data.Json.JsonValueType.Boolean)
                {
                    try { return v.GetBoolean() ? "true" : "false"; } catch { return "false"; }
                }
                return "null";
            }
            catch { return "null"; }
        }

        public sealed class InternalMutationRecord
        {
            public string Type; // "childList" or "attributes"
            public LiteElement Target;
            public List<LiteElement> Added;
            public List<LiteElement> Removed;
            public string AttributeName;
            public string OldValue;
        }

        private System.Net.Http.HttpMessageHandler CreateManagedHandler(Uri uri = null, System.Net.CookieContainer cookies = null)
        {
            var handler = new System.Net.Http.HttpClientHandler();
            if (handler.SupportsAutomaticDecompression)
                handler.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;
            
            if (cookies == null && uri != null && CookieBridge != null)
                cookies = CookieBridge(uri);

            if (cookies != null && handler.SupportsRedirectConfiguration)
                handler.CookieContainer = cookies;
            return handler;
        }

        private async Task<string> FetchAsync(Uri uri)
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient(CreateManagedHandler()))
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows Phone 8.1; ARM; Trident/7.0; Touch; rv:11.0; IEMobile/11.0; NOKIA; Lumia 520) like Gecko");
                    return await client.GetStringAsync(uri);
                }
            }
            catch { return null; }

        }

        /// <summary>Fetch with method, body, and headers. Returns (body, statusCode, responseHeaders).</summary>
        private async Task<Tuple<string, int, System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>>> FetchWithMethodAsync(
            Uri uri, string method, string body, System.Collections.Generic.Dictionary<string, string> requestHeaders)
        {
            try
            {
                using (var handler = CreateManagedHandler(uri))
                using (var client = new System.Net.Http.HttpClient(handler))
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows Phone 8.1; ARM; Trident/7.0; Touch; rv:11.0; IEMobile/11.0; NOKIA; Lumia 520) like Gecko");

                    // Add custom headers
                    if (requestHeaders != null)
                    {
                        foreach (var kvp in requestHeaders)
                        {
                            try { client.DefaultRequestHeaders.TryAddWithoutValidation(kvp.Key, kvp.Value); } catch { }
                        }
                    }

                    System.Net.Http.HttpResponseMessage response;
                    if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(method, "PATCH", StringComparison.OrdinalIgnoreCase))
                    {
                        var content = new System.Net.Http.StringContent(body ?? "", System.Text.Encoding.UTF8,
                            requestHeaders != null && requestHeaders.ContainsKey("Content-Type") ? requestHeaders["Content-Type"] : "application/x-www-form-urlencoded");
                        response = await client.SendAsync(new System.Net.Http.HttpRequestMessage(new System.Net.Http.HttpMethod(method), uri) { Content = content });
                    }
                    else if (string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase))
                    {
                        response = await client.SendAsync(new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Delete, uri));
                    }
                    else
                    {
                        response = await client.GetAsync(uri);
                    }

                    var responseBody = await response.Content.ReadAsStringAsync();
                    var respHeaders = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var header in response.Headers)
                    {
                        respHeaders[header.Key] = new System.Collections.Generic.List<string>(header.Value);
                    }
                    foreach (var header in response.Content.Headers)
                    {
                        respHeaders[header.Key] = new System.Collections.Generic.List<string>(header.Value);
                    }

                    return Tuple.Create(responseBody, (int)response.StatusCode, respHeaders);
                }
            }
            catch (Exception ex)
            {
                return Tuple.Create(ex.Message, 0, new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>(StringComparer.OrdinalIgnoreCase));
            }
        }

        private class HostWindow
        {
            private JavaScriptEngine _engine;
            public HostWindow(JavaScriptEngine engine) { _engine = engine; }
            public void alert(string msg) { _engine._host.SetStatus("[Alert] " + msg); }
            public HostLocation location => new HostLocation(_engine);
            public HostDocument document => new HostDocument(_engine);
            public HostNavigator navigator => new HostNavigator(_engine);
            public HostHistory history => new HostHistory(_engine);
            public string name => "";
            public void close() { }
            public void stop() { }
            public void focus() { }
            public void blur() { }
            public void addEventListener(string type, JSValue callback, JSValue options)
            {
                if (string.IsNullOrEmpty(type) || callback == null || callback.IsNull) return;
                System.Collections.Generic.List<JSValue> list;
                if (!_engine._evtWinCallbacksByType.TryGetValue(type, out list) || list == null)
                { list = new System.Collections.Generic.List<JSValue>(); _engine._evtWinCallbacksByType[type] = list; }
                if (!list.Contains(callback)) list.Add(callback);
            }
            public void removeEventListener(string type, JSValue callback)
            {
                if (string.IsNullOrEmpty(type) || callback == null) return;
                System.Collections.Generic.List<JSValue> list;
                if (_engine._evtWinCallbacksByType.TryGetValue(type, out list) && list != null)
                    list.Remove(callback);
            }
            public bool dispatchEvent(JSValue e)
            {
                try
                {
                    if (e == null || e.IsNull) return false;
                    var type = "";
                    try { type = e.ValueType == JSValueType.Object ? (e["type"]?.ToString() ?? "") : e.ToString(); } catch { }
                    if (string.IsNullOrEmpty(type)) return false;
                    // Fire JSValue callbacks
                    System.Collections.Generic.List<JSValue> list;
                    if (_engine._evtWinCallbacksByType.TryGetValue(type, out list) && list != null)
                    {
                        foreach (var cb in list.ToArray())
                        {
                            try { _engine.EnqueueMicrotask(() => { try { _engine.InvokeJsCallback(cb, type, "window"); } catch { } }); } catch { }
                        }
                    }
                    // Also fire string-based callbacks
                    List<string> strList;
                    if (_engine._evtWin.TryGetValue(type, out strList) && strList != null)
                    {
                        foreach (var fn in strList.ToArray())
                        {
                            try { _engine.EnqueueMicrotask(() => { try { _engine.RunInline(fn + "({type:'" + type + "'})", _engine._ctx, type, "window"); } catch { } }); } catch { }
                        }
                    }
                }
                catch { }
                return true;
            }
            public JSValue getComputedStyle(JSValue el)
            {
                try
                {
                    LiteElement node = null;
                    if (el.Value is JsDomElement jde) node = jde._node;
                    CssComputed css = null;
                    if (node != null && _engine._computedStyles != null)
                        _engine._computedStyles.TryGetValue(node, out css);
                    var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    if (css != null)
                    {
                        dict["display"] = css.Display ?? "block";
                        dict["position"] = css.Position ?? "static";
                        dict["visibility"] = css.Visibility ?? "visible";
                        dict["opacity"] = css.Opacity?.ToString("0.##") ?? "1";
                        dict["overflow"] = css.Overflow ?? "visible";
                        dict["overflow-x"] = css.Overflow ?? "visible";
                        dict["overflow-y"] = css.Overflow ?? "visible";
                        dict["transform"] = css.Transform ?? "none";
                        dict["transform-origin"] = "50% 50%";
                        dict["z-index"] = css.ZIndex?.ToString() ?? "auto";
                        dict["width"] = css.Width.HasValue ? css.Width.Value.ToString("0.##") + "px" : "auto";
                        dict["height"] = css.Height.HasValue ? css.Height.Value.ToString("0.##") + "px" : "auto";
                        dict["min-width"] = css.MinWidth.HasValue ? css.MinWidth.Value.ToString("0.##") + "px" : "0";
                        dict["min-height"] = css.MinHeight.HasValue ? css.MinHeight.Value.ToString("0.##") + "px" : "0";
                        dict["max-width"] = css.MaxWidth.HasValue ? css.MaxWidth.Value.ToString("0.##") + "px" : "none";
                        dict["max-height"] = css.MaxHeight.HasValue ? css.MaxHeight.Value.ToString("0.##") + "px" : "none";
                        dict["color"] = css.ForegroundColor.HasValue ? string.Format("#{0:X2}{1:X2}{2:X2}", css.ForegroundColor.Value.R, css.ForegroundColor.Value.G, css.ForegroundColor.Value.B) : "#000";
                        dict["font-size"] = css.FontSize.HasValue ? css.FontSize.Value.ToString("0.##") + "px" : "16px";
                        dict["font-weight"] = css.FontWeight.HasValue ? (css.FontWeight.Value.Weight > 600 ? "bold" : "normal") : "normal";
                        dict["font-style"] = css.FontStyle.HasValue && css.FontStyle.Value == Windows.UI.Text.FontStyle.Italic ? "italic" : "normal";
                        dict["font-family"] = css.FontFamilyName ?? "serif";
                        dict["line-height"] = css.LineHeight.HasValue ? css.LineHeight.Value.ToString("0.##") : "normal";
                        dict["text-align"] = css.TextAlign.HasValue ? css.TextAlign.Value.ToString().ToLowerInvariant() : "start";
                        dict["text-decoration"] = css.TextDecoration ?? "none";
                        dict["text-transform"] = css.TextTransform ?? "none";
                        dict["text-overflow"] = css.TextOverflow ?? "clip";
                        dict["white-space"] = css.WhiteSpace ?? "normal";
                        dict["word-wrap"] = "normal";
                        dict["letter-spacing"] = css.LetterSpacing.HasValue ? css.LetterSpacing.Value.ToString("0.##") + "px" : "normal";
                        dict["margin-top"] = css.Margin.Top.ToString("0.##") + "px";
                        dict["margin-right"] = css.Margin.Right.ToString("0.##") + "px";
                        dict["margin-bottom"] = css.Margin.Bottom.ToString("0.##") + "px";
                        dict["margin-left"] = css.Margin.Left.ToString("0.##") + "px";
                        dict["padding-top"] = css.Padding.Top.ToString("0.##") + "px";
                        dict["padding-right"] = css.Padding.Right.ToString("0.##") + "px";
                        dict["padding-bottom"] = css.Padding.Bottom.ToString("0.##") + "px";
                        dict["padding-left"] = css.Padding.Left.ToString("0.##") + "px";
                        dict["border-top-width"] = css.BorderThickness.Top.ToString("0.##") + "px";
                        dict["border-right-width"] = css.BorderThickness.Right.ToString("0.##") + "px";
                        dict["border-bottom-width"] = css.BorderThickness.Bottom.ToString("0.##") + "px";
                        dict["border-left-width"] = css.BorderThickness.Left.ToString("0.##") + "px";
                        var borderColor = css.BorderBrushColor ?? (css.BorderBrush is Windows.UI.Xaml.Media.SolidColorBrush sb ? sb.Color : (Windows.UI.Color?)null);
                        var borderHex = borderColor.HasValue ? string.Format("#{0:X2}{1:X2}{2:X2}", borderColor.Value.R, borderColor.Value.G, borderColor.Value.B) : "transparent";
                        dict["border-top-color"] = borderHex;
                        dict["border-right-color"] = borderHex;
                        dict["border-bottom-color"] = borderHex;
                        dict["border-left-color"] = borderHex;
                        dict["border-top-style"] = css.BorderStyle ?? "none";
                        dict["border-right-style"] = css.BorderStyle ?? "none";
                        dict["border-bottom-style"] = css.BorderStyle ?? "none";
                        dict["border-left-style"] = css.BorderStyle ?? "none";
                        dict["border-top-left-radius"] = css.BorderRadius.TopLeft.ToString("0.##") + "px";
                        dict["border-top-right-radius"] = css.BorderRadius.TopRight.ToString("0.##") + "px";
                        dict["border-bottom-right-radius"] = css.BorderRadius.BottomRight.ToString("0.##") + "px";
                        dict["border-bottom-left-radius"] = css.BorderRadius.BottomLeft.ToString("0.##") + "px";
                        dict["background-color"] = css.BackgroundColor.HasValue ? string.Format("#{0:X2}{1:X2}{2:X2}", css.BackgroundColor.Value.R, css.BackgroundColor.Value.G, css.BackgroundColor.Value.B) : "transparent";
                        dict["background-image"] = css.BackgroundImageUrl ?? "none";
                        dict["background-position"] = css.BackgroundPosition ?? "0% 0%";
                        dict["background-repeat"] = css.BackgroundRepeat ?? "repeat";
                        dict["background-size"] = css.BackgroundSize ?? "auto";
                        dict["flex-direction"] = css.FlexDirection ?? "row";
                        dict["flex-wrap"] = css.FlexWrap ?? "nowrap";
                        dict["justify-content"] = css.JustifyContent ?? "flex-start";
                        dict["align-items"] = css.AlignItems ?? "stretch";
                        dict["align-self"] = "auto";
                        dict["flex-grow"] = css.FlexGrow?.ToString("0.##") ?? "0";
                        dict["flex-shrink"] = css.FlexShrink?.ToString("0.##") ?? "1";
                        dict["flex-basis"] = css.FlexBasis.HasValue ? css.FlexBasis.Value.ToString("0.##") + "px" : "auto";
                        dict["gap"] = css.Gap.HasValue ? css.Gap.Value.ToString("0.##") + "px" : "0";
                        dict["grid-template-columns"] = css.GridTemplateColumns ?? "none";
                        dict["grid-template-rows"] = css.GridTemplateRows ?? "none";
                        dict["pointer-events"] = "auto";
                        dict["cursor"] = "auto";
                        dict["list-style-type"] = "disc";
                        dict["table-layout"] = "auto";
                        dict["border-collapse"] = "separate";
                        dict["caption-side"] = "top";
                        dict["empty-cells"] = "show";
                    }
                    return JSValue.Marshal(dict);
                }
                catch { return JSValue.Marshal(new { }); }
            }
            public void postMessage(string msg) { }
            public void setImmediate(JSValue cb) { }
            
            public JSValue onpopstate
            {
                get { return _engine.OnPopState ?? JSValue.Null; }
                set { _engine.OnPopState = value; }
            }

            public JSValue onhashchange
            {
                get { return _engine.OnHashChange ?? JSValue.Null; }
                set { _engine.OnHashChange = value; }
            }

            public JSValue onload { get; set; } = JSValue.Null;
            public JSValue onunload { get; set; } = JSValue.Null;
            public JSValue onresize { get; set; } = JSValue.Null;
            public JSValue onscroll { get; set; } = JSValue.Null;
            public JSValue onerror { get; set; } = JSValue.Null;
        }

        private class HostDocument
        {
            private JavaScriptEngine _engine;
            public HostDocument(JavaScriptEngine engine) { _engine = engine; }
            public object getElementById(string id)
            {
                if (_engine._domRoot == null || string.IsNullOrEmpty(id)) return null;
                foreach (var n in _engine._domRoot.Descendants())
                {
                    if (n.Attr != null)
                    {
                        string v; if (n.Attr.TryGetValue("id", out v) && string.Equals(v, id, StringComparison.Ordinal)) return new JsDomElement(_engine, n);
                    }
                }
                return null;
            }
            public object[] getElementsByTagName(string tag)
            {
                if (string.IsNullOrEmpty(tag) || _engine._domRoot == null) return new object[0];
                var list = new List<object>();
                foreach (var n in _engine._domRoot.Descendants())
                    if (!n.IsText && string.Equals(n.Tag, tag, StringComparison.OrdinalIgnoreCase))
                        list.Add(new JsDomElement(_engine, n));
                return list.ToArray();
            }
            public object[] querySelectorAll(string selector)
            {
                if (_engine._domRoot == null || string.IsNullOrEmpty(selector)) return new object[0];
                var list = new List<object>();
                foreach (var n in _engine._domRoot.Descendants())
                {
                    if (n.IsText) continue;
                    if (MatchesSelector(n, selector))
                        list.Add(new JsDomElement(_engine, n));
                }
                return list.ToArray();
            }
            private bool MatchesSelector(LiteElement el, string selector)
            {
                if (string.IsNullOrEmpty(selector) || el == null) return false;
                return JsDocument.MatchesSimpleSelector(el, selector.Trim());
            }
            public JSValue createElement(string tag)
            {
                if (string.IsNullOrEmpty(tag)) return JSValue.Undefined;
                return JSValue.Marshal(new JsDomElement(_engine, new LiteElement(tag)));
            }
            public JSValue createElementNS(string ns, string tag)
            {
                if (string.IsNullOrEmpty(tag)) return JSValue.Undefined;
                return JSValue.Marshal(new JsDomElement(_engine, new LiteElement(tag)));
            }
            public string title
            {
                get { return _engine._pageTitle ?? string.Empty; }
                set { _engine._pageTitle = value; try { _engine._host?.SetTitle(value ?? string.Empty); } catch { /* swallow */ } }
            }
            public object body
            {
                get
                {
                    if (_engine._domRoot == null) return null;
                    var el = _engine._domRoot.FindById("body") ?? _engine._domRoot.QueryByTag("body").FirstOrDefault();
                    return el != null ? new JsDomElement(_engine, el) : null;
                }
            }
            public object documentElement
            {
                get { return _engine._domRoot != null ? new JsDomElement(_engine, _engine._domRoot) : null; }
            }
            public object head
            {
                get
                {
                    if (_engine._domRoot == null) return null;
                    var el = _engine._domRoot.QueryByTag("head").FirstOrDefault();
                    return el != null ? new JsDomElement(_engine, el) : null;
                }
            }
            public object[] styleSheets => new object[0];
            public object[] adoptedStyleSheets => new object[0];
            public string cookie => "";
            public string referrer => _engine._ctx?.BaseUri?.ToString() ?? "";
            public string readyState => "complete";
            public string characterSet => _engine.DetectedCharset ?? "UTF-8";
            public string charset => _engine.DetectedCharset ?? "UTF-8";
            public string inputEncoding => _engine.DetectedCharset ?? "UTF-8";
            public string contentType => "text/html";
            public string domain => _engine._ctx?.BaseUri?.Host ?? "";
            public string URL => _engine._ctx?.BaseUri?.ToString() ?? "";
            public string documentURI => _engine._ctx?.BaseUri?.ToString() ?? "";
            public string compatMode => "CSS1Compat";
            public object activeElement => null;
            public object fullscreenElement => null;
            public object pointerLockElement => null;
            public object scrollingElement => null;
            public object implementation => JSValue.Marshal(new { hasFeature = new Func<Arguments, JSValue>(a => JSValue.Marshal(true)) });
            public object scripts => new object[0];
            public object links => new object[0];
            public object images => new object[0];
            public object forms => new object[0];
            public object anchors => new object[0];
            public object embeds => new object[0];
            public object plugins => new object[0];
            public object applets => new object[0];
            public object fontFaceSet => JSValue.Marshal(new { ready = JSValue.Marshal(new { then = new Func<Arguments, JSValue>(a => JSValue.Undefined) }), status = JSValue.Marshal("loaded") });
            public object stylesheets => new object[0];
            public JSValue onreadystatechange { get; set; } = JSValue.Null;
            public JSValue onclick { get; set; } = JSValue.Null;
            public JSValue onkeydown { get; set; } = JSValue.Null;
            public JSValue onkeyup { get; set; } = JSValue.Null;
            public JSValue onmousedown { get; set; } = JSValue.Null;
            public JSValue onmouseup { get; set; } = JSValue.Null;
            public JSValue onmousemove { get; set; } = JSValue.Null;
            public JSValue onscroll { get; set; } = JSValue.Null;
            public JSValue onresize { get; set; } = JSValue.Null;
            public JSValue onload { get; set; } = JSValue.Null;
            public JSValue onfocus { get; set; } = JSValue.Null;
            public JSValue onblur { get; set; } = JSValue.Null;
            public JSValue oninput { get; set; } = JSValue.Null;
            public JSValue onsubmit { get; set; } = JSValue.Null;
            public JSValue onwheel { get; set; } = JSValue.Null;
            public JSValue ontouchstart { get; set; } = JSValue.Null;
            public JSValue ontouchend { get; set; } = JSValue.Null;
            public JSValue ontouchmove { get; set; } = JSValue.Null;
            public JSValue onpointerdown { get; set; } = JSValue.Null;
            public JSValue onpointerup { get; set; } = JSValue.Null;
            public JSValue onpointermove { get; set; } = JSValue.Null;
            public JSValue onvisibilitychange { get; set; } = JSValue.Null;
            public bool hidden => false;
            public bool fullscreenEnabled => false;
            public bool pictureInPictureEnabled => false;
            public string visibilityState => "visible";
            public string designMode => "off";
            public string dir => "ltr";
            public object elementFromPoint(double x, double y) { return null; }
            public object getSelection() { return null; }
            public void execCommand(string cmd) { }
            public bool queryCommandSupported(string cmd) { return false; }
            public bool queryCommandEnabled(string cmd) { return false; }
            public bool queryCommandState(string cmd) { return false; }
            public bool hasFocus() { return true; }
            public void open() { }
            public void close() { }
            public void write(string html) { }
            public void writeln(string html) { }
            public object importNode(object node, bool deep) { return null; }
            public object adoptNode(object node) { return null; }
            public void appendChild(object child)
            {
                var j = child as JsDomNodeBase;
                if (j == null || _engine._domRoot == null) return;
                _engine._domRoot.Children.Add(j._node);
                try { lock (_engine._mutationLock) { _engine._pendingMutations.Add(new InternalMutationRecord { Type = "childList", Target = _engine._domRoot, Added = new List<LiteElement> { j._node } }); } } catch { }
                _engine.RequestRepaint();
            }
            public void removeChild(object child)
            {
                var j = child as JsDomNodeBase;
                if (j == null || _engine._domRoot == null) return;
                _engine._domRoot.Children.Remove(j._node);
                try { lock (_engine._mutationLock) { _engine._pendingMutations.Add(new InternalMutationRecord { Type = "childList", Target = _engine._domRoot, Removed = new List<LiteElement> { j._node } }); } } catch { }
                _engine.RequestRepaint();
            }
            public void insertBefore(object newChild, object refChild)
            {
                var jNew = newChild as JsDomNodeBase;
                var jRef = refChild as JsDomNodeBase;
                if (jNew == null || _engine._domRoot == null) return;
                if (jRef == null) { _engine._domRoot.Children.Add(jNew._node); }
                else
                {
                    var idx = _engine._domRoot.Children.IndexOf(jRef._node);
                    if (idx < 0) _engine._domRoot.Children.Add(jNew._node);
                    else _engine._domRoot.Children.Insert(idx, jNew._node);
                }
                try { lock (_engine._mutationLock) { _engine._pendingMutations.Add(new InternalMutationRecord { Type = "childList", Target = _engine._domRoot, Added = new List<LiteElement> { jNew._node } }); } } catch { }
                _engine.RequestRepaint();
            }
            public object createDocumentFragment()
            {
                return new JsDomElement(_engine, new LiteElement("#document-fragment"));
            }
            public object createTextNode(string data)
            {
                var t = new LiteElement("#text") { Text = data ?? "" };
                return new JsDomText(_engine, t);
            }
            public object createComment(string data) { return null; }
            public object createAttribute(string name) { return null; }
            public object createEvent(string type) { return null; }
            public object createRange() { return null; }
            public object caretPositionFromPoint(double x, double y) { return null; }
            public object elementsFromPoint(double x, double y) { return new object[0]; }
            public object querySelector(string selector)
            {
                if (_engine._domRoot == null) return null;
                var doc = new JsDocument(_engine, _engine._domRoot);
                return doc.querySelector(selector);
            }
            public object getElementsByName(string name)
            {
                if (_engine._domRoot == null || string.IsNullOrEmpty(name)) return new object[0];
                var list = new List<object>();
                foreach (var n in _engine._domRoot.Descendants())
                {
                    if (n.IsText) continue;
                    if (n.Attr != null)
                    {
                        string v;
                        if (n.Attr.TryGetValue("name", out v) && string.Equals(v, name, StringComparison.Ordinal))
                            list.Add(new JsDomElement(_engine, n));
                    }
                }
                return list.ToArray();
            }
            public object[] getElementsByClassName(string className)
            {
                if (_engine._domRoot == null || string.IsNullOrEmpty(className)) return new object[0];
                var list = new List<object>();
                foreach (var n in _engine._domRoot.Descendants())
                {
                    if (n.IsText) continue;
                    string cls; if (n.Attr != null && n.Attr.TryGetValue("class", out cls) && !string.IsNullOrEmpty(cls))
                    {
                        var parts = cls.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < parts.Length; i++)
                            if (string.Equals(parts[i], className, StringComparison.Ordinal)) { list.Add(new JsDomElement(_engine, n)); break; }
                    }
                }
                return list.ToArray();
            }
            public object[] getElementsByTagNameNS(string ns, string tag) { return new object[0]; }
            public object getElementByIdNS(string ns, string id) { return null; }
            public void addEventListener(string type, JSValue callback, JSValue options)
            {
                if (string.IsNullOrEmpty(type) || callback == null || callback.IsNull) return;
                System.Collections.Generic.List<JSValue> list;
                if (!_engine._evtDocCallbacksByType.TryGetValue(type, out list) || list == null)
                { list = new System.Collections.Generic.List<JSValue>(); _engine._evtDocCallbacksByType[type] = list; }
                if (!list.Contains(callback)) list.Add(callback);
            }
            public void removeEventListener(string type, JSValue callback)
            {
                if (string.IsNullOrEmpty(type) || callback == null) return;
                System.Collections.Generic.List<JSValue> list;
                if (_engine._evtDocCallbacksByType.TryGetValue(type, out list) && list != null)
                    list.Remove(callback);
            }
            public bool dispatchEvent(JSValue e)
            {
                try
                {
                    if (e == null || e.IsNull) return false;
                    var type = "";
                    try { type = e.ValueType == JSValueType.Object ? (e["type"]?.ToString() ?? "") : e.ToString(); } catch { }
                    if (string.IsNullOrEmpty(type)) return false;
                    System.Collections.Generic.List<JSValue> list;
                    if (_engine._evtDocCallbacksByType.TryGetValue(type, out list) && list != null)
                    {
                        foreach (var cb in list.ToArray())
                        {
                            try { _engine.EnqueueMicrotask(() => { try { _engine.InvokeJsCallback(cb, type, "document"); } catch { } }); } catch { }
                        }
                    }
                }
                catch { }
                return true;
            }
        }

        private class HostConsole
        {
            private JavaScriptEngine _engine;
            public HostConsole(JavaScriptEngine engine) { _engine = engine; }
            public void log(string msg) { System.Diagnostics.Debug.WriteLine(msg); try { DevToolsLogger.Log(msg); } catch { } }
            public void warn(string msg) { System.Diagnostics.Debug.WriteLine("[WARN] " + msg); try { DevToolsLogger.Log("[WARN] " + msg); } catch { } }
            public void error(string msg) { System.Diagnostics.Debug.WriteLine("[ERROR] " + msg); try { DevToolsLogger.Log("[ERROR] " + msg); } catch { } }
            public void info(string msg) { System.Diagnostics.Debug.WriteLine("[INFO] " + msg); try { DevToolsLogger.Log("[INFO] " + msg); } catch { } }
        }

        private class HostNavigator
        {
            private JavaScriptEngine _engine;
            public HostNavigator(JavaScriptEngine engine) { _engine = engine; }
            public string userAgent => "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
            public string appVersion => "5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
            public string appName => "Netscape";
            public string appCodeName => "Mozilla";
            public string platform => "Win32";
            public string vendor => "Google Inc.";
            public string product => "Gecko";
            public string productSub => "20030107";
            public string vendorSub => "";
            public string language => "en-US";
            public string[] languages => new[] { "en-US", "en" };
            public bool onLine => true;
            public bool cookieEnabled => true;
            public string doNotTrack => "unspecified";
            public int hardwareConcurrency => 4;
            public int maxTouchPoints => 5;
            public bool javaEnabled() => false;
            public void sendBeacon(string url) { }
            public JSValue serviceWorker => JSValue.Undefined;
            public JSValue credentials => JSValue.Undefined;
            public JSValue permissions => JSValue.Undefined;
            public JSValue geolocation => JSValue.Undefined;
            public JSValue mediaDevices => JSValue.Undefined;
            public JSValue usb => JSValue.Undefined;
            public JSValue bluetooth => JSValue.Undefined;
            public JSValue hid => JSValue.Undefined;
            public JSValue serial => JSValue.Undefined;
            public JSValue xr => JSValue.Undefined;
            public JSValue clipboard => JSValue.Undefined;
            public JSValue connection => JSValue.Undefined;
            public JSValue wakeLock => JSValue.Undefined;
            public JSValue keyboard => JSValue.Undefined;
            public JSValue locks => JSValue.Undefined;
            public JSValue storage => JSValue.Undefined;
            public JSValue userActivation => JSValue.Undefined;
            public JSValue pdfViewerEnabled => JSValue.Undefined;
        }

        private class HostHistory
        {
            private JavaScriptEngine _engine;
            public HostHistory(JavaScriptEngine engine) { _engine = engine; }
            
            public void pushState(JSValue state, string title, string url) 
            { 
                System.Diagnostics.Debug.WriteLine($"[History] pushState url={url}");
                // In a real implementation, we would update the browser history stack
            }

            public void replaceState(JSValue state, string title, string url) 
            { 
                System.Diagnostics.Debug.WriteLine($"[History] replaceState url={url}");
            }

            public void go(int delta) 
            { 
                System.Diagnostics.Debug.WriteLine($"[History] go({delta})");
                TriggerPopState();
            }
            public void back() 
            { 
                System.Diagnostics.Debug.WriteLine("[History] back()");
                TriggerPopState();
            }
            public void forward() 
            { 
                System.Diagnostics.Debug.WriteLine("[History] forward()");
                TriggerPopState();
            }
            
            private void TriggerPopState()
            {
                try
                {
                    if (_engine.OnPopState != null && _engine.OnPopState.ValueType == JSValueType.Function)
                    {
                        var evt = JSValue.Marshal(new { state = JSValue.Null });
                        (_engine.OnPopState as Function)?.Call(JSValue.Undefined, new Arguments { evt });
                    }
                }
                catch { /* swallow */ }
            }

            public int length => 1;
            public JSValue state => JSValue.Null;
        }

        private class HostLocation
        {
            private JavaScriptEngine _engine;
            public HostLocation(JavaScriptEngine engine) { _engine = engine; }
            private Uri Uri => _engine._ctx?.BaseUri;
            public string href => Uri?.ToString() ?? "";
            public string origin => Uri != null ? (Uri.Scheme + "://" + Uri.Authority) : "";
            public string protocol => Uri?.Scheme ?? "";
            public string host => Uri?.Authority ?? "";
            public string hostname => Uri?.Host ?? "";
            public string port => Uri?.Port != -1 ? Uri.Port.ToString() : "";
            public string pathname => Uri?.AbsolutePath ?? "/";
            public string search => Uri?.Query ?? "";
            public string hash => "";
        }

        private class HostLocalStorage
        {
            private JavaScriptEngine _engine;
            private bool _session;
            public HostLocalStorage(JavaScriptEngine engine, bool session) { _engine = engine; _session = session; }
            public string getItem(string key) { return _engine.LocalStorageGet(key, _engine._ctx); }
            public void setItem(string key, string value) { _engine.LocalStorageSet(key, value, _engine._ctx); }
            public void removeItem(string key) { _engine.LocalStorageRemove(key, _engine._ctx); }
            public void clear() { _engine.LocalStorageClear(_engine._ctx); }
        }

        // MutationObserver host object — registered as NiL.JS function that creates instances
        private sealed class HostMutationObserver
        {
            private readonly JavaScriptEngine _engine;
            private readonly JSValue _callback;
            private JSValue _target;
            private bool _childList, _attributes, _subtree, _attributeOldValue;

            public HostMutationObserver(JavaScriptEngine engine, JSValue callback)
            {
                _engine = engine;
                _callback = callback;
            }

            public void observe(JSValue target, JSValue options)
            {
                try
                {
                    _target = target;
                    _childList = false; _attributes = false; _subtree = false; _attributeOldValue = false;
                    if (options.ValueType == JSValueType.Object)
                    {
                        var cv = options["childList"]; if (cv.ValueType == JSValueType.Boolean) _childList = (bool)cv;
                        var av = options["attributes"]; if (av.ValueType == JSValueType.Boolean) _attributes = (bool)av;
                        var sv = options["subtree"]; if (sv.ValueType == JSValueType.Boolean) _subtree = (bool)sv;
                        var ov = options["attributeOldValue"]; if (ov.ValueType == JSValueType.Boolean) _attributeOldValue = (bool)ov;
                    }
                    lock (_engine._mutationLock) { if (!_engine._activeObservers.Contains(this)) _engine._activeObservers.Add(this); }
                }
                catch { /* swallow */ }
            }

            public void disconnect()
            {
                lock (_engine._mutationLock) { _engine._activeObservers.Remove(this); }
            }

            public JSValue takeRecords()
            {
                lock (_engine._mutationLock)
                {
                    var copy = _engine._pendingMutations.ToList();
                    _engine._pendingMutations.Clear();
                    return _engine.InternalRecordsToJSArray(copy);
                }
            }

            internal JSValue Callback => _callback;
        }

        private sealed class JsFuncDef
        {
            public List<string> Params = new List<string>();
            public List<JsFuncParam> Parameters = new List<JsFuncParam>();
            public string Body;        // block body source
            public string Expr;        // expression body source (arrow)
        }

        // ------------------------------------------------------------------------------------
        // NiL.JS Integration
        // ------------------------------------------------------------------------------------

        private void _nilInit()
        {
            _niljsSafeEvalFailed = false;
            _nil = new GlobalContext();
            _evalContext = new Context(_nil);
            var _e = this;

            // Create host window once and reuse for all aliases
            var hostWindow = new HostWindow(this);

            // Core globals — window, self, globalThis, global
            _nil.DefineVariable("window").Assign(JSValue.Marshal(hostWindow));
            _nil.DefineVariable("self").Assign(JSValue.Marshal(hostWindow));
            // globalThis and global: use a real NiL.JS object so polyfills
            // like SystemJS can write properties (e.g. globalThis.System = ...).
            // SafeEval("this") returns the global scope object.
            JSValue globalScope;
            try { globalScope = SafeEval("this"); if (globalScope == null) globalScope = SafeEval("({})"); } catch { try { globalScope = SafeEval("({})"); } catch { globalScope = JSValue.Marshal(new Dictionary<string,object>()); } }
            _nil.DefineVariable("globalThis").Assign(globalScope);
            _nil.DefineVariable("global").Assign(globalScope);
            _nil.DefineVariable("top").Assign(JSValue.Marshal(hostWindow));
            _nil.DefineVariable("parent").Assign(JSValue.Marshal(hostWindow));

            // DOM globals
            _nil.DefineVariable("document").Assign(JSValue.Marshal(new HostDocument(this)));
            _nil.DefineVariable("console").Assign(JSValue.Marshal(new HostConsole(this)));
            _nil.DefineVariable("navigator").Assign(JSValue.Marshal(new HostNavigator(this)));
            _nil.DefineVariable("location").Assign(JSValue.Marshal(new HostLocation(this)));
            _nil.DefineVariable("history").Assign(JSValue.Marshal(new HostHistory(this)));
            _nil.DefineVariable("localStorage").Assign(JSValue.Marshal(new HostLocalStorage(this, false)));
            _nil.DefineVariable("sessionStorage").Assign(JSValue.Marshal(new HostLocalStorage(this, true)));

            // Window properties commonly checked via `in` operator
            _nil.DefineVariable("devicePixelRatio").Assign(JSValue.Marshal(DeviceDpr()));
            _nil.DefineVariable("innerWidth").Assign(JSValue.Marshal(1024));
            _nil.DefineVariable("innerHeight").Assign(JSValue.Marshal(768));
            _nil.DefineVariable("outerWidth").Assign(JSValue.Marshal(1024));
            _nil.DefineVariable("outerHeight").Assign(JSValue.Marshal(768));
            _nil.DefineVariable("pageXOffset").Assign(JSValue.Marshal(0));
            _nil.DefineVariable("pageYOffset").Assign(JSValue.Marshal(0));
            _nil.DefineVariable("scrollX").Assign(JSValue.Marshal(0));
            _nil.DefineVariable("scrollY").Assign(JSValue.Marshal(0));
            _nil.DefineVariable("screenX").Assign(JSValue.Marshal(0));
            _nil.DefineVariable("screenY").Assign(JSValue.Marshal(0));
            _nil.DefineVariable("closed").Assign(JSValue.Marshal(false));
            _nil.DefineVariable("name").Assign(JSValue.Marshal(""));
            _nil.DefineVariable("origin").Assign(JSValue.Marshal(_ctx?.BaseUri != null ? (_ctx.BaseUri.Scheme + "://" + _ctx.BaseUri.Authority) : "null"));

            // Event listener shims
            _nil.DefineVariable("addEventListener").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Undefined)));
            _nil.DefineVariable("removeEventListener").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Undefined)));
            _nil.DefineVariable("postMessage").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Undefined)));
            _nil.DefineVariable("dispatchEvent").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(false))));
            _nil.DefineVariable("getComputedStyle").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { }))));

            // Performance API
            _nil.DefineVariable("performance").Assign(JSValue.Marshal(new
            {
                now = new Func<double>(() => (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds),
                timeOrigin = (double)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds
            }));

            // URL / URLSearchParams stubs
            _nil.DefineVariable("URL").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                var obj = JSValue.Marshal(new { });
                if (args.Length > 0) obj["href"] = JSValue.Marshal(args[0].ToString());
                obj["toString"] = JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(obj["href"])));
                return obj;
            })));
            _nil.DefineVariable("URLSearchParams").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                var query = args.Length > 0 ? args[0].ToString() : "";
                var params_dict = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(query) && query.StartsWith("?")) query = query.Substring(1);
                foreach (var pair in query.Split('&'))
                {
                    var eq = pair.IndexOf('=');
                    if (eq >= 0)
                        params_dict[System.Uri.UnescapeDataString(pair.Substring(0, eq))] = System.Uri.UnescapeDataString(pair.Substring(eq + 1));
                    else if (!string.IsNullOrEmpty(pair))
                        params_dict[System.Uri.UnescapeDataString(pair)] = "";
                }
                var captured = params_dict;
                var sp = JSValue.Marshal(new { });
                sp["get"] = JSValue.Marshal(new Func<Arguments, JSValue>(a =>
                {
                    var key = a.Length > 0 ? a[0].ToString() : "";
                    return captured.TryGetValue(key, out var val) ? JSValue.Marshal(val) : JSValue.Null;
                }));
                sp["has"] = JSValue.Marshal(new Func<Arguments, JSValue>(a =>
                {
                    var key = a.Length > 0 ? a[0].ToString() : "";
                    return JSValue.Marshal(captured.ContainsKey(key));
                }));
                sp["set"] = JSValue.Marshal(new Func<Arguments, JSValue>(a =>
                {
                    if (a.Length >= 2) captured[a[0].ToString()] = a[1].ToString();
                    return JSValue.Undefined;
                }));
                sp["append"] = JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Undefined));
                sp["delete"] = JSValue.Marshal(new Func<Arguments, JSValue>(a =>
                {
                    if (a.Length > 0) captured.Remove(a[0].ToString());
                    return JSValue.Undefined;
                }));
                sp["toString"] = JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(string.Join("&", captured.Select(kv => $"{kv.Key}={kv.Value}")))));
                sp["entries"] = JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { })));
                sp["keys"] = JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { })));
                sp["values"] = JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { })));
                sp["forEach"] = JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Undefined));
                sp["size"] = JSValue.Marshal(captured.Count);
                return sp;
            })));

            // atob / btoa
            _nil.DefineVariable("atob").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a =>
            {
                try { return JSValue.Marshal(System.Convert.FromBase64String(a[0].ToString())); }
                catch { return JSValue.Undefined; }
            })));
            _nil.DefineVariable("btoa").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a =>
            {
                try { return JSValue.Marshal(System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(a[0].ToString()))); }
                catch { return JSValue.Undefined; }
            })));

            // requestAnimationFrame / cancelAnimationFrame
            _nil.DefineVariable("requestAnimationFrame").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                if (args.Length > 0 && args[0].ValueType == JSValueType.Function)
                {
                    var func = args[0] as Function;
                    var id = Interlocked.Increment(ref _nextTimerId);
                    EnqueueMacroTask(() => { try { func.Call(JSValue.Undefined, new Arguments { JSValue.Marshal((double)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds) }); } catch { } });
                    return JSValue.Marshal(id);
                }
                return JSValue.Marshal(0);
            })));
            _nil.DefineVariable("cancelAnimationFrame").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Undefined)));

            // crypto stub
            _nil.DefineVariable("crypto").Assign(JSValue.Marshal(new
            {
                getRandomValues = new Func<Arguments, JSValue>(a => a.Length > 0 ? a[0] : JSValue.Undefined),
                subtle = JSValue.Undefined
            }));

            // TextEncoder / TextDecoder stubs
            _nil.DefineVariable("TextEncoder").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { encode = new Func<Arguments, JSValue>(e => JSValue.Marshal(new byte[0])) }))));
            _nil.DefineVariable("TextDecoder").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { decode = new Func<Arguments, JSValue>(e => JSValue.Marshal("")) }))));

            // WebSocket stub (Vite HMR checks this)
            _nil.DefineVariable("WebSocket").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Undefined)));

            // Blob / File / FormData stubs
            _nil.DefineVariable("Blob").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { size = JSValue.Marshal(0), type = JSValue.Marshal("") }))));
            _nil.DefineVariable("File").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { name = JSValue.Marshal(""), size = JSValue.Marshal(0), type = JSValue.Marshal("") }))));
            _nil.DefineVariable("FormData").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { append = new Func<Arguments, JSValue>(e => JSValue.Undefined) }))));

            // AbortController / AbortSignal stubs
            _nil.DefineVariable("AbortController").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { signal = JSValue.Marshal(new { aborted = JSValue.Marshal(false) }), abort = new Func<Arguments, JSValue>(e => JSValue.Undefined) }))));

            // CustomEvent / Event stubs
            _nil.DefineVariable("Event").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a =>
            {
                var type = a.Length > 0 ? a[0].ToString() : "";
                var obj = JSValue.Marshal(new { });
                obj["type"] = JSValue.Marshal(type);
                obj["target"] = JSValue.Null;
                obj["currentTarget"] = JSValue.Null;
                obj["bubbles"] = JSValue.Marshal(false);
                obj["cancelable"] = JSValue.Marshal(false);
                obj["defaultPrevented"] = JSValue.Marshal(false);
                obj["preventDefault"] = JSValue.Marshal(new Func<Arguments, JSValue>(e => JSValue.Undefined));
                obj["stopPropagation"] = JSValue.Marshal(new Func<Arguments, JSValue>(e => JSValue.Undefined));
                obj["stopImmediatePropagation"] = JSValue.Marshal(new Func<Arguments, JSValue>(e => JSValue.Undefined));
                obj["composed"] = JSValue.Marshal(false);
                obj["timeStamp"] = JSValue.Marshal((double)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds);
                return obj;
            })));
            _nil.DefineVariable("CustomEvent").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a =>
            {
                var type = a.Length > 0 ? a[0].ToString() : "";
                var detail = a.Length > 1 ? a[1] : JSValue.Null;
                var obj = JSValue.Marshal(new { });
                obj["type"] = JSValue.Marshal(type);
                obj["detail"] = detail;
                obj["target"] = JSValue.Null;
                obj["currentTarget"] = JSValue.Null;
                var bubbles = false; var cancelable = false;
                try
                {
                    if (a.Length > 2 && a[2] != null && a[2].ValueType == JSValueType.Object)
                    {
                        var bv = a[2]["bubbles"]; if (bv != null && !bv.IsNull && bv.ValueType == JSValueType.Boolean) bubbles = (double)bv.Value != 0;
                        var cv = a[2]["cancelable"]; if (cv != null && !cv.IsNull && cv.ValueType == JSValueType.Boolean) cancelable = (double)cv.Value != 0;
                    }
                }
                catch { }
                obj["bubbles"] = JSValue.Marshal(bubbles);
                obj["cancelable"] = JSValue.Marshal(cancelable);
                obj["defaultPrevented"] = JSValue.Marshal(false);
                obj["preventDefault"] = JSValue.Marshal(new Func<Arguments, JSValue>(e => JSValue.Undefined));
                obj["stopPropagation"] = JSValue.Marshal(new Func<Arguments, JSValue>(e => JSValue.Undefined));
                obj["stopImmediatePropagation"] = JSValue.Marshal(new Func<Arguments, JSValue>(e => JSValue.Undefined));
                obj["composed"] = JSValue.Marshal(false);
                obj["timeStamp"] = JSValue.Marshal((double)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds);
                return obj;
            })));
            _nil.DefineVariable("MessageEvent").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { data = JSValue.Null, origin = JSValue.Marshal(""), source = JSValue.Null }))));

            // Image / HTMLImageElement stub
            _nil.DefineVariable("Image").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { width = JSValue.Marshal(0), height = JSValue.Marshal(0), src = JSValue.Marshal(""), onload = JSValue.Null, onerror = JSValue.Null }))));
            _nil.DefineVariable("HTMLImageElement").Assign(JSValue.Marshal(typeof(HostImage)));

            // DOMTokenList stub (classList constructor reference)
            _nil.DefineVariable("DOMTokenList").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { length = JSValue.Marshal(0), value = JSValue.Marshal(""), contains = new Func<Arguments, JSValue>(e => JSValue.Marshal(false)), add = new Func<Arguments, JSValue>(e => JSValue.Undefined), remove = new Func<Arguments, JSValue>(e => JSValue.Undefined), toggle = new Func<Arguments, JSValue>(e => JSValue.Marshal(false)) }))));

            // Element / HTMLElement / SVGElement proto stubs (instanceof checks)
            _nil.DefineVariable("Element").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { }))));
            _nil.DefineVariable("HTMLElement").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { }))));
            _nil.DefineVariable("SVGElement").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { }))));
            _nil.DefineVariable("Node").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { }))));
            _nil.DefineVariable("Text").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { }))));
            _nil.DefineVariable("DocumentFragment").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { nodeType = 11, nodeName = "#document-fragment" }))));
            _nil.DefineVariable("Comment").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { nodeType = 8, nodeName = "#comment" }))));
            _nil.DefineVariable("HTMLCollection").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { length = 0 }))));
            _nil.DefineVariable("HTMLDocument").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { }))));
            _nil.DefineVariable("Option").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new { value = JSValue.Marshal(""), text = JSValue.Marshal("") }))));

            // Expose fetch API (returns proper HostPromise — supports GET/POST/PUT/PATCH/DELETE)

            _nil.DefineVariable("fetch").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                if (args.Length == 0) return JSValue.Undefined;

                // Parse arguments: fetch(url) or fetch(url, options) or fetch(request)
                string url = null;
                string method = "GET";
                string body = null;
                var headers = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var arg0 = args[0];
                if (arg0 != null && arg0.ValueType == JSValueType.Object)
                {
                    // fetch(new Request(url, options)) or fetch({url, method, ...})
                    try { url = arg0["url"]?.ToString() ?? arg0["href"]?.ToString() ?? arg0.ToString(); } catch { url = arg0.ToString(); }
                    try { var m = arg0["method"]; if (m != null && !m.IsNull && m.ValueType != JSValueType.Undefined) method = m.ToString().ToUpperInvariant(); } catch { }
                    try { var b = arg0["body"]; if (b != null && !b.IsNull && b.ValueType != JSValueType.Undefined) body = b.ToString(); } catch { }
                    try
                    {
                        var h = arg0["headers"];
                        if (h != null && !h.IsNull && h.ValueType == JSValueType.Object)
                        {
                            // Headers object or plain object
                            var hStr = h.ToString();
                            if (!string.IsNullOrEmpty(hStr) && hStr != "[object Object]")
                            {
                                // Try to iterate as object
                                var hDict = SafeEval("(function(){var r={};try{var h=" + JsEscape(hStr, '\'') + ";for(var k in h)r[k]=h[k];}catch(e){}return r;})()");
                                if (hDict != null && hDict.ValueType == JSValueType.Object)
                                {
                                    // Can't easily iterate NiL object keys from C#, so parse the string
                                }
                            }
                            // Fallback: if headers is a Headers object with .get()
                            try { var ct = h["Content-Type"]; if (ct != null && !ct.IsNull) headers["Content-Type"] = ct.ToString(); } catch { }
                        }
                    }
                    catch { }
                }
                else
                {
                    url = arg0?.ToString();
                }

                // Parse options object (2nd arg)
                if (args.Length > 1 && args[1] != null && args[1].ValueType == JSValueType.Object)
                {
                    var opts = args[1];
                    try { var m = opts["method"]; if (m != null && !m.IsNull && m.ValueType != JSValueType.Undefined) method = m.ToString().ToUpperInvariant(); } catch { }
                    try { var b = opts["body"]; if (b != null && !b.IsNull && b.ValueType != JSValueType.Undefined) body = b.ToString(); } catch { }
                    try
                    {
                        var h = opts["headers"];
                        if (h != null && !h.IsNull && h.ValueType == JSValueType.Object)
                        {
                            try { var ct = h["Content-Type"]; if (ct != null && !ct.IsNull) headers["Content-Type"] = ct.ToString(); } catch { }
                            try { var ak = h["Accept"]; if (ak != null && !ak.IsNull) headers["Accept"] = ak.ToString(); } catch { }
                        }
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(url)) return JSValue.Undefined;
                var uri = Resolve(_ctx?.BaseUri, url);
                if (uri == null) return JSValue.Undefined;
                var capturedUri = uri;
                var capturedMethod = method;
                var capturedBody = body;
                var capturedHeaders = headers;

                // Create a pending HostPromise
                var promise = new JsMiniRunner.HostPromise { E = this, State = 0, Value = JsMiniRunner.JsVal.Null() };

                // Perform async fetch and settle the promise
                Task.Run(async () =>
                {
                    try
                    {
                        var result = await FetchWithMethodAsync(capturedUri, capturedMethod, capturedBody, capturedHeaders).ConfigureAwait(false);
                        var resp = JSValue.Marshal(new { });
                        resp["ok"] = JSValue.Marshal(result.Item2 >= 200 && result.Item2 < 300);
                        resp["status"] = JSValue.Marshal(result.Item2);
                        resp["statusText"] = JSValue.Marshal(result.Item2 >= 200 && result.Item2 < 300 ? "OK" : "Error");
                        resp["text"] = JSValue.Marshal(new Func<Arguments, JSValue>(b => JSValue.Marshal(result.Item1 ?? "")));
                        resp["json"] = JSValue.Marshal(new Func<Arguments, JSValue>(b =>
                        {
                            try { return SafeEval("JSON.parse(" + JsEscape(result.Item1 ?? "{}", '\'') + ")"); }
                            catch { return JSValue.Null; }
                        }));
                        resp["arrayBuffer"] = JSValue.Marshal(new Func<Arguments, JSValue>(b => JSValue.Marshal(new byte[0])));
                        resp["blob"] = resp["arrayBuffer"];
                        resp["clone"] = JSValue.Marshal(new Func<Arguments, JSValue>(b => resp));
                        // Real headers object
                        var respHeaders = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        if (result.Item3 != null)
                        {
                            foreach (var kvp in result.Item3)
                            {
                                if (!string.IsNullOrEmpty(kvp.Key))
                                    respHeaders[kvp.Key] = string.Join(", ", kvp.Value);
                            }
                        }
                        var headersObj = JSValue.Marshal(new { });
                        headersObj["get"] = JSValue.Marshal(new Func<Arguments, JSValue>(h =>
                        {
                            var name = h.Length > 0 ? h[0].ToString() : "";
                            string val;
                            return respHeaders.TryGetValue(name, out val) ? JSValue.Marshal(val) : JSValue.Null;
                        }));
                        headersObj["has"] = JSValue.Marshal(new Func<Arguments, JSValue>(h =>
                        {
                            var name = h.Length > 0 ? h[0].ToString() : "";
                            return JSValue.Marshal(respHeaders.ContainsKey(name));
                        }));
                        headersObj["entries"] = JSValue.Marshal(new Func<Arguments, JSValue>(h => JSValue.Marshal(new object[0])));
                        headersObj["keys"] = JSValue.Marshal(new Func<Arguments, JSValue>(h => JSValue.Marshal(new object[0])));
                        headersObj["values"] = JSValue.Marshal(new Func<Arguments, JSValue>(h => JSValue.Marshal(new object[0])));
                        resp["headers"] = headersObj;

                        promise.State = 1; // fulfilled
                        promise.Value = new JsMiniRunner.JsVal { Obj = resp };
                    }
                    catch (Exception ex)
                    {
                        var errResp = JSValue.Marshal(new { });
                        errResp["ok"] = JSValue.Marshal(false);
                        errResp["status"] = JSValue.Marshal(0);
                        errResp["statusText"] = JSValue.Marshal("Error");
                        errResp["text"] = JSValue.Marshal(new Func<Arguments, JSValue>(b => JSValue.Marshal(ex.Message ?? "")));
                        errResp["headers"] = JSValue.Marshal(new { get = new Func<Arguments, JSValue>(h => JSValue.Null) });

                        promise.State = -1; // rejected
                        promise.Value = new JsMiniRunner.JsVal { Obj = errResp };
                    }
                    finally
                    {
                        // Schedule promise handling as a microtask
                        EnqueueMicrotask(() => { try { ProcessPromiseHandlers(promise); } catch { /* swallow */ } });
                    }
                });

                // Return the promise object to JavaScript
                return JSValue.Marshal(promise);
            })));

            // Request constructor stub
            _nil.DefineVariable("Request").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                var url = args.Length > 0 ? args[0]?.ToString() ?? "" : "";
                var obj = JSValue.Marshal(new { });
                obj["url"] = JSValue.Marshal(url);
                obj["method"] = JSValue.Marshal(args.Length > 1 && args[1] != null ? (args[1]["method"]?.ToString() ?? "GET") : "GET");
                obj["headers"] = args.Length > 1 && args[1] != null ? args[1]["headers"] ?? JSValue.Marshal(new { }) : JSValue.Marshal(new { });
                obj["body"] = args.Length > 1 && args[1] != null ? args[1]["body"] ?? JSValue.Null : JSValue.Null;
                return obj;
            })));

            // Headers constructor
            _nil.DefineVariable("Headers").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                var store = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                // Initialize from object argument
                if (args.Length > 0 && args[0] != null && args[0].ValueType == JSValueType.Object)
                {
                    try
                    {
                        var init = args[0];
                        // Try common header names
                        string[] commonHeaders = { "Content-Type", "Accept", "Authorization", "X-Requested-With", "Cache-Control", "Pragma", "Origin", "Referer", "User-Agent" };
                        foreach (var h in commonHeaders)
                        {
                            try { var v = init[h]; if (v != null && !v.IsNull && v.ValueType != JSValueType.Undefined) store[h] = v.ToString(); } catch { }
                        }
                    }
                    catch { }
                }
                var headers = JSValue.Marshal(new { });
                headers["append"] = JSValue.Marshal(new Func<Arguments, JSValue>(a =>
                {
                    if (a.Length >= 2) { var k = a[0].ToString(); var v = a[1].ToString(); string existing; if (store.TryGetValue(k, out existing)) store[k] = existing + ", " + v; else store[k] = v; }
                    return JSValue.Undefined;
                }));
                headers["delete"] = JSValue.Marshal(new Func<Arguments, JSValue>(a =>
                {
                    if (a.Length >= 1) store.Remove(a[0].ToString());
                    return JSValue.Undefined;
                }));
                headers["get"] = JSValue.Marshal(new Func<Arguments, JSValue>(a =>
                {
                    string v; return a.Length >= 1 && store.TryGetValue(a[0].ToString(), out v) ? JSValue.Marshal(v) : JSValue.Null;
                }));
                headers["has"] = JSValue.Marshal(new Func<Arguments, JSValue>(a =>
                {
                    return a.Length >= 1 ? JSValue.Marshal(store.ContainsKey(a[0].ToString())) : JSValue.Marshal(false);
                }));
                headers["set"] = JSValue.Marshal(new Func<Arguments, JSValue>(a =>
                {
                    if (a.Length >= 2) store[a[0].ToString()] = a[1].ToString();
                    return JSValue.Undefined;
                }));
                headers["entries"] = JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new object[0])));
                headers["keys"] = JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new object[0])));
                headers["values"] = JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Marshal(new object[0])));
                headers["forEach"] = JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Undefined));
                return headers;
            })));

            // Expose Canvas API
            _nil.DefineVariable("HTMLCanvasElement").Assign(JSValue.Marshal(typeof(HostCanvas)));
            _nil.DefineVariable("Audio").Assign(JSValue.Marshal(typeof(HostAudio)));

            // Expose MutationObserver as factory function
            _nil.DefineVariable("MutationObserver").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                if (args.Length == 0) return JSValue.Undefined;
                return JSValue.Marshal(new HostMutationObserver(this, args[0]));
            })));

            // Expose Timers
            _nil.DefineVariable("setTimeout").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                if (args.Length == 0) return JSValue.Undefined;
                var func = args[0];
                var delay = 0;
                if (args.Length > 1) int.TryParse(args[1].ToString(), out delay);
                
                if (func.ValueType == JSValueType.Function)
                {
                    var id = Interlocked.Increment(ref _nextTimerId);
                    var t = new Timer(_ => 
                    {
                        try
                        {
                            EnqueueMacroTask(() => 
                            {
                                try { var fn = func as Function; if (fn != null) fn.Call(JSValue.Undefined, new Arguments()); } catch { /* swallow */ }
                            });
                        }
                        catch { /* swallow */ }
                    }, null, delay, Timeout.Infinite);
                    lock (_timers) _timers[id] = t;
                    return JSValue.Marshal(id);
                }
                else
                {
                    var code = func.ToString();
                    return JSValue.Marshal(ScheduleTimeout(code, delay));
                }
            })));
            
            _nil.DefineVariable("clearTimeout").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                var id = 0;
                if (args.Length > 0) int.TryParse(args[0].ToString(), out id);
                ClearTimeout(id);
                return JSValue.Undefined;
            })));

            _nil.DefineVariable("setInterval").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                if (args.Length == 0) return JSValue.Undefined;
                var func = args[0];
                var delay = 0;
                if (args.Length > 1) int.TryParse(args[1].ToString(), out delay);
                
                 if (func.ValueType == JSValueType.Function)
                {
                    var id = Interlocked.Increment(ref _nextTimerId);
                    var t = new Timer(_ => 
                    {
                        try
                        {
                            EnqueueMacroTask(() => 
                            {
                                try { var fn = func as Function; if (fn != null) fn.Call(JSValue.Undefined, new Arguments()); } catch { /* swallow */ }
                            });
                        }
                        catch { /* swallow */ }
                    }, null, delay, delay);
                    lock (_timers) _timers[id] = t;
                    return JSValue.Marshal(id);
                }
                else
                {
                    return JSValue.Marshal(ScheduleInterval(func.ToString(), delay));
                }
            })));
            
            _nil.DefineVariable("clearInterval").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                var id = 0;
                if (args.Length > 0) int.TryParse(args[0].ToString(), out id);
                ClearTimeout(id);
                return JSValue.Undefined;
            })));

            // === Missing ES feature stubs (prevent InvalidOperationException on modern sites) ===

            // WeakMap — dictionary keyed by object identity (RuntimeHelpers.GetHashCode)
            _nil.DefineVariable("WeakMap").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                var store = new Dictionary<int, JSValue>();
                var keyList = new System.Runtime.CompilerServices.ConditionalWeakTable<object, JSValue>();
                var reverseMap = new Dictionary<int, object>();
                return JSValue.Marshal(new
                {
                    set = new Func<Arguments, JSValue>(a =>
                    {
                        try
                        {
                            if (a.Length >= 2 && a[0] != null && !a[0].IsNull && a[0].ValueType == JSValueType.Object)
                            {
                                var key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a[0]);
                                store[key] = a[1];
                                reverseMap[key] = a[0];
                            }
                        }
                        catch { /* swallow */ }
                        return JSValue.Undefined;
                    }),
                    get = new Func<Arguments, JSValue>(a =>
                    {
                        try
                        {
                            if (a.Length >= 1 && a[0] != null && !a[0].IsNull && a[0].ValueType == JSValueType.Object)
                            {
                                var key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a[0]);
                                JSValue v;
                                if (store.TryGetValue(key, out v)) return v;
                            }
                        }
                        catch { /* swallow */ }
                        return JSValue.Undefined;
                    }),
                    has = new Func<Arguments, JSValue>(a =>
                    {
                        try
                        {
                            if (a.Length >= 1 && a[0] != null && !a[0].IsNull && a[0].ValueType == JSValueType.Object)
                            {
                                var key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a[0]);
                                return JSValue.Marshal(store.ContainsKey(key));
                            }
                        }
                        catch { /* swallow */ }
                        return JSValue.Marshal(false);
                    }),
                    delete = new Func<Arguments, JSValue>(a =>
                    {
                        try
                        {
                            if (a.Length >= 1 && a[0] != null && !a[0].IsNull && a[0].ValueType == JSValueType.Object)
                            {
                                var key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a[0]);
                                reverseMap.Remove(key);
                                return JSValue.Marshal(store.Remove(key));
                            }
                        }
                        catch { /* swallow */ }
                        return JSValue.Marshal(false);
                    })
                });
            })));

            // WeakSet — set of objects keyed by identity
            _nil.DefineVariable("WeakSet").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                var store = new HashSet<int>();
                var reverseMap = new Dictionary<int, object>();
                return JSValue.Marshal(new
                {
                    add = new Func<Arguments, JSValue>(a =>
                    {
                        try
                        {
                            if (a.Length >= 1 && a[0] != null && !a[0].IsNull && a[0].ValueType == JSValueType.Object)
                            {
                                var key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a[0]);
                                store.Add(key);
                                reverseMap[key] = a[0];
                            }
                        }
                        catch { /* swallow */ }
                        return JSValue.Undefined;
                    }),
                    has = new Func<Arguments, JSValue>(a =>
                    {
                        try
                        {
                            if (a.Length >= 1 && a[0] != null && !a[0].IsNull && a[0].ValueType == JSValueType.Object)
                            {
                                var key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a[0]);
                                return JSValue.Marshal(store.Contains(key));
                            }
                        }
                        catch { /* swallow */ }
                        return JSValue.Marshal(false);
                    }),
                    delete = new Func<Arguments, JSValue>(a =>
                    {
                        try
                        {
                            if (a.Length >= 1 && a[0] != null && !a[0].IsNull && a[0].ValueType == JSValueType.Object)
                            {
                                var key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a[0]);
                                reverseMap.Remove(key);
                                return JSValue.Marshal(store.Remove(key));
                            }
                        }
                        catch { /* swallow */ }
                        return JSValue.Marshal(false);
                    })
                });
            })));

            // Proxy — stub that returns the target (pass-through)
            _nil.DefineVariable("Proxy").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                if (args.Length >= 1) return args[0];
                return JSValue.Undefined;
            })));

            // Reflect — minimal stub with common methods
            _nil.DefineVariable("Reflect").Assign(JSValue.Marshal(new Dictionary<string, object>
            {
                { "get", new Func<Arguments, JSValue>(a => { try { if (a.Length >= 2) return a[1]; } catch { } return JSValue.Undefined; }) },
                { "set", new Func<Arguments, JSValue>(a => JSValue.Marshal(true)) },
                { "has", new Func<Arguments, JSValue>(a => JSValue.Undefined) },
                { "deleteProperty", new Func<Arguments, JSValue>(a => JSValue.Marshal(true)) },
                { "getPrototypeOf", new Func<Arguments, JSValue>(a => { try { if (a.Length >= 1) return a[0]; } catch { } return JSValue.Undefined; }) },
                { "setPrototypeOf", new Func<Arguments, JSValue>(a => JSValue.Marshal(true)) },
                { "isExtensible", new Func<Arguments, JSValue>(a => JSValue.Marshal(true)) },
                { "preventExtensions", new Func<Arguments, JSValue>(a => JSValue.Marshal(true)) },
                { "ownKeys", new Func<Arguments, JSValue>(a => JSValue.Marshal(new object[0])) },
                { "defineProperty", new Func<Arguments, JSValue>(a => JSValue.Marshal(true)) },
                { "getOwnPropertyDescriptor", new Func<Arguments, JSValue>(a => JSValue.Undefined) },
                { "construct", new Func<Arguments, JSValue>(a => { try { if (a.Length >= 1) return a[0]; } catch { } return JSValue.Undefined; }) },
                { "apply", new Func<Arguments, JSValue>(a => { try { if (a.Length >= 1 && a[0].ValueType == JSValueType.Function) return (a[0] as Function).Call(JSValue.Undefined, a.Length >= 3 ? (a[2] as Arguments ?? new Arguments()) : new Arguments()); } catch { } return JSValue.Undefined; }) }
            }));

            // Intl — minimal stub (prevents "Intl is not defined" crashes)
            _nil.DefineVariable("Intl").Assign(JSValue.Marshal(new Dictionary<string, object>
            {
                { "DateTimeFormat", new Func<Arguments, JSValue>(a => JSValue.Marshal(new Dictionary<string, object> { { "format", new Func<Arguments, JSValue>(a2 => JSValue.Marshal("")) }, { "resolvedOptions", new Func<Arguments, JSValue>(a2 => JSValue.Marshal(new Dictionary<string, object> { { "locale", "" }, { "timeZone", "" } })) } })) },
                { "NumberFormat", new Func<Arguments, JSValue>(a => JSValue.Marshal(new Dictionary<string, object> { { "format", new Func<Arguments, JSValue>(a2 => JSValue.Marshal("0")) } })) },
                { "Collator", new Func<Arguments, JSValue>(a => JSValue.Marshal(new Dictionary<string, object> { { "compare", new Func<Arguments, JSValue>(a2 => JSValue.Marshal(0)) } })) }
            }));

            // IntersectionObserver stub
            _nil.DefineVariable("IntersectionObserver").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                return JSValue.Marshal(new
                {
                    observe = new Func<Arguments, JSValue>(a => JSValue.Undefined),
                    unobserve = new Func<Arguments, JSValue>(a => JSValue.Undefined),
                    disconnect = new Func<Arguments, JSValue>(a => JSValue.Undefined),
                    root = JSValue.Null,
                    rootMargin = "",
                    thresholds = new object[0]
                });
            })));

            // ResizeObserver stub
            _nil.DefineVariable("ResizeObserver").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                return JSValue.Marshal(new
                {
                    observe = new Func<Arguments, JSValue>(a => JSValue.Undefined),
                    unobserve = new Func<Arguments, JSValue>(a => JSValue.Undefined),
                    disconnect = new Func<Arguments, JSValue>(a => JSValue.Undefined)
                });
            })));

            // WeakRef stub — prevents JSException when modern JS tries to use WeakRef.
            // Returns a simple { deref: () => target } wrapper.
            _nil.DefineVariable("WeakRef").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                var target = args.Length >= 1 ? args[0] : JSValue.Undefined;
                return JSValue.Marshal(new
                {
                    deref = new Func<Arguments, JSValue>(a => target)
                });
            })));

            // __sysImport — standalone host function for SystemJS dynamic loading.
            // We do NOT pre-define System here — let the Vite polyfill create it
            // as a real JS object (so .register, .resolve etc. work).
            // System.import calls are intercepted in RunScriptsAsync and forwarded
            // to __sysImport (bypasses polyfill's DOM-based import).
            // NOTE: synchronous fetch — blocks JS thread so System.register and
            // module execute() complete before Phase3 finishes. Otherwise SystemJS
            // never triggers execute() because no script onload event fires.
            var sysEngine = this;
            // __diagLog — host function that writes to Debug.WriteLine (bypasses console.log regex limitation)
            _nil.DefineVariable("__diagLog").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                if (args.Length > 0 && args[0] != null)
                {
                    var msg = args[0].ToString();
                    try { System.Diagnostics.Debug.WriteLine(msg); DevToolsLogger.Log(msg); } catch { }
                }
                return JSValue.Undefined;
            })));
            // __storeData — host function to pass extracted JSON from JS to C# during script execution
            _storedData.Clear();
            _nil.DefineVariable("__storeData").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                if (args.Length >= 2 && args[0] != null && args[1] != null)
                {
                    var key = args[0].ToString();
                    var val = args[1].ToString();
                    try { _storedData[key] = val; } catch { }
                }
                return JSValue.Undefined;
            })));
            _nil.DefineVariable("__sysImport").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                if (args.Length == 0) return JSValue.Undefined;
                var url = args[0]?.ToString();
                if (string.IsNullOrWhiteSpace(url)) return JSValue.Undefined;
                var resolved = JavaScriptEngine.Resolve(sysEngine._ctx?.BaseUri, url);
                if (resolved == null) return JSValue.Undefined;
                try { System.Diagnostics.Debug.WriteLine("[DIAG:System.import] fetching " + resolved); DevToolsLogger.Log("[DIAG:System.import] fetching " + resolved); } catch { }
                try
                {
                    var txt = sysEngine.FetchScriptStringAsync(resolved, sysEngine._ctx?.BaseUri).GetAwaiter().GetResult();
                    if (!string.IsNullOrEmpty(txt))
                    {
                        try { System.Diagnostics.Debug.WriteLine("[DIAG:System.import] OK: " + txt.Length + " bytes from " + resolved); DevToolsLogger.Log("[DIAG:System.import] OK: " + txt.Length + " bytes from " + resolved); } catch { }
                        // Search chunk for embedded graph data (nodes/links)
                        try { SearchChunkForGraphData(txt); } catch { }
                        // Use SafeEval directly (no IIFE wrapping) to create System
                        // in the global context, then run the chunk — avoids RunInline's
                        // IIFE scope isolation issue with globalThis.
                        var sysInject = @"
if (typeof __diagLog === 'function') { __diagLog('[DIAG:SYS] __diagLog accessible'); }
// Patch setTimeout/requestAnimationFrame synchronously so async callbacks fire immediately
(function(){
    if (typeof setTimeout === 'function') {
        var origST = setTimeout;
        setTimeout = function(fn, d) { if (typeof fn === 'function') { try { fn(); } catch(e) { if (typeof __diagLog === 'function') __diagLog('[DIAG:TIMER] sync setTimeout err: ' + ((e&&e.message)||typeof e)); } } return 0; };
    }
    if (typeof requestAnimationFrame === 'function') {
        requestAnimationFrame = function(fn) { if (typeof fn === 'function') { try { fn(); } catch(e) {} } return 0; };
    }
})();
var __sys = { _reg: { _entries: {}, _modId: 0 } };
(function(r){
    r.set = function(k,v) { this._entries[k] = v; };
    r.get = function(k) { return this._entries[k]; };
    r.forEach = function(fn) { for(var k in this._entries) fn(this._entries[k], k); };
})(__sys._reg);
__sys.registry = __sys._reg;
__sys.register = function(deps, declare) {
    if (typeof __diagLog === 'function') { __diagLog('[DIAG:SYS] register ENTERED typeof this=' + typeof this + ' has_reg=' + (typeof this._reg !== 'undefined')); }
    try {
        var r = this._reg;
        var url = 'mod:' + (++r._modId);
        var wrappedDeclare = function(_export, _ctx) {
            if (typeof __diagLog === 'function') { __diagLog('[DIAG:MOD] declare called url=' + url); }
            var declared = declare(_export, _ctx);
            if (declared && typeof declared.execute === 'function') {
                var origExec = declared.execute;
                declared.execute = function() {
                    if (typeof __diagLog === 'function') { __diagLog('[DIAG:MOD] execute START url=' + url); }
                    try {
                        var ret = origExec();
                        if (typeof __diagLog === 'function') { __diagLog('[DIAG:MOD] execute END url=' + url + ' ret=' + (ret === undefined ? 'undefined' : typeof ret)); }
                        return ret;
                    } catch(e) {
                        if (typeof __diagLog === 'function') { __diagLog('[DIAG:MOD] execute FAIL url=' + url + ' err=' + ((e && e.message) || typeof e)); }
                        throw e;
                    }
                };
            }
            return declared;
        };
        r.set(url, { deps: deps || [], declare: wrappedDeclare, execute: null, url: url });
        if (typeof __diagLog === 'function') { __diagLog('[DIAG:SYS] register OK url=' + url); }
    } catch(e) {
        if (typeof __diagLog === 'function') { __diagLog('[DIAG:SYS] register ERROR: ' + (e.message||e)); }
    }
};
__sys.import = function(url) { return typeof __sysImport === 'function' ? __sysImport(url) : undefined; };
__sys.resolve = function() { return ''; };
__sys.instantiate = function() { return undefined; };
var System = __sys;
globalThis.System = __sys;
";
                        try { System.Diagnostics.Debug.WriteLine("[DIAG:SYS] creating System via SafeEval (" + sysInject.Length + " bytes)..."); } catch { }
                        try { sysEngine.SafeEval(sysInject); System.Diagnostics.Debug.WriteLine("[DIAG:SYS] SafeEval(sysInject) OK"); } catch (Exception ex) { try { System.Diagnostics.Debug.WriteLine("[DIAG:SYS] SafeEval(sysInject) exception: " + ex.GetType().Name + " - " + ex.Message); } catch { } }
                        // Also register System as a bare global identifier on _nil so that
                        // chunk code calling System.register(...) resolves correctly.
                        // NiL.JS does not make globalThis.System properties visible as bare identifiers.
                        try { var sysVal = SafeEval("globalThis.System"); if (sysVal != null && !sysVal.IsNull && sysVal.ValueType != NiL.JS.Core.JSValueType.Undefined) { _nil.DefineVariable("System").Assign(sysVal); System.Diagnostics.Debug.WriteLine("[DIAG:SYS] System registered on _nil"); DevToolsLogger.Log("[DIAG:SYS] System registered on _nil"); } } catch (Exception ex2) { try { System.Diagnostics.Debug.WriteLine("[DIAG:SYS] _nil registration failed: " + ex2.Message); } catch { } }
                        // Verify System exists (both via globalThis and bare identifier)
                        try { var sysOk = sysEngine.SafeEval("typeof globalThis.System.register === 'function' ? 'fn' : 'no'"); System.Diagnostics.Debug.WriteLine("[DIAG:SYS] System.register = " + (sysOk?.ToString() ?? "null")); } catch { }
                        // Mark System with a test prop to detect context reset
                        try { sysEngine.SafeEval("globalThis.System.__ctxTest = 42;"); } catch { }
                        // DIRECT TEST: can we call System.register with a minimal module?
                        try { sysEngine.SafeEval(
                            "(function(){" +
                            "var S=globalThis.System;" +
                            "if(typeof __diagLog==='function')__diagLog('[DIAG:SYS] direct_test before register');" +
                            "S.register([],function(e,ctx){" +
                            "if(typeof __diagLog==='function')__diagLog('[DIAG:SYS] direct_test declare called');" +
                            "return {execute:function(){" +
                            "if(typeof __diagLog==='function')__diagLog('[DIAG:SYS] direct_test execute called');" +
                            "}};" +
                            "});" +
                            "if(typeof __diagLog==='function')__diagLog('[DIAG:SYS] direct_test after register');" +
                            "})();"
                        ); } catch (Exception ex) { try { System.Diagnostics.Debug.WriteLine("[DIAG:SYS] direct_test exception: " + ex.GetType().Name + " - " + ex.Message); } catch { } }
                        // Check registry after direct test
                        try { var rcAfter = sysEngine.SafeEval("(function(){var r=globalThis.System._reg;if(!r||!r._entries)return'no-reg';var c=0;for(var k in r._entries)c++;return c+'';})()"); System.Diagnostics.Debug.WriteLine("[DIAG:SYS] direct_test _entries count=" + (rcAfter?.ToString() ?? "null")); } catch { }
                        try { var modIdAfter = sysEngine.SafeEval("globalThis.System._reg?._modId+'' || 'none'"); System.Diagnostics.Debug.WriteLine("[DIAG:SYS] direct_test _modId=" + (modIdAfter?.ToString() ?? "null")); } catch { }
                        try { System.Diagnostics.Debug.WriteLine("[DIAG:SYS] running chunk via SafeEvalFast (" + txt.Length + " bytes)..."); } catch { }
                        // Leak inline data from the chunk to window.* BEFORE splitting, so
                        // that even if the SafeEvalFast fails, the data can be extracted post-hoc.
                        // Note: chunk uses bare `Kf={entries:[...]}` without `var` (minifier chain).
                        // FIX: Use globalThis.* instead of window.* — HostWindow is a C# wrapper
                        // that silently drops dynamic property assignments. globalThis is a native
                        // NiL.JS object that supports arbitrary property storage.
                        try {
                            if (txt.Contains("Kf={entries:[") && txt.Contains("wf={collections:[")) {
                                System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Leaking chunk data vars to globalThis.* (sysImport path)");
                                txt = txt.Replace("Kf={entries:[", "globalThis.Kf={entries:[");
                                txt = txt.Replace("wf={collections:[", "globalThis.wf={collections:[");
                                txt = txt.Replace("Cf={stories:[", "globalThis.Cf={stories:[");
                                int vfIdx2 = txt.IndexOf("vf=[", StringComparison.Ordinal);
                                if (vfIdx2 >= 0) { txt = txt.Substring(0, vfIdx2) + "globalThis.vf=" + txt.Substring(vfIdx2 + 3); }
                                System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Data leak complete (sysImport path)");
                            }
                        } catch (Exception leakEx) { try { System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Data leak error: " + leakEx.GetType().Name + " - " + leakEx.Message); } catch { } }
                        // ---------------------------------------------------------------
                        // DATA EXTRACTION: NiL.JS cannot evaluate the 589KB chunk at all
                        // (SafeEval and SafeEvalFast both fail silently). So extract the
                        // inline data from the C# string using brace-counting and inject
                        // it into NiL.JS via small SafeEval calls, bypassing the chunk eval.
                        // ---------------------------------------------------------------
                        try {
                            System.Diagnostics.Debug.WriteLine("[DIAG:DATA] Starting extraction from chunk text...");
                            // Inline JS value extractor: finds prefix="globalThis.varName=",
                            // "window.varName=", or bare "varName=" and walks braces/brackets
                            // to the matching closer, skipping strings.
                            Func<string, string, string> extractValue = (text, varName) => {
                                int idx = text.IndexOf("globalThis." + varName + "=");
                                int start;
                                if (idx >= 0) { start = idx + varName.Length + 12; }
                                else {
                                    idx = text.IndexOf("window." + varName + "=");
                                    if (idx >= 0) { start = idx + varName.Length + 8; }
                                    else {
                                        idx = text.IndexOf(varName + "=");
                                        if (idx < 0) return null;
                                        start = idx + varName.Length + 1;
                                    }
                                }
                                if (start >= text.Length) return null;
                                char first = text[start];
                                if (first != '{' && first != '[') return null;
                                int depth = 1, pos = start + 1;
                                while (pos < text.Length && depth > 0) {
                                    char c = text[pos];
                                    if (c == '"') { pos++; while (pos < text.Length && text[pos] != '"') { if (text[pos] == '\\') pos++; pos++; } if (pos < text.Length) pos++; continue; }
                                    if (c == '\'') { pos++; while (pos < text.Length && text[pos] != '\'') { if (text[pos] == '\\') pos++; pos++; } if (pos < text.Length) pos++; continue; }
                                    if (c == '{' || c == '[') depth++;
                                    else if (c == '}' || c == ']') { depth--; if (depth == 0) return text.Substring(start, pos - start + 1); }
                                    pos++;
                                }
                                return null;
                            };
                            string kfRaw = extractValue(txt, "Kf");
                            string wfRaw = extractValue(txt, "wf");
                            string cfRaw = extractValue(txt, "Cf");
                            string vfRaw = extractValue(txt, "vf");
                            System.Diagnostics.Debug.WriteLine("[DIAG:DATA] Extracted: Kf=" + (kfRaw != null ? (kfRaw.Length + "B") : "MISS") +
                                " wf=" + (wfRaw != null ? (wfRaw.Length + "B") : "MISS") +
                                " Cf=" + (cfRaw != null ? (cfRaw.Length + "B") : "MISS") +
                                " vf=" + (vfRaw != null ? (vfRaw.Length + "B") : "MISS"));
                            // Inject data into NiL.JS via SafeEval using globalThis (native JS object)
                            // instead of window (C# HostWindow wrapper that drops dynamic props).
                            if (kfRaw != null) { sysEngine.SafeEval("globalThis.Kf=" + kfRaw + ";"); System.Diagnostics.Debug.WriteLine("[DIAG:DATA] Kf injected"); }
                            if (wfRaw != null) { sysEngine.SafeEval("globalThis.wf=" + wfRaw + ";"); System.Diagnostics.Debug.WriteLine("[DIAG:DATA] wf injected"); }
                            if (cfRaw != null) { sysEngine.SafeEval("globalThis.Cf=" + cfRaw + ";"); System.Diagnostics.Debug.WriteLine("[DIAG:DATA] Cf injected"); }
                            if (vfRaw != null) { sysEngine.SafeEval("globalThis.vf=" + vfRaw + ";"); System.Diagnostics.Debug.WriteLine("[DIAG:DATA] vf injected"); }
                            // Run sync graph builder once data is in NiL.JS
                            if (kfRaw != null && wfRaw != null) {
                                string graphCode =
                                "try{(function(){var __f=globalThis.Kf.entries;if(!__f)return;" +
                                "var __xf=globalThis.wf.collections.map(function(c){return{id:c.id,name:c.title,description:c.blurb,theme:c.grouping,keywords:c.keywords}});" +
                                "var __Pf={};__xf.forEach(function(c){__Pf[c.id]=c});" +
                                "var __nodes=[],__links=[],__conns={};" +
                                "__f.forEach(function(e){__nodes.push({id:e.id,name:e.title,start:e.start,file:e.file,type:'entry'});" +
                                "(e.collections||[]).forEach(function(cId){if(cId!=='C0030'&&cId[0]!=='K'&&__Pf[cId]){" +
                                "__links.push({source:e,target:__Pf[cId]});if(!__conns[e.id])__conns[e.id]=[];__conns[e.id].push(cId);" +
                                "if(!__conns[cId])__conns[cId]=[];__conns[cId].push(e.id)}})});" +
                                "__xf.forEach(function(e){__nodes.push({id:e.id,name:e.title,theme:e.theme,type:'collection'})});" +
                                "globalThis.__graphData={nodes:__nodes,links:__links,nodeConnections:__conns};" +
                                "if(typeof globalThis!=='undefined'&&globalThis.System)globalThis.System.__graphData=globalThis.__graphData;" +
                                "globalThis.__entries=__f;globalThis.__collections=globalThis.wf.collections;globalThis.__xf=__xf;globalThis.__Pf=__Pf;" +
                                "if(typeof globalThis.Cf!=='undefined'){globalThis.__stories=globalThis.Cf.stories;globalThis.__entriesWithDates=__f.filter(function(e){return e.start&&e.start.length>=5})}" +
                                "if(typeof globalThis.vf!=='undefined')globalThis.__keywords=globalThis.vf;" +
                                "if(typeof __diagLog==='function')__diagLog('[DIAG:DATA] graphBuilder done nodes='+__nodes.length+' links='+__links.length);" +
                                "})();}catch(e){try{if(typeof __diagLog==='function')__diagLog('[DIAG:DATA] graphErr '+(e.message||e))}catch(ee){}}";
                                sysEngine.SafeEval(graphCode);
                                System.Diagnostics.Debug.WriteLine("[DIAG:DATA] Graph builder executed");
                            }
                            // C#-only data storage: Extract entries/collections/stories from the
                            // raw JS literals and store directly in _storedData. This bypasses
                            // NiL.JS JSON.stringify which times out on large arrays (722 entries).
                            try {
                                Func<string, string, string> extractSubArray = (objLiteral, key) => {
                                    var keyStr = key + ":[";
                                    int idx = objLiteral.IndexOf(keyStr);
                                    if (idx < 0) return null;
                                    int start = idx + keyStr.Length - 1;
                                    int depth = 1, pos = start + 1;
                                    while (pos < objLiteral.Length && depth > 0) {
                                        char c = objLiteral[pos];
                                        if (c == '"') { pos++; while (pos < objLiteral.Length && objLiteral[pos] != '"') { if (objLiteral[pos] == '\\') pos++; pos++; } if (pos < objLiteral.Length) pos++; continue; }
                                        if (c == '\'') { pos++; while (pos < objLiteral.Length && objLiteral[pos] != '\'') { if (objLiteral[pos] == '\\') pos++; pos++; } if (pos < objLiteral.Length) pos++; continue; }
                                        if (c == '{' || c == '[') depth++;
                                        else if (c == '}' || c == ']') { depth--; if (depth == 0) return objLiteral.Substring(start, pos - start + 1); }
                                        pos++;
                                    }
                                    return null;
                                };
                                // Quote unquoted JS keys: turns {id:"x"} into {"id":"x"}
                                Func<string, string> quoteJsKeys = (s) => {
                                    if (string.IsNullOrEmpty(s)) return s;
                                    var sb = new System.Text.StringBuilder(s.Length + s.Length / 4);
                                    bool inStr = false; char strCh = '\0';
                                    for (int i = 0; i < s.Length; i++) {
                                        char c = s[i];
                                        if (inStr) {
                                            sb.Append(c);
                                            if (c == '\\') { i++; if (i < s.Length) sb.Append(s[i]); }
                                            else if (c == strCh) inStr = false;
                                            continue;
                                        }
                                        if (c == '"' || c == '\'') { inStr = true; strCh = c; sb.Append('"'); continue; }
                                        if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || c == '$') {
                                            int start = i;
                                            while (i < s.Length && ((s[i] >= 'a' && s[i] <= 'z') || (s[i] >= 'A' && s[i] <= 'Z') || (s[i] >= '0' && s[i] <= '9') || s[i] == '_' || s[i] == '$')) i++;
                                            int end = i; int j = end;
                                            while (j < s.Length && s[j] == ' ') j++;
                                            if (j < s.Length && s[j] == ':') { sb.Append('"'); sb.Append(s, start, end - start); sb.Append('"'); }
                                            else { sb.Append(s, start, end - start); }
                                            i = end - 1; continue;
                                        }
                                        sb.Append(c);
                                    }
                                    return sb.ToString();
                                };
                                if (kfRaw != null) {
                                    string entriesArr = extractSubArray(kfRaw, "entries");
                                    if (entriesArr != null) {
                                        _storedData["__entries"] = quoteJsKeys(entriesArr);
                                        System.Diagnostics.Debug.WriteLine("[DIAG:DATA] _storedData[__entries] set via C# extraction (" + entriesArr.Length + "B)");
                                        DevToolsLogger.Log("[DIAG:DATA] _storedData[__entries] set via C# extraction (" + entriesArr.Length + "B)");
                                    }
                                }
                                if (wfRaw != null) {
                                    string colsArr = extractSubArray(wfRaw, "collections");
                                    if (colsArr != null) {
                                        _storedData["__collections"] = quoteJsKeys(colsArr);
                                        System.Diagnostics.Debug.WriteLine("[DIAG:DATA] _storedData[__collections] set via C# extraction (" + colsArr.Length + "B)");
                                        DevToolsLogger.Log("[DIAG:DATA] _storedData[__collections] set via C# extraction (" + colsArr.Length + "B)");
                                    }
                                }
                                if (cfRaw != null) {
                                    string storiesArr = extractSubArray(cfRaw, "stories");
                                    if (storiesArr != null) {
                                        _storedData["__stories"] = quoteJsKeys(storiesArr);
                                        System.Diagnostics.Debug.WriteLine("[DIAG:DATA] _storedData[__stories] set via C# extraction (" + storiesArr.Length + "B)");
                                        DevToolsLogger.Log("[DIAG:DATA] _storedData[__stories] set via C# extraction (" + storiesArr.Length + "B)");
                                    }
                                }
                            } catch (Exception csEx) {
                                System.Diagnostics.Debug.WriteLine("[DIAG:DATA] C# extraction error: " + csEx.GetType().Name + " - " + csEx.Message);
                                DevToolsLogger.Log("[DIAG:DATA] C# extraction error: " + csEx.GetType().Name);
                            }
                            // Verify
                            try {
                                var gd = sysEngine.SafeEval("typeof globalThis.__graphData !== 'undefined' ? ('nodes='+globalThis.__graphData.nodes.length+' links='+globalThis.__graphData.links.length) : 'missing'");
                                System.Diagnostics.Debug.WriteLine("[DIAG:DATA] __graphData = " + (gd?.ToString() ?? "null"));
                                DevToolsLogger.Log("[DIAG:DATA] __graphData=" + (gd?.ToString() ?? "null"));
                            } catch { }
                            // SVG rendering: ABANDONED — SVG→XAML bridge was unstable
                            // (content doubling on scroll, architectural mismatch, Win SDK 15063 limits).
                            // Data is available for future Skia renderer at v1.0+.
                            try {
                                string diagCode = "if(typeof __diagLog==='function'){var _gd=globalThis.__graphData;__diagLog('[DIAG:CARD] graphData available nodes='+(_gd?_gd.nodes.length:'no')+' links='+(_gd?_gd.links.length:'no'));var _en=globalThis.__entries;__diagLog('[DIAG:CARD] entries='+(_en?_en.length:'no')+' stories='+(globalThis.__stories?globalThis.__stories.length:'no'));}";
                                sysEngine.SafeEval(diagCode);
                            } catch (Exception diagEx) {
                                DevToolsLogger.Log("[DIAG:CARD] Diag error: " + diagEx.GetType().Name);
                            }
                         } catch (Exception dataEx) {
                            System.Diagnostics.Debug.WriteLine("[DIAG:DATA] Extraction error: " + dataEx.GetType().Name + " - " + (dataEx.Message ?? ""));
                            DevToolsLogger.Log("[DIAG:DATA] Extraction error: " + dataEx.GetType().Name + " - " + (dataEx.Message ?? ""));
                        }
                        try { System.Diagnostics.Debug.WriteLine("[DIAG:SYS] running chunk via SafeEval (" + txt.Length + " bytes)..."); } catch { }
                        try { var pfx = txt.Length > 80 ? txt.Substring(0, 80) : txt; System.Diagnostics.Debug.WriteLine("[DIAG:SYS] chunk prefix: '" + pfx.Replace("\0","\\0").Replace("\r","\\r").Replace("\n","\\n") + "'"); } catch { }
                        try { var sysIdx = txt.IndexOf("System.register(", StringComparison.Ordinal); System.Diagnostics.Debug.WriteLine("[DIAG:SYS] IndexOf 'System.register(' = " + sysIdx); } catch { }
                        try { var lowerIdx = txt.IndexOf("system.register(", StringComparison.OrdinalIgnoreCase); System.Diagnostics.Debug.WriteLine("[DIAG:SYS] IndexOf (ignore case) 'system.register(' = " + lowerIdx); } catch { }
                        // Search for Mf definition in chunk (the scheduling function used with 100ms delay)
                        try {
                            var mfIdx = txt.IndexOf(",Mf=", StringComparison.Ordinal);
                            if (mfIdx > 0) {
                                var s = Math.Max(0, mfIdx - 30);
                                var e = Math.Min(txt.Length, mfIdx + 150);
                                var ctx = txt.Substring(s, e - s);
                                System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Mf context: '" + ctx.Replace("\0","\\0").Replace("\r","\\r").Replace("\n","\\n") + "'");
                            } else {
                                mfIdx = txt.IndexOf("Mf=function", StringComparison.Ordinal);
                                if (mfIdx > 0) {
                                    var s = Math.Max(0, mfIdx - 20);
                                    var e = Math.Min(txt.Length, mfIdx + 150);
                                    var ctx = txt.Substring(s, e - s);
                                    System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Mf function context: '" + ctx.Replace("\0","\\0").Replace("\r","\\r").Replace("\n","\\n") + "'");
                                } else {
                                    mfIdx = txt.IndexOf("Mf=", StringComparison.Ordinal);
                                    if (mfIdx > 0) {
                                        var s = Math.Max(0, mfIdx - 20);
                                        var e = Math.Min(txt.Length, mfIdx + 100);
                                        var ctx = txt.Substring(s, e - s);
                                        System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Mf loose context: '" + ctx.Replace("\0","\\0").Replace("\r","\\r").Replace("\n","\\n") + "'");
                                     } else {
                                        // Search for function Mf( pattern
                                        mfIdx = txt.IndexOf("function Mf(", StringComparison.Ordinal);
                                        if (mfIdx > 0) {
                                            var s = Math.Max(0, mfIdx - 20);
                                            var e = Math.Min(txt.Length, mfIdx + 150);
                                            var ctx = txt.Substring(s, e - s);
                                            System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Mf function declaration context: '" + ctx.Replace("\0","\\0").Replace("\r","\\r").Replace("\n","\\n") + "'");
                                        } else {
                                            System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Mf not found in chunk");
                                        }
                                    }
                                }
                            }
                        } catch (Exception mfEx) { try { System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Mf search error: " + mfEx.GetType().Name + " - " + mfEx.Message); } catch { } }
                        // Split chunk into individual System.register(...) calls to
                        // avoid NiL.JS parse failures on the full 589KB file.
                        // The paren matcher must skip //line comments, /*block comments*/,
                        // template literals, and strings so parens inside them don't
                        // throw off the depth counter.
                        var sysCalls = new List<string>();
                        int sysPos = 0;
                        while (sysPos < txt.Length)
                        {
                            int rIdx = txt.IndexOf("System.register(", sysPos, StringComparison.Ordinal);
                            if (rIdx < 0) break;
                            int pStart = rIdx + "System.register".Length;
                            if (pStart >= txt.Length || txt[pStart] != '(') { sysPos = rIdx + 1; continue; }
                            int depth = 1;
                            int i = pStart + 1;
                            bool inStr = false;
                            char strChar = '\0';
                            bool inTmpl = false;
                            bool inLineCmt = false;
                            bool inBlockCmt = false;
                            bool inRegex = false;
                            bool inRegexClass = false;
                            // Heuristic: after ( [ , ; : { = ! & | ? + - * / % ~ ^ < > the next / starts a regex
                            bool afterExprPrefix = true;
                            while (i < txt.Length && depth > 0)
                            {
                                char c = txt[i];
                                if (inBlockCmt)
                                {
                                    if (c == '*' && i + 1 < txt.Length && txt[i + 1] == '/') { inBlockCmt = false; i += 2; }
                                    else i++;
                                    continue;
                                }
                                if (inLineCmt)
                                {
                                    if (c == '\n' || c == '\r') inLineCmt = false;
                                    i++;
                                    continue;
                                }
                                if (inRegex)
                                {
                                    if (inRegexClass)
                                    {
                                        if (c == '\\') i += 2;
                                        else if (c == ']') inRegexClass = false;
                                        i++;
                                    }
                                    else
                                    {
                                        if (c == '[') { inRegexClass = true; i++; }
                                        else if (c == '\\') i += 2;
                                        else if (c == '/')
                                        {
                                            inRegex = false; afterExprPrefix = false; i++;
                                            // consume optional flags
                                            while (i < txt.Length && ((txt[i] >= 'a' && txt[i] <= 'z') || (txt[i] >= 'A' && txt[i] <= 'Z'))) i++;
                                        }
                                        else i++;
                                    }
                                    continue;
                                }
                                if (inStr)
                                {
                                    if (c == '\\') i += 2;
                                    else { if (c == strChar) { inStr = false; afterExprPrefix = false; } i++; }
                                    continue;
                                }
                                if (inTmpl)
                                {
                                    if (c == '\\') i += 2;
                                    else { if (c == '`') { inTmpl = false; afterExprPrefix = false; } i++; }
                                    continue;
                                }
                                // Not in any comment/string/template/regex
                                if (c == '/' && i + 1 < txt.Length)
                                {
                                    if (txt[i + 1] == '/') { inLineCmt = true; i += 2; continue; }
                                    if (txt[i + 1] == '*') { inBlockCmt = true; i += 2; continue; }
                                    // standalone / : regex or division
                                    if (afterExprPrefix) { inRegex = true; i++; afterExprPrefix = false; continue; }
                                    else { afterExprPrefix = true; i++; continue; }
                                }
                                if (c == '(') { depth++; afterExprPrefix = true; i++; continue; }
                                if (c == ')') { depth--; afterExprPrefix = false; i++; continue; }
                                if (c == '"') { inStr = true; strChar = '"'; i++; continue; }
                                if (c == '\'') { inStr = true; strChar = '\''; i++; continue; }
                                if (c == '`') { inTmpl = true; i++; continue; }
                                if (c == '[' || c == '{' || c == ',' || c == ';' || c == ':') { afterExprPrefix = true; i++; continue; }
                                if (c == ']' || c == '}') { afterExprPrefix = false; i++; continue; }
                                // operators that make the next / a regex
                                if ("=!&|?+-*%^<>\u007e".IndexOf(c) >= 0) { afterExprPrefix = true; i++; continue; }
                                // letters, digits, underscore, dollar, dot make the next / a division
                                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '$' || c == '.') { afterExprPrefix = false; i++; continue; }
                                i++;
                            }
                            if (depth == 0) { sysCalls.Add(txt.Substring(rIdx, i - rIdx)); sysPos = i; }
                            else { sysPos = rIdx + 1; }
                        }
                        try { System.Diagnostics.Debug.WriteLine("[DIAG:SYS] split chunk into " + sysCalls.Count + " System.register calls"); } catch { }
                        int sysCallIdx = 0;
                        foreach (var callRaw in sysCalls)
                        {
                            sysCallIdx++;
                            var call = callRaw;
                            try {
                                // Try multiple patterns to find execute() body start
                                string[] execPrefixes = {
                                    "return{execute:function(){",
                                    "return{execute:function() {",
                                    "execute:function(){",
                                    "execute:function() {",
                                    ":function(){"
                                };
                                int execStart = -1;
                                string execPrefix = null;
                                foreach (var p in execPrefixes) {
                                    execStart = call.IndexOf(p);
                                    if (execStart >= 0) { execPrefix = p; break; }
                                }
                                // Diagnostic: try Contains if still not found
                                if (execStart < 0) {
                                    if (call.Contains("execute:function")) {
                                        System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] call Contains execute:function but IndexOf returned -1!");
                                    }
                                }
                                string injectCode = ";try{globalThis.__diagInjectRan=true;var __dl=typeof __diagLog==='function'?__diagLog:function(){};var wKf=globalThis.Kf,wWf=globalThis.wf,wCf=globalThis.Cf,wVf=globalThis.vf;if(typeof wKf!=='undefined'&&typeof wWf!=='undefined'){var __f=wKf.entries;var __xf=wWf.collections.map(function(c){return{id:c.id,name:c.title,description:c.blurb,theme:c.grouping,keywords:c.keywords}});var __Pf={};__xf.forEach(function(c){__Pf[c.id]=c});var __nodes=[],__links=[],__conns={};__f.forEach(function(e){__nodes.push({id:e.id,name:e.title,start:e.start,file:e.file,type:'entry'});(e.collections||[]).forEach(function(cId){if(cId!=='C0030'&&cId[0]!=='K'&&__Pf[cId]){__links.push({source:e,target:__Pf[cId]});if(!__conns[e.id])__conns[e.id]=[];__conns[e.id].push(cId);if(!__conns[cId])__conns[cId]=[];__conns[cId].push(e.id)}})});__xf.forEach(function(e){__nodes.push({id:e.id,name:e.title,theme:e.theme,type:'collection'})});globalThis.__graphData={nodes:__nodes,links:__links,nodeConnections:__conns};if(typeof globalThis!=='undefined'&&globalThis.System)globalThis.System.__graphData=globalThis.__graphData;globalThis.__entries=__f;globalThis.__collections=wWf.collections;globalThis.__xf=__xf;globalThis.__Pf=__Pf;if(typeof wCf!=='undefined'){globalThis.__stories=wCf.stories;globalThis.__entriesWithDates=__f.filter(function(e){return e.start&&e.start.length>=5})}if(typeof wVf!=='undefined')globalThis.__keywords=wVf;try{if(typeof __storeData==='function'){}}catch(_sd){}__dl('INJECT_RAN nodes='+__nodes.length+' links='+__links.length+' entries='+__f.length)}else{__dl('INJECT_RAN no Kf/wf')}}catch(e){try{var __dl2=typeof __diagLog==='function'?__diagLog:function(){};__dl2('INJECT_ERR '+(e&&e.message||e))}catch(ee){}}";
                                bool injected = false;
                                if (execStart >= 0) {
                                    try { sysEngine.SafeEval("__diagLog('[DIAG:INJECT] Found exec prefix at " + execStart + " call#" + sysCallIdx + "')"); } catch { }
                                    int depth = 1;
                                    int pos = execStart + execPrefix.Length;
                                    while (pos < call.Length && depth > 0) {
                                        char c = call[pos];
                                        if (c == '\'') {
                                            pos++; while (pos < call.Length && call[pos] != '\'') { if (call[pos] == '\\') pos++; pos++; }
                                            if (pos < call.Length) pos++; continue;
                                        }
                                        if (c == '"') {
                                            pos++; while (pos < call.Length && call[pos] != '"') { if (call[pos] == '\\') pos++; pos++; }
                                            if (pos < call.Length) pos++; continue;
                                        }
                                        if (c == '`') {
                                            pos++;
                                            while (pos < call.Length && call[pos] != '`') {
                                                if (call[pos] == '\\') { pos += 2; continue; }
                                                if (call[pos] == '$' && pos + 1 < call.Length && call[pos + 1] == '{') {
                                                    pos += 2; int ed = 1;
                                                    while (pos < call.Length && ed > 0) { if (call[pos] == '{') ed++; else if (call[pos] == '}') ed--; pos++; }
                                                    continue;
                                                }
                                                pos++;
                                            }
                                            if (pos < call.Length) pos++;
                                            continue;
                                        }
                                        if (c == '/' && pos + 1 < call.Length) {
                                            if (call[pos + 1] == '/') {
                                                pos += 2; while (pos < call.Length && call[pos] != '\n') pos++;
                                                continue;
                                            }
                                            if (call[pos + 1] == '*') {
                                                pos += 2; while (pos + 1 < call.Length && !(call[pos] == '*' && call[pos + 1] == '/')) pos++;
                                                if (pos + 1 < call.Length) pos += 2; continue;
                                            }
                                        }
                                        if (c == '{') { depth++; }
                                        else if (c == '}') {
                                            depth--;
                                            if (depth == 0) {
                                                call = call.Substring(0, pos) + injectCode + call.Substring(pos);
                                                try { sysEngine.SafeEval("__diagLog('[DIAG:INJECT] Primary OK call#" + sysCallIdx + " offset=" + pos + "')"); } catch { }
                                                injected = true;
                                                break;
                                            }
                                        }
                                        pos++;
                                    }
                                    if (!injected) {
                                        try { sysEngine.SafeEval("__diagLog('[DIAG:INJECT] Primary walker failed call#" + sysCallIdx + " depth=" + depth + " pos=" + pos + "')"); } catch { }
                                    }
                                } else {
                                    try { sysEngine.SafeEval("__diagLog('[DIAG:INJECT] No exec prefix call#" + sysCallIdx + "')"); } catch { }
                                }
                                if (!injected) {
                                    try {
                                        var prefix = call.Length > 200 ? call.Substring(0, 200) : call;
                                        sysEngine.SafeEval("__diagLog('[DIAG:INJECT] Fallback try call#" + sysCallIdx + " callLen=" + call.Length + " prefix=[" + prefix.Replace("'","\\'").Replace("\0"," ").Replace("\r"," ").Replace("\n"," ") + "]')");
                                    } catch {
                                        try { sysEngine.SafeEval("__diagLog('[DIAG:INJECT] Fallback try call#" + sysCallIdx + "')"); } catch { }
                                    }
                                    int lastBrace = call.LastIndexOf('}');
                                    if (lastBrace > 0) {
                                        int seqStart = lastBrace;
                                        while (seqStart > 0 && call[seqStart - 1] == '}') seqStart--;
                                        call = call.Substring(0, seqStart) + injectCode + call.Substring(seqStart);
                                        try { sysEngine.SafeEval("__diagLog('[DIAG:INJECT] Fallback OK call#" + sysCallIdx + " seqStart=" + seqStart + "')"); } catch { }
                                        injected = true;
                                    } else {
                                        try { sysEngine.SafeEval("__diagLog('[DIAG:INJECT] Fallback failed call#" + sysCallIdx + "')"); } catch { }
                                    }
                                }
                            } catch (Exception injEx) { try { System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Inject error call #" + sysCallIdx + ": " + injEx.GetType().Name + " - " + injEx.Message); DevToolsLogger.Log("[DIAG:CHUNK] Inject error call #" + sysCallIdx + ": " + injEx.GetType().Name + " - " + injEx.Message); } catch { } }

                            try
                            {
                                // SafeEvalFast for the chunk — SafeEval has a 7s timeout and hash-throttle
                                // that silently fail the 589KB chunk. SafeEvalFast is a bare eval
                                // call with no throttle, timeout, or exception suppression.
                                try { System.Diagnostics.Debug.WriteLine("[DIAG:SYS] call #" + sysCallIdx + " via SafeEvalFast (" + call.Length + " bytes)"); } catch { }
                                var callWrapped = "var System=globalThis.System;" + call;
                                sysEngine.SafeEvalFast(callWrapped);
                                DevToolsLogger.Log("[DIAG:SYS] call #" + sysCallIdx + " SafeEvalFast done");
                ContinueAfterStandardChecks:
                                ; // placeholder to continue flow
                            }
                            catch (Exception ex)
                            {
                                try { System.Diagnostics.Debug.WriteLine("[DIAG:SYS] call #" + sysCallIdx + " FAIL: " + ex.GetType().Name + " - " + (ex.Message ?? "")); DevToolsLogger.Log("[DIAG:SYS] call #" + sysCallIdx + " FAIL: " + ex.GetType().Name + " - " + (ex.Message ?? "")); } catch { }
                            }
                        }
                        // Check if context was reset (test prop lost)
                        try { var ctxTest = sysEngine.SafeEval("globalThis.System.__ctxTest === 42 ? 'ok' : 'LOST'"); var msg = "[DIAG:SYS] ctxTest=" + (ctxTest?.ToString() ?? "null"); System.Diagnostics.Debug.WriteLine(msg); DevToolsLogger.Log(msg); } catch { }
                        // Also check if System variable resolves in eval
                        try { var sysVarTest = sysEngine.SafeEval("typeof System !== 'undefined' ? 'defined' : 'undefined'"); var msg = "[DIAG:SYS] System var=" + (sysVarTest?.ToString() ?? "null"); System.Diagnostics.Debug.WriteLine(msg); DevToolsLogger.Log(msg); } catch { }
                        // Check from C# if System was created
                        try { var s = sysEngine.SafeEval("typeof globalThis.System !== 'undefined' ? 'ok' : 'missing'"); var msg = "[DIAG:SYS] globalThis.System after chunk: " + (s?.ToString() ?? "null"); System.Diagnostics.Debug.WriteLine(msg); DevToolsLogger.Log(msg); } catch { }
                        // Check _modId (incremented on each register call)
                        try { var modId = sysEngine.SafeEval("globalThis.System && globalThis.System._reg ? (globalThis.System._reg._modId+'') : 'no-reg'"); var msg = "[DIAG:SYS] _modId=" + (modId?.ToString() ?? "null"); System.Diagnostics.Debug.WriteLine(msg); DevToolsLogger.Log(msg); } catch { }
                        // Check registry entry count
                        try { var rc = sysEngine.SafeEval("(function(){var S=globalThis.System;if(!S||!S.registry||!S.registry._entries)return'no-reg';var c=0;for(var k in S.registry._entries)c++;return c+'';})()"); var msg = "[DIAG:SYS] registry entries: " + (rc?.ToString() ?? "null"); System.Diagnostics.Debug.WriteLine(msg); DevToolsLogger.Log(msg); } catch { }
                        // Force-execute all registered modules with diagnostics
                        try { sysEngine.SafeEval(@"
(function(){
    var S = globalThis.System;
    if (!S || !S.registry) { return; }
    var _e = S.registry._entries;
    if (!_e) { return; }
    var count = 0, ecount = 0;
    for(var url in _e) {
        ecount++;
        var m = _e[url];
        if (m && typeof m.declare === 'function') {
            count++;
            try {
                var _export = function(n,v){};
                var _ctx = { meta: { url: url } };
                var declared = m.declare(_export, _ctx);
                if (declared && typeof declared.execute === 'function') {
                    declared.execute();
                }
            } catch(e) {
                var _d = typeof __diagLog === 'function' ? __diagLog : function(){};
                try { _d('[DIAG:SYS] EXEC FAIL url=' + url + ' ' + (e.message||e)); } catch(ee) {}
            }
        }
    }
    var _d2 = typeof __diagLog === 'function' ? __diagLog : function(){};
    try { _d2('[DIAG:SYS] force-exec: ' + ecount + ' entries, ' + count + ' declared'); } catch(ee) {}
})();"); } catch { }
                        // Flush microtasks so Svelte onMount / Promise callbacks fire before Phase 4
                        try { sysEngine.FlushMicrotasks(); } catch { }
                        // Diagnostics: check globals and DOM after execution
                        try { var d3check = sysEngine.SafeEval("typeof d3 !== 'undefined' ? 'd3_defined' : 'd3_missing'"); System.Diagnostics.Debug.WriteLine("[DIAG:SYS] post-exec d3=" + (d3check?.ToString() ?? "null")); DevToolsLogger.Log("[DIAG:SYS] post-exec d3=" + (d3check?.ToString() ?? "null")); } catch { }
                        try { var d3s = sysEngine.SafeEval("typeof d3 !== 'undefined' && typeof d3.select !== 'undefined' ? 'function' : 'no'"); System.Diagnostics.Debug.WriteLine("[DIAG:EXEC] typeof d3.select = " + (d3s?.ToString() ?? "null")); } catch { }
                        try { var d3f = sysEngine.SafeEval("typeof d3 !== 'undefined' && typeof d3.forceSimulation !== 'undefined' ? 'function' : 'no'"); System.Diagnostics.Debug.WriteLine("[DIAG:EXEC] typeof d3.forceSimulation = " + (d3f?.ToString() ?? "null")); } catch { }
                        try { var mc = sysEngine.SafeEval("typeof Map"); System.Diagnostics.Debug.WriteLine("[DIAG:SYS] post-exec typeof Map=" + (mc?.ToString() ?? "null")); DevToolsLogger.Log("[DIAG:SYS] post-exec typeof Map=" + (mc?.ToString() ?? "null")); } catch { }
                        try { var sc = sysEngine.SafeEval("typeof Set"); System.Diagnostics.Debug.WriteLine("[DIAG:SYS] post-exec typeof Set=" + (sc?.ToString() ?? "null")); DevToolsLogger.Log("[DIAG:SYS] post-exec typeof Set=" + (sc?.ToString() ?? "null")); } catch { }
                        try { var bodyCheck = sysEngine.SafeEval("(function(){if(!document||!document.body)return'no_body';var c=0;try{c=document.body.children.length}catch(e){}return'body_children='+c+' tag='+(document.body.tagName||'');})()"); System.Diagnostics.Debug.WriteLine("[DIAG:SYS] post-exec " + (bodyCheck?.ToString() ?? "null")); DevToolsLogger.Log("[DIAG:SYS] post-exec " + (bodyCheck?.ToString() ?? "null")); } catch { }
                        // Diagnostics: check async support and SVG DOM state
                        try { var rr = sysEngine.SafeEval("typeof regeneratorRuntime"); System.Diagnostics.Debug.WriteLine("[DIAG:JS] typeof regeneratorRuntime=" + (rr?.ToString() ?? "null")); } catch { }
                        try { var pr = sysEngine.SafeEval("typeof Promise"); System.Diagnostics.Debug.WriteLine("[DIAG:JS] typeof Promise=" + (pr?.ToString() ?? "null")); } catch { }
                        try { var prFn = sysEngine.SafeEval("(function(){try{return typeof Promise!=='undefined'?'ok':'no';}catch(e){return 'err:'+e;}})()"); System.Diagnostics.Debug.WriteLine("[DIAG:JS] Promise global=" + (prFn?.ToString() ?? "null")); } catch { }
                        try { var svgCheck = sysEngine.SafeEval("(function(){var t=document.getElementById('timeline');if(!t)return'no_timeline';var svg=t.querySelector('svg');if(!svg)return'no_svg';return'svg_id='+(svg.id||'none')+' children='+(svg.children?svg.children.length:'null')+' childNodes='+(svg.childNodes?svg.childNodes.length:'null');})()"); System.Diagnostics.Debug.WriteLine("[DIAG:JS] timeline SVG=" + (svgCheck?.ToString() ?? "null")); } catch { }
                        try { var execResult = sysEngine.SafeEval("(function(){try{var S=globalThis.System;if(!S||!S.registry||!S.registry._entries)return'no_reg';for(var k in S.registry._entries){var m=S.registry._entries[k];if(m&&m.declare){var r=m.declare(function(){},{});if(r&&typeof r.execute==='function'){var ret=r.execute();return typeof ret!=='undefined'?('ret='+typeof ret):'ret=undefined';}}}return'no_exec';})()"); System.Diagnostics.Debug.WriteLine("[DIAG:JS] execute return=" + (execResult?.ToString() ?? "null")); DevToolsLogger.Log("[DIAG:JS] execute return=" + (execResult?.ToString() ?? "null")); } catch { }
                        try { var asyncTest = sysEngine.SafeEval("(function(){try{var fn=new Function('return Promise.resolve(42).then(function(v){return v+1})');var p=fn();return typeof p==='object'&&typeof p.then==='function'?'Promise_chaining_works':'no_then';}catch(e){return 'err:'+(e.message||typeof e);}})()"); System.Diagnostics.Debug.WriteLine("[DIAG:JS] Promise test=" + (asyncTest?.ToString() ?? "null")); } catch { }
                        // Check setTimeout and requestAnimationFrame availability
                        try { var st = sysEngine.SafeEval("typeof setTimeout"); System.Diagnostics.Debug.WriteLine("[DIAG:JS] typeof setTimeout=" + (st?.ToString() ?? "null")); } catch { }
                        try { var rAF = sysEngine.SafeEval("typeof requestAnimationFrame"); System.Diagnostics.Debug.WriteLine("[DIAG:JS] typeof requestAnimationFrame=" + (rAF?.ToString() ?? "null")); } catch { }
                        try { var cIC = sysEngine.SafeEval("typeof setInterval"); System.Diagnostics.Debug.WriteLine("[DIAG:JS] typeof setInterval=" + (cIC?.ToString() ?? "null")); } catch { }
                        // Test if setTimeout actually fires: schedule a timer, flush microtasks, check flag
                        try { sysEngine.SafeEval("globalThis.__timerFlag=false;setTimeout(function(){globalThis.__timerFlag=true},1)"); } catch { }
                        try { sysEngine.FlushMicrotasks(); } catch { }
                        try { var stResult = sysEngine.SafeEval("globalThis.__timerFlag ? 'fired' : 'pending'"); System.Diagnostics.Debug.WriteLine("[DIAG:JS] setTimeout fired=" + (stResult?.ToString() ?? "null")); } catch { }
                        // Check System.__graphData set by injected code
                        try { var gd = sysEngine.SafeEval("(function(){var g=globalThis.System&&globalThis.System.__graphData;if(!g)return'no_graphData';return'graphData nodes='+(g.nodes?g.nodes.length:0)+' links='+(g.links?g.links.length:0);})()"); DevToolsLogger.Log("[DIAG:DATA] System.__graphData=" + (gd?.ToString() ?? "null")); } catch { }
                        // Also check globalThis.__graphData
                        try { var wgd = sysEngine.SafeEval("typeof globalThis.__graphData !== 'undefined' ? 'globalThis.__graphData='+(globalThis.__graphData.nodes?globalThis.__graphData.nodes.length:0)+'/'+(globalThis.__graphData.links?globalThis.__graphData.links.length:0) : 'undefined'"); DevToolsLogger.Log("[DIAG:DATA] " + (wgd?.ToString() ?? "null")); } catch { }
                        // Check raw data fields set by sync graph builder
                        try { var ent = sysEngine.SafeEval("typeof globalThis.__entries !== 'undefined' ? '__entries='+globalThis.__entries.length : 'no_entries'"); DevToolsLogger.Log("[DIAG:DATA] " + (ent?.ToString() ?? "null")); } catch { }
                        try { var ewd = sysEngine.SafeEval("typeof globalThis.__entriesWithDates !== 'undefined' ? '__entriesWithDates='+globalThis.__entriesWithDates.length : 'no_ewd'"); DevToolsLogger.Log("[DIAG:DATA] " + (ewd?.ToString() ?? "null")); } catch { }
                        try { var st = sysEngine.SafeEval("typeof globalThis.__stories !== 'undefined' ? '__stories='+globalThis.__stories.length : 'no_stories'"); DevToolsLogger.Log("[DIAG:DATA] " + (st?.ToString() ?? "null")); } catch { }
                        try { var kw = sysEngine.SafeEval("typeof globalThis.__keywords !== 'undefined' ? '__keywords='+globalThis.__keywords.length : 'no_keywords'"); DevToolsLogger.Log("[DIAG:DATA] " + (kw?.ToString() ?? "null")); } catch { }
                        try { var pf = sysEngine.SafeEval("typeof globalThis.__Pf !== 'undefined' ? '__Pf='+Object.keys(globalThis.__Pf).length : 'no_Pf'"); DevToolsLogger.Log("[DIAG:DATA] " + (pf?.ToString() ?? "null")); } catch { }
                        // Also check for post-exec module-scope vars leaked by chunk injection
                        try { var diagInject = sysEngine.SafeEval("typeof globalThis.__diagInjectRan !== 'undefined' ? globalThis.__diagInjectRan : 'undefined'"); System.Diagnostics.Debug.WriteLine("[DIAG:MOD] __diagInjectRan=" + (diagInject?.ToString() ?? "null")); try { DevToolsLogger.Log("[DIAG:MOD] __diagInjectRan=" + (diagInject?.ToString() ?? "null")); } catch { } } catch { }
                        try { var diagMf = sysEngine.SafeEval("typeof globalThis.__diagMf !== 'undefined' ? (typeof globalThis.__diagMf) : 'undefined'"); System.Diagnostics.Debug.WriteLine("[DIAG:MOD] __diagMf=" + (diagMf?.ToString() ?? "null")); } catch { }
                        try { var diagS = sysEngine.SafeEval("typeof globalThis.__diagS !== 'undefined' ? (globalThis.__diagS.tagName||typeof globalThis.__diagS) : 'undefined'"); System.Diagnostics.Debug.WriteLine("[DIAG:MOD] __diagS=" + (diagS?.ToString() ?? "null")); } catch { }
                        try { var diagIf = sysEngine.SafeEval("typeof globalThis.__diagIf !== 'undefined' ? (Array.isArray(globalThis.__diagIf)?'array['+globalThis.__diagIf.length+']':typeof globalThis.__diagIf) : 'undefined'"); System.Diagnostics.Debug.WriteLine("[DIAG:MOD] __diagIf=" + (diagIf?.ToString() ?? "null")); } catch { }
                        try { var diagR = sysEngine.SafeEval("typeof globalThis.__diagR !== 'undefined' ? 'function' : 'undefined'"); System.Diagnostics.Debug.WriteLine("[DIAG:MOD] __diagR=" + (diagR?.ToString() ?? "null")); } catch { }
                        // Route detection — log only, no navigation (disabled to prevent double render)
                        try { sysEngine.SafeEval(@"
(function(){
    try {
        __diagLog('[DIAG:ROUTE] location=' + (window.location.href || 'none') + ' hash=' + (window.location.hash || 'none') + ' path=' + (window.location.pathname || 'none'));
        var links = document.querySelectorAll('a');
        var navLink = null;
        for (var i = 0; i < links.length; i++) {
            var text = (links[i].textContent || '').toLowerCase().trim();
            var href = (links[i].getAttribute('href') || '').toLowerCase();
            var cls = (links[i].getAttribute('class') || '').toLowerCase();
            var id = (links[i].id || '').toLowerCase();
            if (text.indexOf('network') >= 0 || text.indexOf('timeline') >= 0 || text.indexOf('graph') >= 0 ||
                href.indexOf('network') >= 0 || href.indexOf('timeline') >= 0 || href.indexOf('graph') >= 0 ||
                cls.indexOf('network') >= 0 || cls.indexOf('timeline') >= 0 || cls.indexOf('graph') >= 0) {
                navLink = links[i];
                __diagLog('[DIAG:ROUTE] Found nav link #' + i + ' text=[' + (links[i].textContent||'').trim() + '] href=' + href + ' class=' + cls + ' id=' + id);
            }
        }
        if (!navLink) { __diagLog('[DIAG:ROUTE] No network/timeline/graph nav link found'); }
        // Navigation disabled — hash set and nav click removed to prevent re-render cycle
    } catch(e) { __diagLog('[DIAG:ROUTE] Route error'); }
})();
"); } catch { }
// Post-exec data diagnostics
try { sysEngine.SafeEval(@"
(function(){
    try {
        // Check for If and Pf data references (from chunk analysis)
        var plot = document.getElementById('plot');
        __diagLog('[DIAG:DATA] plot=' + (plot?'found':'missing') + ' children=' + (plot?plot.children.length:'0'));
        var timeline = document.getElementById('timeline');
        __diagLog('[DIAG:DATA] timeline tag=' + (timeline?timeline.tagName:'none') + ' children=' + (timeline?timeline.children.length:'0'));
        var canvas = document.querySelector('canvas');
        __diagLog('[DIAG:DATA] canvas=' + (canvas?'found at '+(canvas.id||'no-id'):'none'));
        // Check for any large arrays/datasets on window
        var dataKeys = [];
        for (var k in window) {
            try {
                var v = window[k];
                if (v && Array.isArray(v) && v.length > 50) {
                    dataKeys.push(k + '[' + v.length + ']');
                }
            } catch(e) {}
        }
        __diagLog('[DIAG:DATA] large arrays on window: ' + (dataKeys.length ? dataKeys.join(', ') : 'none'));
    } catch(e) { __diagLog('[DIAG:DATA] error'); }
})();
"); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    try { System.Diagnostics.Debug.WriteLine("[DIAG:System.import] fetch exception for " + resolved + ": " + ex.GetType().Name + " - " + ex.Message); } catch { }
                }
                return JSValue.Undefined;
            })));

            // ===== ES polyfills via NiL.JS eval =====
            try
            {
                SafeEval(@"
// Defensive polyfills: avoid injecting malformed or unsafe code that may
// trigger internal NiL.JS engine bugs (IndexOutOfRange, etc.). Keep them
// minimal and defensive.
if (!Object.assign) Object.assign = function(t){for(var i=1;i<arguments.length;i++){var s=arguments[i]; if (s && (typeof s === 'object' || typeof s === 'function')) { for(var k in s) { if (Object.prototype.hasOwnProperty.call(s,k)) { try { t[k] = s[k]; } catch(e) { /* swallow */ } } } } } return t};
if (!Object.fromEntries) Object.fromEntries = function(e){var r={}; try { e.forEach(function(kv){ r[kv[0]] = kv[1]; }); } catch(e) { } return r};
if (!Array.prototype.flat) Array.prototype.flat = function(d){d=void 0===d?1:d;return this.reduce(function(a,v){return a.concat(Array.isArray(v)&&d>0?v.flat(d-1):[v])},[])};
if (!Array.prototype.flatMap) Array.prototype.flatMap = function(f){var t=this;return this.reduce(function(a,v,i){return a.concat(f.call(t,v,i,this))},[])};
// Array.prototype.at — ES2022
if (!Array.prototype.at) Array.prototype.at = function(i){var l=this.length;var idx=i<0?l+i:i;return idx>=0&&idx<l?this[idx]:undefined};
// String.prototype.at — ES2022
if (!String.prototype.at) String.prototype.at = function(i){var l=this.length;var idx=i<0?l+i:i;return idx>=0&&idx<l?this[idx]:undefined};
// Object.hasOwn — ES2022
if (!Object.hasOwn) Object.hasOwn = function(o,p){return Object.prototype.hasOwnProperty.call(o,p)};
// Array.prototype.findLast — ES2023
if (!Array.prototype.findLast) Array.prototype.findLast = function(f,t){for(var i=this.length-1;i>=0;i--){if(f.call(t,this[i],i,this))return this[i]}return undefined};
// Array.prototype.findLastIndex — ES2023
if (!Array.prototype.findLastIndex) Array.prototype.findLastIndex = function(f,t){for(var i=this.length-1;i>=0;i--){if(f.call(t,this[i],i,this))return i}return -1};
// String.prototype.padStart — ES2017
if (!String.prototype.padStart) String.prototype.padStart = function(l,p){p=p||' ';var s=this;while(s.length<l)s=p+s;return s};
// String.prototype.padEnd — ES2017
if (!String.prototype.padEnd) String.prototype.padEnd = function(l,p){p=p||' ';var s=this;while(s.length<l)s=s+p;return s};
// structuredClone — basic (deep copy via JSON)
if (typeof structuredClone === 'undefined') { try { structuredClone = function(v){return JSON.parse(JSON.stringify(v))}; } catch(e) {} }
// Promise.allSettled — ES2020
if (typeof Promise !== 'undefined' && !Promise.allSettled) { Promise.allSettled = function(ps){return Promise.all(ps.map(function(p){return Promise.resolve(p).then(function(v){return {status:'fulfilled',value:v}},function(e){return {status:'rejected',reason:e}})}))}; }
// Array.prototype.includes — ES2016
if (!Array.prototype.includes) Array.prototype.includes = function(v,f){var a=this;var l=a.length;var i=f||0;for(;i<l;i++){if(a[i]===v||(a[i]!==a[i]&&v!==v))return true}return false};
// Object.entries — ES2017
if (!Object.entries) Object.entries = function(o){var r=[];for(var k in o)if(Object.prototype.hasOwnProperty.call(o,k))r.push([k,o[k]]);return r};
// Object.values — ES2017
if (!Object.values) Object.values = function(o){var r=[];for(var k in o)if(Object.prototype.hasOwnProperty.call(o,k))r.push(o[k]);return r};
// Number.isNaN — ES2015
if (!Number.isNaN) Number.isNaN = function(v){return typeof v === 'number' && v !== v};
// Number.isFinite — ES2015
if (!Number.isFinite) Number.isFinite = function(v){return typeof v === 'number' && isFinite(v)};
// Number.parseInt / Number.parseFloat — ES2015
if (!Number.parseInt) Number.parseInt = parseInt;
if (!Number.parseFloat) Number.parseFloat = parseFloat;
");
            }
            catch { System.Diagnostics.Debug.WriteLine("[NiLJS] Polyfill injection failed"); }
        }

        private static void DiagInspectSystemJS(JavaScriptEngine sysEngine)
        {
            try
            {
                // Polyfills-legacy sets System on globalThis (not as a NiL.JS global variable)
                var sysVal = sysEngine.SafeEval("globalThis.System");
                if (sysVal == null || sysVal.IsNull || sysVal.ValueType == NiL.JS.Core.JSValueType.Undefined)
                {
                    System.Diagnostics.Debug.WriteLine("[DIAG:SYS] globalThis.System is undefined/null");
                    DevToolsLogger.Log("[DIAG:SYS] globalThis.System is undefined/null");
                    return;
                }
                // Log System object keys (safe typeof check, avoid Object.keys which crashes on polyfilled System)
                var sysKeysStr = "unknown";
                try { sysKeysStr = sysEngine.SafeEval("(function(){ var k='',s=globalThis.System; for(var n in s) k+=','+n; return k.substring(1); })()")?.ToString() ?? "null"; } catch { sysKeysStr = "(safeEval failed)"; }
                System.Diagnostics.Debug.WriteLine("[DIAG:SYS] System keys: " + sysKeysStr);
                DevToolsLogger.Log("[DIAG:SYS] System keys: " + sysKeysStr);
                // Check registry (safe iteration, no Object.keys)
                var registryVal = sysEngine.SafeEval("globalThis.System && globalThis.System.registry ? (function(){ var k='',r=globalThis.System.registry; for(var n in r) k+=','+n; return k.substring(1); })() : null");
                if (registryVal == null || registryVal.IsNull || registryVal.ValueType == NiL.JS.Core.JSValueType.Undefined)
                {
                    System.Diagnostics.Debug.WriteLine("[DIAG:SYS] System.registry missing or null");
                    DevToolsLogger.Log("[DIAG:SYS] System.registry missing or null");
                    // Try to create a minimal registry on System so module execution can proceed
                    sysEngine.SafeEval("if (globalThis.System && !globalThis.System.registry) { globalThis.System.registry = {}; globalThis.System.registry._entries = {}; globalThis.System.registry.forEach = function(fn) { for (var k in globalThis.System.registry._entries) fn(globalThis.System.registry._entries[k], k); }; globalThis.System.registry.set = function(k,v) { globalThis.System.registry._entries[k] = v; }; globalThis.System.registry.get = function(k) { return globalThis.System.registry._entries[k]; }; }");
                    System.Diagnostics.Debug.WriteLine("[DIAG:SYS] registry polyfill injected");
                    DevToolsLogger.Log("[DIAG:SYS] registry polyfill injected");
                    return;
                }
                var keysStr = registryVal.ToString();
                System.Diagnostics.Debug.WriteLine("[DIAG:SYS] registry keys: " + keysStr);
                DevToolsLogger.Log("[DIAG:SYS] registry keys: " + keysStr);
                // For each key, check entry structure
                var keys = keysStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var k in keys)
                {
                    var trimmedKey = k.Trim().Trim('"').Trim('\'');
                    if (string.IsNullOrEmpty(trimmedKey)) continue;
                    try
                    {
                        var entryInfo = sysEngine.SafeEval(@"
(function(k){
    var e = System.registry[k] || System.registry._entries && System.registry._entries[k];
    if (!e) return 'key=' + k + ' NOT_FOUND';
    var hasExec = typeof e.execute === 'function';
    var hasDecl = typeof e.declare === 'function';
    var typeStr = typeof e;
    var ownKeys = ''; try { for(var n in e) ownKeys+=','+n; ownKeys=ownKeys.substring(1); } catch(ex){ ownKeys='(iter error)'; }
    return 'key=' + k + ' type=' + typeStr + ' hasExec=' + hasExec + ' hasDecl=' + hasDecl + ' ownKeys=[' + ownKeys + ']';
})('" + trimmedKey.Replace("'", "\\'") + @"')");
                        var info = entryInfo?.ToString() ?? "null";
                        System.Diagnostics.Debug.WriteLine("[DIAG:SYS] " + info);
                        DevToolsLogger.Log("[DIAG:SYS] " + info);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[DIAG:SYS] C# inspect exception: " + ex.Message);
                DevToolsLogger.Log("[DIAG:SYS] C# inspect exception: " + ex.Message);
            }
        }

        private void _nilSyncDocument()
        {
            // Placeholder for future NiL.JS DOM sync operations
        }

        // Host Classes for NiL.JS

        private class HostCanvas
        {
            public int width { get; set; } = 300;
            public int height { get; set; } = 150;

            public HostContext2D getContext(string type)
            {
                if (type == "2d") return new HostContext2D();
                return null;
            }
        }

        private class HostImage
        {
            public int width { get; set; }
            public int height { get; set; }
            public int naturalWidth { get; set; }
            public int naturalHeight { get; set; }
            public string src { get; set; } = "";
            public string alt { get; set; } = "";
            public JSValue onload { get; set; } = JSValue.Null;
            public JSValue onerror { get; set; } = JSValue.Null;
            public bool complete { get; set; } = false;
            public bool crossOrigin { get; set; } = false;
            public string referrerPolicy { get; set; } = "";
            public string decoding { get; set; } = "auto";
            public string fetchPriority { get; set; } = "auto";
            public string loading { get; set; } = "eager";
            public JSValue decode() { return JSValue.Undefined; }
        }

        private class HostContext2D
        {
            public string fillStyle { get; set; } = "#000000";
            public string strokeStyle { get; set; } = "#000000";
            public double lineWidth { get; set; } = 1.0;
            public string font { get; set; } = "10px sans-serif";

            public void fillRect(double x, double y, double w, double h) 
            { 
                System.Diagnostics.Debug.WriteLine($"[Canvas] fillRect({x},{y},{w},{h}) style={fillStyle}");
            }

            public void strokeRect(double x, double y, double w, double h) 
            { 
                System.Diagnostics.Debug.WriteLine($"[Canvas] strokeRect({x},{y},{w},{h}) style={strokeStyle}");
            }

            public void clearRect(double x, double y, double w, double h) 
            { 
                System.Diagnostics.Debug.WriteLine($"[Canvas] clearRect({x},{y},{w},{h})");
            }

            public void fillText(string text, double x, double y) 
            { 
                System.Diagnostics.Debug.WriteLine($"[Canvas] fillText('{text}',{x},{y}) font={font} style={fillStyle}");
            }

            public JSValue measureText(string text)
            {
                // Basic approximation: 6px per char
                var width = (text ?? "").Length * 6.0;
                return JSValue.Marshal(new { width = width });
            }

            public void beginPath() { System.Diagnostics.Debug.WriteLine("[Canvas] beginPath"); }
            public void closePath() { System.Diagnostics.Debug.WriteLine("[Canvas] closePath"); }
            public void moveTo(double x, double y) { System.Diagnostics.Debug.WriteLine($"[Canvas] moveTo({x},{y})"); }
            public void lineTo(double x, double y) { System.Diagnostics.Debug.WriteLine($"[Canvas] lineTo({x},{y})"); }
            public void stroke() { System.Diagnostics.Debug.WriteLine($"[Canvas] stroke style={strokeStyle}"); }
            public void fill() { System.Diagnostics.Debug.WriteLine($"[Canvas] fill style={fillStyle}"); }
            
            public void drawImage(JSValue image, double x, double y) 
            { 
                System.Diagnostics.Debug.WriteLine($"[Canvas] drawImage({image},{x},{y})");
            }
        }

        private class HostAudio
        {
            public string src { get; set; }
            public double currentTime { get; set; }
            public double duration { get; set; }
            public bool paused { get; set; } = true;

            public HostAudio(string src)
            {
                this.src = src;
            }

            public void play() 
            { 
                paused = false; 
                // Trigger host audio playback if possible
            }
            
            public void pause() 
            { 
                paused = true; 
            }
        }

        private abstract class JsBindingPattern
        {
            public string DefaultExpr;
        }

        private sealed class JsIdentifierPattern : JsBindingPattern
        {
            public string Name;
        }

        private sealed class JsObjectPattern : JsBindingPattern
        {
            public sealed class PropertyBinding
            {
                public string Key;
                public JsBindingPattern Target;
            }

            public List<PropertyBinding> Properties = new List<PropertyBinding>();
            public string RestIdentifier;
        }

        private sealed class JsArrayPattern : JsBindingPattern
        {
            public sealed class ElementBinding
            {
                public bool IsHole;
                public JsBindingPattern Target;
            }

            public List<ElementBinding> Elements = new List<ElementBinding>();
            public JsIdentifierPattern RestTarget;
        }

        private sealed class JsFuncParam
        {
            public string Raw;
            public JsBindingPattern Pattern;
            public bool IsRest;
        }

        private static JsFuncParam CreateIdentifierParam(string name, bool isRest = false)
        {
            return new JsFuncParam
            {
                Raw = name ?? string.Empty,
                Pattern = new JsIdentifierPattern { Name = name },
                IsRest = isRest
            };
        }

        private readonly Dictionary<string, JsFuncDef> _userFunctionsEx = new Dictionary<string, JsFuncDef>(StringComparer.Ordinal);

        /// <summary>Expose current DOM to the engine (for document.* bridge).</summary>
        public void SetDom(LiteElement domRoot)
        {
            // Phase DIAG (issue D): reset per-page counters
            _diagSafeEvalCalls = 0; _diagSafeEvalFails = 0; _diagJSException = 0;
            try { System.Diagnostics.Debug.WriteLine("[DIAG:JS-RESET] SafeEval/js counters reset for new SetDom"); } catch { }
            // reset per-page NiL.JS failure state
            lock (_nilEvalLock)
            {
                // Keep _badScriptHashes persistent across navigations so previously
                // observed failing scripts remain skipped. Only reset per-page
                // counters here.
                _safeEvalErrorCount = 0;
                _skipInlineScriptsForPage = false;
            }
            // reset per-page skipped diagnostics counter
            System.Threading.Interlocked.Exchange(ref _diagSkippedInlineScripts, 0);
            _domRoot = domRoot;
            // JS is "enabled" in this app; hide server noscript overlays & flip no-js ? js
            this.SanitizeForScriptingEnabled(domRoot);
            try { _nilSyncDocument(); } catch { /* swallow */ }
            
            // Execute inline scripts
            try
            {
                int _diagScriptIdx = 0;
                foreach (var s in _domRoot.SelfAndDescendants())
                {
                    if (string.Equals(s.Tag, "script", StringComparison.OrdinalIgnoreCase))
                    {
                        if (s.Attr != null && s.Attr.ContainsKey("src")) continue; // Skip external for now
                        var code = CollectScriptText(s);
                        if (!string.IsNullOrWhiteSpace(code))
                        {
                            // Phase DIAG (issue D): log script preview so we can map a
                            // later NiL.JS IndexOutOfRangeException/JSException to a
                            // specific <script> block in the page.
                            _diagScriptIdx++;
                            string preview = code.Length > 240 ? code.Substring(0, 240) + "…" : code;
                            preview = preview.Replace("\r", " ").Replace("\n", " ");
                            try { System.Diagnostics.Debug.WriteLine("[DIAG:JS-SCRIPT] idx=" + _diagScriptIdx + " len=" + code.Length + " preview=\"" + preview + "\""); } catch { }

                            // Pre-skip known-bad inline scripts to avoid a guaranteed crash.
                            var h = 0;
                            try { unchecked { for (int i = 0; i < code.Length; i++) h = h * 31 + code[i]; } } catch { }
                            bool preSkip = false;
                            lock (_nilEvalLock)
                            {
                                if (_skipInlineScriptsForPage)
                                {
                                    preSkip = true;
                                }
                                else if (h != 0)
                                {
                                    BadHashRecord rec = null;
                                    _badHashRecords.TryGetValue(h.ToString(), out rec);
                                    if (rec != null && rec.Count >= BadHashRecordThreshold) preSkip = true;
                                }
                            }
                            if (preSkip)
                            {
                                System.Threading.Interlocked.Increment(ref _diagSkippedInlineScripts);
                                try { System.Diagnostics.Debug.WriteLine("[DIAG:JS-SKIP] pre-skip hash=" + h + " preview=\"" + (preview.Length > 80 ? preview.Substring(0, 80) + "…" : preview) + "\""); } catch { }
                                continue;
                            }

                            RunGlobalScript(code);
                        }
                    }
                }
                try { System.Diagnostics.Debug.WriteLine("[DIAG:JS-SUMMARY] inline-scripts=" + _diagScriptIdx + " SafeEval.calls=" + _diagSafeEvalCalls + " SafeEval.fails=" + _diagSafeEvalFails + " JSException=" + _diagJSException + " SkippedInline=" + _diagSkippedInlineScripts); } catch { }
                try { DumpBadHashSummary(10); } catch { }
            }
            catch { /* swallow */ }
        }

        private JSValue SafeEval(string code)
        {
            // Phase DIAG (issue D): code hash + first 80 chars of code to
            // disambiguate which call site triggered the failure.
            int _diagCodeHash = 0;
            string _diagCodePreview = "";
            try
            {
                if (!string.IsNullOrEmpty(code))
                {
                    unchecked { for (int i = 0; i < code.Length; i++) _diagCodeHash = _diagCodeHash * 31 + code[i]; }
                    _diagCodePreview = code.Length > 80 ? code.Substring(0, 80) + "…" : code;
                    _diagCodePreview = _diagCodePreview.Replace("\r", " ").Replace("\n", " ");
                }
            }
            catch { }
            _diagSafeEvalCalls++;
            try
            {
                // If we've globally disabled inline scripts for this page, skip evaluation.
                    lock (_nilEvalLock)
                    {
                        if (_skipInlineScriptsForPage)
                        {
                            try { System.Diagnostics.Debug.WriteLine("[DIAG:JS-SKIP] global skip active (threshold reached)"); } catch { }
                            return JSValue.Undefined;
                        }
                        if (_diagCodeHash != 0)
                        {
                            BadHashRecord recChk = null;
                            _badHashRecords.TryGetValue(_diagCodeHash.ToString(), out recChk);
                            if (recChk != null && recChk.Count >= BadHashRecordThreshold)
                            {
                                try { System.Diagnostics.Debug.WriteLine("[DIAG:JS-SKIP] hash=" + _diagCodeHash + " preview=\"" + _diagCodePreview + "\""); } catch { }
                                return JSValue.Undefined;
                            }
                        }
                    }

                if (_evalContext != null)
                {
                    _niljsSafeEvalFailed = false;
                    // Serialize calls into NiL.JS: only one thread may Eval at a time
                    lock (_nilEvalLock)
                    {
                        // Phase S.2: JS execution timeout via NiL.JS DebuggerCallback.
                        // This fires on each expression evaluation step, letting us abort
                        // tight loops (while(true){}) after timeoutMs.
                        const int timeoutMs = 7000;
                        var start = Environment.TickCount;
                        var oldDebug = _evalContext.Debugging;
                        _evalContext.Debugging = true;
                        Exception timeoutEx = null;
                        DebuggerCallback cb = (ctx, e) =>
                        {
                            if (timeoutEx == null && Environment.TickCount - start >= timeoutMs)
                            {
                                timeoutEx = new TimeoutException("JS execution exceeded " + timeoutMs + "ms (hash=" + _diagCodeHash + ")");
                            }
                            if (timeoutEx != null) throw timeoutEx;
                        };
                        _evalContext.DebuggerCallback += cb;
                        try
                        {
                            return _evalContext.Eval(code);
                        }
                        catch (Exception ex) when (!(ex is InvalidOperationException))
                        {
                            // Catch JSException and other runtime errors immediately
                            // so they never propagate past SafeEval (avoids debugger
                            // "unhandled in non-user code" stop on NiL.JS frames).
                            return JSValue.Undefined;
                        }
                        finally
                        {
                            _evalContext.Debugging = oldDebug;
                            _evalContext.DebuggerCallback -= cb;
                            if (timeoutEx != null)
                            {
                                try { DevToolsLogger.Log("[JS:TIMEOUT] Script exceeded " + timeoutMs + "ms — abandoned (hash=" + _diagCodeHash + ")"); } catch { }
                                _niljsSafeEvalFailed = true;
                                _diagSafeEvalFails++;
                            }
                        }
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Context likely corrupted — recreate and retry
                System.Diagnostics.Debug.WriteLine("[NiLJS] Context corrupt, reinitializing... (hash=" + _diagCodeHash + " preview=\"" + _diagCodePreview + "\")");
                _nilInit();
                try
                {
                    if (_evalContext != null)
                    {
                        lock (_nilEvalLock)
                        {
                            return _evalContext.Eval(code);
                        }
                    }
                }
                catch (Exception ex2) { System.Diagnostics.Debug.WriteLine("[NiLJS] SafeEval retry failed: " + ex2.GetType().Name + ": " + ex2.Message + " (hash=" + _diagCodeHash + " preview=\"" + _diagCodePreview + "\")"); _niljsSafeEvalFailed = true; _diagSafeEvalFails++; }
            }
            catch (Exception ex)
            {
                var exType = ex.GetType().Name;
                // Suppress verbose logging for Regex ArgumentOutOfRangeException noise
                bool isRegexNoise = exType == "ArgumentOutOfRangeException" && ex.StackTrace != null && ex.StackTrace.Contains("System.Text.RegularExpressions");
                if (!isRegexNoise)
                {
                    System.Diagnostics.Debug.WriteLine("[NiLJS] SafeEval: " + exType + ": " + ex.Message + " (hash=" + _diagCodeHash + " preview=\"" + _diagCodePreview + "\")");
                    // Log full exception details and a managed stack trace to help post-mortem
                    try
                    {
                        System.Diagnostics.Debug.WriteLine("[NiLJS] SafeEval Exception.ToString(): " + ex.ToString());
                        System.Diagnostics.Debug.WriteLine("[NiLJS] SafeEval Environment.StackTrace:\n" + System.Environment.StackTrace);
                        try
                        {
                            System.Diagnostics.Debug.WriteLine(
                                "[NiLJS] SafeEval Exception StackTrace (detailed):\n" + ex.StackTrace.ToString());
                        }
                        catch { /* swallow */ }
                    }
                    catch { /* swallow logging errors */ }
                }
                _niljsSafeEvalFailed = true;
                _diagSafeEvalFails++;
                if (exType == "JSException") _diagJSException++;

                try
                {
                    // Only record hard NiL.JS failures to the bad-script set to avoid overbroad skipping.
                    var shouldRecord = ex is IndexOutOfRangeException || ex.GetType().Name == "JSException";
                    lock (_nilEvalLock)
                    {
                        bool needPersist = false;
                        if (shouldRecord && _diagCodeHash != 0)
                        {
                            // Record/increment a BadHashRecord entry keyed by string(hash)
                            var hk = _diagCodeHash.ToString();
                            BadHashRecord rec = null;
                            if (!_badHashRecords.TryGetValue(hk, out rec) || rec == null)
                            {
                                rec = new BadHashRecord
                                {
                                    Hash = hk,
                                    Preview = (_diagCodePreview.Length > 160) ? _diagCodePreview.Substring(0, 160) : _diagCodePreview,
                                    Count = 1,
                                    FirstSeen = DateTime.UtcNow,
                                    LastSeen = DateTime.UtcNow
                                };
                                _badHashRecords[hk] = rec;
                            }
                            else
                            {
                                try { rec.Count++; } catch { rec.Count = (rec.Count < int.MaxValue) ? rec.Count + 1 : rec.Count; }
                                rec.LastSeen = DateTime.UtcNow;
                                if (string.IsNullOrWhiteSpace(rec.Preview) && !string.IsNullOrWhiteSpace(_diagCodePreview)) rec.Preview = (_diagCodePreview.Length > 160) ? _diagCodePreview.Substring(0, 160) : _diagCodePreview;
                                _badHashRecords[hk] = rec;
                            }

                            // Populate diagnostic fields for this failure (best-effort, truncated)
                            try
                            {
                                var etype = ex.GetType().Name;
                                var emsg = ex.Message ?? "";
                                var estr = ex.ToString() ?? "";
                                if (rec != null)
                                {
                                    rec.LastExceptionType = etype;
                                    rec.LastExceptionMessage = emsg.Length > 1024 ? emsg.Substring(0, 1024) : emsg;
                                    rec.LastExceptionStack = estr.Length > 8192 ? estr.Substring(0, 8192) : estr;
                                    rec.LastExceptionTime = DateTime.UtcNow;
                                    try { rec.LastExceptionContext = "";/*(_diagPageUri ?? "") + "";*/ } catch { }
                                    _badHashRecords[hk] = rec;
                                }
                            }
                            catch { }

                            if (rec != null && rec.Count >= BadHashRecordThreshold)
                            {
                                try { System.Diagnostics.Debug.WriteLine("[DIAG:JS-BAD] hash=" + _diagCodeHash + " reached threshold=" + rec.Count + " preview=\"" + (rec.Preview.Length > 80 ? rec.Preview.Substring(0, 80) + "…" : rec.Preview) + "\""); } catch { }
                                // Request persistent save after leaving the nil-eval lock
                                needPersist = true;
                            }
                            else
                            {
                                try { System.Diagnostics.Debug.WriteLine("[DIAG:JS-BAD] hash=" + _diagCodeHash + " observed-fails=" + rec.Count + " (waiting for threshold=" + BadHashRecordThreshold + ")"); } catch { }
                            }
                        }

                        if (shouldRecord) _safeEvalErrorCount++;
                        if (_safeEvalErrorCount > SafeEvalErrorThreshold)
                        {
                            _skipInlineScriptsForPage = true;
                            try { System.Diagnostics.Debug.WriteLine("[DIAG:JS-THRESHOLD] safeEval errors=" + _safeEvalErrorCount + " -> disabling further inline scripts for this page"); } catch { }
                        }
                        // Persist bad-hash store if needed (outside nilEvalLock scope)
                        if (needPersist)
                        {
                            try { ScheduleSaveBadHashStoreDebounced(); } catch { }
                        }
                    }
                }
                catch { /* swallow */ }
            }
            return JSValue.Undefined;
        }

        // Fast eval for LARGE trusted scripts (d3.js). Skips DebuggerCallback instrumentation,
        // which triggers NiL.JS Regex issues in UWP build for scripts > 100KB.
        private void SafeEvalFast(string code)
        {
            try
            {
                lock (_nilEvalLock)
                {
                    if (_evalContext != null)
                    {
                        _evalContext.Eval(code);
                    }
                }
            }
            catch (Exception ex)
            {
                try { System.Diagnostics.Debug.WriteLine("[NiLJS] SafeEvalFast: " + ex.GetType().Name + ": " + ex.Message); } catch { }
            }
        }

        private void RunGlobalScript(string js)
        {
            if (string.IsNullOrWhiteSpace(js)) return;
            try { SafeEval(js); } catch { /* swallow */ }
        }

        private static string CollectScriptText(LiteElement n)
        {
            if (n == null) return "";
            if (n.IsText) return n.Text ?? "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n.Children.Count; i++) sb.Append(CollectScriptText(n.Children[i]));
            return sb.ToString();
        }

        #region JS enabled sanitizer
        // INSIDE: public sealed class JavaScriptEngine { ... }
        private void SanitizeForScriptingEnabled(LiteElement rootArg = null)
        {
            var root = rootArg ?? _domRoot;          // OK in instance method
            if (root == null) return;

            // flip no-js ? js on <html>/<body>
            Action<LiteElement> flipClass = n =>
            {
                if (n == null) return;
                var attrs = n.Attr;
                if (attrs == null) return;

                string cls;
                if (!attrs.TryGetValue("class", out cls) || string.IsNullOrWhiteSpace(cls)) return;

                var parts = new HashSet<string>(
                    cls.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries),
                    StringComparer.OrdinalIgnoreCase);

                var changed = false;
                if (parts.Remove("no-js")) changed = true;
                if (!parts.Contains("js")) { parts.Add("js"); changed = true; }
                if (changed) attrs["class"] = string.Join(" ", parts.ToArray());
            };

            try
            {
                var html = (root.QueryByTag("html") ?? Enumerable.Empty<LiteElement>()).FirstOrDefault();
                if (html != null) flipClass(html);
                var body = (root.QueryByTag("body") ?? Enumerable.Empty<LiteElement>()).FirstOrDefault();
                if (body != null) flipClass(body);
            }
            catch { /* swallow */ }

            try
            {
                var toRemove = new List<LiteElement>();
                foreach (var n in root.Descendants())
                    if (string.Equals(n.Tag, "noscript", StringComparison.OrdinalIgnoreCase))
                        toRemove.Add(n);
                foreach (var n in toRemove) n.Parent?.Children.Remove(n);
            }
            catch { /* swallow */ }

            this.RequestRepaint();                    // OK in instance method
        }

        #endregion


        private int ScheduleTimeout(string code, int ms)
        {
            if (!SandboxAllows(SandboxFeature.Scripts, "setTimeout")) return -1;
            if (!SandboxAllows(SandboxFeature.Timers, "setTimeout")) return -1;
            if (ms < 0) ms = 0;
            var id = Interlocked.Increment(ref _nextTimerId);
            var compiled = GetOrCacheFuncDef(code);
            Timer t = null;
            t = new Timer(state =>
            {
                try
                {
                    try { ClearTimeout(id); } catch { /* swallow */ }
                    EnqueueMacroTask(() =>
                    {
                        try { ExecuteCachedInline(compiled); }
                        catch { /* swallow */ }
                    });
                }
                catch { /* swallow */ }
            }, null, ms, Timeout.Infinite);
            lock (_timers) { _timers[id] = t; }
            return id;
        }

        private int ScheduleInterval(string code, int ms)
        {
            if (!SandboxAllows(SandboxFeature.Scripts, "setInterval")) return -1;
            if (!SandboxAllows(SandboxFeature.Timers, "setInterval")) return -1;
            if (ms < 0) ms = 0;
            var id = Interlocked.Increment(ref _nextTimerId);
            var compiled = GetOrCacheFuncDef(code);
            Timer t = null;
            t = new Timer(state =>
            {
                try
                {
                    EnqueueMacroTask(() =>
                    {
                        try { ExecuteCachedInline(compiled); }
                        catch { /* swallow */ }
                    });
                }
                catch { /* swallow */ }
            }, null, ms, ms);
            lock (_timers) { _timers[id] = t; }
            return id;
        }

        private void ExecuteCachedInline(JsFuncDef def)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.Body)) return;
            try { _nilSyncDocument(); SafeEval(def.Body); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[NiLJS] ExecuteCachedInline: " + ex.GetType().Name + ": " + ex.Message); }
        }

        // Small Response-like wrapper for JS-0 so fetch(...).then(fnName) can be invoked as fnName(response)
        // The response provides .text() and .json() methods which call host helper __hostResolveText to
        // schedule the then-callback. We create the object as an immediately-invoked expression string.
        private class SimpleResponse
        {
            private readonly string _body;
            private readonly int _status;
            private readonly string _statusText;
            private readonly bool _ok;
            public SimpleResponse(string body, int status = 200, string statusText = "OK", bool ok = true)
            {
                _body = body ?? "";
                _status = status;
                _statusText = statusText ?? string.Empty;
                _ok = ok;
            }
            // Emit a response object; small bodies are inlined as literals, larger ones are tokenized.
            public string EmitResponseObject(Func<string, string> registerToken, Func<int> getInlineThreshold)
            {
                var inlineThreshold = getInlineThreshold == null ? 1024 : getInlineThreshold();
                if ((_body ?? "").Length <= inlineThreshold)
                {
                    // inline: embed body directly using single-quote escaping
                    var esc = JsEscape(_body ?? "", '\'');
                    var sb = new StringBuilder();
                    sb.Append("(function(){var o={};");
                    sb.Append($"o.status={_status}|0;");
                    sb.Append("o.statusText='").Append(JsEscape(_statusText, '\'')).Append("';");
                    sb.Append(_ok ? "o.ok=true;" : "o.ok=false;");
                    sb.Append("o.text=function(a){if(typeof a==='string')return (function(){a('" + esc + "');return {then:function(fn){fn('" + esc + "');}}})(); if(a&&typeof a.then==='function')return a;return {then:function(fn){fn('" + esc + "');}};};");
                    sb.Append("o.json=function(a){try{var j=JSON.parse('" + esc + "'); if(typeof a==='string') return (function(){__hostResolveText('" + esc + "',a);return {then:function(fn){fn(j);}} })(); if(a&&typeof a.then==='function')return a; return {then:function(fn){fn(j);}} }catch(e){ if(typeof a==='string') return __hostResolveText('" + esc + "',a); if(a&&typeof a.then==='function') return a; return {then:function(fn){fn(null);}}};");
                    sb.Append("return o;})()");
                    return sb.ToString();
                }
                else
                {
                    var token = registerToken != null ? registerToken(_body) : Guid.NewGuid().ToString("N");
                    var sb = new StringBuilder();
                    sb.Append("(function(){var o={};");
                    sb.Append($"o.status={_status}|0;");
                    sb.Append("o.statusText='").Append(JsEscape(_statusText, '\'')).Append("';");
                    sb.Append(_ok ? "o.ok=true;" : "o.ok=false;");
                    sb.Append("o.text=function(a){if(typeof a==='string')return __hostResolveToken('");
                    sb.Append(token);
                    sb.Append("',a);if(a&&typeof a.then==='function')return a;return {then:function(fn){return __hostResolveToken('");
                    sb.Append(token);
                    sb.Append("',fn);}};};");
                    sb.Append("o.json=function(a){if(typeof a==='string')return __hostResolveToken('");
                    sb.Append(token);
                    sb.Append("',a);if(a&&typeof a.then==='function')return a;return {then:function(fn){return __hostResolveToken('");
                    sb.Append(token);
                    sb.Append("',fn);}};};");
                    sb.Append("return o;})()");
                    return sb.ToString();
                }
            }
        }
        // add this inside the BrowserCore.Engine namespace, before JavaScriptEngine
        internal sealed class ResponseEntry
        {
            public string Body;
            public DateTime Ts;

            public ResponseEntry(string body, DateTime ts)
            {
                Body = body ?? string.Empty;
                Ts = ts;
            }
        }

        // Register response body in registry and return token; thread-safe and compact token generation.
        private string RegisterResponseBody(string body)
        {
            try
            {
                var id = Interlocked.Increment(ref _responseCounter);
                var token = "r" + id.ToString("x");
                lock (_responseLock)
                {
                    // Evict expired entries first
                    var now = DateTime.UtcNow;
                    var expired = _responseRegistry.Where(kv => now - kv.Value.Ts > _responseTtl).Select(kv => kv.Key).ToList();
                    foreach (var k in expired) { _responseRegistry.Remove(k); _responseLru.Remove(k); }

                    _responseRegistry[token] = new ResponseEntry(body ?? "", DateTime.UtcNow);
                    _responseLru.AddFirst(token);
                    // Enforce capacity
                    while (_responseLru.Count > _responseCapacity)
                    {
                        var last = _responseLru.Last.Value;
                        _responseLru.RemoveLast();
                        _responseRegistry.Remove(last);
                    }
                }
                return token;
            }
            catch { return Guid.NewGuid().ToString("N"); }
        }

        // Explicit purge of all registered tokens (e.g., on navigation)
        public void PurgeResponseRegistry()
        {
            lock (_responseLock)
            {
                _responseRegistry.Clear();
                _responseLru.Clear();
            }
        }

        // For tests: get current registry count
        public int GetResponseRegistryCount()
        {
            lock (_responseLock) { return _responseRegistry.Count; }
        }
        private System.Net.Http.HttpClient _http;

        private void ClearTimeout(int id)
        {
            lock (_timers)
            {
                Timer t; if (_timers.TryGetValue(id, out t))
                {
                    try { t.Dispose(); } catch { /* swallow */ }
                    _timers.Remove(id);
                }
            }
        }

        private void DispatchToUi(Action action)
        {
            if (action == null) return;
            var repaintHost = _host as IJsHostRepaint;
            if (repaintHost != null)
            {
                try { repaintHost.InvokeOnUiThread(action); return; }
                catch { /* swallow */ }
            }

            try { action(); }
            catch { /* swallow */ }
        }

        private void EnqueueMacroTask(Action action)
        {
            if (action == null) return;

            bool schedulePump = false;
            lock (_macroTaskLock)
            {
                _macroTasks.Enqueue(action);
                if (!_macroPumpScheduled)
                {
                    _macroPumpScheduled = true;
                    schedulePump = true;
                }
            }

            if (schedulePump)
            {
                DispatchToUi(DrainMacroTasks);
            }
        }

        private void DrainMacroTasks()
        {
            while (true)
            {
                Action task = null;
                lock (_macroTaskLock)
                {
                    if (_macroTasks.Count == 0)
                    {
                        _macroPumpScheduled = false;
                        break;
                    }
                    task = _macroTasks.Dequeue();
                }

                if (task == null) continue;

                System.Threading.Interlocked.Exchange(ref _macroExecuting, 1);
                try { task(); }
                catch { /* swallow */ }
                finally
                {
                    try { DrainMicrotasksInternal(); } catch { /* swallow */ }
                    System.Threading.Interlocked.Exchange(ref _macroExecuting, 0);
                }
            }
        }

        private void EnqueueMicrotaskInternal(Action action)
        {
            if (action == null) return;

            bool shouldSchedulePump = false;
            lock (_microtaskLock)
            {
                _microtasks.Enqueue(action);
                var macroRunning = System.Threading.Interlocked.CompareExchange(ref _macroExecuting, 0, 0) != 0;
                if (!macroRunning && !_microtaskPumpScheduled)
                {
                    _microtaskPumpScheduled = true;
                    shouldSchedulePump = true;
                }
            }

            if (shouldSchedulePump)
            {
                DispatchToUi(DrainMicrotasksInternal);
            }
        }

        private const int MicrotaskBatchSize = 16;

        private void DrainMicrotasksInternal()
        {
            int processed = 0;
            bool hasMore = false;
            while (processed < MicrotaskBatchSize)
            {
                Action work = null;
                lock (_microtaskLock)
                {
                    if (_microtasks.Count == 0)
                    {
                        _microtaskPumpScheduled = false;
                        break;
                    }
                    work = _microtasks.Dequeue();
                    if (_microtasks.Count > 0) hasMore = true;
                }

                try { work?.Invoke(); }
                catch { /* swallow */ }
                processed++;
            }

            if (hasMore)
            {
                // Reschedule another batch to avoid starving the UI thread
                DispatchToUi(DrainMicrotasksInternal);
            }
            else
            {
                lock (_microtaskLock) { _microtaskPumpScheduled = false; }
            }
        }

        // Drain any microtasks synchronously on the current thread
        private void FlushMicrotasks()
        {
            DrainMicrotasksInternal();
        }

        private void TraceFeatureGap(string category, string detail, string snippet = null, Exception ex = null)
        {
            try
            {
                var cat = string.IsNullOrWhiteSpace(category) ? "MiniRunner" : category.Trim();
                var det = string.IsNullOrWhiteSpace(detail) ? "unknown" : detail.Trim();
                var sample = snippet ?? string.Empty;
                if (sample.Length > 64) sample = sample.Substring(0, 64);
                var key = cat + "|" + det + "|" + sample;

                lock (_featureTraceLock)
                {
                    if (string.Equals(_lastFeatureTraceKey, key, StringComparison.Ordinal) &&
                        (System.DateTime.UtcNow - _lastFeatureTraceTime).TotalSeconds < 5)
                    {
                        return;
                    }
                    _lastFeatureTraceKey = key;
                    _lastFeatureTraceTime = System.DateTime.UtcNow;
                }

                var builder = new System.Text.StringBuilder();
                builder.Append("[JS gap] ").Append(cat).Append(':').Append(' ').Append(det);

                if (!string.IsNullOrWhiteSpace(snippet))
                {
                    var trimmed = snippet.Trim();
                    if (trimmed.Length > 120) trimmed = trimmed.Substring(0, 120) + "…";
                    builder.Append(" | snippet: ").Append(trimmed);
                }

                if (ex != null && !string.IsNullOrWhiteSpace(ex.Message))
                {
                    builder.Append(" (" + ex.Message.Trim() + ")");
                }

                var message = builder.ToString();
                try { System.Diagnostics.Debug.WriteLine(message); } catch { /* swallow */ }
                try { _host?.SetStatus(message); } catch { /* swallow */ }
            }
            catch { /* swallow */ }
        }

        internal void ReportMiniRunnerFailure(string phase, string snippet, Exception ex)
        {
            if (ex == null) return;
            var category = string.IsNullOrWhiteSpace(phase) ? "MiniRunner" : phase;
            var detail = ex.GetType().Name;
            TraceFeatureGap(category, detail, snippet, ex);
        }

        /// <summary>Run a small, inline handler body (e.g., onclick="..."). Return true if default should be canceled.</summary>
        public bool RunInline(string js, JsContext ctx = null)
        {
            return RunInline(js, ctx, null, null);
        }

        public void PatchD3DomManipulation()
        {
            try
            {
                RunInline(@"
                    (function() {
                        if (typeof d3 === 'undefined' || !d3.selection) {
                            console.log('[DIAG] D3 patch: d3 not available');
                            return;
                        }
                        if (d3.selection.prototype._patched) {
                            console.log('[DIAG] D3 patch already applied');
                            return;
                        }
                        console.log('[DIAG] D3 patch: applying...');
                        var origSelect = d3.select;
                        d3.select = function(selector) {
                            var element = typeof selector === 'string' ? document.querySelector(selector) : selector;
                            var wrapper = {
                                _element: element,
                                append: function(tagName) {
                                    var ns = 'http://www.w3.org/2000/svg';
                                    var newEl = document.createElementNS(ns, tagName);
                                    element.appendChild(newEl);
                                    return d3.select(newEl);
                                },
                                attr: function(name, value) {
                                    element.setAttribute(name, value);
                                    return this;
                                },
                                style: function(name, value) {
                                    element.style[name] = value;
                                    return this;
                                },
                                text: function(value) {
                                    element.textContent = value;
                                    return this;
                                },
                                on: function(type, listener) {
                                    element.addEventListener(type, listener);
                                    return this;
                                }
                            };
                            return wrapper;
                        };
                        d3.selection.prototype._patched = true;
                        console.log('[DIAG] D3 patch applied successfully');
                    })();
                ");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[DIAG] D3 patch exception: " + ex.Message);
            }
        }

        public void InterceptFetch()
        {
            try
            {
                RunInline(@"
                    (function() {
                        if (globalThis.__fetchIntercepted) return;
                        var originalFetch = window.fetch;
                        window.fetch = function(url, options) {
                            console.log('[FETCH] intercepted: ' + url);
                            return originalFetch.apply(this, arguments).then(function(response) {
                                var cloned = response.clone();
                                cloned.text().then(function(text) {
                                    console.log('[FETCH] response from ' + url + ' (first 200 chars): ' + text.substring(0, 200));
                                        try {
                                            var data = JSON.parse(text);
                                            globalThis.__graphData = data;
                                            if (typeof System !== 'undefined') System.__graphData = data;
                                        console.log('[DIAG] Graph data captured, nodes=' + (data.nodes ? data.nodes.length : 0));
                                    } catch(_) {}
                                });
                                return response;
                            });
                        };
                        globalThis.__fetchIntercepted = true;
                        console.log('[DIAG] Fetch interceptor installed');
                    })();
                ");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[DIAG] Fetch interceptor exception: " + ex.Message);
            }
        }

        public void InterceptXhr()
        {
            try
            {
                RunInline(@"
                    (function() {
                        if (globalThis.__xhrIntercepted) return;

                        // If XMLHttpRequest doesn't exist in NiL.JS, create one using fetch
                        var NativeXHR = null;
                        try { NativeXHR = XMLHttpRequest; } catch(e) { NativeXHR = null; }
                        if (!NativeXHR) { NativeXHR = window.XMLHttpRequest; }
                        if (!NativeXHR)
                        {
                            console.log('[XHR] No native XMLHttpRequest — creating fetch-based stub');
                            NativeXHR = function()
                            {
                                // Capture JsMiniRunner instance for closures
var self = this;
                                self.readyState = 0;
                                self.status = 0;
                                self.statusText = '';
                                self.responseText = '';
                                self.response = null;
                                self.responseType = '';
                                self.withCredentials = false;
                                self.timeout = 0;
                                self.onload = null;
                                self.onerror = null;
                                self.onreadystatechange = null;

                                var _method = '';
                                var _url = '';
                                var _headers = {};
                                var _aborted = false;

                                self.open = function(method, url, async, user, password)
                                {
                                    _method = method;
                                    _url = url;
                                    self.readyState = 1;
                                    console.log('[XHR] open ' + method + ' ' + url);
                                };

                                self.setRequestHeader = function(name, value)
                                {
                                    _headers[name] = value;
                                };

                                self.send = function(body)
                                {
                                    if (_aborted) return;
                                    console.log('[XHR] send ' + _method + ' ' + _url);
                                    self.readyState = 2;

                                    window.fetch(_url, {
                                        method: _method,
                                        headers: _headers,
                                        body: body || null
                                    })
                                    .then(function(response)
                                    {
                                        self.status = response.status;
                                        self.statusText = response.statusText || '';
                                        self.readyState = 3;
                                        return response.text();
                                    })
                                    .then(function(text)
                                    {
                                        if (_aborted) return;
                                        self.responseText = text;
                                        self.response = text;
                                        self.readyState = 4;
                                        console.log('[XHR] response from ' + _url + ' status=' + self.status + ' len=' + text.length + ' (first 200): ' + text.substring(0, 200));

                                        try {
                                            var data = JSON.parse(text);
                                            globalThis.__graphData = data;
                                            if (typeof System !== 'undefined') System.__graphData = data;
                                            console.log('[DIAG] Graph data captured via XHR, nodes=' + (data.nodes ? data.nodes.length : 0));
                                        } catch(_) {}

                                        if (self.onload) {
                                            try { self.onload.call(self); } catch(e) { console.log('[XHR] onload error: ' + e); }
                                        }
                                        if (self.onreadystatechange) {
                                            try { self.onreadystatechange.call(self); } catch(e) { console.log('[XHR] onreadystatechange error: ' + e); }
                                        }
                                    })
                                    .catch(function(err)
                                    {
                                        if (_aborted) return;
                                        console.log('[XHR] error for ' + _url + ': ' + err);
                                        self.status = 0;
                                        if (self.onerror) {
                                            try { self.onerror.call(self, err); } catch(e) { console.log('[XHR] onerror error: ' + e); }
                                        }
                                    });
                                };

                                self.abort = function() { _aborted = true; };
                                self.getResponseHeader = function(name) { return null; };
                                self.getAllResponseHeaders = function() { return ''; };
                                self.overrideMimeType = function(mime) {};
                            };
                        }
                        else
                        {
                            console.log('[XHR] Native XMLHttpRequest found — wrapping with logging');
                        }

                        // Wrap the constructor with logging interceptor
                        window.XMLHttpRequest = function() {
                            var xhr = new NativeXHR();
                            var _url = '';
                            var _method = '';
                            var _headers = {};
                            var origOpen = xhr.open.bind(xhr);
                            var origSend = xhr.send.bind(xhr);
                            var origSetReqHdr = xhr.setRequestHeader.bind(xhr);
                            xhr.open = function(method, url, async, user, password) {
                                _method = method;
                                _url = url;
                                console.log('[XHR] open ' + method + ' ' + url);
                                return origOpen(method, url, async, user, password);
                            };
                            xhr.setRequestHeader = function(name, value) {
                                _headers[name] = value;
                                return origSetReqHdr(name, value);
                            };
                            xhr.send = function(body) {
                                console.log('[XHR] send ' + _method + ' ' + _url);
                                var origOnLoad = xhr.onload;
                                xhr.onload = function() {
                                    var text = '';
                                    try { text = xhr.responseText || ''; } catch(e) {}
                                    console.log('[XHR] response from ' + _url + ' status=' + xhr.status + ' len=' + text.length + ' (first 200): ' + text.substring(0, 200));
                                    if (origOnLoad) origOnLoad.call(xhr);
                                };
                                return origSend(body);
                            };
                            return xhr;
                        };
                        globalThis.__xhrIntercepted = true;
                        console.log('[DIAG] XHR interceptor installed');
                    })();
                ");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[DIAG] XHR interceptor exception: " + ex.Message);
            }
        }

        /// <summary>
        /// Search for global data objects (nodes/links arrays or embedded JSON) and report them.
        /// </summary>
        public void SearchGlobalData()
        {
            try
            {
                RunInline(@"
                    (function() {
                        try {
                            // Search for {nodes, links} pattern objects
                            var foundData = false;
                            for (var key in window) {
                                try {
                                    var val = window[key];
                                    if (!val || typeof val !== 'object') continue;
                                    // Check for {nodes, links}
                                    if (val.nodes && val.links && Array.isArray(val.nodes) && Array.isArray(val.links)) {
                                        console.log('[DIAG:DATA] Found graph data at window.' + key + ' nodes=' + val.nodes.length + ' links=' + val.links.length);
                                        foundData = true;
                                    }
                                    // Check for large arrays that might be node/link data
                                    if (Array.isArray(val)) {
                                        var first = val[0];
                                        if (first && typeof first === 'object') {
                                            var fkeys = Object.keys(first);
                                            if (fkeys.indexOf('source') >= 0 || fkeys.indexOf('target') >= 0 || fkeys.indexOf('x') >= 0 || fkeys.indexOf('y') >= 0) {
                                                console.log('[DIAG:DATA] Found data array at window.' + key + ' len=' + val.length + ' sample_keys=' + JSON.stringify(fkeys));
                                                foundData = true;
                                            }
                                        }
                                    }
                                } catch(e) { /* ignore individual key errors */ }
                            }
                            // Check for __INITIAL_STATE__ or __NEXT_DATA__ or __NUXT__
                            if (typeof window.__INITIAL_STATE__ !== 'undefined') {
                                console.log('[DIAG:DATA] Found __INITIAL_STATE__ type=' + typeof window.__INITIAL_STATE__);
                            }
                            if (typeof window.__NEXT_DATA__ !== 'undefined') {
                                console.log('[DIAG:DATA] Found __NEXT_DATA__ type=' + typeof window.__NEXT_DATA__);
                            }
                            if (typeof window.__NUXT__ !== 'undefined') {
                                console.log('[DIAG:DATA] Found __NUXT__ type=' + typeof window.__NUXT__);
                            }
                            // Check for data embedded in script tags (as JSON)
                            var scripts = document.getElementsByTagName('script');
                            for (var i = 0; i < scripts.length; i++) {
                                var type = scripts[i].getAttribute('type') || '';
                                var src = scripts[i].getAttribute('src') || '';
                                if (type.indexOf('json') >= 0 || type.indexOf('data') >= 0) {
                                    console.log('[DIAG:DATA] Script #' + i + ' type=' + type + ' src=' + src + ' textLen=' + (scripts[i].textContent || '').length);
                                }
                            }
                            if (!foundData) console.log('[DIAG:DATA] No graph data found in global scope');
                        } catch(e) { console.log('[DIAG:DATA] Search error: ' + e.message); }
                    })();
                ");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[DIAG:DATA] SearchGlobalData exception: " + ex.Message);
            }
        }

        /// <summary>Search SystemJS chunk text for embedded graph data (nodes/links arrays).</summary>
        private static void SearchChunkForGraphData(string chunk)
        {
            if (string.IsNullOrEmpty(chunk) || chunk.Length < 100) return;

            int nodesIdx = chunk.IndexOf("nodes", StringComparison.Ordinal);
            if (nodesIdx < 0) nodesIdx = chunk.IndexOf("\"nodes\"", StringComparison.Ordinal);
            int linksIdx = chunk.IndexOf("links", StringComparison.Ordinal);
            if (linksIdx < 0) linksIdx = chunk.IndexOf("\"links\"", StringComparison.Ordinal);

            if (nodesIdx >= 0 || linksIdx >= 0)
            {
                var foundMsg = "[DIAG:CHUNK] Found 'nodes' at offset " + nodesIdx
                    + ", 'links' at offset " + linksIdx;
                System.Diagnostics.Debug.WriteLine(foundMsg);
                DevToolsLogger.Log(foundMsg);
                // Sample context around nodes (if it's a different location than links)
                if (nodesIdx >= 0 && nodesIdx != linksIdx)
                {
                    int start = Math.Max(0, nodesIdx - 80);
                    int len = Math.Min(200, chunk.Length - start);
                    var ctx = chunk.Substring(start, len);
                    System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Context around 'nodes': "
                        + ctx.Replace("\r", "").Replace("\n", "\\n"));

                    // Dump 500 chars after nodes to see the full expression
                    int afterStart = nodesIdx;
                    int afterLen = Math.Min(500, chunk.Length - afterStart);
                    var afterCtx = chunk.Substring(afterStart, afterLen);
                    System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] 500 chars AFTER 'nodes': "
                        + afterCtx.Replace("\r", "").Replace("\n", "\\n"));
                }
                // Sample context around links
                if (linksIdx >= 0)
                {
                    int start = Math.Max(0, linksIdx - 80);
                    int len = Math.Min(300, chunk.Length - start);
                    var ctx = chunk.Substring(start, len);
                    System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Context around 'links': "
                        + ctx.Replace("\r", "").Replace("\n", "\\n"));
                }
                // Dump 200 chars BEFORE nodes to see where n/t come from
                if (nodesIdx >= 0)
                {
                    int beforeStart = Math.Max(0, nodesIdx - 300);
                    int beforeLen = Math.Min(300, nodesIdx - beforeStart);
                    var beforeCtx = chunk.Substring(beforeStart, beforeLen);
                    System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] 300 chars BEFORE 'nodes': "
                        + beforeCtx.Replace("\r", "").Replace("\n", "\\n"));
                }
            }
            else
            {
                var noMsg = "[DIAG:CHUNK] No 'nodes'/'links' found in chunk — data is loaded externally or uses different naming";
                System.Diagnostics.Debug.WriteLine(noMsg);
                DevToolsLogger.Log(noMsg);
            }

            // Search for large array assignments: =[{ or =[{\"
            int arrayStart = chunk.IndexOf("=[{", StringComparison.Ordinal);
            if (arrayStart < 0) arrayStart = chunk.IndexOf("= [{\"", StringComparison.Ordinal);
            if (arrayStart >= 0)
            {
                System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Found large object array pattern at offset " + arrayStart);
                int previewStart = Math.Max(0, arrayStart - 40);
                int previewLen = Math.Min(200, chunk.Length - previewStart);
                System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Preview: " + chunk.Substring(previewStart, previewLen)
                    .Replace("\r", "").Replace("\n", "\\n"));
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] No large object array assignment found");
            }

            // Search for link-like data: source/target pairs (minified JS pattern)
            int sourceIdx = chunk.IndexOf("source:", StringComparison.Ordinal);
            if (sourceIdx < 0) sourceIdx = chunk.IndexOf("\"source\":", StringComparison.Ordinal);
            if (sourceIdx >= 0)
            {
                int contextStart = Math.Max(0, sourceIdx - 100);
                int contextLen = Math.Min(200, chunk.Length - contextStart);
                System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Found 'source:' at offset " + sourceIdx
                    + " ctx: " + chunk.Substring(contextStart, contextLen).Replace("\r", "").Replace("\n", "\\n"));
                int targetIdx = chunk.IndexOf("target:", sourceIdx, StringComparison.Ordinal);
                if (targetIdx < 0) targetIdx = chunk.IndexOf("\"target\":", sourceIdx, StringComparison.Ordinal);
                if (targetIdx >= 0)
                {
                    int tCtxStart = Math.Max(0, targetIdx - 60);
                    int tCtxLen = Math.Min(120, chunk.Length - tCtxStart);
                    System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Found 'target:' nearby at offset " + targetIdx
                        + " ctx: " + chunk.Substring(tCtxStart, tCtxLen).Replace("\r", "").Replace("\n", "\\n"));
                }
            }
            else
            {
                // Check for non-colon pattern: source, - might be in different format
                int sourceStrIdx = chunk.IndexOf("\"source\"", StringComparison.Ordinal);
                if (sourceStrIdx >= 0)
                    System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Found '\"source\"' (string key) at offset " + sourceStrIdx);
                else
                    System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] No 'source'/'target' link data found in chunk — data is not embedded as static JSON");
            }

            // Count how many data-like object patterns exist in the chunk
            int braceCount = 0;
            int searchFrom = 0;
            while ((searchFrom = chunk.IndexOf(":{", searchFrom, StringComparison.Ordinal)) >= 0 && searchFrom < chunk.Length - 2)
            {
                // Check if followed by " (string value) or number or [
                char next = chunk[searchFrom + 2];
                if (next == '{') { /* nested object */ }
                braceCount++;
                searchFrom += 2;
                if (braceCount > 100) break;
            }
            System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Total ':{' occurrences (object nesting): " + braceCount);

            // Dump the first 1000 chars of the chunk to understand module structure
            int dumpLen = Math.Min(1000, chunk.Length);
            var dump = chunk.Substring(0, dumpLen);
            System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] First 1000 chars: " + dump.Replace("\r", "").Replace("\n", "\\n"));

            // If chunk is big, also dump a region around 40% (might be the data load area)
            if (chunk.Length > 200000)
            {
                int midRegion = chunk.Length / 3;
                int midDumpLen = Math.Min(400, chunk.Length - midRegion);
                System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Dump at offset " + midRegion + ": "
                    + chunk.Substring(midRegion, midDumpLen).Replace("\r", "").Replace("\n", "\\n"));
            }
        }

        /// <summary>Run inline JavaScript with optional event metadata (type/id) for the Mini interpreter.</summary>
        public bool RunInline(string js, JsContext ctx, string eventType, string eventTargetId)
        {
            if (string.IsNullOrWhiteSpace(js)) return false;
            var detail = string.IsNullOrEmpty(eventType)
                ? (ctx != null && ctx.BaseUri != null ? "inline " + ctx.BaseUri : "inline")
                : (string.IsNullOrEmpty(eventTargetId) ? eventType : eventType + "@" + eventTargetId);
            if (!SandboxAllows(SandboxFeature.Scripts, detail)) return false;
            if (!string.IsNullOrEmpty(eventType) && !SandboxAllows(SandboxFeature.InlineScripts, detail)) return false;
            // preserve/override context while running
            var prevCtx = _ctx;
            if (ctx != null) _ctx = ctx;
            _preventDefaultRequested = false;
            try
            {
                if (_nil != null)
                {
                    _nilSyncDocument();
                    var expr = "(function(){ var __r=(function(){" + js + "})(); return __r===false; })()";
                    var val = SafeEval(expr);
                    if (val != null && string.Equals(val.ToString(), "true", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { /* swallow */ }
            bool miniHandled = false;
            bool miniCanceled = false;
            try
            {
                if (UseMiniPrattEngine)
                {
                    var miniRunner = new JsMiniRunner(this);
                    bool? cancelDefault;
                    if (miniRunner.TryRunInline(js, eventType, eventTargetId, out cancelDefault))
                    {
                        miniHandled = true;
                        if (cancelDefault.HasValue && cancelDefault.Value) miniCanceled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                ReportMiniRunnerFailure("RunInline", js, ex);
                // JS error occurred: ex.Message (status reporting not available in this context)
                miniHandled = false; miniCanceled = false;
            }

            if (miniHandled)
            {
                if (_preventDefaultRequested || miniCanceled) return true;
                return false;
            }

            // JS-0 allowlist
            var parts = js.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in parts)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                // Fast-path: first-token dispatch tries only the relevant subset of patterns
                var token = GetFirstToken(line);
                if (token != null && TryPatternsByToken(line, token, _ctx)) continue;
                // Phase 1/2/3 builtins
                if (HandlePhase123Builtins(line, _ctx)) continue;

                // return false;
                if (RxReturnFalse.IsMatch(line)) { _preventDefaultRequested = true; return true; }
                // Separate default prevention vs propagation: only preventDefault cancels default.
                if (RxEventPreventDefault.IsMatch(line)) { _preventDefaultRequested = true; return true; }
                if (RxEventStopPropagation.IsMatch(line)) { _stopPropagationRequested = true; continue; }

                // document.getElementById('id').innerHTML = '...'
                var mHtmlAssign = System.Text.RegularExpressions.Regex.Match(line,
                    @"^\s*document\s*\.\s*getElementById\s*\(\s*(['""])(?<id>.+?)\1\s*\)\s*\.\s*innerHTML\s*=\s*(['""])(?<val>.*?)\3\s*;?\s*$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (mHtmlAssign.Success && _domRoot != null)
                {
                    try
                    {
                        var id = mHtmlAssign.Groups["id"].Value;
                        var val = mHtmlAssign.Groups["val"].Value;
                        var doc = new JsDocument(this, _domRoot);
                        var el = doc.getElementById(id) as JsDomElement;
                        if (el != null) { el.innerHTML = val; RequestRepaint(); }
                    }
                    catch { /* swallow */ }
                    continue;
                }

                // document.getElementById('id').setInnerHTML('...', true|false)
                var mSetHtml = System.Text.RegularExpressions.Regex.Match(line,
                    @"^\s*document\s*\.\s*getElementById\s*\(\s*(['""])(?<id>.+?)\1\s*\)\s*\.\s*setInnerHTML\s*\(\s*(['""])(?<val>.*?)\3\s*,\s*(?<flag>true|false)\s*\)\s*;?\s*$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (mSetHtml.Success && _domRoot != null)
                {
                    try
                    {
                        var id = mSetHtml.Groups["id"].Value;
                        var val = mSetHtml.Groups["val"].Value;
                        var flag = string.Equals(mSetHtml.Groups["flag"].Value, "true", StringComparison.OrdinalIgnoreCase);
                        var doc = new JsDocument(this, _domRoot);
                        var el = doc.getElementById(id) as JsDomElement;
                        if (el != null) { el.setInnerHTML(val, flag); RequestRepaint(); }
                    }
                    catch { /* swallow */ }
                    continue;
                }

                // location.href = ' '
                var mHref = RxHrefAssign.Match(line);
                if (mHref.Success) { Navigate(mHref.Groups["url"].Value); continue; }

                // window.location=  or window.location.href= 
                var mW = RxWindowLoc.Match(line);
                if (mW.Success) { Navigate(mW.Groups["url"].Value); continue; }

                // location.assign(' ')
                var mAssign = RxAssignCall.Match(line);
                if (mAssign.Success) { Navigate(mAssign.Groups["url"].Value); continue; }

                // location.replace(' ')
                var mReplace = RxReplaceCall.Match(line);
                if (mReplace.Success) { Navigate(mReplace.Groups["url"].Value); continue; }

                // alert(' ')
                var mAlert = RxAlert.Match(line);
                if (mAlert.Success) { _host.SetStatus(mAlert.Groups["msg"].Value ?? ""); continue; }

                // console.log(' ')
                var mLog = RxConsoleLog.Match(line);
                if (mLog.Success) { _host.SetStatus(mLog.Groups["msg"].Value ?? ""); continue; }
                var mOpen = RxWindowOpen.Match(line);
                if (mOpen.Success) { Navigate(mOpen.Groups["url"].Value); continue; }
                if (RxLocationReload.IsMatch(line)) { try { if (_ctx?.BaseUri != null) _host.Navigate(_ctx.BaseUri); else RequestRepaint(); } catch { /* swallow */ } continue; }
                if (RxVoidZero.IsMatch(line)) { continue; }

                // document.getElementById('id').click()
                var mClick = System.Text.RegularExpressions.Regex.Match(
                    line,
                    @"^\s*document\s*\.\s*getElementById\s*\(\s*(['""])(?<id>[^'""]+)\1\s*\)\s*\.\s*click\s*\(\s*\)\s*;?\s*$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
                if (mClick.Success)
                {
                    var id = mClick.Groups["id"].Value;
                    try { RaiseElementEvent(id, "click"); } catch { /* swallow */ }
                    continue;
                }

                // document.getElementById('id').innerText = '...'
                var mSetText = RxGetByIdInnerTextAssign.Match(line);
                if (mSetText.Success)
                {
                    var id = mSetText.Groups["id"].Value;
                    var val = mSetText.Groups["val"].Value;
                    if (_domRoot != null)
                    {
                        var doc = new JsDocument(this, _domRoot);
                        var el = doc.getElementById(id) as JsDomElement;
                        if (el != null) el.innerText = val;
                    }
                    continue;
                }

                // document.getElementById('id').setAttribute('name','value')
                var mSetAttr = RxGetByIdSetAttr.Match(line);
                if (mSetAttr.Success)
                {
                    var id = mSetAttr.Groups["id"].Value;
                    var an = mSetAttr.Groups["an"].Value;
                    var av = mSetAttr.Groups["av"].Value;
                    if (_domRoot != null)
                    {
                        var doc = new JsDocument(this, _domRoot);
                        var el = doc.getElementById(id) as JsDomElement;
                        if (el != null) el.setAttribute(an, av);
                    }
                    continue;
                }

                // document.getElementById('id').style.prop = 'val'
                var mStyle = RxElStyleSet.Match(line);
                if (mStyle.Success)
                {
                    var id = mStyle.Groups["id"].Value;
                    var prop = (mStyle.Groups["prop"].Value ?? "").Trim().ToLowerInvariant();
                    var val = mStyle.Groups["val"].Value ?? "";
                    TryUpdateInlineStyle(id, prop, val);
                    continue;
                }

                // document.getElementById('id').classList.add/remove/toggle('cls')
                var mCls = RxElClassListOp.Match(line);
                if (mCls.Success)
                {
                    var id = mCls.Groups["id"].Value; var op = mCls.Groups["op"].Value.ToLowerInvariant(); var cls = mCls.Groups["cls"].Value;
                    TryUpdateClassList(id, op, cls);
                    continue;
                }

                // document.getElementById('id').onclick = fnName  (and oninput/onchange)
                var mOn = RxElOnAssign.Match(line);
                if (mOn.Success)
                {
                    var id = mOn.Groups["id"].Value; var evt = mOn.Groups["evt"].Value.ToLowerInvariant(); var fn = mOn.Groups["fn"].Value;
                    var evtName = evt == "click" ? "click" : evt == "input" ? "input" : evt == "change" ? "change" : evt;
                    RegisterElementListener(id, evtName, fn);
                    continue;
                }

                // setTimeout('code', ms)
                var mTO = RxSetTimeout.Match(line);
                if (mTO.Success)
                {
                    var code = mTO.Groups["code"].Value;
                    int ms; if (!int.TryParse(mTO.Groups["ms"].Value, out ms)) ms = 0;
                    var id = ScheduleTimeout(code, ms);
                    // no return value to JS-0, but we can SetStatus with id for debugging
                    try { _host.SetStatus("setTimeout id=" + id); } catch { /* swallow */ }
                    continue;
                }

                // setTimeout(fnName, ms)
                var mTO2 = RxSetTimeoutFn.Match(line);
                if (mTO2.Success)
                {
                    int ms2 = 0; int.TryParse(mTO2.Groups["ms"].Value, out ms2);
                    var fn = mTO2.Groups["fn"].Value;
                    var id2 = ScheduleTimeout(fn + "()", ms2);
                    try { _host.SetStatus("setTimeout id=" + id2); } catch { /* swallow */ }
                    continue;
                }

                // setInterval('code', ms)
                var mSI = RxSetInterval.Match(line);
                if (mSI.Success)
                {
                    var code = mSI.Groups["code"].Value; int ms; if (!int.TryParse(mSI.Groups["ms"].Value, out ms)) ms = 0;
                    var id = ScheduleInterval(code, ms);
                    try { _host.SetStatus("setInterval id=" + id); } catch { /* swallow */ }
                    continue;
                }

                // setInterval(fnName, ms)
                var mSIF = RxSetIntervalFn.Match(line);
                if (mSIF.Success)
                {
                    int ms = 0; int.TryParse(mSIF.Groups["ms"].Value, out ms);
                    var fn = mSIF.Groups["fn"].Value;
                    var id = ScheduleInterval(fn + "()", ms);
                    try { _host.SetStatus("setInterval id=" + id); } catch { /* swallow */ }
                    continue;
                }

                // clearTimeout(id)
                var mClear = RxClearTimeout.Match(line);
                if (mClear.Success)
                {
                    int id; if (int.TryParse(mClear.Groups["id"].Value, out id)) ClearTimeout(id);
                    continue;
                }

                // clearInterval(id)
                var mCI = RxClearInterval.Match(line);
                if (mCI.Success)
                {
                    int id; if (int.TryParse(mCI.Groups["id"].Value, out id)) ClearTimeout(id);
                    continue;
                }

                // history.pushState/replaceState(..., url)
                var mHP = RxHistoryPush.Match(line);
                if (mHP.Success) { var u = Resolve(_ctx?.BaseUri, mHP.Groups["url"].Value); if (u != null) { HistoryPush(u); } continue; }
                var mHR = RxHistoryReplace.Match(line);
                if (mHR.Success) { var u = Resolve(_ctx?.BaseUri, mHR.Groups["url"].Value); if (u != null) { HistoryReplace(u); } continue; }

                                // navigator.serviceWorker.register(...) no-op to keep sites from hard-failing
                if (Regex.IsMatch(line, @"^\s*navigator\s*\.\s*serviceWorker\s*\.\s*register\s*\(", RegexOptions.IgnoreCase))
                {
                    try { _host.SetStatus("serviceWorker.register: no-op"); } catch { /* swallow */ }
                    continue;
                }// fetchText('url','elementId') -> populates element.innerText with fetched content
                var mFetch = RxFetchText.Match(line);
                if (mFetch.Success)
                {
                    var url = mFetch.Groups["url"].Value;
                    var id = mFetch.Groups["id"].Value;
                    var resolved = Resolve(_ctx?.BaseUri ?? null, url);
                    if (resolved != null && _domRoot != null)
                    {
                        // fire-and-forget fetch; when done, set element innerText and request repaint
                        Task.Run(async () =>
                        {
                            try
                            {
                                string txt;
                                if (FetchOverride != null) txt = await FetchOverride(resolved).ConfigureAwait(false);
                                else txt = await _http.GetStringAsync(resolved).ConfigureAwait(false);
                                var doc = new JsDocument(this, _domRoot);
                                var el = doc.getElementById(id) as JsDomElement;
                                if (el != null) { el.innerText = txt; RequestRepaint(); }
                            }
                            catch (Exception ex) { try { _host.SetStatus("fetchText failed: " + ex.Message); } catch { /* swallow */ } }
                        });
                    }
                    continue;
                }

                // fetch('url').then('code') -> fetch then execute inline code (supports '%s' substitution)
                var mFThen = RxFetchThen.Match(line);
                if (mFThen.Success)
                {
                    var url = mFThen.Groups["url"].Value;
                    var code = mFThen.Groups["code"].Value;
                    var resolved = Resolve(_ctx?.BaseUri ?? null, url);
                    if (resolved != null)
                    {
                        Task.Run(async () =>
                        {
                            try
                            {
                                string txt = null;
                                if (FetchOverride != null) txt = await FetchOverride(resolved).ConfigureAwait(false);
                                else txt = await FetchScriptStringAsync(resolved, _ctx?.BaseUri).ConfigureAwait(false);
                                var exec = code;
                                if (!string.IsNullOrEmpty(exec) && exec.Contains("%s")) exec = exec.Replace("%s", txt ?? "");
                                try { RunInline(exec, _ctx); } catch { /* swallow */ }
                            }
                            catch { /* swallow */ }
                        });
                    }
                    continue;
                }

                // fetch('url').then(fnName) -> fetch then call fnName(fetchedText)
                var mFThenFn = RxFetchThenFunc.Match(line);
                if (mFThenFn.Success)
                {
                    var url = mFThenFn.Groups["url"].Value;
                    var fn = mFThenFn.Groups["fn"].Value;
                    var resolved = Resolve(_ctx?.BaseUri ?? null, url);
                    if (resolved != null)
                    {
                        Task.Run(async () =>
                        {
                            try
                            {
                                string txt = null;
                                if (FetchOverride != null) txt = await FetchOverride(resolved).ConfigureAwait(false);
                                else txt = await FetchScriptStringAsync(resolved, _ctx?.BaseUri).ConfigureAwait(false);
                                // schedule as microtask to run in JS-0 and pass a lightweight Response-like object
                                var sr = new SimpleResponse(txt);
                                var respExpr = sr.EmitResponseObject(RegisterResponseBody, () => _inlineThreshold);
                                EnqueueMicrotask(() => { try { RunInline(fn + "(" + respExpr + ")", _ctx); } catch { /* swallow */ } });
                            }
                            catch { /* swallow */ }
                        });
                    }
                    continue;
                }

                // __hostResolveToken('token','fnName') -> run microtask calling fnName(body) with body from registry
                if (line.StartsWith("__hostResolveToken(", StringComparison.Ordinal))
                {
                    try
                    {
                        var inside = line.Substring("__hostResolveToken(".Length).Trim();
                        if (inside.EndsWith(")")) inside = inside.Substring(0, inside.Length - 1).Trim();
                        // split token and fnName
                        var comma = inside.IndexOf(',');
                        if (comma > 0)
                        {
                            var tokenPart = inside.Substring(0, comma).Trim();
                            var fnPart = inside.Substring(comma + 1).Trim();
                            if ((tokenPart.StartsWith("\"") && tokenPart.EndsWith("\"")) || (tokenPart.StartsWith("'") && tokenPart.EndsWith("'")))
                                tokenPart = tokenPart.Substring(1, tokenPart.Length - 2);
                            if ((fnPart.StartsWith("\"") && fnPart.EndsWith("\"")) || (fnPart.StartsWith("'") && fnPart.EndsWith("'")))
                                fnPart = fnPart.Substring(1, fnPart.Length - 2);
                            string body = null;
                            lock (_responseLock)
                            {
                                ResponseEntry pair;
                                if (_responseRegistry.TryGetValue(tokenPart, out pair))
                                {
                                    var now = DateTime.UtcNow;
                                    if (now - pair.Ts > _responseTtl)
                                    {
                                        // expired
                                        _responseRegistry.Remove(tokenPart);
                                        _responseLru.Remove(tokenPart);
                                    }
                                    else
                                    {
                                        body = pair.Body;
                                        // update recency
                                        _responseRegistry[tokenPart] = new ResponseEntry(pair.Body, now);
                                        _responseLru.Remove(tokenPart);
                                        _responseLru.AddFirst(tokenPart);
                                    }
                                }
                            }
                            if (body != null)
                            {
                                // schedule callback depending on expected resolution type: if fnPart appears to expect JSON
                                // we try to parse and emit a JS literal representing the parsed object for json().then(fn)
                                if (fnPart.EndsWith(".json_callback", StringComparison.Ordinal))
                                {
                                    // internal marker: if fn name ends with .json_callback we unwrap as JSON
                                    try
                                    {
                                        var parsed = Windows.Data.Json.JsonValue.Parse(body) as Windows.Data.Json.IJsonValue;
                                        var literal = JsonToJsLiteral(parsed);
                                        EnqueueMicrotask(() => { try { RunInline(fnPart.Substring(0, fnPart.Length - ".json_callback".Length) + "(" + literal + ")", _ctx); } catch { /* swallow */ } });
                                    }
                                    catch
                                    {
                                        var escErr = JsEscape(body, '\'');
                                        EnqueueMicrotask(() => { try { RunInline(fnPart + "('" + escErr + "')", _ctx); } catch { /* swallow */ } });
                                    }
                                }
                                else
                                {
                                    var esc = JsEscape(body, '\'');
                                    EnqueueMicrotask(() => { try { RunInline(fnPart + "('" + esc + "')", _ctx); } catch { /* swallow */ } });
                                }
                            }
                        }
                    }
                    catch { /* swallow */ }
                    continue;
                }

                // Promise.resolve().then(fnName) -> schedule microtask that calls fnName()
                var mPThenFn = RxPromiseThenFunc.Match(line);
                if (mPThenFn.Success)
                {
                    var fn = mPThenFn.Groups["fn"].Value;
                    EnqueueMicrotask(() => { try { RunInline(fn + "()", _ctx); } catch { /* swallow */ } });
                    continue;
                }

                // Promise.resolve().then('code') -> schedule microtask
                if (line.StartsWith("Promise.resolve().then(", StringComparison.Ordinal))
                {
                    try
                    {
                        var inside = line.Substring("Promise.resolve().then(".Length).Trim();
                        if (inside.EndsWith(")")) inside = inside.Substring(0, inside.Length - 1).Trim();
                        if (inside.StartsWith("\"") && inside.EndsWith("\""))
                        {
                            var code = inside.Substring(1, inside.Length - 2);
                            EnqueueMicrotask(() => { try { RunInline(code, ctx); } catch { /* swallow */ } });
                        }
                    }
                    catch { /* swallow */ }
                    continue;
                }

                // minimal support for scheduling microtasks via Promise.resolve().then(fn) style patterns
                if (line.StartsWith("__enqueueMicrotask(", StringComparison.Ordinal))
                {
                    // pattern: __enqueueMicrotask('code'); we'll eval code later on threadpool
                    var arg = line.Substring("__enqueueMicrotask(".Length).Trim();
                    if (arg.EndsWith(")")) arg = arg.Substring(0, arg.Length - 1).Trim();
                    if (arg.StartsWith("\"") && arg.EndsWith("\"")) arg = arg.Substring(1, arg.Length - 2);
                    var codeToRun = arg;
                    EnqueueMicrotask(() => { try { RunInline(codeToRun, ctx); } catch { /* swallow */ } });
                    continue;
                }

                // __hostResolveText('<body>','fnName') -> schedule microtask to call fnName('<body>')
                if (line.StartsWith("__hostResolveText(", StringComparison.Ordinal))
                {
                    try
                    {
                        var inside = line.Substring("__hostResolveText(".Length).Trim();
                        if (inside.EndsWith(")")) inside = inside.Substring(0, inside.Length - 1).Trim();
                        // naive split by comma (body may contain commas but is quoted)
                        var comma = inside.LastIndexOf(',');
                        if (comma > 0)
                        {
                            var bodyPart = inside.Substring(0, comma).Trim();
                            var fnPart = inside.Substring(comma + 1).Trim();
                            // strip surrounding quotes from fnPart
                            if ((fnPart.StartsWith("\"") && fnPart.EndsWith("\"")) || (fnPart.StartsWith("'") && fnPart.EndsWith("'")))
                                fnPart = fnPart.Substring(1, fnPart.Length - 2);
                            // strip quotes from bodyPart if present
                            string body = bodyPart;
                            if ((body.StartsWith("\"") && body.EndsWith("\"")) || (body.StartsWith("'") && body.EndsWith("'")))
                                body = body.Substring(1, body.Length - 2);
                            // schedule microtask that calls fnPart(body)
                            var escaped = body; // body is already host-escaped when embedded
                            EnqueueMicrotask(() => { try { RunInline(fnPart + "('" + escaped + "')", _ctx); } catch { /* swallow */ } });
                        }
                    }
                    catch { /* swallow */ }
                    continue;
                }

                // --------- XHR host shims (moved OUTSIDE __hostResolveText block so they execute) ---------
                var mXnew = RxXhrNew.Match(line);
                if (mXnew.Success) { XhrNew(mXnew.Groups["id"].Value); continue; }

                var mXopen = RxXhrOpen.Match(line);
                if (mXopen.Success) { XhrOpen(mXopen.Groups["id"].Value, mXopen.Groups["m"].Value, mXopen.Groups["url"].Value, ctx); continue; }

                var mXhdr = RxXhrSetHdr.Match(line);
                if (mXhdr.Success) { XhrSetHeader(mXhdr.Groups["id"].Value, mXhdr.Groups["n"].Value, mXhdr.Groups["v"].Value); continue; }

                var mXsend = RxXhrSend.Match(line);
                if (mXsend.Success)
                {
                    var id = mXsend.Groups["id"].Value;
                    var body = mXsend.Groups["body"].Success ? mXsend.Groups["body"].Value : null;
                    var onload = mXsend.Groups["onload"].Value;
                    var onerr = mXsend.Groups["onerror"].Success ? mXsend.Groups["onerror"].Value : null;
                    XhrSend(id, body, onload, onerr, ctx);
                    continue;
                }

                var mXdel = RxXhrDeliver.Match(line);
                if (mXdel.Success)
                {
                    var xhrToken = mXdel.Groups["token"].Value;
                    var fn = mXdel.Groups["fn"].Value;
                    int st = 0; int.TryParse(mXdel.Groups["status"].Value, out st);
                    string body = null;
                    lock (_responseLock)
                    {
                        ResponseEntry pair;
                        if (_responseRegistry.TryGetValue(xhrToken, out pair))
                        {
                            var now = DateTime.UtcNow;
                            if (now - pair.Ts <= _responseTtl)
                            {
                                body = pair.Body;
                                _responseRegistry[xhrToken] = new ResponseEntry(pair.Body, now);
                                _responseLru.Remove(xhrToken); _responseLru.AddFirst(xhrToken);
                            }
                            else { _responseRegistry.Remove(xhrToken); _responseLru.Remove(xhrToken); }
                        }
                    }
                    if (body != null)
                    {
                        var esc = JsEscape(body, '\'');
                        EnqueueMicrotask(() => { try { RunInline(fn + "(" + st + ",'" + esc + "')", _ctx); } catch { /* swallow */ } });
                    }
                    continue;
                }
            }

            // restore context
            if (ctx != null) _ctx = prevCtx;
            return false;
        }

        public async Task RunScriptsAsync(LiteElement domRoot, Uri baseUri)
        {
            if (domRoot == null) return;
            try { System.Diagnostics.Debug.WriteLine("[Diag] RunScriptsAsync start (Phase123) - Parallelized"); } catch { /* swallow */ }
            try
            {

            _domRoot = domRoot;
            // Reset per-page script budget at the start of a new run.
            _pageScriptBytesUsed = 0;

            var immediate = new List<LiteElement>();
            var deferred = new List<LiteElement>();
            var asyncs = new List<LiteElement>();

            // classify
            foreach (var n in domRoot.Descendants())
            {
                if (n.Tag != "script") continue;
                var hasAsync = n.Attr != null && (n.Attr.ContainsKey("async"));
                var hasDefer = n.Attr != null && (n.Attr.ContainsKey("defer"));
                var type = n.Attr != null && n.Attr.ContainsKey("type") ? (n.Attr["type"] ?? "").Trim().ToLowerInvariant() : "";
                var treatAsDefer = hasDefer || type == "module";

                // Phase T+ / Nokia Design Archive compat:
                // Skip ES module scripts — NiL.JS doesn't support modern ESM syntax
                // (async generators, import.meta, etc.). Vite-built SPAs ship a
                // legacy bundle via <script nomodule> that uses ES5 + SystemJS.
                if (type == "module") continue;

                // Execute <script nomodule> — inverse of browser behavior.
                // In modern browsers the nomodule attribute suppresses execution,
                // but our engine needs the legacy (ES5) fallback instead.
                if (hasAsync) asyncs.Add(n);
                else if (treatAsDefer) deferred.Add(n);
                else immediate.Add(n);
            }

            try { System.Diagnostics.Debug.WriteLine("[DIAG:RUNSCRIPTS] classified: immediate=" + immediate.Count + " deferred=" + deferred.Count + " async=" + asyncs.Count + " baseUri=" + baseUri); } catch { }

            // Helper to fetch script content (parallelizable)
            // Returns: Content, ResolvedUri, IsModule, IsInline, ShouldRun
            async Task<Tuple<string, Uri, bool, bool, bool>> PreFetch(LiteElement node)
            {
                bool isModule = false;
                string typeAttr = null;
                if (node.Attr != null && node.Attr.TryGetValue("type", out typeAttr) && !string.IsNullOrWhiteSpace(typeAttr))
                    isModule = typeAttr.IndexOf("module", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!SandboxAllows(SandboxFeature.Scripts, isModule ? "script[type=module]" : "script"))
                    return Tuple.Create<string, Uri, bool, bool, bool>(null, null, false, false, false);

                string src = null; if (node.Attr != null) node.Attr.TryGetValue("src", out src);
                
                if (!string.IsNullOrWhiteSpace(src))
                {
                    var resolved = Resolve(baseUri, src) ?? Resolve(_ctx?.BaseUri, src);
                    if (resolved == null) return Tuple.Create<string, Uri, bool, bool, bool>(null, null, false, false, false);

                    string nomodule = null; node.Attr?.TryGetValue("nomodule", out nomodule);
                    try { System.Diagnostics.Debug.WriteLine("[DIAG:FETCH] PreFetch external script src=\"" + src + "\" resolved=\"" + resolved + "\" nomodule=" + (nomodule != null) + " isModule=" + isModule + " allowExt=" + _allowExternalScripts); } catch { }

                    if (isModule) return Tuple.Create<string, Uri, bool, bool, bool>(null, resolved, true, false, true); // Modules handled in exec

                    if (!_allowExternalScripts) return Tuple.Create<string, Uri, bool, bool, bool>(null, resolved, false, false, false);
                    if (!SandboxAllows(SandboxFeature.ExternalScripts, resolved.ToString())) return Tuple.Create<string, Uri, bool, bool, bool>(null, resolved, false, false, false);

                    try
                    {
                        var txt = await FetchScriptStringAsync(resolved, baseUri);
                        try { System.Diagnostics.Debug.WriteLine("[DIAG:FETCH] PreFetch result len=" + (txt?.Length ?? -1) + " uri=\"" + resolved + "\""); } catch { }
                        return Tuple.Create<string, Uri, bool, bool, bool>(txt, resolved, false, false, true);
                    }
                    catch (Exception ex) { try { System.Diagnostics.Debug.WriteLine("[DIAG:FETCH] PreFetch exception for \"" + resolved + "\": " + ex.GetType().Name + " - " + ex.Message); } catch { } return Tuple.Create<string, Uri, bool, bool, bool>(null, resolved, false, false, false); }
                }
                else
                {
                    var uri = isModule ? null : baseUri;
                    return Tuple.Create<string, Uri, bool, bool, bool>(CollectScriptText(node) ?? string.Empty, uri, isModule, true, true);
                }
            }

            // Helper to run fetched script
            async Task Execute(LiteElement node, Tuple<string, Uri, bool, bool, bool> res)
            {
                var content = res.Item1;
                var resolved = res.Item2;
                var isModule = res.Item3;
                var isInline = res.Item4;
                var shouldRun = res.Item5;

                if (!shouldRun) return;

                if (isModule)
                {
                    const int moduleApproxBytes = 16 * 1024;
                    if (_pageScriptBytesUsed + moduleApproxBytes > _pageScriptByteBudget)
                    {
                        try { System.Diagnostics.Debug.WriteLine("[JS-BUDGET] Skipping module script (budget exceeded): " + resolved); } catch { /* swallow */ }
                        return;
                    }
                    Interlocked.Add(ref _pageScriptBytesUsed, moduleApproxBytes);

                    await _moduleLoader.ExecuteModuleTagAsync(node, resolved, baseUri, content);
                    return;
                }

                if (content == null) { try { System.Diagnostics.Debug.WriteLine("[DIAG:EXEC] content is null, skipping. uri=\"" + resolved + "\" isInline=" + isInline); } catch { } return; }

                int len = content.Length;
                string preview = len > 120 ? content.Substring(0, 120) + "…" : content;
                preview = preview.Replace("\r", " ").Replace("\n", " ");
                try { System.Diagnostics.Debug.WriteLine("[DIAG:EXEC] len=" + len + " isInline=" + isInline + " isModule=" + isModule + " uri=\"" + resolved + "\" preview=\"" + preview + "\""); } catch { }

                if (_pageScriptBytesUsed + len > _pageScriptByteBudget)
                {
                     if (!isInline || len > TinyInlineFreeThreshold)
                     {
                        try { System.Diagnostics.Debug.WriteLine("[JS-BUDGET] Skipping script (budget exceeded) baseUri=" + baseUri + " size=" + len); } catch { /* swallow */ }
                        return;
                     }
                }
                if (len > TinyInlineFreeThreshold) Interlocked.Add(ref _pageScriptBytesUsed, len);

                // Phase DIAG: external scripts bypass RunInline's silent catch + IIFE wrapping
                // which can break UMD globals (d3.js). Use SafeEval directly with error logging.
                // Wrapping in JS try-catch to survive internal NiL.JS partial evaluation failures.
                if (!isInline)
                {
                    // Polyfill ES6 features BEFORE d3.js v7 (which uses class extends Map/Set, Symbol, typed arrays)
                    // NiL.JS's HostMapType/HostSetType are C# marker objects that don't support extends or iteration.
                    // Use C# API: _nil.Eval() creates the constructor JSValue, then _nil.DefineVariable().Assign()
                    // bypasses JS-level read-only protection on built-in globals (bare Map = Map$ silently fails).
                    if (resolved != null && resolved.AbsoluteUri != null && resolved.AbsoluteUri.Contains("d3js.org"))
                    {
                        // URL is rewritten d3.v7 → d3.v5 (FetchScriptStringAsync line 5633).
                        // d3.v5 is ES5 — no ES6 polyfills needed. Skip the polyfill prefix entirely
                        // to avoid any interference with NiL.JS evaluation.
                        try { System.Diagnostics.Debug.WriteLine("[DIAG:EXEC] d3js.org script (no polyfill prefix — using v5)"); } catch { }
                    }
                    // Leak inline data from the Nokia Design Archive chunk to globalThis.*
                    // FIX: Use globalThis.* instead of window.* — HostWindow is a C# wrapper
                    // that silently drops dynamic property assignments.
                    if (content.Contains("var Kf={entries:[") && content.Contains("var wf={collections:["))
                    {
                        try { System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Leaking chunk data vars to globalThis.*"); } catch { }
                        content = content.Replace("var Kf={entries:[", "globalThis.Kf={entries:[");
                        content = content.Replace("var wf={collections:[", "globalThis.wf={collections:[");
                        content = content.Replace("var Cf={stories:[", "globalThis.Cf={stories:[");
                        // vf is a flat array: var vf=[...] — replace with globalThis.vf=
                        int vfIdx = content.IndexOf("var vf=", StringComparison.Ordinal);
                        if (vfIdx >= 0) { content = content.Substring(0, vfIdx) + "globalThis.vf=" + content.Substring(vfIdx + 7); }
                        try { System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Data leak complete"); } catch { }
                        // Inject sync graph builder into execute function body
                        try { System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Injecting sync graph builder into execute"); } catch { }
                        string[] gPrefixes = { "return{execute:function(){", "return{execute:function() {", "execute:function(){", "execute:function() {", ":function(){" };
                        int gStart = -1; string gPref = null;
                        foreach (var p in gPrefixes) { gStart = content.IndexOf(p); if (gStart >= 0) { gPref = p; break; } }
                        if (gStart >= 0)
                        {
                            int gDepth = 1, gPos = gStart + gPref.Length;
                            while (gPos < content.Length && gDepth > 0)
                            {
                                char c = content[gPos];
                                if (c == '\'') { gPos++; while (gPos < content.Length && content[gPos] != '\'') { if (content[gPos] == '\\') gPos++; gPos++; } if (gPos < content.Length) gPos++; continue; }
                                if (c == '"') { gPos++; while (gPos < content.Length && content[gPos] != '"') { if (content[gPos] == '\\') gPos++; gPos++; } if (gPos < content.Length) gPos++; continue; }
                                if (c == '`') { gPos++; while (gPos < content.Length && content[gPos] != '`') { if (content[gPos] == '\\') gPos += 2; else if (content[gPos] == '$' && gPos + 1 < content.Length && content[gPos + 1] == '{') { gPos += 2; int ed = 1; while (gPos < content.Length && ed > 0) { if (content[gPos] == '{') ed++; else if (content[gPos] == '}') ed--; gPos++; } } else gPos++; } if (gPos < content.Length) gPos++; continue; }
                                if (c == '/' && gPos + 1 < content.Length) { if (content[gPos + 1] == '/') { gPos += 2; while (gPos < content.Length && content[gPos] != '\n') gPos++; continue; } if (content[gPos + 1] == '*') { gPos += 2; while (gPos + 1 < content.Length && !(content[gPos] == '*' && content[gPos + 1] == '/')) gPos++; if (gPos + 1 < content.Length) gPos += 2; continue; } }
                                if (c == '{') gDepth++;
                                else if (c == '}') { gDepth--; if (gDepth == 0) { content = content.Substring(0, gPos) + ";try{globalThis.__diagInjectRan=true;var __dl=typeof __diagLog==='function'?__diagLog:function(){};var wKf=globalThis.Kf,wWf=globalThis.wf,wCf=globalThis.Cf,wVf=globalThis.vf;if(typeof wKf!=='undefined'&&typeof wWf!=='undefined'){var __f=wKf.entries;var __xf=wWf.collections.map(function(c){return{id:c.id,name:c.title,description:c.blurb,theme:c.grouping,keywords:c.keywords}});var __Pf={};__xf.forEach(function(c){__Pf[c.id]=c});var __nodes=[],__links=[],__conns={};__f.forEach(function(e){__nodes.push({id:e.id,name:e.title,start:e.start,file:e.file,type:'entry'});(e.collections||[]).forEach(function(cId){if(cId!=='C0030'&&cId[0]!=='K'&&__Pf[cId]){__links.push({source:e,target:__Pf[cId]});if(!__conns[e.id])__conns[e.id]=[];__conns[e.id].push(cId);if(!__conns[cId])__conns[cId]=[];__conns[cId].push(e.id)}})});__xf.forEach(function(e){__nodes.push({id:e.id,name:e.title,theme:e.theme,type:'collection'})});globalThis.__graphData={nodes:__nodes,links:__links,nodeConnections:__conns};if(typeof globalThis!=='undefined'&&globalThis.System)globalThis.System.__graphData=globalThis.__graphData;globalThis.__entries=__f;globalThis.__collections=wWf.collections;globalThis.__xf=__xf;globalThis.__Pf=__Pf;if(typeof wCf!=='undefined'){globalThis.__stories=wCf.stories;globalThis.__entriesWithDates=__f.filter(function(e){return e.start&&e.start.length>=5})}if(typeof wVf!=='undefined')globalThis.__keywords=wVf;try{if(typeof __storeData==='function'){}}catch(_sd){}__dl('INJECT_RAN nodes='+__nodes.length+' links='+__links.length+' entries='+__f.length)}else{__dl('INJECT_RAN no Kf/wf')}}catch(e){try{var __dl2=typeof __diagLog==='function'?__diagLog:function(){};__dl2('INJECT_ERR '+(e&&e.message||e))}catch(ee){}}" + content.Substring(gPos); break; } }
                                gPos++;
                            }
                        }
                        try { System.Diagnostics.Debug.WriteLine("[DIAG:CHUNK] Injection complete, gStart=" + gStart); } catch { }
                    }
                    try { 
                        System.Diagnostics.Debug.WriteLine("[DIAG:EXEC] eval " + resolved + " (direct eval, no debug)"); 
                        _nilSyncDocument(); 
                        string evalContent = content;
                        // Expose bare `System` for scripts that use System.register (e.g. the Nokia chunk)
                        if (resolved == null || !resolved.AbsoluteUri.Contains("d3js.org")) {
                            evalContent = "var System=globalThis.System;" + evalContent;
                        }
                        SafeEvalFast("try{ " + evalContent + " }catch(e){ try { console.log('[d3-err:'+((e&&e.message)||typeof e)+']') }catch(_){} }"); 
                    }
                    catch (Exception ex) { try { System.Diagnostics.Debug.WriteLine("[DIAG:EXEC] SafeEval error for " + resolved + ": " + ex.GetType().Name + " - " + ex.Message); } catch { } }
                }
                else
                {
                    try { RunInline(content, new JsContext { BaseUri = resolved }); }
                    catch (Exception ex) { try { System.Diagnostics.Debug.WriteLine($"[Diag] RunInline exception: {ex}"); } catch { /* swallow */ } }
                }
            }

            // 1) Immediate: Parallel Fetch, Sequential Exec
            var immTasks = immediate.Select(n => PreFetch(n)).ToList();
            for (int i = 0; i < immediate.Count; i++)
            {
                var res = await immTasks[i];
                // Nokia compat: Vite polyfill overwrites System.import with its
                // own DOM-based version. Intercept inline System.import(...) calls
                // and forward to our standalone __sysImport (FetchScriptStringAsync
                // + RunInline) so the legacy chunk actually loads.
                if (res != null && res.Item4 && res.Item1 != null)
                {
                    var trimmed = res.Item1.TrimStart();
                    if (trimmed.StartsWith("System.import("))
                    {
                        var newContent = "__sysImport" + trimmed.Substring("System.import".Length);
                        res = Tuple.Create(newContent, res.Item2, res.Item3, res.Item4, res.Item5);
                    }
                }
                await Execute(immediate[i], res);
            }

            // 2) Async: Fire and forget (Parallel Fetch & Exec)
            foreach (var s in asyncs)
            {
                Interlocked.Increment(ref _pendingAsyncScripts);
                var _ = Task.Run(async () =>
                {
                    try
                    {
                        var res = await PreFetch(s);
                        var repaint = _host as IJsHostRepaint;
                        if (repaint != null)
                        {
                            repaint.InvokeOnUiThread(() =>
                            {
                                try
                                {
                                    var t = Execute(s, res);
                                    if (t.IsFaulted)
                                    {
                                        try { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] Execute faulted: " + t.Exception); }
                                        catch { }
                                    }
                                    if (!t.IsCompleted)
                                    {
                                        t.ContinueWith(t2 =>
                                        {
                                            if (t2.IsFaulted)
                                            {
                                                try { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] Execute async fault: " + t2.Exception); }
                                                catch { }
                                            }
                                        }, TaskContinuationOptions.OnlyOnFaulted);
                                    }
                                }
                                catch { /* swallow */ }
                            });
                        }
                        else
                        {
                            await Execute(s, res);
                        }
                    }
                    catch { /* swallow */ }
                    finally { try { DecAndMaybeFireLoad(); } catch { /* swallow */ } }
                });
            }

            // 3) Deferred: Parallel Fetch, Sequential Exec
            var defTasks = deferred.Select(n => PreFetch(n)).ToList();
            for (int i = 0; i < deferred.Count; i++)
            {
                var res = await defTasks[i];
                await Execute(deferred[i], res);
            }

            _readyState = "interactive"; try { FireDocumentEvent("readystatechange"); } catch { /* swallow */ }

            // 4) DOMContentLoaded
            if (!_domContentLoadedFired)
            {
                _domContentLoadedFired = true;
                try { FireDocumentEvent("DOMContentLoaded"); } catch { /* swallow */ }
            }

            // 5) window.load (when asyncs done, or immediately if none)
            if (Volatile.Read(ref _pendingAsyncScripts) == 0 && !_windowLoadFired)
            {
                _windowLoadFired = true;
                try { FireWindowEvent("load"); } catch { /* swallow */ }
                _readyState = "complete"; try { FireDocumentEvent("readystatechange"); } catch { /* swallow */ }
            }
            this.SanitizeForScriptingEnabled(_domRoot);
            RequestRepaint();
            }
            catch (Exception ex)
            {
                try { System.Diagnostics.Debug.WriteLine("[Diag] RunScriptsAsync top-level EXC: " + ex.GetType().Name + ": " + ex.Message); } catch { /* swallow */ }
            }
        }

        private void DecAndMaybeFireLoad()
        {
            if (Interlocked.Decrement(ref _pendingAsyncScripts) == 0 && _domContentLoadedFired && !_windowLoadFired)
            {
                _windowLoadFired = true;
                try { FireWindowEvent("load"); } catch { /* swallow */ }
            }
        }

        // ------------------------------------------------------------------------------------
        // helpers
        private void Navigate(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            Uri abs = null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out abs) && _ctx != null && _ctx.BaseUri != null)
            {
                Uri rel;
                if (Uri.TryCreate(_ctx.BaseUri, url, out rel)) abs = rel;
            }
            if (abs != null && SandboxAllows(SandboxFeature.Navigation, "navigate -> " + abs.AbsoluteUri))
                _host.Navigate(abs);
        }

        // Robust script fetch helper mirroring the main engine's request behavior for diagnostics.
        // IMPORTANT: For X/Twitter hosts, we DO NOT fall back to WinRT; we try managed + legacy rewrite, then bail.
        private async Task<string> FetchScriptStringAsync(Uri uri, Uri referer = null, bool triedRedirect = false)
        {
            if (uri == null) return null;
            try { if (SubresourceAllowed != null && !SubresourceAllowed(uri, "script")) return null; } catch { /* swallow */ }
            lock (_script404Lock) { if (_script404.Contains(uri.AbsoluteUri)) return null; }

            // Handle ms-appx:/// local package files
            if (uri.Scheme == "ms-appx")
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("[Module] ms-appx fetch: " + uri);
                    var file = await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(uri).AsTask().ConfigureAwait(false);
                    System.Diagnostics.Debug.WriteLine("[Module] ms-appx file found: " + file.Path);
                    var text = await Windows.Storage.FileIO.ReadTextAsync(file).AsTask().ConfigureAwait(false);
                    System.Diagnostics.Debug.WriteLine("[Module] ms-appx OK: " + text.Length + " bytes");
                    return text;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[Module] ms-appx FAIL: " + ex.GetType().Name + " - " + ex.Message);
                    return null;
                }
            }

            // Identify X/Twitter/CDN hosts
            Func<string, bool> isXHost = h =>
                (h ?? "").IndexOf("abs.twimg.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (h ?? "").IndexOf("twimg.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (h ?? "").IndexOf("twitter.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (h ?? "").IndexOf("x.com", StringComparison.OrdinalIgnoreCase) >= 0;

            var host = uri.Host ?? string.Empty;

            // Rewrite d3.v7 → d3.v5 (NiL.JS has limited ES6+ support; v5 uses ES5)
            uri = TryRewriteD3jsUrl(uri) ?? uri;

            // 0) Try host-provided external fetcher and in-memory cache first.
            var key = uri.AbsoluteUri;
            try
            {
                lock (_scriptMap)
                {
                    LinkedListNode<Tuple<string, ScriptCacheEntry>> node;
                    if (_scriptMap.TryGetValue(key, out node) && node != null)
                    {
                        _scriptLru.Remove(node);
                        _scriptLru.AddFirst(node);
                        if (node.Value != null && node.Value.Item2 != null)
                            return node.Value.Item2.Body;
                    }
                }
            }
            catch { /* swallow */ }

            if (ExternalScriptFetcher != null)
            {
                try
                {
                    var fromHost = await ExternalScriptFetcher(uri, referer).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(fromHost))
                    {
                        try
                        {
                            lock (_scriptMap)
                            {
                                LinkedListNode<Tuple<string, ScriptCacheEntry>> node;
                                if (_scriptMap.TryGetValue(key, out node) && node != null)
                                {
                                    _scriptLru.Remove(node);
                                }
                                var entry = new ScriptCacheEntry { Body = fromHost };
                                var tuple = new Tuple<string, ScriptCacheEntry>(key, entry);
                                node = new LinkedListNode<Tuple<string, ScriptCacheEntry>>(tuple);
                                _scriptLru.AddFirst(node);
                                _scriptMap[key] = node;
                                while (_scriptLru.Count > _scriptCap)
                                {
                                    var last = _scriptLru.Last; if (last == null) break;
                                    _scriptLru.RemoveLast();
                                    if (last.Value != null) _scriptMap.Remove(last.Value.Item1);
                                }
                            }
                        }
                        catch { /* swallow */ }
                        return fromHost;
                    }
                }
                catch { /* swallow */ }
            }

            // 1) Managed-first path with cookie bridge + safe headers
            try
            {
                var handler = CreateManagedHandler(uri);
                using (var client = new HttpClient(handler))
                {
                    var req = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, uri) { Version = new Version(1, 1) };
                    BuildSafeSubresourceHeaders(req, referer);
                    var resp = await client.SendAsync(req).ConfigureAwait(false);

                    // Follow basic redirects
                    if ((int)resp.StatusCode >= 300 && (int)resp.StatusCode < 400 && resp.Headers?.Location != null)
                    {
                        var loc = resp.Headers.Location;
                        if (!loc.IsAbsoluteUri && req.RequestUri != null) loc = new Uri(req.RequestUri, loc);
                        return await FetchScriptStringAsync(loc, referer, true).ConfigureAwait(false);
                    }

                    // Try legacy rewrite on 404 for X/Twitter
                    if ((int)resp.StatusCode == 404 && isXHost(host))
                    {
                        lock (_script404Lock) { _script404.Add(req.RequestUri.AbsoluteUri); }
                        var alt = TryRewriteXAsset(uri);
                        if (alt != null)
                        {
                            var altReq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, alt) { Version = new Version(1, 1) };
                            BuildSafeSubresourceHeaders(altReq, referer);
                            var altResp = await client.SendAsync(altReq).ConfigureAwait(false);
                            if (altResp.IsSuccessStatusCode)
                            {
                                var altBytes = await altResp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                                string enc = null; try { enc = string.Join(",", altResp.Content.Headers.ContentEncoding); } catch { /* swallow */ }
                                var result = DecodeBytes(altBytes, enc);
                                return result;
                            }
                        }
                        // bail   don t try other stacks for these hosts
                        return null;
                    }

                    if (!resp.IsSuccessStatusCode)
                    {
                        try { System.Diagnostics.Debug.WriteLine("[DIAG:FETCH] managed-first HTTP " + (int)resp.StatusCode + " for " + uri); } catch { }
                        // If X/Twitter host => bail quietly (avoids WinRT 0x80072EFD spam)
                        if (isXHost(host)) return null;

                        // Non-X host: try our shared managed client (_http) below
                    }
                    else
                    {
                        var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        string enc = null; try { enc = string.Join(",", resp.Content.Headers.ContentEncoding); } catch { /* swallow */ }
                        var result = DecodeBytes(bytes, enc);
                        DetectedCharset = CharsetDetector.LastDetectedCharset ?? "UTF-8";
                        try { System.Diagnostics.Debug.WriteLine("[DIAG:FETCH] managed-first OK: " + (result?.Length ?? 0) + " bytes from " + uri); } catch { }
                        return result;
                    }
                }
            }
            catch
            {
                // Managed-first threw; if X/Twitter host, bail. Otherwise, try generic managed below.
                if (isXHost(host)) return null;
            }

            // 2) Generic path using this class's shared managed HttpClient (_http)
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, uri);
                BuildSafeSubresourceHeaders(req, referer);

                var resp = await _http.SendAsync(req).ConfigureAwait(false);

                // Follow simple redirects
                if ((int)resp.StatusCode >= 300 && (int)resp.StatusCode < 400 && resp.Headers?.Location != null)
                {
                    var loc = resp.Headers.Location;
                    if (!loc.IsAbsoluteUri && req.RequestUri != null) loc = new Uri(req.RequestUri, loc);
                    return await FetchScriptStringAsync(loc, referer, true).ConfigureAwait(false);
                }

                // Legacy rewrite for X/Twitter on 404 (one try)
                if ((int)resp.StatusCode == 404 && isXHost(host))
                {
                    lock (_script404Lock) { _script404.Add(req.RequestUri.AbsoluteUri); }
                    var alt = TryRewriteXAsset(uri);
                    if (alt != null)
                    {
                        var altReq = new HttpRequestMessage(HttpMethod.Get, alt);
                        BuildSafeSubresourceHeaders(altReq, referer);
                        var altResp = await _http.SendAsync(altReq).ConfigureAwait(false);
                        if (altResp.IsSuccessStatusCode)
                        {
                            var altBytes = await altResp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            string enc = null; try { enc = string.Join(",", altResp.Content.Headers.ContentEncoding); } catch { /* swallow */ }
                            var altResult = DecodeBytes(altBytes, enc);
                            return altResult;
                        }
                    }
                    return null; // bail   do not try further paths
                }

                if (!resp.IsSuccessStatusCode)
                {
                    try { System.Diagnostics.Debug.WriteLine("[DIAG:FETCH] generic HTTP " + (int)resp.StatusCode + " for " + uri); } catch { }
                    return null;
                }

                var b2 = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                string enc2 = null; try { enc2 = string.Join(",", resp.Content.Headers.ContentEncoding); } catch { /* swallow */ }
                var result = DecodeBytes(b2, enc2);
                DetectedCharset = CharsetDetector.LastDetectedCharset ?? "UTF-8";
                try { System.Diagnostics.Debug.WriteLine("[DIAG:FETCH] generic OK: " + (result?.Length ?? 0) + " bytes from " + uri); } catch { }
                return result;
            }
            catch
            {
                return null;
            }
        }

        // Build a minimal, WP/UWP-safe header set for subresource (script) requests.
        private static void BuildSafeSubresourceHeaders(System.Net.Http.HttpRequestMessage req, Uri referer)
        {
            if (req == null) return;
            const string ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
            try { req.Headers.TryAddWithoutValidation("User-Agent", ua); } catch { /* swallow */ }
            try { req.Headers.TryAddWithoutValidation("Accept", "*/*"); } catch { /* swallow */ }
            try { req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9"); } catch { /* swallow */ }
            if (referer != null) { try { req.Headers.Referrer = new Uri(referer.AbsoluteUri); } catch { /* swallow */ } }
            // Intentionally NOT setting Accept-Encoding / Connection / Host / DNT / Sec-* / UIR
        }

        // Simple rewrite for Twitter/X bundles that frequently move from "client-web" to "client-web-legacy".
        private static Uri TryRewriteXAsset(Uri u)
        {
            if (u == null) return null;
            var s = u.AbsoluteUri;
            if (s.IndexOf("/responsive-web/client-web/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var alt = s.Replace("/responsive-web/client-web/", "/responsive-web/client-web-legacy/");
                try { return new Uri(alt); } catch { /* swallow */ }
            }
            return null;
        }

        // Rewrite d3.v7 → d3.v5 for NiL.JS compatibility (v5 targets ES5, v7 uses ES6+ features NiL.JS can't parse)
        private static Uri TryRewriteD3jsUrl(Uri u)
        {
            if (u == null) return null;
            var s = u.AbsoluteUri;
            if (s.IndexOf("d3js.org/d3.v7", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var alt = s.Replace("d3.v7", "d3.v5");
                try { return new Uri(alt); } catch { /* swallow */ }
            }
            return null;
        }

        internal Task<string> FetchModuleTextAsync(Uri uri, Uri referer)
        {
            return FetchScriptStringAsync(uri, referer);
        }

        public static string DecodeBytes(byte[] bytes, string contentEncoding)
        {
            try
            {
                if (bytes == null || bytes.Length == 0) return "";
                Stream s = new MemoryStream(bytes);
                byte[] decompressed = null;
                if (!string.IsNullOrEmpty(contentEncoding))
                {
                    var ce = contentEncoding.ToLowerInvariant();
                    if (ce.Contains("br"))
                    {
                        try
                        {
                            // Brotli may not be available on this target; attempt via reflection and fall through on failure
                            Type broType = null;
                            try { broType = typeof(System.IO.Compression.GZipStream).GetTypeInfo().Assembly.GetType("System.IO.Compression.BrotliStream"); } catch { /* swallow */ }
                            if (broType == null)
                            {
                                try { broType = Type.GetType("System.IO.Compression.BrotliStream"); } catch { /* swallow */ }
                            }
                            if (broType != null)
                            {
                                using (var bro = (Stream)Activator.CreateInstance(broType, new object[] { s, CompressionMode.Decompress }))
                                using (var ms = new MemoryStream())
                                {
                                    bro.CopyTo(ms);
                                    decompressed = ms.ToArray();
                                }
                            }
                        }
                        catch { /* platform may not have Brotli - fall through */ }
                    }
                    if (decompressed == null && ce.Contains("gzip"))
                    {
                        using (var gz = new GZipStream(s, CompressionMode.Decompress))
                        using (var ms = new MemoryStream())
                        {
                            gz.CopyTo(ms);
                            decompressed = ms.ToArray();
                        }
                    }
                    if (decompressed == null && ce.Contains("deflate"))
                    {
                        using (var dz = new DeflateStream(s, CompressionMode.Decompress))
                        using (var ms = new MemoryStream())
                        {
                            dz.CopyTo(ms);
                            decompressed = ms.ToArray();
                        }
                    }
                }
                if (decompressed == null) decompressed = bytes;

                // Detect charset from BOM or <meta charset> in content
                var detected = CharsetDetector.DetectEncoding(decompressed);
                var enc = detected ?? Encoding.UTF8;
                try { return enc.GetString(decompressed, 0, decompressed.Length); } catch { try { return Encoding.UTF8.GetString(decompressed, 0, decompressed.Length); } catch { return ""; } }
            }
            catch { try { return Encoding.UTF8.GetString(bytes, 0, bytes.Length); } catch { try { return Encoding.UTF8.GetString(bytes, 0, bytes.Length); } catch { return ""; } } }
        }

        // Public helper so a full JS engine binding (or tests) can call into the same fetch logic.
        public async Task<string> FetchAsync(Uri uri, Uri referer = null)
        {
            if (uri == null) return null;
            if (FetchOverride != null) return await FetchOverride(uri).ConfigureAwait(false);
            // reuse existing FetchScriptStringAsync logic (it implements managed fallback and decoding)
            try { return await FetchScriptStringAsync(uri, referer).ConfigureAwait(false); } catch { return null; }
        }

        // Minimal fetch with options: method/headers/body (text). Returns decoded text.
        public async Task<string> FetchAsync(Uri uri, Uri referer, string method, Dictionary<string, string> headers, string body)
        {
            if (uri == null) return null;
            try
            {
                var handler = CreateManagedHandler(uri);
                using (var client = new System.Net.Http.HttpClient(handler))
                {
                    var req = new System.Net.Http.HttpRequestMessage(new System.Net.Http.HttpMethod(string.IsNullOrEmpty(method) ? "GET" : method), uri)
                    { Version = new Version(1, 1) };
                    BuildSafeSubresourceHeaders(req, referer);
                    if (headers != null)
                    {
                        foreach (var kv in headers)
                        {
                            try { req.Headers.TryAddWithoutValidation(kv.Key, kv.Value); } catch { /* swallow */ }
                        }
                    }
                    if (!string.IsNullOrEmpty(body) && !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                    {
                        string ct = null; if (headers != null) headers.TryGetValue("Content-Type", out ct);
                        req.Content = new System.Net.Http.StringContent(body ?? string.Empty, Encoding.UTF8,
                            string.IsNullOrEmpty(ct) ? "text/plain;charset=UTF-8" : ct);
                    }
                    var resp = await client.SendAsync(req).ConfigureAwait(false);
                    var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    string enc = null; try { enc = string.Join(",", resp.Content.Headers.ContentEncoding); } catch { /* swallow */ }
                    return DecodeBytes(bytes, enc);
                }
            }
            catch { return null; }
        }

        // Detailed fetch returning status/headers/body for Response-like objects
        public async Task<FetchResult> FetchDetailedAsync(Uri uri, Uri referer, string method = null, Dictionary<string, string> headers = null, string body = null)
        {
            if (uri == null) return null;
            try
            {
                var handler = CreateManagedHandler(uri);
                using (var client = new System.Net.Http.HttpClient(handler))
                {
                    var req = new System.Net.Http.HttpRequestMessage(new System.Net.Http.HttpMethod(string.IsNullOrEmpty(method) ? "GET" : method), uri)
                    { Version = new Version(1, 1) };
                    BuildSafeSubresourceHeaders(req, referer);
                    if (headers != null)
                    {
                        foreach (var kv in headers)
                            try { req.Headers.TryAddWithoutValidation(kv.Key, kv.Value); } catch { /* swallow */ }
                    }
                    if (!string.IsNullOrEmpty(body) && !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                    {
                        string ct = null; if (headers != null) headers.TryGetValue("Content-Type", out ct);
                        req.Content = new System.Net.Http.StringContent(body ?? string.Empty, Encoding.UTF8,
                            string.IsNullOrEmpty(ct) ? "text/plain;charset=UTF-8" : ct);
                    }
                    var resp = await client.SendAsync(req).ConfigureAwait(false);
                    var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    string enc = null; try { enc = string.Join(",", resp.Content.Headers.ContentEncoding); } catch { /* swallow */ }
                    var txt = DecodeBytes(bytes, enc);
                    var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    try { foreach (var kv in resp.Headers) h[kv.Key] = string.Join(",", kv.Value ?? new string[0]); } catch { /* swallow */ }
                    try { foreach (var kv in resp.Content.Headers) h[kv.Key] = string.Join(",", kv.Value ?? new string[0]); } catch { /* swallow */ }
                    return new FetchResult
                    {
                        Body = txt ?? string.Empty,
                        Status = (int)resp.StatusCode,
                        StatusText = resp.ReasonPhrase ?? string.Empty,
                        Headers = h,
                        Ok = ((int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300)
                    };
                }
            }
            catch { return null; }
        }

        // URL helper: resolves relative to baseUri and returns absolute string
        public string CreateUrl(string url, string baseUri)
        {
            try
            {
                Uri b = null;
                if (!string.IsNullOrWhiteSpace(baseUri)) Uri.TryCreate(baseUri, UriKind.Absolute, out b);
                Uri outUri = null;
                if (Uri.TryCreate(url, UriKind.Absolute, out outUri)) return outUri.AbsoluteUri;
                if (b != null && Uri.TryCreate(b, url, out outUri)) return outUri.AbsoluteUri;
            }
            catch { /* swallow */ }
            return null;
        }

        // Parse query string into dictionary-style key/value pairs (URLSearchParams-like)
        public Dictionary<string, string> ParseQuery(string query)
        {
            var d = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(query)) return d;
            var q = query;
            if (q.StartsWith("?")) q = q.Substring(1);
            var parts = q.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var kv = p.Split(new[] { '=' }, 2);
                try
                {
                    var k = Uri.UnescapeDataString(kv[0]);
                    var v = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
                    if (!d.ContainsKey(k)) d[k] = v;
                }
                catch { /* swallow */ }
            }
            return d;
        }

        // TextEncoder/Decoder helpers
        public byte[] TextEncodeUtf8(string s) { return s == null ? new byte[0] : Encoding.UTF8.GetBytes(s); }
        public string TextDecodeUtf8(byte[] data) { try { return data == null ? "" : Encoding.UTF8.GetString(data, 0, data.Length); } catch { return ""; } }

        // crypto.getRandomValues helper
        public byte[] CryptoGetRandomValues(int length)
        {
            try
            {
                if (length <= 0) return new byte[0];
                var buf = new byte[length];
                try
                {
                    // Try to use System.Security.Cryptography.RandomNumberGenerator via reflection so code compiles
                    // even when System.Security.Cryptography isn't available at compile-time on older targets.
                    try
                    {
                        var rngType = Type.GetType("System.Security.Cryptography.RandomNumberGenerator");
                        bool filled = false;
                        if (rngType != null)
                        {
                            MethodInfo createMethod = null;
                            try { createMethod = rngType.GetRuntimeMethod("Create", new Type[0]); } catch { /* swallow */ }
                            if (createMethod == null) { try { var ti = rngType.GetTypeInfo(); if (ti != null) createMethod = ti.GetDeclaredMethod("Create"); } catch { /* swallow */ } }
                            if (createMethod == null) { try { var ti3 = rngType.GetTypeInfo(); if (ti3 != null) createMethod = ti3.GetDeclaredMethod("Create"); } catch { /* swallow */ } }
                            if (createMethod != null)
                            {
                                try
                                {
                                    using (var rng = (IDisposable)createMethod.Invoke(null, null))
                                    {
                                        MethodInfo getBytes = null;
                                        try { getBytes = rng.GetType().GetRuntimeMethod("GetBytes", new Type[] { typeof(byte[]) }); } catch { /* swallow */ }
                                        if (getBytes == null) { try { var ti2 = rng.GetType().GetTypeInfo(); if (ti2 != null) getBytes = ti2.GetDeclaredMethod("GetBytes"); } catch { /* swallow */ } }
                                        if (getBytes == null) { try { var gi3 = rng.GetType().GetTypeInfo(); if (gi3 != null) getBytes = gi3.GetDeclaredMethod("GetBytes"); } catch { /* swallow */ } }
                                        if (getBytes != null) { getBytes.Invoke(rng, new object[] { buf }); filled = true; }
                                    }
                                }
                                catch { /* swallow */ }
                            }
                        }
                        // If managed RNG path failed or not available, use CryptographicBuffer fallback
                        if (!filled)
                        {
                            var wb = Windows.Security.Cryptography.CryptographicBuffer.GenerateRandom((uint)length);
                            Windows.Security.Cryptography.CryptographicBuffer.CopyToByteArray(wb, out buf);
                        }
                    }
                    catch
                    {
                        // Fallback for UWP: use Windows.Security.Cryptography.CryptographicBuffer
                        var wb = Windows.Security.Cryptography.CryptographicBuffer.GenerateRandom((uint)length);
                        Windows.Security.Cryptography.CryptographicBuffer.CopyToByteArray(wb, out buf);
                    }
                }
                catch { /* final fallback: zeros */ }
                return buf;
            }
            catch { return new byte[0]; }
        }

        // Minimal JS string escape using StringBuilder to minimize temporary allocations.
        // Supports either single-quote or double-quote embedding.
        private static string JsEscape(string s, char quote = '\'')
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 32);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c == quote) { sb.Append('\\'); sb.Append(c); }
                        else if (c < 32 || c == '\u2028' || c == '\u2029')
                        {
                            // encode as \uXXXX for control and line-sep chars
                            sb.Append("\\u"); sb.Append(((int)c).ToString("x4"));
                        }
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        // (old single-quote helper removed) use JsEscape(string, char) defined above

        private void TryUpdateInlineStyle(string id, string prop, string val)
        {
            try
            {
                if (_domRoot == null || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(prop)) return;
                var doc = new JsDocument(this, _domRoot);
                var el = doc.getElementById(id) as JsDomElement; if (el == null) return;
                var style = el.getAttribute("style") ?? string.Empty;
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var part in style.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split(new[] { ':' }, 2); if (kv.Length == 2) dict[kv[0].Trim()] = kv[1].Trim();
                }
                dict[prop] = val ?? string.Empty;
                var sb = new StringBuilder(); bool first = true; foreach (var kv in dict)
                { if (!first) sb.Append(';'); first = false; sb.Append(kv.Key).Append(':').Append(kv.Value); }
                el.setAttribute("style", sb.ToString());
            }
            catch { /* swallow */ }
        }
        



        // Result of a network fetch used to construct Response-like objects in JS-0
        public sealed class FetchResult
        {
            public string Body;
            public int Status;
            public string StatusText;
            public Dictionary<string, string> Headers;
            public bool Ok;
        }

        // ---------------- Script blocks (very small subset) ----------------
        private readonly Dictionary<string, string> _userFunctions = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Execute a multi-line <script> block.
        /// Tries ES5-lite interpreter first; if parsing fails, falls back to line-by-line allowlist runner.
        /// </summary>
        public void ExecuteScriptBlock(string code, JsContext ctx)
        {
            if (string.IsNullOrWhiteSpace(code)) return;
            if (!SandboxAllows(SandboxFeature.Scripts, ctx?.BaseUri != null ? "script-block " + ctx.BaseUri : "script-block")) return;
            var prev = _ctx; if (ctx != null) _ctx = ctx;
            try
            {
                // Try full NiL.JS engine first for proper JavaScript execution
                try
                {
                    if (_nil != null)
                    {
                        _nilSyncDocument();
                        SafeEval(code);
                        if (!_niljsSafeEvalFailed) return;
                    }
                    else
                    {
                        // NiL.JS not initialized, fall back immediately
                    }
                }
                catch (Exception ex)
                {
                    // Log the exception but don't rethrow - fall back to mini runner
                    try { System.Diagnostics.Debug.WriteLine($"[JS] NiL.JS failed: {ex.Message}"); } catch { /* swallow */ }
                }

                // Try advanced ES5-lite execution
                try
                {
                    var mini = new JsMiniRunner(this);
                    if (mini.Execute(code)) return;
                }
                catch (Exception ex)
                {
                    ReportMiniRunnerFailure("ExecuteScriptBlock", code, ex);
                }

                // Fallback: very simple splitter + allowlist
                foreach (var stmt in SplitTopLevelStatements(code))
                {
                    var s = (stmt ?? "").Trim(); if (s.Length == 0) continue;
                    // function foo(){...}
                    var mFn = Regex.Match(s, @"^\s*function\s+([A-Za-z_$][A-Za-z0-9_$]*)\s*\([^)]*\)\s*\{([\s\S]*)\}\s*;?\s*$", RegexOptions.Singleline);
                    if (mFn.Success) { try { _userFunctions[mFn.Groups[1].Value] = mFn.Groups[2].Value ?? ""; } catch { /* swallow */ } continue; }

                    // bare call: foo();
                    var mCall = Regex.Match(s, @"^\s*([A-Za-z_$][A-Za-z0-9_$]*)\s*\(\s*\)\s*;?\s*$");
                    if (mCall.Success)
                    {
                        string body; if (_userFunctions.TryGetValue(mCall.Groups[1].Value, out body)) { ExecuteScriptBlock(body, _ctx); continue; }
                    }
                    try { RunInline(s, _ctx); } catch { /* swallow */ }
                }
            }
            finally { _ctx = prev; }
        }

        /// <summary>
        /// Register a user-defined function body so later inline calls (foo()) can expand.
        /// Mirrors the fallback function declaration path in ExecuteScriptBlock.
        /// </summary>
        public void RegisterUserFunction(string name, string body)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                _userFunctions[name] = body ?? string.Empty;
            }
            catch { /* swallow */ }
        }

        /// <summary>
        /// Evaluate a JavaScript expression and return a string representation.
        /// Very small subset: tries full engines first, then the mini runner, finally a naive allowlist.
        /// Safe for diagnostics (sandbox respected).
        /// </summary>
        public string EvalToString(string expr, JsContext ctx)
        {
            if (string.IsNullOrWhiteSpace(expr)) return string.Empty;
            if (!SandboxAllows(SandboxFeature.Scripts, "eval-expression")) return string.Empty;
            var prev = _ctx; if (ctx != null) _ctx = ctx;
            try
            {
                try
                {
                    if (_nil != null)
                    {
                        _nilSyncDocument();
                        var v = SafeEval(expr);
                        return v != null ? v.ToString() : string.Empty;
                    }
                }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[NiLJS] EvalToString: " + ex.GetType().Name + ": " + ex.Message); }
                // Mini runner expression parse
                try
                {
                    var mini = new JsMiniRunner(this);
                    return mini.TryEvalToString(expr);
                }
                catch { /* swallow */ }

                // Fallback: run inline and no result
                try { RunInline(expr, _ctx); } catch { /* swallow */ }
                return string.Empty;
            }
            finally { _ctx = prev; }
        }

        private static IEnumerable<string> SplitTopLevelStatements(string code)
        {
            if (code == null) yield break;
            var sb = new StringBuilder();
            int depthParen = 0, depthBrace = 0, depthBracket = 0;
            bool inStr = false; char strQ = '\0'; bool esc = false;
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                sb.Append(c);
                if (inStr)
                {
                    if (esc) { esc = false; continue; }
                    if (c == '\\') { esc = true; continue; }
                    if (c == strQ) { inStr = false; strQ = '\0'; }
                    continue;
                }
                if (c == '\'' || c == '"') { inStr = true; strQ = c; continue; }
                if (c == '(') depthParen++;
                else if (c == ')') depthParen = Math.Max(0, depthParen - 1);
                else if (c == '{') depthBrace++;
                else if (c == '}') depthBrace = Math.Max(0, depthBrace - 1);
                else if (c == '[') depthBracket++;
                else if (c == ']') depthBracket = Math.Max(0, depthBracket - 1);

                if (c == ';' && depthParen == 0 && depthBrace == 0 && depthBracket == 0)
                {
                    yield return sb.ToString(); sb.Clear();
                }
            }
            var tail = sb.ToString(); if (!string.IsNullOrWhiteSpace(tail)) yield return tail;
        }

        // ---------------- ES5-lite interpreter (Phase 1/2/3 scaffolding) ----------------

        // Cache compiled JsFuncDef by body text to avoid re-parsing on repeated invocations
        private readonly Dictionary<string, JsFuncDef> _compiledFunctions = new Dictionary<string, JsFuncDef>(StringComparer.Ordinal);

        private JsFuncDef GetOrCacheFuncDef(string body)
        {
            if (body == null) return new JsFuncDef { Body = null };
            JsFuncDef def;
            if (!_compiledFunctions.TryGetValue(body, out def))
            {
                def = new JsFuncDef { Body = body };
                _compiledFunctions[body] = def;
            }
            return def;
        }

        internal static double DeviceDpr()
        {
            try { var dpr = CssParser.MediaDppx; if (dpr.HasValue) return dpr.Value; } catch { /* swallow */ }
            try { var di = Windows.Graphics.Display.DisplayInformation.GetForCurrentView(); return di.RawPixelsPerViewPixel; } catch { /* swallow */ }
            return 1.0;
        }

        /// <summary>
        /// Evaluate a CSS media query string for JS matchMedia().
        /// Mirrors CssLoader.EvaluateMediaQuery logic but usable from JS engine.
        /// </summary>
        public bool EvaluateMediaQueryString(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            var q = query.ToLowerInvariant().Trim();
            bool negate = q.StartsWith("not ", StringComparison.Ordinal);
            if (negate) q = q.Substring(4).Trim();
            if (q.StartsWith("only ", StringComparison.Ordinal)) q = q.Substring(5).Trim();
            if (q.StartsWith("screen", StringComparison.Ordinal)) q = q.Substring(6).Trim();
            else if (q.StartsWith("all", StringComparison.Ordinal)) q = q.Substring(3).Trim();
            if (string.IsNullOrWhiteSpace(q)) return !negate;

            try
            {
                double w = 0, h = 0;
                try { var b = Windows.UI.Xaml.Window.Current.Bounds; w = b.Width; h = b.Height; } catch { /* swallow */ }
                double dpr = DeviceDpr();
                var scheme = CssParser.MediaPrefersColorScheme ?? "light";
                var scripting = CssParser.MediaScripting ?? "enabled";

                var parts = q.Split(new[] { " and " }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var p = part.Trim().Trim('(', ')').Trim();
                    if (string.IsNullOrWhiteSpace(p)) continue;

                    bool condition = true;
                    if (p.StartsWith("min-width"))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(p, @"(\d+(?:\.\d+)?)\s*px");
                        if (m.Success) { double v; if (double.TryParse(m.Groups[1].Value, out v)) condition = w >= v; }
                    }
                    else if (p.StartsWith("max-width"))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(p, @"(\d+(?:\.\d+)?)\s*px");
                        if (m.Success) { double v; if (double.TryParse(m.Groups[1].Value, out v)) condition = w <= v; }
                    }
                    else if (p.Contains("prefers-color-scheme"))
                    {
                        condition = (p.Contains("dark") && scheme == "dark") || (p.Contains("light") && scheme == "light");
                    }
                    else if (p.Contains("scripting"))
                    {
                        if (p.Contains("none")) condition = scripting == "none";
                        else if (p.Contains("initial-only")) condition = scripting == "initial-only";
                        else condition = scripting == "enabled";
                    }
                    else if (p.Contains("resolution") || p.Contains("device-pixel-ratio"))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(p, @"(?<v>\d+(?:\.\d+)?)\s*(?<u>dppx|x|dpi|dpcm)?");
                        if (m.Success)
                        {
                            double v; if (!double.TryParse(m.Groups["v"].Value, out v)) continue;
                            var unit = m.Groups["u"].Value.ToLowerInvariant();
                            if (unit == "dpi") v /= 96.0;
                            else if (unit == "dpcm") v /= 37.8;
                            if (p.StartsWith("min")) condition = dpr >= v;
                            else if (p.StartsWith("max")) condition = dpr <= v;
                            else condition = Math.Abs(dpr - v) < 0.01;
                        }
                    }
                    else if (p.Contains("orientation"))
                    {
                        bool isLandscape = w > h;
                        condition = (p.Contains("landscape") && isLandscape) || (p.Contains("portrait") && !isLandscape);
                    }
                    // unknown features: condition stays true (permissive)

                    if (!condition) return negate ? true : false;
                }
            }
            catch { /* swallow */ }

            return !negate;
        }

        private sealed class JsMiniRunner
        {
            private readonly JavaScriptEngine _e;
            private string _src; private int _pos; private int _len;
            private readonly Dictionary<string, JsVal> _globals = new Dictionary<string, JsVal>(StringComparer.Ordinal);
            private readonly List<Dictionary<string, JsVal>> _blockScopes = new List<Dictionary<string, JsVal>>();

            public sealed class JsVal
            {
                public double? Num; public string Str; public bool? Bool; public object Obj;
                public static JsVal FromNum(double n) { var v = new JsVal(); v.Num = n; return v; }
                public static JsVal FromStr(string s) { var v = new JsVal(); v.Str = s; return v; }
                public static JsVal FromBool(bool b) { var v = new JsVal(); v.Bool = b; return v; }
                public static JsVal Null() { return new JsVal(); }
                public override string ToString() { if (Str != null) return Str; if (Num.HasValue) return Num.Value.ToString(System.Globalization.CultureInfo.InvariantCulture); if (Bool.HasValue) return Bool.Value ? "true" : "false"; return "null"; }
                public bool Truthy() { if (Bool.HasValue) return Bool.Value; if (Num.HasValue) return Math.Abs(Num.Value) > 1e-9; if (Str != null) return Str.Length > 0; return false; }
            }

            private static bool IsNullish(JsVal v)
            {
                if (v == null) return true;
                if (v.Obj != null) return false;
                if (v.Str != null) return false;
                if (v.Num.HasValue) return false;
                if (v.Bool.HasValue) return false;
                return true;
            }

            private void PushBlockScope() { _blockScopes.Add(new Dictionary<string, JsVal>(StringComparer.Ordinal)); }

            private void PopBlockScope() { if (_blockScopes.Count > 0) _blockScopes.RemoveAt(_blockScopes.Count - 1); }

            private bool TryGetVar(string name, out JsVal val)
            {
                for (int i = _blockScopes.Count - 1; i >= 0; i--)
                    if (_blockScopes[i].TryGetValue(name, out val)) return true;
                return _globals.TryGetValue(name, out val);
            }

            private void SetVarDecl(string name, JsVal val, bool blockScoped)
            {
                if (blockScoped && _blockScopes.Count > 0)
                    _blockScopes[_blockScopes.Count - 1][name] = val;
                else
                    _globals[name] = val;
            }

            private void SetVarAssign(string name, JsVal val)
            {
                for (int i = _blockScopes.Count - 1; i >= 0; i--)
                    if (_blockScopes[i].ContainsKey(name)) { _blockScopes[i][name] = val; return; }
                _globals[name] = val;
            }

            public string TryEvalToString(string expr)
            {
                var e = (expr ?? string.Empty).Trim();
                if (e.Length == 0) return string.Empty;
                try
                {
                    var child = new JsMiniRunner(_e);
                    child._globals.Clear();
                    foreach (var kv in _globals) child._globals[kv.Key] = kv.Value;
                    child._src = e + ";";
                    child._pos = 0;
                    child._len = child._src.Length;
                    child.SkipWs();
                    var value = child.ParseExpression();
                    child.Expect(";");
                    foreach (var kv in child._globals) _globals[kv.Key] = kv.Value;
                    return value != null ? value.ToString() : string.Empty;
                }
                catch
                {
                    _e.TraceFeatureGap("MiniRunner", "EvalExpression", e);
                    return string.Empty;
                }
            }

            private sealed class ReturnEx : Exception { public JsVal Value; public ReturnEx(JsVal v) { Value = v; } }

            // Host object shims (very small)
            private sealed class HostDocument { public JavaScriptEngine E; public HostDocument(JavaScriptEngine e) { E = e; } }
            private sealed class HostElement { public JavaScriptEngine E; public string Id; public LiteElement Node; public HostElement(JavaScriptEngine e, string id) { E = e; Id = id; } }
            private sealed class HostStyle { public JavaScriptEngine E; public string Id; public LiteElement Node; public HostStyle(JavaScriptEngine e, string id) { E = e; Id = id; } }
            private sealed class HostClassList { public JavaScriptEngine E; public string Id; public LiteElement Node; public HostClassList(JavaScriptEngine e, string id) { E = e; Id = id; } }
            private sealed class HostDataset { public JavaScriptEngine E; public LiteElement Node; public HostDataset(JavaScriptEngine e, LiteElement node) { E = e; Node = node; } }
            private sealed class HostConsole { public JavaScriptEngine E; public HostConsole(JavaScriptEngine e) { E = e; } }
            private sealed class HostNavigator { public JavaScriptEngine E; public HostNavigator(JavaScriptEngine e) { E = e; } }
            private sealed class HostClipboard { public JavaScriptEngine E; public HostClipboard(JavaScriptEngine e) { E = e; } }
            private sealed class HostPerformance { public JavaScriptEngine E; public HostPerformance(JavaScriptEngine e) { E = e; } }
            private sealed class HostHistory { public JavaScriptEngine E; public HostHistory(JavaScriptEngine e) { E = e; } }
            private sealed class HostWindow { public JavaScriptEngine E; public HostWindow(JavaScriptEngine e) { E = e; } }
            private sealed class HostLocation { public JavaScriptEngine E; public HostLocation(JavaScriptEngine e) { E = e; } }
            private sealed class HostEvent
            {
                public JavaScriptEngine E;
                public string Type;
                public string TargetId;
                public bool DefaultPrevented;
                public bool PropagationStopped;
                public HostEvent(JavaScriptEngine e, string type, string targetId)
                {
                    E = e;
                    Type = type;
                    TargetId = targetId;
                }
            }
            private sealed class HostJSON { public JavaScriptEngine E; public HostJSON(JavaScriptEngine e) { E = e; } }
            private sealed class HostObjectType { public JavaScriptEngine E; public HostObjectType(JavaScriptEngine e) { E = e; } }
            private sealed class HostMapType { public JavaScriptEngine E; public HostMapType(JavaScriptEngine e) { E = e; } }
            private sealed class HostSetType { public JavaScriptEngine E; public HostSetType(JavaScriptEngine e) { E = e; } }
            private sealed class HostLocalStorage { public JavaScriptEngine E; public bool Session; public HostLocalStorage(JavaScriptEngine e, bool s) { E = e; Session = s; } }
            private sealed class HostMap { public readonly Dictionary<string, JsVal> Data = new Dictionary<string, JsVal>(StringComparer.Ordinal); }
            private sealed class HostSet { public readonly HashSet<string> Data = new HashSet<string>(StringComparer.Ordinal); }
            private sealed class HostFunc { public Func<List<JsVal>, JsVal> F; public HostFunc(Func<List<JsVal>, JsVal> f) { F = f; } }
            private sealed class HostFetchResp { public JavaScriptEngine E; public System.Threading.Tasks.Task<FetchResult> Task; }
            private sealed class HostPromiseType { public JavaScriptEngine E; public HostPromiseType(JavaScriptEngine e) { E = e; } }
            public sealed class PromiseHandler { public bool IsFulfill; public JsFuncDef Fn; public HostPromise Next; }
            public sealed class HostPromise
            {
                public JavaScriptEngine E;
                public int State; // 0=pending, 1=resolved, -1=rejected
                public JsVal Value;
                public List<PromiseHandler> Handlers = new List<PromiseHandler>();
            }

            private sealed class ClassMethod
            {
                public string Name;
                public List<JsFuncParam> Parameters = new List<JsFuncParam>();
                public string Body;
                public bool IsStatic;
            }

            public JsMiniRunner(JavaScriptEngine e) { _e = e; InitHostGlobals(); }

                private void InitHostGlobals()
                {
                    _globals["document"] = new JsVal { Obj = new HostDocument(_e) };
                _globals["console"] = new JsVal { Obj = new HostConsole(_e) };
                _globals["history"] = new JsVal { Obj = new HostHistory(_e) };
                _globals["window"] = new JsVal { Obj = new HostWindow(_e) };
                _globals["JSON"] = new JsVal { Obj = new HostJSON(_e) };
                _globals["Promise"] = new JsVal { Obj = new HostPromiseType(_e) };
                _globals["Object"] = new JsVal { Obj = new HostObjectType(_e) };
                _globals["Map"] = new JsVal { Obj = new HostMapType(_e) };
                    _globals["Set"] = new JsVal { Obj = new HostSetType(_e) };

                    // Minimal getComputedStyle shim (prevents crashes in libraries expecting it)
                    _globals["getComputedStyle"] = new JsVal { Obj = new HostFunc(args =>
                    {
                        try
                        {
                            var styleMap = new Dictionary<string, JsVal>(StringComparer.OrdinalIgnoreCase);
                            styleMap["display"] = JsVal.FromStr("block");
                            styleMap["position"] = JsVal.FromStr("static");
                            styleMap["visibility"] = JsVal.FromStr("visible");
                            styleMap["opacity"] = JsVal.FromStr("1");
                            styleMap["getPropertyValue"] = new JsVal { Obj = new HostFunc(a => { try { var p = ToStr(a.Count > 0 ? a[0] : JsVal.Null()).ToLowerInvariant(); JsVal v; if (styleMap.TryGetValue(p, out v)) return v; } catch { /* swallow */ } return JsVal.FromStr(""); }) };
                            return new JsVal { Obj = styleMap };
                        }
                        catch { return new JsVal { Obj = new Dictionary<string, JsVal>(StringComparer.Ordinal) }; }
                    }) };

                // Navigator/performance globals (also accessible via window.navigator/performance)
                _globals["navigator"] = new JsVal { Obj = new HostNavigator(_e) };
                _globals["performance"] = new JsVal { Obj = new HostPerformance(_e) };
                _globals["localStorage"] = new JsVal { Obj = new HostLocalStorage(_e, false) };
                _globals["sessionStorage"] = new JsVal { Obj = new HostLocalStorage(_e, true) };

                // Minimal Date.now()
                var dateMap = new Dictionary<string, JsVal>(StringComparer.Ordinal);
                dateMap["now"] = new JsVal { Obj = new HostFunc(args => { try { var ms = (DateTime.UtcNow - new DateTime(1970,1,1)).TotalMilliseconds; return JsVal.FromNum(ms); } catch { return JsVal.FromNum(0); } }) };
                _globals["Date"] = new JsVal { Obj = dateMap };

                // Timers: setTimeout/clearTimeout/setInterval/clearInterval return numeric IDs
                _globals["setTimeout"] = new JsVal { Obj = new HostFunc(args =>
                {
                    try
                    {
                        var code = ToStr(args.Count > 0 ? args[0] : JsVal.Null());
                        int ms = (int)ToNum(args.Count > 1 ? args[1] : JsVal.FromNum(0));
                        var id = _e.ScheduleTimeout(string.IsNullOrEmpty(code) ? "" : code, ms);
                        return JsVal.FromNum(id);
                    }
                    catch { return JsVal.FromNum(0); }
                }) };
                _globals["clearTimeout"] = new JsVal { Obj = new HostFunc(args => { try { int id = (int)ToNum(args.Count > 0 ? args[0] : JsVal.FromNum(0)); _e.ClearTimeout(id); } catch { /* swallow */ } return JsVal.Null(); }) };
                _globals["setInterval"] = new JsVal { Obj = new HostFunc(args =>
                {
                    try
                    {
                        var code = ToStr(args.Count > 0 ? args[0] : JsVal.Null());
                        int ms = (int)ToNum(args.Count > 1 ? args[1] : JsVal.FromNum(0));
                        var id = _e.ScheduleInterval(string.IsNullOrEmpty(code) ? "" : code, ms);
                        return JsVal.FromNum(id);
                    }
                    catch { return JsVal.FromNum(0); }
                }) };
                _globals["clearInterval"] = new JsVal { Obj = new HostFunc(args => { try { int id = (int)ToNum(args.Count > 0 ? args[0] : JsVal.FromNum(0)); _e.ClearTimeout(id); } catch { /* swallow */ } return JsVal.Null(); }) };

                // queueMicrotask(fn)
                _globals["queueMicrotask"] = new JsVal { Obj = new HostFunc(args => { try { var a = args.Count > 0 ? args[0] : JsVal.Null(); var f = a.Obj as JsFuncDef; if (f != null) { _e.EnqueueMicrotask(() => { try { var r = new JsMiniRunner(_e); r.InvokeFunction(f, new List<JsVal>()); } catch { /* swallow */ } }); } else { var code = ToStr(a); if (!string.IsNullOrEmpty(code)) _e.EnqueueMicrotask(() => { try { _e.RunInline(code, _e._ctx); } catch { /* swallow */ } }); } } catch { /* swallow */ } return JsVal.Null(); }) };

                // atob/btoa helpers (string only)
                _globals["atob"] = new JsVal { Obj = new HostFunc(args => { try { var s = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); var bytes = Convert.FromBase64String(s ?? ""); var txt = Encoding.UTF8.GetString(bytes, 0, bytes.Length); return JsVal.FromStr(txt); } catch { return JsVal.FromStr(""); } }) };
                    _globals["btoa"] = new JsVal { Obj = new HostFunc(args => { try { var s = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); var bytes = Encoding.UTF8.GetBytes(s ?? ""); var b64 = Convert.ToBase64String(bytes); return JsVal.FromStr(b64); } catch { return JsVal.FromStr(""); } }) };

                    // getComputedStyle — populates from _computedStyles if available, else raw defaults
                    _globals["getComputedStyle"] = new JsVal { Obj = new HostFunc(args =>
                    {
                        try
                        {
                            var styleMap = new Dictionary<string, JsVal>(StringComparer.OrdinalIgnoreCase);
                            LiteElement node = null;
                            CssComputed css = null;
                            if (args.Count > 0)
                            {
                                var a0 = args[0];
                                var jde = a0.Obj as JsDomElement; if (jde != null) node = jde._node;
                                var hel = a0.Obj as HostElement; if (hel != null) node = hel.Node;
                                if (node != null && _e._computedStyles != null)
                                    _e._computedStyles.TryGetValue(node, out css);
                            }
                            // defaults (used as fallback if no computed style)
                            styleMap["display"] = JsVal.FromStr("block");
                            styleMap["position"] = JsVal.FromStr("static");
                            styleMap["visibility"] = JsVal.FromStr("visible");
                            styleMap["opacity"] = JsVal.FromStr("1");
                            styleMap["overflow"] = JsVal.FromStr("visible");
                            styleMap["transform"] = JsVal.FromStr("none");
                            styleMap["z-index"] = JsVal.FromStr("auto");
                            styleMap["color"] = JsVal.FromStr("#000000");
                            styleMap["text-align"] = JsVal.FromStr("start");
                            styleMap["font-size"] = JsVal.FromStr("16px");
                            styleMap["line-height"] = JsVal.FromStr("1.5");
                            styleMap["background-color"] = JsVal.FromStr("transparent");
                            styleMap["border-color"] = JsVal.FromStr("transparent");
                            styleMap["background-repeat"] = JsVal.FromStr("repeat");
                            styleMap["background-size"] = JsVal.FromStr("auto");
                            styleMap["background-position"] = JsVal.FromStr("0% 0%");
                            styleMap["border-top-width"] = JsVal.FromStr("0px");
                            styleMap["border-right-width"] = JsVal.FromStr("0px");
                            styleMap["border-bottom-width"] = JsVal.FromStr("0px");
                            styleMap["border-left-width"] = JsVal.FromStr("0px");

                            // overwrite defaults with real computed values
                            if (css != null) PopulateStyleMap(styleMap, css);

                            // still apply inline style attribute on top (highest priority for the JS view)
                            if (node != null && node.Attr != null)
                            {
                                Func<string,string,string> pick = (style, prop) =>
                                {
                                    try { foreach (var part in (style ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)) { var kv = part.Split(new[] { ':' }, 2); if (kv.Length == 2 && kv[0].Trim().Equals(prop, StringComparison.OrdinalIgnoreCase)) return kv[1].Trim(); } } catch { /* swallow */ } return null;
                                };
                                string styleAttr; if (node.Attr.TryGetValue("style", out styleAttr))
                                {
                                    Action<string,string> apply = (prop, key) => { var v = pick(styleAttr, prop); if (!string.IsNullOrEmpty(v)) styleMap[key ?? prop] = JsVal.FromStr(v); };
                                    apply("overflow", null); apply("transform", null); apply("z-index", null); apply("width", null); apply("height", null);
                                    apply("color", null); apply("text-align", null); apply("font-size", null); apply("line-height", null);
                                    apply("background-repeat", null); apply("background-size", null); apply("background-position", null);
                                    apply("border-top-width", null); apply("border-right-width", null); apply("border-bottom-width", null); apply("border-left-width", null);
                                    apply("background-color", null); apply("border-color", null); apply("font-weight", null);
                                }
                                string w; if (node.Attr.TryGetValue("width", out w) && !styleMap.ContainsKey("width")) styleMap["width"] = JsVal.FromStr(w.EndsWith("px")?w:(w+"px"));
                                string h; if (node.Attr.TryGetValue("height", out h) && !styleMap.ContainsKey("height")) styleMap["height"] = JsVal.FromStr(h.EndsWith("px")?h:(h+"px"));
                            }

                            styleMap["getPropertyValue"] = new JsVal { Obj = new HostFunc(a => { try { var p = ToStr(a.Count > 0 ? a[0] : JsVal.Null()).ToLowerInvariant(); JsVal v; if (styleMap.TryGetValue(p, out v)) return v; } catch { /* swallow */ } return JsVal.FromStr(""); }) };
                            return new JsVal { Obj = styleMap };
                        }
                        catch { return new JsVal { Obj = new Dictionary<string, JsVal>(StringComparer.Ordinal) }; }
                    }) };
                }

        private static void PopulateStyleMap(Dictionary<string, JsVal> map, CssComputed css)
        {
            if (css == null) return;
            void SetStr(string key, string val) { if (val != null) map[key] = JsVal.FromStr(val); }
            void SetDbl(string key, double? val, string unit = "px") { if (val.HasValue) map[key] = JsVal.FromStr(val.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + unit); }

            SetStr("display", css.Display);
            SetStr("position", css.Position);
            SetStr("visibility", css.Visibility);
            if (css.Opacity.HasValue) map["opacity"] = JsVal.FromStr(css.Opacity.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            SetStr("overflow", css.Overflow ?? css.OverflowX);
            SetStr("overflow-x", css.OverflowX);
            SetStr("overflow-y", css.OverflowY);
            SetStr("transform", css.Transform);
            SetStr("transform-origin", css.TransformOrigin);
            if (css.ZIndex.HasValue) map["z-index"] = JsVal.FromStr(css.ZIndex.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            else map["z-index"] = JsVal.FromStr("auto");
            SetStr("box-sizing", css.BoxSizing);
            SetStr("float", css.Float);
            SetStr("clear", css.Clear);

            SetDbl("width", css.Width);
            SetDbl("height", css.Height);
            SetDbl("min-width", css.MinWidth);
            SetDbl("min-height", css.MinHeight);
            SetDbl("max-width", css.MaxWidth);
            SetDbl("max-height", css.MaxHeight);
            SetDbl("left", css.Left);
            SetDbl("top", css.Top);
            SetDbl("right", css.Right);
            SetDbl("bottom", css.Bottom);

            SetDbl("margin-top", css.Margin.Top);
            SetDbl("margin-right", css.Margin.Right);
            SetDbl("margin-bottom", css.Margin.Bottom);
            SetDbl("margin-left", css.Margin.Left);
            SetDbl("padding-top", css.Padding.Top);
            SetDbl("padding-right", css.Padding.Right);
            SetDbl("padding-bottom", css.Padding.Bottom);
            SetDbl("padding-left", css.Padding.Left);
            SetDbl("border-top-width", css.BorderThickness.Top);
            SetDbl("border-right-width", css.BorderThickness.Right);
            SetDbl("border-bottom-width", css.BorderThickness.Bottom);
            SetDbl("border-left-width", css.BorderThickness.Left);

            SetStr("border-style", css.BorderStyle);
            if (css.BorderBrushColor.HasValue)
                map["border-color"] = JsVal.FromStr(ColorToCss(css.BorderBrushColor.Value));
            else if (css.BorderBrush != null)
                map["border-color"] = JsVal.FromStr(css.BorderBrush.ToString());

            SetDbl("outline-width", css.OutlineWidth);
            SetStr("outline-style", css.OutlineStyle);
            if (css.OutlineColor.HasValue) map["outline-color"] = JsVal.FromStr(ColorToCss(css.OutlineColor.Value));

            SetDbl("font-size", css.FontSize);
            SetStr("font-family", css.FontFamilyName);
            if (css.FontWeight.HasValue) map["font-weight"] = JsVal.FromStr(css.FontWeight.Value.Weight.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (css.FontStyle.HasValue) map["font-style"] = JsVal.FromStr(css.FontStyle.Value == Windows.UI.Text.FontStyle.Italic ? "italic" : css.FontStyle.Value == Windows.UI.Text.FontStyle.Oblique ? "oblique" : "normal");
            SetDbl("line-height", css.LineHeight);
            SetDbl("letter-spacing", css.LetterSpacing);
            SetDbl("word-spacing", css.WordSpacing);
            SetDbl("text-indent", css.TextIndent);
            SetStr("text-align", css.TextAlign.HasValue ? css.TextAlign.Value.ToString().ToLowerInvariant() : null);
            SetStr("text-transform", css.TextTransform);
            SetStr("text-decoration", css.TextDecoration);
            SetStr("vertical-align", css.VerticalAlign);
            SetStr("white-space", css.WhiteSpace);
            SetStr("text-overflow", css.TextOverflow);
            SetStr("hyphens", css.Hyphens);

            SetStr("color", css.ForegroundColor.HasValue ? ColorToCss(css.ForegroundColor.Value) : (css.Foreground != null ? css.Foreground.ToString() : null));
            SetStr("background-color", css.BackgroundColor.HasValue ? ColorToCss(css.BackgroundColor.Value) : (css.Background != null ? css.Background.ToString() : null));
            SetStr("background-repeat", css.BackgroundRepeat);
            SetStr("background-position", css.BackgroundPosition);
            SetStr("background-size", css.BackgroundSize);
            if (!string.IsNullOrEmpty(css.BackgroundImageUrl)) map["background-image"] = JsVal.FromStr("url(" + css.BackgroundImageUrl + ")");

            SetStr("list-style-type", css.ListStyleType);
            SetStr("list-style-position", css.ListStylePosition);
            SetStr("list-style-image", css.ListStyleImage);

            SetStr("object-fit", css.ObjectFit);
            SetStr("pointer-events", css.PointerEvents);
            SetStr("cursor", css.Cursor);
            SetStr("text-shadow", css.TextShadow);
            SetStr("box-shadow", css.BoxShadow);

            SetStr("flex-direction", css.FlexDirection);
            SetStr("flex-wrap", css.FlexWrap);
            SetStr("justify-content", css.JustifyContent);
            SetStr("align-items", css.AlignItems);
            SetStr("align-content", css.AlignContent);
            SetDbl("flex-grow", css.FlexGrow, "");
            SetDbl("flex-shrink", css.FlexShrink, "");
            SetDbl("flex-basis", css.FlexBasis);

            SetDbl("column-gap", css.ColumnGap);
            SetDbl("row-gap", css.RowGap);
            SetDbl("gap", css.Gap);

            SetStr("grid-template-columns", css.GridTemplateColumns);
            SetStr("grid-template-rows", css.GridTemplateRows);
            SetStr("grid-template-areas", css.GridTemplateAreas);
            SetStr("grid-auto-columns", css.GridAutoColumns);
            SetStr("grid-auto-rows", css.GridAutoRows);
            SetStr("grid-auto-flow", css.GridAutoFlow);
            SetStr("grid-column", css.GridColumn);
            SetStr("grid-row", css.GridRow);
            SetStr("grid-area", css.GridArea);

            SetDbl("border-spacing", css.BorderSpacing);
            SetDbl("border-spacing-vertical", css.BorderSpacingVertical);

            SetDbl("aspect-ratio", css.AspectRatio, "");
        }

        private static string ColorToCss(Windows.UI.Color c)
        {
            if (c.A < 255) return string.Format(System.Globalization.CultureInfo.InvariantCulture, "rgba({0},{1},{2},{3:0.##})", c.R, c.G, c.B, c.A / 255.0);
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

public bool Execute(string code)
            {
                if (string.IsNullOrWhiteSpace(code)) return true;
                _src = code; _pos = 0; _len = code.Length;
                try
                {
                    SkipWs();
                    while (!Eof())
                    {
                        ParseStatement();
                        SkipWs();
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    _e.ReportMiniRunnerFailure("Execute", CurrentSnippet(), ex);
                    return false;
                }
            }

            public bool TryRunInline(string code, string eventType, string eventTargetId, out bool? cancelDefault)
            {
                cancelDefault = null;
                if (string.IsNullOrWhiteSpace(code)) return true;
                var evt = new HostEvent(_e, eventType, eventTargetId);
                JsVal prevEvent = null; bool hadEvent = _globals.TryGetValue("event", out prevEvent);
                try
                {
                    _globals["event"] = new JsVal { Obj = evt };
                    _globals["__mini_result"] = JsVal.Null();
                    var wrapped = "__mini_result = (function(){ " + code + " })();";
                    if (!Execute(wrapped)) return false;
                    JsVal res;
                    if (_globals.TryGetValue("__mini_result", out res) && res != null)
                    {
                        if (res.Bool.HasValue) cancelDefault = !res.Bool.Value;
                    }
                    if (evt.DefaultPrevented)
                    {
                        cancelDefault = true;
                        _e._preventDefaultRequested = true;
                    }
                    if (evt.PropagationStopped) _e._stopPropagationRequested = true;
                    return true;
                }
                catch (Exception ex)
                {
                    _e.ReportMiniRunnerFailure("RunInline.Execute", code, ex);
                    return false;
                }
                finally
                {
                    if (hadEvent) _globals["event"] = prevEvent; else _globals.Remove("event");
                    _globals.Remove("__mini_result");
                }
            }

            // ---------- Lexer helpers ----------
            private bool Eof() { return _pos >= _len; }
            private char Cur() { return _pos < _len ? _src[_pos] : '\0'; }
            private char Peek(int d) { int i = _pos + d; return i < _len ? _src[i] : '\0'; }
            private void Next() { _pos++; }
            private void SkipWs()
            {
                while (!Eof())
                {
                    char c = Cur();
                    if (char.IsWhiteSpace(c)) { _pos++; continue; }
                    if (c == '/' && Peek(1) == '/') { while (!Eof() && Cur() != '\n') _pos++; continue; }
                    if (c == '/' && Peek(1) == '*') { _pos += 2; while (!Eof() && !(Cur() == '*' && Peek(1) == '/')) _pos++; if (!Eof()) _pos += 2; continue; }
                    break;
                }
            }

            private string CurrentSnippet()
            {
                try
                {
                    var start = Math.Max(0, _pos - 24);
                    var end = Math.Min(_len, _pos + 24);
                    if (end <= start) return string.Empty;
                    return _src.Substring(start, end - start);
                }
                catch { return string.Empty; }
            }

            private bool TerminatorContains(char[] arr, char c)
            {
                if (arr == null || arr.Length == 0) return false;
                for (int i = 0; i < arr.Length; i++)
                    if (arr[i] == c) return true;
                return false;
            }

            private string CaptureInitializerExpression(params char[] terminators)
            {
                SkipWs();
                int start = _pos;
                int depthParen = 0, depthBrace = 0, depthBracket = 0;
                bool inString = false;
                char stringQuote = '\0';
                bool escape = false;

                while (!Eof())
                {
                    char ch = Cur();
                    if (inString)
                    {
                        _pos++;
                        if (escape)
                        {
                            escape = false;
                            continue;
                        }
                        if (ch == '\\')
                        {
                            escape = true;
                            continue;
                        }
                        if (ch == stringQuote)
                        {
                            inString = false;
                        }
                        continue;
                    }

                    if (ch == '\'' || ch == '"')
                    {
                        inString = true;
                        stringQuote = ch;
                        _pos++;
                        continue;
                    }

                    if (ch == '(')
                    {
                        depthParen++;
                        _pos++;
                        continue;
                    }
                    if (ch == ')')
                    {
                        if (depthParen > 0)
                        {
                            depthParen--;
                            _pos++;
                            continue;
                        }
                        if (TerminatorContains(terminators, ')')) break;
                        _pos++;
                        continue;
                    }

                    if (ch == '{')
                    {
                        depthBrace++;
                        _pos++;
                        continue;
                    }
                    if (ch == '}')
                    {
                        if (depthBrace > 0)
                        {
                            depthBrace--;
                            _pos++;
                            continue;
                        }
                        if (TerminatorContains(terminators, '}')) break;
                        _pos++;
                        continue;
                    }

                    if (ch == '[')
                    {
                        depthBracket++;
                        _pos++;
                        continue;
                    }
                    if (ch == ']')
                    {
                        if (depthBracket > 0)
                        {
                            depthBracket--;
                            _pos++;
                            continue;
                        }
                        if (TerminatorContains(terminators, ']')) break;
                        _pos++;
                        continue;
                    }

                    if (ch == '/' && Peek(1) == '/')
                    {
                        while (!Eof() && Cur() != '\n') _pos++;
                        continue;
                    }
                    if (ch == '/' && Peek(1) == '*')
                    {
                        _pos += 2;
                        while (!Eof() && !(Cur() == '*' && Peek(1) == '/')) _pos++;
                        if (!Eof()) _pos += 2;
                        continue;
                    }

                    if (depthParen == 0 && depthBrace == 0 && depthBracket == 0)
                    {
                        if (ch == ',' || TerminatorContains(terminators, ch))
                        {
                            break;
                        }
                    }

                    _pos++;
                }

                int end = _pos;
                return _src.Substring(start, Math.Max(0, end - start)).Trim();
            }

            private JsBindingPattern ParseBindingPattern()
            {
                SkipWs();
                if (Match("{"))
                {
                    return ParseObjectBindingPattern();
                }
                if (Match("["))
                {
                    return ParseArrayBindingPattern();
                }
                return new JsIdentifierPattern { Name = ParseIdent() };
            }

            private JsBindingPattern ParseObjectBindingPattern()
            {
                var pattern = new JsObjectPattern();
                SkipWs();
                if (Match("}")) return pattern;

                while (true)
                {
                    SkipWs();
                    if (!Eof() && Cur() == '}')
                    {
                        Next();
                        break;
                    }
                    if (Match("..."))
                    {
                        try
                        {
                            pattern.RestIdentifier = ParseIdent();
                        }
                        catch
                        {
                            _e.TraceFeatureGap("MiniRunner", "DestructureRestIdentifier", CurrentSnippet());
                            throw;
                        }
                        SkipWs();
                        Expect("}");
                        break;
                    }

                    if (Cur() == '[')
                    {
                        _e.TraceFeatureGap("MiniRunner", "DestructureComputedKey", CurrentSnippet());
                        throw new Exception("computed property not supported");
                    }

                    string key;
                    if (Cur() == '\'' || Cur() == '"') key = ParseString();
                    else key = ParseIdent();

                    SkipWs();
                    JsBindingPattern target;
                    if (Match(":")) target = ParseBindingPattern();
                    else target = new JsIdentifierPattern { Name = key };

                    SkipWs();
                    if (Match("="))
                    {
                        var defExpr = CaptureInitializerExpression('}', ',');
                        target.DefaultExpr = defExpr;
                        SkipWs();
                    }

                    pattern.Properties.Add(new JsObjectPattern.PropertyBinding { Key = key, Target = target });

                    if (Match("}")) break;
                    Expect(",");
                }

                return pattern;
            }

            private JsBindingPattern ParseArrayBindingPattern()
            {
                var pattern = new JsArrayPattern();
                SkipWs();
                if (Match("]")) return pattern;

                while (true)
                {
                    SkipWs();
                    if (!Eof() && Cur() == ']')
                    {
                        Next();
                        break;
                    }
                    if (Match("..."))
                    {
                        var rest = ParseBindingPattern() as JsIdentifierPattern;
                        if (rest == null || string.IsNullOrEmpty(rest.Name))
                        {
                            _e.TraceFeatureGap("MiniRunner", "DestructureArrayRest", CurrentSnippet());
                            throw new Exception("array rest requires identifier");
                        }
                        pattern.RestTarget = rest;
                        SkipWs();
                        Expect("]");
                        break;
                    }

                    if (Cur() == ',')
                    {
                        Next();
                        pattern.Elements.Add(new JsArrayPattern.ElementBinding { IsHole = true });
                        continue;
                    }

                    var element = ParseBindingPattern();

                    SkipWs();
                    if (Match("="))
                    {
                        var defExpr = CaptureInitializerExpression(']', ',');
                        element.DefaultExpr = defExpr;
                        SkipWs();
                    }

                    pattern.Elements.Add(new JsArrayPattern.ElementBinding { Target = element });

                    if (Match("]")) break;
                    Expect(",");
                }

                return pattern;
            }

            private JsVal EvaluateInitializerSnippet(string snippet)
            {
                var expr = (snippet ?? string.Empty).Trim();
                if (expr.Length == 0) return JsVal.Null();
                try
                {
                    var child = new JsMiniRunner(_e);
                    child._globals.Clear();
                    foreach (var kv in _globals) child._globals[kv.Key] = kv.Value;
                    child._src = expr + ";";
                    child._pos = 0;
                    child._len = child._src.Length;
                    child.SkipWs();
                    var value = child.ParseExpression();
                    child.Expect(";");
                    foreach (var kv in child._globals) _globals[kv.Key] = kv.Value;
                    return value ?? JsVal.Null();
                }
                catch
                {
                    _e.TraceFeatureGap("MiniRunner", "DestructureDefaultEval", expr);
                    return JsVal.Null();
                }
            }

            private void AssignBinding(JsBindingPattern pattern, JsVal value, bool hasValue, bool declBlockScoped = false)
            {
                if (pattern == null) return;

                bool effectiveHasValue = hasValue;
                if (!effectiveHasValue && !string.IsNullOrWhiteSpace(pattern.DefaultExpr))
                {
                    var fallback = EvaluateInitializerSnippet(pattern.DefaultExpr);
                    if (fallback != null)
                    {
                        value = fallback;
                        effectiveHasValue = true;
                    }
                }

                var id = pattern as JsIdentifierPattern;
                if (id != null)
                {
                    if (!string.IsNullOrEmpty(id.Name))
                    {
                        SetVarDecl(id.Name, effectiveHasValue ? (value ?? JsVal.Null()) : JsVal.Null(), declBlockScoped);
                    }
                    return;
                }

                var obj = pattern as JsObjectPattern;
                if (obj != null)
                {
                    Dictionary<string, JsVal> dict = null;
                    if (effectiveHasValue) dict = value != null ? value.Obj as Dictionary<string, JsVal> : null;

                    foreach (var prop in obj.Properties)
                    {
                        JsVal extracted = JsVal.Null();
                        bool propHas = false;
                        if (effectiveHasValue)
                        {
                            if (dict != null && prop.Key != null && dict.TryGetValue(prop.Key, out extracted))
                            {
                                propHas = true;
                            }
                            else
                            {
                                try
                                {
                                    var member = GetMember(value, prop.Key);
                                    if (member != null)
                                    {
                                        extracted = member;
                                        propHas = true;
                                    }
                                }
                                catch { /* swallow */ }
                            }
                        }
                        AssignBinding(prop.Target, extracted, propHas, declBlockScoped);
                    }

                    if (!string.IsNullOrEmpty(obj.RestIdentifier))
                    {
                        var rest = new Dictionary<string, JsVal>(StringComparer.Ordinal);
                        if (effectiveHasValue && dict != null)
                        {
                            var used = new HashSet<string>(StringComparer.Ordinal);
                            foreach (var prop in obj.Properties)
                            {
                                if (!string.IsNullOrEmpty(prop.Key)) used.Add(prop.Key);
                            }
                            foreach (var kv in dict)
                            {
                                if (!used.Contains(kv.Key)) rest[kv.Key] = kv.Value;
                            }
                        }
                        SetVarDecl(obj.RestIdentifier, new JsVal { Obj = rest }, declBlockScoped);
                    }
                    return;
                }

                var arr = pattern as JsArrayPattern;
                if (arr != null)
                {
                    List<JsVal> list = null;
                    if (effectiveHasValue) list = value != null ? value.Obj as List<JsVal> : null;
                    int index = 0;
                    foreach (var element in arr.Elements)
                    {
                        if (element.IsHole)
                        {
                            index++;
                            continue;
                        }

                        JsVal elementValue = JsVal.Null();
                        bool elementHas = false;
                        if (effectiveHasValue && list != null && index < list.Count)
                        {
                            elementValue = list[index];
                            elementHas = true;
                        }
                        AssignBinding(element.Target, elementValue, elementHas, declBlockScoped);
                        index++;
                    }

                    if (arr.RestTarget != null)
                    {
                        var restList = new List<JsVal>();
                        if (effectiveHasValue && list != null && index < list.Count)
                        {
                            for (int i = index; i < list.Count; i++) restList.Add(list[i]);
                        }
                        SetVarDecl(arr.RestTarget.Name, new JsVal { Obj = restList }, declBlockScoped);
                    }
                }
            }

            private List<JsFuncParam> ParseFunctionParameterList(char closing)
            {
                var list = new List<JsFuncParam>();
                SkipWs();
                if (Match(closing.ToString())) return list;

                while (true)
                {
                    var param = ParseFunctionParameter(closing);
                    list.Add(param);
                    SkipWs();
                    if (Match(closing.ToString())) break;
                    Expect(",");
                }

                return list;
            }

            private JsFuncParam ParseFunctionParameter(char closing)
            {
                SkipWs();
                int start = _pos;
                bool isRest = Match("...");

                JsBindingPattern pattern;
                if (isRest)
                {
                    try
                    {
                        var id = ParseIdent();
                        pattern = new JsIdentifierPattern { Name = id };
                    }
                    catch
                    {
                        _e.TraceFeatureGap("MiniRunner", "RestParameterIdentifier", CurrentSnippet());
                        throw;
                    }
                }
                else
                {
                    pattern = ParseBindingPattern();
                }

                SkipWs();
                if (Match("="))
                {
                    var defExpr = CaptureInitializerExpression(closing, ',');
                    pattern.DefaultExpr = defExpr;
                    SkipWs();
                }

                int end = _pos;
                var raw = _src.Substring(start, Math.Max(0, end - start));
                return new JsFuncParam { Raw = raw.Trim(), Pattern = pattern, IsRest = isRest };
            }

            private void PopulateFunctionParameters(JsFuncDef def, IEnumerable<JsFuncParam> parameters)
            {
                if (def == null || parameters == null) return;
                foreach (var param in parameters)
                {
                    if (param == null) continue;
                    def.Parameters.Add(param);
                    var idParam = param.Pattern as JsIdentifierPattern;
                    var hasDefault = param.Pattern != null && !string.IsNullOrWhiteSpace(param.Pattern.DefaultExpr);
                    if (idParam != null && !param.IsRest && !hasDefault)
                        def.Params.Add(idParam.Name);
                    else
                        def.Params.Add(param.Raw);
                }
            }

            private bool Match(string s)
            { SkipWs(); if (_pos + s.Length <= _len && string.Compare(_src, _pos, s, 0, s.Length, StringComparison.Ordinal) == 0) { _pos += s.Length; return true; } return false; }
            private void Expect(string s) { if (!Match(s)) throw new Exception("expect " + s); }

            private string ParseIdent()
            {
                SkipWs();
                if (Eof())
                {
                    _e.TraceFeatureGap("MiniRunner", "IdentifierMissing", CurrentSnippet());
                    throw new Exception("ident");
                }

                int start = _pos;
                char c = Cur();
                if (!(char.IsLetter(c) || c == '_' || c == '$'))
                {
                    _e.TraceFeatureGap("MiniRunner", "IdentifierStart", CurrentSnippet());
                    throw new Exception("ident");
                }

                Next();
                while (!Eof())
                {
                    c = Cur();
                    if (char.IsLetterOrDigit(c) || c == '_' || c == '$')
                    {
                        Next();
                    }
                    else
                    {
                        break;
                    }
                }

                return _src.Substring(start, _pos - start);
            }

            private string ParseString()
            {
                SkipWs(); char q = Cur(); if (q != '\'' && q != '"') throw new Exception("string");
                Next(); var sb = new StringBuilder(); bool esc = false;
                while (!Eof())
                {
                    char c = Cur(); Next();
                    if (esc) { sb.Append(c); esc = false; continue; }
                    if (c == '\\') { esc = true; continue; }
                    if (c == q) break; sb.Append(c);
                }
                return sb.ToString();
            }

            private JsVal ParseNumber()
            {
                SkipWs(); int start = _pos; bool dot = false; if (Cur() == '-') Next();
                while (!Eof()) { char c = Cur(); if (char.IsDigit(c)) Next(); else if (c == '.' && !dot) { dot = true; Next(); } else break; }
                var s = _src.Substring(start, _pos - start); double d = 0; double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d); return JsVal.FromNum(d);
            }

            // ---------- Parser/Eval ----------
            private void ParseStatement()
            {
                SkipWs();
                if (Match("var")) { ParseVarDecl(false); return; }
                if (Match("let") || Match("const")) { ParseVarDecl(true); return; }
                if (Match("if")) { ParseIf(); return; }
                if (Match("while")) { ParseWhile(); return; }
                if (Match("for")) { ParseFor(); return; }
                if (Match("try")) { ParseTryCatchFinally(); return; }
                if (Match("throw")) { var exv = ParseExpression(); Expect(";"); throw new ThrowEx(exv); }
                if (Match("return")) { JsVal v = JsVal.Null(); if (!Match(";")) { v = ParseExpression(); Expect(";"); } throw new ReturnEx(v); }
                if (Match("{")) { PushBlockScope(); while (!Match("}")) ParseStatement(); PopBlockScope(); return; }
                if (Match("class")) { ParseClassDecl(); return; }
                // function decl (parse params)
                if (Match("function"))
                {
                    var name = ParseIdent(); Expect("(");
                    var parameters = ParseFunctionParameterList(')');
                    Expect("{"); int bodyStart = _pos; int depth = 1; while (!Eof() && depth > 0) { if (Cur() == '{') depth++; else if (Cur() == '}') depth--; Next(); }
                    string body = _src.Substring(bodyStart, Math.Max(0, _pos - bodyStart - 1));
                    try { _e._userFunctions[name] = body; } catch { /* swallow */ }
                    try
                    {
                        var def = new JsFuncDef { Body = body };
                        PopulateFunctionParameters(def, parameters);
                        _e._userFunctionsEx[name] = def; SetVarDecl(name, new JsVal { Obj = def }, false);
                    }
                    catch { /* swallow */ }
                    return;
                }
                // expression / call / assignment
                var exprStart = _pos; var v2 = ParseExpression(); Expect(";");
                // Best-effort call routing for known globals: console.log, alert, window.open, location.reload, history.pushState/replaceState
                // We detect these by re-reading the slice and letting RunInline handle them when they match allowlist.
                try
                {
                    var slice = _src.Substring(exprStart, Math.Max(0, _pos - exprStart));
                    if (slice.IndexOf("console.", StringComparison.Ordinal) >= 0 || slice.IndexOf("alert(", StringComparison.Ordinal) >= 0 || slice.IndexOf("window.", StringComparison.Ordinal) >= 0 || slice.IndexOf("location.", StringComparison.Ordinal) >= 0 || slice.IndexOf("history.", StringComparison.Ordinal) >= 0 || slice.IndexOf("document.getElementById", StringComparison.Ordinal) >= 0 || slice.IndexOf(".style", StringComparison.Ordinal) >= 0 || slice.IndexOf("classList", StringComparison.Ordinal) >= 0)
                    {
                        try
                        {
                            var mStyle = Regex.Match(slice, @"document\s*\.\s*getElementById\s*\(\s*(['""])(?<id>[^'""]+)\1\s*\)\s*\.\s*style\s*\.\s*(?<prop>[A-Za-z-]+)\s*=", RegexOptions.IgnoreCase);
                            if (mStyle.Success)
                            {
                                var id = mStyle.Groups["id"].Value; var prop = mStyle.Groups["prop"].Value; _e.TryUpdateInlineStyle(id, prop, ToStr(v2));
                            }
                            else
                            {
                                var mInner = Regex.Match(slice, @"document\s*\.\s*getElementById\s*\(\s*(['""])(?<id>[^'""]+)\1\s*\)\s*\.\s*innerText\s*=\s*(['""])(?<val>.*?)\3", RegexOptions.IgnoreCase);
                                if (mInner.Success)
                                {
                                    var id2 = mInner.Groups["id"].Value; var val2 = mInner.Groups["val"].Value;
                                    try { var doc2 = new JsDocument(_e, _e._domRoot); var el2 = doc2.getElementById(id2) as JsDomElement; if (el2 != null) el2.innerText = val2; } catch { /* swallow */ }
                                }
                                else
                                {
                                    var mHref = Regex.Match(slice, @"(?:window\s*\.\s*)?location\s*\.\s*href\s*=\s*(['""])(?<u>.*?)\1", RegexOptions.IgnoreCase);
                                    if (mHref.Success)
                                    {
                                        try { var url = mHref.Groups["u"].Value; var abs = Resolve(_e._ctx?.BaseUri, url); if (abs != null) _e._host.Navigate(abs); else _e.Navigate(url); } catch { /* swallow */ }
                                    }
                                    else
                                    {
                                var mCls = Regex.Match(slice, @"document\s*\.\s*getElementById\s*\(\s*(['""])(?<id>[^'""]+)\1\s*\)\s*\.\s*classList\s*\.\s*(?<op>add|remove|toggle)\s*\(\s*(['""])(?<cls>[^'""]+)\4\s*\)", RegexOptions.IgnoreCase);
                                        if (mCls.Success) _e.TryUpdateClassList(mCls.Groups["id"].Value, mCls.Groups["op"].Value, mCls.Groups["cls"].Value);
                                        else _e.RunInline(slice, _e._ctx);
                                    }
                                }
                            }
                        }
                        catch { /* swallow */ }
                    }
                }
                catch { /* swallow */ }
            }

            private void ParseVarDecl(bool blockScoped)
            {
                SkipWs();
                int patternStart = _pos;
                JsBindingPattern pattern = null;
                char cur = Cur();
                if (cur == '{' || cur == '[')
                {
                    try
                    {
                        pattern = ParseBindingPattern();
                    }
                    catch
                    {
                        _pos = patternStart;
                        pattern = null;
                    }
                }

                if (pattern != null)
                {
                    SkipWs();
                    if (!Match("="))
                    {
                        _e.TraceFeatureGap("MiniRunner", "DestructureMissingInitializer", CurrentSnippet());
                        Expect("=");
                    }
                    var value = ParseExpression();
                    var hasValue = value != null;
                    Expect(";");
                    AssignBinding(pattern, value, hasValue, blockScoped);
                    return;
                }

                _pos = patternStart;
                var name = ParseIdent();
                JsVal val = JsVal.Null();
                if (Match("=")) val = ParseExpression();
                Expect(";");
                SetVarDecl(name, val, blockScoped);
            }

            private void ParseClassDecl()
            {
                var name = ParseIdent();
                SkipWs();
                if (Match("extends"))
                {
                    // Basic support for "extends" keyword - parse and ignore for now
                    try { ParseIdent(); } catch { /* swallow */ }
                    SkipWs();
                }

                Expect("{");

                string ctorBody = null;
                var ctorParams = new List<JsFuncParam>();
                var methods = new List<ClassMethod>();

                while (!Match("}"))
                {
                    SkipWs();
                    bool isStatic = Match("static");
                    SkipWs();

                    string methodName;
                    if (Match("constructor")) methodName = "constructor";
                    else methodName = ParseIdent();

                    Expect("(");
                    var parameters = ParseFunctionParameterList(')');

                    Expect("{");
                    int bodyStart = _pos; int depth = 1;
                    while (!Eof() && depth > 0)
                    {
                        if (Cur() == '{') depth++;
                        else if (Cur() == '}') depth--;
                        Next();
                    }
                    string body = _src.Substring(bodyStart, Math.Max(0, _pos - bodyStart - 1));

                    if (methodName == "constructor" && !isStatic)
                    {
                        ctorBody = body;
                        ctorParams = parameters;
                    }
                    else
                    {
                        methods.Add(new ClassMethod { Name = methodName, Parameters = parameters, Body = body, IsStatic = isStatic });
                    }

                    SkipWs();
                }

                var fnBody = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(ctorBody))
                {
                    fnBody.Append(ctorBody);
                    if (!ctorBody.TrimEnd().EndsWith(";")) fnBody.AppendLine();
                }

                foreach (var method in methods)
                {
                    if (method.IsStatic) continue;
                    fnBody.Append("this.")
                          .Append(method.Name)
                          .Append(" = function(")
                          .Append(string.Join(",", method.Parameters.Select(p => p.Raw)))
                          .Append("){")
                          .Append(method.Body)
                          .Append("};");
                }

                if (fnBody.Length == 0) fnBody.Append(";");

                var def = new JsFuncDef { Body = fnBody.ToString() };
                PopulateFunctionParameters(def, ctorParams);

                SetVarDecl(name, new JsVal { Obj = def }, false);
                try { _e._userFunctionsEx[name] = def; } catch { /* swallow */ }

                var staticMethods = methods.Where(m => m.IsStatic).ToList();
                if (staticMethods.Count > 0)
                {
                    var bag = new Dictionary<string, JsVal>(StringComparer.Ordinal);
                    foreach (var sm in staticMethods)
                    {
                        var smDef = new JsFuncDef { Body = sm.Body };
                        PopulateFunctionParameters(smDef, sm.Parameters);
                        bag[sm.Name] = new JsVal { Obj = smDef };
                    }
                    _globals["__fn_" + def.GetHashCode()] = new JsVal { Obj = bag };
                }
            }

            private void ParseIf()
            {
                Expect("("); var cond = ParseExpression(); Expect(")"); if (Match("{")) { if (cond.Truthy()) { while (!Match("}")) ParseStatement(); if (Match("else")) { if (Match("{")) { while (!Match("}")) { SkipBlock(); } } } } else { SkipBlockBody(); if (Match("else")) { if (Match("{")) { while (!Match("}")) ParseStatement(); } } } }
                else { // single statement
                    if (cond.Truthy()) ParseStatement(); else { SkipOneStatement(); if (Match("else")) ParseStatement(); }
                }
            }

            private void ParseWhile()
            {
                int save;
                Expect("("); var condStart = _pos; var cond = ParseExpression(); Expect(")");
                if (Match("{"))
                {
                    int bodyStart = _pos; while (cond.Truthy()) { _pos = bodyStart; while (!Match("}")) ParseStatement(); _pos = condStart; cond = ParseExpression(); Expect(")"); Expect("{"); bodyStart = _pos; }
                    // consume remaining body
                    while (!Match("}")) SkipBlock();
                }
                else { save = _pos; if (cond.Truthy()) { ParseStatement(); _pos = condStart; cond = ParseExpression(); Expect(")"); _pos = save; } else { SkipOneStatement(); } }
            }

            private void SkipBlockBody() { int depth = 1; while (!Eof() && depth > 0) { if (Cur() == '{') depth++; else if (Cur() == '}') depth--; Next(); } }
            private void SkipBlock() { if (Cur() == '{') { SkipBlockBody(); } else SkipOneStatement(); }
            private void SkipOneStatement() { if (Match("{")) { SkipBlockBody(); } else { while (!Eof() && Cur() != ';' && Cur() != '}') Next(); if (Cur() == ';') Next(); } }

            private JsVal ParseExpression() { return ParseAssignment(); }

            private sealed class ThrowEx : Exception { public JsVal Value; public ThrowEx(JsVal v) { Value = v; } }

            private void ParseTryCatchFinally()
            {
                // try { ... } [catch (e) { ... }] [finally { ... }]
                string tryBody = CaptureBodyAsText();
                bool hasCatch = false; string catchVar = null; string catchBody = null; bool hasFinally = false; string finallyBody = null;
                SkipWs();
                if (Match("catch"))
                {
                    hasCatch = true; Expect("("); catchVar = ParseIdent(); Expect(")"); catchBody = CaptureBodyAsText();
                }
                SkipWs();
                if (Match("finally"))
                {
                    hasFinally = true; finallyBody = CaptureBodyAsText();
                }
                try
                {
                    ExecuteString(tryBody);
                }
                catch (ThrowEx te)
                {
                    if (hasCatch)
                    {
                        if (!string.IsNullOrEmpty(catchVar)) _globals[catchVar] = te.Value ?? JsVal.Null();
                        ExecuteString(catchBody);
                    }
                }
                finally
                {
                    if (hasFinally) ExecuteString(finallyBody);
                }
            }

            private void ParseFor()
            {
                // Supports:
                //  - for (var k in expr) stmt;
                //  - for (k in expr) stmt;
                //  - for (init; cond; post) stmt;
                Expect("(");
                SkipWs();

                // Try for-in first
                int forHeadStart = _pos;
                bool hadVar = Match("var");
                SkipWs();
                string vname = null; int afterVarPos = _pos;
                try { vname = ParseIdent(); }
                catch { vname = null; }
                SkipWs();
                if (vname != null && Match("in"))
                {
                    // for-in branch
                    var iterable = ParseExpression();
                    Expect(")");
                    string body = CaptureBodyAsText();
                    var dict = iterable.Obj as Dictionary<string, JsVal>;
                    var list = iterable.Obj as List<JsVal>;
                    if (dict != null)
                    {
                        foreach (var kv in dict)
                        {
                            _globals[vname] = JsVal.FromStr(kv.Key);
                            ExecuteString(body);
                        }
                    }
                    else if (list != null)
                    {
                        for (int i = 0; i < list.Count; i++) { _globals[vname] = JsVal.FromNum(i); ExecuteString(body); }
                    }
                    return;
                }

                // Not for-in: reset and parse general for(init;cond;post)
                _pos = forHeadStart;
                // init
                if (Match(";"))
                {
                    // no init
                }
                else if (Match("var"))
                {
                    // simple "var name[=expr]" inside for header
                    SkipWs(); var nm = ParseIdent();
                    if (Match("=")) { var initv = ParseExpression(); _globals[nm] = initv; }
                    else { _globals[nm] = JsVal.Null(); }
                    Expect(";");
                }
                else
                {
                    // expression initializer
                    var _ = ParseExpression();
                    Expect(";");
                }

                // Remember cond and post positions
                int condStart = _pos;
                bool hasCond = true;
                JsVal condValFirst = null;
                if (Match(";"))
                {
                    hasCond = false; // always true
                }
                else
                {
                    // evaluate once to advance to ';'
                    condValFirst = ParseExpression();
                    Expect(";");
                }

                int postStart = _pos;
                // Skip post expression without executing it initially: scan to matching ')'
                int scanPos = _pos; int depth = 0; bool foundClose = false; int closePos = _pos;
                while (!Eof())
                {
                    char ch = Cur();
                    if (ch == '\'') { ParseString(); continue; }
                    if (ch == '"') { ParseString(); continue; }
                    if (ch == '(') { depth++; Next(); continue; }
                    if (ch == ')') { if (depth == 0) { foundClose = true; closePos = _pos; break; } depth--; Next(); continue; }
                    Next();
                }
                if (!foundClose) { if (!Eof() && Cur() == ')') closePos = _pos; }
                _pos = closePos;
                Expect(")");

                // Capture body as text for simple iteration execution
                string bodyText = CaptureBodyAsText();
                int afterBody = _pos;

                // Loop
                while (true)
                {
                    bool condOk = true;
                    if (hasCond)
                    {
                        _pos = condStart;
                        var c = ParseExpression();
                        Expect(";");
                        condOk = c != null && c.Truthy();
                    }
                    if (!condOk) break;

                    // Execute body
                    try { ExecuteString(bodyText); } catch { /* swallow */ }

                    // Execute post
                    _pos = postStart;
                    if (postStart < closePos)
                    {
                        try { var __tmp = ParseExpression(); } catch { /* swallow */ }
                    }
                    // move past ')'
                    try { Expect(")"); } catch { /* swallow */ }
                }

                // position after the entire for statement
                _pos = afterBody;
            }

            private string CaptureBodyAsText()
            {
                SkipWs(); if (Match("{")) { int start = _pos; int depth = 1; while (!Eof() && depth > 0) { if (Cur() == '{') depth++; else if (Cur() == '}') depth--; Next(); } return _src.Substring(start, Math.Max(0, _pos - start - 1)); }
                int s = _pos; while (!Eof() && Cur() != ';') Next(); if (!Eof()) Next(); return _src.Substring(s, _pos - s);
            }

            private void ExecuteString(string code)
            { var child = new JsMiniRunner(_e); child._globals.Clear(); foreach (var kv in _globals) child._globals[kv.Key] = kv.Value; child._src = code ?? ""; child._pos = 0; child._len = child._src.Length; child.SkipWs(); while (!child.Eof()) { child.ParseStatement(); child.SkipWs(); } foreach (var kv in child._globals) _globals[kv.Key] = kv.Value; }

            private JsVal ParseAssignment()
            {
                int save = _pos;

                try
                {
                    SkipWs();
                    int patternStart = _pos;
                    char cur = Cur();
                    if (cur == '{' || cur == '[')
                    {
                        try
                        {
                            var pattern = ParseBindingPattern();
                            SkipWs();
                            if (Match("="))
                            {
                                var rhs = ParseAssignment();
                                AssignBinding(pattern, rhs, rhs != null);
                                return rhs;
                            }
                            _pos = patternStart;
                        }
                        catch
                        {
                            _pos = patternStart;
                        }
                    }

                    _pos = save;
                }
                catch
                {
                    _pos = save;
                }

                try
                {
                    // IDENT = expr (simple)
                    var name = ParseIdent(); if (Match("=")) { var v = ParseAssignment(); SetVarAssign(name, v); return v; }
                    // Deep lvalue chain: base.prop[...].prop
                    _pos = save;
                    string baseNm; List<string> chain;
                    if (TryParseDeepLValue(out baseNm, out chain))
                    {
                        if (Match("="))
                        {
                            var rhs = ParseAssignment();
                            JsVal cur; TryGetVar(baseNm, out cur);
                            for (int i = 0; i < chain.Count - 1; i++) cur = GetMember(cur, chain[i]);
                            var prop = chain[chain.Count - 1];
                            // Host: location.href
                            if (cur != null && cur.Obj is HostLocation && string.Equals(prop, "href", StringComparison.Ordinal))
                            { try { var s = ToStr(rhs); var abs = Resolve(_e._ctx?.BaseUri, s); if (abs != null) _e._host.Navigate(abs); else _e.Navigate(s); } catch { /* swallow */ } return rhs; }
                            // Host: element.innerText / textContent
                            var hel = cur != null ? cur.Obj as HostElement : null;
                            if (hel != null && string.Equals(prop, "innerText", StringComparison.Ordinal))
                            { try { if (!string.IsNullOrEmpty(hel.Id)) { var doc = new JsDocument(_e, _e._domRoot); var el = doc.getElementById(hel.Id) as JsDomElement; if (el != null) el.innerText = ToStr(rhs); } else if (hel.Node != null) { hel.Node.RemoveAllChildren(); var t = new LiteElement("#text"); t.Text = ToStr(rhs); hel.Node.Append(t); _e.RequestRepaint(); } } catch { /* swallow */ } return rhs; }
                            if (hel != null && string.Equals(prop, "textContent", StringComparison.Ordinal))
                            { try { if (!string.IsNullOrEmpty(hel.Id)) { var doc = new JsDocument(_e, _e._domRoot); var el = doc.getElementById(hel.Id) as JsDomElement; if (el != null) el.innerText = ToStr(rhs); } else if (hel.Node != null) { hel.Node.RemoveAllChildren(); var t = new LiteElement("#text"); t.Text = ToStr(rhs); hel.Node.Append(t); _e.RequestRepaint(); } } catch { /* swallow */ } return rhs; }
                            // JS-0 DOM bridge element.innerText / textContent / id / value / checked
                            var jde = cur != null ? cur.Obj as JsDomElement : null;
                            if (jde != null)
                            {
                                if (string.Equals(prop, "innerText", StringComparison.Ordinal))
                                { try { jde.innerText = ToStr(rhs); } catch { /* swallow */ } return rhs; }
                                if (string.Equals(prop, "textContent", StringComparison.Ordinal))
                                { try { jde.innerText = ToStr(rhs); } catch { /* swallow */ } return rhs; }
                                if (string.Equals(prop, "id", StringComparison.Ordinal))
                                { try { jde.id = ToStr(rhs); } catch { /* swallow */ } return rhs; }
                                if (string.Equals(prop, "value", StringComparison.Ordinal))
                                { try { jde.value = ToStr(rhs); } catch { /* swallow */ } return rhs; }
                                if (string.Equals(prop, "checked", StringComparison.Ordinal))
                                {
                                    try
                                    {
                                        var v = Convert.ToBoolean(rhs);
                                        // reflect as attribute for simple renderer mapping
                                        jde.setAttribute("checked", v ? "checked" : null);
                                    }
                                    catch { /* swallow */ }
                                    return rhs;
                                }
                            }
                            // document.title setter
                            var hdoc = cur != null ? cur.Obj as HostDocument : null;
                            if (hdoc != null && string.Equals(prop, "title", StringComparison.Ordinal)) { try { _e._docTitle = ToStr(rhs); } catch { /* swallow */ } return rhs; }
                            var hev = cur != null ? cur.Obj as HostEvent : null;
                            if (hev != null)
                            {
                                if (string.Equals(prop, "cancelBubble", StringComparison.Ordinal))
                                {
                                    try
                                    {
                                        var stop = rhs != null ? rhs.Truthy() : false;
                                        hev.PropagationStopped = stop;
                                        if (stop) _e._stopPropagationRequested = true;
                                    }
                                    catch { /* swallow */ }
                                    return rhs;
                                }
                                if (string.Equals(prop, "returnValue", StringComparison.Ordinal))
                                {
                                    try
                                    {
                                        var keepDefault = rhs != null ? rhs.Truthy() : false;
                                        hev.DefaultPrevented = !keepDefault;
                                        if (!keepDefault) _e._preventDefaultRequested = true;
                                    }
                                    catch { /* swallow */ }
                                    return rhs;
                                }
                            }
                            // dataset write: dataset.foo = 'bar'
                            var hds = cur != null ? cur.Obj as HostDataset : null;
                            if (hds != null)
                            { try { if (hds.Node != null && !string.IsNullOrEmpty(prop)) hds.Node.SetAttribute("data-" + prop, ToStr(rhs)); } catch { /* swallow */ } return rhs; }
                            // Host: style.prop
                            var hst = cur != null ? cur.Obj as HostStyle : null; if (hst != null)
                            { try { var vstr = ToStr(rhs); if (!string.IsNullOrEmpty(hst.Id)) _e.TryUpdateInlineStyle(hst.Id, prop, vstr); else if (hst.Node != null) { var style = hst.Node.GetAttribute("style") ?? ""; var dict = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); foreach (var part in style.Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries)) { var kv=part.Split(new[]{':'},2); if (kv.Length==2) dict[kv[0].Trim()] = kv[1].Trim(); } dict[prop]=vstr??""; var sb=new StringBuilder(); bool first=true; foreach(var kv in dict){ if(!first) sb.Append(';'); first=false; sb.Append(kv.Key).Append(':').Append(kv.Value);} hst.Node.SetAttribute("style", sb.ToString()); } } catch { /* swallow */ } return rhs; }
                            // User map fallback
                            JsVal root; if (!TryGetVar(baseNm, out root) || !(root.Obj is Dictionary<string, JsVal>)) root = new JsVal { Obj = new Dictionary<string, JsVal>(StringComparer.Ordinal) };
                            var map = root.Obj as Dictionary<string, JsVal>; map[prop] = rhs; SetVarAssign(baseNm, root); return rhs;
                        }
                        _pos = save;
                    }
                    _pos = save; return ParseNullish();
                }
                catch { _pos = save; return ParseNullish(); }
            }

            private JsVal ParseNullish()
            {
                var v = ParseOr();
                while (true)
                {
                    if (Match("??"))
                    {
                        var r = ParseOr();
                        if (IsNullish(v)) v = r;
                        continue;
                    }
                    break;
                }
                return v;
            }

            private bool TryParseDeepLValue(out string baseName, out List<string> keys)
            {
                baseName = null; keys = new List<string>(); int p = _pos;
                try
                {
                    var nm = ParseIdent(); if (string.IsNullOrEmpty(nm)) { _pos = p; return false; }
                    baseName = nm;
                    while (true)
                    {
                        SkipWs(); if (Match(".")) { var k = ParseIdent(); keys.Add(k); continue; }
                        if (Match("[")) { var kv = ParseExpression(); Expect("]"); keys.Add(ToStr(kv)); continue; }
                        break;
                    }
                    if (keys.Count == 0) { _pos = p; return false; }
                    return true;
                }
                catch { _pos = p; baseName = null; keys = null; return false; }
            }

            private bool TryParseObjPropLValue(out string objName, out string prop)
            {
                objName = null; prop = null; int p = _pos; try
                {
                    SkipWs(); int start = _pos; var id = ParseIdent(); if (string.IsNullOrEmpty(id)) { _pos = p; return false; }
                    SkipWs(); if (Match(".")) { var key = ParseIdent(); if (string.IsNullOrEmpty(key)) { _pos = p; return false; } objName = id; prop = key; return true; }
                    if (Match("[")) { var s = ParseExpression(); Expect("]"); objName = id; prop = ToStr(s); return true; }
                    _pos = p; return false;
                }
                catch { _pos = p; return false; }
            }

            private JsVal ParseOr() { var v = ParseAnd(); while (true) { if (Match("||")) { var r = ParseAnd(); v = JsVal.FromBool(v.Truthy() || r.Truthy()); } else break; } return v; }
            private JsVal ParseAnd() { var v = ParseEquality(); while (true) { if (Match("&&")) { var r = ParseEquality(); v = JsVal.FromBool(v.Truthy() && r.Truthy()); } else break; } return v; }
            private JsVal ParseEquality() { var v = ParseRel(); while (true) { if (Match("===") || Match("==")) { var r = ParseRel(); v = JsVal.FromBool(ToStr(v) == ToStr(r)); } else if (Match("!==") || Match("!=")) { var r2 = ParseRel(); v = JsVal.FromBool(ToStr(v) != ToStr(r2)); } else break; } return v; }
            private JsVal ParseRel() { var v = ParseAdd(); while (true) { if (Match("<=")) { var r = ParseAdd(); v = JsVal.FromBool(ToNum(v) <= ToNum(r)); } else if (Match(">=")) { var r2 = ParseAdd(); v = JsVal.FromBool(ToNum(v) >= ToNum(r2)); } else if (Match("<")) { var r3 = ParseAdd(); v = JsVal.FromBool(ToNum(v) < ToNum(r3)); } else if (Match(">")) { var r4 = ParseAdd(); v = JsVal.FromBool(ToNum(v) > ToNum(r4)); } else if (Match("instanceof")) { var ctor = ParseAdd(); v = JsVal.FromBool(InstanceOf(v, ctor)); } else break; } return v; }
            private JsVal ParseAdd() { var v = ParseMul(); while (true) { if (Match("+")) { var r = ParseMul(); if (v.Str != null || r.Str != null) v = JsVal.FromStr(ToStr(v) + ToStr(r)); else v = JsVal.FromNum(ToNum(v) + ToNum(r)); } else if (Match("-")) { var r2 = ParseMul(); v = JsVal.FromNum(ToNum(v) - ToNum(r2)); } else break; } return v; }
            private JsVal ParseMul() { var v = ParseUnary(); while (true) { if (Match("*")) { var r = ParseUnary(); v = JsVal.FromNum(ToNum(v) * ToNum(r)); } else if (Match("/")) { var r2 = ParseUnary(); var denom = ToNum(r2); v = JsVal.FromNum(denom == 0 ? 0 : ToNum(v) / denom); } else if (Match("%")) { var r3 = ParseUnary(); v = JsVal.FromNum(ToNum(v) % ToNum(r3)); } else break; } return v; }
            private JsVal ParseUnary()
            {
                if (Match("typeof")) { var tv = ParseUnary(); return JsVal.FromStr(TypeOf(tv)); }
                if (Match("!")) { var v = ParseUnary(); return JsVal.FromBool(!v.Truthy()); }
                if (Match("-")) { var v2 = ParseUnary(); return JsVal.FromNum(-ToNum(v2)); }
                if (Match("+")) { var v3 = ParseUnary(); return JsVal.FromNum(ToNum(v3)); }
                if (Match("new"))
                {
                    SkipWs();
                    int ctorStart = _pos;
                    string ctorBase;
                    List<string> ctorChain;
                    if (!TryParseDeepLValue(out ctorBase, out ctorChain))
                    {
                        _pos = ctorStart;
                        ctorBase = ParseIdent();
                        ctorChain = new List<string>();
                    }

                    if (string.Equals(ctorBase, "Promise", StringComparison.Ordinal) && (ctorChain == null || ctorChain.Count == 0))
                    {
                        Expect("("); JsVal exec = null; if (!Match(")")) { exec = ParseExpression(); Expect(")"); }
                        var p = new HostPromise { E = _e, State = 0, Value = JsVal.Null() };
                        var resolve = new JsFuncDef { Body = null }; resolve.Params.Add("v");
                        var reject = new JsFuncDef { Body = null }; reject.Params.Add("e");
                        var fdef = exec != null ? exec.Obj as JsFuncDef : null;
                        if (fdef != null)
                        {
                            try
                            {
                                var runner = new JsMiniRunner(_e);
                                var resHF = new JsVal { Obj = new HostFunc(a => { var v = a.Count > 0 ? a[0] : JsVal.Null(); p.State = 1; p.Value = v; _e.EnqueueMicrotask(() => { try { ProcessPromiseHandlers(p); } catch { /* swallow */ } }); return JsVal.Null(); }) };
                                var rejHF = new JsVal { Obj = new HostFunc(a => { var v = a.Count > 0 ? a[0] : JsVal.Null(); p.State = -1; p.Value = v; _e.EnqueueMicrotask(() => { try { ProcessPromiseHandlers(p); } catch { /* swallow */ } }); return JsVal.Null(); }) };
                                var child = new JsMiniRunner(_e);
                                child.InvokeFunction(fdef, new List<JsVal> { resHF, rejHF });
                            }
                            catch { /* swallow */ }
                        }
                        return new JsVal { Obj = p };
                    }
                    else
                    {
                        var args = new List<JsVal>();
                        SkipWs();
                        if (Match("("))
                        {
                            if (!Match(")"))
                            {
                                do { args.Add(ParseExpression()); } while (Match(","));
                                Expect(")");
                            }
                        }

                        JsVal ctorVal = null;
                        if (!string.IsNullOrEmpty(ctorBase)) TryGetVar(ctorBase, out ctorVal);
                        if (ctorChain != null)
                        {
                            for (int i = 0; i < ctorChain.Count; i++)
                            {
                                ctorVal = GetMember(ctorVal, ctorChain[i]);
                            }
                        }

                        return ConstructInstance(ctorVal, args);
                    }
                }
                return ParsePrimary();
            }
            private JsVal ParsePrimary()
            {
                SkipWs(); char c = Cur();
                JsVal v = null;
                if (c == '\'' || c == '"') v = JsVal.FromStr(ParseString());
                else if (char.IsDigit(c) || c == '-') v = ParseNumber();
                else if (Match("true")) v = JsVal.FromBool(true);
                else if (Match("false")) v = JsVal.FromBool(false);
                else if (Match("null")) v = JsVal.Null();
                else if (Match("{")) { v = ParseObjectLiteral(); }
                else if (Match("[")) { v = ParseArrayLiteral(); }
                else if (TryParseArrowFunction(out v)) { }
                else if (Match("(")) { v = ParseExpression(); Expect(")"); }
                else if (Match("function")) { v = ParseFunctionExpression(); }
                else { var id = ParseIdent(); JsVal tmp; if (TryGetVar(id, out tmp)) v = tmp; else v = JsVal.Null(); }

                while (true)
                {
                    if (Match("?."))
                    {
                        if (Match("("))
                        {
                            var optArgs = ParseArguments();
                            if (IsNullish(v)) v = JsVal.Null(); else v = Invoke(v, optArgs);
                            continue;
                        }
                        if (Match("["))
                        {
                            var optKey = ParseExpression();
                            Expect("]");
                            if (IsNullish(v)) v = JsVal.Null(); else v = GetIndex(v, optKey);
                            continue;
                        }
                        var optName = ParseIdent();
                        if (IsNullish(v)) v = JsVal.Null(); else v = GetMember(v, optName);
                        continue;
                    }
                    if (Match(".")) { var name = ParseIdent(); v = GetMember(v, name); continue; }
                    if (Match("[")) { var key = ParseExpression(); Expect("]"); v = GetIndex(v, key); continue; }
                    if (Match("(")) { var args = ParseArguments(); v = Invoke(v, args); continue; }
                    break;
                }
                return v ?? JsVal.Null();
            }

            private bool TryParseArrowFunction(out JsVal func)
            {
                func = null; int p0 = _pos; try
                {
                    List<JsFuncParam> parameters;
                    if (Match("("))
                    {
                        parameters = ParseFunctionParameterList(')');
                    }
                    else
                    {
                        // single param form (identifier only)
                        var single = ParseIdent();
                        parameters = new List<JsFuncParam> { CreateIdentifierParam(single) };
                    }

                    SkipWs(); if (!Match("=>")) { _pos = p0; return false; }
                    SkipWs();
                    var def = new JsFuncDef();
                    PopulateFunctionParameters(def, parameters);
                    if (Match("{"))
                    {
                        int start = _pos; int depth = 1; while (!Eof() && depth > 0) { if (Cur() == '{') depth++; else if (Cur() == '}') depth--; Next(); }
                        def.Body = _src.Substring(start, Math.Max(0, _pos - start - 1));
                    }
                    else
                    {
                        int es = _pos; var _ = ParseExpression(); int ee = _pos; def.Expr = _src.Substring(es, ee - es);
                    }
                    func = new JsVal { Obj = def }; return true;
                }
                catch { _pos = p0; func = null; return false; }
            }

            private JsVal ParseFunctionExpression()
            {
                Expect("(");
                var parameters = ParseFunctionParameterList(')');
                Expect("{"); int bodyStart = _pos; int depth = 1; while (!Eof() && depth > 0) { if (Cur() == '{') depth++; else if (Cur() == '}') depth--; Next(); }
                string body = _src.Substring(bodyStart, Math.Max(0, _pos - bodyStart - 1));
                var def = new JsFuncDef { Body = body };
                PopulateFunctionParameters(def, parameters);
                return new JsVal { Obj = def };
            }

            private JsVal ParseObjectLiteral()
            {
                var dict = new Dictionary<string, JsVal>(StringComparer.Ordinal);
                SkipWs(); if (Match("}")) return new JsVal { Obj = dict };
                while (true) { string key; SkipWs(); if (Cur() == '\'' || Cur() == '"') key = ParseString(); else key = ParseIdent(); Expect(":"); var val = ParseExpression(); dict[key] = val; if (Match("}")) break; Expect(","); }
                return new JsVal { Obj = dict };
            }

            private JsVal ParseArrayLiteral()
            {
                var list = new List<JsVal>(); SkipWs(); if (Match("]")) return new JsVal { Obj = list }; while (true) { var v = ParseExpression(); list.Add(v); if (Match("]")) break; Expect(","); } return new JsVal { Obj = list };
            }

            private List<JsVal> ParseArguments()
            { var args = new List<JsVal>(); SkipWs(); if (Match(")")) return args; do { var a = ParseExpression(); args.Add(a); } while (Match(",")); Expect(")"); return args; }

            // Helper to convert .NET collections to a JavaScript array (NativeList)
            private static JSValue ToJsArray(IEnumerable<object> items)
            {
                return Context.CurrentGlobalContext.ProxyValue(items);
            }

            private JsVal GetMember(JsVal obj, string name)
            {
                if (obj == null) return JsVal.Null();
                var map = obj.Obj as Dictionary<string, JsVal>; if (map != null) { JsVal v; return map.TryGetValue(name, out v) ? v : JsVal.Null(); }
                var arr = obj.Obj as List<JsVal>; if (arr != null) { if (name == "length") return JsVal.FromNum(arr.Count); return JsVal.Null(); }

                // Host objects
                if (obj.Obj is HostDocument)
                {
                    if (name == "readyState") { try { return JsVal.FromStr("complete"); } catch { return JsVal.FromStr("complete"); } }
                    if (name == "title") { try { return JsVal.FromStr(_e._docTitle ?? ""); } catch { return JsVal.FromStr(""); } }
                    if (name == "documentElement")
                    {
                        try
                        {
                            double w = 0, h = 0; try { var b = Windows.UI.Xaml.Window.Current.Bounds; w = b.Width; h = b.Height; } catch { /* swallow */ }
                            var docElMap = new Dictionary<string, JsVal>(StringComparer.Ordinal)
                            {
                                { "clientWidth", JsVal.FromNum(w) },
                                { "clientHeight", JsVal.FromNum(h) }
                            };
                            return new JsVal { Obj = docElMap };
                        }
                        catch { return new JsVal { Obj = new Dictionary<string, JsVal>(StringComparer.Ordinal) }; } 
                    }
                    if (name == "cookie")
                    {
                        try { var s = _e.GetCookieString(_e._ctx != null ? _e._ctx.BaseUri : null); return JsVal.FromStr(s ?? ""); } catch { return JsVal.FromStr(""); }
                    }
                    if (name == "location") return new JsVal { Obj = new HostLocation(_e) };
                    if (name == "getElementById")
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            string id = ToStr(args.Count > 0 ? args[0] : JsVal.Null());
                            try
                            {
                                var doc = new JsDocument(_e, _e._domRoot);
                                var jel = doc.getElementById(id) as JsDomElement;
                                LiteElement node = null; try { if (jel != null) node = jel._node; } catch { /* swallow */ }
                                return new JsVal { Obj = new HostElement(_e, id) { Node = node } };
                            }
                            catch { return new JsVal { Obj = new HostElement(_e, id) }; }
                        }) };
                    if (name == "querySelector")
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            try
                            {
                                string sel = ToStr(args.Count > 0 ? args[0] : JsVal.Null());
                                var doc = new JsDocument(_e, _e._domRoot);
                                var jsel = doc.querySelector(sel) as JsDomElement;
                                if (jsel != null)
                                {
                                    var idv = jsel.getAttribute("id");
                                    return new JsVal { Obj = new HostElement(_e, idv) };
                                }
                            }
                            catch { /* swallow */ }
                            return JsVal.Null();
                        }) };
                    if (name == "querySelectorAll")
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            try
                            {
                                string sel = ToStr(args.Count > 0 ? args[0] : JsVal.Null());
                                var doc = new JsDocument(_e, _e._domRoot);
                                var nodeArray = doc.querySelectorAll(sel) as object[];
                                var results = new List<object>();
                                if (nodeArray != null)
                                {
                                    for (int i = 0; i < nodeArray.Length; i++)
                                    {
                                        var el = nodeArray[i] as JsDomElement;
                                        if (el != null)
                                        {
                                            var idv = el.getAttribute("id");
                                            results.Add(new HostElement(_e, idv));
                                        }
                                    }
                                }
                                return new JsVal { Obj = ToJsArray(results) };
                            }
                            catch { return new JsVal { Obj = ToJsArray(new List<object>()) }; }
                        }) };
                     // Added support for getElementsByTagName
                     if (name == "getElementsByTagName")
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            try
                            {
                                string tag = ToStr(args.Count > 0 ? args[0] : JsVal.Null());
                                var doc = new JsDocument(_e, _e._domRoot);
                                var arr = doc.getElementsByTagName(tag) as object[];
                                var list = new List<object>();
                                if (arr != null)
                                {
                                    for (int i = 0; i < arr.Length; i++)
                                    {
                                        var el = arr[i] as JsDomElement;
                                        if (el != null)
                                        {
                                            var idv = el.getAttribute("id");
                                            list.Add(new HostElement(_e, idv));
                                        }
                                    }
                                }
                                return new JsVal { Obj = ToJsArray(list) };
                            }
                            catch { return new JsVal { Obj = ToJsArray(new List<object>()) }; }
                        }) };
                     // Added support for getElementsByClassName
                     if (name == "getElementsByClassName")
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            try
                            {
                                string className = ToStr(args.Count > 0 ? args[0] : JsVal.Null());
                                 var doc = new JsDocument(_e, _e._domRoot);
                                 var arr = doc.getElementsByClassName(className) as object[];
                                var list = new List<object>();
                                if (arr != null)
                                {
                                    for (int i = 0; i < arr.Length; i++)
                                    {
                                        var el = arr[i] as JsDomElement;
                                        if (el != null)
                                        {
                                            var idv = el.getAttribute("id");
                                            list.Add(new HostElement(_e, idv));
                                        }
                                    }
                                }
                                return new JsVal { Obj = ToJsArray(list) };
                            }
                            catch { return new JsVal { Obj = ToJsArray(new List<object>()) }; }
                        }) };
                     if (name == "createElement")
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            try
                            {
                                string tag = ToStr(args.Count > 0 ? args[0] : JsVal.Null());
                                var doc = new JsDocument(_e, _e._domRoot);
                                var el = doc.createElement(tag);
                                return new JsVal { Obj = el };
                            }
                            catch { return JsVal.Null(); }
                        }) };
                    if (name == "createTextNode")
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            try
                            {
                                string text = ToStr(args.Count > 0 ? args[0] : JsVal.Null());
                                var doc = new JsDocument(_e, _e._domRoot);
                                var t = doc.createTextNode(text);
                                return new JsVal { Obj = t };
                            }
                            catch { return JsVal.Null(); }
                        }) };
                    if (name == "appendChild")
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            try
                            {
                                var doc = new JsDocument(_e, _e._domRoot);
                                var child = args.Count > 0 ? args[0].Obj as JsDomNodeBase : null;
                                if (child != null) doc.appendChild(child);
                            }
                            catch { /* swallow */ }
                            return JsVal.Null();
                        }) };
                    if (name == "removeChild")
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            try
                            {
                                var b = new JsDocument(_e, _e._domRoot).body;
                                var child = args.Count > 0 ? args[0].Obj as JsDomNodeBase : null;
                                if (child != null)
                                {
                                    try { b._node.Children.Remove(child._node); } catch { /* swallow */ }
                                    _e.RequestRepaint();
                                }
                            }
                            catch { /* swallow */ }
                            return JsVal.Null();
                        }) };
                    if (name == "body")
                    {
                        try { return new JsVal { Obj = new JsDocument(_e, _e._domRoot).body }; } catch { return JsVal.Null(); }
                    }
                    return JsVal.Null();
                }
                // JS-0 DOM objects (new document.createElement, etc.)
                if (obj.Obj is HostEvent)
                {
                    var ev = (HostEvent)obj.Obj;
                    if (name == "type") return JsVal.FromStr(ev.Type ?? "");
                    if (name == "target" || name == "currentTarget")
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(ev.TargetId))
                            {
                                var doc = new JsDocument(_e, _e._domRoot);
                                var el = doc.getElementById(ev.TargetId) as JsDomElement;
                                if (el != null) return new JsVal { Obj = el };
                            }
                        }
                        catch { /* swallow */ }
                        return JsVal.Null();
                    }
                    if (name == "preventDefault")
                        return new JsVal { Obj = new HostFunc(args => { try { ev.DefaultPrevented = true; _e._preventDefaultRequested = true; } catch { /* swallow */ } return JsVal.Null(); }) };
                    if (name == "stopPropagation")
                        return new JsVal { Obj = new HostFunc(args => { try { ev.PropagationStopped = true; _e._stopPropagationRequested = true; } catch { /* swallow */ } return JsVal.Null(); }) };
                    if (name == "defaultPrevented") return JsVal.FromBool(ev.DefaultPrevented);
                    if (name == "cancelBubble") return JsVal.FromBool(ev.PropagationStopped);
                    if (name == "returnValue") return JsVal.FromBool(!ev.DefaultPrevented);
                    return JsVal.Null();
                }
                // JS-0 DOM objects (new document.createElement, etc.)
                if (obj.Obj is JsDomElement)
                {
                    var jde = (JsDomElement)obj.Obj;
                    if (name == "appendChild")
                        return new JsVal { Obj = new HostFunc(args => { try { var child = args.Count > 0 ? args[0].Obj as JsDomNodeBase : null; if (child != null) jde.appendChild(child); } catch { /* swallow */ } return JsVal.Null(); }) };
                    if (name == "removeChild")
                        return new JsVal { Obj = new HostFunc(args => { try { var child = args.Count > 0 ? args[0].Obj as JsDomNodeBase : null; if (child != null) jde.removeChild(child); } catch { /* swallow */ } return JsVal.Null(); }) };
                    if (name == "setAttribute")
                        return new JsVal { Obj = new HostFunc(args => { try { string an = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); string av = ToStr(args.Count > 1 ? args[1] : JsVal.Null()); jde.setAttribute(an, av); } catch { /* swallow */ } return JsVal.Null(); }) };
                    if (name == "getAttribute")
                        return new JsVal { Obj = new HostFunc(args => { try { string an = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); var v = jde.getAttribute(an); return v == null ? JsVal.Null() : JsVal.FromStr(v); } catch { /* swallow */ } return JsVal.Null(); }) };
                    if (name == "innerText")
                    {
                        try { return JsVal.FromStr(jde.innerText ?? ""); } catch { return JsVal.Null(); }
                    }
                    if (name == "style") return new JsVal { Obj = new HostStyle(_e, null) { Node = jde._node } };
                    if (name == "classList") return new JsVal { Obj = new HostClassList(_e, null) { Node = jde._node } };
                    if (name == "dataset") return new JsVal { Obj = new HostDataset(_e, jde._node) };
                    if (name == "dataset")
                    {
                        try
                        {
                            var maps = new Dictionary<string, JsVal>(StringComparer.OrdinalIgnoreCase);
                            var n = jde._node; if (n != null && n.Attr != null)
                            {
                                foreach (var kv in n.Attr)
                                    if (kv.Key != null && kv.Key.StartsWith("data-", StringComparison.OrdinalIgnoreCase))
                                        maps[kv.Key.Substring(5)] = JsVal.FromStr(kv.Value);
                            }
                            return new JsVal { Obj = maps };
                        }
                        catch { return new JsVal { Obj = new Dictionary<string, JsVal>() }; }
                    }
                    if (name == "getBoundingClientRect")
                    {
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            try
                            {
                                double x=0,y=0,w=0,h=0;
                                if (!TryGetVisualRect(jde._node, out x, out y, out w, out h))
                                {
                                    // Fallback: probe width/height from attributes/style
                                    var n = jde._node; double tmp;
                                    if (n != null && n.Attr != null)
                                    {
                                        string sw, sh;
                                        if (n.Attr.TryGetValue("width", out sw) && double.TryParse((sw ?? "").Replace("px",""), out tmp)) w = tmp;
                                        if (n.Attr.TryGetValue("height", out sh) && double.TryParse((sh ?? "").Replace("px",""), out tmp)) h = tmp;
                                    }
                                }
                                var rect = new Dictionary<string, JsVal>(StringComparer.Ordinal)
                                {
                                    {"x",JsVal.FromNum(x)},{"y",JsVal.FromNum(y)},{"left",JsVal.FromNum(x)},{"top",JsVal.FromNum(y)},
                                    {"width",JsVal.FromNum(w)},{"height",JsVal.FromNum(h)},{"right",JsVal.FromNum(x+w)},{"bottom",JsVal.FromNum(y+h)}
                                };
                                return new JsVal { Obj = rect };
                            }
                            catch { var r=new Dictionary<string,JsVal>(StringComparer.Ordinal){ {"x",JsVal.FromNum(0)},{"y",JsVal.FromNum(0)},{"left",JsVal.FromNum(0)},{"top",JsVal.FromNum(0)},{"width",JsVal.FromNum(0)},{"height",JsVal.FromNum(0)},{"right",JsVal.FromNum(0)},{"bottom",JsVal.FromNum(0)} }; return new JsVal{Obj=r}; }
                        }) };
                    }
                    if (name == "querySelector") return new JsVal { Obj = new HostFunc(args => { try { string s = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); var r = jde.querySelector(s) as JsDomElement; return r == null ? JsVal.Null() : new JsVal { Obj = r }; } catch { /* swallow */ } return JsVal.Null(); }) };
                    if (name == "querySelectorAll") return new JsVal { Obj = new HostFunc(args => { try { string s = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); var arrs = jde.querySelectorAll(s) as object[]; var list = new List<object>(); if (arrs != null) { for (int i = 0; i < arrs.Length; i++) { var el = arrs[i] as JsDomElement; if (el != null) list.Add(el); } } return new JsVal { Obj = ToJsArray(list) }; } catch { /* swallow */ } return new JsVal { Obj = ToJsArray(new List<object>()) }; }) };
                    if (name == "insertAdjacentHTML") return new JsVal { Obj = new HostFunc(args => { try { var pos = ToStr(args.Count>0?args[0]:JsVal.Null()); var html = ToStr(args.Count>1?args[1]:JsVal.Null()); jde.insertAdjacentHTML(pos, html); } catch { /* swallow */ } return JsVal.Null(); }) };
                    return JsVal.Null();
                }
                if (obj.Obj is HostElement)
                {
                    var he = (HostElement)obj.Obj;
                    if (name == "style") return new JsVal { Obj = new HostStyle(_e, he.Id) { Node = he.Node } };
                    if (name == "classList") return new JsVal { Obj = new HostClassList(_e, he.Id) { Node = he.Node } };
                    if (name == "dataset") return new JsVal { Obj = new HostDataset(_e, he.Node) };
                    if (name == "addEventListener")
                    {
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            try
                            {
                                string evt = ToStr(args.Count > 0 ? args[0] : JsVal.Null());
                                // ensure element has id
                                if (string.IsNullOrEmpty(he.Id) && he.Node != null)
                                {
                                    var genId = "auto_" + (_e._cbCounter++).ToString();
                                    he.Node.SetAttribute("id", genId); he.Id = genId;
                                }
                                if (!string.IsNullOrEmpty(he.Id))
                                {
                                    var v = args.Count > 1 ? args[1] : JsVal.Null();
                                    var def = v != null ? v.Obj as JsFuncDef : null;
                                    if (def != null)
                                    {
                                        var gen = "__cb" + (_e._cbCounter++).ToString();
                                        try { _e._userFunctionsEx[gen] = def; } catch { /* swallow */ }
                                        _e.RegisterElementListener(he.Id, evt, gen);
                                    }
                                    else
                                    {
                                        var nameStr = ToStr(v);
                                        if (!string.IsNullOrWhiteSpace(nameStr)) _e.RegisterElementListener(he.Id, evt, nameStr);
                                    }
                                }
                            }
                            catch { /* swallow */ }
                            return JsVal.Null();
                        }) };
                    }
                    if (name == "removeEventListener")
                    {
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            try
                            {
                                string evt = ToStr(args.Count > 0 ? args[0] : JsVal.Null());
                                if (!string.IsNullOrEmpty(he.Id))
                                {
                                    var v = args.Count > 1 ? args[1] : JsVal.Null();
                                    var nameStr = ToStr(v);
                                    if (!string.IsNullOrWhiteSpace(nameStr)) _e.RemoveElementListener(he.Id, evt, nameStr);
                                    // If a function object is passed, we do not have a stable mapping -> no-op.
                                }
                            }
                            catch { /* swallow */ }
                            return JsVal.Null();
                        }) };
                    }
                    if (name == "dispatchEvent")
                    {
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            try
                            {
                                string evt = null;
                                if (args.Count > 0)
                                {
                                    var a0 = args[0];
                                    var maps = a0 != null ? a0.Obj as Dictionary<string, JsVal> : null;
                                    if (maps != null)
                                    {
                                        JsVal tv; if (maps.TryGetValue("type", out tv)) evt = ToStr(tv);
                                    }
                                    if (string.IsNullOrWhiteSpace(evt)) evt = ToStr(a0);
                                }
                                if (!string.IsNullOrWhiteSpace(evt) && !string.IsNullOrWhiteSpace(he.Id))
                                    _e.RaiseElementEventById(he.Id, evt);
                            }
                            catch { /* swallow */ }
                            return JsVal.Null();
                        }) };
                    }
                    if (name == "setAttribute") return new JsVal { Obj = new HostFunc(args => { string an = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); string av = ToStr(args.Count > 1 ? args[1] : JsVal.Null()); try { var doc = new JsDocument(_e, _e._domRoot); var el = doc.getElementById(he.Id) as JsDomElement; if (el != null) el.setAttribute(an, av); } catch { /* swallow */ } return JsVal.Null(); }) };
                    if (name == "getAttribute") return new JsVal { Obj = new HostFunc(args => { string an = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); try { var doc = new JsDocument(_e, _e._domRoot); var el = doc.getElementById(he.Id) as JsDomElement; if (el != null) { var v = el.getAttribute(an); return v == null ? JsVal.Null() : JsVal.FromStr(v); } } catch { /* swallow */ } return JsVal.Null(); }) };
                    if (name == "innerText") { try { var doc = new JsDocument(_e, _e._domRoot); var el = doc.getElementById(he.Id) as JsDomElement; if (el != null) return JsVal.FromStr(el.innerText ?? ""); } catch { /* swallow */ } return JsVal.Null(); }
                    if (name == "removeAttribute") return new JsVal { Obj = new HostFunc(args => { string an = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); try { var doc = new JsDocument(_e, _e._domRoot); var el = doc.getElementById(he.Id) as JsDomElement; if (el != null) el.setAttribute(an, null); } catch { /* swallow */ } return JsVal.Null(); }) };
                    if (name == "hasAttribute") return new JsVal { Obj = new HostFunc(args => { string an = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); try { var doc = new JsDocument(_e, _e._domRoot); var el = doc.getElementById(he.Id) as JsDomElement; if (el != null) { var v = el.getAttribute(an); return JsVal.FromBool(!string.IsNullOrEmpty(v)); } } catch { /* swallow */ } return JsVal.FromBool(false); }) };
                    if (name == "children") { try { var doc = new JsDocument(_e, _e._domRoot); var el = doc.getElementById(he.Id) as JsDomElement; if (el != null) { var kids = el.children as object[]; var list = new List<object>(); if (kids != null) { foreach (var k in kids) { var kid = k as JsDomElement; if (kid != null) { var kidId = kid.getAttribute("id"); list.Add(new HostElement(_e, kidId ?? "")); } } } return new JsVal { Obj = ToJsArray(list) }; } } catch { /* swallow */ } return new JsVal { Obj = ToJsArray(new List<object>()) }; }
                    if (name == "childNodes") { try { var doc = new JsDocument(_e, _e._domRoot); var el = doc.getElementById(he.Id) as JsDomElement; if (el != null) { var nodes = el.childNodes as object[]; var list = new List<object>(); if (nodes != null) { foreach (var n in nodes) { var node = n as JsDomElement; if (node != null) { var nodeId = node.getAttribute("id"); list.Add(new HostElement(_e, nodeId ?? "")); } } } return new JsVal { Obj = ToJsArray(list) }; } } catch { /* swallow */ } return new JsVal { Obj = ToJsArray(new List<object>()) }; }
                    if (name == "getElementsByTagName") return new JsVal { Obj = new HostFunc(args => { try { string tag = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); var jde = new JsDomElement(_e, _e._domRoot?.FindById(he.Id) ?? _e._domRoot); var arr = jde.getElementsByTagName(tag) as object[]; var list = new List<object>(); if (arr != null) { for (int i = 0; i < arr.Length; i++) { var el = arr[i] as JsDomElement; if (el != null) { var idv = el.getAttribute("id"); list.Add(new HostElement(_e, idv ?? "")); } } } return new JsVal { Obj = ToJsArray(list) }; } catch { /* swallow */ } return new JsVal { Obj = ToJsArray(new List<object>()) }; }) };
                    if (name == "getElementsByClassName") return new JsVal { Obj = new HostFunc(args => { try { string cls = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); var jde = new JsDomElement(_e, _e._domRoot?.FindById(he.Id) ?? _e._domRoot); var arr = jde.getElementsByClassName(cls) as object[]; var list = new List<object>(); if (arr != null) { for (int i = 0; i < arr.Length; i++) { var el = arr[i] as JsDomElement; if (el != null) { var idv = el.getAttribute("id"); list.Add(new HostElement(_e, idv ?? "")); } } } return new JsVal { Obj = ToJsArray(list) }; } catch { /* swallow */ } return new JsVal { Obj = ToJsArray(new List<object>()) }; }) };
                    return JsVal.Null();
                }
                if (obj.Obj is JsFuncDef)
                {
                    var fdef = (JsFuncDef)obj.Obj;
                    // use hidden bag in globals for function properties
                    Dictionary<string, JsVal> bag = null; JsVal ex;
                    var gkey = "__fn_" + fdef.GetHashCode().ToString();
                    if (_globals.TryGetValue(gkey, out ex)) bag = ex.Obj as Dictionary<string, JsVal>;
                    if (bag == null) { bag = new Dictionary<string, JsVal>(StringComparer.Ordinal); _globals[gkey] = new JsVal { Obj = bag }; }
                    JsVal got;
                    if (string.Equals(name, "prototype", StringComparison.Ordinal))
                    {
                        if (!bag.TryGetValue("prototype", out got)) { got = new JsVal { Obj = new Dictionary<string, JsVal>(StringComparer.Ordinal) }; bag["prototype"] = got; }
                        return got;
                    }
                    if (bag.TryGetValue(name, out got)) return got;
                    return JsVal.Null();
                }
                if (obj.Obj is HostStyle)
                {
                    var hs = (HostStyle)obj.Obj;
                    // cssText — get/set the full style attribute string
                    if (name == "cssText")
                    {
                        return new JsVal
                        {
                            Obj = new HostFunc(args =>
                            {
                                if (args.Count > 0)
                                {
                                    // setter: set the entire style string
                                    var val = ToStr(args[0]);
                                    try
                                    {
                                        if (!string.IsNullOrEmpty(hs.Id)) _e.TryUpdateInlineStyle(hs.Id, "", val);
                                        else if (hs.Node != null) hs.Node.SetAttribute("style", val ?? "");
                                    }
                                    catch { }
                                    return JsVal.Null();
                                }
                                // getter: return the current style string
                                string styleStr = "";
                                try { if (hs.Node != null) styleStr = hs.Node.GetAttribute("style") ?? ""; } catch { }
                                return JsVal.FromStr(styleStr);
                            })
                        };
                    }
                    // length — number of style declarations
                    if (name == "length")
                    {
                        int count = 0;
                        try
                        {
                            if (hs.Node != null)
                            {
                                var style = hs.Node.GetAttribute("style") ?? "";
                                if (!string.IsNullOrWhiteSpace(style))
                                    count = style.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Length;
                            }
                        }
                        catch { }
                        return JsVal.FromNum(count);
                    }
                    // item(index) — return the property name at index
                    if (name == "item")
                    {
                        return new JsVal
                        {
                            Obj = new HostFunc(args =>
                            {
                                int idx = args.Count > 0 ? (int)args[0].Num : 0;
                                try
                                {
                                    if (hs.Node != null)
                                    {
                                        var style = hs.Node.GetAttribute("style") ?? "";
                                        var parts = style.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                                        if (idx >= 0 && idx < parts.Length)
                                        {
                                            var kv = parts[idx].Split(new[] { ':' }, 2);
                                            if (kv.Length >= 1) return JsVal.FromStr(kv[0].Trim());
                                        }
                                    }
                                }
                                catch { }
                                return JsVal.Null();
                            })
                        };
                    }
                    // removeProperty(prop) — remove a property and return its old value
                    if (name == "removeProperty")
                    {
                        return new JsVal
                        {
                            Obj = new HostFunc(args =>
                            {
                                var prop = ToStr(args.Count > 0 ? args[0] : JsVal.Null());
                                string oldVal = "";
                                try
                                {
                                    if (hs.Node != null)
                                    {
                                        var style = hs.Node.GetAttribute("style") ?? "";
                                        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                        foreach (var part in style.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                                        {
                                            var kv = part.Split(new[] { ':' }, 2);
                                            if (kv.Length == 2) dict[kv[0].Trim()] = kv[1].Trim();
                                        }
                                        if (dict.TryGetValue(prop, out oldVal)) dict.Remove(prop);
                                        var sb = new StringBuilder();
                                        bool first = true;
                                        foreach (var kv2 in dict) { if (!first) sb.Append(';'); first = false; sb.Append(kv2.Key).Append(':').Append(kv2.Value); }
                                        hs.Node.SetAttribute("style", sb.ToString());
                                    }
                                }
                                catch { }
                                return JsVal.FromStr(oldVal);
                            })
                        };
                    }
                    if (name == "setProperty") { return new JsVal { Obj = new HostFunc(args => { string prop = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); string val = ToStr(args.Count > 1 ? args[1] : JsVal.Null()); try { if (!string.IsNullOrEmpty(hs.Id)) _e.TryUpdateInlineStyle(hs.Id, prop, val); else if (hs.Node != null) { var style = hs.Node.GetAttribute("style") ?? ""; var dict = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); foreach (var part in style.Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries)) { var kv = part.Split(new[]{':'},2); if (kv.Length==2) dict[kv[0].Trim()] = kv[1].Trim(); } dict[prop]=val??""; var sb=new StringBuilder(); bool first=true; foreach(var kv in dict){ if(!first) sb.Append(';'); first=false; sb.Append(kv.Key).Append(':').Append(kv.Value);} hs.Node.SetAttribute("style", sb.ToString()); } } catch { /* swallow */ } return JsVal.Null(); }) }; }
                    if (name == "getPropertyValue")
                    {
                        return new JsVal
                        {
                            Obj = new HostFunc(args =>
                            {
                                var prop = ToStr(args.Count > 0 ? args[0] : JsVal.Null());
                                try
                                {
                                    if (hs.Node != null)
                                    {
                                        var style = hs.Node.GetAttribute("style") ?? "";
                                        foreach (var part in style.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                                        {
                                            var kv = part.Split(new[] { ':' }, 2);
                                            if (kv.Length == 2 && string.Equals(kv[0].Trim(), prop, StringComparison.OrdinalIgnoreCase))
                                                return JsVal.FromStr(kv[1].Trim());
                                        }
                                    }
                                }
                                catch { }
                                return JsVal.FromStr("");
                            })
                        };
                    }
                    // Reading individual style properties (e.g., el.style.width)
                    try
                    {
                        if (hs.Node != null)
                        {
                            var style = hs.Node.GetAttribute("style") ?? "";
                            var propName = name.Replace("Float", "");
                            foreach (var part in style.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                var kv = part.Split(new[] { ':' }, 2);
                                if (kv.Length == 2 && string.Equals(kv[0].Trim(), propName, StringComparison.OrdinalIgnoreCase))
                                    return JsVal.FromStr(kv[1].Trim());
                            }
                        }
                    }
                    catch { }
                    return JsVal.Null();
                }
                if (obj.Obj is HostClassList)
                {
                    var cl = (HostClassList)obj.Obj;
                    if (name == "add" || name == "remove" || name == "toggle") return new JsVal { Obj = new HostFunc(args => { string cls = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); try { if (!string.IsNullOrEmpty(cl.Id)) _e.TryUpdateClassList(cl.Id, name, cls); else if (cl.Node != null) { if (name=="add") cl.Node.AddClass(cls); else if (name=="remove") cl.Node.RemoveClass(cls); else cl.Node.ToggleClass(cls); } } catch { /* swallow */ } return JsVal.Null(); }) };
                    if (name == "contains") return new JsVal { Obj = new HostFunc(args => { string cls = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); try { if (cl.Node != null) { var cur = cl.Node.GetAttribute("class") ?? ""; var set = new HashSet<string>(cur.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal); return JsVal.FromBool(set.Contains(cls)); } } catch { /* swallow */ } return JsVal.FromBool(false); }) };
                    return JsVal.Null();
                }
                if (obj.Obj is HostDataset)
                {
                    var ds = (HostDataset)obj.Obj;
                    if (name == "set") return new JsVal { Obj = new HostFunc(args => { try { var k = ToStr(args.Count>0?args[0]:JsVal.Null()); var v = ToStr(args.Count>1?args[1]:JsVal.Null()); if (ds.Node!=null && !string.IsNullOrEmpty(k)) ds.Node.SetAttribute("data-"+k, v??""); } catch { /* swallow */ } return JsVal.Null(); }) };
                    // reading arbitrary key as property
                    try { var v = ds.Node != null ? ds.Node.GetAttribute("data-" + name) : null; if (v != null) return JsVal.FromStr(v); } catch { /* swallow */ }
                    return JsVal.Null();
                }
                if (obj.Obj is HostClipboard)
                {
                    if (name == "readText")
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            string text = "";
                            try
                            {
#if HAS_CLIPBOARD
                                var data = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                                if (data != null && data.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
                                {
                                    var op = data.GetTextAsync(); op.AsTask().Wait(500);
                                    try { text = op.GetResults(); } catch { /* swallow */ }
                                }
#endif
                            }
                            catch { /* swallow */ }
                            var fn = args.Count>0 ? args[0].Obj as JsFuncDef : null;
                            if (fn != null) { var r = new JsMiniRunner(_e); r.InvokeFunction(fn, new List<JsVal> { JsVal.FromStr(text) }); return JsVal.Null(); }
                            return JsVal.FromStr(text);
                        }) };
                    if (name == "writeText")
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            try
                            {
#if HAS_CLIPBOARD
                                var s = ToStr(args.Count>0?args[0]:JsVal.Null());
                                var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
                                pkg.SetText(s ?? "");
                                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
                                Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
#endif
                            }
                            catch { /* swallow */ }
                            return JsVal.Null();
                        }) };
                    return JsVal.Null();
                }
                if (obj.Obj is HostConsole)
                {
                    if (name == "log" || name == "warn" || name == "error" || name == "info")
                        return new JsVal { Obj = new HostFunc(args => { try { string msg = args.Count > 0 ? ToStr(args[0]) : ""; _e._host.SetStatus(msg); } catch { /* swallow */ } return JsVal.Null(); }) };
                    return JsVal.Null();
                }
                if (obj.Obj is HostHistory)
                {
                    if (name == "pushState") return new JsVal { Obj = new HostFunc(args => { try { string url = ToStr(args.Count > 2 ? args[2] : JsVal.Null()); var u = Resolve(_e._ctx?.BaseUri, url); if (u != null) _e.HistoryPush(u); } catch { /* swallow */ } return JsVal.Null(); }) };
                    if (name == "replaceState") return new JsVal { Obj = new HostFunc(args => { try { string url = ToStr(args.Count > 2 ? args[2] : JsVal.Null()); var u = Resolve(_e._ctx?.BaseUri, url); if (u != null) _e.HistoryReplace(u); } catch { /* swallow */ } return JsVal.Null(); }) };
                    return JsVal.Null();
                }
                if (obj.Obj is HostWindow)
                {
                    if (name == "open") return new JsVal { Obj = new HostFunc(args => { try { string url = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); _e.Navigate(url); } catch { /* swallow */ } return JsVal.Null(); }) };
                    if (name == "innerWidth") { try { double w = 0; try { w = Windows.UI.Xaml.Window.Current.Bounds.Width; } catch { /* swallow */ } return JsVal.FromNum(w); } catch { return JsVal.FromNum(0); } }
                    if (name == "innerHeight") { try { double h = 0; try { h = Windows.UI.Xaml.Window.Current.Bounds.Height; } catch { /* swallow */ } return JsVal.FromNum(h); } catch { return JsVal.FromNum(0); } }
                    if (name == "navigator") return new JsVal { Obj = new HostNavigator(_e) };
                    if (name == "performance") return new JsVal { Obj = new HostPerformance(_e) };
                    if (name == "matchMedia")
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            try
                            {
                                string q = ToStr(args.Count > 0 ? args[0] : JsVal.Null());
                                var mediaMatches = _e.EvaluateMediaQueryString(q);
                                var m = new Dictionary<string, JsVal>(StringComparer.Ordinal) { { "matches", JsVal.FromBool(mediaMatches) } };
                                return new JsVal { Obj = m };
                            }
                            catch { return new JsVal { Obj = new Dictionary<string, JsVal>(StringComparer.Ordinal) { { "matches", JsVal.FromBool(false) } } }; }
                        }) };
                    if (name == "fetch")
                    {
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            try
                            {
                                var url = ToStr(args.Count > 0 ? args[0] : JsVal.Null());
                                Uri abs = null; try { abs = Resolve(_e._ctx?.BaseUri, url); } catch { /* swallow */ }
                                // options: { method, headers:{}, body }
                                string method = null; string body = null; Dictionary<string,string> headers = null;
                                if (args.Count > 1)
                                {
                                    var opts = args[1].Obj as Dictionary<string, JsVal>;
                                    if (opts != null)
                                    {
                                        JsVal mv; if (opts.TryGetValue("method", out mv)) method = ToStr(mv);
                                        JsVal bv; if (opts.TryGetValue("body", out bv)) body = ToStr(bv);
                                        JsVal hv; if (opts.TryGetValue("headers", out hv))
                                        {
                                            var hmap = hv.Obj as Dictionary<string, JsVal>;
                                            if (hmap != null)
                                            {
                                                headers = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                                                foreach (var kv in hmap) headers[kv.Key] = ToStr(kv.Value);
                                            }
                                        }
                                    }
                                }
                                System.Threading.Tasks.Task<FetchResult> task = _e.FetchDetailedAsync(abs ?? new Uri(url), _e._ctx?.BaseUri, method, headers, body);
                                return new JsVal { Obj = new HostFetchResp { E = _e, Task = task } };
                            }
                            catch { return JsVal.Null(); }
                        }) };
                    }
                    if (name == "location") return new JsVal { Obj = new HostLocation(_e) };
                    return JsVal.Null();
                }
                if (obj.Obj is HostNavigator)
                {
                    if (name == "clipboard")
                    {
                        return new JsVal { Obj = new HostClipboard(_e) };
                    }
                    if (name == "userAgent")
                    {
                        try
                        {
                            // Reuse the desktop-ish UA used for network requests
                            var ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
                            return JsVal.FromStr(ua);
                        }
                        catch { return JsVal.FromStr("Mozilla/5.0"); }
                    }
                    if (name == "platform") { return JsVal.FromStr("Win32"); }
                    if (name == "language") { try { var l = Windows.Globalization.ApplicationLanguages.Languages; return JsVal.FromStr((l != null && l.Count > 0) ? l[0] : "en-US"); } catch { return JsVal.FromStr("en-US"); } }
                    if (name == "languages")
                    {
                        try
                        {
                            var list = new List<JsVal>(); var langs = Windows.Globalization.ApplicationLanguages.Languages; if (langs != null) { foreach (var s in langs) list.Add(JsVal.FromStr(s)); }
                            return new JsVal { Obj = list };
                        }
                        catch { return new JsVal { Obj = new List<JsVal>() }; }
                    }
                    if (name == "onLine") { return JsVal.FromBool(true); }
                    if (name == "hardwareConcurrency") { return JsVal.FromNum(1); }
                    return JsVal.Null();
                }
                if (obj.Obj is HostClassList)
                {
                    var cl = (HostClassList)obj.Obj;
                    if (name == "add") return new JsVal { Obj = new HostFunc(args => { try { var cls = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); if (cl.Node != null) { var cur = cl.Node.GetAttribute("class") ?? ""; var set = new HashSet<string>(cur.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal); set.Add(cls); cl.Node.SetAttribute("class", string.Join(" ", set.ToArray())); } } catch { /* swallow */ } return JsVal.Null(); }) };
                    if (name == "remove") return new JsVal { Obj = new HostFunc(args => { try { var cls = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); if (cl.Node != null) { var cur = cl.Node.GetAttribute("class") ?? ""; var set = new HashSet<string>(cur.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal); set.Remove(cls); cl.Node.SetAttribute("class", string.Join(" ", set.ToArray())); } } catch { /* swallow */ } return JsVal.Null(); }) };
                    if (name == "toggle") return new JsVal { Obj = new HostFunc(args => { try { var cls = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); if (cl.Node != null) { var cur = cl.Node.GetAttribute("class") ?? ""; var set = new HashSet<string>(cur.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal); if (!set.Add(cls)) set.Remove(cls); cl.Node.SetAttribute("class", string.Join(" ", set.ToArray())); } } catch { /* swallow */ } return JsVal.Null(); }) };
                    if (name == "contains") return new JsVal { Obj = new HostFunc(args => { try { var cls = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); if (cl.Node != null) { var cur = cl.Node.GetAttribute("class") ?? ""; var set = new HashSet<string>(cur.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal); return JsVal.FromBool(set.Contains(cls)); } } catch { /* swallow */ } return JsVal.FromBool(false); }) };
                    return JsVal.Null();
                }
                if (obj.Obj is HostLocalStorage)
                {
                    var h = (HostLocalStorage)obj.Obj;
                    if (name == "getItem") return new JsVal { Obj = new HostFunc(args => { try { string k = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); var bag = h.Session ? _e.GetSessionStorageFor(_e._ctx?.BaseUri) : _e.GetLocalStorageFor(_e._ctx?.BaseUri); string v=null; lock(_e._storageLock) bag.TryGetValue(k, out v); return JsVal.FromStr(v??""); } catch { return JsVal.Null(); } }) };
                    if (name == "setItem") return new JsVal { Obj = new HostFunc(args => { try { string k = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); string v = ToStr(args.Count > 1 ? args[1] : JsVal.Null()); var bag = h.Session ? _e.GetSessionStorageFor(_e._ctx?.BaseUri) : _e.GetLocalStorageFor(_e._ctx?.BaseUri); lock(_e._storageLock) bag[k]=v??""; if(!h.Session) _e.PersistLocalStorage(); } catch { /* swallow */ } return JsVal.Null(); }) };
                    if (name == "removeItem") return new JsVal { Obj = new HostFunc(args => { try { string k = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); var bag = h.Session ? _e.GetSessionStorageFor(_e._ctx?.BaseUri) : _e.GetLocalStorageFor(_e._ctx?.BaseUri); lock(_e._storageLock) bag.Remove(k); if(!h.Session) _e.PersistLocalStorage(); } catch { /* swallow */ } return JsVal.Null(); }) };
                    if (name == "clear") return new JsVal { Obj = new HostFunc(args => { try { var bag = h.Session ? _e.GetSessionStorageFor(_e._ctx?.BaseUri) : _e.GetLocalStorageFor(_e._ctx?.BaseUri); lock(_e._storageLock) bag.Clear(); if(!h.Session) _e.PersistLocalStorage(); } catch { /* swallow */ } return JsVal.Null(); }) };
                    return JsVal.Null();
                }
                if (obj.Obj is HostPerformance)
                {
                    if (name == "now")
                    {
                        return new JsVal { Obj = new HostFunc(args => { try { var sw = System.Diagnostics.Stopwatch.GetTimestamp(); var freq = (double)System.Diagnostics.Stopwatch.Frequency; var ms = (sw * 1000.0) / freq; return JsVal.FromNum(ms); } catch { return JsVal.FromNum(0); } }) };
                    }
                    return JsVal.Null();
                }
                if (obj.Obj is HostObjectType)
                {
                    if (name == "defineProperty")
                    {
                        // value-only defineProperty: Object.defineProperty(obj, key, { value: v })
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            try
                            {
                                var target = args.Count > 0 ? args[0] : null;
                                var key = ToStr(args.Count > 1 ? args[1] : JsVal.Null());
                                var desc = args.Count > 2 ? args[2] : null;
                                if (target == null || string.IsNullOrEmpty(key) || desc == null) return JsVal.Null();
                                JsVal vprop = JsVal.Null();
                                var dmap = desc.Obj as Dictionary<string, JsVal>;
                                if (dmap != null)
                                {
                                    JsVal tmp; if (dmap.TryGetValue("value", out tmp)) vprop = tmp;
                                }
                                // plain object
                                var maps = target.Obj as Dictionary<string, JsVal>;
                                if (maps != null) { maps[key] = vprop; return JsVal.Null(); }
                                // function object: attach via hidden map
                                var fdef = target.Obj as JsFuncDef;
                                if (fdef != null)
                                {
                                    // emulate a simple property bag via a parallel map in globals
                                    Dictionary<string, JsVal> bag;
                                    var gkey = "__fn_" + fdef.GetHashCode().ToString();
                                    JsVal ex;
                                    if (!_globals.TryGetValue(gkey, out ex) || (bag = ex.Obj as Dictionary<string, JsVal>) == null)
                                    { bag = new Dictionary<string, JsVal>(StringComparer.Ordinal); _globals[gkey] = new JsVal { Obj = bag }; }
                                    bag[key] = vprop; return JsVal.Null();
                                }
                            }
                            catch { /* swallow */ }
                            return JsVal.Null();
                        }) };
                    }
                    return JsVal.Null();
                }
                if (obj.Obj is HostFetchResp)
                {
                    var resp = (HostFetchResp)obj.Obj;
                    if (name == "then")
                    {
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            var fn = args.Count > 0 ? args[0].Obj as JsFuncDef : null;
                            if (fn != null && resp.Task != null)
                            {
                                resp.Task.ContinueWith(t =>
                                {
                                    try
                                    {
                                        var fr = t.Result ?? new FetchResult();
                                        // Build Response-like object
                                        var ro = new Dictionary<string, JsVal>(StringComparer.Ordinal);
                                        ro["status"] = JsVal.FromNum(fr.Status);
                                        ro["statusText"] = JsVal.FromStr(fr.StatusText ?? "");
                                        ro["ok"] = JsVal.FromBool(fr.Ok);
                                        // headers object
                                        var hdrs = new Dictionary<string, JsVal>(StringComparer.OrdinalIgnoreCase);
                                        if (fr.Headers != null) { foreach (var kv in fr.Headers) hdrs[kv.Key] = JsVal.FromStr(kv.Value ?? ""); }
                                        ro["headers"] = new JsVal { Obj = hdrs };
                                        // text(fn?) and json(fn?) — call callback immediately when provided
                                        ro["text"] = new JsVal { Obj = new HostFunc(a => { var f = a.Count > 0 ? a[0].Obj as JsFuncDef : null; if (f != null) { var r2 = new JsMiniRunner(_e); r2.InvokeFunction(f, new List<JsVal> { JsVal.FromStr(fr.Body ?? "") }); } return JsVal.FromStr(fr.Body ?? ""); }) };
                                        ro["json"] = new JsVal { Obj = new HostFunc(a => { var f = a.Count > 0 ? a[0].Obj as JsFuncDef : null; JsVal arg; try { var j = Windows.Data.Json.JsonValue.Parse(fr.Body ?? ""); arg = FromJson(j); } catch { arg = JsVal.Null(); } if (f != null) { var r2 = new JsMiniRunner(_e); r2.InvokeFunction(f, new List<JsVal> { arg }); } return arg; }) };
                                        var runner = new JsMiniRunner(_e);
                                        runner.InvokeFunction(fn, new List<JsVal> { new JsVal { Obj = ro } });
                                    }
                                    catch { /* swallow */ }
                                });
                            }
                            return JsVal.Null();
                        }) };
                    }
                    if (name == "text")
                    {
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            var fn = args.Count > 0 ? args[0].Obj as JsFuncDef : null;
                            if (fn != null && resp.Task != null)
                            {
                                resp.Task.ContinueWith(t =>
                                {
                                    try
                                    {
                                        var text = (t.Result != null ? t.Result.Body : "") ?? "";
                                        var runner = new JsMiniRunner(_e);
                                        runner.InvokeFunction(fn, new List<JsVal> { JsVal.FromStr(text) });
                                    }
                                    catch { /* swallow */ }
                                });
                            }
                            return JsVal.Null();
                        }) };
                    }
                    if (name == "json")
                    {
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            var fn = args.Count > 0 ? args[0].Obj as JsFuncDef : null;
                            if (fn != null && resp.Task != null)
                            {
                                resp.Task.ContinueWith(t =>
                                {
                                    try
                                    {
                                        var s = (t.Result != null ? t.Result.Body : "") ?? ""; Windows.Data.Json.IJsonValue jv = null; try { jv = Windows.Data.Json.JsonValue.Parse(s); } catch { /* swallow */ }
                                        var arg = jv != null ? FromJson(jv) : JsVal.Null();
                                        var runner = new JsMiniRunner(_e);
                                        runner.InvokeFunction(fn, new List<JsVal> { arg });
                                    }
                                    catch { /* swallow */ }
                                });
                            }
                            return JsVal.Null();
                        }) };
                    }
                    return JsVal.Null();
                }
                if (obj.Obj is HostLocation)
                {
                    if (name == "href") { var u = _e._ctx != null && _e._ctx.BaseUri != null ? _e._ctx.BaseUri.AbsoluteUri : ""; return JsVal.FromStr(u); }
                    if (name == "reload") return new JsVal { Obj = new HostFunc(args => { try { var b = _e._ctx != null ? _e._ctx.BaseUri : null; if (b != null) _e._host.Navigate(b); else _e.RequestRepaint(); } catch { /* swallow */ } return JsVal.Null(); }) };
                    return JsVal.Null();
                }
                if (obj.Obj is HostJSON)
                {
                    if (name == "parse") return new JsVal { Obj = new HostFunc(args => { try { var s = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); var v = Windows.Data.Json.JsonValue.Parse(s); return FromJson(v); } catch { return JsVal.Null(); } }) };
                    if (name == "stringify") return new JsVal { Obj = new HostFunc(args => { try { return JsVal.FromStr(ToJson(args.Count > 0 ? args[0] : JsVal.Null())); } catch { return JsVal.FromStr("null"); } }) };
                    return JsVal.Null();
                }
                if (obj.Obj is HostPromiseType)
                {
                    if (name == "resolve")
                    {
                        return new JsVal { Obj = new HostFunc(args => { var p = new HostPromise { E = _e, State = 1, Value = args.Count > 0 ? args[0] : JsVal.Null() }; return new JsVal { Obj = p }; }) };
                    }
                    if (name == "reject")
                    {
                        return new JsVal { Obj = new HostFunc(args => { var p = new HostPromise { E = _e, State = -1, Value = args.Count > 0 ? args[0] : JsVal.Null() }; return new JsVal { Obj = p }; }) };
                    }
                    return JsVal.Null();
                }
                if (obj.Obj is HostPromise)
                {
                    var hp = (HostPromise)obj.Obj;
                    if (name == "then")
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            var onFulfilled = args.Count > 0 ? args[0].Obj as JsFuncDef : null;
                            var onRejected = args.Count > 1 ? args[1].Obj as JsFuncDef : null;
                            var next = new HostPromise { E = _e, State = 0, Value = JsVal.Null() };
                            if (onFulfilled != null) hp.Handlers.Add(new PromiseHandler { IsFulfill = true, Fn = onFulfilled, Next = next });
                            else hp.Handlers.Add(new PromiseHandler { IsFulfill = true, Fn = null, Next = next });
                            if (onRejected != null) hp.Handlers.Add(new PromiseHandler { IsFulfill = false, Fn = onRejected, Next = next });

                            // If already settled, schedule processing immediately
                            if (hp.State != 0)
                            {
                                _e.EnqueueMicrotask(() => { try { ProcessPromiseHandlers(hp); } catch { /* swallow */ } });
                            }
                            return new JsVal { Obj = next };
                        }) };
                    if (name == "catch")
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            var onRejected = args.Count > 0 ? args[0].Obj as JsFuncDef : null;
                            var next = new HostPromise { E = _e, State = 0, Value = JsVal.Null() };
                            if (onRejected != null) hp.Handlers.Add(new PromiseHandler { IsFulfill = false, Fn = onRejected, Next = next });
                            if (hp.State != 0) { _e.EnqueueMicrotask(() => { try { ProcessPromiseHandlers(hp); } catch { /* swallow */ } }); }
                            return new JsVal { Obj = next };
                        }) };
                    if (name == "finally")
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            var onFinally = args.Count > 0 ? args[0].Obj as JsFuncDef : null;
                            var next = new HostPromise { E = _e, State = 0, Value = JsVal.Null() };
                            // Wrap finally to pass through the value unchanged
                            JsFuncDef passthrough = null;
                            if (onFinally != null)
                            {
                                passthrough = new JsFuncDef { Body = null };
                                passthrough.Params.Add("v");
                                // When called, run onFinally(); then resolve with original v
                                _e.EnqueueMicrotask(() => { }); // ensure globals exist
                            }
                            hp.Handlers.Add(new PromiseHandler { IsFulfill = true, Fn = onFinally, Next = next });
                            hp.Handlers.Add(new PromiseHandler { IsFulfill = false, Fn = onFinally, Next = next });
                            if (hp.State != 0) { _e.EnqueueMicrotask(() => { try { ProcessPromiseHandlers(hp); } catch { /* swallow */ } }); }
                            return new JsVal { Obj = next };
                        }) };
                    return JsVal.Null();
                }
                return JsVal.Null();
            }

            private JsVal GetIndex(JsVal obj, JsVal key)
            { var map = obj.Obj as Dictionary<string, JsVal>; if (map != null) { JsVal v; return map.TryGetValue(ToStr(key), out v) ? v : JsVal.Null(); } var arr = obj.Obj as List<JsVal>; if (arr != null) { int i = (int)ToNum(key); if (i >= 0 && i < arr.Count) return arr[i]; return JsVal.Null(); } return JsVal.Null(); }

            private JsVal ConstructInstance(JsVal ctor, List<JsVal> args)
            {
                if (ctor == null) return new JsVal { Obj = new Dictionary<string, JsVal>(StringComparer.Ordinal) };

                var host = ctor.Obj as HostFunc;
                if (host != null)
                {
                    try { return host.F(args ?? new List<JsVal>()); }
                    catch { return JsVal.Null(); }
                }

                var def = ctor.Obj as JsFuncDef;
                if (def == null) return new JsVal { Obj = new Dictionary<string, JsVal>(StringComparer.Ordinal) };

                var instanceMap = new Dictionary<string, JsVal>(StringComparer.Ordinal);
                var instance = new JsVal { Obj = instanceMap };
                JsVal result = JsVal.Null();
                try
                {
                    result = InvokeFunction(def, args ?? new List<JsVal>(), instance);
                }
                catch { /* swallow */ }

                if (result != null && (result.Obj != null || result.Str != null || result.Num.HasValue || result.Bool.HasValue))
                {
                    return result;
                }

                return instance;
            }

            private JsVal Invoke(JsVal callee, List<JsVal> args)
            { var host = callee.Obj as HostFunc; if (host != null) { try { return host.F(args ?? new List<JsVal>()); } catch { return JsVal.Null(); } } var def = callee.Obj as JsFuncDef; if (def != null) { return InvokeFunction(def, args ?? new List<JsVal>()); } return JsVal.Null(); }

            internal JsVal InvokeFunction(JsFuncDef def, List<JsVal> args, JsVal thisObj = null)
            {
                var initial = new Dictionary<string, JsVal>(_globals, StringComparer.Ordinal);
                if (thisObj != null) initial["this"] = thisObj;
                else
                {
                    JsVal existingThis;
                    if (_globals.TryGetValue("this", out existingThis)) initial["this"] = existingThis;
                    else initial["this"] = JsVal.Null();
                }
                if (def.Parameters != null && def.Parameters.Count > 0)
                {
                    var binder = new JsMiniRunner(_e);
                    binder._globals.Clear();
                    foreach (var kv in initial) binder._globals[kv.Key] = kv.Value;

                    int argIndex = 0;
                    var totalArgs = args ?? new List<JsVal>();
                    foreach (var param in def.Parameters)
                    {
                        if (param == null) continue;
                        if (param.IsRest)
                        {
                            var restList = new List<JsVal>();
                            if (argIndex < totalArgs.Count)
                            {
                                for (int i = argIndex; i < totalArgs.Count; i++) restList.Add(totalArgs[i]);
                            }
                            binder.AssignBinding(param.Pattern, new JsVal { Obj = restList }, true);
                            argIndex = totalArgs.Count;
                        }
                        else
                        {
                            bool hasArg = argIndex < totalArgs.Count;
                            var passed = hasArg ? totalArgs[argIndex] : JsVal.Null();
                            binder.AssignBinding(param.Pattern, passed, hasArg);
                            argIndex++;
                        }
                    }

                    initial = new Dictionary<string, JsVal>(binder._globals, StringComparer.Ordinal);
                }
                else
                {
                    for (int i = 0; i < def.Params.Count; i++)
                    {
                        var v = (args != null && i < args.Count) ? args[i] : JsVal.Null();
                        initial[def.Params[i]] = v;
                    }
                }
                var child = new JsMiniRunner(_e); child._globals.Clear(); foreach (var kv in initial) child._globals[kv.Key] = kv.Value;
                try
                {
                    // Arrow function with expression body => implicit return
                    if (!string.IsNullOrWhiteSpace(def.Expr))
                    {
                        child._src = def.Expr ?? ""; child._pos = 0; child._len = child._src.Length; child.SkipWs();
                        var result = child.ParseExpression();
                        return result ?? JsVal.Null();
                    }
                }
                catch { /* swallow */ }
                try
                {
                    child._src = def.Body ?? ""; child._pos = 0; child._len = child._src.Length; child.SkipWs();
                    while (!child.Eof()) { child.ParseStatement(); child.SkipWs(); }
                }
                catch (ReturnEx rex) { return rex.Value ?? JsVal.Null(); }
                catch { /* swallow */ }
                return JsVal.Null();
            }
            private static string TypeOf(JsVal v) {
                if (v == null) return "undefined";
                if (v.Obj is HostFunc || v.Obj is JsFuncDef) return "function";
                if (v.Str != null) return "string";
                if (v.Num.HasValue) return "number";
                if (v.Bool.HasValue) return "boolean";
                if (v.Obj != null) return "object";
                // Null/unknown => report undefined for broader compat (undeclared checks)
                return "undefined";
            }

            private static bool InstanceOf(JsVal v, JsVal ctor)
            {
                if (v == null || ctor == null) return false;
                var c = ctor.Obj;
                if (c is HostPromiseType) return v.Obj is HostPromise;
                if (c is HostMapType) return v.Obj is HostMap;
                if (c is HostSetType) return v.Obj is HostSet;
                if (c is HostObjectType) return v.Obj != null;
                return false;
            }

            private static double ToNum(JsVal v) { if (v == null) return 0; if (v.Num.HasValue) return v.Num.Value; double d = 0; if (v.Str != null) { double.TryParse(v.Str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d); return d; } if (v.Bool.HasValue) return v.Bool.Value ? 1 : 0; return 0; }
            private static string ToStr(JsVal v) { if (v == null) return ""; if (v.Str != null) return v.Str; if (v.Num.HasValue) return v.Num.Value.ToString(System.Globalization.CultureInfo.InvariantCulture); if (v.Bool.HasValue) return v.Bool.Value ? "true" : "false"; return ""; }

            private static JsVal FromJson(Windows.Data.Json.IJsonValue jv)
            {
                if (jv == null) return JsVal.Null();
                switch (jv.ValueType)
                {
                    case Windows.Data.Json.JsonValueType.String:
                        try { return JsVal.FromStr(jv.GetString()); } catch { return JsVal.FromStr(""); }
                    case Windows.Data.Json.JsonValueType.Boolean:
                        try { return JsVal.FromBool(jv.GetBoolean()); } catch { return JsVal.FromBool(false); }
                    case Windows.Data.Json.JsonValueType.Array:
                        try { var a = jv.GetArray(); var list = new List<JsVal>(); foreach (var it in a) list.Add(FromJson(it)); return new JsVal { Obj = list }; } catch { return new JsVal { Obj = new List<JsVal>() }; }
                    case Windows.Data.Json.JsonValueType.Object:
                        try { var o = jv.GetObject(); var dict = new Dictionary<string, JsVal>(StringComparer.Ordinal); foreach (var kv in o) dict[kv.Key] = FromJson(kv.Value); return new JsVal { Obj = dict }; } catch { return new JsVal { Obj = new Dictionary<string, JsVal>() }; }
                    default: return JsVal.Null();
                }
            }

            private static string ToJson(JsVal v)
            {
                if (v == null) return "null";
                if (v.Str != null) return "\"" + (v.Str ?? "").Replace("\"", "\\\"") + "\"";
                if (v.Num.HasValue) return v.Num.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (v.Bool.HasValue) return v.Bool.Value ? "true" : "false";
                var map = v.Obj as Dictionary<string, JsVal>; if (map != null) { var sb = new StringBuilder(); sb.Append("{"); bool first = true; foreach (var kv in map) { if (!first) sb.Append(","); first = false; sb.Append("\"").Append(kv.Key.Replace("\"", "\\\"")).Append("\":").Append(ToJson(kv.Value)); } sb.Append("}"); return sb.ToString(); }
                var arr = v.Obj as List<JsVal>; if (arr != null) { var sb = new StringBuilder(); sb.Append("["); for (int i = 0; i < arr.Count; i++) { if (i > 0) sb.Append(","); sb.Append(ToJson(arr[i])); } sb.Append("]"); return sb.ToString(); }
                return "null";
            }

            // Process promise handlers for a settled promise
            private void ProcessPromiseHandlers(HostPromise p)
            {
                if (p == null || p.State == 0) return;
                var handlers = p.Handlers != null ? p.Handlers.ToArray() : new PromiseHandler[0];
                for (int i = 0; i < handlers.Length; i++)
                {
                    var h = handlers[i]; if (h == null) continue;
                    bool match = (p.State == 1 && h.IsFulfill) || (p.State == -1 && !h.IsFulfill);
                    if (!match) continue;
                    try
                    {
                        if (h.Fn != null)
                        {
                            var r = new JsMiniRunner(_e);
                        var ret = r.InvokeFunction(h.Fn, new List<JsMiniRunner.JsVal> { p.Value });
                            var rp = ret != null ? ret.Obj as HostPromise : null;
                            if (rp != null)
                            {
                                // adopt
                                if (rp.State == 0)
                                {
                                    rp.Handlers.Add(new PromiseHandler { IsFulfill = true, Fn = null, Next = h.Next });
                                    rp.Handlers.Add(new PromiseHandler { IsFulfill = false, Fn = null, Next = h.Next });
                                }
                                else
                                {
                                    if (h.Next != null) { h.Next.State = rp.State; h.Next.Value = rp.Value; _e.EnqueueMicrotask(() => { try { ProcessPromiseHandlers(h.Next); } catch { /* swallow */ } }); }
                                }
                            }
                            else
                            {
                                if (h.Next != null) { h.Next.State = 1; h.Next.Value = ret ?? JsVal.Null(); _e.EnqueueMicrotask(() => { try { ProcessPromiseHandlers(h.Next); } catch { /* swallow */ } }); }
                            }
                        }
                        else
                        {
                            // no handler => propagate as-is
                            if (h.Next != null) { h.Next.State = p.State; h.Next.Value = p.Value; _e.EnqueueMicrotask(() => { try { ProcessPromiseHandlers(h.Next); } catch { /* swallow */ } }); }
                        }
                    }
                    catch { /* swallow */ }
                }
            }
        }

        private void TryUpdateClassList(string id, string op, string cls)
        {
            try
            {
                if (_domRoot == null || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(cls)) return;
                var doc = new JsDocument(this, _domRoot);
                var el = doc.getElementById(id) as JsDomElement; if (el == null) return;
                var cur = el.getAttribute("class") ?? string.Empty;
                var set = new HashSet<string>(cur.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
                bool changed = false;
                if (op == "add") { if (!set.Contains(cls)) { set.Add(cls); changed = true; } }
                else if (op == "remove") { if (set.Remove(cls)) changed = true; }
                else if (op == "toggle") { if (!set.Remove(cls)) { set.Add(cls); } changed = true; }
                if (changed) el.setAttribute("class", string.Join(" ", set.ToArray()));
            }
            catch { /* swallow */ }
        }


        // Process promise handlers for a settled HostPromise (outer engine implementation)
        private void ProcessPromiseHandlers(JsMiniRunner.HostPromise p)
        {
            if (p == null || p.State == 0) return;
            var handlers = p.Handlers != null ? p.Handlers.ToArray() : new JsMiniRunner.PromiseHandler[0];
            for (int i = 0; i < handlers.Length; i++)
            {
                var h = handlers[i]; if (h == null) continue;
                bool match = (p.State == 1 && h.IsFulfill) || (p.State == -1 && !h.IsFulfill);
                if (!match) continue;
                try
                {
                    if (h.Fn != null)
                    {
                        var r = new JsMiniRunner(_e);
                        var ret = r.InvokeFunction(h.Fn, new List<JsMiniRunner.JsVal> { p.Value });
                        var rp = ret != null ? ret.Obj as JsMiniRunner.HostPromise : null;
                        if (rp != null)
                        {
                            if (rp.State == 0)
                            {
                                rp.Handlers.Add(new JsMiniRunner.PromiseHandler { IsFulfill = true, Fn = null, Next = h.Next });
                                rp.Handlers.Add(new JsMiniRunner.PromiseHandler { IsFulfill = false, Fn = null, Next = h.Next });
                            }
                            else
                            {
                                if (h.Next != null) { h.Next.State = rp.State; h.Next.Value = rp.Value; EnqueueMicrotask(() => { try { ProcessPromiseHandlers(h.Next); } catch { } }); }
                            }
                        }
                        else
                        {
                             if (h.Next != null) { h.Next.State = 1; h.Next.Value = ret ?? JsMiniRunner.JsVal.Null(); EnqueueMicrotask(() => { try { ProcessPromiseHandlers(h.Next); } catch { } }); }
                        }
                    }
                    else
                    {
                        // no handler => propagate as-is
                        if (h.Next != null) { h.Next.State = p.State; h.Next.Value = p.Value; EnqueueMicrotask(() => { try { ProcessPromiseHandlers(h.Next); } catch { } }); }
                    }
                }
                catch { /* swallow */ }
            }
        }

        private static Uri Resolve(Uri baseUri, string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            Uri abs;
            if (Uri.TryCreate(url, UriKind.Absolute, out abs)) return abs;
            if (baseUri != null && Uri.TryCreate(baseUri, url, out abs)) return abs;
            return null;
        }

        // --------------------------- Document / Element bridge (minimal) ---------------------------
        private sealed class JsDocument
        {
            private readonly JavaScriptEngine _e;
            private LiteElement _root;
            public JsDocument(JavaScriptEngine e, LiteElement root) { _e = e; _root = root; }

            public object getElementById(string id)
            {
                if (string.IsNullOrEmpty(id) || _root == null) return null;
                foreach (var n in _root.Descendants())
                {
                    if (n.Attr != null)
                    {
                        string v; if (n.Attr.TryGetValue("id", out v) && string.Equals(v, id, StringComparison.Ordinal)) return new JsDomElement(_e, n);
                    }
                }
                return null;
            }

            public object[] getElementsByTagName(string tag)
            {
                if (string.IsNullOrEmpty(tag) || _root == null) return new object[0];
                var list = new List<object>();
                foreach (var n in _root.Descendants())
                    if (!n.IsText && string.Equals(n.Tag, tag, StringComparison.OrdinalIgnoreCase))
                        list.Add(new JsDomElement(_e, n));
                return list.ToArray();
            }

            // supports only: "#id", ".class", "tag"
            public object[] getElementsByClassName(string className)
            {
                if (string.IsNullOrEmpty(className) || _root == null) return new object[0];
                var list = new List<object>();
                foreach (var n in _root.Descendants())
                {
                    if (n.IsText) continue;
                    string cls; if (n.Attr != null && n.Attr.TryGetValue("class", out cls) && !string.IsNullOrEmpty(cls))
                    {
                        var parts = cls.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < parts.Length; i++)
                            if (string.Equals(parts[i], className, StringComparison.Ordinal)) { list.Add(new JsDomElement(_e, n)); break; }
                    }
                }
                return list.ToArray();
            }
            public object querySelector(string sel)
            {
                var all = querySelectorAll(sel);
                return all != null && all.Length > 0 ? all[0] : null;
            }

            public object[] querySelectorAll(string sel)
            {
                if (string.IsNullOrWhiteSpace(sel) || _root == null) return new object[0];
                sel = sel.Trim();
                var list = new List<object>();
                if (sel.StartsWith("#"))
                {
                    var id = sel.Substring(1);
                    var e1 = getElementById(id);
                    return e1 == null ? new object[0] : new[] { e1 };
                }
                else if (sel.StartsWith("."))
                {
                    var cls = sel.Substring(1);
                    foreach (var n in _root.Descendants())
                    {
                        string v;
                        if (n.Attr != null && n.Attr.TryGetValue("class", out v) && !string.IsNullOrWhiteSpace(v))
                        {
                            var parts = v.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int i = 0; i < parts.Length; i++)
                                if (string.Equals(parts[i], cls, StringComparison.Ordinal)) { list.Add(new JsDomElement(_e, n)); break; }
                        }
                    }
                }
                else
                {
                    // support descendant selectors like "div span" and attribute selectors like "tag[attr=value]"
                    if (sel.Contains(" "))
                    {
                        var parts = sel.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        var current = new List<LiteElement> { _root };
                        foreach (var p in parts)
                        {
                            var next = new List<LiteElement>();
                            foreach (var c in current)
                                foreach (var d in c.Descendants())
                                    if (MatchesSimpleSelector(d, p)) next.Add(d);
                            current = next;
                        }
                        foreach (var it in current) list.Add(new JsDomElement(_e, it));
                    }
                    else
                    {
                        // single-part tag or attribute selector
                        foreach (var n in _root.Descendants()) if (MatchesSimpleSelector(n, sel)) list.Add(new JsDomElement(_e, n));
                    }
                }
                return list.ToArray();
            }

            internal static bool MatchesSimpleSelector(LiteElement n, string sel)
            {
                if (string.IsNullOrWhiteSpace(sel) || n == null) return false;
                sel = sel.Trim();

                // Handle :not() pseudo-class
                if (sel.StartsWith(":not(", StringComparison.OrdinalIgnoreCase) && sel.EndsWith(")"))
                {
                    var inner = sel.Substring(5, sel.Length - 6).Trim();
                    return !MatchesSimpleSelector(n, inner);
                }

                // Handle adjacent sibling: "a + b" or general sibling: "a ~ b"
                if (sel.Contains(" + ") || sel.Contains(" ~ "))
                {
                    var parts = sel.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 3 && (parts[1] == "+" || parts[1] == "~"))
                    {
                        var a = parts[0]; var op = parts[1]; var b = parts[2];
                        if (!MatchesSimpleSelector(n, b)) return false;
                        var parent = FindParent(n);
                        if (parent == null) return false;
                        var siblings = parent.Children;
                        int idx = siblings.IndexOf(n);
                        if (idx <= 0) return false;
                        if (op == "+") return MatchesSimpleSelector(siblings[idx - 1], a);
                        for (int i = 0; i < idx; i++) if (MatchesSimpleSelector(siblings[i], a)) return true;
                        return false;
                    }
                }

                // Handle descendant selector with child combinator: "a > b"
                // (not supported as a simple selector, skip gracefully)
                if (sel.Contains(" > ")) return false;

                // --- Parse compound selector: tag, #id, .class, [attr], :pseudo, or combination ---
                string matchTag = null;
                string matchId = null;
                var matchClasses = new List<string>();
                var matchAttrs = new List<AttrSelector>();
                var matchPseudo = new List<string>();

                // Split selector into parts by '#' '.' '[' ':'
                // But we need to be careful: "#foo.bar" means tag=None id=foo class=bar
                // "div.foo#bar" means tag=div class=foo id=bar
                // ".foo.bar" means class=foo class=bar
                // "div[data-x]" means tag=div attr=data-x
                // "li:first-child" means tag=li pseudo=first-child
                // "li:nth-child(2)" means tag=li pseudo:nth-child(2)
                // "div:not(.hidden)" means tag=div pseudo:not(.hidden)

                var idx2 = 0;
                while (idx2 < sel.Length)
                {
                    var c = sel[idx2];
                    if (c == '#')
                    {
                        // id
                        idx2++;
                        var start = idx2;
                        while (idx2 < sel.Length && IsSelectorChar(sel[idx2])) idx2++;
                        matchId = sel.Substring(start, idx2 - start);
                    }
                    else if (c == '.')
                    {
                        // class
                        idx2++;
                        var start = idx2;
                        while (idx2 < sel.Length && IsSelectorChar(sel[idx2])) idx2++;
                        matchClasses.Add(sel.Substring(start, idx2 - start));
                    }
                    else if (c == '[')
                    {
                        // attribute selector
                        idx2++;
                        var start = idx2;
                        while (idx2 < sel.Length && sel[idx2] != ']') idx2++;
                        var attrExpr = sel.Substring(start, idx2 - start);
                        if (idx2 < sel.Length) idx2++; // skip ']'
                        var eqPos = attrExpr.IndexOf('=');
                        if (eqPos >= 0)
                        {
                            var attrName = attrExpr.Substring(0, eqPos).Trim();
                            var attrOp = "=";
                            if (attrName.EndsWith("^")) { attrOp = "^="; attrName = attrName.TrimEnd('^'); }
                            else if (attrName.EndsWith("$")) { attrOp = "$="; attrName = attrName.TrimEnd('$'); }
                            else if (attrName.EndsWith("*")) { attrOp = "*="; attrName = attrName.TrimEnd('*'); }
                            else if (attrName.EndsWith("~")) { attrOp = "~="; attrName = attrName.TrimEnd('~'); }
                            else if (attrName.EndsWith("|")) { attrOp = "|="; attrName = attrName.TrimEnd('|'); }
                            var attrVal = attrExpr.Substring(eqPos + 1).Trim('"', '\'', ' ');
                            matchAttrs.Add(new AttrSelector { Name = attrName, Op = attrOp, Value = attrVal });
                        }
                        else
                        {
                            matchAttrs.Add(new AttrSelector { Name = attrExpr.Trim(), Op = null, Value = null });
                        }
                    }
                    else if (c == ':')
                    {
                        // pseudo-class
                        idx2++;
                        var start = idx2;
                        while (idx2 < sel.Length && sel[idx2] != '(' && sel[idx2] != '.' && sel[idx2] != '#' && sel[idx2] != '[' && sel[idx2] != ':' && sel[idx2] != ' ' && sel[idx2] != '+' && sel[idx2] != '~' && sel[idx2] != '>') idx2++;
                        var pseudoName = sel.Substring(start, idx2 - start);
                        if (idx2 < sel.Length && sel[idx2] == '(')
                        {
                            idx2++;
                            var parenStart = idx2;
                            int depth = 1;
                            while (idx2 < sel.Length && depth > 0) { if (sel[idx2] == '(') depth++; if (sel[idx2] == ')') depth--; idx2++; }
                            var pseudoArg = sel.Substring(parenStart, idx2 - parenStart - 1);
                            matchPseudo.Add(pseudoName + "(" + pseudoArg + ")");
                        }
                        else
                        {
                            matchPseudo.Add(pseudoName);
                        }
                    }
                    else if (matchTag == null && IsSelectorChar(c))
                    {
                        // tag name
                        var start = idx2;
                        while (idx2 < sel.Length && IsSelectorChar(sel[idx2])) idx2++;
                        matchTag = sel.Substring(start, idx2 - start);
                    }
                    else
                    {
                        idx2++;
                    }
                }

                // Match tag
                if (matchTag != null && !string.Equals(n.Tag, matchTag, StringComparison.OrdinalIgnoreCase))
                    return false;

                // Match id
                if (matchId != null)
                {
                    string idVal;
                    if (n.Attr == null || !n.Attr.TryGetValue("id", out idVal) || !string.Equals(idVal, matchId, StringComparison.Ordinal))
                        return false;
                }

                // Match classes
                foreach (var cls in matchClasses)
                {
                    string classVal;
                    if (n.Attr == null || !n.Attr.TryGetValue("class", out classVal) || string.IsNullOrEmpty(classVal))
                        return false;
                    var parts = classVal.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    bool found = false;
                    for (int i = 0; i < parts.Length; i++)
                        if (string.Equals(parts[i], cls, StringComparison.Ordinal)) { found = true; break; }
                    if (!found) return false;
                }

                // Match attributes
                foreach (var attr in matchAttrs)
                {
                    if (n.Attr == null) return false;
                    string av;
                    if (!n.Attr.TryGetValue(attr.Name, out av)) return false;
                    if (attr.Op == null) continue; // presence check passed
                    if (attr.Op == "=" && !string.Equals(av, attr.Value, StringComparison.Ordinal)) return false;
                    if (attr.Op == "^=" && (av == null || !av.StartsWith(attr.Value, StringComparison.Ordinal))) return false;
                    if (attr.Op == "$=" && (av == null || !av.EndsWith(attr.Value, StringComparison.Ordinal))) return false;
                    if (attr.Op == "*=" && (av == null || av.IndexOf(attr.Value, StringComparison.Ordinal) < 0)) return false;
                    if (attr.Op == "~=")
                    {
                        var parts = (av ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        bool found = false;
                        for (int i = 0; i < parts.Length; i++)
                            if (string.Equals(parts[i], attr.Value, StringComparison.Ordinal)) { found = true; break; }
                        if (!found) return false;
                    }
                    if (attr.Op == "|=" && (av == null || (!string.Equals(av, attr.Value, StringComparison.Ordinal) && !av.StartsWith(attr.Value + "-", StringComparison.Ordinal)))) return false;
                }

                // Match pseudo-classes
                foreach (var pseudo in matchPseudo)
                {
                    if (pseudo == "first-child")
                    {
                        var parent = FindParent(n);
                        if (parent == null || parent.Children.Count == 0 || parent.Children[0] != n) return false;
                    }
                    else if (pseudo == "last-child")
                    {
                        var parent = FindParent(n);
                        if (parent == null || parent.Children.Count == 0 || parent.Children[parent.Children.Count - 1] != n) return false;
                    }
                    else if (pseudo.StartsWith("nth-child("))
                    {
                        var arg = pseudo.Substring(10, pseudo.Length - 11).Trim();
                        int nth;
                        if (!int.TryParse(arg, out nth)) return false;
                        var parent = FindParent(n);
                        if (parent == null) return false;
                        var childIdx = parent.Children.IndexOf(n) + 1; // 1-based
                        if (childIdx != nth) return false;
                    }
                    else if (pseudo.StartsWith("nth-last-child("))
                    {
                        var arg = pseudo.Substring(15, pseudo.Length - 16).Trim();
                        int nth;
                        if (!int.TryParse(arg, out nth)) return false;
                        var parent = FindParent(n);
                        if (parent == null) return false;
                        var childIdx = parent.Children.Count - parent.Children.IndexOf(n); // 1-based from end
                        if (childIdx != nth) return false;
                    }
                    else if (pseudo == "empty")
                    {
                        if (n.Children.Count > 0) return false;
                    }
                    else if (pseudo.StartsWith(":not("))
                    {
                        var inner = pseudo.Substring(5, pseudo.Length - 6);
                        if (MatchesSimpleSelector(n, inner)) return false;
                    }
                    // :hover, :focus, :active — always false in static rendering
                    else if (pseudo == "hover" || pseudo == "focus" || pseudo == "active" || pseudo == "visited" || pseudo == "link")
                    {
                        return false;
                    }
                    // :root — match if parent is DOCUMENT
                    else if (pseudo == "root")
                    {
                        return n.Parent != null && n.Parent.Tag == "#document";
                    }
                }

                return true;
            }

            private static bool IsSelectorChar(char c)
            {
                return char.IsLetterOrDigit(c) || c == '-' || c == '_';
            }

            private struct AttrSelector
            {
                public string Name;
                public string Op; // =, ^=, $=, *=, ~=, |=, or null for presence
                public string Value;
            }

            private static LiteElement FindParent(LiteElement n)
            {
                if (n == null) return null;
                return n.Parent;
            }

            // Public helper used by tests to run selector queries against a LiteElement root
            public static class QueryHelper
            {
                public static LiteElement[] QuerySelectorAll(LiteElement root, string sel)
                {
                    if (root == null || string.IsNullOrWhiteSpace(sel)) return new LiteElement[0];
                    sel = sel.Trim();
                    var list = new List<LiteElement>();
                    if (sel.StartsWith("#"))
                    {
                        var id = sel.Substring(1);
                        var e = root.FindById(id);
                        return e == null ? new LiteElement[0] : new[] { e };
                    }
                    else if (sel.StartsWith("."))
                    {
                        var cls = sel.Substring(1);
                        foreach (var n in root.Descendants()) if (n.Classes.Contains(cls, StringComparer.OrdinalIgnoreCase)) list.Add(n);
                        return list.ToArray();
                    }

                    if (sel.Contains(" "))
                    {
                        var parts = sel.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        var current = new List<LiteElement> { root };
                        foreach (var p in parts)
                        {
                            var next = new List<LiteElement>();
                            foreach (var c in current)
                                foreach (var d in c.Descendants())
                                    if (MatchesSimpleSelector(d, p)) next.Add(d);
                            current = next;
                        }
                        return current.ToArray();
                    }

                    foreach (var n in root.Descendants()) if (MatchesSimpleSelector(n, sel)) list.Add(n);
                    return list.ToArray();
                }
            }

            // inside class JsDocument
            public JsDomElement createElement(string tag)
            {
                if (string.IsNullOrWhiteSpace(tag)) return null;

                // Just create the element. Do NOT assign to el.Attr (setter is non-public).
                var el = new LiteElement(tag.ToLowerInvariant());
                return new JsDomElement(_e, el);
            }

            public JsDomText createTextNode(string text)
            {
                var t = new LiteElement("#text");
                t.Text = text ?? "";
                return new JsDomText(_e, t);
            }

            public JsDomElement body
            {
                get
                {
                    foreach (var n in _root.Children) if (n.Tag == "body") return new JsDomElement(_e, n);
                    // fallback to root
                    return new JsDomElement(_e, _root);
                }
            }

            // naive append to <body> (or root)
            public void appendChild(object child)
            {
                if (!_e.SandboxAllows(SandboxFeature.DomMutation, "document.appendChild")) return;
                var j = child as JsDomNodeBase;
                if (j == null) return;
                var host = body;
                host._node.Children.Add(j._node);
                try
                {
                    lock (_e._mutationLock)
                    {
                        _e._pendingMutations.Add(new InternalMutationRecord { Type = "childList", Target = host._node, Added = new List<LiteElement> { j._node } });
                    }
                }
                catch { /* swallow */ }
                _e.RequestRepaint();
            }

            public void removeChild(object child)
            {
                if (!_e.SandboxAllows(SandboxFeature.DomMutation, "document.removeChild")) return;
                var j = child as JsDomNodeBase;
                if (j == null) return;
                var host = body;
                try
                {
                    if (host != null && host._node != null)
                    {
                        host._node.Children.Remove(j._node);
                        lock (_e._mutationLock)
                        {
                            _e._pendingMutations.Add(new InternalMutationRecord { Type = "childList", Target = host._node, Removed = new List<LiteElement> { j._node } });
                        }
                        _e.RequestRepaint();
                    }
                }
                catch { /* swallow */ }
            }
        }

        private abstract class JsDomNodeBase
        {
            protected readonly JavaScriptEngine _e;
            internal readonly LiteElement _node;
            protected JsDomNodeBase(JavaScriptEngine e, LiteElement n) { _e = e; _node = n; }
        }

        private sealed class JsDomText : JsDomNodeBase
        {
            public JsDomText(JavaScriptEngine e, LiteElement n) : base(e, n) { }
            public int nodeType => 3;
            public string nodeName => "#text";
            public string data { get { return _node.Text ?? ""; } set { _node.Text = value ?? ""; } }
            public string nodeValue { get { return data; } set { data = value; } }
            public string textContent { get { return data; } set { data = value; } }
            public object parentNode
            {
                get
                {
                    var p = _node.Parent;
                    return p != null ? new JsDomElement(_e, p) : null;
                }
            }
            public object nextSibling
            {
                get
                {
                    var p = _node.Parent;
                    if (p == null) return null;
                    var idx = p.Children.IndexOf(_node);
                    if (idx < 0 || idx >= p.Children.Count - 1) return null;
                    for (int i = idx + 1; i < p.Children.Count; i++)
                        if (p.Children[i] != null) return new JsDomElement(_e, p.Children[i]);
                    return null;
                }
            }
            public object previousSibling
            {
                get
                {
                    var p = _node.Parent;
                    if (p == null) return null;
                    var idx = p.Children.IndexOf(_node);
                    if (idx <= 0) return null;
                    for (int i = idx - 1; i >= 0; i--)
                        if (p.Children[i] != null) return new JsDomElement(_e, p.Children[i]);
                    return null;
                }
            }
            public object ownerDocument => new HostDocument(_e);
        }

        private sealed class JsDomElement : JsDomNodeBase
        {
            public JsDomElement(JavaScriptEngine e, LiteElement n) : base(e, n) { }
            public string tagName => (_node.Tag ?? "").ToUpperInvariant();
            public string nodeName => (_node.Tag ?? "").ToUpperInvariant();

            public string nodeType => _node.IsText ? "3" : "1";
            public string namespaceURI
            {
                get
                {
                    var tag = (_node.Tag ?? "").ToLowerInvariant();
                    if (tag == "svg" || tag == "g" || tag == "path" || tag == "circle" || tag == "ellipse" || tag == "line" || tag == "polyline" || tag == "polygon" || tag == "rect" || tag == "text" || tag == "tspan" || tag == "defs" || tag == "use" || tag == "symbol" || tag == "clipPath" || tag == "mask" || tag == "linearGradient" || tag == "radialGradient" || tag == "stop" || tag == "image" || tag == "foreignObject" || tag == "#document-fragment")
                        return "http://www.w3.org/2000/svg";
                    return "http://www.w3.org/1999/xhtml";
                }
            }
            public string textContent
            {
                get { return CollectText(_node); }
                set
                {
                    _node.RemoveAllChildren();
                    if (!string.IsNullOrEmpty(value))
                    {
                        var t = new LiteElement("#text") { Text = value };
                        _node.Children.Add(t);
                    }
                    _e.RequestRepaint();
                }
            }
            public string className
            {
                get
                {
                    if (_node.Attr == null) return null;
                    string v; return _node.Attr.TryGetValue("class", out v) ? v : null;
                }
                set
                {
                    _node.SetAttribute("class", value ?? "");
                    _e.RequestRepaint();
                }
            }
            public object parentNode
            {
                get
                {
                    var p = _node.Parent;
                    return p != null ? new JsDomElement(_e, p) : null;
                }
            }
            public object parentElement
            {
                get
                {
                    var p = _node.Parent;
                    if (p == null || p.IsText) return null;
                    return new JsDomElement(_e, p);
                }
            }
            public object nextSibling
            {
                get
                {
                    var p = _node.Parent;
                    if (p == null) return null;
                    var idx = p.Children.IndexOf(_node);
                    if (idx < 0 || idx >= p.Children.Count - 1) return null;
                    for (int i = idx + 1; i < p.Children.Count; i++)
                        if (!p.Children[i].IsText) return new JsDomElement(_e, p.Children[i]);
                    return null;
                }
            }
            public object nextElementSibling
            {
                get
                {
                    var p = _node.Parent;
                    if (p == null) return null;
                    var idx = p.Children.IndexOf(_node);
                    if (idx < 0 || idx >= p.Children.Count - 1) return null;
                    for (int i = idx + 1; i < p.Children.Count; i++)
                        if (!p.Children[i].IsText) return new JsDomElement(_e, p.Children[i]);
                    return null;
                }
            }
            public object previousSibling
            {
                get
                {
                    var p = _node.Parent;
                    if (p == null) return null;
                    var idx = p.Children.IndexOf(_node);
                    if (idx <= 0) return null;
                    for (int i = idx - 1; i >= 0; i--)
                        if (!p.Children[i].IsText) return new JsDomElement(_e, p.Children[i]);
                    return null;
                }
            }
            public object previousElementSibling
            {
                get
                {
                    var p = _node.Parent;
                    if (p == null) return null;
                    var idx = p.Children.IndexOf(_node);
                    if (idx <= 0) return null;
                    for (int i = idx - 1; i >= 0; i--)
                        if (!p.Children[i].IsText) return new JsDomElement(_e, p.Children[i]);
                    return null;
                }
            }
            public object firstChild
            {
                get
                {
                    if (_node.Children == null || _node.Children.Count == 0) return null;
                    for (int i = 0; i < _node.Children.Count; i++)
                        if (_node.Children[i] != null) return new JsDomElement(_e, _node.Children[i]);
                    return null;
                }
            }
            public object lastChild
            {
                get
                {
                    if (_node.Children == null || _node.Children.Count == 0) return null;
                    for (int i = _node.Children.Count - 1; i >= 0; i--)
                        if (_node.Children[i] != null) return new JsDomElement(_e, _node.Children[i]);
                    return null;
                }
            }
            public object[] children
            {
                get
                {
                    if (_node.Children == null || _node.Children.Count == 0) return new object[0];
                    var list = new List<object>();
                    for (int i = 0; i < _node.Children.Count; i++)
                    {
                        var ch = _node.Children[i];
                        if (ch != null && !ch.IsText) list.Add(new JsDomElement(_e, ch));
                    }
                    return list.ToArray();
                }
            }
            public object[] childNodes
            {
                get
                {
                    if (_node.Children == null || _node.Children.Count == 0) return new object[0];
                    var list = new List<object>();
                    for (int i = 0; i < _node.Children.Count; i++)
                    {
                        var ch = _node.Children[i];
                        if (ch != null) list.Add(new JsDomElement(_e, ch));
                    }
                    return list.ToArray();
                }
            }
            public object ownerDocument => new HostDocument(_e);

            // inside class JsDomElement
            public string id
            {
                get
                {
                    if (_node.Attr == null) return null;
                    string v;
                    return _node.Attr.TryGetValue("id", out v) ? v : null;
                }
                set
                {
                    // Attr's setter is inaccessible; only mutate the existing dictionary.
                    try { _node.SetAttribute("id", value ?? ""); } catch { /* swallow */ }
                }
            }

            public string innerText
            {
                get { return CollectText(_node); }
                set
                {
                    // keep the element node as-is; replace its children with a single #text child
                    _node.Children.Clear();

                    var textNode = new LiteElement("#text") { Text = value ?? "" };
                    _node.Children.Add(textNode);

                    try
                    {
                        lock (_e._mutationLock)
                        {
                            _e._pendingMutations.Add(new InternalMutationRecord { Type = "childList", Target = _node, Added = new List<LiteElement> { textNode } });
                        }
                    }
                    catch { /* swallow */ }

                    _e.RequestRepaint();
                }
            }

            public string innerHTML
            {
                get
                {
                    try { return SerializeChildren(_node); } catch { return innerText; }
                }
                set
                {
                    try
                    {
                        if (!_e.SandboxAllows(SandboxFeature.DomMutation, "innerHTML")) return;
                        var html = value ?? string.Empty;
                        // Parse fragment and replace children
                        var parser = new HtmlLiteParser(html);
                        var doc = parser.Parse();
                        // choose body children if present; otherwise use document children
                        LiteElement container = null;
                        try
                        {
                            container = (doc.QueryByTag("body") ?? System.Linq.Enumerable.Empty<LiteElement>()).FirstOrDefault();
                            if (container == null) container = doc;
                        }
                        catch { container = doc; }

                        var removedList = new List<LiteElement>(_node.Children);
                        _node.RemoveAllChildren();
                        var addedList = new List<LiteElement>();
                        foreach (var ch in container.Children)
                        {
                            var clone = CloneTree(ch);
                            _node.Append(clone);
                            addedList.Add(clone);
                        }

                        try
                        {
                            lock (_e._mutationLock)
                            {
                                _e._pendingMutations.Add(new InternalMutationRecord
                                {
                                    Type = "childList",
                                    Target = _node,
                                    Added = addedList,
                                    Removed = removedList
                                });
                            }
                        }
                        catch { /* swallow */ }

                        // Optionally execute inline <script> blocks in the assigned fragment
                        if (_e.ExecuteInlineScriptsOnInnerHTML)
                        {
                            try
                            {
                                foreach (var s in _node.SelfAndDescendants())
                                {
                                    if (!string.Equals(s.Tag, "script", StringComparison.OrdinalIgnoreCase)) continue;
                                    // skip external scripts here
                                    if (s.Attr != null && s.Attr.ContainsKey("src")) continue;
                                    var code = s.CollectText();
                                    if (!string.IsNullOrWhiteSpace(code))
                                    {
                                        try { _e.RunInline(code, new JsContext { BaseUri = _e._ctx?.BaseUri }); } catch { /* swallow */ }
                                    }
                                }
                            }
                            catch { /* swallow */ }
                        }
                    }
                    catch { /* swallow */ }

                    _e.RequestRepaint();
                }
            }

            // Per-call override to control script execution when assigning innerHTML
            public void setInnerHTML(string html, bool executeScripts)
            {
                bool prev = _e.ExecuteInlineScriptsOnInnerHTML;
                try
                {
                    _e.ExecuteInlineScriptsOnInnerHTML = executeScripts;
                    this.innerHTML = html ?? string.Empty;
                }
                finally { _e.ExecuteInlineScriptsOnInnerHTML = prev; }
            }

            public void insertAdjacentHTML(string position, string html)
            {
                try
                {
                    var pos = (position ?? "").Trim().ToLowerInvariant();
                    var parser = new HtmlLiteParser(html ?? string.Empty);
                    var frag = parser.Parse();
                    LiteElement container = null;
                    try
                    {
                        container = (frag.QueryByTag("body") ?? System.Linq.Enumerable.Empty<LiteElement>()).FirstOrDefault();
                        if (container == null) container = frag;
                    }
                    catch { container = frag; }

                    var added = new List<LiteElement>();
                    if (pos == "afterbegin")
                    {
                        for (int i = container.Children.Count - 1; i >= 0; i--) { var clone = CloneTree(container.Children[i]); _node.Children.Insert(0, clone); added.Add(clone); }
                    }
                    else if (pos == "beforeend")
                    {
                        for (int i = 0; i < container.Children.Count; i++) { var clone = CloneTree(container.Children[i]); _node.Children.Add(clone); added.Add(clone); }
                    }
                    else if (pos == "beforebegin")
                    {
                        var parent = _node.Parent; if (parent != null)
                        {
                            var idx = parent.Children.IndexOf(_node);
                            for (int i = 0; i < container.Children.Count; i++) { var clone = CloneTree(container.Children[i]); parent.Children.Insert(idx++, clone); added.Add(clone); }
                        }
                    }
                    else if (pos == "afterend")
                    {
                        var parent = _node.Parent; if (parent != null)
                        {
                            var idx = parent.Children.IndexOf(_node) + 1;
                            for (int i = 0; i < container.Children.Count; i++) { var clone = CloneTree(container.Children[i]); parent.Children.Insert(idx++, clone); added.Add(clone); }
                        }
                    }
                    else
                    {
                        // default to beforeend
                        for (int i = 0; i < container.Children.Count; i++) { var clone = CloneTree(container.Children[i]); _node.Children.Add(clone); added.Add(clone); }
                    }

                    try
                    {
                        var target = (pos == "beforebegin" || pos == "afterend") ? (_node.Parent ?? _node) : _node;
                        lock (_e._mutationLock)
                        {
                            _e._pendingMutations.Add(new InternalMutationRecord { Type = "childList", Target = target, Added = added });
                        }
                    }
                    catch { /* swallow */ }

                    _e.RequestRepaint();
                }
                catch { /* swallow */ }
            }

            public void setAttribute(string name, string value)
            {
                if (!_e.SandboxAllows(SandboxFeature.DomMutation, "element.setAttribute")) return;
                if (string.IsNullOrWhiteSpace(name)) return;

                // We can't assign to _node.Attr (setter is inaccessible). Only mutate if it's already created.
                _node.SetAttribute(name, value);

                // record attribute mutation
                try
                {
                    lock (_e._mutationLock)
                    {
                        _e._pendingMutations.Add(new InternalMutationRecord { Type = "attributes", Target = _node, AttributeName = name });
                    }
                }
                catch { /* swallow */ }

                _e.RequestRepaint();
            }

            public string getAttribute(string name)
            {
                if (_node.Attr == null || string.IsNullOrWhiteSpace(name)) return null;
                string v; return _node.Attr.TryGetValue(name, out v) ? v : null;
            }

            public void appendChild(object child)
            {
                if (!_e.SandboxAllows(SandboxFeature.DomMutation, "element.appendChild")) return;
                var j = child as JsDomNodeBase; if (j == null) return;
                _node.Children.Add(j._node);
                try
                {
                    lock (_e._mutationLock) { _e._pendingMutations.Add(new InternalMutationRecord { Type = "childList", Target = _node, Added = new List<LiteElement> { j._node } }); }
                }
                catch { /* swallow */ }
                _e.RequestRepaint();
            }

            public bool hidden
            {
                get
                {
                    if (_node == null) return false;
                    return string.Equals(_node.GetAttribute("hidden"), "hidden", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(_node.GetAttribute("hidden"), "", StringComparison.Ordinal);
                }
                set
                {
                    if (_node == null) return;
                    if (value) _node.SetAttribute("hidden", "hidden");
                    else _node.RemoveAttribute("hidden");
                    _e.RequestRepaint();
                }
            }

            public string value
            {
                get
                {
                    if (_node == null) return null;
                    var tag = (_node.Tag ?? "").ToLowerInvariant();
                    if (tag == "textarea") return CollectText(_node);
                    if (_node.Attr == null) return null;
                    string v; return _node.Attr.TryGetValue("value", out v) ? v : null;
                }
                set
                {
                    if (_node == null) return;
                    var tag = (_node.Tag ?? "").ToLowerInvariant();
                    if (tag == "textarea") { innerText = value ?? ""; return; }
                    _node.SetAttribute("value", value ?? "");
                    try
                    {
                        lock (_e._mutationLock)
                        {
                            _e._pendingMutations.Add(new InternalMutationRecord { Type = "attributes", Target = _node, AttributeName = "value" });
                        }
                    }
                    catch { /* swallow */ }
                    _e.RequestRepaint();
                }
            }

            public void removeChild(object child)
            {
                if (!_e.SandboxAllows(SandboxFeature.DomMutation, "element.removeChild")) return;
                var j = child as JsDomNodeBase; if (j == null) return;
                try
                {
                    _node.Children.Remove(j._node);
                    lock (_e._mutationLock) { _e._pendingMutations.Add(new InternalMutationRecord { Type = "childList", Target = _node, Removed = new List<LiteElement> { j._node } }); }
                }
                catch { /* swallow */ }
                _e.RequestRepaint();
            }

            public void insertBefore(object newChild, object refChild)
            {
                if (!_e.SandboxAllows(SandboxFeature.DomMutation, "element.insertBefore")) return;
                var jNew = newChild as JsDomNodeBase;
                var jRef = refChild as JsDomNodeBase;
                if (jNew == null) return;
                if (jRef == null) { _node.Children.Add(jNew._node); }
                else
                {
                    var idx = _node.Children.IndexOf(jRef._node);
                    if (idx < 0) _node.Children.Add(jNew._node);
                    else _node.Children.Insert(idx, jNew._node);
                }
                try
                {
                    lock (_e._mutationLock) { _e._pendingMutations.Add(new InternalMutationRecord { Type = "childList", Target = _node, Added = new List<LiteElement> { jNew._node } }); }
                }
                catch { }
                _e.RequestRepaint();
            }

            public void replaceChild(object newChild, object oldChild)
            {
                if (!_e.SandboxAllows(SandboxFeature.DomMutation, "element.replaceChild")) return;
                var jNew = newChild as JsDomNodeBase;
                var jOld = oldChild as JsDomNodeBase;
                if (jNew == null || jOld == null) return;
                var idx = _node.Children.IndexOf(jOld._node);
                if (idx < 0) return;
                _node.Children[idx] = jNew._node;
                try
                {
                    lock (_e._mutationLock) { _e._pendingMutations.Add(new InternalMutationRecord { Type = "childList", Target = _node, Removed = new List<LiteElement> { jOld._node }, Added = new List<LiteElement> { jNew._node } }); }
                }
                catch { }
                _e.RequestRepaint();
            }

            public void remove()
            {
                if (!_e.SandboxAllows(SandboxFeature.DomMutation, "element.remove")) return;
                var p = _node.Parent;
                if (p == null) return;
                p.Children.Remove(_node);
                var removed = new List<LiteElement> { _node };
                try
                {
                    lock (_e._mutationLock) { _e._pendingMutations.Add(new InternalMutationRecord { Type = "childList", Target = p, Removed = removed }); }
                }
                catch { }
                _e.RequestRepaint();
            }

            public bool contains(object other)
            {
                var j = other as JsDomNodeBase;
                if (j == null || j._node == null) return false;
                if (j._node == _node) return true;
                foreach (var d in _node.Descendants())
                    if (d == j._node) return true;
                return false;
            }

            public object cloneNode(bool deep)
            {
                if (deep) return CloneTree(_node) != null ? new JsDomElement(_e, CloneTree(_node)) : null;
                var c = new LiteElement(_node.Tag);
                try
                {
                    if (_node.Attr != null) c.CopyAttributesFrom(_node);
                }
                catch { }
                return new JsDomElement(_e, c);
            }

            public bool matches(string selector)
            {
                if (string.IsNullOrWhiteSpace(selector)) return false;
                return JsDocument.MatchesSimpleSelector(_node, selector);
            }
            public object closest(string selector)
            {
                if (string.IsNullOrWhiteSpace(selector)) return null;
                var n = _node;
                while (n != null)
                {
                    if (JsDocument.MatchesSimpleSelector(n, selector))
                        return new JsDomElement(_e, n);
                    n = n.Parent;
                }
                return null;
            }
            public void scrollIntoView(object arg) { }

            // Offset properties – use explicit width/height and CSS left/top when available.
            public double offsetTop
            {
                get
                {
                    // Prefer "top" style or attribute, otherwise 0.
                    if (_node == null) return 0;
                    string top;
                    if (_node.Attr != null && _node.Attr.TryGetValue("top", out top) && TryParseNumeric(top, out var val))
                        return val;
                    var style = _node.GetAttribute("style");
                    if (!string.IsNullOrEmpty(style))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(style, @"\btop\s*:\s*([\d.]+)(px|em|rem)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                            return parsed;
                    }
                    return 0;
                }
            }

            public double offsetLeft
            {
                get
                {
                    // Prefer "left" style or attribute, otherwise 0.
                    if (_node == null) return 0;
                    string left;
                    if (_node.Attr != null && _node.Attr.TryGetValue("left", out left) && TryParseNumeric(left, out var val))
                        return val;
                    var style = _node.GetAttribute("style");
                    if (!string.IsNullOrEmpty(style))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(style, @"\bleft\s*:\s*([\d.]+)(px|em|rem)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                            return parsed;
                    }
                    return 0;
                }
            }
            public double offsetWidth
            {
                get
                {
                    if (_node == null) return 0;
                    string w;
                    if (_node.Attr != null && _node.Attr.TryGetValue("width", out w) && TryParseNumeric(w, out var parsed))
                        return parsed;
                    var style = _node.GetAttribute("style");
                    if (!string.IsNullOrEmpty(style))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(style, @"\bwidth\s*:\s*([\d.]+)(px|em|rem)?");
                        if (m.Success && TryParseNumeric(m.Groups[1].Value, out parsed))
                            return parsed;
                    }
                    return 0;
                }
            }
            public double offsetHeight
            {
                get
                {
                    if (_node == null) return 0;
                    string h;
                    if (_node.Attr != null && _node.Attr.TryGetValue("height", out h) && double.TryParse(h, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                        return parsed;
                    var style = _node.GetAttribute("style");
                    if (!string.IsNullOrEmpty(style))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(style, @"\bheight\s*:\s*(\d+(?:\.\d+)?)(px|em|rem)?");
                        if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out parsed))
                            return parsed;
                    }
                    return 0;
                }
            }
            public object offsetParent
            {
                get
                {
                    if (_node == null) return null;
                    if (HasPositionFixed(_node)) return null;
                    for (var p = _node.Parent; p != null; p = p.Parent)
                    {
                        if (HasPositionFixed(p)) return null;
                        if (!string.Equals(p.Tag, "body", StringComparison.OrdinalIgnoreCase) && HasPositionedStyle(p))
                            return new JsDomElement(_e, p);
                        if (string.Equals(p.Tag, "body", StringComparison.OrdinalIgnoreCase))
                            return new JsDomElement(_e, p);
                    }
                    return null;
                }
            }
            private static bool HasPositionFixed(LiteElement n)
            {
                var style = n.GetAttribute("style");
                if (string.IsNullOrEmpty(style)) return false;
                return System.Text.RegularExpressions.Regex.IsMatch(style, @"\bposition\s*:\s*fixed");
            }
            private static bool HasPositionedStyle(LiteElement n)
            {
                var style = n.GetAttribute("style");
                if (string.IsNullOrEmpty(style)) return false;
                return System.Text.RegularExpressions.Regex.IsMatch(style, @"\bposition\s*:\s*(?:relative|absolute|fixed|sticky)");
            }
            public double clientWidth => 0;
            public double clientHeight => 0;
            public double scrollWidth => 0;
            public double scrollHeight => 0;
            public double scrollTop { get => 0; set { } }
            public double scrollLeft { get => 0; set { } }

            public object[] getElementsByTagName(string tag)
            {
                if (string.IsNullOrEmpty(tag) || _node == null) return new object[0];
                var list = new List<object>();
                foreach (var n in _node.Descendants())
                    if (!n.IsText && string.Equals(n.Tag, tag, StringComparison.OrdinalIgnoreCase))
                        list.Add(new JsDomElement(_e, n));
                return list.ToArray();
            }
            public object[] getElementsByClassName(string className)
            {
                if (string.IsNullOrEmpty(className) || _node == null) return new object[0];
                var list = new List<object>();
                foreach (var n in _node.Descendants())
                {
                    if (n.IsText) continue;
                    string cls; if (n.Attr != null && n.Attr.TryGetValue("class", out cls) && !string.IsNullOrEmpty(cls))
                    {
                        var parts = cls.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < parts.Length; i++)
                            if (string.Equals(parts[i], className, StringComparison.Ordinal)) { list.Add(new JsDomElement(_e, n)); break; }
                    }
                }
                return list.ToArray();
            }

            public void setAttributeNS(string ns, string name, string value)
            {
                setAttribute(name, value);
            }

            public string getAttributeNS(string ns, string name)
            {
                return getAttribute(name);
            }

            public bool hasChildNodes()
            {
                return _node.Children != null && _node.Children.Count > 0;
            }

            public object querySelector(string sel) { return new JsDocument(_e, _node).querySelector(sel); }
            public object[] querySelectorAll(string sel) { return new JsDocument(_e, _node).querySelectorAll(sel); }

            private static string CollectText(LiteElement n)
            {
                if (n == null) return "";
                if (n.IsText) return n.Text ?? "";
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < n.Children.Count; i++) sb.Append(CollectText(n.Children[i]));
                return sb.ToString();
            }
            private static LiteElement CloneTree(LiteElement n)
            {
                if (n == null) return null;
                var c = new LiteElement(n.Tag);
                c.Text = n.Text;
                try
                {
                    if (n.Attr != null)
                    {
                        c.CopyAttributesFrom(n);
                    }
                }
                catch { /* swallow */ }
                try
                {
                    for (int i = 0; i < n.Children.Count; i++)
                    {
                        var childClone = CloneTree(n.Children[i]);
                        if (childClone != null) c.Append(childClone);
                    }
                }
                catch { /* swallow */ }
                return c;
            }

            private static string SerializeChildren(LiteElement n)
            {
                var sb = new System.Text.StringBuilder();
                try
                {
                    for (int i = 0; i < n.Children.Count; i++) SerializeNode(n.Children[i], sb);
                }
                catch { /* swallow */ }
                return sb.ToString();
            }

            private static void SerializeNode(LiteElement n, System.Text.StringBuilder sb)
            {
                if (n == null || sb == null) return;
                if (n.IsText) { sb.Append(EscapeHtml(n.Text ?? "")); return; }
                var tag = n.Tag ?? "";
                sb.Append('<').Append(tag);
                try
                {
                    if (n.Attr != null)
                    {
                        foreach (var kv in n.Attr)
                        {
                            if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                            var original = n.GetOriginalAttributeName(kv.Key) ?? kv.Key;
                            sb.Append(' ').Append(original).Append('=').Append('"').Append(EscapeHtml(kv.Value ?? "")).Append('"');
                        }
                    }
                }
                catch { /* swallow */ }
                // void tags
                if (LiteDomUtil.IsVoid(tag)) { sb.Append('/').Append('>'); return; }
                sb.Append('>');
                // Raw-text containers: script/style/textarea should not HTML-escape their text content
                if (string.Equals(tag, "script", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tag, "style", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tag, "textarea", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        for (int i = 0; i < n.Children.Count; i++)
                        {
                            var ch = n.Children[i];
                            if (ch != null && ch.IsText) sb.Append(ch.Text ?? "");
                            else SerializeNode(ch, sb);
                        }
                    }
                    catch { /* swallow */ }
                }
                else
                {
                    for (int i = 0; i < n.Children.Count; i++) SerializeNode(n.Children[i], sb);
                }
                sb.Append("</").Append(tag).Append('>');
            }

            private static string EscapeHtml(string s)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
            }
            private static void SanitizeForScriptingEnabled(JavaScriptEngine self, LiteElement rootArg = null)
            {
                if (self == null) return;
                var root = rootArg ?? self._domRoot;
                if (root == null) return;

                Action<LiteElement> flipClass = n =>
                {
                    if (n == null) return;
                    var attrs = n.Attr;
                    if (attrs == null) return;

                    string cls;
                    if (!attrs.TryGetValue("class", out cls) || string.IsNullOrWhiteSpace(cls)) return;

                    var parts = new HashSet<string>(
                        cls.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries),
                        StringComparer.OrdinalIgnoreCase);

                    var changed = false;
                    if (parts.Remove("no-js")) changed = true;
                    if (!parts.Contains("js")) { parts.Add("js"); changed = true; }
                    if (changed) attrs["class"] = string.Join(" ", parts.ToArray());
                };

                try
                {
                    var html = (root.QueryByTag("html") ?? Enumerable.Empty<LiteElement>()).FirstOrDefault();
                    if (html != null) flipClass(html);
                    var body = (root.QueryByTag("body") ?? Enumerable.Empty<LiteElement>()).FirstOrDefault();
                    if (body != null) flipClass(body);
                }
                catch { /* swallow */ }

                // Do NOT remove <noscript> on this platform; we are a limited JS renderer
                // and <noscript> often contains the only usable fallback content.
                // Leave nodes intact so the renderer can show them.

                self.RequestRepaint();
            }

        }

    }

    // ------------------- Host interface + context (stable) -------------------

    public interface IJsHost
    {
        void Navigate(Uri target);
        void PostForm(Uri target, string body);
        void SetStatus(string s);
        void SetTitle(string tval);
    }

    // Optional host interface: if implemented, JavaScriptEngine will call RequestRender() when DOM changes
    public interface IJsHostRepaint
    {
        // Request the host to schedule a UI re-render. Implementations should marshal to UI thread.
        void RequestRender();

        // Optional: host can provide a helper to invoke code on the UI thread. Timers and
        // other background callbacks should use this to safely mutate UI-bound data.
        void InvokeOnUiThread(Action action);
    }

    public sealed class JsContext
    {
        public Uri BaseUri { get; set; }
    }

    /// <summary>Convenience adapter so callers can pass delegates.</summary>
    public sealed class JsHostAdapter : IJsHost, IJsHostRepaint
    {
    private readonly Action<Uri> _navigate;
    private readonly Action<Uri, string> _post;
    private readonly Action<string> _status;
    private readonly Action _requestRender;
    private readonly Action<Action> _invokeOnUiThread;
    private readonly Action<string> _setTitle;

        public JsHostAdapter(Action<Uri> navigate, Action<Uri, string> post, Action<string> status, Action requestRender = null, Action<Action> invokeOnUiThread = null, Action<string> setTitle = null)
        {
            _navigate = navigate ?? (_ => { });
            _post = post ?? ((_, __) => { });
            _status = status ?? (_ => { });
            _requestRender = requestRender ?? (() => { try { _status("[DOM mutated]"); } catch { /* swallow */ } });
            _invokeOnUiThread = invokeOnUiThread ?? (a => { try { a(); } catch { /* swallow */ } });
            _setTitle = setTitle ?? (_ => { });
        }

        public void Navigate(Uri target) => _navigate(target);
        public void PostForm(Uri target, string body) => _post(target, body);
        public void SetStatus(string s) => _status(s);

        // IJsHostRepaint implementation
        public void RequestRender() { try { _requestRender(); } catch { /* swallow */ } }
        public void InvokeOnUiThread(Action action) { if (action == null) return; try { _invokeOnUiThread(action); } catch { /* swallow */ } }

        public void SetTitle(string tval)
        {
            try { _setTitle(tval ?? string.Empty); }
            catch { /* swallow */ }
        }
    }
}
