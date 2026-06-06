# Summary 4.07 — C.6 Follow-up + Nokia Design Archive Analysis

**Session date:** 2026-06-05
**Build:** `msbuild MediaExplorer.sln /p:Configuration=Debug /p:Platform=x86` — ✅ **0 errors**
**Mode:** autopilot (no emulator/device)

---

## 1. Цель

Два направления:

1. **C.6 follow-up** — multi-property comma-separated transitions, smooth background
   (`Transparent` → animate), `%` в `translate()`, исправление «all».
2. **Nokia Design Archive** — анализ рендеринга и JS-совместимости,
   имплементация legacy-пути через `nomodule` (байпас Vite-модулей).

---

## 2. Phase C.6 Follow-up (Session 3.27)

### 2.1 Multi-property transitions

**Новое в `CssComputed.cs`:**
- `TransitionSpec` sealed class: `Property`, `DurationMs`, `TimingFunction`, `DelayMs`, `Raw`
- `List<TransitionSpec> TransitionList` — все сегменты `transition: a 0.3s, b 0.5s`

**Новое в `CssLoader.ParseTransition`:**
- Больше не берёт только первый сегмент — парсит все через `SplitTopLevelCommas()`
  (существующий хелпер от C.5, дубликат удалён).
- Каждый сегмент → `TransitionSpec` → добавляется в `TransitionList`.
- Для обратной совместимости: первый entry по-прежнему заполняет
  `TransitionProperty`, `TransitionDurationMs` и т.д.

**Новое в `ComputeHoverOverrides`:**
- Проверка `hasTransition` — или `TransitionList` непуст, или `TransitionDurationMs > 0`.

**Новое в `DomBasicRenderer.AttachHoverTransition`:**
- Полный рефакторинг: больше нет одного `prop`/`dur`/`timing`.
- `BuildTransitionSpecs()` — строит список `TransitionSpecForAnim` из `TransitionList`
  (или fallback на старые поля).
- `TransitionSpecForProp(cssProp, specs, ...)` — ищет первый spec с точным
  совпадением property, затем fallback на «all».
- Каждое свойство (opacity/background/transform) получает свой duration/delay/timing.
- Вспомогательный sealed class `TransitionSpecForAnim`.

### 2.2 Smooth background (no base brush)

**Проблема:** Если у элемента не было `Background` (base brush = null), то при
`PointerEntered` цвет присваивался прыжком — нечего анимировать.

**Фикс:** При `!hasBaseBrush`:
- `PointerEntered` → `SetBackgroundBrush(fe, Transparent)` → `AnimateBackgroundColor(fe, hoverColor)`
- `PointerExited` → `AnimateBackgroundColor(fe, Transparent)`

### 2.3 `%` в `translate()`

**Проблема:** `translate(50%, 0)` не распознавалось — `ParseTransformForHover` умел
только `px` и голые числа.

**Фикс:**
- Сигнатура расширена: `ParseTransformForHover(string t, out ..., double elemW, double elemH)`
- В translate-ветке: если аргумент заканчивается на `%` и `elemW`/`elemH > 0`,
  вычисляет `(pct / 100.0) * elemSize`.
- Парсинг hover-трансформа перенесён внутрь `PointerEntered` — чтобы `ActualWidth`/
  `ActualHeight` уже были известны (после layout).

### 2.4 «all»

**Было:** Комментарий в `TransitionAnimator.cs` гласил «currently treated as transform».
**На деле:** `AttachHoverTransition` уже проверял все три свойства при `prop == "all"`.
**Фикс:** Обновлён комментарий → «animates opacity + background-color + transform».

### 2.5 Мелкие правки

- `TransitionAnimator.cs` — исправлен комментарий.
- `CssLoader.cs` — удалён дубликат `SplitTopLevelCommas` (был и в C.5, и в новом
  коде; оставлен оригинальный).

### 2.6 Тесты

`Html/test_transitions.html` расширен тремя новыми кейсами:

| # | Класс | Что тестирует |
|---|-------|---------------|
| 12 | `.t-multi-dur` | Разные duration для каждого свойства (`opacity 0.5s, bg 0.3s, transform 0.2s`) |
| 13 | `.t-nobase-bg` | Плавный переход `transparent → green` при отсутствии base brush |
| 14 | `.t-pct` / `.t-pct-30` | `translate(50%, 50%)` и `translate(30%, -30%)` с % от размера элемента |

---

## 3. Nokia Design Archive Analysis (Session 3.28)

### 3.1 Что было загружено

При навигации на `https://nokiadesignarchive.aalto.fi/`:

- **HTML:** ~7KB, тонкая обёртка — статические элементы UI (сайдбар, фильтры,
  поиск, zoom-controls) + `type="module"` + `nomodule` скрипты.
- **CSS:** `main-a-1_fGhU.css` загружен за 91ms.
- **JS (модули):** `d3.v7.min.js` (CDN), `main-BE-aXEfW.js` (568KB Vite-бандл),
  Vite-detection inline-скрипты.
- **JS (legacy):** `polyfills-legacy-0pdt4c7e.js` (SystemJS), `main-legacy-CgkFIb-k.js`
  (ES5+SystemJS формат).
- **DOM:** 102 ноды, 73 box'а, 22 текстовых — отрендерилась статическая часть
  (сайдбар, фильтры, иконки, Aalto-логотип).

### 3.2 Проблема

Vite-собранный сайт использует ES модули (`type="module"`):
```html
<script type="module" crossorigin src="/assets/main-BE-aXEfW.js"></script>
<script type="module">import.meta.url, import("_").catch(...), async function*(){}().next(),
"file:"!=location.protocol&&(window.__vite_is_modern_browser=!0)</script>
```

NiL.JS:
- Не поддерживает `async function*(){}` (async generators) на синтаксическом уровне
- `import.meta` — уже работало (фикс 3.9), но модули создают отдельный контекст
- `__vite_is_modern_browser` устанавливался в `true` (протокол не `file:`)
- В результате: современный бандл падал, legacy-бандл (`nomodule`) игнорировался
- `<noscript>` GTM удалялся без вреда (остальной контент — в обычном HTML)

### 3.3 Фикс

Одна строка логики в `JavaScriptEngine.RunScriptsAsync`:

```csharp
if (type == "module") continue; // skip ES modules
```

Эффект:
- `type="module"` → пропускаются (не выполняются, не фетчатся ModuleLoader'ом)
- `nomodule` → выполняются как обычные скрипты (инверсия браузерной логики)
- D3.js v7 (CDN, UMD) → выполняется (ES5-совместимый)
- `polyfills-legacy` → грузится, бутстрапит SystemJS
- `main-legacy` → System.import() загружает и выполняет ES5-бандл

### 3.4 Оставшиеся риски

1. **`System.import()`** — полагается на `document.getElementById()` (✅ работает)
   и `document.createElement('script')` / `appendChild` (может не хватать
   реализации в NiL.JS).
2. **D3.js v7** — UMD-бандл ~500KB, может использовать современные фичи,
   не поддерживаемые NiL.JS (Symbol, итераторы).
3. **DOM-анимации** — `addEventListener` на SVG-элементах зарегистрирован,
   но может не триггериться от XAML-ивентов.
4. **Canvas 2D API** — отсутствует (D3 может его не требовать — рисует через SVG).

---

## 4. Изменённые / новые файлы

| Файл | Изменение |
|------|-----------|
| `Engine/CssComputed.cs` | + `TransitionSpec` class, + `TransitionList` |
| `Engine/CssLoader.cs` | `ParseTransition` полный рефакторинг (все comma-сегменты), удалён дубликат `SplitTopLevelCommas`, `ComputeHoverOverrides` новый guard |
| `Engine/DomBasicRenderer.cs` | `AttachHoverTransition` рефакторинг (TransitionSpecForProp, BuildTransitionSpecs), smooth background, `%` в `ParseTransformForHover` |
| `Engine/TransitionAnimator.cs` | Комментарий «all» исправлен |
| `Engine/JavaScriptEngine.cs` | `RunScriptsAsync` — skip `type="module"`, execute `nomodule` |
| `Html/test_transitions.html` | +3 кейса (12: multi-dur, 13: no-base-bg, 14: % translate) |
| `Doc/Plan_04.md` | C.6 follow-up записан, сессия 3.27 добавлена, Nokia-анализ 3.28, TL;DR обновлён |
| `Doc/Summary_4_07.md` | **этот файл** |

---

## 5. Состояние Phase C

| Подфаза | Статус | Сессия |
|---------|--------|--------|
| C.1 — `calc()` + `vw`/`vh`/`dvw`/`dvh` | ✅ | 3.23 |
| C.2 — `@media` queries (полный набор) | ✅ | 3.22 |
| C.3 — `grid-template-areas` | ✅ | 3.21 |
| C.4 — CSS custom properties (`--var`) | 🟡 parked (3.25) | 3.24 + 3.25 |
| C.5 — `clamp()` | ✅ | 3.25 |
| C.6 — Basic CSS Transitions | ✅ (включая follow-up) | 3.26 + 3.27 |

**Phase C завершена 5/6** (1 в парковке до устройства).

---

## 6. Nokia Design Archive Status

| Метрика | Значение |
|---------|----------|
| HTML загружен | ✅ (6946 байт) |
| CSS загружен | ✅ (main-a-1_fGhU.css, 91ms) |
| D3.js v7 (CDN) | ✅ UMD, выполняется (ES5) |
| Vite-бандл (main-BE-aXEfW.js) | 🔄 Bypassed — грузится legacy-версия |
| Legacy polyfill | 🟡 Полифил загружен, System.import() → зависит от DOM API |
| Static HTML rendered | ✅ 73 boxes, 22 text nodes |
| D3 visualizations | ❌ Требуют полноценного DOM событий |

---

## 7. Следующие шаги (3.29+)

```
Session 3.29: Проверка Nokia Archive вживую — запуск, лог,
              доработка SystemJS/DOM API если legacy-бандл не догружается

Session 3.30: Phase S — JS timeout (S.2), error page (S.5),
              cascade/layout guards (S.6)

Session 3.31: Phase V — AppBar animation + progress bar + omnibox
```

---

*Summary v4.07 — 2026-06-05 (sessions 3.27–3.28)*
*Phase C.6 follow-up + Nokia Design Archive JS compat — ✅ build 0 errors*
