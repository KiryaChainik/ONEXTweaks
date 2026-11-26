// SPDX-License-Identifier: AGPL-3.0-only
using Windows.Devices.Sensors;
namespace ONEXTweaks.WidgetBridge;
internal interface IControllerGyroStream:IAsyncDisposable {void Configure(bool enabled);ControllerGyroReading Read();}
internal sealed class ControllerGyroStream:IControllerGyroStream
{
 // The WinRT report parameters are device-wide: preview and aiming must never restore over one another.
 internal static readonly SemaphoreSlim SensorLease=new(1,1);
 private readonly CancellationTokenSource stop=new();private readonly SemaphoreSlim changed=new(0,1);private readonly Task worker;private readonly object data=new();
 private int desired;private ControllerGyroReading reading=new();private long gyroArrival,accArrival;private bool disposed;
 internal ControllerGyroStream(){worker=Task.Run(Loop);}
 public void Configure(bool enabled){
  int value=enabled?1:0;if(Interlocked.Exchange(ref desired,value)==value)return;
  lock(data){reading=new();gyroArrival=accArrival=0;}
  try{changed.Release();}catch(SemaphoreFullException){}
 }
 public ControllerGyroReading Read(){
  lock(data){long now=Environment.TickCount64;return reading with{Ready=Volatile.Read(ref desired)==1&&reading.Ready,GyroAgeMilliseconds=gyroArrival==0?long.MaxValue:Math.Max(0,now-gyroArrival),AccelerationAgeMilliseconds=accArrival==0?long.MaxValue:Math.Max(0,now-accArrival)};}
 }
 private async Task Loop(){
  try{while(!stop.IsCancellationRequested){
   if(Volatile.Read(ref desired)==0){await changed.WaitAsync(stop.Token);continue;}
   await Session(stop.Token);
   if(Volatile.Read(ref desired)==1)await Task.Delay(1000,stop.Token);
  }}catch(OperationCanceledException){}
 }
 private async Task Session(CancellationToken token){
  await SensorLease.WaitAsync(token);try{if(Volatile.Read(ref desired)==0)return;await OwnedSession(token);}finally{SensorLease.Release();}
 }
 private async Task OwnedSession(CancellationToken token){
  Gyrometer? gyro=null;Accelerometer? acceleration=null;uint oldg=0,olda=0,oldLatency=0;double[]? oldThreshold=null;bool gs=false,acs=false,gConfigured=false,aConfigured=false,restored=true;long latestGyro=0,latestAcc=0;
  void G(Gyrometer sender,GyrometerReadingChangedEventArgs e){try{var r=e.Reading;lock(data){if(r.Timestamp.UtcTicks<=latestGyro)return;latestGyro=r.Timestamp.UtcTicks;gyroArrival=Environment.TickCount64;reading=reading with{Ready=accArrival!=0,Angular=new(r.AngularVelocityX,r.AngularVelocityY,r.AngularVelocityZ),Sequence=reading.Sequence+1,Error=null};}}catch{lock(data){gyroArrival=0;reading=reading with{Ready=false,Error="Gyro reading unavailable"};}}}
  void A(Accelerometer sender,AccelerometerReadingChangedEventArgs e){try{var r=e.Reading;lock(data){if(r.Timestamp.UtcTicks<=latestAcc)return;latestAcc=r.Timestamp.UtcTicks;accArrival=Environment.TickCount64;reading=reading with{Ready=gyroArrival!=0,Acceleration=new(r.AccelerationX,r.AccelerationY,r.AccelerationZ),Error=null};}}catch{lock(data){accArrival=0;reading=reading with{Ready=false,Error="Acceleration reading unavailable"};}}}
  try{
   lock(data){reading=new();gyroArrival=accArrival=0;}
   token.ThrowIfCancellationRequested();gyro=Gyrometer.GetDefault();acceleration=Accelerometer.GetDefault();if(gyro==null||acceleration==null)throw new InvalidOperationException("Windows motion sensors are unavailable");
   olda=acceleration.ReportInterval;aConfigured=true;acceleration.ReportInterval=Math.Max(16u,acceleration.MinimumReportInterval);acceleration.ReadingChanged+=A;acs=true;
   oldg=gyro.ReportInterval;oldLatency=gyro.ReportLatency;var t=gyro.ReportThreshold;oldThreshold=[t.XAxisInDegreesPerSecond,t.YAxisInDegreesPerSecond,t.ZAxisInDegreesPerSecond];gConfigured=true;gyro.ReportLatency=0;gyro.ReportInterval=Math.Max(16u,gyro.MinimumReportInterval);t.XAxisInDegreesPerSecond=.001;t.YAxisInDegreesPerSecond=.001;t.ZAxisInDegreesPerSecond=.001;gyro.ReadingChanged+=G;gs=true;
   while(Volatile.Read(ref desired)==1){await changed.WaitAsync(TimeSpan.FromMilliseconds(250),token);}
  }catch(OperationCanceledException){}catch(Exception e){lock(data)reading=reading with{Ready=false,Error="Motion sensors unavailable (0x"+unchecked((uint)e.HResult).ToString("X8")+")"};}
  finally{
   try{if(gs)gyro!.ReadingChanged-=G;if(gConfigured){gyro!.ReportInterval=oldg;gyro.ReportLatency=oldLatency;if(oldThreshold!=null){var t=gyro.ReportThreshold;t.XAxisInDegreesPerSecond=oldThreshold[0];t.YAxisInDegreesPerSecond=oldThreshold[1];t.ZAxisInDegreesPerSecond=oldThreshold[2];}}}catch{restored=false;}
   try{if(acs)acceleration!.ReadingChanged-=A;if(aConfigured)acceleration!.ReportInterval=olda;}catch{restored=false;}
   lock(data){reading=reading with{Ready=false,Error=restored?reading.Error:"Motion sensor parameters could not be restored",TemporaryParametersRestored=restored};gyroArrival=accArrival=0;}
  }
 }
 public async ValueTask DisposeAsync(){
  if(disposed)return;disposed=true;Configure(false);stop.Cancel();try{await worker.ConfigureAwait(false);}finally{changed.Dispose();stop.Dispose();}
 }
}
