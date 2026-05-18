# Summary_03 — Phase 3 (Layout Offload + Virtualization + QoL)

## Goal
Stop freezing the UI thread during layout, add element recycling to VirtualizingRenderer, reduce unnecessary re-renders, and polish UX for W10M.

## Changes

### Files modified
- `Engine\Core\VirtualizingRenderer.cs` — full rewrite with element recycling + lazy images
- `Engine\CustomHtmlEngine.cs` — split Build→Layout→Paint pipeline across threads
- `Engine\ResourceManager.cs` — UA unified, removed mobile fallback
- `Engine\BrowserApi.cs` — UA unified, removed `ApplyKnownBasicRewrite`
- `Engine\JavaScriptEngine.cs` — UA unified (3 sites)
- `MainPage.xaml` — removed all `CornerRadius` (5), GPU toggle → HomePage input
- `MainPage.xaml.cs` — debounced SizeChanged, home page persistence, start on saved HP

---

### 1. Layout offloaded to background thread
**Before:** `Layout` on UI thread → freeze 500–2000ms  
**After:** `Layout` in `Task.Run()` (pure math, no XAML)

```
UI thread:   Build RenderObject
Background:  LayoutEngine.PerformLayout
UI thread:   VirtualizingRenderer.Paint
```

### 2. Element recycling in VirtualizingRenderer
**Before:** `_canvas.Children.Clear()` + full rebuild on every scroll  
**After:** Diff-based visible set + typed element pools

| Pool key | Type | Reset on return |
|----------|------|-----------------|
| `IMG` | `Grid` | `Children.Clear()`, `Background = null` |
| `TextBlock` | `TextBlock` | `Text = ""`, `Inlines.Clear()` |
| `Border` | `Border` | `Child = null`, brushes/thickness cleared |
| `TextBox` | `TextBox` | `Text = ""` |
| `Button` | `Button` | `Content = null` |
| `Image` | `Image` | `Source = null` (pooled via Grid children) |

### 3. Lazy image loading (new)
- `CreateImageVisual` no longer sets `Image.Source` — only registers in `_lazyImages[node] = img`
- `ProcessLazyImages()` called after each `UpdateView()` diff:
  - Visible + Source null → resolve URI / data-URI and set `Source`
  - Scrolled away + Source set → `Source = null` (free memory)
- Removal loop cancels pending loads before element is pooled
- Grid children leak fixed: `ReturnToPool` now calls `Grid.Children.Clear()`

### 4. Debounced SizeChanged
- 300ms debounce before `NavigateAsync`
- Quick successive resizes (rotation animation) collapse into one final render

### 5. CornerRadius removed from all XAML (5 places)
`CornerRadius` on W10M causes silent crash. Removed from:
- BarButtonStyle Border (`CornerRadius="4"`)
- BarOmniboxStyle Border (`CornerRadius="16"`)
- Drag handle Border (`CornerRadius="2"`)
- Settings overlay Border (`CornerRadius="8"`)
- About overlay Border (`CornerRadius="8"`)

### 6. User-Agent unified → Chrome 131 Desktop
All 8 UA strings replaced with:
```
Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36
```
Eliminates mobile/low-end triggers (`Android 10; K`, `WP 8.1 IE`, `Pixel 5`) that caused Google to redirect to `/doodles`.

### 7. Removed `ApplyKnownBasicRewrite` (Google `?gbv=1`)
Was adding `gbv=1` to all `*.google.com` URLs — triggered Google's basic HTML mode which sends 302 → `/doodles`.

### 8. Settings: GPU toggle → HomePage input
- Removed `GpuToggle` (enable/disable GPU Accel was unused, except setting `BitmapCache`)
- Added `HomePageBox` TextBox + `Save` button
- HomePage persisted in `ApplicationData.LocalSettings["HomePage"]`
- On startup: if HomePage is set → navigate there; otherwise → show Welcome page

### 9. Pipeline architecture (current)
```
CustomHtmlEngine.RenderAsync:
  CSS Compute (any thread)
  → Build RenderObject (UI dispatcher)
  → Layout (Task.Run — background)
  → VirtualizingRenderer.Paint (UI dispatcher)
    → On scroll: diff visible set, recycle elements, lazy-load images
```

### Remaining from Phase 3 plan
- `ApplyReadableForeground` tree walk still present in fallback renderer path — serves readability on dark error pages, negligible overhead since it only runs on fallback, not on VirtualizingRenderer path.

### Build result
- **0 errors** (when built in VS with UWP workload)
- **~650 lines changed** across 7 files
