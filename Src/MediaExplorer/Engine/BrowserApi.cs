using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Windows.UI.Xaml;
using Windows.UI.Core;
using Windows.Data.Json; // Native JSON support for WP8.1
using BrowserCore.Engine;
using NiL.JS.Core;

namespace BrowserCore.Api
{
    /// <summary>
    /// High-level facade wrapping existing engine pieces.
    /// C# 5.0 Compatible for Windows Phone 8.1 (No Newtonsoft dependency)
    /// </summary>
    public interface IBrowser
    {
        Uri CurrentUri { get; }
        Uri BaseUri { get; }
        Task<bool> NavigateAsync(string url);
        bool CanGoBack { get; }
        bool CanGoForward { get; }
        void GoBack();
        void GoForward();
        void Reload();

        event EventHandler<bool> LoadingChanged;

        void PostMessage(string message);
        event EventHandler<string> MessageReceived;

        Task<string> FetchTextAsync(string url);
        Task<IDictionary<string, object>> FetchJsonAsync(string url);

        Task<bool> EvaluateAsync(string code);
        string EvaluateExpression(string expr);
        void RegisterFunction(string name, string body);
        void SetSandbox(SandboxPolicy policy);

        IReadOnlyDictionary<string, string> GetLocalStorageSnapshot();
        void SetLocalStorageItem(string key, string value);
        string GetLocalStorageItem(string key);
        void RemoveLocalStorageItem(string key);
        void ClearLocalStorage();

        IReadOnlyDictionary<string, string> GetCookies();
        void SetCookie(string name, string value);
        void DeleteCookie(string name);

        IList<string> GetAllLinks();
        string GetTextContent();

        event EventHandler<Uri> Navigated;
        event EventHandler<string> NavigationFailed;
        event EventHandler<string> StatusMessage;
        event EventHandler<FrameworkElement> RepaintReady;
    }

    public sealed class BrowserHost : IBrowser, IDisposable
    {
        private readonly CustomHtmlEngine _engine = new CustomHtmlEngine();
        private readonly ResourceManager _resources;
        private readonly JavaScriptEngine _js;
        private readonly IJsRuntime _runtime;

        private readonly List<Uri> _history = new List<Uri>();
        private int _historyIndex = -1;
        private const int MaxHistoryItems = 50;

        private Uri _current;
        private Uri _base;

        private bool _isDisposed = false;
        private int _navigationId = 0;

        public event EventHandler<Uri> Navigated;
        public event EventHandler<string> NavigationFailed;
        public event EventHandler<string> StatusMessage;
        public event EventHandler<string> MessageReceived;
        public event EventHandler<bool> LoadingChanged;
        public event EventHandler<FrameworkElement> RepaintReady;

        public string RenderMode
        {
            get => _engine.RenderModeString;
            set => _engine.RenderModeString = value;
        }

        public BrowserHost()
        {
            _resources = new ResourceManager(new Windows.Web.Http.HttpClient());

            _js = new JavaScriptEngine(new JsHostAdapter(
                delegate (Uri u) {
                    try { var ignored = NavigateInternalAsync(u); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
                },
                delegate (Uri target, string body) {
                    try { var ignored = NavigateInternalAsync(target); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
                },
                delegate (string msg) { RaiseStatus(msg); },
                null,
                delegate (Action a) {
                    try
                    {
                        var disp = UiThreadHelper.TryGetDispatcher();
                        if (disp != null) UiThreadHelper.RunAsync(disp, Windows.UI.Core.CoreDispatcherPriority.Normal, () => { try { a(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); } });
                        else a();
                    }
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
                }))
            {
                ExecuteInlineScriptsOnInnerHTML = true
            };

            _runtime = new JsZeroRuntime(_js);
            _engine.EnableJavaScript = true;

            _engine.RepaintReady += async delegate (FrameworkElement e) {
                await RaiseRepaintAsync(e);
            };
            _engine.LoadingChanged += delegate (object s, bool isLoading) {
                var handler = LoadingChanged;
                if (handler != null) handler(this, isLoading);
            };

            InstallMessagingShim();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            try
            {
                if (_engine != null) _engine.Dispose();

                // ERROR FIX: Removed explicit cast to IDisposable for _resources
                // because the ResourceManager class definition does not support it.

                _history.Clear();
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
        }

        public Uri CurrentUri { get { return _current; } }
        public Uri BaseUri { get { return _base; } }
        public bool CanGoBack { get { return _historyIndex > 0; } }
        public bool CanGoForward { get { return _historyIndex >= 0 && _historyIndex < _history.Count - 1; } }

        /// <summary>Expose the current active Lite DOM for DevTools.</summary>
        public LiteElement GetActiveDom()
        {
            return _engine.GetActiveDom();
        }

        private void RaiseStatus(string msg)
        {
            try
            {
                var handler = StatusMessage;
                if (handler != null) handler(this, msg);
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
        }

        private async Task RaiseRepaintAsync(FrameworkElement element)
        {
            if (element == null) return;
            var handler = RepaintReady;
            if (handler == null) return;

            try
            {
                var disp = UiThreadHelper.TryGetDispatcher();
                if (disp != null && !UiThreadHelper.HasThreadAccess(disp))
                {
                    await UiThreadHelper.RunAsyncAwaitable(disp, Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        try { handler(this, element); }
                        catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
                    });
                }
                else
                {
                    handler(this, element);
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
        }

        private void RaiseNavigated(Uri uri)
        {
            if (uri == null) return;
            System.Diagnostics.Debug.WriteLine($"[NavEvent] RaiseNavigated uri={uri}");
            var handler = Navigated;
            if (handler == null)
            {
                System.Diagnostics.Debug.WriteLine("[NavEvent] WARNING: No Navigated handler subscribed!");
                return;
            }

            try
            {
                var disp = UiThreadHelper.TryGetDispatcher();
                if (disp != null && !UiThreadHelper.HasThreadAccess(disp))
                {
                    UiThreadHelper.RunAsync(disp, Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        try { handler(this, uri); }
                        catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
                    });
                }
                else
                {
                    handler(this, uri);
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
        }

        private void RaiseNavigationFailed(string message)
        {
            var handler = NavigationFailed;
            if (handler == null) return;

            try
            {
                var disp = UiThreadHelper.TryGetDispatcher();
                if (disp != null && !UiThreadHelper.HasThreadAccess(disp))
                {
                    UiThreadHelper.RunAsync(disp, Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        try { handler(this, message); }
                        catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
                    });
                }
                else
                {
                    handler(this, message);
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
        }

        private async Task NavigateInternalAsync(Uri uri, bool addToHistory = true)
        {
            if (uri == null) return;
            await NavigateAsync(uri.AbsoluteUri, addToHistory);
        }

        private void UpdateState(Uri uri)
        {
            if (uri == null) return;
            _current = uri;
            try
            {
                var ub = new UriBuilder(uri);
                ub.Path = string.Empty;
                ub.Query = string.Empty;
                ub.Fragment = string.Empty;
                _base = ub.Uri;
            }
            catch { _base = uri; }

            if (_historyIndex >= 0 && _historyIndex < _history.Count)
                _history[_historyIndex] = uri;

            RaiseNavigated(uri);
        }

        public async Task<bool> NavigateAsync(string url)
        {
            return await NavigateAsync(url, true);
        }

        private async Task<bool> NavigateAsync(string url, bool addToHistory)
        {
            if (_isDisposed) { System.Diagnostics.Debug.WriteLine("[DIAG] BrowserHost.NavigateAsync SKIP disposed"); return false; }

            int currentNavId = System.Threading.Interlocked.Increment(ref _navigationId);
            System.Diagnostics.Debug.WriteLine("[DIAG] BrowserHost.NavigateAsync START navId=" + currentNavId + " url=" + url);

            RaiseStatus("Navigating...");
            Uri uri;

            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                var q = Uri.EscapeDataString(url ?? "");
                uri = new Uri("https://www.google.com/search?q=" + q + "&hl=en&gbv=1");
            }

            try
            {
                string html = await _resources.FetchTextAsync(uri);
                System.Diagnostics.Debug.WriteLine("[DIAG] BrowserHost FetchTextAsync htmlLen=" + (html != null ? html.Length.ToString() : "null") + " navId=" + currentNavId);

                if (_navigationId != currentNavId) { System.Diagnostics.Debug.WriteLine("[DIAG] BrowserHost ABORT navId changed " + currentNavId + " -> " + _navigationId); return false; }

                // Try alternate user agents if empty
                if (string.IsNullOrWhiteSpace(html))
                {
                    try
                    {
                        var ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
                        html = await _resources.FetchTextWithOptionsAsync(uri, null,
                            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8", "document", ua, "identity");
                    }
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
                }

                if (string.IsNullOrWhiteSpace(html))
                {
                    try
                    {
                        var ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
                        html = await _resources.FetchTextWithOptionsAsync(uri, null,
                            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8", "document", ua, "identity");
                    }
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
                }

                // If still empty, show a user-friendly error page
                if (string.IsNullOrWhiteSpace(html))
                {
                    RaiseNavigationFailed("Empty response");
                    string errorHtml = "<html><body style='font-family:sans-serif;color:#b00;padding:2em;'><h2>Network Error</h2><p>Could not load the requested page. Please check your internet connection or try again later.</p></body></html>";
                    // Render the error page
                    double vwErr = 0;
                    try { vwErr = Windows.UI.Xaml.Window.Current.Bounds.Width; } catch { vwErr = 480; }
                    if (vwErr <= 0) vwErr = 480;
                    var element = await _engine.RenderAsync(
                        errorHtml,
                        uri,
                        u => Task.FromResult<string>(null),
                        u => Task.FromResult<Windows.Storage.Streams.IRandomAccessStream>(null),
                        delegate (Uri u) { },
                        vwErr);
                    await RaiseRepaintAsync(element);
                    RaiseStatus("Network error: Empty response");
                    return false;
                }

                var meta = BrowserCoreHelpers.TryExtractMetaRefresh(uri, html);
                if (meta != null && !string.Equals(meta.AbsoluteUri, uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
                {
                    return await NavigateAsync(meta.AbsoluteUri);
                }

                var scriptRedirect = BrowserCoreHelpers.TryExtractScriptRedirect(uri, html);
                if (scriptRedirect != null && !string.Equals(scriptRedirect.AbsoluteUri, uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
                {
                    return await NavigateAsync(scriptRedirect.AbsoluteUri);
                }

                var jsAllowed = BrowserCoreHelpers.IsJavaScriptAllowed(_engine.EnableJavaScript, uri);
                if (!jsAllowed && !BrowserCoreHelpers.ShouldPreferScriptShell(uri))
                {
                    var noscript = BrowserCoreHelpers.TryExtractNoScriptHtml(html);
                    if (!string.IsNullOrWhiteSpace(noscript)) html = noscript;
                }

                if (_navigationId != currentNavId) return false;

                    // Prevent NullReferenceException by checking html again
                    if (string.IsNullOrWhiteSpace(html))
                    {
                        RaiseNavigationFailed("Empty response");
                        string errorHtml = "<html><body style='font-family:sans-serif;color:#b00;padding:2em;'><h2>Network Error</h2><p>Could not load the requested page. Please check your internet connection or try again later.</p></body></html>";
                        double vwErr2 = 0;
                        try { vwErr2 = Windows.UI.Xaml.Window.Current.Bounds.Width; } catch { vwErr2 = 480; }
                        if (vwErr2 <= 0) vwErr2 = 480;
                        var element = await _engine.RenderAsync(
                            errorHtml,
                            uri,
                            u => Task.FromResult<string>(null),
                            u => Task.FromResult<Windows.Storage.Streams.IRandomAccessStream>(null),
                            delegate (Uri u) { },
                            vwErr2);
                        await RaiseRepaintAsync(element);
                        RaiseStatus("Network error: Empty response");
                        return false;
                    }

                System.Diagnostics.Debug.WriteLine("[DIAG] BrowserHost _engine.RenderAsync START navId=" + currentNavId);
                
                // Calculate viewport width for vw/vh/calc() support
                double vw = 0;
                try { vw = Windows.UI.Xaml.Window.Current.Bounds.Width; } catch { vw = 480; }
                if (vw <= 0) vw = 480;
                
                var element2 = await _engine.RenderAsync(
                    html,
                    uri,
                    u => _resources.FetchTextAsync(u, uri),
                    u => _resources.FetchImageAsync(u, uri, ResourceManager.ResourcePriority.Low),
                    delegate (Uri u) { try { var ignored = NavigateInternalAsync(u); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); } },
                    vw);
                System.Diagnostics.Debug.WriteLine("[DIAG] BrowserHost _engine.RenderAsync DONE element2=" + (element2 != null ? element2.GetType().Name : "null") + " navId=" + currentNavId);

                if (_navigationId != currentNavId) { System.Diagnostics.Debug.WriteLine("[DIAG] BrowserHost ABORT after render navId " + currentNavId + " -> " + _navigationId); return false; }

                await RaiseRepaintAsync(element2);
                System.Diagnostics.Debug.WriteLine("[DIAG] BrowserHost RaiseRepaintAsync DONE navId=" + currentNavId);

                if (addToHistory) AddHistory(uri);
                UpdateState(uri);
                RaiseStatus("Loaded.");
                return true;
            }
            catch (Exception ex)
            {
                RaiseNavigationFailed(ex.Message);
                string errorHtml = $"<html><body style='font-family:sans-serif;color:#b00;padding:2em;'><h2>Network Error</h2><p>{System.Net.WebUtility.HtmlEncode(ex.Message)}</p></body></html>";
                // Render the error page
                double vwCatch = 0;
                try { vwCatch = Windows.UI.Xaml.Window.Current.Bounds.Width; } catch { vwCatch = 480; }
                if (vwCatch <= 0) vwCatch = 480;
                var element = await _engine.RenderAsync(
                    errorHtml,
                    uri,
                    u => Task.FromResult<string>(null),
                    u => Task.FromResult<Windows.Storage.Streams.IRandomAccessStream>(null),
                    delegate (Uri u) { },
                    vwCatch);
                await RaiseRepaintAsync(element);
                RaiseStatus("Error: " + ex.Message);
                return false;
            }
        }

        private void AddHistory(Uri uri)
        {
            if (uri == null) return;

            System.Diagnostics.Debug.WriteLine($"[History] AddHistory uri={uri} index={_historyIndex} count={_history.Count}");

            if (_historyIndex >= 0 && _historyIndex + 1 < _history.Count)
            {
                _history.RemoveRange(_historyIndex + 1, _history.Count - (_historyIndex + 1));
            }

            _history.Add(uri);

            if (_history.Count > MaxHistoryItems)
            {
                _history.RemoveAt(0);
            }

            _historyIndex = _history.Count - 1;
            System.Diagnostics.Debug.WriteLine($"[History] After add: index={_historyIndex} count={_history.Count} CanGoBack={CanGoBack} CanGoForward={CanGoForward}");
        }

        public IList<string> GetHistoryUrls()
        {
            var list = new List<string>(_history.Count);
            for (int i = 0; i < _history.Count; i++)
            {
                if (_history[i] != null)
                    list.Add(_history[i].AbsoluteUri);
            }
            return list;
        }

        public void GoBack()
        {
            if (CanGoBack)
            {
                _historyIndex--;
                var ignored = NavigateInternalAsync(_history[_historyIndex], addToHistory: false);
            }
        }

        public void GoForward()
        {
            if (CanGoForward)
            {
                _historyIndex++;
                var ignored = NavigateInternalAsync(_history[_historyIndex], addToHistory: false);
            }
        }

        public void Reload()
        {
            if (_current != null)
            {
                var ignored = NavigateAsync(_current.AbsoluteUri);
            }
        }

        public void PostMessage(string message) { }

        private void InstallMessagingShim() { }

        private void DeliverJsMessage(string msg)
        {
            try
            {
                var handler = MessageReceived;
                if (handler != null) handler(this, msg ?? string.Empty);
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
        }

        public async Task<bool> EvaluateAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            return await Task.Run(() => _js.RunInline(code, null));
        }

        public string EvaluateExpression(string expr)
        { return _runtime.EvaluateExpression(expr, null); }

        public void RegisterFunction(string name, string body)
        { _runtime.RegisterHostFunction(name, body); }

        public void SetSandbox(SandboxPolicy policy)
        { _runtime.Sandbox = policy; }

        public async Task<string> FetchTextAsync(string url)
        {
            try
            {
                Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return null;
                return await _resources.FetchTextAsync(u);
            }
            catch { return null; }
        }

        // ERROR FIX: Use Windows.Data.Json instead of Newtonsoft
        public async Task<IDictionary<string, object>> FetchJsonAsync(string url)
        {
            try
            {
                var txt = await FetchTextAsync(url);
                if (string.IsNullOrWhiteSpace(txt)) return null;

                JsonObject jsonObject;
                if (JsonObject.TryParse(txt, out jsonObject))
                {
                    // Manually convert JsonObject to Dictionary
                    var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in jsonObject)
                    {
                        dict[item.Key] = ConvertJsonValue(item.Value);
                    }
                    return dict;
                }

                // Fallback if parsing failed (but txt exists)
                var fallback = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                fallback.Add("_", txt);
                return fallback;
            }
            catch
            {
                return null;
            }
        }

        // Helper to convert Windows.Data.Json types to standard C# types
        private object ConvertJsonValue(IJsonValue val)
        {
            if (val == null) return null;
            switch (val.ValueType)
            {
                case JsonValueType.String: return val.GetString();
                case JsonValueType.Number: return val.GetNumber();
                case JsonValueType.Boolean: return val.GetBoolean();
                case JsonValueType.Null: return null;
                case JsonValueType.Array:
                    // Recursive list conversion could go here, returning string for now for safety
                    return val.GetArray().ToString();
                case JsonValueType.Object:
                    return val.GetObject().ToString();
                default: return val.ToString();
            }
        }

        public IReadOnlyDictionary<string, string> GetLocalStorageSnapshot()
        {
            return new Dictionary<string, string>();
        }

        public void SetLocalStorageItem(string key, string value)
        { _js.LocalStorageSet(key, value, null); }

        public string GetLocalStorageItem(string key)
        { return _js.LocalStorageGet(key, null); }

        public void RemoveLocalStorageItem(string key)
        { _js.LocalStorageRemove(key, null); }

        public void ClearLocalStorage()
        { _js.LocalStorageClear(null); }

        public IReadOnlyDictionary<string, string> GetCookies()
        {
            var u = _current ?? _base;
            return _engine.GetCookieSnapshot(u);
        }

        public void SetCookie(string name, string value)
        {
            var u = _current ?? _base;
            _engine.SetCookie(u, name, value);
        }

        public void DeleteCookie(string name)
        {
            var u = _current ?? _base;
            _engine.DeleteCookie(u, name);
        }

        public IList<string> GetAllLinks()
        {
            var list = new List<string>();
            try
            {
                var root = _engine.GetActiveDom();
                foreach (var n in root.SelfAndDescendants())
                {
                    if (n.Tag == "a" && n.Attr != null)
                    {
                        string href;
                        if (n.Attr.TryGetValue("href", out href) && !string.IsNullOrWhiteSpace(href))
                            list.Add(href);
                    }
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
            return list;
        }

        public string GetTextContent()
        {
            try
            {
                var root = _engine.GetActiveDom();
                if (root == null) return string.Empty;
                var sb = new System.Text.StringBuilder();
                foreach (var n in root.SelfAndDescendants())
                {
                    if (n.IsText && !string.IsNullOrWhiteSpace(n.Text))
                        sb.AppendLine(n.Text.Trim());
                }
                return sb.ToString();
            }
            catch { return string.Empty; }
        }
    }

    internal static class BrowserCoreHelpers
    {
        private static readonly Regex MetaTagRegex = new Regex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly Regex MetaAttrRegex = new Regex(@"(?<name>[\w:-]+)\s*=\s*(?:(['""])(?<valueQuoted>.*?)\2|(?<valueBare>[^\s'""/>]+))", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly Regex NoScriptRegex = new Regex(@"<noscript[^>]*>(?<inner>[\s\S]*?)</noscript>", RegexOptions.IgnoreCase);
        private static readonly Regex ScriptTagRegex = new Regex(@"<script\b[^>]*>(?<code>[\s\S]*?)</script>", RegexOptions.IgnoreCase);
        private static readonly Regex JsRedirectSimple = new Regex(@"location\s*(?:\.\s*(?:href|assign|replace))?\s*=\s*(?<quote>['""])(?<url>[^'""\r\n]+)\k<quote>", RegexOptions.IgnoreCase);
        private static readonly Regex JsRedirectMethod = new Regex(@"location\s*\.\s*(?:assign|replace)\s*\(\s*(?<quote>['""])(?<url>[^'""\r\n]+)\k<quote>\s*\)", RegexOptions.IgnoreCase);

        internal static Uri TryExtractMetaRefresh(Uri baseUri, string html)
        {
            try
            {
                if (string.IsNullOrEmpty(html)) return null;

                var tagMatches = MetaTagRegex.Matches(html);
                foreach (Match tag in tagMatches)
                {
                    if (!tag.Success) continue;

                    var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var attrMatches = MetaAttrRegex.Matches(tag.Value);

                    foreach (Match attr in attrMatches)
                    {
                        var name = attr.Groups["name"].Value;
                        var value = attr.Groups["valueQuoted"].Success
                            ? attr.Groups["valueQuoted"].Value
                            : attr.Groups["valueBare"].Value;

                        if (!string.IsNullOrEmpty(name)) attrs[name] = value;
                    }

                    string httpEquiv;
                    if (!attrs.TryGetValue("http-equiv", out httpEquiv) ||
                        !string.Equals(httpEquiv, "refresh", StringComparison.OrdinalIgnoreCase)) continue;

                    string content;
                    if (!attrs.TryGetValue("content", out content) || string.IsNullOrWhiteSpace(content)) continue;

                    string urlComponent = null;
                    double dummy;

                    foreach (var segment in content.Split(';'))
                    {
                        var part = (segment ?? string.Empty).Trim();
                        if (part.Length == 0) continue;

                        var kv = part.Split(new[] { '=' }, 2);
                        if (kv.Length == 2 && string.Equals(kv[0].Trim(), "url", StringComparison.OrdinalIgnoreCase))
                        {
                            urlComponent = kv[1].Trim().Trim('"', '\'');
                        }
                        else if (urlComponent == null)
                        {
                            if (!double.TryParse(part, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dummy))
                            {
                                urlComponent = part.Trim().Trim('"', '\'');
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(urlComponent)) continue;

                    Uri abs;
                    if (Uri.TryCreate(urlComponent, UriKind.Absolute, out abs)) return abs;
                    if (baseUri != null)
                    {
                        try { return new Uri(baseUri, urlComponent); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
                    }
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
            return null;
        }

        internal static string TryExtractNoScriptHtml(string html)
        {
            try
            {
                if (string.IsNullOrEmpty(html)) return null;
                var m = NoScriptRegex.Match(html);
                if (m.Success)
                {
                    var inner = m.Groups["inner"].Value;
                    if (!string.IsNullOrWhiteSpace(inner)) return inner;
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
            return null;
        }

        internal static Uri TryExtractScriptRedirect(Uri baseUri, string html)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(html)) return null;

                var scriptMatches = ScriptTagRegex.Matches(html);
                foreach (Match script in scriptMatches)
                {
                    if (!script.Success) continue;
                    var code = script.Groups["code"].Value;
                    if (string.IsNullOrWhiteSpace(code)) continue;

                    var redirect = JsRedirectSimple.Match(code);
                    if (!redirect.Success)
                    {
                        redirect = JsRedirectMethod.Match(code);
                    }

                    if (redirect.Success)
                    {
                        var target = redirect.Groups["url"].Value.Trim();
                        Uri abs;
                        if (Uri.TryCreate(target, UriKind.Absolute, out abs)) return abs;
                        if (baseUri != null)
                        {
                            try { return new Uri(baseUri, target); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
                        }
                    }
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
            return null;
        }

        internal static bool? GetJsQueryOverride(Uri uri)
        {
            try
            {
                var query = uri == null ? null : uri.Query;
                if (string.IsNullOrWhiteSpace(query)) return null;

                var trimmed = query.TrimStart('?');
                var parts = trimmed.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var part in parts)
                {
                    var eqIndex = part.IndexOf('=');
                    if (eqIndex < 0) continue;

                    var key = Uri.UnescapeDataString(part.Substring(0, eqIndex)).Trim();
                    if (!string.Equals(key, "js", StringComparison.OrdinalIgnoreCase)) continue;

                    var valStr = Uri.UnescapeDataString(part.Substring(eqIndex + 1)).Trim();

                    if (string.Equals(valStr, "1") ||
                        string.Equals(valStr, "true", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(valStr, "on", StringComparison.OrdinalIgnoreCase)) return true;

                    if (string.Equals(valStr, "0") ||
                        string.Equals(valStr, "false", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(valStr, "off", StringComparison.OrdinalIgnoreCase)) return false;
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/BrowserApi.cs] empty catch empty catch"); }
            return null;
        }

        internal static bool IsJavaScriptAllowed(bool engineEnabled, Uri uri)
        {
            if (!engineEnabled) return false;
            var queryOverride = GetJsQueryOverride(uri);
            if (queryOverride.HasValue) return queryOverride.Value;
            return true;
        }

        internal static bool ShouldPreferScriptShell(Uri uri)
        {
            try
            {
                var host = uri == null ? null : uri.Host;
                if (string.IsNullOrWhiteSpace(host)) return false;

                var h = host.ToLowerInvariant();
                if (h == "x.com" || h.EndsWith(".x.com")) return true;
                if (h == "twitter.com" || h.EndsWith(".twitter.com")) return true;

                return false;
            }
            catch { return false; }
        }
    }
}