// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class KeyboardMouseNativeVerification
{
 [StructLayout(LayoutKind.Sequential)]private struct Point{internal int X,Y;}
 [DllImport("user32.dll")]private static extern bool GetCursorPos(out Point point);
 internal static async Task<int> Run(string file)
 {
  int checks=0;void Check(bool value,string message){checks++;if(!value)throw new IOException(message);}
  try
  {
   Check(ControllerInputDesktop.Available(),"Normal input desktop required");using var window=new ControllerTestWindow();window.RequireForeground();using var emitter=new ControllerInputEmitter();var policy=new KeyboardMousePolicy().Validate();var engine=new KeyboardMouseTranslator();long now=0;
   void Apply(ControllerFrame frame,int ticks=1,KeyboardMousePolicy? selected=null){window.RequireForeground();for(int n=0;n<ticks;n++){now+=10;emitter.Apply(engine.Translate(frame,selected??policy,.01,now),.01);}}
   Apply(new());Check(!ControllerTestWindow.IsHeld(32)&&!ControllerTestWindow.IsHeld(160)&&!ControllerTestWindow.IsHeld(162),"Protect owner held keys");Apply(new(4096));await Task.Delay(50);Check(ControllerTestWindow.IsHeld(32),"Actual Space key hold missing");Apply(new());await Task.Delay(50);Check(!ControllerTestWindow.IsHeld(32),"Actual Space not released");
   Apply(new(Lt:255));await Task.Delay(40);Check(ControllerTestWindow.IsHeld(160),"Actual left Shift missing");Apply(new());Apply(new(128));await Task.Delay(40);Check(ControllerTestWindow.IsHeld(162),"Actual left Ctrl missing");Apply(new());
   var combo=(policy with{Bindings=new(){{"A",new("combo","left",[17])}}}).Validate();engine.Reset();Apply(new(),selected:combo);await Task.Delay(90);int before=window.Events.Count;Apply(new(4096),selected:combo);await Task.Delay(60);Apply(new(),selected:combo);await Task.Delay(60);var events=window.Events.Skip(before).ToArray();int up=Array.FindIndex(events,value=>value.StartsWith("514:",StringComparison.Ordinal));int ctrlUp=Array.FindIndex(events,value=>value=="257:17");Check(up>=0&&ctrlUp>up,"Ctrl released before mouse-up");Check((ulong.Parse(events[up].Split(':')[1])&8)!=0,"Ctrl-click mouse-up lost modifier");
   engine.Reset();Apply(new());before=window.Events.Count;Apply(new(Ly:32767),30);Apply(new());await Task.Delay(70);Check(window.Events.Skip(before).Any(value=>value.StartsWith("522:",StringComparison.Ordinal)),"Actual continuous vertical wheel missing");before=window.Events.Count;Apply(new(Lx:32767),30);Apply(new());await Task.Delay(70);Check(window.Events.Skip(before).Any(value=>value.StartsWith("526:",StringComparison.Ordinal)),"Actual horizontal wheel missing");
   GetCursorPos(out var origin);Apply(new(Rx:32767),5);Apply(new());GetCursorPos(out var moved);Check(moved.X>origin.X&&Math.Abs(moved.Y-origin.Y)<=1,"Actual right-stick cursor movement missing");
   var fn=(policy with{FnButton="OEM:M1",FnBindings=new(){{"A",new("keyboard",Keys:[17,67])}}}).Validate();engine.Reset();emitter.Apply(engine.Translate(new(),fn,.01,now+=10),.01);emitter.Apply(engine.Translate(new(4096),fn,.01,now+=10,16),.01);Check(!ControllerTestWindow.IsHeld(67),"FN transition typed held button");emitter.Apply(engine.Translate(new(),fn,.01,now+=10,16),.01);emitter.Apply(engine.Translate(new(4096),fn,.01,now+=10,16),.01);await Task.Delay(40);Check(ControllerTestWindow.IsHeld(17)&&ControllerTestWindow.IsHeld(67),"Actual FN Ctrl+C hold missing");emitter.Apply(engine.Translate(new(4096),fn,.01,now+=10),.01);await Task.Delay(40);Check(!ControllerTestWindow.IsHeld(17)&&!ControllerTestWindow.IsHeld(67),"Actual FN release left keys held");emitter.Dispose();
   using var source=new ControllerOemRawInput();int toggles=0,gameBar=0;var oem=new ControllerOemPolicy(DoublePresses:new(){{"KeyboardMouse",new(true,150,new("inputMode"))}}).Validate();source.ConfigureGestures(()=>oem);source.ReserveKeyboardSwitch(true);using var entry=new GameBarEntry(Path.GetDirectoryName(file)!,new GameBarEntryRuntime(()=>false,()=>false,()=>gameBar++,()=>gameBar++,()=>{gameBar++;return Task.CompletedTask;}),deferStart:true);using var system=new ControllerOemSystemActions(source,()=>oem,()=>false,entry,()=>{Interlocked.Increment(ref toggles);return Task.CompletedTask;});for(int n=0;n<100&&!source.Listening;n++)await Task.Delay(10);Check(source.Listening&&!source.OwnsAliases,"Off-group gesture probe changed firmware ownership");
   async Task Press(string id){source.FromHook(new(id,true));await Task.Delay(20);source.FromHook(new(id,false));await Task.Delay(25);}
   await Press("KeyboardMouse");await Press("KeyboardMouse");for(int n=0;n<80&&toggles==0;n++)await Task.Delay(10);Check(toggles==1&&gameBar==0,"Global Keyboard double failed with remapping group Off");await Press("Home");await Task.Delay(200);Check(gameBar==0,"Disabled OEM group leaked Home action");source.ReserveKeyboardSwitch(false);await Press("KeyboardMouse");await Press("KeyboardMouse");await Task.Delay(230);Check(toggles==1,"Disabled reserved hook still toggled mode");
   // Simulate only the input-release restriction, without opening or typing
   // on a secure desktop. Restore the real physical HID before retrying keys.
   string reports=Path.GetDirectoryName(file)!,journal=ControllerCaptureLease.LeaseFile(reports);Check(!File.Exists(journal)&&ControllerNative.Slots().SequenceEqual(new[]{0})&&ControllerNative.Sources().Length==1,"Protect owner capture before recovery test");
   string beforeHide;using(var hide=new ControllerHidHide())beforeHide=JsonSerializer.Serialize(hide.Read(),Backend.Program.Json);
   ControllerCaptureLease? lease=null;ControllerInputEmitter? late=null;
   try
   {
    lease=await ControllerCaptureLease.Acquire(reports,ControllerNative.Sources().Single());var owned=JsonSerializer.Deserialize<ControllerLeaseData>(File.ReadAllText(journal),Backend.Program.Json)!;Check(owned.Token==lease.Token&&owned.Pid==Environment.ProcessId,"Own recovery identity mismatch");
    using(var watchdog=System.Diagnostics.Process.GetProcessById(owned.WatchdogPid)){Check(watchdog.StartTime.ToUniversalTime().Ticks==owned.WatchdogStartedUtcTicks&&watchdog.MainModule?.FileName==Environment.ProcessPath,"Protect unrelated watchdog");watchdog.Kill();await watchdog.WaitForExitAsync();}
    late=new ControllerInputEmitter((keys,mouse)=>lease.Input(keys,mouse));window.RequireForeground();late.Apply(new(new(),[17],[],0,0,0,[]),0);await Task.Delay(50);Check(ControllerTestWindow.IsHeld(17),"Owned recovery Ctrl not held");bool deferred=false;try{await ControllerCaptureLease.RecoverStopped(reports,()=>false);}catch(IOException){deferred=true;}Check(deferred&&File.Exists(journal),"Deferred input journal was lost");
    var pending=JsonSerializer.Deserialize<ControllerLeaseData>(File.ReadAllText(journal),Backend.Program.Json)!;Check(pending.PhysicalRestored&&pending.HeldKeys?.Contains(17)==true,"Physical restore did not precede deferred release");Check((await ControllerRemapper.Observe(reports)).SequenceEqual(new[]{0}),"Independent physical input not restored while key release blocked");
    late.Dispose();late=null;await ControllerCaptureLease.RecoverStopped(reports);Check(!File.Exists(journal)&&!ControllerTestWindow.IsHeld(17),"Deferred input retry did not clean up");
    using(var hide=new ControllerHidHide())Check(beforeHide==JsonSerializer.Serialize(hide.Read(),Backend.Program.Json),"Owned recovery changed unrelated HidHide state");lease.Dispose();lease=null;
   }
   finally{late?.Dispose();try{await ControllerCaptureLease.RecoverStopped(reports);}finally{lease?.Dispose();}}
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,actualOwnWindowSendInput=true,actualCursorAndBothWheels=true,actualModifierMouseReleaseOrder=true,actualFnHoldRelease=true,actualIndependentRecoveryBeforeKeyRelease=true,simulatedProtectedDesktopRestriction=true,hidHideChangesOwnedQaOnly=true,syntheticOemKeyboardDouble=true,globalKeyboardDoubleWithExtraGroupOff=true,settingsWrites=0,firmwareWrites=0,productionCaptureEnabled=false,physicalOwnerInputTested=false},Backend.Program.Json));return 0;
  }catch(Exception error){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=error.Message},Backend.Program.Json));return 2;}
 }
 [DllImport("user32.dll")]private static extern nint GetForegroundWindow();
 [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint window,out uint pid);
 internal static async Task<int> TaskManager(string file)
 {
  var before=System.Diagnostics.Process.GetProcessesByName("Taskmgr");var old=before.Select(p=>p.Id).ToHashSet();foreach(var process in before)process.Dispose();
  try{
   if(!ControllerInputDesktop.Available())throw new IOException("Normal input desktop required");
   using var window=new ControllerTestWindow();window.RequireForeground();using var emitter=new ControllerInputEmitter();var engine=new KeyboardMouseTranslator();var policy=new KeyboardMousePolicy().Validate();emitter.Apply(engine.Translate(new(),policy,.01,0),.01);ushort mask=(ushort)(1<<Array.IndexOf(ControllerOemPolicy.Inputs,"Onex"));
   emitter.Apply(engine.Translate(new(Oem:mask),policy,.01,10),.01);await Task.Delay(80);emitter.Dispose();
   bool found=false,focused=false;for(int n=0;n<100;n++){var targets=System.Diagnostics.Process.GetProcessesByName("Taskmgr");GetWindowThreadProcessId(GetForegroundWindow(),out uint foreground);found=targets.Length>0;focused=targets.Any(p=>p.Id==foreground);foreach(var target in targets)target.Dispose();if(found&&focused)break;await Task.Delay(50);}
   if(!found||!focused||!ControllerInputDesktop.Available())throw new IOException("Direct Task Manager launch/focus not confirmed");
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,actualTaskManagerLaunchedOrFocused=found,foregroundConfirmed=focused,ordinaryDesktopStayedAvailable=true,syntheticOemOnexToActualSendInput=true,securityScreenOpened=false,securityPolicyChanged=false,physicalOwnerInputTested=false},Backend.Program.Json));return 0;
  }catch(Exception error){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,error=error.Message},Backend.Program.Json));return 2;}
  finally{foreach(var process in System.Diagnostics.Process.GetProcessesByName("Taskmgr")){using(process){if(!old.Contains(process.Id))process.CloseMainWindow();}}}
 }
}
