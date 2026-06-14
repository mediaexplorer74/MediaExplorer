# June 12, 2026 — Timeline Mode + Automation Loop

## Summary

Implemented timeline rendering, fixed double-render crash, automated the build-test cycle.

### New: Timeline Mode
- **Data**: 230 stories (`__stories` from `Cf.stories`) + 130 dated entries (`__entriesWithDates`)
- **Layout**: Greedy row-packing (non-overlapping date ranges), 140 rows from 230 stories
- **SVG**: Compact 3px/row bars, year axis, entry scatter at bottom
- **Dimensions**: 31KB SVG, 490px height (was 2340px before fix)
- **Colors**: Teal bars for primary date range, red for secondary (`start2`/`end2`)
- **Route**: Injected into `#timeline` div via `innerHTML`

### Fixed: Double Re-render Loop
- **Root cause**: Route detection code set `window.location.hash = '#/network'` and clicked nav links, triggering a full page reload → second render cycle
- **Fix**: Removed hash set and nav link click from route detection; now log-only
- **Result**: Single render pass, no mirror/nesting effect, app exits cleanly

### Fixed: Timeline Height Explosion
- **Before**: 140 rows × 16px + labels = 2340px total → app froze on second render
- **After**: 140 rows × 3px, no labels, capped at 490px

### New: Fully Automated Build-Test Loop
AI can now:
1. Build → Deploy+Launch → Wait 120s → Kill process → Read log → Analyze → Repeat
2. No manual launch or "готово" signal needed
3. Script in AGENTS.md

### Data Flow
```
Chunk (589KB) → brace-counting → Kf/wf/Cf/vf → SafeEval injection
  → Sync graph builder → window.__graphData (755n + 1647l)
  → Graph SVG (circular) → #plot.innerHTML
  → Timeline SVG (compact rows) → #timeline.innerHTML
  → XAML render (no second cycle)
```

### Key Metrics
- Graph SVG: 755 nodes, 1647 links, 145KB XML, 800×600 viewBox
- Timeline SVG: 230 stories, 140 rows, 31KB XML, 800×490 viewBox
- First render: Phase3 JS DONE + Phase4 BuildVisualTreeAsync DONE
- No second render cycle (route navigation disabled)
- Clean process exit after 120s
