// SPDX-License-Identifier: AGPL-3.0-only
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace ONEXTweaks.WidgetBridge;
internal sealed class NativeAwakePlatform:IAwakePlatform
{
    private readonly string reason;private readonly Func<IDisposable> dim;private readonly Func<Task> shutdown;
    internal NativeAwakePlatform(MediaController? media=null,string reason="ONEXTweaks Amphetamine: active keep-awake session",Func<IDisposable>? dim=null,Func<Task>? shutdown=null)
    {
        this.reason=reason;this.dim=dim??(()=>
        {
            if(media is null)throw new InvalidOperationException("Brightness control is unavailable.");
            string owner=media.DimForAwakeAsync().GetAwaiter().GetResult();
            return new Cleanup(()=>media.RestoreAwakeDimAsync(owner).GetAwaiter().GetResult());
        });
        this.shutdown=shutdown??(()=>SystemPowerActions.Execute("poweroff",false));
    }
    public int? BatteryPercent
    {
        get{if(!GetSystemPowerStatus(out var power))throw new Win32Exception(Marshal.GetLastWin32Error());return power.Percent==255||(power.Flags&128)!=0?null:power.Percent;}
    }
    public IDisposable Hold(bool displayRequired)=>new Request(reason,displayRequired);
    public IDisposable SubscribeSuspend(Action stop)=>new Subscription(stop);
    public IDisposable DimBrightness()=>dim();public Task Shutdown()=>shutdown();
    private sealed class Cleanup(Action action):IDisposable
    {private Action? clean=action;public void Dispose()=>Interlocked.Exchange(ref clean,null)?.Invoke();}
    private sealed class Request:IDisposable
    {
        private readonly SafeFileHandle handle;private readonly List<int> types=[];
        internal Request(string text,bool display)
        {
            nint message=Marshal.StringToHGlobalUni(text);
            try{var context=new Reason{Version=0,Flags=1,Simple=message};handle=PowerCreateRequest(ref context);}
            finally{Marshal.FreeHGlobal(message);}
            if(handle.IsInvalid){handle.Dispose();throw new Win32Exception(Marshal.GetLastWin32Error());}
            try{foreach(int type in display?new[]{1,3,0}:new[]{1,3}){if(!PowerSetRequest(handle,type))throw new Win32Exception(Marshal.GetLastWin32Error());types.Add(type);}}
            catch{Dispose();throw;}
        }
        public void Dispose(){if(handle.IsClosed)return;foreach(int type in types)PowerClearRequest(handle,type);types.Clear();handle.Dispose();}
    }
    private sealed class Subscription:IDisposable
    {
        private readonly Notify callback;private nint registration;
        internal Subscription(Action stop)
        {
            // Brightness restoration may use WMI. Never block the Windows power callback on it.
            callback=(_,type,_)=>{if(type==4)ThreadPool.QueueUserWorkItem(_=>{try{stop();}catch{}});return 0;};
            var parameters=new Subscribe{Callback=Marshal.GetFunctionPointerForDelegate(callback),Context=0};
            uint status=PowerRegisterSuspendResumeNotification(2,ref parameters,out registration);if(status!=0)throw new Win32Exception((int)status);
        }
        public void Dispose(){if(registration!=0){PowerUnregisterSuspendResumeNotification(registration);registration=0;}GC.KeepAlive(callback);}
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate uint Notify(nint context,uint type,nint setting);
    [StructLayout(LayoutKind.Sequential)]private struct Subscribe{internal nint Callback,Context;}
    [StructLayout(LayoutKind.Explicit,Size=32)]private struct Reason{[FieldOffset(0)]internal uint Version;[FieldOffset(4)]internal uint Flags;[FieldOffset(8)]internal nint Simple;}
    [StructLayout(LayoutKind.Sequential)]private struct PowerStatus{internal byte Ac,Flags,Percent,Reserved;internal uint Life,FullLife;}
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetSystemPowerStatus(out PowerStatus state);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern SafeFileHandle PowerCreateRequest(ref Reason context);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool PowerSetRequest(SafeFileHandle handle,int type);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool PowerClearRequest(SafeFileHandle handle,int type);
    [DllImport("powrprof.dll")]private static extern uint PowerRegisterSuspendResumeNotification(uint flags,ref Subscribe recipient,out nint registration);
    [DllImport("powrprof.dll")]private static extern uint PowerUnregisterSuspendResumeNotification(nint registration);
}
