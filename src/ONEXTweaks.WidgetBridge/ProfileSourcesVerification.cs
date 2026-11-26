// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ProfileSourcesVerification
{
    internal static async Task<int> Run(string report)
    {
        string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"qa-sources-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"Reports"));
        string file=Path.Combine(root,"performance-profiles.json");var checks=new List<string>();string? error=null;PerformanceProfiles? manager=null;
        var value=new ProfileValues(11,20,0,0,0,1,new("RTSS",false,60),0,0,false,Display:new(){Mode=new(1920,1200,144)},FanProfile:"global");var live=value;bool ac=false,fail=false;int captures=0,applies=0;
        void Check(bool ok,string name){if(!ok)throw new IOException(name);checks.Add(name);}
        Task<PerformanceProfiles> Open()=>PerformanceProfiles.Create(Path.Combine(root,"Reports"),()=>{captures++;return Task.FromResult(live);},next=>{if(fail){fail=false;throw new IOException("Injected apply failure");}applies++;live=next;return Task.CompletedTask;},()=>null,()=>ac,new(()=>null,_=>false,()=>[]),startWorker:false);
        ProfileDocument Store()=>JsonSerializer.Deserialize<ProfileDocument>(File.ReadAllText(file),Backend.Program.Json)!;
        async Task Change(object request){using var doc=JsonDocument.Parse(JsonSerializer.Serialize(request,Backend.Program.Json));await manager!.Mutate(doc.RootElement);}
        Task Patch(string source,string key,object? edit,string id="global")=>Change(new{operation="source",id,source,key,value=edit});
        try
        {
            var original=new ProfileDocument(5,new("global","global","",false,false,value,value,DateTimeOffset.UnixEpoch),[],PerformanceProfiles.Groups.ToDictionary(group=>group,_=>true),new());File.WriteAllText(file,JsonSerializer.Serialize(original,Backend.Program.Json));manager=await Open();
            Check(Store().Schema==7&&Store().Global.Ac==value&&Store().Global.Dc==value,"Migration preserves original profile values");
            await Patch("dc","pl1",12);Check(Store().Global.Dc.Pl1==12&&Store().Global.Ac.Pl1==11&&live.Pl1==12,"Shared DC edit applies live and leaves AC archive untouched");
            ac=true;await manager.RefreshPowerSource();Check(live.Pl1==12,"Unsplit profile uses DC values while charging");
            await Change(new{operation="split",enabled=true});Check(Store().Global.AcInitialized&&Store().Global.Ac.Pl1==12,"First split initializes AC from current DC exactly once");
            int cc=captures,aa=applies;await Patch("dc","pl1",13);Check(Store().Global.Dc.Pl1==13&&live.Pl1==12&&captures==cc&&applies==aa,"Inactive DC saves without capture/apply or changing hardware");
            await Patch("ac","pl1",15);Check(live.Pl1==15&&Store().Global.Dc.Pl1==13,"Active AC edit changes only AC");
            await Patch("ac","pl2",26);await Patch("ac","separatePl2",true);Check(live.SeparateEnabled&&live.Pl2==26,"AC PL2 has its own remembered manual limit");
            await Patch("ac","separatePl2",false);Check(live.EffectivePl2==16&&Store().Global.Ac.Pl2==26,"Disabling PL2 applies PL1+1 and retains remembered manual value");
            await Patch("ac","separatePl2",true);Check(live.EffectivePl2==26,"Enabling PL2 restores the remembered value");
            await Change(new{operation="split",enabled=false});Check(live.Pl1==13&&Store().Global.Ac.Pl1==15&&Store().Global.Ac.Pl2==26,"Disabling split applies DC and retains AC settings");
            await Patch("dc","pl1",14);await Change(new{operation="split",enabled=true});Check(live.Pl1==15&&Store().Global.Ac.Pl2==26&&Store().Global.Dc.Pl1==14,"Re-enabling split restores old AC after shared edits");
            ac=false;await manager.RefreshPowerSource();Check(live.Pl1==14,"Model source switch applies saved DC");
            var patches=new (string Key,object? Value)[]{("boost",4),("scheduling",2),("clockMhz",0),("cpu:freq1",3500),("cpu:epp0",80),("powerMode",2),("fpsProvider","Intel"),("fpsValue",45),("fpsEnabled",true),("frameGeneration",2),("vrr",1),("vrrMode",1),("lowLatency",2),("frameSync",3),("mode",new{width=1280,height=800,hz=60}),("scalingMode",2),("scalingMethod",1),("fanProfile",FanPreferences.AutoId)};
            cc=captures;aa=applies;foreach(var patch in patches)await Patch("ac",patch.Key,patch.Value);
            var saved=Store().Global.Ac;Check(saved.Boost==4&&saved.CpuTuning?.PerfFrequency==3500&&saved.CpuTuning.EffEpp==80&&saved.PowerMode==2&&saved.Fps==new FpsPreferences("Intel",true,45)&&saved.Display?.FrameGeneration==2&&saved.Display.Mode==new DisplayModeValue(1280,800,60)&&saved.FanProfile==FanPreferences.AutoId,"All seven source groups persist their own values");
            Check(captures==cc&&applies==aa&&live.Pl1==14,"Entire inactive-source setup performs no hardware operations");
            ac=true;await manager.RefreshPowerSource();Check(live==ProfileResolution.Resolve(Store(),Store().Global,true),"Model AC transition applies the complete saved side");
            string before=File.ReadAllText(file);var actual=live;bool rejected=false;try{await Patch("ac","cpu:min1",101);}catch(InvalidDataException){rejected=true;}Check(rejected&&File.ReadAllText(file)==before&&live==actual,"Invalid source value is rejected without data/hardware changes");
            rejected=false;try{await Patch("ac","saturation",80);}catch(InvalidDataException){rejected=true;}Check(rejected&&File.ReadAllText(file)==before,"Screen color cannot be edited through Performance source patches");
            rejected=false;try{await Patch("ac","pl1",20,"not-active");}catch(InvalidOperationException){rejected=true;}Check(rejected&&File.ReadAllText(file)==before,"Stale profile identity is rejected");
            fail=true;rejected=false;try{await Patch("ac","pl1",16);}catch(IOException){rejected=true;}Check(rejected&&File.ReadAllText(file)==before&&live==actual,"Failed live source apply rolls saved state back");
            await manager.DisposeAsync();manager=null;manager=await Open();Check(Store().Global.Ac==saved&&Store().Global.Dc.Pl1==14&&Store().Global.AcInitialized,"Both source values and initialized AC survive manager reload");
        }
        catch(Exception failure){error=failure.ToString();}
        finally{if(manager!=null)await manager.DisposeAsync();}
        await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed=error==null,checks,error,modelOnly=true,hardwareTested=false,physicalPowerSourceTested=false},Backend.Program.Json));return error==null?0:1;
    }
}
