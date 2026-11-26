// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexPaletteAsync(JsonObject report)
        {
            var values=ApplicationData.Current.LocalSettings.Values;
            string ColorOf(Brush b)=>(b as SolidColorBrush)?.Color.ToString();
            string layout=string.Join(",",qsTileDefinitions.OrderBy(t=>t.Order).Select(t=>t.Id+":"+t.IsVisible));
            try
            {
                report["startupTheme"]=JsonValue.CreateStringValue(currentThemeName);
                if(currentThemeName!=OnexPalette.Name||values["WidgetTheme"] as string!=OnexPalette.Name||ThemeComboBox.Items.OfType<ComboBoxItem>().All(i=>i.Content as string!=OnexPalette.Name))throw new InvalidOperationException("ONEXPLAYER theme not loaded/persisted/selectable");
                ApplyTheme("Default");ApplyTheme(OnexPalette.Name);PerformanceNavItem.IsChecked=true;await Task.Delay(250);
                if(ColorOf(Background)!=OnexPalette.Page.ToString()||ColorOf((Brush)Resources["SystemAccentBrush"])!=OnexPalette.Orange.ToString()||ColorOf((Brush)Resources["CardBackgroundBrush"])!=OnexPalette.Card.ToString())throw new InvalidOperationException("Brand surfaces/accent do not match palette");
                if(ColorOf(qsTileMap["Power"].StateText.Foreground)!="#FFA0A0A0"||ColorOf(qsTileMap["PowerMode"].StateText.Foreground)!="#FFA0A0A0")throw new InvalidOperationException("Ordinary captions lost their neutral color");
                foreach(var tile in qsTileDefinitions.Where(t=>t.TileButton!=null))if(ColorOf(tile.TileButton.Background)!=OnexPalette.Card.ToString())throw new InvalidOperationException("Tile has colored active background: "+tile.Id);
                if(ColorOf(RailBrightnessSlider.Foreground)!=OnexPalette.Cyan.ToString()||ColorOf(RailVolumeSlider.Foreground)!=OnexPalette.Cyan.ToString()||ColorOf(_releaseHome.PowerInput.Foreground)!=OnexPalette.Orange.ToString())throw new InvalidOperationException("Power/media accents inconsistent");
                if(layout!=string.Join(",",qsTileDefinitions.OrderBy(t=>t.Order).Select(t=>t.Id+":"+t.IsVisible)))throw new InvalidOperationException("Theme switch changed tile layout");
                await SaveOnexImage("onex-brand-home.png");
                _releasePowerNav.IsChecked=true;PerformanceScrollViewer.ChangeView(null,0,null,true);await Task.Delay(150);await SaveOnexImage("onex-brand-performance.png");
                PerformanceNavItem.IsChecked=true;var menu=ShowOnexCpuSettingsAsync(qsTileMap["CPUBoost"].TileButton);await Task.Delay(300);var dialog=_onexDetailsDialog;if(dialog==null||ColorOf(dialog.Background)!=OnexPalette.Card.ToString())throw new InvalidOperationException("CPU menu did not use brand surface");
                var primary=dialog.PrimaryButtonStyle.Setters.OfType<Setter>().First(s=>s.Property==Control.BackgroundProperty).Value as Brush;if(ColorOf(primary)!=OnexPalette.Orange.ToString())throw new InvalidOperationException("Menu Apply button not branded");var actualButton=FindOnexSettingsControls<Button>(dialog).First(b=>b.Content as string=="Apply");report["actualApplyBackground"]=JsonValue.CreateStringValue(ColorOf(actualButton.Background)??"none");if(ColorOf(actualButton.Background)!=OnexPalette.Orange.ToString())throw new InvalidOperationException("WinUI default-button style overrode brand accent");await SaveOnexImage("onex-brand-cpu-menu.png",dialog);dialog.Hide();await menu;
                report["orange"]=JsonValue.CreateStringValue(OnexPalette.Orange.ToString());report["cyan"]=JsonValue.CreateStringValue(OnexPalette.Cyan.ToString());report["page"]=JsonValue.CreateStringValue(OnexPalette.Page.ToString());report["ordinaryCaptionsNeutral"]=JsonValue.CreateBooleanValue(true);report["activeTileBackgroundNeutral"]=JsonValue.CreateBooleanValue(true);report["menuStyled"]=JsonValue.CreateBooleanValue(true);report["themeSwitchPreservesLayout"]=JsonValue.CreateBooleanValue(true);
            }
            finally{CloseOnexMenus();ApplyTheme(OnexPalette.Name);values["WidgetTheme"]=OnexPalette.Name;PerformanceNavItem.IsChecked=true;}
        }
    }
}
