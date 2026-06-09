using System;
using System.Threading.Tasks;

using Windows.Storage;

namespace BrowserCore.Engine
{
    public static class DevToolsLogger
    {
        public static event Action<string> OnLog;

        public static void Log(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
            OnLog?.Invoke(message);
            var _ = WriteLogAsync(message);
        }

        public static void Log(string format, params object[] args)
        {
            Log(string.Format(format, args));
        }

        private static async Task WriteLogAsync(string message)
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("Logger.txt", CreationCollisionOption.OpenIfExists);
                await FileIO.AppendTextAsync(file, message + "\r\n");
            }
            catch
            {
                try
                {
                    var fallback = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MediaExplorerLogger.txt");
                    System.IO.File.AppendAllText(fallback, message + "\r\n");
                }
                catch { }
            }
        }
    }
}
