# Summary 4.18 — DOM API Stubs + Revert d3js.org Skip (fix `d3=d3_missing`)

**Session date:** 2026-06-06
**Build:** `dotnet build Src\NilJsTest` — ✅ **0 errors**
**Mode:** autopilot — log analysis + DOM stubs + revert aggressive skip

---

## 1. Analysis of Live Test Log

**Build 4.17 changes (skip + DOM stubs) deployed and tested.** Result: `d3=d3_missing` (was `d3_defined` in 4.17).

### Key log trace
```
[DIAG:EXEC] skip d3js.org script (loaded via System.register)      ← 4.17's skip FIRES
...
[DIAG:EXEC] eval https://...polyfills-legacy-0pdt4c7e.js           ← polyfill evaled
Вызвано исключение: NiL.JS.Core.JSException × 2                   ← polyfill throws
d3js-err:undefined                                                  ← from SafeEval catch block
...
[DIAG:SYS] running chunk via SafeEval (589152 bytes)...
Вызвано исключение: NiL.JS.Core.JSException × 3                   ← chunk throws
[DIAG:SYS] post-exec d3=d3_missing                                  ← ❌ d3 MISSING
```

### Root cause of `d3=d3_missing`

The d3js.org content (248KB, d3.v5.16.0) defines the global `d3` at the **top** of the file:
```js
var d3 = {version: "5.16.0"};
```
DOM API access (which throws JSException) happens **later** in the file. So even though eval throws, the `d3` global **persists** in the NiL.JS global scope. The chunk's `System.register` factory references `d` (global `d3`), so it needs this global to be defined.

The skip was **too aggressive** — by skipping the d3js.org eval entirely, we prevented the `d3` global from being defined, causing the chunk to produce `d3=d3_missing`.

### Where `d3js-err:undefined` actually comes from

Line 5439 of `JavaScriptEngine.cs`:
```csharp
SafeEval("try{ " + content + " }catch(e){ try { console.log('d3js-err:'+e.message) }catch(_){} }");
```

The `d3js-err:` prefix is hardcoded in the C# catch wrapper for **every** external script eval. The `e.message = undefined` because the thrown value is not an Error object (DOM API access throws a plain value). In the 4.18 log, this came from the **polyfill eval** (2 JSExceptions), not from d3js.org eval.

### 3 JSExceptions in chunk + ArgumentOutOfRangeException

All non-fatal:
- **3 JSExceptions** from chunk's System.register `execute()` — DOM API gaps (same as before)
- **ArgumentOutOfRangeException from System.Text.RegularExpressions.dll** — internal .NET issue during large-string processing, caught by outer try/catch

These existed in 4.17 too and didn't prevent `d3=d3_defined`.

---

## 2. Fixes Applied

### 2.1 Revert d3js.org skip (CRITICAL)
- **Remove skip in `Execute`** (was lines 5327-5332)
- **Remove skip in immediate loop** (was lines 5467-5472)
- d3js.org content now evaled normally → `d3` global defined → `d3=d3_defined` restored

### 2.2 Added `namespaceURI` to `JsDomElement`
Returns `"http://www.w3.org/2000/svg"` for SVG tags, `"http://www.w3.org/1999/xhtml"` for HTML. Prevents d3 from throwing when checking `document.documentElement.namespaceURI`.

### 2.3 Added `ownerDocument` to `JsDomElement`
Returns `JsDocument` reference (or null if no root). Prevents TypeError on `element.ownerDocument` access.

### 2.4 DOM stubs from 4.17 (kept)
- `parentElement`, `closest`, `nextElementSibling`, `previousElementSibling`
- `scrollIntoView` (no-op)
- `createDocumentFragment` (returns JsDomElement)
- Globals: `Element`, `HTMLElement`, `SVGElement`, `Node`, `Text`, `DOMTokenList`

---

## 3. Expected Result After This Build

```
[DIAG:EXEC] eval https://d3js.org/d3.v7.min.js
[DIAG:EXEC] polyfill strip-extends done hasMap=False hasSet=False    ← d3.v5 has no extends Map/Set
Вызвано исключение: NiL.JS.Core.JSException × 1 or 0              ← may be 0 now due to namespaceURI stub
d3js-err:undefined if exception                                    ← from catch block, harmless
...
[DIAG:SYS] post-exec d3=d3_defined                                  ← ✅ restored
```

---

## 4. Files Changed

| File | Change |
|------|--------|
| `JavaScriptEngine.cs` (was 5327-5332) | **Removed** d3js.org skip in `Execute` — eval d3 normally |
| `JavaScriptEngine.cs` (was 5467-5472) | **Removed** d3js.org skip in immediate loop |
| `JavaScriptEngine.cs` (JsDomElement) | Added `namespaceURI` (SVG/XHTML namespace) |
| `JavaScriptEngine.cs` (JsDomElement) | Added `ownerDocument` (JsDocument ref) |
| `JavaScriptEngine.cs` (JsDomElement) | (4.17) Added `parentElement`, `closest`, `nextElementSibling`, `previousElementSibling`, `scrollIntoView` |
| `JavaScriptEngine.cs` (HostDocument) | (4.17) `createDocumentFragment` returns JsDomElement |
| `JavaScriptEngine.cs` (global setup) | (4.17) Added `Element`, `HTMLElement`, `SVGElement`, `Node`, `Text`, `DOMTokenList` |
