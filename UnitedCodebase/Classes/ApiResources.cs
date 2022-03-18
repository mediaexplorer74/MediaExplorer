using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Foundation.Metadata;
using Windows.Phone.Devices.Notification;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Input.Preview.Injection;
using Windows.UI.Popups;
using Windows.UI.Xaml.Controls;

namespace UnitedCodebase.Classes
{
    public class ApiResources
    {
        static ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;

        #region "Notifications"
        public static async void NotifyUpdate(Uri versionUrl)
        {
            Package package = Package.Current;
            PackageId packageId = package.Id;
            PackageVersion Version = packageId.Version;
            string currentVersion = string.Format("{0}.{1}.{2}.{3}", Version.Major.ToString(), Version.Minor.ToString(), Version.Build.ToString(), Version.Revision.ToString());

            //Create an HTTP client object
            Windows.Web.Http.HttpClient httpClient = new Windows.Web.Http.HttpClient();

            //Add a user-agent header to the GET request. 
            var headers = httpClient.DefaultRequestHeaders;

            //The safe way to add a header value is to use the TryParseAdd method and verify the return value is true,
            //especially if the header value is coming from user input.
            string header = "ie";
            if (!headers.UserAgent.TryParseAdd(header))
            {
            }

            header = "Mozilla/5.0 (Windows Phone 10.0;) Edge/12.10240";


            //Send the GET request asynchronously and retrieve the response as a string.
            Windows.Web.Http.HttpResponseMessage httpResponse = new Windows.Web.Http.HttpResponseMessage();
            string httpResponseBody = "";

            try
            {
                //Send the GET request
                httpResponse = await httpClient.GetAsync(versionUrl);
                if (httpResponse.IsSuccessStatusCode == true)
                {
                    httpResponse.EnsureSuccessStatusCode();
                    httpResponseBody = await httpResponse.Content.ReadAsStringAsync();
                }
            }
            catch (Exception)
            {
            }

            if (currentVersion.CompareTo(httpResponseBody) < 0)
            {
                new UCNotification("New version", string.Format("A new {0} version is available!", httpResponseBody)).ShowNotification();
            }
        }
        #endregion

        #region "Message Boxes"
        public static async void ShowMessageBox(string Title, string Content,
            UICommandInvokedHandler CommandYes, UICommandInvokedHandler CommandNo)
        {
            var messageDialog = new MessageDialog(Content, Title);

            var yesCommand = new UICommand("Yes", CommandYes);
            var noCommand = new UICommand("No", CommandNo);
            var cancelCommand = new UICommand("Cancel");

            messageDialog.Commands.Add(yesCommand);

            messageDialog.DefaultCommandIndex = 0;
            messageDialog.CancelCommandIndex = 1;

            if (noCommand != null)
            {
                messageDialog.Commands.Add(noCommand);
                messageDialog.CancelCommandIndex = (uint)messageDialog.Commands.Count - 1;
            }

            if (cancelCommand != null)
            {
                // Devices with a hardware back button
                // use the hardware button for Cancel.
                // for other devices, show a third option

                var t_hardwareBackButton = "Windows.Phone.UI.Input.HardwareButtons";

                if (ApiInformation.IsTypePresent(t_hardwareBackButton))
                {
                    // disable the default Cancel command index
                    // so that dialog.ShowAsync() returns null
                    // in that case

                    messageDialog.CancelCommandIndex = UInt32.MaxValue;
                }
                else
                {
                    messageDialog.Commands.Add(cancelCommand);
                    messageDialog.CancelCommandIndex = (uint)messageDialog.Commands.Count - 1;
                }
            }

            await messageDialog.ShowAsync();
        }


        public static async void ShowMessageBoxTwoButton(string Title, string Content,
            UICommandInvokedHandler CommandYes)
        {
            var messageDialog = new MessageDialog(Content, Title);

            var yesCommand = new UICommand("Yes", CommandYes);
            var noCommand = new UICommand("No");

            messageDialog.Commands.Add(yesCommand);
            messageDialog.Commands.Add(noCommand);

            messageDialog.DefaultCommandIndex = 0;
            messageDialog.CancelCommandIndex = 1;

            // Devices with a hardware back button
            // use the hardware button for Cancel.
            // for other devices, show a third option

            await messageDialog.ShowAsync();
        }
        #endregion

        #region "Content Dialogs"
        public static async void ShowPopUpDialogAsync(string title, object content, bool twobutton = false, bool prompt = false)
        {
            ContentDialog PopUpDialog = new ContentDialog()
            {
                PrimaryButtonText = "OK"
            };

            if (twobutton)
            {
                PopUpDialog.SecondaryButtonText = "Cancel";
            }

            PopUpDialog.Title = title;

            PopUpDialog.Content = content;

            if (prompt)
            {
                ContentControl contentControl = new ContentControl
                {
                    Content = content
                };
                StackPanel ContentStackPanel = new StackPanel();
                ContentStackPanel.Children.Add(contentControl);
                ContentStackPanel.Children.Add(new TextBox() { });
                PopUpDialog.Content = ContentStackPanel;
            }

            await PopUpDialog
                .ShowAsync();
        }
        #endregion

        public static void Vibrate(double timeMilliseconds)
        {
            string VibrateBool = localSettings.Values["vibrate"].ToString();
            if (VibrateBool == "1" &&
                ApiInformation.IsTypePresent("Windows.Phone.PhoneContract"))
            {
                var device = VibrationDevice.GetDefault();
                device.Vibrate(TimeSpan.FromMilliseconds(timeMilliseconds));
            }
        }

        private void InvokeKey(VirtualKey key)
        {
            InputInjector inputInjector = InputInjector.TryCreate();
            var info = new InjectedInputKeyboardInfo
            {
                VirtualKey = (ushort)(VirtualKey)key
            };
            inputInjector.InjectKeyboardInput(new[] { info });
        }

        public static async Task<StorageLibrary> TryAccessLibraryAsync(KnownLibraryId library)
        {
            try
            {
                return await StorageLibrary.GetLibraryAsync(library);
            }
            catch (UnauthorizedAccessException)
            {
                ContentDialog unauthorizedAccessDialog = new ContentDialog()
                {
                    Title = "Unauthorized Access",
                    Content = string.Format("\r\nTo access the {0} library, go to Settings and enable access. \r\nMake sure that changes has been saved.", library),
                    PrimaryButtonText = "OK"
                };

                await unauthorizedAccessDialog.ShowAsync();
            }
            return null;
        }

        public static string GetQueryVariable(string text, string variable)
        {
            string[] vars = text.Split('&');
            string[] pair = new string[0];
            string _pair = "";

            for (int i = 0; i < vars.Length; i++)
            {
                if (vars[i].StartsWith(variable))
                {
                    pair = vars[i].Split('=');
                }
            }

            for (int i = 0; i < pair.Length; i++)
            {
                if (i % 2 == 1 && pair[i] != variable)
                {
                    _pair = pair[i];
                }
            }

            return _pair;
        }

        public static async Task<string> GetUniqueName(string Header, StorageFolder folder)
        {
            int uniqueNum = 1;
            string uniqueStr = string.Empty;
            if (await folder.TryGetItemAsync(Header) == null)
            {
                return uniqueStr;
            }
            else
            {
                for (uniqueNum = 1; uniqueNum < 100; uniqueNum++)
                {
                    if (await folder.TryGetItemAsync(Header + " " + uniqueStr) == null)
                    {
                        break;
                    }
                    else
                    {
                        uniqueStr = string.Format("({0})", uniqueNum);
                    }
                }
            }

            return uniqueStr;
        }

        public static CoreWindow CurrentView
        {
            get
            {
                return CoreWindow.GetForCurrentThread();
            }
        }
    }
}
