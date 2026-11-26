// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml;
using Windows.Storage;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private bool _onexDesktopAvailable, _onexCanClose, _onexDesktopBusy;
        private string _onexTargetToken, _onexTargetApp;
        private ContentDialog _onexCloseDialog;
        private string _onexCloseTargetToken;
        private void ShowOnexDesktopStatus(string message)
        {
            OnexDesktopStatus.Text=OnexStrings.Get(message)??"";
            OnexDesktopStatus.Visibility=string.IsNullOrEmpty(message)?Visibility.Collapsed:Visibility.Visible;
        }
        private async Task ConfirmOnexCloseWindowAsync()
        {
            if(!App.IsConnected||!_onexDesktopAvailable||!_onexCanClose||_onexDesktopBusy||string.IsNullOrEmpty(_onexTargetToken))return;
            string token=_onexTargetToken,app=_onexTargetApp??OnexStrings.Get("selected window");
            _onexDesktopBusy=true;_onexCloseTargetToken=token;ApplyOnexTileAvailability();ApplyOnexMediaAvailability();ShowOnexDesktopStatus(null);
            var dialog=new ContentDialog
            {
                Title=global::XboxGamingBar.OnexStrings.Format("Close {0}?",app),
                Content=global::XboxGamingBar.OnexStrings.Get("Request this window to close. The app may ask you to save your work."),
                PrimaryButtonText=global::XboxGamingBar.OnexStrings.Get("Close window"),CloseButtonText=global::XboxGamingBar.OnexStrings.Get("Cancel"),DefaultButton=ContentDialogButton.Close
            };
            _onexCloseDialog=dialog;
            ContentDialogResult result=ContentDialogResult.None;
            try{result=await dialog.ShowAsync();}
            catch(Exception ex){ShowOnexDesktopStatus(ex.Message);Logger.Warn("Close confirmation: "+ex.Message);}
            finally{_onexCloseDialog=null;_onexDesktopBusy=false;ApplyOnexTileAvailability();ApplyOnexMediaAvailability();}
            if(result==ContentDialogResult.Primary)await InvokeOnexDesktopActionAsync("closeWindow",token);
        }
        private void ApplyOnexDesktopState(string payload)
        {
            if (!JsonObject.TryParse(payload, out var state)) return;
            _onexDesktopAvailable = state.GetNamedBoolean("available", false);
            _onexCanClose = state.GetNamedBoolean("canClose", false);
            _onexTargetToken = state.TryGetValue("targetToken", out var token) && token.ValueType == JsonValueType.String ? token.GetString() : null;
            _onexTargetApp = state.TryGetValue("targetApp", out var app) && app.ValueType == JsonValueType.String ? app.GetString() : null;
            ApplyOnexTileAvailability();ApplyOnexMediaAvailability();
        }
        private async Task InvokeOnexDesktopActionAsync(string action,string frozenTargetToken=null)
        {
            if (!App.IsConnected || !_onexDesktopAvailable || _onexDesktopBusy) return;
            _onexDesktopBusy = true;ApplyOnexTileAvailability();ApplyOnexMediaAvailability();ShowOnexDesktopStatus(null);
            try
            {
                var request = new ValueSet { ["OnexDesktopAction"] = action, ["OnexUserInput"] = true, ["InGameBar"] = widget != null };
                if (action == "closeWindow"||frozenTargetToken!=null) request["TargetToken"] = frozenTargetToken ?? _onexTargetToken ?? "";
                var reply = await App.SendMessageAsync(request);
                if (reply == null || reply.TryGetValue("Error", out var error))
                {
                    string message = reply == null ? OnexStrings.Get("Desktop action connection unavailable") : reply["Error"].ToString();
                    ShowOnexDesktopStatus(message);
                    Logger.Warn("Desktop action: " + message);
                    string id = action == "taskManager" ? "ActionTaskManager" : action == "explorer" ? "ActionExplorer" : action=="keyboard" ? "Keyboard" : "ActionEndTask";
                    if(action=="volumeMixer")ToolTipService.SetToolTip(RailMixerButton,message);
                    else if (qsTileMap.TryGetValue(action=="wifiNetworks"?"OnexWifi":id, out var tile)) ToolTipService.SetToolTip(tile.TileButton, message);
                }
            }
            catch (Exception ex) { ShowOnexDesktopStatus(ex.Message);Logger.Warn("Desktop action: " + ex.Message); }
            finally { _onexDesktopBusy = false; ApplyOnexTileAvailability();ApplyOnexMediaAvailability(); }
        }
        private async Task VerifyOnexDesktopTileAsync(string action)
        {
            var folder = ApplicationData.Current.LocalFolder;
            var configuration = JsonObject.Parse(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-desktop-verification-armed.json")));
            if (configuration.GetNamedString("action") != action) return;
            QuickNavItem.IsChecked = true;
            string id = action == "taskManager" ? "ActionTaskManager" : action == "explorer" ? "ActionExplorer" : action=="keyboard" ? "Keyboard" : "ActionEndTask";
            var report = new JsonObject();
            try
            {
                if (action == "closeWindow" && _onexTargetApp != configuration.GetNamedString("expectedTargetApp")) throw new InvalidOperationException("Close test target is not the dedicated verification window");
                if (!qsTileMap.TryGetValue(id, out var tile) || !tile.IsActionable) throw new InvalidOperationException("Desktop tile is not actionable: " + id);
                report["tile"] = JsonValue.CreateStringValue(id);
                report["isGameBar"] = JsonValue.CreateBooleanValue(widget != null);
                report["invoked"] = JsonValue.CreateBooleanValue(true);
                // Go through the imported tile click dispatcher and its availability guard.
                var marker = await folder.CreateFileAsync("onex-desktop-tile-invoked.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(marker, report.Stringify());
                QuickSettingsTile_Click(new Button { Tag = id }, new RoutedEventArgs());
                if(action=="closeWindow")
                {
                    async Task<Button> DialogButton(string name)
                    {
                        for(int i=0;i<30;i++)
                        {
                            await Task.Delay(100);_onexCloseDialog?.ApplyTemplate();
                            if(_onexCloseDialog!=null&&FindDescendantByName(_onexCloseDialog,name) is Button b)return b;
                        }
                        throw new InvalidOperationException("Close confirmation button unavailable: "+name);
                    }
                    var cancel=await DialogButton("CloseButton");
                    string frozen=_onexCloseTargetToken;
                    var cancelPeer=new Windows.UI.Xaml.Automation.Peers.ButtonAutomationPeer(cancel);
                    ((Windows.UI.Xaml.Automation.Provider.IInvokeProvider)cancelPeer.GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                    for(int i=0;i<30&&_onexDesktopBusy;i++)await Task.Delay(100);
                    if(_onexDesktopBusy||!_onexCanClose||_onexTargetToken!=frozen)throw new InvalidOperationException("Cancellation changed the target or left the action busy");
                    report["cancelledFirst"]=JsonValue.CreateBooleanValue(true);
                    QuickSettingsTile_Click(new Button{Tag=id},new RoutedEventArgs());
                    var confirm=await DialogButton("PrimaryButton");
                    if(_onexCloseTargetToken!=frozen)throw new InvalidOperationException("Confirmation target changed");
                    report["confirmationShown"]=JsonValue.CreateBooleanValue(true);
                    await FileIO.WriteTextAsync(marker,report.Stringify());
                    var confirmPeer=new Windows.UI.Xaml.Automation.Peers.ButtonAutomationPeer(confirm);
                    ((Windows.UI.Xaml.Automation.Provider.IInvokeProvider)confirmPeer.GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                }
            }
            catch (Exception ex)
            {
                report["invoked"] = JsonValue.CreateBooleanValue(false);
                report["error"] = JsonValue.CreateStringValue(ex.ToString());
                var marker = await folder.CreateFileAsync("onex-desktop-tile-invoked.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(marker, report.Stringify());
            }
        }
    }
}
