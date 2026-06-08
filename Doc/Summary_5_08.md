# Summary 5.08 – Static SVG injection works; D3 append crashes NiL.JS

**Date:** 2026-06-07  
**Session focus:** Verify XAML pipeline via static SVG; diagnose D3 DOM manipulation

## What was done
- Replaced manual D3 circle test with static SVG injection using `document.createElementNS` and `appendChild` inside the delay loop.
- Added diagnostic to test `d3.select(container).append('svg')` after static SVG injection.
- Extended delay checks to 3000/4000/5000 ms.

## Results from test log
- Static SVG injection succeeded: `[DIAG] Static SVG created via DOM methods` → XAML pipeline rendered a red circle (visible in UI).
- Incremental update detected mutations and performed full `UpdateView`.
- D3 append test: `[DIAG] D3 selection: ok` (so `d3.select` works), but immediately after, a `NiL.JS.Core.JSException` occurs, crashing the app.
- Original D3 timeline never runs; forced init attempt didn't appear.

## Root cause analysis
- XAML pipeline (VirtualizingRenderer) works perfectly – static SVG with `<circle>` renders.
- D3 is loaded and `d3.select` returns a valid selection, but `selection.append('svg')` crashes NiL.JS (likely due to missing internal methods like `createElementNS` or improper handling of D3's chaining).
- The original D3 timeline script never executes its graph creation because it expects a full DOM API that NiL.JS lacks, and our forced `DOMContentLoaded` event failed earlier.

## Next steps
1. **Avoid D3 DOM manipulation** – since D3 `append` crashes NiL.JS, we cannot rely on D3 to build the SVG tree.
2. **Alternative: manually reconstruct the timeline DOM** – after the page loads, we can extract the intended SVG structure from the original D3-generated DOM (if it exists in the LiteElement tree) and manually render it using our XAML mapping.
3. **Fallback to G.1 (SvgImageSource)** – if manual reconstruction is too complex, implement a fallback that serializes the SVG string from the original D3 script (e.g., intercept the SVG string before NiL.JS fails) and renders it as an image.
4. **Investigate NiL.JS D3 crash** – capture more detailed exception information to see exactly which method call fails (e.g., try to call `d3.select(container).append` and log the error message before the crash).

## Files modified
- `Src/MediaExplorer/Engine/CustomHtmlEngine.cs` – added static SVG injection and D3 append test.

## Build status
✅ Rebuild succeeded, but app crashes during D3 append test.

## Open issues
- D3 `append` crashes NiL.JS.
- Original D3 timeline still not rendered.
- Pipeline works for static SVG, so the issue is D3-specific.

---
*Next session: 5.09 – Decide between manual DOM reconstruction or G.1 fallback.*
