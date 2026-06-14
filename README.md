# MediaExplorer 1.0.10 — main branch

![](/Images/logo.png)

## What Is This?

MediaExplorer is a hobby browser for Windows 10 Mobile (W10M, build 15063+) built **without** the system WebView or Chakra engine. It uses a custom HTML parser, CSS engine with selectors/cascade/flexbox, a JavaScript runtime powered by [NiL.JS](https://github.com/nilproject/NiL.JS), and a XAML-based renderer.

**v1.0 is optimized for the [Nokia Design Archive](https://nokiadesignarchive.aalto.fi/)** — a museum website with 722+ entries, 33 collections, 230 stories, and 91 keywords documenting Nokia's design history. MediaExplorer extracts the archive data from the site's JavaScript bundle, renders it as interactive swipeable cards with images, collection chips, keyword tags, and links to the Aalto University repository.

~40 files, ~25k+ lines of code. v1.0 release.

## Screenshots

![](/Images/sshot01.png)
![](/Images/sshot02.png)
![](/Images/sshot03.png)
![](/Images/sshot04.png)

## Features
- **Custom rendering engine** — HTML parser, CSS cascade, flexbox, XAML renderer
- **JavaScript** — NiL.JS runtime with ES Modules support (Vite bundles parse; D3v4/v5 support)
- **DevTools** — Console, DOM inspector, Network tab, Debug log
- **3 UI modes** — Hided (strip), Semi (expandable), Full (standard app bar)
- **3 render modes** — Full (JS+CSS), Rich (CSS, no JS), Poor (plain text)
- **Disk cache** — Resource caching with priority queue
- **MutationObserver** — Incremental re-render on DOM changes
- **Snapshot button** — Screenshot (single or full-page) saved to Pictures/MediaExplorer
- **Keyboard shortcuts** — Ctrl+L (focus URL), Ctrl+B (toggle bar)
- **Smartphone card mode** — Auto-detects narrow viewport (<600px), shows archive content as swipeable cards with detail views, category index, and link routing
- **Nokia Design Archive integration** — Extracts __entries, __collections, __stories data from JS globals; renders as interactive cards with type badges, collection chips, and story references

## Status

- **v1.0 release.** Phases R (Retrench), S (Smartphone adaptivity), T (Text/Image/Link detail views) complete.
- **SVG→XAML bridge abandoned** — after 18+ sessions, the architectural mismatch proved unfixable on Win SDK 15063. Replaced with text/image/link rendering and card-based layout for narrow viewports.
- **Data extraction intact** — `__graphData` (755 nodes, 1647 links), `__entries` (722), `__stories` (230), `__collections` (33) available for future SkiaSharp renderer.

## Dev section (June 13, 2026)

### Key decisions
- **SVG→XAML bridge removed** — content doubling on scroll, architectural mismatch, too heavy for Lumia
- **Card mode** — transparent replacement for narrow viewports: swipeable cards with entry details, category index, collection filtering
- **Link routing** — internal entry URLs (/entry/E0001) → card mode; external URLs → system browser via Launcher
- **Data extraction** — preserved for future SkiaSharp Canvas2D renderer
- **General web rendering (v1.1)** — Custom RenderTreeBuilder pipeline for real websites. First successful rendering: Hacker News with full 30 news items, orange header, vote arrows, clickable links, footer

### Key files
`MainPage.xaml.cs` — card mode, navigation, link routing
`Engine/BrowserApi.cs` — ExtractEntriesJson/ExtractCollectionsJson/ExtractStoriesJson
`Engine/Core/RenderTreeBuilder.cs` — HTML→RenderObject tree, UA styles, HTML presentational attributes
`Engine/Core/RenderBox.cs` — Flex/block/inline layout engine
`Engine/Core/VirtualizingRenderer.cs` — Canvas-based virtualizing renderer with lazy images
`Engine/CssLoader.cs` — CSS cascade, selectors, media queries, pseudo-classes
`AGENTS.md` — automation loop commands

## Dev section END

This is a homemade browser engine — not production-ready, not intended to replace Edge or Chrome. It exists to prove that you don't need Chromium to render a webpage.

## Milestones

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
- White screen on some sites (dzen.ru, ya.ru)
- ES Modules runtime errors still being fixed
- Private fields (`#name`) not supported
- SVG→XAML rendering abandoned — complex graph/timeline deferred to SkiaSharp (v1.1+)

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

[m][e] June 14, 2026

![](/Images/footer.png)
