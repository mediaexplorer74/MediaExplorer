# Plan 09 — RemoteRender / Remote Web Streaming + Post-Plan-8 Cleanup

**Date:** June 20, 2026  
**Target versions:** v1.2.5 → v1.5  
**Core theme:** MediaExplorer must learn to survive the modern web by taking broken pages "on tow" through RemoteRender.

---

## Why Plan 09 Exists

Plan 08 significantly improved the product:
- Slim AppBar is in place
- AI Hub prototype is real
- Dashboard / Speed Dial is real
- History / Favorites groundwork exists
- AI Connector / Smart Fallback is much more implemented than originally expected

But one hard truth remains:

> **MediaExplorer still renders many real-world sites poorly or not at all.**

The UI is no longer the main bottleneck. The browser engine is.

That means the next plan should pivot from "more local UI polish" to:

1. **cleanup legacy overlaps after Plan 8**
2. **stabilize existing UX**
3. **elevate RemoteRender from prototype to real rescue engine**

---

## Product Vision

MediaExplorer should become a **hybrid retro-browser** with three survival layers:

### Layer 1 — Local Native Rendering
- NiL.JS + RenderTreeBuilder + XAML renderer
- fastest and most battery-friendly path
- ideal for simple / old / static sites

### Layer 2 — AI Fallback Interpretation
- for pages that fail to render meaningfully
- summarize, extract, and present page content
- useful for docs/articles/news when layout is broken

### Layer 3 — RemoteRender / Remote Web Streaming
- for heavy SPA / modern JS sites
- Playwright remotely loads and interacts with the real page
- MediaExplorer becomes a lightweight remote viewing + input client

**This third layer is the real strategic leap.**

---

## Goals of Plan 09

1. Remove or resolve UI duplication created during the transition from Plan 08
2. Stabilize Hub / Dashboard / History / Favorites UX
3. Turn RemoteRender into a practical browser rescue path
4. Allow per-site and automatic switching between local, AI, Edge, and remote engines
5. Make modern broken sites at least *usable*, even if not natively renderable

---

# Phase 1 — Cleanup After Plan 08

## 1.1 Reader Mode Decision
### Problem
Reader Mode and AI Summary currently overlap conceptually.

### Options
#### Option A — Remove Reader Mode entirely (recommended)
Keep:
- normal page content
- AI Summary
- E-book modes
- copy/share/screenshot tools

#### Option B — Keep Reader Mode, but redefine it
Reader Mode becomes:
- purely manual simplified text view
- no AI
- fast, local, deterministic

### Recommendation
For speed and clarity, choose **Option A** and delete Reader Mode fully.

### Tasks
- Remove `ReadingOverlay` from `MainPage.xaml`
- Remove `StartReadingMode()` routing from Hub
- Remove Reader-specific zoom UI if not reused elsewhere
- Route users to AI Summary / E-book mode instead

**Files:**
- `Src/MediaExplorer/MainPage.xaml`
- `Src/MediaExplorer/MainPage.xaml.cs`

---

## 1.2 Hub Cleanup / Dynamic Island Polish
### Problem
Hub works, but still feels like a functional prototype rather than a final mobile interaction system.

### Improvements
- consistent open/close animation
- swipe-down-to-close
- clearer main menu vs sub-panel transitions
- proper inline panel for AI Summary instead of temporary/legacy behavior
- reduce visual state confusion

### Desired states
1. **Collapsed** — hidden
2. **Main Hub** — menu of actions
3. **Sub-panel** — Favorites / History / AI Summary / future Remote controls
4. **Context mode** — small stateful panel for engine status / remote connected / fallback active

**Files:**
- `Src/MediaExplorer/MainPage.xaml`
- `Src/MediaExplorer/MainPage.xaml.cs`

---

## 1.3 Favorites / History UX Completion
### Current issue
Foundation exists, but polish is incomplete.

### Must-do
- Add explicit “Add current page to Favorites” action in Hub
- Add remove actions in Favorites list
- Add clear-all action in History
- Add stronger recent/history presentation
- Optional: date grouping in History

**Files:**
- `Src/MediaExplorer/MainPage.xaml.cs`
- maybe some XAML additions inside Hub

---

## 1.4 Dashboard Stabilization
### Goal
Make Dashboard the real mobile-friendly start/home experience.

### Improvements
- optional “Home” action in Hub
- clearer empty-state behavior
- more stable pin management
- better tile sizing on small screens
- make current page pinning obvious

**Files:**
- `Src/MediaExplorer/MainPage.xaml`
- `Src/MediaExplorer/MainPage.xaml.cs`

---

# Phase 2 — RemoteRender Productization (Core of Plan 09)

## 2.1 RemoteRender as a Real Engine Choice
### Current state
RemoteRenderer already exists, but behaves more like an experimental subsystem than a first-class engine.

### Goal
Make RemoteRender a selectable and understandable engine mode.

### Requirements
- show current engine in UI / status
- allow manual switch to Remote
- allow per-site engine preference
- preserve Auto / Local / Edge / Remote concept cleanly

**Files:**
- `Src/MediaExplorer/Engine/EngineRouter.cs`
- `Src/MediaExplorer/MainPage.xaml.cs`
- `Src/MediaExplorer/MainPage.xaml`

---

## 2.2 Remote Server Settings UI
### Problem
RemoteRender cannot become practical until server settings are user-visible.

### Add to Settings
New section under AI Connectors or separate “Remote Render” pivot:
- Enable RemoteRender
- Server URL (`ws://...` / `wss://...`)
- Wait time after page load
- Mobile viewport width / height
- Auto-reconnect toggle
- Use remote for known heavy sites toggle

### Optional later
- PIN / token auth
- trusted server list

**Files:**
- `Src/MediaExplorer/SettingsPage.xaml`
- `Src/MediaExplorer/SettingsPage.xaml.cs`

---

## 2.3 Remote Status / Remote Session UX
### Goal
User should always know when page is remote, local, AI or Edge-rendered.

### Additions
- status message: `Engine: RemoteRender`
- toast on connect/disconnect/fallback
- small badge in Hub: `Remote Connected`
- optional remote latency / last screenshot timestamp

### Future idea
A compact “Remote Session” sub-panel in Hub:
- reconnect
- refresh screenshot
- copy remote page text
- disconnect

---

## 2.4 Remote Screenshot Viewer
### Goal
The remote page must display as a usable visual surface.

### Requirements
- screenshot shown in `RemoteView`
- proper scaling for Lumia screens
- tap-to-click mapping
- scroll gesture mapping
- keyboard / input bridge where possible

### Current risks to solve
- coordinate mapping correctness
- screenshot update lag
- scroll feels jerky
- no typed URL / form interaction flow

**Files:**
- `Src/MediaExplorer/MainPage.xaml.cs`
- `Src/MediaExplorer/Engine/RemoteRenderer.cs`

---

## 2.5 Remote Input Routing
### Goal
RemoteRender should not only show the page — it must support real interaction.

### Input support to validate / harden
- tap/click
- scroll
- keyboard Enter
- text input into search boxes/forms
- navigation after click

### Important gap
Remote input UX may need a dedicated helper UI:
- tap a field → open local text prompt → send `type`
- Enter key → send `press`

---

# Phase 3 — Automatic Rescue Path

## 3.1 Detect Local Failure Better
### Trigger conditions
RemoteRender should be offered or auto-used when:
- white screen
- very low visible element count
- empty text output
- repeated repaint block / failed render
- known SPA host

### Action model
When local rendering fails:
1. show toast:
   - `Page failed to render locally.`
2. offer choices:
   - `Try AI Summary`
   - `Open in Edge`
   - `Use RemoteRender`

or in Auto mode:
- directly switch to preferred fallback path

---

## 3.2 RemoteRender as Auto Fallback for Known Sites
### Known candidates
- Reddit
- DuckDuckGo
- MDN
- Dzen
- other modern SPA / JS-heavy sites

### Strategy
- maintain known-heavy host list in `EngineRouter`
- default route:
  - Local first for simple sites
  - Remote preferred for SPA-heavy sites if enabled

---

## 3.3 Per-Site Engine Memory
### Goal
If user manually picks Remote for a site once, remember it.

### Behavior
- `example.com` → Remote
- `news.ycombinator.com` → Local
- `reddit.com` → Remote / Edge / AI fallback depending choice

Store this in LocalSettings or structured site-engine map.

---

# Phase 4 — Remote Web Streaming Maturity

## 4.1 Security / Trust
### Needed before serious use
- PIN code or token for server
- optional allowlist / trusted host check
- avoid exposing open unauthenticated browser endpoint

### Files
- `Src/RemoteRender/server.js`
- `Src/MediaExplorer/Engine/RemoteRenderer.cs`
- Settings UI

---

## 4.2 Streaming Evolution
### Current model
RemoteRender is basically:
- render page remotely
- send screenshot snapshots

### Better long-term model
- partial screenshot refresh
- faster interaction loop
- optional low-bandwidth mode
- maybe JPEG quality presets
- maybe text overlay extraction for accessibility

---

## 4.3 AI + Remote Combined Mode
This is a high-value future feature.

### Idea
When page is remote-rendered:
- also fetch remote page text
- run AI Summary on remote text
- allow “visual remote page + AI summary” together

This gives MediaExplorer a unique role:
- not only browser fallback
- but **AI-assisted remote rescue browser**

---

# Engine Strategy After Plan 09

## Final engine matrix
| Engine | Best for | Weakness |
|---|---|---|
| Local (NiL.JS/XAML) | old/static/simple sites | breaks on modern SPA |
| AI Summary | articles/docs when layout fails | not interactive |
| Edge | compatibility fallback | less custom / less controllable |
| RemoteRender | modern SPA / heavy JS | latency / setup |

This should become an intentional product design, not an accidental collection of modes.

---

# UI Additions Proposed in Plan 09

## Hub additions
- `Add to Favorites`
- `Home / Dashboard`
- `Remote Session`
- engine status badge
- maybe `Open in RemoteRender`

## Settings additions
- RemoteRender section
- Server URL
- Connect test button
- viewport settings
- auto fallback preferences
- per-site engine override management (later)

---

# Milestones

## v1.2.5 — Cleanup + Stabilization
1. Remove or finalize Reader Mode ✅
2. Finish Hub transitions ✅
3. Finish Favorites / History UX ✅
4. Stabilize Dashboard behavior ✅
5. Fix obvious repaint / overlay / white-screen regressions ⏳

## v1.3 — RemoteRender First-Class Prototype
1. Settings UI for RemoteRender ✅
2. manual connect + manual remote render ✅
3. screenshot display + tap/scroll interaction ✅
4. user-visible engine status ✅
5. remote session UX (connect / reconnect / disconnect / refresh shot) ✅

## v1.4 — Automatic Rescue Layer
1. detect local failure better ✅
2. offer RemoteRender / AI / Edge fallback ✅
3. per-site engine memory ✅ (Edge/Remote rescue choices)
4. auto-route known heavy sites ✅ (when RemotePreferHeavySites is enabled)
5. typed failure reasons ✅ (empty / block / code junk / minimal text / network-like)
6. user-facing rescue memory reset ✅

## v1.5 — Remote Web Streaming Beta
1. polished remote session UX ✅
2. security/PIN/token ✅ (first PIN-based gate)
3. better latency handling ✅ (session status + screenshot timing + remote text fetch)
4. AI + Remote combined workflows ✅ (AI summary over remote-fetched page text)

---

# Files to Create / Modify

| File | Phase | Changes |
|---|---|---|
| `Src/MediaExplorer/MainPage.xaml` | 1 | Hub polish, remove Reader overlay or refactor, add remote session UI elements |
| `Src/MediaExplorer/MainPage.xaml.cs` | 1 | cleanup legacy reader logic, favorites/history polish, dashboard polish |
| `Src/MediaExplorer/SettingsPage.xaml` | 2 | add RemoteRender settings section |
| `Src/MediaExplorer/SettingsPage.xaml.cs` | 2 | load/save remote settings |
| `Src/MediaExplorer/Engine/EngineRouter.cs` | 2/3 | better engine selection, known-heavy hosts, per-site routing |
| `Src/MediaExplorer/Engine/RemoteRenderer.cs` | 2/4 | connect/reconnect/input/session improvements |
| `Src/RemoteRender/server.js` | 2/4 | auth, streaming polish, protocol hardening |
| `Src/MediaExplorer/Engine/SmartFallbackRenderer.cs` | 3 | integrate remote option into fallback strategy |
| `Doc/Summary_8_01.md` | — | audit summary |
| `Doc/Plan_09.md` | — | this plan |

---

# Final Thesis of Plan 09

Plan 08 proved that MediaExplorer can become a compelling retro-mobile browser UI.

Plan 09 must prove that it can remain useful on the modern web.

And that means embracing a hybrid model:

> **render locally when possible, summarize when necessary, and stream remotely when the modern web becomes too heavy.**

This is not a retreat from the original vision.
It is how the original vision survives.

---

*End of Plan 09 — June 20, 2026*
