// SPDX-License-Identifier: AGPL-3.0-only
// ABI from Intel IGCL igcl_api.h, ctl_3d_feature_* (x64).
using System.Runtime.InteropServices;
using static ONEXTweaks.Backend.IgclNative;
namespace ONEXTweaks.WidgetBridge;
internal sealed record IntelFpsState(bool Available,bool Enabled,int Value,int Minimum,int Maximum,int Step,string? Error=null);
internal sealed class IntelFrameLimit:IDisposable
{
    [StructLayout(LayoutKind.Explicit,Size=24)] private struct Caps
    { [FieldOffset(0)]public uint Size;[FieldOffset(4)]public byte Version;[FieldOffset(8)]public uint Count;[FieldOffset(16)]public IntPtr Details; }
    [StructLayout(LayoutKind.Explicit,Size=56)] private struct Feature
    { [FieldOffset(0)]public uint Size;[FieldOffset(4)]public byte Version;[FieldOffset(8)]public uint Type;[FieldOffset(16)]public IntPtr App;[FieldOffset(24)]public sbyte AppLength;[FieldOffset(25)]public byte Set;[FieldOffset(28)]public uint ValueType;[FieldOffset(32)]public byte Enabled;[FieldOffset(36)]public int Value;[FieldOffset(40)]public int CustomSize;[FieldOffset(48)]public IntPtr Custom; }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("ControlLib.dll",ExactSpelling=true)] private static extern uint ctlGetSupported3DCapabilities(IntPtr adapter,ref Caps caps);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("ControlLib.dll",ExactSpelling=true)] private static extern uint ctlGetSet3DFeature(IntPtr adapter,ref Feature feature);
    private IntPtr api,adapter;private int minimum,maximum,step;private string? error;
    internal IntelFrameLimit(){try{Initialize();}catch(Exception ex){error=ex.Message;Dispose();}}
    private static void Check(uint result,string operation){if(result!=0)throw new IOException(operation+" 0x"+result.ToString("X8"));}
    private void Initialize()
    {
        var init=new InitArgs{Size=(uint)Marshal.SizeOf<InitArgs>(),AppVersion=1u<<16,Flags=1};Check(ctlInit(ref init,out api),"Intel initialization");
        uint count=0;Check(ctlEnumerateDevices(api,ref count,IntPtr.Zero),"Intel enumeration");if(count is 0 or >64)throw new IOException("No Intel GPU");
        IntPtr devices=Marshal.AllocHGlobal((int)count*8),id=Marshal.AllocHGlobal(8);
        try{Check(ctlEnumerateDevices(api,ref count,devices),"Intel adapters");if(count>64)throw new IOException("Adapter count changed");for(int i=0;i<count;i++){var candidate=Marshal.ReadIntPtr(devices,i*8);var props=new AdapterProperties{Size=(uint)Marshal.SizeOf<AdapterProperties>(),DeviceId=id,DeviceIdSize=8,Name="",Reserved=new byte[108]};if(ctlGetDeviceProperties(candidate,ref props)==0&&props.PciVendorId==0x8086&&props.PciDeviceId==0xB080){adapter=candidate;break;}}}
        finally{Marshal.FreeHGlobal(devices);Marshal.FreeHGlobal(id);}
        if(adapter==IntPtr.Zero)throw new IOException("Intel Arc B390 not found");
        var caps=new Caps{Size=24};Check(ctlGetSupported3DCapabilities(adapter,ref caps),"Intel 3D capabilities");if(caps.Count is 0 or >256)throw new IOException("Invalid Intel capability count");
        uint capacity=caps.Count;caps.Details=Marshal.AllocHGlobal((int)capacity*72);
        try{unsafe{new Span<byte>((void*)caps.Details,(int)capacity*72).Clear();}Check(ctlGetSupported3DCapabilities(adapter,ref caps),"Intel 3D features");if(caps.Count>capacity)throw new IOException("Intel capabilities changed");for(int i=0;i<caps.Count;i++){IntPtr detail=caps.Details+i*72;if(Marshal.ReadInt32(detail)!=2)continue;if(Marshal.ReadInt32(detail,4)!=2)throw new IOException("Unexpected Intel frame limiter value type");minimum=Marshal.ReadInt32(detail,12);maximum=Marshal.ReadInt32(detail,16);step=Marshal.ReadInt32(detail,20);break;}}
        finally{Marshal.FreeHGlobal(caps.Details);}
        if(minimum<1||maximum<minimum||maximum>2000||step<1)throw new IOException("Intel driver does not expose FRAME_LIMIT");
        _=ReadNative();
    }
    private Feature ReadNative(){var feature=new Feature{Size=56,Type=2,ValueType=2};Check(ctlGetSet3DFeature(adapter,ref feature),"Intel frame limit read");return feature;}
    internal IntelFpsState Read(){if(adapter==IntPtr.Zero)return new(false,false,0,minimum,maximum,step,error);try{var f=ReadNative();return new(true,f.Enabled!=0,f.Value,minimum,maximum,step);}catch(Exception ex){return new(false,false,0,minimum,maximum,step,ex.Message);}}
    internal unsafe object InspectApplication(string executable)
    {
        if(adapter==IntPtr.Zero)throw new IOException(error??"Intel frame limit unavailable");
        if(executable.Length is <5 or >127||!executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||
            executable.Any(ch=>ch is <' ' or >'~' or '/' or '\\' or ':'))
            throw new ArgumentException("Expected an executable name accepted by Intel IGCL",nameof(executable));
        byte[] name=System.Text.Encoding.ASCII.GetBytes(executable+"\0");
        fixed(byte* application=name)
        {
            var named=new Feature{Size=56,App=(nint)application,AppLength=(sbyte)(name.Length-1)};
            var cap=named with{Type=2,ValueType=2};
            uint appResult=ctlGetSet3DFeature(adapter,ref cap);
            Span<uint> buffer=stackalloc uint[7];
            buffer.Clear();
            fixed(uint* data=buffer)
            {
                var live=named with{Type=19,ValueType=5,CustomSize=buffer.Length*sizeof(uint),Custom=(nint)data};
                uint stateResult=ctlGetSet3DFeature(adapter,ref live);
                return new{executable,global=Read(),appResult,appEnabled=appResult==0?(bool?)(cap.Enabled!=0):null,
                    appLimit=appResult==0?(int?)cap.Value:null,stateResult,graphicsApi=stateResult==0?(uint?)buffer[0]:null,
                    targetFps=stateResult==0?(uint?)buffer[1]:null,framePacing=stateResult==0?(uint?)buffer[2]:null};
            }
        }
    }
    internal void Set(bool enabled,int value)
    {
        if(adapter==IntPtr.Zero)throw new IOException(error??"Intel frame limit unavailable");if(enabled&&(value<minimum||value>maximum||(value-minimum)%step!=0))throw new ArgumentOutOfRangeException(nameof(value));
        var feature=new Feature{Size=56,Type=2,ValueType=2,Set=1,Enabled=enabled?(byte)1:(byte)0,Value=Math.Clamp(value,minimum,maximum)};Check(ctlGetSet3DFeature(adapter,ref feature),"Intel frame limit set");var actual=ReadNative();if(actual.Enabled!=feature.Enabled||enabled&&actual.Value!=value)throw new IOException("Intel FPS limit readback mismatch");
    }
    public void Dispose(){adapter=IntPtr.Zero;if(api!=IntPtr.Zero){ctlClose(api);api=IntPtr.Zero;}}
}
