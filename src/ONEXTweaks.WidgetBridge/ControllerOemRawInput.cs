// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
namespace ONEXTweaks.WidgetBridge;

internal sealed class ControllerOemRawInput:IDisposable
{
 private readonly CancellationTokenSource stop=new();private readonly Thread thread;
 private readonly object gate=new();private readonly ControllerOemSignalState state=new();private readonly Dictionary<string,long> presses=ControllerOemPolicy.Inputs.ToDictionary(id=>id,id=>0L),releases=ControllerOemPolicy.Inputs.ToDictionary(id=>id,id=>0L);
 private readonly object gestureGate=new();private readonly ControllerOemGestureState gestures=new();private Func<ControllerOemPolicy> gesturePolicy=()=>fallbackGesturePolicy;private static readonly ControllerOemPolicy fallbackGesturePolicy=new ControllerOemPolicy().Validate();private long inputGeneration;
 private int hookSource,firmwareAliases,keyboardSwitch;private readonly ConcurrentQueue<(ControllerOemSignal Signal,long Generation)> pending=new();
 private readonly Dictionary<nint,bool> allowed=[];private readonly ControllerOemKeyboardDecoder keyboard=new();
 private readonly WindowCallback callback;private bool listening,vendorSeen,keyboardSeen;private string? error;
 private readonly TouchCornerDigitizer? touch;
 private readonly Action? onSessionEnd;
 internal event Action<ControllerOemSignal>? Signal;
 internal event Action<ControllerOemSignal>? ActionSignal;
 internal void ConfigureGestures(Func<ControllerOemPolicy> current){gesturePolicy=current;RefreshGestures();}
 private void DispatchGestures(IEnumerable<ControllerOemSignal> signals){foreach(var value in signals)try{ActionSignal?.Invoke(value);}catch(Exception failure){lock(gate)error=failure.GetType().Name;}}
 internal void RefreshGestures(){ControllerOemSignal[] values;long now=Environment.TickCount64;var current=gesturePolicy();lock(gate){inputGeneration++;var held=state.Held;lock(gestureGate)values=gestures.Reconfigure(current,GestureEnabled,now,held);}DispatchGestures(values);}
 private void TickGestures(){ControllerOemSignal[] values;long now=Environment.TickCount64;lock(gestureGate){var changed=gestures.Update(gesturePolicy(),GestureEnabled,now);var expired=gestures.Tick(now);values=changed.Concat(expired).ToArray();}DispatchGestures(values);}
 private void CancelGestures(){ControllerOemSignal[] values;lock(gestureGate)values=gestures.Cancel();DispatchGestures(values);}
 private object ReadGestures(){lock(gestureGate)return gestures.Read();}
 internal ControllerOemRawInput(Action? onTouchCorner=null,Action? onSessionEnd=null){this.onSessionEnd=onSessionEnd;if(onTouchCorner!=null)touch=new TouchCornerDigitizer(onTouchCorner);callback=Message;thread=new Thread(Pump){IsBackground=true,Name="ONEXTweaks OEM input"};thread.Start();}
 internal bool KeyboardSwitchEnabled=>Volatile.Read(ref keyboardSwitch)==1;
 internal bool GestureEnabled=>OwnsAliases||KeyboardSwitchEnabled;
 internal void ReserveKeyboardSwitch(bool enabled){Volatile.Write(ref keyboardSwitch,enabled?1:0);RefreshGestures();}
 internal bool OwnsAliases=>Volatile.Read(ref firmwareAliases)==1;
 internal bool Listening{get{lock(gate)return listening;}}
 internal object Read(){lock(gate)return new{listening,vendorSeen,keyboardSeen,held=state.Held,presses=presses.ToDictionary(pair=>pair.Key,pair=>pair.Value),releases=releases.ToDictionary(pair=>pair.Key,pair=>pair.Value),error,gestures=ReadGestures(),touchCorner=touch?.Read(),inputRecording=false,firmwareWrites=0};}
 internal ushort HeldMask{get{lock(gate)return state.Mask;}}
 internal ushort RemapMask{get{lock(gestureGate)return OwnsAliases?gestures.Mask:(ushort)0;}}
 internal void HookSource(bool enabled)=>Volatile.Write(ref hookSource,enabled?1:0);
 internal void FirmwareAliases(bool enabled){Volatile.Write(ref firmwareAliases,enabled?1:0);RefreshGestures();}
 internal void FromHook(ControllerOemSignal signal)=>Publish(signal,"hook",true);
 private void Raise(ControllerOemSignal signal,bool route=true,long? generation=null)
 {lock(gate){if(signal.Pressed)presses[signal.Button]++;else releases[signal.Button]++;}try{Signal?.Invoke(signal);}catch(Exception e){lock(gate)error=e.GetType().Name;}
 ControllerOemSignal[] actions;long now=Environment.TickCount64;lock(gestureGate){if(generation.HasValue&&generation.Value!=Volatile.Read(ref inputGeneration))actions=[];else{var changed=gestures.Update(gesturePolicy(),GestureEnabled,now);actions=changed.Concat(gestures.Feed(route&&(OwnsAliases||KeyboardSwitchEnabled&&signal.Button=="KeyboardMouse")?signal:signal with{Cancelled=true},now)).ToArray();}}DispatchGestures(actions);}

 private void Publish(ControllerOemSignal signal,string origin,bool deferred=false)
 {ControllerOemSignal? changed;long generation;lock(gate){generation=inputGeneration;changed=state.Apply(signal,origin,Environment.TickCount64,Volatile.Read(ref firmwareAliases)==1);}if(changed!=null){if(deferred)pending.Enqueue((changed.Value,generation));else if(generation==Volatile.Read(ref inputGeneration))Raise(changed.Value,generation:generation);}}
 internal void ReleaseSource(string origin)
 {ControllerOemSignal[] changes;lock(gate){inputGeneration++;changes=state.ReleaseSource(origin,Environment.TickCount64);}CancelGestures();foreach(var signal in changes)Raise(signal,false);}
 private void ReleaseAll(){CancelGestures();ControllerOemSignal[] changes;lock(gate){inputGeneration++;changes=state.Reset();}foreach(var signal in changes)Raise(signal,false);keyboard.Reset();}
 private void ExpirePulses(){ControllerOemSignal[] changes;lock(gate)changes=state.Expire(Environment.TickCount64);foreach(var signal in changes)Raise(signal);}
 private bool IsOem(nint device)
 {
  if(allowed.TryGetValue(device,out bool value))return value;
  uint length=0;GetRawInputDeviceInfoW(device,0x20000007,null,ref length);if(length==0||length>2048)return allowed[device]=false;
  var name=new StringBuilder((int)length+1);GetRawInputDeviceInfoW(device,0x20000007,name,ref length);string path=name.ToString();
  // Identity is checked before reading payloads. Other keyboards/mice are never decoded.
  return allowed[device]=path.Contains("VID_1A86&PID_FE00",StringComparison.OrdinalIgnoreCase)||path.Contains("VID_1A86&PID_FE02",StringComparison.OrdinalIgnoreCase);
 }
 private nint Message(nint window,uint message,nuint w,nint l)
 {
  try
  {
   // A query may be cancelled. Release our capture only once Windows commits the session end.
   if(message==0x0016&&w!=0)onSessionEnd?.Invoke(); // WM_ENDSESSION
   if(message==0x218&&(w==4||w==7||w==0x12)){ReleaseAll();touch?.Reset();}
   if(message==0x007E)touch?.OnDisplayChanged(); // WM_DISPLAYCHANGE
   if(message==0xFE){bool tracked=allowed.GetValueOrDefault(l);allowed.Remove(l);if(tracked)ReleaseAll();if(w==2)touch?.Remove(l);else touch?.CanRead(l);}
   if(message!=0xFF)return DefWindowProcW(window,message,w,l);
   uint headerSize=(uint)Marshal.SizeOf<RawHeader>();nint metadata=Marshal.AllocHGlobal((int)headerSize);
   bool touchReport=false;
   try
   {
    uint size=headerSize;
    if(GetRawInputData(l,0x10000005,metadata,ref size,headerSize)!=headerSize)return DefWindowProcW(window,message,w,l);
    var header=Marshal.PtrToStructure<RawHeader>(metadata);
    touchReport=header.Type==2&&touch?.CanRead(header.Device)==true;
    if(!touchReport&&!IsOem(header.Device))return DefWindowProcW(window,message,w,l);
   }
   finally{Marshal.FreeHGlobal(metadata);}
   uint bytes=0;GetRawInputData(l,0x10000003,0,ref bytes,headerSize);if(bytes<headerSize||bytes>4096)return DefWindowProcW(window,message,w,l);
   nint buffer=Marshal.AllocHGlobal((int)bytes);
   try
   {
    if(GetRawInputData(l,0x10000003,buffer,ref bytes,headerSize)!=bytes)return DefWindowProcW(window,message,w,l);
    var header=Marshal.PtrToStructure<RawHeader>(buffer);nint body=buffer+(int)headerSize;
    if(touchReport)touch?.Observe(header.Device,body,bytes-headerSize);
    else if(header.Type==1&&bytes>=headerSize+Marshal.SizeOf<RawKeyboard>())
    {
     var key=Marshal.PtrToStructure<RawKeyboard>(body);lock(gate)keyboardSeen=true;
     if(Volatile.Read(ref hookSource)==0)
     {
      bool down=key.Message is 256 or 260;
      if(Volatile.Read(ref firmwareAliases)==1&&key.Vk is >=124 and <=127)Publish(new(new[]{"M1","M2","Home","GKey"}[key.Vk-124],down),"keyboard");
      else foreach(var signal in keyboard.Feed(key.Vk,down))Publish(signal,"keyboard");
     }
    }
    else if(header.Type==2&&bytes>=headerSize+8)
    {
     int length=Marshal.ReadInt32(body),count=Marshal.ReadInt32(body+4);
     if(length is not(64 or 65)||count<1||(long)length*count>bytes-headerSize-8)return DefWindowProcW(window,message,w,l);
     lock(gate)vendorSeen=true;var report=new byte[length];
     for(int i=0;i<count;i++){Marshal.Copy(body+8+i*length,report,0,length);var signal=ControllerOemVendorDecoder.Decode(report);if(signal!=null)Publish(signal.Value,"vendor");}
    }
   }
   finally{Marshal.FreeHGlobal(buffer);}
  }
  catch(Exception e){lock(gate)error=e.GetType().Name;ReleaseAll();}
  return DefWindowProcW(window,message,w,l);
 }
 private void Pump()
 {
  nint window=0,module=GetModuleHandleW(null);string name="ONEXTweaks.OemInput."+Environment.ProcessId+"."+Guid.NewGuid().ToString("N");
  try
  {
   var wc=new WindowClass{Size=(uint)Marshal.SizeOf<WindowClass>(),Callback=callback,Instance=module,Name=name};
   if(RegisterClassExW(ref wc)==0)throw new IOException("OEM input window registration failed");
   window=CreateWindowExW(0,name,"",0,0,0,0,0,0,0,module,0);if(window==0)throw new IOException("OEM input window unavailable");
   var devices=new[]{new RawDevice{Page=1,Usage=6,Flags=0x2100,Target=window},new RawDevice{Page=0xFF00,Usage=1,Flags=0x2100,Target=window}};
   if(!RegisterRawInputDevices(devices,(uint)devices.Length,(uint)Marshal.SizeOf<RawDevice>()))throw new IOException("OEM input subscription failed");lock(gate)listening=true;
   bool touchRegistered=touch!=null&&RegisterRawInputDevices([new RawDevice{Page=0x0d,Usage=4,Flags=0x2100,Target=window}],1,(uint)Marshal.SizeOf<RawDevice>());
   touch?.SetRegistered(touchRegistered);
   if(touchRegistered)touch?.Scan();
   while(!stop.IsCancellationRequested)
   {
    // A busy HID device must not starve cancellation or release-only pulse expiry.
    for(int n=0;n<64&&PeekMessageW(out var message,0,0,0,1);n++){TranslateMessage(ref message);DispatchMessageW(ref message);}
    while(pending.TryDequeue(out var signal))if(signal.Generation==Volatile.Read(ref inputGeneration))Raise(signal.Signal,generation:signal.Generation);ExpirePulses();TickGestures();Thread.Sleep(4);
   }
   var remove=devices.Select(d=>new RawDevice{Page=d.Page,Usage=d.Usage,Flags=1,Target=0}).ToArray();RegisterRawInputDevices(remove,(uint)remove.Length,(uint)Marshal.SizeOf<RawDevice>());
   if(touchRegistered)RegisterRawInputDevices([new RawDevice{Page=0x0d,Usage=4,Flags=1,Target=0}],1,(uint)Marshal.SizeOf<RawDevice>());
  }
  catch(Exception e){lock(gate)error=e.GetType().Name;}
  finally{lock(gate)listening=false;while(pending.TryDequeue(out _)){}ReleaseAll();touch?.SetRegistered(false);touch?.Dispose();if(window!=0)DestroyWindow(window);UnregisterClassW(name,module);}
 }
 public void Dispose(){stop.Cancel();if(Thread.CurrentThread!=thread)thread.Join(3000);stop.Dispose();}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct WindowClass{internal uint Size,Style;internal WindowCallback Callback;internal int ClassExtra,WindowExtra;internal nint Instance,Icon,Cursor,Background;internal string? Menu;internal string Name;internal nint SmallIcon;}
 [StructLayout(LayoutKind.Sequential)]private struct NativeMessage{internal nint Window;internal uint Id;internal nuint WParam;internal nint LParam;internal uint Time;internal int X,Y;internal uint Private;}
 [StructLayout(LayoutKind.Sequential)]private struct RawDevice{internal ushort Page,Usage;internal uint Flags;internal nint Target;}
 [StructLayout(LayoutKind.Sequential)]private struct RawHeader{internal uint Type,Size;internal nint Device;internal nuint WParam;}
 [StructLayout(LayoutKind.Sequential)]private struct RawKeyboard{internal ushort Make,Flags,Reserved,Vk;internal uint Message,Extra;}
 private delegate nint WindowCallback(nint window,uint message,nuint w,nint l);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern ushort RegisterClassExW(ref WindowClass value);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint CreateWindowExW(uint ex,string cls,string title,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint param);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool UnregisterClassW(string name,nint module);
 [DllImport("user32.dll")]private static extern bool DestroyWindow(nint window);
 [DllImport("user32.dll")]private static extern nint DefWindowProcW(nint window,uint message,nuint w,nint l);
 [DllImport("user32.dll")]private static extern bool RegisterRawInputDevices(RawDevice[] values,uint count,uint size);
 [DllImport("user32.dll")]private static extern uint GetRawInputData(nint input,uint command,nint data,ref uint size,uint headerSize);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint GetRawInputDeviceInfoW(nint device,uint command,StringBuilder? data,ref uint size);
 [DllImport("user32.dll")]private static extern bool PeekMessageW(out NativeMessage value,nint window,uint min,uint max,uint remove);
 [DllImport("user32.dll")]private static extern bool TranslateMessage(ref NativeMessage value);
 [DllImport("user32.dll")]private static extern nint DispatchMessageW(ref NativeMessage value);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern nint GetModuleHandleW(string? name);
}
