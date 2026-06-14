# Summary 4.03 — Phase C.4: CSS Custom Properties (--var) Scope Fix

**Session date:** 2026-06-04
**Build:** `msbuild MediaExplorer.sln` — ✅ **0 errors**

---

## 1. Problem

CSS custom properties (`--name`) cascade per-element: a child inherits the nearest ancestor's value, not the `:root`'s. Example:

```css
:root { --x: blue; }
.parent { --x: purple; }
.child { color: var(--x); }
/* .child inside .parent → should be purple, was blue */
```

## 2. Root Cause

`ResolveVariables()` (строка 3273) делала **глобальную** подстановку `var(--name)` во все CSS-правила, используя только значения из `:root`, **до** каскада. После этого каскад видел уже подставленное значение `:root`, и переопределения на элементах-родителях игнорировались.

**Исправление:** убрали вызов `ResolveVariables(allRules)` на строке 212. Per-element резолв через `ResolveCustomPropertyReferences` (строка 1124/1511) уже умеет ходить по цепочке наследования: `rawCustom` → `current.CustomProperties` (скопированы от parent'а) → fallback.

## 3. Root cause: skipped elements break inheritance chain

В основном каскаде (`CascadeIntoComputedStyles`) — если у элемента нет ни одного подходящего CSS-правила, он **скипался** (`continue`) и не попадал в `result`. Дочерние элементы не находили родителя в `result`, и наследование `CustomProperties` обрывалось.

Раньше `ResolveVariables` маскировал этот баг: `var(--name)` подменялся глобально из `:root` до каскада, и дочерние элементы получали значение напрямую, минуя наследование.

## 4. Changes

| Файл | Изменение |
|------|-----------|
| `Engine/CssLoader.cs:212` | **Удалён** вызов `ResolveVariables(allRules)`, добавлен комментарий |
| `Engine/CssLoader.cs:1448` | Вместо `continue` для элементов без правил — создаём `CssComputed` с наследованием от родителя и сохраняем в `result`, чтобы дети могли найти родителя |
| `Html/test.html` | Добавлен `T-C-013` — три бара: `:root` (синий), наследованный от `#cp-override` (фиолетовый), fallback (малиновый) |

## 5. Testing

`T-C-013` в `about:test` проверяет три сценария:
1. Синий бар — `:root { --cp-global: #0078D7; }` + `background: var(--cp-global)`
2. Фиолетовый бар — `#cp-override { --cp-global: purple; }` + наследование в child
3. Малиновый бар — fallback `var(--cp-missing, crimson)`

## 6. Runtime Status: T-C-013 Still Broken

Despite code analysis showing correct logic at every level, T-C-013 bars render text but not backgrounds at runtime. `var(--cp-global)` resolves to empty string. The 3 bars are invisible (white text on white background).

### What was analyzed (all look correct on paper)

| Check | Status |
|-------|--------|
| `:root` pseudo-class matches `<html>` | ✅ `MatchesSingle` line 2288 checks `n.Tag == "html"` |
| Selector index: `:root` indexed under `"*"` | ✅ No tag/id/class → wildcard bucket |
| Candidate gathering for `<html>` | ✅ `TryAddFromIndex("*")` finds `:root` rule |
| `<html>` gets `--cp-global: #0078D7` | ✅ `chosen["--cp-global"]` set from matched rule |
| `<style>` inside `<div>` parsed correctly | ✅ `ForeignContentTags` includes `"style"` |
| `InheritFrom` copies `CustomProperties` | ✅ line 1268-1274 |
| `EvaluateVarExpression` checks `current.CustomProperties` | ✅ line 2438 |
| Inline `style="background:var(--cp-global)"` parsed | ✅ `ParseDeclarations` handles `var()` |
| `ComputeAsync` result used (line 577, not discarded at line 1261) | ✅ |

### Debug logging added (Session 3.24b)

7 `Debug.WriteLine` points with prefix `[CssLoader-TCP13]`:

| Tag | What it logs |
|-----|-------------|
| `MATCH` | Rule with `--custom` declarations matched a node |
| `INLINE` | Inline style contains `var()` — property, raw value |
| `INHERIT` | `CustomProperties` copied parent→child — tag, keys, count |
| `EVAL_VAR` | `EvaluateVarExpression` path: rawCurrent / CustomProps / MISS |
| `RESOLVE` | `var()` in non-custom prop resolved — input → output |
| `BG` | `ExtractBackgroundColor` — raw value, parsed color, Map state |
| `CASCADE START` | Node count, rule count, selector index keys |

**Next:** Capture debug output from about:test T-C-013 and analyze the log to find where the chain breaks.

## 7. Next Steps

```
Session 3.25: Capture T-C-013 debug logs, identify root cause, fix
              Phase C.5 — clamp()
              Add T-C-014 to test.html, verify pass
```

---

*Summary v4.03 — 2026-06-04 (updated post-session 3.24b)*
*Phase C.4 — CSS Custom Properties scope fix — code done, runtime debugging in progress*
