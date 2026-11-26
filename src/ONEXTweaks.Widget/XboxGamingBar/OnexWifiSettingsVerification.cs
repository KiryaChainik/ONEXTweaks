// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexWifiSettingsAsync(JsonObject report)
        {
            var initial=await App.SendMessageAsync(new ValueSet{{"OnexWifiGet",true}});ApplyOnexWifiState((string)initial["OnexWifiState"]);bool radio=OnexWifiOn;string originalNetwork=OnexWifiNetworkName;
            var rejected=await App.SendMessageAsync(new ValueSet{{"OnexDesktopAction","networkSettings"}});if(rejected==null||!rejected.ContainsKey("Error"))throw new InvalidOperationException("Unmarked network settings launch accepted");
            PerformanceNavItem.IsChecked=true;var tile=qsTileMap["OnexWifi"];var showing=ShowOnexWifiWindowAsync(tile.TileButton);
            try
            {
                for(int n=0;n<80&&(_onexWifiWindowState==null||_onexDetailsDialog==null);n++)await Task.Delay(100);UpdateLayout();
                if(!_onexWifiWindowOpen||!_onexWifiSettingsButton.IsEnabled||_onexDetailsDialog==null)throw new InvalidOperationException("Wi-Fi settings gear missing or disabled");
                var gear=_onexWifiSettingsButton.TransformToVisual(_onexDetailsDialog).TransformBounds(new Windows.Foundation.Rect(0,0,_onexWifiSettingsButton.ActualWidth,_onexWifiSettingsButton.ActualHeight));var refresh=_onexWifiRefreshButton.TransformToVisual(_onexDetailsDialog).TransformBounds(new Windows.Foundation.Rect(0,0,_onexWifiRefreshButton.ActualWidth,_onexWifiRefreshButton.ActualHeight));
                if(gear.Width!=40||gear.Height!=40||Math.Abs(gear.Top-refresh.Top)>.5||Math.Abs(refresh.Left-gear.Right-8)>.5||refresh.Right>_onexDetailsDialog.ActualWidth+.5||gear.Left<0)throw new InvalidOperationException("Header action geometry/clipping regression");
                if(_onexWifiRefreshButton.XYFocusLeft!=_onexWifiSettingsButton||_onexWifiSettingsButton.XYFocusLeft!=_onexWifiWindowRadio||!_onexWifiSettingsButton.Focus(FocusState.Keyboard))throw new InvalidOperationException("Settings gear is not reachable by controller focus");
                await SaveOnexImage("onex-wifi-header-actions.png",(UIElement)_onexWifiSettingsButton.Parent,0);
                ((IInvokeProvider)new ButtonAutomationPeer(_onexWifiSettingsButton).GetPattern(PatternInterface.Invoke)).Invoke();
                for(int n=0;n<80&&_onexWifiWindowOpen;n++)await Task.Delay(100);if(_onexWifiWindowOpen)throw new InvalidOperationException("Wi-Fi popup did not close before Windows Settings");
                var final=await App.SendMessageAsync(new ValueSet{{"OnexWifiGet",true}});ApplyOnexWifiState((string)final["OnexWifiState"]);if(OnexWifiOn!=radio||OnexWifiNetworkName!=originalNetwork)throw new InvalidOperationException("Settings shortcut changed the Wi-Fi connection");
                report["gearInvokedViaNativeButton"]=JsonValue.CreateBooleanValue(true);report["headerButtonsUnclipped"]=JsonValue.CreateBooleanValue(true);report["sameSizeAndEightPointGap"]=JsonValue.CreateBooleanValue(true);report["controllerFocusReachable"]=JsonValue.CreateBooleanValue(true);report["radioAndConnectionPreserved"]=JsonValue.CreateBooleanValue(true);report["unmarkedActionRejected"]=JsonValue.CreateBooleanValue(true);report["expectedUri"]=JsonValue.CreateStringValue("ms-settings:network-status");
            }
            finally{if(_onexWifiWindowOpen)_onexDetailsDialog?.Hide();await showing;}
        }
    }
}
