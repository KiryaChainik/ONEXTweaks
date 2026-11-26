// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
/// <summary>Own-widget-only input fixture. Uses public coordinates produced by our installed UI verifier.</summary>
internal static class ControllerDualSenseNavigationProbe
{
 [StructLayout(LayoutKind.Sequential)]private struct Point{internal int X,Y;}
 [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern nint WindowFromPhysicalPoint(Point point);
 [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern uint GetWindowThreadProcessId(nint handle,out uint pid);
 [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern bool GetCursorPos(out Point point);
 [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern bool SetPhysicalCursorPos(int x,int y);
 [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern void mouse_event(uint flags,uint x,uint y,uint data,nuint extra);
 [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern bool SetProcessDPIAware();
 private static bool OwnSurface(Point point){try{GetWindowThreadProcessId(WindowFromPhysicalPoint(point),out uint pid);using var p=Process.GetProcessById((int)pid);string image=p.MainModule!.FileName;return image.Contains("ONEXTweaks.Widget_",StringComparison.OrdinalIgnoreCase)||image.Contains("Microsoft.XboxGamingOverlay_",StringComparison.OrdinalIgnoreCase);}catch{return false;}}
 internal static async Task<int> Run(string file,bool edge=false){
  SetProcessDPIAware();string reports=Path.GetDirectoryName(file)!,local=WidgetPackageIdentity.LocalState;ControllerViiperDualSenseDevice? output=null;ControllerDualSenseHidReader? hid=null;ControllerCaptureLease? lease=null;ControllerInputEmitter? input=null;int steps=0,hidCrossReadbacks=0;bool finished=false,restored=false;string? error=null;var before=ControllerNative.Slots();
  try{
   if(!before.SequenceEqual(new[]{0})||!ControllerNative.Read(0,out var frame)||!ControllerNative.Neutral(frame)||File.Exists(ControllerCaptureLease.LeaseFile(reports)))throw new IOException("Protect owner active input/capture");var sources=ControllerNative.Sources();if(sources.Length!=1)throw new IOException("Protect additional controllers");
   output=new(edge);hid=await ControllerDualSenseHidReader.OpenOwned(output.Identity,output.Product);await hid.ReadUntil(b=>b[8]==8&&b[9]==0&&b[10]==0);lease=await ControllerCaptureLease.Acquire(reports,sources[0]);if((await ControllerRemapper.Observe(reports)).Length!=0)throw new IOException("Physical source was not independently hidden");input=new((keys,mouse)=>lease.Input(keys,mouse));var navigation=new ControllerDs4NavigationRelay();void Submit(ControllerFrame value){var translated=navigation.Apply(new(value,[],[],0,0,0,[]),NativeGameBarState.IsInputRedirected(),Environment.TickCount64);var basis=ControllerViiperDs4Device.Encode(translated.Pad);output.Submit(new(){Lx=basis.Lx,Ly=basis.Ly,Rx=basis.Rx,Ry=basis.Ry,Buttons=basis.Buttons,DPad=basis.DPad,L2=basis.L2,R2=basis.R2,AccelZ=-8192});input.Apply(translated,0);}await File.WriteAllTextAsync(file+".ready.json",JsonSerializer.Serialize(new{pid=Environment.ProcessId,sourceHidden=true,identityConfirmed=true}));long heartbeat=0;string last="";DateTime deadline=DateTime.UtcNow.AddSeconds(160);
   while(DateTime.UtcNow<deadline){
    if(Environment.TickCount64>=heartbeat){lease.Touch();heartbeat=Environment.TickCount64+700;}
    JsonDocument? doc=null;try{using var stream=new FileStream(Path.Combine(local,"onex-gamepad-qa-state.json"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);doc=await JsonDocument.ParseAsync(stream);}catch(IOException){}catch(JsonException){}
    if(doc==null){await Task.Delay(40);continue;}using(doc){var root=doc.RootElement;if(root.TryGetProperty("finished",out var done)&&done.GetBoolean()){finished=true;break;}if(!root.TryGetProperty("id",out var identity)){await Task.Delay(40);continue;}string id=identity.GetString()!;if(id==last){await Task.Delay(40);continue;}string key=root.GetProperty("button").GetString()!;var point=new Point{X=(int)root.GetProperty("x").GetDouble(),Y=(int)root.GetProperty("y").GetDouble()};
     if(!NativeGameBarState.IsInputRedirected()||!OwnSurface(point))throw new IOException("Own overlay is not the active protected input surface");
     if(key=="focus"){
      await Task.Delay(200);GetCursorPos(out var previous);try{SetPhysicalCursorPos(point.X,point.Y);await Task.Delay(40);mouse_event(2,0,0,0,ControllerInputEmitter.OwnTag);await Task.Delay(60);mouse_event(4,0,0,0,ControllerInputEmitter.OwnTag);await Task.Delay(100);}finally{SetPhysicalCursorPos(previous.X,previous.Y);}
     }else{
      ControllerFrame pressed=key switch{"up"=>new(1),"down"=>new(2),"left"=>new(4),"right"=>new(8),"a"=>new(0x1000),"b"=>new(0x2000),"x"=>new(0x4000),"lt"=>new(Lt:255),"rt"=>new(Rt:255),_=>throw new InvalidDataException("Unsupported DS4 own-UI fixture control")};Submit(pressed);if(key=="a"){await hid.ReadUntil(b=>(b[8]&0xf0)==0x20);hidCrossReadbacks++;}await Task.Delay(100);Submit(pressed);await Task.Delay(20);Submit(new());await Task.Delay(120);
     }
     await File.WriteAllTextAsync(Path.Combine(local,"onex-gamepad-qa-ack.txt"),id);steps++;last=id;
    }
   }
   if(!finished||steps!=12)throw new IOException("Own-widget DS4 input fixture did not finish");
  }catch(Exception e){error=e.GetType().Name+": "+e.Message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>",StringComparison.OrdinalIgnoreCase);}
  finally{
   try{input?.Dispose();}catch{}try{hid?.Dispose();output?.Dispose();}catch(Exception e){error=(error??"")+"; "+e.Message;}try{lease?.Dispose();restored=(await ControllerRemapper.Observe(reports)).SequenceEqual(before)&&!File.Exists(ControllerCaptureLease.LeaseFile(reports));}catch(Exception e){error=(error??"")+"; "+e.Message;}
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=error==null&&finished&&restored,steps,hidCrossReadbacks,error,physicalInputRestored=restored,virtualDevice=edge?"DualSenseEdge":"DualSense",physicalSourceHiddenDuringProbe=true,uiDeliveryMustBeVerifiedSeparately=true,contextKeyRelayTested=true,productionNavigationRelayTested=false,ownerPhysicalButtonsUsed=false},Backend.Program.Json));
  }
  using var result=JsonDocument.Parse(await File.ReadAllTextAsync(file));return result.RootElement.GetProperty("passed").GetBoolean()?0:2;
 }
}
