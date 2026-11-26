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
  private async Task VerifyOnexControllerTriggersAsync(JsonObject report)
  {
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   await FlushOnexControllerPolicyAsync();for(int n=0;n<100&&_onexControllerBusy;n++)await Task.Delay(30);Check(!_onexControllerBusy&&_onexControllerPending==null,"Protect pending profile changes");
   var tab=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);string open=_onexControllerOpen;
   await ReadOnexControllerAsync();string before=ControllerEditorPolicy.Stringify();bool guard=_onexControllerReadOnlyProjection;_onexControllerReadOnlyProjection=true;
   try{
    LegionNavItem.IsChecked=true;var state=JsonObject.Parse(_onexControllerState.Stringify());state["enabled"]=JsonValue.CreateBooleanValue(true);var policy=state.GetNamedObject("editor").GetNamedObject("policy");policy["useHardwareController"]=JsonValue.CreateBooleanValue(false);var tuning=policy.GetNamedObject("tuning",null)??new JsonObject();policy["tuning"]=tuning;tuning["leftTriggerStart"]=JsonValue.CreateNumberValue(10);tuning["leftTriggerEnd"]=JsonValue.CreateNumberValue(60);tuning["rightTriggerStart"]=JsonValue.CreateNumberValue(20);tuning["rightTriggerEnd"]=JsonValue.CreateNumberValue(80);tuning["hairTriggers"]=JsonValue.CreateBooleanValue(false);
    ApplyOnexControllerState(state.Stringify());SetOnexControllerCard("sticks");UpdateLayout();await Task.Delay(120);
    Check(_onexControllerLeftTriggerStart.Value==10&&_onexControllerLeftTriggerEnd.Value==60&&_onexControllerRightTriggerStart.Value==20&&_onexControllerRightTriggerEnd.Value==80,"Independent trigger values not projected");
    Check(_onexControllerLeftTriggerStart.Maximum==59&&_onexControllerLeftTriggerEnd.Minimum==11&&_onexControllerRightTriggerStart.Maximum==79&&_onexControllerRightTriggerEnd.Minimum==21,"Paired limits can cross");
    foreach(var slider in new[]{_onexControllerLeftTriggerStart,_onexControllerLeftTriggerEnd,_onexControllerRightTriggerStart,_onexControllerRightTriggerEnd}){
     Check(slider.IsEnabled&&slider.StepFrequency==1&&slider.SmallChange==5,"Trigger availability/1%touch/5%gamepad configuration wrong");
     Check(OnexControllerRows().SelectMany(row=>row).Contains(slider),"Trigger absent from navigation");
     slider.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(70);Check(slider.Focus(FocusState.Keyboard),"Trigger native focus failed");
     Check(slider.ActualWidth>100&&slider.ActualHeight>0,"Trigger native geometry missing");
    }
    await SaveOnexImage("onex-triggers311-range.png",(FrameworkElement)_onexControllerSections.Single(section=>section.Id=="sticks").Detail.Child,80);
    tuning["hairTriggers"]=JsonValue.CreateBooleanValue(true);ApplyOnexControllerState(state.Stringify());await Task.Delay(60);
    Check(_onexControllerHairTriggers.IsChecked==true&&_onexControllerHairTriggers.IsEnabled,"Hair mode not displayed");
    Check(_onexControllerTriggerRangeHint.Visibility==Visibility.Visible,"Hair mode lock does not explain how to restore analogue endpoints");
    Check(!_onexControllerLeftTriggerEnd.IsEnabled&&!_onexControllerRightTriggerEnd.IsEnabled&&_onexControllerLeftTriggerStart.IsEnabled&&_onexControllerRightTriggerStart.IsEnabled,"Hair mode disabled wrong fields");
    Check(!OnexControllerRows().SelectMany(row=>row).Contains(_onexControllerLeftTriggerEnd)&&!OnexControllerRows().SelectMany(row=>row).Contains(_onexControllerRightTriggerEnd),"Disabled endpoints remain in navigation");
    Check(_onexControllerLeftTriggerEnd.Value==60&&_onexControllerRightTriggerEnd.Value==80,"Hair mode changed stored endpoint projection");
    tuning["hairTriggers"]=JsonValue.CreateBooleanValue(false);ApplyOnexControllerState(state.Stringify());Check(_onexControllerLeftTriggerEnd.IsEnabled&&_onexControllerLeftTriggerEnd.Value==60&&_onexControllerRightTriggerEnd.IsEnabled&&_onexControllerRightTriggerEnd.Value==80,"Hair Off lost endpoint or availability");
    state["enabled"]=JsonValue.CreateBooleanValue(false);ApplyOnexControllerState(state.Stringify());Check(!_onexControllerHairTriggers.IsEnabled&&!_onexControllerLeftTriggerStart.IsEnabled&&!_onexControllerRightTriggerStart.IsEnabled&&!_onexControllerLeftTriggerEnd.IsEnabled&&!_onexControllerRightTriggerEnd.IsEnabled,"Hardware mode exposes software trigger tuning");
    Check(!_onexControllerLeftDeadzone.IsEnabled&&!_onexControllerRightDeadzone.IsEnabled&&!_onexControllerThreshold.IsEnabled&&!_onexControllerInvertLx.IsEnabled&&!_onexControllerInvertLy.IsEnabled&&!_onexControllerInvertRx.IsEnabled&&!_onexControllerInvertRy.IsEnabled&&!_onexControllerSwap.IsEnabled,"Hardware mode exposes software stick/activation tuning");
    Check(_onexControllerTuningModeHint!=null&&_onexControllerTuningModeHint.Visibility==Visibility.Visible,"Hardware locks do not explain how to enable Virtual Controller");
    Check(_onexControllerState.GetNamedBoolean("enabled")==false,"Availability changed the selected Hardware mode");
    state["enabled"]=JsonValue.CreateBooleanValue(true);policy["useHardwareController"]=JsonValue.CreateBooleanValue(true);ApplyOnexControllerState(state.Stringify());
    Check(!_onexControllerLeftDeadzone.IsEnabled&&!_onexControllerLeftTriggerStart.IsEnabled&&_onexControllerTuningModeHint.Text==OnexControllerSoftwareTuningLockReason(),"Per-profile Hardware override exposes software tuning or the wrong unlock action");
    policy["useHardwareController"]=JsonValue.CreateBooleanValue(false);ApplyOnexControllerState(state.Stringify());
    Check(_onexControllerLeftDeadzone.IsEnabled&&_onexControllerLeftTriggerStart.IsEnabled&&_onexControllerLeftTriggerEnd.IsEnabled&&_onexControllerLeftTriggerStart.Value==10&&_onexControllerLeftTriggerEnd.Value==60&&_onexControllerRightTriggerStart.Value==20&&_onexControllerRightTriggerEnd.Value==80,"Unlocking Virtual Controller lost tuning values or left the controls locked");
    Check(OnexControllerRows().SelectMany(row=>row).Contains(_onexControllerSticksReset),"Existing scoped Restore Default missing");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["projectionOnly"]=JsonValue.CreateBooleanValue(true);report["settingsWrites"]=JsonValue.CreateNumberValue(0);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["gamepadEventsInjectedHere"]=JsonValue.CreateBooleanValue(false);
   }finally{
    _onexControllerReadOnlyProjection=guard;await ReadOnexControllerAsync();Check(ControllerEditorPolicy.Stringify()==before,"Actual profile changed during read-only trigger UI test");SetOnexControllerCard(open);if(tab!=null)tab.IsChecked=true;
   }
  }
 }
}
