// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace ONEXTweaks.WidgetBridge;
/// <summary>Dedicated owned foreground sink. Test input is never emitted while another app owns foreground.</summary>
internal sealed class ControllerTestWindow:IDisposable
{
 private delegate nint Procedure(nint window,uint message,nuint w,nint l);
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct Class{internal uint Size,Style;internal Procedure Proc;internal int ClassExtra,WindowExtra;internal nint Instance,Icon,Cursor,Background;internal string? Menu;internal string Name;internal nint SmallIcon;}
 [StructLayout(LayoutKind.Sequential)]private struct Message{internal nint Window;internal uint Id;internal nuint WParam;internal nint LParam;internal uint Time;internal int X,Y;internal uint Private;}
 [StructLayout(LayoutKind.Sequential)]private struct Point{internal int X,Y;}
 [StructLayout(LayoutKind.Sequential)]private struct WheelInput{internal Point Position;internal uint Data,Flags,Time;internal nuint Extra;}
 private delegate nint MouseHook(int code,nuint message,nint input);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern ushort RegisterClassExW(ref Class value);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool UnregisterClassW(string name,nint module);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint CreateWindowExW(uint extended,string cls,string title,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint data);
 [DllImport("user32.dll")]private static extern nint DefWindowProcW(nint window,uint message,nuint w,nint l);
 [DllImport("user32.dll")]private static extern bool DestroyWindow(nint window);
 [DllImport("user32.dll")]private static extern void PostQuitMessage(int code);
 [DllImport("user32.dll")]private static extern bool PostMessageW(nint window,uint message,nuint w,nint l);
 [DllImport("user32.dll")]private static extern int GetMessageW(out Message value,nint window,uint min,uint max);
 [DllImport("user32.dll")]private static extern bool TranslateMessage(ref Message value);
 [DllImport("user32.dll")]private static extern nint DispatchMessageW(ref Message value);
 [DllImport("user32.dll")]private static extern bool SetForegroundWindow(nint window);
 [DllImport("user32.dll")]private static extern bool AllowSetForegroundWindow(uint pid);
 [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
 [DllImport("user32.dll")]private static extern bool BringWindowToTop(nint window);
 [DllImport("user32.dll")]private static extern bool AttachThreadInput(uint from,uint to,bool attach);
 [DllImport("kernel32.dll")]private static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")]private static extern nint GetForegroundWindow();
 [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint window,out uint pid);
 [DllImport("user32.dll")]private static extern nint GetMessageExtraInfo();
 [DllImport("user32.dll")]private static extern bool GetCursorPos(out Point point);
 [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll",SetLastError=true)]private static extern nint SetWindowsHookExW(int kind,MouseHook hook,nint module,uint thread);
 [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(nint hook);
 [DllImport("user32.dll")]private static extern nint CallNextHookEx(nint hook,int code,nuint message,nint input);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern nint GetModuleHandleW(string? name);
 [DllImport("user32.dll",SetLastError=true)]private static extern nint OpenInputDesktop(uint flags,bool inherit,uint access);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool GetUserObjectInformationW(nint handle,int index,System.Text.StringBuilder value,uint length,out uint needed);
 [DllImport("user32.dll")]private static extern bool CloseDesktop(nint desktop);
 private readonly string className="ONEXTweaksControllerQa"+Environment.ProcessId+"_"+Guid.NewGuid().ToString("N");private readonly Thread thread;private readonly Procedure procedure;private readonly ManualResetEventSlim ready=new();private readonly nint previous;private readonly Point cursor;private nint window;private Exception? failure;
 internal ConcurrentQueue<string> Events{get;}=new();
 internal ConcurrentQueue<(uint Message,short Delta)> WheelEvents{get;}=new();
 private readonly MouseHook? wheelHook;
 private nint hookHandle;
 internal ControllerTestWindow(bool observeWheel=false){wheelHook=observeWheel?InterceptProbeWheel:null;previous=GetForegroundWindow();GetCursorPos(out cursor);procedure=WindowProcedure;thread=new Thread(Run){IsBackground=true,Name="ONEXTweaks controller QA window"};thread.Start();if(!ready.Wait(2000))throw new IOException("Own controller QA window did not initialize");if(failure!=null)throw new IOException("Own controller QA window failed",failure);}
 private void Run()
 {
  try{string name=className;var cls=new Class{Size=(uint)Marshal.SizeOf<Class>(),Proc=procedure,Instance=GetModuleHandleW(null),Background=6,Name=name};if(RegisterClassExW(ref cls)==0)throw new Win32Exception(Marshal.GetLastWin32Error());window=CreateWindowExW(8,name,"ONEXTweaks - Controller verification",0x10cf0000,200,160,720,300,0,0,cls.Instance,0);if(window==0)throw new Win32Exception(Marshal.GetLastWin32Error());CreateWindowExW(0,"STATIC","ONEXTweaks controller verification\r\nAutomatic input test. Please leave controls untouched.",0x50000000,24,40,660,120,window,0,cls.Instance,0);uint foregroundThread=GetWindowThreadProcessId(GetForegroundWindow(),out _);uint currentThread=GetCurrentThreadId();bool joined=foregroundThread!=0&&foregroundThread!=currentThread&&AttachThreadInput(currentThread,foregroundThread,true);try{BringWindowToTop(window);SetForegroundWindow(window);}finally{if(joined)AttachThreadInput(currentThread,foregroundThread,false);}SetCursorPos(480,400);ready.Set();while(GetMessageW(out var message,0,0,0)>0){TranslateMessage(ref message);DispatchMessageW(ref message);}}
  catch(Exception e){failure=e;ready.Set();}
  finally{if(hookHandle!=0)UnhookWindowsHookEx(hookHandle);}
 }
 private nint WindowProcedure(nint h,uint message,nuint w,nint l)
 {
  if(message==0x10){DestroyWindow(h);return 0;}if(message==2){PostQuitMessage(0);return 0;}
  if(message is 0x100 or 0x101 or 0x104 or 0x105 or 0x201 or 0x202 or 0x204 or 0x205 or 0x207 or 0x208 or 0x20a or 0x20e){if(unchecked((nuint)GetMessageExtraInfo())==ControllerInputEmitter.OwnTag)Events.Enqueue(message+":"+w);return 0;}
  if(message==0x8001&&wheelHook!=null){hookHandle=SetWindowsHookExW(14,wheelHook,GetModuleHandleW(null),0);return hookHandle;}
  return DefWindowProcW(h,message,w,l);
 }
 internal void RequireForeground(){uint pid=0;for(int n=0;n<60;n++){GetWindowThreadProcessId(GetForegroundWindow(),out pid);if(pid==(uint)Environment.ProcessId)return;Thread.Sleep(25);}if(pid!=(uint)Environment.ProcessId){string desktop="unavailable";nint input=OpenInputDesktop(0,false,1);if(input!=0){try{var text=new System.Text.StringBuilder(128);if(GetUserObjectInformationW(input,2,text,256,out _))desktop=text.ToString()=="Default"?"default":"protected";}finally{CloseDesktop(input);}}throw new IOException("Injected test refused: own foreground not confirmed; input desktop="+desktop+"; foreground present="+(pid!=0));}}
 internal bool OwnsForeground(){GetWindowThreadProcessId(GetForegroundWindow(),out uint pid);return pid==(uint)Environment.ProcessId;}
 internal void PermitForeground(int pid){GetWindowThreadProcessId(GetForegroundWindow(),out uint foreground);if(foreground==(uint)Environment.ProcessId)AllowSetForegroundWindow((uint)pid);/* The child independently requires its own foreground before emitting input. */}
 internal static bool IsHeld(int key)=>(GetAsyncKeyState(key)&0x8000)!=0;
 internal void StartWheelProbe()
 {
  RequireForeground();
  if(wheelHook==null||!PostMessageW(window,0x8001,0,0)||!SpinWait.SpinUntil(()=>Volatile.Read(ref hookHandle)!=0,1000))
   throw new IOException("The Windows wheel input observer could not start");
 }
 private nint InterceptProbeWheel(int code,nuint message,nint data)
 {
  if(code>=0&&message is 0x20a or 0x20e)
  {
   var input=Marshal.PtrToStructure<WheelInput>(data);
   if((input.Flags&1)!=0&&input.Extra==ControllerInputEmitter.OwnTag)
   {
    // Consume only the test wheel input so the desktop underneath cannot scroll.
    WheelEvents.Enqueue(((uint)message,unchecked((short)(input.Data>>16))));
    return 1;
   }
  }
  return CallNextHookEx(0,code,message,data);
 }
 public void Dispose(){if(window!=0)PostMessageW(window,0x10,0,0);if(thread.Join(2000))UnregisterClassW(className,GetModuleHandleW(null));SetCursorPos(cursor.X,cursor.Y);if(previous!=0)SetForegroundWindow(previous);ready.Dispose();}
}
