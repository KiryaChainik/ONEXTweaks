// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class FanEditorVerification
{
    internal static async Task<int> Run(string report)
    {
        string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"qa-fan-editor-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"Reports"));
        string file=Path.Combine(root,"fan-profiles.json");var checks=new List<string>();string? error=null;FanPreferences live=new(0);int applies=0;bool ac=true,fail=false;string context="owner";var links=new Dictionary<string,string>();
        void Check(bool ok,string name){if(!ok)throw new IOException(name);checks.Add(name);}
        var manager=new FanProfiles(Path.Combine(root,"Reports"),new(1,1),new(()=>[]),()=>live,(value,_)=>{applies++;if(fail){fail=false;throw new IOException("Injected apply failure");}live=value;},_=>{},null,()=>context,startWorker:false,onAc:()=>ac,currentSession:()=>null);
        void Change(object request,FanParentContext? parent=null,bool failBind=false)
        {
            using var json=JsonDocument.Parse(JsonSerializer.Serialize(request,Backend.Program.Json));
            manager.Mutate(json.RootElement,id=>links[context]=id,parent,(id,fan)=>{if(failBind)throw new IOException("Injected parent write failure");links[id]=fan;},id=>{foreach(var key in links.Where(pair=>pair.Value==id).Select(pair=>pair.Key).ToArray())links.Remove(key);});
        }
        bool Reject(Action action){try{action();return false;}catch(Exception failure) when(failure is InvalidOperationException or InvalidDataException or IOException){return true;}}
        try
        {
            var parent=new FanParentContext("inactive","QA Parent",new(0),false);int before=applies;
            Change(new{operation="create",performanceProfileId=parent.Id},parent);string own=manager.EditorId;
            Check(manager.ActiveId=="global"&&own!="global"&&live.Mode==0&&applies==before,"Inactive parent creation does not select Performance or apply hardware");
            Check(links[parent.Id]==own&&!links.ContainsKey("owner"),"Creation binds only the explicitly chosen parent");
            using(var stored=JsonDocument.Parse(File.ReadAllText(file)))Check(stored.RootElement.GetProperty("profiles").EnumerateArray().Single(entry=>entry.GetProperty("id").GetString()==own).GetProperty("performanceProfileId").GetString()==parent.Id,"Origin Performance identity is retained");
            var curve=new FanCurve(20,40,60,80,90);Change(new{operation="values",id=own,onAc=true,values=new FanPreferences(3,curve)});
            Check(manager.ValuesFor(own)==new FanPreferences(3,curve)&&applies==before&&live.Mode==0,"Inactive custom curve autosaves without applying");
            using(var read=JsonDocument.Parse(manager.Read()))Check(read.RootElement.GetProperty("activeId").GetString()==own&&read.RootElement.GetProperty("appliedId").GetString()=="global"&&read.RootElement.GetProperty("editingInactiveProfile").GetBoolean(),"Editor selection is separate from actual fan output");
            Change(new{operation="rename",id=own,name="QA Renamed"});Check(applies==before&&manager.EditorId==own,"Renaming inactive editor preserves actual fan and selection");
            context="inactive";manager.SetPerformanceOverride(own,false);Check(manager.ActiveId==own&&manager.EditorId==own&&live==new FanPreferences(3,curve),"Activating parent applies its saved custom curve and selects matching Fan editor");
            Change(new{operation="select",id="firmware-0"});Check(manager.ActiveId=="firmware-0"&&live.Mode==0&&links[context]=="firmware-0","Explicit manual relink replaces parent binding");
            manager.SetPerformanceOverride("firmware-2",false);Check(manager.ActiveId=="firmware-0"&&links[context]=="firmware-0","Original creation metadata never overwrites a later relink");
            Change(new{operation="create",name="QA Manual"});string manual=manager.ActiveId;Check(manual.StartsWith("firmware-",StringComparison.Ordinal)==false&&links[context]==manual,"Manual creation follows shared selection and binding workflow");
            Change(new{operation="select",id="firmware-0",temporary=true});Check(manager.ActiveId=="firmware-0"&&live.Mode==0&&links[context]==manual,"Tile override changes output without overwriting saved Performance link");
            manager.SetPerformanceOverride(manual,false);Check(manager.ActiveId=="firmware-0"&&live.Mode==0,"Other Performance setting changes preserve a held Fan override");
            context="restarted-session";manager.SetPerformanceOverride(manual,false);Check(manager.ActiveId==manual&&live.Mode==0,"Restarted process/context restores the saved Fan binding");
            string saved=File.ReadAllText(file);before=applies;
            Check(Reject(()=>Change(new{operation="create",processId=123,startedUtcTicks="1"}))&&File.ReadAllText(file)==saved&&applies==before,"Process creation is rejected without native process lookup");
            Check(Reject(()=>Change(new{operation="select",id="firmware-1"}))&&File.ReadAllText(file)==saved&&applies==before,"Retired Fan preset cannot be selected");
            Check(Reject(()=>Change(new{operation="values",id="firmware-0",values=new FanPreferences(3,curve)}))&&File.ReadAllText(file)==saved,"Firmware presets are immutable");
            Check(Reject(()=>Change(new{operation="values",id=manual,onAc=false,values=new FanPreferences(3,curve)}))&&File.ReadAllText(file)==saved,"Source change guard rejects stale Fan edits");
            Check(Reject(()=>Change(new{operation="create",performanceProfileId="another"},new("another","QA Failure",new(0),false),true))&&File.ReadAllText(file)==saved&&applies==before,"Failed parent binding rolls detached catalog creation back without applying");
            fail=true;before=applies;
            Check(Reject(()=>Change(new{operation="values",id=manual,onAc=true,values=new FanPreferences(3,curve)}))
                &&manager.ValuesFor(manual)==new FanPreferences(3,curve)&&live.Mode==0&&applies==before+1,"Rejected hardware write keeps custom curve, reports failure and doesn't retry");
            var reloaded=new FanProfiles(Path.Combine(root,"Reports"),new(1,1),new(()=>[]),()=>live,(_,_)=>throw new IOException("Unexpected apply"),_=>{},null,()=>context,startWorker:false,onAc:()=>ac,currentSession:()=>null);
            try{Check(reloaded.ValuesFor(manual)==new FanPreferences(3,curve),"Rejected custom curve survives reopening the profile catalog");}
            finally{await reloaded.DisposeAsync();}
            saved=File.ReadAllText(file);before=applies;
            Check(Reject(()=>Change(new{operation="values",id=manual,onAc=true,values=new FanPreferences(3,new FanCurve(60,20,60,80,90))}))
                &&File.ReadAllText(file)==saved&&applies==before,"Invalid curve is neither stored nor applied");
            before=applies;Change(new{operation="delete",id=own});Check(!manager.Exists(own)&&manager.ActiveId==manual&&live.Mode==0&&applies==before,"Deleting inactive Fan profile preserves current output and selection");
            Change(new{operation="values",id=manual,onAc=true,values=new FanPreferences(3,curve)});Check(live.Mode==3&&manager.ValuesFor(manual).Curve==curve,"Active custom autosave reaches applied hardware model");
            Change(new{operation="values",id=manual,onAc=true,values=new FanPreferences(0,curve)});Check(manager.ValuesFor(manual).Curve==curve&&live.Mode==0,"Turning custom off retains the editable curve");
            var longParent=new FanParentContext("long-parent",new string('x',160),new(0),false);before=applies;Change(new{operation="create",performanceProfileId=longParent.Id},longParent);using(var read=JsonDocument.Parse(manager.Read()))Check(read.RootElement.GetProperty("entries").EnumerateArray().Single(entry=>entry.GetProperty("id").GetString()==manager.EditorId).GetProperty("name").GetString()!.Length<=160&&applies==before,"Long Performance names create bounded Fan names without hardware changes");
        }
        catch(Exception failure){error=failure.ToString();}
        finally{await manager.DisposeAsync();}
        await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed=error==null,checks,error,modelOnly=true,hardwareTested=false,physicalOwnerInputTested=false},Backend.Program.Json));return error==null?0:1;
    }
}
