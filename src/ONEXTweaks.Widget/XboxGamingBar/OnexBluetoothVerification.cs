// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Devices.Enumeration;
using Windows.Storage;
using Windows.UI.Xaml;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexBluetoothAsync(JsonObject report)
        {
            for(int attempt=0;attempt<60&&_onexBluetoothReadBusy;attempt++)await Task.Delay(100);await RefreshOnexBluetoothAsync(true);bool original=OnexBluetoothOn;
            try
            {
                if(_onexBluetoothRadios.Count==0)throw new InvalidOperationException("Bluetooth radio not exposed to widget: "+_onexBluetoothError);
                if(!qsTileMap.TryGetValue("OnexBluetooth",out var tile)||!tile.IsActionable||!OnexHasTileDetails(tile.Id))throw new InvalidOperationException("Bluetooth tile/details not connected");
                if(!QuickSettings.QuickSettingsConfig.Instance.IsDuplicateOnexAction(QuickSettings.TileActionType.ToggleBluetooth))throw new InvalidOperationException("Duplicate Bluetooth action permitted");
                report["radioCount"]=JsonValue.CreateNumberValue(_onexBluetoothRadios.Count);report["radioInitiallyOn"]=JsonValue.CreateBooleanValue(original);report["pairedEndpointCount"]=JsonValue.CreateNumberValue(_onexBluetoothDevices.Values.Count(d=>d.Pairing.IsPaired));report["duplicateActionRejected"]=JsonValue.CreateBooleanValue(true);
                bool radioChanged=false;
                if(await ApplicationData.Current.LocalFolder.TryGetItemAsync("onex-bluetooth-radio-test.json")!=null)
                {
                    var accessDeadline=DateTimeOffset.UtcNow.AddSeconds(20);_onexBluetoothAccess=await Windows.Devices.Radios.Radio.RequestAccessAsync();report["radioPermissionStatus"]=JsonValue.CreateStringValue(_onexBluetoothAccess.ToString());
                    bool safe=_onexBluetoothAccess==Windows.Devices.Radios.RadioAccessStatus.Allowed&&DateTimeOffset.UtcNow<accessDeadline&&Windows.Gaming.UI.GameBar.IsInputRedirected&&await ApplicationData.Current.LocalFolder.TryGetItemAsync("onex-bluetooth-radio-test.json")!=null&&!_onexBluetoothDevices.Values.Any(OnexBluetoothConnected);
                    if(safe)
                    {
                        try{await SetOnexBluetoothRadioAsync(!original);if(OnexBluetoothOn==original)throw new InvalidOperationException("Bluetooth radio did not change: "+_onexBluetoothError);radioChanged=true;report["actualRadioToggleVerified"]=JsonValue.CreateBooleanValue(true);}
                        finally{await SetOnexBluetoothRadioAsync(original);if(OnexBluetoothOn!=original)throw new InvalidOperationException("Original Bluetooth radio state not restored");report["radioStateRestored"]=JsonValue.CreateBooleanValue(true);}
                    }
                    else{report["radioChangeSkipped"]=JsonValue.CreateBooleanValue(true);report["skipReason"]=JsonValue.CreateStringValue("Access was not allowed promptly, diagnostic expired, foreground changed, or a connected device exists");}
                }
                var opening=ShowOnexTileDetailsAsync(tile.TileButton,tile.Id);for(int attempt=0;attempt<100&&(!_onexBluetoothWindowOpen||_onexDetailsDialog==null);attempt++)await Task.Delay(100);await Task.Delay(4000);
                if(!_onexBluetoothWindowOpen||_onexDetailsDialog==null||!Windows.Gaming.UI.GameBar.IsInputRedirected)throw new InvalidOperationException("Bluetooth dialog did not stay inside Game Bar");
                UpdateOnexBluetoothWindow();
                var watcherStates=new JsonArray();foreach(var watcher in _onexBluetoothWatchers){watcherStates.Add(JsonValue.CreateStringValue(watcher.Status.ToString()));if(watcher.Status==DeviceWatcherStatus.Aborted)throw new InvalidOperationException("Bluetooth discovery aborted: "+_onexBluetoothWindowError);}report["discoveryStates"]=watcherStates;
                if(_onexBluetoothWindowError!=null)throw new InvalidOperationException(_onexBluetoothWindowError);
                if(_onexBluetoothList.SelectedItem!=null||_onexBluetoothPairButton.Visibility!=Visibility.Visible||_onexBluetoothPairButton.IsEnabled)throw new InvalidOperationException("Bluetooth pairing action disappeared before device selection");
                _onexDetailsDialog.UpdateLayout();
                var viewport=_onexDetailsDialog.Content as Windows.UI.Xaml.Controls.ScrollViewer;
                var buttonBounds=_onexBluetoothPairButton.TransformToVisual(viewport).TransformBounds(new Windows.Foundation.Rect(0,0,_onexBluetoothPairButton.ActualWidth,_onexBluetoothPairButton.ActualHeight));
                if(buttonBounds.Width<=0||buttonBounds.Height<=0||buttonBounds.X<0||buttonBounds.Y<0||buttonBounds.Right>viewport.ActualWidth+1||buttonBounds.Bottom>viewport.ActualHeight+1)throw new InvalidOperationException("Bluetooth pairing action outside visible dialog viewport");
                var closeBounds=_onexBluetoothClose.TransformToVisual(viewport).TransformBounds(new Windows.Foundation.Rect(0,0,_onexBluetoothClose.ActualWidth,_onexBluetoothClose.ActualHeight));
                if(closeBounds.Width<=0||closeBounds.Height<=0||closeBounds.X<0||closeBounds.Y<0||closeBounds.Right>viewport.ActualWidth+1||closeBounds.Bottom>viewport.ActualHeight+1)throw new InvalidOperationException("Bluetooth close button outside visible dialog viewport");
                report["pairActionVisibleWithoutSelection"]=JsonValue.CreateBooleanValue(true);report["pairActionInsideViewport"]=JsonValue.CreateBooleanValue(true);
                if((_onexBluetoothPairButton.Background as Windows.UI.Xaml.Media.SolidColorBrush)?.Color!=OnexSettingsBrush(48,55,64).Color)throw new InvalidOperationException("Unselected Bluetooth footer action is not gray");report["unselectedActionGrayAndDisabled"]=JsonValue.CreateBooleanValue(true);
                var settingsBounds=_onexBluetoothSettings.TransformToVisual(viewport).TransformBounds(new Windows.Foundation.Rect(0,0,_onexBluetoothSettings.ActualWidth,_onexBluetoothSettings.ActualHeight));var refreshBounds=_onexBluetoothRefresh.TransformToVisual(viewport).TransformBounds(new Windows.Foundation.Rect(0,0,_onexBluetoothRefresh.ActualWidth,_onexBluetoothRefresh.ActualHeight));
                if(settingsBounds.Right>refreshBounds.X||Math.Abs(settingsBounds.Y-refreshBounds.Y)>1||settingsBounds.Width!=40||refreshBounds.Width!=40||refreshBounds.Right>viewport.ActualWidth+1)throw new InvalidOperationException("Bluetooth settings gear is not beside refresh inside header");report["settingsGearBesideRefresh"]=JsonValue.CreateBooleanValue(true);
                if(Math.Abs(buttonBounds.Y-closeBounds.Y)>1||buttonBounds.Right>=closeBounds.X)throw new InvalidOperationException("Bluetooth pair/close actions are not on opposite sides of the footer");report["footerPairLeftCloseRight"]=JsonValue.CreateBooleanValue(true);report["closeActionInsideViewport"]=JsonValue.CreateBooleanValue(true);await SaveOnexImage("onex-bluetooth-window-private.png",_onexDetailsDialog,0);
                var selectable=_onexBluetoothRows.Values.FirstOrDefault(row=>!row.Device.Pairing.IsPaired&&row.Device.Pairing.CanPair);
                if(selectable!=null)
                {
                    // Selection only: never invoke PairAsync on a nearby device during QA.
                    _onexBluetoothList.SelectedItem=selectable.Item;await Task.Delay(150);
                    if(SelectedBluetoothDevice()==null||!_onexBluetoothPairButton.IsEnabled||_onexBluetoothPairButton.Visibility!=Visibility.Visible)throw new InvalidOperationException("Selecting Bluetooth device did not enable visible pair action");
                    string identity=_onexBluetoothSelectedIdentity;await RefreshOnexBluetoothAsync(true);UpdateOnexBluetoothWindow();
                    if(_onexBluetoothSelectedIdentity!=identity||SelectedBluetoothDevice()==null||!_onexBluetoothPairButton.IsEnabled)throw new InvalidOperationException("Bluetooth refresh lost selected pair action");
                    report["deviceSelectionEnablesPairAction"]=JsonValue.CreateBooleanValue(true);report["refreshRetainsSelectedPairAction"]=JsonValue.CreateBooleanValue(true);
                    await SaveOnexImage("onex-bluetooth-selected-private.png",_onexDetailsDialog,0);_onexBluetoothList.SelectedItem=null;UpdateOnexBluetoothWindow();
                }
                else report["deviceSelectionCheckSkipped"]=JsonValue.CreateStringValue("No nearby pairable endpoint; no physical pairing attempted");
                await SaveOnexImage("onex-bluetooth-toggle.png",_onexBluetoothWindowRadio,0);
                var accent=(_onexBluetoothWindowRadio.Resources["ToggleSwitchFillOn"] as Windows.UI.Xaml.Media.SolidColorBrush)?.Color.ToString();if(accent!=(OnexDialogAccent as Windows.UI.Xaml.Media.SolidColorBrush).Color.ToString())throw new InvalidOperationException("Bluetooth switch accent differs from tile dialogs");
                report["tileDialogAccent"]=JsonValue.CreateStringValue(accent);report["discoveredVisibleRows"]=JsonValue.CreateNumberValue(_onexBluetoothRows.Count);report["ownDialogInsideGameBar"]=JsonValue.CreateBooleanValue(true);report["radioChangesExecuted"]=JsonValue.CreateBooleanValue(radioChanged);report["physicalPairingChanges"]=JsonValue.CreateNumberValue(0);
                // Synthetic ceremony UI only; no nearby device is paired or unpaired by this check.
                _onexBluetoothWindowBusy=true;_onexBluetoothConfirmRequiresPin=false;var confirmation=ConfirmOnexBluetoothAsync("Проверка интерфейса подтверждения кода","123456",false);await Task.Delay(100);
                if(_onexBluetoothConfirmCard.Visibility!=Visibility.Visible||_onexBluetoothDeviceCard.Visibility!=Visibility.Collapsed)throw new InvalidOperationException("Own Bluetooth code confirmation not shown");
                await SaveOnexImage("onex-bluetooth-confirmation-synthetic.png",_onexDetailsDialog,0);
                ((Windows.UI.Xaml.Automation.Provider.IInvokeProvider)new Windows.UI.Xaml.Automation.Peers.ButtonAutomationPeer(_onexBluetoothConfirmAccept).GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();if(await confirmation!="accept")throw new InvalidOperationException("Bluetooth code confirmation did not resolve");
                var input=ConfirmOnexBluetoothAsync("Проверка ввода PIN",null,true);_onexBluetoothPinInput.Text="123456";((Windows.UI.Xaml.Automation.Provider.IInvokeProvider)new Windows.UI.Xaml.Automation.Peers.ButtonAutomationPeer(_onexBluetoothConfirmAccept).GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();if(await input!="123456")throw new InvalidOperationException("Bluetooth PIN input did not resolve");_onexBluetoothWindowBusy=false;UpdateOnexBluetoothWindow();report["syntheticCodeAndPinUiVerified"]=JsonValue.CreateBooleanValue(true);
                var pairingDialog=_onexDetailsDialog;int pairingGeneration=_onexBluetoothWindowGeneration;
                _onexBluetoothWindowBusy=true;_onexBluetoothPairingActive=true;
                try
                {
                    var pending=ConfirmOnexBluetoothAsync("Проверка сохранения сопряжения при системном диалоге","123456",false);
                    CloseOnexMenus(systemTransition:true);CloseOnexMenus(systemTransition:true);await Task.Delay(100);
                    if(opening.IsCompleted||pending.IsCompleted||_onexDetailsDialog!=pairingDialog||pairingGeneration!=_onexBluetoothWindowGeneration)throw new InvalidOperationException("System foreground transition cancelled pending Bluetooth ceremony");
                    report["systemTransitionPreservesPendingCeremony"]=JsonValue.CreateBooleanValue(true);
                    await SetOnexBluetoothOverlayAsync(false);if(pending.IsCompleted||opening.IsCompleted)throw new InvalidOperationException("Explicit input release lost pending Bluetooth ceremony");await SetOnexBluetoothOverlayAsync(true);if(pending.IsCompleted||opening.IsCompleted)throw new InvalidOperationException("Explicit input return lost pending Bluetooth ceremony");report["bridgeInputHandoffVerified"]=JsonValue.CreateBooleanValue(true);
                    if(await ApplicationData.Current.LocalFolder.TryGetItemAsync("onex-bluetooth-lifecycle-test.json")!=null)
                    {
                        async Task Signal(string phase)=>await FileIO.WriteTextAsync(await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-bluetooth-lifecycle-qa.json",CreationCollisionOption.ReplaceExisting),"{\"phase\":\""+phase+"\"}");
                        await Signal("awaiting-hide");
                        for(int attempt=0;attempt<150&&Windows.Gaming.UI.GameBar.IsInputRedirected;attempt++)await Task.Delay(100);
                        if(Windows.Gaming.UI.GameBar.IsInputRedirected)throw new InvalidOperationException("Game Bar did not leave foreground during Bluetooth lifecycle check");
                        await Signal("awaiting-open");
                        for(int attempt=0;attempt<150&&!Windows.Gaming.UI.GameBar.IsInputRedirected;attempt++)await Task.Delay(100);
                        if(!Windows.Gaming.UI.GameBar.IsInputRedirected)throw new InvalidOperationException("Game Bar did not return during Bluetooth lifecycle check");
                        await Task.Delay(300);
                        if(opening.IsCompleted||pending.IsCompleted||_onexDetailsDialog!=pairingDialog||pairingGeneration!=_onexBluetoothWindowGeneration||_onexBluetoothConfirmCard.Visibility!=Visibility.Visible)throw new InvalidOperationException("Real Game Bar background transition lost Bluetooth ceremony");
                        report["actualGameBarHideReopenPreservesSyntheticCeremony"]=JsonValue.CreateBooleanValue(true);await Signal("restored");
                    }
                    _onexBluetoothAnswer.TrySetResult("accept");if(await pending!="accept")throw new InvalidOperationException("Retained Bluetooth ceremony could not be confirmed");
                    var cancelled=ConfirmOnexBluetoothAsync("Проверка явной отмены",null,false);CloseOnexMenus();await opening;
                    if(await cancelled!=null||_onexBluetoothWindowOpen||_onexBluetoothPairingActive||_onexBluetoothAnswer!=null)throw new InvalidOperationException("Explicit close did not clear Bluetooth ceremony");
                    report["explicitCloseCancelsSyntheticCeremony"]=JsonValue.CreateBooleanValue(true);
                }
                finally{_onexBluetoothPairingActive=false;_onexBluetoothWindowBusy=false;}
                for(int cycle=0;cycle<3;cycle++)
                {
                    for(int attempt=0;attempt<50&&_onexDetailsDialog!=null;attempt++)await Task.Delay(50);await Task.Delay(100);tile.TileButton.Focus(FocusState.Keyboard);if(!OpenFocusedOnexTileDetails())throw new InvalidOperationException("Bluetooth X route missing on reopen cycle "+cycle);for(int attempt=0;attempt<50&&!_onexBluetoothWindowOpen;attempt++)await Task.Delay(100);await Task.Delay(150);
                    if(!_onexBluetoothWindowOpen||_onexBluetoothAnswer!=null||_onexBluetoothPairButton.Visibility!=Visibility.Visible||_onexBluetoothPairButton.IsEnabled)throw new InvalidOperationException("Bluetooth action lost after cancelled ceremony/reopen, cycle "+cycle);
                    if(cycle==0)await SaveOnexImage("onex-bluetooth-reopened-private.png",_onexDetailsDialog,0);
                    if(cycle<2)
                    {
                        var abandoned=ConfirmOnexBluetoothAsync("Проверка закрытия ожидающего подтверждения",null,false);
                        ((Windows.UI.Xaml.Automation.Provider.IInvokeProvider)new Windows.UI.Xaml.Automation.Peers.ButtonAutomationPeer(_onexBluetoothClose).GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                        if(await abandoned!=null)throw new InvalidOperationException("Closed Bluetooth confirmation was accepted");
                    }
                    else CloseOnexMenus(systemTransition:true);
                    for(int attempt=0;attempt<50&&_onexBluetoothWindowOpen;attempt++)await Task.Delay(100);
                    if(_onexBluetoothWindowOpen||_onexBluetoothAnswer!=null)throw new InvalidOperationException("Bluetooth dialog close retained pending answer");
                }
                report["reopenedActionVisibleAfterCancel"]=JsonValue.CreateBooleanValue(true);report["repeatedConfirmationCloseReopenCycles"]=JsonValue.CreateNumberValue(3);report["idleDialogClosesNormally"]=JsonValue.CreateBooleanValue(true);report["xOwnDialogVerified"]=JsonValue.CreateBooleanValue(true);
                var hostWindow=ShowOnexTileDetailsAsync(tile.TileButton,tile.Id);for(int attempt=0;attempt<50&&!_onexBluetoothWindowOpen;attempt++)await Task.Delay(100);
                _onexBluetoothPairingActive=true;_onexBluetoothWindowBusy=true;UpdateOnexBluetoothWindow();
                try
                {
                    var host=await PairThroughOnexBluetoothHostAsync(null,true,_onexBluetoothWindowGeneration);
                    if(host.GetNamedString("status","")!="ProbeCompleted"||host.GetNamedBoolean("paired",true))throw new InvalidOperationException("Independent pairing host probe failed");
                    for(int attempt=0;attempt<75&&!Windows.Gaming.UI.GameBar.IsInputRedirected;attempt++)await Task.Delay(40);
                    if(!Windows.Gaming.UI.GameBar.IsInputRedirected||!_onexBluetoothWindowOpen||_onexBluetoothPairButton.Visibility!=Visibility.Visible)throw new InvalidOperationException("Independent host did not return to our Bluetooth dialog");
                    report["pairingHostIndependentProcess"]=JsonValue.CreateBooleanValue(true);report["pairingHostApplicationId"]=JsonValue.CreateStringValue(host.GetNamedString("applicationId"));report["pairingHostReturnedToWidget"]=JsonValue.CreateBooleanValue(true);report["pairingHostProbeDidNotPair"]=JsonValue.CreateBooleanValue(true);
                }
                finally{_onexBluetoothPairingActive=false;_onexBluetoothWindowBusy=false;_onexDetailsDialog?.Hide();await hostWindow;}
                await RefreshOnexBluetoothAsync();if(OnexBluetoothOn!=original)throw new InvalidOperationException("Read-only UI inspection changed Bluetooth state");
            }
            finally{_onexBluetoothPairingActive=false;_onexDetailsDialog?.Hide();}
        }
    }
}
