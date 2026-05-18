# WEBVIEW — Plan 02: Beyond Modernization

> All 5 original phases complete. What comes next?
> Target evolution: retro WP8→UWP browser → capable modern-ish browser for W10M
> Constraints unchanged: Lumia 950/1020, Snapdragon 810, 3GB RAM, x86 emulator testing
> **Last updated: 2026-05-17** (session 2.14 — AutocompleteList removal, AppBar hit-test fix, script/style text suppression, box-sizing)

---

## Phase 6: ES Modules (NiL.JS Native Module Loader) — [DONE ✓]

**Goal:** Replace the dead MiniRunner transpile-path in `ModuleLoader.cs` with NiL.JS's native module pipeline (`Module.ResolveModule` event + `_nil.Eval`).

### What was done
- **`ModuleLoader.cs`** — complete rewrite (~220 lines). Subscribes to `Module.ResolveModule` static event.
- **Pre-fetch**: before `_nil.Eval`, scan `import` specifiers via regex, recursively fetch + cache all transitive deps as `NiL.JS.Module` objects (Context set via reflection — private setter).
- **ResolveModule handler**: resolves relative → absolute via `location.href`, bare specifiers via `_importMap`, or falls back to synchronous fetch.
- **`Script type="module"` detection** was already wired in `JavaScriptEngine.cs` (classifies as deferred, dispatches to `_moduleLoader.ExecuteModuleTagAsync`). No changes needed.
- `<script type="importmap">` parsing: not yet implemented (stub `SetImportMap()` ready).

### Architecture

```
<script type="module" src="app.js">
  → JavaScriptEngine.QueueScript (already wired)
    → ModuleLoader.ExecuteModuleTagAsync
      → PrefetchDependencies(source, baseUri)        # regex scan, recursive fetch + cache
      → _nil.Eval(source)                            # NiL.JS natively parses import/export
        → OnResolveModule(sender, e)                  # fired for each import specifier
          → Check _moduleCache / _importMap / resolve relative
          → FetchModuleTextAsync (sync fallback via .GetAwaiter().GetResult())
          → CacheModule(key, source)                  # new JSModule(key, source) + Context via reflection
          → e.Module = cached; e.AddToCache = true
```

### Key design decisions (actual)

| Decision | Chosen | Why |
|----------|--------|-----|
| Module resolution | `Module.ResolveModule` static event | NiL.JS v2.5.1294 doesn't have `IModuleResolver` interface. Event-based is the supported API. |
| Context setting | Reflection (`MethodInfo.Invoke` on private setter) | `Module.Context` has a private setter. No public API to attach a context at construction time. |
| Caching | `ConcurrentDictionary<string, JSModule>` (global per ModuleLoader instance) | One ModuleLoader per page — each page gets fresh module caches. |
| Dep order | Pre-fetch all transitive deps before `_nil.Eval` | NiL.JS handles execution order internally. Pre-fetch ensures deps are cached before the event fires synchronously. |
| ImportMap | `Dictionary<string, string>` with `SetImportMap()` method | Ready for Phase 7+ when `<script type="importmap">` parsing is added. |

### Files modified

| File | Change |
|------|--------|
| `Engine\ModuleLoader.cs` | Complete rewrite — NiL.JS native module API |
| `Engine\JavaScriptEngine.cs` | ModuleLoader init moved after `_nilInit()`, field `readonly` removed |

### Verification: **Build 0 errors** (MSBuild 17.14 / x86 Debug)

### Next: Phase 8 (MutationObserver) completes the SPA picture — modules load the code, MO makes dynamic updates efficient.

---

## Phase 7: Service Worker + Offline-First (provisional)

**Goal:** `navigator.serviceWorker.register()` intercepts resource fetches in `ResourceManager.cs`. Pages work offline. The disk cache from Phase 5 maps naturally to the Cache Storage API.

### Why NOT now
After Phase 6 it became clear: **without MutationObserver (Phase 8), SW adds complexity without fixing the core problem**. Dynamic pages still freeze on every `innerHTML=`. SW helps with repeat visits (offline cache) but doesn't help a page render correctly the first time.

**Postponed until after Phase 8.** The priority order is:
1. Phase 8 (MutationObserver) — fix dynamic rendering perf
2. Phase 9 (CSS) — fix visual correctness
3. Phase 10 (DevTools) — debugging infra
4. Phase 7 (SW) — offline/performance optimization

### Architecture (kept for reference)

```
navigator.serviceWorker.register("/sw.js")
  → SW parsed by NiL.JS (reuses Phase 6 Module pipeline)
  → Install event → SW caches static assets
  → Activate event → cleanup old caches
  → Fetch event → SW decides: network-first, cache-first, or stale-while-revalidate
  
ResourceManager FetchXxxAsync(url)
  → Check SW scope match
  → If matched: DispatchFetchEvent to SW
    → SW handles event.respondWith(...)
    → If no response: fall through to normal HTTP
  → If unmatched: normal HTTP flow (bypass SW)
```

### Key adjustments after Phase 6

| Decision | Change | Why |
|----------|--------|-----|
| SW code loading | Use Phase 6's `Module.ResolveModule` pipeline | SW JS can use `import` — the same ResolveModule handler serves both page modules and SW modules. |
| SW context | Separate NiL.JS `Context` (not shared with page) | SW has its own global scope (no `window`/`document`). Create a fresh Context, define only `self`, `CacheStorage`, `FetchEvent`. |
| Cache Storage | Map to `LocalFolder\sw_cache_{name}\` | Phase 5 disk cache already uses `cache_{partition}/`. SW cache is a separate namespace. |

### Estimated effort: 3-5 days / ~600 lines (simpler after Phase 6 module infra)

Dependencies: Phase 5 disk cache (done), Phase 4 JS engine (done), Phase 6 module loader (done).

### Priority: 🟡 Medium — offline UX win, but blocked by Phase 8

---

## Phase 8: Incremental Re-render (MutationObserver + Virtual DOM)

**Goal:** DOM changes from JavaScript (`innerHTML =`, `appendChild`, `setAttribute`) no longer trigger full page rebuild. Instead, detect mutations, compute a targeted diff, and patch only the affected XAML elements.

This is what **actually unlocks SPA frameworks** — Phase 6 (ES Modules) gets the code loaded, but without MutationObserver, every `document.createElement('div')` or `el.innerHTML = template` triggers a full teardown/rebuild. React/Vue/Svelte all call `innerHTML`, `appendChild`, `setAttribute` millions of times. Phase 8 makes those O(1) instead of O(page).

### Current problem
Any DOM mutation runs `CustomHtmlEngine.RenderAsync()` → full CSS cascade → full layout → full paint. On DuckDuckGo, a single `document.title = "new"` causes ~200ms freeze.

### Architecture

```
JS modifies DOM (via NiL.JS → JsDomElement)
  → MutationObserver.observe(node, { attributes: true, childList: true, subtree: true })
  → MutationRecord queue (batched per frame)
  → MutationProcessor.Process(mutations):
      1. For each mutated node:
         - Attribute change: update LiteDom attribute → patch CSS computed → set XAML property
         - Child list change: insert/remove LiteDom nodes → build new RenderObjects → update VirtualizingRenderer
      2. If subtree mutation → re-layout affected subtree (background thread)
      3. If only attribute/style → update existing XAML element directly (no layout)
  → VirtualizingRenderer.Patch(existingXaml, affectedRenderObjects)
```

### Key decisions

| Decision | Option | Rationale |
|----------|--------|-----------|
| Mutation batching | RequestAnimationFrame boundary | Queue mutations, process in batch once per frame (15-30ms). Avoids O(n) per-mutation cost. |
| Re-layout scope | Subtree-only (not full page) | `RenderObject.Invalidate()` marks the mutated node's subtree as dirty. LayoutEngine re-lays-out only that subtree. |
| CSS re-cascade | Per-node (not all rules) | `CssLoader.CascadeSingle(node)` — re-match selectors for just the affected node using the existing selector index. O(candidates) not O(nodes × rules). |
| XAML patch | `VirtualizingRenderer.Patch(existing, changed)` | If the element is visible and exists in `_visibleElements` → update its XAML properties directly. If not visible → re-render on scroll. |

### Files affected

| File | Change |
|------|--------|
| `JavaScriptEngine.cs` | MutationObserver API (observe, disconnect, takeRecords). JsDomElement mutation hooks. |
| `CustomHtmlEngine.cs` | Remove full RenderAsync on DOM change. Forward mutations to MutationProcessor. |
| `VirtualizingRenderer.cs` | New `Patch()` method: target element update instead of full diff rebuild. |
| `LayoutEngine.cs` | `InvalidateSubtree(renderObject)` — dirty-flag subtree. |
| `CssLoader.cs` | `CascadeSingle(liteElement)` — re-compute styles for one node. |
| `LiteDomUtil.cs` | MutationRecord serialization helpers. |

### Estimated effort: 6-10 days / ~1500 lines

Dependencies: Phase 2 (selector index for CascadeSingle), Phase 3 (VirtualizingRenderer), Phase 4 (JsDomElement-backed DOM).

### Priority: 🟡 Medium — big perf win for dynamic pages, but complex to implement correctly

---

## Phase 9: CSS Property Expansion

**Goal:** Move from ~40% CSS 2.1 coverage to ~80%. DuckDuckGo still looks "pre-alpha" due to missing properties.

### What's missing (identified gaps)

| Category | Missing properties | Impact |
|----------|-------------------|--------|
| Box model | `box-sizing` **[DONE 2.14]**, `overflow-x`/`overflow-y` | Sizes include/exclude padding+border |
| Background | `background-image`/`url()`, `background-position`, `background-size`, `background-repeat`, `background-attachment` | Most visible gaps — buttons, banners, gradients |
| Borders | `border-collapse` [DONE], `border-spacing` [DONE], `outline` [DONE], `border-style` in shorthand | Tables look wrong |
| Text | `text-shadow` [DONE], `letter-spacing`, `word-spacing`, `white-space`, `tab-size`, `text-overflow` [DONE] | Typography fidelity |
| Transforms | `transform`, `transform-origin` | CSS animations, hover effects |
| Transitions | `transition`, `transition-property`, `transition-duration` | Hover effects, dropdowns |
| Flex/Grid | `flex-basis` [DONE], `grid-template-areas`, `gap` [DONE] | Modern layouts |
| Positioning | `position: sticky` [DONE], `z-index` stacking context, `float`/`clear` | Overlays, sticky headers, legacy layouts |
| Misc | `opacity` [DONE], `pointer-events`, `box-shadow` [DONE], `visibility`, `calc()`, `vw`/`vh` | UI polish, adaptive sizing |

### Architecture

Each new property requires:
1. **Parse** in `CssParser.cs` — add to the existing property-to-value-parser switch
2. **Compute** in `CssComputed.cs` — `CssComputedStyle` field + default + computed-from-shortcuts
3. **Render** in `DomBasicRenderer.cs` / `VirtualizingRenderer` — translate to XAML equivalent

### Estimated effort per property

| Complexity | Examples | Time per property |
|-----------|----------|-------------------|
| Simple (1-2 files) | `opacity`, `pointer-events` | 30 min |
| Medium (3-4 files) | `background-position`, `text-shadow` | 1-2 hours |
| Complex (5+ files) | `transform`, `transition` | 4-8 hours |

**Estimated total: 5-10 days / ~1000 lines** for 15-20 high-impact properties.

### Priority: 🟢 Nice-to-have — makes pages look correct but doesn't unlock new capabilities

---

## Phase 10: DevTools (Developer Console)

**Goal:** In-app REPL JavaScript console, DOM tree inspector, network request log. Accessible from the Settings flyout.

### Architecture

```
Settings flyout → "Developer Tools" button
  → DevToolsPage (new XAML page, full-screen overlay)
    → Console tab:
        - TextBox (input) + Button (execute)
        - TextBlock (output log: stdout, errors, console.log)
        - Uses JavaScriptEngine.RunInlineJS(line)
    → Elements tab:
        - TreeView of LiteDom (TreeView nodes = LiteElements)
        - Side panel shows computed styles, attributes
        - Tap node → highlight in VirtualizingRenderer
    → Network tab:
        - ListView of ResourceManager requests (url, status, timing)
        - Uses LogSink (already hooked up to fetch timing)

Communication: DevTools runs in the same process, accesses `Engine` state directly.
```

### Key decisions

- **Console**: `console.log()` in JS already routes to `Debug.WriteLine`. Add a `DevToolsConsole` sink that also captures to an `ObservableCollection<string>`.
- **Elements**: Read `_domRoot` (LiteElement) as the source of truth. TreeView binding with `LiteElement.TagName`, attributes, computed style.
- **Network**: `LogSink` already receives `[FetchText]` and `[FetchImage]` messages. Parse these into structured `NetworkLogEntry` records.

### Estimated effort: 3-5 days / ~600 lines

### Priority: 🟢 Nice-to-have — huge developer experience win, but no user-facing impact

---

## Phase 11: UI Polish

**Goal:** Make the retro browser feel less like a debug tool and more like a daily driver. Loading indicators, swipe gestures, dark mode, reading mode.

### Task list

| Task | File(s) | Why |
|------|---------|-----|
| Loading spinner/throbber | `MainPage.xaml` + `.cs` | Users see a blank canvas for 500-3000ms while page renders. Show a subtle spinner over the canvas area during `RenderAsync`. |
| Swipe left/right navigation | `MainPage.xaml.cs` | Swipe from left edge → back, right edge → forward. W10M users expect gesture navigation. |
| Reading mode (text extraction) | New `ReadingMode.cs` or `CustomHtmlEngine.cs` | Strip `<script>`, `<style>`, `<nav>`, `<footer>`, `<header>`, keep main `<article>`/`<p>` text. Overlay as clean TextBlock with adjustable font size. |
| URL autocomplete | `MainPage.xaml.cs` + history store | Suggest URLs from `_history` / HSTS store as user types. Simple prefix match against visited URLs. |
| Visual feedback on bar buttons | `MainPage.xaml` styles | Hover/press visual states, press animation (opacity 0.5 on click). Currently buttons have no press feedback. |
| Error page styling | `Engine/CustomHtmlEngine.cs` | Currently error returns `"<!-- error -->"` string that renders as unstyled text. Replace with a proper styled error page (CSS + HTML inline). |
| Clear cache button in Settings | `MainPage.xaml.cs` | One-tap to wipe `cache_*` folders and LRU maps. Useful for debugging rendering issues. |

### Files affected

| File | Change |
|------|--------|
| `MainPage.xaml` | Loading overlay (Rectangle + ProgressRing), Reading mode overlay, button visual states |
| `MainPage.xaml.cs` | GestureRecognizer for swipe, loading state machine, URL autocomplete |
| `MainPage.xaml.cs` (settings) | Clear cache button, reading mode toggle |
| `CustomHtmlEngine.cs` | New `TextExtract(LiteElement)` method for reading mode. Error page template. |
| `ResourceManager.cs` | Public `ClearCache()` method (clear LRU + delete cache folders) |
| `BrowserApi.cs` | Maybe: history store for autocomplete suggestions |

### Estimated effort: 3-5 days / ~700 lines

### Priority: 🟡 Medium — makes the browser feel polished

---

## Phase 12: AI Integration (DeepSeek via OpenRouter)

**Goal:** An AI button on the expanded AppBar that sends current page content to DeepSeek (via OpenRouter API) and displays a summary/analysis in a side panel. No complex RAG, no vector DB — just page text → LLM → formatted output.

### Why DeepSeek + OpenRouter?
- **DeepSeek V3** is the cheapest high-quality model on OpenRouter ($0.50/M tokens). For a ~5KB page summarization, that's ~$0.000005 per request.
- **OpenRouter** provides a single OpenAI-compatible REST API (`POST https://openrouter.ai/api/v1/chat/completions`) — no SDK needed. Works with `HttpClient` directly.
- No API key management on the client side — key is hardcoded or stored in `ApplicationData.LocalSettings` (user-provided).

### Architecture

```
User taps "AI" button on AppBar
  → MainPage extracts page text from LiteDom
    → LiteDomUtil.ExtractText(LiteElement): recursive walk, collect innerText of all non-script/non-style nodes
    → Truncate to ~4000 tokens (~16KB text)
  → Send POST to OpenRouter API:
    POST https://openrouter.ai/api/v1/chat/completions
    Headers:
      Authorization: Bearer {user-api-key}
      Content-Type: application/json
    Body:
    {
      "model": "deepseek/deepseek-chat",
      "messages": [
        { "role": "system", "content": "Summarize the following web page content in 3-5 bullet points. Be concise." },
        { "role": "user", "content": "{page_text}" }
      ],
      "max_tokens": 500,
      "temperature": 0.3
    }
  → Parse JSON response → extract summary text
  → Display in a slide-up overlay panel
```

### Cost per request
- Average page text: ~3000 tokens input
- Summary output: ~150 tokens
- DeepSeek V3: $0.50/M input, $2.00/M output
- Per request: ~$0.0015 + $0.0003 = **$0.0018** (< 0.2¢)
- 555 free summaries per dollar

### Key decisions

| Decision | Option | Rationale |
|----------|--------|-----------|
| API key storage | `ApplicationData.LocalSettings["OpenRouterKey"]` with Settings textbox | User brings their own key. No server-side proxy needed. |
| Model | `deepseek/deepseek-chat` (V3) | Cheapest capable model on OpenRouter. DeepSeek V4/R1 available too but more expensive. |
| Page text extraction | `LiteDomUtil.ExtractText(element)` — DFS, skip `<script>`, `<style>`, `<nav>`, `<footer>` | Pure C# DOM walk, no LLM pre-processing. Truncate at 4000 tokens. |
| UI | Slide-up overlay panel (same as reading mode, or separate) | Bottom sheet pattern — doesn't navigate away from current page. Has "Close" button and "Copy" button. |
| Streaming | No — simple request/response | Streaming adds complexity (WebSocket or SSE). For a 150-token summary, latency is ~1-2s even without streaming. |

### Files affected

| File | Change |
|------|--------|
| `MainPage.xaml` | AI button on AppBar. AI overlay panel (TextBlock + Close button + loading indicator). Settings: API key TextBox. |
| `MainPage.xaml.cs` | AI button handler: extract text → call API → show result. API key load/save. |
| `LiteDomUtil.cs` | New `ExtractText(LiteElement, maxTokens)` method. |
| New `OpenRouterClient.cs` | Static `SummarizeAsync(string apiKey, string text)` — HTTP POST, parse JSON response. |

### OpenRouter API call (c# sketch)

```csharp
public static async Task<string> SummarizeAsync(string apiKey, string pageText)
{
    var json = $@"{{
        ""model"": ""deepseek/deepseek-chat"",
        ""messages"": [
            {{ ""role"": ""system"", ""content"": ""Summarize in 3-5 bullet points."" }},
            {{ ""role"": ""user"", ""content"": {JsonEncode(pageText)} }}
        ],
        ""max_tokens"": 500
    }}";
    var client = new HttpClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    var resp = await client.PostAsync("https://openrouter.ai/api/v1/chat/completions",
        new StringContent(json, Encoding.UTF8, "application/json"));
    var body = await resp.Content.ReadAsStringAsync();
    // Parse: JObject.Parse(body)["choices"][0]["message"]["content"]
    return extractedText;
}
```

### Estimated effort: 2-3 days / ~350 lines

Dependencies: None (uses `HttpClient` directly, not `ResourceManager`). LiteDom already accessible.

### Priority: 🟢 Nice-to-have — fun feature, zero impact on rendering pipeline

---

## Summary

| Phase | Title | Lines | Effort | Priority |
|-------|-------|-------|--------|----------|
| 6 | ES Modules (NiL.JS Native) | ~220 | 2 days | 🔴 Unlocks modern JS (with Phase 8) |
| 7 | Service Worker + Offline | ~800 | 4-6 days | 🟡 Offline UX |
| 8 | Incremental Re-render | ~500 (Step A) | 3 days / 3-7 left | 🔴 SPA readiness |
| 9 | CSS Property Expansion | ~1000 | 5-10 days | 🟢 Page correctness |
| 10 | DevTools | ~600 | 3-5 days | 🟢 Developer experience |
| 11 | UI Polish | ~700 | 3-5 days | 🟡 UX polish |
| 12 | AI Integration (DeepSeek) | ~350 | 2-3 days | 🟢 Fun feature |

**Total:** ~6150 lines, ~30-40 working days

## Architecture evolution (full pipeline)

```
HTML → LiteDom → JsDomElement ↔ NiL.JS (ES Modules via ModuleResolver)
                                          ↓
                                    MutationObserver
                                          ↓
LiteDom (mutated) → MutationProcessor → subtree dirty flags
       ↓
CssLoader.CascadeSingle(node) → CssComputedStyle
       ↓
LayoutEngine.InvalidateSubtree(node)→Relayout subtree (Task.Run)
       ↓
VirtualizingRenderer.Patch() → update XAML in place
       ↓
ServiceWorker intercepts fetch → CacheStorage API → ResourceManager disk cache → HTTP
```

---

## Threading model (post all phases)

```
UI Thread:     [Input] → DevTools console eval → NiL.JS RunInline
                       → MutationObserver tick
                       → CssLoader.CascadeSingle (uses selector index, O(candidates))
                       → VirtualizingRenderer.Patch (update XAML)
                       → Paint (XAML composition)

Background 1:  LayoutEngine.PerformLayout (subtree, only if geometry changed)
Background 2:  ResourceManager HTTP fetch (IO-bound)
Background 3:  ServiceWorker install/activate (IO-bound)
Background 4:  ES Module graph resolution (CPU-bound: parse, build graph)
```

---

---

## Session 2.13: White Screen Fixes, AppBar Modes, Settings Page

### Bug fixes (render pipeline)

| Fix | Root cause | Files |
|-----|-----------|-------|
| White screen | `MainPage_SizeChanged` → `NavigateAsync` после каждого рендера (второй проход очищал `ContentHost`) | `MainPage.xaml.cs` |
| Stale repaint duplication | Асинхронные `RepaintReady` от предыдущих навигаций применялись после новых | `MainPage.xaml.cs` — `_renderSequence` guard |
| `RPC_E_WRONG_THREAD` | `SolidColorBrush` в `Background`, `Foreground`, `BorderBrush`, `OutlineBrush` создавался в background thread (CssLoader, RenderTreeBuilder) | `CssComputed.cs`, `CssLoader.cs`, `RendererStyles.cs`, `RenderTreeBuilder.cs` |
| ResourceManager memory leak | Вставка без проверки дубликатов ключа в `LinkedListNode` | `ResourceManager.cs` |

### New features

| Feature | Description |
|---------|-------------|
| AppBar modes | Full (52px always) / Semi (24→52) / Hided (6→52) — выбор в Settings → UI |
| Settings page | Отдельная страница с Pivot (General / UI / Advanced / About). `Frame.Navigate(typeof(SettingsPage))` |
| Diagnostics | `[DIAG]` маркеры во всех стадиях пайплайна (NavigateAsync, ResetContentHost, RepaintReady, RenderAsync phases, BuildVisualTreeAsync steps, RenderTreeBuilder counts, VirtualizingRenderer canvas) |
| Omnibox placeholder | `PlaceholderTextContentPresenter` добавлен в кастомный шаблон TextBox |
| AppBar touch target | BarStrip 4→12, BottomBar collapsed 20→24, expanded 48→52 |

### Settings page structure

```
SettingsPage (Frame.Navigate)
  └── Pivot:
       ├── General: JS toggle, Home Page
       ├── UI: AppBar mode ComboBox (Full/Semi/Hided)
       ├── Advanced: API Key, Clear Cache
       └── About: version info
```

### Plan B: Hybrid Render Engine

Если `CustomHtmlEngine` упрётся в производительность (SPA, тяжёлые сайты), альтернатива — `IBrowserEngine` interface с двумя реализациями:

```
interface IBrowserEngine {
    Task<FrameworkElement> RenderAsync(string html, Uri baseUri, ...);
    Task<string> GetTextContent();
    void SetJavaScriptEnabled(bool enabled);
    event Action<string> StatusMessage;
}
```

| Реализация | Когда использовать | Ограничения |
|------------|------------------|-------------|
| `CustomHtmlEngine` | Наш рендерер. Полный контроль DOM/XAML, AI-интеграция, чтение, волшебный курсор. | Медленный на SPA. |
| `EdgeHtmlEngine` (WebView) | Быстрый рендер сложных сайтов. Native EdgeHTML (Chakra) — C/C++, ядро ОС. | Чёрный ящик. AI/курсор через JS‑инъекцию + `ScriptNotify`. |

**AI-селекция через WebView (план Б):**
```js
// injected at page load
document.addEventListener('click', e => {
  window.external.notify(JSON.stringify({
    type: 'ai-selection',
    text: e.target.innerText?.slice(0, 2000)
  }));
});
// MainPage: WebView.ScriptNotify → DeepSeek API
```

Переключение: Settings → "Render engine" ComboBox (Custom / EdgeHTML). Без перезапуска.

### "Волшебный курсор" (AI виджет)

На `CustomHtmlEngine`:
- `PointerEntered` / `PointerMoved` на Border/TextBlock узла → `LiteElement` ссылка
- Показываем FloatingBorder рядом с курсором
- Кнопки: "Объясни", "Переведи", "Перефразируй"
- Текст берётся из `node.InnerText` (уже в C#)

На `EdgeHtmlEngine`:
- JS‑инъекция через `ScriptNotify` (см. выше)
- Ответ приходит строкой → тот же FloatingBorder

### Phase 8 Step B (Incremental Render)

Остаётся после Step A (MutationObserver API). Суть:
- `MutationObserver` уже записывает мутации (Step A)
- Step B: `VirtualizingRenderer.Patch()` — обновление только изменённого XAML-узла вместо полного `Clear()+Add()`
- `LayoutEngine.InvalidateSubtree()` — dirty-флаги на поддереве
- `CssLoader.CascadeSingle()` — пересчёт стилей одного узла

Без Step B каждый `innerHTML=` вызывает полный `RenderAsync`. Для статических страниц (dzen.ru, ya.ru) это не критично. Для SPA — тормоза.

### Known issues (unchanged)

- Dzen.ru, ya.ru still show white screen despite `MainPage_SizeChanged` fix — may need debug log to confirm single pass
- `NiL.JS.Core.JSException` on complex sites — expected for limited JS engine
- `System.IO.FileNotFoundException` in ResourceManager — some from 404 trackers, some may indicate missing resource hooks
- BuildTools 2019 lacks UWP XAML targets — build requires Visual Studio

### Files changed (session 2.13)

New: `SettingsPage.xaml`, `SettingsPage.xaml.cs`, `Doc/Summary_2_13.md`

Modified: `MainPage.xaml`, `MainPage.xaml.cs`, `CssComputed.cs`, `CssLoader.cs`, `RendererStyles.cs`, `ResourceManager.cs`, `CustomHtmlEngine.cs`, `RenderTreeBuilder.cs`, `VirtualizingRenderer.cs`, `WEBVIEW.csproj`, `Doc/Plan_02.md`

---

*Plan v2.2 — 2026-05-17*
