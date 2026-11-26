// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexGyroDashboardAsync(JsonObject report)
  {
   int checks=0;void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
   var pose=new OnexGyroPose();
   Vector3[] axes={Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ};
   foreach(var axis in axes)
   {
    foreach(int sign in new[]{1,-1})
    {
     pose.Recenter();
     for(int n=1;n<=120;n++)
     {
      pose.Integrate(axis*sign*100,.01);
      if(n==45||n==89||n==90||n==91||n==120)
       Check(Vector3.Distance(pose.RotationDegrees,axis*(sign*n))<.01,"One-axis rotation leaked into another axis or flipped at 90°");
     }
    }
   }
   pose.Recenter();
   Check(pose.Orientation==Quaternion.Identity,"Recenter did not reset the presentation pose");
   pose.Integrate(Vector3.UnitX*90,1);
   Check(pose.Orientation==Quaternion.Identity,"Pose integrated across a missing interval");
   Vector3 gravity=new Vector3(.4f,.3f,(float)Math.Sqrt(.75));
   for(int n=0;n<100;n++)pose.CorrectGravity(gravity,.01);
   Check(pose.RotationDegrees.Length()<.001,"A steady angled console acquired false rotation");
   var chassisCases=new[]{
    (Sensor:-Vector3.UnitY,Chassis:Vector3.UnitX,Action:"top edge lift"),
    (Sensor:Vector3.UnitX,Chassis:-Vector3.UnitY,Action:"right edge lift"),
    (Sensor:Vector3.UnitZ,Chassis:-Vector3.UnitZ,Action:"face-up clockwise turn")
   };
   foreach(var motionCase in chassisCases)
   foreach(int direction in new[]{1,-1})
   {
    pose.Recenter();
    for(int n=0;n<=30;n++)
    {
     var physical=Quaternion.CreateFromAxisAngle(motionCase.Chassis,direction*n*(float)(Math.PI/180));
     Vector3 actualGravity=Vector3.Transform(-Vector3.UnitZ,Quaternion.Conjugate(physical));
     pose.Observe(actualGravity,motionCase.Sensor*direction*100,n,0,n*.01);
    }
    Check(Vector3.Distance(pose.RotationDegrees,motionCase.Chassis*direction*30)<.01,"Wrong chassis rotation for "+motionCase.Action);
   }
   pose.Recenter();
   pose.Observe(-Vector3.UnitY,-Vector3.UnitX*90,1,0,0);
   pose.Observe(-Vector3.UnitY,-Vector3.UnitX*90,2,0,.05);
   Quaternion before=pose.Orientation;
   for(int n=2;n<=9;n++)pose.Observe(-Vector3.UnitY,-Vector3.UnitX*90,2,(n-1)*50,n*.05);
   Check(pose.Orientation==before,"Repeated gyro values kept rotating the model");
   pose.Pause();
   pose.Observe(-Vector3.UnitY,-Vector3.UnitX*90,50,0,5);
   Check(pose.Orientation==before,"First sample after a stale gap invented motion");
   var dashboard=new OnexGyroDashboard();var dialog=CreateOnexControllerDiagnosticDialog("gyro",dashboard.Panel);
   var shown=dialog.ShowAsync();
   try
   {
    for(int n=0;n<60&&!dashboard.ArtworkReady;n++)await Task.Delay(25);Check(dashboard.ArtworkReady,"Textured 3D console did not render");
    var state=JsonObject.Parse(@"{""active"":true,""available"":true,""kind"":""gyro"",""motion"":{""sequence"":7,""gyroAgeMilliseconds"":0,""accelerationAgeMilliseconds"":0,""angular"":{""x"":90,""y"":-45,""z"":180},""acceleration"":{""x"":0.2,""y"":-0.3,""z"":0.9}},""gyroPreview"":{""tick"":42,""sensorSequence"":7,""raw"":{""x"":90,""y"":-45,""z"":180},""calibrated"":{""x"":89.8,""y"":-44.9,""z"":180},""axes"":{""x"":44.9,""y"":89.8},""filtered"":{""x"":20,""y"":60},""output"":{""x"":-32768,""y"":0},""outputUnit"":""stickCounts"",""stage"":""Output"",""active"":true,""virtualMode"":true,""calibrationApplied"":true,""gain"":{""x"":1,""y"":1},""settings"":{""target"":""RightStick"",""activation"":""Always"",""activationButton"":""None"",""processing"":{""engine"":""Adaptive"",""gateTenths"":10,""stickConversion"":""Yaw"",""antiDeadzone"":10}}}}");
    dashboard.ApplySample(state);Check(dashboard.HasSample&&dashboard.AxisValue(0)=="+44.90"&&dashboard.AxisValue(2)=="+20.00","Resolved and filtered rates were conflated");
    Check(dashboard.AxisValue(4)=="-100.00%"&&dashboard.AxisValue(5)=="0.00%","Saturated stick output lost its exact endpoint");
    await SaveOnexImage("onex-gyro1081-dashboard-projection.png",dashboard.Panel,80);
    var preview=state.GetNamedObject("gyroPreview");preview["output"]=JsonValue.CreateNullValue();preview["axes"]=JsonValue.CreateNullValue();preview["filtered"]=JsonValue.CreateNullValue();preview["stage"]=JsonValue.CreateStringValue("WaitingForActivation");dashboard.ApplySample(state);
    Check(dashboard.HasSample&&dashboard.AxisValue(0)=="—"&&dashboard.AxisValue(4)=="—","Inactive stages displayed invented zero input");
    preview["stage"]=JsonValue.CreateStringValue("GateClosed");preview["output"]=new JsonObject{{"x",JsonValue.CreateNumberValue(0)},{"y",JsonValue.CreateNumberValue(0)}};dashboard.ApplySample(state);Check(dashboard.AxisValue(4)=="0.00%","An actual gate rejection did not show zero output");
    var settings=preview.GetNamedObject("settings");settings["target"]=JsonValue.CreateStringValue("Mouse");settings["processing"]=new JsonObject{{"stickConversion",JsonValue.CreateStringValue("Roll")},{"stickCurve",new JsonObject{{"enabled",JsonValue.CreateBooleanValue(true)}}}};preview["outputUnit"]=JsonValue.CreateStringValue("mouseCountsPerTick");preview["output"]=new JsonObject{{"x",JsonValue.CreateNumberValue(2)},{"y",JsonValue.CreateNumberValue(-1)}};dashboard.ApplySample(state);
    Check(dashboard.AxisValue(4)=="+2 Δ"&&dashboard.Effect("axes").Contains(OnexStrings.Get("Yaw"))&&!dashboard.Effect("gate").Contains(OnexStrings.Get("Custom curve")),"Mouse preview claimed stick-only processing");
    state.GetNamedObject("motion")["gyroAgeMilliseconds"]=JsonValue.CreateNumberValue(501);preview["stage"]=JsonValue.CreateStringValue("SensorStale");preview["output"]=JsonValue.CreateNullValue();dashboard.ApplySample(state);Check(dashboard.HasSample&&!dashboard.Fresh&&dashboard.StageText.Contains(OnexStrings.Get("Held sensor values"))&&dashboard.AxisValue(4)=="—","Held sensor values were presented as fresh output");
    dashboard.Recenter();
    var motion=state.GetNamedObject("motion");
    motion["gyroAgeMilliseconds"]=JsonValue.CreateNumberValue(0);
    motion["acceleration"]=new JsonObject{{"x",JsonValue.CreateNumberValue(0)},{"y",JsonValue.CreateNumberValue(-1)},{"z",JsonValue.CreateNumberValue(0)}};
    preview["calibrated"]=new JsonObject{{"x",JsonValue.CreateNumberValue(-90)},{"y",JsonValue.CreateNumberValue(0)},{"z",JsonValue.CreateNumberValue(0)}};
    int previousFrames=dashboard.ModelFrames;
    for(int n=0;n<=26;n++)
    {
     motion["sequence"]=JsonValue.CreateNumberValue(20+n);
     dashboard.ApplySample(state,10+n*.05);
    }
    var expected=Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)(117*Math.PI/180));
    for(int n=0;n<40&&Math.Abs(Quaternion.Dot(dashboard.PresentedPose,expected))<.9999;n++)await Task.Delay(25);
    Check(dashboard.ModelFrames>previousFrames&&Math.Abs(Quaternion.Dot(dashboard.PresentedPose,expected))>=.9999,"Native 3D view did not follow an isolated 117° Y rotation");
    Check(Math.Abs(dashboard.PresentedPose.X)<.0001&&Math.Abs(dashboard.PresentedPose.Z)<.0001,"Native Y presentation introduced a diagonal rotation");
    report["pureYFrames"]=JsonValue.CreateNumberValue(dashboard.ModelFrames-previousFrames);
    report["modelRenderMilliseconds"]=JsonValue.CreateNumberValue(dashboard.ModelRenderMilliseconds);
    await SaveOnexImage("onex-gyro1081-y117-projection.png",dashboard.Panel,80);
    dashboard.Recenter();
    motion["gyroAgeMilliseconds"]=JsonValue.CreateNumberValue(800);
    motion["acceleration"]=new JsonObject{{"x",JsonValue.CreateNumberValue(0)},{"y",JsonValue.CreateNumberValue(0)},{"z",JsonValue.CreateNumberValue(-1)}};
    dashboard.ApplySample(state,20);
    motion["acceleration"]=new JsonObject{{"x",JsonValue.CreateNumberValue(0)},{"y",JsonValue.CreateNumberValue(-.5)},{"z",JsonValue.CreateNumberValue(-Math.Sqrt(.75))}};
    for(int n=1;n<=120;n++)dashboard.ApplySample(state,20+n*.05);
    var expectedTilt=Quaternion.CreateFromAxisAngle(Vector3.UnitX,(float)(Math.PI/6));
    for(int n=0;n<40&&Math.Abs(Quaternion.Dot(dashboard.PresentedPose,expectedTilt))<.9999;n++)await Task.Delay(25);
    Check(Math.Abs(Quaternion.Dot(dashboard.PresentedPose,expectedTilt))>=.9999,"A held gyroscope prevented fresh gravity from establishing the forward/backward tilt");
    Check(Math.Abs(dashboard.PresentedPose.Y)<.0001&&Math.Abs(dashboard.PresentedPose.Z)<.0001,"A held angular rate rotated the model around another axis");
    previousFrames=dashboard.ModelFrames;
    dashboard.ClearSample();
    for(int n=0;n<40&&dashboard.ModelFrames<=previousFrames;n++)await Task.Delay(25);
    Check(!dashboard.HasSample&&dashboard.ModelFrames>previousFrames&&dashboard.PresentedPose==Quaternion.Identity,"Unavailable data retained the previous 3D pose");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["syntheticUiProjectionOnly"]=JsonValue.CreateBooleanValue(true);report["profileWrites"]=JsonValue.CreateNumberValue(0);report["actualMotionAcceptance"]=JsonValue.CreateBooleanValue(false);
   }
   finally{dialog.Hide();await shown;}
  }
 }
}
