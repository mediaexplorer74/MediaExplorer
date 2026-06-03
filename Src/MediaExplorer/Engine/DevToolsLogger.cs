using System;

namespace BrowserCore.Engine
{
    /// <summary>
    /// Static logger that mirrors Debug.WriteLine to DevTools Console.
    /// Used by TestLogger and JS console.log implementation.
    /// </summary>
    public static class DevToolsLogger
    {
        public static event Action<string> OnLog;

        public static void Log(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
            OnLog?.Invoke(message);
        }

        public static void Log(string format, params object[] args)
        {
            Log(string.Format(format, args));
        }
    }
}
