// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private JsonObject _onexAirplaneState;
        private bool _onexAirplaneBusy;
        private bool OnexAirplaneOn=>_onexAirplaneState?.GetNamedBoolean("on",false)==true;
        private bool OnexAirplaneReady=>App.IsConnected&&!_onexAirplaneBusy&&_onexAirplaneState?.GetNamedBoolean("available",false)==true;
        private void ApplyOnexAirplaneState(string json){if(JsonObject.TryParse(json,out var state))_onexAirplaneState=state;ApplyOnexTileAvailability();}
        private async Task SetOnexAirplaneAsync(bool on)
        {
            if(!OnexAirplaneReady)return;_onexAirplaneBusy=true;ApplyOnexTileAvailability();
            try
            {
                var reply=await SendOnexHomeRequestAsync(new ValueSet{{"OnexAirplaneSet",on}});
                ApplyOnexAirplaneState((string)reply["OnexAirplaneState"]);ApplyOnexWifiState((string)reply["OnexWifiState"]);_=RefreshOnexBluetoothAsync();
            }
            catch(Exception error){ShowOnexDesktopStatus(error.Message);}
            finally{_onexAirplaneBusy=false;ApplyOnexTileAvailability();}
        }
        private void ApplyOnexAirplaneTile(TileDefinition tile)
        {
            if(tile.StateText==null)return;bool available=_onexAirplaneState?.GetNamedBoolean("available",false)==true;
            string caption=OnexStrings.Get(_onexAirplaneBusy?"Applying…":!available?"Unavailable":OnexAirplaneOn?"On":"Off");
            tile.StateText.Text=caption;tile.StateText.MaxLines=1;tile.StateText.TextWrapping=TextWrapping.NoWrap;ApplyOnexToggleTileVisual(tile,OnexAirplaneOn,available);
            if(tile.TileButton!=null){ToolTipService.SetToolTip(tile.TileButton,available?global::XboxGamingBar.OnexStrings.Format("Airplane mode · {0}\nEnabling Wi-Fi turns airplane mode off.",caption):global::XboxGamingBar.OnexStrings.Get("Windows airplane mode control is unavailable"));Windows.UI.Xaml.Automation.AutomationProperties.SetName(tile.TileButton,global::XboxGamingBar.OnexStrings.Format("Airplane mode: {0}",caption));}
        }
    }
}
