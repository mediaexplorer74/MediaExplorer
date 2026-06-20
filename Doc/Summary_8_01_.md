# Summary_8_01 — Session 20: Plan 8 — UI Refresh: AI Hub + Dashboard + Connectors

**Date**: June 14, 2026
**Target**: v1.5 — UI overhaul + AI Connector system

---

## What Was Done

### Phase 1: AppBar Redesign + AI Hub Overlay ✅
- AppBar reduced from 8 elements to 4: ← → Omnibox ≡ (Menu)
- Snapshot/Copy/AI buttons moved to AI Hub overlay
- Hub scrollable (ScrollViewer) with all 11 menu items
- HubBackButton + sub-panels for Favorites, History, AI Summary

### Phase 2: Start Dashboard + Speed Dial ✅
- Dashboard overlay with 4-column speed dial grid (6 default pins)
- Recent History list (last 10 entries)
- Add current URL as pin (+ button), long-press to remove
- Ctrl+Home keyboard shortcut, Hub → Home button
- Dashboard auto-hides on navigation

### Phase 3: History + Favorites System ✅
- History grouped by date (Today/Yesterday/This Week/Older)
- Timestamps on each history entry
- "Clear All History" button
- Favorites: "★ Add current page" button + ✕ remove per entry
- History: ✕ remove per entry
- LoadHistory limit increased to 100 entries

### Phase 4: AI Connector Presets ✅
- Settings → AI Connectors tab with 4 connectors: Rich/Poor/Asceti/Smart
- Per-connector: API Key, Model Family, Model ID, Enabled, Auto-format
- Active connector selector (dropdown)
- ConnectorStorage: save/load per connector in LocalSettings
- Smart preset: quality threshold + max attempts config
- OpenRouter model IDs updated (old models removed):
  - Rich: `openai/gpt-4o`
  - Poor: `mistralai/ministral-8b-2512`
  - Asceti: `google/gemma-4-26b-a4b-it:free`

### Smart Fallback Renderer ✅
- `SmartFallbackRenderer.cs` — detects empty/code-junk renders
- Detection heuristics:
  - <5 visible elements + <50 chars text
  - ISP block pages ("Network Error", "blocked", "verification")
  - Code junk: >30% code chars OR 3+ CSS/JS patterns
  - Verification/captcha pages (Cloudflare, "Please wait")
- HTTP fetch with HTML cleanup (strip script/style/svg/head)
- AI summary displayed in Hub panel (Reader Mode removed)
- Poor/Asceti mode: ALL sites trigger AI fallback automatically on every navigation
- Rich mode: only triggers on bad renders (code junk heuristic)
- Reddit: all reddit.com → JSON API fallback
- Auto-retry on 429 (rate limit) with retry-after detection
- Stale AI Summary fix: content cleared on navigation and Hub close
- Logging: `[DIAG:FALLBACK]` + `[DIAG:API]` prefixes for diagnostics

### Additional Fixes
- StatusBar moved to separate row (Row 3) below BottomBar — no longer overlaps Hub
- Reader Mode: copy button (📋) added to toolbar
- AI Summary: shows results in Hub sub-panel with ProgressSpinner (not Toast)
- Settings: removed "Show in AppBar" toggles (Screenshot/Copy moved to Hub)
- E-book mode label in Hub shows current mode (Rich/Poor/Asceti)
- Build: `AiConnectorPreset.cs` + `SmartFallbackRenderer.cs` + `ApiClient.cs` added to .csproj

---

## Key Discoveries

### OpenRouter Model IDs Change Frequently
- Old `mistralai/mistral-7b-instruct` → 404 (removed from OpenRouter)
- Current valid models: `ministral-8b-2512`, `ministral-3b-2512`, `ministral-14b-2512`
- Free models: `google/gemma-4-26b-a4b-it:free`, `nvidia/nemotron-*:free`, `qwen/qwen3-*:free`
- Always verify model IDs via `GET https://openrouter.ai/api/v1/models`

### ISP Block Pages Fool NiL.JS
- Blocked sites return small HTML pages (202 bytes) with "Network Error" text
- NiL.JS renders them as valid pages (6 elements > 5 threshold)
- Need keyword-based detection, not just element count

### Reddit Has Cloudflare Verification
- Both reddit.com and old.reddit.com trigger JS-based bot verification
- HTTP fetch gets "Please wait for verification" page
- JSON API (`.json` suffix) works for old.reddit.com but not new reddit.com

### Reading Mode Auto-Start Timing
- `RenderMode == "Poor"` auto-starts Reading Mode after first repaint
- Must use `_firstRepaintDone` flag (not `_suppressRepaintHandler`) to skip initial load

---

## Files Created/Modified

| File | Changes |
|------|---------|
| `Engine/AiConnectorPreset.cs` | **NEW** — ConnectorType enum, AiConnectorConfig, ConnectorStorage |
| `Engine/SmartFallbackRenderer.cs` | **NEW** — Empty/code-junk/ISP-block detection, AI chain |
| `Engine/OpenRouterClient.cs` | Added configurable model support |
| `MainPage.xaml` | Hub ScrollViewer, Dashboard overlay, Reader copy button, StatusBar row |
| `MainPage.xaml.cs` | Hub logic, Dashboard, Fallback, Reader copy, Reddit redirect |
| `SettingsPage.xaml` | AI Connectors tab, removed Show in AppBar |
| `SettingsPage.xaml.cs` | Connector load/save, removed old API key |
| `Doc/Plan_08.md` | Updated milestones |

---

## Current State

- **v1.5 release.** Phases 1-4 of Plan 8 complete.
- AI Hub with 11 menu items (scrollable)
- Dashboard with speed dial + recent history
- History grouped by date, Favorites with add/remove
- AI Connector system (Rich/Poor/Asceti/Smart) with per-connector API keys
- Smart Fallback: auto-detect broken renders → AI summary in Reader Mode
- Known issue: Some OpenRouter model IDs return 404 (models deprecated/renamed)

---

*End of Summary_8_01 — June 14, 2026*
