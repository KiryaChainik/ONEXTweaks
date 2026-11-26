// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexControllerLayoutAsync(JsonObject report)
  {
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   await FlushOnexControllerPolicyAsync();for(int n=0;n<100&&_onexControllerBusy;n++)await Task.Delay(30);Check(!_onexControllerBusy&&_onexControllerPending==null,"Protect pending layout edit");
   await ReadOnexControllerAsync();string before=ControllerEditorPolicy.Stringify();var tab=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);string open=_onexControllerOpen;bool guard=_onexControllerReadOnlyProjection;_onexControllerReadOnlyProjection=true;
   try{
    LegionNavItem.IsChecked=true;var state=JsonObject.Parse(_onexControllerState.Stringify());state["enabled"]=JsonValue.CreateBooleanValue(true);var policy=state.GetNamedObject("editor").GetNamedObject("policy");policy["useHardwareController"]=JsonValue.CreateBooleanValue(false);policy["gamepadRemappingDisabled"]=JsonValue.CreateBooleanValue(false);policy["bindings"]=new JsonObject();var tuning=policy.GetNamedObject("tuning",null)??new JsonObject();policy["tuning"]=tuning;tuning["nintendoLayout"]=JsonValue.CreateBooleanValue(true);tuning["leftDeadzone"]=JsonValue.CreateNumberValue(50);tuning["rightDeadzone"]=JsonValue.CreateNumberValue(45);
    ApplyOnexControllerState(state.Stringify());SetOnexControllerCard("sticks");UpdateLayout();await Task.Delay(120);
    Check(_onexControllerNintendo.IsOn&&_onexControllerNintendo.IsEnabled,"Nintendo toggle unavailable or wrong");Check(_onexControllerNintendo.Resources["ToggleSwitchFillOn"] is Windows.UI.Xaml.Media.SolidColorBrush fill&&fill.Color==OnexPalette.Cyan,"Nintendo accent differs from sticks card");
    Check(_onexControllerLeftDeadzone.Maximum==50&&_onexControllerRightDeadzone.Maximum==50&&_onexControllerLeftDeadzone.Value==50&&_onexControllerRightDeadzone.Value==45,"Physical stick deadzone range wrong");
    foreach(var pair in new[]{new[]{"A","B"},new[]{"B","A"},new[]{"X","Y"},new[]{"Y","X"}})Check(OnexControllerBindingSummary(pair[0])==pair[1]+" · Nintendo","Diagram summary does not show effective output");
    Check(OnexControllerRows().SelectMany(row=>row).Contains(_onexControllerNintendo),"Nintendo toggle missing from navigation");_onexControllerNintendo.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(70);Check(_onexControllerNintendo.Focus(FocusState.Keyboard),"Nintendo native focus failed");
    var bitmap=new Windows.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(_onexControllerNintendo);var pixels=await bitmap.GetPixelsAsync();var bytes=new byte[pixels.Length];using(var reader=Windows.Storage.Streams.DataReader.FromBuffer(pixels))reader.ReadBytes(bytes);int cyan=0,orange=0;for(int n=0;n+3<bytes.Length;n+=4){if(bytes[n+3]<200)continue;if(bytes[n]>150&&bytes[n+1]>150&&bytes[n+2]<80)cyan++;if(bytes[n]<100&&bytes[n+1]<130&&bytes[n+2]>160)orange++;}report["renderedSwitchCyanPixels"]=JsonValue.CreateNumberValue(cyan);report["renderedSwitchOrangePixels"]=JsonValue.CreateNumberValue(orange);await SaveOnexImage("onex-layout314-switch.png",_onexControllerNintendo,40);Check(cyan>20&&orange==0,"Rendered Nintendo switch still uses wrong accent");
    await SaveOnexImage("onex-layout312-controls.png",(FrameworkElement)_onexControllerSections.Single(section=>section.Id=="sticks").Detail.Child,80);
    policy["bindings"]=new JsonObject{{"A",new JsonObject{{"kind",JsonValue.CreateStringValue("gamepad")},{"target",JsonValue.CreateStringValue("X")}}}};ApplyOnexControllerState(state.Stringify());Check(OnexControllerBindingSummary("A")=="Y · Nintendo","Custom target summary order wrong");
    tuning["nintendoLayout"]=JsonValue.CreateBooleanValue(false);ApplyOnexControllerState(state.Stringify());Check(!_onexControllerNintendo.IsOn&&OnexControllerBindingSummary("A")=="X","Off did not restore target summary");
    tuning["nintendoLayout"]=JsonValue.CreateBooleanValue(true);policy["gamepadRemappingDisabled"]=JsonValue.CreateBooleanValue(true);ApplyOnexControllerState(state.Stringify());Check(_onexControllerNintendo.IsOn&&!_onexControllerNintendo.IsEnabled&&!OnexControllerNintendoActive,"Full Remapping Off changed stored setting or kept layout active");
    policy["gamepadRemappingDisabled"]=JsonValue.CreateBooleanValue(false);state["enabled"]=JsonValue.CreateBooleanValue(false);ApplyOnexControllerState(state.Stringify());Check(!_onexControllerNintendo.IsEnabled&&!OnexControllerNintendoActive,"Hardware mode offers ineffective layout");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["projectionOnly"]=JsonValue.CreateBooleanValue(true);report["settingsWrites"]=JsonValue.CreateNumberValue(0);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["gamepadEventsInjectedHere"]=JsonValue.CreateBooleanValue(false);
   }finally{_onexControllerReadOnlyProjection=guard;await ReadOnexControllerAsync();Check(ControllerEditorPolicy.Stringify()==before,"Actual layout profile changed during read-only test");SetOnexControllerCard(open);if(tab!=null)tab.IsChecked=true;}
  }
 }
}
