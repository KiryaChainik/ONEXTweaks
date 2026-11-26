// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private JsonObject _onexWifiState;
        private bool _onexWifiBusy;
        private string OnexWifiNetworkName=>_onexWifiState!=null&&_onexWifiState.TryGetValue("networkName",out var name)&&name.ValueType==JsonValueType.String?name.GetString():"";
        private bool OnexWifiOn=>_onexWifiState?.GetNamedBoolean("on",false)==true;
        private bool OnexWifiReady=>App.IsConnected&&!_onexWifiBusy&&_onexWifiState?.GetNamedBoolean("available",false)==true;
        private void ApplyOnexWifiState(string json)
        {if(JsonObject.TryParse(json,out var state))_onexWifiState=state;ApplyOnexTileAvailability();}
        private async Task ToggleOnexWifiAsync()
        {
            if(!OnexWifiReady)return;bool target=!OnexWifiOn;_onexWifiBusy=true;ApplyOnexTileAvailability();
            try{var reply=await SendOnexHomeRequestAsync(new ValueSet{{"OnexWifiSet",target}});ApplyOnexWifiState((string)reply["OnexWifiState"]);if(reply.TryGetValue("OnexAirplaneState",out var mode))ApplyOnexAirplaneState((string)mode);}
            catch(Exception e){ShowOnexDesktopStatus(e.Message);}
            finally{_onexWifiBusy=false;ApplyOnexTileAvailability();}
        }
        private void ApplyOnexWifiTile(TileDefinition tile)
        {
            if(tile.StateText==null)return;
            bool available=_onexWifiState?.GetNamedBoolean("available",false)==true,connected=_onexWifiState?.GetNamedBoolean("connected",false)==true;
            string name=OnexWifiNetworkName;
            string caption=_onexWifiBusy?OnexStrings.Get("Applying…"):!available?OnexStrings.Get("Unavailable"):!OnexWifiOn?OnexStrings.Get("Off"):connected?(string.IsNullOrWhiteSpace(name)?OnexStrings.Get("Connected"):name):OnexStrings.Get(OnexStrings.Get("Not connected"));
            tile.StateText.Text=caption;tile.StateText.TextWrapping=TextWrapping.NoWrap;tile.StateText.MaxLines=1;tile.StateText.TextTrimming=TextTrimming.CharacterEllipsis;
            ApplyOnexToggleTileVisual(tile,OnexWifiOn,available);
            if(tile.TileButton!=null)
            {
                string error=available?null:_onexWifiState!=null&&_onexWifiState.TryGetValue("error",out var message)&&message.ValueType==JsonValueType.String?message.GetString():OnexStrings.Get("Wi-Fi unavailable");
                ToolTipService.SetToolTip(tile.TileButton,error!=null?OnexStrings.Get(error):OnexStrings.Format("Wi-Fi · {0}\nTap to toggle · Hold / Gamepad X: Windows networks",caption));
                AutomationProperties.SetName(tile.TileButton,global::XboxGamingBar.OnexStrings.Format("Wi-Fi: {0}",caption));
            }
        }
    }
}
