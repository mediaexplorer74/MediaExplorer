using System;
using Windows.ApplicationModel;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace WEBVIEW
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            InitializeComponent();

            Loaded += (s, e) =>
            {
                LoadSettings();
                var v = Package.Current.Id.Version;
                VersionText.Text = $"Version: {v.Major}.{v.Minor}.{v.Build}.{v.Revision} (dev; pre-alpha)";
                SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Visible;
            };

            SystemNavigationManager.GetForCurrentView().BackRequested += (s, e) =>
            {
                if (Frame.CanGoBack)
                {
                    e.Handled = true;
                    Frame.GoBack();
                }
            };

            JsToggle.Toggled += (s, e) =>
            {
                try { if (MainPage.Current != null) MainPage.Current.JsEnabled = JsToggle.IsOn; } catch { }
            };

            SaveHomePageButton.Click += (s, e) =>
            {
                try
                {
                    var url = HomePageBox?.Text?.Trim() ?? string.Empty;
                    var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                    if (string.IsNullOrWhiteSpace(url))
                        settings.Values.Remove("HomePage");
                    else
                        settings.Values["HomePage"] = url;
                    if (MainPage.Current != null) MainPage.Current.UpdateStatusMessage("Home page saved.");
                }
                catch { }
            };

            AppBarModeCombo.SelectionChanged += (s, e) =>
            {
                if (AppBarModeCombo == null) return;
                int idx = AppBarModeCombo.SelectedIndex;
                string mode = idx == 0 ? "Full" : idx == 1 ? "Semi" : "Hided";
                SaveAppBarMode(mode);
                if (MainPage.Current != null) MainPage.Current.ApplyAppBarMode();
            };

            RenderModeCombo.SelectionChanged += (s, e) =>
            {
                if (RenderModeCombo == null) return;
                int idx = RenderModeCombo.SelectedIndex;
                string mode = idx == 0 ? "Full" : idx == 1 ? "Rich" : "Poor";
                SaveRenderMode(mode);
                if (MainPage.Current != null) MainPage.Current.RenderMode = mode;
            };

            ClearCacheButton.Click += (s, e) =>
            {
                try
                {
                    if (MainPage.Current != null) MainPage.Current.ClearResourceCache();
                }
                catch { }
            };

            DevToolsToggle.Toggled += (s, e) =>
            {
                try
                {
                    var on = DevToolsToggle.IsOn;
                    SaveDevToolsEnabled(on);
                    if (MainPage.Current != null) MainPage.Current.DevToolsEnabled = on;
                }
                catch { }
            };

            StatusBarToggle.Toggled += (s, e) =>
            {
                try
                {
                    var on = StatusBarToggle.IsOn;
                    SaveStatusBarVisible(on);
                    if (MainPage.Current != null) MainPage.Current.ApplyStatusBar();
                }
                catch { }
            };
        }

        private void LoadSettings()
        {
            try
            {
                // JavaScript
                if (JsToggle != null && MainPage.Current != null)
                    JsToggle.IsOn = MainPage.Current.JsEnabled;

                // Home page
                var hp = LoadHomePage();
                if (HomePageBox != null && !string.IsNullOrWhiteSpace(hp))
                    HomePageBox.Text = hp;

                // API key
                var key = LoadAiKey();
                if (AiKeyBox != null && !string.IsNullOrWhiteSpace(key))
                    AiKeyBox.Text = key;

                // AppBar mode
                if (AppBarModeCombo != null)
                {
                    var mode = LoadAppBarMode();
                    int idx = mode == "Full" ? 0 : mode == "Semi" ? 1 : 2;
                    AppBarModeCombo.SelectedIndex = idx;
                }

                // Render mode
                if (RenderModeCombo != null)
                {
                    var mode = LoadRenderMode();
                    int idx = mode == "Full" ? 0 : mode == "Rich" ? 1 : 2;
                    RenderModeCombo.SelectedIndex = idx;
                }

                // DevTools
                if (DevToolsToggle != null)
                    DevToolsToggle.IsOn = LoadDevToolsEnabled();

                // Status Bar
                if (StatusBarToggle != null)
                    StatusBarToggle.IsOn = LoadStatusBarVisible();
            }
            catch { }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            try
            {
                // Save API key on exit
                SaveAiKey(AiKeyBox?.Text?.Trim() ?? string.Empty);
            }
            catch { }

            try { SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed; } catch { }
            base.OnNavigatedFrom(e);
        }

        private static string LoadHomePage()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue("HomePage", out var val) && val is string s && !string.IsNullOrWhiteSpace(s))
                    return s;
            }
            catch { }
            return null;
        }

        private static string LoadAiKey()
        {
            try
            {
                var s = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (s.Values.TryGetValue("OpenRouterKey", out var v) && v is string key && !string.IsNullOrWhiteSpace(key))
                    return key;
            }
            catch { }
            return null;
        }

        private static void SaveAiKey(string key)
        {
            try
            {
                var s = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (string.IsNullOrWhiteSpace(key))
                    s.Values.Remove("OpenRouterKey");
                else
                    s.Values["OpenRouterKey"] = key;
            }
            catch { }
        }

        private static string LoadAppBarMode()
        {
            try
            {
                var s = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (s.Values.TryGetValue("AppBarMode", out var v) && v is string mode)
                    return mode;
            }
            catch { }
            return "Semi";
        }

        private static void SaveAppBarMode(string mode)
        {
            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["AppBarMode"] = mode;
            }
            catch { }
        }

        private static string LoadRenderMode()
        {
            try
            {
                var s = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (s.Values.TryGetValue("RenderMode", out var v) && v is string mode)
                    return mode;
            }
            catch { }
            return "Full";
        }

        private static void SaveRenderMode(string mode)
        {
            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["RenderMode"] = mode;
            }
            catch { }
        }

        private static bool LoadDevToolsEnabled()
        {
            try
            {
                var s = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (s.Values.TryGetValue("DevToolsEnabled", out var v) && v is bool b)
                    return b;
            }
            catch { }
            return false;
        }

        private static void SaveDevToolsEnabled(bool enabled)
        {
            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["DevToolsEnabled"] = enabled;
            }
            catch { }
        }

        private static bool LoadStatusBarVisible()
        {
            try
            {
                var s = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (s.Values.TryGetValue("StatusBarVisible", out var v) && v is bool b)
                    return b;
            }
            catch { }
            return true;
        }

        private static void SaveStatusBarVisible(bool visible)
        {
            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["StatusBarVisible"] = visible;
            }
            catch { }
        }
    }
}
