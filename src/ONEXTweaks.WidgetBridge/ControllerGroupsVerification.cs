// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerGroupsVerification
{
 private sealed class CaptureFake:IControllerCaptureRuntime
 {
  internal ControllerPolicy? Current;
  public ControllerRuntimeState Read()=>new(false,false,0,-1,null,[],0);
  public void Policy(ControllerPolicy value)=>Current=value;
  public Task Start()=>Task.CompletedTask;public Task Stop()=>Task.CompletedTask;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
 }
 private sealed class OemFake:IControllerOemRuntime
 {
  public bool Prepared{get;private set;}internal int Prepares,Releases;
  public object Read()=>new{prepared=Prepared};public Task Prepare(){Prepares++;Prepared=true;return Task.CompletedTask;}public Task Release(){Releases++;Prepared=false;return Task.CompletedTask;}
 }
 internal static async Task<int> Verify(string output)
 {
  int checks=0;void Check(bool ok,string name){checks++;if(!ok)throw new InvalidOperationException(name);}
  string folder=Path.Combine(Path.GetTempPath(),"ONEXTweaks-Groups288-"+Guid.NewGuid().ToString("N")),reports=Path.Combine(folder,"Reports"),file=Path.Combine(folder,"controller-profiles.json");Directory.CreateDirectory(reports);
  try
  {
   string Json<T>(T value)=>JsonSerializer.Serialize(value,Backend.Program.Json);
   var original=new ControllerPolicy().Validate();Check(!Json(original).Contains("gamepadRemappingDisabled",StringComparison.Ordinal)&&!Json(original).Contains("vibration",StringComparison.Ordinal),"Default schema must not rewrite old files");Check(!JsonSerializer.Deserialize<ControllerPolicy>("{}",Backend.Program.Json)!.Validate().GamepadRemappingDisabled,"Legacy remapping defaults On");
   var policy=new ControllerPolicy(new(){{"A",new("gamepad","B")},{"X",new("keyboard",Keys:[17,75])},{"LT",new("mouse","right")},{"F13",new("keyboard",Keys:[112])}},new(5,7,true,false,false,true,true,40),Oem:new(new(){{"M1",new("gamepad","X")}}),UseHardwareController:true,Vibration:new(65)).Validate();
   var standardOff=policy with{GamepadRemappingDisabled=true};Check(Json(standardOff.Bindings)==Json(policy.Bindings),"Disabling must retain assignments");Check(standardOff.For("A").Kind=="default"&&standardOff.For("X").Kind=="default"&&standardOff.For("LT").Kind=="default","Disabled standard bindings must bypass");Check(standardOff.For("M1").Target=="X"&&standardOff.For("F13").Kind=="keyboard","Other groups/legacy aliases must stay independent");
   foreach(string source in ControllerPolicy.Buttons.Keys.Concat(ControllerPolicy.Analog))foreach(string target in ControllerPolicy.PadTargets)
   {
    var p=new ControllerPolicy(new(){{source,new("gamepad",target)}},GamepadRemappingDisabled:true).Validate();var frame=new ControllerFrame(4097,123,210,25000,-29000,-23000,27000);var expected=new ControllerTranslator().Translate(frame,original);var actual=new ControllerTranslator().Translate(frame,p);Check(actual.Pad==expected.Pad&&actual.Keys.Count==0&&actual.Mouse.Count==0&&actual.Wheel==0,"Off must preserve original output for every source/target pair");
   }
   var translated=new ControllerTranslator().Translate(new(4096),new ControllerPolicy(new(){{"A",new("keyboard",Keys:[17])}}).Validate());Check(translated.Keys.Contains(17)&&translated.Pad.Buttons==0,"Enabled mapped key output");var stopped=new ControllerTranslator().Translate(new(4096),new ControllerPolicy(new(){{"A",new("keyboard",Keys:[17])}},GamepadRemappingDisabled:true).Validate());Check(stopped.Keys.Count==0&&stopped.Pad.Buttons==4096,"Off output releases mapped key and restores original held button");
   var reset=ControllerRemappingGroups.ResetGroup(standardOff,"standard");Check(reset.Bindings!.Count==1&&reset.Bindings.ContainsKey("F13"),"Standard reset must retain legacy aliases");Check(Json(reset.Oem)==Json(policy.Oem)&&reset.Tuning==policy.Tuning&&reset.Vibration==policy.Vibration&&reset.GamepadRemappingDisabled&&reset.UseHardwareController,"Standard reset touched another scope");
   var single=ControllerRemappingGroups.ResetBinding(policy,"A");Check(!single.Bindings!.ContainsKey("A")&&single.Bindings.Count==policy.Bindings!.Count-1&&Json(single.Oem)==Json(policy.Oem)&&single.Tuning==policy.Tuning,"Single-source reset scope");var oemSingle=ControllerRemappingGroups.ResetBinding(policy,"OEM:M1");Check(oemSingle.For("M1").Kind=="disabled"&&Json(oemSingle.Bindings)==Json(policy.Bindings),"Single OEM reset scope");
   var oemReset=ControllerRemappingGroups.ResetGroup(policy,"oem");Check(oemReset.For("Home").Kind=="widget"&&oemReset.For("GKey").Kind=="widget"&&oemReset.For("Onex").Kind=="widget"&&oemReset.For("KeyboardMouse").Kind=="keyboardPopup"&&oemReset.For("M1").Kind=="disabled"&&Json(oemReset.Bindings)==Json(policy.Bindings)&&oemReset.Tuning==policy.Tuning,"OEM group factory defaults and scope");
   var tuneReset=ControllerRemappingGroups.ResetGroup(policy,"sticks");Check(tuneReset.Tuning==new ControllerTuning()&&Json(tuneReset.Bindings)==Json(policy.Bindings)&&Json(tuneReset.Oem)==Json(policy.Oem)&&tuneReset.Vibration==policy.Vibration,"Sticks/trigger reset must not touch mappings or vibration");var vibrationReset=ControllerRemappingGroups.ResetGroup(policy,"vibration");Check(vibrationReset.Vibration==null&&vibrationReset.Tuning==policy.Tuning&&Json(vibrationReset.Bindings)==Json(policy.Bindings),"Vibration reset scope");
   var onlyM1=new ControllerPolicy(Oem:new(ControllerOemPolicy.Inputs.ToDictionary(id=>id,id=>new ControllerBinding(id=="M1"?"widget":"disabled")))).Validate();bool rejected=false;try{ControllerRemappingGroups.ResetBinding(onlyM1,"OEM:M1");}catch(InvalidDataException){rejected=true;}Check(rejected&&onlyM1.For("M1").Kind=="widget","Cannot reset the last widget source");Check(ControllerRemappingGroups.ResetGroup(onlyM1,"oem").For("Home").Kind=="widget","Full OEM reset must restore access");
   foreach(int intensity in new[]{0,1,25,50,65,99,100}){var v=new ControllerVibration(intensity).Validate();foreach(byte value in new byte[]{0,1,64,127,128,254,255}){var scaled=v.Scale(value,value);Check(scaled.Large<=value&&scaled.Large==scaled.Small&&(intensity!=0||scaled.Large==0)&&(intensity!=100||scaled.Large==value),"Real feedback scaling range/default");}}
   foreach(int bad in new[]{-1,101}){rejected=false;try{new ControllerPolicy(Vibration:new(bad)).Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"Reject invalid vibration range");}
   var fake=new CaptureFake();var oem=new OemFake();await using(var profiles=new ControllerProfiles(reports,()=>"global:0",()=>"global",()=>new[]{("global","Global")},capture:fake))
   {
    profiles.BindOemManager(oem);async Task Change(object value){using var doc=JsonDocument.Parse(Json(value));await profiles.Mutate(doc.RootElement);}ControllerProfileStore Store()=>JsonSerializer.Deserialize<ControllerProfileStore>(File.ReadAllText(file),Backend.Program.Json)!;
    await Change(new{operation="create",name="Owned Groups Model"});string id=profiles.EditingId;await Change(new{operation="edit",id,policy=standardOff});Check(Store().Profiles.Single().Policy.GamepadRemappingDisabled&&!Store().Enabled,"Profile off flag persisted without changing capture mode");
    await Change(new{operation="oemEnable",enabled=true});await Change(new{operation="oemEnable",enabled=false});Check(oem.Prepares==1&&oem.Releases==1&&!Store().OemEnabled&&Store().Profiles.Single().Policy.GamepadRemappingDisabled,"OEM global switch is independent from standard profile flag");
    await Change(new{operation="resetGroup",id,group="standard"});Check(Store().Profiles.Single().Policy.GamepadRemappingDisabled&&Store().Profiles.Single().Policy.Bindings!.Count==1,"API scoped reset preserves disabled state");await Change(new{operation="edit",id,policy=onlyM1});string before=File.ReadAllText(file);rejected=false;try{await Change(new{operation="resetBinding",id,source="OEM:M1"});}catch(InvalidDataException){rejected=true;}Check(rejected&&before==File.ReadAllText(file),"Rejected reset must not touch stored data");rejected=false;try{await Change(new{operation="resetGroup",id="global",group="standard"});}catch(InvalidOperationException){rejected=true;}Check(rejected&&before==File.ReadAllText(file),"Stale editor reset must be rejected atomically");await Change(new{operation="resetGroup",id,group="oem"});Check(Store().Profiles.Single().Policy.For("Home").Kind=="widget","API OEM defaults");
   }
   await File.WriteAllTextAsync(output,Json(new{passed=true,checks,kind="pure translation + owned temporary profile storage/fake runtimes; no hardware or driver operations",actualOwnerHardwareTested=false}));return 0;
  }
  catch(Exception error){await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=false,checks,error=error.Message},Backend.Program.Json));return 2;}
  finally{Directory.Delete(folder,true);}
 }
}
