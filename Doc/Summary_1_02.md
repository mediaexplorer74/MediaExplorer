# Summary_02 — Phase 2 (CSS Cascade Optimization)

## Goal
Replace the O(nodes × rules) cascade matching with a selector index, and eliminate regex/split/linq overhead in hot parsing paths.

## Changes

### Files modified
- `Engine\CssLoader.cs` — major rewrite of cascade + parsing utilities
- `Engine\HtmlLite.cs` — added cached sibling/type index fields

### 1. Selector Index (biggest perf win)
**Before:** For each DOM node, iterated ALL rules and ALL selector chains — O(nodes × rules × chains).
**After:** Builds `Dictionary<rightmostKey, List<CssRule>>` once. For each node, collects candidate keys (tag, `.class`, `#id`, `*`) and only tests rules whose rightmost segment matches any key.

```
Before: 500 nodes × 2000 rules × 2 chains = 2M Matches() calls
After:  500 nodes × ~50 candidates = 25K Matches() calls → ~80× fewer
```

### 2. Cached sibling/type indices
- Added `_cachedChildIndex` / `_cachedTypeIndex` fields to `LiteElement` (internal, default -1)
- Pre-computed in one O(n) flatten pass at start of cascade
- `:nth-child`, `:first-child`, `:nth-of-type`, `nth-last-child`, `last-of-type` etc. read cached value instead of O(n) scan of parent's children
- Invalidation in `Append`, `Prepend`, `InsertBefore`, `InsertAfter`, `Remove`, `RemoveAllChildren`

### 3. Hot-path regex → compiled static fields
| Pattern | Usage | Compiled |
|---------|-------|----------|
| `@import url(...)` / `"..."` | `ExtractImportUrl` | `_importUrlRx` |
| `min-width: Npx` | `ExtractPx` for media queries | `_minWidthRx` |
| `max-width: Npx` | `ExtractPx` for media queries | `_maxWidthRx` |
| `url(...)` in values | `ResolveUrlIfNeeded` | `_urlFuncRx` |
| hex/rgb color | `ExtractBackgroundColor`, `ExtractBorderColor` | `_hexColorRx` |
| `Npx` in border | `ExtractBorderThickness` | `_borderPxRx` |
| `var(--name)` | `ResolveVariables` | `_varRefRx` |

### 4. `StripComments` without Regex
**Before:** `Regex.Replace(css, @"/\*[\s\S]*?\*/", "")` — allocates regex engine per call.
**After:** Char-by-char `StringBuilder` — single pass, no regex.

### 5. `ParseDeclarations` without `Split`
**Before:** `declText.Split(';')` then each part `Split(':', 2)` — allocates arrays per declaration block.
**After:** Char-by-char parser — single pass, no intermediate array allocations.

### 6. Manual sort instead of LINQ
**Before:** `items.GroupBy(...).Select(g => g.OrderByDescending(...).ToList())` — allocates GroupBy groupings + OrderBy iterators.
**After:** Manual `Dictionary<string, List<...>>` grouping + `List.Sort((a,b) => ...)` with comparison chain (important desc, specificity desc, sourceOrder desc).

### Build result
- **0 errors**, 13 pre-existing warnings (unchanged)
- **~200 lines added, ~80 removed**
