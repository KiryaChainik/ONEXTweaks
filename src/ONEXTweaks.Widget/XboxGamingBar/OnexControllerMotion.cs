// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Globalization;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private StackPanel _onexControllerMotionPanel;private TextBlock _onexControllerMotionStatus,_onexControllerMotionReading,_onexControllerMotionCorrection;
  private TextBlock _onexMotionActionHint;
  private ToggleSwitch _onexGyroNativeMotion;private TextBlock _onexGyroNativeMotionHint;
  private const string GyroNativeDs4Reason="Direct motion input requires DualShock 4. Choose DualShock 4 in Controller Mode settings. Stick / mouse gyro aiming above also works with Xbox 360.";
  private const string GyroNativeReadyReason="Sends rotation and acceleration to games or Steam Input that support motion controls. Independent of Use gyro as. Motion forwarding pauses while the widget is open.";
  private const string MotionBusyReason="A sensor check or calibration is running. Wait for it to finish before starting another check or resetting calibration.";
  private const string MotionResetReason="There is no saved calibration to reset. Run Calibrate gyro to save one.";
  private Button _onexControllerMotionCheck,_onexControllerMotionCalibrate,_onexControllerMotionReset;
  private DispatcherTimer _onexControllerMotionTimer;private bool _onexControllerMotionPolling;
  private JsonObject OnexMotionObject(JsonObject parent,string key){var value=parent?.GetNamedValue(key,JsonValue.CreateNullValue());return value?.ValueType==JsonValueType.Object?value.GetObject():null;}
  private Panel CreateOnexControllerMotionSection()
  {
   var panel=_onexControllerMotionPanel=new StackPanel{Name="OnexControllerMotionPanel",Spacing=12};
   _onexControllerGyroTest=CreateOnexControllerDiagnosticButton("gyro");panel.Children.Add(_onexControllerGyroTest);
   BuildOnexControllerGyro(panel);
   panel.Children.Add(new TextBlock{Text=OnexStrings.Get("Direct motion input · DS4 games"),FontSize=18,FontWeight=Windows.UI.Text.FontWeights.SemiBold});
   _onexGyroNativeMotion=new ToggleSwitch{Name="OnexGyroNativeMotion",Header=OnexStrings.Get("Forward sensors to games"),MinWidth=0,HorizontalAlignment=HorizontalAlignment.Stretch};
   OnexInputGuard.Wire(_onexGyroNativeMotion);OnexAccentResources(_onexGyroNativeMotion,OnexPalette.Magenta);_onexControllerControls.Add(_onexGyroNativeMotion);panel.Children.Add(_onexGyroNativeMotion);
   _onexGyroNativeMotion.Toggled+=(s,e)=>{if(!_onexControllerSync&&_onexGyroNativeMotion.IsEnabled&&OnexInputGuard.ConsumeToggleInput(_onexGyroNativeMotion))QueueOnexControllerNativeMotion(_onexGyroNativeMotion.IsOn);};
   _onexGyroNativeMotionHint=ControllerHelp("");panel.Children.Add(_onexGyroNativeMotionHint);
   var calibrationPanel=GyroSectionBody(panel,"calibration","Gyro calibration","Saved idle correction","Place the console on a steady surface for three seconds. Calibration removes idle rotation bias, not gravity. Accelerometer values can stay nonzero when tilted. The correction is shared by profiles.");
   _onexControllerMotionStatus=ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Check the motion sensors or calibrate idle drift"));calibrationPanel.Children.Add(_onexControllerMotionStatus);
   _onexControllerMotionReading=ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Last angular rate: —"));calibrationPanel.Children.Add(_onexControllerMotionReading);
   _onexControllerMotionCorrection=ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Idle correction: not calibrated"));calibrationPanel.Children.Add(_onexControllerMotionCorrection);
   calibrationPanel.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Lay the console on a steady surface, then choose Calibrate gyro. Keep it still for 3 seconds. Calibration removes idle drift: for example, a stationary 0.2°/s becomes 0°/s. The correction is shared by all controller profiles.")));
   calibrationPanel.Children.Add(ControllerHelp(OnexStrings.Get("Calibration corrects idle rotation drift, not gravity. Accelerometer axes change when the console tilts, including while you hold it still at an angle. This is expected.")));
   Button Action(string name,string caption,string operation){
    var button=new Button{Name=name,Content=OnexStrings.Get(caption),Style=(Style)Resources["StandaloneButtonStyle"],HorizontalAlignment=HorizontalAlignment.Stretch};
    _onexControllerControls.Add(button);button.Click+=async(s,e)=>await ControllerActionAsync(new JsonObject{{"operation",JsonValue.CreateStringValue(operation)}});return button;
   }
   _onexControllerMotionCheck=Action("OnexControllerMotionCheck",OnexStrings.Get("Check sensors"),"motionCheck");_onexControllerMotionCalibrate=Action("OnexControllerMotionCalibrate","Calibrate gyro","motionCalibrate");_onexControllerMotionReset=Action("OnexControllerMotionReset",OnexStrings.Get("Reset Calibration"),"motionReset");
   calibrationPanel.Children.Add(OnexSettingsColumns(_onexControllerMotionCheck,_onexControllerMotionCalibrate));calibrationPanel.Children.Add(_onexControllerMotionReset);
   _onexMotionActionHint=ControllerHelp("");calibrationPanel.Children.Add(_onexMotionActionHint);
   _onexControllerMotionTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(600)};
   _onexControllerMotionTimer.Tick+=async(s,e)=>{
    if(_onexControllerMotionPolling||_onexControllerBusy||_onexControllerReadOnlyProjection)return;
    _onexControllerMotionPolling=true;try{await ReadOnexControllerAsync();}catch{_onexControllerMotionTimer.Stop();}finally{_onexControllerMotionPolling=false;}
   };
   return panel;
  }
  private string MotionVectorText(JsonObject value)=>value==null?"—":"X "+value.GetNamedNumber("x",0).ToString("+0.000;-0.000;0.000",CultureInfo.InvariantCulture)+" · Y "+value.GetNamedNumber("y",0).ToString("+0.000;-0.000;0.000",CultureInfo.InvariantCulture)+" · Z "+value.GetNamedNumber("z",0).ToString("+0.000;-0.000;0.000",CultureInfo.InvariantCulture)+" °/s";
  private void ApplyOnexControllerMotion()
  {
   if(_onexControllerMotionPanel==null)return;var motion=OnexMotionObject(_onexControllerState,"motion");var calibration=OnexMotionObject(motion,"calibration");
   _onexControllerMotionStatus.Text=OnexStrings.Get(ControllerText(motion,"status","Check the motion sensors or calibrate idle drift"));
   _onexControllerMotionReading.Text=global::XboxGamingBar.OnexStrings.Format("Last angular rate: {0}",MotionVectorText(OnexMotionObject(motion,"angularVelocity")));
   _onexControllerMotionCorrection.Text=global::XboxGamingBar.OnexStrings.Format("Idle correction: {0}",(calibration==null?global::XboxGamingBar.OnexStrings.Get("not calibrated"):MotionVectorText(calibration)));
   bool busy=motion?.GetNamedBoolean("busy",false)==true;
   if(busy&&!_onexControllerReadOnlyProjection)_onexControllerMotionTimer.Start();else _onexControllerMotionTimer.Stop();
   RefreshOnexControllerMotionAvailability();
  }
  private void RefreshOnexGyroNativeMotionAvailability()
  {
   if(_onexGyroNativeMotion==null)return;
   bool ready=App.IsConnected&&_onexControllerState?.ContainsKey("entries")==true&&OnexControllerEditingVirtual;
   bool ds4=ControllerText(_onexControllerPending??ControllerEditorPolicy,"virtualTarget","Xbox360")=="DualShock4";
   bool supported=_onexControllerState?.GetNamedBoolean("ds4AuxiliarySupported",false)==true;
   bool was=_onexControllerSync;_onexControllerSync=true;
   try{_onexGyroNativeMotion.IsOn=(_onexControllerPending??ControllerEditorPolicy)?.GetNamedBoolean("forwardMotion",false)==true;}
   finally{_onexControllerSync=was;}
   _onexGyroNativeMotion.IsEnabled=ready&&ds4&&supported;
   _onexGyroNativeMotionHint.Text=!ready?OnexControllerSoftwareTuningLockReason():!ds4?OnexStrings.Get(GyroNativeDs4Reason):
    !supported?OnexStrings.Get("Motion forwarding is unavailable. Update the ONEXTweaks Bridge to enable this feature."):OnexStrings.Get(_onexGyroNativeMotion.IsOn?GyroNativeReadyReason:"Off · direct motion for DS4 games. Enable to see setup details.");
  }
  private void QueueOnexControllerNativeMotion(bool enabled)
  {
   QueueOnexControllerPolicy(policy=>policy["forwardMotion"]=JsonValue.CreateBooleanValue(enabled));
   bool value=(_onexControllerPending??ControllerEditorPolicy)?.GetNamedBoolean("forwardMotion",false)==true;
   bool was=_onexControllerSync;_onexControllerSync=true;
   try{if(_onexControllerForwardMotion!=null)_onexControllerForwardMotion.IsOn=value;if(_onexGyroNativeMotion!=null)_onexGyroNativeMotion.IsOn=value;}
   finally{_onexControllerSync=was;}
   RefreshOnexGyroNativeMotionAvailability();
  }
  private void RefreshOnexControllerMotionAvailability()
  {
   if(_onexControllerMotionCheck==null)return;
   var motion=OnexMotionObject(_onexControllerState,"motion");
   bool connected=App.IsConnected&&_onexControllerState?.ContainsKey("entries")==true;
   bool busy=motion?.GetNamedBoolean("busy",false)==true;
   bool calibrated=OnexMotionObject(motion,"calibration")!=null;
   _onexControllerMotionCheck.IsEnabled=connected&&!busy;
   _onexControllerMotionCalibrate.IsEnabled=connected&&!busy;
   _onexControllerMotionReset.IsEnabled=connected&&!busy&&calibrated;
   SetGyroHint(_onexMotionActionHint,!connected?OnexControllerSoftwareTuningLockReason():
    busy?OnexStrings.Get(MotionBusyReason):!calibrated?OnexStrings.Get(MotionResetReason):null);
   WireOnexControllerNavigation();
  }
  private string DescribeOnexControllerMotion(){
   var motion=OnexMotionObject(_onexControllerState,"motion");if(motion?.GetNamedBoolean("busy",false)==true)return global::XboxGamingBar.OnexStrings.Get("Measuring · keep still");
   if(DescribeOnexControllerGyro(_onexControllerPending??ControllerEditorPolicy)!=OnexStrings.Get("Off"))return DescribeOnexControllerGyro(_onexControllerPending??ControllerEditorPolicy);
   return OnexMotionObject(motion,"calibration")!=null?global::XboxGamingBar.OnexStrings.Get("Calibrated · idle drift"):motion?.GetNamedValue("gyroAvailable",JsonValue.CreateNullValue())?.ValueType==JsonValueType.Boolean&&motion.GetNamedBoolean("gyroAvailable")?global::XboxGamingBar.OnexStrings.Get("Windows sensor · not calibrated"):global::XboxGamingBar.OnexStrings.Get("Sensor check & calibration");
  }
 }
}
