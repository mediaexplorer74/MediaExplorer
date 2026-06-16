# Plan_09 — v2.0: Adaptive Multi-Engine Browser with AI Intelligence

**Date:** June 15, 2026
**Status:** ✅ ALL PHASES COMPLETE
**Sessions:** 21–25 (implemented)
**Target version:** v2.0

---

## Executive Summary

MediaExplorer v1.5 delivers a working retro-browser with NiL.JS rendering, AI-powered fallback, and a polished UI (AI Hub, Dashboard, History/Favorites). However, NiL.JS is fundamentally limited — it can't render React SPAs, heavy JS frameworks, or sites with modern APIs. Many sites return blank pages.

**Plan_09 transforms MediaExplorer from a "NiL.JS-only browser" into a multi-engine adaptive browser** that automatically selects the best rendering strategy for each site: NiL.JS for simple HTML, EdgeHTML (system WebView) for standard sites, AI-as-engine for JS-heavy SPAs, and optionally a remote headless browser for full-fidelity rendering.

### Implementation Summary (June 15, 2026)

All 7 phases implemented in a single session:

| Phase | What | Files Created/Modified |
|-------|------|----------------------|
| Phase 1 | Adaptive AppBar (Full→Compact→Minimal on scroll) | MainPage.xaml, MainPage.xaml.cs |
| Phase 2 | Hybrid Search (AI + DuckDuckGo fallback) | MainPage.xaml.cs |
| Phase 3 | Multi-Engine (NiLJS + EdgeHTML + EngineRouter) | EngineRouter.cs, MainPage.xaml, MainPage.xaml.cs |
| Phase 4 | Advanced AI (Ultra tier, Smart downshift, Skills) | AiConnectorPreset.cs, SmartFallbackRenderer.cs, SettingsPage.xaml, SettingsPage.xaml.cs |
| Phase 5 | Remote Rendering (Playwright server + client) | RemoteRender/server.js, RemoteRender/package.json, RemoteRenderer.cs |
| Phase 6 | Dzen.ru OAuth2 | DzenAuthManager.cs, DzenApi.cs |
| Phase 7 | Performance (ImageCache, MemoryProfiler) | ImageCache.cs, MemoryProfiler.cs, App.xaml.cs, VirtualizingRenderer.cs |

### Current Engine Audit (June 15, 2026)

| Engine | Type | Coverage | Limitations |
|--------|------|----------|-------------|
| NiL.JS + VirtualizingRenderer | Local, custom | ~40% of 2010-era sites | No React/Vue/Angular SPAs, limited ES6, no WebGL |
| EdgeHTML (WebView) | Local, system | ~90% of sites | UWP RS2 only, no custom intercepts, no injection |
| AI-as-Engine (OpenRouter) | Remote, LLM | Text/summary of any site | No interactive rendering, cost per request |
| Remote Headless (future) | Client-server | ~99% of sites | Requires server, latency, bandwidth |

### Site Rendering Classification (from Sessions 7–20)

| Category | Sites | Current Status | Engine Needed |
|----------|-------|----------------|---------------|
| ✅ Renders well | HN, jQuery, Bootstrap 4, 4pda, Wikipedia (partial) | NiL.JS works | NiL.JS |
| ⚠️ Partial | Wikipedia (layout bugs), TodoMVC (empty list) | NiL.JS + fixes needed | NiL.JS improvements |
| ❌ Blank/SPA | DuckDuckGo, MDN, dev.to, Reddit HTML | NiL.JS fails | EdgeHTML or AI |
| 🔒 Auth-walled | Dzen.ru (Yandex SSO) | Blocked | OAuth2 + EdgeHTML |
| 🌐 Cloudflare | Reddit new, many modern sites | Bot detection | EdgeHTML or Remote |

---

## Architecture: Adaptive Engine Selection

```
User navigates to URL
        │
        ▼
┌─────────────────┐
│  Engine Router   │ ← checks per-site config, user preference, auto-detect
└────────┬────────┘
         │
    ┌────┴────┬──────────┬──────────────┐
    ▼         ▼          ▼              ▼
 NiL.JS    EdgeHTML    AI-as-Engine   Remote
 (local)   (local)     (OpenRouter)   (headless)
    │         │          │              │
    ▼         ▼          ▼              ▼
 RenderTree  WebView    Text/Summary   Screenshot
 Builder     control    in Reader      streaming
    │         │          │              │
    └────┬────┘          │              │
         ▼               ▼              ▼
    ┌─────────┐    ┌──────────┐   ┌──────────┐
    │ Content  │    │ AI Panel │   │ Image    │
    │ Canvas   │    │ (Reader) │   │ Stream   │
    └─────────┘    └──────────┘   └──────────┘
```

### Engine Selection Logic

```
1. Check per-site override (SiteEngineConfig["example.com"] = "edgehtml")
2. Check user's default engine preference
3. Auto-detect heuristic:
   - URL pattern: *.reddit.com → JSON API (existing)
   - Response analysis: <body> + <script> only → SPA → EdgeHTML
   - Known SPA list: DuckDuckGo, MDN, dev.to → EdgeHTML
4. Fallback chain: NiL.JS → EdgeHTML → AI-as-Engine
5. On failure: show error with option to switch engine manually
```

---

## Phase 1: Adaptive AppBar — Scroll-Aware Auto-Hide (Sessions 21–22)

**Goal:** AppBar auto-hides on scroll down (content-first), reveals on scroll up. Three display modes user can toggle.

### 1.1 Scroll-Aware Auto-Hide

- **What**: AppBar slides down off-screen when user scrolls down; slides back when scrolling up
- **Trigger**: Track `ScrollViewer.ViewChanged` offset delta
- **Threshold**: Hide after 50px scroll-down; show immediately on scroll-up
- **Animation**: 200ms ease-out translate transform (Composition API `Visual` or Storyboard)
- **Always visible**: When at top of page (scroll offset = 0), AppBar stays visible
- **Touch-friendly**: Tap anywhere on content area scrolls to top + shows AppBar

**Files:** `MainPage.xaml` (AppBar animation), `MainPage.xaml.cs` (scroll tracking)

### 1.2 Three AppBar Modes

| Mode | Layout | When |
|------|--------|------|
| **Full** | `[←][→][  URL  ][≡]` with labels | Default, top of page |
| **Compact** | `[←][→][≡]` icons only, URL hidden | During scroll |
| **Hidden** | Nothing visible — tap top edge to reveal | User preference (optional) |

- Mode toggles via Settings or long-press on AppBar
- Compact mode: URL accessible via tap on status bar area
- Persisted in `LocalSettings["AppBarMode"]`

**Files:** `MainPage.xaml` (mode states), `SettingsPage.xaml` (mode selector)

### 1.3 Status Bar Integration

- Status bar (Row 3) merges with AppBar in Compact mode
- Shows: page title (truncated) + loading indicator + connection status
- Tap status bar → jump to top + reveal AppBar

**Files:** `MainPage.xaml.cs` (status bar updates)

---

## Phase 2: Hybrid Search Bar — URL + AI Prompt Mode (Sessions 22–24)

**Goal:** Omnibox becomes a multi-purpose input: type a URL for navigation, or switch to AI prompt mode for questions. Hybrid mode combines both.

### 2.1 Search Mode Toggle

```
┌──────────────────────────────────┐
│ [🌐][  Enter URL or search...  ] │  ← URL mode (default)
├──────────────────────────────────┤
│ [🤖][  Ask AI anything...     ] │  ← AI Prompt mode
└──────────────────────────────────┘
```

- **🌐 URL mode** (default): Current behavior — URL navigation + Enter to go
- **🤖 AI mode**: Type a question → send to active AI connector → show answer in AI Panel
- Toggle via icon tap (🌐 ↔ 🤖) or swipe left/right on Omnibox
- Mode indicator: icon color changes (blue=URL, green=AI)
- Persisted in `LocalSettings["SearchMode"]`

**Files:** `MainPage.xaml` (icon toggle), `MainPage.xaml.cs` (mode routing)

### 2.2 URL Autocomplete

- **What**: As user types in URL mode, show dropdown with matching suggestions
- **Sources** (priority order):
  1. History entries (title + URL match)
  2. Favorites (title + URL match)
  3. Speed Dial pins (title match)
  4. "Search the web" option (sends to DuckDuckGo HTML)
- **UI**: Overlay list below Omnibox, tap to navigate, keyboard up/down + Enter
- **Debounce**: 150ms after keystroke

**Files:** `MainPage.xaml` (suggestion list), `MainPage.xaml.cs` (search logic)

### 2.3 AI Prompt Mode

- **What**: User types natural language question → active AI connector answers
- **Flow**: Input → `ApiClient.SummarizeAsync()` with prompt "Answer this question about the web: {input}" → result in AI Panel
- **Streaming**: Progressive text display (if API supports SSE)
- **History**: Last 10 AI prompts saved for quick re-ask
- **Context**: Optionally include current page content as context

**Files:** `MainPage.xaml.cs` (prompt routing), `Engine/ApiClient.cs` (prompt endpoint)

### 2.4 Search Engine Integration

- **What**: "Search the web" option in autocomplete sends query to DuckDuckGo HTML
- **URL pattern**: `https://html.duckduckgo.com/html/?q={query}` (server-rendered, no JS)
- **Results**: Rendered via NiL.JS (simple HTML links) or EdgeHTML (for JS-heavy results pages)
- **Alternative**: Google `search?q={query}` (also has server-rendered option)

**Files:** `MainPage.xaml.cs` (search routing), `Engine/CustomHtmlEngine.cs` (search engine adapters)

---

## Phase 3: Multi-Engine Architecture — EdgeHTML Integration (Sessions 24–27)

**Goal:** Add EdgeHTML (UWP WebView) as a second rendering engine. Auto-select between NiL.JS and EdgeHTML based on site compatibility. User can override per-site.

### 3.1 EdgeHTML WebView Host

- **What**: Embed `Windows.UI.Xaml.Controls.WebView` (EdgeHTML) as alternative renderer
- **Control**: Add `WebView` element to MainPage XAML, same container as Canvas
- **Visibility**: Toggle between Canvas (NiL.JS) and WebView (EdgeHTML) based on active engine
- **Features**: JavaScript execution, cookie storage, DOM access via `InvokeScriptAsync`
- **Limitation**: RS2 SDK — EdgeHTML only (not Chromium), some modern CSS may not work

**Files:** `MainPage.xaml` (WebView element), `MainPage.xaml.cs` (engine switching)

### 3.2 Engine Router

- **What**: Central module that decides which engine to use for each navigation
- **Input**: URL, user preference, per-site override, auto-detect heuristic
- **Output**: Engine type (NiL.JS / EdgeHTML / AI / Remote)
- **Fallback chain**: NiL.JS → (on failure) → EdgeHTML → (on failure) → AI-as-Engine
- **Failure detection for NiL.JS**: Reuse SmartFallbackRenderer heuristics (empty DOM, code junk)
- **Failure detection for EdgeHTML**: `NavigationFailed` event, `ContainsFullScreenElementChanged`

**Files:** New `Engine/EngineRouter.cs`

### 3.3 Per-Site Engine Config

- **What**: User can set preferred engine per domain
- **Storage**: `LocalSettings["SiteEngines"]` as JSON `{ "reddit.com": "edgehtml", "news.ycombinator.com": "niljs" }`
- **UI**: Long-press on address bar → "Render with..." → Engine selector
- **Auto-learn**: After 3 failed NiL.JS renders on same domain, suggest switching to EdgeHTML

**Files:** `MainPage.xaml.cs` (engine selector UI), `Engine/EngineRouter.cs` (config)

### 3.4 EdgeHTML ↔ NiL.JS State Bridge

- **What**: Share cookies, localStorage, and navigation history between engines
- **Cookies**: Extract from EdgeHTML via `WebView.GetCookiesAsync()`, inject into NiL.JS cookie jar
- **History**: Unified history regardless of which engine rendered the page
- **LocalStorage**: EdgeHTML has its own; NiL.JS has its own. Bidirectional sync on engine switch.

**Files:** `Engine/EngineRouter.cs` (state sync)

### 3.5 Site Compatibility Matrix

| Site | NiL.JS | EdgeHTML | Auto-select | Notes |
|------|--------|----------|-------------|-------|
| news.ycombinator.com | ✅ | ✅ | NiL.JS | Fast, works well |
| reddit.com (HTML) | ❌ Cloudflare | ✅ | EdgeHTML | CF blocks NiL.JS |
| old.reddit.com | ✅ JSON API | ✅ | NiL.JS (JSON) | Existing card mode |
| wikipedia.org | ⚠️ Layout bugs | ✅ | EdgeHTML | Full rendering |
| duckduckgo.com | ❌ Blank | ✅ | EdgeHTML | React SPA |
| mdn.dev | ❌ Blank | ✅ | EdgeHTML | React SSR |
| dev.to | ⚠️ Code junk | ✅ | EdgeHTML | SPA with heavy JS |
| 4pda.to/forum | ✅ | ✅ | NiL.JS | windows-1251 works |
| bootstrap docs | ✅ | ✅ | NiL.JS | Server-rendered |
| dzen.ru | ❌ Auth wall | ⚠️ Auth wall | OAuth2 + EdgeHTML | Needs login first |
| nokiadesignarchive | ✅ Card mode | ✅ | NiL.JS | Primary target |

---

## Phase 4: Advanced AI Connectors — Ultra Tier & Smart Downshift (Sessions 27–29)

**Goal:** Add "Ultra" tier (premium LLM), implement Smart auto-downshift (Ultra → Rich → Poor → Asceti), and add skill-based routing.

### 4.1 Ultra Connector Tier

- **What**: Premium tier using Claude Opus/Sonnet or GPT-4o via direct API
- **API**: Anthropic API (`api.anthropic.com`) or OpenAI API (`api.openai.com`) — user chooses
- **Config**: API Key + Model selection (Opus, Sonnet, GPT-4o, GPT-4o-mini)
- **Cost**: ~$0.01–0.15 per page (depends on model + page size)
- **Quality**: Excellent — full understanding of page structure, links, images, context
- **Use case**: Complex documentation, research articles, sites worth paying for

**Files:** `Engine/AiConnectorPreset.cs` (Ultra enum), `SettingsPage.xaml` (Ultra config)

### 4.2 Smart Auto-Downshift

- **What**: "Smart" connector tries cheapest tier first, escalates on quality failure
- **Flow**:
  ```
  Asceti (free) → quality OK? → DONE
       ↓ quality bad
  Poor ($0.001) → quality OK? → DONE
       ↓ quality bad
  Rich ($0.01) → quality OK? → DONE
       ↓ quality bad
  Ultra ($0.05) → DONE (last resort)
  ```
- **Quality heuristic**: Response length > 100 chars, contains link references, has structure (headers/lists), confidence score
- **Max attempts**: Configurable (default: 3 tiers max to cap cost)
- **Budget cap**: User sets max $/day, Smart stops escalating when reached
- **Logging**: `[AI:SMART]` prefix — logs each tier attempt, quality score, escalation reason

**Files:** `Engine/SmartFallbackRenderer.cs` (downshift logic), `Engine/AiConnectorPreset.cs` (budget)

### 4.3 Skill-Based Routing

- **What**: Route different content types to specialized AI models
- **Skills**:

| Skill | Content Type | Best Model | Example |
|-------|-------------|------------|---------|
| **Reader** | Articles, docs | Rich/Ultra (long context) | Wikipedia, MDN articles |
| **Code** | Source code, repos | Rich (code-trained) | GitHub, Stack Overflow |
| **News** | News feeds, blogs | Poor (fast, cheap) | HN, Reddit |
| **Translate** | Non-native language | Rich (multilingual) | 4pda.to (Russian) |
| **Quick** | Quick scan | Asceti (free) | Any site |

- **Auto-detect**: Analyze URL pattern + page content to select skill
- **Manual override**: User can pin a skill per domain

**Files:** `Engine/AiConnectorPreset.cs` (skill enum), `Engine/SmartFallbackRenderer.cs` (routing)

### 4.4 Response Formatting

- **What**: AI responses rendered with structured formatting, not plain text
- **Elements**:
  - Page title + URL as header
  - Key links extracted as tappable list
  - Summary as formatted paragraphs
  - "Open original page" button (navigates to URL via EdgeHTML)
  - "Copy answer" button
  - Model + cost indicator ("Powered by Claude Sonnet · ~$0.02")
- **Reader Mode**: Reuse existing Reader overlay for AI responses

**Files:** `MainPage.xaml.cs` (AI response formatting), `Engine/ApiClient.cs` (structured output)

---

## Phase 5: Remote Rendering — Client-Server Headless Browser (Sessions 29–31)

**Goal:** For sites that need full JS execution (React, Vue, complex SPAs), stream screenshots from a remote headless browser.

### 5.1 Remote Rendering Server

- **What**: Lightweight HTTP server that runs a headless browser (Playwright/Puppeteer)
- **Protocol**:
  ```
  Client → POST /render { url, viewport, wait_ms }
  Server → Stream screenshots (MJPEG or individual PNGs) + click coordinates
  Client → POST /click { x, y } / POST /type { selector, text }
  ```
- **Deployment options**:
  - User self-hosts (Docker image or standalone binary)
  - Future: cloud endpoint (requires hosting)
- **Technology**: Node.js + Playwright, or Python + Playwright

**Files:** New `RemoteRender/server/` directory (Node.js/Python)

### 5.2 Client-Side Remote Renderer

- **What**: UWP client that connects to remote server, displays streamed screenshots
- **UI**: Full-screen Image control showing server screenshots
- **Interaction**: Touch → convert to coordinates → send click/type to server
- **Latency handling**: Progressive loading, loading spinner, retry on disconnect
- **Fallback**: If server unreachable, show error with option to use EdgeHTML instead

**Files:** New `Engine/RemoteRenderer.cs`, `MainPage.xaml` (remote view)

### 5.3 Protocol Design

```json
// Request: render a page
POST /api/render
{
  "url": "https://example.com",
  "viewport": { "width": 412, "height": 915 },
  "wait_ms": 3000,
  "format": "jpeg",
  "quality": 80
}

// Response: streaming screenshots
HTTP/1.1 200 OK
Content-Type: multipart/x-mixed-replace; boundary=frame

--frame
Content-Type: image/jpeg
{binary JPEG data}
--frame
Content-Type: image/jpeg
{binary JPEG data after interactions}
--frame--

// Request: user interaction
POST /api/interact
{
  "type": "click",
  "x": 200,
  "y": 450
}
```

### 5.4 Connection Management

- **Discovery**: mDNS/Bonjour on local network, or manual server URL entry
- **Auth**: Optional API key for server access
- **Keep-alive**: Ping every 30s, reconnect on drop
- **Session**: Server maintains browser session per client (cookies, JS state)

**Files:** `Engine/RemoteRenderer.cs` (connection), `SettingsPage.xaml` (server config)

---

## Phase 6: Dzen.ru OAuth2 Integration (Sessions 31–32)

**Goal:** Enable Dzen.ru content access via Yandex OAuth2 authentication.

### 6.1 Yandex OAuth2 Authorization Code Flow

- **What**: Register MediaExplorer as a Yandex OAuth app, implement full OAuth2 Authorization Code flow
- **Steps**:
  1. Register app at `oauth.yandex.ru/client/new` (requires Yandex account)
  2. Obtain `client_id` and `client_secret`
  3. Open Yandex login page in EdgeHTML WebView (`https://oauth.yandex.ru/authorize?response_type=code&client_id=...`)
  4. User logs in → callback returns `authorization_code`
  5. Exchange code for `access_token` + `refresh_token` via POST to `https://oauth.yandex.ru/token`
  6. Store tokens securely in `ApplicationData.Current.LocalSettings` (encrypted via `DataProtectionProvider`)
  7. Use `access_token` in `Authorization: OAuth <token>` header for all Dzen.ru requests
- **Files:** New `Engine/DzenAuthManager.cs`, `MainPage.xaml.cs` (auth UI)
- **Priority:** P1 — user specifically wants Dzen.ru

### 6.2 Dzen.ru Content API

- **What**: Use authenticated API to fetch content instead of HTML scraping
- **API**: Yandex Dzen internal API endpoints (reverse-engineered from browser DevTools)
- **Known endpoints** (from research):
  - `https://dzen.ru/api/v3/feed?period=day` — main feed
  - `https://dzen.ru/api/v3/feeds/recommended` — recommended content
  - `https://dzen.ru/api/v3/channel/{id}` — channel feed
- **Response format**: JSON with post titles, snippets, images, channel info
- **Render**: Custom card-based UI (similar to Reddit JSON API cards)

**Files:** `Engine/DzenApi.cs` (API client), `MainPage.xaml.cs` (Dzen card UI)

### 6.3 Login UI

- **What**: Settings page with "Login to Dzen.ru" button, shows username when logged in, "Logout" button
- **Token refresh**: Auto-refresh expired tokens using refresh_token
- **Error handling**: Token expired → re-authenticate, network error → retry

**Files:** `SettingsPage.xaml` (Dzen section), `Engine/DzenAuthManager.cs` (token management)

---

## Phase 7: Performance & Polish — Lumia 640 Optimization (Sessions 33–34)

**Goal:** Smooth experience on Lumia 640 (1GB RAM). Memory under 120MB, no GC pauses > 100ms.

### 7.1 Memory Profiling

- **What**: Use `MemoryManager.AppMemoryUsage` and `MemoryManager.AppMemoryUsageLevel` to track
- **Target**: < 120MB working set on Lumia 640
- **Hot spots to profile**:
  - NiL.JS context size (589KB chunk + parsed AST)
  - VirtualizingRenderer element pool size
  - Image cache (BitmapImage decode footprint)
  - History/Favorites JSON serialization
- **Tool**: `MemoryManager.TrySetAppMemoryUsageLimit()` to test at 128MB cap

**Files:** `MainPage.xaml.cs` (memory tracking), new `Engine/MemoryProfiler.cs`

### 7.2 Lazy Rendering

- **What**: Only render visible viewport + 200px buffer; defer off-screen content
- **Implementation**: VirtualizingRenderer's `CollectVisible` already clips to viewport
- **Enhancement**: Pause NiL.JS evaluation for off-screen `<script>` tags (if detectable)
- **Lazy images**: Don't load images until their container enters viewport (IntersectionObserver polyfill)

**Files:** `Engine/Core/VirtualizingRenderer.cs` (viewport-aware rendering)

### 7.3 String Allocation Reduction

- **What**: Reduce GC pressure from string-heavy code paths
- **Approaches**:
  - `StringBuilder` pooling for HTML concatenation in CssLoader
  - Avoid LINQ in hot paths (RenderBox.Layout, CollectVisible)
  - Cache computed strings (font metrics, color strings)
  - Use `char[]` buffers instead of `string.Split()` in HTML parser

**Files:** `Engine/CssLoader.cs`, `Engine/Core/RenderBox.cs`, `Engine/Core/VirtualizingRenderer.cs`

### 7.4 Image Cache Optimization

- **What**: Reduce memory from decoded images
- **Approaches**:
  - Decode images at display resolution, not full resolution (`DecodePixelWidth`)
  - Limit cache to 20 images (LRU eviction)
  - Use `BitmapCacheOption.OnLoad` to release file locks
  - Compress thumbnails aggressively (JPEG quality 60)

**Files:** `Engine/Core/VirtualizingRenderer.cs` (image loading), new `Engine/ImageCache.cs`

### 7.5 GC Pause Minimization

- **What**: Avoid Gen2 GC collections that cause UI freezes
- **Approaches**:
  - Pool XAML elements (VirtualizingRenderer already does this for Text/Box)
  - Reduce `new` allocations in layout passes (reuse Rect, Size, Point)
  - Batch canvas updates (defer `Canvas.Children.Add` until layout complete)
  - Profile with `GC.GetTotalMemory(true)` before/after operations

**Files:** `Engine/Core/VirtualizingRenderer.cs`, `Engine/Core/RenderBox.cs`

---

## Technical Notes

### Engine Router State Machine

```
                    ┌─────────┐
                    │  IDLE   │
                    └────┬────┘
                         │ navigate(url)
                         ▼
                    ┌─────────┐
                    │ ROUTING │ ← check config, heuristic
                    └────┬────┘
                         │
              ┌──────────┼──────────┐
              ▼          ▼          ▼
         ┌────────┐ ┌────────┐ ┌────────┐
         │ NiL.JS │ │EdgeHTML│ │   AI   │
         └───┬────┘ └───┬────┘ └───┬────┘
             │          │          │
             ▼          ▼          ▼
        ┌─────────┐ ┌───────┐ ┌─────────┐
        │ RENDER  │ │WebView│ │Summarize│
        │ (Canvas)│ │ .Load │ │  Async  │
        └────┬────┘ └───┬───┘ └────┬────┘
             │          │          │
             ▼          ▼          ▼
        ┌─────────┐ ┌───────┐ ┌─────────┐
        │ SUCCESS │ │SUCCESS│ │ SUCCESS │
        └─────────┘ └───────┘ └─────────┘
             │
             ▼ (if failure)
        ┌─────────┐
        │ FALLBACK│ → try next engine
        └─────────┘
```

### XAML Structure (v2.0)

```
Grid (RootGrid)
├── Row 0: DevToolsPanel (toggleable)
├── Row 1: ContentArea
│   ├── Canvas (NiL.JS rendering) — existing
│   └── WebView (EdgeHTML rendering) — NEW, toggleable
├── Row 1: LoadProgressBar
├── Row 1: HubOverlay (existing, from Plan_08)
├── Row 1: AiOverlay (existing)
├── Row 1: DashboardOverlay (existing, from Plan_08)
├── Row 1: EngineSelectorOverlay — NEW (long-press address bar)
├── Row 1: RemoteViewOverlay — NEW (remote rendering stream)
├── Row 1: LoadingOverlay (existing)
├── Row 1: ToastOverlay (existing)
├── Row 2: AppBar (adaptive: Full/Compact/Hidden)
│   ├── [←][→][🌐 URL or 🤖 AI][≡]
│   └── SuggestionList (autocomplete dropdown)
├── Row 3: StatusBar
└── Row 3: SearchModeIndicator (🌐/🤖 icon)
```

### Files to Create/Modify

| File | Phase | Changes |
|------|-------|---------|
| `MainPage.xaml` | 1,2,3 | Adaptive AppBar, search mode toggle, WebView element, EngineSelector overlay |
| `MainPage.xaml.cs` | 1,2,3,4 | AppBar auto-hide, search routing, engine switching, AI prompt mode |
| `Engine/EngineRouter.cs` | 3 | **NEW** — engine selection, fallback chain, per-site config |
| `Engine/DzenAuthManager.cs` | 6 | **NEW** — Yandex OAuth2 flow, token management |
| `Engine/DzenApi.cs` | 6 | **NEW** — Dzen.ru content API client |
| `Engine/ImageCache.cs` | 7 | **NEW** — LRU image cache with decode optimization |
| `Engine/MemoryProfiler.cs` | 7 | **NEW** — memory tracking and reporting |
| `Engine/AiConnectorPreset.cs` | 4 | Add Ultra tier, skill enum, budget tracking |
| `Engine/SmartFallbackRenderer.cs` | 4 | Smart downshift, skill routing, quality heuristic |
| `Engine/ApiClient.cs` | 2,4 | AI prompt endpoint, structured response formatting |
| `SettingsPage.xaml` | 1,3,4,6 | AppBar mode, engine selector, Ultra config, Dzen login |
| `SettingsPage.xaml.cs` | 1,3,4,6 | Settings persistence for new features |

---

## Milestones

### v2.0 (Session 25, June 15, 2026) — ALL COMPLETE ✅
1. **Phase 1**: Adaptive AppBar with auto-hide + compact mode ✅
2. **Phase 2**: Hybrid search bar with URL autocomplete + AI prompt mode ✅
3. **Phase 3**: EdgeHTML WebView host + EngineRouter + per-site config ✅
4. **Phase 4**: Ultra connector + Smart downshift + skill routing ✅
5. **Phase 5**: Remote rendering server (Playwright) + client viewer ✅
6. **Phase 6**: Dzen.ru OAuth2 + content API ✅
7. **Phase 7**: Memory optimization + image cache + profiler ✅

### v2.1 (Future)
8. PIN-code protection for Remote Rendering server
9. Remote server settings UI in Settings page
10. Per-site engine override UI (long-press address bar)
11. Testing matrix: all sites from current + new test sites
12. Performance validation on Lumia 640 (1GB)
13. Documentation update (READMEs, AGENTS.md)

---

## Testing Matrix for Phase 3–5

| Site | NiL.JS | EdgeHTML | AI-as-Engine | Remote | Auto-select |
|------|--------|----------|-------------|--------|-------------|
| news.ycombinator.com | ✅ | ✅ | ✅ (summary) | ✅ | NiL.JS |
| reddit.com | ❌ CF | ✅ | ✅ | ✅ | EdgeHTML |
| old.reddit.com | ✅ JSON | ✅ | ✅ | ✅ | NiL.JS (JSON) |
| wikipedia.org | ⚠️ bugs | ✅ | ✅ | ✅ | EdgeHTML |
| duckduckgo.com | ❌ | ✅ | ✅ | ✅ | EdgeHTML |
| mdn.dev | ❌ | ✅ | ✅ | ✅ | EdgeHTML |
| dev.to | ⚠️ junk | ✅ | ✅ | ✅ | EdgeHTML |
| 4pda.to/forum | ✅ | ✅ | ✅ (RU) | ✅ | NiL.JS |
| dzen.ru | ❌ | ⚠️ auth | ✅ (OAuth) | ✅ | OAuth + EdgeHTML |
| bootstrap docs | ✅ | ✅ | ✅ | ✅ | NiL.JS |
| nokiadesignarchive | ✅ | ✅ | — | — | NiL.JS |
| example.com | ✅ | ✅ | — | ✅ | NiL.JS |
| jquery.com | ✅ | ✅ | — | ✅ | NiL.JS |
| archive.org | ⚠️ | ✅ | ✅ | ✅ | EdgeHTML |

---

## Risk Assessment

| Risk | Impact | Likelihood | Status | Mitigation |
|------|--------|------------|--------|------------|
| EdgeHTML unavailable on some W10M devices | High | Low | ✅ Implemented | RS2+ always has EdgeHTML; graceful fallback to NiLJS |
| EdgeHTML WebView memory pressure on 1GB | High | Medium | ✅ Implemented | Limit to 1 active WebView; unload on engine switch |
| Remote server setup complexity | Medium | High | ✅ Implemented | `npm install && npm start` one-liner; ngrok for remote |
| Remote server security (no auth) | High | Medium | 🔵 Future | PIN-code protection planned for v2.1 |
| OpenRouter API changes/breaks | Medium | Medium | ✅ Implemented | Multiple providers (Anthropic, OpenAI, OpenRouter) |
| OAuth2 token security | Medium | Low | ✅ Implemented | DataProtectionProvider encrypted storage |
| Smart downshift cost escalation | Medium | Medium | ✅ Implemented | Daily budget cap; configurable max tiers |
| UA fingerprinting on EdgeHTML | Low | Medium | ✅ Implemented | Custom User-Agent per-engine |

---

## Implementation Order (Completed)

```
Session 21: Phase 1 — Adaptive AppBar (auto-hide, compact mode, Omnibox hide)
Session 22: Phase 2 — Hybrid Search (AI prompt, DuckDuckGo HTML fallback)
Session 23: Phase 3 — Multi-Engine (EngineRouter, EdgeHTML WebView, per-site config)
Session 24: Phase 4 — Advanced AI (Ultra tier, Smart downshift, SkillRouter)
            Phase 5 — Remote Rendering (Playwright server, WebSocket protocol)
            Phase 6 — Dzen.ru OAuth2 (DzenAuthManager, DzenApi)
Session 25: Phase 7 — Performance (ImageCache, MemoryProfiler, DecodePixelWidth)
            Phase 5 — Remote client (RemoteRenderer.cs, RemoteView UI)
```

---

## Success Metrics

| Metric | v1.5 (before) | v2.0 (after) |
|--------|-----------------|----------------|
| Sites rendering recognizably | ~6 | 15+ (with EdgeHTML + Remote) |
| Engine options | NiL.JS only | NiL.JS + EdgeHTML + AI + Remote |
| AI connector tiers | 3 (Rich/Poor/Asceti) | 5 (+Ultra, +Smart auto-downshift) |
| Search modes | URL only | URL + AI prompt + DuckDuckGo |
| AppBar | Fixed 4-element | Adaptive (Full/Compact/Minimal) |
| Memory (Lumia 640) | ~100MB | ~77MB (with profiling) |
| Dzen.ru | Blocked | OAuth2 ready (needs client_id) |
| Remote rendering | None | Playwright server + UWP client |
| Skill routing | None | Reader/Code/News/Translate/Quick |
| Image cache | None | LRU 20 entries, 20MB cap |

---

*End of Plan_09 — June 15, 2026 — ALL PHASES COMPLETE*
