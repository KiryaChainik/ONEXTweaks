// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerMacroVerification
{
 private sealed class Capture:IControllerCaptureRuntime
 {
  public ControllerRuntimeState Read()=>new(false,false,0,-1,null,[],0);
  public void Policy(ControllerPolicy policy){}public Task Start()=>Task.CompletedTask;public Task Stop()=>Task.CompletedTask;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
 }
 internal static async Task<int> Run(string file)
 {
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  void Invalid(ControllerMacro macro){try{macro.Validate();throw new IOException("Invalid macro accepted");}catch(InvalidDataException){checks++;}}
  ControllerPolicy Policy(string mode="once",string[]? targets=null,int interval=80,int press=40)=>new ControllerPolicy(new(){{"A",new("macro",Macro:new(targets??["B","Y"],mode,interval,press))}}).Validate();
  try{
   foreach(var macro in new[]{new ControllerMacro(),new ControllerMacro([]),new ControllerMacro(Enumerable.Repeat("A",33).ToArray()),new ControllerMacro(["unknown"]),new ControllerMacro(["A"],"recordKeys"),new ControllerMacro(["A"],IntervalMs:9),new ControllerMacro(["A"],IntervalMs:501),new ControllerMacro(["A"],PressMs:9),new ControllerMacro(["A"],PressMs:501)})Invalid(macro);
   var source=new[]{"B"};var validated=new ControllerMacro(source).Validate();source[0]="unknown";Check(validated.Buttons![0]=="B","Validation must own its targets");
   Check(new ControllerMacro(Enumerable.Repeat("A",32).ToArray(),"repeatHeld",10,500).Validate().Buttons!.Length==32,"Bounded legal extremes");
   foreach(string mode in new[]{"once","repeatHeld","holdToggle","repeatToggle"}){var policy=Policy(mode);var copy=JsonSerializer.Deserialize<ControllerPolicy>(JsonSerializer.Serialize(policy,Backend.Program.Json),Backend.Program.Json)!.Validate();Check(copy.For("A").Macro!.Mode==mode&&copy.HasMacros,"Profile macro roundtrip");}
   foreach(bool dbl in new[]{false,true}){var oem=dbl?new ControllerOemPolicy(DoublePresses:new(){{"M1",new(true,300,new("macro",Macro:new(["RT"])))}}):new ControllerOemPolicy(new(){{"M1",new("macro",Macro:new(["LT"]))}});var policy=new ControllerPolicy(Oem:oem).Validate();Check(policy.HasMacros&&policy.For(dbl?"M1:Double":"M1").Kind=="macro","OEM Single/Double validation");Check(ControllerRemappingGroups.ResetBinding(policy,"OEM:M1").For("M1").Kind=="disabled","OEM restore default removes macro");}
   var player=new ControllerMacroRunner();HashSet<string> up=[],down=["A"];var once=Policy();
   Check(player.Tick(down,once,0).Length==0&&player.Tick(down,once,10).Length==0,"Held source must not replay after reset");player.Tick(up,once,11);
   Check(player.Tick(down,once,12).SequenceEqual(["B"]),"Once starts first step");Check(player.Tick(up,once,20).SequenceEqual(["B"]),"Once continues after release");
   Check(player.Tick(up,once,52).Length==0,"Press duration ends in release");Check(player.Tick(up,once,92).SequenceEqual(["Y"]),"Next ordered step");
   Check(player.Tick(up,once,132).Length==0&&player.Tick(up,once,172).Length==0,"Once finishes neutral");Check(player.Tick(up,once,500).Length==0,"Once never repeats by itself");
   player.Reset();var repeat=Policy("repeatHeld",["B"]);player.Tick(up,repeat,0);Check(player.Tick(down,repeat,1).SequenceEqual(["B"]),"Repeat while held starts");Check(player.Tick(down,repeat,41).Length==0&&player.Tick(down,repeat,81).SequenceEqual(["B"]),"Repeat has distinct release and next press");Check(player.Tick(up,repeat,82).Length==0,"Repeat stops immediately on source release");
   foreach(string mode in new[]{"holdToggle","repeatToggle"}){player.Reset();var policy=Policy(mode,["B","Y"]);player.Tick(up,policy,0);var first=player.Tick(down,policy,1);Check(first.Contains("B")&&(mode!="holdToggle"||first.Contains("Y")),"Toggle starts");Check(player.Tick(up,policy,2).Contains("B"),"Toggle survives source release");Check(player.Tick(down,policy,3).Length==0&&player.Tick(up,policy,4).Length==0,"Second press stops toggle");}
   player.Reset();var min=Policy(interval:10,press:500);player.Tick(up,min,0);Check(player.Tick(down,min,1).Contains("B")&&player.Tick(up,min,11).Length==0,"Press duration capped by interval");Check(player.Tick(up,min,12).Length==0,"Guaranteed release sample");Check(player.Tick(up,min,19).Contains("Y"),"Bounded next step after release gap");
   player.Reset();player.Tick(up,once,0);player.Tick(down,once,1);Check(player.Tick(up,once,900).Length==0&&player.Tick(up,once,901).Length==0,"Stall never bursts missed presses");Check(player.Tick(up,once,940).Contains("Y"),"Stall restarts bounded next phase");
   player.Reset();var hold=Policy("holdToggle");player.Tick(up,hold,0);player.Tick(down,hold,1);Check(player.Tick(down,hold,1600).Length==0&&player.Tick(down,hold,1601).Length==0,"Suspend-sized gap cancels and blocks held source");player.Tick(up,hold,1602);Check(player.Tick(down,hold,1603).Contains("B"),"Release allows fresh activation");Check(player.Tick(down,hold,10).Length==0,"Clock rollback cancels");
   player.Reset();player.Tick(up,once,0);Check(!player.NeedsTick&&player.Tick(down,once,90000).Contains("B"),"Fresh input after idle must start without extra ticking");Check(player.NeedsTick,"Active sequence must keep worker ticking");
   var tx=new ControllerTranslator();tx.Translate(new(),once,milliseconds:0);var output=tx.Translate(new(Buttons:4096),once,milliseconds:1);Check(output.Pad.Buttons==8192,"Macro replaces its source rather than leaks A");
   output=tx.Translate(new(Buttons:8192),once,milliseconds:41);Check(output.Pad.Buttons==8192,"Macro release does not release unrelated physical button");
   tx.Reset();var two=new ControllerPolicy(new(){{"A",new("macro",Macro:new(["B"],"holdToggle"))},{"X",new("macro",Macro:new(["B","RT"],"holdToggle"))}}).Validate();tx.Translate(new(),two,milliseconds:0);output=tx.Translate(new(Buttons:20480),two,milliseconds:1);Check(output.Pad.Buttons==8192&&output.Pad.Rt==255,"Simultaneous macro targets merge");
   tx.Translate(new(),two,milliseconds:2);output=tx.Translate(new(Buttons:4096),two,milliseconds:3);Check(output.Pad.Buttons==8192&&output.Pad.Rt==255,"Stopping one macro leaves another source held");
   tx.Translate(new(),two,true,4);output=tx.Translate(new(Buttons:20480),two,milliseconds:5);Check(output.Pad.Buttons==0&&output.Pad.Rt==0,"Overlay cancels toggles and no held-source replay");tx.Translate(new(),two,milliseconds:6);output=tx.Translate(new(Buttons:4096),two,milliseconds:7);Check(output.Pad.Buttons==8192,"Fresh input allowed after overlay");
   tx.Reset();output=tx.Translate(new(Buttons:4096),two,milliseconds:8);Check(output.Pad.Buttons==0,"Revision reset blocks held source");Check(ControllerRemappingGroups.ResetGroup(two,"standard").HasMacros==false,"Group default removes macro");
   tx.Reset();var disabled=two with{GamepadRemappingDisabled=true};tx.Translate(new(),disabled,milliseconds:0);output=tx.Translate(new(Buttons:4096),disabled,milliseconds:1);Check(output.Pad.Buttons==4096,"Group Off restores physical source and does not run stored macro");
   tx.Reset();var axes=Policy("holdToggle",["LT","Guide","RSRight","LSUp"]);tx.Translate(new(),axes,milliseconds:0);output=tx.Translate(new(Buttons:4096),axes,milliseconds:1);Check(output.Pad.Lt==255&&output.Guide&&output.Pad.Rx==32767&&output.Pad.Ly==32767,"Axes triggers and Guide output use same merger");
   string ownRoot=Path.Combine(Path.GetDirectoryName(file)!,"macro-model-"+Guid.NewGuid().ToString("N")),reports=Path.Combine(ownRoot,"Reports");Directory.CreateDirectory(reports);
   try{
    string id;async Task Change(ControllerProfiles profiles,object value){using var json=JsonDocument.Parse(JsonSerializer.Serialize(value,Backend.Program.Json));await profiles.Mutate(json.RootElement);}
    await using(var profiles=new ControllerProfiles(reports,()=>"fixed",()=>"global",()=>[],capture:new Capture(),defaultVirtualAvailable:()=>false)){
     await Change(profiles,new{operation="create",name="Owned macro model"});id=profiles.EditingId;await Change(profiles,new{operation="edit",id,policy=two});
     using var dto=JsonSerializer.SerializeToDocument(profiles.Read(),Backend.Program.Json);Check(dto.RootElement.GetProperty("editor").GetProperty("policy").GetProperty("bindings").GetProperty("A").GetProperty("macro").GetProperty("mode").GetString()=="holdToggle","Profile manager persists macro");
     string before=File.ReadAllText(Path.Combine(ownRoot,"controller-profiles.json"));bool rejected=false;try{await Change(profiles,new{operation="edit",id,policy=new ControllerPolicy(new(){{"A",new("macro",Macro:new(["invalid"]))}})});}catch(InvalidDataException){rejected=true;}Check(rejected&&File.ReadAllText(Path.Combine(ownRoot,"controller-profiles.json"))==before,"Invalid macro must reject atomically");
    }
    await using(var profiles=new ControllerProfiles(reports,()=>"fixed",()=>"global",()=>[],capture:new Capture(),defaultVirtualAvailable:()=>false)){Check(profiles.EditingId==id,"Selected macro profile survives reload");using var dto=JsonSerializer.SerializeToDocument(profiles.Read(),Backend.Program.Json);Check(dto.RootElement.GetProperty("editor").GetProperty("policy").GetProperty("bindings").GetProperty("X").GetProperty("macro").GetProperty("buttons").GetArrayLength()==2,"Saved sequence survives reload");await Change(profiles,new{operation="resetBinding",id,source="A"});dto.Dispose();using var reset=JsonSerializer.SerializeToDocument(profiles.Read(),Backend.Program.Json);var bindings=reset.RootElement.GetProperty("editor").GetProperty("policy").GetProperty("bindings");Check(!bindings.TryGetProperty("A",out _)&&bindings.TryGetProperty("X",out _),"Per-button default preserves other macro");await Change(profiles,new{operation="delete",id});Check(profiles.EditingId=="global","Only owned profile deleted");}
   }finally{Directory.Delete(ownRoot,true);}
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,hardwareWrites=0,physicalOwnerInputTested=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message,modelOnly=true},Backend.Program.Json));return 2;}
 }
 internal static async Task<int> Native(string file)
 {
  int checks=0;ControllerVirtualPad? pad=null;var slots=ControllerNative.Slots();void Check(bool value,string why){checks++;if(!value)throw new IOException(why);}
  try{
   Check(slots.SequenceEqual(new[]{0})&&ControllerNative.Read(0,out var physical)&&ControllerNative.Neutral(physical),"Protect owner physical input and current virtual output");
   using var window=new ControllerTestWindow();window.RequireForeground();pad=new ControllerVirtualPad(0,false);
   async Task Read(ControllerFrame expected){for(int n=0;n<100;n++){if(ControllerNative.Read(pad.Slot,out var state)&&state.Buttons==expected.Buttons&&state.Lt==expected.Lt&&state.Rt==expected.Rt&&state.Lx==expected.Lx&&state.Ly==expected.Ly&&state.Rx==expected.Rx&&state.Ry==expected.Ry){Check(true,"Actual VIIPER state readback");return;}await Task.Delay(5);}throw new IOException("Macro virtual output mismatch");}
   async Task Submit(ControllerTranslator translator,ControllerPolicy policy,ControllerFrame source,long now,bool overlay=false){window.RequireForeground();var output=translator.Translate(source,policy,overlay,now);pad.Submit(output.Pad,output.Guide);await Read(output.Pad);}
   var tx=new ControllerTranslator();var policy=new ControllerPolicy(new(){{"A",new("macro",Macro:new(["B","RT","LSUp"],"once"))}}).Validate();
   await Submit(tx,policy,new(),0);await Submit(tx,policy,new(Buttons:4096),1);await Submit(tx,policy,new(),41);await Submit(tx,policy,new(),81);await Submit(tx,policy,new(),121);await Submit(tx,policy,new(),161);await Submit(tx,policy,new(),201);await Submit(tx,policy,new(),241);
   foreach(string mode in new[]{"repeatHeld","holdToggle","repeatToggle"}){
    tx.Reset();policy=new ControllerPolicy(new(){{"A",new("macro",Macro:new(["B","Y"],mode))}}).Validate();await Submit(tx,policy,new(),0);await Submit(tx,policy,new(Buttons:4096),1);await Submit(tx,policy,new(),2);await Submit(tx,policy,new(Buttons:4096),3);await Submit(tx,policy,new(),4);await Submit(tx,policy,new(),5,true);await Submit(tx,policy,new(Buttons:4096),6);
   }
   pad.Submit(new());await Read(new());pad.Dispose();pad=null;for(int n=0;n<100&&!ControllerNative.Slots().SequenceEqual(slots);n++)await Task.Delay(20);Check(ControllerNative.Slots().SequenceEqual(slots),"Own temporary VIIPER device not removed");
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,actualViiperReadback=true,modes=4,inputKind="synthetic source and controlled clock -> actual VIIPER/XInput readback",productionCaptureEnabled=false,physicalOwnerInputTested=false,settingsWrites=0,physicalHidingChanges=0,temporaryVirtualRemoved=true},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>")},Backend.Program.Json));return 2;}finally{try{pad?.Dispose();}catch{}}
 }

}
