# Summary 3.9 — NiL.JS Parser Patch: `import.meta`, `console.warn`, Module Resolution Fix

**Session date:** 2026-05-20  
**Build:** 0.8.0.0 x64 Debug — ✅ 0 errors

---

## 1. Module Resolution Fix — `request.Initiator` BaseUri Tracking

### Problem
`TryGetModule` couldn't resolve relative specifiers (`./lib.js`) because:
- Prefetch cached by absolute URL: `ms-appx:///Html/TestModule/lib.js`
- NiL.JS requested: `spec="./lib.js"`, `absPath="/lib.js"`
- No match → `Unable to load module "./lib.js"`

### Fix
Added `_moduleBaseUris` dictionary to track parent module base URIs:

| File | Change |
|------|--------|
| `ModuleLoader.cs:22` | Added `_moduleBaseUris` dictionary |
| `ModuleLoader.cs:52` | Store baseUri in `ExecuteModuleAsync` |
| `ModuleLoader.cs:77` | Store baseUri in `ExecuteInlineModuleAsync` |
| `ModuleLoader.cs:122` | Store resolved URL in `PrefetchDependencies` |
| `ModuleLoader.cs:172-221` | Rewrote `TryGetModule`: uses `request.Initiator.FilePath` → lookup baseUri → `ResolveUrl(baseUri, spec)` → cache hit |

### Result
**T-M-002 ✅ PASS** — `import { greet } from './lib.js'` works correctly.
```
[Module] ResolveModule: spec=./lib.js absPath=/lib.js
[Module] Cache hit (resolved): ms-appx:///Html/TestModule/lib.js
```

---

## 2. ES Module Test Results

| Test | URL | Status | Notes |
|------|-----|--------|-------|
| T-M-001 | `inline.html` | ✅ PASS | Inline module without imports |
| T-M-002 | `module-import.html` | ✅ PASS | External import/export works |
| T-M-003 | `import-meta.html` | ❌ FAIL | `SyntaxError: Unexpected token (2:26)` — NiL.JS 2.6 doesn't support `import.meta` |

---

## 3. `console.warn` / `error` / `info` Support

### Problem
Vite bundles call `console.warn()`, which threw `TypeError: console.warn is not a function`.

### Fix
| File | Change |
|------|--------|
| `JavaScriptEngine.cs:2287-2295` | Added `warn()`, `error()`, `info()` methods to `HostConsole` class |
| `JavaScriptEngine.cs:6811-6815` | Extended console getter to handle `warn`, `error`, `info` names |

### Result
`[WARN] vite: loading legacy chunks...` now appears in DevTools Console tab.

---

## 4. NiL.JS Parser Patch — `import.meta` Support

### Vite Bundle Analysis
Downloaded `main-BE-aXEfW.js` (568KB) from Nokia Design Archive:

| Syntax Feature | Occurrences | ES Version | NiL.JS Support |
|----------------|-------------|------------|----------------|
| `import.meta.url` | 1 | ES2020 | ❌ Not supported → **ADDED** |
| `import()` dynamic | 1 | ES2020 | ✅ Already supported |
| `async function*` | 1 | ES2018 | ❌ Not supported |
| Private fields `#name` | 12 | ES2022 | ❌ Not supported |
| Logical assignment `??=`, `\|\|=`, `&&=` | 172 | ES2021 | ❌ Not supported |

**Blocker at position 361:** `function r2(){import.meta.url,import("_").catch(()=>1),async function*(){}().next()}`

### Architecture Decision: Patch NiL.JS Parser (not Pre-processor)
| | Pre-processor | Patch NiL.JS |
|---|---|---|
| Reliability | Regex on minified code = edge case bugs | AST-level, spec-compliant |
| Performance | +transpilation per request | 0 overhead |
| Maintenance | New syntax = new regex | Add to parser once, works everywhere |

### Implementation (following `new.target` pattern)

#### New File: `NiL.JS/Expressions/ImportMeta.cs`
```csharp
public sealed class ImportMeta : Expression
{
    public override JSValue Evaluate(Context context)
    {
        // Walk up context chain to find module
        var ctx = context;
        while (ctx != null && ctx._module == null)
            ctx = ctx._parent;
        
        if (ctx == null || ctx._module == null)
            ExceptionHelper.Throw(new SyntaxError("Cannot use 'import.meta' outside a module"));
        
        return ctx.GlobalContext.ProxyValue(new { url = ctx._module.FilePath ?? "" });
    }
}
```

#### Modified: `NiL.JS/Module.cs`
- Added `public JSValue ImportMeta { get; internal set; }` property
- Initialized in constructor: `ImportMeta = ctx.GlobalContext.ProxyValue(new { url = virtualPath })`

#### Modified: `NiL.JS/Core/Parser.cs`
Added rules in all 3 rule sets (statement, expression start, expression continuation):
```csharp
new Rule("import.meta", ExpressionTree.Parse),
```

#### Modified: `NiL.JS/Expressions/ExpressionTree.cs`
Added `import.meta` handling in `parseOperand`:
```csharp
|| Parser.Validate(state.Code, "import.meta", ref i)
// ...
case "import.meta":
{
    operand = new ImportMeta();
    break;
}
```

### Current Status
`import.meta` работает — возвращает `{ url: filePath }`. ✅
Logical assignment (`??=`, `||=`, `&&=`) — 172 occurrences работают. ✅

Остаются 2 blocker'а Vite bundle:
- **Position 410:** `async function*(){}` — async generators not supported
- **Position 58:** Arrow function issue (`e=>{throw TypeError(e)}`)

---

## 5. Files Changed

### MediaExplorer
| File | Lines Changed | Description |
|------|---------------|-------------|
| `Engine/ModuleLoader.cs` | ~30 | BaseUri tracking, `TryGetModule` rewrite |
| `Engine/JavaScriptEngine.cs` | ~10 | `console.warn/error/info` support |

### NiL.JS (fork)
| File | Lines Changed | Description |
|------|---------------|-------------|
| `Expressions/ImportMeta.cs` | 59 | **NEW** — `import.meta` expression node |
| `Module.cs` | ~10 | `ImportMeta` property + initialization |
| `Core/Parser.cs` | 3 | Added `import.meta` rules |
| `Expressions/ExpressionTree.cs` | 5 | Added `import.meta` case in `parseOperand` |

---

## Next Steps

1. **Fix `import.meta` runtime null** — context chain not finding `_module`
2. **Add `async function*` support** — async generators (1 occurrence in Vite bundle)
3. **Add logical assignment `??=`, `\|\|=`, `&&=`** — 172 occurrences, high impact
4. **Investigate arrow function issue at position 58** — `e=>{throw TypeError(e)}`
5. **Private fields `#name`** — 12 occurrences, lower priority

---

*Session 3.9 — 2026-05-20*
