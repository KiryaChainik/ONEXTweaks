// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Automation.Peers;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexTabPaletteAsync(JsonObject report)
        {
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            Color ColorOf(Brush brush)=>(brush as SolidColorBrush)?.Color??Colors.Transparent;
            async Task Painted(){await Task.Delay(180);ApplyOnexTabPalette();}
            var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);Check(OnexBrandTheme,"Owner alternate theme protected; brand palette QA deferred");
            var before=await ReadOnexStateAsync();var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");int step=0;
            async Task Input(string key,Func<bool> ready)
            {
                string id=run+":"+(++step);var rect=new RadioButtonAutomationPeer(PerformanceNavItem).GetBoundingRectangle();
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2)}}.Stringify());
                bool accepted=false;for(int n=0;n<100;n++){await Task.Delay(80);try{accepted=await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&ready();}catch{}if(accepted)break;}Check(accepted,"Palette controller navigation failed: "+key);await Task.Delay(120);
            }
            try
            {
                PerformanceNavItem.IsChecked=true;await Painted();Check(ColorOf(RailBrightnessSlider.Foreground)==OnexPalette.Cyan&&ColorOf(RailVolumeSlider.Foreground)==OnexPalette.Cyan,"Home media cyan changed");await SaveOnexImage("onex-palette-home228.png",_releaseHome,150);
                await Input("focus",()=>true);int count=_onexTriggerNavigationCount;await Input("rt",()=>_onexTriggerNavigationCount>count);count=_onexTriggerNavigationCount;await Input("lt",()=>_onexTriggerNavigationCount>count&&PerformanceNavItem.IsChecked==true);
                _releasePowerNav.IsChecked=true;await Painted();Check(_onexPerfSections.Select(card=>OnexPerformanceAccent(card.Id)).Distinct().Count()>=4,"Performance cards are monochrome");
                foreach(var card in _onexPerfSections){var icon=OnexOwnedContent(card.Header.Content as DependencyObject).OfType<FontIcon>().First();Check(ColorOf(icon.Foreground)==OnexPerformanceAccent(card.Id),"Performance icon does not match its section");}await SaveOnexImage("onex-palette-performance228.png",PerformanceScrollViewer,150);
                DisplayNavItem.IsChecked=true;await Painted();Check(ColorOf(DisplaySaturationSlider.Foreground)==OnexPalette.Violet,"Display controls are not violet");Check(_onexHdrCalibration.FocusVisualMargin.Left>=0,"Display combo focus extends beyond bounds");await SaveOnexImage("onex-palette-screen228.png",DisplayScrollViewer,150);
                FanNavItem.IsChecked=true;await Painted();Check(_onexFanCustomToggle.Resources["SystemControlHighlightAccentBrush"] is SolidColorBrush fan&&fan.Color==OnexPalette.Cyan,"Fan controls are not cyan");await SaveOnexImage("onex-palette-fan228.png",FanScrollViewer,150);
                _releaseOsdNav.IsChecked=true;await OpenOnexOverlaySettingsAsync();await Painted();Check(ColorOf(OSDOpacitySlider.Foreground)==OnexPalette.Cyan,"Overlay controls are not cyan");await SaveOnexImage("onex-palette-overlay228.png",OnexOverlayScrollViewer,150);
                PerformanceNavItem.IsChecked=true;await Painted();var opening=ShowOnexResolutionSettingsAsync(qsTileMap["OnexResolution"].TileButton);for(int n=0;n<120&&_onexDetailsDialog?.IsLoaded!=true;n++)await Task.Delay(50);
                Check(_onexDetailsDialog?.IsLoaded==true&&_onexResolutionChoice.FocusVisualMargin.Left>=0,"Popup ComboBox focus bounds invalid");Check(_onexResolutionChoice.Resources["SystemControlHighlightAccentBrush"] is SolidColorBrush popup&&popup.Color==OnexPalette.Orange,"Home tile popup lost orange accent");_onexResolutionChoice.Focus(FocusState.Keyboard);await Task.Delay(120);_onexDetailsDialog.UpdateLayout();
                var popupViewport=(ScrollViewer)_onexDetailsDialog.Content;double popupRight=_onexResolutionChoice.TransformToVisual(popupViewport).TransformPoint(new Windows.Foundation.Point(_onexResolutionChoice.ActualWidth,0)).X;
                Check(popupRight<=popupViewport.ViewportWidth+.5,"Popup control extends beyond its viewport");report["popupControlRight"]=JsonValue.CreateNumberValue(popupRight);report["popupViewportWidth"]=JsonValue.CreateNumberValue(popupViewport.ViewportWidth);
                await SaveOnexImage("onex-palette-popup228.png",_onexDetailsDialog,150);CloseOnexMenus();await opening;
                Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==before.GetNamedString("rawLimits"),"Palette changed TDP");
                report["fourPerformanceAccents"]=JsonValue.CreateBooleanValue(true);report["screenVioletFanOverlayCyan"]=JsonValue.CreateBooleanValue(true);report["homeMediaCyanPopupOrangePreserved"]=JsonValue.CreateBooleanValue(true);report["focusInsetsNonnegative"]=JsonValue.CreateBooleanValue(true);report["injectedControllerSteps"]=JsonValue.CreateNumberValue(step);report["hardwareWrites"]=JsonValue.CreateNumberValue(0);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally{CloseOnexMenus();if(original!=null)original.IsChecked=true;await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");}
        }
    }
}
