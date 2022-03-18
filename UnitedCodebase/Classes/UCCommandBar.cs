using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace UnitedCodebase.Classes
{
    public class UCCommandBar : CommandBar
    {
        public UCCommandBar()
        {
        }

        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            if (GetTemplateChild("LayoutRoot") is Grid layoutRoot)
            {
                VisualStateManager.SetCustomVisualStateManager(layoutRoot, new UCCommandBarVisualStateManager());
            }
        }
    }

    public class UCCommandBarVisualStateManager : VisualStateManager
    {
        protected override bool GoToStateCore(Control control, FrameworkElement templateRoot, string stateName, VisualStateGroup group, VisualState state, bool useTransitions)
        {
            //replace OpenUp state change with OpenDown one and continue as normal
            if (!string.IsNullOrWhiteSpace(stateName) && stateName.EndsWith("OpenUp"))
            {
                stateName = stateName.Substring(0, stateName.Length - 6) + "OpenDown";
            }

            return base.GoToStateCore(control, templateRoot, stateName, group, state, useTransitions);
         }   
    }
}
