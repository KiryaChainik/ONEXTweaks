// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal sealed record ControllerMotionState(bool Busy=false,string Operation="",string Status="Check the motion sensors or calibrate idle drift",bool? GyroAvailable=null,bool? AccelerometerAvailable=null,int GyroSamples=0,int AccelerometerSamples=0,MotionVector? AngularVelocity=null,MotionVector? Acceleration=null,GyroIdleCalibration? Calibration=null,bool? TemporaryParametersRestored=null,string? Error=null);
internal sealed class ControllerMotion:IAsyncDisposable
{
 private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};
 private readonly object gate=new();private readonly string file;private readonly CancellationTokenSource lifetime=new();private readonly Func<CancellationToken,Task<ControllerMotionCapture>> source;
 private ControllerMotionState state=new();private Task? pending;private bool disposed;
 internal ControllerMotion(string file,Func<CancellationToken,Task<ControllerMotionCapture>>? source=null){
  this.file=file;this.source=source??ControllerMotionSensors.Capture;
  if(File.Exists(file))try{state=state with{Calibration=(JsonSerializer.Deserialize<GyroIdleCalibration>(File.ReadAllText(file),Json)??throw new InvalidDataException()).Validate(),Status="Idle calibration loaded"};}catch{state=state with{Error="Saved gyro calibration is unreadable; it has been kept unchanged"};}
 }
 internal ControllerMotionState Read(){lock(gate)return state;}
 internal Task Completion{get{lock(gate)return pending??Task.CompletedTask;}}
 internal void Begin(string operation){
  if(operation is not("motionCheck" or "motionCalibrate" or "motionReset"))throw new InvalidDataException("Unknown gyro action");
  lock(gate){
   ObjectDisposedException.ThrowIf(disposed,this);if(state.Busy)throw new InvalidOperationException("Wait for the current sensor check");
   if(operation=="motionReset"){if(File.Exists(file))File.Delete(file);state=state with{Calibration=null,Error=null,Status="Idle calibration reset",Operation=""};return;}
   state=state with{Busy=true,Operation=operation,Error=null,Status=operation=="motionCalibrate"?"Keep the console still for 3 seconds…":"Checking motion sensors…"};
   pending=Task.Run(()=>Run(operation,lifetime.Token));
  }
 }
 private async Task Run(string operation,CancellationToken cancel){
  try{
   var result=await source(cancel).ConfigureAwait(false);cancel.ThrowIfCancellationRequested();
   var window=new GyroIdleCalibrationWindow();foreach(var row in result.Gyro)window.Add(row.Timestamp,row.Value);
   var calibration=operation=="motionCalibrate"?window.FinishOnChange(result.Started,result.Ended,DateTimeOffset.UtcNow,result.Acceleration):null;
   string? error=result.ErrorHResult!=0?"Motion sensor read failed (0x"+unchecked((uint)result.ErrorHResult).ToString("X8")+")":!result.ParametersRestored?"Temporary sensor parameters could not be restored":!result.GyroAvailable?"No Windows gyroscope is available":result.Gyro.Length==0?"No fresh gyro reading was received":operation=="motionCalibrate"&&calibration?.Passed!=true?calibration?.Reason:null;
   lock(gate){
    cancel.ThrowIfCancellationRequested();var saved=state.Calibration;
    if(error==null&&calibration?.Calibration!=null){Write(calibration.Calibration);saved=calibration.Calibration;}
    state=state with{Busy=false,Operation="",Status=error??(operation=="motionCalibrate"?"Idle drift calibration applied":"Windows motion sensors are available"),GyroAvailable=result.GyroAvailable,AccelerometerAvailable=result.AccelerometerAvailable,GyroSamples=result.Gyro.Length,AccelerometerSamples=result.Acceleration.Length,AngularVelocity=result.Gyro.Length>0?result.Gyro[^1].Value:null,Acceleration=result.Acceleration.Length>0?result.Acceleration[^1].Value:null,TemporaryParametersRestored=result.ParametersRestored,Calibration=saved,Error=error};
   }
  }catch(OperationCanceledException){lock(gate)state=state with{Busy=false,Operation="",Status="Sensor operation cancelled"};}
   catch{lock(gate)state=state with{Busy=false,Operation="",Status="Sensor operation failed",Error="The previous gyro calibration has been kept"};}
 }
 private void Write(GyroIdleCalibration calibration){
  calibration.Validate();string temp=file+".motion-"+Guid.NewGuid().ToString("N")+".tmp";
  try{File.WriteAllText(temp,JsonSerializer.Serialize(calibration,Json));File.Move(temp,file,true);}finally{if(File.Exists(temp))File.Delete(temp);}
 }
 public async ValueTask DisposeAsync(){
  Task? work;lock(gate){if(disposed)return;disposed=true;lifetime.Cancel();work=pending;}
  try{if(work!=null)await work.ConfigureAwait(false);}finally{lifetime.Dispose();}
 }
}
