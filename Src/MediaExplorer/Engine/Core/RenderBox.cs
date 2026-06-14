using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Windows.UI.Xaml;

using BrowserCore.Engine;

namespace BrowserCore.Engine.Core
{
    public class RenderBox : RenderObject
    {
        // Table grid layout data (populated by RenderTreeBuilder for TABLE elements)
        public int TableRows;
        public int TableCols;
        public bool[,] TableOccupied;  // [row,col] = true if cell occupied
        public int[,] TableColSpans;   // [row,col] = colspan of cell at this position
        public int[,] TableRowSpans;   // [row,col] = rowspan of cell at this position

        public override void Layout(Size availableSize)
        {
            if (Style == null) Style = new CssComputed(); // Ensure Style is never null

            // display:none handling disabled — re-enabling breaks scrolling on 4pda.to
            // and other sites that use CSS display:none on wrapper elements.
            // The original "smart" logic (force-show BODY/MAIN/DIV) still broke things.
            // TODO: Only apply display:none when it comes from CSS stylesheet, not from UA defaults.

            // 1. Calculate Box Model properties
            var margin = Style.Margin;
            var padding = Style.Padding;
            var border = Style.BorderThickness;



            // 2. Determine Width
            double targetWidth = 0;
            
            string bs = null;
            if (Style.Map != null) Style.Map.TryGetValue("box-sizing", out bs);
            bool isBorderBox = (bs ?? "").Trim().Equals("border-box", StringComparison.OrdinalIgnoreCase);

            if (Style.Width.HasValue) 
            {
                targetWidth = Style.Width.Value;
                if (!isBorderBox) targetWidth += padding.Left + padding.Right + border.Left + border.Right;
            }
            else if (Style.WidthPercent.HasValue) targetWidth = availableSize.Width * (Style.WidthPercent.Value / 100.0) - margin.Left - margin.Right;
            else if (Style.Display == "inline") targetWidth = double.PositiveInfinity;
            else targetWidth = availableSize.Width - margin.Left - margin.Right;
            
            bool isBlock = Style.Display == "block" || Style.Display == null; 
            
            if (!Style.Width.HasValue && !Style.WidthPercent.HasValue)
            {
                var tag = Node?.Tag?.ToUpperInvariant();
                if (tag == "INPUT" || tag == "BUTTON" || tag == "SELECT" || tag == "IMG")
                {
                    // If it's a block, it takes full width (already set above).
                    // If it's inline/inline-block, we give it intrinsic width.
                    if (Style.Display == "inline" || Style.Display == "inline-block")
                    {
                         if (tag == "IMG") targetWidth = 300;
                         else targetWidth = 150;
                    }
                }
                else if (tag == "HR")
                {
                    // HR takes full width by default
                }
            }

            if (targetWidth < 0) targetWidth = 0;

            // Constrain by Min/Max Width
            if (Style.MinWidth.HasValue && targetWidth < Style.MinWidth.Value) targetWidth = Style.MinWidth.Value;
            if (Style.MaxWidth.HasValue && targetWidth > Style.MaxWidth.Value) targetWidth = Style.MaxWidth.Value;

            // 3. Prepare content area
            double contentWidth = targetWidth - padding.Left - padding.Right - border.Left - border.Right;
            if (contentWidth < 0) contentWidth = 0;

            double contentHeight = 0;

            // 4. Layout Children
            // Check if we should do Inline Layout, Block Layout, Flex Layout, Grid Layout, or Table Layout
            bool isFlex = Style.Display == "flex" || Style.Display == "inline-flex";
            bool isGrid = Style.Display == "grid" || Style.Display == "inline-grid";
            bool isTable = TableOccupied != null && Node?.Tag?.ToUpperInvariant() == "TABLE";

            if (isTable)
            {
                contentHeight = LayoutTableChildren(contentWidth);
            }
            else if (isGrid)
            {
                contentHeight = LayoutGridChildren(contentWidth);
            }
            else if (isFlex)
            {
                var diagTag2 = Node?.Tag ?? "?";
                DevToolsLogger.Log($"[DIAG:LAYOUT:FLEX] tag={diagTag2} dir={Style.FlexDirection} children={Children.Count} contentWidth={contentWidth:F0}");
                contentHeight = LayoutFlexChildren(contentWidth);
            }
            else
            {
                // Heuristic: If we have children and the first one is inline, we try inline layout.
                bool isInlineFormattingContext = HasInlineChildren();
                var diagTag3 = Node?.Tag ?? "?";
                DevToolsLogger.Log($"[DIAG:LAYOUT:DECIDE] tag={diagTag3} display={Style?.Display} isInline={isInlineFormattingContext} childCount={Children.Count}");

                if (isInlineFormattingContext)
                {
                    contentHeight = LayoutInlineChildren(contentWidth);
                }
                else
                {
                    contentHeight = LayoutBlockChildren(contentWidth);
                }
            }

            // 5. Determine Height
            double targetHeight = Style.Height ?? (contentHeight + padding.Top + padding.Bottom + border.Top + border.Bottom);
            
            // Height Percent Support
            if (!Style.Height.HasValue && Style.HeightPercent.HasValue && !double.IsInfinity(availableSize.Height))
            {
                targetHeight = availableSize.Height * (Style.HeightPercent.Value / 100.0) - margin.Top - margin.Bottom;
            }

            // Aspect Ratio Support
            // If aspect-ratio is specified and one dimension is known, calculate the other
            if (Style.AspectRatio.HasValue && Style.AspectRatio.Value > 0)
            {
                bool widthIsSet = Style.Width.HasValue || Style.WidthPercent.HasValue;
                bool heightIsSet = Style.Height.HasValue || Style.HeightPercent.HasValue;
                
                if (widthIsSet && !heightIsSet)
                {
                    // Width is known, calculate height from aspect ratio
                    // aspect-ratio = width / height, so height = width / aspect-ratio
                    targetHeight = (targetWidth - padding.Left - padding.Right - border.Left - border.Right) / Style.AspectRatio.Value + padding.Top + padding.Bottom + border.Top + border.Bottom;
                }
                else if (!widthIsSet && heightIsSet)
                {
                    // Height is known, calculate width from aspect ratio
                    // aspect-ratio = width / height, so width = height * aspect-ratio  
                    double innerHeight = targetHeight - padding.Top - padding.Bottom - border.Top - border.Bottom;
                    targetWidth = innerHeight * Style.AspectRatio.Value + padding.Left + padding.Right + border.Left + border.Right;
                   
                    // Re-apply width constraints
                    if (Style.MinWidth.HasValue && targetWidth < Style.MinWidth.Value) targetWidth = Style.MinWidth.Value;
                    if (Style.MaxWidth.HasValue && targetWidth > Style.MaxWidth.Value) targetWidth = Style.MaxWidth.Value;
                }
            }

            // Intrinsic height for replaced elements if not specified
            if (!Style.Height.HasValue && !Style.HeightPercent.HasValue && contentHeight == 0)
            {
                var tag = Node?.Tag?.ToUpperInvariant();
                if (tag == "INPUT" || tag == "BUTTON" || tag == "SELECT") targetHeight = 30; // Default height
                else if (tag == "IMG") targetHeight = 150; // Placeholder height
            }
            
            // Constrain by Min/Max Height
            if (Style.MinHeight.HasValue && targetHeight < Style.MinHeight.Value) targetHeight = Style.MinHeight.Value;
            if (Style.MaxHeight.HasValue && targetHeight > Style.MaxHeight.Value) targetHeight = Style.MaxHeight.Value;

            // Handle infinite width (shrink to fit)
            if (double.IsInfinity(targetWidth))
            {
                double maxRight = padding.Left + border.Left;
                if (Children.Count > 0)
                {
                    foreach (var child in Children)
                    {
                        if (child.Style != null && (child.Style.Position == "absolute" || child.Style.Position == "fixed")) continue;

                        var childMargin = child.Style?.Margin ?? new Thickness(0);
                        double right = child.Bounds.X + child.Bounds.Width + childMargin.Right;
                        if (right > maxRight) maxRight = right;
                    }
                }
                
                targetWidth = maxRight + padding.Right + border.Right;
                
                // Re-apply min/max constraints
                if (Style.MinWidth.HasValue && targetWidth < Style.MinWidth.Value) targetWidth = Style.MinWidth.Value;
                if (Style.MaxWidth.HasValue && targetWidth > Style.MaxWidth.Value) targetWidth = Style.MaxWidth.Value;
            }

            // 6. Set Final Bounds
            // Sanitize values to prevent Rect constructor crashes
            targetWidth = EnsureValid(targetWidth);
            targetHeight = EnsureValid(targetHeight);
            Bounds = new Rect(0, 0, targetWidth, targetHeight);

            if (Node?.HasAttribute("bgcolor") == true)
            {
                DevToolsLogger.Log($"[DIAG:BGCOLOR] tag={Node?.Tag} bg={Node.GetAttribute("bgcolor")} w={targetWidth:F0} h={targetHeight:F0} contentH={contentHeight:F0} pad={padding.Top}+{padding.Bottom} styleH={Style?.Height}");
            }

            // 7. Layout Absolute Children
            LayoutAbsoluteChildren(new Size(targetWidth, targetHeight));
        }

        private bool HasInlineChildren()
        {
            if (Children.Count == 0) return false;
            // If first child is text or explicitly inline, we treat as inline context
            var first = Children[0];
            if (first is RenderText) return true;
            if (first.Style != null && (first.Style.Display == "inline" || first.Style.Display == "inline-block")) return true;
            return false;
        }

        private double LayoutBlockChildren(double contentWidth)
        {
            var diagTag = Node?.Tag ?? "?";
            DevToolsLogger.Log($"[DIAG:LAYOUT:BLOCK] tag={diagTag} children={Children.Count} contentWidth={contentWidth:F0}");
            double currentY = Style.Padding.Top + Style.BorderThickness.Top;
            foreach (var child in Children)
            {
                if (child.Style != null && (child.Style.Position == "absolute" || child.Style.Position == "fixed")) continue;

                child.Layout(new Size(contentWidth, double.PositiveInfinity));
                var childMargin = child.Style?.Margin ?? new Thickness(0);
                
                currentY += childMargin.Top;
                
                var childBounds = child.Bounds;
                childBounds.X = Style.Padding.Left + Style.BorderThickness.Left + childMargin.Left;
                childBounds.Y = currentY;
                child.Bounds = childBounds;

                currentY += childBounds.Height + childMargin.Bottom;
            }
            return currentY - (Style.Padding.Top + Style.BorderThickness.Top); // Return content height
        }

        private double LayoutInlineChildren(double contentWidth)
        {
            var diagTag = Node?.Tag ?? "?";
            var diagDisp = Style?.Display ?? "null";
            DevToolsLogger.Log($"[DIAG:LAYOUT:INLINE] tag={diagTag} display={diagDisp} children={Children.Count} contentWidth={contentWidth:F0}");
            double startX = Style.Padding.Left + Style.BorderThickness.Left;
            double startY = Style.Padding.Top + Style.BorderThickness.Top;
            
            double currentX = startX;
            double currentY = startY;
            double currentRowHeight = 0;

            foreach (var child in Children)
            {
                if (child.Style != null && (child.Style.Position == "absolute" || child.Style.Position == "fixed")) continue;

                // Measure child
                child.Layout(new Size(contentWidth, double.PositiveInfinity));
                
                var childMargin = child.Style?.Margin ?? new Thickness(0);
                double childTotalWidth = child.Bounds.Width + childMargin.Left + childMargin.Right;
                double childTotalHeight = child.Bounds.Height + childMargin.Top + childMargin.Bottom;

                // Check if fits on current line
                if (currentX + childTotalWidth > startX + contentWidth && currentX > startX)
                {
                    // Wrap to next line
                    currentX = startX;
                    currentY += currentRowHeight;
                    currentRowHeight = 0;
                }

                // Position child
                var childBounds = child.Bounds;
                childBounds.X = currentX + childMargin.Left;
                childBounds.Y = currentY + childMargin.Top;
                child.Bounds = childBounds;

                DevToolsLogger.Log($"[DIAG:POS] parent={Node?.Tag} child={child.Node?.Tag ?? "txt"} x={childBounds.X:F0} y={childBounds.Y:F0} w={child.Bounds.Width:F0} h={child.Bounds.Height:F0}");

                // Advance
                currentX += childTotalWidth;
                currentRowHeight = Math.Max(currentRowHeight, childTotalHeight);
            }

            return (currentY + currentRowHeight) - startY;
        }

        private double LayoutFlexChildren(double contentWidth)
        {
            // Basic Flexbox Implementation
            var dir = Style.FlexDirection?.ToLowerInvariant() ?? "row";
            var wrap = Style.FlexWrap?.ToLowerInvariant() ?? "nowrap";
            var justify = Style.JustifyContent?.ToLowerInvariant() ?? "flex-start";
            var align = Style.AlignItems?.ToLowerInvariant() ?? "stretch";

            bool isRow = dir.Contains("row");
            bool isReverse = dir.Contains("reverse");
            bool canWrap = wrap == "wrap";

            double startX = Style.Padding.Left + Style.BorderThickness.Left;
            double startY = Style.Padding.Top + Style.BorderThickness.Top;
            
            double mainAxisCurrent = isRow ? startX : startY;
            double crossAxisCurrent = isRow ? startY : startX;
            double crossAxisMax = 0; // Max size in cross axis for current line

            // 1. Measure all children first (intrinsic sizes)
            foreach (var child in Children)
            {
                if (child.Style != null && (child.Style.Position == "absolute" || child.Style.Position == "fixed")) continue;

                // Constrain row children to contentWidth to prevent overflow
                double childAvailW = isRow ? contentWidth : contentWidth;
                child.Layout(new Size(childAvailW, double.PositiveInfinity));
            }

            // 2. Position children (Simplified: Single line or simple wrap, no shrinking/growing yet)
            // TODO: Implement FlexGrow/Shrink
            
            var lines = new System.Collections.Generic.List<System.Collections.Generic.List<RenderObject>>();
            var currentLine = new System.Collections.Generic.List<RenderObject>();
            lines.Add(currentLine);

            double currentMainSize = 0;

            foreach (var child in Children)
            {
                if (child.Style != null && (child.Style.Position == "absolute" || child.Style.Position == "fixed")) continue;

                var childMargin = child.Style?.Margin ?? new Thickness(0);
                double childMainSize = isRow 
                    ? child.Bounds.Width + childMargin.Left + childMargin.Right 
                    : child.Bounds.Height + childMargin.Top + childMargin.Bottom;
                
                double childCrossSize = isRow
                    ? child.Bounds.Height + childMargin.Top + childMargin.Bottom
                    : child.Bounds.Width + childMargin.Left + childMargin.Right;

                if (canWrap && currentMainSize + childMainSize > (isRow ? contentWidth : double.PositiveInfinity) && currentMainSize > 0)
                {
                    // Wrap
                    currentLine = new System.Collections.Generic.List<RenderObject>();
                    lines.Add(currentLine);
                    currentMainSize = 0;
                    // Reset cross axis max for new line? No, we need to track line heights.
                }

                currentLine.Add(child);
                currentMainSize += childMainSize;
            }

            // 3. Layout lines
            double totalCrossSize = 0;
            double totalMainSize = 0;
            
            foreach (var line in lines)
            {
                double lineCrossSize = 0;
                double lineMainSize = 0;
                double totalGrow = 0;

                // 1. Calculate initial line size and total grow
                foreach (var child in line)
                {
                    var childMargin = child.Style?.Margin ?? new Thickness(0);
                    double childMain = isRow
                        ? child.Bounds.Width + childMargin.Left + childMargin.Right
                        : child.Bounds.Height + childMargin.Top + childMargin.Bottom;
                    lineMainSize += childMain;
                    totalGrow += child.Style?.FlexGrow ?? 0;
                }

                // 2. Apply Flex Grow
                // Only apply if we have a finite constraint on the main axis
                double constraint = isRow ? contentWidth : double.PositiveInfinity; // TODO: Pass contentHeight for column
                double freeSpace = constraint - lineMainSize;

                // 2a. Apply Flex Shrink when overflowing
                if (freeSpace < 0 && !double.IsInfinity(freeSpace))
                {
                    double overflow = -freeSpace;
                    double totalShrink = 0;
                    foreach (var child in line)
                    {
                        double shrink = child.Style?.FlexShrink ?? 1;
                        double childMain = isRow
                            ? child.Bounds.Width + (child.Style?.Margin ?? new Thickness(0)).Left + (child.Style?.Margin ?? new Thickness(0)).Right
                            : child.Bounds.Height + (child.Style?.Margin ?? new Thickness(0)).Top + (child.Style?.Margin ?? new Thickness(0)).Bottom;
                        totalShrink += shrink * childMain;
                    }

                    if (totalShrink > 0)
                    {
                        double shrinkRemaining = overflow;
                        foreach (var child in line)
                        {
                            double shrink = child.Style?.FlexShrink ?? 1;
                            var childMargin = child.Style?.Margin ?? new Thickness(0);
                            double childMain = isRow
                                ? child.Bounds.Width + childMargin.Left + childMargin.Right
                                : child.Bounds.Height + childMargin.Top + childMargin.Bottom;
                            double shrinkAmount = (shrink * childMain / totalShrink) * overflow;
                            shrinkAmount = Math.Min(shrinkAmount, shrinkRemaining);

                            if (isRow)
                            {
                                double newWidth = Math.Max(0, child.Bounds.Width - shrinkAmount);
                                var oldWidth = child.Style.Width;
                                child.Style.Width = newWidth;
                                child.Layout(new Size(newWidth, double.PositiveInfinity));
                                child.Style.Width = oldWidth;
                                shrinkRemaining -= (childMain - (child.Bounds.Width + childMargin.Left + childMargin.Right));
                            }
                        }
                        // Recalculate lineMainSize after shrinking
                        lineMainSize = 0;
                        foreach (var child in line)
                        {
                            var m = child.Style?.Margin ?? new Thickness(0);
                            lineMainSize += isRow
                                ? child.Bounds.Width + m.Left + m.Right
                                : child.Bounds.Height + m.Top + m.Bottom;
                        }
                        freeSpace = constraint - lineMainSize;
                    }
                }

                if (freeSpace > 0 && totalGrow > 0 && !double.IsInfinity(freeSpace))
                {
                    foreach (var child in line)
                    {
                        double grow = child.Style?.FlexGrow ?? 0;
                        if (grow > 0)
                        {
                            double extra = freeSpace * (grow / totalGrow);
                            if (isRow)
                            {
                                double newWidth = child.Bounds.Width + extra;
                                
                                // Override width style to force layout to the new flex width
                                var oldWidth = child.Style.Width;
                                child.Style.Width = newWidth;

                                // Re-layout with fixed width
                                child.Layout(new Size(newWidth, double.PositiveInfinity));

                                // Restore width style
                                child.Style.Width = oldWidth;
                            }
                            else
                            {
                                // For column, we would adjust height, but we need to know width constraint
                                // child.Layout(new Size(child.Bounds.Width, child.Bounds.Height + extra));
                            }
                        }
                    }
                    lineMainSize = constraint;
                }

                // 3. Calculate Cross Size (after potential resize)
                foreach (var child in line)
                {
                    var childMargin = child.Style?.Margin ?? new Thickness(0);
                    double childCross = isRow
                        ? child.Bounds.Height + childMargin.Top + childMargin.Bottom
                        : child.Bounds.Width + childMargin.Left + childMargin.Right;
                    
                    lineCrossSize = Math.Max(lineCrossSize, childCross);
                }

                // Distribute main axis space (Justify Content)
                double remainingMain = (isRow ? contentWidth : 0) - lineMainSize; // Only for row? Column usually doesn't justify unless height is fixed.
                if (remainingMain < 0) remainingMain = 0;
                
                double startMain = isRow ? startX : startY;
                double gapMain = 0;

                if (isRow) // Justify applies to main axis
                {
                    if (justify == "center") startMain += remainingMain / 2;
                    else if (justify == "flex-end") startMain += remainingMain;
                    else if (justify == "space-between" && line.Count > 1) gapMain = remainingMain / (line.Count - 1);
                    else if (justify == "space-around") { gapMain = remainingMain / line.Count; startMain += gapMain / 2; }
                    else if (justify == "space-evenly")
                    {
                        if (line.Count > 0)
                        {
                            gapMain = remainingMain / (line.Count + 1);
                            startMain += gapMain;
                        }
                    }
                }

                double itemMainPos = startMain;

                foreach (var child in line)
                {
                    var childMargin = child.Style?.Margin ?? new Thickness(0);
                    var bounds = child.Bounds;

                    // Align Items (Cross Axis)
                    double itemCrossPos = isRow ? crossAxisCurrent : crossAxisCurrent; // Start of line
                    
                    // Stretch?
                    if (align == "stretch")
                    {
                        if (isRow) bounds.Height = Math.Max(bounds.Height, lineCrossSize - childMargin.Top - childMargin.Bottom);
                        else bounds.Width = Math.Max(bounds.Width, lineCrossSize - childMargin.Left - childMargin.Right);
                    }
                    else if (align == "center")
                    {
                        double childCross = isRow ? bounds.Height : bounds.Width;
                        double freeCross = lineCrossSize - childCross - (isRow ? (childMargin.Top + childMargin.Bottom) : (childMargin.Left + childMargin.Right));
                        itemCrossPos += freeCross / 2;
                    }
                    else if (align == "flex-end")
                    {
                        double childCross = isRow ? bounds.Height : bounds.Width;
                        double freeCross = lineCrossSize - childCross - (isRow ? (childMargin.Top + childMargin.Bottom) : (childMargin.Left + childMargin.Right));
                        itemCrossPos += freeCross;
                    }

                    if (isRow)
                    {
                        bounds.X = itemMainPos + childMargin.Left;
                        bounds.Y = itemCrossPos + childMargin.Top;
                        itemMainPos += bounds.Width + childMargin.Left + childMargin.Right + gapMain;
                    }
                    else
                    {
                        bounds.X = itemCrossPos + childMargin.Left;
                        bounds.Y = itemMainPos + childMargin.Top;
                        itemMainPos += bounds.Height + childMargin.Top + childMargin.Bottom + gapMain;
                    }
                    
                    child.Bounds = bounds;
                }

                crossAxisCurrent += lineCrossSize;
                totalCrossSize += lineCrossSize;
                totalMainSize = itemMainPos - startMain;
            }

            if (isRow)
                return totalCrossSize;
            else
                return totalMainSize;
        }

        private double LayoutGridChildren(double contentWidth)
        {
            var diagTag = Node?.Tag ?? "?";
            var cols = Style?.GridTemplateColumns;
            var rows = Style?.GridTemplateRows;
            var gap = Style?.Gap ?? Style?.ColumnGap ?? 0;
            var rowGap = Style?.Gap ?? Style?.RowGap ?? 0;

            DevToolsLogger.Log($"[DIAG:LAYOUT:GRID] tag={diagTag} cols='{cols}' rows='{rows}' gap={gap} children={Children.Count} contentWidth={contentWidth:F0}");

            if (string.IsNullOrWhiteSpace(cols) && string.IsNullOrWhiteSpace(rows))
            {
                cols = $"repeat({Children.Count}, 1fr)";
            }

            int numCols = ParseGridTrackCount(cols);
            int numRows = ParseGridTrackCount(rows);
            if (numCols == 0) numCols = Math.Max(1, Children.Count);
            if (numRows == 0) numRows = 1;

            double[] colSizes = ResolveGridTracks(cols, numCols, contentWidth, gap);
            double[] rowSizes = ResolveGridTracks(rows, numRows, double.PositiveInfinity, rowGap);

            int autoRowIdx = 0;
            for (int i = 0; i < Children.Count; i++)
            {
                var child = Children[i];
                if (child.Style != null && (child.Style.Position == "absolute" || child.Style.Position == "fixed")) continue;

                int col = 0, row = 0, colSpan = 1, rowSpan = 1;
                ParseGridItemPlacement(child, numCols, numRows, i, out col, out row, out colSpan, out rowSpan);

                if (row >= numRows)
                {
                    var newRowSizes = new double[row + 1];
                    Array.Copy(rowSizes, newRowSizes, rowSizes.Length);
                    for (int r = rowSizes.Length; r <= row; r++)
                        newRowSizes[r] = 0;
                    rowSizes = newRowSizes;
                    numRows = rowSizes.Length;
                }

                double cellX = 0;
                for (int c = 0; c < col; c++) cellX += colSizes[c] + (c < numCols - 1 ? gap : 0);
                double cellY = 0;
                for (int r = 0; r < row; r++) cellY += rowSizes[r] + (r < numRows - 1 ? rowGap : 0);

                double cellW = 0;
                for (int c = col; c < Math.Min(col + colSpan, numCols); c++)
                    cellW += colSizes[c] + (c > col ? gap : 0);
                double cellH = 0;
                for (int r = row; r < Math.Min(row + rowSpan, numRows); r++)
                    cellH += rowSizes[r] + (r > row ? rowGap : 0);

                if (colSpan == 1 && colSizes[col] == 0) cellW = contentWidth;
                if (rowSpan == 1 && rowSizes.Length > row && rowSizes[row] == 0)
                {
                    child.Layout(new Size(cellW, double.PositiveInfinity));
                    rowSizes[row] = Math.Max(rowSizes[row], child.Bounds.Height);
                    cellH = rowSizes[row];
                }
                else
                {
                    child.Layout(new Size(cellW, cellH > 0 ? cellH : double.PositiveInfinity));
                    if (row < rowSizes.Length && rowSizes[row] == 0)
                        rowSizes[row] = child.Bounds.Height;
                }

                var childMargin = child.Style?.Margin ?? new Thickness(0);
                var bounds = child.Bounds;
                bounds.X = cellX + childMargin.Left;
                bounds.Y = cellY + childMargin.Top;
                child.Bounds = bounds;
            }

            double totalHeight = 0;
            for (int r = 0; r < rowSizes.Length; r++)
                totalHeight += rowSizes[r] + (r < rowSizes.Length - 1 ? rowGap : 0);
            return totalHeight;
        }

        private int ParseGridTrackCount(string template)
        {
            if (string.IsNullOrWhiteSpace(template)) return 0;
            if (template.Trim() == "none") return 0;
            int count = 0;
            bool inRepeat = false;
            int repeatCount = 0;
            var parts = template.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var pt = p.Trim().ToLowerInvariant();
                if (pt.StartsWith("repeat("))
                {
                    inRepeat = true;
                    var inner = pt.Substring(7).TrimEnd(')');
                    var commaIdx = inner.IndexOf(',');
                    if (commaIdx > 0)
                    {
                        var countStr = inner.Substring(0, commaIdx).Trim();
                        if (int.TryParse(countStr, out repeatCount))
                        {
                            var trackDef = inner.Substring(commaIdx + 1).Trim();
                            int tracksInRepeat = trackDef.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                            count += repeatCount * tracksInRepeat;
                        }
                    }
                    inRepeat = false;
                }
                else if (pt == ",") { }
                else count++;
            }
            return Math.Max(1, count);
        }

        private double[] ResolveGridTracks(string template, int trackCount, double availableSize, double gap)
        {
            var result = new double[trackCount];
            if (string.IsNullOrWhiteSpace(template) || template.Trim() == "none")
            {
                double autoSize = trackCount > 0 ? (availableSize - gap * (trackCount - 1)) / trackCount : 0;
                for (int i = 0; i < trackCount; i++) result[i] = Math.Max(0, autoSize);
                return result;
            }

            var tokens = ExpandRepeat(template, trackCount);
            double totalFr = 0;
            int autoCount = 0;

            for (int i = 0; i < Math.Min(tokens.Count, trackCount); i++)
            {
                var t = tokens[i].Trim().ToLowerInvariant();
                if (t.EndsWith("fr"))
                {
                    double fr;
                    if (double.TryParse(t.Substring(0, t.Length - 2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fr))
                        totalFr += fr;
                }
                else if (t == "auto" || t == "minmax" || t.StartsWith("minmax"))
                {
                    autoCount++;
                }
                else
                {
                    double px;
                    if (TryParseGridLength(t, out px))
                        result[i] = px;
                    else
                        autoCount++;
                }
            }

            double totalGap = gap * Math.Max(0, trackCount - 1);
            double usedSpace = totalGap;
            for (int i = 0; i < trackCount; i++)
                if (result[i] > 0) usedSpace += result[i];
            double freeSpace = Math.Max(0, availableSize - usedSpace);

            for (int i = 0; i < Math.Min(tokens.Count, trackCount); i++)
            {
                var t = tokens[i].Trim().ToLowerInvariant();
                if (t.EndsWith("fr"))
                {
                    double fr;
                    if (double.TryParse(t.Substring(0, t.Length - 2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fr))
                        result[i] = totalFr > 0 ? freeSpace * (fr / totalFr) : 0;
                }
                else if (result[i] == 0)
                {
                    result[i] = autoCount > 0 ? freeSpace / autoCount : 0;
                }
            }

            return result;
        }

        private List<string> ExpandRepeat(string template, int targetCount)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < template.Length)
            {
                while (i < template.Length && char.IsWhiteSpace(template[i])) i++;
                if (i >= template.Length) break;

                if (template.Substring(i).StartsWith("repeat(", StringComparison.OrdinalIgnoreCase))
                {
                    int startParen = template.IndexOf('(', i);
                    int endParen = FindMatchingParen(template, startParen);
                    if (endParen > startParen)
                    {
                        var inner = template.Substring(startParen + 1, endParen - startParen - 1);
                        int commaIdx = FindUnquotedComma(inner);
                        if (commaIdx > 0)
                        {
                            var countStr = inner.Substring(0, commaIdx).Trim();
                            var trackDef = inner.Substring(commaIdx + 1).Trim();
                            int repeatCount;
                            if (countStr == "auto-fill" || countStr == "auto-fit")
                                repeatCount = targetCount;
                            else if (int.TryParse(countStr, out repeatCount))
                            { }
                            else
                                repeatCount = 1;

                            var trackTokens = trackDef.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int r = 0; r < repeatCount; r++)
                                foreach (var tt in trackTokens)
                                    tokens.Add(tt);
                        }
                        i = endParen + 1;
                        continue;
                    }
                }

                int nextSpace = i;
                while (nextSpace < template.Length && !char.IsWhiteSpace(template[nextSpace]) && template[nextSpace] != ',') nextSpace++;
                if (nextSpace > i) tokens.Add(template.Substring(i, nextSpace - i));
                i = nextSpace;
                if (i < template.Length && template[i] == ',') i++;
            }
            while (tokens.Count < targetCount) tokens.Add("auto");
            return tokens;
        }

        private int FindMatchingParen(string s, int openIdx)
        {
            int depth = 0;
            for (int i = openIdx; i < s.Length; i++)
            {
                if (s[i] == '(') depth++;
                else if (s[i] == ')') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private int FindUnquotedComma(string s)
        {
            int depth = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '(') depth++;
                else if (s[i] == ')') depth--;
                else if (s[i] == ',' && depth == 0) return i;
            }
            return -1;
        }

        private bool TryParseGridLength(string s, out double px)
        {
            px = 0;
            s = s.Trim();
            if (s.EndsWith("px"))
            {
                return double.TryParse(s.Substring(0, s.Length - 2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out px);
            }
            if (s.EndsWith("%"))
            {
                double pct;
                if (double.TryParse(s.Substring(0, s.Length - 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pct))
                { px = pct; return true; }
            }
            return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out px);
        }

        private void ParseGridItemPlacement(RenderObject child, int numCols, int numRows, int autoIndex,
            out int col, out int row, out int colSpan, out int rowSpan)
        {
            col = 0; row = 0; colSpan = 1; rowSpan = 1;

            var gridCol = child.Style?.GridColumn;
            var gridRow = child.Style?.GridRow;
            var gridArea = child.Style?.GridArea;

            if (!string.IsNullOrWhiteSpace(gridArea))
            {
                var areaParts = gridArea.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (areaParts.Length >= 2)
                {
                    col = ParseGridLine(areaParts[0].Trim(), numCols) - 1;
                    row = ParseGridLine(areaParts[1].Trim(), numRows) - 1;
                    if (areaParts.Length >= 4)
                    {
                        int endCol = ParseGridLine(areaParts[2].Trim(), numCols);
                        int endRow = ParseGridLine(areaParts[3].Trim(), numRows);
                        colSpan = Math.Max(1, endCol - col);
                        rowSpan = Math.Max(1, endRow - row);
                    }
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(gridCol))
            {
                var parts = gridCol.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                col = ParseGridLine(parts[0].Trim(), numCols) - 1;
                if (parts.Length > 1)
                {
                    int endCol = ParseGridLine(parts[1].Trim(), numCols);
                    colSpan = Math.Max(1, endCol - col);
                }
            }

            if (!string.IsNullOrWhiteSpace(gridRow))
            {
                var parts = gridRow.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                row = ParseGridLine(parts[0].Trim(), numRows) - 1;
                if (parts.Length > 1)
                {
                    int endRow = ParseGridLine(parts[1].Trim(), numRows);
                    rowSpan = Math.Max(1, endRow - row);
                }
            }

            if (string.IsNullOrWhiteSpace(gridCol) && string.IsNullOrWhiteSpace(gridRow))
            {
                col = autoIndex % numCols;
                row = autoIndex / numCols;
            }

            col = Math.Max(0, Math.Min(col, numCols - 1));
            row = Math.Max(0, row);
        }

        private int ParseGridLine(string s, int max)
        {
            s = s.Trim().ToLowerInvariant();
            if (s == "auto" || s == "span") return max + 1;
            if (s.StartsWith("span "))
            {
                int span;
                if (int.TryParse(s.Substring(5).Trim(), out span)) return span;
                return 1;
            }
            int val;
            if (int.TryParse(s, out val)) return val;
            return max + 1;
        }

        private double LayoutTableChildren(double contentWidth)
        {
            if (TableOccupied == null || TableRows == 0 || TableCols == 0)
                return LayoutFlexChildren(contentWidth);

            var gap = Style?.BorderSpacing ?? 1.0;
            var colWidths = new double[TableCols];
            var rowHeights = new double[TableRows];

            // Pass 1: layout cells to determine natural column widths and row heights
            for (int r = 0; r < TableRows; r++)
            {
                for (int c = 0; c < TableCols; c++)
                {
                    if (!TableOccupied[r, c]) continue;
                    // Find the cell at this position
                    int childIdx = -1;
                    for (int i = 0; i < Children.Count; i++)
                    {
                        var child = Children[i];
                        var childTag = child.Node?.Tag?.ToUpperInvariant();
                        if (childTag != "TD" && childTag != "TH") continue;
                        // Find which grid position this child maps to
                        // by checking its stored table placement
                        if (child.TableRow == r && child.TableCol == c)
                        {
                            childIdx = i;
                            break;
                        }
                    }
                    if (childIdx < 0) continue;
                    var cell = Children[childIdx];
                    int cs = cell.TableColSpan > 0 ? cell.TableColSpan : 1;
                    int rs = cell.TableRowSpan > 0 ? cell.TableRowSpan : 1;

                    double availW = 0;
                    for (int cc = c; cc < Math.Min(c + cs, TableCols); cc++)
                        availW += colWidths[cc] + gap;
                    if (availW <= 0) availW = contentWidth / Math.Max(1, TableCols) * cs;

                    cell.Layout(new Size(availW, double.PositiveInfinity));
                    double cellH = cell.Bounds.Height;
                    double cellW = cell.Bounds.Width;

                    // Distribute width across spanned columns
                    if (cs == 1)
                        colWidths[c] = Math.Max(colWidths[c], cellW);
                    else
                    {
                        double perCol = cellW / cs;
                        for (int cc = c; cc < Math.Min(c + cs, TableCols); cc++)
                            colWidths[cc] = Math.Max(colWidths[cc], perCol);
                    }

                    // Distribute height across spanned rows
                    if (rs == 1)
                        rowHeights[r] = Math.Max(rowHeights[r], cellH);
                    else
                    {
                        // Give each spanned row an equal share, but respect existing row heights
                        double perRow = cellH / rs;
                        for (int rr = r; rr < Math.Min(r + rs, TableRows); rr++)
                            rowHeights[rr] = Math.Max(rowHeights[rr], perRow);
                    }
                }
            }

            // Normalize column widths to fit contentWidth
            double totalW = 0;
            for (int c = 0; c < TableCols; c++) totalW += colWidths[c] + gap;
            if (totalW > contentWidth && totalW > 0)
            {
                double scale = contentWidth / totalW;
                for (int c = 0; c < TableCols; c++) colWidths[c] *= scale;
            }
            else if (totalW < contentWidth && TableCols > 0)
            {
                double extra = (contentWidth - totalW) / TableCols;
                for (int c = 0; c < TableCols; c++) colWidths[c] += extra;
            }

            // Pass 2: position cells
            for (int r = 0; r < TableRows; r++)
            {
                for (int c = 0; c < TableCols; c++)
                {
                    if (!TableOccupied[r, c]) continue;
                    var cell = Children.FirstOrDefault(ch =>
                        ch.Node?.Tag != null &&
                        (ch.Node.Tag.ToUpperInvariant() == "TD" || ch.Node.Tag.ToUpperInvariant() == "TH") &&
                        ch.TableRow == r && ch.TableCol == c);
                    if (cell == null) continue;

                    int cs = cell.TableColSpan > 0 ? cell.TableColSpan : 1;
                    int rs = cell.TableRowSpan > 0 ? cell.TableRowSpan : 1;

                    double x = 0;
                    for (int cc = 0; cc < c; cc++) x += colWidths[cc] + gap;
                    double y = 0;
                    for (int rr = 0; rr < r; rr++) y += rowHeights[rr] + gap;
                    double w = 0;
                    for (int cc = c; cc < Math.Min(c + cs, TableCols); cc++)
                        w += colWidths[cc] + (cc > c ? gap : 0);
                    double h = 0;
                    for (int rr = r; rr < Math.Min(r + rs, TableRows); rr++)
                        h += rowHeights[rr] + (rr > r ? gap : 0);

                    cell.Layout(new Size(w, h > 0 ? h : double.PositiveInfinity));
                    var bounds = cell.Bounds;
                    bounds.X = x;
                    bounds.Y = y;
                    bounds.Width = w;
                    bounds.Height = h > 0 ? h : cell.Bounds.Height;
                    cell.Bounds = bounds;
                }
            }

            double totalHeight = 0;
            for (int r = 0; r < TableRows; r++) totalHeight += rowHeights[r] + gap;
            return totalHeight;
        }

        private void LayoutAbsoluteChildren(Size availableSize)
        {
            foreach (var child in Children)
            {
                if (child.Style != null && (child.Style.Position == "absolute" || child.Style.Position == "fixed"))
                {
                    // Measure
                    // If left/right are both set, width is constrained.
                    // If top/bottom are both set, height is constrained.
                    
                    double x = 0;
                    double y = 0;
                    
                    // Simplified: Just layout with available size
                    child.Layout(availableSize);
                    
                    var bounds = child.Bounds;
                    
                    if (child.Style.Left.HasValue) x = child.Style.Left.Value;
                    else if (child.Style.Right.HasValue) x = availableSize.Width - child.Style.Right.Value - bounds.Width;
                    
                    if (child.Style.Top.HasValue) y = child.Style.Top.Value;
                    else if (child.Style.Bottom.HasValue) y = availableSize.Height - child.Style.Bottom.Value - bounds.Height;

                    bounds.X = x;
                    bounds.Y = y;
                    child.Bounds = bounds;
                }
            }
        }

        private static double EnsureValid(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                return 0;
            return value;
        }
    }
}
