# MediaExplorer / WEBVIEW — Plan 03: Testing-First Stabilization & Completion

> **Project:** MediaExplorer 0.40 (codename "WebView") — retro UWP browser for Windows 10 Mobile
> **Hardware target:** Lumia 950/1020 (Snapdragon 810, 3 GB RAM, 5" 1440p)
> **Test environment:** x86 emulator (primary) → ARM device (validation)
> **Engine:** Custom HTML parser + CSS cascade + NiL.JS + XAML renderer (~35 files, ~20 000 lines)
> **Last plan update:** 2026-05-19 (Plan_03 — post-session 3.7)

---

## Current State (as of session 3.6)

| Phase | Title | Status |
|-------|-------|--------|
| 0 | Infrastructure & Code Cleanup | ✅ DONE |
| 1 | Bottom Collapsible AppBar | ✅ DONE |
| 2 | CSS Cascade Optimization (selector index) | ✅ DONE |
| 3 | Layout Offload + VirtualizingRenderer | ✅ DONE |
| 4 | JavaScript Engine Improvements | ✅ DONE |
| 5 | Resource Loading (disk cache, priority queue) | ✅ DONE |
| 6 | ES Modules (NiL.JS native module loader) | ✅ DONE (needs Vite validation) |
| 7 | Service Worker + Offline-First | ⏸ DEFERRED (after Phase T + 8B) |
| 8A | MutationObserver API (NiL.JS host object) | ✅ DONE |
| 8B | Incremental Re-render (VirtualizingRenderer.Patch) | ✅ DONE |
| 9/14/15 | CSS Property Expansion (batches 1–4) | 🟡 IN PROGRESS |
| **10** | **DevTools Console + Inspector** | **✅ DONE** (Console, DOM, Network, Debug tabs) |
| 11 | UI Polish (loading, swipe, reading mode) | ✅ DONE |
| 12 | AI Integration (DeepSeek via OpenRouter) | ✅ DONE |
| 13 | White Screen Fixes, AppBar Modes, Settings Page | ✅ DONE |
| 16/17/18 | Image Loading Fix + Diagnostics | ✅ DONE |
| T | Testing Infrastructure | ✅ DONE |
| **19** | **DevTools Enhancement & ES Modules Validation** | **🟡 IN PROGRESS** |
| **20** | **NiL.JS 2.6 Integration (netstandard2.0 → 1.4)** | **✅ DONE** (fully builds for W10M 15063) |
| **16.5** | **CSS Stabilization (margin/padding, font-weight, text-decoration)** | **✅ DONE** (Session 3.19) |
| **20.1** | **NiL.JS netstandard1.4 Migration** | **✅ DONE** (Session 3.20 — ~120 compile errors fixed) |

**Known active issues:**
- **ES Modules Vite Compatibility**: ✅ **PARSER FIXED (Session 3.14)** — full 568KB bundle parses without syntax errors.
- **NiL.JS migrated to `netstandard1.4`**: Now compatible with UWP 15063 (W10M). Source in `Src/NiL.JS`, `ProjectReference` in `MediaExplorer.csproj`.
- **White screen on dzen.ru / ya.ru**: May still manifest.

**New Primary Target:**
- **Nokia Design Archive** (`nokiadesignarchive.aalto.fi`) — The "Museum Build" benchmark.

---

## Overview: What Plan 03 Adds

Plans 01 and 02 were **feature-driven**. Plan 03 is **quality-driven**. The single biggest gap after 18 sessions is the complete absence of a repeatable testing methodology. Every fix is validated by eyeballing a page, which means regressions are invisible and it's impossible to know what "done" looks like for a given feature.

Plan 03 introduces:
1. **Phase T — Testing Infrastructure** (highest priority, cross-cutting)
2. **Phase 8B — Incremental Render** (completes the MutationObserver story)
3. **Phase 15 — Rendering Modes** (FULL / RICH / POOR — partially started in README)
4. **Phase 16 — Remaining CSS** (transform, calc/vw/vh, CSS Grid areas, transitions)
5. **Phase 10 — DevTools** (in-app console + DOM inspector + network log)
6. **Phase 17 — Robustness & Memory** (crash hardening, memory budget enforcement)
7. **Phase 7 — Service Worker** (offline-first, deferred until after Phase T + 8B)

---

## Phase T: Testing Infrastructure

> **Priority: 🔴 Must have — enables everything else**
> **Effort: 3–4 days / ~600 lines**

### Why this is Phase T (not Phase 10 or lower)

Without reproducible tests, every subsequent phase is built on sand:
- A CSS fix for ya.ru might silently break DuckDuckGo
- An image fix might regress text rendering
- A NiL.JS change might break setTimeout-based animations that were working

The existing `[DIAG]` logging system is a foundation but it's write-only — there's no structured way to assert outcomes.

### T.1 — Embedded HTML Test Suite (`about:test`)

Add a special URL scheme `about:test` that loads a bundled multi-section test document from app resources (similar to how `welcome.html` works today, but structured as a test runner).

The test document is divided into four tabs/sections:

**Section A — HTML Structure Tests**

Each test is a labeled block that shows what _should_ render and what _actually_ renders side by side where possible:

```html
<!-- T-H-001: Headings -->
<h1>H1 Heading</h1>
<h2>H2 Heading</h2>
<h3>H3 Heading</h3>
<!-- Expected: decreasing font size, bold -->

<!-- T-H-002: Ordered and Unordered Lists -->
<ul><li>Apple</li><li>Banana</li></ul>
<ol><li>First</li><li>Second</li></ol>
<!-- Expected: bullets for ul, numbers for ol -->

<!-- T-H-003: Table with colspan/rowspan -->
<table border="1">
  <tr><th colspan="2">Header</th></tr>
  <tr><td>A</td><td>B</td></tr>
</table>

<!-- T-H-004: Form elements -->
<form>
  <input type="text" placeholder="Enter text">
  <input type="checkbox"> Checkbox
  <button type="submit">Submit</button>
</form>

<!-- T-H-005: Inline elements: <strong>, <em>, <code>, <a>, <abbr> -->
<p>This is <strong>bold</strong>, <em>italic</em>, <code>code</code>,
   <a href="#">link</a>, and <abbr title="abbr">abbr</abbr>.</p>

<!-- T-H-006: Block quotation and pre -->
<blockquote>This is a blockquote.</blockquote>
<pre>function hello() {
    console.log("pre-formatted");
}</pre>

<!-- T-H-007: Image with alt text, broken image -->
<img src="https://upload.wikimedia.org/wikipedia/en/a/a9/Example.jpg" alt="Example">
<img src="https://broken.invalid/image.png" alt="Broken">

<!-- T-H-008: Nested divs and spans -->
<div style="background:#eee; padding:10px">
  Outer div
  <span style="color:red">Red span</span>
  <div style="background:#ccc; margin:5px">Inner div</div>
</div>
```

**Section B — CSS Property Tests**

Each test is a self-contained cell showing one CSS property with an expected visual description below it. A failing test is visually obvious because the description won't match what you see.

```html
<!-- T-C-001: box-sizing: border-box -->
<div style="width:200px; padding:20px; border:5px solid black;
            box-sizing:border-box; background:#fde">
  Total width should be 200px (including padding+border)
</div>

<!-- T-C-002: flexbox row -->
<div style="display:flex; gap:10px; background:#edf">
  <div style="flex:1; background:pink; padding:5px">1</div>
  <div style="flex:2; background:lightblue; padding:5px">2 (twice as wide)</div>
</div>

<!-- T-C-003: text-overflow: ellipsis -->
<div style="width:150px; overflow:hidden; white-space:nowrap;
            text-overflow:ellipsis; border:1px solid">
  This text is too long and should be cut with ellipsis
</div>

<!-- T-C-004: background-image -->
<div style="width:100px; height:100px;
            background-image:url(https://httpbin.org/image/png);
            background-size:cover; border:1px solid">
</div>

<!-- T-C-005: border-radius (DISABLED on W10M — expect rectangular) -->
<div style="border-radius:12px; background:gold; padding:10px; width:120px">
  Should be rectangular on W10M (CornerRadius causes crash)
</div>

<!-- T-C-006: position: relative + absolute -->
<div style="position:relative; height:60px; background:#eed; border:1px solid">
  Relative container
  <div style="position:absolute; right:5px; top:5px; background:coral; padding:3px">
    Absolute child
  </div>
</div>

<!-- T-C-007: CSS Grid -->
<div style="display:grid; grid-template-columns: 1fr 1fr 1fr; gap:5px">
  <div style="background:tomato; padding:5px">Col 1</div>
  <div style="background:gold; padding:5px">Col 2</div>
  <div style="background:lightgreen; padding:5px">Col 3</div>
</div>

<!-- T-C-008: transform: rotate -->
<div style="display:inline-block; transform:rotate(15deg);
            background:skyblue; padding:5px; margin:20px">
  Rotated 15°
</div>

<!-- T-C-009: opacity -->
<div style="background:red; width:80px; height:40px; opacity:0.4">
  40% opacity
</div>

<!-- T-C-010: visibility: hidden (takes up space) -->
<div style="display:flex; gap:5px">
  <div style="background:blue; width:40px; height:40px">Visible</div>
  <div style="background:blue; width:40px; height:40px; visibility:hidden">Hidden</div>
  <div style="background:blue; width:40px; height:40px">Visible</div>
</div>
<!-- Expected: gap where hidden element is -->
```

**Section C — JavaScript Behavior Tests**

A self-contained JS test runner that writes `[PASS]` / `[FAIL: reason]` to a `<div id="js-results">`. This is the closest thing to unit tests possible without a separate test harness.

```html
<div id="js-results" style="font-family:monospace; font-size:12px"></div>
<script>
var results = document.getElementById('js-results');
var passed = 0, failed = 0;

function assert(name, condition, actual) {
    if (condition) {
        results.innerHTML += '<div style="color:green">[PASS] ' + name + '</div>';
        passed++;
    } else {
        results.innerHTML += '<div style="color:red">[FAIL] ' + name +
            (actual !== undefined ? ' — got: ' + actual : '') + '</div>';
        failed++;
    }
}

// T-J-001: Basic arithmetic and variable scoping
var x = 10;
assert("var arithmetic", x * 2 === 20);

// T-J-002: let block scoping
{ let y = 5; }
assert("let block scope", typeof y === 'undefined', typeof y);

// T-J-003: Array methods
var arr = [1,2,3];
assert("Array.map", arr.map(function(n){ return n*2; }).join(',') === '2,4,6');
assert("Array.filter", arr.filter(function(n){ return n > 1; }).length === 2);

// T-J-004: String methods
assert("String.includes", "hello world".includes("world"));
assert("String.trim", "  hi  ".trim() === "hi");

// T-J-005: setTimeout (async — check via flag)
var timerFired = false;
setTimeout(function(){ timerFired = true; }, 10);
setTimeout(function(){
    assert("setTimeout fires", timerFired);
}, 100);

// T-J-006: DOM access
var el = document.getElementById('js-results');
assert("getElementById returns element", el !== null);
assert("element.tagName", el.tagName === 'DIV' || el.tagName === 'div');

// T-J-007: DOM mutation
var div = document.createElement('div');
div.innerHTML = '<span>test</span>';
assert("createElement + innerHTML", div.innerHTML.indexOf('test') >= 0);

// T-J-008: JSON
var obj = JSON.parse('{"a":1,"b":"two"}');
assert("JSON.parse", obj.a === 1 && obj.b === 'two');
assert("JSON.stringify", JSON.stringify({x:42}) === '{"x":42}');

// T-J-009: Promise (basic)
var pFired = false;
Promise.resolve(42).then(function(v){ pFired = (v === 42); });
setTimeout(function(){ assert("Promise.resolve.then", pFired); }, 150);

// T-J-010: fetch (network — async)
setTimeout(function(){
    fetch('https://httpbin.org/get').then(function(r){
        assert("fetch status ok", r.ok, r.status);
    });
}, 200);

// Summary (delayed to allow async tests)
setTimeout(function(){
    results.innerHTML += '<hr><b>Results: ' + passed + ' passed, ' + failed + ' failed</b>';
}, 500);
</script>
```

**Section D — Integration Smoke Tests**

A curated list of 10 target sites, each annotated with what features they exercise. The tester manually navigates to each and logs observations in the project wiki / Issues:

| ID | URL | Key features exercised | Minimum acceptable result |
|----|-----|----------------------|--------------------------|
| T-S-001 | duckduckgo.com | Flexbox, forms, HTTPS | Search box visible, typing works |
| T-S-002 | ya.ru | CSS Grid, images, cyrillic | Logo + search bar visible |
| T-S-003 | dzen.ru | Dynamic JS (Svelte), images | Page structure visible, no crash |
| T-S-004 | httpbin.org/html | Basic HTML (Moby Dick) | Text readable, no crash |
| T-S-005 | example.com | Minimal HTML | Renders correctly |
| T-S-006 | lite.cnn.com | Lists, links, images | Articles list visible |
| T-S-007 | text.npr.org | Text-heavy, links | Articles readable |
| T-S-008 | lobste.rs | Tables, pagination | Link list visible |
| T-S-009 | hacker-news.firebaseapp.com | ES Modules, Firebase | Loads or degrades gracefully |
| T-S-010 | about:test | Our own test suite | All T-H / T-C pass visually |

### T.2 — Structured Test Logging

Extend the existing `[DIAG]` system with test-specific markers that can be grepped from debug output:

```
[TEST:PASS] T-J-001 var-arithmetic
[TEST:FAIL] T-J-007 createElement-innerHTML — got: undefined
[TEST:SITE] T-S-001 duckduckgo.com — RENDER OK (1 pass, 0 crash)
[TEST:PERF] cascade=14ms layout=32ms paint=11ms total=57ms
```

Add a `TestLogger` static class with `Pass(id, name)`, `Fail(id, name, actual)`, `Site(id, url, result)`, `Perf(phase, ms)` methods. All route to `Debug.WriteLine` with structured prefix.

### T.3 — Performance Regression Baseline

The `[DIAG]` markers already exist at key pipeline stages. Add timestamp capture and output a structured perf line after each full render:

```
[TEST:PERF] url=duckduckgo.com nodes=312 rules=1847 cascade=18ms layout=44ms paint=9ms total=71ms
```

Maintain a handwritten reference table in `Doc/Perf_Baseline.md` with expected ranges per target site. Any value 2× the baseline triggers investigation.

### T.4 — Visual Reference Screenshots

Maintain a `/Images/tests/` folder in the repo with reference screenshots for each T-C-xxx test case and each T-S-xxx target site. When a CSS change is made, compare the new render against the reference screenshot visually. Low-tech but effective for a solo project.

### T.5 — Snapshot Button (AppBar Screenshot)

Add a **Snapshot** button (📷 camera icon) to the bottom AppBar. On click:
1. Checks if the page is scrollable (if `ScrollViewer.ScrollableHeight > ViewportHeight * 1.5`)
2. For short pages: instant single-frame capture.
3. For long pages: automatic scrolling mode:
   - Calculates number of frames needed
   - Scrolls to each position with 250ms render delay
   - Captures each frame via `RenderTargetBitmap`
   - Stitches all frames vertically into one PNG
   - Restores original scroll position
4. Encodes to PNG and saves to `Pictures\MediaExplorer\` folder with auto-generated filename (with sanitization: replace `.` and `\` with `_`)
5. Shows status message: `"Snapshot saved: <filename>"`

```csharp
// MainPage.xaml.cs — SnapshotButton_Click
private async void SnapshotButton_Click(object sender, RoutedEventArgs e)
{
    if (IsScrollablePage())
    {
        await CaptureAndStitchAsync();
    }
    else
    {
        await CaptureSingleFrameAsync();
    }
}
```

**Files affected:**

| File | Change |
|------|--------|
| `MainPage.xaml` | Add SnapshotButton (Grid.Column 6, FontIcon Glyph="&#xE722;") |
| `MainPage.xaml.cs` | Add SnapshotButton_Click handler + filename sanitization |
| `Doc/Plan_03.md` | This section (T.5) |

### Files affected

| File | Change |
|------|--------|
| `Html/test.html` | New multi-section test document (A+B+C) |
| `Html/welcome.html` | Add link to `about:test` |
| `Engine/CustomHtmlEngine.cs` | Handle `about:test` URL → load `test.html` from resources |
| New `Engine/TestLogger.cs` | Structured `[TEST:]` logging helpers |
| New `Doc/Perf_Baseline.md` | Handwritten perf baseline table |
| New `Images/tests/` folder | Reference screenshots |

---

## Phase 8B: Incremental Re-render

> **Priority: 🔴 Must have for SPA correctness**
> **Effort: 4–6 days / ~700 lines**
> **Depends on:** Phase 8A (done), Phase 2 (done), Phase T (recommended first)

### Current state

Phase 8A delivered the MutationObserver API: mutations are recorded in `_pendingMutations`, observer callbacks are invoked via `EnqueueMicrotask`. However, each callback still triggers a full `RenderAsync()` — the incremental render path (`VirtualizingRenderer.Patch`) was not implemented.

### What changes

**Step 1: `CssLoader.CascadeSingle(LiteElement node)`**

Re-match CSS selectors for a single node using the existing selector index from Phase 2. O(candidates) instead of O(nodes × rules).

```csharp
// CssLoader.cs
public CssComputedStyle CascadeSingle(LiteElement node)
{
    var css = new CssComputedStyle();
    var keys = node.GetSelectorKeys(); // tag, .class, #id, *
    foreach (var key in keys)
        if (_selectorIndex.TryGetValue(key, out var rules))
            foreach (var rule in rules)
                if (rule.Matches(node))
                    ApplyRule(css, rule);
    return css;
}
```

**Step 2: `LayoutEngine.InvalidateSubtree(RenderObject node)`**

Mark a subtree dirty without re-laying-out the entire page:

```csharp
// LayoutEngine.cs
public void InvalidateSubtree(RenderObject root)
{
    root.IsDirty = true;
    foreach (var child in root.Children)
        InvalidateSubtree(child);
}
public async Task RelayoutSubtreeAsync(RenderObject root, double availableWidth)
{
    // Re-run layout only for root and its children
    await Task.Run(() => PerformLayoutRecursive(root, availableWidth));
}
```

**Step 3: `VirtualizingRenderer.Patch(MutationRecord[] mutations)`**

The core of Phase 8B. Instead of `Clear() + full rebuild`, patch only the affected XAML elements:

```csharp
// VirtualizingRenderer.cs
public async Task PatchAsync(InternalMutationRecord[] mutations)
{
    foreach (var m in mutations)
    {
        if (m.Type == "attributes")
        {
            // Re-cascade styles for the node, update XAML element in-place
            if (_visibleElements.TryGetValue(m.Target, out var xamlEl))
            {
                var newCss = _cssLoader.CascadeSingle(m.Target);
                await UpdateXamlElementStyleAsync(xamlEl, newCss);
            }
        }
        else if (m.Type == "childList")
        {
            // Remove XAML for removed nodes, add XAML for added nodes
            foreach (var removed in m.Removed)
                RemoveFromCanvas(removed);
            foreach (var added in m.Added)
                await AddToCanvasAsync(added);
        }
    }
}
```

**Step 4: Wire `CustomHtmlEngine` to use Patch instead of RenderAsync**

```csharp
// CustomHtmlEngine.cs — in mutation callback
if (_pendingMutations.Count > 0 && _renderer != null)
{
    var mutations = DrainMutations();
    await _renderer.PatchAsync(mutations);
}
else
{
    await RenderAsync(...); // full render only on first load
}
```

### Testing (using Phase T infrastructure)

T-J-007 already tests `createElement + innerHTML`. Add:
- `T-J-011`: `setAttribute` on visible element (CSS class change via `setAttribute('class', ...)`)
- `T-J-012`: `appendChild` to a visible container — new child should appear without flicker
- `T-J-013`: `removeChild` — element removed without full-page refresh
- `T-S-003` (dzen.ru): measure render time before/after 8B, expect significant improvement

---

## Phase 15: Rendering Modes (FULL / RICH / POOR)

> **Priority: 🟡 Medium — UX differentiator mentioned in README**
> **Effort: 2–3 days / ~300 lines**

The README already lists three rendering modes, and the basic AppBar mode switch is in Settings. This phase formalizes the rendering pipeline behind those modes.

### Mode definitions

| Mode | JS | CSS | Images | AI cursor | Magic Bubble | Use case |
|------|----|-----|--------|-----------|--------------|----------|
| FULL | NiL.JS enabled | Full cascade | Enabled | Optional | ✅ Long-tap → AI element explanation | Modern sites |
| RICH | MiniRunner only (timeouts/analytics-kill) | Full cascade | Enabled | Enabled | ✅ Long-tap → AI element explanation | Reading + AI |
| POOR | Disabled | Minimal inline (reader stylesheet) | Disabled | Enabled | ✅ Long-tap → AI content summary | E-book / FIDO-style |

### User philosophy (beyond technical implementation)

**RICH — "Reading mode with AI companion"**
- Technical: skip NiL.JS module loading, run only MiniRunner for timeouts/analytics-kill
- User experience: full visual fidelity (CSS + images) but no heavy JS execution
- **Magic Bubble**: long-tap / long-click on any element → overlay popup with AI-powered explanation of that element's content
- Initial implementation: stub popup (placeholder UI, no AI call yet)

**POOR — "E-book mode with magic summary"**
- Technical: no JS, minimal inline stylesheet, no images — plain text reading experience
- User experience: imitate an "e-reader" — strip CSS noise, show clean text, preserve readability
- **Magic Bubble**: long-tap / long-click anywhere → overlay popup with AI summary of the entire page content
- Initial implementation: stub popup (placeholder UI, no AI call yet)

**Magic Bubble — shared component**
- Trigger: long-tap (touch) or long mouse press (>500ms) on content area
- UI: semi-transparent overlay popup near the tapped position
- Content: depends on mode (RICH = element explanation, POOR = page summary)
- Phase 15 scope: UI stub only (show popup with placeholder text like "[AI: analyzing...]")
- Phase 12+ integration: wire to DeepSeek/OpenRouter for actual AI responses

### Implementation

Add `RenderMode` enum to `CustomHtmlEngine`. Each call to `RenderAsync` checks the mode:
- POOR: skip Phase 3 (JS), skip image fetch, apply minimal inline stylesheet
- RICH: skip NiL.JS module loading; run only MiniRunner for timeouts/analytics-kill
- FULL: current behavior

The Settings page "Rendering" ComboBox (already partially wired) controls this.

**POOR mode minimal stylesheet** (embedded string in `CustomHtmlEngine`):

```css
body { font-family: Segoe UI, sans-serif; font-size: 14px; margin: 8px; color: #111; }
a { color: #00b; }
img { display: none; }
h1, h2, h3 { font-weight: bold; }
```

### Testing

Add `T-M-001` to `T-M-003` in the test suite: navigate to `about:test` in each mode. Verify that in POOR mode images are hidden and JS tests are skipped (results `<div>` stays empty), in RICH mode the basic JS tests pass but module-dependent ones fail gracefully, in FULL mode all tests pass.

---

## Phase 16: Remaining CSS — Transform, calc(), CSS Grid Areas, Transitions

> **Priority: 🟡 Medium**
> **Effort: 5–7 days / ~800 lines**

### 16.1 CSS `transform`

The most visible gap: icons, arrows, chevrons, checkboxes all use `transform: rotate()`, `scale()`, `translateX()`.

```csharp
// DomBasicRenderer.cs — ApplyComputedStyles()
if (!string.IsNullOrEmpty(css.Transform))
{
    var tg = new TransformGroup();
    foreach (var fn in ParseTransformFunctions(css.Transform))
    {
        switch (fn.Name)
        {
            case "rotate":
                tg.Children.Add(new RotateTransform { Angle = fn.Arg0Deg });
                break;
            case "scale":
                tg.Children.Add(new ScaleTransform { ScaleX = fn.Arg0, ScaleY = fn.Arg1 });
                break;
            case "translateX":
                tg.Children.Add(new TranslateTransform { X = fn.Arg0Px });
                break;
            case "translateY":
                tg.Children.Add(new TranslateTransform { Y = fn.Arg0Px });
                break;
            case "translate":
                tg.Children.Add(new TranslateTransform { X = fn.Arg0Px, Y = fn.Arg1Px });
                break;
        }
    }
    el.RenderTransform = tg;
    // TransformOrigin already set in Phase 15
}
```

Test: `T-C-008` (already in the test suite).

### 16.2 `calc()` and viewport units (`vw`, `vh`)

Many sites use `width: calc(100% - 16px)` or `height: 100vh`. These are currently ignored, causing layout overflow or collapsed elements.

Strategy: resolve `calc()` and `vw`/`vh` expressions at cascade time when the viewport size is known.

```csharp
// CssLoader.cs — TryPx extension
private double? TryPxResolved(string value, double viewportW, double viewportH)
{
    if (value.StartsWith("calc("))
        return EvalCalc(value.Substring(5, value.Length - 6), viewportW, viewportH);
    if (value.EndsWith("vw"))
        return double.Parse(value.Replace("vw","").Trim()) * viewportW / 100.0;
    if (value.EndsWith("vh"))
        return double.Parse(value.Replace("vh","").Trim()) * viewportH / 100.0;
    return TryPx(value); // existing fallback
}
```

`EvalCalc` implements a minimal expression evaluator for `+`, `-`, `*`, `/` with `px`, `%`, `em`, `vw`, `vh` operands. No need for a full parser — a two-operand evaluator handles 95% of real-world `calc()` usage.

Test: Add `T-C-011`: `width: calc(100% - 40px)` inside a `200px` container → element should be `160px` wide.

### 16.3 `grid-template-areas`

CSS Grid `grid-template-areas` is what many modern layouts use for hero sections and sidebars. The current FlexPanel handles one-dimensional layouts; Grid areas need a two-dimensional pass.

Minimal implementation: parse `grid-template-areas` and `grid-area` on children, assign rows/columns via XAML `Grid.Row` and `Grid.Column` attached properties.

```csharp
// TableRenderer.cs or new GridAreaRenderer.cs
var areas = ParseGridAreas(css.GridTemplateAreas); // returns 2D string[][]
var grid = new Grid();
// Add row/column definitions from areas
foreach (var child in element.Children)
{
    var area = child.CssComputed.GridArea;
    var (row, col, rowSpan, colSpan) = areas.Find(area);
    var xamlChild = await RenderNodeAsync(child);
    Grid.SetRow(xamlChild, row);
    Grid.SetColumn(xamlChild, col);
    Grid.SetRowSpan(xamlChild, rowSpan);
    Grid.SetColumnSpan(xamlChild, colSpan);
    grid.Children.Add(xamlChild);
}
```

Test: `T-C-007` (3-column grid already present), add `T-C-014`: named grid areas (header / sidebar / main / footer layout).

### 16.4 Basic CSS Transitions

CSS transitions (opacity, color, transform) make hover effects and dropdowns work. Full XAML storyboards are expensive; use a lightweight approach:

- Parse `transition: property duration ease`
- On CSS property change, check if the element is in `_visibleElements`
- If yes, create a `DoubleAnimation` on the relevant XAML property with the specified duration
- Only support: `opacity`, `transform` (via `RenderTransform`), background-color changes

This is a "good enough" approximation — true CSS transitions require per-frame interpolation which is handled by XAML's animation system anyway.

Test: Add `T-C-015`: button with `transition: opacity 0.3s` and hover state (manual test — tap, observe fade).

---

## Phase 10: DevTools (In-App Console + DOM Inspector + Network Log)

> **Priority: 🟡 Medium — dramatically improves debuggability**
> **Effort: 3–5 days / ~600 lines**

### Architecture (unchanged from Plan 02, refined)

```
Settings → "Developer Tools"
  → Frame.Navigate(typeof(DevToolsPage))
    → Pivot:
         ├── Console: TextBox (input) + ScrollViewer (output log)
         ├── DOM: TreeView of LiteElement hierarchy
         └── Network: ListView of ResourceManager fetch log
```

### Console Tab

`console.log()` already routes to `Debug.WriteLine`. Add a `DevToolsConsole.Sink` that also appends to an `ObservableCollection<string>` bound to a `ListView` in the Console tab.

The input TextBox accepts any JS and runs it via `JavaScriptEngine.RunInlineJS(line)`. Output (return value and any thrown exception) appears in the log.

```csharp
// DevToolsPage.xaml.cs
async void ConsoleRun_Click(object sender, RoutedEventArgs e)
{
    var code = ConsoleInput.Text;
    ConsoleInput.Text = "";
    AppendLog("> " + code, Colors.White);
    try
    {
        var result = await MainPage.Current.Browser.EvalJavaScriptAsync(code);
        AppendLog("← " + result, Colors.LightGreen);
    }
    catch (Exception ex)
    {
        AppendLog("✕ " + ex.Message, Colors.Salmon);
    }
}
```

### DOM Tab

A `TreeView` bound to `LiteElement` tree. Each node shows `tagName#id.classes`. Tapping a node shows its computed style and attributes in a side panel. This directly replaces the need to stare at debug logs to understand why an element isn't rendering.

### Network Tab

`ResourceManager` already logs `[FetchText]` / `[FetchImage]` with URL and timing. Add a `NetworkLogEntry` class and a static `NetworkLog` list. Cap at 200 entries (ring buffer). The Network tab binds to this list.

### Testing value

The DevTools console is itself a test runner: the JS test page (Section C from Phase T) can be run directly from the console input, and the output appears in the log — no need to navigate to `about:test`.

---

## Phase 17: Robustness & Memory (Crash Hardening)

> **Priority: 🟡 Medium — required before claiming "museum-stable"**
> **Effort: 2–3 days / ~300 lines**

### 17.1 NaN/Infinity guards

`elementSize=NaNxNaN` still appears in logs (noted in session 2.18). This means some element gets an invalid size passed to `VirtualizingRenderer`, which either renders invisibly or causes layout corruption.

Audit: grep for every `.Width = `, `.Height = `, `new GridLength(` in `DomBasicRenderer.cs` and `VirtualizingRenderer.cs`. Wrap each in:
```csharp
if (!double.IsNaN(value) && !double.IsInfinity(value) && value >= 0)
    element.Width = value;
```

Add a `[DIAG:NaN]` log when a NaN is detected and the source property that produced it.

### 17.2 DOM size limit

Enforce the 10 000 element limit from Plan 01. Add a counter in `HtmlParser`. When exceeded, truncate the remaining tree and render a `<!-- DOM truncated at 10000 nodes -->` comment visible in DevTools.

### 17.3 CSS rule limit

Already planned (max 5 000 rules). Verify it's enforced; add a log `[WARN] CSS rule cap hit at {n}`.

### 17.4 JS execution timeout

NiL.JS can loop infinitely on buggy scripts (e.g., `while(true){}`). Add a `CancellationToken` with 5-second timeout wrapping each `_nil.Eval()` call. On timeout: log `[JS:TIMEOUT]`, abandon the script, continue rendering.

### 17.5 `OverflowException` / `ExecutionEngineException` guard

Both were fixed in Phase 0 but may recur on new sites. Ensure every entry into `CascadeIntoComputedStyles` and `PerformLayout` has a top-level try/catch that logs the exception with `[ERROR:CASCADE]` / `[ERROR:LAYOUT]` and continues rendering without crashing the app.

### Testing

Add `T-R-001` to `T-R-005` to the test suite:

| ID | Test | Expected |
|----|------|----------|
| T-R-001 | Load `about:test` with 500 CSS rules injected via `<style>` | No crash, renders |
| T-R-002 | Run `while(true){}` in DevTools console | 5s timeout, `[JS:TIMEOUT]` in log |
| T-R-003 | Load a page with 11 000 DOM nodes (stress HTML) | DOM truncated gracefully, visible message |
| T-R-004 | Image URL that returns 404 | `[img]` placeholder, no crash |
| T-R-005 | Navigate to an invalid URL (`not-a-url`) | Error page shown, no crash |

---

## Phase 7: Service Worker + Offline-First (Deferred, Now Re-Planned)

> **Priority: 🟢 Nice-to-have — enabled by Phase T + 8B + DevTools**
> **Effort: 3–5 days / ~600 lines**

Now that the module loader (Phase 6), disk cache (Phase 5), and MutationObserver (Phase 8A) are complete, Service Worker is architecturally feasible. It was deferred in Plan 02 because dynamic rendering was broken — Phase 8B fixes that.

### Minimal implementation plan

Only `install` + `fetch` events. No `activate`, no background sync.

```
navigator.serviceWorker.register('/sw.js')
  → SwContext (separate NiL.JS Context)
  → install event → cache specified URLs to LocalFolder\sw_cache\
  → fetch event → check sw_cache → if miss, pass to ResourceManager
```

The SW context has no `window`, `document`, or DOM. It exposes:
- `self` (the SW global)
- `caches.open(name)` / `caches.match(url)` / `caches.put(url, response)` — backed by `LocalFolder\sw_cache_{name}\`
- `fetch(url)` — backed by `ResourceManager.FetchTextAsync`

Testing via T-S-009 (hacker-news Firebase app) — it ships a service worker and this will exercise the full registration flow.

---

## Phase 19: DevTools Enhancement & ES Modules Validation

> **Priority: 🟡 Medium — improves debuggability + validates Phase 6**
> **Effort: 2–3 days / ~400 lines**

### 19.1 DevTools Enhancement (Session 3.6)

**Completed:**
- DOM tab: Connected `DumpDomTree()` to `_browser.GetActiveDom()`
- Network tab: Added `_networkLog` to `ResourceManager` with `GetNetworkLog()`
- Debug tab: New 4th tab for `[DIAG]` engine logs (orange color)
- All tab handlers updated to manage visibility correctly

**Files changed:**
- `MainPage.xaml` — Added DevDebugTab + DevDebugContent
- `MainPage.xaml.cs` — Added `_debugLogBuffer`, `DevDebugTab_Click`
- `Engine/ResourceManager.cs` — Added network logging
- `Engine/BrowserApi.cs` — Added `GetActiveDom()`

### 19.2 DevTools Logging Fixes (Session 3.7)

**Problem:** `[Module]` messages from `ModuleLoader` used `Debug.WriteLine()` only — invisible in Console/Debug tabs. Same for `[ImgTry]`/`[ImgSkipSvg]` in `DomBasicRenderer`.

**Changes:**
- `ModuleLoader.cs` — All `[Module]` diagnostics routed through `DevToolsLogger.Log()` instead of `Debug.WriteLine()` only
- `DomBasicRenderer.cs` — `log()` lambda (image loading diagnostics) now calls both `Debug.WriteLine()` and `DevToolsLogger.Log()`
- `CustomHtmlEngine.cs` — Added `[DIAG] SvgType available: true/false` diagnostic on each render

**Result:** Console/Debug tabs now show `[Module] Fetching`, `[Module] Eval error`, `[ImgTry]`, `[ImgSkipSvg]`, and `[DIAG] SvgType available:` messages.

**Observation (Session 3.7):** `SvgType available: true` on desktop target. `SyntaxError: Unexpected token (1:361)` confirmed — NiL.JS 2.5.1294 `Eval()` cannot parse Vite ES module bundles.

### 19.3 ES Module Test Suite (Session 3.7)

**Created `Html/TestModule/`:**
- `inline.html` — inline `<script type="module">` without imports
- `module-import.html` — `import { greet } from './lib.js'`
- `import-meta.html` — `import.meta.url` resolution
- `lib.js` — exported function + variable

Links added to `Html/test.html` as Section E (T-M-001 through T-M-003).

### 19.4 Next Direction: NiL.JS 2.6 Downshift

After auditing `Src/NiL.JS` (2.6 source), the downshift is feasible:

| Concern | Status |
|---------|--------|
| `Span<T>`, `stackalloc`, `ref struct` | **0 occurrences** |
| `ValueTask`, `IAsyncEnumerable` | **0 occurrences** |
| `System.Buffers`, `System.IO.Pipelines` | **0 occurrences** |
| `[Serializable]` (~170 uses) | Already guarded by `#if !(PORTABLE \|\| NETCORE)` |
| `AppDomain` (2 uses) | Already guarded by `#if !NETCORE` |
| `CompiledNode.cs` (JIT) | Already guarded by `#if !NETCORE` |
| `ValueTuple` polyfill | Need to extend `#if NET461` → `#if NET461 \|\| NETSTANDARD1_4` |
| `System.Reflection.Emit` | Available as NuGet for netstandard1.4 |

Switch from NuGet `NiL.JS 2.5.1294` → project reference `Src/NiL.JS` (with downshift).

---

## Phase 20: NiL.JS 2.6 Integration + Parser Patch (netstandard2.0 via ProjectReference)

> **Priority: 🔴 Must have — unblocks ES Modules validation**
> **Effort: 3–5 days / ~300 lines changed in NiL.JS + MediaExplorer**
> **Status: 🟡 IN PROGRESS (import.meta added, logical assignment next)**

### Background

Current project uses `NiL.JS 2.5.1294` as a NuGet package. Vite bundles fail with `SyntaxError: Unexpected token (1:361)` because `Eval()` cannot parse `import`/`export` declarations. The local source (`Src/NiL.JS`, version 2.6) has better ES module support but targets `netstandard2.1+`.

### What Was Done

**Step 1 — NiL.JS 2.6 netstandard2.0 build:**
- Added `<TargetFramework>netstandard2.0</TargetFramework>` to `NiL.JS.csproj`
- Polyfills in `Backward.cs`: `MaybeNullWhenAttribute`, `TypeBuilder.CreateType()` fix
- `Tools.cs:679`: `Enum.TryParse` → `Enum.Parse` under `NETSTANDARD2_0`
- Build: `msbuild /p:TargetFramework=netstandard2.0` → 0 errors

**Step 2 — Switch to ProjectReference:**
- Removed `PackageReference Include="NiL.JS" Version="2.6.0-local"`
- Added `ProjectReference Include="..\NiL.JS\NiL.JS\NiL.JS.csproj"`
- Linked source approach abandoned (300+ files incompatible with UWP)

**Step 3 — ModuleLoader rewrite:**
- `IModuleResolver` interface implementation (replaces `ResolveModuleEventArgs`)
- `Module.ModuleResolversChain.Add(this)` (replaces static event)
- `RunModule(key, code)` → `new JSModule(key, code, _nil).Run()`
- `_fetchTasks` for async prefetch of imported modules
- `ms-appx:///` fetch via `StorageFile.GetFileFromApplicationUriAsync`

**Step 4 — GlobalContext fix (InvalidCastException):**
- `_nil` type changed: `Context` → `GlobalContext` (both `JavaScriptEngine.cs` and `ModuleLoader.cs`)
- `_nilInit()`: `new Context()` → `new GlobalContext()`
- Removed `(GlobalContext)` cast — no longer needed

**Step 5 — Module Resolution Fix (baseUri tracking):**
- Added `_moduleBaseUris` dictionary to track parent module base URIs
- `TryGetModule` now uses `request.Initiator.FilePath` → lookup baseUri → resolve relative specifiers
- **T-M-002 ✅ PASS** — `import { greet } from './lib.js'` works correctly

**Step 6 — console.warn/error/info support:**
- Added `warn()`, `error()`, `info()` methods to `HostConsole` class
- Extended console getter to handle all 4 method names

**Step 7 — `import.meta` Parser Patch:**
- Created `Expressions/ImportMeta.cs` (following `NewTarget` pattern)
- Added `ImportMeta` property to `Module.cs` with `_oValue = Dictionary<string, JSValue>` initialization
- Added parser rules in `Parser.cs` (all 3 rule sets)
- Added `import.meta` case in `ExpressionTree.cs:parseOperand`
- `import.meta` now parses without `SyntaxError` and returns `{ url: filePath }` at runtime

### Vite Bundle Analysis (nokiadesignarchive.aalto.fi)

| Syntax Feature | Occurrences | ES Version | NiL.JS Support | Priority |
|----------------|-------------|------------|----------------|----------|
| `import.meta.url` | 1 | ES2020 | ✅ ADDED + runtime works | 🔴 |
| `import()` dynamic | 1 | ES2020 | ✅ Supported + graceful fallback | 🔴 |
| `async function*` | 1 | ES2018 | ✅ **FIXED (Session 3.11)** | 🔴 |
| Private fields `#name` | 12 | ES2022 | ❌ Not supported | 🟢 |
| Logical assignment `??=`, `\|\|=`, `&&=` | 172 | ES2021 | ✅ ADDED (Session 3.10) | 🔴 |
| Arrow functions | ~50 | ES2015 | ✅ **FIXED (Sessions 3.11, 3.14)** | 🔴 |
| **Async arrow functions with await** | ~20 | ES2017 | ✅ **FIXED (Session 3.14)** | 🔴 |
| **Destructuring with array defaults** | ~20 | ES2015 | ✅ **FIXED (Session 3.10)** | 🔴 |
| **Async methods in object literals** | ~50 | ES2017 | ✅ **FIXED (Session 3.11)** | 🔴 |
| **get/set as field names with colons** | ~30 | ES5 | ✅ **FIXED (Session 3.12)** | 🔴 |
| **IIFE after function (no semicolon)** | ~5 | ES5 | ✅ **FIXED (Session 3.14)** | 🔴 |
| **`const{x}` without space** | ~30 | ES2015 | ✅ **FIXED (Session 3.14)** | 🔴 |

**Step 13 — Full Bundle Parse (Session 3.14):**
- **568,328 characters** — parses completely without syntax errors!
- First runtime error: `ReferenceError: Variable "document" is not defined` (expected — DOM global)
- Parser position progress: 1133 → 133079 → 133673 → 150674 → 168266 → **FULL PARSE**

**Step 8 — Logical Assignment (`??=`, `||=`, `&&=`):**
- Created `Expressions/LogicalAssignment.cs` — handles all 3 logical assignment operators
- Added 3 new OperationTypes: `LogicalOrAssignment`, `LogicalAndAssignment`, `NullishCoalescingAssignment`
- Modified `ExpressionTree.cs` parser to recognize `||=`, `&&=`, `??=` syntax
- Added `Visitor.cs` method for LogicalAssignment
- **172 occurrences** in Vite bundle now work correctly ✅

**Step 9 — Destructuring with Array Defaults Fix:**
- Исправлен баг в `Parser.cs` в функции `skipExpression` внутри `ValidateDestructuring`
- Заменено общее условие на три отдельных с правильными парами скобок (`{}` `()` `[]`)
- **~20 occurrences** в Vite bundle теперь работают корректно ✅

**Step 10 — Async Methods in Object Literals Fix:**
- Исправлен баг в `ObjectDefinition.cs` — парсер теперь не парсит "async" как имя поля
- Добавлена переменная `nameStart`, чтобы правильно обрабатывать позицию после `async` или `*`
- **~50 occurrences** в Vite bundle теперь работают корректно ✅

**Step 11 — get/set Keywords as Regular Field Names Fix:**
- Исправлен баг в `ObjectDefinition.cs` — теперь парсер проверяет не только `(` после `get`/`set`, но и `:`
- Если после `get`/`set` идет `:`, то это обрабатывается как обычное имя поля, а не как getter/setter
- **~30 occurrences** в Vite bundle теперь работают корректно ✅

**Step 12 — Critical Regression Fix:**
- Исправлен критический баг, который сломал обычные (не‑async/не‑generator) методы в объектах
- Убрано условие сброса `i` только при наличии async/asterisk; теперь `i` всегда сбрасывается на `nameStart` для всех методов
- **All object‑literal parsing is now fully functional again! ✅

### Risks

- `import.meta` — ✅ ADDED to NiL.JS parser (Session 3.9)
- `typeof import` syntax — may still not be supported
- `async function*` (async generators) — ✅ **FIXED (Session 3.11)**
- **Destructuring with array defaults** — ✅ FIXED (Session 3.10)
- **Logical assignment `??=`, `\|\|=`, `&&=`** — ✅ ADDED (Session 3.10)
- **Async methods in object literals** — ✅ FIXED (Session 3.11)
- **get/set as field names with colons** — ✅ FIXED (Session 3.12)
- **Critical regression: normal methods in objects** — ✅ FIXED (Session 3.13)
- **Arrow functions in expressions** — ✅ FIXED (Sessions 3.11, 3.14)
- **Async arrow functions with await** — ✅ FIXED (Session 3.14)
- **IIFE after function declaration** — ✅ FIXED (Session 3.14)
- **`const{x}` without space** — ✅ FIXED (Session 3.14)
- Private fields `#name` — ❌ Not supported, 12 occurrences in Vite bundle
- **Full bundle parse** — ✅ **568KB parses without syntax errors! (Session 3.14)**

### Remaining Work

1. **`in` operator fix** — NiL.JS `In.cs:42` throws TypeError when RHS is not Object; need to return `false` for non-objects (browser-compatible behavior)
2. **Private fields `#name`** — 12 occurrences, lower priority
3. **Continue fixing runtime errors** in the bundle (many `InvalidOperationException` in NiL.JS)
4. **Log noise reduction** — 315 `empty catch` blocks spamming Visual Studio Output

---

## Summary & Sequencing

| Phase | Title | Priority | Effort | Depends on |
|-------|-------|----------|--------|------------|
| **T** | **Testing Infrastructure** | 🔴 | 3–4 days | — | ✅ DONE |
| **8B** | **Incremental Re-render** | 🔴 | 4–6 days | T, 8A | ✅ DONE |
| **15** | Rendering Modes (FULL/RICH/POOR) | 🟡 | 2–3 days | T | ✅ DONE |
| **16** | CSS: transform, calc, Grid areas, transitions | 🟡 | 5–7 days | T, 16.1 before 16.4 |
| **10** | DevTools (Console + DOM + Network + Debug) | 🟡 | 3–5 days | T | ✅ DONE |
| **17** | Robustness & Memory |  | 2–3 days | T |
| **19** | DevTools Enhancement & ES Modules Validation | 🟡 | 2–3 days | 10, 6 |  IN PROGRESS |
| **20** | NiL.JS 2.6 Integration + Parser Patch | ✅ DONE | ~300 lines | 19 | Full 568KB Vite bundle parses + Nokia Archive validates (Sessions 3.14–3.15) |
| **7** | Service Worker + Offline-First | 🟢 | 3–5 days | 8B, 10 |

**Total: ~22–33 working days / ~3300 lines**

---

## Recommended Session Sequence

```
Session 2.19: Phase T — test.html (Sections A+B+C), TestLogger, about:test routing ✅
Session 2.20: Phase T — site matrix first run, Perf_Baseline.md, reference screenshots ✅
Session 2.21: Phase 8B — CascadeSingle + InvalidateSubtree ✅
Session 2.22: Phase 8B — VirtualizingRenderer.Patch + wire to CustomHtmlEngine ✅
Session 2.23: Phase 15 — RenderMode enum + POOR/RICH implementations ✅
Session 2.24: Phase 16.1 — transform (rotate/scale/translate) [ALREADY DONE]
Session 2.25: Phase 16.2 — calc() + vw/vh resolver
Session 2.26: Phase 16.3 — grid-template-areas
Session 2.27: Phase 16.4 — basic CSS transitions
Session 2.28: Phase 10 — DevTools Console + DOM tab ✅
Session 2.29: Phase 10 — Network tab + TestLogger integration ✅
Session 2.30: Phase 17 — NaN guards, DOM limit, JS timeout
Session 2.31: Phase 7 — SwContext + install/fetch events
Session 2.32: Phase 7 — caches API + ResourceManager integration
Session 2.33: Full regression run against T-S site matrix; update Perf_Baseline.md
Session 3.4: DevTools Panel (Console + DOM + Network tabs) ✅
Session 3.5: DevTools Logger + Nokia Archive Focus ✅
Session 3.6: DevTools Enhancement (Debug tab, Network log, DOM tree) ✅
Session 3.7: Phase 19 — DevTools logging fixes, SVG diagnostic, ES Module test suite ✅
Session 3.8: Phase 20 — NiL.JS 2.6 integration, GlobalContext fix (InvalidCastException) ✅
Session 3.9: Phase 20 — Module resolution fix, console.warn, import.meta parser patch ✅
Session 3.10: Phase 20 — import.meta runtime fix, logical assignment (??=, ||=, &&=), destructuring with array defaults ✅
Session 3.11: Phase 20 — Arrow function fix (position 58), async function* support ✅
Session 3.12: Phase 20 — get/set as field names with colons ✅
Session 3.13: Phase 20 — Critical regression fix (normal methods in objects) ✅
Session 3.14: Phase 20 — **Full Vite bundle parses!** (import space, IIFE, const space, async arrow await) ✅
Session 3.15: Phase 20 — **Nokia Archive validation + UI polish** (Toast, MessageOverlay, Status Bar toggle, Nav fix) ✅
Session 3.16: Phase 20 — **NiL.JS `new keyword` fix** (MethodProxy + ExternalFunction), **HostLocation** (pathname, origin, etc.) ✅
Session 3.17: Phase 20 — **Massive globals expansion** — `_nilInit()` rewritten: 50+ JS globals (performance, crypto, URL, WebSocket, Blob, Event, Image, atob/btoa, requestAnimationFrame, full HostNavigator with 30+ properties, HostImage, window aliases) ✅
Session 3.18: Phase 20 — **`in` operator fix** (In.cs returns `false` instead of throw) + **JS ENGINE FROZEN**  — 9 sessions in rabbit hole, shift to CSS/Rendering for 5+ sessions ✅
Session 3.19: Phase 16.5 — **CSS Stabilization** — VirtualizingRenderer margin/padding fix, HTTPS→HTTP redirect handling, `text.npr.org` + `example.com` → `iana.org` working ✅

Session 3.20: Phase 20.1 — **NiL.JS netstandard1.4 Migration** — target framework changed from `netstandard2.0` to `netstandard1.4` (UWP 15063 compat). ~120 compile errors fixed via `Backward.cs` polyfills, property→`GetTypeInfo()` call-site changes, type stubs, and `#if` guards across 20+ files. Full solution builds with **0 errors**. ✅
Session 3.20: Phase 16.6 — CSS Grid/Flexbox for complex layouts (`iana.org`), CSS variables, `@media` queries ← NEXT
```

---

## Architecture State (post all phases)

```
HTML → LiteElement (HtmlParser)
  → JsDomElement ↔ NiL.JS (ES Modules, ModuleResolver)
                ↔ MutationObserver (8A done)
                          ↓ mutations
              MutationProcessor → CssLoader.CascadeSingle(node)   [8B ✅]
                                → LayoutEngine.InvalidateSubtree  [8B ✅]
                                → VirtualizingRenderer.Patch()    [8B ✅]
                                         ↓
            ServiceWorker (Phase 7) intercepts fetch → sw_cache\
                                         ↓
            ResourceManager (priority queue, disk cache, per-file lock)
                                         ↓
                         HTTP (cookie-aware, HSTS)

UI Thread:   MutationObserver tick → CascadeSingle → Patch XAML → Paint
Background:  LayoutEngine.RelayoutSubtreeAsync (dirty subtree only)
             ResourceManager fetch (IO-bound)
             ES Module graph resolution (CPU-bound)

DevTools:    Console → RunInlineJS → log output
              DOM tab → LiteElement tree (live via GetActiveDom)
              Network tab → ResourceManager fetch log (ring buffer 200)
              Debug tab → [DIAG] engine logs (filtered stream)

Rendering modes:
  FULL:  NiL.JS + CSS cascade + images
  RICH:  MiniRunner only + CSS cascade + images + AI cursor
  POOR:  No JS + minimal CSS + no images (FIDO-style)

Test infrastructure:
  about:test  → test.html (HTML/CSS/JS sections)
  TestLogger  → [TEST:PASS/FAIL/SITE/PERF] structured debug output
  Perf_Baseline.md → handwritten performance reference table
  Images/tests/   → visual reference screenshots
```

---

## Notes for Solo Development

Since this is a one-person retro project with no CI and no unit test framework:

**After each session:** run `about:test`, check JS results count, check site T-S-001 (DuckDuckGo), note any regressions. Five minutes of structured testing beats hours of debugging surprises.

**Before each CSS batch:** add the relevant T-C-xxx cases to `test.html` first. Write the test, see it fail, implement the feature, see it pass. Red → Green even without a formal test runner.

**Keep `Perf_Baseline.md` honest:** if cascade time doubles after a change, investigate before moving on. The Snapdragon 810 has no headroom.

**The POOR mode is your emergency exit:** if a site crashes the engine, switch to POOR and at least show the user readable text. This is the "museum browser" philosophy — graceful degradation over crash-and-burn.

**Future: Toast Notifications via ErrorOverlay:** The `ErrorOverlay` (`MainPage.xaml`, Grid.Row="1") is designed for "Aw, Snap!" crashes (large centered panel). It could be repurposed as a **toast notification** for non-critical events (Snapshot saved, Copied to clipboard, etc.) by showing it briefly (2-3s) with a transparent background and fading out. This would be far more visible on small screens than the current status bar. *(Idea noted for future implementation)*

---

*Plan v3.7 — 2026-06-03*
*Based on: Plan_01.md (v1.0), Plan_02.md (v2.2), sessions 2.01–3.20, GitHub repos mediaexplorer74/MediaExplorer + UDAIE-A/WEBVIEW*
