# Summary 3.21 — Phase 16.6: CSS Grid + Flexbox Routing + grid-template-areas

**Session date:** 2026-06-03  
**Build:** `msbuild MediaExplorer.sln` — ✅ **0 errors** (C# compilation passes)  
**Test result:** ya.ru — 132 nodes, 189 boxes, 23 text, 1065px canvas (renders). dzen.ru — white screen (JS-dependent SPA, NiL.JS `InvalidOperationException`).

---

## 1. Changes Applied

### Fix 1: CSS Grid Layout (`RenderCssGridAsync`)

**Problem:** `display: grid` was parsed by the CSS cascade but ignored by the renderer. Grid containers fell through to `RenderGenericContainerAsync` → plain `StackPanel`, losing all grid structure.

**Solution:** Created `RenderCssGridAsync` — maps CSS Grid properties to UWP `Grid` panel with proper column/row definitions and child placement.

**Files changed:**

| File | Change |
|------|--------|
| `Engine/CssComputed.cs` | Added 9 grid properties: `GridTemplateColumns`, `GridTemplateRows`, `GridTemplateAreas`, `GridAutoColumns`, `GridAutoRows`, `GridAutoFlow`, `GridColumn`, `GridRow`, `GridArea` |
| `Engine/CssLoader.cs` | Cascade parsing for all 9 grid properties (~10 lines) |
| `Engine/DomBasicRenderer.cs` | ~400 lines: `RenderCssGridAsync`, track parsers, grid placement, auto-placement, named areas, flex routing fix |

**Features implemented:**
- `grid-template-columns` / `grid-template-rows` — full track list parser:
  - `repeat(N, ...)` with inner track expansion
  - `minmax(min, max)` with MinWidth/MinHeight on definitions
  - `fr` units → `GridUnitType.Star`
  - `px`, percentage (→ star), `auto`/`min-content`/`max-content`
- `grid-template-areas` — parses quoted string syntax, builds contiguous bounding box for each named area
- `grid-column` / `grid-row` — explicit line numbers, `span N`, `M / N`, `M / span N`
- `grid-area` — named area resolution from `grid-template-areas`
- Auto-placement — row-fill (default) and `grid-auto-flow: column` modes
- Gap — `column-gap`/`row-gap` mapped to `ColumnSpacing`/`RowSpacing`
- Absolute children — collected into Canvas overlay
- Grid size inferred from areas if no explicit template

### Fix 2: Flexbox Container Routing

**Problem:** `IsFlexContainer()` was defined but never called. `display: flex` was ignored by the renderer.

**Fix:** Added flex container check in `DispatchTagAsync` before the tag-based switch — routes to `MakeGridFallbackAsync` (FlexPanel).

### Fix 3: `CollectAbsoluteChildren` — static call fix

**Problem:** `CollectAbsoluteChildren` was `static` but called instance method `TryGetCss`.  
**Fix:** Changed to `TryGetCssStatic`.

---

## 2. Architecture

```
DispatchTagAsync
  ├── display: grid / inline-grid  →  RenderCssGridAsync  →  UWP Grid
  │     ├── ParseGridTrackList         (templates → ColumnDefinitions)
  │     ├── ParseGridRowTrackList      (templates → RowDefinitions)
  │     ├── ParseGridTemplateAreas     (named areas → GridAreaInfo map)
  │     ├── ParseGridPlacement         (grid-column/row → line/span)
  │     └── Auto-placement             (row-fill / column-fill)
  │
  ├── display: flex / inline-flex →  MakeGridFallbackAsync  →  FlexPanel
  │
  └── (default)                  →  RenderBlockAsync       →  StackPanel
```

---

## 3. Test Observations (from user-run on ya.ru + dzen.ru)

| Site | Nodes | Boxes | Text | Canvas | Status |
|------|-------|-------|------|--------|--------|
| ya.ru | 132 | 189 | 23 | 1065px | Рендер есть, но JS падает с InvalidOperationException (caught) |
| dzen.ru | 1 | 2 | 0 | 16px | **Белый экран** — SPA, 3KB HTML-шелл, JS не выполнился |

**Key findings:**
- ya.ru показывает, что движок может рендерить сложные страницы (132 узла, images грузятся)
- dzen.ru — белый экран из-за JS-зависимости. NiL.JS не может выполнить JS-код SPA
- Multiple `InvalidOperationException` из NiL.JS — все caught, но JS-функциональность severely limited
- `FileNotFoundException` (caught) при загрузке ресурсов — штатное поведение
- DNS resolution failure на `mc.yandex.ru` — сеть/SNA, не движок

---

## 4. Next Steps

1. **Rebuild and test `iana.org`** — validate CSS Grid improvements on a known-working site
2. **Improve NiL.JS JS execution** — reduce `InvalidOperationException` count for better SPA support
3. **getComputedStyle** — return real computed CSS values instead of `{}`
4. **`@media` query improvements** — density, prefers-color-scheme, scripting
5. **Grid edge cases** — negative line numbers, `justify-items`/`align-items`, dense packing

---

*Summary v3.21 — 2026-06-03*
