// SPDX-License-Identifier: AGPL-3.0-only
using ONEXTweaks.Panel;
namespace ONEXTweaks.WidgetBridge;
internal sealed record ControllerGameLightingRequest(string Operation,string LeaseId,int Epoch,byte Red=0,byte Green=0,byte Blue=0);
internal sealed record ControllerGameLightingReply(bool Supported,bool Active,bool Suppressed,string Status);
internal sealed class ControllerGameLighting:IDisposable
{
 private static int epochs;internal static int NextEpoch()=>Interlocked.Increment(ref epochs);private readonly Func<ControllerGameLightingRequest,Task<ControllerGameLightingReply>> send;private readonly object gate=new();private readonly CancellationTokenSource stop=new();private readonly Task worker;
 private bool enabled,paused,hasColor,disposed;private string context="",id=Guid.NewGuid().ToString("N");private int epoch=NextEpoch();private byte red,green,blue;private string status="Off";
 internal string Status{get{lock(gate)return status;}}
 internal ControllerGameLighting(Func<ControllerGameLightingRequest,Task<ControllerGameLightingReply>> sink){send=sink;worker=Task.Run(Run);}
 internal static async Task<ControllerGameLightingReply> Send(PowerChannel power,ControllerGameLightingRequest request){
  var reply=await power.GameLightingAsync(request.Operation,request.LeaseId,request.Epoch,request.Red,request.Green,request.Blue);
  var state=reply.LightingFeedback;if(!reply.Ok||state==null)return new(false,false,false,reply.Error??"Hardware lighting feedback unavailable");
  return new(state["supported"]?.GetValue<bool>()==true,state["active"]?.GetValue<bool>()==true,state["suppressed"]?.GetValue<bool>()==true,state["error"]?.GetValue<string>()??state["status"]?.GetValue<string>()??"Unavailable");
 }
 internal void Configure(bool on,bool pause,string session){
  lock(gate){if(disposed)return;if(on!=enabled||session!=context){id=Guid.NewGuid().ToString("N");epoch=NextEpoch();context=session;hasColor=false;}enabled=on;paused=pause;if(!on)status="Off";else if(pause)status="Paused in ONEXTweaks";else if(!hasColor)status="Waiting for game color";}
 }
 internal void Color(byte r,byte g,byte b){lock(gate){if(disposed||!enabled)return;red=r;green=g;blue=b;hasColor=true;}}
 private async Task Run(){
  string? active=null;int activeEpoch=0;string? blocked=null;long due=0;
  try{while(!stop.IsCancellationRequested){
   ControllerGameLightingRequest? request=null;
   lock(gate){bool wants=enabled&&!paused&&hasColor&&blocked!=id;if(active!=null&&(!wants||active!=id)){request=new("end",active,activeEpoch);active=null;due=0;}else if(wants&&Environment.TickCount64>=due){request=new("update",id,epoch,red,green,blue);due=Environment.TickCount64+1000;}}
   if(request!=null)try{
    var reply=await send(request);due=Environment.TickCount64+1000;
    lock(gate){if(request.Operation=="update"&&request.LeaseId==id){status=reply.Status;if(reply.Suppressed||!reply.Supported)blocked=id;if(reply.Active){active=request.LeaseId;activeEpoch=request.Epoch;}}}
   }catch{lock(gate){if(request.Operation=="update"&&request.LeaseId==id)status="Hardware lighting temporarily unavailable";}}
   await Task.Delay(100,stop.Token);
  }}catch(OperationCanceledException){}
  finally{if(active!=null)try{await send(new("end",active,activeEpoch));}catch{/* Privileged lease expires independently within5s or when the pipe closes. */}}
 }
 internal async Task Finish(){lock(gate){if(!disposed){disposed=true;enabled=false;stop.Cancel();}}await worker;}
 public void Dispose(){lock(gate){if(disposed)return;disposed=true;enabled=false;stop.Cancel();}_=worker.ContinueWith(_=>stop.Dispose(),TaskScheduler.Default);}
}
