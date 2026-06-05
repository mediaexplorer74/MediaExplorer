using System;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace BrowserCore.Engine
{
    internal static class UiThreadHelper
    {
        internal static CoreDispatcher TryGetDispatcher()
        {
            try
            {
                var disp = Window.Current?.Dispatcher;
                if (disp != null)
                    return disp;
            }
            catch { /* swallow */ }

            try
            {
                return CoreApplication.MainView?.CoreWindow?.Dispatcher;
            }
            catch { /* swallow */ }

            return null;
        }

        internal static bool HasThreadAccess(CoreDispatcher dispatcher)
        {
            try { return dispatcher?.HasThreadAccess ?? false; }
            catch { return false; }
        }

        internal static void RunAsync(CoreDispatcher dispatcher, CoreDispatcherPriority priority, DispatchedHandler callback)
        {
            if (dispatcher == null || callback == null) return;
            try { dispatcher.RunAsync(priority, callback); }
            catch { /* swallow */ }
        }

        internal static async System.Threading.Tasks.Task RunAsyncAwaitable(CoreDispatcher dispatcher, CoreDispatcherPriority priority, DispatchedHandler callback)
        {
            if (dispatcher == null || callback == null) return;
            try
            {
                await dispatcher.RunAsync(priority, callback);
            }
            catch { /* swallow */ }
        }
    }
}
