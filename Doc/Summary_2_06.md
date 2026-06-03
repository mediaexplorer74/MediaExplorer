# Summary_2_6 — Phase 6 (ES Modules)

## Goal
Turn NiL.JS into a real ES module loader via its native `Module.ResolveModule` event + `Context.Eval`, replacing the old transpile-and-MiniRunner approach.

## Changes

### Files modified
- `Engine\ModuleLoader.cs` — complete rewrite (NiL.JS native module API)
- `Engine\JavaScriptEngine.cs` — ModuleLoader init moved after `_nilInit()`, `_moduleLoader` field no longer `readonly`
- `Engine\ResourceManager.cs` — `Array.ConvertAll` → LINQ `.Select().ToArray()`
- `Engine\JavaScriptEngine.cs` — `#if USE_NILJS` guards for MiniRunner dead code
- `MainPage.xaml.cs` — braces around `if`/`else` with `var _ = ...`
- `WEBVIEW.csproj` — added `<Compile Include>` for `OpenRouterClient.cs`
- `Engine\OpenRouterClient.cs` — `TryAppendWithoutValidation` → `Headers.Add`

---

### 1. Native NiL.JS module loading

**Before:** ModuleLoader transpiled `import`/`export` via ad-hoc regex, wrapped in an IIFE shim (`window.__esM[key]`), and executed through `ExecuteScriptBlock` → `_nil.Eval`.

**After:** Uses NiL.JS's built-in module pipeline:

```
<script type="module" src="app.js">
  → ModuleLoader.ExecuteModuleTagAsync
    → Prefetch all transitive imports (regex scan, recursive fetch)
    → Cache each as NiL.JS.Module(key, source) with Context set via reflection
    → _nil.Eval(source) — NiL.JS natively parses import/export, fires ResolveModule
```

Key components:

**`Module.ResolveModule` event handler** — subscribed once, handles every `import` specifier encountered during `_nil.Eval`:
- Resolves relative → absolute (via `location.href`)
- Resolves bare specifiers via `importMap`
- Falls back to synchronous fetch (`GetAwaiter().GetResult()`) for uncached modules
- Returns cached `NiL.JS.Module` objects

**Pre-fetch** — before executing the top-level module, `PrefetchDependencies` scans `import` statements via regex, recursively fetches and caches all transitive dependencies as `NiL.JS.Module` objects with their `Context` set.

**NiL.JS handles:** live bindings, circular deps, export scoping — no manual transpilation needed.

---

### 2. ImportMap support (wired)

`SetImportMap(Dictionary<string, string>)` stores bare specifier → URL mappings. The `ResolveModule` handler checks the import map before attempting URL resolution.

---

### 3. Script module detection (pre-wired)

JavaScriptEngine.cs already had:
- Module classification (`type == "module"` → deferred)
- Dispatch to `_moduleLoader.ExecuteModuleTagAsync()`
- Script budget check (16KB per module)

No changes needed.

---

### 4. Build errors fixed

| Error | File | Fix |
|-------|------|-----|
| CS1023 | `MainPage.xaml.cs:148` | Added braces around `if`/`else` with `var _ = ...` |
| CS0117 | `Engine\ResourceManager.cs:613` | `Array.ConvertAll` → `args.Select(a => a.Name).ToArray()` |
| CS0136 | `Engine\JavaScriptEngine.cs:3703` | Renamed `token` → `xhrToken` (scope conflict) |
| CS0103 | `Engine\JavaScriptEngine.cs:755` | Wrapped dead MiniRunner call in `#if USE_NILJS` |
| CS0103 | `Engine\JavaScriptEngine.cs:1172` | Same |
| CS0122 | `Engine\JavaScriptEngine.cs:2817` | `ExecuteCachedInline` — MiniRunner path behind `#if !USE_NILJS` |
| CS0103 | `MainPage.xaml.cs:859` | Added `<Compile Include>` for `OpenRouterClient.cs` to csproj |
| CS1061 | `Engine\OpenRouterClient.cs:27` | `TryAppendWithoutValidation` → `Headers.Add` |
| CS0200 | `Engine\ModuleLoader.cs` | Module.Context is private setter — use reflection (`MethodInfo.Invoke`) |
| CS0104 | `Engine\ModuleLoader.cs` | `Module` ambiguous with `System.Reflection.Module` — alias `JSModule = NiL.JS.Module` |
| CS1061 | `Engine\ModuleLoader.cs` | `Context.GetValue` → `GetVariable`, `JSValue.IsUndefined` → `ValueType == JSValueType.Undefined` |

## Build result
- **0 errors** (MSBuild 17.14 / x86 Debug)
- **~220 lines** ModuleLoader rewrite, ~90 lines fixes elsewhere

## Next
Phase 7 (Service Worker + Offline) — fetch interception in ResourceManager, `navigator.serviceWorker.register()`.
