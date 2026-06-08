# Summary 5.10 — Execute tracing, route simulation, deeper chunk scan

**Date:** 2026-06-08  
**Session focus:** SystemJS module execute tracing, route/navigation simulation, enhanced chunk data scanning

## Что сделано

### 1. System.register execute wrapped with [DIAG:MOD] tracing (JavaScriptEngine.cs:3487)
- `__sys.register` теперь оборачивает `declare` callback: перехватывает возвращаемый `{ execute }` 
- Каждый `execute()` логируется как `[DIAG:MOD] execute START/END/FAIL url=mod:N`
- Если execute бросает исключение, ловится и пишется `[DIAG:MOD] execute FAIL` с сообщением ошибки
- Позволяет точно определить, какой модуль выполняется, сколько раз, и есть ли ошибки

### 2. Route detection + navigation simulation после force-exec (JavaScriptEngine.cs:3719)
- Логирует `[DIAG:ROUTE] location=... hash=... path=...`
- Ищет `<a>` ссылки с текстом/href/class содержащими "network", "timeline", "graph"
- Устанавливает `window.location.hash = '#/network'`
- Пытается кликнуть найденную навигационную ссылку
- Всё обёрнуто в try-catch, не блокирует выполнение

### 3. Route/navigation simulation в TriggerDelayedSvgRefresh (CustomHtmlEngine.cs:2048)
- На каждом delay check (3/4/5 сек) выполняет навигационную симуляцию:
  - Логирует `[DIAG:NAV] location href=... hash=...`
  - Ищет и кликает graph-ссылки
  - Диспатчит MouseEvent click
  - Устанавливает hash на `#/network`
  - Пытается вручную вызвать `hashchange` listeners (через `document._events`)
- После навигации **повторно выполняет** `execute()` модуля

### 4. Улучшен SearchChunkForGraphData (JavaScriptEngine.cs:5002)
- Дампит **500 chars после** `'nodes'` для полного контекста D3 force simulation
- Дампит **300 chars до** `'nodes'` чтобы увидеть, откуда берутся `n` и `t`
- Дампит **300 chars вокруг** `'links'`
- Ищет **`source:` / `"source":` / `target:` / `"target":`** паттерны (link data)
- Дампит **первые 1000 chars** чанка для понимания структуры модуля
- Дампит **середину чанка** (~196KB) если >200KB
- Считает количество `:{` вхождений (уровень вложенности объектов)

## Анализ

### Ключевое открытие сессии
`nodes` на offset 576552 — это **D3 force simulation setup code**, не данные:
```
e.nodes(t),l.links(n.map((e=>({source:e.source.id,target:e.target.id})))),A({alphaTarget:.3})
```
- `e` = simulation object
- `t` = nodes data (переменная из замыкания)
- `n` = links data (из замыкания)
- Данные (`n`, `t`) не статический JSON — они загружаются динамически или присваиваются в другом месте

### `[d3-err:object]` (JavaScriptEngine.cs:5901)
- Появляется дважды при `SafeEvalFast` внешних скриптов
- D3 v5 (248KB) или какой-то другой внешний скрипт бросает исключение без `.message`
- Исключение ловится try-catch, скрипт продолжает работу (частично)
- Не фатально — `d3.select` и `d3.forceSimulation` всё равно доступны

### Гипотеза: graph route-gated
- `execute()` возвращает `undefined` — код установки приложения выполняется
- Но граф не создаётся, потому что SPA routing не активирован
- Приложение начинает на welcome-странице, граф — на странице `/network` или `/timeline`
- Новая навигационная симуляция должна активировать правильный route

## Следующие шаги
1. **Deploy + collect log** — запустить приложение с новыми диагностиками, собрать Output window
2. **Анализ [DIAG:MOD]** — посмотреть, что пишет execute tracing (сколько модулей, какие ошибки)
3. **Анализ [DIAG:ROUTE]** — посмотреть, находит ли навигационные ссылки и срабатывает ли hash change
4. **Если route-активация сработала** — должен появиться граф
5. **Если нет** — нужно исследовать, как именно приложение загружает данные (возможно fetch/XHR с URL, не перехваченным нами)

## Файлы изменены
- `Src/MediaExplorer/Engine/JavaScriptEngine.cs` — execute wrapping, route detection, Enhanced SearchChunkForGraphData
- `Src/MediaExplorer/Engine/CustomHtmlEngine.cs` — navigation simulation in TriggerDelayedSvgRefresh

## Build status
✅ **0 errors** via VS 2026 Insiders MSBuild (x64 Debug, 1m30s)

---
*Next session: 5.11 — Deploy and analyze new diagnostics; if route simulation works, D3 graph should appear*
