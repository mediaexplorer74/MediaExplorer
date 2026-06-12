# MediaExplorer — AI Context

## Automation Loop (June 12 — Fully Automated)

```powershell
# Step 1 — Build
msbuild "Src\MediaExplorer\MediaExplorer.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:m

# Step 2 — Deploy + launch
powershell -ExecutionPolicy Bypass -File "Src\MediaExplorer\DeployAndRun.ps1" -Platform x64

# Step 3 — Wait 120s for app to render, then auto-close
Start-Sleep -Seconds 120
Get-Process -Name "MediaExplorer" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Host "MediaExplorer closed after 120s wait"

# Step 4 — Read diagnostics log
Get-Content "$env:LOCALAPPDATA\Packages\MediaExplorerV0p57_5gyrq6psz227t\LocalState\Logger.txt" -Tail 80
```

**Loop**: Build → Deploy+Launch → wait 120s → Kill → Read log → Analyze → Fix → Rebuild → repeat.

**One-liner** (AI executes all steps in sequence):
```powershell
msbuild "Src\MediaExplorer\MediaExplorer.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:m; if ($?) { powershell -ExecutionPolicy Bypass -File "Src\MediaExplorer\DeployAndRun.ps1" -Platform x64; Start-Sleep -Seconds 120; Get-Process -Name "MediaExplorer" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; Get-Content "$env:LOCALAPPDATA\Packages\MediaExplorerV0p57_5gyrq6psz227t\LocalState\Logger.txt" -Tail 80 }
```

## Current State (June 12, 2026)

**Version**: 0.57.100.0, AUMID `MediaExplorerV0p57!App`, PackageFamilyName `MediaExplorerV0p57_5gyrq6psz227t`

### Done

#### Session 1 (June 11–12 morning) — Core rendering & overlay fixes
- **Data extraction** from 589KB JS chunk via brace-counting: extracts `Kf`, `wf`, `Cf`, `vf` → `__graphData` (755 nodes, 1647 links)
- **Graph rendering** via circular layout (O(n) cos/sin) → SVG XML → `#plot.innerHTML` → XAML Ellipse/Line
- **Timeline rendering** via compact greedy-packing layout → SVG XML → `#timeline.innerHTML` → XAML Line/Circle
  - 230 stories packed into 140 rows at 3px/row, total SVG height 490px (31KB)
  - Year axis with tick marks, story bars (teal), secondary date ranges (red), entry scatter at bottom
- **Route detection** — log-only (hash set + nav link click disabled to prevent re-render cycle)
- **No second render cycle** — app runs single pass, clean exit
- **DeployAndRun.ps1** — `-Build` flag, `-NoLaunch` flag, platform support
- **Pure circular layout** — stable, no NaN nodes, all 755 nodes valid
- **SVG coordinate rounding** (1dp) reduces XML size ~40%
- **Timeline height explosion**: 140 rows × 3px (was 16px/row); labels removed → height 490px vs 2340px
- **Double render loop**: removed `window.location.hash = '#/network'` and `navLink.click()` from route detection
- **"Inception" visual bug (nested windows)**: disabled redundant `TriggerDelayedSvgExtractionAsync` and `TriggerDelayedSvgRefresh` in `CustomHtmlEngine.cs`
- **Overlay race — overrideStartup:true for "Loaded."**
- **NavigationCacheMode=Required** on MainPage
- **RunNilJsStartupTest checks _welcomeShown**
- **OnNavigatedTo guard with _welcomeShown**
- **_navigationComplete guard in LoadingChanged handler**
- **Dark background rect added to graph SVG** (`<rect fill="#1a1a2e"/>`)
- **Timeline rendering DISABLED** (try-catch skip) for isolating Network rendering issues

#### Session 2 (June 12 afternoon) — CacheMode fix, node limiting, label contrast
- **CacheMode="{x:Null}"** on ScrollViewer (MainPage.xaml:172) — removes BitmapCache that caused "mirror world" / garbage from other windows when app was covered/uncovered
- **Graph node limiting** (first 200 nodes) — `_maxNodes` + `_limitedIds` set filter links + circles in JavaScriptEngine.cs for debug
- **Checkbox/Radio label contrast** — `Foreground = #CCC` on all CheckBox & RadioButton controls in DomBasicRenderer.cs (both standalone and form-group variants)
- **Fixed XamlParseException crash** — `CacheMode="None"` is invalid in UWP; replaced with `CacheMode="{x:Null}"`

### Diagnostics Architecture
- `DevToolsLogger.Log(msg)` → `%LOCALAPPDATA%\Packages\...\LocalState\Logger.txt`
- `Debug.WriteLine(msg)` → VS Output only
- `__diagLog(msg)` → C# callback, writes both
- Key prefixes: `[DIAG:TIMELINE-SVG]`, `[DIAG:SVG]`, `[DIAG:DATA]`, `[DIAG:ROUTE]`, `[DIAG:SYS]`, `[DIAG:REPAINT]`

### Files
- `Src/MediaExplorer/Engine/JavaScriptEngine.cs` — data extraction ~line 3515, graph SVG ~3560, timeline SVG ~3636, route detection ~line 4092, SafeEval ~line 4364
- `Src/MediaExplorer/Engine/Core/VirtualizingRenderer.cs` — SVG→XAML mapping (~line 994)
- `Src/MediaExplorer/Engine/DomBasicRenderer.cs` — checkbox/radio render (~line 3520, 3931, 3942)
- `Src/MediaExplorer/Engine/CustomHtmlEngine.cs` — ScheduleRepaintFromJs (~line 1138), DispatchRepaintAsync (~line 919)
- `Src/MediaExplorer/MainPage.xaml` — ScrollViewer CacheMode (line 172)
- `Src/MediaExplorer/MainPage.xaml.cs` — Engine_RepaintReady (~line 694)
- `Src/MediaExplorer/Engine/DevToolsLogger.cs` — file logger
- `Src/MediaExplorer/DeployAndRun.ps1` — deploy script
- `Src/NilJsTest/tests/main-legacy-CgkFIb-k.js` — archived chunk (589KB)

### RnD Plan (Next Steps)

#### Phase 1 — Stabilise rendering (current)
1. **Fix circle duplication on scroll** — add guard in `ScheduleRepaintFromJs` (`CustomHtmlEngine.cs:1138`) to not queue `RequestRender()` during active scroll; or deduplicate elements in `VirtualizingRenderer.BuildVisualTree` before appending to Canvas.
2. **CacheMode={x:Null}** — ✅ DONE (MainPage.xaml:172)
3. **Checkbox label contrast** — ✅ DONE (DomBasicRenderer.cs — Foreground=#CCC on CheckBox/RadioButton)
4. **Reduce element count** — ✅ DONE (200-node limit in JavaScriptEngine.cs graph render)

#### Phase 2 — Smartphone adaptivity
5. **Viewport-fragmented rendering** — detect narrow viewport (< 600px) and render a "magic slider" (one content card at a time, swipe left/right) instead of full 755-node graph. Research: use `ManipulationMode="TranslateX"` on ContentArea (already set at MainPage.xaml:171) for swipe detection; implement card stack with prev/next navigation.
6. **Auto-detect smartphone** — check `Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily` or `ApplicationView.GetForCurrentView().VisibleBounds.Width < 600`.

#### Phase 3 — Interactivity
7. **Node tap handler** — circles already have `data-id/name/type` attributes + `Tapped` event in VirtualizingRenderer.cs (~line 1064). Wire to `ContentDialog` showing node name + type.
8. **Timeline re-enable** — restore timeline SVG rendering after graph issues resolved.

### Commands
```powershell
msbuild "Src\MediaExplorer\MediaExplorer.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:m
powershell -ExecutionPolicy Bypass -File "Src\MediaExplorer\DeployAndRun.ps1" -Platform x64
```
