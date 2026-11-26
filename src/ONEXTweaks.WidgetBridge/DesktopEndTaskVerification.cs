// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ONEXTweaks.WidgetBridge;

internal sealed partial class DesktopActions
{
    internal static int VerifyNativeClose(string output)
    {
        var checks=new List<string>();
        string? error=null;
        using var process=Process.GetCurrentProcess();
        using var desktop=new DesktopActions(trackForeground:false);
        var windows=new List<nint>();
        void Check(bool condition,string name)
        {
            if(!condition)throw new IOException(name);
            checks.Add(name);
        }
        nint CreateFixture()
        {
            const uint noActivateToolWindow=0x08000080;
            const uint visiblePopup=0x90000000;
            nint window=CreateEndTaskFixture(noActivateToolWindow,"Static","ONEXTweaks disposable End Task fixture",visiblePopup,
                -20000,-20000,100,100,0,0,0,0);
            if(window==0)throw new IOException("Could not create the native test window");
            windows.Add(window);
            return window;
        }
        try
        {
            if(process.SessionId==0)throw new IOException("Interactive Windows session is required");
            var identity=ProfileSessions.Identify(process.Id)??throw new IOException("Could not identify the test fixture process");
            nint first=CreateFixture();
            var sessions=new ProfileSessions(()=>[]);
            Check(sessions.Candidates().Any(candidate=>candidate.Session==identity),
                "The app picker discovers a running native window without RTSS or a Performance profile");
            desktop.ObserveForeground(first,false);
            var original=desktop.target??throw new IOException("The test window was not admitted");
            desktop.RetainedApplication=()=>identity;
            desktop.ObserveForeground(0,true);
            Check(desktop.target==original,"Game Bar input redirection retains the exact previous native window");
            Check(DestroyEndTaskFixture(first),"Fixture closed its old window");
            nint second=CreateFixture();
            nint other=CreateFixture();
            desktop.ObserveForeground(0,true);
            Check(desktop.target==null||!Valid(desktop.target),"No replacement chosen when a process has two matching windows");
            Check(DestroyEndTaskFixture(other),"Fixture removed its extra window");
            desktop.ObserveForeground(0,true);
            var replacement=desktop.target??throw new IOException("Missing native replacement");
            Check(replacement.Window==second&&replacement.Token!=original.Token,"Same running game can replace its destroyed window");
            var rejected=desktop.Execute("closeWindow",original.Token,false).GetAwaiter().GetResult();
            Check(!rejected.Ok&&IsWindow(second),"Stale window token cannot close the replacement window");
            var result=desktop.Execute("closeWindow",replacement.Token,false).GetAwaiter().GetResult();
            while(PeekEndTaskFixtureMessage(out var message,second,0,0,1))DispatchEndTaskFixtureMessage(ref message);
            Check(result.Ok&&result.CloseRequested&&!IsWindow(second),"End Task sends WM_CLOSE and the native fixture window closes");
        }
        catch(Exception failure){error=failure.ToString();}
        finally{foreach(nint window in windows)if(IsWindow(window))DestroyEndTaskFixture(window);}
        File.WriteAllText(output,JsonSerializer.Serialize(new{passed=error==null,checks,error},Backend.Program.Json));
        return error==null?0:1;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct EndTaskFixtureMessage
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X,Y;
        public uint Private;
    }
    [DllImport("user32.dll",EntryPoint="CreateWindowExW",CharSet=CharSet.Unicode)]
    private static extern nint CreateEndTaskFixture(uint extended,string windowClass,string name,uint style,int x,int y,int width,int height,nint parent,nint menu,nint module,nint data);
    [DllImport("user32.dll",EntryPoint="DestroyWindow")]
    private static extern bool DestroyEndTaskFixture(nint window);
    [DllImport("user32.dll",EntryPoint="PeekMessageW")]
    private static extern bool PeekEndTaskFixtureMessage(out EndTaskFixtureMessage message,nint window,uint first,uint last,uint remove);
    [DllImport("user32.dll",EntryPoint="DispatchMessageW")]
    private static extern nint DispatchEndTaskFixtureMessage(ref EndTaskFixtureMessage message);
}
