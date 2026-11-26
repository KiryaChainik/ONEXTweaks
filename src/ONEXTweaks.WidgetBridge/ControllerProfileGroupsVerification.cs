// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerProfileGroupsVerification
{
 private sealed class Fake:IControllerCaptureRuntime{
  internal ControllerPolicy? Current;internal int Changes;public ControllerRuntimeState Read()=>new(false,false,0,-1,null,[],0);
  public void Policy(ControllerPolicy p){Current=p;Changes++;}public Task Start()=>throw new InvalidOperationException("Model must not capture hardware");public Task Stop()=>Task.CompletedTask;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
 }
 internal static async Task<int> Run(string output){
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
  var global=new ControllerPolicy(Bindings:new(){{"A",new("gamepad","B")}},Tuning:new(NintendoLayout:true),Vibration:new(Intensity:31),ForwardGameLighting:true).Validate();
  var local=new ControllerPolicy(Bindings:new(){{"A",new("gamepad","Y")}},Tuning:new(LeftDeadzone:7),Vibration:new(Intensity:64),VirtualTarget:"DualShock4",ForwardMotion:true).Validate();
  Check(ReferenceEquals(ControllerProfileGroups.Compose(local,global,null),local),"Legacy profiles must keep every own setting");
  Check(!JsonSerializer.Serialize(new ControllerProfileEntry("x","QA",null,local),Backend.Program.Json).Contains("captureGroups"),"Default must not rewrite legacy documents");
  for(int bits=0;bits<16;bits++){
   var g=new ControllerProfileGroups((bits&1)!=0,(bits&2)!=0,(bits&4)!=0,(bits&8)!=0);var value=ControllerProfileGroups.Compose(local,global,g);
   Check(value.Bindings!["A"].Target==(g.Mappings?"Y":"B")&&value.Tuning!.NintendoLayout==!g.NintendoLayout&&value.Vibration!.Intensity==(g.Vibration?64:31)&&value.ForwardGameLighting==!g.GameLighting,"Mixed capture groups");
   Check(value.Tuning!.LeftDeadzone==7&&value.VirtualTarget=="DualShock4"&&value.ForwardMotion,"Inheritance changed a group outside its scope");
  }
  var inherited=new ControllerProfileGroups(false,false,false,false);var applied=ControllerProfileGroups.Compose(local,global,inherited);
  var changed=ControllerProfileGroups.Edit(local,global,inherited,applied with{Bindings=new(){{"A",new("gamepad","X")}},Tuning=applied.Tuning! with{NintendoLayout=false,RightDeadzone=9},Vibration=new(Intensity:22),ForwardGameLighting=false});
  Check(changed.Local.Bindings!["A"].Target=="Y"&&!changed.Local.Tuning!.NintendoLayout&&changed.Local.Vibration!.Intensity==64&&!changed.Local.ForwardGameLighting,"Inherited edits overwrote dormant local values");
  Check(changed.Global.Bindings!["A"].Target=="X"&&!changed.Global.Tuning!.NintendoLayout&&changed.Global.Vibration!.Intensity==22&&!changed.Global.ForwardGameLighting,"Inherited edits did not update Global");
  Check(changed.Local.Tuning!.RightDeadzone==9&&changed.Global.Tuning!.RightDeadzone==0,"Local stick tuning was routed to Global");
  var unchanged=ControllerProfileGroups.Edit(local,global,inherited,applied with{Tuning=applied.Tuning! with{RightDeadzone=9}});
  Check(ReferenceEquals(unchanged.Global,global),"Unrelated local edit rewrote Global");
  string folder=Path.Combine(Path.GetTempPath(),"ONEXTweaks-CaptureGroups-"+Guid.NewGuid().ToString("N")),reports=Path.Combine(folder,"Reports"),file=Path.Combine(folder,"controller-profiles.json");Directory.CreateDirectory(reports);
  string stamp="global:0",perf="global",id="";var fake=new Fake();
  ControllerProfileStore Saved()=>JsonSerializer.Deserialize<ControllerProfileStore>(File.ReadAllText(file),Backend.Program.Json)!;
  try{
   await using(var profiles=new ControllerProfiles(reports,()=>stamp,()=>perf,()=>new[]{("global","Global"),("game","QA Game")},capture:fake,defaultVirtualAvailable:()=>false)){
    async Task Change(object request){using var json=JsonDocument.Parse(JsonSerializer.Serialize(request,Backend.Program.Json));await profiles.Mutate(json.RootElement);}
    ControllerPolicy Editor(){using var json=JsonDocument.Parse(JsonSerializer.Serialize(profiles.Read(),Backend.Program.Json));return json.RootElement.GetProperty("editor").GetProperty("policy").Deserialize<ControllerPolicy>(Backend.Program.Json)!;}
    await Change(new{operation="edit",id="global",policy=global});await Change(new{operation="create",name="QA Scope"});id=profiles.EditingId;await Change(new{operation="edit",id,policy=local});
    await Change(new{operation="captureGroups",id,groups=inherited});Check(Editor().Bindings!["A"].Target=="B"&&fake.Current!.Bindings!["A"].Target=="B","Editor/runtime did not use effective Global mappings");
    Check(Saved().Profiles.Single().Policy.Bindings!["A"].Target=="Y","Scope change lost dormant local mapping");int count=fake.Changes;await profiles.MaintainRuntime();await profiles.MaintainRuntime();Check(fake.Changes==count,"Unchanged inheritance repeatedly reset runtime policy");
    await Change(new{operation="edit",id,policy=Editor() with{Bindings=new(){{"A",new("gamepad","X")}}}});Check(Saved().Global.Policy.Bindings!["A"].Target=="X"&&Saved().Profiles.Single().Policy.Bindings!["A"].Target=="Y","Real store routed inherited mapping incorrectly");
    await Change(new{operation="captureGroups",id,groups=new ControllerProfileGroups()});Check(Editor().Bindings!["A"].Target=="Y"&&Editor().Vibration!.Intensity==64,"Re-enabling capture did not restore dormant values");
    await Change(new{operation="captureGroups",id,groups=new ControllerProfileGroups(false,true,true,false)});
    await Change(new{operation="edit",id,policy=Editor() with{Vibration=new(Intensity:42)}});Check(Saved().Global.Policy.Vibration!.Intensity==31&&Saved().Profiles.Single().Policy.Vibration!.Intensity==42,"Independent vibration edit changed Global");
    await Change(new{operation="resetBinding",id,source="A"});Check(Saved().Global.Policy.For("A").Kind=="default"&&Saved().Profiles.Single().Policy.Bindings!["A"].Target=="Y","Per-button reset ignored inherited scope");
    await Change(new{operation="edit",id,policy=Editor() with{Bindings=new(){{"A",new("gamepad","X")}}}});
    byte[] before=File.ReadAllBytes(file);bool rejected=false;try{await Change(new{operation="captureGroups",id="global",groups=inherited});}catch(InvalidDataException){rejected=true;}Check(rejected&&before.SequenceEqual(File.ReadAllBytes(file)),"Global inherited itself or invalid mutation wrote settings");
   }
   var restarted=new Fake();await using(var profiles=new ControllerProfiles(reports,()=>stamp,()=>perf,()=>new[]{("global","Global")},capture:restarted,defaultVirtualAvailable:()=>false)){await profiles.Resume();Check(profiles.AppliedId==id&&restarted.Current!.Bindings!["A"].Target=="X"&&restarted.Current.Vibration!.Intensity==42,"Saved scope did not survive manager restart");}
   Check(Saved().Profiles.Single().Policy.Bindings!["A"].Target=="Y","Restart destroyed dormant values");
   await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=true,checks,temporaryStore=true,syntheticCaptureRuntime=true,hardwareCapture=false,ownerSettingsChanged=false},Backend.Program.Json));return 0;
  }finally{Directory.Delete(folder,true);}
 }
 internal static async Task<int> Native(string output){
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
  string reports=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ONEXTweaks","dev","Reports"),ownerFile=Path.GetFullPath(Path.Combine(reports,"..","controller-profiles.json"));byte[] owner=await File.ReadAllBytesAsync(ownerFile);
  using(var json=JsonDocument.Parse(owner))Check(!json.RootElement.GetProperty("enabled").GetBoolean(),"Protect owner active capture");
  Check(!File.Exists(ControllerCaptureLease.LeaseFile(reports))&&ControllerNative.Slots().SequenceEqual(new[]{0})&&ControllerNative.Read(0,out var source)&&ControllerNative.Neutral(source),"Requires one neutral physical source and no existing capture");
  ControllerHideState Hide(){using var h=new ControllerHidHide();return h.Read();}var before=Hide();
  string folder=Path.Combine(Path.GetTempPath(),"ONEXTweaks-CaptureGroupsNative-"+Guid.NewGuid().ToString("N")),ownReports=Path.Combine(folder,"Reports"),file=Path.Combine(folder,"controller-profiles.json");Directory.CreateDirectory(ownReports);
  var global=new ControllerPolicy(Bindings:new(){{"A",new("gamepad","B")}}).Validate();var local=new ControllerPolicy(Bindings:new(){{"A",new("gamepad","Y")}}).Validate();var own=new ControllerProfileStore(1,true,"qa","qa","qa:1",new("global","Global",null,global),[new("qa","QA capture",null,local,new(false))]);await File.WriteAllTextAsync(file,JsonSerializer.Serialize(own,Backend.Program.Json));
  ControllerProfileStore Saved()=>JsonSerializer.Deserialize<ControllerProfileStore>(File.ReadAllText(file),Backend.Program.Json)!;
  try{
   for(int run=0;run<2;run++){
    var capture=new ControllerRemapper(reports);await using(var profiles=new ControllerProfiles(ownReports,()=>"qa:1",()=>"global",()=>new[]{("global","Global")},capture:capture)){
     await profiles.Resume();await Task.Delay(180);var state=capture.Read();Check(state.Running&&state.OutputTarget=="Xbox360"&&state.Frames>0,"Actual capture did not receive physical neutral frames");Check((await ControllerRemapper.Observe(reports)).SequenceEqual(new[]{state.ObserverVirtualSlot}),"Independent view differs from the challenged owned output");
     ControllerPolicy Editor(){using var json=JsonDocument.Parse(JsonSerializer.Serialize(profiles.Read(),Backend.Program.Json));return json.RootElement.GetProperty("editor").GetProperty("policy").Deserialize<ControllerPolicy>(Backend.Program.Json)!;}
     Check(Editor().For("A").Target==(run==0?"B":"X"),"Effective scope did not reach actual runtime manager/restart");
     if(run==0){
      using var edit=JsonDocument.Parse(JsonSerializer.Serialize(new{operation="edit",id="qa",policy=Editor() with{Bindings=new(){{"A",new("gamepad","X")}}}},Backend.Program.Json));await profiles.Mutate(edit.RootElement);Check(Saved().Global.Policy.For("A").Target=="X"&&Saved().Profiles.Single().Policy.For("A").Target=="Y","Actual manager lost dormant values or failed global writeback");
      using var ownScope=JsonDocument.Parse("{\"operation\":\"captureGroups\",\"id\":\"qa\",\"groups\":{}}");await profiles.Mutate(ownScope.RootElement);Check(Editor().For("A").Target=="Y"&&capture.Read().VirtualSlot==state.VirtualSlot,"Scope toggle reconnected the virtual pad or lost local mapping");
      using var inheritedScope=JsonDocument.Parse("{\"operation\":\"captureGroups\",\"id\":\"qa\",\"groups\":{\"mappings\":false}}");await profiles.Mutate(inheritedScope.RootElement);Check(Editor().For("A").Target=="X","Scope did not resume inheritance");
     }
    }
    Check((await ControllerRemapper.Observe(reports)).SequenceEqual(new[]{0})&&!File.Exists(ControllerCaptureLease.LeaseFile(reports)),"Actual physical input was not restored");
   }
   var after=Hide();Check(before.Active==after.Active&&before.Inverse==after.Inverse&&before.Devices.Order().SequenceEqual(after.Devices.Order())&&before.Applications.Order().SequenceEqual(after.Applications.Order()),"Original HidHide configuration was not preserved");
   byte[] now=await File.ReadAllBytesAsync(ownerFile);Check(owner.SequenceEqual(now),"Native scope test changed owner profiles");
   await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=true,checks,actualCaptureStarts=2,actualNeutralPhysicalFrames=true,actualViiperAndHidHide=true,temporaryStore=true,ownerProfileChanges=0,physicalOwnerButtonsTested=false,nonNeutralGameMappingOutputTested=false},Backend.Program.Json));return 0;
  }finally{Directory.Delete(folder,true);}
 }

}
