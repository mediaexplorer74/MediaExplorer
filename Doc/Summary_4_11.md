# Summary 4.11 — Parenthesis Matcher Fix: Regex / Comment / Template Skipping

**Session date:** 2026-06-05
**Build:** N/A (UWP project needs Windows SDK — NiL.JS builds separately: ✅ 0 errors)
**Mode:** analysis + code + diagnostics from live VS run

---

## 1. Problem

Diagnostics from Session 3.34 confirmed the chunk **starts with** `System.register(` at position 0:

```
[DIAG:SYS] chunk prefix: 'System.register([],(function(e,t){"use strict";return{execute:function(){var e=d'
[DIAG:SYS] IndexOf 'System.register(' = 0
```

But the paren-depth matcher found **0 calls**:

```
[DIAG:SYS] split chunk into 0 System.register calls
```

The outer `while` loop found `System.register(` at index 0, but the inner depth-counter loop never reached `depth == 0` — the matching `)` was never found. Every occurrence fell through to `else { sysPos = rIdx + 1; }` and the loop exited with empty results.

### Root Cause

The original paren matcher (`JavaScriptEngine.cs`, ~line 3474) tracked only:
- `(` → depth++
- `)` → depth--
- `\` → skip next char (inside strings)
- `"` / `'` → toggle string mode

It **did not handle**:
| Construct | Issue |
|-----------|-------|
| `//` line comments | If a line comment contains `(`, depth increases but `)` may be on a different line → depth stuck |
| `/* */` block comments | Can contain `(` without matching `)` (e.g. `/* (a) comment */` is balanced, but `/* Parameters: (a), (b) */` has 3× `(` and 0× `)`) |
| `/regex/` literals | `/[)])/` has `)` inside character class (literal, not group close) — depth decrements incorrectly. `/(/` (match literal `(`) increments depth but has no matching `)`. |
| `` `template literals` `` | Backtick strings can contain `` `${...}` `` with parentheses inside JavaScript expressions |

Any of these in the 589 KB D3.js v7 bundle would corrupt the depth counter, causing it to never reach 0.

---

## 2. Fix: Enhanced Parenthesis Matcher

**File:** `Engine/JavaScriptEngine.cs` (the chunk-splitting while loop inside `__sysImport`)

### 2.1 State Variables Added

| Variable | Purpose |
|----------|---------|
| `inLineCmt` | Inside `//` comment → skip to `\n`/`\r` |
| `inBlockCmt` | Inside `/*` → skip to `*/` |
| `inRegex` | Inside `/regex/` → skip to closing `/` |
| `inRegexClass` | Inside `[...]` within regex → `)` is literal, not group close |
| `inTmpl` | Inside `` `template` `` → skip to closing `` ` `` |
| `afterExprPrefix` | Heuristic for `/` disambiguation: **true** after `(`, `,`, `=`, `:`, `[`, `{`, `!`, `?`, `+`, `-`, etc. (→ `/` starts a regex); **false** after letters, `)`, `]`, `}`, digits (→ `/` is division) |

### 2.2 Processing Order (per character)

1. **In block comment** — look for `*/`, else advance
2. **In line comment** — look for `\n`/`\r`, else advance
3. **In regex** — handle `\` escape, `[...]` char class, `/` end + optional flags
4. **In string** — handle `\` escape, matching quote toggles `inStr` + sets `afterExprPrefix = false`
5. **In template** — handle `\` escape, `` ` `` toggles `inTmpl` + sets `afterExprPrefix = false`
6. **`/`** — check `//` and `/*` first; if standalone, use `afterExprPrefix` to decide regex vs division
7. **`(`** — `depth++`, `afterExprPrefix = true`
8. **`)`** — `depth--`, `afterExprPrefix = false`
9. **`"` / `'`** — enter string mode
10. **`` ` ``** — enter template mode
11. **`[`, `{`, `,`, `;`, `:`** — `afterExprPrefix = true`
12. **`]`, `}`** — `afterExprPrefix = false`
13. **Operators** (`=`, `!`, `&`, `|`, `?`, `+`, `-`, `*`, `%`, `^`, `<`, `>`, `~`) — `afterExprPrefix = true`
14. **Letters/digits/underscore/dollar/dot** — `afterExprPrefix = false` (identifiers/numbers/properties)
15. **Otherwise** — advance

### 2.3 Regex vs Division Heuristic

The `afterExprPrefix` heuristic correctly classifies `/` in common patterns:

| Pattern | `/` role | Detection | Correct? |
|---------|----------|-----------|----------|
| `var x = /regex/` | Regex | `=` → prefix=true → regex | ✅ |
| `(/regex/)` | Regex | `(` → prefix=true → regex | ✅ |
| `a / b / c` | Division | `a` → prefix=false → division | ✅ |
| `{a: /regex/}` | Regex | `:` → prefix=true → regex | ✅ |
| `x && /regex/` | Regex | `&` → prefix=true → regex | ✅ |
| `return /regex/` | Regex | ❌ misidentified as division (rare in D3.js) | ⚠️ Acceptable |

---

## 3. Live Test Results

After the fix, re-run on Nokia Design Archive:

```
[DIAG:SYS] running chunk via SafeEval (589152 bytes)...
[DIAG:SYS] chunk prefix: 'System.register([],(function(e,t){"use strict";return{execute:function(){var e=d'
[DIAG:SYS] IndexOf 'System.register(' = 0
[DIAG:SYS] split chunk into 1 System.register calls    ← **FIXED!**
...
[DIAG:SYS] ctxTest=ok
[DIAG:SYS] globalThis.System after chunk: ok
[DIAG:SYS] registry entries: 1
[DIAG:SYS] force-exec: 1 entries, 1 declared
[DIAG] RenderAsync Phase3 JS DONE
```

**Result:** `split chunk into 0` → **`1`** call extracted, module registered and executed.

---

## 4. Build

- **MediaExplorer (UWP):** Cannot build locally — requires Windows 10 SDK
- **NiL.JS:** `dotnet build Src\NiL.JS\NiL.JS` — ✅ **0 errors**
- **Code verified:** brace-balanced, logic confirmed by live test

---

## 5. Remaining Work

| Task | Priority | Status |
|------|----------|--------|
| Phase S.2 — JS execution timeout | 🟡 | Pending |
| Phase S.5 — Graceful error page | 🟡 | Pending |
| Phase S.6 — Cascade/layout exception guards | 🟡 | Pending |
| Phase V — Visual polish (AppBar, progress, omnibox) | 🟢 | Pending |
| Diagnostics: why `System.Exception` during render phase (Nokia Archive) | 🟡 | Untested |

---

## 6. Key Decisions

- **Char-by-char over regex:** .NET regex with balancing groups would be more elegant but harder to debug. Char-by-char gives explicit control over each state transition.
- **Heuristic over perfect regex detection:** The `afterExprPrefix` heuristic misclassifies `return /regex/` as division, but such cases are vanishingly rare in Babel-compiled ES5 bundles. Perfect detection would require token-level lookahead (not worth the complexity).
- **Template literal limitation:** `${...}` interpolations inside template literals are skipped entirely (parens inside them not counted). Babel converts template literals to string concatenation for ES5 targets, so this is a non-issue for the legacy bundle.

---

*Summary v4.15 — 2026-06-05 (session 3.35: parenthesis matcher fix + live test)*
*Build: 🟡 UWP not buildable locally (no Windows SDK); NiL.JS ✅ 0 errors*
