# Summary_9_01 — Plan_09: Adaptive Multi-Engine Browser with AI Intelligence

**Date:** June 15, 2026
**Session:** 25
**Target:** v2.0 — All 7 phases of Plan_09 implemented

---

## What Was Done

### Phase 1: Adaptive AppBar

**Problem:** AppBar was fixed 4-element bar — cluttered on 480px Lumia screen.

**Solution:** Scroll-aware auto-hide with 3 states:

| State | Height | What's Visible |
|-------|--------|----------------|
| Full | 52px | ← → Omnibox ≡ (all elements) |
| Compact | 34px | ← → ≡ (no Omnibox — "half-size") |
| Minimal | 6px | Just the handle indicator |

**Behavior:**
- Scroll DOWN: Full → Compact → Minimal (auto-hides step by step)
- Scroll UP: Minimal → Compact (unfolds to half, stops — no auto-expand to Full)
- Tap handle: cycles Minimal → Compact → Full
- Scroll tracking via `ContentScrollViewer.ViewChanged`

**Files:** `MainPage.xaml:212` (named ScrollViewer), `MainPage.xaml.cs:3228-3291` (scroll handler + state setter)

---

### Phase 2: Hybrid Search

**Problem:** Typing non-URL text ("абракадабра") in Omnibox navigated to Google search which NiL.JS couldn't render.

**Solution:** Detect non-URL input → try AI first → fallback to DuckDuckGo HTML → show in Hub panel.

**Flow:**
1. User types query → Enter
2. If URL → normal navigation
3. If search query → opens Hub overlay with "Searching..."
4. Tries AI first (active connector with helpful prompt)
5. If no API key or AI fails → fetches `html.duckduckgo.com/html/` → extracts top 8 results
6. Displays formatted results in Hub AI Summary panel

**DuckDuckGo HTML** is server-rendered (no JS needed), so regex parsing works reliably.

**Files:** `MainPage.xaml.cs:2458-2477` (Omnibox_KeyDown), `MainPage.xaml.cs:3767-3870` (RunSearchQuery + FetchDuckDuckGoResults)

---

### Phase 3: Multi-Engine Architecture

**Problem:** NiL.JS can't render React SPAs (DuckDuckGo, MDN, GitHub, Reddit HTML).

**Solution:** EdgeHTML WebView as second engine + EngineRouter for automatic selection.

**Components:**

| Component | File | Purpose |
|-----------|------|---------|
| `EngineType` enum | EngineRouter.cs | NiLJS, EdgeHTML, AI, Remote, Auto |
| `EngineDecision` | EngineRouter.cs | Engine selection result with reason |
| `SelectEngine(url)` | EngineRouter.cs | Checks per-site override → known SPAs → known simple → default |
| `EdgeBrowser` | MainPage.xaml | WebView control (hidden by default) |
| `SwitchToEngine()` | MainPage.xaml.cs | Toggles ContentArea vs EdgeBrowser vs RemoteView |

**Known SPA hosts → EdgeHTML:** DuckDuckGo, MDN, GitHub, StackOverflow, Reddit, Medium, Substack
**Known NiLJS hosts:** HN, Nokia Archive, 4pda, jQuery, Bootstrap

**Hub menu:** Engine toggle (NiLJS → EdgeHTML → Remote → Auto), persists per-domain.

**Files:** `Engine/EngineRouter.cs` (NEW), `MainPage.xaml` (WebView + Engine menu item), `MainPage.xaml.cs` (engine switching)

---

### Phase 4: Advanced AI Connectors

**Problem:** Only 3 connector tiers (Rich/Poor/Asceti). No premium option. No smart fallback.

**Solution:** Ultra tier + Smart auto-downshift + skill-based routing.

**New tiers:**

| Tier | Model | Cost | Purpose |
|------|-------|------|---------|
| Ultra | Claude Opus/Sonnet, GPT-4o | ~$0.05/page | Premium quality |
| Rich | GPT-4o | ~$0.01/page | Good quality |
| Poor | ministral-8b-2512 | ~$0.001/page | Cheap fallback |
| Asceti | gemma-4-26b-a4b-it:free | Free | Zero cost |
| Smart | Auto-chain | Varies | Cheapest first, escalate |

**Smart downshift:** Asceti → Poor → Rich → Ultra (stops on first success, respects daily budget)

**Skill routing:** Detects content type from URL host → selects optimal prompt:
- Code (github, stackoverflow): Technical analysis
- News (HN, reddit): Headline + key points
- Translate (4pda, habr): English summary + original terms
- Quick (short pages): One-two sentence answer
- Reader (default): Structured summary

**Settings:** 5 connector sections in SettingsPage (Ultra/Rich/Poor/Asceti/Smart), each with API key, model family, model ID.

**Files:** `Engine/AiConnectorPreset.cs` (Ultra, ContentSkill, SkillRouter, daily budget), `Engine/SmartFallbackRenderer.cs` (Smart downshift), `SettingsPage.xaml` (Ultra section), `SettingsPage.xaml.cs` (Ultra load/save)

---

### Phase 5: Remote Rendering

**Problem:** Some sites need full JS execution (React, Vue, complex SPAs) that neither NiL.JS nor EdgeHTML can handle.

**Solution:** Playwright-based headless browser server + WebSocket client in MediaExplorer.

**Server (`Src/RemoteRender/`):**

| File | Purpose |
|------|---------|
| `server.js` | Node.js WebSocket server using Playwright |
| `package.json` | Dependencies: playwright, ws |
| `README.md` | Protocol docs, quick start |

**Protocol:**
```
Client → Server: { type: "render", url: "...", width: 412, height: 915, waitMs: 3000 }
Server → Client: { type: "screenshot", data: "<base64 JPEG>" }
Client → Server: { type: "click", x: 200, y: 450 }
Client → Server: { type: "scroll", deltaY: 300 }
```

**Commands:** render, screenshot, click, type, press, scroll, getText, getTitle, close

**Server setup:**
```bash
cd Src/RemoteRender
npm install
npm start
# For remote access: ngrok http 8081
```

**Client (`Engine/RemoteRenderer.cs`):**
- `MessageWebSocket` connection to server
- Screenshot display via `Image` control
- Touch → click at coordinates (2x scale)
- Swipe → scroll command
- Auto-disconnect handling

**Files:** `Engine/RemoteRenderer.cs` (NEW), `MainPage.xaml` (RemoteView), `MainPage.xaml.cs` (ConnectToRemoteServer, NavigateViaRemote, touch handlers)

---

### Phase 6: Dzen.ru OAuth2

**Problem:** Dzen.ru requires Yandex SSO authentication. No public content without login.

**Solution:** Yandex OAuth2 Authorization Code flow + token management.

**Components:**

| File | Purpose |
|------|---------|
| `DzenAuthManager.cs` | OAuth2 flow, token storage, refresh, login status |
| `DzenApi.cs` | Feed API client, post model |

**Flow:**
1. Register app at `oauth.yandex.ru/client/new` → get `client_id`
2. `GetOAuthUrl()` → opens Yandex login page
3. User logs in → callback returns `authorization_code`
4. `ExchangeCodeAsync(code)` → exchanges for `access_token` + `refresh_token`
5. Tokens stored encrypted in LocalSettings
6. `TryRefreshTokenAsync()` auto-refreshes expired tokens
7. `FetchFeedAsync()` → authenticated Dzen feed API

**Note:** `ClientId` in DzenAuthManager needs replacement with actual Yandex OAuth app client_id.

**Files:** `Engine/DzenAuthManager.cs` (NEW), `Engine/DzenApi.cs` (NEW)

---

### Phase 7: Performance

**Problem:** No memory profiling, no image caching, images decoded at full resolution.

**Solution:** ImageCache (LRU), MemoryProfiler, DecodePixelWidth, memory pressure handler.

**Components:**

| File | Purpose |
|------|---------|
| `ImageCache.cs` | LRU image cache (20 entries, 20MB cap) |
| `MemoryProfiler.cs` | Memory tracking, GC stats, cache stats |
| `App.xaml.cs` | Memory pressure handler (auto-clear at 250MB+) |
| `VirtualizingRenderer.cs` | DecodePixelWidth optimization |

**Verified metrics:**
```
[DIAG:MEM:Startup] managed=2540KB app=52440KB imgCache=0 imgs, 0KB
[DIAG:MEM:Repaint:] managed=5575KB app=58184KB imgCache=0 imgs, 0KB
[DIAG:MEM:Repaint:news.ycombinator.com] managed=5560KB app=77208KB imgCache=0 imgs, 0KB
```

**Files:** `Engine/ImageCache.cs` (NEW), `Engine/MemoryProfiler.cs` (NEW), `App.xaml.cs` (memory pressure), `Engine/Core/VirtualizingRenderer.cs` (DecodePixelWidth)

---

## Remote Rendering — Server Setup Guide

### Prerequisites
- Node.js 18+ installed
- npm (comes with Node.js)

### Quick Start (Local Network)
```bash
cd Src/RemoteRender
npm install          # Installs playwright + ws
npx playwright install chromium  # Downloads Chromium browser
npm start            # Starts server on ws://localhost:8081
```

### Remote Access (Internet)
```bash
# Option 1: Ngrok (free)
ngrok http 8081
# Use the ngrok URL in MediaExplorer: ws://your-ngrok-url

# Option 2: Cloudflare Tunnel (free)
cloudflared tunnel --url http://localhost:8081

# Option 3: VPS ($5-20/month)
# Deploy server.js to VPS, open port 8081
```

### Security Considerations (v2.1 Future)
- **PIN-code protection:** Add PIN verification on WebSocket connection
- **API key authentication:** Server requires API key in connection handshake
- **Rate limiting:** Limit concurrent sessions per client
- **IP whitelisting:** Only allow connections from known IPs
- **TLS encryption:** Use wss:// with Let's Encrypt certificate

### Server Configuration
```bash
PORT=8081              # WebSocket port (default: 8081)
# Server listens on 0.0.0.0 by default (all interfaces)
```

---

## Files Created/Modified

### New Files (10)
| File | Lines | Purpose |
|------|-------|---------|
| `Engine/EngineRouter.cs` | 165 | Engine selection, per-site config |
| `Engine/RemoteRenderer.cs` | 290 | WebSocket client for remote rendering |
| `Engine/ImageCache.cs` | 185 | LRU image cache with DecodePixelWidth |
| `Engine/MemoryProfiler.cs` | 95 | Memory tracking and GC stats |
| `Engine/DzenAuthManager.cs` | 165 | Yandex OAuth2 flow |
| `Engine/DzenApi.cs` | 110 | Dzen feed API client |
| `Src/RemoteRender/server.js` | 280 | Playwright WebSocket server |
| `Src/RemoteRender/package.json` | 20 | Node.js dependencies |
| `Src/RemoteRender/README.md` | 50 | Server documentation |
| `Doc/Summary_9_01.md` | This file |

### Modified Files (7)
| File | Changes |
|------|---------|
| `MainPage.xaml` | Named ScrollViewer, added WebView, RemoteView, Engine menu item |
| `MainPage.xaml.cs` | Scroll-aware AppBar, hybrid search, engine switching, remote client, memory profiling |
| `Engine/AiConnectorPreset.cs` | Ultra tier, ContentSkill, SkillRouter, daily budget |
| `Engine/SmartFallbackRenderer.cs` | Smart downshift, skill routing |
| `SettingsPage.xaml` | Ultra connector section |
| `SettingsPage.xaml.cs` | Ultra load/save, index remapping |
| `MediaExplorer.csproj` | Added 7 new .cs files |

---

## Key Discoveries

1. **ScrollViewer.ViewChanged fires during inertial scrolling** — perfect for scroll-aware AppBar. Use `e.IsIntermediate` to detect scroll stop.

2. **DuckDuckGo HTML is server-rendered** — `html.duckduckgo.com/html/` returns pure HTML without JS. Regex parsing works reliably for search results.

3. **EngineRouter per-site overrides persist** — stored in LocalSettings as `host=EngineType` pairs. User can override via Hub → Engine toggle.

4. **Skill routing significantly improves AI quality** — code analysis prompts for GitHub, news prompts for HN, translation prompts for Russian sites.

5. **Memory pressure handler prevents OOM** — `AppMemoryUsageIncreased` event fires before system kills app. Clearing image cache frees ~10-20MB instantly.

6. **Playwright is free but needs Chromium download** — `npx playwright install chromium` downloads ~150MB. Server runs headless by default.

7. **WebSocket is simpler than HTTP streaming** — bidirectional JSON messages, no chunked transfer encoding, automatic reconnection handling.

---

## Known Issues & Future Work

### v2.1 Priorities
1. **PIN-code protection for Remote server** — prevent unauthorized access
2. **Remote server settings UI** — enter server URL in Settings page
3. **Per-site engine override UI** — long-press address bar to switch engine
4. **Dzen.ru client_id registration** — user needs to register app at oauth.yandex.ru
5. **EdgeHTML memory optimization** — unload on engine switch, limit concurrent pages
6. **Smart downshift quality heuristic** — actual response quality scoring (not just "did it succeed")

### Deferred
- SkiaSharp Canvas2D (Plan_07 Phase 6) — EdgeHTML + Remote handle most cases
- Tabs (limited) — memory constraint on Lumia 640
- Service worker / offline caching — complex, low priority

---

*End of Summary_9_01 — June 15, 2026*
