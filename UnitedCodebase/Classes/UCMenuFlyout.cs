using Windows.Foundation.Metadata;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace UnitedCodebase.Classes
{
    public class UCMenuFlyout : MenuFlyout
    {
        public UCMenuFlyout()
        {
            Opened += UCMenuFlyout_Opened;
            Closed += UCMenuFlyout_Closed;
        }

        public bool IsMenuOpened;
        public int FixerTImes = 0;
        private void UCMenuFlyout_Opened(object sender, object e)
        {
            IsMenuOpened = true;

            if (ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 3))
            {
                if (Target is RichEditBox || Target is WebView || Target is TextBox || Target is AutoSuggestBox || Target is TextBlock)
                {
                    if (Target is RichEditBox)
                    {
                        ((RichEditBox)Target).PreventKeyboardDisplayOnProgrammaticFocus = true;
                    }

                    if (Target is TextBox)
                    {
                        ((TextBox)Target).PreventKeyboardDisplayOnProgrammaticFocus = true;
                    }

                    if (Target is AutoSuggestBox)
                    {
                        ((AutoSuggestBox)Target).IsSuggestionListOpen = false;
                    }

                    if (InputPane.GetForCurrentView().Visible)
                    {
                        InputPane.GetForCurrentView().TryHide();
                    }

                    ApiResources.Vibrate(50);                    
                }
            }
            else
            {
               ApiResources.Vibrate(50);
            }
        }

        private void UCMenuFlyout_Closed(object sender, object e)
        {
            IsMenuOpened = false;

            if (ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 3))
            {
                if (Target is RichEditBox)
                {
                    ((RichEditBox)Target).PreventKeyboardDisplayOnProgrammaticFocus = false;

                    ((RichEditBox)Target).Focus(FocusState.Keyboard);
                }

                if (Target is WebView)
                {
                    ((WebView)Target).Focus(FocusState.Keyboard);
                }

                if (Target is TextBox)
                {
                    ((TextBox)Target).PreventKeyboardDisplayOnProgrammaticFocus = false;

                    ((TextBox)Target).Focus(FocusState.Keyboard);
                }

                if (Target is AutoSuggestBox)
                {
                    ((AutoSuggestBox)Target).Focus(FocusState.Keyboard);
                }

                if (Target is TextBlock)
                {
                    ((TextBlock)Target).Focus(FocusState.Keyboard);
                }
            }
        }
    }
}
