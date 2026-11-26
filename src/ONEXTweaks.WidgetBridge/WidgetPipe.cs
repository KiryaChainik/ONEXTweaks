// ONEXTweaks adaptation of the ClawTweaks widget protocol. AGPL-3.0.
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace ONEXTweaks.WidgetBridge;

internal static class WidgetPipe
{
    internal const string Name = "ONEXTweaks.Widget.v1";
    internal static NamedPipeServerStream Create(string name=Name)
    {
        // UWP has both the user's SID and a restricted AppContainer token. Give packaged clients
        // access, then require the connecting process to have our user SID and interactive session.
        string sid = WindowsIdentity.GetCurrent().User!.Value;
        string sddl = $"D:P(A;;GA;;;{sid})(A;;GRGW;;;AC)";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var descriptor, out _))
            throw new Win32Exception();
        try
        {
            var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            var handle = CreateNamedPipe(@"\\.\pipe\" + name, 3 | 0x40000000 | 0x80000,
                8, 1, 65536, 65536, 0, ref security);
            if (handle.IsInvalid) throw new Win32Exception();
            return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
        }
        finally { LocalFree(descriptor); }
    }

    internal static void VerifyClient(NamedPipeServerStream pipe)
    {
        try
        {
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint pid)) throw new Win32Exception();
            VerifyClientProcess(pid);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        { throw new IOException("Widget client disconnected during authentication", e); }
    }
    internal static uint VerifyApplication(NamedPipeServerStream pipe,string applicationId)
    {
        VerifyClient(pipe);
        if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle,out uint pid)) throw new Win32Exception();
        using var process=Process.GetProcessById((int)pid);
        if(!IsApplication(process, applicationId))
            throw new UnauthorizedAccessException("Unexpected packaged controller test client");
        return pid;
    }
    internal static bool IsApplication(Process process, string applicationId)
    {
        const uint queryLimitedInformation = 0x1000;
        using var handle = OpenProcess(queryLimitedInformation, false, process.Id);
        if (handle.IsInvalid) return false;
        uint length = 256;
        var id = new System.Text.StringBuilder((int)length);
        return GetApplicationUserModelId(handle, ref length, id) == 0 &&
            WidgetPackageIdentity.MatchesApplication(id.ToString(), applicationId);
    }
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)]bool inherit, int processId);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, ExactSpelling=true)]
    private static extern int GetApplicationUserModelId(SafeProcessHandle process,ref uint length,System.Text.StringBuilder id);

    internal static void VerifyClientProcess(uint pid)
    {
        try{VerifyLiveClientProcess(pid);}
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {throw new IOException("Widget client disconnected during authentication",e);}
    }
    private static void VerifyLiveClientProcess(uint pid)
    {
        using var process = Process.GetProcessById((int)pid);
        if (process.SessionId != Process.GetCurrentProcess().SessionId) throw new UnauthorizedAccessException("Wrong client session");
        if (!OpenProcessToken(process.Handle, 8, out var token)) throw new Win32Exception();
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            if (identity.User != WindowsIdentity.GetCurrent().User) throw new UnauthorizedAccessException("Wrong client user");
    }

    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { internal int Length; internal IntPtr Descriptor; internal int Inherit; }
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern SafePipeHandle CreateNamedPipe(string name, uint access, uint mode, uint instances, uint outputSize, uint inputSize, uint timeout, ref SecurityAttributes security);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("advapi32.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
}
