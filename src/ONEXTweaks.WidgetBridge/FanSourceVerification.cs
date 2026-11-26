// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class FanSourceVerification
{
 internal static async Task<int> Run(string report)
 {
  string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"qa-fan-sources-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"Reports"));var checks=new List<string>();string? error=null;
  FanPreferences live=new(0);bool ac=true,failApply=false;int applies=0;string context="owner";FanProfiles? manager=null;
  FanProfiles Open(string? linked)=>new(Path.Combine(root,"Reports"),new(1,1),new(()=>[]),()=>live,(value,_)=>{applies++;if(failApply)throw new IOException("Injected fan readback failure");live=value;},_=>{},linked,()=>context,startWorker:false,onAc:()=>ac,currentSession:()=>null);
  void Check(bool ok,string name){if(!ok)throw new IOException(name);checks.Add(name);}
  void Change(object request,FanParentContext? parent=null){using var json=JsonDocument.Parse(JsonSerializer.Serialize(request,Backend.Program.Json));manager!.Mutate(json.RootElement,parent:parent,bindParent:(_,_)=>{});}
  JsonElement Entry(string id){using var json=JsonDocument.Parse(manager!.Read());return json.RootElement.GetProperty("entries").EnumerateArray().Single(entry=>entry.GetProperty("id").GetString()==id).Clone();}
  bool Reject(object request){try{Change(request);return false;}catch(Exception ex)when(ex is InvalidDataException or InvalidOperationException or IOException){return true;}}
  try
  {
   manager=Open(null);var parent=new FanParentContext("qa-parent","QA Fan Source Parent",new(0),false);Change(new{operation="create",performanceProfileId=parent.Id},parent);string own=manager.EditorId;int start=applies;
   Change(new{operation="split",id=own,enabled=true});Check(Entry(own).GetProperty("split").GetBoolean()&&applies==start,"Inactive profile split never applies hardware");
   var acCurve=new FanPreferences(3,new(40,50,60,70,80));var dcCurve=new FanPreferences(3,new(30,40,50,60,70));
   FanPreferences Values(string source)=>Entry(own).GetProperty(source).Deserialize<FanPreferences>(Backend.Program.Json)!;
   Change(new{operation="values",id=own,onAc=true,source="ac",values=acCurve});Change(new{operation="values",id=own,onAc=true,source="dc",values=new FanPreferences(0)});
   Check(Values("ac")==acCurve&&Values("dc").Mode==0&&applies==start,"Explicit AC/DC writes are independent and inactive profile autosaves stay offline");
   context=parent.Id;manager.SetPerformanceOverride(own,false);Check(live==acCurve,"Active split profile applies AC values on simulated AC");start=applies;
   Change(new{operation="values",id=own,onAc=true,source="dc",values=dcCurve});Check(live==acCurve&&applies==start,"Inactive source edit on active profile never applies hardware");
   Change(new{operation="values",id=own,onAc=true,source="ac",values=new FanPreferences(0)});Check(live.Mode==0&&applies==start+1,"Live source edit applies immediately");
   Change(new{operation="split",id=own,enabled=false});Check(live==dcCurve&&Values("ac").Mode==0,"Disabling split uses DC and retains hidden AC");
   Change(new{operation="values",id=own,onAc=true,source="dc",values=dcCurve});Check(live==dcCurve&&Values("ac").Mode==0,"Unified edits on AC update DC without overwriting saved AC");
   Check(Reject(new{operation="values",id=own,onAc=true,source="ac",values=new FanPreferences(0)}),"Hidden AC editor rejects edits while split is off");
   await manager.DisposeAsync();manager=Open(own);manager.SetPerformanceOverride(own,false);Check(live==dcCurve&&Values("ac").Mode==0,"Hidden unequal sides survive catalog reload");
   Change(new{operation="split",id=own,enabled=true});Check(live.Mode==0,"Re-enabling split restores the previous AC values");ac=false;manager.SetPerformanceOverride(own,false);Check(live==dcCurve,"Simulated source switch applies DC values");
   start=applies;failApply=true;
   Change(new{operation="split",id=own,enabled=false});
   Change(new{operation="split",id=own,enabled=true});
   Check(Entry(own).GetProperty("split").GetBoolean()&&applies==start,"Splitting on DC exposes AC even when the fan writer is unavailable");
   ac=true;start=applies;
   bool savedOnFailure=false;
   try{Change(new{operation="split",id=own,enabled=false});}
   catch(FanProfileApplyException){savedOnFailure=true;}
   Check(savedOnFailure&&!Entry(own).GetProperty("split").GetBoolean()&&Values("dc")==dcCurve&&Values("ac").Mode==0&&applies==start+1,
       "If the effective curve changes and fails, retain the split selection and both curves");
   failApply=false;ac=false;
   string saved=File.ReadAllText(Path.Combine(root,"fan-profiles.json"));start=applies;
   Check(Reject(new{operation="values",id=own,onAc=true,source="dc",values=new FanPreferences(0)})&&File.ReadAllText(Path.Combine(root,"fan-profiles.json"))==saved&&applies==start,"Stale power-source context cannot write or apply");
   Check(Reject(new{operation="values",id=own,onAc=false,source="other",values=new FanPreferences(0)})&&applies==start,"Unknown source cannot write or apply");
  }
  catch(Exception ex){error=ex.ToString();}
  finally{if(manager!=null)await manager.DisposeAsync();}
  await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed=error==null,checks,error,modelOnly=true,hardwareTested=false,physicalPowerSourceTested=false},Backend.Program.Json));return error==null?0:1;
 }
}
