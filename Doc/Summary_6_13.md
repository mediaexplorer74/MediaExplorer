# Session 6_12b — Phase R Complete: SVG→XAML Bridge Stripped

## Goal
Complete Phase R (Retrench) — remove all SVG→XAML rendering code that was declared unfit after 18+ sessions of debugging. Content doubled on every scroll/window-move due to architectural mismatch: SVG attributes vs XAML properties with no delta-update path. Win SDK 15063 (RS2) lacks modern capabilities. 200+ XAML shapes re-created per scroll — too heavy for Lumia 950 (3GB) / 640 (1GB).

## Changes

### `VirtualizingRenderer.cs` (1536 → 899 lines)
Removed:
- Orphaned `SvgRenderState` class body (was missing its class declaration after prior partial cleanup)
- `RenderSvgElement` — SVG LiteElement → XAML Canvas/Viewbox converter
- `AppendSvgChild` — switch-case for g/circle/line/rect/path/text/ellipse with state inheritance, translate transforms, dedup logic, and node-tap wiring
- `ApplySvgStateOverrides` — fill/stroke/opacity/style inheritance
- `GetAttr`, `TryGetAttr`, `ParseSvgLength`, `Clamp`, `CreateSvgBrush`, `CreateSvgPathElement` — SVG helpers
- `InjectSvgAsync` — SvgImageSource-based injection pipeline
- `RefreshSvgAsync` — throttled re-inject (15fps)
- `BuildSvgImageAsync`, `BuildSvgImageOnUiThreadAsync`, `BuildSvgImageOnUiThreadCoreAsync` — SvgImageSource load with thread marshalling
- `ParseSvgAttr` — fallback parser
- `_lastSvgRender` field
- Unused usings: `Windows.UI.Xaml.Shapes`, `Windows.UI.Xaml.Markup`, `System.Globalization`
Note: `Windows.UI.Xaml.Input` restored (needed for `TappedEventHandler` in link handler)

### `CustomHtmlEngine.cs` (2198 → 1802 lines)
Removed:
- `_svgRefreshPending` field
- `TriggerDelayedSvgExtractionAsync()` — waited 2s for D3, called `DumpSvgDom` + `GetDocumentSvgRoots` + `InjectSvgAsync`
- `TriggerDelayedSvgRefresh()` — **~340 lines** of:
  - D3 availability diagnostics (d3.select, d3.forceSimulation checks)
  - Manual circle drawing via D3 on timeline container
  - Global function search for timeline/graph init candidates with auto-invocation
  - Synthetic DOMContentLoaded dispatch
  - `System.register` module re-execution loop
  - Route simulation: nav link click, hash change (`#/network`), hashchange event dispatch
  - Polling loop (3s/4s/5s delays) checking for non-search-icon SVG children
  - Full re-render on SVG detection
- `LogTimelineStateIfEmpty()` — post-exhaustion timeline diagnostics
- `IsSvgOrHasSvgAncestor()` — no longer called after removing SVG mutation special-case
- SVG mutation special-case in `IncrementalUpdateAsync` (was doing full `UpdateView` on SVG mutations)
- SVG diagnostic comments (inline code comments only, no functional code)

### `JavaScriptEngine.cs` (11311 → 11187 lines)
Removed:
- `GetDocumentSvgRoots()` — walks LiteElement DOM collecting `<svg>` roots
- `CollectSvgRoots()` — recursive helper
- `SerializeSvgNode()` — recursive SVG subtree serializer (attributes, children, text, self-closing, xmlns)
- `DumpSvgDom()` — debug output of serialized SVG

## Preserved
- **Data extraction** — `window.__graphData` (755 nodes, 1647 links), `__entries` (722), `__stories` (230), `__keywords` (91), `__collections` (33) — kept for future SkiaSharp renderer
- **Route detection** — log-only hash set + nav link click prevention — kept
- **No second render cycle** — single pass — kept
- **DeployAndRun.ps1** — kept
- **Double render loop fix**, **Inception fix**, **Overlay race fix**, **NavigationCacheMode**, **welcomeShown/`_navigationComplete` guards** — kept
- **CacheMode="{x:Null}"** on ScrollViewer — kept
- **Checkbox/Radio label contrast** (`Foreground=#CCC`) — kept
- **DomBasicRenderer** (stable text/image/link renderer) — core of new card-based approach — kept
- **NodeTapped event chain** — still wired through CustomHtmlEngine → BrowserApi → MainPage; not raised currently but kept for future card tap handling
- **`namespaceURI` property** in LiteElement — DOM infrastructure for correct SVG element namespace — kept
- **SVG handler in DomBasicRenderer** — handles inline search icon SVG — kept
- **`SerializeSvgNode` reference check** — confirmed no remaining callers

## Build
`msbuild "Src\MediaExplorer\MediaExplorer.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:m` — **succeeded**.
All warnings are pre-existing (unused fields, deprecated `JSValue.Marshal` API).

## Next (Phase S — Smartphone Card Layout)
1. Auto-detect narrow viewport (`ApplicationView.VisibleBounds.Width < 600`)
2. Card stack layout via DomBasicRenderer HTML fragments from `__entries` data
3. Swipe navigation via `ManipulationMode="TranslateX"` (already on `ContentArea`)
4. Phase T: Entry detail + link routing
5. Phase U: Cleanup dead SVG diagnostics, performance tuning on Lumia 640
6. v1.0+: SkiaSharp (`SKXamlCanvas` + `DrawCircle`/`DrawLine`)
