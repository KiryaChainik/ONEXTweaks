// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ONEXTweaks.WidgetBridge;
internal static class PerformanceManagerVerification
{
    internal static async Task<int> Run(string report)
    {
        string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"qa-performance-model-"+Guid.NewGuid().ToString("N"));
        string folder=Path.Combine(root,"Reports"),file=Path.Combine(root,"performance-profiles.json");Directory.CreateDirectory(folder);
        var checks=new List<string>();string? error=null;PerformanceProfiles? manager=null;
        var initial=new ProfileValues(11,18,0,0,0,1,new("RTSS",false,60),0,0,false);
        var dc=initial with{Pl1=9,Pl2=16};ProfileValues live=initial;
        bool ac=true,failNextApply=false;int actuatorCalls=0;ProfileSession? current=null;
        var living=new HashSet<(int,long)>();var candidates=new List<ProfileCandidate>();
        bool Alive(ProfileSession process)=>living.Contains((process.ProcessId,process.StartedUtcTicks));
        int maintenanceCalls=0;var runtime=new PerformanceProfileRuntime(()=>current,Alive,()=>candidates.Where(candidate=>Alive(candidate.Session)).ToArray(),MaintainCpu:policy=>{maintenanceCalls++;if(live.CpuTuning!=policy)live=live with{CpuTuning=policy};return Task.CompletedTask;});
        Task Apply(ProfileValues value){actuatorCalls++;if(failNextApply){failNextApply=false;throw new IOException("Injected actuator failure");}live=value;return Task.CompletedTask;}
        Task<PerformanceProfiles> Open()=>PerformanceProfiles.Create(folder,()=>Task.FromResult(live),Apply,()=>null,()=>ac,runtime,startWorker:false);
        void Check(bool result,string name){if(!result)throw new IOException(name);checks.Add(name);}
        async Task Change(object request){using var json=JsonDocument.Parse(JsonSerializer.Serialize(request,Backend.Program.Json));await manager!.Mutate(json.RootElement);}
        ProfileDocument Store()=>JsonSerializer.Deserialize<ProfileDocument>(File.ReadAllText(file),Backend.Program.Json)!;
        string Active(){using var state=JsonDocument.Parse(manager!.Read());return state.RootElement.GetProperty("activeId").GetString()!;}
        try
        {
            var flags=PerformanceProfiles.Groups.ToDictionary(group=>group,_=>true);flags["TDP"]=false;
            var legacy=new ProfileEntry("legacy","Legacy QA",@"C:\ONEXTweaksQA\Legacy.exe",false,true,initial with{Pl1=30,Pl2=31},dc with{Pl1=20,Pl2=21},DateTimeOffset.UnixEpoch);
            var document=new ProfileDocument(3,new("global","global","",false,true,initial,dc,DateTimeOffset.UnixEpoch),[legacy],flags);
            var old=JsonNode.Parse(JsonSerializer.Serialize(document,Backend.Program.Json))!.AsObject();old.Remove("scope");old["flags"]!.AsObject().Remove("ScreenProfile");
            old["global"]!.AsObject().Remove("useSharedPolicy");foreach(var entry in old["games"]!.AsArray())entry!.AsObject().Remove("useSharedPolicy");
            File.WriteAllText(file,old.ToJsonString());
            manager=await Open();await manager.Poll();var migrated=Store();
            Check(migrated.Schema==6&&migrated.Global.Ac==initial&&migrated.Global.Dc==dc,"Legacy migration preserves exact Global AC/DC values");
            Check(migrated.Games.Single().Ac==legacy.Ac&&migrated.Games.Single().Dc==legacy.Dc&&!migrated.Flags["TDP"]&&migrated.Games.Single().UseSharedPolicy,"Legacy game values and shared policy are retained");
            Check(live==initial&&actuatorCalls==0,"Matching startup does not write the actuator");
            await Change(new{operation="create",name="Manual QA"});string manual=Active();
            Check(manual!="global"&&Store().Games.Single(entry=>entry.Id==manual).Path==""&&!Store().Games.Single(entry=>entry.Id==manual).UseSharedPolicy,"Manual profile is created and immediately selected");
            live=live with{Pl1=15,Pl2=22,SeparatePl2=true};await manager.Record("TDP");
            Check(Store().Games.Single(entry=>entry.Id==manual).Dc.Pl1==15&&Store().Global.Ac.Pl1==11,"New Manual autosaves to itself despite a legacy shared flag");
            await Change(new{operation="split",enabled=true});live=live with{Pl1=16};await manager.Record("TDP");
            ac=false;await manager.RefreshPowerSource();Check(live.Pl1==15,"Manual AC changes do not overwrite its DC side");
            live=live with{Pl1=14};await manager.Record("TDP");ac=true;await manager.RefreshPowerSource();Check(live.Pl1==16&&Store().Games.Single(entry=>entry.Id==manual).Dc.Pl1==14,"DC autosave and AC restore are independent");
            var process=new ProfileSession(101,638000000000000001,@"C:\ONEXTweaksQA\GameA.exe","GameA");current=process;living.Add((process.ProcessId,process.StartedUtcTicks));candidates.Add(new(process,true));
            await Change(new{operation="create",processId=101,startedUtcTicks="638000000000000001"});string app=Active();
            Check(Store().Games.Single(entry=>entry.Id==app).Name=="GameA"&&Store().Games.Single(entry=>entry.Id==app).Use,"Process profile uses its real candidate name and becomes active");
            live=live with{Pl1=21,Pl2=25};await manager.Record("TDP");
            Check(Store().Games.Single(entry=>entry.Id==app).Dc.Pl1==21&&Store().Global.Ac.Pl1==11,"New Per-app autosaves without overwriting Global");
            await Change(new{operation="select",id=manual});await manager.Poll();Check(Active()==manual&&live.Pl1==16,"Manual selection overrides the live app");
            current=null;await manager.Poll();Check(Active()==manual,"Ordinary focus loss preserves the live session override");
            current=process;await manager.DisposeAsync();manager=await Open();live=initial;await manager.Poll();Check(Active()==manual&&live.Pl1==16,"Manual hold and saved values survive a helper reload");
            living.Remove((101,process.StartedUtcTicks));var restarted=process with{StartedUtcTicks=process.StartedUtcTicks+1};current=restarted;living.Add((101,restarted.StartedUtcTicks));candidates.Add(new(restarted,true));
            await manager.Poll();Check(Active()==app&&live.Pl1==21,"A restarted process with reused PID activates its own saved profile");
            string saved=File.ReadAllText(file);bool rejected=false;
            try{await Change(new{operation="create",processId=101,startedUtcTicks="638000000000000001"});}catch(IOException){rejected=true;}
            Check(rejected&&File.ReadAllText(file)==saved,"Stale candidate session is rejected without a file change");
            rejected=false;try{await Change(new{operation="create",processId=101,startedUtcTicks="638000000000000002"});}catch(InvalidOperationException){rejected=true;}
            Check(rejected&&File.ReadAllText(file)==saved,"Duplicate process binding is rejected without a file change");
            failNextApply=true;rejected=false;try{await Change(new{operation="select",id="global"});}catch(IOException){rejected=true;}
            Check(rejected&&Active()==app&&live.Pl1==21&&File.ReadAllText(file)==saved,"Failed actuator transition preserves active profile, values and file");
            Directory.CreateDirectory(file+".tmp");rejected=false;
            try{await Change(new{operation="select",id="global"});}catch(Exception failure) when(failure is IOException or UnauthorizedAccessException){rejected=true;}
            Directory.Delete(file+".tmp");
            Check(rejected&&Active()==app&&live.Pl1==21&&File.ReadAllText(file)==saved,"Failed persistence after apply rolls hardware back as well as selection");
            await Change(new{operation="keep",id=app});await manager.Poll();Check(Active()=="global"&&Store().Games.Any(entry=>entry.Id==app),"Keep It returns Global and retains the saved app profile");
            await Change(new{operation="create",name="Alpha QA"});await Change(new{operation="create",name="Zulu QA"});
            using(var state=JsonDocument.Parse(manager.Read()))
            {
                var entries=state.RootElement.GetProperty("entries").EnumerateArray().ToArray();var manualNames=entries.Where(entry=>entry.GetProperty("id").GetString()!="global"&&entry.GetProperty("processPath").ValueKind==JsonValueKind.Null).Select(entry=>entry.GetProperty("name").GetString()!).ToArray();
                Check(entries[0].GetProperty("id").GetString()=="global"&&manualNames.SequenceEqual(manualNames.OrderBy(name=>name,StringComparer.CurrentCultureIgnoreCase)),"Picker order is Global then alphabetic Manual profiles");
                Check(entries.Skip(1).SkipWhile(entry=>entry.GetProperty("processPath").ValueKind==JsonValueKind.Null).All(entry=>entry.GetProperty("processPath").ValueKind==JsonValueKind.String),"Per-app entries stay below the Manual group");
            }
            await Change(new{operation="delete",id=manual});Check(Store().Games.All(entry=>entry.Id!=manual)&&Store().Games.Any(entry=>entry.Id==app),"Delete removes only the requested saved profile");
            rejected=false;try{await Change(new{operation="delete",id="global"});}catch(InvalidOperationException){rejected=true;}Check(rejected,"Global cannot be deleted");
            await Change(new{operation="automatic"});Check(Active()==app&&Store().Scope!.Selection=="auto"&&Store().Scope!.ManualHold==null,"Returning to automatic scope resolves the live process without deleting profiles");
            current=null;await manager.Poll();Check(Active()=="global","Automatic scope returns to Global after the process context ends");
            Check(Store().Flags["ScreenProfile"]&&Store().Global.Ac.ScreenProfile==null,"Legacy screen behavior stays unbound while the new policy flag is added");
            await Change(new{operation="create",name="Screen binding QA"});string screenManual=Active();await Change(new{operation="screen",screenProfileId="screen-qa"});
            Check(live.ScreenProfile=="screen-qa"&&Store().Games.Single(entry=>entry.Id==screenManual).Ac.ScreenProfile==null&&Store().Games.Single(entry=>entry.Id==screenManual).Dc.ScreenProfile=="screen-qa","Legacy reference metadata uses the shared DC side without replacing archived AC");
            await Change(new{operation="select",id="global"});Check(live.ScreenProfile==null,"Leaving a linked profile clears its temporary screen override");
            await Change(new{operation="select",id=screenManual});Check(live.ScreenProfile=="screen-qa","Selecting the saved profile restores its screen reference");
            await Change(new{operation="screen",screenProfileId=(string?)null});Check(live.ScreenProfile==null&&Store().Games.Single(entry=>entry.Id==screenManual).Ac.ScreenProfile==null,"Follow Display removes the link without deleting the preset");
            var cpuPolicy=new CpuTuningPolicy(PerfFrequency:3500,EffEpp:70,EffAwakeMinimum:50);live=live with{CpuTuning=cpuPolicy};await manager.Record("CPUBoost");
            Check(Store().Games.Single(entry=>entry.Id==screenManual).Ac.CpuTuning==null&&Store().Games.Single(entry=>entry.Id==screenManual).Dc.CpuTuning==cpuPolicy&&Store().Global.Ac.CpuTuning==null,"Shared DC advanced CPU policy autosaves without changing Global or archived AC");
            await Change(new{operation="select",id="global"});Check(live.CpuTuning==null,"Global without an advanced policy releases the CPU override");
            await Change(new{operation="select",id=screenManual});Check(live.CpuTuning==cpuPolicy,"Selecting the saved profile restores the full advanced CPU policy");
            await Change(new{operation="split",enabled=true});live=live with{CpuTuning=cpuPolicy with{EffEpp=40}};await manager.Record("CPUBoost");ac=false;await manager.RefreshPowerSource();
            Check(live.CpuTuning==cpuPolicy&&Store().Games.Single(entry=>entry.Id==screenManual).Ac.CpuTuning?.EffEpp==40,"Advanced CPU AC and DC policies remain independent");
            int priorMaintenance=maintenanceCalls;int priorPl1=live.Pl1;live=live with{CpuTuning=null};await manager.Poll();Check(maintenanceCalls>priorMaintenance&&live.CpuTuning==cpuPolicy&&live.Pl1==priorPl1,"An unchanged selected profile reconciles CPU drift without changing TDP or profile scope");
        }
        catch(Exception failure){error=failure.ToString();}
        finally{if(manager!=null)await manager.DisposeAsync();Directory.Delete(root,true);}
        await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed=error==null,checks,error,modelOnly=true,hardwareWrites=0,realProcessTested=false,liveOwnerStoreTouched=false},Backend.Program.Json));
        return error==null?0:1;
    }
}
