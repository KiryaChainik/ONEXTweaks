// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;

/// <summary>Isolated physical-pixel/cadence measurements; never changes owner policy or captures a controller.</summary>
internal static class KeyboardMouseCursorVerification
{
 [StructLayout(LayoutKind.Sequential)]private struct Point{internal int X,Y;}
 [DllImport("user32.dll")]private static extern bool GetPhysicalCursorPos(out Point point);
 [DllImport("user32.dll")]private static extern bool SetPhysicalCursorPos(int x,int y);
 [DllImport("user32.dll")]private static extern bool SetProcessDPIAware();
 [DllImport("user32.dll",EntryPoint="SystemParametersInfoW")]private static extern bool ReadMouse(uint action,uint param,[Out]int[] values,uint flags);
 [DllImport("user32.dll",EntryPoint="SystemParametersInfoW")]private static extern bool ReadSpeed(uint action,uint param,ref int value,uint flags);
 private static Point Read(){if(!GetPhysicalCursorPos(out var point))throw new IOException("Physical cursor position unavailable");return point;}
 private static double Percentile(List<double> data,double fraction){var sorted=data.Order().ToArray();return sorted.Length==0?0:sorted[(int)Math.Clamp(Math.Ceiling(sorted.Length*fraction)-1,0,sorted.Length-1)];}
 private static readonly (short X,short Y,int Duration)[] StartRamps=[(9000,8700,100),(14500,8700,110),(-15500,11200,125),(18000,-17000,160)];
 private static ControllerFrame Ramp((short X,short Y,int Duration) target,double elapsed)
 {
  double travel=Math.Clamp(elapsed/target.Duration,0,1);
  return new(Rx:(short)(target.X*travel),Ry:(short)(target.Y*travel));
 }
 internal static async Task<int> Models(string file)
 {
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  try{
   foreach(int sign in new[]{-1,1})foreach(double speed in new[]{.01,.96,4,8}){
    var a=new DesktopCursorMotion();var b=new DesktopCursorMotion();int ax=0,ay=0,bx=0,by=0;
    for(int i=0;i<125;i++){var d=a.Next((short)(sign*15000),(short)(sign*8000),speed,.008);ax+=d.X;ay+=d.Y;}
    for(int i=0;i<500;i++){var d=b.Next((short)(sign*15000),(short)(sign*8000),speed,.002);bx+=d.X;by+=d.Y;}
    Check(Math.Abs(ax-bx)<=1&&Math.Abs(ay-by)<=1,"Desktop velocity depends on refresh cadence");
    Check(Math.Abs(ax-sign*15000/32768.0*650*speed)<1.01&&Math.Abs(ay+sign*8000/32768.0*650*speed)<1.01,"Physical pixel velocity integration changed");
   }
   var motion=new DesktopCursorMotion();motion.Next(2000,0,1,.01);Check(motion.Next(0,0,1,.01)==(0,0),"Neutral cursor drifts");
   Check(motion.Next(-2000,0,1,.01).X==0,"Reversal replayed a positive fraction");motion.Reset();Check(motion.Next(0,0,1,.01)==(0,0),"Cursor reset retained motion");
   Check(motion.Next(32767,32767,double.NaN,.01)==(0,0)&&motion.Next(32767,32767,1,double.PositiveInfinity)==(0,0),"Invalid clock/speed emitted motion");
   Check(motion.Next(32767,0,1,1)==(0,0),"A stale sample produced a cursor leap");
   motion.Reset();int irregularX=0,steadyX=0;
   foreach(int ms in new[]{3,7,5,15,20})irregularX+=motion.Next(20000,12000,1,ms/1000.0).X;
   motion.Reset();
   for(int i=0;i<10;i++)steadyX+=motion.Next(20000,12000,1,.005).X;
   Check(Math.Abs(irregularX-steadyX)<=1,"A normal scheduler delay discarded cursor distance");
   var heldDiagonal=new ControllerOutput(new(),[],[],0,25000,19500,[],CursorSpeedScale:4,DesktopCursor:true);
   var sustained=new DesktopCursorMotion();var paceReference=new DesktopCursorMotion();
   (int X,int Y) IntegrateReference(double elapsed)=>paceReference.Next(heldDiagonal.CursorX,heldDiagonal.CursorY,heldDiagonal.CursorSpeedScale,elapsed);
   for(int i=0;i<200;i++)Check(sustained.NextFresh(heldDiagonal,i%2==0?.003:.005)==IntegrateReference(i%2==0?.003:.005),
    "Short timing variation changed the steady cursor response");
   var unrestricted=IntegrateReference(.020);
   var bounded=sustained.NextFresh(heldDiagonal,.020);
   Check(unrestricted.X==40&&bounded.X is >=15 and <=17&&bounded.Y is >=-13 and <=-11,
    "The first move after a pause used more than two periods or changed diagonal direction");
   var ordinary=sustained.NextFresh(heldDiagonal,.004);
   Check(ordinary.X is >=7 and <=9&&ordinary.Y is >=-7 and <=-5,"A pause left cursor distance to catch up later");
   var rightAndDown=heldDiagonal with{CursorY=-19500};
   var resumed=sustained.NextFresh(rightAndDown,.050);
   Check(resumed.X is >=15 and <=17&&resumed.Y is >=11 and <=13,
    "A fresh sample after a long pause stalled or moved in the old direction");
   motion.Reset();
   int spread=0,previousStep=0;
   for(int i=0;i<140;i++)
   {
    int step=motion.Next((short)(i%2==0?23000:14500),0,4,.004).X;
    if(i>10)spread=Math.Max(spread,Math.Abs(step-previousStep));
    previousStep=step;
   }
   Check(spread<=2,"Small velocity changes caused large cursor step differences");
   Check(motion.Next(0,0,4,.004)==(0,0)&&motion.Next(-32768,0,4,.004).X<0,"Filtering delayed stop or reversal");
   foreach(int sign in new[]{-1,1})
   {
    short held=(short)(sign*12000);
    motion.Reset();
    var reference=new DesktopCursorMotion();
    for(int i=0;i<100;i++)
    {
     motion.Next(24000,held,1,.004);
     reference.Next(24000,held,1,.004);
    }
    for(int i=0;i<12;i++)
    {
     var reversed=motion.Next(24000,(short)-held,1,.004);
     var straight=reference.Next(24000,held,1,.004);
     Check(reversed.Y*sign>=0&&(i!=0||reversed.Y*sign>0)&&reversed.X==straight.X,
      "Reversing one axis on a diagonal delayed the turn or disturbed the held axis");
    }
    for(int i=0;i<12;i++)
    {
     var straightened=motion.Next(24000,0,1,.004);
     var straight=reference.Next(24000,held,1,.004);
     Check(straightened.Y==0&&straightened.X==straight.X,"Straightening a diagonal replayed movement on the released axis");
    }
   }
   foreach(double milliseconds in new[]{4,5,1000.0/120,1000.0/144})
   {
    long period=(long)Math.Round(milliseconds*Stopwatch.Frequency/1000);
    var deadline=new KeyboardMouseMotionPump.Deadline();
    long start=period*10;
    Check(deadline.Take(start,milliseconds)>0&&deadline.Next==start+period,"An on-time motion deadline changed phase");
    long slightlyLate=deadline.Next+period/8;
    Check(deadline.Take(slightlyLate,milliseconds)>0&&deadline.Next==start+2*period,"Small wake jitter shifted the motion cadence");
    long late=deadline.Next+7*period/8;
    Check(deadline.Take(late,milliseconds)>0&&deadline.Next==late+period,"A late wake left a near-immediate catch-up event");
    Check(deadline.Take(late+period/8,milliseconds)==0&&deadline.Take(late+period,milliseconds)>0,"Motion replayed a skipped deadline or failed to resume");
   }
   foreach(int origin in new[]{-3840,0,1920})foreach(int extent in new[]{1920,2560,3840,7680})foreach(int offset in new[]{0,1,17,479,1023,1918,1919}){
    long pixel=origin+Math.Clamp(offset,0,extent-1);int normalized=DesktopCursorMotion.PixelCoordinate(pixel,origin,extent);
    Check(origin+(int)Math.Floor(normalized*extent/65536.0)==pixel,"Absolute normalization missed the physical pixel centre");
   }
   var policy=new KeyboardMousePolicy();var tx=new KeyboardMouseTranslator();tx.Translate(new(),policy,.008,0);var output=tx.Translate(new(Rx:15000),policy,.008,8);
   Check(output.DesktopCursor&&output.CursorX>0&&output.DesktopInputInterval==ControllerOutput.DesktopCursorMilliseconds,"Keyboard/mouse did not select its fixed cursor cadence");
   Check(!tx.Translate(new(Rx:15000),policy,.008,16,overlayOpen:true).DesktopCursor,"Overlay pause retained desktop cursor motion");
   Check(!new ControllerOutput(new(),[],[],0,0,0,[]).DesktopCursor,"Gamepad/gyro default mouse transport changed");
   Check(new KeyboardMouseTranslator().Translate(new(),policy,0,0).DesktopInputInterval==4,"Idle desktop input did not retain the precise polling cadence");
   tx.Reset();tx.Translate(new(),policy,0,0);
   var medium=tx.Translate(new(Ly:19000),policy,.004,4);
   var fast=tx.Translate(new(Ly:32767),policy,.004,8);
   Check(!medium.FastDesktopScroll&&fast.FastDesktopScroll,"Display pacing was not restricted to strong, fast scrolling");
   foreach(int hz in new[]{120,144})
   {
    double period=1.0/hz;
    var normalRate=new KeyboardMouseWheelMotion();
    int sum=0;
    for(int tick=0;tick<hz;tick++)sum+=normalRate.Next(fast,period,period).Vertical;
    Check(Math.Abs(sum-fast.VerticalWheelRate)<1.01,"The fast wheel limit changed the steady requested speed");
    int limit=(int)Math.Ceiling(Math.Abs(fast.VerticalWheelRate)*2*period);
    foreach(double lateBy in new[]{.020,.0319,.032,.0321,.052,.086})
    {
     var recovered=new KeyboardMouseWheelMotion();
     int emitted=Math.Abs(recovered.Next(fast,lateBy,period).Vertical);
     int after=Math.Abs(recovered.Next(fast,period,period).Vertical);
     Check(emitted>0&&emitted<=limit&&after<=Math.Ceiling(Math.Abs(fast.VerticalWheelRate)*period),
      "Late fast wheel output vanished, sent a burst, or carried debt into the next event");
     recovered.Reset();
     Check(recovered.Next(fast with{VerticalWheelRate=0},period,period)==(0,0),
      "Neutral fast wheel retained movement");
    }
    Check(new KeyboardMouseWheelMotion().Next(medium,.052,period)==(0,0),
     "Late output changed the non-fast wheel path");
   }
   foreach(int refresh in new[]{0,60,100,120,144,240})
   {
    double expected=refresh is 120 or 144?1000.0/refresh:ControllerOutput.DesktopWheelMilliseconds;
    Check(KeyboardMouseWheelCadence.Interval(fast,refresh)==expected,"Fast scroll used an unvalidated display interval");
    Check(KeyboardMouseWheelCadence.Interval(medium,refresh)==ControllerOutput.DesktopWheelMilliseconds,
     "Display pacing changed moderate scroll");
   }
   tx.Reset();var slowPolicy=policy with{ScrollSpeed=50};
   tx.Translate(new(),slowPolicy,0,0);
   Check(!tx.Translate(new(Ly:32767),slowPolicy,.004,4).FastDesktopScroll,"Display pacing changed the slow user speed");
   tx.Reset();tx.Translate(new(),policy,0,0);
   int lastSpeed=0;
   for(int raw=0;raw<=32767;raw+=31)
   {
    var intent=tx.Translate(new(Rx:(short)raw),policy,.004,4);
    Check(intent.CursorX>=lastSpeed,"The accelerated cursor slowed as stick deflection increased");
    Check(raw>3276||intent.CursorX==0,"The cursor curve moved inside the saved deadzone");
    lastSpeed=intent.CursorX;
   }
   var atEdge=tx.Translate(new(Rx:32767),policy,.004,8);
   Check(atEdge.CursorX>=32765,"The cursor lost its full-deflection speed");
   VerifyPriorityLease(Check);
   await VerifyRecorder(policy,fast,output,Check);
   foreach(var target in StartRamps)
   {
    var rampPolicy=policy with{MouseSpeed=98};
    tx.Reset();tx.Translate(new(),rampPolicy,0,0);motion.Reset();
    double oldVelocityX=0,oldVelocityY=0,oldX=0,oldY=0;
    int previousFirst=0,candidateFirst=0;
    for(int time=2;time<=240;time+=2)
    {
     var sample=tx.Translate(Ramp(target,time),rampPolicy,.002,time);
     double velocityScale=650*sample.CursorSpeedScale/32768;
     double oldWeight=oldVelocityX==0&&oldVelocityY==0?1:1-Math.Exp(-.002/.030);
     oldVelocityX+=(sample.CursorX*velocityScale-oldVelocityX)*oldWeight;
     oldVelocityY+=(sample.CursorY*velocityScale-oldVelocityY)*oldWeight;
     oldX+=oldVelocityX*.002;oldY+=oldVelocityY*.002;
     if(previousFirst==0&&(Math.Abs(oldX)>.5||Math.Abs(oldY)>.5))previousFirst=time;
     if(motion.Next(sample.CursorX,sample.CursorY,sample.CursorSpeedScale,.002)!=(0,0)&&candidateFirst==0)candidateFirst=time;
    }
    Check(candidateFirst>0&&previousFirst-candidateFirst>=10,"Diagonal start latency did not improve for a gradual stick ramp");
   }
   tx.Reset();tx.Translate(new(),policy,0,0);motion.Reset();
   var fine=tx.Translate(new(Rx:3476,Ry:3476),policy,.004,4);
   int firstFine=0;
   for(int time=4;time<=120;time+=4)
   {
    var step=motion.Next(fine.CursorX,fine.CursorY,fine.CursorSpeedScale,.004);
    if(step==(0,0))continue;
    Check(step.X==1&&step.Y==-1,"Fine 45-degree movement started in only one axis");
    firstFine=time;break;
   }
   Check(firstFine is >0 and <=60,"Fine diagonal cursor movement took too long to emit a pixel");
   foreach(bool acceleration in new[]{true,false})
   foreach(var (rx,ry) in new (short,short)[]{(21000,11000),(11000,21000),(-21000,11000),(21000,-11000)})
   {
    var cursorPolicy=policy with{MouseAcceleration=acceleration};
    tx.Reset();tx.Translate(new(),cursorPolicy,.002,0);
    var diagonal=tx.Translate(new(Rx:rx,Ry:ry),cursorPolicy,.002,2);
    Check(Math.Abs(diagonal.CursorY/(double)diagonal.CursorX-ry/(double)rx)<.001,"The acceleration curve bent a diagonal trajectory");
    int x=0,y=0;motion.Reset();
    for(int i=0;i<180;i++)
    {
     var d=motion.Next(diagonal.CursorX,diagonal.CursorY,1,.002);x+=d.X;y+=d.Y;
     double distance=Math.Abs(x*(double)ry+y*(double)rx)/Math.Sqrt((double)rx*rx+(double)ry*ry);
     Check(distance<1,"Subpixel rounding moved the cursor away from its diagonal path");
    }
   }
   var mixed=policy with{Bindings=new(){{"RSUp",new("keyboard",Keys:[38])}}};
   tx.Reset();tx.Translate(new(),mixed,.002,0);
   Check(tx.Translate(new(Rx:20000,Ry:13000),mixed,.002,2) is {CursorY:0} custom&&custom.Keys.Contains(38),"A mixed cursor/key stick binding was displaced by radial handling");
   using var release=new ManualResetEventSlim();
   var heartbeatStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
   var heartbeat=new ControllerLeaseHeartbeat(()=>{heartbeatStarted.TrySetResult();release.Wait(TimeSpan.FromSeconds(3));});
   heartbeat.Tick(0);
   try
   {
    await heartbeatStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
    var pending=heartbeat.AwaitPending();heartbeat.Tick(901);
    Check(!pending.IsCompleted&&ReferenceEquals(pending,heartbeat.AwaitPending()),"A slow recovery heartbeat blocked or queued another write");
   }
   finally{release.Set();await heartbeat.AwaitPending();}
   using(var guardEntered=new ManualResetEventSlim())
   using(var releaseGuard=new ManualResetEventSlim())
   {
    int movements=0;
    using(var pump=new KeyboardMouseMotionPump((_,_)=>Interlocked.Increment(ref movements),(_,_)=>{},()=>{
     guardEntered.Set();
     return releaseGuard.Wait(TimeSpan.FromSeconds(5));
    }))
    {
     Task? clear=null;
     try
     {
      var active=new ControllerOutput(new(),[],[],0,25000,13000,[],CursorSpeedScale:2,DesktopCursor:true);
      pump.Publish(active,Stopwatch.GetTimestamp());
      Check(guardEntered.Wait(TimeSpan.FromSeconds(2)),"The motion worker never entered the desktop guard");
      var neutral=new ControllerOutput(new(),[],[],0,0,0,[]);
      clear=Task.Run(()=>pump.Publish(neutral,Stopwatch.GetTimestamp()));
      await clear.WaitAsync(TimeSpan.FromSeconds(1));
     }
     finally{releaseGuard.Set();if(clear!=null)await clear;}
     await Task.Delay(15);
    }
    Check(Volatile.Read(ref movements)==0,"A guard in flight emitted motion after input had been cleared");
   }
   using(var sendEntered=new ManualResetEventSlim())
   using(var releaseSend=new ManualResetEventSlim())
   {
    int wheelEvents=0;
    using(var pump=new KeyboardMouseMotionPump((_,_)=>{
     sendEntered.Set();
     if(!releaseSend.Wait(TimeSpan.FromSeconds(5)))throw new IOException("Test send was never released");
    },(_,_)=>Interlocked.Increment(ref wheelEvents),()=>true))
    {
     Task? clear=null;
     try
     {
      var combined=new ControllerOutput(new(),[],[],0,25000,13000,[],CursorSpeedScale:2,
       DesktopCursor:true,VerticalWheelRate:960,ScrollIntervalMilliseconds:5);
      pump.Publish(combined,Stopwatch.GetTimestamp());
      Check(sendEntered.Wait(TimeSpan.FromSeconds(2)),"The motion worker never reached Windows output");
      var neutral=new ControllerOutput(new(),[],[],0,0,0,[]);
      clear=Task.Run(()=>pump.Publish(neutral,Stopwatch.GetTimestamp()));
      await clear.WaitAsync(TimeSpan.FromSeconds(1));
     }
     finally{releaseSend.Set();if(clear!=null)await clear;}
     await Task.Delay(15);
    }
    Check(Volatile.Read(ref wheelEvents)==0,"A prepared wheel step leaked after a stalled cursor send and neutral input");
   }
   var emission=new System.Collections.Concurrent.ConcurrentQueue<double>();
   using(var pump=new KeyboardMouseMotionPump((_,_)=>emission.Enqueue(Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency),(_,_)=>{},()=>true))
   {
    var target=new ControllerOutput(new(),[],[],0,25000,13000,[],CursorSpeedScale:2,DesktopCursor:true);
    for(int i=0;i<12;i++){pump.Publish(target,Stopwatch.GetTimestamp());await Task.Delay(3);}
    int beforeGap=emission.Count;
    await Task.Delay(14);
    Check(emission.Count-beforeGap>=2,"A short source-side stall froze desktop motion");
    await Task.Delay(85);int expired=emission.Count;
    await Task.Delay(30);
    Check(expired==emission.Count,"The desktop thread emitted stale input after the source stopped");
   }
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,settingsWrites=0,physicalOwnerInputTested=false,
    cursorRecovery=new{unrestrictedX=unrestricted.X,boundedX=bounded.X,ordinaryX=ordinary.X,resumedX=resumed.X,resumedY=resumed.Y}},Backend.Program.Json));return 0;
  }catch(Exception error){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=error.Message,modelOnly=true},Backend.Program.Json));return 2;}
 }
 private static void VerifyPriorityLease(Action<bool,string> check)
 {
  var current=ProcessPriorityClass.BelowNormal;
  int reads=0,writes=0;
  bool failRead=false,failWrite=false;
  ProcessPriorityClass ReadClass()
  {
   reads++;
   return failRead?throw new System.ComponentModel.Win32Exception(5):current;
  }
  void WriteClass(ProcessPriorityClass value)
  {
   writes++;
   if(failWrite)throw new System.ComponentModel.Win32Exception(5);
   current=value;
  }
  using var lease=new KeyboardMousePriorityLease(ReadClass,WriteClass);
  lease.Enter();lease.Enter();
  check(current==ProcessPriorityClass.Normal&&reads==1&&writes==1,"Repeated desktop input re-applied scheduling");
  lease.Leave();lease.Leave();
  check(current==ProcessPriorityClass.BelowNormal&&reads==2&&writes==2,"Leaving keyboard/mouse did not restore its original scheduling");
  foreach(var original in new[]{ProcessPriorityClass.Normal,ProcessPriorityClass.High,ProcessPriorityClass.Idle})
  {
   current=original;
   lease.Enter();lease.Leave();
   check(current==original&&writes==2,"A process priority chosen outside keyboard/mouse mode changed");
  }
  current=ProcessPriorityClass.BelowNormal;
  lease.Enter();
  current=ProcessPriorityClass.AboveNormal;
  lease.Leave();
  check(current==ProcessPriorityClass.AboveNormal&&writes==3,"Leaving keyboard/mouse overwrote an external priority change");

  current=ProcessPriorityClass.BelowNormal;
  failRead=true;
  lease.Enter();lease.Leave();
  check(current==ProcessPriorityClass.BelowNormal&&writes==3,"A failed query changed the process priority");
  failRead=false;failWrite=true;
  lease.Enter();lease.Leave();
  check(current==ProcessPriorityClass.BelowNormal&&writes==4,"A failed priority change claimed ownership");
  failWrite=false;
  lease.Enter();
  failWrite=true;
  lease.Leave();
  failWrite=false;
  lease.Dispose();
  check(current==ProcessPriorityClass.BelowNormal&&writes==7,"Worker cleanup did not retry an owned restoration");
 }

 private static async Task VerifyRecorder(KeyboardMousePolicy policy,ControllerOutput wheel,ControllerOutput cursor,Action<bool,string> check)
 {
  string root=Path.Combine(Path.GetTempPath(),"ONEXTweaks-trace-model-"+Guid.NewGuid().ToString("N"));
  string directory=Directory.CreateDirectory(Path.Combine(root,"privateReports")).FullName;
  string reports=Directory.CreateDirectory(Path.Combine(root,"Reports")).FullName;
  string request=Path.Combine(directory,"keyboard-mouse-motion-trace.request");
  string NewReceipt(out string id)
  {
   id=Guid.NewGuid().ToString("N");
   return Path.Combine(directory,$"keyboard-mouse-motion-trace-{id}.json");
  }
  static async Task Until(Func<bool> condition)
  {
   var deadline=Stopwatch.StartNew();
   while(!condition())
   {
    if(deadline.Elapsed>TimeSpan.FromSeconds(8))throw new IOException("Motion recorder model timed out");
    await Task.Delay(10);
   }
  }
  var neutral=new ControllerOutput(new(),[],[],0,0,0,[],DesktopCursor:true);
  try
  {
   await using var recorder=new KeyboardMouseMotionRecorder(reports,TimeSpan.FromSeconds(3));
   check(recorder.Current==null,"Recorder ran without a request");
   string receipt=NewReceipt(out string id);
   using(var transition=new KeyboardMouseMotionPump((_,_)=>{},(_,_)=>{},()=>true,recorder))
   {
    await File.WriteAllTextAsync(request,id);
    await Until(()=>recorder.Current!=null);
    var pending=recorder.Current!;
    pending.ObserveIntent(neutral,policy,Stopwatch.GetTimestamp());
    transition.Publish(neutral,Stopwatch.GetTimestamp());
    await Task.Delay(71);
    check(!pending.IsRecording&&File.Exists(receipt+".claimed")&&!File.Exists(receipt+".recording"),
     "A neutral mode transition started the motion window");
   }
   var trace=recorder.Current??throw new IOException("Pump disposal consumed the pending request");
   int packet=0;
   int[] segments=new int[2];
   for(int i=0;i<segments.Length;i++)
   {
    using var emitted=new ManualResetEventSlim();
    using var pump=new KeyboardMouseMotionPump((_,_)=>emitted.Set(),(_,_)=>emitted.Set(),()=>true,recorder);
    segments[i]=pump.Segment;
    var target=i==0?wheel:cursor;
    long at=Stopwatch.GetTimestamp();
    trace.ObserveIntent(target,policy,at);
    pump.Publish(target,at);
    trace.Sample(pump.Segment,at,at,at,at,at,at,at,(uint)++packet,new(Ly:32767,Buttons:16),target,false,1,.004);
    trace.SourcePhases(at,at,at,at,at,at,true);
    await Until(()=>
    {
     if(emitted.IsSet)return true;
     pump.Publish(target,Stopwatch.GetTimestamp());
     return false;
    });
   }
   await Until(()=>File.Exists(receipt));
   using(var doc=JsonDocument.Parse(await File.ReadAllTextAsync(receipt)))
   {
    var data=doc.RootElement;
    var axes=data.GetProperty("axes").EnumerateArray().ToArray();
    var sends=data.GetProperty("mouse").EnumerateArray().Where(x=>x.GetProperty("kind").GetString() is "wheel" or "cursor").ToArray();
    check(data.GetProperty("status").GetString()=="complete"&&data.GetProperty("stopReason").GetString()=="deadline"
     &&File.Exists(receipt+".recording")&&!File.Exists(request),"The recorder did not finish at its motion deadline");
    check(axes.Length==2&&axes[0].GetProperty("ly").GetInt16()==32767&&!axes[0].TryGetProperty("buttons",out _)
     &&data.GetProperty("sourcePhases").GetArrayLength()==2,"Missing source measurements or unexpected controller buttons");
    check(segments[0]!=segments[1]&&segments.SequenceEqual(axes.Select(x=>x.GetProperty("segment").GetInt32()))
     &&segments.All(segment=>sends.Any(row=>row.GetProperty("segment").GetInt32()==segment&&row.GetProperty("threadId").GetUInt32()!=0)),
     "The trace did not follow physical output across a sender replacement");
    check(sends.Any(x=>x.GetProperty("kind").GetString()=="wheel")&&sends.Any(x=>x.GetProperty("kind").GetString()=="cursor"),
     "The recorder lost an actual pump output");
   }
   await Until(()=>recorder.Current==null);
   check(!trace.IsRecording,"Completed motion request stayed active");
   File.WriteAllText(request,id);
   check(KeyboardMouseMotionTrace.Claim(directory,TimeSpan.FromSeconds(1))==null,"A completed request was reusable");
   File.Delete(request);
   // An established pump must also acquire a newly armed recorder.
   using var later=new KeyboardMouseMotionPump((_,_)=>{},(_,_)=>{},()=>true,recorder);
   later.Publish(neutral,Stopwatch.GetTimestamp());
   string overflowReceipt=NewReceipt(out string second);
   await File.WriteAllTextAsync(request,second);
   await Until(()=>recorder.Current!=null);
   var full=recorder.Current!;
   long began=Stopwatch.GetTimestamp();
   full.ObserveIntent(cursor,policy,began);
   later.Publish(cursor,began);
   for(int i=0;i<8193;i++)
    full.Sample(later.Segment,began,began,began,began,began,began,began,(uint)i,new(),cursor,false,1,.004);
   await Until(()=>File.Exists(overflowReceipt));
   using var overflow=JsonDocument.Parse(await File.ReadAllTextAsync(overflowReceipt));
   check(overflow.RootElement.GetProperty("axes").GetArrayLength()==8192&&overflow.RootElement.GetProperty("inputFull").GetBoolean()
    &&overflow.RootElement.GetProperty("status").GetString()=="partial","Recorder exceeded its sample bound or claimed a complete capture");
  }
  finally{Directory.Delete(root,true);}
 }

 internal static async Task<int> Probe(string file)
 {
  var measurements=new List<object>();
  var wheelMeasurements=new List<object>();var startupMeasurements=new List<object>();
  try{
   SetProcessDPIAware();var windowsMouse=new int[3];int windowsSpeed=0;bool mouseRead=ReadMouse(3,0,windowsMouse,0),speedRead=ReadSpeed(0x70,0,ref windowsSpeed,0);
   string reports=Path.GetDirectoryName(file)!;var saved=new KeyboardMouseSettings(reports).Value;
   // Only copy scalar settings into a temporary policy. Custom bindings and all persisted settings stay untouched.
   var policy=new KeyboardMousePolicy(MouseSpeed:saved.MouseSpeed,MouseAcceleration:saved.MouseAcceleration,StickDeadzone:saved.StickDeadzone,HoldAcceleration:0,ScrollSpeed:saved.ScrollSpeed,AdaptiveScroll:saved.AdaptiveScroll).Validate();
   var cadence=new List<double>();var clock=Stopwatch.StartNew();double previous=clock.Elapsed.TotalMilliseconds;
   for(int i=0;i<160;i++){await Task.Delay(8);double now=clock.Elapsed.TotalMilliseconds;cadence.Add(now-previous);previous=now;}
   // Measured before opening the visible sink: a hidden helper must get the precise cadence too.
   using var timer=new ControllerCursorCadence();bool timerAvailable=timer.Available;var preciseCadence=new List<double>();clock.Restart();previous=0;
   for(int i=0;i<160;i++){if(!timer.Wait())await Task.Delay(8);double now=clock.Elapsed.TotalMilliseconds;preciseCadence.Add(now-previous);previous=now;}
   if(!timerAvailable||Percentile(preciseCadence,.5)>=12.5)throw new IOException("Precise hidden-process cursor cadence did not improve");
   using(var canceled=new CancellationTokenSource()){canceled.Cancel();bool rejected=false;try{timer.Wait(canceled.Token);}catch(OperationCanceledException){rejected=true;}if(!rejected)throw new IOException("Cursor wait ignored cancellation");}
   using var window=new ControllerTestWindow(observeWheel:true);window.RequireForeground();var origin=Read();
   foreach(short stick in new short[]{6500,16000,22000,27000,32767})foreach(bool candidate in new[]{false,true})
   {
    window.RequireForeground();if(!SetPhysicalCursorPos(origin.X,origin.Y))throw new IOException("Own test cursor positioning failed");await Task.Delay(50);
    var tx=new KeyboardMouseTranslator();tx.Translate(new(),policy,.01,0);var output=tx.Translate(new(Rx:stick),policy,.01,10);
    double velocity=output.CursorX/32768.0*650*output.CursorSpeedScale,duration=Math.Min(.65,220/Math.Max(1,Math.Abs(velocity)));
    using var emitter=new ControllerInputEmitter();var before=Read();var intervals=new List<double>();var steps=new List<double>();double desired=0;int lastX=before.X,zeroSteps=0;
    timer.WaitForNext(default,0,0);
    clock.Restart();long prior=Stopwatch.GetTimestamp();
    while(clock.Elapsed.TotalSeconds<duration){
     bool waited=candidate?timer.WaitForNext(default,output.DesktopInputInterval,prior):timer.Wait(intervalMilliseconds:4,workMilliseconds:(Stopwatch.GetTimestamp()-prior)*1000.0/Stopwatch.Frequency);
     if(!waited)throw new IOException("The Windows cursor timer was unavailable");
     long now=Stopwatch.GetTimestamp();double dt=(now-prior)/(double)Stopwatch.Frequency;intervals.Add(dt*1000);prior=now;window.RequireForeground();
     if(dt<=DesktopCursorMotion.MaximumSampleSeconds)desired+=velocity*dt;
     emitter.Apply(output,dt);
     var point=Read();int step=point.X-lastX;lastX=point.X;if(step==0)zeroSteps++;steps.Add(Math.Abs(step));
    }
    await Task.Delay(40);var after=Read();double error=after.X-before.X-Math.Truncate(desired);measurements.Add(new{stick,candidate,expectedPixels=desired,actualPixels=after.X-before.X,errorPixels=error,frames=intervals.Count,zeroSteps,p50Milliseconds=Percentile(intervals,.5),p95Milliseconds=Percentile(intervals,.95),maxMilliseconds=intervals.Max(),p95PixelStep=Percentile(steps,.95),maximumPixelStep=steps.Max()});
    if(candidate&&(Percentile(intervals,.5)>=5||Percentile(intervals,.95)>=7))throw new IOException("The four-millisecond cursor stream missed its native timing target");
    if(Math.Abs(error)>1)throw new IOException("Actual continuous desktop pixel movement did not match the integrated velocity");
   }
   int directionChecks=0;var diagonalMeasurements=new List<object>();
   foreach(var frame in new[]{new ControllerFrame(Rx:-12000),new ControllerFrame(Ry:12000),new ControllerFrame(Ry:-12000),new ControllerFrame(Rx:21000,Ry:11000),new ControllerFrame(Rx:11000,Ry:-21000)}){
    window.RequireForeground();SetPhysicalCursorPos(origin.X,origin.Y);await Task.Delay(30);var tx=new KeyboardMouseTranslator();tx.Translate(new(),policy,.008,0);var output=tx.Translate(frame,policy,.008,8);using var emitter=new ControllerInputEmitter();double x=0,y=0;var before=Read();
    double maxDeviation=0;double length=Math.Sqrt((double)frame.Rx*frame.Rx+(double)frame.Ry*frame.Ry);
    for(int i=0;i<100;i++){
     window.RequireForeground();if(!timer.Wait(intervalMilliseconds:2))throw new IOException("The Windows cursor timer was unavailable");
     emitter.Apply(output,.002);x+=output.CursorX/32768.0*650*output.CursorSpeedScale*.002;y-=output.CursorY/32768.0*650*output.CursorSpeedScale*.002;
     var current=Read();maxDeviation=Math.Max(maxDeviation,Math.Abs((current.X-before.X)*(double)frame.Ry+(current.Y-before.Y)*(double)frame.Rx)/length);
    }
    await Task.Delay(35);var actual=Read();if(Math.Abs(actual.X-before.X-Math.Truncate(x))>1||Math.Abs(actual.Y-before.Y-Math.Truncate(y))>1)throw new IOException("Actual vertical/diagonal desktop displacement mismatch");directionChecks++;
    if(frame.Rx!=0&&frame.Ry!=0){diagonalMeasurements.Add(new{frame.Rx,frame.Ry,samples=100,maxDeviation});if(maxDeviation>1)throw new IOException("Physical cursor left the intended diagonal by more than one pixel");}
   }
   foreach(var target in StartRamps)
   {
    window.RequireForeground();SetPhysicalCursorPos(origin.X,origin.Y);await Task.Delay(20);
    var rampPolicy=policy with{MouseSpeed=98,MouseAcceleration=true};
    using var emitter=new ControllerInputEmitter();var tx=new KeyboardMouseTranslator();tx.Translate(new(),rampPolicy,0,0);
    var before=Read();double activeTime=-1,firstMove=-1;long start=Stopwatch.GetTimestamp(),previousTick=start;
    timer.WaitForNext(default,0,0);
    while(true)
    {
     window.RequireForeground();long tick=Stopwatch.GetTimestamp();double now=(tick-start)*1000.0/Stopwatch.Frequency;
     if(now>240)break;
     double dt=(tick-previousTick)/(double)Stopwatch.Frequency;previousTick=tick;
     var sample=tx.Translate(Ramp(target,now),rampPolicy,dt,(long)now);
     if(activeTime<0&&(sample.CursorX!=0||sample.CursorY!=0))activeTime=now;
     emitter.Apply(sample,dt);
     var after=Read();
     if(after.X!=before.X||after.Y!=before.Y){firstMove=now;break;}
     if(!timer.WaitForNext(default,sample.DesktopInputInterval,tick))throw new IOException("The Windows startup timer was unavailable");
    }
    double onset=firstMove-activeTime;
    startupMeasurements.Add(new{target.X,target.Y,target.Duration,activeTime,firstMove,onset});
    if(activeTime<0||firstMove<0||onset>70)throw new IOException("A gradual diagonal remained stationary for too long");
   }
   window.StartWheelProbe();
   var testTx=new KeyboardMouseTranslator();testTx.Translate(new(),policy,0,0);
   var combined=testTx.Translate(new(Rx:20000,Ry:12000,Ly:32767),policy,.004,4);
   window.RequireForeground();SetPhysicalCursorPos(origin.X,origin.Y);var pumpBefore=Read();
   window.WheelEvents.Clear();
   var pumpTime=Stopwatch.StartNew();long pumpGapStart,pumpGapEnd;
   int duringGap;
   using(var pump=new KeyboardMouseMotionPump(canEmit:window.OwnsForeground))
   {
    for(int i=0;i<12;i++){window.RequireForeground();pump.Publish(combined,Stopwatch.GetTimestamp());if(!timer.Wait(intervalMilliseconds:4))throw new IOException("Native motion sender timer unavailable");}
    pumpGapStart=pumpTime.ElapsedMilliseconds;int wheelBeforeGap=window.WheelEvents.Count;
    if(!timer.Wait(intervalMilliseconds:16))throw new IOException("Native motion sender timer unavailable");
    pumpGapEnd=pumpTime.ElapsedMilliseconds;duringGap=window.WheelEvents.Count-wheelBeforeGap;
    if(duringGap<2)throw new IOException("Actual Windows wheel events stopped during a short source stall");
    var progressed=Read();if(progressed.X<=pumpBefore.X||progressed.Y>=pumpBefore.Y)throw new IOException("Actual diagonal cursor did not move during combined scroll");
    pump.Publish(testTx.Translate(new(),policy,.004,20),Stopwatch.GetTimestamp());
    int afterPause=window.WheelEvents.Count;var frozen=Read();await Task.Delay(55);
    var after=Read();if(afterPause!=window.WheelEvents.Count||after.X!=frozen.X||after.Y!=frozen.Y)throw new IOException("Desktop motion continued after neutral input");
   }
   var pumpMeasurement=new{duration= pumpGapEnd-pumpGapStart,wheelEventsDuringSourceGap=duringGap,neutralStoppedCursorAndWheel=true};
   foreach(int scrollSpeed in new[]{100,144,saved.ScrollSpeed}.Distinct())
   foreach(var (frame,message) in new[]{(new ControllerFrame(Ly:19000),0x20a),(new ControllerFrame(Ly:32767),0x20a),(new ControllerFrame(Lx:-32768),0x20e)})
   {
    var scrollPolicy=policy with{ScrollSpeed=scrollSpeed,AdaptiveScroll=true};
    window.WheelEvents.Clear();var tx=new KeyboardMouseTranslator();tx.Translate(new(),scrollPolicy,.008,0);
    using var emitter=new ControllerInputEmitter();var expected=new List<int>();var wheelMotion=new KeyboardMouseWheelMotion();
    int interval=tx.Translate(frame,scrollPolicy,0,0).ScrollIntervalMilliseconds;
    timer.WaitForNext(default,0,0);long prior=Stopwatch.GetTimestamp();
    for(int i=1;i<=36;i++)
    {
     bool waited=timer.WaitForNext(default,interval,prior);
     if(!waited)throw new IOException("The Windows wheel timer was unavailable");
     long now=Stopwatch.GetTimestamp();double dt=(now-prior)/(double)Stopwatch.Frequency;prior=now;
     window.RequireForeground();var output=tx.Translate(frame,scrollPolicy,dt,i*interval);
     var wheel=wheelMotion.Next(output,dt);
     int delta=message==0x20a?wheel.Vertical:wheel.Horizontal;
     if(delta!=0)expected.Add(delta);
     emitter.Apply(output,dt);
    }
    await Task.Delay(100);
    var delivered=window.WheelEvents.ToArray();var received=delivered.Where(item=>item.Message==message).Select(item=>(int)item.Delta).ToArray();
    bool matched=received.SequenceEqual(expected)&&received.Length==delivered.Length;
    wheelMeasurements.Add(new{message,scrollSpeed,interval,expected,received,events=received.Length,total=received.Sum(),maxDelta=received.Select(Math.Abs).DefaultIfEmpty().Max(),matched});
    if(!matched)throw new IOException("Windows did not report the sub-notch scroll deltas at the input hook");
   }
   int afterSpeed=0;var afterMouse=new int[3];if(!ReadMouse(3,0,afterMouse,0)||!ReadSpeed(0x70,0,ref afterSpeed,0)||afterSpeed!=windowsSpeed||!windowsMouse.SequenceEqual(afterMouse))throw new IOException("Windows pointer settings changed during measurement");
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,measurements,directionChecks,diagonalMeasurements,startupMeasurements,wheelMeasurements,pumpMeasurement,delay8Cadence=new{samples=cadence.Count,p50Milliseconds=Percentile(cadence,.5),p95Milliseconds=Percentile(cadence,.95),maxMilliseconds=cadence.Max()},precise8Cadence=new{available=timerAvailable,samples=preciseCadence.Count,p50Milliseconds=Percentile(preciseCadence,.5),p95Milliseconds=Percentile(preciseCadence,.95),maxMilliseconds=preciseCadence.Max(),measuredWithNoVisibleProbeWindow=true},windowsPointer=new{read=mouseRead&&speedRead,speed=windowsSpeed,threshold1=windowsMouse[0],threshold2=windowsMouse[1],acceleration=windowsMouse[2],preserved=true},policyScalars=new{policy.MouseSpeed,policy.MouseAcceleration,policy.StickDeadzone},actualWindowsSendInput=true,actualPhysicalCursorReadback=true,wheelObservedBeforeDelivery=true,wheelApplicationReadback=false,source="Synthetic sticks through actual production translator, emitter and precise wait",productionCaptureEnabled=false,settingsWrites=0,physicalOwnerInputTested=false},Backend.Program.Json));return 0;
  }catch(Exception error){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,measurements,startupMeasurements,wheelMeasurements,error=error.Message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>"),settingsWrites=0},Backend.Program.Json));return 2;}
 }
}
