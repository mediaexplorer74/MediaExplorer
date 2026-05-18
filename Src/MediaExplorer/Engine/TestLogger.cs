using System;
using System.Diagnostics;

namespace BrowserCore.Engine
{
    /// <summary>
    /// Structured test logging for MediaExplorer test infrastructure.
    /// Outputs grep-friendly markers: [TEST:PASS], [TEST:FAIL], [TEST:SITE], [TEST:PERF]
    /// </summary>
    public static class TestLogger
    {
        // ===========================
        // Test result logging
        // ===========================

        /// <summary>
        /// Log a passing test.
        /// Format: [TEST:PASS] T-J-001 var-arithmetic
        /// </summary>
        [Conditional("DEBUG")]
        public static void Pass(string testId, string testName)
        {
            Debug.WriteLine($"[TEST:PASS] {testId} {testName}");
        }

        /// <summary>
        /// Log a failing test with optional actual value.
        /// Format: [TEST:FAIL] T-J-007 createElement-innerHTML — got: undefined
        /// </summary>
        [Conditional("DEBUG")]
        public static void Fail(string testId, string testName, string actual = null)
        {
            if (string.IsNullOrEmpty(actual))
                Debug.WriteLine($"[TEST:FAIL] {testId} {testName}");
            else
                Debug.WriteLine($"[TEST:FAIL] {testId} {testName} — got: {actual}");
        }

        // ===========================
        // Site smoke test logging
        // ===========================

        /// <summary>
        /// Log a site smoke test result.
        /// Format: [TEST:SITE] T-S-001 duckduckgo.com — RENDER OK (1 pass, 0 crash)
        /// </summary>
        [Conditional("DEBUG")]
        public static void Site(string testId, string url, string result)
        {
            Debug.WriteLine($"[TEST:SITE] {testId} {url} — {result}");
        }

        /// <summary>
        /// Log a site crash or error.
        /// Format: [TEST:SITE] T-S-003 dzen.ru — CRASH (NiL.JS exception)
        /// </summary>
        [Conditional("DEBUG")]
        public static void SiteError(string testId, string url, string error)
        {
            Debug.WriteLine($"[TEST:SITE] {testId} {url} — ERROR: {error}");
        }

        // ===========================
        // Performance logging
        // ===========================

        /// <summary>
        /// Log performance metrics for a render pipeline stage.
        /// Format: [TEST:PERF] cascade=14ms layout=32ms paint=11ms total=57ms
        /// </summary>
        [Conditional("DEBUG")]
        public static void Perf(string url, int cascadeMs, int layoutMs, int paintMs, int totalMs, int nodeCount = 0, int ruleCount = 0)
        {
            if (nodeCount > 0 && ruleCount > 0)
                Debug.WriteLine($"[TEST:PERF] url={url} nodes={nodeCount} rules={ruleCount} cascade={cascadeMs}ms layout={layoutMs}ms paint={paintMs}ms total={totalMs}ms");
            else
                Debug.WriteLine($"[TEST:PERF] url={url} cascade={cascadeMs}ms layout={layoutMs}ms paint={paintMs}ms total={totalMs}ms");
        }

        /// <summary>
        /// Log a single pipeline stage timing.
        /// Format: [TEST:PERF] phase=css-parse ms=12
        /// </summary>
        [Conditional("DEBUG")]
        public static void PerfStage(string phase, int ms)
        {
            Debug.WriteLine($"[TEST:PERF] phase={phase} ms={ms}");
        }

        // ===========================
        // Utility
        // ===========================

        /// <summary>
        /// Stopwatch helper: returns elapsed milliseconds and stops the watch.
        /// Usage: var ms = TestLogger.Stop(sw);
        /// </summary>
        public static int Stop(System.Diagnostics.Stopwatch sw)
        {
            if (sw == null) return 0;
            sw.Stop();
            return (int)sw.ElapsedMilliseconds;
        }

        /// <summary>
        /// Start a named performance measurement.
        /// Usage: var sw = TestLogger.Start("css-parse");
        /// </summary>
        public static System.Diagnostics.Stopwatch Start(string phase)
        {
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            return sw;
        }
    }
}
