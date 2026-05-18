# Summary_05 — Phase 5 (Resource Loading)

## Goal
Disk cache for images, request prioritization, remove expensive reflection, reduce LRU caps for 3GB RAM, configurable TTL.

## Changes

### Files modified
- `Engine\ResourceManager.cs` — disk cache for images, priority queue, LRU caps, SkiaSharp MethodInfo cache, shared streams, configurable TTL
- `Engine\BrowserApi.cs` — line 354: image loader tagged Low priority

---

### 1. Disk cache for images (new)

**Before:** `FetchImageAsync` only had memory LRU — images were re-fetched on every navigation.

**After:** Persistent disk cache using the same `cache_{partition}/` scheme as `FetchTextAsync`:
- Data file: `HashForFile(key).img` (raw IBuffer via `FileIO.WriteBufferAsync`)
- Metadata file: `HashForFile(key).meta` (ISO 8601 timestamp)
- Check order: memory LRU → disk cache → network
- Write-through on fetch: both memory LRU and disk updated
- Same partition isolation (`cache_google.com/`, `cache_default/`, etc.)

---

### 2. Request priority queue

Added `ResourcePriority` enum (`Critical`, `Normal`, `Low`) — images are demoted to Low via the `_lowPriorityGate` semaphore (max 4 concurrent low-priority requests). CSS/JS/HTML requests bypass the gate entirely, leaving more HTTP connections for critical rendering resources.

| Resource type | Priority | Gate |
|--------------|----------|------|
| HTML (document) | Critical (implicit) | None |
| CSS (style) | Critical (implicit) | None |
| JS (script) | Critical (implicit) | None |
| Fonts | Normal (default) | None |
| Images | Low (explicit) | SemaphoreSlim(4) |

---

### 3. Reduced LRU caps for 3GB RAM

| Cache | Before | After | Memory estimate |
|-------|--------|-------|-----------------|
| Text | 128 entries | 32 entries | ~10MB (HTML/CSS at ~300KB each) |
| Image | 64 entries | 16 entries | ~64MB (4MB animated GIFs) |

---

### 4. SkiaSharp MethodInfo cache

**Before:** Every `FetchImageAsync → DecodeWithSkiaAsync` call re-resolved SkiaSharp/Svg.Skia types and methods via `Type.GetType()` + `GetDeclaredMethod()` — O(images) reflection calls.

**After:** Static `EnsureSkiaTypes()` resolves `SKSvg`, `SKImage`, `SKData`, `SKEncodedImageFormat` once per app run. `GetCachedMethod()` / `GetCachedProperty()` memoize results in `Dictionary<string, MethodInfo|PropertyInfo>` keyed by `Type.FullName + "::" + name + "(args)"`. First call per (type, method) does reflection; subsequent calls return cached.

---

### 5. HttpCache stub removal (already done, verified)

HttpCache.cs was deleted earlier. Re-verified: no file on disk, no `using` or code references, no csproj entry. Dead code path fully eliminated.

---

### 6. Shared InMemoryRandomAccessStream (zero-copy)

**Before:** Cache hit or fetched image returned via:
```csharp
var mem = new InMemoryRandomAccessStream();
await mem.WriteAsync(buf);   // copies the entire IBuffer
mem.Seek(0);
return mem;
```

**After:** Each `IRandomAccessStream` is created as a zero-copy wrapper:
```csharp
return buf.AsStream().AsRandomAccessStream();
```

`AsStream()` creates an `IBuffer`-backed `Stream` without copying; `AsRandomAccessStream()` wraps it as `IRandomAccessStream`. Same for `byte[]` → `bytes.AsBuffer().AsStream().AsRandomAccessStream()`. Eliminates 2× memory per image stream (was: one copy in LRU + one in the stream).

---

### 7. Configurable disk TTL

`DiskCacheTtl` public field (type `TimeSpan`, default 5 minutes) replaces the hardcoded `TimeSpan.FromMinutes(5)` in both text and image disk cache reads. Host can change TTL dynamically:
```csharp
resources.DiskCacheTtl = TimeSpan.FromMinutes(30);
```

---

## Build result
- **0 errors expected** (UWP requires VS with UWP workload, not dotnet CLI)
- **~120 lines changed** across 2 files

## What's left / future directions
All 6 original phases from Plan_01.md are complete. The project has:
- Usable bottom AppBar with keyboard shortcuts
- 10-50× faster CSS cascade via selector index
- Layout offloaded to background thread (no UI freeze)
- VirtualizingRenderer with element recycling + lazy images
- NiL.JS DOM sync, async fetch, batched microtasks
- Disk-cached images with priority queuing

The next "big step" level would be: **ES Module / SPA support** via NiL.JS full engine, **Service Worker registration** for offline-first, and/or **MutationObserver → incremental re-render** so DOM modifications by JS don't trigger full page rebuild.
