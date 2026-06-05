using System.Collections.Generic;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace BrowserCore.Engine
{
    /// <summary>
    /// Computed CSS values for a DOM element. Contains a raw property map plus common typed projections
    /// used by DomBasicRenderer and RendererStyles.
    /// </summary>
    public sealed class CssComputed
    {
        public Dictionary<string, string> Map { get; private set; }
        public Dictionary<string, string> CustomProperties { get; private set; }

        public CssComputed()
        {
            Map = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            CustomProperties = new Dictionary<string, string>(System.StringComparer.Ordinal);
        }

        // Display & positioning
        public string Display { get; set; }
        public string Position { get; set; }
        public int? ZIndex { get; set; }
        public double? Left { get; set; }
        public double? Top { get; set; }
        public double? Right { get; set; }
        public double? Bottom { get; set; }
        public string Overflow { get; set; }
        public string OverflowX { get; set; }
        public string OverflowY { get; set; }
        public string Visibility { get; set; } // "visible" (default), "hidden", "collapse"
        public string Float { get; set; } // "none", "left", "right"
        public string Clear { get; set; } // "none", "left", "right", "both"

        // Box model
        public string BoxSizing { get; set; } // "content-box" (default) or "border-box"
        public Thickness Margin { get; set; }
        public Thickness Padding { get; set; }
        public Thickness BorderThickness { get; set; }
        public Brush BorderBrush { get; set; }
        public CornerRadius BorderRadius { get; set; }

        // Sizing
        public double? Width { get; set; }
        public double? Height { get; set; }
        public double? MinWidth { get; set; }
        public double? MinHeight { get; set; }
        public double? MaxWidth { get; set; }
        public double? MaxHeight { get; set; }
        public double? WidthPercent { get; set; }
        public double? HeightPercent { get; set; }
        public double? AspectRatio { get; set; }  // width/height ratio

        // Flexbox / Grid gaps
        public double? ColumnGap { get; set; }
        public double? RowGap { get; set; }
        public double? Gap { get; set; }

        // Flexbox
        public string FlexDirection { get; set; }
        public string FlexWrap { get; set; }
        public string JustifyContent { get; set; }
        public string AlignItems { get; set; }
        public string AlignContent { get; set; }
        public double? FlexGrow { get; set; }
        public double? FlexShrink { get; set; }
        public double? FlexBasis { get; set; }

        // Typography & Visuals
        public Brush Background { get; set; }
        public Windows.UI.Color? BackgroundColor { get; set; }
        public Brush Foreground { get; set; }
        public Windows.UI.Color? ForegroundColor { get; set; }
        public double? FontSize { get; set; }
        public FontWeight? FontWeight { get; set; }
        public FontStyle? FontStyle { get; set; }
        
        public string FontFamilyName { get; set; }
        private FontFamily _fontFamily;
        public FontFamily FontFamily 
        { 
            get
            {
                if (_fontFamily == null && !string.IsNullOrEmpty(FontFamilyName))
                {
                    try { _fontFamily = new FontFamily(FontFamilyName); } catch { /* swallow */ }
                }
                return _fontFamily;
            }
            set { _fontFamily = value; }
        }

        public TextAlignment? TextAlign { get; set; }
        public string Hyphens { get; set; }
        public string TextDecoration { get; set; }
        public string WhiteSpace { get; set; } // "normal", "nowrap", "pre", "pre-wrap", "pre-line"
        public string TextOverflow { get; set; } // "clip", "ellipsis"
        public string ListStyleType { get; set; }
        public string ListStylePosition { get; set; } // "outside" (default), "inside"
        public string ListStyleImage { get; set; } // resolved URL for custom bullet

        // Typography spacing
        public double? LetterSpacing { get; set; } // px
        public double? WordSpacing { get; set; } // px
        public double? LineHeight { get; set; } // px (or "normal")
        public string TextTransform { get; set; } // "none", "uppercase", "lowercase", "capitalize"
        public double? TextIndent { get; set; } // px

        // Inline alignment
        public string VerticalAlign { get; set; } // "baseline", "top", "middle", "bottom", "sub", "super", or px/% value

        // Interaction
        public string PointerEvents { get; set; } // "auto", "none"
        public string Cursor { get; set; } // "auto", "pointer", "default", etc.
        
        // Extra color for border
        public Windows.UI.Color? BorderBrushColor { get; set; }

        // Table
        public double? BorderSpacing { get; set; }        // horizontal (or single-value)
        public double? BorderSpacingVertical { get; set; } // vertical (2-value syntax)

        // Outline (store Color, not Brush — SolidColorBrush must be created on UI thread)
        public double? OutlineWidth { get; set; }
        public string OutlineStyle { get; set; }
        public Windows.UI.Color? OutlineColor { get; set; }

        // Visual effects
        public string Transform { get; set; }
        public string TransformOrigin { get; set; } // e.g. "center", "50% 50%", "0 0"
        public double? Opacity { get; set; }        // 0.0 to 1.0
        public string TextShadow { get; set; }      // CSS text-shadow value  
        public string BoxShadow { get; set; }       // CSS box-shadow value

        // Border style (for shorthand parsing)
        public string BorderStyle { get; set; } // "solid", "dashed", "dotted", etc.

        // Background image
        public string BackgroundImageUrl { get; set; }  // resolved absolute URL
        public string BackgroundRepeat { get; set; }    // repeat, no-repeat, repeat-x, repeat-y
        public string BackgroundPosition { get; set; }  // e.g. "center", "50% 50%", "left top"
        public string BackgroundSize { get; set; }      // cover, contain, or "Wpx Hpx"

        // Image fitting
        public string ObjectFit { get; set; }           // "fill", "contain", "cover", "none", "scale-down"

        // CSS Grid
        public string GridTemplateColumns { get; set; }
        public string GridTemplateRows { get; set; }
        public string GridTemplateAreas { get; set; }
        public string GridAutoColumns { get; set; }
        public string GridAutoRows { get; set; }
        public string GridAutoFlow { get; set; }
        public string GridColumn { get; set; }
        public string GridRow { get; set; }
        public string GridArea { get; set; }

        // CSS Transitions (Phase C.6, Session 3.26)
        // Raw shorthand value as authored in CSS (e.g. "opacity 0.3s ease")
        public string Transition { get; set; }
        // Parsed duration in milliseconds. 0 means "no animation" (instant).
        public double TransitionDurationMs { get; set; }
        // Property name to animate. One of:
        //   "opacity" | "background-color" | "transform" | "all"
        // Empty means the shorthand couldn't be parsed.
        public string TransitionProperty { get; set; }
        // Easing function. One of: "ease" | "linear" | "ease-in" |
        //   "ease-out" | "ease-in-out". Defaults to "ease".
        public string TransitionTimingFunction { get; set; }
        // Optional delay before the animation starts. 0 = immediate.
        public double TransitionDelayMs { get; set; }
        // :hover override. Null if no :hover rules apply to this element.
        // When non-null, the renderer swaps in these computed values on
        // PointerEntered and reverts to base on PointerExited, animating
        // any property listed in TransitionProperty via TransitionAnimator.
        public CssComputed Hover { get; set; }
    }
}
