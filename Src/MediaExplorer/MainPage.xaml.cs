using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using BrowserCore.Engine;
using BrowserCore.Api;
using Windows.Web.Http;
using Windows.Storage;
using Windows.Data.Json;
using Windows.System;

namespace WEBVIEW
{
    public sealed partial class MainPage : Page
    {
        private bool _safeMode = false;
        private string _lastErrorMessage;
        private bool _messageOverlayVisible;
        private DispatcherTimer _toastTimer;
        public static MainPage Current { get; private set; }

        private readonly HttpClient _http = new HttpClient();
        private readonly ResourceManager _resources;
        private StorageFile _logFile; // Log file in Pictures/MediaExplorer/Logger.txt
        private readonly CustomHtmlEngine _welcomeEngine = new CustomHtmlEngine();
        private readonly BrowserHost _browser;
        private bool _barExpanded = false;
        private string _appBarMode = "Semi"; // "Full", "Semi", "Hided"

        // ═══ TEST URL ═══ Change this to test different sites ═══
        // Set to null/empty to use saved Home Page from Settings.
        // Examples:
        //   "https://news.ycombinator.com"     — Hacker News (simple)
        //   "https://en.m.wikipedia.org"        — Wikipedia mobile (tables)
        //   "https://developer.mozilla.org"     — MDN (flexbox-heavy)
        //   "https://getbootstrap.com"          — Bootstrap docs
        //   "https://github.com"                — GitHub (complex)
        private const string TEST_URL = "https://en.m.wikipedia.org/wiki/Main_Page";
        // ═════════════════════════════════════════════════════════

        // Card mode (Phase S/T) — narrow viewport card stack
        private bool _cardMode;
        private bool _showCategoryIndex;
        private string _filterCollectionId; // non-null → show only entries in this collection
        private int _currentCardIndex;
        private List<Dictionary<string, object>> _entryCards = new List<Dictionary<string, object>>();
        private List<Dictionary<string, object>> _backupEntryCards;
        private List<Dictionary<string, object>> _collectionMap = new List<Dictionary<string, object>>();
        private Dictionary<string, string> _collectionLookup = new Dictionary<string, string>();
        private List<Dictionary<string, object>> _storiesList = new List<Dictionary<string, object>>();
        private Dictionary<string, string> _keywordMap = new Dictionary<string, string>();

        private bool IsNarrowViewport
        {
            get
            {
                try
                {
                    double w = Windows.UI.ViewManagement.ApplicationView.GetForCurrentView().VisibleBounds.Width;
                    if (w <= 0) w = ContentArea?.ActualWidth ?? 800;
                    return w < 600;
                }
                catch { return false; }
            }
        }

        // Magic Bubble: long-tap triggers existing AI summary (reuses AiOverlay)
        private DateTime _holdingStart;

        // DevTools
        private bool _devToolsEnabled;
        private System.Text.StringBuilder _devConsoleBuffer = new System.Text.StringBuilder();
        private System.Text.StringBuilder _debugLogBuffer = new System.Text.StringBuilder();

        public bool DevToolsEnabled
        {
            get => _devToolsEnabled;
            set
            {
                _devToolsEnabled = value;
                Ui(() =>
                {
                    if (DevToolsPanel != null)
                        DevToolsPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                    if (DevToolsRow != null)
                        DevToolsRow.Height = value ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                    if (value && DevConsoleText != null && DevConsoleText.Blocks.Count == 0)
                        DevToolsLog("[DevTools] Console ready.");
                });
            }
        }

        private Uri _currentUri;
        private bool _welcomeShown;
        private FrameworkElement _activeVisual;
        private int _activeVisualIndex = -1;
        private int _renderSequence = 0;
        private string _startupStatusMessage;
        private bool _startupStatusPinned;
        private bool _suppressBarCollapse = false;
        private bool _suppressRepaintHandler = false;
        private bool _animatingBar = false;
        private string _lastFailedAddress;
        private string _pageTitle;
        private bool _loadProgressActive = false;
        private bool _navigationComplete;
        public MainPage()
        {
            InitializeComponent();
            Current = this;

            // Subscribe to DevToolsLogger to capture TestLogger and JS console output
            try
            {
                BrowserCore.Engine.DevToolsLogger.OnLog += msg =>
                {
                    if (_devToolsEnabled)
                    {
                        // Must dispatch to UI thread because OnLog can fire from background threads (ResourceManager, ModuleLoader)
                        Ui(() =>
                        {
                            try
                            {
                                AppendDevToolsLog(msg);
                                // Also buffer [DIAG] messages for Debug tab
                                if (msg.StartsWith("[DIAG]"))
                                {
                                    _debugLogBuffer.AppendLine(msg);
                                    // Limit buffer size
                                    if (_debugLogBuffer.Length > 50000)
                                        _debugLogBuffer.Clear();
                                }
                            }
                            catch { }
                        });
                    }
                };
            }
            catch { }

            if (GoButton != null) GoButton.Click += GoButton_Click;
            if (Omnibox != null) Omnibox.KeyDown += Omnibox_KeyDown;
            if (BackButton != null) BackButton.Click += BackButton_Click;
            if (ForwardButton != null) ForwardButton.Click += ForwardButton_Click;
            if (SettingsButton != null) SettingsButton.Click += SettingsButton_Click;
            if (AiButton != null) AiButton.Click += AiButton_Click;
            if (SnapshotButton != null) SnapshotButton.Click += SnapshotButton_Click;
            if (CopyButton != null) CopyButton.Click += CopyButton_Click;
            if (AiCloseButton != null) AiCloseButton.Click += AiCloseButton_Click;
            if (AiCopyButton != null) AiCopyButton.Click += AiCopyButton_Click;
            if (ReaderCloseButton != null) ReaderCloseButton.Click += ReaderCloseButton_Click;
            if (ContentArea != null) ContentArea.ManipulationDelta += ContentArea_ManipulationDelta;

            // Hardware/software Back button � browser navigation
            try
            {
                SystemNavigationManager.GetForCurrentView().BackRequested += (s, e) =>
                {
                    try
                    {
                        if (Frame == null || Frame.CurrentSourcePageType != typeof(MainPage)) return;
                        if (_browser != null && _browser.CanGoBack)
                        {
                            _browser.GoBack();
                            e.Handled = true;
                            UpdateNavButtons();
                        }
                    }
                    catch { }
                };
            }
            catch { }

            // Bottom bar � mouse & touch
            if (BarStrip != null)
            {
                BarStrip.Tapped += BarStrip_Tapped;
                BarStrip.PointerEntered += (s, e) => Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.Hand, 0);
                BarStrip.PointerExited += (s, e) => Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.Arrow, 0);
                BarStrip.ManipulationMode = ManipulationModes.TranslateY;
                BarStrip.ManipulationDelta += BarStrip_ManipulationDelta;
            }
            if (BottomBar != null)
            {
                BottomBar.ManipulationMode = ManipulationModes.TranslateY;
                BottomBar.ManipulationDelta += BarStrip_ManipulationDelta;
            }

            // Keyboard shortcuts: Ctrl+L = URL focus+expand, Ctrl+B = toggle bar
            Window.Current.CoreWindow.KeyDown += (s, e) =>
            {
                try
                {
                    var ctrl = (Window.Current.CoreWindow.GetAsyncKeyState(Windows.System.VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
                    if (ctrl && e.VirtualKey == Windows.System.VirtualKey.L)
                    {
                        ExpandBar();
                        if (Omnibox != null) { Omnibox.Focus(FocusState.Programmatic); Omnibox.SelectAll(); }
                    }
                    if (ctrl && e.VirtualKey == Windows.System.VirtualKey.B)
                    {
                        ToggleBar();
                    }
                }
                catch { }
            };

            // Clip bottom bar so content doesn't overflow when collapsed
            if (BottomBar != null)
                BottomBar.SizeChanged += (s, e) =>
                {
                    try { if (!_animatingBar) BottomBar.Clip = new RectangleGeometry { Rect = new Rect(0, 0, BottomBar.ActualWidth, BottomBar.Height) }; }
                    catch { }
                };

            // Tap on content area to collapse bar
            if (ContentArea != null)
                ContentArea.Tapped += (s, e) => CollapseBar();

            // Magic Bubble: long-tap / long-press on content area
            if (ContentArea != null)
                ContentArea.Holding += ContentArea_Holding;

            _resources = new ResourceManager(_http);

            try { if (ContentArea != null) JavaScriptEngine.RegisterVisualRoot(ContentArea); } catch { }
            try { if (ContentArea != null) ContentArea.Background = new SolidColorBrush(Windows.UI.Colors.White); } catch { }

            _welcomeEngine.EnableJavaScript = true;
            _welcomeEngine.RepaintReady += Engine_RepaintReady;
            _welcomeEngine.LoadingChanged += (s, loading) => Ui(() =>
            {
                if (LoadingRing != null) LoadingRing.IsActive = loading;
                if (LoadingOverlay != null) LoadingOverlay.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            });

            _browser = new BrowserHost();
            _browser.Navigated += (s, uri) => Ui(() => { UpdateCurrentLocation(uri); UpdateNavButtons(); });
            _browser.NavigationFailed += (s, msg) => Ui(() => ShowGlobalError(msg));
            _browser.StatusMessage += (s, msg) =>
            {
                System.Diagnostics.Debug.WriteLine("[DIAG:OVL] StatusMessage msg=" + (msg ?? "null") + " pinned=" + _startupStatusPinned);
                if (msg == "Loaded.")
                {
                    UpdateStatusMessage("Loaded.", overrideStartup: true);
                    Ui(() =>
                    {
                        System.Diagnostics.Debug.WriteLine("[DIAG:OVL] Hiding overlay");
                        if (LoadingRing != null) LoadingRing.IsActive = false;
                        if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Collapsed;
                    });
                }
                else
                {
                    UpdateStatusMessage(msg);
                }
            };
            _browser.LoadingChanged += (s, loading) => Ui(() =>
            {
                if (loading && _navigationComplete) return;
                _navigationComplete = !loading;
                if (LoadingRing != null) LoadingRing.IsActive = loading;
                if (LoadingOverlay != null) LoadingOverlay.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
                if (loading)
                {
                    if (LoadProgressBar != null)
                    {
                        LoadProgressBar.Visibility = Visibility.Visible;
                        LoadProgressBar.Width = 0;
                        LoadProgressBar.Opacity = 1;
                        AnimateLoadProgress();
                    }
                }
                else
                {
                    FadeOutLoadProgress();
                }
            });
            _browser.RepaintReady += (s, element) => Engine_RepaintReady(element);
            _browser.NodeTapped += (id, name, type) => Ui(async () =>
            {
                var dialog = new ContentDialog
                {
                    Title = name ?? id,
                    Content = $"Type: {type}\nID: {id}",
                    CloseButtonText = "OK"
                };
                System.Diagnostics.Debug.WriteLine("[DIAG:NODE] dialog id=" + id + " name=" + name + " type=" + type);
                await dialog.ShowAsync();
            });

            SizeChanged += MainPage_SizeChanged;

            Loaded += (s, e) => { try { ApplyAppBarMode(); ApplyRenderMode(); ApplyDevTools(); ApplyStatusBar(); ApplyButtonVisibility(); } catch { } };
            try { ApplyAppBarMode(); } catch { }
            try { ApplyRenderMode(); } catch { }
            try { ApplyDevTools(); } catch { }
            try { ApplyStatusBar(); } catch { }

            Task.Run(() => RunNilJsStartupTest());
            try { this.NavigationCacheMode = NavigationCacheMode.Required; } catch { }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            try
            {
                var folder = Windows.Storage.ApplicationData.Current.LocalFolder;
                var file = await folder.CreateFileAsync("OnNavTest.txt", CreationCollisionOption.ReplaceExisting);
                await Windows.Storage.FileIO.WriteTextAsync(file, "OnNavigatedTo OK");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("NAVTEST FAIL: " + ex.Message);
            }

            // Priority: TEST_URL constant > launch args > env var > saved home page
            if (!string.IsNullOrWhiteSpace(TEST_URL))
            {
                var _ = NavigateAsync(TEST_URL);
                return;
            }

            var launchUrl = e.Parameter as string;
            if (string.IsNullOrWhiteSpace(launchUrl))
            {
                try { launchUrl = Environment.GetEnvironmentVariable("MEDIAEXPLORER_STARTUP_URL"); } catch { }
            }
            if (!string.IsNullOrWhiteSpace(launchUrl))
            {
                var _ = NavigateAsync(launchUrl);
                return;
            }
            if (!_welcomeShown)
            {
                if (!_startupStatusPinned)
                    UpdateStatusMessage("Enter a URL and press Go.");
                _welcomeShown = true;
                var homePage = LoadHomePage();
                if (!string.IsNullOrWhiteSpace(homePage))
                {
                    var _ = NavigateAsync(homePage);
                }
                else
                {
                    var _ = ShowWelcomeAsync();
                }
            }
        }

        // ========== Bottom Bar expand/collapse ==========

        private void ExpandBar()
        {
            if (_barExpanded) return;
            _barExpanded = true;
            if (BarStrip != null) BarStrip.IsHitTestVisible = false;
            if (BarContent != null) BarContent.IsHitTestVisible = true;
            AnimateBarHeight(52);
        }

        private void CollapseBar()
        {
            if (!_barExpanded || _suppressBarCollapse) return;
            if (_appBarMode == "Full") return;
            _barExpanded = false;
            double target = _appBarMode == "Hided" ? 6 : 24;
            if (BarStrip != null) BarStrip.IsHitTestVisible = true;
            if (BarContent != null) BarContent.IsHitTestVisible = false;
            AnimateBarHeight(target);
        }

        // --- Magic Bubble (long-tap triggers existing AI summary) ---

        private void ContentArea_Holding(object sender, HoldingRoutedEventArgs e)
        {
            if (e.HoldingState == Windows.UI.Input.HoldingState.Started)
            {
                _holdingStart = DateTime.Now;
            }
            else if (e.HoldingState == Windows.UI.Input.HoldingState.Completed)
            {
                var elapsed = DateTime.Now - _holdingStart;
                if (elapsed.TotalMilliseconds >= 500)
                {
                    // Long press � reuse existing AiOverlay + OpenRouter summary
                    AiButton_Click(null, null);
                }
            }
        }

        private void ToggleBar()
        {
            if (_barExpanded) CollapseBar();
            else ExpandBar();
        }

        private void UpdateBarClip()
        {
            try
            {
                if (BottomBar != null)
                {
                    double w = BottomBar.ActualWidth;
                    if (w <= 0) try { w = Window.Current.Bounds.Width; } catch { w = 360; }
                    BottomBar.Clip = new RectangleGeometry { Rect = new Rect(0, 0, w, BottomBar.Height) };
                }
            }
            catch { }
        }

        // V.1 — Smooth Height animation with cubic ease out (avoids Storyboard layout conflict)
        private async void AnimateBarHeight(double targetHeight)
        {
            if (_animatingBar)
            {
                BottomBar.Height = targetHeight;
                UpdateBarClip();
                return;
            }
            _animatingBar = true;
            try
            {
                double startHeight = BottomBar.Height;
                if (Math.Abs(startHeight - targetHeight) < 0.5) return;
                int steps = 10;
                for (int i = 1; i <= steps; i++)
                {
                    double t = (double)i / steps;
                    t = 1 - Math.Pow(1 - t, 3);
                    double h = startHeight + (targetHeight - startHeight) * t;
                    BottomBar.Height = h;
                    UpdateBarClip();
                    await Task.Delay(15);
                }
                BottomBar.Height = targetHeight;
                UpdateBarClip();
            }
            finally { _animatingBar = false; }
        }

        // V.2 — Progress bar fill animation (stops at ~80%, jumps to 100% on complete)
        private async void AnimateLoadProgress()
        {
            _loadProgressActive = true;
            if (LoadProgressBar == null || ContentArea == null) return;
            double maxWidth = ContentArea.ActualWidth;
            if (maxWidth <= 0) try { maxWidth = Window.Current.Bounds.Width; } catch { maxWidth = 360; }
            for (int i = 1; _loadProgressActive && i <= 40; i++)
            {
                double t = (double)i / 40;
                double fill = 0.03 + 0.77 * (1 - Math.Pow(1 - t, 2));
                LoadProgressBar.Width = maxWidth * fill;
                await Task.Delay(50);
            }
        }

        private async void FadeOutLoadProgress()
        {
            _loadProgressActive = false;
            if (LoadProgressBar == null) return;
            await Task.Delay(200);
            if (_loadProgressActive) return;
            try { if (ContentArea != null) LoadProgressBar.Width = ContentArea.ActualWidth; } catch { }
            await Task.Delay(100);
            int steps = 8;
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                LoadProgressBar.Opacity = 1 - t;
                await Task.Delay(25);
            }
            LoadProgressBar.Visibility = Visibility.Collapsed;
            LoadProgressBar.Opacity = 1;
        }

        private void BarStrip_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ToggleBar();
            e.Handled = true;
        }

        private void BarStrip_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            if (e.Delta.Translation.Y < -20)
            {
                ExpandBar();
                e.Complete();
            }
            else if (e.Delta.Translation.Y > 20)
            {
                CollapseBar();
                e.Complete();
            }
        }

        // ========== UI helpers ==========

        private void Ui(Action action)
        {
            try
            {
                var disp = UiThreadHelper.TryGetDispatcher();
                if (disp != null && !UiThreadHelper.HasThreadAccess(disp))
                    UiThreadHelper.RunAsync(disp, CoreDispatcherPriority.Normal, () => { try { action(); } catch { } });
                else action();
            }
            catch { }
        }

        private async Task UiAsync(Action action)
        {
            try
            {
                var disp = UiThreadHelper.TryGetDispatcher();
                if (disp != null && !UiThreadHelper.HasThreadAccess(disp))
                    await UiThreadHelper.RunAsyncAwaitable(disp, CoreDispatcherPriority.Normal, () => { try { action(); } catch { } });
                else action();
            }
            catch { }
        }

        private void SetStartupStatus(string message)
        {
            System.Diagnostics.Debug.WriteLine("[DIAG:OVL] SetStartupStatus msg=" + (message ?? "null") + " pinned=true");
            _startupStatusMessage = message ?? string.Empty;
            _startupStatusPinned = true;
            Ui(() =>
            {
                try
                {
                    if (StatusText != null) StatusText.Text = _startupStatusMessage;
                    if (OverlayStatusText != null) OverlayStatusText.Text = _startupStatusMessage;
                    if (_startupStatusMessage.IndexOf("passed", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Collapsed;
                        if (LoadingRing != null) LoadingRing.IsActive = false;
                    }
                }
                catch { }
            });
        }

        public void UpdateStatusMessage(string message, bool overrideStartup = false)
        {
            if (!overrideStartup && _startupStatusPinned) { System.Diagnostics.Debug.WriteLine("[DIAG:OVL] UpdateStatusMessage BLOCKED pinned=true msg=" + (message ?? "null")); return; }
            Ui(() =>
            {
                try
                {
                    var text = message ?? string.Empty;
                    if (StatusText != null) StatusText.Text = text;
                    if (OverlayStatusText != null) OverlayStatusText.Text = text;
                }
                catch { }
            });
            if (overrideStartup) { _startupStatusPinned = false; _startupStatusMessage = null; }
        }

        private void UpdateCurrentLocation(Uri uri)
        {
            _currentUri = uri;
            if (uri == null) return;
            Ui(() => { try { if (Omnibox != null) Omnibox.Text = uri.AbsoluteUri; } catch { } });
        }

        private static bool LooksLikeUrl(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s.IndexOf(' ') >= 0) return false;
            if (s.Contains(".")) return true;
            if (s.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            byte b;
            if (s.Length > 1 && byte.TryParse(s.Split('.')[0], out b)) return true;
            return false;
        }

        private static bool TryParseEntryUrl(string url, out string entryId)
        {
            entryId = null;
            if (string.IsNullOrWhiteSpace(url)) return false;
            try
            {
                Uri uri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
                {
                    // Relative URL: try /entry/E0001 pattern
                    if (url.StartsWith("/entry/", StringComparison.OrdinalIgnoreCase))
                    {
                        string seg = url.Substring(7).Trim('/').Split('/')[0].Trim();
                        if (seg.Length > 0 && seg[0] == 'E')
                        {
                            entryId = seg;
                            return true;
                        }
                    }
                    return false;
                }
                // Absolute URL: check path for /entry/E0001
                string path = uri.AbsolutePath ?? "";
                if (path.StartsWith("/entry/", StringComparison.OrdinalIgnoreCase))
                {
                    string seg = path.Substring(7).Trim('/').Split('/')[0].Trim();
                    if (seg.Length > 0 && seg[0] == 'E')
                    {
                        entryId = seg;
                        return true;
                    }
                }
                // Check query param ?entry=E0001
                string query = uri.Query ?? "";
                if (query.Contains("entry="))
                {
                    var parts = query.TrimStart('?').Split('&');
                    for (int i = 0; i < parts.Length; i++)
                    {
                        var kv = parts[i].Split('=');
                        if (kv.Length == 2 && string.Equals(kv[0], "entry", StringComparison.OrdinalIgnoreCase))
                        {
                            string val = Uri.UnescapeDataString(kv[1]).Trim();
                            if (val.Length > 0 && val[0] == 'E')
                            {
                                entryId = val;
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static string BuildSearchUrl(string query)
        {
            var q = Uri.EscapeDataString(query ?? string.Empty);
            return "https://www.google.com/search?q=" + q + "&hl=en";
        }

        private string GetAddressFromUI()
        {
            try { if (Omnibox != null && !string.IsNullOrWhiteSpace(Omnibox.Text)) return Omnibox.Text.Trim(); } catch { }
            return string.Empty;
        }

        private async Task NavigateAsync(string address)
        {
            _lastFailedAddress = address;
            System.Diagnostics.Debug.WriteLine("[DIAG] MainPage.NavigateAsync START seq=" + _renderSequence + " address=" + address);
            CollapseBar();
            if (string.IsNullOrWhiteSpace(address))
            {
                UpdateStatusMessage("Enter a URL.");
                return;
            }

            // Phase T: Link routing — intercept entry URLs to show in card mode
            string entryId;
            if (TryParseEntryUrl(address, out entryId))
            {
                System.Diagnostics.Debug.WriteLine("[DIAG:CARD] Intercepted entry URL: " + address + " -> id=" + entryId);
                if (_entryCards.Count > 0 || TryLoadEntryCards())
                {
                    for (int i = 0; i < _entryCards.Count; i++)
                    {
                        if (DictStr(_entryCards[i], "id", "") == entryId)
                        {
                            _cardMode = true;
                            _showCategoryIndex = false;
                            _filterCollectionId = null;
                            _backupEntryCards = null;
                            _currentCardIndex = i;
                            ResetContentHost();
                            Ui(() =>
                            {
                                ContentHost.Children.Clear();
                                ContentHost.Children.Add(BuildCardPanel());
                            });
                            UpdateStatusMessage("");
                            return;
                        }
                    }
                }
                // Entry not found in loaded data — fall through to normal navigation
            }

            // Phase T: External link routing — open in system browser when in card mode
            if (_cardMode && !string.IsNullOrEmpty(address))
            {
                Uri extUri;
                if (Uri.TryCreate(address, UriKind.Absolute, out extUri))
                {
                    string host = extUri.Host?.ToLowerInvariant() ?? "";
                    if (!host.Contains("nokiadesignarchive") && !host.Contains("aalto.fi"))
                    {
                        System.Diagnostics.Debug.WriteLine("[DIAG:CARD] External link, launching: " + address);
                        await Launcher.LaunchUriAsync(extUri);
                        return;
                    }
                }
            }

            // about:scheme routing
            if (address.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                var aboutPage = address.Substring(6).Trim().ToLowerInvariant();
                if (aboutPage == "test")
                {
                    address = "ms-appx:///Html/test.html";
                }
                else if (aboutPage == "blank")
                {
                    ResetContentHost();
                    UpdateStatusMessage("about:blank");
                    return;
                }
                else
                {
                    UpdateStatusMessage("Unknown about: page � " + address);
                    return;
                }
            }

            if (!address.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                !address.StartsWith("file", StringComparison.OrdinalIgnoreCase) &&
                !address.StartsWith("ms-appx", StringComparison.OrdinalIgnoreCase))
            {
                var raw = address.Trim();
                address = LooksLikeUrl(raw) ? ("https://" + raw) : BuildSearchUrl(raw);
            }
            ClearStartupStatus();
            ResetContentHost();
            try
            {
                await _browser.NavigateAsync(address);
            }
            catch (Exception ex)
            {
                try { System.Diagnostics.Debug.WriteLine("[DIAG] MainPage.NavigateAsync EXCEPTION: " + ex.GetType().Name + ": " + ex.Message); } catch { }
                ShowGlobalError("Navigation failed: " + ex.Message);
            }
            System.Diagnostics.Debug.WriteLine("[DIAG] MainPage.NavigateAsync DONE");
        }

        private async Task ShowWelcomeAsync()
        {
            Ui(() =>
            {
                try
                {
                    if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Visible;
                    if (LoadingRing != null) LoadingRing.IsActive = true;
                    if (ContentHost != null) ContentHost.Children.Clear();
                    _activeVisual = null; _activeVisualIndex = -1;
                    if (_safeMode) { _welcomeEngine.SafeMode = true; _welcomeEngine.ApplySafeMode(); }
                }
                catch { }
            });
            try
            {
                var baseUri = new Uri("ms-appx:///Html/welcome.html");
                var html = await LoadTextFromAppxAsync(baseUri);
                if (string.IsNullOrWhiteSpace(html))
                {
                    html = "<!doctype html><html><head><meta charset='utf-8'><title>Welcome</title></head><body style='font-family:Segoe UI,Arial;padding:12px;'><h1>Welcome</h1><p>Type a URL in the bar above and press Enter.</p></body></html>";
                    baseUri = new Uri("about:blank");
                }

                Func<Uri, Task<string>> fetchCss = async (uri) =>
                {
                    if (uri == null) return string.Empty;
                    if (string.Equals(uri.Scheme, "ms-appx", StringComparison.OrdinalIgnoreCase))
                        return await LoadTextFromAppxAsync(uri);
                    try { return await _resources.FetchTextAsync(uri, null, "text/css", "style") ?? string.Empty; }
                    catch { return string.Empty; }
                };
                _welcomeEngine.ScriptFetcher = async (uri) =>
                {
                    if (uri == null) return string.Empty;
                    if (string.Equals(uri.Scheme, "ms-appx", StringComparison.OrdinalIgnoreCase))
                        return await LoadTextFromAppxAsync(uri);
                    try { return await _resources.FetchTextAsync(uri, null, "application/javascript", "script") ?? string.Empty; }
                    catch { return string.Empty; }
                };

                Func<Uri, Task<Windows.Storage.Streams.IRandomAccessStream>> imgLoader = async (uri) =>
                {
                    if (uri == null) return null;
                    try { return await _resources.FetchImageAsync(uri, baseUri); }
                    catch { return null; }
                };
                Action<Uri> onNavigate = (u) => { var _ = NavigateAsync(u.AbsoluteUri); };
                double vw = 0; try { vw = ContentHost.ActualWidth; } catch { }
                if (vw <= 0) { try { vw = Window.Current.Bounds.Width - 16; } catch { vw = 360; } }
                _suppressRepaintHandler = true;
                var elt = await _welcomeEngine.RenderAsync(html, baseUri, fetchCss, imgLoader, onNavigate, vw);
                _suppressRepaintHandler = false;
                if (elt == null || IsEffectivelyEmpty(elt) || ContentHost == null || ContentHost.Children == null)
                {
                    var sp = new StackPanel { Margin = new Thickness(24) };
                    sp.Children.Add(new TextBlock { Text = "MediaExplorer", FontSize = 26, FontWeight = Windows.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Windows.UI.Colors.White) });
                    sp.Children.Add(new TextBlock { Text = "Type a URL in the bar above and press Enter.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Foreground = new SolidColorBrush(Windows.UI.Colors.Gray) });
                    elt = new Border { Background = new SolidColorBrush(Color.FromArgb(255, 26, 26, 26)), Child = sp };
                }
                Ui(() =>
                {
                    try
                    {
                        ContentHost.Children.Add(elt);
                        _activeVisual = elt;
                        _activeVisualIndex = ContentHost.Children.IndexOf(elt);
                    }
                    catch { }
                });
            }
            catch { }
            finally
            {
                Ui(() =>
                {
                    try { if (LoadingRing != null) LoadingRing.IsActive = false; if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Collapsed; }
                    catch { }
                });
            }
            if (!_startupStatusPinned) UpdateStatusMessage("Ready.");
        }

        private void Engine_RepaintReady(FrameworkElement element)
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "EngineRepaintReady.txt"), "ENTERED\r\n"); } catch { }
            DevToolsLogger.Log("[DIAG:CARD] Engine_RepaintReady ENTERED");
            if (element == null) { System.Diagnostics.Debug.WriteLine("[DIAG] Engine_RepaintReady SKIP element=null"); DevToolsLogger.Log("[DIAG:CARD] Engine_RepaintReady SKIP element=null"); return; }
            if (_suppressRepaintHandler) { System.Diagnostics.Debug.WriteLine("[DIAG] Engine_RepaintReady SKIP suppressed"); DevToolsLogger.Log("[DIAG:CARD] Engine_RepaintReady SKIP suppressed"); return; }
            int seq = _renderSequence;
            // Phase DIAG (issue E): this fires BEFORE the element is added to the
            // visual tree, so Width/Height are *explicit* (NaN if not set) and
            // ActualWidth/ActualHeight are *0* (no layout pass yet). The "0x0"
            // reading here is meaningless — the element will be measured once
            // ContentHost.Children.Add(element) runs. Log both numbers and a
            // POST-MEASURE reading (one render-tick later) for comparison.
            double wExp = double.NaN, hExp = double.NaN, wAct = 0, hAct = 0;
            try { wExp = element.Width; hExp = element.Height; } catch { }
            try { wAct = element.ActualWidth; hAct = element.ActualHeight; } catch { }
            System.Diagnostics.Debug.WriteLine(
                "[DIAG:REPAINT-PRE] seq=" + seq + " type=" + element.GetType().Name +
                " explicit=(" + (double.IsNaN(wExp) ? "NaN" : wExp.ToString()) + "x" + (double.IsNaN(hExp) ? "NaN" : hExp.ToString()) + ")" +
                " actual=(" + wAct + "x" + hAct + ")" +
                " → this is PRE-MEASURE; will re-check after Add()");
            // Also schedule a post-measure read so we can see the real size.
            // We post to DispatcherQueue at Background priority to give the layout
            // system a chance to measure us. Use a fresh local copy of element.
            var elementRef = element;
            try
            {
                if (Windows.ApplicationModel.Core.CoreApplication.MainView?.Dispatcher != null)
                {
                    Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.RunAsync(
                        Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                        {
                            try
                            {
                                double w2 = elementRef.ActualWidth, h2 = elementRef.ActualHeight;
                                System.Diagnostics.Debug.WriteLine(
                                    "[DIAG:REPAINT-POST] type=" + elementRef.GetType().Name +
                                    " actual=(" + w2 + "x" + h2 + ")");
                            }
                            catch { }
                        });
                }
            }
            catch { }
            // Use the same NaN-guarded numbers for backward compat (the old log
            // line). Prefer ActualWidth/ActualHeight when explicit is NaN.
            double w = 0, h = 0;
            if (!double.IsNaN(wExp) && !double.IsInfinity(wExp)) w = wExp;
            if (!double.IsNaN(hExp) && !double.IsInfinity(hExp)) h = hExp;
            if (double.IsNaN(w) || double.IsInfinity(w)) w = wAct;
            if (double.IsNaN(h) || double.IsInfinity(h)) h = hAct;
            // Final NaN/Inf guard
            if (double.IsNaN(w) || double.IsInfinity(w)) w = 0;
            if (double.IsNaN(h) || double.IsInfinity(h)) h = 0;
            // Keep the existing [DIAG] line format for backward compat (other tools may filter it)
            System.Diagnostics.Debug.WriteLine("[DIAG] Engine_RepaintReady seq=" + seq + " currentSeq=" + _renderSequence + " elementSize=" + w + "x" + h + " type=" + element.GetType().Name);
            Ui(() =>
            {
                try
                {
                    DevToolsLogger.Log("[DIAG:CARD] Ui(()=>...) block executing seq=" + seq + " renderSeq=" + _renderSequence);
                    if (ContentHost == null) { System.Diagnostics.Debug.WriteLine("[DIAG] Engine_RepaintReady SKIP ContentHost=null"); DevToolsLogger.Log("[DIAG:CARD] SKIP ContentHost=null"); return; }
                    if (seq != _renderSequence) { System.Diagnostics.Debug.WriteLine("[DIAG] Engine_RepaintReady SKIP stale seq=" + seq + " current=" + _renderSequence); DevToolsLogger.Log("[DIAG:CARD] SKIP stale seq=" + seq + " current=" + _renderSequence); return; }
                    ContentHost.Children.Clear();
                    DevToolsLogger.Log("[DIAG:CARD] About to call TryLoadEntryCards");
                    if (TryLoadEntryCards())
                    {
                        ShowCardMode(true);
                        System.Diagnostics.Debug.WriteLine("[DIAG:CARD] Card mode activated with " + _entryCards.Count + " entries");
                    }
                    else
                    {
                        _cardMode = false;
                        ContentHost.Children.Add(element);
                        _activeVisual = element;
                        _activeVisualIndex = ContentHost.Children.IndexOf(element);
                        System.Diagnostics.Debug.WriteLine("[DIAG] Engine_RepaintReady ADDED idx=" + _activeVisualIndex + " children=" + ContentHost.Children.Count);
                        ApplyAppBarMode();
                        if (string.Equals(_browser.RenderMode, "Rich", StringComparison.OrdinalIgnoreCase) && !_suppressRepaintHandler)
                            StartReadingMode();
                    }
                }
                catch (Exception ex) { var m = "[DIAG:CARD] Engine_RepaintReady EXC " + ex.GetType().Name + ": " + ex.Message; System.Diagnostics.Debug.WriteLine(m); DevToolsLogger.Log(m); }
            });
        }

        private void ResetContentHost()
        {
            _renderSequence++;
            System.Diagnostics.Debug.WriteLine("[DIAG] ResetContentHost seq=" + _renderSequence);
            Ui(() =>
            {
                try { if (ContentHost != null) { ContentHost.Children.Clear(); System.Diagnostics.Debug.WriteLine("[DIAG] ResetContentHost CLEARED children=" + ContentHost.Children.Count); } _activeVisual = null; _activeVisualIndex = -1; }
                catch { }
            });
        }

        // ─── Phase S: Card mode ───────────────────────────────────────

        private bool TryLoadEntryCards()
        {
            DevToolsLogger.Log("[DIAG:CARD] TryLoadEntryCards STARTED");
            try
            {
                string json = _browser.ExtractEntriesJson();
                if (string.IsNullOrEmpty(json) || json == "null") return false;

                // Debug: save raw JSON to diagnose parse failures
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "DebugEntries.json"), json); } catch { }

                // Try parsing; if Windows.Data.Json fails, attempt to clean control chars
                Windows.Data.Json.JsonArray arr = null;
                try { arr = Windows.Data.Json.JsonArray.Parse(json); }
                catch
                {
                    // Strip control characters (U+0000-U+001F except \t \n \r) that Windows.Data.Json rejects
                    var cleaned = System.Text.RegularExpressions.Regex.Replace(json, @"[\u0000-\u0008\u000b\u000c\u000e-\u001f]", "");
                    arr = Windows.Data.Json.JsonArray.Parse(cleaned);
                }
                _entryCards.Clear();
                _collectionMap.Clear();
                _collectionLookup.Clear();
                _storiesList.Clear();

                // Also try to load collections for name resolution
                try
                {
                    string colJson = _browser.ExtractCollectionsJson();
                    if (!string.IsNullOrEmpty(colJson) && colJson != "null")
                    {
                        var colArr = Windows.Data.Json.JsonArray.Parse(colJson);
                        for (int ci = 0; ci < colArr.Count; ci++)
                        {
                            var co = colArr[ci].GetObject();
                            var map = new Dictionary<string, object>();
                            foreach (var kv in co) map[kv.Key] = CoerceJson(kv.Value);
                            _collectionMap.Add(map);
                            // Build lookup: id → title
                            string cid = DictStr(map, "id");
                            string cname = DictStr(map, "title", DictStr(map, "name", cid));
                            if (!string.IsNullOrEmpty(cid)) _collectionLookup[cid] = cname;
                        }
                    }
                }
                catch { }

                // Load stories data
                try
                {
                    string stJson = _browser.ExtractStoriesJson();
                    if (!string.IsNullOrEmpty(stJson) && stJson != "null")
                    {
                        var stArr = Windows.Data.Json.JsonArray.Parse(stJson);
                        for (int si = 0; si < stArr.Count; si++)
                        {
                            var so = stArr[si].GetObject();
                            var story = new Dictionary<string, object>();
                            foreach (var kv in so) story[kv.Key] = CoerceJson(kv.Value);
                            _storiesList.Add(story);
                        }
                    }
                }
                catch { }

                // Load keywords data
                try
                {
                    _keywordMap.Clear();
                    string kwJson = _browser.ExtractKeywordsJson();
                    if (!string.IsNullOrEmpty(kwJson) && kwJson != "null")
                    {
                        var kwArr = Windows.Data.Json.JsonArray.Parse(kwJson);
                        for (int ki = 0; ki < kwArr.Count; ki++)
                        {
                            var ko = kwArr[ki].GetObject();
                            string kid = "", kname = "";
                            foreach (var kv in ko)
                            {
                                if (kv.Key == "id") kid = kv.Value.GetString();
                                else if (kv.Key == "name" || kv.Key == "title") kname = kv.Value.GetString();
                            }
                            if (!string.IsNullOrEmpty(kid) && !string.IsNullOrEmpty(kname))
                                _keywordMap[kid] = kname;
                        }
                    }
                }
                catch { }

                for (int i = 0; i < arr.Count; i++)
                {
                    var obj = arr[i].GetObject();
                    var card = new Dictionary<string, object>();
                    foreach (var kv in obj) card[kv.Key] = CoerceJson(kv.Value);
                    _entryCards.Add(card);
                }

                string loadMsg = "[DIAG:CARD] TryLoadEntryCards SUCCESS entries=" + _entryCards.Count + " collections=" + _collectionMap.Count + " stories=" + _storiesList.Count + " keywords=" + _keywordMap.Count;
                System.Diagnostics.Debug.WriteLine(loadMsg);
                DevToolsLogger.Log(loadMsg);
                return _entryCards.Count > 0;
            }
            catch (Exception ex)
            {
                string excMsg = "[DIAG:CARD] TryLoadEntryCards EXC: " + ex.GetType().Name + ": " + ex.Message;
                System.Diagnostics.Debug.WriteLine(excMsg);
                DevToolsLogger.Log(excMsg);
                return false;
            }
        }

        private static object CoerceJson(IJsonValue val)
        {
            if (val == null) return null;
            switch (val.ValueType)
            {
                case JsonValueType.String: return val.GetString();
                case JsonValueType.Number: return val.GetNumber();
                case JsonValueType.Boolean: return val.GetBoolean();
                case JsonValueType.Null: return null;
                case JsonValueType.Array:
                    var list = new List<object>();
                    var a = val.GetArray();
                    for (int i = 0; i < a.Count; i++) list.Add(CoerceJson(a[i]));
                    return list;
                case JsonValueType.Object:
                    var dict = new Dictionary<string, object>();
                    var o = val.GetObject();
                    foreach (var kv in o) dict[kv.Key] = CoerceJson(kv.Value);
                    return dict;
                default: return null;
            }
        }

        private string DictStr(Dictionary<string, object> d, string key, string fallback = "")
        {
            if (d == null) return fallback;
            object v;
            if (d.TryGetValue(key, out v) && v != null) return v.ToString();
            return fallback;
        }

        private List<object> DictList(Dictionary<string, object> d, string key)
        {
            if (d == null) return null;
            object v;
            if (d.TryGetValue(key, out v) && v is List<object> list) return list;
            return null;
        }

        private void ShowCardMode(bool enable, bool showIndex = false)
        {
            _cardMode = enable;
            _showCategoryIndex = showIndex;
            _currentCardIndex = 0;
            if (ContentArea != null)
                ContentArea.Background = new SolidColorBrush(enable ? Colors.Black : Colors.White);
            if (enable && _entryCards.Count > 0)
            {
                ContentHost.Children.Clear();
                ContentHost.Children.Add(BuildCardPanel());
            }
        }

        private UIElement BuildCardPanel()
        {
            if (_showCategoryIndex)
                return BuildCategoryIndexPanel();

            var root = new Grid { Background = new SolidColorBrush(Windows.UI.Colors.Black) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: toolbar
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1: card
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: nav bar

            // Top toolbar: "Browse" / filter label
            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(12, 8, 8, 4)
            };

            var browseBtn = new TextBlock
            {
                Text = string.IsNullOrEmpty(_filterCollectionId) ? "\u2630 Browse" : "\u2190 All",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(220, 100, 180, 255))
            };
            browseBtn.Tapped += (s, e) =>
            {
                e.Handled = true;
                if (string.IsNullOrEmpty(_filterCollectionId))
                    ShowCardMode(true, showIndex: true);
                else
                {
                    _filterCollectionId = null;
                    if (_backupEntryCards != null)
                        _entryCards = _backupEntryCards;
                    _currentCardIndex = 0;
                    ShowCardMode(true);
                }
            };
            toolbar.Children.Add(browseBtn);

            if (!string.IsNullOrEmpty(_filterCollectionId))
            {
                string colName;
                if (!_collectionLookup.TryGetValue(_filterCollectionId, out colName))
                    colName = _filterCollectionId;
                toolbar.Children.Add(new TextBlock
                {
                    Text = " \u2192 " + colName + " (" + _entryCards.Count + ")",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromArgb(200, 200, 200, 200)),
                    Margin = new Thickness(4, 0, 0, 0)
                });
            }
            Grid.SetRow(toolbar, 0);
            root.Children.Add(toolbar);

            // Current card content
            var cardBorder = new Border
            {
                Margin = new Thickness(12, 0, 12, 0),
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            cardBorder.Child = BuildCardContent(_currentCardIndex);
            Grid.SetRow(cardBorder, 1);
            root.Children.Add(cardBorder);

            // Bottom nav bar: « first ‹ prev | counter | next › last »
            int total = _entryCards.Count;
            var navBar = new Grid();
            navBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // first
            navBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // prev
            navBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // counter
            navBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // next
            navBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // last

            var firstBtn = new TextBlock
            {
                Text = "\u00AB",
                FontSize = 20,
                Foreground = new SolidColorBrush(_currentCardIndex > 0 ? Color.FromArgb(220, 100, 180, 255) : Color.FromArgb(100, 100, 100, 100)),
                Margin = new Thickness(12, 4, 6, 12),
                VerticalAlignment = VerticalAlignment.Center
            };
            firstBtn.Tapped += (s, e) => { e.Handled = true; NavCardTo(0); };
            Grid.SetColumn(firstBtn, 0);
            navBar.Children.Add(firstBtn);

            var prevBtn = new TextBlock
            {
                Text = "\u2039",
                FontSize = 24,
                Foreground = new SolidColorBrush(_currentCardIndex > 0 ? Color.FromArgb(220, 100, 180, 255) : Color.FromArgb(100, 100, 100, 100)),
                Margin = new Thickness(6, 4, 12, 12),
                VerticalAlignment = VerticalAlignment.Center
            };
            prevBtn.Tapped += (s, e) => { e.Handled = true; NavCard(-1); };
            Grid.SetColumn(prevBtn, 1);
            navBar.Children.Add(prevBtn);

            string counterText = (_currentCardIndex + 1) + " / " + total;
            if (total < 722)
                counterText += " (of 722)";
            var counterBlock = new TextBlock
            {
                Text = counterText,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 200, 200, 200)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(counterBlock, 2);
            navBar.Children.Add(counterBlock);

            var nextBtn = new TextBlock
            {
                Text = "\u203A",
                FontSize = 24,
                Foreground = new SolidColorBrush(_currentCardIndex < total - 1 ? Color.FromArgb(220, 100, 180, 255) : Color.FromArgb(100, 100, 100, 100)),
                Margin = new Thickness(12, 4, 6, 12),
                VerticalAlignment = VerticalAlignment.Center
            };
            nextBtn.Tapped += (s, e) => { e.Handled = true; NavCard(1); };
            Grid.SetColumn(nextBtn, 3);
            navBar.Children.Add(nextBtn);

            var lastBtn = new TextBlock
            {
                Text = "\u00BB",
                FontSize = 20,
                Foreground = new SolidColorBrush(_currentCardIndex < total - 1 ? Color.FromArgb(220, 100, 180, 255) : Color.FromArgb(100, 100, 100, 100)),
                Margin = new Thickness(6, 4, 12, 12),
                VerticalAlignment = VerticalAlignment.Center
            };
            lastBtn.Tapped += (s, e) => { e.Handled = true; NavCardTo(total - 1); };
            Grid.SetColumn(lastBtn, 4);
            navBar.Children.Add(lastBtn);

            Grid.SetRow(navBar, 2);
            root.Children.Add(navBar);

            return root;
        }

        private UIElement BuildCardContent(int index)
        {
            if (index < 0 || index >= _entryCards.Count)
                return new TextBlock { Text = "No more entries", Foreground = new SolidColorBrush(Colors.Gray) };

            var card = _entryCards[index];
            string title = DictStr(card, "title", DictStr(card, "name", "Untitled"));
            string desc = DictStr(card, "description", DictStr(card, "blurb", ""));
            string date = DictStr(card, "start", "");
            string imageFile = DictStr(card, "file", "");
            string typeVal = DictStr(card, "type", "");
            string entryId = DictStr(card, "id", "");

            var grid = new Grid
            {
                Margin = new Thickness(16),
                Width = 320,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: title+type badge
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: date
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: image
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3: desc
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4: related collections
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 5: stories
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 6: keywords
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 7: permalink

            // Title row with optional type badge
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            titleRow.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 20,
                FontWeight = Windows.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrEmpty(typeVal))
            {
                titleRow.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 80, 120, 200)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(8, 4, 0, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = typeVal, FontSize = 10, Foreground = new SolidColorBrush(Colors.White) }
                });
            }
            grid.Children.Add(titleRow);

            // Date
            if (!string.IsNullOrEmpty(date))
            {
                var dateTb = new TextBlock
                {
                    Text = date,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromArgb(200, 180, 180, 180)),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                Grid.SetRow(dateTb, 1);
                grid.Children.Add(dateTb);
            }

            // Image (if file exists)
            if (!string.IsNullOrEmpty(imageFile))
            {
                try
                {
                    var img = new Image
                    {
                        Stretch = Stretch.Uniform,
                        MaxHeight = 200,
                        Margin = new Thickness(0, 0, 0, 8),
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    var imgUri = new Uri("https://nokiadesignarchive.aalto.fi/images/archive/" + Uri.EscapeDataString(imageFile) + ".jpg");
                    img.Source = new BitmapImage(imgUri);
                    Grid.SetRow(img, 2);
                    grid.Children.Add(img);
                }
                catch { }
            }

            // Description
            if (!string.IsNullOrEmpty(desc))
            {
                var descTb = new TextBlock
                {
                    Text = desc,
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromArgb(220, 200, 200, 200)),
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 20
                };
                Grid.SetRow(descTb, 3);
                grid.Children.Add(descTb);
            }

            // Related collections as chips
            var entryCols = DictList(card, "collections");
            if (entryCols != null && entryCols.Count > 0)
            {
                var chipRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
                foreach (var colIdObj in entryCols)
                {
                    string colId = colIdObj?.ToString() ?? "";
                    if (string.IsNullOrEmpty(colId)) continue;
                    string colName;
                    if (!_collectionLookup.TryGetValue(colId, out colName))
                        colName = colId;
                    string capturedColId = colId;
                    var chip = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                        CornerRadius = new CornerRadius(4),
                        Margin = new Thickness(0, 0, 6, 2),
                        Padding = new Thickness(8, 3, 8, 3)
                    };
                    var chipText = new TextBlock
                    {
                        Text = colName,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(220, 180, 200, 255))
                    };
                    chip.Child = chipText;
                    chip.Tapped += (s, e) =>
                    {
                        e.Handled = true;
                        FilterByCollection(capturedColId);
                    };
                    chipRow.Children.Add(chip);
                }
                Grid.SetRow(chipRow, 4);
                grid.Children.Add(chipRow);
            }

            // Stories referencing this entry
            if (!string.IsNullOrEmpty(entryId) && _storiesList.Count > 0)
            {
                var matchingStories = new List<string>();
                foreach (var story in _storiesList)
                {
                    var storyEntries = DictList(story, "entries");
                    if (storyEntries != null)
                    {
                        foreach (var se in storyEntries)
                        {
                            if (se?.ToString() == entryId)
                            {
                                matchingStories.Add(DictStr(story, "title", "Untitled story"));
                                break;
                            }
                        }
                    }
                }
                if (matchingStories.Count > 0)
                {
                    var storyLabel = new TextBlock
                    {
                        Text = "In stories: " + string.Join(", ", matchingStories),
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromArgb(180, 160, 200, 160)),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    Grid.SetRow(storyLabel, 5);
                    grid.Children.Add(storyLabel);
                }
            }

            // Related keywords as chips
            var entryKw = DictList(card, "keywords");
            if (entryKw != null && entryKw.Count > 0 && _keywordMap.Count > 0)
            {
                var kwRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 4) };
                int kwShown = 0;
                foreach (var kwIdObj in entryKw)
                {
                    string kwId = kwIdObj?.ToString() ?? "";
                    if (string.IsNullOrEmpty(kwId)) continue;
                    string kwName;
                    if (!_keywordMap.TryGetValue(kwId, out kwName)) continue;
                    if (kwShown >= 6) break;
                    kwShown++;
                    var kwChip = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 180, 160, 40)),
                        CornerRadius = new CornerRadius(4),
                        Margin = new Thickness(0, 0, 6, 2),
                        Padding = new Thickness(8, 3, 8, 3)
                    };
                    kwChip.Child = new TextBlock { Text = kwName, FontSize = 11, Foreground = new SolidColorBrush(Colors.Black) };
                    kwRow.Children.Add(kwChip);
                }
                if (kwRow.Children.Count > 0)
                {
                    var kwLabel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                    kwLabel.Children.Add(new TextBlock { Text = "Related keywords: ", FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(150, 180, 180, 180)) });
                    kwLabel.Children.Add(kwRow);
                    Grid.SetRow(kwLabel, 6);
                    grid.Children.Add(kwLabel);
                }
            }

            // Permalink button
            string permalink = DictStr(card, "permalink", "");
            if (!string.IsNullOrEmpty(permalink))
            {
                var permBorder = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromArgb(200, 200, 200, 200)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(12, 6, 12, 6),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                permBorder.Child = new TextBlock
                {
                    Text = "View on Aalto repository",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                string capturedUrl = permalink;
                permBorder.Tapped += async (s, e) =>
                {
                    e.Handled = true;
                    try { await Windows.System.Launcher.LaunchUriAsync(new Uri(capturedUrl)); } catch { }
                };
                Grid.SetRow(permBorder, 7);
                grid.Children.Add(permBorder);
            }

            return grid;
        }

        private void NavCard(int direction)
        {
            if (!_cardMode || _showCategoryIndex || _entryCards.Count == 0) return;
            int newIndex = _currentCardIndex + direction;
            if (newIndex < 0 || newIndex >= _entryCards.Count) return;
            _currentCardIndex = newIndex;
            System.Diagnostics.Debug.WriteLine("[DIAG:CARD] Nav to index " + _currentCardIndex + " / " + _entryCards.Count);

            ContentHost.Children.Clear();
            ContentHost.Children.Add(BuildCardPanel());
        }

        private void NavCardTo(int index)
        {
            if (!_cardMode || _showCategoryIndex || _entryCards.Count == 0) return;
            if (index < 0 || index >= _entryCards.Count) return;
            _currentCardIndex = index;
            System.Diagnostics.Debug.WriteLine("[DIAG:CARD] Nav to index " + _currentCardIndex + " / " + _entryCards.Count);

            ContentHost.Children.Clear();
            ContentHost.Children.Add(BuildCardPanel());
        }

        private void FilterByCollection(string colId)
        {
            if (_backupEntryCards == null)
                _backupEntryCards = new List<Dictionary<string, object>>(_entryCards);

            var filtered = new List<Dictionary<string, object>>();
            foreach (var e in _backupEntryCards)
            {
                var cols = DictList(e, "collections");
                if (cols != null)
                {
                    for (int i = 0; i < cols.Count; i++)
                    {
                        if (cols[i]?.ToString() == colId)
                        {
                            filtered.Add(e);
                            break;
                        }
                    }
                }
            }

            _filterCollectionId = colId;
            _entryCards = filtered;
            _currentCardIndex = 0;
            _showCategoryIndex = false;
            ContentHost.Children.Clear();
            ContentHost.Children.Add(BuildCardPanel());
            System.Diagnostics.Debug.WriteLine("[DIAG:CARD] Filtered to collection " + colId + ": " + filtered.Count + " entries");
        }

        private UIElement BuildCategoryIndexPanel()
        {
            var root = new Grid { Background = new SolidColorBrush(Color.FromArgb(255, 18, 18, 18)) };

            var sv = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(12, 40, 12, 12)
            };
            var innerStack = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

            // "Back to cards" button
            var backBtn = new TextBlock
            {
                Text = "\u2190 Cards",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(220, 100, 180, 255)),
                Margin = new Thickness(0, 0, 0, 12)
            };
            backBtn.Tapped += (s, e) =>
            {
                e.Handled = true;
                _showCategoryIndex = false;
                ContentHost.Children.Clear();
                ContentHost.Children.Add(BuildCardPanel());
            };
            innerStack.Children.Add(backBtn);

            // Title
            innerStack.Children.Add(new TextBlock
            {
                Text = "Categories",
                FontSize = 22,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                Margin = new Thickness(0, 0, 0, 12)
            });

            // Group collections by "grouping" or "theme" field
            var groups = new Dictionary<string, List<Dictionary<string, object>>>();
            var ungrouped = new List<Dictionary<string, object>>();
            foreach (var col in _collectionMap)
            {
                string group = DictStr(col, "grouping", DictStr(col, "theme", ""));
                if (string.IsNullOrEmpty(group))
                    ungrouped.Add(col);
                else
                {
                    List<Dictionary<string, object>> list;
                    if (!groups.TryGetValue(group, out list))
                    {
                        list = new List<Dictionary<string, object>>();
                        groups[group] = list;
                    }
                    list.Add(col);
                }
            }

            foreach (var kv in groups)
            {
                // Group header
                innerStack.Children.Add(new TextBlock
                {
                    Text = kv.Key,
                    FontSize = 16,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(220, 180, 200, 255)),
                    Margin = new Thickness(0, 8, 0, 4)
                });
                foreach (var col in kv.Value)
                {
                    string colId = DictStr(col, "id", "");
                    string colName = DictStr(col, "title", DictStr(col, "name", colId));
                    string colBlurb = DictStr(col, "blurb", "");
                    int entryCount = 0;
                    foreach (var e in _backupEntryCards ?? _entryCards)
                    {
                        var cols = DictList(e, "collections");
                        if (cols != null)
                        {
                            for (int ci = 0; ci < cols.Count; ci++)
                            {
                                if (cols[ci]?.ToString() == colId)
                                {
                                    entryCount++;
                                    break;
                                }
                            }
                        }
                    }
                    string captured = colId;
                    var colItem = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                        CornerRadius = new CornerRadius(6),
                        Margin = new Thickness(0, 2, 0, 2),
                        Padding = new Thickness(10, 6, 10, 6)
                    };
                    colItem.Tapped += (s, e) =>
                    {
                        e.Handled = true;
                        FilterByCollection(captured);
                    };
                    var colStack = new StackPanel { Orientation = Orientation.Horizontal };
                    colStack.Children.Add(new TextBlock
                    {
                        Text = colName,
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Windows.UI.Colors.White)
                    });
                    colStack.Children.Add(new TextBlock
                    {
                        Text = " (" + entryCount + ")",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromArgb(160, 160, 160, 160)),
                        Margin = new Thickness(4, 0, 0, 0)
                    });
                    colItem.Child = colStack;
                    innerStack.Children.Add(colItem);

                    if (!string.IsNullOrEmpty(colBlurb))
                    {
                        innerStack.Children.Add(new TextBlock
                        {
                            Text = colBlurb,
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.FromArgb(140, 180, 180, 180)),
                            Margin = new Thickness(10, 0, 0, 4),
                            TextWrapping = TextWrapping.Wrap
                        });
                    }
                }
            }

            // Ungrouped collections
            if (ungrouped.Count > 0)
            {
                innerStack.Children.Add(new TextBlock
                {
                    Text = "Other",
                    FontSize = 16,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(220, 180, 200, 255)),
                    Margin = new Thickness(0, 8, 0, 4)
                });
                foreach (var col in ungrouped)
                {
                    string colId = DictStr(col, "id", "");
                    string colName = DictStr(col, "title", DictStr(col, "name", colId));
                    string captured = colId;
                    var colItem = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(255, 35, 35, 35)),
                        CornerRadius = new CornerRadius(6),
                        Margin = new Thickness(0, 2, 0, 2),
                        Padding = new Thickness(10, 6, 10, 6)
                    };
                    colItem.Tapped += (s, e) =>
                    {
                        e.Handled = true;
                        FilterByCollection(captured);
                    };
                    colItem.Child = new TextBlock { Text = colName, FontSize = 14, Foreground = new SolidColorBrush(Windows.UI.Colors.White) };
                    innerStack.Children.Add(colItem);
                }
            }

            sv.Content = innerStack;
            root.Children.Add(sv);
            return root;
        }

        private void RunNilJsStartupTest()
        {
            string status = "NiL.JS startup skipped.";
            try
            {
                var contextType = Type.GetType("NiL.JS.Core.Context, NiL.JS", false);
                if (contextType != null)
                {
                    object ctx = null;
                    try { ctx = Activator.CreateInstance(contextType); }
                    catch (Exception ex) when (ex.GetType().Name == "MissingMethodException") { status = "NiL.JS startup skipped: default constructor unavailable."; }
                    catch (TypeLoadException tex) { status = "NiL.JS unsupported: " + tex.Message; }
                    if (ctx != null) status = "NiL.JS startup check passed (simplified)";
                }
                else if (!status.StartsWith("NiL.JS", StringComparison.Ordinal))
                    status = "NiL.JS startup skipped: Context type unavailable.";
            }
            catch (TypeLoadException tex) { status = "NiL.JS unsupported: " + tex.Message; }
            catch (Exception ex) { status = "NiL.JS startup failed: " + ex.GetType().Name + ": " + ex.Message; }
            if (_welcomeShown)
                UpdateStatusMessage(status);
            else
                SetStartupStatus(status);
        }

        private static async Task<string> LoadTextFromAppxAsync(Uri uri)
        {
            if (uri == null) return string.Empty;
            try
            {
                var file = await StorageFile.GetFileFromApplicationUriAsync(uri);
                return await FileIO.ReadTextAsync(file);
            }
            catch { return string.Empty; }
        }

        // ========== Event Handlers ==========

        private async void GoButton_Click(object sender, RoutedEventArgs e) => await NavigateAsync(GetAddressFromUI());

        private async void Omnibox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await NavigateAsync(GetAddressFromUI());
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            try { _browser.GoBack(); } catch { }
            UpdateNavButtons();
        }

        private void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            try { _browser.GoForward(); } catch { }
            UpdateNavButtons();
        }

        private void UpdateNavButtons()
        {
            try
            {
                bool canBack = _browser.CanGoBack;
                bool canForward = _browser.CanGoForward;
                System.Diagnostics.Debug.WriteLine($"[NavButtons] Back={canBack} Forward={canForward} BackBtn={BackButton != null} FwdBtn={ForwardButton != null}");
                if (BackButton != null) BackButton.IsEnabled = canBack;
                if (ForwardButton != null) ForwardButton.IsEnabled = canForward;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NavButtons] Error: {ex.Message}");
            }
        }

        private async void MainPage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Size change on mobile/emulator does not require full page re-navigation.
            // The initial render uses the current window size. Ignore subsequent changes
            // to avoid infinite render loop (each render changes layout > fires SizeChanged).
            // Future: re-evaluate CSS media queries without re-fetching HTML.
        }

        public void ShowGlobalError(string message)
        {
            UpdateStatusMessage("Error: " + (message ?? string.Empty), overrideStartup: true);
            try { if (LoadingRing != null) LoadingRing.IsActive = false; } catch { }
            try { if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Collapsed; } catch { }
            try { if (LoadProgressBar != null) { LoadProgressBar.Visibility = Visibility.Collapsed; LoadProgressBar.Opacity = 1; } } catch { }
            _loadProgressActive = false;
            ShowMessageOverlay(message, "Aw, Snap!", "Important", isError: true);
        }

        private void ShowMessageOverlay(string message, string title = "Aw, Snap!", string icon = "Important", bool isError = true)
        {
            Ui(() =>
            {
                _lastErrorMessage = message ?? "Unknown error.";
                _messageOverlayVisible = true;
                if (MessageOverlay != null) MessageOverlay.Visibility = Visibility.Visible;
                if (MessageOverlayTitle != null) MessageOverlayTitle.Text = title;
                if (MessageText != null) MessageText.Text = _lastErrorMessage;
                if (MessageOverlayIcon != null)
                {
                    MessageOverlayIcon.Foreground = new SolidColorBrush(
                        isError ? Windows.UI.Colors.Crimson : Windows.UI.Colors.LightSkyBlue);
                    if (Enum.TryParse<Symbol>(icon, out var sym))
                        MessageOverlayIcon.Symbol = sym;
                }
                if (ErrorRetryPoor != null)
                    ErrorRetryPoor.Visibility = isError && !string.IsNullOrWhiteSpace(_lastFailedAddress)
                        ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        private void HideMessageOverlay()
        {
            Ui(() =>
            {
                _messageOverlayVisible = false;
                if (MessageOverlay != null) MessageOverlay.Visibility = Visibility.Collapsed;
            });
        }

        private void MessageClose_Click(object sender, RoutedEventArgs e) => HideMessageOverlay();

        // V.5 — Switch to POOR mode and retry failed navigation
        private void ErrorRetryPoor_Click(object sender, RoutedEventArgs e)
        {
            HideMessageOverlay();
            RenderMode = "Poor";
            if (!string.IsNullOrWhiteSpace(_lastFailedAddress))
            {
                var _ = NavigateAsync(_lastFailedAddress);
            }
        }

        // ========== Toast Notifications ==========
        public void ShowToast(string message, int durationMs = 5000)
        {
            Ui(() =>
            {
                if (ToastOverlay == null || ToastBar == null || ToastMessage == null) return;

                _toastTimer?.Stop();

                ToastMessage.Text = message;
                ToastOverlay.Visibility = Visibility.Visible;
                ToastBar.Opacity = 0;

                var sb = new Windows.UI.Xaml.Media.Animation.Storyboard();
                var da = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = 0,
                    To = 0.95,
                    Duration = TimeSpan.FromMilliseconds(200)
                };
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(da, ToastBar);
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(da, "Opacity");
                sb.Children.Add(da);
                sb.Begin();

                _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
                _toastTimer.Tick += (s, e) =>
                {
                    _toastTimer.Stop();
                    HideToast();
                };
                _toastTimer.Start();
            });
        }

        private void HideToast()
        {
            Ui(() =>
            {
                if (ToastBar == null || ToastOverlay == null) return;

                var sb = new Windows.UI.Xaml.Media.Animation.Storyboard();
                var da = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = ToastBar.Opacity,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(300)
                };
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(da, ToastBar);
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(da, "Opacity");
                sb.Children.Add(da);
                sb.Completed += (s, e) =>
                {
                    if (ToastOverlay != null)
                        ToastOverlay.Visibility = Visibility.Collapsed;
                };
                sb.Begin();
            });
        }

        // ========== Settings ==========
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try { Frame.Navigate(typeof(SettingsPage)); } catch { }
        }

        public void ApplyRenderMode()
        {
            try
            {
                var s = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (s.Values.TryGetValue("RenderMode", out var v) && v is string mode && !string.IsNullOrWhiteSpace(mode))
                    RenderMode = mode;
            }
            catch { }
        }

        public void ApplyDevTools()
        {
            try
            {
                var s = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (s.Values.TryGetValue("DevToolsEnabled", out var v) && v is bool enabled)
                    DevToolsEnabled = enabled;
            }
            catch { }
        }

        public void ApplyStatusBar()
        {
            try
            {
                var s = Windows.Storage.ApplicationData.Current.LocalSettings;
                bool visible = true;
                if (s.Values.TryGetValue("StatusBarVisible", out var v) && v is bool vb)
                    visible = vb;
                if (StatusBarBorder != null)
                    StatusBarBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
        }

        public void ApplyButtonVisibility()
        {
            try
            {
                var s = Windows.Storage.ApplicationData.Current.LocalSettings;
                bool showSnapshot = true;
                bool showCopy = true;
                if (s.Values.TryGetValue("ShowSnapshot", out var v1) && v1 is bool b1)
                    showSnapshot = b1;
                if (s.Values.TryGetValue("ShowCopy", out var v2) && v2 is bool b2)
                    showCopy = b2;
                if (SnapshotButton != null)
                    SnapshotButton.Visibility = showSnapshot ? Visibility.Visible : Visibility.Collapsed;
                if (CopyButton != null)
                    CopyButton.Visibility = showCopy ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
        }

        public void ApplyAppBarMode()
        {
            _appBarMode = LoadAppBarMode();
            if (_appBarMode == "Full")
            {
                if (BottomBar != null) BottomBar.Height = 52;
                if (BarContent != null) BarContent.IsHitTestVisible = true;
                if (BarStrip != null) BarStrip.IsHitTestVisible = false;
                _barExpanded = true;
            }
            else if (_appBarMode == "Hided")
            {
                if (BottomBar != null) BottomBar.Height = 6;
                if (BarContent != null) BarContent.IsHitTestVisible = false;
                if (BarStrip != null) BarStrip.IsHitTestVisible = true;
                _barExpanded = false;
            }
            else
            {
                if (BottomBar != null) BottomBar.Height = 24;
                if (BarContent != null) BarContent.IsHitTestVisible = false;
                if (BarStrip != null) BarStrip.IsHitTestVisible = true;
                _barExpanded = false;
            }
            UpdateBarClip();
        }

        private static string LoadAppBarMode()
        {
            try
            {
                var s = ApplicationData.Current.LocalSettings;
                if (s.Values.TryGetValue("AppBarMode", out var v) && v is string mode && !string.IsNullOrWhiteSpace(mode))
                    return mode;
            }
            catch { }
            return "Semi";
        }

        public bool JsEnabled
        {
            get => _welcomeEngine.EnableJavaScript;
            set => _welcomeEngine.EnableJavaScript = value;
        }

        public string RenderMode
        {
            get => _browser.RenderMode;
            set
            {
                _browser.RenderMode = value;
                _welcomeEngine.RenderModeString = value;
            }
        }

        public void ClearResourceCache()
        {
            _resources.ClearCache();
            UpdateStatusMessage("Cache cleared.");
        }

        // --- Snapshot (screenshot) ---

        private async void SnapshotButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[Snapshot] START");

                // Check if page is scrollable and tall enough to warrant scrolling capture
                ScrollViewer sv = null;
                try
                {
                    for (int i = 0; i < Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(ContentArea); i++)
                    {
                        var child = Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(ContentArea, i);
                        if (child is ScrollViewer) { sv = (ScrollViewer)child; break; }
                    }
                }
                catch { }

                // If scrollable height is significant (more than 1.5x viewport), use scrolling mode
                if (sv != null && sv.ScrollableHeight > sv.ViewportHeight * 1.5)
                {
                    System.Diagnostics.Debug.WriteLine("[Snapshot] Page is tall (" + sv.ScrollableHeight + " > " + (sv.ViewportHeight * 1.5) + "), using SnapshotWithScrolling");
                    SnapshotWithScrolling();
                    return;
                }

                // Otherwise, use basic snapshot (faster)
                System.Diagnostics.Debug.WriteLine("[Snapshot] Page fits in viewport, using basic snapshot");

                // Prefer _activeVisual (the rendered page Border) over ContentArea
                FrameworkElement target = _activeVisual;
                if (target == null)
                {
                    System.Diagnostics.Debug.WriteLine("[Snapshot] _activeVisual is null, falling back to ContentHost");
                    target = ContentHost;
                }
                if (target == null)
                {
                    System.Diagnostics.Debug.WriteLine("[Snapshot] FAIL: no render target available");
                    UpdateStatusMessage("Snapshot: nothing to capture.");
                    return;
                }

                System.Diagnostics.Debug.WriteLine("[Snapshot] target=" + target.GetType().Name + " ActualSize=" + target.ActualWidth + "x" + target.ActualHeight);

                UpdateStatusMessage("Taking snapshot...");

                var bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(target);

                System.Diagnostics.Debug.WriteLine("[Snapshot] bitmap.PixelWidth=" + bitmap.PixelWidth + " PixelHeight=" + bitmap.PixelHeight);

                if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0)
                {
                    // Fallback: try rendering ContentArea (visible viewport)
                    System.Diagnostics.Debug.WriteLine("[Snapshot] Zero-size bitmap, trying ContentArea fallback");
                    if (ContentArea != null)
                    {
                        bitmap = new RenderTargetBitmap();
                        await bitmap.RenderAsync(ContentArea);
                        System.Diagnostics.Debug.WriteLine("[Snapshot] fallback bitmap=" + bitmap.PixelWidth + "x" + bitmap.PixelHeight);
                    }
                    if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0)
                    {
                        System.Diagnostics.Debug.WriteLine("[Snapshot] FAIL: still zero-size");
                        UpdateStatusMessage("Snapshot failed: could not capture content.");
                        return;
                    }
                }

                var pixelBuffer = await bitmap.GetPixelsAsync();
                var pixels = pixelBuffer.ToArray();
                System.Diagnostics.Debug.WriteLine("[Snapshot] pixels.Length=" + pixels.Length);

                var filename = GenerateSnapshotFilename();
                System.Diagnostics.Debug.WriteLine("[Snapshot] filename=" + filename);

                await SavePngAsync(bitmap, pixels, filename);

                System.Diagnostics.Debug.WriteLine("[Snapshot] DONE");
                UpdateStatusMessage("Snapshot saved: " + filename);
                ShowToast("?? Snapshot saved: " + filename, 3000);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Snapshot] ERROR: " + ex.GetType().Name + " � " + ex.Message);
                if (ex.InnerException != null)
                    System.Diagnostics.Debug.WriteLine("[Snapshot] Inner: " + ex.InnerException.Message);
                UpdateStatusMessage("Snapshot failed: " + ex.Message, overrideStartup: true);
            }
        }

        // --- Snapshot with scrolling support (full-page capture) ---

        private async void SnapshotWithScrolling()
        {
            try
            {
                if (ContentHost.Children.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[Snapshot] FAIL: ContentHost has no children");
                    UpdateStatusMessage("Snapshot: nothing to capture.");
                    return;
                }

                UpdateStatusMessage("Taking full-page snapshot...");

                // Find the ScrollViewer inside ContentArea
                ScrollViewer sv = null;
                try
                {
                    for (int i = 0; i < Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(ContentArea); i++)
                    {
                        var child = Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(ContentArea, i);
                        if (child is ScrollViewer) { sv = (ScrollViewer)child; break; }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[Snapshot] Find SV error: " + ex.Message); }

                if (sv == null)
                {
                    System.Diagnostics.Debug.WriteLine("[Snapshot] No ScrollViewer found, using basic snapshot");
                    SnapshotButton_Click(null, null);
                    return;
                }

                double originalOffset = sv.VerticalOffset;
                double viewportHeight = sv.ViewportHeight;
                double totalHeight = sv.ScrollableHeight + viewportHeight;

                System.Diagnostics.Debug.WriteLine("[Snapshot] Viewport=" + viewportHeight + " Total=" + totalHeight + " Scrollable=" + sv.ScrollableHeight);

                // Calculate number of captures needed (limit to 50 to prevent memory issues)
                int captures = (int)Math.Ceiling(totalHeight / viewportHeight);
                if (captures > 50) captures = 50;
                if (captures < 1) captures = 1;

                System.Diagnostics.Debug.WriteLine("[Snapshot] Will capture " + captures + " frames");

                var frames = new List<byte[]>();
                int frameWidth = 0, frameHeight = 0;

                try
                {
                    for (int i = 0; i < captures; i++)
                    {
                        double offset = i * viewportHeight;
                        if (offset > sv.ScrollableHeight) offset = sv.ScrollableHeight;

                        System.Diagnostics.Debug.WriteLine("[Snapshot] Scrolling to offset " + offset + " (frame " + (i + 1) + "/" + captures + ")");
                        UpdateStatusMessage("Capturing frame " + (i + 1) + "/" + captures + "...");

                        // Scroll to position
                        sv.ChangeView(null, offset, null, true);

                        // Wait for rendering to complete
                        await Task.Delay(250);

                        // Capture the frame
                        var bitmap = new RenderTargetBitmap();
                        await bitmap.RenderAsync(ContentArea);

                        if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0)
                        {
                            System.Diagnostics.Debug.WriteLine("[Snapshot] Frame " + (i + 1) + " is empty, skipping");
                            continue;
                        }

                        frameWidth = bitmap.PixelWidth;
                        frameHeight = bitmap.PixelHeight;

                        var pixelBuffer = await bitmap.GetPixelsAsync();
                        frames.Add(pixelBuffer.ToArray());

                        System.Diagnostics.Debug.WriteLine("[Snapshot] Frame " + (i + 1) + " captured: " + frameWidth + "x" + frameHeight);
                    }
                }
                finally
                {
                    // Restore original scroll position
                    try { sv.ChangeView(null, originalOffset, null, true); } catch { }
                }

                if (frames.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[Snapshot] FAIL: no frames captured");
                    UpdateStatusMessage("Snapshot failed: could not capture content.");
                    return;
                }

                // Stitch frames together vertically
                System.Diagnostics.Debug.WriteLine("[Snapshot] Stitching " + frames.Count + " frames...");
                UpdateStatusMessage("Stitching " + frames.Count + " frames...");

                int totalPixelHeight = frameHeight * frames.Count;
                var stitchedPixels = new byte[frameWidth * totalPixelHeight * 4];

                for (int i = 0; i < frames.Count; i++)
                {
                    var frame = frames[i];
                    int destOffset = i * frameWidth * frameHeight * 4;
                    Array.Copy(frame, 0, stitchedPixels, destOffset, frame.Length);
                }

                System.Diagnostics.Debug.WriteLine("[Snapshot] Stitched size: " + frameWidth + "x" + totalPixelHeight);

                var filename = GenerateSnapshotFilename();
                await SaveStitchedPngAsync(frameWidth, totalPixelHeight, stitchedPixels, filename);

                System.Diagnostics.Debug.WriteLine("[Snapshot] DONE");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Snapshot] OUTER error: " + ex.GetType().Name + " � " + ex.Message);
                if (ex.InnerException != null)
                    System.Diagnostics.Debug.WriteLine("[Snapshot] Inner: " + ex.InnerException.Message);
                UpdateStatusMessage("Snapshot failed: " + ex.Message, overrideStartup: true);
            }
        }

        private async Task SaveStitchedPngAsync(int width, int height, byte[] pixels, string filename)
        {
            System.Diagnostics.Debug.WriteLine("[Snapshot] Saving stitched PNG: " + width + "x" + height + " pixels=" + pixels.Length);

            var folder = await Windows.Storage.KnownFolders.PicturesLibrary.CreateFolderAsync(
                "MediaExplorer", Windows.Storage.CreationCollisionOption.OpenIfExists);

            System.Diagnostics.Debug.WriteLine("[Snapshot] Folder: " + folder.Path);

            var file = await folder.CreateFileAsync(filename, Windows.Storage.CreationCollisionOption.GenerateUniqueName);

            System.Diagnostics.Debug.WriteLine("[Snapshot] File: " + file.Name);

            using (var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))
            {
                System.Diagnostics.Debug.WriteLine("[Snapshot] Encoding PNG...");

                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Ignore,
                    (uint)width,
                    (uint)height,
                    96.0, 96.0,
                    pixels);
                await encoder.FlushAsync();

                System.Diagnostics.Debug.WriteLine("[Snapshot] PNG flushed");
            }

            System.Diagnostics.Debug.WriteLine("[Snapshot] File saved successfully");
            UpdateStatusMessage("Full-page snapshot saved: " + filename, overrideStartup: true);
            ShowToast("?? Full-page snapshot saved: " + filename, 3000);
        }

        private async Task SavePngAsync(RenderTargetBitmap bitmap, byte[] pixels, string filename)
        {
            System.Diagnostics.Debug.WriteLine("[Snapshot] Saving to Pictures\\MediaExplorer\\" + filename);

            var folder = await Windows.Storage.KnownFolders.PicturesLibrary.CreateFolderAsync(
                "MediaExplorer", Windows.Storage.CreationCollisionOption.OpenIfExists);

            System.Diagnostics.Debug.WriteLine("[Snapshot] Folder path=" + folder.Path);

            var file = await folder.CreateFileAsync(filename, Windows.Storage.CreationCollisionOption.GenerateUniqueName);

            System.Diagnostics.Debug.WriteLine("[Snapshot] File created: " + file.Name);

            using (var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))
            {
                System.Diagnostics.Debug.WriteLine("[Snapshot] Stream opened, encoding PNG...");

                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Ignore,
                    (uint)bitmap.PixelWidth,
                    (uint)bitmap.PixelHeight,
                    96.0, 96.0,
                    pixels);
                await encoder.FlushAsync();

                System.Diagnostics.Debug.WriteLine("[Snapshot] PNG encoded and flushed");
            }

            System.Diagnostics.Debug.WriteLine("[Snapshot] Stream closed, file saved");
            UpdateStatusMessage("Snapshot saved: " + filename, overrideStartup: true);
        }

        private string GenerateSnapshotFilename()
        {
            string baseName = "snapshot";
            try
            {
                if (_currentUri != null)
                {
                    var host = _currentUri.Host;
                    if (string.IsNullOrWhiteSpace(host))
                    {
                        var path = _currentUri.AbsolutePath.Trim('/');
                        if (!string.IsNullOrWhiteSpace(path))
                            host = path;
                        else
                            host = _currentUri.Scheme;
                    }
                    if (!string.IsNullOrWhiteSpace(host))
                    {
                        baseName = host.Replace('.', '_').Replace(':', '_').Replace('/', '_').Replace('\\', '_');
                        var pathPart = _currentUri.AbsolutePath.Trim('/');
                        if (!string.IsNullOrWhiteSpace(pathPart) && pathPart != host)
                        {
                            var safePath = pathPart.Replace('/', '_').Replace('\\', '_');
                            if (safePath.Length > 30) safePath = safePath.Substring(0, 30);
                            baseName += "_" + safePath;
                        }
                    }
                }
            }
            catch { }

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return $"{baseName}_{timestamp}.png";
        }

        // --- Copy page text to clipboard ---

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dom = _browser.GetActiveDom();
                if (dom == null)
                {
                    UpdateStatusMessage("Nothing to copy.");
                    return;
                }

                var text = ExtractInnerText(dom);
                if (string.IsNullOrWhiteSpace(text))
                {
                    UpdateStatusMessage("No text content found.");
                    return;
                }

                var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
                pkg.SetText(text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
                UpdateStatusMessage("Copied " + text.Length + " characters to clipboard.");
                ShowToast("?? Copied " + text.Length + " characters to clipboard", 2000);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Copy] ERROR: " + ex.Message);
                UpdateStatusMessage("Copy failed: " + ex.Message);
            }
        }

        private string ExtractInnerText(BrowserCore.Engine.LiteElement node)
        {
            if (node == null) return "";
            var sb = new System.Text.StringBuilder();
            ExtractInnerTextRecursive(node, sb);
            return sb.ToString().Trim();
        }

        private void ExtractInnerTextRecursive(BrowserCore.Engine.LiteElement node, System.Text.StringBuilder sb)
        {
            if (node == null) return;

            // Skip script/style content
            if (node.Tag == "script" || node.Tag == "style") return;

            if (node.IsText)
            {
                var text = node.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (sb.Length > 0 && !sb.ToString().EndsWith("\n") && !sb.ToString().EndsWith(" "))
                        sb.Append(" ");
                    sb.Append(text);
                }
            }
            else
            {
                // Add newline for block elements
                if (node.Tag == "p" || node.Tag == "div" || node.Tag == "h1" || node.Tag == "h2" || 
                    node.Tag == "h3" || node.Tag == "h4" || node.Tag == "h5" || node.Tag == "h6" ||
                    node.Tag == "li" || node.Tag == "br" || node.Tag == "hr")
                {
                    if (sb.Length > 0 && !sb.ToString().EndsWith("\n"))
                        sb.AppendLine();
                }

                if (node.Children != null)
                {
                    foreach (var child in node.Children)
                        ExtractInnerTextRecursive(child, sb);
                }

                // Add newline after closing block elements
                if (node.Tag == "p" || node.Tag == "div" || node.Tag == "h1" || node.Tag == "h2" || 
                    node.Tag == "h3" || node.Tag == "h4" || node.Tag == "h5" || node.Tag == "h6" ||
                    node.Tag == "li" || node.Tag == "br")
                {
                    if (!sb.ToString().EndsWith("\n"))
                        sb.AppendLine();
                }
            }
        }

        private static string LoadHomePage()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue("HomePage", out var val) && val is string s && !string.IsNullOrWhiteSpace(s))
                    return s;
            }
            catch { }
            try
            {
                var startupFile = System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, "startup_url.txt");
                if (System.IO.File.Exists(startupFile))
                {
                    var url = System.IO.File.ReadAllText(startupFile)?.Trim();
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        System.IO.File.Delete(startupFile);
                        return url;
                    }
                }
            }
            catch { }
            return "https://nokiadesignarchive.aalto.fi/";
        }

        private static bool IsEffectivelyEmpty(FrameworkElement fe)
        {
            try
            {
                if (fe == null) return true;
                var tb = fe as TextBlock; if (tb != null) return string.IsNullOrWhiteSpace(tb.Text);
                var rtb = fe as RichTextBlock; if (rtb != null) return rtb.Blocks == null || rtb.Blocks.Count == 0;
                var border = fe as Border; if (border != null) return border.Child == null || IsEffectivelyEmpty(border.Child as FrameworkElement);
                var panel = fe as Panel;
                if (panel != null)
                {
                    if (panel.Children == null || panel.Children.Count == 0) return true;
                    foreach (var ch in panel.Children) { var cfe = ch as FrameworkElement; if (cfe != null && !IsEffectivelyEmpty(cfe)) return false; }
                    return true;
                }
                var cc = fe as ContentControl; if (cc != null) return cc.Content == null || IsEffectivelyEmpty(cc.Content as FrameworkElement);
            }
            catch { }
            return false;
        }

        private void ClearStartupStatus()
        {
            System.Diagnostics.Debug.WriteLine("[DIAG:OVL] ClearStartupStatus");
            _startupStatusPinned = false;
            _startupStatusMessage = null;
            _navigationComplete = false;
        }

        // --- Swipe navigation ---
        private void ContentArea_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            try
            {
                if (_cardMode)
                {
                    // Card mode: swipe left/right to navigate cards
                    if (e.Delta.Translation.X > 80)
                    {
                        NavCard(-1);
                        e.Complete();
                    }
                    else if (e.Delta.Translation.X < -80)
                    {
                        NavCard(1);
                        e.Complete();
                    }
                }
                else
                {
                    // Normal mode: swipe for browser back/forward
                    if (e.Delta.Translation.X > 80 && _browser.CanGoBack)
                    {
                        _browser.GoBack();
                        e.Complete();
                    }
                    else if (e.Delta.Translation.X < -80 && _browser.CanGoForward)
                    {
                        _browser.GoForward();
                        e.Complete();
                    }
                }
            }
            catch { }
        }

        // --- Reading Mode ---
        private double _readerFontSize = 16;

        public void StartReadingMode()
        {
            try
            {
                string text;
                try { text = _browser?.GetTextContent() ?? string.Empty; } catch { text = string.Empty; }
                if (string.IsNullOrWhiteSpace(text))
                {
                    UpdateStatusMessage("No page content for reading mode.");
                    return;
                }
                if (ReadingOverlay != null)
                {
                    _suppressBarCollapse = true;
                    ReadingOverlay.Visibility = Visibility.Visible;
                    _readerFontSize = 16;
                    if (ReaderContentText != null)
                    {
                        ReaderContentText.FontSize = _readerFontSize;
                        ReaderContentText.Text = text;
                    }
                }
            }
            catch { }
        }

        private void ReaderFontPlus_Click(object sender, RoutedEventArgs e)
        {
            _readerFontSize = Math.Min(_readerFontSize + 2, 36);
            if (ReaderContentText != null) ReaderContentText.FontSize = _readerFontSize;
        }

        private void ReaderFontMinus_Click(object sender, RoutedEventArgs e)
        {
            _readerFontSize = Math.Max(_readerFontSize - 2, 10);
            if (ReaderContentText != null) ReaderContentText.FontSize = _readerFontSize;
        }

        private void ReaderCloseButton_Click(object sender, RoutedEventArgs e)
        {
            try { if (ReadingOverlay != null) ReadingOverlay.Visibility = Visibility.Collapsed; _suppressBarCollapse = false; } catch { }
        }

        // --- AI ---
        private string LoadAiKey()
        {
            try
            {
                var s = ApplicationData.Current.LocalSettings;
                if (s.Values.TryGetValue("OpenRouterKey", out var v) && v is string key && !string.IsNullOrWhiteSpace(key))
                    return key;
            }
            catch { }
            return null;
        }

        private void SaveAiKey(string key)
        {
            try
            {
                var s = ApplicationData.Current.LocalSettings;
                if (string.IsNullOrWhiteSpace(key))
                    s.Values.Remove("OpenRouterKey");
                else
                    s.Values["OpenRouterKey"] = key;
            }
            catch { }
        }

        private async void AiButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExpandBar();

                var key = LoadAiKey();

                string pageText;
                try
                {
                    var textContent = _browser?.GetTextContent();
                    if (string.IsNullOrWhiteSpace(textContent))
                        textContent = _welcomeEngine.GetActiveDom()?.CollectText(true);
                    pageText = textContent ?? string.Empty;
                }
                catch
                {
                    pageText = string.Empty;
                }

                if (string.IsNullOrWhiteSpace(pageText))
                {
                    ShowAiResult("No page content to analyze. Navigate to a page first.", false);
                    return;
                }

                if (string.IsNullOrWhiteSpace(key))
                {
                    var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    pkg.SetText(pageText);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);

                    SnapshotButton_Click(null, null);

                    ShowAiResult("No API key set — copied page text to clipboard and saved screenshot.\n\nOpen Settings → Advanced → OpenRouter API Key for AI summaries.", false);
                    return;
                }

                if (pageText.Length > 16000)
                    pageText = pageText.Substring(0, 16000) + "\n[truncated]";

                ShowAiResult("Analyzing page content...", true);
                var summary = await OpenRouterClient.SummarizeAsync(key, pageText);
                ShowAiResult(summary, false);
            }
            catch (Exception ex)
            {
                ShowAiResult($"Error: {ex.Message}", false);
            }
        }

        private void ShowAiResult(string text, bool isLoading)
        {
            Ui(() =>
            {
                if (AiOverlay != null)
                {
                    AiOverlay.Visibility = Visibility.Visible;
                    _suppressBarCollapse = true;
                }
                if (AiResultText != null)
                    AiResultText.Text = text ?? string.Empty;
                if (AiTitleText != null)
                    AiTitleText.Text = isLoading ? "Thinking..." : "AI Summary";
                if (AiCopyButton != null)
                    AiCopyButton.Visibility = isLoading ? Visibility.Collapsed : Visibility.Visible;
            });
        }

        private void AiCloseButton_Click(object sender, RoutedEventArgs e)
        {
            try { if (AiOverlay != null) AiOverlay.Visibility = Visibility.Collapsed; _suppressBarCollapse = false; } catch { }
        }

        private void AiCopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var text = AiResultText?.Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    pkg.SetText(text);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
                    UpdateStatusMessage("Summary copied to clipboard.");
                }
            }
            catch { }
        }

        // --- DevTools ---

        private void AppendDevToolsLog(string message)
        {
            if (DevConsoleText == null) return;

            // Determine color based on message content
            Windows.UI.Color color = Windows.UI.Colors.Gray;
            if (message.Contains("[TEST:PASS]")) color = Windows.UI.Colors.LimeGreen;
            else if (message.Contains("[TEST:FAIL]")) color = Windows.UI.Colors.Red;
            else if (message.Contains("[TEST:SITE]")) color = Windows.UI.Colors.Yellow;
            else if (message.Contains("[TEST:PERF]")) color = Windows.UI.Colors.Cyan;
            else if (message.Contains("[ERROR]") || message.Contains("Exception") || message.Contains("Error:")) color = Windows.UI.Colors.OrangeRed;
            else if (message.StartsWith("[DIAG]")) color = Windows.UI.Colors.DarkGray;

            // RichTextBlock uses Blocks -> Paragraph -> Inlines
            if (DevConsoleText.Blocks.Count == 0)
                DevConsoleText.Blocks.Add(new Windows.UI.Xaml.Documents.Paragraph());
            
            var paragraph = (Windows.UI.Xaml.Documents.Paragraph)DevConsoleText.Blocks[0];
            
            // Append with color using Inlines
            var run = new Windows.UI.Xaml.Documents.Run
            {
                Text = message + "\n",
                Foreground = new SolidColorBrush(color)
            };

            // Limit log size to prevent UI lag
            if (paragraph.Inlines.Count > 500)
            {
                paragraph.Inlines.Clear();
                _devConsoleBuffer.Clear();
            }

            paragraph.Inlines.Add(run);
            _devConsoleBuffer.AppendLine(message);

            // Auto-scroll
            if (DevConsoleOutput != null)
                DevConsoleOutput.ChangeView(null, DevConsoleOutput.ScrollableHeight, null);
        }

        private void DevToolsLog(string message)
        {
            // This is for internal DevTools messages, not captured by TraceListener
            AppendDevToolsLog(message);
        }

        private void DevConsoleTab_Click(object sender, RoutedEventArgs e)
        {
            if (DevConsoleContent != null) DevConsoleContent.Visibility = Visibility.Visible;
            if (DevDomContent != null) DevDomContent.Visibility = Visibility.Collapsed;
            if (DevNetworkContent != null) DevNetworkContent.Visibility = Visibility.Collapsed;
            if (DevDebugContent != null) DevDebugContent.Visibility = Visibility.Collapsed;
            if (DevConsoleTab != null) DevConsoleTab.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC));
            if (DevDomTab != null) DevDomTab.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x88, 0x88, 0x88));
            if (DevNetworkTab != null) DevNetworkTab.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x88, 0x88, 0x88));
            if (DevDebugTab != null) DevDebugTab.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x88, 0x88, 0x88));
        }

        private void DevDomTab_Click(object sender, RoutedEventArgs e)
        {
            if (DevConsoleContent != null) DevConsoleContent.Visibility = Visibility.Collapsed;
            if (DevDomContent != null) DevDomContent.Visibility = Visibility.Visible;
            if (DevNetworkContent != null) DevNetworkContent.Visibility = Visibility.Collapsed;
            if (DevDebugContent != null) DevDebugContent.Visibility = Visibility.Collapsed;
            if (DevConsoleTab != null) DevConsoleTab.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x88, 0x88, 0x88));
            if (DevDomTab != null) DevDomTab.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC));
            if (DevNetworkTab != null) DevNetworkTab.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x88, 0x88, 0x88));
            if (DevDebugTab != null) DevDebugTab.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x88, 0x88, 0x88));

            // Populate DOM tree from active browser engine
            try
            {
                var dom = _browser.GetActiveDom();
                if (dom != null && DevDomText != null)
                    DevDomText.Text = DumpDomTree(dom, 0);
                else if (DevDomText != null)
                    DevDomText.Text = "(no DOM loaded)";
            }
            catch { }
        }

        private void DevNetworkTab_Click(object sender, RoutedEventArgs e)
        {
            if (DevConsoleContent != null) DevConsoleContent.Visibility = Visibility.Collapsed;
            if (DevDomContent != null) DevDomContent.Visibility = Visibility.Collapsed;
            if (DevNetworkContent != null) DevNetworkContent.Visibility = Visibility.Visible;
            if (DevDebugContent != null) DevDebugContent.Visibility = Visibility.Collapsed;
            if (DevConsoleTab != null) DevConsoleTab.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x88, 0x88, 0x88));
            if (DevDomTab != null) DevDomTab.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x88, 0x88, 0x88));
            if (DevNetworkTab != null) DevNetworkTab.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC));
            if (DevDebugTab != null) DevDebugTab.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x88, 0x88, 0x88));

            // Show network log from ResourceManager
            if (DevNetworkText != null)
                DevNetworkText.Text = _resources.GetNetworkLog();
        }

        private void DevDebugTab_Click(object sender, RoutedEventArgs e)
        {
            if (DevConsoleContent != null) DevConsoleContent.Visibility = Visibility.Collapsed;
            if (DevDomContent != null) DevDomContent.Visibility = Visibility.Collapsed;
            if (DevNetworkContent != null) DevNetworkContent.Visibility = Visibility.Collapsed;
            if (DevDebugContent != null) DevDebugContent.Visibility = Visibility.Visible;
            if (DevConsoleTab != null) DevConsoleTab.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x88, 0x88, 0x88));
            if (DevDomTab != null) DevDomTab.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x88, 0x88, 0x88));
            if (DevNetworkTab != null) DevNetworkTab.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x88, 0x88, 0x88));
            if (DevDebugTab != null) DevDebugTab.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC));

            // Show debug log (filtered [DIAG] messages)
            if (DevDebugText != null)
                DevDebugText.Text = _debugLogBuffer.ToString();
        }

        private void DevToolsClose_Click(object sender, RoutedEventArgs e)
        {
            DevToolsEnabled = false;
            // Also save to settings
            try { ApplicationData.Current.LocalSettings.Values["DevToolsEnabled"] = false; } catch { }
        }

        private void DevConsoleRun_Click(object sender, RoutedEventArgs e)
        {
            var code = DevConsoleInput?.Text?.Trim();
            if (string.IsNullOrEmpty(code)) return;

            // Show input in log
            AppendDevToolsLog("> " + code);
            DevConsoleInput.Text = "";

            try
            {
                // Try to evaluate as expression first
                var result = _browser.EvaluateExpression(code);
                if (result != null && result != "undefined")
                    AppendDevToolsLog("< " + result);
                else
                {
                    // If expression returned nothing, try running as statement
                    // Wrap in console.log to capture output
                    var wrappedCode = "try { var __r = (" + code + "); if(__r !== undefined) console.log(__r); } catch(e) { console.error(e); }";
                    _browser.EvaluateAsync(wrappedCode).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                AppendDevToolsLog("? " + ex.Message);
            }
        }

        private string DumpDomTree(BrowserCore.Engine.LiteElement node, int depth)
        {
            if (node == null) return "";
            var sb = new System.Text.StringBuilder();
            var indent = new string(' ', depth * 2);
            if (node.IsText)
            {
                var text = (node.Text ?? "").Trim();
                if (text.Length > 0)
                    sb.AppendLine(indent + "#text: \"" + (text.Length > 40 ? text.Substring(0, 40) + "..." : text) + "\"");
            }
            else
            {
                sb.Append(indent + "<" + (node.Tag ?? "unknown"));
                if (node.Attr != null)
                {
                    foreach (var kv in node.Attr)
                        sb.Append(" " + kv.Key + "=\"" + (kv.Value.Length > 20 ? kv.Value.Substring(0, 20) + "..." : kv.Value) + "\"");
                }
                sb.AppendLine(">");
                if (node.Children != null)
                    foreach (var child in node.Children)
                        sb.Append(DumpDomTree(child, depth + 1));
            }
            return sb.ToString();
        }
    }
}
