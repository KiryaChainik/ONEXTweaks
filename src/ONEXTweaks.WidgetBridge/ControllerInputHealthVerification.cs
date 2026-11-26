// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerInputHealthVerification
{
 internal static async Task<int> Models(string file){
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
  var hardware=new ControllerRuntimeState(false,false,0,-1,null,[],0);var xbox=hardware with{Running=true,VirtualSlot=1,ObserverVirtualSlot=0};var ds4=xbox with{OutputTarget="DualShock4",VirtualSlot=-1,ObserverVirtualSlot=-1};
  ControllerInputHealthState E(ControllerRuntimeState state,int count=1,int[]? visible=null,bool sony=false,bool hidden=true,bool active=true,bool inverse=false,string? error=null)=>ControllerInputHealth.Evaluate(state,count,visible??[0],sony,hidden,active,inverse,error);
  Check(E(hardware).Issues.Length==0&&E(hardware).GamePlayer==1,"Healthy physical path");
  Check(E(xbox).Issues.Length==0&&E(xbox).GamePlayer==1,"Game player number must come from independent view, not whitelisted slot2");
  Check(E(ds4,visible:[],sony:true).Issues.Length==0&&E(ds4,visible:[],sony:true).GamePlayer==0,"DS4 must not claim an XInput player slot");
  Check(E(xbox,visible:[]).Issues.Contains("outputMissing"),"Missing Xbox output");
  Check(E(xbox,visible:[0,1]).Issues.Contains("duplicateXboxInput"),"Duplicate virtual/physical input");
  Check(E(ds4,visible:[],sony:false).Issues.Contains("sonyOutputMissing"),"Missing owned Sony HID");
  Check(E(ds4,sony:true).Issues.Contains("xboxAlongsideSony"),"Extra Xbox alongside captured DS4");
  Check(E(xbox,hidden:false).Issues.Contains("sourceNotHidden"),"Capture without physical hiding");
  Check(E(hardware,visible:[]).Issues.Contains("hardwareHidden"),"Physical controller hidden in Hardware mode");
  Check(E(hardware,visible:[0,1]).Issues.Contains("extraVisibleInput"),"Additional visible controllers");
  Check(E(hardware,count:0).Issues.Contains("sourceMissing")&&E(hardware,count:2).Issues.Contains("extraPhysicalSource"),"Supported physical source count");
  Check(E(hardware,inverse:true).Issues.Contains("inverseHideMode"),"Protected inverse HidHide configuration");
  var unavailable=E(hardware,error:"read unavailable");Check(unavailable.Issues.SequenceEqual(new[]{"unavailable"})&&unavailable.VisibleXboxCount==-1&&unavailable.GamePlayer==0,"Failed read must not invent conflicts or player numbers");
  Check(!ControllerInputHealth.Snapshot(E(xbox),xbox)!.Stale,"Fresh unchanged snapshot");
  Check(ControllerInputHealth.Snapshot(E(xbox),ds4)!.Stale,"Mode change must expire prior result");
  Check(ControllerInputHealth.Snapshot(E(xbox) with{CheckedUtc=DateTimeOffset.UtcNow.AddMinutes(-2)},xbox)!.Stale,"Old result must be labelled stale");
  Check(ControllerInputHealth.Snapshot(null,xbox)==null,"No check must remain unknown");
  var physical=new ControllerSource("physical",["physical","physical-hid"]);var virtualPad=new ControllerSource("virtual",["virtual","virtual-hid"]);
  Check(ControllerInputHealth.CapturedSources([physical,virtualPad],["physical","physical-hid"]).SequenceEqual(new[]{physical}),"Own virtual Xbox was counted as a physical source");
  Check(ControllerInputHealth.CapturedSources([physical,virtualPad],[]).Length==0,"Missing capture identity guessed a physical source");
  Check(ControllerInputHealth.CapturedSources([physical,virtualPad,new("foreign",["foreign-hid"])],["physical-hid"]).SequenceEqual(new[]{physical}),"A foreign endpoint entered the current capture identity");
  await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,driverChanges=0},Backend.Program.Json));return 0;
 }
 internal static async Task<int> Native(string file){
  ControllerInputHealthState? lastHealth=null;int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
  string reports=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ONEXTweaks","dev","Reports"),ownerPath=Path.Combine(reports,"..","controller-profiles.json");byte[] owner=await File.ReadAllBytesAsync(ownerPath);
  using(var json=JsonDocument.Parse(owner))Check(!json.RootElement.GetProperty("enabled").GetBoolean(),"Protect owner active capture");
  Check(!File.Exists(ControllerCaptureLease.LeaseFile(reports))&&ControllerNative.Slots().SequenceEqual(new[]{0})&&ControllerNative.Read(0,out var input)&&ControllerNative.Neutral(input),"Protect additional/active input");
  ControllerHideState Hide(){using var h=new ControllerHidHide();return h.Read();}bool Same(ControllerHideState a,ControllerHideState b)=>a.Active==b.Active&&a.Inverse==b.Inverse&&a.Devices.Order().SequenceEqual(b.Devices.Order())&&a.Applications.Order().SequenceEqual(b.Applications.Order());var initial=Hide();
  try{await using(var capture=new ControllerRemapper(reports)){
   var before=Hide();var hardware=await ControllerInputHealth.Check(reports,capture);lastHealth=hardware;Check(hardware.Error==null&&hardware.Issues.Length==0&&hardware.Mode=="Hardware"&&hardware.VisibleXboxCount==1&&hardware.PhysicalSourceCount==1,"Read-only actual Hardware path failed");Check(Same(before,Hide()),"Hardware health check changed HidHide");
   foreach(string target in new[]{"Xbox360","DualShock4"}){
    capture.Policy(new ControllerPolicy(VirtualTarget:target));await capture.Start();await Task.Delay(120);before=Hide();var health=await ControllerInputHealth.Check(reports,capture);lastHealth=health;Check(health.Error==null&&!health.Stale&&health.Issues.Length==0&&health.Mode==target&&health.PhysicalHidden,"Actual captured health path failed");
    if(target=="Xbox360")Check(health.VisibleXboxCount==1&&health.GamePlayer==capture.Read().ObserverVirtualSlot+1,"Actual game player differs from independent slot");
    else Check(health.VisibleXboxCount==0&&health.OwnedSonyPresent&&health.GamePlayer==0,"Actual DS4 HID identity/visibility failed");
    Check(Same(before,Hide())&&capture.Read().Running,"Health check changed capture/hiding");
    await capture.Stop();Check((await ControllerRemapper.Observe(reports)).SequenceEqual(new[]{0})&&!File.Exists(ControllerCaptureLease.LeaseFile(reports)),"Physical source not restored");
   }
  }
  Check(Same(initial,Hide()),"Original HidHide configuration changed");byte[] after=await File.ReadAllBytesAsync(ownerPath);Check(owner.SequenceEqual(after),"Health test changed owner profiles");
  await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,actualReadOnlyHealthModes=new[]{"Hardware","Xbox360","DualShock4"},actualCaptureStarts=2,actualIndependentObserver=true,actualOwnedSonyReadOnlyNonce=true,ownerProfileChanges=0,physicalOwnerInputTested=false,driverChanges=0},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,lastHealth,error=e.GetType().Name+": "+e.Message,ownerPhysicalInputTested=false},Backend.Program.Json));return 2;}
 }
}
