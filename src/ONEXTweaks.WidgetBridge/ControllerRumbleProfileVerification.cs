// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Concurrent;
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerRumbleProfileVerification
{
 private sealed class Capture:IControllerCaptureRuntime
 {public ControllerRuntimeState Read()=>new(false,false,0,-1,null,[],0);public void Policy(ControllerPolicy p){}public Task Start()=>Task.CompletedTask;public Task Stop()=>Task.CompletedTask;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
 internal static async Task<int> Models(string file)
 {
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  try{
   var expected=new[]{("Balanced",200,80),("Sharp",160,96),("Soft",130,52),("Impact",240,64),("Boosted",250,100)};
   foreach(var row in expected){
    var value=new ControllerVibration(100,row.Item1).Validate();Check(value.Scale(200,80)==((byte)row.Item2,(byte)row.Item3),"Preset response example wrong");Check((value with{SwapMotors=true}).Scale(200,80)==((byte)row.Item3,(byte)row.Item2),"Swap response order wrong");
    foreach(int intensity in new[]{0,1,25,50,100})for(int raw=0;raw<=255;raw++){
     var normal=value with{Intensity=intensity};var output=normal.Scale((byte)raw,(byte)(255-raw));int cap=(255*intensity+50)/100;Check(output.Large<=cap&&output.Small<=cap&&(intensity!=0||output==(0,0)),"Intensity ceiling or zero silence broken");
     Check((normal with{SwapMotors=true}).Scale((byte)raw,(byte)(255-raw))==(output.Small,output.Large),"Swap/integer rounding not symmetric");
    }
    Check(value.Scale(0,0)==(0,0),"Preset emitted vibration for silence");
   }
   bool bad=false;try{new ControllerVibration(Profile:"unknown").Validate();}catch(InvalidDataException){bad=true;}Check(bad,"Unknown preset accepted");
   var old=JsonSerializer.Deserialize<ControllerVibration>("{\"intensity\":37}",Backend.Program.Json)!.Validate();Check(old is{Intensity:37,Profile:"Balanced",SwapMotors:false}&&old.Scale(200,100)==(74,37),"Legacy intensity changed");
   ControllerVibration current=new(50,"Sharp");var calls=new ConcurrentQueue<(byte,byte)>();using(var router=new ControllerRumbleForwarder(()=>current,(l,s)=>calls.Enqueue((l,s)))){
    router.Enable();router.Feedback(200,80);Check(calls.Last()==(80,48),"Preset not used for feedback");
    current=current with{Profile="Impact",SwapMotors=true};router.Refresh();Check(calls.Last()==(32,120),"Live preset/swap change not reapplied");
    var pulse=router.Test(new(25,"Soft",true),30);router.Feedback(100,220);current=new(50,"Boosted",true);await pulse;Check(calls.Last()==current.Scale(100,220),"Test did not restore latest profile and callback");
    current=current with{Intensity=0};router.Refresh();Check(calls.Last()==(0,0),"Zero failed after boosted swap");
   }
   Check(calls.Last()==(0,0),"Dispose did not stop rumble");
   string root=Path.Combine(Path.GetDirectoryName(file)!,"rumble-profile-model-"+Guid.NewGuid().ToString("N")),reports=Path.Combine(root,"Reports");Directory.CreateDirectory(reports);
   try{
    string id;await using(var profiles=new ControllerProfiles(reports,()=>"fixed",()=>"global",()=>[],capture:new Capture(),defaultVirtualAvailable:()=>false)){
     async Task Change(object request){using var doc=JsonSerializer.SerializeToDocument(request,Backend.Program.Json);await profiles.Mutate(doc.RootElement);}
     await Change(new{operation="create",name="Owned rumble profile"});id=profiles.EditingId;await Change(new{operation="edit",id,policy=new ControllerPolicy(Vibration:new(45,"Impact",true))});using var dto=JsonSerializer.SerializeToDocument(profiles.Read(),Backend.Program.Json);var vibration=dto.RootElement.GetProperty("editor").GetProperty("policy").GetProperty("vibration");Check(vibration.GetProperty("intensity").GetInt32()==45&&vibration.GetProperty("profile").GetString()=="Impact"&&vibration.GetProperty("swapMotors").GetBoolean(),"Profile did not persist all vibration fields");
     string path=Path.Combine(root,"controller-profiles.json"),before=File.ReadAllText(path);bad=false;try{await Change(new{operation="edit",id,policy=new ControllerPolicy(Vibration:new(20,"unknown"))});}catch(InvalidDataException){bad=true;}Check(bad&&File.ReadAllText(path)==before,"Invalid preset did not reject atomically");
    }
    await using(var profiles=new ControllerProfiles(reports,()=>"fixed",()=>"global",()=>[],capture:new Capture(),defaultVirtualAvailable:()=>false)){using var dto=JsonSerializer.SerializeToDocument(profiles.Read(),Backend.Program.Json);Check(profiles.EditingId==id&&dto.RootElement.GetProperty("editor").GetProperty("policy").GetProperty("vibration").GetProperty("profile").GetString()=="Impact","Reload lost preset");}
    var reset=ControllerRemappingGroups.ResetGroup(new ControllerPolicy(Tuning:new(LeftDeadzone:7),Vibration:new(45,"Sharp",true)).Validate(),"vibration");Check(reset.Vibration==null&&reset.Tuning!.LeftDeadzone==7,"Vibration reset changed other settings");
   }finally{Directory.Delete(root,true);}
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,hardwareWrites=0,physicalOwnerPerceptionTested=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message,modelOnly=true},Backend.Program.Json));return 2;}
 }
 internal static async Task<int> Native(string file)
 {
  int checks=0,forwarded=0,source=-1;ControllerVirtualPad? pad=null;var actual=new ConcurrentQueue<(byte Large,byte Small)>();void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  try{
   using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\ONEXTweaks\Overlay");source=Math.Clamp(key?.GetValue("GuideControllerIndex") as int? ??0,0,3);Check(ControllerNative.Sources().Length==1&&ControllerNative.Slots().SequenceEqual(new[]{source}),"Protect owner additional controller/current capture");
   ControllerVibration current=new(50);pad=new ControllerVirtualPad(source,false,vibration:()=>Volatile.Read(ref current),inspectForwarded:(l,s)=>{actual.Enqueue((l,s));Interlocked.Increment(ref forwarded);});pad.EnableRumbleForwarding();
   async Task Send(byte l,byte s,(byte Large,byte Small) expected){int before=Volatile.Read(ref forwarded);ControllerNative.RumbleChecked(pad.Slot,l,s);for(int n=0;n<100&&!actual.Skip(before).Contains(expected);n++)await Task.Delay(10);Check(actual.Skip(before).Contains(expected),"VIIPER profile feedback did not reach acknowledged physical API");}
   await Send(200,80,current.Scale(200,80));
   foreach(string profile in ControllerVibration.Profiles)foreach(bool swap in new[]{false,true}){
    await Send(0,0,(0,0));var next=new ControllerVibration(50,profile,swap).Validate();Volatile.Write(ref current,next);await Send(200,80,next.Scale(200,80));
    Volatile.Write(ref current,next with{Intensity=0});pad.RefreshVibration();Check(actual.Last()==(0,0),"Zero/live setting did not silence actual physical sink");
   }
   await Send(0,0,(0,0));pad.Dispose();pad=null;Check(ControllerNative.Slots().SequenceEqual(new[]{source}),"Own temporary feedback device not removed");
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,forwarded,actualViiperFeedback=true,physicalXInputApiAcknowledged=true,presets=5,swapBothWays=true,temporaryVirtualRemoved=true,productionCaptureEnabled=false,settingsWrites=0,physicalHidingChanges=0,ownerPhysicalPerceptionTested=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message},Backend.Program.Json));return 2;}finally{try{if(source>=0)ControllerNative.Rumble(source,0,0);}catch{}try{pad?.Dispose();}catch{}}
 }
}
