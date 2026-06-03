# Summary 3.20 — NiL.JS netstandard1.4 Migration for W10M 15063 + Full Solution Build

**Session date:** 2026-06-03  
**Build:** `msbuild MediaExplorer.sln /p:Configuration=Debug /p:Platform=x86` — ✅ **0 errors**  
**Test result:** N/A (build/compile session only)

---

## 1. Changes Applied

### Fix 1: NiL.JS netstandard2.0 → netstandard1.4 Migration

**Problem:** MediaExplorer targets `TargetPlatformMinVersion=10.0.15063.0` (Windows 10 Mobile Anniversary Update). NiL.JS was compiled as `netstandard2.0`, which is NOT supported by the .NET Standard library version bundled with UWP 15063 (`NETStandard.Library` 1.x). This would cause `MissingMethodException` / type-not-found crashes at runtime on W10M devices.

**Solution:** Migrated NiL.JS from `netstandard2.0` to `netstandard1.4` — the highest .NET Standard version compatible with UWP 15063. This required polyfilling ~120 missing API surfaces at compile time.

#### What was done in `Src/NiL.JS`:

**a) Target framework:** `NiL.JS.csproj` → `<TargetFramework>netstandard1.4</TargetFramework>`

**b) Polyfills added to `Backward.cs`** (under `#if NETSTANDARD1_4`):

| Category | Types/Methods |
|----------|---------------|
| Missing types | `SerializableAttribute`, `NonSerializedAttribute`, `ICloneable`, `AssemblyLoadEventArgs`, `ApplicationException`, `DBNull`, `RuntimeWrappedException`, `ExpandoObject` |
| Enums | `NormalizationForm` (System.Text), `MemberTypes`, `BindingFlags` |
| Threading | `Thread` (stub — Sleep only, Name/IsAlive removed) |
| Diagnostics | `StackTrace`, `StackFrame` polyfill + `GetFrames()`, `DebuggerPolyfill.Log` |
| Reflection — Type | `IsClass`, `IsInterface`, `IsEnum`, `IsValueType`, `IsAbstract`, `ContainsGenericParameters`, `BaseType`, `GetGenericArguments`, `GetInterfaces`, `GetInterface`, `GetTypeCode`, `GetMethods`, `GetFields`, `GetProperties`, `GetConstructors`, `GetConstructor`, `GetMembers`, `GetMethod` (4 overloads), `GetField` (2 overloads), `GetProperty` |
| Reflection — MemberInfo | `GetMemberType()` |
| Reflection — PropertyInfo | `GetGetMethod()`, `GetSetMethod()` + `(bool)` overloads |
| Reflection — EventInfo | `GetAddMethod()` + `(bool)` overload |
| Reflection — Emit | `TypeBuilderPolyfill.CreateType()` → `.AsType()` |

**c) Guards added** (`#if !NETSTANDARD1_4` / `#if NETSTANDARD1_4`):
- `BinaryTree.cs` — serialization guards extended
- `NamespaceProvider.cs` — entire class guarded
- `Module.cs` — `ClrNamespace()` guarded
- `Proxy.cs` — `[Serializable]`, `[NonSerialized]`, `ConstructorProxy` path
- `CompiledNode.cs` — `[NonSerialized]`
- `GlobalContext.cs` — `System.Dynamic` import, `ExpandoObject` usage, `GetTypeCode` call
- `ObjectDesctructor.cs` — `System.Data` import
- `String.cs` — `Normalize()` calls guarded
- `GlobalFunctions.cs` — Thread-based `__pinvoke` guarded
- `ExceptionHelper.cs` — `StackTrace(Exception, bool)` → `StackTrace(Exception)`
- `Array.cs` — `GetFrame().GetMethod()` path guarded
- `ExternalFunction.cs` — `.Method` → `.GetMethodInfo()` for `NETSTANDARD1_4`
- `ForIn.cs`, `ForOf.cs` — `Array.AsReadOnly` → `new ReadOnlyCollection`
- `DoWhile.cs` — `Debugger.Log` → `DebuggerPolyfill.Log`
- `Backward.cs` — `GetInterface`, `GetTypeCode` guarded out of `Backward` class for 1.4 (moved to `TypePolyfill`)

**d) Call-site fixes** (property-style → `GetTypeInfo()`):
- Changed `type.IsValueType` → `type.GetTypeInfo().IsValueType` (15+ files)
- Changed `type.IsClass` → `type.GetTypeInfo().IsClass`
- Changed `type.IsEnum` → `type.GetTypeInfo().IsEnum`
- Changed `type.IsInterface` → `type.GetTypeInfo().IsInterface`
- Changed `type.IsAbstract` → `type.GetTypeInfo().IsAbstract`
- Changed `type.BaseType` → `type.GetTypeInfo().BaseType`
- Changed `type.ContainsGenericParameters` → `type.GetTypeInfo().ContainsGenericParameters`
- Changed `type.GetGenericArguments()` → `type.GetTypeInfo().GenericTypeArguments`
- Changed `Type.GetTypeCode(typeof(T))` → `typeof(T).GetTypeCode()` (extension method)
- Changed `Delegate.Method` → `Delegate.GetMethodInfo()`
- Changed `ExternalFunctionDelegate.Method` → `((Delegate)d).GetMethodInfo()`
- Changed `string.GetEnumerator()` → `((IEnumerable<char>)str).GetEnumerator()`
- Changed `[BindingFlags.X | Y]` → `BindingFlags.X | Y` (collection expression → bitwise)

**Result:** `NiL.JS.dll` compiles as `netstandard1.4` with **0 errors**, 3 warnings (NETSDK1215 — expected for retro target).

---

### Fix 2: MediaExplorer — `UriPartial`/`GetLeftPart` unavailable in UWP

**Problem:** `JavaScriptEngine.cs` used `Uri.GetLeftPart(UriPartial.Authority)` which is not available in the UWP API surface.

**Fix:** Replaced with equivalent string concatenation: `Uri.Scheme + "://" + Uri.Authority`

**File changed:** `Engine/JavaScriptEngine.cs` (2 occurrences: `HostLocation.origin` property + `_nilInit()` window.origin)

---

## 2. Build Verification

| Project | Framework | Result |
|---------|-----------|--------|
| `NiL.JS` | `netstandard1.4` | ✅ 0 errors |
| `MediaExplorer` | `uap10.0.15063` | ✅ 0 errors |
| `MediaExplorer.sln` | Full solution | ✅ `.appxbundle` produced |

---

## 3. Files Modified (NiL.JS — 20+ files)

| File | Change |
|------|--------|
| `NiL.JS.csproj` | `netstandard2.0` → `netstandard1.4` |
| `Backward.cs` | Massive polyfill block (~250 lines) |
| `BinaryTree.cs` | Serialization guards |
| `NamespaceProvider.cs` | Entire class guarded |
| `Module.cs` | `ClrNamespace` guarded |
| `Proxy.cs` | Serializable, NonSerialized, ConstructorProxy, is Type → is TypeInfo |
| `CompiledNode.cs` | NonSerialized guard |
| `GlobalContext.cs` | Dynamic import, ExpandoObject, GetTypeCode |
| `ObjectDesctructor.cs` | Data import guard |
| `String.cs` | Normalize guard, GetEnumerator fix |
| `GlobalFunctions.cs` | Thread/__pinvoke guard |
| `ExternalFunction.cs` | Method → GetMethodInfo |
| `ExceptionHelper.cs` | StackTrace constructor fix |
| `Array.cs` | GetMethod guard |
| `ForIn.cs`, `ForOf.cs` | Array.AsReadOnly → ReadOnlyCollection |
| `DoWhile.cs` | Debugger.Log → DebuggerPolyfill.Log |
| `Tools.cs` | 6 property→GetTypeInfo fixes |
| `JSValueExtensions.cs` | 3 GetTypeCode, IsClass, IsInterface, GetMethods fixes |
| `CodeNode.cs` | 5-arg GetMethod → GetDeclaredMethods |
| `JITHelpers.cs` | 3 GetMethod(name,Type[]), GetTypeCode fixes |
| `ConstructorProxy.cs` | 5 property→GetTypeInfo fixes |
| `MethodProxy.cs` | Collection expression, IsValueType fixes |
| `SparseArray.cs` | Added `using System.Reflection` |
| `NativeReadOnlyList.cs` | Added `using System.Reflection` |
| `NativeList.cs` | Delegate.Method → GetMethodInfo, GetTypeCode |
| `Context.cs` | (no change needed — Delegate.GetMethodInfo conflict resolved) |
| `ConvertToInteger.cs` | (no change needed) |

## 4. Files Modified (MediaExplorer)

| File | Change |
|------|--------|
| `Engine/JavaScriptEngine.cs` | `Uri.GetLeftPart(UriPartial.Authority)` → `Uri.Scheme + "://" + Uri.Authority` |

---

## 5. Known Issues

- **Vertical stretch in flat Canvas rendering** — `VirtualizingRenderer.cs` has the `TextBlock` Height fix (remove explicit Height) but user hasn't rebuilt with this change. If stretch persists, check `ScrollViewer` in `StackPanel` (ContentHost).
- **`iana.org` layout** — CSS Grid/Flexbox not fully supported (pre-existing, Phase 16.5+)
- **`NETSDK1215` warning** — Expected: .NET Standard < 2.0 is deprecated per Microsoft guidance. No runtime impact.

---

## 6. Next Steps

Return to main track — improve content rendering compatibility:
1. **Rebuild and test** — verify the `TextBlock` Height fix resolves vertical stretch
2. **Fix `iana.org` layout** — CSS Grid/Flexbox for complex layouts
3. **Continue Phase 16/16.5+** — CSS custom properties (`var()`), `@media` queries, more properties
4. **Remove dead `FlexPanel.cs`** from project once flat Canvas is validated

---

*Summary v3.20 — 2026-06-03*
