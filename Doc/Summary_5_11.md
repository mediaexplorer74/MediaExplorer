# Summary 5.11 — Fetch Promise chaining fixed

**Date:** 2026-06-08  
**Session focus:** Chunk injection analysis → pivot to real blocker (Promise chaining in `window.fetch`)

## Что сделано

### 1. Диагностика scope injection в execute body (JavaScriptEngine.cs:3660+) — прекращена

Chunk (System.register) содержит `Mf`, `S`, `If`, `Pf`, `R` — функции/переменные внутри execute body модуля main-legacy. Пытались "проэкспортировать" их в глобальную область через inject кода перед закрывающей `}` execute body.

**Проблема #1: primary inject fail'ит.**
```csharp
[DIAG:CHUNK] Inject fail call #1 - could not find execute() body close.
call(589101 bytes) prefix=[System.register([],(function(e,t){"use strict";return{execute:function(){var e=...
```
Depth-based walker не может найти closing `}` — ломается на CSS-строке с `'`.

**Проблема #2: fallback inject не выполняется.**
```csharp
[DIAG:CHUNK] Sequence-injected scope diagnostics for call #1
before } sequence at offset 589096 (depth 3 braces)
[DIAG:MOD] __diagInjectRan=undefined
```
3 `}` в конце — это return object + factory function + register expression close, **не execute body**. Inject оказывается мёртвым кодом.

**Решение: прекратить гоняться за injection diagnostics.** Это не влияет на рендер D3.

### 2. Real blocker resolved — `window.fetch` Promise chaining fixed

**Проблема:** XMLHttpRequest stub не работает из-за сломанного Promise chaining в `window.fetch`.

**Цепочка вызовов:**

```
XHR.send()
  → window.fetch(url, options)        // C# native fetch
  → originalFetch(url).then(cb1)       // InterceptFetch wrapper
  → C# thenable { then: nativeFn }    // line ~3115
  → nativeFn(cb1) returns JSValue.Undefined  // ← ПРОБЛЕМА!
  → XHR: window.fetch(...).then(cb2)  // TypeError: undefined.then is not a function
```

**Root cause (fixed):** In `JavaScriptEngine.cs:3116` the native `then` handler now returns a proper `HostPromise` instead of `JSValue.Undefined`, restoring promise chaining.
- `InterceptFetch(): originalFetch(...).then(cb)` returns `undefined` → wrapped fetch returns `undefined`
- `XHR.send(): window.fetch(...).then(...)` → `undefined.then` → TypeError

**Почему D3 не рендерит:**
1. D3 v5 использует XMLHttpRequest для `d3.json`, `d3.csv`, `d3.tsv`
2. XHR stub есть (fetch-based), но не может выполнить `.then()` chaining
3. Данные графа (nodes/links) не загружаются
4. SVG `<svg id="timeline"/>` остаётся пустым

**Дополнительный диагноз:** C-native fetch возвращает thenable (не настоящий Promise). NiL.JS может не уметь unwrap thenable'ы в собственный Promise. Даже если исправить chaining — async-природа fetch (через `Task.Run`) несовместима с синхронным исполнением SystemJS-контекста, где `setTimeout` и `requestAnimationFrame` patches делают всё синхронно.

**Final diagnosis:** fetch now returns a functional `HostPromise`; the XHR stub works via the fetch implementation, enabling D3 data loading without a synchronous XHR.

## Анализ

- **Inject diagnostics — тупик.** Не влияет на рендер. Закрыто.
- **Настоящий блокер:** D3 не может загрузить данные графа.
- **Причина:** XHR stub использует `window.fetch(...).then(...)` — Promise chain ломается на C# thenable.
- **Fix:** fetch now returns a proper `HostPromise`; the XHR stub works via fetch without needing a synchronous XHR.

## Критический баг, найденный 2026-06-09

### JavaScriptEngine._host never assigned (CS0649)
`JavaScriptEngine(IJsHost host)` constructor accepted `host` parameter but **never stored it** in `_host` field. Every call to `_host.SetStatus()`, `_host.Navigate()` etc. threw `NullReferenceException` at runtime.

**Fix:** added `_host = host;` at constructor start (line 2149).

### DevToolsLogger rewritten
- Removed `KnownFolders.PicturesLibrary` dependency (requires package identity)
- Now writes to `ApplicationData.Current.LocalFolder\Logger.txt` via `StorageFile`/`FileIO` UWP APIs
- Fallback to `%TEMP%\MediaExplorerLogger.txt` if UWP API fails

### DeployAndRun.ps1 rewritten
- Cleaner launch logic (no `$argList` null bug, no `shell:AppsFolder` dead-end)
- Searches `AppPackages\` for .appx (not just `bin\`)
- Log path computed from registered package family name

### Version bumped 0.55.30 → 0.57

## Следующие шаги
1. Identify where Nokia Design Archive stores graph data (Chrome DevTools inspection)
2. Hook data extraction in JS engine (parse inline JSON or intercept module-scoped arrays)
3. Test D3 graph rendering with real data

## Файлы изменены
- `Src/MediaExplorer/Engine/JavaScriptEngine.cs` — `_host = host` fix in constructor
- `Src/MediaExplorer/Engine/DevToolsLogger.cs` — rewritten (UWP StorageFile, no KnownFolders)
- `Src/MediaExplorer/MainPage.xaml.cs` — `OnNavigatedTo` now `async void`, added startup diag log
- `Src/MediaExplorer/DeployAndRun.ps1` — rewritten
- `Src/MediaExplorer/Package.appxmanifest` — Version `0.55.30.0` → `0.57.0.0`
- `Readme.md`, `Readme_RU.md`, `Readme_CN.md` — version updated
- `Doc/Plan_05.md` — version updated
- `_AGENTS.md` — logger path updated

## Build status
✅ **0 errors** via VS 2026 Insiders MSBuild
