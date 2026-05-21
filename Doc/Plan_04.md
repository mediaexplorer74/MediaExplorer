# MediaExplorer / WEBVIEW — Plan 04: Pragmatic Path to v1.0

> **Project:** MediaExplorer 0.38.x (codename "WebView") — retro UWP museum browser for W10M
> **Hardware target:** Lumia 950/1020 (Snapdragon 810, 3 GB RAM, 5" 1440p)
> **Test environment:** x86 emulator (primary) → ARM device (validation)
> **Author note:** Plan 04 is the first plan written with explicit *honesty constraints* —
> it distinguishes between what is achievable, what is a trap, and what the project
> actually needs to feel finished. Read the Honest Assessment section before the phases.
> **Last updated:** 2026-05-24

---

## Honest Assessment: Where You Are and What That Means

### What has actually been built (remarkable)

After 35+ sessions across three plan documents, MediaExplorer is not a toy. It is:

- A working custom HTML parser + CSS cascade engine with a selector index (O(candidates) not O(n×rules))
- A XAML-based virtualizing renderer with element recycling and lazy image loading
- A NiL.JS 2.6 integration where a real 568 KB Vite bundle now **parses without syntax errors** — this required expert-level parser surgery across 9 sessions (3.9–3.17)
- 50+ browser globals, MutationObserver, ES Modules, incremental re-render, disk cache, reading modes, AI integration, DevTools with 4 tabs, structured test infrastructure

That is, objectively, a remarkable solo achievement. Most people who start "let's build a browser engine" projects abandon them at the "renders Hello World" stage.

### The current trap: the JS engine rabbit hole

Sessions 3.9–3.17 were almost entirely NiL.JS surgery. Every fix revealed another issue:

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
3.18 → in operator ← CURRENTLY STUCK
```

The pattern is an **infinite horizon**: after `in`, there will be private fields `#name`
(12 occurrences), then `Symbol`, then `WeakRef`, then more missing DOM APIs, then more
`InvalidOperationException` spam from NiL.JS internals. Each step is individually small,
but collectively this path leads to "implementing a browser DOM from scratch," which is
multi-year solo work.

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
| `in` operator fix | ❌ | 30 min |
| Private class fields `#name` (12 uses) | ❌ NiL.JS unsupported | 3–5 days |
| Full DOM event system (addEventListener on SVG, input) | ❌ Stubs only | 5–10 days |
| Canvas 2D API | ❌ Not started | 10–20 days |
| SVG DOM manipulation via JS | ❌ Not started | 10–15 days |
| CSS transitions driven by JS | ❌ Partial | 2–3 days |
| D3 data binding (enter/exit/update) via DOM mutations | ❌ Untested | Unknown |

**Total estimate for Nokia Archive to actually render:** 30–55 additional days of focused work.

**Recommendation:** Accept Nokia Archive as a "stretch goal that may never be reached" and
redirect to making the browser excellent for its real audience — simple to moderate sites on
a retro Lumia.

### What "v1.0 museum-stable" actually requires

| Requirement | Status | Effort |
|-------------|--------|--------|
| Fix `in` operator | ❌ | 30 min |
| CSS: calc(), vw/vh | ❌ | 1–2 days |
| CSS: grid-template-areas | ❌ | 2–3 days |
| CSS: proper @media queries | ❌ | 1 day |
| Robustness: NaN guards, JS timeout, DOM limit | ❌ | 1–2 days |
| Test suite: all T-H and T-C pass | 🟡 partial | 1 day |
| 5 target sites render acceptably | 🟡 some | Depends on CSS |
| No crash in 10 min normal browsing | 🟡 | Depends on stability |

**Total estimate for v1.0:** 8–12 additional days. This is absolutely achievable.

---

## v1.0 Release Criteria (explicit "done" definition)

A release is called v1.0 when ALL of these pass:

```
HTML:  T-H-001 through T-H-008 — all pass visually
CSS:   T-C-001 through T-C-010 — at least 8/10 pass visually
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

### C.2 — `@media` query improvements

Currently only `min-width` is supported. Add:
- `max-width`
- `min-height` / `max-height`
- `orientation: portrait | landscape`
- `prefers-color-scheme: dark | light` (always return `light` for retro browser feel)

```csharp
// CssLoader.cs — FlattenBasicMedia
private bool EvaluateMediaQuery(string mediaText, double vpW, double vpH)
{
    // e.g. "screen and (min-width: 768px) and (max-width: 1200px)"
    var conditions = ParseMediaConditions(mediaText);
    return conditions.All(c => c switch {
        ("min-width", var px) => vpW >= px,
        ("max-width", var px) => vpW <= px,
        ("min-height", var px) => vpH >= px,
        ("max-height", var px) => vpH <= px,
        ("orientation", "landscape") => vpW > vpH,
        ("orientation", "portrait") => vpH >= vpW,
        ("prefers-color-scheme", _) => true, // always match light
        _ => true // unknown → don't filter out
    });
}
```

**Test:** Add `T-C-012`: stylesheet with `@media (max-width: 600px)` rule — on emulator
(1024px wide) the rule should NOT apply; at 400px viewport it should.

### C.3 — `grid-template-areas`

Many modern layouts use named grid areas for hero/sidebar/footer patterns. This is the
most impactful missing layout feature after flexbox.

**Implementation:**

```csharp
// In FlexGridRenderer.cs or new GridAreaRenderer.cs
// Parse: grid-template-areas: "header header" "nav main" "footer footer"
// Returns 2D string[][]
// Assign Grid.Row / Grid.Column / Grid.RowSpan / Grid.ColumnSpan via attached properties
```

**Test:** Add `T-C-014`:
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

### C.5 — `clamp()` (bonus, if time allows)

```css
font-size: clamp(14px, 2vw, 20px)
```

One-liner: `return Math.Max(min, Math.Min(max, preferred))` after resolving each operand.

### Testing

After each C.x sub-phase: navigate to `about:test`, check the corresponding T-C-xxx row.
Log result in `Doc/Perf_Baseline.md`.

**Files affected:** `Engine/CssLoader.cs`, `Engine/CssComputed.cs`, `Html/test.html`

---

## Phase S: Stability & Robustness

> **Priority: 🔴 Required for v1.0**
> **Effort: 2–3 days / ~300 lines**

### S.1 — NaN/Infinity guards (systematic)

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

### S.2 — JS execution timeout

NiL.JS can loop forever. Wrap each `_nil.Eval()` call:

```csharp
// JavaScriptEngine.cs
private async Task EvalWithTimeout(string code, int timeoutMs = 5000)
{
    using var cts = new CancellationTokenSource(timeoutMs);
    try
    {
        await Task.Run(() => _nil.Eval(code), cts.Token);
    }
    catch (OperationCanceledException)
    {
        DevToolsLogger.Log("[JS:TIMEOUT] Script exceeded 5000ms — abandoned");
    }
}
```

### S.3 — DOM node cap

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

### S.4 — CSS rule cap

Verify the existing rule cap (5 000) is enforced. Add:
```csharp
if (_rules.Count >= MaxRules)
{
    DevToolsLogger.Log($"[WARN] CSS rule cap hit at {MaxRules}");
    return; // don't add more rules
}
```

### S.5 — Graceful error page

When `NavigateAsync` throws any unhandled exception, show the styled error page
(from Phase 11) instead of crashing or showing blank. Ensure the try-catch in
`NavigateAsync` always calls `ShowErrorPage(url, ex.Message)`.

### Testing

| ID | Test | Expected |
|----|------|----------|
| T-R-001 | Navigate to `not-a-url` | Error page shown, no crash |
| T-R-002 | Run `while(true){}` in DevTools console | `[JS:TIMEOUT]` in log, UI responsive |
| T-R-003 | Inject 11 000 DOM nodes via innerHTML | `[WARN] DOM cap hit`, partial render |
| T-R-004 | `image.png` returns 404 | `[img]` placeholder, no crash |

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

### V.1 — Smooth AppBar animation

Replace the current instant `BottomBar.Height = 52/24` toggle with a proper
`DoubleAnimation` over 150ms using the `EasingFunction = CubicEase`. The previous
attempt failed because of a UWP layout pass conflict. Use a clip animation on the
BottomBar's `RenderTransform.Y` instead of animating `Height`:

```csharp
// Animate TranslateTransform.Y from +28 to 0 (collapse from bottom)
// instead of animating Height (which conflicts with UWP layout engine)
var tt = new TranslateTransform();
BottomBar.RenderTransform = tt;
var anim = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(150),
    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
tt.BeginAnimation(TranslateTransform.YProperty, anim);
```

### V.2 — Page loading progress indicator

The existing `ProgressRing` shows while loading. Improve it:
- Show a thin top progress bar (like Chrome/Firefox) that fills from 0% to 100%
- Use `_resources.ActiveFetchCount` to estimate progress (reduce from max → 0)
- Auto-hide with 300ms fade after render completes

```csharp
// MainPage.xaml: thin 3px border at top of content area
// Width animates from 0 to ContentArea.ActualWidth proportionally
// Timer fires every 200ms during load to update progress
```

### V.3 — Omnibox improvements

- Show page title (not URL) in collapsed bar
- Show full URL only when expanded and focused
- On focus: select all text (Ctrl+A behavior)
- Display scheme (https://) in gray, host in white, path in lighter gray

### V.4 — Better empty state

When the welcome page is showing (no navigation yet), display:
- App name + version
- A short one-sentence description
- Quick links: about:test, httpbin.org/html, text.npr.org
- A randomly chosen "museum quote" about the web/technology from a small embedded list

### V.5 — Error recovery button

When an error page is shown, add a "Try in POOR mode" button. One tap switches to
POOR rendering mode and retries the URL. This is the museum browser's escape hatch.

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
              NilJS_Compat.md, JS engine freeze comment ← NEXT SESSION

Session 3.19: Phase C.1 — calc() + vw/vh resolver
              Add T-C-011, T-C-012 to test.html, verify pass

Session 3.20: Phase C.2 — @media improvements (max-width, orientation)
              Run T-S-002 (ya.ru) — check if mobile-first CSS now applies

Session 3.21: Phase C.3 — grid-template-areas
              Add T-C-014, run T-S-001 (DuckDuckGo) and T-S-002 (ya.ru)

Session 3.22: Phase S — NaN guards + JS timeout + DOM cap + error recovery
              Run T-R-001..004, all should pass

Session 3.23: Phase T+ — JS compat matrix Section F, CSS coverage grid Section G
              END of JS engine freeze — reassess NiL.JS work if needed

Session 3.24: Phase V — AppBar animation + progress bar + omnibox improvements

Session 3.25: Phase V — Error recovery button + welcome page polish

Session 3.26: Full regression run
              T-H-001..008: all visual pass
              T-C-001..014: target 10/14 pass
              T-J-001..010: target 7/10 pass
              T-S-001,004,005,007,008: all "acceptable" visually
              → If criteria met: TAG v1.0-museum

Session 3.27+: Phase 7 (Service Worker) — optional, post-v1.0
```

---

## Summary Table

| Phase | Title | Priority | Effort | Prerequisite | V1.0 blocker? |
|-------|-------|----------|--------|--------------|--------------|
| **R** | Rationalization & JS Freeze | 🔴 | 1 hour | — | ✅ Yes |
| **C** | CSS Completion (calc, media, grid-areas) | 🔴 | 4–6 days | R | ✅ Yes |
| **S** | Stability & Robustness | 🔴 | 2–3 days | R | ✅ Yes |
| **T+** | Testing Expansion (compat matrix, perf) | 🟡 | 2 days | S | No |
| **V** | Visual Polish | 🟡 | 2–3 days | S | No |
| **7** | Service Worker | 🟢 | 3–5 days | C, S | No |

**Minimum path to v1.0:** Phases R + C + S = **7–10 days of focused work**

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

---

*Plan v4.0 — 2026-05-24*
*Based on: Plan_01 (v1.0), Plan_02 (v2.2), Plan_03 (v3.5), sessions 2.01–3.17*
*Next session: 3.18 — Phase R (JS engine freeze + in operator fix)*
