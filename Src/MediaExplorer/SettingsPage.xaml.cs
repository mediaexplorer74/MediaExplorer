using System;
using Windows.ApplicationModel;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using BrowserCore.Engine;

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

            // RenderModeCombo controls the e-book rendering profile:
            //   Rich   – Full graphics, CSS + JS (default)
            //   Poor   – Card/index style, minimal CSS, no images
            //   Asceti – Pure text, no CSS, no images (old e-book style)
            RenderModeCombo.SelectionChanged += (s, e) =>
            {
                if (RenderModeCombo == null) return;
                int idx = RenderModeCombo.SelectedIndex;
                string mode = idx == 0 ? "Rich" : idx == 1 ? "Poor" : "Asceti";
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

            // AI Connectors
            ActiveConnectorCombo.SelectionChanged += (s, e) =>
            {
                if (ActiveConnectorCombo == null) return;
                int idx = ActiveConnectorCombo.SelectedIndex;
                var type = idx == 0 ? ConnectorType.Ultra : idx == 1 ? ConnectorType.Rich :
                    idx == 2 ? ConnectorType.Poor : idx == 3 ? ConnectorType.Asceti : ConnectorType.Smart;
                ConnectorStorage.SaveActiveConnector(type);
            };
        }

        private void LoadSettings()
        {
            try
            {
                // Home page
                var hp = LoadHomePage();
                if (HomePageBox != null && !string.IsNullOrWhiteSpace(hp))
                    HomePageBox.Text = hp;

                // AppBar mode
                if (AppBarModeCombo != null)
                {
                    var mode = LoadAppBarMode();
                    int idx = mode == "Full" ? 0 : mode == "Semi" ? 1 : 2;
                    AppBarModeCombo.SelectedIndex = idx;
                }

                // E-book mode
                if (RenderModeCombo != null)
                {
                    var mode = LoadRenderMode();
                    int idx = mode == "Rich" ? 0 : mode == "Poor" ? 1 : 2;
                    RenderModeCombo.SelectedIndex = idx;
                }

                // DevTools
                if (DevToolsToggle != null)
                    DevToolsToggle.IsOn = LoadDevToolsEnabled();

                // Status Bar
                if (StatusBarToggle != null)
                    StatusBarToggle.IsOn = LoadStatusBarVisible();

                // AI Connectors
                LoadConnectorSettings();
            }
            catch { }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            try
            {
                SaveConnectorSettings();
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
                {
                    // Backward compat: "Full" maps to "Rich"
                    if (string.Equals(mode, "Full", StringComparison.OrdinalIgnoreCase))
                        return "Rich";
                    return mode;
                }
            }
            catch { }
            return "Rich";
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

        private void LoadConnectorSettings()
        {
            var defaults = AiConnectorConfig.GetDefaults();

            // Active connector (Ultra=0, Rich=1, Poor=2, Asceti=3, Smart=4)
            var active = ConnectorStorage.LoadActiveConnector();
            ActiveConnectorCombo.SelectedIndex = active == ConnectorType.Ultra ? 0 :
                active == ConnectorType.Rich ? 1 : active == ConnectorType.Poor ? 2 :
                active == ConnectorType.Asceti ? 3 : 4;

            // Ultra
            var ultra = ConnectorStorage.Load(ConnectorType.Ultra, defaults[0]);
            UltraEnabled.IsOn = ultra.Enabled;
            UltraApiKey.Text = ultra.ApiKey ?? "";
            UltraFamily.Text = ultra.ModelFamily ?? "anthropic";
            UltraModel.Text = ultra.ModelId ?? "claude-sonnet-4-20250514";
            UltraAutoFormat.IsOn = ultra.AutoFormat;

            // Rich
            var rich = ConnectorStorage.Load(ConnectorType.Rich, defaults[1]);
            RichEnabled.IsOn = rich.Enabled;
            RichApiKey.Text = rich.ApiKey ?? "";
            RichFamily.Text = rich.ModelFamily ?? "openai";
            RichModel.Text = rich.ModelId ?? "gpt-4o";
            RichAutoFormat.IsOn = rich.AutoFormat;

            // Poor
            var poor = ConnectorStorage.Load(ConnectorType.Poor, defaults[2]);
            PoorEnabled.IsOn = poor.Enabled;
            PoorApiKey.Text = poor.ApiKey ?? "";
            PoorFamily.Text = poor.ModelFamily ?? "mistralai";
            PoorModel.Text = poor.ModelId ?? "ministral-8b-2512";
            PoorAutoFormat.IsOn = poor.AutoFormat;

            // Asceti
            var asceti = ConnectorStorage.Load(ConnectorType.Asceti, defaults[3]);
            AscetiEnabled.IsOn = asceti.Enabled;
            AscetiApiKey.Text = asceti.ApiKey ?? "";
            AscetiFamily.Text = asceti.ModelFamily ?? "google";
            AscetiModel.Text = asceti.ModelId ?? "gemma-4-26b-a4b-it:free";
            AscetiAutoFormat.IsOn = asceti.AutoFormat;
        }

        private void SaveConnectorSettings()
        {
            var ultra = new AiConnectorConfig
            {
                Type = ConnectorType.Ultra, Name = "Ultra",
                ModelFamily = UltraFamily.Text?.Trim() ?? "anthropic",
                ModelId = UltraModel.Text?.Trim() ?? "claude-sonnet-4-20250514",
                IsOnline = true,
                Enabled = UltraEnabled.IsOn, ApiKey = UltraApiKey.Text?.Trim() ?? "",
                AutoFormat = UltraAutoFormat.IsOn, QualityThreshold = 1.0, MaxAttempts = 1, DailyBudgetUsd = 1.0
            };
            ConnectorStorage.Save(ultra);

            var rich = new AiConnectorConfig
            {
                Type = ConnectorType.Rich, Name = "Rich",
                ModelFamily = RichFamily.Text?.Trim() ?? "openai",
                ModelId = RichModel.Text?.Trim() ?? "gpt-4o",
                IsOnline = true,
                Enabled = RichEnabled.IsOn, ApiKey = RichApiKey.Text?.Trim() ?? "",
                AutoFormat = RichAutoFormat.IsOn, QualityThreshold = 0.8, MaxAttempts = 1, DailyBudgetUsd = 0.5
            };
            ConnectorStorage.Save(rich);

            var poor = new AiConnectorConfig
            {
                Type = ConnectorType.Poor, Name = "Poor",
                ModelFamily = PoorFamily.Text?.Trim() ?? "mistralai",
                ModelId = PoorModel.Text?.Trim() ?? "ministral-8b-2512",
                IsOnline = true,
                Enabled = PoorEnabled.IsOn, ApiKey = PoorApiKey.Text?.Trim() ?? "",
                AutoFormat = PoorAutoFormat.IsOn, QualityThreshold = 0.5, MaxAttempts = 1, DailyBudgetUsd = 0.1
            };
            ConnectorStorage.Save(poor);

            var asceti = new AiConnectorConfig
            {
                Type = ConnectorType.Asceti, Name = "Asceti",
                ModelFamily = AscetiFamily.Text?.Trim() ?? "google",
                ModelId = AscetiModel.Text?.Trim() ?? "gemma-4-26b-a4b-it:free",
                IsOnline = true,
                Enabled = AscetiEnabled.IsOn, ApiKey = AscetiApiKey.Text?.Trim() ?? "",
                AutoFormat = AscetiAutoFormat.IsOn, QualityThreshold = 0.3, MaxAttempts = 1, DailyBudgetUsd = 0.0
            };
            ConnectorStorage.Save(asceti);
        }
    }
}
