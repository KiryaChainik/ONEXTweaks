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

  private async Task RecoverOnexControllerDiagramQaAsync(JsonObject report)
  {
   await ReadOnexControllerAsync();var folder=ApplicationData.Current.LocalFolder;var marker=await folder.TryGetItemAsync("onex-controllermap285-qa-owned.json");bool removed=false;
   if(marker!=null)
   {
    var owned=JsonObject.Parse(await FileIO.ReadTextAsync((StorageFile)marker));string id=ControllerText(owned,"createdId"),name=ControllerText(owned,"name"),run=ControllerText(owned,"run");
    if(string.IsNullOrEmpty(id)||id=="global"||run.Length!=32||name!="ONEXQA Mapping285 "+run.Substring(0,10)||owned.GetNamedArray("beforeIds").Any(value=>value.GetString()==id))throw new InvalidOperationException("Previous QA identity proof invalid; not deleting");
    var entry=_onexControllerState.GetNamedArray("entries").Select(value=>value.GetObject()).SingleOrDefault(value=>ControllerText(value,"id")==id);
    if(entry!=null){if(ControllerText(entry,"name")!=name)throw new InvalidOperationException("Previous QA name changed; not deleting");bool selected=ControllerEditorId==id;await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(id)}});if(selected)await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("automatic")}});removed=true;}await marker.DeleteAsync();
   }
   report["removedOnlyProvenOwnInterruptedQaProfile"]=JsonValue.CreateBooleanValue(removed);report["ownerSettingsRestoredFromOldSnapshot"]=JsonValue.CreateBooleanValue(false);report["productionCaptureEnabled"]=JsonValue.CreateBooleanValue(false);
  }
  private async Task VerifyOnexControllerDiagramAsync(JsonObject report)
  {
   void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
   var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N"),ownId=null,name="ONEXQA Mapping285 "+run.Substring(0,10);int step=0;var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);string previousCard=_onexControllerOpen;
   async Task Write(string file,string value){for(int n=0;;n++){try{File.WriteAllText(Path.Combine(folder.Path,file),value);return;}catch(IOException)when(n<25){await Task.Delay(20);}}}
   async Task Input(string key,FrameworkElement target=null)
   {
    var control=target??FocusManager.GetFocusedElement() as FrameworkElement;Check(control!=null,"Mapping QA has no target");var peer=FrameworkElementAutomationPeer.FromElement(control)??FrameworkElementAutomationPeer.CreatePeerForElement(control);var rect=peer.GetBoundingRectangle();double scale=Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;string id=run+":"+(++step);
    await Write("onex-gamepad-qa-state.json",new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2+widget.WindowBounds.X*scale)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2+widget.WindowBounds.Y*scale)}}.Stringify());bool accepted=false;for(int n=0;n<120;n++){await Task.Delay(60);try{accepted=await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id;}catch{}if(accepted)break;}Check(accepted,"Mapping input timeout at step"+step);await Task.Delay(130);
   }
   async Task Focus(Control control){control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(140);Check(control.Focus(FocusState.Keyboard),"Cannot focus "+control.Name);await Task.Delay(50);}
   async Task Saved(){for(int n=0;n<120&&(_onexControllerPending!=null||_onexControllerBusy);n++)await Task.Delay(50);Check(_onexControllerPending==null&&!_onexControllerBusy,"Mapping did not settle");await ReadOnexControllerAsync();Check(ControllerEditorId==ownId,"Mapping escaped own QA profile");}
   try
   {
    LegionNavItem.IsChecked=true;await ReadOnexControllerAsync();Check(!_onexControllerState.GetNamedBoolean("enabled",false)&&!_onexControllerState.GetNamedBoolean("manualOverride",false)&&ControllerEditorId=="global"&&ControllerText(_onexControllerState,"appliedId")=="global","Protect owner active controller mode/selection");
    var owned=new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"name",JsonValue.CreateStringValue(name)},{"beforeIds",new JsonArray()}};foreach(var entry in _onexControllerState.GetNamedArray("entries"))owned.GetNamedArray("beforeIds").Add(entry.GetObject()["id"]);await Write("onex-controllermap285-qa-owned.json",owned.Stringify());
    await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"name",JsonValue.CreateStringValue(name)}});ownId=ControllerEditorId;Check(ownId!="global","QA profile did not create");owned["createdId"]=JsonValue.CreateStringValue(ownId);await Write("onex-controllermap285-qa-owned.json",owned.Stringify());bool legacyMouse=ControllerEditorPolicy.GetNamedBoolean("mouseMode",false);
    if(_onexControllerOpen!="mapping")SetOnexControllerCard("mapping");for(int n=0;n<50&&!_onexControllerDiagramReady&&_onexControllerDiagramError==null;n++)await Task.Delay(80);Check(_onexControllerDiagramReady,"Native diagram did not load: "+_onexControllerDiagramError);
    var sources=OnexControllerOemSources.Concat(ControllerInputs).ToArray();Check(sources.Length==30&&_onexControllerInputTiles.Count==30&&_onexControllerDiagramButtons.Count==30,"Missing physical source selector/hotspot");Check(sources.All(id=>_onexControllerInputTiles.ContainsKey(id)&&_onexControllerDiagramButtons.ContainsKey(id)),"Source ID differs from policy adapter");
    Check(((Grid)_onexControllerMappingPanel.Children[0]).Children.Contains(_onexControllerDiagram)&&_onexControllerMappingPanel.Children[1]==_onexControllerOemBlock,"Extra buttons are not immediately below diagram");Check(!_onexControllerSource.IsLoaded&&!OnexControllerRows().SelectMany(row=>row).Contains(_onexControllerSource),"Legacy source dropdown remains in visual/focus tree");Check(!OnexOwnedContent(_onexControllerSections[0].Detail).OfType<ComboBox>().Any(),"Mapping overview still contains a dropdown");
    foreach(string id in sources){await Focus(_onexControllerInputTiles[id]);Check(ControllerSourceId==id&&_onexControllerDiagramName.Text==OnexControllerSourceName(id),"Source focus does not synchronize diagram");Check(_onexControllerInputTiles[id].Visibility==Visibility.Visible&&OnexControllerRows().SelectMany(row=>row).Contains(_onexControllerInputTiles[id]),"Source missing from focus graph");Check(!_onexControllerDiagramButtons[id].IsTabStop,"Duplicate tiny diagram focus target");}
    Check(_onexControllerPending==null&&!_onexControllerBusy,"Source selection queued an edit");
    LegionScrollViewer.ChangeView(null,0,null,true);await Task.Delay(100);_onexControllerDiagram.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(160);await SaveOnexImage("onex-controller-diagram285.png",_onexControllerDiagram,80);await SaveOnexImage("onex-controller-extra285.png",_onexControllerOemBlock,80);await SaveOnexImage("onex-controller-inputs285.png",_onexControllerStandardInputs,80);
    var face=_onexControllerDiagramButtons["A"];face.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(150);await Input("touch",face);Check(ControllerSourceId=="A"&&IsOnexRemapOpen(_onexControllerMappingFlyout),"Touch hotspot did not open source editor: "+_onexControllerDiagramError);Check(_onexControllerKind.Items.OfType<ComboBoxItem>().Skip(5).All(item=>!item.IsEnabled),"Standard source exposed OEM system actions");
    Check(ReferenceEquals(FocusManager.GetFocusedElement(),_onexControllerKind),"Source editor did not focus Action");await Input("a");await Input("down");await Input("down");await Input("a");Check(_onexControllerKind.SelectedIndex==2,"Gamepad did not select Gamepad action");await Saved();await Input("down");Check(ReferenceEquals(FocusManager.GetFocusedElement(),_onexControllerPadTarget),"Down skipped action target");await Input("a");await Input("down");await Input("a");await Saved();Check(ControllerText(ControllerEditorPolicy.GetNamedObject("bindings").GetNamedObject("A"),"target")=="B","Gamepad A->B did not save");Check(_onexControllerInputSummaries["A"].Text=="B","Source summary did not update");await SaveOnexImage("onex-controller-popup285.png",_onexControllerMappingEditor,100);await Input("b");Check(!IsOnexRemapOpen(_onexControllerMappingFlyout)&&_onexControllerOpen=="mapping","B did not close only the source editor");
    await Focus(_onexControllerInputTiles["OEM:M1"]);await Input("a");Check(IsOnexRemapOpen(_onexControllerMappingFlyout)&&_onexControllerDiagramName.Text=="Rear · M1","Gamepad did not open rear source editor");Check(_onexControllerKind.Items.OfType<ComboBoxItem>().Skip(5).All(item=>item.IsEnabled),"OEM source lost system actions");
    await Input("a");int index=_onexControllerKind.SelectedIndex;for(int n=index;n<5;n++)await Input("down");for(int n=index;n>5;n--)await Input("up");await Input("a");await Saved();Check(ControllerText(ControllerEditorPolicy.GetNamedObject("oem").GetNamedObject("bindings").GetNamedObject("M1"),"kind")=="widget","Rear source binding did not save");await Input("b");Check(!IsOnexRemapOpen(_onexControllerMappingFlyout),"Rear source editor did not close");Check(ControllerEditorPolicy.GetNamedBoolean("mouseMode",false)==legacyMouse,"Removed Input mode rewrote legacy data");
    await Focus(_onexControllerInputTiles["A"]);await Input("right");Check(ControllerSourceId=="B","D-pad right did not select next visible source");await Input("down");Check(ControllerSourceId=="RB","D-pad down did not follow source row");await Input("up");Check(ControllerSourceId=="B","D-pad up did not return");await Input("b");Check(_onexControllerOpen==null,"B did not close mapping section");
    foreach(string key in new[]{"nativeVectorLoaded","all30VisibleSourceTiles","all30MatchingDiagramHotspots","oemBlockImmediatelyBelowDiagram","noSourceDropdownInVisualOrNavigation","noInputModeEditor","noDuplicateDiagramGamepadFocus","allSourcesHighlightOnFocus","sourceSelectionHasNoWrites","injectedTouchHotspotOpensEditor","injectedGamepadActionAndTargetSave","oemRearBindingAutosave","liveMappingSummaries","popupBClose","sourceDpadNavigation","legacyMouseModePreserved"})report[key]=JsonValue.CreateBooleanValue(true);report["injectedInputSteps"]=JsonValue.CreateNumberValue(step);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["productionCaptureEnabled"]=JsonValue.CreateBooleanValue(false);report["firmwareDriverChanges"]=JsonValue.CreateNumberValue(0);
   }
   finally
   {
    _onexControllerMappingFlyout?.Hide();if(ownId!=null){await ReadOnexControllerAsync();var own=_onexControllerState.GetNamedArray("entries").Select(value=>value.GetObject()).SingleOrDefault(entry=>ControllerText(entry,"id")==ownId);if(own!=null){Check(ControllerText(own,"name")==name,"Own profile identity changed; deletion refused");await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(ownId)}});await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("automatic")}});}var marker=await folder.TryGetItemAsync("onex-controllermap285-qa-owned.json");if(marker!=null)await marker.DeleteAsync();}
    if(_onexControllerOpen!=null)SetOnexControllerCard(null);if(previousCard!=null)SetOnexControllerCard(previousCard);if(original!=null)original.IsChecked=true;await Write("onex-gamepad-qa-state.json","{\"finished\":true}");
   }
  }
 }
}
