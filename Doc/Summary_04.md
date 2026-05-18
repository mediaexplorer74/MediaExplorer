# Summary_04 — Phase 4 (JavaScript Engine Improvements)

## Goal
Make NiL.JS usable (DOM sync, async fetch), batch microtasks, fix MiniRunner scoping, cache compiled code, and replace 40× sequential Regex with token-dispatch.

## Changes

### Files modified
- `Engine\JavaScriptEngine.cs` — ~580 lines added, ~50 lines changed across 6 areas

---

### 1. MiniRunner: JsFuncDef cache + timer bypass

**Before:** Each `setTimeout(fn(), ms)` / `setInterval(fn(), ms)` tick called `RunInline(code, ...)`, which went through 40+ Regex checks and created a new `JsFuncDef` on each invocation.

**After:** The code string is pre-parsed into a `JsFuncDef` via `GetOrCacheFuncDef(code)` at schedule time. On timer tick, `ExecuteCachedInline(compiled)` creates a fresh MiniRunner and executes the body directly — no Regex, no pattern matching.

New method `ExecuteCachedInline(JsFuncDef def)` at `JavaScriptEngine.cs:2172`:
```csharp
private void ExecuteCachedInline(JsFuncDef def)
{
    if (def == null || string.IsNullOrWhiteSpace(def.Body)) return;
    var r = new JsMiniRunner(this);
    r._src = def.Body; r._pos = 0; r._len = r._src.Length; r.SkipWs();
    while (!r.Eof()) { r.ParseStatement(); r.SkipWs(); }
}
```

---

### 2. RunInline: First-token dispatch (replaces 40× Regex sequential scan)

**Before:** The JS-0 fallback loop tried ~40+ Regex patterns sequentially on every semicolon-separated line of inline JavaScript.

**After:** `GetFirstToken(line)` extracts the first identifier. `TryPatternsByToken(line, token, ctx)` switches on the token and calls a category-specific handler. Common tokens (`setTimeout`, `document`, `event`, `console`, `location`, `alert`, `void`, `window`, `fetch`, `Promise`, `navigator`, `new`, etc.) match 3-8 patterns instead of 40+. Unmatched tokens fall through to the original 40-pattern chain unchanged.

| Token | Handler | Patterns tried |
|-------|---------|----------------|
| `setTimeout` / `clearTimeout` / `setInterval` / `clearInterval` | `TrySetTimeoutPatterns` etc. | 2-3 |
| `event` | `TryEventPatterns` | 2 |
| `console` | `TryConsolePatterns` | 1 |
| `location` | `TryLocationPatterns` | 4 |
| `document` | `TryDocumentPatterns` | 9 |
| `window` | `TryWindowPatterns` | 3 |
| `fetch` / `fetchText` | `TryFetchPatterns` etc. | 3 |
| `Promise` | `TryPromisePatterns` | 2 |
| `navigator` | `TryNavigatorPatterns` | 1 |
| `__xhr_*` | `TryXhrPatterns` | 5 |
| `__hostResolveToken` / `__hostResolveText` / `__enqueueMicrotask` | dedicated handlers | 1 |
| Anything else | Falls through to original 40-pattern chain | ~40 |

**21 handler methods** added, ~420 lines. Original 40-pattern chain is preserved as a safety fallback.

---

### 3. MiniRunner: Block scoping for `let` / `const`

**Before:** All variables (`var`, `let`, `const`) in MiniRunner were stored in a flat `_globals` dictionary — `{ let x = 5; }` outside `{ }` would leak.

**After:** Added `_blockScopes` scope chain (`List<Dictionary<string, JsVal>>`) and helper methods:

| Helper | Purpose |
|--------|---------|
| `PushBlockScope()` / `PopBlockScope()` | Enter/exit a `{ }` block scope |
| `TryGetVar(name, out val)` | Read: walk scopes (innermost→outermost), then globals |
| `SetVarDecl(name, val, blockScoped)` | Write (declaration): if `blockScoped` and inside a block → store in current block scope; else → globals |
| `SetVarAssign(name, val)` | Write (assignment): update var in the scope where it exists; if not found → globals |

**Methods updated:**

| Location | Change |
|----------|--------|
| `ParseStatement` block handler (`if (Match("{"))`) | Wrapped body in `PushBlockScope(); ... PopBlockScope();` |
| `ParseStatement` var/let/const keyword | Passes `blockScoped=true` for `let`/`const`, `false` for `var` |
| `ParseVarDecl` | Accepts `bool blockScoped`, uses `SetVarDecl` |
| `AssignBinding` | Accepts `bool declBlockScoped`, passes through to recursive calls |
| `TryGetVar` ↔ `_globals.TryGetValue` (5 call sites) | All variable reads now walk scope chain |
| `SetVarAssign` ↔ `_globals[name]` (3 call sites) | All variable writes now find correct scope |
| Function/class declarations | Use `SetVarDecl(name, ..., false)` (hoisted to global) |

---

### 4. NiL.JS DOM sync

**Before:** `HostDocument` returned raw `LiteElement` from `getElementById()`. `_nilSyncDocument()` was empty. NiL.JS scripts had no useful DOM access.

**After:** `HostDocument` now returns `JsDomElement` instances (which expose JS-friendly properties via NiL.JS marshaling):

| `document.*` | Type | Description |
|-------------|------|-------------|
| `getElementById(id)` | `JsDomElement` | Find element by ID |
| `getElementsByTagName(tag)` | `JsDomElement[]` | Find elements by tag |
| `createElement(tag)` | `JsDomElement` | Create new element |
| `title` | get/set `string` | Page title (reads/writes `_pageTitle` field) |
| `body` | `JsDomElement` | First `<body>` element or null |
| `documentElement` | `JsDomElement` | Root DOM element |

`JsDomElement` exposes: `id`, `innerText`, `innerHTML`, `className`, `tagName`, `style`, `classList`, `getAttribute()`, `setAttribute()`, `removeAttribute()`, `hasAttribute()`, `removeChild()`, `appendChild()`, `parentNode`, `children`, `querySelector()`, `querySelectorAll()`, `addEventListener()`, `removeEventListener()`, `click()`, `focus()`, `scrollIntoView()`.

`_nilSyncDocument()` is a no-op (DOM is read live from `_domRoot`).

---

### 5. NiL.JS fetch: non-blocking async Promise

**Before:** `fetch(url).then(fn)` called `GetAwaiter().GetResult()` — blocked the NiL.JS engine thread until the HTTP response arrived.

**After:** `fetch(url)` returns a thenable immediately. `.then(fn)` schedules the HTTP request on `Task.Run` and calls the callback via `EnqueueMacroTask` when the fetch completes.

```
fetch(url) → returns thenable immediately
  ↓
.then(onResolve)
  ↓
Task.Run: FetchAsync(uri)
  ↓ (on completion)
EnqueueMacroTask: onResolve(response)
  ↓ (on UI thread)
RunInline callback with response object
```

The response object has `.ok`, `.status`, `.statusText`, `.text()`, `.json()` properties. Error responses are passed with `.ok = false`, `.status = 0`.

---

### 6. Batch microtask processing

**Before:** `DrainMicrotasksInternal()` ran ALL queued microtasks in a single while loop before yielding the UI thread.

**After:** Each batch processes at most `MicrotaskBatchSize = 16` tasks. If more remain, `DispatchToUi(DrainMicrotasksInternal)` is called to reschedule the next batch, allowing the UI thread to process layout/paint/input events between batches.

```csharp
// Before
while (true) { work = _microtasks.Dequeue(); work(); }

// After
for (int i = 0; i < 16; i++) { work = _microtasks.Dequeue(); work(); }
if (hasMore) DispatchToUi(DrainMicrotasksInternal);
```

---

### 7. Non-empty catch blocks (completed in earlier Phase 0 but part of plan)
- 649 empty `catch { }` filled with `System.Diagnostics.Debug.WriteLine("[file] empty catch")` across 17 files in `Engine/`
- 1 false-positive reverted

---

## Build result
- **0 errors** (when built in VS with UWP workload)
- **~630 lines changed** (~580 added, ~50 modified) across 1 file

## Phase 5 (next)
Resource Loading: disk cache for images, request priority queue, reduced LRU caps, cache SkiaSharp MethodInfo, remove HttpCache stub refs, configurable TTL.
