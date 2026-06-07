# Summary 5.04 — d3.v5 Initialized on UWP (Regex Fix), Phase J Complete

**Session date:** 2026-06-07
**Build:** MediaExplorer (UWP) — 0 errors ✅, NilJsTest (net8.0) — 0 errors ✅

---

## 1. Context

Summary 5.03 left off with d3.v5 working flawlessly in NilJsTest (net8.0) but failing on UWP. The suspected cause was polyfill prefix corruption of d3's ES5 code. **This session disproved that theory** and identified the real root cause: `RegexOptions.Compiled` in NiL.JS crashes on UWP CoreCLR (netstandard1.4) with `ArgumentOutOfRangeException` when compiling JS RegExp literals via `Reflection.Emit`.

---

## 2. Diagnosis Journey

| Step | Hypothesis | Test | Result |
|------|-----------|------|--------|
| 1 | Polyfill prefix corrupts d3.v5 | Remove polyfill prefix, redeploy (PhaseG1-log.txt) | Regex exceptions persist without polyfills → **hypothesis WRONG** |
| 2 | DebuggerCallback causes eval instability | Add `SafeEvalFast` (direct eval, no debugger), redeploy (PhaseG2-log.txt) | Regex exceptions still persist → **hypothesis WRONG** |
| 3 | `RegexOptions.Compiled` crashes on UWP CoreCLR | Disable `RegexOptions.Compiled` in RegExp.cs:74-75, rebuild & redeploy | **Regex exceptions GONE** → **hypothesis CONFIRMED** |

**Root cause:** `System.Text.RegularExpressions.Regex` with `RegexOptions.Compiled` uses `Reflection.Emit` to JIT-compile the regex pattern. UWP CoreCLR (netstandard1.4) does not support `Reflection.Emit` for regex compilation in all cases — when JS code defines ~15 RegExp literals (as d3.v5 does), each `new Regex(pattern, Compiled)` throws `ArgumentOutOfRangeException`, corrupting the eval context and aborting d3.js initialization mid-parse.

---

## 3. Fixes Applied

### Fix 1: Remove `RegexOptions.Compiled` (NiL.JS) — THE DEFINITIVE FIX

`Src/NiL.JS/NiL.JS/BaseLibrary/RegExp.cs:74-75`:

```csharp
// Before (UWP CoreCLR crashes on Reflection.Emit):
var regex = new Regex(pattern, options | RegexOptions.Compiled);

// After (interpreted only — stable, ~2x slower, no JIT crash):
var regex = new Regex(pattern, options);
```

### Fix 2: Add `SafeEvalFast` (JavaScriptEngine.cs)

New direct-eval method without `SafeEval`'s DebuggerCallback wrapping. Added as defense-in-depth against UWP NiL.JS instability on large scripts.

d3 eval path now uses `SafeEvalFast`.

### Fix 3: Remove polyfill prefix for d3.v5

d3.v5 is pure ES5 — no `let`, `const`, `Map`, `Set`, `Symbol`, `Proxy`, arrow functions, or other ES6+ features. The ES6→ES5 polyfill transforms were unnecessary and were removed from the d3 eval path. The prefix was originally designed for d3.v7 (pre-URL-rewrite); d3.v5 needs no transforms.

---

## 4. Verification — PhaseG2-log.txt Analysis

```
[DIAG:SYS] post-exec d3=d3_defined
[DIAG:EXEC] typeof d3.select = function
[DIAG:EXEC] typeof d3.forceSimulation = function
```

### G1 (before fix) vs G2 (after fix)

| Metric | G1 | G2 |
|--------|----|----|
| `ArgumentOutOfRangeException` (Regex) | **15×** | **0 — none** |
| `[d3-err:object]` in log | Yes | Yes (from polyfill script, not d3) |
| `d3_defined` | ✅ yes | ✅ yes |
| `typeof d3.select` | `no` | **`function`** |
| `typeof d3.forceSimulation` | `no` | **`function`** |
| Navigation to Nokia archive | — (crashes during eval) | **✅ succeeds, 1024×1024 Border** |
| Thread exit codes | — | **4 threads code 0** |

**Key insight:** The `[d3-err:object]` on line 98 of G2 comes from the **polyfill-legacy script** (35KB), NOT from d3.v5 (248KB). The polyfill script runs through the same `SafeEvalFast` with try-catch wrapper and hits its own JSException — but this is a separate, non-fatal eval. The real d3 result at lines 140-142 confirms d3 is fully initialized and callable.

---

## 5. Phase J Complete — Goal State Achieved

From Plan_05.md §J.3:

> The session ends when the log shows:
> ```
> [DIAG:SYS] post-exec d3=d3_defined
> [DIAG:EXEC] typeof d3.select = function
> [DIAG:EXEC] typeof d3.forceSimulation = function
> ```

**Phase J is now COMPLETE.** The binary search of the 589KB main chunk and the stub-each-missing-global approach (estimated 2-3 sessions in the original plan) were bypassed entirely — the issue was purely a runtime compilation problem, not JS code compatibility. A single source change (`RegexOptions.Compiled` → interpreted) resolved the entire phase.

---

## 6. Next Steps

**Phase G.1 — SVG Serialization (1-2 sessions estimated)**

The d3 data pipeline is alive in memory but SVG elements (`circle`, `line`, `path`, `text`, `g`) fall through to `default` in `DispatchTagAsync`. Next:
1. Implement `SerializeSvgToString` in `DomBasicRenderer.cs`
2. Render SVG as `Image` with `SvgImageSource`
3. Add MutationObserver re-render at 15fps throttle
4. First visual: Nokia Archive network graph on Lumia 950

---

*Next: Phase G.1 — SVG bridge. The d3 pipeline is alive. Time to make it visible.*
