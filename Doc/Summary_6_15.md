# MediaExplorer — Summary 6.15

**Version**: 1.0.0.0 | **Session**: 6.15 | **Date**: 2026-06-12
**Platform**: W10M 15063+ (RS2) / x64 dev emulator
**Identity**: `MediaExplorerV1p0` | **FamilyName**: `MediaExplorerV1p0_5gyrq6psz227t`

---

## What was done

### 1. Fixed NiL.JS eval context isolation — the main blocker for card mode

**Root cause:** `HostWindow` (JavaScriptEngine.cs:2432) is a C# POCO with fixed properties. When NiL.JS evaluates `window.Kf = {entries:[...]}`, it silently fails because `HostWindow` has no `Kf` property. The entire data pipeline broke: `__storeData` was never called → `ExtractEntriesJson()` returned null → card mode never activated.

**Fix:** Replaced `window.*` with `globalThis.*` for all data injection/reading. `globalThis` is defined as `SafeEval("this")` which returns a native NiL.JS `ObjectInstance` (not a C# wrapper). Dynamic property assignment works correctly.

**Files changed:** `Engine/JavaScriptEngine.cs` — 8 edit sites:
- `__sysImport` data leak (line ~3494): `window.Kf=` → `globalThis.Kf=`
- `extractValue` function (line ~3512): Added `globalThis.` prefix search
- SafeEval injection (line ~3542): `window.Kf=` → `globalThis.Kf=`
- Graph builder code (line ~3559): All `window.*` → `globalThis.*`
- Verification code (line ~3924): All `window.*` → `globalThis.*`
- `RunScriptsAsync` data leak (line ~6118): `window.Kf=` → `globalThis.Kf=`
- Injected graph builder (line ~6143): All `window.*` → `globalThis.*`
- Fetch/XHR interceptors (lines ~5040, 5050, 5146): `window.__*` → `globalThis.__*`

### 2. Fixed JSON.stringify timeout — C#-only data extraction

**Root cause:** The graph builder's SafeEval call included `JSON.stringify(722 entries)` inside a 7-second timeout window. `JSON.stringify` on large NiL.JS arrays triggered the timeout, swallowing the `__storeData` call silently. Data existed in NiL.JS (`globalThis.__entries`) but never reached C#'s `_storedData`.

**Fix:** Added C#-only extraction path that bypasses NiL.JS entirely:
- After brace-counting extracts raw JS literals (kfRaw, wfRaw, cfRaw)
- `extractSubArray()` finds entries/collections/stories arrays inside the objects
- `quoteJsKeys()` converts JS object literals to valid JSON (quotes unquoted keys)
- Stores directly in static `_storedData` dictionary

**Log confirms:** `[DIAG:DATA] _storedData[__entries] set via C# extraction (253069B)`

### 3. Fixed entry image URLs

**Root cause:** `file` field is a slug like `"04_morph_wrist_mode"` without extension or path. Original code constructed wrong URLs.

**Fix:** Image URL pattern is `./images/archive/{file}.jpg`. Changed `BuildCardContent` to:
```csharp
new Uri("https://nokiadesignarchive.aalto.fi/images/archive/" + Uri.EscapeDataString(imageFile) + ".jpg")
```

### 4. Improved card navigation UI

**Before:** Invisible 60px transparent tap zones on left/right edges. No visible prev/next buttons.

**After:**
- **◀ ▶ buttons** at bottom — clearly visible, centered between arrows
- **Counter shows filtered context**: "1 / 116 (of 722)" when collection-filtered
- **☰ Browse** / **← All** toggle with collection name and count
- Card panel uses 3-row Grid layout (toolbar / card / nav bar)

---

## Test results

| Test | Result | Notes |
|------|--------|-------|
| Card mode activates | ✅ | 722 entries loaded from Nokia Design Archive |
| Entry images display | ✅ | Morph concept, Street Style photos load correctly |
| ◀ ▶ prev/next | ✅ | Buttons work on desktop |
| Swipe navigation | ✅ | Works on touch devices |
| Collection chip filter | ✅ | Filters to collection entries, counter updates |
| Browse → category index | ✅ | 33 collections grouped by theme |
| "All" restore | ✅ | Clears filter, returns to full 722 list |
| Data extraction | ✅ | 253KB entries, 33KB collections, 57KB stories |
| Entry URL interception | ✅ | `/entry/E0001` routes to card mode |

---

## Key learnings

1. **NiL.JS `globalThis` vs `window`**: `window` is a C# wrapper (`HostWindow`) that drops dynamic properties. `globalThis` is a native NiL.JS object. Always use `globalThis.*` for data injection.

2. **`_storedData` is static**: All `JavaScriptEngine` instances share the same dictionary. But `_nilInit()` calls `_storedData.Clear()` — so timing matters.

3. **SafeEval timeout (7s)**: `JSON.stringify` on large arrays (722 entries) inside a single SafeEval can trigger the timeout. Extract data in C# instead of relying on JS callbacks.

4. **Image URL pattern**: Nokia Design Archive uses `./images/archive/{file}.jpg` — discovered by analyzing the minified JS chunk's `Zf()` function.

---

## Files modified this session

| File | Changes |
|------|---------|
| `Engine/JavaScriptEngine.cs` | `window.*` → `globalThis.*` (8 sites); C# extraction helpers; removed `__storeData` from graph builder |
| `MainPage.xaml.cs` | Image URL pattern fix; card panel UI rewrite with ◀ ▶ nav bar |

---

## Next session (6.16)

- Test on real Lumia 950 device
- Fix: after collection filter, first card should jump to an entry unique to that collection (not same as before)
- Polish: card entrance/exit transitions, image preloading
