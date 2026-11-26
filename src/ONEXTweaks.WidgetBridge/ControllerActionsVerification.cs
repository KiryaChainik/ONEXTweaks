// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerActionsVerification
{
 internal static async Task<int> Model(string file)
 {
  int checks=0;void Check(bool ok,string message){checks++;if(!ok)throw new InvalidOperationException(message);}
  try
  {
   foreach(string source in ControllerPolicy.Inputs.Concat(ControllerOemPolicy.Inputs))
   {
    ControllerFrame frame=ControllerPolicy.Buttons.TryGetValue(source,out var mask)?new(mask):source switch{"LT"=>new(Lt:255),"RT"=>new(Rt:255),"LSUp"=>new(Ly:30000),"LSDown"=>new(Ly:-30000),"LSLeft"=>new(Lx:-30000),"LSRight"=>new(Lx:30000),"RSUp"=>new(Ry:30000),"RSDown"=>new(Ry:-30000),"RSLeft"=>new(Rx:-30000),"RSRight"=>new(Rx:30000),_=>ControllerOemPolicy.Inputs.Contains(source)?new(Oem:(ushort)(1<<Array.IndexOf(ControllerOemPolicy.Inputs,source))):new(Extras:(ushort)(1<<Array.IndexOf(ControllerPolicy.Extra,source)))};
    ControllerPolicy Bind(ControllerBinding binding)=>ControllerOemPolicy.Inputs.Contains(source)?new ControllerPolicy(Oem:new(new(){{source,binding}})).Validate():new ControllerPolicy(new(){{source,binding}}).Validate();
    var translator=new ControllerTranslator();var guide=Bind(new("gamepad","Guide"));Check(translator.Translate(frame,guide).Guide,"Guide not held for "+source);Check(translator.Translate(new(),guide).Guide==false,"Guide stuck after release");Check(!translator.Translate(frame,guide,true).Guide,"Guide mapping must bypass in overlay");if(ControllerRemappingGroups.IsStandard(source))Check(!translator.Translate(frame,guide with{GamepadRemappingDisabled=true}).Guide,"Disabled group leaked Guide");
    foreach(var wheel in new[]{("wheelLeft",-1),("wheelRight",1)}){translator.Reset();var p=Bind(new("mouse",wheel.Item1));var first=translator.Translate(frame,p);Check(first.HorizontalWheel==wheel.Item2&&first.Wheel==0,"Horizontal direction lost");Check(translator.Translate(frame,p).HorizontalWheel==0,"Held wheel repeats");translator.Translate(new(),p);Check(translator.Translate(frame,p).HorizontalWheel==wheel.Item2,"Wheel did not rearm");}
    var keys=new[]{17,16,65,66,67};var keyPolicy=Bind(new("keyboard",Keys:keys));Check(translator.Translate(frame,keyPolicy).Keys.SetEquals(keys),"Five-key chord lost");Check(translator.Translate(new(),keyPolicy).Keys.Count==0,"Chord stuck after release");
   }
   bool rejected=false;try{new ControllerPolicy(new(){{"A",new("keyboard",Keys:[17,16,65,66,67,68])}}).Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"Six-key chord accepted");
   rejected=false;try{new ControllerOemPolicy(ControllerOemPolicy.Inputs.ToDictionary(id=>id,id=>new ControllerBinding("gamepad","Guide"))).Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"Guide bypassed last widget assignment guard");
   var retained=new ControllerOemPolicy(new(){{"M1",new("gamepad","Guide")}}).Validate();Check(ControllerOemPolicy.Inputs.Any(id=>retained.For(id).Kind=="widget"),"Safe Guide assignment removes widget access");
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,physicalOwnerInputTested=false},Backend.Program.Json));return 0;
  }catch(Exception error){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=error.Message},Backend.Program.Json));return 2;}
 }
 [StructLayout(LayoutKind.Sequential)]private struct Pad{internal ushort Buttons;internal byte Lt,Rt;internal short Lx,Ly,Rx,Ry;}
 [StructLayout(LayoutKind.Sequential)]private struct State{internal uint Packet;internal Pad Pad;}
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate uint ReadState(uint index,out State state);
 [DllImport("kernel32.dll",EntryPoint="GetProcAddress")]private static extern nint Ordinal(nint library,nint ordinal);
 internal static async Task<int> Native(string file)
 {
  int checks=0;ControllerVirtualPad? pad=null;nint library=0;var slots=ControllerNative.Slots();void Check(bool ok,string message){checks++;if(!ok)throw new IOException(message);}
  try
  {
   Check(slots.SequenceEqual(new[]{0})&&ControllerNative.Read(0,out var physical)&&ControllerNative.Neutral(physical),"Protect physical source before action QA");
   using var window=new ControllerTestWindow();window.RequireForeground();using var emitter=new ControllerInputEmitter();var translator=new ControllerTranslator();var idle=new ControllerFrame();
   foreach(string target in new[]{"left","right","middle","wheelUp","wheelDown","wheelLeft","wheelRight"})
   {
    window.RequireForeground();translator.Reset();var policy=new ControllerPolicy(new(){{"A",new("mouse",target)}}).Validate();int count=window.Events.Count;emitter.Apply(translator.Translate(new(4096),policy),0);emitter.Apply(translator.Translate(new(4096),policy),0);emitter.Apply(translator.Translate(idle,policy),0);await Task.Delay(70);var events=window.Events.Skip(count).ToArray();uint down=target switch{"left"=>0x201,"right"=>0x204,"middle"=>0x207,"wheelLeft" or "wheelRight"=>0x20e,_=>0x20a};Check(events.Count(value=>value.StartsWith(down+":",StringComparison.Ordinal))==1,"Mouse action missing or repeated: "+target);if(target.StartsWith("wheel",StringComparison.Ordinal)){var word=ulong.Parse(events.Single(value=>value.StartsWith(down+":",StringComparison.Ordinal)).Split(':')[1]);short delta=unchecked((short)(word>>16));Check(delta==(target is "wheelUp" or "wheelRight"?120:-120),"Actual wheel direction wrong");}
   }
   var keys=new[]{17,16,65,66,67};Check(keys.All(key=>!ControllerTestWindow.IsHeld(key)),"Protect currently held owner keys");window.RequireForeground();var chord=new ControllerPolicy(new(){{"A",new("keyboard",Keys:keys)}}).Validate();emitter.Apply(translator.Translate(new(4096),chord),0);await Task.Delay(80);Check(keys.All(ControllerTestWindow.IsHeld),"Actual five-key hold incomplete");emitter.Apply(translator.Translate(idle,chord),0);await Task.Delay(80);Check(keys.All(key=>!ControllerTestWindow.IsHeld(key)),"Actual five-key release incomplete");
   // Real Guide readback is separate from UI/input assertions. It may be handled
   // by Windows/Steam, so no more keyboard/mouse input is sent afterwards.
   pad=new ControllerVirtualPad(0,false);library=NativeLibrary.Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"xinput1_4.dll"));nint address=Ordinal(library,100);Check(address!=0,"Guide state API unavailable");var read=Marshal.GetDelegateForFunctionPointer<ReadState>(address);
   async Task Guide(bool expected){for(int n=0;n<80;n++){if(read((uint)pad.Slot,out var state)==0&&((state.Pad.Buttons&0x400)!=0)==expected){Check(true,"Real Guide state");return;}await Task.Delay(5);}throw new IOException("Actual VIIPER Guide state mismatch");}
   var mapped=new ControllerPolicy(new(){{"A",new("gamepad","Guide")}}).Validate();var output=translator.Translate(new(4096),mapped);pad.Submit(output.Pad,output.Guide);await Guide(true);pad.Guide(true);output=translator.Translate(idle,mapped);pad.Submit(output.Pad,output.Guide);await Guide(true);pad.Guide(false);await Guide(false);pad.Submit(new(),true);await Guide(true);pad.Submit(new());await Guide(false);pad.Dispose();pad=null;Check(ControllerNative.Slots().SequenceEqual(slots),"Temporary VIIPER device remained");
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,mouseActions=7,fiveKeyPhysicalApiHoldRelease=true,actualGuideReadback=true,transientAndMappedGuideCombined=true,temporaryVirtualRemoved=true,productionCaptureEnabled=false,physicalOwnerInputTested=false,inputKind="synthetic source frames -> real own-window SendInput and VIIPER XInput",settingsWrites=0},Backend.Program.Json));return 0;
  }catch(Exception error){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=error.Message},Backend.Program.Json));return 2;}
  finally{try{pad?.Dispose();}catch{}if(library!=0)NativeLibrary.Free(library);}
 }
}
