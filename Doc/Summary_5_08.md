# Summary 5.08/5.09 – Two Parallel SVG Paths: G.2 Live XAML (+ D3 patch) vs G.1 SvgImageSource

**Date:** 2026-06-08  
**Session focus:** Analyze full build log, identify two parallel rendering paths, fix G.1 thread crash, diagnose missing XHR

## The Two Parallel Paths

### Path G.2 — Live XAML Shapes via VirtualizingRenderer (✅ Primary, working for static SVGs)

**What it does:** Maps SVG elements to native UWP XAML shapes — `<circle>` → `Ellipse`, `<line>` → `Line`, `<path>` → `Path`, `<text>` → `TextBlock`, `<g>` → `Canvas` — integrated into `VirtualizingRenderer.CreateBoxVisual` / `RenderSvgElement`.

**What works:**
- ✅ Static `<svg class="search-icon">` (251 chars, X icon) renders via G.2, visible on screen
- ✅ D3 patch (`PatchD3DomManipulation`) intercepts `d3.select`, wraps `createElementNS` → manual D3 calls produce visible circles via `UpdateView()`
- ✅ SVG mutation detection: `IsSvgOrHasSvgAncestor` → forces full `UpdateView()` instead of broken `PatchAdded`
- ✅ Three‑attempt delayed refresh (3000/4000/5000 ms) with D3 manual circle test

**What's blocked:**
- ❌ `<svg id="timeline"/>` is self-closing (55 chars, empty) — D3 created the shell but never populated it
- ❌ No XMLHttpRequest constructor in NiL.JS → `[XHR] No XMLHttpRequest available` → D3 v5's data loading (`d3.json`, `d3.csv`, etc.) fails silently
- ❌ No graph data in global scope — `[DIAG:DATA] No graph data found in global scope`
- ❌ Fetch interceptor captured only consent-manager requests (`usercentrics.eu`), no timeline data URL

### Path G.1 — SvgImageSource Fallback (❌ Was crashing, ✅ Now fixed)

**What it does:** Serializes the D3-generated SVG DOM tree to an SVG string, renders it as `SvgImageSource` → `Image` element, added to the Canvas. Provides a bitmap-level fallback when G.2's live element mapping can't work.

**Components (all implemented):**
- `JavaScriptEngine.GetDocumentSvgRoots()` / `CollectSvgRoots()` / `SerializeSvgNode()` / `DumpSvgDom()` — extract SVG from LiteElement tree
- `CustomHtmlEngine.TriggerDelayedSvgExtractionAsync()` — dump → wait 2s → dump → `InjectSvgAsync()`
- `VirtualizingRenderer.InjectSvgAsync()` / `RefreshSvgAsync()` / `BuildSvgImageAsync()` / `ParseSvgAttr()` — create Image from SVG string

**What was found:**
- `[SVG] GetDocumentSvgRoots → found 2 <svg> element(s)` — extraction works
- SVG 1: search-icon (static, 251 chars) → would render
- SVG 2: timeline (`<svg id="timeline"/>`, empty, 55 chars) → would render blank
- **Crashed with `RPC_E_WRONG_THREAD`** — `SvgImageSource` created on background thread

**Fix applied this session:**
- `BuildSvgImageAsync` now marshals to UI thread via `_scrollViewer.Dispatcher` using `TaskCompletionSource` pattern
- Build confirms: **0 errors** via `MSBuild.exe` VS 2026 Insiders (x86 Debug, 1m25s)

## Comprehensive Log Analysis

| Signal | Meaning |
|--------|---------|
| `[DIAG:EXEC] d3js.org script (no polyfill prefix — using v5)` | D3 loads as v5 (248313 bytes = v5.16.0) despite requesting `d3.v7.min.js` |
| `[DIAG] D3 patch applied successfully` | `PatchD3DomManipulation` intercepts `d3.select` |
| `[DIAG] D3 svg appended` + `[DIAG] D3 circle appended` | Manual test circle renders at each delay check |
| `typeof d3.select = function` + `typeof d3.forceSimulation = function` | D3 engine fully operational |
| `[XHR] No XMLHttpRequest available` | NiL.JS has no XHR — **root cause of empty timeline** |
| `[DIAG:DATA] No graph data found in global scope` | All data scans return nothing |
| `[DIAG:SYS] split chunk into 1 System.register calls` → `[DIAG:SYS] call #1 OK (589101 bytes)` | SystemJS main module loads and executes |
| `RPC_E_WRONG_THREAD` in `InjectSvgAsync` | G.1 thread crash (now fixed) |
| `[DIAG:SVG] Timeline div element not found in DOM` | `_activeDom` staleness at final check |

## Critical Path Forward

```
Add XMLHttpRequest stub → D3 gets data → timeline SVG populated → G.2 detects mutations → graph renders
                                                              ↓ (if no data via XHR)
                                      Search SystemJS 589KB chunk for embedded JSON → manual graph construction
                                                              ↓ (if no JSON found)
                                      G.1 SvgImageSource fallback (thread crash now fixed)
```

## Files modified
- `Src/MediaExplorer/Engine/Core/VirtualizingRenderer.cs` — `BuildSvgImageAsync` thread marshalling fix (dispatcher + `TaskCompletionSource`)

## Build status
✅ **0 errors** via VS 2026 Insiders MSBuild (x86 Debug, 1m25s)

## Open issues
- ❌ **XMLHttpRequest not available** in NiL.JS — the primary blocker. D3 v5 needs it for `d3.json` / `d3.csv` / `d3.tsv`.
- ❌ Timeline `<svg id="timeline"/>` empty (`children=0`) — D3 never receives data to populate it.
- ❌ No graph data visible anywhere (`window`, `__INITIAL_STATE__`, `__NEXT_DATA__`, etc.) — D3's data loading chain is broken.
- ✅ G.1 thread crash **FIXED** — will show at least the empty SVG as image.

---
*Next session: 5.10 — Add minimal XMLHttpRequest stub to JavaScriptEngine.cs, rebuild, deploy, test*
