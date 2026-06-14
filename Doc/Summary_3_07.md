# Summary 3.7 — DevTools Logging Overhaul, SVG Diagnostic & ES Module Tests

**Session date:** 2026-05-19 (Evening)  
**Build:** 0.8.0.0 x64 Debug — ✅ 0 errors

---

## 1. DevTools Logging Fixes

### Problem
- `[Module]` messages from `ModuleLoader.cs` used `Debug.WriteLine()` — invisible in Console/Debug tabs
- `[ImgTry]`/`[ImgSkipSvg]` in `DomBasicRenderer.cs` used `Debug.WriteLine()` — invisible in DevTools
- Only `[DIAG]` from `CustomHtmlEngine` reached the DevTools tabs

### Changes
| File | Change |
|------|--------|
| `Engine/ModuleLoader.cs:73-87` | All `[Module]` messages: `Debug.WriteLine` → `DevToolsLogger.Log()` |
| `Engine/ModuleLoader.cs:106` | `[Module] Eval error` — same fix |
| `Engine/ModuleLoader.cs:112-139` | `[Module] Found X import(s)`, `[Module] Skip`, `[Module] Prefetching`, `[Module] Prefetch failed`, `[Module] WARNING: Dependency is HTML` — all routed |
| `Engine/DomBasicRenderer.cs:1809-1813` | `log()` lambda now calls both `Debug.WriteLine` AND `DevToolsLogger.Log()` |
| `Engine/CustomHtmlEngine.cs:1168-1171` | Added `[DIAG] SvgType available: true/false` diagnostic |

### Result
Console/Debug tabs now show complete diagnostic picture:
```
[Module] Fetching: https://.../assets/main-BE-aXEfW.js
[Module] Fetched OK: ... (568328 bytes)
[Module] Eval error: SyntaxError: Unexpected token (1:361)
[Module] WARNING: Got HTML instead of JS from ...
[IMG] https://.../images/icon-network.svg
[DIAG] SvgType available: true
```

---

## 2. SVG Diagnostic

**Observation:** `SvgType available: true` — `SvgImageSource` resolves on desktop. No `[ImgTry]` appeared for Nokia Archive SVGs, suggesting `<img src="./images/...">` relative URLs may not be resolved to absolute URIs before reaching `DomBasicRenderer`.

---

## 3. ES Module Test Suite

Created `Html/TestModule/` (moved from `TestModule/`):

| File | Test ID | Purpose |
|------|---------|---------|
| `inline.html` | T-M-001 | Inline `<script type="module">` without imports |
| `module-import.html` | T-M-002 | `import { greet } from './lib.js'` |
| `import-meta.html` | T-M-003 | `import.meta.url` resolution |
| `lib.js` | — | Exported function + variable for T-M-002 |

All linked in `Html/test.html` as Section E.

---

## 4. NiL.JS 2.6 Downshift Audit

**Source location:** `Src/NiL.JS` (already in repo, version 2.6)
**Current target:** `netstandard2.1+`
**Goal:** Downshift to `netstandard1.4` / UWP 15063 compatible

### Audit results (key findings)

| API | Occurrences | Status |
|-----|-------------|--------|
| `Span<T>`, `stackalloc`, `ref struct` | **0** | No issue |
| `ValueTask`, `IAsyncEnumerable` | **0** | No issue |
| `System.Numerics`, `System.IO.Pipelines`, `System.Buffers` | **0** | No issue |
| `[Serializable]` | ~170 | ✅ Already guarded by `#if !(PORTABLE \|\| NETCORE)` |
| `AppDomain` | 2 | ✅ Already guarded by `#if !NETCORE` |
| `CompiledNode.cs` (JIT) | entire file | ✅ Guarded by `#if !NETCORE` |
| `ValueTuple` polyfill | `Backward.cs` | ⚠ Need to extend `#if NET461` → `#if NET461 \|\| NETSTANDARD1_4` |
| `System.Reflection.Emit` | `JSValueExtensions.cs` | ✅ Available as NuGet for netstandard1.4 |

**Conclusion:** Downshift is realistic, estimated 1–2 days.

---

## 5. Key Observations from Nokia Archive Test Run

- **DOM tree** — full structure visible in DevTools (head, body, SVG elements, scripts)
- **Network tab** — shows `[GET]`, `[OK]`, `[IMG]` for CSS, SVGs, PNGs
- **Debug tab** — shows `[DIAG]` pipeline (START → Phase3 → Phase4 → DONE)
- **Console tab** — shows `[Module]` + `[DIAG]` + `[Img]` messages
- **Error:** `[Module] Eval error: SyntaxError: Unexpected token (1:361)` — Vite 568KB bundle unparseable
- **SVG images** — not rendered in content area (relative URL resolution issue + no `[ImgTry]` in console)
- **SvgType** — available on desktop (`true`)

---

## Next Steps

1. **Phase 20** — NiL.JS 2.6 downshift + switch from NuGet to ProjectReference
2. **Phase 19.3** — Test ES Module suite against updated NiL.JS
3. **Phase 19.4** — Nokia Archive validation with new NiL.JS

---

*Session 3.7 — 2026-05-19*
