using System;
using System.Collections.Generic;
using System.Linq;

namespace BrowserCore.Engine.Core
{
    public static class RenderTreeBuilder
    {
        private static int _diagBoxCount = 0;
        private static int _diagTextCount = 0;
        private static int _diagFilteredCount = 0;

        public static RenderObject Build(LiteElement root, Dictionary<LiteElement, CssComputed> styles)
        {
            return Build(root, styles, null);
        }

        public static RenderObject Build(LiteElement root, Dictionary<LiteElement, CssComputed> styles, Dictionary<LiteElement, RenderObject> mapping)
        {
            if (root == null) return null;

            _diagBoxCount = 0;
            _diagTextCount = 0;
            _diagFilteredCount = 0;

            var result = BuildInternal(root, styles, mapping);

            System.Diagnostics.Debug.WriteLine("[DIAG] RenderTreeBuilder.Build DONE boxes=" + _diagBoxCount + " text=" + _diagTextCount + " filtered=" + _diagFilteredCount);
            return result;
        }

        public static RenderObject BuildSubtree(LiteElement node, Dictionary<LiteElement, CssComputed> styles, Dictionary<LiteElement, RenderObject> mapping)
        {
            if (node == null) return null;
            return BuildInternal(node, styles, mapping);
        }

        private static RenderObject BuildInternal(LiteElement root, Dictionary<LiteElement, CssComputed> styles, Dictionary<LiteElement, RenderObject> mapping)
        {
            if (root == null) return null;

            RenderObject renderNode = null;

            if (root.IsText)
            {
                if (!string.IsNullOrWhiteSpace(root.Text))
                {
                    renderNode = new RenderText { Text = root.Text, Node = root };
                    _diagTextCount++;
                    if (mapping != null) mapping[root] = renderNode;
                }
            }
            else
            {
                string tag = root.Tag?.ToUpperInvariant();
                if (tag == "HEAD" || tag == "STYLE" || tag == "SCRIPT" || tag == "META" || tag == "TITLE" || tag == "LINK")
                {
                    _diagFilteredCount++;
                    return null;
                }

                var box = new RenderBox { Node = root };
                renderNode = box;
                _diagBoxCount++;
                if (mapping != null) mapping[root] = box;

                if (styles != null && styles.TryGetValue(root, out var css))
                    box.Style = css;
                else
                    box.Style = new CssComputed();

                ApplyUserAgentStyles(box);

                if (tag == "LI")
                {
                    bool suppressMarker = box.Style != null &&
                        string.Equals(box.Style.ListStyleType, "none", StringComparison.OrdinalIgnoreCase);
                    if (!suppressMarker)
                    {
                        var parentTag = root.Parent?.Tag?.ToUpperInvariant();
                        string marker = null;
                        if (parentTag == "UL") marker = "\u2022 ";
                        else if (parentTag == "OL")
                        {
                            int index = 1;
                            var siblings = root.Parent.Children;
                            for (int i = 0; i < siblings.Count; i++)
                            {
                                if (siblings[i] == root) break;
                                if (siblings[i].Tag?.ToUpperInvariant() == "LI") index++;
                            }
                            marker = $"{index}. ";
                        }
                        if (marker != null)
                        {
                            var markerNode = new RenderText
                            {
                                Text = marker,
                                Style = box.Style,
                                Parent = box
                            };
                            box.AddChild(markerNode);
                        }
                    }
                }

                if (root.Children != null)
                {
                    foreach (var child in root.Children)
                    {
                        var childRender = BuildInternal(child, styles, mapping);
                        if (childRender != null)
                            box.AddChild(childRender);
                    }
                }

                if (tag == "DIV" && root.Attr != null)
                {
                    string cls;
                    if (root.Attr.TryGetValue("class", out cls) && cls != null && cls.IndexOf("votearrow", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var arrowNode = new RenderText
                        {
                            Text = "\u25B2",
                            Style = box.Style,
                            Parent = box
                        };
                        box.Style.Display = "inline";
                        box.Style.ForegroundColor = Windows.UI.Color.FromArgb(255, 136, 136, 136);
                        box.Style.FontSize = 10;
                        box.AddChild(arrowNode);
                    }
                }
            }

            return renderNode;
        }

        private static void ApplyUserAgentStyles(RenderBox box)
        {
            if (box == null || box.Node == null || box.Style == null) return;

            var tag = box.Node.Tag?.ToUpperInvariant();
            if (string.IsNullOrEmpty(tag)) return;

            if (box.Style.Display == null)
            {
                if (tag == "#DOCUMENT" || tag == "HTML" || tag == "BODY" || tag == "DIV" || tag == "P" || tag == "H1" || tag == "H2" || tag == "H3" || tag == "H4" || tag == "H5" || tag == "H6" || tag == "UL" || tag == "OL" || tag == "HEADER" || tag == "FOOTER" || tag == "MAIN" || tag == "SECTION" || tag == "ARTICLE" || tag == "NAV" || tag == "HR" || tag == "PRE" || tag == "BLOCKQUOTE" || tag == "DT" || tag == "DD")
                    box.Style.Display = "block";
                else if (tag == "LI")
                {
                    bool inNavOrFooter = false;
                    var p = box.Node;
                    while (p != null)
                    {
                         var pt = p.Tag?.ToUpperInvariant();
                         var pid = p.Id?.ToLowerInvariant() ?? "";
                         var pclass = p.GetAttribute("class")?.ToLowerInvariant() ?? "";
                         if (pt == "NAV" || pt == "FOOTER" ||
                             pid.Contains("nav") || pid.Contains("menu") || pid.Contains("footer") ||
                             pclass.Contains("nav") || pclass.Contains("menu") || pclass.Contains("footer"))
                         { inNavOrFooter = true; break; }
                         if (pt == "BODY") break;
                         p = p.Parent;
                    }
                    if (inNavOrFooter)
                    {
                        box.Style.Display = "inline-block";
                        if (box.Style.ListStyleType == null) box.Style.ListStyleType = "none";
                    }
                    else
                        box.Style.Display = "block";
                }
                else if (tag == "TABLE") { box.Style.Display = "flex"; box.Style.FlexDirection = "column"; }
                else if (tag == "TR") { box.Style.Display = "flex"; box.Style.FlexDirection = "row"; }
                else if (tag == "CENTER") { box.Style.Display = "block"; }
                else if (tag == "INPUT" || tag == "BUTTON" || tag == "SELECT" || tag == "IMG" || tag == "TEXTAREA")
                    box.Style.Display = "inline-block";
                else if (tag == "A" || tag == "SPAN" || tag == "B" || tag == "I" || tag == "STRONG" || tag == "EM" || tag == "LABEL" || tag == "CODE" || tag == "TH" || tag == "TD")
                {
                    if (tag == "TH" || tag == "TD")
                    {
                        box.Style.Display = "block";
                        if (IsZero(box.Style.Padding)) box.Style.Padding = new Windows.UI.Xaml.Thickness(4);
                    }
                    else
                        box.Style.Display = "inline";
                }
                else
                    box.Style.Display = "inline";
            }

            ApplyHtmlAttributes(box, tag);

            if (tag == "SPAN" && string.Equals(box.Style.Display, "block", StringComparison.OrdinalIgnoreCase))
            {
                box.Style.Display = "inline";
            }

            if (tag == "B" && string.Equals(box.Style.Display, "block", StringComparison.OrdinalIgnoreCase))
            {
                var parentBox = box.Parent;
                if (parentBox != null && parentBox.Style != null &&
                    !string.IsNullOrEmpty(parentBox.Style.Display) &&
                    !string.Equals(parentBox.Style.Display, "flex", StringComparison.OrdinalIgnoreCase))
                    box.Style.Display = "inline";
            }

            if (IsZero(box.Style.Margin))
            {
                if (tag == "BODY") box.Style.Margin = new Windows.UI.Xaml.Thickness(8);
                else if (tag == "P") box.Style.Margin = new Windows.UI.Xaml.Thickness(0, 16, 0, 16);
                else if (tag == "H1") box.Style.Margin = new Windows.UI.Xaml.Thickness(0, 21, 0, 21);
                else if (tag == "H2") box.Style.Margin = new Windows.UI.Xaml.Thickness(0, 19, 0, 19);
                else if (tag == "H3") box.Style.Margin = new Windows.UI.Xaml.Thickness(0, 18, 0, 18);
                else if (tag == "UL" || tag == "OL") box.Style.Margin = new Windows.UI.Xaml.Thickness(20, 16, 0, 16);
                else if (tag == "HR") box.Style.Margin = new Windows.UI.Xaml.Thickness(0, 8, 0, 8);
                else if (tag == "BLOCKQUOTE") box.Style.Margin = new Windows.UI.Xaml.Thickness(40, 16, 40, 16);
                else if (tag == "DD") box.Style.Margin = new Windows.UI.Xaml.Thickness(40, 0, 0, 0);
            }

            if (tag == "H1") { if (!box.Style.FontSize.HasValue) box.Style.FontSize = 32; if (!box.Style.FontWeight.HasValue) box.Style.FontWeight = Windows.UI.Text.FontWeights.Bold; }
            else if (tag == "H2") { if (!box.Style.FontSize.HasValue) box.Style.FontSize = 24; if (!box.Style.FontWeight.HasValue) box.Style.FontWeight = Windows.UI.Text.FontWeights.Bold; }
            else if (tag == "H3") { if (!box.Style.FontSize.HasValue) box.Style.FontSize = 18; if (!box.Style.FontWeight.HasValue) box.Style.FontWeight = Windows.UI.Text.FontWeights.Bold; }
            else if (tag == "B" || tag == "STRONG" || tag == "TH") { if (!box.Style.FontWeight.HasValue) box.Style.FontWeight = Windows.UI.Text.FontWeights.Bold; }
            else if (tag == "I" || tag == "EM") { if (!box.Style.FontStyle.HasValue) box.Style.FontStyle = Windows.UI.Text.FontStyle.Italic; }
            else if (tag == "U") { if (box.Style.TextDecoration == null) box.Style.TextDecoration = "underline"; }
            else if (tag == "S" || tag == "STRIKE" || tag == "DEL") { if (box.Style.TextDecoration == null) box.Style.TextDecoration = "line-through"; }
            else if (tag == "MARK")
            {
                if (!box.Style.BackgroundColor.HasValue) box.Style.BackgroundColor = Windows.UI.Colors.Yellow;
                if (!box.Style.ForegroundColor.HasValue) box.Style.ForegroundColor = Windows.UI.Colors.Black;
            }
            else if (tag == "CODE")
            {
                if (string.IsNullOrEmpty(box.Style.FontFamilyName)) box.Style.FontFamilyName = "Consolas";
                if (!box.Style.BackgroundColor.HasValue) box.Style.BackgroundColor = Windows.UI.Color.FromArgb(255, 238, 238, 238);
                if (IsZero(box.Style.Padding)) box.Style.Padding = new Windows.UI.Xaml.Thickness(2, 0, 2, 0);
                if (box.Style.BorderRadius == default(Windows.UI.Xaml.CornerRadius)) box.Style.BorderRadius = new Windows.UI.Xaml.CornerRadius(3);
            }
            else if (tag == "BLOCKQUOTE")
            {
                if (IsZero(box.Style.Margin)) box.Style.Margin = new Windows.UI.Xaml.Thickness(40, 16, 40, 16);
                if (!box.Style.BorderBrushColor.HasValue) box.Style.BorderBrushColor = Windows.UI.Colors.Gray;
                if (IsZero(box.Style.BorderThickness)) box.Style.BorderThickness = new Windows.UI.Xaml.Thickness(4, 0, 0, 0);
                if (IsZero(box.Style.Padding)) box.Style.Padding = new Windows.UI.Xaml.Thickness(10, 0, 0, 0);
            }
            else if (tag == "A") { if (!box.Style.ForegroundColor.HasValue) box.Style.ForegroundColor = Windows.UI.Colors.Blue; }
            else if (tag == "PRE") { if (string.IsNullOrEmpty(box.Style.FontFamilyName)) box.Style.FontFamilyName = "Consolas"; }

            if (tag == "HR")
            {
                if (box.Style.Height == null) box.Style.Height = 1;
                if (box.Style.BorderThickness == null) box.Style.BorderThickness = new Windows.UI.Xaml.Thickness(1);
                if (!box.Style.BorderBrushColor.HasValue) box.Style.BorderBrushColor = Windows.UI.Colors.Gray;
            }
        }

        private static void ApplyHtmlAttributes(RenderBox box, string tag)
        {
            var attr = box.Node.Attr;
            if (attr == null) return;

            string bgColor;
            if (attr.TryGetValue("bgcolor", out bgColor) && !string.IsNullOrWhiteSpace(bgColor))
            {
                var parsed = TryParseHtmlColor(bgColor);
                if (parsed.HasValue && !box.Style.BackgroundColor.HasValue)
                    box.Style.BackgroundColor = parsed.Value;
                if ((tag == "TD" || tag == "TH") && !box.Style.FlexGrow.HasValue)
                    box.Style.FlexGrow = 1;
            }

            string color;
            if (attr.TryGetValue("color", out color) && !string.IsNullOrWhiteSpace(color))
            {
                var parsed = TryParseHtmlColor(color);
                if (parsed.HasValue && !box.Style.ForegroundColor.HasValue)
                    box.Style.ForegroundColor = parsed.Value;
            }

            string width;
            if (attr.TryGetValue("width", out width) && !string.IsNullOrWhiteSpace(width))
            {
                if (width.EndsWith("%"))
                {
                    double pct;
                    if (double.TryParse(width.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pct))
                    {
                        if (!box.Style.Width.HasValue && !box.Style.WidthPercent.HasValue)
                            box.Style.WidthPercent = pct;
                    }
                }
                else
                {
                    double px;
                    if (TryParseDouble(width, out px) && px > 0)
                    {
                        if (!box.Style.Width.HasValue && !box.Style.WidthPercent.HasValue)
                            box.Style.Width = px;
                    }
                }
            }

            string height;
            if (attr.TryGetValue("height", out height) && !string.IsNullOrWhiteSpace(height))
            {
                if (height.EndsWith("%"))
                {
                    double pct;
                    if (double.TryParse(height.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pct))
                    {
                        if (!box.Style.Height.HasValue && !box.Style.HeightPercent.HasValue)
                            box.Style.HeightPercent = pct;
                    }
                }
                else
                {
                    double px;
                    if (TryParseDouble(height, out px) && px > 0)
                    {
                        if (!box.Style.Height.HasValue && !box.Style.HeightPercent.HasValue)
                            box.Style.Height = px;
                    }
                }
            }

            string cellpadding;
            if (attr.TryGetValue("cellpadding", out cellpadding) && !string.IsNullOrWhiteSpace(cellpadding))
            {
                double px;
                if (TryParseDouble(cellpadding, out px) && px >= 0)
                {
                    if ((tag == "TD" || tag == "TH") && IsZero(box.Style.Padding))
                        box.Style.Padding = new Windows.UI.Xaml.Thickness(px);
                }
            }

            string align;
            if (attr.TryGetValue("align", out align) && !string.IsNullOrWhiteSpace(align))
            {
                var a = align.Trim().ToLowerInvariant();
                if (box.Style.TextAlign == null)
                {
                    if (a == "center") box.Style.TextAlign = Windows.UI.Xaml.TextAlignment.Center;
                    else if (a == "right") box.Style.TextAlign = Windows.UI.Xaml.TextAlignment.Right;
                    else if (a == "left" || a == "justify") box.Style.TextAlign = Windows.UI.Xaml.TextAlignment.Left;
                }
            }
        }

        private static Windows.UI.Color? TryParseHtmlColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim();
            try { return CssParser.ParseColor(value); } catch { return null;
            }
        }

        private static bool TryParseDouble(string s, out double result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            return double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result);
        }

        private static bool IsZero(Windows.UI.Xaml.Thickness t)
        {
            return t.Left == 0 && t.Top == 0 && t.Right == 0 && t.Bottom == 0;
        }
    }
}
