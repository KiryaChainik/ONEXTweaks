// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerGyroStickCurveVerification
{
 private sealed class Capture:IControllerCaptureRuntime{public ControllerRuntimeState Read()=>new(false,false,0,-1,null,[],0);public void Policy(ControllerPolicy policy){}public Task Start()=>Task.CompletedTask;public Task Stop()=>Task.CompletedTask;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
 private static ControllerOutput Basis()=>new(new(),[],[],0,0,0,[]);
 private static ControllerGyroReading Fresh(double x,double y=0)=>new(true,new(y,-x,0),new(0,0,-1),0,0,1);
 private static ControllerGyroPolicy Policy(ControllerGyroStickCurve curve,string target="RightStick")=>new(target,"Always",Processing:new(StickCurve:curve));
 private static double Radius((double X,double Y) vector)=>Math.Sqrt(vector.X*vector.X+vector.Y*vector.Y);
 internal static async Task<int> Models(string file){
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  try{
   var curve=new ControllerGyroStickCurve(true).Validate();
   foreach(var invalid in new[]{curve with{Sensitivity=0},curve with{Sensitivity=401},curve with{Deadzone=51},curve with{MinInput=101},curve with{MaxInput=49},curve with{MaxInput=721},curve with{Deadzone=50,MaxInput=50},curve with{MinInput=70,MaxInput=60},curve with{MinOutput=-1},curve with{MaxOutput=101},curve with{MinOutput=80,MaxOutput=50},curve with{Power=9},curve with{Power=401},curve with{PrecisionSpeed=-1},curve with{PrecisionSpeed=101},curve with{OutputMix=-101},curve with{OutputMix=101}}){bool rejected=false;try{new ControllerGyroProcessing(StickCurve:invalid).Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"Invalid stick curve accepted");}
   Check(curve.Map(0,0)==(0.0,0.0)&&curve.Map(2,0)==(0.0,0.0),"Deadzone created motion at rest/boundary");
   var linear=curve with{Deadzone=0,MaxInput=200};Check(Math.Abs(linear.Map(100,0).X-16383.5)<.01,"Linear midpoint is not half radius");
   Check((linear with{Power=200}).Map(100,0).X<linear.Map(100,0).X&&(linear with{Power=50}).Map(100,0).X>linear.Map(100,0).X,"Power shape is not relaxed/aggressive");
   Check((linear with{Sensitivity=200}).Map(100,0).X>linear.Map(100,0).X,"Curve sensitivity not applied");
   Check((linear with{PrecisionSpeed=100}).Map(30,0).X<linear.Map(30,0).X,"Slow precision range did not damp movement");
   foreach(int power in new[]{10,50,100,200,400}){double previous=0;for(int speed=0;speed<=720;speed+=2){double radius=Radius((curve with{Power=power,MinOutput=20,MaxOutput=70}).Map(speed,speed/3.0));Check(radius>=previous-.0001&&radius<=.7*32767+.01,"Curve is not monotonic/bounded");previous=radius;}}
   var floor=curve with{MinOutput=20,MaxOutput=70};Check(Radius(floor.Map(2.01,0))>=.2*32767&&Radius(floor.Map(1000,1000))<=.7*32767+.01,"Minimum/maximum output radius lost");
   var diagonal=linear.Map(30,-40);Check(Math.Abs(diagonal.X/diagonal.Y+.75)<1e-9,"Balanced curve changed direction");
   Check((linear with{OutputMix=100}).Map(0,100)==(0.0,0.0)&&(linear with{OutputMix=-100}).Map(100,0)==(0.0,0.0),"Full axis bias did not suppress other axis");
   Check(Math.Abs((linear with{OutputMix=50}).Map(30,-40).X/(linear with{OutputMix=50}).Map(30,-40).Y+1.5)<1e-9,"Horizontal bias ratio wrong");
   Check(curve.Map(double.NaN,1)==(0.0,0.0)&&curve.Map(1,double.PositiveInfinity)==(0.0,0.0),"Non-finite curve propagated");
   var old=new ControllerGyroRouter();var off=new ControllerGyroRouter();var legacy=new ControllerGyroPolicy("RightStick","Always",Processing:new(AntiDeadzone:20));
   for(int n=0;n<100;n++){var reading=Fresh((n-50)*3,8);Check(old.Apply(Basis(),new(),legacy,reading,null,0,30,false,n*10,.01)==off.Apply(Basis(),new(),legacy with{Processing=legacy.Processing! with{StickCurve=curve with{Enabled=false}}},reading,null,0,30,false,n*10,.01),"Disabled curve altered legacy output");}
   var mouseA=new ControllerGyroRouter();var mouseB=new ControllerGyroRouter();var mousePolicy=new ControllerGyroPolicy("Mouse","Always",Processing:new("Adaptive",80,80));
   for(int n=0;n<50;n++){var reading=Fresh(n==0?90:1,3);Check(mouseA.Apply(Basis(),new(),mousePolicy,reading,null,0,30,false,n*10,.01)==mouseB.Apply(Basis(),new(),mousePolicy with{Processing=mousePolicy.Processing! with{StickCurve=floor}},reading,null,0,30,false,n*10,.01),"Stored stick-only curve changed Mouse target");}
   foreach(string target in new[]{"LeftStick","RightStick"}){var router=new ControllerGyroRouter();var policy=Policy(floor,target) with{Processing=new("Adaptive",100,100,StickCurve:floor)};var moving=router.Apply(Basis(),new(),policy,Fresh(90),null,0,30,false,0,.01);Check(target=="LeftStick"?moving.Pad.Lx>0:moving.Pad.Rx>0,"Curve routed to wrong stick");Check(router.Apply(Basis(),new(),policy,Fresh(1),null,0,30,false,10,.01).Pad==new ControllerFrame(),"Minimum output amplified filter tail below curve deadzone");}
   var p=Policy(floor);var basis=Basis() with{Pad=new(Rx:30000)};var additive=new ControllerGyroRouter().Apply(basis,new(),p,Fresh(500),null,0,30,false,0,.01);Check(additive.Pad.Rx==32767,"Physical stick addition did not saturate safely");
   Check(new ControllerGyroRouter().Apply(Basis(),new(),p,Fresh(90),null,0,30,true,0,.01).Pad==new ControllerFrame(),"Paused curve emitted input");
   string serialized=JsonSerializer.Serialize(p,Backend.Program.Json);var saved=JsonSerializer.Deserialize<ControllerGyroPolicy>(serialized,Backend.Program.Json)!.Validate();Check(saved==p,"Curve profile roundtrip lost settings");
   var legacyJson=JsonSerializer.Deserialize<ControllerGyroProcessing>("{}",Backend.Program.Json)!.Validate();Check(legacyJson.StickCurve==null&&!JsonSerializer.Serialize(legacyJson,Backend.Program.Json).Contains("stickCurve"),"Legacy processing gained a curve on load");
   Check(ControllerRemappingGroups.ResetGroup(new ControllerPolicy(Gyro:p,Tuning:new(LeftDeadzone:12)),"gyro") is{Gyro:null,Tuning.LeftDeadzone:12},"Gyro defaults touched other profile settings");
   string root=Path.Combine(Path.GetDirectoryName(file)!,"owned-gyrocurve-"+Guid.NewGuid().ToString("N")),reports=Path.Combine(root,"Reports");Directory.CreateDirectory(reports);
   try{
    string id;await using(var profiles=new ControllerProfiles(reports,()=>"fixed",()=>"global",()=>[],capture:new Capture(),defaultVirtualAvailable:()=>false)){
     async Task Change(object value){using var data=JsonSerializer.SerializeToDocument(value,Backend.Program.Json);await profiles.Mutate(data.RootElement);}
     await Change(new{operation="create",name="Owned curve model"});id=profiles.EditingId;await Change(new{operation="edit",id,policy=new ControllerPolicy(Gyro:p)});
     string path=Path.Combine(root,"controller-profiles.json"),before=File.ReadAllText(path);bool rejected=false;try{await Change(new{operation="edit",id,policy=new ControllerPolicy(Gyro:Policy(curve with{MinOutput=80,MaxOutput=50}))});}catch(InvalidDataException){rejected=true;}Check(rejected&&File.ReadAllText(path)==before,"Invalid curve edit rewrote selected profile");
    }
    await using(var profiles=new ControllerProfiles(reports,()=>"fixed",()=>"global",()=>[],capture:new Capture(),defaultVirtualAvailable:()=>false)){using var data=JsonSerializer.SerializeToDocument(profiles.Read(),Backend.Program.Json);var actual=data.RootElement.GetProperty("editor").GetProperty("policy").GetProperty("gyro").GetProperty("processing").GetProperty("stickCurve");Check(profiles.EditingId==id&&actual.GetProperty("enabled").GetBoolean()&&actual.GetProperty("minOutput").GetInt32()==20&&actual.GetProperty("maxOutput").GetInt32()==70,"Selected controller curve failed save/reload");}
   }finally{Directory.Delete(root,true);}
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,ownerPreferenceWrites=0,physicalOwnerAimingTested=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message},Backend.Program.Json));return 2;}
 }
 internal static async Task<int> Native(string file){
  int checks=0;ControllerVirtualPad? pad=null;var before=ControllerNative.Slots();void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  try{
   Check(before.SequenceEqual(new[]{0})&&ControllerNative.Read(0,out var physical)&&ControllerNative.Neutral(physical),"Protect owner active controller/capture");using var window=new ControllerTestWindow();window.RequireForeground();pad=new ControllerVirtualPad(0,false);
   foreach(string target in new[]{"LeftStick","RightStick"})foreach(var c in new[]{new ControllerGyroStickCurve(true),new(true,Power:200),new(true,MinOutput:20,MaxOutput:60),new(true,OutputMix:100),new(true,PrecisionSpeed:80)})foreach(var reading in new[]{Fresh(90,30),Fresh(-90,-30),Fresh(1)}){
    var output=new ControllerGyroRouter().Apply(Basis(),new(),Policy(c,target),reading,null,0,30,false,0,.01);window.RequireForeground();pad.Submit(output.Pad);bool seen=false;for(int n=0;n<100;n++){if(ControllerNative.Read(pad.Slot,out var value)&&value.Lx==output.Pad.Lx&&value.Ly==output.Pad.Ly&&value.Rx==output.Pad.Rx&&value.Ry==output.Pad.Ry&&value.Buttons==0){seen=true;break;}await Task.Delay(5);}Check(seen,"Actual VIIPER/XInput stick curve frame mismatch");
   }
   pad.Submit(new());pad.Dispose();pad=null;Check(ControllerNative.Slots().SequenceEqual(before),"Own temporary curve pad lingered");
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,actualViiperAndXInputReadback=true,syntheticAngularSamples=true,physicalInputRestored=true,mouseKeyInjection=0,ownerPreferenceWrites=0,physicalOwnerAimingTested=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>")},Backend.Program.Json));return 2;}
  finally{try{pad?.Dispose();}catch{}}
 }
}
