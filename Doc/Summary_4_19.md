# Summary 4.19 — For‑of iteration crash & fix plan

**Session date:** 2026-06-06 (autopilot continuation)
**Build:** `dotnet build Src\MediaExplorer` — ✅ **0 errors**
**Mode:** analysis & documentation update

---

## 1. Problem discovered

When evaluating the Nokia Design Archive chunk (`main-BE-aXEfW.js`) the engine crashes after the IIFE following `r2`.  Isolating the code shows that any use of:

```js
for (const o of document.querySelectorAll('link[rel="modulepreload"]')) n(o);
```
or even a simple:

```js
var a = document.querySelectorAll('link[rel="modulepreload"]'); a;
```
causes a fatal `StackOverflowException`/unprintable exception in the .NET host.  The same happens with `document.createElement('link')`.

### Root cause
`HostDocument.querySelectorAll` (and `getElementsByTagName`, etc.) returns a **plain .NET `object[]`**.  When the engine evaluates the `for…of` loop it calls `source.ToIterable()`.  `source.Value` is the raw array, which is **not an `IIterable`** and does not provide a `Symbol.iterator` function.  Consequently `ToIterable` throws a `TypeError` (`source is not iterable`).  The exception propagates out of the engine and results in an unrecoverable crash.

## 2. Immediate mitigation steps

1. **Change `HostDocument.querySelectorAll` (and similar methods) to return a proper JavaScript array** via `JSValue.Marshal` / `ProxyValue`, which yields a `NativeList` that implements `IIterable`.
2. Ensure `document.createElement` already returns a `JsDomElement` (which is iterable‑friendly), but verify that any host‑returned collections are wrapped appropriately.
3. Add a tiny helper in `JavaScriptEngine` for safe conversion:
   ```csharp
   private static JSValue ToJsArray(IEnumerable<object> items)
       => Context.CurrentGlobalContext.ProxyValue(items);
   ```
   and use it in the host methods.
4. Update the host‑function wrapper for `querySelectorAll` (lines around 8438) to marshal the returned list into a `NativeList` rather than a raw `List<JsVal>`.

## 3. Planned implementation

- Modify `HostDocument.querySelectorAll` to build a `List<object>` and return `Context.CurrentGlobalContext.ProxyValue(list)`.  This will be automatically wrapped as a `NativeList` with iterator support.
- Similarly adjust `getElementsByTagName` and any other methods that currently return `object[]`.
- Add unit tests in `NilJsTest` that instantiate `JavaScriptEngine` (via `JsZeroRuntime`) and run the problematic snippets to confirm the crash is gone.
- Verify the full Nokia chunk runs without fatal errors after these changes.

## 4. Documentation updates

- **Plan_04.md** updated with detailed next‑step bullet list (see modifications).
- Added `document.getElementsByClassName` wrapper to JsDocument and host‑function mapping, returning a `NativeList` (JS iterable).
- Added this **Summary_4_19.md** to capture the analysis and fix plan.

---

## 5. Expected outcome after changes

- `for…of` over `document.querySelectorAll` works, returning each `JsDomElement`.
- No fatal crashes when evaluating the IIFE; the Nokia archive page proceeds to render SVG elements via D3.
- NilJsTest can now run DOM‑dependent snippets reliably, providing a fast feedback loop for further DOM stub work.

---

*Next actions*: implement the code changes, run the updated test harness, and if successful, resume binary‑search of the remaining portion of `main‑BE‑aXEfW.js` to confirm full script stability.
