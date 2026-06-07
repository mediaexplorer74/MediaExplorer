# Summary 2.15 — Phase 14: CSS Property Expansion (Batch 1)

**Session date:** 2026-05-18
**Build:** 0.8.0.0 ARM Debug — ✅ 0 errors

---

## 1. New CSS Properties Added

### CssComputed.cs — New fields
| Property | Type | Purpose |
|----------|------|---------|
| `OverflowX` / `OverflowY` | `string` | Per-axis overflow control |
| `LetterSpacing` | `double?` | Character spacing (px) |
| `WordSpacing` | `double?` | Word spacing (px) |
| `LineHeight` | `double?` | Line height (px or unitless multiplier) |
| `TextTransform` | `string` | uppercase/lowercase/capitalize |
| `TextIndent` | `double?` | First-line indent (px) |
| `VerticalAlign` | `string` | Inline element alignment |
| `PointerEvents` | `string` | "auto" / "none" |
| `Cursor` | `string` | Mouse cursor style |
| `TransformOrigin` | `string` | Transform pivot point |
| `BorderStyle` | `string` | solid/dashed/dotted/none |
| `ListStylePosition` | `string` | inside/outside |
| `ListStyleImage` | `string` | Custom bullet URL |

### CssLoader.cs — Parsing
- **z-index**: parsed as `int` from Map
- **overflow-x/y**: parsed with fallback to `overflow` shorthand
- **letter-spacing / word-spacing**: parsed via `TryPx`
- **line-height**: parsed as px or unitless multiplier (× font-size)
- **text-transform**: stored as string
- **text-indent**: parsed via `TryPx`
- **vertical-align**: stored as string
- **pointer-events**: stored as string
- **cursor**: stored as string
- **transform-origin**: stored as string
- **border-style**: extracted from `border` shorthand + longhands + `ExtractBorderStyle()` helper
- **list-style-position / list-style-image**: parsed with URL resolution

### DomBasicRenderer.cs — Rendering
- **letter-spacing**: mapped to UWP `CharacterSpacing` (1/1000 em units)
- **word-spacing**: added to existing `CharacterSpacing`
- **line-height**: `LineStackingStrategy.BlockLineHeight` + `LineHeight`
- **text-transform**: uppercase/lowercase/capitalize via `CapitalizeWords()` helper
- **text-indent**: applied as left padding on TextBlock
- **pointer-events**: `IsHitTestVisible = false` for "none"
- **transform-origin**: parsed via `ParseTransformOrigin()` → `RenderTransformOrigin`
- **border-style**: "none"/"hidden" → skip border rendering in `WrapWithBoxes`

### RendererStyles.cs — Border style
- `border-style: none` / `hidden` → no border rendered even if thickness > 0
- `mapHasBorder` now includes `border-style` key check

---

## 2. NaN Prevention (from previous session)

- `ApplyComputedLayout`: checks `!double.IsNaN()` and `> 0` before assigning Width/Height/Min/Max

## 3. Text Suppression Enhancements (from previous session)

- Added patterns for `desktop`+`common`, `yaru_desktop`, `direct-close`
- Added regex for infrastructure underscore patterns

---

## Files Changed

| File | Change |
|------|--------|
| `Engine/CssComputed.cs` | +13 new fields |
| `Engine/CssLoader.cs` | Parsing for all new properties + `ExtractBorderStyle()` |
| `Engine/DomBasicRenderer.cs` | Rendering in `ApplyComputedStyles()` + `CapitalizeWords()` + `ParseTransformOrigin()` |
| `Engine/RendererStyles.cs` | Border style handling in `WrapWithBoxes` |

---

## Build Result

```
WEBVIEW -> bin\ARM\Debug\WEBVIEW.exe
WEBVIEW -> AppPackages\WEBVIEW_0.8.0.0_Debug_Test\WEBVIEW_0.8.0.0_arm_Debug.appxbundle
```

**0 errors**, 7 pre-existing warnings.

---

## 4. Batch 2 Additions

### vertical-align
- Inline images and elements: offset via Margin (top/middle/bottom/sub/super)
- Applied in `AppendInline` for `InlineUIContainer` with Image and Border children

### overflow-x/y
- `ApplyInlineOverflow` now reads from both inline style AND CssComputed
- `hidden` → RectangleGeometry clip
- `auto`/`scroll` → ScrollViewer with per-axis scrollbar visibility

### cursor
- PointerEntered/Exited handlers set CoreWindow.PointerCursor
- Supports: pointer, text, wait, help, crosshair, move, resize-*, default

### list-style-position/image
- `list-style-image`: renders custom bullet Image instead of text
- `list-style-position: inside`: detected (spacing adjusted)

---

## 5. Batch 3 Additions

### @media improvements
- Added `min-height` / `max-height` support in `FlattenBasicMedia`
- Added `orientation: landscape/portrait` detection
- New regex: `_minHeightRx`, `_maxHeightRx`
- `ExtractPx` now handles all 4 media features (width/height min/max)

### Transitions improvements
- Added `AddDeleteThemeTransition` for opacity/background/color changes
- Added `EntranceThemeTransition` for display/visibility changes
- `RepositionThemeTransition` already existed for transform/position

---

## 6. Batch 4 Additions (Quick Wins from Gap Analysis)

### BackgroundColor fix (dead code → working)
- `css.BackgroundColor` was parsed but never rendered
- `WrapWithBoxes` now checks `css.BackgroundColor.HasValue` and creates `SolidColorBrush`
- Same fix for `BorderBrushColor` → `BorderBrush` conversion

### TextOverflow in ApplyInlineStyles
- Added `text-overflow: ellipsis` handling in inline style parser
- Sets `TextTrimming.CharacterEllipsis` + `MaxLines = 1`

### Float/Clear improvements
- Float already existed but Clear was missing
- Added `clear: left/right/both` → adds 20px top margin after floated elements
- Tracks `previousWasFloat` state for proper clear behavior

### object-fit pipeline
- Added `ObjectFit` field to CssComputed
- Parsed in CssLoader: fill/contain/cover/none/scale-down
- Rendered in MakeImageAsync: maps to UWP `Stretch` enum
  - `fill` → `Stretch.Fill`
  - `contain` → `Stretch.Uniform`
  - `cover` → `Stretch.UniformToFill`
  - `none` → `Stretch.None`

---

## Next (Phase 14 Batch 5)

1. **transition/animation** — basic CSS transitions
2. **grid-template-areas** — CSS Grid layout support
3. **box-shadow** — improved rendering with multiple layers
4. **@media queries** — responsive design support
5. **:hover/:active pseudo-classes** — interactive states

---

*Session 2.15 — 2026-05-18*
