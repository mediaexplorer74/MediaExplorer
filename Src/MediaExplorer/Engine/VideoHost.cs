using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace BrowserCore.Engine
{
    // Inline video host that feels like a browser <video> element.
    // Minimal controls: tap to play/pause; optional overlay button.
    internal sealed class VideoHost : Grid
    {
        private readonly MediaElement _media;
        private readonly Grid _overlay;
        private readonly Border _playButton;
        private bool _controls;
        private bool _started;

        public VideoHost()
        {
            Background = new SolidColorBrush(Colors.Black);

            _media = new MediaElement
            {
                AutoPlay = false,
                AreTransportControlsEnabled = false // on some SKUs; harmless if ignored
            };

            _overlay = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            _playButton = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(24),
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "▶", Foreground = new SolidColorBrush(Colors.White), FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };

            _overlay.Children.Add(_playButton);

            Children.Add(_media);
            Children.Add(_overlay);

            Tapped += OnTapped;
        }

        public void SetControls(bool showControls)
        {
            _controls = showControls;
            _overlay.Visibility = showControls ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetPoster(ImageSource poster)
        {
            // Optional: in future overlay an Image before playback
        }

        public void SetSource(Uri uri)
        {
            try { _media.Source = uri; } catch { System.Diagnostics.Debug.WriteLine(" [Engine/VideoHost.cs] empty catch empty catch"); }
        }

        private void OnTapped(object sender, TappedRoutedEventArgs e)
        {
            try
            {
                if (!_started)
                {
                    _started = true;
                    _overlay.Visibility = _controls ? Visibility.Collapsed : Visibility.Collapsed;
                    _media.Play();
                    return;
                }
                if (_media.CurrentState == MediaElementState.Playing) _media.Pause(); else _media.Play();
            }
            catch { System.Diagnostics.Debug.WriteLine(" [Engine/VideoHost.cs] empty catch empty catch"); }
        }
    }
}

