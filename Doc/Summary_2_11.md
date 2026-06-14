# Summary 2.17 — Image Diagnostics + Placeholder Fix

**Session date:** 2026-05-18
**Build:** 0.8.0.0 ARM Debug — ✅ 0 errors

---

## 1. Image Loading Diagnostics

### Problem
- Images were being fetched successfully (`[FetchImage] ... in 260ms`) but not displaying
- No visibility into why `LoadImageSourceUiThreadAsync` was failing
- `System.ArgumentException` in `System.Net.Http.dll` from problematic `Seek(0)` call

### Fix — DomBasicRenderer.cs
- Removed `ras.Seek(0)` call (IRandomAccessStream.Seek expects ulong, caused ArgumentException)
- Added detailed diagnostic logging:
  - `[ImgTryStream] {url} len={size}` - stream received from ImageLoader
  - `[ImgLoadOK] {url}` - image successfully decoded
  - `[ImgLoadFail] {url} ex={type}: {message}` - decode failure reason
  - `[ImgNullStream] {url}` - ImageLoader returned null
  - `[ImgFallbackUri] {url}` - falling back to UriSource
  - `[ImgSkipSvg] {url}` - SVG skipped (no SvgImageSource support)
  - `[ImgLoadExc] {url} ex={type}: {message}` - unexpected exception

---

## 2. Placeholder Logic Fix

### Problem
- When images failed to load, alt text "Image" was displayed instead of a placeholder
- Generic alt="Image" is not useful; placeholders are better visual indicators

### Fix — DomBasicRenderer.cs
- **Block context (`MakeImageAsync`)**: Always shows `[img]` placeholder (80×60) when all candidates fail. If alt text exists and is NOT "Image", includes truncated alt text in placeholder.
- **Inline context (`AppendInline`)**: Always shows `?` placeholder (40×30) with meaningful alt text if different from "Image".

```csharp
var placeholderText = "[img]";
if (!string.IsNullOrWhiteSpace(alt) && !alt.Equals("Image", StringComparison.OrdinalIgnoreCase))
{
    placeholderText = alt.Length > 20 ? alt.Substring(0, 20) + "..." : alt;
}
```

---

## 3. File Locking Cleanup Fix

### Problem
- Semaphore cleanup logic had incorrect condition (`!_fileLocks.TryGetValue` always false)
- Could cause race conditions or memory leaks

### Fix — ResourceManager.cs
- Simplified cleanup: only remove semaphore if uncontested (`CurrentCount == 1`) and still in dictionary
- Removed unused `_fileLockCleanup` lock object

---

## Files Changed

| File | Change |
|------|--------|
| `Engine/DomBasicRenderer.cs` | Image diagnostics + placeholder logic fix |
| `Engine/ResourceManager.cs` | File locking cleanup fix |

---

## Build Result

```
WEBVIEW -> bin\ARM\Debug\WEBVIEW.exe
WEBVIEW -> AppPackages\WEBVIEW_0.8.0.0_Debug_Test\WEBVIEW_0.8.0.0_arm_Debug.appxbundle
```

**0 errors**, 7 pre-existing warnings.

---

## Known Limitations

- **SVG icons**: UWP's `BitmapImage` cannot decode SVG files. Weather icons (`.svg`) will show `[img]` placeholders unless `SvgImageSource` is available on the platform.
- **elementSize=NaNxNaN**: Still persisting in logs; needs separate investigation.

---

## Next Steps

1. Test with new diagnostic logs to identify exact image loading failures
2. Investigate `elementSize=NaNxNaN` root cause
3. Consider adding SVG support via `SvgImageSource` if available on target platform

---

*Session 2.17 — 2026-05-18*
