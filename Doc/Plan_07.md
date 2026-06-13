# Plan_07 — Road to v2.0: Mature Browser Engine

**Date**: June 13, 2026
**Status**: Planning

---

## Executive Summary

MediaExplorer v1.0 successfully renders Nokia Design Archive cards on Lumia 640/950. However, the custom browser engine (NiL.JS + DomBasicRenderer + CssParser) has significant gaps that prevent rendering most real-world websites from the 2000s–2020s era. This plan defines a phased roadmap to transform MediaExplorer from a "card viewer" into a usable retro-browser.

**Current Engine Audit** (June 13, 2026):

| Component | Version / Target | Status |
|-----------|-----------------|--------|
| NiL.JS | v2.6 / netstandard1.4 | ES2015 parser; limited by netstandard1.4 APIs |
| CSS Parser | Custom (CssParser.cs) | Selectors work (tag/id/class/attr/nth-child/combinators); no `calc()`, transitions basic |
| CSS Layout | DomBasicRenderer.cs | Flexbox detection + basic alignment; Grid detection + basic template; no flex-grow/shrink |
| JS APIs | Pattern-matched in JavaScriptEngine.cs | fetch, XHR, Promise, rAF, document, localStorage, cookies |
| HTML | DomBasicRenderer.cs | ~40 elements; tables basic; forms minimal |
| Rendering | XAML StackPanel/Grid | No canvas, no SVG, no animations |

**⚠️ Critical: TWO Rendering Pipelines** (discovered June 13, 2026):

| Pipeline | Entry Point | Used By | Fix Location |
|----------|-------------|---------|--------------|
| Legacy | `DomBasicRenderer.DispatchTagAsync()` → direct XAML | Nokia card mode | DomBasicRenderer.cs |
| **Active** | `RenderTreeBuilder.Build()` → `LayoutEngine` → `VirtualizingRenderer` → Canvas | HN, general web | RenderTreeBuilder.cs + RenderBox.cs |

**All fixes for general web rendering must go in `RenderTreeBuilder` and `RenderBox`, NOT in `DomBasicRenderer`.**

**NiL.JS Constraints** (netstandard1.4):
- No `System.Text.Json` → manual JSON parsing
- Limited reflection emit → no dynamic proxy generation
- No `Span<T>` / `Memory<T>` → string-heavy code paths
- Community fork frozen at v2.6; upstream abandoned

---

## Architecture: Three Parallel Tracks

```
Track A: CSS Engine        ████████████████████████████████→ v2.0
Track B: JS Engine + APIs  ████████████████████████████████→ v2.0
Track C: Canvas (SkiaSharp)           ████████████████████→ v2.0
           ↑ starts at Phase 6
```

---

## Phase 1: CSS Foundation (Sessions 7–9)

**Goal**: Make 60% of 2010-era sites render recognizably.

### 1.1 CSS Calc() Support
- **What**: Parse and evaluate `calc()`, `min()`, `max()` in CSS values
- **Files**: `CssParser.cs`, `CssValueResolver.cs` (new)
- **Approach**: Recursive descent parser for math expressions; resolve at layout time using viewport/parent dimensions
- **Priority**: P0 — many sites use `calc(100% - 20px)` in headers/footers

### 1.2 Flex-grow / Flex-shrink / Flex-basis
- **What**: Complete the flexbox algorithm in `RenderBox.cs`
- **Files**: `RenderBox.cs` (line 306: `// TODO: Implement FlexGrow/Shrink`)
- **Approach**: Two-pass layout — first measure natural sizes, then distribute free/overflow space proportionally
- **Priority**: P0 — flexbox is ubiquitous since 2013

### 1.3 CSS Variables (Custom Properties)
- **What**: Support `--my-var: value` and `var(--my-var)` in stylesheets
- **Files**: `CssParser.cs`, `CssPropertyMap.cs` (new)
- **Approach**: Store custom properties per-element; resolve `var()` references during cascade
- **Priority**: P1 — Bootstrap 4+, many 2018+ sites

### 1.4 CSS Transitions (Basic)
- **What**: Animate `opacity`, `transform`, `background-color` on state changes
- **Files**: `DomBasicRenderer.cs`, new `CssAnimator.cs`
- **Approach**: Composition API `ImplicitAnimation` or timer-based interpolation
- **Priority**: P2 — visual polish

### 1.5 Media Queries (Viewport-Based)
- **What**: Evaluate `@media (min-width: N)`, `(max-width: N)`, `(orientation:)`
- **Files**: `CssParser.cs` (has `matchMedia` stub), `JavaScriptEngine.cs` (line 7104)
- **Priority**: P0 — responsive layouts depend on this

### Testing Sites for Phase 1
| Site | Complexity | Why |
|------|-----------|-----|
| example.com | Trivial | Baseline |
| Wikipedia (mobile) | Simple HTML | Tests tables, basic CSS |
| Hacker News | Simple CSS | Tests `calc()`, basic flex |
| MDN Web Docs (2015 archived) | Medium | Tests flexbox-heavy layout |
| Bootstrap 4 docs (archived) | Medium | Tests CSS variables, grid |

---

## Phase 2: CSS Layout Engine (Sessions 10–12)

**Goal**: Reliable flexbox + CSS grid for real sites.

### 2.1 CSS Grid Level 1
- **What**: Full `grid-template-columns/rows`, `grid-gap`, `grid-area` placement, `fr` units, `repeat()`, `auto-fit/auto-fill`
- **Files**: `DomBasicRenderer.cs` (lines 1621–1765 have basic grid), `RenderBox.cs`
- **Approach**: Implement grid line placement algorithm; use XAML Grid under the hood with dynamic column/row defs
- **Priority**: P0 — many 2015+ sites use grid

### 2.2 Position: Sticky
- **What**: `position: sticky` for headers/navbars
- **Files**: `DomBasicRenderer.cs`, `CustomHtmlEngine.cs`
- **Approach**: Track scroll offset; toggle between relative/fixed positioning
- **Priority**: P1

### 2.3 Overflow & Scroll Containers
- **What**: `overflow: auto/scroll` on individual elements, nested scroll
- **Files**: `DomBasicRenderer.cs`
- **Approach**: Wrap overflowing content in ScrollViewer; handle nested scroll correctly
- **Priority**: P0 — many layouts depend on this

### 2.4 CSS Shorthand Expansion
- **What**: Expand `margin: 10px 20px`, `border: 1px solid red`, `background: url(...) no-repeat center`
- **Files**: `CssParser.cs`
- **Priority**: P0 — shorthand is the norm, not the exception

### Testing Sites for Phase 2
| Site | Complexity | Why |
|------|-----------|-----|
| CSS-Tricks (archived) | Medium | Flexbox guides with examples |
| GitHub (archived 2016) | Complex | Grid + sticky headers |
| StackOverflow (archived) | Complex | Nested layouts, overflow |

---

## Phase 3: JavaScript Engine Improvements (Sessions 13–15)

**Goal**: Run 2010–2015 era JavaScript frameworks.

### 3.1 ES6+ Polyfills in NiL.JS
- **What**: Ensure `Symbol`, `Map/Set`, `WeakMap`, `Array.from`, `Object.assign`, `String.includes/startsWith/endsWith`, template literals, destructuring, spread operator, default params, `for...of` all work
- **Files**: `JavaScriptEngine.cs` (bootstrap phase), NiL.JS source
- **Approach**: Audit NiL.JS v2.6 ES6 support; add polyfills via bootstrap script for gaps
- **Priority**: P0 — most frameworks require these

### 3.2 `document.createElement` / DOM Manipulation
- **What**: Full `createElement`, `appendChild`, `removeChild`, `insertBefore`, `querySelector`, `querySelectorAll`, `getElementById`, `getElementsByClassName`, `getElementsByTagName`
- **Files**: `JavaScriptEngine.cs` (line 1063+ has `document` patterns)
- **Priority**: P0 — any framework does DOM manipulation

### 3.3 `element.style` / `getComputedStyle`
- **What**: `element.style.cssText`, `element.style.setProperty()`, `window.getComputedStyle(el)`
- **Files**: `JavaScriptEngine.cs` (line 2466 has partial style map), `CssParser.cs`
- **Priority**: P0 — jQuery/Vanilla JS all do this

### 3.4 `setTimeout` / `setInterval` (Robust)
- **What**: Ensure timer accuracy, proper cleanup on navigation, cancellation
- **Files**: `JavaScriptEngine.cs` (has timer code ~line 536)
- **Priority**: P0

### 3.5 Event System Completion
- **What**: `addEventListener` with options `{ once, capture, passive }`, `removeEventListener`, `event.preventDefault()`, `event.stopPropagation()`, `CustomEvent`, `dispatchEvent`
- **Files**: `JavaScriptEngine.cs` (line 365+ has event patterns)
- **Priority**: P0

### 3.6 `fetch` API (Full)
- **What**: `fetch(url, { method, headers, body, mode, credentials })`, `Response` object, `Headers` object, streaming
- **Files**: `JavaScriptEngine.cs` (line 1035 has fetch patterns)
- **Priority**: P0

### Testing Sites for Phase 3
| Site | Framework | Why |
|------|----------|-----|
| TodoMVC (backbone, angular 1) | Backbone.js / Angular 1.x | DOM manipulation, events |
| jQuery.com (archived) | jQuery | The most common library of 2010s |
| React tutorial (archived) | React 15/16 | Virtual DOM, createElement |

---

## Phase 4: HTML & Media (Sessions 16–17)

**Goal**: Handle tables, forms, audio/video placeholders, responsive images.

### 4.1 Table Layout Engine
- **What**: `border-collapse`, `colspan`/`rowspan`, `<thead>/<tbody>/<tfoot>`, proper cell sizing
- **Files**: `DomBasicRenderer.cs`
- **Priority**: P1 — Wikipedia, documentation sites

### 4.2 Form Elements
- **What**: `<select>` dropdown, `<input type="checkbox/radio">`, `<textarea>`, form validation, `<label>` association
- **Files**: `DomBasicRenderer.cs`
- **Priority**: P1 — search forms are essential

### 4.3 Responsive Images
- **What**: `srcset`, `sizes` attribute, `<picture>` element, `loading="lazy"`
- **Files**: `DomBasicRenderer.cs`, `ResourceManager.cs`
- **Priority**: P1

### 4.4 `<iframe>` (Limited)
- **What**: Render iframe content inline (not sandboxed) for embeds
- **Files**: `DomBasicRenderer.cs`
- **Priority**: P2

---

## Phase 5: Site Compatibility Hardening (Sessions 18–20)

**Goal**: Make the top 20 "retro-friendly" sites render.

### 5.1 jQuery Compatibility Layer
- **What**: Test jQuery 1.x/2.x/3.x; fix `$.ajax`, `$.ready`, CSS manipulation gaps
- **Priority**: P0 — jQuery powers ~70% of 2010s sites

### 5.2 Bootstrap 3/4 Compatibility
- **What**: Ensure Bootstrap's grid, navbar, modal, dropdown all render
- **Priority**: P1 — most popular CSS framework of the era

### 5.3 Common Pattern Fixes
- **What**: Fix patterns that appear across many sites:
  - `position: fixed` header + scrollable body
  - Sticky footer (flexbox-based)
  - Image lightboxes (CSS-only)
  - Accordion/collapse (CSS-only or minimal JS)
- **Priority**: P0

### 5.4 Site-Specific Compatibility Modes
- **What**: Per-site tweaks (like browser quirks modes) for sites that need special handling
- **Files**: New `CompatibilityModes.cs`
- **Sites**: Wikipedia, GitHub, Hacker News, Reddit (old), StackOverflow
- **Priority**: P1

### Testing Matrix for Phase 5
| Site | Year | Key Challenge |
|------|------|---------------|
| Wikipedia (mobile) | 2015 | Tables, TOC, collapsibles |
| GitHub (archived) | 2016 | Flexbox, SVG icons, sticky header |
| Hacker News | 2007 | Minimal CSS, simple HTML |
| Reddit (old.reddit) | 2017 | Nested comments, vote arrows |
| StackOverflow | 2012 | Tabs, code blocks, voting |
| CSS-Tricks | 2015 | Articles with embedded demos |
| MDN | 2016 | Sidebar nav, code blocks |
| Archive.org | 2010 | Frames, Wayback Machine toolbar |
| Google (cached) | 2015 | Simple search results |
| DuckDuckGo | 2014 | Clean HTML, minimal JS |

---

## Phase 6: SkiaSharp Canvas Rendering (Sessions 21–24)

**Goal**: Render timelines, network graphs, and SVG-like visualizations.

### 6.1 SkiaSharp Integration
- **What**: Add `SkiaSharp.Views.UWP` NuGet package; create `SKXamlCanvas` host
- **Files**: New `Views/SkiaHost.cs`
- **Target**: netstandard2.0 (may need to update NiL.JS target or use separate assembly)
- **Risk**: High — netstandard1.4 vs 2.0 compatibility; UWP ARM NuGet availability
- **Mitigation**: Test with minimal UWP sample first; fallback to manual DLL reference

### 6.2 Canvas2D API Bridge
- **What**: Implement `canvas.getContext('2d')` with SkiaSharp backend:
  - `fillRect`, `strokeRect`, `clearRect`
  - `beginPath`, `moveTo`, `lineTo`, `stroke`, `fill`
  - `arc`, `arcTo`, `bezierCurveTo`, `quadraticCurveTo`
  - `drawImage` (from loaded BitmapImage)
  - `fillText`, `strokeText`
  - `fillStyle`, `strokeStyle`, `lineWidth`, `globalAlpha`
- **Files**: New `Engine/Canvas2DRenderer.cs`, JavaScript API bridge
- **Priority**: P0 for Nokia Design Archive timeline

### 6.3 Nokia Design Archive Timeline
- **What**: Render the timeline visualization from the archive's JS chunks
- **Files**: `Canvas2DRenderer.cs` + site-specific adapter
- **Priority**: P0 — user specifically requested this

### 6.4 SVG Rendering (Basic)
- **What**: Parse SVG elements (`<circle>`, `<rect>`, `<line>`, `<path>`, `<text>`) and render via SkiaSharp
- **Files**: New `Engine/SvgRenderer.cs`
- **Priority**: P1 — many sites use inline SVG for icons/charts

### 6.5 Network Graph Visualization
- **What**: Render force-directed graph layout using SkiaSharp
- **Files**: New `Views/GraphCanvas.cs`
- **Priority**: P2 — Nokia Design Archive network view

---

## Phase 7: Performance & Polish (Sessions 25–27)

**Goal**: Smooth on Lumia 640 (1GB RAM).

### 7.1 Memory Profiling
- **What**: Use `MemoryManager.AppMemoryUsage` to track; identify leaks
- **Priority**: P0

### 7.2 Lazy Rendering
- **What**: Only render visible viewport + buffer; virtualize long pages
- **Priority**: P1

### 7.3 String Allocation Reduction
- **What**: Use `StringBuilder` pooling, `ReadOnlySpan` where available
- **Priority**: P1

### 7.4 GC Pause Minimization
- **What**: Reduce large object heap allocations; pool XAML elements
- **Priority**: P2

---

## Phase 8: User-Facing Features (Sessions 28–30)

### 8.1 Bookmarks
- **What**: Save/load bookmarks with folders
- **Priority**: P0

### 8.2 Download Manager
- **What**: Download files to phone storage
- **Priority**: P1

### 8.3 Tabbed Browsing (Limited)
- **What**: 2–3 tabs with memory-aware switching (unload inactive)
- **Priority**: P2

### 8.4 Find on Page
- **What**: Ctrl+F equivalent, highlight matches
- **Priority**: P1

---

## Implementation Order (Sessions 7→30)

```
Session 7-9:   Phase 1 — CSS Foundation (calc, flex-grow, variables, media queries)
Session 10-12: Phase 2 — CSS Layout (grid, sticky, overflow, shorthand)
Session 13-15: Phase 3 — JS Engine (ES6 polyfills, DOM API, fetch full, events)
Session 16-17: Phase 4 — HTML (tables, forms, responsive images)
Session 18-20: Phase 5 — Site Compatibility (jQuery, Bootstrap, test matrix)
Session 21-24: Phase 6 — SkiaSharp Canvas (Canvas2D API, timeline, SVG)
Session 25-27: Phase 7 — Performance (memory, lazy render, GC tuning)
Session 28-30: Phase 8 — User Features (bookmarks, downloads, tabs, find)
```

---

## Risk Assessment

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| NiL.JS v2.6 can't support ES6+ fully | High | Medium | Polyfill bootstrap; consider migrating to Jint if blocking |
| netstandard1.4 blocks SkiaSharp | High | Medium | Use separate netstandard2.0 assembly; P/Invoke fallback |
| SkiaSharp NuGet unavailable for UWP ARM | High | Low | Pre-test with minimal sample; manual DLL reference |
| Flexbox/grid algorithm too complex for XAML mapping | Medium | Medium | Start with common patterns; accept approximations |
| Memory pressure on 1GB devices | High | Medium | Virtualize rendering; profile aggressively |
| Site compatibility regression | Medium | High | Per-site compatibility modes; snapshot testing |

---

## NiL.JS → Jint Migration (Contingency)

If NiL.JS v2.6 proves too limiting (missing critical ES6 features, no path forward):

1. **Jint** (GitHub: sebastienros/jint) — ES5.1 interpreter, no JIT, but mature and well-tested
2. **Jurassic** — Full ES5.1, JIT-compiled, but larger footprint
3. **Custom polyfill layer** — Keep NiL.JS, add missing features via JS bootstrap scripts

**Decision point**: After Phase 3 (Session 15), evaluate if NiL.JS + polyfills can handle jQuery 3.x. If not, prototype Jint integration.

---

## Success Metrics

| Metric | v1.0 | v1.5 (Phase 5) | v2.0 (Phase 8) |
|--------|------|-----------------|-----------------|
| Sites rendering recognizably | 1 | 10 | 20 |
| Flexbox support | Detection only | Full layout | Full + responsive |
| CSS Grid | Detection only | Basic template | Full Level 1 |
| JavaScript frameworks | None | jQuery 3.x | jQuery + Bootstrap JS |
| Canvas/SVG | None | Basic Canvas2D | Canvas2D + SVG |
| Memory (Lumia 640) | ~80MB | <120MB | <150MB |

---

## Session 7 Progress (June 13, 2026)

### Hacker News Test — First Real Site

**What works now:**
- ✅ Page loads, HTML parsed correctly (34KB)
- ✅ News items render with titles, points, authors, timestamps, comment counts
- ✅ Table layout partially works (numbered list, metadata rows)
- ✅ Links are visible and correctly colored
- ✅ Images load (Y logo shows)
- ✅ Footer links (Guidelines, FAQ, etc.) render horizontally
- ✅ Search form renders at bottom

**Remaining issues:**
- ❌ Nav bar links (new | past | comments | ask | show | jobs | submit) vertical instead of horizontal
- ❌ Orange header bar background missing
- ❌ Vote arrows (▲) not rendering
- ❌ Article titles not clickable (no tap handler for `<a>` in news items)
- ❌ `display: block` elements (like `<td>`) with inline children still have edge cases

**Key discoveries:**
1. **Dual rendering pipelines** — `DomBasicRenderer` (legacy, card mode) vs `RenderTreeBuilder` → `VirtualizingRenderer` (active, general web)
2. **Inline width bug** — `display:inline` elements got full parent width instead of shrink-to-fit → fixed with `targetWidth = Infinity`
3. **Missing user-agent styles** — `<CENTER>` had no style, `<TD>` had `Width=0` → both fixed
4. **Text measurement** — `RenderText.Layout()` uses `fontSize * 0.55` per char (approximation)

**Files modified this session:**
- `Engine/Core/RenderTreeBuilder.cs` — added `<CENTER> block`, removed `<TD> Width=0`
- `Engine/Core/RenderBox.cs` — inline width fix, layout diagnostics
- `Engine/Core/RenderText.cs` — (read only, no changes)
- `Engine/DevToolsLogger.cs` — timestamp on first write, fresh log per session
- `MainPage.xaml.cs` — `TEST_URL` constant for site testing
- `Doc/Plan_07.md` — updated with pipeline findings

---

## Session 8 Progress (June 13, 2026)

### HN Full Page Rendering — Navbar, Orange Header, All 30 News Items, Footer

**What was done:**
1. **HTML presentational attributes** — Added `ApplyHtmlAttributes()` to `RenderTreeBuilder.ApplyUserAgentStyles()`:
   - `bgcolor` → `BackgroundColor` (enables `<td bgcolor="#ff6600">`)
   - `color` → `ForegroundColor`
   - `width` → `Width`/`WidthPercent` (handles `width="85%"` on tables)
   - `height` → `Height`/`HeightPercent`
   - `cellpadding` → `Padding` on `<td>`/`<th>`
   - `align` → `TextAlign`

2. **VirtualizingRenderer BackgroundColor support**:
   - `HasBorderOrBackground()` now checks `BackgroundColor.HasValue`
   - `CreateBoxVisual()` creates Brush from BackgroundColor when Background is null
   - `ApplyStyleToVisual()` applies BackgroundColor to Border elements

3. **Root block display** — Added `#DOCUMENT`, `HTML`, `BODY` to block display list. Fixes infinite width propagation (`85% of ∞ = ∞`).

4. **CSS Media Query Whitespace Bug (CRITICAL FIX)** — `EvaluateMediaQuery()` in CssLoader.cs split on `" and "` but CSS media queries have newlines (`@media only screen\nand (min-width:300px)\nand (max-width:750px)`). Split failed → fell through to "unknown features → assume match" → **mobile CSS always applied regardless of viewport**. Fixed with `Regex.Replace(query, @"\s+", " ")`.

5. **`<span>` always inline** — Override CSS `display:block` on `<span>` elements. Span is semantically inline; CSS setting it to block was from broken media query.

6. **`<b>` inline override** — When `<b>` has `display:block` and parent is not flex, force to `inline`.

7. **Text measurement fix** — `RenderText.Layout()` charAdvance multiplier `0.55→0.62` (regular), `0.62→0.68` (bold). Fixes Verdana font truncation ("ne"→"new", "sho"→"show").

8. **Flex-column height bug (CRITICAL FIX)** — `LayoutFlexChildren()` returned `totalCrossSize` (sum of cross-axis sizes) for BOTH row and column layouts. For `flex-direction:column`, cross-axis = width, so it returned total WIDTH instead of total HEIGHT. Fixed by tracking `totalMainSize` and returning it for column layout.

**What works now:**
- ✅ Orange `#ff6600` header bar with correct height (~50px)
- ✅ **Nav bar HORIZONTAL**: Hacker News | new | past | comments | ask | show | jobs | submit | login
- ✅ All **30 news items** with numbered list, titles, points, authors, timestamps, comment counts
- ✅ Source domains in parentheses (e.g., "12gramsofcarbon.com")
- ✅ Footer: Guidelines | FAQ | Lists | API | Security | Legal | Apply to YC | Contact | Search
- ✅ "More" link at bottom
- ✅ Beige background (#f6f6ef) on content area
- ✅ Proper 85% table width
- ✅ Links underlined (clickable)

**Remaining issues:**
- ❌ Vote arrows (▲) not rendering (CSS background SVG)
- ❌ Article titles not clickable (links appear underlined but click not verified)
- ❌ Orange separator line at bottom of page renders as orange block (footer `<td bgcolor="#ff6600">`)
- ❌ Search form input not visible

**Key discoveries this session:**
1. **CSS Media Query Whitespace Bug** — `EvaluateMediaQuery()` splits on `" and "` but CSS has `\nand`. Parser falls through to "assume match" → mobile CSS always active. Fix: `Regex.Replace(@"\s+", " ")`.
2. **Root block display** — `#document`/`html`/`body` as `display:inline` caused ∞ width → `width="85%"` of ∞ = ∞.
3. **Flex-column return value bug** — `LayoutFlexChildren()` returned cross-axis total (width for column) instead of main-axis total (height for column). Caused ALL flex-column tables to have height = max-width of children.
4. **Text measurement** — Verdana is wider than the `0.55` multiplier assumed. Bumped to `0.62`/`0.68`.

**Files modified this session:**
- `Engine/Core/RenderTreeBuilder.cs` — `ApplyHtmlAttributes()`, `TryParseHtmlColor()`, `TryParseDouble()`, `<span>`/`<b>` inline overrides, `#DOCUMENT`/`HTML`/`BODY` block display
- `Engine/Core/VirtualizingRenderer.cs` — `HasBorderOrBackground()` BackgroundColor check, `CreateBoxVisual()` Brush creation, `ApplyStyleToVisual()` BackgroundColor to Border
- `Engine/Core/RenderBox.cs` — `LayoutFlexChildren()` column height fix, BGCOLOR diagnostic logging
- `Engine/Core/RenderText.cs` — charAdvance multiplier increase (0.55→0.62, 0.62→0.68)
- `Engine/CustomHtmlEngine.cs` — `ConfigureMedia()` CSS viewport clamping
- `Engine/CssLoader.cs` — `EvaluateMediaQuery()` whitespace normalization

---

## Session 9 Progress (June 13, 2026)

### Phase 1 Completion — Links + Layout Fixes

**What was done:**
1. **Flex-shrink for flex-row** (`RenderBox.cs:310`) — when flex-row children overflow the container, they proportionally shrink. Uses `FlexShrink` style property (default 1). Re-measures children with constrained width.
2. **Removed `flex-grow:1` from all `<td>`** (`RenderTreeBuilder.cs:181-186`) — previously ALL `<td>` got `flex-grow:1`, causing rank/vote/title columns to expand equally in flex-row, creating huge spacing gaps.
3. **Conditional flex-grow for `<td>` with bgcolor** (`RenderTreeBuilder.cs:265-270`) — only `<td>` elements with `bgcolor` attribute get `flex-grow:1`. This restores the full-width orange header bar while keeping news rows compact.
4. **`<A>` tag visual type key** (`VirtualizingRenderer.cs:265`) — `GetVisualTypeKey` now returns `typeof(Border)` for `<A>` tags, enabling proper element pool reuse.
5. **Link tap diagnostics** — `[DIAG:LINK] Attached handler` for every `<A>` tag with href; `[DIAG:LINK] Tapped` on click. All ~150+ links on HN receive handlers and navigation works.

**What works now:**
- ✅ All links clickable — `AttachLinkHandler` walks up RenderObject tree to find `<A>` with `href`, attaches `Tapped` handler
- ✅ HN layout compact — rank, vote, title, domain all properly spaced
- ✅ Orange header full-width
- ✅ Content fits viewport (no right-side overflow)

**Remaining from Phase 1:**
- Search form input visibility (footer `<center>` overflows)

**Files modified this session:**
- `Engine/Core/RenderBox.cs` — flex-shrink implementation
- `Engine/Core/RenderTreeBuilder.cs` — conditional `<td>` flex-grow
- `Engine/Core/VirtualizingRenderer.cs` — `<A>` visual type key, link diagnostics

---

## Session 10 Progress (June 13, 2026)

### Phase 2 Start — CSS Grid + Overflow

**What was done:**
1. **CSS Grid Layout Engine** (`RenderBox.cs:557-825`) — Full `LayoutGridChildren` method:
   - Parses `grid-template-columns`/`grid-template-rows` with `fr` units, `repeat()`, `px`, `%`
   - Resolves tracks proportionally using free space distribution
   - Handles `gap`/`row-gap`/`column-gap` between tracks
   - Supports `grid-column`/`grid-row`/`grid-area` item placement with spans
   - Auto-placement for items without explicit placement
   - Expanded `ExpandRepeat()`, `FindMatchingParen()`, `ParseGridLine()` helpers
   - No regression on HN (no grid sites tested yet)

2. **Overflow Clipping** (`VirtualizingRenderer.cs:195-244`) — `CollectVisible` now clips children to parent bounds:
   - `overflow: hidden` / `overflow: clip` — children outside parent rect are not collected
   - `overflow: auto` / `overflow: scroll` — same clipping (visual scroll needs nested ScrollViewer)
   - Replaced old `HasOverflowVisible` logic with proper clipping rects

**Verified working:**
- ✅ HN full page — all 30 items, footer, search input, links all functional
- ✅ No regression from grid/overflow changes

**Next (Session 11):**
- Position: sticky (T1.3)
- Test grid on real sites (example.com → Wikipedia)
- Overflow auto/scroll with nested ScrollViewer

**Files modified this session:**
- `Engine/Core/RenderBox.cs` — CSS Grid layout engine (`LayoutGridChildren`, `ResolveGridTracks`, `ExpandRepeat`, `ParseGridItemPlacement`, etc.)
- `Engine/Core/VirtualizingRenderer.cs` — overflow clipping in `CollectVisible`

---

## Session 11 Progress (June 13, 2026)

### Phase 2 Completion — Position: Sticky

**What was done:**
1. **Position: sticky** (`VirtualizingRenderer.cs`) — elements with `position: sticky` stick to the viewport top when scrolled past:
   - `_stickyElements` HashSet tracks sticky nodes
   - `_stickyOriginalY` stores natural Y position before sticky clamping
   - `_stickyTop` stores the `top` offset (e.g., `top: 0` means stick to very top)
   - `PlaceVisualOnCanvas` detects `position: sticky` and registers elements
   - `UpdateView` repositions sticky elements on every scroll: `targetY = max(origY, scrollOffset + top)`
   - Removes from `_stickyElements` on `ReturnToPool`

**Phase 2 Final Status:**
| Feature | Status | Notes |
|---------|--------|-------|
| CSS Grid Level 1 | ✅ Done | `fr`, `repeat()`, `gap`, `grid-column/row/area` |
| Overflow hidden/clip | ✅ Done | Children clipped to parent bounds |
| Position: sticky | ✅ Done | Re-positioned on scroll |
| CSS Shorthand | ✅ Already done | margin/padding/border/background |
| Overflow auto/scroll | 🔜 Deferred | Needs nested ScrollViewer |

**Files modified this session:**
- `Engine/Core/VirtualizingRenderer.cs` — sticky tracking + repositioning

---

### Session 12 — Phase 3 Start Preview

**Next priorities:**
1. Test CSS Grid on real sites (Wikipedia, MDN)
2. Overflow auto/scroll with nested ScrollViewer
3. More CSS features: `text-overflow: ellipsis`, `white-space`, `line-height`
4. Test on more sites from Phase 5 matrix

---

*End of Plan_07 — June 13, 2026*
