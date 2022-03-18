using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Microsoft.Graphics.Canvas.Effects;
using Windows.UI.Xaml.Media;
using System;
using Microsoft.Graphics.Canvas;
using Windows.UI.Core;
using Windows.System.Power;
using Windows.UI.ViewManagement;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;

namespace UnitedCodebase.Brushes
{
    /// <summary>bb
    /// A lightweight control to add blur and tint effect.
    /// </summary>
    /// <seealso cref="Windows.UI.Xaml.Controls.Control" />
    public class UCAcrylicBrush : XamlCompositionBrushBase
    {
        public CompositionEffectBrush effectBrush;
        Compositor compositor = Window.Current.Compositor;
        CompositionBackdropBrush backdropBrush;

        UISettings DefaultTheme = new UISettings();

        static float sc_blurRadius = 30.0f;
        static float sc_noiseOpacity = 0.02f;
        static Color Sc_exclusionColor = new Color() { A = 26, R = 255, B = 255, G = 255 };
        static float sc_saturation = 1.25f;

        public bool FallbackForced
        {
            get { return (bool)GetValue(FallbackForcedProperty); }
            set { SetValue(FallbackForcedProperty, value); }
        }

        public static readonly DependencyProperty FallbackForcedProperty = DependencyProperty.Register(
            nameof(FallbackForced), typeof(bool), typeof(UCAcrylicBrush), new PropertyMetadata(false, OnFallbackForcedChanged));

        private static void OnFallbackForcedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            
        }

        public CustomAcrylicBackgroundSource BackgroundSource
        {
            get { return (CustomAcrylicBackgroundSource)GetValue(BackgroundSourceProperty); }
            set { SetValue(BackgroundSourceProperty, value); }
        }

        public static readonly DependencyProperty BackgroundSourceProperty = DependencyProperty.Register(
            nameof(BackgroundSource), typeof(CustomAcrylicBackgroundSource), typeof(UCAcrylicBrush), new PropertyMetadata(default(CustomAcrylicBackgroundSource), OnBackgroundSourceChanged));

        private static void OnBackgroundSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {

        }

        public Color TintColor
        {
            get { return (Color)GetValue(TintColorProperty); }
            set { SetValue(TintColorProperty, value); }
        }

        public static readonly DependencyProperty TintColorProperty = DependencyProperty.Register(
            nameof(TintColor), typeof(Color), typeof(UCAcrylicBrush), new PropertyMetadata(default(Color), OnTintColorChanged));

        private static void OnTintColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {

        }

        public double TintOpacity
        {
            get { return (double)GetValue(TintOpacityProperty); }
            set { SetValue(TintOpacityProperty, value); }
        }

        public static readonly DependencyProperty TintOpacityProperty = DependencyProperty.Register(
            nameof(TintOpacity), typeof(double), typeof(UCAcrylicBrush), new PropertyMetadata(default(double), OnTintOpacityChanged));

        private static void OnTintOpacityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {

        }

        private void ConnectAcrylicBrush(bool useFallback)
        {
            DisconnectAcryilicBrush();

            bool isWindowed = BackgroundSource == CustomAcrylicBackgroundSource.Hostbackdrop;
            if (useFallback == false && PowerManager.EnergySaverStatus != EnergySaverStatus.On && DefaultTheme.AdvancedEffectsEnabled
                && !(UIViewSettings.GetForCurrentView().UserInteractionMode == UserInteractionMode.Touch && isWindowed))
            {
                ArithmeticCompositeEffect crossFadeEffect = new ArithmeticCompositeEffect
                {
                    MultiplyAmount = 0f,
                    Source1Amount = (float)TintOpacity,
                    Source2Amount = 1 - (float)TintOpacity,
                    Source1 = new ColorSourceEffect
                    {
                        Color = TintColor
                    },
                    Source2 = new ArithmeticCompositeEffect
                    {
                        MultiplyAmount = 0f,
                        Source1Amount = sc_noiseOpacity,
                        Source2Amount = 0.98f,
                        Source1 = new BorderEffect
                        {
                            Source = new CompositionEffectSourceParameter("image"),
                            ExtendX = CanvasEdgeBehavior.Wrap,
                            ExtendY = CanvasEdgeBehavior.Wrap,
                        },
                        Source2 = new BlendEffect
                        {
                            Mode = BlendEffectMode.Exclusion,
                            Foreground = new ColorSourceEffect
                            {
                                Color = Sc_exclusionColor
                            },
                            Background = new SaturationEffect
                            {
                                Saturation = sc_saturation,
                                Source = new GaussianBlurEffect
                                {
                                    BlurAmount = sc_blurRadius,
                                    BorderMode = EffectBorderMode.Hard,
                                    Name = "Blur",
                                    Optimization = EffectOptimization.Balanced,
                                    Source = new CompositionEffectSourceParameter("source")
                                }
                            }
                        }
                    }
                };

                CompositionEffectFactory effectFactory = compositor.CreateEffectFactory(crossFadeEffect);
                effectBrush = effectFactory.CreateBrush();

                LoadedImageSurface imagesurface = LoadedImageSurface.StartLoadFromUri(new Uri("ms-appx:///UnitedCodebase/Assets/NoiseAsset_256X256_PNG.png"));

                CompositionSurfaceBrush imagebrush = compositor.CreateSurfaceBrush(imagesurface);
                imagebrush.Stretch = CompositionStretch.None;
                effectBrush.SetSourceParameter("image", imagebrush);

                if (BackgroundSource == CustomAcrylicBackgroundSource.Hostbackdrop)
                {
                    backdropBrush = compositor.CreateHostBackdropBrush();

                    effectBrush.SetSourceParameter("source", backdropBrush);
                }
                else
                {
                    backdropBrush = compositor.CreateBackdropBrush();

                    effectBrush.SetSourceParameter("source", backdropBrush);
                }

                CompositionBrush = effectBrush;
            }
            else
            {
                CompositionBrush = compositor.CreateColorBrush(FallbackColor);
            }
        }

        protected override void OnConnected()
        {
            bool usingFallback;

            if (BackgroundSource == CustomAcrylicBackgroundSource.Hostbackdrop)
            {
                usingFallback = !CompositionCapabilities.GetForCurrentView().AreEffectsSupported();
            }
            else
            {
                usingFallback = !CompositionCapabilities.GetForCurrentView().AreEffectsFast();
            }

            ConnectAcrylicBrush(usingFallback);

            CoreWindow.GetForCurrentThread().Activated += UCAcrylicBrush_Activated;
            CoreWindow.GetForCurrentThread().VisibilityChanged += UCAcrylicBrush_VisibilityChanged;
        }

        private void UCAcrylicBrush_Activated(CoreWindow sender, WindowActivatedEventArgs args)
        {
            ConnectAcrylicBrush(args.WindowActivationState == CoreWindowActivationState.Deactivated);
            if (args.WindowActivationState != CoreWindowActivationState.Deactivated)
            {
                PowerManager.EnergySaverStatusChanged += PowerManager_EnergySaverStatusChanged;
                DefaultTheme.AdvancedEffectsEnabledChanged += DefaultTheme_AdvancedEffectsEnabledChanged;
            }
            else
            {
                PowerManager.EnergySaverStatusChanged -= PowerManager_EnergySaverStatusChanged;
                DefaultTheme.AdvancedEffectsEnabledChanged -= DefaultTheme_AdvancedEffectsEnabledChanged;
            }

            
        }
        private async void DefaultTheme_AdvancedEffectsEnabledChanged(UISettings sender, object args)
        {
            var window = CoreWindow.GetForCurrentThread();
            await Task.Run(async () =>
            {
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    DisconnectAcryilicBrush();
                    ConnectAcrylicBrush(PowerManager.EnergySaverStatus == EnergySaverStatus.On);
                });
            });
        }

        private void UCAcrylicBrush_VisibilityChanged(CoreWindow sender, VisibilityChangedEventArgs args)
        {
            ConnectAcrylicBrush(!args.Visible);
        }

        private async void PowerManager_EnergySaverStatusChanged(object sender, object e)
        {
            var window = CoreWindow.GetForCurrentThread();
            await Task.Run(async () =>
            {
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    DisconnectAcryilicBrush();
                    ConnectAcrylicBrush(PowerManager.EnergySaverStatus == EnergySaverStatus.On);
                });
            });
        }

        private void DisconnectAcryilicBrush()
        {
            if (backdropBrush != null)
            {
                backdropBrush.Dispose();
                backdropBrush = null;
            }

            if (CompositionBrush != null)
            {
                CompositionBrush.Dispose();
                CompositionBrush = null;
            }
        }

        protected override void OnDisconnected()
        {
            DisconnectAcryilicBrush();
            CoreWindow.GetForCurrentThread().Activated -= UCAcrylicBrush_Activated;
            CoreWindow.GetForCurrentThread().VisibilityChanged -= UCAcrylicBrush_VisibilityChanged;
        }
    }

    public enum CustomAcrylicBackgroundSource
    {
        Hostbackdrop, Backdrop
    }
}