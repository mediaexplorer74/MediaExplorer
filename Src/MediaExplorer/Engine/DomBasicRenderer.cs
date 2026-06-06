using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;
using Windows.UI.Xaml.Markup;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Storage.Streams;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;

namespace BrowserCore.Engine
{
    public partial class DomBasicRenderer
    {
        public DomBasicRenderer()
        {
        }

        // Phase S.1: NaN/Infinity/negative guard for size properties.
        // Used at hot-path assignments to FrameworkElement.Width/Height and
        // to GridLength construction. Returns the fallback (default 0) when
        // the value would otherwise poison XAML layout.
        // Logs once per (source, call site) tuple via DevToolsLogger to keep
        // diagnostic value while not spamming per-frame.
        private static double SanitizeSize(double v, double fallback, string source)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v < 0)
            {
                try { DevToolsLogger.Log($"[DIAG:NaN] source={source} val={v} → fallback={fallback}"); } catch { }
                return fallback;
            }
            return v;
        }

        private static double SanitizeSize(double v, string source)
            => SanitizeSize(v, 0, source);

        private async Task<FrameworkElement> DispatchTagAsync(LiteElement n, Uri baseUri, Action<Uri> onNavigate, JavaScriptEngine js, CancellationToken ct)
        {
            // Check CSS display value for Grid/Flex containers
            var css = TryGetCss(n);
            if (css != null)
            {
                // Phase DIAG (issue A): trace flex/grid detection per element
                var dispRaw = GetDisplayValue(css);
                var dispNorm = NormalizeDisplayValue(dispRaw);
                try
                {
                    bool isGrid = IsGridContainer(css);
                    bool isFlex = !isGrid && IsFlexContainer(css);
                    if (dispNorm != null && (dispNorm == "grid" || dispNorm == "inline-grid" || dispNorm.Contains("flex") || dispNorm.Contains("flexbox")))
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "[DIAG:FLEXGRID] tag=" + n.Tag +
                            " id=" + (n.Attr != null && n.Attr.ContainsKey("id") ? n.Attr["id"] : "") +
                            " display.raw=\"" + (dispRaw ?? "<null>") + "\"" +
                            " display.norm=\"" + (dispNorm ?? "<null>") + "\"" +
                            " → IsGrid=" + isGrid + " IsFlex=" + isFlex +
                            " → path=" + (isGrid ? "RenderCssGridAsync" : isFlex ? "MakeGridFallbackAsync" : "fallback-to-block"));
                    }
                }
                catch { /* swallow */ }

                if (IsGridContainer(css))
                    return await RenderCssGridAsync(n, baseUri, onNavigate, js, ct);
                if (IsFlexContainer(css))
                    return await MakeGridFallbackAsync(n, baseUri, onNavigate, js, ct);
            }

            switch (n.TagId)
            {
                case HtmlTag.Div:
                case HtmlTag.P:
                case HtmlTag.H1: case HtmlTag.H2: case HtmlTag.H3:
                case HtmlTag.H4: case HtmlTag.H5: case HtmlTag.H6:
                case HtmlTag.Article: case HtmlTag.Section: case HtmlTag.Nav:
                case HtmlTag.Header: case HtmlTag.Footer: case HtmlTag.Main:
                case HtmlTag.Aside: case HtmlTag.Figure: case HtmlTag.Figcaption:
                case HtmlTag.Blockquote: case HtmlTag.Pre: case HtmlTag.Address:
                case HtmlTag.Center: case HtmlTag.Form: case HtmlTag.Dialog:
                case HtmlTag.Details: case HtmlTag.Summary:
                    return await RenderBlockAsync(n, baseUri, onNavigate, js, ct);

                case HtmlTag.Table:
                    return await RenderTableAsync(n, baseUri, onNavigate, js, ct);

                case HtmlTag.Ul:
                    return await MakeListAsync(n, false, baseUri, onNavigate, js, ct);
                case HtmlTag.Ol:
                    return await MakeListAsync(n, true, baseUri, onNavigate, js, ct);

                case HtmlTag.Svg:
                    return RenderInlineSvg(n);
                case HtmlTag.Img:
                    return await MakeImageAsync(n, baseUri, ct);
                case HtmlTag.Picture:
                    return await MakePictureAsync(n, baseUri, ct);

                case HtmlTag.Textarea:
                    return MakeTextarea(n);
                case HtmlTag.Select:
                    return MakeSelect(n);
                case HtmlTag.Button:
                    return await MakeButtonAsync(n, baseUri, onNavigate, js, ct);
                case HtmlTag.A:
                    return await MakeLinkAsync(n, baseUri, onNavigate, js, ct);

                case HtmlTag.Script: case HtmlTag.Style:
                case HtmlTag.Head: case HtmlTag.Title:
                case HtmlTag.Meta: case HtmlTag.Link:
                case HtmlTag.Noscript:
                    return null;

                default:
                    return await RenderGenericContainerAsync(n, baseUri, onNavigate, js, ct);
            }
        }

        private static readonly HashSet<string> FlexDisplayKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "flex",
            "inline-flex",
            "flexbox",
            "inline-flexbox",
            "-ms-flexbox",
            "-ms-inline-flexbox",
            "-webkit-flex",
            "-webkit-inline-flex"
        };

        private Brush LinkBrush = new SolidColorBrush(Windows.UI.Colors.Blue);

        private static string GetDisplayValue(CssComputed css)
        {
            if (css == null) return null;

            var display = css.Display;
            if (string.IsNullOrWhiteSpace(display) && css.Map != null)
            {
                string raw;
                if (css.Map.TryGetValue("display", out raw))
                {
                    display = raw;
                }
            }

            return display;
        }

        private static string NormalizeDisplayValue(string display)
        {
            return string.IsNullOrWhiteSpace(display) ? null : display.Trim().ToLowerInvariant();
        }

        private static bool IsFlexContainer(CssComputed css)
        {
            var normalized = NormalizeDisplayValue(GetDisplayValue(css));
            if (string.IsNullOrEmpty(normalized)) return false;
            if (FlexDisplayKeywords.Contains(normalized)) return true;

            // Legacy keywords occasionally appear without being in the allow-list (e.g., vendor shorthands)
            return normalized.EndsWith("flex", StringComparison.Ordinal) || normalized.EndsWith("flexbox", StringComparison.Ordinal);
        }

        private static bool IsInlineFlex(CssComputed css)
        {
            var normalized = NormalizeDisplayValue(GetDisplayValue(css));
            if (string.IsNullOrEmpty(normalized)) return false;
            return normalized == "inline-flex" || normalized == "inline-flexbox" || normalized == "-ms-inline-flexbox" || normalized == "-webkit-inline-flex";
        }

        private static bool IsGridContainer(CssComputed css)
        {
            var normalized = NormalizeDisplayValue(GetDisplayValue(css));
            return normalized == "grid" || normalized == "inline-grid";
        }

        // Some event args types on WP8.1 don't expose Handled. Use reflection to set when available.
        private static void TrySetHandled(object e)
        {
            try
            {
                if (e == null) return;
                var t = e.GetType();
                System.Reflection.PropertyInfo p = null;
                try { p = t.GetRuntimeProperty("Handled"); } catch { /* swallow */ }
                if (p == null)
                {
                    try { var ti = t.GetTypeInfo(); if (ti != null) p = ti.GetDeclaredProperty("Handled"); } catch { /* swallow */ }
                }
                if (p != null && p.CanWrite && p.PropertyType == typeof(bool))
                {
                    try { p.SetValue(e, true); } catch { /* swallow */ }
                }
            }
            catch { /* swallow */ }
        }

        // ---------- Engine integration ----------
        public event Action<string> StatusMessage;
        public event Action<Uri> LinkLongPressed;
        public event EventHandler<FormSubmitEventArgs> FormSubmit;

        // These are still optional and respected by the renderer
        public Dictionary<LiteElement, CssComputed> ComputedStyles { get; set; }
        public JavaScriptEngine Js { get; set; }
        // Optional loader to fetch HTML for iframes or embedded documents
        public Func<Uri, Task<string>> HtmlLoader { get; set; }
        public Action<Uri, string> OnPost { get; set; }
        public Func<Uri, Task<IRandomAccessStream>> ImageLoader { get; set; }

        private static readonly string[] HiddenTags = { "script", "style", "head", "title", "meta", "link" };
        private Uri _baseUriForResources; // set during BuildAsync
        private readonly System.Collections.Generic.List<System.Tuple<FrameworkElement, CssComputed>> _pendingFixed = new System.Collections.Generic.List<System.Tuple<FrameworkElement, CssComputed>>();
        private Canvas _fixedBehind;
        private Canvas _fixedFront;

        private static bool LooksSvg(Uri u) =>
            u != null && (u.AbsolutePath?.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) == true);

        // ---------- SVG reflection cache ----------
        private static readonly Type SvgType =
            Type.GetType("Windows.UI.Xaml.Media.Imaging.SvgImageSource, Windows, ContentType=WindowsRuntime");
        private static readonly MethodInfo SvgSetSourceAsync =
            SvgType?.GetRuntimeMethod("SetSourceAsync", new[] { typeof(IRandomAccessStream) });
        private static readonly PropertyInfo SvgUriSourceProp =
            SvgType?.GetRuntimeProperty("UriSource");
        private static readonly Type TextDecorationsType =
            Type.GetType("Windows.UI.Text.TextDecorations, Windows, ContentType=WindowsRuntime");
        private static readonly PropertyInfo TextBlockTextDecorationsProp =
            typeof(TextBlock).GetRuntimeProperty("TextDecorations");
        private static readonly int TextDecorationsValueNone =
            TextDecorationsType != null ? Convert.ToInt32(Enum.Parse(TextDecorationsType, "None")) : 0;
        private static readonly int TextDecorationsValueUnderline =
            TextDecorationsType != null ? Convert.ToInt32(Enum.Parse(TextDecorationsType, "Underline")) : 0;
        private static readonly int TextDecorationsValueStrikethrough =
            TextDecorationsType != null ? Convert.ToInt32(Enum.Parse(TextDecorationsType, "Strikethrough")) : 0;
        private static readonly bool SupportsTextDecorations =
            TextDecorationsType != null && TextBlockTextDecorationsProp != null;

        // Sticky behavior: keep an element stuck to top when scrolled past its original position
        private void AttachStickyBehavior(FrameworkElement fe, CssComputed css)
        {
            if (fe == null) return;
            fe.Loaded += (s, e) =>
            {
                try
                {
                    var scroller = FindAncestorScrollViewer(fe);
                    if (scroller == null) return;
                    double top = css != null && css.Top.HasValue ? css.Top.Value : 0;
                    double bottom = css != null && css.Bottom.HasValue ? css.Bottom.Value : double.NaN;
                    // Compute original Y relative to scroller content
                    double originY = 0;
                    try
                    {
                        var content = scroller.Content as UIElement;
                        if (content != null)
                        {
                            var pt = fe.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point(0, 0));
                            originY = pt.Y;
                        }
                    }
                    catch { /* swallow */ }

                    var trans = fe.RenderTransform as TranslateTransform;
                    if (trans == null) { trans = new TranslateTransform(); fe.RenderTransform = trans; }

                    scroller.ViewChanged += (s2, e2) =>
                    {
                        try
                        {
                            var offset = scroller.VerticalOffset;
                            if (!double.IsNaN(bottom))
                            {
                                double vh = 0, eh = 0;
                                try { vh = scroller.ViewportHeight; } catch { /* swallow */ }
                                try { eh = fe.ActualHeight; } catch { /* swallow */ }
                                var targetBottom = offset + Math.Max(0, vh) - bottom;
                                var defaultBottom = originY + eh;
                                var desired = targetBottom - defaultBottom;
                                if (desired < 0) desired = 0;
                                trans.Y = desired;
                            }
                            else
                            {
                                var desired = offset - (originY - top);
                                if (desired < 0) desired = 0;
                                trans.Y = desired;
                            }
                        }
                        catch { /* swallow */ }
                    };
                }
                catch { /* swallow */ }
            };
        }

        private static ScrollViewer FindAncestorScrollViewer(FrameworkElement fe)
        {
            try
            {
                DependencyObject cur = fe;
                while (cur != null)
                {
                    var sv = cur as ScrollViewer; if (sv != null) return sv;
                    cur = Windows.UI.Xaml.Media.VisualTreeHelper.GetParent(cur);
                }
            }
            catch { /* swallow */ }
            return null;
        }

        // Position element on a Canvas honoring left/top/right/bottom when possible.
        private static void PositionOnCanvas(FrameworkElement fe, CssComputed css, FrameworkElement relative)
        {
            if (fe == null) return;
            Action place = () =>
            {
                try
                {
                    double left = 0, top = 0;
                    double rw = 0, rh = 0, ew = 0, eh = 0;
                    bool isFixed = css != null && string.Equals(css.Position, "fixed", StringComparison.OrdinalIgnoreCase);
                    try
                    {
                        if (isFixed)
                        {
                            var b = Window.Current.Bounds; rw = b.Width; rh = b.Height;
                        }
                        else if (relative != null)
                        {
                            rw = relative.ActualWidth; rh = relative.ActualHeight;
                        }
                    }
                    catch { /* swallow */ }
                    try { ew = fe.ActualWidth; eh = fe.ActualHeight; } catch { /* swallow */ }

                    if (css != null)
                    {
                        // Helper to read px or % from raw map
                        Func<string, double?, double?, double> pxOrPercent = (name, container, elem) =>
                        {
                            try
                            {
                                string raw;
                                if (css.Map != null && css.Map.TryGetValue(name, out raw) && !string.IsNullOrWhiteSpace(raw))
                                {
                                    raw = raw.Trim();
                                    if (raw.EndsWith("%"))
                                    {
                                        double p; if (double.TryParse(raw.TrimEnd('%'), out p) && container.HasValue)
                                            return Math.Max(0, (p / 100.0) * container.Value);
                                    }
                                    double px; if (double.TryParse(raw.Replace("px", "").Trim(), out px)) return px;
                                }
                            }
                            catch { /* swallow */ }
                            return double.NaN;
                        };

                        double l = double.NaN, r = double.NaN, t = double.NaN, b = double.NaN;
                        if (css.Left.HasValue) l = css.Left.Value; else l = pxOrPercent("left", rw, ew);
                        if (css.Right.HasValue) r = css.Right.Value; else r = pxOrPercent("right", rw, ew);
                        if (css.Top.HasValue) t = css.Top.Value; else t = pxOrPercent("top", rh, eh);
                        if (css.Bottom.HasValue) b = css.Bottom.Value; else b = pxOrPercent("bottom", rh, eh);

                        if (!double.IsNaN(l)) left = l;
                        else if (!double.IsNaN(r) && rw > 0 && ew > 0) left = Math.Max(0, rw - ew - r);

                        if (!double.IsNaN(t)) top = t;
                        else if (!double.IsNaN(b) && rh > 0 && eh > 0) top = Math.Max(0, rh - eh - b);
                    }

                    Canvas.SetLeft(fe, left);
                    Canvas.SetTop(fe, top);
                    if (css != null && css.ZIndex.HasValue) Canvas.SetZIndex(fe, css.ZIndex.Value);
                }
                catch { /* swallow */ }
            };

            fe.Loaded += (s, e) => { try { place(); } catch { /* swallow */ } };
            fe.SizeChanged += (s, e) => { try { place(); } catch { /* swallow */ } };
            if (relative != null) relative.SizeChanged += (s, e) => { try { place(); } catch { /* swallow */ } };
            try
            {
                if (css != null && string.Equals(css.Position, "fixed", StringComparison.OrdinalIgnoreCase))
                    Window.Current.SizeChanged += (s, e) => { try { place(); } catch { /* swallow */ } };
            }
            catch { /* swallow */ }
            place();
        }

        // ---------- Helpers: URLs, text ----------
        private static Uri ResolveUri(Uri baseUri, string href)
        {
            if (string.IsNullOrWhiteSpace(href)) return null;
            href = href.Trim();

            // protocol-relative //host/path
            if (href.StartsWith("//"))
            {
                try { return new Uri((baseUri?.Scheme ?? "https") + ":" + href); } catch { return null; }
            }

            Uri abs;
            if (Uri.TryCreate(href, UriKind.Absolute, out abs)) return abs;
            if (baseUri != null && Uri.TryCreate(baseUri, href, out abs)) return abs;
            return null;
        }

        private static string GatherText(LiteElement n)
        {
            if (n == null) return "";
            if (n.IsText)
            {
                var raw = n.Text ?? string.Empty;
                if (n == null)
                    return string.Empty;
                var normalized = NormalizeTextNodeContent(raw);
                if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;
                if (ShouldSuppressTextNode(normalized)) return string.Empty;

                bool leadingWs = raw.Length > 0 && char.IsWhiteSpace(raw[0]);
                bool trailingWs = raw.Length > 0 && char.IsWhiteSpace(raw[raw.Length - 1]);

                if (leadingWs) normalized = " " + normalized;
                if (trailingWs) normalized += " ";
                return normalized;
            }

            var sb = new System.Text.StringBuilder();
            if (n != null && n.Children != null)
            {
                foreach (var ch in n.Children)
                {
                    if (ch == null) continue;
                    if (!ch.IsText && HiddenTags.Contains(ch.Tag)) continue;
                    var part = GatherText(ch);
                    if (!string.IsNullOrEmpty(part)) sb.Append(part);
                }
            }
            return sb.ToString();
        }

        // faster than Regex.Replace("\\s+"," ")
        private static string CollapseWs(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = WebUtility.HtmlDecode(s);

            bool inWs = false;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (char.IsWhiteSpace(c))
                {
                    if (!inWs) { sb.Append(' '); inWs = true; }
                }
                else { sb.Append(c); inWs = false; }
            }
            var res = sb.ToString();
            int start = 0, end = res.Length - 1;
            if (start <= end && res[start] == ' ') start++;
            if (end >= start && res[end] == ' ') end--;
            return (start == 0 && end == res.Length - 1) ? res : res.Substring(start, end - start + 1);
        }

        private static string DictGet(IDictionary<string, string> d, string key)
        {
            if (d == null) return null;
            string v; return d.TryGetValue(key, out v) ? v : null;
        }

        private static bool TryGetAttr(LiteElement node, string name, out string value)
        {
            value = null;
            if (node == null || node.Attr == null) return false;
            foreach (var kv in node.Attr)
            {
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = kv.Value;
                    return true;
                }
            }
            return false;
        }

        private sealed class SvgRenderState
        {
            public Brush Fill;
            public Brush Stroke;
            public double StrokeWidth;
            public double Opacity = 1.0;

            public SvgRenderState Clone()
            {
                return new SvgRenderState
                {
                    Fill = Fill,
                    Stroke = Stroke,
                    StrokeWidth = StrokeWidth,
                    Opacity = Opacity
                };
            }
        }

        private FrameworkElement RenderInlineSvg(LiteElement svg)
        {
            if (svg == null) return null;

            var state = new SvgRenderState
            {
                Fill = new SolidColorBrush(Windows.UI.Colors.Black),
                StrokeWidth = 1.0
            };

            ApplySvgStateOverrides(svg, state);

            double width = ParseSvgLength(DictGet(svg.Attr, "width"));
            double height = ParseSvgLength(DictGet(svg.Attr, "height"));

            double vbX = 0, vbY = 0, vbW = 0, vbH = 0;
            bool hasViewBox = false;
            string viewBox;
            if (TryGetAttr(svg, "viewBox", out viewBox) || TryGetAttr(svg, "viewbox", out viewBox))
            {
                var parts = (viewBox ?? string.Empty).Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4)
                {
                    if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out vbX) &&
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out vbY) &&
                        double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out vbW) &&
                        double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out vbH))
                    {
                        hasViewBox = vbW > 0 && vbH > 0;
                    }
                }
            }

            var canvas = new Canvas { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            if (hasViewBox)
            {
                canvas.Width = vbW;
                canvas.Height = vbH;
                if (Math.Abs(vbX) > double.Epsilon || Math.Abs(vbY) > double.Epsilon)
                {
                    canvas.RenderTransform = new TranslateTransform { X = -vbX, Y = -vbY };
                }
            }
            else
            {
                if (!double.IsNaN(width) && width > 0) canvas.Width = SanitizeSize(width, "svg.canvas.width");
                if (!double.IsNaN(height) && height > 0) canvas.Height = SanitizeSize(height, "svg.canvas.height");
            }

            if (svg.Children != null)
            foreach (var child in svg.Children)
            {
                AppendSvgElement(canvas, child, state);
            }

            FrameworkElement result = canvas;
            if (hasViewBox || !double.IsNaN(width) || !double.IsNaN(height))
            {
                var vb = new Viewbox { Stretch = Stretch.Uniform, Child = canvas };
                if (!double.IsNaN(width) && width > 0) vb.Width = width;
                if (!double.IsNaN(height) && height > 0) vb.Height = height;
                result = vb;
            }

            return result;
        }

        private void AppendSvgElement(Canvas canvas, LiteElement node, SvgRenderState inherited)
        {
            if (canvas == null || node == null) return;
            if (node.IsText) return;

            var state = inherited?.Clone() ?? new SvgRenderState();
            ApplySvgStateOverrides(node, state);

            if (string.Equals(node.Tag, "g", StringComparison.OrdinalIgnoreCase))
            {
                if (node.Children != null)
                foreach (var child in node.Children)
                {
                    AppendSvgElement(canvas, child, state);
                }
                return;
            }

            if (string.Equals(node.Tag, "path", StringComparison.OrdinalIgnoreCase))
            {
                string data;
                if (!TryGetAttr(node, "d", out data)) return;
                try
                {
                    string fillRuleAttr;
                    TryGetAttr(node, "fill-rule", out fillRuleAttr);
                    var geom = CreateSvgPathGeometry(data, fillRuleAttr);
                    if (geom == null) return;

                    var path = new Path
                    {
                        Data = geom,
                        Opacity = Clamp(state.Opacity, 0, 1),
                        Stretch = Stretch.None
                    };
                    path.Fill = state.Fill;
                    path.Stroke = state.Stroke;
                    if (path.Stroke != null)
                    {
                        path.StrokeThickness = state.StrokeWidth > 0 ? state.StrokeWidth : 1;
                    }
                    canvas.Children.Add(path);
                }
                catch { /* swallow */ }
                return;
            }

            if (node.Children != null)
            foreach (var child in node.Children)
            {
                AppendSvgElement(canvas, child, state);
            }
        }

        private static void ApplySvgStateOverrides(LiteElement node, SvgRenderState state)
        {
            if (node == null || state == null) return;

            string fill;
            if (TryGetAttr(node, "fill", out fill))
            {
                var brush = CreateSvgBrush(fill, allowNone: true);
                if (brush != null || string.Equals((fill ?? string.Empty).Trim(), "none", StringComparison.OrdinalIgnoreCase))
                    state.Fill = brush;
            }

            string stroke;
            if (TryGetAttr(node, "stroke", out stroke))
            {
                var brush = CreateSvgBrush(stroke, allowNone: true);
                if (brush != null || string.Equals((stroke ?? string.Empty).Trim(), "none", StringComparison.OrdinalIgnoreCase))
                    state.Stroke = brush;
            }

            string strokeWidth;
            if (TryGetAttr(node, "stroke-width", out strokeWidth))
            {
                var w = ParseSvgLength(strokeWidth);
                if (!double.IsNaN(w) && w >= 0) state.StrokeWidth = w;
            }

            string opacity;
            if (TryGetAttr(node, "opacity", out opacity))
            {
                double o;
                if (double.TryParse(opacity, NumberStyles.Float, CultureInfo.InvariantCulture, out o)) state.Opacity = Clamp(o, 0, 1);
            }

            string fillOpacity;
            if (TryGetAttr(node, "fill-opacity", out fillOpacity))
            {
                double fo;
                if (double.TryParse(fillOpacity, NumberStyles.Float, CultureInfo.InvariantCulture, out fo))
                {
                    var brush = state.Fill as SolidColorBrush;
                    if (brush != null)
                    {
                        var col = brush.Color; col.A = (byte)(Clamp(fo, 0, 1) * 255);
                        state.Fill = new SolidColorBrush(col);
                    }
                }
            }

            string strokeOpacity;
            if (TryGetAttr(node, "stroke-opacity", out strokeOpacity))
            {
                double so;
                if (double.TryParse(strokeOpacity, NumberStyles.Float, CultureInfo.InvariantCulture, out so))
                {
                    var brush = state.Stroke as SolidColorBrush;
                    if (brush != null)
                    {
                        var col = brush.Color; col.A = (byte)(Clamp(so, 0, 1) * 255);
                        state.Stroke = new SolidColorBrush(col);
                    }
                }
            }
        }

        private static double ParseSvgLength(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return double.NaN;
            var s = raw.Trim();
            if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 2);
            double val;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out val) ? val : double.NaN;
        }

        private static Brush CreateSvgBrush(string value, bool allowNone)
        {
            if (string.IsNullOrWhiteSpace(value)) return allowNone ? null : new SolidColorBrush(Windows.UI.Colors.Black);
            var s = value.Trim();
            if (allowNone && string.Equals(s, "none", StringComparison.OrdinalIgnoreCase)) return null;
            var parsed = CssParser.ParseColor(s);
            if (parsed.HasValue) return new SolidColorBrush(parsed.Value);
            return allowNone ? null : new SolidColorBrush(Windows.UI.Colors.Black);
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static Geometry CreateSvgPathGeometry(string data, string fillRule)
        {
            if (string.IsNullOrWhiteSpace(data)) return null;
            try
            {
                var escaped = data.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("'", "&apos;").Replace("<", "&lt;").Replace(">", "&gt;");
                var xaml = "<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data=\"" + escaped + "\" />";
                var obj = Windows.UI.Xaml.Markup.XamlReader.Load(xaml) as Path;
                if (obj == null) return null;
                var geom = obj.Data;
                var pg = geom as PathGeometry;
                if (pg != null)
                {
                    pg.FillRule = !string.IsNullOrWhiteSpace(fillRule) && string.Equals(fillRule.Trim(), "evenodd", StringComparison.OrdinalIgnoreCase)
                        ? FillRule.EvenOdd : FillRule.Nonzero;
                }
                return geom;
            }
            catch { return null; }
        }

        // Compose inline content as RichTextBlock Inlines for better fidelity (links, emphasis, breaks)
        private async void AppendInline(Paragraph para, LiteElement node, Uri baseUri, Action<Uri> onNavigate, string wsMode = null)
        {
            if (para == null || node == null) return;

            if (node.IsText)
            {
                var raw = node.Text ?? string.Empty;
                var normalized = NormalizeTextNodeContent(raw);
                if (string.IsNullOrWhiteSpace(normalized)) return;
                if (ShouldSuppressTextNode(normalized)) return;

                bool leadingWs = raw.Length > 0 && char.IsWhiteSpace(raw[0]);
                bool trailingWs = raw.Length > 0 && char.IsWhiteSpace(raw[raw.Length - 1]);
                var display = normalized;
                if (leadingWs) display = " " + display;
                if (trailingWs) display += " ";

                var t = TransformWhitespace(display, wsMode);
                if (t.Length > 0) para.Inlines.Add(new Run { Text = t });
                return;
            }

            // Inline-recursive builder for known inline tags
            switch (node.Tag)
            {
                case "br":
                    para.Inlines.Add(new LineBreak());
                    return;
                // (Cases for em, i, strong, b, u, small, span, sup, sub moved to unified block below)

                case "a":
                    {
                        string href = DictGet(node.Attr, "href");
                        var abs = ResolveUri(baseUri, href);
                        CssComputed css = null; try { css = TryGetCss(node); } catch { /* swallow */ }
                        string td = null; try { if (css != null && css.Map != null) css.Map.TryGetValue("text-decoration", out td); } catch { /* swallow */ }

                        // If text-decoration: none, emulate a non-underlined link using InlineUIContainer + TextBlock
                        if (!string.IsNullOrWhiteSpace(td) && td.IndexOf("none", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap };
                            try { RendererStyles.ApplyTextStyle(tb, css); } catch { /* swallow */ }
                            var linkText = TransformWhitespace(GatherText(node) ?? "", wsMode);
                            if (string.IsNullOrWhiteSpace(linkText) && abs != null) linkText = abs.AbsoluteUri;
                            tb.Text = linkText ?? string.Empty;
                            // hover effect: adjust color slightly
                            var orig = tb.Foreground as SolidColorBrush;
                            var hover = TryBuildHoverBrush(orig);
                            if (hover != null)
                            {
                                tb.PointerEntered += (s, e) => { try { tb.Foreground = hover; } catch { /* swallow */ } };
                                tb.PointerExited += (s, e) => { try { tb.Foreground = orig; } catch { /* swallow */ } };
                            }
                            if (abs != null && onNavigate != null) tb.Tapped += (s, e) =>
                            {
                                bool cancelDom = false;
                                try
                                {
                                    if (Js != null)
                                    {
                                        string id = null; try { if (node.Attr != null) node.Attr.TryGetValue("id", out id); } catch { /* swallow */ }
                                        if (!string.IsNullOrWhiteSpace(id)) cancelDom = Js.RaiseElementEventSync(id, "click");
                                    }
                                }
                                catch { /* swallow */ }
                                if (cancelDom) { TrySetHandled(e); return; }
                                onNavigate(abs);
                            };
                            // onclick support
                            string onclick = DictGet(node.Attr, "onclick");
                            if (!string.IsNullOrWhiteSpace(onclick) && Js != null)
                            {
                                tb.Tapped += (s, e) =>
                                {
                                    bool cancel = false;
                                    try
                                    {
                                        string id = null; try { if (node.Attr != null) node.Attr.TryGetValue("id", out id); } catch { /* swallow */ }
                                        var code = PreprocessInlineHandler(onclick, id);
                                        cancel = Js.RunInline(code, new JsContext { BaseUri = baseUri }, "click", id);
                                    }
                                    catch { /* swallow */ }
                                    if (cancel) TrySetHandled(e);
                                };
                            }
                            para.Inlines.Add(new InlineUIContainer { Child = tb });
                            TryAppendSpacingAfter(para, css);
                            return;
                        }

                        // Default: true Hyperlink
                        var link = new Hyperlink();
                        try { ApplyInlineTextStyle(link, css); } catch { /* swallow */ }
                        // onclick inline handler support (may cancel default)
                        string onclickH = DictGet(node.Attr, "onclick");
                        link.Click += (s, e) =>
                        {
                            bool cancel = false;
                            try
                            {
                                if (!string.IsNullOrWhiteSpace(onclickH) && Js != null)
                                {
                                    string id = null; try { if (node.Attr != null) node.Attr.TryGetValue("id", out id); } catch { /* swallow */ }
                                    var code = PreprocessInlineHandler(onclickH, id);
                                    cancel = Js.RunInline(code, new JsContext { BaseUri = baseUri }, "click", id);
                                }
                            }
                            catch { /* swallow */ }
                            bool cancelDom = false; try { if (Js != null) { string id2 = null; try { if (node.Attr != null) node.Attr.TryGetValue("id", out id2); } catch { /* swallow */ } if (!string.IsNullOrWhiteSpace(id2)) cancelDom = Js.RaiseElementEventSync(id2, "click"); } } catch { /* swallow */ }
                            if (cancel || cancelDom) { TrySetHandled(e); return; }
                            if (abs != null && onNavigate != null) onNavigate(abs);
                        };
                        if (node.Children != null)
                        foreach (var ch in node.Children) AppendInline(link.Inlines, ch, baseUri, onNavigate, wsMode);
                        if (link.Inlines.Count == 0 && abs != null) link.Inlines.Add(new Run { Text = abs.AbsoluteUri });
                        para.Inlines.Add(link);
                        TryAppendSpacingAfter(para, css);
                        return;
                    }
                case "button":
                    {
                        var btnText = CollapseWs(GatherText(node)); if (string.IsNullOrWhiteSpace(btnText)) btnText = "Button";
                        var btn = new Button
                        {
                            Content = btnText,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 245, 245)),
                            Foreground = new SolidColorBrush(Windows.UI.Colors.Black),
                            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 210, 210, 210)),
                            Padding = new Thickness(12, 6, 12, 6),
                            MinHeight = 32,
                            Margin = new Thickness(2, 2, 2, 2)
                        };
                        // Try to apply inline styles (font/foreground)
                        try { var css = TryGetCss(node); RendererStyles.ApplyTextStyle(btn, css); } catch { /* swallow */ }
                        // Add click event handling
                        btn.Click += (s, e) =>
                        {
                            bool cancel = false;
                            try
                            {
                                if (Js != null)
                                {
                                    string id = null; try { if (node.Attr != null) node.Attr.TryGetValue("id", out id); } catch { /* swallow */ }
                                    if (!string.IsNullOrWhiteSpace(id))
                                    {
                                        cancel = Js.RaiseElementEventSync(id, "click");
                                    }
                                }
                            }
                            catch { /* swallow */ }
                            if (cancel) { TrySetHandled(e); }
                        };
                        para.Inlines.Add(new InlineUIContainer { Child = btn });
                        return;
                    }
                case "input":
                    {
                        var type = (DictGet(node.Attr, "type") ?? "text").Trim().ToLowerInvariant();
                        if (type == "button" || type == "submit")
                        {
                            var label = DictGet(node.Attr, "value") ?? "Submit";
                            var btn = new Button
                            {
                                Content = label,
                                HorizontalAlignment = HorizontalAlignment.Left,
                                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 245, 245)),
                                Foreground = new SolidColorBrush(Windows.UI.Colors.Black),
                                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 210, 210, 210)),
                                Padding = new Thickness(12, 6, 12, 6),
                                MinHeight = 32,
                                Margin = new Thickness(2, 2, 2, 2)
                            };
                            try { var css = TryGetCss(node); RendererStyles.ApplyTextStyle(btn, css); } catch { /* swallow */ }
                            // Add click event handling
                            btn.Click += (s, e) =>
                            {
                                bool cancel = false;
                                try
                                {
                                    if (Js != null)
                                    {
                                        string id = null; try { if (node.Attr != null) node.Attr.TryGetValue("id", out id); } catch { /* swallow */ }
                                        if (!string.IsNullOrWhiteSpace(id))
                                        {
                                            cancel = Js.RaiseElementEventSync(id, "click");
                                        }
                                    }
                                }
                                catch { /* swallow */ }
                                if (cancel) { TrySetHandled(e); }
                            };
                            para.Inlines.Add(new InlineUIContainer { Child = btn });
                            return;
                        }
                        if (type == "text" || type == "search" || type == "email" || type == "url")
                        {
                            double vw = 0; try { vw = Window.Current.Bounds.Width; } catch { /* swallow */ }
                            if (vw <= 0) vw = 360;
                            var tb = new TextBox
                            {
                                Margin = new Thickness(2, 0, 2, 0),
                                Background = new SolidColorBrush(Windows.UI.Colors.White),
                                Foreground = new SolidColorBrush(Windows.UI.Colors.Black),
                                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 220, 220)),
                                Height = 36,
                                Padding = new Thickness(8, 4, 8, 4)
                            };
                            try
                            {
                                string v = DictGet(node.Attr, "value"); if (!string.IsNullOrEmpty(v)) tb.Text = v;
                                string ph = DictGet(node.Attr, "placeholder"); if (!string.IsNullOrEmpty(ph)) tb.PlaceholderText = ph;
                                if (node.Attr != null && node.Attr.ContainsKey("disabled")) tb.IsEnabled = false;
                                if (node.Attr != null && node.Attr.ContainsKey("readonly")) tb.IsReadOnly = true;
                            }
                            catch { /* swallow */ }
                            try { var css = TryGetCss(node); RendererStyles.ApplyTextStyle(tb, css); } catch { /* swallow */ }
                            // Heuristic sizing for inline search textboxes if width not specified
                            try
                            {
                                if (double.IsNaN(tb.Width) || tb.Width <= 0)
                                {
                                    tb.MinWidth = Math.Min(480, Math.Max(160, vw * 0.5));
                                    tb.MaxWidth = Math.Min(720, Math.Max(tb.MinWidth, vw * 0.9));
                                }
                            }
                            catch { /* swallow */ }
                            string id = null; try { if (node.Attr != null) node.Attr.TryGetValue("id", out id); } catch { /* swallow */ }
                            if (!string.IsNullOrWhiteSpace(id) && Js != null)
                            {
                                tb.TextChanged += (s, e) => { try { Js.RaiseElementEvent(id, "input", tb.Text ?? ""); } catch { /* swallow */ } };
                                tb.LostFocus += (s, e) => { try { Js.RaiseElementEvent(id, "change", tb.Text ?? ""); } catch { /* swallow */ } };
                            }
                            para.Inlines.Add(new InlineUIContainer { Child = tb });
                            return;
                        }
                        if (type == "password")
                        {
                            var pb = new PasswordBox { MinWidth = 80, Margin = new Thickness(2, 0, 2, 0) };
                            try
                            {
                                if (node.Attr != null && node.Attr.ContainsKey("disabled")) pb.IsEnabled = false;
                            }
                            catch { /* swallow */ }
                            try { var css = TryGetCss(node); RendererStyles.ApplyTextStyle(pb, css); } catch { /* swallow */ }
                            string id = null; try { if (node.Attr != null) node.Attr.TryGetValue("id", out id); } catch { /* swallow */ }
                            if (!string.IsNullOrWhiteSpace(id) && Js != null)
                            {
                                pb.PasswordChanged += (s, e) => { try { Js.RaiseElementEvent(id, "input", ""); } catch { /* swallow */ } };
                                pb.LostFocus += (s, e) => { try { Js.RaiseElementEvent(id, "change", ""); } catch { /* swallow */ } };
                            }
                            para.Inlines.Add(new InlineUIContainer { Child = pb });
                            return;
                        }
                        if (type == "checkbox")
                        {
                            var cb = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
                            try { var css = TryGetCss(node); RendererStyles.ApplyTextStyle(cb, css); } catch { /* swallow */ }
                            // state
                            try { if (node.Attr != null && node.Attr.ContainsKey("checked")) cb.IsChecked = true; } catch { /* swallow */ }
                            try { if (node.Attr != null && node.Attr.ContainsKey("disabled")) cb.IsEnabled = false; } catch { /* swallow */ }
                            // JS bridge if element has id
                            string id = null; try { if (node.Attr != null) node.Attr.TryGetValue("id", out id); } catch { /* swallow */ }
                            if (!string.IsNullOrWhiteSpace(id) && Js != null)
                            {
                                cb.Click += (s, e) => { try { Js.RaiseElementEvent(id, "change", null, cb.IsChecked == true); } catch { try { Js.RaiseElementEventById(id, "change"); } catch { /* swallow */ } } };
                            }
                            para.Inlines.Add(new InlineUIContainer { Child = cb });
                            return;
                        }
                        if (type == "radio")
                        {
                            var rb = new RadioButton { VerticalAlignment = VerticalAlignment.Center };
                            try { var css = TryGetCss(node); RendererStyles.ApplyTextStyle(rb, css); } catch { /* swallow */ }
                            string name = DictGet(node.Attr, "name"); if (!string.IsNullOrWhiteSpace(name)) rb.GroupName = name;
                            try { if (node.Attr != null && node.Attr.ContainsKey("checked")) rb.IsChecked = true; } catch { /* swallow */ }
                            try { if (node.Attr != null && node.Attr.ContainsKey("disabled")) rb.IsEnabled = false; } catch { /* swallow */ }
                            string id = null; try { if (node.Attr != null) node.Attr.TryGetValue("id", out id); } catch { /* swallow */ }
                            string value = DictGet(node.Attr, "value");
                            if (!string.IsNullOrWhiteSpace(id) && Js != null)
                            {
                                rb.Click += (s, e) =>
                                {
                                    try { Js.RaiseElementEvent(id, "change", value ?? "", rb.IsChecked == true); }
                                    catch { try { Js.RaiseElementEventById(id, "change"); } catch { /* swallow */ } }
                                };
                            }
                            para.Inlines.Add(new InlineUIContainer { Child = rb });
                            return;
                        }
                        // fallback: plain text
                        var t = DictGet(node.Attr, "value") ?? "";
                        if (!string.IsNullOrEmpty(t)) para.Inlines.Add(new Run { Text = t });
                        return;
                    }
                case "select":
                    {
                        var combo = new ComboBox { MinWidth = 100, Margin = new Thickness(2, 0, 2, 0) };
                        try
                        {
                            foreach (var opt in node.Children.Where(c => c.Tag == "option"))
                            {
                                var it = new OptionItem
                                {
                                    Text = CollapseWs(opt.Text ?? DictGet(opt.Attr, "label") ?? DictGet(opt.Attr, "value")),
                                    Value = DictGet(opt.Attr, "value") ?? CollapseWs(opt.Text)
                                };
                                combo.Items.Add(it);
                                // selected attribute
                                try { if (opt.Attr != null && opt.Attr.ContainsKey("selected")) combo.SelectedItem = it; } catch { /* swallow */ }
                            }
                        }
                        catch { /* swallow */ }
                        try { var css = TryGetCss(node); RendererStyles.ApplyTextStyle(combo, css); } catch { /* swallow */ }
                        string id = null; try { if (node.Attr != null) node.Attr.TryGetValue("id", out id); } catch { /* swallow */ }
                        if (!string.IsNullOrWhiteSpace(id) && Js != null)
                        {
                            combo.SelectionChanged += (s, e) =>
                            {
                                try
                                {
                                    var it = combo.SelectedItem as OptionItem;
                                    string txt = it != null ? (it.Value ?? it.Text ?? "") : (combo.SelectedItem?.ToString() ?? "");
                                    Js.RaiseElementEvent(id, "change", txt);
                                }
                                catch { try { Js.RaiseElementEventById(id, "change"); } catch { /* swallow */ } }
                            };
                        }
                        para.Inlines.Add(new InlineUIContainer { Child = combo });
                        return;
                    }
                case "img":
                    {
                        try
                        {
                            var fe = await MakeImageAsync(node, baseUri);
                            if (fe != null)
                            {
                                var imgCss = TryGetCss(node);
                                var ui = new InlineUIContainer { Child = fe };
                                if (imgCss != null && !string.IsNullOrEmpty(imgCss.VerticalAlign))
                                {
                                    var va = imgCss.VerticalAlign.ToLowerInvariant();
                                    double offset = 0;
                                    if (va == "top" || va == "text-top") offset = -4;
                                    else if (va == "middle" || va == "center") offset = -2;
                                    else if (va == "bottom" || va == "text-bottom") offset = 2;
                                    else if (va == "sub") offset = 4;
                                    else if (va == "super") offset = -6;
                                    if (offset != 0)
                                    {
                                        fe.Margin = new Thickness(0, offset, 0, 0);
                                    }
                                }
                                para.Inlines.Add(ui);
                                TryAppendSpacingAfter(para, TryGetCss(node));
                                return;
                            }
                        }
                        catch { /* swallow */ }

                        var alt = DictGet(node.Attr, "alt");
                        var placeholderText = "[img]";
                        if (!string.IsNullOrWhiteSpace(alt) && !alt.Equals("Image", StringComparison.OrdinalIgnoreCase))
                        {
                            placeholderText = alt.Length > 10 ? alt.Substring(0, 10) + "…" : alt;
                        }
                        var placeholder = new Border
                        {
                            Width = 40,
                            Height = 30,
                            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 40, 40, 40)),
                            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 80, 80, 80)),
                            BorderThickness = new Thickness(1),
                            Child = new TextBlock
                            {
                                Text = placeholderText,
                                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150)),
                                FontSize = 10,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            }
                        };
                        para.Inlines.Add(new InlineUIContainer { Child = placeholder });
                        return;
                    }
                case "span":
                case "strong":
                case "b":
                case "em":
                case "i":
                case "u":
                case "small":
                case "sub":
                case "sup":
                    {
                        var css = TryGetCss(node);
                        bool hasBox = css != null && (css.Background != null ||
                            (css.Padding.Left != 0 || css.Padding.Top != 0 || css.Padding.Right != 0 || css.Padding.Bottom != 0) ||
                            (css.BorderThickness.Left != 0 || css.BorderThickness.Top != 0 || css.BorderThickness.Right != 0 || css.BorderThickness.Bottom != 0));

                        if (hasBox)
                        {
                            try
                            {
                                var rtb = new RichTextBlock { IsTextSelectionEnabled = false, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0) };
                                try { RendererStyles.ApplyTextStyle(rtb, css); } catch { /* swallow */ }
                                var p = new Paragraph();
                                rtb.Blocks.Add(p);
                                if (node.Children != null)
                                {
                                    foreach (var child in node.Children)
                                    {
                                        if (child != null) AppendInline(p, child, baseUri, onNavigate, wsMode);
                                    }
                                }

                                var bd = new Border { Child = rtb };
                                if (css.Background != null) bd.Background = css.Background;
                                bd.Padding = css.Padding;
                                bd.BorderThickness = css.BorderThickness;
                                if (css.BorderBrush != null) bd.BorderBrush = css.BorderBrush;
                                bd.CornerRadius = css.BorderRadius;
                                bd.Margin = css.Margin;

                                if (!string.IsNullOrEmpty(css.VerticalAlign))
                                {
                                    var va = css.VerticalAlign.ToLowerInvariant();
                                    double offset = 0;
                                    if (va == "top" || va == "text-top") offset = -4;
                                    else if (va == "middle" || va == "center") offset = -2;
                                    else if (va == "bottom" || va == "text-bottom") offset = 2;
                                    else if (va == "sub") offset = 4;
                                    else if (va == "super") offset = -6;
                                    if (offset != 0)
                                    {
                                        bd.Margin = new Thickness(bd.Margin.Left, bd.Margin.Top + offset, bd.Margin.Right, bd.Margin.Bottom);
                                    }
                                }
                                para.Inlines.Add(new InlineUIContainer { Child = bd });
                            }
                            catch { /* swallow */ }
                            return;
                        }

                        Span sp = new Span();
                        if (node.Tag == "strong" || node.Tag == "b") sp.FontWeight = FontWeights.Bold;
                        else if (node.Tag == "em" || node.Tag == "i") sp.FontStyle = Windows.UI.Text.FontStyle.Italic;
                        else if (node.Tag == "u") { var u = new Underline(); sp = u; }

                        if (css != null) ApplyInlineTextStyle(sp, css);

                        // Default sizing for semantic tags if not overridden by CSS
                        try 
                        {
                            if (node.Tag == "small" && (css == null || !css.FontSize.HasValue)) sp.FontSize = 12; // 0.75em approx
                            else if ((node.Tag == "sub" || node.Tag == "sup") && (css == null || !css.FontSize.HasValue)) sp.FontSize = 10; 
                        } 
                        catch { /* swallow */ }

                        foreach (var child in node.Children) AppendInline(sp.Inlines, child, baseUri, onNavigate, wsMode);
                        para.Inlines.Add(sp);
                        return;
                    }
                case "code":
                    {
                        var sp = new Span();
                        try { sp.FontFamily = new FontFamily("Consolas, Courier New, monospace"); } catch { /* swallow */ }
                        var codeText = TransformWhitespace(GatherText(node) ?? "", wsMode == "pre" ? "pre" : "pre-wrap");
                        if (!string.IsNullOrEmpty(codeText)) sp.Inlines.Add(new Run { Text = codeText });
                        para.Inlines.Add(sp);
                        return;
                    }
                case "mark":
                    {
                        // Highlight background via Border + TextBlock inside InlineUIContainer
                        var text = TransformWhitespace(GatherText(node) ?? "", wsMode);
                        var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
                        var bd = new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(128, 255, 255, 0)), Padding = new Thickness(1,0,1,0), Child = tb };
                        para.Inlines.Add(new InlineUIContainer { Child = bd });
                        return;
                    }
                case "abbr":
                    {
                        var title = DictGet(node.Attr, "title");
                        var text = TransformWhitespace(GatherText(node) ?? "", wsMode);
                        var sb = new System.Text.StringBuilder(); sb.Append(text);
                        if (!string.IsNullOrWhiteSpace(title)) sb.Append(" (" + title + ")");
                        para.Inlines.Add(new Run { Text = sb.ToString() });
                        return;
                    }
                case "kbd":
                    {
                        var text = TransformWhitespace(GatherText(node) ?? "", wsMode);
                        var tb = new TextBlock { Text = text, FontFamily = new FontFamily("Consolas, Courier New, monospace"), TextWrapping = TextWrapping.Wrap };
                        var bd = new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(32, 0, 0, 0)), BorderBrush = new SolidColorBrush(Windows.UI.Colors.Gray), BorderThickness = new Thickness(1, 1, 1, 1), Padding = new Thickness(2,0,2,0), Child = tb };
                        para.Inlines.Add(new InlineUIContainer { Child = bd });
                        return;
                    }
                case "s":
                case "del":
                    {
                        var text = TransformWhitespace(GatherText(node) ?? "", wsMode);
                        var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
                        try
                        {
                            var prop = tb.GetType().GetRuntimeProperty("TextDecorations");
                            if (prop != null && prop.CanWrite)
                            {
                                var t = prop.PropertyType; // avoid compile-time reference to Windows.UI.Text.TextDecorations
                                var val = Enum.Parse(t, "Strikethrough", true);
                                prop.SetValue(tb, val);
                            }
                        }
                        catch { /* swallow */ }
                        para.Inlines.Add(new InlineUIContainer { Child = tb });
                        return;
                    }
                case "ins":
                    {
                        var u = new Underline();
                        if (node.Children != null)
                        {
                            foreach (var ch in node.Children) AppendInline(u.Inlines, ch, baseUri, onNavigate, wsMode);
                        }
                        para.Inlines.Add(u);
                        return;
                    }
                case "q":
                    {
                        para.Inlines.Add(new Run { Text = "\u201C" });
                        if (node.Children != null)
                        {
                            foreach (var ch in node.Children) AppendInline(para, ch, baseUri, onNavigate, wsMode);
                        }
                        para.Inlines.Add(new Run { Text = "\u201D" });
                        return;
                    }
                case "cite":
                    {
                        var it = new Italic();
                        if (node.Children != null)
                        {
                            foreach (var ch in node.Children) AppendInline(it.Inlines, ch, baseUri, onNavigate, wsMode);
                        }
                        para.Inlines.Add(it);
                        return;
                    }
            }

            // Fallback for unrecognized inline nodes: append text content
            var fallbackText = TransformWhitespace(GatherText(node) ?? "", wsMode);
            if (!string.IsNullOrWhiteSpace(fallbackText)) para.Inlines.Add(new Run { Text = fallbackText });
        }

        // Overloads to allow building into nested Inline containers (Bold/Italic/Hyperlink/Span)
        private void AppendInline(InlineCollection col, LiteElement node, Uri baseUri, Action<Uri> onNavigate, string wsMode = null)
        {
            var tempPara = new Paragraph();
            AppendInline(tempPara, node, baseUri, onNavigate, wsMode);
            // Move produced inlines into target collection by reparenting (remove before add)
            while (tempPara.Inlines.Count > 0)
            {
                var inline = tempPara.Inlines[0];
                tempPara.Inlines.RemoveAt(0);
                try
                {
                    col.Add(inline);
                }
                catch (Exception)
                {
                    // Some WP8.1 inline types (e.g., certain nested structures) may not be addable here.
                    // Fall back to a plain-text run extracted from the inline tree so we never crash.
                    try
                    {
                        var text = TryInlineToText(inline);
                        if (!string.IsNullOrEmpty(text)) col.Add(new Run { Text = text });
                    }
                    catch { /* swallow */ }
                }
            }
        }

        private static string TryInlineToText(Inline inline)
        {
            try
            {
                if (inline == null) return string.Empty;
                var run = inline as Run; if (run != null) return run.Text ?? string.Empty;
                var span = inline as Span;
                if (span != null)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var i in span.Inlines)
                    {
                        var t = TryInlineToText(i); if (!string.IsNullOrEmpty(t)) sb.Append(t);
                    }
                    return sb.ToString();
                }
                var link = inline as Hyperlink;
                if (link != null)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var i in link.Inlines)
                    {
                        var t = TryInlineToText(i); if (!string.IsNullOrEmpty(t)) sb.Append(t);
                    }
                    return sb.ToString();
                }
                var ui = inline as InlineUIContainer;
                if (ui != null)
                {
                    // Best-effort textual placeholder
                    return string.Empty;
                }
                return inline.ToString();
            }
            catch { return string.Empty; }
        }

        private static string NormalizeWhitespaceMode(string ws)
        {
            if (string.IsNullOrWhiteSpace(ws)) return "normal";
            ws = ws.Trim().ToLowerInvariant();
            if (ws == "nowrap" || ws == "pre" || ws == "pre-wrap") return ws;
            return "normal";
        }

        private static string TransformWhitespace(string text, string wsMode)
        {
            text = text ?? string.Empty;
            wsMode = NormalizeWhitespaceMode(wsMode);
            if (wsMode == "pre")
            {
                return ToPre(text);
            }
            if (wsMode == "pre-wrap")
            {
                return ToPreWrap(text);
            }
            // normal/nowrap: collapse
            return CollapseWs(text);
        }

        private static string ToPre(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            s = s.Replace("\t", "    ");
            return s.Replace(' ', '\u00A0');
        }

        private static string ToPreWrap(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            s = s.Replace("\t", "    ");
            int cap = (s.Length > 500000000) ? s.Length : s.Length * 2;
            var sb = new System.Text.StringBuilder(cap);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ')
                {
                    int j = i;
                    while (j < s.Length && s[j] == ' ') j++;
                    int count = j - i;
                    for (int k = 0; k < count; k++) sb.Append((k % 2 == 0) ? ' ' : '\u00A0');
                    i = j;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static SolidColorBrush TryBuildHoverBrush(SolidColorBrush baseBrush)
        {
            try
            {
                var c = (baseBrush != null) ? baseBrush.Color : Windows.UI.Colors.DodgerBlue;
                byte a = c.A;
                // lighten by 10%
                Func<byte, byte> L = v => (byte)Math.Min(255, (int)(v + (255 - v) * 0.10));
                var hc = Windows.UI.Color.FromArgb(a, L(c.R), L(c.G), L(c.B));
                return new SolidColorBrush(hc);
            }
            catch { return baseBrush; }
        }

        private static void TryAppendSpacingAfter(Paragraph para, CssComputed css)
        {
            try
            {
                if (para == null) return;
                bool needsSpace = false;
                if (css != null && css.Map != null)
                {
                    string mr = null; css.Map.TryGetValue("margin-right", out mr);
                    double v; if (TryPx(mr, out v) && v > 0) needsSpace = true;
                    if (!needsSpace)
                    {
                        string m = null; if (css.Map.TryGetValue("margin", out m) && !string.IsNullOrWhiteSpace(m))
                        {
                            var parts = m.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length == 2) { if (TryPx(parts[1], out v) && v > 0) needsSpace = true; }
                            else if (parts.Length >= 4) { if (TryPx(parts[1], out v) && v > 0) needsSpace = true; }
                        }
                    }
                    if (!needsSpace)
                    {
                        var disp = css.Display ?? (css.Map.ContainsKey("display") ? css.Map["display"] : null);
                        if (!string.IsNullOrWhiteSpace(disp) && disp.Trim().Equals("inline-block", StringComparison.OrdinalIgnoreCase)) needsSpace = true;
                    }
                }
                if (needsSpace)
                {
                    // add a single collapsing space
                    para.Inlines.Add(new Run { Text = " " });
                }
            }
            catch { /* swallow */ }
        }

        private static void ApplyInlineTextStyle(TextElement te, CssComputed css)
        {
            if (te == null || css == null) return;
            try
            {
                if (css.Foreground != null) te.Foreground = css.Foreground;
            }
            catch { /* swallow */ }
            try
            {
                if (css.FontFamily != null) te.FontFamily = css.FontFamily;
            }
            catch { /* swallow */ }
            try
            {
                if (css.FontSize.HasValue && css.FontSize.Value > 0) te.FontSize = css.FontSize.Value;
            }
            catch { /* swallow */ }
            try
            {
                if (css.FontStyle.HasValue) te.FontStyle = css.FontStyle.Value;
            }
            catch { /* swallow */ }
            try
            {
                if (css.FontWeight.HasValue) te.FontWeight = css.FontWeight.Value;
            }
            catch { /* swallow */ }
        }

        // ---------- CSS Grid ----------

        private async Task<FrameworkElement> RenderCssGridAsync(LiteElement n, Uri baseUri, Action<Uri> onNavigate, JavaScriptEngine js, CancellationToken ct)
        {
            var css = TryGetCss(n);
            var grid = new Grid();

            try { ApplyComputedStyles(grid, n); } catch { }
            try { ApplyInlineStyles(grid, n); } catch { }

            // Parse column template
            var colTemplate = css?.GridTemplateColumns;
            if (!string.IsNullOrWhiteSpace(colTemplate))
            {
                var colDefs = ParseGridTrackList(colTemplate);
                foreach (var cd in colDefs)
                    grid.ColumnDefinitions.Add(cd);
            }

            // Parse row template
            var rowTemplate = css?.GridTemplateRows;
            if (!string.IsNullOrWhiteSpace(rowTemplate))
            {
                var rowDefs = ParseGridRowTrackList(rowTemplate);
                foreach (var rd in rowDefs)
                    grid.RowDefinitions.Add(rd);
            }

            // Gap
            double colGap = 0, rowGap = 0;
            if (css != null)
            {
                if (css.ColumnGap.HasValue) colGap = css.ColumnGap.Value;
                if (css.RowGap.HasValue) rowGap = css.RowGap.Value;
                if (css.Gap.HasValue)
                {
                    if (colGap <= 0) colGap = css.Gap.Value;
                    if (rowGap <= 0) rowGap = css.Gap.Value;
                }
            }
            if (colGap > 0) grid.ColumnSpacing = colGap;
            if (rowGap > 0) grid.RowSpacing = rowGap;

            grid.HorizontalAlignment = HorizontalAlignment.Stretch;

            // Parse grid-template-areas
            var namedAreas = ParseGridTemplateAreas(css?.GridTemplateAreas);
            bool hasNamedAreas = namedAreas != null && namedAreas.Count > 0;

            // If only areas are defined (no explicit template), infer grid size
            if (hasNamedAreas && grid.ColumnDefinitions.Count == 0)
            {
                int maxCol = 0, maxRow = 0;
                foreach (var area in namedAreas.Values)
                {
                    int endCol = area.col + area.colSpan;
                    int endRow = area.row + area.rowSpan;
                    if (endCol > maxCol) maxCol = endCol;
                    if (endRow > maxRow) maxRow = endRow;
                }
                for (int c = 0; c < maxCol; c++)
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                for (int r = 0; r < maxRow; r++)
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            // Place children
            var children = n.Children ?? new List<LiteElement>();
            int nextCol = 0, nextRow = 0;
            int maxCols = grid.ColumnDefinitions.Count > 0 ? grid.ColumnDefinitions.Count : 1;
            string autoFlow = css?.GridAutoFlow;

            foreach (var child in children)
            {
                ct.ThrowIfCancellationRequested();
                var childCss = TryGetCss(child);

                // Skip absolute/fixed — handled by parent
                if (childCss != null && (childCss.Position == "absolute" || childCss.Position == "fixed")) continue;

                var fe = await RenderNodeAsync(child, baseUri, onNavigate, js, ct);
                if (fe == null) continue;

                int col = -1, row = -1, colSpan = 1, rowSpan = 1;

                // Check named grid-area first (grid-template-areas)
                string areaName = childCss?.GridArea;
                if (hasNamedAreas && !string.IsNullOrWhiteSpace(areaName) && areaName.IndexOf('/') < 0)
                {
                    GridAreaInfo area;
                    if (namedAreas.TryGetValue(areaName.Trim().ToLowerInvariant(), out area))
                    {
                        col = area.col;
                        row = area.row;
                        colSpan = area.colSpan;
                        rowSpan = area.rowSpan;
                    }
                }
                else if (childCss != null) ParseGridPlacement(childCss, out col, out row, out colSpan, out rowSpan);

                if (col >= 0 || row >= 0)
                {
                    if (col >= 0) Grid.SetColumn(fe, col);
                    if (row >= 0) Grid.SetRow(fe, row);
                }
                else
                {
                    // Auto-placement
                    if (autoFlow != null && autoFlow.Contains("column"))
                    {
                        Grid.SetColumn(fe, nextCol);
                        Grid.SetRow(fe, nextRow);
                        nextRow++;
                        if (maxCols > 0 && nextRow >= grid.RowDefinitions.Count)
                        { nextRow = 0; nextCol++; }
                    }
                    else
                    {
                        Grid.SetColumn(fe, nextCol);
                        Grid.SetRow(fe, nextRow);
                        nextCol++;
                        if (nextCol >= maxCols) { nextCol = 0; nextRow++; }
                    }
                }

                if (colSpan > 1) Grid.SetColumnSpan(fe, colSpan);
                if (rowSpan > 1) Grid.SetRowSpan(fe, rowSpan);

                grid.Children.Add(fe);
            }

            // Handle absolute-positioned children via Canvas overlay
            var absoluteItems = CollectAbsoluteChildren(n);
            if (absoluteItems.Count > 0)
            {
                var overlay = new Canvas();
                overlay.Children.Add(grid);
                foreach (var abs in absoluteItems)
                {
                    var absFe = await RenderNodeAsync(abs.Item1, baseUri, onNavigate, js, ct);
                    if (absFe != null)
                    {
                        overlay.Children.Add(absFe);
                        try { PositionOnCanvas(absFe, abs.Item2, grid); } catch { }
                    }
                }
                return overlay;
            }

            return grid;
        }

        private static List<ColumnDefinition> ParseGridTrackList(string template)
        {
            var defs = new List<ColumnDefinition>();
            if (string.IsNullOrWhiteSpace(template)) return defs;

            // Handle repeat()
            var repeatMatch = Regex.Match(template, @"repeat\(\s*(\d+)\s*,\s*(.+?)\s*\)", RegexOptions.IgnoreCase);
            if (repeatMatch.Success)
            {
                int count;
                if (int.TryParse(repeatMatch.Groups[1].Value, out count) && count > 0)
                {
                    var inner = repeatMatch.Groups[2].Value.Trim();
                    var innerTracks = ParseTrackValues(inner);
                    for (int i = 0; i < count; i++)
                        defs.AddRange(innerTracks);
                }
                // Also parse any tracks before/after repeat
                var before = template.Substring(0, repeatMatch.Index).Trim();
                var after = template.Substring(repeatMatch.Index + repeatMatch.Length).Trim();
                if (!string.IsNullOrEmpty(before))
                    defs.InsertRange(0, ParseTrackValues(before));
                if (!string.IsNullOrEmpty(after))
                    defs.AddRange(ParseTrackValues(after));
                return defs;
            }

            return ParseTrackValues(template);
        }

        private static List<ColumnDefinition> ParseTrackValues(string value)
        {
            var defs = new List<ColumnDefinition>();
            if (string.IsNullOrWhiteSpace(value)) return defs;

            var parts = SplitGridTrackValues(value);
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Length == 0) continue;

                // minmax(min, max)
                var mm = Regex.Match(trimmed, @"minmax\(\s*(.+?)\s*,\s*(.+?)\s*\)", RegexOptions.IgnoreCase);
                if (mm.Success)
                {
                    var minVal = mm.Groups[1].Value.Trim();
                    var maxVal = mm.Groups[2].Value.Trim();
                    var gLen = ParseGridLength(maxVal);
                    if (gLen.GridUnitType != GridUnitType.Auto)
                    {
                        double minPx;
                        if (TryPx(minVal, out minPx))
                            defs.Add(new ColumnDefinition { Width = gLen, MinWidth = minPx });
                        else
                            defs.Add(new ColumnDefinition { Width = gLen });
                    }
                    else
                        defs.Add(new ColumnDefinition());
                    continue;
                }

                // fr unit
                var fr = Regex.Match(trimmed, @"^([0-9]*\.?[0-9]+)fr$", RegexOptions.IgnoreCase);
                if (fr.Success)
                {
                    double val;
                    double.TryParse(fr.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out val);
                    if (val <= 0) val = 1;
                    defs.Add(new ColumnDefinition { Width = new GridLength(val, GridUnitType.Star) });
                    continue;
                }

                // px
                double px;
                if (TryPx(trimmed, out px))
                {
                    defs.Add(new ColumnDefinition { Width = new GridLength(px, GridUnitType.Pixel) });
                    continue;
                }

                // percentage
                double pct;
                var pctMatch = Regex.Match(trimmed, @"^([0-9]*\.?[0-9]+)%$");
                if (pctMatch.Success && double.TryParse(pctMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out pct))
                {
                    defs.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
                    continue;
                }

                // auto
                if (trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("min-content", StringComparison.OrdinalIgnoreCase))
                {
                    defs.Add(new ColumnDefinition());
                    continue;
                }

                // max-content / fit-content — treat as auto
                if (trimmed.Equals("max-content", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("fit-content", StringComparison.OrdinalIgnoreCase))
                {
                    defs.Add(new ColumnDefinition());
                    continue;
                }

                // default: auto
                defs.Add(new ColumnDefinition());
            }

            return defs;
        }

        private static List<string> SplitGridTrackValues(string value)
        {
            var parts = new List<string>();
            int depth = 0;
            int start = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ' ' && depth == 0)
                {
                    var part = value.Substring(start, i - start).Trim();
                    if (part.Length > 0) parts.Add(part);
                    start = i + 1;
                }
            }
            var last = value.Substring(start).Trim();
            if (last.Length > 0) parts.Add(last);
            return parts;
        }

        private static GridLength ParseGridLength(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new GridLength(1, GridUnitType.Star);

            var trimmed = value.Trim();

            // fr
            var fr = Regex.Match(trimmed, @"^([0-9]*\.?[0-9]+)fr$", RegexOptions.IgnoreCase);
            if (fr.Success)
            {
                double val;
                double.TryParse(fr.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out val);
                if (val <= 0) val = 1;
                return new GridLength(val, GridUnitType.Star);
            }

            // px
            double px;
            if (TryPx(trimmed, out px))
                return new GridLength(px, GridUnitType.Pixel);

            // percentage treated as star
            double pct;
            var pctMatch = Regex.Match(trimmed, @"^([0-9]*\.?[0-9]+)%$");
            if (pctMatch.Success && double.TryParse(pctMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out pct))
                return new GridLength(pct, GridUnitType.Star);

            return new GridLength(1, GridUnitType.Star);
        }

        private static void ParseGridPlacement(CssComputed css, out int col, out int row, out int colSpan, out int rowSpan)
        {
            col = -1; row = -1; colSpan = 1; rowSpan = 1;

            // Parse grid-area shorthand
            if (!string.IsNullOrWhiteSpace(css.GridArea) && css.GridArea.IndexOf('/') < 0)
            {
                // Named grid area — skip explicit placement (would need grid-template-areas parsing)
                return;
            }

            // Parse grid-column
            if (!string.IsNullOrWhiteSpace(css.GridColumn))
            {
                ParseGridLine(css.GridColumn, out col, out colSpan);
            }

            // Parse grid-row
            if (!string.IsNullOrWhiteSpace(css.GridRow))
            {
                ParseGridLine(css.GridRow, out row, out rowSpan);
            }
        }

        private static void ParseGridLine(string value, out int start, out int span)
        {
            start = -1; span = 1;
            if (string.IsNullOrWhiteSpace(value)) return;

            var trimmed = value.Trim().ToLowerInvariant();

            // "span N"
            if (trimmed.StartsWith("span "))
            {
                int s;
                if (int.TryParse(trimmed.Substring(5).Trim(), out s) && s > 0)
                    span = s;
                return;
            }

            // "N / span M" or "N / M"
            var slash = trimmed.IndexOf('/');
            if (slash >= 0)
            {
                var left = trimmed.Substring(0, slash).Trim();
                var right = trimmed.Substring(slash + 1).Trim();

                // left side
                int c;
                if (int.TryParse(left, out c) && c > 0)
                    start = c - 1; // CSS grid lines are 1-based

                // right side
                if (right.StartsWith("span "))
                {
                    int s;
                    if (int.TryParse(right.Substring(5).Trim(), out s) && s > 0)
                        span = s;
                }
                else
                {
                    int end;
                    if (int.TryParse(right, out end) && end > 0)
                        span = end - (start >= 0 ? start : 0);
                }
                return;
            }

            // single number
            int n;
            if (int.TryParse(trimmed, out n) && n > 0)
                start = n - 1;
        }

        private struct GridAreaInfo { public int col, row, colSpan, rowSpan; }

        private static Dictionary<string, GridAreaInfo> ParseGridTemplateAreas(string areas)
        {
            var result = new Dictionary<string, GridAreaInfo>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(areas)) return result;

            // Split into rows (each quoted string is a row)
            var rows = new List<List<string>>();
            int idx = 0;
            while (idx < areas.Length)
            {
                // Find opening quote
                int qStart = areas.IndexOf('"', idx);
                if (qStart < 0) break;
                qStart++;
                int qEnd = areas.IndexOf('"', qStart);
                if (qEnd < 0) break;
                var rowStr = areas.Substring(qStart, qEnd - qStart).Trim();
                idx = qEnd + 1;

                if (string.IsNullOrEmpty(rowStr)) continue;
                var cells = new List<string>();
                int ci = 0;
                while (ci < rowStr.Length)
                {
                    // skip whitespace
                    while (ci < rowStr.Length && char.IsWhiteSpace(rowStr[ci])) ci++;
                    if (ci >= rowStr.Length) break;
                    // read token
                    int tokStart = ci;
                    while (ci < rowStr.Length && !char.IsWhiteSpace(rowStr[ci])) ci++;
                    var token = rowStr.Substring(tokStart, ci - tokStart);
                    if (token != ".") // "." = empty cell
                        cells.Add(token);
                    else
                        cells.Add(null);
                }
                if (cells.Count > 0) rows.Add(cells);
            }

            if (rows.Count == 0) return result;

            // Build contiguous area map: find bounding box for each named area
            // First pass: collect all cells by name
            var areaCells = new Dictionary<string, List<Tuple<int, int>>>(StringComparer.OrdinalIgnoreCase);
            for (int r = 0; r < rows.Count; r++)
            {
                for (int c = 0; c < rows[r].Count; c++)
                {
                    var name = rows[r][c];
                    if (name == null) continue;
                    List<Tuple<int, int>> cells;
                    if (!areaCells.TryGetValue(name, out cells))
                        areaCells[name] = cells = new List<Tuple<int, int>>();
                    cells.Add(Tuple.Create(c, r));
                }
            }

            foreach (var kv in areaCells)
            {
                if (kv.Value.Count == 0) continue;
                int minC = int.MaxValue, minR = int.MaxValue, maxC = int.MinValue, maxR = int.MinValue;
                foreach (var cell in kv.Value)
                {
                    if (cell.Item1 < minC) minC = cell.Item1;
                    if (cell.Item1 > maxC) maxC = cell.Item1;
                    if (cell.Item2 < minR) minR = cell.Item2;
                    if (cell.Item2 > maxR) maxR = cell.Item2;
                }
                result[kv.Key] = new GridAreaInfo
                {
                    col = minC, row = minR,
                    colSpan = maxC - minC + 1,
                    rowSpan = maxR - minR + 1
                };
            }

            return result;
        }

        private static List<Tuple<LiteElement, CssComputed>> CollectAbsoluteChildren(LiteElement n)
        {
            var list = new List<Tuple<LiteElement, CssComputed>>();
            if (n.Children == null) return list;
            foreach (var child in n.Children)
            {
                var css = TryGetCssStatic(child);
                if (css != null && (css.Position == "absolute" || css.Position == "fixed"))
                    list.Add(Tuple.Create(child, css));
            }
            return list;
        }

        private static List<RowDefinition> ParseGridRowTrackList(string template)
        {
            var defs = new List<RowDefinition>();
            if (string.IsNullOrWhiteSpace(template)) return defs;

            var colDefs = ParseGridTrackList(template);
            foreach (var cd in colDefs)
            {
                var rd = new RowDefinition { Height = cd.Width };
                if (cd.MinWidth > 0) rd.MinHeight = cd.MinWidth;
                defs.Add(rd);
            }
            return defs;
        }

        // ---------- Flexbox (legacy subset) ----------


        private async Task<FrameworkElement> MakeGridFallbackAsync(LiteElement n, Uri baseUri, Action<Uri> onNavigate, JavaScriptEngine js, CancellationToken ct)
        {
            var css = TryGetCss(n);

            double columnGap = 0, rowGap = 0;
            if (css != null)
            {
                if (css.ColumnGap.HasValue) columnGap = css.ColumnGap.Value;
                if (css.RowGap.HasValue) rowGap = css.RowGap.Value;
                if (css.Gap.HasValue)
                {
                    if (columnGap <= 0) columnGap = css.Gap.Value;
                    if (rowGap <= 0) rowGap = css.Gap.Value;
                }
            }
            if (columnGap <= 0 && rowGap > 0) columnGap = rowGap;
            if (rowGap <= 0 && columnGap > 0) rowGap = columnGap;

            var wrapPanel = new FlexPanel
            {
                Orientation = Orientation.Horizontal,
                ColumnGap = Math.Max(0, columnGap),
                RowGap = Math.Max(0, rowGap)
            };

            wrapPanel.HorizontalAlignment = HorizontalAlignment.Stretch;

            try
            {
                string justify;
                if (css != null && css.Map != null && css.Map.TryGetValue("justify-content", out justify) && !string.IsNullOrWhiteSpace(justify))
                    wrapPanel.JustifyContent = justify.Trim().ToLowerInvariant();

                string alignContent;
                if (css != null && css.Map != null && css.Map.TryGetValue("align-content", out alignContent) && !string.IsNullOrWhiteSpace(alignContent))
                {
                    wrapPanel.AlignContent = alignContent.Trim().ToLowerInvariant();
                    var ac = wrapPanel.AlignContent;
                    if (ac.Contains("center")) wrapPanel.VerticalAlignment = VerticalAlignment.Center;
                    else if (ac.Contains("flex-end") || ac.Contains("end")) wrapPanel.VerticalAlignment = VerticalAlignment.Bottom;
                    else if (ac.Contains("flex-start") || ac.Contains("start")) wrapPanel.VerticalAlignment = VerticalAlignment.Top;
                    else wrapPanel.VerticalAlignment = VerticalAlignment.Stretch;
                }
            }
            catch { /* swallow */ }

            var absoluteItems = new List<Tuple<FrameworkElement, CssComputed>>();
            var children = n.Children ?? new List<LiteElement>();

            double minColumnWidth = ExtractMinColumnWidth(css);
            if (double.IsNaN(minColumnWidth) || minColumnWidth < 0) minColumnWidth = 0;

            foreach (var child in children)
            {
                ct.ThrowIfCancellationRequested();
                var cssChild = TryGetCss(child);
                bool isAbs = cssChild != null && string.Equals(cssChild.Position, "absolute", StringComparison.OrdinalIgnoreCase);
                bool isFixed = cssChild != null && string.Equals(cssChild.Position, "fixed", StringComparison.OrdinalIgnoreCase);
                bool isSticky = cssChild != null && string.Equals(cssChild.Position, "sticky", StringComparison.OrdinalIgnoreCase);

                if (isAbs)
                {
                    var absElt = await RenderNodeAsync(child, baseUri, onNavigate, js, ct);
                    if (absElt != null) absoluteItems.Add(Tuple.Create(absElt, cssChild));
                    continue;
                }
                if (isFixed)
                {
                    var fixedElt = await RenderNodeAsync(child, baseUri, onNavigate, js, ct);
                    if (fixedElt != null) _pendingFixed.Add(System.Tuple.Create(fixedElt, cssChild));
                    continue;
                }
                if (isSticky)
                {
                    var stickyElt = await RenderNodeAsync(child, baseUri, onNavigate, js, ct);
                    if (stickyElt != null)
                    {
                        try { AttachStickyBehavior(stickyElt, cssChild); } catch { /* swallow */ }
                    }
                    continue;
                }

                var element = await RenderNodeAsync(child, baseUri, onNavigate, js, ct);
                if (element == null) continue;

                ApplyGridItemSizing(element, cssChild, minColumnWidth, wrapPanel.ColumnGap);

                wrapPanel.Children.Add(element);
            }

            FrameworkElement container = wrapPanel;
            if (absoluteItems.Count > 0)
            {
                var grid = new Grid();
                grid.Children.Add(wrapPanel);
                foreach (var tuple in absoluteItems)
                {
                    var fe = tuple.Item1;
                    grid.Children.Add(fe);
                    try { PositionOnCanvas(fe, tuple.Item2, wrapPanel); } catch { /* swallow */ }
                }
                container = grid;
            }

            return container;
        }

        private static void ApplyGridItemSizing(FrameworkElement element, CssComputed css, double minColumnWidth, double columnGap)
        {
            if (element == null) return;

            if (minColumnWidth > 0 && element.MinWidth < minColumnWidth)
            {
                element.MinWidth = minColumnWidth;
            }

            if (css == null || css.Map == null) return;

            try
            {
                string gridColumn;
                if (css.Map.TryGetValue("grid-column", out gridColumn) && !string.IsNullOrWhiteSpace(gridColumn) && minColumnWidth > 0)
                {
                    var match = Regex.Match(gridColumn, @"span\s+(\d+)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        int span;
                        if (int.TryParse(match.Groups[1].Value, out span) && span > 1)
                        {
                            double width = span * minColumnWidth;
                            if (columnGap > 0) width += (span - 1) * columnGap;
                            if (width > element.MinWidth) element.MinWidth = width;
                        }
                    }
                }
            }
            catch { /* swallow */ }
        }

        private static double ExtractMinColumnWidth(CssComputed css)
        {
            if (css == null || css.Map == null) return double.NaN;

            try
            {
                string template;
                if (css.Map.TryGetValue("grid-template-columns", out template))
                {
                    double fromTemplate = ParsePreferredTrackSize(template);
                    if (!double.IsNaN(fromTemplate)) return fromTemplate;
                }

                string auto;
                if (css.Map.TryGetValue("grid-auto-columns", out auto))
                {
                    double px;
                    if (TryPx(auto, out px)) return px;
                }
            }
            catch { /* swallow */ }

            return double.NaN;
        }

        private static double ParsePreferredTrackSize(string template)
        {
            if (string.IsNullOrWhiteSpace(template)) return double.NaN;

            try
            {
                var minmax = Regex.Match(template, @"minmax\(\s*([^,]+),", RegexOptions.IgnoreCase);
                if (minmax.Success)
                {
                    double px;
                    if (TryPx(minmax.Groups[1].Value.Trim(), out px)) return px;
                }

                var repeat = Regex.Match(template, @"repeat\(\s*[^,]+,\s*([^\)]+)\)", RegexOptions.IgnoreCase);
                if (repeat.Success)
                {
                    var inner = repeat.Groups[1].Value;
                    double px;
                    if (TryPx(inner.Trim(), out px)) return px;
                    var innerMin = Regex.Match(inner, @"minmax\(\s*([^,]+),", RegexOptions.IgnoreCase);
                    if (innerMin.Success && TryPx(innerMin.Groups[1].Value.Trim(), out px)) return px;
                }

                var simple = Regex.Match(template, @"(?<num>[-+]?[0-9]*\.?[0-9]+(?:px|rem|em)?)", RegexOptions.IgnoreCase);
                if (simple.Success)
                {
                    double px;
                    if (TryPx(simple.Groups["num"].Value, out px)) return px;
                }
            }
            catch { /* swallow */ }

            return double.NaN;
        }

        // ---------- Image helpers (no WebView) ----------
        private static readonly string[] _supportedImgMime =
        {
            "image/png","image/jpeg","image/jpg","image/gif","image/svg+xml"
        };

        private static bool IsSupportedImageType(string typeOrUrl)
        {
            if (string.IsNullOrWhiteSpace(typeOrUrl)) return false;

            // MIME?
            if (typeOrUrl.IndexOf("/", StringComparison.Ordinal) > 0)
                return _supportedImgMime.Any(m => typeOrUrl.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);

            // extension?
            var u = typeOrUrl.ToLowerInvariant();
            return u.EndsWith(".png") || u.EndsWith(".jpg") || u.EndsWith(".jpeg") ||
                   u.EndsWith(".gif") || u.EndsWith(".svg");
        }

        private static string RewriteImageUrlIfNeeded(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;

            // twitter/x pattern: ...?format=webp -> ?format=jpg
            if (url.IndexOf("format=webp", StringComparison.OrdinalIgnoreCase) >= 0)
                url = Regex.Replace(url, @"format=webp", "format=jpg", RegexOptions.IgnoreCase);

            // also skip "f=webp" style flags
            url = Regex.Replace(url, @"(\?|&)(f|fmt)=webp", "$1$2=jpg", RegexOptions.IgnoreCase);

            // common extension swap: .webp/.avif -> .png (safer alpha support)
            if (Regex.IsMatch(url, @"\.(webp|avif)(\?.*)?$", RegexOptions.IgnoreCase))
                url = Regex.Replace(url, @"\.(webp|avif)(\?.*)?$", ".png$2", RegexOptions.IgnoreCase);

            return url;
        }

        // support both width (NNw) and density (Nx) descriptors
        private static string PickSrcFromSrcset(string srcset, double deviceWidth, double deviceScale = 1.0)
        {
            if (string.IsNullOrWhiteSpace(srcset)) return null;

            var widthCands = new List<Tuple<string, int>>();
            var dppxCands = new List<Tuple<string, double>>();

            foreach (var part in srcset.Split(','))
            {
                var p = part.Trim();
                if (p.Length == 0) continue;
                var sp = p.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (sp.Length == 0) continue;

                var url = sp[0];
                if (sp.Length >= 2)
                {
                    var d = sp[1].Trim().ToLowerInvariant();
                    int w;
                    double x;
                    if (d.EndsWith("w") && int.TryParse(d.TrimEnd('w'), out w))
                        widthCands.Add(Tuple.Create(url, w));
                    else if (d.EndsWith("x") && double.TryParse(d.TrimEnd('x'), out x))
                        dppxCands.Add(Tuple.Create(url, x));
                    else
                        widthCands.Add(Tuple.Create(url, 0));
                }
                else
                {
                    widthCands.Add(Tuple.Create(url, 0));
                }
            }

            if (dppxCands.Count > 0)
            {
                var bests = dppxCands
                    .OrderBy(c => Math.Abs(c.Item2 - Math.Max(1.0, deviceScale)))
                    .First().Item1;
                return bests;
            }

            if (widthCands.Count == 0) return null;

            int dw = (int)Math.Max(320, deviceWidth <= 0 ? 360 : deviceWidth);
            var sorted = widthCands.OrderBy(c => c.Item2 == 0 ? int.MaxValue : c.Item2).ToList();
            // Prefer the largest candidate up to ~1.5x device width to balance quality
            var limit = (int)Math.Round(dw * Math.Max(1.0, deviceScale) * 1.5);
            Tuple<string,int> best = null;
            foreach (var c in sorted)
            {
                if (c.Item2 == 0) { best = c; continue; }
                if (c.Item2 <= limit) best = c; else break;
            }
            var chosen = (best ?? sorted.Last()).Item1;
            return chosen;
        }
        private Task<FrameworkElement> MakeImageAsync(LiteElement n, Uri baseUri)
            => MakeImageAsync(n, baseUri, System.Threading.CancellationToken.None);

        private async Task<FrameworkElement> MakePictureAsync(LiteElement n, Uri baseUri, CancellationToken ct)
        {
            if (n == null || n.Children == null) return null;
            var img = n.Children.FirstOrDefault(c => string.Equals(c.Tag, "img", StringComparison.OrdinalIgnoreCase));
            if (img != null)
            {
                return await MakeImageAsync(img, baseUri, ct);
            }
            return null;
        }

        private async Task<FrameworkElement> MakeImageAsync(LiteElement n, Uri baseUri, CancellationToken ct)
        {
            System.Diagnostics.Debug.WriteLine($"[MakeImage] START tag={n?.Tag}");
            ct.ThrowIfCancellationRequested();
            if (n == null) return null;

            var candidates = ResolveImageUriCandidates(n, baseUri);
            System.Diagnostics.Debug.WriteLine($"[MakeImage] candidates={candidates?.Count ?? 0}");
            if (candidates == null || candidates.Count == 0)
            {
                var altOnly = DictGet(n.Attr, "alt");
                if (!string.IsNullOrWhiteSpace(altOnly))
                {
                    var tb = RenderPlainTextBlock(altOnly);
                    if (tb != null) return tb;
                }
                return null;
            }

            var img = new Image { Stretch = Stretch.Uniform };
            
            // Apply object-fit
            var imgCss = TryGetCss(n);
            if (imgCss != null && !string.IsNullOrEmpty(imgCss.ObjectFit))
            {
                var of = imgCss.ObjectFit.ToLowerInvariant();
                if (of == "fill") img.Stretch = Stretch.Fill;
                else if (of == "contain") img.Stretch = Stretch.Uniform;
                else if (of == "cover") img.Stretch = Stretch.UniformToFill;
                else if (of == "none") img.Stretch = Stretch.None;
                else if (of == "scale-down") img.Stretch = Stretch.Uniform;
            }
            
            var alt = DictGet(n.Attr, "alt");
            int reqW = GetIntAttr(n, "width", 0);
            int current = 0;
            bool switching = false;

            Action<string, Uri> log = (msg, u) => {
                System.Diagnostics.Debug.WriteLine($"{msg} {u}");
                DevToolsLogger.Log($"{msg} {u}");
            };

            Func<int, Task<bool>> applyCandidateAsync = null;
            applyCandidateAsync = async (index) =>
            {
                if (index >= candidates.Count) return false;
                current = index;
                var uri = candidates[index];
                if (uri == null) return await applyCandidateAsync(index + 1);

                if (LooksSvg(uri) && SvgType == null)
                {
                    log("[ImgSkipSvg]", uri);
                    return await applyCandidateAsync(index + 1);
                }

                try
                {
                    log("[ImgTry]", uri);
                    var source = await LoadImageSourceAsync(uri, reqW);
                    if (source == null)
                    {
                        log("[ImgTryNull]", uri);
                        return await applyCandidateAsync(index + 1);
                    }

                    try { img.Source = source; } catch { /* swallow */ }
                    return true;
                }
                catch (Exception ex)
                {
                    log("[ImgTryError] " + ex.Message, uri);
                    return await applyCandidateAsync(index + 1);
                }
            };

            img.ImageOpened += (s, e) =>
            {
                Uri opened = (current >= 0 && current < candidates.Count) ? candidates[current] : null;
                log("[ImgOpened]", opened);
            };

            img.ImageFailed += async (s, e) =>
            {
                Uri failed = (current >= 0 && current < candidates.Count) ? candidates[current] : null;
                log("[ImgFailed]", failed);
                if (switching) return;
                switching = true;
                try
                {
                    if (!await applyCandidateAsync(current + 1))
                    {
                        log("[ImgFailedAll]", failed);
                        if (!string.IsNullOrWhiteSpace(alt))
                        {
                            try { img.Source = null; } catch { /* swallow */ }
                            img.Visibility = Visibility.Collapsed;
                        }
                    }
                }
                finally
                {
                    switching = false;
                }
            };

            if (!await applyCandidateAsync(0))
            {
                alt = DictGet(n.Attr, "alt");
                var placeholderText = "[img]";
                if (!string.IsNullOrWhiteSpace(alt) && !alt.Equals("Image", StringComparison.OrdinalIgnoreCase))
                {
                    placeholderText = alt.Length > 20 ? alt.Substring(0, 20) + "..." : alt;
                }
                var placeholder = new Border
                {
                    Width = 80,
                    Height = 60,
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 40, 40, 40)),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 80, 80, 80)),
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = placeholderText,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150)),
                        FontSize = 12,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    }
                };
                return placeholder;
            }

            if (n.Attr != null)
            {
                string w;
                if (n.Attr.TryGetValue("width", out w))
                {
                    double wd;
                    if (double.TryParse(w, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out wd))
                        img.Width = SanitizeSize(wd, "img.width@attr");
                }
                string h;
                if (n.Attr.TryGetValue("height", out h))
                {
                    double hd;
                    if (double.TryParse(h, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out hd))
                        img.Height = SanitizeSize(hd, "img.height@attr");
                }
            }

            ApplyComputedStyles(img, n);
            ApplyInlineStyles(img, n);
            return img;
        }

        private async Task<FrameworkElement> MakeVideoAsync(LiteElement n, Uri baseUri, CancellationToken ct)
        {
            Uri src = null;
            string poster = null;
            bool controls = true;
            try
            {
                if (n.Attr != null)
                {
                    string s; if (n.Attr.TryGetValue("src", out s)) src = ResolveUri(baseUri, s);
                    string c; controls = !(n.Attr.TryGetValue("controls", out c) && c == null);
                    string p; if (n.Attr.TryGetValue("poster", out p)) poster = p;
                }
            }
            catch { /* swallow */ }

            if (src == null)
            {
                try
                {
                    if (n.Children != null)
                    foreach (var ch in n.Children)
                    {
                        if (string.Equals(ch.Tag, "source", StringComparison.OrdinalIgnoreCase) && ch.Attr != null)
                        {
                            string t = null; ch.Attr.TryGetValue("type", out t);
                            string u = null; ch.Attr.TryGetValue("src", out u);
                            if (string.IsNullOrWhiteSpace(u)) continue;
                            if (string.IsNullOrWhiteSpace(t) || t.IndexOf("mp4", StringComparison.OrdinalIgnoreCase) >= 0)
                            { src = ResolveUri(baseUri, u); break; }
                        }
                    }
                }
                catch { /* swallow */ }
            }

            var host = new BrowserCore.Engine.VideoHost();
            host.SetControls(controls);
            if (src != null) host.SetSource(src);

            var wrapped = RendererStyles.WrapWithBoxes(host, TryGetCss(n));
            return wrapped ?? (FrameworkElement)host;
        }

        private async Task<FrameworkElement> RenderPictureAsync(LiteElement picture, Uri baseUri, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (picture == null) return null;

            // Try <source> elements: prefer supported types; pick from srcset with device width
            foreach (var s in picture.Children.Where(c => c.Tag == "source"))
            {
                if (s.Attr == null) continue;

                string type = null; s.Attr.TryGetValue("type", out type);
                string srcset = null; s.Attr.TryGetValue("srcset", out srcset);

                if (!string.IsNullOrWhiteSpace(type) && !IsSupportedImageType(type))
                    continue;

                var candidate = PickSrcFromSrcset(srcset, Window.Current?.Bounds.Width ?? 360.0);
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    candidate = RewriteImageUrlIfNeeded(candidate);
                    var abs = ResolveUri(baseUri, candidate);
                    if (abs != null)
                    {
                        var img = new Image { Stretch = Stretch.Uniform };
                        var src = await LoadImageSourceAsync(abs);
                        if (src != null) { img.Source = src; return ApplyBoxesAndText(img, picture); }
                    }
                }
            }

            // Fallback to inner <img>
            var innerImg = picture.Children.FirstOrDefault(c => c.Tag == "img");
            if (innerImg != null)
                return await MakeImageAsync(innerImg, baseUri, ct);

            // Nothing usable ? keep layout stable
            return ApplyBoxesAndText(new Border { Height = 1, Opacity = 0 }, picture);
        }

        // ---------- Entry point ----------
        // Back-compat: existing signature
        public Task<FrameworkElement> BuildAsync(LiteElement root, Uri baseUri, Action<Uri> onNavigate, JavaScriptEngine js = null)
            => BuildAsync(root, baseUri, onNavigate, js, CancellationToken.None);

        public async Task<FrameworkElement> BuildAsync(LiteElement root, Uri baseUri, Action<Uri> onNavigate, JavaScriptEngine js, CancellationToken ct)
        {
            // UI-thread guard: if we're off the UI thread, marshal and re-enter BuildAsync there.
            try
            {
                var disp = UiThreadHelper.TryGetDispatcher();
                if (disp != null && !UiThreadHelper.HasThreadAccess(disp))
                {
                    var tcs = new System.Threading.Tasks.TaskCompletionSource<FrameworkElement>();
                    await UiThreadHelper.RunAsyncAwaitable(disp, Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                    {
                        try { var fe = await BuildAsync(root, baseUri, onNavigate, js, ct); tcs.TrySetResult(fe); }
                        catch (Exception ex) { tcs.TrySetException(ex); }
                    });
                    return await tcs.Task;
                }
            }
            catch { /* swallow */ }
            ct.ThrowIfCancellationRequested();

            _renderedOneSearchForm = false;
            _baseUriForResources = baseUri;
            // expose computed styles statically for helper lookups (list-style-type bullets)
            try { _computedStylesStatic = this.ComputedStyles; } catch { /* swallow */ }
            if (Js != null) Js._computedStyles = ComputedStyles;

            // Honor <base href>
            var baseTag = root.Descendants().FirstOrDefault(n => n.Tag == "base");
            if (baseTag != null && baseTag.Attr != null)
            {
                string b;
                if (baseTag.Attr.TryGetValue("href", out b))
                {
                    Uri bu;
                    if (Uri.TryCreate(b, UriKind.Absolute, out bu)) baseUri = bu;
                    else if (baseUri != null && Uri.TryCreate(baseUri, b, out bu)) baseUri = bu;
                }
            }

            // <meta http-equiv="refresh">
            var refresh = root.Descendants().FirstOrDefault(n =>
                n.Tag == "meta" && n.Attr != null &&
                n.Attr.ContainsKey("http-equiv") &&
                string.Equals(n.Attr["http-equiv"], "refresh", StringComparison.OrdinalIgnoreCase) &&
                n.Attr.ContainsKey("content"));

            if (refresh != null && onNavigate != null)
            {
                var content = refresh.Attr["content"] ?? "";
                // Common forms: "0; URL=/path" or "0;url('/path')"
                string extracted = null;
                try
                {
                    var m = Regex.Match(content, "url\\s*=\\s*([^;]+)", RegexOptions.IgnoreCase);
                    if (m.Success) extracted = m.Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(extracted))
                    {
                        m = Regex.Match(content, "url\\s*\\(\\s*['\\\"]?([^'\\\")]+)['\\\"]?\\s*\\)", RegexOptions.IgnoreCase);
                        if (m.Success) extracted = m.Groups[1].Value;
                    }
                }
                catch { /* swallow */ }

                var part = (extracted ?? string.Empty).Trim().Trim('\'', '"', ' ', ';');
                if (!string.IsNullOrWhiteSpace(part))
                {
                    var target = ResolveUri(baseUri, part);
                    if (target != null)
                    {
                        try
                        {
                            onNavigate(target);
                        }
                        catch { onNavigate(target); }
                        return new StackPanel { Margin = new Thickness(8, 8, 8, 8) };
                    }
                }
            }

            var body = root.Descendants().FirstOrDefault(n => n.Tag == "body") ?? root;

            var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8, 8, 8, 24) };
            try { System.Diagnostics.Debug.WriteLine("[BuildAsync] start nodes=" + (body.Children!=null? body.Children.Count:0)); } catch { /* swallow */ }
            if (body.Children != null)
            {
                foreach (var child in body.Children)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var elt = await RenderNodeAsync(child, baseUri, onNavigate, js, ct);
                        if (elt != null) panel.Children.Add(elt);
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine("RenderNodeAsync failed: " + ex); }
                }
            }

            // Apply computed + inline styles to body container
            try
            {
                // Phase DIAG (issue B): log what body CSS is computed BEFORE applying,
                // and what margin the StackPanel already has. Helps see if body padding
                // (16px in test.html) is reaching the panel container.
                if (ComputedStyles != null)
                {
                    CssComputed cssBodyDiag = null;
                    ComputedStyles.TryGetValue(body, out cssBodyDiag);
                    if (cssBodyDiag == null)
                    {
                        var bodyKey2 = ComputedStyles.Keys.FirstOrDefault(k => string.Equals(k.Tag, "body", StringComparison.OrdinalIgnoreCase));
                        if (bodyKey2 != null) ComputedStyles.TryGetValue(bodyKey2, out cssBodyDiag);
                    }
                    if (cssBodyDiag != null)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "[DIAG:BODY-CSS] body padding=L" + cssBodyDiag.Padding.Left + " T" + cssBodyDiag.Padding.Top +
                            " R" + cssBodyDiag.Padding.Right + " B" + cssBodyDiag.Padding.Bottom +
                            " margin=L" + cssBodyDiag.Margin.Left + " T" + cssBodyDiag.Margin.Top +
                            " R" + cssBodyDiag.Margin.Right + " B" + cssBodyDiag.Margin.Bottom +
                            " bg=" + (cssBodyDiag.Background != null ? cssBodyDiag.Background.ToString() : "<null>") +
                            " display=" + (GetDisplayValue(cssBodyDiag) ?? "<null>"));
                    }
                }
                System.Diagnostics.Debug.WriteLine("[DIAG:BODY-PANEL] StackPanel margin pre-apply=L" + panel.Margin.Left + " T" + panel.Margin.Top + " R" + panel.Margin.Right + " B" + panel.Margin.Bottom);
                ApplyComputedStyles(panel, body);
                ApplyInlineStyles(panel, body);
                System.Diagnostics.Debug.WriteLine("[DIAG:BODY-PANEL] StackPanel margin post-apply=L" + panel.Margin.Left + " T" + panel.Margin.Top + " R" + panel.Margin.Right + " B" + panel.Margin.Bottom);
            }
            catch { /* swallow */ }

            // Wrap body/html background if present
            FrameworkElement rootVisual = panel;
            try
            {
                Brush bg = null;
                if (ComputedStyles != null)
                {
                    CssComputed cssBody;
                    if (body != null && ComputedStyles.TryGetValue(body, out cssBody) &&
                        cssBody != null && cssBody.Background != null)
                    {
                        bg = cssBody.Background;
                    }
                    else
                    {
                        var bodyKey = ComputedStyles.Keys.FirstOrDefault(k => string.Equals(k.Tag, "body", StringComparison.OrdinalIgnoreCase));
                        if (bodyKey != null && ComputedStyles.TryGetValue(bodyKey, out cssBody) &&
                            cssBody != null && cssBody.Background != null)
                            bg = cssBody.Background;
                        else
                        {
                            var htmlKey = ComputedStyles.Keys.FirstOrDefault(k => string.Equals(k.Tag, "html", StringComparison.OrdinalIgnoreCase));
                            CssComputed cssHtml;
                            if (htmlKey != null && ComputedStyles.TryGetValue(htmlKey, out cssHtml) &&
                                cssHtml != null && cssHtml.Background != null)
                                bg = cssHtml.Background;
                        }
                    }
                }
                if (bg == null)
                {
                    var panelBg = (panel as Panel)?.Background;
                    if (panelBg != null) bg = panelBg;
                }
                if (bg != null)
                {
                    var wrapper = new Border
                    {
                        Background = bg,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        Padding = panel.Margin
                    };
                    panel.Margin = new Thickness(0, 0, 0, 0);
                    wrapper.Child = panel;
                    rootVisual = wrapper;
                }
            }
            catch { /* swallow */ }

            // Synthetic search bar only if there's no visible form and the host is a search engine
            bool hasUsableForm = body.Descendants().Any(d => d.Tag == "form" && !IsHidden(d));
            if (!hasUsableForm && IsSearchHost(baseUri))
            {
                var synthetic = MakeSyntheticSearch(baseUri, onNavigate);
                if (synthetic != null) panel.Children.Add(synthetic);
            }

            // If nothing rendered, show OpenGraph preview (JS-only pages)
            if (panel.Children.Count == 0)
            {
                var og = CreateOpenGraphPreview(root, baseUri, onNavigate);
                if (og != null) panel.Children.Add(og);
            }

            // Wrap with a grid that hosts fixed-position overlays (behind and front)
            try
            {
                var grid = new Grid();
                _fixedBehind = new Canvas { IsHitTestVisible = false, Background = null };
                _fixedFront = new Canvas { IsHitTestVisible = false, Background = null };
                grid.Children.Add(_fixedBehind);
                grid.Children.Add(rootVisual);
                grid.Children.Add(_fixedFront);
                // add pending fixed items to appropriate overlay based on z-index
                foreach (var kv in _pendingFixed)
                {
                    try
                    {
                        var fe = kv.Item1; var css = kv.Item2;
                        var target = (css != null && css.ZIndex.HasValue && css.ZIndex.Value < 0) ? _fixedBehind : _fixedFront;
                        target.Children.Add(fe);
                        PositionOnCanvas(fe, css, grid);
                    }
                    catch { /* swallow */ }
                }
                _pendingFixed.Clear();
                try { System.Diagnostics.Debug.WriteLine("[BuildAsync] done children=" + panel.Children.Count); } catch { /* swallow */ }
                return grid;
            }
            catch { /* swallow */ }

            return rootVisual;
        }

        private static void ApplyFlexChildAlignment(FrameworkElement fe, bool isRow, string align)
        {
            try
            {
                if (isRow)
                {
                    if (align == "center") fe.VerticalAlignment = VerticalAlignment.Center;
                    else if (align == "flex-start" || align == "start") fe.VerticalAlignment = VerticalAlignment.Top;
                    else if (align == "flex-end" || align == "end") fe.VerticalAlignment = VerticalAlignment.Bottom;
                    else fe.VerticalAlignment = VerticalAlignment.Stretch;
                }
                else
                {
                    if (align == "center") fe.HorizontalAlignment = HorizontalAlignment.Center;
                    else if (align == "flex-start" || align == "start") fe.HorizontalAlignment = HorizontalAlignment.Left;
                    else if (align == "flex-end" || align == "end") fe.HorizontalAlignment = HorizontalAlignment.Right;
                    else fe.HorizontalAlignment = HorizontalAlignment.Stretch;
                }
            }
            catch { /* swallow */ }
        }

        private static void ApplyJustify(FrameworkElement cont, bool isRow, string justify)
        {
            try
            {
                if (isRow)
                {
                    if (justify == "center") cont.HorizontalAlignment = HorizontalAlignment.Center;
                    else if (justify == "flex-end" || justify == "end") cont.HorizontalAlignment = HorizontalAlignment.Right;
                    else cont.HorizontalAlignment = HorizontalAlignment.Stretch;
                }
                else
                {
                    if (justify == "center") cont.VerticalAlignment = VerticalAlignment.Center;
                    else if (justify == "flex-end" || justify == "end") cont.VerticalAlignment = VerticalAlignment.Bottom;
                    else cont.VerticalAlignment = VerticalAlignment.Stretch;
                }
            }
            catch { /* swallow */ }
        }

        private static FrameworkElement BuildJustifiedLine(List<FrameworkElement> elems, bool isRow, string mode, string align)
        {
            try
            {
                var grid = new Grid();
                if (elems == null || elems.Count == 0) return grid;
                if (isRow)
                {
                    bool around = string.Equals(mode, "space-around", StringComparison.OrdinalIgnoreCase);
                    if (around) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    for (int i = 0; i < elems.Count; i++)
                    {
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        if (i < elems.Count - 1) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    }
                    if (around) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    int col = around ? 1 : 0;
                    for (int i = 0; i < elems.Count; i++)
                    {
                        var fe = elems[i]; ApplyFlexChildAlignment(fe, true, align);
                        Grid.SetColumn(fe, col); grid.Children.Add(fe);
                        col += 2; // skip spacer
                    }
                }
                else
                {
                    bool around = string.Equals(mode, "space-around", StringComparison.OrdinalIgnoreCase);
                    if (around) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    for (int i = 0; i < elems.Count; i++)
                    {
                        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        if (i < elems.Count - 1) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    }
                    if (around) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    int row = around ? 1 : 0;
                    for (int i = 0; i < elems.Count; i++)
                    {
                        var fe = elems[i]; ApplyFlexChildAlignment(fe, false, align);
                        Grid.SetRow(fe, row); grid.Children.Add(fe);
                        row += 2;
                    }
                }
                return grid;
            }
            catch { return new Grid(); }
        }

        private async Task<FrameworkElement> RenderNodeAsync(LiteElement n, Uri baseUri, Action<Uri> onNavigate, JavaScriptEngine js, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (n == null) return null;
            if (n.IsText)
            {
                var txt = CollapseWs(n.Text);
                if (string.IsNullOrWhiteSpace(txt)) return null;
                if (ShouldSuppressTextNode(txt)) return null;

                var tb = new TextBlock { Text = txt, FontSize = 15, Foreground = new SolidColorBrush(Windows.UI.Colors.Black) };

                // Apply white-space from parent's computed style
                CssComputed parentCss = null;
                if (n.Parent != null && ComputedStyles != null)
                    ComputedStyles.TryGetValue(n.Parent, out parentCss);
                if (parentCss != null && !string.IsNullOrEmpty(parentCss.WhiteSpace))
                {
                    var ws = parentCss.WhiteSpace.Trim().ToLowerInvariant();
                    if (ws == "nowrap")
                    {
                        tb.TextWrapping = TextWrapping.NoWrap;
                        if (parentCss.TextOverflow != null && parentCss.TextOverflow.Trim().ToLowerInvariant() == "ellipsis")
                            tb.TextTrimming = TextTrimming.CharacterEllipsis;
                    }
                    else if (ws == "pre" || ws == "pre-wrap")
                    {
                        tb.TextWrapping = ws == "pre" ? TextWrapping.NoWrap : TextWrapping.Wrap;
                    }
                    else if (ws == "pre-line")
                    {
                        tb.TextWrapping = TextWrapping.Wrap;
                    }
                    else
                    {
                        tb.TextWrapping = TextWrapping.WrapWholeWords;
                    }
                }
                else
                {
                    tb.TextWrapping = TextWrapping.WrapWholeWords;
                }

                return tb;
            }

            return await DispatchTagAsync(n, baseUri, onNavigate, js, ct);
        }

        private async Task<FrameworkElement> RenderBlockAsync(LiteElement n, Uri baseUri, Action<Uri> onNavigate, JavaScriptEngine js, CancellationToken ct)
        {
            return await RenderGenericContainerAsync(n, baseUri, onNavigate, js, ct);
        }

        private async Task<FrameworkElement> RenderGenericContainerAsync(LiteElement n, Uri baseUri, Action<Uri> onNavigate, JavaScriptEngine js, CancellationToken ct)
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical };
            try { ApplyComputedStyles(panel, n); } catch { /* swallow */ }
            try { ApplyInlineStyles(panel, n); } catch { /* swallow */ }

            // Phase DIAG (issue C): log the resulting block element margin for
            // top-level structural elements (h1..h6, .test-card, body, divs in
            // test pages). Helps see why huge vertical gaps appear in test.html.
            try
            {
                bool isInteresting = false;
                if (n.Tag != null)
                {
                    if (n.Tag.Length == 2 && n.Tag[0] == 'h' && n.Tag[1] >= '1' && n.Tag[1] <= '6') isInteresting = true;
                    else if (n.Tag == "div" || n.Tag == "p" || n.Tag == "section" || n.Tag == "article") isInteresting = true;
                }
                if (!isInteresting && n.Attr != null && n.Attr.ContainsKey("class"))
                {
                    var cls = n.Attr["class"] ?? "";
                    if (cls.Contains("test-card") || cls.Contains("section-label")) isInteresting = true;
                }
                if (isInteresting)
                {
                    var cssForEl = TryGetCss(n);
                    string marginL = "<n/a>", marginT = "<n/a>", marginR = "<n/a>", marginB = "<n/a>";
                    if (cssForEl != null)
                    {
                        marginL = cssForEl.Margin.Left.ToString();
                        marginT = cssForEl.Margin.Top.ToString();
                        marginR = cssForEl.Margin.Right.ToString();
                        marginB = cssForEl.Margin.Bottom.ToString();
                    }
                    System.Diagnostics.Debug.WriteLine(
                        "[DIAG:MARGIN] tag=" + n.Tag +
                        " id=" + (n.Attr != null && n.Attr.ContainsKey("id") ? n.Attr["id"] : "") +
                        " class=" + (n.Attr != null && n.Attr.ContainsKey("class") ? n.Attr["class"] : "") +
                        " css.margin=L" + marginL + " T" + marginT + " R" + marginR + " B" + marginB +
                        " → panel.margin=L" + panel.Margin.Left + " T" + panel.Margin.Top + " R" + panel.Margin.Right + " B" + panel.Margin.Bottom +
                        " display=" + (cssForEl != null ? (GetDisplayValue(cssForEl) ?? "<null>") : "<n/a>"));
                }
            }
            catch { /* swallow */ }

            if (n.Children != null)
            {
                bool previousWasFloat = false;
                foreach (var child in n.Children)
                {
                    ct.ThrowIfCancellationRequested();
                    var elt = await RenderNodeAsync(child, baseUri, onNavigate, js, ct);
                    if (elt == null) continue;

                    CssComputed childCss = null;
                    if (ComputedStyles != null) ComputedStyles.TryGetValue(child, out childCss);
                    
                    // Handle clear: add spacing to push below floated elements
                    if (childCss != null && !string.IsNullOrEmpty(childCss.Clear))
                    {
                        var clearVal = childCss.Clear.Trim().ToLowerInvariant();
                        if (clearVal == "left" || clearVal == "right" || clearVal == "both")
                        {
                            if (previousWasFloat)
                            {
                                elt.Margin = new Thickness(elt.Margin.Left, 20, elt.Margin.Right, elt.Margin.Bottom);
                            }
                        }
                    }

                    // Handle float: wrap floated elements in a container with horizontal alignment
                    if (childCss != null && !string.IsNullOrEmpty(childCss.Float))
                    {
                        var floatVal = childCss.Float.Trim().ToLowerInvariant();
                        if (floatVal == "left")
                        {
                            elt = new Border { Child = elt, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 8, 8) };
                            previousWasFloat = true;
                        }
                        else if (floatVal == "right")
                        {
                            elt = new Border { Child = elt, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8, 0, 0, 8) };
                            previousWasFloat = true;
                        }
                        else
                        {
                            previousWasFloat = false;
                        }
                    }
                    else
                    {
                        previousWasFloat = false;
                    }

                    panel.Children.Add(elt);
                }
            }

            // Sticky support
            if (ComputedStyles != null && ComputedStyles.TryGetValue(n, out var css))
            {
                if (string.Equals(css.Position, "sticky", StringComparison.OrdinalIgnoreCase))
                {
                    AttachStickyBehavior(panel, css);
                }
            }

            return ApplyBoxesAndText(panel, n);
        }

        private async Task<FrameworkElement> MakeButtonAsync(LiteElement n, Uri baseUri, Action<Uri> onNavigate, JavaScriptEngine js, CancellationToken ct)
        {
            var btn = new Button();
            
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            if (n.Children != null)
            {
                foreach (var child in n.Children)
                {
                    var elt = await RenderNodeAsync(child, baseUri, onNavigate, js, ct);
                    if (elt != null) panel.Children.Add(elt);
                }
            }
            
            if (panel.Children.Count == 0 && !string.IsNullOrWhiteSpace(n.Text))
            {
                 panel.Children.Add(new TextBlock { Text = n.Text });
            }
            
            btn.Content = panel;

            try { ApplyComputedStyles(btn, n); } catch { /* swallow */ }
            try { ApplyInlineStyles(btn, n); } catch { /* swallow */ }

            if (n.Attr != null && n.Attr.TryGetValue("onclick", out var code) && !string.IsNullOrWhiteSpace(code))
            {
                string id = null;
                n.Attr.TryGetValue("id", out id);
                btn.Click += (s, e) =>
                {
                    try
                    {
                        js.RunInline(code, null, "click", id);
                    }
                    catch { /* swallow */ }
                };
            }

            return btn;
        }

        private async Task<FrameworkElement> MakeLinkAsync(LiteElement n, Uri baseUri, Action<Uri> onNavigate, JavaScriptEngine js, CancellationToken ct)
        {
            var href = DictGet(n.Attr, "href");
            Uri abs = null;
            if (!string.IsNullOrEmpty(href))
            {
                Uri.TryCreate(baseUri, href, out abs);
            }

            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap };
            
            // Default link styling
            tb.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 102, 204));
            tb.TextDecorations = TextDecorations.Underline;
            tb.IsTextSelectionEnabled = false;

            // Render children into the TextBlock via Inlines
            if (n.Children != null && n.Children.Count > 0)
            {
                var para = new Paragraph();
                foreach (var child in n.Children)
                {
                    AppendInline(para, child, baseUri, onNavigate);
                }
                // Copy inlines to TextBlock
                foreach (var inline in para.Inlines)
                {
                    tb.Inlines.Add(inline);
                }
            }
            else if (!string.IsNullOrWhiteSpace(n.Text))
            {
                tb.Text = CollapseWs(n.Text);
            }

            // Apply CSS styles (may override color/underline)
            try { ApplyComputedStyles(tb, n); } catch { }
            try { ApplyInlineStyles(tb, n); } catch { }

            // Click handler
            if (abs != null)
            {
                var linkContainer = new Border { Child = tb, Padding = new Thickness(0) };
                linkContainer.Tapped += (s, e) =>
                {
                    try { onNavigate?.Invoke(abs); }
                    catch { System.Diagnostics.Debug.WriteLine(" [Engine/DomBasicRenderer.cs] link navigate failed"); }
                };
                // Pointer cursor
                linkContainer.PointerEntered += (s, e) =>
                {
                    try { Windows.UI.Core.CoreWindow.GetForCurrentThread().PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.Hand, 1); }
                    catch { }
                };
                linkContainer.PointerExited += (s, e) =>
                {
                    try { Windows.UI.Core.CoreWindow.GetForCurrentThread().PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.Arrow, 1); }
                    catch { }
                };
                return Finish(linkContainer, n);
            }

            return Finish(tb, n);
        }

        private async Task<FrameworkElement> RenderTableAsync(LiteElement n, Uri baseUri, Action<Uri> onNavigate, JavaScriptEngine js, CancellationToken ct)
        {
             return await MakeTableAsync(n, baseUri, onNavigate, js, ct);
        }

        private FrameworkElement MakeCodeInline(LiteElement n)
        {
            var tb = new TextBlock
            {
                TextWrapping = TextWrapping.WrapWholeWords,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                Text = CollapseWs(GatherText(n))
            };
            var chip = new Border
            {
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 1, 4, 1),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(30, 255, 255, 255)),
                Child = tb
            };
            return chip;
        }

        private FrameworkElement MakePre(LiteElement n)
        {
            var raw = n.IsText ? (n.Text ?? "") : GatherText(n);
            raw = WebUtility.HtmlDecode(raw);

            var tb = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                Text = raw,
                TextWrapping = TextWrapping.NoWrap,
                IsTextSelectionEnabled = true
            };
            var scroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = tb
            };
            var box = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(24, 255, 255, 255)),
                Child = scroller,
                Padding = new Thickness(8)
            };
            return box;
        }

        private async Task<FrameworkElement> MakeListAsync(LiteElement n, bool ordered, Uri baseUri, Action<Uri> onNavigate, JavaScriptEngine js, CancellationToken ct)
        {
            bool listStyleNone = false;
            bool inlineItems = false;
            try
            {
                var cssUl = TryGetCss(n);
                if (cssUl != null && cssUl.Map != null)
                {
                    string ls; if (cssUl.Map.TryGetValue("list-style", out ls) && !string.IsNullOrWhiteSpace(ls) && ls.ToLowerInvariant().Contains("none")) listStyleNone = true;
                    string lst; if (cssUl.Map.TryGetValue("list-style-type", out lst) && !string.IsNullOrWhiteSpace(lst) && lst.ToLowerInvariant().Contains("none")) listStyleNone = true;
                    string disp; if (cssUl.Map.TryGetValue("display", out disp) && !string.IsNullOrWhiteSpace(disp) && disp.ToLowerInvariant().Contains("flex")) inlineItems = true;
                }
            }
            catch { /* swallow */ }

            var liNodes = (n.Children?.Where(c => c.Tag == "li") ?? Enumerable.Empty<LiteElement>()).ToList();
            foreach (var li in liNodes)
            {
                try
                {
                    var cssLi = TryGetCss(li);
                    if (cssLi != null && cssLi.Map != null)
                    {
                        string disp; if (cssLi.Map.TryGetValue("display", out disp) && !string.IsNullOrWhiteSpace(disp))
                        {
                            var d = disp.ToLowerInvariant(); if (d.Contains("inline")) inlineItems = true;
                        }
                        string ls; if (cssLi.Map.TryGetValue("list-style", out ls) && !string.IsNullOrWhiteSpace(ls) && ls.ToLowerInvariant().Contains("none")) listStyleNone = true;
                        string lst; if (cssLi.Map.TryGetValue("list-style-type", out lst) && !string.IsNullOrWhiteSpace(lst) && lst.ToLowerInvariant().Contains("none")) listStyleNone = true;
                    }
                }
                catch { /* swallow */ }
            }

            if (inlineItems)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
                bool first = true;
                foreach (var li in liNodes)
                {
                    ct.ThrowIfCancellationRequested();
                    var content = await RenderNodeAsync(li, baseUri, onNavigate, js, ct);
                    if (content == null) content = new TextBlock { Text = CollapseWs(GatherText(li)) };
                    if (!first) content.Margin = new Thickness(12, 0, 0, 0);
                    first = false;
                    row.Children.Add(content);
                }
                return Finish(row, n);
            }
            else
            {
                var stack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 6, 0, 6) };
                int i = 1;
                try { if (ordered && n.Attr != null) { string st; if (n.Attr.TryGetValue("start", out st)) { int si; if (int.TryParse(st, out si) && si > 0) i = si; } } } catch { /* swallow */ }
                foreach (var li in liNodes)
                {
                    ct.ThrowIfCancellationRequested();
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                    try { if (ordered && li != null && li.Attr != null) { string v; if (li.Attr.TryGetValue("value", out v)) { int vi; if (int.TryParse(v, out vi) && vi > 0) i = vi; } } } catch { /* swallow */ }
                    
                    if (!listStyleNone)
                    {
                        var cssUl = TryGetCss(n);
                        var cssLi = TryGetCss(li);
                        var effectiveCss = cssLi ?? cssUl;
                        
                        // Check for custom list-style-image
                        string listStyleImg = effectiveCss?.ListStyleImage;
                        if (!string.IsNullOrEmpty(listStyleImg))
                        {
                            try
                            {
                                var img = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 4, 0) };
                                var imgUri = new Uri(listStyleImg, UriKind.RelativeOrAbsolute);
                                var bmp = new BitmapImage(imgUri);
                                img.Source = bmp;
                                row.Children.Add(img);
                            }
                            catch { System.Diagnostics.Debug.WriteLine(" [Engine/DomBasicRenderer.cs] list-style-image load failed"); }
                        }
                        else
                        {
                            var bullet = new TextBlock { Text = (ordered ? (ToOrderedBullet(n, li, i)) : ToUnorderedBullet(n, li)) + " ", Width = 22, HorizontalAlignment = HorizontalAlignment.Left };
                            row.Children.Add(bullet);
                        }

                        // list-style-position: inside moves bullet into content area
                        string listStylePos = effectiveCss?.ListStylePosition;
                        if (listStylePos != null && listStylePos.ToLowerInvariant() == "inside")
                        {
                            // Bullet already added, just adjust spacing
                        }
                    }

                    var content = await RenderNodeAsync(li, baseUri, onNavigate, js, ct);
                    if (content == null) content = new TextBlock { Text = CollapseWs(GatherText(li)) };
                    
                    if (listStyleNone) content.Margin = new Thickness(0, 0, 0, 0);
                    else content.Margin = new Thickness(6, 0, 0, 0);

                    row.Children.Add(content);
                    stack.Children.Add(row);
                    if (ordered) i++;
                }
                return Finish(stack, n);
            }
        }

        private FrameworkElement MakeParagraph(LiteElement n)
        {
            var rtb = new RichTextBlock { TextWrapping = TextWrapping.WrapWholeWords };
            var p = new Paragraph();
            var txt = CollapseWs(GatherText(n));
            var css = TryGetCss(n);
            txt = ApplyHyphens(txt, css);
            p.Inlines.Add(new Run { Text = txt });
            rtb.Blocks.Add(p);
            return ApplyBoxesAndText(rtb, n);
        }

        private FrameworkElement MakeParagraphStyled(LiteElement n, bool isItalic = false, double? sizeOverride = null)
        {
            var rtb = new RichTextBlock { TextWrapping = TextWrapping.WrapWholeWords };
            var p = new Paragraph();
            Inline inline;
            var txt = CollapseWs(GatherText(n));
            var css = TryGetCss(n);
            txt = ApplyHyphens(txt, css);
            if (n.Tag == "u") { var u = new Underline(); u.Inlines.Add(new Run { Text = txt }); inline = u; }
            else { inline = new Run { Text = txt }; }
            p.Inlines.Add(inline);
            rtb.Blocks.Add(p);
            if (isItalic) rtb.FontStyle = FontStyle.Italic;
            if (sizeOverride.HasValue) rtb.FontSize = sizeOverride.Value;
            return ApplyBoxesAndText(rtb, n);
        }

        private FrameworkElement MakeHeading(LiteElement n, double fontSize)
        {
            var rtb = new RichTextBlock { TextWrapping = TextWrapping.WrapWholeWords };
            var p = new Paragraph();
            var txt = CollapseWs(GatherText(n));
            var css = TryGetCss(n);
            txt = ApplyHyphens(txt, css);
            p.Inlines.Add(new Run { Text = txt, FontSize = fontSize, FontWeight = FontWeights.Bold });
            rtb.Blocks.Add(p);
            double size = 24;
            if (n.Tag == "h1") size = 32;
            else if (n.Tag == "h2") size = 24;
            else if (n.Tag == "h3") size = 18.72;
            else if (n.Tag == "h4") size = 16;
            else if (n.Tag == "h5") size = 13.28;
            else if (n.Tag == "h6") size = 10.72;
            rtb.FontSize = size;
            rtb.FontWeight = FontWeights.Bold;
            return ApplyBoxesAndText(rtb, n);
        }

        private FrameworkElement MakeImage(LiteElement n, Uri baseUri)
        {
            var img = new Image { Stretch = Stretch.Uniform };
            var src = DictGet(n.Attr, "src");
            var u = ResolveUri(baseUri, src);
            if (u != null)
            {
                try { img.Source = new BitmapImage(u); } catch { /* swallow */ }
            }
            if (n.Attr != null)
            {
                if (n.Attr.TryGetValue("width", out var w) && double.TryParse(w, out var wd)) img.Width = SanitizeSize(wd, "img.width@attr2");
                if (n.Attr.TryGetValue("height", out var h) && double.TryParse(h, out var hd)) img.Height = SanitizeSize(hd, "img.height@attr2");
            }
            return ApplyBoxesAndText(img, n);
        }

        private FrameworkElement MakeHr(LiteElement n)
        {
            var rect = new Windows.UI.Xaml.Shapes.Rectangle { Height = 2, Fill = new SolidColorBrush(Windows.UI.Colors.Gray), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 6, 0, 6) };
            return ApplyBoxesAndText(rect, n);
        }

        private FrameworkElement MakeBlockquote(LiteElement n)
        {
             var p = MakeParagraph(n);
             p.Margin = new Thickness(16, 0, 16, 0);
             return p;
        }

        private FrameworkElement MakeInput(LiteElement n) 
        { 
            var type = (DictGet(n.Attr, "type") ?? "text").ToLowerInvariant();
            var val = DictGet(n.Attr, "value") ?? "";
            var placeholder = DictGet(n.Attr, "placeholder") ?? "";

            if (type == "password")
            {
                var pb = new PasswordBox { Password = val };
                if (!string.IsNullOrEmpty(placeholder)) 
                {
                     // PasswordBox doesn't support PlaceholderText directly in all versions, 
                     // but we can try setting a tooltip or just leave it.
                     ToolTipService.SetToolTip(pb, placeholder);
                }
                return pb;
            }
            else if (type == "checkbox")
            {
                var cb = new CheckBox { Content = val, IsChecked = n.Attr != null && n.Attr.ContainsKey("checked") };
                return cb;
            }
            else if (type == "radio")
            {
                var rb = new RadioButton { Content = val, IsChecked = n.Attr != null && n.Attr.ContainsKey("checked") };
                if (n.Attr != null && n.Attr.ContainsKey("name")) rb.GroupName = n.Attr["name"];
                return rb;
            }
            else if (type == "submit" || type == "button" || type == "reset")
            {
                var btn = new Button { Content = string.IsNullOrEmpty(val) ? (type == "submit" ? "Submit" : "Button") : val };
                return btn;
            }
            else if (type == "date")
            {
                try { return new DatePicker(); } catch { return new TextBox { Text = val, PlaceholderText = placeholder }; }
            }
            else if (type == "time")
            {
                try { return new TimePicker(); } catch { return new TextBox { Text = val, PlaceholderText = placeholder }; }
            }
            
            return new TextBox { Text = val, PlaceholderText = placeholder }; 
        }

        private FrameworkElement MakeTextarea(LiteElement n)
        {
            var val = n.IsText ? n.Text : GatherText(n);
            var placeholder = DictGet(n.Attr, "placeholder") ?? "";
            return new TextBox { Text = val ?? "", PlaceholderText = placeholder, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
        }

        private FrameworkElement MakeSelect(LiteElement n)
        {
            var cb = new ComboBox();
            if (n.Children != null)
            {
                foreach (var child in n.Children)
                {
                    if (child.Tag == "option")
                    {
                        var item = new ComboBoxItem { Content = GatherText(child) };
                        if (child.Attr != null && child.Attr.ContainsKey("selected")) item.IsSelected = true;
                        cb.Items.Add(item);
                    }
                }
            }
            if (cb.Items.Count > 0 && cb.SelectedIndex < 0) cb.SelectedIndex = 0;
            return cb;
        }

        private FrameworkElement MakeLink(LiteElement n, Uri baseUri, Action<Uri> onNavigate, JavaScriptEngine js)
        {
            string text = null;
            var href = DictGet(n.Attr, "href");
            Uri abs = ResolveUri(baseUri, href);
            
            // Handle Google redirector or similar tracking params if needed
            try
            {
                if (abs != null && !string.IsNullOrWhiteSpace(href))
                {
                    int qidx = href.IndexOf('?');
                    if (qidx >= 0 && qidx < href.Length - 1)
                    {
                        var query = href.Substring(qidx + 1);
                        string target = null;
                        foreach (var part in query.Split('&'))
                        {
                            var kv = part.Split(new[] { '=' }, 2);
                            if (kv.Length == 2)
                            {
                                var name = kv[0]; var val = Uri.UnescapeDataString(kv[1]);
                                if (string.Equals(name, "url", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "q", StringComparison.OrdinalIgnoreCase))
                                {
                                    target = val; break;
                                }
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(target))
                        {
                            Uri t;
                            if (Uri.TryCreate(target, UriKind.Absolute, out t)) abs = t;
                            else if (Uri.TryCreate(baseUri, target, out t)) abs = t;
                        }
                    }
                }
            }
            catch { /* swallow */ }

            // Prefer rendering an inline image if the link wraps one (e.g., Google logo)
            FrameworkElement inlineContent = null;
            try
            {
                var imgChild0 = n.Children.FirstOrDefault(c => c.Tag == "img");
                if (imgChild0 != null)
                {
                    var u = ResolveBestImgUri(imgChild0, baseUri);
                    if (u != null)
                    {
                        var img = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
                        // Size hints from attributes or computed CSS
                        try
                        {
                            int iw = 0, ih = 0; string ws = null, hs = null;
                            if (imgChild0.Attr != null)
                            {
                                imgChild0.Attr.TryGetValue("width", out ws);
                                imgChild0.Attr.TryGetValue("height", out hs);
                            }
                            if (!string.IsNullOrWhiteSpace(ws)) int.TryParse(ws, out iw);
                            if (!string.IsNullOrWhiteSpace(hs)) int.TryParse(hs, out ih);
                            if (iw <= 0 || ih <= 0)
                            {
                                CssComputed cssImg = null; if (ComputedStyles != null) ComputedStyles.TryGetValue(imgChild0, out cssImg);
                                if (cssImg != null)
                                {
                                    if (iw <= 0 && cssImg.Width.HasValue) iw = (int)cssImg.Width.Value;
                                    if (ih <= 0 && cssImg.Height.HasValue) ih = (int)cssImg.Height.Value;
                                }
                            }
                            if (iw > 0) img.Width = SanitizeSize(iw, "img.width@css"); if (ih > 0) img.Height = SanitizeSize(ih, "img.height@css");
                        }
                        catch { /* swallow */ }
                        // Bind source now, then try cookie-aware loader asynchronously for fidelity
                        try { img.Source = new BitmapImage(u); } catch { /* swallow */ }
                        var uiImg = img; var reqW = (int)(uiImg.Width > 0 ? uiImg.Width : Window.Current?.Bounds.Width ?? 360.0);
                        // NOTE: async image enhancement removed to keep link image creation strictly synchronous/UI-thread bound.
                        // Future: queue background decode, then dispatcher assign if needed.
                        inlineContent = img;
                    }
                }
            }
            catch { /* swallow */ }
            if (inlineContent == null)
            {
                // If anchor has only a CSS background image, synthesize an inline Image so it contributes layout
                try
                {
                    CssComputed cssA = null; if (ComputedStyles != null) ComputedStyles.TryGetValue(n, out cssA);
                    string bgDecl = null;
                    if (cssA != null && cssA.Map != null)
                    {
                        if (!cssA.Map.TryGetValue("background-image", out bgDecl) || string.IsNullOrWhiteSpace(bgDecl))
                            cssA.Map.TryGetValue("background", out bgDecl);
                    }
                    if (!string.IsNullOrWhiteSpace(bgDecl) && bgDecl.IndexOf("url", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(bgDecl, "url\\(['\"']?(?<u>[^)\"']+)['\"']?\\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            var raw = m.Groups["u"].Value;
                            raw = RewriteImageUrlIfNeeded(raw);
                            var u = ResolveUri(baseUri, raw);
                            if (u != null)
                            {
                                var img = new Image { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left };
                                try { img.Source = new BitmapImage(u); } catch { /* swallow */ }
                                inlineContent = img;
                            }
                        }
                    }
                }
                catch { /* swallow */ }

                if (inlineContent == null && string.IsNullOrWhiteSpace(text))
                {
                    var imgChild = n.Children.FirstOrDefault(c => c.Tag == "img" && c.Attr != null && c.Attr.ContainsKey("alt"));
                    if (imgChild != null) text = CollapseWs(imgChild.Attr["alt"] ?? "");
                }
            }
            if (string.IsNullOrWhiteSpace(text) && n.Attr != null)
            {
                string t;
                if (n.Attr.TryGetValue("title", out t) && !string.IsNullOrWhiteSpace(t)) text = t;
                else if (n.Attr.TryGetValue("aria-label", out t) && !string.IsNullOrWhiteSpace(t)) text = t;
            }
            if (string.IsNullOrWhiteSpace(text)) text = string.IsNullOrEmpty(href) ? "(link)" : href;

            var hb = new HyperlinkButton
            {
                Content = (object)inlineContent ?? (object)text,
                Foreground = LinkBrush,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 4),
                Padding = new Thickness(0, 0, 0, 0)
            };
            try { Windows.UI.Xaml.Automation.AutomationProperties.SetName(hb, text); } catch { /* swallow */ }
            try { hb.IsTabStop = true; } catch { /* swallow */ }
            // Key handling falls through to Click via default behaviors
            ToolTipService.SetToolTip(hb, abs != null ? (object)abs.AbsoluteUri : href);

            // If CSS computed style indicates display:block (or list-item), treat this link as a block
            try
            {
                var css = TryGetCss(n);
                var disp = css != null ? (css.Map != null && css.Map.ContainsKey("display") ? (css.Map["display"] ?? "").ToLowerInvariant() : (css.Display ?? "").ToLowerInvariant()) : string.Empty;
                if (disp.Contains("block") || disp.Contains("list-item"))
                {
                    hb.HorizontalAlignment = HorizontalAlignment.Stretch;
                    hb.Margin = new Thickness(0, 6, 0, 6);
                }
            }
            catch { /* swallow */ }

            if (!string.IsNullOrWhiteSpace(href) && href.Trim().StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            {
                abs = null; // strip unsafe javascript: URLs
            }

            // Additional google redirector forms (e.g., /url=/httpservice/retry/enablejs or /url=<absolute>)
            try
            {
                if (abs == null && !string.IsNullOrWhiteSpace(href) && baseUri != null)
                {
                    var host = baseUri.Host ?? string.Empty;
                    var trimmed = href.Trim();
                    if (host.IndexOf("google.", StringComparison.OrdinalIgnoreCase) >= 0 && trimmed.StartsWith("/url=", StringComparison.OrdinalIgnoreCase))
                    {
                        var after = trimmed.Substring(5); // after '/url='
                        // If it looks like an absolute URL, use it; otherwise, ignore (enablejs etc.)
                        Uri t;
                        if (Uri.TryCreate(after, UriKind.Absolute, out t)) abs = t;
                        else if (after.StartsWith("/httpservice/retry/enablejs", StringComparison.OrdinalIgnoreCase)) abs = null; // ignore JS-enabler stubs
                    }
                }
            }
            catch { /* swallow */ }

            // Normalize Google redirectors for absolute links too (https://google.com/url?... or /url=...)
            try
            {
                if (abs != null)
                {
                    var host = abs.Host ?? string.Empty;
                    if (host.IndexOf("google.", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var path = abs.AbsolutePath ?? string.Empty;
                        Uri resolvedTarget = null;

                        if (string.Equals(path, "/url", StringComparison.OrdinalIgnoreCase))
                        {
                            var q = (abs.Query ?? string.Empty).TrimStart('?');
                            string target = null;
                            var parts = q.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int i = 0; i < parts.Length; i++)
                            {
                                var kv = parts[i].Split(new[] { '=' }, 2);
                                if (kv.Length == 2)
                                {
                                    var name = kv[0];
                                    var val = Uri.UnescapeDataString(kv[1] ?? "");
                                    if (string.Equals(name, "url", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "q", StringComparison.OrdinalIgnoreCase))
                                    { target = val; break; }
                                }
                            }
                            if (!string.IsNullOrWhiteSpace(target))
                            {
                                // Ignore JS enabler
                                if (target.StartsWith("/httpservice/retry/enablejs", StringComparison.OrdinalIgnoreCase)) { resolvedTarget = null; }
                                else
                                {
                                    Uri t;
                                    if (Uri.TryCreate(target, UriKind.Absolute, out t)) resolvedTarget = t;
                                    else if (baseUri != null && Uri.TryCreate(baseUri, target, out t)) resolvedTarget = t;
                                }
                            }
                        }
                        else if (path.StartsWith("/url=", StringComparison.OrdinalIgnoreCase))
                        {
                            var target = Uri.UnescapeDataString(path.Substring(5));
                            if (!string.IsNullOrWhiteSpace(target) && !target.StartsWith("/httpservice/retry/enablejs", StringComparison.OrdinalIgnoreCase))
                            {
                                Uri t;
                                if (Uri.TryCreate(target, UriKind.Absolute, out t)) resolvedTarget = t;
                                else if (baseUri != null && Uri.TryCreate(baseUri, target, out t)) resolvedTarget = t;
                            }
                        }

                        if (resolvedTarget != null) abs = resolvedTarget; else if (!string.Equals(path, "/url", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("/url=", StringComparison.OrdinalIgnoreCase)) { /* keep abs */ }
                        else if (resolvedTarget == null) { abs = null; }
                    }
                }
            }
            catch { /* swallow */ }

            if (abs != null && onNavigate != null)
                hb.Click += (s, e) =>
                {
                    string elId = null; try { if (n.Attr != null) n.Attr.TryGetValue("id", out elId); } catch { /* swallow */ }
                    bool cancel = false;
                    // Inline JS handler may cancel default ("return false;")
                    string onclick;
                    if (n.Attr != null && n.Attr.TryGetValue("onclick", out onclick) && Js != null)
                    {
                        var script = PreprocessInlineHandler(onclick, elId);
                        cancel = Js.RunInline(script, new JsContext { BaseUri = baseUri }, "click", elId);
                    }
                    // Fire addEventListener listeners synchronously to honor preventDefault/stopPropagation
                    bool cancelDom = false;
                    try { if (!string.IsNullOrWhiteSpace(elId) && Js != null) cancelDom = Js.RaiseElementEventSync(elId, "click"); } catch { cancelDom = false; }
                    if (cancel || cancelDom)
                    {
                        TrySetHandled(e);
                        return;
                    }
                    if (abs != null) onNavigate(abs);
                };

            if (abs != null)
            {
                hb.PointerEntered += (s, e) => { StatusMessage?.Invoke(abs.AbsoluteUri); };
                hb.PointerExited += (s, e) => { StatusMessage?.Invoke(string.Empty); };
                hb.Holding += (s, e) =>
                {
                    if (e.HoldingState == Windows.UI.Input.HoldingState.Started) LinkLongPressed?.Invoke(abs);
                };
            }

            return hb;
        }

        private FrameworkElement RenderLooseInput(LiteElement n)
        {
            string type = null; if (n.Attr != null) n.Attr.TryGetValue("type", out type);
            type = (type ?? "text").ToLowerInvariant();

            if (n.Tag == "textarea")
                return new TextBox { AcceptsReturn = true, Height = 80, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6), Background = new SolidColorBrush(Windows.UI.Colors.White), Foreground = new SolidColorBrush(Windows.UI.Colors.Black), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 220, 220)), Padding = new Thickness(8,4,8,4) };

            if (type == "submit" || n.Tag == "button")
            {
                var btn = new Button { Content = CollapseWs(GatherText(n)).Length > 0 ? CollapseWs(GatherText(n)) : "Submit", Margin = new Thickness(0, 4, 0, 4) };
                // Add click event handling
                btn.Click += (s, e) =>
                {
                    bool cancel = false;
                    try
                    {
                        if (Js != null)
                        {
                            string id = null; try { if (n.Attr != null) n.Attr.TryGetValue("id", out id); } catch { /* swallow */ }
                            if (!string.IsNullOrWhiteSpace(id))
                            {
                                cancel = Js.RaiseElementEventSync(id, "click");
                            }
                        }
                    }
                    catch { /* swallow */ }
                    if (cancel) { TrySetHandled(e); }
                };
                return btn;
            }

            return new TextBox { Margin = new Thickness(0, 4, 0, 4), Background = new SolidColorBrush(Windows.UI.Colors.White), Foreground = new SolidColorBrush(Windows.UI.Colors.Black), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 220, 220)), Padding = new Thickness(8,4,8,4), Height = 36 };
        }

        private sealed class OptionItem
        {
            public string Text { get; set; }
            public string Value { get; set; }
            public override string ToString() => Text ?? Value ?? "";
        }

        private async Task<FrameworkElement> MakeFormAsync(LiteElement form, Uri baseUri, Action<Uri> onNavigate, JavaScriptEngine js, CancellationToken ct)
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 2, 0, 8) };

            var inputsSingle = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);
            var inputsMulti = new Dictionary<string, List<Control>>(StringComparer.OrdinalIgnoreCase);
            var staticInputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string submitText = "Submit";
            var textBoxes = new List<TextBox>();

            // Flatten descendants
            var all = new List<LiteElement>();
            var stack = new Stack<LiteElement>();
            foreach (var ch in form.Children) stack.Push(ch);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                all.Add(cur);
                for (int i = cur.Children.Count - 1; i >= 0; i--) stack.Push(cur.Children[i]);
            }

            foreach (var child in all)
            {
                ct.ThrowIfCancellationRequested();
                if (child.Attr == null) continue;

                // Skip disabled
                if (child.Attr.ContainsKey("disabled")) continue;

                string name;

                if (child.Tag == "input" && child.Attr.TryGetValue("name", out name))
                {
                    string type; child.Attr.TryGetValue("type", out type);
                    type = (type ?? "text").ToLowerInvariant();

                    if (type == "hidden")
                    {
                        string val; child.Attr.TryGetValue("value", out val);
                        staticInputs[name] = val ?? "";
                        continue;
                    }

                    if (type == "checkbox")
                    {
                        var cb = new CheckBox { Margin = new Thickness(0, 4, 0, 4) };
                        string v; child.Attr.TryGetValue("value", out v);
                        cb.Content = CollapseWs(DictGet(child.Attr, "label") ?? child.Text ?? name);
                        panel.Children.Add(cb);
                        if (!inputsMulti.ContainsKey(name)) inputsMulti[name] = new List<Control>();
                        inputsMulti[name].Add(cb);
                        continue;
                    }

                    if (type == "radio")
                    {
                        var rb = new RadioButton { Margin = new Thickness(0, 4, 0, 4), GroupName = name };
                        string v; child.Attr.TryGetValue("value", out v);
                        rb.Content = CollapseWs(DictGet(child.Attr, "label") ?? child.Text ?? (v ?? name));
                        panel.Children.Add(rb);
                        if (!inputsMulti.ContainsKey(name)) inputsMulti[name] = new List<Control>();
                        inputsMulti[name].Add(rb);
                        continue;
                    }

                    if (type == "text" || type == "search" || type == "email" || type == "url" || type == "password")
                    {
                        var tb = new TextBox { Margin = new Thickness(0, 4, 0, 4) };
                        string val; if (child.Attr.TryGetValue("value", out val)) tb.Text = val;
                        panel.Children.Add(tb);
                        inputsSingle[name] = tb;
                        textBoxes.Add(tb);
                        continue;
                    }

                    if (type == "submit")
                    {
                        string value;
                        if (child.Attr.TryGetValue("value", out value) && !string.IsNullOrWhiteSpace(value))
                            submitText = value;
                        continue;
                    }
                }
                else if (child.Tag == "textarea" && child.Attr.TryGetValue("name", out name))
                {
                    var tb = new TextBox { AcceptsReturn = true, Height = 80, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
                    panel.Children.Add(tb);
                    inputsSingle[name] = tb;
                    textBoxes.Add(tb);
                }
                else if (child.Tag == "select" && child.Attr.TryGetValue("name", out name))
                {
                    bool multiple = child.Attr.ContainsKey("multiple");
                    var options = child.Children.Where(c => c.Tag == "option").ToList();

                    if (multiple)
                    {
                        var lb = new ListBox { SelectionMode = SelectionMode.Multiple, Margin = new Thickness(0, 4, 0, 4) };
                        foreach (var opt in options)
                        {
                            var it = new OptionItem
                            {
                                Text = CollapseWs(opt.Text ?? DictGet(opt.Attr, "label") ?? DictGet(opt.Attr, "value")),
                                Value = DictGet(opt.Attr, "value") ?? CollapseWs(opt.Text)
                            };
                            lb.Items.Add(it);
                        }
                        panel.Children.Add(lb);
                        if (!inputsMulti.ContainsKey(name)) inputsMulti[name] = new List<Control>();
                        inputsMulti[name].Add(lb);
                    }
                    else
                    {
                        var combo = new ComboBox { Margin = new Thickness(0, 4, 0, 4) };
                        foreach (var opt in options)
                        {
                            var it = new OptionItem
                            {
                                Text = CollapseWs(opt.Text ?? DictGet(opt.Attr, "label") ?? DictGet(opt.Attr, "value")),
                                Value = DictGet(opt.Attr, "value") ?? CollapseWs(opt.Text)
                            };
                            combo.Items.Add(it);
                        }
                        panel.Children.Add(combo);
                        inputsSingle[name] = combo;
                    }
                }
            }

            if (inputsSingle.Count == 0 && inputsMulti.Count == 0)
            {
                var tb = new TextBox { Margin = new Thickness(0, 4, 0, 4), PlaceholderText = "Search?", Background = new SolidColorBrush(Windows.UI.Colors.White), Foreground = new SolidColorBrush(Windows.UI.Colors.Black), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 220, 220)), Padding = new Thickness(8,4,8,4), Height = 36 };
                panel.Children.Add(tb);
                inputsSingle["q"] = tb;
                submitText = "Search";
                textBoxes.Add(tb);
            }

            var submitBtn = new Button
            {
                Content = submitText,
                Margin = new Thickness(0, 6, 0, 6),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 245, 245)),
                Foreground = new SolidColorBrush(Windows.UI.Colors.Black),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 210, 210, 210)),
                Padding = new Thickness(12, 6, 12, 6),
                MinHeight = 32,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            // Heuristic: widen primary text inputs (search box)
            try
            {
                double vw = 0; try { vw = Window.Current.Bounds.Width; } catch { /* swallow */ }
                if (vw <= 0) vw = 360;
                double minSearch = Math.Min(720, Math.Max(220, vw * 0.75));
                double maxSearch = Math.Min(800, Math.Max(minSearch, vw * 0.95));

                // If there is exactly one text box, treat it as the main input
                if (textBoxes.Count == 1)
                {
                    var tb0 = textBoxes[0];
                    if (tb0 != null)
                    {
                        if (double.IsNaN(tb0.Width) || tb0.Width <= 0) tb0.MinWidth = minSearch;
                        tb0.MaxWidth = maxSearch;
                        tb0.HorizontalAlignment = HorizontalAlignment.Left;
                    }
                }
                else
                {
                    foreach (var tbx in textBoxes)
                    {
                        if (tbx == null) continue;
                        if (double.IsNaN(tbx.Width) || tbx.Width <= 0) tbx.MinWidth = Math.Min(560, Math.Max(180, vw * 0.55));
                        tbx.MaxWidth = Math.Min(720, Math.Max(tbx.MinWidth, vw * 0.9));
                        tbx.HorizontalAlignment = HorizontalAlignment.Left;
                    }
                }
            }
            catch { /* swallow */ }

            // Focus first textbox if any
            TextBox firstTb = null;
            foreach (var ctrl in inputsSingle.Values) { var tb = ctrl as TextBox; if (tb != null) { firstTb = tb; break; } }
            if (firstTb != null) firstTb.Loaded += (s, e) => firstTb.Focus(FocusState.Programmatic);

            // Submit action
            Action submit = () =>
            {
                // onsubmit JS (may cancel via return false)
                string onsubmit;
                if (form.Attr != null && form.Attr.TryGetValue("onsubmit", out onsubmit) && Js != null)
                {
                    string formId = null; try { if (form.Attr != null) form.Attr.TryGetValue("id", out formId); } catch { /* swallow */ }
                    bool cancel = Js.RunInline(onsubmit, new JsContext { BaseUri = baseUri }, "submit", formId);
                    if (cancel) return;
                }
                // Property-based handler (form.onsubmit = function(){...})
                try
                {
                    /* property onsubmit not canceling in JS-0 */
                }
                catch { /* swallow */ }

                string action = null; if (form.Attr != null) form.Attr.TryGetValue("action", out action);
                string method = null; if (form.Attr != null) form.Attr.TryGetValue("method", out method);
                method = (method ?? "get").Trim().ToLowerInvariant();

            var qs = new System.Text.StringBuilder();

                Action<string, string> append = (k, v) =>
                {
                    if (qs.Length > 0) qs.Append("&");
                    qs.Append(Uri.EscapeDataString(k)).Append("=").Append(Uri.EscapeDataString(v ?? ""));
                };

                try
                {
                    // Compatibility hint: Google results render better in Basic HTML mode under limited JS engines
                    string host = baseUri != null ? (baseUri.Host ?? "").ToLowerInvariant() : "";
                    if (host.Contains("google."))
                    {
                        append("hl", "en");
                        append("gbv", "1");
                    }
                }
                catch { /* swallow */ }

                foreach (var kv in staticInputs) append(kv.Key, kv.Value);

                foreach (var kv in inputsSingle)
                {
                    var ctrl = kv.Value;
                    var tbx = ctrl as TextBox;
                    var combo = ctrl as ComboBox;

                    if (tbx != null) append(kv.Key, tbx.Text ?? "");
                    else if (combo != null)
                    {
                        var it = combo.SelectedItem as OptionItem;
                        append(kv.Key, it?.Value ?? it?.Text ?? "");
                    }
                }

                foreach (var kv in inputsMulti)
                {
                    foreach (var ctrl in kv.Value)
                    {
                        var cb = ctrl as CheckBox;
                        var rb = ctrl as RadioButton;
                        var lb = ctrl as ListBox;

                        if (cb != null)
                        {
                            if (cb.IsChecked == true)
                            {
                                // value attribute if provided, else "on"
                                append(kv.Key, "on");
                            }
                        }
                        else if (rb != null)
                        {
                            if (rb.IsChecked == true)
                            {
                                append(kv.Key, (rb.Content as string) ?? "on");
                            }
                        }
                        else if (lb != null)
                        {
                            foreach (var sel in lb.SelectedItems)
                            {
                                var it = sel as OptionItem;
                                append(kv.Key, it?.Value ?? it?.Text ?? "");
                            }
                        }
                    }
                }

                string forcedAction = null;
                if (string.IsNullOrEmpty(action) && baseUri != null)
                {
                    var host = baseUri.Host.ToLowerInvariant();
                    if (host.Contains("google.")) forcedAction = baseUri.Scheme + "://www.google.com/search";
                    else if (host.Contains("bing.")) forcedAction = baseUri.Scheme + "://www.bing.com/search";
                    else if (host.Contains("duckduckgo.")) forcedAction = baseUri.Scheme + "://duckduckgo.com/";
                }

                Uri target = null;
                string actionOrForced = !string.IsNullOrEmpty(action) ? action : forcedAction;
                if (string.IsNullOrEmpty(actionOrForced)) target = baseUri;
                else
                {
                    Uri abs;
                    if (Uri.TryCreate(actionOrForced, UriKind.Absolute, out abs)) target = abs;
                    else if (baseUri != null && Uri.TryCreate(baseUri, actionOrForced, out abs)) target = abs;
                }
                if (target == null) return;

                if (method == "get")
                {
                    if (onNavigate != null)
                    {
                        string sep = (target.Query != null && target.Query.Length > 0) ? "&" : "?";
                        var nav = new Uri(target.AbsoluteUri + sep + qs.ToString());
                        onNavigate(nav);
                    }
                    return;
                }

                if (method == "post")
                {
                    if (OnPost != null)
                    {
                        OnPost(target, qs.ToString());
                        return;
                    }
                    // fallback to GET append
                    if (onNavigate != null)
                    {
                        string sep = (target.Query != null && target.Query.Length > 0) ? "&" : "?";
                        var nav = new Uri(target.AbsoluteUri + sep + qs.ToString());
                        onNavigate(nav);
                    }
                    return;
                }

                // Otherwise, safe GET fallback
                if (onNavigate != null)
                {
                    string sep = (target.Query != null && target.Query.Length > 0) ? "&" : "?";
                    var nav = new Uri(target.AbsoluteUri + sep + qs.ToString());
                    onNavigate(nav);
                }
            };

            foreach (var tb in textBoxes)
            {
                tb.KeyDown += (s, e) =>
                {
                    if (!tb.AcceptsReturn && e.Key == Windows.System.VirtualKey.Enter)
                    {
                        TrySetHandled(e);
                        submit();
                    }
                };
            }

            submitBtn.Click += (s, e) =>
            {
                bool cancel = false;
                try
                {
                    if (Js != null)
                    {
                        string id = null; try { if (form.Attr != null) form.Attr.TryGetValue("id", out id); } catch { /* swallow */ }
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            cancel = Js.RaiseElementEventSync(id, "click");
                        }
                    }
                }
                catch { /* swallow */ }
                if (cancel) { TrySetHandled(e); return; }
                submit();
            };

            panel.Children.Add(submitBtn);

            // Center narrow single-form pages for visibility
            try
            {
                if (textBoxes.Count <= 1 && inputsSingle.Count + inputsMulti.Count <= 2)
                {
                    var wrap = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
                    // Pull content up a bit to reduce empty top area
                    try
                    {
                        if (baseUri != null && (baseUri.Host ?? "").ToLowerInvariant().Contains("google."))
                            wrap.Margin = new Thickness(0, 12, 0, 0);
                        else
                            wrap.Margin = new Thickness(0, 8, 0, 0);
                    }
                    catch { /* swallow */ }
                    wrap.Children.Add(panel);
                    return Finish(wrap, form);
                }
            }
            catch { /* swallow */ }

            return Finish(panel, form);
        }

        private static int GetIntAttr(LiteElement n, string name, int fallback = 1)
        {
            if (n?.Attr == null) return fallback;
            string v; if (!n.Attr.TryGetValue(name, out v)) return fallback;
            int i; return int.TryParse(v, out i) && i > 0 ? i : fallback;
        }


        // ---------- Blockquote ----------
        private async Task<FrameworkElement> MakeBlockquote(LiteElement n, Uri baseUri, Action<Uri> onNavigate, JavaScriptEngine js, CancellationToken ct)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 8, 0, 8) };

            if (n.Children != null)
            foreach (var ch in n.Children)
            {
                ct.ThrowIfCancellationRequested();
                var elt = await RenderNodeAsync(ch, baseUri, onNavigate, js, ct);
                if (elt != null) stack.Children.Add(elt);
            }

            var adorn = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 0),
                Margin = new Thickness(0, 0, 0, 0),
                Child = stack
            };

            var wrap = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            wrap.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            wrap.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var bar = new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 180, 180, 180)), Margin = new Thickness(0, 2, 8, 2) };
            Grid.SetColumn(bar, 0);
            Grid.SetColumn(adorn, 1);

            wrap.Children.Add(bar);
            wrap.Children.Add(adorn);

            return Finish(wrap, n);
        }

        // ---------- CSS hookup & wrapping ----------
        private CssComputed TryGetCss(LiteElement n)
        {
            if (ComputedStyles == null || n == null) return null;

            CssComputed css;
            if (ComputedStyles.TryGetValue(n, out css) && css != null) return css;

            try
            {
                var k = ComputedStyles.Keys.FirstOrDefault(k2 => k2 != null &&
                    string.Equals(k2.Tag, n.Tag, StringComparison.OrdinalIgnoreCase));
                if (k != null && ComputedStyles.TryGetValue(k, out css) && css != null) return css;
            }
            catch { /* swallow */ }

            return null;
        }

        // Back-compat shim for older call sites that still call ApplyBoxesAndText.
        // It delegates to the newer Finish(...) pipeline so you keep all styling behavior.
        private FrameworkElement ApplyBoxesAndText(FrameworkElement inner, LiteElement n)
        {
            return Finish(inner, n);
        }

        private void ApplyComputedLayout(FrameworkElement fe, CssComputed css)
        {
            if (fe == null || css == null) return;

            double widthAdjust = 0, heightAdjust = 0;
            bool isBorderBox = string.Equals(css.BoxSizing, "border-box", StringComparison.OrdinalIgnoreCase);

            if (!isBorderBox)
            {
                widthAdjust = css.Padding.Left + css.Padding.Right + css.BorderThickness.Left + css.BorderThickness.Right;
                heightAdjust = css.Padding.Top + css.Padding.Bottom + css.BorderThickness.Top + css.BorderThickness.Bottom;
            }

            // Safety: if adjustments are NaN, zero them out
            if (double.IsNaN(widthAdjust)) widthAdjust = 0;
            if (double.IsNaN(heightAdjust)) heightAdjust = 0;

            if (css.Width.HasValue && !double.IsNaN(css.Width.Value) && css.Width.Value > 0)
                fe.Width = SanitizeSize(css.Width.Value + widthAdjust, "css.Width");
            if (css.Height.HasValue && !double.IsNaN(css.Height.Value) && css.Height.Value > 0)
                fe.Height = SanitizeSize(css.Height.Value + heightAdjust, "css.Height");
            if (css.MinWidth.HasValue && !double.IsNaN(css.MinWidth.Value) && css.MinWidth.Value > 0)
                fe.MinWidth = css.MinWidth.Value + widthAdjust;
            if (css.MinHeight.HasValue && !double.IsNaN(css.MinHeight.Value) && css.MinHeight.Value > 0)
                fe.MinHeight = css.MinHeight.Value + heightAdjust;
            if (css.MaxWidth.HasValue && !double.IsNaN(css.MaxWidth.Value) && css.MaxWidth.Value > 0)
                fe.MaxWidth = css.MaxWidth.Value + widthAdjust;
            if (css.MaxHeight.HasValue && !double.IsNaN(css.MaxHeight.Value) && css.MaxHeight.Value > 0)
                fe.MaxHeight = css.MaxHeight.Value + heightAdjust;
            
            if (css.Margin.Left != 0 || css.Margin.Top != 0 || css.Margin.Right != 0 || css.Margin.Bottom != 0)
            {
                fe.Margin = css.Margin;
            }
        }

        private void ApplyTransform(FrameworkElement fe, string transformValue)
        {
            if (fe == null || string.IsNullOrWhiteSpace(transformValue)) return;

            var transforms = new System.Collections.Generic.List<Transform>();
            var parts = transformValue.Split(new[] { ')' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                // translate(x, y) or translateX(x) or translateY(y)
                if (trimmed.StartsWith("translate(", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("translateX(", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("translateY(", StringComparison.OrdinalIgnoreCase))
                {
                    double tx = 0, ty = 0;
                    var openIdx = trimmed.IndexOf('(');
                    var args = trimmed.Substring(openIdx + 1).Trim();
                    var argParts = args.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                    double val;
                    if (argParts.Length > 0 && TryParseCssDouble(argParts[0].Trim(), out val)) tx = val;
                    if (argParts.Length > 1 && TryParseCssDouble(argParts[1].Trim(), out val)) ty = val;

                    if (trimmed.StartsWith("translateX(", StringComparison.OrdinalIgnoreCase))
                    {
                        ty = 0;
                    }
                    else if (trimmed.StartsWith("translateY(", StringComparison.OrdinalIgnoreCase))
                    {
                        tx = 0;
                    }

                    transforms.Add(new TranslateTransform { X = tx, Y = ty });
                }
                // scale(x, y) or scaleX(x) or scaleY(y)
                else if (trimmed.StartsWith("scale(", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.StartsWith("scaleX(", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.StartsWith("scaleY(", StringComparison.OrdinalIgnoreCase))
                {
                    double sx = 1, sy = 1;
                    var openIdx = trimmed.IndexOf('(');
                    var args = trimmed.Substring(openIdx + 1).Trim();
                    var argParts = args.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                    double val;
                    if (argParts.Length > 0 && double.TryParse(argParts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out val)) sx = val;
                    if (argParts.Length > 1 && double.TryParse(argParts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out val)) sy = val;

                    if (trimmed.StartsWith("scaleX(", StringComparison.OrdinalIgnoreCase))
                    {
                        sy = 1;
                    }
                    else if (trimmed.StartsWith("scaleY(", StringComparison.OrdinalIgnoreCase))
                    {
                        sx = 1;
                    }

                    // Safety: ignore degenerate scales that collapse elements
                    if (sx > 0.01 && sy > 0.01)
                        transforms.Add(new ScaleTransform { ScaleX = sx, ScaleY = sy });
                }
                // rotate(angle)
                else if (trimmed.StartsWith("rotate(", StringComparison.OrdinalIgnoreCase))
                {
                    var openIdx = trimmed.IndexOf('(');
                    var args = trimmed.Substring(openIdx + 1).Trim();
                    double angle = 0;

                    if (args.EndsWith("deg"))
                    {
                        var numStr = args.Substring(0, args.Length - 3).Trim();
                        double.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out angle);
                    }
                    else if (args.EndsWith("rad"))
                    {
                        var numStr = args.Substring(0, args.Length - 3).Trim();
                        double rad;
                        if (double.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rad))
                            angle = rad * 180.0 / Math.PI;
                    }
                    else
                    {
                        double.TryParse(args, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out angle);
                    }

                    transforms.Add(new RotateTransform { Angle = angle });
                }
            }

            if (transforms.Count == 1)
            {
                fe.RenderTransform = transforms[0];
            }
            else if (transforms.Count > 1)
            {
                var group = new TransformGroup();
                foreach (var t in transforms) group.Children.Add(t);
                fe.RenderTransform = group;
            }
        }

        private static Point? ParseTransformOrigin(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim().ToLowerInvariant();

            // Keyword positions
            if (value == "center" || value == "center center" || value == "50% 50%")
                return new Point(0.5, 0.5);
            if (value == "top" || value == "top center" || value == "center top")
                return new Point(0.5, 0.0);
            if (value == "bottom" || value == "bottom center" || value == "center bottom")
                return new Point(0.5, 1.0);
            if (value == "left" || value == "left center" || value == "center left")
                return new Point(0.0, 0.5);
            if (value == "right" || value == "right center" || value == "center right")
                return new Point(1.0, 0.5);
            if (value == "top left" || value == "left top")
                return new Point(0.0, 0.0);
            if (value == "top right" || value == "right top")
                return new Point(1.0, 0.0);
            if (value == "bottom left" || value == "left bottom")
                return new Point(0.0, 1.0);
            if (value == "bottom right" || value == "right bottom")
                return new Point(1.0, 1.0);

            // Parse "X Y" (percentages or px)
            var parts = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                double x, y;
                // X
                var xs = parts[0].Trim();
                if (xs.EndsWith("%"))
                {
                    double xv;
                    if (double.TryParse(xs.Substring(0, xs.Length - 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out xv))
                        x = xv / 100.0;
                    else return null;
                }
                else if (xs.EndsWith("px"))
                {
                    // px can't be converted to relative origin, default to center
                    x = 0.5;
                }
                else
                {
                    if (double.TryParse(xs, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x))
                        x = x / 100.0; // assume percentage if unitless
                    else return null;
                }

                // Y
                var ys = parts[1].Trim();
                if (ys.EndsWith("%"))
                {
                    double yv;
                    if (double.TryParse(ys.Substring(0, ys.Length - 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out yv))
                        y = yv / 100.0;
                    else return null;
                }
                else if (ys.EndsWith("px"))
                {
                    y = 0.5;
                }
                else
                {
                    if (double.TryParse(ys, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y))
                        y = y / 100.0;
                    else return null;
                }

                return new Point(x, y);
            }

            return null;
        }

        private bool TryParseCssDouble(string s, out double v)
        {
            v = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            var sl = s.ToLowerInvariant();
            if (sl.EndsWith("px")) s = s.Substring(0, s.Length - 2).Trim();
            return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
        }

        private FrameworkElement Finish(FrameworkElement content, LiteElement n)
        {
            var css = TryGetCss(n);

            // Fallback: if we have a background color but the element didn't support it (e.g. TextBlock), wrap it
            // This runs if WrapWithBoxes didn't wrap it (e.g. css was null or background was missing in css but present in inline style)
            if (n.Attr != null && n.Attr.ContainsKey("style"))
            {
                var kv = ParseInlineStyle(n.Attr["style"]);
                string bg = null;
                if (kv.TryGetValue("background-color", out bg) || kv.TryGetValue("background", out bg))
                {
                    var brush = TryBrush(bg);
                    if (brush != null && !(content is Border) && !(content is Panel))
                    {
                        var b = new Border { Child = content, Background = brush };
                        // Move margin to the wrapper so background doesn't cover the margin
                        b.Margin = content.Margin;
                        content.Margin = new Thickness(0);
                        content = b;
                    }
                }
            }

            // Fallback: background-image wrapper (if not already wrapped)
            if (css != null && !string.IsNullOrEmpty(css.BackgroundImageUrl) && !(content is Border) && !(content is Panel))
            {
                try
                {
                    var imgBrush = RendererStyles.TryMakeImageBrushFromUrl(css.BackgroundImageUrl, css);
                    if (imgBrush != null)
                    {
                        var b = new Border { Child = content, Background = imgBrush };
                        b.Margin = content.Margin;
                        content.Margin = new Thickness(0);
                        content = b;
                    }
                }
                catch { /* swallow */ }
            }

            try { BrowserCore.Engine.JavaScriptEngine.RegisterDomVisual(n, content); } catch { /* swallow */ }

            // Apply box-sizing aware layout
            try { ApplyComputedLayout(content, css); } catch { /* swallow */ }

            // Apply CSS transform (translate, scale, rotate)
            if (css != null && !string.IsNullOrEmpty(css.Transform))
            {
                try { ApplyTransform(content, css.Transform); } catch { /* swallow */ }
            }

            // Minimal event bridge to JS for elements with explicit id
            string id = null;
            if (Js != null && n?.Attr != null && n.Attr.TryGetValue("id", out id) && !string.IsNullOrWhiteSpace(id))
            {
                // Taps -> "click"
                content.Tapped += (s, e) =>
                {
                    try
                    {
                        var pos = e.GetPosition(content);
                        // Raise synchronously so we can honor preventDefault
                        bool cancel = false;
                        try { cancel = Js.RaiseElementEventSync(id, "click", null, null, pos.X, pos.Y); } catch { cancel = false; }
                        if (cancel) { TrySetHandled(e); return; }
                    }
                    catch { /* swallow */ }
                };

                // Text input -> "input"
                var tb = content as TextBox;
                if (tb != null)
                {
                    tb.TextChanged += (s, e) => { try { Js.RaiseElementEvent(id, "input", tb.Text ?? ""); } catch { /* swallow */ } };
                    tb.LostFocus += (s, e) => { try { Js.RaiseElementEvent(id, "change", tb.Text ?? ""); } catch { /* swallow */ } };
                }

                // Combo / Listbox / CheckBox -> "change"
                var combo = content as ComboBox;
                if (combo != null) combo.SelectionChanged += (s, e) =>
                {
                    try
                    {
                        var it = combo.SelectedItem;
                        string txt = "";
                        var feIt = it as FrameworkElement;
                        if (feIt != null)
                        {
                            var cc = feIt as ContentControl;
                            if (cc != null)
                            {
                                var cs = cc.Content as string;
                                txt = cs ?? (cc.Content != null ? cc.Content.ToString() : "");
                            }
                            else
                            {
                                txt = feIt.ToString();
                            }
                        }
                        else if (it != null)
                        {
                            txt = it.ToString();
                        }
                        Js.RaiseElementEvent(id, "change", txt);
                    }
                    catch { try { Js.RaiseElementEventById(id, "change"); } catch { /* swallow */ } }
                };
                var list = content as ListBox;
                if (list != null) list.SelectionChanged += (s, e) =>
                {
                    try
                    {
                        var it = list.SelectedItem;
                        string txt = "";
                        var feIt = it as FrameworkElement;
                        if (feIt != null)
                        {
                            var cc = feIt as ContentControl;
                            if (cc != null)
                            {
                                var cs = cc.Content as string;
                                txt = cs ?? (cc.Content != null ? cc.Content.ToString() : "");
                            }
                            else
                            {
                                txt = feIt.ToString();
                            }
                        }
                        else if (it != null)
                        {
                            txt = it.ToString();
                        }
                        Js.RaiseElementEvent(id, "change", txt);
                    }
                    catch { try { Js.RaiseElementEventById(id, "change"); } catch { /* swallow */ } }
                };
                var chk = content as CheckBox;
                if (chk != null) chk.Click += (s, e) => { try { Js.RaiseElementEvent(id, "change", null, chk.IsChecked == true); } catch { try { Js.RaiseElementEventById(id, "change"); } catch { /* swallow */ } } };
            }

            // Inline on* attribute handlers (onclick/oninput/onchange)
            if (Js != null && n?.Attr != null)
            {
                string on;
                // onclick -> Tapped/Click
                if (n.Attr.TryGetValue("onclick", out on) && !string.IsNullOrWhiteSpace(on))
                {
                    var btn = content as Button;
                    var hbtn = content as HyperlinkButton;
                    var code = PreprocessInlineHandler(on, id);
                    if (btn != null) btn.Click += (s, e) => { try { Js.RunInline(code, new JsContext { BaseUri = _baseUriForResources }, "click", id); } catch { /* swallow */ } };
                    else if (hbtn != null) hbtn.Click += (s, e) => { try { Js.RunInline(code, new JsContext { BaseUri = _baseUriForResources }, "click", id); } catch { /* swallow */ } };
                    else content.Tapped += (s, e) => { try { Js.RunInline(code, new JsContext { BaseUri = _baseUriForResources }, "click", id); } catch { /* swallow */ } };
                }

                // oninput -> TextChanged for TextBox
                if (n.Attr.TryGetValue("oninput", out on) && !string.IsNullOrWhiteSpace(on))
                {
                    var tb = content as TextBox;
                    var code = PreprocessInlineHandler(on, id);
                    if (tb != null) tb.TextChanged += (s, e) => { try { Js.RunInline(code, new JsContext { BaseUri = _baseUriForResources }, "input", id); } catch { /* swallow */ } };
                }

                // onchange -> LostFocus for TextBox; SelectionChanged for ComboBox/ListBox; Click for CheckBox/Radio
                if (n.Attr.TryGetValue("onchange", out on) && !string.IsNullOrWhiteSpace(on))
                {
                    var code = PreprocessInlineHandler(on, id);
                    var tb = content as TextBox; if (tb != null) tb.LostFocus += (s, e) => { try { Js.RunInline(code, new JsContext { BaseUri = _baseUriForResources }, "change", id); } catch { /* swallow */ } };
                    var combo = content as ComboBox; if (combo != null) combo.SelectionChanged += (s, e) => { try { Js.RunInline(code, new JsContext { BaseUri = _baseUriForResources }, "change", id); } catch { /* swallow */ } };
                    var list = content as ListBox; if (list != null) list.SelectionChanged += (s, e) => { try { Js.RunInline(code, new JsContext { BaseUri = _baseUriForResources }, "change", id); } catch { /* swallow */ } };
                    var chk = content as CheckBox; if (chk != null) chk.Click += (s, e) => { try { Js.RunInline(code, new JsContext { BaseUri = _baseUriForResources }, "change", id); } catch { /* swallow */ } };
                }
            }

            return content;
        }

        private static string PreprocessInlineHandler(string code, string id)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(id)) return code;
            try
            {
                // Replace leading 'this.' tokens with document.getElementById('id').
                return Regex.Replace(code, @"\bthis\.", "document.getElementById('" + id.Replace("'", "\\'") + "').");
            }
            catch { return code; }
        }

        private void ApplyComputedStyles(FrameworkElement fe, LiteElement n)
        {
            if (ComputedStyles == null || n == null) return;
            CssComputed st = null;
            if (!ComputedStyles.TryGetValue(n, out st) || st == null)
            {
                try
                {
                    var byTag = ComputedStyles.Keys.FirstOrDefault(k => k != null && string.Equals(k.Tag, n.Tag, StringComparison.OrdinalIgnoreCase));
                    if (byTag != null) ComputedStyles.TryGetValue(byTag, out st);
                }
                catch { /* swallow */ }
                if (st == null) return;
            }

            try
            {
                if (!string.IsNullOrEmpty(st.Display) && string.Equals(st.Display, "none", StringComparison.OrdinalIgnoreCase))
                {
                    fe.Visibility = Visibility.Collapsed;
                    return;
                }
                if (!string.IsNullOrEmpty(st.Visibility) && string.Equals(st.Visibility, "hidden", StringComparison.OrdinalIgnoreCase))
                {
                    fe.Opacity = 0;
                }
                else if (!string.IsNullOrEmpty(st.Visibility) && string.Equals(st.Visibility, "collapse", StringComparison.OrdinalIgnoreCase))
                {
                    fe.Visibility = Visibility.Collapsed;
                }
            }
            catch { /* swallow */ }

            if (st.Foreground != null)
            {
                var c1 = fe as Control;
                var tb = fe as TextBlock;
                if (c1 != null) c1.Foreground = st.Foreground;
                else if (tb != null) tb.Foreground = st.Foreground;
            }
            if (st.FontSize.HasValue)
            {
                double px = st.FontSize.Value;
                var c2 = fe as Control;
                var tb2 = fe as TextBlock;
                if (c2 != null) c2.FontSize = px;
                else if (tb2 != null) tb2.FontSize = px;
            }
            if (st.FontWeight.HasValue)
            {
                var fw = st.FontWeight.Value;
                var c3 = fe as Control;
                var tb3 = fe as TextBlock;
                if (c3 != null) c3.FontWeight = fw;
                else if (tb3 != null) tb3.FontWeight = fw;
            }
            if (st.TextAlign.HasValue)
            {
                var tb = fe as TextBlock;
                if (tb != null) tb.TextAlignment = st.TextAlign.Value;
            }
            if (st.Background != null)
            {
                var border = fe as Border;
                if (border != null) border.Background = st.Background;
                else if (fe is Panel) ((Panel)fe).Background = st.Background;
            }
            if (st.Opacity.HasValue)
            {
                fe.Opacity = st.Opacity.Value;
            }

            // letter-spacing (UWP CharacterSpacing is in 1/1000 em units)
            if (st.LetterSpacing.HasValue)
            {
                var tb = fe as TextBlock;
                var c = fe as Control;
                if (tb != null)
                {
                    double fontSize = st.FontSize ?? 16.0;
                    tb.CharacterSpacing = (int)(st.LetterSpacing.Value / fontSize * 1000.0);
                }
                else if (c != null)
                {
                    double fontSize = st.FontSize ?? 16.0;
                    c.CharacterSpacing = (int)(st.LetterSpacing.Value / fontSize * 1000.0);
                }
            }

            // word-spacing (approximate via CharacterSpacing since UWP lacks direct word-spacing)
            if (st.WordSpacing.HasValue)
            {
                var tb = fe as TextBlock;
                if (tb != null)
                {
                    double fontSize = st.FontSize ?? 16.0;
                    int wordSpacingUnits = (int)(st.WordSpacing.Value / fontSize * 1000.0);
                    tb.CharacterSpacing = tb.CharacterSpacing + wordSpacingUnits;
                }
            }

            // line-height
            if (st.LineHeight.HasValue && st.LineHeight.Value > 0)
            {
                var tb = fe as TextBlock;
                if (tb != null)
                {
                    tb.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                    tb.LineHeight = st.LineHeight.Value;
                }
            }

            // text-transform
            if (!string.IsNullOrEmpty(st.TextTransform))
            {
                var tb = fe as TextBlock;
                if (tb != null)
                {
                    var text = tb.Text;
                    if (!string.IsNullOrEmpty(text))
                    {
                        var tt = st.TextTransform.ToLowerInvariant();
                        if (tt == "uppercase") tb.Text = text.ToUpperInvariant();
                        else if (tt == "lowercase") tb.Text = text.ToLowerInvariant();
                        else if (tt == "capitalize") tb.Text = CapitalizeWords(text);
                    }
                }
            }

            // text-indent (apply as left padding)
            if (st.TextIndent.HasValue)
            {
                var tb = fe as TextBlock;
                if (tb != null)
                {
                    var pad = tb.Padding;
                    tb.Padding = new Thickness(pad.Left + st.TextIndent.Value, pad.Top, pad.Right, pad.Bottom);
                }
            }

            // pointer-events
            if (!string.IsNullOrEmpty(st.PointerEvents))
            {
                if (st.PointerEvents.ToLowerInvariant() == "none")
                {
                    fe.IsHitTestVisible = false;
                }
                else
                {
                    fe.IsHitTestVisible = true;
                }
            }

            // cursor (set on PointerEntered/Exited)
            if (!string.IsNullOrEmpty(st.Cursor))
            {
                var cursorType = ParseCursorType(st.Cursor);
                if (cursorType.HasValue)
                {
                    fe.PointerEntered += (s, e) =>
                    {
                        try { Windows.UI.Core.CoreWindow.GetForCurrentThread().PointerCursor = new Windows.UI.Core.CoreCursor(cursorType.Value, 1); }
                        catch { }
                    };
                    fe.PointerExited += (s, e) =>
                    {
                        try { Windows.UI.Core.CoreWindow.GetForCurrentThread().PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.Arrow, 1); }
                        catch { }
                    };
                }
            }

            // transform-origin
            if (!string.IsNullOrEmpty(st.TransformOrigin))
            {
                var origin = ParseTransformOrigin(st.TransformOrigin);
                if (origin.HasValue)
                {
                    fe.RenderTransformOrigin = origin.Value;
                }
            }

            // Phase C.6: attach :hover → transition animation handlers when
            // (a) the element has a :hover override computed in CssLoader
            // (b) the base computed style has a transition with a positive
            //     duration. Without (b) there's no animation to play and the
            // hover state would just snap.
            try
            {
                if (st.Hover != null && st.TransitionDurationMs > 0)
                {
                    AttachHoverTransition(fe, st);
                }
            }
            catch { /* swallow */ }
        }

        // Phase C.6: wire PointerEntered/Exited to drive Storyboard animations
        // between base and hover computed values. Supports multi-property
        // comma-separated transitions via TransitionList.
        private void AttachHoverTransition(FrameworkElement fe, CssComputed baseCss)
        {
            if (fe == null || baseCss == null || baseCss.Hover == null) return;
            var hover = baseCss.Hover;

            // Build transition spec list — use TransitionList if available,
            // otherwise fall back to single-entry from old fields.
            var specs = BuildTransitionSpecs(baseCss);
            if (specs.Count == 0) return;

            // Capture base values BEFORE we attach any handlers, so we can
            // return to them on PointerExited.
            double baseOpacity = fe.Opacity;
            Windows.UI.Color baseColor = GetBackgroundColor(fe) ?? Windows.UI.Colors.Transparent;
            bool hasBaseBrush = GetBackgroundBrush(fe) != null;

            double baseScaleX = 1, baseScaleY = 1, baseRotate = 0, baseTx = 0, baseTy = 0;
            var group = fe.RenderTransform as Windows.UI.Xaml.Media.TransformGroup;
            if (group != null && group.Children != null)
            {
                for (int i = 0; i < group.Children.Count; i++)
                {
                    var tr = group.Children[i];
                    if (tr is Windows.UI.Xaml.Media.ScaleTransform sc) { baseScaleX = sc.ScaleX; baseScaleY = sc.ScaleY; }
                    else if (tr is Windows.UI.Xaml.Media.RotateTransform rt) { baseRotate = rt.Angle; }
                    else if (tr is Windows.UI.Xaml.Media.TranslateTransform tt) { baseTx = tt.X; baseTy = tt.Y; }
                }
            }

            // Pre-compute hover values from the hover computed style.
            double? hoverOpacity = null;
            Windows.UI.Color? hoverColor = null;
            double? hoverScaleX = null, hoverScaleY = null;
            double? hoverRotate = null;
            double? hoverTx = null, hoverTy = null;

            // Check what hover actually overrides
            var oStr = DictGet(hover.Map, "opacity");
            double ov = 0;
            bool opacityChanges = !string.IsNullOrWhiteSpace(oStr) && TryPxOrDouble(oStr, out ov);

            var cStr = ExtractBackgroundColor(hover.Map);
            bool colorChanges = !string.IsNullOrWhiteSpace(cStr);
            Windows.UI.Xaml.Media.SolidColorBrush hoverBrush = null;
            if (colorChanges) hoverBrush = TryParseCssColorToBrush(cStr);

            var tStr = DictGet(hover.Map, "transform");
            bool transformChanges = !string.IsNullOrWhiteSpace(tStr);

            // Only attach if there's at least one thing to animate
            if (!opacityChanges && !colorChanges && !transformChanges) return;

            // Resolve which spec applies to each property
            TransitionSpecForProp("opacity", specs, out double opacityDur, out double opacityDelay, out string opacityTiming);
            TransitionSpecForProp("background-color", specs, out double bgDur, out double bgDelay, out string bgTiming);
            TransitionSpecForProp("transform", specs, out double xfDur, out double xfDelay, out string xfTiming);

            fe.PointerEntered += (s, e) =>
            {
                try
                {
                    if (opacityChanges)
                    {
                        hoverOpacity = Math.Max(0, Math.Min(1, ov));
                        TransitionAnimator.AnimateOpacity(fe, hoverOpacity.Value, opacityDur, opacityDelay, opacityTiming);
                    }
                    if (colorChanges && hoverBrush != null)
                    {
                        hoverColor = hoverBrush.Color;
                        if (hasBaseBrush)
                            TransitionAnimator.AnimateBackgroundColor(fe, hoverColor.Value, bgDur, bgDelay, bgTiming);
                        else
                        {
                            // Smooth background: set Transparent base brush first, then animate
                            SetBackgroundBrush(fe, new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Transparent));
                            TransitionAnimator.AnimateBackgroundColor(fe, hoverColor.Value, bgDur, bgDelay, bgTiming);
                        }
                    }
                    if (transformChanges)
                    {
                        // Parse with actual element dimensions (supports % in translate)
                        double elemW = 0, elemH = 0;
                        try { elemW = fe.ActualWidth; elemH = fe.ActualHeight; } catch { }
                        ParseTransformForHover(tStr, out double hSx, out double hSy, out double hRot, out double hTx, out double hTy, elemW, elemH);
                        hoverScaleX = hSx; hoverScaleY = hSy; hoverRotate = hRot; hoverTx = hTx; hoverTy = hTy;
                        if (hoverScaleX.HasValue || hoverScaleY.HasValue)
                            TransitionAnimator.AnimateTransformScale(fe,
                                hoverScaleX ?? baseScaleX, hoverScaleY ?? baseScaleY, xfDur, xfDelay, xfTiming);
                        if (hoverRotate.HasValue)
                            TransitionAnimator.AnimateTransformRotate(fe, hoverRotate.Value, xfDur, xfDelay, xfTiming);
                        if (hoverTx.HasValue || hoverTy.HasValue)
                            TransitionAnimator.AnimateTransformTranslate(fe,
                                hoverTx ?? baseTx, hoverTy ?? baseTy, xfDur, xfDelay, xfTiming);
                    }
                }
                catch { }
            };

            fe.PointerExited += (s, e) =>
            {
                try
                {
                    if (opacityChanges)
                        TransitionAnimator.AnimateOpacity(fe, baseOpacity, opacityDur, opacityDelay, opacityTiming);
                    if (colorChanges && hoverBrush != null)
                    {
                        if (hasBaseBrush)
                            TransitionAnimator.AnimateBackgroundColor(fe, baseColor, bgDur, bgDelay, bgTiming);
                        else
                            TransitionAnimator.AnimateBackgroundColor(fe, Windows.UI.Colors.Transparent, bgDur, bgDelay, bgTiming);
                    }
                    if (transformChanges)
                    {
                        if (hoverScaleX.HasValue || hoverScaleY.HasValue)
                            TransitionAnimator.AnimateTransformScale(fe, baseScaleX, baseScaleY, xfDur, xfDelay, xfTiming);
                        if (hoverRotate.HasValue)
                            TransitionAnimator.AnimateTransformRotate(fe, baseRotate, xfDur, xfDelay, xfTiming);
                        if (hoverTx.HasValue || hoverTy.HasValue)
                            TransitionAnimator.AnimateTransformTranslate(fe, baseTx, baseTy, xfDur, xfDelay, xfTiming);
                    }
                }
                catch { }
            };
        }

        // Build flat transition spec list from CssComputed (TransitionList or fallback).
        private static List<TransitionSpecForAnim> BuildTransitionSpecs(CssComputed css)
        {
            var result = new List<TransitionSpecForAnim>();
            if (css.TransitionList != null && css.TransitionList.Count > 0)
            {
                foreach (var s in css.TransitionList)
                    result.Add(new TransitionSpecForAnim(s.Property, s.DurationMs, s.TimingFunction, s.DelayMs));
            }
            else if (css.TransitionDurationMs > 0)
            {
                result.Add(new TransitionSpecForAnim(
                    css.TransitionProperty ?? "all",
                    css.TransitionDurationMs,
                    css.TransitionTimingFunction ?? "ease",
                    css.TransitionDelayMs));
            }
            return result;
        }

        // For a given CSS property name, find the first matching transition spec
        // (exact match, or "all" catch-all). Sets out parameters; if no match,
        // duration stays 0 (no animation).
        private static void TransitionSpecForProp(string cssProp, List<TransitionSpecForAnim> specs,
            out double durMs, out double delayMs, out string timing)
        {
            durMs = 0; delayMs = 0; timing = "ease";
            if (specs == null) return;
            // First exact match
            for (int i = 0; i < specs.Count; i++)
            {
                var s = specs[i];
                if (string.Equals(s.Prop, cssProp, StringComparison.OrdinalIgnoreCase))
                { durMs = s.DurationMs; delayMs = s.DelayMs; timing = s.Timing; return; }
            }
            // Fallback to "all"
            for (int i = 0; i < specs.Count; i++)
            {
                var s = specs[i];
                if (string.Equals(s.Prop, "all", StringComparison.OrdinalIgnoreCase))
                { durMs = s.DurationMs; delayMs = s.DelayMs; timing = s.Timing; return; }
            }
        }

        // Lightweight struct for transition spec data
        private sealed class TransitionSpecForAnim
        {
            public string Prop;
            public double DurationMs;
            public string Timing;
            public double DelayMs;
            public TransitionSpecForAnim(string p, double d, string t, double dl)
            { Prop = p; DurationMs = d; Timing = t; DelayMs = dl; }
        }

        // Phase C.6: small set of helpers to read/write the Background property
        // across UWP types that expose it (Control, Panel, Border, ContentPresenter).
        // FrameworkElement itself has no Background, so we must dispatch.
        private static Windows.UI.Xaml.Media.Brush GetBackgroundBrush(FrameworkElement fe)
        {
            if (fe is Windows.UI.Xaml.Controls.Control c) return c.Background;
            if (fe is Windows.UI.Xaml.Controls.Panel p) return p.Background;
            if (fe is Windows.UI.Xaml.Controls.Border b) return b.Background;
            if (fe is Windows.UI.Xaml.Controls.ContentPresenter cp) return cp.Background;
            return null;
        }

        private static void SetBackgroundBrush(FrameworkElement fe, Windows.UI.Xaml.Media.Brush brush)
        {
            if (fe is Windows.UI.Xaml.Controls.Control c) c.Background = brush;
            else if (fe is Windows.UI.Xaml.Controls.Panel p) p.Background = brush;
            else if (fe is Windows.UI.Xaml.Controls.Border b) b.Background = brush;
            else if (fe is Windows.UI.Xaml.Controls.ContentPresenter cp) cp.Background = brush;
        }

        private static Windows.UI.Color? GetBackgroundColor(FrameworkElement fe)
        {
            var br = GetBackgroundBrush(fe) as Windows.UI.Xaml.Media.SolidColorBrush;
            if (br != null) return br.Color;
            return null;
        }

        // Try to read a value as a number (with optional unit). For opacity we
        // just want 0..1 doubles.
        private static bool TryPxOrDouble(string s, out double v)
        {
            v = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            // Strip "px" if present
            if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - 2).Trim();
            return double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v);
        }

        // Parse a CSS color into a SolidColorBrush for hover background.
        private static Windows.UI.Xaml.Media.SolidColorBrush TryParseCssColorToBrush(string css)
        {
            if (string.IsNullOrWhiteSpace(css)) return null;
            try
            {
                var s = css.Trim();
                byte a = 0xFF, r = 0, g = 0, b = 0;
                if (s.StartsWith("#"))
                {
                    s = s.Substring(1);
                    if (s.Length == 3)
                    {
                        r = Convert.ToByte(new string(s[0], 2), 16);
                        g = Convert.ToByte(new string(s[1], 2), 16);
                        b = Convert.ToByte(new string(s[2], 2), 16);
                    }
                    else if (s.Length == 6)
                    {
                        r = Convert.ToByte(s.Substring(0, 2), 16);
                        g = Convert.ToByte(s.Substring(2, 2), 16);
                        b = Convert.ToByte(s.Substring(4, 2), 16);
                    }
                    else if (s.Length == 8)
                    {
                        a = Convert.ToByte(s.Substring(0, 2), 16);
                        r = Convert.ToByte(s.Substring(2, 2), 16);
                        g = Convert.ToByte(s.Substring(4, 2), 16);
                        b = Convert.ToByte(s.Substring(6, 2), 16);
                    }
                    return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
                }
                var l = s.ToLowerInvariant();
                if (l == "black") return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Black);
                if (l == "white") return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White);
                if (l == "red") return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Red);
                if (l == "green") return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Green);
                if (l == "blue") return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Blue);
                if (l == "gray" || l == "grey") return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Gray);
                if (l == "purple") return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Purple);
                if (l == "orange") return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Orange);
                if (l == "yellow") return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Yellow);
                if (l == "crimson") return new Windows.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0xFF, 0xDC, 0x14, 0x3C));
                if (l == "transparent") return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Transparent);
            }
            catch { }
            return null;
        }

        // Parse a transform list like "scale(1.1) rotate(15deg) translate(10px,20px)"
        // and pull out the values used by TransitionAnimator.
        private static void ParseTransformForHover(string t, out double sx, out double sy, out double rot, out double tx, out double ty,
            double elemW = 0, double elemH = 0)
        {
            sx = 1; sy = 1; rot = 0; tx = 0; ty = 0;
            if (string.IsNullOrWhiteSpace(t)) return;
            var rx = new System.Text.RegularExpressions.Regex(@"(?<fn>[a-zA-Z]+)\s*\((?<args>[^)]*)\)");
            foreach (System.Text.RegularExpressions.Match mm in rx.Matches(t))
            {
                var fn = (mm.Groups["fn"].Value ?? "").ToLowerInvariant();
                var args = (mm.Groups["args"].Value ?? "").Trim();
                var parts = args.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (fn == "scale")
                {
                    if (parts.Length >= 1) double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sx);
                    if (parts.Length >= 2) double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sy);
                    else sy = sx;
                }
                else if (fn == "rotate")
                {
                    if (parts.Length >= 1)
                    {
                        var a = parts[0].Trim();
                        if (a.EndsWith("deg", StringComparison.OrdinalIgnoreCase)) a = a.Substring(0, a.Length - 3);
                        double.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rot);
                    }
                }
                else if (fn == "translate")
                {
                    if (parts.Length >= 1)
                    {
                        var a = parts[0].Trim();
                        if (a.EndsWith("%", StringComparison.Ordinal) && elemW > 0)
                        {
                            double p; if (double.TryParse(a.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out p))
                                tx = (p / 100.0) * elemW;
                        }
                        else
                        {
                            if (a.EndsWith("px", StringComparison.OrdinalIgnoreCase)) a = a.Substring(0, a.Length - 2);
                            double.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out tx);
                        }
                    }
                    if (parts.Length >= 2)
                    {
                        var b = parts[1].Trim();
                        if (b.EndsWith("%", StringComparison.Ordinal) && elemH > 0)
                        {
                            double p; if (double.TryParse(b.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out p))
                                ty = (p / 100.0) * elemH;
                        }
                        else
                        {
                            if (b.EndsWith("px", StringComparison.OrdinalIgnoreCase)) b = b.Substring(0, b.Length - 2);
                            double.TryParse(b, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ty);
                        }
                    }
                }
            }
        }

        // Forward to css.Map["background"] || css.Map["background-color"], with
        // a best-effort strip of "url(...)" / position keywords so we can get
        // the color out of a "background: red url(...)" shorthand.
        private static string ExtractBackgroundColor(Dictionary<string, string> map)
        {
            if (map == null) return null;
            string v;
            if (map.TryGetValue("background-color", out v) && !string.IsNullOrWhiteSpace(v)) return v;
            if (map.TryGetValue("background", out v) && !string.IsNullOrWhiteSpace(v))
            {
                // Take the first token that looks like a color (starts with #, rgb(, or is a known name)
                foreach (var part in v.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (part.StartsWith("#") || part.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
                        return part;
                }
            }
            return null;
        }

        private static string DictGet(Dictionary<string, string> d, string key)
        {
            if (d == null) return null;
            string v;
            return d.TryGetValue(key, out v) ? v : null;
        }

        private static void ApplyInlineStyles(FrameworkElement fe, LiteElement n)
        {
            if (n?.Attr == null) return;
            string style;
            if (!n.Attr.TryGetValue("style", out style) || string.IsNullOrWhiteSpace(style)) return;

            var kv = ParseInlineStyle(style);
            foreach (var pair in kv)
            {
                var key = pair.Key;
                var val = pair.Value;

                if (key == "color")
                {
                    var brush = TryBrush(val);
                    var c1 = fe as Control;
                    var tb = fe as TextBlock;
                    if (c1 != null && brush != null) c1.Foreground = brush;
                    else if (tb != null && brush != null) tb.Foreground = brush;
                }
                else if (key == "font-size")
                {
                    double px;
                    if (TryPx(val, out px))
                    {
                        var c2 = fe as Control;
                        var tb2 = fe as TextBlock;
                        if (c2 != null) c2.FontSize = px;
                        else if (tb2 != null) tb2.FontSize = px;
                    }
                }
                else if (key == "font-weight")
                {
                    bool bold = string.Equals((val ?? "").Trim(), "bold", StringComparison.OrdinalIgnoreCase);
                    var c3 = fe as Control;
                    var tb3 = fe as TextBlock;
                    if (c3 != null && bold) c3.FontWeight = FontWeights.Bold;
                    else if (tb3 != null && bold) tb3.FontWeight = FontWeights.Bold;
                }
                else if (key == "font-style")
                {
                    ApplyFontStyle(fe, val);
                }
                else if (key == "margin")
                {
                    Thickness th;
                    if (TryThickness(val, out th)) fe.Margin = th;
                }
                else if (key == "margin-top")
                {
                    double mt;
                    if (TryPx(val, out mt)) fe.Margin = new Thickness(fe.Margin.Left, mt, fe.Margin.Right, fe.Margin.Bottom);
                }
                else if (key == "margin-bottom")
                {
                    double mb;
                    if (TryPx(val, out mb)) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top, fe.Margin.Right, mb);
                }
                else if (key == "margin-left")
                {
                    double ml;
                    if (TryPx(val, out ml)) fe.Margin = new Thickness(ml, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
                }
                else if (key == "margin-right")
                {
                    double mr;
                    if (TryPx(val, out mr)) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top, mr, fe.Margin.Bottom);
                }
                else if (key == "padding")
                {
                    Thickness th;
                    if (TryThickness(val, out th))
                    {
                        var ctrl = fe as Control;
                        if (ctrl != null) ctrl.Padding = th;
                        else
                        {
                            var border = fe as Border;
                            if (border != null) border.Padding = th;
                        }
                    }
                }
                else if (key == "padding-top")
                {
                    double pt;
                    if (TryPx(val, out pt))
                    {
                        var ctrl = fe as Control;
                        if (ctrl != null)
                        {
                            var pad = ctrl.Padding;
                            ctrl.Padding = new Thickness(pad.Left, pt, pad.Right, pad.Bottom);
                        }
                        else
                        {
                            var border = fe as Border;
                            if (border != null)
                            {
                                var pad = border.Padding;
                                border.Padding = new Thickness(pad.Left, pt, pad.Right, pad.Bottom);
                            }
                        }
                    }
                }
                else if (key == "padding-bottom")
                {
                    double pb;
                    if (TryPx(val, out pb))
                    {
                        var ctrl = fe as Control;
                        if (ctrl != null)
                        {
                            var pad = ctrl.Padding;
                            ctrl.Padding = new Thickness(pad.Left, pad.Top, pad.Right, pb);
                        }
                        else
                        {
                            var border = fe as Border;
                            if (border != null)
                            {
                                var pad = border.Padding;
                                border.Padding = new Thickness(pad.Left, pad.Top, pad.Right, pb);
                            }
                        }
                    }
                }
                else if (key == "padding-left")
                {
                    double pl;
                    if (TryPx(val, out pl))
                    {
                        var ctrl = fe as Control;
                        if (ctrl != null)
                        {
                            var pad = ctrl.Padding;
                            ctrl.Padding = new Thickness(pl, pad.Top, pad.Right, pad.Bottom);
                        }
                        else
                        {
                            var border = fe as Border;
                            if (border != null)
                            {
                                var pad = border.Padding;
                                border.Padding = new Thickness(pl, pad.Top, pad.Right, pad.Bottom);
                            }
                        }
                    }
                }
                else if (key == "padding-right")
                {
                    double pr;
                    if (TryPx(val, out pr))
                    {
                        var ctrl = fe as Control;
                        if (ctrl != null)
                        {
                            var pad = ctrl.Padding;
                            ctrl.Padding = new Thickness(pad.Left, pad.Top, pr, pad.Bottom);
                        }
                        else
                        {
                            var border = fe as Border;
                            if (border != null)
                            {
                                var pad = border.Padding;
                                border.Padding = new Thickness(pad.Left, pad.Top, pr, pad.Bottom);
                            }
                        }
                    }
                }
                else if (key == "text-align")
                {
                    var tb = fe as TextBlock;
                    if (tb != null)
                    {
                        var v = (val ?? "").Trim().ToLowerInvariant();
                        if (v == "center") tb.TextAlignment = TextAlignment.Center;
                        else if (v == "right") tb.TextAlignment = TextAlignment.Right;
                        else if (v == "justify") tb.TextAlignment = TextAlignment.Justify;
                        else tb.TextAlignment = TextAlignment.Left;
                    }
                }
                else if (key == "background" || key == "background-color")
                {
                    var brush = TryBrush(val);
                    var border = fe as Border;
                    if (border != null && brush != null) border.Background = brush;
                    else if (fe is Panel && brush != null) ((Panel)fe).Background = brush;
                }
                else if (key == "line-height")
                {
                    var tb = fe as TextBlock;
                    double px;
                    if (tb != null && TryPx(val, out px)) { tb.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; tb.LineHeight = px; }
                }
                else if (key == "text-transform")
                {
                    var tb = fe as TextBlock;
                    if (tb != null)
                    {
                        var v = (val ?? "").Trim().ToLowerInvariant();
                        var t = tb.Text ?? "";

                        if (v == "uppercase") tb.Text = t.ToUpperInvariant();
                        else if (v == "lowercase") tb.Text = t.ToLowerInvariant();
                        else if (v == "capitalize") tb.Text = CapitalizeWords(t);
                    }
                }
                else if (key == "text-decoration")
                {
                    ApplyTextDecorations(fe, val);
                }
                else if (key == "letter-spacing")
                {
                    ApplyLetterSpacing(fe, val);
                }
                else if (key == "opacity")
                {
                    ApplyOpacity(fe, val);
                }
                else if (key == "display")
                {
                    var v = (val ?? "").Trim().ToLowerInvariant();
                    if (v == "none") fe.Visibility = Visibility.Collapsed;
                }
                else if (key == "border")
                {
                    ApplyBorderShorthand(fe, val);
                }
                else if (key == "border-color")
                {
                    var brush = TryBrush(val);
                    if (brush != null)
                    {
                        var border = fe as Border;
                        if (border != null) border.BorderBrush = brush;
                        var ctrl = fe as Control;
                        if (ctrl != null) ctrl.BorderBrush = brush;
                    }
                }
                else if (key == "border-width")
                {
                    Thickness th;
                    if (TryThickness(val, out th))
                    {
                        var border = fe as Border;
                        if (border != null) border.BorderThickness = th;
                        var ctrl = fe as Control;
                        if (ctrl != null) ctrl.BorderThickness = th;
                    }
                }
                else if (key == "border-style")
                {
                    var v = (val ?? "").Trim().ToLowerInvariant();
                    if (v == "none" || v == "hidden")
                    {
                        var border = fe as Border;
                        if (border != null) border.BorderThickness = new Thickness(0, 0, 0, 0);
                        var ctrl = fe as Control;
                        if (ctrl != null) ctrl.BorderThickness = new Thickness(0, 0, 0, 0);
                    }
                }
                else if (key == "border-radius")
                {
                    var border = fe as Border;
                    if (border != null)
                    {
                        CornerRadius cr;
                        if (TryCornerRadius(val, out cr)) border.CornerRadius = cr;
                    }
                }
                else if (key == "border-top-left-radius")
                {
                    var border = fe as Border;
                    if (border != null)
                    {
                        double px;
                        if (TryPx(val, out px))
                        {
                            var cr = border.CornerRadius;
                            cr.TopLeft = px;
                            border.CornerRadius = cr;
                        }
                    }
                }
                else if (key == "border-top-right-radius")
                {
                    var border = fe as Border;
                    if (border != null)
                    {
                        double px;
                        if (TryPx(val, out px))
                        {
                            var cr = border.CornerRadius;
                            cr.TopRight = px;
                            border.CornerRadius = cr;
                        }
                    }
                }
                else if (key == "border-bottom-right-radius")
                {
                    var border = fe as Border;
                    if (border != null)
                    {
                        double px;
                        if (TryPx(val, out px))
                        {
                            var cr = border.CornerRadius;
                            cr.BottomRight = px;
                            border.CornerRadius = cr;
                        }
                    }
                }
                else if (key == "border-bottom-left-radius")
                {
                    var border = fe as Border;
                    if (border != null)
                    {
                        double px;
                        if (TryPx(val, out px))
                        {
                            var cr = border.CornerRadius;
                            cr.BottomLeft = px;
                            border.CornerRadius = cr;
                        }
                    }
                }
                else if (key == "background-image")
                {
                    var m = Regex.Match(val ?? "", @"url\(['""]?(?<u>[^)'""]+)['""]?\)", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        // Placeholder for background image logic
                    }
                }
                else if (key == "text-overflow")
                {
                    var tb = fe as TextBlock;
                    if (tb != null)
                    {
                        var v = (val ?? "").Trim().ToLowerInvariant();
                        if (v == "ellipsis")
                        {
                            tb.TextTrimming = TextTrimming.CharacterEllipsis;
                            // Ensure single line for ellipsis to work properly
                            if (tb.TextWrapping == TextWrapping.NoWrap || tb.MaxLines == 1)
                            {
                                tb.MaxLines = 1;
                            }
                        }
                        else if (v == "clip")
                        {
                            tb.TextTrimming = TextTrimming.None;
                        }
                    }
                }
            }
        }

        private FrameworkElement ApplyInlineOverflow(FrameworkElement content, LiteElement n)
        {
            if (content == null || n == null) return content;

            string style = null;
            string ov = null, ovx = null, ovy = null;

            // From inline style attribute
            if (n.Attr != null && n.Attr.TryGetValue("style", out style) && !string.IsNullOrWhiteSpace(style))
            {
                var kv = ParseInlineStyle(style);
                kv.TryGetValue("overflow", out ov);
                kv.TryGetValue("overflow-x", out ovx);
                kv.TryGetValue("overflow-y", out ovy);
            }

            // Override from CssComputed if available
            var css = TryGetCss(n);
            if (css != null)
            {
                if (!string.IsNullOrEmpty(css.Overflow)) ov = css.Overflow;
                if (!string.IsNullOrEmpty(css.OverflowX)) ovx = css.OverflowX;
                if (!string.IsNullOrEmpty(css.OverflowY)) ovy = css.OverflowY;
            }

            Func<string, string> normalize = s => string.IsNullOrWhiteSpace(s) ? string.Empty : s.Trim().ToLowerInvariant();

            var overflow = normalize(ov);
            var overflowX = normalize(ovx);
            var overflowY = normalize(ovy);

            bool anyAxisScroll = (overflowX == "auto" || overflowX == "scroll") ||
                                 (overflowY == "auto" || overflowY == "scroll");

            if (overflow == "hidden" || (overflowX == "hidden" && !anyAxisScroll) || (overflowY == "hidden" && !anyAxisScroll))
            {
                Action<FrameworkElement> applyClip = fe =>
                {
                    if (fe == null) return;
                    try
                    {
                        var rect = new Rect(0, 0, fe.ActualWidth, fe.ActualHeight);
                        fe.Clip = new RectangleGeometry { Rect = rect };
                    }
                    catch { /* swallow */ }
                };

                var target = content;
                applyClip(target);
                target.SizeChanged += (s, e) => applyClip(s as FrameworkElement);
                return target;
            }
            else if (overflow == "auto" || overflow == "scroll" || anyAxisScroll)
            {
                ScrollViewer scroller;
                var existing = content as ScrollViewer;
                if (existing != null)
                {
                    scroller = existing;
                }
                else
                {
                    scroller = new ScrollViewer { Content = content };
                }

                scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;

                if (!string.IsNullOrEmpty(overflowX))
                {
                    if (overflowX == "hidden") scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    else if (overflowX == "scroll" || overflowX == "auto") scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                }

                if (!string.IsNullOrEmpty(overflowY))
                {
                    if (overflowY == "hidden") scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    else if (overflowY == "scroll" || overflowY == "auto") scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                }

                return scroller;
            }

            return content;
        }

        private static Windows.UI.Core.CoreCursorType? ParseCursorType(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var v = value.Trim().ToLowerInvariant();
            if (v == "pointer" || v == "hand") return Windows.UI.Core.CoreCursorType.Hand;
            if (v == "text" || v == "ibeam") return Windows.UI.Core.CoreCursorType.IBeam;
            if (v == "wait" || v == "progress" || v == "spinner") return Windows.UI.Core.CoreCursorType.Wait;
            if (v == "help") return Windows.UI.Core.CoreCursorType.Help;
            if (v == "crosshair") return Windows.UI.Core.CoreCursorType.Cross;
            if (v == "move") return Windows.UI.Core.CoreCursorType.SizeAll;
            if (v.Contains("resize-ew") || v == "ew-resize" || v == "col-resize") return Windows.UI.Core.CoreCursorType.SizeWestEast;
            if (v.Contains("resize-ns") || v == "ns-resize" || v == "row-resize") return Windows.UI.Core.CoreCursorType.SizeNorthSouth;
            if (v.Contains("ne-sw") || v == "nesw-resize") return Windows.UI.Core.CoreCursorType.SizeNortheastSouthwest;
            if (v.Contains("nw-se") || v == "nwse-resize") return Windows.UI.Core.CoreCursorType.SizeNorthwestSoutheast;
            if (v == "default" || v == "auto") return Windows.UI.Core.CoreCursorType.Arrow;
            return null;
        }

        private static string CapitalizeWords(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new System.Text.StringBuilder(text.Length);
            bool capitalizeNext = true;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || c == '-' || c == '\'')
                {
                    sb.Append(c);
                    capitalizeNext = true;
                }
                else if (capitalizeNext)
                {
                    sb.Append(char.ToUpperInvariant(c));
                    capitalizeNext = false;
                }
                else
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }
            return sb.ToString();
        }

        private static bool TryPx(string s, out double px)
        {
            px = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim().ToLowerInvariant();
            if (s.EndsWith("px")) s = s.Substring(0, s.Length - 2);
            double v;
            if (double.TryParse(s, out v)) { px = v; return true; }
            return false;
        }

        private static bool TryThickness(string s, out Thickness th)
        {
            th = new Thickness(0, 0, 0, 0);
            if (string.IsNullOrWhiteSpace(s)) return false;
            var parts = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 1)
            {
                double v; TryPx(parts[0], out v);
                th = new Thickness(v); return true;
            }
            if (parts.Length == 2)
            {
                double y; TryPx(parts[0], out y);
                double x; TryPx(parts[1], out x);
                th = new Thickness(x, y, x, y); return true;
            }
            if (parts.Length == 3)
            {
                double t; TryPx(parts[0], out t);
                double x; TryPx(parts[1], out x);
                double b; TryPx(parts[2], out b);
                th = new Thickness(x, t, x, b); return true;
            }
            if (parts.Length >= 4)
            {
                double top; TryPx(parts[0], out top);
                double right; TryPx(parts[1], out right);
                double bottom; TryPx(parts[2], out bottom);
                double left; TryPx(parts[3], out left);
                th = new Thickness(left, top, right, bottom); return true;
            }
            return false;
        }

        private static bool TryCornerRadius(string s, out CornerRadius cr)
        {
            cr = new CornerRadius(0);
            if (string.IsNullOrWhiteSpace(s)) return false;
            var parts = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            double tl;
            if (!TryPx(parts[0], out tl)) return false;

            double tr, br, bl;
            if (parts.Length == 1)
            {
                tr = br = bl = tl;
            }
            else if (parts.Length == 2)
            {
                if (!TryPx(parts[1], out tr)) return false;
                br = tl;
                bl = tr;
            }
            else if (parts.Length == 3)
            {
                if (!TryPx(parts[1], out tr)) return false;
                if (!TryPx(parts[2], out br)) return false;
                bl = tr;
            }
            else
            {
                if (!TryPx(parts[1], out tr)) return false;
                if (!TryPx(parts[2], out br)) return false;
                if (!TryPx(parts[3], out bl)) return false;
            }

            cr = new CornerRadius(tl, tr, br, bl);
            return true;
        }

        private static void ApplyTextDecorations(FrameworkElement fe, string value)
        {
            if (!SupportsTextDecorations || string.IsNullOrWhiteSpace(value)) return;
            var tb = fe as TextBlock;
            if (tb == null) return;

            var tokens = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return;

            bool underline = false;
            bool strike = false;
            bool explicitNone = false;

            foreach (var token in tokens)
            {
                var part = token.Trim().ToLowerInvariant();
                if (part == "none")
                {
                    explicitNone = true;
                    break;
                }
                if (part == "underline") underline = true;
                else if (part == "line-through" || part == "linethrough" || part == "strikethrough") strike = true;
            }

            int decoValue = TextDecorationsValueNone;

            if (explicitNone)
            {
                decoValue = TextDecorationsValueNone;
            }
            else
            {
                if (!underline && !strike) return;
                if (underline) decoValue |= TextDecorationsValueUnderline;
                if (strike) decoValue |= TextDecorationsValueStrikethrough;
            }

            var enumValue = Enum.ToObject(TextDecorationsType, decoValue);
            try { TextBlockTextDecorationsProp?.SetValue(tb, enumValue); } catch { /* swallow */ }
        }

        private static void ApplyLetterSpacing(FrameworkElement fe, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            double spacingValue;
            double fontSize = 0;

            var tb = fe as TextBlock;
            if (tb != null) fontSize = tb.FontSize;
            var ctrl = fe as Control;
            if (ctrl != null && fontSize <= 0) fontSize = ctrl.FontSize;
            if (fontSize <= 0) fontSize = 14;

            bool handled = false;
            var trimmed = value.Trim().ToLowerInvariant();
            if (TryPx(trimmed, out spacingValue))
            {
                spacingValue = (spacingValue / Math.Max(1, fontSize)) * 1000.0;
                handled = true;
            }
            else if (trimmed.EndsWith("em"))
            {
                double em;
                if (double.TryParse(trimmed.Substring(0, trimmed.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out em))
                {
                    spacingValue = em * 1000.0;
                    handled = true;
                }
            }
            else if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out spacingValue))
            {
                spacingValue = (spacingValue / Math.Max(1, fontSize)) * 1000.0;
                handled = true;
            }

            if (!handled) return;

            var characterSpacing = (int)Math.Round(spacingValue);
            if (tb != null) tb.CharacterSpacing = characterSpacing;
            if (ctrl != null) ctrl.CharacterSpacing = characterSpacing;
        }

        private static void ApplyFontStyle(FrameworkElement fe, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var normalized = value.Trim().ToLowerInvariant();
            var fontStyle = (normalized == "italic" || normalized == "oblique") ? FontStyle.Italic : FontStyle.Normal;

            var ctrl = fe as Control;
            if (ctrl != null) ctrl.FontStyle = fontStyle;
            var tb = fe as TextBlock;
            if (tb != null) tb.FontStyle = fontStyle;
        }

        private static void ApplyOpacity(FrameworkElement fe, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || fe == null) return;
            double parsed;
            if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return;
            if (parsed < 0) parsed = 0;
            if (parsed > 1) parsed = 1;
            fe.Opacity = parsed;
        }

        private static void ApplyBorderShorthand(FrameworkElement fe, string value)
        {
            if (fe == null || string.IsNullOrWhiteSpace(value)) return;
            var border = fe as Border;
            var ctrl = fe as Control;
            if (border == null && ctrl == null) return;

            double width = double.NaN;
            Brush brush = null;

            var tokens = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in tokens)
            {
                var token = raw.Trim();
                if (token.Length == 0) continue;

                double px;
                if (TryPx(token, out px))
                {
                    width = px;
                    continue;
                }

                var lower = token.ToLowerInvariant();
                if (lower == "thin") { width = 1; continue; }
                if (lower == "medium") { width = 2; continue; }
                if (lower == "thick") { width = 3; continue; }
                if (lower == "none" || lower == "hidden") { width = 0; continue; }
                if (lower == "solid" || lower == "dashed" || lower == "dotted" || lower == "double" ||
                    lower == "groove" || lower == "ridge" || lower == "inset" || lower == "outset")
                {
                    continue;
                }

                var candidate = TryBrush(token);
                if (candidate != null) brush = candidate;
            }

            if (!double.IsNaN(width))
            {
                var thickness = new Thickness(width);
                if (border != null) border.BorderThickness = thickness;
                if (ctrl != null) ctrl.BorderThickness = thickness;
            }
            if (brush != null)
            {
                if (border != null) border.BorderBrush = brush;
                if (ctrl != null) ctrl.BorderBrush = brush;
            }
        }

        private static Brush TryBrush(string css)
        {
            try
            {
                var c = CssParser.ParseColor(css);
                return c.HasValue ? new SolidColorBrush(c.Value) : null;
            }
            catch { return null; }
        }

        private static Windows.UI.Color FromHex(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 3)
            {
                // #rgb
                byte r = Convert.ToByte(new string(hex[0], 2), 16);
                byte g = Convert.ToByte(new string(hex[1], 2), 16);
                byte b = Convert.ToByte(new string(hex[2], 2), 16);
                return Windows.UI.Color.FromArgb(255, r, g, b);
            }
            if (hex.Length == 4)
            {
                // #argb
                byte a = Convert.ToByte(new string(hex[0], 2), 16);
                byte r = Convert.ToByte(new string(hex[1], 2), 16);
                byte g = Convert.ToByte(new string(hex[2], 2), 16);
                byte b = Convert.ToByte(new string(hex[3], 2), 16);
                return Windows.UI.Color.FromArgb(a, r, g, b);
            }
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return Windows.UI.Color.FromArgb(255, r, g, b);
            }
            if (hex.Length == 8)
            {
                byte a = Convert.ToByte(hex.Substring(0, 2), 16);
                byte r = Convert.ToByte(hex.Substring(2, 2), 16);
                byte g = Convert.ToByte(hex.Substring(4, 2), 16);
                byte b = Convert.ToByte(hex.Substring(6, 2), 16);
                return Windows.UI.Color.FromArgb(a, r, g, b);
            }
            return Windows.UI.Colors.Black;
        }

        private static Dictionary<string, string> ParseInlineStyle(string style)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var parts = style.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var kv = part.Split(new[] { ':' }, 2);
                if (kv.Length == 2)
                {
                    var k = kv[0].Trim().ToLowerInvariant();
                    var v = kv[1].Trim();
                    if (!dict.ContainsKey(k)) dict[k] = v;
                }
            }
            return dict;
        }

        // ---------- Visibility / search helpers ----------
        // Debug: allow rendering nodes even if inline CSS hides them
        private static bool DebugShowHidden = true;

        private static bool IsHidden(LiteElement n)
        {
            if (DebugShowHidden) return false;
            if (n == null || n.Attr == null) return false;

            string v;

            if (n.Attr.ContainsKey("hidden")) return true;

            if (n.Attr.TryGetValue("aria-hidden", out v) && v != null &&
                v.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)) return true;

            if (n.Attr.TryGetValue("style", out v) && !string.IsNullOrWhiteSpace(v))
            {
                var s = v.ToLowerInvariant();
                if (s.Contains("display:none") || s.Contains("visibility:hidden")) return true;
            }

            return false;
        }

        private static bool IsSearchHost(Uri u)
        {
            if (u == null) return false;
            var h = u.Host.ToLowerInvariant();
            return h.Contains("google.") || h.Contains("bing.") || h.Contains("duckduckgo.");
        }

        private FrameworkElement MakeSyntheticSearch(Uri baseUri, Action<Uri> onNavigate)
        {
            if (baseUri == null || onNavigate == null) return null;

            string endpoint = null;
            var hostName = baseUri.Host.ToLowerInvariant();

            if (hostName.Contains("google.")) endpoint = baseUri.Scheme + "://www.google.com/search";
            else if (hostName.Contains("bing.")) endpoint = baseUri.Scheme + "://www.bing.com/search";
            else if (hostName.Contains("duckduckgo.")) endpoint = baseUri.Scheme + "://duckduckgo.com/";
            else return null;

            // Centered, visible synthetic search box
            var container = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 12) };
            var vstack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
            var caption = new TextBlock { Text = "Basic search (JS disabled)", Opacity = 0.7, Margin = new Thickness(0, 0, 0, 6) };
            vstack.Children.Add(caption);
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var box = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Colors.White),
                BorderBrush = new SolidColorBrush(Windows.UI.Colors.Gray),
                BorderThickness = new Thickness(2, 2, 2, 2),
                Padding = new Thickness(8, 8, 8, 8),
                Child = panel
            };
            vstack.Children.Add(box);
            container.Children.Add(vstack);

            var tb = new TextBox { Width = 260, Margin = new Thickness(0, 0, 8, 0), PlaceholderText = "Search the web" };
            // Make the input clearly visible even without focus
            try { tb.Background = new SolidColorBrush(Windows.UI.Colors.White); } catch { /* swallow */ }
            try { tb.Foreground = new SolidColorBrush(Windows.UI.Colors.Black); } catch { /* swallow */ }
            try { tb.BorderBrush = new SolidColorBrush(Windows.UI.Colors.Gray); tb.BorderThickness = new Thickness(1, 1, 1, 1); } catch { /* swallow */ }
            try { tb.Height = 36; } catch { /* swallow */ }
            // Focus automatically so user sees caret immediately
            tb.Loaded += (s, e) => { try { tb.Focus(Windows.UI.Xaml.FocusState.Programmatic); } catch { /* swallow */ } };
            var btn = new Button { Content = "Search", Width = 90, HorizontalAlignment = HorizontalAlignment.Left };

            btn.Click += (s, e) =>
            {
                var q = Uri.EscapeDataString(tb.Text ?? "");
                Uri nav;
                char sep = endpoint.Contains("?") ? '&' : '?';
                if (hostName.Contains("duckduckgo."))
                {
                    nav = new Uri(endpoint + sep + "q=" + q);
                }
                else if (hostName.Contains("google."))
                {
                    nav = new Uri(endpoint + sep + "q=" + q + "&hl=en");
                }
                else
                {
                    nav = new Uri(endpoint + "?q=" + q);
                }
                onNavigate(nav);
            };

            panel.Children.Add(tb);
            panel.Children.Add(btn);
            return container;
        }

        private static bool LooksLikeSearchInput(LiteElement input)
        {
            if (input == null || input.Attr == null || input.Tag != "input") return false;

            string type; input.Attr.TryGetValue("type", out type);
            type = (type ?? "text").Trim().ToLowerInvariant();

            if (!(type == "text" || type == "search" || type == "url" || type == "email" || type == "password"))
                return false;

            string name; input.Attr.TryGetValue("name", out name);
            if (string.IsNullOrWhiteSpace(name)) return false;

            var key = name.Trim().ToLowerInvariant();
            return key == "q" || key == "query" || key == "search" || key == "s" || key == "keywords" || key == "term" || key == "text";
        }

        private static bool IsSearchForm(LiteElement form)
        {
            if (form == null || form.Tag != "form") return false;
            foreach (var d in form.Descendants())
                if (LooksLikeSearchInput(d)) return true;
            return false;
        }

        private bool _renderedOneSearchForm = false;

        // ---------- OpenGraph preview ----------
        private FrameworkElement CreateOpenGraphPreview(LiteElement doc, Uri baseUri, Action<Uri> onNavigate)
        {
            string title = GetMeta(doc, "og:title");
            string desc = GetMeta(doc, "og:description");
            string img = GetMeta(doc, "og:image");
            string url = GetMeta(doc, "og:url");

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(desc) && string.IsNullOrWhiteSpace(img))
                return null;

            var wrap = new Grid { Margin = new Thickness(8, 12, 8, 12) };
            wrap.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            wrap.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var card = new Border
            {
                Padding = new Thickness(12, 12, 12, 12),
                BorderThickness = new Thickness(1, 1, 1, 1),
                BorderBrush = new SolidColorBrush(Windows.UI.Colors.DimGray),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(12, 255, 255, 255)),
                CornerRadius = new CornerRadius(4)
            };

            var stack = new StackPanel { Orientation = Orientation.Vertical };
            if (!string.IsNullOrWhiteSpace(title))
                stack.Children.Add(new TextBlock { Text = title.Trim(), FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.WrapWholeWords });

            if (!string.IsNullOrWhiteSpace(desc))
                stack.Children.Add(new TextBlock { Text = desc.Trim(), Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.WrapWholeWords });

            if (!string.IsNullOrWhiteSpace(img))
            {
                var u = ResolveUri(baseUri, img);
                var image = new Image { Stretch = Stretch.Uniform, MaxHeight = 280, MaxWidth = 480 };
                if (u != null) image.Source = new BitmapImage(u);
                stack.Children.Add(image);
            }

            if (!string.IsNullOrWhiteSpace(url))
            {
                var abs = ResolveUri(baseUri, url);
                var btn = new Button
                {
                    Content = "Open",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                if (abs != null && onNavigate != null)
                    btn.Click += (s, e) => onNavigate(abs);
                stack.Children.Add(btn);
            }

            card.Child = stack;
            Grid.SetRow(card, 0);
            wrap.Children.Add(card);

            var previewMessage = "This page requires JavaScript. Showing preview.";
            if (Js != null && Js.AllowExternalScripts)
            {
                previewMessage = "This page relies on advanced scripting and may not render fully. Showing preview.";
            }

            var lbl = new TextBlock
            {
                Text = previewMessage,
                Margin = new Thickness(0, 8, 0, 0),
                Opacity = 0.7
            };
            Grid.SetRow(lbl, 1);
            wrap.Children.Add(lbl);

            return wrap;
        }

        private static string GetMeta(LiteElement root, string propertyValue)
        {
            try
            {
                var m = root.Descendants().FirstOrDefault(n =>
                    n.Tag == "meta" && n.Attr != null &&
                    ((n.Attr.ContainsKey("property") && string.Equals(n.Attr["property"], propertyValue, StringComparison.OrdinalIgnoreCase)) ||
                     (n.Attr.ContainsKey("name") && string.Equals(n.Attr["name"], propertyValue, StringComparison.OrdinalIgnoreCase)))
                    && n.Attr.ContainsKey("content"));
                if (m != null) return m.Attr["content"];
            }
            catch { /* swallow */ }
            return null;
        }

        // ---------- Material Icons ----------
        private static bool IsMaterialIcon(LiteElement n)
        {
            if (n == null || n.Attr == null) return false;
            string cls; if (!n.Attr.TryGetValue("class", out cls) || string.IsNullOrWhiteSpace(cls)) return false;
            return cls.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                      .Any(c => string.Equals(c, "material-icons", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(c, "material-symbols-outlined", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(c, "material-symbols-rounded", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(c, "material-symbols-sharp", StringComparison.OrdinalIgnoreCase));
        }

        private FrameworkElement MakeMaterialIcon(LiteElement n)
        {
            var name = (CollapseWs(GatherText(n)) ?? "").Trim().ToLowerInvariant();
            string glyph;
            switch (name)
            {
                case "menu": glyph = "\u2261"; break;                 // =
                case "search": glyph = "\uD83D\uDD0D"; break;         // ??
                case "clear":
                case "close": glyph = "\u2715"; break;                 // ?
                case "more_vert": glyph = "\u22EE"; break;            // ?
                case "more_horiz": glyph = "\u22EF"; break;           // ?
                case "chevron_left": glyph = "\u2039"; break;         // ?
                case "chevron_right": glyph = "\u203A"; break;        // ?
                case "arrow_back": glyph = "\u2190"; break;           // ?
                case "arrow_forward": glyph = "\u2192"; break;        // ?
                case "play_arrow": glyph = "\u25B6"; break;           // ?
                case "pause": glyph = "\u275A\u275A"; break;          // ??
                case "home": glyph = "\u2302"; break;                 // ?
                case "share": glyph = "\u21AA"; break;                // ?
                default: glyph = name; break;                          // show token if unknown
            }

            var tb = new TextBlock
            {
                Text = glyph,
                FontSize = 20,
                Margin = new Thickness(0, 2, 0, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            ApplyComputedStyles(tb, n);
            ApplyInlineStyles(tb, n);
            return tb;
        }

        // Wrapper: ensure all UI-affinitized image object creation occurs on the UI thread.
        // Root cause of lingering RPC_E_WRONG_THREAD was BitmapImage/SVG ImageSource construction on a background pool.
        // This method now marshals to UI thread if required before creating any XAML-affinitized objects.
        private Task<ImageSource> LoadImageSourceAsync(Uri abs, int decodeWidthHint = 0)
        {
            var disp = UiThreadHelper.TryGetDispatcher();
            if (disp != null && !UiThreadHelper.HasThreadAccess(disp))
            {
                var tcs = new System.Threading.Tasks.TaskCompletionSource<ImageSource>();
                UiThreadHelper.RunAsync(disp, Windows.UI.Core.CoreDispatcherPriority.Low, async () =>
                {
                    try { var r = await LoadImageSourceUiThreadAsync(abs, decodeWidthHint); tcs.TrySetResult(r); }
                    catch (Exception ex) { tcs.TrySetException(ex); }
                });
                return tcs.Task;
            }
            // Already on UI thread
            return LoadImageSourceUiThreadAsync(abs, decodeWidthHint);
        }

        // Performs actual image source construction (must run on UI thread).
        private async Task<ImageSource> LoadImageSourceUiThreadAsync(Uri abs, int decodeWidthHint)
        {
            if (abs == null) return null;

            // Try SVG via reflection (no compile-time dependency).
            if (LooksSvg(abs) && SvgType != null)
            {
                try
                {
                    var svg = (ImageSource)Activator.CreateInstance(SvgType);

                    // Prefer cookie-aware stream provided by the host
                    if (ImageLoader != null)
                    {
                        var stream = await ImageLoader(abs);
                        if (stream != null && SvgSetSourceAsync != null)
                        {
                            var op = SvgSetSourceAsync.Invoke(svg, new object[] { stream }) as IAsyncAction;
                            if (op != null)
                            {
                                try { await op.AsTask(); } catch { /* swallow */ }
                                return svg;
                            }
                        }
                    }

                    // Fallback: set UriSource property (may not carry cookies)
                    SvgUriSourceProp?.SetValue(svg, abs);
                    return svg;
                }
                catch
                {
                    // Fall through to raster path on any failure
                }
            }

            // Raster path
            try
            {
                // Guard: WP8.1 cannot decode SVG via BitmapImage; skip if no SvgImageSource
                if (LooksSvg(abs) && SvgType == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ImgSkipSvg] {abs}");
                    return null; // avoid native crash on unsupported format
                }
                var bmp = new BitmapImage();
                if (decodeWidthHint > 0) bmp.DecodePixelWidth = decodeWidthHint;

                if (ImageLoader != null)
                {
                    var s = await ImageLoader(abs);
                    if (s != null)
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"[ImgTryStream] {abs} len={s.Size}");
                            await bmp.SetSourceAsync(s);
                            System.Diagnostics.Debug.WriteLine($"[ImgLoadOK] {abs}");
                            return bmp;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ImgLoadFail] {abs} ex={ex.GetType().Name}: {ex.Message}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[ImgNullStream] {abs}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[ImgFallbackUri] {abs}");
                bmp.UriSource = abs; // last fallback
                return bmp;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImgLoadExc] {abs} ex={ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private IReadOnlyList<Uri> ResolveImageUriCandidates(LiteElement n, Uri baseUri)
        {
            var results = new List<Uri>();
            if (n == null) return results;

            string src = null;
            string srcset = null;

            if (n.Attr != null)
            {
                n.Attr.TryGetValue("src", out src);
                if (string.IsNullOrWhiteSpace(src))
                {
                    string v;
                    if (n.Attr.TryGetValue("data-src", out v) && !string.IsNullOrWhiteSpace(v)) src = v;
                    else if (n.Attr.TryGetValue("data-original", out v) && !string.IsNullOrWhiteSpace(v)) src = v;
                    else if (n.Attr.TryGetValue("data-lazy", out v) && !string.IsNullOrWhiteSpace(v)) src = v;
                    else if (n.Attr.TryGetValue("data-src-small", out v) && !string.IsNullOrWhiteSpace(v)) src = v;
                }

                if (!n.Attr.TryGetValue("srcset", out srcset) || string.IsNullOrWhiteSpace(srcset))
                {
                    string ds;
                    if (n.Attr.TryGetValue("data-srcset", out ds) && !string.IsNullOrWhiteSpace(ds))
                        srcset = ds;
                }
            }

            var candidateStrings = new List<string>();

            // gather explicit srcset entries (order preserved)
            if (!string.IsNullOrWhiteSpace(srcset))
            {
                var tokens = new List<string>();
                foreach (var part in srcset.Split(','))
                {
                    var trimmed = part.Trim();
                    if (trimmed.Length == 0) continue;
                    var firstSpace = trimmed.IndexOf(' ');
                    var url = firstSpace >= 0 ? trimmed.Substring(0, firstSpace) : trimmed;
                    if (!string.IsNullOrWhiteSpace(url)) tokens.Add(url);
                }

                var dw = 360.0;
                try { dw = Window.Current?.Bounds.Width ?? 360.0; } catch { /* swallow */ }
                var preferred = PickSrcFromSrcset(srcset, dw);
                if (!string.IsNullOrWhiteSpace(preferred)) candidateStrings.Add(preferred);

                foreach (var token in tokens)
                {
                    if (!string.IsNullOrWhiteSpace(token)) candidateStrings.Add(token);
                }
            }

            if (!string.IsNullOrWhiteSpace(src))
                candidateStrings.Add(src);

            var seenStrings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Helper in method scope (C# 6/7 compatible): add candidate URI if valid and not duplicate
            Action<string> tryAdd = candidate =>
            {
                if (string.IsNullOrWhiteSpace(candidate)) return;
                Uri abs = ResolveUri(baseUri, candidate);
                if (abs == null) return;
                var key = abs.ToString();
                if (seenUris.Add(key)) results.Add(abs);
            };

            Action<string> addVariant = raw =>
            {
                if (string.IsNullOrWhiteSpace(raw)) return;
                if (!seenStrings.Add(raw)) return;

                var rewritten = RewriteImageUrlIfNeeded(raw);
                tryAdd(rewritten);
                if (!string.Equals(rewritten, raw, StringComparison.OrdinalIgnoreCase))
                    tryAdd(raw);
            };

            foreach (var cand in candidateStrings)
            {
                addVariant(cand);
            }

            return results;
        }

        private Uri ResolveBestImgUri(LiteElement n, Uri baseUri)
        {
            var list = ResolveImageUriCandidates(n, baseUri);
            return (list != null && list.Count > 0) ? list[0] : null;
        }
        // (continue DomBasicRenderer – removed premature class closure and duplicate partial declaration)
        
        // [NEW FEATURE] Render <details>/<summary> without external Expander dependency.
        private async System.Threading.Tasks.Task<FrameworkElement> MakeDetailsAsync(LiteElement n, Uri baseUri, Action<Uri> onNavigate, JavaScriptEngine js, System.Threading.CancellationToken ct)
        {
            var root = new StackPanel { Orientation = Orientation.Vertical };
            var headerText = "Details";
            try
            {
                var sn = default(LiteElement);
                if (n.Children != null)
                {
                    for (int k = 0; k < n.Children.Count; k++)
                    {
                        var ch = n.Children[k];
                        if (!ch.IsText && string.Equals(ch.Tag, "summary", StringComparison.OrdinalIgnoreCase)) { sn = ch; break; }
                    }
                }
                if (sn != null) headerText = CollapseWs(GatherText(sn) ?? "Details");
                else headerText = CollapseWs(GatherText(n) ?? "Details");
            } catch { /* swallow */ }
            var btn = new Button { Content = new TextBlock { Text = headerText, FontWeight = Windows.UI.Text.FontWeights.SemiBold } };
            var content = new StackPanel { Orientation = Orientation.Vertical };
            bool isOpen = false; try { if (n.Attr != null && n.Attr.ContainsKey("open")) isOpen = true; } catch { /* swallow */ }
            content.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            LiteElement summaryNode = null;
            if (n.Children != null)
            {
                for (int k = 0; k < n.Children.Count; k++)
                {
                    var ch = n.Children[k];
                    if (!ch.IsText && string.Equals(ch.Tag, "summary", StringComparison.OrdinalIgnoreCase) && summaryNode == null) { summaryNode = ch; break; }
                }
                for (int k = 0; k < n.Children.Count; k++)
                {
                    var ch = n.Children[k];
                    if (summaryNode != null && object.ReferenceEquals(ch, summaryNode)) continue;
                    var fe = await RenderNodeAsync(ch, baseUri, onNavigate, js, ct);
                    if (fe != null) content.Children.Add(fe);
                }
            }
            btn.Click += (s_, e_) => { content.Visibility = content.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; };
            root.Children.Add(btn);
            root.Children.Add(content);
            return root;
        }

        // [NEW FEATURE] <progress> element
        private FrameworkElement MakeProgress(LiteElement n)
        {
            double value = 0, max = 1;
            try { string s; if (n.Attr != null && n.Attr.TryGetValue("value", out s)) double.TryParse(s, out value); } catch { /* swallow */ }
            try { string s; if (n.Attr != null && n.Attr.TryGetValue("max", out s)) double.TryParse(s, out max); } catch { /* swallow */ }
            if (max <= 0) max = 1;
            if (value < 0) value = 0; if (value > max) value = max;
            var pb = new ProgressBar { Minimum = 0, Maximum = max, Value = value, Height = 4, Margin = new Thickness(0, 4, 0, 4) };
            return pb;
        }

        // [NEW FEATURE] <meter> element
        private FrameworkElement MakeMeter(LiteElement n)
        {
            double value = 0, min = 0, max = 1, low = double.NaN, high = double.NaN, optimum = double.NaN;
            try { string s; if (n.Attr != null && n.Attr.TryGetValue("value", out s)) double.TryParse(s, out value); } catch { /* swallow */ }
            try { string s; if (n.Attr != null && n.Attr.TryGetValue("min", out s)) double.TryParse(s, out min); } catch { /* swallow */ }
            try { string s; if (n.Attr != null && n.Attr.TryGetValue("max", out s)) double.TryParse(s, out max); } catch { /* swallow */ }
            try { string s; if (n.Attr != null && n.Attr.TryGetValue("low", out s)) double.TryParse(s, out low); } catch { /* swallow */ }
            try { string s; if (n.Attr != null && n.Attr.TryGetValue("high", out s)) double.TryParse(s, out high); } catch { /* swallow */ }
            try { string s; if (n.Attr != null && n.Attr.TryGetValue("optimum", out s)) double.TryParse(s, out optimum); } catch { /* swallow */ }
            if (max <= min) { max = min + 1; }
            if (value < min) value = min; if (value > max) value = max;
            var pb = new ProgressBar { Minimum = min, Maximum = max, Value = value, Height = 4, Margin = new Thickness(0, 4, 0, 4) };
            try
            {
                if (!double.IsNaN(low) && !double.IsNaN(high) && !double.IsNaN(optimum))
                {
                    bool good = (optimum <= low) ? (value <= low) : (optimum >= high ? (value >= high) : (value >= low && value <= high));
                    if (!good) pb.Opacity = 0.7;
                }
            } catch { /* swallow */ }
            return pb;
        }

        // Apply position:relative offsets using translate without removing from flow
        private static void ApplyRelativeOffset(FrameworkElement fe, CssComputed css, FrameworkElement container)
        {
            if (fe == null || css == null) return;
            try
            {
                // Only for explicitly relative; otherwise ignore
                if (!(string.Equals(css.Position, "relative", StringComparison.OrdinalIgnoreCase))) return;

                TranslateTransform tt = null;
                Action place = () =>
                {
                    try
                    {
                        double cw = 0, ch = 0;
                        try { if (container != null) { cw = container.ActualWidth; ch = container.ActualHeight; } } catch { /* swallow */ }

                        Func<string, double?, double> pxOrPercent = (name, containerSize) =>
                        {
                            try
                            {
                                string raw;
                                if (css.Map != null && css.Map.TryGetValue(name, out raw) && !string.IsNullOrWhiteSpace(raw))
                                {
                                    raw = raw.Trim();
                                    if (raw.EndsWith("%"))
                                    {
                                        double p; if (double.TryParse(raw.TrimEnd('%'), out p) && containerSize.HasValue)
                                            return (p / 100.0) * containerSize.Value;
                                    }
                                    double px; if (double.TryParse(raw.Replace("px", "").Trim(), out px)) return px;
                                }
                            }
                            catch { /* swallow */ }
                            return double.NaN;
                        };

                        double l = double.NaN, r = double.NaN, t = double.NaN, b = double.NaN;
                        if (css.Left.HasValue) l = css.Left.Value; else l = pxOrPercent("left", cw);
                        if (css.Right.HasValue) r = css.Right.Value; else r = pxOrPercent("right", cw);
                        if (css.Top.HasValue) t = css.Top.Value; else t = pxOrPercent("top", ch);
                        if (css.Bottom.HasValue) b = css.Bottom.Value; else b = pxOrPercent("bottom", ch);

                        double dx = 0, dy = 0;
                        if (!double.IsNaN(l)) dx += l;
                        if (!double.IsNaN(r)) dx -= r;
                        if (!double.IsNaN(t)) dy += t;
                        if (!double.IsNaN(b)) dy -= b;

                        if (tt == null)
                        {
                            tt = new TranslateTransform();
                            fe.RenderTransform = tt;
                        }
                        tt.X = dx; tt.Y = dy;

                        // z-index affects stacking where overlapping occurs (mostly in Canvas); respect if provided
                        if (css.ZIndex.HasValue) Canvas.SetZIndex(fe, css.ZIndex.Value);
                    }
                    catch { /* swallow */ }
                };

                fe.Loaded += (s, e) => { try { place(); } catch { /* swallow */ } };
                fe.SizeChanged += (s, e) => { try { place(); } catch { /* swallow */ } };
                if (container != null) container.SizeChanged += (s, e) => { try { place(); } catch { /* swallow */ } };
                place();
            }
            catch { /* swallow */ }
        }

        // ---- List bullet helpers and CSS lookup ----
        private static string ToUnorderedBullet(LiteElement list, LiteElement li)
        {
            try
            {
                // Attribute 'type' wins over CSS for compatibility
                string type = null;
                if (list != null && list.Attr != null && list.Attr.TryGetValue("type", out type) && !string.IsNullOrWhiteSpace(type)) { }
                else if (li != null && li.Attr != null && li.Attr.TryGetValue("type", out type) && !string.IsNullOrWhiteSpace(type)) { }
                else { var cssUl = TryGetCssStatic(list); if (cssUl != null && cssUl.Map != null) cssUl.Map.TryGetValue("list-style-type", out type); }
                var cssLi = TryGetCssStatic(li); if (cssLi != null && cssLi.Map != null) { string tmp; if (cssLi.Map.TryGetValue("list-style-type", out tmp) && !string.IsNullOrWhiteSpace(tmp)) type = tmp; }
                var t = (type ?? "").Trim().ToLowerInvariant();
                if (t.Contains("circle")) return "◦";
                if (t.Contains("square")) return "▪";
                if (t.Contains("none")) return string.Empty;
                return "•";
            }
            catch { return "•"; }
        }

        private static string ToOrderedBullet(LiteElement list, LiteElement li, int idx)
        {
            try
            {
                // HTML 'type' attribute (A/a/I/i/1) has priority
                string type = null;
                if (list != null && list.Attr != null && list.Attr.TryGetValue("type", out type) && !string.IsNullOrWhiteSpace(type)) { }
                else if (li != null && li.Attr != null && li.Attr.TryGetValue("type", out type) && !string.IsNullOrWhiteSpace(type)) { }
                else { var cssOl = TryGetCssStatic(list); if (cssOl != null && cssOl.Map != null) cssOl.Map.TryGetValue("list-style-type", out type); }
                var cssLi = TryGetCssStatic(li); if (cssLi != null && cssLi.Map != null) { string tmp; if (cssLi.Map.TryGetValue("list-style-type", out tmp) && !string.IsNullOrWhiteSpace(tmp)) type = tmp; }
                var t = (type ?? "").Trim().ToLowerInvariant();
                // HTML types
                if (t == "a" || t.Contains("lower-alpha") || t.Contains("lower-latin")) return ToAlpha(idx, false) + ".";
                if (t == "A" || t.Contains("upper-alpha") || t.Contains("upper-latin")) return ToAlpha(idx, true) + ".";
                if (t == "i" || t.Contains("lower-roman")) return ToRoman(idx).ToLowerInvariant() + ".";
                if (t == "I" || t.Contains("upper-roman")) return ToRoman(idx).ToUpperInvariant() + ".";
                if (t == "1" || t.Contains("decimal")) return idx.ToString() + ".";
                return idx.ToString() + ".";
            }
            catch { return idx.ToString() + "."; }
        }

        private static string ToRoman(int n)
        {
            if (n <= 0) return n.ToString();
            int[] vals = {1000,900,500,400,100,90,50,40,10,9,5,4,1};
            string[] syms = {"M","CM","D","CD","C","XC","L","XL","X","IX","V","IV","I"};
            int i = 0; var sb = new System.Text.StringBuilder(); int x = n;
            while (x > 0) { if (x >= vals[i]) { sb.Append(syms[i]); x -= vals[i]; } else i++; }
            return sb.ToString();
        }

        private static string ToAlpha(int n, bool upper)
        {
            if (n <= 0) return n.ToString();
            n--; var sb = new System.Text.StringBuilder();
            do { int rem = n % 26; char ch = (char)((upper ? 'A' : 'a') + rem); sb.Insert(0, ch); n = n / 26 - 1; } while (n >= 0);
            return sb.ToString();
        }

        private static CssComputed TryGetCssStatic(LiteElement el)
        {
            try { return (_computedStylesStatic != null && el != null && _computedStylesStatic.ContainsKey(el)) ? _computedStylesStatic[el] : null; } catch { return null; }
        }
        private static System.Collections.Generic.Dictionary<LiteElement, CssComputed> _computedStylesStatic;

        private static string ApplyHyphens(string text, CssComputed css)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // Default (manual): preserve soft hyphens.
            // None: strip them.
            // Auto: not supported, treated as manual.
            if (css != null && string.Equals(css.Hyphens, "none", StringComparison.OrdinalIgnoreCase))
            {
                return text.Replace("\u00AD", "");
            }
            return text;
        }

        private static string NormalizeTextNodeContent(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var trimmed = raw.Trim();

            // If there are tags, take the text after the last close-angle to prefer visible content.
            int lastTagClose = trimmed.LastIndexOf('>');
            if (lastTagClose >= 0 && lastTagClose < trimmed.Length - 1)
            {
                var after = trimmed.Substring(lastTagClose + 1).Trim();
                if (!string.IsNullOrEmpty(after)) trimmed = after;
            }

            if (string.IsNullOrWhiteSpace(trimmed)) return null;

            // Iteratively strip leading attribute-like sequences such as: key="value" or key='value' or key=value
            // and leading filename fragments like something.png or stray punctuation/quotes that often appear when markup is injected.
            // Implement with simple scanning (regex-free) to avoid escaping issues and keep behavior explicit.
            int guard = 0;
            while (guard++ < 8)
            {
                var before = trimmed;

                // Skip leading whitespace
                int idx = 0;
                while (idx < trimmed.Length && char.IsWhiteSpace(trimmed[idx])) idx++;
                if (idx >= trimmed.Length) return null;

                // Try to parse a key=value sequence at the start
                int keyStart = idx;
                while (idx < trimmed.Length && (char.IsLetterOrDigit(trimmed[idx]) || trimmed[idx] == '_' || trimmed[idx] == ':' || trimmed[idx] == '-' || trimmed[idx] == '.' || trimmed[idx] == '/')) idx++;
                if (idx < trimmed.Length && trimmed[idx] == '=')
                {
                    // consume '=' and the value (quoted or unquoted)
                    idx++; while (idx < trimmed.Length && char.IsWhiteSpace(trimmed[idx])) idx++;
                    if (idx < trimmed.Length)
                    {
                        if (trimmed[idx] == '"' || trimmed[idx] == '\'')
                        {
                            char q = trimmed[idx++];
                            while (idx < trimmed.Length && trimmed[idx] != q) idx++;
                            if (idx < trimmed.Length) idx++; // consume closing quote
                        }
                        else
                        {
                            while (idx < trimmed.Length && !char.IsWhiteSpace(trimmed[idx]) && trimmed[idx] != '>') idx++;
                        }

                        trimmed = trimmed.Substring(idx).TrimStart();
                        if (trimmed.Length == 0) return null;
                        continue;
                    }
                }

                // Try token at start (possible filename). If first token contains a known image extension, strip it.
                idx = 0; while (idx < trimmed.Length && char.IsWhiteSpace(trimmed[idx])) idx++;
                int tokenStart = idx; while (idx < trimmed.Length && !char.IsWhiteSpace(trimmed[idx])) idx++;
                if (tokenStart < idx)
                {
                    var token = trimmed.Substring(tokenStart, idx - tokenStart).ToLowerInvariant();
                    if (token.Contains(".png") || token.Contains(".jpg") || token.Contains(".jpeg") || token.Contains(".gif") || token.Contains(".svg"))
                    {
                        trimmed = trimmed.Substring(idx).TrimStart();
                        if (trimmed.Length == 0) return null;
                        continue;
                    }
                }

                // Strip leading non-alphanumeric punctuation (quotes, slashes, equals, angle brackets, etc.)
                idx = 0; while (idx < trimmed.Length && !char.IsLetterOrDigit(trimmed[idx]) && trimmed[idx] != '\u00AD') idx++;
                if (idx > 0)
                {
                    trimmed = trimmed.Substring(idx).TrimStart();
                    if (trimmed.Length == 0) return null;
                    continue;
                }

                // Nothing more to strip
                if (trimmed == before) break;
            }

            // As a last resort, if there's still a closing tag later, use text after it
            int lt = trimmed.IndexOf('<');
            int gt = trimmed.LastIndexOf('>');
            if (lt >= 0 && gt >= lt)
            {
                var after = trimmed.Substring(gt + 1).Trim();
                if (!string.IsNullOrEmpty(after)) trimmed = after;
            }

            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed.Trim();
        }

        // Heuristic: ignore text nodes that are actually CSS/JS blobs from noscript fallbacks.
        private static bool ShouldSuppressTextNode(string raw)
        {
#if DEBUG
            bool debugEnabled = true; // flip to false to silence diagnostics quickly
#else
            const bool debugEnabled = false;
#endif
            string reason = null;

            if (string.IsNullOrWhiteSpace(raw)) { reason = "empty"; return true; }

            var trimmed = raw.Trim();
            if (trimmed.Length <= 1) return false; // allow single char labels like © or ®

            var lower = trimmed.ToLowerInvariant();

            // Aggressive infra-noise check FIRST (before any other heuristics)
            if (lower.IndexOf("balancer", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("nodeversion", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("yaru_", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("direct-close-block", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("yastatic", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("mc.yandex", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("yabs.yandex", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("avatars.mds.yandex", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("yandex.ru", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("yandex", StringComparison.Ordinal) >= 0 && lower.IndexOf("браузер", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("desktop", StringComparison.Ordinal) >= 0 && lower.IndexOf("common", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("yaru_desktop", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("direct-close", StringComparison.Ordinal) >= 0)
            {
                reason = "infra-noise";
            }

            // Short infra-noise tokens (standalone or dominant)
            if (reason == null)
            {
                // Check for standalone infra tokens
                var parts = trimmed.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var p = part.ToLowerInvariant();
                    if (p == "yaru" || p == "yandex" || p == "desktop" || p == "common" || p == "block" || p == "close" ||
                        p.StartsWith("yaru") || p.EndsWith("yaru") ||
                        p.Contains("yaru_") || p.Contains("balancer") || p.Contains("nodeversion") ||
                        p.Contains("yastatic") || p.Contains("mc.yandex") || p.Contains("yabs.yandex") ||
                        p.Contains("desktop_common") || p.Contains("direct-close"))
                    {
                        reason = "short-infra-token";
                        break;
                    }
                }
            }

            // CSS/JSON braces check (aggressive)
            if (reason == null && (trimmed.Contains('{') || trimmed.Contains('}')))
                reason = "css-json-braces";

            // CSS selector pattern: word{ or word word{ (even without . or #)
            if (reason == null && trimmed.Length >= 3)
            {
                var cssSelectorRx = new System.Text.RegularExpressions.Regex(@"[.#]?[a-zA-Z][\w-]*(\s+[a-zA-Z][\w-]*)*\s*\{", System.Text.RegularExpressions.RegexOptions.Compiled);
                if (cssSelectorRx.IsMatch(trimmed)) reason = "css-selector";
            }

            // Long numeric ID with dash (balancer IDs, timestamps) - even if short
            if (reason == null && trimmed.Length >= 10)
            {
                var numIdRx = new System.Text.RegularExpressions.Regex(@"\d{8,}-\d+", System.Text.RegularExpressions.RegexOptions.Compiled);
                if (numIdRx.IsMatch(trimmed)) reason = "numeric-id";
            }

            // Long hash/ID pattern - even if short
            if (reason == null && trimmed.Length >= 15)
            {
                var hashRx = new System.Text.RegularExpressions.Regex(@"[a-zA-Z0-9]{10,}-[a-zA-Z0-9\-]{3,}", System.Text.RegularExpressions.RegexOptions.Compiled);
                if (hashRx.IsMatch(trimmed)) reason = "long-hash-id";
            }

            // JSON-like pattern: starts with { or contains "key":"value"
            if (reason == null && trimmed.Length >= 10)
            {
                if (trimmed.StartsWith("{") || trimmed.Contains("\"static\"") || trimmed.Contains("\"content\"") || trimmed.Contains("\"domain\"") || trimmed.Contains("\"nodeVersion\""))
                    reason = "json-like";
            }

            // Pure numeric or alphanumeric with dashes (like balancer IDs)
            if (reason == null && trimmed.Length >= 10)
            {
                var pureIdRx = new System.Text.RegularExpressions.Regex(@"^[\d\-]+$", System.Text.RegularExpressions.RegexOptions.Compiled);
                if (pureIdRx.IsMatch(trimmed) && trimmed.Contains("-")) reason = "pure-numeric-id";
            }

            // Infrastructure noise with underscores (yaru_desktop_common, etc.)
            if (reason == null && trimmed.Length >= 8)
            {
                var infraUnderscoreRx = new System.Text.RegularExpressions.Regex(@"^[a-zA-Z][\w]*_[a-zA-Z][\w]*$", System.Text.RegularExpressions.RegexOptions.Compiled);
                if (infraUnderscoreRx.IsMatch(trimmed) && (trimmed.Contains("desktop") || trimmed.Contains("common") || trimmed.Contains("block") || trimmed.Contains("close")))
                    reason = "infra-underscore";
            }

            // Continue with original heuristics only if not already suppressed
            if (reason == null)
            {
                int braceCount = 0, semicolonCount = 0, angleCount = 0;
                int letterDigitCount = 0, whitespaceCount = 0, otherCount = 0;
                int symbolRun = 0, maxSymbolRun = 0;
                for (int i = 0; i < trimmed.Length; i++)
                {
                    var ch = trimmed[i];
                    if (ch == '{' || ch == '}') braceCount++;
                    else if (ch == ';') semicolonCount++;
                    else if (ch == '<' || ch == '>') angleCount++;

                    if (char.IsLetterOrDigit(ch))
                    {
                        letterDigitCount++;
                        symbolRun = 0;
                    }
                    else if (char.IsWhiteSpace(ch))
                    {
                        whitespaceCount++;
                        symbolRun = 0;
                    }
                    else
                    {
                        otherCount++;
                        symbolRun++;
                        if (symbolRun > maxSymbolRun) maxSymbolRun = symbolRun;
                    }
                }

                int symbolScore = braceCount + semicolonCount + angleCount;
                if (symbolScore >= Math.Max(8, trimmed.Length / 5)) reason = "symbol-score";
                else if (trimmed.Length >= 20 && maxSymbolRun >= 6) reason = "symbol-run";
                else if (trimmed.Length >= 40)
                {
                    int effectiveLen = Math.Max(1, trimmed.Length - whitespaceCount);
                    double letterRatio = letterDigitCount / (double)effectiveLen;
                    if (letterDigitCount > 0 && letterRatio < 0.3 && otherCount > letterDigitCount) reason = "low-letter-ratio";
                    else if (letterDigitCount == 0 && otherCount > 0) reason = "no-letters";
                }

                // Embedded tags/attributes only suppress if they dominate (high symbolScore)
                if (reason == null && (trimmed.Contains("</") || trimmed.Contains("/>")) && symbolScore > 4) reason = "embedded-tags";
                if (reason == null && (trimmed.Contains("=\"") || trimmed.Contains("='")) && symbolScore > 4) reason = "attributes-inline";

                int attrHits = 0;
                if (lower.Contains(" alt=") || lower.StartsWith("alt=")) attrHits++;
                if (lower.Contains("height=")) attrHits++;
                if (lower.Contains("width=")) attrHits++;
                if (lower.Contains("style=")) attrHits++;
                if ((lower.Contains("display:") || lower.Contains("border:"))) attrHits++;
                if (reason == null && attrHits >= 2 && symbolScore > 3) reason = "attr-dump";

                bool hasImgName = lower.Contains(".png") || lower.Contains(".jpg") || lower.Contains(".jpeg") || lower.Contains(".gif") || lower.Contains("image/");
                if (reason == null && hasImgName && attrHits >= 1 && symbolScore > 4) reason = "image-meta";
                if (reason == null && lower.Contains("<img")) reason = "img-fragment";
                if (reason == null && (lower.Contains("function(") || lower.Contains("var ") || lower.Contains("let ") || lower.Contains("const "))) reason = "js-snippet";
                if (reason == null && (lower.Contains("</style>") || lower.Contains("<style") || lower.Contains("</script>"))) reason = "style-script-marker";
                if (reason == null && (lower.StartsWith("/*") || lower.StartsWith("//"))) reason = "comment";
                if (reason == null && braceCount >= 3 && lower.Contains(".")) reason = "css-js-mix";
                if (reason == null && trimmed.Length >= 80 && otherCount >= Math.Max(15, trimmed.Length / 2)) reason = "dense-symbols";

                // CSS property pattern: word: value; (multiple occurrences)
                if (reason == null && trimmed.Length >= 15)
                {
                    int colonCount = 0;
                    for (int i = 0; i < trimmed.Length; i++)
                        if (trimmed[i] == ':') colonCount++;
                    if (colonCount >= 2 && (lower.Contains("opacity") || lower.Contains("color") || lower.Contains("transition") || lower.Contains("display") || lower.Contains("margin") || lower.Contains("padding") || lower.Contains("border") || lower.Contains("background") || lower.Contains("font") || lower.Contains("width") || lower.Contains("height")))
                        reason = "css-properties";
                }

                // JSON pattern: {"key":"value",...}
                if (reason == null && trimmed.Length >= 10 && trimmed[0] == '{' && trimmed[trimmed.Length - 1] == '}')
                {
                    int quoteCount = 0;
                    for (int i = 0; i < trimmed.Length; i++)
                        if (trimmed[i] == '"') quoteCount++;
                    if (quoteCount >= 4) reason = "json-object";
                }

                // Aggressive CSS/JSON density check
                if (reason == null && trimmed.Length >= 10)
                {
                    int braceColonQuote = 0;
                    for (int i = 0; i < trimmed.Length; i++)
                    {
                        var ch = trimmed[i];
                        if (ch == '{' || ch == '}' || ch == ':' || ch == '"' || ch == ';')
                            braceColonQuote++;
                    }
                    double ratio = (double)braceColonQuote / trimmed.Length;
                    if (ratio > 0.05 && braceColonQuote >= 2)
                        reason = "css-json-density";
                }
            }

            bool suppress = reason != null;
            if (debugEnabled && suppress)
            {
                try { System.Diagnostics.Debug.WriteLine("[TextSuppress] reason=" + reason + " len=" + trimmed.Length + " sample=" + trimmed.Substring(0, Math.Min(60, trimmed.Length)).Replace('\n',' ').Replace('\r',' ')); } catch { /* swallow */ }
            }
            return suppress;
        }

        private TextBlock RenderPlainTextBlock(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                    return null;

                return new TextBlock
                {
                    Text = text.Trim(),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Windows.UI.Colors.Black),
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 0, 4)
                };
            }
            catch
            {
                return null;
            }
        }
    }

    // ---------- Form submit event payload (moved outside DomBasicRenderer) ----------
    public sealed class FormSubmitEventArgs : EventArgs
    {
        public string Method { get; set; } // "post", "get", etc.
        public Uri Action { get; set; }
        public Dictionary<string, string> Fields { get; set; }
    }
}
