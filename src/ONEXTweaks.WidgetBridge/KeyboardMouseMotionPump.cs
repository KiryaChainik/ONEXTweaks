// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
namespace ONEXTweaks.WidgetBridge;

internal sealed class KeyboardMouseWheelMotion
{
 private double x,y;
 internal void Reset(){x=y=0;}

 private static int Integrate(ref double pending,double rate,double seconds)
 {
  if(rate==0||Math.Sign(pending)!=Math.Sign(rate))pending=0;
  pending+=rate*seconds;
  int units=(int)pending;
  pending-=units;
  return units;
 }

 internal (int Horizontal,int Vertical) Next(ControllerOutput target,double seconds,double fastPeriodSeconds=0)
 {
  if(!double.IsFinite(seconds)){Reset();return default;}
  if(target.FastDesktopScroll&&fastPeriodSeconds>0)
  {
   // The pump has already checked the sample's age. A delayed output with
   // fresh input can resume immediately, with no backlog from the pause.
   seconds=Math.Min(seconds,2*fastPeriodSeconds);
  }
  else if(seconds>DesktopCursorMotion.MaximumSampleSeconds){Reset();return default;}
  double elapsed=Math.Max(0,seconds);
  return(Integrate(ref x,target.HorizontalWheelRate,elapsed),Integrate(ref y,target.VerticalWheelRate,elapsed));
 }
}

internal sealed class KeyboardMouseWheelCadence
{
 private Task<int>? pending;
 private long nextRead;
 private int refreshHz;

 private static int ReadRefresh()
 {
  try{return WindowsDisplayModes.ReadSingleDisplayRefresh();}
  catch(IOException){return 0;}
 }

 internal static double Interval(ControllerOutput output,int refreshHz) =>
  output.FastDesktopScroll&&refreshHz is (120 or 144) ? 1000.0/refreshHz : output.ScrollIntervalMilliseconds;

 internal double Get(ControllerOutput output,long now)
 {
  if(pending is {IsCompleted:true})
  {
   refreshHz=pending.GetAwaiter().GetResult();
   pending=null;
  }
  if(!output.FastDesktopScroll)return output.ScrollIntervalMilliseconds;
  if(now>=nextRead&&pending==null)
  {
   pending=Task.Run(ReadRefresh);
   nextRead=now+Stopwatch.Frequency;
  }
  return Interval(output,refreshHz);
 }
}

/// <summary>Integrates the most recent intent on separate cursor and wheel deadlines.</summary>
internal sealed class KeyboardMouseMotionPump:IDisposable
{
 private static readonly long StaleTicks=Stopwatch.Frequency*32/1000;
 private readonly object gate=new();
 private readonly AutoResetEvent wake=new(false);
 private readonly CancellationTokenSource stop=new();
 private readonly Thread thread;
 private readonly DesktopCursorMotion cursor=new();
 private readonly KeyboardMouseWheelMotion wheel=new();
 private readonly KeyboardMouseWheelCadence wheelCadence=new();
 private readonly Action<int,int> move,scroll;
 private readonly bool nativeCursor;
 private readonly Func<bool>? canEmit;
 private readonly KeyboardMouseMotionRecorder? recorder;
 internal int Segment {get;}
 private ControllerOutput? target;
 private long updatedAt;
 private long cursorGeneration,wheelGeneration;
 private bool resetCursorClock,resetWheelClock;
 private Exception? failure;

 private readonly record struct PendingOutput(long SampledAt,long Generation,int X,int Y)
 {
  internal bool HasDelta=>X!=0||Y!=0;
 }

 internal KeyboardMouseMotionPump(Action<int,int>? move=null,Action<int,int>? scroll=null,Func<bool>? canEmit=null,KeyboardMouseMotionRecorder? recorder=null)
 {
  nativeCursor=move==null;
  this.move=move??ControllerInputEmitter.MoveContinuousDesktop;
  this.scroll=scroll??ControllerInputEmitter.EmitContinuousWheel;
  this.canEmit=canEmit;
  this.recorder=recorder;
  Segment=recorder?.NewSegment()??0;
  thread=new Thread(Run){IsBackground=true,Name="ONEXTweaks desktop motion"};
  thread.Start();
 }

 internal void Publish(ControllerOutput output,long sampledAt)
 {
  if(Volatile.Read(ref failure) is {} error)throw new IOException("Windows desktop input stopped",error);
  lock(gate)
  {
   if(!output.DesktopCursor||!output.ContinuousCursor&&!output.ContinuousScroll){ClearTarget();return;}
   bool idle=target==null;
   if(target?.ContinuousCursor==true&&!output.ContinuousCursor)cursorGeneration++;
   if(target?.ContinuousScroll==true&&!output.ContinuousScroll)wheelGeneration++;
   if(target?.ContinuousCursor!=true){cursor.Reset();resetCursorClock=true;}
   if(target?.ContinuousScroll!=true){wheel.Reset();resetWheelClock=true;}
   updatedAt=sampledAt;target=output;
   if(idle)wake.Set();
  }
 }

 private void ClearTarget()
 {
  if(target?.ContinuousCursor==true)cursorGeneration++;
  if(target?.ContinuousScroll==true)wheelGeneration++;
  target=null;cursor.Reset();wheel.Reset();
 }

 internal sealed class Deadline
 {
  private long next,previous;
  private long periodTicks;
  internal long Next=>next;
  internal void Reset(){periodTicks=0;next=0;}
  internal double Take(long now,double milliseconds)
  {
   long period=(long)Math.Round(milliseconds*Stopwatch.Frequency/1000);
   if(periodTicks==0){previous=now-period;next=now;}
   else if(periodTicks!=period){next=previous+period;}
   periodTicks=period;
   if(now<next)return 0;
   double seconds=(now-previous)/(double)Stopwatch.Frequency;
   previous=now;
   next+=period;
   // Keep the regular phase under small delays, but skip a deadline that
   // would put two emissions nearly back to back after a late wake.
   if(next-now<period/2)next=now+period;
   return seconds;
  }
 }

 private void Run()
 {
  using var timer=new ControllerCursorCadence();
  var cursorClock=new Deadline();var wheelClock=new Deadline();
  nint previousDpi=0;
  try
  {
   uint threadId=recorder!=null?KeyboardMouseMotionTrace.GetCurrentThreadId():0;
   if(nativeCursor)previousDpi=ControllerInputEmitter.EnterDesktopDpi();
   while(!stop.IsCancellationRequested)
   {
    long due=0;
    bool hasTarget;
    lock(gate) hasTarget=target!=null;
    if(!hasTarget){wake.WaitOne();continue;}

    // A Windows desktop or Game Bar query can block for longer than a
    // controller sampling interval. Let Publish replace or clear the intent
    // while that query is in progress.
    var trace=recorder?.Current;
    if(trace?.IsRecording==true)trace.IdentifySender(Segment,threadId);else trace=null;
    bool permitted=CheckPermission(trace);
    long checkedAt=Stopwatch.GetTimestamp();
    PendingOutput cursorOutput=default,wheelOutput=default;
    lock(gate)
    {
     if(stop.IsCancellationRequested)break;
     long now=Stopwatch.GetTimestamp();
     if(target!=null&&(!permitted||now-updatedAt>=StaleTicks))ClearTarget();
     if(resetCursorClock){cursorClock.Reset();resetCursorClock=false;}
     if(resetWheelClock){wheelClock.Reset();resetWheelClock=false;}
     if(target?.ContinuousCursor==true)
     {
      double dt=cursorClock.Take(now,ControllerOutput.DesktopCursorMilliseconds);
      if(dt>0)
      {
       var point=cursor.NextFresh(target,dt);
       cursorOutput=new(updatedAt,cursorGeneration,point.X,point.Y);
      }
      due=cursorClock.Next;
     }
     else {cursorClock.Reset();cursor.Reset();}
     if(target?.ContinuousScroll==true)
     {
      double interval=wheelCadence.Get(target,now);
      double dt=wheelClock.Take(now,interval);
      if(dt>0)
      {
       var delta=wheel.Next(target,dt,interval/1000);
       wheelOutput=new(updatedAt,wheelGeneration,delta.Horizontal,delta.Vertical);
      }
      due=due==0?wheelClock.Next:Math.Min(due,wheelClock.Next);
     }
     else {wheelClock.Reset();wheel.Reset();}
    }
    // Desktop APIs can block after a motion step is ready. Only the sender
    // waits for Windows; new samples and neutral input can still be published.
    if(cursorOutput.HasDelta)TryEmit(trace,cursorOutput,checkedAt,isCursor:true);
    if(wheelOutput.HasDelta)TryEmit(trace,wheelOutput,checkedAt,isCursor:false);
    if(due==0){wake.WaitOne();continue;}
    long waitedAt=trace?.BeginOutput()??0;
    double milliseconds=(due-Stopwatch.GetTimestamp())*1000.0/Stopwatch.Frequency;
    if(!timer.Wait(stop.Token,milliseconds)&&milliseconds>0)stop.Token.WaitHandle.WaitOne((int)Math.Ceiling(milliseconds));
    trace?.Timer(waitedAt,due);
   }
  }
  catch(OperationCanceledException)when(stop.IsCancellationRequested){}
  catch(Exception error){Volatile.Write(ref failure,error);}
  finally{ControllerInputEmitter.RestoreDesktopDpi(previousDpi);}
 }

 private bool CheckPermission(KeyboardMouseMotionTrace? trace)
 {
  if(canEmit!=null)return canEmit();
  long started=trace?.BeginOutput()??0;
  bool desktopAvailable=ControllerInputDesktop.Available();
  long desktopChecked=started!=0?Stopwatch.GetTimestamp():0;
  bool redirected=desktopAvailable&&NativeGameBarState.IsInputRedirected();
  trace?.Guard(started,desktopChecked,desktopAvailable,redirected);
  return desktopAvailable&&!redirected;
 }

 private void TryEmit(KeyboardMouseMotionTrace? trace,PendingOutput output,long checkedAt,bool isCursor)
 {
  long now=Stopwatch.GetTimestamp();
  if(now-output.SampledAt>=StaleTicks)return;
  if(now-checkedAt>=Stopwatch.Frequency/250&&!CheckPermission(trace))return;
  lock(gate)
  {
   if(stop.IsCancellationRequested||target==null||Stopwatch.GetTimestamp()-output.SampledAt>=StaleTicks)return;
   if(isCursor)
   {
    if(output.Generation!=cursorGeneration)return;
    output=output with{X=KeepDirection(output.X,target.CursorX),Y=KeepDirection(output.Y,-(double)target.CursorY)};
   }
   else
   {
    if(output.Generation!=wheelGeneration)return;
    output=output with{X=KeepDirection(output.X,target.HorizontalWheelRate),Y=KeepDirection(output.Y,target.VerticalWheelRate)};
   }
  }
  if(!output.HasDelta)return;

  // One already-admitted send can finish. A following send will recheck the
  // generation and age even if the previous call stalled in Windows.
  long started=trace?.BeginOutput()??0;
  if(isCursor)move(output.X,output.Y);else scroll(output.X,output.Y);
  trace?.Emitted(isCursor?"cursor":"wheel",output.SampledAt,started,output.X,output.Y);
 }

 private static int KeepDirection(int step,double intent)=>step*intent>0?step:0;

 public void Dispose()
 {
  stop.Cancel();wake.Set();thread.Join();
  wake.Dispose();stop.Dispose();
 }
}
