# Summary 5.07 – Phase G.2 v3: Forced D3 init, still empty SVG

**Date:** 2026-06-07  
**Session focus:** Verify SVG rendering from D3 on Nokia Design Archive timeline

## What was done
- Added forced D3 initialization in `TriggerDelayedSvgRefresh`:
  - Checks for `window.initTimeline`, calls it if exists
  - Otherwise dispatches `DOMContentLoaded` event
- Used `RunInline` (public method) to execute the script after Phase 4
- Extended SVG delay checks: 500ms, 1000ms, 2000ms, with total SVG count and children count
- Added final diagnostic `LogTimelineStateIfEmpty()` to dump timeline element state
- Added fetch diagnostics in `JavaScriptEngine.cs` to log any D3 data fetches

## Results from test log
- D3 loads and defines `d3.select` and `d3.forceSimulation`
- Timeline SVG element appears after Phase 4 with `children=0`
- Delayed checks show `svgWithChildren=0` for all attempts
- **No `[FETCH] Starting fetch for:` logs** → D3 never attempted to load data
- **No injected script logs** (`[DIAG] Calling window.initTimeline()` or `DOMContentLoaded`) → script didn't run
- `[DIAG:SVG] Timeline div element not found in DOM` – final state check fails because timeline div is not found (contradicts earlier presence? Possibly due to timing or different DOM tree)

## Root cause analysis
- The injected script in `TriggerDelayedSvgRefresh` does not produce output → likely `_activeJs` is `null` when the method runs.
- The JS engine reference is not preserved after Phase 3 execution, so `_activeJs` remains null.
- Without a live JS engine, forced DOM events cannot be dispatched.
- D3’s own data fetch never happens – possibly because the graph initialization code that would call `fetch` is never executed (due to missing timeline container at execution time).

## Next steps
1. **Preserve `_activeJs` reference** – assign `_activeJs = js` immediately after creating the engine, and clear it only on navigation reset.
2. **Add `regeneratorRuntime` polyfill** – inject `regenerator-runtime/runtime.js` before main-legacy chunk (addresses `typeof regeneratorRuntime=undefined`).
3. **Re‑run with engine alive** – verify that forced `DOMContentLoaded` event now triggers and D3 data fetch appears in logs.
4. **If still empty**, manually call D3 graph creation function after container exists.

## Files modified
- `Src/MediaExplorer/Engine/CustomHtmlEngine.cs` – added forced init and extended delay checks
- `Src/MediaExplorer/Engine/JavaScriptEngine.cs` – added fetch diagnostics

## Build status
✅ Rebuild with `RunInline` succeeded (0 errors).

## Open issues
- `_activeJs` null in `TriggerDelayedSvgRefresh`
- No fetch attempts from D3
- SVG remains empty

---
*Next session: 5.08 – Fix `_activeJs` persistence and test forced D3 init again.*
