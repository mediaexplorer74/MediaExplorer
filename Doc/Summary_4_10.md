# Summary 4.10 — Chunk Splitting: Bypassing NiL.JS Parse Limitations

**Session date:** 2026-06-05
**Build:** `msbuild Src\MediaExplorer.sln /p:Configuration=Debug /p:Platform=x64` — ✅ **0 errors**
**Mode:** code + live VS run (diagnostics)

---

## 1. Problem

After Session 3.33 (minimal SystemJS replacement), diagnostics showed that `System.register` was **never called** by the legacy chunk despite the chunk starting with `System.register([],...)`:

```
[DIAG:SYS] System.register = fn               ← our System.register exists
[DIAG:SYS] running chunk via SafeEval (589152 bytes)...
Вызвано исключение: NiL.JS.Core.JSException ×3
[DIAG:SYS] _modId=0                           ← never incremented!
[DIAG:SYS] registry entries: 0                ← no modules registered
```

A direct test `System.register([], ...)` via a separate `SafeEval` call worked perfectly:
```
[DIAG:SYS] register ENTERED typeof this=object has_reg=true
[DIAG:SYS] register OK url=mod:1
```

This proved the **register function itself works** but the chunk code is never reaching it.

### Root Cause

NiL.JS cannot parse the entire **589 KB chunk** as a single `Eval()` call. Some syntax somewhere in the file causes the parser to throw **3 JSExceptions** during parsing — before *any* code executes, including the `System.register(...)` at the very start of the file.

---

## 2. Fix: Split Chunk into Individual `System.register(...)` Calls

Instead of evaluating the entire chunk at once, we extract each top-level `System.register(deps, declare)` expression using C# parenthesis matching (handling strings and escaped chars) and evaluate each one independently via `SafeEval`.

### 2.1 Implementation

**File:** `Engine/JavaScriptEngine.cs` (inside `__sysImport` lambda, replacing `sysEngine.SafeEval(txt)`)

```csharp
var sysCalls = new List<string>();
int sysPos = 0;
while (sysPos < txt.Length)
{
    int rIdx = txt.IndexOf("System.register(", sysPos, StringComparison.Ordinal);
    if (rIdx < 0) break;
    // match parentheses for complete expression
    int pStart = rIdx + "System.register".Length;
    ...
    // extract balanced expression
    if (depth == 0) { sysCalls.Add(txt.Substring(rIdx, i - rIdx)); sysPos = i; }
}
// evaluate each extracted call separately
foreach (var call in sysCalls) {
    sysEngine.SafeEval(call);
    ...
}
```

### 2.2 Files Changed

| File | Change |
|------|--------|
| `Engine/JavaScriptEngine.cs` | Replaced single `sysEngine.SafeEval(txt)` with inline chunk-splitting loop + per-call `SafeEval`. Removed orphaned `SplitSystemRegisterCalls` helper method. Fixed class closing brace (was accidentally removed during edits). Fixed `_reg count` diagnostic to count `_entries` keys instead of `_reg` properties. |

### 2.3 Diagnostics Added

- `[DIAG:SYS] split chunk into N System.register calls` — total extracted calls
- `[DIAG:SYS] call #N OK (X bytes)` — each successfully evaluated call
- `[DIAG:SYS] call #N FAIL: ExceptionType - message` — failed calls
- `[DIAG:SYS] direct_test _entries count=N` — counts `_entries` keys (was wrongly counting `_reg` properties)

---

## 3. Build

```
msbuild "Src\MediaExplorer.sln" /p:Configuration=Debug /p:Platform=x64
```

- **0 errors**, warnings are pre-existing (`CS0618: JSValue.Marshal obsolete`, `CS0067`, `CS0414`)

---

## 4. Live Test Results

The chunk-splitting approach was **verified live** in Session 3.35:

```
[DIAG:SYS] running chunk via SafeEval (589152 bytes)...
[DIAG:SYS] chunk prefix: 'System.register([],(function(e,t){"use strict";return{execute:function(){var e=d'
[DIAG:SYS] IndexOf 'System.register(' = 0
[DIAG:SYS] split chunk into 1 System.register calls    ← ✅ extracted!
...
[DIAG:SYS] ctxTest=ok
[DIAG:SYS] System var=defined
[DIAG:SYS] globalThis.System after chunk: ok
[DIAG:SYS] _modId=1
[DIAG:SYS] registry entries: 1
[DIAG:SYS] force-exec: 1 entries, 1 declared
[DIAG] RenderAsync Phase3 JS DONE
```

**Key metrics:**
- 1 System.register call extracted from 589 KB chunk (the chunk is a single call)
- Module registered successfully (`_modId=1`, `registry entries: 1`)
- Force-execute called `declare()` → `execute()` — module ran
- JS phase completed, page renders (1024×1024, 73 boxes, 22 text nodes)

### 4.1 Parenthesis Matcher Bug (found during live test)

Initial live test showed `split chunk into 0 System.register calls` despite `IndexOf('System.register(') = 0`. Root cause: the paren-depth matcher didn't skip:
- `//` line comments — parens inside them corrupted depth
- `/* */` block comments — same issue
- `/regex/` literals — `[)]`, `\(`, `\)` etc. contain `()` that aren't script-level grouping parens
- `` `template literals` `` — parens inside expanded `{...}` thrown off depth

**Fix** (Session 3.35): Enhanced matcher with comment/regex/template skipping. See `Doc/Summary_4_11.md`.

---

## 5. Remaining Work

| Task | Priority | Status |
|------|----------|--------|
| Live test of chunk-splitting approach | 🔴 | ✅ DONE (3.35) |
| Fix paren matcher (comments/regex/templates) | 🔴 | ✅ DONE (3.35) |
| Phase S.2 — JS timeout | 🟡 | Pending |
| Phase S.5 — Error page | 🟡 | Pending |
| Phase S.6 — Cascade/layout guards | 🟡 | Pending |
| Phase V — Visual polish | 🟢 | Pending |

---

## 6. Key Decisions

- **Split, don't fix the parser:** NiL.JS is a frozen dependency (JS Engine Freeze per Plan_04). Instead of patching NiL.JS parser bugs, we work around the limitation by feeding it smaller chunks.
- **Inline C# splitting, not a separate method:** The original `SplitSystemRegisterCalls` helper method caused a compilation error (`does not exist in current context`) due to class-scope issues from conditional compilation (`#if USE_NILJS`). Inlined the logic directly in the lambda to avoid scope problems.
- **Parenthesis matching, not regex:** Simple `String.IndexOf` + depth counter correctly handles nested function bodies, strings, and escaped characters in Babel-compiled ES5 output.

---

*Summary v4.14 — 2026-06-05 (session 3.34: chunk-splitting to bypass NiL.JS parse limit)*
*Updated 2026-06-05 (session 3.35: live test + parenthesis matcher fix)*
*Build: ✅ 0 errors*
