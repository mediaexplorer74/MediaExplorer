# Plan 8 — UI Refresh: AI Hub + Start Dashboard + History/Favorites

**Date:** June 14, 2026
**Target versions:** v1.2 (Phase 1-2), v1.5 (Phase 3)
**Design inspiration:** Kiwi Browser, Via Browser, Firefox Focus, Samsung Internet

---

## Problem Statement

Current AppBar has **8 elements** crammed into one row: Back, Forward, Omnibox, Go, AI, Settings, Snapshot, Copy. On a 480px Lumia screen this is cluttered. Secondary features (History, Favorites, Reading Mode, AI Summary, E-book modes) are either hidden in Settings or triggered by obscure gestures.

**Goal:** Minimalist bottom bar (3-4 elements max). All secondary features accessible via a single "AI Hub" overlay. Start page with speed dial for pinned sites.

---

## UI Vision

### Current AppBar (v1.1)
```
[←][→][  Search or enter URL  ][▶][🤖][⚙][📷][📋]  ← 8 elements, cluttered
```

### Target AppBar (v1.2)
```
[←][→][  Search or enter URL  ][≡]  ← 4 elements, clean
```
- `≡` = "Menu" button → opens AI Hub overlay
- Snapshot/Copy move to AI Hub
- Go button removed (Enter key on keyboard = Go)

### AI Hub Overlay (new)
```
┌──────────────────────────────┐
│  AI Hub                  [✕] │
├──────────────────────────────┤
│  ⭐ Favorites                │
│  📜 History                  │
│  📖 Reading Mode + AI Summary│
│  📷 Screenshot               │
│  📋 Copy Text                │
│  🔄 E-book Mode (Rich/Poor/Asceti) │
│  🛠 DevTools                 │
│  ⚙ Settings                  │
└──────────────────────────────┘
```
Full-screen dark overlay, list-style menu. Tap item → action or sub-panel. Tap ✕ or swipe down → close.

### Start Dashboard (new)
```
┌──────────────────────────────┐
│  MediaExplorer v1.2          │
│  ┌────┐ ┌────┐ ┌────┐ ┌────┐│
│  │ Wiki│ │HN  │ │ 4pda│ │Red││
│  └────┘ └────┘ └────┘ └────┘│
│  ┌────┐ ┌────┐ ┌────┐ ┌────┐│
│  │ MDN │ │Arch│ │ ...│ │ + ││
│  └────┘ └────┘ └────┘ └────┘│
│  Recently visited:           │
│  • reddit.com/r/nba          │
│  • 4pda.to/forum             │
│  • wikipedia.org             │
└──────────────────────────────┘
```
Shown when no URL is loaded or on "Home" tap. Pinned sites (configurable) + recent history.

---

## Phase 1: AppBar Redesign + AI Hub Overlay (v1.2)

### 1.1 Slim Down AppBar
- **Remove** Go button (Enter key = Go)
- **Remove** Snapshot button (move to AI Hub)
- **Remove** Copy button (move to AI Hub)
- **Remove** AI button (replaced by Menu/Hub button)
- **Rename** AI button → Menu button (`≡` hamburger glyph)
- **Keep**: Back, Forward, Omnibox, Menu(≡)
- **Result**: 4 elements, clean look

**Files:** `MainPage.xaml` (Grid.ColumnDefinitions 8→4, remove buttons), `MainPage.xaml.cs` (remove click handlers for removed buttons, keep as methods)

### 1.2 AI Hub Overlay
New full-screen overlay triggered by `≡` button.

**Content (vertical list):**
| Icon | Label | Action |
|------|-------|--------|
| ⭐ | Favorites | Open Favorites sub-panel |
| 📜 | History | Open History sub-panel |
| 📖 | Reading Mode | Toggle reading mode (existing `StartReadingMode`) |
| 🤖 | AI Summary | Open AI summary panel (existing AiOverlay) |
| 📷 | Screenshot | Take screenshot (existing `SnapshotButton_Click`) |
| 📋 | Copy Text | Copy page text (existing `CopyButton_Click`) |
| 🔄 | E-book Mode | Cycle Rich→Poor→Asceti (or open picker) |
| 🛠 | DevTools | Toggle DevTools panel |
| ⚙ | Settings | Navigate to SettingsPage |

**Implementation:**
- New XAML overlay: `HubOverlay` Grid (similar to AiOverlay but full-height list)
- New code: `HubOpenButton_Click`, `HubCloseButton_Click`, `HubItem_Click`
- Swipe-down-to-close gesture on the overlay
- Dark semi-transparent background (#E6000000)

**Files:** `MainPage.xaml` (new overlay), `MainPage.xaml.cs` (hub logic)

### 1.3 Go Button → Enter Key
- Omnibox `KeyDown` handler already exists (`Omnibox_KeyDown`)
- Confirm Enter key triggers navigation (should already work)
- Remove `GoButton` from XAML and code

---

## Phase 2: Start Dashboard + Speed Dial (v1.2)

### 2.1 Start Dashboard
Shown when:
- App first launches (no home page set)
- User taps a "Home" button (add to AI Hub or long-press Back)
- Current URL is cleared

**Content:**
- App title/version
- **Speed Dial grid** (4 columns): 8 pinned site tiles with icon + label
- **Recent History** list: last 10 visited URLs

### 2.2 Speed Dial (Pinned Sites)
- **Default pins**: Wikipedia, Hacker News, 4pda.to, Reddit, Archive.org, MDN
- **User can**: long-press to remove, tap "+" to add current URL
- **Storage**: `LocalSettings.Values["SpeedDial"]` as JSON array of `{url, title, icon}`

### 2.3 Tile Design
Each tile:
```
┌─────────┐
│   🌐    │  ← favicon or first letter
│  Label  │  ← site name (truncated)
└─────────┘
```
- 80x80px tiles with rounded corners
- Tap → navigate to URL
- Long-press → context menu (Remove, Edit)

### 2.4 Recent History (Read-only preview)
- Last 10 visited URLs from navigation history
- Shown below speed dial
- Tap → navigate

**Files:** `MainPage.xaml` (new Dashboard overlay), `MainPage.xaml.cs` (dashboard logic, speed dial storage)

---

## Phase 3: History + Favorites System (v1.5)

### 3.1 History
- **Auto-record** every navigation URL + timestamp + title
- **Storage**: `LocalSettings` or local SQLite file (if >100 entries)
- **View**: Scrollable list in AI Hub sub-panel
  - Grouped by date (Today, Yesterday, This Week, Older)
  - Each entry: title, URL, timestamp
  - Swipe-left to delete
  - Tap → navigate
- **Clear all** button in History panel header

### 3.2 Favorites (Bookmarks)
- **Add**: Star icon in AI Hub → "Add to Favorites" (current page)
- **View**: Scrollable list in AI Hub sub-panel
  - Each entry: title, URL
  - Swipe-left to remove
  - Tap → navigate
- **Storage**: `LocalSettings.Values["Favorites"]` as JSON array

### 3.3 History/Favorites Sub-panels
Both open as child overlays within the AI Hub:
```
AI Hub → [⭐ Favorites] → Full-screen list with back arrow
AI Hub → [📜 History] → Full-screen list with back arrow
```
Navigation: Hub button → Hub overlay → tap item → sub-panel → tap entry → navigate + close all

---

## Technical Notes

### Storage Strategy
- **Speed Dial**: `LocalSettings["SpeedDial"]` — small, rarely changes
- **History**: `LocalSettings["History"]` — append-only, prune at 500 entries
- **Favorites**: `LocalSettings["Favorites"]` — small, user-managed

### XAML Structure (v1.2)
```
Grid (RootGrid)
├── Row 0: DevToolsPanel (toggleable)
├── Row 1: ContentArea
├── Row 1: LoadProgressBar
├── Row 1: HubOverlay (NEW — full-screen, toggleable)
│   ├── Header: "AI Hub" + Close button
│   └── List: Favorites, History, Reading, AI, Screenshot, Copy, E-book, DevTools, Settings
├── Row 1: AiOverlay (existing)
├── Row 1: ReadingOverlay (existing)
├── Row 1: LoadingOverlay (existing)
├── Row 1: MessageOverlay (existing)
├── Row 1: ToastOverlay (existing)
├── Row 2: BottomBar (slim: ← → Omnibox ≡)
└── Row 2: StatusBar
```

### AppBar Column Mapping (v1.2)
| Column | Old | New |
|--------|-----|-----|
| 0 | Back | Back |
| 1 | Forward | Forward |
| 2 | Omnibox | Omnibox |
| 3 | Go | ~~removed~~ |
| 4 | AI | ~~removed~~ (→ Hub) |
| 5 | Settings | ~~removed~~ (→ Hub) |
| 6 | Snapshot | ~~removed~~ (→ Hub) |
| 7 | Copy | ~~removed~~ (→ Hub) |
| — | — | Menu(≡) (new column 3) |

---

## Phase 4: AI Connector Presets — Smart Fallback Rendering (v1.5)

### Problem
NiL.JS can't render many modern sites (React SPAs, heavy JS, complex APIs). User sees white screen. Current AI Summary only works on pages that **already rendered** — it summarizes visible content. But what about pages that render **nothing**?

### Core Idea
Repurpose E-book Modes (Rich/Poor/Asceti) as **AI Connector Tiers** — different levels of AI-powered page analysis, triggered automatically when rendering fails.

### Architecture
```
Page load → NiL.JS renders → success? 
  ├─ YES → show page (optionally enhance with AI Summary)
  └─ NO (white screen / empty DOM)
       → auto-trigger AI Connector based on selected preset:
            ┌──────────────────────────────────────────────┐
            │  Preset    │ Source         │ Cost    │ What │
            ├──────────────────────────────────────────────┤
            │  Rich      │ OpenRouter     │ $$$     │ Full │
            │            │ (Claude/GPT)   │         │ LLM  │
            ├──────────────────────────────────────────────┤
            │  Poor      │ OpenRouter     │ $       │ Basic│
            │            │ (small models) │         │ fetch│
            ├──────────────────────────────────────────────┤
            │  Asceti    │ OpenRouter     │ Free    │ Free │
            │            │ (free models)  │         │ model│
            ├──────────────────────────────────────────────┤
            │  Smart     │ Auto-chain     │ Varies  │ Try  │
            │            │ Asceti→Poor→Rich│        │ best │
            └──────────────────────────────────────────────┘
```

### Preset Details

#### Rich (OpenRouter — Full LLM)
- **API**: OpenRouter (Claude 3.5 Sonnet, GPT-4o, etc.)
- **Flow**: Fetch page HTML → extract text/structure → send to LLM with prompt "Describe this page content, structure, links"
- **Cost**: ~$0.01-0.05 per page (depends on model + page size)
- **Quality**: Excellent — full understanding of page layout, links, images
- **Use case**: Complex sites worth paying for (documentation, articles)

#### Poor (OpenRouter — Cheap Model)
- **API**: OpenRouter (Phi-3, Gemini Flash, etc.)
- **Flow**: Fetch → basic extraction → lightweight LLM summary
- **Cost**: ~$0.001-0.005 per page
- **Quality**: Good text summary, may miss layout nuance
- **Use case**: Quick scan of articles, news

#### Asceti (Free OpenRouter Model)
- **API**: OpenRouter free tier (e.g. `mistralai/mistral-7b-instruct:free`, `google/gemma-2-9b-it:free`)
- **Flow**: Fetch → extract → free LLM summary
- **Cost**: Free (rate-limited)
- **Quality**: Basic — small model, limited context
- **Use case**: Offline-style free fallback, no API budget

#### Smart (Auto-Chain)
- **Flow**: Try Asceti (free) first → if quality threshold not met → try Poor → if still bad → try Rich
- **Threshold**: Based on response length, keyword coverage, confidence score
- **Cost**: Varies — usually Asceti or Poor, only escalates when needed
- **Use case**: Default for most users — best quality at minimal cost

### Detection: "Page Failed to Render"
When to trigger AI fallback:
- DOM has < 5 visible elements after render
- Body is empty or contains only `<script>` tags
- All text content < 50 characters
- RenderTreeBuilder produced 0 canvas children
- Timeout: 10s without meaningful render

### UI Integration
- **Hub → E-book Mode**: cycles Rich/Poor/Asceti/Smart (existing cycle logic)
- **Smart indicator**: Hub shows "Smart" badge when auto-chain is active
- **Fallback toast**: "Page couldn't render. Using [preset] AI connector..."
- **Settings → AI Connector**: dropdown to configure default preset + API key

### Implementation Files

| File | Changes |
|------|---------|
| `Engine/AiConnectorPreset.cs` | **NEW** — enum (Rich/Poor/Asceti/Smart), preset configs |
| `Engine/SmartFallbackRenderer.cs` | **NEW** — render failure detection + AI chain logic |
| `Engine/OpenRouterClient.cs` | Extend with preset-aware prompts (brief/detailed) |
| `MainPage.xaml.cs` | Hook into RepaintReady: detect empty render → trigger fallback |
| `MainPage.xaml` | Hub E-book label shows current preset + Smart badge |
| `SettingsPage.xaml` | AI Connector section: preset selector + API key |

---

## Milestones

### v1.5 (current — Session 20, June 14, 2026)
1. **Phase 1**: Slim AppBar (← → Omnibox ≡) + AI Hub overlay with all features ✅
2. **Phase 2**: Start Dashboard with speed dial + recent history ✅
3. **Phase 3**: Full History/Favorites system with storage, sub-panels, management ✅
4. **Phase 4**: AI Connector Presets — Settings UI + editable model family/ID ✅
5. **Smart Fallback Renderer** — Poor/Asceti: auto AI Summary on every nav; Rich: only on bad render ✅
6. **Reader Mode removed** — replaced by AI Summary in Hub panel ✅
7. **Stale AI Summary fix** — Hub content cleared on navigation/close ✅

### v2.0 (long-term)
6. **Phase 5**: Dzen.ru OAuth2 + authenticated API access

---

## Files to Create/Modify

| File | Phase | Changes |
|------|-------|---------|
| `MainPage.xaml` | 1 | Remove Go/AI/Snapshot/Copy buttons, add Menu(≡) button, add HubOverlay XAML |
| `MainPage.xaml.cs` | 1 | Hub overlay logic, move button handlers to hub, remove GoButton_Click |
| `MainPage.xaml` | 2 | Add Dashboard overlay with SpeedDial grid + Recent list |
| `MainPage.xaml.cs` | 2 | Dashboard logic, speed dial storage, tile tap handlers |
| `MainPage.xaml.cs` | 3 | History recording, Favorites CRUD, sub-panel navigation |
| `SettingsPage.xaml` | 1 | Remove Snapshot/Copy toggles (moved to Hub) |
| `Engine/AiConnectorPreset.cs` | 4 | **NEW** — preset enum + config (Rich/Poor/Asceti/Smart) |
| `Engine/SmartFallbackRenderer.cs` | 4 | **NEW** — failure detection + OpenRouter chain |
| `Engine/OpenRouterClient.cs` | 4 | Preset-aware prompts + free model support |
| `MainPage.xaml.cs` | 4 | Fallback trigger on empty render |
| `MainPage.xaml` | 4 | Smart badge in Hub |
| `SettingsPage.xaml` | 4 | AI Connector section |
| `Doc/Plan_08.md` | — | This plan |

---

*End of Plan 8 — June 14, 2026*
