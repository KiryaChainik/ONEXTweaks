// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;

namespace ONEXTweaks.WidgetBridge;

internal sealed record AirplaneState(bool Available,bool On,string? Error);
internal sealed class AirplaneController
{
    // RadioMgr.idl documents the system/individual-radio distinction. This master
    // COM interface is internal to Windows; reject unsupported/unknown results.
    // Interface reference: https://github.com/fafalone/RadioMan#how-it-works
    private readonly object gate=new();
    internal AirplaneState Read()
    {
        lock(gate)
        {
            try{using var api=new Session();return api.Read();}
            catch(Exception error){return new(false,false,error.Message);}
        }
    }
    internal AirplaneState Set(bool on)
    {
        lock(gate)
        {
            using var api=new Session();var original=api.Read();
            if(!original.Available)throw new InvalidOperationException(original.Error);
            if(original.On==on)return original;
            try
            {
                api.Write(on);
                for(int attempt=0;attempt<60;attempt++){var actual=api.Read();if(actual.Available&&actual.On==on)return actual;Thread.Sleep(50);}
                throw new IOException("Windows did not confirm airplane mode");
            }
            catch
            {
                try{api.Write(original.On);}catch{}
                throw;
            }
        }
    }
    private sealed class Session : IDisposable
    {
        private readonly int initialized;
        private IRadioManager? manager;
        internal Session()
        {
            initialized=CoInitializeEx(0,0);
            if(initialized<0&&initialized!=unchecked((int)0x80010106))Marshal.ThrowExceptionForHR(initialized);
            nint pointer=0;
            try
            {
                Guid cls=new("581333F6-28DB-41BE-BC7A-FF201F12F3F6"),iid=new("DB3AFBFB-08E6-46C6-AA70-BF9A34C30AB7");
                Marshal.ThrowExceptionForHR(CoCreateInstance(ref cls,0,4,ref iid,out pointer));manager=(IRadioManager)Marshal.GetObjectForIUnknown(pointer);
            }
            catch{if(initialized>=0)CoUninitialize();throw;}
            finally{if(pointer!=0)Marshal.Release(pointer);}
        }
        internal AirplaneState Read()
        {
            Marshal.ThrowExceptionForHR(manager!.IsRMSupported(out int supported));
            if(supported!=1)return new(false,false,"Windows radio management is unavailable");
            Marshal.ThrowExceptionForHR(manager.GetSystemRadioState(out int enabled,out _,out _));
            if(enabled is not (0 or 1))throw new IOException("Unknown Windows system radio state");
            return new(true,enabled==0,null);
        }
        internal void Write(bool on)=>Marshal.ThrowExceptionForHR(manager!.SetSystemRadioState(on?0:1));
        public void Dispose(){if(manager!=null){Marshal.FinalReleaseComObject(manager);manager=null;}if(initialized>=0)CoUninitialize();}
    }
    [ComImport,Guid("DB3AFBFB-08E6-46C6-AA70-BF9A34C30AB7"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IRadioManager
    {
        [PreserveSig]int IsRMSupported(out int supported);
        [PreserveSig]int GetUIRadioInstances(out nint instances);
        [PreserveSig]int GetSystemRadioState(out int enabled,out int hardware,out int reason);
        [PreserveSig]int SetSystemRadioState(int enabled);
        [PreserveSig]int Refresh();
        [PreserveSig]int OnHardwareSliderChange(int a,int b);
    }
    [DllImport("ole32.dll")]private static extern int CoInitializeEx(nint reserved,uint mode);
    [DllImport("ole32.dll")]private static extern void CoUninitialize();
    [DllImport("ole32.dll")]private static extern int CoCreateInstance(ref Guid cls,nint outer,uint context,ref Guid iid,out nint value);
}
