# MediaExplorer — AI Context

## Workflow (semi-automatic)

1. **Build**: `msbuild "Src\MediaExplorer\MediaExplorer.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:m`
2. **Deploy**: `DeployAndRun.ps1` — регистрирует пакет и запускает приложение. `-Build` для сборки перед деплоем, `-NoLaunch` без запуска.
3. **Manual launch**: Пользователь сам запускает `MediaExplorerV0p57` из меню Пуск.
4. **User signal**: Пользователь говорит "готово" — AI читает логи.
5. **AI reads logs**: `%LOCALAPPDATA%\Packages\MediaExplorerV0p57_5gyrq6psz227t\LocalState\Logger.txt`
6. **AI analyzes & fixes** → rebuild → goto 1.

## Current State (June 11, 2026)

**Version**: 0.57.0.0, AUMID `MediaExplorerV0p57!App`, PackageFamilyName `MediaExplorerV0p57_5gyrq6psz227t`

### BREAKTHROUGH: Data Extraction Works (June 11)

**NiL.JS CANNOT evaluate the 589KB chunk** — both SafeEval (7s timeout) and SafeEvalFast fail silently. The `System.register(...)` inside the chunk never fires. All previous execute-body injections + var→window leaks were **dead code**.

**Fix**: Extract data from the C# string using character-level brace-counting, bypassing NiL.JS entirely:

1. Find `Kf=` in the raw chunk text via `txt.IndexOf("Kf=")`
2. Walk forward char-by-char, counting `{`/`[` (+1) and `}`/`]` (-1), skipping strings
3. Extract the JS literal for Kf, wf, Cf, vf
4. Inject each via `sysEngine.SafeEval("window.Kf=" + val + ";")`
5. Run sync graph builder as SafeEval

**Result**: `window.__graphData` = 755 nodes + 1647 links on all 4+ page loads.
All raw data also available: `__entries` (722), `__stories` (230), `__keywords` (91), etc.

### Key Discoveries

#### Graph data is embedded in the JS chunk
- `Kf` — `Kf = { entries: [...] }` (200+ records, inline data in the chunk, **NO `var`** prefix — bare `Kf=`)
- `wf` — `wf = { collections: [...] }` (~15 collections)
- `Cf` — `Cf = { stories: [...] }` (stories data for Timeline mode)
- `vf` — `vf = [...]` (keywords)
- **No `graph.json` or similar fetch** — graph data is inline in the chunk
- **D3 rendering still broken** because it waits on Promise chain (`Tf`/`Mf`) that never fires

#### Why injectCode (execute body) never worked
- The chunk's `System.register(...)` never fires because NiL.JS silently fails on 589KB evaluation
- Without module registration, force-exec has nothing to execute
- The sync graph builder injected into execute() body is **dead code**
- The `__diagInjectRan` check returns `undefined` confirming the inject never ran

#### Why data extraction works
- NiL.JS CAN evaluate small SafeEval calls (20-50KB each)
- The brace-counting extractor handles minified code correctly (skips strings)
- Works regardless of `var`/bare assignment because it finds `Kf=` directly

### Still works (previous changes)
- `SafeEval` chunk evaluation (fallback to `callWrapped`)
- `force-exec` loop that iterates `System.registry._entries` and calls `m.declare(...)` + `declared.execute()`
- `FlushMicrotasks()` after force-exec
- `SubresourceAllowed = (u, kind) => true` in `CustomHtmlEngine.cs`

### Diagnostics architecture
- `DevToolsLogger.Log(msg)` → пишет в `%LOCALAPPDATA%\Packages\...\LocalState\Logger.txt`
- `Debug.WriteLine(msg)` → только в VS Output, НЕ в Logger.txt
- `__diagLog(msg)` → C# callback на `_nil`, вызывает `DevToolsLogger.Log` + `Debug.WriteLine`
- `[DIAG:INJECT]`, `[DIAG:SYS]`, `[DIAG:DATA]`, `[DIAG:MOD]` — ключевые префиксы

### Current Approach: Pure-JS Force Layout (June 11-12)
- D3 `forceSimulation` + `forceLink` breaks in NiL.JS — all node positions become NaN after `sim.tick(100)` because link source/target objects have different references than node objects in `gd.nodes`, and D3's `.id()` resolution fails silently.
- **Fix**: Replace D3 force with a manual ~50-line force-directed layout:
  1. Build `nodeMap` by `id`
  2. Resolve links: convert link source/target to actual node objects from `nodeMap`
  3. Random initial positions (deterministic via index)
  4. 120 iterations: repulsion (all pairs), spring attraction (links), center gravity
  5. Velocity damping (`cooling=0.98`, `dt=0.4`)
- SVG XML build + `innerHTML` injection unchanged.
- **Timeline mode** will use same technique for story/entry nodes.

### Next Steps
1. **Test pure-JS force layout**: rebuild & run — check `[DIAG:SVG] validNodes=755 nanNodes=0`
2. If SVG renders visible graph: refine styling (colors, sizes, label overlay)
3. **Timeline mode**: port same layout to timeline page

### Files
- `Src/MediaExplorer/Engine/JavaScriptEngine.cs` — основной движок (~11310 строк), data extraction ~lines 3487-3562, injection ~lines 3632-3728, SafeEval ~line 4197
- `Src/MediaExplorer/Engine/DevToolsLogger.cs` — логгер
- `Src/MediaExplorer/DeployAndRun.ps1` — скрипт деплоя
- `Src/NilJsTest/tests/main-legacy-CgkFIb-k.js` — архивная копия чанка 589 КБ
- `Doc/Summary_6_11.md` — последний summary (June 11 — data extraction breakthrough)
- `Doc/Summary_6_10.md` — предыдущий summary (superseded)
- `Doc/Plan_05.md` — генеральный план

### Build Commands
```powershell
msbuild "Src\MediaExplorer\MediaExplorer.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:m
powershell -ExecutionPolicy Bypass -File "Src\MediaExplorer\DeployAndRun.ps1" -Platform x64
```
