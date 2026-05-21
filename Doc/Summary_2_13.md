# Summary 2.13 — White Screen Fixes, AppBar Modes, Settings Page Refactor

## 1. White Screen Root Cause Found & Fixed

**Root cause:** `MainPage_SizeChanged` вызывал `_browser.NavigateAsync()` после каждого рендера (300ms debounce) — второй проход очищал `ContentHost` и запускал повторный полный рендер, создавая белый экран.

### Fixed:
- **`MainPage_SizeChanged`** — удалён `NavigateAsync`. Размер окна не меняется в процессе сессии на эмуляторе/мобильном.
- **`_renderSequence` guard** — счётчик в `ResetContentHost()`, захватывается в `Engine_RepaintReady`. Устаревшие repaint от предыдущих навигаций игнорируются.
- **`Engine_RepaintReady`** — упрощён до всегда `Clear()` + `Add()` (без replace-логики, которая оставляла осиротевшие элементы).

**Log evidence:** двойной рендер исчез. После фикса в логе один проход, элемент добавлен в `ContentHost`.

## 2. OutlineBrush → OutlineColor (RPC_E_WRONG_THREAD)

**Проблема:** `SolidColorBrush` создавался в `CssLoader.CascadeIntoComputedStyles` (background thread) → `RPC_E_WRONG_THREAD`.

**Fix:**
- `CssComputed.cs`: `OutlineBrush: Brush` → `OutlineColor: Color?`
- `CssLoader.cs`: убран `new SolidColorBrush`, сохраняется `col.Value`
- `RendererStyles.WrapWithBoxes`: создаёт `SolidColorBrush` в UI-потоке

Аналогично почищены `Background`, `Foreground`, `BorderBrush` в `RenderTreeBuilder.BuildInternal`.

## 3. ResourceManager Cache Dedup

Все 3 точки вставки в memory-cache (`_textMap`, `_imgMap`, `_styleMap`) теперь проверяют существующий ключ и удаляют старый `LinkedListNode` перед вставкой. Фикс утечки `_textCap=32` / `_imgCap=16`.

## 4. Omnibox Placeholder

Добавлен `PlaceholderTextContentPresenter` в кастомный шаблон TextBox (был пропущен, placeholder никогда не показывался).

## 5. AppBar Touch Target

- `BarStrip` height: 4 → 12 (легче попасть пальцем/мышью)
- `BottomBar` collapsed: 20 → 24
- `BottomBar` expanded: 48 → 52

## 6. AppBar Modes (Full / Semi / Hided)

Новая настройка в Settings → UI:

| Mode | Collapsed | Behavior |
|------|-----------|----------|
| Full | 52px (всегда) | Не сворачивается |
| Semi | 24px (полоска) | Tap/swipe → 52px (pre-2.13 behavior) |
| Hided | 6px (узкая полоска) | Tap/swipe → 52px |

- Хранится в `ApplicationData.LocalSettings["AppBarMode"]`
- `CollapseBar()` проверяет `_appBarMode`: Full → no-op, Hided → 6px, Semi → 24px
- `ExpandBar()` всегда → 52px, независимо от режима

## 7. Settings Page Refactor

**Before:** Overlay-панель в MainPage.xaml (SettingsOverlay + AboutOverlay) с кнопками.

**After:** Отдельная `SettingsPage.xaml` + `.xaml.cs` (полноэкранный Page):

```
SettingsPage (Frame.Navigate)
  ├── Header: [Back] button + title
  └── Pivot:
       ├── General: JS toggle, Home Page
       ├── UI: AppBar mode ComboBox
       ├── Advanced: API Key, Clear Cache
       └── About: version info
```

- `MainPage.SettingsButton_Click` → `Frame.Navigate(typeof(SettingsPage))`
- Hardware Back button + software Back button
- Настройки сохраняются при выходе из страницы (OnNavigatedFrom)
- `MainPage.Current` exposes: `JsEnabled`, `ClearResourceCache()`, `ApplyAppBarMode()`, `StartReadingMode()`

**Cleaned up:** удалены `SettingsOverlay`, `AboutOverlay`, `ReadingModeButton`, `CloseSettingsButton`, `AboutButton` из MainPage.xaml + code-behind.

## 8. Diagnostics

Во всех стадиях пайплайна добавлены `[DIAG]`:

| Stage | Что логирует |
|-------|-------------|
| `MainPage.NavigateAsync` | seq, address |
| `ResetContentHost` | seq, children count after clear |
| `Engine_RepaintReady` | seq match, element size/type, add result |
| `RenderAsync` | Phase1(parse)/Phase2(CSS)/Phase3(JS)/Phase4(build) markers |
| `BuildVisualTreeAsync` | Step1(RenderTreeBuilder) / Step2(Layout) / Step3(Paint) markers |
| `CustomHtmlEngine` fetch/render/repaint | Fetch done, render start/end, repaint fire |
| `RenderTreeBuilder.Build` | Box/text/filtered counts |
| `VirtualizingRenderer` constructor | Canvas size, root children count |

## Files Changed

| File | Change |
|------|--------|
| `MainPage.xaml` | BarStrip=12, BottomBar sizes, placeholder template. Removed SettingsOverlay + AboutOverlay. |
| `MainPage.xaml.cs` | `_appBarMode`, `ApplyAppBarMode()`, CollapseBar mode check, Settings→Frame.Navigate, public JsEnabled/ClearResourceCache/StartReadingMode. Removed old overlay handlers. |
| `SettingsPage.xaml` | **NEW** — Pivot page with 4 tabs |
| `SettingsPage.xaml.cs` | **NEW** — settings load/save, back nav |
| `Engine/CssComputed.cs` | `OutlineBrush`→`OutlineColor` (Color?) |
| `Engine/CssLoader.cs` | No more SolidColorBrush creation |
| `Engine/RendererStyles.cs` | Creates Brush from OutlineColor in UI thread |
| `Engine/ResourceManager.cs` | Cache key dedup in 3 insertion sites |
| `Engine/CustomHtmlEngine.cs` | [DIAG] markers throughout |
| `Engine/Core/RenderTreeBuilder.cs` | Build→BuildInternal wrapper with diagnostics |
| `Engine/Core/VirtualizingRenderer.cs` | [DIAG] in constructor |
| `WEBVIEW.csproj` | Added SettingsPage.xaml + .xaml.cs |
| `Doc/Plan_02.md` | Updated |

## Build

- Visual Studio: 0 errors (expected)
- Console BuildTools: missing UWP XAML targets (unchanged)

## Next

- User to test white screen fix with dzen.ru / ya.ru
- Full diagnostics logging to confirm single render pass
- Remove `WEBVIEW/Doc/` duplicate docs → consolidate in `Doc/`
