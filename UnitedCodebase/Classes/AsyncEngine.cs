using System;
using Windows.Foundation;

namespace UnitedCodebase.Classes
{
    public class AsyncEngine
    {
        private static string _result;

        public string strResult
        {
            get
            {
                return _result;
            }
        }

        public static async void Execute(IAsyncAction action)
        {
            try
            {
                await action;
                if (action.Status == AsyncStatus.Completed)
                {
                    action.Close();
                    action = null;

                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                    GC.Collect(1, GCCollectionMode.Forced);
                }
            }
            catch (Exception) { }
        }

        public static async void ExecuteString(IAsyncOperation<string> action)
        {
            try
            {
                _result = await action;
                if (action.Status == AsyncStatus.Completed)
                {
                    action.Close();
                    action = null;

                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                    GC.Collect(1, GCCollectionMode.Forced);
                }
            }
            catch (Exception) { }
        }
    }
}
