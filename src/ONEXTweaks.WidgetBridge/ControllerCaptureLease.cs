// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal sealed record ControllerLeaseData(string Token,int Pid,long StartedUtcTicks,DateTimeOffset Heartbeat,bool WasActive,string[] AddedDevices,string[] AddedApplications,int[]? HeldKeys=null,string[]? HeldMouse=null,bool Recovering=false,int WatchdogPid=0,long WatchdogStartedUtcTicks=0,[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]bool PhysicalRestored=false,bool HidePending=false);
internal sealed class ControllerCaptureLease:IDisposable
{
 private readonly string file;private ControllerLeaseData data;private bool released;
 private static int sessionEnding;
 internal string Token=>data.Token;
 private ControllerCaptureLease(string path,ControllerLeaseData value){file=path;data=value;}
 internal static string LeaseFile(string reports)=>Path.Combine(reports,"..","privateReports","controller-capture-lease.json");
 private sealed class JournalGuard:IDisposable
 {
  private readonly Mutex mutex;private bool held;
  internal JournalGuard(string file){string name=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(file).ToUpperInvariant())))[..24];mutex=new(false,@"Local\ONEXTweaks.ControllerJournal.v1."+name);try{held=mutex.WaitOne(3000);}catch(AbandonedMutexException){held=true;}if(!held){mutex.Dispose();throw new IOException("Controller recovery journal is busy");}}
  public void Dispose(){if(held){held=false;mutex.ReleaseMutex();}mutex.Dispose();}
 }
 private static void Save(string file,ControllerLeaseData data)
 {
  using var guard=new JournalGuard(file);Directory.CreateDirectory(Path.GetDirectoryName(file)!);string temp=file+".tmp-"+Guid.NewGuid().ToString("N");
  try{File.WriteAllText(temp,JsonSerializer.Serialize(data,Backend.Program.Json));for(int n=0;;n++){try{File.Move(temp,file,true);return;}catch(Exception e)when(n<12&&e is IOException or UnauthorizedAccessException&&(e.HResult is unchecked((int)0x80070005) or unchecked((int)0x80070020) or unchecked((int)0x80070021))){Thread.Sleep(15);}}}
  finally{if(File.Exists(temp))try{File.Delete(temp);}catch{}}
 }
 private static ControllerLeaseData? Load(string file){using var guard=new JournalGuard(file);if(!File.Exists(file))return null;using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);return JsonSerializer.Deserialize<ControllerLeaseData>(stream,Backend.Program.Json);}
 internal static string[] OwnSourceIds(string reports){
  var value=Load(LeaseFile(reports));if(value==null||value.Pid!=Environment.ProcessId||value.StartedUtcTicks!=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks||value.Recovering||value.PhysicalRestored||value.HidePending||!Guid.TryParseExact(value.Token,"N",out _)||!value.AddedDevices.All(ValidDevice))return [];return value.AddedDevices.ToArray();
 }
 private static bool ValidDevice(string id)=>id.Length<512&&new[]{@"USB\VID_045E&PID_028E\",@"USB\VID_045E&PID_028E&",@"HID\VID_045E&PID_028E\",@"HID\VID_045E&PID_028E&"}.Any(prefix=>id.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))&&!id.Contains('"')&&!id.Contains('\0');
 private static bool ValidApplication(string image)=>BridgeInstallationLayout.IsRecordedLeaseImage(image,
  ControllerHidHide.ImagePath(BridgeInstallationLayout.InstalledImage),
  ControllerHidHide.ImagePath(BridgeInstallationLayout.DevelopmentProject)+@"\");
 internal static void ValidateConfiguration(ControllerHideState state,string[] source)
 {
  if(state.Inverse)throw new InvalidOperationException("HidHide is configured in inverse mode. Existing configuration is protected.");
  if(!state.Active&&state.Devices.Length!=0)throw new InvalidOperationException("Existing inactive HidHide device rules are protected from automatic activation.");
  if(source.Intersect(state.Devices,StringComparer.OrdinalIgnoreCase).Any())throw new InvalidOperationException("Another input tool already hides this controller");
 }
 internal static async Task<ControllerCaptureLease> Acquire(string reports,ControllerSource source)
 {
  RequireActiveSession();
  string image=Environment.ProcessPath??throw new IOException("The remapper image is unknown");
  if(!BridgeInstallationLayout.IsCaptureImage(image))throw new IOException("Use the installed remapper executable");
  string file=LeaseFile(reports);await RecoverStale(file);if(File.Exists(file))throw new IOException("A controller capture lease is already active");
  if(source.InstanceIds.Length is <2 or >8||!source.InstanceIds.All(ValidDevice)||!ValidDevice(source.UsbInstance))throw new InvalidDataException("Only the identified ONEX Xbox gamepad interfaces may be hidden");
  using var hide=new ControllerHidHide();var before=hide.Read();ValidateConfiguration(before,source.InstanceIds);
  string ntImage=ControllerHidHide.ImagePath(image);string[] addedApps=before.Applications.Contains(ntImage,StringComparer.OrdinalIgnoreCase)?[]:[ntImage];
  var data=new ControllerLeaseData(Guid.NewGuid().ToString("N"),Environment.ProcessId,Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,DateTimeOffset.UtcNow,before.Active,source.InstanceIds,addedApps,HidePending:true);
  var lease=new ControllerCaptureLease(file,data);
  using(var journal=new JournalGuard(file)){RequireActiveSession();if(File.Exists(file))throw new IOException("A controller capture lease is already active");Save(file,data);}
  try
  {
   var start=new ProcessStartInfo(image){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--controller-watchdog");start.ArgumentList.Add(file);start.ArgumentList.Add(data.Token);
   using var watchdog=Process.Start(start)??throw new IOException("Controller recovery monitor did not start");
   using(var journal=new JournalGuard(file))
   {
    data=lease.RequirePending() with{WatchdogPid=watchdog.Id,WatchdogStartedUtcTicks=watchdog.StartTime.ToUniversalTime().Ticks,Heartbeat=DateTimeOffset.UtcNow};
    lease.data=data;Save(file,data);
   }
   string ready=file+".ready-"+data.Token;for(int n=0;n<80&&!File.Exists(ready);n++){if(watchdog.HasExited)throw new IOException("Controller recovery monitor exited");await Task.Delay(25);}if(!File.Exists(ready))throw new IOException("Controller recovery monitor was not confirmed");File.Delete(ready);
   lease.CommitHide(
    () =>
    {
     var current=hide.Read();
     if(current.Active!=before.Active||current.Inverse!=before.Inverse||
        !current.Devices.SequenceEqual(before.Devices,StringComparer.OrdinalIgnoreCase)||
        !current.Applications.SequenceEqual(before.Applications,StringComparer.OrdinalIgnoreCase))
      throw new IOException("HidHide configuration changed before controller capture");
    },
    () =>
    {
     hide.Applications(before.Applications.Concat(addedApps));
     hide.Devices(before.Devices.Concat(data.AddedDevices));
     hide.Active(true);
     var after=hide.Read();
     if(!after.Active||after.Inverse||data.AddedDevices.Except(after.Devices,StringComparer.OrdinalIgnoreCase).Any()||
        !after.Applications.Contains(ntImage,StringComparer.OrdinalIgnoreCase))
      throw new IOException("Controller cloaking was not confirmed");
    });
   return lease;
  }
  catch{hide.Dispose();lease.Dispose();throw;}
 }
 private ControllerLeaseData RequirePending()
 {
  var current=Load(file);
  if(current==null||current.Token!=data.Token||current.Recovering||!current.HidePending)
   throw new IOException("Controller capture reservation expired");
  return current;
 }
 private void CommitHide(Action verifyConfiguration,Action hide)
 {
  using var journal=new JournalGuard(file);
  RequireActiveSession();
  var current=RequirePending();
  verifyConfiguration();
  // Arm cleanup before the first driver write. The watchdog and a resumed
  // acquisition cannot apply changes on opposite sides of journal deletion.
  data=current with{HidePending=false,Heartbeat=DateTimeOffset.UtcNow};
  Save(file,data);
  hide();
  Touch();
 }
 internal static async Task VerifyHideCommit(string file)
 {
  var reservation=new ControllerLeaseData(Guid.NewGuid().ToString("N"),Environment.ProcessId,
   Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,DateTimeOffset.UtcNow.AddSeconds(-10),false,[],[],HidePending:true);
  var lease=new ControllerCaptureLease(file,reservation);
  int writes=0;
  void Apply()=>writes++;
  void ExpectRejected()
  {
   try{lease.CommitHide(()=>{},Apply);}catch(IOException){return;}
   throw new IOException("A reclaimed controller reservation could still hide the source");
  }
  Save(file,reservation);
  await Cleanup(file,reservation.Token,requireStale:true);
  if(File.Exists(file))throw new IOException("Watchdog did not reclaim the stale reservation");
  ExpectRejected();
  Save(file,reservation with{Token=Guid.NewGuid().ToString("N")});
  ExpectRejected();
  Save(file,reservation with{Recovering=true});
  ExpectRejected();
  if(writes!=0)throw new IOException("A reclaimed controller reservation wrote to HidHide");
  Save(file,reservation);
  try{lease.CommitHide(()=>throw new InvalidOperationException("Changed driver snapshot"),Apply);}
  catch(InvalidOperationException){}
  if(writes!=0||Load(file)?.HidePending!=true)throw new IOException("Changed HidHide settings armed cleanup");
  lease.CommitHide(()=>{},() =>
  {
   if(Load(file)?.HidePending!=false)throw new IOException("HidHide writes ran before cleanup was armed");
   Apply();
  });
  if(writes!=1||Load(file)?.Heartbeat<=reservation.Heartbeat)throw new IOException("A valid capture did not commit");
  bool rejectedDuplicate=false;
  try{lease.CommitHide(()=>{},Apply);}catch(IOException){rejectedDuplicate=true;}
  if(!rejectedDuplicate||writes!=1)throw new IOException("A controller reservation was committed twice");
 }
 internal void Touch(){using var guard=new JournalGuard(file);if(released)throw new ObjectDisposedException(nameof(ControllerCaptureLease));var current=Load(file);if(current?.Token!=data.Token||current.Recovering)throw new IOException("Controller recovery lease was lost");data=data with{Heartbeat=DateTimeOffset.UtcNow};Save(file,data);}
 internal void Input(int[] keys,string[] mouse){using var guard=new JournalGuard(file);if(released)throw new ObjectDisposedException(nameof(ControllerCaptureLease));var current=Load(file);if(current?.Token!=data.Token||current.Recovering)throw new IOException("Controller input recovery already started");data=data with{HeldKeys=keys,HeldMouse=mouse};Save(file,data);}
 private static bool OwnerAlive(ControllerLeaseData value){try{using var p=Process.GetProcessById(value.Pid);return !p.HasExited&&p.StartTime.ToUniversalTime().Ticks==value.StartedUtcTicks;}catch{return false;}}
 internal static async Task RecoverStale(string file){var value=Load(file);if(value==null)return;if(!value.Recovering&&OwnerAlive(value)&&(DateTimeOffset.UtcNow-value.Heartbeat).TotalSeconds<5)throw new IOException("The existing controller capture is still active");await Cleanup(file,value.Token,requireStale:true);}
 private static void RequireActiveSession()
 {
  if(Volatile.Read(ref sessionEnding)!=0)throw new IOException("Windows is ending the session.");
 }
 internal static void PrepareForSessionEnd(string file)
 {
  // Keep new capture out even if no lease exists yet. The journal serializes this release with CommitHide.
  if(Interlocked.Exchange(ref sessionEnding,1)!=0)return;
  using var guard=new JournalGuard(file);
  var current=Load(file);
  if(current==null||current.Pid!=Environment.ProcessId||!OwnerAlive(current))return;
  // One synchronous attempt. A remaining recovery journal is handled by the existing watchdog/logon task.
  CleanupOnce(file,current.Token,requireStale:false,inputReady:null);
 }
 private static async Task Cleanup(string file,string token,bool requireStale=false,Func<bool>? inputReady=null)
 {
  Exception? failure=null;
  for(int attempt=0;attempt<30;attempt++)
  {
   try{CleanupOnce(file,token,requireStale,inputReady);return;}
   catch(Exception error){failure=error;await Task.Delay(80);}
  }
  throw new IOException("Controller recovery could not complete",failure);
 }
 private static void CleanupOnce(string file,string token,bool requireStale,Func<bool>? inputReady)
 {
  using var guard=new JournalGuard(file);var value=Load(file);if(value==null||value.Token!=token)return;if(requireStale&&!value.Recovering&&OwnerAlive(value)&&(DateTimeOffset.UtcNow-value.Heartbeat).TotalSeconds<4)return;if(!Guid.TryParseExact(token,"N",out _)||!value.AddedDevices.All(ValidDevice)||value.AddedApplications.Length>1||!value.AddedApplications.All(ValidApplication))throw new InvalidDataException("Controller recovery identity is invalid");
  value=value with{Recovering=true};Save(file,value);Exception? inputFailure=null;
  try{if(((value.HeldKeys?.Length??0)>0||(value.HeldMouse?.Length??0)>0)&&!(inputReady?.Invoke()??ControllerInputDesktop.Available()))throw new IOException("Owned input release is pending until the normal Windows desktop returns");ControllerInputEmitter.ReleaseRecorded(value.HeldKeys,value.HeldMouse);value=value with{HeldKeys=[],HeldMouse=[]};Save(file,value);}catch(Exception error){inputFailure=error;}
  // Unhide the physical controller even while Windows blocks key-up on its
  // secure desktop. Keep only our input journal for retry after unlock.
  if(!value.PhysicalRestored&&!value.HidePending){using var hide=new ControllerHidHide();var fresh=hide.Read();string[] devices=fresh.Devices.Except(value.AddedDevices,StringComparer.OrdinalIgnoreCase).ToArray();string[] apps=fresh.Applications.Except(value.AddedApplications,StringComparer.OrdinalIgnoreCase).ToArray();hide.Devices(devices);hide.Applications(apps);if(!value.WasActive&&devices.Length==0)hide.Active(false);var actual=hide.Read();if(value.AddedDevices.Intersect(actual.Devices,StringComparer.OrdinalIgnoreCase).Any())throw new IOException("Physical controller recovery was not confirmed");value=value with{PhysicalRestored=true};Save(file,value);}
  if(inputFailure!=null)throw inputFailure;if(Load(file)?.Token==token)File.Delete(file);
 }
 internal static async Task<int> VerifySessionEnd(string report)
 {
  string dir=Path.Combine(Path.GetTempPath(),"onex-controller-session-end-"+Guid.NewGuid().ToString("N"));
  string file=Path.Combine(dir,"lease.json");
  Directory.CreateDirectory(dir);
  try
  {
   var data=new ControllerLeaseData(Guid.NewGuid().ToString("N"),Environment.ProcessId,
    Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,DateTimeOffset.UtcNow,false,[],[],HidePending:true);
   Save(file,data);
   var pending=new ControllerCaptureLease(file,data);
   PrepareForSessionEnd(file);
   if(File.Exists(file))throw new IOException("An owned pending reservation survived session end.");
   bool writes=false,rejected=false;
   try{pending.CommitHide(()=>{},()=>writes=true);}catch(IOException e)when(e.Message=="Windows is ending the session."){rejected=true;}
   if(!rejected||writes)throw new IOException("Capture could commit after session end.");
   bool acquisitionRejected=false;
   try{await Acquire(dir,null!);}catch(IOException e)when(e.Message=="Windows is ending the session."){acquisitionRejected=true;}
   if(!acquisitionRejected)throw new IOException("New capture could start after session end.");
   PrepareForSessionEnd(file);
   await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed=true,ownedReservationReleased=true,blockedInFlightCommit=true,blockedNewCapture=true,repeatSafe=true,driverWrites=0},Backend.Program.Json));
   return 0;
  }
  finally{Directory.Delete(dir,true);}
 }
 internal static async Task<int> Watch(string file,string token)
 {
  string expected=Path.GetFullPath(LeaseFile(BridgeInstallationLayout.Reports));if(!Path.GetFullPath(file).Equals(expected,StringComparison.OrdinalIgnoreCase))return 2;
  var value=Load(file);if(value?.Token!=token)return 2;string ready=file+".ready-"+token,temp=ready+".tmp";await File.WriteAllTextAsync(temp,token);File.Move(temp,ready,true);
  while(true){await Task.Delay(200);value=Load(file);if(value==null||value.Token!=token)return 0;if(value.Recovering||!OwnerAlive(value)||(DateTimeOffset.UtcNow-value.Heartbeat).TotalSeconds>4){try{await Cleanup(file,token,requireStale:true);if(Load(file)?.Token!=token)return 0;}catch{await Task.Delay(1000);}}}
 }
 internal static async Task<string> RecoverAfterLogon(string reports){string path=LeaseFile(reports);var value=Load(path);if(value==null)return "no-lease";if(!value.Recovering&&OwnerAlive(value)&&(DateTimeOffset.UtcNow-value.Heartbeat).TotalSeconds<5)return "active-capture-protected";await Cleanup(path,value.Token,requireStale:true);return File.Exists(path)?"active-capture-protected":"stale-input-restored";}
 internal static async Task RecoverStopped(string reports,Func<bool>? inputReady=null){string path=LeaseFile(reports);var value=Load(path);if(value==null)return;if(value.Pid!=Environment.ProcessId&&OwnerAlive(value))throw new IOException("A different controller capture still runs");await Cleanup(path,value.Token,inputReady:inputReady);}
 public void Dispose(){if(released)return;released=true;Cleanup(file,data.Token).GetAwaiter().GetResult();}
}
