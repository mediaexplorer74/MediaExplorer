# Summary 2.16 — Image Rendering Fix + File Locking Fix

**Session date:** 2026-05-18
**Build:** 0.8.0.0 ARM Debug — ✅ 0 errors

---

## 1. Image Loading Wire-Up

### Problem
- `imgLoader` was `null` in `MainPage.xaml.cs:379`
- Images fell back to `bmp.UriSource = abs` which has no cookies/headers
- Many sites (dzen.ru, ya.ru) require cookies for image delivery

### Fix — MainPage.xaml.cs
- Wired `imgLoader` to `_resources.FetchImageAsync(uri, baseUri)`
- Now images go through the cookie-aware `ResourceManager` pipeline
- Includes memory cache, disk cache, HSTS, proper headers

```csharp
Func<Uri, Task<Windows.Storage.Streams.IRandomAccessStream>> imgLoader = async (uri) =>
{
    if (uri == null) return null;
    try { return await _resources.FetchImageAsync(uri, baseUri); }
    catch { return null; }
};
```

---

## 2. File Locking Fix in ResourceManager

### Problem
- Concurrent image fetches to the same URL caused "The file is in use" errors
- `FileIO.ReadBufferAsync` (disk cache read) and `FileIO.WriteBufferAsync` (disk cache write) conflicted
- Multiple parallel requests for the same image URL would race on the same cache file

### Fix — ResourceManager.cs
- Added per-file locking using `ConcurrentDictionary<string, SemaphoreSlim>`
- `_fileLocks` dictionary keyed by `partition:filename`
- All disk cache operations (read + write) wrapped in `await fileLock.WaitAsync()` / `fileLock.Release()`
- Automatic cleanup: semaphores removed from dictionary when no longer contested

```csharp
private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = 
    new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

var fileLockKey = partition + ":" + fname;
var fileLock = _fileLocks.GetOrAdd(fileLockKey, _ => new SemaphoreSlim(1, 1));
await fileLock.WaitAsync();
try { /* disk cache read/write */ }
finally { fileLock.Release(); /* cleanup if uncontested */ }
```

---

## 3. Image Placeholder for Failed Loads

### Problem
- When all image candidates failed, `MakeImageAsync` returned `null`
- Inline images showed only alt text or nothing
- No visual indication that an image was supposed to be there

### Fix — DomBasicRenderer.cs

#### MakeImageAsync (block context)
- Returns a `[img]` placeholder Border (80×60) when all candidates fail and no alt text
- Placeholder has light gray background, subtle border, centered text

#### AppendInline case "img" (inline context)
- Now handles any `FrameworkElement` returned by `MakeImageAsync` (not just `Image`)
- Falls back to a small `?` placeholder (40×30) if no alt text and no image

```csharp
var placeholder = new Border
{
    Width = 80, Height = 60,
    Background = new SolidColorBrush(Color.FromArgb(255, 240, 240, 240)),
    BorderBrush = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
    BorderThickness = new Thickness(1),
    Child = new TextBlock { Text = "[img]", ... }
};
```

---

## Files Changed

| File | Change |
|------|--------|
| `MainPage.xaml.cs` | Wired `imgLoader` to `_resources.FetchImageAsync` |
| `Engine/ResourceManager.cs` | Added per-file `SemaphoreSlim` locking for disk cache |
| `Engine/DomBasicRenderer.cs` | Image placeholders + inline `FrameworkElement` handling |

---

## Build Result

```
WEBVIEW -> bin\ARM\Debug\WEBVIEW.exe
WEBVIEW -> AppPackages\WEBVIEW_0.8.0.0_Debug_Test\WEBVIEW_0.8.0.0_arm_Debug.appxbundle
```

**0 errors**, 7 pre-existing warnings.

---

## Next Steps

1. Test on Lumia 950/1020 with real sites (dzen.ru, ya.ru)
2. Verify images load through ResourceManager pipeline
3. Check for "file is in use" errors in debug output
4. Confirm placeholders appear for broken/missing images

---

*Session 2.16 — 2026-05-18*
