// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json.Serialization;
namespace ONEXTweaks.WidgetBridge;
internal sealed record ControllerGyroProcessing(string Engine="Linear",int AdaptiveSmoothing=50,int PrecisionSmoothing=50,int GateTenths=0,int AntiDeadzone=0,bool WorldSpace=false,bool InvertX=false,bool InvertY=false,int OutputRotation=0,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]ControllerGyroStickCurve? StickCurve=null,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? StickConversion=null)
{
 internal ControllerGyroProcessing Validate(){
  if(Engine is not("Linear" or "Adaptive" or "Precision")||AdaptiveSmoothing is <0 or >100||PrecisionSmoothing is <0 or >100||GateTenths is <0 or >100||AntiDeadzone is <0 or >50||OutputRotation is not(0 or 90 or 180 or 270))throw new InvalidDataException("Unsupported gyro processing");
  if(StickConversion!=null&&StickConversion is not("Yaw" or "Roll" or "YawRoll"))throw new InvalidDataException("Unsupported gyro stick input conversion");
  StickCurve?.Validate();return this;
 }
 internal int Smoothing=>Engine=="Precision"?PrecisionSmoothing:AdaptiveSmoothing;
}
internal sealed class ControllerGyroFilter
{
 private ControllerGyroProcessing? prior;private bool primed;private double previousX,previousY,filteredX,filteredY,dx,dy;
 internal void Reset(){prior=null;primed=false;previousX=previousY=filteredX=filteredY=dx=dy=0;}
 private static double Alpha(double cutoff,double dt)=>1/(1+1/(2*Math.PI*cutoff*dt));
 internal (double X,double Y) Apply(double x,double y,ControllerGyroProcessing? options,double dt){
  var p=options??new();
  if(!double.IsFinite(x)||!double.IsFinite(y)||!double.IsFinite(dt)||dt<=0||dt>.05){Reset();return(0,0);}
  if(prior!=p){Reset();prior=p;}
  if(p.Engine=="Linear"){primed=false;return(x,y);}
  if(p.Engine=="Precision"){
   // A speed-dependent sqrt gain: slow movement is finer, fast turns accelerate.
   // This is our backend implementation of the public Claw engine description.
   double speed=Math.Sqrt(x*x+y*y),gain=speed==0?0:Math.Clamp(Math.Sqrt(speed/90),.15,3);x*=gain;y*=gain;
  }
  if(p.Smoothing==0){primed=false;return(x,y);}
  if(!primed){primed=true;previousX=filteredX=x;previousY=filteredY=y;dx=dy=0;return(x,y);}
  // One Euro: low-pass the derivative, then raise the signal cutoff as speed changes.
  // Hz and °/s units; smoothing 0 bypasses, 100 has a .5Hz minimum cutoff.
  double ad=Alpha(1,dt);dx+=ad*((x-previousX)/dt-dx);dy+=ad*((y-previousY)/dt-dy);previousX=x;previousY=y;
  double minimum=30*Math.Pow(1.0/60,p.Smoothing/100.0),beta=p.Engine=="Precision"?.01:.02;
  double cutoff=minimum+beta*Math.Sqrt(dx*dx+dy*dy),a=Alpha(cutoff,dt);filteredX+=a*(x-filteredX);filteredY+=a*(y-filteredY);return(filteredX,filteredY);
 }
}
