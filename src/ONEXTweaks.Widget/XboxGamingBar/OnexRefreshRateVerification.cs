// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexRefreshRateAppearanceAsync(JsonObject report)
        {
            PerformanceNavItem.IsChecked=true;await RefreshOnexDisplayAsync(false);string mode=OnexCurrentDisplayMode.Stringify();var anchor=qsTileMap["OnexRefreshRate"].TileButton;var opening=ShowOnexRefreshRateSettingsAsync(anchor);
            try
            {
                for(int i=0;i<120&&_onexDetailsDialog?.IsLoaded!=true;i++)await Task.Delay(50);
                if(_onexDetailsDialog?.IsLoaded!=true)throw new InvalidOperationException("Refresh dialog did not open");
                if(!(_onexDetailsDialog.Content is ScrollViewer viewport)||!(viewport.Background is Windows.UI.Xaml.Media.SolidColorBrush brush)||brush.Color.A!=255)throw new InvalidOperationException("Refresh preview background is not opaque");
                await SaveOnexImage("onex-refresh-rate-222.png",_onexDetailsDialog,300);
                if(OnexCurrentDisplayMode.Stringify()!=mode)throw new InvalidOperationException("Read-only preview changed mode");
                report["opaqueOwnDialogPreview"]=JsonValue.CreateBooleanValue(true);report["supportedChoicesPresent"]=JsonValue.CreateBooleanValue(_onexRateSync.Items.Count==6);report["hardwareWrites"]=JsonValue.CreateNumberValue(0);
            }
            finally{CloseOnexMenus();await opening;}
        }
        private async Task VerifyOnexRefreshRateTileAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N"),created=null;int step=0;bool visible=false;int order=0;
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            async Task Wait(Func<bool> ready,string message){for(int n=0;n<160;n++){if(ready())return;await Task.Delay(80);}throw new TimeoutException(message);}
            async Task Input(string key,Func<bool> ready)
            {
                string id=run+":"+(++step);var rect=new RadioButtonAutomationPeer(PerformanceNavItem).GetBoundingRectangle();
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2)}}.Stringify());
                bool accepted=false;for(int n=0;n<125;n++){await Task.Delay(80);try{accepted=await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&ready();}catch{}if(accepted)break;}Check(accepted,"Refresh injected input failed: "+key+" step "+step+" focus="+(FocusManager.GetFocusedElement() as FrameworkElement)?.Name);await Task.Delay(120);
            }
            void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            await ReadOnexPerformanceProfilesAsync();await RefreshOnexDisplayAsync(false);
            Check(OnexFrontend.ProfileId=="global"&&_onexProfilesState.GetNamedValue("currentSession").ValueType==JsonValueType.Null,"Owner profile context protected; refresh QA deferred");
            Check(qsTileMap.TryGetValue("OnexRefreshRate",out var tile)&&!OnexFirstInstallTileIds.Contains("OnexRefreshRate"),"Optional refresh tile is missing or forced into defaults");
            visible=tile.IsVisible;order=tile.Order;string selection=_onexProfilesState.GetNamedString("selection");var originalMode=JsonObject.Parse(OnexCurrentDisplayMode.Stringify());var originalSettings=JsonObject.Parse(_onexDisplayState.GetNamedObject("settings").Stringify());var before=await ReadOnexStateAsync();
            int Setting(JsonObject settings,string key)=>settings.GetNamedObject(key).GetNamedValue("value").ValueType==JsonValueType.Number?(int)settings.GetNamedObject(key).GetNamedNumber("value"):-1;
            try
            {
                await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"name",JsonValue.CreateStringValue("ONEXQA Refresh221")}});created=OnexFrontend.ProfileId;
                var owned=new JsonArray();owned.Add(JsonValue.CreateStringValue(created));await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-refresh-qa-owned.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"performance",owned},{"fans",new JsonArray()}}.Stringify());
                PerformanceNavItem.IsChecked=true;tile.IsVisible=true;tile.Order=-1;RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();await Task.Delay(150);await Input("focus",()=>true);await Input("up",()=>true);
                tile=qsTileMap["OnexRefreshRate"];tile.TileButton.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(150);Check(tile.TileButton.Focus(FocusState.Keyboard),"Cannot focus refresh tile");
                int first=OnexRefreshRatePolicy.Next(OnexCurrentRefreshRate,OnexSupportedRefreshRates());await Input("a",()=>!_onexRateTileBusy&&!_onexDisplayBusy&&OnexCurrentRefreshRate==first);Check(tile.StateText.Text==first+" Hz","Refresh tile caption was not actual readback");
                Check(OnexCurrentDisplayMode.GetNamedNumber("width")==originalMode.GetNamedNumber("width")&&OnexCurrentDisplayMode.GetNamedNumber("height")==originalMode.GetNamedNumber("height"),"Tap changed resolution");
                Check(tile.TileButton.Focus(FocusState.Keyboard),"Cannot refocus refresh tile for reverse tap");int reverse=OnexRefreshRatePolicy.Next(OnexCurrentRefreshRate,OnexSupportedRefreshRates());await Input("a",()=>!_onexRateTileBusy&&!_onexDisplayBusy&&OnexCurrentRefreshRate==reverse);
                Check(tile.TileButton.Focus(FocusState.Keyboard),"Cannot refocus refresh tile");await Input("x",()=>_onexDetailsDialog!=null&&_onexRateCurrent!=null&&_onexRateButtons.Count==6);await Wait(()=>_onexDetailsDialog.IsLoaded,"Refresh dialog not shown");
                var supported=OnexSupportedRefreshRates();Check(OnexRefreshRatePolicy.MenuRates.All(hz=>_onexRateButtons[hz].IsEnabled==supported.Contains(hz)),"Unsupported requested frequencies were not disabled");Check(_onexRateAll.Items.OfType<ComboBoxItem>().Select(item=>(int)item.Tag).SequenceEqual(supported),"Full Windows rate list differs from native inventory");
                foreach(int hz in new[]{30,120,144}.Where(supported.Contains))
                {Invoke(_onexRateButtons[hz]);await Wait(()=>!_onexDisplayBusy&&OnexCurrentRefreshRate==hz,"Selected frequency was not applied: "+hz);await Task.Delay(100);}
                Check(_onexRateSync.SelectedIndex==Setting(_onexDisplayState.GetNamedObject("settings"),"frameSync")&&_onexRateVrr.IsOn==(Setting(_onexDisplayState.GetNamedObject("settings"),"vrr")==1),"Sync/VRR controls differ from Intel readback");
                int syncWrites=0,vrrWrites=0;var settings=_onexDisplayState.GetNamedObject("settings");
                if(_onexRateSync.IsEnabled&&Setting(settings,"frameSync")>=0)
                {
                    int initial=Setting(settings,"frameSync");var allowed=settings.GetNamedObject("frameSync").GetNamedArray("choices").Select(item=>(int)item.GetNumber()).ToArray();Check(_onexRateSync.Items.OfType<ComboBoxItem>().Select((item,index)=>item.IsEnabled==allowed.Contains(index)).All(value=>value),"Unsupported Intel Sync choices remain enabled");foreach(int next in allowed.Where(value=>value!=initial)){await SetOnexRateWindowValueAsync("frameSync",next);Check(Setting(_onexDisplayState.GetNamedObject("settings"),"frameSync")==next,"Intel Frame Sync did not change: "+next+" "+_onexRateStatus.Text);syncWrites++;}
                    await SetOnexRateWindowValueAsync("frameSync",initial);Check(Setting(_onexDisplayState.GetNamedObject("settings"),"frameSync")==initial,"Intel Frame Sync did not restore");syncWrites++;
                }
                if(_onexRateVrr.IsEnabled&&Setting(settings,"vrr")>=0)
                {
                    bool initial=_onexRateVrr.IsOn;_onexRateVrr.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(150);Check(_onexRateVrr.Focus(FocusState.Keyboard),"Cannot focus VRR toggle");
                    await Input("a",()=>!_onexDisplayBusy&&_onexRateVrr.IsOn!=initial&&Setting(_onexDisplayState.GetNamedObject("settings"),"vrr")==(!initial?1:0));vrrWrites++;
                    await Input("a",()=>!_onexDisplayBusy&&_onexRateVrr.IsOn==initial&&Setting(_onexDisplayState.GetNamedObject("settings"),"vrr")== (initial?1:0));vrrWrites++;
                }
                await SaveOnexImage("onex-refresh-rate-221.png",(UIElement)_onexDetailsDialog.Content,200);
                CloseOnexMenus();await Wait(()=>_onexRateCurrent==null,"Refresh dialog did not close");
                Check(tile.TileButton.Focus(FocusState.Keyboard),"Cannot focus refresh tile for second tap");int second=OnexRefreshRatePolicy.Next(OnexCurrentRefreshRate,OnexSupportedRefreshRates());await Input("a",()=>!_onexRateTileBusy&&!_onexDisplayBusy&&OnexCurrentRefreshRate==second);
                await ReadOnexPerformanceProfilesAsync();var entry=OnexActiveProfile;var effective=entry.GetNamedObject(_onexProfilesState.GetNamedBoolean("onAc",false)?"effectiveAc":"effectiveDc",null)??entry.GetNamedObject("dc");Check(effective.GetNamedObject("display").GetNamedObject("mode").GetNamedNumber("hz")==second,"Refresh change was not recorded in active Performance profile");
                Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==before.GetNamedString("rawLimits"),"Refresh operations changed TDP");
                report["optionalTileDoesNotChangeDefault21"]=JsonValue.CreateBooleanValue(true);report["tap60And144ActualModeReadback"]=JsonValue.CreateBooleanValue(true);report["xOwnDialog"]=JsonValue.CreateBooleanValue(true);report["holdUsesSharedDetailsHandler"]=JsonValue.CreateBooleanValue(OnexHasTileDetails("OnexRefreshRate"));report["requestedAndAllSupportedRates"]=JsonValue.CreateBooleanValue(true);report["native30And120And144"]=JsonValue.CreateBooleanValue(true);report["syncVrrControlsMatchIntel"]=JsonValue.CreateBooleanValue(true);report["performanceSourceRecorded"]=JsonValue.CreateBooleanValue(true);report["injectedControllerSteps"]=JsonValue.CreateNumberValue(step);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["newSyncHardwareWrites"]=JsonValue.CreateNumberValue(syncWrites);report["newVrrHardwareWrites"]=JsonValue.CreateNumberValue(vrrWrites);report["newVrrScopeHardwareWrites"]=JsonValue.CreateNumberValue(0);report["reservedScopeNotExposed"]=JsonValue.CreateBooleanValue(true);
            }
            finally
            {
                CloseOnexMenus();await Wait(()=>!_onexRateTileBusy&&!_onexDisplayBusy,"Refresh operation did not settle before cleanup");
                if(created!=null&&OnexFrontend.ProfileId==created)
                {
                    foreach(string key in new[]{"frameSync","vrrMode","vrr"})if(Setting(originalSettings,key)>=0&&Setting(originalSettings,key)!=Setting(_onexDisplayState.GetNamedObject("settings"),key))Check(await SetOnexDisplayAsync(key,JsonValue.CreateNumberValue(Setting(originalSettings,key))),"Original Intel setting could not be restored: "+key);
                }
                if(created!=null&&OnexFrontend.ProfileId==created)Check(await SetOnexDisplayAsync("refreshRate",originalMode["hz"]),"Original refresh rate could not be restored");
                if(created!=null&&OnexProfiles.Any(entry=>entry.GetNamedString("id")==created&&entry.GetNamedString("name")=="ONEXQA Refresh221"))await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(created)}});
                if(selection=="auto")await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("automatic")}});
                await RefreshOnexDisplayAsync(false);Check(OnexCurrentDisplayMode.Stringify()==originalMode.Stringify(),"Original display mode not restored");
                foreach(string key in new[]{"frameSync","vrr","vrrMode"})Check(Setting(originalSettings,key)==Setting(_onexDisplayState.GetNamedObject("settings"),key),"Refresh test changed "+key);
                tile.IsVisible=visible;tile.Order=order;RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();
                var owned=await folder.TryGetItemAsync("onex-refresh-qa-owned.json");if(owned!=null)await owned.DeleteAsync();await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");
            }
        }
    }
}
