// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using System.Text.Json.Serialization;
namespace ONEXTweaks.WidgetBridge;
internal sealed record ControllerProfileEntry(string Id,string Name,string? PerformanceProfileId,ControllerPolicy Policy,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]ControllerProfileGroups? CaptureGroups=null,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? ProcessPath=null);
internal sealed record ControllerProfileStore(int Schema,bool Enabled,string EditorId,string? ManualId,string? ManualContext,ControllerProfileEntry Global,List<ControllerProfileEntry> Profiles,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]bool OemEnabled=false);
internal sealed partial class ControllerProfiles:IAsyncDisposable
{
 private ControllerInputHealthState? inputHealth;internal async Task RefreshInputHealth(){inputHealth=await ControllerInputHealth.Check(Path.Combine(Path.GetDirectoryName(file)!,"Reports"),runtime);}
 private readonly Func<bool> gameLightingAvailable;private readonly ControllerMotion motion;private readonly ControllerDiagnostics diagnostics;
 private ControllerVibrationTestResult? lastVibrationTest;private int vibrationTestSerial;private string? vibrationTestProfile;
 private IControllerOemRuntime? oemManager;private long nextOemAttempt;private int oemFailures;private ControllerOemPolicy currentOem=new ControllerOemPolicy().Validate();private ControllerOemPolicy? lastOem;private string? lastOemProfile;
 internal ControllerOemPolicy CurrentOem=>Volatile.Read(ref currentOem);
 internal void BindOemManager(IControllerOemRuntime manager){oemManager=manager;}
 private readonly string file;private readonly ControllerOemRawInput? oemButtons;private readonly IControllerCaptureRuntime runtime;private ControllerPolicy? lastRuntimePolicy;private bool captureWasRunning;private string? modeError;private readonly Func<string> context,performance;private readonly Func<(string Id,string Name)[]> parents;private ControllerProfileStore store;private string? lastRuntimeLightingContext;private string? lastActive;private string? lastContext;
 private static readonly int[] CaptureRetryDelays=[1000,2000,5000,10000,30000];
 private readonly Func<long> runtimeClock;
 private readonly Func<ProfileSession?> currentSession;
 private int captureFailures;
 private long nextCaptureAttempt,runningSince=-1;
 internal ControllerProfiles(string reports,Func<string> stamp,Func<string> activePerformance,Func<(string Id,string Name)[]> candidates,ControllerOemRawInput? buttons=null,IControllerCaptureRuntime? capture=null,KeyboardMouseSettings? keyboardSettings=null,Func<bool>? defaultVirtualAvailable=null,ONEXTweaks.Panel.PowerChannel? power=null,Func<long>? captureClock=null,Func<ProfileSession?>? session=null)
 {
  runtimeClock=captureClock??(()=>Environment.TickCount64);
  currentSession=session??(()=>null);
  gameLightingAvailable=()=>power?.SupportsGameLighting==true;keyboardMouse=keyboardSettings;oemButtons=buttons;buttons?.ReserveKeyboardSwitch(keyboardMouse!=null);file=Path.Combine(reports,"..","controller-profiles.json");motion=new ControllerMotion(Path.Combine(Path.GetDirectoryName(file)!,"controller-motion-calibration.json"));diagnostics=new ControllerDiagnostics();context=stamp;performance=activePerformance;parents=candidates;runtime=capture??new ControllerRemapper(reports,buttons,()=>motion.Read().Calibration,()=>motion.Read().Busy||diagnostics.Active,gameLighting:power==null?null:request=>ControllerGameLighting.Send(power,request));
  store=File.Exists(file)?JsonSerializer.Deserialize<ControllerProfileStore>(File.ReadAllText(file),Backend.Program.Json)??throw new InvalidDataException("Controller profiles are unreadable"):new(1,defaultVirtualAvailable?.Invoke()??(capture==null&&ControllerViiperInstallation.Read().CanUseVirtual),"global",null,null,new("global","Global Profile",null,new ControllerPolicy().Validate()),[]);
  var bound=store.Profiles.Where(entry=>entry.ProcessPath!=null).ToArray();
  if(store.Schema!=1||store.Profiles.Count>128||Entries.Select(e=>e.Id).Distinct().Count()!=Entries.Count()||
      store.Global.ProcessPath!=null||bound.Any(entry=>entry.PerformanceProfileId!=null||!Path.IsPathFullyQualified(entry.ProcessPath!)||!entry.ProcessPath!.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))||
      bound.Select(entry=>entry.ProcessPath!).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=bound.Length)throw new InvalidDataException("Unsupported controller profile document");
  store=store with{Global=store.Global with{Policy=store.Global.Policy.Validate()},Profiles=store.Profiles.Select(entry=>entry with{Policy=entry.Policy.Validate()}).ToList()};if(!File.Exists(file))Save();lastContext=context();Refresh();buttons?.ConfigureGestures(()=>CurrentOem);
 }
 private ControllerDiagnosticObservation? lastDiagnosticObservation;
 internal string BeginHostSticks()=>BeginHostDiagnostic("sticks");
 internal string BeginHostDiagnostic(string kind){
  var motionState=motion.Read();
  if(motionState.Busy)throw new InvalidOperationException("Wait for the motion check or calibration to finish");
  var editor=Find(store.EditorId);
  var policy=Effective(editor);
  ControllerGyroPreviewSettings? preview=kind=="gyro"?new(policy.Gyro??new(),motionState.Calibration,
   store.Enabled&&!policy.UseHardwareController&&!KeyboardMouseActive):null;
  // Capture the effective editor policy once. Physical and OEM button levels remain read-only, live inputs.
  diagnostics.Begin(kind,policy.Tuning??new(),preview,()=>(ushort)(oemButtons?.OwnsAliases==true?oemButtons.HeldMask:0));
  return diagnostics.Session.Token;
 }
 internal void EndHostSticks(string token)=>diagnostics.Stop(token);
 internal ControllerDiagnosticState DiagnosticRead(string token){var value=diagnostics.Read(token);Volatile.Write(ref lastDiagnosticObservation,new(DateTimeOffset.UtcNow,value,(runtime as ControllerRemapper)?.DiagnosticInput));return value;}
 internal object? DiagnosticSnapshot{get{var sample=Volatile.Read(ref lastDiagnosticObservation);if(sample==null||(DateTimeOffset.UtcNow-sample.UpdatedUtc).TotalSeconds>30)return null;return new{sample.UpdatedUtc,ageMilliseconds=(DateTimeOffset.UtcNow-sample.UpdatedUtc).TotalMilliseconds,sample.State,sample.RuntimeInput,runtimeAgeMilliseconds=sample.RuntimeInput==null?(double?)null:System.Diagnostics.Stopwatch.GetElapsedTime(sample.RuntimeInput.Timestamp).TotalMilliseconds};}}
 internal string AppliedId=>Active.Id;internal string EditingId=>store.EditorId;
 private IEnumerable<ControllerProfileEntry> Entries=>new[]{store.Global}.Concat(store.Profiles);
 private ControllerProfileEntry Find(string id)=>Entries.FirstOrDefault(e=>e.Id==id)??throw new InvalidDataException("The controller profile no longer exists");
 private ControllerProfileEntry? FindProcess(string path)=>store.Profiles.FirstOrDefault(entry=>entry.ProcessPath?.Equals(path,StringComparison.OrdinalIgnoreCase)==true);
 private ControllerProfileEntry Active=>store.ManualId!=null&&store.ManualContext==context()?Find(store.ManualId):
    (currentSession() is {} session?FindProcess(session.Path):null)??Entries.FirstOrDefault(e=>e.PerformanceProfileId==performance())??store.Global;
 internal async Task CreateForProcess(ProfileSession process)
 {
  var existing=FindProcess(process.Path);
  if(existing!=null){store=store with{EditorId=existing.Id};Save();return;}
  if(store.Profiles.Count>=128)throw new InvalidDataException("Controller profile limit reached");
  string basis=process.Name[..Math.Min(process.Name.Length,80)];
  string name=basis;
  for(int index=2;Entries.Any(entry=>entry.Name.Equals(name,StringComparison.OrdinalIgnoreCase));index++)name=basis+" ("+index+")";
  var entry=new ControllerProfileEntry(Guid.NewGuid().ToString("N"),name,null,Effective(Active).Validate(),ProcessPath:process.Path);
  var before=store;
  store=store with{EditorId=entry.Id,Profiles=store.Profiles.Append(entry).ToList()};
  try{Save();}catch{store=before;throw;}
  if(Active.Id==entry.Id)await MaintainRuntime(true);
 }
 private readonly Dictionary<string,(ControllerPolicy Local,ControllerPolicy Global,ControllerProfileGroups Groups,ControllerPolicy Effective)> effectiveCache=new();
 private ControllerPolicy Effective(ControllerProfileEntry entry){
  if(entry.Id=="global"||entry.CaptureGroups==null||entry.CaptureGroups.All)return entry.Policy;
  if(effectiveCache.TryGetValue(entry.Id,out var cached)&&ReferenceEquals(cached.Local,entry.Policy)&&ReferenceEquals(cached.Global,store.Global.Policy)&&cached.Groups==entry.CaptureGroups)return cached.Effective;
  var result=ControllerProfileGroups.Compose(entry.Policy,store.Global.Policy,entry.CaptureGroups);effectiveCache[entry.Id]=(entry.Policy,store.Global.Policy,entry.CaptureGroups,result);return result;
 }
 private void EditPolicy(ControllerProfileEntry entry,ControllerPolicy proposed){
  var changed=entry.Id=="global"?(proposed.Validate(),store.Global.Policy):ControllerProfileGroups.Edit(entry.Policy,store.Global.Policy,entry.CaptureGroups,proposed);
  if(entry.Id!="global"&&!ReferenceEquals(changed.Item2,store.Global.Policy))store=store with{Global=store.Global with{Policy=changed.Item2}};
  Replace(entry with{Policy=changed.Item1});
 }
 private void Save(){string temp=file+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(store,Backend.Program.Json));File.Move(temp,file,true);}
 private void Replace(ControllerProfileEntry entry){if(entry.Id=="global")store=store with{Global=entry};else{int index=store.Profiles.FindIndex(e=>e.Id==entry.Id);if(index<0)throw new InvalidDataException("Controller profile was removed");store.Profiles[index]=entry;}Save();}
 internal void Refresh()
 {
  if(store.ManualId!=null&&store.ManualContext!=context()){store=store with{ManualId=null,ManualContext=null};Save();}
  if(lastContext!=context()){lastContext=context();store=store with{EditorId=Active.Id};Save();}
  var active=Active with{Policy=Effective(Active)};
  string oemProfile=KeyboardMouseActive?"<keyboardMouse>":active.Id;
  var keyboardPolicy=KeyboardMouseActive?keyboardMouse?.Value:null;
  bool keyboardBindingsChanged=keyboardPolicy!=null&&!ReferenceEquals(keyboardPolicy,lastOemKeyboardPolicy)&&
   (lastOemKeyboardPolicy==null||!keyboardPolicy.SameBindings(lastOemKeyboardPolicy));
  lastOemKeyboardPolicy=keyboardPolicy;
  if(lastOemProfile!=oemProfile||!KeyboardMouseActive&&!ReferenceEquals(lastOem,active.Policy.Oem)||keyboardBindingsChanged)
  {lastOemProfile=oemProfile;lastOem=active.Policy.Oem;Volatile.Write(ref currentOem,KeyboardOem(lastOem));oemButtons?.RefreshGestures();}
 }
 // Desired OEM state is a user preference. A temporarily unavailable MCU at
 // logon must not turn that preference off. Normal telemetry retries once the
 // hardware channel is ready, under the same profile gate as explicit edits.
 internal async Task MaintainOem(long? clock=null)
 {
  long now=clock??Environment.TickCount64;
  if(!(store.OemEnabled||KeyboardMouseActive)||oemManager==null||oemManager.Prepared||now<nextOemAttempt)return;
  try{await oemManager.Prepare();oemFailures=0;nextOemAttempt=0;}
  catch{oemFailures=Math.Min(oemFailures+1,5);nextOemAttempt=now+new[]{0,1000,2000,5000,10000,30000}[oemFailures];}
 }
 private bool HardwareOverride=>!KeyboardMouseActive&&Active.Id!="global"&&Active.Policy.UseHardwareController;
 private bool WantsVirtual=>KeyboardMouseActive||store.Enabled&&!HardwareOverride;
 private void ApplyRuntimePolicy(ControllerProfileEntry active)
 {if(KeyboardMouseActive){if(lastActive!="<keyboardMouse>"||lastRuntimeKeyboardRevision!=KeyboardRevision){runtime.Policy(new ControllerPolicy(KeyboardMouse:keyboardMouse!.Value));lastActive="<keyboardMouse>";lastRuntimeKeyboardRevision=KeyboardRevision;}return;}string lightingContext=active.Policy.ForwardGameLighting?active.Id+":"+context():"";if(active.Id!=lastActive||!ReferenceEquals(active.Policy,lastRuntimePolicy)||lastRuntimeLightingContext!=lightingContext){runtime.Policy(lightingContext.Length==0?active.Policy:active.Policy with{LightingContext=lightingContext});lastRuntimeLightingContext=lightingContext;lastActive=active.Id;lastRuntimePolicy=active.Policy;}}
 private void ResetCaptureRetries(){captureFailures=0;nextCaptureAttempt=0;runningSince=-1;}
 // Capture changes are serialized with profile mutations. A capture failure
 // returns physical input without changing the user's saved controller mode.
 internal async Task MaintainRuntime(bool throwOnError=false)
 {
  Refresh();var active=Active with{Policy=Effective(Active)};bool desired=WantsVirtual;long now=runtimeClock();
  try
  {
   var state=runtime.Read();if(state.Busy)return;
   if(!desired){if(state.Running){await runtime.Stop();inputHealth=null;}captureWasRunning=false;ApplyRuntimePolicy(active);ResetCaptureRetries();modeError=null;return;}
   if(captureWasRunning&&!state.Running&&state.Error!=null)throw new IOException(state.Error);
   ApplyRuntimePolicy(active);string target=KeyboardMouseActive?"Xbox360":active.Policy.OutputTarget;
   if(state.Running&&state.OutputTarget!=target){await runtime.Stop();captureWasRunning=false;runningSince=-1;inputHealth=null;state=runtime.Read();}
   if(!state.Running)
   {
    if(now<nextCaptureAttempt)return;
    inputHealth=null;await runtime.Start();state=runtime.Read();
   }
   captureWasRunning=state.Running;
   if(!state.Running)return;
   if(runningSince<0)runningSince=runtimeClock();
   if(captureFailures!=0&&now-runningSince>=10000){captureFailures=0;nextCaptureAttempt=0;}
   modeError=null;
  }
  catch(ControllerInputNotNeutralException error){modeError=error.Message;captureWasRunning=false;runningSince=-1;}
  catch(Exception error)
  {
   modeError=error.Message;captureWasRunning=false;runningSince=-1;
   if(desired)
   {
    nextCaptureAttempt=captureFailures<CaptureRetryDelays.Length?runtimeClock()+CaptureRetryDelays[captureFailures++]:long.MaxValue;
    if(KeyboardMouseActive)
    {
     Volatile.Write(ref keyboardMouseActive,false);Interlocked.Increment(ref keyboardModeRevision);Refresh();
     if(!store.OemEnabled&&oemManager!=null)try{await oemManager.Release();}catch(Exception recovery){modeError+="; OEM restore: "+recovery.Message;}
    }
   }
   if(throwOnError)throw;
  }
 }
 internal async Task Resume(){await MaintainOem();await MaintainRuntime();}
 internal object Read()
 {
  Refresh();var active=Active;var editor=Find(store.EditorId) with{Policy=Effective(Find(store.EditorId))};bool hidHide=false;try{using var h=new ControllerHidHide();h.Read();hidHide=true;}catch{}
  return new{inputHealthSupported=true,inputHealth=ControllerInputHealth.Snapshot(inputHealth,runtime.Read()),captureGroupsSupported=true,ds4LightingSupported=gameLightingAvailable(),ds4AuxiliarySupported=true,ds4TouchpadZonesSupported=true,diagnostics=diagnostics.Session,motion=motion.Read(),schema=1,inputMode=KeyboardMouseActive?"keyboardMouse":"gamepad",keyboardDoubleReserved=keyboardMouse!=null,virtualBackend="VIIPER",backendInstallation=ControllerViiperInstallation.Read(),lastVibrationTest,vibrationTestSerial,vibrationTestProfile,enabled=store.Enabled,effectiveEnabled=WantsVirtual,hardwareProfileOverride=HardwareOverride,modeError,activeId=editor.Id,appliedId=active.Id,activeName=active.Name,editorId=editor.Id,editor,onAc=false,manualOverride=store.ManualId!=null,selection=store.ManualId!=null?"manual":"automatic",profiles=Entries.Select(e=>e with{Policy=Effective(e)}).ToArray(),entries=Entries.OrderBy(e=>e.Id=="global"?0:e.PerformanceProfileId==null&&e.ProcessPath==null?1:2).ThenBy(e=>e.Name,StringComparer.OrdinalIgnoreCase).Select(e=>new{id=e.Id,name=e.Name,processPath=e.ProcessPath??e.PerformanceProfileId??"",split=false,ac=e.Policy,dc=e.Policy,effectiveAc=Effective(e),effectiveDc=Effective(e)}).ToArray(),creationParents=parents().Where(p=>p.Id!="global").Select(p=>new{id=p.Id,name=p.Name}).ToArray(),linkedGroupName="Per-game / Per-app",runtime=runtime.Read(),hidHideAvailable=hidHide,inputs=ControllerPolicy.Inputs,targets=ControllerPolicy.PadTargets,parentCandidates=parents().Select(p=>new{id=p.Id,name=p.Name}).ToArray(),reservedGuide=true,extraKeysNeedFirmwareSignal=true,standardRemappingEnabled=!editor.Policy.GamepadRemappingDisabled,oemEnabled=store.OemEnabled,oemRuntime=oemManager?.Read(),oemDefaults=ControllerOemPolicy.Inputs.ToDictionary(id=>id,ControllerOemPolicy.Default),oemInputs=ControllerOemPolicy.Inputs.Select(id=>new{id,location=ControllerOemPolicy.Front.Contains(id)?"front":"rear"}).ToArray(),oemInputState=oemButtons?.Read()};
 }
 internal async Task Mutate(JsonElement change)
 {
  string operation=change.GetProperty("operation").GetString()??"";
  if(operation=="diagnosticsStart"){if(motion.Read().Busy)throw new InvalidOperationException("Wait for the motion check or calibration to finish");diagnostics.Begin(change.GetProperty("kind").GetString()??"",Find(store.EditorId).Policy.Tuning??new());return;}
  if(operation=="diagnosticsStop"){diagnostics.Stop(change.GetProperty("token").GetString()??"");return;}
  if(operation is "motionCheck" or "motionCalibrate" or "motionReset"){if(diagnostics.Active)throw new InvalidOperationException("Close the live controller test first");motion.Begin(operation);return;}
  Refresh();
  if(operation=="oemEnable")
  {
   if(oemManager==null)throw new IOException("OEM button manager unavailable");bool value=change.GetProperty("enabled").GetBoolean();
   try{if(value)await oemManager.Prepare();else if(!KeyboardMouseActive)await oemManager.Release();store=store with{OemEnabled=value};Save();}
   catch{if(oemManager.Prepared){store=store with{OemEnabled=true};Save();}throw;}return;
  }
  if(operation=="enable")
  {
   bool value=change.GetProperty("enabled").GetBoolean();store=store with{Enabled=value};Save();ResetCaptureRetries();await MaintainRuntime(true);return;
  }
  if(operation=="recover"){store=store with{Enabled=false};Save();ResetCaptureRetries();Volatile.Write(ref keyboardMouseActive,false);Interlocked.Increment(ref keyboardModeRevision);Refresh();await runtime.Stop();captureWasRunning=false;modeError=null;await ControllerCaptureLease.RecoverStopped(Path.Combine(Path.GetDirectoryName(file)!,"Reports"));return;}
  if(operation=="automatic"){store=store with{ManualId=null,ManualContext=null};lastActive=null;Refresh();store=store with{EditorId=Active.Id};Save();await MaintainRuntime(true);return;}
  if(operation=="create")
  {
   string? parent=change.TryGetProperty("performanceProfileId",out var supplied)?supplied.GetString():null;var available=parents();if(parent!=null&&!available.Any(p=>p.Id==parent))throw new InvalidDataException("The Performance profile no longer exists");
   var existing=parent==null?null:Entries.FirstOrDefault(e=>e.PerformanceProfileId==parent);if(existing!=null){store=store with{EditorId=existing.Id};Save();return;}
   if(store.Profiles.Count>=128)throw new InvalidDataException("Controller profile limit reached");string? requested=change.TryGetProperty("name",out var title)?title.GetString()?.Trim():null;if(requested!=null&&(requested.Length is <1 or >80||requested.Any(char.IsControl)))throw new InvalidDataException("Use a name from1 to80 characters");string baseName=parent==null?requested??"Manual Profile":available.First(p=>p.Id==parent).Name;string name=baseName;for(int n=2;Entries.Any(e=>e.Name.Equals(name,StringComparison.OrdinalIgnoreCase));n++)name=baseName+" ("+n+")";
   var entry=new ControllerProfileEntry(Guid.NewGuid().ToString("N"),name,parent,Effective(Active).Validate());
   var before=store;
   store=store with{EditorId=entry.Id,Profiles=store.Profiles.Append(entry).ToList()};
   if(parent==null)store=store with{ManualId=entry.Id,ManualContext=context()};
   try{Save();}catch{store=before;throw;}
   lastActive=null;await MaintainRuntime(true);return;
  }
  string id=change.TryGetProperty("id",out var identity)?identity.GetString()??"":"";var selected=Find(id);
  switch(operation)
  {
   case "keep":store=store with{EditorId="global",ManualId="global",ManualContext=context()};lastActive=null;Refresh();Save();break;
   case "select":store=store with{EditorId=id,ManualId=id,ManualContext=context()};lastActive=null;Refresh();Save();break;
   case "edit":if(id!=store.EditorId)throw new InvalidOperationException("The editing profile changed; repeat the adjustment");var policy=change.GetProperty("policy").Deserialize<ControllerPolicy>(Backend.Program.Json)?.Validate()??throw new InvalidDataException("Controller policy is missing");if(id=="global"&&policy.UseHardwareController)throw new InvalidDataException("Use Controller Mode to change the Global controller mode");EditPolicy(selected,policy);break;
   case "captureGroups":if(id=="global")throw new InvalidDataException("Global is the source for inherited settings");if(id!=store.EditorId)throw new InvalidOperationException("The editing profile changed; repeat the scope adjustment");var groups=change.GetProperty("groups").Deserialize<ControllerProfileGroups>(Backend.Program.Json)??throw new InvalidDataException("Choose the groups stored in this profile");Replace(selected with{CaptureGroups=groups.All?null:groups});break;
   case "reset":Replace(selected with{Policy=new ControllerPolicy().Validate(),CaptureGroups=null});break;
    case "vibrationTest":if(id!=store.EditorId)throw new InvalidOperationException("The editing profile changed; repeat the vibration test");lastVibrationTest=await ControllerRemapper.TestVibration(Effective(selected).Vibration??new());vibrationTestProfile=id;vibrationTestSerial++;break;
    case "resetGroup":if(id!=store.EditorId)throw new InvalidOperationException("The editing profile changed; repeat the reset");EditPolicy(selected,ControllerRemappingGroups.ResetGroup(Effective(selected),change.GetProperty("group").GetString()??""));break;
    case "resetBinding":if(id!=store.EditorId)throw new InvalidOperationException("The editing profile changed; repeat the reset");EditPolicy(selected,ControllerRemappingGroups.ResetBinding(Effective(selected),change.GetProperty("source").GetString()??""));break;
   case "rename":if(id=="global")throw new InvalidDataException("The Global profile has a fixed name");string title=change.GetProperty("name").GetString()?.Trim()??"";if(title.Length is <1 or >80||title.Any(char.IsControl))throw new InvalidDataException("Use a name from1 to80 characters");Replace(selected with{Name=title});break;
   case "delete":if(id=="global")throw new InvalidDataException("Keep the Global profile");store.Profiles.RemoveAll(e=>e.Id==id);effectiveCache.Remove(id);if(store.ManualId==id)store=store with{ManualId=null,ManualContext=null};if(store.EditorId==id)store=store with{EditorId="global"};lastActive=null;Refresh();Save();break;
   default:throw new InvalidDataException("Unknown controller profile action");
  }
  await MaintainRuntime(true);
 }
 public async ValueTask DisposeAsync(){oemButtons?.ReserveKeyboardSwitch(false);try{await diagnostics.DisposeAsync();await motion.DisposeAsync();}finally{await runtime.DisposeAsync();}}
}
