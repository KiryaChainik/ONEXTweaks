// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.IO;
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
  private async Task VerifyOnexControllerGroupsAsync(JsonObject report)
  {
   void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
   var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N"),ownId=null,name="ONEXQA Groups288 "+run.Substring(0,10);int step=0;bool? originalOem=null;var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);
   async Task Write(string file,string value){for(int n=0;;n++){try{File.WriteAllText(Path.Combine(folder.Path,file),value);return;}catch(IOException)when(n<25){await Task.Delay(20);}}}
   async Task Input(string key,FrameworkElement target=null)
   {
    var control=target??FocusManager.GetFocusedElement() as FrameworkElement;Check(control!=null,"Group QA has no target");var peer=FrameworkElementAutomationPeer.FromElement(control)??FrameworkElementAutomationPeer.CreatePeerForElement(control);var rect=peer.GetBoundingRectangle();double scale=Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;string id=run+":"+(++step);
    await Write("onex-gamepad-qa-state.json",new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2+widget.WindowBounds.X*scale)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2+widget.WindowBounds.Y*scale)}}.Stringify());bool ack=false;for(int n=0;n<120;n++){await Task.Delay(60);try{ack=await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id;}catch{}if(ack)break;}Check(ack,"Group input timeout step"+step);await Task.Delay(140);
   }
   async Task Focus(Control control){control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(120);Check(control.Focus(FocusState.Keyboard),"Group control focus failed: "+control.Name);await Task.Delay(40);}
   async Task ConfirmGroupReset(Button button)
   {
    await Focus(button);
    await Input("a");
    Check(_onexControllerResetMenu?.IsOpen==true,"Group reset did not ask for confirmation");
    var confirm=(MenuFlyoutItem)_onexControllerResetMenu.Items.Single();
    await Focus(confirm);
    await Input("a");
   }
   async Task Saved(){for(int n=0;n<240&&(_onexControllerPending!=null||_onexControllerBusy);n++)await Task.Delay(50);Check(_onexControllerPending==null&&!_onexControllerBusy,"Group operation did not finish");await ReadOnexControllerAsync();Check(ControllerEditorId==ownId,"Group operation left owned profile");}
   JsonObject Binding(string kind,string target=null){var b=new JsonObject{{"kind",JsonValue.CreateStringValue(kind)}};if(target!=null)b["target"]=JsonValue.CreateStringValue(target);return b;}
   async Task Seed(bool onlyM1Widget=false)
   {
    var policy=JsonObject.Parse(ControllerEditorPolicy.Stringify());policy["gamepadRemappingDisabled"]=JsonValue.CreateBooleanValue(false);policy["bindings"]=new JsonObject{{"A",Binding("gamepad","B")},{"X",Binding("gamepad","Y")},{"F13",new JsonObject{{"kind",JsonValue.CreateStringValue("keyboard")},{"keys",new JsonArray{JsonValue.CreateNumberValue(112)}}}}};policy["tuning"]=new JsonObject{{"leftDeadzone",JsonValue.CreateNumberValue(5)},{"rightDeadzone",JsonValue.CreateNumberValue(7)},{"triggerThreshold",JsonValue.CreateNumberValue(35)}};policy["vibration"]=new JsonObject{{"intensity",JsonValue.CreateNumberValue(65)}};
    var oem=new JsonObject();if(onlyM1Widget){foreach(string id in new[]{"Home","GKey","KeyboardMouse","Onex","M1","M2"})oem[id]=Binding(id=="M1"?"widget":"disabled");}else oem["M1"]=Binding("gamepad","A");policy["oem"]=new JsonObject{{"bindings",oem}};await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("edit")},{"id",JsonValue.CreateStringValue(ownId)},{"policy",policy}});
   }
   void Groups(bool standard,bool extra)
   {
    Check(OnexControllerStandardRemapOn==standard&&_onexControllerState.GetNamedBoolean("oemEnabled",false)==extra,"Group state not persisted");Check((_onexControllerStandardInputs.Visibility==Visibility.Visible)==standard&&(_onexControllerOemGrid.Visibility==Visibility.Visible)==extra,"Group grid visibility mismatch");
    foreach(var pair in _onexControllerInputTiles){bool on=pair.Key.StartsWith("OEM:",StringComparison.Ordinal)?extra:standard;Check(pair.Value.IsEnabled==on&&_onexControllerDiagramButtons[pair.Key].IsHitTestVisible==on,"Disabled source still interactive");Check(OnexControllerRows().SelectMany(row=>row).Contains(pair.Value)==on,"Hidden source is still in focus rows");}
    Check(!_onexControllerState.GetNamedBoolean("enabled",false),"Group switch changed global transport");
   }
   try
   {
    LegionNavItem.IsChecked=true;await ReadOnexControllerAsync();Check(!_onexControllerState.GetNamedBoolean("enabled",false)&&!_onexControllerState.GetNamedBoolean("manualOverride",false)&&ControllerEditorId=="global","Protect owner active capture/profile");originalOem=_onexControllerState.GetNamedBoolean("oemEnabled",false);
    var marker=new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"name",JsonValue.CreateStringValue(name)},{"originalOem",JsonValue.CreateBooleanValue(originalOem.Value)},{"beforeIds",new JsonArray()}};foreach(var value in _onexControllerState.GetNamedArray("entries"))marker.GetNamedArray("beforeIds").Add(value.GetObject()["id"]);await Write("onex-controller-groups288-qa-owned.json",marker.Stringify());await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"name",JsonValue.CreateStringValue(name)}});ownId=ControllerEditorId;Check(ownId!="global","Own QA create failed");marker["createdId"]=JsonValue.CreateStringValue(ownId);await Write("onex-controller-groups288-qa-owned.json",marker.Stringify());await Seed();
    Check(_onexControllerSections.Length==5,"Expected five controller cards");var map=_onexControllerSections.Single(card=>card.Id=="mapping");Check(Grid.GetRow(map.Header)==0&&Grid.GetColumnSpan(map.Header)==2,"Remapping is not first full-width card");foreach(var pair in new[]{new{Id="sticks",Row=2,Col=0},new{Id="gyro",Row=2,Col=1},new{Id="vibration",Row=4,Col=0},new{Id="status",Row=4,Col=1}}){var card=_onexControllerSections.Single(c=>c.Id==pair.Id);Check(Grid.GetRow(card.Header)==pair.Row&&Grid.GetColumn(card.Header)==pair.Col,"Card order mismatch");}await SaveOnexImage("onex-controller-cards288.png",_onexControllerCardGrid,120);
    SetOnexControllerCard("mapping");Check(_onexControllerOutline.Connected,"Full-width detail outline is not connected");Check(_onexControllerMappingRows[0][0].Tag as string=="OEM:Home"&&_onexControllerMappingRows[0][1].Tag as string=="OEM:KeyboardMouse"&&_onexControllerMappingRows[1][0].Tag as string=="OEM:GKey"&&_onexControllerMappingRows[1][1].Tag as string=="OEM:Onex"&&_onexControllerMappingRows[2][0].Tag as string=="OEM:M1"&&_onexControllerMappingRows[2][1].Tag as string=="OEM:M2","OEM rows do not match diagram sides");
    if(!originalOem.Value){await Focus(_onexControllerOemEnabled);await Input("a");await Saved();}Groups(true,true);
    await Focus(_onexControllerInputTiles["OEM:M1"]);await Focus(_onexControllerStandardEnabled);await Input("a");await Saved();Groups(false,true);Check(ControllerEditorPolicy.GetNamedObject("bindings").GetNamedObject("A").GetNamedString("target")=="B","Off discarded bindings");var face=_onexControllerDiagramButtons["A"];face.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(120);await Input("touch",face);Check(ControllerSourceId=="OEM:M1"&&!IsOnexRemapOpen(_onexControllerMappingFlyout),"Off gamepad diagram source accepted touch");
    await Focus(_onexControllerOemEnabled);await Input("a");await Saved();Groups(false,false);await SaveOnexImage("onex-controller-groups-off288.png",_onexControllerMappingPanel,80);
    await Focus(_onexControllerStandardEnabled);await Input("a");await Saved();Groups(true,false);await Focus(_onexControllerInputTiles["A"]);var rear=_onexControllerDiagramButtons["OEM:M1"];rear.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(120);await Input("touch",rear);Check(ControllerSourceId=="A"&&!IsOnexRemapOpen(_onexControllerMappingFlyout),"Off OEM diagram source accepted touch");
    await Focus(_onexControllerOemEnabled);await Input("a");await Saved();Groups(true,true);await SaveOnexImage("onex-controller-extra288.png",_onexControllerOemBlock,80);await SaveOnexImage("onex-controller-standard288.png",_onexControllerStandardBlock,80);
    await Focus(_onexControllerInputTiles["A"]);await Input("a");Check(IsOnexRemapOpen(_onexControllerMappingFlyout),"Source editor did not open");await Focus(_onexControllerBindingReset);await Input("a");await Saved();Check(!ControllerEditorPolicy.GetNamedObject("bindings").ContainsKey("A")&&ControllerEditorPolicy.GetNamedObject("bindings").ContainsKey("X")&&_onexControllerInputSummaries["A"].Text=="Original","Single standard reset scope/summary");await Input("b");Check(!IsOnexRemapOpen(_onexControllerMappingFlyout),"Reset editor B close");
    await Focus(_onexControllerStandardEnabled);await Input("a");await Saved();await ConfirmGroupReset(_onexControllerStandardReset);await Saved();Check(!OnexControllerStandardRemapOn&&ControllerEditorPolicy.GetNamedObject("bindings").Count==1&&ControllerEditorPolicy.GetNamedObject("bindings").ContainsKey("F13"),"Group reset did not preserve Off/legacy aliases");Check(ControllerEditorPolicy.GetNamedObject("tuning").GetNamedNumber("leftDeadzone")==5&&ControllerEditorPolicy.GetNamedObject("vibration").GetNamedNumber("intensity")==65&&ControllerEditorPolicy.GetNamedObject("oem").GetNamedObject("bindings").ContainsKey("M1"),"Group reset changed unrelated settings");await Focus(_onexControllerStandardEnabled);await Input("a");await Saved();
    await Seed(true);await Focus(_onexControllerInputTiles["OEM:M1"]);await Input("a");string before=ControllerEditorPolicy.Stringify();await Focus(_onexControllerBindingReset);await Input("a");for(int n=0;n<160&&(_onexControllerPending!=null||_onexControllerBusy);n++)await Task.Delay(50);string rejectedHint=_onexControllerMappingHint.Text;await Saved();Check(ControllerEditorPolicy.Stringify()==before&&rejectedHint.Contains("at least one"),"Last-widget reset was not rejected atomically");await Input("b");await ConfirmGroupReset(_onexControllerOemReset);await Saved();Check(ControllerEditorPolicy.GetNamedObject("oem").GetNamedObject("bindings").Count==0&&_onexControllerInputSummaries["OEM:Home"].Text=="ONEXTweaks"&&_onexControllerInputSummaries["OEM:M1"].Text=="Off","OEM group defaults did not apply");
    SetOnexControllerCard("sticks");await Focus(_onexControllerSticksReset);await Input("a");await Saved();Check(ControllerEditorPolicy.GetNamedObject("tuning").GetNamedNumber("leftDeadzone",0)==0&&ControllerEditorPolicy.GetNamedObject("tuning").GetNamedNumber("triggerThreshold",30)==30&&ControllerEditorPolicy.GetNamedObject("vibration").GetNamedNumber("intensity")==65,"Sticks reset changed vibration or failed");SetOnexControllerCard("vibration");await Focus(_onexControllerVibrationReset);await Input("a");await Saved();Check(ControllerEditorPolicy.GetNamedObject("vibration",null)==null&&ControllerEditorPolicy.GetNamedObject("bindings").ContainsKey("A"),"Vibration reset scope failed");SetOnexControllerCard(null);
    foreach(string key in new[]{"fiveCardLayout","fullWidthJoinedRemapping","leftRightExtraRows","fourIndependentGroupCombinations","groupsPersistThroughRealHelper","disabledGroupsHiddenFromFocus","disabledHotspotsRejectTouch","assignmentsSurviveOffOn","singleButtonRestore","groupRestorePreservesOtherSettingsAndSwitch","legacyAliasesPreserved","lastWidgetRestoreRejected","oemDefaultRestore","sticksAndVibrationIndependentRestores"})report[key]=JsonValue.CreateBooleanValue(true);report["injectedInputSteps"]=JsonValue.CreateNumberValue(step);report["actualOemPrepareReleaseTested"]=JsonValue.CreateBooleanValue(true);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["productionCaptureEnabled"]=JsonValue.CreateBooleanValue(false);
   }
   finally
   {
    _onexControllerMappingFlyout?.Hide();if(ownId!=null){await ReadOnexControllerAsync();if(originalOem.HasValue&&_onexControllerState.GetNamedBoolean("oemEnabled",false)!=originalOem.Value)await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("oemEnable")},{"enabled",JsonValue.CreateBooleanValue(originalOem.Value)}});var own=_onexControllerState.GetNamedArray("entries").Select(value=>value.GetObject()).SingleOrDefault(value=>ControllerText(value,"id")==ownId);if(own!=null){Check(ControllerText(own,"name")==name,"QA identity changed; not deleting");await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(ownId)}});await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("automatic")}});}var marker=await folder.TryGetItemAsync("onex-controller-groups288-qa-owned.json");if(marker!=null)await marker.DeleteAsync();}if(_onexControllerOpen!=null)SetOnexControllerCard(null);if(original!=null)original.IsChecked=true;await Write("onex-gamepad-qa-state.json","{\"finished\":true}");
   }
  }
 }
}
