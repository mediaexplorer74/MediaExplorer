# MediaExplorer / WEBVIEW — Plan 04: Pragmatic Path to v1.0

> **Project:** MediaExplorer 0.42.8 (codename "WebView") — retro UWP museum browser for W10M
> **Hardware target:** Lumia 950/1020 (Snapdragon 810, 3 GB RAM, 5" 1440p)
> **Test environment:** x86 emulator (primary) → ARM device (validation)
> **Author note:** Plan 04 is the first plan written with explicit *honesty constraints* —
> it distinguishes between what is achievable, what is a trap, and what the project
> actually needs to feel finished. Read the Honest Assessment section before the phases.
> **Last updated:** 2026-06-05 (post-session 3.39c)

> **Recent changes (TL;DR):**
> - 2026-06-04..05 — Phase C.6 (Basic CSS Transitions) implemented. See `Doc/Summary_4_05.md`.
> - 2026-06-05 — Phase C.6 follow-up (multi-property comma-separated transitions, smooth background from no-brush, % translate). See `Doc/Summary_4_06.md`.
> - 2026-06-05 (session 3.29) — Codebase verification. See `Doc/Summary_4_08.md`.
> - 2026-06-05 (session 3.30) — Live Nokia test: System.import OK (589KB). Page renders but no D3. Root cause: `System` was read-only anonymous type → `Dictionary<string, object>`. Proxy.cs null-guard. See `Doc/Summary_4_08.md`.
> - 2026-06-05 (session 3.31) — Polyfill overwrites `System.import` → removed `System` pre-definition entirely. Standalone `__sysImport` host function. Inline `System.import(...)` → `__sysImport(...)` intercept in `RunScriptsAsync` loop. Build 0 errors. See `Doc/Summary_4_08.md`.
> - 2026-06-05 (session 3.32) — `__sysImport` was fire-and-forget `Task.Run` → System.register calls happen but `execute()` never called (SystemJS expects script `onload`). Fixed: synchronous fetch with `.GetAwaiter().GetResult()`, plus post-`RunInline` force-execute of System.registry entries. Build 0 errors. See `Doc/Summary_4_08.md`.
> - 2026-06-05 (session 3.33) — Polyfill's System constructor produces object with 0 enumerable properties in NiL.JS (no `register`, no `registry`). Abandoned polyfill repair approach; replaced `globalThis.System` entirely with minimal SystemJS (register, registry, import) via JS snippet before legacy chunk runs. Build 0 errors. See `Doc/Summary_4_09.md`.
> - 2026-06-05 (session 3.34) — Chunk's `System.register` never called. Root cause: NiL.JS can't parse 589KB file at once (3 JSExceptions during parse). Fix: split chunk into individual `System.register(...)` calls, evaluate each via SafeEval. Inline parenthesis-matcher in C#. Build 0 errors. See `Doc/Summary_4_10.md`.
> - 2026-06-05 (session 3.34a) — **Live test FAIL**: `split chunk into 0 System.register calls` despite `IndexOf = 0`. Root cause: paren matcher doesn't skip `//`, `/* */`, `/regex/`, or `` ` `` — `()` inside them corrupt depth counter. See `Doc/Summary_4_10.md` §4.1.
> - 2026-06-05 (session 3.35) — **Fixed**: enhanced paren matcher with regex/comment/template skipping + `afterExprPrefix` heuristic. **Live test PASS**: `split chunk into 1 System.register calls` — module registered and executed. Page renders. See `Doc/Summary_4_11.md`.
> - 2026-06-05 (session 3.36) — **Phase S**: JS execution timeout via NiL.JS DebuggerCallback (7s), graceful error page try/catch in NavigateAsync, RenderPipeline LayoutEngine guard. Build 0 errors.
> - 2026-06-05 (session 3.37) — **Phase V**: smooth AppBar lerp animation (CubicEase 150ms), loading progress bar (Chrome-style top bar), welcome page rewrite (name+quotes+links), error recovery button. Build 0 errors.
> - 2026-06-05 (session 3.39b) — D3 polyfill evolution: `window.Map=Map$` fails (window disconnected), bare `Map=Map$` fails (HostMapType read-only), `_nil.Eval("(function(){})")` throws InvalidOperationException. Key: `_nil.Eval("try{...}catch(e){}")` works for function defs. New approach: try-catch wrapper evals + text replacement (`class extends Map` → `class extends __MapPolyfill`). Build 0 errors.
> - 2026-06-05 (session 3.39c) — D3 polyfill finalization: abandoned separate `_nil.Eval` polyfill calls (still unreliable); prepend polyfill via string prefix instead. Analysis of actual minified d3.js revealed `super()` in comma-expression context (`if(super(),...)`), `new Map` without parens (`{value:new Map}`), and InternSet's `super.add()`. Fixes: comma-expression `super()` → `(this._d={},this.size=0)`, `super.add(` redirect, `new Map`/`new Set` → polyfill (all forms), constructors accept entries/values, `forEach` accepts `thisArg`. **0 NiL.JS JSExceptions during d3.js eval** (previously 3). Still `d3=d3_missing` — JS-level runtime error caught by try/catch. Build 0 errors. See `Doc/Summary_4_15.md`.
> - 2026-06-06 (session 3.40) — **Strategic pivot: URL rewrite d3.v7 → d3.v5.** Log analysis showed `let/const → var` text replacement did not help (3 JSExceptions returned + new InvalidOperationException from `;let{` → `;var {` destructuring). Text-replacement approach abandoned — too many ES6+ features (for...of, default params, destructuring, template literals). Added `TryRewriteD3jsUrl` URL rewrite in `FetchScriptStringAsync` targeting d3.v5 (ES5). Removed broken `;let{` → `;var {` replacement. d3.v5.min.js (v5.16.0, 248KB) confirmed live. Build 0 errors. See `Doc/Summary_4_16.md`.
> - 2026-06-06 (session 3.41) — **NiL.JS parser depth StackOverflow root cause found & fixed.** Created `NilJsTest` test harness. Added depth guard (`state.ParserDepth` + `MaxParserDepth=100`) with vicinity info to prevent fatal StackOverflow. Fixed `JSException(Error)` constructor null-ref crash during parsing (`Context.CurrentGlobalContext` is null before execution). **Key fix:** made comma operator parsing **iterative** instead of recursive — d3.v5's long comma chains (`t.X=...,t.Y=...,t.Z=...`) caused depth 300+ → StackOverflow. Now both d3.v5.min.js (248KB) and d3.v4.min.js (222KB) parse & execute at max depth ~25. Build 0 errors. See `Doc/Summary_4_17.md`.
> - 2026-06-06 (session 3.42) — **Log analysis + revert d3js.org skip + DOM stubs.** Live test revealed `d3=d3_missing` (was `d3_defined` in 3.41). Root cause: d3js.org skip prevented the `d3` global from being defined — d3.v5's `var d3={...}` happens before any DOM access that throws, so the global persists even through SafeEval catch. Reverted skip in both `Execute` and immediate loop. Added `namespaceURI` (SVG/XHTML namespaces) and `ownerDocument` to JsDomElement. Retained 4.17's DOM stubs (`parentElement`, `closest`, `nextElementSibling`, `previousElementSibling`, `scrollIntoView`, `createDocumentFragment`, globals). Build 0 errors. See `Doc/Summary_4_18.md`.

---

## Honest Assessment: Where You Are and What That Means

### What has actually been built (remarkable)

After 38+ sessions across four plan documents, MediaExplorer is not a toy. It is:

- A working custom HTML parser + CSS cascade engine with a selector index (O(candidates) not O(n×rules))
- A XAML-based virtualizing renderer with element recycling and lazy image loading
- A NiL.JS 2.6 integration where a real 568 KB Vite bundle now **parses without syntax errors** — this required expert-level parser surgery across 9 sessions (3.9–3.17)
- 50+ browser globals, MutationObserver, ES Modules, incremental re-render, disk cache, reading modes, AI integration, DevTools with 4 tabs, structured test infrastructure

That is, objectively, a remarkable solo achievement. Most people who start "let's build a browser engine" projects abandon them at the "renders Hello World" stage.

### The current trap: the JS engine rabbit hole (RESOLVED — Sessions 3.9–3.18)

Sessions 3.9–3.17 were almost entirely NiL.JS surgery. Every fix revealed another issue.
**Session 3.18 declared a JS Engine Freeze** — the `in` operator was fixed, `NilJS_Compat.md`
written, and active NiL.JS patching stopped. Sessions 3.19+ shifted to CSS + stability.

The pattern was an **infinite horizon**:
```
3.9  → import.meta, console.warn
3.10 → destructuring defaults, logical assignment (??=, ||=, &&=)
3.11 → async methods in object literals
3.12 → get/set as field names
3.13 → critical regression fix
3.14 → arrow functions, IIFE, const{x} spacing → FULL PARSE ✅
3.15 → querySelectorAll, navigation fixes, toast
3.16 → new keyword, HostLocation
3.17 → 50+ globals expansion
3.18 → in operator ← LAST NiL.JS PATCH (engine freeze declared)
```

**The `in` operator issue is actually a 2-line fix.** The problem is in `NiL.JS/Expressions/In.cs:42`:
```csharp
// Current (throws):
if (!rhs.IsObject)
    ExceptionHelper.Throw(new TypeError("Right-hand value of operator in is not an object."));

// Fix (browser-compatible behavior):
if (rhs == null || !rhs.IsObject)
    return false; // undefined/null RHS → false, not throw
```
Modern JS engines silently return `false` for `'key' in undefined`. NiL.JS throws. Fix it,
then **stop touching NiL.JS for at least 5 sessions**.

### What "Nokia Archive renders perfectly" actually requires

The Nokia Design Archive uses D3.js v7. Making it render correctly needs:

| Requirement | Status | Effort |
|-------------|--------|--------|
| Parser: full ES2022 syntax | ✅ DONE (3.14) | Done |
| `in` operator fix | ✅ DONE (3.18) | Done |
| Private class fields `#name` (12 uses) | ❌ NiL.JS unsupported | Not planned (JS freeze) |
| Full DOM event system (addEventListener on SVG, input) | ❌ Stubs only | 5–10 days |
| Canvas 2D API | ❌ Not started | 10–20 days |
| SVG DOM manipulation via JS | ❌ Not started | 10–15 days |
| CSS transitions driven by JS | ✅ C.6 done (3.26) | Done |
| D3 data binding (enter/exit/update) via DOM mutations | ❌ Untested | Unknown |
| ES module (`type="module"`) execution | 🔄 Bypassed via nomodule legacy path (3.28) | Done |
| SystemJS fallback loader | ✅ Chunk splitting fixed (regex/comment/template skipping). Module registers (`registry entries: 1`) and executes (`force-exec: 1 declared`). JS phase completes without errors. | Done |

**Latest (3.39c — D3 polyfill finalization, 2026-06-05):** Iterating on the polyfill approach revealed multiple constraints:
1. `window.Map = Map$` fails — `window` in SafeEval is disconnected from global scope
2. `Map = Map$` fails — HostMapType is read-only (assign silently ignored)
3. `_nil.Eval("(function(){...})()")` throws `InvalidOperationException: Unable to get this-binding for Global Context`
4. Even `_nil.Eval("(function(){})")` throws the same error
5. `_nil.Eval("try{ (function(){}) }catch(e){}")` works (same mechanism that lets SafeEval handle 279KB d3.js)
6. `class extends Map` fails because HostMapType doesn't implement NiL.JS prototype interfaces

**Approach abandoned after 3.39b:** `_nil.Eval("try{...}catch(e){}")` polyfill definitions still unreliable. **Final approach:** Prepending polyfill JS code directly via string concatenation (no separate `_nil.Eval` calls) + text-replace `extends Map/Set` → removed, `super.*()` → `.call(this,)`, `new Map/Set` → `new __MapPolyfill`/`__SetPolyfill` in content string before eval.

**Key analysis of actual minified d3.js v7.9.0:**
- `extends Map{constructor(t,n=N){if(super(),...` — **`super()` in comma-expression context** → replacing with `;` causes syntax error. Fixed: comma-expression `(this._d={},this.size=0)`.
- `{value:new Map}` — **`new Map` without parens**. Fixed: `new Map` → `new __MapPolyfill` (all forms).
- InternSet's `add(value){super.add(intern_set(this,value))}` — needed `super.add(` redirect.

**Polyfill improvements:**
- `__MapPolyfill(entries)`, `__SetPolyfill(values)` constructors accept optional initial data
- `forEach(fn, thisArg)` — added `thisArg` support
- `super.add(` → `__SetPolyfill.prototype.add.call(this,`

**Result:** `hasMap=True hasSet=True` confirmed. **0 NiL.JS JSExceptions during d3.js eval** (previously 3). Still `d3=d3_missing` — some JS-level runtime error caught by `try{...}catch(e){}`. Next: live test on emulator with new fixes. See `Doc/Summary_4_15.md`.

**Session 3.41 (2026-06-06) — NiL.JS parser depth StackOverflow fixed.**
**d3.v5.min.js now parses and executes successfully.** The 248KB ES5 bundle was hitting recursive comma-operator depth 300+ → fatal StackOverflow. Root cause: `parseContinuation` called `Parse(processComma:true)` which recursed for each comma in long chains `a,b,c,...,z` (~100+ comma items in d3's geo projection section).

**Fix:** comma parsing made iterative — all operands collected in a `while` loop at the same stack level, then chained into a left-associative tree. With this fix, d3.v5 max depth is ~25 (was 300+). Both d3.v5.min.js and d3.v4.min.js parse & execute cleanly with default `MaxParserDepth=100`. See `Doc/Summary_4_17.md`.

**Session 3.42 (2026-06-06) — Log analysis + revert d3js.org skip + DOM stubs.**
Live test of 4.17 changes showed `d3=d3_missing` (was `d3_defined` in 4.17). Root cause: d3js.org skip prevented the `d3` global definition — d3.v5's `var d3={...}` executes before any DOM access that throws, so the global persists through SafeEval catch. Reverted skip in both `Execute` and immediate loop. Added `namespaceURI` (SVG/XHTML) and `ownerDocument` to JsDomElement to reduce DOM exceptions. Retained 4.17's stubs. Build 0 errors. See `Doc/Summary_4_18.md`.

**Next:**
- Verify `d3=d3_defined` on UWP emulator build.
- Diagnose and fix `document.querySelectorAll` / `document.createElement` for‑of iteration crash (return proper JS iterable). 
- Add missing DOM stubs (`ownerDocument`, `namespaceURI` already added) and ensure `querySelectorAll` returns a `NativeList` (JS array).
- Added `document.getElementsByClassName` support; host‑function now returns a `NativeList` iterator‑compatible collection. 
- Extend NilJsTest harness to use `JavaScriptEngine` for DOM‑dependent tests.
- Continue binary‑search of `main‑BE‑aXEfW.js` to isolate any remaining JS syntax issues after IIFE.
- Update documentation to reflect new tasks and findings.


**Total remaining estimate for Nokia Archive to render:** 20–40 days (reduced from 30–55
via legacy bypass, still gated by DOM event system + D3 data binding).

**Recommendation:** Accept Nokia Archive as a "stretch goal that may never be reached" and
redirect to making the browser excellent for its real audience — simple to moderate sites on
a retro Lumia.

### What "v1.0 museum-stable" actually requires

| Requirement | Status | Effort |
|-------------|--------|--------|
| Fix `in` operator | ✅ DONE | Done |
| CSS: calc(), vw/vh | ✅ DONE (3.23) | Done |
| CSS: grid-template-areas | ✅ DONE (3.21) | Done |
| CSS: proper @media queries | ✅ DONE (3.22) | Done |
| CSS: CSS Transitions | ✅ | C.6 — done 3.26 |
| CSS: custom properties scope | 🟡 Code done, T-C-016 parked (3.25) | C.4 — resume when device available |
| CSS: clamp() | ✅ DONE (3.25) | Done |
| Robustness: NaN guards, JS timeout, DOM limit, cascade/layout guards | ❌ | S.1–S.6 — 1–2 days |
| Test suite: all T-H and T-C pass | 🟡 partial | 1 day |
| 5 target sites render acceptably | 🟡 some | Depends on CSS |
| No crash in 10 min normal browsing | 🟡 | Depends on stability |

**Total estimate for v1.0:** 5–9 additional days. This is absolutely achievable.

---

## v1.0 Release Criteria (explicit "done" definition)

A release is called v1.0 when ALL of these pass:

```
HTML:  T-H-001 through T-H-008 — all pass visually
CSS:   T-C-001 through T-C-017 — at least 12/17 pass visually
JS:    T-J-001 through T-J-010 — at least 7/10 pass in about:test
Sites: T-S-001 (DuckDuckGo) — search box visible and typable
       T-S-004 (httpbin.org/html) — text readable, no crash
       T-S-005 (example.com) — renders correctly
       T-S-007 (text.npr.org) — articles readable
       T-S-008 (lobste.rs) — link list visible
Perf:  DuckDuckGo total render time < 3000ms on emulator
       No crash in 10 minutes on any T-S-xxx site
```

---

## NiL.JS Compatibility Matrix (new document: `Doc/NilJS_Compat.md`)

Before spending more time on JS fixes, establish what NiL.JS 2.6 can and cannot do.
This becomes the permanent reference for what to stub vs. fix vs. accept.

### What works (confirmed)

| Feature | ES Version | Status |
|---------|-----------|--------|
| `var`, `let`, `const` | ES5/6 | ✅ |
| Arrow functions `=>` | ES6 | ✅ (fixed 3.11/3.14) |
| Destructuring with defaults | ES6 | ✅ (fixed 3.10) |
| Template literals | ES6 | ✅ |
| `async`/`await` | ES8 | ✅ |
| `async function*` | ES9 | ✅ (fixed 3.11) |
| `import`/`export` (static) | ES6 | ✅ (NiL.JS native) |
| `import()` dynamic | ES2020 | ✅ (graceful fallback) |
| `import.meta` | ES2020 | ✅ (fixed 3.9) |
| Logical assignment `??=`, `\|\|=`, `&&=` | ES2021 | ✅ (fixed 3.10) |
| `Promise`, `async/await` | ES8 | ✅ |
| `JSON.parse` / `JSON.stringify` | ES5 | ✅ |
| `Array` methods (map, filter, reduce) | ES5/6 | ✅ |
| `Object.keys`, `Object.assign` | ES6 | ✅ |
| `class` (basic) | ES6 | ✅ |
| `class` with private fields `#x` | ES2022 | ❌ Not supported |
| `in` operator (object RHS) | ES5 | ✅ |
| `in` operator (non-object RHS) | — | ❌ Throws (fix in 3.18) |
| `Symbol` | ES6 | ❌ Partial |
| `WeakRef`, `FinalizationRegistry` | ES2021 | ❌ Not supported |
| `BigInt` | ES2020 | ❌ Not supported |

### DOM API compatibility

| API | Status | Notes |
|-----|--------|-------|
| `document.getElementById` | ✅ | |
| `document.querySelector` | ✅ | |
| `document.querySelectorAll` | ✅ (3.15) | |
| `document.createElement` | ✅ | |
| `element.innerHTML` | ✅ | triggers MutationObserver |
| `element.setAttribute` | ✅ | |
| `element.addEventListener` | 🟡 stub | registered but not fired on XAML events |
| `element.getBoundingClientRect` | ❌ | returns zeroes |
| `window.addEventListener` | 🟡 stub | |
| Canvas 2D API | ❌ | not implemented |
| SVG DOM manipulation | ❌ | not implemented |
| `ResizeObserver` | ❌ | |
| `IntersectionObserver` | ❌ | |

**Key insight:** `element.addEventListener` is registered but XAML events (click, scroll, resize)
don't fire into NiL.JS. This means interactive JS (dropdowns, modals, carousels) silently does nothing.
This is acceptable for a "museum browser" — document it, don't spend months fixing it.

---

## Phase R: Rationalization & JS Engine Freeze

> **Priority: 🔴 Must do first — 1 session (~1 hour)**
> **Goal:** Fix the one remaining trivial NiL.JS bug, then stop touching NiL.JS for 5 sessions.

### R.1 — Fix `in` operator (NiL.JS In.cs)

```csharp
// NiL.JS/Expressions/In.cs, line ~42
// BEFORE (throws):
if (!rhs.IsObject)
    ExceptionHelper.Throw(new TypeError("..."));

// AFTER (browser-compatible):
if (rhs == null || rhs.ValueType == JSValueType.Undefined
                || rhs.ValueType == JSValueType.Null
                || !rhs.IsObject)
    return false;
```

### R.2 — Reduce InvalidOperationException log noise

315 empty catch blocks were noted in session logs. Add a bulk log suppressor for the 3 most
common NiL.JS internal exception types to keep DevTools console readable:

```csharp
// JavaScriptEngine.cs — in the top-level eval catch
catch (InvalidOperationException ex)
    when (ex.Source == "NiL.JS" && ex.TargetSite?.Name == "Invoke")
{
    // Suppress NiL.JS internal reflection errors silently
    // These are expected when JS uses unsupported features (private fields, Symbol)
}
```

### R.3 — Write `Doc/NilJS_Compat.md`

Copy the compatibility matrix from this plan into a dedicated document. 30 minutes.
Future JS debugging starts here, not by re-running Nokia Archive and guessing.

### R.4 — Set the JS Engine Freeze rule

Add to top of `JavaScriptEngine.cs` as a comment:

```
// JS ENGINE FREEZE — active until session 3.23
// No new NiL.JS patches. Acceptable: adding new host globals (window.xxx = ...).
// Not acceptable: editing Src/NiL.JS/*.cs
// Reason: each NiL.JS fix risks regression (see session 3.13 near-disaster).
//         CSS + Stability work has better ROI right now.
```

### Files affected
- `Src/NiL.JS/Expressions/In.cs` — 3-line fix
- `Engine/JavaScriptEngine.cs` — catch clause + freeze comment
- `Doc/NilJS_Compat.md` — new document

**Effort:** ~1 hour

---

## Phase C: CSS Completion (Highest Visual ROI)

> **Priority: 🔴 High — directly improves every target site**
> **Effort: 4–6 days / ~500 lines**

CSS completion has dramatically better ROI than JS work for visual quality: a site
with broken CSS looks broken even if JS works. A site with great CSS looks right
even without interactive JS.

### C.1 — `calc()` and viewport units (`vw`, `vh`, `dvw`, `dvh`)

Most modern layouts use `width: calc(100% - 16px)` or `height: 100vh`. These are
currently ignored, causing collapsed or overflowing elements.

**Implementation in `CssLoader.cs`:**

```csharp
private double? TryPxResolved(string value, double vpW, double vpH, double parentW = 0)
{
    value = value.Trim();

    // vw / vh
    if (value.EndsWith("vw")) return ParseDouble(value, "vw") * vpW / 100.0;
    if (value.EndsWith("vh")) return ParseDouble(value, "vh") * vpH / 100.0;
    if (value.EndsWith("dvw")) return ParseDouble(value, "dvw") * vpW / 100.0;
    if (value.EndsWith("dvh")) return ParseDouble(value, "dvh") * vpH / 100.0;

    // calc()
    if (value.StartsWith("calc(") && value.EndsWith(")"))
        return EvalCalc(value[5..^1], vpW, vpH, parentW);

    return TryPx(value); // existing fallback
}

// EvalCalc: handles "100% - 16px", "50% + 8px", "2 * 24px"
// Two-operand evaluator is enough for 95% of real-world calc()
private double? EvalCalc(string expr, double vpW, double vpH, double parentW)
{
    // Find +/- that is not inside nested parens (for operator precedence)
    // Parse left and right operands, apply operator
    // Resolve units: % → parentW, px → direct, vw/vh → viewport
    // Return null on parse failure (graceful degradation)
    ...
}
```

**Test:** Add `T-C-011`: `<div style="width:calc(100% - 40px); background:pink">` inside
a `200px` container → element should be `160px` wide.

### C.2 — `@media` query improvements ✅ DONE (Session 3.22)

Implemented in Phase 16.7. Supports `min-width`, `max-width`, `min-height`, `max-height`,
`orientation`, `dppx`, `prefers-color-scheme`, `scripting`, `not`/`and` combinators.
Real DPR via `DisplayInformation.RawPixelsPerViewPixel`. `matchMedia()` rewritten.

Original description below for reference:

> Currently only `min-width` is supported. Add:
> - `max-width`
> - `min-height` / `max-height`
> - `orientation: portrait | landscape`
> - `prefers-color-scheme: dark | light` (always return `light` for retro browser feel)
> 
> **Test:** Add `T-C-012`: stylesheet with `@media (max-width: 600px)` rule — on emulator
> (1024px wide) the rule should NOT apply; at 400px viewport it should.

### C.3 — `grid-template-areas` ✅ DONE (Session 3.21)

Implemented in Phase 16.6 (CSS Grid + Flexbox routing + grid-template-areas).
Supports named areas, explicit placement with spans, auto-placement (row/column flow),
`repeat()`, `minmax()`, `fr`/`px`/`auto` track sizing. Maps to UWP `Grid` panel.

Original test (keep for regression):
```html
<div style="display:grid;
  grid-template-columns:1fr 3fr;
  grid-template-areas:'nav main' 'footer footer'">
  <div style="grid-area:nav; background:salmon">Nav</div>
  <div style="grid-area:main; background:lightblue">Main</div>
  <div style="grid-area:footer; background:gold">Footer</div>
</div>
```

### C.4 — CSS custom properties (`--var`) scope fix

Currently `var(--name)` is resolved globally. CSS custom properties cascade per-element.
Fix: resolve `--name` by walking the element's ancestor chain looking for the nearest
computed style that defines `--name`. If not found, use `:root` value.

**Effort:** ~60 lines in `CssLoader.cs`

### C.5 — `clamp()` (✅ Session 3.25)

```css
font-size: clamp(14px, 2vw, 20px)
```

`return Math.Max(min, Math.Min(max, preferred))` after resolving each operand.
Implemented as a small helper called from both `TryPx` (top-level `clamp(...)`)
and `EvaluateCalc` (nested inside `calc(...)`).

**Files touched:** `Engine/CssLoader.cs` (~25 lines, one helper + two call sites)
+ `Html/test.html` T-C-017.

### C.6 — Basic CSS Transitions (from Plan_03 Phase 16.4) ✅

CSS transitions make hover effects, dropdowns, and interactive feedback work.
Full XAML storyboards are expensive; use a lightweight approach:

- Parse `transition: property duration easing`
- On CSS property change, check if the element is in `_visibleElements`
- If yes, create a `DoubleAnimation` on the relevant XAML property with the specified duration
- Only support: `opacity`, `transform` (via `RenderTransform`), background-color changes

```csharp
// DomBasicRenderer.cs or VirtualizingRenderer.cs
if (!string.IsNullOrEmpty(css.Transition))
{
    var parts = css.Transition.Split(' ');
    var prop = parts[0];               // "opacity"
    var durationMs = ParseDuration(parts.Length > 1 ? parts[1] : "0s");
    if (durationMs > 0 && _visibleElements.TryGetValue(element, out var xamlEl))
    {
        var anim = new DoubleAnimation
        {
            To = targetValue,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, xamlEl);
        Storyboard.SetTargetProperty(anim, prop == "opacity" ? "Opacity" : "(RenderTransform).(RotateTransform.Angle)");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
    }
}
```

This is a "good enough" approximation — true CSS transitions require per-frame interpolation
which is handled by XAML's animation system anyway.

**Test:** Add `T-C-015`: button with `transition: opacity 0.3s` and hover state
(manual test — tap, observe fade).

**Effort:** ~100 lines

**Status (3.26):** ✅ Implemented.
- 6 new fields on `CssComputed`: `Transition`, `TransitionDurationMs`, `TransitionProperty`,
  `TransitionTimingFunction`, `TransitionDelayMs`, `Hover`.
- `CssLoader.MatchesSingle` returns `false` for `:hover`/`:focus`/`:active` in base
  cascade; new `ComputeHoverOverrides` populates a separate `css.Hover` CssComputed.
- `CssLoader.ParseTransition` extracts duration/property/timing/delay from shorthand.
- New `Engine/TransitionAnimator.cs` — 5 `Animate*` methods + `BuildEasing`.
- `DomBasicRenderer.AttachHoverTransition` wires `PointerEntered`/`PointerExited`
  → `Storyboard` for opacity/background-color/transform (scale+rotate+translate).
- T-C-015 extended to 4 boxes (opacity, bg, transform, all).
- New `Html/test_transitions.html` (registered as Content) with 11 demo cases.
- Build 0 errors. See `Doc/Summary_4_05.md` for details.

**Follow-up (3.27):** ✅ Multi-property comma-separated transitions.
- `CssComputed.TransitionSpec` + `TransitionList` store parsed multi-property entries.
- `CssLoader.ParseTransition` parses all comma-segments (reuses existing `SplitTopLevelCommas`).
- `AttachHoverTransition` rewritten: iterates `TransitionList`, resolves per-property
  timing via `TransitionSpecForProp()`. Backward-compat via `TransitionSpecForAnim` struct.
- Smooth background: when no base brush exists, sets `Transparent` brush before
  animating (no jump). PointerExited animates back to `Transparent`.
- `%` in `translate()`: `ParseTransformForHover` accepts `elemW`/`elemH`, computes
  `(pct/100)*elemSize`. Hover values parsed inside `PointerEntered` (sizes known).
- Test page expanded (+3 cases: multi-dur, no-base-bg, % translate). Build 0 errors.
  See `Doc/Summary_4_06.md` for details.

### Testing

After each C.x sub-phase: navigate to `about:test`, check the corresponding T-C-xxx row.
Log result in `Doc/Perf_Baseline.md`.

**Files affected:** `Engine/CssLoader.cs`, `Engine/CssComputed.cs`, `Html/test.html`

---

## Phase S: Stability & Robustness ✅ `DONE (sessions 3.27 + 3.36)`

> **Priority: 🔴 Required for v1.0**
> **Effort: 2–3 days / ~300 lines**

### S.1 — NaN/Infinity guards (systematic) ✅ `DONE (3.27)`

`elementSize=NaNxNaN` in logs indicates invalid sizes leak into `VirtualizingRenderer`.

Audit strategy: grep for all `.Width =`, `.Height =`, `new GridLength(` in
`DomBasicRenderer.cs` and `VirtualizingRenderer.cs`. For each assignment:

```csharp
// Pattern to apply everywhere:
private static double SanitizeSize(double v, double fallback = 0)
    => double.IsNaN(v) || double.IsInfinity(v) || v < 0 ? fallback : v;

// Usage:
element.Width = SanitizeSize(computedWidth);
element.Height = SanitizeSize(computedHeight);
new GridLength(SanitizeSize(colWidth, 1)) // fallback to 1px not 0
```

Add `[DIAG:NaN source={property}]` log when a value is sanitized so you can trace
which CSS property produced it.

### S.2 — JS execution timeout ✅ `DONE (3.36 — DebuggerCallback approach)`

NiL.JS can loop forever. Wrap each `_nil.Eval()` call with a 7s timeout:

```csharp
// JavaScriptEngine.cs — SafeEval timeout via NiL.JS DebuggerCallback
// NiL.JS contexts are not thread-safe, so Task.Run + CancellationToken
// would corrupt state. Instead, use the synchronous DebuggerCallback:
//  1. Enable Context.Debugging = true before Eval
//  2. Register a DebuggerCallback that checks elapsed time
//  3. If >= 7000ms, throw TimeoutException
//  4. In finally, restore Debugging flag + unregister callback
// TimeoutException propagates to outer catch → returns JSValue.Undefined
```

### S.3 — DOM node cap ✅ `DONE (3.27)`

Enforce the 10 000 node limit from Plan 01. In `HtmlParser`:

```csharp
private int _nodeCount = 0;
private const int MaxNodes = 10_000;

LiteElement CreateNode(...)
{
    if (++_nodeCount > MaxNodes)
    {
        DevToolsLogger.Log($"[WARN] DOM cap hit at {MaxNodes} nodes — truncating");
        return null; // caller handles null gracefully
    }
    return new LiteElement(...);
}
```

### S.4 — CSS rule cap ✅ `DONE (3.27)`

Verify the existing rule cap (5 000) is enforced. Add:
```csharp
if (_rules.Count >= MaxRules)
{
    DevToolsLogger.Log($"[WARN] CSS rule cap hit at {MaxRules}");
    return; // don't add more rules
}
```

### S.5 — Graceful error page ✅ `DONE (3.36)`

When `NavigateAsync` throws any unhandled exception, show the error overlay
(via existing `ShowGlobalError`) instead of crashing. The try-catch in
`_browser.NavigateAsync(address)` logs `[DIAG:EXCEPTION]` and calls
`ShowGlobalError("Navigation failed: " + ex.Message)`.

### S.6 — Cascade/Layout exception guards ✅ `DONE (3.36)`

`OverflowException`/`ExecutionEngineException` were partially fixed in Plan_00 but may recur
on new sites. Ensure every entry into `CascadeIntoComputedStyles` and `PerformLayout` has
a top-level try/catch:

```csharp
// CustomHtmlEngine.cs — RenderAsync
try
{
    await Task.Run(() => _cssLoader.CascadeIntoComputedStyles(_domRoot, viewportW));
}
catch (Exception ex)
{
    DevToolsLogger.Log($"[ERROR:CASCADE] {ex.GetType().Name}: {ex.Message}");
    // Continue rendering with partial styles rather than crashing
}

try
{
    await Task.Run(() => _layoutEngine.PerformLayout(_domRoot, viewportW));
}
catch (Exception ex)
{
    DevToolsLogger.Log($"[ERROR:LAYOUT] {ex.GetType().Name}: {ex.Message}");
    // Continue with partial layout rather than crashing
}
```

**Effort:** ~30 lines

### Testing

| ID | Test | Expected |
|----|------|----------|
| T-R-001 | Navigate to `not-a-url` | Error page shown, no crash |
| T-R-002 | Run `while(true){}` in DevTools console | `[JS:TIMEOUT]` in log, UI responsive |
| T-R-003 | Inject 11 000 DOM nodes via innerHTML | `[WARN] DOM cap hit`, partial render |
| T-R-004 | `image.png` returns 404 | `[img]` placeholder, no crash |
| T-R-005 | Inject CSS with `calc(100% / 0)` or NaN-producing values | `[DIAG:NaN]` logged, no crash |

---

## Phase T+: Testing Infrastructure Expansion

> **Priority: 🟡 Medium — makes v1.0 verifiable**
> **Effort: 2 days / ~400 lines**

The Phase T infrastructure from Plan 03 is done (test.html, TestLogger, about:test routing,
reference screenshots). Phase T+ expands it systematically.

### T+.1 — JS Compatibility Test Matrix

Add Section F to `test.html`: **NiL.JS Compatibility Matrix**. Each test has
three outcomes: PASS, FAIL (documented limitation), SKIP (known unsupported).

```javascript
// test.html Section F — NiL.JS compatibility
function assertCompat(id, code, expected, knownBroken) {
    try {
        var result = eval(code);
        var pass = result === expected;
        log(id, pass ? 'PASS' : (knownBroken ? 'KNOWN-FAIL' : 'FAIL'), result);
    } catch(e) {
        log(id, knownBroken ? 'KNOWN-FAIL' : 'EXCEPTION', e.message);
    }
}

// Language features
assertCompat('F-001', '"key" in {key:1}', true, false);
assertCompat('F-002', '"key" in undefined', false, false);    // requires In.cs fix
assertCompat('F-003', 'class A{#x=1; get(){return this.#x;}}; new A().get()', 1, true); // known broken
assertCompat('F-004', '(async function*(){yield 1})()', '[object AsyncGenerator]', false);
assertCompat('F-005', '??= works', true, false);              // logical assignment
assertCompat('F-006', 'import.meta', '[object Object]', false);

// DOM
assertCompat('F-010', 'typeof document.querySelector', 'function', false);
assertCompat('F-011', 'typeof document.querySelectorAll', 'function', false);
assertCompat('F-012', 'document.createElement("div").tagName', 'DIV', false);
assertCompat('F-013', '!!document.getElementById', true, false);
```

### T+.2 — CSS Coverage Report

Add to `test.html` Section G: **CSS Coverage Summary**. A visual grid showing
each supported property with a green/yellow/red cell based on visual correctness.
Manual pass/fail per cell, updated each session.

Layout: a `<table>` with rows = CSS category (box model, flexbox, grid, text,
visual effects, positioning) and columns = property name. Each cell links to the
corresponding T-C-xxx test.

### T+.3 — Automated performance baseline in test.html

Instead of manually timing renders, add a JS snippet that:
1. Times `document.body.innerHTML = bigHtml` (measures JS→DOM mutation path)
2. Reports `[TEST:PERF] mutation=Xms` to the debug log
3. Tests 5 common CSS selectors using `querySelectorAll` and reports hit count

```javascript
// T-P-001: Layout performance
var start = Date.now();
var big = '<div class="container">'.repeat(100) + '</div>'.repeat(100);
document.getElementById('perf-target').innerHTML = big;
console.log('[TEST:PERF] innerHTML-100-divs=' + (Date.now()-start) + 'ms');
```

### T+.4 — Snapshot-assisted regression testing

The existing Snapshot button (Phase T.5) captures full-page PNGs. Add:
- After navigating to each T-S-xxx site, auto-suggest a snapshot filename
- The snapshot goes to `Pictures\MediaExplorer\regression\`
- Compare against the reference image in `Images/tests/` folder visually

No automated pixel comparison needed — this is a one-person project. The value is
having a reference image to diff against after a CSS change.

---

## Phase V: Visual Polish for v1.0

> **Priority: 🟡 Medium — makes the browser feel finished**
> **Effort: 2–3 days / ~300 lines**

These are the small things that make a browser feel like a real browser rather than
a debug tool. Most are already partially implemented.

### V.1 — Smooth AppBar animation ✅ `DONE (3.37)`

Replaced direct `BottomBar.Height = 52/24` with a manual async lerp using
`CubicEase` over 150ms (10 steps × 15ms). Avoids Storyboard+UWP layout
conflict by setting `BottomBar.Height` directly in step increments.
`ExpandBar/CollapseBar` delegate to `AnimateBarHeight(target)`.

### V.2 — Page loading progress indicator ✅ `DONE (3.37)`

Added a thin 3px `Border` (`#FF0078D7`) at the top of the content area.
On `LoadingChanged(true)`: visibility Visible, fills from 0→~80% over 2s
via async lerp. On `LoadingChanged(false)`: snaps to 100%, 200ms hold,
fade out over 200ms (8 steps). Guarded by `_loadProgressActive` flag.

### V.3 — Omnibox improvements ⏳ `DEFERRED`

Not yet implemented. Requires page title tracking from the render engine
and URL scheme coloring (RichEditBox vs TextBox). Will revisit after v1.0.

### V.4 — Better empty state ✅ `DONE (3.37)`

Replaced `Html/welcome.html` (former "Image Test Suite") with a proper
welcome page: dark theme, app name + tagline, description, 4 quick links
(`about:test`, `httpbin.org/html`, `text.npr.org`, `about:blank`), and a
random museum quote per visit (8 quotes from Berners-Lee/Gates/Kay/etc.).
CSS fallback in `ShowWelcomeAsync` also updated (dark bg, white title).

### V.5 — Error recovery button ✅ `DONE (3.37)`

Added "Try in POOR mode" button to `MessageOverlay`. Stored `_lastFailedAddress`
in `NavigateAsync`. On click: switches `RenderMode = "Poor"`, hides overlay,
re-triggers navigation. Button visible only when `isError && _lastFailedAddress != null`.

---

## Phase 7: Service Worker (Deferred, Still Planned)

> **Priority: 🟢 Nice-to-have — after v1.0**
> **Effort: 3–5 days / ~600 lines**

Unchanged from Plan 03. Prerequisites (Phase 6, Phase 5, Phase 8B) are all done.
Implement after v1.0 release. Minimal scope: `install` + `fetch` events only.

---

## Testing Strategy Summary

Since this is a solo project with no CI, no unit test framework, and an ARM device
as the primary target, testing has to be low-friction and high-signal.

### The 5-minute post-session ritual

After every session, before committing:

```
1. Build (0 errors required — never commit with errors)
2. Navigate to about:test
3. Scroll to Section C (JS tests) — note pass/fail count
4. Scroll to Section B (CSS tests) — visually check T-C-001..010
5. Navigate to T-S-001 (DuckDuckGo) — search box visible? ✓/✗
6. Navigate to T-S-004 (httpbin.org/html) — text readable? ✓/✗
7. Open DevTools Debug tab — any new [ERROR:] or [WARN:] markers?
8. Add one line to Doc/Perf_Baseline.md: date, build, pass count
```

If step 6 fails after passing before, **stop and fix before moving on**.

### Three layers of testing

```
Layer 1: about:test (automated, in-browser)
         HTML structure × CSS properties × JS behavior
         Runs on every launch — takes 30 seconds
         Catches: parser regressions, CSS cascade breaks, NiL.JS regressions

Layer 2: Site matrix (manual, weekly)
         T-S-001 through T-S-010 — 10 target sites
         Capture screenshots, compare to reference
         Catches: real-world rendering regressions

Layer 3: ARM device (manual, before major releases)
         Test on actual Lumia 950 (if available)
         Focus: performance (Snapdragon 810 ≠ x86 emulator), touch targets
         Catches: ARM-specific crashes, performance regressions
```

### What each test section covers

**Section A (T-H-xxx) — HTML Parser:**

Tests that the HTML tokenizer and tree builder produce correct DOM structure.
If T-H-003 (table with colspan) breaks, look at `HtmlParser.cs` and `TableRenderer.cs`.
These tests are almost never wrong — HTML parsing is stable.

**Section B (T-C-xxx) — CSS Engine:**

Tests that `CssLoader.cs` + `CssComputed.cs` + `DomBasicRenderer.cs` correctly
apply properties. If T-C-002 (flexbox) breaks after a cascade change, look at
`CascadeIntoComputedStyles` and `FlexPanel`.
Add a new T-C-xxx case before implementing any new CSS property.

**Section C (T-J-xxx) — JavaScript Runtime:**

Tests that NiL.JS + JsDomElement correctly handle JS operations.
A regression here usually means a NiL.JS change broke something.
Known-broken tests are marked `KNOWN-FAIL` — don't "fix" these by changing the test.

**Section F (T-F-xxx) — NiL.JS Compatibility Matrix:**

Static reference. Each row is a JS feature. Green = works, Yellow = partial,
Red = known unsupported (stop trying to fix these in main sessions).
Update once per major NiL.JS change session.

---

## Recommended Session Sequence (from 3.18)

```
Session 3.18: Phase R — in operator fix, InvalidOperationException suppression,
              NilJS_Compat.md, JS engine freeze comment ✅

Session 3.19: Phase 16.5 — CSS Stabilization (margin/padding, font-weight,
              text-decoration); VirtualizingRenderer margin fix, HTTPS→HTTP
              redirect, text.npr.org + example.com → iana.org working ✅

Session 3.20: Phase 20.1 — NiL.JS netstandard1.4 Migration (target switched
              from netstandard2.0 → 1.4). ~120 compile errors fixed across 20+
              files. Full solution 0 errors. ✅

Session 3.21: Phase 16.6 — CSS Grid + Flexbox routing + grid-template-areas.
              RenderCssGridAsync, repeat/minmax/fr/px/auto parsers, named area
              resolution, auto-placement. ya.ru renders (132 nodes). ✅

Session 3.22: Phase 16.7 — getComputedStyle real CSS values, @media (dppx,
              prefers-color-scheme, scripting), NiL.JS empty catch→logging,
              feature stubs (Proxy, WeakMap, WeakSet, Reflect, Intl,
              IntersectionObserver, ResizeObserver), ES polyfills, SafeEval
              context recovery. ✅

Session 3.22b: Crash fix — dzen.ru/pogoda/ unhandled JSException at
               Property.cs:88. RunGlobalScript wrapped in try/catch (missing
               after SafeEval rethrow change). EvalToString changed from
               _nil.Eval()→SafeEval() (was always throwing on GlobalContext).
               All 12 SafeEval callers now protected. ✅

Session 3.22c: NiL.JS platform-guard cleanup. Created
               scripts/refactor_ifdefs.ps1 — evaluates #if conditions
               against netstandard1.4 symbol table (NETSTANDARD1_4 defined;
               NETCORE, PORTABLE, NET40, NET35, WRC, NET461, NET48,
               NETSTANDARD1_3, NET40_OR_GREATER, JIT, CALLSTACKTOSTRING,
               DEV, GIVENAMEFUNCTION, TYPE_SAFE undefined). Handles three
               outcomes: KEEP (unknown symbols → leave guard),
               REMOVE_GUARD (always true → strip guards, keep code),
               REMOVE_BLOCK (always false → remove entire block).
               Removed all dead platform guards across the NiL.JS source;
               only `#if DEBUG` guards remain. ✅

Session 3.23: Phase C.1 — calc() + vw/vh resolver
               TryPx + EvaluateCalc already handled calc/vw/vh; added dvw/dvh.
               T-C-011 already existed + updated with 25dvw. ✅

Session 3.24: Phase C.4 — CSS custom properties (--var) scope fix
               Removed ResolveVariables(allRules) global pre-resolution.
               Fixed CascadeIntoComputedStyles: elements with no matching rules
               now get inherited CssComputed stored in result.
               Added T-C-013 test. Build 0 errors. ✅
               **T-C-013 still broken at runtime** — text nodes exist but
               var() appears to resolve to empty string. Added debug logging
               (MATCH/INLINE/INHERIT/EVAL_VAR/RESOLVE/BG/CASCADE START).
               Awaiting runtime log capture to identify root cause.

Session 3.24b: Debug logging for T-C-013
               Added 7 Debug.WriteLine points in CssLoader.cs prefixed
               [CssLoader-TCP13]. Build 0 errors. ✅

Session 3.25: Output-noise cleanup + Phase C.5 — clamp()
               Removed 9 [CssLoader-TCP13] Debug.WriteLine (autopilot,
                 no way to capture them) and 686 "empty catch empty
                 catch" Debug.WriteLine across Engine/*.cs (pure VS
                 Output noise — try/catch intent preserved as
                 `catch { /* swallow */ }`).
               T-C-016 (custom properties) **parked** — no runtime
                 test environment. Will resume when emulator/device
                 is available; add minimal gated log behind
                 `#if DEBUG_CSS` if it returns.
               Phase C.5: implemented `clamp(min, preferred, max)` in
                 `TryPx` and `EvaluateCalc`. Added T-C-017 to test.html.
                 Build 0 errors. ✅

Session 3.26: Phase C.6 — Basic CSS Transitions
                Add T-C-015 (manual test — tap, observe fade)

Session 3.26 ✅: implemented. Storyboard+DoubleAnimation/ColorAnimation
                for opacity/background-color/transform (scale+rotate+translate).
                Hover overrides in separate CssComputed.Hover; base cascade
                skips :hover/:focus/:active. New TransitionAnimator.cs (~90
                lines). T-C-015 extended to 4 boxes; new test_transitions.html
                with 11 demo cases. Build 0 errors. Summary_4_05.md.

Session 3.27: Phase S — NaN guards + JS timeout + DOM cap + error recovery +
               cascade/layout exception guards
               Run T-R-001..005, all should pass

Session 3.27 ✅ (partial, ~10 min):
  - S.1 NaN guards: added `SanitizeSize(v, fallback, source)` helper in
    DomBasicRenderer.cs. Applied to 5 hot-path sites:
    `css.Width/Height.Value` (line 4168/4170), `img.Width/Height@attr`
    (line 2439/2446 + 3285/3286 + 3445), `svg.canvas.Width/Height` (546/547).
    Logs `[DIAG:NaN] source=…` via DevToolsLogger on first hit per call site.
  - S.3 DOM cap: added `MaxDomNodes = 10000` const + `_nodeCount` counter in
    HtmlLiteParser.cs. When hit, parser stops creating new element nodes
    (keeps consuming input to keep page parseable), logs once via
    `[WARN] DOM cap hit at 10000 nodes — truncating`.
  - S.4 CSS rule cap: added `MaxCssRules = 5000` const in CssLoader.cs.
    When `rules.Count >= MaxCssRules`, further rules are dropped with
    `[WARN] CSS rule cap hit` log (once per ParseRules call).
  - S.2 (JS timeout) + S.5 (error page) + S.6 (cascade/layout try/catch
    audit) deferred to next session — larger refactors.
  - Build 0 errors.

Session 3.27: Phase C.6 follow-up — multi-property comma-separated transitions,
               smooth background from no-brush (Transparent → animate),
               % in translate(50%), «all» fix (comment + code confirmed).
               Support: TransitionList in CssComputed, ParseTransition parses
               all comma-segments, AttachHoverTransition iterates specs,
               TransitionSpecForProp resolves per-property timing.
               Parser: SplitTopLevelCommas reuse (was duplicate).
               Tests: test_transitions.html expanded (+3 cases: multi-dur,
               no-base-bg, % translate). Build 0 errors. ✅

Session 3.28: Nokia Design Archive analysis + JS compat fix.
               Problem: type="module" scripts use ES2020+ syntax (async
               generators, import.meta) that NiL.JS can't evaluate;
               nomodule scripts were ignored.
               Fix: JavaScriptEngine.RunScriptsAsync skips type="module"
               scripts; nomodule scripts execute normally (inverse of
               browser behavior → loads ES5 legacy bundle via SystemJS).
               Added: globalThis/global as real NiL.JS scope object
               (SafeEval("this")), System.import host function (Task.Run
               → FetchScriptStringAsync → RunInline), DIAG logging.
               Build 0 errors. ✅
               Note: Code written in autopilot (no emulator). Never
               live-tested. System.import and System.register format
               behavior unknown.

Session 3.29: Codebase verification + legacy site support assessment.
               Verified all 3.27/3.28 changes present in code.
               Build 0 errors. ✅ Summary_4_08.md.
               Key finding: System.import host function compiles but
               has never been executed. Next: live test on emulator.

Session 3.30: Live Nokia Archive test — **two runs**:
                Run 1: app crashed on CSS phase (race condition, not JS).
                Run 2: full render! System.import OK: 589152 bytes fetched
                and executed. Page renders 73 boxes + text but no D3.
                Root cause: System was read-only anonymous type.
                Fix: System → Dictionary<string, object>; Proxy.cs null-guard.
                Build 0 errors. ✅ Summary_4_08.md.

Session 3.31: Live Nokia re-test with Dictionary fix:
                Polyfill still couldn't extend System — polyfill overwrites
                System.import with DOM-based version. New approach:
                removed System pre-def entirely; standalone __sysImport
                host function; inline System.import(...) → __sysImport(...)
                intercept in RunScriptsAsync loop. Build 0 errors. ✅

Session 3.32: Live Nokia with __sysImport intercept — WORKS:
                `[DIAG:EXEC] len=82 preview="__sysImport(...)"` confirmed.
                `[DIAG:System.import] OK: 589152 bytes`. Modules call
                System.register(...) but execute() never invoked
                (__sysImport was fire-and-forget Task.Run).
                Fix: synchronous fetch GetAwaiter().GetResult();
                post-RunInline force-execute System.registry entries.
                Build 0 errors. Ready for next live test. ✅

Session 3.33: Polyfill's System broken (0 enumerable keys, no register/registry).
                 Replaced globalThis.System with minimal SystemJS (own register,
                 registry._entries, import→__sysImport) before legacy chunk runs.
                 Force-execute module.declare()→execute() after chunk.
                 Build 0 errors. See Doc/Summary_4_09.md. ✅

Session 3.34: Chunk's System.register never called (3 JSExceptions during parse).
                 Root cause: NiL.JS can't parse 589KB as single Eval().
                 Fix: split chunk into individual System.register(...) calls,
                 evaluate each separately via SafeEval. Inline C# parenthesis-
                 matcher. Build 0 errors. See Doc/Summary_4_10.md.

Session 3.34a (live test FAIL): split chunk into 0 System.register calls.
                 Root cause: paren matcher doesn't skip comments/regex/templates.
                 Enhanced matcher with comment/regex/template skipping.
                 Build 0 errors. See Doc/Summary_4_10.md §4.1.

Session 3.35 (live test PASS): split chunk into 1 System.register calls.
                 Module registered (_modId=1) and executed (force-exec: 1 declared).
                 JS phase DONE, page renders 1024×1024.
                 Next: Phase S — JS timeout + error page + cascade/layout guards.
                 ✅ DONE. See Doc/Summary_4_11.md.

Session 3.36: Phase S — JS timeout (DebuggerCallback 7s) + graceful error page
                  + RenderPipeline LayoutEngine guard
                  ✅ DONE. See `Doc/Summary_4_12.md`.

Session 3.37: Phase V — AppBar animation (async lerp CubicEase) + progress bar
                  + welcome page rewrite + error recovery button
                  ✅ DONE. See `Doc/Summary_4_12.md`.

Session 3.38: D3.js debugging R1 — discovered RunInline's silent `catch { /* swallow */ }`.
                  External scripts (d3.js, polyfills) now bypass RunInline → direct SafeEval.
                  Build 0 errors. ✅ See `Doc/Summary_4_14.md`.

Session 3.39: D3.js debugging R2–R5 — 5 polyfill approach iterations.
                    Discovered HostMapType/HostSetType are empty C# marker objects (silently
                    break `class extends Map/Set`). Approaches tried:
                    (1) `window.Map = Map$` — fails (window disconnected from global scope)
                    (2) bare `Map = Map$` — fails (HostMapType read-only, assign ignored)
                    (3) `_nil.Eval("(function(){...})()")` — fails (InvalidOperationException)
                    (4) `_nil.Eval("(function(){})")` step-by-step — fails (same error)
                    (5) try-catch wrapper evals + text replacement — works in code, pending test.
                    Key discovery: `_nil.Eval("try{...}catch(e){}")` works for function definitions.
                    Build 0 errors. ✅ See `Doc/Summary_4_14.md`.

Session 3.39b: D3 polyfill iteration (continued).
                    Attempted `_nil.Eval("try{...}catch(e){}")` for polyfill definitions →
                    still unreliable. Shifted to string-prepend approach: polyfill JS code as
                    string prefix + text replacements before eval.
                    Build 0 errors. ✅

Session 3.39c: D3 polyfill finalization (current session).
                    Analysis of actual minified d3.js v7.9.0 revealed:
                    - `super()` in comma-expression context: `if(super(),...)`
                      Fixed: `(this._d={},this.size=0)` instead of semicolon-separated
                    - `new Map` without parens: `{value:new Map}`
                      Fixed: `new Map` → `new __MapPolyfill` (all forms)
                    - `super.add(` for InternSet → `__SetPolyfill.prototype.add.call(this,`
                    - Constructors accept entries/values
                    - `forEach` accepts `thisArg`
                    Result: **0 NiL.JS JSExceptions during d3.js eval** (previously 3).
                    Still `d3=d3_missing` — JS-level error caught by try/catch.
                    Build 0 errors. ✅ See `Doc/Summary_4_15.md`.

Session 3.40: D3 strategy pivot — URL rewrite d3.v7 → d3.v5.
                     Log analysis of fresh binary (v0.42.8.0) showed:
                     - `let/const → var` text replacement NOT fixing 3 JSExceptions
                     - NEW `InvalidOperationException` from `;let{` → `;var {` (destructuring
                       with `var` still ES6 — NiL.JS can't parse)
                     - Text-replacement approach abandoned — too many ES6+ features to
                       transpile by string replace (for...of ×43, default params ×15,
                       destructuring, template literals, 11 classes)
                     New approach: URL rewrite `d3.v7` → `d3.v5` (ES5) in
                     `FetchScriptStringAsync`. Added `TryRewriteD3jsUrl` helper.
                     Removed broken `;let{` → `;var {` replacement.
                     d3.v5.min.js (v5.16.0, 248KB) confirmed live on d3js.org.
                     Build 0 errors. ✅ See `Doc/Summary_4_16.md`.

Session 3.41: NiL.JS parser depth StackOverflow root cause found & fixed.
                     Created NilJsTest test harness (+fetchd3, --depth, --verbose).
                     Added parser depth guard (state.ParserDepth + MaxParserDepth=100)
                     with vicinity info in ExpressionTree.Parse and Parser.Parse.
                     Fixed JSException(Error) constructor null-ref during parsing.
                     **Key fix:** comma operator parsing made iterative (was recursive)
                     — d3.v5's long comma chains (100+ items) caused depth 300+ →
                     fatal StackOverflow. Now both d3.v5.min.js (248KB) and
                     d3.v4.min.js (222KB) parse & execute at max depth ~25.
                     Build 0 errors. ✅ See `Doc/Summary_4_17.md`.

Session 3.42: Live test — UWP build with d3.v5 URL rewrite, deploy to emulator,
                  verify d3 loads and produces visual output.

Session 3.33+: Phase 7 (Service Worker) — optional, post-v1.0
```

### Test ID mapping in test.html

> **NOTE (3.25):** Duplicate T-C-013 was already resolved in test.html when the
> custom-properties test was added — the test was registered as `T-C-016`, not
> `T-C-013`. The table below reflects the actual test.html state.

| ID | Feature | Status |
|----|---------|--------|
| T-C-001–T-C-010 | Existing CSS tests (box model, flexbox, etc.) | ✅ |
| T-C-011 | calc() + vw/vh + 25dvw | ✅ (3.23) |
| T-C-012 | @media queries | ✅ (3.22) |
| T-C-013 | transform: translate() | ✅ |
| T-C-014 | grid-template-areas | ✅ (3.21) |
| T-C-015 | CSS Transitions (hover, manual) | ✅ — Phase C.6 (3.26) + multi-prop, smooth-bg, % translate (3.27) |
| T-C-016 | CSS custom properties (--var) | 🟡 Broken at runtime; debug logs removed in 3.25; resume when runtime testing possible |
| T-C-017 | `clamp()` | 🆕 — Phase C.5 (3.25) |

---

## Summary Table

| Phase | Title | Priority | Effort | Prerequisite | V1.0 blocker? |
|-------|-------|----------|--------|--------------|--------------|
| **R** | Rationalization & JS Freeze | 🔴 | 1 hour | — | ✅ Yes |
| **C** | CSS Completion (calc ✅, custom props 🟡, transitions) | 🔴 | 1–2 days remaining | R | ✅ Yes |
| **S** | Stability & Robustness (NaN guards, timeout, DOM cap, cascade/layout guards) | 🔴 | 2–3 days | R | ✅ Yes |
| **T+** | Testing Expansion (compat matrix, perf) | 🟡 | 2 days | S | No |
| **V** | Visual Polish | 🟡 | 2–3 days | S | No |
| **7** | Service Worker | 🟢 | 3–5 days | C, S | No |

**Minimum path to v1.0:** Phases R ✅ + C (1–2 days remaining) + S = **3–5 days of focused work**

---

## Longer-term vision (post v1.0)

After v1.0, the project has three potential directions, in order of feasibility:

**Direction A — "Better museum browser":** Improve CSS to 90%+, more target sites,
Service Worker for offline. This is achievable solo in 3–6 months.

**Direction B — "AI-first browser":** Lean into the AI differentiation — better Magic
Bubble, summarization, translation via OpenRouter. Makes the browser unique.
Achievable solo, relatively low JS dependency.

**Direction C — "Hybrid engine":** Add an `IBrowserEngine` abstraction with two
implementations: `CustomHtmlEngine` (current) and `EdgeHtmlEngine` (system WebView
as fallback). Sites that crash the custom engine silently fall back to EdgeHTML.
Medium complexity (~5–7 days), dramatically improves site compatibility.

Direction C is worth considering if the Nokia Archive goal remains important — it
would let you render complex SPA sites via EdgeHTML while keeping the custom engine
for lighter sites.

---

## Notes for Solo Development (updated)

**The `in` operator is the last trivial NiL.JS fix.** After 3.18, resist the urge to
dive back into NiL.JS for 5 sessions. Every NiL.JS session costs 3× more time than
planned (regression risk + debugging + re-test).

**CSS work is high leverage.** A morning on `calc()` makes 30% of modern sites look
significantly better. A morning on NiL.JS makes one site fail less silently.

**Know when a site is a fair test.** httpbin.org/html and text.npr.org are fair tests
for a museum browser — they're text-heavy sites where your renderer should shine.
Nokia Design Archive is an unfair test — it's a D3.js-heavy SPA that needs private
class fields, Canvas API, and full SVG DOM. Don't measure success against unfair tests.

**The POOR mode is your reputation manager.** If a site causes an exception, POOR mode
should always be able to show at least readable text. Users will appreciate graceful
degradation over hard crashes every time.

**Ship v1.0.** A released "museum-stable v1.0" that renders 5 sites correctly is more
valuable than an unreleased browser that almost renders 50 sites. Retro projects gain
momentum from tangible releases.

**Build mechanics normalised.** VS 2026 Insiders MSBuild (`C:\Program Files\Microsoft
Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe`) resolves the UWP WindowsXaml
SDK dependency. The dotnet SDK msbuild (`C:\Program Files\dotnet\sdk\10.0.300\MSBuild.exe`)
does NOT have UWP targets — always use the VS Insiders path. Restore first if
`project.assets.json` is missing (`/t:restore`).

---

## Build

**msbuild from VS 2026 Insiders resolves the UWP WindowsXaml SDK dependency.**

The command:
```
& "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe"
  Src\MediaExplorer.sln /p:Configuration=Debug /p:Platform=x86
```

`dotnet build` still works for `Src\NiL.JS\NiL.JS` (netstandard1.4) but chokes on the main UWP
project (needs WindowsXaml SDK targets from full MSBuild). Always restore first if `project.assets.json`
is missing:

```
& "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe"
  Src\MediaExplorer.sln /t:restore /p:Configuration=Debug /p:Platform=x86
```

The dotnet SDK msbuild (`C:\Program Files\dotnet\sdk\10.0.300\MSBuild.exe`) does NOT have the
UWP targets — ensure VS 2026 Insider msbuild is used.

---

*Plan v4.12 — 2026-06-06*
*Based on: Plan_01 (v1.0), Plan_02 (v2.2), Plan_03 (v3.5), sessions 2.01–3.42*
*Next session: 3.43 — UWP build with DOM stubs + verify `d3=d3_defined` restored*
*Summary files: Summary_4_05.md (C.6 initial), Summary_4_06.md (SafeEval guards), Summary_4_07.md (C.6 follow-up + Nokia), Summary_4_08.md (codebase verification + live test + __sysImport), Summary_4_09.md (minimal SystemJS replacement), Summary_4_10.md (chunk splitting + live test), Summary_4_11.md (regex/comment matcher fix), Summary_4_12.md (Phase S + Phase V), Summary_4_14.md (D3 root cause: HostMapType), Summary_4_15.md (D3 polyfill finalization: comma-expression super(), super.add, new Map/Set full coverage), Summary_4_16.md (D3 strategy pivot: URL rewrite d3.v7 → d3.v5), Summary_4_17.md (NiL.JS parser depth StackOverflow fix: iterative comma parsing), Summary_4_18.md (Log analysis: d3js.org skip broke `d3=d3_missing`, reverted skip + namespaceURI/ownerDocument stubs)*
