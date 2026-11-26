// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
namespace ONEXTweaks.WidgetBridge;

internal sealed record OemSteamResult(string Action,bool Launched,string? Menu,bool ForegroundVerified);
internal sealed partial class DesktopActions
{
 private static readonly SemaphoreSlim oemSteamGate=new(1,1);
 internal static bool OemSteamOpensMenu(string action,bool bigPicturePresent)=>action switch
 {
  "steamBigPicture"=>bigPicturePresent,
  "steamMenu" or "steamQuickAccess"=>true,
  _=>throw new ArgumentException("Unknown Steam extra-button action")
 };
 private static string SteamDirectory()
 {
  using var steam=Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
  string? path=steam?.GetValue("SteamPath") as string;
  if(string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path)||!File.Exists(Path.Combine(path,"steam.exe")))
   throw new FileNotFoundException("Steam is not installed or its installation is unavailable.");
  return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
 }
 internal static nint FindSteamBigPicture()
 {
  string directory=SteamDirectory();nint found=0;int session=Process.GetCurrentProcess().SessionId;
  EnumWindows((window,_)=>
  {
   try
   {
    if(!IsWindowVisible(window))return true;
    var cls=new StringBuilder(128);GetClassName(window,cls,cls.Capacity);if(cls.ToString()!="SDL_app")return true;
    GetWindowThreadProcessId(window,out uint id);using var process=Process.GetProcessById((int)id);
    if(process.SessionId!=session||process.ProcessName is not("steam" or "steamwebhelper"))return true;
    string? executable=process.MainModule?.FileName;
    if(executable==null||!Path.GetFullPath(executable).StartsWith(directory,StringComparison.OrdinalIgnoreCase))return true;
    var title=new StringBuilder(256);GetWindowText(window,title,title.Capacity);
    if(!title.ToString().Contains("Big Picture",StringComparison.OrdinalIgnoreCase))return true;
    found=window;return false;
   }
   catch(System.ComponentModel.Win32Exception){return true;}
   catch(InvalidOperationException){return true;}
   catch(ArgumentException){return true;}
  },0);
  return found;
 }
 private static void RestoreSteamKeyboardFocus(nint window)
 {
  if(GetForegroundWindow()!=window||SteamKeyboardFocus(window)!=window)return;

  string directory=SteamDirectory();
  var renderers=new HashSet<nint>();
  EnumChildWindows(window,(child,_)=>
  {
   if(!IsWindowVisible(child))return true;
   var name=new StringBuilder(64);
   GetClassName(child,name,name.Capacity);
   if(name.ToString()!="Chrome_WidgetWin_1")return true;

   GetWindowThreadProcessId(child,out uint id);
   try
   {
    using var process=Process.GetProcessById((int)id);
    if(process.ProcessName is not("steam" or "steamwebhelper"))return true;
    var executable=process.MainModule?.FileName;
    if(executable==null||!Path.GetFullPath(executable).StartsWith(directory,StringComparison.OrdinalIgnoreCase))return true;
   }
   catch(System.ComponentModel.Win32Exception){return true;}
   catch(InvalidOperationException){return true;}
   catch(ArgumentException){return true;}

   renderers.Add(child);
   return true;
  },0);
  nint renderer=renderers.Count==1?renderers.Single():FindSteamRendererUnderContent(window,renderers);
  if(renderer==0||GetForegroundWindow()!=window||SteamKeyboardFocus(window)!=window)return;

  uint current=GetCurrentThreadId(),target=GetWindowThreadProcessId(renderer,out _);
  if(target==0)return;
  bool attached=target!=current&&AttachThreadInput(current,target,true);
  if(target!=current&&!attached)return;
  try
  {
   if(GetForegroundWindow()==window)SetFocus(renderer);
  }
  finally{if(attached)AttachThreadInput(current,target,false);}
 }
 private static nint FindSteamRendererUnderContent(nint window,HashSet<nint> renderers)
 {
  if(renderers.Count==0)return 0;
  nint previousDpi=SetThreadDpiAwarenessContext(-4);
  if(previousDpi==0)return 0;
  try
  {
   if(!GetWindowRect(window,out var bounds))return 0;
   int width=bounds.Right-bounds.Left;
   int middle=bounds.Top+(bounds.Bottom-bounds.Top)/2;
   nint Hit(int part)
   {
    var point=new SteamPoint{X=bounds.Left+width*part/4,Y=middle};
    for(nint target=WindowFromPoint(point);target!=0&&target!=window;target=GetAncestor(target,1))
     if(renderers.Contains(target))return target;
    return 0;
   }
   nint center=Hit(2),left=Hit(1),right=Hit(3);
   return (left==0||left==center)&&(right==0||right==center)?center:0;
  }
  finally{SetThreadDpiAwarenessContext(previousDpi);}
 }
 private static nint SteamKeyboardFocus(nint window)
 {
  uint owner=GetWindowThreadProcessId(window,out _);
  if(owner==0)return 0;
  var state=new GuiThreadInfo{Size=(uint)Marshal.SizeOf<GuiThreadInfo>()};
  return GetGUIThreadInfo(owner,ref state)?state.Focus:0;
 }
 internal static async Task<OemSteamResult> ExecuteOemSteam(string action,Func<bool> current,CancellationToken cancellation=default)
 {
  _=OemSteamOpensMenu(action,false);
  await oemSteamGate.WaitAsync(cancellation);
  try
  {
   void CheckCurrent(){cancellation.ThrowIfCancellationRequested();if(!current())throw new OperationCanceledException("The extra-button assignment changed.");}
   CheckCurrent();nint before=FindSteamBigPicture();bool launched=before==0,menu=OemSteamOpensMenu(action,!launched);
   // Launch/activate the actual Steam client. Never emulate a Guide or Win+G
   // press, and never send menu shortcuts to a game or the last tracked app.
   if(launched){using var process=Process.Start(new ProcessStartInfo("steam://open/bigpicture"){UseShellExecute=true});}
   nint window=before;bool focused=false;
   for(int n=0;n<300;n++)
   {
    CheckCurrent();window=FindSteamBigPicture();
    if(window!=0&&(GetForegroundWindow()==window||ActivateWindow(window)))
    {
     await Task.Delay(70,cancellation);CheckCurrent();
     if(GetForegroundWindow()==window&&!NativeGameBarState.IsInputRedirected()){focused=true;break;}
    }
    await Task.Delay(50,cancellation);
   }
   if(!focused)throw new InvalidOperationException("Steam Big Picture did not receive focus. Finish signing into Steam and try again.");
   if(menu)
   {
    for(int n=0;n<20&&new[]{0x10,0x11,0x12,0x5b,0x5c}.Any(key=>(GetAsyncKeyState(key)&0x8000)!=0);n++)await Task.Delay(25,cancellation);
    CheckCurrent();
    if(FindSteamBigPicture()!=window)throw new InvalidOperationException("The Steam Big Picture window changed.");
    // Steam's outer SDL window can be active while its visible UI has no keyboard focus.
    RestoreSteamKeyboardFocus(window);
    await SendSteam(SteamPlan(action=="steamQuickAccess"?"steamQuickAccess":"steamMenu",false),
     ()=>!cancellation.IsCancellationRequested&&current()&&GetForegroundWindow()==window&&!NativeGameBarState.IsInputRedirected());
   }
   return new(action,launched,menu?(action=="steamQuickAccess"?"quickAccess":"main"):null,GetForegroundWindow()==window&&!NativeGameBarState.IsInputRedirected());
  }
  finally{oemSteamGate.Release();}
 }
 [StructLayout(LayoutKind.Sequential)]
 private struct GuiThreadInfo
 {
  internal uint Size,Flags;
  internal nint Active,Focus,Capture,MenuOwner,MoveSize,Caret;
  internal int CaretLeft,CaretTop,CaretRight,CaretBottom;
 }
 [StructLayout(LayoutKind.Sequential)]
 private struct SteamPoint { internal int X,Y; }
 [StructLayout(LayoutKind.Sequential)]
 private struct SteamBounds { internal int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")]private static extern bool EnumChildWindows(nint window,EnumWindow callback,nint parameter);
 [DllImport("user32.dll")]private static extern bool GetGUIThreadInfo(uint thread,ref GuiThreadInfo state);
 [DllImport("user32.dll")]private static extern bool GetWindowRect(nint window,out SteamBounds bounds);
 [DllImport("user32.dll")]private static extern nint GetAncestor(nint window,uint flags);
 [DllImport("user32.dll")]private static extern nint WindowFromPoint(SteamPoint point);
 [DllImport("user32.dll")]private static extern nint SetThreadDpiAwarenessContext(nint context);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowText(nint window,StringBuilder text,int count);
}
