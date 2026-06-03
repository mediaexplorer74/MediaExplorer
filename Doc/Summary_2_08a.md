# Summary 2.8 — Phase 8 Step A: NiL.JS MutationObserver API

## Что сделано

Полностью переписан MutationObserver — от Regex-полифилла на строках к NiL.JS host-объекту.

### 1. Удалён старый Regex-полифилл
- Удалён `RxNewMutationObserver` regex (распознавал только `new MutationObserver(fnName)` с простым идентификатором)
- Удалена регистрация в `TryNewPatterns()` (NiL.JS path) и в MiniRunner path
- Теперь `MutationObserver` — настоящий JS-конструктор через NiL.JS marshal

### 2. Создан `HostMutationObserver` (private sealed class)
- **Паттерн**: NiL.JS factory function (аналог setTimeout/fetch, не typeof-конструктор)
- **`new MutationObserver(callback)`** — принимает JSValue function, хранит как `Function`
- **`observe(target, options)`** — парсит `{ childList, attributes, subtree, attributeOldValue }`, регистрирует в `_activeObservers`
- **`disconnect()`** — удаляет из `_activeObservers`
- **`takeRecords()`** — возвращает все ожидающие записи как JS-массив, очищает очередь

### 3. Новый `InternalMutationRecord`
Заменяет старый `MutationRecord`:

```csharp
private sealed class InternalMutationRecord
{
    public string Type;          // "childList" или "attributes"
    public LiteElement Target;   // C#-ссылка на мутированный узел
    public List<LiteElement> Added;
    public List<LiteElement> Removed;
    public string AttributeName;
    public string OldValue;
}
```

### 4. Обновлены все JsDomElement-хуки
Каждый метод мутации теперь записывает `InternalMutationRecord` с живыми `LiteElement`-ссылками:

| Метод | Тип записи | Цель |
|-------|-----------|------|
| `innerText` setter | childList | `_node` (все дети заменены на #text) |
| `innerHTML` setter | childList | `_node` (captured added + removed children) |
| `setAttribute` | attributes | `_node` + имя атрибута |
| `appendChild` | childList | `_node` + добавленный узел |
| `removeChild` | childList | `_node` + удалённый узел |
| `insertAdjacentHTML` | childList | `_node` или parent (для beforebegin/afterend) |
| `value` setter | attributes | `_node` + "value" |

### 5. Переписан `InvokeMutationObservers`
- Берёт снапшот `_activeObservers` и `_pendingMutations` под lock'ом
- Для каждого observer строит JS-массив записей через `InternalRecordsToJSArray`
- Вызывает колбэк через `(callback as Function).Call(JSValue.Undefined, new Arguments { arr, observer })`
- Всё через `EnqueueMicrotask` (не блокирует текущий скрипт)

### 6. `InternalRecordsToJSArray` + `TargetDescriptor`
- Строит JS-объекты напрямую через `_nil.Eval("({})")` и установку свойств
- `TargetDescriptor` генерирует читаемое описание узла (`tag#id` или `tag.class` или `#text`)
- `addedNodes` / `removedNodes` — JS-массивы строковых дескрипторов (Step A: без живых JS-прокси)

## Файлы

| Файл | Изменения |
|------|-----------|
| `Engine\JavaScriptEngine.cs` | ~200 строк изменений: новый класс HostMutationObserver (строки 2360-2376), InternalMutationRecord (2147-2154), InternalRecordsToJSArray (2043-2072), TargetDescriptor (2074-2085), переписан InvokeMutationObservers (2013-2041), обновлены ~10 сайтов записи мутаций, регистрация в _nilInit (2471-2476), удалён Regex (был 1773-1775) + 2 сайта регистрации |

## Чего не хватает до полного Phase 8 (Step B)

| Что | Почему не сейчас |
|-----|-----------------|
| Живые JS-прокси в `addedNodes`/`removedNodes` | Требует хранения маппинга LiteElement ↔ JSValue. Step B. |
| Фильтрация по `target`/`subtree` | Сейчас все записи доставляются всем observer'ам. Безопасно, не по spec. Step B. |
| `attributeOldValue` | Парсится, но oldValue не сохраняется. Step B. |
| `characterData` mutation type | Не реализован (редко используется). Step B. |
| `MutationObserver` backlog `/clear` | `takeRecords()` очищает всё, а не только свои. Step B. |
| Инкрементальный рендер | Основная цель Phase 8 — пока рендер полный O(page). Step B. |

## Связь с планом

Обновлён `Doc/Plan_02.md` — Phase 8 отмечен прогресс Step A. Остаётся Step B (incremental render) для полного закрытия Phase 8.
