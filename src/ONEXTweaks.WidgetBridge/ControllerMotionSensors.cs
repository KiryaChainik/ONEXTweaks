// SPDX-License-Identifier: AGPL-3.0-only
using Windows.Devices.Sensors;
namespace ONEXTweaks.WidgetBridge;
internal sealed record ControllerMotionCapture(bool GyroAvailable,bool AccelerometerAvailable,uint RequestedInterval,long Started,long Ended,MotionObservation[] Gyro,MotionObservation[] Acceleration,int ErrorHResult,bool ParametersRestored);
internal static class ControllerMotionSensors
{
 internal static async Task<ControllerMotionCapture> Capture(CancellationToken cancel)
 {
  await ControllerGyroStream.SensorLease.WaitAsync(cancel);try{return await OwnedCapture(cancel);}finally{ControllerGyroStream.SensorLease.Release();}
 }
 private static async Task<ControllerMotionCapture> OwnedCapture(CancellationToken cancel)
 {
  Gyrometer? gyro=null;Accelerometer? acceleration=null;uint gyroInterval=0,accelerationInterval=0,latency=0,requested=16;double[]? threshold=null;
  bool registeredGyro=false,registeredAcc=false,configuredGyro=false,configuredAcc=false,restored=true;int error=0;long started=0,ended=0;var gate=new object();var angular=new List<MotionObservation>();var gravity=new List<MotionObservation>();
  void Angular(Gyrometer sender,GyrometerReadingChangedEventArgs e){
   var r=e.Reading;var value=new MotionObservation(r.Timestamp.UtcTicks,new(r.AngularVelocityX,r.AngularVelocityY,r.AngularVelocityZ));
   lock(gate)if(angular.Count<1024&&(angular.Count==0||value.Timestamp>angular[^1].Timestamp))angular.Add(value);
  }
  void Accelerated(Accelerometer sender,AccelerometerReadingChangedEventArgs e){
   var r=e.Reading;var value=new MotionObservation(r.Timestamp.UtcTicks,new(r.AccelerationX,r.AccelerationY,r.AccelerationZ));
   lock(gate)if(gravity.Count<1024&&(gravity.Count==0||value.Timestamp>gravity[^1].Timestamp))gravity.Add(value);
  }
  try{
   cancel.ThrowIfCancellationRequested();gyro=Gyrometer.GetDefault();acceleration=Accelerometer.GetDefault();started=DateTimeOffset.UtcNow.UtcTicks;
   if(acceleration!=null){accelerationInterval=acceleration.ReportInterval;configuredAcc=true;acceleration.ReportInterval=Math.Max(16u,acceleration.MinimumReportInterval);acceleration.ReadingChanged+=Accelerated;registeredAcc=true;}
   if(gyro!=null){
    gyroInterval=gyro.ReportInterval;latency=gyro.ReportLatency;var t=gyro.ReportThreshold;threshold=[t.XAxisInDegreesPerSecond,t.YAxisInDegreesPerSecond,t.ZAxisInDegreesPerSecond];requested=Math.Max(16u,gyro.MinimumReportInterval);configuredGyro=true;
    gyro.ReportLatency=0;gyro.ReportInterval=requested;t.XAxisInDegreesPerSecond=.001;t.YAxisInDegreesPerSecond=.001;t.ZAxisInDegreesPerSecond=.001;gyro.ReadingChanged+=Angular;registeredGyro=true;
   }
   await Task.Delay(3000,cancel).ConfigureAwait(false);ended=DateTimeOffset.UtcNow.UtcTicks;
  }catch(Exception e){error=e.HResult;ended=DateTimeOffset.UtcNow.UtcTicks;}
  finally{
   try{if(registeredGyro)gyro!.ReadingChanged-=Angular;if(configuredGyro){gyro!.ReportInterval=gyroInterval;gyro.ReportLatency=latency;if(threshold!=null){var t=gyro.ReportThreshold;t.XAxisInDegreesPerSecond=threshold[0];t.YAxisInDegreesPerSecond=threshold[1];t.ZAxisInDegreesPerSecond=threshold[2];}}}catch{restored=false;}
   try{if(registeredAcc)acceleration!.ReadingChanged-=Accelerated;if(configuredAcc)acceleration!.ReportInterval=accelerationInterval;}catch{restored=false;}
  }
  lock(gate)return new(gyro!=null,acceleration!=null,requested,started,ended,angular.ToArray(),gravity.ToArray(),error,restored);
 }
}
