# WEBVIEW — Plan for Modernization

> Retro alternative browser for Windows 10 Mobile (Lumia 950/1020)
> Target: mid-2010s websites (Google, DuckDuckGo, flexbox/grid, forms)
> Hardware: Snapdragon 810, 3GB RAM, 5" 1440p display

---

## Phase 0: Infrastructure & Code Cleanup

**Goal:** Remove dead code, modularize monolithic files, prepare for deeper changes.

| Task | File(s) | Why |
|------|---------|-----|
| Remove dead commented-out blocks | `JavaScriptEngine.cs` (~150 lines) | Dead DISABLED code pollutes ~7570-line file |
| Split `DomBasicRenderer.cs` (5545 lines) | Into `TableRenderer.cs`, `FormRenderer.cs`, `SvgRenderer.cs` | Modularity, parallel work |
| Remove `SimpleWrapPanel.cs` from build | `WEBVIEW.csproj` | File excluded but still on disk; delete it |
| Clean up `#if USE_NILJS` → runtime flag | `JavaScriptEngine.cs`, `CustomHtmlEngine.cs`, `WEBVIEW.csproj` | Avoid `#define` branching; choose engine at runtime |
| Remove `Compat/HttpCache.cs` stub | `HttpCache.cs`, `ResourceManager.cs` | Stub always returns null; just delete it and inline |

**Estimated effort:** 1-2 days / ~800 lines changed

---

## Phase 1: Bottom Collapsible AppBar

**Goal:** Maximize content area on small screen. Replace top toolbar with a bottom strip that collapses to 4px and swipes up to reveal URL bar + minimal controls.

### Design
```
┌─────────────────────────────────┐
│        Content Area             │
│        (full viewport)          │
│                                 │
│                                 │
│                                 │
│                                 │
│  ┌──────────────────────────┐   │
│  │ ← │  🔍 [url]      ⚙  │   │  ← Collapsed (4px tap zone)
│  └──────────────────────────┘   │
└─────────────────────────────────┘

  On swipe up / tap:
  → Height animates to 48px
  → Shows: Back, URL textbox, Go, Settings
```

### Controls (expanded)
| Control | Size | Action |
|---------|------|--------|
| Back button | 24×24px | Navigate back |
| Forward button | 24×24px | Navigate forward |
| URL TextBox | Remaining width | Enter URL or search query |
| Go button | 36px height | Navigate to URL |
| Settings button | 36px height | Opens Settings flyout |
| About button | Via Settings | App info, credits |
| Reload | Double-tap on URL | Refresh current page |

### Files
- `MainPage.xaml` — Complete layout rewrite (Grid rows: \* + Auto)
- `MainPage.xaml.cs` — Gesture handlers, animation logic
- `AppBarControl.xaml` / `.cs` — Optional: extract into custom UserControl

### Behaviors
- **Swipe up** on bottom strip → expands with spring animation
- **Tap** collapsed strip → expands
- **Tap outside** expanded bar or **swipe down** → collapses
- **Auto-collapse** after navigation (configurable)
- URL displays hostname when collapsed, full URL when expanded

**Estimated effort:** 2-3 days / ~700 lines

---

## Phase 2: CSS Cascade Optimization

**Goal:** Fix the #1 performance bottleneck — O(nodes × rules) cascade matching.

### Current problem
`CascadeIntoComputedStyles` iterates every DOM node against every selector. For 500 nodes × 2000 rules = 1M comparisons, each with DOM traversal.

### Solution: Selector Index

```csharp
// Build index once
var index = new Dictionary<string, List<CssRule>>();
foreach (var rule in rules)
    foreach (var sel in rule.Selectors)
        index.Add(sel.RightmostKey(), rule);

// Match using index
foreach (var node in allNodes)
    foreach (var key in node.Keys())  // {"div", ".foo", "#bar"}
        foreach (var rule in index[key])
            if (rule.Matches(node)) ...  // only for candidate rules
```

### Additional optimizations

| Optimization | File | Speedup |
|-------------|------|---------|
| Cache sibling index on `LiteElement` | `CssLoader.cs` | Avoid O(n) sibling scans for `:nth-child` |
| `StripComments` without Regex | `CssLoader.cs` | char-by-char StringBuilder |
| Inline style parsing without `Split` | `CssLoader.cs` | Reduce string allocations |
| `RegexOptions.Compiled` on hot patterns | `CssLoader.cs` | 2-5x faster regex |
| Sort by specificity inline instead of LINQ | `CssLoader.cs` | No delegate allocations |

### Target: 10-50x faster cascade on complex pages

**Estimated effort:** 3-4 days / ~600 lines

---

## Phase 3: Layout Offload + Virtualization

**Goal:** Stop freezing the UI thread during layout. Add element recycling to VirtualizingRenderer.

### Tasks

| Task | File | Impact |
|------|------|--------|
| Offload `RenderObject.Layout()` to background thread | `LayoutEngine.cs`, `RenderBox.cs` | UI stays responsive (currently freezes 500-2000ms) |
| Element recycling in VirtualizingRenderer | `VirtualizingRenderer.cs` | Replace `_canvas.Children.Clear()` + rebuild with reuse + reposition |
| Lazy image loading (viewport only + 200px buffer) | `VirtualizingRenderer.cs`, `DomBasicRenderer.cs` | Faster initial paint, less memory |
| Remove `ApplyReadableForeground` tree walk | `CustomHtmlEngine.cs:1287` | One less O(n) walk per render |
| Debounce SizeChanged re-render | `MainPage.xaml.cs:514` | Avoid full NavigateAsync on every resize |

### VirtualizingRenderer recycling strategy
- Track `_visibleElements: Dictionary<RenderObject, UIElement>` (currently alive)
- On scroll: compare new visible set → remove gone nodes, add new ones, reposition existing
- Pool `Border`, `TextBlock`, `Image` objects

**Estimated effort:** 5-7 days / ~1000 lines

---

## Phase 4: JavaScript Engine Improvements

**Goal:** Make NiL.JS usable (DOM sync), batch microtasks, fix MiniRunner scoping.

### Tasks

| Task | File(s) | Why |
|------|---------|-----|
| Implement `_nilSyncDocument()` | `JavaScriptEngine.cs` | NiL.JS sees a stale/empty DOM — needs full document tree export |
| Unblock NiL.JS `fetch` | `JavaScriptEngine.cs:1964` | `GetAwaiter().GetResult()` blocks the engine — use async Promise |
| Batch microtask processing | `JavaScriptEngine.cs` | Each microtask = `DispatchToUi` overhead. Batch ~16 tasks per dispatch |
| MiniRunner: fix lexical scoping | `JsMiniRunner` (inside JS.cs) | Currently all vars in flat `_globals` dict; need scope chain for `{ let x }` |
| MiniRunner: reparse on every call | `JsMiniRunner.ExecuteString` | Each timer/event re-parses source. Cache compiled `JsFuncDef` |
| Replace 40× Regex in RunInline with Trie | `RunInline` method | Single-pass dispatch instead of 40 regex scans per line |
| Non-empty catch blocks | All files | Silent swallowing makes debugging impossible. At minimum: `Debug.WriteLine` |

### NiL.JS DOM sync strategy
- On `_nilSyncDocument()`, build a JS object tree mirroring LiteDom:
  ```js
  window.__dom = {
    tag: "html",
    children: [
      { tag: "head", children: [...] },
      { tag: "body", children: [...] }
    ]
  };
  ```
- Expose mutation callbacks: when JS modifies DOM → sync back to LiteDom
- For Phase 4: implement **read-only** sync first (JS can read `.innerHTML`, `.textContent`)
- Phase 4+: implement **write-back** (JS can modify DOM → re-render)

**Estimated effort:** 5-7 days / ~1500 lines

---

## Phase 5: Resource Loading

**Goal:** Disk cache for images, request prioritization, remove expensive reflection.

### Tasks

| Task | File | Impact |
|------|------|--------|
| Add disk cache for images | `ResourceManager.cs` | Images survive navigation without re-fetch |
| Request priority queue | `ResourceManager.cs` | CSS/fonts load before images |
| Reduce LRU caps for 3GB RAM | `ResourceManager.cs` | 32 text + 16 images (was 128+64) |
| Cache SkiaSharp MethodInfo | `ResourceManager.cs` | One reflection call per app run, not per image |
| Remove HttpCache stub references | `ResourceManager.cs`, `JavaScriptEngine.cs` | Clean dead code path |
| Shared InMemoryRandomAccessStream | `ResourceManager.cs` | Don't copy IBuffer → IRandomAccessStream |
| Configurable disk TTL | `ResourceManager.cs` | Instead of hardcoded 5 min |

**Estimated effort:** 2-3 days / ~500 lines

---

## Summary

| Phase | Lines changed | Effort | Priority |
|-------|--------------|--------|----------|
| 0: Infrastructure | ~800 | 1-2 days | 🔴 Must have (blocker) |
| 1: Bottom AppBar | ~700 | 2-3 days | 🔴 Must have (UX) |
| 2: CSS Cascade Index | ~600 | 3-4 days | 🔴 Must have (performance) |
| 3: Layout + Virtualization | ~1000 | 5-7 days | 🟡 Medium |
| 4: JavaScript Engine | ~1500 | 5-7 days | 🟡 Medium |
| 5: Resource Loading | ~500 | 2-3 days | 🟢 Nice to have |

**Total:** ~5100 lines, ~20 working days

---

## Architecture Decisions

### Threading model (after Phase 3)
```
UI Thread:      CSS cascade → Paint → XAML controls
Background:     HTML parse → RenderObject layout → JavaScript execution
```

### Tag dispatch (after Phase 0)
Replace `_tagHandlers` dictionary (string → lambda) with `enum HtmlTag` + `switch`:
```csharp
enum HtmlTag {
    Div, Span, A, Img, Input, Table, Form, ...
    // ~100 common tags + `Unknown`
}
static readonly Dictionary<string, HtmlTag> _tagMap;
```

### Memory budget (Lumia 950 — 3GB)
- CSS rules cache: max 5000 rules
- Image LRU: 16 entries (≈64MB for 4MB GIFs)
- Text LRU: 32 entries (≈10MB for HTML/CSS)
- DOM node limit: 10000 elements (beyond → degrade gracefully)

---

*Plan v1.0 — 2026-05-16*
