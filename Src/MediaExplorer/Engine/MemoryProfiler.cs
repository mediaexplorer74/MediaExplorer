using System;
using System.Diagnostics;
using Windows.System;

namespace BrowserCore.Engine
{
    public static class MemoryProfiler
    {
        private static long _baselineBytes;
        private static DateTime _startTime;
        private static int _gcGen0;
        private static int _gcGen1;
        private static int _gcGen2;

        public static void Start()
        {
            _startTime = DateTime.UtcNow;
            _baselineBytes = GC.GetTotalMemory(false);
            _gcGen0 = GC.CollectionCount(0);
            _gcGen1 = GC.CollectionCount(1);
            _gcGen2 = GC.CollectionCount(2);
        }

        public static string GetSnapshot()
        {
            try
            {
                long current = GC.GetTotalMemory(false);
                long delta = current - _baselineBytes;
                var elapsed = DateTime.UtcNow - _startTime;

                int gen0 = GC.CollectionCount(0) - _gcGen0;
                int gen1 = GC.CollectionCount(1) - _gcGen1;
                int gen2 = GC.CollectionCount(2) - _gcGen2;

                AppMemoryUsageLevel level;
                ulong memUsage = 0;
                try
                {
                    memUsage = MemoryManager.AppMemoryUsage;
                    level = MemoryManager.AppMemoryUsageLevel;
                }
                catch { level = AppMemoryUsageLevel.Low; }

                string stats = string.Format(
                    "Mem: {0}MB (Δ{1:+#;-#;0}KB) | GC: {2}/{3}/{4} | ImgCache: {5} | {6:F1}s",
                    memUsage / (1024 * 1024),
                    delta / 1024,
                    gen0, gen1, gen2,
                    ImageCache.Instance.GetStats(),
                    elapsed.TotalSeconds);

                DevToolsLogger.Log("[DIAG:MEM] " + stats);
                return stats;
            }
            catch (Exception ex)
            {
                return "MemoryProfiler error: " + ex.Message;
            }
        }

        public static void LogMemoryUsage(string context)
        {
            try
            {
                long current = GC.GetTotalMemory(false);
                ulong memUsage = MemoryManager.AppMemoryUsage;
                DevToolsLogger.Log("[DIAG:MEM:" + context + "] managed=" + (current / 1024) + "KB app=" + (memUsage / 1024) + "KB imgCache=" + ImageCache.Instance.GetStats());
            }
            catch { }
        }

        public static void TrimMemory()
        {
            try
            {
                ImageCache.Instance.Clear();
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                DevToolsLogger.Log("[DIAG:MEM] Trimmed — cache cleared, GC2 forced");
            }
            catch { }
        }
    }
}
