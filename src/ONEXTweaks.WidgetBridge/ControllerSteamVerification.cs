// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerSteamVerification
{
 internal static async Task<int> Model(string file)
 {
  int checks=0;void Check(bool value,string message){checks++;if(!value)throw new IOException(message);}
  try
  {
   foreach(string source in ControllerOemPolicy.Inputs)foreach(string action in new[]{"steamBigPicture","steamQuickAccess"})
   {
    var oem=new ControllerOemPolicy(new(){{source,new(action)}},new(){{source,new(true,250,new(action))}}).Validate();
    Check(oem.For(source).Kind==action&&oem.For(source+":Double").Kind==action,"Steam OEM binding lost");
    var policy=new ControllerPolicy(Oem:oem).Validate();string saved=JsonSerializer.Serialize(policy,Backend.Program.Json);var restored=JsonSerializer.Deserialize<ControllerPolicy>(saved,Backend.Program.Json)!.Validate();
    Check(restored.Oem!.For(source).Kind==action&&restored.Oem.DoubleFor(source).Enabled,"Single/double persistence lost");
    var translator=new ControllerTranslator();foreach(int offset in new[]{0,6})
    {
     var output=translator.Translate(new(Oem:(ushort)(1<<(Array.IndexOf(ControllerOemPolicy.Inputs,source)+offset))),restored);
     Check(!output.Guide&&output.Keys.Count==0&&output.Pad.Buttons==0&&output.Wheel==0&&output.HorizontalWheel==0,"Steam leaked keyboard/Guide/gamepad output");
    }
   }
   foreach(string action in new[]{"steamBigPicture","steamQuickAccess"})
   {
    bool rejected=false;try{new ControllerPolicy(new(){{"A",new(action)}}).Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"Steam action accepted on ordinary gamepad source");
    rejected=false;try{new ControllerOemPolicy(ControllerOemPolicy.Inputs.ToDictionary(id=>id,id=>new ControllerBinding(action))).Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"Steam removed last widget assignment");
   }
   Check(!DesktopActions.OemSteamOpensMenu("steamBigPicture",false)&&DesktopActions.OemSteamOpensMenu("steamBigPicture",true),"Launch/menu context wrong");
   Check(DesktopActions.OemSteamOpensMenu("steamQuickAccess",false)&&DesktopActions.OemSteamOpensMenu("steamQuickAccess",true),"Quick Access launch context wrong");
   Check(DesktopActions.OemSteamOpensMenu("steamMenu",false)&&DesktopActions.OemSteamOpensMenu("steamMenu",true),"Explicit menu command must open the menu after launch as well");
   Check(DesktopActions.SteamPlan("steamMenu",true).BigPicture&&DesktopActions.SteamPlan("steamContext",false).BigPicture&&!DesktopActions.SteamPlan("steamContext",true).BigPicture,"Steam menu must focus Big Picture while the in-game overlay keeps its game target");
   Check(DesktopActions.SteamPlan("steamMenu",false).Shortcut=="F1"&&DesktopActions.SteamPlan("steamContext",false).Shortcut=="F1"&&DesktopActions.SteamPlan("steamContext",true).Shortcut=="Shift+Tab","Steam main menu or in-game overlay shortcut changed");
   bool cancelled=false;try{await DesktopActions.ExecuteOemSteam("steamBigPicture",()=>false);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"Stale profile dispatched Steam");
   using var stop=new CancellationTokenSource();stop.Cancel();cancelled=false;try{await DesktopActions.ExecuteOemSteam("steamQuickAccess",()=>true,stop.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"Stopped helper dispatched Steam");
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,physicalOwnerInputTested=false},Backend.Program.Json));return 0;
  }catch(Exception error){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=error.Message},Backend.Program.Json));return 2;}
 }
 internal static async Task<int> Native(string file)
 {
  int checks=0;void Check(bool value,string message){checks++;if(!value)throw new IOException(message);}
  var results=new List<OemSteamResult>();bool gameBarSeen=false;using var stop=new CancellationTokenSource();
  try
  {
   Check(!NativeGameBarState.IsInputRedirected(),"Close Game Bar before the Steam action probe");
   var monitor=Task.Run(async()=>{while(!stop.IsCancellationRequested){gameBarSeen|=NativeGameBarState.IsInputRedirected();await Task.Delay(5);}});
   // Only Steam's own UI is changed. No controller profile or firmware writes.
   using(var close=Process.Start(new ProcessStartInfo("steam://close/bigpicture"){UseShellExecute=true})){}
   for(int n=0;n<160&&DesktopActions.FindSteamBigPicture()!=0;n++)await Task.Delay(50);
   Check(DesktopActions.FindSteamBigPicture()==0,"Steam did not leave Big Picture for the launch probe");
   var launched=await DesktopActions.ExecuteOemSteam("steamBigPicture",()=>true);results.Add(launched);Check(launched.Launched&&launched.Menu==null&&launched.ForegroundVerified,"Big Picture launch/focus failed");await Task.Delay(800);
   int gameBarCalls=0;using var entry=new GameBarEntry(Path.GetDirectoryName(file)!,new GameBarEntryRuntime(()=>false,()=>false,()=>gameBarCalls++,()=>gameBarCalls++,()=>{gameBarCalls++;return Task.CompletedTask;},()=>false,()=>gameBarCalls++),deferStart:true);
   using var source=new ControllerOemRawInput();var policy=new ControllerOemPolicy(new(){{"M1",new("steamBigPicture")}},new(){{"M2",new(true,150,new("steamQuickAccess"))}}).Validate();source.ConfigureGestures(()=>policy);source.FirmwareAliases(true);for(int n=0;n<100&&!source.Listening;n++)await Task.Delay(10);Check(source.Listening,"Synthetic OEM listener unavailable");using var system=new ControllerOemSystemActions(source,()=>policy,()=>true,entry);
   async Task<OemSteamResult> Wait(long expected){for(int n=0;n<400;n++){var value=JsonSerializer.SerializeToElement(system.Read(),Backend.Program.Json);if(value.GetProperty("steamCompleted").GetInt64()==expected)return value.GetProperty("lastSteam").Deserialize<OemSteamResult>(Backend.Program.Json)!;if(value.TryGetProperty("error",out var error)&&error.ValueKind==JsonValueKind.String)throw new IOException(error.GetString());await Task.Delay(20);}throw new IOException("Synthetic OEM Steam dispatch timed out");}
   async Task Press(string id){source.FromHook(new(id,true));await Task.Delay(25);source.FromHook(new(id,false));}
   await Press("M1");var main=await Wait(1);results.Add(main);Check(!main.Launched&&main.Menu=="main"&&main.ForegroundVerified,"Existing Big Picture main menu failed");
   await File.WriteAllTextAsync(file+".main-ready","ready");await Task.Delay(1800);
   await Press("M1");await Wait(2);await Task.Delay(300);
   await Press("M2");await Task.Delay(25);await Press("M2");var quick=await Wait(3);results.Add(quick);Check(!quick.Launched&&quick.Menu=="quickAccess"&&quick.ForegroundVerified,"Quick Access double action failed");
   await File.WriteAllTextAsync(file+".quick-ready","ready");await Task.Delay(1800);Check(gameBarCalls==0,"Steam called widget activation routing");
   stop.Cancel();await monitor;Check(!gameBarSeen,"A Steam action opened or redirected input to Game Bar");
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,results,actualSteamClient=true,syntheticOemSingleAndDouble=true,actualRawInputDispatch=true,keyboardMenuShortcutsSent=true,gameBarSeen,guideSent=false,winGSent=false,physicalOwnerInputTested=false,settingsWrites=0,firmwareWrites=0},Backend.Program.Json));return 0;
  }catch(Exception error){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,results,gameBarSeen,error=error.Message},Backend.Program.Json));return 2;}
  finally{stop.Cancel();}
 }
}
