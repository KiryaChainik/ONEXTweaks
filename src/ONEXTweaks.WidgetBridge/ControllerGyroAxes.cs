// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerGyroAxes
{
 internal static bool TryResolve(MotionVector angular,MotionVector acceleration,ControllerGyroProcessing? options,out double x,out double y){
  x=y=0;if(!angular.Finite||!acceleration.Finite)return false;var p=options??new();
  x=-angular.Y;y=angular.X;
  if(p.WorldSpace){
   double length=acceleration.Length;if(length is <.5 or >1.5)return false;
   double gx=acceleration.X/length,gy=acceleration.Y/length,gz=acceleration.Z/length;
   x=angular.X*gx+angular.Y*gy+angular.Z*gz;
   // Screen-right is perpendicular to gravity in the screen plane.
   // Near flat, tiny accelerometer noise has no reliable screen-plane heading;
   // within about 9 degrees retain the device X pitch axis.
   double right=Math.Sqrt(gx*gx+gy*gy);y=right>.15?(-gy*angular.X+gx*angular.Y)/right:angular.X;
  }
  else{
   // Local ONEX axes: existing yaw=-Y and pitch=X; screen-plane roll=-Z.
   // Combined adds the signed rates, rather than accumulating an angle.
   // The gravity-relative frame above remains authoritative when enabled.
   x=p.StickConversion switch{"Roll"=>-angular.Z,"YawRoll"=>-angular.Y-angular.Z,_=>-angular.Y};
  }
  (x,y)=p.OutputRotation switch{90=>(-y,x),180=>(-x,-y),270=>(y,-x),_=>(x,y)};
  if(p.InvertX)x=-x;if(p.InvertY)y=-y;return double.IsFinite(x)&&double.IsFinite(y);
 }
}
