# Summary_9_04 — Deeper Rescue Heuristics + User-Facing Rescue Memory Control

**Date:** June 20, 2026  
**Focus:** Make Automatic Rescue smarter by distinguishing failure types, and give the user a visible way to reset rescue memory.

---

## What changed

## 1. Failure reasons are now typed
Before this session, the browser mainly treated broken rendering as a binary state:
- rendered OK
- rendered empty/broken

Now `SmartFallbackRenderer` can classify the failure more specifically.

### New enum
- `RenderFailureReason.None`
- `RenderFailureReason.EmptyRender`
- `RenderFailureReason.BlockPage`
- `RenderFailureReason.CodeJunk`
- `RenderFailureReason.MinimalText`
- `RenderFailureReason.NetworkLike`

### New method
- `AnalyzeFailure(...)`

### File
- `Src/MediaExplorer/Engine/SmartFallbackRenderer.cs`

---

## 2. Rescue overlay message now depends on failure type
`MainPage` now uses the typed failure reason to choose a more helpful rescue message.

### Examples
- **BlockPage** → recommends RemoteRender / Edge first
- **CodeJunk** → recommends AI Summary / RemoteRender
- **MinimalText** → recommends AI / POOR / Remote
- **NetworkLike** → suggests network/access style failure with appropriate rescue hints

### New helper
- `ShowRescueOverlayForReason(...)`

### File
- `Src/MediaExplorer/MainPage.xaml.cs`

---

## 3. Rescue heuristics now route through richer diagnostics
In `Engine_RepaintReady(...)`:
- app now calls `AnalyzeFailure(...)`
- logs the exact detected reason
- opens a reason-aware rescue overlay instead of using one generic message

This gives much clearer rescue UX and much better diagnostics.

### File
- `Src/MediaExplorer/MainPage.xaml.cs`

---

## 4. User-facing control over rescue memory
A new visible action was added to the rescue overlay:
- **Forget saved rescue for this site**

This appears when the current host already has a remembered rescue preference.

### Why it matters
Previously, remembered rescue behavior was invisible once stored.
Now user can explicitly clear it and let MediaExplorer try again from a clean state.

### New button
- `ErrorForgetRescue`

### New handler
- `ErrorForgetRescue_Click(...)`

### Files
- `Src/MediaExplorer/MainPage.xaml`
- `Src/MediaExplorer/MainPage.xaml.cs`

---

## 5. Rescue memory now feels more like browser behavior, not hidden state
This session completes an important loop:

1. page fails
2. user chooses a rescue path
3. choice is remembered per host
4. app can apply it automatically later
5. user can now explicitly **forget** that remembered rescue preference

That turns rescue memory into a visible, controllable browser feature.

---

## Build status
Build succeeded.

---

## Product impact
This session pushes MediaExplorer closer to being a real adaptive browser:
- not only does it detect failure,
- it can explain the *kind* of failure,
- suggest a better rescue path,
- remember that path,
- and let the user revoke that memory later.

This is a big step toward a stable v1.4-style rescue layer and a stronger bridge into the later RemoteRender-first architecture.
