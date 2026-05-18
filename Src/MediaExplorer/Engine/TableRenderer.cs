using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace BrowserCore.Engine
{
    // Table rendering partial for DomBasicRenderer
    public partial class DomBasicRenderer
    {
        // ---------- Tables ----------
        private sealed class CellPlacement
        {
            public LiteElement Cell;
            public int Row;
            public int Col;
            public int RowSpan = 1;
            public int ColSpan = 1;
        }

        private GridLength? ParseColWidth(LiteElement col)
        {
            if (col == null) return null;

            if (col.Attr != null)
            {
                string w;
                if (col.Attr.TryGetValue("width", out w) && !string.IsNullOrWhiteSpace(w))
                {
                    w = w.Trim();
                    if (w == "*") return new GridLength(1, GridUnitType.Star);
                    if (w.EndsWith("%"))
                    {
                        double d;
                        if (double.TryParse(w.TrimEnd('%'), out d))
                            return new GridLength(d, GridUnitType.Star);
                    }
                    double px;
                    if (double.TryParse(w.Replace("px", ""), out px))
                        return new GridLength(px);
                }
            }

            try
            {
                var css = TryGetCss(col);
                if (css != null && css.Width.HasValue) return new GridLength(css.Width.Value);
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/TableRenderer.cs] empty catch empty catch"); }

            return null;
        }

        private async Task<FrameworkElement> MakeTableAsync(LiteElement table, Uri baseUri, Action<Uri> onNavigate, JavaScriptEngine js, CancellationToken ct)
        {
            var rows = new List<LiteElement>();
            if (table.Children != null)
            {
                foreach (var ch in table.Children)
                {
                    if (ch.Tag == "tr") rows.Add(ch);
                    else if (ch.Tag == "thead" || ch.Tag == "tbody" || ch.Tag == "tfoot")
                    {
                        if (ch.Children != null)
                        {
                            foreach (var tr in ch.Children) if (tr.Tag == "tr") rows.Add(tr);
                        }
                    }
                }
            }

            if (rows.Count == 0)
            {
                var emptyPanel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 6, 0, 6) };
                if (table.Children != null)
                {
                    foreach (var ch in table.Children)
                    {
                        var elt = await RenderNodeAsync(ch, baseUri, onNavigate, js, ct);
                        if (elt != null) emptyPanel.Children.Add(elt);
                    }
                }
                return Finish(emptyPanel, table);
            }

            int maxCols = 1;
            foreach (var r in rows)
            {
                int sum = 0;
                foreach (var c in r.Children)
                    if (c.Tag == "td" || c.Tag == "th")
                        sum += GetIntAttr(c, "colspan", 1);
                if (sum > maxCols) maxCols = sum;
            }

            int rowCount = rows.Count;
            long totalCells = (long)rowCount * maxCols;
            if (totalCells > 2000000 || maxCols > 5000 || rowCount > 5000)
            {
                return Finish(await RenderTableFallback(table, rows, baseUri, onNavigate, js, ct), table);
            }

            var occ = new bool[rowCount, maxCols];
            var placements = new List<CellPlacement>();

            for (int r = 0; r < rowCount; r++)
            {
                int cIndex = 0;
                foreach (var cell in rows[r].Children.Where(c => c.Tag == "td" || c.Tag == "th"))
                {
                    int colspan = GetIntAttr(cell, "colspan", 1);
                    int rowspan = GetIntAttr(cell, "rowspan", 1);

                    while (cIndex < maxCols && occ[r, cIndex]) cIndex++;
                    if (cIndex >= maxCols) cIndex = maxCols - 1;

                    placements.Add(new CellPlacement
                    {
                        Cell = cell,
                        Row = r,
                        Col = cIndex,
                        RowSpan = Math.Max(1, Math.Min(rowspan, rowCount - r)),
                        ColSpan = Math.Max(1, Math.Min(colspan, maxCols - cIndex))
                    });

                    for (int rr = r; rr < r + rowspan && rr < rowCount; rr++)
                        for (int cc = cIndex; cc < cIndex + colspan && cc < maxCols; cc++)
                            occ[rr, cc] = true;

                    cIndex += colspan;
                }
            }

            int headerRows = 0;
            for (int r = 0; r < rowCount; r++)
            {
                var row = rows[r];
                bool inThead = (row.Parent != null && string.Equals(row.Parent.Tag, "thead", StringComparison.OrdinalIgnoreCase));
                bool allTh = true;
                foreach (var c in row.Children) { if (!(c.Tag == "th")) { allTh = false; break; } }
                if (inThead || allTh) headerRows++;
                else break;
            }

            var colWidths = new GridLength[Math.Min(maxCols, 5000)];
            for (int i = 0; i < maxCols; i++) colWidths[i] = GridLength.Auto;

            try
            {
                int colIdx = 0;
                if (table.Children != null)
                {
                    foreach (var ch in table.Children)
                    {
                        if (ch.Tag == "colgroup")
                        {
                            if (ch.Children != null)
                            {
                                foreach (var c in ch.Children)
                                {
                                    if (c.Tag == "col")
                                    {
                                        int span = GetIntAttr(c, "span", 1);
                                        var w = ParseColWidth(c);
                                        for (int k = 0; k < span && colIdx < maxCols; k++)
                                        {
                                            if (w.HasValue) colWidths[colIdx] = w.Value;
                                            colIdx++;
                                        }
                                    }
                                }
                            }
                        }
                        else if (ch.Tag == "col")
                        {
                            int span = GetIntAttr(ch, "span", 1);
                            var w = ParseColWidth(ch);
                            for (int k = 0; k < span && colIdx < maxCols; k++)
                            {
                                if (w.HasValue) colWidths[colIdx] = w.Value;
                                colIdx++;
                            }
                        }
                    }
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/TableRenderer.cs] empty catch empty catch"); }

            // Read border-spacing from table's computed CSS
            double cellSpacingH = 0, cellSpacingV = 0;
            try
            {
                var tableCss = TryGetCss(table);
                if (tableCss != null)
                {
                    if (tableCss.BorderSpacing.HasValue) cellSpacingH = tableCss.BorderSpacing.Value;
                    if (tableCss.BorderSpacingVertical.HasValue) cellSpacingV = tableCss.BorderSpacingVertical.Value;
                    else cellSpacingV = cellSpacingH;
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/TableRenderer.cs] empty catch empty catch"); }

            var headerGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            var bodyGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            for (int c = 0; c < maxCols; c++)
            {
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = colWidths[c] });
                bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = colWidths[c] });
            }
            for (int r = 0; r < headerRows; r++) headerGrid.RowDefinitions.Add(new RowDefinition());
            for (int r = 0; r < (rowCount - headerRows); r++) bodyGrid.RowDefinitions.Add(new RowDefinition());

            double hs = cellSpacingH / 2, vs = cellSpacingV / 2;
            Func<Grid, int, int, int, int, FrameworkElement, Border> addCell = (g, row, col, rs, cs, content) =>
            {
                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Windows.UI.Colors.DimGray),
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(6, 4, 6, 4),
                    Margin = new Thickness(hs, vs, hs, vs),
                    Child = content
                };
                Grid.SetRow(border, row);
                Grid.SetColumn(border, col);
                if (rs > 1) Grid.SetRowSpan(border, rs);
                if (cs > 1) Grid.SetColumnSpan(border, cs);
                g.Children.Add(border);
                return border;
            };

            foreach (var place in placements)
            {
                ct.ThrowIfCancellationRequested();

                var content = await RenderNodeAsync(place.Cell, baseUri, onNavigate, js, ct);
                if (content == null)
                {
                    content = new TextBlock
                    {
                        Text = CollapseWs(GatherText(place.Cell)),
                        TextWrapping = TextWrapping.WrapWholeWords
                    };
                }

                var tb = content as TextBlock;
                if (place.Cell.Tag == "th" && tb != null) tb.FontWeight = FontWeights.SemiBold;

                if (place.Row < headerRows)
                {
                    int localRow = place.Row;
                    int rs = Math.Min(place.RowSpan, Math.Max(1, headerRows - localRow));
                    var b = addCell(headerGrid, localRow, place.Col, rs, place.ColSpan, content);
                    try { b.Background = new SolidColorBrush(Windows.UI.Colors.LightGray); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/TableRenderer.cs] empty catch empty catch"); }
                }
                else
                {
                    int localRow = place.Row - headerRows;
                    int rs = Math.Min(place.RowSpan, Math.Max(1, (rowCount - headerRows) - localRow));
                    addCell(bodyGrid, localRow, place.Col, rs, place.ColSpan, content);
                }
            }

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(headerGrid, 0);
            root.Children.Add(headerGrid);

            FrameworkElement bodyHost;
            {
                var scroller = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = bodyGrid
                };
                bodyHost = scroller;
            }
            Grid.SetRow(bodyHost, 1);
            root.Children.Add(bodyHost);

            bool syncing = false;
            Action syncCols = () =>
            {
                if (syncing) return;
                syncing = true;
                try
                {
                    for (int i = 0; i < maxCols; i++)
                    {
                        var target = Math.Max(headerGrid.ColumnDefinitions[i].ActualWidth, bodyGrid.ColumnDefinitions[i].ActualWidth);
                        if (double.IsNaN(target) || target <= 0) continue;
                        var hCur = headerGrid.ColumnDefinitions[i].Width;
                        var bCur = bodyGrid.ColumnDefinitions[i].Width;
                        bool changeH = hCur.IsAuto || Math.Abs(hCur.Value - target) > 0.5;
                        bool changeB = bCur.IsAuto || Math.Abs(bCur.Value - target) > 0.5;
                        if (changeH) headerGrid.ColumnDefinitions[i].Width = new GridLength(target);
                        if (changeB) bodyGrid.ColumnDefinitions[i].Width = new GridLength(target);
                    }
                }
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/TableRenderer.cs] empty catch empty catch"); }
                finally { syncing = false; }
            };
            headerGrid.Loaded += (s, e) => { try { syncCols(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/TableRenderer.cs] empty catch empty catch"); } };
            bodyGrid.Loaded += (s, e) => { try { syncCols(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/TableRenderer.cs] empty catch empty catch"); } };
            bodyGrid.SizeChanged += (s, e) => { try { syncCols(); } catch { System.Diagnostics.Debug.WriteLine(" [Engine/TableRenderer.cs] empty catch empty catch"); } };

            return Finish(root, table);
        }

        private async Task<FrameworkElement> RenderTableFallback(LiteElement table, List<LiteElement> rows, Uri baseUri, Action<Uri> onNavigate, JavaScriptEngine js, CancellationToken ct)
        {
            double cellSpacingH = 0, cellSpacingV = 0;
            try
            {
                var tableCss = TryGetCss(table);
                if (tableCss != null)
                {
                    if (tableCss.BorderSpacing.HasValue) cellSpacingH = tableCss.BorderSpacing.Value;
                    if (tableCss.BorderSpacingVertical.HasValue) cellSpacingV = tableCss.BorderSpacingVertical.Value;
                    else cellSpacingV = cellSpacingH;
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/TableRenderer.cs] empty catch empty catch"); }

            var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 6, 0, 6) };
            foreach (var r in rows)
            {
                var rowPanel = new StackPanel { Orientation = Orientation.Horizontal };
                if (r.Children != null)
                {
                    foreach (var c in r.Children.Where(ch => ch.Tag == "td" || ch.Tag == "th"))
                    {
                        var content = await RenderNodeAsync(c, baseUri, onNavigate, js, ct);
                        if (content != null)
                        {
                            content.Margin = new Thickness(cellSpacingH / 2, cellSpacingV / 2, cellSpacingH / 2, cellSpacingV / 2);
                            rowPanel.Children.Add(content);
                        }
                    }
                }
                panel.Children.Add(rowPanel);
            }
            return panel;
        }
    }
}
