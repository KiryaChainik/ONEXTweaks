// SPDX-License-Identifier: AGPL-3.0-only
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ONEXTweaks.WidgetBridge;

internal static class NativeGameBarRecovery
{
    private const string GameBarFamily = "Microsoft.XboxGamingOverlay_8wekyb3d8bbwe";
    private const uint QueryLimitedInformation = 0x1000;
    private const uint Terminate = 0x0001;
    private const uint Synchronize = 0x00100000;

    internal static async Task<bool> StopUnresponsiveHost(Func<bool> stillNeeded, CancellationToken token)
    {
        using var current=Process.GetCurrentProcess();
        var candidates=Process.GetProcessesByName("GameBar");
        try
        {
            if(candidates.Length!=1||candidates[0].SessionId!=current.SessionId)return false;
            var candidate=candidates[0];
            using var handle=OpenProcess(QueryLimitedInformation|Terminate|Synchronize,false,candidate.Id);
            if(handle.IsInvalid)return false;
            var family=new StringBuilder(128);
            uint length=(uint)family.Capacity;
            if(GetPackageFamilyName(handle,ref length,family)!=0||family.ToString()!=GameBarFamily)return false;
            token.ThrowIfCancellationRequested();
            if(!stillNeeded()||!TerminateProcess(handle,0))return false;
            using var bounded=CancellationTokenSource.CreateLinkedTokenSource(token);
            bounded.CancelAfter(TimeSpan.FromSeconds(3));
            try{await candidate.WaitForExitAsync(bounded.Token);return true;}
            catch(OperationCanceledException) when(!token.IsCancellationRequested){return false;}
        }
        catch(InvalidOperationException){return false;}
        catch(Win32Exception){return false;}
        finally{foreach(var candidate in candidates)candidate.Dispose();}
    }

    [DllImport("kernel32.dll",SetLastError=true)]
    private static extern SafeProcessHandle OpenProcess(uint access,[MarshalAs(UnmanagedType.Bool)] bool inherit,int processId);

    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]
    private static extern int GetPackageFamilyName(SafeProcessHandle process,ref uint length,StringBuilder family);

    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeProcessHandle process,uint exitCode);
}
