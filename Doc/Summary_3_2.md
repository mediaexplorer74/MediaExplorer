# Summary 3.2 — Phase 8B: Incremental Re-render

**Session date:** 2026-05-18  
**Build:** 0.8.0.0 x64 Debug — ✅ 0 errors

---

## 1. Phase 8B — Incremental Re-render (CascadeSingle + PatchAsync)

### Problem
До этого каждая DOM-мутация (через MutationObserver) вызывала полный `RenderAsync()` — перепарсинг CSS, полный layout, полная перестройка XAML-дерева. Для SPA это означало фликер и огромные затраты CPU.

### Solution: 3-Phase Incremental Pipeline

#### Phase 1: DOM Mutations
Применение мутаций к RenderObject tree:
- `childList` → `AddChild` / `RemoveChild` на уровне RenderObject
- `attributes` → mark dirty для последующего re-cascade

#### Phase 2: Re-Cascade + Layout
- **`CssLoader.CascadeSingle()`** — пересчитывает стили только для затронутого subtree, используя кэшированные CSS-правила из последнего `ComputeAsync`
- **`LayoutEngine.PerformIncrementalLayout()`** — обрабатывает только dirty ноды (уже был, теперь используется)
- **`LayoutEngine.InvalidateSubtree()`** — новый метод для mark dirty всего subtree

#### Phase 3: Patch Renderer
- **`VirtualizingRenderer.PatchAdded()`** — добавляет только новые visible ноды на canvas
- **`VirtualizingRenderer.PatchStyle()`** — обновляет стиль существующего UIElement in-place
- **`VirtualizingRenderer.ApplyStyleToVisual()`** — обновляет свойства TextBlock/Border/Button/TextBox без пересоздания
- **`VirtualizingRenderer.GetViewportRect()`** — helper для определения видимой области

### Architecture

```
MutationObserver fires
    ↓
DrainRendererMutations() → InternalMutationRecord[]
    ↓
ApplyIncrementalUpdateAsync()
    ├── Phase 1: Apply DOM mutations to RenderObject tree
    │       ├── childList: AddChild / RemoveChild
    │       ├── attributes: MarkDirty
    │       └── Re-cascade: CssLoader.CascadeSingle() for affected nodes
    │
    ├── Phase 2: Incremental layout
    │       └── LayoutEngine.PerformIncrementalLayout(root, viewportSize)
    │
    └── Phase 3: Patch renderer
            ├── PatchAdded(subtreeRoot) — new visible nodes → canvas
            ├── PatchStyle(node) — in-place style update
            └── Fallback: UpdateView() if no specific patches applied
```

### Files affected

| File | Change |
|------|--------|
| `Engine/CssLoader.cs` | `CascadeSingle()` — incremental re-cascade для subtree<br>`_cachedRules` — кэш распарсенных CSS-правил<br>`TryInt()`, `TryCornerRadius()` — новые helpers<br>Accessibility fix: `internal` для вложенных классов |
| `Engine/Core/LayoutEngine.cs` | `InvalidateSubtree()` — mark subtree dirty<br>`RelayoutSubtreeAsync()` — async wrapper для targeted relayout<br>`MarkSubtreeDirty()` — recursive dirty propagation |
| `Engine/Core/VirtualizingRenderer.cs` | `PatchAdded()` — incremental add to canvas<br>`PatchStyle()` — in-place style update<br>`ApplyStyleToVisual()` — update TextBlock/Border/Button/TextBox props<br>`GetViewportRect()` — viewport calculation helper |
| `Engine/CustomHtmlEngine.cs` | `ApplyIncrementalUpdateAsync()` — rewritten as 3-phase pipeline with re-cascade |

### Key design decisions

1. **Cached CSS rules** — `_cachedRules` хранит распарсенные правила из последнего `ComputeAsync`, чтобы `CascadeSingle` не парсил CSS заново
2. **No full rebuild** — если mutations обработаны через patch, `UpdateView()` не вызывается
3. **Fallback safety** — если patch не применился, fallback на полный `UpdateView()`
4. **Thread safety** — `_cachedRules` защищён `lock` для concurrent access

---

## Build Result

```
MediaExplorer -> bin\x64\Debug\MediaExplorer.exe
```

**0 errors**, 1 pre-existing warning (`_errorOverlayVisible` unused).

---

## Plan 03 Status Update

| Phase | Title | Status |
|-------|-------|--------|
| 0 | Infrastructure & Code Cleanup | ✅ DONE |
| 1 | Bottom Collapsible AppBar | ✅ DONE |
| 2 | CSS Cascade Optimization | ✅ DONE |
| 3 | Layout Offload + VirtualizingRenderer | ✅ DONE |
| 4 | JavaScript Engine Improvements | ✅ DONE |
| 5 | Resource Loading | ✅ DONE |
| 6 | ES Modules | ✅ DONE |
| 7 | Service Worker + Offline-First | ⏸ DEFERRED |
| 8A | MutationObserver API | ✅ DONE |
| **8B** | **Incremental Re-render** | **✅ DONE** |
| 9/14/15 | CSS Property Expansion | 🟡 IN PROGRESS |
| 10 | DevTools Console + Inspector | 🔴 PENDING |
| 11 | UI Polish | ✅ DONE |
| 12 | AI Integration | ✅ DONE |
| 13 | White Screen Fixes, Settings | ✅ DONE |
| 16/17/18 | Image Loading + Diagnostics | ✅ DONE |
| **T** | **Testing Infrastructure** | **✅ DONE** (T.1-T.5) |

---

## Next Steps (recommended order)

| Priority | Task | Effort | Rationale |
|----------|------|--------|-----------|
| 🔴 | **Phase 15** — Rendering Modes (FULL/RICH/POOR) + Magic Bubble stub | 2-3 дня | UX differentiator, emergency exit для тяжёлых сайтов, e-book mode |
| 🟡 | **Phase 16.2** — calc() + vw/vh resolver | 1-2 дня | Многие сайты используют `calc(100% - Xpx)` |
| 🟡 | **Phase 16.3** — grid-template-areas | 2-3 дня | Modern layouts rely on 2D grid |
| 🟡 | **Phase 10** — DevTools Console | 3-5 дней | Dramatically improves debuggability |
| 🟢 | **Phase 17** — Robustness & Memory | 2-3 дня | Crash hardening, JS timeout, DOM limit |

### Phase 15 — Detailed scope (updated)

**Technical:**
- `RenderMode` enum (FULL/RICH/POOR) в `CustomHtmlEngine`
- POOR: skip JS, skip images, minimal inline reader stylesheet
- RICH: skip NiL.JS modules, MiniRunner only (timeouts/analytics-kill)
- FULL: current behavior

**UX — Magic Bubble (shared component):**
- Trigger: long-tap / long mouse press (>500ms) on content area
- RICH mode: popup near tapped element → "[AI: explaining this element...]"
- POOR mode: popup → "[AI: summarizing this page...]"
- Phase 15 scope: UI stub only (placeholder text, no AI call)
- Future: wire to Phase 12 AI integration (DeepSeek/OpenRouter)

---

*Session 3.2 — 2026-05-18*  
*Based on: Plan_03.md, sessions 2.21-2.22 (as planned)*
