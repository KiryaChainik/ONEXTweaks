// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ProfileLinkVerification
{
    internal static async Task<int> Run(string report)
    {
        string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"qa-links-"+Guid.NewGuid().ToString("N"));string folder=Path.Combine(root,"Reports"),file=Path.Combine(root,"performance-profiles.json"),childFile=Path.Combine(root,"child.json");Directory.CreateDirectory(folder);
        var checks=new List<string>();string? error=null;PerformanceProfiles? manager=null;int writes=0,captures=0;bool ac=true;
        var value=new ProfileValues(12,20,0,0,0,1,new("RTSS",false,60),0,0,false);var live=value;
        void Check(bool ok,string label){if(!ok)throw new IOException(label);checks.Add(label);}
        ProfileDocument Store()=>JsonSerializer.Deserialize<ProfileDocument>(File.ReadAllText(file),Backend.Program.Json)!;
        Task<PerformanceProfiles> Open()=>PerformanceProfiles.Create(folder,()=>{captures++;return Task.FromResult(live);},desired=>{writes++;live=desired;return Task.CompletedTask;},()=>null,()=>ac,new(()=>null,_=>false,()=>[]),startWorker:false);
        async Task Select(string id){using var change=JsonDocument.Parse(JsonSerializer.Serialize(new{operation="select",id},Backend.Program.Json));await manager!.Mutate(change.RootElement);}
        try
        {
            var flags=PerformanceProfiles.Groups.ToDictionary(group=>group,_=>true);flags["Fan"]=flags["ScreenProfile"]=false;
            var global=new ProfileEntry("global","global","",false,false,value,value,DateTimeOffset.UnixEpoch);
            var first=new ProfileEntry("first","First","",false,true,value with{Pl1=15},value with{Pl1=9},DateTimeOffset.UnixEpoch,true);
            var other=new ProfileEntry("other","Other","",false,false,value,value,DateTimeOffset.UnixEpoch,false);
            File.WriteAllText(file,JsonSerializer.Serialize(new ProfileDocument(5,global,[first,other],flags,new("first","manual",null)),Backend.Program.Json));
            manager=await Open();int originalCaptures=captures;manager.BindPreset("Fan","fan-a");manager.BindPreset("ScreenProfile","screen-a");var doc=Store();var current=doc.Games.Single(entry=>entry.Id=="first");
            Check(current.Ac.FanProfile=="fan-a"&&current.Ac.ScreenProfile=="screen-a","Child selection binds both references to the active Performance AC side");
            Check(current.Dc.FanProfile==null&&current.Dc.ScreenProfile==null&&doc.Global==global,"Binding leaves DC and Global values intact");
            Check(writes==0&&captures==originalCaptures,"Reverse binding neither recaptures nor writes hardware settings");
            var resolved=ProfileResolution.Resolve(doc,current,true);Check(resolved.FanProfile=="fan-a"&&resolved.ScreenProfile=="screen-a","Explicit links are honored despite legacy shared save flags");
            var before=current.Ac;ac=false;await manager.RefreshPowerSource();manager.BindPreset("Fan","fan-dc");manager.BindPreset("ScreenProfile","screen-dc");current=Store().Games.Single(entry=>entry.Id=="first");
            Check(current.Ac==before&&current.Dc.FanProfile=="fan-dc"&&current.Dc.ScreenProfile=="screen-dc","DC selection updates only DC references");
            await Select("other");manager.BindPreset("Fan","fan-b");manager.BindPreset("ScreenProfile","screen-b");doc=Store();current=doc.Games.Single(entry=>entry.Id=="other");
            Check(current.Ac.FanProfile==null&&current.Dc.FanProfile=="fan-b"&&current.Ac.ScreenProfile==null&&current.Dc.ScreenProfile=="screen-b","Unsplit Performance reference edits use DC and retain archived AC");
            Check(doc.Games.Single(entry=>entry.Id=="first").Ac.FanProfile=="fan-a","Switching active Performance profile does not relink another profile");
            string saved=File.ReadAllText(file);Directory.CreateDirectory(file+".tmp");bool rejected=false;try{manager.BindPreset("Fan","should-fail");}catch(Exception e)when(e is IOException or UnauthorizedAccessException){rejected=true;}
            Check(rejected&&File.ReadAllText(file)==saved,"Failed parent persistence preserves its original durable references");
            FanPreferences fanLive=new(0);var catalog=new ScopedProfileCatalog<FanPreferences>(childFile,()=>fanLive,next=>fanLive=next,next=>next.Validate(),()=>true,()=>null,_=>false);
            void Change(object request,Action<string>? follower=null){using var payload=JsonDocument.Parse(JsonSerializer.Serialize(request,Backend.Program.Json));catalog.Mutate(payload.RootElement,_=>null,follower);}
            Change(new{operation="create",name="Child"});string child=catalog.ActiveId;fanLive=new(1);catalog.Record();Change(new{operation="select",id="global"});string childBefore=File.ReadAllText(childFile);rejected=false;
            try{Change(new{operation="select",id=child},id=>manager.BindPreset("Fan",id));}catch(Exception e)when(e is IOException or UnauthorizedAccessException){rejected=true;}
            Check(rejected&&catalog.ActiveId=="global"&&fanLive.Mode==0&&File.ReadAllText(childFile)==childBefore&&File.ReadAllText(file)==saved,"Failed reverse binding rolls back child selection, native value and catalog as one action");
            Directory.Delete(file+".tmp");Change(new{operation="select",id=child},id=>manager.BindPreset("Fan",id));
            Check(catalog.ActiveId==child&&fanLive.Mode==1&&Store().Games.Single(entry=>entry.Id=="other").Dc.FanProfile==child,"Successful reverse selection persists both catalogs consistently");
            manager.BindPreset("Fan","global","fan-a");doc=Store();
            Check(doc.Games.Single(entry=>entry.Id=="first").Ac.FanProfile=="global"&&doc.Games.Single(entry=>entry.Id=="first").Dc.FanProfile=="fan-dc","Deleted preset clears only matching references on other profiles");
            await manager.DisposeAsync();manager=await Open();Check(manager.ActiveFanProfile=="global"&&manager.ActiveScreenProfile=="screen-b","Selected references survive a manager reload");
        }
        catch(Exception e){error=e.ToString();}
        finally{if(manager!=null)await manager.DisposeAsync();Directory.Delete(root,true);}
        await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed=error==null,checks,error,modelOnly=true,hardwareTested=false},Backend.Program.Json));return error==null?0:1;
    }
}
