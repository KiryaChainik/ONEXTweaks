// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
namespace ONEXTweaks.WidgetBridge;

internal readonly record struct GyroPreviewPair(double X,double Y);
internal sealed record ControllerGyroPreviewSettings(ControllerGyroPolicy Policy,GyroIdleCalibration? Calibration,bool VirtualMode);
internal sealed record ControllerGyroPreviewSnapshot(long Tick,long SensorSequence,MotionVector? Raw,MotionVector? Calibrated,
 GyroPreviewPair? Axes,GyroPreviewPair? Filtered,GyroPreviewPair? Output,string OutputUnit,double IntervalMilliseconds,
 string Stage,bool Active,bool CalibrationApplied,ControllerGyroPolicy Settings,bool VirtualMode,bool PhysicalAvailable,
 bool? ActivationPressed,bool? BoostPressed,GyroPreviewPair? Gain);
internal sealed record ControllerGyroPreviewFrame(long Timestamp,ControllerGyroReading Reading,ControllerGyroPreviewSnapshot Preview);

// Only the private preview router receives this collector. Its fields describe stages actually reached in Apply.
internal sealed class ControllerGyroTrace
{
 internal string Stage="SensorUnavailable";
 internal MotionVector? Calibrated;
 internal GyroPreviewPair? Axes,Filtered,Gain;
 internal bool ActivationPressed;
 internal bool? BoostPressed;
}

internal sealed class ControllerGyroPreviewProcessor(ControllerGyroPreviewSettings settings,int threshold)
{
 private static readonly ControllerOutput Neutral=new(new(),[],[],0,0,0,[]);
 private readonly ControllerGyroRouter router=new();
 private long tick;

 internal ControllerGyroPreviewSnapshot Sample(ControllerGyroReading reading,(bool Available,ControllerFrame Frame) physical,
  ushort heldOem,long milliseconds,double seconds)
 {
  var policy=settings.Policy;
  var trace=new ControllerGyroTrace();
  var result=router.Apply(Neutral,physical.Frame,policy,reading,settings.Calibration,heldOem,threshold,
   !physical.Available,milliseconds,seconds,trace);
  if(trace.Stage=="Paused"&&!physical.Available)trace.Stage="WaitingForController";

  // Raw motion and idle-bias correction are meaningful even while a binding or target is disabled.
  MotionVector? raw=reading.Ready&&reading.Angular.Finite&&reading.Angular.Maximum<=2000&&
   reading.Acceleration.Finite&&reading.AccelerationAgeMilliseconds<=1000?reading.Angular:null;
  var calibrated=trace.Calibrated??(raw is {} vector?settings.Calibration?.Correct(vector)??vector:(MotionVector?)null);
  string unit=policy.Target=="Mouse"?"mouseCountsPerTick":policy.Enabled?"stickCounts":"none";
  GyroPreviewPair? output=null;
  if(trace.Stage is "Output" or "GateClosed" or "BelowCurveMinimum"){
   // Neutral is never submitted. Read the rounded, saturated output of the real router.
   output=policy.Target switch{
    "Mouse"=>new(result.RelativeMouseX,result.RelativeMouseY),
    "LeftStick"=>new(result.Pad.Lx,result.Pad.Ly),
    _=>new(result.Pad.Rx,result.Pad.Ry)
   };
  }
  return new(++tick,reading.Sequence,raw,calibrated,trace.Axes,trace.Filtered,output,unit,
   double.IsFinite(seconds)?seconds*1000:0,trace.Stage,router.Active,calibrated.HasValue&&settings.Calibration!=null,
   policy,settings.VirtualMode,physical.Available,physical.Available?trace.ActivationPressed:null,trace.BoostPressed,trace.Gain);
 }
}

// Match the remapper's monotonic 8 ms loop. Slower UI reads only observe an immutable paired sample.
internal sealed class ControllerGyroPreviewPump:IAsyncDisposable
{
 private readonly CancellationTokenSource stop=new();
 private readonly Task worker;
 private ControllerGyroPreviewFrame? latest;
 internal ControllerGyroPreviewFrame? Latest{
  get{
   var value=Volatile.Read(ref latest);
   return value!=null&&Stopwatch.GetElapsedTime(value.Timestamp).TotalMilliseconds<=500?value:null;
  }
 }
 internal ControllerGyroPreviewPump(IControllerGyroStream sensor,ControllerGyroPreviewSettings policy,int threshold,
  Func<(bool Available,ControllerFrame Frame)> physical,Func<ushort> heldOem)
 {
  var processor=new ControllerGyroPreviewProcessor(policy,threshold);
  worker=Task.Run(async()=>{
   long started=Stopwatch.GetTimestamp(),previous=started;
   while(!stop.IsCancellationRequested){
    long now=Stopwatch.GetTimestamp();
    double seconds=Math.Clamp((now-previous)/(double)Stopwatch.Frequency,0,.05);
    previous=now;
    ControllerGyroReading reading;
    (bool Available,ControllerFrame Frame) input=default;
    ushort oem=0;
    try{reading=sensor.Read();}
    catch(Exception e){reading=new(Error:"Motion reading unavailable (0x"+unchecked((uint)e.HResult).ToString("X8")+")");}
    try{input=physical();oem=heldOem();}
    catch{input=default;}
    var snapshot=processor.Sample(reading,input,oem,(long)((now-started)*1000.0/Stopwatch.Frequency),seconds);
    Volatile.Write(ref latest,new(now,reading,snapshot));
    await Task.Delay(8,stop.Token).ConfigureAwait(false);
   }
  });
 }
 public async ValueTask DisposeAsync(){
  stop.Cancel();
  try{await worker.ConfigureAwait(false);}catch(OperationCanceledException) when(stop.IsCancellationRequested){}
  finally{stop.Dispose();}
 }
}
