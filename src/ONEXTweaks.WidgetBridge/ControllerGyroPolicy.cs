// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json.Serialization;
namespace ONEXTweaks.WidgetBridge;
internal sealed record ControllerGyroPolicy(string Target="Disabled",string Activation="Hold",string ActivationButton="None",int SensitivityX=70,int SensitivityY=70,string BoostButton="None",int BoostPercent=100,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]ControllerGyroProcessing? Processing=null)
{
 internal static readonly string[] Targets=["Disabled","LeftStick","RightStick","Mouse"];
 internal static readonly string[] Buttons=["None","LB","LT","RB","RT","OEM:M1","OEM:M2"];
 internal bool Enabled=>Target!="Disabled";
 internal ControllerGyroPolicy Validate(){
  if(!Targets.Contains(Target)||Activation is not("Always" or "Hold" or "Toggle")||!Buttons.Contains(ActivationButton)||!Buttons.Contains(BoostButton)||SensitivityX is <1 or >100||SensitivityY is <1 or >100||BoostPercent is <10 or >300)throw new InvalidDataException("Unsupported gyro settings");
  Processing?.Validate();return this;
 }
 internal static bool Pressed(string name,ControllerFrame physical,ushort heldOem,int threshold){
  if(name=="None")return false;
  if(name.StartsWith("OEM:",StringComparison.Ordinal)){int index=Array.IndexOf(ControllerOemPolicy.Inputs,name[4..]);return index>=0&&(heldOem&(1<<index))!=0;}
  if(name=="LT")return physical.Lt>=threshold*255/100;if(name=="RT")return physical.Rt>=threshold*255/100;
  return ControllerPolicy.Buttons.TryGetValue(name,out ushort mask)&&(physical.Buttons&mask)!=0;
 }
}
internal sealed record ControllerGyroReading(bool Ready=false,MotionVector Angular=default,MotionVector Acceleration=default,long GyroAgeMilliseconds=long.MaxValue,long AccelerationAgeMilliseconds=long.MaxValue,long Sequence=0,string? Error=null,bool? TemporaryParametersRestored=null);
internal sealed record ControllerGyroRuntime(bool Enabled,bool Active,string Target,string Status,long SensorSequence=0);
internal sealed class ControllerGyroRouter
{
 private readonly ControllerGyroFilter filter=new();private bool primed,previous,toggled;private long? previousTime;private double remainderX,remainderY;
 internal bool Active{get;private set;}internal string? BlockedReason{get;private set;}
 internal void Reset(){BlockedReason=null;filter.Reset();primed=previous=toggled=Active=false;previousTime=null;remainderX=remainderY=0;}
 internal ControllerOutput Apply(ControllerOutput basis,ControllerFrame physical,ControllerGyroPolicy settings,ControllerGyroReading reading,GyroIdleCalibration? calibration,ushort heldOem,int triggerThreshold,bool paused,long milliseconds,double seconds,ControllerGyroTrace? trace=null)
 {
  BlockedReason=null;bool down=ControllerGyroPolicy.Pressed(settings.ActivationButton,physical,heldOem,triggerThreshold);
  if(trace!=null)trace.ActivationPressed=down;
  bool gap=previousTime.HasValue&&(milliseconds<previousTime.Value||milliseconds-previousTime.Value>250);
  previousTime=milliseconds;
  bool blocked=paused||!settings.Enabled||!reading.Ready||reading.AccelerationAgeMilliseconds>250||!reading.Angular.Finite||!reading.Acceleration.Finite||reading.Angular.Maximum>2000||gap||!double.IsFinite(seconds)||seconds<=0||seconds>.05;
  if(blocked){
   if(trace!=null)trace.Stage=!reading.Ready||!reading.Angular.Finite||!reading.Acceleration.Finite||reading.Angular.Maximum>2000?"SensorUnavailable":
    reading.AccelerationAgeMilliseconds>250?"SensorStale":!settings.Enabled?"TargetDisabled":paused?"Paused":"TimingGap";
   filter.Reset();toggled=false;primed=true;previous=down;Active=false;remainderX=remainderY=0;return basis;}
  bool rising=primed&&!previous&&down;previous=down;primed=true;
  if(settings.Activation=="Toggle"&&rising)toggled=!toggled;
  bool on=settings.Activation=="Always"||settings.ActivationButton=="None"||settings.Activation=="Hold"&&down||settings.Activation=="Toggle"&&toggled;
  Active=on&&reading.GyroAgeMilliseconds<=500;if(!Active){if(trace!=null)trace.Stage=reading.GyroAgeMilliseconds>500?"SensorStale":"WaitingForActivation";filter.Reset();remainderX=remainderY=0;return basis;}
  var v=calibration?.Correct(reading.Angular)??reading.Angular;
  if(trace!=null)trace.Calibrated=v;
  // ONEXConsole OXP3's published device profile maps horizontal=-Y, vertical=X.
  // These are angular rates; no angle is accumulated across profiles or pauses.
  var axesProcessing=settings.Target=="Mouse"&&settings.Processing?.StickConversion!=null?settings.Processing with{StickConversion=null}:settings.Processing;
  if(!ControllerGyroAxes.TryResolve(v,reading.Acceleration,axesProcessing,out double rateX,out double rateY)){if(trace!=null)trace.Stage="WaitingForGravity";BlockedReason="Waiting for stable gravity";Active=false;filter.Reset();remainderX=remainderY=0;return basis;}
  if(trace!=null)trace.Axes=new(rateX,rateY);
  if(settings.Processing is{} processing&&(processing.GateTenths>0||settings.Target!="Mouse"&&processing.AntiDeadzone>0)&&Math.Sqrt(rateX*rateX+rateY*rateY)<=processing.GateTenths/10.0){if(trace!=null)trace.Stage="GateClosed";filter.Reset();remainderX=remainderY=0;return basis;}
  bool boostPressed=settings.BoostButton!="None"&&ControllerGyroPolicy.Pressed(settings.BoostButton,physical,heldOem,triggerThreshold);
  double boost=boostPressed?settings.BoostPercent/100.0:1;
  double sx=settings.SensitivityX/70.0*boost,sy=settings.SensitivityY/70.0*boost;
  if(trace!=null){trace.BoostPressed=boostPressed;trace.Gain=new(sx,sy);}
  if(settings.Target!="Mouse"&&settings.Processing?.StickCurve is{Enabled:true} activeCurve&&activeCurve.Map(rateX*sx,-rateY*sy)==(0.0,0.0)){if(trace!=null)trace.Stage="BelowCurveMinimum";filter.Reset();remainderX=remainderY=0;return basis;}
  var processed=filter.Apply(rateX,rateY,settings.Processing,seconds);
  double x=processed.X,y=processed.Y;
  if(trace!=null){trace.Filtered=new(x,y);trace.Stage="Output";}
  if(settings.Target=="Mouse"){
   remainderX+=Math.Clamp(x*12*sx*seconds,-256,256);remainderY+=Math.Clamp(y*12*sy*seconds,-256,256);int dx=(int)remainderX,dy=(int)remainderY;remainderX-=dx;remainderY-=dy;
   return basis with{RelativeMouseX=basis.RelativeMouseX+dx,RelativeMouseY=basis.RelativeMouseY+dy};
  }
  short Add(short value,double extra)=>(short)Math.Clamp((int)value+(int)Math.Round(extra),short.MinValue,short.MaxValue);
  double gx,gy;
  if(settings.Processing?.StickCurve is{Enabled:true} curve){(gx,gy)=curve.Map(x*sx,-y*sy);}
  else{
   gx=x/180*32767*sx;gy=-y/180*32767*sy;
   double radius=Math.Sqrt(gx*gx+gy*gy),floor=(settings.Processing?.AntiDeadzone??0)/100.0*32767;
   if(radius>0&&radius<floor){gx*=floor/radius;gy*=floor/radius;}
  }
  var pad=basis.Pad;pad=settings.Target=="LeftStick"?pad with{Lx=Add(pad.Lx,gx),Ly=Add(pad.Ly,gy)}:pad with{Rx=Add(pad.Rx,gx),Ry=Add(pad.Ry,gy)};
  return basis with{Pad=pad};
 }
}
