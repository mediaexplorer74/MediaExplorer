using System;
using System.Collections.Generic;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
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

        // Protect against layout cycles: prevent re-entrance into UpdateView
        private bool _isUpdatingView = false;
        private bool _updateViewPending = false;

        private static bool IsZero(Thickness t)
        {
            return t.Left == 0 && t.Top == 0 && t.Right == 0 && t.Bottom == 0;
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

            // DISABLED: Virtual rendering events were causing layout cycles
            // _scrollViewer.ViewChanged += OnViewChanged;
            // _scrollViewer.SizeChanged += OnSizeChanged;

            System.Diagnostics.Debug.WriteLine("[DIAG] VirtualizingRenderer canvas=" + cw + "x" + ch + " rootChildren=" + (_root != null && _root.Children != null ? _root.Children.Count.ToString() : "0"));

            UpdateView();
        }

        public FrameworkElement GetRootElement()
        {
            return _scrollViewer;
        }

        private void OnViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            try 
            { 
                // Don't trigger layout updates during scrolling animations
                if (!e.IsIntermediate)
                    UpdateView(); 
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[VirtualizingRenderer.OnViewChanged] Exception: " + ex.Message); }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            try 
            { 
                // Debounce size changes to avoid excessive layout updates
                if (_isUpdatingView) return;
                UpdateView(); 
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[VirtualizingRenderer.OnSizeChanged] Exception: " + ex.Message); }
        }

        public void UpdateView()
        {
            // Prevent layout cycles: if we're already updating, queue another update instead
            if (_isUpdatingView)
            {
                _updateViewPending = true;
                return;
            }

            try
            {
                _isUpdatingView = true;
                _updateViewPending = false;

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
                        try { lazyImg.Source = null; } catch { }
                        _lazyImages.Remove(node);
                    }
                    if (_activeElements.TryGetValue(node, out var el))
                    {
                        try { _canvas.Children.Remove(el); } catch { }
                        _activeElements.Remove(node);
                        ReturnToPool(el);
                    }
                }

                // Ensure visible nodes are on canvas with correct positions
                // Process flex/grid containers first, then other nodes
                var flexGridContainers = new List<RenderObject>();
                var otherNodes = new List<RenderObject>();

                foreach (var node in newVisible)
                {
                    if (_activeElements.ContainsKey(node))
                        continue;

                    var display = node.Style?.Display?.ToLowerInvariant();
                    if (display == "flex" || display == "inline-flex" || display == "grid" || display == "inline-grid")
                        flexGridContainers.Add(node);
                    else
                        otherNodes.Add(node);
                }

                // Process flex/grid containers first
                foreach (var node in flexGridContainers)
                {
                    PlaceVisualOnCanvas(node);
                }

                // Then process other nodes
                foreach (var node in otherNodes)
                {
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[VirtualizingRenderer.UpdateView] Exception: " + ex.Message);
            }
            finally
            {
                _isUpdatingView = false;

                // If an update was requested while we were updating, process it now
                if (_updateViewPending)
                {
                    _updateViewPending = false;
                    UpdateView();
                }
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

            // For flex/grid containers, children are added to the container, not placed on canvas directly
            // So we still need to collect them for visibility, but they won't be placed on canvas
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

                // Check for flex/grid containers
                var display = box.Style?.Display?.ToLowerInvariant();
                if (display == "flex" || display == "inline-flex") return typeof(FlexPanel);
                if (display == "grid" || display == "inline-grid") return typeof(Grid);

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
            try
            {
                // Skip if parent is a flex/grid container (children are added to container, not canvas)
                if (node.Parent != null)
                {
                    var parentDisplay = node.Parent.Style?.Display?.ToLowerInvariant();
                    if (parentDisplay == "flex" || parentDisplay == "inline-flex" ||
                        parentDisplay == "grid" || parentDisplay == "inline-grid")
                    {
                        // Child will be added by the parent container's visual creation
                        return;
                    }
                }

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

                // Only update size if it actually changed (avoid triggering layout updates)
                if (visual is FrameworkElement fe)
                {
                    double w = EnsureValid(node.Bounds.Width);
                    double h = EnsureValid(node.Bounds.Height);
                    if (Math.Abs(fe.Width - w) > 0.01 || double.IsNaN(fe.Width))
                        fe.Width = w;
                    if (Math.Abs(fe.Height - h) > 0.01 || double.IsNaN(fe.Height))
                        fe.Height = h;
                }

                // Only update position if it actually changed
                double leftPos = EnsureValid(ax);
                double topPos = EnsureValid(ay);
                if (Math.Abs(Canvas.GetLeft(visual) - leftPos) > 0.01 || double.IsNaN(Canvas.GetLeft(visual)))
                    Canvas.SetLeft(visual, leftPos);
                if (Math.Abs(Canvas.GetTop(visual) - topPos) > 0.01 || double.IsNaN(Canvas.GetTop(visual)))
                    Canvas.SetTop(visual, topPos);

                try
                {
                    // Only add if not already in children
                    if (!_canvas.Children.Contains(visual))
                        _canvas.Children.Add(visual);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[VirtualizingRenderer] Canvas.Children.Add failed: " + ex.Message);
                    // Element might already be in canvas or have other issues, skip
                    return;
                }

                _activeElements[node] = visual;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[VirtualizingRenderer.PlaceVisualOnCanvas] Exception: " + ex.Message);
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
                            catch { System.Diagnostics.Debug.WriteLine(" [Engine/Core/VirtualizingRenderer.cs] empty catch empty catch"); }
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

            // Check if this is a flex container
            var display = box.Style?.Display?.ToLowerInvariant();
            if (display == "flex" || display == "inline-flex")
            {
                return CreateFlexVisual(box);
            }

            // Check if this is a grid container
            if (display == "grid" || display == "inline-grid")
            {
                return CreateGridVisual(box);
            }

            // Create visual for ALL elements, not just those with border/background
            var bg = box.Style?.Background;
            var borderBrush = box.Style?.BorderBrush;
            var borderThick = box.Style?.BorderThickness ?? new Thickness(0);
            var margin = box.Style?.Margin ?? new Thickness(0);
            var padding = box.Style?.Padding ?? new Thickness(0);

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

        private UIElement CreateFlexVisual(RenderBox box)
        {
            var flexPanel = new FlexPanel
            {
                Width = EnsureValid(box.Bounds.Width),
                Height = EnsureValid(box.Bounds.Height),
                FlexDirection = box.Style?.FlexDirection ?? "row",
                FlexWrap = box.Style?.FlexWrap ?? "nowrap",
                JustifyContent = box.Style?.JustifyContent ?? "flex-start",
                AlignItems = box.Style?.AlignItems ?? "stretch",
                Margin = box.Style?.Margin ?? new Thickness(0),
                Padding = box.Style?.Padding ?? new Thickness(0),
                Background = box.Style?.Background
            };

            // Add child visuals to flex panel
            if (box.Children != null)
            {
                foreach (var child in box.Children)
                {
                    if (child.Style != null && (child.Style.Position == "absolute" || child.Style.Position == "fixed"))
                        continue;

                    var childVisual = GetOrCreateVisual(child);
                    if (childVisual != null)
                    {
                        // Remove from canvas if already placed there
                        if (_activeElements.ContainsKey(child))
                        {
                            _canvas.Children.Remove(childVisual);
                            _activeElements.Remove(child);
                        }

                        // Set flex properties on child
                        if (childVisual is FrameworkElement fe)
                        {
                            if (child.Style?.FlexGrow.HasValue == true)
                            {
                                // For UWP, we can't set flex-grow directly, but we can set HorizontalAlignment/VerticalAlignment
                                if (flexPanel.FlexDirection?.Contains("row") == true)
                                    fe.HorizontalAlignment = HorizontalAlignment.Stretch;
                                else
                                    fe.VerticalAlignment = VerticalAlignment.Stretch;
                            }
                            fe.Margin = child.Style?.Margin ?? new Thickness(0);
                        }
                        flexPanel.Children.Add(childVisual);
                    }
                }
            }

            AttachLinkHandler(flexPanel, box);
            return flexPanel;
        }

        private UIElement CreateGridVisual(RenderBox box)
        {
            var grid = new Grid
            {
                Width = EnsureValid(box.Bounds.Width),
                Height = EnsureValid(box.Bounds.Height),
                Margin = box.Style?.Margin ?? new Thickness(0),
                Padding = box.Style?.Padding ?? new Thickness(0),
                Background = box.Style?.Background
            };

            // Parse grid-template-columns
            var columns = box.Style?.Map != null && box.Style.Map.TryGetValue("grid-template-columns", out var colVal) ? colVal : null;
            if (!string.IsNullOrEmpty(columns))
            {
                var colDefs = columns.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var def in colDefs)
                {
                    var cd = new ColumnDefinition();
                    if (def == "1fr" || def.Contains("fr"))
                    {
                        // For fr units, we use Star sizing
                        cd.Width = new GridLength(1, GridUnitType.Star);
                    }
                    else if (def.EndsWith("px"))
                    {
                        double px;
                        if (double.TryParse(def.Replace("px", ""), out px))
                            cd.Width = new GridLength(px);
                    }
                    else if (def == "auto")
                    {
                        cd.Width = GridLength.Auto;
                    }
                    grid.ColumnDefinitions.Add(cd);
                }
            }

            // Parse grid-template-rows
            var rows = box.Style?.Map != null && box.Style.Map.TryGetValue("grid-template-rows", out var rowVal) ? rowVal : null;
            if (!string.IsNullOrEmpty(rows))
            {
                var rowDefs = rows.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var def in rowDefs)
                {
                    var rd = new RowDefinition();
                    if (def == "1fr" || def.Contains("fr"))
                    {
                        rd.Height = new GridLength(1, GridUnitType.Star);
                    }
                    else if (def.EndsWith("px"))
                    {
                        double px;
                        if (double.TryParse(def.Replace("px", ""), out px))
                            rd.Height = new GridLength(px);
                    }
                    else if (def == "auto")
                    {
                        rd.Height = GridLength.Auto;
                    }
                    grid.RowDefinitions.Add(rd);
                }
            }

            // Add child visuals to grid
            if (box.Children != null)
            {
                int col = 0, row = 0;
                foreach (var child in box.Children)
                {
                    if (child.Style != null && (child.Style.Position == "absolute" || child.Style.Position == "fixed"))
                        continue;

                    var childVisual = GetOrCreateVisual(child);
                    if (childVisual != null)
                    {
                        // Remove from canvas if already placed there
                        if (_activeElements.ContainsKey(child))
                        {
                            _canvas.Children.Remove(childVisual);
                            _activeElements.Remove(child);
                        }

                        if (childVisual is FrameworkElement fe)
                        {
                            fe.Margin = child.Style?.Margin ?? new Thickness(0);

                            // Set grid position
                            var gridCol = child.Style?.Map != null && child.Style.Map.TryGetValue("grid-column", out var gcVal) ? gcVal : null;
                            var gridRow = child.Style?.Map != null && child.Style.Map.TryGetValue("grid-row", out var grVal) ? grVal : null;

                            if (!string.IsNullOrEmpty(gridCol))
                            {
                                int c;
                                if (int.TryParse(gridCol, out c))
                                    Grid.SetColumn(fe, c - 1);
                                else if (gridCol.Contains("/"))
                                {
                                    var parts = gridCol.Split('/');
                                    int start, end;
                                    if (int.TryParse(parts[0], out start) && int.TryParse(parts[1], out end))
                                    {
                                        Grid.SetColumn(fe, start - 1);
                                        Grid.SetColumnSpan(fe, end - start);
                                    }
                                }
                            }
                            else
                            {
                                Grid.SetColumn(fe, col);
                            }

                            if (!string.IsNullOrEmpty(gridRow))
                            {
                                int r;
                                if (int.TryParse(gridRow, out r))
                                    Grid.SetRow(fe, r - 1);
                                else if (gridRow.Contains("/"))
                                {
                                    var parts = gridRow.Split('/');
                                    int start, end;
                                    if (int.TryParse(parts[0], out start) && int.TryParse(parts[1], out end))
                                    {
                                        Grid.SetRow(fe, start - 1);
                                        Grid.SetRowSpan(fe, end - start);
                                    }
                                }
                            }
                            else
                            {
                                Grid.SetRow(fe, row);
                            }

                            // Auto-advance position if not explicitly set
                            if (string.IsNullOrEmpty(gridCol)) col++;
                            if (string.IsNullOrEmpty(gridRow)) row++;
                        }
                        grid.Children.Add(childVisual);
                    }
                }
            }

            AttachLinkHandler(grid, box);
            return grid;
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
                BorderThickness = box.Style?.BorderThickness ?? new Thickness(1),
                Padding = box.Style?.Padding ?? new Thickness(8, 6, 8, 6)
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
                    BorderThickness = box.Style?.BorderThickness ?? new Thickness(1),
                    Padding = box.Style?.Padding ?? new Thickness(4),
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
                    BorderThickness = box.Style?.BorderThickness ?? new Thickness(1),
                    Padding = box.Style?.Padding ?? new Thickness(8, 6, 8, 6)
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
                BorderThickness = box.Style?.BorderThickness ?? new Thickness(1),
                Padding = box.Style?.Padding ?? new Thickness(4),
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
                var uri = ResolveUri(_baseUri, href);
                if (uri != null)
                {
                    if (element is Border b && b.Background == null)
                        b.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);

                    element.Tapped += (s, e) =>
                    {
                        e.Handled = true;
                        _onNavigate?.Invoke(uri);
                    };
                }
            }
        }

        private double EnsureValid(double v)
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
