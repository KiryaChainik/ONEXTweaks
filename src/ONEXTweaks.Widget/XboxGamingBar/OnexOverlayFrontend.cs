// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private JsonObject _onexOverlayState;
        private bool _onexOverlayBusy,_onexOverlaySyncing;
        private TextBlock _onexOverlayCaption;
        private static readonly string[] OnexOverlayNames={"Off","Basic","Horizontal","Horizontal Custom","Vertical Custom"};
        private string _onexOverlayCommandError;
        private int OnexOverlayLevel=>(int)(_onexOverlayState?.GetNamedNumber("level",0)??0);
        private void ApplyOnexOverlayState(string json)
        {
            if(!JsonObject.TryParse(json,out var state))return;
            _onexOverlayState=state;_onexOverlaySyncing=true;
            try
            {
                osd.SuppressRemoteSync=true;try{osd.SetValue(OnexOverlayLevel,DateTimeOffset.Now.Ticks);}finally{osd.SuppressRemoteSync=false;}
                isLoadingPerformanceOverlaySetting=true;
                if(!_onexOverlayBusy&&!PerformanceOverlayComboBox.IsDropDownOpen)
                    PerformanceOverlayComboBox.SelectedIndex=OnexOverlayLevel;
                if(_onexOverlayCaption!=null)_onexOverlayCaption.Text=_onexOverlayCommandError??(state.TryGetValue("error",out var error)&&error.ValueType==JsonValueType.String?error.GetString():null)??(state.GetNamedBoolean("running",false)?"RTSS · "+OnexOverlayNames[OnexOverlayLevel]:OnexStrings.Get("RTSS starts when you enable the overlay."));
            }
            finally{isLoadingPerformanceOverlaySetting=false;_onexOverlaySyncing=false;}
            ApplyOnexTileAvailability();
        }
        private async Task SetOnexOverlayLevelAsync(int level)
        {
            if(_onexOverlayBusy||!App.IsConnected)return;
            _onexOverlayCommandError=null;_onexOverlayBusy=true;ApplyOnexTileAvailability();
            try{var reply=await SendOnexHomeRequestAsync(new ValueSet{["OnexOverlayLevel"]=level});ApplyOnexOverlayState((string)reply["OnexOverlayState"]);}
            catch(Exception ex)
            {
                _onexOverlayCommandError=ex.Message;
                Logger.Warn("RTSS overlay change failed: " + ex.Message);
                ShowOnexDesktopStatus(ex.Message);
            }
            finally
            {
                _onexOverlayBusy=false;
                if(_onexOverlayState!=null)ApplyOnexOverlayState(_onexOverlayState.Stringify());
                else ApplyOnexTileAvailability();
            }
        }
        private MenuFlyout ShowOnexOverlayMenu(Button anchor)
        {
            var menu=CreateOnexMenu();
            for(int i=0;i<OnexOverlayNames.Length;i++)
            {
                int level=i;var choice=new MenuFlyoutItem{Text=OnexOverlayNames[i]};if(i==OnexOverlayLevel)choice.Icon=new SymbolIcon(Symbol.Accept);
                choice.Click+=async(s,e)=>await SetOnexOverlayLevelAsync(level);menu.Items.Add(choice);
            }
            menu.Items.Add(new MenuFlyoutSeparator());var settings=new MenuFlyoutItem{Text=global::XboxGamingBar.OnexStrings.Get("Customize overlay"),Icon=new SymbolIcon(Symbol.Setting)};
            settings.Click+=(s,e)=>{_releaseOsdNav.IsChecked=true;_=OpenOnexOverlaySettingsAsync();};menu.Items.Add(settings);
            menu.ShowAt(anchor);return menu;
        }
        private void ApplyOnexOverlayTile(TileDefinition tile)
        {
            if(tile.StateText==null)return;
            bool installed=_onexOverlayState?.GetNamedBoolean("installed",false)==true;
            var preset=_onexOverlayPresets?.GetNamedArray("entries",null)?.Select(value=>value.GetObject()).FirstOrDefault(entry=>entry.GetNamedString("id")==_onexOverlayPresets.GetNamedString("appliedId",_onexOverlayPresets.GetNamedString("activeId","global")));
            tile.StateText.Text=!installed?global::XboxGamingBar.OnexStrings.Get("RTSS missing"):_onexOverlayBusy||_onexOverlayPresetChanging?global::XboxGamingBar.OnexStrings.Get("Applying…"):preset!=null?OnexCatalogEntryName(preset,true):new[]{"Off","Basic","Horizontal","H. Custom","V. Custom"}[OnexOverlayLevel];
            ToolTipService.SetToolTip(tile.TileButton,(preset==null?"":OnexCatalogEntryName(preset,true)+" · "+OnexOverlayTileBrief(preset)+"\n")+OnexStrings.Get("Tap to choose a preset. Hold for details · Gamepad X / Shift+F10"));
            ApplyOnexToggleTileVisual(tile,OnexOverlayLevel>0,installed);
            tile.StateText.Foreground=OnexOverlayLevel==0?OnexSettingsBrush(136,136,136):OnexSolidBrush(OnexBrandTheme?ThemeColors.Shade(CurrentThemeAccent(),.35):CurrentThemeAccent());
        }
    }
}
