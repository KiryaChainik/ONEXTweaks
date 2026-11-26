// SPDX-License-Identifier: AGPL-3.0-only
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace ONEXTweaks.WidgetBridge;
internal sealed record SystemPowerPlan(string Action,string? Executable,string[] Arguments);
internal static class SystemPowerActions
{
    internal static SystemPowerPlan Plan(string action)=>action switch
    {
        "poweroff"=>new(action,"shutdown.exe",["/s","/t","0"]),
        "reboot"=>new(action,"shutdown.exe",["/r","/t","0"]),
        "bios"=>new(action,"shutdown.exe",["/r","/fw","/t","0"]),
        "hibernate"=>new(action,"shutdown.exe",["/h"]),
        "sleep"=>new(action,null,[]),
        _=>throw new ArgumentException("Unknown system power action")
    };
    internal static async Task Execute(string action,bool inGameBar)
    {
        var plan=Plan(action);if(inGameBar)await DesktopActions.DismissOverlay();
        if(action=="sleep")
        {
            EnableShutdownPrivilege();if(!SetSuspendState(false,false,false))throw new Win32Exception(Marshal.GetLastWin32Error());return;
        }
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),plan.Executable!)){UseShellExecute=false,CreateNoWindow=true};
        foreach(string arg in plan.Arguments)start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("Could not launch system power action");await process.WaitForExitAsync();
        if(process.ExitCode!=0)throw new Win32Exception(process.ExitCode);
    }
    private static void EnableShutdownPrivilege()
    {
        if(!OpenProcessToken(Process.GetCurrentProcess().Handle,0x28,out var token))throw new Win32Exception(Marshal.GetLastWin32Error());
        try{if(!LookupPrivilegeValue(null,"SeShutdownPrivilege",out var luid))throw new Win32Exception(Marshal.GetLastWin32Error());var state=new Privileges{Count=1,Luid=luid,Attributes=2};if(!AdjustTokenPrivileges(token,false,ref state,0,0,0)||Marshal.GetLastWin32Error()!=0)throw new Win32Exception(Marshal.GetLastWin32Error());}
        finally{CloseHandle(token);}
    }
    [StructLayout(LayoutKind.Sequential)]private struct Luid{internal uint Low;internal int High;}
    [StructLayout(LayoutKind.Sequential)]private struct Privileges{internal uint Count;internal Luid Luid;internal uint Attributes;}
    [DllImport("powrprof.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.U1)]
    private static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)]bool hibernate,[MarshalAs(UnmanagedType.U1)]bool force,[MarshalAs(UnmanagedType.U1)]bool disableWake);
    [DllImport("advapi32.dll",SetLastError=true)]private static extern bool OpenProcessToken(nint process,uint access,out nint token);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool LookupPrivilegeValue(string? system,string name,out Luid luid);
    [DllImport("advapi32.dll",SetLastError=true)]private static extern bool AdjustTokenPrivileges(nint token,bool disable,ref Privileges state,uint length,nint previous,nint returned);
    [DllImport("kernel32.dll")]private static extern bool CloseHandle(nint handle);
}
