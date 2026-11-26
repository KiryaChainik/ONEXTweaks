// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerLayoutVerification
{
 private sealed class Capture:IControllerCaptureRuntime
 {public ControllerRuntimeState Read()=>new(false,false,0,-1,null,[],0);public void Policy(ControllerPolicy p){}public Task Start()=>Task.CompletedTask;public Task Stop()=>Task.CompletedTask;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
 internal static async Task<int> Models(string file)
 {
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  try{
   var on=new ControllerPolicy(Tuning:new(NintendoLayout:true)).Validate();var off=new ControllerPolicy().Validate();var tx=new ControllerTranslator();
   var pairs=new[]{("A","B"),("B","A"),("X","Y"),("Y","X")};
   foreach(var pair in pairs){var source=new ControllerFrame(ControllerPolicy.Buttons[pair.Item1]);Check(tx.Translate(source,on).Pad.Buttons==ControllerPolicy.Buttons[pair.Item2],"Face swap wrong");Check(tx.Translate(source,off).Pad.Buttons==source.Buttons,"Off changed original");Check(tx.Translate(source,on,true).Pad.Buttons==source.Buttons,"Widget navigation swapped");Check(tx.Translate(source,on with{GamepadRemappingDisabled=true}).Pad.Buttons==source.Buttons,"Full Remapping Off did not bypass layout");}
   for(int mask=0;mask<16;mask++){
    ushort source=0,expected=0;for(int n=0;n<4;n++)if((mask&(1<<n))!=0){source|=ControllerPolicy.Buttons[pairs[n].Item1];expected|=ControllerPolicy.Buttons[pairs[n].Item2];}
    var frame=new ControllerFrame((ushort)(source|1|256),Lt:39,Rt:170,Lx:13245,Ly:-4567,Rx:-12345,Ry:32767);var result=tx.Translate(frame,on).Pad;
    Check(result.Buttons==(expected|1|256)&&result.Lt==39&&result.Rt==170&&result.Lx==13245&&result.Ly==-4567&&result.Rx==-12345&&result.Ry==32767,"Combined faces affected other controls");
    Check(ControllerTranslator.NintendoButtons(ControllerTranslator.NintendoButtons(frame.Buttons))==frame.Buttons,"Layout is not reversible");
   }
   var mapped=new ControllerPolicy(new(){{"A",new("gamepad","X")}},new(NintendoLayout:true)).Validate();Check(tx.Translate(new(4096),mapped).Pad.Buttons==32768,"Layout must run after custom target");
   var macro=new ControllerPolicy(new(){{"A",new("macro",Macro:new(["X"],"holdToggle"))}},new(NintendoLayout:true)).Validate();tx.Reset();tx.Translate(new(),macro,milliseconds:0);Check(tx.Translate(new(4096),macro,milliseconds:1).Pad.Buttons==32768,"Layout must run after macro merger");
   var km=new KeyboardMouseTranslator();var keyboard=new KeyboardMousePolicy().Validate();km.Translate(new(),keyboard,.01,0);Check(km.Translate(new(4096),keyboard,.01,10).Keys.SetEquals(new[]{32}),"Separate keyboard/mouse defaults changed");
   var tuning=new ControllerPolicy(Tuning:new(LeftDeadzone:50,RightDeadzone:50,NintendoLayout:true)).Validate();Check(tx.Translate(new(Lx:16000,Ry:-16000),tuning).Pad is{Lx:0,Ry:0},"50%deadzone not applied");bool bad=false;try{new ControllerPolicy(Tuning:new(LeftDeadzone:51)).Validate();}catch(InvalidDataException){bad=true;}Check(bad,"Unsupported deadzone accepted");
   Check(ControllerRemappingGroups.ResetGroup(tuning,"sticks").Tuning is{NintendoLayout:false,LeftDeadzone:0,RightDeadzone:0},"Scoped Restore Default did not restore layout");
   var old=JsonSerializer.Deserialize<ControllerPolicy>("{\"tuning\":{\"leftDeadzone\":7}}",Backend.Program.Json)!.Validate();Check(old.Tuning is{NintendoLayout:false,LeftDeadzone:7},"Old layout/deadzone preference changed");
   string root=Path.Combine(Path.GetDirectoryName(file)!,"layout-model-"+Guid.NewGuid().ToString("N")),reports=Path.Combine(root,"Reports");Directory.CreateDirectory(reports);
   try{
    string id;await using(var profiles=new ControllerProfiles(reports,()=>"fixed",()=>"global",()=>[],capture:new Capture(),defaultVirtualAvailable:()=>false)){
     async Task Change(object request){using var doc=JsonSerializer.SerializeToDocument(request,Backend.Program.Json);await profiles.Mutate(doc.RootElement);}
     await Change(new{operation="create",name="Owned layout model"});id=profiles.EditingId;await Change(new{operation="edit",id,policy=tuning});using var state=JsonSerializer.SerializeToDocument(profiles.Read(),Backend.Program.Json);Check(state.RootElement.GetProperty("editor").GetProperty("policy").GetProperty("tuning").GetProperty("nintendoLayout").GetBoolean(),"Profile did not save layout");
    }
    await using(var profiles=new ControllerProfiles(reports,()=>"fixed",()=>"global",()=>[],capture:new Capture(),defaultVirtualAvailable:()=>false)){using var state=JsonSerializer.SerializeToDocument(profiles.Read(),Backend.Program.Json);Check(profiles.EditingId==id&&state.RootElement.GetProperty("editor").GetProperty("policy").GetProperty("tuning").GetProperty("leftDeadzone").GetInt32()==50&&state.RootElement.GetProperty("editor").GetProperty("policy").GetProperty("tuning").GetProperty("nintendoLayout").GetBoolean(),"Profile reload lost layout");}
   }finally{Directory.Delete(root,true);}
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,hardwareWrites=0,physicalOwnerInputTested=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message,modelOnly=true},Backend.Program.Json));return 2;}
 }
 internal static async Task<int> Native(string file)
 {
  int checks=0;ControllerVirtualPad? pad=null;var slots=ControllerNative.Slots();void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  try{
   Check(slots.SequenceEqual(new[]{0})&&ControllerNative.Read(0,out var physical)&&ControllerNative.Neutral(physical),"Protect owner physical controls/current capture");
   using var window=new ControllerTestWindow();window.RequireForeground();pad=new ControllerVirtualPad(0,false);var tx=new ControllerTranslator();
   async Task Submit(ControllerFrame frame,ControllerPolicy policy,bool overlay=false){window.RequireForeground();var output=tx.Translate(frame,policy,overlay);pad.Submit(output.Pad,output.Guide);bool match=false;for(int n=0;n<100;n++){if(ControllerNative.Read(pad.Slot,out var current)&&current.Buttons==output.Pad.Buttons&&current.Lt==output.Pad.Lt&&current.Rt==output.Pad.Rt){match=true;break;}await Task.Delay(5);}Check(match,"Actual layout VIIPER readback mismatch");}
   var policy=new ControllerPolicy(Tuning:new(NintendoLayout:true)).Validate();
   for(int n=0;n<16;n++)await Submit(new((ushort)((n<<12)|256),Lt:13,Rt:223),policy);
   await Submit(new(4096),policy with{Tuning=policy.Tuning! with{NintendoLayout=false}});await Submit(new(4096),policy with{GamepadRemappingDisabled=true});await Submit(new(4096),policy,true);
   await Submit(new(4096),new ControllerPolicy(new(){{"A",new("gamepad","X")}},new(NintendoLayout:true)).Validate());
   pad.Submit(new());pad.Dispose();pad=null;for(int n=0;n<100&&!ControllerNative.Slots().SequenceEqual(slots);n++)await Task.Delay(20);Check(ControllerNative.Slots().SequenceEqual(slots),"Own layout virtual target not removed");
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,actualViiperReadback=true,source="synthetic face combinations -> actual VIIPER/XInput",productionCaptureEnabled=false,settingsWrites=0,physicalHidingChanges=0,temporaryVirtualRemoved=true,physicalOwnerInputTested=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>")},Backend.Program.Json));return 2;}finally{try{pad?.Dispose();}catch{}}
 }
}
