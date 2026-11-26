// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerTriggerVerification
{
 private sealed class Capture:IControllerCaptureRuntime
 {public ControllerRuntimeState Read()=>new(false,false,0,-1,null,[],0);public void Policy(ControllerPolicy p){}public Task Start()=>Task.CompletedTask;public Task Stop()=>Task.CompletedTask;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
 internal static async Task<int> Models(string file)
 {
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  try{
   var old=JsonSerializer.Deserialize<ControllerPolicy>("{\"tuning\":{\"triggerThreshold\":35}}",Backend.Program.Json)!.Validate();
   Check(old.Tuning is{LeftTriggerStart:0,LeftTriggerEnd:100,RightTriggerStart:0,RightTriggerEnd:100,HairTriggers:false},"Legacy defaults changed");
   var tx=new ControllerTranslator();for(int n=0;n<=255;n++){var frame=new ControllerFrame(Lt:(byte)n,Rt:(byte)(255-n));Check(tx.Translate(frame,old).Pad is {} p&&p.Lt==n&&p.Rt==255-n,"Legacy analog bytes changed");}
   foreach(var tuning in new[]{new ControllerTuning(LeftTriggerStart:-1),new(LeftTriggerStart:100),new(LeftTriggerEnd:0),new(LeftTriggerEnd:101),new(LeftTriggerStart:50,LeftTriggerEnd:50),new(RightTriggerStart:50,RightTriggerEnd:49),new(RightTriggerStart:-1),new(RightTriggerEnd:101)})
   {bool bad=false;try{new ControllerPolicy(Tuning:tuning).Validate();}catch(InvalidDataException){bad=true;}Check(bad,"Invalid travel accepted");}
   foreach(var pair in new[]{(0,100),(10,60),(20,80),(99,100),(0,1),(80,90)})
   {
    var tune=new ControllerTuning(LeftTriggerStart:pair.Item1,LeftTriggerEnd:pair.Item2,RightTriggerStart:pair.Item1,RightTriggerEnd:pair.Item2);
    var policy=new ControllerPolicy(Tuning:tune).Validate();byte previous=0;
    for(int n=0;n<=255;n++){var p=tx.Translate(new(Lt:(byte)n,Rt:(byte)n),policy).Pad;Check(p.Lt==p.Rt&&p.Lt>=previous,"Trigger range not monotonic/symmetric");previous=p.Lt;}
    Check(tx.Translate(new(),policy).Pad.Lt==0&&tx.Translate(new(Lt:255,Rt:255),policy).Pad is{Lt:255,Rt:255},"Trigger endpoints wrong");
   }
   var independent=new ControllerPolicy(Tuning:new(LeftTriggerEnd:50,RightTriggerStart:20,RightTriggerEnd:80)).Validate();var output=tx.Translate(new(Lt:128,Rt:128),independent);Check(output.Pad.Lt==255&&Math.Abs(output.Pad.Rt-128)<=1,"Left/right ranges not independent");
   foreach(int start in new[]{0,10,30,99}){
    var policy=new ControllerPolicy(Tuning:new(LeftTriggerStart:start,HairTriggers:true)).Validate();
    for(int raw=0;raw<=255;raw++){byte expected=raw*100.0/255<=start||raw*100.0/255<2?(byte)0:(byte)255;Check(tx.Translate(new(Lt:(byte)raw),policy).Pad.Lt==expected,"Hair threshold/noise gate wrong");}
   }
   var stored=new ControllerPolicy(Tuning:new(LeftTriggerStart:10,LeftTriggerEnd:60,RightTriggerStart:15,RightTriggerEnd:80,HairTriggers:true)).Validate();var analog=stored with{Tuning=stored.Tuning! with{HairTriggers=false}};Check(analog.Tuning is{LeftTriggerEnd:60,RightTriggerEnd:80}&&tx.Translate(new(Lt:100),analog).Pad.Lt<255,"Hair Off did not preserve analog travel");
   var mapped=new ControllerPolicy(new(){{"LT",new("keyboard",Keys:[32])}},new(LeftTriggerStart:20,LeftTriggerEnd:80,TriggerThreshold:30)).Validate();Check(!tx.Translate(new(Lt:90),mapped).Keys.Contains(32)&&tx.Translate(new(Lt:100),mapped).Keys.Contains(32),"Mapping threshold did not use tuned trigger");
   Check(tx.Translate(new(Lt:90,Rt:120),stored,overlayOpen:true).Pad is{Lt:90,Rt:120},"Overlay trigger passthrough changed");
   var reset=ControllerRemappingGroups.ResetGroup(stored with{Bindings=new(){{"A",new("gamepad","B")}},Vibration=new(Intensity:55)},"sticks");Check(reset.Tuning is{LeftTriggerStart:0,LeftTriggerEnd:100,RightTriggerStart:0,RightTriggerEnd:100,HairTriggers:false}&&reset.For("A").Target=="B"&&reset.Vibration!.Intensity==55,"Scoped reset changed other settings");
   string root=Path.Combine(Path.GetDirectoryName(file)!,"trigger-model-"+Guid.NewGuid().ToString("N")),reports=Path.Combine(root,"Reports");Directory.CreateDirectory(reports);
   try{
    string id;await using(var profiles=new ControllerProfiles(reports,()=>"fixed",()=>"global",()=>[],capture:new Capture(),defaultVirtualAvailable:()=>false)){
     async Task Change(object request){using var data=JsonSerializer.SerializeToDocument(request,Backend.Program.Json);await profiles.Mutate(data.RootElement);}
     await Change(new{operation="create",name="Owned trigger model"});id=profiles.EditingId;await Change(new{operation="edit",id,policy=stored});
     string path=Path.Combine(root,"controller-profiles.json"),before=File.ReadAllText(path);bool bad=false;try{await Change(new{operation="edit",id,policy=new ControllerPolicy(Tuning:new(LeftTriggerStart:80,LeftTriggerEnd:20))});}catch(InvalidDataException){bad=true;}Check(bad&&File.ReadAllText(path)==before,"Invalid travel did not reject atomically");
    }
    await using(var profiles=new ControllerProfiles(reports,()=>"fixed",()=>"global",()=>[],capture:new Capture(),defaultVirtualAvailable:()=>false)){
     using var dto=JsonSerializer.SerializeToDocument(profiles.Read(),Backend.Program.Json);var tuning=dto.RootElement.GetProperty("editor").GetProperty("policy").GetProperty("tuning");Check(profiles.EditingId==id&&tuning.GetProperty("leftTriggerEnd").GetInt32()==60&&tuning.GetProperty("rightTriggerStart").GetInt32()==15&&tuning.GetProperty("hairTriggers").GetBoolean(),"Travel profile persistence lost");
    }
   }finally{Directory.Delete(root,true);}
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,hardwareWrites=0,physicalOwnerInputTested=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message,modelOnly=true},Backend.Program.Json));return 2;}
 }
 internal static async Task<int> Native(string file)
 {
  int checks=0;ControllerVirtualPad? pad=null;var slots=ControllerNative.Slots();void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  try{
   Check(slots.SequenceEqual(new[]{0})&&ControllerNative.Read(0,out var physical)&&ControllerNative.Neutral(physical),"Protect owner physical input/current capture");
   using var window=new ControllerTestWindow();window.RequireForeground();pad=new ControllerVirtualPad(0,false);var tx=new ControllerTranslator();
   foreach(bool hair in new[]{false,true})foreach(byte raw in new byte[]{0,5,26,52,89,128,153,204,255}){
    var policy=new ControllerPolicy(Tuning:new(LeftTriggerStart:10,LeftTriggerEnd:60,RightTriggerStart:20,RightTriggerEnd:80,HairTriggers:hair)).Validate();window.RequireForeground();var output=tx.Translate(new(Lt:raw,Rt:raw),policy);pad.Submit(output.Pad);
    bool read=false;for(int n=0;n<100;n++){if(ControllerNative.Read(pad.Slot,out var current)&&current.Lt==output.Pad.Lt&&current.Rt==output.Pad.Rt){read=true;break;}await Task.Delay(5);}Check(read,"Actual VIIPER trigger readback mismatch");
   }
   pad.Submit(new());pad.Dispose();pad=null;for(int n=0;n<100&&!ControllerNative.Slots().SequenceEqual(slots);n++)await Task.Delay(20);Check(ControllerNative.Slots().SequenceEqual(slots),"Own temporary VIIPER target not removed");
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,actualViiperReadback=true,source="synthetic trigger frames -> actual VIIPER/XInput",productionCaptureEnabled=false,settingsWrites=0,physicalHidingChanges=0,temporaryVirtualRemoved=true,physicalOwnerInputTested=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>")},Backend.Program.Json));return 2;}finally{try{pad?.Dispose();}catch{}}
 }
}
