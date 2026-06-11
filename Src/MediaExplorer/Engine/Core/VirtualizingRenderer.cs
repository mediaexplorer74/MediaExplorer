using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using Windows.UI.Xaml.Markup;
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

            double absX = parentX + node.Bounds.X;
            double absY = parentY + node.Bounds.Y;

            var nodeRect = new Rect(
                EnsureValid(absX),
                EnsureValid(absY),
                EnsureValid(node.Bounds.Width),
                EnsureValid(node.Bounds.Height));

            bool isVisible = RectHelper.Intersect(visibleRect, nodeRect) != Rect.Empty;
            if (isVisible)
                result.Add(node);

            // Skip subtree if node is completely outside viewport
            // (in our layout model, children are within parent bounds)
            if (!isVisible && !HasOverflowVisible(node))
                return;

            var tag = node.Node?.Tag?.ToUpperInvariant();
            bool isLeafControl = tag == "BUTTON" || tag == "INPUT" || tag == "IMG" || tag == "SELECT" || tag == "TEXTAREA";

            if (!isLeafControl && node.Children != null)
            {
                for (int i = 0; i < node.Children.Count; i++)
                    CollectVisible(node.Children[i], visibleRect, absX, absY, result);
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
                    return typeof(TextBox);
                }
                if (tag == "BUTTON") return typeof(Button);

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
            while (cur != null)
            {
                ax += cur.Bounds.X;
                ay += cur.Bounds.Y;
                cur = cur.Parent;
            }

            if (visual is FrameworkElement fe)
            {
                var tag = (node as RenderBox)?.Node?.Tag?.ToUpperInvariant();
                if (tag != "SVG")
                {
                    fe.Width = EnsureValid(node.Bounds.Width);
                    // Don't set explicit Height on TextBlock — use natural text height
                    if (!(visual is TextBlock))
                        fe.Height = EnsureValid(node.Bounds.Height);
                }
            }
            Canvas.SetLeft(visual, EnsureValid(ax));
            Canvas.SetTop(visual, EnsureValid(ay));
            _canvas.Children.Add(visual);
            _activeElements[node] = visual;
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
                if (style.BorderBrush != null) b.BorderBrush = style.BorderBrush;
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
                _canvas.Children.Remove(el);
                _activeElements.Remove(node);
                ReturnToPool(el);
            }
            if (node.Children != null)
            {
                for (int i = 0; i < node.Children.Count; i++)
                    RemoveSubtreeVisuals(node.Children[i]);
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
                    if (string.IsNullOrEmpty(src)) continue;
                    if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        LoadDataUriAsync(img, src);
                    else
                    {
                        var uri = ResolveUri(_baseUri, src);
                        if (uri != null)
                        {
                            try
                            {
                                img.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(uri);
                            }
                            catch { }
                        }
                    }
                }
                else if (!isVisible && img.Source != null)
                {
                    img.Source = null;
                }
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
                string pt = null;
                try { var p = box.Parent; if (p != null) { var pn = p.Node; if (pn != null) pt = pn.Tag; } } catch { }
                try { System.Diagnostics.Debug.WriteLine("[SVG:G.2] CreateBoxVisual parent=" + (pt ?? "?") + " viewBox=" + (GetAttr(box.Node, "viewBox") ?? "?") + " id=" + (GetAttr(box.Node, "id") ?? "?") + " class=" + (GetAttr(box.Node, "class") ?? "?")); } catch { }
                return RenderSvgElement(box.Node);
            }
            if (tag == "IMG") return CreateImageVisual(box);
            if (tag == "SELECT") return CreateSelectVisual(box);
            if (tag == "INPUT") return CreateInputVisual(box);
            if (tag == "BUTTON") return CreateButtonVisual(box);

            // Create visual for ALL elements, not just those with border/background
            var bg = box.Style?.Background;
            var borderBrush = box.Style?.BorderBrush;
            var borderThick = GetSafeThickness(box.Style?.BorderThickness);
            var margin = GetSafeThickness(box.Style?.Margin);
            var padding = GetSafeThickness(box.Style?.Padding);

            // Only create Border if there's something to style
            if (HasBorderOrBackground(box) || tag == "A" || !IsZero(margin) || !IsZero(padding))
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

                AttachLinkHandler(border, box);
                return border;
            }

            return null;
        }

        private UIElement CreateImageVisual(RenderBox box)
        {
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
                Stretch = Stretch.Uniform
            };

            // Defer actual Source loading until node is confirmed visible (ProcessLazyImages)
            _lazyImages[box] = img;

            grid.Children.Add(img);
            return grid;
        }

        private UIElement CreateSelectVisual(RenderBox box)
        {
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
                Padding = SafeThickness(box.Style?.Padding, new Thickness(8, 6, 8, 6))
            };

            if (box.Children != null)
            {
                foreach (var child in box.Children)
                {
                    if (child.Node != null && string.Equals(child.Node.Tag, "OPTION", StringComparison.OrdinalIgnoreCase))
                    {
                        var text = child.Node.Text ?? "";
                        if (string.IsNullOrEmpty(text) && child.Children.Count > 0 && child.Children[0] is RenderText rt)
                            text = rt.Text;
                        cb.Items.Add(text);
                        if (child.Node.Attr != null && child.Node.Attr.ContainsKey("selected"))
                            cb.SelectedItem = text;
                    }
                }
            }
            if (cb.SelectedIndex < 0 && cb.Items.Count > 0) cb.SelectedIndex = 0;
            return cb;
        }

        private UIElement CreateInputVisual(RenderBox box)
        {
            var type = box.Node.Attr != null && box.Node.Attr.ContainsKey("type")
                ? box.Node.Attr["type"].ToLowerInvariant() : "text";

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
                return btn;
            }
            else if (type == "checkbox")
            {
                return new CheckBox
                {
                    IsChecked = box.Node.Attr != null && box.Node.Attr.ContainsKey("checked"),
                    Width = EnsureValid(box.Bounds.Width),
                    Height = EnsureValid(box.Bounds.Height),
                };
            }
            else
            {
                return new TextBox
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

        private static bool HasBorderOrBackground(RenderBox box)
        {
            return box.Style?.Background != null ||
                   (box.Style?.BorderBrush != null && box.Style?.BorderThickness != null &&
                    box.Style.BorderThickness != new Thickness(0));
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

                    TappedEventHandler handler = (s, e) =>
                    {
                        e.Handled = true;
                        _onNavigate?.Invoke(uri);
                    };
                    _linkHandlerRefs[element] = handler;
                    element.Tapped += handler;
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

        // ── Phase G.2: SVG element rendering (D3.js force graph support) ──

        private sealed class SvgRenderState
        {
            public Brush Fill;
            public Brush Stroke;
            public double StrokeWidth = 1;
            public double Opacity = 1.0;
            public double TranslateX;
            public double TranslateY;

            public SvgRenderState Clone()
            {
                return new SvgRenderState
                {
                    Fill = Fill,
                    Stroke = Stroke,
                    StrokeWidth = StrokeWidth,
                    Opacity = Opacity,
                    TranslateX = TranslateX,
                    TranslateY = TranslateY
                };
            }
        }

        private UIElement RenderSvgElement(LiteElement svg)
        {
            if (svg == null) return null;
            string svgW = GetAttr(svg, "width"), svgH = GetAttr(svg, "height"), svgVb = GetAttr(svg, "viewBox");
            try { System.Diagnostics.Debug.WriteLine("[SVG:G.2] RenderSvgElement tag=" + (svg.Tag ?? "null") + " children=" + (svg.Children != null ? svg.Children.Count.ToString() : "0") + " width=" + (svgW ?? "null") + " height=" + (svgH ?? "null") + " viewBox=" + (svgVb ?? "null")); } catch { }

            double width = ParseSvgLength(GetAttr(svg, "width"));
            double height = ParseSvgLength(GetAttr(svg, "height"));

            double vbX = 0, vbY = 0, vbW = 0, vbH = 0;
            bool hasViewBox = false;
            string viewBox = GetAttr(svg, "viewBox") ?? GetAttr(svg, "viewbox");
            if (viewBox != null)
            {
                var parts = viewBox.Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4 &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out vbX) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out vbY) &&
                    double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out vbW) &&
                    double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out vbH))
                {
                    hasViewBox = vbW > 0 && vbH > 0;
                }
            }

            var canvas = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            if (hasViewBox)
            {
                canvas.Width = vbW;
                canvas.Height = vbH;
            }
            else
            {
                if (!double.IsNaN(width) && width > 0) canvas.Width = width;
                if (!double.IsNaN(height) && height > 0) canvas.Height = height;
            }

            if (svg.Children != null)
            {
                var state = new SvgRenderState();
                foreach (var child in svg.Children)
                {
                    try { System.Diagnostics.Debug.WriteLine("[SVG:G.2] svg child=" + (child?.Tag ?? "null") + " children=" + (child?.Children?.Count ?? 0)); } catch { }
                    AppendSvgChild(canvas, child, state);
                }
            }
            try { System.Diagnostics.Debug.WriteLine("[SVG:G.2] Canvas children=" + canvas.Children.Count); } catch { }

            if (hasViewBox || !double.IsNaN(width) || !double.IsNaN(height))
            {
                var vb = new Viewbox
                {
                    Stretch = Stretch.Uniform,
                    Child = canvas
                };
                if (!double.IsNaN(width) && width > 0) vb.Width = width;
                if (!double.IsNaN(height) && height > 0) vb.Height = height;
                return vb;
            }

            return canvas;
        }

        private void AppendSvgChild(Canvas parent, LiteElement node, SvgRenderState inherited)
        {
            if (node == null || node.IsText) return;

            var state = inherited?.Clone() ?? new SvgRenderState();
            ApplySvgStateOverrides(node, state);

            string tag = node.Tag?.ToLowerInvariant();
            switch (tag)
            {
                case "g":
                {
                    double tx = state.TranslateX, ty = state.TranslateY;
                    string transform = GetAttr(node, "transform");
                    if (transform != null)
                    {
                        int idx = transform.IndexOf("translate(", StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            int start = idx + 10;
                            int end = transform.IndexOf(')', start);
                            if (end > start)
                            {
                                var args = transform.Substring(start, end - start).Split(',');
                                if (args.Length >= 2)
                                {
                                    double.TryParse(args[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out tx);
                                    double.TryParse(args[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out ty);
                                }
                            }
                        }
                    }
                    state.TranslateX = tx;
                    state.TranslateY = ty;

                    int gChildCount = node.Children?.Count ?? 0;
                    try { System.Diagnostics.Debug.WriteLine("[SVG:G.2] g children=" + gChildCount + " tag=" + (node.Tag ?? "")); } catch { }
                    if (gChildCount > 0)
                    {
                        foreach (var child in node.Children)
                            AppendSvgChild(parent, child, state);
                    }
                    break;
                }

                case "circle":
                {
                    double cx = ParseSvgLength(GetAttr(node, "cx")) + state.TranslateX;
                    double cy = ParseSvgLength(GetAttr(node, "cy")) + state.TranslateY;
                    double r = ParseSvgLength(GetAttr(node, "r"));
                    if (double.IsNaN(r) || r <= 0 || double.IsNaN(cx) || double.IsInfinity(cx) || double.IsNaN(cy) || double.IsInfinity(cy)) break;

                    var brushFill = state.Fill ?? new SolidColorBrush(Colors.Black);
                    var ellipse = new Ellipse
                    {
                        Width = 2 * r,
                        Height = 2 * r,
                        Fill = brushFill,
                        Stroke = state.Stroke,
                        StrokeThickness = state.StrokeWidth > 0 ? state.StrokeWidth : 0,
                        Opacity = Clamp(state.Opacity, 0, 1),
                    };
                    Canvas.SetLeft(ellipse, cx - r);
                    Canvas.SetTop(ellipse, cy - r);
                    parent.Children.Add(ellipse);
                    break;
                }

                case "line":
                {
                    double x1 = ParseSvgLength(GetAttr(node, "x1")) + state.TranslateX;
                    double y1 = ParseSvgLength(GetAttr(node, "y1")) + state.TranslateY;
                    double x2 = ParseSvgLength(GetAttr(node, "x2")) + state.TranslateX;
                    double y2 = ParseSvgLength(GetAttr(node, "y2")) + state.TranslateY;
                    if (double.IsNaN(x1) || double.IsInfinity(x1) || double.IsNaN(y1) || double.IsInfinity(y1) || double.IsNaN(x2) || double.IsInfinity(x2) || double.IsNaN(y2) || double.IsInfinity(y2)) break;

                    var line = new Line
                    {
                        X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                        Stroke = state.Stroke ?? new SolidColorBrush(Colors.Black),
                        StrokeThickness = state.StrokeWidth > 0 ? state.StrokeWidth : 1,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        Opacity = Clamp(state.Opacity, 0, 1),
                    };
                    parent.Children.Add(line);
                    break;
                }

                case "rect":
                {
                    double x = ParseSvgLength(GetAttr(node, "x")) + state.TranslateX;
                    double y = ParseSvgLength(GetAttr(node, "y")) + state.TranslateY;
                    double w = ParseSvgLength(GetAttr(node, "width"));
                    double h = ParseSvgLength(GetAttr(node, "height"));
                    if (double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0) break;

                    var rect = new Rectangle
                    {
                        Width = w, Height = h,
                        Fill = state.Fill,
                        Stroke = state.Stroke,
                        StrokeThickness = state.StrokeWidth > 0 ? state.StrokeWidth : 0,
                        Opacity = Clamp(state.Opacity, 0, 1),
                    };
                    double rx = ParseSvgLength(GetAttr(node, "rx"));
                    double ry = ParseSvgLength(GetAttr(node, "ry"));
                    if (!double.IsNaN(rx) || !double.IsNaN(ry))
                    {
                        double cr = Math.Max(double.IsNaN(rx) ? 0 : rx, double.IsNaN(ry) ? 0 : ry);
                        rect.RadiusX = cr; rect.RadiusY = cr;
                    }
                    Canvas.SetLeft(rect, x);
                    Canvas.SetTop(rect, y);
                    parent.Children.Add(rect);
                    break;
                }

                case "path":
                {
                    string data = GetAttr(node, "d");
                    if (string.IsNullOrEmpty(data)) { try { System.Diagnostics.Debug.WriteLine("[SVG:PATH] empty d attribute"); } catch { } break; }
                    string fillRule = GetAttr(node, "fill-rule");
                    string fillStr = GetAttr(node, "fill");
                    string strokeStr = GetAttr(node, "stroke");
                    string swStr = GetAttr(node, "stroke-width");
                    try { System.Diagnostics.Debug.WriteLine("[SVG:PATH] d len=" + data.Length + " fill=" + (fillStr ?? "null") + " stroke=" + (strokeStr ?? "null") + " sw=" + (swStr ?? "null")); } catch { }

                    var path = CreateSvgPathElement(data, fillRule, state);
                    if (path != null)
                    {
                        try { System.Diagnostics.Debug.WriteLine("[SVG:PATH] OK fill=" + (path.Fill != null ? "set" : "null") + " stroke=" + (path.Stroke != null ? "set" : "null") + " sw=" + path.StrokeThickness); } catch { }
                        parent.Children.Add(path);
                    }
                    else try { System.Diagnostics.Debug.WriteLine("[SVG:PATH] CreateSvgPathElement returned null"); } catch { }
                    break;
                }

                case "text":
                {
                    double x = ParseSvgLength(GetAttr(node, "x")) + state.TranslateX;
                    double y = ParseSvgLength(GetAttr(node, "y")) + state.TranslateY;
                    double fontSize = ParseSvgLength(GetAttr(node, "font-size"));
                    if (double.IsNaN(fontSize) || fontSize <= 0) fontSize = 12;

                    var tb = new TextBlock
                    {
                        Text = node.Text ?? "",
                        FontSize = fontSize,
                        Foreground = state.Fill ?? new SolidColorBrush(Colors.Black),
                        Opacity = Clamp(state.Opacity, 0, 1),
                    };

                    string anchor = GetAttr(node, "text-anchor") ?? "start";
                    if (anchor == "middle") tb.TextAlignment = TextAlignment.Center;
                    else if (anchor == "end") tb.TextAlignment = TextAlignment.Right;

                    Canvas.SetLeft(tb, x);
                    Canvas.SetTop(tb, y - fontSize * 0.8);
                    parent.Children.Add(tb);
                    break;
                }

                case "ellipse":
                {
                    double cx = ParseSvgLength(GetAttr(node, "cx")) + state.TranslateX;
                    double cy = ParseSvgLength(GetAttr(node, "cy")) + state.TranslateY;
                    double rx = ParseSvgLength(GetAttr(node, "rx"));
                    double ry = ParseSvgLength(GetAttr(node, "ry"));
                    if (double.IsNaN(rx) || double.IsNaN(ry) || rx <= 0 || ry <= 0) break;

                    var ellipse = new Ellipse
                    {
                        Width = 2 * rx, Height = 2 * ry,
                        Fill = state.Fill ?? new SolidColorBrush(Colors.Black),
                        Stroke = state.Stroke,
                        StrokeThickness = state.StrokeWidth > 0 ? state.StrokeWidth : 0,
                        Opacity = Clamp(state.Opacity, 0, 1),
                    };
                    Canvas.SetLeft(ellipse, cx - rx);
                    Canvas.SetTop(ellipse, cy - ry);
                    parent.Children.Add(ellipse);
                    break;
                }

                default:
                {
                    if (node.Children != null)
                    {
                        foreach (var child in node.Children)
                            AppendSvgChild(parent, child, state);
                    }
                    break;
                }
            }
        }

        private static void ApplySvgStateOverrides(LiteElement node, SvgRenderState state)
        {
            if (node == null || state == null) return;

            string fill;
            if (TryGetAttr(node, "fill", out fill))
            {
                if (string.Equals(fill?.Trim(), "none", StringComparison.OrdinalIgnoreCase))
                    state.Fill = null;
                else
                {
                    var brush = CreateSvgBrush(fill);
                    if (brush != null) state.Fill = brush;
                }
            }

            string stroke;
            if (TryGetAttr(node, "stroke", out stroke))
            {
                if (string.Equals(stroke?.Trim(), "none", StringComparison.OrdinalIgnoreCase))
                    state.Stroke = null;
                else
                {
                    var brush = CreateSvgBrush(stroke);
                    if (brush != null) state.Stroke = brush;
                }
            }

            string strokeWidth;
            if (TryGetAttr(node, "stroke-width", out strokeWidth))
            {
                double w = ParseSvgLength(strokeWidth);
                if (!double.IsNaN(w) && w >= 0) state.StrokeWidth = w;
            }

            string opacity;
            if (TryGetAttr(node, "opacity", out opacity))
            {
                double o;
                if (double.TryParse(opacity, NumberStyles.Float, CultureInfo.InvariantCulture, out o))
                    state.Opacity = Clamp(o, 0, 1);
            }
        }

        private static string GetAttr(LiteElement el, string key)
        {
            if (el?.Attr == null) return null;
            string v;
            if (el.Attr.TryGetValue(key, out v)) return v;
            foreach (var kv in el.Attr)
            {
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
            return null;
        }

        private static bool TryGetAttr(LiteElement node, string name, out string value)
        {
            value = null;
            if (node?.Attr == null) return false;
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

        private static double ParseSvgLength(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return double.NaN;
            var s = raw.Trim();
            if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 2);
            double val;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out val) ? val : double.NaN;
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min) return min; if (v > max) return max; return v;
        }

        private static Brush CreateSvgBrush(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var s = value.Trim();
            if (string.Equals(s, "none", StringComparison.OrdinalIgnoreCase)) return null;
            var parsed = CssParser.ParseColor(s);
            if (parsed.HasValue) return new SolidColorBrush(parsed.Value);
            return null;
        }

        private static Path CreateSvgPathElement(string data, string fillRule, SvgRenderState state)
        {
            if (string.IsNullOrWhiteSpace(data)) return null;
            try
            {
                var escaped = data.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("'", "&apos;").Replace("<", "&lt;").Replace(">", "&gt;");
                var xaml = "<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data=\"" + escaped + "\" />";
                var path = XamlReader.Load(xaml) as Path;
                if (path == null) return null;

                path.Fill = state.Fill;
                path.Stroke = state.Stroke;
                path.StrokeThickness = state.StrokeWidth > 0 ? state.StrokeWidth : 0;
                path.Opacity = Clamp(state.Opacity, 0, 1);

                if (!string.IsNullOrWhiteSpace(fillRule) && string.Equals(fillRule.Trim(), "evenodd", StringComparison.OrdinalIgnoreCase))
                {
                    var pg = path.Data as PathGeometry;
                    if (pg != null) pg.FillRule = FillRule.EvenOdd;
                }
                return path;
            }
            catch { return null; }
        }

        // ─────────────────────────────────────────────────────────────
        //  SVG INJECTION  (Phase G.1 — Session 5.07, per architect)
        //  SvgImageSource-based fallback path
        // ─────────────────────────────────────────────────────────────

        // Throttle: don't re-render SVG more than ~15fps on Snapdragon 810
        private DateTime _lastSvgRender = DateTime.MinValue;

        /// <summary>
        /// Phase G.1 fast path: serialize D3-created SVG nodes to SVG strings,
        /// load each via SvgImageSource, and inject Image elements into the canvas.
        /// </summary>
        public async Task InjectSvgAsync(List<LiteElement> svgRoots)
        {
            if (svgRoots == null || svgRoots.Count == 0) return;

            // Remove any previously injected SVG images so we don't stack them
            var dispatcher = _scrollViewer?.Dispatcher;
            if (dispatcher != null)
            {
                await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    var old = new List<Image>();
                    for (int i = 0; i < _canvas.Children.Count; i++)
                    {
                        if (_canvas.Children[i] is Image img && img.Tag is string s && s == "svg-inject")
                            old.Add(img);
                    }
                    foreach (var img in old)
                        _canvas.Children.Remove(img);
                });
            }

            double yOffset = 0;

            foreach (var svgRoot in svgRoots)
            {
                try
                {
                    // Step 1: serialize the LiteElement SVG subtree to SVG text
                    var svgString = JavaScriptEngine.SerializeSvgNode(svgRoot, isRoot: true);
                    System.Diagnostics.Debug.WriteLine("[SVG-RENDER] Serialized " + svgString.Length + " chars");

                    if (svgString.Length < 20)
                    {
                        System.Diagnostics.Debug.WriteLine("[SVG-RENDER] SVG string too short — "
                            + "D3 may not have populated children yet.");
                        continue;
                    }

                    // Step 2: load into SvgImageSource
                    var image = await BuildSvgImageAsync(svgString).ConfigureAwait(false);
                    if (image == null) continue;

                    // Step 3: size and position on canvas
                    double svgW = ParseSvgAttr(svgRoot, "width", _canvas.ActualWidth > 0 ? _canvas.ActualWidth : 800);
                    double svgH = ParseSvgAttr(svgRoot, "height", 600);

                    image.Width = svgW;
                    image.Height = svgH;
                    image.Tag = "svg-inject";   // marker for cleanup on refresh
                    image.Stretch = Windows.UI.Xaml.Media.Stretch.Uniform;

                    double yPos = yOffset;
                    if (dispatcher != null)
                    {
                        await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            Canvas.SetLeft(image, 0);
                            Canvas.SetTop(image, yPos);
                            _canvas.Children.Add(image);
                            System.Diagnostics.Debug.WriteLine("[SVG-RENDER] Image added to canvas at y=" + yPos
                                + " size=" + svgW + "x" + svgH);
                        });
                    }

                    yOffset += svgH + 8;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[SVG-RENDER] Exception: " + ex.GetType().Name + " — " + ex.Message);
                }
            }
        }

        /// <summary>Re-render SVG (call from MutationObserver tick, max 15fps).</summary>
        public async Task RefreshSvgAsync(List<LiteElement> svgRoots)
        {
            if ((DateTime.UtcNow - _lastSvgRender).TotalMilliseconds < 67) return;
            _lastSvgRender = DateTime.UtcNow;
            await InjectSvgAsync(svgRoots);
        }

        /// <summary>Create an Image element from an SVG string via SvgImageSource. MUST run on UI thread.</summary>
        private async Task<Image> BuildSvgImageAsync(string svgXml)
        {
            // Ensure we're on UI thread (SvgImageSource requires it)
            var dispatcher = _canvas?.Dispatcher ?? _scrollViewer?.Dispatcher
                             ?? Windows.UI.Xaml.Window.Current?.Dispatcher;
            if (dispatcher == null)
            {
                System.Diagnostics.Debug.WriteLine("[SVG-RENDER] No dispatcher available");
                return null;
            }

            if (!dispatcher.HasThreadAccess)
            {
                var tcs = new TaskCompletionSource<Image>();
                // Fire-and-forget on UI thread — RunAsync expects a synchronous delegate,
                // so we start the async work and signal completion via TaskCompletionSource
                await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    var _ = BuildSvgImageOnUiThreadAsync(svgXml, tcs);
                });
                return await tcs.Task.ConfigureAwait(false);
            }

            return await BuildSvgImageOnUiThreadCoreAsync(svgXml).ConfigureAwait(false);
        }

        private async Task BuildSvgImageOnUiThreadAsync(string svgXml, TaskCompletionSource<Image> tcs)
        {
            try
            {
                var result = await BuildSvgImageOnUiThreadCoreAsync(svgXml).ConfigureAwait(true);
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SVG-RENDER] UI thread error: " + ex.Message);
                tcs.TrySetResult(null);
            }
        }

        private async Task<Image> BuildSvgImageOnUiThreadCoreAsync(string svgXml)
        {
            // SvgImageSource requires the SVG to start with a proper namespace.
            // SerializeSvgNode already adds xmlns for root <svg>, so check before adding.
            if (!svgXml.Contains("xmlns=\"http://www.w3.org/2000/svg\""))
                svgXml = svgXml.Replace("<svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"");

            var bytes = System.Text.Encoding.UTF8.GetBytes(svgXml);
            var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();

            using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
            }
            stream.Seek(0);

            var source = new Windows.UI.Xaml.Media.Imaging.SvgImageSource();
            var status = await source.SetSourceAsync(stream);

            if (status != Windows.UI.Xaml.Media.Imaging.SvgImageSourceLoadStatus.Success)
            {
                System.Diagnostics.Debug.WriteLine("[SVG-RENDER] SvgImageSource.SetSourceAsync → " + status);
                System.Diagnostics.Debug.WriteLine("[SVG-RENDER] SVG head: "
                    + svgXml.Substring(0, Math.Min(500, svgXml.Length)));
                return null;
            }

            return new Image { Source = source };
        }

        /// <summary>Parse an SVG attribute from a LiteElement.</summary>
        private static double ParseSvgAttr(LiteElement el, string attr, double fallback)
        {
            if (el?.Attr == null) return fallback;
            string v;
            if (!el.Attr.TryGetValue(attr, out v)) return fallback;
            // Strip "px" suffix if present
            if (v != null)
            {
                v = v.Replace("px", "").Trim();
                double d;
                if (double.TryParse(v, System.Globalization.NumberStyles.Any,
                       System.Globalization.CultureInfo.InvariantCulture, out d))
                    return d;
            }
            return fallback;
        }
    }
}
