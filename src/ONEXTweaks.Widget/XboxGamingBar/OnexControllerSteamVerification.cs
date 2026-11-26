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
  private async Task VerifyOnexControllerSteamAsync(JsonObject report)
  {
   void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
   var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N"),ownId=null,name="ONEXQA Steam297 "+run.Substring(0,10);int step=0;var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);
   async Task Write(string file,string value){for(int n=0;;n++){try{File.WriteAllText(Path.Combine(folder.Path,file),value);return;}catch(IOException)when(n<25){await Task.Delay(20);}}}
   async Task Input(string key)
   {
    var control=FocusManager.GetFocusedElement() as FrameworkElement;Check(control!=null,"Actions input target missing");var peer=FrameworkElementAutomationPeer.FromElement(control)??FrameworkElementAutomationPeer.CreatePeerForElement(control);var r=peer.GetBoundingRectangle();double scale=Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;string id=run+":"+(++step);await Write("onex-gamepad-qa-state.json",new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(r.X+r.Width/2+widget.WindowBounds.X*scale)},{"y",JsonValue.CreateNumberValue(r.Y+r.Height/2+widget.WindowBounds.Y*scale)}}.Stringify());bool ack=false;for(int n=0;n<120;n++){await Task.Delay(60);try{ack=await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id;}catch{}if(ack)break;}Check(ack,"Actions input timeout at "+step);await Task.Delay(130);
   }
   async Task Focus(Control control){control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(120);Check(control.Focus(FocusState.Keyboard),"Actions focus failed: "+control.Name);await Task.Delay(40);}
   async Task Saved(){for(int n=0;n<180&&(_onexControllerPending!=null||_onexControllerBusy);n++)await Task.Delay(50);Check(_onexControllerPending==null&&!_onexControllerBusy,"Actions save did not settle");await ReadOnexControllerAsync();Check(ControllerEditorId==ownId,"Actions escaped QA profile");}
   async Task Pick(ComboBox choice,int index){await Focus(choice);int before=choice.SelectedIndex;await Input("a");Check(choice.IsDropDownOpen,"Action picker did not open");for(int n=before;n<index;n++)await Input("down");for(int n=before;n>index;n--)await Input("up");await Input("a");await Saved();Check(choice.SelectedIndex==index,"Action picker selection mismatch");}
   async Task Source(string id){await Focus(_onexControllerInputTiles[id]);await Input("a");Check(_onexControllerMappingFlyout.IsOpen&&ControllerSourceId==id,"Source editor failed");}
   try
   {
    LegionNavItem.IsChecked=true;await ReadOnexControllerAsync();Check(!_onexControllerState.GetNamedBoolean("enabled",false)&&!_onexControllerState.GetNamedBoolean("manualOverride",false)&&ControllerEditorId=="global"&&_onexControllerState.GetNamedBoolean("oemEnabled",false),"Protect owner mode/selection; extras must already be enabled");var marker=new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"name",JsonValue.CreateStringValue(name)},{"beforeIds",new JsonArray()}};foreach(var item in _onexControllerState.GetNamedArray("entries"))marker.GetNamedArray("beforeIds").Add(item.GetObject()["id"]);await Write("onex-controller-steam297-qa-owned.json",marker.Stringify());await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"name",JsonValue.CreateStringValue(name)}});ownId=ControllerEditorId;Check(ownId!="global","Own QA profile missing");marker["createdId"]=JsonValue.CreateStringValue(ownId);await Write("onex-controller-steam297-qa-owned.json",marker.Stringify());
    var policy=JsonObject.Parse(ControllerEditorPolicy.Stringify());policy["gamepadRemappingDisabled"]=JsonValue.CreateBooleanValue(false);await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("edit")},{"id",JsonValue.CreateStringValue(ownId)},{"policy",policy}});
    if(_onexControllerOpen!="mapping")SetOnexControllerCard("mapping");await Source("OEM:M1");Check(_onexControllerKind.Items.Count==10,"Steam actions missing from OEM chooser");Check(_onexControllerKind.Items.OfType<ComboBoxItem>().Skip(7).Take(2).All(item=>item.IsEnabled),"Steam OEM actions disabled");
    string Single()=>ControllerText(ControllerEditorPolicy.GetNamedObject("oem").GetNamedObject("bindings").GetNamedObject("M1"),"kind");
    string Double()=>ControllerText(ControllerEditorPolicy.GetNamedObject("oem").GetNamedObject("doublePresses").GetNamedObject("M1").GetNamedObject("binding"),"kind");
    await Pick(_onexControllerKind,7);Check(Single()=="steamBigPicture"&&_onexControllerMappingHint.Text.Contains("Big Picture"),"Steam Big Picture single did not save/help missing");
    await Pick(_onexControllerKind,8);Check(Single()=="steamQuickAccess"&&_onexControllerMappingHint.Text.Contains("Quick Access"),"Steam Quick Access single did not save/help missing");
    await SaveOnexImage("onex-controller-steam297-single.png",(FrameworkElement)_onexControllerMappingFlyout.Content,100);
    await Focus(_onexControllerDoubleEnabled);await Input("a");await Saved();Check(_onexControllerDoubleEnabled.IsOn,"Double mode did not enable");await Pick(_onexControllerGesture,1);await Pick(_onexControllerKind,7);Check(Double()=="steamBigPicture"&&Single()=="steamQuickAccess","Double Steam menu save changed single");await Pick(_onexControllerKind,8);Check(Double()=="steamQuickAccess"&&Single()=="steamQuickAccess","Double Quick Access save changed single");
    await Input("b");await Source("A");Check(_onexControllerKind.Items.OfType<ComboBoxItem>().Skip(7).Take(2).All(item=>!item.IsEnabled),"Steam actions leaked to basic gamepad remapping");await Input("b");await Source("OEM:M1");Check(_onexControllerKind.SelectedIndex==8,"Steam binding did not survive closing/reopening editor");
    foreach(string key in new[]{"steamMenuSinglePersists","steamQuickSinglePersists","steamMenuDoublePersists","steamQuickDoublePersists","independentSingleDouble","oemOnly","helpAndSummary","reopenPreservesChoice"})report[key]=JsonValue.CreateBooleanValue(true);report["injectedGamepadSteps"]=JsonValue.CreateNumberValue(step);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["actualSteamExecutionByUiTest"]=JsonValue.CreateBooleanValue(false);report["productionCaptureEnabled"]=JsonValue.CreateBooleanValue(false);report["firmwareWritesByUiTest"]=JsonValue.CreateNumberValue(0);

   }
   finally
   {
    _onexControllerMappingFlyout?.Hide();if(ownId!=null){await ReadOnexControllerAsync();var own=_onexControllerState.GetNamedArray("entries").Select(value=>value.GetObject()).SingleOrDefault(value=>ControllerText(value,"id")==ownId);if(own!=null){Check(ControllerText(own,"name")==name,"Own QA identity changed; not deleting");await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(ownId)}});await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("automatic")}});}var marker=await folder.TryGetItemAsync("onex-controller-steam297-qa-owned.json");if(marker!=null)await marker.DeleteAsync();}if(_onexControllerOpen!=null)SetOnexControllerCard(null);if(original!=null)original.IsChecked=true;await Write("onex-gamepad-qa-state.json","{\"finished\":true}");
   }
  }
 }
}
