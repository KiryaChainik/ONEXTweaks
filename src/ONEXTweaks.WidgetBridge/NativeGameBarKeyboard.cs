// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;

namespace ONEXTweaks.WidgetBridge;
/// <summary>Observe only physical Win+G. Injected dismissal keys never request a restore.</summary>
internal sealed class NativeGameBarKeyboard : IDisposable
{
    private readonly Thread thread;
    private readonly Action restore;
    private uint threadId;
    private nint hook;
    private readonly Callback callback;
    private bool pendingOpen,gHeld;
    private delegate nint Callback(int code,nint message,nint data);
    [StructLayout(LayoutKind.Sequential)]private struct Key {internal uint Vk,Scan,Flags,Time;internal nuint Extra;}
    [StructLayout(LayoutKind.Sequential)]private struct Message {internal nint Window;internal uint Id;internal nuint W;internal nint L;internal uint Time;internal int X,Y;internal uint Private;}
    [DllImport("user32.dll")]private static extern nint SetWindowsHookEx(int id,Callback cb,nint module,uint tid);
    [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]private static extern nint CallNextHookEx(nint hook,int code,nint message,nint data);
    [DllImport("user32.dll")]private static extern int GetMessage(out Message message,nint window,uint min,uint max);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]private static extern bool PostThreadMessage(uint tid,uint msg,nuint w,nint l);
    [DllImport("kernel32.dll")]private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern nint GetModuleHandle(string? name);
    internal NativeGameBarKeyboard(Action restoreWidget)
    {
        restore=restoreWidget;callback=Read;
        thread=new Thread(()=>{threadId=GetCurrentThreadId();hook=SetWindowsHookEx(13,callback,GetModuleHandle(null),0);if(hook!=0){try{while(GetMessage(out _,0,0,0)>0){}}finally{UnhookWindowsHookEx(hook);}}}){IsBackground=true,Name="ONEX WinG observer"};thread.Start();
    }
    private nint Read(int code,nint message,nint data)
    {
        if(code>=0)
        {
            var key=Marshal.PtrToStructure<Key>(data);
            if(key.Vk==0x47&&(key.Flags&0x10)==0)
            {
                if(message==0x100||message==0x104){if(!gHeld){gHeld=true;pendingOpen=(GetAsyncKeyState(0x5b)<0||GetAsyncKeyState(0x5c)<0)&&!NativeGameBarState.IsInputRedirected();}}
                else if(message==0x101||message==0x105){gHeld=false;if(pendingOpen){pendingOpen=false;restore();}}
            }
        }
        return CallNextHookEx(hook,code,message,data);
    }
    public void Dispose(){if(threadId!=0)PostThreadMessage(threadId,0x12,0,0);thread.Join(2000);}
}
