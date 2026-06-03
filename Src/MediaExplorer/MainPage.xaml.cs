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
        private readonly CustomHtmlEngine _welcomeEngine = new CustomHtmlEngine();
        private readonly BrowserHost _browser;
        private bool _barExpanded = false;
        private string _appBarMode = "Semi"; // "Full", "Semi", "Hided"

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

            // Bottom bar — mouse & touch
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
                    try { BottomBar.Clip = new RectangleGeometry { Rect = new Rect(0, 0, BottomBar.ActualWidth, BottomBar.ActualHeight) }; }
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
            _browser.StatusMessage += (s, msg) => UpdateStatusMessage(msg);
            _browser.LoadingChanged += (s, loading) => Ui(() =>
            {
                if (LoadingRing != null) LoadingRing.IsActive = loading;
                if (LoadingOverlay != null) LoadingOverlay.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            });
            _browser.RepaintReady += (s, element) => Engine_RepaintReady(element);

            SizeChanged += MainPage_SizeChanged;

            Loaded += (s, e) => { try { ApplyAppBarMode(); ApplyRenderMode(); ApplyDevTools(); ApplyStatusBar(); } catch { } };
            try { ApplyAppBarMode(); } catch { }
            try { ApplyRenderMode(); } catch { }
            try { ApplyDevTools(); } catch { }
            try { ApplyStatusBar(); } catch { }

            Task.Run(() => RunNilJsStartupTest());
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
                if (!_startupStatusPinned)
                UpdateStatusMessage("Enter a URL and press Go.");
            if (!_welcomeShown)
            {
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
            if (BottomBar != null) BottomBar.Height = 52;
            if (BarStrip != null) BarStrip.IsHitTestVisible = false;
            if (BarContent != null) BarContent.IsHitTestVisible = true;
            UpdateBarClip();
        }

        private void CollapseBar()
        {
            if (!_barExpanded || _suppressBarCollapse) return;
            if (_appBarMode == "Full") return;
            _barExpanded = false;
            if (_appBarMode == "Hided")
            {
                if (BottomBar != null) BottomBar.Height = 6;
            }
            else
            {
                if (BottomBar != null) BottomBar.Height = 24;
            }
            if (BarStrip != null) BarStrip.IsHitTestVisible = true;
            if (BarContent != null) BarContent.IsHitTestVisible = false;
            UpdateBarClip();
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
                    // Long press — reuse existing AiOverlay + OpenRouter summary
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
                    BottomBar.Clip = new RectangleGeometry { Rect = new Rect(0, 0, BottomBar.ActualWidth, BottomBar.ActualHeight) };
            }
            catch { }
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
            if (!overrideStartup && _startupStatusPinned) return;
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
            System.Diagnostics.Debug.WriteLine("[DIAG] MainPage.NavigateAsync START seq=" + _renderSequence + " address=" + address);
            CollapseBar();
            if (string.IsNullOrWhiteSpace(address))
            {
                UpdateStatusMessage("Enter a URL.");
                return;
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
                    UpdateStatusMessage("Unknown about: page — " + address);
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
            await _browser.NavigateAsync(address);
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
                    var sp = new StackPanel { Margin = new Thickness(12) };
                    sp.Children.Add(new TextBlock { Text = "Welcome", FontSize = 20, FontWeight = Windows.UI.Text.FontWeights.SemiBold });
                    sp.Children.Add(new TextBlock { Text = "Type a URL in the bar above and press Enter.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
                    elt = new Border { Background = new SolidColorBrush(Windows.UI.Colors.White), Child = sp };
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
            if (element == null) { System.Diagnostics.Debug.WriteLine("[DIAG] Engine_RepaintReady SKIP element=null"); return; }
            if (_suppressRepaintHandler) { System.Diagnostics.Debug.WriteLine("[DIAG] Engine_RepaintReady SKIP suppressed"); return; }
            int seq = _renderSequence;
            double w = 0, h = 0;
            try { w = element.Width; h = element.Height; } catch { }
            
            // NaN guard: use ActualWidth/ActualHeight as fallback
            bool hasNaN = double.IsNaN(w) || double.IsNaN(h) || double.IsInfinity(w) || double.IsInfinity(h);
            if (hasNaN)
            {
                try { w = element.ActualWidth; h = element.ActualHeight; } catch { }
                if (double.IsNaN(w) || double.IsInfinity(w)) w = 0;
                if (double.IsNaN(h) || double.IsInfinity(h)) h = 0;
            }
            
            System.Diagnostics.Debug.WriteLine("[DIAG] Engine_RepaintReady seq=" + seq + " currentSeq=" + _renderSequence + " elementSize=" + w + "x" + h + " type=" + element.GetType().Name);
            Ui(() =>
            {
                try
                {
                    if (ContentHost == null) { System.Diagnostics.Debug.WriteLine("[DIAG] Engine_RepaintReady SKIP ContentHost=null"); return; }
                    if (seq != _renderSequence) { System.Diagnostics.Debug.WriteLine("[DIAG] Engine_RepaintReady SKIP stale seq=" + seq + " current=" + _renderSequence); return; }
                    ContentHost.Children.Clear();
                    ContentHost.Children.Add(element);
                    _activeVisual = element;
                    _activeVisualIndex = ContentHost.Children.IndexOf(element);
                    System.Diagnostics.Debug.WriteLine("[DIAG] Engine_RepaintReady ADDED idx=" + _activeVisualIndex + " children=" + ContentHost.Children.Count);
                    ApplyAppBarMode();
                    if (string.Equals(_browser.RenderMode, "Rich", StringComparison.OrdinalIgnoreCase) && !_suppressRepaintHandler)
                        StartReadingMode();
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[DIAG] Engine_RepaintReady EXC " + ex.Message); }
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
            // to avoid infinite render loop (each render changes layout → fires SizeChanged).
            // Future: re-evaluate CSS media queries without re-fetching HTML.
        }

        public void ShowGlobalError(string message)
        {
            UpdateStatusMessage("Error: " + (message ?? string.Empty), overrideStartup: true);
            try { if (LoadingRing != null) LoadingRing.IsActive = false; } catch { }
            try { if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Collapsed; } catch { }
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
                ShowToast("📷 Snapshot saved: " + filename, 3000);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Snapshot] ERROR: " + ex.GetType().Name + " — " + ex.Message);
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
                System.Diagnostics.Debug.WriteLine("[Snapshot] OUTER error: " + ex.GetType().Name + " — " + ex.Message);
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
            ShowToast("📷 Full-page snapshot saved: " + filename, 3000);
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
                ShowToast("📋 Copied " + text.Length + " characters to clipboard", 2000);
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
            return null;
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
            _startupStatusPinned = false;
            _startupStatusMessage = null;
        }

        // --- Swipe navigation ---
        private void ContentArea_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            try
            {
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
                if (string.IsNullOrWhiteSpace(key))
                {
                    ShowAiResult("No API key configured. Open Settings and enter your OpenRouter API key.", false);
                    return;
                }

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

                // Truncate to ~4000 tokens (~16000 chars)
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
                    AppendDevToolsLog("← " + result);
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
                AppendDevToolsLog("✕ " + ex.Message);
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
