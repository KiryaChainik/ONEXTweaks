// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace ONEXTweaks.WidgetBridge;
/// <summary>Owned high-resolution wait for desktop cursor and scroll movement.</summary>
internal sealed class ControllerCursorCadence:IDisposable
{
 private sealed class TimerWait:WaitHandle{internal TimerWait(nint handle){SafeWaitHandle=new SafeWaitHandle(handle,true);}}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint CreateWaitableTimerExW(nint attributes,string? name,uint flags,uint access);
 [DllImport("kernel32.dll",SetLastError=true)]private static extern bool SetWaitableTimer(SafeWaitHandle timer,ref long due,int period,nint callback,nint argument,bool resume);
 [DllImport("kernel32.dll")]private static extern bool CancelWaitableTimer(SafeWaitHandle timer);
 private TimerWait? timer;
 private long nextDeadline;
 private int previousInterval;
 internal long NextDeadline=>nextDeadline;
 internal bool Available=>timer!=null;
 internal ControllerCursorCadence(){nint handle=CreateWaitableTimerExW(0,null,2,0x100002);if(handle!=0)timer=new TimerWait(handle);}
 internal bool WaitRelative(CancellationToken token,int intervalMilliseconds,long sampleStarted)
 {
  previousInterval=0;
  return Wait(token,intervalMilliseconds,(Stopwatch.GetTimestamp()-sampleStarted)*1000.0/Stopwatch.Frequency);
 }
 internal bool WaitForNext(CancellationToken token,int intervalMilliseconds,long sampleStarted)
 {
  if(intervalMilliseconds==0){previousInterval=0;return false;}
  long period=intervalMilliseconds*Stopwatch.Frequency/1000;
  if(previousInterval!=intervalMilliseconds||nextDeadline<sampleStarted-period)nextDeadline=sampleStarted;
  previousInterval=intervalMilliseconds;
  nextDeadline+=period;
  return Wait(token,(nextDeadline-Stopwatch.GetTimestamp())*1000.0/Stopwatch.Frequency);
 }
 internal bool Wait(CancellationToken token=default,double intervalMilliseconds=8,double workMilliseconds=0)
 {
  token.ThrowIfCancellationRequested();var current=timer;if(current==null)return false;
  double remaining=intervalMilliseconds-workMilliseconds;
  if(remaining<=0)return true;
  long due=-(long)Math.Ceiling(remaining*10000); // A relative one-shot never wakes the device from sleep.
  if(!SetWaitableTimer(current.SafeWaitHandle,ref due,0,0,0,false)){Dispose();return false;}
  if(token.CanBeCanceled){int result=WaitHandle.WaitAny([current,token.WaitHandle],100);if(result==1)token.ThrowIfCancellationRequested();if(result==WaitHandle.WaitTimeout){Dispose();return false;}}
  else if(!current.WaitOne(100)){Dispose();return false;}
  return true;
 }
 public void Dispose(){var current=timer;timer=null;if(current!=null){CancelWaitableTimer(current.SafeWaitHandle);current.Dispose();}}
}
