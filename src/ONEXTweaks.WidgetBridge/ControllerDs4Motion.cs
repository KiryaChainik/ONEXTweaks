// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;
// WinRT gyro is degrees/s, accelerometer is g. VIIPER0.9 DS4 uses16 counts/degree/s and8192 counts/g.
internal readonly record struct ControllerDs4Motion(short GyroX,short GyroY,short GyroZ,short AccelX,short AccelY,short AccelZ)
{
 internal static ControllerDs4Motion Neutral=>new(0,0,0,0,0,-8192);
 internal static ControllerDs4Motion Read(ControllerGyroReading sample,GyroIdleCalibration? calibration,bool enabled){
  if(!enabled||!sample.Ready||sample.GyroAgeMilliseconds<0||sample.AccelerationAgeMilliseconds<0||sample.AccelerationAgeMilliseconds>250||!sample.Angular.Finite||!sample.Acceleration.Finite||sample.Angular.Maximum>2000||sample.Acceleration.Maximum>64)return Neutral;
  MotionVector angular=sample.GyroAgeMilliseconds<=500?(calibration?.Correct(sample.Angular)??sample.Angular):default;
  short Raw(double value,double scale)=>(short)Math.Clamp((int)Math.Round(value*scale),short.MinValue,short.MaxValue);
  return new(Raw(angular.X,16),Raw(angular.Y,16),Raw(angular.Z,16),Raw(sample.Acceleration.X,8192),Raw(sample.Acceleration.Y,8192),Raw(sample.Acceleration.Z,8192));
 }
 internal static string Status(bool enabled,bool ds4,bool paused,ControllerGyroReading sample)=>!enabled?"Disabled":!ds4?"Requires DualShock 4":paused?"Paused in Game Bar or keyboard & mouse":!sample.Ready||sample.AccelerationAgeMilliseconds>250?"Waiting for motion sensor":sample.Error??"Forwarding";
}
