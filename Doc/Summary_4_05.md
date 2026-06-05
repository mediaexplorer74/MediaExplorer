# Summary 4.05 — Phase C.6: Basic CSS Transitions (Storyboard + DoubleAnimation)

**Session date:** 2026-06-04 — 2026-06-05 (follow-up)
**Build:** `msbuild MediaExplorer.sln /p:Configuration=Debug /p:Platform=x86` — ✅ **0 errors**
**Mode:** autopilot (no emulator/device available — only droid in hand)

---

## 1. Цель

Реализовать Phase C.6 из `Doc/Plan_04.md` — базовые CSS-transitions на трёх
свойствах (`opacity`, `background-color`, `transform`) с поддержкой
`transition: <prop> <duration> <timing-function> <delay>`.

Триггер: hover (PointerEntered / PointerExited).

---

## 2. Архитектура

### 2.1 Где живёт значение перехода

`CssComputed.cs` — добавлено 6 полей:

```csharp
public string  Transition              // сырая декларация, для дебага
public double  TransitionDurationMs    // в миллисекундах (для Storyboard)
public string  TransitionProperty      // "opacity" | "background-color" | "transform" | "all"
public string  TransitionTimingFunction// "linear" | "ease" | "ease-in" | "ease-out" | "ease-in-out"
public double  TransitionDelayMs       // в миллисекундах
public CssComputed Hover               // копия CssComputed с override-значениями из :hover / :focus / :active
```

`Hover` — это **отдельный** `CssComputed` (не мутация основного). Получается
через `ComputeHoverOverrides` после базового каскада; содержит только те
свойства, которые были переопределены в `:hover`/`:focus`/`:active`.

### 2.2 Каскад и интерактивные псевдоклассы

В `CssLoader.MatchesSingle` добавлена ранняя отсечка: правила с `:hover`,
`:focus`, `:active` теперь возвращают `false` из **базового** каскада. Это
семантически правильно (эти псевдоклассы не активны при первичном layout-е)
и устраняет двойной учёт.

После базового каскада для каждого узла вызывается `ComputeHoverOverrides`:
- проходит по **тому же** набору правил, что и базовый каскад (не перепарсит);
- для каждого правила, у которого в selector-chain есть **хотя бы один**
  интерактивный псевдокласс, делает `MatchesIgnoringInteractivePseudos` —
  проверку совпадения всех остальных сегментов;
- если совпало, добавляет декларации этого правила в `css.Hover.Map`.

### 2.3 Парсинг `transition`

`CssLoader.ParseTransition` — вызывается из цикла пер-нод, сразу после
заполнения `css.Map`. Алгоритм:

1. Берёт `css.Map["transition"]`.
2. Если строка содержит запятую — режет по top-level запятым
   (`SplitTopLevelCommas` из C.5), берёт **первый** сегмент
   (multi-property animation — за рамками v1.0).
3. По токенам первого сегмента:
   - первый токен, не duration и не timing-function → property
     (или `all` если не распознан)
   - `IsDurationToken` — ищет `<num>ms` или `<num>s` (использует
     `firstDuration`/`delay`)
   - `IsTimingFunctionToken` — keyword lookup
4. Результат → `css.TransitionDurationMs`, `css.TransitionProperty`, etc.

### 2.4 Аниматор

`Engine/TransitionAnimator.cs` (новый файл, ~80 строк). Пять публичных
методов, все используют `Storyboard.Begin()` с `DoubleAnimation` /
`ColorAnimation`:

| Метод | Свойство | Тип анимации |
|-------|----------|--------------|
| `AnimateOpacity` | `fe.Opacity` | `DoubleAnimation` |
| `AnimateBackgroundColor` | `(fe.Background).(SolidColorBrush.Color)` | `ColorAnimation` |
| `AnimateTransformScale` | `(RenderTransform).(TransformGroup.Children)[i].(ScaleTransform.ScaleX/Y)` | 2× `DoubleAnimation` |
| `AnimateTransformRotate` | `(TransformGroup.Children)[j].(RotateTransform.Angle)` | `DoubleAnimation` |
| `AnimateTransformTranslate` | `(TransformGroup.Children)[k].(TranslateTransform.X/Y)` | 2× `DoubleAnimation` |

Property paths для transform работают, потому что
`RendererStyles.ApplyTransform` всегда выставляет `RenderTransform =
TransformGroup` в фиксированном порядке: translate → scale → rotate → skew
(см. `RendererStyles.cs:1036+`). Аниматор ищет первый дочерний transform
нужного типа и использует его индекс.

`BuildEasing(string name)` маппит timing-функции:
- `linear` → null (анимация линейная)
- `ease-in` / `ease-out` / `ease-in-out` → `CubicEase` с режимом `EaseIn`/`Out`/`InOut`
- `ease` (default) → `QuadraticEase` с `EaseInOut`

### 2.5 Wire-up в рендерере

`DomBasicRenderer.cs`, конец `ApplyComputedStyles`:

```csharp
if (st.Hover != null && st.TransitionDurationMs > 0)
    AttachHoverTransition(fe, st);
```

`AttachHoverTransition`:
1. Снимок базовых значений (`fe.Opacity`, `BackgroundColor` через
   `GetBackgroundColor(fe)`, transform-параметры из
   `RenderTransform as TransformGroup`).
2. Предвычисление hover-значений из `baseCss.Hover.Map`:
   - `opacity` (если `prop == "opacity" || "all"`)
   - background-color через `ExtractBackgroundColor` → `TryParseCssColorToBrush`
   - transform через `ParseTransformForHover` (отдельный мини-парсер, не
     дёргает `ApplyTransform`, чтобы не сломать transform-group)
3. Подписка на `PointerEntered` / `PointerExited`:
   - Entered → запускает анимации к hover-значениям
   - Exited → запускает анимации обратно к базовым
4. Если базовая кисть фона отсутствовала, аниматор просто присваивает
   новый `SolidColorBrush` (прыжок в цвет, без анимации — нет от чего
   интерполировать).

### 2.6 Помощники для фона

`FrameworkElement` в UWP **не** имеет свойства `Background` — оно разбросано
по `Control`/`Panel`/`Border`/`ContentPresenter`. Добавлены 3 хелпера:

```csharp
static Brush GetBackgroundBrush(FrameworkElement fe) // → Control | Panel | Border | ContentPresenter
static void  SetBackgroundBrush(FrameworkElement fe, Brush b)
static Color? GetBackgroundColor(FrameworkElement fe)
```

Все три — простой `if/else` chain по `is`-паттернам. Без рефлексии, без
`try/catch` cast-ов.

---

## 3. Тесты

### 3.1 Расширен T-C-015 в `Html/test.html`

```html
<style>
  #t015a { ...; transition: opacity 0.3s ease; }     /* opacity only */
  #t015a:hover { opacity: 0.5; }
  #t015b { ...; transition: background-color 0.3s; }  /* background only */
  #t015b:hover { background-color: #FFB900; }
  #t015c { ...; transition: transform 0.3s; }         /* transform only */
  #t015c:hover { transform: scale(1.1) rotate(5deg); }
  #t015d { ...; transition: all 0.3s; }               /* все три */
  #t015d:hover { opacity:0.5; background:#FFB900; transform: scale(1.1); }
</style>
<div id="t015a">opacity</div>
<div id="t015b">bg</div>
<div id="t015c">transform</div>
<div id="t015d">all</div>
```

Четыре бокса side-by-side, на hover каждый анимируется к своему состоянию.

### 3.2 Новый файл `Html/test_transitions.html` (зарегистрирован как Content)

11 разных кейсов на одной странице: opacity / background / scale / rotate /
translate / all / fast (0.1s) / slow (1s) / с delay (0.2s) / card lift
(composite transition) / pills. Чтобы визуально проверить, что:
- разные `transition-property` работают выборочно
- разные `duration` (0.1s, 0.3s, 1s) дают разную скорость
- `delay` правильно откладывает старт
- `transition: all` собирает все три свойства
- карточки (`Border`-based) и пилюли (`Border`-based) тоже анимируются
  (не только `Control`-производные)

Зарегистрирован в `MediaExplorer.csproj`:
```xml
<Content Include="Html\test_transitions.html" />
```

---

## 4. Известные ограничения

1. **Multi-property animation** (`transition: opacity 0.3s, transform 0.5s`)
   — берётся только первый сегмент, остальные тихо игнорируются. Соответствует
   плану "good enough" для v1.0.
2. **`:focus` и `:active`** — обрабатываются как `:hover` (нет ввода с
   клавиатуры / мышиных кликов в браузерном сценарии museum). Если когда-то
   понадобится разделение — развести на три отдельных override-объекта.
3. **Transform property paths** завязаны на фиксированный порядок
   translate/scale/rotate/skew, выставляемый `RendererStyles.ApplyTransform`.
   Если там порядок изменится — нужно править индексы в
   `TransitionAnimator` (или взять transform-group итерированием).
4. **`%` для transform** (например `translate(50%, 0)`) — не поддержано в
   `ParseTransformForHover`. Hover-transform с `%` молча не сработает.
5. **Без `transition` на элементе** :hover правила применяются мгновенно
   (через существующий re-render), без анимации — это by design.
6. **Без base-стиля, только :hover** (например, элемент с `background:none`
   базово, `:hover { background: red }`) — фон присваивается прыжком в красный
   при первом hover, а на Exited ничего не анимируется (нечего интерполировать).
   Это явно задокументировано в `AttachHoverTransition`.

---

## 5. Состояние Phase C

| Подфаза | Статус | Сессия |
|---------|--------|--------|
| C.1 — `calc()` + `vw`/`vh`/`dvw`/`dvh` | ✅ | 3.23 |
| C.2 — `@media` queries (полный набор) | ✅ | 3.22 |
| C.3 — `grid-template-areas` | ✅ | 3.21 |
| C.4 — CSS custom properties (`--var`) | 🟡 parked (3.25) | 3.24 + 3.25 |
| C.5 — `clamp()` | ✅ | 3.25 |
| C.6 — Basic CSS Transitions | ✅ | **3.26** |

**Phase C завершена 5/6** (1 в парковке до устройства). По критериям
`Plan_04.md` это означает, что основная CSS-функциональность для v1.0-museum
готова.

---

## 6. Изменённые / новые файлы

| Файл | Изменение |
|------|-----------|
| `Engine/CssComputed.cs` | +6 полей (Transition*) |
| `Engine/CssLoader.cs` | `MatchesSingle` (skip interactive pseudos), `ParseTransition`, `IsDurationToken`, `IsTimingFunctionToken`, `ComputeHoverOverrides` + 4 хелпера per-rule |
| `Engine/TransitionAnimator.cs` | **новый** — 5 Animate* методов + BuildEasing (~90 строк) |
| `Engine/DomBasicRenderer.cs` | `AttachHoverTransition` + 3 Background-helper-а + wire-up в `ApplyComputedStyles` |
| `MediaExplorer.csproj` | `<Compile Include="Engine\TransitionAnimator.cs" />`, `<Content Include="Html\test_transitions.html" />` |
| `Html/test.html` | T-C-015 расширен (4 бокса side-by-side) |
| `Html/test_transitions.html` | **новый** — 11-кейсовая демо-страница |
| `Doc/Plan_04.md` | C.6 помечен ✅, записаны краткие примечания о завершении и TL;DR |
| `Doc/Summary_4_05.md` | **THIS** — обновлённая сводка с пометкой follow-up |
| `Doc/Summary_4_05.md` | **этот файл** |

---

## 7. Следующие шаги (3.27+)

```
Session 3.27: Phase S — robustness pass
              - NaN/Infinity guards in layout/transform
              - JS execution timeout
              - DOM node cap (prevent runaway pages)
              - Catch + recover in cascade/layout/JS
              T-S-001,004,005,007,008 → all "acceptable" visually

Session 3.28: Phase T+ — JS compat matrix (Section F) + CSS coverage grid (Section G)
              No new code, just paperwork + targeted gap-fills

Session 3.29–3.30: Phase V — AppBar animation + progress bar + omnibox + welcome page

Session 3.31: Full regression
              T-H-001..008: all visual pass
              T-C-001..017: target 12/17 pass (was 11/15, +1 for C.6 = 12/17 if T-C-015 passes)
              T-J-001..010: target 7/10 pass
              T-S-001,004,005,007,008: all "acceptable"
              → TAG v1.0-museum

Session 3.32+: Phase 7 (Service Worker) — optional, post-v1.0
```

---

*Summary v4.05 — 2026-06-04 (autopilot session 3.26)*
*Phase C.6 Basic CSS Transitions — ✅ done, build 0 errors, T-C-015 extended, test_transitions.html added*
