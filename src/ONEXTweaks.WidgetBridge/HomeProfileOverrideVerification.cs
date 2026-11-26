// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class HomeProfileOverrideVerification
{
 internal static async Task<int> Run(string report)
 {
  string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"qa-home-profile-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"Reports"));string file=Path.Combine(root,"performance-profiles.json");var checks=new List<string>();string? error=null;PerformanceProfiles? manager=null;
  var globalDc=new ProfileValues(11,12,0,0,0,1,new("Intel",false,60),0,0,false,new(){Mode=new(1920,1200,144)},FanProfile:"global");var globalAc=globalDc with{Pl1=18,Pl2=19,PowerMode=2};var raw=globalDc with{Pl1=13,Pl2=14,PowerMode=0};var own=new ProfileEntry("own","Own QA","",false,true,raw with{Pl1=15,Pl2=16},raw,DateTimeOffset.UnixEpoch,AcInitialized:true);var other=own with{Id="other",Name="Other QA"};bool ac=false;var live=globalDc;int applies=0;
  void Check(bool ok,string text){if(!ok)throw new IOException(text);checks.Add(text);}
  string Data(object value)=>JsonSerializer.Serialize(value,Backend.Program.Json);
  ProfileDocument Store()=>JsonSerializer.Deserialize<ProfileDocument>(File.ReadAllText(file),Backend.Program.Json)!;
  ProfileEntry Own()=>Store().Games.Single(entry=>entry.Id=="own");
  Task<PerformanceProfiles> Open()=>PerformanceProfiles.Create(Path.Combine(root,"Reports"),()=>Task.FromResult(live),value=>{applies++;live=value;return Task.CompletedTask;},()=>null,()=>ac,new(()=>null,_=>false,()=>[]),startWorker:false);
  async Task Change(object value){using var doc=JsonDocument.Parse(Data(value));await manager!.Mutate(doc.RootElement);}
  try
  {
   var initial=new ProfileDocument(6,new("global","global","",false,true,globalAc,globalDc,DateTimeOffset.UnixEpoch,AcInitialized:true),[own,other],PerformanceProfiles.Groups.ToDictionary(key=>key,_=>false),new("own","manual"));File.WriteAllText(file,Data(initial));manager=await Open();
   Check(Store().Schema==7&&Store().Global==initial.Global&&Data(Store().Games)==Data(initial.Games)&&Data(Store().Flags)==Data(initial.Flags),"Schema6 migration preserves all existing settings and source archives");
   var originalGlobal=Data(Store().Global);var originalOther=Data(Store().Games[1]);var originalAc=ProfileResolution.Resolve(Store(),Own(),true);int count=applies;
   live=live with{Pl1=12,Pl2=13};await manager.Record("TDP",preferActive:true);var saved=Store();
   Check(Own().Dc.Pl1==12&&ProfileResolution.HasLocalGroup(Own(),"TDP")&&Data(saved.Global)==originalGlobal&&Data(saved.Games[1])==originalOther,"Home TDP writes only the selected profile, never Global or other profiles");
   Check(ProfileResolution.Resolve(saved,Own(),false).Pl1==12&&ProfileResolution.Resolve(saved,Own(),true).Pl1==originalAc.Pl1,"Only the edited source changes; inactive effective TDP is preserved");
   Check(ProfileResolution.Resolve(saved,Own(),false).PowerMode==globalDc.PowerMode&&Own().LocalGroups!.SequenceEqual(new[]{"TDP"})&&Own().UseSharedPolicy,"Unrelated groups retain Global inheritance");
   Check(applies==count,"Recording acknowledged settings does not reapply hardware");
   live=live with{Pl1=14,Pl2=15};await manager.Record("TDP");Check(Own().Dc.Pl1==14&&Data(Store().Global)==originalGlobal,"Later edits keep an existing local group in the selected profile");
   live=live with{PowerMode=2};await manager.Record("OSPowerMode");Check(Store().Global.Dc.PowerMode==2&&!ProfileResolution.HasLocalGroup(Own(),"OSPowerMode"),"Legacy inherited writes retain their explicit shared-policy behavior");originalGlobal=Data(Store().Global);
   live=live with{PowerMode=0};await manager.Record("OSPowerMode",preferActive:true);Check(ProfileResolution.Resolve(Store(),Own(),false).PowerMode==0&&Data(Store().Global)==originalGlobal,"Home Power Mode locally overrides only its own group");
   live=live with{Boost=4,Scheduling=2};await manager.Record("CPUBoost",preferActive:true);Check(ProfileResolution.Resolve(Store(),Own(),false).Boost==4&&ProfileResolution.HasLocalGroup(Own(),"CPUBoost"),"Home CPU settings persist in the selected profile");
   live=live with{Fps=new("RTSS",true,45)};await manager.Record("FPSLimit",preferActive:true);Check(ProfileResolution.Resolve(Store(),Own(),false).Fps==live.Fps,"Home FPS provider and value persist together");
   live=live with{Overlay=3};await manager.Record("OverlayLevel",preferActive:true);Check(ProfileResolution.Resolve(Store(),Own(),false).Overlay==3,"Home overlay level persists in the selected profile");
   live=live with{Display=live.Display! with{FrameGeneration=2}};await manager.Record("Display",preferActive:true);Check(ProfileResolution.Resolve(Store(),Own(),false).Display!.FrameGeneration==2,"Explicit graphics settings locally override their group");
   live=live with{Display=live.Display! with{Mode=new(1280,800,144)}};await manager.Record("Resolution",preferActive:true);live=live with{Display=live.Display! with{Mode=new(1280,800,100)}};await manager.Record("RefreshRate",preferActive:true);Check(ProfileResolution.Resolve(Store(),Own(),false).Display!.Mode==new DisplayModeValue(1280,800,100),"Resolution and refresh rate override independently");
   manager.BindPreset("Fan",FanPreferences.AutoId);Check(Own().Dc.FanProfile==FanPreferences.AutoId&&Data(Store().Global)==originalGlobal&&Data(Store().Games[1])==originalOther,"Fan selection updates only the selected Performance binding");
   await Change(new{operation="source",id="own",source="ac",key="pl1",value=19});Check(Own().Ac.Pl1==19&&live.Pl1==14,"Performance source editor accepts a local override without touching inactive hardware");
   Check(Own().Ac.ScreenProfile==null&&Own().Dc.ScreenProfile==null,"Screen color remains independent from Performance");
   string snapshot=Data(Store());await manager.DisposeAsync();manager=null;manager=await Open();Check(Data(Store())==snapshot&&ProfileResolution.HasLocalGroup(Own(),"TDP"),"Local overrides and AC/DC values survive manager reload");
   await Change(new{operation="select",id="other"});await Change(new{operation="select",id="own"});Check(live.Pl1==14&&live.PowerMode==0&&live.Fps==new FpsPreferences("RTSS",true,45),"Reselecting profile reapplies saved local settings");
   var bad=Store() with{Games=Store().Games.Select(entry=>entry.Id=="own"?entry with{LocalGroups=new[]{"unknown"}}:entry).ToList()};await manager.DisposeAsync();manager=null;File.WriteAllText(file,Data(bad));bool rejected=false;try{manager=await Open();}catch(InvalidDataException){rejected=true;}Check(rejected,"Invalid local group identities are rejected");
  }
  catch(Exception failure){error=failure.ToString();}
  finally{if(manager!=null)await manager.DisposeAsync();}
  await File.WriteAllTextAsync(report,Data(new{passed=error==null,checks,error,modelOnly=true,hardwareTested=false,physicalPowerSourceTested=false}));return error==null?0:1;
 }
}
