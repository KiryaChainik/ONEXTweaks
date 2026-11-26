// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Gaming.Input;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerAdvancedTargetVerification
{
 internal static async Task<int> Models(string file){
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  try{
   Check(Marshal.SizeOf<ControllerViiperDualSenseDevice.DeviceState>()==36,"DualSense C state size wrong");
   foreach(var pair in new[]{("Buttons",4),("DPad",8),("Touch1X",12),("Touch2X",18),("GyroX",24),("AccelZ",34)})Check(Marshal.OffsetOf<ControllerViiperDualSenseDevice.DeviceState>(pair.Item1).ToInt32()==pair.Item2,"DualSense C field offset wrong");
   Check(!new ControllerPolicy().ForwardGameLighting,"Advanced probe changed default lighting");
   foreach(string target in new[]{"DualSense","DualSenseEdge","SwitchPro","Switch2Pro","XboxElite2","SteamController"}){bool refused=false;try{new ControllerPolicy(VirtualTarget:target).Validate();}catch(InvalidDataException){refused=true;}Check(refused,"Unverified advanced type exposed in production policy");}
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,ownerPreferenceWrites=0},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message},Backend.Program.Json));return 2;}
 }
 internal static async Task<int> Native(string file){
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}var results=new List<object>();ControllerViiperDualSenseDevice? device=null;ControllerDualSenseHidReader? hid=null;string? error=null;var before=ControllerNative.Slots();
  try{
   Check(before.SequenceEqual(new[]{0})&&ControllerNative.Read(0,out var source)&&ControllerNative.Neutral(source),"Protect owner physical controller");
   foreach(bool edge in new[]{false,true}){
    ushort product=edge?(ushort)0x0df2:(ushort)0x0ce6;int existing=RawGameController.RawGameControllers.Count(c=>c.HardwareVendorId==0x054c&&c.HardwareProductId==product);Check(existing==0,"Protect existing Sony controller");
    device=new(edge);hid=await ControllerDualSenseHidReader.OpenOwned(device.Identity,product);Check(true,"Own nonce not confirmed");
    await hid.ReadUntil(b=>b[1]==128&&b[2]==128&&b[3]==128&&b[4]==128&&b[5]==0&&b[6]==0&&b[8]==8&&b[9]==0&&b[10]==0&&BitConverter.ToInt16(b,26)==-8192);Check(true,"Actual neutral Sony report wrong");
    var feedback=new TaskCompletionSource<ControllerViiperDualSenseDevice.FeedbackState>(TaskCreationOptions.RunContinuationsAsynchronously);device.Feedback+=v=>{if(v.Small==13&&v.Large==27&&v.Red==17&&v.Green==89&&v.Blue==201&&v.PlayerLeds==3)feedback.TrySetResult(v);};
    hid.SendOutput(0,13,27,17,89,201);await feedback.Task.WaitAsync(TimeSpan.FromSeconds(4));Check(true,"Actual Sony host feedback callback wrong");
    for(int n=0;n<100&&!RawGameController.RawGameControllers.Any(c=>c.HardwareVendorId==0x054c&&c.HardwareProductId==product);n++)await Task.Delay(30);
    var controllers=RawGameController.RawGameControllers.Where(c=>c.HardwareVendorId==0x054c&&c.HardwareProductId==product).ToArray();bool raw=controllers.Length==1,gamepad=raw&&Gamepad.FromGameController(controllers[0])!=null;
    byte[] identity=device.Identity.ToArray();hid.Dispose();hid=null;device.Dispose();device=null;bool present=true;for(int n=0;n<120;n++){present=await ControllerDualSenseHidReader.IsOwnedPresent(identity,product);if(!present)break;await Task.Delay(50);}Check(!present,"Probe Sony endpoint survived disposal");Check(ControllerNative.Slots().SequenceEqual(before),"Probe altered XInput visibility");
    results.Add(new{target=edge?"DualSenseEdge":"DualSense",actualNeutralHid=true,actualHostFeedback=true,windowsRawGameController=raw,windowsGamepad=gamepad,ownEndpointRemoved=true,productionModeEnabled=false});
   }
  }catch(Exception e){error=e.Message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>",StringComparison.OrdinalIgnoreCase);}
  finally{hid?.Dispose();device?.Dispose();await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=error==null,checks,error,targets=results,physicalOwnerInputTested=false,nonNeutralControllerInputInjected=false,physicalRumbleOrLedForwarded=false,productionModeInstalled=false},Backend.Program.Json));}
  return error==null?0:2;
 }
}
