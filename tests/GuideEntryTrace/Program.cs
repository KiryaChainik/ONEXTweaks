// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ONEXTweaks.WidgetBridge;

if(args.Length!=2)return 2;
string output=Path.GetFullPath(args[0]);int seconds=Math.Clamp(int.Parse(args[1]),10,120);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
using var writer=new StreamWriter(output,false){AutoFlush=true};
nint library=NativeLibrary.Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"xinput1_4.dll"));
try
{
    var read=Marshal.GetDelegateForFunctionPointer<Native.ReadState>(Native.GetOrdinal(library,100));
    var timer=Stopwatch.StartNew();string previous="";long savedAt=-1000;
    while(timer.Elapsed.TotalSeconds<seconds)
    {
        uint status=read(0,out var state);bool down=(state.Pad.Buttons&0x400)!=0;bool bar=NativeGameBarState.IsInputRedirected();bool winG=(Native.GetAsyncKeyState(0x5b)<0||Native.GetAsyncKeyState(0x5c)<0)&&Native.GetAsyncKeyState(0x47)<0;
        nint hwnd=Native.GetForegroundWindow();Native.GetWindowThreadProcessId(hwnd,out uint pid);
        string fingerprint=$"{status}/{down}/{bar}/{winG}/{pid}";
        if(fingerprint!=previous||timer.ElapsedMilliseconds-savedAt>=500)
        {
            string focus="other";try{using var p=Process.GetProcessById((int)pid);if(p.ProcessName.Contains("GameBar",StringComparison.OrdinalIgnoreCase))focus=p.ProcessName;}catch(Exception e) when(e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){}
            writer.WriteLine(JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,ms=timer.ElapsedMilliseconds,status,down,bar,winG,focus,foregroundPid=pid}));previous=fingerprint;savedAt=timer.ElapsedMilliseconds;
        }
        Thread.Sleep(5);
    }
    writer.WriteLine(JsonSerializer.Serialize(new{done=true,utc=DateTimeOffset.UtcNow,scope="Read-only Guide bit, GameBar input state, Win+G chord and GameBar-only foreground process names; no other keys/windows captured"}));
    return 0;
}
catch(Exception e){writer.WriteLine(JsonSerializer.Serialize(new{error=e.ToString()}));return 1;}
finally{NativeLibrary.Free(library);}
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]internal struct Pad{internal ushort Buttons;internal byte Left,Right;internal short Lx,Ly,Rx,Ry;}
    [StructLayout(LayoutKind.Sequential)]internal struct State{internal uint Packet;internal Pad Pad;}
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]internal delegate uint ReadState(uint index,out State state);
    [DllImport("kernel32.dll",EntryPoint="GetProcAddress")]internal static extern nint GetOrdinal(nint module,nint ordinal);
    [DllImport("user32.dll")]internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]internal static extern uint GetWindowThreadProcessId(nint window,out uint pid);
    [DllImport("user32.dll")]internal static extern short GetAsyncKeyState(int key);
}
