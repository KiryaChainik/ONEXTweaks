// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
using System.Security.Cryptography;
namespace ONEXTweaks.WidgetBridge;
/// <summary>Owned DualShock4 endpoint on the already verified VIIPER0.9 library; used by the independently verified capture runtime.</summary>
internal sealed class ControllerViiperDs4Device:IDisposable
{
 [StructLayout(LayoutKind.Sequential)]internal struct DeviceState{
  internal sbyte Lx,Ly,Rx,Ry;internal ushort Buttons;internal byte DPad,L2,R2;
  internal ushort Touch1X,Touch1Y;internal byte Touch1Active;internal ushort Touch2X,Touch2Y;internal byte Touch2Active;
  internal short GyroX,GyroY,GyroZ,AccelX,AccelY,AccelZ;
 }
 [StructLayout(LayoutKind.Sequential)]private struct MetaState{[MarshalAs(UnmanagedType.LPStr)]internal string Serial;[MarshalAs(UnmanagedType.LPStr)]internal string? Board;internal byte Battery;internal double Temperature,Voltage;}
 internal sealed record FeedbackState(byte Flags,byte Small,byte Large,byte Red,byte Green,byte Blue,byte FlashOn,byte FlashOff);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void OutputCallback(nuint handle,byte flags,byte small,byte large,byte red,byte green,byte blue,byte flashOn,byte flashOff);
 private readonly object gate=new();private readonly OutputCallback callback;private nuint server,device;private uint bus;
 internal string Serial{get;}=Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
 internal event Action<FeedbackState>? Feedback;
 internal ControllerViiperDs4Device(){
  string file=Path.Combine(AppContext.BaseDirectory,"libVIIPER.dll");if(!File.Exists(file)||!Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).Equals(ControllerViiperDevice.LibrarySha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Verified VIIPER library missing or changed");
  if(Marshal.SizeOf<DeviceState>()!=34||Marshal.SizeOf<MetaState>()!=40||Marshal.SizeOf<ControllerViiperDevice.ServerConfig>()!=32)throw new IOException("DS4 native ABI layout mismatch");
  callback=(_,flags,small,large,r,g,b,on,off)=>{try{Feedback?.Invoke(new(flags,small,large,r,g,b,on,off));}catch{/* Do not unwind through native callback. */}};
  try{
   var config=new ControllerViiperDevice.ServerConfig{Address="127.0.0.1:0",ConnectionTimeout=30000,DeviceConnectTimeout=5000,FlushInterval=1};if(!NewUSBServer(ref config,out server,0))throw new IOException("DS4 owned USB server failed");bus=ControllerViiperDevice.NextOwnedBus();if(!CreateUSBBus(server,ref bus))throw new IOException("DS4 owned USB bus failed");
   var meta=new MetaState{Serial=Serial,Board="JDM-055",Battery=0x1b,Temperature=28,Voltage=4.2};if(!CreateDS4Device(server,out device,bus,1,0,0,ref meta))throw new IOException("VIIPER DS4 attach failed");if(!SetDS4OutputCallback(device,callback))throw new IOException("VIIPER DS4 feedback unavailable");Submit(Encode(new()));
  }catch{Dispose();throw;}
 }
 private static sbyte Axis(short v,bool invert=false)=>(sbyte)Math.Clamp((int)Math.Round((invert?-(double)v:v)/256),-128,127);
 internal static DeviceState Encode(ControllerFrame f,bool guide=false,bool touchClick=false,ControllerDs4Motion? motion=null,Ds4TouchContacts contacts=Ds4TouchContacts.None){
  ushort buttons=0;void Map(int source,int target){if((f.Buttons&source)!=0)buttons|=(ushort)target;}
  Map(0x1000,0x20);Map(0x2000,0x40);Map(0x4000,0x10);Map(0x8000,0x80);Map(0x100,0x100);Map(0x200,0x200);Map(0x20,0x1000);Map(0x10,0x2000);Map(0x40,0x4000);Map(0x80,0x8000);if(f.Lt>0)buttons|=0x400;if(f.Rt>0)buttons|=0x800;if(guide)buttons|=1;if(touchClick)buttons|=2;
  // The shipped C header advertises USB-hat constants, but its Go wrapper
  // passes DPad unchanged to the bitmask InputState. Use the actual source ABI:
  // 1=up,2=down,4=left,8=right,0=neutral; native HID readback verifies all hats.
  byte dpad=(byte)(f.Buttons&15);if((dpad&3)==3)dpad&=12;if((dpad&12)==12)dpad&=3;
  var imu=motion??ControllerDs4Motion.Neutral;
  var state=new DeviceState{Lx=Axis(f.Lx),Ly=Axis(f.Ly,true),Rx=Axis(f.Rx),Ry=Axis(f.Ry,true),Buttons=buttons,DPad=dpad,L2=f.Lt,R2=f.Rt,GyroX=imu.GyroX,GyroY=imu.GyroY,GyroZ=imu.GyroZ,AccelX=imu.AccelX,AccelY=imu.AccelY,AccelZ=imu.AccelZ};
  if(touchClick&&contacts!=Ds4TouchContacts.None)
  {
   // Place contacts at the middle of each half of the DS4's 1920x942 surface.
   bool left=(contacts&Ds4TouchContacts.Left)!=0;
   bool right=(contacts&Ds4TouchContacts.Right)!=0;
   state.Touch1X=left?(ushort)480:(ushort)1440;
   state.Touch1Y=471;
   state.Touch1Active=1;
   if(left&&right)
   {
    state.Touch2X=1440;
    state.Touch2Y=471;
    state.Touch2Active=1;
   }
  }
  return state;
 }
 internal void Submit(DeviceState state){lock(gate){ObjectDisposedException.ThrowIf(device==0,this);if(!SetDS4DeviceState(device,state))throw new IOException("VIIPER rejected DS4 state");}}
 public void Dispose(){lock(gate){if(device!=0){try{SetDS4DeviceState(device,Encode(new()));SetDS4OutputCallback(device,null);RemoveDS4Device(device);}catch{}device=0;}if(server!=0){try{if(bus!=0)RemoveUSBBus(server,bus);CloseUSBServer(server);}finally{server=0;bus=0;}}GC.KeepAlive(callback);}}
 private const string Library="libVIIPER.dll";
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool NewUSBServer(ref ControllerViiperDevice.ServerConfig config,out nuint server,nint log);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool CreateUSBBus(nuint server,ref uint bus);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool RemoveUSBBus(nuint server,uint bus);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool CloseUSBServer(nuint server);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool CreateDS4Device(nuint server,out nuint device,uint bus,byte attach,ushort vendor,ushort product,ref MetaState meta);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool SetDS4DeviceState(nuint device,DeviceState state);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool SetDS4OutputCallback(nuint device,OutputCallback? callback);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool RemoveDS4Device(nuint device);
}
