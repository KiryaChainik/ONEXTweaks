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
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexResolutionAppearanceAsync(JsonObject report)
        {
            PerformanceNavItem.IsChecked=true;await RefreshOnexDisplayAsync(false);string mode=OnexCurrentDisplayMode.Stringify();
            if(qsTileMap["OnexResolution"].Glyph!="\uE7F4")throw new InvalidOperationException("Resolution tile does not use the monitor icon");
            var opening=ShowOnexResolutionSettingsAsync(qsTileMap["OnexResolution"].TileButton);
            try
            {
                for(int i=0;i<120&&_onexDetailsDialog?.IsLoaded!=true;i++)await Task.Delay(50);
                if(_onexDetailsDialog?.IsLoaded!=true)throw new InvalidOperationException("Resolution dialog not shown");
                _onexResolutionChoice.Focus(FocusState.Keyboard);await SaveOnexImage("onex-resolution-224.png",_onexDetailsDialog,300);
                if(OnexCurrentDisplayMode.Stringify()!=mode)throw new InvalidOperationException("Preview changed mode");
                report["ownDialogAndMonitorIcon"]=JsonValue.CreateBooleanValue(true);report["hardwareWrites"]=JsonValue.CreateNumberValue(0);
            }
            finally{CloseOnexMenus();await opening;}
        }
        private async Task VerifyOnexResolutionTileAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N"),created=null;int step=0;bool visible=false;int order=0;
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            async Task Wait(Func<bool> ready,string message){for(int n=0;n<160;n++){if(ready())return;await Task.Delay(80);}throw new TimeoutException(message);}
            async Task Input(string key,Func<bool> ready)
            {
                string id=run+":"+(++step);var rect=new RadioButtonAutomationPeer(PerformanceNavItem).GetBoundingRectangle();
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2)}}.Stringify());
                bool accepted=false;for(int n=0;n<125;n++){await Task.Delay(80);try{accepted=await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&ready();}catch{}if(accepted)break;}Check(accepted,"Resolution injected input failed: "+key+" step "+step);await Task.Delay(120);
            }
            await ReadOnexPerformanceProfilesAsync();await RefreshOnexDisplayAsync(false);
            Check(OnexFrontend.ProfileId=="global"&&_onexProfilesState.GetNamedValue("currentSession").ValueType==JsonValueType.Null,"Owner profile context protected; resolution QA deferred");
            Check(qsTileMap.TryGetValue("OnexResolution",out var tile)&&!OnexFirstInstallTileIds.Contains("OnexResolution"),"Optional resolution tile missing/forced into defaults");
            var originalMode=JsonObject.Parse(OnexCurrentDisplayMode.Stringify());var originalSystem=JsonObject.Parse(OnexDisplaySystem.Stringify());var before=await ReadOnexStateAsync();string selection=_onexProfilesState.GetNamedString("selection");visible=tile.IsVisible;order=tile.Order;
            Check(OnexSystemNumber(originalSystem,"orientation")==0,"Native resolution fixture requires the existing landscape orientation");
            try
            {
                await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"name",JsonValue.CreateStringValue("ONEXQA Resolution223")}});created=OnexFrontend.ProfileId;
                var owned=new JsonArray();owned.Add(JsonValue.CreateStringValue(created));await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-resolution-qa-owned.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"performance",owned},{"fans",new JsonArray()}}.Stringify());
                if(OnexResolutionTag(OnexCurrentDisplayMode)!="1920x1200")Check(await SetOnexDisplayAsync("resolution",JsonValue.CreateStringValue("1920x1200")),"Fixture resolution unavailable");
                PerformanceNavItem.IsChecked=true;tile.IsVisible=true;tile.Order=-1;RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();await Task.Delay(150);await Input("focus",()=>true);await Input("up",()=>true);tile=qsTileMap["OnexResolution"];
                foreach(string expected in new[]{"1680x1050","1440x900","1280x800","1920x1200"})
                {
                    tile.TileButton.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(150);Check(tile.TileButton.Focus(FocusState.Keyboard),"Cannot focus resolution tile");
                    await Input("a",()=>!_onexResolutionTileBusy&&!_onexDisplayBusy&&OnexResolutionTag(OnexCurrentDisplayMode)==expected);Check(OnexCurrentRefreshRate==originalMode.GetNamedNumber("hz"),"Tap changed refresh rate");
                }
                Check(tile.TileButton.Focus(FocusState.Keyboard),"Cannot focus tile for X");await Input("x",()=>_onexDetailsDialog!=null&&_onexResolutionCurrent!=null);await Wait(()=>_onexDetailsDialog.IsLoaded,"Own resolution dialog not shown");
                Check(_onexResolutionChoice.Items.OfType<ComboBoxItem>().Select(item=>(string)item.Tag).SequenceEqual(OnexSupportedResolutions().Select(OnexResolutionTag)),"Full Windows resolution list differs from actual modes");
                Check(_onexOrientationChoice.Items.Count==4&&_onexScaleChoice.Items.OfType<ComboBoxItem>().Select(item=>(int)item.Tag).SequenceEqual(OnexDisplaySystem.GetNamedObject("scale").GetNamedArray("choices").Select(value=>(int)value.GetNumber())),"Orientation / Windows scale choices not native");
                var extra=OnexSupportedResolutions().FirstOrDefault(mode=>OnexResolutionTag(mode)=="1600x900");if(extra!=null){await SetOnexResolutionWindowAsync("resolution",JsonValue.CreateStringValue("1600x900"));Check(OnexResolutionTag(OnexCurrentDisplayMode)=="1600x900","Full-list mode did not apply");await SetOnexResolutionWindowAsync("resolution",JsonValue.CreateStringValue("1920x1200"));}
                await ReadOnexPerformanceProfilesAsync();string storedBefore=OnexActiveProfile.GetNamedObject("dc").GetNamedObject("display").GetNamedObject("mode").Stringify();
                await SetOnexResolutionWindowAsync("displayOrientation",JsonValue.CreateNumberValue(1));Check(OnexSystemNumber(OnexDisplaySystem,"orientation")==1&&OnexResolutionTag(OnexCurrentDisplayMode)=="1200x1920","Portrait UI operation did not apply");
                await ReadOnexPerformanceProfilesAsync();Check(OnexActiveProfile.GetNamedObject("dc").GetNamedObject("display").GetNamedObject("mode").Stringify()==storedBefore,"System orientation changed the saved Performance resolution");
                await SetOnexResolutionWindowAsync("displayOrientation",JsonValue.CreateNumberValue(0));Check(OnexResolutionTag(OnexCurrentDisplayMode)=="1920x1200","Landscape UI operation did not restore");
                int oldScale=OnexSystemNumber(originalSystem.GetNamedObject("scale"),"current"),scale=oldScale==125?150:125;await SetOnexResolutionWindowAsync("displayScale",JsonValue.CreateNumberValue(scale));Check(OnexSystemNumber(OnexDisplaySystem.GetNamedObject("scale"),"current")==scale,"Windows scale UI did not apply");await SetOnexResolutionWindowAsync("displayScale",JsonValue.CreateNumberValue(oldScale));
                if(_onexDetailsDialog!=null)await SaveOnexImage("onex-resolution-223.png",_onexDetailsDialog,300);
                CloseOnexMenus();await Wait(()=>_onexResolutionCurrent==null,"Resolution popup did not close");
                Check(await SetOnexDisplayAsync("refreshRate",JsonValue.CreateNumberValue(60))&&OnexCurrentRefreshRate==60,"Existing refresh path failed after layout changes");Check(await SetOnexDisplayAsync("refreshRate",originalMode["hz"]),"Original refresh could not be restored");
                Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==before.GetNamedString("rawLimits"),"Layout operations changed TDP");
                report["optionalTileDefaultOrderPreserved"]=JsonValue.CreateBooleanValue(true);report["fourNativeTapModesAndX"]=JsonValue.CreateBooleanValue(true);report["fullWindowsModeListAndExtraMode"]=JsonValue.CreateBooleanValue(true);report["orientationRoundtripIndependentProfile"]=JsonValue.CreateBooleanValue(true);report["windowsScaleRoundtrip"]=JsonValue.CreateBooleanValue(true);report["existingRefreshPath"]=JsonValue.CreateBooleanValue(true);report["injectedControllerSteps"]=JsonValue.CreateNumberValue(step);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                CloseOnexMenus();await Wait(()=>!_onexResolutionTileBusy&&!_onexDisplayBusy,"Layout writer did not settle");
                if(OnexSystemNumber(OnexDisplaySystem,"orientation")!=OnexSystemNumber(originalSystem,"orientation"))Check(await SetOnexDisplayAsync("displayOrientation",originalSystem["orientation"]),"Original orientation not restored");
                if(OnexResolutionTag(OnexCurrentDisplayMode)!=OnexResolutionTag(originalMode))Check(await SetOnexDisplayAsync("resolution",JsonValue.CreateStringValue(OnexResolutionTag(originalMode))),"Original resolution not restored");if(OnexCurrentRefreshRate!=originalMode.GetNamedNumber("hz"))Check(await SetOnexDisplayAsync("refreshRate",originalMode["hz"]),"Original Hz not restored");
                int scale=OnexSystemNumber(originalSystem.GetNamedObject("scale"),"current");if(OnexSystemNumber(OnexDisplaySystem.GetNamedObject("scale"),"current")!=scale)Check(await SetOnexDisplayAsync("displayScale",JsonValue.CreateNumberValue(scale)),"Original Windows scale not restored");
                if(created!=null&&OnexProfiles.Any(entry=>entry.GetNamedString("id")==created&&entry.GetNamedString("name")=="ONEXQA Resolution223"))await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(created)}});if(selection=="auto")await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("automatic")}});
                await RefreshOnexDisplayAsync(false);Check(OnexCurrentDisplayMode.Stringify()==originalMode.Stringify()&&OnexSystemNumber(OnexDisplaySystem,"orientation")==OnexSystemNumber(originalSystem,"orientation")&&OnexSystemNumber(OnexDisplaySystem.GetNamedObject("scale"),"current")==scale,"Original actual layout not restored");tile.IsVisible=visible;tile.Order=order;RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();
                var owned=await folder.TryGetItemAsync("onex-resolution-qa-owned.json");if(owned!=null)await owned.DeleteAsync();await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");
            }
        }
    }
}
