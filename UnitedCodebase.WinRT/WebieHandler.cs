using System;
using System.Threading.Tasks;
using Windows.Foundation.Metadata;

namespace UnitedCodebase.WinRT
{
    public delegate void ReceivedDataHandler(string d);
    public delegate void ShowContextMenuEventHandler(int x, int y);
    public delegate void ShowSelectionMenuEventHandler(int x, int y);

    [AllowForWeb]
    public sealed class WebieHandler
    {
        public event ReceivedDataHandler ReceivedData;
        public event ShowContextMenuEventHandler ContextMenuOpening;
        public event ShowSelectionMenuEventHandler SelectionMenuOpening;

        public async void sendData(string data)
        {
            Task sendData = Task.Run(() =>
            {
                OnReceivedData(data);
            });

            await sendData;

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
        }

        private void OnReceivedData(string args)
        {
            ReceivedDataHandler completedEvent = ReceivedData;
            if (completedEvent != null)
            {
                completedEvent(args);
                GC.Collect(0, GCCollectionMode.Forced);
                GC.Collect(1, GCCollectionMode.Forced);
                GC.Collect(2, GCCollectionMode.Forced);
            }
        }

        public async void showContextMenu(int x, int y)
        {
            Task showContextMenu = Task.Run(() =>
            {
                OnShowContextMenu(x, y);
            });

            await showContextMenu;

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
        }

        private void OnShowContextMenu(int x, int y)
        {
            ShowContextMenuEventHandler completedEvent = ContextMenuOpening;
            if (completedEvent != null)
            {
                completedEvent(x, y);
                GC.Collect(0, GCCollectionMode.Forced);
                GC.Collect(1, GCCollectionMode.Forced);
                GC.Collect(2, GCCollectionMode.Forced);
            }
        }

        public async void showSelectionMenu(int x, int y)
        {
            Task showSelectionMenu = Task.Run(() =>
            {
                OnShowSelectionMenu(x, y);
            });

            await showSelectionMenu;

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
        }

        private void OnShowSelectionMenu(int x, int y)
        {
            ShowSelectionMenuEventHandler completedEvent = SelectionMenuOpening;
            if (completedEvent != null)
            {
                completedEvent(x, y);
                GC.Collect(0, GCCollectionMode.Forced);
                GC.Collect(1, GCCollectionMode.Forced);
                GC.Collect(2, GCCollectionMode.Forced);
            }
        }
    }
}
