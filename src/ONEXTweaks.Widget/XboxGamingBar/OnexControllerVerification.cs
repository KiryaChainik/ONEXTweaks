// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Automation.Peers;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexControllerAsync(JsonObject report)
  {
   void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
   var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N"),ownId=null;string ownName="ONEXQA Controller "+run.Substring(0,10);int step=0;var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);
   JsonObject Tuning()=>_onexControllerState.GetNamedObject("editor").GetNamedObject("policy").GetNamedObject("tuning");
   async Task Settled(){for(int n=0;n<150&&(_onexControllerPending!=null||_onexControllerBusy);n++)await Task.Delay(40);Check(_onexControllerPending==null&&!_onexControllerBusy,"Controller policy write did not settle");await ReadOnexControllerAsync();}
   async Task Input(string key)
   {
    var control=Windows.UI.Xaml.Input.FocusManager.GetFocusedElement() as FrameworkElement;Check(control!=null,"Controller QA has no focused element");var peer=FrameworkElementAutomationPeer.FromElement(control)??FrameworkElementAutomationPeer.CreatePeerForElement(control);var rect=peer.GetBoundingRectangle();string id=run+":"+(++step);
    await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2)}}.Stringify());bool accepted=false;
    for(int n=0;n<120;n++){await Task.Delay(60);try{accepted=await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id;}catch{}if(accepted)break;}Check(accepted,"Controller QA input timed out: "+key);await Task.Delay(100);
   }
   async Task Focus(Control control){control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(120);Check(control.Focus(FocusState.Keyboard),"Controller control could not be focused");await Task.Delay(100);}
   try
   {
    LegionNavItem.IsChecked=true;await Task.Delay(250);var before=await ReadOnexControllerAsync();Check(before.ContainsKey("entries")&&!before.GetNamedBoolean("enabled",false),"Protect an already enabled controller capture");Check(before.GetNamedString("activeId")=="global"&&before.GetNamedString("appliedId")=="global"&&before.GetNamedString("selection")=="automatic","Protect existing owner profile selection");
    var manifest=new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"name",JsonValue.CreateStringValue(ownName)},{"beforeIds",new JsonArray()}};foreach(var row in before.GetNamedArray("entries"))manifest.GetNamedArray("beforeIds").Add(row.GetObject()["id"]);await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-controller-qa-owned.json",CreationCollisionOption.ReplaceExisting),manifest.Stringify());
    await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"name",JsonValue.CreateStringValue(ownName)}});ownId=ControllerEditorId;Check(ownId!="global","Manual controller profile did not select");manifest["createdId"]=JsonValue.CreateStringValue(ownId);await FileIO.WriteTextAsync(await folder.GetFileAsync("onex-controller-qa-owned.json"),manifest.Stringify());
    await _onexControllerPicker.RenameAsync(ownId,ownName+" renamed");Check(_onexControllerState.GetNamedObject("editor").GetNamedString("name")==ownName+" renamed","Controller rename did not persist");
    await Focus(_onexControllerKind);await Input("focus");await Input("a");await Input("down");await Input("down");await Input("a");Check(_onexControllerKind.SelectedIndex==2,"Gamepad mapping action did not select");await Settled();
    await Focus(_onexControllerPadTarget);await Input("a");await Input("down");await Input("a");await Settled();var binding=_onexControllerState.GetNamedObject("editor").GetNamedObject("policy").GetNamedObject("bindings").GetNamedObject("A");Check(binding.GetNamedString("kind")=="gamepad"&&binding.GetNamedString("target")=="B","Controller A to B did not autosave");
    await Focus(_onexControllerLeftDeadzone);double originalValue=_onexControllerLeftDeadzone.Value;await Input("a");await Input("right");Check(_onexControllerLeftDeadzone.Value==originalValue+5,"Controller percentage step is not5");await Input("b");await Settled();Check(Tuning().GetNamedNumber("leftDeadzone")==originalValue,"B applied cancelled controller edit");
    await Input("a");await Input("right");await Input("a");await Settled();Check(Tuning().GetNamedNumber("leftDeadzone")==originalValue+5,"A did not apply controller slider");Check(ReferenceEquals(Windows.UI.Xaml.Input.FocusManager.GetFocusedElement(),_onexControllerLeftDeadzone)&&!OnexInputGuard.IsPreviewing(_onexControllerLeftDeadzone),"A advanced or reengaged another slider");
    await Focus(_onexControllerRightDeadzone);await Input("a");await Input("right");await Input("down");await Settled();Check(Tuning().GetNamedNumber("rightDeadzone")==5&&ReferenceEquals(Windows.UI.Xaml.Input.FocusManager.GetFocusedElement(),_onexControllerThreshold)&&!OnexInputGuard.IsPreviewing(_onexControllerThreshold),"Down failed to save and navigate");
    Check(!_onexControllerState.GetNamedBoolean("enabled"),"Metadata QA enabled physical capture");LegionScrollViewer.ChangeView(null,0,null,true);ApplyOnexTabPalette();await SaveOnexImage("onex-controller-243.png",LegionScrollViewer,200);
    report["sharedProfileCreateRenameAndDeletion"]=JsonValue.CreateBooleanValue(true);report["mappingAutosave"]=JsonValue.CreateBooleanValue(true);report["percentageStep5AndCancelBCommitAAndDown"]=JsonValue.CreateBooleanValue(true);report["injectedControllerSteps"]=JsonValue.CreateNumberValue(step);report["physicalCaptureEnabled"]=JsonValue.CreateBooleanValue(false);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally
   {
    CloseOnexMenus();_onexControllerSaveTimer?.Stop();_onexControllerPending=null;
    try{if(ownId!=null){await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(ownId)}});await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("automatic")}});var item=await folder.TryGetItemAsync("onex-controller-qa-owned.json");if(item!=null)await item.DeleteAsync();}}finally{if(original!=null)original.IsChecked=true;await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");}
   }
  }
 }
}
