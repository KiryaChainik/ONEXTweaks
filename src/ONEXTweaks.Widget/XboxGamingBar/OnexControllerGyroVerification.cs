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

  private void VerifyOnexGyroAvailabilityReasons(JsonObject report)
  {
   int checks=0;
   void Check(bool valid,string message){checks++;if(!valid)throw new InvalidOperationException(message);}
   bool Shows(TextBlock hint,string key)=>hint.Visibility==Visibility.Visible&&hint.Text.Contains(OnexStrings.Get(key));
   string original=_onexControllerState.Stringify(),open=_onexControllerOpen;
   bool previous=_onexControllerReadOnlyProjection;_onexControllerReadOnlyProjection=true;
   try
   {
    var state=JsonObject.Parse(original);
    var policy=state.GetNamedObject("editor").GetNamedObject("policy");
    var gyro=new JsonObject();var processing=new JsonObject();var curve=new JsonObject();
    var motion=new JsonObject();processing["stickCurve"]=curve;gyro["processing"]=processing;policy["gyro"]=gyro;state["motion"]=motion;
    policy["useHardwareController"]=JsonValue.CreateBooleanValue(false);
    void Text(JsonObject parent,string key,string value){parent[key]=JsonValue.CreateStringValue(value);}
    void Flag(JsonObject parent,string key,bool value){parent[key]=JsonValue.CreateBooleanValue(value);}
    void Project(){ApplyOnexControllerState(state.Stringify());}
    SetOnexControllerCard(null);SetOnexControllerCard("gyro");
    Flag(state,"enabled",false);Text(gyro,"target","RightStick");Project();
    Check(!_onexControllerGyroTarget.IsEnabled&&_onexControllerGyroModeHint.Text==OnexControllerSoftwareTuningLockReason(),"Hardware mode is missing its gyro unlock instruction");
    Check(!_onexGyroNativeMotion.IsEnabled,"Hardware mode exposes native motion forwarding");
    Check(_onexControllerGyroTest.IsEnabled&&_onexControllerMotionCheck.IsEnabled&&_onexControllerMotionCalibrate.IsEnabled,"Hardware mode blocked an independent sensor test");
    Check(!_onexControllerMotionReset.IsEnabled&&Shows(_onexMotionActionHint,MotionResetReason),"Missing calibration has no reset explanation");
    Flag(state,"enabled",true);Text(gyro,"target","Disabled");Project();
    Check(_onexControllerGyroTarget.IsEnabled&&_onexControllerGyroX.IsEnabled&&Shows(_onexControllerGyroModeHint,GyroTargetReason),"Off target unnecessarily locks profile editing");
    Flag(state,"ds4AuxiliarySupported",true);Text(gyro,"activation","Hold");Text(gyro,"boostButton","LT");Text(processing,"engine","Adaptive");
    foreach(string output in new[]{"Xbox360","DualShock4"})
    {
     Text(policy,"virtualTarget",output);Flag(policy,"forwardMotion",true);Project();
     foreach(var control in new Control[]{_onexControllerGyroTarget,_onexControllerGyroEngine,_onexControllerGyroSmoothing,_onexControllerGyroX,_onexControllerGyroY,_onexControllerGyroButton,_onexControllerGyroActivation,_onexControllerGyroBoost,_onexControllerGyroGate,_onexControllerGyroFloor,_onexControllerGyroWorld,_onexControllerGyroInvertX,_onexControllerGyroInvertY,_onexControllerGyroRotation,_onexControllerGyroConversion,_onexControllerGyroCurveEnable})
      Check(control.IsEnabled,"Off aiming locks a supported control in "+output+": "+control.Name);
     Check(GyroChoice(_onexControllerGyroTarget)=="Disabled","Editing enabled gyro output automatically");
     Check(_onexGyroNativeMotion.IsEnabled==(output=="DualShock4")&&_onexControllerForwardMotion.IsEnabled==_onexGyroNativeMotion.IsEnabled,"Motion forwarding capability differs between its two views");
     Check(_onexGyroNativeMotion.IsOn&&_onexControllerForwardMotion.IsOn,"Stored motion forwarding is not synchronized");
     if(output=="Xbox360")Check(_onexGyroNativeMotionHint.Text==OnexStrings.Get(GyroNativeDs4Reason),"Xbox360 motion forwarding lacks the DS4 unlock instruction");
     Flag(policy,"forwardMotion",false);Project();Check(!_onexGyroNativeMotion.IsOn&&!_onexControllerForwardMotion.IsOn,"Forwarding Off is not synchronized");
    }
    Text(gyro,"target","Mouse");Text(gyro,"activation","Always");Text(gyro,"boostButton","None");
    Text(processing,"engine","Linear");Flag(state,"oemEnabled",false);Project();
    Check(!_onexControllerGyroButton.IsEnabled&&Shows(_onexGyroButtonHint,GyroAlwaysReason),"Always activation has no button explanation");
    Check(!_onexControllerGyroBoost.IsEnabled&&Shows(_onexGyroButtonHint,GyroBoostReason),"Missing boost button has no sensitivity explanation");
    Check(_onexControllerGyroBoostButton.Items.OfType<ComboBoxItem>().Where(item=>((string)item.Tag).StartsWith("OEM:")).All(item=>!item.IsEnabled)&&Shows(_onexGyroButtonHint,GyroOemReason),"Disabled rear buttons lack an unlock instruction");
    Check(!_onexControllerGyroSmoothing.IsEnabled&&Shows(_onexControllerGyroEngineHint,GyroLinearReason),"Linear smoothing has no engine explanation");
    Check(!_onexControllerGyroConversion.IsEnabled&&!_onexControllerGyroFloor.IsEnabled&&!_onexControllerGyroCurveEnable.IsEnabled&&Shows(_onexGyroStickHint,GyroMouseReason),"Mouse target lacks a stick-only settings explanation");
    Text(gyro,"target","RightStick");Text(gyro,"activation","Hold");Text(gyro,"boostButton","LT");Text(processing,"engine","Adaptive");
    Flag(state,"oemEnabled",true);Flag(processing,"worldSpace",true);Flag(curve,"enabled",true);Project();
    Check(_onexControllerGyroSmoothing.IsEnabled&&_onexControllerGyroButton.IsEnabled&&_onexControllerGyroBoost.IsEnabled&&_onexGyroButtonHint.Visibility==Visibility.Collapsed,"Available settings retain their old lock messages");
    Check(!_onexControllerGyroConversion.IsEnabled&&Shows(_onexGyroConversionHint,GyroWorldReason),"Gravity-relative aiming lacks an axis conversion explanation");
    Check(!_onexControllerGyroFloor.IsEnabled&&Shows(_onexGyroStickHint,GyroCurveFloorReason),"Custom curve lacks its minimum-output explanation");
    Flag(processing,"worldSpace",false);Flag(curve,"enabled",false);Flag(motion,"busy",true);Project();
    Check(_onexControllerGyroConversion.IsEnabled&&_onexControllerGyroFloor.IsEnabled&&_onexGyroConversionHint.Visibility==Visibility.Collapsed&&_onexGyroStickHint.Visibility==Visibility.Collapsed,"Resolved stick settings retain a lock message");
    Check(!_onexControllerMotionCheck.IsEnabled&&!_onexControllerMotionCalibrate.IsEnabled&&!_onexControllerMotionReset.IsEnabled&&Shows(_onexMotionActionHint,MotionBusyReason),"Concurrent sensor action lacks a wait instruction");
    Flag(motion,"busy",false);motion["calibration"]=new JsonObject();Project();
    Check(_onexControllerMotionCheck.IsEnabled&&_onexControllerMotionCalibrate.IsEnabled&&_onexControllerMotionReset.IsEnabled&&_onexMotionActionHint.Visibility==Visibility.Collapsed,"Ready calibration actions retain an obsolete lock");
    Check(_onexControllerPending==null&&!_onexControllerMotionTimer.IsEnabled,"Availability projection started a write or sensor operation");
    report["editableWhileAimingOffBothVirtualOutputs"]=JsonValue.CreateBooleanValue(true);
    report["nativeMotionViewsSynchronizedAndDS4Gated"]=JsonValue.CreateBooleanValue(true);
    report["projectionOnly"]=JsonValue.CreateBooleanValue(true);report["checks"]=JsonValue.CreateNumberValue(checks);
   }
   finally
   {
    try{ApplyOnexControllerState(original);SetOnexControllerCard(null);if(open!=null)SetOnexControllerCard(open);}
    finally{_onexControllerReadOnlyProjection=previous;}
   }
  }

  private async Task VerifyOnexGyroEditableOffUiAsync(JsonObject report)
  {
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   string original=_onexControllerState.Stringify(),open=_onexControllerOpen;
   bool previous=_onexControllerReadOnlyProjection;_onexControllerReadOnlyProjection=true;
   try
   {
    var state=JsonObject.Parse(original);state["enabled"]=JsonValue.CreateBooleanValue(true);state["ds4AuxiliarySupported"]=JsonValue.CreateBooleanValue(true);
    var policy=state.GetNamedObject("editor").GetNamedObject("policy");policy["useHardwareController"]=JsonValue.CreateBooleanValue(false);
    policy["gyro"]=new JsonObject{{"target",JsonValue.CreateStringValue("Disabled")}};
    foreach(string output in new[]{"Xbox360","DualShock4"})
    {
     policy["virtualTarget"]=JsonValue.CreateStringValue(output);ApplyOnexControllerState(state.Stringify());
     SetOnexControllerCard(null);SetOnexControllerCard("gyro");OpenOnexGyroSection(null);UpdateLayout();
     Check(_onexGyroSections.Count==6&&_onexGyroSections.All(section=>section.Body.Visibility==Visibility.Collapsed),"Inactive details are not compact");
     Check(_onexGyroSections.All(section=>OnexControllerRows().SelectMany(row=>row).Contains(section.Header)&&OnexControllerRows().SelectMany(row=>row).Contains(section.Help)),"Section headers or contextual help cannot be reached");
     Check(!OnexControllerRows().SelectMany(row=>row).Contains(_onexControllerGyroRotation)&&!OnexControllerRows().SelectMany(row=>row).Contains(_onexControllerMotionCalibrate),"Hidden gyro details leaked into navigation");
     _onexControllerGyroTarget.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(80);await SaveOnexImage("onex-gyro1079-compact-"+output+".png",this,80);
     OpenOnexGyroSection("axes");_onexControllerGyroAxesPanel.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);
     Check(_onexControllerGyroRotation.IsEnabled&&_onexControllerGyroRotation.Focus(FocusState.Keyboard),"Off aiming rotation chooser cannot be focused in "+output);
     _onexControllerGyroRotation.IsDropDownOpen=true;await Task.Delay(100);
     Check(_onexControllerGyroRotation.IsDropDownOpen&&_onexControllerGyroRotation.Items.Count==4,"Off aiming rotation chooser cannot open in "+output);
     _onexControllerGyroRotation.IsDropDownOpen=false;await Task.Delay(80);
     await SaveOnexImage("onex-gyro1079-"+output+"-off-axes-projection.png",this,80);
     _onexControllerGyroTarget.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(80);
     await SaveOnexImage("onex-gyro1079-"+output+"-off-header-projection.png",this,80);
     _onexGyroNativeMotion.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(80);
     await SaveOnexImage("onex-gyro1079-"+output+"-motion-projection.png",this,80);
     Check(GyroChoice(_onexControllerGyroTarget)=="Disabled"&&_onexControllerPending==null,"Readonly off-state interaction enabled output or queued settings");
    }
    report["projectionOnly"]=JsonValue.CreateBooleanValue(true);report["checks"]=JsonValue.CreateNumberValue(checks);report["profileWrites"]=JsonValue.CreateNumberValue(0);
   }
   finally
   {
    _onexControllerGyroRotation.IsDropDownOpen=false;
    try{ApplyOnexControllerState(original);SetOnexControllerCard(null);if(open!=null)SetOnexControllerCard(open);}
    finally{_onexControllerReadOnlyProjection=previous;}
   }
  }

  private async Task VerifyOnexControllerGyroAsync(JsonObject report){
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   await FlushOnexControllerPolicyAsync();for(int n=0;n<100&&_onexControllerBusy;n++)await Task.Delay(30);Check(!_onexControllerBusy&&_onexControllerPending==null,"Protect owner pending gyro settings");await ReadOnexControllerAsync();
   string before=ControllerEditorPolicy.Stringify(),open=_onexControllerOpen;var tab=GetVisibleNavigationItems().FirstOrDefault(n=>n.IsChecked==true);bool guard=_onexControllerReadOnlyProjection;_onexControllerReadOnlyProjection=true;
   try{
    LegionNavItem.IsChecked=true;var state=JsonObject.Parse(_onexControllerState.Stringify());state["enabled"]=JsonValue.CreateBooleanValue(true);state["oemEnabled"]=JsonValue.CreateBooleanValue(true);
    var policy=state.GetNamedObject("editor").GetNamedObject("policy");policy["useHardwareController"]=JsonValue.CreateBooleanValue(false);
    var gyro=new JsonObject{{"target",JsonValue.CreateStringValue("Mouse")},{"activation",JsonValue.CreateStringValue("Toggle")},{"activationButton",JsonValue.CreateStringValue("LB")},{"sensitivityX",JsonValue.CreateNumberValue(80)},{"sensitivityY",JsonValue.CreateNumberValue(65)},{"boostButton",JsonValue.CreateStringValue("LT")},{"boostPercent",JsonValue.CreateNumberValue(50)}};policy["gyro"]=gyro;
    ApplyOnexControllerState(state.Stringify());if(_onexControllerOpen=="gyro")SetOnexControllerCard(null);SetOnexControllerCard("gyro");UpdateLayout();await Task.Delay(120);
    Check(_onexControllerGyroTarget.Items.OfType<ComboBoxItem>().Select(i=>(string)i.Tag).SequenceEqual(new[]{"Disabled","LeftStick","RightStick","Mouse"}),"Claw gyro target list mismatch");
    Check(GyroChoice(_onexControllerGyroTarget)=="Mouse"&&GyroChoice(_onexControllerGyroButton)=="LB"&&GyroChoice(_onexControllerGyroActivation)=="Toggle","Gyro target/button/mode projection mismatch");
    Check(_onexControllerGyroX.Value==80&&_onexControllerGyroY.Value==65&&_onexControllerGyroBoost.Value==50&&GyroChoice(_onexControllerGyroBoostButton)=="LT","Sensitivity/boost projection mismatch");
    Check(DescribeOnexControllerGyro(policy).Contains("Toggle LB")&&DescribeOnexControllerPolicy(policy).Contains("Gyro: Mouse"),"Saved/profile/card gyro summary missing");
    foreach(var slider in new[]{_onexControllerGyroX,_onexControllerGyroY,_onexControllerGyroBoost})Check(slider.StepFrequency==1&&slider.SmallChange==5,"Gyro percentage step contract changed");
    foreach(var control in new Control[]{_onexControllerGyroTarget,_onexControllerGyroButton,_onexControllerGyroActivation,_onexControllerGyroBoostButton,_onexControllerGyroX,_onexControllerGyroY,_onexControllerGyroBoost,_onexControllerGyroReset,_onexControllerMotionCheck,_onexControllerMotionCalibrate}){
     OpenOnexGyroSection(control==_onexControllerMotionCheck||control==_onexControllerMotionCalibrate?"calibration":"aim");Check(control.IsEnabled&&OnexControllerRows().SelectMany(row=>row).Contains(control),"Visible gyro control missing/disabled in navigation");control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(45);Check(control.Focus(FocusState.Keyboard),"Native gyro focus failed");
    }
    OpenOnexGyroSection("aim");
    foreach(var choice in new[]{_onexControllerGyroTarget,_onexControllerGyroButton,_onexControllerGyroActivation,_onexControllerGyroBoostButton}){
     var bitmap=new Windows.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(choice);var pixels=await bitmap.GetPixelsAsync();var bytes=new byte[pixels.Length];using(var reader=Windows.Storage.Streams.DataReader.FromBuffer(pixels))reader.ReadBytes(bytes);int dark=0,total=0;for(int n=0;n+3<bytes.Length;n+=4){if(bytes[n+3]<200)continue;total++;if(bytes[n]<110&&bytes[n+1]<110&&bytes[n+2]<110)dark++;}Check(total>100&&dark>total/2,"Gyro native chooser is white/unreadable");
    }
    await SaveOnexImage("onex-gyro319-controls.png",_onexControllerGyroPanel,80);
    gyro["boostButton"]=JsonValue.CreateStringValue("None");gyro["activation"]=JsonValue.CreateStringValue("Always");ApplyOnexControllerState(state.Stringify());Check(!_onexControllerGyroBoost.IsEnabled&&!_onexControllerGyroButton.IsEnabled&&_onexControllerGyroActivation.IsEnabled,"None/Always enable states inconsistent");
    state["oemEnabled"]=JsonValue.CreateBooleanValue(false);ApplyOnexControllerState(state.Stringify());Check(_onexControllerGyroButton.Items.OfType<ComboBoxItem>().Where(i=>((string)i.Tag).StartsWith("OEM:")).All(i=>!i.IsEnabled),"Unprepared rear gyro triggers still offered");
    state["enabled"]=JsonValue.CreateBooleanValue(false);ApplyOnexControllerState(state.Stringify());Check(!_onexControllerGyroTarget.IsEnabled&&_onexControllerMotionCheck.IsEnabled&&_onexControllerMotionCalibrate.IsEnabled,"Hardware mode aiming/calibration availability wrong");
    Check(!_onexControllerMotionTimer.IsEnabled&&_onexControllerPending==null,"Projection started real gyro mutation/polling");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["projectionOnly"]=JsonValue.CreateBooleanValue(true);report["settingsWrites"]=JsonValue.CreateNumberValue(0);report["physicalGyroInvokedHere"]=JsonValue.CreateBooleanValue(false);
   }finally{_onexControllerReadOnlyProjection=guard;await ReadOnexControllerAsync();Check(ControllerEditorPolicy.Stringify()==before,"Read-only gyro UI changed owner profile");SetOnexControllerCard(open);if(tab!=null)tab.IsChecked=true;}
  }
 }
}
