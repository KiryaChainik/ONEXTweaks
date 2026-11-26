// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerVerification
{
 internal static async Task<int> Policy(string output)
 {
  int checks=0;void Check(bool ok,string name){checks++;if(!ok)throw new InvalidOperationException(name);}
  var random=new Random(229);var translator=new ControllerTranslator();var identity=new ControllerPolicy().Validate();
  for(int n=0;n<1200;n++){var p=new ControllerFrame((ushort)random.Next(65536),(byte)random.Next(256),(byte)random.Next(256),(short)random.Next(-32768,32768),(short)random.Next(-32768,32768),(short)random.Next(-32768,32768),(short)random.Next(-32768,32768));Check(translator.Translate(p,identity).Pad==p with{Buttons=(ushort)(p.Buttons&0xF3FF)},"Default forwarding changed input");}
  foreach(var input in ControllerPolicy.Buttons)
  {var p=new ControllerPolicy(new(){{input.Key,new("disabled")}}).Validate();Check((translator.Translate(new(input.Value),p).Pad.Buttons&input.Value)==0,"Disabled source leaked");foreach(var target in ControllerPolicy.Buttons){p=new(new(){{input.Key,new("gamepad",target.Key)}});Check(translator.Translate(new(input.Value),p).Pad.Buttons==target.Value,"Button remapping failed");}}
  var swap=new ControllerPolicy(new(){{"A",new("gamepad","B")},{"B",new("gamepad","A")}}).Validate();Check(translator.Translate(new(4096),swap).Pad.Buttons==8192,"Swap A lost B");Check(translator.Translate(new(8192),swap).Pad.Buttons==4096,"Swap B lost A");Check(translator.Translate(new(12288),swap).Pad.Buttons==12288,"Simultaneous swap lost input");
  var opposites=new ControllerPolicy(new(){{"A",new("gamepad","LSUp")},{"B",new("gamepad","LSDown")}}).Validate();Check(translator.Translate(new(12288),opposites).Pad.Ly==0,"Opposing mapped axes must neutralize");
  var chord=new ControllerPolicy(new(){{"A",new("keyboard",Keys:[17,32])},{"B",new("keyboard",Keys:[32])}}).Validate();Check(translator.Translate(new(12288),chord).Keys.SetEquals([17,32]),"Keyboard chord union failed");Check(translator.Translate(new(8192),chord).Keys.SetEquals([32]),"One source released another held source");Check(translator.Translate(new(),chord).Keys.Count==0,"Released keys stuck");
  var wheel=new ControllerPolicy(new(){{"A",new("mouse","wheelUp")}}).Validate();translator.Reset();Check(translator.Translate(new(4096),wheel).Wheel==1&&translator.Translate(new(4096),wheel).Wheel==0,"Wheel repeats while held");translator.Translate(new(),wheel);Check(translator.Translate(new(4096),wheel).Wheel==1,"Wheel did not rearm");
  var trigger=new ControllerPolicy(new(){{"LT",new("keyboard",Keys:[32])}}).Validate();Check(translator.Translate(new(Lt:200),trigger).Pad.Lt==0&&translator.Translate(new(Lt:200),trigger).Keys.Contains(32),"Trigger keyboard mapping failed");Check(translator.Translate(new(Lt:0),trigger).Keys.Count==0,"Trigger release stuck");
  var stick=new ControllerPolicy(new(){{"LSUp",new("keyboard",Keys:[32])}}).Validate();Check(translator.Translate(new(Ly:30000),stick).Pad.Ly==0&&translator.Translate(new(Ly:30000),stick).Keys.Contains(32),"Stick direction not consumed");Check(translator.Translate(new(Ly:-30000),stick).Pad.Ly==-30000,"Opposite stick direction incorrectly consumed");
  foreach(int deadzone in new[]{0,5,20,40})foreach(short value in new short[]{short.MinValue,-20000,-100,0,100,20000,short.MaxValue})foreach(bool invert in new[]{false,true}){short actual=ControllerTranslator.Axis(value,deadzone,invert);Check(actual>=short.MinValue&&actual<=short.MaxValue,"Axis overflow");if(Math.Abs((int)value)<deadzone*32768/100)Check(actual==0,"Deadzone did not neutralize");}
  var swappedPhysical=new ControllerPolicy(new(){{"LSUp",new("keyboard",Keys:[90])}},new(SwapSticks:true)).Validate();var consumed=translator.Translate(new(Ly:30000),swappedPhysical);Check(consumed.Keys.Contains(90)&&consumed.Pad.Ry==0,"Swap changed the physical direction binding identity");
  var explicitAxis=new ControllerPolicy(new(){{"A",new("gamepad","LSRight")}},new(SwapSticks:true)).Validate();Check(translator.Translate(new(4096),explicitAxis).Pad.Lx==32767,"Swap changed an explicitly selected virtual target");
  translator.Reset();var disabledMouseScroll=new ControllerPolicy(new(){{"LSUp",new("disabled")}},MouseMode:true).Validate();Check(translator.Translate(new(Ly:30000),disabledMouseScroll).Wheel==0,"Disabled direction still scrolls in mouse mode");
  translator.Reset();var swappedMouse=new ControllerPolicy(Tuning:new(SwapSticks:true),MouseMode:true).Validate();var cursor=translator.Translate(new(Ly:30000),swappedMouse);Check(cursor.CursorY==30000&&cursor.Wheel==0,"Swapped mouse cursor also scrolls");translator.Reset();var scroll=translator.Translate(new(Ry:30000),swappedMouse);Check(scroll.Wheel==1&&scroll.CursorY==0,"Swapped mouse scroll uses wrong physical stick");
  var overlay=new ControllerPolicy(new(){{"A",new("keyboard",Keys:[32])}},MouseMode:true).Validate();var frame=new ControllerFrame(4096,255,255,1200,3000,27000,-29000);var bypass=translator.Translate(frame,overlay,true);Check(bypass.Pad==frame&&bypass.Keys.Count==0&&bypass.Mouse.Count==0&&bypass.CursorX==0,"Overlay controls are remapped");Check(translator.Translate(new(1024),identity).Pad.Buttons==0,"Reserved Guide leaked");
  foreach(var invalid in new[]{new ControllerPolicy(new(){{"Guide",new("gamepad","A")}}),new ControllerPolicy(new(){{"A",new("gamepad","not-a-button")}}),new ControllerPolicy(new(){{"A",new("keyboard",Keys:[0])}}),new ControllerPolicy(Tuning:new(LeftDeadzone:99))}){bool rejected=false;try{invalid.Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"Unsafe mapping accepted");}
  await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,hardwareWrites=0,physicalOwnerInputTested=false},Backend.Program.Json));return 0;
 }

 internal static async Task<int> Profiles(string output)
 {
  int checks=0;void Check(bool ok,string name){checks++;if(!ok)throw new InvalidOperationException(name);}
  string folder=Path.Combine(Path.GetTempPath(),"ONEXTweaks-ControllerModel-"+Guid.NewGuid().ToString("N"));string reports=Path.Combine(folder,"Reports");Directory.CreateDirectory(reports);string perf="global",stamp="global:0";string file=Path.Combine(folder,"controller-profiles.json");
  try
  {
   await using(var profiles=new ControllerProfiles(reports,()=>stamp,()=>perf,()=>new[]{("global","Global Profile"),("game","QA Game"),("manual-performance","QA Manual Performance")},defaultVirtualAvailable:()=>false))
   {
    async Task Change(object value){using var json=JsonDocument.Parse(JsonSerializer.Serialize(value,Backend.Program.Json));await profiles.Mutate(json.RootElement);}
    Check(profiles.AppliedId=="global"&&profiles.EditingId=="global","Global default");await Change(new{operation="create"});string manual=profiles.EditingId;Check(profiles.AppliedId==manual&&manual!="global","Manual profile selected");
    await Change(new{operation="create",performanceProfileId="game"});string game=profiles.EditingId;Check(profiles.AppliedId==manual&&game!=manual,"Inactive-parent creation does not change applied mapping");
    await Change(new{operation="create",performanceProfileId="game"});Check(profiles.EditingId==game,"Duplicate parent reuses existing profile");
    perf="game";stamp="game:100";profiles.Refresh();Check(profiles.AppliedId==game&&profiles.EditingId==game,"Game context automatically selects linked controller profile");await Change(new{operation="select",id="global"});Check(profiles.AppliedId=="global","Manual override");profiles.Refresh();Check(profiles.AppliedId=="global","Override lasts in same session");stamp="game:101";profiles.Refresh();Check(profiles.AppliedId==game,"Next process session restores game profile");
    await Change(new{operation="edit",id=game,policy=new ControllerPolicy(new(){{"A",new("gamepad","B")}},new(LeftDeadzone:10))});using(var doc=JsonDocument.Parse(await File.ReadAllTextAsync(file))){var row=doc.RootElement.GetProperty("profiles").EnumerateArray().First(e=>e.GetProperty("id").GetString()==game);Check(row.GetProperty("policy").GetProperty("tuning").GetProperty("leftDeadzone").GetInt32()==10,"Edits persist in selected profile");}
    string before=await File.ReadAllTextAsync(file);bool rejected=false;try{await Change(new{operation="edit",id=manual,policy=new ControllerPolicy()});}catch(InvalidOperationException){rejected=true;}Check(rejected&&before==await File.ReadAllTextAsync(file),"Stale editor rejected without writing");
    await Change(new{operation="rename",id=game,name="QA Renamed"});Check(profiles.AppliedId==game,"Rename preserves identity and application");await Change(new{operation="keep",id=game});Check(profiles.AppliedId=="global"&&profiles.EditingId=="global","Keep It returns Global without deleting");
    await Change(new{operation="select",id=game});await Change(new{operation="delete",id=game});Check(profiles.AppliedId=="global"&&profiles.EditingId=="global","Deleting selected profile returns safely to Global");
    before=await File.ReadAllTextAsync(file);rejected=false;try{await Change(new{operation="delete",id="global"});}catch(InvalidDataException){rejected=true;}Check(rejected&&before==await File.ReadAllTextAsync(file),"Global protected");
    await Change(new{operation="select",id=manual});await Change(new{operation="reset",id=manual});using var final=JsonDocument.Parse(await File.ReadAllTextAsync(file));Check(!final.RootElement.GetProperty("enabled").GetBoolean(),"Metadata tests never enable capture");
   }
   await using(var reload=new ControllerProfiles(reports,()=>stamp,()=>perf,()=>new[]{("global","Global Profile"),("game","QA Game")},defaultVirtualAvailable:()=>false)){Check(reload.AppliedId!="global","Saved manual choice reloads without enabling capture");}
   var retryStore=JsonSerializer.Deserialize<ControllerProfileStore>(await File.ReadAllTextAsync(file),Backend.Program.Json)! with{OemEnabled=true,Enabled=false};await File.WriteAllTextAsync(file,JsonSerializer.Serialize(retryStore,Backend.Program.Json));
   await using(var retry=new ControllerProfiles(reports,()=>stamp,()=>perf,()=>new[]{("global","Global Profile"),("game","QA Game")},defaultVirtualAvailable:()=>false))
   {
    var fake=new OemStartupFake();retry.BindOemManager(fake);string baseline=await File.ReadAllTextAsync(file);long now=Environment.TickCount64;
    await retry.Resume();Check(fake.Attempts==1&&!fake.Prepared&&await File.ReadAllTextAsync(file)==baseline,"Transient MCU failure must preserve desired OEM state and all profile settings");
    await retry.MaintainOem(now);Check(fake.Attempts==1,"OEM startup retry must respect backoff");
    await retry.MaintainOem(now+5000);Check(fake.Attempts==2&&!fake.Prepared&&await File.ReadAllTextAsync(file)==baseline,"Repeated MCU failure must not rewrite user preferences");
    await retry.MaintainOem(now+5001);Check(fake.Attempts==2,"Repeated failure must remain throttled");
    await retry.MaintainOem(now+10000);Check(fake.Prepared&&fake.Attempts==3&&await File.ReadAllTextAsync(file)==baseline,"Available MCU must resume desired OEM state without changing preferences");
    fake.CurrentSession=0;Check(!fake.Prepared,"A disconnected PowerAgent session cannot own the OEM mapping");
    fake.CurrentSession=2;Check(!fake.Prepared,"A new pipe session cannot reuse the old OEM preparation");
    await retry.MaintainOem(now+11000);Check(fake.Prepared&&fake.Attempts==4&&await File.ReadAllTextAsync(file)==baseline,"Reconnect must restore mappings without changing preferences");
    await retry.MaintainOem(now+12000);Check(fake.Attempts==4,"A healthy pipe session must not reprogram the buttons");
    using var disable=JsonDocument.Parse("{\"operation\":\"oemEnable\",\"enabled\":false}");await retry.Mutate(disable.RootElement);await retry.MaintainOem(now+100000);
    Check(!fake.Prepared&&fake.Attempts==4,"Explicit Off must stop automatic OEM retries");
   }
   await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,oemStartupFailurePreservesPreference=true,oemBackoffAndRecovery=true,oemReconnectRecovery=true,explicitOffStopsRetry=true,hardwareWrites=0,physicalButtonsPressed=false},Backend.Program.Json));return 0;
  }
  finally{Directory.Delete(folder,true);}
 }
 internal static async Task<int> LoginModels(string output)
 {
  string sid="S-1-5-21-111-222-333-1001",image=@"C:\ONEXTweaks\ONEXTweaks.WidgetBridge.exe",runtime=@"C:\Runtime\dotnet.exe";string valid=ControllerLoginRecovery.Definition(sid,image,runtime);ControllerLoginRecovery.Validate(valid,sid,image,runtime);int checks=1;
  System.Xml.Linq.XNamespace n="http://schemas.microsoft.com/windows/2004/02/mit/task";var original=System.Xml.Linq.XElement.Parse(valid);var changes=new Action<System.Xml.Linq.XElement>[]
  {root=>root.Descendants(n+"RunLevel").Single().Value="HighestAvailable",root=>root.Descendants(n+"LogonType").Single().Value="ServiceAccount",root=>root.Descendants(n+"Arguments").Single().Value+=" --other",root=>root.Descendants(n+"LogonTrigger").Single().Name=n+"BootTrigger",root=>root.Descendants(n+"UserId").First().Value="S-1-5-18",root=>root.Descendants(n+"Command").Single().Value=@"C:\Other.exe",root=>root.Element(n+"Actions")!.Add(new System.Xml.Linq.XElement(n+"Exec",new System.Xml.Linq.XElement(n+"Command",@"C:\Other.exe")))};
  foreach(var change in changes){var copy=new System.Xml.Linq.XElement(original);change(copy);bool rejected=false;try{ControllerLoginRecovery.Validate(copy.ToString(),sid,image,runtime);}catch(InvalidDataException){rejected=true;}if(!rejected)throw new IOException("Unexpected recovery task accepted");checks++;}
  var normalized=new System.Xml.Linq.XElement(original);normalized.Descendants(n+"RunLevel").Single().Remove();ControllerLoginRecovery.Validate(normalized.ToString(),sid,image,runtime);checks++;
  var legacy=new System.Xml.Linq.XElement(original);legacy.Descendants(n+"Command").Single().Value=runtime;legacy.Descendants(n+"Arguments").Single().Value="\""+Path.ChangeExtension(image,".dll")+"\" "+ControllerLoginRecovery.Argument;ControllerLoginRecovery.Validate(legacy.ToString(),sid,image,runtime,legacy:true);checks++;bool legacyRejected=false;try{ControllerLoginRecovery.Validate(legacy.ToString(),sid,image,runtime);}catch(InvalidDataException){legacyRejected=true;}if(!legacyRejected)throw new IOException("Legacy recovery must require exact migration");checks++;
  string installed=BridgeInstallationLayout.InstalledImage,installedTask=ControllerLoginRecovery.InstalledDefinition(sid,installed);
  ControllerLoginRecovery.ValidateInstalled(installedTask,sid,installed);checks++;
  foreach(string wrong in new[]{valid,legacy.ToString(),ControllerLoginRecovery.InstalledDefinition(sid,Path.ChangeExtension(installed,".dll")),
   installedTask.Replace(ControllerLoginRecovery.Argument,ControllerLoginRecovery.Argument+" --unexpected"),
   installedTask.Replace("<Enabled>true</Enabled>","<Enabled>false</Enabled>")})
  {
   bool rejected=false;try{ControllerLoginRecovery.ValidateInstalled(wrong,sid,installed);}catch(InvalidDataException){rejected=true;}
   if(!rejected)throw new IOException("An unrelated or disabled installed recovery task was accepted");checks++;
  }
  if(!BridgeInstallationLayout.IsCaptureImage(installed)||!BridgeInstallationLayout.IsCaptureImage(BridgeInstallationLayout.DevelopmentImage)||
   BridgeInstallationLayout.IsCaptureImage(Path.Combine(Path.GetDirectoryName(installed)!,"ONEXTweaks.ControllerObserver.exe"))||
   BridgeInstallationLayout.IsCaptureImage(Path.Combine(Path.GetDirectoryName(installed)!,"other",BridgeInstallationLayout.BridgeFileName)))
   throw new IOException("Protected or legacy capture image policy changed");
  checks++;
  const string installedNt=@"\Device\HarddiskVolume1\Program Files\ONEXTweaks\WidgetBridge\ONEXTweaks.WidgetBridge.exe";
  const string oldRoot=@"\Device\HarddiskVolume2\Users\Owner\ONEXTweaks\dev\project\";
  if(!BridgeInstallationLayout.IsRecordedLeaseImage(installedNt,installedNt,oldRoot)||
   !BridgeInstallationLayout.IsRecordedLeaseImage(oldRoot+@"src\old\ONEXTweaks.WidgetBridge.exe",installedNt,oldRoot)||
   BridgeInstallationLayout.IsRecordedLeaseImage(installedNt.Replace("WidgetBridge\\","Unrelated\\"),installedNt,oldRoot)||
   BridgeInstallationLayout.IsRecordedLeaseImage(oldRoot+"ONEXTweaks.ControllerObserver.exe",installedNt,oldRoot))
   throw new IOException("Only owned recorded paths should be restored");
  checks++;
  string[] source=[@"USB\VID_045E&PID_028E\Fixture"];
  string[] otherDevice=[@"HID\VID_FFFF&PID_0001\Fixture"];
  ControllerCaptureLease.ValidateConfiguration(new ControllerHideState(true,false,otherDevice,[]),source);
  bool inertPreserved=false;
  try{ControllerCaptureLease.ValidateConfiguration(new ControllerHideState(false,false,otherDevice,[]),source);}
  catch(InvalidOperationException){inertPreserved=true;}
  if(!inertPreserved)throw new IOException("Capture could activate another tool's inactive HidHide configuration");
  checks++;
  string journal=output+".reservation";
  try{await ControllerCaptureLease.VerifyHideCommit(journal);checks++;}
  finally{File.Delete(journal);}
  await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=true,checks,watchdogReclaimedReservationTested=true,modelOnly=true,taskChanges=0,hardwareWrites=0},Backend.Program.Json));return 0;
 }
 internal static async Task<int> LoginNative(string output)
 {
  string reports=Path.GetDirectoryName(output)!,file=ControllerCaptureLease.LeaseFile(reports),status=Path.Combine(reports,"controller-login-recovery.json");ControllerHideState before;using(var h=new ControllerHidHide())before=h.Read();var slots=ControllerNative.Slots();var cases=new List<string>();System.Diagnostics.Process? worker=null,watchdog=null;string? failure=null;
  void Check(bool ok,string text){if(!ok)throw new IOException(text);cases.Add(text);}
  async Task<string> LoginTask(){DateTime started=await ControllerLoginRecovery.RunForVerification();for(int n=0;n<100;n++){await Task.Delay(100);if(File.Exists(status)&&File.GetLastWriteTimeUtc(status)>=started){using var json=JsonDocument.Parse(await File.ReadAllTextAsync(status));return json.RootElement.GetProperty("result").GetString()!;}}throw new IOException("Controller logon recovery task did not report");}
  try{
   ControllerLoginRecovery.Ensure();Check(!File.Exists(file)&&slots.SequenceEqual(new[]{0})&&ControllerNative.Read(0,out var physical)&&ControllerNative.Neutral(physical),"fresh neutral source and registered least-privilege recovery task");
   string ready=Path.Combine(reports,"..","privateReports","controller-login-qa-"+Guid.NewGuid().ToString("N"));var start=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--controller-lease-worker");start.ArgumentList.Add(ready);worker=System.Diagnostics.Process.Start(start)??throw new IOException("Login QA worker missing");for(int n=0;n<120&&!File.Exists(ready);n++){if(worker.HasExited)throw new IOException("Login QA worker failed");await Task.Delay(50);}Check(File.Exists(ready),"only own captured worker ready");File.Delete(ready);
   string activeResult=await LoginTask();Check(activeResult=="active-capture-protected"&&File.Exists(file),"logon recovery protects a fresh active capture ("+activeResult+", lease="+File.Exists(file)+", worker="+(File.Exists(ready+".state.json")?await File.ReadAllTextAsync(ready+".state.json"):"no-status")+")");var lease=JsonSerializer.Deserialize<ControllerLeaseData>(await File.ReadAllTextAsync(file),Backend.Program.Json)??throw new IOException("Own lease missing");Check(lease.Pid==worker.Id&&lease.WatchdogPid>0,"own worker and monitor identity recorded");watchdog=System.Diagnostics.Process.GetProcessById(lease.WatchdogPid);Check(watchdog.StartTime.ToUniversalTime().Ticks==lease.WatchdogStartedUtcTicks&&string.Equals(watchdog.MainModule?.FileName,Environment.ProcessPath,StringComparison.OrdinalIgnoreCase),"monitor executable and start time match own QA lease");
   watchdog.Kill();await watchdog.WaitForExitAsync();worker.Kill();await worker.WaitForExitAsync();string staleResult=await LoginTask();Check(staleResult=="stale-input-restored","independent scheduled task restores a stale lease with helper and monitor stopped ("+staleResult+")");Check(!File.Exists(file)&&(await ControllerRemapper.Observe(reports)).SequenceEqual(slots),"physical XInput returns without launching widget or helper");
  }catch(Exception e){failure=e.Message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>",StringComparison.OrdinalIgnoreCase);}
  finally{
   foreach(var process in new[]{worker,watchdog})if(process!=null){try{if(!process.HasExited)process.Kill();await process.WaitForExitAsync();}catch{}process.Dispose();}
   try{await ControllerCaptureLease.RecoverStopped(reports);}catch(Exception e){failure=(failure??"")+"; "+e.Message;}
   ControllerHideState after;using(var h=new ControllerHidHide())after=h.Read();bool restored=before.Active==after.Active&&before.Inverse==after.Inverse&&before.Devices.SequenceEqual(after.Devices)&&before.Applications.SequenceEqual(after.Applications);await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=failure==null&&restored,checks=cases.Count,cases,error=failure,hidHideConfigurationRestored=restored,physicalButtonsPressed=false,keyboardMouseInjected=false,startupPreferenceChanged=false,widgetOrHelperStartedByRecovery=false},Backend.Program.Json));
  }
  using var result=JsonDocument.Parse(await File.ReadAllTextAsync(output));return result.RootElement.GetProperty("passed").GetBoolean()?0:1;
 }
 private static async Task PublishReady(string path,string content){string temp=path+".tmp-"+Guid.NewGuid().ToString("N");await File.WriteAllTextAsync(temp,content);File.Move(temp,path,true);}
 internal static async Task<int> LeaseWorker(string ready)
 {
  string reports=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ONEXTweaks","dev","Reports");await using var runtime=new ControllerRemapper(reports);await runtime.Start();await PublishReady(ready,"ready");while(true){var state=runtime.Read();await PublishReady(ready+".state.json",JsonSerializer.Serialize(state,Backend.Program.Json));if(!state.Running)return 3;await Task.Delay(100);}
 }
 internal static async Task<int> MarkerLease(string ready)
 {
  string reports=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ONEXTweaks","dev","Reports");var sources=ControllerNative.Sources();if(sources.Length!=1||!ControllerNative.Read(0,out var initial)||!ControllerNative.Neutral(initial))throw new IOException("One neutral physical source required");
  int feedbackCount=0;using var pad=new ControllerVirtualPad(0,false,(large,small)=>{if(large==1&&small==0)Interlocked.Increment(ref feedbackCount);});var marker=new ControllerFrame(Lx:1,Ly:2,Rx:3,Ry:4);pad.Submit(marker);await Task.Delay(300);using var lease=await ControllerCaptureLease.Acquire(reports,sources[0]);var frames=ControllerNative.Slots().Select(slot=>{ControllerNative.Read(slot,out var frame);return new{slot,frame};}).ToArray();string publish=ready+".tmp";await File.WriteAllTextAsync(publish,JsonSerializer.Serialize(new{ownerPid=Environment.ProcessId,ownSlot=pad.Slot,marker,frames,sourceIds=sources[0].InstanceIds},Backend.Program.Json));File.Move(publish,ready,true);
  var until=DateTimeOffset.UtcNow.AddSeconds(20);while(!File.Exists(ready+".stop")&&DateTimeOffset.UtcNow<until){lease.Touch();string temp=ready+".feedback.tmp";await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(new{ownerPid=Environment.ProcessId,ownSlot=pad.Slot,marker,frames,sourceIds=sources[0].InstanceIds,feedbackCount=Volatile.Read(ref feedbackCount)},Backend.Program.Json));File.Move(temp,ready,true);await Task.Delay(300);}pad.Submit(new());return 0;
 }
 internal static async Task<int> InputLeaseWorker(string ready)
 {
  string reports=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ONEXTweaks","dev","Reports");var sources=ControllerNative.Sources();if(sources.Length!=1||!ControllerNative.Read(0,out var input)||!ControllerNative.Neutral(input))throw new IOException("Neutral physical source required");
  using var window=new ControllerTestWindow();window.RequireForeground();using var pad=new ControllerVirtualPad(0,false);pad.Submit(new());using var lease=await ControllerCaptureLease.Acquire(reports,sources[0]);using var emitter=new ControllerInputEmitter((keys,mouse)=>lease.Input(keys,mouse));window.RequireForeground();emitter.Apply(new(new(),System.Collections.Immutable.ImmutableHashSet.Create(17,90),System.Collections.Immutable.ImmutableHashSet.Create("left"),0,0,0,[]),0);
  for(int n=0;n<50&&(!window.Events.Any(e=>e=="256:90")||!window.Events.Any(e=>e.StartsWith("513:",StringComparison.Ordinal)));n++)await Task.Delay(20);
  if(!window.Events.Any(e=>e=="256:90")||!window.Events.Any(e=>e.StartsWith("513:",StringComparison.Ordinal))||!ControllerTestWindow.IsHeld(17)||!ControllerTestWindow.IsHeld(90)||!ControllerTestWindow.IsHeld(1))throw new IOException("Owned keyboard/mouse hold was not observed");
  await PublishReady(ready,"ready");while(true){lease.Touch();await Task.Delay(700);}
 }
 internal static async Task<int> Native(string output,bool cloakOnly=false)
 {
  string reports=Path.GetDirectoryName(output)!;int checks=0;var cases=new List<string>();System.Diagnostics.Process? crashWorker=null;string? failure=null;ControllerHideState before;using(var hide=new ControllerHidHide())before=hide.Read();var slotsBefore=ControllerNative.Slots();
  void Check(bool value,string name){checks++;if(!value)throw new IOException(name);cases.Add(name);}
  async Task<ControllerFrame> Confirm(int slot,ControllerFrame expected){ControllerFrame actual=default;for(int n=0;n<60;n++){if(ControllerNative.Read(slot,out actual)&&actual==expected)return actual;await Task.Delay(10);}throw new IOException("Virtual XInput readback did not match submitted state");}
  try
  {
   Check(slotsBefore.SequenceEqual([0])&&ControllerNative.Read(0,out var physical)&&ControllerNative.Neutral(physical),"one neutral physical source");
   if(!cloakOnly)
   {
   using var ownedWindow=new ControllerTestWindow();await Task.Delay(200);ownedWindow.RequireForeground();
   using(var source=new ControllerVirtualPad(0,false))using(var target=new ControllerVirtualPad(source.Slot,false))
   {
    var policy=new ControllerPolicy(new(){{"A",new("gamepad","B")}}).Validate();var translator=new ControllerTranslator();var vectors=ControllerPolicy.Buttons.Values.Select(mask=>new ControllerFrame(mask)).Concat(new[]{new ControllerFrame(Lt:255),new ControllerFrame(Rt:173),new ControllerFrame(Lx:short.MinValue,Ly:short.MaxValue,Rx:-17000,Ry:21000),new ControllerFrame()}).ToArray();
    foreach(var input in vectors){ownedWindow.RequireForeground();source.Submit(input);var read=await Confirm(source.Slot,input);var mapped=translator.Translate(read,policy);target.Submit(mapped.Pad);await Confirm(target.Slot,mapped.Pad);checks++;}
    source.Submit(new());target.Submit(new());await Confirm(target.Slot,new());cases.Add("18 injected virtual source/translator/virtual target round-trips and neutral release");
   }
   }
   await using(var runtime=new ControllerRemapper(reports))
   {await runtime.Start();var live=runtime.Read();var visible=await ControllerRemapper.Observe(reports);Check(live.Running&&visible.SequenceEqual(new[]{live.ObserverVirtualSlot})&&live.ObserverVirtualSlot>=0,"only feedback-challenged own virtual output visible; per-process slot indices may differ");Check(ControllerNative.Read(0,out _),"whitelisted source reader still sees physical source");for(int tick=0;tick<100;tick++){await Task.Delay(100);if(!runtime.Read().Running)throw new IOException("Steady capture stopped: "+runtime.Read().Error);}Check(runtime.Read().Running&&File.Exists(ControllerCaptureLease.LeaseFile(reports)),"physical capture and journal remain healthy for ten seconds");await runtime.Stop();Check((await ControllerRemapper.Observe(reports)).SequenceEqual(slotsBefore),"normal stop restores physical visibility and removes virtual output");}
   string ready=Path.Combine(reports,"..","privateReports","controller-crash-ready-"+Guid.NewGuid().ToString("N"));var start=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--controller-lease-worker");start.ArgumentList.Add(ready);crashWorker=System.Diagnostics.Process.Start(start)??throw new IOException("Crash-recovery worker did not start");
   for(int n=0;n<120&&!File.Exists(ready);n++){if(crashWorker.HasExited)throw new IOException("Crash-recovery worker could not capture physical controller");await Task.Delay(50);}Check(File.Exists(ready),"separate recovery monitor armed before crash test");File.Delete(ready);Check((await ControllerRemapper.Observe(reports)).Length==1,"one verified virtual output before intentional own-worker crash");crashWorker.Kill();await crashWorker.WaitForExitAsync();
   bool recovered=false;for(int n=0;n<40;n++){await Task.Delay(150);if(!File.Exists(ControllerCaptureLease.LeaseFile(reports))&&(await ControllerRemapper.Observe(reports)).SequenceEqual(slotsBefore)){recovered=true;break;}}Check(recovered,"independent watchdog restores physical input after worker kill");
   if(!cloakOnly)
   {
    using var parentWindow=new ControllerTestWindow();parentWindow.RequireForeground();ready=Path.Combine(reports,"..","privateReports","controller-input-ready-"+Guid.NewGuid().ToString("N"));start=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--controller-input-lease-worker");start.ArgumentList.Add(ready);crashWorker.Dispose();crashWorker=System.Diagnostics.Process.Start(start)??throw new IOException("Owned input worker did not start");parentWindow.PermitForeground(crashWorker.Id);
    for(int n=0;n<120&&!File.Exists(ready);n++){if(crashWorker.HasExited)throw new IOException("Owned keyboard/mouse worker refused input: "+(File.Exists(ready+".error.json")?await File.ReadAllTextAsync(ready+".error.json"):"no error report, exit="+crashWorker.ExitCode));await Task.Delay(50);}Check(File.Exists(ready)&&ControllerTestWindow.IsHeld(17)&&ControllerTestWindow.IsHeld(90)&&ControllerTestWindow.IsHeld(1),"mapped Ctrl/Z and mouse held only in dedicated own foreground window");File.Delete(ready);crashWorker.Kill();await crashWorker.WaitForExitAsync();
    bool released=false;for(int n=0;n<50;n++){await Task.Delay(150);if(!File.Exists(ControllerCaptureLease.LeaseFile(reports))&&!ControllerTestWindow.IsHeld(17)&&!ControllerTestWindow.IsHeld(90)&&!ControllerTestWindow.IsHeld(1)&&(await ControllerRemapper.Observe(reports)).SequenceEqual(slotsBefore)){released=true;break;}}Check(released,"watchdog releases owned keyboard/mouse and physical controller after worker crash");
   }
  }
  catch(Exception e){failure=e.Message;}
  finally
  {
   if(crashWorker!=null){try{if(!crashWorker.HasExited)crashWorker.Kill();await crashWorker.WaitForExitAsync();}catch{}crashWorker.Dispose();}
   try{await ControllerCaptureLease.RecoverStopped(reports);}catch(Exception e){failure=(failure??"")+"; recovery: "+e.Message;}
   bool restored=false,sourceRestored=false;try{ControllerHideState after;using(var hide=new ControllerHidHide())after=hide.Read();restored=before.Active==after.Active&&before.Inverse==after.Inverse&&before.Devices.SequenceEqual(after.Devices)&&before.Applications.SequenceEqual(after.Applications);sourceRestored=(await ControllerRemapper.Observe(reports)).SequenceEqual(slotsBefore);}catch(Exception error){failure=(failure??"")+"; final verification: "+error.Message;}
   await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=failure==null&&restored&&sourceRestored,checks,cases,error=failure?.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>",StringComparison.OrdinalIgnoreCase),hidHideConfigurationRestored=restored,physicalVisibilityRestored=sourceRestored,injectedVirtualSource=!cloakOnly,physicalButtonsPressed=false,keyboardMouseInjectionTested=!cloakOnly&&failure==null,gameTested=false,tdpWritesByCandidate=0},Backend.Program.Json));
  }
  using var result=JsonDocument.Parse(await File.ReadAllTextAsync(output));return result.RootElement.GetProperty("passed").GetBoolean()?0:1;
 }
 internal static async Task<int> Probe(string output)
 {
  var sources=ControllerNative.Sources();object hide;try{using var h=new ControllerHidHide();var state=h.Read();hide=new{available=true,state.Active,state.Inverse,blockedCount=state.Devices.Length,applicationCount=state.Applications.Length};}catch(Exception e){hide=new{available=false,error=e.Message};}
  await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{readOnly=true,physicalSources=sources.Length,sourceNodeCounts=sources.Select(s=>s.InstanceIds.Length).ToArray(),slots=ControllerNative.Slots(),hidHide=hide,hardwareWrites=0},Backend.Program.Json));return 0;
 }
 private sealed class OemStartupFake:IControllerOemRuntime
 {
  internal int Attempts;internal long CurrentSession=1;private long preparedSession;
  public bool Prepared=>ControllerOemManager.OwnsPreparation(preparedSession,CurrentSession);
  public object Read()=>new{prepared=Prepared};
  public Task Prepare(){Attempts++;if(Attempts<=2)throw new IOException("Model MCU is still starting");preparedSession=CurrentSession;return Task.CompletedTask;}
  public Task Release(){preparedSession=0;return Task.CompletedTask;}
 }

}
