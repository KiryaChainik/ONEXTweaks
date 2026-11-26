// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerDs4Verification
{
 internal static async Task<int> Models(string file){
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  try{
   Check(Marshal.SizeOf<ControllerViiperDs4Device.DeviceState>()==34,"DS4 C ABI size wrong");
   foreach(var pair in new[]{("Buttons",4),("DPad",6),("L2",7),("Touch1X",10),("Touch2X",16),("GyroX",22),("AccelZ",32)})Check(Marshal.OffsetOf<ControllerViiperDs4Device.DeviceState>(pair.Item1).ToInt32()==pair.Item2,"DS4 C ABI offset wrong");
   var neutral=ControllerViiperDs4Device.Encode(new());Check(neutral.DPad==0&&neutral.Buttons==0&&neutral.Lx==0&&neutral.Ly==0&&neutral.AccelZ==-8192,"DS4 neutral packet wrong; C header hat constants must not be used as input bitmask");
   foreach(var pair in new[]{(0x1000,0x20),(0x2000,0x40),(0x4000,0x10),(0x8000,0x80),(0x100,0x100),(0x200,0x200),(0x20,0x1000),(0x10,0x2000),(0x40,0x4000),(0x80,0x8000)})Check(ControllerViiperDs4Device.Encode(new((ushort)pair.Item1)).Buttons==pair.Item2,"Xbox-to-DS4 button mapping wrong");
   Check(ControllerViiperDs4Device.Encode(new(),true,true).Buttons==3,"PS/touchpad model mapping wrong");
   foreach(ushort direction in new ushort[]{0,1,2,4,8,5,9,6,10})Check(ControllerViiperDs4Device.Encode(new(direction)).DPad==direction,"DS4 input bitmask mapping changed");
   Check(ControllerViiperDs4Device.Encode(new(3)).DPad==0&&ControllerViiperDs4Device.Encode(new(12)).DPad==0&&ControllerViiperDs4Device.Encode(new(15)).DPad==0,"Opposite D-pad directions not neutralized");
   var extremes=ControllerViiperDs4Device.Encode(new(Lx:-32768,Ly:32767,Rx:32767,Ry:-32768,Lt:17,Rt:250));Check(extremes.Lx==-128&&extremes.Ly==-128&&extremes.Rx==127&&extremes.Ry==127&&extremes.L2==17&&extremes.R2==250&&(extremes.Buttons&0xc00)==0xc00,"Axis endpoint/Y inversion/trigger mapping wrong");
   int previous=-129;for(int x=-32768;x<32768;x+=257){int encoded=ControllerViiperDs4Device.Encode(new(Lx:(short)x)).Lx;Check(encoded>=previous&&encoded is >=-128 and <=127,"DS4 stick response not monotonic/bounded");previous=encoded;}
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,ownerPreferenceWrites=0,physicalOwnerInputTested=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message},Backend.Program.Json));return 2;}
 }
 private static int TouchX(byte[] report,int offset)=>report[offset]|(report[offset+1]&15)<<8;
 private static int TouchY(byte[] report,int offset)=>report[offset+1]>>4|report[offset+2]<<4;
 internal static async Task<int> Native(string file){
  int checks=0;ControllerViiperDs4Device? device=null;ControllerDs4HidReader? hid=null;ControllerVirtualPad? xbox=null;var before=ControllerNative.Slots();void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  try{
   Check(before.SequenceEqual(new[]{0})&&ControllerNative.Read(0,out var physical)&&ControllerNative.Neutral(physical),"Protect owner physical input/current capture");using var window=new ControllerTestWindow();window.RequireForeground();device=new();hid=await ControllerDs4HidReader.OpenOwned(device.Serial);Check(true,"Owned identity was not proved");
   window.RequireForeground();device.Submit(ControllerViiperDs4Device.Encode(new()));await hid.ReadUntil(b=>b[1]==128&&b[2]==128&&b[3]==128&&b[4]==128&&(b[5]&15)==8&&b[6]==0&&(b[7]&3)==0);Check(true,"Actual neutral DS4 HID report mismatch");
   foreach(var pair in new[]{(0,8),(1,0),(9,1),(8,2),(10,3),(2,4),(6,5),(4,6),(5,7)}){window.RequireForeground();device.Submit(ControllerViiperDs4Device.Encode(new((ushort)pair.Item1)));await hid.ReadUntil(b=>(b[5]&15)==pair.Item2);Check(true,"Actual DPad HID hat wrong");}
   foreach(var pair in new[]{(0x1000,0x20),(0x2000,0x40),(0x4000,0x10),(0x8000,0x80)}){window.RequireForeground();device.Submit(ControllerViiperDs4Device.Encode(new((ushort)pair.Item1)));await hid.ReadUntil(b=>(b[5]&0xf0)==pair.Item2);Check(true,"Actual face-button HID bits wrong");}
   window.RequireForeground();device.Submit(ControllerViiperDs4Device.Encode(new(Lx:-32768,Ly:32767,Rx:32767,Ry:-32768,Lt:17,Rt:250)));await hid.ReadUntil(b=>b[1]==0&&b[2]==0&&b[3]==255&&b[4]==255&&b[8]==17&&b[9]==250);Check(true,"Actual DS4 stick/trigger report wrong");
   var touch=ControllerViiperDs4Device.Encode(new());touch.Touch1X=123;touch.Touch1Y=456;touch.Touch1Active=1;touch.Touch2X=1600;touch.Touch2Y=700;touch.Touch2Active=1;touch.GyroX=1234;touch.GyroY=-567;touch.GyroZ=321;touch.AccelX=900;touch.AccelY=-100;touch.AccelZ=-8192;window.RequireForeground();device.Submit(touch);
   await hid.ReadUntil(b=>(b[35]&0x80)==0&&(b[39]&0x80)==0&&TouchX(b,36)==123&&TouchY(b,36)==456&&TouchX(b,40)==1600&&TouchY(b,40)==700&&BitConverter.ToInt16(b,13)==1234&&BitConverter.ToInt16(b,15)==-567&&BitConverter.ToInt16(b,17)==321&&BitConverter.ToInt16(b,19)==900&&BitConverter.ToInt16(b,21)==-100&&BitConverter.ToInt16(b,23)==-8192);Check(true,"Actual touch/IMU HID report wrong");
   device.Submit(ControllerViiperDs4Device.Encode(new()));await hid.ReadUntil(b=>(b[35]&0x80)!=0&&(b[39]&0x80)!=0);Check(true,"Touch release not transmitted");
   var callback=new TaskCompletionSource<ControllerViiperDs4Device.FeedbackState>(TaskCreationOptions.RunContinuationsAsynchronously);device.Feedback+=value=>{if(value.Flags==3&&value.Small==13&&value.Large==27&&value.Red==17&&value.Green==89&&value.Blue==201)callback.TrySetResult(value);};hid.SendOutput(3,13,27,17,89,201);await callback.Task.WaitAsync(TimeSpan.FromSeconds(3));Check(true,"Actual host rumble/LED feedback callback wrong");
   // Coexistence uses the same process-wide bus allocator as the accepted Xbox path.
   window.RequireForeground();xbox=new ControllerVirtualPad(0,false);xbox.Submit(new(Rx:12345));bool seen=false;for(int n=0;n<100;n++){if(ControllerNative.Read(xbox.Slot,out var value)&&value.Rx==12345){seen=true;break;}await Task.Delay(5);}Check(seen,"Parallel owned Xbox endpoint failed");await hid.ReadUntil(b=>(b[5]&15)==8);Check(true,"DS4 lost input while owned Xbox endpoint exists");xbox.Dispose();xbox=null;
   string serial=device.Serial;hid.Dispose();hid=null;device.Dispose();device=null;bool stillPresent=true;for(int n=0;n<100;n++){stillPresent=await ControllerDs4HidReader.IsOwnedPresent(serial);if(!stillPresent)break;await Task.Delay(50);}Check(!stillPresent,"Own DS4 HID endpoint lingered after teardown");Check(ControllerNative.Slots().SequenceEqual(before),"Own virtual Xbox endpoint lingered");
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,actualDs4HidReadback=true,identityByOwnFeatureNonce=true,actualTouchAndImuPacket=true,actualHostRumbleLedFeedback=true,parallelXboxCoexistence=true,ownDs4EndpointRemoved=true,ownerCaptureEnabled=false,ownerPreferenceWrites=0,physicalLedWrites=0,syntheticControllerStates=true,physicalOwnerInputTested=false,installedAsOwnerMode=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>"),ownerPreferenceWrites=0,installedAsOwnerMode=false},Backend.Program.Json));return 2;}
  finally{hid?.Dispose();try{xbox?.Dispose();}catch{}try{device?.Dispose();}catch{}}
 }
}
