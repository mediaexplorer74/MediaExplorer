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
        private static bool _firstWrite = true;

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
                    var path = GetLogPath();
                    if (_firstWrite)
                    {
                        _firstWrite = false;
                        System.IO.File.WriteAllText(path, "[Session started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]\r\n" + message + "\r\n");
                    }
                    else
                    {
                        System.IO.File.AppendAllText(path, message + "\r\n");
                    }
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
