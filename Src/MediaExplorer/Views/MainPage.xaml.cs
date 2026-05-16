using Messenger.UWP.Converters;
using Messenger.UWP.Helpers;
using Messenger.UWP.ViewModels;
using System;
using System.ComponentModel.DataAnnotations;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;

namespace Messenger.UWP.Views
{
    public sealed partial class MainPage : Page
    {
        public MainViewModel ViewModel { get; }

        private WebView webView;
        private WebViewInjector webViewInjector;

        public MainPage()
        {
            InitializeComponent();
            InitializeBackground();
            InitializeWebView();



            // Experimental !! Canvas Size Changed if Update Splash Screen Image Location
            //Canvas.SizeChanged += UpdateSplashScreenImageLocation;

            var appView = ApplicationView.GetForCurrentView();
            appView.SetDesiredBoundsMode(ApplicationViewBoundsMode.UseCoreWindow);
            appView.VisibleBoundsChanged += UpdateContainerLocation;

            ViewModel = new MainViewModel(webView.Navigate);
            DataContext = ViewModel;

            // !!!
            AddTextToDConcole(App.UserAgentMarker);
        }

        private void InitializeBackground()
        {
            if (App.IsAcrylicAvailable)
            {
                /*
                var acrylicBrush = new AcrylicBrush()
                {
                    BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
                    TintColor = Colors.White,
                    FallbackColor = Colors.White,
                    TintOpacity = 0.8,
                };

                BindingOperations.SetBinding(acrylicBrush, AcrylicBrush.AlwaysUseFallbackProperty,
                    new Binding() { Path = new PropertyPath("ShowIcon") });

                Background = acrylicBrush;
                */
                Background = new SolidColorBrush(Colors.DarkGray);
            }
            else
            {
                Background = new SolidColorBrush(Colors.White);
            }
        }

        private void InitializeWebView()
        {
            webViewInjector = new WebViewInjector();

            webViewInjector.AddCss("/Web/bundle.css");
            
            webViewInjector.AddJavaScript("/Web/bundle.js");

            webView = new WebView(WebViewExecutionMode.SameThread);
            
            webView.DefaultBackgroundColor = Colors.Transparent;
            
            webView.NavigationStarting += WebViewNavigationStarting;
            
            webView.NavigationCompleted += WebViewNavigationCompleted;
            
            webView.PermissionRequested += WebViewPermissionRequested;

            Grid.SetRowSpan(webView, 2);
            
            webView.SetBinding(WebView.VisibilityProperty, new Binding
            {
                Path = new PropertyPath("ShowIcon"),
                Converter = new BooleanToVisibilityConverter { Mode = BooleanToVisibilityMode.TrueIsCollapsed }
            });

            Container.Children.Insert(0, webView);
        }

        private void WebViewNavigationStarting(WebView sender, WebViewNavigationStartingEventArgs e)
        {
            Uri UriAttribute = e.Uri;

            //AddItemToDConcole(UriAttribute);

            if (ViewModel != null)
            {
                ViewModel.State = NavigationState.Loading;

                Canvas.SizeChanged += UpdateSplashScreenImageLocation;
            }


        }

        private async void WebViewNavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                // Experimental
                AddItemToDConcole(e.Uri);

                await webViewInjector.InjectAsync(sender);
            }

            if (ViewModel != null)
            {
                //ViewModel.State = e.IsSuccess ? NavigationState.Succeeded : NavigationState.Failed;
                if (e.IsSuccess)
                {
                    ViewModel.State = NavigationState.Succeeded;
                }
                else 
                {
                    ViewModel.State = NavigationState.Failed;
                }
               
            }
        }

        private void WebViewPermissionRequested(WebView sender, WebViewPermissionRequestedEventArgs e)
        {
            var uriHost = e.PermissionRequest.Uri.Host;
            /*
            if (uriHost == "www.messenger.com" || uriHost == "messenger.com")
            {
                if (e.PermissionRequest.PermissionType == WebViewPermissionType.Media)
                    e.PermissionRequest.Allow();
            }
            */
        }

        private void RetryClick(object sender, RoutedEventArgs e)
        {
            ViewModel.Reload(webView.Source);
        }

        private void UpdateSplashScreenImageLocation(object sender, SizeChangedEventArgs e)
        {
            var imageLocation = App.SplashScreen.GetFixedImageLocation();

            Canvas.SetLeft(SplashScreenImage, imageLocation.X);
            Canvas.SetTop(SplashScreenImage, imageLocation.Y);
            SplashScreenImage.Height = imageLocation.Height;
            SplashScreenImage.Width = imageLocation.Width;

            UpdateContainerLocation(ApplicationView.GetForCurrentView());
        }

        private void UpdateContainerLocation(ApplicationView sender, object args = null)
        {
            var visibleBounds = sender.VisibleBounds;

            if (App.IsWindowsMobile)
            {
                var isNotContinuum = UIViewSettings.GetForCurrentView().UserInteractionMode != UserInteractionMode.Mouse;
                if (isNotContinuum)
                    Canvas.SetLeft(Container, visibleBounds.X);
                Canvas.SetTop(Container, visibleBounds.Y);
            }
            Container.Height = visibleBounds.Height;
            Container.Width = visibleBounds.Width;
        }


        

        private void GotoClick(object sender, RoutedEventArgs e)
        {
            //string url = "https://www.ya.ru";
            string url = addressBox.Text.ToLower();

            if (!url.Contains("http://") || !url.Contains("https://"))
            {
                url = "http://" + url;
            }

            ViewModel.Reload(new Uri(url));
        }

        private void addressBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                string url = addressBox.Text.ToLower();

                if (!url.Contains("http://") || !url.Contains("https://"))
                {
                    url = "http://" + url;
                }

                ViewModel.Reload(new Uri(url));

                
            }
        }


        public void AddItemToDConcole(Uri uri)
        {
            ListViewItem item = new ListViewItem();

            item.Content = uri;
            
            // добавляем элемент в ListView

            DConsole.Items.Add(item);
        }


        public void AddTextToDConcole(string txt)
        {
            ListViewItem item = new ListViewItem();

            item.Content = txt;
           

            DConsole.Items.Add(item);
        }
    }
}
