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
  private async Task VerifyOnexControllerProfileModeAsync(JsonObject report)
  {
   void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
   var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N"),ownId=null,name="ONEXQA ProfileMode284 "+run.Substring(0,10);int step=0;var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);
   async Task Write(string file,string value){for(int n=0;;n++){try{File.WriteAllText(Path.Combine(folder.Path,file),value);return;}catch(IOException)when(n<25){await Task.Delay(20);}}}
   async Task Input(string key)
   {
    var control=FocusManager.GetFocusedElement() as FrameworkElement;Check(control!=null,"Profile-mode QA has no focus");var peer=FrameworkElementAutomationPeer.FromElement(control)??FrameworkElementAutomationPeer.CreatePeerForElement(control);var rect=peer.GetBoundingRectangle();double scale=Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;string id=run+":"+(++step);
    await Write("onex-gamepad-qa-state.json",new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2+widget.WindowBounds.X*scale)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2+widget.WindowBounds.Y*scale)}}.Stringify());bool accepted=false;for(int n=0;n<120;n++){await Task.Delay(60);try{accepted=await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id;}catch{}if(accepted)break;}Check(accepted,"Profile-mode input timeout");await Task.Delay(100);
   }
   async Task Settled(bool value){for(int n=0;n<120&&(_onexControllerPending!=null||_onexControllerBusy);n++)await Task.Delay(50);Check(_onexControllerPending==null&&!_onexControllerBusy,"Profile exception did not save");await ReadOnexControllerAsync();Check(ReferenceEquals(FocusManager.GetFocusedElement(),_onexControllerUseHardware),"Saving a profile exception moved gamepad focus");Check(ControllerEditorPolicy.GetNamedBoolean("useHardwareController",false)==value,"Profile Hardware exception did not persist");Check(!_onexControllerState.GetNamedBoolean("enabled",false),"Metadata UI test must preserve global Hardware mode");}
   try
   {
    LegionNavItem.IsChecked=true;await ReadOnexControllerAsync();Check(!_onexControllerState.GetNamedBoolean("enabled",false)&&!_onexControllerState.GetNamedBoolean("manualOverride",false)&&ControllerEditorId=="global"&&ControllerText(_onexControllerState,"appliedId")=="global","Protect owner active controller mode/selection");Check(_onexControllerHardwareProfileCard.Visibility==Visibility.Collapsed,"Global profile must use top-level Controller Mode");
    var owned=new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"name",JsonValue.CreateStringValue(name)},{"beforeIds",new JsonArray()}};foreach(var entry in _onexControllerState.GetNamedArray("entries"))owned.GetNamedArray("beforeIds").Add(entry.GetObject()["id"]);await Write("onex-profilemode284-qa-owned.json",owned.Stringify());
    await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"name",JsonValue.CreateStringValue(name)}});ownId=ControllerEditorId;Check(ownId!="global","QA profile did not create");owned["createdId"]=JsonValue.CreateStringValue(ownId);await Write("onex-profilemode284-qa-owned.json",owned.Stringify());
    Check(_onexControllerHardwareProfileCard.Visibility==Visibility.Visible&&OnexControllerRows().SelectMany(row=>row).Contains(_onexControllerUseHardware),"Profile exception is missing from UI/focus graph");
    _onexControllerUseHardware.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(120);Check(_onexControllerUseHardware.Focus(FocusState.Keyboard),"Cannot focus profile exception");await Input("a");await Settled(true);await SaveOnexImage("onex-controller-hardware284.png",_onexControllerHardwareProfileCard,120);await Input("a");await Settled(false);await Input("touch");await Settled(true);await Input("a");await Settled(false);
    Check(_onexControllerTransport.SelectedIndex==0,"Per-profile setting changed global transport choice");
    foreach(string key in new[]{"profileScopedAutosave","globalModeUnchanged","gamepadToggleBothDirections","touchToggle","exceptionInNavigation","globalUsesTransportStrip"})report[key]=JsonValue.CreateBooleanValue(true);report["injectedInputSteps"]=JsonValue.CreateNumberValue(step);report["captureEnabled"]=JsonValue.CreateBooleanValue(false);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally
   {
    if(ownId!=null){await ReadOnexControllerAsync();var own=_onexControllerState.GetNamedArray("entries").Select(value=>value.GetObject()).SingleOrDefault(entry=>ControllerText(entry,"id")==ownId);if(own!=null){Check(ControllerText(own,"name")==name,"QA profile identity changed; deletion refused");await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(ownId)}});await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("automatic")}});}var marker=await folder.TryGetItemAsync("onex-profilemode284-qa-owned.json");if(marker!=null)await marker.DeleteAsync();}
    if(original!=null)original.IsChecked=true;await Write("onex-gamepad-qa-state.json","{\"finished\":true}");
   }
  }
 }
}
