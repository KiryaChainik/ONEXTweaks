// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.Text.Json;

namespace ONEXTweaks.WidgetBridge;

internal static class GameProfileRegressionVerification
{
    private sealed record Colour(int Saturation);
    private sealed class Capture : IControllerCaptureRuntime
    {
        public ControllerRuntimeState Read()=>new(false,false,0,-1,null,[],0);
        public void Policy(ControllerPolicy policy){}
        public Task Start()=>Task.CompletedTask;
        public Task Stop()=>Task.CompletedTask;
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }

    internal static async Task<int> Run(string output)
    {
        var checks=new List<string>();
        string root=Path.Combine(Path.GetTempPath(),"onex-game-profiles-"+Guid.NewGuid().ToString("N"));
        string reports=Path.Combine(root,"Reports");
        string performanceFile=Path.Combine(root,"performance-profiles.json");
        Directory.CreateDirectory(reports);
        var game=new ProfileSession(301,638998123450000001,Path.Combine(root,"Steam","Game.exe"),"Game");
        var other=game with{ProcessId=302,StartedUtcTicks=game.StartedUtcTicks+1,Path=Path.Combine(root,"Other.exe"),Name="Other"};
        var duplicate=game with{ProcessId=303,StartedUtcTicks=game.StartedUtcTicks+2};
        ProfileSession? current=null;
        bool liveGame=true;
        var baseline=new ProfileValues(12,13,0,0,0,1,new("RTSS",false,60),0,0,false);
        var quiet=baseline with{Pl1=15,Pl2=16};
        var fast=baseline with{Pl1=25,Pl2=26};
        var actual=quiet;
        PerformanceProfiles? model=null;
        string? error=null;
        void Check(bool condition,string name)
        {
            if(!condition)throw new InvalidDataException(name);
            checks.Add(name);
        }
        bool GameAlive(ProfileSession process)=>process==game&&liveGame;
        JsonDocument Command(object value)=>JsonDocument.Parse(JsonSerializer.Serialize(value,Backend.Program.Json));
        Task<PerformanceProfiles> Open()=>PerformanceProfiles.Create(reports,()=>Task.FromResult(actual),value=>
        {actual=value;return Task.CompletedTask;},()=>null,()=>false,new(()=>current,GameAlive,()=>liveGame?[new(game,true)]:[]),startWorker:false);
        try
        {
            var identity=new[]{game,other,duplicate}.ToDictionary(process=>process.ProcessId);
            ProfileSession? Identify(int pid)=>identity.GetValueOrDefault(pid);
            var apps=new []{new RtssApp(301,"Game.exe",60,16,1),new RtssApp(302,"Stale.exe",60,16,1),new RtssApp(999,"Exited.exe",60,16,1)};
            var candidates=ProfileSessions.MergeCandidates([303],apps,game,Identify,_=>true);
            Check(candidates.Length==1&&candidates[0].Game&&candidates[0].Session==game,
                "Minimized game remains a running candidate; stale RTSS PID and duplicate executable are excluded");
            var noHook=ProfileSessions.MergeCandidates([],[],game,Identify,_=>true);
            Check(noHook.Single().Session==game,"Retained game remains bindable before the RTSS hook");
            Check(ProfileSessions.MergeCandidates([],[],game with{StartedUtcTicks=1},Identify,_=>true).Length==0,"Reused foreground PID is not admitted");

            var global=new ProfileEntry("global","Global","",false,false,baseline,baseline,DateTimeOffset.UtcNow);
            var manual=new ProfileEntry("quiet","Quiet","",false,false,quiet,quiet,DateTimeOffset.UtcNow);
            var bound=new ProfileEntry("fast","Fast",other.Path,true,false,fast,fast,DateTimeOffset.UtcNow,false);
            var initial=new ProfileDocument(7,global,[manual,bound],PerformanceProfiles.Groups.ToDictionary(key=>key,_=>true),new("quiet","manual"));
            var oldFanPreset=new ScopedProfile<FanPreferences>("old-fan","Older fan",null,false,new(0),new(0),DateTimeOffset.UtcNow,PerformanceProfileId:bound.Id);
            var fanGlobal=oldFanPreset with{Id="global",Name="Global",PerformanceProfileId=null};
            var fanLegacy=new ScopedProfileDocument<FanPreferences>(1,fanGlobal,[oldFanPreset],"global","manual",null);
            var oldLink=bound with{Ac=bound.Ac with{FanProfile=oldFanPreset.Id},Dc=bound.Dc with{FanProfile=oldFanPreset.Id}};
            var migrated=FanProfiles.MigrateLegacyGameLinks(fanLegacy,initial with{Games=[oldLink]});
            Check(migrated.Profiles.Count==1&&migrated.Profiles[0].Id==oldFanPreset.Id&&migrated.Profiles[0].ProcessPath==other.Path&&migrated.Profiles[0].PerformanceProfileId==null,
                "Existing Fan curve is moved to a direct game binding");
            var removed=migrated with{Profiles=[]};
            Check(ReferenceEquals(FanProfiles.MigrateLegacyGameLinks(removed,initial with{Games=[oldLink]}),removed),
                "Fan migration does not recreate a deliberately deleted profile");
            var direct=oldFanPreset with{Id="new-fan",ProcessPath=other.Path,PerformanceProfileId=null};
            Check(FanProfiles.MigrateLegacyGameLinks(fanLegacy with{Profiles=[oldFanPreset,direct]},initial with{Games=[oldLink]}).Profiles.Count==2,
                "An existing direct Fan binding takes precedence over a legacy link");
            var acCurve=FanCurve.FromPoints([20,30,40,50,65,80,100]);
            var dcCurve=FanCurve.FromPoints([15,25,35,45,55,70,95]);
            var acFan=new FanPreferences(3,acCurve);
            var dcFan=new FanPreferences(3,dcCurve);
            var splitFan=oldFanPreset with{Split=true,Ac=acFan,Dc=dcFan};
            var migratedSplit=FanProfiles.MigrateLegacyGameLinks(fanLegacy with{Profiles=[splitFan]},initial with{Games=[oldLink]});
            var moved=migratedSplit.Profiles.Single();
            Check(moved.Split&&moved.Ac==acFan&&moved.Dc==dcFan,"Moving a Fan profile preserves both AC and DC curves");
            string fanWorkerReports=Path.Combine(root,"fan-worker","Reports");
            Directory.CreateDirectory(fanWorkerReports);
            File.WriteAllText(Path.Combine(fanWorkerReports,"..","fan-profiles.json"),JsonSerializer.Serialize(migratedSplit,Backend.Program.Json));
            var fanWrites=new System.Collections.Concurrent.ConcurrentQueue<FanPreferences>();
            ProfileSession? fanSession=null;
            bool onCharger=true;
            var fanReaders=new ProfileSessions(()=>[]);
            await using(var gateOwner=new FanProfiles(fanWorkerReports, new SemaphoreSlim(1,1),fanReaders,
                ()=>new(0),(value,_)=>fanWrites.Enqueue(value),_=>{},null,onAc:()=>Volatile.Read(ref onCharger),
                currentSession:()=>Volatile.Read(ref fanSession),independent:true))
            {
                async Task ExpectFan(FanPreferences expected,string reason)
                {
                    for(int n=0;n<100;n++)
                    {
                        if(fanWrites.LastOrDefault()==expected){Check(true,reason);return;}
                        await Task.Delay(30);
                    }
                    Check(false,reason);
                }
                await ExpectFan(new(0),"The independent Fan worker starts in Auto without a game");
                Volatile.Write(ref fanSession,other);
                await ExpectFan(acFan,"The independent Fan worker applies the saved AC curve on game entry");
                Volatile.Write(ref onCharger,false);
                await ExpectFan(dcFan,"The independent Fan worker applies the saved DC curve on cable removal");
                Volatile.Write(ref fanSession,null);
                await ExpectFan(new(0),"The independent Fan worker returns to Auto on game exit");
            }
            File.WriteAllText(performanceFile,JsonSerializer.Serialize(initial,Backend.Program.Json));
            model=await Open();
            JsonDocument Request(long stamp)=>Command(new{operation="create",processId=game.ProcessId,startedUtcTicks=stamp.ToString(CultureInfo.InvariantCulture)});
            bool rejected=false;
            using(var request=Request(5))try{model.ResolveLinkedProcess(request.RootElement);}catch(IOException){rejected=true;}
            Check(rejected,"Creation rejects a process that restarted after the picker opened");
            byte[] originalPerformance=File.ReadAllBytes(performanceFile);
            using var chosen=Request(game.StartedUtcTicks);
            var link=model.ResolveLinkedProcess(chosen.RootElement);
            var sessions=new ProfileSessions(()=>[]);
            var writes=0;
            await using(var fan=new FanProfiles(reports,model.Gate,sessions,()=>new(0),(_,_)=>writes++,_=>{},null,
                startWorker:false,onAc:()=>false,currentSession:()=>current))
            {
                fan.CreateForProcess(link);
                string fanId=fan.EditorId;
                fan.CreateForProcess(link);
                Check(fanId!="global"&&fan.EditorId==fanId&&writes==0,"Fan creates one editable app profile without changing hardware");
            }
            string stamp="outside";
            await using(var controller=new ControllerProfiles(reports,()=>stamp,()=>"global",()=>[],capture:new Capture(),
                defaultVirtualAvailable:()=>false,session:()=>current))
            {
                await controller.CreateForProcess(link);
                string controllerId=controller.EditingId;
                await controller.CreateForProcess(link);
                Check(controllerId!="global"&&controller.EditingId==controllerId&&controller.AppliedId=="global","Controller creates one app profile without selecting it for another process");
                stamp="game";current=game;controller.Refresh();
                Check(controller.AppliedId==controllerId,"Controller applies the profile to the matching game");
                stamp="outside";current=null;controller.Refresh();
                Check(controller.AppliedId=="global","Controller releases the app profile when the session ends");
            }
            Check(File.ReadAllBytes(performanceFile).SequenceEqual(originalPerformance)&&actual==quiet,
                "Creating Fan or Controller profiles makes no Performance changes");

            current=other;
            await model.Poll();
            Check(model.ActiveId=="fast"&&actual.Pl1==25,"Performance applies the game policy");
            await model.DisposeAsync();model=await Open();
            current=null;await model.Poll();
            Check(model.ActiveId=="quiet"&&actual.Pl1==15,"Performance restores the last manual policy across Bridge reopen");
            using(var command=Command(new{operation="select",id="global"}))await model.Mutate(command.RootElement);
            current=other;await model.Poll();current=null;await model.Poll();
            Check(model.ActiveId=="global"&&actual.Pl1==12,"Performance restores an explicitly selected Global profile");

            string colours=Path.Combine(root,"screen-colour.json");
            var globalColour=new ScopedProfile<Colour>("global","Global",null,false,new(50),new(50),DateTimeOffset.UtcNow,new(50));
            var manualColour=globalColour with{Id="warm",Name="Warm",Ac=new(60),Dc=new(60),Unified=new(60)};
            var gameColour=globalColour with{Id="game",Name="Game",ProcessPath=game.Path,Ac=new(80),Dc=new(80),Unified=new(80)};
            File.WriteAllText(colours,JsonSerializer.Serialize(new ScopedProfileDocument<Colour>(1,globalColour,[manualColour,gameColour],"warm","manual",null),Backend.Program.Json));
            var applied=new Colour(60);
            ScopedProfileCatalog<Colour> OpenColours()=>new(colours,()=>applied,value=>applied=value,_=>{},()=>false,()=>current,GameAlive,
                rememberManualFallback:true,sourceIndependent:true);
            var screen=OpenColours();current=game;screen.Tick();
            Check(screen.ActiveId=="game"&&applied.Saturation==80,"Screen colour applies its independent game profile");
            screen=OpenColours();current=null;liveGame=false;screen.Tick();
            Check(screen.ActiveId=="warm"&&applied.Saturation==60,"Screen colour restores the previous manual profile");
            using(var command=Command(new{operation="select",id="global"}))screen.Mutate(command.RootElement,_=>null);
            current=game;liveGame=true;screen.Tick();current=null;liveGame=false;screen.Tick();
            Check(screen.ActiveId=="global"&&applied.Saturation==50,"Screen colour restores Global when it was the last explicit choice");
        }
        catch(Exception failure){error=failure.ToString();}
        finally
        {
            if(model!=null)await model.DisposeAsync();
            Directory.Delete(root,true);
        }
        await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=error==null,checks,error,modelOnly=true},Backend.Program.Json));
        return error==null?0:1;
    }
}
