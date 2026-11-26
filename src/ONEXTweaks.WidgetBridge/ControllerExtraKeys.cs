// SPDX-License-Identifier: AGPL-3.0-only
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace ONEXTweaks.WidgetBridge;
/// <summary>Optional firmware F13-F24 signals only. Never intercept letters, Windows shortcuts or injected keys.</summary>
internal sealed class ControllerExtraKeys:IDisposable
{
 [StructLayout(LayoutKind.Sequential)]private struct Key{internal uint Vk,Scan,Flags,Time;internal nuint Extra;}
 [StructLayout(LayoutKind.Sequential)]private struct Message{internal nint Window;internal uint Id;internal nuint WParam;internal nint LParam;internal uint Time;internal int X,Y;internal uint Private;}
 private delegate nint Hook(int code,nuint message,nint data);
 [DllImport("user32.dll",SetLastError=true)]private static extern nint SetWindowsHookExW(int id,Hook callback,nint module,uint thread);
 [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(nint hook);
 [DllImport("user32.dll")]private static extern nint CallNextHookEx(nint hook,int code,nuint message,nint data);
 [DllImport("user32.dll")]private static extern bool PeekMessageW(out Message message,nint window,uint min,uint max,uint remove);
 [DllImport("user32.dll")]private static extern int GetMessageW(out Message message,nint window,uint min,uint max);
 [DllImport("user32.dll")]private static extern bool PostThreadMessageW(uint thread,uint message,nuint w,nint l);
 [DllImport("kernel32.dll")]private static extern uint GetCurrentThreadId();
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern nint GetModuleHandleW(string? name);
 private readonly Thread thread;private readonly Hook callback;private readonly Func<ControllerPolicy> policy;private readonly Func<bool> firmwareAliases;private readonly ManualResetEventSlim ready=new();private readonly HashSet<uint> suppressed=[];private uint id;private int bits;private nint activeHook;private Exception? error;
 internal ushort Mask=>(ushort)Volatile.Read(ref bits);
 internal ControllerExtraKeys(Func<ControllerPolicy> current,Func<bool>? ownsAliases=null)
 {
  policy=current;firmwareAliases=ownsAliases??(()=>false);callback=Observe;thread=new Thread(Run){IsBackground=true,Name="ONEXTweaks extra controller signals"};thread.Start();if(!ready.Wait(2000))throw new IOException("Extra button listener did not initialize");if(error!=null)throw new IOException("Extra button listener failed",error);
 }
 private void Run(){nint hook=0;try{id=GetCurrentThreadId();hook=SetWindowsHookExW(13,callback,GetModuleHandleW(null),0);if(hook==0)throw new Win32Exception(Marshal.GetLastWin32Error());Interlocked.Exchange(ref activeHook,hook);PeekMessageW(out _,0,0,0,0);ready.Set();while(GetMessageW(out _,0,0,0)>0){}}catch(Exception e){error=e;ready.Set();}finally{nint owned=Interlocked.Exchange(ref activeHook,0);if(owned!=0)UnhookWindowsHookEx(owned);}}
 private nint Observe(int code,nuint message,nint pointer)
 {
  try
  {
   if(code>=0&&(message is 0x100 or 0x101 or 0x104 or 0x105))
   {
    var key=Marshal.PtrToStructure<Key>(pointer);if(key.Vk is >=0x7c and <=0x87&&(key.Flags&0x10)==0)
    {
     if(key.Vk<=0x7f&&firmwareAliases()){Interlocked.And(ref bits,~(1<<(int)(key.Vk-0x7c)));return CallNextHookEx(0,code,message,pointer);}
     int mask=1<<(int)(key.Vk-0x7c);bool down=message is 0x100 or 0x104;if(down)Interlocked.Or(ref bits,mask);else Interlocked.And(ref bits,~mask);
     bool consume=down?policy().For("F"+(key.Vk-0x7c+13)).Kind!="default":suppressed.Contains(key.Vk);if(consume){if(down)suppressed.Add(key.Vk);else suppressed.Remove(key.Vk);return 1;}
    }
   }
  }catch{}
  return CallNextHookEx(0,code,message,pointer);
 }
 public void Dispose(){nint owned=Interlocked.Exchange(ref activeHook,0);if(owned!=0)UnhookWindowsHookEx(owned);if(id!=0)PostThreadMessageW(id,0x12,0,0);thread.Join(2000);Volatile.Write(ref bits,0);ready.Dispose();}
}
