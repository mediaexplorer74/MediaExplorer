# Summary 4.14 — D3.js Root Cause: HostMapType/HostSetType empty stubs kill `extends`

**Session date:** 2026-06-05
**Build:** `msbuild Src\MediaExplorer.sln /p:Configuration=Debug /p:Platform=x64` — ✅ **0 errors**
**Mode:** iterative debugging + live emulator test (4 rounds)

---

## 1. Problem

Nokia Archive page loads D3.js v7.9.0 (279KB UMD) without throwing, but `d3` global is never set. Svelte app's `execute()` runs, no SVG elements are created (`boxes=73` never changes).

---

## 2. Debugging Round 1: Visibility

**Observation:** D3.js eval showed 0 `[NiLJS] SafeEval:` error messages, making it seem like D3 loaded fine. But `post-exec d3=d3_missing`.

**Root cause found:** `RunInline()` has a **silent `catch { /* swallow */ }`** on NiL.JS eval failures (line 4653). Any JSException during script execution was completely invisible.

**Fix** (line 5346):
- Non-inline (external) scripts now bypass `RunInline` and call `SafeEval()` directly
- Error logging: `[DIAG:EXEC] SafeEval error for ...`

---

## 3. Debugging Round 2: `this` binding

**Observation:** Even with direct `SafeEval`, D3.js produces no `[DIAG:EXEC] SafeEval error` — yet `d3=d3_missing`. This means **`SafeEval` returns normally** but D3's UMD wrapper doesn't set the global.

**Diagnostic added:** `this | window.d3 | globalThis.d3` — all three show `d3=undefined`.

**Conclusion:** D3.js factory function silently aborts during execution. The JSException from NiL.JS is caught **within** `SafeEval` (which never re-throws — returns `JSValue.Undefined` at line 4187), so no C# exception reaches our catch block.

---

## 4. Debugging Round 3: JS try-catch doesn't help

**Attempt:** Wrapped D3.js content in JavaScript `try{ <d3> }catch(e){}` to survive internal NiL.JS errors.

**Result:** No change — D3 still missing.

**Why:** NiL.JS internal errors are **C# `JSException`** objects thrown inside the C# evaluation loop. JavaScript `try{...}catch(e){}` only catches JavaScript `throw` — C# exceptions bypass it entirely.

---

## 5. Debugging Round 4: HostMapType root cause

**Breakthrough:** Grepped for `Map`/`Set`/`Symbol`/`Float64Array` in `JavaScriptEngine.cs`. Found:

```csharp
_globals["Map"] = new JsVal { Obj = new HostMapType(_e) };
_globals["Set"] = new JsVal { Obj = new HostSetType(_e) };
```

These are **empty C# marker classes** — they don't implement NiL.JS's prototype/constructor interfaces:

```csharp
private sealed class HostMapType {
    public JavaScriptEngine E;
    public HostMapType(JavaScriptEngine e) { E = e; }
}
private sealed class HostSetType {
    public JavaScriptEngine E;
    public HostSetType(JavaScriptEngine e) { E = e; }
}
```

**Why this kills D3.js v7:**
- D3's minified code starts with `class InternMap extends Map` and `class InternSet extends Set`
- `extends Map` requires `Map` to be a valid constructor with `[[ConstructorKind]]: "base"`
- `HostMapType` is a plain C# object — NiL.JS throws JSException when trying to use it as a base class
- This error is caught internally by NiL.JS's evaluation loop (not propagated to C#), so execution continues silently
- The factory function never completes; `this.d3` is never set

**Similar issues:** `Symbol` (not defined), `Float64Array`/`Uint32Array` (not defined) — used later in D3.

---

## 5b. Debugging Round 5: Polyfill evolution (try-catch wrapper)

**Attempt 1 — `window.Map = Map$`:** `Map$` is created as a JS constructor function, then assigned to `window.Map`. **Result:** `window` in SafeEval scope is disconnected from global scope. `typeof Map` still returns `function` (the original HostMapType), not `Map$`.

**Attempt 2 — bare `Map = Map$`:** Without `window.` prefix, assignment goes to global scope. **Result:** HostMapType is read-only. Assignment silently ignored (`typeof Map = function`, still original).

**Attempt 3 — `_nil.Eval("(function(){...})()")`:** Create polyfill constructor via IIFE, get JSValue, `_nil.DefineVariable("Map").Assign(value)`. **Result:** `InvalidOperationException: Unable to get this-binding for Global Context`. Even `_nil.Eval("(function(){})")` fails.

**Attempt 4 — step-by-step:** Each method defined in separate eval, no IIFE. **Result:** Same `InvalidOperationException` on the first `_nil.Eval("(function(){})")`.

**Attempt 5 — try-catch wrapper evals (current):** Key discovery: `_nil.Eval("try{ (function(){}) }catch(e){}")` **works**. NiL.JS creates proper this-binding when function definition is inside a JS try-catch block (same mechanism that lets `SafeEval("try{ " + d3Content + " }catch(e){}")` handle 279KB of code).

**Current fix (JavaScriptEngine.cs line 5359–5400):** Build polyfill constructors via try-catch wrapper evals + text replacement in d3.js source:

```csharp
// Use try-catch at JS level (gives NiL.JS correct this-binding)
var ctor = _nil.Eval("var _pc; try{ _pc = (function(){ this._d = {}; this.size = 0; }) }catch(e){} _pc");
if (ctor is function) {
    _nil.DefineVariable("__MapPolyfill").Assign(ctor);
    // Define all prototype methods inside try{...}catch(e){}
}
// Text-replace d3.js source before eval
content = content.Replace("class extends Map", "class extends __MapPolyfill");
content = content.Replace("class extends Set", "class extends __SetPolyfill");
```

**Why this works:** `_nil.Eval("try{ ... }catch(e){}")` creates a proper execution context with `this`-binding. Even the 279KB d3.js file executes via `SafeEval("try{ " + content + " }catch(e){}")` — the try-catch wrapper is the key to making NiL.JS handle function definitions.

**Previous attempts that failed:**
- `window.Map = Map$` — `window` in SafeEval scope is disconnected from global scope
- `Map = Map$` — HostMapType is read-only, assign silently ignored
- `_nil.Eval("(function(){...})()")` — throws `InvalidOperationException: Unable to get this-binding for Global Context`
- `_nil.Eval("(function(){})")` — same error, even for empty function
- JS try-catch in eval source (`try{ ... }catch(e){}`) — **does NOT catch C# JSException** (different from JS throw)

**Key insight:** Bare function expressions in `_nil.Eval()` throw `InvalidOperationException`, but wrapping them in `try{ ... }catch(e){}` at JS level works. This is why `SafeEval("try{ " + d3Content + " }catch(e){}")` succeeds while `_nil.Eval("(function(){})")` fails.

---

## 6. Key Technical Findings

| Finding | Implication |
|---------|-------------|
| `RunInline` swallows NiL.JS exceptions silently | External scripts must use `SafeEval` directly |
| `SafeEval` catches all exceptions, returns `Undefined` | Can't detect partial eval failures from return value |
| NiL.JS internal JSException ≠ JavaScript `throw` | JS `try/catch` doesn't help — errors are C# exceptions |
| `HostMapType`/`HostSetType` are empty C# marker objects | `class extends Map/Set` always fails |
| `_nil.Eval("(function(){})")` throws InvalidOperationException | Bare function expressions fail in NiL.JS eval |
| `_nil.Eval("try{ (function(){}) }catch(e){}")` works | try-catch wrapper at JS level provides this-binding |
| `window` in SafeEval scope ≠ global scope | `window.Map = X` doesn't affect global `Map` |
| HostMapType/SetType assignments are silently ignored | `Map = X` has no effect (read-only host objects) |

---

## 7. Files Changed

| File | Change |
|------|--------|
| `JavaScriptEngine.cs` (line 5346) | Non-inline scripts: `RunInline` → direct `SafeEval` |
| `JavaScriptEngine.cs` (line 5359–5400) | try-catch wrapper polyfills (`__MapPolyfill`/`__SetPolyfill`) + text replacement (`class extends Map` → polyfill) |
| `JavaScriptEngine.cs` (line 3624) | `FlushMicrotasks()` after force-exec |
| `JavaScriptEngine.cs` (line 3655-3656) | Post-exec diagnostics (d3, scope, body) |

---

## 8. Remaining Work

- ✅ Polyfills rewritten to try-catch wrapper approach + text replacement — **test needed** to verify D3.js now evaluates successfully
- If D3 loads (`d3=defined`), next blockers: microtask flush (already added), SVG rendering (RenderInlineSvg connected), DOM API completeness (createElementNS, etc.)
- Build: 0 errors
