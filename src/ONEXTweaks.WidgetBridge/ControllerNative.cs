// SPDX-License-Identifier: AGPL-3.0-only
using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerNative
{
 [StructLayout(LayoutKind.Sequential)]private struct Pad{public ushort Buttons;public byte Lt,Rt;public short Lx,Ly,Rx,Ry;}
 [StructLayout(LayoutKind.Sequential)]private struct State{public uint Packet;public Pad Pad;}
 [StructLayout(LayoutKind.Sequential)]private struct Vibration{public ushort Large,Small;}
 [DllImport("xinput1_4.dll",ExactSpelling=true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern uint XInputGetState(uint index,out State value);
 [DllImport("xinput1_4.dll",ExactSpelling=true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern uint XInputSetState(uint index,ref Vibration value);
 internal static bool Read(int index,out ControllerFrame frame)=>Read(index,out frame,out _);
 internal static bool Read(int index,out ControllerFrame frame,out uint packet)
 {
  bool ok=XInputGetState((uint)index,out var state)==0;
  packet=state.Packet;
  var p=state.Pad;
  frame=new(p.Buttons,p.Lt,p.Rt,p.Lx,p.Ly,p.Rx,p.Ry);
  return ok;
 }
 internal static int[] Slots()=>Enumerable.Range(0,4).Where(i=>Read(i,out _)).ToArray();
 internal static uint RumbleResult(int index,byte large,byte small){var v=new Vibration{Large=(ushort)(large*257),Small=(ushort)(small*257)};return XInputSetState((uint)index,ref v);}
 internal static void Rumble(int index,byte large,byte small)=>RumbleResult(index,large,small);
 internal static void RumbleChecked(int index,byte large,byte small){uint result=RumbleResult(index,large,small);if(result!=0)throw new IOException("The controller did not accept vibration (XInput "+result+")");}
 internal static bool Neutral(ControllerFrame p)=>p.Buttons==0&&p.Lt<15&&p.Rt<15&&Math.Abs((int)p.Lx)<6000&&Math.Abs((int)p.Ly)<6000&&Math.Abs((int)p.Rx)<6000&&Math.Abs((int)p.Ry)<6000;
 [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)]private static extern int CM_Locate_DevNodeW(out uint node,string id,uint flags);
 [DllImport("cfgmgr32.dll")]private static extern int CM_Get_Parent(out uint parent,uint node,uint flags);
 [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)]private static extern int CM_Get_Device_IDW(uint node,StringBuilder text,uint length,uint flags);
 internal static string[] Ancestors(string id){var values=new List<string>();if(CM_Locate_DevNodeW(out uint node,id,0)!=0)return [];for(int n=0;n<8&&CM_Get_Parent(out node,node,0)==0;n++){var text=new StringBuilder(512);if(CM_Get_Device_IDW(node,text,512,0)!=0)break;values.Add(text.ToString());}return values.ToArray();}
 internal static ControllerSource[] Sources()
 {
  var nodes=new List<(string Id,string Class)>();using var query=new ManagementObjectSearcher("SELECT PNPDeviceID,PNPClass,ConfigManagerErrorCode FROM Win32_PnPEntity");using var results=query.Get();
  foreach(ManagementObject row in results){using(row){if(Convert.ToUInt32(row["ConfigManagerErrorCode"]??1)!=0)continue;string id=(string?)row["PNPDeviceID"]??"",kind=(string?)row["PNPClass"]??"";if(id.Contains("VID_045E&PID_028E",StringComparison.OrdinalIgnoreCase))nodes.Add((id,kind));}}
  var roots=nodes.Where(n=>n.Class.Equals("XnaComposite",StringComparison.OrdinalIgnoreCase)&&n.Id.StartsWith(@"USB\",StringComparison.OrdinalIgnoreCase)&&!Ancestors(n.Id).Any(p=>p.Contains("VIGEM",StringComparison.OrdinalIgnoreCase)||p.Contains("VIIPER",StringComparison.OrdinalIgnoreCase)||p.Contains("USBIP",StringComparison.OrdinalIgnoreCase))).ToArray();
  return roots.Select(root=>new ControllerSource(root.Id,nodes.Where(n=>n.Id.Equals(root.Id,StringComparison.OrdinalIgnoreCase)||Ancestors(n.Id).Contains(root.Id,StringComparer.OrdinalIgnoreCase)).Select(n=>n.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())).ToArray();
 }
}
internal sealed record ControllerSource(string UsbInstance,string[] InstanceIds);
internal sealed class ControllerVirtualPad:IDisposable
{
 private readonly ControllerViiperDevice device;private readonly object gate=new();private ControllerFrame last;private bool guide,mappedGuide,disposed;private readonly ControllerRumbleForwarder rumble;private int probeFeedback;
 internal int Slot{get;}internal int ProbeFeedbackCount=>Volatile.Read(ref probeFeedback);
 internal void EnableRumbleForwarding()=>rumble.Enable();
 internal void RefreshVibration()=>rumble.Refresh();
 internal Task<(byte Large,byte Small)> TestVibration(ControllerVibration value)=>rumble.Test(value);
 internal ControllerVirtualPad(int sourceSlot,bool feedback=true,Action<byte,byte>? inspectFeedback=null,Func<ControllerVibration>? vibration=null,Action<byte,byte>? inspectForwarded=null)
 {
  rumble=new(vibration??(()=>new ControllerVibration()),(large,small)=>{ControllerNative.RumbleChecked(sourceSlot,large,small);inspectForwarded?.Invoke(large,small);});if(feedback)rumble.Enable();int[] before=ControllerNative.Slots();device=new();
  try
  {
   device.Feedback+=(large,small)=>{if(large==1&&small==0)Interlocked.Increment(ref probeFeedback);inspectFeedback?.Invoke(large,small);rumble.Feedback(large,small);};
   int slot=-1;for(int n=0;n<160;n++){var added=ControllerNative.Slots().Except(before).ToArray();if(added.Length>1)throw new IOException("Additional controllers appeared while VIIPER was starting");if(added.Length==1){slot=added[0];break;}Thread.Sleep(50);}
   if(slot<0||slot==sourceSlot)throw new IOException("No independent VIIPER controller slot is available");Slot=slot;
  }
  catch{rumble.Dispose();device.Dispose();throw;}
 }
 internal void Submit(ControllerFrame frame,bool mappingGuide=false){lock(gate){ObjectDisposedException.ThrowIf(disposed,this);last=frame;mappedGuide=mappingGuide;device.Submit(frame,guide||mappedGuide);}}
 internal void Guide(bool pressed){lock(gate){ObjectDisposedException.ThrowIf(disposed,this);guide=pressed;device.Submit(last,guide||mappedGuide);}}
 public void Dispose()
 {
  lock(gate)
  {
   if(disposed)return;disposed=true;rumble.Dispose();device.Dispose();
   // USBIP teardown and XInput enumeration are asynchronous. Do not start a new
   // capture, or restore the physical device, while our neutral output lingers.
   for(int n=0;n<1500&&ControllerNative.Read(Slot,out _);n++)Thread.Sleep(10);
   if(ControllerNative.Read(Slot,out _))throw new IOException("The owned VIIPER output did not disappear after removal");
  }
 }
}
internal sealed class ControllerInputEmitter:IDisposable
{
 internal const nuint OwnTag=0x4f4e4558;
 [StructLayout(LayoutKind.Sequential)]private struct Mouse{internal int X,Y;internal uint Data,Flags,Time;internal nuint Extra;}
 [StructLayout(LayoutKind.Sequential)]private struct Keyboard{internal ushort Key,Scan;internal uint Flags,Time;internal nuint Extra;}
 [StructLayout(LayoutKind.Explicit)]private struct Union{[FieldOffset(0)]internal Mouse Mouse;[FieldOffset(0)]internal Keyboard Keyboard;}
 [StructLayout(LayoutKind.Sequential)]private struct Input{internal uint Kind;internal Union Value;}
 [DllImport("user32.dll",SetLastError=true)]private static extern uint SendInput(uint count,in Input input,int size);
 [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
 [StructLayout(LayoutKind.Sequential)]private struct CursorPoint{internal int X,Y;}
 [DllImport("user32.dll",SetLastError=true)]private static extern bool GetCursorPos(out CursorPoint point);
 [DllImport("user32.dll",SetLastError=true)]private static extern bool GetPhysicalCursorPos(out CursorPoint point);
 [DllImport("user32.dll",SetLastError=true)]private static extern nint SetThreadDpiAwarenessContext(nint context);
 [DllImport("user32.dll")]private static extern int GetSystemMetrics(int index);
 private static readonly object desktopGate=new();
 private readonly HashSet<int> keys=[];private readonly HashSet<string> mouse=[];private double remainderX,remainderY;private readonly DesktopCursorMotion desktopCursor=new();private readonly KeyboardMouseWheelMotion wheelMotion=new();private readonly Action<int[],string[]>? journal;
 internal ControllerInputEmitter(Action<int[],string[]>? persist=null){journal=persist;}
 private static bool Modifier(int key)=>key is 16 or 17 or 18 or 91 or 92 or >=160 and <=165;
 private static readonly int InputSize=Marshal.SizeOf<Input>();
 private static void Emit(Input input){if(SendInput(1,in input,InputSize)!=1)throw new Win32Exception(Marshal.GetLastWin32Error(),"Windows rejected mapped keyboard/mouse input");}
 internal const int NumpadEnter=0x10D;
 internal static bool IsSupportedKey(int key)=>key==NumpadEnter||key is >=8 and <=254 and not(>=195 and <=218);
 private static void Key(int code,bool up)=>Emit(new(){Kind=1,Value=new(){Keyboard=new(){Key=(ushort)(code&255),Flags=(up?2u:0u)|(code is NumpadEnter or >=33 and <=40 or 44 or 45 or 46 or 91 or 92 or 93 or 111 or 144 or 163 or 165?1u:0u),Extra=OwnTag}}});
 internal static (uint Flags,uint Data) MouseButton(string key,bool up)=>key switch
 {
  "left"=>(up?4u:2u,0u),"right"=>(up?16u:8u,0u),"middle"=>(up?64u:32u,0u),
  "x1"=>(up?0x100u:0x80u,1u),"x2"=>(up?0x100u:0x80u,2u),
  _=>throw new ArgumentException("Unknown mouse button")
 };
 private static void Button(string key,bool up)
 {
  var (flags,data)=MouseButton(key,up);
  Emit(new(){Value=new(){Mouse=new(){Flags=flags,Data=data,Extra=OwnTag}}});
 }
 private static void Scroll(int delta,uint flags)
 {
  if(delta!=0)Emit(new(){Value=new(){Mouse=new(){Data=unchecked((uint)delta),Flags=flags,Extra=OwnTag}}});
 }
 internal static void EmitContinuousWheel(int horizontal,int vertical){Scroll(vertical,0x800);Scroll(horizontal,0x1000);}
 internal void Apply(ControllerOutput value,double seconds,bool continuous=true)
 {
  bool changed=!keys.SetEquals(value.Keys)||!mouse.SetEquals(value.Mouse);if(changed)journal?.Invoke(keys.Union(value.Keys).Order().ToArray(),mouse.Union(value.Mouse).Order().ToArray());
  foreach(string key in mouse.Except(value.Mouse).ToArray()){Button(key,true);mouse.Remove(key);}
  foreach(int key in keys.Except(value.Keys).OrderBy(k=>Modifier(k)?1:0).ThenBy(k=>k).ToArray()){Key(key,true);keys.Remove(key);}foreach(int key in value.Keys.OrderBy(k=>Modifier(k)?0:1).ThenBy(k=>k))if(!keys.Contains(key)||(GetAsyncKeyState(key&255)&0x8000)==0){Key(key,false);keys.Add(key);}
  foreach(string key in value.Mouse.Except(mouse)){Button(key,false);mouse.Add(key);}
  if(changed)journal?.Invoke(keys.Order().ToArray(),mouse.Order().ToArray());
  Scroll(value.Wheel*ControllerOutput.WheelDeltaPerNotch,0x800);
  Scroll(value.HorizontalWheel*ControllerOutput.WheelDeltaPerNotch,0x1000);
  (int X,int Y) desktop=default;
  if(value.DesktopCursor&&continuous)
  {desktop=desktopCursor.Next(value.CursorX,value.CursorY,value.CursorSpeedScale,seconds);var delta=wheelMotion.Next(value,seconds);EmitContinuousWheel(delta.Horizontal,delta.Vertical);}
  else{desktopCursor.Reset();wheelMotion.Reset();}
  MoveDesktop((long)desktop.X+value.CursorNudgeX,(long)desktop.Y+value.CursorNudgeY,desktop!=(0,0));
  if(value.DesktopCursor)remainderX=remainderY=0;
  else{remainderX+=value.CursorX/32768.0*650*value.CursorSpeedScale*seconds;remainderY-=value.CursorY/32768.0*650*value.CursorSpeedScale*seconds;}
  int x=(int)remainderX,y=(int)remainderY;remainderX-=x;remainderY-=y;x+=value.RelativeMouseX;y+=value.RelativeMouseY;if(x!=0||y!=0)Emit(new(){Value=new(){Mouse=new(){X=x,Y=y,Flags=1,Extra=OwnTag}}});
 }
 internal static void MoveDesktop(long x,long y,bool continuous)
 {
  if(x==0&&y==0)return;
  nint dpiContext=SetThreadDpiAwarenessContext(-4);
  try{MoveDesktopCore(x,y,continuous,physicalCoordinates:false);}
  finally{RestoreDesktopDpi(dpiContext);}
 }

 internal static nint EnterDesktopDpi()
 {
  nint previous=SetThreadDpiAwarenessContext(-4);
  return previous!=0?previous:throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot set the desktop motion thread DPI");
 }

 internal static void RestoreDesktopDpi(nint previous)
 {
  if(previous!=0)SetThreadDpiAwarenessContext(previous);
 }

 // The continuous sender owns a per-monitor DPI context for its whole life.
 internal static void MoveContinuousDesktop(int x,int y)=>
  MoveDesktopCore(x,y,continuous:true,physicalCoordinates:true);

 private static void MoveDesktopCore(long x,long y,bool continuous,bool physicalCoordinates)
 {
  lock(desktopGate)
  {
   CursorPoint point;
   bool read=physicalCoordinates?GetPhysicalCursorPos(out point):GetCursorPos(out point);
   if(!read)throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot read desktop cursor");
   int left=GetSystemMetrics(76),top=GetSystemMetrics(77),width=GetSystemMetrics(78),height=GetSystemMetrics(79);
   if(width<2||height<2)throw new IOException("Virtual desktop is unavailable");
   int absoluteX=DesktopCursorMotion.PixelCoordinate(point.X+x,left,width),absoluteY=DesktopCursorMotion.PixelCoordinate(point.Y+y,top,height);
   const uint absoluteVirtualDesktop=0xc001,noCoalescing=0x2000;
   uint flags=continuous?absoluteVirtualDesktop|noCoalescing:absoluteVirtualDesktop;
   Emit(new(){Value=new(){Mouse=new(){X=absoluteX,Y=absoluteY,Flags=flags,Extra=OwnTag}}});
  }
 }
 internal static void ReleaseRecorded(int[]? heldKeys,string[]? heldMouse)
 {
  if((heldKeys?.Length??0)>256||(heldKeys??[]).Any(k=>!IsSupportedKey(k))||(heldMouse?.Length??0)>5||(heldMouse??[]).Any(k=>k is not ("left" or "right" or "middle" or "x1" or "x2")))throw new InvalidDataException("Invalid owned input recovery record");
  foreach(string key in (heldMouse??[]).Distinct())Button(key,true);foreach(int key in (heldKeys??[]).Distinct().OrderBy(k=>Modifier(k)?1:0))Key(key,true);
 }
 public void Dispose(){foreach(string key in mouse.ToArray())try{Button(key,true);mouse.Remove(key);}catch{}foreach(int key in keys.OrderBy(k=>Modifier(k)?1:0).ToArray())try{Key(key,true);keys.Remove(key);}catch{}try{journal?.Invoke(keys.Order().ToArray(),mouse.Order().ToArray());}catch{}remainderX=remainderY=0;desktopCursor.Reset();wheelMotion.Reset();}
}
