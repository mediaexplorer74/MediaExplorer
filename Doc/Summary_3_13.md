
# Summary 3.13 — NiL.JS Parser Fix: Critical Regression from Session 3.11/3.12

**Session date:** 2026-05-20  
**Build:** MediaExplorer.sln Debug x86 — ✅ 0 errors, all tests passed!

---

## 1. Critical Bug Recap

### What happened
After making fixes in Sessions 3.11 and 3.12, we accidentally introduced a **critical regression**:

1. **Session 3.11**: Added support for async methods by introducing `nameStart` variable.
2. **Session 3.12**: Tried to fix an unrelated issue by adding a condition to reset `i` only when async/asterisk were present.
3. **Result**: Normal (non‑async, non‑generator) methods in object literals **stopped working entirely**!

---

## 2. Root Cause Analysis

### Why normal methods stopped working
For code like:
```javascript
const obj = { test() { return 42; } };
```
The parser flow was:
1. Set `nameStart = start`
2. Parse "test" using `Parser.ValidateName`, advancing `i` to '('
3. Skip spaces, see `state.Code[i] == '('`
4. **Did NOT reset `i`** (because async/asterisk were false)
5. Call `FunctionDefinition.Parse` starting at '(' — which expects a **name** first! → BOOM! "Method must have name" error!

---

## 3. The Fix Applied

**File:** `NiL.JS/Expressions/ObjectDefinition.cs` (lines 310‑315)

```csharp
// Before (BUGGY):
if (state.Code[i] == '(')
{
    if (async || asterisk)  // Wrong condition!
    {
        i = nameStart;
    }
    initializer = FunctionDefinition.Parse(...);
}

// After (FIXED!):
if (state.Code[i] == '(')
{
    // Сбрасываем к началу имени метода (после async/asterisk, если они есть)
    i = nameStart;  // Always reset! nameStart is correct in all cases!
    initializer = FunctionDefinition.Parse(...);
}
```

**Why this works:**
- `nameStart` is **always** correctly set:
  - If no async/asterisk: `nameStart = start` (original position of the method name)
  - If async/asterisk: `nameStart` is set to position after those keywords (correct start of method name)
- So we **always** need to reset `i` to `nameStart` before calling `FunctionDefinition.Parse`!

---

## 4. Test Results After Fix

✅ **ALL TESTS PASSED!**
1. **Simple async method**: OK
2. **Simple normal method**: OK
3. **get/set with colon**: OK

---

## 5. Complete List of Fixes to Date

| Session | File | Fix |
|---------|------|-----|
| 3.10 | `NiL.JS/Core/Parser.cs` | Destructuring with array defaults, fixed bracket matching in `skipExpression` |
| 3.11 | `NiL.JS/Expressions/ObjectDefinition.cs` | Async methods in object literals, added `nameStart` |
| 3.12 | `NiL.JS/Expressions/ObjectDefinition.cs` | get/set keywords as regular field names with colons |
| 3.13 | `NiL.JS/Expressions/ObjectDefinition.cs` | Critical regression fix: always reset `i` to `nameStart` for methods! |

---

## 6. Next Steps

1. Now that all core object‑literal parsing is fixed, continue fixing remaining errors in the full bundle at position 33:150674
2. Test with the real Nokia Design Archive site
3. Provide debug logs as needed

---

*Session 3.13 — 2026-05-20*

