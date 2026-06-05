using System;
using System.Collections.Generic;
using Windows.Foundation;
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
                fe.Width = EnsureValid(node.Bounds.Width);
                // Don't set explicit Height on TextBlock — use natural text height
                if (!(visual is TextBlock))
                    fe.Height = EnsureValid(node.Bounds.Height);
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
    }
}
