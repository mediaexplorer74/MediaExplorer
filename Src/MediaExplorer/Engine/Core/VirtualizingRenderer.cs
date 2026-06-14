using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI.Xaml.Input;
using BrowserCore.Engine;

namespace BrowserCore.Engine.Core
{
    public class VirtualizingRenderer
    {
        private readonly RenderObject _root;
        private readonly ScrollViewer _scrollViewer;
        private readonly Canvas _canvas;
        private readonly Action<Uri> _onNavigate;
        private readonly Uri _baseUri;

        public event Action<string, string, string> NodeTapped; // id, name, type

        // Active elements currently on canvas (RenderObject → UIElement)
        private readonly Dictionary<RenderObject, UIElement> _activeElements = new Dictionary<RenderObject, UIElement>();

        // Element pools by type (reuse instead of allocate)
        private readonly Dictionary<Type, Stack<UIElement>> _pools = new Dictionary<Type, Stack<UIElement>>();

        // Lazy image loading: RenderObject → Image element (Source deferred until in-viewport)
        private readonly Dictionary<RenderObject, Image> _lazyImages = new Dictionary<RenderObject, Image>();

        // Track elements that already have a Tapped link handler (to avoid duplicates from pool reuse)
        private readonly HashSet<UIElement> _linkHandledElements = new HashSet<UIElement>();

        // Store handler references for cleanup on pool return
        private readonly Dictionary<UIElement, object> _linkHandlerRefs = new Dictionary<UIElement, object>();

        // Track sticky elements for scroll-time repositioning
        private readonly HashSet<RenderObject> _stickyElements = new HashSet<RenderObject>();
        private readonly Dictionary<RenderObject, double> _stickyOriginalY = new Dictionary<RenderObject, double>();
        private readonly Dictionary<RenderObject, double> _stickyTop = new Dictionary<RenderObject, double>();

        // Track overflow:auto/scroll containers → their inner Canvas (for nested scrolling)
        private readonly Dictionary<RenderObject, Canvas> _overflowCanvases = new Dictionary<RenderObject, Canvas>();
        private readonly Dictionary<RenderObject, RenderObject> _overflowParents = new Dictionary<RenderObject, RenderObject>();

        private static bool IsZero(Thickness t)
        {
            return t.Left == 0 && t.Top == 0 && t.Right == 0 && t.Bottom == 0;
        }

        private static Thickness GetSafeThickness(Thickness? t)
        {
            if (t == null) return new Thickness(0);
            return new Thickness(
                EnsureValid(t.Value.Left),
                EnsureValid(t.Value.Top),
                EnsureValid(t.Value.Right),
                EnsureValid(t.Value.Bottom));
        }

        private static Thickness SafeThickness(Thickness? source, Thickness defaultVal)
        {
            if (source == null) return defaultVal;
            return GetSafeThickness(source.Value);
        }

        private static double EnsureDimension(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v < 0) return 0;
            return v;
        }

        private static Thickness EnsureThickness(Thickness t)
        {
            return new Thickness(
                EnsureDimension(t.Left),
                EnsureDimension(t.Top),
                EnsureDimension(t.Right),
                EnsureDimension(t.Bottom));
        }

        public VirtualizingRenderer(RenderObject root, Uri baseUri, Action<Uri> onNavigate)
        {
            _root = root;
            _baseUri = baseUri;
            _onNavigate = onNavigate;

            _canvas = new Canvas();
            double cw = 0, ch = 0;
            if (_root != null)
            {
                cw = EnsureValid(_root.Bounds.Width);
                ch = EnsureValid(_root.Bounds.Height);
                _canvas.Width = cw;
                _canvas.Height = ch;
            }

            _scrollViewer = new ScrollViewer
            {
                Content = _canvas,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            _scrollViewer.ViewChanged += OnViewChanged;
            _scrollViewer.SizeChanged += OnSizeChanged;

            System.Diagnostics.Debug.WriteLine("[DIAG] VirtualizingRenderer canvas=" + cw + "x" + ch + " rootChildren=" + (_root != null && _root.Children != null ? _root.Children.Count.ToString() : "0"));

            UpdateView();
        }

        public FrameworkElement GetRootElement()
        {
            return _scrollViewer;
        }

        private void OnViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            UpdateView();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateView();
        }

        public void UpdateView()
        {
            if (_root == null) return;

            double horizontalOffset = _scrollViewer.HorizontalOffset;
            double verticalOffset = _scrollViewer.VerticalOffset;
            double viewportWidth = _scrollViewer.ViewportWidth;
            double viewportHeight = _scrollViewer.ViewportHeight;

            if (viewportWidth == 0) viewportWidth = _scrollViewer.ActualWidth;
            if (viewportHeight == 0) viewportHeight = _scrollViewer.ActualHeight;
            if (viewportWidth == 0) viewportWidth = 800;
            if (viewportHeight == 0) viewportHeight = 600;

            double buffer = 200;
            horizontalOffset = EnsureValid(horizontalOffset);
            verticalOffset = EnsureValid(verticalOffset);
            viewportWidth = EnsureValid(viewportWidth);
            viewportHeight = EnsureValid(viewportHeight);

            var visibleRect = new Rect(
                horizontalOffset - buffer,
                verticalOffset - buffer,
                viewportWidth + 2 * buffer,
                viewportHeight + 2 * buffer);

            // Walk tree to find newly visible nodes
            var newVisible = new HashSet<RenderObject>();
            CollectVisible(_root, visibleRect, 0, 0, newVisible);

            // Diff: remove elements no longer visible, return to pool
            var toRemove = new List<RenderObject>();
            foreach (var kv in _activeElements)
            {
                if (!newVisible.Contains(kv.Key))
                    toRemove.Add(kv.Key);
            }
            for (int i = 0; i < toRemove.Count; i++)
            {
                var node = toRemove[i];
                // Cancel any pending lazy image load before recycling
                if (_lazyImages.TryGetValue(node, out var lazyImg))
                {
                    lazyImg.Source = null;
                    _lazyImages.Remove(node);
                }
                if (_activeElements.TryGetValue(node, out var el))
                {
                    // Remove from correct canvas (overflow inner or main)
                    if (_overflowParents.TryGetValue(node, out var ovp) && _overflowCanvases.TryGetValue(ovp, out var ovc))
                        ovc.Children.Remove(el);
                    else
                        _canvas.Children.Remove(el);
                    _activeElements.Remove(node);
                    ReturnToPool(el);
                }
            }

            // Place all visible nodes on canvas
            foreach (var node in newVisible)
            {
                if (!_activeElements.ContainsKey(node))
                    PlaceVisualOnCanvas(node);
            }

            // Load / cancel images based on final visible set
            ProcessLazyImages(newVisible);

            // Reposition sticky elements
            foreach (var node in _stickyElements)
            {
                if (!_activeElements.TryGetValue(node, out var el)) continue;
                if (!_stickyOriginalY.TryGetValue(node, out var origY)) continue;
                if (!_stickyTop.TryGetValue(node, out var stickyTop)) continue;

                double targetY = Math.Max(origY, verticalOffset + stickyTop);
                if (Math.Abs(Canvas.GetTop(el) - targetY) > 0.5)
                {
                    Canvas.SetTop(el, EnsureValid(targetY));
                    if (el is FrameworkElement feSticky)
                        feSticky.Width = EnsureValid(node.Bounds.Width);
                }
            }

            // Update canvas size
            if (_root != null)
            {
                _canvas.Width = EnsureValid(_root.Bounds.Width);
                _canvas.Height = EnsureValid(_root.Bounds.Height);
            }
        }

        private void CollectVisible(RenderObject node, Rect visibleRect, double parentX, double parentY, HashSet<RenderObject> result)
        {
            if (node == null) return;

            double absX, absY;
            bool isAbsFixed = node.Style != null &&
                (node.Style.Position == "absolute" || node.Style.Position == "fixed");

            if (isAbsFixed && node.Style.Position == "fixed")
            {
                absX = EnsureValid(node.Bounds.X);
                absY = EnsureValid(node.Bounds.Y);
            }
            else if (isAbsFixed)
            {
                // Walk up to nearest positioned ancestor
                absX = EnsureValid(node.Bounds.X);
                absY = EnsureValid(node.Bounds.Y);
                var p = node.Parent;
                while (p != null)
                {
                    absX += p.Bounds.X;
                    absY += p.Bounds.Y;
                    if (p.Style != null &&
                        (p.Style.Position == "relative" || p.Style.Position == "absolute" ||
                         p.Style.Position == "fixed" || p.Style.Position == "sticky"))
                        break;
                    p = p.Parent;
                }
            }
            else
            {
                absX = parentX + node.Bounds.X;
                absY = parentY + node.Bounds.Y;
            }

            var nodeRect = new Rect(
                EnsureValid(absX),
                EnsureValid(absY),
                EnsureValid(node.Bounds.Width),
                EnsureValid(node.Bounds.Height));

            bool isVisible = RectHelper.Intersect(visibleRect, nodeRect) != Rect.Empty;
            if (isVisible)
                result.Add(node);

            var tag = node.Node?.Tag?.ToUpperInvariant();
            bool isLeafControl = tag == "BUTTON" || tag == "INPUT" || tag == "IMG" || tag == "SELECT" || tag == "TEXTAREA";

            if (!isLeafControl && node.Children != null)
            {
                var overflow = node.Style?.Overflow ?? "";
                bool isHidden = string.Equals(overflow, "hidden", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(overflow, "clip", StringComparison.OrdinalIgnoreCase);
                bool isAuto = string.Equals(overflow, "auto", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(overflow, "scroll", StringComparison.OrdinalIgnoreCase);

                var childVisibleRect = visibleRect;
                if (isHidden && node.Bounds.Width > 0 && node.Bounds.Height > 0)
                {
                    childVisibleRect = RectHelper.Intersect(visibleRect, nodeRect);
                }
                // overflow:auto/scroll → DON'T clip children; inner ScrollViewer handles it
                // (children go on inner Canvas, positioned relative to container)

                for (int i = 0; i < node.Children.Count; i++)
                    CollectVisible(node.Children[i], childVisibleRect, absX, absY, result);
            }
        }

        private static bool HasOverflowVisible(RenderObject node)
        {
            return node.Style != null &&
                   string.Equals(node.Style.Overflow, "visible", StringComparison.OrdinalIgnoreCase);
        }

        private UIElement GetOrCreateVisual(RenderObject node)
        {
            var typeKey = GetVisualTypeKey(node);
            UIElement element = null;

            // Try pool first
            if (typeKey != null && _pools.TryGetValue(typeKey, out var stack) && stack.Count > 0)
                element = stack.Pop();

            if (element == null)
                element = CreateVisual(node);

            return element;
        }

        private Type GetVisualTypeKey(RenderObject node)
        {
            if (node is RenderText) return typeof(TextBlock);
            if (node is RenderBox box)
            {
                var tag = box.Node?.Tag?.ToUpperInvariant();
                if (tag == "IMG") return typeof(Grid);
                if (tag == "SELECT") return typeof(ComboBox);
                if (tag == "INPUT")
                {
                    var type = box.Node.Attr != null && box.Node.Attr.ContainsKey("type")
                        ? box.Node.Attr["type"].ToLowerInvariant() : "text";
                    if (type == "submit" || type == "button" || type == "reset") return typeof(Button);
                    if (type == "checkbox") return typeof(CheckBox);
                    if (type == "radio") return typeof(RadioButton);
                    return typeof(TextBox);
                }
                if (tag == "BUTTON") return typeof(Button);
                if (tag == "A") return typeof(Border);

                if (HasBorderOrBackground(box)) return typeof(Border);
                return null; // no visual needed
            }
            return null;
        }

        public void RemoveVisualFor(RenderObject node)
        {
            if (node == null) return;
            RemoveSubtreeVisuals(node);
            UpdateView();
        }

        public void AddVisualFor(RenderObject node)
        {
            if (node == null) return;
            UpdateView();
        }

        public void UpdateCanvasSize()
        {
            if (_root != null)
            {
                _canvas.Width = EnsureValid(_root.Bounds.Width);
                _canvas.Height = EnsureValid(_root.Bounds.Height);
            }
        }

        /// <summary>
        /// Incremental patch: apply only the changed nodes to the canvas.
        /// Much cheaper than full UpdateView when only a few nodes changed.
        /// </summary>
        public void PatchAdded(RenderObject subtreeRoot)
        {
            if (subtreeRoot == null) return;
            // Only patch if the node is in the current viewport
            var viewport = GetViewportRect();
            var newlyVisible = new HashSet<RenderObject>();
            CollectVisible(subtreeRoot, viewport, 0, 0, newlyVisible);

            foreach (var node in newlyVisible)
            {
                if (_activeElements.ContainsKey(node)) continue;
                PlaceVisualOnCanvas(node);
            }
        }

        /// <summary>
        /// Patch style/attribute changes on an existing node.
        /// Updates the visual in-place without recreating it.
        /// </summary>
        public void PatchStyle(RenderObject node)
        {
            if (node == null) return;
            UIElement el;
            if (!_activeElements.TryGetValue(node, out el)) return;

            // Update bounds
            if (el is FrameworkElement fe)
            {
                fe.Width = EnsureValid(node.Bounds.Width);
                fe.Height = EnsureValid(node.Bounds.Height);
            }

            // Update position
            double ax = 0, ay = 0;
            var cur = node;
            while (cur != null)
            {
                ax += cur.Bounds.X;
                ay += cur.Bounds.Y;
                cur = cur.Parent;
            }
            Canvas.SetLeft(el, EnsureValid(ax));
            Canvas.SetTop(el, EnsureValid(ay));

            // Update style-dependent properties
            ApplyStyleToVisual(el, node);
        }

        private void PlaceVisualOnCanvas(RenderObject node)
        {
            var visual = GetOrCreateVisual(node);
            if (visual == null) return;

            double ax = 0, ay = 0;
            var cur = node;
            bool isAbsFixed = node.Style != null &&
                (node.Style.Position == "absolute" || node.Style.Position == "fixed");

            if (isAbsFixed && node.Style.Position == "fixed")
            {
                // Fixed elements position relative to viewport
                ax = EnsureValid(node.Bounds.X);
                ay = EnsureValid(node.Bounds.Y);
            }
            else if (isAbsFixed)
            {
                // Absolute elements: walk up to nearest positioned ancestor
                cur = node.Parent;
                while (cur != null)
                {
                    ax += cur.Bounds.X;
                    ay += cur.Bounds.Y;
                    if (cur.Style != null &&
                        (cur.Style.Position == "relative" || cur.Style.Position == "absolute" ||
                         cur.Style.Position == "fixed" || cur.Style.Position == "sticky"))
                        break;
                    cur = cur.Parent;
                }
                ax += EnsureValid(node.Bounds.X);
                ay += EnsureValid(node.Bounds.Y);
            }
            else
            {
                cur = node;
                while (cur != null)
                {
                    ax += cur.Bounds.X;
                    ay += cur.Bounds.Y;
                    cur = cur.Parent;
                }
            }

            if (visual is FrameworkElement fe)
            {
                var tag = (node as RenderBox)?.Node?.Tag?.ToUpperInvariant();
                if (tag != "SVG")
                {
                    fe.Width = EnsureValid(node.Bounds.Width);
                    if (!(visual is TextBlock))
                        fe.Height = EnsureValid(node.Bounds.Height);
                }
            }
            Canvas.SetLeft(visual, EnsureValid(ax));
            Canvas.SetTop(visual, EnsureValid(ay));

            // Check if this node's parent is an overflow:auto/scroll container
            RenderObject overflowParent = null;
            if (node.Parent != null && _overflowCanvases.ContainsKey(node.Parent))
                overflowParent = node.Parent;
            else if (node.Parent != null)
            {
                // Walk up to find overflow parent
                var p = node.Parent;
                while (p != null)
                {
                    if (_overflowCanvases.ContainsKey(p))
                    {
                        overflowParent = p;
                        break;
                    }
                    p = p.Parent;
                }
            }

            if (overflowParent != null && _overflowCanvases.TryGetValue(overflowParent, out var innerCanvas))
            {
                // Place inside overflow container's inner Canvas (coordinates relative to container)
                double relX = ax, relY = ay;
                var walk = node.Parent;
                while (walk != null && walk != overflowParent)
                {
                    relX -= walk.Bounds.X;
                    relY -= walk.Bounds.Y;
                    walk = walk.Parent;
                }
                Canvas.SetLeft(visual, EnsureValid(relX));
                Canvas.SetTop(visual, EnsureValid(relY));
                innerCanvas.Children.Add(visual);
                _overflowParents[node] = overflowParent;
            }
            else
            {
                _canvas.Children.Add(visual);
            }

            _activeElements[node] = visual;

            // Track sticky elements
            var pos = node.Style?.Position;
            if (string.Equals(pos, "sticky", StringComparison.OrdinalIgnoreCase))
            {
                _stickyElements.Add(node);
                _stickyOriginalY[node] = ay;
                double topVal = 0;
                if (node.Style?.Top.HasValue == true) topVal = node.Style.Top.Value;
                _stickyTop[node] = topVal;
            }
        }

        private void ApplyStyleToVisual(UIElement el, RenderObject node)
        {
            var style = node.Style;
            if (style == null) return;

            if (el is TextBlock tb)
            {
                if (style.FontSize.HasValue) tb.FontSize = style.FontSize.Value;
                if (style.ForegroundColor.HasValue) tb.Foreground = new SolidColorBrush(style.ForegroundColor.Value);
                if (!string.IsNullOrEmpty(style.FontFamilyName)) tb.FontFamily = new FontFamily(style.FontFamilyName);
                if (style.FontWeight.HasValue) tb.FontWeight = style.FontWeight.Value;
            }
            else if (el is Border b)
            {
                if (style.Background != null) b.Background = style.Background;
                else if (style.BackgroundColor.HasValue) b.Background = new SolidColorBrush(style.BackgroundColor.Value);
                if (style.BorderBrush != null) b.BorderBrush = style.BorderBrush;
                else if (style.BorderBrushColor.HasValue) b.BorderBrush = new SolidColorBrush(style.BorderBrushColor.Value);
                if (style.BorderThickness != default(Thickness)) b.BorderThickness = style.BorderThickness;
                if (style.BorderRadius != default(CornerRadius)) b.CornerRadius = style.BorderRadius;
            }
            else if (el is Button btn)
            {
                if (style.Background != null) btn.Background = style.Background;
                if (style.ForegroundColor.HasValue) btn.Foreground = new SolidColorBrush(style.ForegroundColor.Value);
                if (style.BorderBrush != null) btn.BorderBrush = style.BorderBrush;
                if (style.BorderThickness != default(Thickness)) btn.BorderThickness = style.BorderThickness;
                if (style.FontSize.HasValue) btn.FontSize = style.FontSize.Value;
            }
            else if (el is TextBox tbx)
            {
                if (style.Background != null) tbx.Background = style.Background;
                if (style.ForegroundColor.HasValue) tbx.Foreground = new SolidColorBrush(style.ForegroundColor.Value);
                if (style.BorderBrush != null) tbx.BorderBrush = style.BorderBrush;
                if (style.BorderThickness != default(Thickness)) tbx.BorderThickness = style.BorderThickness;
                if (style.FontSize.HasValue) tbx.FontSize = style.FontSize.Value;
            }
            else if (el is Grid g)
            {
                if (style.BackgroundColor.HasValue) g.Background = new SolidColorBrush(style.BackgroundColor.Value);
            }
        }

        private Rect GetViewportRect()
        {
            double horizontalOffset = _scrollViewer.HorizontalOffset;
            double verticalOffset = _scrollViewer.VerticalOffset;
            double viewportWidth = _scrollViewer.ViewportWidth;
            double viewportHeight = _scrollViewer.ViewportHeight;

            if (viewportWidth == 0) viewportWidth = _scrollViewer.ActualWidth;
            if (viewportHeight == 0) viewportHeight = _scrollViewer.ActualHeight;
            if (viewportWidth == 0) viewportWidth = 800;
            if (viewportHeight == 0) viewportHeight = 600;

            double buffer = 200;
            return new Rect(
                EnsureValid(horizontalOffset - buffer),
                EnsureValid(verticalOffset - buffer),
                EnsureValid(viewportWidth + 2 * buffer),
                EnsureValid(viewportHeight + 2 * buffer));
        }

        private void RemoveSubtreeVisuals(RenderObject node)
        {
            if (node == null) return;
            if (_activeElements.TryGetValue(node, out var el))
            {
                if (_lazyImages.TryGetValue(node, out var lazyImg))
                {
                    lazyImg.Source = null;
                    _lazyImages.Remove(node);
                }
                // Remove from correct canvas (overflow inner or main)
                if (_overflowParents.TryGetValue(node, out var ovp) && _overflowCanvases.TryGetValue(ovp, out var ovc))
                    ovc.Children.Remove(el);
                else
                    _canvas.Children.Remove(el);
                _activeElements.Remove(node);
                ReturnToPool(el);
                _overflowParents.Remove(node);
            }
            if (node.Children != null)
            {
                for (int i = 0; i < node.Children.Count; i++)
                    RemoveSubtreeVisuals(node.Children[i]);
            }
            // Clean up overflow tracking if this was an overflow container
            if (_overflowCanvases.TryGetValue(node, out var innerCv))
            {
                innerCv.Children.Clear();
                _overflowCanvases.Remove(node);
            }
        }

        private void ReturnToPool(UIElement element)
        {
            // Remove any Tapped link handler that was attached
            if (_linkHandlerRefs.TryGetValue(element, out var linkHandler))
            {
                element.Tapped -= (TappedEventHandler)linkHandler;
                _linkHandlerRefs.Remove(element);
                _linkHandledElements.Remove(element);
            }

            var t = element.GetType();
            if (!_pools.TryGetValue(t, out var stack))
            {
                stack = new Stack<UIElement>();
                _pools[t] = stack;
            }
            // Reset state that would be stale on reuse
            if (element is Image img)
            {
                img.Source = null;
            }
            else if (element is TextBlock tb)
            {
                tb.Text = "";
                tb.Inlines.Clear();
            }
            else if (element is Grid g)
            {
                g.Children.Clear();
                g.RowDefinitions.Clear();
                g.ColumnDefinitions.Clear();
                g.Background = null;
            }
            else if (element is Border b)
            {
                b.Child = null;
                b.Background = null;
                b.BorderBrush = null;
                b.BorderThickness = new Thickness(0);
                b.CornerRadius = new CornerRadius(0);
            }
            else if (element is TextBox tbx)
            {
                tbx.Text = "";
            }
            else if (element is Button btn)
            {
                btn.Content = null;
            }
            stack.Push(element);
        }

        private void ProcessLazyImages(HashSet<RenderObject> visible)
        {
            foreach (var kv in _lazyImages)
            {
                var node = kv.Key;
                var img = kv.Value;
                bool isVisible = visible.Contains(node);
                if (isVisible && img.Source == null)
                {
                    if (!(node is RenderBox box)) continue;
                    var src = box.Node?.Attr != null && box.Node.Attr.ContainsKey("src")
                        ? box.Node.Attr["src"] : null;
                    // Try srcset if src is missing
                    if (string.IsNullOrEmpty(src) && box.Node?.Attr != null && box.Node.Attr.ContainsKey("srcset"))
                    {
                        src = PickBestSrcsetUrl(box.Node.Attr["srcset"], box.Bounds.Width);
                    }
                    if (string.IsNullOrEmpty(src))
                    {
                        DevToolsLogger.Log("[DIAG:IMG] SKIP node=" + (box.Node?.Tag ?? "null") + " no src attr");
                        continue;
                    }
                    DevToolsLogger.Log("[DIAG:IMG] LOAD src=" + src.Substring(0, Math.Min(src.Length, 80)));
                    if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        LoadDataUriAsync(img, src);
                    else
                    {
                        var uri = ResolveUri(_baseUri, src);
                        if (uri != null)
                        {
                            try
                            {
                                // Always try BitmapImage first — thumb URLs with .svg in path
                                // return rasterized PNG/JPEG, not SVG. SvgImageSource on
                                // raster data causes black squares.
                                var isSvg = src.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
                                if (isSvg)
                                {
                                    // Store uri for fallback in ImageFailed handler
                                    _pendingSvgFallback[img] = uri;
                                    img.ImageFailed += OnSvgImageFailed;
                                }
                                img.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(uri);
                            }
                            catch (Exception ex)
                            {
                                DevToolsLogger.Log("[DIAG:IMG] FAIL src=" + src + " err=" + ex.Message);
                            }
                        }
                    }
                }
                else if (!isVisible && img.Source != null)
                {
                    img.Source = null;
                }
            }
        }

        private readonly Dictionary<Image, Uri> _pendingSvgFallback = new Dictionary<Image, Uri>();

        private void OnSvgImageFailed(object sender, Windows.UI.Xaml.ExceptionRoutedEventArgs e)
        {
            var img = sender as Image;
            if (img == null) return;
            if (!_pendingSvgFallback.TryGetValue(img, out var uri)) return;
            _pendingSvgFallback.Remove(img);
            img.ImageFailed -= OnSvgImageFailed;
            DevToolsLogger.Log("[DIAG:IMG:SVG_FALLBACK] BitmapImage failed for .svg, trying SvgImageSource uri=" + uri);
            try
            {
                img.Source = new Windows.UI.Xaml.Media.Imaging.SvgImageSource(uri);
                img.ImageFailed += (s2, e2) =>
                {
                    // Both failed — replace with alt text placeholder
                    DevToolsLogger.Log("[DIAG:IMG:SVG_FALLBACK] SvgImageSource also failed, showing placeholder");
                    var parent = img.Parent as Panel;
                    if (parent != null)
                    {
                        string alt = "";
                        if (img.Tag is string s) alt = s;
                        var placeholder = new TextBlock
                        {
                            Text = string.IsNullOrEmpty(alt) ? "\u25A0" : alt,
                            FontSize = 10,
                            Foreground = new SolidColorBrush(Color.FromArgb(100, 150, 150, 150)),
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center
                        };
                        parent.Children.Remove(img);
                        parent.Children.Add(placeholder);
                    }
                };
            }
            catch (Exception ex)
            {
                DevToolsLogger.Log("[DIAG:IMG:SVG_FALLBACK] SvgImageSource also failed: " + ex.Message);
            }
        }

        private UIElement CreateVisual(RenderObject node)
        {
            if (node is RenderBox box)
            {
                return CreateBoxVisual(box);
            }
            else if (node is RenderText textNode)
            {
                return CreateTextVisual(textNode);
            }
            return null;
        }

        private UIElement CreateTextVisual(RenderText textNode)
        {
            var style = textNode.Style ?? textNode.Parent?.Style;
            var margin = style?.Margin ?? new Thickness(0);
            var padding = style?.Padding ?? new Thickness(0);

            // Ensure margin/padding are safe (no NaN/Infinity)
            margin = new Thickness(
                EnsureValid(margin.Left),
                EnsureValid(margin.Top),
                EnsureValid(margin.Right),
                EnsureValid(margin.Bottom));
            padding = new Thickness(
                EnsureValid(padding.Left),
                EnsureValid(padding.Top),
                EnsureValid(padding.Right),
                EnsureValid(padding.Bottom));
            
            var tb = new TextBlock
            {
                Text = textNode.Text,
                FontSize = style?.FontSize ?? 16,
                Foreground = style?.Foreground ?? new SolidColorBrush(Windows.UI.Colors.Black),
                FontFamily = style?.FontFamily ?? new FontFamily("Segoe UI"),
                FontWeight = style?.FontWeight ?? Windows.UI.Text.FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap
            };

            // Apply white-space
            var ws = style?.WhiteSpace ?? "";
            if (string.Equals(ws, "nowrap", StringComparison.OrdinalIgnoreCase))
                tb.TextWrapping = TextWrapping.NoWrap;
            else if (string.Equals(ws, "pre", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(ws, "pre-wrap", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(ws, "pre-line", StringComparison.OrdinalIgnoreCase))
            {
                tb.TextWrapping = string.Equals(ws, "nowrap", StringComparison.OrdinalIgnoreCase)
                    ? TextWrapping.NoWrap : TextWrapping.Wrap;
                tb.FontFamily = new FontFamily("Consolas");
            }
            else if (string.Equals(ws, "normal", StringComparison.OrdinalIgnoreCase))
                tb.TextWrapping = TextWrapping.Wrap;

            // Apply line-height
            if (style?.LineHeight.HasValue == true && style.LineHeight.Value > 0)
                tb.LineHeight = style.LineHeight.Value;

            // Apply text-overflow: ellipsis (check parent box too)
            var parentStyle = textNode.Parent?.Style;
            var textOverflow = style?.TextOverflow ?? parentStyle?.TextOverflow ?? "";
            if (string.Equals(textOverflow, "ellipsis", StringComparison.OrdinalIgnoreCase))
            {
                tb.TextTrimming = Windows.UI.Xaml.TextTrimming.CharacterEllipsis;
                tb.TextWrapping = TextWrapping.NoWrap;
            }

            // Apply text-decoration (underline, line-through)
            if (style?.TextDecoration != null)
            {
                if (style.TextDecoration.Contains("underline"))
                {
                    tb.Text = "";
                    var u = new Windows.UI.Xaml.Documents.Underline();
                    u.Inlines.Add(new Windows.UI.Xaml.Documents.Run { Text = textNode.Text });
                    tb.Inlines.Add(u);
                }
                if (style.TextDecoration.Contains("line-through"))
                {
                    // UWP doesn't support line-through directly, skip for now
                }
            }

            // Wrap in Border if padding is needed (TextBlock doesn't support Padding)
            if (!IsZero(padding) || !IsZero(margin))
            {
                var border = new Border
                {
                    Margin = margin,
                    Padding = padding,
                    Child = tb
                };
                AttachLinkHandler(border, textNode);
                return border;
            }

            if (textNode.Bounds.Width > 0) tb.Width = textNode.Bounds.Width;

            AttachLinkHandler(tb, textNode);
            return tb;
        }

        private UIElement CreateBoxVisual(RenderBox box)
        {
            var tag = box.Node?.Tag?.ToUpperInvariant();

            if (tag == "SVG")
            {
                // SVG→XAML bridge abandoned — content doubling unfixable.
                // Return empty placeholder to avoid crashing the layout.
                return new Windows.UI.Xaml.Controls.Canvas { Width = 1, Height = 1 };
            }
            if (tag == "IMG") return CreateImageVisual(box);
            if (tag == "IFRAME") return CreateIframePlaceholder(box);
            if (tag == "SELECT") return CreateSelectVisual(box);
            if (tag == "INPUT") return CreateInputVisual(box);
            if (tag == "BUTTON") return CreateButtonVisual(box);

            // Create visual for ALL elements, not just those with border/background
            var bg = box.Style?.Background ?? (box.Style?.BackgroundColor.HasValue == true ? new SolidColorBrush(box.Style.BackgroundColor.Value) : null);
            var borderBrush = box.Style?.BorderBrush ?? (box.Style?.BorderBrushColor.HasValue == true ? new SolidColorBrush(box.Style.BorderBrushColor.Value) : null);
            var borderThick = GetSafeThickness(box.Style?.BorderThickness);
            var margin = GetSafeThickness(box.Style?.Margin);
            var padding = GetSafeThickness(box.Style?.Padding);

            // Detect overflow:auto/scroll for nested scrolling
            var overflowVal = box.Style?.Overflow ?? "";
            var isOverflowAuto = string.Equals(overflowVal, "auto", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(overflowVal, "scroll", StringComparison.OrdinalIgnoreCase);

            // Only create Border if there's something to style
            if (HasBorderOrBackground(box) || tag == "A" || !IsZero(margin) || !IsZero(padding) || isOverflowAuto)
            {
                var border = new Border
                {
                    Width = EnsureValid(box.Bounds.Width),
                    Height = EnsureValid(box.Bounds.Height),
                    Background = bg,
                    BorderBrush = borderBrush,
                    BorderThickness = borderThick,
                    CornerRadius = box.Style?.BorderRadius ?? new CornerRadius(0),
                    Margin = margin,
                    Padding = padding
                };

                // overflow:auto/scroll → wrap in ScrollViewer with inner Canvas for children
                if (isOverflowAuto)
                {
                    border.Clip = new RectangleGeometry
                    {
                        Rect = new Rect(0, 0, EnsureValid(box.Bounds.Width), EnsureValid(box.Bounds.Height))
                    };
                    var innerCanvas = new Canvas
                    {
                        Width = EnsureValid(box.Bounds.Width),
                        Height = EnsureValid(box.Bounds.Height)
                    };
                    _overflowCanvases[box] = innerCanvas;
                    var scroller = new ScrollViewer
                    {
                        Content = innerCanvas,
                        Width = EnsureValid(box.Bounds.Width),
                        Height = EnsureValid(box.Bounds.Height),
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
                    };
                    border.Child = scroller;
                }

                AttachLinkHandler(border, box);
                return border;
            }

            return null;
        }

        private UIElement CreateImageVisual(RenderBox box)
        {
            var src = box.Node?.Attr != null && box.Node.Attr.ContainsKey("src")
                ? box.Node.Attr["src"] : "(none)";
            var srcset = box.Node?.Attr != null && box.Node.Attr.ContainsKey("srcset")
                ? box.Node.Attr["srcset"] : "";
            DevToolsLogger.Log("[DIAG:IMG:CREATE] src=" + (src?.Length > 60 ? src.Substring(0, 60) : src) + " srcset=" + (srcset?.Length > 40 ? srcset.Substring(0, 40) : srcset) + " w=" + box.Bounds.Width + " h=" + box.Bounds.Height);

            var grid = new Grid
            {
                Width = EnsureValid(box.Bounds.Width),
                Height = EnsureValid(box.Bounds.Height)
            };

            var altText = box.Node.Attr != null && box.Node.Attr.ContainsKey("alt")
                ? box.Node.Attr["alt"] : "Image";
            var tbAlt = new TextBlock
            {
                Text = altText,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Windows.UI.Colors.Gray),
                FontSize = 12
            };
            grid.Children.Add(tbAlt);

            var img = new Image
            {
                Width = EnsureValid(box.Bounds.Width),
                Height = EnsureValid(box.Bounds.Height),
                Stretch = Stretch.Uniform,
                Tag = altText
            };

            // Track image load failures for diagnostics
            img.ImageFailed += (s, e) =>
            {
                DevToolsLogger.Log("[DIAG:IMG:FAILED] src=" + (src?.Length > 80 ? src.Substring(0, 80) : src) + " err=" + e.ErrorMessage);
            };

            // Defer actual Source loading until node is confirmed visible (ProcessLazyImages)
            _lazyImages[box] = img;

            grid.Children.Add(img);
            return grid;
        }

        private UIElement CreateIframePlaceholder(RenderBox box)
        {
            var src = box.Node?.Attr != null && box.Node.Attr.ContainsKey("src")
                ? box.Node.Attr["src"] : "";
            var title = box.Node?.Attr != null && box.Node.Attr.ContainsKey("title")
                ? box.Node.Attr["title"] : "";
            var w = EnsureValid(box.Bounds.Width);
            var h = EnsureValid(box.Bounds.Height);
            if (w <= 0) w = 300;
            if (h <= 0) h = 200;

            // Build label: title + truncated URL
            var label = "[embedded content]";
            if (!string.IsNullOrEmpty(title))
                label = title;
            else if (!string.IsNullOrEmpty(src))
                label = src.Length > 60 ? src.Substring(0, 57) + "..." : src;

            var tb = new TextBlock
            {
                Text = label,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 80, 80, 160)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(8, 8, 8, 4),
                MaxHeight = h - 16
            };

            var subText = new TextBlock
            {
                Text = "Tap to open in browser",
                Foreground = new SolidColorBrush(Windows.UI.Colors.Gray),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };

            var panel = new StackPanel { Orientation = Orientation.Vertical };
            panel.Children.Add(tb);
            panel.Children.Add(subText);

            var border = new Border
            {
                Width = w,
                Height = h,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 245, 250)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 200)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = panel
            };

            // Make tappable to navigate to iframe src
            if (!string.IsNullOrEmpty(src))
            {
                border.IsTapEnabled = true;
                border.IsHoldingEnabled = true;
                var resolvedUri = ResolveUri(_baseUri, src);
                border.Tapped += (s, e) =>
                {
                    if (resolvedUri != null) _onNavigate?.Invoke(resolvedUri);
                };
                border.Holding += (s, e) =>
                {
                    if (resolvedUri != null) _onNavigate?.Invoke(resolvedUri);
                };
            }

            return border;
        }

        private UIElement CreateSelectVisual(RenderBox box)
        {
            var isDisabled = box.Node?.Attr != null &&
                (box.Node.Attr.ContainsKey("disabled") ||
                 (box.Node.Attr.ContainsKey("class") && box.Node.Attr["class"].IndexOf("disabled", StringComparison.OrdinalIgnoreCase) >= 0));

            var cb = new ComboBox
            {
                Width = EnsureValid(box.Bounds.Width),
                Height = Math.Max(EnsureValid(box.Bounds.Height), 32),
                FontSize = box.Style?.FontSize ?? 14,
                FontWeight = box.Style?.FontWeight ?? Windows.UI.Text.FontWeights.Normal,
                Background = box.Style?.Background ?? new SolidColorBrush(Windows.UI.Colors.White),
                Foreground = box.Style?.Foreground ?? new SolidColorBrush(Windows.UI.Colors.Black),
                BorderBrush = box.Style?.BorderBrush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
                BorderThickness = SafeThickness(box.Style?.BorderThickness, new Thickness(1)),
                Padding = SafeThickness(box.Style?.Padding, new Thickness(8, 6, 8, 6)),
                IsEnabled = !isDisabled
            };

            string selectedValue = null;
            if (box.Node?.Attr != null && box.Node.Attr.ContainsKey("value"))
                selectedValue = box.Node.Attr["value"];

            if (box.Children != null)
            {
                int idx = 0;
                int selectedIdx = -1;
                foreach (var child in box.Children)
                {
                    if (child.Node != null && string.Equals(child.Node.Tag, "OPTION", StringComparison.OrdinalIgnoreCase))
                    {
                        var text = child.Node.Text ?? "";
                        if (string.IsNullOrEmpty(text) && child.Children.Count > 0 && child.Children[0] is RenderText rt)
                            text = rt.Text;
                        cb.Items.Add(text);
                        bool isSelected = child.Node.Attr != null && child.Node.Attr.ContainsKey("selected");
                        if (!isSelected && selectedValue != null && child.Node.Attr != null &&
                            child.Node.Attr.ContainsKey("value") && child.Node.Attr["value"] == selectedValue)
                            isSelected = true;
                        if (isSelected) selectedIdx = idx;
                        idx++;
                    }
                }
                if (selectedIdx >= 0) cb.SelectedIndex = selectedIdx;
            }
            if (cb.SelectedIndex < 0 && cb.Items.Count > 0) cb.SelectedIndex = 0;
            return cb;
        }

        private UIElement CreateInputVisual(RenderBox box)
        {
            var type = box.Node.Attr != null && box.Node.Attr.ContainsKey("type")
                ? box.Node.Attr["type"].ToLowerInvariant() : "text";
            System.Diagnostics.Debug.WriteLine("[DIAG:INPUT] type=" + type + " w=" + box.Bounds.Width + " h=" + box.Bounds.Height + " placeholder=" + (box.Node.Attr?.ContainsKey("placeholder") == true ? box.Node.Attr["placeholder"] : "none"));

            if (type == "submit" || type == "button" || type == "reset")
            {
                var btn = new Button
                {
                    Content = box.Node.Attr != null && box.Node.Attr.ContainsKey("value") ? box.Node.Attr["value"] : "Submit",
                    Width = EnsureValid(box.Bounds.Width),
                    Height = EnsureValid(box.Bounds.Height),
                    Background = box.Style?.Background ?? new SolidColorBrush(Windows.UI.Colors.LightGray),
                    Foreground = box.Style?.Foreground ?? new SolidColorBrush(Windows.UI.Colors.Black),
                    BorderBrush = box.Style?.BorderBrush ?? new SolidColorBrush(Windows.UI.Colors.Gray),
                    BorderThickness = SafeThickness(box.Style?.BorderThickness, new Thickness(1)),
                    Padding = SafeThickness(box.Style?.Padding, new Thickness(4)),
                    FontSize = box.Style?.FontSize ?? 14,
                };

                if (type == "submit")
                {
                    btn.Click += (s, e) =>
                    {
                        try
                        {
                            // Walk up to find FORM parent
                            var formAction = "";
                            var formMethod = "GET";
                            var formEnctype = "application/x-www-form-urlencoded";
                            var cur = box.Parent;
                            while (cur != null)
                            {
                                if (cur.Node?.Tag != null && cur.Node.Tag.Equals("FORM", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (cur.Node.Attr != null)
                                    {
                                        if (cur.Node.Attr.ContainsKey("action")) formAction = cur.Node.Attr["action"];
                                        if (cur.Node.Attr.ContainsKey("method")) formMethod = cur.Node.Attr["method"].ToUpperInvariant();
                                        if (cur.Node.Attr.ContainsKey("enctype")) formEnctype = cur.Node.Attr["enctype"];
                                    }
                                    break;
                                }
                                cur = cur.Parent;
                            }

                            // Collect form data from all inputs in this form
                            var formData = new List<Tuple<string, string>>();
                            CollectFormData(cur, formData, box.Node?.Attr?.ContainsKey("name") == true ? box.Node.Attr["name"] : null, box.Node?.Attr?.ContainsKey("value") == true ? box.Node.Attr["value"] : null);

                            // Build query string
                            var pairs = new List<string>();
                            foreach (var kv in formData)
                            {
                                if (!string.IsNullOrEmpty(kv.Item1))
                                    pairs.Add(Uri.EscapeDataString(kv.Item1) + "=" + Uri.EscapeDataString(kv.Item2 ?? ""));
                            }
                            var queryString = string.Join("&", pairs);

                            // Resolve form action URL
                            string targetUrl = formAction;
                            if (string.IsNullOrEmpty(targetUrl))
                            {
                                targetUrl = _baseUri?.ToString() ?? "";
                            }
                            else
                            {
                                try { targetUrl = new Uri(_baseUri, formAction).ToString(); } catch { }
                            }

                            if (formMethod == "GET" && !string.IsNullOrEmpty(queryString))
                            {
                                var sep = targetUrl.Contains("?") ? "&" : "?";
                                targetUrl += sep + queryString;
                            }

                            DevToolsLogger.Log("[DIAG:FORM] Submit method=" + formMethod + " action=" + targetUrl + " pairs=" + pairs.Count);

                            if (!string.IsNullOrEmpty(targetUrl))
                                _onNavigate?.Invoke(new Uri(targetUrl));
                        }
                        catch (Exception ex)
                        {
                            DevToolsLogger.Log("[DIAG:FORM] Submit ERROR: " + ex.Message);
                        }
                    };
                }

                return btn;
            }
            else if (type == "checkbox")
            {
                var cb = new CheckBox
                {
                    IsChecked = box.Node.Attr != null && box.Node.Attr.ContainsKey("checked"),
                    MinWidth = 20,
                    MinHeight = 20,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                if (box.Bounds.Width > 0) cb.Width = EnsureValid(box.Bounds.Width);
                if (box.Bounds.Height > 0) cb.Height = EnsureValid(box.Bounds.Height);
                return cb;
            }
            else if (type == "radio")
            {
                var rb = new RadioButton
                {
                    IsChecked = box.Node.Attr != null && box.Node.Attr.ContainsKey("checked"),
                    GroupName = box.Node.Attr != null && box.Node.Attr.ContainsKey("name")
                        ? box.Node.Attr["name"] : "",
                    MinWidth = 20,
                    MinHeight = 20,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                if (box.Bounds.Width > 0) rb.Width = EnsureValid(box.Bounds.Width);
                if (box.Bounds.Height > 0) rb.Height = EnsureValid(box.Bounds.Height);
                return rb;
            }
            else
            {
                var isPassword = type == "password";
                var isSearch = type == "search";
                if (isPassword)
                {
                    var pw = new PasswordBox
                    {
                        Width = EnsureValid(box.Bounds.Width),
                        Height = Math.Max(EnsureValid(box.Bounds.Height), 32),
                        FontSize = box.Style?.FontSize ?? 14,
                        Background = box.Style?.Background ?? new SolidColorBrush(Windows.UI.Colors.White),
                        Foreground = box.Style?.Foreground ?? new SolidColorBrush(Windows.UI.Colors.Black),
                        BorderBrush = box.Style?.BorderBrush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
                        BorderThickness = SafeThickness(box.Style?.BorderThickness, new Thickness(1)),
                        Padding = SafeThickness(box.Style?.Padding, new Thickness(8, 6, 8, 6)),
                        PlaceholderText = box.Node.Attr != null && box.Node.Attr.ContainsKey("placeholder") ? box.Node.Attr["placeholder"] : ""
                    };
                    return pw;
                }
                var tb = new TextBox
                {
                    Text = box.Node.Attr != null && box.Node.Attr.ContainsKey("value") ? box.Node.Attr["value"] : "",
                    PlaceholderText = box.Node.Attr != null && box.Node.Attr.ContainsKey("placeholder") ? box.Node.Attr["placeholder"] : "",
                    Width = EnsureValid(box.Bounds.Width),
                    Height = Math.Max(EnsureValid(box.Bounds.Height), 32),
                    FontSize = box.Style?.FontSize ?? 14,
                    Background = box.Style?.Background ?? new SolidColorBrush(Windows.UI.Colors.White),
                    Foreground = box.Style?.Foreground ?? new SolidColorBrush(Windows.UI.Colors.Black),
                    BorderBrush = box.Style?.BorderBrush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
                    BorderThickness = SafeThickness(box.Style?.BorderThickness, new Thickness(1)),
                    Padding = SafeThickness(box.Style?.Padding, new Thickness(8, 6, 8, 6))
                };
                if (isSearch) tb.PlaceholderText = string.IsNullOrEmpty(tb.PlaceholderText) ? "Search" : tb.PlaceholderText;
                if (type == "email") tb.PlaceholderText = string.IsNullOrEmpty(tb.PlaceholderText) ? "email" : tb.PlaceholderText;
                if (type == "tel") tb.PlaceholderText = string.IsNullOrEmpty(tb.PlaceholderText) ? "phone" : tb.PlaceholderText;
                if (type == "url") tb.PlaceholderText = string.IsNullOrEmpty(tb.PlaceholderText) ? "url" : tb.PlaceholderText;
                return tb;
            }
        }

        private UIElement CreateButtonVisual(RenderBox box)
        {
            return new Button
            {
                Content = box.Node.CollectText() ?? "Button",
                Width = EnsureValid(box.Bounds.Width),
                Height = EnsureValid(box.Bounds.Height),
                Background = box.Style?.Background ?? new SolidColorBrush(Windows.UI.Colors.LightGray),
                Foreground = box.Style?.Foreground ?? new SolidColorBrush(Windows.UI.Colors.Black),
                BorderBrush = box.Style?.BorderBrush ?? new SolidColorBrush(Windows.UI.Colors.Gray),
                BorderThickness = SafeThickness(box.Style?.BorderThickness, new Thickness(1)),
                Padding = SafeThickness(box.Style?.Padding, new Thickness(4)),
                FontSize = box.Style?.FontSize ?? 14,
            };
        }

        private static void CollectFormData(RenderObject node, List<Tuple<string, string>> data, string submitName, string submitValue)
        {
            if (node == null) return;

            // Collect from this node if it's an input/select/textarea
            var tag = node.Node?.Tag?.ToUpperInvariant();
            if (tag == "INPUT")
            {
                var name = node.Node?.Attr != null && node.Node.Attr.ContainsKey("name") ? node.Node.Attr["name"] : null;
                var inputType = node.Node?.Attr != null && node.Node.Attr.ContainsKey("type") ? node.Node.Attr["type"].ToLowerInvariant() : "text";
                if (!string.IsNullOrEmpty(name) && inputType != "submit" && inputType != "button" && inputType != "reset")
                {
                    if (inputType == "checkbox" || inputType == "radio")
                    {
                        if (node.Node.Attr.ContainsKey("checked"))
                        {
                            var val = node.Node.Attr.ContainsKey("value") ? node.Node.Attr["value"] : "on";
                            data.Add(Tuple.Create(name, val));
                        }
                    }
                    else
                    {
                        var val = node.Node.Attr.ContainsKey("value") ? node.Node.Attr["value"] : "";
                        data.Add(Tuple.Create(name, val));
                    }
                }
            }
            else if (tag == "SELECT")
            {
                var name = node.Node?.Attr != null && node.Node.Attr.ContainsKey("name") ? node.Node.Attr["name"] : null;
                if (!string.IsNullOrEmpty(name))
                {
                    // Use selected option value
                    var selectedValue = "";
                    if (node.Node.Attr.ContainsKey("value"))
                        selectedValue = node.Node.Attr["value"];
                    data.Add(Tuple.Create(name, selectedValue));
                }
            }
            else if (tag == "TEXTAREA")
            {
                var name = node.Node?.Attr != null && node.Node.Attr.ContainsKey("name") ? node.Node.Attr["name"] : null;
                if (!string.IsNullOrEmpty(name))
                {
                    var val = node.Node?.Text ?? "";
                    data.Add(Tuple.Create(name, val));
                }
            }

            // Recurse into children
            if (node.Children != null)
            {
                for (int i = 0; i < node.Children.Count; i++)
                    CollectFormData(node.Children[i], data, null, null);
            }
        }

        private static bool HasBorderOrBackground(RenderBox box)
        {
            return box.Style?.Background != null ||
                   box.Style?.BackgroundColor.HasValue == true ||
                   (box.Style?.BorderBrush != null && box.Style?.BorderThickness != null &&
                    box.Style.BorderThickness != new Thickness(0));
        }

        private static string PickBestSrcsetUrl(string srcset, double displayWidth)
        {
            if (string.IsNullOrWhiteSpace(srcset)) return null;

            // Parse srcset: "url1 300w, url2 600w, url3 2x" or "url1, url2"
            var entries = srcset.Split(',');
            string bestUrl = null;
            double bestScore = -1;

            foreach (var entry in entries)
            {
                var trimmed = entry.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var url = parts[0];
                double score = 0;
                if (parts.Length > 1)
                {
                    var descriptor = parts[1];
                    if (descriptor.EndsWith("w"))
                    {
                        double w;
                        if (double.TryParse(descriptor.TrimEnd('w'), out w))
                            score = w; // Prefer closest width to display size
                    }
                    else if (descriptor.EndsWith("x"))
                    {
                        double x;
                        if (double.TryParse(descriptor.TrimEnd('x'), out x))
                            score = x * 1000; // Higher density = higher priority
                    }
                }
                else
                {
                    score = 1; // No descriptor = default candidate
                }

                if (bestUrl == null || Math.Abs(score - displayWidth) < Math.Abs(bestScore - displayWidth))
                {
                    bestUrl = url;
                    bestScore = score;
                }
            }
            return bestUrl;
        }

        private void AttachLinkHandler(UIElement element, RenderObject node)
        {
            RenderObject current = node;
            string href = null;
            while (current != null)
            {
                if (current.Node != null &&
                    current.Node.Tag != null &&
                    current.Node.Tag.Equals("A", StringComparison.OrdinalIgnoreCase))
                {
                    if (current.Node.Attr != null && current.Node.Attr.ContainsKey("href"))
                    {
                        href = current.Node.Attr["href"];
                        break;
                    }
                }
                current = current.Parent;
            }

            if (href != null)
            {
                // Prevent duplicate handlers on pooled elements
                if (_linkHandledElements.Contains(element))
                    return;
                _linkHandledElements.Add(element);

                var uri = ResolveUri(_baseUri, href);
                if (uri != null)
                {
                    if (element is Border b && b.Background == null)
                        b.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);

                    string hrefForLog = href;
                    TappedEventHandler handler = (s, e) =>
                    {
                        e.Handled = true;
                        System.Diagnostics.Debug.WriteLine("[DIAG:LINK] Tapped href=" + hrefForLog + " uri=" + uri);
                        DevToolsLogger.Log("[DIAG:LINK] Tapped href=" + hrefForLog + " uri=" + uri);
                        _onNavigate?.Invoke(uri);
                    };
                    _linkHandlerRefs[element] = handler;
                    element.Tapped += handler;
                    DevToolsLogger.Log("[DIAG:LINK] Attached handler to " + element.GetType().Name + " href=" + href + " elemW=" + (element as FrameworkElement)?.Width + " elemH=" + (element as FrameworkElement)?.Height);
                }
            }
        }

        private static double EnsureValid(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            return Math.Max(0, v);
        }

        private static Uri ResolveUri(Uri baseUri, string href)
        {
            if (string.IsNullOrWhiteSpace(href)) return null;
            href = href.Trim();
            if (href.StartsWith("ms-appx:", StringComparison.OrdinalIgnoreCase))
            {
                try { return new Uri(href); } catch { return null; }
            }
            if (href.StartsWith("//"))
            {
                try { return new Uri((baseUri?.Scheme ?? "https") + ":" + href); } catch { return null; }
            }
            Uri abs;
            if (Uri.TryCreate(href, UriKind.Absolute, out abs)) return abs;
            if (baseUri != null)
            {
                try { return new Uri(baseUri, href); } catch { return null; }
            }
            try { return new Uri("ms-appx:///" + href.TrimStart('/')); } catch { return null; }
        }

        private async void LoadDataUriAsync(Image img, string dataUri)
        {
            try
            {
                var commaIndex = dataUri.IndexOf(',');
                if (commaIndex > 5)
                {
                    var base64 = dataUri.Substring(commaIndex + 1);
                    var bytes = Convert.FromBase64String(base64);
                    var stream = new InMemoryRandomAccessStream();
                    await stream.WriteAsync(bytes.AsBuffer());
                    stream.Seek(0);
                    var bmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                    bmp.SetSource(stream);
                    img.Source = bmp;
                }
            }
            catch
            {
                img.Visibility = Visibility.Collapsed;
            }
        }

    }
}
