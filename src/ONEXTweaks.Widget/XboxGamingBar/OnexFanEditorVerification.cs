// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
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
        private async Task VerifyOnexFanEditorAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;var perf=new List<string>();var fans=new List<string>();string run=Guid.NewGuid().ToString("N");int step=0;
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            async Task Wait(Func<bool> ready,string message){for(int n=0;n<120;n++){if(ready())return;await Task.Delay(80);}throw new TimeoutException(message);}
            async Task Manifest(){var parents=new JsonArray();foreach(string id in perf)parents.Add(JsonValue.CreateStringValue(id));var profiles=new JsonArray();foreach(string id in fans)profiles.Add(JsonValue.CreateStringValue(id));await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-fan-qa-owned.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"performance",parents},{"fans",profiles}}.Stringify());}
            async Task Input(string key,Func<bool> ready)
            {
                string id=run+":"+(++step);var rect=new RadioButtonAutomationPeer(FanNavItem).GetBoundingRectangle();
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2)}}.Stringify());
                bool accepted=false;for(int n=0;n<100;n++){await Task.Delay(80);try{accepted=await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&ready();}catch{}if(accepted)break;}Check(accepted,"Fan injected input failed: "+key+" step "+step+" focus="+(FocusManager.GetFocusedElement() as FrameworkElement)?.Name);await Task.Delay(120);
            }
            async Task<JsonObject> Hardware(){var reply=await App.SendMessageAsync(new ValueSet{{"OnexHardwareHomeGet",true}});ApplyOnexHardwareHomeState((string)reply["OnexHardwareHomeState"]);return _onexHardwareHome.GetNamedObject("fan");}
            JsonObject Entry(string id)=>_onexFanProfiles.GetNamedArray("entries").Select(value=>value.GetObject()).Single(value=>value.GetNamedString("id")==id);
            JsonObject Values(string id)=>Entry(id).GetNamedObject(_onexFanProfiles.GetNamedBoolean("onAc")?"ac":"dc");
            void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            await ReadOnexPerformanceProfilesAsync();await ReadOnexFanProfilesAsync();
            Check(OnexFrontend.ProfileId=="global"&&_onexProfilesState.GetNamedValue("currentSession").ValueType==JsonValueType.Null&&OnexAppliedFanId=="global"&&_onexFanProfiles.GetNamedString("naturalActiveId","global")=="global","Owner profile context protected; Fan QA deferred");
            string selection=_onexProfilesState.GetNamedString("selection");var original=await ReadOnexStateAsync();var originalFan=await Hardware();int initialMode=(int)originalFan.GetNamedNumber("profile");
            try
            {
                FanNavItem.IsChecked=true;await ReadOnexFanProfilesAsync();await _onexFanProfilePicker.OpenCreateAsync();
                var menu=_onexOpenMenus.OfType<MenuFlyout>().Last(value=>value.IsOpen);
                Check((menu.Items.First() as MenuFlyoutItem)?.Tag as string=="manual"&&
                    _onexFanProfiles.GetNamedArray("creationParents",new JsonArray()).Count==0,
                    "Fan Create must offer manual and running apps independently");CloseOnexMenus();
                await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"name",JsonValue.CreateStringValue("ONEXQA Fan216")}});string created=_onexFanProfiles.GetNamedString("activeId");fans.Add(created);await Manifest();
                Check(OnexAppliedFanId==created&&OnexFrontend.ProfileId=="global","Creating a Fan profile affected Performance");
                Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==original.GetNamedString("rawLimits"),"Fan creation changed power limits");
                Check(_onexFanCurveSliders.Length==OnexFanCurveKeys.Length&&_onexFanCustomToggle.IsEnabled,"Full Fan editor controls are unavailable");await Input("focus",()=>true);await Input("up",()=>true);
                _onexFanCustomToggle.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);Check(_onexFanCustomToggle.Focus(FocusState.Keyboard),"Cannot focus Fan custom switch");
                await Input("a",()=>_onexFanCustomToggle.IsOn);await Wait(()=>!OnexFanCurvePending&&Values(created).GetNamedNumber("mode")==3,"Custom toggle did not autosave inactive profile");
                var slider=_onexFanCurveSliders[0];slider.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);Check(slider.Focus(FocusState.Keyboard),"Cannot focus Fan point");double initial=slider.Value;int sign=initial<95?1:-1;
                await Input("a",()=>OnexInputGuard.IsPreviewing(slider));await Input(sign>0?"up":"down",()=>slider.Value==initial+sign*5);await Input("b",()=>!OnexInputGuard.IsPreviewing(slider)&&slider.Value==initial);
                await Input("a",()=>OnexInputGuard.IsPreviewing(slider));await Input(sign>0?"up":"down",()=>slider.Value==initial+sign*5);await Input("a",()=>!OnexInputGuard.IsPreviewing(slider));await Wait(()=>!OnexFanCurvePending&&Values(created).GetNamedObject("curve").GetNamedNumber(OnexFanCurveKeys[0])==initial+sign*5,"Fan point did not save on A");
                Check((await Hardware()).GetNamedNumber("profile")==3,"The active Fan profile did not apply Custom");
                var full=new JsonObject();foreach(string key in OnexFanCurveKeys)full[key]=JsonValue.CreateNumberValue(100);
                await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("values")},{"id",JsonValue.CreateStringValue(created)},{"onAc",JsonValue.CreateBooleanValue(_onexFanProfiles.GetNamedBoolean("onAc"))},{"values",new JsonObject{{"mode",JsonValue.CreateNumberValue(3)},{"curve",full}}}});
                var actual=await Hardware();Check(actual.GetNamedNumber("percent")==100&&actual.GetNamedArray("customCurve").All(value=>value.GetNumber()==100)&&((int)actual.GetNamedNumber("ecControl")&1)==1,"Custom fan duty was not acknowledged by OEM/EC");
                await SaveOnexImage("onex-fan-custom-216.png",(UIElement)FanScrollViewer.Content,300);
                await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"name",JsonValue.CreateStringValue("ONEXQA Fan216 Manual")}});string manual=_onexFanProfiles.GetNamedString("activeId");fans.Add(manual);await Manifest();
                var homeFanMenu=BuildOnexHomeProfileMenu(OnexFanTileCatalog(),OnexFanTileState());
                var items=homeFanMenu.Items.ToArray();
                Check(items.Length>=5&&(items[0] as MenuFlyoutItem)?.Tag as string==OnexAutoFanId&&
                    (items[1] as MenuFlyoutItem)?.Tag as string=="global"&&items[2] is MenuFlyoutSeparator,"Home Fan menu must open with Auto and Global, then a separator");
                var expectedUsers=OnexFanTileState().GetNamedArray("entries").Select(item=>item.GetObject())
                    .Where(entry=>!entry.GetNamedString("id").StartsWith("firmware-",StringComparison.Ordinal)&&entry.GetNamedString("id")!="global")
                    .OrderBy(entry=>OnexCatalogEntryName(entry,false),StringComparer.CurrentCultureIgnoreCase).Select(entry=>entry.GetNamedString("id"));
                Check(items.Skip(3).Select(item=>(item as MenuFlyoutItem)?.Tag as string).SequenceEqual(expectedUsers),"Home Fan user profiles are not alphabetical");
                await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("select")},{"id",JsonValue.CreateStringValue(manual)}});
                FanNavItem.IsChecked=true;_onexFanCustomToggle.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);_onexFanCustomToggle.Focus(FocusState.Keyboard);
                await Input("a",()=>!_onexFanCustomToggle.IsOn&&_onexFanCurveSliders.Select(point=>(int)point.Value).SequenceEqual(OnexDefaultFanCurve())&&_onexFanCurveSliders.All(point=>!point.IsEnabled));
                await Wait(()=>!OnexFanCurvePending&&Values(manual).GetNamedNumber("mode")!=3,"Custom Off did not save");
                Check(Values(manual).GetNamedObject("curve").GetNamedNumber("at45")==100&&(await Hardware()).GetNamedNumber("profile")!=3,"Custom Off lost curve or stayed manual");
                await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("select")},{"id",JsonValue.CreateStringValue(OnexAutoFanId)},{"temporary",JsonValue.CreateBooleanValue(true)}});Check((await Hardware()).GetNamedNumber("profile")==0,"Auto Fan selection did not reach hardware");
                Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==original.GetNamedString("rawLimits"),"Fan operations changed TDP");
                report["createFromFanMenu"]=JsonValue.CreateBooleanValue(true);report["performanceUnaffected"]=JsonValue.CreateBooleanValue(true);report["curveControllerBRevertASave"]=JsonValue.CreateBooleanValue(true);report["activeFanAppliesCustom"]=JsonValue.CreateBooleanValue(true);report["oem100PercentAndManualEcReadback"]=JsonValue.CreateBooleanValue(true);report["orderedHomeDropdown"]=JsonValue.CreateBooleanValue(true);report["independentFanSelection"]=JsonValue.CreateBooleanValue(true);report["offRetainsCurveAndAuto"]=JsonValue.CreateBooleanValue(true);report["injectedControllerSteps"]=JsonValue.CreateNumberValue(step);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["independentRpmMeasured"]=JsonValue.CreateBooleanValue(false);report["actualGameProcessRestartTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                CloseOnexMenus();_onexFanCurveTimer.Stop();_onexFanCurveDraft=null;foreach(var slider in _onexFanCurveSliders)OnexInputGuard.Cancel(slider);await Wait(()=>!_onexFanCurveWriting,"Fan writer did not settle before cleanup");
                foreach(string id in fans.AsEnumerable().Reverse())if(OnexPreset(_onexFanProfiles,id)!=null){await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(id)}});}
                foreach(string id in perf.AsEnumerable().Reverse())if(OnexProfiles.Any(entry=>entry.GetNamedString("id")==id&&entry.GetNamedString("name").StartsWith("ONEXQA Fan216",StringComparison.Ordinal)))await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(id)}});
                await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("select")},{"id",JsonValue.CreateStringValue("global")},{"temporary",JsonValue.CreateBooleanValue(true)}});await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("resume")}});
                if(selection=="auto")await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("automatic")}});
                await ReadOnexFanProfilesAsync();Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==original.GetNamedString("rawLimits")&&(await Hardware()).GetNamedNumber("profile")==initialMode,"Original Fan/TDP not restored");
                var manifest=await folder.TryGetItemAsync("onex-fan-qa-owned.json");if(manifest!=null)await manifest.DeleteAsync();await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");
            }
        }
    }
}
