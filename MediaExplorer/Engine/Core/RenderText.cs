using System;
using Windows.Foundation;

namespace BrowserCore.Engine.Core
{
    public class RenderText : RenderObject
    {
        public string Text { get; set; }

        public override void Layout(Size availableSize)
        {
            if (string.IsNullOrEmpty(Text))
            {
                Bounds = Rect.Empty;
                return;
            }

            var style = Style ?? Parent?.Style;
            double fontSize = style?.FontSize ?? 16.0;
            bool isBold = false;
            try { isBold = style?.FontWeight != null && style.FontWeight.Value.Weight >= 700; } catch { }

            double charAdvance = fontSize * (isBold ? 0.62 : 0.55);
            double lineHeight = fontSize * 1.3;

            double maxWidth = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width;
            double totalWidth = 0;
            double totalHeight = lineHeight;

            if (double.IsInfinity(maxWidth))
            {
                totalWidth = Text.Length * charAdvance;
            }
            else
            {
                string[] words = (Text ?? "").Split(' ');
                double currentX = 0;
                double maxLineWidth = 0;
                int lineCount = 1;

                for (int i = 0; i < words.Length; i++)
                {
                    double wordWidth = words[i].Length * charAdvance;
                    if (i > 0) wordWidth += charAdvance;

                    if (currentX + wordWidth > maxWidth && currentX > 0)
                    {
                        lineCount++;
                        currentX = wordWidth - (i > 0 ? charAdvance : 0);
                    }
                    else
                    {
                        currentX += wordWidth;
                    }
                    if (currentX > maxLineWidth) maxLineWidth = currentX;
                }

                totalWidth = maxLineWidth;
                totalHeight = lineHeight * lineCount;
            }

            if (totalWidth < 1) totalWidth = 1;
            if (totalHeight < 1) totalHeight = lineHeight;

            Bounds = new Rect(0, 0, totalWidth, totalHeight);
        }
    }
}
