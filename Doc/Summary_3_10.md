
# Summary 3.10 — NiL.JS Parser Fix: Destructuring with Array Default Values

**Session date:** 2026-05-20  
**Build:** MediaExplorer.sln Debug x86 — ✅ 0 errors

---

## 1. Problem Identification

### Symptom
Парсинг деструктуризации с массивами по умолчанию выдавал ошибку:
```
JSException: ReferenceError: Invalid left-hand side in assignment. (1:6)
```

### Affected Test Cases
- **D4a**: `const{placement:n='bottom',strategy:o='absolute',middleware:r=[],platform:a}=i`
- **D4b**: `const{placement:n='bottom',strategy:o='absolute',middleware:r=[1,2,3],platform:a}=i`
- **D4**: `const{placement:n='bottom',strategy:o='absolute',middleware:r=[],platform:a}=i`

### Root Cause
Баг в функции `skipExpression` внутри `ValidateDestructuring` в `Parser.cs`:
```csharp
// БЫЛО (неправильно):
case '}' or ')' or ']' when (bracketStack.Count == 0 || bracketStack.Pop() != code[index]):
    return false;
```
Ошибка в том, что при сравнении проверялось, совпадает ли открывающая скобка с **закрывающей** (например, `'[' != ']'`), а не с соответствующей открывающей.

---

## 2. Fix Implementation

### Modified File: `NiL.JS/Core/Parser.cs`
Заменили общее условие на три отдельных с правильными парами скобок:
```csharp
// СТАЛО (правильно):
case '}' when (bracketStack.Count == 0 || bracketStack.Pop() != '{'):
    return false;
case ')' when (bracketStack.Count == 0 || bracketStack.Pop() != '('):
    return false;
case ']' when (bracketStack.Count == 0 || bracketStack.Pop() != '['):
    return false;
```

---

## 3. Testing & Validation

### Updated Test File: `NilJsTest/Program.cs`
Добавлены комплексные тесты для проверки паттернов из упакованного бандла:
- `TestBundlePatterns()` — тестирование сложных деструктуризаций с полным контекстом
- `TestSimpleCases()` — изолированное тестирование массивов по умолчанию
- `TestArrayExpression()` — проверка `[]` в разных контекстах
- `TestParseWithContext()` — выполнение кода с определенными переменными

### Test Results
Все тесты теперь проходят успешно:
```
=== D1: Simple destructuring without defaults ===
PASS

=== D2: Destructuring with string defaults ===
PASS

=== D3: Destructuring with number defaults ===
PASS

=== D4a: Destructuring with array literal default ===
PASS

=== D4b: Destructuring with array with elements ===
PASS

=== D4c: Destructuring with multiple defaults ===
PASS

=== D4d: Mixed defaults (array + string) ===
PASS

=== D4e: Complex destructuring from bundle ===
PASS

=== TESTING SIMPLE CASES ===
✅ Simple array default: [1,2,3]
✅ Empty array default: []
✅ Nested array default: [1,[2,3]]
✅ Multiple array defaults: a=[1], b=[2]

=== TESTING ARRAY EXPRESSION ===
✅ Empty array: []
✅ Array with elements: [1,2,3]
✅ Nested array: [1,[2,3]]

=== TESTING BUNDLE PATTERNS ===
✅ Test 1: Complex destructuring with multiple defaults
✅ Test 2: Full bundle pattern with all properties
✅ Test 3: Multiple destructuring assignments
✅ Test 4: Function parameter destructuring

=== TESTING WITH CONTEXT ===
✅ Destructuring with object context
✅ Destructuring with null/undefined fallback
✅ Multiple destructuring in sequence
```

---

## 4. Build Verification

### MediaExplorer Solution Build
```
msbuild MediaExplorer.sln /p:Configuration=Debug /p:Platform=x86
```
**Результат:** ✅ Успешно собрано с предупреждениями (не связанными с нашими изменениями), без ошибок.

---

## 5. Files Changed

| File | Lines Changed | Description |
|------|---------------|-------------|
| `NiL.JS/Core/Parser.cs` | ~10 | Исправлен баг в `skipExpression` внутри `ValidateDestructuring` |
| `NilJsTest/Program.cs` | ~200 | Добавлены расширенные тесты для деструктуризации |

---

## Next Steps

1. **Протестировать выполнение полного упакованного бандла** `main-BE-aXEfW.js`
2. **Синхронизировать изменения** с `!Browsers/MediaExplorer`
3. **Проверить логи отладки** при открытии сайтов с ES Modules

---

*Session 3.10 — 2026-05-20*

