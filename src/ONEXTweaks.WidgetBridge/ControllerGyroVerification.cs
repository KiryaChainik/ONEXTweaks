// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using System.Runtime.InteropServices;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerGyroVerification
{
 private sealed class Capture:IControllerCaptureRuntime
 {public ControllerRuntimeState Read()=>new(false,false,0,-1,null,[],0);public void Policy(ControllerPolicy p){}public Task Start()=>Task.CompletedTask;public Task Stop()=>Task.CompletedTask;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
 private sealed class Samples:IControllerGyroStream
 {internal bool Enabled,Disposed;public void Configure(bool v){Enabled=v;}public ControllerGyroReading Read()=>new(Enabled,new(0,-90,0),new(0,0,-1),0,0,1);public ValueTask DisposeAsync(){Enabled=false;Disposed=true;return ValueTask.CompletedTask;}}
 private static ControllerOutput Basis(ControllerFrame frame=default)=>new(frame,[],[],0,0,0,[]);
 private static ControllerGyroReading Fresh(MotionVector v)=>new(true,v,new(0,0,-1),0,0,1);
 internal static async Task<int> Models(string file){
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  try{
   Check(JsonSerializer.Deserialize<ControllerPolicy>("{}",Backend.Program.Json)!.Validate().Gyro==null,"Legacy profile enabled gyro");
   foreach(var invalid in new[]{new ControllerGyroPolicy(Target:"DS4Motion"),new(Activation:"bad"),new(ActivationButton:"OEM:Onex"),new(SensitivityX:0),new(SensitivityY:101),new(BoostPercent:9),new(BoostPercent:301)}){
    bool rejected=false;try{new ControllerPolicy(Gyro:invalid).Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"Unsupported gyro setting accepted");
   }
   var live=Fresh(new(0,-90,0));var settings=new ControllerGyroPolicy("RightStick","Always");var router=new ControllerGyroRouter();
   var output=router.Apply(Basis(),new(),settings,live,null,0,30,false,0,.01);Check(output.Pad.Rx==16384&&output.Pad.Ry==0&&router.Active,"Linear right-stick mapping wrong");
   router.Reset();output=router.Apply(Basis(new(Lx:1000,Ly:-2000,Rx:123,Ry:456)),new(),settings with{Target="LeftStick"},Fresh(new(-45,-90,0)),null,0,30,false,0,.01);
   Check(output.Pad.Lx==17384&&output.Pad.Ly==6192&&output.Pad.Rx==123&&output.Pad.Ry==456,"Left stick merge/vertical sign wrong");
   foreach(int sign in new[]{-1,1}){
    router.Reset();output=router.Apply(Basis(new(Rx:(short)(sign*30000))),new(),settings,Fresh(new(0,-sign*180,0)),null,0,30,false,0,.01);Check(output.Pad.Rx==(sign>0?32767:-32768),"Analog saturation wrong");
   }
   foreach(string target in ControllerGyroPolicy.Targets){
    router.Reset();var p=settings with{Target=target};var unchanged=Basis(new(4096,Lt:100,Lx:1234));output=router.Apply(unchanged,new(),p,live,null,0,30,true,0,.01);Check(output==unchanged&&!router.Active,"Overlay gyro bypass changed input");
   }
   // Hold reads the physical source, not a remapped output button.
   router.Reset();var hold=settings with{Activation="Hold",ActivationButton="LB"};
   Check(router.Apply(Basis(),new(),hold,live,null,0,30,false,0,.01).Pad.Rx==0,"Hold active without button");
   Check(router.Apply(Basis(),new(256),hold,live,null,0,30,false,10,.01).Pad.Rx>0,"Physical shoulder hold ignored");
   Check(router.Apply(Basis(),new(),hold,live,null,0,30,false,20,.01).Pad.Rx==0,"Released hold left gyro output");
   var mapped=new ControllerTranslator().Translate(new(256),new ControllerPolicy(new(){{"LB",new("disabled")}}).Validate());
   Check(router.Apply(mapped,new(256),hold,live,null,0,30,false,30,.01).Pad.Rx>0,"Custom remap swallowed physical gyro button");
   foreach(string button in new[]{"LT","RT"}){
    router.Reset();var p=hold with{ActivationButton=button};
    Check(router.Apply(Basis(),new(Lt:75,Rt:75),p,live,null,0,30,false,0,.01).Pad.Rx==0,"Trigger activated below raw threshold");
    Check(router.Apply(Basis(),new(Lt:77,Rt:77),p,live,null,0,30,false,10,.01).Pad.Rx>0,"Trigger threshold hold ignored");
   }
   foreach(string button in new[]{"M1","M2"}){
    router.Reset();ushort mask=(ushort)(1<<Array.IndexOf(ControllerOemPolicy.Inputs,button));var p=hold with{ActivationButton="OEM:"+button};
    Check(router.Apply(Basis(),new(),p,live,null,mask,30,false,0,.01).Pad.Rx>0,"Verified rear hold ignored");
    Check(router.Apply(Basis(),new(),p,live,null,0,30,false,10,.01).Pad.Rx==0,"Unavailable rear hold retained output");
   }
   router.Reset();var toggle=hold with{Activation="Toggle"};
   Check(router.Apply(Basis(),new(256),toggle,live,null,0,30,false,0,.01).Pad.Rx==0,"Initial held button toggled unexpectedly");
   router.Apply(Basis(),new(),toggle,live,null,0,30,false,10,.01);
   Check(router.Apply(Basis(),new(256),toggle,live,null,0,30,false,20,.01).Pad.Rx>0,"Fresh toggle did not activate");
   for(int n=3;n<15;n++)Check(router.Apply(Basis(),new(256),toggle,live,null,0,30,false,n*10,.01).Pad.Rx>0,"Held toggle flipped repeatedly");
   router.Apply(Basis(),new(),toggle,live,null,0,30,false,150,.01);
   Check(router.Apply(Basis(),new(),toggle,live with{GyroAgeMilliseconds=1000},null,0,30,false,160,.01).Pad.Rx==0,"Stale angular rate emitted movement");
   Check(router.Apply(Basis(),new(),toggle,live,null,0,30,false,170,.01).Pad.Rx>0,"Idle on-change pause lost toggle latch");
   Check(router.Apply(Basis(),new(256),toggle,live,null,0,30,false,180,.01).Pad.Rx==0,"Second toggle did not deactivate");
   // Every hard pause requires a fresh toggle edge; no inherited latch after profile/clock/source loss.
   for(int mode=0;mode<5;mode++){
    router.Reset();router.Apply(Basis(),new(),toggle,live,null,0,30,false,0,.01);router.Apply(Basis(),new(256),toggle,live,null,0,30,false,10,.01);
    var failed=mode==0?live with{Ready=false}:mode==1?live with{AccelerationAgeMilliseconds=300}:mode==2?live with{Angular=new(double.NaN,0,0)}:live;
    router.Apply(Basis(),new(256),toggle,failed,null,0,30,mode==3,mode==4?500:20,.01);
    Check(router.Apply(Basis(),new(256),toggle,live,null,0,30,false,mode==4?510:30,.01).Pad.Rx==0,"Hard pause inherited toggle state");
   }
   foreach(int sensitivity in new[]{1,10,50,70,100})foreach(int boost in new[]{10,50,100,200,300}){
    router.Reset();var p=new ControllerGyroPolicy("Mouse","Always",SensitivityX:sensitivity,SensitivityY:sensitivity,BoostButton:"LB",BoostPercent:boost);
    int sum=0;for(int n=0;n<100;n++)sum+=router.Apply(Basis(),new(256),p,Fresh(new(0,-10,0)),null,0,30,false,n*10,.01).RelativeMouseX;
    double expected=120*sensitivity/70.0*boost/100.0;Check(Math.Abs(sum-expected)<1.01,"Mouse sensitivity/boost rate not integrated consistently");
   }
   int Total(int rate){var t=new ControllerGyroRouter();var p=new ControllerGyroPolicy("Mouse","Always");int total=0;for(int n=0;n<rate;n++)total+=t.Apply(Basis(),new(),p,Fresh(new(0,-13,0)),null,0,30,false,n*1000/rate,1.0/rate).RelativeMouseX;return total;}
   Check(Math.Abs(Total(50)-Total(100))<=1&&Math.Abs(Total(100)-Total(125))<=1,"Output depends on input-loop frame rate");
   router.Reset();var bias=new GyroIdleCalibration(1,DateTimeOffset.UtcNow,.18,-.06,.12,1,3000,"OnChange",192);
   Check(router.Apply(Basis(),new(),new("Mouse","Always"),Fresh(new(.18,-.06,.12)),bias,0,30,false,0,.01).RelativeMouseX==0,"Shared calibration not subtracted");
   router.Reset();var slow=new ControllerGyroPolicy("Mouse","Hold","LB",SensitivityX:1);
   router.Apply(Basis(),new(256),slow,Fresh(new(0,-1,0)),null,0,30,false,0,.01);router.Apply(Basis(),new(),slow,live,null,0,30,false,10,.01);
   Check(router.Apply(Basis(),new(256),slow,Fresh(new(0,-1,0)),null,0,30,false,20,.01).RelativeMouseX==0,"Mouse remainder leaked through pause");
   var policy=new ControllerPolicy(Tuning:new(LeftDeadzone:7),Gyro:settings).Validate();
   Check(ControllerRemappingGroups.ResetGroup(policy,"gyro") is{Gyro:null,Tuning.LeftDeadzone:7},"Gyro Restore Default changed stick settings");
   string root=Path.Combine(Path.GetDirectoryName(file)!,"gyro319-model-"+Guid.NewGuid().ToString("N")),reports=Path.Combine(root,"Reports");Directory.CreateDirectory(reports);
   try{
    string id;await using(var profiles=new ControllerProfiles(reports,()=>"fixed",()=>"global",()=>[],capture:new Capture(),defaultVirtualAvailable:()=>false)){
     async Task Change(object request){using var doc=JsonSerializer.SerializeToDocument(request,Backend.Program.Json);await profiles.Mutate(doc.RootElement);}
     await Change(new{operation="create",name="Owned gyro profile"});id=profiles.EditingId;await Change(new{operation="edit",id,policy});
     string path=Path.Combine(root,"controller-profiles.json"),before=File.ReadAllText(path);bool bad=false;try{await Change(new{operation="edit",id,policy=policy with{Gyro=settings with{BoostPercent=301}}});}catch(InvalidDataException){bad=true;}Check(bad&&File.ReadAllText(path)==before,"Invalid gyro edit changed saved profile");
    }
    await using(var profiles=new ControllerProfiles(reports,()=>"fixed",()=>"global",()=>[],capture:new Capture(),defaultVirtualAvailable:()=>false)){
     using var dto=JsonSerializer.SerializeToDocument(profiles.Read(),Backend.Program.Json);var gyro=dto.RootElement.GetProperty("editor").GetProperty("policy").GetProperty("gyro");Check(profiles.EditingId==id&&gyro.GetProperty("target").GetString()=="RightStick"&&gyro.GetProperty("sensitivityX").GetInt32()==70,"Gyro selection did not persist/reload");
    }
   }finally{Directory.Delete(root,true);}
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,hardwareWrites=0,physicalGameAcceptanceTested=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,modelOnly=true,error=e.Message},Backend.Program.Json));return 2;}
 }
 [StructLayout(LayoutKind.Sequential)]private struct Point{internal int X,Y;}
 [DllImport("user32.dll")]private static extern bool GetPhysicalCursorPos(out Point value);
 [DllImport("user32.dll")]private static extern bool SetProcessDPIAware();
 internal static async Task<int> Native(string file){
  int checks=0;ControllerVirtualPad? pad=null;ControllerRemapper? runtime=null;Samples? input=null;var slots=ControllerNative.Slots();void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  try{
   Check(slots.SequenceEqual(new[]{0})&&ControllerNative.Read(0,out var physical)&&ControllerNative.Neutral(physical),"Protect owner physical controls/current capture");
   await using(var sensors=new ControllerGyroStream()){
    Check(!sensors.Read().Ready,"Disabled stream accessed sensors");sensors.Configure(true);
    for(int n=0;n<200&&!sensors.Read().Ready;n++)await Task.Delay(20);
    var sample=sensors.Read();Check(sample.Ready&&sample.Angular.Finite&&sample.Acceleration.Finite&&sample.Sequence>=1,"Real streaming Windows gyro unavailable");
    sensors.Configure(false);Check(!sensors.Read().Ready,"Disabled stream still ready");
    for(int n=0;n<200&&sensors.Read().TemporaryParametersRestored!=true;n++)await Task.Delay(20);
    Check(sensors.Read().TemporaryParametersRestored==true,"Real stream did not restore sensor properties");
   }
   SetProcessDPIAware();using var window=new ControllerTestWindow();window.RequireForeground();pad=new ControllerVirtualPad(0,false);var router=new ControllerGyroRouter();
   foreach(string target in new[]{"LeftStick","RightStick"})foreach(int sign in new[]{-1,1}){
    router.Reset();var p=new ControllerGyroPolicy(target,"Always");var output=router.Apply(Basis(),new(),p,Fresh(new(-45*sign,-90*sign,0)),null,0,30,false,0,.01);window.RequireForeground();pad.Submit(output.Pad);
    bool seen=false;for(int n=0;n<100;n++){if(ControllerNative.Read(pad.Slot,out var observed)&&observed.Lx==output.Pad.Lx&&observed.Ly==output.Pad.Ly&&observed.Rx==output.Pad.Rx&&observed.Ry==output.Pad.Ry){seen=true;break;}await Task.Delay(5);}Check(seen,"Actual gyro VIIPER stick readback differs");
   }
   pad.Submit(new());pad.Dispose();pad=null;Check(ControllerNative.Slots().SequenceEqual(slots),"Temporary gyro virtual pad lingered");
   using(var emitter=new ControllerInputEmitter()){
    foreach(var pair in new[]{new MotionVector(0,-30,0),new(0,30,0),new(30,0,0),new(-30,0,0)}){
     router.Reset();var p=new ControllerGyroPolicy("Mouse","Always");window.RequireForeground();Check(GetPhysicalCursorPos(out var before),"Cursor read before gyro failed");
     for(int n=0;n<10;n++){window.RequireForeground();emitter.Apply(router.Apply(Basis(),new(),p,Fresh(pair),null,0,30,false,n*10,.01),.01);await Task.Delay(10);}
     Check(GetPhysicalCursorPos(out var after),"Cursor read after gyro failed");Check(pair.Y<0?after.X>before.X:pair.Y>0?after.X<before.X:pair.X>0?after.Y>before.Y:after.Y<before.Y,"Real relative SendInput gyro direction wrong");
     window.RequireForeground();var paused=router.Apply(Basis(),new(),p,Fresh(pair),null,0,30,true,110,.01);emitter.Apply(paused,.01);await Task.Delay(20);Check(GetPhysicalCursorPos(out var stopped)&&stopped.X==after.X&&stopped.Y==after.Y,"Paused gyro still moved actual cursor");
    }
   }
   // Exercise the changed production capture loop, its policy transition and recovery.
   window.RequireForeground();string reports=Path.GetDirectoryName(file)!;
   Check(!File.Exists(ControllerCaptureLease.LeaseFile(reports)),"Protect active capture lease");
   input=new Samples();runtime=new ControllerRemapper(reports,gyroFactory:()=>input);runtime.Policy(new(Gyro:new("RightStick","Always")));await runtime.Start();
   bool active=false;for(int n=0;n<160;n++){var state=runtime.Read();if(state.Running&&ControllerNative.Read(state.VirtualSlot,out var v)&&ControllerNative.Read(0,out var source)&&v.Rx==Math.Clamp(source.Rx+16384,-32768,32767)&&state.Gyro?.Active==true){active=true;break;}await Task.Delay(10);}Check(active,"Production remapper loop did not consume gyro stream");
   runtime.Policy(new());bool disabled=false;for(int n=0;n<120;n++){var state=runtime.Read();if(ControllerNative.Read(state.VirtualSlot,out var v)&&ControllerNative.Read(0,out var source)&&v.Rx==source.Rx&&!input.Enabled){disabled=true;break;}await Task.Delay(10);}Check(disabled,"Policy disable did not neutralize gyro in real capture");
   await runtime.Stop();Check(input.Disposed&&ControllerNative.Slots().SequenceEqual(slots)&&!File.Exists(ControllerCaptureLease.LeaseFile(reports)),"Real physical input/stream not recovered after stop");await runtime.DisposeAsync();runtime=null;
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,actualWindowsSensorStream=true,actualViiperAndXInput=true,actualRelativeSendInputAndPhysicalCursorReadback=true,syntheticGyroFramesForOutput=true,temporaryRealCaptureLoopTested=true,physicalInputRestored=true,productionPreferenceNotEnabled=true,ownerSettingsWrites=0,firmwareWrites=0,physicalOwnerAimingTested=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>")},Backend.Program.Json));return 2;}
  finally{if(runtime!=null)try{await runtime.DisposeAsync();}catch{}try{pad?.Dispose();}catch{}}
 }

}
