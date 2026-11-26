using System;
using Shared.Enums;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class CPUBoostProperty : WidgetToggleProperty
    {
        protected override void ToggleSwitch_ValueChanged(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            if(OnexFrontend.UsesOwnBackend)
            {
                if(!IsUpdatingUI && WidgetSliderProperty.HelperSyncCount==0)(Owner as GamingWidget)?.OnexBoostToggleChanged(UI.IsOn);
                return;
            }
            base.ToggleSwitch_ValueChanged(sender,e);
        }
        public CPUBoostProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.CPUBoost, inUI, inOwner)
        {
        }
    }
}
