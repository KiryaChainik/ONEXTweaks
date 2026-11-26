// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexAirplaneAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;
            async Task Refresh(){var reply=await SendOnexHomeRequestAsync(new ValueSet{{"OnexAirplaneGet",true}});ApplyOnexAirplaneState((string)reply["OnexAirplaneState"]);ApplyOnexWifiState((string)reply["OnexWifiState"]);}
            async Task Signal(string phase)=>await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-airplane-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"phase\":\""+phase+"\"}");
            void Tap(TileDefinition tile)=>((IInvokeProvider)new ButtonAutomationPeer(tile.TileButton).GetPattern(PatternInterface.Invoke)).Invoke();
            async Task WaitMode(bool on){for(int n=0;n<60;n++){await Refresh();if(OnexAirplaneOn==on&&!_onexAirplaneBusy)return;await Task.Delay(100);}throw new InvalidOperationException("Actual airplane mode did not reach "+on);}
            async Task WaitWifi(bool on,bool connection){for(int n=0;n<100;n++){await Refresh();if(OnexWifiOn==on&&(!connection||_onexWifiState.GetNamedBoolean("connected",false)))return;await Task.Delay(100);}throw new InvalidOperationException("Wi-Fi radio/connection did not recover");}
            await Refresh();await RefreshOnexBluetoothAsync();bool bluetoothOriginallyOn=OnexBluetoothOn;
            if(!qsTileMap.TryGetValue("OnexAirplane",out var airplane)||!qsTileMap.TryGetValue("OnexWifi",out var wifi)||!airplane.IsActionable||!OnexAirplaneReady)throw new InvalidOperationException("Airplane tile is not actionable");
            if(!QuickSettings.QuickSettingsConfig.Instance.IsDuplicateOnexAction(QuickSettings.TileActionType.ToggleAirplane))throw new InvalidOperationException("Duplicate airplane action allowed");
            var armed=await folder.TryGetItemAsync("onex-airplane-recovery-ready.json") as StorageFile;
            if(armed==null)throw new InvalidOperationException("Independent radio recovery is not armed");var safety=JsonObject.Parse(await FileIO.ReadTextAsync(armed));
            if(!safety.GetNamedBoolean("ready",false)||!safety.GetNamedBoolean("originalAirplaneOff",false)||!DateTimeOffset.TryParse(safety.GetNamedString("createdUtc"),out var created)||DateTimeOffset.UtcNow-created>TimeSpan.FromSeconds(30))throw new InvalidOperationException("Radio recovery expired");
            if(OnexAirplaneOn||!OnexWifiOn||!_onexWifiState.GetNamedBoolean("connected",false))throw new InvalidOperationException("Initial radio state is unsuitable for recovery test");
            bool touched=false;
            try
            {
                await Signal("started");touched=true;Tap(airplane);await WaitMode(true);await WaitWifi(false,false);
                if(airplane.StateText.Text!="On")throw new InvalidOperationException("Airplane caption does not match actual state");
                await SaveOnexImage("onex-airplane-on.png",airplane.TileButton,0);report["tileTapEnabledActualAirplaneMode"]=JsonValue.CreateBooleanValue(true);report["airplaneDisabledActualWifi"]=JsonValue.CreateBooleanValue(true);
                if(!wifi.IsActionable)throw new InvalidOperationException("Wi-Fi cannot be enabled while airplane mode is on");Tap(wifi);await WaitMode(false);await WaitWifi(true,true);
                report["wifiTapClearsActualAirplaneMode"]=JsonValue.CreateBooleanValue(true);report["wifiConnectionRecovered"]=JsonValue.CreateBooleanValue(true);
                Tap(airplane);await WaitMode(true);Tap(airplane);await WaitMode(false);await WaitWifi(true,true);
                report["airplaneTileOnOffRoundTrip"]=JsonValue.CreateBooleanValue(true);report["duplicateActionRejected"]=JsonValue.CreateBooleanValue(true);
                await SaveOnexImage("onex-airplane-off.png",airplane.TileButton,0);
                await RefreshOnexBluetoothAsync();if(OnexBluetoothOn!=bluetoothOriginallyOn)throw new InvalidOperationException("Windows did not restore original Bluetooth radio state");report["originalBluetoothRadioRestored"]=JsonValue.CreateBooleanValue(true);
            }
            finally
            {
                if(touched){try{await SendOnexHomeRequestAsync(new ValueSet{{"OnexAirplaneSet",false}});await SendOnexHomeRequestAsync(new ValueSet{{"OnexWifiSet",true}});await WaitWifi(true,true);}finally{await Signal("done");}}
            }
        }
    }
}
