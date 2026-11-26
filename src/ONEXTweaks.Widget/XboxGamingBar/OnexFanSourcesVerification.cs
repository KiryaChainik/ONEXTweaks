// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.IO;
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
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexFanSourceEditorsAsync(JsonObject report)
  {
   var folder=ApplicationData.Current.LocalFolder;var perf=new List<string>();var fans=new List<string>();string run=Guid.NewGuid().ToString("N");int step=0;
   void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
   async Task Wait(Func<bool> ready,string text){for(int n=0;n<120;n++){if(ready())return;await Task.Delay(60);}throw new TimeoutException(text);}
   async Task Write(string name,string text){for(int n=0;;n++){try{File.WriteAllText(Path.Combine(folder.Path,name),text);return;}catch(IOException){if(n>=40)throw;await Task.Delay(25);}}}
   async Task Manifest(){var a=new JsonArray();foreach(string id in perf)a.Add(JsonValue.CreateStringValue(id));var b=new JsonArray();foreach(string id in fans)b.Add(JsonValue.CreateStringValue(id));await Write("onex-fan-sources-qa-owned.json",new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"performance",a},{"fans",b}}.Stringify());}
   async Task Focus(Control control){control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(80);Check(control.Focus(FocusState.Keyboard),"Cannot focus Fan source control");await Task.Delay(40);}
   async Task Input(string key,Func<bool> ready)
   {
    string id=run+":"+(++step);var rect=new RadioButtonAutomationPeer(FanNavItem).GetBoundingRectangle();await Write("onex-gamepad-qa-state.json",new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2)}}.Stringify());
    bool accepted=false;for(int n=0;n<100;n++){await Task.Delay(60);try{accepted=File.ReadAllText(Path.Combine(folder.Path,"onex-gamepad-qa-ack.txt"))==id&&ready();}catch{}if(accepted)break;}Check(accepted,"Fan source input failed: "+key+" step="+step);await Task.Delay(100);
   }
   JsonObject Entry(string id)=>OnexPreset(_onexFanProfiles,id);
   JsonObject Values(string id,bool ac)=>Entry(id).GetNamedObject(ac?"ac":"dc");
   async Task<JsonObject> Hardware(){var reply=await App.SendMessageAsync(new ValueSet{{"OnexHardwareHomeGet",true}});ApplyOnexHardwareHomeState((string)reply["OnexHardwareHomeState"]);return _onexHardwareHome.GetNamedObject("fan");}
   string Curve(JsonObject fan)=>fan.GetNamedValue("customCurve",JsonValue.CreateNullValue()).Stringify();
   await ReadOnexPerformanceProfilesAsync();await ReadOnexFanProfilesAsync();
   Check(OnexFrontend.ProfileId=="global"&&OnexPreset(_onexFanProfiles,OnexAppliedFanId)!=null&&_onexFanProfiles.GetNamedString("naturalActiveId","global")=="global","Owner context protected; Fan source QA deferred");
   string selection=_onexProfilesState.GetNamedString("selection");var originalTab=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);var power=await ReadOnexStateAsync();var initialFan=await Hardware();string originalCurve=Curve(initialFan);int initialMode=(int)initialFan.GetNamedNumber("profile");
   string originalApplied=OnexAppliedFanId;var originalValues=JsonObject.Parse(Values(OnexAppliedFanId,_onexFanProfiles.GetNamedBoolean("onAc")&&Entry(OnexAppliedFanId).GetNamedBoolean("split",false)).Stringify());
   try
   {
    foreach(string suffix in new[]{" A"," B"}){await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"name",JsonValue.CreateStringValue("ONEXQA FanSources254 "+run.Substring(0,8)+suffix)}});perf.Add(OnexFrontend.ProfileId);await Manifest();}
    FanNavItem.IsChecked=true;await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"performanceProfileId",JsonValue.CreateStringValue(perf[0])}});string own=_onexFanProfiles.GetNamedString("activeId");fans.Add(own);await Manifest();
    Check(OnexAppliedFanId==originalApplied&&_onexFanProfiles.GetNamedBoolean("editingInactiveProfile"),"Fan fixture must remain inactive");await Input("focus",()=>true);await Focus(_onexFanProfilePicker.Split);await Input("a",()=>_onexFanProfilePicker.Split.IsOn);await Wait(()=>Entry(own).GetNamedBoolean("split")&&_onexFanAcForm.Root.Visibility==Visibility.Visible,"Split did not reveal complete AC editor");
    var parent=(Panel)FanScrollViewer.Content;Check(parent.Children.IndexOf(_onexFanProfilePickerCard)<parent.Children.IndexOf(_onexFanDcForm.Root)&&parent.Children.IndexOf(_onexFanDcForm.Root)<parent.Children.IndexOf(_onexFanProfileFooter)&&parent.Children.IndexOf(_onexFanProfileFooter)<parent.Children.IndexOf(_onexFanAcForm.Root),"Fan sections are not Header/DC/Split/AC");
    foreach(var f in OnexFanForms)
    {
     Check(f.Sliders.Length==OnexFanCurveKeys.Length&&f.Heading.Visibility==Visibility.Visible,"Incomplete Fan source editor");string other=Values(own,!f.Ac).Stringify();
     await Focus(f.Custom);if(!f.Custom.IsOn)await Input("a",()=>f.Custom.IsOn);await Wait(()=>!OnexFanCurvePending&&Values(own,f.Ac).GetNamedNumber("mode")==3,"Source custom mode did not save");
     var slider=f.Sliders[0];await Focus(slider);double initial=slider.Value;int sign=initial<90?1:-1;
     await Input("a",()=>OnexInputGuard.IsPreviewing(slider));await Input(sign>0?"right":"left",()=>slider.Value==initial+sign*5);await Input("b",()=>!OnexInputGuard.IsPreviewing(slider)&&slider.Value==initial);
     await Input("a",()=>OnexInputGuard.IsPreviewing(slider));await Input(sign>0?"right":"left",()=>slider.Value==initial+sign*5);await Input("down",()=>!ReferenceEquals(FocusManager.GetFocusedElement(),slider));await Wait(()=>!OnexFanCurvePending&&Values(own,f.Ac).GetNamedObject("curve").GetNamedNumber(OnexFanCurveKeys[0])==initial+sign*5,"Source point did not save on down");
     Check(Values(own,!f.Ac).Stringify()==other,"Source editor overwrote its other side");var currentFan=await Hardware();Check(currentFan.GetNamedNumber("profile")==initialMode&&Curve(currentFan)==originalCurve,"Inactive editor changed actual Fan output or curve");
    }
    string savedAc=Values(own,true).Stringify();await Focus(_onexFanProfilePicker.Split);await Input("a",()=>!_onexFanProfilePicker.Split.IsOn);await Wait(()=>!Entry(own).GetNamedBoolean("split")&&_onexFanAcForm.Root.Visibility==Visibility.Collapsed,"Split off did not hide AC");Check(_onexFanDcForm.Heading.Visibility==Visibility.Collapsed&&Values(own,true).Stringify()==savedAc,"Hidden AC settings were lost");
    var dc=_onexFanDcForm.Sliders[0];await Focus(dc);double before=dc.Value;int delta=before<90?5:-5;await Input("a",()=>OnexInputGuard.IsPreviewing(dc));await Input(delta>0?"right":"left",()=>dc.Value==before+delta);await Input("a",()=>!OnexInputGuard.IsPreviewing(dc));await Wait(()=>!OnexFanCurvePending&&Values(own,false).GetNamedObject("curve").GetNamedNumber(OnexFanCurveKeys[0])==before+delta,"Unified point did not commit through A");Check(Values(own,true).Stringify()==savedAc,"Unified edit overwrote archived AC");
    await Focus(_onexFanProfilePicker.Split);await Input("a",()=>_onexFanProfilePicker.Split.IsOn);await Wait(()=>Entry(own).GetNamedBoolean("split")&&_onexFanAcForm.Root.Visibility==Visibility.Visible,"Split on did not restore AC");Check(Values(own,true).Stringify()==savedAc&&_onexFanAcForm.Sliders[0].Value==Values(own,true).GetNamedObject("curve").GetNamedNumber(OnexFanCurveKeys[0]),"Restored AC editor differs from saved AC");
    await Focus(_onexFanAcForm.Custom);await Input("up",()=>ReferenceEquals(FocusManager.GetFocusedElement(),_onexFanProfilePicker.Split));
    // Activate only the owner's exact current Fan values. Test a changed OTHER side;
    // the OEM curve and output must remain untouched by that inactive-source write.
    bool onAc=_onexFanProfiles.GetNamedBoolean("onAc");await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("values")},{"id",JsonValue.CreateStringValue(own)},{"onAc",JsonValue.CreateBooleanValue(onAc)},{"source",JsonValue.CreateStringValue(onAc?"ac":"dc")},{"values",originalValues}});
    await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("select")},{"id",JsonValue.CreateStringValue(perf[0])}});await ReadOnexFanProfilesAsync();Check(OnexAppliedFanId==own,"Performance activation did not select bound Fan profile");
    var hardwareBefore=await Hardware();var otherValues=JsonObject.Parse(Values(own,!onAc).Stringify());otherValues["mode"]=JsonValue.CreateNumberValue(3);
    await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("values")},{"id",JsonValue.CreateStringValue(own)},{"onAc",JsonValue.CreateBooleanValue(onAc)},{"source",JsonValue.CreateStringValue(onAc?"dc":"ac")},{"values",otherValues}});var hardwareAfter=await Hardware();Check(hardwareBefore.GetNamedNumber("profile")==hardwareAfter.GetNamedNumber("profile")&&Curve(hardwareBefore)==Curve(hardwareAfter),"Inactive source of active profile changed OEM output");
    await SaveOnexImage("onex-fan-sources255.png",(UIElement)FanScrollViewer.Content,180);
    report["fullDcSplitAcLayout"]=JsonValue.CreateBooleanValue(true);report["independentCurveAutosaveBothSources"]=JsonValue.CreateBooleanValue(true);report["bothSourcesBRevertDownSaveAndACommit"]=JsonValue.CreateBooleanValue(true);report["offEditOnRetainsHiddenAc"]=JsonValue.CreateBooleanValue(true);report["acUpReturnsToSplit"]=JsonValue.CreateBooleanValue(true);report["activeProfileInactiveSourceNoOemChange"]=JsonValue.CreateBooleanValue(true);report["injectedControllerSteps"]=JsonValue.CreateNumberValue(step);report["physicalPowerSourceTested"]=JsonValue.CreateBooleanValue(false);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally
   {
    CloseOnexMenus();foreach(var f in OnexFanForms){f.Timer.Stop();f.Draft=null;foreach(var slider in f.Sliders)OnexInputGuard.Cancel(slider);}await Wait(()=>!_onexFanCurveWriting,"Fan writers did not settle");
    foreach(string id in fans.AsEnumerable().Reverse())if(Entry(id)!=null)await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(id)}});
    foreach(string id in perf.AsEnumerable().Reverse())if(OnexProfiles.Any(entry=>entry.GetNamedString("id")==id&&entry.GetNamedString("name").StartsWith("ONEXQA FanSources254 "+run.Substring(0,8),StringComparison.Ordinal)))await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(id)}});
    if(selection=="auto")await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("automatic")}});
    await ReadOnexFanProfilesAsync();var current=await Hardware();Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==power.GetNamedString("rawLimits")&&current.GetNamedNumber("profile")==initialMode&&Curve(current)==originalCurve,"Owner Fan/TDP was not preserved");
    var manifest=await folder.TryGetItemAsync("onex-fan-sources-qa-owned.json");if(manifest!=null)await manifest.DeleteAsync();report["ownQaProfilesDeletedAfterRun"]=JsonValue.CreateBooleanValue(true);if(originalTab!=null)originalTab.IsChecked=true;await Write("onex-gamepad-qa-state.json","{\"finished\":true}");
   }
  }
 }
}
