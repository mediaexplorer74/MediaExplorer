# MediaExplorer 2.0-alpha — ai_hub branch

![](/Images/logo.png)

## What Is This?

MediaExplorer is a hobby browser for Windows 10 Mobile (W10M, build 15063+) built **without** the system WebView or Chakra engine. It uses a custom HTML parser, CSS engine with selectors/cascade/flexbox, a JavaScript runtime powered by [NiL.JS](https://github.com/nilproject/NiL.JS), and a XAML-based renderer.

**v1.5 features an AI Hub overlay** — a scrollable menu with 10 tools (Favorites, History, AI Summary, Screenshot, Copy Text, E-book Mode, DevTools, Settings, Home, Engine). When a website can't be rendered by NiL.JS, the **Smart Fallback Renderer** automatically fetches the page, sends it to an AI connector (OpenRouter API), and displays a summary in the Hub AI panel.

~50 files, ~30k+ lines of code. Early **v2.0-alpha** stage built on top of the completed v1.5 architecture.

## Screenshots

![](/Images/sshot01.png)
![](/Images/sshot02.png)
![](/Images/sshot03.png)


## Features
- AppBar Situational Size, Hybrid Search, Multi-Engine Architecture, Advanced AI Connector, Remote Rendering [Playwright], DzenRu Auth2 Tweak
- **Custom rendering engine** — HTML parser, CSS cascade, flexbox, XAML renderer
- **JavaScript** — NiL.JS runtime with ES Modules support (Vite bundles parse; D3v4/v5 support)
- **AI Hub** — Scrollable overlay with Favorites, History, AI Summary, Screenshot, Copy Text, E-book Mode, DevTools, Settings, Home, Engine, Remote Session
- **Remote Rendering [Playwright]** — Configurable server URL, viewport, load delay, PIN / token field, connect/reconnect/disconnect controls, remote text fetch, and AI summary over remote page text
- **AI Connector Presets** — Rich (GPT-4o), Poor (Ministral-8B), Asceti (Gemma-4 free), Smart (auto-chain cheapest first). Per-connector API keys.
- **Start Dashboard** — Speed dial grid (6 pinned sites) + recent history list
- **History system** — Auto-records navigation, grouped by date (Today/Yesterday/This Week/Older), clear all
- **Favorites** — Add current page, remove per entry, stored in LocalSettings
- **DevTools** — Console, DOM inspector, Network tab, Debug log
- **3 UI modes** — Hided (strip), Semi (expandable), Full (standard app bar)
- **3 e-book modes** — Rich (full graphics, CSS + JS), Poor (card/index style, minimal CSS), Asceti (pure text, no CSS/images)
- **Disk cache** — Resource caching with priority queue
- **MutationObserver** — Incremental re-render on DOM changes
- **Snapshot** — Screenshot (single or full-page) saved to Pictures/MediaExplorer
- **Keyboard shortcuts** — Ctrl+L (focus URL), Ctrl+B (toggle bar), Ctrl+Home (Dashboard)
- **Smartphone card mode** — Auto-detects narrow viewport, swipeable cards with detail views
- **Nokia Design Archive integration** — Extracts entries/collections/stories from JS globals

## Status

- **v2.0-alpha in progress.** Plan 8 is complete; Plan 09 is underway with Hub polish, Dashboard polish, RemoteRender settings/session UX, and Automatic Rescue Path.
- **v1.5 architecture completed earlier.** AI Hub, Start Dashboard, History/Favorites, AI Connector presets, Smart Fallback Renderer.
- **Data extraction intact** — `__graphData` (755 nodes, 1647 links), `__entries` (722), `__stories` (230), `__collections` (33) available for future SkiaSharp renderer.

## Dev section (June 14, 2026)

### Key decisions
- **AI Hub** — Single scrollable overlay replaces cluttered 8-element AppBar with 4 elements (← → Omnibox ≡)
- **Smart Fallback** — When NiL.JS can't render a site, auto-fetch via HTTP → send to AI connector → show summary in the Hub AI panel
- **AI Connector Presets** — Rich/Poor/Asceti/Smart tiers with per-connector API keys (OpenRouter)
- **Poor mode = AI-first** — In Poor render mode, ALL sites trigger AI fallback automatically
- **E-book modes evolved** — Now serve as AI connector tier selector
- **Reddit** — old.reddit.com → card mode (JSON API); reddit.com → JSON API fallback

### Key files
`MainPage.xaml.cs` — AI Hub, Dashboard, History/Favorites, Smart Fallback, card mode
`Engine/AiConnectorPreset.cs` — ConnectorType enum, config, storage
`Engine/SmartFallbackRenderer.cs` — Empty/code-junk/ISP detection, AI chain
`Engine/ApiClient.cs` — OpenRouter API with configurable model
`Engine/Core/RenderTreeBuilder.cs` — HTML→RenderObject tree, UA styles
`Engine/Core/RenderBox.cs` — Flex/block/inline layout engine
`Engine/Core/VirtualizingRenderer.cs` — Canvas-based virtualizing renderer
`SettingsPage.xaml` — AI Connectors tab with per-connector config
`AGENTS.md` — automation loop commands

## Dev section END

This is a homemade browser engine — not production-ready, not intended to replace Edge or Chrome. It exists to prove that you don't need Chromium to render a webpage.

## Milestones

- **2026.06.20 — v2.0.0** Plan 09 Phase 1 cleanup + RemoteRender settings/session UX: Reader Mode traces removed, Hub transitions polished, Dashboard improved, Remote Render settings UI added, Remote Session panel added.
- **2026.06.14 — v1.1.0** E-book modes (Rich/Poor/Asceti), markdown-to-html, charset detection (windows-1251 etc.), table improvements (border-spacing, CAPTION), CSS edge cases (display:none, overflow-x/y, text-overflow:clip). JS always enabled.
- **2026.06.13 — v1.0.10** General web rendering. Hacker News fully renders: orange header, 30 news items, vote arrows, clickable navigation, footer. CSS pseudo-classes (`:link`/`:visited`), HTML presentation attributes (`bgcolor`, `width`), SVG logo support.
- **2026.06.12 — v1.0.0** Phases R+S+T complete. Card-based smartphone layout, entry detail views, category index, link routing. SVG→XAML bridge abandoned.
- **2026.06.07 — v0.55.0** D3.js force-directed graph (Nokia Design Archive) renders as live XAML shapes.
- **2026.06.05 — v0.50.0** First successful d3.js evaluation on UWP via NiL.JS.
- **2026.05.xx — v0.42.8** NiL.JS runtime ported to .NET Native 1.4 (W10M 15063-compatible).

## Testing

MediaExplorer works best with the Nokia Design Archive. For general web testing, these sites are recommended (ordered by compatibility):

| Site | Type | Notes |
|------|------|-------|
| [hackaday.com](https://hackaday.com) | Blog/ESP | Text + images, light JS |
| [arstechnica.com](https://arstechnica.com) | News | Heavy React, but has content |
| [wikipedia.org](https://wikipedia.org) | Wiki | Complex HTML/CSS, good load test |
| [archive.org](https://archive.org) | Archive | Similar spirit to Nokia Design Archive |
| [musicbrainz.org](https://musicbrainz.org) | Database | Text data, SPA |
| [openlibrary.org](https://openlibrary.org) | Books | Images + text, moderate JS |
| [librivox.org](https://librivox.org) | Audiobooks | Simple HTML, good stability test |
| [indiehackers.com](https://indiehackers.com) | Community | React SPA, content-heavy |
| [producthunt.com](https://producthunt.com) | Startups | Heavy SPA, good stress test |
| [news.ycombinator.com](https://news.ycombinator.com) | Minimalism | **Fully renders** — orange header, 30 items, vote arrows, clickable links |

**Tip:** Start with lightweight sites (Hackaday, Wikipedia, LibriVox) on Lumia 950. Heavy SPA sites (Reddit, Product Hunt) are useful as stress tests but may break on NiL.JS.

## Known Issues

- Source is AI-generated ("neuro-slop"), except the original UDAIE-A WebView code
- Not tested on any W10M device
- White screen on some sites (dzen.ru, ya.ru) — Smart Fallback handles most cases
- ES Modules runtime errors still being fixed
- Private fields (`#name`) not supported
- SVG→XAML rendering abandoned — complex graph/timeline deferred to SkiaSharp (v1.1+)
- OpenRouter model IDs change frequently — verify via API before use
- Some sites (Reddit, Medium) blocked by Cloudflare/captcha — cannot be fetched by Smart Fallback

## Credits

- [UDAIE-A/WEBVIEW](https://github.com/UDAIE-A/WEBVIEW) — Original WebView for Windows Phone 8.1
- [NiL.JS](https://github.com/nilproject/NiL.JS) — JavaScript engine

## Docs

See `/Doc` folder for development plans and session summaries.

## Contributing

**Calling all retro-computing enthusiasts!** If you still have a Lumia 950/1020 gathering dust, or you just love the idea of a browser that doesn't need 2 GB of Chromium to open a webpage — this project needs you.

- **Developers:** Fork, fix, PR. The codebase is messy but honest. Every line was fought for.
- **W10M testers:** Try it on your device, report what breaks. Your hardware is the real test bench.
- **CSS/JS nerds:** If you know why `calc(100% - 16px)` doesn't work here — you already know what to do.

## Issues & Bug Reports

Found a site that renders wrong? Crashed on your phone? Have a feature request?

→ **[Open an Issue](https://github.com/mediaexplorer74/MediaExplorer/Issues)**

Include: URL, what you expected, what you got. Screenshots help.

---

As is. No support. RnD only. DIY.

[m][e] June 20, 2026

![](/Images/footer.png)
