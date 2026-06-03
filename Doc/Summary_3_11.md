
# Summary 3.11 — NiL.JS Parser Fix: Async Methods in Object Literals

**Session date:** 2026-05-20  
**Build:** MediaExplorer.sln Debug x86 — ✅ 0 errors

---

## 1. Problem Analysis from Browser Logs

### Logs Recap
From MediaExplorer DevTools Console:
```
[Module] Fetching: `https://nokiadesignarchive.aalto.fi/assets/main-BE-aXEfW.js`
[Module] Fetched OK: `https://nokiadesignarchive.aalto.fi/assets/main-BE-aXEfW.js`  (568328 bytes)
[Module] Eval error: SyntaxError: Unexpected token (33:135565)
[Module] ResolveModule: spec=_ absPath=/_.js
```

### Key Findings
1. **SyntaxError at 33:135565**: Async method in object literal syntax (`async fn(t) { ... }`)
2. **Module request for "_"**: From `import("_")` in line 1 of the bundle

---

## 2. Bug 1: Destructuring with Array Default Values

**File:** `NiL.JS/Core/Parser.cs`  
**Fixed in:** Session 3.10

### Problem
The `skipExpression` function inside `ValidateDestructuring` was incorrectly comparing opening and closing brackets:
```csharp
// БЫЛО (неправильно):
case '}' or ')' or ']' when (bracketStack.Count == 0 || bracketStack.Pop() != code[index]):
    return false;
```

### Fix
Replaced with three separate conditions with proper bracket pairs:
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

## 3. Bug 2: Async Methods in Object Literals

**File:** `NiL.JS/Expressions/ObjectDefinition.cs`  
**Fixed in:** Session 3.11

### Problem
When parsing `async fn(t) { ... }` in an object literal:
1. Parser first recognized `async` and set `async = true`
2. But then at line 276, it reset `i = start` and parsed "async" as a **field name** instead of a keyword!
3. This caused it to miss the `(` after the method name

### Fix
Modified the logic to skip `async` and `*` when parsing method names:

**Change 1 (lines 276-281):**
```csharp
int nameStart = start;
if (async || asterisk)
{
    // Если у нас есть async или *, то пропускаем их, не парся как имя поля
    nameStart = i;
}
```

**Change 2 (lines 286):**
```csharp
fieldName = Tools.Unescape(state.Code.Substring(nameStart, i - nameStart), state.Strict);
```

**Change 3 (lines 312-313):**
```csharp
// Не сбрасываем i полностью, а возвращаемся к началу имени метода (после async/asterisk)
i = nameStart;
```

---

## 4. Testing Results

### Simple Async Method Tests
All tests now pass:
```
=== TEST 1: Async method in object literal ===
Result: OK

=== TEST 2: Simple async method ===
Result: OK

=== TEST 3: Async function expression ===
Result: OK
```

### Full Bundle Progress
The bundle now parses further - from error at 33:135565 to error at 33:6314 (significant progress!).

---

## 5. Files Changed

| File | Lines Changed | Description |
|------|---------------|-------------|
| `NiL.JS/Core/Parser.cs` | ~10 | Fixed bracket matching in `skipExpression` |
| `NiL.JS/Expressions/ObjectDefinition.cs` | ~15 | Fixed async method parsing in object literals |

---

## 6. Next Steps

1. Continue fixing remaining syntax errors in the bundle
2. Test with the real Nokia Design Archive site
3. Provide debug logs from the browser

---

*Session 3.11 — 2026-05-20*

