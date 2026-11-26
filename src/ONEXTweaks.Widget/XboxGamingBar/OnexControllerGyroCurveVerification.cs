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
  private async Task VerifyOnexControllerGyroCurveAsync(JsonObject report){
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   await FlushOnexControllerPolicyAsync();for(int n=0;n<100&&_onexControllerBusy;n++)await Task.Delay(30);Check(!_onexControllerBusy&&_onexControllerPending==null,"Protect pending owner settings");await ReadOnexControllerAsync();
   string before=ControllerEditorPolicy.Stringify(),open=_onexControllerOpen;var tab=GetVisibleNavigationItems().FirstOrDefault(n=>n.IsChecked==true);bool guard=_onexControllerReadOnlyProjection;
   try{
    _onexControllerReadOnlyProjection=true;LegionNavItem.IsChecked=true;var state=JsonObject.Parse(_onexControllerState.Stringify());state["enabled"]=JsonValue.CreateBooleanValue(true);var policy=state.GetNamedObject("editor").GetNamedObject("policy");policy["useHardwareController"]=JsonValue.CreateBooleanValue(false);
    var curve=new JsonObject{{"enabled",JsonValue.CreateBooleanValue(true)}};foreach(var item in OnexGyroCurveDefaults)curve[item.Key]=JsonValue.CreateNumberValue(item.Value);
    curve["power"]=JsonValue.CreateNumberValue(200);curve["maxOutput"]=JsonValue.CreateNumberValue(70);curve["outputMix"]=JsonValue.CreateNumberValue(-25);
    var processing=new JsonObject{{"engine",JsonValue.CreateStringValue("Linear")},{"antiDeadzone",JsonValue.CreateNumberValue(20)},{"stickCurve",curve}};var gyro=new JsonObject{{"target",JsonValue.CreateStringValue("RightStick")},{"processing",processing}};policy["gyro"]=gyro;
    ApplyOnexControllerState(state.Stringify());SetOnexControllerCard(null);SetOnexControllerCard("gyro");UpdateLayout();await Task.Delay(100);
    Check(_onexControllerGyroCurveEnable.IsChecked==true&&_onexControllerGyroCurveEnable.IsEnabled&&_onexControllerGyroCurveDetails.Visibility==Visibility.Visible,"Custom curve visibility/availability wrong");
    Check(_onexControllerGyroCurveSliders.Count==9,"Curve does not expose all nine pinned controls");Check(!_onexControllerGyroFloor.IsEnabled&&_onexControllerGyroFloor.Value==20,"Custom curve did not preserve/replace legacy floor");
    OpenOnexGyroSection("curve");
    foreach(var item in OnexGyroCurveDefaults){var slider=_onexControllerGyroCurveSliders[item.Key];Check(slider.Value==curve.GetNamedNumber(item.Key)&&slider.IsEnabled,"Curve field failed projection: "+item.Key);Check(slider.StepFrequency==1&&slider.SmallChange==(item.Key=="deadzone"||item.Key=="minInput"||item.Key=="precisionSpeed"?1:5),"Curve touch/gamepad step wrong: "+item.Key);Check(OnexControllerRows().SelectMany(r=>r).Contains(slider),"Curve control missing in navigation");slider.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(45);Check(slider.Focus(FocusState.Keyboard),"Curve control cannot focus");}
    Check(_onexControllerGyroCurveSliders["maxInput"].Minimum==50&&_onexControllerGyroCurveSliders["maxInput"].Maximum==720&&_onexControllerGyroCurveSliders["outputMix"].Minimum==-100,"Pinned curve ranges wrong");Check(_onexControllerGyroCurveReset.IsEnabled&&OnexControllerRows().SelectMany(r=>r).Contains(_onexControllerGyroCurveReset),"Scoped curve reset unavailable");
    _onexControllerGyroCurvePanel.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(90);await SaveOnexImage("onex-gyro331-curve.png",_onexControllerGyroCurvePanel,80);
    curve["enabled"]=JsonValue.CreateBooleanValue(false);ApplyOnexControllerState(state.Stringify());Check(_onexControllerGyroCurveDetails.Visibility==Visibility.Collapsed&&_onexControllerGyroFloor.IsEnabled,"Legacy response was not restored when curve disabled");Check(!OnexControllerRows().SelectMany(r=>r).Contains(_onexControllerGyroCurveSliders["power"])&&_onexControllerGyroCurveSliders["power"].Value==200,"Hidden curve retained focus or lost saved settings");
    curve["enabled"]=JsonValue.CreateBooleanValue(true);gyro["target"]=JsonValue.CreateStringValue("Mouse");ApplyOnexControllerState(state.Stringify());Check(!_onexControllerGyroCurveEnable.IsEnabled&&_onexControllerGyroCurveSliders.Values.All(s=>!s.IsEnabled)&&!_onexControllerGyroFloor.IsEnabled,"Mouse target allowed stick-only controls");
    gyro["target"]=JsonValue.CreateStringValue("LeftStick");ApplyOnexControllerState(state.Stringify());Check(_onexControllerGyroCurveEnable.IsEnabled,"Left stick cannot use curve");
    processing.Remove("stickCurve");ApplyOnexControllerState(state.Stringify());Check(_onexControllerGyroCurveEnable.IsChecked!=true&&_onexControllerGyroCurveDetails.Visibility==Visibility.Collapsed&&_onexControllerGyroFloor.IsEnabled,"Legacy profile gained a custom curve");
    state["enabled"]=JsonValue.CreateBooleanValue(false);ApplyOnexControllerState(state.Stringify());Check(!_onexControllerGyroCurveEnable.IsEnabled&&_onexControllerGyroTest.IsEnabled,"Hardware mode did not retain independent sensor diagnostics");Check(_onexControllerPending==null,"Read-only curve projection queued owner settings");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["projectionOnly"]=JsonValue.CreateBooleanValue(true);report["profileWrites"]=JsonValue.CreateNumberValue(0);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
   }finally{_onexControllerReadOnlyProjection=guard;await ReadOnexControllerAsync();Check(ControllerEditorPolicy.Stringify()==before,"Curve projection changed owner policy");SetOnexControllerCard(open);if(tab!=null)tab.IsChecked=true;}
  }
 }
}
