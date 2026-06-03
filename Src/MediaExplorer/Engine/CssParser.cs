using System;
using System.Globalization;
using System.Reflection;
using System.Collections.Generic;

namespace BrowserCore.Engine
{
    /// <summary>
    /// Lean CSS utility providing only media environment hints and color parsing that
    /// the rest of the engine (CustomHtmlEngine, DomBasicRenderer, CssLoader) currently uses.
    /// Original CssMini.cs became irreparably corrupted (thousands of syntax errors & duplicate blocks)
    /// and has been removed for build stability. Extend cautiously as needed.
    /// </summary>
    public static class CssParser
    {
        // Media query environment hints (set externally before parsing/cascade)
        public static double? MediaViewportWidth { get; set; }
        public static double? MediaViewportHeight { get; set; }
        public static double? MediaDppx { get; set; }
        public static string MediaPrefersColorScheme { get; set; }
        public static string MediaScripting { get; set; } // "enabled", "none", "initial-only"

        private static readonly Dictionary<string, Windows.UI.Color> _namedColors 
            = new Dictionary<string, Windows.UI.Color>(StringComparer.OrdinalIgnoreCase);

        static CssParser()
        {
            // Pre-populate common colors to ensure they work even if reflection fails
            _namedColors["black"] = Windows.UI.Colors.Black;
            _namedColors["white"] = Windows.UI.Colors.White;
            _namedColors["gray"] = Windows.UI.Colors.Gray;
            _namedColors["grey"] = Windows.UI.Colors.Gray;
            _namedColors["red"] = Windows.UI.Colors.Red;
            _namedColors["green"] = Windows.UI.Colors.Green;
            _namedColors["blue"] = Windows.UI.Colors.Blue;
            _namedColors["yellow"] = Windows.UI.Colors.Yellow;
            _namedColors["cyan"] = Windows.UI.Colors.Cyan;
            _namedColors["magenta"] = Windows.UI.Colors.Magenta;
            _namedColors["transparent"] = Windows.UI.Colors.Transparent;
            _namedColors["lightcoral"] = Windows.UI.Colors.LightCoral;
            _namedColors["purple"] = Windows.UI.Colors.Purple;
            _namedColors["orange"] = Windows.UI.Colors.Orange;
            _namedColors["gold"] = Windows.UI.Colors.Gold;
            _namedColors["brown"] = Windows.UI.Colors.Brown;
            _namedColors["pink"] = Windows.UI.Colors.Pink;
            _namedColors["teal"] = Windows.UI.Colors.Teal;
            _namedColors["lime"] = Windows.UI.Colors.Lime;
            _namedColors["maroon"] = Windows.UI.Colors.Maroon;
            _namedColors["navy"] = Windows.UI.Colors.Navy;
            _namedColors["olive"] = Windows.UI.Colors.Olive;
            _namedColors["silver"] = Windows.UI.Colors.Silver;

            try
            {
                var props = typeof(Windows.UI.Colors).GetRuntimeProperties();
                foreach (var p in props)
                {
                    if (p.PropertyType == typeof(Windows.UI.Color))
                    {
                        try 
                        { 
                            var c = (Windows.UI.Color)p.GetValue(null);
                            _namedColors[p.Name] = c;
                        } 
                        catch { System.Diagnostics.Debug.WriteLine(" [Engine/CssParser.cs] empty catch empty catch"); }
                    }
                }
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/CssParser.cs] empty catch empty catch"); }
        }

        /// <summary>
        /// Parse a CSS color value (#hex, rgb/rgba(), or a small set of named colors).
        /// Returns null if unsupported. Alpha defaults to 255 when omitted.
        /// </summary>
        public static Windows.UI.Color? ParseColor(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();

            // transparent keyword
            if (string.Equals(s, "transparent", StringComparison.OrdinalIgnoreCase))
                return Windows.UI.Color.FromArgb(0, 0, 0, 0);

            var named = GetNamedColor(s);
            if (named.HasValue) return named.Value;

            // Hex forms: #rgb #rgba #rrggbb #rrggbbaa
            if (s[0] == '#')
            {
                var hex = s.Substring(1);
                if (hex.Length == 3) hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
                if (hex.Length == 4) hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2], hex[3], hex[3]);
                if (hex.Length == 6) hex += "ff"; // append opaque alpha
                if (hex.Length == 8)
                {
                    try
                    {
                        byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                        byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                        byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                        byte a = byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber);
                        return Windows.UI.Color.FromArgb(a, r, g, b);
                    }
                    catch { return null; }
                }
                return null;
            }

            // rgb()/rgba() functional notation
            if (s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
            {
                int open = s.IndexOf('('); int close = s.LastIndexOf(')');
                if (open > 0 && close > open)
                {
                    var inner = s.Substring(open + 1, close - open - 1);
                    var parts = inner.Split(',');
                    if (parts.Length >= 3)
                    {
                        int r = ParseComponent(parts[0]);
                        int g = ParseComponent(parts[1]);
                        int b = ParseComponent(parts[2]);
                        byte a = 255;
                        if (parts.Length >= 4)
                        {
                            var aRaw = parts[3].Trim();
                            if (aRaw.EndsWith("%"))
                            {
                                double pct;
                                if (double.TryParse(aRaw.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out pct))
                                {
                                    pct = Math.Max(0, Math.Min(100, pct));
                                    a = (byte)(pct / 100.0 * 255);
                                }
                            }
                            else
                            {
                                double alpha;
                                if (double.TryParse(aRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out alpha))
                                {
                                    alpha = Math.Max(0, Math.Min(1, alpha));
                                    a = (byte)(alpha * 255);
                                }
                            }
                        }
                        return Windows.UI.Color.FromArgb(a, (byte)r, (byte)g, (byte)b);
                    }
                }
                return null;
            }

            return null;
        }

        private static int ParseComponent(string raw)
        {
            raw = (raw ?? "").Trim();
            if (raw.EndsWith("%"))
            {
                double pct;
                if (double.TryParse(raw.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out pct))
                {
                    pct = Math.Max(0, Math.Min(100, pct));
                    return (int)(pct / 100.0 * 255);
                }
                return 0;
            }
            int v;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
            {
                if (v < 0) v = 0; else if (v > 255) v = 255;
                return v;
            }
            double dv;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out dv))
            {
                dv = Math.Max(0, Math.Min(255, dv));
                return (int)Math.Round(dv);
            }
            return 0;
        }

        private static Windows.UI.Color? GetNamedColor(string name)
        {
            Windows.UI.Color c;
            if (_namedColors.TryGetValue(name, out c)) return c;
            return null;
        }
    }
}