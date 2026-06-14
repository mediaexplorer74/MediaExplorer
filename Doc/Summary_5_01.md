# Summary 4.20 — Phase I: DOM Iterable Fix Implementation

**Session date:** 2026-06-06 (autopilot + human implementation)
**Based on:** Plan_05.md Phase I - Iterable Horizon
**Build:** NilJsTest builds successfully ✅

---

## 1. Problem Solved

As identified in Summary_4_19.md, the Nokia Design Archive chunk (`main-BE-aXEfW.js`) was crashing with a fatal `StackOverflowException` when evaluating:

```js
for (const o of document.querySelectorAll('link[rel="modulepreload"]')) n(o);
```

**Root cause:** `HostDocument.querySelectorAll` and similar DOM collection methods returned plain .NET `object[]` arrays, which are not `IIterable` in NiL.JS. When the JS engine tried to call `ToIterable()` on these arrays, it threw a `TypeError`, which propagated as a fatal exception.

## 2. Solution Implemented

Added `ToJsArray` helper usage to all DOM collection-returning host function wrappers. The helper uses `Context.CurrentGlobalContext.ProxyValue(items)` which wraps .NET collections as `NativeList` implementing `IIterable`.

### 2.1 Helper Method (Already Existed)
Location: `Src/MediaExplorer/Engine/JavaScriptEngine.cs:8215`

```csharp
private static JSValue ToJsArray(IEnumerable<object> items)
{
    return Context.CurrentGlobalContext.ProxyValue(items);
}
```

### 2.2 Fixed Host Function Wrappers

| Location | Method | Change |
|----------|--------|--------|
| Line 8280-8304 | `document.querySelectorAll` | Returns `ToJsArray(results)` instead of `JsVal { Obj = results }` |
| Line 8306-8330 | `document.getElementsByTagName` | Returns `ToJsArray(list)` instead of `JsVal { Obj = list }` |
| Line 8332-8356 | `document.getElementsByClassName` | Returns `ToJsArray(list)` instead of `JsVal { Obj = list }` |
| Line 8506 | `element.querySelectorAll` | Returns `ToJsArray(list)` instead of `JsVal { Obj = list }` |

### 2.3 Added HostElement Property Handlers

| Location | Property | Implementation |
|----------|----------|----------------|
| Line 8598 | `element.children` | Creates `List<HostElement>`, wraps with `ToJsArray` |
| Line 8599 | `element.childNodes` | Creates `List<HostElement>`, wraps with `ToJsArray` |
| Line 8600 | `element.getElementsByTagName` | Creates `List<HostElement>`, wraps with `ToJsArray` |
| Line 8601 | `element.getElementsByClassName` | Creates `List<HostElement>`, wraps with `ToJsArray` |

### 2.4 Type Changes

- Changed internal result lists from `List<JsVal>` to `List<object>` containing `HostElement` objects
- Wrapped all results with `ToJsArray(list)` which returns a NiL.JS `JSValue` (NativeList)
- Wrapped JSValue in `JsVal { Obj = ... }` for compatibility with JsMiniRunner

## 3. Expected Outcome

After these changes:
- `for...of` over `document.querySelectorAll()` works without crashes
- `for...of` over `element.children`, `element.childNodes` works
- `for...of` over `getElementsByTagName()`, `getElementsByClassName()` works
- Nokia Archive chunk can progress past the `modulepreload` IIFE
- No fatal `StackOverflowException` from iteration attempts

## 4. Files Modified

| File | Lines Changed | Description |
|------|---------------|-------------|
| `Src/MediaExplorer/Engine/JavaScriptEngine.cs` | 8280-8304, 8306-8330, 8332-8356, 8506, 8598-8601 | Updated host function wrappers to use ToJsArray |

## 5. Next Steps (Per Plan_05.md)

1. **Test the changes** - Deploy to emulator and verify Nokia Archive chunk loads
2. **Phase J** - Binary search the remaining portion of `main-BE-aXEfW.js` to find any remaining crash sites
3. **Phase G.1** - Implement SVG serialization to SvgImageSource for visual rendering
4. **Add NilJsTest coverage** - Add test case for for-of over querySelectorAll

## 6. Test Case for NilJsTest (To Be Added)

```csharp
// In Src/NilJsTest/Program.cs or a new test file
// Test: for-of over querySelectorAll result
var engine = new JavaScriptEngine();
engine.RunInlineHtml("<div id='a'></div><div id='b'></div>");
var result = engine.SafeEval(@"
    var items = [];
    for (var el of document.querySelectorAll('div')) { items.push(el.tagName); }
    items.join(',');
");
Assert("for-of querySelectorAll", result?.ToString() == "DIV,DIV");
```

---

*Next session: 3.43 — Verify Phase I fix, begin Phase J binary search*
