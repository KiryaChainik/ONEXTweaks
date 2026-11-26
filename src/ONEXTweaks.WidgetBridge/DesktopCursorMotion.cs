// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;
/// <summary>Integrates desktop cursor speed in physical pixels. Sub-pixel motion survives between samples.</summary>
internal sealed class DesktopCursorMotion
{
 internal const double MaximumSampleSeconds=.032;
 private double x,y,velocityX,velocityY,movingSeconds;
 private bool hasMoved;
 internal void Reset(){x=y=velocityX=velocityY=movingSeconds=0;hasMoved=false;}
 private static int Step(ref double remainder,double amount)
 {
  remainder+=amount;int whole=(int)Math.Round(remainder,MidpointRounding.ToEven);remainder-=whole;return whole;
 }
 private static int AdvanceAxis(ref double remainder,ref double velocity,double target,double weight,double seconds)
 {
  if(Math.Sign(velocity)!=Math.Sign(target))
  {
   // Discard motion from the previous direction on this axis only.
   remainder=0;
   velocity=target;
  }
  else velocity+=(target-velocity)*weight;
  return Step(ref remainder,velocity*seconds);
 }
 internal (int X,int Y) Next(short horizontal,short vertical,double speed,double seconds)
 {
  if(!double.IsFinite(speed)||!double.IsFinite(seconds)||speed<0||seconds>MaximumSampleSeconds){Reset();return default;}
  if(seconds<=0)return default;
  double dt=seconds;
  double scale=650*Math.Clamp(speed,0,8)/32768;
  double targetX=horizontal*scale,targetY=-vertical*scale;
  if(targetX==0&&targetY==0){Reset();return default;}
  // The beginning of a stick ramp should be responsive even while it is still
  // below one pixel per sample. Blend into the sustained-motion filter.
  double blend=Math.Clamp(movingSeconds/.045,0,1);
  double weight=1-Math.Exp(-dt/(.006+.024*blend));
  if(hasMoved)movingSeconds=Math.Min(.045,movingSeconds+dt);
  int dx=AdvanceAxis(ref x,ref velocityX,targetX,weight,dt);
  int dy=AdvanceAxis(ref y,ref velocityY,targetY,weight,dt);
  hasMoved|=dx!=0||dy!=0;
  return(dx,dy);
 }

 // The sender verifies that stick intent is fresh. Do not replay missed
 // distance in one large step or carry that distance into later steps.
 internal (int X,int Y) NextFresh(ControllerOutput target,double elapsedSeconds)
 {
  const double maximumStepSeconds=2*ControllerOutput.DesktopCursorMilliseconds/1000.0;
  return Next(target.CursorX,target.CursorY,target.CursorSpeedScale,
   double.IsFinite(elapsedSeconds)?Math.Min(elapsedSeconds,maximumStepSeconds):elapsedSeconds);
 }

 internal static int PixelCoordinate(long pixel,int origin,int extent)
 {
  if(extent<2)throw new IOException("Virtual desktop is unavailable");
  // Aim at the physical pixel centre, including negative-origin multi-monitor desktops.
  double centre=Math.Clamp(pixel-origin,0,(long)extent-1)+.5;
  return(int)Math.Clamp(Math.Floor(centre*65536/extent),0,65535);
 }
}
