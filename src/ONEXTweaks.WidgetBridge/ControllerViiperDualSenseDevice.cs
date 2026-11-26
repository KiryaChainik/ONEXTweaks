// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
using System.Security.Cryptography;
namespace ONEXTweaks.WidgetBridge;
// Capability probe backend only; the production target validator does not enable these devices yet.
internal sealed class ControllerViiperDualSenseDevice:IDisposable
{
 [StructLayout(LayoutKind.Sequential)]internal struct DeviceState{
  internal sbyte Lx,Ly,Rx,Ry;internal uint Buttons;internal byte DPad,L2,R2;
  internal ushort Touch1X,Touch1Y;internal byte Touch1Active;internal ushort Touch2X,Touch2Y;internal byte Touch2Active;
  internal short GyroX,GyroY,GyroZ,AccelX,AccelY,AccelZ;
 }
 [StructLayout(LayoutKind.Sequential)]private struct MetaState{
  [MarshalAs(UnmanagedType.LPStr)]internal string Serial;[MarshalAs(UnmanagedType.LPStr)]internal string Mac;[MarshalAs(UnmanagedType.LPStr)]internal string Board;
  internal byte Battery;internal double Temperature,Voltage;[MarshalAs(UnmanagedType.LPStr)]internal string Shell;
 }
 internal sealed record FeedbackState(byte Small,byte Large,byte Red,byte Green,byte Blue,byte PlayerLeds);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void OutputCallback(nuint handle,byte small,byte large,byte red,byte green,byte blue,byte playerLeds);
 private readonly object gate=new();private readonly OutputCallback callback;private nuint server,device;private uint bus;
 internal byte[] Identity{get;}=RandomNumberGenerator.GetBytes(6);internal ushort Product{get;}
 internal event Action<FeedbackState>? Feedback;
 internal ControllerViiperDualSenseDevice(bool edge){
  string library=Path.Combine(AppContext.BaseDirectory,"libVIIPER.dll");if(!File.Exists(library)||!Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(library))).Equals(ControllerViiperDevice.LibrarySha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Pinned VIIPER library missing or changed");
  if(Marshal.SizeOf<DeviceState>()!=36||Marshal.SizeOf<MetaState>()!=56)throw new IOException("DualSense C ABI layout mismatch");Product=edge?(ushort)0x0df2:(ushort)0x0ce6;Identity[0]=(byte)((Identity[0]|2)&254);
  callback=(_,small,large,r,g,b,player)=>{try{Feedback?.Invoke(new(small,large,r,g,b,player));}catch{}};
  try{
   var config=new ControllerViiperDevice.ServerConfig{Address="127.0.0.1:0",ConnectionTimeout=30000,DeviceConnectTimeout=5000,FlushInterval=1};if(!NewUSBServer(ref config,out server,0))throw new IOException("Owned DualSense USB server failed");bus=ControllerViiperDevice.NextOwnedBus();if(!CreateUSBBus(server,ref bus))throw new IOException("Owned DualSense bus failed");
   var meta=new MetaState{Serial=Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),Mac=string.Join(":",Identity.Select(x=>x.ToString("X2"))),Board=edge?"HMB-010":"BDM-050",Battery=0x2a,Temperature=28,Voltage=3.8,Shell="01"};
   bool created=edge?CreateDualSenseEdgeDevice(server,out device,bus,1,0,0,ref meta):CreateDualSenseDevice(server,out device,bus,1,0,0,ref meta);if(!created)throw new IOException("Official DualSense creation failed");if(!SetDualSenseOutputCallback(device,callback))throw new IOException("DualSense output callback failed");Submit(new(){AccelZ=-8192});
  }catch{Dispose();throw;}
 }
 internal void Submit(DeviceState state){lock(gate){ObjectDisposedException.ThrowIf(device==0,this);if(!SetDualSenseDeviceState(device,state))throw new IOException("DualSense state was rejected");}}
 public void Dispose(){lock(gate){if(device!=0){try{SetDualSenseDeviceState(device,new(){AccelZ=-8192});SetDualSenseOutputCallback(device,null);RemoveDualSenseDevice(device);}finally{device=0;}}if(server!=0){try{if(bus!=0)RemoveUSBBus(server,bus);CloseUSBServer(server);}finally{server=0;bus=0;}}GC.KeepAlive(callback);}}
 private const string Library="libVIIPER.dll";
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool NewUSBServer(ref ControllerViiperDevice.ServerConfig config,out nuint handle,nint log);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool CreateUSBBus(nuint handle,ref uint bus);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool RemoveUSBBus(nuint handle,uint bus);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool CloseUSBServer(nuint handle);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool CreateDualSenseDevice(nuint server,out nuint handle,uint bus,byte attach,ushort vendor,ushort product,ref MetaState meta);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool CreateDualSenseEdgeDevice(nuint server,out nuint handle,uint bus,byte attach,ushort vendor,ushort product,ref MetaState meta);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool SetDualSenseDeviceState(nuint handle,DeviceState state);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool SetDualSenseOutputCallback(nuint handle,OutputCallback? callback);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)][DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool RemoveDualSenseDevice(nuint handle);
}
