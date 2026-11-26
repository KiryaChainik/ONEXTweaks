// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal sealed record ProfileGameContext(DetectedGame Game,long StartedUtcTicks);
internal sealed class ProfileGameDetector(Func<IReadOnlyList<RtssApp>> applications,Action<Exception>? log=null,string? contextFile=null)
{
    private DetectedGame? previous;private ProfileGameContext? saved=Load(contextFile);private Task? startup;private long retry;
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowText(nint window,StringBuilder text,int size);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint window,out uint pid);
    [DllImport("kernel32.dll")]private static extern nint OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern bool QueryFullProcessImageName(nint process,uint flags,StringBuilder path,ref uint size);
    [DllImport("kernel32.dll")]private static extern bool CloseHandle(nint handle);
    private static string PathOf(uint pid){nint process=OpenProcess(0x1000,false,pid);if(process==0)throw new IOException("Cannot identify game process");try{var path=new StringBuilder(32768);uint size=(uint)path.Capacity;if(!QueryFullProcessImageName(process,0,path,ref size))throw new IOException("Cannot identify game executable");return path.ToString();}finally{CloseHandle(process);}}
    private static ProfileGameContext? Load(string? path)
    {try{if(path==null||!File.Exists(path))return null;var value=JsonSerializer.Deserialize<ProfileGameContext>(File.ReadAllText(path),Backend.Program.Json);return value?.Game.ProcessId>0&&value.Game.Name.Length<=160&&value.Game.Path.Length<=32768&&value.StartedUtcTicks>0?value:null;}catch(Exception){return null;}}
    private void Store(ProfileGameContext? value)
    {
        if(value==saved)return;saved=value;if(contextFile==null)return;
        try{if(value==null){if(File.Exists(contextFile))File.Delete(contextFile);}else{string temp=contextFile+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(value,Backend.Program.Json));File.Move(temp,contextFile,true);}}catch(Exception ex){try{log?.Invoke(ex);}catch(Exception){}}
    }
    private DetectedGame? Clear(){previous=null;Store(null);return null;}
    internal DetectedGame? Read()
    {
        // Profiles need RTSS hooks even while the HUD and frame limiter are off.
        // Start it in the background so the widget readiness handshake remains responsive.
        if(RtssOverlay.DirectoryPath!=null&&!RtssOverlay.IsRunning()&&(startup==null||startup.IsCompleted)&&Environment.TickCount64>=retry)
        {retry=Environment.TickCount64+10000;startup=Task.Run(()=>{try{RtssOverlay.EnsureRunning();}catch(Exception ex){try{log?.Invoke(ex);}catch(Exception){}}});}
        if(DesktopActions.IsGameBarForeground()||NativeGameBarState.IsInputRedirected())
        {
            var candidate=previous??saved?.Game;
            if(candidate!=null)try{using var p=Process.GetProcessById(candidate.ProcessId);if(!p.HasExited&&saved!=null&&p.StartTime.ToUniversalTime().Ticks==saved.StartedUtcTicks&&PathOf((uint)p.Id).Equals(candidate.Path,StringComparison.OrdinalIgnoreCase))return previous=candidate;}catch(Exception){}
            return Clear();
        }
        GetWindowThreadProcessId(DesktopActions.ForegroundHandle,out uint pid);if(pid==0)return Clear();
        var app=applications().FirstOrDefault(a=>a.ProcessId==pid&&(a.Flags&0xFFFF)!=0&&a.Fps>1);
        if(app==null)return Clear();
        try{using var process=Process.GetProcessById((int)pid);string path=PathOf(pid);if(!System.IO.Path.GetFileName(path).Equals(app.Name,StringComparison.OrdinalIgnoreCase))return Clear();var text=new StringBuilder(512);GetWindowText(DesktopActions.ForegroundHandle,text,text.Capacity);string title=text.ToString().Trim();if(title.Length<2)title=process.ProcessName;if(title.Length>160)title=title[..160];previous=new((int)pid,title,path);Store(new(previous,process.StartTime.ToUniversalTime().Ticks));return previous;}
        catch(Exception){return Clear();}
    }
}
