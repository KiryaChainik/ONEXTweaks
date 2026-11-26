// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class KeyboardMousePrecisionVerification
{
 private sealed class Capture:IControllerCaptureRuntime
 {public ControllerRuntimeState Read()=>new(false,false,0,-1,null,[],0);public void Policy(ControllerPolicy value){}public Task Start()=>Task.CompletedTask;public Task Stop()=>Task.CompletedTask;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
 internal static async Task<int> Models(string file)
 {
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  try{
   var old=JsonSerializer.Deserialize<KeyboardMousePolicy>("{\"schema\":1,\"mouseSpeed\":125,\"scrollSpeed\":150,\"mouseAcceleration\":false}",Backend.Program.Json)!.Validate();
   Check(old.StickDeadzone==10&&old.HoldAcceleration==0&&old.NudgeStep==10&&old.MouseSpeed==125&&!old.MouseAcceleration,"Old file defaults changed");
   foreach(var bad in new[]{old with{MouseSpeed=0},old with{MouseSpeed=401},old with{StickDeadzone=-1},old with{StickDeadzone=21},old with{HoldAcceleration=-1},old with{HoldAcceleration=101},old with{NudgeStep=0},old with{NudgeStep=51}})
   {bool rejected=false;try{bad.Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"Invalid precision boundary accepted");}
   foreach(int speed in new[]{1,400})Check((old with{MouseSpeed=speed}).Validate().MouseSpeed==speed,"Sensitivity extremes rejected");
   ControllerOutput Axis(short x,int threshold){var p=old with{StickDeadzone=threshold};var tx=new KeyboardMouseTranslator();tx.Translate(new(),p,.01,0);return tx.Translate(new(Rx:x),p,.01,10);}
   Check(Axis(3000,10).CursorX==0&&Axis(3000,0).CursorX>0,"Adjustable deadzone missing");
   Check(Axis(6000,20).CursorX==0&&Axis(6000,10).CursorX>0,"Higher deadzone not applied");
   var policy=(old with{MouseSpeed=100,HoldAcceleration=100}).Validate();var ramp=new KeyboardMouseTranslator();ramp.Translate(new(),policy,.01,0);
   Check(ramp.Translate(new(Rx:32767),policy,.01,10).CursorSpeedScale==1,"Ramp first sample changed");
   Check(Math.Abs(ramp.Translate(new(Rx:32767),policy,.01,510).CursorSpeedScale-1.5)<.001,"Half second ramp wrong");
   Check(ramp.Translate(new(Rx:32767),policy,.01,1010).CursorSpeedScale==2,"Ramp cap wrong");
   Check(ramp.Translate(new(Rx:32767),policy,.01,1510).CursorSpeedScale==2,"Held ramp exceeded2x");
   Check(ramp.Translate(new(Rx:-32768),policy,.01,1520).CursorSpeedScale==1,"Direction change did not reset ramp");
   ramp.Translate(new(),policy,.01,1530);Check(ramp.Translate(new(Rx:-32768,Ry:-32768),policy,.01,1540).CursorSpeedScale==1,"Down-left direction reused neutral sentinel");
   Check(ramp.Translate(new(Rx:-32768,Ry:-32768),policy,.01,4000).CursorSpeedScale==1,"Long clock gap did not reset ramp");
   Check(ramp.Translate(new(Rx:-32768,Ry:-32768),policy,.01,3000).CursorSpeedScale==1,"Clock rollback did not reset ramp");
   ramp.Translate(new(Rx:32767),policy,.01,4010,overlayOpen:true);Check(ramp.Translate(new(Rx:32767),policy,.01,4020).CursorX==0,"Held overlay stick replayed");
   ramp.Translate(new(),policy,.01,4030);Check(ramp.Translate(new(Rx:32767),policy,.01,4040).CursorSpeedScale==1,"Overlay ramp not reset");
   var fnRamp=policy with{FnButton="OEM:M1"};ramp.Reset();ramp.Translate(new(),fnRamp,.01,0);ramp.Translate(new(Rx:32767),fnRamp,.01,1);ramp.Translate(new(Rx:32767),fnRamp,.01,501);Check(ramp.Translate(new(Rx:32767),fnRamp,.01,502,16).CursorX==0,"FN transition reused held movement");
   foreach(string direction in new[]{"up","down","left","right"})
   {
    var p=(old with{Bindings=new(){{"A",new("nudge",direction)}},NudgeStep=17}).Validate();var tx=new KeyboardMouseTranslator();tx.Translate(new(),p,.01,0);var first=tx.Translate(new(4096),p,.01,10);Check(first.CursorNudgeX==(direction=="left"?-17:direction=="right"?17:0)&&first.CursorNudgeY==(direction=="up"?-17:direction=="down"?17:0),"Nudge direction/size wrong");
    Check(tx.Translate(new(4096),p,.01,20).CursorNudgeX==0&&tx.Translate(new(4096),p,.01,30).CursorNudgeY==0,"Held nudge repeated");
    tx.Translate(new(),p,.01,40);Check(tx.Translate(new(4096),p,.01,50) is {} again&&(again.CursorNudgeX!=0||again.CursorNudgeY!=0),"Released nudge did not rearm");
    tx.Reset();Check(tx.Translate(new(4096),p,.01,60) is {} blocked&&blocked.CursorNudgeX==0&&blocked.CursorNudgeY==0,"Reset replayed held nudge");
   }
   var fnPolicy=(old with{FnButton="OEM:M1",FnBindings=new(){{"A",new("nudge","left")}}}).Validate();var layered=new KeyboardMouseTranslator();layered.Translate(new(),fnPolicy,.01,0,16);Check(layered.Translate(new(4096),fnPolicy,.01,10,16).CursorNudgeX==-10,"FN nudge missing");
   foreach(string source in KeyboardMousePolicy.Inputs){var p=(old with{Bindings=new(){{source,new("nudge","right")}}}).Validate();Check(p.For(source).Kind=="nudge","Nudge not assignable to all controls");}
   string root=Path.Combine(Path.GetDirectoryName(file)!,"mouse-precision-model-"+Guid.NewGuid().ToString("N")),reports=Path.Combine(root,"Reports");Directory.CreateDirectory(reports);
   try{
    string path=Path.Combine(root,"keyboard-mouse-settings.json"),legacy="{\"schema\":1,\"mouseSpeed\":125,\"scrollSpeed\":150,\"mouseAcceleration\":false}";
    File.WriteAllText(path,legacy);var settings=new KeyboardMouseSettings(reports);Check(File.ReadAllText(path)==legacy,"Startup rewrote old owner file");
    await using var profiles=new ControllerProfiles(reports,()=>"fixed",()=>"global",()=>[],capture:new Capture(),keyboardSettings:settings,defaultVirtualAvailable:()=>false);
    string controllerPath=Path.Combine(root,"controller-profiles.json"),before=File.ReadAllText(controllerPath);
    async Task Change(object request){using var json=JsonDocument.Parse(JsonSerializer.Serialize(request,Backend.Program.Json));await profiles.MutateKeyboardMouse(json.RootElement);}
    foreach(var pair in new[]{("stickDeadzone",5),("holdAcceleration",75),("nudgeStep",23),("mouseSpeed",400)})await Change(new{operation="tuning",key=pair.Item1,value=pair.Item2});
    Check(settings.Value is {StickDeadzone:5,HoldAcceleration:75,NudgeStep:23,MouseSpeed:400},"Advanced tuning mutation did not persist");
    string precision=File.ReadAllText(path);bool rejected=false;try{await Change(new{operation="tuning",key="nudgeStep",value=51});}catch(InvalidDataException){rejected=true;}Check(rejected&&precision==File.ReadAllText(path),"Invalid tuning did not reject atomically");
    Check(new KeyboardMouseSettings(reports).Value is {StickDeadzone:5,HoldAcceleration:75,NudgeStep:23,MouseSpeed:400},"Advanced settings reload lost");
    await Change(new{operation="bind",source="A",layer="base",binding=new ControllerBinding("nudge","right")});
    await Change(new{operation="resetPrecision"});Check(settings.Value is {StickDeadzone:10,HoldAcceleration:0,NudgeStep:10,MouseSpeed:400,ScrollSpeed:150,MouseAcceleration:false}&&settings.Value.For("A").Kind=="nudge","Precision reset touched other settings");
    Check(File.ReadAllText(controllerPath)==before&&!profiles.KeyboardMouseActive,"Precision changed gamepad profiles or mode");
   }finally{Directory.Delete(root,true);}
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,hardwareWrites=0,physicalOwnerInputTested=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message,modelOnly=true},Backend.Program.Json));return 2;}
 }
 [StructLayout(LayoutKind.Sequential)]private struct Point{internal int X,Y;}
 [DllImport("user32.dll")]private static extern bool GetPhysicalCursorPos(out Point value);
 [DllImport("user32.dll")]private static extern bool SetProcessDPIAware();
 internal static async Task<int> Native(string file)
 {
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  SetProcessDPIAware();
  try{
   using var window=new ControllerTestWindow();window.RequireForeground();using var emitter=new ControllerInputEmitter();
   Point Read(){if(!GetPhysicalCursorPos(out var point))throw new IOException("Physical cursor read failed");return point;}
   foreach(var pair in new[]{("right",10,0),("down",0,10),("left",-10,0),("up",0,-10),("right",37,0)})
   {
    var policy=(new KeyboardMousePolicy(Bindings:new(){{"A",new("nudge",pair.Item1)}},NudgeStep:Math.Max(Math.Abs(pair.Item2),Math.Abs(pair.Item3)))).Validate();var tx=new KeyboardMouseTranslator();tx.Translate(new(),policy,.01,0);window.RequireForeground();var before=Read();
    emitter.Apply(tx.Translate(new(4096),policy,.01,10),.01);await Task.Delay(35);var after=Read();Check(Math.Abs(after.X-before.X-pair.Item2)<=1&&Math.Abs(after.Y-before.Y-pair.Item3)<=1,"Actual nudge pixel readback mismatch");
    window.RequireForeground();emitter.Apply(tx.Translate(new(4096),policy,.01,20),.01);await Task.Delay(30);var held=Read();Check(held.X==after.X&&held.Y==after.Y,"Actual held nudge repeated");
    window.RequireForeground();emitter.Apply(tx.Translate(new(),policy,.01,30),.01);var released=Read();Check(released.X==held.X&&released.Y==held.Y,"Nudge release moved cursor");
   }
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,actualWindowsSendInput=true,actualPhysicalCursorReadback=true,source="synthetic source and controlled clock -> owned foreground only",productionCaptureEnabled=false,physicalOwnerInputTested=false,settingsWrites=0},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>")},Backend.Program.Json));return 2;}
 }
}
