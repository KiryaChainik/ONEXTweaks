// Independent Windows readback/restoration for the interactive integration test. AGPL-3.0-only.
using System;
using System.Runtime.InteropServices;
public sealed class MediaControlProbe : IDisposable
{
    private object enumerator;
    private Device device;
    private Endpoint endpoint;
    public MediaControlProbe()
    {
        enumerator = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
        Marshal.ThrowExceptionForHR(((Enumerator)enumerator).Default(0,1,out device));
        Guid iid = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A"); object value;
        Marshal.ThrowExceptionForHR(device.Activate(ref iid,23,IntPtr.Zero,out value));
        endpoint = (Endpoint)value;
    }
    public float Volume() { float value; Marshal.ThrowExceptionForHR(endpoint.GetScalar(out value)); return value; }
    public bool Muted() { bool value; Marshal.ThrowExceptionForHR(endpoint.GetMuted(out value)); return value; }
    public void SetVolume(float value) { Guid context=Guid.Empty; Marshal.ThrowExceptionForHR(endpoint.SetScalar(value,ref context)); }
    public void SetMuted(bool value) { Guid context=Guid.Empty; Marshal.ThrowExceptionForHR(endpoint.SetMuted(value,ref context)); }
    public void Dispose() { if(endpoint!=null) Marshal.ReleaseComObject(endpoint); if(device!=null) Marshal.ReleaseComObject(device); if(enumerator!=null) Marshal.ReleaseComObject(enumerator); endpoint=null; device=null; enumerator=null; }
    [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface Enumerator {
        [PreserveSig] int Enumerate(int flow,uint mask,out IntPtr result);
        [PreserveSig] int Default(int flow,int role,out Device result);
    }
    [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface Device {
        [PreserveSig] int Activate(ref Guid iid,uint context,IntPtr parameters,[MarshalAs(UnmanagedType.IUnknown)] out object result);
    }
    [ComImport,Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface Endpoint {
        [PreserveSig] int Register(IntPtr callback); [PreserveSig] int Unregister(IntPtr callback);
        [PreserveSig] int Channels(out uint count); [PreserveSig] int SetDb(float v,ref Guid c);
        [PreserveSig] int SetScalar(float v,ref Guid c); [PreserveSig] int GetDb(out float v);
        [PreserveSig] int GetScalar(out float v); [PreserveSig] int SetChannelDb(uint ch,float v,ref Guid c);
        [PreserveSig] int SetChannelScalar(uint ch,float v,ref Guid c); [PreserveSig] int GetChannelDb(uint ch,out float v);
        [PreserveSig] int GetChannelScalar(uint ch,out float v);
        [PreserveSig] int SetMuted([MarshalAs(UnmanagedType.Bool)] bool value,ref Guid context);
        [PreserveSig] int GetMuted([MarshalAs(UnmanagedType.Bool)] out bool value);
    }
}
