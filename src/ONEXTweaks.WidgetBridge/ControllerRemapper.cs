// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
namespace ONEXTweaks.WidgetBridge;
internal sealed record ControllerRuntimeState(bool Running,bool Busy,int SourceSlot,int VirtualSlot,string? Error,string[] Pressed,long Frames,string? LastInput=null,int ObserverVirtualSlot=-1,ControllerGyroRuntime? Gyro=null,string OutputTarget="Xbox360",string? MotionForwarding=null,string? LightingForwarding=null);
internal sealed class ControllerInputNotNeutralException(string message):InvalidOperationException(message){}
internal interface IControllerCaptureRuntime:IAsyncDisposable
{
 ControllerRuntimeState Read();void Policy(ControllerPolicy next);Task Start();Task Stop();
}
internal sealed class ControllerRemapper:IAsyncDisposable,IControllerCaptureRuntime
{
 private static ControllerOutputDevice? currentOutput;private static int ownSlot=-1;internal static int OutputSlot=>Volatile.Read(ref ownSlot);
 internal static string? OwnDs4Identity=>Volatile.Read(ref currentOutput)?.Serial;
 private readonly Func<int,Func<ControllerVibration>,ControllerOutputDevice> outputFactory;
 private readonly Func<GyroIdleCalibration?> calibration;private readonly Func<bool> motionBusy;private readonly Func<IControllerGyroStream> gyroFactory;
 private readonly string reports;private readonly ControllerOemRawInput? oemButtons;private readonly SemaphoreSlim gate=new(1,1);private CancellationTokenSource? stopping;private Task? worker;
 private KeyboardMouseMotionRecorder? motionRecorder;
 private ControllerRawDiagnosticSample? diagnosticInput;
 internal ControllerRawDiagnosticSample? DiagnosticInput=>Read().Running?Volatile.Read(ref diagnosticInput):null;
 private ControllerPolicy policy=new ControllerPolicy().Validate();private int revision;private ControllerRuntimeState state=new(false,false,-1,-1,null,[],0);
 private static readonly SemaphoreSlim directVibration=new(1,1);
 internal static async Task<ControllerVibrationTestResult> TestVibration(ControllerVibration value)
 {
  value.Validate();var captured=Volatile.Read(ref currentOutput);if(captured!=null){var pulse=await captured.TestVibration(value);return new(value.Intensity,400,pulse.Large,pulse.Small,"captured physical controller");}
  await directVibration.WaitAsync();try
  {
   if(ControllerNative.Sources().Length!=1)throw new IOException("Connect only the built-in ONEX controller before testing vibration");
   using var config=Registry.CurrentUser.OpenSubKey(@"Software\ONEXTweaks\Overlay");int source=Math.Clamp(config?.GetValue("GuideControllerIndex") as int? ??0,0,3);var slots=ControllerNative.Slots();
   if(!slots.Contains(source)||slots.Any(slot=>slot!=source&&slot!=VirtualGuide.OwnIndex))throw new IOException("An additional controller is connected. Vibration test is paused to protect it.");
   using var forward=new ControllerRumbleForwarder(()=>value,(large,small)=>ControllerNative.RumbleChecked(source,large,small));var pulse=await forward.Test(value);return new(value.Intensity,400,pulse.Large,pulse.Small,"physical controller without capture");
  }finally{directVibration.Release();}
 }
 internal static async Task<int?> TapGuide()
 {
  var output=Volatile.Read(ref currentOutput);if(output==null)return null;
  try{output.Guide(true);await Task.Delay(100);}finally{try{output.Guide(false);}catch(ObjectDisposedException){}}
  return output.Slot;
 }
 ControllerRuntimeState IControllerCaptureRuntime.Read()=>Read();
 void IControllerCaptureRuntime.Policy(ControllerPolicy next)=>Policy(next);
 Task IControllerCaptureRuntime.Start()=>Start();Task IControllerCaptureRuntime.Stop()=>Stop();
 internal ControllerRuntimeState Read()=>Volatile.Read(ref state);
 internal void Policy(ControllerPolicy next){Volatile.Write(ref policy,next.Validate());Interlocked.Increment(ref revision);}
 internal ControllerRemapper(string directory,ControllerOemRawInput? buttons=null,Func<GyroIdleCalibration?>? calibration=null,Func<bool>? motionBusy=null,Func<IControllerGyroStream>? gyroFactory=null,Func<int,Func<ControllerVibration>,ControllerOutputDevice>? outputFactory=null,Func<ControllerGameLightingRequest,Task<ControllerGameLightingReply>>? gameLighting=null){this.outputFactory=outputFactory??((source,vibration)=>new(source,vibration,Volatile.Read(ref policy).OutputTarget,gameLighting));reports=directory;oemButtons=buttons;this.calibration=calibration??(()=>null);this.motionBusy=motionBusy??(()=>false);this.gyroFactory=gyroFactory??(()=>new ControllerGyroStream());}
 internal async Task Start()
 {
  await gate.WaitAsync();ControllerOutputDevice? pad=null;ControllerCaptureLease? lease=null;int challengedSource=-1;ControllerRouteTrace? routeTrace=null;
  try
  {
   if(Read().Running)return;if(worker!=null)await worker;Volatile.Write(ref diagnosticInput,null);Volatile.Write(ref state,new(false,true,-1,-1,null,[],0));
   routeTrace=ControllerRouteTrace.TryStart(reports);
   routeTrace?.Point("capture-attempt");
   if(!ControllerInputDesktop.Available())throw new ControllerInputNotNeutralException("Windows is displaying a secure screen; physical controller input remains available");
   var sources=ControllerNative.Sources();if(sources.Length!=1)throw new InvalidOperationException("Connect only the built-in ONEX controller before enabling remapping");
   var builtIn=sources[0];
   using var config=Registry.CurrentUser.OpenSubKey(@"Software\ONEXTweaks\Overlay");int source=Math.Clamp(config?.GetValue("GuideControllerIndex") as int? ??0,0,3);
   var slots=ControllerNative.Slots();if(!slots.Contains(source)||slots.Any(s=>s!=source&&s!=VirtualGuide.OwnIndex))throw new InvalidOperationException("An additional controller or input emulator is active. It is protected from automatic capture.");
   if(!ControllerNative.Read(source,out var initial)||!ControllerNative.Neutral(initial))throw new ControllerInputNotNeutralException("Release all controller buttons and center both sticks; Virtual Controller will resume automatically");
   routeTrace?.Point("source-validated",source);
   routeTrace?.Slots(source,"before-output");
   ControllerLoginRecovery.Ensure();routeTrace?.Point("output-create");
   pad=outputFactory(source,()=>Volatile.Read(ref policy).Vibration??new ControllerVibration());pad.Submit(new());Volatile.Write(ref ownSlot,pad.Slot);
   routeTrace?.Point("output-created",pad.Slot,pad.Target);
   lease=await ControllerCaptureLease.Acquire(reports,builtIn);
   routeTrace?.Point("lease-active");
   routeTrace?.Slots(source,"after-hide");
   challengedSource=source;int beforeFeedback=pad.ProbeFeedbackCount;int observerSlot=-1;if(pad.Target=="DualShock4"){await ControllerDs4Observer.Confirm(reports,pad.Serial!);}else{var visible=await Observe(reports,true);if(visible.Length!=1)throw new IOException("Only our virtual Xbox output may remain visible");observerSlot=visible[0];}for(int n=0;n<30&&pad.ProbeFeedbackCount==beforeFeedback;n++)await Task.Delay(10);if(pad.ProbeFeedbackCount<=beforeFeedback)throw new IOException("The independent controller identity challenge failed; physical input was restored");pad.EnableRumbleForwarding();
   stopping=new();var captured=pad;var owned=lease;pad=null;lease=null;var token=stopping.Token;Volatile.Write(ref currentOutput,captured);Volatile.Write(ref state,new(true,false,source,captured.Slot,null,[],0,ObserverVirtualSlot:observerSlot,OutputTarget:captured.Target));
   var recorder=motionRecorder??=new KeyboardMouseMotionRecorder(reports);
   worker=Task.Run(async()=>
   {
    string? error=null;long frames=0;var translator=new ControllerTranslator();var keyboardMouse=new KeyboardMouseTranslator();var gyroRouter=new ControllerGyroRouter();var ds4Navigation=new ControllerDs4NavigationRelay();IControllerGyroStream? gyroInput=null;using var emitter=new ControllerInputEmitter((keys,mouse)=>owned.Input(keys,mouse));using var cursorCadence=new ControllerCursorCadence();ControllerExtraKeys? extras=null;long started=Stopwatch.GetTimestamp(),previous=started;ControllerFrame? last=null;int lastRevision=-1;bool lastOverlay=false,lastRedirected=false;
    var heartbeat=new ControllerLeaseHeartbeat(owned.Touch);
    using var desktopPriority=new KeyboardMousePriorityLease();
    KeyboardMouseMotionPump? desktopMotion=null;
    KeyboardMousePolicy? lastKeyboardPolicy=null;
    try
    {
     routeTrace?.Point("worker-started");
     routeTrace?.StartPhysicalReader(builtIn);
     gyroInput=gyroFactory();extras=new ControllerExtraKeys(()=>Volatile.Read(ref policy),()=>oemButtons?.OwnsAliases==true);
     while(!token.IsCancellationRequested)
     {
      long ticks=Stopwatch.GetTimestamp(),now=(long)((ticks-started)*1000.0/Stopwatch.Frequency);if((ticks-previous)/(double)Stopwatch.Frequency>1.5)throw new IOException("Controller remapping paused after sleep. Enable it again when ready.");double seconds=Math.Clamp((ticks-previous)/(double)Stopwatch.Frequency,0,.05);previous=ticks;
      var motionTrace=recorder.Current;
      bool measure=motionTrace!=null;
      long desktopCheckStarted=measure?Stopwatch.GetTimestamp():0;
      long published=0,captureCompleted=0,emitCompleted=0;
      if(!ControllerInputDesktop.Available())throw new IOException("Windows switched to a secure screen; physical controller input was restored");
      long readStarted=measure?Stopwatch.GetTimestamp():0;
      if(!ControllerNative.Read(source,out var frame,out uint packet))throw new IOException("The physical controller disconnected. Its normal input was restored.");
      long sampledAt=Stopwatch.GetTimestamp();
      frame=frame with{Extras=(ushort)(oemButtons?.OwnsAliases==true?extras.Mask&0xFFF0:extras.Mask),Oem=oemButtons?.RemapMask??0};var current=Volatile.Read(ref policy);int generation=Volatile.Read(ref revision);bool motionTest=motionBusy();
      if(motionTest){var previousDiagnostic=Volatile.Read(ref diagnosticInput);if(previousDiagnostic==null||Stopwatch.GetElapsedTime(previousDiagnostic.Timestamp).TotalMilliseconds>=40)Volatile.Write(ref diagnosticInput,new(sampledAt,source,packet,ControllerAxes.From(frame)));}
      long gameBarCheckStarted=measure?Stopwatch.GetTimestamp():0;
      bool redirected=NativeGameBarState.IsInputRedirected();
      long gameBarCheckCompleted=measure?Stopwatch.GetTimestamp():0;
      routeTrace?.Input(frame,packet,redirected,source);
      bool overlay=redirected||motionTest;var gyro=current.Gyro??new();bool gyroPaused=overlay||current.KeyboardMouse!=null;captured.ConfigureLighting(current.ForwardGameLighting&&current.KeyboardMouse==null,overlay,current.LightingContext);gyroInput.Configure((gyro.Enabled||current.ForwardMotion&&captured.Target=="DualShock4")&&!gyroPaused);
      if(last!=frame||generation!=lastRevision||overlay!=lastOverlay||current.MouseMode||current.KeyboardMouse!=null||translator.NeedsTick||gyro.Enabled||ds4Navigation.NeedsTick||redirected!=lastRedirected||current.ForwardMotion&&captured.Target=="DualShock4")
      {
       if(generation!=lastRevision)
       {
        bool tuningOnly=current.KeyboardMouse is {} next&&lastKeyboardPolicy!=null&&next.SameBindings(lastKeyboardPolicy);
        if(!tuningOnly){emitter.Dispose();translator.Reset();keyboardMouse.Reset();gyroRouter.Reset();ds4Navigation.Reset();captured.RefreshVibration();}
       }
       var output=current.KeyboardMouse is {} km?keyboardMouse.Translate(frame,km,seconds,now,oemButtons?.HeldMask??0,overlay):translator.Translate(frame,current,overlay,now);var sensor=gyroInput.Read();var motionCalibration=calibration();output=gyroRouter.Apply(output,frame,gyro,sensor,motionCalibration,(ushort)(oemButtons?.OwnsAliases==true?oemButtons.HeldMask:0),(current.Tuning??new()).TriggerThreshold,gyroPaused,now,seconds);output=ds4Navigation.Apply(output,captured.Target=="DualShock4"&&redirected,now);
       var forwardedMotion=ControllerDs4Motion.Read(sensor,motionCalibration,current.ForwardMotion&&captured.Target=="DualShock4"&&!gyroPaused);
       if(current.KeyboardMouse is {} mousePolicy)
       {
        if(desktopMotion==null)
        {
         desktopPriority.Enter();
         desktopMotion=new KeyboardMouseMotionPump(recorder:recorder);
        }
        if(!overlay)motionTrace?.ObserveIntent(output,mousePolicy,sampledAt);
        desktopMotion.Publish(output,sampledAt);
        if(motionTrace?.IsRecording==true)
        {
         published=Stopwatch.GetTimestamp();
         motionTrace.Sample(desktopMotion.Segment,ticks,desktopCheckStarted,readStarted,sampledAt,gameBarCheckStarted,
          gameBarCheckCompleted,published,packet,frame,output,overlay,generation,seconds);
        }
       }
       else if(desktopMotion!=null){desktopPriority.Leave();desktopMotion.Dispose();desktopMotion=null;}
       captured.Submit(output.Pad,output.Guide,output.TouchpadClick,forwardedMotion,skipDuplicates:output.DesktopCursor,contacts:output.TouchpadContacts);
       routeTrace?.Submitted(output.Pad);
       if(published!=0)captureCompleted=Stopwatch.GetTimestamp();
       emitter.Apply(output,seconds,continuous:desktopMotion==null);frames++;
       if(published!=0)emitCompleted=Stopwatch.GetTimestamp();
       Volatile.Write(ref state,new(true,false,source,captured.Slot,null,output.Pressed,frames,output.Pressed.Length>0?string.Join(" + ",output.Pressed):Read().LastInput,observerSlot,new(gyro.Enabled,gyroRouter.Active,gyro.Target,gyroPaused?"Paused":!gyro.Enabled?"Disabled":sensor.Error??gyroRouter.BlockedReason??(gyroRouter.Active?"Active":sensor.Ready&&sensor.GyroAgeMilliseconds<=500?"Waiting for activation":"Waiting for motion"),sensor.Sequence),captured.Target,ControllerDs4Motion.Status(current.ForwardMotion,captured.Target=="DualShock4",gyroPaused,sensor),captured.LightingStatus));
       last=frame;lastRevision=generation;lastOverlay=overlay;lastRedirected=redirected;lastKeyboardPolicy=current.KeyboardMouse;
      }
      if(Read().LightingForwarding!=captured.LightingStatus)Volatile.Write(ref state,Read() with{LightingForwarding=captured.LightingStatus});
      heartbeat.Tick(now);
      long waitStarted=published!=0?Stopwatch.GetTimestamp():0;
      bool waited=cursorCadence.WaitForNext(token,current.KeyboardMouse!=null?4:0,ticks);
      if(waitStarted!=0)
       motionTrace!.SourcePhases(published,captureCompleted,emitCompleted,waitStarted,Stopwatch.GetTimestamp(),cursorCadence.NextDeadline,waited);
      if(!waited)await Task.Delay(8,token);
     }
    }
    catch(OperationCanceledException){}catch(Exception e){error=e.Message;try{string user=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);File.WriteAllText(Path.Combine(reports,"controller-runtime-error.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,type=e.GetType().Name,detail=e.ToString().Replace(user,"<user>",StringComparison.OrdinalIgnoreCase),frames},Backend.Program.Json));}catch{}}
    finally
    {
     routeTrace?.Point("worker-stop",detail:error);routeTrace?.Dispose();
     desktopPriority.Leave();desktopMotion?.Dispose();Interlocked.CompareExchange(ref currentOutput,null,captured);extras?.Dispose();emitter.Dispose();try{ControllerNative.Rumble(source,0,0);}catch{}try{captured.Dispose();}catch{}Volatile.Write(ref ownSlot,-1);
     try{await heartbeat.AwaitPending();}catch(Exception e){error??=e.Message;}
     try{owned.Dispose();}catch(Exception e){error="Physical controller recovery: "+e.Message;}
     Volatile.Write(ref state,new(false,false,source,-1,error,[],frames));if(gyroInput!=null)try{await gyroInput.DisposeAsync();}catch{/* Physical input was already restored above. */}
    }
   });
  }
  catch(Exception e){routeTrace?.Point("capture-failed",detail:e.Message);routeTrace?.Dispose();try{if(challengedSource>=0)ControllerNative.Rumble(challengedSource,0,0);}catch{}try{pad?.Dispose();}catch{}try{lease?.Dispose();}catch{}Volatile.Write(ref ownSlot,-1);Volatile.Write(ref state,new(false,false,-1,-1,e.Message,[],0));throw;}
  finally{gate.Release();}
 }
 internal static async Task<int[]> Observe(string reports,bool challenge=false)
 {
  string image=Path.Combine(AppContext.BaseDirectory,"ONEXTweaks.ControllerObserver.exe");if(!File.Exists(image))throw new FileNotFoundException("The independent controller visibility observer is missing");
  string file=Path.Combine(reports,"..","privateReports","controller-visibility-"+Guid.NewGuid().ToString("N")+".json");Directory.CreateDirectory(Path.GetDirectoryName(file)!);
  try{var start=new ProcessStartInfo(image){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--controller-visibility-probe");start.ArgumentList.Add(file);if(challenge)start.ArgumentList.Add("--challenge-owned-virtual");using var process=Process.Start(start)??throw new IOException("The controller visibility observer did not start");using var deadline=new CancellationTokenSource(5000);try{await process.WaitForExitAsync(deadline.Token);}catch(OperationCanceledException){process.Kill();throw new IOException("The controller visibility observer timed out");}if(process.ExitCode!=0){string reason="";try{using var failed=JsonDocument.Parse(await File.ReadAllTextAsync(file));reason=" ("+failed.RootElement.GetProperty("kind").GetString()+", native code "+failed.RootElement.GetProperty("nativeCode")+")";}catch{}throw new IOException("The controller visibility observer failed"+reason);}using var json=JsonDocument.Parse(await File.ReadAllTextAsync(file));if(json.RootElement.GetProperty("whitelisted").GetBoolean()||json.RootElement.GetProperty("pid").GetInt32()==Environment.ProcessId)throw new IOException("The independent observer is unexpectedly exempt from hiding");return json.RootElement.GetProperty("slots").EnumerateArray().Select(x=>x.GetInt32()).ToArray();}
  finally{if(File.Exists(file))File.Delete(file);}
 }
 internal async Task Stop(){await gate.WaitAsync();try{stopping?.Cancel();if(worker!=null)await worker;stopping?.Dispose();stopping=null;worker=null;}finally{gate.Release();}}
 public async ValueTask DisposeAsync(){await Stop();if(motionRecorder!=null)await motionRecorder.DisposeAsync();gate.Dispose();}
}

/// <summary>Temporarily restores ordinary process scheduling for interactive desktop input.</summary>
internal sealed class KeyboardMousePriorityLease(Func<ProcessPriorityClass> read,Action<ProcessPriorityClass> write) : IDisposable
{
 private bool active,ownsChange;

 internal KeyboardMousePriorityLease() : this(ReadProcessPriority,WriteProcessPriority) { }

 private static ProcessPriorityClass ReadProcessPriority()
 {
  using var process=Process.GetCurrentProcess();
  return process.PriorityClass;
 }

 private static void WriteProcessPriority(ProcessPriorityClass priority)
 {
  using var process=Process.GetCurrentProcess();
  process.PriorityClass=priority;
 }

 private static bool Unavailable(Exception error) => error is System.ComponentModel.Win32Exception
  or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or System.Security.SecurityException;

 internal void Enter()
 {
  if(active)return;
  active=true;
  try
  {
   var current=read();
   if(current!=ProcessPriorityClass.Normal)ownsChange=false;
   if(current!=ProcessPriorityClass.BelowNormal)return;
   write(ProcessPriorityClass.Normal);
   ownsChange=true;
  }
  catch(Exception error) when(Unavailable(error)) { }
 }

 internal void Leave()
 {
  active=false;
  if(!ownsChange)return;
  try
  {
   if(read()==ProcessPriorityClass.Normal)write(ProcessPriorityClass.BelowNormal);
   ownsChange=false;
  }
  catch(Exception error) when(Unavailable(error))
  {
   // Keep ownership so worker cleanup can retry a transient restoration failure.
  }
 }

 public void Dispose()=>Leave();
}

internal sealed class ControllerLeaseHeartbeat(Action renew)
{
 private Task? pending;
 private long due;
 internal void Tick(long now)
 {
  if(pending is {IsCompleted:true}){pending.GetAwaiter().GetResult();pending=null;}
  if(now<due||pending!=null)return;
  // The controller loop remains the source of renewals. A stalled loop cannot
  // keep the lease alive indefinitely, and there is at most one write in flight.
  pending=Task.Run(renew);due=now+900;
 }
 internal Task AwaitPending()=>pending??Task.CompletedTask;
}
