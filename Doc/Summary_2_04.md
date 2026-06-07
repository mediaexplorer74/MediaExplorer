# Summary 2.9 — Phase 9: CSS Property Expansion (partial)

## Что сделано

### `border-spacing` (таблицы)
- Добавлены поля `BorderSpacing` (горизонталь/одно значение) и `BorderSpacingVertical` (двухзначный синтаксис) в `CssComputed.cs`
- Парсинг в `CssLoader.cs`: чтение `border-spacing` из CSS, разбивка 1 или 2 значений через `TryPx`
- Рендер в `TableRenderer.cs`:
  - `MakeTableAsync`: таблица читает `border-spacing` из собственного CSS, применяет как `Margin = new Thickness(hs, vs, hs, vs)` на `Border` каждой ячейки
  - `RenderTableFallback`: то же самое для стекового fallback-рендерера больших таблиц
- **Эффект**: ячейки перестают слипаться, между ними появляется заданный автором сайта отступ

### `outline`
- Добавлены поля `OutlineWidth`, `OutlineStyle`, `OutlineBrush` в `CssComputed.cs`
- Парсинг в `CssLoader.cs`:
  - Шортханд `outline: width style color` — разбор по словам, определение типа каждого токена (ключевое слово ширины/length, стиль, цвет)
  - Longhands `outline-width`, `outline-style`, `outline-color` — перезаписывают значения после шортханда
  - Дефолты: `outline-style: solid`, `outline-color: black` при наличии ширины
- Рендер в `RendererStyles.WrapWithBoxes`:
  - Если outline не `none` и >0px: создаётся `Grid`, контент сдвигается внутрь через `Margin = ow`, поверх накладывается пустой `Border` с `BorderThickness = ow` и `IsHitTestVisible = false`
  - **Не влияет на layout** — Grid размером с контент, outline рисуется за его пределами
- **Эффект**: фокусные кольца на ссылках/кнопках (`outline: 2px solid Highlight`) отображаются

### `text-overflow: ellipsis`
- **Уже было реализовано** (только подтверждено):
  - `CssLoader` сохраняет все CSS-декларации (включая `text-overflow`) в `css.Map`
  - `RendererStyles.ApplyTextStyle` читает его напрямую из Map и устанавливает `TextTrimming.CharacterEllipsis`

## Файлы

| Файл | Что изменилось |
|------|----------------|
| `Engine\CssComputed.cs` | +`BorderSpacing`, `BorderSpacingVertical`, `OutlineWidth`, `OutlineStyle`, `OutlineBrush` |
| `Engine\CssLoader.cs` | Парсинг `border-spacing` (строки 1115-1131) + `outline` шортханд/longhands (1133-1183) + using `Windows.UI.Xaml.Media` |
| `Engine\TableRenderer.cs` | `MakeTableAsync` + `RenderTableFallback`: border-spacing → cell margin (строки 195-236, 318-352) |
| `Engine\RendererStyles.cs` | `WrapWithBoxes`: outline рендер через Grid-оверлей (строки 432-452) |
| `Doc\Plan_02.md` | Phase 9 таблица: `border-spacing`, `outline`, `text-overflow` отмечены как DONE |

## Оценка

- **border-spacing**: ~60 строк за ~25 минут
- **outline**: ~100 строк за ~30 минут
- **text-overflow**: 0 строк (уже было)
- Сборка: 0 ошибок (UWP — Visual Studio, не считая BuildTools без XAML targets)

## Что остаётся в Phase 9

| Свойство | Сложность | Заметки |
|----------|-----------|---------|
| `border-collapse: collapse` | средняя | Merge границ таблиц. В паре с border-spacing имеет смысл. |
| `word-spacing`, `tab-size` | простая | Редко, но влияет на читаемость |
| `background-attachment` | средняя | Fixed/scroll фоны — редкий use case |
| `transform-origin` | средняя | Сейчас transform без точки опоры |

## Связь с другими фазами

- Phase 9 покрывает визуальные дефекты и идёт **до** Phase 8 (MutationObserver) — правильный порядок: сначала страницы выглядят правильно, потом оптимизируем динамику.
- Phase 6 (ES Modules) поставил модули, но SPA-фреймворки встанут только после Phase 8 (MutationObserver).
- Phase 7 (Service Worker) перенесён после Phase 8 — сначала fix rendering, потом offline.
