// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using System.Text.Json.Nodes;
namespace ONEXTweaks.WidgetBridge;

// Own ONEX fan profiles; custom duty curves use the verified OEM channel.
internal sealed class FanProfiles:IAsyncDisposable
{
    private readonly ScopedProfileCatalog<FanPreferences> catalog;
    private readonly SemaphoreSlim gate;
    private readonly Action<FanPreferences,bool> apply;
    private readonly Action<Exception> log;
    private readonly Func<FanPreferences> capture;
    private readonly Func<JsonNode?> observe;
    private readonly Action<FanPreferences>? restore;
    private readonly FanConnectionRecovery recovery=new();
    private readonly Func<string> performanceContext;
    private readonly Func<bool> onAc;
    private readonly CancellationTokenSource stopping=new();
    private readonly Task worker;
    private string? performanceOverride,signature;
    private string? editorId,editorContext,temporaryId,temporaryContext;
    internal FanProfiles(string folder,SemaphoreSlim gate,ProfileSessions sessions,Func<FanPreferences> capture,Action<FanPreferences,bool> apply,Action<Exception> log,string? initialOverride,Func<string>? performanceContext=null,bool startWorker=true,Func<bool>? onAc=null,Func<ProfileSession?>? currentSession=null,Func<JsonNode?>? observe=null,Action<FanPreferences>? restore=null,bool independent=false)
    {
        this.gate=gate;this.apply=apply;this.log=log;this.capture=()=>capture().WithSupportedMode();this.observe=observe??(()=>null);this.restore=restore;this.performanceContext=performanceContext??(()=>"");this.onAc=onAc??WindowsPowerSettings.IsOnAc;performanceOverride=FanPreferences.SupportedId(initialOverride);
        string file=Path.Combine(folder,"..","fan-profiles.json");
        var globalValue=this.capture();var global=new ScopedProfile<FanPreferences>("global","Global Profile",null,false,globalValue,globalValue,DateTimeOffset.UtcNow);
        string legacy=Path.Combine(folder,"..","performance-profiles.json");
        ProfileDocument? old=null;
        if(File.Exists(legacy)&&(independent||!File.Exists(file)))
        {
            old=JsonSerializer.Deserialize<ProfileDocument>(File.ReadAllText(legacy),Backend.Program.Json)??throw new InvalidDataException("Performance profiles are unreadable");
            if(!File.Exists(file)&&!independent)
                global=global with{Split=old.Global.Split,Ac=new(FanPreferences.SupportedMode(old.Global.Ac.Fan)),Dc=new(FanPreferences.SupportedMode(old.Global.Dc.Fan))};
        }
        var auto=new ScopedProfile<FanPreferences>(FanPreferences.AutoId,"Auto",null,false,new(0),new(0),DateTimeOffset.UtcNow);
        catalog=new(file,this.capture,value=>{if(performanceOverride==null)apply(value,false);},value=>value.Validate(),this.onAc,currentSession??sessions.Current,ProfileSessions.Alive,global,[auto],rememberManualFallback:true,retainSourceSides:true,migrateLoaded:data=>
        {
            var supported=MigrateRetiredPresets(data,auto);
            return independent&&old!=null?MigrateLegacyGameLinks(supported,old):supported;
        });
        worker=startWorker?Task.Run(Run):Task.CompletedTask;
    }
    internal static ScopedProfileDocument<FanPreferences> MigrateLegacyGameLinks(ScopedProfileDocument<FanPreferences> fan,ProfileDocument legacy)
    {
        if(fan.FanLinksMigrated)return fan;
        var entries=fan.Profiles.ToList();
        ScopedProfile<FanPreferences>? Preset(string? id)=>
            id=="global"?fan.Global:fan.Profiles.FirstOrDefault(entry=>entry.Id==FanPreferences.SupportedId(id));
        var bound=entries.Where(entry=>entry.ProcessPath!=null).Select(entry=>entry.ProcessPath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var parent in legacy.Games.Where(entry=>entry.Use&&entry.Path.Length>0))
        {
            if(bound.Contains(parent.Path))continue;
            var ac=Preset(parent.Split?parent.Ac.FanProfile:parent.Dc.FanProfile);
            var dc=Preset(parent.Dc.FanProfile);
            if(ac==null&&dc==null)continue;
            var owned=entries.FirstOrDefault(entry=>entry.PerformanceProfileId==parent.Id&&entry.ProcessPath==null);
            if(owned!=null&&ac?.Id==owned.Id&&dc?.Id==owned.Id)
            {
                entries[entries.IndexOf(owned)]=owned with{ProcessPath=parent.Path,PerformanceProfileId=null};
            }
            else if(entries.Count<512)
            {
                var acValues=(ac??fan.Global).Split?(ac??fan.Global).Ac:(ac??fan.Global).Dc;
                var dcValues=(dc??fan.Global).Dc;
                string basis=parent.Name[..Math.Min(parent.Name.Length,150)];
                string name=basis;
                for(int i=2;entries.Any(entry=>entry.Name.Equals(name,StringComparison.OrdinalIgnoreCase));i++)name=basis+" ("+i+")";
                entries.Add(new(Guid.NewGuid().ToString("N"),name,parent.Path,acValues!=dcValues,acValues,dcValues,DateTimeOffset.UtcNow,AcInitialized:acValues!=dcValues));
            }
            else continue;
            bound.Add(parent.Path);
        }
        return fan with{Profiles=entries,FanLinksMigrated=true};
    }
    private static ScopedProfileDocument<FanPreferences> MigrateRetiredPresets(
        ScopedProfileDocument<FanPreferences> data, ScopedProfile<FanPreferences> auto)
    {
        static ScopedProfile<FanPreferences> Update(ScopedProfile<FanPreferences> entry) => entry with
        {
            Ac = entry.Ac.WithSupportedMode(),
            Dc = entry.Dc.WithSupportedMode(),
            Unified = entry.Unified?.WithSupportedMode()
        };

        var global = Update(data.Global);
        var profiles = data.Profiles.Where(entry => !FanPreferences.IsRetiredPreset(entry.Id)).Select(Update).ToList();
        if (!profiles.Any(entry => entry.Id == FanPreferences.AutoId)) profiles.Insert(0, auto);
        string active = FanPreferences.SupportedId(data.ActiveId)!;
        string? lastManual = FanPreferences.SupportedId(data.LastManualId);
        if (global == data.Global && profiles.SequenceEqual(data.Profiles) &&
            active == data.ActiveId && lastManual == data.LastManualId) return data;
        return data with { Global = global, Profiles = profiles, ActiveId = active, LastManualId = lastManual };
    }
    internal bool Exists(string id)=>catalog.Find(id)!=null;
    internal FanPreferences ValuesFor(string id)=>catalog.ValuesFor(id);
    internal string EditorId=>editorId??ActiveId;
    internal string? PerformanceOverride=>performanceOverride;
    internal static string ResolveId(string natural,bool manualHold,bool naturalApp,string? linked,Func<string,bool> exists)=>linked!=null&&exists(linked)?linked:linked!=null?"global":natural;
    internal string ActiveId=>temporaryId!=null&&temporaryContext==performanceContext()&&Exists(temporaryId)?temporaryId:
        catalog.Active.ProcessPath!=null?catalog.ActiveId:ResolveId(catalog.ActiveId,catalog.HasManualHold,false,performanceOverride,Exists);
    private string Signature()=>ActiveId+":"+onAc()+":"+catalog.Find(ActiveId)!.ModifiedUtc.UtcTicks;
    internal string Read(string? performanceProfiles=null)
    {
        ClearStaleEditor();var value=JsonNode.Parse(catalog.Read())!.AsObject();value["activeId"]=EditorId;value["appliedId"]=ActiveId;value["naturalActiveId"]=catalog.ActiveId;value["editingInactiveProfile"]=EditorId!=ActiveId;value["linkedPerformanceProfile"]=performanceOverride!=null;if(performanceOverride!=null)value["manualOverride"]=temporaryId!=null;value["missingPerformanceLink"]=performanceOverride!=null&&catalog.Find(performanceOverride)==null;if(performanceProfiles!=null){var parents=JsonNode.Parse(performanceProfiles)?["entries"]?.AsArray();value["creationParents"]=new JsonArray(parents?.Select(entry=>(JsonNode?)new JsonObject{["id"]=entry!["id"]!.GetValue<string>(),["name"]=entry["id"]!.GetValue<string>()=="global"?"Global Profile":entry["name"]!.GetValue<string>()}).ToArray()??[]);}
        return value.ToJsonString(Backend.Program.Json);
    }
    internal void CreateForProcess(ProfileSession process)
    {
        var existing=catalog.FindProcess(process.Path);
        string fanId=existing?.Id??catalog.CreateDetached(process.Name[..Math.Min(process.Name.Length,160)],capture(),null,processPath:process.Path);
        editorId=fanId==ActiveId?null:fanId;editorContext=editorId==null?null:Context();
    }
    private string Context()=>performanceContext()+":"+onAc();
    internal void ClearTemporary(){temporaryId=null;temporaryContext=null;}
    internal async Task ExplicitPerformanceChange(Func<Task> change)
    {
        string? previous=temporaryId,context=temporaryContext;ClearTemporary();
        try{await change();}
        catch(Exception failure)
        {
            try{temporaryId=previous;temporaryContext=context;ClearStaleEditor();apply(catalog.ValuesFor(ActiveId),false);signature=Signature();}
            catch(Exception recovery){throw new AggregateException("Performance selection failed and Fan recovery failed",failure,recovery);}
            throw;
        }
    }
    private void ClearStaleEditor(){if(temporaryId!=null&&(temporaryContext!=performanceContext()||!Exists(temporaryId)))ClearTemporary();if(editorId!=null&&(editorContext!=Context()||catalog.Find(editorId)==null)){editorId=null;editorContext=null;}}
    internal void SetPerformanceOverride(string? id,bool powerGateOwned)
    {id=FanPreferences.SupportedId(id);catalog.Tick(applyValues:false);ClearStaleEditor();if(id!=performanceOverride){editorId=null;editorContext=null;}performanceOverride=id;if(id!=null&&catalog.Find(id)==null)log(new InvalidDataException("Linked fan preset was removed; Global is used"));apply(catalog.ValuesFor(ActiveId),powerGateOwned);signature=Signature();}
    internal void Record(){editorId=null;editorContext=null;var before=catalog.ValuesFor(ActiveId);var value=capture();if(value.Curve==null)value=value with{Curve=before.Curve};catalog.StoreValues(ActiveId,value);signature=Signature();}
    internal void Mutate(JsonElement request,Action<string>? followSelection=null,FanParentContext? parent=null,Action<string,string>? bindParent=null,Action<string>? unlink=null)
    {
        ClearStaleEditor();string operation=request.GetProperty("operation").GetString()??"";
        string id=request.TryGetProperty("id",out var identity)?identity.GetString()??"":EditorId;
        if(operation=="select"&&FanPreferences.IsRetiredPreset(id))throw new InvalidDataException("Fan profile no longer exists");
        if(request.TryGetProperty("processId",out _))throw new InvalidOperationException("Create Fan profiles from an existing Performance profile");
        if(operation is "values" or "rename" or "delete" or "split"&&id.StartsWith("firmware-",StringComparison.Ordinal))throw new InvalidOperationException("System Fan presets are read-only; create a Manual Profile");
        if(operation=="resume")
        {ClearTemporary();editorId=null;editorContext=null;apply(catalog.ValuesFor(ActiveId),false);signature=Signature();return;}
        if(operation=="select"&&request.TryGetProperty("temporary",out var temporary)&&temporary.ValueKind==JsonValueKind.True)
        {
            var desired=catalog.ValuesFor(id);var before=capture();
            try{apply(desired,false);catalog.SelectWithoutApply(id);}catch(Exception failure)
            {
                try{apply(before,false);}
                catch(Exception recovery){throw new AggregateException("Temporary Fan selection failed and recovery failed",failure,recovery);}
                throw;
            }
            temporaryId=id;temporaryContext=performanceContext();editorId=null;editorContext=null;signature=Signature();return;
        }
        if(operation=="edit")
        {if(!Exists(id))throw new InvalidDataException("Fan profile no longer exists");editorId=id==ActiveId?null:id;editorContext=editorId==null?null:Context();return;}
        if(operation=="values")
        {
            if(id!=EditorId)throw new InvalidOperationException("Fan editor selection changed");
            if(request.TryGetProperty("onAc",out var source)&&source.GetBoolean()!=onAc())throw new InvalidOperationException("Power source changed; retry the Fan adjustment");
            var entry=catalog.Find(id)??throw new InvalidDataException("Fan profile no longer exists");
            string edited=request.TryGetProperty("source",out var side)?side.GetString()??"":entry.Split&&onAc()?"ac":"dc";
            if(edited is not("ac" or "dc")||edited=="ac"&&!entry.Split)throw new InvalidOperationException("Enable separate AC/DC before editing AC");
            var values=JsonSerializer.Deserialize<FanPreferences>(request.GetProperty("values"),Backend.Program.Json)??throw new InvalidDataException("Fan settings missing");values.Validate();values=values.WithSupportedMode();
            bool editedAc=edited=="ac",live=id==ActiveId&&(!entry.Split||editedAc==onAc());
            catalog.StoreValues(id,values,editedAc);
            // Remember this revision even on rejection. A timer must not repeatedly
            // try a curve after the agent has returned the fan to firmware control.
            signature=Signature();
            if(live)
            {
                try{apply(values,false);}
                catch(Exception failure){throw new FanProfileApplyException(failure);}
            }
            return;
        }
        if(operation=="create"&&request.TryGetProperty("performanceProfileId",out var parentId))
        {
            if(parent==null||bindParent==null||parentId.GetString()!=parent.Id)throw new InvalidDataException("Performance profile no longer exists");
            parent.Seed.Validate();string created=catalog.CreateDetached(parent.Id=="global"?"Global Fan":parent.Name[..Math.Min(parent.Name.Length,153)]+" Fan",parent.Seed.WithSupportedMode(),parent.Id,selected=>bindParent(parent.Id,selected));
            if(parent.Active){ClearTemporary();performanceOverride=created;editorId=null;editorContext=null;apply(catalog.ValuesFor(created),false);}
            else{editorId=created;editorContext=Context();}
            signature=Signature();return;
        }
        if(operation=="delete"&&id!=ActiveId)
        {catalog.DeleteDetached(id,()=>unlink?.Invoke(id));if(editorId==id){editorId=null;editorContext=null;}signature=Signature();return;}
        if(operation=="keep"&&editorId!=null&&editorId!=ActiveId){editorId=null;editorContext=null;return;}
        if(operation=="rename")
        {catalog.Mutate(request,_=>null);signature=Signature();return;}
        if(operation=="split")
        {
            bool enabled = request.GetProperty("enabled").GetBoolean();
            bool live = id == ActiveId;
            var previousValues = catalog.ValuesFor(id);
            catalog.StoreSourceSplit(id, enabled);
            signature = Signature();
            var desired = catalog.ValuesFor(id);
            // A split that keeps the current effective values is a catalog edit.
            // If the effective side does change, reflect the committed value in
            // the widget even when the agent has to return the fan to Auto.
            if (live && previousValues != desired)
            {
                try { apply(desired, false); }
                catch (Exception failure) { throw new FanProfileApplyException(failure); }
            }
            return;
        }
        editorId=null;editorContext=null;ClearTemporary();
        string? previous=performanceOverride;performanceOverride=null;
        try{catalog.Mutate(request,_=>null,selected=>{if(operation=="delete")unlink?.Invoke(id);followSelection?.Invoke(selected);});if(followSelection!=null)performanceOverride=catalog.ActiveId;signature=Signature();}
        catch{performanceOverride=previous;throw;}
    }
    private async Task Run()
    {
        try
        {
            while(!stopping.IsCancellationRequested)
            {
                await gate.WaitAsync(stopping.Token);
                try
                {
                    ClearStaleEditor();
                    // The Fan worker owns hardware application for passive selection
                    // changes. A rejected revision remains saved and gets one attempt.
                    catalog.Tick(applyValues:false);
                    string current=Signature();
                    var desired=catalog.ValuesFor(ActiveId);
                    if(signature!=current)
                    {
                        signature=current;
                        apply(desired,false);
                    }
                    else if(restore!=null&&recovery.ShouldRestore(current,desired,observe(),DateTimeOffset.UtcNow))
                        restore(desired);
                }
                catch(Exception failure){catalog.ReportError(failure);log(failure);}
                finally{gate.Release();}
                await Task.Delay(650,stopping.Token);
            }
        }
        catch(OperationCanceledException){}
    }
    public async ValueTask DisposeAsync(){stopping.Cancel();await worker;stopping.Dispose();}
}

internal sealed class FanProfileApplyException(Exception failure) : IOException(failure.Message, failure);
