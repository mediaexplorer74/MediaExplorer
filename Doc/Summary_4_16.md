# Summary 4.16 — D3 Strategy Shift: URL Rewrite d3.v7 → d3.v5 (ES5)

**Session date:** 2026-06-06
**Build:** `msbuild Src\MediaExplorer.sln /p:Configuration=Debug /p:Platform=x86` — ✅ **0 errors**
**Mode:** log analysis + strategic pivot

---

## 1. Problem Restated

After session 3.39c, d3.js eval produced **0 NiL.JS JSExceptions** at C# level, but still `d3=d3_missing` — a JS-level runtime error caught by `SafeEval`'s `try{...}catch(e){}`. The gap is between "parse succeeded" and "factory completed".

The plan was to add diagnostic logging to capture the actual JS error message. However, a fresh log analysis revealed the text-replacement approach is fundamentally insufficient.

---

## 2. Log Analysis: let/const → var Did Not Help

A new log (v0.42.8.0) was analyzed. Key changes from previous:

| Metric | Before (3.39c) | After (let/const → var) |
|--------|---------------|------------------------|
| `[DIAG:EXEC]` for d3.js | Present | Present |
| **NiL.JS JSExceptions** | **0** | **3** (returned!) |
| InvalidOperationException | None | **NEW** (before 3 JSExceptions) |
| `d3=` | `d3_missing` | `d3_missing` |
| `d3js-err:` from d3.js itself | No | No |

### 2.1 InvalidOperationException — New Symptom

The log shows `System.InvalidOperationException` right before the 3 JSExceptions. This triggers SafeEval's `catch (InvalidOperationException)` handler (line 4070), which calls `_nilInit()` and retries `_evalContext.Eval(code)`. The retry produces 3 JSExceptions.

### 2.2 Root Cause: `;let{` → `;var {` Destructuring

The replacement `;let{data:i,width:o,height:a}=n` → `;var {data:i,width:o,height:a}=n` creates a `var` destructuring assignment. NiL.JS does NOT support destructuring with `var` either — it's still ES6+ syntax. This causes a parse error that manifests as `InvalidOperationException`.

### 2.3 let/const Replacement Was Incomplete

Even after `let/const → var`, d3.js v7.9.0 still contains:

| Feature | Count | Example |
|---------|-------|---------|
| `for...of` | ~43 | `for(var n of t)` — still `of` syntax |
| Default params | ~15 | `function(t,n,e=0,i=t.length)` |
| Destructuring | ~5 | `var {data:i,...}=n` (still destructuring) |
| Template literals | ~10 | `` `invalid ${t}` `` |
| Classes | 11 | `class InternMap{...}` |
| Arrow functions | many | `(e,r)=>n(t(e),r)` |

Text replacement (`.Replace()`) cannot systematically transpile all ES6+ syntax to ES5. Each new fix risks introducing new issues (as seen with InvalidOperationException).

---

## 3. Decision: Strategic Pivot — d3.v5 (ES5)

Instead of continuing the text-replacement approach, d3.js v7 is abandoned in favor of d3.js v5.16.0 — the last version that targets ES5 syntax natively.

### 3.1 Why d3.v5 Works

- d3.v5 was published ~2019 targeting ES5 browsers
- No `let`/`const`/`for...of`/`template literals`/`arrow functions` in the bundled output
- NiL.JS can parse and execute it without any text replacements
- Basic APIs used by Nokia Archive (`d3.select`, `d3.scaleOrdinal`, `d3.forceSimulation`, `d3.drag`, `d3.zoom`) are present in both v5 and v7

### 3.2 Implementation: URL Rewrite in FetchScriptStringAsync

**File:** `Src/MediaExplorer/Engine/JavaScriptEngine.cs`

**New method `TryRewriteD3jsUrl`** (line 5792-5802):
```csharp
private static Uri TryRewriteD3jsUrl(Uri u)
{
    if (u == null) return null;
    var s = u.AbsoluteUri;
    if (s.IndexOf("d3js.org/d3.v7", StringComparison.OrdinalIgnoreCase) >= 0)
    {
        var alt = s.Replace("d3.v7", "d3.v5");
        try { return new Uri(alt); } catch { /* swallow */ }
    }
    return null;
}
```

**Call site** (line 5590-5591):
```csharp
// Rewrite d3.v7 → d3.v5 (NiL.JS has limited ES6+ support; v5 uses ES5)
uri = TryRewriteD3jsUrl(uri) ?? uri;
```

The rewrite is applied:
- Before the cache key is computed (so rewritten URL is cached)
- Before `ExternalScriptFetcher` is called
- Before both HTTP request paths (managed-first + generic)
- Also applies to `FetchAsync(Uri, Uri)` which delegates to `FetchScriptStringAsync`

### 3.3 Removed Destructuring Replacement

The `;let{` → `;var {` line was removed from the ES6-to-ES5 replacement chain (`.Replace(";let{", ";var {")` deleted) because it creates invalid `var destructuring` syntax.

---

## 4. d3.v5 Verification

`https://d3js.org/d3.v5.min.js` is live (v5.16.0, 248KB). Successfully fetched and verified. The URL scheme `d3js.org/d3.v{version}.min.js` serves all historical versions.

---

## 5. Files Changed

| File | Change |
|------|--------|
| `JavaScriptEngine.cs` (line 5590-5591) | Added URL rewrite call before cache/HTTP |
| `JavaScriptEngine.cs` (line 5792-5802) | New `TryRewriteD3jsUrl` helper |
| `JavaScriptEngine.cs` (line ~5395) | Removed `.Replace(";let{", ";var {")` |
| `Doc/Summary_4_16.md` | **New file — this document** |

---

## 6. Build

```
msbuild Src\MediaExplorer.sln /p:Configuration=Debug /p:Platform=x86
Build succeeded. 0 errors, 198 warnings.
```

---

## 7. Remaining Work

- 🔴 **Live test on emulator** — compile and run with d3.v5 URL rewrite
- 🟡 If D3 still fails: verify d3.v5 actually parses in NiL.JS (add `[DIAG:EXEC]` log for version)
- 🟡 If D3 loads: check for v5-to-v7 API differences (`d3.event` removed in v6, zoom/drag API changes)
- 🟡 Microtask flush (already added in 3.39)
- 🟡 Update NiL.JS Compat Matrix with actual supported features
