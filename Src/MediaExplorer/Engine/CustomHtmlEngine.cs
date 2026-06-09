using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using BrowserCore.Api;
using BrowserCore.Engine.Core;
using WEBVIEW.Engine;
using Windows.Foundation;
using NiL.JS.Core;

namespace BrowserCore.Engine
{
    /// <summary>
    /// Rendering mode controlling JS/CSS/image behavior.
    /// FULL  — NiL.JS + full CSS cascade + images (default)
    /// RICH  — MiniRunner only (timeouts/analytics-kill) + full CSS + images + AI companion
    /// POOR  — No JS + minimal reader stylesheet + no images + AI summary (e-book mode)
    /// </summary>
    public enum RenderModeType
    {
        Full,   // NiL.JS + full CSS + images
        Rich,   // MiniRunner only + full CSS + images
        Poor    // No JS + reader stylesheet + no images
    }

    /// <summary>
    /// Lightweight orchestrator that composes HtmlLiteParser + CssLoader + DomBasicRenderer.
    /// Clean, dependency-free wrapper suitable for WP8.1 without WebView.
    /// </summary>
    public sealed class CustomHtmlEngine : IDisposable
    {
        public bool SafeMode { get; set; } = false;

        private RenderModeType _renderMode = RenderModeType.Full;
        public RenderModeType RenderMode
        {
            get { return _renderMode; }
            set
            {
                _renderMode = value;
                // Backward compat: also update string property
                _renderModeString = value.ToString();
            }
        }

        // Backward compat: string-based API (Settings page, BrowserApi)
        private string _renderModeString = "Full";
        public string RenderModeString
        {
            get { return _renderModeString; }
            set
            {
                _renderModeString = value;
                if (string.Equals(value, "Poor", StringComparison.OrdinalIgnoreCase))
                    _renderMode = RenderModeType.Poor;
                else if (string.Equals(value, "Rich", StringComparison.OrdinalIgnoreCase))
                    _renderMode = RenderModeType.Rich;
                else
                    _renderMode = RenderModeType.Full;
            }
        }

        public event EventHandler<bool> LoadingChanged;

        public bool EnableJavaScript { get; set; } = true;

        /// <summary>
        /// Timeout (ms) for the entire JS execution phase (RunScriptsAsync).
        /// Default 60s allows heavy scripts like D3.js to initialize.
        /// Increase for JS-heavy SPAs, decrease for simple text sites.
        /// </summary>
        public int JsPhaseTimeoutMs { get; set; } = 60000;

        public void ApplySafeMode()
        {
            if (SafeMode)
            {
                EnableJavaScript = false;
                // Optionally disable other advanced features here
            }
        }

        public Func<Uri, Task<string>> ScriptFetcher { get; set; }

        public event Action<FrameworkElement> RepaintReady;

        // Incremental render state
        private RenderObject _currentRenderTree;
        private VirtualizingRenderer _currentRenderer;
        private Dictionary<LiteElement, CssComputed> _currentStyles;
        private Dictionary<LiteElement, RenderObject> _elementToRenderObject;
        private bool _hasRenderState;

        private LiteElement _activeDom;
        private Uri _activeBaseUri;
        private Func<Uri, Task<string>> _activeFetchCss;
        private Func<Uri, Task<IRandomAccessStream>> _activeImageLoader;
        private Action<Uri> _activeOnNavigate;
        private double? _activeViewportWidth;
        private Action<Brush> _activeFixedBackground;
        private JavaScriptEngine _activeJs;
        private readonly CookieContainer _jsCookieJar = new CookieContainer();
        private readonly System.Threading.SemaphoreSlim _repaintGate = new System.Threading.SemaphoreSlim(1, 1);
        private int _repaintScheduled;
        private int _svgRefreshPending;
        private readonly CoreDispatcher _uiDispatcher;
        private volatile int _isRendering;

        public CustomHtmlEngine()
        {
            _uiDispatcher = UiThreadHelper.TryGetDispatcher();
        }

        private static readonly System.Collections.Generic.HashSet<string> _loadingTokens =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "loading",
                "loaded",
                "pleasewait",
                "justamoment",
                "holdtight",
                "hangtight",
                "stillworking"
            };

        // Heuristic: detect JS-heavy SPA/app-shell sites where our JS engine
        // cannot realistically reproduce full behavior (e.g., modern google.com),
        // and skip expensive JS execution + double-render for performance.
        private static bool IsJsHeavyAppShell(Uri baseUri)
        {
            try
            {
                if (baseUri == null || string.IsNullOrEmpty(baseUri.Host)) return false;
                var host = baseUri.Host.ToLowerInvariant();

                // Start conservatively: only google.com and www.google.com.
                if (host == "google.com" || host == "www.google.com") return true;

                return false;
            }
            catch { return false; }
        }

        public void Dispose()
        {
            try
            {
                _repaintGate.Dispose();
                // Unsubscribe events if necessary or clean up JS engine
                _activeJs = null; 
                _activeDom = null;
            }
            catch { /* swallow */ }
        }

        private async Task RaiseLoadingChangedAsync(bool isLoading)
        {
            var handler = LoadingChanged;
            if (handler == null) return;
            try
            {
                var disp = _uiDispatcher ?? UiThreadHelper.TryGetDispatcher();
                if (disp != null && !UiThreadHelper.HasThreadAccess(disp))
                {
                    await UiThreadHelper.RunAsyncAwaitable(disp, CoreDispatcherPriority.Normal, () =>
                    {
                        try { handler(this, isLoading); }
                        catch { /* swallow */ }
                    });
                }
                else
                {
                    handler(this, isLoading);
                }
            }
            catch { /* swallow */ }
        }

        // Resolve a possibly relative URL against a base
        private static Uri ResolveUri(Uri baseUri, string href)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(href)) return null;
                href = href.Trim();
                if (href.StartsWith("//"))
                {
                    var scheme = baseUri != null ? baseUri.Scheme : "https";
                    return new Uri(scheme + ":" + href);
                }
                Uri abs;
                if (Uri.TryCreate(href, UriKind.Absolute, out abs)) return abs;
                if (baseUri != null && Uri.TryCreate(baseUri, href, out abs)) return abs;
            }
            catch { /* swallow */ }
            return null;
        }

        // ---------------------------------------------------------
        // Helper: Image Logic (Consolidated for Performance/DRY)
        // ---------------------------------------------------------

        private static string RewriteWebPToJpg(string u)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(u)) return u;
                
                // Quick check before regex to save CPU on WP8.1
                if (u.IndexOf("webp", StringComparison.OrdinalIgnoreCase) < 0 && 
                    u.IndexOf("avif", StringComparison.OrdinalIgnoreCase) < 0) 
                    return u;

                if (u.IndexOf("format=webp", StringComparison.OrdinalIgnoreCase) >= 0)
                    u = System.Text.RegularExpressions.Regex.Replace(u, @"format=webp", "format=jpg", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                u = System.Text.RegularExpressions.Regex.Replace(u, @"(\?|&)(f|fmt)=webp", "$1$2=jpg", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (System.Text.RegularExpressions.Regex.IsMatch(u, @"\.(webp|avif)(\?.*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    u = System.Text.RegularExpressions.Regex.Replace(u, @"\.(webp|avif)(\?.*)?$", ".jpg$2", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch { /* swallow */ }
            return u;
        }

        private static string PickBestImageFromSrcSet(string srcset, double viewportWidth)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(srcset)) return null;
                
                // Optimization: Avoid LINQ overhead on hot paths if possible, but here LINQ is readable.
                // Split by comma
                var candidates = srcset.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s =>
                    {
                        var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        var url = parts[0];
                        int width = 0;
                        if (parts.Length > 1)
                        {
                             var d = parts[1].Trim().ToLowerInvariant();
                             if (d.EndsWith("w")) int.TryParse(d.TrimEnd('w'), out width);
                        }
                        return new { url, width };
                    })
                    .OrderBy(s => s.width) // Sort smallest to largest
                    .ToList();

                // Logic: Find first image >= viewport width. If none, use the largest available.
                var bestCandidate = candidates.FirstOrDefault(c => c.width >= viewportWidth) ?? candidates.LastOrDefault();
                
                return RewriteWebPToJpg(bestCandidate?.url);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WARN] Srcset parsing failed: {ex.Message}");
                return null;
            }
        }

        // ---------------------------------------------------------
        // End Helper
        // ---------------------------------------------------------

        // Kick off background image fetches early (img/srcset/background-image)
        private static void PrewarmImages(LiteElement root, Uri baseUri, Func<Uri, Task<IRandomAccessStream>> imageLoader, double? viewportWidth)
        {
            if (root == null || imageLoader == null) return;
            try
            {
                var tasks = new List<Task>();
                var gate = new System.Threading.SemaphoreSlim(6);
                int budget = 32; // avoid over-queuing
                double dw = viewportWidth ?? 0; 
                try { if (dw <= 0) dw = Windows.UI.Xaml.Window.Current.Bounds.Width; } catch { /* swallow */ }
                if (dw <= 0) dw = 480;

                // Helper to execute load
                Action<string> queueLoad = (rawUrl) => 
                {
                    if (budget <= 0) return;
                    var clean = RewriteWebPToJpg(rawUrl);
                    var abs = ResolveUri(baseUri, clean);
                    if (abs != null)
                    {
                        budget--;
                        tasks.Add(Task.Run(async () => 
                        { 
                            try 
                            { 
                                await gate.WaitAsync(); 
                                try { await imageLoader(abs); } 
                                finally { gate.Release(); } 
                            } 
                            catch { /* swallow */ } 
                        }));
                    }
                };

                // Preload/Prefetch links
                foreach (var link in root.Descendants().Where(n => n.Tag == "link" && n.Attr != null))
                {
                    string rel;
                    if (link.Attr.TryGetValue("rel", out rel) && (rel == "preload" || rel == "prefetch"))
                    {
                        string href;
                        if (link.Attr.TryGetValue("href", out href))
                        {
                            string asAttr;
                            if (link.Attr.TryGetValue("as", out asAttr) && asAttr == "image")
                            {
                                queueLoad(href);
                            }
                        }
                    }
                }

                // Images and Backgrounds
                foreach (var n in root.SelfAndDescendants())
                {
                    if (budget <= 0) break;
                    try
                    {
                        if (n.IsText) continue;
                        if (n.Tag == "img" && n.Attr != null)
                        {
                            string src = null; n.Attr.TryGetValue("src", out src);
                            if (string.IsNullOrWhiteSpace(src))
                            {
                                string v; 
                                if (n.Attr.TryGetValue("data-src", out v)) src = v; 
                                else if (n.Attr.TryGetValue("data-original", out v)) src = v; 
                                else if (n.Attr.TryGetValue("data-lazy", out v)) src = v;
                            }

                            string srcset = null; n.Attr.TryGetValue("srcset", out srcset);
                            string chosen = null;
                            if (!string.IsNullOrWhiteSpace(srcset)) 
                            {
                                chosen = PickBestImageFromSrcSet(srcset, dw);
                            }
                            
                            if (string.IsNullOrWhiteSpace(chosen)) chosen = src;
                            queueLoad(chosen);
                        }
                        else if (n.Attr != null)
                        {
                            string style; 
                            if (n.Attr.TryGetValue("style", out style) && !string.IsNullOrWhiteSpace(style))
                            {
                                // background-image/background shorthand regex
                                var m = System.Text.RegularExpressions.Regex.Match(style, "url\\(['\"']?(?<u>[^)\"']+)['\"']?\\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (m.Success)
                                {
                                    queueLoad(m.Groups["u"].Value);
                                }
                            }
                        }
                    }
                    catch { /* swallow */ }
                }
                // Fire-and-forget; we do not await prewarm tasks to avoid blocking render
            }
            catch { /* swallow */ }
        }

        private static string GatherPlainText(LiteElement n)
        {
            if (n == null) return string.Empty;
            var sb = new System.Text.StringBuilder();
            Action<LiteElement> walk = null;
            walk = (el) =>
            {
                if (el == null) return;
                if (el.IsText) { var t = el.Text ?? string.Empty; sb.Append(t); return; }
                if (el.Children != null)
                {
                    for (int i = 0; i < el.Children.Count; i++) walk(el.Children[i]);
                }
            };
            walk(n);
            var s = sb.ToString();
            // collapse whitespace lightly
            bool inWs = false; var outSb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (char.IsWhiteSpace(c)) { if (!inWs) { outSb.Append(' '); inWs = true; } }
                else { outSb.Append(c); inWs = false; }
            }
            return outSb.ToString().Trim();
        }

        /// <summary>
        /// POOR mode: apply minimal reader stylesheet to DOM nodes.
        /// Strips CSS noise, shows clean text, preserves readability — e-book feel.
        /// </summary>
        private void ApplyReaderStylesheet(LiteElement dom)
        {
            if (dom == null) return;
            foreach (var node in dom.SelfAndDescendants())
            {
                if (node.IsText) continue;
                var tag = node.Tag?.ToUpperInvariant();
                if (tag == "SCRIPT" || tag == "STYLE" || tag == "META" || tag == "LINK" || tag == "HEAD")
                    continue;

                // Hide images in POOR mode
                if (tag == "IMG")
                {
                    node.SetAttribute("style", "display:none");
                    continue;
                }

                // Hide nav, header, footer, sidebar noise
                var id = (node.Attr != null && node.Attr.ContainsKey("id")) ? node.Attr["id"].ToLowerInvariant() : "";
                var cls = (node.Attr != null && node.Attr.ContainsKey("class")) ? node.Attr["class"].ToLowerInvariant() : "";
                if (id.Contains("nav") || id.Contains("menu") || id.Contains("sidebar") || id.Contains("ad") ||
                    cls.Contains("nav") || cls.Contains("menu") || cls.Contains("sidebar") || cls.Contains("ad-") || cls.Contains("advertisement"))
                {
                    node.SetAttribute("style", "display:none");
                    continue;
                }

                // Apply reader styles to content elements
                if (tag == "BODY")
                    ApplyInlineStyle(node, "font-family:Segoe UI,sans-serif; font-size:16px; line-height:1.6; color:#111; margin:16px; max-width:65ch;");
                else if (tag == "H1")
                    ApplyInlineStyle(node, "font-size:28px; font-weight:bold; margin:24px 0 12px; color:#000;");
                else if (tag == "H2")
                    ApplyInlineStyle(node, "font-size:22px; font-weight:bold; margin:20px 0 10px; color:#000;");
                else if (tag == "H3")
                    ApplyInlineStyle(node, "font-size:18px; font-weight:bold; margin:16px 0 8px; color:#000;");
                else if (tag == "P")
                    ApplyInlineStyle(node, "margin:0 0 12px;");
                else if (tag == "A")
                    ApplyInlineStyle(node, "color:#00b; text-decoration:underline;");
                else if (tag == "BLOCKQUOTE")
                    ApplyInlineStyle(node, "border-left:4px solid #ccc; padding-left:12px; margin:12px 0; color:#555; font-style:italic;");
                else if (tag == "PRE" || tag == "CODE")
                    ApplyInlineStyle(node, "font-family:Consolas,monospace; font-size:14px; background:#f5f5f5; padding:2px 4px; border-radius:3px;");
                else if (tag == "UL" || tag == "OL")
                    ApplyInlineStyle(node, "margin:8px 0 8px 24px; padding:0;");
                else if (tag == "LI")
                    ApplyInlineStyle(node, "margin:4px 0;");
                else if (tag == "TABLE")
                    ApplyInlineStyle(node, "border-collapse:collapse; margin:12px 0; width:auto;");
                else if (tag == "TH" || tag == "TD")
                    ApplyInlineStyle(node, "border:1px solid #ccc; padding:6px 8px;");
                else if (tag == "HR")
                    ApplyInlineStyle(node, "border:none; border-top:1px solid #ccc; margin:16px 0;");
            }
        }

        private static void ApplyInlineStyle(LiteElement node, string style)
        {
            if (node == null || string.IsNullOrEmpty(style)) return;
            string existing = null;
            if (node.Attr != null) node.Attr.TryGetValue("style", out existing);
            node.SetAttribute("style", style + (existing != null ? " " + existing : ""));
        }

        /// <summary>
        /// RICH mode: run MiniRunner for setTimeout/clearTimeout support and analytics kill.
        /// Does NOT execute full JS — only lightweight timers and script blocking.
        /// </summary>
        private void RunRichMiniRunner(LiteElement dom, JavaScriptEngine js)
        {
            if (dom == null || js == null) return;

            // Collect inline <script> blocks that are likely analytics/tracking
            var scripts = dom.Descendants()
                .Where(n => string.Equals(n.Tag, "script", StringComparison.OrdinalIgnoreCase))
                .ToList();

            int killed = 0;
            foreach (var script in scripts)
            {
                var src = script.Attr != null && script.Attr.ContainsKey("src") ? script.Attr["src"] : "";
                var text = script.Text ?? "";
                var combined = (src + " " + text).ToLowerInvariant();

                // Kill known analytics/tracking patterns
                if (combined.Contains("google-analytics") ||
                    combined.Contains("gtag") ||
                    combined.Contains("googletagmanager") ||
                    combined.Contains("facebook.com/tr") ||
                    combined.Contains("fbq(") ||
                    combined.Contains("analytics") ||
                    combined.Contains("telemetry") ||
                    combined.Contains("tracking") ||
                    combined.Contains("doubleclick"))
                {
                    // Remove analytics scripts — they won't execute
                    script.Text = "";
                    if (script.Attr != null) script.Attr["src"] = "";
                    killed++;
                    System.Diagnostics.Debug.WriteLine("[RICH] Killed analytics script: " + (src != "" ? src : "(inline)"));
                }
            }

            System.Diagnostics.Debug.WriteLine("[RICH] MiniRunner: killed " + killed + " analytics scripts, setTimeout/clearTimeout available");
        }

        private void CaptureActiveContext(
            LiteElement dom,
            Uri baseUri,
            Func<Uri, Task<string>> fetchExternalCssAsync,
            Func<Uri, Task<IRandomAccessStream>> imageLoader,
            Action<Uri> onNavigate,
            double? viewportWidth,
            Action<Brush> onFixedBackground,
            JavaScriptEngine js)
        {
            _activeDom = dom;
            _activeBaseUri = baseUri;
            _activeFetchCss = fetchExternalCssAsync;
            _activeImageLoader = imageLoader;
            _activeOnNavigate = onNavigate;
            _activeViewportWidth = viewportWidth;
            _activeFixedBackground = onFixedBackground;
            _activeJs = js;
            if (_activeJs != null)
            {
                try { _activeJs.FetchOverride = ScriptFetcher; }
                catch { /* swallow */ }
            }
        }

        private void ConfigureMedia(double? viewportWidth)
        {
            try
            {
                double vw = viewportWidth ?? 0;
                double vh = 0;
                try { vh = Windows.UI.Xaml.Window.Current.Bounds.Height; } catch { vh = 800; }
                if (vw <= 0) { try { vw = Windows.UI.Xaml.Window.Current.Bounds.Width; } catch { vw = 480; } }
                
                // Initialize viewport dimensions for vw/vh/calc() in CssLoader
                CssLoader.SetViewportDimensions(vw, vh);
                
                CssParser.MediaViewportWidth = viewportWidth;
                try { CssParser.MediaViewportHeight = vh; } catch { /* swallow */ }
                try
                {
                    double dpr = 1.0;
                    try { var di = Windows.Graphics.Display.DisplayInformation.GetForCurrentView(); dpr = di.RawPixelsPerViewPixel; } catch { /* swallow */ }
                    CssParser.MediaDppx = dpr;
                }
                catch { CssParser.MediaDppx = 1.0; }
                try { CssParser.MediaPrefersColorScheme = ((Application.Current != null && Application.Current.RequestedTheme == ApplicationTheme.Dark) ? "dark" : "light"); }
                catch { CssParser.MediaPrefersColorScheme = "light"; }
                try { CssParser.MediaScripting = "enabled"; } catch { /* swallow */ }
            }
            catch { /* swallow */ }
        }

        private async Task<FrameworkElement> BuildVisualTreeAsync(
            LiteElement dom,
            Uri baseUri,
            Func<Uri, Task<string>> fetchExternalCssAsync,
            Func<Uri, Task<IRandomAccessStream>> imageLoader,
            Action<Uri> onNavigate,
            JavaScriptEngine js,
            double? viewportWidth,
            Action<Brush> onFixedBackground,
            bool includeDiagnosticsBanner)
        {
            if (dom == null) { System.Diagnostics.Debug.WriteLine("[DIAG] BuildVisualTree SKIP dom=null"); return null; }

            ConfigureMedia(viewportWidth);

            var cssFetcher = fetchExternalCssAsync ?? (async _ => string.Empty);
            System.Diagnostics.Debug.WriteLine("[DIAG] BuildVisualTree CssLoader.ComputeAsync start");
            var computed = await CssLoader.ComputeAsync(dom, baseUri, cssFetcher, viewportWidth, null);
            System.Diagnostics.Debug.WriteLine("[DIAG] BuildVisualTree CssLoader.ComputeAsync DONE nodes=" + (computed != null ? computed.Count.ToString() : "null"));

            try
            {
                if (onFixedBackground != null && computed != null)
                {
                    Windows.UI.Xaml.Media.Brush fixedBg = null;
                    bool bgFixed = false;
                    Func<string, string[]> splitLayers = (raw) =>
                    {
                        if (string.IsNullOrWhiteSpace(raw)) return new string[0];
                        var list = new System.Collections.Generic.List<string>();
                        var sb = new System.Text.StringBuilder(); int depth = 0; bool inQ = false; char qc = '\0';
                        foreach (var ch in raw)
                        {
                            if ((ch == '\'' || ch == '"')) { if (!inQ) { inQ = true; qc = ch; } else if (qc == ch) inQ = false; }
                            else if (!inQ && ch == '(') depth++; else if (!inQ && ch == ')') depth = Math.Max(0, depth - 1);
                            if (!inQ && depth == 0 && ch == ',') { list.Add(sb.ToString()); sb.Clear(); }
                            else sb.Append(ch);
                        }
                        if (sb.Length > 0) list.Add(sb.ToString());
                        return list.ToArray();
                    };
                    Func<LiteElement, bool> check = (el) =>
                    {
                        if (el == null) return false;
                        CssComputed c; if (!computed.TryGetValue(el, out c) || c == null) return false;
                        if (c.Background != null)
                        {
                            string att; if (c.Map != null && c.Map.TryGetValue("background-attachment", out att))
                            {
                                var atts = splitLayers(att);
                                foreach (var a in atts)
                                {
                                    if ((a ?? string.Empty).IndexOf("fixed", StringComparison.OrdinalIgnoreCase) >= 0)
                                    { fixedBg = c.Background; return true; }
                                }
                            }
                            string bg; if (c.Map != null && c.Map.TryGetValue("background", out bg))
                            {
                                if (!string.IsNullOrWhiteSpace(bg))
                                {
                                    var layers = splitLayers(bg);
                                    foreach (var raw in layers)
                                    {
                                        var layer = (raw ?? string.Empty).Trim();
                                        if (layer.IndexOf("fixed", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            fixedBg = c.Background; return true;
                                        }
                                    }
                                }
                            }
                        }
                        return false;
                    };
                    var htmlNode = dom.Descendants().FirstOrDefault(n => n.Tag == "html");
                    var bodyNode = dom.Descendants().FirstOrDefault(n => n.Tag == "body");
                    bgFixed = check(bodyNode) || check(htmlNode);
                    
                    if (onFixedBackground != null)
                    {
                        var disp = UiThreadHelper.TryGetDispatcher();
                        if (disp != null)
                        {
                            await UiThreadHelper.RunAsyncAwaitable(disp, CoreDispatcherPriority.Normal, () =>
                            {
                                onFixedBackground(bgFixed ? fixedBg : null);
                            });
                        }
                    }
                }
            }
            catch { /* swallow */ }

            if (imageLoader == null)
                imageLoader = _ => Task.FromResult<IRandomAccessStream>(null);

            var renderer = new DomBasicRenderer
            {
                ComputedStyles = computed,
                ImageLoader = imageLoader,
                Js = js
            };

            FrameworkElement element = null;
            try
            {
                var disp = UiThreadHelper.TryGetDispatcher();
                if (disp != null)
                {
                    // Step 1: Build Render Tree on UI thread (creates Brushes with thread affinity)
                    RenderObject renderRoot = null;
                    Size viewSize = new Size();
                    var mapping = new Dictionary<LiteElement, RenderObject>();

                    // Get viewport size on UI thread (fast)
                    await UiThreadHelper.RunAsyncAwaitable(disp, CoreDispatcherPriority.Normal, () =>
                    {
                        try
                        {
                            double vw = viewportWidth ?? 0;
                            double vh = 0;
                            try { vh = Windows.UI.Xaml.Window.Current.Bounds.Height; } catch { vh = 800; }
                            if (vw <= 0) { try { vw = Windows.UI.Xaml.Window.Current.Bounds.Width; } catch { vw = 480; } }
                            viewSize = new Size(vw, vh);
                        }
                        catch { }
                    });

                    // Step 1: Build Render Tree on background thread (no XAML objects, safe)
                    var buildDom = dom;
                    var buildComputed = computed;
                    var buildMapping = mapping;
                    System.Diagnostics.Debug.WriteLine("[DIAG] BuildVisualTree Step1 RenderTreeBuilder.Build start");
                    await Task.Run(() =>
                    {
                        try
                        {
                            renderRoot = RenderTreeBuilder.Build(buildDom, buildComputed, buildMapping);
                            System.Diagnostics.Debug.WriteLine("[DIAG] BuildVisualTree Step1 DONE root=" + (renderRoot != null ? renderRoot.GetType().Name : "null"));
                        }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[DIAG] BuildVisualTree Step1 EXC " + ex.Message); }
                    });

                    // Step 2: Layout on background thread (pure computation, no XAML)
                    var layoutRoot = renderRoot;
                    var layoutSize = viewSize;
                    System.Diagnostics.Debug.WriteLine("[DIAG] BuildVisualTree Step2 Layout start size=" + viewSize.Width + "x" + viewSize.Height);
                    await Task.Run(() =>
                    {
                        try { LayoutEngine.PerformLayout(layoutRoot, layoutSize); System.Diagnostics.Debug.WriteLine("[DIAG] BuildVisualTree Step2 Layout DONE"); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[DIAG] BuildVisualTree Step2 Layout EXC " + ex.Message); }
                    });

                    // Step 3: Paint on UI thread (creates XAML elements)
                    var tcs = new TaskCompletionSource<FrameworkElement>();
                    await UiThreadHelper.RunAsyncAwaitable(disp, CoreDispatcherPriority.Normal, () =>
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine("[DIAG] BuildVisualTree Step3 Paint start");
                            var vRenderer = new VirtualizingRenderer(renderRoot, baseUri, onNavigate);
                            _currentRenderTree = renderRoot;
                            _currentRenderer = vRenderer;
                            _currentStyles = computed;
                            _elementToRenderObject = mapping;
                            _hasRenderState = true;
                            var visualRoot = vRenderer.GetRootElement();
                            System.Diagnostics.Debug.WriteLine("[DIAG] BuildVisualTree Step3 Paint DONE rootType=" + (visualRoot != null ? visualRoot.GetType().Name : "null"));
                            tcs.TrySetResult(visualRoot);
                        }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[DIAG] BuildVisualTree Step3 Paint EXC " + ex.Message); tcs.TrySetException(ex); }
                    });
                    element = await tcs.Task.ConfigureAwait(true);
                }
                else
                {
                    throw new InvalidOperationException("Cannot render visual tree without UI thread access.");
                }
            }
            catch (Exception threadEx)
            {
                var disp = UiThreadHelper.TryGetDispatcher();
                if (disp != null)
                {
                    await UiThreadHelper.RunAsyncAwaitable(disp, CoreDispatcherPriority.Normal, () =>
                    {
                        var sp = new StackPanel { Margin = new Thickness(12, 12, 12, 12) };
                        sp.Children.Add(new TextBlock { Text = "Render thread error", FontSize = 18, FontWeight = Windows.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Windows.UI.Colors.Black) });
                        string detail = threadEx.Message;
                        try { detail += "\n" + (threadEx.StackTrace ?? "(no stack)" ); } catch { /* swallow */ }
                        sp.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Windows.UI.Colors.Black), Margin = new Thickness(0,6,0,0) });
                        element = new Border { Background = new SolidColorBrush(Windows.UI.Colors.White), Child = sp };
                    });
                }
            }

            // Wrap in background/border - MUST be on UI thread
            if (element != null)
            {
                // Re-dispatch if we somehow ended up on a background thread (e.g. if disp was null initially but we need UI thread now? Unlikely if disp was null).
                // If disp != null, we want to ensure this runs on UI thread.
                var disp = UiThreadHelper.TryGetDispatcher();
                if (disp != null && !UiThreadHelper.HasThreadAccess(disp))
                {
                     var tcsWrap = new TaskCompletionSource<FrameworkElement>();
                     await UiThreadHelper.RunAsyncAwaitable(disp, CoreDispatcherPriority.Normal, () =>
                     {
                         try
                         {
                             if (computed != null)
                             {
                                var bodyNode = dom.Descendants().FirstOrDefault(n => n.Tag == "body");
                                var htmlNode = dom.Descendants().FirstOrDefault(n => n.Tag == "html");
                                CssComputed cb = null;
                                 if (bodyNode != null && computed.TryGetValue(bodyNode, out cb) && cb != null && cb.Background != null)
                                 {
                                     element = new Border { Background = cb.Background, Child = element };
                                 }
                                 else if (htmlNode != null && computed.TryGetValue(htmlNode, out cb) && cb != null && cb.Background != null)
                                 {
                                     element = new Border { Background = cb.Background, Child = element };
                                 }
                                 else
                                 {
                                     var bg = new SolidColorBrush(Windows.UI.Colors.White);
                                     element = new Border { Background = bg, Child = element };
                                 }
                             }
                             tcsWrap.TrySetResult(element);
                         }
                         catch (Exception ex) { tcsWrap.TrySetException(ex); }
                     });
                     element = await tcsWrap.Task;
                }
                else
                {
                    // Already on UI thread (or no dispatcher available), run directly
                    try
                    {
                        if (computed != null)
                        {
                            var bodyNode = dom.Descendants().FirstOrDefault(n => n.Tag == "body");
                            var htmlNode = dom.Descendants().FirstOrDefault(n => n.Tag == "html");
                            CssComputed cb = null;
                             if (bodyNode != null && computed.TryGetValue(bodyNode, out cb) && cb != null && cb.Background != null)
                             {
                                 element = new Border { Background = cb.Background, Child = element };
                             }
                             else if (htmlNode != null && computed.TryGetValue(htmlNode, out cb) && cb != null && cb.Background != null)
                             {
                                 element = new Border { Background = cb.Background, Child = element };
                             }
                             else
                             {
                                 var bg = new SolidColorBrush(Windows.UI.Colors.White);
                                 element = new Border { Background = bg, Child = element };
                             }
                        }
                    }
                    catch { /* swallow */ }
                }
            }

            if (element == null || IsEffectivelyEmpty(element))
            {
                var fallback = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(12, 12, 12, 12) };
                var fg = new SolidColorBrush(Windows.UI.Colors.White);
                string title = null;
                try { var tnode = dom.Descendants().FirstOrDefault(n => n.Tag == "title"); if (tnode != null) title = tnode.Text; } catch { /* swallow */ }
                if (string.IsNullOrWhiteSpace(title)) title = baseUri != null ? baseUri.Host : "This page";
                fallback.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = Windows.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6), Foreground = fg });
                fallback.Children.Add(new TextBlock { Text = baseUri != null ? baseUri.AbsoluteUri : string.Empty, TextWrapping = TextWrapping.Wrap, Foreground = fg });
                element = new Border { Background = new SolidColorBrush(Windows.UI.Colors.Black), Child = fallback };
            }

            // GPU Acceleration: Apply BitmapCache if enabled
            if (EnableGpuAcceleration && element != null)
            {
                try
                {
                    element.CacheMode = new Windows.UI.Xaml.Media.BitmapCache();
                }
                catch { /* swallow */ }
            }

            if (includeDiagnosticsBanner)
            {
                try
                {
                    var wrap = new StackPanel { Orientation = Orientation.Vertical };
                    var banner = new Border
                    {
                        Background = new SolidColorBrush(Windows.UI.Colors.Black),
                        Child = new TextBlock { Text = "[Rendered]", Foreground = new SolidColorBrush(Windows.UI.Colors.White), Margin = new Thickness(0, 0, 0, 6) }
                    };
                    wrap.Children.Add(banner);
                    if (element != null) wrap.Children.Add(element);
                    element = wrap;
                }
                catch { /* swallow */ }
            }

            return element;
        }

        public bool EnableGpuAcceleration { get; set; } = false;

        private async Task<FrameworkElement> RefreshAsyncInternal(bool includeDiagnosticsBanner)
        {
            // Ensure we are on the UI thread. If not, marshal the call.
            var uiDisp = _uiDispatcher ?? UiThreadHelper.TryGetDispatcher();
            if (uiDisp != null && !UiThreadHelper.HasThreadAccess(uiDisp))
            {
                var tcs = new TaskCompletionSource<FrameworkElement>();
                await UiThreadHelper.RunAsyncAwaitable(uiDisp, CoreDispatcherPriority.Normal, async () =>
                {
                    try
                    {
                        var result = await RefreshAsyncInternal(includeDiagnosticsBanner);
                        tcs.SetResult(result);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });
                return await tcs.Task;
            }

            if (_activeDom == null)
                return null;

            var fetchCss = _activeFetchCss ?? (async _ => string.Empty);
            return await BuildVisualTreeAsync(
                _activeDom,
                _activeBaseUri,
                fetchCss,
                _activeImageLoader,
                _activeOnNavigate,
                _activeJs,
                _activeViewportWidth,
                _activeFixedBackground,
                includeDiagnosticsBanner).ConfigureAwait(false);
        }

        private async Task DispatchRepaintAsync(FrameworkElement element)
        {
            if (element == null) return;
            var handler = RepaintReady;
            if (handler == null) return;

            try
            {
                var disp = UiThreadHelper.TryGetDispatcher();
                if (disp != null && !UiThreadHelper.HasThreadAccess(disp))
                {
                    await UiThreadHelper.RunAsyncAwaitable(disp, CoreDispatcherPriority.Normal, () =>
                    {
                        try { handler(element); }
                        catch { /* swallow */ }
                    }).ConfigureAwait(false);
                }
                else
                {
                    handler(element);
                }
            }
            catch { /* swallow */ }
        }

        private async Task<bool> ApplyIncrementalUpdateAsync(CoreDispatcher disp)
        {
            if (!_hasRenderState || _currentRenderTree == null || _currentRenderer == null || _activeJs == null)
                return false;

            var mutations = _activeJs.DrainRendererMutations();
            if (mutations == null || mutations.Count == 0)
                return false;

            System.Diagnostics.Debug.WriteLine("[DIAG] IncrementalUpdate mutations=" + mutations.Count);

            // Collect affected LiteElements for re-cascade
            var affectedNodes = new HashSet<LiteElement>();
            var addedNodes = new List<LiteElement>();
            var removedNodes = new List<LiteElement>();

            foreach (var mut in mutations)
            {
                if (mut.Type == "childList")
                {
                    if (mut.Added != null)
                        foreach (var a in mut.Added)
                        {
                            affectedNodes.Add(a);
                            addedNodes.Add(a);
                        }
                    if (mut.Removed != null)
                        foreach (var r in mut.Removed) affectedNodes.Add(r);
                    if (mut.Target != null) affectedNodes.Add(mut.Target);
                }
                else if (mut.Type == "attributes")
                {
                    if (mut.Target != null) affectedNodes.Add(mut.Target);
                }
            }

            // Process mutations on UI thread
            await UiThreadHelper.RunAsyncAwaitable(disp, CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    // Phase 1: Apply DOM mutations to RenderObject tree
                    foreach (var mut in mutations)
                    {
                        if (mut.Type == "childList")
                        {
                            // Removed nodes
                            if (mut.Removed != null)
                            {
                                foreach (var removed in mut.Removed)
                                {
                                    if (_elementToRenderObject.TryGetValue(removed, out var ro))
                                    {
                                        var parent = ro.Parent;
                                        if (parent != null)
                                        {
                                            parent.RemoveChild(ro);
                                            _currentRenderer.RemoveVisualFor(ro);
                                        }
                                        RemoveFromMapping(ro);
                                    }
                                }
                            }

                            // Added nodes
                            if (mut.Added != null)
                            {
                                var targetParent = mut.Target;
                                foreach (var added in mut.Added)
                                {
                                    // Re-cascade styles for the added subtree
                                    var newStyles = CssLoader.CascadeSingle(added, _currentStyles);
                                    foreach (var kv in newStyles)
                                        _currentStyles[kv.Key] = kv.Value;

                                    var newSubtree = RenderTreeBuilder.BuildSubtree(added, _currentStyles, _elementToRenderObject);
                                    if (newSubtree != null && _elementToRenderObject.TryGetValue(targetParent, out var parentRo))
                                    {
                                        int insertIndex = 0;
                                        if (mut.Added.Count > 1)
                                        {
                                            var targetChildren = targetParent.Children;
                                            for (int i = 0; i < targetChildren.Count; i++)
                                            {
                                                var child = targetChildren[i];
                                                if (_elementToRenderObject.TryGetValue(child, out var childRo) && childRo.Parent == parentRo)
                                                {
                                                    insertIndex = parentRo.Children.IndexOf(childRo);
                                                    if (insertIndex >= 0)
                                                    {
                                                        parentRo.Children.Insert(insertIndex, newSubtree);
                                                        newSubtree.Parent = parentRo;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            parentRo.AddChild(newSubtree);
                                        }
                                    }
                                }
                            }
                        }
                        else if (mut.Type == "attributes")
                        {
                            // Re-cascade styles for the attribute-changed node
                            if (_elementToRenderObject.TryGetValue(mut.Target, out var ro))
                            {
                                var newStyles = CssLoader.CascadeSingle(mut.Target, _currentStyles);
                                foreach (var kv in newStyles)
                                {
                                    _currentStyles[kv.Key] = kv.Value;
                                    // Update RenderObject style reference
                                    RenderObject targetRo;
                                    if (_elementToRenderObject.TryGetValue(kv.Key, out targetRo))
                                        targetRo.Style = kv.Value;
                                }
                                ro.MarkDirty();
                            }
                        }
                    }

                    // Phase 2: Incremental layout
                    double vw = _activeViewportWidth ?? 0;
                    double vh = 0;
                    try { vh = Windows.UI.Xaml.Window.Current.Bounds.Height; } catch { vh = 800; }
                    if (vw <= 0) { try { vw = Windows.UI.Xaml.Window.Current.Bounds.Width; } catch { vw = 480; } }
                    LayoutEngine.PerformIncrementalLayout(_currentRenderTree, new Windows.Foundation.Size(vw, vh));

                    // Phase 3: Patch renderer (incremental, not full UpdateView)
                    _currentRenderer.UpdateCanvasSize();

                    // Check if any mutation affects an SVG subtree
                    bool hasSvgMutation = false;
                    foreach (var n in addedNodes) { if (IsSvgOrHasSvgAncestor(n)) { hasSvgMutation = true; break; } }
                    if (!hasSvgMutation)
                    {
                        foreach (var n in affectedNodes) { if (IsSvgOrHasSvgAncestor(n)) { hasSvgMutation = true; break; } }
                    }

                    if (hasSvgMutation)
                    {
                        System.Diagnostics.Debug.WriteLine("[DIAG] IncrementalUpdate SVG mutation detected, full UpdateView");
                        // SVG mutations need full SVG re-render (individual patches don't produce correct XAML shapes)
                        _currentRenderer.UpdateView();
                    }
                    else
                    {
                        // Patch added subtrees
                        foreach (var added in addedNodes)
                        {
                            if (_elementToRenderObject.TryGetValue(added, out var ro))
                                _currentRenderer.PatchAdded(ro);
                        }

                        // Patch style-changed nodes
                        foreach (var node in affectedNodes)
                        {
                            if (_elementToRenderObject.TryGetValue(node, out var ro))
                                _currentRenderer.PatchStyle(ro);
                        }

                        // Fallback: if no specific patches were applied, do full UpdateView
                        if (addedNodes.Count == 0 && affectedNodes.Count == 0)
                            _currentRenderer.UpdateView();
                    }
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[DIAG] IncrementalUpdate EXC " + ex.Message);
                }
            });
            System.Diagnostics.Debug.WriteLine("[DIAG] IncrementalUpdate DONE");
            return true;
        }

        private void RemoveFromMapping(RenderObject node)
        {
            if (node == null) return;
            if (node.Node != null && _elementToRenderObject != null)
            {
                _elementToRenderObject.Remove(node.Node);
            }
            if (node.Children != null)
            {
                for (int i = 0; i < node.Children.Count; i++)
                    RemoveFromMapping(node.Children[i]);
            }
        }

        private void ScheduleRepaintFromJs()
        {
            if (!EnableJavaScript) return;
            if (_activeDom == null) return;
            if (_isRendering != 0) return;

            if (System.Threading.Interlocked.Exchange(ref _repaintScheduled, 1) == 1)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await _repaintGate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        var disp = _uiDispatcher ?? UiThreadHelper.TryGetDispatcher();
                        bool applied = false;
                        if (disp != null)
                        {
                            try { applied = await ApplyIncrementalUpdateAsync(disp).ConfigureAwait(false); }
                            catch { applied = false; }
                        }

                        if (!applied)
                        {
                            var element = await RefreshAsyncInternal(includeDiagnosticsBanner: false).ConfigureAwait(false);
                            await DispatchRepaintAsync(element).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        _repaintGate.Release();
                    }
                }
                catch { /* swallow */ }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _repaintScheduled, 0);
                }
            });
        }

        public async Task<FrameworkElement> RefreshAsync(bool includeDiagnosticsBanner = false)
        {
            await _repaintGate.WaitAsync().ConfigureAwait(false);
            try
            {
                return await RefreshAsyncInternal(includeDiagnosticsBanner).ConfigureAwait(false);
            }
            finally
            {
                _repaintGate.Release();
            }
        }

        /// <summary>
        /// Render HTML into a XAML element using the managed engine pipeline.
        /// </summary>
        public async Task<FrameworkElement> RenderAsync(
            string html,
            Uri baseUri,
            Func<Uri, Task<string>> fetchExternalCssAsync,
            Func<Uri, Task<IRandomAccessStream>> imageLoader,
            Action<Uri> onNavigate,
            double? viewportWidth = null,
            Action<Windows.UI.Xaml.Media.Brush> onFixedBackground = null,
            bool? forceJavascript = null,
            bool disableAutoFallback = false)
        {
            var host = (baseUri != null ? baseUri.Host : "null");
            var diagMsg = "[DIAG] RenderAsync START host=" + host + " htmlLen=" + (html != null ? html.Length.ToString() : "null") + " vw=" + (viewportWidth.HasValue ? viewportWidth.Value.ToString() : "null") + " js=" + (forceJavascript.HasValue ? forceJavascript.Value.ToString() : "default");
            System.Diagnostics.Debug.WriteLine(diagMsg);
            DevToolsLogger.Log(diagMsg);

            // Diagnostic: check SVG decoder availability
            var svgType = Type.GetType("Windows.UI.Xaml.Media.Imaging.SvgImageSource, Windows, ContentType=WindowsRuntime");
            var svgDiag = "[DIAG] SvgType available: " + (svgType != null ? "true" : "false");
            System.Diagnostics.Debug.WriteLine(svgDiag);
            DevToolsLogger.Log(svgDiag);

            // Ensure we are on the UI thread. If not, marshal the call.
            var uiDisp = _uiDispatcher ?? UiThreadHelper.TryGetDispatcher();
            if (uiDisp != null && !UiThreadHelper.HasThreadAccess(uiDisp))
            {
                System.Diagnostics.Debug.WriteLine("[DIAG] RenderAsync marshalling to UI thread");
                var tcs = new TaskCompletionSource<FrameworkElement>();
                await UiThreadHelper.RunAsyncAwaitable(uiDisp, CoreDispatcherPriority.Normal, async () =>
                {
                    try
                    {
                        var result = await RenderAsync(html, baseUri, fetchExternalCssAsync, imageLoader, onNavigate, viewportWidth, onFixedBackground, forceJavascript, disableAutoFallback);
                        tcs.SetResult(result);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });
                return await tcs.Task;
            }

            await RaiseLoadingChangedAsync(true);

            _isRendering = 1;
            try
            {
                // 1) Parse DOM (background)
                var parser = new HtmlLiteParser(html ?? string.Empty);
                LiteElement dom = null;
                try
                {
                    dom = await Task.Run(() => parser.Parse());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[DIAG] RenderAsync Parse error: " + ex.Message);
                    return null;
                }

                if (dom == null)
                {
                    System.Diagnostics.Debug.WriteLine("[DIAG] RenderAsync dom=null after parse");
                    return null;
                }
                _activeDom = dom;
                System.Diagnostics.Debug.WriteLine("[DIAG] RenderAsync Phase1 PARSED dom children=" + (dom.Children != null ? dom.Children.Count.ToString() : "0"));

                // POOR mode — e-book style: minimal reader stylesheet, no JS, no images
                if (_renderMode == RenderModeType.Poor)
                {
                    var msg = "[DIAG] RenderAsync Poor mode — reader stylesheet";
                    System.Diagnostics.Debug.WriteLine(msg);
                    DevToolsLogger.Log(msg);

                    // Apply reader stylesheet overrides before building visual tree
                    ApplyReaderStylesheet(dom);

                    // No-op image loader for POOR mode (skip all image fetching)
                    Func<Uri, Task<IRandomAccessStream>> noImageLoader = async _ => null;

                    var poorElement = await BuildVisualTreeAsync(
                        dom,
                        baseUri,
                        fetchExternalCssAsync,
                        noImageLoader, // Skip images in Poor mode
                        onNavigate,
                        null, // No JS in Poor mode
                        viewportWidth,
                        _activeFixedBackground,
                        false // No diagnostics banner in Poor mode
                    ).ConfigureAwait(false);

                    return poorElement;
                }

                // 2. Load CSS
                System.Diagnostics.Debug.WriteLine("[DIAG] RenderAsync Phase2 CSS start");
                try
                {
                    await CssLoader.ComputeAsync(dom, baseUri, fetchExternalCssAsync).ConfigureAwait(true);
                    System.Diagnostics.Debug.WriteLine("[DIAG] RenderAsync Phase2 CSS DONE");
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[DIAG] RenderAsync Phase2 CSS EXC " + ex.Message); }

                // Declarative Shadow DOM: Find templates and attach them as shadow roots
                try
                {
                    var templates = dom.Descendants().Where(n => n.Tag == "template" && n.Attr != null && n.Attr.ContainsKey("shadowrootmode")).ToList();
                    foreach (var template in templates)
                    {
                        var parent = template.Parent;
                        if (parent != null)
                        {
                            // The template content becomes the shadow root
                            parent.ShadowRoot = template.Children; 
                            // Detach the template itself from the main DOM
                            parent.Children.Remove(template);
                        }
                    }
                }
                catch { /* swallow */ }


                // Hint CSS that JS is enabled: swap 'no-js' -> 'js' on <html> element if present
                try
                {
                    var htmlNode0 = dom.Descendants().FirstOrDefault(n => n.Tag == "html");
                    if (htmlNode0 != null)
                    {
                        string cls = null; if (htmlNode0.Attr != null && htmlNode0.Attr.TryGetValue("class", out cls))
                        {
                            var parts = (cls ?? string.Empty).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                            bool changed = false;
                            if (parts.RemoveAll(s => string.Equals(s, "no-js", StringComparison.OrdinalIgnoreCase)) > 0) changed = true;
                            if (!parts.Any(s => string.Equals(s, "js", StringComparison.OrdinalIgnoreCase))) { parts.Add("js"); changed = true; }
                            if (changed)
                            {
                                htmlNode0.SetAttribute("class", string.Join(" ", parts));
                            }
                        }
                        else
                        {
                            htmlNode0.SetAttribute("class", "js");
                        }
                    }
                }
                catch { /* swallow */ }

                // 1.25) Prewarm images in the background so first paint can swap in sooner
                try { PrewarmImages(dom, baseUri, imageLoader, viewportWidth); } catch { /* swallow */ }

                bool allowJs = EnableJavaScript;
                bool richMode = _renderMode == RenderModeType.Rich;
                if (richMode)
                    allowJs = false; // RICH: skip NiL.JS full engine, MiniRunner only below
                if (forceJavascript.HasValue)
                {
                    allowJs = forceJavascript.Value;
                }

                // OPTIMIZATION: Scan DOM for scripts or event handlers.
                // If none found, disable JS to avoid heavy engine startup cost (10-30s on low-end devices).
                if (allowJs)
                {
                    bool hasScripts = false;
                    try
                    {
                        // Check for <script> tags
                        if (dom.Descendants().Any(n => string.Equals(n.Tag, "script", StringComparison.OrdinalIgnoreCase)))
                        {
                            hasScripts = true;
                        }
                        else
                        {
                            // Check for inline event handlers (onclick, onload, etc.)
                            foreach (var node in dom.SelfAndDescendants())
                            {
                                if (node.Attr != null)
                                {
                                    foreach (var key in node.Attr.Keys)
                                    {
                                        if (key.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                                        {
                                            hasScripts = true;
                                            break;
                                        }
                                    }
                                }
                                if (hasScripts) break;
                            }
                        }
                    }
                    catch { hasScripts = true; } // Fail safe: assume scripts exist if check fails

                    if (!hasScripts)
                    {
                        allowJs = false;
                        try { System.Diagnostics.Debug.WriteLine("[PERF] No scripts detected. Skipping JS engine initialization."); } catch { /* swallow */ }
                    }
                }

                // Performance fast-path: for clearly JS-heavy SPA/app-shell sites that
                // our engine cannot fully support (e.g., modern google.com), skip
                // JavaScript execution entirely to avoid 20-30s loads and double renders.
                if (allowJs && IsJsHeavyAppShell(baseUri))
                {
                    try { System.Diagnostics.Debug.WriteLine("[SAFE-MODE] Skipping JS for heavy app-shell site " + baseUri); } catch { /* swallow */ }
                    allowJs = false;
                }
                try { System.Diagnostics.Debug.WriteLine("[JS ENABLE] initial EnableJavaScript=" + EnableJavaScript + " baseUri=" + (baseUri!=null? baseUri.AbsoluteUri: "(null)")); } catch { /* swallow */ }

                var jsOverride = BrowserCoreHelpers.GetJsQueryOverride(baseUri);
                if (jsOverride.HasValue)
                {
                    allowJs = jsOverride.Value;
                    try { System.Diagnostics.Debug.WriteLine("[JS ENABLE] query override detected -> " + allowJs); } catch { /* swallow */ }
                }

                try { System.Diagnostics.Debug.WriteLine("[JS ENABLE] final allowJs=" + allowJs); } catch { /* swallow */ }

                if (allowJs)
                {
                    try
                    {
                        var noscripts = dom.Descendants()
                            .Where(n => string.Equals(n.Tag, "noscript", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        int removed = 0;
                        foreach (var node in noscripts)
                        {
                            node.Remove(); removed++;
                        }
                        try { System.Diagnostics.Debug.WriteLine("[JS ENABLE] removed noscript count=" + removed); } catch { /* swallow */ }
                    }
                    catch { /* swallow */ }
                }

                var cssFetcher = fetchExternalCssAsync ?? (async _ => string.Empty);

                JavaScriptEngine js = null;
                if (allowJs)
                {
                    js = new JavaScriptEngine(new JsHostAdapter(
                        navigate: onNavigate,
                        post: (_, __) => { },
                        status: _ => { },
                        requestRender: ScheduleRepaintFromJs,
                        invokeOnUiThread: action =>
                        {
                            try
                            {
                                var disp = UiThreadHelper.TryGetDispatcher();
                                // Fix: Never block threads with .Wait() in WP8.1
                                if (disp != null && !UiThreadHelper.HasThreadAccess(disp))
                                {
                                    var _ = disp.RunAsync(CoreDispatcherPriority.Normal, () => { try { action(); } catch { /* swallow */ } });
                                }
                                else
                                {
                                    try { action(); }
                                    catch { /* swallow */ }
                                }
                            }
                            catch { try { action(); } catch { /* swallow */ } }
                        }))
                    {
                        Sandbox = allowJs ? SandboxPolicy.AllowAll : SandboxPolicy.NoScripts,
                        AllowExternalScripts = true,
                        SubresourceAllowed = (u, kind) => true,
                        ExecuteInlineScriptsOnInnerHTML = allowJs
                    };
                }

                if (js != null)
                {
                    js.CookieBridge = scope => _jsCookieJar;
                    js.UseMiniPrattEngine = false;

                    // When a ResourceManager-backed fetcher is available, reuse it for
                    // script text as well so we benefit from its disk cache and
                    // origin partitioning. We pass secFetchDest="script".
                    if (fetchExternalCssAsync != null)
                    {
                        js.ExternalScriptFetcher = async (u, referer2) =>
                        {
                            try
                            {
                                // We don't have direct access to secFetchDest here, but
                                // the CSS fetcher already understands origin partitioning
                                // and disk cache behavior. For scripts, we can call back
                                // into BrowserApi/ResourceManager via ScriptFetcher when
                                // configured, or fall back to text fetch with a script
                                // Accept header in the navigation stack.
                                if (ScriptFetcher != null)
                                {
                                    return await ScriptFetcher(u).ConfigureAwait(false);
                                }
                            }
                            catch { /* swallow */ }
                            return null;
                        };
                    }
                }

                // Domain-specific tuning: Facebook relies on heavier JS bundles to
                // replace its initial skeleton UI. For facebook.* hosts we allow a
                // larger per-page JS byte budget so more of the app can execute.
                try
                {
                    if (js != null && baseUri != null)
                    {
                        var pageHost = (baseUri.Host ?? string.Empty).ToLowerInvariant();
                        if (pageHost.EndsWith("facebook.com", StringComparison.Ordinal))
                        {
                            // Roughly 512 KB; this is still far smaller than a
                            // desktop browser might execute, but enough to let
                            // primary bundles run.
                            js.PageScriptByteBudget = 512 * 1024;
                            System.Diagnostics.Debug.WriteLine("[JS-BUDGET] Facebook host detected; budget raised to " + js.PageScriptByteBudget + " bytes for " + baseUri);
                        }
                    }
                }
                catch { /* swallow */ }

                CaptureActiveContext(dom, baseUri, cssFetcher, imageLoader, onNavigate, viewportWidth, onFixedBackground, js);

                if (allowJs)
                {
                    var msg = "[DIAG] RenderAsync Phase3 JS RunScriptsAsync start";
                    System.Diagnostics.Debug.WriteLine(msg);
                    DevToolsLogger.Log(msg);
                    try
                    {
                        var jsTask = js.RunScriptsAsync(dom, baseUri);
                        if (await Task.WhenAny(jsTask, Task.Delay(JsPhaseTimeoutMs)) == jsTask)
                        {
                            await jsTask;
                            var m2 = "[DIAG] RenderAsync Phase3 JS DONE"; System.Diagnostics.Debug.WriteLine(m2); DevToolsLogger.Log(m2);
                        }
                                else
                                {
                                    try { System.Diagnostics.Debug.WriteLine("[DIAG] RenderAsync Phase3 JS TIMEOUT (" + JsPhaseTimeoutMs + "ms)"); } catch { }
                                    try { DevToolsLogger.Log("[DIAG] RenderAsync Phase3 JS TIMEOUT (" + JsPhaseTimeoutMs + "ms)"); } catch { }
                                }

                                // Patch D3 DOM manipulation after scripts have run
                                try { js?.PatchD3DomManipulation(); } catch (Exception patchEx) { System.Diagnostics.Debug.WriteLine("[DIAG] D3 patch error: " + patchEx.Message); }
                                // Install fetch interceptor to capture data URL
                                try { js?.InterceptFetch(); } catch (Exception fetchEx) { System.Diagnostics.Debug.WriteLine("[DIAG] Fetch interceptor error: " + fetchEx.Message); }
                                // Install XHR interceptor to capture XMLHttpRequest data fetches
                                try { js?.InterceptXhr(); } catch (Exception xhrEx) { System.Diagnostics.Debug.WriteLine("[DIAG] XHR interceptor error: " + xhrEx.Message); }
                                // Search for global data objects
                                try { js?.SearchGlobalData(); } catch (Exception dataEx) { System.Diagnostics.Debug.WriteLine("[DIAG] Global data search error: " + dataEx.Message); }
                                // Kick off G.1 SVG extraction (fire-and-forget) — Phase G.1 fast path
#pragma warning disable CS4014
                                try { TriggerDelayedSvgExtractionAsync(); } catch { }
#pragma warning restore CS4014
                            }
                            catch (Exception ex) { var m3 = "[DIAG] RenderAsync Phase3 JS EXC " + ex.Message; System.Diagnostics.Debug.WriteLine(m3); DevToolsLogger.Log(m3); }
                        }
                else if (richMode)
                {
                    // RICH mode: run MiniRunner only for setTimeout/clearTimeout + analytics kill
                    var msg = "[DIAG] RenderAsync Phase3 RICH MiniRunner start";
                    System.Diagnostics.Debug.WriteLine(msg);
                    DevToolsLogger.Log(msg);
                    try { RunRichMiniRunner(dom, js); var m2 = "[DIAG] RenderAsync Phase3 RICH MiniRunner DONE"; System.Diagnostics.Debug.WriteLine(m2); DevToolsLogger.Log(m2); } catch (Exception ex) { var m3 = "[DIAG] RenderAsync Phase3 RICH MiniRunner EXC " + ex.Message; System.Diagnostics.Debug.WriteLine(m3); DevToolsLogger.Log(m3); }
                }
                else { var msg = "[DIAG] RenderAsync Phase3 JS SKIPPED allowJs=" + allowJs; System.Diagnostics.Debug.WriteLine(msg); DevToolsLogger.Log(msg); }

                var msg4 = "[DIAG] RenderAsync Phase4 BuildVisualTreeAsync start";
                System.Diagnostics.Debug.WriteLine(msg4);
                DevToolsLogger.Log(msg4);
                FrameworkElement element = null;
                try
                {
                    element = await BuildVisualTreeAsync(dom, baseUri, cssFetcher, imageLoader, onNavigate, js, viewportWidth, onFixedBackground, includeDiagnosticsBanner: false).ConfigureAwait(false);
                }
                catch (JSException jex)
                {
                    System.Diagnostics.Debug.WriteLine("[RENDER] Phase4 JSException swallowed: " + jex.Message);
                    element = null;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[RENDER] Phase4 Exception swallowed: " + ex.GetType().Name + ": " + ex.Message);
                    element = null;
                }
                var msg5 = "[DIAG] RenderAsync Phase4 BuildVisualTreeAsync DONE element=" + (element != null ? element.GetType().Name : "null");
                System.Diagnostics.Debug.WriteLine(msg5);
                DevToolsLogger.Log(msg5);

                // Auto-fallback (re-render without JS) only makes sense when we
                // actually attempted JS and are not in app-shell safe-mode.
                if (allowJs && !disableAutoFallback && !IsJsHeavyAppShell(baseUri))
                {
                    bool isEmpty;
                    try { isEmpty = element == null || IsEffectivelyEmpty(element); }
                    catch { isEmpty = element == null; }

                    if (isEmpty)
                    {
                        try { System.Diagnostics.Debug.WriteLine("[RENDER] JS path empty, retrying without scripts"); } catch { /* swallow */ }
                        await RaiseLoadingChangedAsync(false);
                        return await RenderAsync(html, baseUri, fetchExternalCssAsync, imageLoader, onNavigate, viewportWidth, onFixedBackground, forceJavascript: false, disableAutoFallback: true).ConfigureAwait(false);
                    }
                }

                try { System.Diagnostics.Debug.WriteLine("[RENDER] Final element null=" + (element == null) + " empty=" + (element != null && IsEffectivelyEmpty(element))); } catch { /* swallow */ }
                TriggerDelayedSvgRefresh();
                return element;
            }
            finally
            {
                _isRendering = 0;
                await RaiseLoadingChangedAsync(false);
            }
        }

        /// <summary>
        /// Convenience wrapper to kick off a render without awaiting the resulting element.
        /// Useful for non-visual navigations when only DOM/state is needed.
        /// </summary>
        public void LoadHtml(
            string html,
            Uri baseUri,
            Func<Uri, Task<string>> fetchExternalCssAsync,
            Func<Uri, Task<IRandomAccessStream>> imageLoader,
            Action<Uri> onNavigate,
            double? viewportWidth = null,
            Action<Windows.UI.Xaml.Media.Brush> onFixedBackground = null)
        {
            try { var _ = RenderAsync(html, baseUri, fetchExternalCssAsync, imageLoader, onNavigate, viewportWidth, onFixedBackground); }
            catch { /* swallow */ }
        }

        /// <summary>Expose the current active Lite DOM (last parsed).</summary>
        public LiteElement GetActiveDom()
        {
            return _activeDom;
        }

        // ---------------- Cookie helpers for host APIs ----------------
        public IReadOnlyDictionary<string,string> GetCookieSnapshot(Uri scope)
        {
            var dict = new Dictionary<string,string>(StringComparer.Ordinal);
            try
            {
                var u = scope ?? _activeBaseUri; if (u == null) return dict;
                var coll = _jsCookieJar.GetCookies(u);
                if (coll != null)
                {
                    foreach (System.Net.Cookie c in coll)
                    {
                        if (!dict.ContainsKey(c.Name)) dict[c.Name] = c.Value ?? string.Empty;
                    }
                }
            }
            catch { /* swallow */ }
            return dict;
        }

        public void SetCookie(Uri scope, string name, string value, string path = "/")
        {
            try
            {
                var u = scope ?? _activeBaseUri; if (u == null) return;
                var cookie = new System.Net.Cookie(name ?? string.Empty, value ?? string.Empty, path ?? "/", u.Host);
                _jsCookieJar.Add(u, cookie);
            }
            catch { /* swallow */ }
        }

        public void DeleteCookie(Uri scope, string name)
        {
            try
            {
                var u = scope ?? _activeBaseUri; if (u == null) return;
                // Overwrite with expired cookie
                var expired = new System.Net.Cookie(name ?? string.Empty, string.Empty, "/", u.Host) { Expires = DateTime.UtcNow.AddDays(-1) };
                _jsCookieJar.Add(u, expired);
            }
            catch { /* swallow */ }
        }

        private static bool IsEffectivelyEmpty(FrameworkElement fe)
        {
            try
            {
                if (fe == null) return true;
                // Deep inspect: if only canvases with no children or no text/images, treat as empty
                return !HasMeaningfulContent(fe);
            }
            catch { return false; }
        }

        private static string NormalizeLoadingKey(string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(value)) return string.Empty;
                var trimmed = value.Trim();
                if (trimmed.Length == 0) return string.Empty;
                var filtered = new string(trimmed.Where(char.IsLetterOrDigit).ToArray());
                return filtered.ToLowerInvariant();
            }
            catch { return string.Empty; }
        }

        private static bool HasMeaningfulContent(DependencyObject node)
        {
            try
            {
                if (node == null) return false;
                if (node is ProgressRing || node is ProgressBar) return false;
                var progressIndicatorType = node.GetType().Name;
                if (progressIndicatorType.IndexOf("Progress", StringComparison.OrdinalIgnoreCase) >= 0 && !(node is Button))
                    return false;
                // Text presence
                var tb = node as TextBlock;
                if (tb != null)
                {
                    var candidate = tb.Text ?? string.Empty;
                    if (tb.Inlines != null && tb.Inlines.Count > 0)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var inline in tb.Inlines.OfType<Run>())
                        {
                            if (!string.IsNullOrWhiteSpace(inline.Text)) sb.Append(inline.Text);
                        }
                        if (sb.Length > 0) candidate = sb.ToString();
                    }
                    if (string.IsNullOrWhiteSpace(candidate)) return false;
                    return !_loadingTokens.Contains(NormalizeLoadingKey(candidate));
                }
                var rtb = node as RichTextBlock;
                if (rtb != null)
                {
                    if (rtb.Blocks == null || rtb.Blocks.Count == 0) return false;
                    foreach (var block in rtb.Blocks.OfType<Paragraph>())
                    {
                        foreach (var inline in block.Inlines.OfType<Run>())
                        {
                            if (string.IsNullOrWhiteSpace(inline.Text)) continue;
                            if (!_loadingTokens.Contains(NormalizeLoadingKey(inline.Text))) return true;
                        }
                    }
                    return false;
                }
                // Images or controls indicate content
                if (node is Image || node is Button || node is HyperlinkButton || node is RichEditBox) return true;
                // Border: check child
                var border = node as Border; if (border != null) return HasMeaningfulContent(border.Child);
                // ScrollViewer: check content
                var sv = node as ScrollViewer; if (sv != null) return HasMeaningfulContent(sv.Content as DependencyObject);
                // Panel: check children, but ignore pure overlay canvases with no children
                var panel = node as Panel;
                if (panel != null)
                {
                    bool any = false;
                    for (int i = 0; i < panel.Children.Count; i++)
                    {
                        var ch = panel.Children[i];
                        // ignore empty canvas overlays
                        var c = ch as Canvas; if (c != null && c.Children != null && c.Children.Count == 0) continue;
                        if (HasMeaningfulContent(ch)) { any = true; break; }
                    }
                    return any;
                }
                // Grid etc: fall back to visual tree walk
                int count = Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
                if (count == 0)
                {
                    // No children; not meaningful by default
                    return false;
                }
                for (int i = 0; i < count; i++)
                {
                    if (HasMeaningfulContent(Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i))) return true;
                }
                return false;
            }
            catch { return true; }
        }

        private static void ApplyDefaultForeground(DependencyObject node, SolidColorBrush desired)
        {
            try
            {
                if (node == null || desired == null) return;
                var tb = node as TextBlock; if (tb != null) { if (tb.Foreground == null) tb.Foreground = desired; }
                var rtb = node as RichTextBlock; if (rtb != null) { if (rtb.Foreground == null) rtb.Foreground = desired; }
                var border = node as Border; if (border != null) { if (border.Child != null) ApplyDefaultForeground(border.Child, desired); return; }
                var panel = node as Panel; if (panel != null && panel.Children != null)
                {
                    for (int i = 0; i < panel.Children.Count; i++) ApplyDefaultForeground(panel.Children[i], desired);
                    return;
                }
                var contentCtrl = node as ContentControl;
                if (contentCtrl != null)
                {
                    var d = contentCtrl.Content as DependencyObject;
                    if (d != null) ApplyDefaultForeground(d, desired);
                    return;
                }
                // Fallback: walk visual children when possible
                int count = Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++) ApplyDefaultForeground(Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i), desired);
            }
            catch { /* swallow */ }
        }

        private static bool IsSvgOrHasSvgAncestor(LiteElement node)
        {
            if (string.Equals(node.Tag, "svg", StringComparison.OrdinalIgnoreCase))
                return true;
            var cur = node.Parent;
            while (cur != null)
            {
                if (string.Equals(cur.Tag, "svg", StringComparison.OrdinalIgnoreCase))
                    return true;
                cur = cur.Parent;
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────
        //  SVG EXTRACTION TRIGGER  (Phase G.1 — Session 5.07, per architect)
        // ─────────────────────────────────────────────────────────────
        private async Task TriggerDelayedSvgExtractionAsync()
        {
            try
            {
                if (_activeJs == null || _activeDom == null) return;

                // Diagnostic: dump immediately to see if D3 built anything at all
                System.Diagnostics.Debug.WriteLine("[SVG] Immediate dump (before D3 settles):");
                _activeJs.DumpSvgDom();

                // Wait for D3 force simulation to settle (2s)
                await Task.Delay(2000).ConfigureAwait(false);

                System.Diagnostics.Debug.WriteLine("[SVG] Post-delay dump (after 2s):");
                _activeJs.DumpSvgDom();

                var svgRoots = _activeJs.GetDocumentSvgRoots();

                if (svgRoots.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[SVG] Still no SVG roots after 2s. "
                        + "D3 may not be executing. Check [d3-err:...] log lines.");
                    return;
                }

                System.Diagnostics.Debug.WriteLine("[SVG] Injecting " + svgRoots.Count + " SVG root(s) into renderer.");

                if (_currentRenderer != null)
                {
                    await _currentRenderer.InjectSvgAsync(svgRoots).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SVG] TriggerDelayedSvgExtractionAsync EXC: " + ex.GetType().Name + " - " + ex.Message);
            }
        }

        private void TriggerDelayedSvgRefresh()
        {
            System.Diagnostics.Debug.WriteLine("[DIAG] _activeJs is " + (_activeJs != null ? "not null" : "null"));
            if (System.Threading.Interlocked.Exchange(ref _svgRefreshPending, 1) != 0)
                return;
            if (_activeDom == null) { _svgRefreshPending = 0; return; }
            bool hasSvg = false;
            try
            {
                foreach (var n in _activeDom.Descendants())
                {
                    if (string.Equals(n.Tag, "svg", StringComparison.OrdinalIgnoreCase))
                    { hasSvg = true; break; }
                }
            }
            catch { _svgRefreshPending = 0; return; }
            if (!hasSvg) { _svgRefreshPending = 0; return; }

            var disp = _uiDispatcher ?? UiThreadHelper.TryGetDispatcher();
            if (disp == null) { _svgRefreshPending = 0; return; }

            // Diagnostic: search for global data and test D3 DOM manipulation
            try
            {
                if (_activeJs != null)
                {
                    // Search for global data objects
                    try { _activeJs.SearchGlobalData(); } catch { }

                    // First, search for any global timeline/graph init functions
                    _activeJs.RunInline(@"
                        var found = [];
                        for (var key in window) {
                            if (key.toLowerCase().includes('timeline') || key.toLowerCase().includes('graph') || key.toLowerCase().includes('init')) {
                                found.push(key);
                            }
                        }
                        console.log('[DIAG] Potential init functions: ' + found.join(', '));
                    ");

                    // Then, try to manually draw a circle using D3 immediately (no delay)
                    _activeJs.RunInline(@"
                        (function() {
                            var container = document.getElementById('timeline');
                            if (container) {
                                if (typeof d3 !== 'undefined' && d3.select) {
                                    var svg = d3.select(container).append('svg').attr('width', 400).attr('height', 200);
                                    svg.append('circle').attr('cx', 100).attr('cy', 100).attr('r', 50).attr('fill', 'red');
                                    console.log('[DIAG] Manual circle added via D3');
                                } else {
                                    console.log('[DIAG] D3 not available for manual circle');
                                }
                            } else {
                                console.log('[DIAG] Timeline container not found for manual circle');
                            }
                        })();
                    ");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[DIAG] D3 test: _activeJs is null");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[DIAG] D3 test exception: " + ex.Message);
            }

            int[] delays = { 3000, 4000, 5000 };
                    Task.Run(async () =>
                    {
                        try
                        {
                            for (int attempt = 0; attempt < delays.Length; attempt++)
                            {
                                try { await Task.Delay(delays[attempt]).ConfigureAwait(false); }
                                catch { break; }

                                if (_activeDom == null) break;

                                // Check if any non-search-icon SVG has children (D3 populated it)
                                bool found = false;
                                string foundId = "?", foundCls = "?", foundChildren = "0";
                                int totalSvgCount = 0;
                                int svgWithChildren = 0;
                                foreach (var n in _activeDom.Descendants())
                                {
                                    if (!string.Equals(n.Tag, "svg", StringComparison.OrdinalIgnoreCase))
                                        continue;
                                    totalSvgCount++;
                                    string cls = null, id = null;
                                    try { if (n.Attr != null) { n.Attr.TryGetValue("class", out cls); n.Attr.TryGetValue("id", out id); } } catch { }
                                    int cc = n.Children?.Count ?? 0;
                                    foundId = id ?? "?";
                                    foundCls = cls ?? "?";
                                    foundChildren = cc.ToString();
                                    // Skip search icon (1 child, class=search-icon)
                                    if (cc <= 1 && "search-icon".Equals(cls, StringComparison.OrdinalIgnoreCase))
                                        continue;
if (cc > 0) { svgWithChildren++; found = true; break; }
                                }

                                System.Diagnostics.Debug.WriteLine("[DIAG:SVG] Delay check attempt=" + attempt + " delay=" + delays[attempt] + "ms totalSVG=" + totalSvgCount + " svgWithChildren=" + svgWithChildren + " found=" + found + " id=" + foundId + " class=" + foundCls + " children=" + foundChildren);

                                // If timeline SVG exists but has no children, try to manually create an SVG using DOM methods
                                if (!found && totalSvgCount > 0)
                                {
                                    try
                                    {
                                        // Search global data objects each attempt
                                        try { _activeJs?.SearchGlobalData(); } catch { }
                                        // Install XHR interceptor if not already (try each attempt)
                                        try { _activeJs?.InterceptXhr(); } catch { }

                                        // Diagnostic: search for global timeline/graph init functions
                                        _activeJs?.RunInline(@"
                                            (function() {
                                                try {
                                                    console.log('[DIAG] Searching for global init functions...');
                                                    var candidates = [];
                                                    for (var key in window) {
                                                        if (typeof window[key] === 'function') {
                                                            var lower = key.toLowerCase();
                                                            if (lower.includes('timeline') || lower.includes('graph') || lower.includes('init') || lower.includes('create') || lower.includes('render')) {
                                                                candidates.push(key);
                                                            }
                                                        }
                                                    }
                                                    console.log('[DIAG] Found candidate functions: ' + (candidates.length ? candidates.join(', ') : 'none'));
                                                    // Try to call any candidate that looks promising
                                                    for (var i = 0; i < candidates.length; i++) {
                                                        var fnName = candidates[i];
                                                        try {
                                                            if (fnName.toLowerCase().includes('timeline') || fnName.toLowerCase().includes('graph')) {
                                                                window[fnName]();
                                                                console.log('[DIAG] Called function: ' + fnName);
                                                            }
                                                        } catch(e) { /* ignore */ }
                                                    }
                                                } catch(e) { console.log('[DIAG] Global search error: ' + e.message); }
                                            })();
                                        ");

                                        // Also force DOMContentLoaded via alternative method (call all functions registered via addEventListener? not easy)
                                        // Instead, directly dispatch a synthetic event using our own custom event object (since NiL.JS lacks Event)
                                        _activeJs.RunInline(@"
                                            (function() {
                                                try {
                                                    if (typeof window.__fireDOMContentLoaded === 'undefined') {
                                                        var evt = { type: 'DOMContentLoaded', target: document };
                                                        var listeners = document._events && document._events['DOMContentLoaded'];
                                                        if (listeners) {
                                                            for (var i = 0; i < listeners.length; i++) {
                                                                try { listeners[i](evt); } catch(e) {}
                                                            }
                                                        }
                                                        window.__fireDOMContentLoaded = true;
                                                        console.log('[DIAG] Manually fired DOMContentLoaded listeners');
                                                    }
                                                } catch(e) { console.log('[DIAG] Fire DOMContentLoaded error: ' + e.message); }
                                            })();
                                        ");

                                        // Also test D3's append method to see why it fails
                                        _activeJs.RunInline(@"
                                            (function() {
                                                try {
                                                    var container = document.getElementById('timeline');
                                                    if (!container) { console.log('[DIAG] D3 test: no container'); return; }
                                                    var selection = d3.select(container);
                                                    console.log('[DIAG] D3 selection: ' + (selection ? 'ok' : 'null'));
                                                    if (selection && typeof selection.append === 'function') {
                                                        var svg = selection.append('svg').attr('width', 400).attr('height', 200);
                                                        console.log('[DIAG] D3 svg appended');
                                                        var circle = svg.append('circle').attr('cx', 100).attr('cy', 100).attr('r', 50).attr('fill', 'red');
                                                        console.log('[DIAG] D3 circle appended');
                                                    } else {
                                                        console.log('[DIAG] D3 selection.append is not a function');
                                                    }
                                                } catch(e) {
                                                    console.log('[DIAG] D3 test error: ' + (e.message || e));
                                                }
                                            })();
                                        ");

                                        // Route/navigation simulation — try to activate the graph page
                                        _activeJs.RunInline(@"
                                            (function() {
                                                try {
                                                    console.log('[DIAG:NAV] location href=' + (window.location.href || 'none') + ' hash=' + (window.location.hash || 'none'));
                                                    // Check for nav links by text content
                                                    var navLinks = document.querySelectorAll('a');
                                                    var graphNav = null;
                                                    for (var i = 0; i < navLinks.length; i++) {
                                                        var t = (navLinks[i].textContent || '').toLowerCase().trim();
                                                        var h = (navLinks[i].getAttribute('href') || '').toLowerCase();
                                                        if (t === 'network' || t === 'timeline' || t === 'graph' ||
                                                            h.indexOf('network') >= 0 || h.indexOf('timeline') >= 0) {
                                                            graphNav = navLinks[i];
                                                            console.log('[DIAG:NAV] Found graph link #' + i + ' text=[' + t + '] href=[' + h + ']');
                                                        }
                                                    }
                                                    if (graphNav) {
                                                        if (typeof graphNav.click === 'function') {
                                                            try { graphNav.click(); console.log('[DIAG:NAV] Clicked graph nav link'); } catch(e) { try { __diagLog('[DIAG:NAV] Click error'); } catch(_) {} }
                                                        }
                                                        // Also try dispatching a click event
                                                        if (typeof document.createEvent === 'function') {
                                                            try {
                                                                var evt = document.createEvent('MouseEvents');
                                                                if (evt && typeof evt.initEvent === 'function') {
                                                                    evt.initEvent('click', true, true);
                                                                    graphNav.dispatchEvent(evt);
                                                                    console.log('[DIAG:NAV] Dispatched click event on nav link');
                                                                }
                                                            } catch(e) { try { __diagLog('[DIAG:NAV] DispatchEvent error'); } catch(_) {} }
                                                        }
                                                    } else {
                                                        console.log('[DIAG:NAV] No graph nav link found — checking hash-based routing');
                                                    }
                                                    // Set hash to trigger route
                                                    try {
                                                        var oldHash = window.location.hash;
                                                        window.location.hash = '#/network';
                                                        console.log('[DIAG:NAV] Set hash from [' + oldHash + '] to #/network');
                                                    } catch(e) { try { __diagLog('[DIAG:NAV] Hash set error'); } catch(_) {} }
                                                    // Fire hashchange event manually
                                                    try {
                                                        if (typeof window.__hashchangeFired === 'undefined') {
                                                            var listeners = document._events && document._events['hashchange'];
                                                            if (listeners) {
                                                                for (var i = 0; i < listeners.length; i++) {
                                                                    try { listeners[i]({ type: 'hashchange', newURL: '#/network', oldURL: '' }); } catch(e) {}
                                                                }
                                                                console.log('[DIAG:NAV] Fired hashchange listeners');
                                                            }
                                                            window.__hashchangeFired = true;
                                                        }
                                                    } catch(e) { try { __diagLog('[DIAG:NAV] hashchange fire error'); } catch(_) {} }
                                                } catch(e) { try { __diagLog('[DIAG:NAV] Error in nav simulation'); } catch(_) {} }
                                            })();
                                        ");
                                        // Force re-execute the main module after navigation
                                        _activeJs.RunInline(@"
                                            (function() {
                                                try {
                                                    var S = globalThis.System;
                                                    if (!S || !S.registry || !S.registry._entries) {
                                                        console.log('[DIAG] No System registry to re-execute');
                                                        return;
                                                    }
                                                    var executed = false;
                                                    for (var url in S.registry._entries) {
                                                        var m = S.registry._entries[url];
                                                        if (m && typeof m.declare === 'function') {
                                                            var _export = function(n,v){};
                                                            var _ctx = { meta: { url: url } };
                                                            var declared = m.declare(_export, _ctx);
                                                            if (declared && typeof declared.execute === 'function') {
                                                                declared.execute();
                                                                console.log('[DIAG] Re-executed module: ' + url);
                                                                executed = true;
                                                            }
                                                        }
                                                    }
                                                    if (!executed) console.log('[DIAG] No module to re-execute');
                                                } catch(e) {
                                                    console.log('[DIAG] Re-execution error: ' + (e.message || e));
                                                }
                                            })();
                                        ");
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine("[DIAG] Static SVG exception: " + ex.Message);
                                    }
                                }

                                if (found)
                                {
                                    System.Diagnostics.Debug.WriteLine("[DIAG:SVG] Delayed refresh: SVG children detected");
                                    var element = await RefreshAsyncInternal(includeDiagnosticsBanner: false).ConfigureAwait(false);
                                    if (element != null)
                                        await DispatchRepaintAsync(element).ConfigureAwait(false);
                                    return;
                                }
                            }
                            // All delays exhausted without finding SVG children
                            System.Diagnostics.Debug.WriteLine("[DIAG:SVG] All delays exhausted, no SVG children found - final state check");
                            await LogTimelineStateIfEmpty().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[DIAG:SVG] Delayed refresh EXC " + ex.GetType().Name + " - " + ex.Message);
                }
                finally { System.Threading.Interlocked.Exchange(ref _svgRefreshPending, 0); }
            });
        }

        // Diagnostic: dump timeline element state if SVG still empty after all attempts
        private async Task LogTimelineStateIfEmpty()
        {
            if (_activeDom == null) return;
            try
            {
                var timelineDiv = _activeDom.Descendants().FirstOrDefault(n => n.Tag == "div" && n.Attr != null && n.Attr.ContainsKey("id") && n.Attr["id"] == "timeline");
                if (timelineDiv == null)
                {
                    System.Diagnostics.Debug.WriteLine("[DIAG:SVG] Timeline div element not found in DOM");
                    return;
                }
                var svg = timelineDiv.Children?.FirstOrDefault(n => n.Tag == "svg");
                if (svg == null) 
                { 
                    System.Diagnostics.Debug.WriteLine("[DIAG:SVG] No SVG child under timeline div");
                    return;
                }
                var svgChildren = svg.Children?.Count ?? 0;
                System.Diagnostics.Debug.WriteLine("[DIAG:SVG] Timeline SVG children=" + svgChildren + " after all delays - D3 may not have rendered");
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[DIAG:SVG] LogTimelineStateIfEmpty EXC: " + ex.Message); }
        }
    }
}
