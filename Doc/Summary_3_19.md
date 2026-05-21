# Summary 3.19 — Phase 16.5: CSS Stabilization + Redirect Fix

**Session date:** 2026-05-21  
**Build:** MediaExplorer.sln Debug x64 — ✅ 0 errors  
**Test result:** CSS margin/padding now works on `text.npr.org`, `example.com` → "Learn more" redirects to `iana.org` successfully

---

## 1. Changes Applied

### Fix 1: CSS Margin/Padding in VirtualizingRenderer

**Problem:** CSS styles (margin, padding, font-weight, text-decoration) were computed correctly by `CssLoader` but not applied to visual elements by `VirtualizingRenderer`. `text.npr.org` rendered as plain text with no spacing.

**Root cause:** `VirtualizingRenderer.CreateBoxVisual()` only created `Border` elements for nodes with `border/background` or `<a>` tags. Plain `<div>`, `<p>`, `<ul>`, `<li>` elements had no visual container to hold margin/padding.

**Files changed:**
- `Engine/Core/VirtualizingRenderer.cs` — `CreateBoxVisual()`: now creates `Border` for ALL elements with non-zero margin/padding, not just those with border/background
- `Engine/Core/VirtualizingRenderer.cs` — `CreateTextVisual()`: wraps `TextBlock` in `Border` when padding is needed (TextBlock doesn't support Padding natively)
- `Engine/Core/VirtualizingRenderer.cs` — added `IsZero(Thickness)` helper method

**Result:** `text.npr.org` now renders with proper list indentation, link underlines, heading sizes, and paragraph spacing.

---

### Fix 2: HTTPS→HTTP Redirect Handling

**Problem:** UWP `HttpClient` blocks redirects from HTTPS to HTTP at protocol level, throwing exception: "A redirect request will change a secure to a non-secure connection". This prevented navigation from `example.com` → `iana.org`.

**Root cause:** UWP `HttpBaseProtocolFilter` enforces HTTPS→HTTP redirect blocking. Even with `AllowAutoRedirect = false`, the exception is thrown before we can read the `Location` header.

**Files changed:**
- `Engine/ResourceManager.cs` — `ResourceManager()` constructor: set `AllowAutoRedirect = false` for manual redirect handling
- `Engine/ResourceManager.cs` — `FetchTextAsync()`: added exception handler that detects "redirect...secure" error, converts URL from `https://` to `http://`, and retries

**Result:** `example.com` → "Learn more" now successfully navigates to `iana.org/domains/example`.

---

## 2. Test Results

| Site | Before | After |
|------|--------|-------|
| `text.npr.org` | Plain text, no spacing, no underlines | ✅ Lists indented, links underlined, headings bold |
| `example.com` → "Learn more" | Network Error (redirect blocked) | ✅ Navigates to `iana.org` |
| `text.npr.org` article link | ✅ Works | ✅ Works |

---

## 3. Known Issues

- **`iana.org` layout broken:** Complex CSS Grid/Flexbox layout not fully supported yet. Content is readable but menu/sidebar positioning is off. This is expected — Phase 16.5 is CSS stabilization, not full Grid support.
- **URL difference:** `iana.org` vs `www.iana.org` — server returns same content, no functional issue.
- **Trailing slash normalization:** User noted past issues with URLs like `https://dzen.ru/pogoda` (no trailing slash) causing Google search redirects. May or may not still be an issue — needs verification.

---

## 4. Files Modified

| File | Change |
|------|--------|
| `Engine/Core/VirtualizingRenderer.cs` | CSS margin/padding for all elements |
| `Engine/ResourceManager.cs` | HTTPS→HTTP redirect handling |
| `Engine/DomBasicRenderer.cs` | Removed debug logging, fixed `cssUl` scope |
| `Engine/CssLoader.cs` | Removed debug logging |
| `Doc/Plan_03.md` | Added Phase 16.5 status |
| `Doc/Summary_3_19.md` | New file |

---

## 5. Next Steps

1. **Fix `iana.org` layout** — CSS Grid/Flexbox support for complex layouts
2. **Verify trailing slash normalization** — test `dzen.ru/pogoda` vs `dzen.ru/pogoda/`
3. **Continue Phase 16** — CSS variables, `@media` queries, more properties

---

*Summary v3.19 — 2026-05-21*
