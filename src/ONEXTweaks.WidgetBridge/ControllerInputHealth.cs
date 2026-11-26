// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;
internal sealed record ControllerInputHealthState(DateTimeOffset CheckedUtc,string Mode,int PhysicalSourceCount,int VisibleXboxCount,int GamePlayer,bool OwnedSonyPresent,bool PhysicalHidden,bool HidHideActive,bool HidHideInverse,string[] Issues,bool Stale=false,string? Error=null);
internal static class ControllerInputHealth
{
 internal static ControllerSource[] CapturedSources(ControllerSource[] sources,string[] ids)=>sources.Where(source=>source.InstanceIds.Intersect(ids,StringComparer.OrdinalIgnoreCase).Any()).ToArray();
 internal static string Mode(ControllerRuntimeState state)=>state.Running?state.OutputTarget:"Hardware";
 internal static ControllerInputHealthState Evaluate(ControllerRuntimeState state,int sourceCount,int[] visible,bool ownedSony,bool physicalHidden,bool hideActive,bool inverse,string? error=null){
  var issues=new List<string>();string mode=Mode(state);
  if(error!=null)issues.Add("unavailable");
  else{
   if(sourceCount==0)issues.Add("sourceMissing");else if(sourceCount>1)issues.Add("extraPhysicalSource");
   if(inverse)issues.Add("inverseHideMode");
   if(state.Running){
    if(!physicalHidden)issues.Add("sourceNotHidden");
    if(mode=="Xbox360"){if(visible.Length==0)issues.Add("outputMissing");else if(visible.Length>1)issues.Add("duplicateXboxInput");}
    else if(mode=="DualShock4"){if(!ownedSony)issues.Add("sonyOutputMissing");if(visible.Length>0)issues.Add("xboxAlongsideSony");}
   }else{if(sourceCount==1&&visible.Length==0)issues.Add("hardwareHidden");if(visible.Length>1)issues.Add("extraVisibleInput");}
  }
  return new(DateTimeOffset.UtcNow,mode,sourceCount,error==null?visible.Length:-1,error==null&&mode!="DualShock4"&&visible.Length==1?visible[0]+1:0,ownedSony,physicalHidden,hideActive,inverse,issues.ToArray(),Error:error);
 }
 internal static ControllerInputHealthState? Snapshot(ControllerInputHealthState? health,ControllerRuntimeState now)=>health==null?null:health with{Stale=health.Mode!=Mode(now)||(DateTimeOffset.UtcNow-health.CheckedUtc).TotalSeconds>60};
 internal static async Task<ControllerInputHealthState> Check(string reports,IControllerCaptureRuntime runtime){
  var state=runtime.Read();int count=0;int[] visible=[];bool sony=false,blocked=false,active=false,inverse=false;
  try{
   var sources=ControllerNative.Sources();string[] sourceIds=[];
   if(state.Running){sourceIds=ControllerCaptureLease.OwnSourceIds(reports);if(sourceIds.Length==0)throw new IOException("The current capture identity could not be confirmed");sources=CapturedSources(sources,sourceIds);}
   count=sources.Length;if(!state.Running&&sources.Length==1)sourceIds=sources[0].InstanceIds;
   ControllerHideState config;using(var hide=new ControllerHidHide()){config=hide.Read();}active=config.Active;inverse=config.Inverse;
   blocked=active&&!inverse&&count==1&&sourceIds.Length>0&&sourceIds.All(id=>config.Devices.Contains(id,StringComparer.OrdinalIgnoreCase));
   visible=await ControllerRemapper.Observe(reports);if(state.Running&&state.OutputTarget=="DualShock4"){string? identity=ControllerRemapper.OwnDs4Identity;if(identity!=null)sony=await ControllerDs4HidReader.IsOwnedPresent(identity);}
   var result=Evaluate(state,count,visible,sony,blocked,active,inverse);return Snapshot(result,runtime.Read())!;
  }catch(Exception error){return Evaluate(state,count,visible,sony,blocked,active,inverse,error.GetType().Name+": "+error.Message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>",StringComparison.OrdinalIgnoreCase));}
 }
}
