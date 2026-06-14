# Summary 4.13 — D3.js: DOM API + SVG rendering

**Session date:** 2026-06-05
**Build:** `msbuild Src\MediaExplorer.sln /p:Configuration=Debug /p:Platform=x64` — ✅ **0 errors**
**Mode:** code editing + build verification

---

## 1. Phase3 JS Timeout Fix (carryover from 4.12)

**Problem:** D3.js execution aborted after 15s with `[DIAG] RenderAsync Phase3 JS TIMEOUT` — no D3 visualizations were created.

**Root cause:** `_scriptTimeoutMs` was hardcoded to 15000ms in `CustomHtmlEngine.cs`. The Nokia Archive page loads D3.js (279KB) + polyfills (35KB) + SystemJS chunk (589KB) — 900KB+ of JS total, which needs ~30-50s in NiL.JS.

**Fix** (`CustomHtmlEngine.cs:1519`, `BrowserHost.cs:201`):
- `_scriptTimeoutMs` made configurable via `BrowserHost.Config`
- Default raised from 15000→60000ms

**Result:** `[DIAG] RenderAsync Phase3 JS DONE` — timeout eliminated.

---

## 2. WeakRef Stub

**Problem:** `WeakRef is not defined` JSException during SystemJS polyfill execution.

**Fix** (`JavaScriptEngine.cs:3400` globals section):
```csharp
_globals["WeakRef"] = JsVal.Null();
```

**Result:** No more `WeakRef` errors.

---

## 3. DOM API Additions for D3.js Compatibility (E1–E3)

**Root cause of NO visual output:** D3.js creates SVG elements via `document.createElementNS("http://www.w3.org/2000/svg", "svg")` — this API didn't exist on `HostDocument`. Additionally, D3 relies on DOM traversal properties (`parentNode`, `nextSibling`, `insertBefore`, `textContent`, `nodeType`, etc.) that were missing from `JsDomElement`.

**All changes in `JavaScriptEngine.cs`:**

### E1 — HostDocument additions (line 2577)

| Method | Description |
|--------|-------------|
| `createElementNS(string ns, string tag)` | Delegates to `createElement(tag)`, ignores namespace |
| `createTextNode(string data)` | Was `return null`; now returns real `JsDomText` instance |
| `querySelector(string selector)` | Was `return null`; now delegates to `JsDocument.querySelector` |
| `getElementsByClassName(string className)` | Was `return new object[0]`; now actually searches DOM |

### E2 — JsDomElement CLR properties (line 9310)

| Property | Type | Notes |
|----------|------|-------|
| `nodeType` | string | Returns `"3"` for text nodes, `"1"` for elements |
| `nodeName` | string | Alias for `tagName` |
| `textContent` | get/set | Gets all text / replaces children with text node |
| `className` | get/set | Gets/sets `class` attribute |
| `parentNode` | object | Returns parent as `JsDomElement` or null |
| `nextSibling` | object | Next element sibling |
| `previousSibling` | object | Previous element sibling |
| `firstChild` | object | First child element |
| `lastChild` | object | Last child element |
| `children` | object[] | Non-text child elements |
| `childNodes` | object[] | All children (including text) |
| `ownerDocument` | object | Returns `HostDocument` instance |

### E3 — JsDomElement CLR methods

| Method | Description |
|--------|-------------|
| `insertBefore(newChild, refChild)` | Inserts before reference node |
| `replaceChild(newChild, oldChild)` | Replaces child node |
| `remove()` | Removes self from parent |
| `contains(other)` | Checks if other is descendant |
| `cloneNode(bool deep)` | Clones element (with or without children) |
| `matches(string selector)` | Checks CSS selector match |
| `getElementsByTagName(string tag)` | Searches descendants by tag |
| `getElementsByClassName(string className)` | Searches descendants by class |
| `setAttributeNS(ns, name, value)` | Delegates to `setAttribute` |
| `getAttributeNS(ns, name)` | Delegates to `getAttribute` |
| `hasChildNodes()` | Returns bool |

### JsDomText CLR additions

| Property | Type | Notes |
|----------|------|-------|
| `nodeType` | int | Returns `3` (was `"text"` string) |
| `nodeName` | string | Returns `"#text"` |
| `nodeValue` | get/set | Same as `data` |
| `textContent` | get/set | Same as `data` |
| `parentNode` | object | Parent as `JsDomElement` |
| `nextSibling` / `previousSibling` | object | Sibling elements |
| `ownerDocument` | object | Returns `HostDocument` |

---

## 4. SVG Rendering Fix (E4)

**Problem:** `RenderInlineSvg` (line 521 of `DomBasicRenderer.cs`) was defined but **never called**. `HtmlTag.Svg` was not handled in `DispatchTagAsync` switch — SVG elements fell through to `default:` and were rendered as `StackPanel` (invisible for SVG).

**Fix** (`DomBasicRenderer.cs:105`):
```csharp
case HtmlTag.Svg:
    return RenderInlineSvg(n);
```

**Result:** `<svg>` elements parsed from HTML or created via JS DOM APIs are now rendered as XAML Canvas with paths/shapes.

---

## 5. Remaining Work

- **D3 visualizations MAY NOT appear yet** — even with the DOM API fixes, D3 re-checks `getBoundingClientRect()` which returns 0s, and many other subtle DOM differences may cause silent failures
- **CSS computed styles** — SVG elements need `fill`, `stroke`, etc. from CSS, but AppendSvgElement reads only attributes, not computed styles
- **Test** — run actual Nokia Archive page with debug logging to verify D3 creates SVG elements in LiteElement tree
- No SVG `getBBox()` or `getCTM()` stubs (D3 might use them for layout)
