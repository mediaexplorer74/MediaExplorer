# Summary 2.14 — AutocompleteList Removal, AppBar Hit-Test Fix, Script/Style Text Suppression, box-sizing, background-image, visibility, float, white-space, [Rendered] removal

**Session date:** 2026-05-17
**Build:** 0.8.0.0 x86 Debug — ✅ 0 errors

---

## 1. AutocompleteList Removed

**Problem:** Невидимый `AutocompleteList` (ListView) перекрывал Omnibox и блокировал ввод URL. Добавлен другим ИИ в предыдущих сессиях, но не был согласован с автором проекта.

**Removed:**
- `MainPage.xaml:170` — `<ListView x:Name="AutocompleteList" .../>`
- `MainPage.xaml.cs` — подписка `AutocompleteList.ItemClick`, методы `Omnibox_TextChanged` и `AutocompleteList_ItemClick`

**Result:** Omnibox полностью доступен для ввода, App Bar больше не "двоит" и не блокирует.

---

## 2. AppBar "Magic" Hit-Test Fix

**Problem:** `BarStrip` (полоска-handle) всегда `IsHitTestVisible="True"` и рендерится поверх `BarContent` (последним в XAML). В развёрнутом состоянии 12-пиксельная полоска блокировала верхнюю часть Omnibox — ввод адреса не работал.

**Fix:**
- `ExpandBar()`: `BarStrip.IsHitTestVisible = false` (не блокирует Omnibox)
- `CollapseBar()`: `BarStrip.IsHitTestVisible = true` (работает как handle)
- `ApplyAppBarMode()`: режим "Full" → `BarStrip.IsHitTestVisible = false`, "Semi"/"Hided" collapsed → `true`

**Files:** `MainPage.xaml.cs` — `ExpandBar()`, `CollapseBar()`, `ApplyAppBarMode()`

---

## 3. Script/Style Text Suppression in Visual Render

**Problem:** Содержимое `<script>` и `<style>` тегов просачивалось в визуальное дерево как текст. На ya.ru рендерились CSS-правила (`.direct-close-block close-icon{color:#993355...`) и JSON-данные (`{"static":"2026-05-15-1127"...}`).

**Root cause:** `RenderNodeAsync()` рендерил текстовые узлы напрямую без проверки `ShouldSuppressTextNode()`. Эвристика существовала, но использовалась только в `GatherText()` для извлечения текста, не при визуальном рендеринге.

**Fix:**
```csharp
// DomBasicRenderer.cs:2226-2231
if (n.IsText)
{
    var txt = CollapseWs(n.Text);
    if (string.IsNullOrWhiteSpace(txt)) return null;
    if (ShouldSuppressTextNode(txt)) return null;  // ← NEW
    return new TextBlock { ... };
}
```

**Result:** CSS/JS-код из `<script>`/`<style>` больше не рендерится как видимый текст.

---

## 4. box-sizing: border-box Support

**Problem:** Без поддержки `box-sizing` все размеры разъезжаются. Современные сайты (ya.ru, DuckDuckGo) массово используют `box-sizing: border-box` — ширина/высота включают padding и border. XAML по умолчанию работает как `border-box`, но CSS-движок интерпретировал размеры как `content-box`.

**Implementation:**

| File | Change |
|------|--------|
| `CssComputed.cs` | Added `BoxSizing` property (`"content-box"`, `"border-box"`, `"padding-box"`) |
| `CssLoader.cs` | Parse `box-sizing` in cascade, set typed property |
| `DomBasicRenderer.cs` | `ApplyComputedLayout()` — adjusts Width/Height/Min/Max based on box-sizing |

**Logic:**
- `border-box`: Width/Height used as-is (XAML default matches this)
- `content-box`: Width/Height + padding + border = total XAML size

**Files changed:**
- `Engine/CssComputed.cs` — `BoxSizing` field
- `Engine/CssLoader.cs` — cascade parsing (~10 lines)
- `Engine/DomBasicRenderer.cs` — `ApplyComputedLayout()` rewrite (~20 lines)

---

## 5. background-image / url() Support

**Problem:** Фоновые изображения (иконки, баннеры, градиенты) не отображались. `TryMakeImageBrush` существовал, но использовал сырой URL без разрешения относительно base URI.

**Implementation:**
- `CssComputed.cs`: Added `BackgroundImageUrl`, `BackgroundRepeat`, `BackgroundPosition`, `BackgroundSize`
- `CssLoader.cs`: Parse `background-image` and shorthand `background` for `url()`, resolve URL via `ResolveUrlIfNeeded`
- `RendererStyles.cs`: `TryMakeImageBrush` now uses pre-resolved `css.BackgroundImageUrl`; added public `TryMakeImageBrushFromUrl()` for fallback in `Finish()`
- `DomBasicRenderer.cs`: `Finish()` adds background-image wrapper if `WrapWithBoxes` didn't wrap

**Files changed:**
- `Engine/CssComputed.cs` — 4 new fields
- `Engine/CssLoader.cs` — background-image parsing (~30 lines)
- `Engine/RendererStyles.cs` — `TryMakeImageBrush` refactor + `TryMakeImageBrushFromUrl()` (~60 lines)
- `Engine/DomBasicRenderer.cs` — `Finish()` fallback (~15 lines)

---

## 6. visibility: hidden Support

**Problem:** `visibility: hidden` не поддерживался. Элементы должны занимать место в layout, но быть невидимыми.

**Implementation:**
- `CssComputed.cs`: Added `Visibility` field
- `CssLoader.cs`: Parse `visibility` property
- `DomBasicRenderer.cs`: `ApplyComputedStyles()` — `hidden` → `Opacity = 0` (UWP has no `Visibility.Hidden`), `collapse` → `Visibility.Collapsed`

**Files changed:**
- `Engine/CssComputed.cs` — `Visibility` field
- `Engine/CssLoader.cs` — cascade parsing
- `Engine/DomBasicRenderer.cs` — `ApplyComputedStyles()` (~10 lines)

---

## 7. float: left/right Support

**Problem:** `float` не поддерживался. Элементы с `float: left/right` должны обтекаться текстом.

**Implementation:**
- `CssComputed.cs`: Added `Float` and `Clear` fields
- `CssLoader.cs`: Parse `float` and `clear` properties
- `DomBasicRenderer.cs`: `RenderGenericContainerAsync()` — wrap floated elements in Border with HorizontalAlignment; clear adds spacing

**Files changed:**
- `Engine/CssComputed.cs` — `Float`, `Clear` fields
- `Engine/CssLoader.cs` — cascade parsing
- `Engine/DomBasicRenderer.cs` — `RenderGenericContainerAsync()` (~20 lines)

---

## 8. white-space / text-overflow Support

**Problem:** `white-space: nowrap` и `text-overflow: ellipsis` не поддерживались. Текст всегда переносился.

**Implementation:**
- `CssComputed.cs`: Added `WhiteSpace` and `TextOverflow` fields
- `CssLoader.cs`: Parse both properties
- `DomBasicRenderer.cs`: `RenderNodeAsync()` — applies TextWrapping and TextTrimming based on parent's white-space/text-overflow

**Mapping:**
| CSS | XAML |
|-----|------|
| `white-space: nowrap` | `TextWrapping.NoWrap` |
| `white-space: pre` | `TextWrapping.NoWrap` |
| `white-space: pre-wrap` | `TextWrapping.Wrap` |
| `white-space: pre-line` | `TextWrapping.Wrap` |
| `text-overflow: ellipsis` + nowrap | `TextTrimming.CharacterEllipsis` |

**Files changed:**
- `Engine/CssComputed.cs` — 2 new fields
- `Engine/CssLoader.cs` — cascade parsing
- `Engine/DomBasicRenderer.cs` — `RenderNodeAsync()` (~30 lines)

---

## 9. [Rendered] Diagnostics Banner Removed

**Problem:** Баннер `[Rendered]` отображался в верхней части каждой страницы (debug-артефакт).

**Fix:** `CustomHtmlEngine.cs:1264` — `includeDiagnosticsBanner: true` → `false`

---

## Build Result

```
WEBVIEW -> bin\x86\Debug\WEBVIEW.exe
WEBVIEW -> AppPackages\WEBVIEW_0.8.0.0_Debug_Test\WEBVIEW_0.8.0.0_x86_Debug.appxbundle
```

**0 errors**, 6 pre-existing warnings (unused fields/events).

---

## Next (pending user testing)

1. **transform** — иконки, стрелки, чекбоксы
2. **calc() / vw / vh** — адаптивные размеры
3. **border-style в shorthand** — `border: 1px solid #ccc`
4. **letter-spacing / word-spacing** — типографика
5. **z-index** — stacking context для оверлеев

---

*Session 2.14 — 2026-05-17*
