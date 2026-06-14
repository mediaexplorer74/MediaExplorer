# Summary June 11 — Data Extraction Breakthrough 🎉

## The Problem
NiL.JS silently fails to evaluate the 589KB main-legacy chunk (both `SafeEval` with 7s timeout and bare `SafeEvalFast`). The `System.register(...)` call inside the chunk never fires — no module is registered for force-exec. All previous injections (sync graph builder into execute body, var→window data leak) were **dead code** because the execute function is never created.

## The Fix: C# Brace-Counting Data Extraction

Instead of evaluating the chunk as JS (which fails), we **extract the embedded data from the C# string** using character-level brace/bracket counting:

1. Find `Kf=` in the raw chunk text (`txt.IndexOf("Kf=")`)
2. Walk forward character by character, counting `{`/`[` (+1) and `}`/`]` (-1), skipping string literals
3. When depth reaches 0, we have the complete JS literal for `Kf`
4. Repeat for `wf`, `Cf`, `vf`
5. Inject each into NiL.JS via `SafeEval("window.Kf=" + extractedValue + ";")`
6. Run the graph builder synchronously from C# `SafeEval`

This completely **bypasses NiL.JS's 589KB evaluation failure** — the data values are 20-50KB each, well within NiL.JS's parsing capacity.

## Key Files Changed

| File | Lines | Change |
|------|-------|--------|
| `JavaScriptEngine.cs` | 3476-3486 | Fixed var→window leak: `Kf=` (bare) instead of `var Kf=` |
| `JavaScriptEngine.cs` | 3487-3562 | **NEW**: C# data extraction + SafeEval injection + sync graph builder |

## Results from Log

```
[DIADATA] graphBuilder done nodes=755 links=1647          ← graph built!
[DIAG:DATA] __graphData=nodes=755 links=1647              ← stored on window
[DIAG:DATA] __entries=722                                  ← 722 archive entries
[DIAG:DATA] __entriesWithDates=130                         ← 130 with valid dates (Timeline)
[DIAG:DATA] __stories=230                                  ← 230 stories
[DIAG:DATA] __keywords=91                                  ← 91 keywords
[DIAG:DATA] __Pf=33                                        ← 33 collections
[DIAG:DATA] large arrays on window: vf[91], __entries[722], __stories[230], ...
[DIAG:DATA] Found graph data at window.__graphData nodes=755 links=1647  ← persists through pipeline
```

## What Changed

### Phase 0 → Phase 1: Bypass chunk evaluation entirely
- **Before**: Inject sync graph builder into execute() body → chunk SafeEvalFast → execute() never runs → dead code
- **After**: Extract data from C# string → inject into NiL.JS via small SafeEval calls → run graph builder → **LIVE**

## Current Status

| Capability | Status |
|---|---|
| Graph data (755 nodes, 1647 links) extracted | ✅ |
| Graph data stored on `window.__graphData` | ✅ |
| Raw entry data (`__entries`, `__collections`, etc.) | ✅ |
| Timeline data (`__stories`, `__entriesWithDates`) | ✅ |
| Keywords (`__keywords`) | ✅ |
| Graph data found at Phase 4 rendering | ✅ |
| D3 visualization renders the graph | ❌ — `canvas=none` |
| Timeline mode renders | ❌ — `tag=SVG children=0` |

## The Remaining Blocker: D3 Canvas Rendering

The graph data is now available at `window.__graphData`, but the original web app's D3 rendering code never triggers because it waits on:

```javascript
// The data flow in the original chunk:
var Tf = Promise.resolve(!0).then(...)  // ← never executes (NiL.JS microtasks)
var Mf = function(fn) { Tf.then(fn) }   // ← scheduled on Tf
Mf(function() {
    // a = o.nodes, r = o.links;  // ← graph assignment + D3 render call
})
```

Since `Promise.resolve(!0).then(...)` never fires, `Mf` never schedules the render callback, and D3 never receives the data even though `window.__graphData` is populated.

## Next Session: D3 Canvas Rendering

The next session should trigger D3's force simulation and canvas rendering with our extracted data. Options:

1. **SafeEval D3 render call**: After extracting graph data, directly call D3 force simulation with `window.__graphData`:
   ```javascript
   var svg = d3.select("#graph");
   var simulation = d3.forceSimulation(nodes)
       .force("link", d3.forceLink(links))
       ...
   ```
   This bypasses the Promise chain entirely and renders using our extracted data.

2. **Patch Mf/Tf to use window.__graphData**: Replace the Tf Promise with a resolved value:
   ```javascript
   // In injected code:
   Mf = function(fn) { fn(); }
   Tf = Promise.resolve();
   ```
   This makes D3's original rendering call fire synchronously.

3. **Find and call the original render function**: The chunk has a render function `R(t, n)` that draws on the canvas. Find it and call it with our extracted nodes/links.

The primary goal: **a visible force-directed graph on the Lumia 950 screen.**

## Known Issues
- NiL.JS still cannot execute `Promise.resolve().then(...)` (microtask scheduler limitation)
- `SafeEval` catch-all at line 4263 swallows all exceptions silently
- The sync graph builder only replicates data processing; it does NOT trigger rendering
- `console.log` etc. are undefined in NiL.JS — always use `__diagLog` for diagnostics
