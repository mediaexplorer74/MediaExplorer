using System;
using System.Threading.Tasks;

using Windows.Storage;

namespace BrowserCore.Engine
{
    public static class DevToolsLogger
    {
        public static event Action<string> OnLog;

        private static string _logPath = null;
        private static readonly object _logLock = new object();

        private static string GetLogPath()
        {
            if (_logPath != null) return _logPath;
            try
            {
                _logPath = System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, "Logger.txt");
            }
            catch
            {
                _logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MediaExplorerLogger.txt");
            }
            return _logPath;
        }

        public static void Log(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
            OnLog?.Invoke(message);
            lock (_logLock)
            {
                try
                {
                    System.IO.File.AppendAllText(GetLogPath(), message + "\r\n");
                }
                catch { }
            }
        }

        public static void Log(string format, params object[] args)
        {
            Log(string.Format(format, args));
        }
    }
}
