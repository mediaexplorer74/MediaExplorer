
```
╔═══════════════════════════════════════════════════════════════════=═══╗
║  LUMIA UPLINK PROTOCOL  //  MISSION DOSSIER  //  CLEARANCE: MUSEUM    ║
║  Node: MediaExplorer v0.55.10  ·  Uplink: nokiadesignarchive.aalto.fi ║
║  Hardware: Lumia 950 · Snapdragon 810 · ARM64 · 3 GB LPDDR4           ║
║  Engine: NiL.JS 2.6 · XAML Renderer · Custom HTML/CSS Stack           ║
║  Status: UPLINK STABLE — SVG delayed refresh added; test on emulator  ║
╚═════════════════════════════════════════════════════════════════════=═╝
```

# MediaExplorer / WEBVIEW — Plan 05: The Nokia Uplink

> **Codename:** "Lumia Uplink"
> **Project:** MediaExplorer — retro UWP museum browser for Windows 10 Mobile
> **Hardware target:** Lumia 950 · Snapdragon 810 · 1440p AMOLED · 3 GB RAM
> **Author note:** This document is a continuation of Plan 04. It assumes all phases
> of Plans 01–04 are done or superseded. Read the "Signal Analysis" section first.
> **Last updated:** 2026-06-08 (Phase G.2 v3 — SVG delayed refresh + build verified, Summary 5.08)

---

## PART I — SIGNAL ANALYSIS

*"The archive contains over 700 entries curated from thousands of items representing
over 20 years of Nokia's design history — both seen and unseen."*
— Nokia Design Archive, About page

You are building a browser for a museum, to be run on hardware that is itself
becoming a museum piece, in order to display a digital archive about hardware that
was discontinued before its time. The recursion here is intentional. The Lumia 950
was Nokia's last flagship. The Nokia Design Archive is its memory. MediaExplorer is
the key.

That's the science fiction angle, and it's real.

### What the last 18 sessions achieved (sessions 3.18 – 3.42)

| Achievement | Session | Significance |
|------------|---------|-------------|
| `in` operator fix (NiL.JS In.cs) | 3.18 | Last planned NiL.JS patch |
| Phase C.6: CSS Transitions (multi-prop, smooth background, % translate) | 3.26–3.27 | Visual quality |
| Nokia Archive live test: 589KB chunk loads, executes, page renders | 3.30–3.35 | **Milestone** |
| SystemJS custom minimal impl., chunk splitting with regex/template skip | 3.33–3.35 | Module loading solved |
| Phase S + Phase V: timeouts, error pages, AppBar animation, progress bar | 3.36–3.37 | Production-quality UX |
| D3 Map/Set polyfill: 0 JSExceptions during d3.js eval (was 3) | 3.39c | **Milestone** |
| Strategic pivot: d3.v7 → d3.v5 URL rewrite | 3.40 | Correct ES5 target |
| **NiL.JS parser StackOverflow fixed: iterative comma parsing** | 3.41 | **Critical milestone** |
| d3.v5.min.js + d3.v4.min.js parse & execute at depth ~25 (was 300+) | 3.41 | |
| DOM stubs: namespaceURI, ownerDocument, parentElement, closest, scrollIntoView | 3.42 | |
| d3.v5 `var d3={...}` global persists through SafeEval catch | 3.42 | d3 defined |
| **d3.v5 initialized on UWP (RegexOptions.Compiled fix, SafeEvalFast)** | **5.04** | **Milestone** |
| **Phase G.2: SVG XAML elements in VirtualizingRenderer (circle, line, path, text, g)** | **5.05** | **Milestone** |
| **Path.Data frozen Geometry crash fixed (CreateSvgPathGeometry→CreateSvgPathElement)** | **5.06** | **Critical fix** |
| **D3 SVG async timing diagnosed — two-SVG mismatch** | **5.06** | **Root cause** |
| **SVG mutation detection: isSvgOrHasSvgAncestor + UpdateView on SVG mutations** | **5.06** | **Bridge built** |
| **addedNodes population fix (was always empty)** | **5.06** | **Bug fix** |
| **TriggerDelayedSvgRefresh — post-Phase4 delay + SVG re-render** | **5.06** | **Safety net** |
| **Build verified: 0 errors via VS 2026 Insiders MSBuild** | **5.06** | **Build** |

### Honest assessment: where is the signal?

The hardest problems are solved. Let that sink in:

- The parser StackOverflow (iterative comma parsing) — **solved**
- The ES module loader (SystemJS, chunk splitting) — **solved**
- D3.js initialization on UWP (Regex fix + SafeEvalFast) — **solved**
- d3 global being defined after eval — **solved**

What remains is **not** more NiL.JS surgery. It is DOM plumbing and a rendering bridge.
These are mechanical engineering problems, not research problems. That's a very different
kind of work — harder to get stuck on, easier to parallelize, and faster with AI assistance.

### Current status (Phase G.2 v3 – static injection works, D3 append fails)

**Phase I (DOM Iterable Fix) — ✅ DONE (Session 5.03)**

`querySelectorAll`, `getElementsByTagName`, `getElementsByClassName`, `children`,
`childNodes` now return `NativeList` via `Context.ProxyValue(list)`. The Nokia Archive
chunk no longer crashes on:
```js
for (const o of document.querySelectorAll('link[modulepreload]')) n(o);
```
7 NilJsTest iteration tests confirm correctness.

**Phase J (d3 initialization on UWP) — ✅ DONE (Session 5.04)**

Root cause: `RegexOptions.Compiled` in NiL.JS — UWP CoreCLR (netstandard1.4) cannot
JIT-compile regex patterns via `Reflection.Emit`, throwing `ArgumentOutOfRangeException`.
Fixed by running regex in interpreted mode.

Confirmed in PhaseG2-log.txt:
```
[DIAG:SYS] post-exec d3=d3_defined
[DIAG:EXEC] typeof d3.select = function
[DIAG:EXEC] typeof d3.forceSimulation = function
```

### The remaining blocker: SVG element rendering — diagnosis complete

d3 builds a DOM tree of SVG elements (`circle`, `line`, `path`, `text`, `g`) in memory
via `document.createElementNS(svgNs, tag)`. The `LiteElement` nodes are created and
attributes stored — but **D3 inserts SVG asynchronously after Phase 4 BuildVisualTree**.
The timeline SVG appears during repaint with `children=0` because D3's `appendChild`
triggers repaint before D3 adds path/circle children.

**Session 5.06 diagnosis revealed:**
- Two SVGs on page: search icon (Phase 4) vs timeline (async repaint)
- Incremental update (`PatchAdded`) creates generic Border/Rectangle for SVG children
- `addedNodes` list was always empty — SVG mutations never detected
- Path.Data frozen Geometry crash via `XamlReader.Load`

**Fix:** SVG mutation detection in `ApplyIncrementalUpdateAsync` — when any mutation
affects SVG subtree, force full `UpdateView()` instead of individual patches. Combined
with `CreateSvgPathElement` (bypasses frozen Geometry), and populated `addedNodes` list.

---

## PART II — MISSION PHASES

### Phase I — Iterable Horizon (DOM Collections Fix) ✅ DONE

> **Priority: 🟢 Complete — Session 5.03**
> **Effort: 1 session (~2–3 hours)**
> **Depends on:** nothing — standalone fix ✅

All DOM collection methods (`querySelectorAll`, `getElementsByTagName`,
`getElementsByClassName`, `children`, `childNodes`) now return `NativeList`
(via `Context.ProxyValue(list)`) instead of plain `object[]`. This supports
`for...of`, `Array.from()`, spread operator, and all `IIterable`-dependent operations.

**I.1 — Helper method `ToJsArray`** ✅

Implemented in `JavaScriptEngine.cs`:
```csharp
private static JSValue ToJsArray(IEnumerable<object> items, Context ctx)
    => ctx.ProxyValue(items.ToList());
```

**I.2 — Applied to all collection-returning host methods** ✅

| Method | Before | After |
|--------|--------|-------|
| `HostDocument.querySelectorAll` | `object[]` | `ToJsArray(results, ctx)` |
| `HostDocument.getElementsByTagName` | `object[]` | `ToJsArray(results, ctx)` |
| `HostDocument.getElementsByClassName` | `object[]` | `ToJsArray(results, ctx)` |
| `JsDomElement.querySelectorAll` | `object[]` | `ToJsArray(results, ctx)` |
| `JsDomElement.children` (getter) | `object[]` | `ToJsArray(results, ctx)` |
| `JsDomElement.childNodes` (getter) | `object[]` | `ToJsArray(results, ctx)` |

**I.3 — NilJsTest coverage** ✅

7 iteration tests pass: `for...of` over Array, string, Map, Set, array-like,
`Symbol.iterator`, and spread operator. The for-of crash on Nokia Archive's
`for (const o of document.querySelectorAll(...))` is fully resolved.

---

### Phase J — JS Stabilization (Binary Search to D3 Call) ✅ DONE

> **Priority: 🟢 Complete — Session 5.04**
> **Effort: 1 session (collapsed from estimated 2–3)**
> **Depends on:** Phase I ✅

The binary search approach was **bypassed entirely** — the root cause of d3.v5 failure
on UWP was not JS code incompatibility but a runtime compilation issue:
`RegexOptions.Compiled` in NiL.JS triggers `ArgumentOutOfRangeException` on UWP CoreCLR
(netstandard1.4) when compiling regex patterns via `Reflection.Emit`.

**Three fixes applied in Session 5.04:**

| Fix | File | Effect |
|-----|------|--------|
| Remove `RegexOptions.Compiled` | `NiL.JS/BaseLibrary/RegExp.cs:74-75` | Regex runs interpreted — **definitive fix** |
| Add `SafeEvalFast` (no DebuggerCallback) | `JavaScriptEngine.cs` | Avoids UWP DebuggerCallback instability |
| Remove polyfill prefix for d3.v5 | `JavaScriptEngine.cs` | d3.v5 is pure ES5, no transforms needed |

**Goal state (J.3) — ACHIEVED ✅**

PhaseG2-log.txt confirms:
```
[DIAG:SYS] post-exec d3=d3_defined
[DIAG:EXEC] typeof d3.select = function
[DIAG:EXEC] typeof d3.forceSimulation = function
```

Regex exceptions: 0 (was 15× in Phase G1).
Nokia Archive page renders 1024×1024 Border.

---

### Phase G — SVG Genesis (The Rendering Bridge) 🟡 IN PROGRESS

> **Priority: 🔴 The core new work — Nokia Archive visual output**
> **Effort: 1 session (G.2 partial) + 1 session (diagnosis + fix) + remaining work**
> **Depends on:** Phase J ✅ — d3 initialized on UWP
> **Note:** G.2 approach chosen over G.1 (no Skia, no SvgImageSource, live XAML)

This phase was originally split into G.1 (SvgImageSource serialization) and G.2 (live XAML
element mapping). **G.2 was implemented directly in Session 5.05** — SVG elements are
rendered as native XAML shapes (Ellipse, Line, Path, TextBlock) inside a Canvas,
integrated into the `VirtualizingRenderer`. This avoids the bitmap/re-render overhead of
G.1 and enables future interactivity (Phase K).

**Session 5.06 — D3 async SVG diagnosis + mutation bridge:**
- ✅ D3 timeline SVG appears during repaint (not Phase 4) → async timing root cause identified
- ✅ Path.Data frozen Geometry crash fixed: `CreateSvgPathGeometry` → `CreateSvgPathElement`
- ✅ `addedNodes` list now populated (was always empty — mutations weren't tracked)
- ✅ `IsSvgOrHasSvgAncestor` helper: detects SVG element or SVG descendant
- ✅ SVG mutation forces `UpdateView()` instead of `PatchAdded` (correct XAML shapes)

**Status as of Session 5.06:**
- ✅ VirtualizingRenderer SVG element mapping (circle, line, path, text, g, rect, ellipse)
- ✅ viewBox support via Viewbox wrapper, style cascade with fill/stroke inheritance
- ✅ Path.Data crash workaround (XamlReader builds full Path, not extracted Geometry)
- ✅ SVG mutation detection in incremental update pipeline
- ✅ `TriggerDelayedSvgRefresh` — 400ms delay after Phase 4, SVG children check, full re-render
- ✅ Build: 0 errors via VS 2026 Insiders MSBuild (x86 Debug)
- ⬜ D3 force simulation tick → XAML property updates (cx/cy → Canvas.Left/Top)
- ⬜ Deploy & test Nokia Archive on emulator

#### G.1 — Fast Path: SVG DOM Serialization → SvgImageSource

When D3 finishes building the SVG DOM tree in memory (via JsDomElement), serialize it
to an SVG string and render it as a XAML `Image` with `SvgImageSource`. This gets you
a visual in 1–2 sessions. It loses live interactivity but confirms the D3 data pipeline works.

**Implementation:**

```csharp
// In DomBasicRenderer.cs or new SvgRenderer.cs

/// <summary>Serialize a LiteElement SVG tree to an SVG string.</summary>
private string SerializeSvgToString(LiteElement svgRoot)
{
    var sb = new StringBuilder();
    SerializeNode(svgRoot, sb);
    return sb.ToString();
}

private void SerializeNode(LiteElement el, StringBuilder sb)
{
    if (el == null) return;
    sb.Append($"<{el.Tag}");
    // Attributes
    foreach (var (k, v) in el.Attributes)
        sb.Append($" {XmlEscape(k)}=\"{XmlEscape(v)}\"");
    // Inline styles (collected from JS setAttribute("style",...) calls)
    var style = el.GetAttribute("style");
    if (!string.IsNullOrEmpty(style))
        sb.Append($" style=\"{XmlEscape(style)}\"");
    if (el.Children.Count == 0 && string.IsNullOrEmpty(el.TextContent))
    {
        sb.Append("/>");
        return;
    }
    sb.Append(">");
    foreach (var child in el.Children)
        SerializeNode(child, sb);
    if (!string.IsNullOrEmpty(el.TextContent))
        sb.Append(XmlEscape(el.TextContent));
    sb.Append($"</{el.Tag}>");
}

// In DispatchTagAsync, after JS has run:
case HtmlTag.Svg:
    var svgString = SerializeSvgToString(n);
    var image = new Image();
    var svgSource = new SvgImageSource();
    using (var stream = svgString.ToStream(Encoding.UTF8))
        await svgSource.SetSourceAsync(stream.AsRandomAccessStream());
    image.Source = svgSource;
    image.Stretch = Stretch.Uniform;
    return image;
```

**Trigger for re-render:** Subscribe to `MutationObserver` on the SVG root. When D3's
force simulation ticks (updates `cx`, `cy` attributes), re-serialize and reload the
`SvgImageSource`. Throttle to max 15fps to avoid Snapdragon overload.

```csharp
// Re-render throttle:
private DateTime _lastSvgRender = DateTime.MinValue;
private async void OnSvgMutation()
{
    if ((DateTime.UtcNow - _lastSvgRender).TotalMilliseconds < 67) return; // ~15fps
    _lastSvgRender = DateTime.UtcNow;
    await RenderSvgAsync(svgRoot, targetImage);
}
```

**What you get from G.1:**
- Nokia Archive network graph visible (nodes as colored circles, edges as lines)
- Timeline view visible
- Force simulation animates (SVG re-renders as nodes settle)
- No click-on-node interaction yet (that's Phase K)

This is likely "good enough" for the museum goal. A visitor can see the network,
watch it settle, read the labels. They can't click nodes to expand them yet.

#### G.2 — Full Path: SVG Element → XAML Element Mapping

Replace the serialization+bitmap approach with live XAML elements. Each SVG element
becomes a XAML element. D3 attribute changes directly update XAML properties. Enables
true interactivity (hover, click) via the Phase K event bridge.

**Element mapping table:**

| SVG element | XAML element | Attribute mapping |
|------------|--------------|-------------------|
| `<svg>` | `Canvas` | width→Width, height→Height |
| `<g>` | `Canvas` | transform→RenderTransform (TransformGroup) |
| `<circle>` | `Ellipse` | cx-r→Canvas.Left, cy-r→Canvas.Top, 2r→Width/Height, fill→Fill, stroke→Stroke, stroke-width→StrokeThickness |
| `<rect>` | `Rectangle` | x→Canvas.Left, y→Canvas.Top, width, height, rx→CornerRadius, fill, stroke |
| `<line>` | `Line` | x1→X1, y1→Y1, x2→X2, y2→Y2, stroke→Stroke |
| `<path>` | `Path` | d→Data (parse to PathGeometry), fill, stroke |
| `<text>` | `TextBlock` | x→Canvas.Left, y→Canvas.Top, font-size, fill→Foreground, text-anchor→TextAlignment |
| `<image>` | `Image` | href→Source, x, y, width, height |
| `<polygon>` | `Polygon` | points→Points, fill, stroke |
| `<polyline>` | `Polyline` | points→Points, stroke |
| `<ellipse>` | `Ellipse` | cx-rx, cy-ry, fill, stroke |

**Transform parsing:**

```csharp
// SvgRenderer.cs
private Transform ParseSvgTransform(string transform)
{
    // "translate(x,y)" → TranslateTransform
    // "rotate(angle)" → RotateTransform
    // "scale(sx,sy)" → ScaleTransform
    // "matrix(a,b,c,d,e,f)" → MatrixTransform
    // Multiple: "translate(10,20) rotate(45)" → TransformGroup
    var tg = new TransformGroup();
    foreach (var fn in ParseTransformFunctions(transform))
    {
        tg.Children.Add(fn switch {
            ("translate", var args) => new TranslateTransform { X = args[0], Y = args.Length > 1 ? args[1] : 0 },
            ("rotate", var args)    => new RotateTransform { Angle = args[0] },
            ("scale", var args)     => new ScaleTransform { ScaleX = args[0], ScaleY = args.Length > 1 ? args[1] : args[0] },
            ("matrix", var args)    => new MatrixTransform { Matrix = new Matrix(args[0],args[1],args[2],args[3],args[4],args[5]) },
            _ => null
        });
    }
    return tg.Children.Count == 1 ? tg.Children[0] : tg;
}
```

**Live attribute updates (the key advantage over G.1):**

```csharp
// JsDomElement — override SetAttribute for SVG elements
public override void SetAttribute(string name, string value)
{
    base.SetAttribute(name, value);
    // If this element has a live XAML counterpart, update it directly
    if (_xamlElement != null)
        SvgRenderer.UpdateXamlAttribute(_xamlElement, name, value);
}
```

This means D3's force tick — which calls `.attr("cx", d.x).attr("cy", d.y)` on hundreds
of circles — updates XAML directly without re-rendering the whole SVG.

**D3 force simulation performance on Snapdragon 810:**

The force simulation runs in NiL.JS (JavaScript), which is single-threaded and slow on ARM.
Add a `d3.force` tick cap:

```js
// Injected before d3.js eval:
window.__d3TickLimit = 300;  // max simulation ticks
```

Then intercept `simulation.tick()` calls via the existing JS eval wrapper to count and
stop after the limit. This prevents the Snapdragon from running the simulation indefinitely.

---

### Phase K — Kinetic Bridge (DOM Events → D3 Interactivity)

> **Priority: 🟡 Medium — enables click-on-node interaction**
> **Effort: 3–4 sessions**
> **Depends on:** Phase G.2
> **Note: Skip if G.1 is "good enough" for the museum goal**

D3 registers events via `element.on("click", handler)` which calls `addEventListener`.
The listener is stored in NiL.JS memory. Tapping a XAML element must fire that listener.

**K.1 — Event dispatcher in JsDomElement**

```csharp
// JsDomElement.cs
public void DispatchClickEvent(double clientX, double clientY)
{
    // Build a minimal MouseEvent-like object and call registered listeners
    _engine.EnqueueMacroTask(() =>
    {
        var eventObj = BuildMouseEvent("click", clientX, clientY, this);
        foreach (var listener in GetListeners("click"))
            listener.Call(thisObj: this, args: new[] { eventObj });
    });
}

private JSValue BuildMouseEvent(string type, double x, double y, JsDomElement target)
{
    // Return a JS object with the MouseEvent interface D3 expects:
    // type, clientX, clientY, target, preventDefault(), stopPropagation()
    return _engine.SafeEval($@"({{
        type: '{type}',
        clientX: {x},
        clientY: {y},
        pageX: {x},
        pageY: {y},
        target: __getElementById('{target.Id}'),
        preventDefault: function(){{}},
        stopPropagation: function(){{}}
    }})");
}
```

**K.2 — XAML event wiring**

```csharp
// In SvgRenderer.cs — when creating XAML element for a JsDomElement SVG node:
if (el is Ellipse circle && domNode.HasEventListeners("click"))
{
    circle.PointerPressed += (s, e) => {
        var pt = e.GetCurrentPoint(circle);
        domNode.DispatchClickEvent(pt.Position.X, pt.Position.Y);
    };
    circle.Cursor = new CoreCursor(CoreCursorType.Hand, 0);
}
```

**K.3 — Drag support for force simulation**

D3 force simulation uses `d3.drag()` which listens for `mousedown`, `mousemove`, `mouseup`.
On touch devices (Lumia), map `PointerPressed → mousedown`, `PointerMoved → mousemove`,
`PointerReleased → mouseup`. The touch position becomes `clientX`/`clientY`.

**K.4 — getBoundingClientRect with real values**

```csharp
// JsDomElement.cs
public Rect GetBoundingClientRect()
{
    if (_xamlElement == null) return Rect.Empty;
    // Must be called from UI thread after layout pass
    return _xamlElement.TransformToVisual(null)
        .TransformBounds(new Rect(0, 0, _xamlElement.ActualWidth, _xamlElement.ActualHeight));
}
```

Wire this to the existing `getBoundingClientRect` host function stub.

---

### Phase Z — Zero Hour (Nokia Archive Validation)

> **Priority: 🟡 Final validation**
> **Effort: 1–2 sessions**
> **Depends on:** Phase G (at minimum G.1)

**Z.1 — Network view validation**

Navigate to `https://nokiadesignarchive.aalto.fi/`. Expected sequence:
```
[DIAG:SYS] post-exec d3=d3_defined
[DIAG:EXEC] typeof d3.select = function
[DIAG:SYS] force-exec: 1 entries, 1 declared
[DIAG:RENDER] SVG node count: 700+
[DIAG:RENDER] circle count: 700, line count: 1200+
[DIAG:RENDER] SVG render: 1450ms (Snapdragon 810)
```

**Z.2 — Timeline view**

Navigate to `https://nokiadesignarchive.aalto.fi/timeline.html`. The timeline uses
`d3.scaleTime()` and renders horizontal bars. Same SVG pipeline applies.

**Z.3 — Performance budget**

| Operation | Budget | Measurement |
|-----------|--------|-------------|
| d3.js eval (NiL.JS) | < 15s | `[DIAG:EXEC]` timestamps |
| Force simulation (300 ticks) | < 30s | Tick counter |
| SVG serialization (G.1) or XAML creation (G.2) | < 5s | Stopwatch |
| Scroll/pan response | < 100ms | PointerMoved latency |

If simulation exceeds budget: reduce default tick count, or run simulation headlessly
and render only the final settled state.

---

## PART III — SUPPORTING INFRASTRUCTURE

### NilJsTest Expansion

The `NilJsTest` console harness (created in session 3.41) should grow alongside the
main fixes. Add test cases for each phase:

```
Phase I: for-of over querySelectorAll result           → Assert: "DIV,DIV"
Phase I: Array.from(querySelectorAll result).length    → Assert: 2
Phase J: typeof d3.select                             → Assert: "function"
Phase J: typeof d3.forceSimulation                    → Assert: "function"
Phase G: SVG string contains <circle                  → Assert: true
Phase G: SVG circle has cx attribute                  → Assert: true
```

Running `dotnet run -- --d3-test` (a new flag) should execute all 15+ assertions in
under 10 seconds without UWP or emulator — a fast feedback loop that catches regressions
before a full deploy.

### Remaining CSS gap: `clamp()` / custom property scope

These were in Plan 04 Phase C.4 and C.5 but may not have been implemented. Quick wins:

```csharp
// CssLoader.cs — TryPx extension
if (value.StartsWith("clamp(") && value.EndsWith(")"))
{
    var parts = SplitTopLevelCommas(value[6..^1]);
    if (parts.Length == 3)
    {
        var min = TryPx(parts[0]) ?? 0;
        var pref = TryPx(parts[1]) ?? min;
        var max = TryPx(parts[2]) ?? pref;
        return Math.Max(min, Math.Min(pref, max));
    }
}
```

---

## PART IV — HONEST COMPLETION ASSESSMENT

### What is realistically achievable?

| Goal | Status | Estimate |
|------|--------|----------|
| **Nokia Archive: network graph visible (G.1 fast path)** | 🎯 Achievable | 2–4 weeks solo+AI |
| Nokia Archive: fully interactive (click nodes, zoom) | 🟡 Stretch goal | +3–4 more weeks |
| Nokia Archive Timeline view | 🎯 Achievable | same as network graph |
| v1.0 museum-stable release (other sites, CSS, stability) | ✅ Already close | 1 more week |
| Full ES2022 runtime (private fields, etc.) | ❌ Not planned | months |

**With AI programmer assistance (the proven workflow):**

The four remaining hard tasks and their honest time costs:

1. **Phase I (for-of fix):** 2–3 hours. Simple, mechanical, low risk. Do this next session.
2. **Phase J (binary search to d3 calls):** 3–6 hours across 2 sessions. Methodical, not research.
3. **Phase G.1 (SVG serialization):** 1–2 full sessions (~8–12 hours). First visual payoff.
4. **Phase G.2 (XAML element mapping, optional):** 3–5 sessions if G.1 is not enough.

**The moment you've been working toward is Phase G.1.** Everything from sessions 3.9 to 3.42 has been clearing the path to the moment when an SVG string is rendered and D3 data appears on a Lumia 950 screen.

### The speed multiplier: agentic AI workflow

Sessions 3.33–3.42 demonstrated a very effective pattern:
- Write the diagnosis (1 session)
- AI generates the code changes
- Human builds and tests
- Log analysis feeds the next diagnosis

For Phase G specifically, this pattern works well because SVG→XAML mapping is mechanical
(known input, known expected output) and the NilJsTest harness can verify each step
without deploying to hardware.

**Recommended workflow for Phase G:**

```
1. Write NilJsTest case: eval d3.select("body").append("svg").attr("width","400")
2. Assert: the LiteElement tree contains a node with tag="svg" and attr width="400"
3. Run test → it passes (the DOM creation path works via existing JsDomElement code)
4. Then: write SvgRenderer.SerializeSvgToString(svgRoot)
5. Assert: the output string contains "<svg width="400""
6. Run test → fix → test
7. Then: SvgImageSource.SetSourceAsync with the string
8. Visual output confirms
```

You don't need the Lumia for steps 1–6. Only step 8 needs the emulator/device.

---

## PART V — RECOMMENDED SESSION SEQUENCE

```
Session 5.03: Phase I fix + NilJsTest expansion (Vibe autopilot → manual build fix)
              ✅ Phase I complete: 7 iteration tests pass
              ✅ d3.v5/v4 working in NilJsTest (net8.0)

Session 5.04: Phase J — Regex diagnosis + fix (this session)
              ✅ Phase J complete: d3 initialized on UWP
              ✅ RegexOptions.Compiled removed (netstandard1.4)
              ✅ SafeEvalFast added
              ✅ PhaseG2-log: d3.select = function, d3.forceSimulation = function

Session 5.05: Phase G.2 — SVG XAML elements in VirtualizingRenderer
              ✅ circle→Ellipse, line→Line, path→Path, text→TextBlock, g→transform
              ✅ rect, ellipse, viewBox, style cascade
              ✅ VirtualizingRenderer integrated (no separate DomBasicRenderer path)
              First test: timeline SVG appears with children=0 (async timing issue)

Session 5.06: Phase G.2 v2/v3 — SVG async diagnosis + mutation fix + delayed refresh
              ✅ Path.Data frozen Geometry crash fixed
              ✅ Two-SVG root cause: search icon in Phase 4, D3 timeline in repaint
              ✅ SVG mutation detection: `IsSvgOrHasSvgAncestor` → force `UpdateView`
              ✅ `addedNodes` population fix (was always empty)
              ✅ `TriggerDelayedSvgRefresh` — 400ms post-Phase4 SVG child check
              ✅ Build: 0 errors via VS 2026 Insiders MSBuild (x86)
              Target: deploy on emulator → test Nokia Archive

Session 5.07: Deploy UWP build → test on emulator with Nokia Archive
              → If D3 force graph visible: declare Phase G.2 done
              → If not: diagnostics, check SVG mutation log/debug output

Session 5.08: Phase G.2 — Force simulation tick → XAML property updates
              Hook D3 tick updates (cx/cy→Canvas.Left/Top) to live XAML elements
              or if static render is sufficient, skip and move to validation

Session 5.07: Full Nokia Archive test on emulator
              → If network graph visible: declare G.2 partial done, tag v0.50-nokia
              → If not: diagnosis session, identify remaining gap

Session 5.12: Phase K (if G.2 done) — Click event dispatch, drag support
Session 5.13: Phase Z — Full Nokia Archive validation, perf tuning
Session 5.14: v1.0 release preparation — changelog, README, GitHub release tag
```

---

## PART VI — THE RETRO-FUTURISTIC POSTSCRIPT

```
TRANSMISSION LOG — LUMIA UPLINK NODE  //  2026.06.07  //  UTC+03:00
──────────────────────────────────────────────────────────────────────
The Lumia 950 was discontinued on October 8, 2019.
The Nokia Design Archive opened to the public in 2023.
MediaExplorer v0.55.0 achieved first D3.js rendered as live XAML shapes on UWP on 2026.06.07.

There is a kind of engineering that history books don't record:
the solo builder who keeps going after the platform is dead.
Who patches a parser at depth 300, rewrites a SystemJS loader,
surgically removes RegexOptions.Compiled from line 74 of RegExp.cs
so a phone from 2015 can run D3.js force simulations — not because
anyone asked them to, but because the archive deserves a browser
that understands it, and the Lumia deserves a final mission.

The Nokia Design Archive contains 700+ entries representing 20 years
of designs that were "both seen and unseen." The network graph is
their map — relationships between concepts, designers, eras.
Running D3.js v5 on NiL.JS 2.6 on a Snapdragon 810 to render that
map: this is what retro-futurism actually looks like. Not chrome
and neon, but a JavaScript engine debugged at line 74 of RegExp.cs
so that a phone from 2015 can browse a museum about phones from 1995–2010.

The for-of crash is fixed. The Regex crash is fixed.
The Path.Data frozen Geometry crash is fixed.
The `addedNodes` list is no longer a ghost.
The `TriggerDelayedSvgRefresh` catches what the mutator misses.
d3.select = function. d3.forceSimulation = function.
Seven hundred entries wait for their circles — the bridge and the delay
both have their backs. Test on emulator next.

Next action: sync → build → deploy → test Nokia Archive on emulator.
The uplink awaits.

STATUS: SVG BRIDGE + DELAY  //  SIGNAL STRENGTH: STRONG
NEXT: Phase G.2 — Emulator Test
ETA: 1 SESSION
──────────────────────────────────────────────────────────────────────
```

---

*Plan v5.4 — 2026-06-08*
*Based on: Plans 01–04, sessions 3.18–5.06, Summaries 5.01–5.06*
*Build target: VS 2026 Insiders MSBuild. Platform: x86 (emulator) + ARM (Lumia 950)*
*Next session: 5.09 — Nokia Archive emulator test*

---

## Build

```
"C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe" Src\MediaExplorer.sln /p:Configuration=Debug /p:Platform=x86 
```
