// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerProfileModeVerification
{
 private sealed class CaptureFake:IControllerCaptureRuntime
 {
  internal int Starts,Stops,PolicyChanges;internal bool FailStart,WaitNeutral;internal ControllerPolicy? Current;
  internal ControllerRuntimeState State=new(false,false,0,-1,null,[],0);
  public ControllerRuntimeState Read()=>State;
  public void Policy(ControllerPolicy value){PolicyChanges++;Current=value;}
  public Task Start(){Starts++;if(WaitNeutral){State=State with{Error="neutral"};throw new ControllerInputNotNeutralException("neutral");}if(FailStart){State=State with{Error="owned test failure"};throw new IOException("owned test failure");}State=new(true,false,0,1,null,[],1);return Task.CompletedTask;}
  public Task Stop(){Stops++;State=State with{Running=false,Error=null,VirtualSlot=-1};return Task.CompletedTask;}
  public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
 }
 internal static async Task<int> Model(string output)
 {
  int checks=0;void Check(bool ok,string name){checks++;if(!ok)throw new InvalidOperationException(name);}
  string folder=Path.Combine(Path.GetTempPath(),"ONEXTweaks-ProfileModeModel-"+Guid.NewGuid().ToString("N")),reports=Path.Combine(folder,"Reports"),file=Path.Combine(folder,"controller-profiles.json");Directory.CreateDirectory(reports);
  string perf="global",stamp="global:0";var fake=new CaptureFake();long clock=1000;
  ControllerProfileStore Store()=>JsonSerializer.Deserialize<ControllerProfileStore>(File.ReadAllText(file),Backend.Program.Json)!;
  try
  {
   Check(!JsonSerializer.Serialize(new ControllerPolicy().Validate(),Backend.Program.Json).Contains("useHardwareController",StringComparison.Ordinal),"Default flag must not rewrite legacy policy JSON");
   Check(!JsonSerializer.Deserialize<ControllerPolicy>("{}",Backend.Program.Json)!.Validate().UseHardwareController,"Legacy policy must inherit global mode");
   Check(JsonSerializer.Deserialize<ControllerPolicy>(JsonSerializer.Serialize(new ControllerPolicy(UseHardwareController:true).Validate(),Backend.Program.Json),Backend.Program.Json)!.UseHardwareController,"Hardware exception must roundtrip");
   await using(var profiles=new ControllerProfiles(reports,()=>stamp,()=>perf,()=>new[]{("global","Global"),("game","QA Game")},capture:fake,captureClock:()=>clock))
   {
    async Task Change(object value){using var json=JsonDocument.Parse(JsonSerializer.Serialize(value,Backend.Program.Json));await profiles.Mutate(json.RootElement);}
    await profiles.Resume();Check(fake.Starts==0&&!Store().Enabled,"Default Hardware mode must not start capture");
    await Change(new{operation="enable",enabled=true});Check(fake.Starts==1&&fake.State.Running&&Store().Enabled,"Global Virtual enable");
    await Change(new{operation="edit",id="global",policy=new ControllerPolicy(Bindings:new(){["A"]=new("gamepad","X")})});
    await Change(new{operation="create",performanceProfileId="game"});string game=profiles.EditingId;Check(profiles.AppliedId=="global","Creating inactive linked profile must not select it");
    await Change(new{operation="edit",id=game,policy=new ControllerPolicy(UseHardwareController:true)});Check(fake.Stops==0&&fake.State.Running,"Editing an inactive Hardware profile must not stop active capture");
    perf="game";stamp="game:1";await profiles.MaintainRuntime();Check(profiles.AppliedId==game&&!fake.State.Running&&fake.Stops==1,"Game activation must return physical input");Check(Store().Enabled,"Hardware pause must preserve global Virtual preference");
    await profiles.MaintainRuntime();Check(fake.Stops==1&&fake.Starts==1,"Unchanged Hardware context must be idempotent");
    perf="global";stamp="global:2";await profiles.MaintainRuntime();Check(fake.State.Running&&fake.Starts==2&&Store().Enabled,"Leaving game must resume Virtual");
    await profiles.MaintainRuntime();Check(fake.Starts==2,"Unchanged Virtual context must not create another output");
    await Change(new{operation="select",id=game});Check(!fake.State.Running&&Store().Enabled,"Manual Hardware selection must pause capture");await profiles.MaintainRuntime();Check(profiles.AppliedId==game,"Manual mode must last in current context");
    stamp="global:3";await profiles.MaintainRuntime();Check(profiles.AppliedId=="global"&&fake.State.Running,"New context must clear manual override and resume Virtual");
    await Change(new{operation="select",id=game});await Change(new{operation="enable",enabled=false});int starts=fake.Starts;perf="global";stamp="global:4";await profiles.MaintainRuntime();Check(!fake.State.Running&&fake.Starts==starts&&!Store().Enabled,"Global Hardware must override every profile");
    perf="game";stamp="game:5";await profiles.MaintainRuntime();await Change(new{operation="enable",enabled=true});Check(Store().Enabled&&!fake.State.Running&&fake.Starts==starts,"Enabling global Virtual in a Hardware profile must defer capture");
    await Change(new{operation="edit",id=game,policy=new ControllerPolicy()});Check(fake.State.Running&&fake.Starts==starts+1,"Turning profile exception off must resume capture");
    await Change(new{operation="edit",id=game,policy=new ControllerPolicy(UseHardwareController:true)});Check(!fake.State.Running&&Store().Enabled,"Turning profile exception on must stop capture");
    await Change(new{operation="reset",id=game});Check(fake.State.Running&&!Store().Profiles.Single(e=>e.Id==game).Policy.UseHardwareController,"Reset must restore inherited mode");
    await Change(new{operation="edit",id=game,policy=new ControllerPolicy(UseHardwareController:true)});await Change(new{operation="delete",id=game});Check(profiles.AppliedId=="global"&&fake.State.Running,"Deleting active Hardware profile must resume Global");
    string before=File.ReadAllText(file);bool rejected=false;try{await Change(new{operation="edit",id="global",policy=new ControllerPolicy(UseHardwareController:true)});}catch(InvalidDataException){rejected=true;}Check(rejected&&before==File.ReadAllText(file),"Global exception must reject without touching store");
    await Change(new{operation="create",performanceProfileId="game"});game=profiles.EditingId;await Change(new{operation="edit",id=game,policy=new ControllerPolicy(UseHardwareController:true)});perf="game";stamp="game:6";await profiles.MaintainRuntime();
    fake.WaitNeutral=true;perf="global";stamp="global:7";await profiles.MaintainRuntime();Check(Store().Enabled&&!fake.State.Running,"Held input must keep desired Virtual preference while physical input remains visible");
    fake.WaitNeutral=false;await profiles.MaintainRuntime();Check(fake.State.Running&&Store().Enabled,"Releasing input must retry and resume Virtual");
    fake.State=fake.State with{Running=false,Error="unexpected source loss"};int beforeRetry=fake.Starts;
    await profiles.MaintainRuntime();await profiles.MaintainRuntime();Check(Store().Enabled&&!fake.State.Running&&fake.Starts==beforeRetry,
      "Unexpected failure must retain Virtual preference and honor the retry delay");
    Check(Store().Global.Policy.For("A").Target=="X","An unexpected capture failure must preserve the saved mapping");
    clock+=1000;await profiles.MaintainRuntime();Check(fake.State.Running&&fake.Starts==beforeRetry+1,"Virtual capture must resume after the retry delay");
    fake.State=fake.State with{Running=false,Error="early worker exit"};await profiles.MaintainRuntime();clock+=1000;
    await profiles.MaintainRuntime();Check(!fake.State.Running,"A short-lived worker must not reset the retry budget");
    fake.FailStart=true;bool failed=false;try{await Change(new{operation="enable",enabled=true});}catch(IOException){failed=true;}
    Check(failed&&Store().Enabled&&!fake.State.Running,"Failed start must retain the chosen mode while leaving physical input available");
    foreach(int delay in new[]{1000,2000,5000,10000,30000}){clock+=delay;await profiles.MaintainRuntime();}
    fake.FailStart=false;int exhausted=fake.Starts;clock+=60000;await profiles.MaintainRuntime();
    Check(fake.Starts==exhausted&&!fake.State.Running&&Store().Enabled,"Exhausted retries must wait for explicit input or a fresh startup");
    await Change(new{operation="enable",enabled=false});clock+=60000;await profiles.MaintainRuntime();
    Check(fake.Starts==exhausted&&!Store().Enabled,"Explicit Hardware mode must cancel pending capture recovery");
    await Change(new{operation="select",id=game});await Change(new{operation="enable",enabled=true});Check(Store().Enabled&&!fake.State.Running,"Saved boot fixture must be Hardware profile with global Virtual On");
   }
   var reloadFake=new CaptureFake();await using(var reload=new ControllerProfiles(reports,()=>stamp,()=>perf,()=>new[]{("global","Global"),("game","QA Game")},capture:reloadFake)){await reload.Resume();Check(Store().Enabled&&reloadFake.Starts==0&&!reloadFake.State.Running,"Restart in Hardware override must not capture briefly");stamp="global:8";await reload.MaintainRuntime();Check(reloadFake.State.Running&&reloadFake.Current?.For("A").Target=="X","Restarted manager must resume the saved global Virtual mapping");}
   await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,hardwareWrites=0,physicalOwnerInputTested=false},Backend.Program.Json));return 0;
  }
  finally{Directory.Delete(folder,true);}
 }
 internal static async Task<int> Native(string output)
 {
  int checks=0;void Check(bool ok,string name){checks++;if(!ok)throw new InvalidOperationException(name);}
  string reports=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ONEXTweaks","dev","Reports"),ownerFile=Path.GetFullPath(Path.Combine(reports,"..","controller-profiles.json"));
  byte[] owner=await File.ReadAllBytesAsync(ownerFile);using(var saved=JsonDocument.Parse(owner))Check(!saved.RootElement.GetProperty("enabled").GetBoolean(),"Protect owner active virtual capture");
  Check(!File.Exists(ControllerCaptureLease.LeaseFile(reports)),"Protect an existing capture lease");Check(ControllerNative.Sources().Length==1&&ControllerNative.Slots().SequenceEqual(new[]{0})&&ControllerNative.Read(0,out var input)&&ControllerNative.Neutral(input),"Native verification requires one neutral physical source");
  ControllerHideState ReadHide(){using var hide=new ControllerHidHide();return hide.Read();}
  var initial=ReadHide();string folder=Path.Combine(Path.GetTempPath(),"ONEXTweaks-ProfileModeNative-"+Guid.NewGuid().ToString("N")),ownReports=Path.Combine(folder,"Reports"),perf="game",stamp="game:1";Directory.CreateDirectory(ownReports);string file=Path.Combine(folder,"controller-profiles.json");
  var store=new ControllerProfileStore(1,true,"game",null,null,new("global","Global",null,new ControllerPolicy().Validate()),[new("game","QA Hardware","game",new ControllerPolicy(UseHardwareController:true).Validate())]);await File.WriteAllTextAsync(file,JsonSerializer.Serialize(store,Backend.Program.Json));
  try
  {
   await using(var profiles=new ControllerProfiles(ownReports,()=>stamp,()=>perf,()=>new[]{("global","Global"),("game","QA Hardware")},capture:new ControllerRemapper(reports)))
   {
    async Task AssertMode(bool virtualMode,string label){using var data=JsonDocument.Parse(JsonSerializer.Serialize(profiles.Read(),Backend.Program.Json));Check(data.RootElement.GetProperty("enabled").GetBoolean(),label+": global preference lost");Check(data.RootElement.GetProperty("runtime").GetProperty("running").GetBoolean()==virtualMode,label+": actual worker mode differs");Check((await ControllerRemapper.Observe(reports)).Length==1,label+": independent observer must see one controller");Check(File.Exists(ControllerCaptureLease.LeaseFile(reports))==virtualMode,label+": actual capture lease differs");}
    await profiles.Resume();await AssertMode(false,"boot in Hardware profile");
    perf="global";stamp="global:2";await profiles.MaintainRuntime(true);await AssertMode(true,"leave Hardware profile");
    perf="game";stamp="game:3";await profiles.MaintainRuntime(true);await AssertMode(false,"enter Hardware profile");
    perf="global";stamp="global:4";await profiles.MaintainRuntime(true);await AssertMode(true,"return to Global");
    using var off=JsonDocument.Parse("{\"operation\":\"enable\",\"enabled\":false}");await profiles.Mutate(off.RootElement);Check(!File.Exists(ControllerCaptureLease.LeaseFile(reports))&&ControllerRemapper.OutputSlot==-1,"Global Hardware must release capture");
   }
   var after=ReadHide();Check(initial.Active==after.Active&&initial.Inverse==after.Inverse&&initial.Devices.Order().SequenceEqual(after.Devices.Order())&&initial.Applications.Order().SequenceEqual(after.Applications.Order()),"Fresh HidHide configuration must be restored");byte[] ownerAfter=await File.ReadAllBytesAsync(ownerFile);Check(owner.SequenceEqual(ownerAfter),"Native test must not modify owner controller preferences");
   await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=true,checks,actualVirtualStarts=2,actualStops=2,controlledContext=true,actualGameDetectionTested=false,actualViiperAndHidHide=true,ownerPreferencesChanged=false,physicalOwnerInputTested=false},Backend.Program.Json));return 0;
  }
  finally{Directory.Delete(folder,true);}
 }
}
