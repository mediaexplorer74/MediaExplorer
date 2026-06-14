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
Get-Content "$env:LOCALAPPDATA\Packages\MediaExplorerV1p1_5gyrq6psz227t\LocalState\Logger.txt" -Tail 80
```

**Loop**: Build → Deploy+Launch → wait 150s → Kill → Read log → Analyze → Fix → Rebuild → repeat.

**One-liner**:
```powershell
msbuild "Src\MediaExplorer\MediaExplorer.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:m; if ($?) { powershell -ExecutionPolicy Bypass -File "Src\MediaExplorer\DeployAndRun.ps1" -Platform x64; Start-Sleep -Seconds 150; Get-Process -Name "MediaExplorer" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; Get-Content "$env:LOCALAPPDATA\Packages\MediaExplorerV1p1_5gyrq6psz227t\LocalState\Logger.txt" -Tail 80 }
```

## Current State (June 14, 2026 — Session 19)

**Version**: 1.1.0.0, AUMID `MediaExplorerV1p1!App`, PackageFamilyName `MediaExplorerV1p1_5gyrq6psz227t`
**Package Identity**: `MediaExplorerV1p1` (renamed from V1p0, fresh install required)

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
- `DevToolsLogger.Log(msg)` → `%LOCALAPPDATA%\Packages\MediaExplorerV1p1_5gyrq6psz227t\LocalState\Logger.txt`
- `Debug.WriteLine(msg)` → VS Output only
- `__diagLog(msg)` → C# callback, writes both
- Key prefixes: `[DIAG:DATA]`, `[DIAG:SYS]`, `[DIAG:ROUTE]`, `[DIAG:CARD]` — keep

### Files
- `Src/MediaExplorer/Engine/JavaScriptEngine.cs` — ES6+ polyfills, event system, fetch API, DOM manipulation, `globalThis.*` injection, SafeEval, `__storeData`, charset detection integration
- `Src/MediaExplorer/Engine/CharsetDetector.cs` — **NEW** BOM + `<meta charset>` encoding detection (UTF-8, windows-1251, koi8-r, shift_jis, etc.)
- `Src/MediaExplorer/Engine/MarkdownRenderer.cs` — **NEW** Markdown-to-HTML converter (headers, bold, italic, links, images, code blocks, lists, tables)
- `Src/MediaExplorer/Engine/BrowserApi.cs` — `ExtractEntriesJson()`, `ExtractCollectionsJson()`, `ExtractStoriesJson()`, markdown URL detection
- `Src/MediaExplorer/Engine/DomBasicRenderer.cs` — stable text/image/link renderer
- `Src/MediaExplorer/Engine/CustomHtmlEngine.cs` — RenderTreeBuilder → LayoutEngine → VirtualizingRenderer pipeline, **E-book modes** (Rich/Poor/Asceti)
- `Src/MediaExplorer/Engine/Core/VirtualizingRenderer.cs` — XAML Canvas rendering, overflow:auto/scroll, overflow-x/y, sticky positioning, SVG→BitmapImage fallback, text-overflow:clip
- `Src/MediaExplorer/Engine/Core/RenderTreeBuilder.cs` — table grid layout, CAPTION support, HTML attribute parsing, body display:none override
- `Src/MediaExplorer/Engine/Core/RenderBox.cs` — table grid layout, flex/block/inline layout, border-spacing, display:none
- `Src/MediaExplorer/MainPage.xaml.cs` — card mode, navigation UI, image URLs, link routing, **Reddit JSON API card mode**, Reddit comments view, score coloring
- `Src/MediaExplorer/MainPage.xaml` — ScrollViewer CacheMode
- `Src/MediaExplorer/SettingsPage.xaml` — E-book mode selector (Rich/Poor/Asceti), no JS toggle
- `Src/MediaExplorer/Engine/DevToolsLogger.cs` — diagnostic logging

### Post-v1.0 Roadmap

#### v1.1 (current session)
- Phase 3 (JS Engine): ES6 polyfills, DOM API, fetch, events, element.style ✅
- Phase 4 (HTML & Media): table grid layout, select, iframe, srcset ✅
- Phase 5 (Site Compatibility): Reddit JSON API, SVG fallback, body display override ✅
- Settings re-navigation fix ✅
- Reddit score coloring (orange/red/gray) ✅
- Reddit comments view (fetches comments.json, nested indentation) ✅
- Reddit image loading (HttpClient, graceful 403 fallback) ✅
- Nav bar spacing fix ✅
- Phase 5 testing: TodoMVC ⚠️, MDN ❌, Bootstrap 4 ✅, 4pda.to ✅
- Site compatibility hardening: CharsetDetector ✅, border-spacing ✅, CAPTION ✅, display:none ✅, overflow-x/y ✅, text-overflow:clip ✅
- E-book modes: Rich/Poor/Asceti ✅, JS toggle removed ✅, settings renamed ✅
- Markdown-to-HTML: MarkdownRenderer.cs ✅, .md URL detection ✅

#### v1.2 (next iteration)
1. **Performance tuning** — focus on Lumia 640 (1GB) perf, reduce memory
2. **SkiaSharp graph rendering** — прикольная тема, но лучше в v2.0

#### v2.0 (long-term)
1. **Dzen.ru OAuth2** — Yandex OAuth Authorization Code flow, token storage, login UI, authenticated API access

### Commands
```powershell
msbuild "Src\MediaExplorer\MediaExplorer.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:m
powershell -ExecutionPolicy Bypass -File "Src\MediaExplorer\DeployAndRun.ps1" -Platform x64
```
