# Summary 4.01 — NiL.JS Crash Hardening (Phase 16.7)

**Session date:** 2026-06-03  
**Build:** `msbuild MediaExplorer.sln` — ✅ **0 errors** (C# compilation passes)  
**Test result:** ya.ru — 132 nodes, 191 boxes, renders with ScrollViewer (JS DONE). dzen.ru — still crashes with unhandled `JSException` from `RegExp.cs:161` (ArgumentOutOfRangeException → SyntaxError).

---

## 1. Problem: Unhandled JSException crashing the app

**Root cause chain:**
1. NiL.JS `Context.Eval()` was rethrowing unhandled exceptions in `SafeEval()`
2. Timer/macro-task callbacks on ThreadPool threads could throw `JSException` without outer try/catch
3. `Application.UnhandledException` (UWP) only catches UI-thread exceptions — background thread exceptions crash the process
4. `TaskScheduler.UnobservedTaskException` was not registered — fire-and-forget `Task` exceptions were silently unobserved and eventually crashed

**Specific dzen.ru crash:**
```
ArgumentOutOfRangeException in System.Text.RegularExpressions.dll
  → caught by NiL.JS RegExp.cs:161 (catch (ArgumentException e))
  → ExceptionHelper.Throw(new SyntaxError(e.Message))
  → JSException escapes unhandled on background thread
```

---

## 2. Changes Applied

### Fix 1: `SafeEval` — no rethrow, `_niljsSafeEvalFailed` flag

**File:** `Engine/JavaScriptEngine.cs:3284`

| Before | After |
|--------|-------|
| `catch (Exception ex) { ... throw; }` or `catch { }` (inconsistent) | `catch (Exception ex) { _niljsSafeEvalFailed = true; return JSValue.Undefined; }` |

All `_nil.Eval()` calls replaced with `SafeEval()`. After a failed eval, `ExecuteScriptBlock` checks the flag and falls through to the ES5-lite mini-runner instead of crashing.

### Fix 2: Timer callbacks — outer try/catch (4 locations)

**File:** `Engine/JavaScriptEngine.cs`

All Timer delegate bodies now wrapped in outer try/catch to catch any exception on the ThreadPool thread before it crashes the process:

| Location | Change |
|----------|--------|
| `setTimeout` (Function) L2934 | `new Timer(_ => { try { EnqueueMacroTask(...) } catch { } })` |
| `setInterval` (Function) L2969 | Same |
| `ScheduleTimeout` L3401 | Same |
| `ScheduleInterval` L3426 | Same |

Previously the Timer callbacks had try/catch only around the inner logic (ClearTimeout, ExecuteCachedInline) but NOT around the `EnqueueMacroTask()` call itself or the outer lambda.

### Fix 3: `BrowserApi.cs` — `InvokeOnUiThread` try/catch

**File:** `Engine/BrowserApi.cs:104`

The `() => a()` callback dispatched to the UI thread via `CoreDispatcher.RunAsync` was not wrapped in try/catch. If `a()` threw on the UI thread, the exception propagated as unhandled (caught only by `Application.UnhandledException` which sets `e.Handled = true`, but still appears as a debugger break).

**Fix:** Wrapped both `a()` inside the dispatcher callback AND the entire dispatch logic in try/catch.

### Fix 4: `App.xaml.cs` — `TaskScheduler.UnobservedTaskException` handler

**File:** `App.xaml.cs:38`

Added handler with `e.SetObserved()` to prevent fire-and-forget `Task` exceptions from crashing the process on finalization.

---

## 3. Architecture — Error Handling Flow

```
JS Execution
  └─ SafeEval(code) ─────────────────── catch → _niljsSafeEvalFailed=true, return Undefined
       └─ _evalContext.Eval(code)
            └─ JS → regex → RegExp.cs:161 → SyntaxError (JSException)
                 └─ Context.Eval() catch(JSException) → rethrow → SafeEval catches ✓

Timer/Interval callbacks (ThreadPool)
  └─ try {
       EnqueueMacroTask(() → {
         try { fn.Call(…) } catch { }  ← inner
       })
     } catch { }                       ← NEW outer

UI thread dispatcher (CoreDispatcher.RunAsync)
  └─ try { a() } catch { }            ← NEW

Unobserved Task exceptions
  └─ TaskScheduler.UnobservedTaskException → e.SetObserved() ← NEW
```

---

## 4. Remaining Issue

dzen.ru still throws `JSException` from `RegExp.cs:161` (ArgumentOutOfRangeException in .NET Regex → SyntaxError). The new try/catch layers should now catch it even if it comes from a background thread or unobserved Task. After syncing and testing, if the crash persists, a full stack trace from `RegExp.cs:161` is needed to identify the exact call path.

---

## 5. Next Steps

1. **Sync & test dzen.ru** — verify the new try/catch layers prevent the crash
2. **If crash persists** — get full stack trace from `RegExp.cs:161` to trace the unhandled path
3. **P3.21 → P4 overall:** Continue improving NiL.JS stability, then move to `getComputedStyle`, CSSOM, SPA detection

---

*Summary v4.01 — 2026-06-03*
