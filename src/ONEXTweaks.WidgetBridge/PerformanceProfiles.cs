// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
namespace ONEXTweaks.WidgetBridge;
internal sealed record PerformanceProfileRuntime(Func<ProfileSession?> Current,Func<ProfileSession,bool> Alive,Func<ProfileCandidate[]> Candidates,Func<string?>? ScreenProfile=null,Func<CpuTuningPolicy?,Task>? MaintainCpu=null,Func<string?>? FanProfile=null,Func<Task<PerformanceTransitionSnapshot>>? CaptureTransition=null);
internal sealed record PerformanceTransitionSnapshot(ProfileValues Values, Func<Task>? Restore = null);
internal sealed class PerformanceProfiles:IAsyncDisposable
{
    internal const string HardwareUnavailable = "Performance profiles require the ONEXPLAYER hardware service.";
    internal static readonly string[] Groups=["TDP","CPUBoost","FPSLimit","OSPowerMode","Fan","OverlayLevel","Display","Resolution","RefreshRate","ScreenProfile"];
    internal readonly SemaphoreSlim Gate=new(1,1);
    private readonly string file,reports;
    private readonly Func<Task<ProfileValues>> capture;
    private readonly Func<ProfileValues,Task> apply;
    private readonly Func<bool> onAc;
    private readonly PerformanceProfileRuntime runtime;
    private readonly CancellationTokenSource stopping=new();
    private Task worker=Task.CompletedTask;
    private ProfileDocument? loadedDocument;
    private ProfileDocument document
    {
        get => loadedDocument ?? throw new InvalidOperationException(HardwareUnavailable);
        set => loadedDocument = value;
    }
    private ProfileSession? currentSession;
    private DetectedGame? game;
    private string context="",status="{}";
    private bool powerAc;
    private string? error;
    private PerformanceProfiles(string folder,ProfileDocument? initial,Func<Task<ProfileValues>> capture,Func<ProfileValues,Task> apply,Func<bool> onAc,PerformanceProfileRuntime runtime)
    {reports=folder;file=Path.Combine(folder,"..","performance-profiles.json");loadedDocument=initial;this.capture=capture;this.apply=apply;this.onAc=onAc;this.runtime=runtime;powerAc=onAc();if(Available)Publish();}
    internal bool Available => loadedDocument != null;
    internal static PerformanceProfiles WithoutHardware(string folder, Func<bool> onAc, PerformanceProfileRuntime runtime)
        => new(folder, null, () => throw new InvalidOperationException(HardwareUnavailable),
            _ => throw new InvalidOperationException(HardwareUnavailable), onAc, runtime);
    internal static async Task<PerformanceProfiles> Create(string folder,Func<Task<ProfileValues>> capture,Func<ProfileValues,Task> apply,Func<DetectedGame?> detect,Func<bool> onAc,PerformanceProfileRuntime? runtime=null,bool startWorker=true)
    {
        string file=Path.Combine(folder,"..","performance-profiles.json");ProfileDocument initial;
        bool initializing = !File.Exists(file);
        if(!initializing)
        {
            string json;
            try { json = File.ReadAllText(file); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                ProfileStoreStartupDiagnostic.TryWrite(folder, ProfileStoreStartupFailure.StoreIoFailure);
                throw;
            }
            ProfileDocument? loaded;
            try
            {
                loaded = JsonSerializer.Deserialize<ProfileDocument>(json, Backend.Program.Json);
            }
            catch (JsonException)
            {
                ProfileStoreStartupDiagnostic.TryWrite(folder, ProfileStoreStartupFailure.InvalidJson);
                throw;
            }
            if (loaded is null)
            {
                ProfileStoreStartupDiagnostic.TryWrite(folder, ProfileStoreStartupFailure.EmptyDocument);
                throw new InvalidDataException("Empty performance profile store");
            }
            initial = loaded;
            if(initial.Schema is 1 or 2)initial=UpgradeDisplay(initial,(await capture()).Display);
            if(initial.Schema==3)initial=initial with{Schema=4,Scope=new()};
            if(initial.Schema==4){var flags=new Dictionary<string,bool>(initial.Flags);flags.TryAdd("ScreenProfile",true);initial=initial with{Schema=5,Flags=flags};}
            if(initial.Schema==5)initial=initial with{Schema=6,Global=initial.Global with{AcInitialized=initial.Global.Split},Games=initial.Games.Select(entry=>entry with{AcInitialized=entry.Split}).ToList()};
            if(initial.Schema==6)initial=initial with{Schema=7};
            try { Validate(initial); }
            catch (InvalidDataException)
            {
                ProfileStoreStartupDiagnostic.TryWrite(folder, ProfileStoreStartupFailure.InvalidProfileDocument);
                throw;
            }
        }
        else
        {
            if(File.Exists(file+".tmp"))
            {
                ProfileStoreStartupDiagnostic.TryWrite(folder, ProfileStoreStartupFailure.InterruptedSave);
                throw new InvalidDataException("Uncommitted performance profile store");
            }
            var value=await capture();value.Validate();var windows=WindowsPowerSettings.Read();if(!windows.Available||windows.Cpu==null)throw new IOException(windows.Error);
            var ac=value with{Boost=(int)windows.Cpu.BoostAc,Scheduling=(int)windows.Cpu.SchedulingAc,PowerMode=windows.ModeAc};
            var dc=value with{Boost=(int)windows.Cpu.BoostDc,Scheduling=(int)windows.Cpu.SchedulingDc,PowerMode=windows.ModeDc};
            initial=new(7,new("global","global","",false,ac!=dc,ac,dc,DateTimeOffset.UtcNow,AcInitialized:ac!=dc),[],Groups.ToDictionary(group=>group,_=>true),new());
        }
        ProfileSession? LegacyCurrent(){var detected=detect();return detected==null?null:ProfileSessions.Identify(detected.ProcessId);}
        runtime??=new(LegacyCurrent,ProfileSessions.Alive,()=>[]);
        var manager=new PerformanceProfiles(folder,initial,capture,apply,onAc,runtime);
        try { manager.Save(initializing); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ProfileStoreStartupDiagnostic.TryWrite(folder, ProfileStoreStartupFailure.StoreIoFailure);
            throw;
        }
        if(startWorker)manager.worker=Task.Run(manager.Run);return manager;
    }
    internal static ProfileDocument UpgradeDisplay(ProfileDocument initial,DisplayPreferences? baseline)
    {
        if(initial.Schema is not(1 or 2))return initial;
        var flags=new Dictionary<string,bool>(initial.Flags);foreach(string group in Groups)flags.TryAdd(group,true);
        var global=initial.Global with{Ac=initial.Global.Ac with{Display=initial.Global.Ac.Display??baseline},Dc=initial.Global.Dc with{Display=initial.Global.Dc.Display??baseline}};
        return initial with{Schema=3,Global=global,Flags=flags};
    }
    private static PerformanceSelectable[] Selectables(ProfileDocument value)=>value.Games.Prepend(value.Global)
        .Select(entry=>new PerformanceSelectable(entry.Id,string.IsNullOrEmpty(entry.Path)?null:entry.Path,entry.Use)).ToArray();
    private static void Validate(ProfileDocument value)
    {
        if(value.Schema!=7||value.Scope==null||value.Games.Count>512||value.Global.Id!="global"||Groups.Any(group=>!value.Flags.ContainsKey(group)))throw new InvalidDataException("Unsupported profile store");
        foreach(var entry in value.Games.Prepend(value.Global))
        {
            entry.Ac.Validate();entry.Dc.Validate();
            if(entry.LocalGroups is{} local&&(local.Length>Groups.Length||local.Distinct(StringComparer.Ordinal).Count()!=local.Length||local.Any(group=>!Groups.Contains(group,StringComparer.Ordinal)||group=="ScreenProfile")))throw new InvalidDataException("Invalid local profile groups");
            if(entry.Split&&!entry.AcInitialized)throw new InvalidDataException("Split profile has no initialized AC side");
            if(entry.Name.Length is <1 or >160||entry.Name.Any(char.IsControl))throw new InvalidDataException("Invalid profile identity");
        }
        PerformanceSelectionPolicy.Validate(value.Scope,Selectables(value));
    }
    private ProfileEntry? Current=>game==null?null:document.Games.FirstOrDefault(entry=>entry.Path.Equals(game.Path,StringComparison.OrdinalIgnoreCase));
    internal string? ActiveScreenProfile=>Available?ProfileResolution.Resolve(document,Active,powerAc).ScreenProfile:null;
    internal string? ActiveFanProfile=>Available?ProfileResolution.Resolve(document,Active,powerAc).FanProfile:null;
    private ProfileEntry Active=>document.Scope!.ActiveId=="global"?document.Global:document.Games.Single(entry=>entry.Id==document.Scope.ActiveId);
    private void Replace(ProfileEntry entry){document=entry.Id=="global"?document with{Global=entry}:document with{Games=document.Games.Select(item=>item.Id==entry.Id?entry:item).ToList()};}
    private string Save(bool initializing = false)
    {
        Validate(document);
        string temporary=file+".tmp";
        string json=JsonSerializer.Serialize(document,Backend.Program.Json);
        if (initializing)
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
                writer.Write(json);
        }
        else File.WriteAllText(temporary,json);
        File.Move(temporary,file,overwrite: !initializing);
        return json;
    }
    private void RefreshContext()
    {currentSession=runtime.Current();game=currentSession==null?null:new(currentSession.ProcessId,currentSession.Name,currentSession.Path);}
    private string Context()
    {
        string key=Active.Id+":"+powerAc;
        if(document.Scope!.Selection=="auto"&&currentSession!=null&&Active.Path.Equals(currentSession.Path,StringComparison.OrdinalIgnoreCase))
            key+=":"+currentSession.ProcessId+":"+currentSession.StartedUtcTicks;
        return key;
    }
    private void RestoreOwnedFile(string? written,Exception failure)
    {
        if(written==null)return;
        try
        {
            if(!File.ReadAllBytes(file).AsSpan().SequenceEqual(System.Text.Encoding.UTF8.GetBytes(written)))
                throw new IOException("Profile file ownership changed; rollback refused");
            Save();
        }
        catch(Exception restoration)
        {
            error=failure.Message+"; catalog rollback failed: "+restoration.Message;
            var combined=new AggregateException(failure,restoration);
            PublishFailure(combined);throw new IOException(error,combined);
        }
    }
    private void PublishFailure(Exception failure)
    {
        try{Publish();}
        catch(Exception publication){throw new AggregateException("Performance profile failure and status publication failure",failure,publication);}
    }
    // Caller owns Gate. Apply + save is one transaction, including rollback if
    // disk persistence fails after a successful hardware change.
    private async Task Commit(ProfileDocument before,string oldContext,bool oldAc,string reason)
    {
        PerformanceTransitionSnapshot? snapshot = null;
        bool attempted = false;
        string? written = null;
        try
        {
            Validate(document);
            snapshot = runtime.CaptureTransition != null ? await runtime.CaptureTransition() : new(await capture());
            snapshot.Values.Validate();
            var desired = ProfileResolution.Resolve(document, Active, powerAc);
            if (snapshot.Restore != null || snapshot.Values != desired)
            {
                attempted = true;
                await apply(desired);
            }
            written=Save();context=Context();error=null;Publish();
        }
        catch(Exception failure)
        {
            document=before;context=oldContext;powerAc=oldAc;
            RestoreOwnedFile(written,failure);
            if(attempted && snapshot != null)try{await (snapshot.Restore?.Invoke() ?? apply(snapshot.Values));}catch(Exception rollback)
            {
                error=failure.Message+"; rollback failed: "+rollback.Message;
                var combined=new AggregateException(failure,rollback);
                PublishFailure(combined);throw new IOException(error,combined);
            }
            error=failure.Message;PublishFailure(failure);throw;
        }
        if(attempted)try{await File.AppendAllTextAsync(Path.Combine(reports,"profile-switches.jsonl"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,reason,game,activeId=Active.Id},Backend.Program.Json)+Environment.NewLine);}catch(IOException){}
    }
    internal async Task Poll()
    {
        var before=document;string oldContext=context;bool oldAc=powerAc;RefreshContext();
        var selected=PerformanceSelectionPolicy.Tick(document.Scope!,currentSession,runtime.Alive,Selectables(document));
        document=document with{Scope=selected};powerAc=onAc();
        if(selected!=before.Scope||Context()!=context)await Commit(before,oldContext,oldAc,context.Length==0?"startup":"automatic");
        else{if(runtime.MaintainCpu!=null)await runtime.MaintainCpu(ProfileResolution.Resolve(document,Active,powerAc).CpuTuning);Publish();}
    }
    private async Task Run()
    {
        try
        {
            await Task.Delay(2200,stopping.Token);ProfileSession? pending=null;int stable=0;
            while(!stopping.IsCancellationRequested)
            {
                await Gate.WaitAsync(stopping.Token);
                try{var candidate=runtime.Current();if(candidate==pending)stable++;else{pending=candidate;stable=1;}if(stable>=2)await Poll();}
                catch(Exception failure){error=failure.Message;PublishFailure(failure);}
                finally{Gate.Release();}
                await Task.Delay(600,stopping.Token);
            }
        }
        catch(OperationCanceledException){}
    }
    internal async Task Record(string group,CpuPowerSettings? cpuPolicies=null,string? selectedScreen=null,bool preferActive=false,bool? powerWriteOnAc=null)
    {
        var before=document;bool oldAc=powerAc;string? written=null;try
        {
            if(!Groups.Contains(group))throw new InvalidDataException("Unknown profile setting");
            bool source=onAc();
            PowerSourceChangedException.Require(powerWriteOnAc,source);
            var value=await capture();
            if(powerWriteOnAc.HasValue)PowerSourceChangedException.Require(powerWriteOnAc,onAc());
            powerAc=source;
            if(group=="ScreenProfile")value=value with{ScreenProfile=selectedScreen};value.Validate();var target=preferActive||document.Flags[group]||!Active.UseSharedPolicy||ProfileResolution.HasLocalGroup(Active,group)?Active:document.Global;
            if(preferActive&&target.Id!="global"&&target.UseSharedPolicy&&!document.Flags[group]&&!ProfileResolution.HasLocalGroup(target,group))
            {
                // Freeze only this inherited group, preserving its other source's effective
                // value and every unrelated inheritance rule. Unsplit AC archives stay intact.
                var local=target.LocalGroups??[];var frozenDc=target.Dc.GroupFrom(group,ProfileResolution.Resolve(document,target,false));var frozenAc=target.Split?target.Ac.GroupFrom(group,ProfileResolution.Resolve(document,target,true)):target.Ac;
                target=target with{Dc=frozenDc,Ac=frozenAc,LocalGroups=local.Append(group).OrderBy(key=>key,StringComparer.Ordinal).ToArray()};
            }
            var ac=powerAc&&target.Split?target.Ac.GroupFrom(group,value):target.Ac;var dc=!powerAc||!target.Split?target.Dc.GroupFrom(group,value):target.Dc;
            bool split=target.Split;
            if(group=="CPUBoost"&&cpuPolicies!=null){var cpuSide=(powerAc&&split?ac:dc) with{Boost=(int)(powerAc?cpuPolicies.BoostAc:cpuPolicies.BoostDc),Scheduling=(int)(powerAc?cpuPolicies.SchedulingAc:cpuPolicies.SchedulingDc)};if(powerAc&&split)ac=cpuSide;else dc=cpuSide;}
            Replace(target with{Split=split,Ac=ac,Dc=dc,ModifiedUtc=DateTimeOffset.UtcNow});written=Save();context=Context();error=null;Publish();
        }
        catch(Exception failure){document=before;powerAc=oldAc;RestoreOwnedFile(written,failure);error=failure.Message;PublishFailure(failure);throw;}
    }
    // Called under Gate after a child catalog has applied/persisted the choice.
    // Only links are changed: never recapture/reapply TDP, CPU, media or display.
    internal ProfileEntry? FindEntry(string id)=>id=="global"?document.Global:document.Games.FirstOrDefault(entry=>entry.Id==id);
    internal ProfileValues ValuesForEntry(string id)=>ProfileResolution.Resolve(document,FindEntry(id)??throw new InvalidDataException("Performance profile no longer exists"),onAc());
    internal void BindFanToEntry(string parentId,string fanId)
    {
        if(fanId.Length is <1 or >64||fanId.Any(char.IsControl))throw new InvalidDataException("Invalid Fan profile");
        var parent=FindEntry(parentId)??throw new InvalidDataException("Performance profile no longer exists");var before=document;string? written=null;
        try{Replace(parent with{Ac=parent.Ac with{FanProfile=fanId},Dc=parent.Dc with{FanProfile=fanId},ModifiedUtc=DateTimeOffset.UtcNow});Validate(document);written=Save();context=Context();error=null;Publish();}
        catch(Exception failure){document=before;RestoreOwnedFile(written,failure);error=failure.Message;PublishFailure(failure);throw;}
    }
    internal void UnlinkFanEntry(string fanId)
    {
        var before=document;string? written=null;
        try
        {
            ProfileEntry Clear(ProfileEntry entry)=>entry.Ac.FanProfile!=fanId&&entry.Dc.FanProfile!=fanId?entry:entry with{Ac=entry.Ac.FanProfile==fanId?entry.Ac with{FanProfile="global"}:entry.Ac,Dc=entry.Dc.FanProfile==fanId?entry.Dc with{FanProfile="global"}:entry.Dc,ModifiedUtc=DateTimeOffset.UtcNow};
            document=document with{Global=Clear(document.Global),Games=document.Games.Select(Clear).ToList()};Validate(document);written=Save();context=Context();error=null;Publish();
        }
        catch(Exception failure){document=before;RestoreOwnedFile(written,failure);error=failure.Message;PublishFailure(failure);throw;}
    }
    internal void BindPreset(string group,string id,string? removedId=null)
    {
        if(group is not("Fan" or "ScreenProfile")||id.Length is <1 or >64||id.Any(char.IsControl))throw new InvalidDataException("Invalid preset binding");
        var before=document;string? written=null;
        try
        {
            ProfileValues Link(ProfileValues value,string selected)=>group=="Fan"?value with{FanProfile=selected}:value with{ScreenProfile=selected};
            bool Matches(ProfileValues value)=>removedId!=null&&(group=="Fan"?value.FanProfile:value.ScreenProfile)==removedId;
            if(removedId!=null)
            {
                ProfileEntry Clear(ProfileEntry entry){bool ac=Matches(entry.Ac),dc=Matches(entry.Dc);return !ac&&!dc?entry:entry with{Ac=ac?Link(entry.Ac,"global"):entry.Ac,Dc=dc?Link(entry.Dc,"global"):entry.Dc,ModifiedUtc=DateTimeOffset.UtcNow};}
                document=document with{Global=Clear(document.Global),Games=document.Games.Select(Clear).ToList()};
            }
            var target=Active;var side=Link(powerAc&&target.Split?target.Ac:target.Dc,id);side.Validate();
            if((powerAc&&target.Split?target.Ac:target.Dc)!=side)Replace(target with{Ac=powerAc&&target.Split?side:target.Ac,Dc=!powerAc||!target.Split?side:target.Dc,ModifiedUtc=DateTimeOffset.UtcNow});
            written=Save();context=Context();error=null;Publish();
        }
        catch(Exception failure){document=before;RestoreOwnedFile(written,failure);error=failure.Message;PublishFailure(failure);throw;}
    }
    internal string Candidates()=>JsonSerializer.Serialize(runtime.Candidates(),Backend.Program.Json);
    internal ProfileSession ResolveLinkedProcess(JsonElement change)
    {
        if(change.GetProperty("operation").GetString()!="create"||change.TryGetProperty("performanceProfileId",out _))
            throw new InvalidDataException("Choose one running app or one saved Performance profile");
        int pid=change.GetProperty("processId").GetInt32();
        if(!change.TryGetProperty("startedUtcTicks",out var stamp)||stamp.ValueKind!=JsonValueKind.String||
            !long.TryParse(stamp.GetString(),NumberStyles.None,CultureInfo.InvariantCulture,out long started)||started<=0)
            throw new InvalidDataException("The running process identity is missing");
        var process=runtime.Candidates().FirstOrDefault(candidate=>candidate.Session.ProcessId==pid)?.Session;
        if(process==null||process.StartedUtcTicks!=started||!runtime.Alive(process))
            throw new IOException("The process restarted or exited. Open Create Profile again.");
        return process;
    }
    internal string ActiveId=>Available?Active.Id:"global";
    internal ProfileSession? CurrentSession=>currentSession;
    internal long CurrentSessionStamp=>currentSession?.StartedUtcTicks??0;
    internal async Task Mutate(JsonElement request)
    {
        var before=document;string? written=null;string oldContext=context;bool oldAc=powerAc;RefreshContext();powerAc=onAc();
        string operation=request.GetProperty("operation").GetString()??"";
        try
        {
            string id=request.TryGetProperty("id",out var token)?token.GetString()??"":Active.Id;
            var target=id=="global"?document.Global:document.Games.FirstOrDefault(entry=>entry.Id==id);
            switch(operation)
            {
                case "rename":
                    if(target==null||target.Id=="global")throw new InvalidOperationException("Global Profile cannot be renamed");
                    string renamed=(request.GetProperty("name").GetString()??"").Trim();
                    if(renamed.Length is <1 or >160||renamed.Any(char.IsControl))throw new InvalidDataException("Use a name of 1–160 characters without control characters");
                    if(document.Games.Any(entry=>entry.Id!=target.Id&&entry.Name.Equals(renamed,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("A profile with this name already exists");
                    Replace(target with{Name=renamed,ModifiedUtc=DateTimeOffset.UtcNow});powerAc=oldAc;Validate(document);written=Save();error=null;Publish();return;
                case "select":document=document with{Scope=PerformanceSelectionPolicy.Select(document.Scope!,id,currentSession,Selectables(document))};break;
                case "automatic":document=document with{Scope=PerformanceSelectionPolicy.Tick(new(),currentSession,runtime.Alive,Selectables(document))};break;
                case "create":
                    ProfileSession? process=null;string name=(request.TryGetProperty("name",out var title)?title.GetString():null)?.Trim()??"";
                    if(request.TryGetProperty("processId",out var pid))
                    {
                        process=runtime.Candidates().FirstOrDefault(candidate=>candidate.Session.ProcessId==pid.GetInt32())?.Session??throw new IOException("Process is no longer a candidate; refresh the list");
                        var stamp=request.GetProperty("startedUtcTicks");long ticks=stamp.ValueKind==JsonValueKind.String?long.Parse(stamp.GetString()!,CultureInfo.InvariantCulture):stamp.GetInt64();
                        if(ticks!=process.StartedUtcTicks||!runtime.Alive(process))throw new IOException("Process restarted or exited; refresh the list");
                        if(document.Games.Any(entry=>entry.Path.Equals(process.Path,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("A profile for this process already exists; choose it from the list");
                        name=process.Name.Length>160?process.Name[..160]:process.Name;
                    }
                    if(name.Length==0){int n=1;while(document.Games.Any(entry=>entry.Name.Equals("Manual "+n,StringComparison.OrdinalIgnoreCase)))n++;name="Manual "+n;}
                    var seed=await capture();seed=seed with{ScreenProfile=null,CpuTuning=seed.CpuTuning?.Empty==true?null:seed.CpuTuning,FanProfile=runtime.FanProfile?.Invoke()??seed.FanProfile};seed.Validate();var created=new ProfileEntry(Guid.NewGuid().ToString("N"),name,process?.Path??"",process!=null,false,seed,seed,DateTimeOffset.UtcNow,false);
                    document=document with{Games=document.Games.Append(created).ToList()};document=document with{Scope=PerformanceSelectionPolicy.Select(document.Scope!,created.Id,process??currentSession,Selectables(document))};break;
                case "use":
                    if(game==null)throw new InvalidOperationException("No detected app/game");target=Current;
                    if(target==null){var initial=await capture();target=new(Guid.NewGuid().ToString("N"),game.Name,game.Path,false,false,initial,initial,DateTimeOffset.UtcNow);document=document with{Games=document.Games.Append(target).ToList()};}
                    bool enabled=request.GetProperty("enabled").GetBoolean();Replace(target with{Use=enabled,ModifiedUtc=DateTimeOffset.UtcNow});document=document with{Scope=PerformanceSelectionPolicy.Select(document.Scope!,enabled?target.Id:"global",currentSession,Selectables(document))};break;
                case "split":
                    if(target==null)throw new InvalidDataException("Profile not found");bool splitEnabled=request.GetProperty("enabled").GetBoolean();
                    Replace(target with{Split=splitEnabled,Ac=splitEnabled&&!target.AcInitialized?target.Dc:target.Ac,AcInitialized=target.AcInitialized||splitEnabled,ModifiedUtc=DateTimeOffset.UtcNow});break;
                case "source":
                    if(target==null||target.Id!=Active.Id)throw new InvalidOperationException("The active profile changed; reopen the editor");
                    string source=request.GetProperty("source").GetString()??"",key=request.GetProperty("key").GetString()??"";
                    if(source is not("ac" or "dc")||source=="ac"&&!target.Split)throw new InvalidDataException("Enable separate AC/DC before editing AC");
                    string group=ProfileSourcePatch.Group(key);
                    if(target.Id!="global"&&target.UseSharedPolicy&&!ProfileResolution.HasLocalGroup(target,group)&&!document.Flags.GetValueOrDefault(group,true))throw new InvalidOperationException("This setting is inherited from Global; enable its per-profile policy first");
                    bool editAc=source=="ac";var changed=ProfileSourcePatch.Apply(editAc?target.Ac:target.Dc,key,request.GetProperty("value"));
                    Replace(target with{Ac=editAc?changed:target.Ac,Dc=editAc?target.Dc:changed,ModifiedUtc=DateTimeOffset.UtcNow});
                    if(target.Split&&editAc!=powerAc){powerAc=oldAc;Validate(document);written=Save();error=null;Publish();return;}
                    break;
                case "screen":
                    target=Active;var screenToken=request.GetProperty("screenProfileId");string? screenId=screenToken.ValueKind==JsonValueKind.Null?null:screenToken.GetString();
                    var side=(powerAc&&target.Split?target.Ac:target.Dc) with{ScreenProfile=screenId};side.Validate();Replace(target with{Ac=powerAc&&target.Split?side:target.Ac,Dc=!powerAc||!target.Split?side:target.Dc,ModifiedUtc=DateTimeOffset.UtcNow});break;
                case "fan":
                    target=Active;var fanToken=request.GetProperty("fanProfileId");string? fanId=fanToken.ValueKind==JsonValueKind.Null?null:fanToken.GetString();
                    var fanSide=(powerAc&&target.Split?target.Ac:target.Dc) with{FanProfile=fanId};fanSide.Validate();Replace(target with{Ac=powerAc&&target.Split?fanSide:target.Ac,Dc=!powerAc||!target.Split?fanSide:target.Dc,ModifiedUtc=DateTimeOffset.UtcNow});break;
                case "delete":case "keep":
                    if(target==null||target.Id=="global")throw new InvalidOperationException("Global Profile cannot be removed");
                    if(operation=="delete")
                    {
                        bool inactive=target.Id!=Active.Id;document=document with{Games=document.Games.Where(entry=>entry.Id!=target.Id).ToList()};
                        if(inactive){powerAc=oldAc;Validate(document);written=Save();error=null;Publish();return;}
                    }
                    document=document with{Scope=PerformanceSelectionPolicy.Select(document.Scope!,"global",currentSession,Selectables(document))};break;
                case "flags":
                    var flags=new Dictionary<string,bool>(document.Flags);foreach(var flag in request.GetProperty("flags").EnumerateObject())if(Groups.Contains(flag.Name))flags[flag.Name]=flag.Value.GetBoolean();document=document with{Flags=flags};break;
                case "edit":
                    if(target==null)throw new InvalidDataException("Profile not found");var ac=JsonSerializer.Deserialize<ProfileValues>(request.GetProperty("ac"),Backend.Program.Json)??throw new InvalidDataException("Missing AC values");var dc=JsonSerializer.Deserialize<ProfileValues>(request.GetProperty("dc"),Backend.Program.Json)??throw new InvalidDataException("Missing DC values");ac.Validate();dc.Validate();bool split=request.GetProperty("split").GetBoolean();Replace(target with{Split=split,Ac=split?ac:target.Ac,Dc=split?dc:ac,AcInitialized=target.AcInitialized||split,ModifiedUtc=DateTimeOffset.UtcNow});break;
                default:throw new InvalidDataException("Unknown profile operation");
            }
        }
        catch(Exception failure){document=before;powerAc=oldAc;RestoreOwnedFile(written,failure);error=failure.Message;PublishFailure(failure);throw;}
        await Commit(before,oldContext,oldAc,"explicit-"+operation);
    }
    internal async Task RefreshPowerSource()
    {if(!Available)return;bool ac=onAc();if(ac==powerAc)return;var before=document;string oldContext=context;bool oldAc=powerAc;powerAc=ac;await Commit(before,oldContext,oldAc,"power-source");}
    internal string Read()=>Volatile.Read(ref status);
    private void Publish()
    {
        var ordered=document.Games.OrderBy(entry=>string.IsNullOrEmpty(entry.Path)?0:1).ThenBy(entry=>entry.Name,StringComparer.CurrentCultureIgnoreCase).Prepend(document.Global);
        var entries=ordered.Select(entry=>new{entry.Id,entry.Name,entry.Path,processPath=string.IsNullOrEmpty(entry.Path)?null:entry.Path,entry.Use,entry.Split,entry.Ac,entry.Dc,effectiveAc=ProfileResolution.Resolve(document,entry,true),effectiveDc=ProfileResolution.Resolve(document,entry,false),entry.ModifiedUtc,entry.UseSharedPolicy,entry.AcInitialized,entry.LocalGroups});
        var value=new{onAc=powerAc,currentGame=game,currentSession,activeId=Active.Id,selection=document.Scope!.Selection,manualOverride=document.Scope.ManualHold!=null&&runtime.Alive(document.Scope.ManualHold),entries,flags=document.Flags,snapshot=SnapshotXml(),error};
        Volatile.Write(ref status,JsonSerializer.Serialize(value,Backend.Program.Json));
    }
    private string SnapshotXml()
    {
        XElement Profile(ProfileEntry entry)
        {
            var dc=ProfileResolution.Resolve(document,entry,false);var ac=ProfileResolution.Resolve(document,entry,true);var node=new XElement("GameProfile",new XElement("GameId",new XElement("Name",entry.Name),new XElement("Path",entry.Path)),new XElement("Use",entry.Use),new XElement("PowerSourceSplit",entry.Split));
            void Side(ProfileValues v,string suffix){void Add(string key,object value)=>node.Add(new XElement(key+suffix,value));Add("TDP",v.Pl1);Add("TDPBoostEnabled",v.SeparateEnabled);Add("TDPBoostFPPTWatts",v.Pl2);Add("CPUBoost",v.Boost>0);Add("FpsCapMode",v.Fps.Provider=="Intel"?1:0);Add("FPSLimit",v.Fps.Provider=="RTSS"&&v.Fps.Enabled?v.Fps.Value:0);Add("IntelFpsTier",v.Fps.Provider=="Intel"&&v.Fps.Enabled?v.Fps.Value:0);Add("OSPowerMode",v.PowerMode);
                if(v.Display is {} display)
                {
                    if(display.Colors is {} color){Add("IntelColorSaturation",color.Saturation);Add("IntelColorHue",color.Hue);Add("IntelDisplayContrast",color.Contrast);Add("IntelDisplayBrightness",color.Brightness);Add("IntelDisplayGamma",color.Gamma);}
                    foreach(var pair in new[]{("IntelAdaptiveSharpness",display.Sharpness),("IntelFrameGeneration",display.FrameGeneration),("IntelLowLatency",display.LowLatency),("IntelFrameSync",display.FrameSync),("IntelVrr",display.Vrr),("IntelVrrMode",display.VrrMode),("IntelScalingMode",display.ScalingMode),("IntelScalingMethod",display.ScalingMethod)})if(pair.Item2.HasValue)Add(pair.Item1,pair.Item1=="IntelVrrMode"?(pair.Item2.Value==1?1:2):pair.Item2.Value);
                    if(display.Mode is {} mode){Add("Resolution",mode.Width+"x"+mode.Height);Add("RefreshRate",mode.Hz);}
                }}
            Side(dc,"");Side(ac,"_Plugged");node.Add(new XElement("OverlayLevel",(powerAc?ac:dc).Overlay));return node;
        }
        return new XElement("ArrayOfGameProfile",document.Games.Prepend(document.Global).Select(Profile)).ToString(SaveOptions.DisableFormatting);
    }
    public async ValueTask DisposeAsync(){stopping.Cancel();await worker;stopping.Dispose();Gate.Dispose();}
}
