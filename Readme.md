# MediaExplorer 0.57.100 - dev branch

![](/Images/logo.png)

## What Is This?

MediaExplorer is a hobby browser for Windows 10 Mobile (W10M, build 15063+) built **without** the system WebView or Chakra engine. It uses a custom HTML parser, CSS engine with selectors/cascade/flexbox, a JavaScript runtime powered by [NiL.JS](https://github.com/nilproject/NiL.JS), and a XAML-based renderer.

~35 files, ~20k+ lines of code. Pre-alpha. Museum-grade.

## Screenshots

![](/Images/sshot01.png)
![](/Images/sshot02.png)


## Features
- **Custom rendering engine** — HTML parser, CSS cascade, flexbox, XAML renderer
- **JavaScript** — NiL.JS runtime with ES Modules support (Vite bundles parse; D3v4/v5 support)
- **DevTools** — Console, DOM inspector, Network tab, Debug log
- **3 UI modes** — Hided (strip), Semi (expandable), Full (standard app bar)
- **3 render modes** — Full (JS+CSS), Rich (CSS, no JS), Poor (plain text)
- **Disk cache** — Resource caching with priority queue
- **MutationObserver** — Incremental re-render on DOM changes
- **Snapshot button** — Screenshot (single or full-page) saved to Pictures/MediaExplorer
- **Keyboard shortcuts** — Ctrl+L (focus URL), Ctrl+B (toggle bar)

## Status

- **Pre-alpha.**  All features are highly unfinished.
- **NiL.JS 2.6 Integration (netstandard2.0 → 1.4)** | **✅ DONE** (fully builds for W10M 15063)


## Dev section START

### Goal

Build and deploy MediaExplorer with working D3 graph rendering from the Nokia Design Archive.

### Constraints & Preferences

Must support automated test cycle: build → deploy → run → capture diagnostics → analyze.
All tests run on local x64 machine via VS 2026 Insiders (packages from this toolchain are broken on Phone Portal and on other machines with VS 2022).

x64 Debug VCLibs available; x86 Debug VCLibs missing on this machine.

Nokia Archive is a Vite/React SPA with obfuscated bundled JS; hard to locate graph data via Chrome inspection.

### Progress

Done

_host null crash fixed: JavaScriptEngine(IJsHost host) constructor now assigns _host = host (was missing – caused NullReferenceException in RequestRepaint() at startup).

DevToolsLogger rewritten: replaced KnownFolders.PicturesLibrary with ApplicationData.Current.LocalFolder\Logger.txt via UWP StorageFile/FileIO APIs; fallback to %TEMP%\MediaExplorerLogger.txt.

DeployAndRun.ps1 rewritten cleanly: no $argList null bug, finds .appx in AppPackages\ (not just bin\), uses exe path from unpacked folder (AUMID launch via shell:AppsFolder\ fails on this system).

Manifest dependencies restored: Microsoft.VCLibs.140.00.Debug + Microsoft.VCLibs.140.00.UWPDesktop added back. Registration succeeds for x64 Debug.

Version bumped: 0.55.30 → 0.57 everywhere.

Documentation updated: _AGENDS.md logger path (KnownFolders → LocalState), Doc/Summary_5_11.md with session notes.

App launches and runs: loads Nokia Archive HTML (6946 bytes), fetches & executes D3 v5, polyfills, main chunk (589 KB module), 
React/System.register module system works.

D3 patches applied: d3.select, d3.forceSimulation functional; D3 patch applied successfully.

Fetch & XHR interceptors installed: window.fetch and XMLHttpRequest stub created.

Rendering pipeline works: BuildVisualTreeAsync completes (102 CSS nodes, 73 render boxes, SVG elements rendered).

Injection code updated: brace-walker now handles backtick template literals (`...`) with ${...} interpolation. Injected code sets globalThis.__diagInjectRan=true and captures window.__graphData from If/Pf local variables.

Diagnostic tracing added: SafeEval("__diagLog(...)") calls and synchronous file writes to %TEMP%\MEDIADIAG.txt for injection code path confirmation.
Build succeeds: 0 errors, CS0618 warnings (pre-existing, safe to ignore).


In Progress

Verify chunk injection works: sysCalls.Count is logged to MEDIADIAG.txt after split, with file-write diagnostics before each SafeEval(call).
Check why [DIAG:INJECT] messages don't appear in the Logger.txt — DevToolsLogger.Log fire-and-forget async writes lose entries due to file-locking; the sync MEDIADIAG.txt trace will confirm if the injection loop is reached.

Blocked

Graph data not found ([DIAG:DATA] No graph data found in global scope). Data (If, Pf arrays) is inside System.register module local scope – not on window after execute() returns. Injection fix (template literal handling + __graphData capture) needs verification.

[DIAG:INJECT] diagnostics missing from Logger.txt – root cause is DevToolsLogger.WriteLogAsync fire-and-forget with file-lock contention, not necessarily that the injection code isn't reached.

Unknown whether sysCalls is empty – the 589 KB chunk may not contain System.register( if the bundler uses a different module wrapper format.
System.IO.FileLoadException: The file is in use during BuildVisualTreeAsync – non-fatal.
VS 2026 Insiders-built packages cannot be installed on Phone Portal (broken toolchain).

### Key Decisions

Template-literal fix instead of Symbol.toStringTag marker: The brace-walker now skips backtick template literals entirely (including ${...} expressions). This fixes the injection position detection without relying on Symbol.toStringTag which may not exist in all System.register output.

__diagInjectRan diagnostic now matches injected code: was checking globalThis.__diagInjectRan but injected code never set it (only called 
__diagLog). Now injection sets globalThis.__diagInjectRan=true.
Sync file write for diagnostic tracing: DevToolsLogger.Log async file writes lose entries due to locking. Switched to synchronous System.IO.File.AppendAllText to %TEMP%\MEDIADIAG.txt for reliable code-path confirmation.

__diagLog via SafeEval for JS-side diagnostics: uses the same logging pipeline as working [DIAG:DATA] messages, bypassing DevToolsLogger.Log C# issues.

x64 Debug as primary test platform (x86 VCLibs Debug missing).

Direct exe launch from unpacked folder (not AUMID) – shell:AppsFolder\ fails with Start-Process on this system.

### Next Steps

Deploy and run the latest build (with MEDIADIAG.txt sync diagnostics).

Read %TEMP%\MEDIADIAG.txt after run to confirm whether sysCalls is non-empty and injection loop is reached.

- If sysCalls is empty, investigate why System.register( is not found in the 589 KB chunk (check chunk prefix via SafeEval("__diagLog(...)")).
- If sysCalls is non-empty, verify primary/failback injection succeeded and window.__graphData is populated.
- If still no graph data, use Chrome DevTools on Nokia Archive to confirm where data lives (inline JSON, fetch/XHR payload, or embedded in JS bundle).
Once D3 graph renders, move to Phase Z (broader site compatibility testing).

### Critical Context

Brace-walker template literal bug: The original injection brace-walker handled '...' and "..." strings but NOT backtick template literals. Minified JS commonly uses template literals with ${...} containing braces that throw off the depth counter.
injected variable scope: declared inside the try block at ~line 3637, NOT accessible after the catch at line 3714. Diagnostic at line 3715 removed reference to injected.
DevToolsLogger.Log reliability: uses var _ = WriteLogAsync(message) – fire-and-forget async. Multiple concurrent calls cause FileLoadException locking. Synchronous System.IO.File.AppendAllText to MEDIADIAG.txt used for reliable diagnostics.
__diagLog via SafeEval works: confirmed by [DIAG:DATA] plot=found children=0 (line 3829) which uses the same pattern.
Current build: 0 errors, CS0618 warnings (deprecated JSValue.Marshal, safe to ignore).
Chunk format: assumed to be System.register([],(function(e,t){"use strict";return{execute:function(){...}})) but NOT confirmed — the MediaExplorer searching for System.register( at line 3519 may fail if the bundler uses a different wrapper.
Key open question: is the 589 KB chunk actually wrapped in System.register( or a different call? The split code (lines 3515-3607) only looks for System.register(, and sysCalls may be empty.

### Relevant Files
- Src/MediaExplorer/Engine/JavaScriptEngine.cs – chunk injection at ~lines 3625-3730, template-literal handling, __diagInjectRan fix, MEDIADIAG.txt diagnostics. _host = host fix at constructor line 2149.
- Src/MediaExplorer/Engine/DevToolsLogger.cs – rewritten with async file writes; known fire-and-forget lock issue.
- Src/MediaExplorer/DeployAndRun.ps1 – rewritten deploy/run/log script.
- Src/MediaExplorer/Package.appxmanifest – version 0.57.0.0, VCLibs + UWPDesktop dependencies.
- Src/MediaExplorer/MainPage.xaml.cs – startup diag logging.
- Doc/Summary_5_11.md – updated with _host fix and session notes.

## Dev section END


This is a homemade browser engine — not production-ready, not intended to replace Edge or Chrome. It exists to prove that you don't need Chromium to render a webpage.

## Milestones

- **2026.06.07 — v0.55.0** D3.js force-directed graph (Nokia Design Archive) renders as live XAML shapes — no Skia, no SvgImageSource, no WebView. SVG elements (circle, line, rect, path, text, g) map to native UWP UIElement descendants (Ellipse, Line, Rectangle, Path, TextBlock, Canvas) in the VirtualizingRenderer pipeline with style cascading (fill, stroke, stroke-width, opacity) and viewBox scaling.
- **2026.06.05 — v0.50.0** First successful d3.js evaluation on UWP via NiL.JS: force simulation initialises, tick function fires, DOM SVG nodes created. Known limitation at this point: D3 DOM output not yet rendered to screen (solved in v0.55).
- **2026.05.xx — v0.42.8** NiL.JS runtime ported from netstandard2.0 to .NET Native 1.4 (W10M 15063-compatible). ES Modules parsing support added. MutationObserver implemented.

## Known Issues

- Source is AI-generated ("neuro-slop"), except the original UDAIE-A WebView code
- Not tested on any W10M device
- White screen on some sites (dzen.ru, ya.ru)
- ES Modules runtime errors still being fixed
- Private fields (`#name`) not supported

## Credits

- [UDAIE-A/WEBVIEW](https://github.com/UDAIE-A/WEBVIEW) — Original WebView for Windows Phone 8.1
- [NiL.JS](https://github.com/nilproject/NiL.JS) — JavaScript engine

## Docs

See `/Doc` folder for development plans and session summaries.

## Contributing

**Calling all retro-computing enthusiasts!** If you still have a Lumia 950/1020 gathering dust, or you just love the idea of a browser that doesn't need 2 GB of Chromium to open a webpage — this project needs you.

- **Developers:** Fork, fix, PR. The codebase is messy but honest. Every line was fought for.
- **W10M testers:** Try it on your device, report what breaks. Your hardware is the real test bench.
- **CSS/JS nerds:** If you know why `calc(100% - 16px)` doesn't work here — you already know what to do.

## Issues & Bug Reports

Found a site that renders wrong? Crashed on your phone? Have a feature request?

→ **[Open an Issue](https://github.com/mediaexplorer74/MediaExplorer/Issues)**

Include: URL, what you expected, what you got. Screenshots help.

---

As is. No support. RnD only. DIY.

[m][e] June 11, 2026

![](/Images/footer.png)
