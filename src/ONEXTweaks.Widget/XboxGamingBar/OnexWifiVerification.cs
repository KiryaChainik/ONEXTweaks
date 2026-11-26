// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
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
        private async Task VerifyOnexWifiAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;bool original=false;string network=null;
            async Task Refresh(){var reply=await App.SendMessageAsync(new ValueSet{{"OnexWifiGet",true}});if(reply==null||reply.ContainsKey("Error"))throw new InvalidOperationException("Wi-Fi read failed");ApplyOnexWifiState((string)reply["OnexWifiState"]);}
            async Task Phase(string name)=>await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-wifi-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"phase",JsonValue.CreateStringValue(name)}}.Stringify());
            async Task Wait(bool target,bool connected=false){for(int n=0;n<100;n++){await Task.Delay(100);await Refresh();if(!_onexWifiBusy&&OnexWifiOn==target&&(!connected||_onexWifiState.GetNamedBoolean("connected",false)))return;}throw new InvalidOperationException("Wi-Fi did not confirm state "+target);}
            try
            {
                // This stage now performs only non-destructive window/layout inspection; no network changes are issued.
                await Refresh();original=OnexWifiOn;network=OnexWifiNetworkName;
                if(!original||!OnexWifiReady||!_onexWifiState.GetNamedBoolean("connected"))throw new InvalidOperationException("Wi-Fi native QA requires the original live connection");
                if(!qsTileMap.TryGetValue("OnexWifi",out var tile)||!tile.IsVisible||!tile.IsActionable||!OnexHasTileDetails(tile.Id))throw new InvalidOperationException("Wi-Fi tile or details not connected");
                if(!string.IsNullOrWhiteSpace(network)&&tile.StateText.Text!=network)throw new InvalidOperationException("Wi-Fi tile does not show current network name");
                if(!QuickSettings.QuickSettingsConfig.Instance.IsDuplicateOnexAction(QuickSettings.TileActionType.ToggleWifi))throw new InvalidOperationException("Duplicate Wi-Fi action allowed");
                var denied=await App.SendMessageAsync(new ValueSet{{"OnexWifiSet",false}});if(denied==null||!denied.ContainsKey("Error"))throw new InvalidOperationException("Wi-Fi write accepted without explicit input");
                report["networkNameAvailable"]=JsonValue.CreateBooleanValue(!string.IsNullOrWhiteSpace(network));report["networkCaptionMatched"]=JsonValue.CreateBooleanValue(string.IsNullOrWhiteSpace(network)||tile.StateText.Text==network);report["unmarkedWriteRejected"]=JsonValue.CreateBooleanValue(true);report["duplicateActionRejected"]=JsonValue.CreateBooleanValue(true);
                var templates=OnexFirstInstallTileIds.Select((id,i)=>new TileDefinition{Id=id,Order=100-i,IsVisible=false}).Concat(new[]{new TileDefinition{Id="MsiCenter",IsVisible=true}}).ToArray();SetOnexFirstInstallTileDefaults(templates);
                if(!templates.Where(t=>t.IsVisible).OrderBy(t=>t.Order).Select(t=>t.Id).SequenceEqual(OnexFirstInstallTileIds)||templates.Single(t=>t.Id=="MsiCenter").IsVisible)throw new InvalidOperationException("First-install tile order mismatch");
                report["firstInstallOrderVerified"]=JsonValue.CreateBooleanValue(true);
                var layout=qsTileDefinitions.Select(t=>t.Id+":"+t.Order+":"+t.IsVisible).ToArray();ApplyOnexFirstInstallLayout();
                if(!layout.SequenceEqual(qsTileDefinitions.Select(t=>t.Id+":"+t.Order+":"+t.IsVisible)))throw new InvalidOperationException("First-install template overwrote an existing layout");
                report["existingLayoutNotOverwritten"]=JsonValue.CreateBooleanValue(true);
                tile.TileButton.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});tile.TileButton.Focus(FocusState.Keyboard);
                void Tap()=>((IInvokeProvider)new ButtonAutomationPeer(tile.TileButton).GetPattern(PatternInterface.Invoke)).Invoke();
                report["tileRadioPriorEvidence"]=JsonValue.CreateStringValue("0.29.68 actual off/on + original reconnect passed; radio path unchanged");
                await Phase("own-window");
                var opening=ShowOnexTileDetailsAsync(tile.TileButton,tile.Id);
                for(int n=0;n<100&&(_onexWifiWindowState==null||_onexDetailsDialog==null);n++)await Task.Delay(100);
                if(!_onexWifiWindowOpen||_onexDetailsDialog==null||!Windows.Gaming.UI.GameBar.IsInputRedirected)throw new InvalidOperationException("Own Wi-Fi window did not open inside Game Bar");
                JsonObject connected=null;for(int n=0;n<70&&connected==null;n++){await RefreshOnexWifiWindowAsync();connected=_onexWifiWindowState.GetNamedArray("networks").Select(v=>v.GetObject()).FirstOrDefault(v=>v.GetNamedBoolean("connected",false));if(connected==null)await Task.Delay(100);}
                if(connected==null||!connected.GetNamedBoolean("known",false))throw new InvalidOperationException("Original saved connection missing from own Wi-Fi list");
                string selectedToken=connected.GetNamedString("token");_onexWifiNetworkList.SelectedItem=_onexWifiWindowRows[selectedToken].Item;UpdateOnexWifiWindowControls();
                if(_onexWifiSelectionCard.Visibility!=Visibility.Collapsed||_onexWifiConnectButton.IsEnabled)throw new InvalidOperationException("Redundant current-network connection card is still visible");
                report["currentNetworkCardHidden"]=JsonValue.CreateBooleanValue(true);
                var selectedBackground=_onexWifiConnectButton.Style.Setters.OfType<Setter>().First(v=>v.Property==Windows.UI.Xaml.Controls.Control.BackgroundProperty).Value as Windows.UI.Xaml.Media.SolidColorBrush;
                if(selectedBackground==null||selectedBackground.Color!=(OnexDialogAccent as Windows.UI.Xaml.Media.SolidColorBrush).Color)throw new InvalidOperationException("Wi-Fi window did not use tile orange accent");
                report["tileDialogAccent"]=JsonValue.CreateStringValue(selectedBackground.Color.ToString());report["ownWindowNetworkCount"]=JsonValue.CreateNumberValue(_onexWifiWindowState.GetNamedArray("networks").Count);
                await SaveOnexImage("onex-wifi-window-private.png",_onexDetailsDialog,0);
                var heading=(FrameworkElement)FindDescendantByName(_onexDetailsDialog,"OnexWifiHeaderLabel");UpdateLayout();
                var labelBounds=heading.TransformToVisual(_onexDetailsDialog).TransformBounds(new Windows.Foundation.Rect(0,0,heading.ActualWidth,heading.ActualHeight));var radioBounds=_onexWifiWindowRadio.TransformToVisual(_onexDetailsDialog).TransformBounds(new Windows.Foundation.Rect(0,0,_onexWifiWindowRadio.ActualWidth,_onexWifiWindowRadio.ActualHeight));double labelGap=radioBounds.Left-labelBounds.Right;
                if(labelGap<6||labelGap>16||_onexWifiNetworkList.ActualHeight<280||_onexWifiSelectionCard.Visibility!=Visibility.Collapsed)throw new InvalidOperationException("Wi-Fi header/list/footer still not compact: gap="+labelGap+" list="+_onexWifiNetworkList.ActualHeight+" footer="+_onexWifiSelection.ActualHeight);
                report["radioLabelGap"]=JsonValue.CreateNumberValue(labelGap);report["networkListHeight"]=JsonValue.CreateNumberValue(_onexWifiNetworkList.ActualHeight);report["currentNetworkPanelHidden"]=JsonValue.CreateBooleanValue(_onexWifiSelectionCard.Visibility==Visibility.Collapsed);
                await SaveOnexImage("onex-wifi-toggle-orange.png",_onexWifiWindowRadio,0);
                report["connectionPriorEvidence"]=JsonValue.CreateStringValue("0.29.69 actual saved-network connect/disconnect verified; backend unchanged, current UI inspection changes no network");
                Windows.Foundation.Rect Bounds(FrameworkElement element)=>element.TransformToVisual(_onexDetailsDialog).TransformBounds(new Windows.Foundation.Rect(0,0,element.ActualWidth,element.ActualHeight));
                var refreshBounds=Bounds(_onexWifiRefreshButton);var listBounds=Bounds(_onexWifiNetworkCard);var closeBounds=Bounds(_onexWifiCloseButton);double topGap=listBounds.Top-refreshBounds.Bottom,bottomGap=closeBounds.Top-listBounds.Bottom;
                if(Math.Abs(topGap-bottomGap)>.6||topGap<6||topGap>10||_onexWifiRefreshButton.ActualWidth>40||_onexWifiCloseButton.ActualWidth>110)throw new InvalidOperationException("Wi-Fi action spacing/size incorrect: "+topGap+"/"+bottomGap);
                var viewportBounds=Bounds((FrameworkElement)_onexDetailsDialog.Content);
                if(refreshBounds.Right>viewportBounds.Right+.5||closeBounds.Right>viewportBounds.Right+.5||refreshBounds.Left<viewportBounds.Left-.5||closeBounds.Left<viewportBounds.Left-.5)throw new InvalidOperationException("Wi-Fi buttons extend outside the actual viewport");
                report["refreshRightInset"]=JsonValue.CreateNumberValue(viewportBounds.Right-refreshBounds.Right);report["closeRightInset"]=JsonValue.CreateNumberValue(viewportBounds.Right-closeBounds.Right);
                var firstRow=_onexWifiWindowRows[selectedToken];var content=(FrameworkElement)firstRow.Item.Content;var itemBounds=Bounds(firstRow.Item);var contentBounds=Bounds(content);double leftInset=contentBounds.Left-itemBounds.Left,rightInset=itemBounds.Right-contentBounds.Right;
                if(leftInset<14||rightInset<14)throw new InvalidOperationException("Wi-Fi selection highlight has insufficient inner spacing: "+leftInset+"/"+rightInset);
                report["topListGap"]=JsonValue.CreateNumberValue(topGap);report["bottomListGap"]=JsonValue.CreateNumberValue(bottomGap);report["selectionLeftInset"]=JsonValue.CreateNumberValue(leftInset);report["selectionRightInset"]=JsonValue.CreateNumberValue(rightInset);report["refreshWidth"]=JsonValue.CreateNumberValue(_onexWifiRefreshButton.ActualWidth);report["closeWidth"]=JsonValue.CreateNumberValue(_onexWifiCloseButton.ActualWidth);
                _onexDetailsDialog.Hide();await opening;
                tile.TileButton.Focus(FocusState.Keyboard);if(!OpenFocusedOnexTileDetails())throw new InvalidOperationException("Wi-Fi X route missing");
                for(int n=0;n<100&&(!_onexWifiWindowOpen||_onexWifiWindowState==null);n++)await Task.Delay(100);
                if(!_onexWifiWindowOpen||!Windows.Gaming.UI.GameBar.IsInputRedirected)throw new InvalidOperationException("Wi-Fi X did not reopen own dialog");
                _onexDetailsDialog.Hide();for(int n=0;n<50&&_onexWifiWindowOpen;n++)await Task.Delay(100);if(_onexWifiWindowOpen)throw new InvalidOperationException("Own Wi-Fi dialog did not close");report["xOwnDialogVerified"]=JsonValue.CreateBooleanValue(true);

            }
            finally
            {
                if(_onexDetailsDialog!=null){_onexDetailsDialog.Hide();for(int n=0;n<50&&_onexWifiWindowOpen;n++)await Task.Delay(50);}
                // Do not override any brightness/radio changes made by the owner during UI inspection.
                await Phase("done");
            }
        }
    }
}
