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
  private async Task VerifyOnexControllerGyroConversionAsync(JsonObject report){
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   await FlushOnexControllerPolicyAsync();for(int n=0;n<100&&_onexControllerBusy;n++)await Task.Delay(30);Check(!_onexControllerBusy&&_onexControllerPending==null,"Protect pending owner changes");await ReadOnexControllerAsync();string before=ControllerEditorPolicy.Stringify(),open=_onexControllerOpen;var tab=GetVisibleNavigationItems().FirstOrDefault(n=>n.IsChecked==true);bool guard=_onexControllerReadOnlyProjection;
   try{
    _onexControllerReadOnlyProjection=true;LegionNavItem.IsChecked=true;var state=JsonObject.Parse(_onexControllerState.Stringify());state["enabled"]=JsonValue.CreateBooleanValue(true);var policy=state.GetNamedObject("editor").GetNamedObject("policy");policy["useHardwareController"]=JsonValue.CreateBooleanValue(false);var processing=new JsonObject{{"engine",JsonValue.CreateStringValue("Linear")}};var gyro=new JsonObject{{"target",JsonValue.CreateStringValue("RightStick")},{"processing",processing}};policy["gyro"]=gyro;
    ApplyOnexControllerState(state.Stringify());SetOnexControllerCard(null);SetOnexControllerCard("gyro");UpdateLayout();await Task.Delay(100);
    Check(_onexControllerGyroConversion.Items.OfType<ComboBoxItem>().Select(i=>(string)i.Tag).SequenceEqual(new[]{"Yaw","Roll","YawRoll"}),"Input conversion choices differ from pinned frontend");Check(GyroChoice(_onexControllerGyroConversion)=="Yaw"&&_onexControllerGyroConversion.IsEnabled,"Legacy conversion default is not Yaw");
    foreach(string source in new[]{"Yaw","Roll","YawRoll"}){processing["stickConversion"]=JsonValue.CreateStringValue(source);ApplyOnexControllerState(state.Stringify());Check(GyroChoice(_onexControllerGyroConversion)==source,"Saved source not projected");Check(OnexControllerRows().SelectMany(r=>r).Contains(_onexControllerGyroConversion),"Source chooser absent from navigation");_onexControllerGyroConversion.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(60);Check(_onexControllerGyroConversion.Focus(FocusState.Keyboard),"Source chooser cannot focus");}
    Check(DescribeOnexControllerGyro(policy).Contains("Yaw + Roll"),"Profile summary omits selected source");
    _onexControllerGyroAxesPanel.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(90);await SaveOnexImage("onex-gyro332-conversion.png",_onexControllerGyroAxesPanel,80);
    processing["worldSpace"]=JsonValue.CreateBooleanValue(true);ApplyOnexControllerState(state.Stringify());Check(!_onexControllerGyroConversion.IsEnabled&&GyroChoice(_onexControllerGyroConversion)=="YawRoll"&&_onexControllerGyroRotation.IsEnabled,"Gravity frame did not retain/disable dormant local source");processing["worldSpace"]=JsonValue.CreateBooleanValue(false);ApplyOnexControllerState(state.Stringify());Check(_onexControllerGyroConversion.IsEnabled&&GyroChoice(_onexControllerGyroConversion)=="YawRoll","Stored source did not return from gravity mode");
    gyro["target"]=JsonValue.CreateStringValue("Mouse");ApplyOnexControllerState(state.Stringify());Check(!_onexControllerGyroConversion.IsEnabled&&_onexControllerGyroWorld.IsEnabled,"Mouse should keep existing axes but not local stick source");
    gyro["target"]=JsonValue.CreateStringValue("LeftStick");ApplyOnexControllerState(state.Stringify());Check(_onexControllerGyroConversion.IsEnabled,"Left stick cannot select source");processing.Remove("stickConversion");ApplyOnexControllerState(state.Stringify());Check(GyroChoice(_onexControllerGyroConversion)=="Yaw","Absent source did not preserve old Yaw");
    state["enabled"]=JsonValue.CreateBooleanValue(false);ApplyOnexControllerState(state.Stringify());Check(!_onexControllerGyroConversion.IsEnabled&&_onexControllerGyroTest.IsEnabled,"Hardware must keep diagnostics independent");Check(_onexControllerPending==null,"Read-only projection queued owner writes");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["projectionOnly"]=JsonValue.CreateBooleanValue(true);report["profileWrites"]=JsonValue.CreateNumberValue(0);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
   }finally{_onexControllerReadOnlyProjection=guard;await ReadOnexControllerAsync();Check(ControllerEditorPolicy.Stringify()==before,"Projection changed owner profile");SetOnexControllerCard(open);if(tab!=null)tab.IsChecked=true;}
  }
 }
}
