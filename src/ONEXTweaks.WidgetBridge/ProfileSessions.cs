// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ONEXTweaks.WidgetBridge;

internal sealed record ProfileCandidate(ProfileSession Session,bool Game);
internal sealed class ProfileSessions(Func<IReadOnlyList<RtssApp>> applications,Func<DetectedGame?>? previousGame=null)
{
    private ProfileSession? previous;
    private static readonly HashSet<string> Excluded=new(StringComparer.OrdinalIgnoreCase)
    {"explorer","dwm","ApplicationFrameHost","SystemSettings","SearchHost","StartMenuExperienceHost","ShellExperienceHost","TextInputHost","GameBar","GameBarFTServer","GameBarPresenceWriter","XboxGamingBar","steam","steamwebhelper","RTSS","RTSSHooksLoader64","RTSSHooksLoader","ONEXRenderProbe","chrome","msedge","firefox","devenv","powershell","pwsh","cmd","OpenConsole","WindowsTerminal"};
    private delegate bool EnumerateWindow(nint window,nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumerateWindow callback,nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window,out uint pid);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern bool QueryFullProcessImageName(nint process,uint flags,StringBuilder path,ref uint size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint process);
    internal static ProfileSession? Identify(int pid)
    {
        try
        {
            using var process=Process.GetProcessById(pid);
            if(process.SessionId!=Process.GetCurrentProcess().SessionId||process.HasExited)return null;
            nint handle=OpenProcess(0x1000,false,(uint)pid);if(handle==0)return null;
            try
            {
                var buffer=new StringBuilder(32768);uint length=(uint)buffer.Capacity;
                if(!QueryFullProcessImageName(handle,0,buffer,ref length))return null;
                string path=buffer.ToString();return new(pid,process.StartTime.ToUniversalTime().Ticks,path,System.IO.Path.GetFileNameWithoutExtension(path));
            }
            finally{CloseHandle(handle);}
        }
        catch(Exception error) when(error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){return null;}
    }
    internal static bool Alive(ProfileSession session)
    {var current=Identify(session.ProcessId);return current!=null&&current.StartedUtcTicks==session.StartedUtcTicks&&current.Path.Equals(session.Path,StringComparison.OrdinalIgnoreCase);}
    private static bool Eligible(ProfileSession session)=>!Excluded.Contains(session.Name)&&!session.Name.StartsWith("ONEXTweaks",StringComparison.OrdinalIgnoreCase)&&!session.Path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows)+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    internal ProfileSession? Retained()
    {
        var session=Volatile.Read(ref previous);
        return session!=null&&Alive(session)?session:null;
    }
    internal ProfileSession? Current()
    {
        if(DesktopActions.IsGameBarForeground()||NativeGameBarState.IsInputRedirected())
        {
            if(Retained() is {} retained)return retained;
            var game=previousGame?.Invoke();var restored=game==null?null:Identify(game.ProcessId);
            if(restored==null||!Eligible(restored)||!restored.Path.Equals(game!.Path,StringComparison.OrdinalIgnoreCase))restored=null;
            Volatile.Write(ref previous,restored);return restored;
        }
        GetWindowThreadProcessId(DesktopActions.ForegroundHandle,out uint pid);
        var current=pid==0?null:Identify((int)pid);
        if(current!=null&&!Eligible(current))current=null;
        Volatile.Write(ref previous,current);return current;
    }
    internal ProfileCandidate[] Candidates()
    {
        var pids=new HashSet<int>();
        EnumWindows((window,_)=>{if(IsWindowVisible(window)){GetWindowThreadProcessId(window,out uint pid);if(pid>0)pids.Add((int)pid);}return true;},0);
        return MergeCandidates(pids,applications(),Retained(),Identify,Eligible);
    }
    internal static ProfileCandidate[] MergeCandidates(IEnumerable<int> visible,IReadOnlyList<RtssApp> applications,
        ProfileSession? retained,Func<int,ProfileSession?> identify,Func<ProfileSession,bool> eligible)
    {
        var games=applications.Where(app=>app.ProcessId<=int.MaxValue&&(app.Flags&0xFFFF)!=0&&app.Fps>1)
            .ToLookup(app=>(int)app.ProcessId);
        var hasWindow=visible.ToHashSet();
        var pids=hasWindow.Concat(games.Select(group=>group.Key));
        if(retained!=null)pids=pids.Append(retained.ProcessId);
        return pids.Where(pid=>pid>0).Distinct().Select(identify).OfType<ProfileSession>().Where(eligible)
            .Select(session=>(session,game:games[session.ProcessId].Any(app=>app.Name.Equals(Path.GetFileName(session.Path),StringComparison.OrdinalIgnoreCase)),
                retained:retained!=null&&session.ProcessId==retained.ProcessId&&session.StartedUtcTicks==retained.StartedUtcTicks&&
                    session.Path.Equals(retained.Path,StringComparison.OrdinalIgnoreCase)))
            .Where(item=>item.game||item.retained||hasWindow.Contains(item.session.ProcessId))
            .OrderByDescending(item=>item.retained).ThenByDescending(item=>item.game).ThenBy(item=>item.session.ProcessId)
            .GroupBy(item=>item.session.Path,StringComparer.OrdinalIgnoreCase).Select(group=>group.First())
            .OrderByDescending(item=>item.retained).ThenByDescending(item=>item.game)
            .ThenBy(item=>item.session.Name,StringComparer.CurrentCultureIgnoreCase).Take(128)
            .Select(item=>new ProfileCandidate(item.session,item.game)).ToArray();
    }
}
