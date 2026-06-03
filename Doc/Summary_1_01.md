# Summary_01 — Phase 0 + Phase 1 (Infrastructure + Bottom AppBar)

## Phase 0: Infrastructure & Code Cleanup

### Fixes (crashes eliminated)
| Bug | Root cause | Fix |
|-----|-----------|-----|
| Compilation errors | Duplicate `CssParser` in `CssMini.cs`, missing `using System.Collections.Generic`, duplicate `FlexPanel` in `SimpleWrapPanel.cs`, `#if USE_NILJS` blocking NiL.JS imports | Cleaned up duplicates, added missing usings, fixed `#if` guard |
| `NullReferenceException` on duckduckgo.com | `ResourceManager.cs` called `resp.StatusCode` without null-check on `HttpResponseMessage` | Added null-guard before accessing `StatusCode` |
| `OverflowException: Array dimensions exceeded supported range` | `DomBasicRenderer.cs:3385` created `new bool[rowCount, maxCols]` where `maxCols` could overflow | Guard with fallback + cap on `new GridLength[maxCols]` |
| `ExecutionEngineException` in `CssLoader.SplitTokens` | Large string arrays in `SplitTokens` / deep cascade recursion | Added input length limit (65KB), token limit (8192), cascade wrapped in try-catch |
| `BarStrip` not receiving taps | `BarContent` rendered **on top** of `BarStrip` (Z-order) — tap events hit `BarContent` instead of the drag handle | Swapped XAML order: `BarContent` first (behind), `BarStrip` last (on top). `BarContent.IsHitTestVisible` toggles with expand/collapse |
| Bottom bar height animation not working | `Storyboard.DoubleAnimation` on `Grid.Height` competed with UWP layout pass inside Auto-sized Grid row | Replaced animation with direct `BottomBar.Height = 48/20` + manual clip update |

### Dead code removed
- `HttpCache.cs` stub (always returned null)
- `SimpleWrapPanel.cs` (duplicate of `FlexPanel`)
- ~200 lines DISABLED MiniJs blocks in `JavaScriptEngine.cs`
- DISABLED Phase C Module system blocks
- Unused `ExpandBarStoryboard`/`CollapseBarStoryboard` XAML resources

### Refactoring
- `DomBasicRenderer.cs`: split into `TableRenderer.cs` (partial class) — reduced from 5545→4940 lines
- `HtmlTag.cs`: created enum (~100 tags) + `HtmlTagLookup.FromString()`; added `TagId` property to `LiteElement`
- `DispatchTagAsync()`: replaced `_tagHandlers` dictionary with switch-based dispatch

### Retained (NOT deleted)
- `MiniJs.cs` — used by `ModuleLoader`
- `JsRuntimeAbstraction.cs` — used by `BrowserApi.cs`

## Phase 1: Bottom Collapsible AppBar

### Layout (MainPage.xaml)
- `Grid.RowDefinitions`: `*` (content) + `Auto` (bar)
- Bottom bar: `Grid` with `Height="20"` collapsed, internal Z-order:
  1. `BarContent` (buttons panel — renders behind)
  2. `BarStrip` (drag handle — renders on top)
- Styles: `BarButtonStyle` (24×24px, dark theme), `BarOmniboxStyle` (rounded, dark)

### Controls (expanded, 48px)
| Button | Position | Action |
|--------|----------|--------|
| Back | Left | `_browser.GoBack()` |
| Forward | Left | `_browser.GoForward()` |
| Omnibox (URL) | Center | Enter URL/search, keyboard Enter |
| Go | Right | Navigate |
| Settings | Right | Opens overlay with JS/Gpu toggles, Clear Cache, About |

### Behaviors
- **Tap BarStrip** → toggle expand/collapse
- **Swipe up/down** on BarStrip → expand/collapse (TranslateY manipulation)
- **Tap content area** → collapse bar
- **Ctrl+L** → expand + focus URL + select all
- **Ctrl+B** → toggle bar
- **Auto-collapse** on navigation
- **Settings/About overlays** suppress bar collapse (`_suppressBarCollapse`)
- **Clip** on BottomBar prevents content overflow during animation

### Height states
- Collapsed: 20px (only BarStrip visible, BarContent behind it)
- Expanded: 48px (BarStrip top 20px = toggle zone, BarContent 20-48px = buttons)

## Current status
- DuckDuckGo renders without crashing (NiL.JS active, CSS cascade safely degrades)
- Bar expand/collapse uses direct Height assignment (no animation)
- **Build:** 0 errors, 13 warnings (pre-existing)
- **Target hardware:** Lumia 950/1020 (Snapdragon 810, 3GB RAM)
- **Testing:** x86 emulator only (no real ARM device)

## Next up: Phase 2 (CSS Cascade Optimization)
- Selector index (Dictionary by rightmost key) — replace O(nodes×rules) with O(nodes×candidates)
- Compiled regex, StripComments without Regex, manual sort, sibling index cache
