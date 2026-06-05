# Summary 4.02 — Phase C.1: calc() + vw/vh/dvw/dvh

**Session date:** 2026-06-04
**Build:** `msbuild MediaExplorer.sln` — ✅ **0 errors**

---

## 1. Проверка: что уже было реализовано

При анализе кода выяснилось, что `calc()` + `vw`/`vh` уже полностью реализованы:

- **`CssLoader.cs:2859` — `TryPx`**: обрабатывает `px`, `vw`, `vh`, `rem`, `em`, `calc()`, raw numbers
- **`CssLoader.cs:2928` — `EvaluateCalc`**: заменяет CSS-единицы внутри `calc(...)` и вычисляет арифметику (+, -, *, /, скобки) через рекурсивный парсер `EvaluateMathExpression`/`EvalAddSub`/`EvalMulDiv`/`EvalPrimary`
- **`test.html:301` — `T-C-011`**: тест для `calc(100% - 40px)` и `50vw` уже существует

## 2. Что добавлено

### `dvw`/`dvh` (dynamic viewport units) — отсутствовали

**Файл:** `Engine/CssLoader.cs`

| Где | Что сделано |
|-----|-------------|
| `TryPx()` | Добавлены блоки `dvw`/`dvh` **перед** `vw`/`vh` (чтобы `EndsWith("vw")` не перехватил `"50dvw"`) |
| `EvaluateCalc()` | Добавлены regex-замены `([\d.]+)\s*dvw` и `([\d.]+)\s*dvh` перед `vw`/`vh` |

**Файл:** `Html/test.html`

- `T-C-011` дополнен строкой `25dvw` (coral) для проверки dynamic viewport units

## 3. Состояние Phase C.1

| Пункт | Статус |
|-------|--------|
| `TryPxResolved()` с явными параметрами vpW/vpH | ✅ Уже работает через статические `_viewportWidth`/`_viewportHeight` + `SetViewportDimensions()` |
| `vw`/`vh` | ✅ |
| `dvw`/`dvh` | ✅ Добавлены |
| `calc(100% - 40px)` | ✅ |
| `EvalCalc` с parent-relative `%` | 🟡 Не добавлен — `%` внутри calc пока считается от viewport, не от parent. Это известное ограничение. |
| `T-C-011` в test.html | ✅ Обновлён |

**Phase C.1 можно считать выполненной.**

---

## 4. Next Steps

```
Session 3.24: Phase C.4 — CSS custom properties (--var) scope fix
               Add T-C-013 to test.html, verify pass
```

---

*Summary v4.02 — 2026-06-04*
*Phase C.1 — calc() + vw/vh/dvw/dvh — done*
