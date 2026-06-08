# Summary 5.09 — XMLHttpRequest stub работает; DevTools Console жив

**Date:** 2026-06-08  
**Session focus:** XHR stub implementation, DevTools Console routing fix, log analysis

## Что сделано

### 1. XMLHttpRequest — fetch-based заглушка (JavaScriptEngine.cs:4793)
- Если `XMLHttpRequest` не существует в NiL.JS, он **создаётся с нуля** через `window.fetch`
- `open(method, url)` → сохраняет параметры, логгирует `[XHR] open ...`
- `setRequestHeader(name, value)` → сохраняет заголовки
- `send(body)` → вызывает `fetch(url, {method, headers, body})`, заполняет `responseText`, `status`, `readyState=4`, вызывает `onload` + `onreadystatechange`
- `abort()`, `overrideMimeType()`, `getResponseHeader()` — заглушки
- Поверх конструктора надет логгирующий wrapper с единым форматом `[XHR] open/send/response`

### 2. HostConsole → DevToolsLogger (JavaScriptEngine.cs:2815)
- `HostConsole.log/warn/error/info` теперь форвардят сообщения в `DevToolsLogger.Log()`
- `console.log(...)` из JavaScript (включая `[XHR]` сообщения) появляются на вкладке **Console** в DevTools

### 3. G.1 thread crash — частично пофикшен, остались проблемы
- `BuildSvgImageAsync` обёрнут в `TaskCompletionSource` + `dispatcher.RunAsync`
- **НО:** dispatch не работает из-за `async void` с `Dispatcher.RunAsync` (делегат возвращает void на первом await, RunAsync завершается раньше async работы)
- **Аргумент Exception:** первый SVG (251 chars, search-icon) падает с `ArgumentException` из-за дублированного `xmlns`
- **Thread crash:** второй SVG (55 chars, timeline) всё ещё падает с `RPC_E_WRONG_THREAD`

## Анализ логов

### ✅ Что работает
| Сигнал | Статус |
|--------|--------|
| `[XHR] No native XMLHttpRequest — creating fetch-based stub` | XHR stub создан |
| `[DIAG] XHR interceptor installed` | Wrapper надет |
| `[DIAG] D3 patch applied successfully` | D3 patch работает |
| `[DIAG] D3 circle appended` + UpdateView | Manual D3 circle рендерится |
| DevTools Console показывает все `[DIAG]` сообщения | Console tab жив |
| DevTools DOM показывает `<svg id="timeline"/>` | DOM tab работает |
| DevTools Network показывает ResourceManager запросы | Network tab работает |
| DevTools Debug показывает фильтрованные `[DIAG]` | Debug tab работает |

### ❌ Что не работает
| Сигнал | Проблема |
|--------|----------|
| Нет `[XHR] open` сообщений | D3/приложение **не вызывает** XMLHttpRequest — данные загружаются иначе |
| `[DIAG:JS] timeline SVG=no_svg` | main module проверяет наличие SVG в DOM — его нет (Phase 4 ещё не создал) |
| `[DIAG:JS] execute return=undefined` | execute() выполнился, но без эффекта |
| `<svg id="timeline"/>` children=0 | D3 не заполняет timeline — код graph creation пропущен |
| `<div id="plot">` пустой | Основной граф не создан |
| G.1 crash (RPC_E_WRONG_THREAD) | Dispatcher marshalling не работает из-за async void |
| Первый SVG (ArgumentException) | Дублированный `xmlns` в `SerializeSvgNode` |

### Ключевое открытие
**Ни XHR, ни fetch не используются для загрузки данных графа.** Данные, вероятно, **встроены прямо в 589KB SystemJS-чанк** (Vite-сборка). Приложение — монолитный SPA-бандл, где data лежит inline. Проблема в том, что:
1. `execute()` запускается в Phase 3 — DOM-контейнеры (`#timeline`, `#plot`) ещё не существуют
2. Код проверяет их наличие и пропускает создание графа
3. При повторном execute (на delay checks) код видит какой-то флаг "already initialized" и снова ничего не делает

## Следующие шаги
1. **Починить G.1 thread crash** — заменить async void на правильный dispatcher pattern
2. **Исследовать 589KB чанк** — найти, где встроены данные (nodes/links) и извлечь их
3. **Либо:** создать DOM-контейнеры (`#plot`) ДО Phase 3 JS, чтобы код graph creation выполнился
4. **Либо:** перехватить данные из чанка и скормить их D3 вручную

## Файлы изменены
- `Src/MediaExplorer/Engine/JavaScriptEngine.cs` — `InterceptXhr()` (fetch-based XHR stub), `HostConsole` (DevToolsLogger routing)
- `Src/MediaExplorer/Engine/Core/VirtualizingRenderer.cs` — `BuildSvgImageAsync` (dispatch fix, недоделан)

## Build status
✅ **0 errors** via VS 2026 Insiders MSBuild (x64 Debug, 2m53s)

---
*Next session: 5.10 — Fix G.1 thread crash properly + investigate embedded data in SystemJS chunk*
