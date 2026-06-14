# Summary June 10 — Sync Graph Data Capture

## Problem
Graph nodes/links are embedded inline in the 589 KB main-legacy chunk (`Kf.entries`, `wf.collections`), not fetched via HTTP. The data processing runs inside `Promise.resolve(!0).then(() => { ... })`. NiL.JS microtasks never fire, so the callback never executes and the graph data is never processed.

## Fix
A synchronous graph builder is injected into the **end** of the chunk's `execute()` function (`JavaScriptEngine.cs:3638`). The injector:
1. Finds `execute:function(){`
2. Walks to the matching `}` (end of execute body)
3. Inserts code that reads `Kf.entries` and `wf.collections` directly

The injected code replicates the original processing **synchronously**:
- Maps `Kf.entries` → nodes (with `id`, `name`, `start`, `file`, `type: 'entry'`)
- Processes `wf.collections` → processed collections (`__xf`)
- Builds `__Pf` (collectionsById)
- Creates links between entries and their collections (excluding `C0030` and `K*`)
- Pushes collection nodes
- Stores result as `window.__graphData = { nodes, links, nodeConnections }`
- Also stores `System.__graphData`

## Raw Data Exposed on `window`
| Property | Source | Content |
|---|---|---|
| `__entries` | `Kf.entries` | 200+ records with `id`, `title`, `start`, `collections[]` |
| `__collections` | `wf.collections` | ~15 raw collections |
| `__xf` | processed from `wf` | collections with `id`, `name`, `description`, `theme` |
| `__Pf` | `__xf.reduce(...)` | collections lookup by id |
| `__stories` | `Cf.stories` | stories data for Timeline mode |
| `__entriesWithDates` | entries with `start.length >= 5` | Timeline-ready entries |
| `__keywords` | `vf` | keyword data |

## What's Still Working (previous sessions)
- Fallback eval via `SafeEval(callWrapped)`
- `force-exec` loop (calls `m.declare() + declared.execute()`)
- `FlushMicrotasks()` after force-exec
- `SubresourceAllowed = (u, kind) => true` in `CustomHtmlEngine.cs`
- D3 diagnostics (`typeof d3`, `typeof d3.select`, `typeof d3.forceSimulation`)

## What to Test
1. Launch app, check `Logger.txt` for:
   - `[DIAG:INJECT]` — does the inject prefix get found?
   - `[DIAG:MOD] __diagInjectRan=true`
   - `[DIAG:DATA] System.__graphData=nodes=X/links=Y` (X > 0, Y > 0)
   - `[DIAG:DATA] __entries=N`, `__stories=M`, etc.
2. If graph data is present, check canvas rendering (does the page show a `<canvas id="graph">`?)
3. Timeline data: `__entriesWithDates` should have entries with valid dates

## Known Limitations
- NiL.JS still cannot execute `Promise.resolve().then(...)` — this is a fundamental limitation of the engine's microtask scheduler
- The sync builder only replicates the data processing; it does NOT trigger any rendering — React/Svelte UI components that expect `_f`, `xf`, etc. will not see them (only `window.__graphData` is populated)
- If future chunk versions change the variable names or data structure, the injectCode must be updated
