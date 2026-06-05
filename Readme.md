# MediaExplorer 0.42.5 - dev branch

![](/Images/logo.png)

## What Is This?

MediaExplorer is a hobby browser for Windows 10 Mobile (W10M, build 15063+) built **without** the system WebView or Chakra engine. It uses a custom HTML parser, CSS engine with selectors/cascade/flexbox, a JavaScript runtime powered by [NiL.JS](https://github.com/nilproject/NiL.JS), and a XAML-based renderer.

~35 files, ~20k+ lines of code. Pre-alpha. Museum-grade.

## Screenshots

![](/Images/sshot01.png)
![](/Images/sshot02.png)
![](/Images/sshot03.png)


## Features

- **Custom rendering engine** — HTML parser, CSS cascade, flexbox, XAML renderer
- **JavaScript** — NiL.JS runtime with ES Modules support (Vite bundles parse)
- **DevTools** — Console, DOM inspector, Network tab, Debug log
- **3 UI modes** — Hided (strip), Semi (expandable), Full (standard app bar)
- **3 render modes** — Full (JS+CSS), Rich (CSS, no JS), Poor (plain text)
- **Disk cache** — Resource caching with priority queue
- **MutationObserver** — Incremental re-render on DOM changes
- **Snapshot button** — Screenshot (single or full-page) saved to Pictures/MediaExplorer
- **Keyboard shortcuts** — Ctrl+L (focus URL), Ctrl+B (toggle bar)

## Status

- **Pre-alpha.**  All features are highly unfinished.
- **NiL.JS 2.6 Integration (netstandard2.0 → 1.4)** | **✅ DONE** (fully builds for W10M 15063)

This is a homemade browser engine — not production-ready, not intended to replace Edge or Chrome. It exists to prove that you don't need Chromium to render a webpage.

## Known Issues

- Source is AI-generated ("neuro-slop"), except the original UDAIE-A WebView code
- Not tested on any W10M device
- White screen on some sites (dzen.ru, ya.ru)
- ES Modules runtime errors still being fixed
- Private fields (`#name`) not supported

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

[m][e] June 4, 2026

![](/Images/footer.png)
