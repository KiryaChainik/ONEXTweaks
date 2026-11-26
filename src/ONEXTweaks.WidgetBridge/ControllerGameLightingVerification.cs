// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using ONEXTweaks.Power;
using ONEXTweaks.Panel;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerGameLightingVerification
{
 internal static async Task<int> Models(string file){
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}string first=Guid.NewGuid().ToString("N"),second=Guid.NewGuid().ToString("N");var lease=new GameLightingLease();
  try{
   Check(lease.Update(first,1,255,30,90,0)&&lease.Active,"First game color not applied");lease.Applied(0);
   Check(!lease.Update(first,1,255,30,90,500)&&lease.Expires==5500,"Heartbeat rewrote unchanged color or failed to renew");
   Check(!lease.Update(first,1,10,20,30,600)&&lease.Dirty,"Rate limit dropped pending color");Check(lease.Update(first,1,10,20,30,1000),"Pending rate-limited color never applied");lease.Applied(1000);
   Check(!lease.Expire(5999)&&lease.Expire(6000)&&!lease.Active,"Lease expiry boundary wrong");
   Check(lease.Update(first,1,10,20,30,6100),"Same-color resume failed after expiry");lease.Applied(6100);Check(lease.End(first,1)&&!lease.Active,"Explicit release failed");
   Check(lease.Update(first,1,10,20,30,7200),"Same-color resume after overlay pause failed");lease.Manual();Check(!lease.Update(first,1,100,100,100,7300)&&!lease.Active,"Manual LED setting lost priority");lease.Disconnect();Check(!lease.Update(first,1,100,100,100,7400),"Reconnect bypassed manual suppression");
   Check(lease.Update(second,2,7,8,9,7500)&&lease.Id==second,"Deliberate new profile/session did not rearm");lease.Applied(7500);
   Check(!lease.Update(first,1,250,250,250,9000)&&lease.Id==second&&lease.Red==7,"Late old session overwrote new game color");Check(!lease.End(first,1)&&lease.Active,"Old cleanup stopped new profile lighting");
   foreach(var invalid in new[]{new ControllerGameLightingRequest("update","bad",1),new("update",first,0)}){bool rejected=false;try{lease.Update(invalid.LeaseId,invalid.Epoch,0,0,0,0);}catch(ArgumentException){rejected=true;}Check(rejected,"Invalid session accepted");}
   bool rejectedColor=false;try{lease.Update(second,2,256,0,0,9000);}catch(ArgumentException){rejectedColor=true;}Check(rejectedColor,"Unbounded RGB accepted");
   var policy=new ControllerPolicy(VirtualTarget:"DualShock4",ForwardGameLighting:true);var serialized=JsonSerializer.Serialize(policy,Backend.Program.Json);Check(JsonSerializer.Deserialize<ControllerPolicy>(serialized,Backend.Program.Json)!.ForwardGameLighting&&!serialized.Contains("lightingContext"),"Saved opt-in/context isolation failed");Check(!JsonSerializer.Serialize(new ControllerPolicy(),Backend.Program.Json).Contains("forwardGameLighting"),"Legacy profile was eagerly changed");
   using(var closed=new PowerChannel()){closed.Dispose();bool refused=false;try{await closed.GameLightingAsync("end",first,1,0,0,0);}catch(ObjectDisposedException){refused=true;}Check(refused,"Disposed channel could reconnect during old actor cleanup");}
   var requests=new System.Collections.Concurrent.ConcurrentQueue<ControllerGameLightingRequest>();var actor=new ControllerGameLighting(r=>{requests.Enqueue(r);return Task.FromResult(new ControllerGameLightingReply(true,r.Operation=="update",false,r.Operation=="update"?"Active":"Off"));});
   async Task Wait(Func<bool> condition){for(int n=0;n<100&&!condition();n++)await Task.Delay(20);Check(condition(),"Actor request deadline");}
   actor.Color(1,2,3);await Task.Delay(120);Check(requests.IsEmpty,"Disabled actor forwarded host RGB");
   actor.Configure(true,false,"one");actor.Color(10,20,30);await Wait(()=>requests.Any(r=>r.Operation=="update"));var a=requests.First(r=>r.Operation=="update");Check(a.Red==10&&a.Green==20&&a.Blue==30,"Callback RGB changed");
   actor.Configure(true,true,"one");await Wait(()=>requests.Any(r=>r.Operation=="end"&&r.LeaseId==a.LeaseId));
   actor.Configure(true,false,"one");await Wait(()=>requests.Count(r=>r.Operation=="update"&&r.LeaseId==a.LeaseId)>=2);
   actor.Configure(true,false,"two");await Task.Delay(150);int count=requests.Count(r=>r.Operation=="update");await Task.Delay(150);Check(requests.Count(r=>r.Operation=="update")==count,"New profile reused previous game color");actor.Color(40,50,60);await Wait(()=>requests.Any(r=>r.Operation=="update"&&r.LeaseId!=a.LeaseId));
   var b=requests.Last(r=>r.Operation=="update");Check(b.Epoch>a.Epoch,"Session epoch did not advance");await actor.Finish();Check(requests.Any(r=>r.Operation=="end"&&r.LeaseId==b.LeaseId),"Dispose did not release active override");
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,ownerPreferenceWrites=0,physicalLedWrites=0},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message},Backend.Program.Json));return 2;}
 }
 internal static async Task<int> Native(string file){
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}string reports=Path.GetDirectoryName(file)!,homeFile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"ONEXTweaks","PowerAgent","home-oem-settings.json"),journal=Path.Combine(Path.GetDirectoryName(homeFile)!,"game-lighting-lease.json"),phase=Path.Combine(reports,"led341-native-stage.json"),marker=Path.Combine(reports,"led341-agent-restarted.json"),run=Guid.NewGuid().ToString("N");byte[] original=await File.ReadAllBytesAsync(homeFile);PowerChannel? power=null;ControllerOutputDevice? output=null;ControllerDs4HidReader? hid=null;string? error=null;bool actualFeedback=false,agentRestart=false,agentApi=false,expiry=false,manualApi=false;
  async Task Wait(Func<bool> ready,string why,int attempts=150){for(int n=0;n<attempts;n++){if(ready())return;await Task.Delay(20);}throw new IOException(why);}
  async Task<System.Text.Json.Nodes.JsonObject> Home(){var reply=await power!.ReadHomeAsync();if(!reply.Ok||reply.Home==null)throw new IOException("Current owner lighting unavailable");return reply.Home;}
  string Settings(System.Text.Json.Nodes.JsonObject home){
   var light=home["lighting"]!.AsObject();var fan=home["fan"]!.AsObject();
   return new System.Text.Json.Nodes.JsonObject{{"cpuClockMhz",home["cpuClockMhz"]!.DeepClone()},{"lightingEnabled",light["enabled"]!.DeepClone()},{"zones",light["zones"]!.DeepClone()},{"fanPreset1",fan["preset1"]!.DeepClone()},{"fanPreset2",fan["preset2"]!.DeepClone()}}.ToJsonString();
  }
  bool Active(System.Text.Json.Nodes.JsonObject home)=>home["lighting"]?["gameFeedback"]?["active"]?.GetValue<bool>()==true;
  try{
   Check(ControllerNative.Slots().SequenceEqual(new[]{0})&&ControllerNative.Read(0,out var source)&&ControllerNative.Neutral(source),"Protect owner physical controller");
   power=new();var home=await Home();agentApi=true;var light=home["lighting"]!.AsObject();Check(power.SupportsGameLighting&&light["gameFeedback"]?["supported"]?.GetValue<bool>()==true,"Updated Agent capability missing");
   Check(light["available"]!.GetValue<bool>()&&light["enabled"]!.GetValue<bool>()&&!light["rawCalibrationBypass"]!.GetValue<bool>()&&light["zones"]!.AsArray().Any(z=>z!["enabled"]!.GetValue<bool>()),"Protect owner lighting Off/calibration: native color writes skipped");
   string first=Guid.NewGuid().ToString("N");int firstEpoch=ControllerGameLighting.NextEpoch();var reply=await power.GameLightingAsync("update",first,firstEpoch,35,60,95);Check(reply.Ok&&reply.LightingFeedback?["active"]?.GetValue<bool>()==true&&File.Exists(journal),"Temporary native override not acknowledged");
   byte[] current=await File.ReadAllBytesAsync(homeFile);Check(original.SequenceEqual(current),"Game color overwrote saved owner settings");
   var manual=await power.SetLightingAsync(Settings(await Home()));Check(manual.Ok&&!Active(manual.Home!)&&!File.Exists(journal),"Manual same-current settings did not restore/cancel game override");manualApi=true;
   reply=await power.GameLightingAsync("update",first,firstEpoch,200,30,40);Check(reply.LightingFeedback?["suppressed"]?.GetValue<bool>()==true&&!File.Exists(journal),"Current game lease overrode manual lighting");
   string second=Guid.NewGuid().ToString("N");int secondEpoch=ControllerGameLighting.NextEpoch();reply=await power.GameLightingAsync("update",second,secondEpoch,70,50,110);Check(reply.LightingFeedback?["active"]?.GetValue<bool>()==true,"New explicit lighting session did not rearm");
   await Task.Delay(6200);home=await Home();Check(!Active(home)&&!File.Exists(journal)&&home["lighting"]!["gameFeedback"]!["status"]!.GetValue<string>()=="Expired","Independent5s timer did not restore saved lighting");expiry=true;
   var requests=new System.Collections.Concurrent.ConcurrentQueue<(ControllerGameLightingRequest Request,ControllerGameLightingReply Reply)>();var channel=power;
   output=new(0,()=>new(),"DualShock4",async request=>{var r=await ControllerGameLighting.Send(channel,request);requests.Enqueue((request,r));return r;});hid=await ControllerDs4HidReader.OpenOwned(output.Serial!);output.ConfigureLighting(true,false,"owned-native");
   hid.SendOutput(2,0,0,45,70,100);await Wait(()=>requests.Any(r=>r.Request.Red==45&&r.Request.Green==70&&r.Request.Blue==100&&r.Reply.Active),"Actual DS4 RGB callback did not reach OEM temporary API; callbacks="+output.RgbFeedbackCount+"; actor="+output.LightingStatus);actualFeedback=true;Check(true,"Native host callback path");
   output.ConfigureLighting(true,true,"owned-native");await Wait(()=>requests.Any(r=>r.Request.Operation=="end"),"Overlay pause did not release lighting");home=await Home();Check(!Active(home)&&!File.Exists(journal),"Overlay pause did not restore latest owner colors");
   output.ConfigureLighting(true,false,"owned-native");int updates=requests.Count(r=>r.Request.Operation=="update");await Wait(()=>requests.Count(r=>r.Request.Operation=="update")>updates,"Game lighting did not resume");Check(Active(await Home()),"Resumed callback not applied");
   manual=await power.SetLightingAsync(Settings(await Home()));Check(manual.Ok,"Current same-settings manual edit failed");await Wait(()=>requests.Any(r=>r.Reply.Suppressed),"Actual actor failed to observe manual LED priority");int count=requests.Count(r=>r.Request.Operation=="update");await Task.Delay(1400);Check(requests.Count(r=>r.Request.Operation=="update")==count,"Suppressed actor kept renewing game override");
   hid.Dispose();hid=null;output.Dispose();output=null;await Task.Delay(300);
   string crash=Guid.NewGuid().ToString("N");reply=await power.GameLightingAsync("update",crash,ControllerGameLighting.NextEpoch(),55,85,115);Check(reply.LightingFeedback?["active"]?.GetValue<bool>()==true&&File.Exists(journal),"Restart recovery journal not armed");
   await File.WriteAllTextAsync(phase,JsonSerializer.Serialize(new{run,stage="crash-armed",utc=DateTimeOffset.UtcNow},Backend.Program.Json));
   bool restarted=false;for(int n=0;n<400;n++){try{using var stamp=JsonDocument.Parse(await File.ReadAllTextAsync(marker));if(stamp.RootElement.GetProperty("run").GetString()==run){restarted=true;break;}}catch{}await Task.Delay(50);}Check(restarted,"Coordinator did not confirm own Agent restart");agentRestart=true;
   home=null!;for(int n=0;n<50;n++){try{home=await Home();if(!Active(home)&&!File.Exists(journal))break;}catch{}await Task.Delay(100);}
   Check(home!=null&&!Active(home)&&!File.Exists(journal)&&home["lighting"]!["gameFeedback"]!["error"]==null,"Actual Agent restart failed to restore saved colors");
   current=await File.ReadAllBytesAsync(homeFile);Check(original.SequenceEqual(current),"Manual/current restore changed saved owner bytes");
  }catch(Exception e){error=e.Message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>",StringComparison.OrdinalIgnoreCase);}
  finally{
   hid?.Dispose();output?.Dispose();power?.Dispose();await Task.Delay(500);
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=error==null,checks,error,actualAgentApi=agentApi,actualDs4RgbCallback=actualFeedback,independentExpiry=expiry,manualSameCurrentSettingsApi=manualApi,actualAgentProcessRestart=agentRestart,ownerPreferenceRollback=false,opticalColorOrGameTested=false,physicalZoneReadbackAvailable=false,ownerPhysicalInputTested=false},Backend.Program.Json));
  }
  return error==null?0:2;
 }

}
