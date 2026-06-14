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

## Milestones

### v1.2 (next iteration)
1. **Phase 1**: Slim AppBar (← → Omnibox ≡) + AI Hub overlay with all features
2. **Phase 2**: Start Dashboard with speed dial + recent history

### v1.5 (later)
3. **Phase 3**: Full History/Favorites system with storage, sub-panels, management

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
| `Doc/Plan_08.md` | — | This plan |

---

*End of Plan 8 — June 14, 2026*
