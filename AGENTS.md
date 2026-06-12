# MediaExplorer — AI Context

## Automation Loop (June 12 — Fully Automated)

```powershell
# Step 1 — Build
msbuild "Src\MediaExplorer\MediaExplorer.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:m

# Step 2 — Deploy + launch
powershell -ExecutionPolicy Bypass -File "Src\MediaExplorer\DeployAndRun.ps1" -Platform x64

# Step 3 — Wait 150s for app to render (Nokia Archive loads ~589KB JS chunk)
Start-Sleep -Seconds 150
Get-Process -Name "MediaExplorer" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# Step 4 — Read diagnostics log
Get-Content "$env:LOCALAPPDATA\Packages\MediaExplorerV1p0_5gyrq6psz227t\LocalState\Logger.txt" -Tail 80
```

**Loop**: Build → Deploy+Launch → wait 150s → Kill → Read log → Analyze → Fix → Rebuild → repeat.

**One-liner**:
```powershell
msbuild "Src\MediaExplorer\MediaExplorer.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:m; if ($?) { powershell -ExecutionPolicy Bypass -File "Src\MediaExplorer\DeployAndRun.ps1" -Platform x64; Start-Sleep -Seconds 150; Get-Process -Name "MediaExplorer" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; Get-Content "$env:LOCALAPPDATA\Packages\MediaExplorerV1p0_5gyrq6psz227t\LocalState\Logger.txt" -Tail 80 }
```

## Current State (June 12, 2026 — Session 6.15)

**Version**: 1.0.0.0, AUMID `MediaExplorerV1p0!App`, PackageFamilyName `MediaExplorerV1p0_5gyrq6psz227t`
**Package Identity**: `MediaExplorerV1p0` (renamed from V0p57, new GUID, fresh install required)

### Critical Decision: SVG→XAML Bridge Abandoned

After 18+ sessions of debugging, the SVG→XAML rendering bridge is declared **unfit**:
- Content doubles on every scroll/window-move — unfixable
- Architectural mismatch: SVG attributes vs XAML properties with no delta-update path
- Win SDK 15063 (RS2) lacks modern capabilities (`x:Load`, `SvgImageSource` without hacks)
- 200+ XAML shapes re-created per scroll — too heavy for Lumia 950 (3GB) / 640 (1GB)

**New direction:** Remove all SVG→XAML rendering. Render only simplified content:
text blocks, images, link handlers via the existing stable DomBasicRenderer.
Return to complex graph/timeline at v1.1+ using SkiaSharp (hardware Canvas2D).

### Critical Decision #2: Card Mode is Now Default on All Viewports

Card mode activates whenever `__entries` data is available, regardless of viewport width.
On desktop (1024px+), card mode provides the same swipe/detail/filter UI as on phones.
For sites without entry data, `TryLoadEntryCards()` returns false → normal full-page rendering.

### Critical Decision #3: NiL.JS `globalThis` vs `window` for Data Injection

`HostWindow` (JavaScriptEngine.cs) is a C# POCO with fixed properties. `window.Kf = value` silently fails.
`globalThis` is a native NiL.JS ObjectInstance. Always use `globalThis.*` for data injection/reading.

### Critical Decision #4: C#-Only Data Extraction (bypasses NiL.JS JSON.stringify)

`JSON.stringify(722 entries)` inside a SafeEval call triggers the 7-second timeout.
Solution: Extract data from raw JS literals via C# brace-counting + JS-to-JSON key quoting.
Stores directly in static `_storedData` dictionary — no NiL.JS dependency.

### Session 6.15 — Card Mode Fixed + Navigation UI

#### What was fixed
1. **NiL.JS eval context isolation** — `window.*` → `globalThis.*` across 8 sites in JavaScriptEngine.cs
2. **JSON.stringify timeout** — C#-only extraction via `extractSubArray()` + `quoteJsKeys()` helpers
3. **Entry image URLs** — pattern is `./images/archive/{file}.jpg` (discovered from JS chunk's `Zf()` function)
4. **Card navigation UI** — visible ◀ ▶ buttons, counter with filtered/total context, Browse/All toggle

#### Key image URL discovery
Entry `file` field is a slug (e.g., `"04_morph_wrist_mode"`). Full URL:
```
https://nokiadesignarchive.aalto.fi/images/archive/{file}.jpg
```

#### C# extraction helpers added to JavaScriptEngine.cs
- `extractSubArray(objLiteral, key)` — finds `key:[...]` in JS object literal via brace-counting
- `quoteJsKeys(s)` — converts `{id:"x"}` to `{"id":"x"}` for valid JSON

### Diagnostics Architecture
- `DevToolsLogger.Log(msg)` → `%LOCALAPPDATA%\Packages\MediaExplorerV1p0_5gyrq6psz227t\LocalState\Logger.txt`
- `Debug.WriteLine(msg)` → VS Output only
- `__diagLog(msg)` → C# callback, writes both
- Key prefixes: `[DIAG:DATA]`, `[DIAG:SYS]`, `[DIAG:ROUTE]`, `[DIAG:CARD]` — keep

### Files
- `Src/MediaExplorer/Engine/JavaScriptEngine.cs` — data extraction, `globalThis.*` injection, SafeEval, `__storeData`
- `Src/MediaExplorer/Engine/BrowserApi.cs` — `ExtractEntriesJson()`, `ExtractCollectionsJson()`, `ExtractStoriesJson()`
- `Src/MediaExplorer/Engine/DomBasicRenderer.cs` — stable text/image/link renderer — **core**
- `Src/MediaExplorer/Engine/CustomHtmlEngine.cs` — ScheduleRepaintFromJs, DispatchRepaintAsync
- `Src/MediaExplorer/MainPage.xaml.cs` — card mode, navigation UI, image URLs, link routing — **core**
- `Src/MediaExplorer/MainPage.xaml` — ScrollViewer CacheMode
- `Src/MediaExplorer/Engine/DevToolsLogger.cs` — diagnostic logging

### Post-v1.0 Roadmap

#### v1.0+ (next iteration)
1. **After-collection-filter UX** — jump to first entry unique to filtered collection
2. **Card polish** — entrance/exit transitions, image preloading, smoother swipe
3. **Search/filter in category index** — text search across entry names/descriptions
4. **SkiaSharp graph rendering** — `SKXamlCanvas` with `DrawCircle`/`DrawLine` — 60fps on GPU
5. **Hybrid engine** — EdgeHTML for standard browsing, Custom engine for CSS/JS dev experiments

#### v1.1+ (future)
6. **E-book modes** — Poor (ascii), Rich (text+CSS), Asceti (minimal)
7. **Performance tuning** — focus on Lumia 640 (1GB) perf, reduce memory
8. **Orphaned ideas** — Sass preprocessing, markdown-to-html, service-worker-like caching

### Commands
```powershell
msbuild "Src\MediaExplorer\MediaExplorer.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:m
powershell -ExecutionPolicy Bypass -File "Src\MediaExplorer\DeployAndRun.ps1" -Platform x64
```
