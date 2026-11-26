// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerDs4AuxVerification
{
 internal static async Task<int> Models(string file){
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  try{
   var policy=new ControllerPolicy(Bindings:new(){["A"]=new("gamepad","TouchpadClick")},VirtualTarget:"DualShock4",ForwardMotion:true).Validate();
   var legacy=JsonSerializer.Deserialize<ControllerPolicy>("{}",Backend.Program.Json)!;Check(!legacy.ForwardMotion&&!JsonSerializer.Serialize(legacy,Backend.Program.Json).Contains("forwardMotion"),"Legacy motion forwarding changed");
   var roundtrip=JsonSerializer.Deserialize<ControllerPolicy>(JsonSerializer.Serialize(policy,Backend.Program.Json),Backend.Program.Json)!;Check(roundtrip.ForwardMotion&&roundtrip.Bindings!["A"].Target=="TouchpadClick","Aux policy did not roundtrip");
   var translator=new ControllerTranslator();ControllerOutput Map(ControllerFrame f,ControllerPolicy? p=null,bool overlay=false)=>translator.Translate(f,p??policy,overlay,100);
   Check(Map(new(0x1000)).TouchpadClick&&Map(new(0x1000)).Pad.Buttons==0,"Touch mapping did not consume original A");
   Check(!Map(new()).TouchpadClick,"Touch click stayed pressed after source release");Check(!Map(new(0x1000),policy with{VirtualTarget="Xbox360"}).TouchpadClick&&Map(new(0x1000),policy with{VirtualTarget="Xbox360"}).Pad.Buttons==0x1000,"Xbox emitted unsupported DS4 click");
   Check(!Map(new(0x1000),overlay:true).TouchpadClick,"Overlay emitted game touch click");
   Check(!Map(new(0x1000),policy with{GamepadRemappingDisabled=true}).TouchpadClick&&Map(new(0x1000),policy with{GamepadRemappingDisabled=true}).Pad.Buttons==0x1000,"Disabled standard group still remapped");
   var extra=policy with{Bindings=null,Oem=new(Bindings:new(){["M1"]=new("gamepad","TouchpadClick")})};Check(Map(new(Oem:16),extra).TouchpadClick,"OEM did not reach DS4 touch target");
   var macroPolicy=policy with{Bindings=new(){["A"]=new("macro",Macro:new(["TouchpadClick"],"holdToggle"))}};var macroTranslator=new ControllerTranslator();macroTranslator.Translate(new(),macroPolicy,false,0);Check(macroTranslator.Translate(new(0x1000),macroPolicy,false,10).TouchpadClick,"Macro could not hold DS4 touch click");Check(macroTranslator.Translate(new(),macroPolicy,false,20).TouchpadClick,"Hold macro lost touch state");macroTranslator.Translate(new(),macroPolicy,true,30);Check(!macroTranslator.Translate(new(),macroPolicy,false,40).TouchpadClick,"Overlay did not cancel touch macro");
   var source=new ControllerGyroReading(true,new(10,-20,30),new(.5,-.25,-1),1,1,42);var imu=ControllerDs4Motion.Read(source,null,true);
   Check(imu==new ControllerDs4Motion(160,-320,480,4096,-2048,-8192),"Motion unit/sign conversion wrong");
   var calibration=new GyroIdleCalibration(1,DateTimeOffset.UtcNow,.5,-.25,1,100,2000).Validate();var corrected=ControllerDs4Motion.Read(source,calibration,true);Check(corrected.GyroX==152&&corrected.GyroY==-316&&corrected.GyroZ==464&&corrected.AccelZ==-8192,"Idle bias removed gravity or changed units");
   Check(ControllerDs4Motion.Read(source,null,false)==ControllerDs4Motion.Neutral,"Disabled motion not neutral");
   foreach(var invalid in new[]{source with{Ready=false},source with{AccelerationAgeMilliseconds=251},source with{AccelerationAgeMilliseconds=-1},source with{GyroAgeMilliseconds=-1},source with{Angular=new(double.NaN,0,0)},source with{Angular=new(2001,0,0)},source with{Acceleration=new(65,0,0)},source with{Acceleration=new(double.PositiveInfinity,0,0)}})Check(ControllerDs4Motion.Read(invalid,null,true)==ControllerDs4Motion.Neutral,"Invalid/stale sensor sample escaped");
   Check(ControllerDs4Motion.Read(source with{GyroAgeMilliseconds=501},calibration,true)==new ControllerDs4Motion(0,0,0,4096,-2048,-8192),"Old gyro rate accumulated while stationary");
   var limits=ControllerDs4Motion.Read(source with{Angular=new(-2000,2000,0),Acceleration=new(-8,8,0)},null,true);Check(limits.GyroX==-32000&&limits.GyroY==32000&&limits.AccelX==short.MinValue&&limits.AccelY==short.MaxValue,"Sensor saturation overflowed");
   var state=ControllerViiperDs4Device.Encode(new(),false,true,imu);Check(state.Buttons==2&&state.GyroX==160&&state.AccelY==-2048,"DS4 encoder dropped auxiliary packet");
   Check(state.Touch1Active==0&&state.Touch2Active==0,"Whole-pad click acquired a synthetic contact");
   var zones=new ControllerPolicy(Bindings:new(){["A"]=new("gamepad","TouchpadClickLeft"),["B"]=new("gamepad","TouchpadClickRight")},VirtualTarget:"DualShock4").Validate();
   zones=JsonSerializer.Deserialize<ControllerPolicy>(JsonSerializer.Serialize(zones,Backend.Program.Json),Backend.Program.Json)!.Validate();
   foreach(var (mask,expected,coordinate) in new (ushort,Ds4TouchContacts,ushort)[]{(0x1000,Ds4TouchContacts.Left,480),(0x2000,Ds4TouchContacts.Right,1440)})
   {
    var frame=new ControllerFrame(mask);
    var mapped=Map(frame,zones);
    Check(mapped.TouchpadClick&&mapped.TouchpadContacts==expected&&mapped.Pad.Buttons==0,"DS4 zone did not consume source and report its half");
    var report=ControllerViiperDs4Device.Encode(mapped.Pad,mapped.Guide,mapped.TouchpadClick,contacts:mapped.TouchpadContacts);
    Check(report.Buttons==2&&report.Touch1Active==1&&report.Touch1X==coordinate&&report.Touch1Y==471&&report.Touch2Active==0,"DS4 zone did not encode one contact plus click");
    var xbox=Map(frame,zones with{VirtualTarget="Xbox360"});
    Check(xbox.Pad.Buttons==mask&&!xbox.TouchpadClick&&xbox.TouchpadContacts==Ds4TouchContacts.None,"Xbox lost original input or emitted a DS4 zone");
   }
   var both=Map(new(0x3000),zones);
   var two=ControllerViiperDs4Device.Encode(both.Pad,touchClick:both.TouchpadClick,contacts:both.TouchpadContacts);
   Check(two.Buttons==2&&two.Touch1X==480&&two.Touch2X==1440&&two.Touch1Active==1&&two.Touch2Active==1,"Simultaneous halves did not encode both contacts");
   var released=Map(new(),zones);
   Check(!released.TouchpadClick&&released.TouchpadContacts==Ds4TouchContacts.None,"Releasing the source left an active click/contact");
   var redirected=Map(new(0x3000),zones,overlay:true);
   Check(!redirected.TouchpadClick&&redirected.TouchpadContacts==Ds4TouchContacts.None,"Opening Game Bar leaked a touchpad zone");
   Check(ControllerViiperDs4Device.Encode(new()).AccelZ==-8192&&!Map(new()).TouchpadClick,"Default DS4 packet changed");
   Check(ControllerDs4Motion.Status(true,true,true,source).StartsWith("Paused")&&ControllerDs4Motion.Status(true,false,false,source)=="Requires DualShock 4"&&ControllerDs4Motion.Status(true,true,false,new())=="Waiting for motion sensor","Motion status is misleading");
   Check((policy with{KeyboardMouse=new()}).OutputTarget=="Xbox360"&&(policy with{KeyboardMouse=new()}).ForwardMotion,"KBM lost dormant motion preference");
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,ownerPreferenceWrites=0,physicalOwnerInputTested=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message},Backend.Program.Json));return 2;}
 }
 private static bool Matches(byte[] b,ControllerDs4Motion m)=>BitConverter.ToInt16(b,13)==m.GyroX&&BitConverter.ToInt16(b,15)==m.GyroY&&BitConverter.ToInt16(b,17)==m.GyroZ&&BitConverter.ToInt16(b,19)==m.AccelX&&BitConverter.ToInt16(b,21)==m.AccelY&&BitConverter.ToInt16(b,23)==m.AccelZ;
 private static int TouchX(byte[] b)=>b[36]|(b[37]&15)<<8;
 private static int TouchY(byte[] b)=>b[37]>>4|b[38]<<4;
 [StructLayout(LayoutKind.Sequential)]private struct Point{internal int X,Y;}
 [DllImport("user32.dll")]private static extern nint WindowFromPhysicalPoint(Point point);
 [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint window,out uint pid);
 [DllImport("user32.dll")]private static extern bool SetPhysicalCursorPos(int x,int y);
 [DllImport("user32.dll")]private static extern bool GetPhysicalCursorPos(out Point point);
 private static bool OwnSurface(Point point){try{GetWindowThreadProcessId(WindowFromPhysicalPoint(point),out uint pid);using var process=Process.GetProcessById((int)pid);string path=process.MainModule!.FileName;return path.Contains("ONEXTweaks.Widget_",StringComparison.OrdinalIgnoreCase)||path.Contains("Microsoft.XboxGamingOverlay_",StringComparison.OrdinalIgnoreCase);}catch{return false;}}
 internal static async Task<int> Transport(string file,bool overlayFixture=false){
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}ControllerOutputDevice? device=null;ControllerDs4HidReader? hid=null;ControllerTestWindow? window=null;var before=ControllerNative.Slots();string? run=null;Point point=default,previous=default;string local=WidgetPackageIdentity.LocalState;
  try{
   Check(before.SequenceEqual(new[]{0})&&ControllerNative.Read(0,out var physical)&&ControllerNative.Neutral(physical),"Protect owner physical controller");
   if(overlayFixture){
    for(int n=0;n<600;n++){try{using var state=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(local,"onex-ds4-aux-qa-state.json")));var root=state.RootElement;if(root.GetProperty("active").GetBoolean()&&DateTimeOffset.UtcNow-DateTimeOffset.Parse(root.GetProperty("utc").GetString()!)<TimeSpan.FromSeconds(4)&&Guid.TryParse(root.GetProperty("run").GetString(),out _)){point=new(){X=root.GetProperty("x").GetInt32(),Y=root.GetProperty("y").GetInt32()};if(NativeGameBarState.IsInputRedirected()&&OwnSurface(point)){run=root.GetProperty("run").GetString();break;}}}catch{}await Task.Delay(100);}
    if(run==null)throw new IOException("Own live UWP auxiliary fixture was not confirmed");GetPhysicalCursorPos(out previous);
   }else{window=new();window.RequireForeground();}
   void Protect(){if(!overlayFixture){window!.RequireForeground();return;}if(!NativeGameBarState.IsInputRedirected()||!OwnSurface(point))throw new IOException("Auxiliary input refused: own UWP surface is not active");if(!SetPhysicalCursorPos(point.X,point.Y)||!OwnSurface(point))throw new IOException("Auxiliary input refused: own UWP target moved");}
   Protect();device=new(0,()=>new(),"DualShock4");hid=await ControllerDs4HidReader.OpenOwned(device.Serial!);
   foreach(var motion in new[]{new ControllerDs4Motion(160,0,0,0,0,-8192),new ControllerDs4Motion(0,-320,0,0,0,-8192),new ControllerDs4Motion(0,0,480,0,0,-8192),new ControllerDs4Motion(0,0,0,4096,0,0),new ControllerDs4Motion(0,0,0,0,-2048,0),new ControllerDs4Motion(0,0,0,0,0,-8192)}){Protect();device.Submit(new(),imu:motion);await hid.ReadUntil(b=>Matches(b,motion));Check(true,"Actual auxiliary motion axis not transmitted");}
   var translator=new ControllerTranslator();var policy=new ControllerPolicy(Bindings:new(){["A"]=new("gamepad","TouchpadClick")},VirtualTarget:"DualShock4");Protect();var press=translator.Translate(new(0x1000),policy,false,100);device.Submit(press.Pad,press.Guide,press.TouchpadClick);await hid.ReadUntil(b=>(b[7]&3)==2&&(b[5]&0xf0)==0);Check(true,"Mapped touch click not transmitted through actual output");
   Protect();var release=translator.Translate(new(),policy,false,110);device.Submit(release.Pad,release.Guide,release.TouchpadClick);await hid.ReadUntil(b=>(b[7]&3)==0&&Matches(b,ControllerDs4Motion.Neutral));Check(true,"Mapped touch click or motion did not release");
   foreach(var (target,x) in new[]{("TouchpadClickLeft",480),("TouchpadClickRight",1440)})
   {
    var zone=new ControllerPolicy(Bindings:new(){["A"]=new("gamepad",target)},VirtualTarget:"DualShock4").Validate();
    var clicked=translator.Translate(new(0x1000),zone,false,120);
    Protect();device.Submit(clicked.Pad,clicked.Guide,clicked.TouchpadClick,contacts:clicked.TouchpadContacts);
    await hid.ReadUntil(b=>(b[7]&3)==2&&(b[5]&0xf0)==0&&(b[35]&0x80)==0&&(b[39]&0x80)!=0&&TouchX(b)==x&&TouchY(b)==471);
    Check(true,"Mapped DS4 half did not produce the expected HID contact and click");
    var up=translator.Translate(new(),zone,false,130);
    Protect();device.Submit(up.Pad,up.Guide,up.TouchpadClick,contacts:up.TouchpadContacts);
    await hid.ReadUntil(b=>(b[7]&3)==0&&(b[35]&0x80)!=0&&(b[39]&0x80)!=0);
    Check(true,"DS4 touchpad half did not release");
   }
   string serial=device.Serial!;hid.Dispose();hid=null;device.Dispose();device=null;Check(!await ControllerDs4HidReader.IsOwnedPresent(serial)&&ControllerNative.Slots().SequenceEqual(before),"Own endpoint not removed");
   if(run!=null)await File.WriteAllTextAsync(Path.Combine(local,"onex-ds4-aux-qa-ack.json"),JsonSerializer.Serialize(new{run,passed=true,checks},Backend.Program.Json));
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,actualDs4HidReadback=true,splitTouchpadHidVerified=true,syntheticMotionAndSourceButton=true,physicalOwnerInputTested=false,ownerPreferenceWrites=0,physicalLedWrites=0,ownUwpFixture=overlayFixture},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>")},Backend.Program.Json));return 2;}
  finally{hid?.Dispose();device?.Dispose();window?.Dispose();if(overlayFixture&&run!=null)SetPhysicalCursorPos(previous.X,previous.Y);}
 }
 private sealed class Sensor:IControllerGyroStream{
  private readonly ControllerGyroStream inner=new();internal ControllerGyroReading Last=new();public void Configure(bool enabled)=>inner.Configure(enabled);public ControllerGyroReading Read(){var result=inner.Read();Volatile.Write(ref Last,result);return result;}public ValueTask DisposeAsync()=>inner.DisposeAsync();
 }
 internal static async Task<int> Motion(string file){
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}string reports=Path.GetDirectoryName(file)!,owner=Path.Combine(reports,"..","controller-profiles.json");byte[] original=await File.ReadAllBytesAsync(owner);ControllerHideState before;using(var hide=new ControllerHidHide())before=hide.Read();ControllerRemapper? runtime=null;ControllerDs4HidReader? hid=null;ControllerOutputDevice? output=null;var sensor=new Sensor();bool paused=false;string? error=null;bool sensorRestored=false;
  try{
   using(var preferences=JsonDocument.Parse(original))Check(!preferences.RootElement.GetProperty("enabled").GetBoolean(),"Protect owner virtual preference");
   Check(!File.Exists(ControllerCaptureLease.LeaseFile(reports))&&ControllerNative.Slots().SequenceEqual(new[]{0})&&ControllerNative.Read(0,out var physical)&&ControllerNative.Neutral(physical),"Protect owner active controller");
   runtime=new(reports,motionBusy:()=>Volatile.Read(ref paused),gyroFactory:()=>sensor,outputFactory:(source,vibration)=>output=new(source,vibration,"DualShock4"));var policy=new ControllerPolicy(VirtualTarget:"DualShock4",ForwardMotion:true);runtime.Policy(policy);await runtime.Start();hid=await ControllerDs4HidReader.OpenOwned(output!.Serial!);
   for(int n=0;n<200&&(!Volatile.Read(ref sensor.Last).Ready||runtime.Read().MotionForwarding!="Forwarding");n++)await Task.Delay(25);
   var sample=Volatile.Read(ref sensor.Last);Check(sample.Ready&&sample.AccelerationAgeMilliseconds<=250&&sample.Acceleration.Length is >.7 and <1.3,"Fresh actual stationary Windows sensor missing");
   // Acceleration jitters slightly between the independently sampled source and HID stream.
   var expected=ControllerDs4Motion.Read(sample,null,true);await hid.ReadUntil(b=>Math.Abs(BitConverter.ToInt16(b,19)-expected.AccelX)<=64&&Math.Abs(BitConverter.ToInt16(b,21)-expected.AccelY)<=64&&Math.Abs(BitConverter.ToInt16(b,23)-expected.AccelZ)<=64);Check(runtime.Read().MotionForwarding=="Forwarding","Actual WinRT acceleration did not reach DS4");
   Volatile.Write(ref paused,true);await hid.ReadUntil(b=>Matches(b,ControllerDs4Motion.Neutral));for(int n=0;n<50&&!runtime.Read().MotionForwarding!.StartsWith("Paused");n++)await Task.Delay(20);Check(runtime.Read().MotionForwarding!.StartsWith("Paused"),"Configuration pause did not neutralize motion");
   Volatile.Write(ref paused,false);for(int n=0;n<200&&runtime.Read().MotionForwarding!="Forwarding";n++)await Task.Delay(25);Check(runtime.Read().MotionForwarding=="Forwarding","Motion did not resume after pause");
   runtime.Policy(policy with{ForwardMotion=false});await hid.ReadUntil(b=>Matches(b,ControllerDs4Motion.Neutral));for(int n=0;n<50&&runtime.Read().MotionForwarding!="Disabled";n++)await Task.Delay(20);Check(runtime.Read().MotionForwarding=="Disabled","Turning motion off left old IMU active");
   hid.Dispose();hid=null;await runtime.DisposeAsync();runtime=null;Check(!File.Exists(ControllerCaptureLease.LeaseFile(reports))&&(await ControllerRemapper.Observe(reports)).SequenceEqual(new[]{0}),"Physical controller not restored");
   sensorRestored=Volatile.Read(ref sensor.Last).TemporaryParametersRestored==true||sensor.Read().TemporaryParametersRestored==true;Check(sensorRestored,"Sensor report settings not restored");
  }catch(Exception e){error=e.Message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>");}
  finally{
   hid?.Dispose();if(runtime!=null)try{await runtime.DisposeAsync();}catch(Exception e){error=(error??"")+"; "+e.Message;}await sensor.DisposeAsync();try{await ControllerCaptureLease.RecoverStopped(reports);}catch(Exception e){error=(error??"")+"; "+e.Message;}
   ControllerHideState after;using(var hide=new ControllerHidHide())after=hide.Read();bool hideRestored=before.Active==after.Active&&before.Inverse==after.Inverse&&before.Devices.SequenceEqual(after.Devices)&&before.Applications.SequenceEqual(after.Applications);byte[] current=await File.ReadAllBytesAsync(owner);bool preferences=original.SequenceEqual(current);
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=error==null&&hideRestored&&preferences,checks,error,hidHideRestored=hideRestored,ownerControllerPreferencesPreserved=preferences,actualWindowsSensors=true,actualDs4HidReadback=true,pauseInjected=true,sensorParametersRestored=sensorRestored,physicalOwnerMovementTested=false,ownerPreferenceWrites=0},Backend.Program.Json));
  }
  using var result=JsonDocument.Parse(await File.ReadAllTextAsync(file));return result.RootElement.GetProperty("passed").GetBoolean()?0:2;
 }
}
