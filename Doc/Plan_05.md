
```
╔═══════════════════════════════════════════════════════════════════════════════════════╗
║  LUMIA UPLINK PROTOCOL  //  MISSION DOSSIER  //  CLEARANCE: MUSEUM                    ║
║  Node: MediaExplorer v0.57.100  ·  Uplink: nokiadesignarchive.aalto.fi                ║
║  Hardware: Lumia 950 · Snapdragon 810 · ARM64 · 3 GB LPDDR4                           ║
║  Engine: NiL.JS 2.6 · XAML Renderer · Custom HTML/CSS Stack                          ║
║  Status: SVG→XAML BRIDGE ABANDONED — Simplifying to text/images/links                 ║
╚═══════════════════════════════════════════════════════════════════════════════════════╝
```

# MediaExplorer / WEBVIEW — Plan 05: The Nokia Uplink

> **Codename:** "Lumia Uplink"
> **Project:** MediaExplorer — retro UWP museum browser for Windows 10 Mobile
> **Hardware target:** Lumia 950 · Snapdragon 810 · 1440p AMOLED · 3 GB RAM
> **Author note:** This document is a continuation of Plan 04. It assumes all phases
> of Plans 01–04 are done or superseded.
> **Last updated:** 2026-06-12 — SVG→XAML BRIDGE ABANDONED, pivot to simplified adaptive rendering

---

## PART I — SIGNAL ANALYSIS

*"The archive contains over 700 entries curated from thousands of items representing
over 20 years of Nokia's design history — both seen and unseen."*
— Nokia Design Archive, About page

You are building a browser for a museum, to be run on hardware that is itself
becoming a museum piece, in order to display a digital archive about hardware that
was discontinued before its time.

### What happened — the SVG→XAML bridge failure

Sessions 5.05–6.12 were spent building and debugging the SVG→XAML rendering bridge:
DOM stubs, D3 initialization, mutation observers, `<innerHTML>` injection, circular graph
layout, timeline compact packing, deduplication guards, CacheMode fixes.

**The bridge never became stable. Key failures:**

1. **Content doubling on scroll/window move** — any scroll or resize triggers a re-render
   that duplicates all XAML circles and lines instead of replacing them. Deduplication
   guards in `VirtualizingRenderer.AppendSvgChild` mitigated but never eliminated it.

2. **Fundamental architectural mismatch** — SVG elements carry `cx`,`cy` as attributes;
   XAML Ellipse uses `Canvas.Left`/`Top` with `Width`/`Height`. Every scroll causes
   `innerHTML` re-injection → whole DOM re-parse → all XAML elements re-created.
   No delta-update possible.

3. **Win SDK 15063 (RS2) limitations** — no `x:Load`, no `x:DeferLoadStrategy`,
   no `CompositionTarget` animation, no `SvgImageSource` without thread marshalling
   hacks. The platform was designed for text-heavy LOB apps, not dynamic SVG graphs.

4. **Performance on Lumia 950 target** — 200+ XAML shapes re-rendered per scroll on
   3 GB Snapdragon 810. Even 200 circles + filtered lines exceed budget for fluid UI.

5. **Timeline rendering also unstable** — disabled after first tests because it shared
   the same fragile SVG→XAML pipeline.

**Honest conclusion: the SVG→XAML bridge is not fixable within the current stack.**
It will never be stable on Lumia hardware with Win SDK 15063. Further attempts are
wasted effort.

### Where does this leave the project?

The core NI L.JS infrastructure is solid:
- ✅ Chunk extraction via C# brace-counting (755 nodes, 1647 links)
- ✅ SystemJS module loader works
- ✅ DOM stubs, CSS, HTML rendering work
- ✅ Deploy script, diagnostics, build pipeline all stable

**The project needs a fundamental rendering strategy change:**

1. **Now (MVP approach):** Abandon complex SVG→XAML. Render only simplified content:
   text blocks, images, link handlers. No graph/timeline SVG injection. Use the
   existing HTML/CSS renderer (DomBasicRenderer) which is already stable.

2. **Smartphone adaptivity:** Detect viewport width < 600px and render a card-based
   layout — one content card at a time, swipeable. No large SVGs at all on small screens.

3. **v1.0+ (future):** Reintroduce complex rendering via Skia or MonoGame — native
   Canvas2D drawing, no SVG→XAML bridge. This is a full rewrite of the rendering
   backend and belongs in the v1 planning phase, not before MVP.

### Data extraction — the one lasting success

The C# brace-counting extraction in `JavaScriptEngine.cs:3487-3562` successfully
extracts all archive data from the 589KB JS chunk:

| Variable | Content | Count |
|----------|---------|-------|
| `Kf` | Entries array | 722 entries |
| `wf` | Keywords array | 91 keywords |
| `Cf` | Stories array | 230 stories |
| `vf` | Collection flags array | 33 collections |
| `__graphData` | Processed nodes + links | 755 nodes, 1647 links |

This data is available on `window` for any JS code to consume — it just won't be
rendered as SVG→XAML anymore.

---

## PART II — NEW MISSION: SIMPLIFIED ADAPTIVE RENDERING

### Phase R — Retrench (current)

> **Priority: 🔴 Critical — replace the unstable rendering pipeline**
> **Effort: 1–2 sessions**
> **Depends on:** Nothing — we keep what works

**R.1 — Remove all SVG→XAML injection code**

Remove or disable:
- The `renderCode` block in `JavaScriptEngine.cs` (~line 3560) that builds SVG XML
  for graph (circular layout) and timeline — clean up, no more `#plot.innerHTML`
  or `#timeline.innerHTML` injections.
- The SVG→XAML element mapping in `VirtualizingRenderer.cs` (circle→Ellipse,
  line→Line, path→Path) — delete or flag as dead code.
- `TriggerDelayedSvgExtractionAsync` and `TriggerDelayedSvgRefresh` — if still
  referenced, remove.
- The deduplication logic in `AppendSvgChild` — no longer needed.
- Node limiting (`_maxNodes`, `_limitedIds`) — debug only, remove.

**R.2 — Verify base HTML/CSS rendering still works**

After removing SVG code, confirm the Nokia Archive home page still renders:
- Header with logo and navigation links
- Search box (text input)
- Category cards (text + images)
- Footer

The existing DomBasicRenderer and VirtualizingRenderer handle these already.
No SVG code should be needed for the basic page.

**R.3 — Data remains accessible for future use**

Keep the data extraction code intact — it populates `window.__graphData` and
related globals. This data can be used later by a Skia/MonoGame renderer or
for server-side export. Mark it as "reserved for future renderer."

### Phase S — Smartphone Adaptivity (card-based layout)

> **Priority: 🔴 Critical — must work on museum Lumia 640/950 with 1–3 GB RAM**
> **Effort: 2–3 sessions**
> **Depends on:** Phase R ✅ (clean slate)

**The core idea:** Instead of rendering a large SVG graph/timeline, present archive
content as a stack of swipeable cards. Each card shows one piece of content (entry,
story, keyword) with its name, description, and image. Navigation by swipe (touch)
or pointer drag (mouse).

**S.1 — Auto-detect smartphone**

```csharp
// In MainPage.xaml.cs or BrowserApi.cs
bool IsNarrowViewport()
{
    var bounds = ApplicationView.GetForCurrentView().VisibleBounds;
    return bounds.Width < 600;
}
```

Also check `Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily`
for `"Windows.Mobile"`.

**S.2 — Card stack layout**

Instead of rendering the full page HTML, intercept page load and build a card stack:

```
┌─────────────────────┐
│ ← Archive            │  ← header with back button
├─────────────────────┤
│                     │
│   [image]           │  ← media
│                     │
│   Entry Name        │  ← title
│   ─────────────     │
│   Description text  │  ← content
│   that can scroll   │
│   within the card   │
│                     │
│   🔗 Related links  │  ← tap handlers
├─────────────────────┤
│  ◀  ●  ●  ●  ▶     │  ← dots + prev/next
└─────────────────────┘
```

**Implementation approach:**

Option A (preferred): **HTML-only cards** — no XAML custom elements.
Use the existing DomBasicRenderer to render each card as an HTML fragment
with `<div>`, `<img>`, `<p>`, `<a>` tags. The renderer already handles these.
Cards are stacked in a ScrollViewer with `SnapPointsType="MandatorySingle"`.

Option B: **XAML native cards** — build a XAML `DataTemplate` with
`Image`, `TextBlock`, `Button` for each card. More work but better performance.

**S.3 — Swipe/click navigation**

The ContentArea already has `ManipulationMode="TranslateX"` (MainPage.xaml:171).
Wire it:

```csharp
// MainPage.xaml.cs
private int _currentCardIndex = 0;
private List<CardData> _cards;

private void ContentArea_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
{
    if (e.Cumulative.Translation.X < -50) // swipe left → next
        NavigateCard(1);
    else if (e.Cumulative.Translation.X > 50) // swipe right → prev
        NavigateCard(-1);
}

private void NavigateCard(int delta)
{
    var newIndex = Math.Clamp(_currentCardIndex + delta, 0, _cards.Count - 1);
    if (newIndex != _currentCardIndex)
    {
        _currentCardIndex = newIndex;
        RenderCurrentCard();
    }
}
```

Also support mouse: `PointerPressed` + `PointerMoved` + `PointerReleased`
for drag detection when no touch screen.

**S.4 — Card data from extracted globals**

Use the data already on `window`:
- `__entries` (722) — each has `id`, `name`, `type`, `description`, `image`
- `__stories` (230) — narrative content with date ranges
- `__keywords` (91) — tag-style entries with links
- `__graphData.nodes` (755) — individual graph nodes with names and types

Each becomes a card. Navigation follows the archive structure:
- Start with a "hub" card showing main categories
- Tap a category → show entries in that category
- Each entry card shows name, image, description, related links

**S.5 — Performance budget on Lumia 640/950**

| Operation | Budget | Notes |
|-----------|--------|-------|
| Card render (HTML) | < 200ms | DomBasicRenderer creates 10–20 LiteElements |
| Card render (XAML) | < 500ms | XAML elements + async image load |
| Swipe response | < 50ms | Pointer/Manipluation event → card change |
| Image load | < 2s | From nokiadesignarchive.aalto.fi over WiFi |
| Memory per card | < 5 MB | One card at a time, destroy previous |

### Phase T — Text-Image-Link Rendering (the default view)

> **Priority: 🟡 High — replaces SVG graph for all viewports**
> **Effort: 1 session**
> **Depends on:** Phase R ✅

Even on desktop (wide viewport), the SVG graph was never stable. Replace the
"Network" view entirely with a clean text/image/link layout:

**T.1 — Archive entry detail view**

When user clicks/taps an entry link, render:
- Entry name (large text, white)
- Entry type badge (collection/story/entry, colored pill)
- Description text (wrapped, readable font size)
- Image(s) if available
- Related entries as clickable links (from graph edges)
- Date range if available

**T.2 — Category index**

Replace the graph visualization with a simple index:
- Alphabetical or category-grouped list of entries
- Each is a text link → opens detail view (T.1)
- Search/filter on top

**T.3 — Link handling**

When a link is tapped:
- If it's an internal archive URL → render as card (Phase S) or detail view (T.1)
- If it's external → open in system browser via `await Launcher.LaunchUriAsync(uri)`
- Graph data edges become "related entries" links

### Phase U — Uplink Interface Cleanup

> **Priority: 🟢 Nice-to-have before MVP**
> **Effort: 1 session**
> **Depends on:** Phases R + S ✅

- Remove dead SVG→XAML code from codebase (or clearly mark as `[OBSOLETE]`)
- Clean up diagnostics: remove `[DIAG:SVG]`, `[DIAG:TIMELINE-SVG]`, `[DIAG:REPAINT]` noise
- Keep `[DIAG:DATA]`, `[DIAG:SYS]`, `[DIAG:ROUTE]` for ongoing debugging
- Remove `_globalSvgRenderCount`, `_svgRenderGeneration`, `_dispatchCount` counters
- Simplify `VirtualizingRenderer` — remove AppendSvgChild, dedup, SVG-specific mapping

---

## PART III — FUTURE: SKIA/MONOGAME RENDERING (v1.0+)

> **Priority: 🔵 Deferred to v1.0 planning**
> **Effort: 4–6 sessions**
> **Depends on:** MVP delivery + successful museum deployment

For v1.0, when complex graph/timeline rendering is needed again:

**Why Skia (recommended):**
- `SkiaSharp` for UWP — NuGet package, well-maintained
- Hardware-accelerated Canvas2D via GPU (Snapdragon Adreno)
- Direct drawing: no SVG→XAML bridge needed
- Touch/gesture support built-in
- Can draw 700+ circles + 1600+ lines at 60fps

**Why NOT SVG→XAML (recap):**
- Architectural impedance mismatch (SVG attributes vs XAML properties)
- Win SDK 15063 lacks modern UI capabilities
- Content duplication on scroll — unfixable
- HTML/CSS stack in DomBasicRenderer is fine for text UI, wrong for dynamic graphics

**Migration path:**
1. Add `SkiaSharp.Views.UWP` NuGet package
2. Replace `#plot` div with `SKXamlCanvas`
3. Port circular layout + line rendering to Skia `SKCanvas.DrawCircle`/`DrawLine`
4. Add touch hit-testing for node interaction
5. Port timeline layout to Skia bars
6. Remove all SVG→XAML dead code

---

## PART IV — HONEST COMPLETION ASSESSMENT (REVISED)

| Goal | Status | Estimate |
|------|--------|----------|
| **Nokia Archive: base page renders** | ✅ Done | Already works |
| **Nokia Archive: text/image/link navigation** | 🎯 Achievable with SVG removal | 1–2 sessions |
| **Smartphone adaptivity (card stack)** | 🎯 Achievable via DomBasicRenderer | 2–3 sessions |
| **Archive data visible to user** | ✅ Done (extracted, on window) | Already works |
| **Complex graph rendering** | ❌ Abandoned (Skia/MonoGame in v1) | v1.0+ |
| **Timeline rendering** | ❌ Abandoned (Skia/MonoGame in v1) | v1.0+ |
| **v0.57 MVP for museum** | 🎯 Achievable | **4–6 sessions** |

### Speed multiplier: removing SVG→XAML = removing the bug factory

Every SVG→XAML bug fix created 2 more bugs. The code paths involved:
- `CustomHtmlEngine.cs` (ScheduleRepaintFromJs, DispatchRepaintAsync)
- `VirtualizingRenderer.cs` (AppendSvgChild, dedup, element mapping)
- `JavaScriptEngine.cs` (renderCode, node limiting, layout)
- `MainPage.xaml` (CacheMode gymnastics)
- `MainPage.xaml.cs` (repaint guards, timing hacks)

**Removing this entire subsystem eliminates the primary source of instability.**
The remaining DomBasicRenderer + VirtualizingRenderer (for text/images/links)
has been stable for months of development.

---

## PART V — REVISED SESSION SEQUENCE

```
Session 6.12b: Phase R — Remove SVG→XAML injection code
                ✅ Strip renderCode from JavaScriptEngine.cs
                ✅ Clean up dead SVG code paths
                ✅ Verify base page still renders
                ⬜

Session 6.13: Phase S — Card stack for smartphone viewport
                ⬜ Detect narrow viewport (<600px)
                ⬜ Build card template in DomBasicRenderer
                ⬜ Wire swipe navigation (ManipulationMode)
                ⬜ Test on Lumia 950 emulator

Session 6.14: Phase T — Text/Image/Link detail views
                ⬜ Entry detail page (from __entries data)
                ⬜ Category index
                ⬜ Related links from graph edges
                ⬜ Internal/external link routing

Session 6.15: Phase U — Cleanup + MVP polish
                ⬜ Remove dead SVG code and diagnostics
                ⬜ Performance tuning on Lumia 640 (1 GB)
                ⬜ Final deploy test
```

---

## PART VI — THE RETRO-FUTURISTIC POSTSCRIPT (REVISED)

```
TRANSMISSION LOG — LUMIA UPLINK NODE  //  2026.06.12  //  UTC+03:00
──────────────────────────────────────────────────────────────────────
The SVG→XAML bridge is dead. Long live the text.

We spent 18 sessions trying to force D3.js force simulations through
a DOM stub that was never designed for it. The circles appeared,
then doubled. The lines rendered, then vanished. Every scroll was a
lottery. Every patch exposed two new cracks.

This is not failure. This is learning the hard way that a museum
browser on a 2015 phone needs to know its limits.

The data is extracted — 755 nodes, 1647 links, 722 entries, 230 stories.
It sits in memory, waiting for a renderer that can do it justice.
That renderer will come — SkiaSharp, hardware-accelerated, on a
clean Canvas2D surface. But not today.

Today, we make the archive readable. Text blocks. Images. Links.
A card stack that a museum visitor can swipe through on a Lumia 640
with one thumb. No circles. No force simulation. No SVG→XAML.

The uplink continues. Just slower, lower, and more honest.

STATUS: SVG BRIDGE ABANDONED // PIVOT TO TEXT/IMAGE/LINK RENDERER
NEXT: Phase R — Strip SVG code → verify base page → card stack
ETA: 1 SESSION TO CLEAN SLATE
──────────────────────────────────────────────────────────────────────
```

---

## Build

```
"C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe" Src\MediaExplorer.sln /p:Configuration=Debug /p:Platform=x86 
```

## Deployment  

```
powershell -ExecutionPolicy Bypass -File "Src\MediaExplorer\DeployAndRun.ps1" -Platform x64
```

---

*Plan v5.7 — 2026-06-12 — SVG→XAML BRIDGE ABANDONED*
*Based on: Sessions 5.05–6.12, Summaries 5.05–6.12*
*Build target: VS 2026 Insiders MSBuild. Platform: x64 (desktop) + ARM (Lumia 950)*
*Next session: 6.12b — Phase R: Remove SVG→XAML, stabilize base page*
