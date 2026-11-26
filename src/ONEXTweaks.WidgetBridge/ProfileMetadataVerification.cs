// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ProfileMetadataVerification
{
    private sealed record Value(int Number);
    internal static async Task<int> Run(string report)
    {
        string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"qa-metadata-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"Reports"));
        var checks=new List<string>();string? error=null;PerformanceProfiles? manager=null;
        void Check(bool ok,string name){if(!ok)throw new IOException(name);checks.Add(name);}
        try
        {
            var values=new ProfileValues(11,18,0,0,0,1,new("RTSS",false,60),0,0,false,FanProfile:"global");
            string file=Path.Combine(root,"performance-profiles.json");var data=new ProfileDocument(5,new("global","global","",false,false,values,values,DateTimeOffset.UnixEpoch),[],PerformanceProfiles.Groups.ToDictionary(key=>key,_=>true),new());File.WriteAllText(file,JsonSerializer.Serialize(data,Backend.Program.Json));
            int captures=0,applies=0;var live=values;
            Task<PerformanceProfiles> Open()=>PerformanceProfiles.Create(Path.Combine(root,"Reports"),()=>{captures++;return Task.FromResult(live);},value=>{applies++;live=value;return Task.CompletedTask;},()=>null,()=>true,new(()=>null,_=>false,()=>[]),startWorker:false);
            manager=await Open();
            async Task Change(object value){using var doc=JsonDocument.Parse(JsonSerializer.Serialize(value,Backend.Program.Json));await manager.Mutate(doc.RootElement);}
            string Active(){using var state=JsonDocument.Parse(manager.Read());return state.RootElement.GetProperty("activeId").GetString()!;}
            ProfileDocument Store()=>JsonSerializer.Deserialize<ProfileDocument>(File.ReadAllText(file),Backend.Program.Json)!;
            await Change(new{operation="create",name="QA A"});string first=Active();var before=Store().Games.Single(entry=>entry.Id==first);int cc=captures,aa=applies;
            await Change(new{operation="rename",id=first,name="  QA renamed <&>  "});var after=Store().Games.Single(entry=>entry.Id==first);
            Check(after.Name=="QA renamed <&>"&&after.Id==first&&after.Ac==before.Ac&&after.Dc==before.Dc&&Active()==first,"Rename trims name and preserves ID, settings, links and selection");
            Check(captures==cc&&applies==aa,"Performance rename neither captures nor applies hardware");
            string saved=File.ReadAllText(file);bool rejected=false;try{await Change(new{operation="rename",id=first,name=" \n "});}catch(InvalidDataException){rejected=true;}
            Check(rejected&&saved==File.ReadAllText(file),"Blank/invalid rename does not change the file");
            rejected=false;try{await Change(new{operation="rename",id="global",name="QA Global"});}catch(InvalidOperationException){rejected=true;}Check(rejected&&saved==File.ReadAllText(file),"Global rename is rejected");
            await Change(new{operation="create",name="QA B"});string second=Active();cc=captures;aa=applies;
            await Change(new{operation="rename",id=first,name="QA inactive"});Check(Active()==second&&captures==cc&&applies==aa,"Renaming inactive profile leaves active selection and hardware untouched");
            saved=File.ReadAllText(file);rejected=false;try{await Change(new{operation="rename",id=first,name="qa b"});}catch(InvalidOperationException){rejected=true;}Check(rejected&&saved==File.ReadAllText(file),"Case-insensitive duplicate rename is rejected");
            await Change(new{operation="delete",id=first});Check(Active()==second&&Store().Games.All(entry=>entry.Id!=first)&&captures==cc&&applies==aa,"Deleting inactive profile does not switch active scope or touch hardware");
            saved=File.ReadAllText(file);Directory.CreateDirectory(file+".tmp");rejected=false;
            try{await Change(new{operation="rename",id=second,name="QA failed save"});}catch(Exception failure)when(failure is IOException or UnauthorizedAccessException){rejected=true;}Directory.Delete(file+".tmp");
            Check(rejected&&File.ReadAllText(file)==saved&&captures==cc&&applies==aa,"Failed metadata persistence rolls back without hardware operations");
            await manager.DisposeAsync();manager=null;manager=await Open();Check(Active()==second&&Store().Games.Single().Name=="QA B","Names and selection survive manager reload");
            string scopedFile=Path.Combine(root,"scoped.json");Value scopedLive=new(5);int scopedApplies=0,follow=0;
            var catalog=new ScopedProfileCatalog<Value>(scopedFile,()=>scopedLive,value=>{scopedApplies++;scopedLive=value;},_=>{},()=>true,()=>null,_=>false,sourceIndependent:true);
            void Scoped(object value){using var doc=JsonDocument.Parse(JsonSerializer.Serialize(value,Backend.Program.Json));catalog.Mutate(doc.RootElement,_=>null,_=>follow++);}
            Scoped(new{operation="create",name="QA screen"});string id=catalog.ActiveId;int oldFollow=follow,oldApplies=scopedApplies;
            Scoped(new{operation="rename",id,name="QA screen renamed"});
            Check(catalog.ActiveId==id&&catalog.CurrentValues==scopedLive&&follow==oldFollow&&scopedApplies==oldApplies,"Scoped rename preserves selection/values and never triggers parent-link follower");
            using var state=JsonDocument.Parse(catalog.Read());Check(state.RootElement.GetProperty("entries").EnumerateArray().Single(entry=>entry.GetProperty("id").GetString()==id).GetProperty("name").GetString()=="QA screen renamed","Scoped rename is published to picker state");
        }
        catch(Exception failure){error=failure.ToString();}
        finally{if(manager!=null)await manager.DisposeAsync();}
        await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed=error==null,checks,error,modelOnly=true,hardwareTested=false},Backend.Program.Json));return error==null?0:1;
    }
}
