// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class DesktopControllerVerification
{
    [StructLayout(LayoutKind.Sequential)]private struct Pad{internal ushort Buttons;internal byte L,R;internal short Lx,Ly,Rx,Ry;}
    [StructLayout(LayoutKind.Sequential)]private struct State{internal uint Packet;internal Pad Pad;}
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate uint ReadState(uint index,out State state);
    [DllImport("kernel32.dll",EntryPoint="GetProcAddress")]private static extern nint GetOrdinal(nint library,nint ordinal);
    internal static async Task<int> Verify(string report)
    {
        var cases=new[]{("steamMenu",false,"Ctrl+1",true),("steamQuickAccess",false,"Ctrl+2",true),("steamOverlay",false,"Shift+Tab",false),("steamContext",false,"Ctrl+1",true),("steamContext",true,"Shift+Tab",false)};
        nint library=0;
        try
        {
            using(var gone=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe"),"/c exit"){CreateNoWindow=true,UseShellExecute=false})!)
            {int pid=gone.Id;gone.WaitForExit();bool rejected=false;try{WidgetPipe.VerifyClientProcess((uint)pid);}catch(IOException){rejected=true;}if(!rejected)throw new IOException("Exited pipe client was accepted");}
            WidgetPipe.VerifyClientProcess((uint)Environment.ProcessId);
            foreach(var c in cases)if(DesktopActions.SteamPlan(c.Item1,c.Item2)!=new ControllerActionPlan(c.Item3,c.Item4))throw new IOException("Steam dispatch mismatch");
            library=NativeLibrary.Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"xinput1_4.dll"));var read=Marshal.GetDelegateForFunctionPointer<ReadState>(GetOrdinal(library,100));
            var samples=new List<(int Index,bool Down)>();var tapping=VirtualGuide.Tap();
            while(!tapping.IsCompleted){for(uint i=0;i<4;i++)if(read(i,out var state)==0)samples.Add(((int)i,(state.Pad.Buttons&0x400)!=0));await Task.Delay(5);}
            int index=await tapping;await Task.Delay(30);if(read((uint)index,out var released)!=0||(released.Pad.Buttons&0x400)!=0)throw new IOException("Virtual Guide did not release");
            int count=samples.Count(x=>x.Index==index&&x.Down);if(count<2)throw new IOException("No real XInput Guide-down observed");
            string? chrome=null;string? chromeError=null;try{chrome=DesktopActions.ResolveChrome();}catch(FileNotFoundException e){chromeError=e.Message;}
            await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed=true,exitedPipeClientRejected=true,liveClientAuthenticated=true,steamPlans=cases.Select(c=>new{action=c.Item1,gameRunning=c.Item2,shortcut=c.Item3,legacy=c.Item4}),guide=new{index,downSamples=count,released=true,winGUsed=false},chrome,chromeError,steamUiTested=false},Backend.Program.Json));return 0;
        }
        catch(Exception e){await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed=false,error=e.ToString()},Backend.Program.Json));return 1;}
        finally{VirtualGuide.Disconnect();if(library!=0)NativeLibrary.Free(library);}
    }
}
