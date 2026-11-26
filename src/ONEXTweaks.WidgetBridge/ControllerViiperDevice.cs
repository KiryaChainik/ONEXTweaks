// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
using System.Security.Cryptography;
namespace ONEXTweaks.WidgetBridge;

// ABI checked against the header shipped in the official VIIPER v0.9.0 archive.
// Each instance owns its server, bus and device. It never removes foreign buses.
internal sealed class ControllerViiperDevice:IDisposable
{
 internal const string LibrarySha256="879b4dd556a8a5374957995406c749cd92cbb865d4e0632bd9c37e88a28c12c9";
 [StructLayout(LayoutKind.Sequential)]internal struct ServerConfig
 {
  [MarshalAs(UnmanagedType.LPStr)]internal string Address;
  internal ulong ConnectionTimeout,DeviceConnectTimeout;
  internal uint FlushInterval;
 }
 [StructLayout(LayoutKind.Sequential)]internal struct DeviceState
 {
  internal uint Buttons;internal byte Lt,Rt;internal short Lx,Ly,Rx,Ry;
  internal byte Reserved0,Reserved1,Reserved2,Reserved3,Reserved4,Reserved5;
 }
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void RumbleCallback(nuint device,byte left,byte right);
 private static int nextBusId;
 internal static uint NextOwnedBus()=>unchecked((uint)Interlocked.Increment(ref nextBusId));
 private readonly object gate=new();private readonly RumbleCallback callback;private nuint server,device;private uint bus;
 internal event Action<byte,byte>? Feedback;
 internal ControllerViiperDevice()
 {
  string file=Path.Combine(AppContext.BaseDirectory,"libVIIPER.dll");
  if(!File.Exists(file)||!Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).Equals(LibrarySha256,StringComparison.OrdinalIgnoreCase))throw new IOException("The verified VIIPER library is missing or changed");
  if(Marshal.SizeOf<ServerConfig>()!=32||Marshal.SizeOf<DeviceState>()!=20)throw new IOException("The VIIPER native layout is incompatible");
  callback=(_,left,right)=>{try{Feedback?.Invoke(left,right);}catch{/* Never unwind managed code through a native callback. */}};
  try
  {
   var config=new ServerConfig{Address="127.0.0.1:0",ConnectionTimeout=30000,DeviceConnectTimeout=5000,FlushInterval=1};
   if(!NewUSBServer(ref config,out server,0))throw new IOException("VIIPER could not start its local USB server");
   // v0.9.0 allocates bus IDs globally within the library, while its server's
   // automatic choice only sees that server. Explicit process-wide IDs avoid
   // collisions when two owned servers coexist during verification or teardown.
   bus=NextOwnedBus();if(!CreateUSBBus(server,ref bus))throw new IOException("VIIPER could not create its owned USB bus");
   if(!CreateXbox360Device(server,out device,bus,1,0,0,1))throw new IOException("VIIPER could not attach an Xbox controller; check the signed usbip-win2 driver");
   if(!SetXbox360RumbleCallback(device,callback))throw new IOException("VIIPER rumble feedback is unavailable");
   Submit(new());
  }
  catch{Dispose();throw;}
 }
 internal static DeviceState Encode(ControllerFrame frame,bool guide=false)=>new(){Buttons=(uint)(frame.Buttons&0xF3FF)|(guide?0x400u:0),Lt=frame.Lt,Rt=frame.Rt,Lx=frame.Lx,Ly=frame.Ly,Rx=frame.Rx,Ry=frame.Ry};
 internal void Submit(ControllerFrame frame,bool guide=false)
 {lock(gate){ObjectDisposedException.ThrowIf(device==0,this);if(!SetXbox360DeviceState(device,Encode(frame,guide)))throw new IOException("VIIPER rejected a controller state");}}
 public void Dispose()
 {
  lock(gate)
  {
   if(device!=0){try{SetXbox360DeviceState(device,new());SetXbox360RumbleCallback(device,null);RemoveXbox360Device(device);}catch{}device=0;}
   if(server!=0){try{if(bus!=0)RemoveUSBBus(server,bus);CloseUSBServer(server);}finally{server=0;bus=0;}}
   GC.KeepAlive(callback);
  }
 }
 private const string Library="libVIIPER.dll";
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool NewUSBServer(ref ServerConfig config,out nuint server,nint log);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool CloseUSBServer(nuint server);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool CreateUSBBus(nuint server,ref uint bus);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool RemoveUSBBus(nuint server,uint bus);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool CreateXbox360Device(nuint server,out nuint device,uint bus,byte autoAttach,ushort vendor,ushort product,byte subtype);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool SetXbox360DeviceState(nuint device,DeviceState state);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool SetXbox360RumbleCallback(nuint device,RumbleCallback? callback);
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool RemoveXbox360Device(nuint device);
}
