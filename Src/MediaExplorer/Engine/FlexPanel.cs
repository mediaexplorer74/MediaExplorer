using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace BrowserCore.Engine
{
    internal sealed class FlexPanel : Panel
    {
        public Orientation Orientation { get; set; } = Orientation.Horizontal;
        public string FlexDirection { get; set; } = "row";
        public string FlexWrap { get; set; } = "nowrap";
        public string JustifyContent { get; set; } = "flex-start";
        public string AlignItems { get; set; } = "stretch";
        public string AlignContent { get; set; } = "stretch";
        public double RowGap { get; set; } = 0;
        public double ColumnGap { get; set; } = 0;
        public Thickness Padding { get; set; } = new Thickness(0);

        private List<FlexLine> _lines = new List<FlexLine>();

        protected override Size MeasureOverride(Size availableSize)
        {
            _lines.Clear();
            if (Children.Count == 0) return new Size(0, 0);

            // Parse properties
            bool isRow = FlexDirection == null || FlexDirection.ToLowerInvariant().Contains("row");
            bool isReverse = FlexDirection != null && FlexDirection.ToLowerInvariant().Contains("reverse");
            bool isWrap = FlexWrap != null && FlexWrap.ToLowerInvariant() != "nowrap";

            // Account for padding
            double paddingMain = isRow ? Padding.Left + Padding.Right : Padding.Top + Padding.Bottom;
            double paddingCross = isRow ? Padding.Top + Padding.Bottom : Padding.Left + Padding.Right;
            double mainAvailable = (isRow ? availableSize.Width : availableSize.Height) - paddingMain;
            double crossAvailable = (isRow ? availableSize.Height : availableSize.Width) - paddingCross;

            if (mainAvailable < 0) mainAvailable = 0;
            if (crossAvailable < 0) crossAvailable = 0;

            var currentLine = new FlexLine { IsRow = isRow };
            _lines.Add(currentLine);

            foreach (var child in Children)
            {
                // Initial measure to get desired size
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var desired = child.DesiredSize;
                
                double basis = GetFlexBasis(child);
                if (double.IsNaN(basis)) basis = isRow ? desired.Width : desired.Height;

                // Check if we need to wrap
                if (isWrap && currentLine.Items.Count > 0 && 
                    currentLine.MainSize + basis + (isRow ? ColumnGap : RowGap) > mainAvailable)
                {
                    currentLine = new FlexLine { IsRow = isRow };
                    _lines.Add(currentLine);
                }

                currentLine.AddItem(child, basis, GetFlexGrow(child), GetFlexShrink(child), isRow ? ColumnGap : RowGap);
            }

            // Resolve flexible lengths within each line
            double maxMainSize = 0;
            double totalCrossSize = 0;

            foreach (var line in _lines)
            {
                line.ResolveFlexibility(mainAvailable);
                maxMainSize = Math.Max(maxMainSize, line.MainSize);
                totalCrossSize += line.CrossSize + (isRow ? RowGap : ColumnGap);
            }
            // Remove last gap
            if (_lines.Count > 0) totalCrossSize -= (isRow ? RowGap : ColumnGap);

            // Add padding to the final size
            double finalMain = maxMainSize + paddingMain;
            double finalCross = totalCrossSize + paddingCross;
            return isRow ? new Size(finalMain, finalCross) : new Size(finalCross, finalMain);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (Children.Count == 0) return finalSize;

            bool isRow = FlexDirection == null || FlexDirection.ToLowerInvariant().Contains("row");
            bool isReverse = FlexDirection != null && FlexDirection.ToLowerInvariant().Contains("reverse");
            bool wrapReverse = FlexWrap != null && FlexWrap.ToLowerInvariant().Contains("wrap-reverse");

            double mainSize = isRow ? finalSize.Width - Padding.Left - Padding.Right : finalSize.Height - Padding.Top - Padding.Bottom;
            double crossSize = isRow ? finalSize.Height - Padding.Top - Padding.Bottom : finalSize.Width - Padding.Left - Padding.Right;

            if (mainSize < 0) mainSize = 0;
            if (crossSize < 0) crossSize = 0;

            // AlignContent (distribute lines along cross axis)
            DistributeLines(crossSize, AlignContent, isRow ? RowGap : ColumnGap);

            foreach (var line in _lines)
            {
                // JustifyContent (distribute items along main axis)
                line.Justify(mainSize, JustifyContent);

                // AlignItems (align items within line on cross axis)
                line.Align(AlignItems);

                foreach (var item in line.Items)
                {
                    var rect = item.Rect;
                    // Handle reverse direction
                    if (isReverse)
                    {
                        if (isRow) rect.X = mainSize - rect.X - rect.Width;
                        else rect.Y = mainSize - rect.Y - rect.Height;
                    }
                    
                    // Handle wrap reverse
                    if (wrapReverse)
                    {
                        if (isRow) rect.Y = crossSize - rect.Y - rect.Height;
                        else rect.X = crossSize - rect.X - rect.Width;
                    }

                    // Flip X/Y if column
                    if (!isRow)
                    {
                        var tmpX = rect.X; rect.X = rect.Y; rect.Y = tmpX;
                        var tmpW = rect.Width; rect.Width = rect.Height; rect.Height = tmpW;
                    }

                    // Add padding offset
                    rect.X += Padding.Left;
                    rect.Y += Padding.Top;

                    item.Element.Arrange(rect);
                }
            }

            return finalSize;
        }

        private void DistributeLines(double totalCrossSpace, string alignContent, double gap)
        {
            double contentHeight = _lines.Sum(l => l.CrossSize) + (_lines.Count - 1) * gap;
            double freeSpace = totalCrossSpace - contentHeight;
            double y = 0;

            if (freeSpace > 0)
            {
                var ac = (alignContent ?? "stretch").ToLowerInvariant();
                if (ac.Contains("center")) y = freeSpace / 2;
                else if (ac.Contains("flex-end")) y = freeSpace;
                else if (ac.Contains("space-between"))
                {
                    if (_lines.Count > 1) gap += freeSpace / (_lines.Count - 1);
                }
                else if (ac.Contains("space-around"))
                {
                    double half = freeSpace / (_lines.Count * 2);
                    y = half;
                    gap += half * 2;
                }
            }

            foreach (var line in _lines)
            {
                line.CrossPos = y;
                y += line.CrossSize + gap;
            }
            
            // Stretch lines if needed
            if ((alignContent ?? "").Contains("stretch") && freeSpace > 0)
            {
                double add = freeSpace / _lines.Count;
                double curY = 0;
                foreach (var line in _lines)
                {
                    line.CrossPos = curY;
                    line.CrossSize += add;
                    curY += line.CrossSize + gap;
                }
            }
        }

        private class FlexLine
        {
            public List<FlexItem> Items = new List<FlexItem>();
            public double MainSize { get; private set; }
            public double CrossSize { get; set; }
            public double CrossPos { get; set; }
            public bool IsRow { get; set; }

            public void AddItem(UIElement element, double basis, double grow, double shrink, double gap)
            {
                if (Items.Count > 0) MainSize += gap;
                Items.Add(new FlexItem { Element = element, Basis = basis, Grow = grow, Shrink = shrink });
                MainSize += basis;
                
                var ds = element.DesiredSize;
                CrossSize = Math.Max(CrossSize, IsRow ? ds.Height : ds.Width);
            }

            public void ResolveFlexibility(double availableSpace)
            {
                double freeSpace = availableSpace - MainSize;
                if (freeSpace > 0)
                {
                    double totalGrow = Items.Sum(i => i.Grow);
                    if (totalGrow > 0)
                    {
                        foreach (var item in Items)
                        {
                            double add = freeSpace * (item.Grow / totalGrow);
                            item.TargetMainSize = item.Basis + add;
                        }
                        MainSize = availableSpace; // Filled
                    }
                    else
                    {
                        foreach (var item in Items) item.TargetMainSize = item.Basis;
                    }
                }
                else if (freeSpace < 0)
                {
                    double totalShrink = Items.Sum(i => i.Shrink);
                    if (totalShrink > 0)
                    {
                        // Weighted shrink
                        double totalWeighted = Items.Sum(i => i.Shrink * i.Basis);
                        foreach (var item in Items)
                        {
                            double sub = Math.Abs(freeSpace) * (item.Shrink * item.Basis / totalWeighted);
                            item.TargetMainSize = Math.Max(0, item.Basis - sub);
                        }
                        MainSize = availableSpace; // Shrunk to fit
                    }
                    else
                    {
                         foreach (var item in Items) item.TargetMainSize = item.Basis;
                    }
                }
                else
                {
                    foreach (var item in Items) item.TargetMainSize = item.Basis;
                }
            }

            public void Justify(double lineMainSize, string justifyContent)
            {
                double contentSize = Items.Sum(i => i.TargetMainSize);
                double free = lineMainSize - contentSize;
                double x = 0;
                double gap = 0;

                var jc = (justifyContent ?? "flex-start").ToLowerInvariant();
                if (jc.Contains("center")) x = free / 2;
                else if (jc.Contains("flex-end")) x = free;
                else if (jc.Contains("space-between"))
                {
                    if (Items.Count > 1) gap = free / (Items.Count - 1);
                }
                else if (jc.Contains("space-around"))
                {
                    double half = free / (Items.Count * 2);
                    x = half;
                    gap = half * 2;
                }
                else if (jc.Contains("space-evenly"))
                {
                    gap = free / (Items.Count + 1);
                    x = gap;
                }

                foreach (var item in Items)
                {
                    item.Rect = new Rect(x, CrossPos, item.TargetMainSize, CrossSize);
                    x += item.TargetMainSize + gap;
                }
            }

            public void Align(string alignItems)
            {
                var ai = (alignItems ?? "stretch").ToLowerInvariant();
                foreach (var item in Items)
                {
                    // Measure with final main size
                    if (IsRow) item.Element.Measure(new Size(item.TargetMainSize, double.PositiveInfinity));
                    else item.Element.Measure(new Size(double.PositiveInfinity, item.TargetMainSize));

                    double childCross = IsRow ? item.Element.DesiredSize.Height : item.Element.DesiredSize.Width;
                    double y = item.Rect.Y;
                    double h = childCross;

                    if (ai.Contains("stretch"))
                    {
                        h = CrossSize;
                        if (IsRow) item.Element.Measure(new Size(item.TargetMainSize, h));
                        else item.Element.Measure(new Size(h, item.TargetMainSize));
                    }
                    else if (ai.Contains("center"))
                    {
                        y += (CrossSize - childCross) / 2;
                    }
                    else if (ai.Contains("flex-end"))
                    {
                        y += CrossSize - childCross;
                    }
                    
                    // Update rect
                    if (IsRow) item.Rect = new Rect(item.Rect.X, y, item.TargetMainSize, h);
                    else item.Rect = new Rect(y, item.Rect.X, h, item.TargetMainSize); // Swapped for column later
                }
            }
        }

        private class FlexItem
        {
            public UIElement Element;
            public double Basis;
            public double Grow;
            public double Shrink;
            public double TargetMainSize;
            public Rect Rect;
        }

        public static readonly DependencyProperty FlexGrowProperty =
            DependencyProperty.RegisterAttached("FlexGrow", typeof(double), typeof(FlexPanel), new PropertyMetadata(0.0));
        public static void SetFlexGrow(UIElement element, double value) => element.SetValue(FlexGrowProperty, value);
        public static double GetFlexGrow(UIElement element) => (double)element.GetValue(FlexGrowProperty);

        public static readonly DependencyProperty FlexShrinkProperty =
            DependencyProperty.RegisterAttached("FlexShrink", typeof(double), typeof(FlexPanel), new PropertyMetadata(1.0));
        public static void SetFlexShrink(UIElement element, double value) => element.SetValue(FlexShrinkProperty, value);
        public static double GetFlexShrink(UIElement element) => (double)element.GetValue(FlexShrinkProperty);

        public static readonly DependencyProperty FlexBasisProperty =
            DependencyProperty.RegisterAttached("FlexBasis", typeof(double), typeof(FlexPanel), new PropertyMetadata(double.NaN));
        public static void SetFlexBasis(UIElement element, double value) => element.SetValue(FlexBasisProperty, value);
        public static double GetFlexBasis(UIElement element) => (double)element.GetValue(FlexBasisProperty);
    }
}
