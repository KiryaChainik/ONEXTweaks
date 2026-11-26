using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
namespace ONEXTweaks.Power
{
 internal sealed partial class Hardware
 {
  private readonly GameLightingLease gameLighting=new GameLightingLease();
  private readonly Stopwatch gameLightingClock=Stopwatch.StartNew();
  private Timer gameLightingTimer;private bool gameLightingApplied,gameLightingRestore;private string gameLightingError;
  private static string GameLightingJournal{get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"game-lighting-lease.json");}}
  private void InitializeGameLightingRecovery()
  {
   // Existence is enough: recovery always reads current owner settings, never colors from a journal.
   gameLightingRestore=File.Exists(GameLightingJournal);gameLightingApplied=gameLightingRestore;
   gameLightingTimer=new Timer(GameLightingTick,null,500,500);
  }
  private void WriteGameLightingJournal()
  {
   string temp=GameLightingJournal+".tmp";File.WriteAllText(temp,Program.Json.Serialize(new{schema=1,temporaryOverride=true}));
   if(File.Exists(GameLightingJournal))File.Replace(temp,GameLightingJournal,null);else File.Move(temp,GameLightingJournal);
  }
  private void ClearGameLightingJournal(){if(File.Exists(GameLightingJournal))File.Delete(GameLightingJournal);gameLightingApplied=gameLightingRestore=false;gameLightingError=null;}
  private void RestoreGameLighting()
  {
   if(!gameLightingApplied&&!gameLightingRestore)return;
   try{
    var latest=Program.Json.Deserialize<HomeSettings>(File.ReadAllText(HomePath));ValidateHome(latest);
    NormalizeLighting(latest);
    ApplyLighting(latest);home=latest;homeLoaded=true;lastLightingWasRaw=false;ClearGameLightingJournal();
   }catch(Exception e){gameLightingRestore=true;gameLightingError=e.Message;}
  }
  private void ApplyGameLighting()
  {
   LoadHome();if(lastLightingWasRaw){gameLighting.Status="Paused for LED calibration";return;}
   if(!home.lightingEnabled||!home.zones.Any(z=>z.enabled)){gameLighting.Status="Saved lighting is Off";return;}
   var value=Program.Json.Deserialize<HomeSettings>(Program.Json.Serialize(home));
   foreach(var zone in value.zones){zone.mode=2;zone.breathing=false;zone.animationSpeed=0;zone.r=gameLighting.Red;zone.g=gameLighting.Green;zone.b=gameLighting.Blue;}
   if(!gameLightingApplied)WriteGameLightingJournal();gameLightingApplied=true;ApplyLighting(value);gameLighting.Applied(gameLightingClock.ElapsedMilliseconds);gameLightingError=null;
  }
  internal object GameLightingFeedback(string operation,string id,int epoch,int red,int green,int blue)
  {
   lock(SyncRoot){
    if(operation=="end"){gameLighting.End(id,epoch);if(!gameLighting.Active)RestoreGameLighting();return GameLightingState(id);}
    if(operation!="update")throw new ArgumentException("Unknown temporary lighting operation");
    if(gameLightingRestore){RestoreGameLighting();if(gameLightingRestore)return GameLightingState(id);}
    bool apply=gameLighting.Update(id,epoch,red,green,blue,gameLightingClock.ElapsedMilliseconds);
    if(apply)try{ApplyGameLighting();}catch(Exception e){gameLightingError=e.Message;gameLightingRestore=gameLightingApplied;gameLighting.End(id,epoch);RestoreGameLighting();gameLightingError=e.Message;}
    return GameLightingState(id);
   }
  }
  private object GameLightingState(string requestedId=null){return new{supported=true,active=gameLighting.Active&&gameLightingApplied,suppressed=gameLighting.SuppressedId==(requestedId??gameLighting.Id)&&!string.IsNullOrEmpty(requestedId??gameLighting.Id),status=gameLightingRestore?"Restoring saved lighting":gameLighting.Status,error=gameLightingError,temporary=true,savedSettingsWritten=false};}
  private void GameLightingTick(object ignored)
  {
   if(!Monitor.TryEnter(SyncRoot))return;
   try{
    if(homeDisposed)return;
    if(gameLighting.Expire(gameLightingClock.ElapsedMilliseconds))gameLightingRestore=gameLightingApplied;
    if(gameLightingRestore)RestoreGameLighting();
   }catch(Exception e){gameLightingError=e.Message;}finally{Monitor.Exit(SyncRoot);}
  }
  private void StopGameLighting(){gameLighting.Disconnect();gameLightingRestore=gameLightingApplied||gameLightingRestore;RestoreGameLighting();}
 }
}
