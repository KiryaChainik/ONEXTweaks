// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerDiagnosticsVerification
{
 private sealed class Samples:IControllerGyroStream{
  internal bool Enabled,Disposed;internal ControllerGyroReading Value=new(true,new(15,-30,60),new(.25,-.5,1),0,0,7);
  public void Configure(bool enabled)=>Enabled=enabled;public ControllerGyroReading Read(){var sample=Volatile.Read(ref Value);return sample with{Ready=Enabled&&sample.Ready};}public ValueTask DisposeAsync(){Disposed=true;return ValueTask.CompletedTask;}
 }
 private static async Task VerifyGyroPreview(Action<bool,string> check)
 {
  var reading=new ControllerGyroReading(true,new(11,-21,3),new(0,-1,0),0,0,7);
  var bias=new GyroIdleCalibration(1,DateTimeOffset.UnixEpoch,1,-1,1,150,2000);
  var always=new ControllerGyroPolicy("RightStick","Always");
  var physical=(true,new ControllerFrame());
  ControllerGyroPreviewProcessor Processor(ControllerGyroPolicy policy)=>new(new(policy,bias,false),20);
  var processor=Processor(always);
  var first=processor.Sample(reading,physical,0,0,.008);
  check(first.Raw==reading.Angular&&first.Calibrated==new MotionVector(10,-20,2)&&first.CalibrationApplied,"Raw and bias-corrected body rates lost");
  check(first.Axes==new GyroPreviewPair(20,10)&&first.Filtered==first.Axes,"Resolved local axes differ from router");
  check(first.Output==new GyroPreviewPair(3641,-1820)&&first.OutputUnit=="stickCounts"&&!first.VirtualMode,"Final stick units lost sign, saturation or profile mode");
  var disabled=Processor(new()).Sample(reading,physical,0,0,.008);
  check(disabled.Stage=="TargetDisabled"&&disabled.Raw==first.Raw&&disabled.Calibrated==first.Calibrated&&disabled.Axes==null&&disabled.Output==null,"Disabled mapping invented output or hid calibration");
  var stale=processor.Sample(reading with{GyroAgeMilliseconds=501},physical,0,8,.008);
  check(stale.Stage=="SensorStale"&&stale.Output==null&&stale.Filtered==null&&!stale.Active,"Stale sensor reused last target output");
  var absent=processor.Sample(reading,(false,default),0,16,.008);
  check(absent.Stage=="WaitingForController"&&absent.Calibrated==first.Calibrated&&absent.Output==null,"Disconnected controller hid rates or produced output");
  var invalid=processor.Sample(reading with{Angular=new(double.NaN,0,0)},physical,0,24,.008);
  check(invalid.Stage=="SensorUnavailable"&&invalid.Raw==null&&invalid.Calibrated==null&&invalid.Output==null,"Invalid rate reached JSON");
  var gap=processor.Sample(reading,physical,0,400,.008);
  check(gap.Stage=="TimingGap"&&gap.Output==null,"Timing gap emitted target movement");
  var low=reading with{Angular=new(1,-2,1)};
  var gate=Processor(always with{Processing=new(GateTenths:20)}).Sample(low,physical,0,0,.008);
  check(gate.Active&&gate.Stage=="GateClosed"&&gate.Axes==new GyroPreviewPair(1,0)&&gate.Filtered==null&&gate.Output==new GyroPreviewPair(0,0),"Gate rejection indistinguishable from missing data");
  var curve=Processor(always with{Processing=new(StickCurve:new(Enabled:true,MinInput:30))}).Sample(reading,physical,0,0,.008);
  check(curve.Stage=="BelowCurveMinimum"&&curve.Filtered==null&&curve.Output==new GyroPreviewPair(0,0),"Custom curve rejection reported as a filtered stage");
  var floor=Processor(always with{Processing=new(AntiDeadzone:30)}).Sample(low,physical,0,0,.008);
  check(floor.Output==new GyroPreviewPair(9830,0),"Anti-deadzone floor missing from final output");
  var hold=Processor(always with{Activation="Hold",ActivationButton="LT",BoostButton="OEM:M1",BoostPercent=200});
  check(hold.Sample(reading,(true,new(Lt:50)),0,0,.008).Stage=="WaitingForActivation","Hold threshold ignored physical trigger");
  int m1=Array.IndexOf(ControllerOemPolicy.Inputs,"M1");
  var boosted=hold.Sample(reading,(true,new(Lt:51)),(ushort)(1<<m1),8,.008);
  check(boosted.Output==new GyroPreviewPair(7282,-3641)&&boosted.BoostPressed==true&&boosted.ActivationPressed==true&&boosted.Gain==new GyroPreviewPair(2,2),"Physical boost or trigger threshold did not reach output");
  var toggle=Processor(always with{Activation="Toggle",ActivationButton="LT"});
  toggle.Sample(reading,physical,0,0,.008);
  check(toggle.Sample(reading,(true,new(Lt:255)),0,8,.008).Active&&toggle.Sample(reading,physical,0,16,.008).Active&&
   !toggle.Sample(reading,(true,new(Lt:255)),0,24,.008).Active,"Preview toggle did not retain state across ticks");
  var adaptive=Processor(always with{Processing=new("Adaptive",AdaptiveSmoothing:100)});
  adaptive.Sample(reading,physical,0,0,.008);
  var filtered=adaptive.Sample(reading with{Angular=new(21,-41,1)},physical,0,8,.008);
  check(filtered.Axes==new GyroPreviewPair(40,20)&&filtered.Filtered is {X:>20 and <40,Y:>10 and <20},"Filter trace reused input or failed to advance");
  var mousePolicy=always with{Target="Mouse",Processing=new("Precision",PrecisionSmoothing:65)};
  var mouse=Processor(mousePolicy);var normal=new ControllerGyroRouter();
  var neutral=new ControllerOutput(new(),[],[],0,0,0,[]);
  // Compare against the public gameplay calculation across sign changes and fractional mouse remainders.
  for(int i=0;i<20;i++){
   var sample=reading with{Angular=new(i*.75,-45+i*4,4)};
   var traced=mouse.Sample(sample,physical,0,i*8,.008);
   var actual=normal.Apply(neutral,physical.Item2,mousePolicy,sample,bias,0,20,false,i*8,.008);
   check(traced.OutputUnit=="mouseCountsPerTick"&&traced.Output==new GyroPreviewPair(actual.RelativeMouseX,actual.RelativeMouseY),"Mouse preview diverged from the production router");
  }
  var sensor=new Samples();int inputReads=0;long leaseTime=0;
  await using var live=new ControllerDiagnostics(()=>{Interlocked.Increment(ref inputReads);return physical;},()=>sensor,()=>Volatile.Read(ref leaseTime));
  live.Begin("gyro",new(),new(always,bias,true));string token=live.Session.Token;
  ControllerDiagnosticState state=live.Read(token);
  for(int i=0;i<100&&state.GyroPreview?.Stage!="Output";i++){await Task.Delay(10);state=live.Read(token);}
  check(state.Available&&state.GyroPreview is {Stage:"Output",VirtualMode:true}&&state.Motion?.Sequence==state.GyroPreview.SensorSequence,"Worker failed to publish paired sensor/preview state");
  int before=Volatile.Read(ref inputReads);await Task.Delay(80);
  check(Volatile.Read(ref inputReads)>before&&live.Read(token).GyroPreview!.Tick>state.GyroPreview!.Tick,"Read-only host polling drove the filter clock");
  using(var wire=JsonDocument.Parse(JsonSerializer.Serialize(state,Backend.Program.Json))){
   var preview=wire.RootElement.GetProperty("gyroPreview");
   check(preview.GetProperty("raw").GetProperty("z").GetDouble()==60&&preview.GetProperty("output").GetProperty("x").ValueKind==JsonValueKind.Number&&preview.GetProperty("settings").GetProperty("target").GetString()=="RightStick","Host JSON lost raw, target or output");
  }
  Volatile.Write(ref sensor.Value,sensor.Value with{Angular=new(2001,0,0),Sequence=8});
  for(int i=0;i<100&&state.GyroPreview?.SensorSequence!=8;i++){await Task.Delay(10);state=live.Read(token);}
  check(!state.Available&&state.Motion==null&&state.GyroPreview is {Stage:"SensorUnavailable",Raw:null,Calibrated:null,Output:null},"Host marked out-of-range sensor sample as rendered data");
  Volatile.Write(ref leaseTime,6000);live.Expire();await live.DisposeAsync();
  before=Volatile.Read(ref inputReads);await Task.Delay(40);
  check(!live.Active&&sensor.Disposed&&!sensor.Enabled&&Volatile.Read(ref inputReads)==before,"Expired preview continued polling after lease disposal");
 }
 internal static async Task<int> Models(string file){
  int checks=0;void Check(bool value,string why){checks++;if(!value)throw new IOException(why);}
  try{
   var returning=new ControllerDiagnosticReturnGate(1,2);
   Check(returning.Observe(false,false,2)==ControllerDiagnosticReturnAction.Wait,"Return raced ahead of the host process exit");
   Check(returning.Observe(false,false,1)==ControllerDiagnosticReturnAction.Wait,"Restored desktop alone bypassed the process exit");
   Check(returning.Observe(true,false,0)==ControllerDiagnosticReturnAction.Wait,"Transient empty foreground opened the overlay");
   Check(returning.Observe(true,false,2)==ControllerDiagnosticReturnAction.Wait,"Dying host window opened the overlay");
   Check(returning.Observe(true,false,1)==ControllerDiagnosticReturnAction.Open,"Exited host did not return to the expected desktop");
   var switched=new ControllerDiagnosticReturnGate(1,2);
   Check(switched.Observe(false,false,3)==ControllerDiagnosticReturnAction.Cancel&&switched.Observe(true,false,1)==ControllerDiagnosticReturnAction.Cancel,"Alt+Tab cancellation was lost");
   var alreadyOpen=new ControllerDiagnosticReturnGate(1,2);
   Check(alreadyOpen.Observe(false,true,2)==ControllerDiagnosticReturnAction.AlreadyOpen&&alreadyOpen.Observe(true,false,1)==ControllerDiagnosticReturnAction.Cancel,"Independently opened overlay was reopened");
   Check(new ControllerDiagnosticReturnGate(0,2).Observe(true,false,1)==ControllerDiagnosticReturnAction.Cancel,"Unknown origin permitted a foreground change");
   long clock=0;int factories=0,reads=0;bool connected=true;var frame=new ControllerFrame(Lx:-32768,Ly:32767,Rx:16384,Ry:-16384,Lt:128,Rt:255);var samples=new Samples();
   await using var tests=new ControllerDiagnostics(()=>{reads++;return(connected,frame);},()=>{factories++;return samples;},()=>clock);
   Check(!tests.Active&&reads==0&&factories==0,"Construction read hardware or opened sensors");
   bool invalid=false;try{tests.Begin("wrong",new());}catch(InvalidDataException){invalid=true;}Check(invalid&&!tests.Active,"Invalid test kind activated hardware");
   tests.Begin("sticks",new());string token=tests.Session.Token;Check(token.Length==32&&tests.Active&&factories==0,"Sticks test created gyro");
   var state=tests.Read(token);Check(state.Available&&state.Physical!.Lx==-100&&state.Physical.Ly==100&&Math.Abs(state.Physical.Rx-50.0015)<.01,"Physical signed stick range wrong");
   Check(state.Physical!.Rt==100&&Math.Abs(state.Physical.Lt-50.196)<.01&&state.Physical==state.Tuned,"Default tuning preview altered input");
   using(var wire=JsonDocument.Parse(JsonSerializer.Serialize(state,Backend.Program.Json))){var p=wire.RootElement.GetProperty("physical");Check(p.GetProperty("lx").GetDouble()==-100&&p.GetProperty("ly").GetDouble()==100,"Diagnostic JSON stick fields do not match widget keys");Check(p.GetProperty("rt").GetDouble()==100&&p.GetProperty("lt").ValueKind==JsonValueKind.Number,"Diagnostic JSON triggers missing or nonnumeric");Check(wire.RootElement.GetProperty("tuned").GetProperty("rx").GetDouble()>50,"Diagnostic JSON tuned axes lost their values");}
   invalid=false;try{tests.Read("not-owner");}catch(InvalidOperationException){invalid=true;}Check(invalid&&tests.Active,"Foreign token read or ended live session");
   tests.Stop("not-owner");Check(tests.Active,"Foreign token closed live session");
   connected=false;state=tests.Read(token);Check(!state.Available&&state.Physical==null&&state.Tuned==null,"Disconnected input shown as zero or retained as live");
   tests.Stop(token);Check(!tests.Active&&!tests.Read(token).Active&&factories==0,"Sticks stop leaked source");connected=true;
   frame=new(Lx:3000,Ly:16384,Rx:-32768,Ry:0,Lt:51,Rt:204);
   tests.Begin("sticks",new(LeftDeadzone:20,InvertLy:true,SwapSticks:true,LeftTriggerStart:20,LeftTriggerEnd:80,RightTriggerStart:20,RightTriggerEnd:80));
   state=tests.Read(tests.Session.Token);Check(state.Tuned!.Rx==0&&state.Tuned.Lx==-100,"Deadzone/swap preview wrong");
   Check(state.Tuned.Ry< -37.4&&state.Tuned.Ry> -37.6,"Inversion/deadzone scaling preview wrong");
   Check(state.Tuned.Lt==0&&state.Tuned.Rt==100,"Trigger travel preview wrong");tests.Stop(tests.Session.Token);
   tests.Begin("sticks",new(HairTriggers:true));state=tests.Read(tests.Session.Token);Check(state.Tuned!.Lt==100&&state.Tuned.Rt==100,"Hair trigger preview not digital");tests.Stop(tests.Session.Token);
   tests.Begin("gyro",new());token=tests.Session.Token;Check(samples.Enabled&&factories==1,"Explicit gyro start failed");state=tests.Read(token);
   Check(state.Available&&state.Motion!.Angular==new MotionVector(15,-30,60)&&state.Motion.Acceleration==new MotionVector(.25,-.5,1),"Six sensor channels altered");
   samples.Value=samples.Value with{GyroAgeMilliseconds=1500};state=tests.Read(token);Check(state.Available&&state.Status.Contains("held"),"On-change held gyro invented new samples or shown unavailable");
   samples.Value=samples.Value with{AccelerationAgeMilliseconds=1001};Check(!tests.Read(token).Available,"Disconnected motion sensors still shown live");
   samples.Value=samples.Value with{AccelerationAgeMilliseconds=0,Angular=new(double.NaN,0,0)};Check(!tests.Read(token).Available,"Non-finite input leaked into JSON");
   clock=4999;tests.Read(token);clock=5001;Check(tests.Active,"Polling did not renew lease");
   clock=10000;tests.Expire();Check(!tests.Active&&!samples.Enabled&&samples.Disposed,"Lost widget lease did not stop and dispose sensor");
   samples=new();tests.Begin("gyro",new());token=tests.Session.Token;tests.Stop(token);Check(!tests.Active&&!samples.Enabled&&samples.Disposed,"Explicit close leaked gyro");
   await tests.DisposeAsync();invalid=false;try{tests.Begin("sticks",new());}catch(ObjectDisposedException){invalid=true;}Check(invalid,"Disposed session reopened");
   await VerifyGyroPreview(Check);
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,gyroPreviewTrace=true,gyroPreviewMatchesGameplay=true,gyroPreviewLeaseStopped=true,syntheticFrames=true,actualHardware=false,outputInjection=0,profileWrites=0},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message},Backend.Program.Json));return 2;}
 }
 internal static async Task<int> Native(string file){
  int checks=0;ControllerGyroStream? sensor=null;void Check(bool value,string why){checks++;if(!value)throw new IOException(why);}
  try{
   using var config=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\ONEXTweaks\Overlay");int source=Math.Clamp(config?.GetValue("GuideControllerIndex") as int? ??0,0,3);Check(ControllerNative.Read(source,out _),"Physical XInput unavailable");
   await using var tests=new ControllerDiagnostics(gyroFactory:()=>sensor=new ControllerGyroStream());
   tests.Begin("sticks",new());var sticks=tests.Read(tests.Session.Token);Check(sticks.Available&&sticks.Physical!=null&&sticks.Tuned==sticks.Physical,"Actual XInput live test failed");
   Check(sticks.Physical!.Lx is >=-100 and <=100&&sticks.Physical.Ly is >=-100 and <=100&&sticks.Physical.Lt is >=0 and <=100,"Actual physical range invalid");
   tests.Stop(tests.Session.Token);Check(!tests.Active&&sensor==null,"Sticks test touched motion source");
   await using var aiming=new ControllerGyroStream();aiming.Configure(true);
   for(int n=0;n<100&&!aiming.Read().Ready;n++)await Task.Delay(50);Check(aiming.Read().Ready,"Pre-existing motion source unavailable");
   tests.Begin("gyro",new());string token=tests.Session.Token;await Task.Delay(100);Check(!tests.Read(token).Available,"Preview bypassed live sensor ownership");
   aiming.Configure(false);ControllerDiagnosticState reading=new(false,false,"","",0);
   for(int n=0;n<100;n++){reading=tests.Read(token);if(reading.Available)break;await Task.Delay(50);}
   Check(reading.Available&&reading.Motion!.Sequence>=1&&reading.Motion.Angular.Finite&&reading.Motion.Acceleration.Finite,"Real six-channel sensor stream unavailable");
   Check(reading.Motion!.Acceleration.Length is >.3 and <2,"Actual acceleration gravity range invalid");
   using(var cancel=new CancellationTokenSource(100)){bool cancelled=false;try{await ControllerMotionSensors.Capture(cancel.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled&&tests.Read(token).Available,"Calibration bypassed preview ownership or failed cancellation");}
   tests.Stop(token);Check(!tests.Active,"Live gyro stop failed");await tests.DisposeAsync();
   Check(sensor!.Read().TemporaryParametersRestored==true,"Real sensor report parameters not restored after close");
   Check(ControllerNative.Read(source,out _),"Physical input lost after diagnostics");
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,actualPhysicalXInputRead=true,actualWindowsSixChannels=true,actualSerializedSensorHandoff=true,actualSensorParametersRestored=true,profileWrites=0,outputInjection=0,firmwareWrites=0,ownerPhysicalMovementTested=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>")},Backend.Program.Json));return 2;}
 }
}
