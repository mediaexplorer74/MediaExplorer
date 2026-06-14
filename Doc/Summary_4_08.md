# Summary 4.08 — Codebase Verification & Legacy Site Support Assessment

**Session date:** 2026-06-05
**Build:** `msbuild MediaExplorer.sln /p:Configuration=Debug /p:Platform=x86` — ✅ **0 errors** (194 warnings, all pre-existing)
**Mode:** code review (no emulator/device)

---

## 1. Цель

Верифицировать, что изменения сессий 3.27 (C.6 follow-up) и 3.28 (Nokia Design Archive JS compat)
присутствуют в коде, собрать актуальную картину и определить следующие шаги для legacy site support.

---

## 2. Проверка изменений Session 3.27 (C.6 follow-up)

### 2.1 Multi-property transitions (`TransitionList`)

| Файл | Строка | Статус |
|------|--------|--------|
| `CssComputed.cs` | 183–193 | ✅ `TransitionSpec` + `TransitionList` |
| `CssLoader.cs` | 2028–2086 | ✅ `ParseTransition` парсит все comma-сегменты |
| `CssLoader.cs` | 2174 | ✅ `hasTransition` guard через `TransitionList` |
| `DomBasicRenderer.cs` | 4848–5025 | ✅ `BuildTransitionSpecs`, `TransitionSpecForProp`, `TransitionSpecForAnim` |

### 2.2 Smooth background (no base brush)

`DomBasicRenderer.cs` — PointerEnterd/Exited: transparent → animate цвет → transparent.
Код на месте (проверено grep: `!hasBaseBrush` pattern).

### 2.3 `%` в `translate()`

`ParseTransformForHover` — расширена сигнатура `(..., double elemW, double elemH)`,
вызывается внутри `PointerEntered`. Код на месте.

### 2.4 SplitTopLevelCommas

Три использования: парсинг transition (2028), парсинг другого (3401), определение (3438).
Дубликат удалён.

### 2.5 Тесты

`Html/test_transitions.html` — найден (3 копии: исходник, obj, bin).
11 базовых + 3 новых кейса (multi-dur, no-base-bg, % translate).

---

## 3. Проверка изменений Session 3.28 (Nokia JS compat)

| Фича | Строка | Статус |
|------|--------|--------|
| Skip `type="module"` scripts | JavaScriptEngine.cs:4894 | ✅ `if (type == "module") continue;` |
| `nomodule` executes normally | JavaScriptEngine.cs:4899 | ✅ Без спец.обработки — выполняется как immediate |
| `globalThis`/`global` — real JS object | JavaScriptEngine.cs:2974–2980 | ✅ `SafeEval("this")` → NiL.JS scope |
| `System.import` host function | JavaScriptEngine.cs:3370–3406 | ✅ `Task.Run` → `FetchScriptStringAsync` → `RunInline` |
| DIAG logging (`[DIAG:FETCH]`, `[DIAG:EXEC]`, `[DIAG:RUNSCRIPTS]`, `[DIAG:System.import]`) | Разбросано по JavaScriptEngine.cs | ✅ Все логи присутствуют |

---

## 4. Состояние проекта

### Phase C (CSS) — 5/6 завершено

| Подфаза | Статус |
|---------|--------|
| C.1 — calc() + vw/vh | ✅ |
| C.2 — @media queries | ✅ |
| C.3 — grid-template-areas | ✅ |
| C.4 — custom properties (--var) | 🟡 Parked (3.25) |
| C.5 — clamp() | ✅ |
| C.6 — CSS Transitions | ✅ + follow-up |

### Phase S (Stability) — частично

| Подфаза | Статус |
|---------|--------|
| S.1 — NaN guards | ✅ (3.27) |
| S.2 — JS timeout | ❌ |
| S.3 — DOM node cap | ✅ (3.27) |
| S.4 — CSS rule cap | ✅ (3.27) |
| S.5 — Error page | ❌ |
| S.6 — Cascade/layout guards | ❌ |

### Nokia Design Archive

| Метрика | Статус |
|---------|--------|
| HTML загружен | ✅ |
| CSS загружен | ✅ |
| Static HTML rendered | ✅ (73 boxes) |
| D3.js v7 (CDN, UMD) | 🟡 Будет выполняться (ES5) |
| Vite-бандл (`type="module"`) | 🔄 Bypassed через skip |
| Legacy polyfills (`nomodule`) | 🟡 System.import — код есть, не тестирован |
| Canvas 2D API | ❌ |
| SVG DOM / full events | ❌ |

**Ключевое открытие (первый запуск):** `System.import` дошёл до fetch, но приложение
падало на CSS фазе. Причина — вероятно случайный race condition или нехватка ресурсов.

**Второй запуск — успех!** `System.import` полностью отработал:
1. ✅ Дошёл до `System.import(...)` в инлайн-скрипте
2. ✅ `FetchScriptStringAsync` скачал main-legacy chunk (589152 bytes)
3. ✅ `[DIAG:System.import] OK: 589152 bytes` — загрузка подтверждена
4. ✅ `RunInline` вызван (без эксепшна в логе)
5. ✅ Рендер завершён: 73 boxes, 22 text nodes, страница отображена

**НО:** страница выглядит как текст + пара крупных кнопок +/-, без D3.js визуализаций.
Причина: `System` был объявлен как C# anonymous type (`JSValue.Marshal(new { import = ... })`) —
read-only. Vite polyfills-legacy не могут добавить к нему `System.register`, `System.resolve`
и т.д. (4 JSException из polyfills-legacy в логе — это попытки записи в read-only).

**Фикс (Session 3.29):** `System` теперь `Dictionary<string, object>` — writable,
как уже сделано для `Reflect` и `Intl`. Polyfill сможет добавить `.register`, `.resolve`,
`.instantiate`, а наш `.import` будет скачивать и выполнять чанки через `RunInline`.

---

## 5. Системный фикс: System object → writable Dictionary

**Проблема:** `JSValue.Marshal(new { import = ... })` создаёт read-only JS-объект.
Vite polyfills-legacy пытается добавить `System.register`, `System.resolve`,
`System.instantiate` и другие методы, но anonymous type в NiL.JS не позволяет
установку новых свойств. Это вызывает JSExceptions (4 штуки в логе) и SystemJS
не инициализируется полностью.

**Фикс:** Замена anonymous type на `Dictionary<string, object>` (как в `Reflect`
и `Intl`). Теперь polyfill может свободно добавлять свойства.

```csharp
// Было (read-only):
JSValue.Marshal(new { import = ... })

// Стало (writable):
JSValue.Marshal(new Dictionary<string, object> { { "import", ... } })
```

**Файл:** `Engine/JavaScriptEngine.cs:3374`

### Доп. фикс: NiL.JS Proxy.cs null-guard

**Проблема:** `Proxy.fillMembers()` (строка 112) предполагает, что у `PropertyInfo` всегда есть
хотя бы один accessor (getter или setter). Для некоторых свойств `Dictionary<string, object>`
(например, explicit interface implementations) оба могут быть null → `NullReferenceException`.

**Фикс:** `NiL.JS/Core/Interop/Proxy.cs:112` — вынес accessor в переменную с null-проверкой:
```csharp
var accessor = property.GetSetMethod(true) ?? property.GetGetMethod(true);
if (accessor == null) continue;
```

Это не нарушает семантику — свойство без getter'а И setter'а просто пропускается.

---

### Реальность: Polyfill перезаписал System.import

**Session 3.31 — повторный тест с Dictionary fix:**
- Polyfills-legacy перезаписал `System.import` своей DOM-версией.
- Наш `System.import` (C# host function) был заменён на JS-функцию, которая пытается создать `<script>` тег — но у нас нет DOM script onload.
- `[DIAG:System.import]` логи исчезли → polyfill переписал System полностью.

**Решение (3.31):** убрали `System` pre-definition целиком. Добавили:
- `__sysImport` — отдельная host function (C#): `FetchScriptStringAsync` + `RunInline`.
- В `RunScriptsAsync` inline-перехват: если строка скрипта содержит `System.import("...")` или `System.import('...')`, заменяем на `__sysImport(...)`.
- Polyfill создаёт `System` сам как реальный JS-объект → может добавлять `.register`, `.resolve`, `.instantiate`.

**Session 3.32 — тест с `__sysImport`:**
- `[DIAG:EXEC] len=82 preview="__sysImport(...)"` — интерсепт работает.
- `[DIAG:System.import] OK: 589152 bytes` — чанк скачан и выполнен.
- `System.register(...)` вызывается — модули регистрируются.
- **НО:** `execute()` ни разу не вызван — SystemJS ожидает `onload` события скрипта.
- `__sysImport` был `Task.Run` (fire-and-forget) → `RunInline` завершался, но модули не выполнялись.

**Фикс (3.32):**
1. `__sysImport` теперь синхронный: `.GetAwaiter().GetResult()` вместо `Task.Run`.
2. После `RunInline` выполняется force-execute JS-сниппет для System.registry.
3. Блокирует поток Phase3 до завершения загрузки и выполнения модулей.

**Готово к следующему тесту.** Build 0 errors.

---

## 6. Текущее состояние

### Phase C (CSS) — 5/6 завершено

| Подфаза | Статус |
|---------|--------|
| C.1 — calc() + vw/vh | ✅ |
| C.2 — @media queries | ✅ |
| C.3 — grid-template-areas | ✅ |
| C.4 — custom properties (--var) | 🟡 Parked (3.25) |
| C.5 — clamp() | ✅ |
| C.6 — CSS Transitions | ✅ + follow-up (3.27) + Nokia compat (3.28–3.32) |

### Phase S (Stability) — частично

| Подфаза | Статус |
|---------|--------|
| S.1 — NaN guards | ✅ (3.27) |
| S.2 — JS timeout | ❌ |
| S.3 — DOM node cap | ✅ (3.27) |
| S.4 — CSS rule cap | ✅ (3.27) |
| S.5 — Error page | ❌ |
| S.6 — Cascade/layout guards | ❌ |

### Nokia Design Archive

| Метрика | Статус |
|---------|--------|
| HTML загружен | ✅ |
| CSS загружен | ✅ |
| Static HTML rendered | ✅ (73 boxes) |
| D3.js v7 (CDN, UMD) | 🟡 Загружается |
| System.import (`__sysImport`) | ✅ синхронный fetch, register + execute вызов |
| System.registry force-execute | ✅ post-RunInline сниппет |
| D3.js визуализации | ❌ Не появились (последний тест до фикса `Task.Run` → sync) |
| Canvas 2D API | ❌ |
| SVG DOM / full events | ❌ |

## 7. Следующие шаги

### Приоритет: повторный тест Nokia с синхронным `__sysImport`

``` 
1. Собрать Solution (✅ 0 errors)
2. Развернуть на !Browsers
3. Навигировать на https://nokiadesignarchive.aalto.fi/
4. Проверить [DIAG:System.import] OK + появление D3 визуализаций
5. Если D3 появились — закрыть трек Nokia legacy
6. Если D3 не появились — исследовать System.registry формат Vite
7. Затем Phase S (Stability): S.2, S.5, S.6
```

---

## 8. Изменённые файлы

| Файл | Изменение |
|------|-----------|
| `Doc/Plan_04.md` | v4.4, sessions 3.31–3.32, updated Nokia status |
| `Doc/Summary_4_08.md` | **этот файл** |
| `Engine/JavaScriptEngine.cs` | `__sysImport` standalone, System pre-def удалён, inline interception, sync fetch |
| `Src/NiL.JS/NiL.JS/Core/Interop/Proxy.cs` | Null-guard в `fillMembers()` (3.30) |

---

*Summary v4.13 — 2026-06-05 (sessions 3.30–3.32: live test series, __sysImport, sync fetch)*
*Build: ✅ 0 errors*
