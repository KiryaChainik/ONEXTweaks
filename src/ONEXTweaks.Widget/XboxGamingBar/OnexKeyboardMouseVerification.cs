// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexKeyboardMouseAsync(JsonObject report)
  {
   void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
   var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");int step=0;var originalTab=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);JsonObject originalPolicy=null;string expected=null;int expectedRevision=0;string originalGeneration=null;bool originalMode=false;
   async Task Write(string name,string value){for(int n=0;;n++){try{File.WriteAllText(Path.Combine(folder.Path,name),value);return;}catch(IOException)when(n<25){await Task.Delay(20);}}}
   async Task Input(string button)
   {var target=FocusManager.GetFocusedElement() as FrameworkElement;Check(target!=null,"KBM input target missing");var peer=FrameworkElementAutomationPeer.FromElement(target)??FrameworkElementAutomationPeer.CreatePeerForElement(target);var r=peer.GetBoundingRectangle();double scale=Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;string id=run+":"+(++step);await Write("onex-gamepad-qa-state.json",new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(button)},{"x",JsonValue.CreateNumberValue(r.X+r.Width/2+widget.WindowBounds.X*scale)},{"y",JsonValue.CreateNumberValue(r.Y+r.Height/2+widget.WindowBounds.Y*scale)}}.Stringify());bool ack=false;for(int n=0;n<120;n++){await Task.Delay(60);try{ack=await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id;}catch{}if(ack)break;}Check(ack,"KBM injected input timeout");await Task.Delay(130);}
   async Task Focus(Control value){value.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(140);Check(value.Focus(FocusState.Keyboard),"KBM focus rejected: "+value.Name);await Task.Delay(40);}
   async Task Saved(){for(int n=0;n<200&&(_kbmBusy||_kbmTuning.Count>0);n++)await Task.Delay(40);Check(!_kbmBusy&&_kbmTuning.Count==0,"KBM save did not settle");expected=_kbmLastWritePolicy;expectedRevision=_kbmLastWriteRevision;await ReadOnexKeyboardMouseAsync();Check(KbmPolicy.Stringify()==expected&&(int)_kbmState.GetNamedNumber("revision")==expectedRevision,"Concurrent keyboard/mouse edit detected");await Write("onex-keyboard298-qa-owned.json",new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"original",originalPolicy},{"expected",JsonValue.CreateStringValue(expected)}}.Stringify());}
   async Task Pick(ComboBox choice,int index,bool keepSelection=true){await Focus(choice);int before=choice.SelectedIndex;await Input("a");Check(choice.IsDropDownOpen,"KBM chooser did not open");int count=index>=before?Enumerable.Range(before+1,index-before).Count(n=>((ComboBoxItem)choice.Items[n]).IsEnabled):Enumerable.Range(index,before-index).Count(n=>((ComboBoxItem)choice.Items[n]).IsEnabled);for(int n=0;n<count;n++)await Input(index>=before?"down":"up");await Input("a");await Saved();Check(!keepSelection||choice.SelectedIndex==index,"KBM chooser selected wrong action");}
   try
   {
    EnsureOnexKeyboardMouseFrontend();_onexKeyboardMouseNav.IsChecked=true;await ReadOnexKeyboardMouseAsync();Check(KbmPolicy!=null,"KBM policy unavailable");originalMode=ControllerText(_kbmState,"mode")=="keyboardMouse";Check(!originalMode,"Protect owner active keyboard/mouse mode during UI probe");originalPolicy=JsonObject.Parse(KbmPolicy.Stringify());originalGeneration=ControllerText(_kbmState,"generation");expected=originalPolicy.Stringify();expectedRevision=(int)_kbmState.GetNamedNumber("revision");_kbmLastWritePolicy=expected;_kbmLastWriteRevision=expectedRevision;await Write("onex-keyboard298-qa-owned.json",new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"original",originalPolicy},{"expected",JsonValue.CreateStringValue(expected)}}.Stringify());
    var navigation=GetVisibleNavigationItems();Check(navigation.FindIndex(nav=>ReferenceEquals(nav,_onexKeyboardMouseNav))==navigation.FindIndex(nav=>ReferenceEquals(nav,LegionNavItem))+1&&navigation.Count==OnexTabIds.Length,"Keyboard/mouse tab order wrong");_kbmSync=true;try{BuildKbmEditor();}finally{_kbmSync=false;}SetKbmSettingsCard(_kbmSections[0]);Check(_kbmTiles.Count==30&&_kbmDiagramKeys.Count==30&&_kbmBaseRows.SelectMany(row=>row).Count()==30,"Shared 30-source diagram incomplete");Check(_kbmAction.Items.OfType<ComboBoxItem>().Where(item=>item.Tag is JsonObject).All(item=>ControllerText((JsonObject)item.Tag,"kind")!="gamepad"),"Gamepad actions leaked into keyboard/mouse chooser");
    double before=KbmPolicy.GetNamedNumber("mouseSpeed");await Focus(_kbmMouseSpeed);await Input("a");await Input(before>=300?"left":"right");Check(KbmPolicy.GetNamedNumber("mouseSpeed")==before,"Speed preview saved before A");await Input("b");Check(_kbmMouseSpeed.Value==before,"B did not cancel speed");await Input("a");await Input(before>=300?"left":"right");await Input("a");await Saved();Check(Math.Abs(KbmPolicy.GetNamedNumber("mouseSpeed")-before)==5&&ReferenceEquals(FocusManager.GetFocusedElement(),_kbmMouseSpeed),"A/5%/focus speed commit wrong");await Input("a");await Input(before>=295?"left":"right");await Input("down");await Saved();Check(ReferenceEquals(FocusManager.GetFocusedElement(),_kbmAcceleration),"Vertical slider commit did not move to next row");
    SetKbmSettingsCard(_kbmSections[2]);
    int fnIndex=_kbmFn.Items.OfType<ComboBoxItem>().Select((item,index)=>new{item,index}).Single(value=>(string)value.item.Tag=="OEM:M1").index;await Pick(_kbmFn,fnIndex);Check(ControllerText(KbmPolicy,"fnButton")=="OEM:M1"&&_kbmDiagrams.Count==1,"Fn modifier did not save or caused a second diagram");
    await Focus(_kbmTiles["base:A"]);await Input("a");Check(_kbmFlyout.IsOpen&&_kbmLayer=="base"&&_kbmSource=="A","Shared button editor not opened");await Focus(_kbmFnLayerButton);await Input("a");Check(_kbmFlyout.IsOpen&&_kbmLayer=="fn","Fn did not open inside the same editor");string baseA=KbmEffective("A","base").Stringify();int copy=_kbmAction.Items.OfType<ComboBoxItem>().Select((item,index)=>new{item,index}).Single(value=>value.item.Content as string=="Copy · Ctrl+C").index;await Pick(_kbmAction,copy,false);Check(KbmSummary(KbmEffective("A","fn"))==KbmSummary(KbmBinding("keyboard","",17,67))&&KbmEffective("A","base").Stringify()==baseA,"FN edit overwrote base layer");Check(_kbmKeys.Count==5,"Five-key custom editor missing");await Input("b");
    await SaveOnexImage("onex-keyboard298-base-diagram.png",_kbmDiagrams["base"],100);
    foreach(string key in new[]{"tabAfterController","one30SourceDiagram","keyboardMouseOnlyActions","sliderPreviewCancel","sliderAcommit5percent","sliderVerticalCommit","sameWindowFnLayer","fnIndependentBinding","fiveCustomKeysAvailable"})report[key]=JsonValue.CreateBooleanValue(true);report["injectedGamepadSteps"]=JsonValue.CreateNumberValue(step);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["actualModeCaptureTestedHere"]=JsonValue.CreateBooleanValue(false);
   }
   finally
   {
    _kbmFlyout?.Hide();if(originalPolicy!=null){await ReadOnexKeyboardMouseAsync();Check(ControllerText(_kbmState,"generation")==originalGeneration&&KbmPolicy.Stringify()==expected&&(int)_kbmState.GetNamedNumber("revision")==expectedRevision,"Owner keyboard/mouse settings changed; stale QA restore refused");await KbmChange(new JsonObject{{"operation",JsonValue.CreateStringValue("edit")},{"policy",originalPolicy},{"expectedRevision",JsonValue.CreateNumberValue(expectedRevision)}});Check(KbmPolicy.Stringify()==originalPolicy.Stringify(),"Original keyboard/mouse settings not restored");var marker=await folder.TryGetItemAsync("onex-keyboard298-qa-owned.json");if(marker!=null)await marker.DeleteAsync();}if(originalTab!=null)originalTab.IsChecked=true;await Write("onex-gamepad-qa-state.json","{\"finished\":true}");
   }
  }
  private async Task VerifyOnexKeyboardMouseModeAsync(JsonObject report)
  {
   void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
   var originalTab=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);EnsureOnexKeyboardMouseFrontend();await ReadOnexControllerAsync();await ReadOnexKeyboardMouseAsync();bool wasActive=ControllerText(_kbmState,"mode")=="keyboardMouse";Check(!wasActive&&!_onexControllerState.GetNamedBoolean("enabled",false),"Protect owner active capture");string selected=ControllerEditorId,policy=ControllerEditorPolicy.Stringify();
   try
   {
    _onexKeyboardMouseNav.IsChecked=true;await KbmChange(new JsonObject{{"operation",JsonValue.CreateStringValue("mode")},{"mode",JsonValue.CreateStringValue("keyboardMouse")}});for(int n=0;n<120&&!_kbmState.GetNamedObject("runtime").GetNamedBoolean("running",false);n++){await Task.Delay(100);await ReadOnexKeyboardMouseAsync();}Check(ControllerText(_kbmState,"mode")=="keyboardMouse"&&_kbmState.GetNamedObject("runtime").GetNamedBoolean("running",false),"Real keyboard/mouse capture did not start");Check(ControllerEditorId==selected&&ControllerEditorPolicy.Stringify()==policy,"Mode activation modified gamepad profile");await Task.Delay(1500);await KbmChange(new JsonObject{{"operation",JsonValue.CreateStringValue("mode")},{"mode",JsonValue.CreateStringValue("gamepad")}});Check(ControllerText(_kbmState,"mode")=="gamepad"&&!_kbmState.GetNamedObject("runtime").GetNamedBoolean("running",false)&&ControllerEditorId==selected&&ControllerEditorPolicy.Stringify()==policy,"Gamepad mode/profile not restored");report["actualCaptureStartStop"]=JsonValue.CreateBooleanValue(true);report["gamepadProfilePreserved"]=JsonValue.CreateBooleanValue(true);report["controllerPhysicalInputRequired"]=JsonValue.CreateBooleanValue(false);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["evidenceKind"]=JsonValue.CreateStringValue("Real app-service mode commands and real VIIPER/HidHide capture; no injected physical controller input");
   }
   finally{await ReadOnexKeyboardMouseAsync();if(ControllerText(_kbmState,"mode")=="keyboardMouse")await KbmChange(new JsonObject{{"operation",JsonValue.CreateStringValue("mode")},{"mode",JsonValue.CreateStringValue("gamepad")}});if(originalTab!=null)originalTab.IsChecked=true;}
  }
 }
}
