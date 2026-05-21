
# Summary 3.15 — UI Polish: Toast Notifications, Navigation Fix, Status Bar Toggle

**Session date:** 2026-05-21  
**Build:** MediaExplorer.sln Release x64 — ✅ 0 errors  
**Test result:** Nokia Archive + ya.ru navigation working, Toast notifications visible

---

## 1. Changes Applied

### Fix 1: Toast Notification System
**Problem:** No visual feedback for user actions (Snapshot, Copy). Status bar messages too subtle.

**Files changed:**
- `MainPage.xaml` — Added `ToastOverlay` Grid with `ToastBar` Border (fade animation, auto-hide)
- `MainPage.xaml.cs` — Added `ShowToast()` method with `DispatcherTimer`, `HideToast()` with Storyboard fade-out
- Default duration: **5 seconds**, positioned 60px above App Bar

**Toast triggers added:**
- Snapshot: `📷 Snapshot saved: <filename>` (5s)
- Copy: `📋 Copied <N> characters to clipboard` (5s)

### Fix 2: ErrorOverlay → MessageOverlay (Rename)
**Problem:** `ErrorOverlay` name was too specific — couldn't be reused for non-error messages.

**Files changed:**
- `MainPage.xaml` — Renamed `ErrorOverlay` → `MessageOverlay`, `ErrorOverlayMessage` → `MessageText`, `ErrorOverlayClose` → `MessageClose`, added `MessageOverlayIcon` and `MessageOverlayTitle`
- `MainPage.xaml.cs` — Renamed `ShowErrorOverlay` → `ShowMessageOverlay`, `HideErrorOverlay` → `HideMessageOverlay`, `ErrorOverlayClose_Click` → `MessageClose_Click`, `_errorOverlayVisible` → `_messageOverlayVisible`
- `ShowMessageOverlay` now accepts `title`, `icon` (Symbol enum), and `isError` (color) parameters

### Fix 3: Status Bar Toggle in Settings
**Problem:** Status bar always visible, no way to hide it for cleaner view.

**Files changed:**
- `SettingsPage.xaml` — Added `StatusBarToggle` ToggleSwitch in Advanced section
- `SettingsPage.xaml.cs` — Added `LoadStatusBarVisible()` / `SaveStatusBarVisible()` methods, wired `Toggled` event
- `MainPage.xaml.cs` — Added `ApplyStatusBar()` method, called on load and settings change
- `MainPage.xaml` — Added `x:Name="StatusBarBorder"` to status bar Border
- Setting key: `StatusBarVisible` (default: true)

### Fix 4: Navigation History Fix (Back/Forward buttons)
**Problem:** Back button didn't work. `GoBack()` called `NavigateAsync()` which called `AddHistory()` again, adding the URL to the end of the stack and breaking navigation.

**Files changed:**
- `BrowserApi.cs` — Added `addToHistory` parameter to `NavigateAsync(string url, bool addToHistory)` and `NavigateInternalAsync(Uri uri, bool addToHistory)`
- `GoBack()` / `GoForward()` now pass `addToHistory: false`
- Reordered: `AddHistory(uri)` now called **BEFORE** `UpdateState(uri)` so `RaiseNavigated` fires with correct history state

### Fix 5: ModuleLoader Log Cleanup
**Problem:** `ResolveModule failed` log message looked alarming for expected Vite feature detection.

**Files changed:**
- `ModuleLoader.cs` — Special case for `spec == "_"`: logs `import("_") — Vite feature detection (expected to fail)` and `import("_") skipped — Vite feature detection` instead of generic error messages

### Fix 6: document.querySelectorAll (from Session 3.14)
**Problem:** `document.querySelectorAll is not a function` — `HostDocument` class didn't implement this method.

**Files changed:**
- `JavaScriptEngine.cs` — Added `querySelectorAll(string selector)` and `MatchesSelector(LiteElement el, string selector)` to `HostDocument` class
- Supports: tag selector (`link`), attribute selector (`link[rel="modulepreload"]`)

---

## 2. Test Results

| Test | Result |
|------|--------|
| Toast duration (5s) | ✅ OK — readable |
| Toast position (above App Bar) | ✅ OK — visible even with DevTools open |
| Status Bar toggle in Settings | ✅ OK — persists across sessions |
| Navigation Back (Nokia → ya.ru → Back) | ✅ OK — returns to Nokia Archive |
| Navigation Forward | ✅ OK — `Back=False Forward=True` correct |
| Module logs for `import("_")` | ✅ OK — no alarming "failed" messages |
| `document.querySelectorAll` | ✅ OK — Vite modulepreload polyfill works |

---

## 3. Remaining Issues

| Issue | Priority | Notes |
|-------|----------|-------|
| `TypeError: Type "<_nilInit>b__236_5" can not be created with new keyword` | 🟡 | NiL.JS runtime error in Vite bundle — anonymous lambda type |
| Many `InvalidOperationException` in NiL.JS.dll | 🟢 | Expected — JS engine catching errors in Vite bundle |
| `FileNotFoundException` in ResourceManager | 🟢 | Expected — missing resources, handled gracefully |

---

## 4. Files Changed Summary

| File | Change |
|------|--------|
| `MainPage.xaml` | ToastOverlay, MessageOverlay rename, StatusBarBorder name |
| `MainPage.xaml.cs` | ShowToast, ShowMessageOverlay, ApplyStatusBar, _messageOverlayVisible |
| `SettingsPage.xaml` | StatusBarToggle |
| `SettingsPage.xaml.cs` | Load/Save StatusBarVisible |
| `BrowserApi.cs` | addToHistory parameter, AddHistory before UpdateState |
| `ModuleLoader.cs` | Vite feature detection log messages |
| `JavaScriptEngine.cs` | querySelectorAll + MatchesSelector in HostDocument |

---

*Session 3.15 — 2026-05-21*
