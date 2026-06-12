# Plan_06 — Post-v1.0 Roadmap

**Date**: June 12, 2026
**Status**: Planning — ready to begin execution after v1.0 testing

---

## Overview

v1.0 (Phases R/S/T) delivers a working card-based archive browser for narrow viewports on W10M. The SVG→XAML bridge is abandoned; complex graph and timeline rendering is deferred to SkiaSharp. This plan defines the next iteration of work.

**Priority tiers**:
- **P0** — Must-have for next release
- **P1** — Important but can wait
- **P2** — Nice-to-have / research

---

## v1.0+ (Immediate Next Iteration)

### 1. Search/Filter in Category Index (P0)

**What**: Add text search across entry names and descriptions in the category index view.

- Text input field at top of category index
- Real-time filtering as user types (debounced 300ms)
- Match against entry `name`, `description`, `type` fields
- Show matched entry count + "no results" state
- Filtered entries become tappable → card mode at that entry
- Re-use existing `_entryCards` / `_backupEntryCards` mechanism

**Files**: `MainPage.xaml.cs` (search input handler, filter logic)
**Risk**: Low — self-contained UI feature

### 2. Card Polish — Transitions & Animation (P1)

**What**: Entrance/exit transitions for card mode, smoother swipe feedback.

- Card entrance: fade + slide-up when entering card mode from full page
- Card exit: fade + slide-down when closing card mode
- Swipe feedback: opacity/scale transform during drag (beyond threshold → commit)
- Collection filter enter/exit: subtle fade transition
- Category index → card: slide transition

**Files**: `MainPage.xaml.cs` (Storyboard animations, Composition API)
**Risk**: Low-Medium — Composition API available on 15063 (requires `Windows.UI.Composition`)

### 3. Image Preloading (P1)

**What**: Pre-cache images for adjacent entries in card mode to reduce visual loading.

- When viewing card at index `i`, begin loading image for `i+1` (and optionally `i-1`)
- Use `BitmapImage` with `UriSource` and track load state
- Show low-res placeholder or skeleton while loading
- Cancel preloads when filter changes or nav skips multiple cards

**Files**: `MainPage.xaml.cs` (BitmapImage cache dictionary)
**Risk**: Low — standard UWP pattern

### 4. SkiaSharp Graph Rendering (P1)

**What**: Reintroduce graph/timeline visualization via `SkiaSharp.Views.UWP`.

- Add `SkiaSharp.Views.UWP` NuGet package (need netstandard1.4 compatible version)
- `SKXamlCanvas` overlay in card mode (or as separate view)
- Draw nodes as circles with `DrawCircle`, edges as lines with `DrawLine`
- Layout algorithms: circular (simple), force-directed (from saved `__graphData`)
- Timeline: greedy row packing (from saved `__collections`/`__stories`)
- Performance target: 60fps on Lumia 950 GPU

**Files**: New `Views/GraphCanvas.cs`, `Views/TimelineCanvas.cs`
**Risk**: High — need compatible SkiaSharp package for W10M 15063; NuGet ecosystem for UWP ARM is fragile

### 5. Hybrid Engine — EdgeHTML + Custom (P2)

**What**: Allow the app to use the system WebView (EdgeHTML) for standard browsing and fall back to the custom engine for CSS/JS dev experiments.

- "Engine" toggle button in UI (or auto-detect by URL)
- EdgeHTML via `WebView` control (available on 15063)
- Custom engine retains all current behavior for `localhost`/`about:` pages
- Share cookies/cache between engines (optional)

**Files**: `MainPage.xaml` + `MainPage.xaml.cs` (WebView visibility toggling)
**Risk**: Medium — two rendering engines means double the state to manage

### 6. E-book Mode Improvements (P2)

**What**: Enhance the three reading modes (Poor/Rich/Asceti).

- **Poor (ascii)**: Strip all images, render text as monospace, minimal margins — for 1GB devices
- **Rich (text+CSS)**: Current behavior — CSS applied, JS disabled, images shown
- **Asceti (minimal)**: New mode — only entry title + description + type badge, no images, no chips — for quick scanning

**Files**: `MainPage.xaml.cs` (mode switching, render options)
**Risk**: Low — mostly filtering what DomBasicRenderer outputs

---

## v1.1+ (Future)

### 7. Orphaned Ideas from Old Plans

From Plan_01/02/03, never implemented:

- **Sass/SCSS/LESS preprocessing** — parse in JS, output CSS → feed into CSS engine
- **Markdown-to-HTML** — render `.md` files as styled pages
- **Service-worker-like caching** — offline-first resource caching with stale-while-revalidate
- **CSS `calc()` support** — required for many real-world sites
- **CSS Grid layout** — beyond flexbox
- **WebSocket / fetch API** — beyond XHR

**Priority**: All P2 — these are experiments, not core features.

### 8. Performance Tuning

**Focus**: Lumia 640 (1GB RAM) performance.

- Measure memory usage with `MemoryManager.AppMemoryUsage`
- Lazy-load entry data (chunked parsing of `__entries`)
- Reduce XAML element count in card mode (recycle card panels)
- Minimize string allocations in JSON extraction
- Profile GC pauses during card navigation
- Consider `{x:Bind}` vs `Binding` performance

**Priority**: P2 until SkiaSharp graph is in place

---

## Implementation Order

```
Iteration A: Search/filter + image preloading (P0-P1, ~1 session)
Iteration B: Card transitions + animation polish (P1, ~1 session)
Iteration C: SkiaSharp graph rendering (P1, ~2-3 sessions, high risk)
Iteration D: Hybrid engine experiment (P2, ~1 session)
Iteration E: E-book mode improvements (P2, ~1 session)
Iteration F: Orphaned ideas exploration (P2, ~2 sessions)
Iteration G: Performance tuning on Lumia 640 (P2, ongoing)
```

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| SkiaSharp NuGet incompatible with 15063 | High | Pre-test with sample UWP app; consider manual DLL reference |
| Composition API unavailable | Medium | Fall back to Storyboard-based animations (always available) |
| Search perf with 722 entries | Low | Debounce input; filter on background thread |
| Image preloading causes OOM on 1GB | Medium | Limit preload queue to 2 images; cancel on filter change |

---

*End of Plan_06 — June 12, 2026*
