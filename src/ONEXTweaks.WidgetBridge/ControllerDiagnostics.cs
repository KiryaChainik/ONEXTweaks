// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.Win32;
namespace ONEXTweaks.WidgetBridge;
internal sealed record ControllerAxes(double Lx,double Ly,double Rx,double Ry,double Lt,double Rt)
{
 internal static ControllerAxes From(ControllerFrame p){
  double Axis(short value)=>value<0?value/32768.0*100:value/32767.0*100;
  return new(Axis(p.Lx),Axis(p.Ly),Axis(p.Rx),Axis(p.Ry),p.Lt/255.0*100,p.Rt/255.0*100);
 }
 internal static ControllerAxes Tuned(ControllerFrame p,ControllerTuning t){
  var f=p with{Lx=ControllerTranslator.Axis(p.Lx,t.LeftDeadzone,t.InvertLx),Ly=ControllerTranslator.Axis(p.Ly,t.LeftDeadzone,t.InvertLy),Rx=ControllerTranslator.Axis(p.Rx,t.RightDeadzone,t.InvertRx),Ry=ControllerTranslator.Axis(p.Ry,t.RightDeadzone,t.InvertRy),Lt=ControllerTranslator.Trigger(p.Lt,t.LeftTriggerStart,t.LeftTriggerEnd,t.HairTriggers),Rt=ControllerTranslator.Trigger(p.Rt,t.RightTriggerStart,t.RightTriggerEnd,t.HairTriggers)};
  if(t.SwapSticks)f=f with{Lx=f.Rx,Ly=f.Ry,Rx=f.Lx,Ry=f.Ly};return From(f);
 }
}
internal sealed record ControllerRawDiagnosticSample(long Timestamp,int SourceSlot,uint Packet,ControllerAxes Physical);
internal sealed record ControllerDiagnosticObservation(DateTimeOffset UpdatedUtc,ControllerDiagnosticState State,ControllerRawDiagnosticSample? RuntimeInput);
internal sealed record ControllerDiagnosticSession(bool Active,string Kind,string Token);
internal sealed record ControllerDiagnosticState(bool Active,bool Available,string Kind,string Status,long Sequence,ControllerAxes? Physical=null,ControllerAxes? Tuned=null,ControllerTuning? Tuning=null,ControllerGyroReading? Motion=null,int SourceSlot=-1,ControllerGyroPreviewSnapshot? GyroPreview=null);
internal sealed class ControllerDiagnostics:IAsyncDisposable
{
 private readonly object gate=new();private readonly Func<(bool Available,ControllerFrame Frame)> input;private readonly Func<IControllerGyroStream> gyroFactory;private readonly Func<long> clock;
 private readonly CancellationTokenSource lifetime=new();private readonly Task monitor;private readonly List<Task> closing=[];private IControllerGyroStream? gyro;private ControllerGyroPreviewPump? preview;
 private readonly bool useNativeInput;
 private ControllerDiagnosticSession session=new(false,"","");private ControllerTuning tuning=new();private long deadline,sequence;private bool disposed;private int lastSourceSlot=-1;
 internal ControllerDiagnostics(Func<(bool,ControllerFrame)>? input=null,Func<IControllerGyroStream>? gyroFactory=null,Func<long>? clock=null){
  useNativeInput=input==null;this.input=input??Physical;this.gyroFactory=gyroFactory??(()=>new ControllerGyroStream());this.clock=clock??(()=>Environment.TickCount64);monitor=Task.Run(Monitor);
 }
 private static int SourceSlot(){
  using var settings=Registry.CurrentUser.OpenSubKey(@"Software\ONEXTweaks\Overlay");return Math.Clamp(settings?.GetValue("GuideControllerIndex") as int? ??0,0,3);
 }
 private (bool,ControllerFrame) Physical()=>Physical(SourceSlot());
 private (bool,ControllerFrame) Physical(int slot){
  Volatile.Write(ref lastSourceSlot,slot);bool ok=ControllerNative.Read(slot,out var frame);return(ok,frame);
 }
 internal bool Active{get{lock(gate){ExpireCore();return session.Active;}}}
 internal ControllerDiagnosticSession Session{get{lock(gate){ExpireCore();return session;}}}
 internal void Begin(string kind,ControllerTuning value,ControllerGyroPreviewSettings? settings=null,Func<ushort>? heldOem=null){
  if(kind is not("sticks" or "gyro"))throw new InvalidDataException("Unknown controller test");var validated=new ControllerPolicy(Tuning:value).Validate().Tuning!;
  lock(gate){
   ObjectDisposedException.ThrowIf(disposed,this);ExpireCore();if(session.Active)throw new InvalidOperationException("Close the current controller test first");
   tuning=validated;sequence=0;deadline=clock()+5000;session=new(true,kind,Guid.NewGuid().ToString("N"));
   if(kind=="gyro"){
    try{
     gyro=gyroFactory();gyro.Configure(true);
     if(settings!=null){
      Func<(bool,ControllerFrame)> read=input;
      if(useNativeInput){int slot=SourceSlot();read=()=>Physical(slot);}
      preview=new(gyro,settings,validated.TriggerThreshold,read,heldOem??(()=>0));
     }
    }catch{StopCore();throw;}
   }
  }
 }
 internal ControllerDiagnosticState Read(string token){
  lock(gate){
   ExpireCore();if(!session.Active)return new(false,false,"","Controller test ended",sequence);
   if(token!=session.Token)throw new InvalidOperationException("Controller test session changed");
   deadline=clock()+5000;sequence++;
   if(session.Kind=="sticks"){var p=input();return new(true,p.Available,"sticks",p.Available?"Outline: physical input · colour: after stick/trigger settings":"Controller disconnected",sequence,p.Available?ControllerAxes.From(p.Frame):null,p.Available?ControllerAxes.Tuned(p.Frame,tuning):null,tuning,SourceSlot:Volatile.Read(ref lastSourceSlot));}
   var frame=preview?.Latest;
   if(preview!=null&&frame==null)return new(true,false,"gyro","Connecting to motion sensors…",sequence);
   var sample=frame?.Reading??gyro!.Read();bool valid=sample.Ready&&sample.AccelerationAgeMilliseconds<=1000&&sample.Angular.Finite&&sample.Acceleration.Finite&&
    (frame==null||frame.Preview is {Raw:not null,Calibrated:not null});
   return new(true,valid,"gyro",sample.Error??(!valid?"Connecting to motion sensors…":sample.GyroAgeMilliseconds>500?"Latest gyro value held while unchanged":"Live motion sensors"),sequence,Motion:valid?sample:null,GyroPreview:frame?.Preview);
  }
 }
 internal void Stop(string token){lock(gate){if(session.Active&&session.Token==token)StopCore();}}
 internal void Expire(){lock(gate)ExpireCore();}
 private void ExpireCore(){if(session.Active&&clock()>=deadline)StopCore();}
 private void StopCore(){
  session=new(false,"","");deadline=0;
  if(gyro is{} sensor){gyro=null;var pump=preview;preview=null;sensor.Configure(false);closing.RemoveAll(t=>t.IsCompleted);closing.Add(Close(sensor,pump));}
 }
 private static async Task Close(IControllerGyroStream sensor,ControllerGyroPreviewPump? pump){
  try{if(pump!=null)await pump.DisposeAsync().ConfigureAwait(false);}
  finally{await sensor.DisposeAsync().ConfigureAwait(false);}
 }
 private async Task Monitor(){try{using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(250));while(await timer.WaitForNextTickAsync(lifetime.Token))Expire();}catch(OperationCanceledException){}}
 public async ValueTask DisposeAsync(){
  Task[] tasks;lock(gate){if(disposed)return;disposed=true;StopCore();tasks=closing.ToArray();lifetime.Cancel();}
  try{await monitor.ConfigureAwait(false);await Task.WhenAll(tasks).ConfigureAwait(false);}finally{lifetime.Dispose();}
 }
}
