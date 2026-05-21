
# Summary 3.12 — NiL.JS Parser Fix: get/set as Field Names with Colons

**Session date:** 2026-05-20  
**Build:** MediaExplorer.sln Debug x86 — ✅ 0 errors

---

## 1. Recap of Previous Fixes

| Fix | File | Description |
|-----|------|-------------|
| Destructuring with array defaults | `Parser.cs` | Fixed `skipExpression` bracket matching |
| Async methods in object literals | `ObjectDefinition.cs` | Fixed `async`/`*` as field names |

---

## 2. New Bug: `get`/`set` Keywords as Regular Field Names

### Issue
When parsing object literals like:
```javascript
{ set: function() { throw Error() } }
```
- The parser recognized "set" as a getter/setter keyword
- But it didn't reset the state properly when it saw `:` after it
- This caused a syntax error!

### Root Cause in `ObjectDefinition.cs`
Original code at lines 142-148:
```csharp
bool getOrSet = Parser.Validate(state.Code, "get", ref i) || Parser.Validate(state.Code, "set", ref i);
Tools.SkipSpaces(state.Code, ref i);
if (getOrSet &amp;&amp; state.Code[i] == '(')  // only checked for '('!
{
    getOrSet = false;
    i = start;
}
```
**Problem:** Only reset `getOrSet` when `(` was found, NOT when `:` was found!

### Fix Applied
```csharp
if (getOrSet &amp;&amp; (state.Code[i] == '(' || state.Code[i] == ':'))  // check for both!
{
    getOrSet = false;
    i = start;
}
```

---

## 3. Testing Results

### Test 1: Fixed Pattern from Bundle
```javascript
Object.defineProperty(t.prototype,'props',{set:function(){throw Error()}})
```
**Result:** ✅ OK (previously ERROR!)

### Test 2: Full Bundle Progress
Now gets to error at 33:150674 (from 33:6314) — huge progress!

---

## 4. Files Changed

| File | Lines Changed | Description |
|------|---------------|-------------|
| `NiL.JS/Expressions/ObjectDefinition.cs` | 1 | Added `:` check in `getOrSet` condition |

---

## 5. Key Findings from Browser Logs

### Module `_` Request
The request for `_.js` comes from line 1 of the bundle:
```javascript
import("_").catch(() =&gt; 1)
```
This is normal Vite preload code — it's trying to load a dynamic module that probably doesn't exist in the test environment.

---

## 6. Next Steps

1. Continue fixing remaining syntax errors in the bundle
2. Test with real Nokia Design Archive site
3. Provide debug logs from MediaExplorer

---

*Session 3.12 — 2026-05-20*

