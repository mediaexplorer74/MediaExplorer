# Summary 4.06 — NiL.JS SafeEval Guards & Inline Script Stability

Short: Session notes for 2026-06-04. Focus: reduce crashes caused by NiL.JS while executing inline scripts, add diagnostics, and improve test tooling.

Key points:
- Implemented pre-skip of repeat-failing inline scripts by hash to avoid repeated crashes.
- Introduced a debounce/threshold (BadHashRecordThreshold = 2) before marking a hash as "bad" to avoid false positives.
- Improved DIAG logging for visibility ([DIAG:JS-SCRIPT], [DIAG:JS-SKIP], [DIAG:JS-BAD], [DIAG:JS-SUMMARY]).
- Added test tooling improvements: NilJsTest auto-discovery and temporary test.html injection to reproduce failures.

Files of interest:
- Src/MediaExplorer/Engine/JavaScriptEngine.cs
- Src/NilJsTest/Program.cs
- Src/MediaExplorer/Html/test.html

How to verify:
- Build the UWP project, open about:test (ms-appx:///Html/test.html), observe DIAG logs in VS Output, and check for js_bad_hashes.json in Pictures/MediaExplorer or the app LocalState.

Note: This file is an English summary. Original session notes have been condensed for repository consistency.
