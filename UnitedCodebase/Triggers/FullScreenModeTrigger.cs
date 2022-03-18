using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;

namespace UnitedCodebase.Triggers
{
    public class FullScreenModeTrigger : StateTriggerBase
    {
        public bool IsFullScreen
        {
            get { return (bool)GetValue(IsFullScreenProperty); }
            set { SetValue(IsFullScreenProperty, value); }
        }

        public static readonly DependencyProperty IsFullScreenProperty =
            DependencyProperty.Register("IsFullScreen", typeof(bool),
            typeof(FullScreenModeTrigger),
            new PropertyMetadata(false, OnIsFullScreenPropertyChanged));

        private static void OnIsFullScreenPropertyChanged(DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            FullScreenModeTrigger obj = (FullScreenModeTrigger)d;
            if (!Windows.ApplicationModel.DesignMode.DesignModeEnabled)
            {
                var isFullScreen = ApplicationView.GetForCurrentView().IsFullScreenMode;
                obj.UpdateTrigger(isFullScreen);
            }
        }

        public FullScreenModeTrigger()
        {
            if (!Windows.ApplicationModel.DesignMode.DesignModeEnabled)
            {
                ApplicationView.GetForCurrentView().VisibleBoundsChanged +=
                    FullScreenModeTrigger_VisibleBoundsChanged;
                Window.Current.CoreWindow.SizeChanged += CoreWindow_SizeChanged;
                Window.Current.CoreWindow.Activated += CoreWindow_Activated;
                Window.Current.CoreWindow.VisibilityChanged += CoreWindow_VisibilityChanged;
            }
        }

        private void CoreWindow_VisibilityChanged(Windows.UI.Core.CoreWindow sender, Windows.UI.Core.VisibilityChangedEventArgs args)
        {
            UpdateTrigger(ApplicationView.GetForCurrentView().IsFullScreenMode);
        }

        private void CoreWindow_Activated(Windows.UI.Core.CoreWindow sender, Windows.UI.Core.WindowActivatedEventArgs args)
        {
            UpdateTrigger(ApplicationView.GetForCurrentView().IsFullScreenMode);
        }

        private void CoreWindow_SizeChanged(Windows.UI.Core.CoreWindow sender, Windows.UI.Core.WindowSizeChangedEventArgs args)
        {
            UpdateTrigger(ApplicationView.GetForCurrentView().IsFullScreenMode);
        }

        private void FullScreenModeTrigger_VisibleBoundsChanged(ApplicationView sender,
            object args)
        {
            UpdateTrigger(sender.IsFullScreenMode);
        }

        private void UpdateTrigger(bool isFullScreen)
        {
            SetActive(isFullScreen == IsFullScreen);
        }
    }
}
