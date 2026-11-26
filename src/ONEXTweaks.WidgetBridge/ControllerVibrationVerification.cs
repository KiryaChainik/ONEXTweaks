// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Concurrent;
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerVibrationVerification
{
 internal static async Task<int> Model(string file)
 {
  int checks=0;void Check(bool ok,string name){checks++;if(!ok)throw new InvalidOperationException(name);}
  try
  {
   for(int percent=0;percent<=100;percent++)for(int motor=0;motor<=255;motor++){var value=new ControllerVibration(percent).Validate().Scale((byte)motor,(byte)(255-motor));Check(value.Large<=motor&&value.Small<=255-motor&&(percent!=100||value.Large==motor&&value.Small==255-motor)&&(percent!=0||value.Large==0&&value.Small==0),"Intensity bounded, zero silent,100% original");}
   int intensity=50;var sent=new ConcurrentQueue<(byte Large,byte Small)>();using(var router=new ControllerRumbleForwarder(()=>new(intensity),(l,s)=>sent.Enqueue((l,s))))
   {
    router.Feedback(200,100);Check(sent.IsEmpty,"Identity probe must not reach motors before forwarding enabled");router.Enable();router.Feedback(200,100);Check(sent.Last()==(100,50),"Real callback scaling");intensity=0;router.Refresh();Check(sent.Last()==(0,0),"Changing strength stops latched rumble without another game callback");intensity=50;router.Refresh();Check(sent.Last()==(100,50),"Profile strength reapplied to latest rumble");var test=router.Test(new(25),50);Check(sent.Last()==(64,64),"Configured test pulse");int count=sent.Count;router.Feedback(80,160);Check(sent.Count==count,"Game feedback cannot overwrite active test");intensity=75;await test;Check(sent.Last()==(60,120),"Restore latest game feedback with latest profile strength");
    var interrupted=router.Test(new(100),500);router.Dispose();Check(sent.Last()==(0,0),"Stop immediately releases motors");bool canceled=false;try{await interrupted;}catch(OperationCanceledException){canceled=true;}Check(canceled&&sent.Last()==(0,0),"Disposal cancels test without restoring stale game rumble");int after=sent.Count;router.Feedback(255,255);Check(sent.Count==after,"No feedback after stop");
   }
   sent.Clear();using(var direct=new ControllerRumbleForwarder(()=>new(),(l,s)=>sent.Enqueue((l,s)))){await direct.Test(new(50),20);Check(sent.First()==(128,128)&&sent.Last()==(0,0),"Hardware-only test is bounded and ends at zero");}
   int failures=1;using(var errors=new ControllerRumbleForwarder(()=>new(),(l,s)=>{if(failures-->0)throw new IOException("owned test failure");}))
   {bool rejected=false;try{await errors.Test(new(),20);}catch(IOException){rejected=true;}Check(rejected,"Failed native write must surface");await errors.Test(new(),20);Check(true,"Test semaphore released after failed start");}
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,kind="pure model/fake output sink; no hardware",actualOwnerHardwareTested=false},Backend.Program.Json));return 0;
  }catch(Exception error){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=error.Message},Backend.Program.Json));return 2;}
 }
 internal static async Task<int> Native(string file)
 {
  int checks=0,forwarded=0;ControllerVirtualPad? pad=null;int source=-1;var actual=new ConcurrentQueue<(byte Large,byte Small)>();void Check(bool ok,string name){checks++;if(!ok)throw new InvalidOperationException(name);}
  try
  {
   using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\ONEXTweaks\Overlay");source=Math.Clamp(key?.GetValue("GuideControllerIndex") as int? ??0,0,3);Check(ControllerNative.Sources().Length==1&&ControllerNative.Slots().SequenceEqual(new[]{source}),"Protect extra controller/capture before native rumble probe");int percent=50;
   pad=new ControllerVirtualPad(source,false,vibration:()=>new(percent),inspectForwarded:(l,s)=>{actual.Enqueue((l,s));Interlocked.Increment(ref forwarded);});pad.EnableRumbleForwarding();
   async Task Send(byte l,byte s,byte expectedL,byte expectedS){int before=Volatile.Read(ref forwarded);ControllerNative.RumbleChecked(pad!.Slot,l,s);for(int n=0;n<100&&!actual.Skip(before).Any(value=>value==(expectedL,expectedS));n++)await Task.Delay(10);Check(actual.Skip(before).Any(value=>value==(expectedL,expectedS)),"VIIPER callback did not forward expected intensity to successful physical XInput API");}
   await Send(200,100,100,50);percent=25;pad.RefreshVibration();Check(actual.Last()==(50,25),"Strength change refreshes actual physical API without new game feedback");percent=0;pad.RefreshVibration();Check(actual.Last()==(0,0),"Zero strength immediately silences physical sink");percent=0;await Send(80,220,0,0);percent=100;await Send(11,17,11,17);await Send(0,0,0,0);pad.Dispose();pad=null;Check(ControllerNative.Slots().SequenceEqual(new[]{source}),"Temporary VIIPER output was not removed");
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,forwarded,actualViiperFeedback=true,physicalXInputApiAcknowledged=true,temporaryVirtualRemoved=true,productionCaptureEnabled=false,ownerPhysicalPerceptionTested=false,settingsWrites=0},Backend.Program.Json));return 0;
  }
  catch(Exception error){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=error.Message},Backend.Program.Json));return 2;}
  finally{try{if(source>=0)ControllerNative.Rumble(source,0,0);}catch{}try{pad?.Dispose();}catch{}}
 }
}
