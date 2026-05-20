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
using Math = System.Math;
#if USE_ECMA_EXPERIMENTAL
using EcmaEngine.Interpreter;
using EcmaEngine.Runtime;
#endif
using NiL.JS.Core;
using NiL.JS.BaseLibrary;

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

        // Optional: Jint ES5 runtime integration (guarded by USE_JINT)
#if USE_JINT && !WINDOWS_PHONE_APP
        private Jint.Engine _jint;
#endif
#if USE_NILJS
        private GlobalContext _nil;
#endif
#if USE_ECMA_EXPERIMENTAL
        private JsInterpreter _exp;
        public bool UseExperimentalEcmaEngine { get; set; } = false;
#endif
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

        internal bool SandboxAllows(SandboxFeature feature, string detail = null)
        {
            if (_sandbox.Allows(feature)) return true;
            RecordSandboxBlock(feature, detail);
            return false;
        }

        private void RecordSandboxBlock(SandboxFeature feature, string detail)
        {
            var messageDetail = detail ?? string.Empty;
            try { TraceFeatureGap("Sandbox", feature.ToString(), messageDetail); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
            try
            {
                var status = string.IsNullOrWhiteSpace(messageDetail)
                    ? "[Sandbox] Blocked " + feature
                    : "[Sandbox] Blocked " + feature + " : " + messageDetail;
                _host?.SetStatus(status);
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }

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
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                try { var wrs = _visualRoot; if (wrs != null) root = wrs.Target as Windows.UI.Xaml.UIElement; } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                if (root == null)
                    root = Windows.UI.Xaml.Window.Current != null ? Windows.UI.Xaml.Window.Current.Content as Windows.UI.Xaml.UIElement : null;
                if (root != null)
                {
                    var p = fe.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0));
                    x = p.X; y = p.Y; return true;
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
            return false;
        }
        public static void RegisterVisualRoot(Windows.UI.Xaml.UIElement root)
        {
            try { _visualRoot = (root != null ? new System.WeakReference(root) : null); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
        }
        // ---- Phase 1/2/3 state ----
        private readonly Dictionary<string, List<string>> _evtDoc = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _evtWin = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
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
                List<string> list; if (_evtDoc.TryGetValue(evt, out list) && list != null)
                {
                    foreach (var fn in list.ToArray())
                    {
                        EnqueueMicrotask(() => { try { RunInline(fn + "({ type:'" + evt + "', target:'document' })", _ctx, evt, "document"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
                    }
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }

        }

        private void FireWindowEvent(string evt)
        {
            try
            {
                List<string> list; if (_evtWin.TryGetValue(evt, out list) && list != null)
                {
                    foreach (var fn in list.ToArray())
                    {
                        EnqueueMicrotask(() => { try { RunInline(fn + "({ type:'" + evt + "', target:'window' })", _ctx, evt, "window"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
                    }
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                        catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                    }
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }

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
                        catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                    }
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                        catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                    };
                    if (repaintHost != null) repaintHost.InvokeOnUiThread(run); else run();
                }
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                    try { t.Dispose(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                    Action run = () => { try { RunInline(fnName + "(Date.now&&Date.now()||0)", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } };
                    if (repaintHost != null) repaintHost.InvokeOnUiThread(run); else run();
                }
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                    try { t.Dispose(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                        catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                    }
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
            try { if (prev != null && string.Equals(BaseWithoutFragment(prev), BaseWithoutFragment(u), StringComparison.OrdinalIgnoreCase) && !string.Equals(prev.Fragment ?? "", u.Fragment ?? "", StringComparison.Ordinal)) FireWindowEvent("hashchange"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
        }

        private void HistoryReplace(Uri u)
        {
            if (u == null) return;
            if (!SandboxAllows(SandboxFeature.Navigation, "history.replaceState -> " + (u?.AbsoluteUri ?? ""))) return;
            Uri prev = null; if (_historyIndex >= 0 && _historyIndex < _history.Count) prev = _history[_historyIndex];
            if (_historyIndex < 0) { _history.Add(u); _historyIndex = _history.Count - 1; }
            else _history[_historyIndex] = u;
            try { if (prev != null && string.Equals(BaseWithoutFragment(prev), BaseWithoutFragment(u), StringComparison.OrdinalIgnoreCase) && !string.Equals(prev.Fragment ?? "", u.Fragment ?? "", StringComparison.Ordinal)) FireWindowEvent("hashchange"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
        }

        private void HistoryGo(int delta)
        {
            var target = _historyIndex + delta;
            if (target < 0 || target >= _history.Count) return;
            if (!SandboxAllows(SandboxFeature.Navigation, "history.go(" + delta + ")")) return;
            _historyIndex = target;
            try { _host.Navigate(_history[_historyIndex]); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
#if USE_NILJS
                    return false;
#else
                    return TryReturnPatterns(line);  // "return false;"
#endif
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
                try { _host.SetStatus("setTimeout id=" + id); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                return true;
            }
            var mTO2 = RxSetTimeoutFn.Match(line);
            if (mTO2.Success)
            {
                int ms2 = 0; int.TryParse(mTO2.Groups["ms"].Value, out ms2);
                var fn = mTO2.Groups["fn"].Value;
                var id2 = ScheduleTimeout(fn + "()", ms2);
                try { _host.SetStatus("setTimeout id=" + id2); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                try { _host.SetStatus("setInterval id=" + id); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                return true;
            }
            var mSIF = RxSetIntervalFn.Match(line);
            if (mSIF.Success)
            {
                int ms = 0; int.TryParse(mSIF.Groups["ms"].Value, out ms);
                var fn = mSIF.Groups["fn"].Value;
                var id = ScheduleInterval(fn + "()", ms);
                try { _host.SetStatus("setInterval id=" + id); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
            if (mLog.Success) { try { _host.SetStatus(mLog.Groups["msg"].Value ?? ""); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return true; }
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
            if (RxLocationReload.IsMatch(line)) { try { if (ctx?.BaseUri != null) _host.Navigate(ctx.BaseUri); else RequestRepaint(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return true; }
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
            if (RxLocationReload.IsMatch(line)) { try { if (ctx?.BaseUri != null) _host.Navigate(ctx.BaseUri); else RequestRepaint(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return true; }
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                return true;
            }
            var mClick = System.Text.RegularExpressions.Regex.Match(line,
                @"^\s*document\s*\.\s*getElementById\s*\(\s*(['""])(?<id>[^'""]+)\1\s*\)\s*\.\s*click\s*\(\s*\)\s*;?\s*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (mClick.Success)
            {
                try { RaiseElementEvent(mClick.Groups["id"].Value, "click"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                EnqueueMicrotask(() => { try { RunInline(fn + "()", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
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
                        EnqueueMicrotask(() => { try { RunInline(code, ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
                    }
                }
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                try { _host.SetStatus("serviceWorker.register: no-op"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                            try { RunInline(exec, _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                        }
                        catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                            EnqueueMicrotask(() => { try { RunInline(fn + "(" + respExpr + ")", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
                        }
                        catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                        catch (Exception ex) { try { _host.SetStatus("fetchText failed: " + ex.Message); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }
                    });
                }
                return true;
            }
            return false;
        }

        private bool TryRafPatterns(string line)
        {
            var mRaf = Regex.Match(line, @"^\s*requestAnimationFrame\s*\(\s*(?<fn>[A-Za-z_$][A-Za-z0-9_$]*)\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mRaf.Success) { var id = RequestAnimationFrame(mRaf.Groups["fn"].Value); try { _host.SetStatus("rAF id=" + id); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return true; }
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
#if !USE_NILJS
            var mXdeliver = RxXhrDeliver.Match(line);
            if (mXdeliver.Success)
            {
                var token = mXdeliver.Groups["token"].Value;
                var fn = mXdeliver.Groups["fn"].Value;
                int status; if (!int.TryParse(mXdeliver.Groups["status"].Value, out status)) status = -1;
                XhrDeliver(token, fn, status);
                return true;
            }
#endif
            return false;
        }

        private bool TryGetCookiePatterns(string line, JsContext ctx)
        {
            var mCget = Regex.Match(line, @"^\s*__getCookie\s*\(\s*(?<fn>[A-Za-z_$][A-Za-z0-9_$]*)\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mCget.Success)
            {
                var s = GetCookieString(ctx?.BaseUri ?? _ctx?.BaseUri);
                var esc = JsEscape(s ?? "", '\'');
                EnqueueMicrotask(() => { try { RunInline(mCget.Groups["fn"].Value + "('" + esc + "')", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
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
                                    EnqueueMicrotask(() => { try { RunInline(fnPart.Substring(0, fnPart.Length - ".json_callback".Length) + "(" + literal + ")", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
                                }
                                catch
                                {
                                    var escErr = JsEscape(body, '\'');
                                    EnqueueMicrotask(() => { try { RunInline(fnPart + "('" + escErr + "')", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
                                }
                            }
                            else
                            {
                                var esc = JsEscape(body, '\'');
                                EnqueueMicrotask(() => { try { RunInline(fnPart + "('" + esc + "')", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
                            }
                        }
                    }
                }
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                        EnqueueMicrotask(() => { try { RunInline(fnPart + "('" + body + "')", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
                    }
                }
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                EnqueueMicrotask(() => { try { RunInline(codeToRun, ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
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
                try { _host.SetStatus("setInterval id=" + id); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                return true;
            }
            var mCI = Regex.Match(line, @"^\s*clearInterval\s*\(\s*(?<id>\d+)\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mCI.Success) { int id; if (int.TryParse(mCI.Groups["id"].Value, out id)) ClearInterval(id); return true; }

            // ---------- requestAnimationFrame / cancelAnimationFrame ----------
            var mRaf = Regex.Match(line, @"^\s*requestAnimationFrame\s*\(\s*(?<fn>[A-Za-z_$][A-Za-z0-9_$]*)\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mRaf.Success) { var id = RequestAnimationFrame(mRaf.Groups["fn"].Value); try { _host.SetStatus("rAF id=" + id); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return true; }
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
                var esc = JsEscape(val ?? "", '\''); EnqueueMicrotask(() => { try { RunInline(mLSgetCb.Groups["fn"].Value + "('" + esc + "')", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
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
                try { var _ = SaveLocalStorageAsync(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                var escSS = JsEscape(vSS ?? "", '\''); EnqueueMicrotask(() => { try { RunInline(mSSget.Groups["fn"].Value + "('" + escSS + "')", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
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
                EnqueueMicrotask(() => { try { RunInline(mCget.Groups["fn"].Value + "('" + esc + "')", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
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
                if (u != null) { HistoryPush(u); try { _host.SetStatus("pushState -> " + u); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }
                return true;
            }
            var mRep = Regex.Match(line, @"^\s*history\s*\.replaceState\s*\(\s*.*?,\s*['""](?<url>.*?)['""]\s*\)\s*;?$", RegexOptions.IgnoreCase);
            if (mRep.Success)
            {
                var u = Resolve(ctx?.BaseUri ?? _ctx?.BaseUri, mRep.Groups["url"].Value);
                if (u != null) { HistoryReplace(u); try { _host.SetStatus("replaceState -> " + u); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
            };
            if (repaintHost != null) repaintHost.InvokeOnUiThread(run); else run();
        }
        catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
        finally
        {
            lock (_timers) { try { if (_timers.ContainsKey(id)) { _timers[id].Dispose(); } } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } _timers.Remove(id); }
        }
    };
    t = new System.Threading.Timer(fire, null, ms, System.Threading.Timeout.Infinite);
    lock (_timers) _timers[id] = t;
    try { _host.SetStatus("setTimeout id=" + id); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
    return true;
}
var mCT = System.Text.RegularExpressions.Regex.Match(line, @"^\s*clearTimeout\s*\(\s*(?<id>\d+)\s*\)\s*;?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
if (mCT.Success)
{
    int id; if (int.TryParse(mCT.Groups["id"].Value, out id))
    {
        lock (_timers)
        {
            System.Threading.Timer t; if (_timers.TryGetValue(id, out t)) { try { t.Dispose(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } _timers.Remove(id); }
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
    try { _host.SetStatus(kind + ": " + msg); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
    try { BrowserCore.Engine.DevToolsLogger.Log("[JS:" + kind.ToUpperInvariant() + "] " + msg); } catch { }
    return true;
}

// ---------- location.assign / replace / href= ----------
var mAssign = System.Text.RegularExpressions.Regex.Match(line, @"^\s*location\s*\.\s*assign\s*\(\s*['""](?<u>.+?)['""]\s*\)\s*;?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
if (mAssign.Success)
{
    var u = Resolve(ctx?.BaseUri ?? _ctx?.BaseUri, mAssign.Groups["u"].Value);
    if (u != null) { try { _host.Navigate(u); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }
    return true;
}
var mReplace = System.Text.RegularExpressions.Regex.Match(line, @"^\s*location\s*\.\s*replace\s*\(\s*['""](?<u>.+?)['""]\s*\)\s*;?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
if (mReplace.Success)
{
    var u = Resolve(ctx?.BaseUri ?? _ctx?.BaseUri, mReplace.Groups["u"].Value);
    if (u != null) { try { _host.Navigate(u); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }
    return true;
}
var mHrefSet = System.Text.RegularExpressions.Regex.Match(line, @"^\s*location\s*\.\s*href\s*=\s*['""](?<u>.+?)['""]\s*;?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
if (mHrefSet.Success)
{
    var u = Resolve(ctx?.BaseUri ?? _ctx?.BaseUri, mHrefSet.Groups["u"].Value);
    if (u != null) { try { _host.Navigate(u); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                            try { req.Headers.TryAddWithoutValidation(kv.Key, kv.Value); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }

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
                        string enc = null; try { enc = string.Join(",", resp.Content.Headers.ContentEncoding); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                        EnqueueMicrotask(() => { try { RunInline(st.OnErrorFn + "(" + status + ",'" + escErr + "')", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
                        return;
                    }

                    var bodyOut = text ?? "";
                    if (bodyOut.Length <= _inlineThreshold)
                    {
                        var esc = JsEscape(bodyOut, '\'');
                        EnqueueMicrotask(() => { try { RunInline(st.OnLoadFn + "(" + status + ",'" + esc + "')", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
                    }
                    else
                    {
                        var token = RegisterResponseBody(bodyOut);
                        EnqueueMicrotask(() => { try { RunInline("__xhr_deliver('" + token + "'," + st.OnLoadFn + "," + status + ")", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
                    }
                }
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                finally
                {
                    try { lock (_xhrLock) _xhr.Remove(id); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                }
            });
        }


        // No full JS engine bundled here; we keep a tiny allowlist (JS-0) for inline handlers.
        public JavaScriptEngine(IJsHost host)
        {
            // ***

            try
            {
                // AppDomain.CurrentDomain is not available on UWP (no .NET AppDomain)
                // First-chance exception logging is handled by App.UnhandledException
            }
            catch { }

            // ***
            _host = host ?? new JsHostAdapter(_ => { }, (_, __) => { }, _ => { });
            try { _http = new System.Net.Http.HttpClient(CreateManagedHandler(null)); } catch { _http = new System.Net.Http.HttpClient(); }
            try { var _ = RestoreLocalStorageAsync(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
#if !USE_NILJS
            // start a background cleanup task to evict expired or over-capacity response tokens
            if (!_responseCleanupRunning)
            {
                _responseCleanupRunning = true;
                Task.Run(async () =>
                {
                    try
                    {
                        while (_responseCleanupRunning)
                        {
                            try
                            {
                                lock (_responseLock)
                                {
                                    var now = DateTime.UtcNow;
                                    var expired = _responseRegistry.Where(kv => now - kv.Value.Ts > _responseTtl).Select(kv => kv.Key).ToList();
                                    foreach (var k in expired) { _responseRegistry.Remove(k); _responseLru.Remove(k); }
                                    while (_responseLru.Count > _responseCapacity)
                                    {
                                        var last = _responseLru.Last.Value;
                                        _responseLru.RemoveLast();
                                        _responseRegistry.Remove(last);
                                    }
                                }
                            }
                            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                            await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                        }
                    }
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                });
            }
#endif
#if USE_NILJS
            try
            {
                // NiL.JS context initialized here when compiled with USE_NILJS
                _nilInit();
                _moduleLoader = new ModuleLoader(this, _nil);
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
#endif

#if USE_JINT && !WINDOWS_PHONE_APP
            try
            {
                _jint = new Jint.Engine(cfg => cfg.Strict(false));
                InitJintGlobals();
            }
            catch { _jint = null; }
#endif
#if USE_ECMA_EXPERIMENTAL
            try { _exp = new JsInterpreter(); } catch { _exp = null; }
#endif
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
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
        }

    public void LocalStorageSet(string key, string value, JsContext ctx) { try { if (!SandboxAllows(SandboxFeature.Storage, "localStorage.setItem")) return; var bag = GetLocalStorageFor(ctx?.BaseUri ?? _ctx?.BaseUri); lock(_storageLock) bag[key] = value ?? ""; PersistLocalStorage(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }
    public string LocalStorageGet(string key, JsContext ctx) { try { if (!SandboxAllows(SandboxFeature.Storage, "localStorage.getItem")) return null; var bag = GetLocalStorageFor(ctx?.BaseUri ?? _ctx?.BaseUri); string v=null; lock(_storageLock) bag.TryGetValue(key, out v); return v; } catch { return null; } }
    public void LocalStorageRemove(string key, JsContext ctx) { try { if (!SandboxAllows(SandboxFeature.Storage, "localStorage.removeItem")) return; var bag = GetLocalStorageFor(ctx?.BaseUri ?? _ctx?.BaseUri); lock(_storageLock) bag.Remove(key); PersistLocalStorage(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }
    public void LocalStorageClear(JsContext ctx) { try { if (!SandboxAllows(SandboxFeature.Storage, "localStorage.clear")) return; var bag = GetLocalStorageFor(ctx?.BaseUri ?? _ctx?.BaseUri); lock(_storageLock) bag.Clear(); PersistLocalStorage(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }

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
                    try { hc.DefaultRequestHeaders.UserAgent.ParseAdd("MiniJs/1.0"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                        catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                    });
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
        }

        private JSValue InternalRecordsToJSArray(List<InternalMutationRecord> records)
        {
            var arr = _nil.Eval("[]");
            for (int i = 0; i < records.Count; i++)
            {
                var r = records[i];
                var rec = _nil.Eval("({})");
                rec["type"] = JSValue.Marshal(r.Type ?? "");
                rec["target"] = JSValue.Marshal(TargetDescriptor(r.Target));
                if (r.Type == "childList")
                {
                    var added = _nil.Eval("[]");
                    if (r.Added != null)
                        for (int a = 0; a < r.Added.Count; a++)
                            added[(string)a.ToString()] = JSValue.Marshal(TargetDescriptor(r.Added[a]));
                    rec["addedNodes"] = added;
                    var removed = _nil.Eval("[]");
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

        private class HostWindow
        {
            private JavaScriptEngine _engine;
            public HostWindow(JavaScriptEngine engine) { _engine = engine; }
            public void alert(string msg) { _engine._host.SetStatus("[Alert] " + msg); }
            
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
            public JSValue createElement(string tag)
            {
                if (string.IsNullOrEmpty(tag)) return JSValue.Undefined;
                return JSValue.Marshal(new JsDomElement(_engine, new LiteElement(tag)));
            }
            public string title
            {
                get { return _engine._pageTitle ?? string.Empty; }
                set { _engine._pageTitle = value; try { _engine._host?.SetTitle(value ?? string.Empty); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }
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
        }

        private class HostConsole
        {
            private JavaScriptEngine _engine;
            public HostConsole(JavaScriptEngine engine) { _engine = engine; }
            public void log(string msg) { System.Diagnostics.Debug.WriteLine(msg); }
        }

        private class HostNavigator
        {
            private JavaScriptEngine _engine;
            public HostNavigator(JavaScriptEngine engine) { _engine = engine; }
            public string userAgent => "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
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
#if USE_NILJS
                if (_engine.OnPopState != null && _engine.OnPopState.ValueType == JSValueType.Function)
                {
                    var evt = JSValue.Marshal(new { state = JSValue.Null });
                    (_engine.OnPopState as Function)?.Call(JSValue.Undefined, new Arguments { evt });
                }
#endif
            }

            public int length => 1;
            public JSValue state => JSValue.Null;
        }

        private class HostLocation
        {
            private JavaScriptEngine _engine;
            public HostLocation(JavaScriptEngine engine) { _engine = engine; }
            public string href => _engine._ctx?.BaseUri?.ToString() ?? "";
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

#if USE_NILJS
        // ------------------------------------------------------------------------------------
        // NiL.JS Integration
        // ------------------------------------------------------------------------------------

        private void _nilInit()
        {
            _nil = new GlobalContext();

            // ***
            // create host window once and reuse for aliases
            var hostWindow = new HostWindow(this);
            _nil.DefineVariable("window").Assign(JSValue.Marshal(hostWindow));
            try { _nil.DefineVariable("self").Assign(JSValue.Marshal(hostWindow)); } catch { }
            try { _nil.DefineVariable("globalThis").Assign(JSValue.Marshal(hostWindow)); } catch { }

            // minimal common shims
            try
            {
                _nil.DefineVariable("addEventListener").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Undefined)));
                _nil.DefineVariable("removeEventListener").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Undefined)));
                _nil.DefineVariable("postMessage").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Undefined)));
                _nil.DefineVariable("setImmediate").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(a => JSValue.Undefined)));
            }
            catch { }
            // ***

            // Expose standard globals
            _nil.DefineVariable("window").Assign(JSValue.Marshal(new HostWindow(this)));
            _nil.DefineVariable("document").Assign(JSValue.Marshal(new HostDocument(this)));
            _nil.DefineVariable("console").Assign(JSValue.Marshal(new HostConsole(this)));
            _nil.DefineVariable("navigator").Assign(JSValue.Marshal(new HostNavigator(this)));
            _nil.DefineVariable("location").Assign(JSValue.Marshal(new HostLocation(this)));
            _nil.DefineVariable("history").Assign(JSValue.Marshal(new HostHistory(this)));
            _nil.DefineVariable("localStorage").Assign(JSValue.Marshal(new HostLocalStorage(this, false)));
            _nil.DefineVariable("sessionStorage").Assign(JSValue.Marshal(new HostLocalStorage(this, true)));

            // Expose fetch API (async via Promise-like thenable)
            _nil.DefineVariable("fetch").Assign(JSValue.Marshal(new Func<Arguments, JSValue>(args =>
            {
                if (args.Length == 0) return JSValue.Undefined;
                var url = args[0].ToString();
                var uri = Resolve(_ctx?.BaseUri, url);
                if (uri == null) return JSValue.Undefined;
                var capturedUri = uri;

                var thenable = JSValue.Marshal(new { });
                thenable["then"] = JSValue.Marshal(new Func<Arguments, JSValue>(a =>
                {
                    if (a.Length > 0 && a[0].ValueType == JSValueType.Function)
                    {
                        var onResolve = a[0] as Function;
                        Task.Run(async () =>
                        {
                            try
                            {
                                var result = await FetchAsync(capturedUri).ConfigureAwait(false);
                                EnqueueMacroTask(() =>
                                {
                                    try
                                    {
                                        var resp = JSValue.Marshal(new { });
                                        resp["ok"] = JSValue.Marshal(true);
                                        resp["status"] = JSValue.Marshal(200);
                                        resp["statusText"] = JSValue.Marshal("OK");
                                        resp["text"] = JSValue.Marshal(new Func<Arguments, JSValue>(b => JSValue.Marshal(result ?? "")));
                                        resp["json"] = JSValue.Marshal(new Func<Arguments, JSValue>(b =>
                                        {
                                            try { return _nil.Eval("JSON.parse(" + JsEscape(result ?? "{}", '\'') + ")"); }
                                            catch { return JSValue.Null; }
                                        }));
                                        onResolve.Call(JSValue.Undefined, new Arguments { resp });
                                    }
                                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                                });
                            }
                            catch (Exception ex)
                            {
                                EnqueueMacroTask(() =>
                                {
                                    try
                                    {
                                        var errResp = JSValue.Marshal(new { });
                                        errResp["ok"] = JSValue.Marshal(false);
                                        errResp["status"] = JSValue.Marshal(0);
                                        errResp["statusText"] = JSValue.Marshal("Error");
                                        errResp["text"] = JSValue.Marshal(new Func<Arguments, JSValue>(b => JSValue.Marshal(ex.Message ?? "")));
                                        onResolve.Call(JSValue.Undefined, new Arguments { errResp });
                                    }
                                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                                });
                            }
                        });
                    }
                    return thenable;
                }));
                return thenable;
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
                        EnqueueMacroTask(() => 
                        {
                            try { (func as Function).Call(JSValue.Undefined, new Arguments()); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                        });
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
                        EnqueueMacroTask(() => 
                        {
                            try { (func as Function).Call(JSValue.Undefined, new Arguments()); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                        });
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
#endif

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
            _domRoot = domRoot;
            // JS is "enabled" in this app; hide server noscript overlays & flip no-js ? js
            this.SanitizeForScriptingEnabled(domRoot);
#if USE_NILJS
            try { _nilSyncDocument(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
            
            // Execute inline scripts
            try
            {
                foreach (var s in _domRoot.SelfAndDescendants())
                {
                    if (string.Equals(s.Tag, "script", StringComparison.OrdinalIgnoreCase))
                    {
                        if (s.Attr != null && s.Attr.ContainsKey("src")) continue; // Skip external for now
                        var code = CollectScriptText(s);
                        if (!string.IsNullOrWhiteSpace(code))
                        {
                            RunGlobalScript(code);
                        }
                    }
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
#endif
        }

        private void RunGlobalScript(string js)
        {
            if (string.IsNullOrWhiteSpace(js)) return;
#if USE_NILJS
            try
            {
                if (_nil != null)
                {
                    _nil.Eval(js);
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
#endif
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
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }

            try
            {
                var toRemove = new List<LiteElement>();
                foreach (var n in root.Descendants())
                    if (string.Equals(n.Tag, "noscript", StringComparison.OrdinalIgnoreCase))
                        toRemove.Add(n);
                foreach (var n in toRemove) n.Parent?.Children.Remove(n);
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }

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
                try { ClearTimeout(id); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                EnqueueMacroTask(() =>
                {
                    try { ExecuteCachedInline(compiled); }
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                });
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
                EnqueueMacroTask(() =>
                {
                    try { ExecuteCachedInline(compiled); }
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                });
            }, null, ms, ms);
            lock (_timers) { _timers[id] = t; }
            return id;
        }

        private void ExecuteCachedInline(JsFuncDef def)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.Body)) return;
#if USE_NILJS
            try { _nilSyncDocument(); _nil.Eval(def.Body); } catch { }
#else
            var r = new JsMiniRunner(this);
            r._src = def.Body; r._pos = 0; r._len = r._src.Length; r.SkipWs();
            while (!r.Eof()) { r.ParseStatement(); r.SkipWs(); }
#endif
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
                    try { t.Dispose(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
            }

            try { action(); }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                finally
                {
                    try { DrainMicrotasksInternal(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                try { System.Diagnostics.Debug.WriteLine(message); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                try { _host?.SetStatus(message); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
#if USE_NILJS
            try
            {
                if (_nil != null)
                {
                    _nilSyncDocument();
                    var expr = "(function(){ var __r=(function(){" + js + "})(); return __r===false; })()";
                    var val = _nil.Eval(expr);
                    if (val != null && string.Equals(val.ToString(), "true", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
#endif
#if USE_JINT && !WINDOWS_PHONE_APP
            // Try Jint first for ES5 inline code; wrap to capture return value
            try
            {
                if (_jint != null)
                {
                    // Sync location-href variable
                    _jint.SetValue("__host_location_href", _ctx?.BaseUri?.AbsoluteUri ?? string.Empty);
                    var wrapped = "(function(){" + js + "})()";
                    var val = _jint.Execute(wrapped).GetCompletionValue();
                    try
                    {
                        // if explicit false is returned, cancel default
                        if (val.IsBoolean() && !val.AsBoolean()) return true;
                    }
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                }
            }
            catch { /* fall back to JS-0 allowlist below */ }
#endif
#if USE_ECMA_EXPERIMENTAL
            // Try experimental interpreter as an optional path
            try
            {
                if (UseExperimentalEcmaEngine && _exp != null)
                {
                    var wrapped = "(function(){" + js + "})()";
                    var v = _exp.Execute(wrapped);
                    if (v.Type == JsValue.Kind.Boolean && v.Bool == false) return true;
                }
            }
            catch { /* fall back to JS-0 allowlist below */ }
#endif
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
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                    continue;
                }

                // location.href = '�'
                var mHref = RxHrefAssign.Match(line);
                if (mHref.Success) { Navigate(mHref.Groups["url"].Value); continue; }

                // window.location=� or window.location.href=�
                var mW = RxWindowLoc.Match(line);
                if (mW.Success) { Navigate(mW.Groups["url"].Value); continue; }

                // location.assign('�')
                var mAssign = RxAssignCall.Match(line);
                if (mAssign.Success) { Navigate(mAssign.Groups["url"].Value); continue; }

                // location.replace('�')
                var mReplace = RxReplaceCall.Match(line);
                if (mReplace.Success) { Navigate(mReplace.Groups["url"].Value); continue; }

                // alert('�')
                var mAlert = RxAlert.Match(line);
                if (mAlert.Success) { _host.SetStatus(mAlert.Groups["msg"].Value ?? ""); continue; }

                // console.log('�')
                var mLog = RxConsoleLog.Match(line);
                if (mLog.Success) { _host.SetStatus(mLog.Groups["msg"].Value ?? ""); continue; }
                var mOpen = RxWindowOpen.Match(line);
                if (mOpen.Success) { Navigate(mOpen.Groups["url"].Value); continue; }
                if (RxLocationReload.IsMatch(line)) { try { if (_ctx?.BaseUri != null) _host.Navigate(_ctx.BaseUri); else RequestRepaint(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } continue; }
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
                    try { RaiseElementEvent(id, "click"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                    try { _host.SetStatus("setTimeout id=" + id); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                    continue;
                }

                // setTimeout(fnName, ms)
                var mTO2 = RxSetTimeoutFn.Match(line);
                if (mTO2.Success)
                {
                    int ms2 = 0; int.TryParse(mTO2.Groups["ms"].Value, out ms2);
                    var fn = mTO2.Groups["fn"].Value;
                    var id2 = ScheduleTimeout(fn + "()", ms2);
                    try { _host.SetStatus("setTimeout id=" + id2); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                    continue;
                }

                // setInterval('code', ms)
                var mSI = RxSetInterval.Match(line);
                if (mSI.Success)
                {
                    var code = mSI.Groups["code"].Value; int ms; if (!int.TryParse(mSI.Groups["ms"].Value, out ms)) ms = 0;
                    var id = ScheduleInterval(code, ms);
                    try { _host.SetStatus("setInterval id=" + id); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                    continue;
                }

                // setInterval(fnName, ms)
                var mSIF = RxSetIntervalFn.Match(line);
                if (mSIF.Success)
                {
                    int ms = 0; int.TryParse(mSIF.Groups["ms"].Value, out ms);
                    var fn = mSIF.Groups["fn"].Value;
                    var id = ScheduleInterval(fn + "()", ms);
                    try { _host.SetStatus("setInterval id=" + id); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                    try { _host.SetStatus("serviceWorker.register: no-op"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                            catch (Exception ex) { try { _host.SetStatus("fetchText failed: " + ex.Message); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }
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
                                try { RunInline(exec, _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                            }
                            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                                EnqueueMicrotask(() => { try { RunInline(fn + "(" + respExpr + ")", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
                            }
                            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                                        EnqueueMicrotask(() => { try { RunInline(fnPart.Substring(0, fnPart.Length - ".json_callback".Length) + "(" + literal + ")", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
                                    }
                                    catch
                                    {
                                        var escErr = JsEscape(body, '\'');
                                        EnqueueMicrotask(() => { try { RunInline(fnPart + "('" + escErr + "')", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
                                    }
                                }
                                else
                                {
                                    var esc = JsEscape(body, '\'');
                                    EnqueueMicrotask(() => { try { RunInline(fnPart + "('" + esc + "')", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
                                }
                            }
                        }
                    }
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                    continue;
                }

                // Promise.resolve().then(fnName) -> schedule microtask that calls fnName()
                var mPThenFn = RxPromiseThenFunc.Match(line);
                if (mPThenFn.Success)
                {
                    var fn = mPThenFn.Groups["fn"].Value;
                    EnqueueMicrotask(() => { try { RunInline(fn + "()", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
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
                            EnqueueMicrotask(() => { try { RunInline(code, ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
                        }
                    }
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                    EnqueueMicrotask(() => { try { RunInline(codeToRun, ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
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
                            EnqueueMicrotask(() => { try { RunInline(fnPart + "('" + escaped + "')", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
                        }
                    }
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                        EnqueueMicrotask(() => { try { RunInline(fn + "(" + st + ",'" + esc + "')", _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
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
            try { System.Diagnostics.Debug.WriteLine("[Diag] RunScriptsAsync start (Phase123) - Parallelized"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }

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

                if (hasAsync) asyncs.Add(n);
                else if (treatAsDefer) deferred.Add(n);
                else immediate.Add(n);
            }

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

                    if (isModule) return Tuple.Create<string, Uri, bool, bool, bool>(null, resolved, true, false, true); // Modules handled in exec

                    if (!_allowExternalScripts) return Tuple.Create<string, Uri, bool, bool, bool>(null, resolved, false, false, false);
                    if (!SandboxAllows(SandboxFeature.ExternalScripts, resolved.ToString())) return Tuple.Create<string, Uri, bool, bool, bool>(null, resolved, false, false, false);

                    try
                    {
                        var txt = await FetchScriptStringAsync(resolved, baseUri);
                        return Tuple.Create<string, Uri, bool, bool, bool>(txt, resolved, false, false, true);
                    }
                    catch { return Tuple.Create<string, Uri, bool, bool, bool>(null, resolved, false, false, false); }
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
                        try { System.Diagnostics.Debug.WriteLine("[JS-BUDGET] Skipping module script (budget exceeded): " + resolved); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                        return;
                    }
                    Interlocked.Add(ref _pageScriptBytesUsed, moduleApproxBytes);

                    await _moduleLoader.ExecuteModuleTagAsync(node, resolved, baseUri, content);
                    return;
                }

                if (content == null) return;

                int len = content.Length;
                if (_pageScriptBytesUsed + len > _pageScriptByteBudget)
                {
                     if (!isInline || len > TinyInlineFreeThreshold)
                     {
                        try { System.Diagnostics.Debug.WriteLine("[JS-BUDGET] Skipping script (budget exceeded) baseUri=" + baseUri + " size=" + len); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                        return;
                     }
                }
                if (len > TinyInlineFreeThreshold) Interlocked.Add(ref _pageScriptBytesUsed, len);

                try { RunInline(content, new JsContext { BaseUri = resolved }); }
                catch (Exception ex) { try { System.Diagnostics.Debug.WriteLine($"[Diag] RunInline exception: {ex}"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }
            }

            // 1) Immediate: Parallel Fetch, Sequential Exec
            var immTasks = immediate.Select(n => PreFetch(n)).ToList();
            for (int i = 0; i < immediate.Count; i++)
            {
                var res = await immTasks[i];
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
                            repaint.InvokeOnUiThread(async () => 
                            { 
                                try { await Execute(s, res); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } 
                            });
                        }
                        else
                        {
                            await Execute(s, res);
                        }
                    }
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                    finally { try { DecAndMaybeFireLoad(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }
                });
            }

            // 3) Deferred: Parallel Fetch, Sequential Exec
            var defTasks = deferred.Select(n => PreFetch(n)).ToList();
            for (int i = 0; i < deferred.Count; i++)
            {
                var res = await defTasks[i];
                await Execute(deferred[i], res);
            }

            _readyState = "interactive"; try { FireDocumentEvent("readystatechange"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }

            // 4) DOMContentLoaded
            if (!_domContentLoadedFired)
            {
                _domContentLoadedFired = true;
                FireDocumentEvent("DOMContentLoaded");
            }

            // 5) window.load (when asyncs done, or immediately if none)
            if (Volatile.Read(ref _pendingAsyncScripts) == 0 && !_windowLoadFired)
            {
                _windowLoadFired = true;
                FireWindowEvent("load");
                _readyState = "complete"; try { FireDocumentEvent("readystatechange"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
            }
            this.SanitizeForScriptingEnabled(_domRoot);
            RequestRepaint();
        }

        private void DecAndMaybeFireLoad()
        {
            if (Interlocked.Decrement(ref _pendingAsyncScripts) == 0 && _domContentLoadedFired && !_windowLoadFired)
            {
                _windowLoadFired = true;
                FireWindowEvent("load");
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
            try { if (SubresourceAllowed != null && !SubresourceAllowed(uri, "script")) return null; } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }

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
                        catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                        return fromHost;
                    }
                }
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                                string enc = null; try { enc = string.Join(",", altResp.Content.Headers.ContentEncoding); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                                var result = DecodeBytes(altBytes, enc);
                                return result;
                            }
                        }
                        // bail � don�t try other stacks for these hosts
                        return null;
                    }

                    if (!resp.IsSuccessStatusCode)
                    {
                        // If X/Twitter host => bail quietly (avoids WinRT 0x80072EFD spam)
                        if (isXHost(host)) return null;

                        // Non-X host: try our shared managed client (_http) below
                    }
                    else
                    {
                        var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        string enc = null; try { enc = string.Join(",", resp.Content.Headers.ContentEncoding); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                        var result = DecodeBytes(bytes, enc);
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
                            string enc = null; try { enc = string.Join(",", altResp.Content.Headers.ContentEncoding); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                            var altResult = DecodeBytes(altBytes, enc);
                            return altResult;
                        }
                    }
                    return null; // bail � do not try further paths
                }

                if (!resp.IsSuccessStatusCode) return null;

                var b2 = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                string enc2 = null; try { enc2 = string.Join(",", resp.Content.Headers.ContentEncoding); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                var result = DecodeBytes(b2, enc2);
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
            try { req.Headers.TryAddWithoutValidation("User-Agent", ua); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
            try { req.Headers.TryAddWithoutValidation("Accept", "*/*"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
            try { req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
            if (referer != null) { try { req.Headers.Referrer = new Uri(referer.AbsoluteUri); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }
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
                try { return new Uri(alt); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                if (!string.IsNullOrEmpty(contentEncoding))
                {
                    var ce = contentEncoding.ToLowerInvariant();
                    if (ce.Contains("br"))
                    {
                        try
                        {
                            // Brotli may not be available on this target; attempt via reflection and fall through on failure
                            Type broType = null;
                            try { broType = typeof(System.IO.Compression.GZipStream).GetTypeInfo().Assembly.GetType("System.IO.Compression.BrotliStream"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                            if (broType == null)
                            {
                                try { broType = Type.GetType("System.IO.Compression.BrotliStream"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                            }
                            if (broType != null)
                            {
                                // Use the BrotliStream type via reflection to avoid compile-time dependency when absent
                                using (var bro = (Stream)Activator.CreateInstance(broType, new object[] { s, CompressionMode.Decompress }))
                                using (var ms = new MemoryStream())
                                {
                                    bro.CopyTo(ms);
                                    return Encoding.UTF8.GetString(ms.ToArray(), 0, (int)ms.Length);
                                }
                            }
                        }
                        catch { /* platform may not have Brotli - fall through */ }
                    }
                    if (ce.Contains("gzip"))
                    {
                        using (var gz = new GZipStream(s, CompressionMode.Decompress))
                        using (var ms = new MemoryStream())
                        {
                            gz.CopyTo(ms);
                            return Encoding.UTF8.GetString(ms.ToArray(), 0, (int)ms.Length);
                        }
                    }
                    if (ce.Contains("deflate"))
                    {
                        using (var dz = new DeflateStream(s, CompressionMode.Decompress))
                        using (var ms = new MemoryStream())
                        {
                            dz.CopyTo(ms);
                            return Encoding.UTF8.GetString(ms.ToArray(), 0, (int)ms.Length);
                        }
                    }
                }
                // no encoding declared: assume UTF8 (use index/count overload where available)
                try { return Encoding.UTF8.GetString(bytes, 0, bytes.Length); } catch { try { return Encoding.UTF8.GetString(bytes, 0, bytes.Length); } catch { return ""; } }
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
                            try { req.Headers.TryAddWithoutValidation(kv.Key, kv.Value); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                    string enc = null; try { enc = string.Join(",", resp.Content.Headers.ContentEncoding); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                            try { req.Headers.TryAddWithoutValidation(kv.Key, kv.Value); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                    }
                    if (!string.IsNullOrEmpty(body) && !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                    {
                        string ct = null; if (headers != null) headers.TryGetValue("Content-Type", out ct);
                        req.Content = new System.Net.Http.StringContent(body ?? string.Empty, Encoding.UTF8,
                            string.IsNullOrEmpty(ct) ? "text/plain;charset=UTF-8" : ct);
                    }
                    var resp = await client.SendAsync(req).ConfigureAwait(false);
                    var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    string enc = null; try { enc = string.Join(",", resp.Content.Headers.ContentEncoding); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                    var txt = DecodeBytes(bytes, enc);
                    var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    try { foreach (var kv in resp.Headers) h[kv.Key] = string.Join(",", kv.Value ?? new string[0]); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                    try { foreach (var kv in resp.Content.Headers) h[kv.Key] = string.Join(",", kv.Value ?? new string[0]); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                            try { createMethod = rngType.GetRuntimeMethod("Create", new Type[0]); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                            if (createMethod == null) { try { var ti = rngType.GetTypeInfo(); if (ti != null) createMethod = ti.GetDeclaredMethod("Create"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }
                            if (createMethod == null) { try { var ti3 = rngType.GetTypeInfo(); if (ti3 != null) createMethod = ti3.GetDeclaredMethod("Create"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }
                            if (createMethod != null)
                            {
                                try
                                {
                                    using (var rng = (IDisposable)createMethod.Invoke(null, null))
                                    {
                                        MethodInfo getBytes = null;
                                        try { getBytes = rng.GetType().GetRuntimeMethod("GetBytes", new Type[] { typeof(byte[]) }); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                                        if (getBytes == null) { try { var ti2 = rng.GetType().GetTypeInfo(); if (ti2 != null) getBytes = ti2.GetDeclaredMethod("GetBytes"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }
                                        if (getBytes == null) { try { var gi3 = rng.GetType().GetTypeInfo(); if (gi3 != null) getBytes = gi3.GetDeclaredMethod("GetBytes"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }
                                        if (getBytes != null) { getBytes.Invoke(rng, new object[] { buf }); filled = true; }
                                    }
                                }
                                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
#if USE_NILJS
                // Try full NiL.JS engine first for proper JavaScript execution
                try
                {
                    if (_nil != null)
                    {
                        _nilSyncDocument();
                        _nil.Eval(code);
                        return;
                    }
                    else
                    {
                        // NiL.JS not initialized, fall back immediately
                    }
                }
                catch (Exception ex)
                {
                    // Log the exception but don't rethrow - fall back to mini runner
                    try { System.Diagnostics.Debug.WriteLine($"[JS] NiL.JS failed: {ex.Message}"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                }
#endif

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
                    if (mFn.Success) { try { _userFunctions[mFn.Groups[1].Value] = mFn.Groups[2].Value ?? ""; } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } continue; }

                    // bare call: foo();
                    var mCall = Regex.Match(s, @"^\s*([A-Za-z_$][A-Za-z0-9_$]*)\s*\(\s*\)\s*;?\s*$");
                    if (mCall.Success)
                    {
                        string body; if (_userFunctions.TryGetValue(mCall.Groups[1].Value, out body)) { ExecuteScriptBlock(body, _ctx); continue; }
                    }
                    try { RunInline(s, _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
#if USE_NILJS
                try
                {
                    if (_nil != null)
                    {
                        _nilSyncDocument();
                        var v = _nil.Eval(expr);
                        return v != null ? v.ToString() : string.Empty;
                    }
                }
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
#endif
#if USE_JINT && !WINDOWS_PHONE_APP
                try
                {
                    if (_jint != null)
                    {
                        _jint.SetValue("__host_location_href", _ctx?.BaseUri?.AbsoluteUri ?? string.Empty);
                        var v = _jint.Execute(expr).GetCompletionValue();
                        return v != null ? v.ToString() : string.Empty;
                    }
                }
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
#endif
                // Mini runner expression parse
                try
                {
                    var mini = new JsMiniRunner(this);
                    return mini.TryEvalToString(expr);
                }
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }

                // Fallback: run inline and no result
                try { RunInline(expr, _ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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

        private sealed class JsMiniRunner
        {
            private readonly JavaScriptEngine _e;
            private string _src; private int _pos; private int _len;
            private readonly Dictionary<string, JsVal> _globals = new Dictionary<string, JsVal>(StringComparer.Ordinal);
            private readonly List<Dictionary<string, JsVal>> _blockScopes = new List<Dictionary<string, JsVal>>();

            private sealed class JsVal
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
            private sealed class PromiseHandler { public bool IsFulfill; public JsFuncDef Fn; public HostPromise Next; }
            private sealed class HostPromise
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
                            styleMap["getPropertyValue"] = new JsVal { Obj = new HostFunc(a => { try { var p = ToStr(a.Count > 0 ? a[0] : JsVal.Null()).ToLowerInvariant(); JsVal v; if (styleMap.TryGetValue(p, out v)) return v; } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.FromStr(""); }) };
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
                _globals["clearTimeout"] = new JsVal { Obj = new HostFunc(args => { try { int id = (int)ToNum(args.Count > 0 ? args[0] : JsVal.FromNum(0)); _e.ClearTimeout(id); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
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
                _globals["clearInterval"] = new JsVal { Obj = new HostFunc(args => { try { int id = (int)ToNum(args.Count > 0 ? args[0] : JsVal.FromNum(0)); _e.ClearTimeout(id); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };

                // queueMicrotask(fn)
                _globals["queueMicrotask"] = new JsVal { Obj = new HostFunc(args => { try { var a = args.Count > 0 ? args[0] : JsVal.Null(); var f = a.Obj as JsFuncDef; if (f != null) { _e.EnqueueMicrotask(() => { try { var r = new JsMiniRunner(_e); r.InvokeFunction(f, new List<JsVal>()); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }); } else { var code = ToStr(a); if (!string.IsNullOrEmpty(code)) _e.EnqueueMicrotask(() => { try { _e.RunInline(code, _e._ctx); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }); } } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };

                // atob/btoa helpers (string only)
                _globals["atob"] = new JsVal { Obj = new HostFunc(args => { try { var s = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); var bytes = Convert.FromBase64String(s ?? ""); var txt = Encoding.UTF8.GetString(bytes, 0, bytes.Length); return JsVal.FromStr(txt); } catch { return JsVal.FromStr(""); } }) };
                    _globals["btoa"] = new JsVal { Obj = new HostFunc(args => { try { var s = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); var bytes = Encoding.UTF8.GetBytes(s ?? ""); var b64 = Convert.ToBase64String(bytes); return JsVal.FromStr(b64); } catch { return JsVal.FromStr(""); } }) };

                    // Minimal getComputedStyle shim (global)
                    _globals["getComputedStyle"] = new JsVal { Obj = new HostFunc(args =>
                    {
                        try
                        {
                            var styleMap = new Dictionary<string, JsVal>(StringComparer.OrdinalIgnoreCase);
                            Func<string,string,string> pick = (style, prop) =>
                            {
                                try
                                {
                                    foreach (var part in (style ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                                    {
                                        var kv = part.Split(new[] { ':' }, 2);
                                        if (kv.Length == 2 && kv[0].Trim().Equals(prop, StringComparison.OrdinalIgnoreCase)) return kv[1].Trim();
                                    }
                                }
                                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                                return null;
                            };
                            // defaults
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
                            // extras commonly probed by sites
                            styleMap["background-color"] = JsVal.FromStr("transparent");
                            styleMap["border-color"] = JsVal.FromStr("transparent");
                            styleMap["background-repeat"] = JsVal.FromStr("repeat");
                            styleMap["background-size"] = JsVal.FromStr("auto");
                            styleMap["background-position"] = JsVal.FromStr("0% 0%");
                            styleMap["border-top-width"] = JsVal.FromStr("0px");
                            styleMap["border-right-width"] = JsVal.FromStr("0px");
                            styleMap["border-bottom-width"] = JsVal.FromStr("0px");
                            styleMap["border-left-width"] = JsVal.FromStr("0px");

                            if (args.Count > 0)
                            {
                                var a0 = args[0];
                                LiteElement node = null;
                                var jde = a0.Obj as JsDomElement; if (jde != null) node = jde._node;
                                var hel = a0.Obj as HostElement; if (hel != null) node = hel.Node;
                                if (node != null && node.Attr != null)
                                {
                                    string style; if (node.Attr.TryGetValue("style", out style))
                                    {
                                    var ov = pick(style, "overflow"); if (!string.IsNullOrEmpty(ov)) styleMap["overflow"] = JsVal.FromStr(ov);
                                    var tr = pick(style, "transform"); if (!string.IsNullOrEmpty(tr)) styleMap["transform"] = JsVal.FromStr(tr);
                                    var zi = pick(style, "z-index"); if (!string.IsNullOrEmpty(zi)) styleMap["z-index"] = JsVal.FromStr(zi);
                                    var ww = pick(style, "width"); if (!string.IsNullOrEmpty(ww)) styleMap["width"] = JsVal.FromStr(ww);
                                    var hh = pick(style, "height"); if (!string.IsNullOrEmpty(hh)) styleMap["height"] = JsVal.FromStr(hh);
                                    var col = pick(style, "color"); if (!string.IsNullOrEmpty(col)) styleMap["color"] = JsVal.FromStr(col);
                                    var ta = pick(style, "text-align"); if (!string.IsNullOrEmpty(ta)) styleMap["text-align"] = JsVal.FromStr(ta);
                                    var fs = pick(style, "font-size"); if (!string.IsNullOrEmpty(fs)) styleMap["font-size"] = JsVal.FromStr(fs);
                                    var lh = pick(style, "line-height"); if (!string.IsNullOrEmpty(lh)) styleMap["line-height"] = JsVal.FromStr(lh);
                                    var bgr = pick(style, "background-repeat"); if (!string.IsNullOrEmpty(bgr)) styleMap["background-repeat"] = JsVal.FromStr(bgr);
                                    var bgs = pick(style, "background-size"); if (!string.IsNullOrEmpty(bgs)) styleMap["background-size"] = JsVal.FromStr(bgs);
                                    var bgp = pick(style, "background-position"); if (!string.IsNullOrEmpty(bgp)) styleMap["background-position"] = JsVal.FromStr(bgp);
                                    var btw = pick(style, "border-top-width"); if (!string.IsNullOrEmpty(btw)) styleMap["border-top-width"] = JsVal.FromStr(btw);
                                    var brw = pick(style, "border-right-width"); if (!string.IsNullOrEmpty(brw)) styleMap["border-right-width"] = JsVal.FromStr(brw);
                                    var bbw = pick(style, "border-bottom-width"); if (!string.IsNullOrEmpty(bbw)) styleMap["border-bottom-width"] = JsVal.FromStr(bbw);
                                    var blw = pick(style, "border-left-width"); if (!string.IsNullOrEmpty(blw)) styleMap["border-left-width"] = JsVal.FromStr(blw);
                                    var bgc = pick(style, "background-color"); if (!string.IsNullOrEmpty(bgc)) styleMap["background-color"] = JsVal.FromStr(bgc);
                                    var bdc = pick(style, "border-color"); if (!string.IsNullOrEmpty(bdc)) styleMap["border-color"] = JsVal.FromStr(bdc);
                                    var fw = pick(style, "font-weight"); if (!string.IsNullOrEmpty(fw)) styleMap["font-weight"] = JsVal.FromStr(fw);
                                }
                                string w; if (node.Attr.TryGetValue("width", out w) && !styleMap.ContainsKey("width")) styleMap["width"] = JsVal.FromStr(w.EndsWith("px")?w:(w+"px"));
                                string h; if (node.Attr.TryGetValue("height", out h) && !styleMap.ContainsKey("height")) styleMap["height"] = JsVal.FromStr(h.EndsWith("px")?h:(h+"px"));
                            }
                            }

                            styleMap["getPropertyValue"] = new JsVal { Obj = new HostFunc(a => { try { var p = ToStr(a.Count > 0 ? a[0] : JsVal.Null()).ToLowerInvariant(); JsVal v; if (styleMap.TryGetValue(p, out v)) return v; } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.FromStr(""); }) };
                            return new JsVal { Obj = styleMap };
                        }
                        catch { return new JsVal { Obj = new Dictionary<string, JsVal>(StringComparer.Ordinal) }; }
                    }) };
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
                                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                    try { _e._userFunctions[name] = body; } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                    try
                    {
                        var def = new JsFuncDef { Body = body };
                        PopulateFunctionParameters(def, parameters);
                        _e._userFunctionsEx[name] = def; SetVarDecl(name, new JsVal { Obj = def }, false);
                    }
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                                    try { var doc2 = new JsDocument(_e, _e._domRoot); var el2 = doc2.getElementById(id2) as JsDomElement; if (el2 != null) el2.innerText = val2; } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                                }
                                else
                                {
                                    var mHref = Regex.Match(slice, @"(?:window\s*\.\s*)?location\s*\.\s*href\s*=\s*(['""])(?<u>.*?)\1", RegexOptions.IgnoreCase);
                                    if (mHref.Success)
                                    {
                                        try { var url = mHref.Groups["u"].Value; var abs = Resolve(_e._ctx?.BaseUri, url); if (abs != null) _e._host.Navigate(abs); else _e.Navigate(url); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                        catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                    }
                }
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                    try { ParseIdent(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                try { _e._userFunctionsEx[name] = def; } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }

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
                    try { ExecuteString(bodyText); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }

                    // Execute post
                    _pos = postStart;
                    if (postStart < closePos)
                    {
                        try { var __tmp = ParseExpression(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                    }
                    // move past ')'
                    try { Expect(")"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                            { try { var s = ToStr(rhs); var abs = Resolve(_e._ctx?.BaseUri, s); if (abs != null) _e._host.Navigate(abs); else _e.Navigate(s); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return rhs; }
                            // Host: element.innerText / textContent
                            var hel = cur != null ? cur.Obj as HostElement : null;
                            if (hel != null && string.Equals(prop, "innerText", StringComparison.Ordinal))
                            { try { if (!string.IsNullOrEmpty(hel.Id)) { var doc = new JsDocument(_e, _e._domRoot); var el = doc.getElementById(hel.Id) as JsDomElement; if (el != null) el.innerText = ToStr(rhs); } else if (hel.Node != null) { hel.Node.RemoveAllChildren(); var t = new LiteElement("#text"); t.Text = ToStr(rhs); hel.Node.Append(t); _e.RequestRepaint(); } } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return rhs; }
                            if (hel != null && string.Equals(prop, "textContent", StringComparison.Ordinal))
                            { try { if (!string.IsNullOrEmpty(hel.Id)) { var doc = new JsDocument(_e, _e._domRoot); var el = doc.getElementById(hel.Id) as JsDomElement; if (el != null) el.innerText = ToStr(rhs); } else if (hel.Node != null) { hel.Node.RemoveAllChildren(); var t = new LiteElement("#text"); t.Text = ToStr(rhs); hel.Node.Append(t); _e.RequestRepaint(); } } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return rhs; }
                            // JS-0 DOM bridge element.innerText / textContent / id / value / checked
                            var jde = cur != null ? cur.Obj as JsDomElement : null;
                            if (jde != null)
                            {
                                if (string.Equals(prop, "innerText", StringComparison.Ordinal))
                                { try { jde.innerText = ToStr(rhs); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return rhs; }
                                if (string.Equals(prop, "textContent", StringComparison.Ordinal))
                                { try { jde.innerText = ToStr(rhs); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return rhs; }
                                if (string.Equals(prop, "id", StringComparison.Ordinal))
                                { try { jde.id = ToStr(rhs); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return rhs; }
                                if (string.Equals(prop, "value", StringComparison.Ordinal))
                                { try { jde.value = ToStr(rhs); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return rhs; }
                                if (string.Equals(prop, "checked", StringComparison.Ordinal))
                                {
                                    try
                                    {
                                        var v = Convert.ToBoolean(rhs);
                                        // reflect as attribute for simple renderer mapping
                                        jde.setAttribute("checked", v ? "checked" : null);
                                    }
                                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                                    return rhs;
                                }
                            }
                            // document.title setter
                            var hdoc = cur != null ? cur.Obj as HostDocument : null;
                            if (hdoc != null && string.Equals(prop, "title", StringComparison.Ordinal)) { try { _e._docTitle = ToStr(rhs); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return rhs; }
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
                                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                                    return rhs;
                                }
                            }
                            // dataset write: dataset.foo = 'bar'
                            var hds = cur != null ? cur.Obj as HostDataset : null;
                            if (hds != null)
                            { try { if (hds.Node != null && !string.IsNullOrEmpty(prop)) hds.Node.SetAttribute("data-" + prop, ToStr(rhs)); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return rhs; }
                            // Host: style.prop
                            var hst = cur != null ? cur.Obj as HostStyle : null; if (hst != null)
                            { try { var vstr = ToStr(rhs); if (!string.IsNullOrEmpty(hst.Id)) _e.TryUpdateInlineStyle(hst.Id, prop, vstr); else if (hst.Node != null) { var style = hst.Node.GetAttribute("style") ?? ""; var dict = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); foreach (var part in style.Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries)) { var kv=part.Split(new[]{':'},2); if (kv.Length==2) dict[kv[0].Trim()] = kv[1].Trim(); } dict[prop]=vstr??""; var sb=new StringBuilder(); bool first=true; foreach(var kv in dict){ if(!first) sb.Append(';'); first=false; sb.Append(kv.Key).Append(':').Append(kv.Value);} hst.Node.SetAttribute("style", sb.ToString()); } } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return rhs; }
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
                                var resHF = new JsVal { Obj = new HostFunc(a => { var v = a.Count > 0 ? a[0] : JsVal.Null(); p.State = 1; p.Value = v; _e.EnqueueMicrotask(() => { try { ProcessPromiseHandlers(p); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }); return JsVal.Null(); }) };
                                var rejHF = new JsVal { Obj = new HostFunc(a => { var v = a.Count > 0 ? a[0] : JsVal.Null(); p.State = -1; p.Value = v; _e.EnqueueMicrotask(() => { try { ProcessPromiseHandlers(p); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }); return JsVal.Null(); }) };
                                var child = new JsMiniRunner(_e);
                                child.InvokeFunction(fdef, new List<JsVal> { resHF, rejHF });
                            }
                            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                            double w = 0, h = 0; try { var b = Windows.UI.Xaml.Window.Current.Bounds; w = b.Width; h = b.Height; } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                                LiteElement node = null; try { if (jel != null) node = jel._node; } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                            return JsVal.Null();
                        }) };
                    if (name == "querySelectorAll")
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            try
                            {
                                string sel = ToStr(args.Count > 0 ? args[0] : JsVal.Null());
                                var doc = new JsDocument(_e, _e._domRoot);
                                var results = new List<JsVal>();
                                var nodeArray = doc.querySelectorAll(sel) as object[];
                                if (nodeArray != null)
                                {
                                    for (int i = 0; i < nodeArray.Length; i++)
                                    {
                                        var el = nodeArray[i] as JsDomElement;
                                        if (el != null)
                                        {
                                            var idv = el.getAttribute("id");
                                            results.Add(new JsVal { Obj = new HostElement(_e, idv) });
                                        }
                                    }
                                }
                                return new JsVal { Obj = results };
                            }
                            catch { return new JsVal { Obj = new List<JsVal>() }; }
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
                            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                                    try { b._node.Children.Remove(child._node); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                                    _e.RequestRepaint();
                                }
                            }
                            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                        catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                        return JsVal.Null();
                    }
                    if (name == "preventDefault")
                        return new JsVal { Obj = new HostFunc(args => { try { ev.DefaultPrevented = true; _e._preventDefaultRequested = true; } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
                    if (name == "stopPropagation")
                        return new JsVal { Obj = new HostFunc(args => { try { ev.PropagationStopped = true; _e._stopPropagationRequested = true; } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
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
                        return new JsVal { Obj = new HostFunc(args => { try { var child = args.Count > 0 ? args[0].Obj as JsDomNodeBase : null; if (child != null) jde.appendChild(child); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
                    if (name == "removeChild")
                        return new JsVal { Obj = new HostFunc(args => { try { var child = args.Count > 0 ? args[0].Obj as JsDomNodeBase : null; if (child != null) jde.removeChild(child); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
                    if (name == "setAttribute")
                        return new JsVal { Obj = new HostFunc(args => { try { string an = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); string av = ToStr(args.Count > 1 ? args[1] : JsVal.Null()); jde.setAttribute(an, av); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
                    if (name == "getAttribute")
                        return new JsVal { Obj = new HostFunc(args => { try { string an = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); var v = jde.getAttribute(an); return v == null ? JsVal.Null() : JsVal.FromStr(v); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
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
                    if (name == "querySelector") return new JsVal { Obj = new HostFunc(args => { try { string s = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); var r = jde.querySelector(s) as JsDomElement; return r == null ? JsVal.Null() : new JsVal { Obj = r }; } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
                    if (name == "querySelectorAll") return new JsVal { Obj = new HostFunc(args => { try { string s = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); var arrs = jde.querySelectorAll(s) as object[]; var list = new List<JsVal>(); if (arrs != null) { for (int i = 0; i < arrs.Length; i++) { var el = arrs[i] as JsDomElement; if (el != null) list.Add(new JsVal { Obj = el }); } } return new JsVal { Obj = list }; } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return new JsVal { Obj = new List<JsVal>() }; }) };
                    if (name == "insertAdjacentHTML") return new JsVal { Obj = new HostFunc(args => { try { var pos = ToStr(args.Count>0?args[0]:JsVal.Null()); var html = ToStr(args.Count>1?args[1]:JsVal.Null()); jde.insertAdjacentHTML(pos, html); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
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
                                        try { _e._userFunctionsEx[gen] = def; } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                                        _e.RegisterElementListener(he.Id, evt, gen);
                                    }
                                    else
                                    {
                                        var nameStr = ToStr(v);
                                        if (!string.IsNullOrWhiteSpace(nameStr)) _e.RegisterElementListener(he.Id, evt, nameStr);
                                    }
                                }
                            }
                            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                            return JsVal.Null();
                        }) };
                    }
                    if (name == "setAttribute") return new JsVal { Obj = new HostFunc(args => { string an = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); string av = ToStr(args.Count > 1 ? args[1] : JsVal.Null()); try { var doc = new JsDocument(_e, _e._domRoot); var el = doc.getElementById(he.Id) as JsDomElement; if (el != null) el.setAttribute(an, av); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
                    if (name == "getAttribute") return new JsVal { Obj = new HostFunc(args => { string an = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); try { var doc = new JsDocument(_e, _e._domRoot); var el = doc.getElementById(he.Id) as JsDomElement; if (el != null) { var v = el.getAttribute(an); return v == null ? JsVal.Null() : JsVal.FromStr(v); } } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
                    if (name == "innerText") { try { var doc = new JsDocument(_e, _e._domRoot); var el = doc.getElementById(he.Id) as JsDomElement; if (el != null) return JsVal.FromStr(el.innerText ?? ""); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }
                    if (name == "removeAttribute") return new JsVal { Obj = new HostFunc(args => { string an = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); try { var doc = new JsDocument(_e, _e._domRoot); var el = doc.getElementById(he.Id) as JsDomElement; if (el != null) el.setAttribute(an, null); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
                    if (name == "hasAttribute") return new JsVal { Obj = new HostFunc(args => { string an = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); try { var doc = new JsDocument(_e, _e._domRoot); var el = doc.getElementById(he.Id) as JsDomElement; if (el != null) { var v = el.getAttribute(an); return JsVal.FromBool(!string.IsNullOrEmpty(v)); } } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.FromBool(false); }) };
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
                    if (name == "setProperty") { var hs = (HostStyle)obj.Obj; return new JsVal { Obj = new HostFunc(args => { string prop = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); string val = ToStr(args.Count > 1 ? args[1] : JsVal.Null()); try { if (!string.IsNullOrEmpty(hs.Id)) _e.TryUpdateInlineStyle(hs.Id, prop, val); else if (hs.Node != null) { var style = hs.Node.GetAttribute("style") ?? ""; var dict = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); foreach (var part in style.Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries)) { var kv = part.Split(new[]{':'},2); if (kv.Length==2) dict[kv[0].Trim()] = kv[1].Trim(); } dict[prop]=val??""; var sb=new StringBuilder(); bool first=true; foreach(var kv in dict){ if(!first) sb.Append(';'); first=false; sb.Append(kv.Key).Append(':').Append(kv.Value);} hs.Node.SetAttribute("style", sb.ToString()); } } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) }; }
                    return JsVal.Null();
                }
                if (obj.Obj is HostClassList)
                {
                    var cl = (HostClassList)obj.Obj;
                    if (name == "add" || name == "remove" || name == "toggle") return new JsVal { Obj = new HostFunc(args => { string cls = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); try { if (!string.IsNullOrEmpty(cl.Id)) _e.TryUpdateClassList(cl.Id, name, cls); else if (cl.Node != null) { if (name=="add") cl.Node.AddClass(cls); else if (name=="remove") cl.Node.RemoveClass(cls); else cl.Node.ToggleClass(cls); } } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
                    if (name == "contains") return new JsVal { Obj = new HostFunc(args => { string cls = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); try { if (cl.Node != null) { var cur = cl.Node.GetAttribute("class") ?? ""; var set = new HashSet<string>(cur.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal); return JsVal.FromBool(set.Contains(cls)); } } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.FromBool(false); }) };
                    return JsVal.Null();
                }
                if (obj.Obj is HostDataset)
                {
                    var ds = (HostDataset)obj.Obj;
                    if (name == "set") return new JsVal { Obj = new HostFunc(args => { try { var k = ToStr(args.Count>0?args[0]:JsVal.Null()); var v = ToStr(args.Count>1?args[1]:JsVal.Null()); if (ds.Node!=null && !string.IsNullOrEmpty(k)) ds.Node.SetAttribute("data-"+k, v??""); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
                    // reading arbitrary key as property
                    try { var v = ds.Node != null ? ds.Node.GetAttribute("data-" + name) : null; if (v != null) return JsVal.FromStr(v); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                                    try { text = op.GetResults(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                                }
#endif
                            }
                            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                            return JsVal.Null();
                        }) };
                    return JsVal.Null();
                }
                if (obj.Obj is HostConsole)
                {
                    if (name == "log") return new JsVal { Obj = new HostFunc(args => { try { _e._host.SetStatus(args.Count > 0 ? ToStr(args[0]) : ""); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
                    return JsVal.Null();
                }
                if (obj.Obj is HostHistory)
                {
                    if (name == "pushState") return new JsVal { Obj = new HostFunc(args => { try { string url = ToStr(args.Count > 2 ? args[2] : JsVal.Null()); var u = Resolve(_e._ctx?.BaseUri, url); if (u != null) _e.HistoryPush(u); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
                    if (name == "replaceState") return new JsVal { Obj = new HostFunc(args => { try { string url = ToStr(args.Count > 2 ? args[2] : JsVal.Null()); var u = Resolve(_e._ctx?.BaseUri, url); if (u != null) _e.HistoryReplace(u); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
                    return JsVal.Null();
                }
                if (obj.Obj is HostWindow)
                {
                    if (name == "open") return new JsVal { Obj = new HostFunc(args => { try { string url = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); _e.Navigate(url); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
                    if (name == "innerWidth") { try { double w = 0; try { w = Windows.UI.Xaml.Window.Current.Bounds.Width; } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.FromNum(w); } catch { return JsVal.FromNum(0); } }
                    if (name == "innerHeight") { try { double h = 0; try { h = Windows.UI.Xaml.Window.Current.Bounds.Height; } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.FromNum(h); } catch { return JsVal.FromNum(0); } }
                    if (name == "navigator") return new JsVal { Obj = new HostNavigator(_e) };
                    if (name == "performance") return new JsVal { Obj = new HostPerformance(_e) };
                    if (name == "matchMedia")
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            try
                            {
                                string q = ToStr(args.Count > 0 ? args[0] : JsVal.Null());
                                bool matches = false;
                                double w = 0; double h = 0; try { var b = Windows.UI.Xaml.Window.Current.Bounds; w = b.Width; h = b.Height; } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                                if (!string.IsNullOrEmpty(q))
                                {
                                    var s = q.ToLowerInvariant();
                                    if (s.Contains("prefers-color-scheme")) { matches = s.Contains("light"); }
                                    var mw = System.Text.RegularExpressions.Regex.Match(s, @"min-width\s*:\s*(\d+)px");
                                    if (mw.Success) { double v; if (double.TryParse(mw.Groups[1].Value, out v)) matches = matches || (w >= v); }
                                    var xw = System.Text.RegularExpressions.Regex.Match(s, @"max-width\s*:\s*(\d+)px");
                                    if (xw.Success) { double v; if (double.TryParse(xw.Groups[1].Value, out v)) matches = matches || (w <= v); }
                                }
                                var m = new Dictionary<string, JsVal>(StringComparer.Ordinal) { { "matches", JsVal.FromBool(matches) } };
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
                                Uri abs = null; try { abs = Resolve(_e._ctx?.BaseUri, url); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                    if (name == "add") return new JsVal { Obj = new HostFunc(args => { try { var cls = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); if (cl.Node != null) { var cur = cl.Node.GetAttribute("class") ?? ""; var set = new HashSet<string>(cur.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal); set.Add(cls); cl.Node.SetAttribute("class", string.Join(" ", set.ToArray())); } } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
                    if (name == "remove") return new JsVal { Obj = new HostFunc(args => { try { var cls = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); if (cl.Node != null) { var cur = cl.Node.GetAttribute("class") ?? ""; var set = new HashSet<string>(cur.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal); set.Remove(cls); cl.Node.SetAttribute("class", string.Join(" ", set.ToArray())); } } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
                    if (name == "toggle") return new JsVal { Obj = new HostFunc(args => { try { var cls = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); if (cl.Node != null) { var cur = cl.Node.GetAttribute("class") ?? ""; var set = new HashSet<string>(cur.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal); if (!set.Add(cls)) set.Remove(cls); cl.Node.SetAttribute("class", string.Join(" ", set.ToArray())); } } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
                    if (name == "contains") return new JsVal { Obj = new HostFunc(args => { try { var cls = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); if (cl.Node != null) { var cur = cl.Node.GetAttribute("class") ?? ""; var set = new HashSet<string>(cur.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal); return JsVal.FromBool(set.Contains(cls)); } } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.FromBool(false); }) };
                    return JsVal.Null();
                }
                if (obj.Obj is HostLocalStorage)
                {
                    var h = (HostLocalStorage)obj.Obj;
                    if (name == "getItem") return new JsVal { Obj = new HostFunc(args => { try { string k = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); var bag = h.Session ? _e.GetSessionStorageFor(_e._ctx?.BaseUri) : _e.GetLocalStorageFor(_e._ctx?.BaseUri); string v=null; lock(_e._storageLock) bag.TryGetValue(k, out v); return JsVal.FromStr(v??""); } catch { return JsVal.Null(); } }) };
                    if (name == "setItem") return new JsVal { Obj = new HostFunc(args => { try { string k = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); string v = ToStr(args.Count > 1 ? args[1] : JsVal.Null()); var bag = h.Session ? _e.GetSessionStorageFor(_e._ctx?.BaseUri) : _e.GetLocalStorageFor(_e._ctx?.BaseUri); lock(_e._storageLock) bag[k]=v??""; if(!h.Session) _e.PersistLocalStorage(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
                    if (name == "removeItem") return new JsVal { Obj = new HostFunc(args => { try { string k = ToStr(args.Count > 0 ? args[0] : JsVal.Null()); var bag = h.Session ? _e.GetSessionStorageFor(_e._ctx?.BaseUri) : _e.GetLocalStorageFor(_e._ctx?.BaseUri); lock(_e._storageLock) bag.Remove(k); if(!h.Session) _e.PersistLocalStorage(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
                    if (name == "clear") return new JsVal { Obj = new HostFunc(args => { try { var bag = h.Session ? _e.GetSessionStorageFor(_e._ctx?.BaseUri) : _e.GetLocalStorageFor(_e._ctx?.BaseUri); lock(_e._storageLock) bag.Clear(); if(!h.Session) _e.PersistLocalStorage(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
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
                            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                                        var s = (t.Result != null ? t.Result.Body : "") ?? ""; Windows.Data.Json.IJsonValue jv = null; try { jv = Windows.Data.Json.JsonValue.Parse(s); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                                        var arg = jv != null ? FromJson(jv) : JsVal.Null();
                                        var runner = new JsMiniRunner(_e);
                                        runner.InvokeFunction(fn, new List<JsVal> { arg });
                                    }
                                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                    if (name == "reload") return new JsVal { Obj = new HostFunc(args => { try { var b = _e._ctx != null ? _e._ctx.BaseUri : null; if (b != null) _e._host.Navigate(b); else _e.RequestRepaint(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } return JsVal.Null(); }) };
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
                                _e.EnqueueMicrotask(() => { try { ProcessPromiseHandlers(hp); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
                            }
                            return new JsVal { Obj = next };
                        }) };
                    if (name == "catch")
                        return new JsVal { Obj = new HostFunc(args =>
                        {
                            var onRejected = args.Count > 0 ? args[0].Obj as JsFuncDef : null;
                            var next = new HostPromise { E = _e, State = 0, Value = JsVal.Null() };
                            if (onRejected != null) hp.Handlers.Add(new PromiseHandler { IsFulfill = false, Fn = onRejected, Next = next });
                            if (hp.State != 0) { _e.EnqueueMicrotask(() => { try { ProcessPromiseHandlers(hp); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }); }
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
                            if (hp.State != 0) { _e.EnqueueMicrotask(() => { try { ProcessPromiseHandlers(hp); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }); }
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }

                if (result != null && (result.Obj != null || result.Str != null || result.Num.HasValue || result.Bool.HasValue))
                {
                    return result;
                }

                return instance;
            }

            private JsVal Invoke(JsVal callee, List<JsVal> args)
            { var host = callee.Obj as HostFunc; if (host != null) { try { return host.F(args ?? new List<JsVal>()); } catch { return JsVal.Null(); } } var def = callee.Obj as JsFuncDef; if (def != null) { return InvokeFunction(def, args ?? new List<JsVal>()); } return JsVal.Null(); }

            private JsVal InvokeFunction(JsFuncDef def, List<JsVal> args, JsVal thisObj = null)
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                try
                {
                    child._src = def.Body ?? ""; child._pos = 0; child._len = child._src.Length; child.SkipWs();
                    while (!child.Eof()) { child.ParseStatement(); child.SkipWs(); }
                }
                catch (ReturnEx rex) { return rex.Value ?? JsVal.Null(); }
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                            var ret = r.InvokeFunction(h.Fn, new List<JsVal> { p.Value });
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
                                    if (h.Next != null) { h.Next.State = rp.State; h.Next.Value = rp.Value; _e.EnqueueMicrotask(() => { try { ProcessPromiseHandlers(h.Next); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }); }
                                }
                            }
                            else
                            {
                                if (h.Next != null) { h.Next.State = 1; h.Next.Value = ret ?? JsVal.Null(); _e.EnqueueMicrotask(() => { try { ProcessPromiseHandlers(h.Next); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }); }
                            }
                        }
                        else
                        {
                            // no handler => propagate as-is
                            if (h.Next != null) { h.Next.State = p.State; h.Next.Value = p.Value; _e.EnqueueMicrotask(() => { try { ProcessPromiseHandlers(h.Next); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }); }
                        }
                    }
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                if (string.IsNullOrWhiteSpace(sel)) return false;

                // Adjacent sibling selector a + b or general sibling a ~ b
                if (sel.Contains("+") || sel.Contains("~"))
                {
                    // handle e.g. "div + p" or "li ~ li"
                    var parts = sel.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 3 && (parts[1] == "+" || parts[1] == "~"))
                    {
                        var a = parts[0];
                        var op = parts[1];
                        var b = parts[2];
                        // This function is called per candidate element 'n' � check if n matches 'b' and has preceding sibling matching 'a'
                        if (!MatchesSimpleSelector(n, b)) return false;
                        var parent = FindParent(n);
                        if (parent == null) return false;
                        var siblings = parent.Children;
                        int idx = siblings.IndexOf(n);
                        if (idx <= 0) return false;
                        if (op == "+")
                        {
                            var prev = siblings[idx - 1];
                            return MatchesSimpleSelector(prev, a);
                        }
                        else
                        {
                            for (int i = 0; i < idx; i++) if (MatchesSimpleSelector(siblings[i], a)) return true;
                            return false;
                        }
                    }
                }

                // attribute selectors and tag
                // supported forms:
                // tag, tag[attr], tag[attr='val'], tag[attr^='val'], tag[attr$='val'], tag[attr*='val'], tag[attr~='val'], tag[attr|='val']
                var m = System.Text.RegularExpressions.Regex.Match(sel,
                    @"^(?:(?<tag>[a-zA-Z0-9_-]+))?(?:\[(?<attr>[a-zA-Z0-9_-]+)(?:(?<op>\^=|\$=|\*=|~=|\|=|=)(?:'(?<val1>[^']*)'|""(?<val2>[^""]*)""))?\])?$");
                if (m.Success)
                {
                    var tag = m.Groups["tag"].Value;
                    var attr = m.Groups["attr"].Value;
                    var op = m.Groups["op"].Value;
                    var val = m.Groups["val1"].Success ? m.Groups["val1"].Value : (m.Groups["val2"].Success ? m.Groups["val2"].Value : null);
                    if (!string.IsNullOrEmpty(tag) && !string.Equals(n.Tag, tag, StringComparison.OrdinalIgnoreCase)) return false;
                    if (string.IsNullOrEmpty(attr)) return true;
                    if (n.Attr == null) return false;
                    string av; if (!n.Attr.TryGetValue(attr, out av)) return false;
                    if (string.IsNullOrEmpty(op))
                    {
                        // presence or exact match if value provided
                        if (val == null) return !string.IsNullOrEmpty(av);
                        return string.Equals(av, val, StringComparison.Ordinal);
                    }
                    switch (op)
                    {
                        case "^=": return av != null && av.StartsWith(val, StringComparison.Ordinal);
                        case "$=": return av != null && av.EndsWith(val, StringComparison.Ordinal);
                        case "*=": return av != null && av.IndexOf(val, StringComparison.Ordinal) >= 0;
                        case "~=":
                            {
                                var parts = (av ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var p in parts) if (string.Equals(p, val, StringComparison.Ordinal)) return true;
                                return false;
                            }
                        case "|=":
                            return av != null && (string.Equals(av, val, StringComparison.Ordinal) || (av.StartsWith(val + "-", StringComparison.Ordinal)));
                        case "=":
                            return string.Equals(av, val, StringComparison.Ordinal);
                        default:
                            return false;
                    }
                }

                // fallback: tag name only
                return string.Equals(n.Tag, sel, StringComparison.OrdinalIgnoreCase);
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
            public string nodeType => "text";
            public string data { get { return _node.Text ?? ""; } set { _node.Text = value ?? ""; } }
        }

        private sealed class JsDomElement : JsDomNodeBase
        {
            public JsDomElement(JavaScriptEngine e, LiteElement n) : base(e, n) { }
            public string tagName => (_node.Tag ?? "").ToUpperInvariant();

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
                    try { _node.SetAttribute("id", value ?? ""); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }

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
                        catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }

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
                                        try { _e.RunInline(code, new JsContext { BaseUri = _e._ctx?.BaseUri }); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                                    }
                                }
                            }
                            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                        }
                    }
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }

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
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }

                    _e.RequestRepaint();
                }
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }

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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                _e.RequestRepaint();
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
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                _e.RequestRepaint();
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                try
                {
                    for (int i = 0; i < n.Children.Count; i++)
                    {
                        var childClone = CloneTree(n.Children[i]);
                        if (childClone != null) c.Append(childClone);
                    }
                }
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
                return c;
            }

            private static string SerializeChildren(LiteElement n)
            {
                var sb = new System.Text.StringBuilder();
                try
                {
                    for (int i = 0; i < n.Children.Count; i++) SerializeNode(n.Children[i], sb);
                }
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
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
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }

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
            _requestRender = requestRender ?? (() => { try { _status("[DOM mutated]"); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
            _invokeOnUiThread = invokeOnUiThread ?? (a => { try { a(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } });
            _setTitle = setTitle ?? (_ => { });
        }

        public void Navigate(Uri target) => _navigate(target);
        public void PostForm(Uri target, string body) => _post(target, body);
        public void SetStatus(string s) => _status(s);

        // IJsHostRepaint implementation
        public void RequestRender() { try { _requestRender(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }
        public void InvokeOnUiThread(Action action) { if (action == null) return; try { _invokeOnUiThread(action); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); } }

        public void SetTitle(string tval)
        {
            try { _setTitle(tval ?? string.Empty); }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/JavaScriptEngine.cs] empty catch empty catch"); }
        }
    }
}
