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
  private async Task VerifyOnexControllerMotionAsync(JsonObject report){
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   await FlushOnexControllerPolicyAsync();for(int n=0;n<100&&_onexControllerBusy;n++)await Task.Delay(30);Check(!_onexControllerBusy&&_onexControllerPending==null,"Protect pending controller edits");await ReadOnexControllerAsync();
   string before=ControllerEditorPolicy.Stringify(),open=_onexControllerOpen;var tab=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);bool guard=_onexControllerReadOnlyProjection;_onexControllerReadOnlyProjection=true;
   try{
    LegionNavItem.IsChecked=true;var state=JsonObject.Parse(_onexControllerState.Stringify());
    var calibration=new JsonObject{{"x",JsonValue.CreateNumberValue(.183)},{"y",JsonValue.CreateNumberValue(-.061)},{"z",JsonValue.CreateNumberValue(.122)},{"samples",JsonValue.CreateNumberValue(1)},{"method",JsonValue.CreateStringValue("OnChange")},{"accelerometerSamples",JsonValue.CreateNumberValue(192)}};
    var motion=new JsonObject{{"busy",JsonValue.CreateBooleanValue(false)},{"status",JsonValue.CreateStringValue("Idle drift calibration applied")},{"gyroAvailable",JsonValue.CreateBooleanValue(true)},{"angularVelocity",JsonObject.Parse(calibration.Stringify())},{"calibration",calibration}};
    state["motion"]=motion;ApplyOnexControllerState(state.Stringify());SetOnexControllerCard("gyro");UpdateLayout();await Task.Delay(120);
    Check(_onexControllerSections.Single(c=>c.Id=="gyro").Summary.Text.Contains("Calibrated"),"Gyro card summary missing");
    Check(_onexControllerMotionCorrection.Text.Contains("+0.183")&&_onexControllerMotionCorrection.Text.Contains("-0.061"),"Actual correction units/sign missing");
    Check(_onexControllerMotionReading.Text.Contains("°/s"),"Angular-rate units missing");
    Check(_onexControllerMotionCheck.IsEnabled&&_onexControllerMotionCalibrate.IsEnabled&&_onexControllerMotionReset.IsEnabled,"Ready source controls unavailable");
    foreach(var control in new[]{_onexControllerMotionCheck,_onexControllerMotionCalibrate,_onexControllerMotionReset}){
     Check(OnexControllerRows().SelectMany(row=>row).Contains(control),"Gyro control missing from navigation");control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(60);Check(control.Focus(FocusState.Keyboard),"Native gyro focus failed");Check(control.ActualWidth>80&&control.ActualHeight>=30,"Gyro button clipped or too small");
    }
    Check(ReferenceEquals(_onexControllerMotionCheck.XYFocusRight,_onexControllerMotionCalibrate)&&ReferenceEquals(_onexControllerMotionCheck.XYFocusDown,_onexControllerMotionReset),"Gyro directional navigation wrong");
    await SaveOnexImage("onex-motion318-card.png",_onexControllerMotionPanel,80);
    motion["busy"]=JsonValue.CreateBooleanValue(true);ApplyOnexControllerState(state.Stringify());Check(!_onexControllerMotionCheck.IsEnabled&&!_onexControllerMotionCalibrate.IsEnabled&&!_onexControllerMotionReset.IsEnabled,"Concurrent sensor edits still enabled");Check(!_onexControllerMotionTimer.IsEnabled,"Read-only UI projection started real polling");
    motion["busy"]=JsonValue.CreateBooleanValue(false);motion["status"]=JsonValue.CreateStringValue("Movement detected; keep the console still and repeat");ApplyOnexControllerState(state.Stringify());Check(_onexControllerMotionStatus.Text.Contains("Movement detected")&&_onexControllerMotionCorrection.Text.Contains("+0.183"),"Failure hides previous calibration");
    motion["calibration"]=JsonValue.CreateNullValue();ApplyOnexControllerState(state.Stringify());Check(!_onexControllerMotionReset.IsEnabled&&_onexControllerMotionCalibrate.IsEnabled,"No-calibration restore state wrong");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["projectionOnly"]=JsonValue.CreateBooleanValue(true);report["settingsWrites"]=JsonValue.CreateNumberValue(0);report["physicalSensorsInvokedHere"]=JsonValue.CreateBooleanValue(false);
   }finally{_onexControllerReadOnlyProjection=guard;await ReadOnexControllerAsync();Check(ControllerEditorPolicy.Stringify()==before,"Read-only gyro UI changed actual profile");SetOnexControllerCard(open);if(tab!=null)tab.IsChecked=true;}
  }
 }
}
