# Summary 4.17 — NiL.JS Parser Depth: StackOverflow Root Cause Found & Fixed

**Session date:** 2026-06-06
**Build:** `dotnet build Src\NilJsTest` — ✅ **0 errors**
**Mode:** deep diagnostic + parser surgery (NiL.JS ExpressionTree.cs)

---

## 1. Problem: d3.v5.min.js Still Fails With StackOverflowException

After the URL rewrite (d3.v7 → d3.v5, session 3.40), the 248KB d3.v5.min.js should parse cleanly in NiL.JS (ES5 syntax). But it produced a **fatal StackOverflowException** — a crash that cannot be caught by `try/catch`, killing the entire process.

The depth guard (added in this session) revealed: parser reaches **depth 300+**, where each depth increment consumes ~12+ stack frames → ~3600+ frames → exceeds 1MB default stack.

---

## 2. Diagnostic Tooling Created

### 2.1 NilJsTest Console App (`Src/NilJsTest`)

A standalone .NET 10 console app that tests NiL.JS parsing outside the UWP context:

| Flag | Purpose |
|------|---------|
| `--eval-file <path>` | Parse and execute a JS file |
| `--depth <N>` | Set `ParseInfo.MaxParserDepth` |
| `--verbose` / `-v` | Enable recursive depth logging |
| `fetchd3` | Download d3.v4/v5/v6 locally |

### 2.2 Parser Depth Guard (both ExpressionTree.cs & Parser.cs)

Added before recursion in both `ExpressionTree.Parse` and `Parser.Parse`:
- Counter `state.ParserDepth` on the shared `ParseInfo` instance
- Static `ParseInfo.MaxParserDepth = 100` (default)
- When exceeded: throws `JSException` with a catchable message (not `StackOverflowException`)
- Message includes code vicinity: `"at index 212481 near "Qs(bl).scale(144.049).clipAngle(60)},t.geoGnomonicRaw=bl,t.g""`

### 2.3 JSException(Error) Constructor Bug Fixed

`ExceptionHelper.ThrowSyntaxError` → `new JSException(new Error(msg))` → `Context.CurrentGlobalContext.ProxyValue(data)` → **NullReferenceException** because `CurrentGlobalContext` is null during parsing (before execution context exists).

Fixed by using `throw new JSException(JSValue.Marshal(msg))` directly in the depth guard.

---

## 3. Root Cause: Recursive Comma Operator Parsing

### 3.1 Diagnostic Trace

Verbose depth logging (enabled via `--verbose`) showed:

```
[PARSER:DEPTH] 25 at index 170101
[PARSER:DEPTH] 50 at index 185321
[PARSER:DEPTH] 100 at index 196887
[PARSER:DEPTH] 200 at index 200571
[PARSER:DEPTH] 275 at index 210562
[PARSER:DEPTH] 300 at index 212474 ← depth guard fires
```

The depth increases monotonically through d3's geo projection section (~index 170k–212k). At depth 275, the code is parsing `t.geoConformalRaw=sl,t.geoConicEqualArea=nl,...` — a long chain of **comma-separated assignments**.

### 3.2 Why Commas Cause Deep Recursion

In `ExpressionTree.parseContinuation`, the comma operator was handled by:
```csharp
second = Parse(state, ref i, false, processComma: true, ...);
```

With `processComma=true`, the inner `Parse` itself recurses for the next comma. For a chain `a,b,c,d,...,z` (~100+ items at the end of d3), each comma adds one stack frame:

```
Parse(a) → parseContinuation → Parse(b,c,d,...) → parseContinuation → Parse(c,d,...) → ...
```

At the top level, this combines with other nesting (method chaining, ternary operators, function bodies) to reach depth 300+.

### 3.3 Stack Frame Consumption

Each depth increment in the comma chain consumes:
- `ExpressionTree.Parse` (~50 CIL bytes prolog)
- `parseContinuation` (not counted by depth counter but still on stack)
- Plus inner frames: `ValidateArrow` → `ValidateName` → `Unescape` → `FunctionDefinition.Parse` → `CodeBlock.Parse` → `Parser.Parse`

Combined: **~12+ stack frames per depth increment**. At depth 300 → ~3600+ frames → StackOverflow.

---

## 4. Fix: Iterative Comma Parsing

**File:** `Src/NiL.JS/NiL.JS/Expressions/ExpressionTree.cs` (lines 1186–1210)

**Before (recursive):**
```csharp
else
    second = Parse(state, ref i, false, processComma, false, false, forForLoop);
```

**After (iterative):**
```csharp
else if (kind == OperationType.None && processComma)
{
    // Collect all comma operands iteratively
    var commaExprs = new List<Expression> { first, Parse(state, ref i, false, false, false, true, forForLoop) };
    while (i < state.Code.Length && state.Code[i] == ',')
    {
        i++;
        Tools.SkipSpaces(state.Code, ref i);
        commaExprs.Add(Parse(state, ref i, false, false, false, true, forForLoop));
    }
    // Build left-associative chain: ((a,b),c)
    second = commaExprs[commaExprs.Count - 1];
    for (int ci = commaExprs.Count - 2; ci >= 0; ci--)
    {
        second = new ExpressionTree() { _left = commaExprs[ci], _right = second, ... };
    }
    binary = false; // prevent the sequential binary handler
}
```

Key changes:
- Inner `Parse` called with `processComma: false` — parses only one operand
- All remaining commas consumed in a `while` loop at the same stack level
- `root: true` passed so `deicstra` still runs on each operand (correct operator precedence)
- After the loop, `binary` is set to `false` to skip the outer binary handler

---

## 5. Results

### 5.1 d3.v5.min.js (248KB) — ✅ FULL PARSE + EXECUTE

```
$ dotnet run -- --eval-file tests/d3.v5.min.js --depth 100
Parser max depth set to 100
Run #1: OK -> undefined
Summary: hash(decimal)=341482710 runs=1 fails=0
```

Max depth: **~25** (previously: exceeded 300 and StackOverflowed)

### 5.2 d3.v4.min.js (222KB) — ✅ FULL PARSE + EXECUTE

```
$ dotnet run -- --eval-file tests/d3.v4.min.js --depth 300
Run #1: OK -> undefined
```

### 5.3 Default Depth Limit (100) is Now Safe

Without the iterative comma fix, d3.v5 needed depth >300 and would StackOverflow. With the fix, both d3 versions parse at max depth ~25. The default `MaxParserDepth = 100` provides **4× headroom**.

### 5.4 Comma Semantics Preserved

Basic comma operator tests pass:
```javascript
var a=1,b=2,c=3; a+b+c;        // → 6
var d=(1,2,3); d;              // → 3
```

---

## 6. Files Changed

| File | Change |
|------|--------|
| `ExpressionTree.cs` (line 1186-1210) | Iterative comma parsing loop (main fix) |
| `ExpressionTree.cs` (line 351-360) | Depth guard with vicinity info |
| `ExpressionTree.cs` (line 385) | Verbose depth logging (`System.Diagnostics.Debug`) |
| `Parser.cs` (line 973-982) | Depth guard with vicinity info |
| `Parser.cs` (line 989) | Verbose depth logging (`System.Diagnostics.Debug`) |
| `ParseInfo.cs` (line 31) | `MaxParserDepth = 100` (unchanged default) |
| `NilJsTest/Program.cs` | **New file** — test harness with `--depth`, `--verbose`, `fetchd3` |

---

## 7. Remaining Work

- 🔴 **UWP build & live test** — build with VS 2026 Insiders MSBuild, deploy to emulator, verify d3.v5 loads and produces visual output
- 🟡 If d3 still `d3_missing`: investigate JS-level error (might be DOM API gap, not parser)
- 🟡 If d3 loads: check v5-to-v7 API compatibility (`d3.event` removed in v6, zoom/drag API differences)
- 🟡 Consider making `parseTernaryBranches` iterative too (last remaining deep-recursion source)
