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
  private async Task VerifyOnexControllerGyroAxesAsync(JsonObject report){
   int checks=0;void Check(bool v,string why){checks++;if(!v)throw new InvalidOperationException(why);}
   await FlushOnexControllerPolicyAsync();for(int n=0;n<100&&_onexControllerBusy;n++)await Task.Delay(30);Check(!_onexControllerBusy&&_onexControllerPending==null,"Protect pending owner settings");await ReadOnexControllerAsync();
   string before=ControllerEditorPolicy.Stringify(),open=_onexControllerOpen;var tab=GetVisibleNavigationItems().FirstOrDefault(n=>n.IsChecked==true);bool guard=_onexControllerReadOnlyProjection;
   try{
    _onexControllerReadOnlyProjection=true;LegionNavItem.IsChecked=true;
    var state=JsonObject.Parse(_onexControllerState.Stringify());state["enabled"]=JsonValue.CreateBooleanValue(true);var policy=state.GetNamedObject("editor").GetNamedObject("policy");policy["useHardwareController"]=JsonValue.CreateBooleanValue(false);
    var processing=new JsonObject{{"engine",JsonValue.CreateStringValue("Precision")},{"adaptiveSmoothing",JsonValue.CreateNumberValue(20)},{"precisionSmoothing",JsonValue.CreateNumberValue(73)},{"gateTenths",JsonValue.CreateNumberValue(15)},{"antiDeadzone",JsonValue.CreateNumberValue(20)}};
    var gyro=new JsonObject{{"target",JsonValue.CreateStringValue("Mouse")},{"processing",processing}};policy["gyro"]=gyro;ApplyOnexControllerState(state.Stringify());SetOnexControllerCard(null);SetOnexControllerCard("gyro");UpdateLayout();await Task.Delay(100);
    Check(_onexControllerGyroEngine.Items.OfType<ComboBoxItem>().Select(i=>(string)i.Tag).SequenceEqual(new[]{"Adaptive","Linear","Precision"}),"Engine list differs fromClaw UI");
    Check(GyroChoice(_onexControllerGyroEngine)=="Precision"&&_onexControllerGyroSmoothing.Value==73&&_onexControllerGyroSmoothing.IsEnabled,"Precision state did not project");
    Check(_onexControllerGyroSmoothing.StepFrequency==1&&_onexControllerGyroSmoothing.SmallChange==5,"Smoothing steps wrong");
    foreach(string engine in new[]{"Adaptive","Linear","Precision"}){
     processing["engine"]=JsonValue.CreateStringValue(engine);ApplyOnexControllerState(state.Stringify());
     Check(_onexControllerGyroSmoothing.Value==(engine=="Precision"?73:20),"Per-engine smoothing not retained");
     Check(_onexControllerGyroSmoothing.IsEnabled==(engine!="Linear"),"Linear smoothing enable state wrong");
    }
    Check(DescribeOnexControllerGyro(policy).Contains("Precision"),"Profile/card summary missing engine");
    OpenOnexGyroSection("processing");
    foreach(var control in new Control[]{_onexControllerGyroEngine,_onexControllerGyroSmoothing}){
     Check(OnexControllerRows().SelectMany(row=>row).Contains(control),"Processing control missing in navigation");
     control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(60);Check(control.Focus(FocusState.Keyboard),"Processing control cannot focus");
    }
    var bitmap=new Windows.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(_onexControllerGyroEngine);var pixels=await bitmap.GetPixelsAsync();var bytes=new byte[pixels.Length];using(var reader=Windows.Storage.Streams.DataReader.FromBuffer(pixels))reader.ReadBytes(bytes);int total=0,dark=0;for(int i=0;i+3<bytes.Length;i+=4){if(bytes[i+3]<200)continue;total++;if(bytes[i]<110&&bytes[i+1]<110&&bytes[i+2]<110)dark++;}Check(total>100&&dark>total/2,"Engine native chooser unreadable");
    _onexControllerGyroProcessingPanel.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(90);await SaveOnexImage("onex-gyro322-processing.png",_onexControllerGyroProcessingPanel,80);
    gyro.Remove("processing");ApplyOnexControllerState(state.Stringify());Check(GyroChoice(_onexControllerGyroEngine)=="Linear"&&!_onexControllerGyroSmoothing.IsEnabled,"Legacy profile did not stayLinear");
    state["enabled"]=JsonValue.CreateBooleanValue(false);ApplyOnexControllerState(state.Stringify());Check(!_onexControllerGyroEngine.IsEnabled&&_onexControllerGyroTest.IsEnabled,"Hardware mode shoulddisable processing butretain diagnostics");
    state["enabled"]=JsonValue.CreateBooleanValue(true);gyro["processing"]=processing;gyro["target"]=JsonValue.CreateStringValue("Mouse");ApplyOnexControllerState(state.Stringify());
    Check(_onexControllerGyroGate.Value==15&&_onexControllerGyroFloor.Value==20&&_onexControllerGyroGate.IsEnabled&&!_onexControllerGyroFloor.IsEnabled,"Mouse gate/floor projection wrong");
    gyro["target"]=JsonValue.CreateStringValue("RightStick");ApplyOnexControllerState(state.Stringify());Check(_onexControllerGyroFloor.IsEnabled,"Stick target floor not available");
    Check(_onexControllerGyroGate.SmallChange==1&&_onexControllerGyroFloor.SmallChange==5,"Gate/percentage navigation steps wrong");
    OpenOnexGyroSection("thresholds");
    foreach(var control in new Control[]{_onexControllerGyroGate,_onexControllerGyroFloor}){Check(OnexControllerRows().SelectMany(row=>row).Contains(control),"Threshold control missing in navigation");control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(60);Check(control.Focus(FocusState.Keyboard),"Threshold cannot focus");}
    _onexControllerGyroThresholdsPanel.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(90);await SaveOnexImage("onex-gyro323-thresholds.png",_onexControllerGyroThresholdsPanel,80);
    processing["worldSpace"]=JsonValue.CreateBooleanValue(true);processing["invertX"]=JsonValue.CreateBooleanValue(true);processing["outputRotation"]=JsonValue.CreateNumberValue(90);ApplyOnexControllerState(state.Stringify());
    Check(_onexControllerGyroWorld.IsChecked==true&&_onexControllerGyroInvertX.IsChecked==true&&_onexControllerGyroInvertY.IsChecked==false&&GyroChoice(_onexControllerGyroRotation)=="90","Axes state failed projection");
    OpenOnexGyroSection("axes");
    foreach(var control in new Control[]{_onexControllerGyroWorld,_onexControllerGyroInvertX,_onexControllerGyroInvertY,_onexControllerGyroRotation}){Check(control.IsEnabled&&OnexControllerRows().SelectMany(row=>row).Contains(control),"Axes control unavailable/missing fromnavigation");control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(60);Check(control.Focus(FocusState.Keyboard),"Axes focus failed");}
    _onexControllerGyroAxesPanel.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(90);await SaveOnexImage("onex-gyro325-axes.png",_onexControllerGyroAxesPanel,80);
    Check(_onexControllerPending==null,"Read-only projection queued owner settings");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["projectionOnly"]=JsonValue.CreateBooleanValue(true);report["profileWrites"]=JsonValue.CreateNumberValue(0);
   }finally{_onexControllerReadOnlyProjection=guard;await ReadOnexControllerAsync();Check(ControllerEditorPolicy.Stringify()==before,"Processing projection changed owner profile");SetOnexControllerCard(open);if(tab!=null)tab.IsChecked=true;}
  }
 }
}
