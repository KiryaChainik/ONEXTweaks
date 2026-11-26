// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal sealed record OverlayPresetEntry(string Id,string Name,bool BuiltIn,OverlayPreferences Values,DateTimeOffset ModifiedUtc);
internal sealed record OverlayPresetDocument(int Schema,string ActiveId,List<OverlayPresetEntry> Presets);
internal sealed class OverlayPresetCatalog
{
 private readonly string file;private readonly Func<OverlayPreferences> capture;private readonly Action<OverlayPreferences> apply;private OverlayPresetDocument document;
 internal static readonly string[] StandardNames={"Basic","Horizontal","Horizontal Custom","Vertical Custom"};
 internal OverlayPresetCatalog(string file,Func<OverlayPreferences> capture,Action<OverlayPreferences> apply)
 {
  this.file=file;this.capture=capture;this.apply=apply;
  if(File.Exists(file))document=JsonSerializer.Deserialize<OverlayPresetDocument>(File.ReadAllText(file),Backend.Program.Json)??throw new InvalidDataException("Empty overlay preset store");
  else{var initial=capture();RtssOverlay.ValidatePreset(initial);var entries=new List<OverlayPresetEntry>{new("global","Default Preset",true,initial,DateTimeOffset.UtcNow)};for(int i=0;i<4;i++)entries.Add(new("firmware-overlay-"+(i+1),StandardNames[i],true,initial with{Level=i+1},DateTimeOffset.UtcNow));document=new(1,"global",entries);Save();}
  Validate(document);
 }
 internal string ActiveId=>document.ActiveId;
 internal void EnsureActive(string expected){if(expected!=ActiveId)throw new InvalidOperationException("Overlay preset changed; retry the adjustment");}
 private static void Validate(OverlayPresetDocument value)
 {
  if(value.Schema!=1||value.Presets.Count is <1 or >128||value.Presets.Count(entry=>entry.Id=="global")!=1||value.Presets.Select(entry=>entry.Id).Distinct(StringComparer.Ordinal).Count()!=value.Presets.Count||!value.Presets.Any(entry=>entry.Id==value.ActiveId))throw new InvalidDataException("Invalid overlay preset store");
  foreach(var entry in value.Presets){if(entry.Id.Length is <1 or >64||entry.Id.Any(char.IsControl)||entry.Name.Length is <1 or >160||entry.Name.Any(char.IsControl))throw new InvalidDataException("Invalid overlay preset identity");RtssOverlay.ValidatePreset(entry.Values);}
 }
 private void Save(){Validate(document);string temp=file+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(document,Backend.Program.Json));File.Move(temp,file,true);}
 internal string Read()=>JsonSerializer.Serialize(new{presetCatalog=true,sourceIndependent=true,activeId=ActiveId,appliedId=ActiveId,onAc=false,entries=document.Presets.Select(entry=>new{entry.Id,entry.Name,entry.BuiltIn,entry.ModifiedUtc,split=false,dc=entry.Values,ac=entry.Values}),candidates=Array.Empty<object>()},Backend.Program.Json);
 internal void Record()
 {
  var values=capture();RtssOverlay.ValidatePreset(values);var entry=document.Presets.Single(entry=>entry.Id==ActiveId);if(entry.Values==values)return;var before=document;try{document=document with{Presets=document.Presets.Select(item=>item.Id==ActiveId?item with{Values=values,ModifiedUtc=DateTimeOffset.UtcNow}:item).ToList()};Save();}catch{document=before;throw;}
 }
 internal void Mutate(JsonElement request)
 {
  string operation=request.GetProperty("operation").GetString()??"";string id=request.TryGetProperty("id",out var identity)?identity.GetString()??"":ActiveId;var entry=document.Presets.FirstOrDefault(entry=>entry.Id==id);var before=document;OverlayPreferences? actual=null;bool applied=false;
  try
  {
   switch(operation)
   {
    case "create":
     string name=(request.TryGetProperty("name",out var title)?title.GetString():null)?.Trim()??"";if(name.Length==0){int n=1;while(document.Presets.Any(item=>item.Name.Equals("Preset "+n,StringComparison.OrdinalIgnoreCase)))n++;name="Preset "+n;}
     if(name.Length>160||name.Any(char.IsControl)||document.Presets.Any(item=>item.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Use a unique preset name");var clone=capture();RtssOverlay.ValidatePreset(clone);var created=new OverlayPresetEntry(Guid.NewGuid().ToString("N"),name,false,clone,DateTimeOffset.UtcNow);bool activate=!request.TryGetProperty("activate",out var active)||active.GetBoolean();document=document with{ActiveId=activate?created.Id:ActiveId,Presets=document.Presets.Append(created).ToList()};break;
    case "select":if(entry==null)throw new InvalidDataException("Preset no longer exists");actual=capture();apply(entry.Values);applied=true;document=document with{ActiveId=entry.Id};break;
    case "rename":
     if(entry==null||entry.BuiltIn)throw new InvalidOperationException("Standard presets cannot be renamed");string renamed=(request.GetProperty("name").GetString()??"").Trim();if(renamed.Length is <1 or >160||renamed.Any(char.IsControl)||document.Presets.Any(item=>item.Id!=id&&item.Name.Equals(renamed,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Use a unique non-empty preset name");document=document with{Presets=document.Presets.Select(item=>item.Id==id?item with{Name=renamed,ModifiedUtc=DateTimeOffset.UtcNow}:item).ToList()};break;
    case "delete":
     if(entry==null||entry.BuiltIn)throw new InvalidOperationException("Standard presets cannot be deleted");if(id==ActiveId){actual=capture();apply(document.Presets.Single(item=>item.Id=="global").Values);applied=true;}document=document with{ActiveId=id==ActiveId?"global":ActiveId,Presets=document.Presets.Where(item=>item.Id!=id).ToList()};break;
    case "keep":return;
    default:throw new InvalidDataException("Unknown preset operation");
   }
   Save();
  }
  catch{document=before;if(applied&&actual!=null)apply(actual);throw;}
 }
}
