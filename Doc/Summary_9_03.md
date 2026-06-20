# Summary_9_03 — Automatic Rescue Path + Per-Site Rescue Preference

**Date:** June 20, 2026  
**Focus:** Make MediaExplorer react intelligently when local rendering fails, and start remembering rescue choices per site.

---

## What changed

## 1. Automatic Rescue Overlay
When a local NiL.JS render produces an empty or meaningless result, MediaExplorer no longer falls back silently in all cases.

Instead, it can now show a **rescue overlay** offering explicit choices:
- **AI Summary**
- **Open in Edge**
- **Use RemoteRender**
- **Try in POOR mode**

This uses the existing `MessageOverlay` and extends it with new action buttons.

### New buttons
- `ErrorRetryAi`
- `ErrorRetryEdge`
- `ErrorRetryRemote`

### Files
- `Src/MediaExplorer/MainPage.xaml`
- `Src/MediaExplorer/MainPage.xaml.cs`

---

## 2. Empty-render detection now branches by mode
In `Engine_RepaintReady(...)` the rescue logic is now split more intentionally:

### POOR mode
- still auto-triggers AI fallback immediately

### Normal local rendering
- if render looks empty / broken, app now shows **rescue choices** instead of forcing a silent fallback path

This is a major UX improvement, because the user can now decide how to rescue the page.

### File
- `Src/MediaExplorer/MainPage.xaml.cs`

---

## 3. Prefer RemoteRender for heavy sites
A new saved setting already existed from the previous step:
- `RemotePreferHeavySites`

Now it is wired into navigation logic.

### Behavior
If:
- site is recognized by `EngineRouter` as heavy / SPA-like
- and `RemotePreferHeavySites == true`

then MediaExplorer will route the site to **RemoteRender** instead of EdgeHTML.

This is the first real step toward automatic remote rescue.

### Files
- `Src/MediaExplorer/MainPage.xaml.cs`
- `Src/MediaExplorer/Engine/EngineRouter.cs`

---

## 4. Per-site rescue preference / engine memory
Rescue actions now start remembering engine choice per site.

### Implemented
When user chooses from rescue overlay:
- **Open in Edge** → site engine override saved as `EdgeHTML`
- **Use RemoteRender** → site engine override saved as `Remote`

This uses:
- `EngineRouter.SetSiteEngine(host, engine)`

### New helper
- `RememberRescueEngine(...)`

This means MediaExplorer begins to evolve from a one-shot recovery browser into a browser that learns rescue preferences host-by-host.

### File
- `Src/MediaExplorer/MainPage.xaml.cs`

---

## Why this matters
Before this session:
- broken local render often meant white screen, confused fallback, or manual experimentation

After this session:
- user gets explicit rescue choices
- heavy sites can prefer RemoteRender automatically
- rescue choices begin to persist per host

This is exactly the product direction defined in Plan 09:

> local when possible, AI when useful, remote when necessary.

---

## Build status
Build succeeded.

---

## Resulting maturity of Plan 09 v1.4 milestone
The following are now partially or fully real:
- automatic rescue overlay
- manual rescue options
- remote preference for heavy sites
- first version of per-site rescue memory

The next logical step is to deepen this into a more complete host-memory system and refine rescue heuristics.
