// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using Windows.Data.Json;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private Slider _onexControllerLeftTriggerStart,_onexControllerLeftTriggerEnd,_onexControllerRightTriggerStart,_onexControllerRightTriggerEnd;
  private CheckBox _onexControllerHairTriggers;
  private Windows.UI.Xaml.Controls.TextBlock _onexControllerTuningModeHint,_onexControllerTriggerRangeHint;
  private void BuildOnexControllerTriggers(StackPanel host,Func<Panel,string,string,string,int,int,Slider> percent,Func<string,string,CheckBox> flag)
  {
   _onexControllerTuningModeHint=ControllerHelp("");host.Children.Insert(0,_onexControllerTuningModeHint);
   host.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Trigger travel: Deadzone ignores the start of the press. Full press at sets where output reaches 100%. For example, 20–80% ignores the first 20% and reaches full output at 80%.")));
   var left=new StackPanel{Spacing=8};var right=new StackPanel{Spacing=8};
   _onexControllerLeftTriggerStart=percent(left,"OnexControllerLeftTriggerStart","LT · deadzone","leftTriggerStart",99,0);
   _onexControllerLeftTriggerEnd=percent(left,"OnexControllerLeftTriggerEnd",OnexStrings.Get("LT · full press at"),"leftTriggerEnd",100,1);
   _onexControllerRightTriggerStart=percent(right,"OnexControllerRightTriggerStart","RT · deadzone","rightTriggerStart",99,0);
   _onexControllerRightTriggerEnd=percent(right,"OnexControllerRightTriggerEnd",OnexStrings.Get("RT · full press at"),"rightTriggerEnd",100,1);
   host.Children.Add(OnexSettingsColumns(left,right));
   _onexControllerHairTriggers=flag("Hair Triggers","hairTriggers");host.Children.Add(_onexControllerHairTriggers);
   _onexControllerTriggerRangeHint=ControllerHelp("");host.Children.Add(_onexControllerTriggerRangeHint);
   host.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Hair Triggers: после мёртвой зоны сразу 100%, отпускание — 0%. Минимум 2% защищает от шума около нуля. Например, для стрельбы — короткое нажатие; для плавного газа в гонках оставь Off. Значения Full press at сохраняются и возвращаются после Off.")));
  }
  private void ApplyOnexControllerTriggers(JsonObject tuning)
  {
   void Range(Slider start,Slider end,string prefix){
    double a=tuning?.GetNamedNumber(prefix+"TriggerStart",0)??0,b=tuning?.GetNamedNumber(prefix+"TriggerEnd",100)??100;
    // Reset limits before projecting another profile, so old limits cannot clamp its values.
    start.Maximum=99;end.Minimum=1;
    if(!OnexInputGuard.IsEditing(start))start.Value=a;if(!OnexInputGuard.IsEditing(end))end.Value=b;
    if(!OnexInputGuard.IsEditing(start))start.Maximum=end.Value-1;if(!OnexInputGuard.IsEditing(end))end.Minimum=start.Value+1;
   }
   Range(_onexControllerLeftTriggerStart,_onexControllerLeftTriggerEnd,"left");Range(_onexControllerRightTriggerStart,_onexControllerRightTriggerEnd,"right");
   _onexControllerHairTriggers.IsChecked=tuning?.GetNamedBoolean("hairTriggers",false)==true;
  }
  private string OnexControllerSoftwareTuningLockReason()
  {
   if(!App.IsConnected)return OnexStrings.Get("Waiting for the controller helper to reconnect. Reopen ONEXTweaks if the connection does not return.");
   if(_onexControllerState?.ContainsKey("entries")!=true)return OnexStrings.Get("Controller profiles are loading. These settings will unlock when loading finishes.");
   if(_onexControllerState.GetNamedBoolean("enabled",false)&&(_onexControllerPending??ControllerEditorPolicy)?.GetNamedBoolean("useHardwareController",false)==true)
    return OnexStrings.Get("This profile uses Hardware Controller. Turn off Use Hardware Controller for this Profile to enable software stick, gyro and trigger settings. Saved values are kept.");
   if(_onexControllerState.GetNamedString("inputMode","gamepad")=="keyboardMouse")
    return OnexStrings.Get("Keyboard & mouse uses its own settings. Switch to Gamepad mode and select Virtual Controller under Controller Mode to edit this gamepad profile's stick, gyro and trigger output.");
   return OnexStrings.Get("Hardware Controller sends the original input, so software stick, gyro and trigger settings are locked. Set Controller Mode to Virtual Controller to enable them. Saved values are kept.");
  }
  private void RefreshOnexControllerTriggerAvailability()
  {
   if(_onexControllerHairTriggers==null)return;
   bool ready=App.IsConnected&&_onexControllerState?.ContainsKey("entries")==true;
   bool enabled=ready&&OnexControllerEditingVirtual;
   if(_onexControllerTuningModeHint!=null){
    _onexControllerTuningModeHint.Text=enabled
     ?global::XboxGamingBar.OnexStrings.Get("Virtual Controller: saved stick and trigger tuning is applied in games. The test shows physical input and the configured output.")
     :OnexControllerSoftwareTuningLockReason();
    _onexControllerTuningModeHint.Visibility=Windows.UI.Xaml.Visibility.Visible;
   }
   foreach(var control in new Windows.UI.Xaml.Controls.Control[]{_onexControllerLeftDeadzone,_onexControllerRightDeadzone,_onexControllerThreshold,_onexControllerInvertLx,_onexControllerInvertLy,_onexControllerInvertRx,_onexControllerInvertRy,_onexControllerSwap})
    if(control!=null)control.IsEnabled=enabled;
   foreach(var slider in new[]{_onexControllerLeftTriggerStart,_onexControllerRightTriggerStart})slider.IsEnabled=enabled;
   foreach(var slider in new[]{_onexControllerLeftTriggerEnd,_onexControllerRightTriggerEnd})slider.IsEnabled=enabled&&_onexControllerHairTriggers.IsChecked!=true;
   _onexControllerHairTriggers.IsEnabled=enabled;
   if(_onexControllerTriggerRangeHint!=null){
    _onexControllerTriggerRangeHint.Text=OnexStrings.Get("Full press at is locked while Hair Triggers is on: output jumps to 100% after the deadzone. Turn Hair Triggers off to adjust analogue travel.");
    _onexControllerTriggerRangeHint.Visibility=enabled&&_onexControllerHairTriggers.IsChecked==true?Windows.UI.Xaml.Visibility.Visible:Windows.UI.Xaml.Visibility.Collapsed;
   }
  }
  private string DescribeOnexControllerTriggers(JsonObject tuning)=>global::XboxGamingBar.OnexStrings.Format("\nTriggers LT: {0}–{1}% · RT: {2}–{3}%{4}{5}",(tuning?.GetNamedNumber("leftTriggerStart",0)??0),(tuning?.GetNamedNumber("leftTriggerEnd",100)??100),(tuning?.GetNamedNumber("rightTriggerStart",0)??0),(tuning?.GetNamedNumber("rightTriggerEnd",100)??100),(tuning?.GetNamedBoolean("hairTriggers",false)==true?global::XboxGamingBar.OnexStrings.Get(" · Hair On"):""),(tuning?.GetNamedBoolean("nintendoLayout",false)==true?global::XboxGamingBar.OnexStrings.Get(" · Nintendo"):""));
  private IEnumerable<Control[]> OnexControllerTriggerRows()
  {
   yield return new Control[]{_onexControllerLeftTriggerStart,_onexControllerRightTriggerStart};
   if(_onexControllerHairTriggers.IsChecked!=true)yield return new Control[]{_onexControllerLeftTriggerEnd,_onexControllerRightTriggerEnd};
   yield return new Control[]{_onexControllerHairTriggers};
  }
 }
}
