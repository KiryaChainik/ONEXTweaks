// SPDX-License-Identifier: AGPL-3.0-only
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace ONEXTweaks.WidgetBridge;

// A fixed shortcut filter, not a keyboard recorder. Only the two known OXP3
// system chords and four provisioned F-keys can be consumed; injected input
// and every other key are passed through. No arbitrary key data is stored.
internal sealed class ControllerOemChordHook:IDisposable
{
 private readonly ControllerOemRawInput input;private readonly Func<bool> enabled,aliases;private readonly Func<bool> keyboardOnly;private readonly Action<string>? chordPressed;
 private readonly HashSet<uint> modifiers=[],suppressed=[];private readonly HashSet<string> down=[];
 private readonly Callback callback;private readonly Thread thread;private readonly ManualResetEventSlim ready=new();
 private nint hook;private uint threadId;private Exception? failure;private bool onex,keyboard;
 internal ControllerOemChordHook(ControllerOemRawInput source,Func<bool> active,Func<bool> firmwareKeys,Func<bool>? onlyKeyboard=null,Action<string>? knownChordPressed=null)
 {
  input=source;enabled=active;aliases=firmwareKeys;keyboardOnly=onlyKeyboard??(()=>false);chordPressed=knownChordPressed;callback=Read;
  thread=new Thread(Pump){IsBackground=true,Name="ONEXTweaks OEM shortcut filter"};thread.Start();
  if(!ready.Wait(2500))throw new IOException("OEM shortcut filter did not initialize");
  if(failure!=null)throw new IOException("OEM shortcut filter unavailable",failure);
 }
 private void Publish(string button,bool pressed)
 {
  if(pressed?down.Add(button):down.Remove(button))input.FromHook(new(button,pressed));
 }
 private void Release(){foreach(string button in down.ToArray())Publish(button,false);onex=false;keyboard=false;input.ReleaseSource("hook");}
 private nint Read(int code,nuint message,nint pointer)
 {
  try
  {
   if(code<0||message is not(0x100 or 0x101 or 0x104 or 0x105))return CallNextHookEx(0,code,message,pointer);
   var key=Marshal.PtrToStructure<Key>(pointer);if((key.Flags&0x10)!=0)return CallNextHookEx(0,code,message,pointer);
   uint normalized=key.Vk switch{162 or 163=>17,164 or 165=>18,_=>key.Vk};
   bool isModifier=normalized is 17 or 18 or 91;bool pressed=message is 0x100 or 0x104;
   if(isModifier){if(pressed)modifiers.Add(normalized);else modifiers.Remove(normalized);}
   bool consume=!pressed&&suppressed.Remove(key.Vk);
   if(!enabled())
   {
    Release();return consume?1:CallNextHookEx(0,code,message,pointer);
   }
   if(key.Vk is >=124 and <=127)
   {
    string button=new[]{"M1","M2","Home","GKey"}[key.Vk-124];
    if(aliases()||!pressed&&down.Contains(button)){Publish(button,pressed);if(pressed)suppressed.Add(key.Vk);return 1;}
   }
   bool ctrl=modifiers.Contains(17),win=modifiers.Contains(91),alt=modifiers.Contains(18);
   if(key.Vk==79)
   {
    if(pressed&&ctrl&&win&&!alt){keyboard=true;chordPressed?.Invoke("KeyboardMouse");Publish("KeyboardMouse",true);suppressed.Add(key.Vk);return 1;}
    if(!pressed&&keyboard){keyboard=false;Publish("KeyboardMouse",false);return consume?1:CallNextHookEx(0,code,message,pointer);}
    if(!pressed&&ctrl&&win&&!alt&&!consume){input.FromHook(new("KeyboardMouse",true,true));return 1;}
   }
   if(isModifier&&!keyboardOnly())
   {
    if(pressed&&ctrl&&win&&alt&&!onex){onex=true;chordPressed?.Invoke("Onex");Publish("Onex",true);suppressed.Add(key.Vk);return 1;}
    if(!pressed&&onex){onex=false;Publish("Onex",false);}
    else if(!pressed&&normalized==18&&ctrl&&win&&!consume){input.FromHook(new("Onex",true,true));return 1;}
    if(!pressed&&keyboard){keyboard=false;Publish("KeyboardMouse",false);}
   }
   return consume?1:CallNextHookEx(0,code,message,pointer);
  }
  catch{return CallNextHookEx(0,code,message,pointer);}
 }
 private void Pump()
 {
  try
  {
   threadId=GetCurrentThreadId();PeekMessageW(out _,0,0,0,0);hook=SetWindowsHookExW(13,callback,GetModuleHandleW(null),0);
   if(hook==0)throw new Win32Exception(Marshal.GetLastWin32Error());input.HookSource(true);ready.Set();
   while(GetMessageW(out _,0,0,0)>0){}
  }
  catch(Exception e){failure=e;ready.Set();}
  finally{if(hook!=0)UnhookWindowsHookEx(hook);hook=0;Release();input.HookSource(false);}
 }
 public void Dispose(){if(threadId!=0)PostThreadMessageW(threadId,0x12,0,0);thread.Join(2500);ready.Dispose();}
 [StructLayout(LayoutKind.Sequential)]private struct Key{internal uint Vk,Scan,Flags,Time;internal nuint Extra;}
 [StructLayout(LayoutKind.Sequential)]private struct Message{internal nint Window;internal uint Id;internal nuint WParam;internal nint LParam;internal uint Time;internal int X,Y;internal uint Private;}
 private delegate nint Callback(int code,nuint message,nint pointer);
 [DllImport("user32.dll",SetLastError=true)]private static extern nint SetWindowsHookExW(int id,Callback callback,nint module,uint thread);
 [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(nint hook);
 [DllImport("user32.dll")]private static extern nint CallNextHookEx(nint hook,int code,nuint message,nint pointer);
 [DllImport("user32.dll")]private static extern int GetMessageW(out Message message,nint window,uint min,uint max);
 [DllImport("user32.dll")]private static extern bool PeekMessageW(out Message message,nint window,uint min,uint max,uint remove);
 [DllImport("user32.dll")]private static extern bool PostThreadMessageW(uint thread,uint message,nuint w,nint l);
 [DllImport("kernel32.dll")]private static extern uint GetCurrentThreadId();
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern nint GetModuleHandleW(string? name);
}
