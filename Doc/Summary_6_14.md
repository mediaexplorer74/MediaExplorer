# MediaExplorer — Summary

**Version**: 1.0.0.0 | **Codename**: *(none)* | **Platform**: W10M 15063+ (RS2)
**Target**: Lumia 950 / 640 / 1020 (ARM), x64 (dev emulator)
**Identity**: `MediaExplorerV1p0` (new GUID `d98c852a-79c9-49c9-b630-fdeea9ec3335`)
**AUMID**: `MediaExplorerV1p0!App` | **FamilyName**: `MediaExplorerV1p0_5gyrq6psz227t`

---

## Feature Checklist

Status legend: ✅ done | 🟡 partial | ⏸ deferred | ❌ abandoned

### Phase 0 — Foundation (UDAIE-A WebView port)

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| 0.1 | WP8.1 WebView → UWP port | ✅ | Core architecture from UDAIE-A/WEBVIEW |
| 0.2 | WebViewBasic (minimal browser shell) | ✅ | Address bar, back/forward, nav buttons |
| 0.3 | Deployment via AppX registration | ✅ | `DeployAndRun.ps1` |
| 0.4 | AppxManifest for W10M 15063 | ✅ | TargetDeviceFamily, capabilities |
| 0.5 | DevToolsLogger | ✅ | File-based logging to LocalState |

### Phase 1 — HTML + CSS engine (Plan_01)

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| 1.1 | Custom HTML parser | ✅ | Tag-based, self-closing, attribute parsing |
| 1.2 | CSS selector engine | ✅ | Class, ID, tag, descendant, pseudo-class |
| 1.3 | CSS cascade + specificity | ✅ | Inline > ID > class > tag |
| 1.4 | Flexbox layout | ✅ | Basic flex-direction, wrap, justify-content |
| 1.5 | Box model (margin/border/padding) | ✅ | Collapsing margins partial |
| 1.6 | DOM tree (parent/child/sibling) | ✅ | Full tree with mutation tracking |
| 1.7 | Inline styles via `style` attribute | ✅ | Parsed and applied |
| 1.8 | `<style>` tag support | ✅ | Appended to stylesheets |
| 1.9 | External CSS loading | ✅ | HTTP fetch + cache |

### Phase 2 — JavaScript runtime (Plan_02)

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| 2.1 | NiL.JS integration | ✅ | netstandard1.4 for W10M compat |
| 2.2 | NiL.JS 2.6.1 (netstandard2.0→1.4 port) | ✅ | Custom fork, full W10M build |
| 2.3 | Console API (log, warn, error) | ✅ | Mapped to C# callbacks |
| 2.4 | ES Modules parsing | ✅ | Vite bundles parse successfully |
| 2.5 | `window.__globals` data extraction | ✅ | `__graphData`, `__entries`, etc. |
| 2.6 | D3.js force simulation | ✅ | Tick fires, nodes/links generated |
| 2.7 | D3.js v4/v5 DOM output | ✅ | SVG elements created in DOM |
| 2.8 | Private fields (`#name`) | ❌ | Not supported by NiL.JS |
| 2.9 | Arrow functions / async / promises | ✅ | NiL.JS built-in |
| 2.10 | ES Modules runtime errors | 🟡 | Still being fixed case-by-case |

### Phase 3 — XAML rendering (Plan_03)

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| 3.1 | DomBasicRenderer (text/image/link) | ✅ | Stable, used as core renderer |
| 3.2 | XAML Image element from `<img>` | ✅ | URL → BitmapImage → Image |
| 3.3 | Hyperlink from `<a>` | ✅ | Click → NavigateAsync |
| 3.4 | TextBlock from text/`<p>`/`<h1-6>` | ✅ | Font size cascading |
| 3.5 | CSS → XAML property mapping | ✅ | color → Foreground, etc. |
| 3.6 | ScrollViewer content hosting | ✅ | VirtualizingStackPanel |
| 3.7 | Flexbox → XAML layout | 🟡 | Basic works, `calc()` missing |
| 3.8 | 3 UI modes (Hided/Semi/Full) | ✅ | Strip, expandable, full bar |
| 3.9 | 3 render modes (Full/Rich/Poor) | ✅ | JS+CSS / CSS-only / plain text |
| 3.10 | MutationObserver → incremental re-render | ✅ | DOM change detection |
| 3.11 | CSS animations/transitions | ⏸ | Not implemented |

### Phase 4 — SVG→XAML bridge (Plan_04/05) — ABANDONED

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| 4.1 | SVG element → XAML UIElement map | ❌ | Bridge abandoned after 18+ sessions |
| 4.2 | `<circle>` → Ellipse | ❌ | Content doubling unfixable |
| 4.3 | `<line>` → Line | ❌ | No delta-update path |
| 4.4 | `<rect>` → Rectangle | ❌ | 200+ shapes recreated per scroll |
| 4.5 | `<path>` → Path | ❌ | Too heavy for Lumia 640 (1GB) |
| 4.6 | `<text>` → TextBlock | ❌ | Win SDK 15063 lacks SvgImageSource |
| 4.7 | `<g>` → Canvas | ❌ | All abandoned |
| 4.8 | viewBox scaling | ❌ | |
| 4.9 | Circle layout (graph) | ❌ | Deferred to SkiaSharp v1.1+ |
| 4.10 | Timeline layout (greedy row packing) | ❌ | Deferred to SkiaSharp v1.1+ |

### Phase R — Retrench (Plan_05)

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| R.1 | Remove SVG→XAML injection code | ✅ | renderCode stripped from JS engine |
| R.2 | Remove VirtualizingRenderer SVG mapping | ✅ | SVG→XAML mapping removed |
| R.3 | Remove dedup logic (D2D/VisualTree) | ✅ | No longer needed |
| R.4 | Verify base HTML/CSS rendering | ✅ | Nokia Archive page renders via DomBasicRenderer |
| R.5 | Keep data extraction intact | ✅ | `__graphData` + globals reserved |
| R.6 | Clean up SVG diagnostic logs | 🟡 | `[DIAG:SVG]` etc still present in code |

### Phase S — Smartphone adaptivity

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| S.1 | Narrow viewport detection (<600px) | ✅ | `ApplicationView.VisibleBounds.Width` check |
| S.2 | Card stack layout | ✅ | One entry per card, swipeable |
| S.3 | Card toolbar (close/browse/back/forward) | ✅ | Bottom bar with actions |
| S.4 | Swipe navigation (ManipulationMode) | ✅ | TranslateX with thresholds |
| S.5 | Entry list rebuild from `__entries` data | ✅ | 722 entries parsed + displayed |
| S.6 | Card mode toggle (full page ↔ cards) | ✅ | "Close" exits card mode |

### Phase T — Text/Image/Link detail views

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| T.1 | Type badge pill on cards | ✅ | `type` field → TextBlock badge |
| T.2 | Related collection chips | ✅ | Tappable → filter by collection |
| T.3 | Story references | ✅ | Links to containing stories |
| T.4 | Category index (Collections grouped) | ✅ | "Browse" shows grouped list |
| T.5 | Collection → entry filter | ✅ | Tap collection → filtered list |
| T.6 | "All" button restores full list | ✅ | Clears collection filter |
| T.7 | Entry URL interception | ✅ | `/entry/E0001` or `?entry=E0001` |
| T.8 | External link routing | ✅ | `Launcher.LaunchUriAsync` |
| T.9 | `ExtractEntriesJson` from JS globals | ✅ | `__entries` → List<Dictionary> |
| T.10 | `ExtractCollectionsJson` | ✅ | `__collections` → List<Dictionary> |
| T.11 | `ExtractStoriesJson` | ✅ | `__stories` → List<Dictionary> |
| T.12 | `_collectionLookup` Dictionary | ✅ | ID→title for chip resolution |
| T.13 | `_backupEntryCards` for filter restore | ✅ | Preserves full list on filter |
| T.14 | `_filterCollectionId` filter state | ✅ | Active collection filter |

### Infrastructure

| # | Feature | Status | Notes |
|---|---------|--------|-------|
| I.1 | Disk cache (priority queue) | ✅ | HTTP resource caching |
| I.2 | DevTools (Console/DOM/Network/Debug) | ✅ | 4-tab DevTools panel |
| I.3 | Screenshot (single + full-page) | ✅ | Saves to Pictures/MediaExplorer |
| I.4 | Keyboard shortcuts (Ctrl+L, Ctrl+B) | ✅ | Focus URL, toggle bar |
| I.5 | Startup URL file (startup_url.txt) | ✅ | Auto-navigate on launch |
| I.6 | Automation loop (build→deploy→log) | ✅ | Full CI-like loop |
| I.7 | ARM cross-compilation (x64 dev/test) | ✅ | Platform targets: x64, ARM, x86 |
| I.8 | AppXBundle build | 🟡 | APPX4001 warning (BundlePlatforms) |

---

## Key Decisions

1. **SVG→XAML bridge abandoned** (June 2026) — after 18+ sessions, architectural mismatch with no delta-update path on Win SDK 15063. Replaced by card-based layout.
2. **Version jump 0.57 → 1.0** — marks Phase T completion; fresh install required (new GUID, new package identity).
3. **SkiaSharp deferred to v1.1+** — graph/timeline rendering via `SKXamlCanvas` with Canvas2D API.
4. **DomBasicRenderer as core** — stable text/image/link renderer for all current content.

## File Map (key files)

| File | Lines | Role |
|------|-------|------|
| `MainPage.xaml.cs` | ~1200 | Card mode, navigation, link routing, UI |
| `MainPage.xaml` | ~200 | Layout, ScrollViewer, CacheMode |
| `Engine/JavaScriptEngine.cs` | ~4200 | NiL.JS context, data extraction, render code |
| `Engine/BrowserApi.cs` | ~600 | JSON extraction helpers |
| `Engine/DomBasicRenderer.cs` | ~800 | Text/image/link XAML rendering |
| `Engine/CustomHtmlEngine.cs` | ~1500 | HTML parse → DOM → render pipeline |
| `Engine/Core/VirtualizingRenderer.cs` | ~500 | SVG→XAML mapping (stripped) |
| `Engine/DevToolsLogger.cs` | ~50 | Diagnostic logging |
| `Package.appxmanifest` | ~60 | App identity, capabilities, display |

---

## Build & Deploy

```powershell
msbuild "Src\MediaExplorer\MediaExplorer.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:m
powershell -ExecutionPolicy Bypass -File "Src\MediaExplorer\DeployAndRun.ps1" -Platform x64
```

## Post-v1.0 Roadmap

See `Doc/Plan_06.md` for full roadmap.
