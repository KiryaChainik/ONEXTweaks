// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class OverlayPresetVerification
{
 internal static async Task<int> Run(string report)
 {
  string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"qa-overlay-presets-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string file=Path.Combine(root,"overlay-presets.json");var live=new OverlayPreferences(0,"TextSize:175;Font:Bahnschrift|400;Opacity:90;L3:FPS,CPU,GPU;L4:FPS,Memory");var original=live;int applies=0;bool fail=false;var checks=new List<string>();string? error=null;OverlayPresetCatalog? catalog=null;
  void Check(bool ok,string name){if(!ok)throw new IOException(name);checks.Add(name);}
  OverlayPresetDocument Store()=>JsonSerializer.Deserialize<OverlayPresetDocument>(File.ReadAllText(file),Backend.Program.Json)!;
  string Data(object value)=>JsonSerializer.Serialize(value,Backend.Program.Json);
  OverlayPresetCatalog Open()=>new(file,()=>live,value=>{if(fail){fail=false;throw new IOException("Injected RTSS apply failure");}applies++;live=value;});
  void Change(object value){using var doc=JsonDocument.Parse(Data(value));catalog!.Mutate(doc.RootElement);}
  try
  {
   catalog=Open();Check(live==original&&applies==0,"First catalog creation preserves legacy overlay settings without applying hardware");Check(Store().Presets.Count==5&&Store().Presets.Skip(1).Select(entry=>entry.Name).SequenceEqual(OverlayPresetCatalog.StandardNames),"Four standard English layout names are exact");Check(Store().Presets.All(entry=>entry.Values.Config==original.Config),"Existing customized layout/configuration is preserved in initial snapshots");
   Change(new{operation="create",name="QA Preset",activate=false});string own=Store().Presets.Single(entry=>entry.Name=="QA Preset").Id;Check(catalog.ActiveId=="global"&&applies==0,"Inactive create adds a preset without applying or changing selected preset");
   Change(new{operation="rename",id=own,name="QA Renamed"});Check(Store().Presets.Single(entry=>entry.Id==own).Name=="QA Renamed"&&live==original,"Rename keeps values and actual overlay unchanged");
   Change(new{operation="select",id=own});Check(catalog.ActiveId==own&&live==original,"Selecting a saved preset applies its complete captured settings");
   live=new(3,"TextSize:200;Opacity:80;L3:FPS,CPU;L3_Order:CPU,FPS");catalog.Record();var saved=Store().Presets.Single(entry=>entry.Id==own).Values;Check(saved==live&&Store().Presets.Single(entry=>entry.Id=="global").Values==original,"Style/layout/metrics edits save only into active preset");
   Change(new{operation="select",id="firmware-overlay-2"});Check(live.Level==2&&live.Config==original.Config,"Standard Horizontal preset restores its saved layout/style");Change(new{operation="select",id=own});Check(live==saved,"Manual preset restores custom metrics order and opacity");
   catalog=Open();Check(catalog.ActiveId==own&&Store().Presets.Single(entry=>entry.Id==own).Values==saved,"Preset choice and full configuration survive reload");
   string before=File.ReadAllText(file);int count=applies;bool rejected=false;try{catalog.EnsureActive("old-preset");}catch(InvalidOperationException){rejected=true;}Check(rejected&&File.ReadAllText(file)==before&&applies==count,"Stale preset edit is rejected without touching settings");
   fail=true;rejected=false;try{Change(new{operation="select",id="global"});}catch(IOException){rejected=true;}Check(rejected&&File.ReadAllText(file)==before&&catalog.ActiveId==own&&live==saved,"Failed RTSS apply leaves selection and stored values intact");
   Change(new{operation="keep",id=own});Check(File.ReadAllText(file)==before,"Keep It does not delete or alter a preset");
   foreach(string id in new[]{"global","firmware-overlay-1"}){rejected=false;try{Change(new{operation="delete",id});}catch(InvalidOperationException){rejected=true;}Check(rejected,"Default/standard presets are protected: "+id);}
   rejected=false;try{Change(new{operation="rename",id=own,name="Basic"});}catch(InvalidDataException){rejected=true;}Check(rejected,"Duplicate preset names are rejected");
   Change(new{operation="select",id="global"});count=applies;Change(new{operation="delete",id=own});Check(Store().Presets.All(entry=>entry.Id!=own)&&applies==count&&live==original,"Inactive deletion does not apply RTSS or change other presets");
   Change(new{operation="create",name="QA Active"});string active=catalog.ActiveId;Check(active!="global"&&live==original&&applies==count,"User create selects captured current settings without redundant hardware apply");Change(new{operation="delete",id=active});Check(catalog.ActiveId=="global"&&live==original,"Active deletion restores Default Preset safely");
   string valid=File.ReadAllText(file);var bad=Store();bad=bad with{Presets=bad.Presets.Select(entry=>entry.Id=="global"?entry with{Values=new(9,"bad")}:entry).ToList()};File.WriteAllText(file,Data(bad));rejected=false;try{catalog=Open();}catch(InvalidDataException){rejected=true;}Check(rejected&&File.ReadAllText(file)==Data(bad),"Corrupt preset data is rejected and retained, never reset silently");File.WriteAllText(file,valid);
  }
  catch(Exception failure){error=failure.ToString();}
  await File.WriteAllTextAsync(report,Data(new{passed=error==null,checks,error,modelOnly=true,hardwareTested=false,actualRtssApplyTested=false}));return error==null?0:1;
 }
}
