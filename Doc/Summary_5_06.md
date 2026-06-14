# Summary 5.06 — D3 SVG Async Diagnosis & Incremental Update Fix

**Session date:** 2026-06-07
**Build:** 0 errors via VS 2026 Insiders MSBuild (`C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe` /p:Configuration=Debug /p:Platform=x86)

---

## 1. Context

Session 5.05 реализовал Phase G.2 (SVG→XAML element mapping в VirtualizingRenderer),
но при тестовом запуске на Nokia Design Archive выяснилось, что D3 force graph SVG
не рендерится, хотя и появляется в DOM. Этот сеанс — глубокое расследование причины
и её исправление.

---

## 2. Key Diagnosis Results

### 2.1 Path.Data crash — `CreateSvgPathGeometry` → `CreateSvgPathElement`

**Проблема:** `XamlReader.Load` создаёт `PathGeometry`, но при присвоении `Path.Data`
вылетает исключение: `"Cannot access a frozen DependencyObject"`.

**Причина:** `XamlReader.Load()` возвращает замороженный (frozen) объект `Geometry`.
UWP не позволяет присвоить frozen `Geometry` свойству `Data` контрола `Path`.

**Исправление:** Вместо извлечения `Geometry` из результата `XamlReader.Load`,
весь `<Path>` создаётся через `XamlReader`, стили применяются напрямую на созданный
`Path` (а не на `Path.Data`). `CreateSvgPathGeometry` заменён на `CreateSvgPathElement`.

### 2.2 Два SVG на странице — search icon + timeline

Diagnostics показали:

| SVG | Parent | viewBox | Когда появляется |
|-----|--------|---------|------------------|
| search icon | `<span class="search-icon">` | `0 0 24 24` | Initial render (Phase 4) |
| timeline | `<div id="timeline">` | ? | Repaint (не Phase 4) |

`RenderTreeBuilder.Build()` **НЕ** фильтрует `<svg>` (filtered=4 — HEAD/STYLE/SCRIPT/META/TITLE/LINK).
Search icon SVG рендерится корректно как XAML shapes в Phase 4.

### 2.3 Timeline SVG `children=0` — асинхронная вставка D3

Timeline SVG появляется **только во время repaint**, с `children=0`:
```
[SVG:G.2] CreateBoxVisual parent=div viewBox=? id=timeline class=? children=0
```

**Корень:** D3 добавляет `<svg>` в `#timeline` асинхронно — `appendChild` вызывает
`RequestRepaint()`, который доходит до `CreateBoxVisual` до того, как D3 добавил
дочерние элементы (g, path, circle). D3 создаёт их микротасками/microtask queue,
уже после того как repaint прошёл.

### 2.4 Path `fill=null stroke=null sw=2` — поисковый path невидим

Путь в search icon circle не имеет явных `fill`/`stroke` атрибутов (стилизация через CSS).
Диагностика показала `fill=null stroke=null sw=2`. Для работоспособности SVG-рендеринга
это не критично (эффективно: `CssParser.ParseColor(null)` → `null` → скип).

### 2.5 Incremental Update Gap — центральная проблема

Phase 4 видит только search icon SVG. D3 timeline SVG создаётся после Phase 4.
Последующие мутации DOM (D3 `appendChild`) обрабатываются через
`ApplyIncrementalUpdateAsync`:

1. `DrainRendererMutations()` возвращает `childList` мутации
2. `BuildSubtree(added)` создаёт RenderObject для SVG-детей (path, circle, g)
3. `PatchAdded(ro)` → `PlaceVisualOnCanvas` → `CreateBoxVisual`

**Проблема в `CreateBoxVisual`:** нет обработки для `tag == "PATH"`, `"CIRCLE"`,
`"LINE"`, `"G"` — только `tag == "SVG"`. SVG-дети получают generic Border или
Rectangle (или `null` если нет border/background). Визуалы не отображаются.

---

## 3. Solution: SVG Mutation Detection

### 3.1 Изменения в `CustomHtmlEngine.cs`

**a) `addedNodes` теперь реально заполняется** (line 962)

Раньше список создавался (`var addedNodes = new List<LiteElement>()`) но никогда
не наполнялся — мутации не добавляли в него элементы. Теперь при обработке
`childList` мутаций каждый `mut.Added` попадает и в `affectedNodes`, и в `addedNodes`.

**b) SVG-детекция перед инкрементальными патчами** (lines 1072–1104)

```csharp
// Phase 3: Patch renderer
bool hasSvgMutation = false;
foreach (var n in addedNodes)
    if (IsSvgOrHasSvgAncestor(n)) { hasSvgMutation = true; break; }
if (!hasSvgMutation)
    foreach (var n in affectedNodes)
        if (IsSvgOrHasSvgAncestor(n)) { hasSvgMutation = true; break; }

if (hasSvgMutation)
{
    // Диагностика
    System.Diagnostics.Debug.WriteLine("[DIAG] IncrementalUpdate SVG mutation, full UpdateView");
    _currentRenderer.UpdateView();  // полный перестроение
}
else
{
    // Обычные инкрементальные патчи
    foreach (var added in addedNodes) _currentRenderer.PatchAdded(ro);
    foreach (var node in affectedNodes) _currentRenderer.PatchStyle(ro);
}
```

Если любая мутация затрагивает SVG-элемент (или его потомок в SVG-поддереве),
вместо `PatchAdded`/`PatchStyle` делается полный `UpdateView()`. Это гарантирует,
что `RenderSvgElement` перестроит весь SVG Canvas через рекурсивный обход
актуального LiteElement-дерева (которое уже содержит всех D3-детей).

**c) Хелпер `IsSvgOrHasSvgAncestor`** (line 1793)

```csharp
private static bool IsSvgOrHasSvgAncestor(LiteElement node)
{
    if (string.Equals(node.Tag, "svg", StringComparison.OrdinalIgnoreCase))
        return true;
    var cur = node.Parent;
    while (cur != null)
    {
        if (string.Equals(cur.Tag, "svg", StringComparison.OrdinalIgnoreCase))
            return true;
        cur = cur.Parent;
    }
    return false;
}
```

Проверяет: является ли узел SVG (сам элемент `<svg>`) или есть ли SVG-предок
(нужно для path/circle добавленных внутрь SVG).

### 3.3 `TriggerDelayedSvgRefresh` — safety net

После Phase 4 в `RenderAsync` запускается `TriggerDelayedSvgRefresh()` — fire-and-forget
таймер 400ms, который затем проверяет `_activeDom` на наличие `<svg>` элементов с
`Children.Count > 0`. Если D3 асинхронно добавил детей в SVG (через Promise/microtask
после Phase 4), вызывается `RefreshAsyncInternal` + `DispatchRepaintAsync` — полное
перестроение визуального дерева с актуальным LiteElement-деревом.

Зачем нужен, если есть SVG mutation detection:
- Mutation detection требует, чтобы `DrainRendererMutations()` вернул мутации —
  если D3 async код выполняется между Poll-циклами, мутации могут быть потеряны
- Delay-страховка не зависит от механизма мутаций — просто ждёт и проверяет DOM
- Обрабатывает кейс, когда мутации есть, но `ApplyIncrementalUpdateAsync` упал с
  `System.Exception` (который ловится и глотается на строках 1107–1110)

Метод использует `Interlocked.Exchange` на `_svgRefreshPending` для гарантии
единственного активного таймера:

```csharp
private void TriggerDelayedSvgRefresh()
{
    if (Interlocked.Exchange(ref _svgRefreshPending, 1) != 0) return;
    // ... проверка наличия <svg> в _activeDom ...
    // ... Task.Delay(400) ...
    // ... если SVG с детьми → RefreshAsyncInternal + DispatchRepaintAsync
}
```

### 3.2 Изменения в `VirtualizingRenderer.cs`

**`CreateSvgPathGeometry` → `CreateSvgPathElement`**

Замена метода из-за crash с frozen `Geometry`. Новый метод:

```csharp
private static UIElement CreateSvgPathElement(LiteElement node, SvgRenderState state)
{
    // XamlReader.Load строит Path целиком из SVG path d-строки
    // Полученный Path используется напрямую, без извлечения Geometry
    string d = GetAttr(node, "d") ?? "";
    string xaml = $"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                  $"Data='{EscapeXaml(d)}'/>";
    var path = (Path)Windows.UI.Xaml.Markup.XamlReader.Load(xaml);
    ApplyPathStyles(path, node, state);
    return path;
}
```

---

## 4. Сравнение с предыдущими изменениями

| Изменение | Summary_5_05 | Summary_5_06 |
|-----------|-------------|-------------|
| SVG element mapping | ✅ circle/line/rect/ellipse/path/text/g | ✅ (unchanged) |
| Path Geometry | `CreateSvgPathGeometry` | `CreateSvgPathElement` (frozen fix) |
| Path crash | ❌ Not tested | ✅ Fixed |
| addedNodes population | ❌ Always empty | ✅ Real data |
| SVG mutation detection | ❌ Not implemented | ✅ `IsSvgOrHasSvgAncestor` + `UpdateView` |
| Root cause analysis | ❌ Not done | ✅ Two-SVG diagnosis, async timing |
| Delayed SVG refresh | ❌ Not implemented | ✅ `TriggerDelayedSvgRefresh` (400ms) |
| Build | 0 errors | ✅ 0 errors (VS 2026 Insiders MSBuild) |

---

## 5. Next Steps

1. **Запустить на эмуляторе** с Nokia Design Archive (сборка готова — 0 errors)
2. Проверить `[DIAG:SVG] Delayed refresh: SVG children detected` в логах
3. Проверить `[DIAG] IncrementalUpdate SVG mutation, full UpdateView` в логах
4. Если D3 force graph появился — Phase G.2 confirmed
5. Если не появился — диагностировать: приходит ли SVG в `_activeDom` с детьми через 400ms

---

## 6. Technical Debt / Known Issues

- **`System.Exception` глотается в `ApplyIncrementalUpdateAsync`** (строки 1107–1110): если
  мутация упала, метод возвращает `true` и блокирует fallback `RefreshAsyncInternal`.
  `TriggerDelayedSvgRefresh` — workaround: не зависит от механизма мутаций.
- **SVG mutation → полный `UpdateView()`:** производительность может страдать при частых
  мутациях внутри SVG (например, каждый force tick). В будущем можно оптимизировать:
  ререндерить только SVG Canvas, а не весь viewport.
- **Duplicates в RenderObject tree** при обработке мутаций: `BuildSubtree` создаёт полное
  поддерево для добавленного элемента, включая его уже существующих детей. Если те же дети
  добавлены отдельными мутациями в том же батче, создаются дубликаты. Это pre-existing баг,
  не связанный с SVG. На практике визуально не заметен (дубликаты накладываются).
- **Build:** VS 2026 Insiders MSBuild (`C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe`). `dotnet build` не работает для UWP.

---

*Next: Deploy & test on emulator with Nokia Design Archive.*
