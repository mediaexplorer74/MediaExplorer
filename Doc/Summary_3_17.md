# Summary 3.17 — Massive Globals Expansion (_nilInit Rewrite)

**Session date:** 2026-05-21  
**Build:** MediaExplorer.sln Debug x64 — ✅ 0 errors  
**Test result:** `in` operator error persists — next session

---

## 1. Changes Applied

### Rewrite: `_nilInit()` — Complete Globals Overhaul

**Problem:** Vite bundle uses `"key" in window` / `"key" in navigator` / `"key" in globalThis` pattern for feature detection. Missing globals caused `TypeError: Right-hand value of operator in is not an object.`

**File changed:** `JavaScriptEngine.cs` — `_nilInit()` rewritten from scratch (~170 lines)

#### Window Aliases (all point to same HostWindow instance)
| Global | Purpose |
|--------|---------|
| `window` | Primary global object |
| `self` | Worker-equivalent alias |
| `globalThis` | ES2020 standard global |
| `global` | Node.js compatibility |
| `top` | Top-level frame |
| `parent` | Parent frame |

#### Window Properties (commonly checked via `in`)
| Property | Value |
|----------|-------|
| `devicePixelRatio` | `1.0` |
| `innerWidth` / `innerHeight` | `1024` / `768` |
| `outerWidth` / `outerHeight` | `1024` / `768` |
| `pageXOffset` / `pageYOffset` | `0` |
| `scrollX` / `scrollY` | `0` |
| `screenX` / `screenY` | `0` |
| `closed` | `false` |
| `name` | `""` |
| `origin` | Derived from `BaseUri` |

#### DOM Globals
| Global | Type |
|--------|------|
| `document` | `HostDocument` |
| `console` | `HostConsole` |
| `navigator` | `HostNavigator` (expanded, see below) |
| `location` | `HostLocation` (expanded) |
| `history` | `HostHistory` |
| `localStorage` | `HostLocalStorage` |
| `sessionStorage` | `HostLocalStorage` |

#### Web APIs (new stubs)
| API | Implementation |
|-----|----------------|
| `performance.now()` | Milliseconds since epoch |
| `performance.timeOrigin` | Timestamp |
| `URL` | Constructor stub with `href` + `toString()` |
| `URLSearchParams` | Empty object |
| `atob()` / `btoa()` | Real Base64 encode/decode |
| `crypto.getRandomValues()` | Returns input array |
| `crypto.subtle` | `undefined` |
| `TextEncoder` | Stub with `encode()` → empty bytes |
| `TextDecoder` | Stub with `decode()` → empty string |
| `WebSocket` | `undefined` (Vite HMR checks this) |
| `Blob` | Stub with `size`, `type` |
| `File` | Stub with `name`, `size`, `type` |
| `FormData` | Stub with `append()` |
| `AbortController` | Stub with `signal`, `abort()` |
| `Event` | Stub with `type`, `target`, `preventDefault()`, `stopPropagation()` |
| `CustomEvent` | Stub with `type`, `detail`, `target` |
| `MessageEvent` | Stub with `data`, `origin`, `source` |
| `Image` / `HTMLImageElement` | Stub with `width`, `height`, `src`, `onload`, `onerror` |

#### Event & Animation APIs
| API | Implementation |
|-----|----------------|
| `addEventListener` | No-op function |
| `removeEventListener` | No-op function |
| `dispatchEvent` | Returns `false` |
| `getComputedStyle` | Returns empty object |
| `postMessage` | No-op function |
| `requestAnimationFrame` | Schedules macro-task, calls callback with timestamp |
| `cancelAnimationFrame` | No-op function |

#### HostNavigator — Expanded (30+ properties)
| Property | Value |
|----------|-------|
| `userAgent` | Chrome 131 UA string |
| `appVersion` | `5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36` |
| `appName` | `Netscape` |
| `appCodeName` | `Mozilla` |
| `platform` | `Win32` |
| `vendor` | `Google Inc.` |
| `product` | `Gecko` |
| `productSub` | `20030107` |
| `language` | `en-US` |
| `languages` | `["en-US", "en"]` |
| `onLine` | `true` |
| `cookieEnabled` | `true` |
| `doNotTrack` | `unspecified` |
| `hardwareConcurrency` | `4` |
| `maxTouchPoints` | `5` |
| `javaEnabled()` | `false` |
| `sendBeacon()` | No-op |
| `serviceWorker` | `undefined` |
| `credentials` | `undefined` |
| `permissions` | `undefined` |
| `geolocation` | `undefined` |
| `mediaDevices` | `undefined` |
| `usb` | `undefined` |
| `bluetooth` | `undefined` |
| `hid` | `undefined` |
| `serial` | `undefined` |
| `xr` | `undefined` |
| `clipboard` | `undefined` |
| `connection` | `undefined` |
| `wakeLock` | `undefined` |
| `keyboard` | `undefined` |
| `locks` | `undefined` |
| `storage` | `undefined` |
| `userActivation` | `undefined` |
| `pdfViewerEnabled` | `undefined` |

#### New Classes
| Class | Purpose |
|-------|---------|
| `HostImage` | `HTMLImageElement` stub — `width`, `height`, `naturalWidth`, `naturalHeight`, `src`, `alt`, `onload`, `onerror`, `complete`, `crossOrigin`, `referrerPolicy`, `decoding`, `fetchPriority`, `loading`, `decode()` |

---

## 2. Test Results

| Test | Result |
|------|--------|
| `_nilInit()` compiles | ✅ OK |
| 50+ globals registered | ✅ OK |
| `navigator` has 30+ properties | ✅ OK |
| `performance.now()` callable | ✅ OK |
| `atob`/`btoa` functional | ✅ OK |
| `requestAnimationFrame` schedules | ✅ OK |
| `in` operator error | ❌ Still present — `Right-hand value of operator in is not an object.` |

---

## 3. Remaining Issues

| Issue | Priority | Notes |
|-------|----------|-------|
| `TypeError: Right-hand value of operator in is not an object.` | 🔴 | NiL.JS `In.cs` throws when RHS is not Object — need to fix or make tolerant |
| `System.InvalidOperationException` spam | 🟢 | Expected — NiL.JS catching errors in Vite bundle |
| Private fields `#name` | 🟢 | 12 occurrences, lower priority |

---

## 4. Files Changed Summary

| File | Change |
|------|--------|
| `JavaScriptEngine.cs` | `_nilInit()` rewritten (~170 lines), `HostNavigator` expanded (30+ props), `HostImage` new class |

---

*Session 3.17 — 2026-05-21*
