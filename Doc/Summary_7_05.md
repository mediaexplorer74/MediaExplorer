# Summary 7.05 — Session 19 Code Changes

**Date:** June 14, 2026
**Focus:** E-book Modes (Rich/Poor/Asceti) + Markdown-to-HTML

---

## Overview

Two major features added:
1. **E-book Modes** — Replaced the old 3-mode render system (Full/Rich/Poor) with a cleaner E-book mode selector (Rich/Poor/Asceti). JS is now always enabled. JS toggle removed from settings.
2. **Markdown-to-HTML** — New `MarkdownRenderer.cs` converts markdown to styled HTML. Auto-detects `.md` URLs during navigation.

**Version bumped from v1.0 to v1.1** across all files (manifest, READMEs, Plan_07.md).

---

## Changes by File

### SettingsPage.xaml
- **Removed** `<ToggleSwitch x:Name="JsToggle" ...>` — JS toggle no longer in UI
- **Renamed** "Render Mode" → "E-book Mode"
- **Updated ComboBox items**:
  - "Full (CSS + JS + interactive)" → "Rich (full graphics, CSS + JS)"
  - "Rich (CSS, no JS, reading mode)" → "Poor (card/index style, minimal CSS)"
  - "Poor (plain text, no CSS/JS)" → "Asceti (no images, no CSS, pure text)"

### SettingsPage.xaml.cs
- **Removed** `JsToggle.Toggled` handler (lines ~33-36)
- **Removed** `JsToggle.IsOn` loading from `LoadSettings()` (lines ~132-134)
- **Updated** `RenderModeCombo.SelectionChanged` handler: `"Full"→0, "Rich"→1, "Poor"→2` → `"Rich"→0, "Poor"→1, "Asceti"→2`
- **Updated** `LoadRenderMode()`: default changed from `"Full"` to `"Rich"`, backward compat maps `"Full"` → `"Rich"`
- **Updated** `LoadSettings()`: mode index mapping `mode == "Full" ? 0 : mode == "Rich" ? 1 : 2` → `mode == "Rich" ? 0 : mode == "Poor" ? 1 : 2`

### MainPage.xaml.cs
- **Removed** `JsEnabled` property (was `_welcomeEngine.EnableJavaScript` getter/setter)
- **Updated** `ApplyRenderMode()`: added backward compat `"Full"` → `"Rich"` mapping

### Engine/CustomHtmlEngine.cs
- **Updated** `RenderModeType` enum: removed `Full`, renamed `Rich` → `Rich` (full JS), `Poor` → `Poor` (minimal CSS), added `Asceti` (no CSS/images)
  ```csharp
  public enum RenderModeType { Rich, Poor, Asceti }
  ```
- **Updated** `_renderMode` default: `RenderModeType.Full` → `RenderModeType.Rich`
- **Updated** `_renderModeString` default: `"Full"` → `"Rich"`
- **Updated** `RenderModeString` setter: `"Asceti"` → `RenderModeType.Asceti`, else → `RenderModeType.Rich`
- **Added** Asceti rendering block (before Poor mode): no CSS fetcher, no image loader, no JS
- **Removed** `richMode` variable and MiniRunner dead code path (old `Rich` mode was MiniRunner-only; now `Rich` = full JS)

### Engine/MarkdownRenderer.cs (NEW)
- **Pure line-by-line markdown parser** (no regex, netstandard1.4 compatible)
- **Supported syntax**: `#`-`######` headers, `**bold**`, `*italic*`, `~~strikethrough~~`, `` `code` ``, code blocks (```), `[links](url)`, `![images](url)`, `- `/* ` unordered lists, `1. ` ordered lists, `> ` blockquotes, `---`/`***`/`___` horizontal rules, `|` tables
- **Inline parsing**: nested bold/italic, nested links, recursive inline code
- **Styled HTML output**: Segoe UI, 720px max-width, clean typography, syntax-highlighted code blocks
- **Namespace**: `BrowserCore.Engine`

### Engine/BrowserApi.cs
- **Added** `BrowserCoreHelpers.IsMarkdownUrl(Uri)` — detects `.md`, `.markdown`, `.mdown`, `.mkd` extensions
- **Added** markdown interception in `NavigateInternalAsync()`: after `FetchTextAsync`, checks URL and renders via `MarkdownRenderer.RenderToHtml()`

### MediaExplorer.csproj
- **Added** `<Compile Include="Engine\MarkdownRenderer.cs" />`

---

## Version Bump (v1.0 → v1.1)

### Package.appxmanifest
- `Name="MediaExplorerV1p0"` → `Name="MediaExplorerV1p1"`
- `Version="1.0.100.0"` → `Version="1.1.0.0"`
- `DisplayName="MediaExplorer v1.0"` → `DisplayName="MediaExplorer v1.1"` (2 occurrences)

### README files (EN/RU/CN)
- Title: "MediaExplorer 1.0.100" → "MediaExplorer 1.1.0"
- Status section: "v1.0" → "v1.1"
- Features list: "3 render modes — Full (JS+CSS), Rich (CSS, no JS), Poor (plain text)" → "3 e-book modes — Rich (full graphics), Poor (card/index style), Asceti (pure text)"
- Dev section text: updated references from v1.0 to v1.1
- Milestones: added new v1.1.0 entry

### Plan_07.md
- Title: "Session 19 — E-book Modes + Markdown-to-HTML" added
- All v1.0 references updated to v1.1

---

## Build/Deploy Results

| Step | Status |
|------|--------|
| Build | ✅ Clean (warnings only) |
| Deploy | ✅ Package registered |
| Reddit card mode | ✅ 25 posts, navigation working |
| Settings page | ✅ E-book Mode with Rich/Poor/Asceti, JS toggle removed |
| Rich mode | ✅ Full graphics, CSS + JS |

---

## Key Design Decisions

1. **JS always enabled** — No user toggle. The `EnableJavaScript` property remains in code for programmatic control (e.g., `Rich` mode could disable it in the future), but users can't turn it off.

2. **Rich = full JS** (was "Full") — The old `Rich` mode was MiniRunner-only (no full JS). Now `Rich` means the complete rendering pipeline: NiL.JS + CSS + images.

3. **Poor = card/index style** — Reader stylesheet with minimal CSS. Hides images, nav, sidebar. Applies readable fonts. Card mode activates when `__entries` data exists.

4. **Asceti = pure text** — No CSS, no images, no JS. Raw HTML structure only. For old e-book reader experience.

5. **Backward compat** — Old `"Full"` setting maps to `"Rich"` in both `ApplyRenderMode()` and `LoadRenderMode()`.

6. **Markdown detected by URL** — `.md`/`.markdown`/`.mdown`/`.mkd` extensions trigger `MarkdownRenderer.RenderToHtml()` before the normal HTML rendering pipeline.

---

## Files Modified

| File | Lines Changed |
|------|--------------|
| `SettingsPage.xaml` | -2 (JS toggle), +3 (E-book Mode label + ComboBox items) |
| `SettingsPage.xaml.cs` | -8 (JsToggle), +12 (mode mapping, backward compat) |
| `MainPage.xaml.cs` | -5 (JsEnabled property), +5 (ApplyRenderMode backward compat) |
| `Engine/CustomHtmlEngine.cs` | +25 (Asceti block), -10 (MiniRunner dead code), ~15 (enum/defaults) |
| `Engine/MarkdownRenderer.cs` | **+230** (new file) |
| `Engine/BrowserApi.cs` | +15 (IsMarkdownUrl, markdown interception) |
| `MediaExplorer.csproj` | +1 (MarkdownRenderer.cs compile include) |
| `Package.appxmanifest` | +3 (name, version, display name) |
| `Readme.md` | ~10 (version, features, milestones) |
| `Readme_RU.md` | ~10 (version, features, milestones) |
| `Readme_CN.md` | ~10 (version, features, milestones) |
| `Doc/Plan_07.md` | +50 (Session 19 section) |
| `AGENTS.md` | ~10 (v1.1 state update) |
