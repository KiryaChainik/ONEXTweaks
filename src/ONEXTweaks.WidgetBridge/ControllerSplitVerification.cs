// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerSplitVerification
{
 private sealed class Capture:IControllerCaptureRuntime
 {
  internal int Starts,Stops;private bool running;
  public ControllerRuntimeState Read()=>new(running,false,0,running?1:-1,null,[],0);
  public void Policy(ControllerPolicy value){}
  public Task Start(){Starts++;running=true;return Task.CompletedTask;}
  public Task Stop(){Stops++;running=false;return Task.CompletedTask;}
  public ValueTask DisposeAsync(){running=false;return ValueTask.CompletedTask;}
 }
 internal static async Task<int> Run(string file)
 {
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  string root=Path.Combine(Path.GetDirectoryName(file)!,"split-model-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   foreach(bool installed in new[]{false,true}){string data=Path.Combine(root,installed?"installed":"missing"),reports=Path.Combine(data,"Reports");Directory.CreateDirectory(reports);var capture=new Capture();
    await using(var profiles=new ControllerProfiles(reports,()=>"fixed",()=>"global",()=>[],capture:capture,defaultVirtualAvailable:()=>installed)){using var state=JsonSerializer.SerializeToDocument(profiles.Read(),Backend.Program.Json);Check(state.RootElement.GetProperty("enabled").GetBoolean()==installed,"Factory default must follow verified VIIPER availability");await profiles.Resume();Check(capture.Starts==(installed?1:0),"Factory default must reconcile capture only when available");using var change=JsonDocument.Parse("{\"operation\":\"enable\",\"enabled\":false}");await profiles.Mutate(change.RootElement);}
    await using(var profiles=new ControllerProfiles(reports,()=>"fixed",()=>"global",()=>[],capture:new Capture(),defaultVirtualAvailable:()=>throw new IOException("Saved mode must not consult factory default"))){using var state=JsonSerializer.SerializeToDocument(profiles.Read(),Backend.Program.Json);Check(!state.RootElement.GetProperty("enabled").GetBoolean(),"Saved Hardware preference was overwritten");}
   }
   var status=ControllerViiperInstallation.Read(true);Check(status.DriverService=="usbip2_ude","Incorrect VIIPER driver service");Check(!status.CanUseVirtual||status.LibraryInstalled&&status.LibraryVerified&&status.DriverInstalled&&!status.DriverDisabled,"Invalid installation readiness");
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,realInstallationReadOnly=true,installation=status,productionSettingsWrites=0,driverChanges=0,physicalOwnerInputTested=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message},Backend.Program.Json));return 2;}finally{Directory.Delete(root,true);}
 }
}
