// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;

namespace ONEXTweaks.WidgetBridge;

internal sealed record ScopedProfile<T>(string Id,string Name,string? ProcessPath,bool Split,T Ac,T Dc,DateTimeOffset ModifiedUtc,[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]T? Unified=null,[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]string? PerformanceProfileId=null,[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]bool AcInitialized=false) where T:class;
internal sealed record ScopedProfileDocument<T>(int Schema,ScopedProfile<T> Global,List<ScopedProfile<T>> Profiles,string ActiveId,string Selection,ProfileSession? ManualHold,[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]string? LastManualId=null,[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]bool FanLinksMigrated=false) where T:class;

// Shared profile identity/selection/autosave semantics. Display uses it first;
// Performance and Fan reuse the same catalog and picker in their queue stages.
// All methods are called under the owner's synchronization gate.
internal sealed class ScopedProfileCatalog<T> where T:class
{
    private readonly string file;
    private readonly bool rememberManualFallback;
    private readonly bool sourceIndependent;
    private readonly bool retainSourceSides;
    private readonly Func<T> capture;
    private readonly Func<T,bool>? needsInitialReapply;
    private readonly Action<T> apply,validate;
    private readonly Func<bool> onAc;
    private readonly Func<ProfileSession?> session;
    private readonly Func<ProfileSession,bool> alive;
    private ScopedProfileDocument<T> data;
    private bool ac,initialized,initialForcedReapplyAttempted;
    private ProfileSession? currentSession;
    private string? error;
    private string status="{}";
    internal string ActiveId=>data.ActiveId;
    internal bool HasManualHold=>data.ManualHold!=null&&alive(data.ManualHold);
    internal bool WasLoaded {get;}
    internal ScopedProfile<T> Active=>data.ActiveId=="global"?data.Global:data.Profiles.Single(entry=>entry.Id==data.ActiveId);
    internal T CurrentValues=>Values(Active);
    internal ScopedProfile<T>? Find(string id)=>id=="global"?data.Global:data.Profiles.FirstOrDefault(entry=>entry.Id==id);
    internal ScopedProfile<T>? FindProcess(string path)=>data.Profiles.FirstOrDefault(entry=>
        entry.ProcessPath?.Equals(path,StringComparison.OrdinalIgnoreCase)==true);
    internal T ValuesFor(string id)=>Values(Find(id)??throw new InvalidDataException("Profile no longer exists"));
    private T Values(ScopedProfile<T> entry)=>sourceIndependent?entry.Unified??throw new InvalidDataException("Independent profile has no values"):onAc()&&(!retainSourceSides||entry.Split)?entry.Ac:entry.Dc;
    internal ScopedProfileCatalog(string file,Func<T> capture,Action<T> apply,Action<T> validate,Func<bool> onAc,Func<ProfileSession?> session,Func<ProfileSession,bool> alive,ScopedProfile<T>? initialGlobal=null,List<ScopedProfile<T>>? initialProfiles=null,bool rememberManualFallback=false,bool sourceIndependent=false,string? initialIndependentId=null,bool retainSourceSides=false,Func<T,bool>? needsInitialReapply=null,Func<ScopedProfileDocument<T>,ScopedProfileDocument<T>>? migrateLoaded=null)
    {
        this.retainSourceSides=retainSourceSides;this.sourceIndependent=sourceIndependent;this.rememberManualFallback=rememberManualFallback;this.file=file;this.capture=capture;this.apply=apply;this.validate=validate;this.onAc=onAc;this.session=session;this.alive=alive;this.needsInitialReapply=needsInitialReapply;ac=onAc();
        WasLoaded=File.Exists(file);
        if(WasLoaded)data=JsonSerializer.Deserialize<ScopedProfileDocument<T>>(File.ReadAllText(file),Backend.Program.Json)??throw new InvalidDataException("Empty profile catalog");
        else{var value=capture();validate(value);data=new(1,initialGlobal??new("global","Global Profile",null,false,value,value,DateTimeOffset.UtcNow),initialProfiles??[],"global","manual",null);}
        var loaded=data;
        if(WasLoaded&&migrateLoaded!=null)data=migrateLoaded(data);
        bool migrated=!ReferenceEquals(loaded,data);
        if(sourceIndependent)
        {
            migrated|=data.Global.Unified==null||data.Profiles.Any(entry=>entry.Unified==null);
            ScopedProfile<T> Unify(ScopedProfile<T> entry)=>entry with{Unified=entry.Unified??(ac?entry.Ac:entry.Dc)};
            data=data with{Global=Unify(data.Global),Profiles=data.Profiles.Select(Unify).ToList()};
            // One-time detachment keeps the formerly visible preset. AC/DC values
            // remain archived in place; future edits and source changes use Unified.
            if(migrated&&initialIndependentId!=null&&Find(initialIndependentId)!=null)
                data=data with{ActiveId=initialIndependentId,Selection="manual",ManualHold=session()};
        }
        if(rememberManualFallback&&data.LastManualId==null&&data.Selection=="manual"&&Find(data.ActiveId) is {ProcessPath:null})
        {
            data=data with{LastManualId=data.ActiveId};
            migrated=true;
        }
        Validate(data);if(!WasLoaded||migrated)Write(data);Publish();
    }
    private void Validate(ScopedProfileDocument<T> value)
    {
        if(value.Schema!=1||value.Global.Id!="global"||value.Global.ProcessPath!=null||value.Profiles.Count>512||value.Selection is not("auto" or "manual"))throw new InvalidDataException("Unsupported profile catalog");
        var all=value.Profiles.Prepend(value.Global).ToArray();
        if(all.Select(entry=>entry.Id).Distinct(StringComparer.Ordinal).Count()!=all.Length||!all.Any(entry=>entry.Id==value.ActiveId))throw new InvalidDataException("Invalid profile selection");
        if(value.Profiles.Where(entry=>entry.ProcessPath!=null).Select(entry=>entry.ProcessPath!).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=value.Profiles.Count(entry=>entry.ProcessPath!=null))throw new InvalidDataException("Duplicate process profile");
        foreach(var entry in all)
        {
            if(entry.Id.Length is <1 or >64||entry.Name.Length is <1 or >160||entry.Name.Any(char.IsControl)||entry.ProcessPath!=null&&(!Path.IsPathFullyQualified(entry.ProcessPath)||!entry.ProcessPath.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Invalid profile identity");
            if(entry.PerformanceProfileId!=null&&(entry.PerformanceProfileId.Length is <1 or >64||entry.PerformanceProfileId.Any(char.IsControl)))throw new InvalidDataException("Invalid parent Performance profile");
            validate(entry.Ac);validate(entry.Dc);
            if(entry.Unified!=null)validate(entry.Unified);
            if(sourceIndependent&&entry.Unified==null)throw new InvalidDataException("Missing independent profile values");
            if(!retainSourceSides&&!entry.Split&&!EqualityComparer<T>.Default.Equals(entry.Ac,entry.Dc))throw new InvalidDataException("Unsplit profile sides differ");
        }
    }
    private void Write(ScopedProfileDocument<T> value)
    {
        Validate(value);string text=JsonSerializer.Serialize(value,Backend.Program.Json),temporary=file+".tmp";
        File.WriteAllText(temporary,text);File.Move(temporary,file,true);
    }
    private static ScopedProfileDocument<T> Replace(ScopedProfileDocument<T> value,ScopedProfile<T> entry)=>entry.Id=="global"?value with{Global=entry}:value with{Profiles=value.Profiles.Select(item=>item.Id==entry.Id?entry:item).ToList()};
    private T Desired(ScopedProfileDocument<T> value)
    {var entry=value.ActiveId=="global"?value.Global:value.Profiles.Single(item=>item.Id==value.ActiveId);return Values(entry);}
    private void Commit(ScopedProfileDocument<T> next,bool applyValues,Action<string>? followSelection=null)
    {
        Validate(next);var previous=data;var before=applyValues?capture():null;bool written=false,applyAttempted=false;string expected=JsonSerializer.Serialize(next,Backend.Program.Json);
        try
        {
            if(applyValues&&!EqualityComparer<T>.Default.Equals(before,Desired(next))){applyAttempted=true;apply(Desired(next));}
            Write(next);written=true;followSelection?.Invoke(next.ActiveId);data=next;error=null;Publish();
        }
        catch(Exception failure)
        {
            if(written)
            {
                string current;
                try{current=File.ReadAllText(file);}
                catch(Exception recovery){throw new AggregateException("Profile transaction failed and rollback ownership could not be read",failure,recovery);}
                if(current!=expected)throw new IOException("Catalog changed outside this transaction; rollback refused",failure);
                try{Write(previous);data=previous;}
                catch(Exception recovery){throw new AggregateException("Profile transaction failed and catalog rollback failed",failure,recovery);}
            }
            if(applyAttempted)try{apply(before!);}catch(Exception recovery)
            {
                error=failure.Message+"; recovery: "+recovery.Message;
                try{Publish();}
                catch(Exception publication){throw new AggregateException("Profile transaction, recovery and status publication failed",failure,recovery,publication);}
                throw new IOException(error,new AggregateException(failure,recovery));
            }
            error=failure.Message;
            try{Publish();}
            catch(Exception publication){throw new AggregateException("Profile transaction failed and its status could not be published",failure,publication);}
            throw;
        }
    }
    internal void Tick(bool applyValues=true)
    {
        bool side=onAc(),sideChanged=side!=ac;ac=side;currentSession=session();
        if(!initialized)
        {if(applyValues&&WasLoaded)Reapply(initial:true);initialized=true;}
        if(data.ManualHold!=null&&alive(data.ManualHold)){if(applyValues&&sideChanged)Reapply();Publish();return;}
        var matching=currentSession==null?null:FindProcess(currentSession.Path);
        string fallback=rememberManualFallback&&data.LastManualId!=null&&Find(data.LastManualId) is {ProcessPath:null}?data.LastManualId:"global";
        bool returnFromGame=rememberManualFallback&&data.ManualHold!=null&&Active.ProcessPath!=null;
        string selected=matching?.Id??(data.Selection=="auto"||returnFromGame?fallback:data.ActiveId);
        string selection=matching!=null?"auto":rememberManualFallback&&(data.Selection=="auto"||returnFromGame)?"manual":data.Selection;
        if(selected!=data.ActiveId||selection!=data.Selection||data.ManualHold!=null)
            Commit(data with{ActiveId=selected,Selection=selection,ManualHold=null},applyValues);
        else if(applyValues&&sideChanged)Reapply();
        Publish();
    }
    internal void Reapply()=>Reapply(initial:false);

    private void Reapply(bool initial)
    {
        var before=capture();var desired=Desired(data);
        bool sameValues=EqualityComparer<T>.Default.Equals(before,desired);
        if(sameValues)
        {
            // Persisted values can match even after the hardware has reset.
            if(!initial||initialForcedReapplyAttempted||needsInitialReapply?.Invoke(desired)!=true)return;
            initialForcedReapplyAttempted=true;
        }
        try{apply(desired);error=null;}
        catch(Exception failure)
        {
            try{if(!sameValues)apply(before);}
            catch(Exception recovery){throw new IOException(failure.Message+"; profile recovery failed: "+recovery.Message,new AggregateException(failure,recovery));}
            error=failure.Message;
            try{Publish();}
            catch(Exception publication){throw new AggregateException("Profile reapply failed and its status could not be published",failure,publication);}
            throw;
        }
    }
    internal void Record(string? targetId=null)
    {
        ac=onAc();var value=capture();validate(value);var entry=targetId==null?Active:Find(targetId)??throw new InvalidDataException("Profile no longer exists");
        entry=UpdatedValues(entry,value,ac);
        Commit(Replace(data,entry),false);
    }
    internal void SelectWithoutApply(string id)
    {
        var entry=Find(id)??throw new InvalidDataException("Profile no longer exists");
        Commit(data with{ActiveId=id,Selection="manual",ManualHold=session(),LastManualId=rememberManualFallback&&entry.ProcessPath==null?id:data.LastManualId},false);
    }
    internal string CreateDetached(string name,T seed,string? parentId,Action<string>? bind=null,string? processPath=null)
    {
        validate(seed);name=name.Trim();if(name.Length is <1 or >160||name.Any(char.IsControl))throw new InvalidDataException("Invalid profile name");
        string basis=name;int suffix=2;while(data.Profiles.Any(entry=>entry.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))name=basis[..Math.Min(basis.Length,150)]+" "+suffix++;
        var created=new ScopedProfile<T>(Guid.NewGuid().ToString("N"),name,processPath,false,seed,seed,DateTimeOffset.UtcNow,sourceIndependent?seed:null,parentId);
        Commit(data with{Profiles=data.Profiles.Append(created).ToList()},false,_=>bind?.Invoke(created.Id));return created.Id;
    }
    private ScopedProfile<T> UpdatedValues(ScopedProfile<T> entry,T value,bool side)
    {
        if(sourceIndependent)return entry with{Unified=value,ModifiedUtc=DateTimeOffset.UtcNow};
        if(entry.Split)return entry with{Ac=side?value:entry.Ac,Dc=side?entry.Dc:value,AcInitialized=retainSourceSides||entry.AcInitialized,ModifiedUtc=DateTimeOffset.UtcNow};
        // Fan's unified editor is DC. A previously configured AC side stays archived.
        return retainSourceSides?entry with{Dc=value,Ac=entry.AcInitialized?entry.Ac:value,ModifiedUtc=DateTimeOffset.UtcNow}:entry with{Ac=value,Dc=value,ModifiedUtc=DateTimeOffset.UtcNow};
    }
    internal void StoreValues(string id,T value,bool? sourceAc=null)
    {
        validate(value);var entry=Find(id)??throw new InvalidDataException("Profile no longer exists");
        if(sourceAc.HasValue&&!retainSourceSides)throw new InvalidOperationException("Explicit source editing is unavailable for this catalog");
        if(sourceAc==true&&!entry.Split)throw new InvalidOperationException("Enable separate AC/DC before editing AC");
        Commit(Replace(data,UpdatedValues(entry,value,sourceAc??onAc())),false);
    }
    private ScopedProfile<T> SourceSplitEntry(string id,bool enabled)
    {
        if(!retainSourceSides)throw new InvalidOperationException("Retained source editing is unavailable for this catalog");
        var entry=Find(id)??throw new InvalidDataException("Profile no longer exists");bool initialized=entry.AcInitialized||entry.Split;
        return entry with{Split=enabled,Ac=enabled&&!initialized?entry.Dc:entry.Ac,AcInitialized=initialized||enabled,ModifiedUtc=DateTimeOffset.UtcNow};
    }
    internal T ValuesWithSourceSplit(string id,bool enabled)=>Values(SourceSplitEntry(id,enabled));
    internal void StoreSourceSplit(string id,bool enabled)=>Commit(Replace(data,SourceSplitEntry(id,enabled)),false);
    internal void DeleteDetached(string id,Action? unlink=null)
    {
        if(id=="global"||Find(id)==null)throw new InvalidOperationException("Profile cannot be removed");
        Commit(data with{Profiles=data.Profiles.Where(entry=>entry.Id!=id).ToList(),ActiveId=data.ActiveId==id?"global":data.ActiveId,LastManualId=data.LastManualId==id?"global":data.LastManualId},false,_=>unlink?.Invoke());
    }
    internal void Mutate(JsonElement request,Func<int,ProfileSession?> identify,Action<string>? followSelection=null)
    {
        string operation=request.GetProperty("operation").GetString()??"";
        string id=request.TryGetProperty("id",out var value)?value.GetString()??"":data.ActiveId;
        var target=id=="global"?data.Global:data.Profiles.FirstOrDefault(entry=>entry.Id==id);
        var next=data;currentSession=session();ac=onAc();
        switch(operation)
        {
            case "rename":
                if(target==null||target.Id=="global")throw new InvalidOperationException("Global Profile cannot be renamed");
                string renamed=(request.GetProperty("name").GetString()??"").Trim();
                if(renamed.Length is <1 or >160||renamed.Any(char.IsControl))throw new InvalidDataException("Use a name of 1–160 characters without control characters");
                if(data.Profiles.Any(entry=>entry.Id!=target.Id&&entry.Name.Equals(renamed,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("A profile with this name already exists");
                Commit(Replace(data,target with{Name=renamed,ModifiedUtc=DateTimeOffset.UtcNow}),false);return;
            case "select":
                if(target==null)throw new InvalidDataException("Profile no longer exists");
                next=data with{ActiveId=target.Id,Selection="manual",ManualHold=currentSession};break;
            case "create":
                string name=(request.TryGetProperty("name",out var title)?title.GetString():null)?.Trim()??"";
                ProfileSession? process=null;
                if(request.TryGetProperty("processId",out var pid))
                {
                    process=identify(pid.GetInt32())??throw new IOException("This process is no longer available");
                    var stamp=request.GetProperty("startedUtcTicks");long ticks=stamp.ValueKind==JsonValueKind.String?long.Parse(stamp.GetString()!,System.Globalization.CultureInfo.InvariantCulture):stamp.GetInt64();
                    if(ticks!=process.StartedUtcTicks)throw new IOException("The selected process restarted; refresh the list");
                    if(data.Profiles.Any(entry=>entry.ProcessPath?.Equals(process.Path,StringComparison.OrdinalIgnoreCase)==true))throw new InvalidOperationException("A profile for this process already exists; choose it from the list");
                    name=process.Name;
                }
                if(name.Length==0){int n=1;while(data.Profiles.Any(entry=>entry.Name.Equals("Manual "+n,StringComparison.OrdinalIgnoreCase)))n++;name="Manual "+n;}
                if(name.Length>160||name.Any(char.IsControl))throw new InvalidDataException("Use a profile name of up to160 characters");
                var seed=capture();validate(seed);var created=new ScopedProfile<T>(Guid.NewGuid().ToString("N"),name,process?.Path,false,seed,seed,DateTimeOffset.UtcNow,sourceIndependent?seed:null);
                next=data with{Profiles=data.Profiles.Append(created).ToList(),ActiveId=created.Id,Selection="manual",ManualHold=process??currentSession};break;
            case "delete":case "keep":
                if(target==null||target.Id=="global")throw new InvalidOperationException("Global Profile cannot be removed");
                next=data with{Profiles=operation=="delete"?data.Profiles.Where(entry=>entry.Id!=target.Id).ToList():data.Profiles,ActiveId="global",Selection="manual",ManualHold=currentSession};break;
            case "split":
                if(sourceIndependent)throw new InvalidOperationException("Screen Color profiles use one set of values for battery and AC");
                if(target==null)throw new InvalidDataException("Profile no longer exists");
                var effective=ac?target.Ac:target.Dc;target=target with{Split=request.GetProperty("enabled").GetBoolean(),Ac=effective,Dc=effective,ModifiedUtc=DateTimeOffset.UtcNow};next=Replace(data,target);break;
            default:throw new InvalidDataException("Unknown profile catalog operation");
        }
        if(rememberManualFallback)
        {
            var active=next.ActiveId=="global"?next.Global:next.Profiles.Single(entry=>entry.Id==next.ActiveId);
            if(operation is "select" or "create" or "keep" or "delete"&&active.ProcessPath==null)next=next with{LastManualId=active.Id};
        }
        Commit(next,true,operation=="split"?null:followSelection);
    }
    internal string Read()=>Volatile.Read(ref status);
    internal void ReportError(Exception failure){error=failure.Message;Publish();}
    private void Publish()
    {
        var ordered=data.Profiles.OrderBy(entry=>entry.ProcessPath==null?0:1).ThenBy(entry=>entry.Name,StringComparer.CurrentCultureIgnoreCase);
        var entries=ordered.Prepend(data.Global).Select(entry=>sourceIndependent?entry with{Split=false,Ac=Values(entry),Dc=Values(entry)}:entry);
        Volatile.Write(ref status,JsonSerializer.Serialize(new{activeId=data.ActiveId,onAc=ac,sourceIndependent,selection=data.Selection,manualOverride=data.ManualHold!=null&&alive(data.ManualHold),currentSession,entries,error},Backend.Program.Json));
    }
}
