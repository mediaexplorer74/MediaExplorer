# Summary 3.8 — NiL.JS 2.6 Integration, GlobalContext Fix & ES Module Test Results

**Session date:** 2026-05-19 (Night)  
**Build:** 0.8.0.0 x64 Debug — ✅ 0 errors (after GlobalContext fix)

---

## 1. NiL.JS 2.6 Integration — ModuleLoader Rewrite

### Background
NiL.JS 2.5.1294 (NuGet) cannot parse `import`/`export` declarations. Version 2.6 (source in `Src/NiL.JS`) has native ES Module support via `IModuleResolver` and `Module.ModuleResolversChain`.

### Changes

| File | Change |
|------|--------|
| `MediaExplorer.csproj` | Replaced `PackageReference NiL.JS 2.5.1294` → `PackageReference NiL.JS 2.6.0-local` (local NuGet package) |
| `nuget.config` | Added `LocalNiLJS` source → `local-nuget/` |
| `Engine/ModuleLoader.cs` | Full rewrite: `IModuleResolver` implementation, `Module.ModuleResolversChain`, `RunModule()`, `_fetchTasks` for async prefetch |
| `Engine/JavaScriptEngine.cs` | `_nil` type: `Context` → `GlobalContext`; `_nilInit()`: `new Context()` → `new GlobalContext()` |

### ModuleLoader Architecture (NiL.JS 2.6)

```
ModuleLoader : IModuleResolver
├── ResolveModule(path) → checks _moduleCache, returns cached JSModule
├── RunModule(key, code) → new JSModule(key, code, _nil) → .Run()
├── FetchScriptStringAsync(url) → ResourceManager or ms-appx StorageFile
└── _importRegex → extracts import specifiers from inline script text
```

### Key API Differences (2.5 → 2.6)

| 2.5 (old) | 2.6 (new) |
|-----------|-----------|
| `Context.ResolveModule += handler` | `Module.ModuleResolversChain.Add(resolver)` |
| `ResolveModuleEventArgs` | `IModuleResolver` interface |
| `Context _nil` | `GlobalContext _nil` |
| `module.Run()` | `module.Run()` (same, but requires `GlobalContext` ctor) |

---

## 2. The `InvalidCastException` Bug

### Symptom
All 3 ES Module tests failed with:
```
[Module] Eval error: Unable to cast object of type 'NiL.JS.Core.Context' to type 'NiL.JS.Core.GlobalContext'.
```

### Root Cause
`JSModule` constructor signature in NiL.JS 2.6:
```csharp
public Module(string path, string code, GlobalContext globalContext)
```
`_nil` was initialized as `new Context()` (base class), but `JSModule` requires `GlobalContext` (derived class). The cast `(GlobalContext)_nil` throws `InvalidCastException` at runtime.

### Fix
| File | Before | After |
|------|--------|-------|
| `JavaScriptEngine.cs:45` | `private Context _nil;` | `private GlobalContext _nil;` |
| `JavaScriptEngine.cs:2427` | `_nil = new Context();` | `_nil = new GlobalContext();` |
| `ModuleLoader.cs:16` | `private readonly Context _nil;` | `private readonly GlobalContext _nil;` |
| `ModuleLoader.cs:24` | `public ModuleLoader(JavaScriptEngine engine, Context nil)` | `public ModuleLoader(JavaScriptEngine engine, GlobalContext nil)` |

The `(GlobalContext)` cast was removed from `ModuleLoader.cs` — no longer needed since `_nil` is now typed correctly.

### Build Result
```
0 errors, 1 warning (CS0414: _errorOverlayVisible unused)
```

---

## 3. ES Module Test Results (Post-Fix Pending)

### T-M-001 — Inline Module (no imports)
**URL:** `ms-appx:///Html/TestModule/inline.html`  
**Status:** ⏳ Needs re-test after GlobalContext fix  
**Previous result:** `[Module] Eval error: InvalidCastException`

### T-M-002 — External import/export
**URL:** `ms-appx:///Html/TestModule/module-import.html`  
**Status:** ⏳ Needs re-test after GlobalContext fix  
**Previous result:** 
- `[Module] Found 1 import(s)` ✅
- `[Module] Prefetching dependency: lib.js` ✅
- `[Module] ms-appx file found: ...lib.js` ✅
- `[Module] ms-appx OK: 94 bytes` ✅
- `[DIAG] RenderAsync Phase3 JS EXC InvalidCastException` ❌

### T-M-003 — import.meta.url
**URL:** `ms-appx:///Html/TestModule/import-meta.html`  
**Status:** ⏳ Needs re-test after GlobalContext fix  
**Previous result:** `[Module] Eval error: InvalidCastException`

### Key Observation from T-M-002 Log
The `ms-appx` fetch pipeline works correctly:
```
[Module] ms-appx fetch: ms-appx:///Html/TestModule/lib.js
[Module] ms-appx file found: C:\...\AppX\Html\TestModule\lib.js
[Module] ms-appx OK: 94 bytes
```
The only failure was the `InvalidCastException` when creating `JSModule`.

---

## 4. NiL.JS 2.6 `GlobalContext` Details

`GlobalContext` is a sealed class in NiL.JS that extends `Context`:
```csharp
public sealed class GlobalContext : Context
{
    public GlobalContext();
    public GlobalContext(string name);
    // ... prototype initialization, variable registry, etc.
}
```

It provides:
- `ProxyValue(object)` — marshal .NET objects to JS values
- `GetConstructor(Type)` — get JS constructor for .NET type
- `GetPrototype(Type)` — get JS prototype for .NET type
- `_booleanPrototype`, `_numberPrototype`, `_stringPrototype`, etc.

`Context.DefaultGlobalContext` is a static singleton `GlobalContext` created at startup.

---

## 5. Files Changed in This Session

| File | Lines Changed | Description |
|------|---------------|-------------|
| `Engine/JavaScriptEngine.cs` | 2 | `_nil` type + initialization |
| `Engine/ModuleLoader.cs` | 2 | `_nil` field type + constructor parameter type |

---

## Next Steps

1. **Re-run ES Module tests** (T-M-001, T-M-002, T-M-003) — verify `GlobalContext` fix resolves `InvalidCastException`
2. **Check `import.meta.url` support** — NiL.JS 2.6 may still not support `import.meta` (known limitation)
3. **Check `typeof import` support** — NiL.JS 2.6 may still not parse this syntax
4. **Nokia Archive validation** — test with real Vite bundles
5. **Update Plan_03.md** — mark Phase 20 as IN PROGRESS

---

*Session 3.8 — 2026-05-19*
