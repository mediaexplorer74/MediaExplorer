# Summary 4.09 — Minimal SystemJS: Replacing Broken Polyfill

**Session date:** 2026-06-05
**Build:** `msbuild MediaExplorer.sln /p:Configuration=Debug /p:Platform=x64` — ✅ **0 errors** (2 warnings, pre-existing)
**Mode:** code + live VS run

---

## 1. Problem

The Vite polyfills-legacy script creates `System` via a `SystemJS()` constructor. In NiL.JS this constructor produces an **object with zero enumerable properties** — no `register`, no `registry`, no `resolve`. Diagnostics confirmed:

```
[DIAG:SYS] System keys:        ← empty! for...in finds nothing
[DIAG:SYS] System.registry missing or null
[DIAG:SYS] registry polyfill injected   ← our C# fallback added one
[DIAG:SYS] System undefined             ← force-execute snippet can't find System
```

`for...in` on the polyfill's `System` returns nothing because all properties are set as non-enumerable (`Object.defineProperty` style), which NiL.JS doesn't surface through enumeration. The main-legacy chunk calls `System.register(...)` which throws 2 JSExceptions (method doesn't exist). Modules are never stored and D3.js never executes.

---

## 2. Root Cause

The polyfill's SystemJS constructor uses ES6 features (`Map`, `Symbol`, `Proxy`) that NiL.JS doesn't fully support. The resulting object:

- `typeof` returns `"object"` (C# `SafeEval` finds it)
- Zero enumerable keys (no `register`, `import`, `resolve`, etc.)
- C# `SafeEval("globalThis.System")` returns a non-null JSValue, but JS `globalThis.System` from within an IIFE returns `undefined` (NiL.JS scoping quirk)
- No way to patch or extend it without the same constructor failures

---

## 3. Fix: Replace `globalThis.System` Entirely

Instead of patching the broken polyfill object, we **replace** `globalThis.System` with a minimal implementation **before** the legacy chunk runs. This goes into the `__sysImport` C# host function (the entry point for all `System.import()` calls).

### 3.1 Before chunk — create minimal System (JS snippet via RunInline)

```javascript
globalThis.System = {
    registry: { _entries: {}, _modId: 0,
        set: function(k,v) { this._entries[k] = v; },
        get: function(k) { return this._entries[k]; },
        forEach: function(fn) { for(var k in this._entries) fn(this._entries[k], k); }
    },
    register: function(deps, declare) {
        var url = 'mod:' + (++registry._modId);
        registry.set(url, { deps: deps || [], declare: declare, url: url });
        __diagLog('[DIAG:SYS] register url=' + url);
    },
    import: function(url) { return __sysImport(url); },
    resolve: function() { return ''; }
};
```

Key points:
- `register(deps, declare)` auto-generates module IDs, stores entry in `_entries`
- `import` delegates to the existing C# `__sysImport` host function
- `registry` is a plain JS object with `_entries` dictionary
- All properties are **enumerable** — `for...in` works
- **Called BEFORE** `RunInline(mainLegacyChunk)` so the chunk finds a working `System.register`

### 3.2 After chunk — force-execute all registered modules

```javascript
var S = globalThis.System;
// iterate S.registry._entries
for(var url in _e) {
    var m = _e[url];
    if (m && typeof m.declare === 'function') {
        // call declare(_export, _context) to get { execute }
        var declared = m.declare(function(n,v){}, { meta: { url: url } });
        if (declared && typeof declared.execute === 'function') {
            declared.execute();  // run the module
        }
    }
}
```

This replaces the previous approach that tried to inject a registry into the broken polyfill System.

### 3.3 Files changed

| File | Change |
|------|--------|
| `Engine/JavaScriptEngine.cs` | Lines 3403–3443: replaced registry injection + `__sysRef` workaround with full System replacement (create + force-execute). Removed `_nil.DefineVariable("__sysRef")` experiment. |

### 3.4 Old approach removed

- Registry injection snippet: `if (_S && !_S.registry) { _S.registry = ... }` — was useless because polyfill System has no `register` method
- `__sysRef` variable trick: `_nil.DefineVariable("__sysRef").Assign(sysRef)` — workaround for `globalThis` scoping issue, no longer needed because we create System directly via JS

---

## 4. Build

```
msbuild "Src\MediaExplorer.sln" /p:Configuration=Debug /p:Platform=x64
```
- **0 errors**, 2 pre-existing warnings (APPX4001, APPX1503)
- NiL.JS (netstandard1.4) builds clean

---

## 5. Expected Diagnostics

When the app runs (F5 in VS), the Debug Output / DevTools console should show:

```
[DIAG:SYS] creating minimal System...
[DIAG:SYS] register url=mod:1
[DIAG:SYS] register url=mod:2
... (one per module in the legacy chunk)
[DIAG:SYS] EXEC OK url=mod:X
... (one per successfully executed module)
[DIAG:SYS] force-exec done: N entries, M executed
Phase3 JS DONE
```

If D3.js visualizations appear, the Nokia milestone is reached.

---

## 6. Remaining Work

| Task | Priority |
|------|----------|
| Live VS test of new System | 🔴 |
| Phase S.2 — JS timeout | 🟡 |
| Phase S.5 — Error page | 🟡 |
| Phase S.6 — Cascade/layout guards | 🟡 |
| Phase V — Visual polish | 🟢 |

---

## 7. Key Decisions

- **Abandon polyfill repair:** Trying to inject `register`/`registry` into the broken polyfill System is fragile and doesn't fix the non-enumerable properties issue. Complete replacement is simpler and more reliable.
- **Own minimal SystemJS:** Mirrors exactly what Vite's legacy format expects: `System.register(deps, declare)`, `System.registry`, `System.import`. No dependency resolution in initial version — modules that need deps from other modules will fail silently (to be fixed if D3 requires it).
- **Plain JS, not C#:** The replacement is a JS snippet evaluated via `RunInline`. This avoids C#-to-JS marshalling issues and keeps all logic in NiL.JS's own context.
- **Keep `__sysImport` as C# host function:** The fetch + RunInline orchestration stays in C# (thread safety, `FetchScriptStringAsync`, error handling).

---

*Summary v4.13 — 2026-06-05 (session 3.33: minimal SystemJS replacement)*
*Build: ✅ 0 errors*
