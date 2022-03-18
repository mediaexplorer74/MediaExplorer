using Windows.ApplicationModel;
using Windows.Foundation.Metadata;
using Windows.Graphics.Display;

namespace UnitedCodebase.Classes
{
    public class TitleBarAPI
    {
        static DisplayInformation displayInformation = DisplayInformation.GetForCurrentView();

        public static string UserFriendlyTitle(string title)
        {
            if ((ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 2) && displayInformation.DiagonalSizeInInches < 4.5)
                || displayInformation.ResolutionScale > ResolutionScale.Scale350Percent)
            {
                return Package.Current.DisplayName;
            }
            else
            {
                if (title.Length > 18)
                {
                    title = string.Format("{0} ... {1}", title.Substring(0, 13), title.Substring(title.Length - 3));
                }
            }

            return string.Format("{0} – {1}", title, Package.Current.DisplayName);
        }
    }
}
