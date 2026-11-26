// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerGyroConversionVerification
{
 private sealed class Capture:IControllerCaptureRuntime{public ControllerRuntimeState Read()=>new(false,false,0,-1,null,[],0);public void Policy(ControllerPolicy p){}public Task Start()=>Task.CompletedTask;public Task Stop()=>Task.CompletedTask;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
 private static ControllerOutput Basis()=>new(new(),[],[],0,0,0,[]);
 private static ControllerGyroReading Reading(MotionVector angular)=>new(true,angular,new(0,0,-1),0,0,1);
 internal static async Task<int> Models(string file){
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  try{
   var vector=new MotionVector(10,-20,-30);var gravity=new MotionVector(0,0,-1);
   foreach(var pair in new[]{("Yaw",20.0),("Roll",30.0),("YawRoll",50.0)})foreach(int rotation in new[]{0,90,180,270})foreach(bool invert in new[]{false,true}){
    var p=new ControllerGyroProcessing(OutputRotation:rotation,InvertX:invert,StickConversion:pair.Item1);Check(ControllerGyroAxes.TryResolve(vector,gravity,p,out double x,out double y),"Valid local conversion blocked");
    (double X,double Y) expected=rotation switch{90=>(-10,pair.Item2),180=>(-pair.Item2,-10),270=>(10,-pair.Item2),_=>(pair.Item2,10)};if(invert)expected.X=-expected.X;Check(x==expected.X&&y==expected.Y,"Local conversion/rotation/inversion mismatch");
   }
   var random=new Random(1405);for(int n=0;n<100;n++){
    var angular=new MotionVector(random.Next(-100,100),random.Next(-100,100),random.Next(-100,100));ControllerGyroAxes.TryResolve(angular,gravity,null,out double oldX,out double oldY);ControllerGyroAxes.TryResolve(angular,gravity,new(StickConversion:"Yaw"),out double x,out double y);Check(oldX==x&&oldY==y,"Explicit Yaw changed old local mapping");
   }
   foreach(var g in new[]{new MotionVector(0,-1,0),gravity,new(-1,0,0)})foreach(string source in new[]{"Yaw","Roll","YawRoll"}){
    Check(ControllerGyroAxes.TryResolve(vector,g,new(WorldSpace:true),out double oldX,out double oldY)&&ControllerGyroAxes.TryResolve(vector,g,new(WorldSpace:true,StickConversion:source),out double x,out double y)&&oldX==x&&oldY==y,"Dormant local selection changed gravity frame");
   }
   bool invalid=false;try{new ControllerGyroPolicy(Processing:new(StickConversion:"invalid")).Validate();}catch(InvalidDataException){invalid=true;}Check(invalid,"Invalid source conversion accepted");
   var oldJson=JsonSerializer.Deserialize<ControllerGyroProcessing>("{}",Backend.Program.Json)!.Validate();Check(oldJson.StickConversion==null&&!JsonSerializer.Serialize(oldJson,Backend.Program.Json).Contains("stickConversion"),"Legacy JSON acquired a source selection");
   var mouseA=new ControllerGyroRouter();var mouseB=new ControllerGyroRouter();var mp=new ControllerGyroPolicy("Mouse","Always",Processing:new("Adaptive",80,80));
   for(int n=0;n<60;n++){var r=Reading(vector with{Z=n-30});Check(mouseA.Apply(Basis(),new(),mp,r,null,0,30,false,n*10,.01)==mouseB.Apply(Basis(),new(),mp with{Processing=mp.Processing! with{StickConversion="YawRoll"}},r,null,0,30,false,n*10,.01),"Inactive stick source changed Mouse output/filtering");}
   foreach(string source in new[]{"Yaw","Roll","YawRoll"})foreach(string target in new[]{"LeftStick","RightStick"}){
    var p=new ControllerGyroPolicy(target,"Always",Processing:new(StickConversion:source));var output=new ControllerGyroRouter().Apply(Basis(),new(),p,Reading(vector),null,0,30,false,0,.01);Check(target=="LeftStick"?output.Pad.Lx>0&&output.Pad.Rx==0:output.Pad.Rx>0&&output.Pad.Lx==0,"Source conversion routed to wrong stick");
    Check(new ControllerGyroRouter().Apply(Basis(),new(),p,Reading(vector),null,0,30,true,0,.01).Pad==new ControllerFrame(),"Paused conversion emitted input");
   }
   var yawOnly=new ControllerGyroRouter().Apply(Basis(),new(),new("RightStick","Always",Processing:new(StickConversion:"Yaw")),Reading(new(0,0,-90)),null,0,30,false,0,.01);var rollOnly=new ControllerGyroRouter().Apply(Basis(),new(),new("RightStick","Always",Processing:new(StickConversion:"Roll")),Reading(new(0,0,-90)),null,0,30,false,0,.01);Check(yawOnly.Pad.Rx==0&&rollOnly.Pad.Rx>16000,"Roll motion did not select distinct angular axis");
   var curve=new ControllerGyroStickCurve(true,MaxOutput:60);var savedPolicy=new ControllerPolicy(Tuning:new(LeftDeadzone:7),Gyro:new("RightStick","Always",Processing:new("Precision",20,70,WorldSpace:false,StickCurve:curve,StickConversion:"YawRoll")));
   string root=Path.Combine(Path.GetDirectoryName(file)!,"owned-gyro-conversion-"+Guid.NewGuid().ToString("N")),reports=Path.Combine(root,"Reports");Directory.CreateDirectory(reports);
   try{
    string id;await using(var profiles=new ControllerProfiles(reports,()=>"fixed",()=>"global",()=>[],capture:new Capture(),defaultVirtualAvailable:()=>false)){async Task Change(object value){using var data=JsonSerializer.SerializeToDocument(value,Backend.Program.Json);await profiles.Mutate(data.RootElement);}await Change(new{operation="create",name="Owned source model"});id=profiles.EditingId;await Change(new{operation="edit",id,policy=savedPolicy});string path=Path.Combine(root,"controller-profiles.json"),before=File.ReadAllText(path);bool rejected=false;try{await Change(new{operation="edit",id,policy=savedPolicy with{Gyro=savedPolicy.Gyro! with{Processing=new(StickConversion:"bad")}}});}catch(InvalidDataException){rejected=true;}Check(rejected&&File.ReadAllText(path)==before,"Invalid source edit changed profile file");}
    await using(var profiles=new ControllerProfiles(reports,()=>"fixed",()=>"global",()=>[],capture:new Capture(),defaultVirtualAvailable:()=>false)){using var data=JsonSerializer.SerializeToDocument(profiles.Read(),Backend.Program.Json);var p=data.RootElement.GetProperty("editor").GetProperty("policy");Check(profiles.EditingId==id&&p.GetProperty("gyro").GetProperty("processing").GetProperty("stickConversion").GetString()=="YawRoll"&&p.GetProperty("gyro").GetProperty("processing").GetProperty("stickCurve").GetProperty("maxOutput").GetInt32()==60&&p.GetProperty("tuning").GetProperty("leftDeadzone").GetInt32()==7,"Source selection did not persist with other profile settings");}
   }finally{Directory.Delete(root,true);}
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,ownerPreferenceWrites=0,physicalOwnerAimingTested=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message},Backend.Program.Json));return 2;}
 }
 internal static async Task<int> Native(string file){
  int checks=0;ControllerVirtualPad? pad=null;var before=ControllerNative.Slots();void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  try{
   Check(before.SequenceEqual(new[]{0})&&ControllerNative.Read(0,out var physical)&&ControllerNative.Neutral(physical),"Protect current physical input/capture");using var window=new ControllerTestWindow();window.RequireForeground();pad=new ControllerVirtualPad(0,false);
   foreach(string source in new[]{"Yaw","Roll","YawRoll"})foreach(string target in new[]{"LeftStick","RightStick"})foreach(var angular in new[]{new MotionVector(10,-20,-30),new(-10,20,30),new(0,0,-90)}){
    var p=new ControllerGyroPolicy(target,"Always",Processing:new(StickConversion:source));var output=new ControllerGyroRouter().Apply(Basis(),new(),p,Reading(angular),null,0,30,false,0,.01);window.RequireForeground();pad.Submit(output.Pad);bool seen=false;for(int n=0;n<100;n++){if(ControllerNative.Read(pad.Slot,out var actual)&&actual.Lx==output.Pad.Lx&&actual.Ly==output.Pad.Ly&&actual.Rx==output.Pad.Rx&&actual.Ry==output.Pad.Ry&&actual.Buttons==0){seen=true;break;}await Task.Delay(5);}Check(seen,"Actual VIIPER source-conversion frame mismatch");
   }
   pad.Submit(new());pad.Dispose();pad=null;Check(ControllerNative.Slots().SequenceEqual(before),"Own conversion pad lingered");await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,actualViiperAndXInputReadback=true,syntheticAngularSamples=true,physicalInputRestored=true,mouseKeyInjection=0,ownerPreferenceWrites=0,physicalOwnerAimingTested=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>")},Backend.Program.Json));return 2;}finally{try{pad?.Dispose();}catch{}}
 }
}
