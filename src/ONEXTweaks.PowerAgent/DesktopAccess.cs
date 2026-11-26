using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace ONEXTweaks.Power
{
    internal sealed class DesktopAccess
    {
        private const int QueryLimitedInformation = 0x1000;
        private const int TokenQuery = 8;
        private const int DaclSecurityInformation = 4;
        internal const PipeAccessRights ClientRights = PipeAccessRights.ReadData | PipeAccessRights.WriteData |
            PipeAccessRights.ReadAttributes | PipeAccessRights.ReadPermissions | PipeAccessRights.Synchronize;
        private static readonly SecurityIdentifier SystemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        private readonly SecurityIdentifier owner;
        private readonly string bridgeImage;

        private DesktopAccess(SecurityIdentifier owner, string bridgeImage)
        {
            this.owner = owner;
            this.bridgeImage = bridgeImage;
        }

        internal static DesktopAccess FromScheduledAction(string[] args)
        {
            if (args.Length != 2 || !IsDesktopSid(args[1]))
                throw new ArgumentException("An enrolled desktop account SID is required");
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ONEXTweaks");
            string agent = Path.Combine(root, "PowerAgent", "ONEXTweaks.PowerAgent.exe");
            using (var self = Process.GetCurrentProcess())
            using (var identity = WindowsIdentity.GetCurrent())
                if (!identity.IsSystem || self.SessionId != 0 || !SamePath(self.MainModule.FileName, agent))
                    throw new UnauthorizedAccessException("The installed task must run as LocalSystem in session zero");
            var owner = new SecurityIdentifier(args[1]);
            GrantProcessQuery(owner);
            return new DesktopAccess(owner, Path.Combine(root, "WidgetBridge", "ONEXTweaks.WidgetBridge.exe"));
        }

        internal static bool IsDesktopSid(string value)
        {
            // Local/domain accounts and Microsoft Entra user SIDs each have four variable subauthorities.
            if (value == null || value.Length > 64) return false;
            var parts = value.Split('-');
            if (parts.Length != 8 || parts[0] != "S" || parts[1] != "1") return false;
            if (!(parts[2] == "5" && parts[3] == "21") && !(parts[2] == "12" && parts[3] == "1")) return false;
            for (int i = 4; i < parts.Length; i++)
            {
                uint component;
                if (!UInt32.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out component) ||
                    component.ToString(CultureInfo.InvariantCulture) != parts[i]) return false;
            }
            return true;
        }

        internal static PipeSecurity PipeAcl(DesktopAccess desktop)
        {
            SecurityIdentifier server;
            using (var identity = WindowsIdentity.GetCurrent()) server = identity.User;
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(server);
            security.AddAccessRule(new PipeAccessRule(server, PipeAccessRights.FullControl, AccessControlType.Allow));
            if (desktop == null)
                security.AddAccessRule(new PipeAccessRule(SystemSid, PipeAccessRights.FullControl, AccessControlType.Allow));
            else
            {
                var network = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
                security.AddAccessRule(new PipeAccessRule(network, PipeAccessRights.FullControl, AccessControlType.Deny));
                security.AddAccessRule(new PipeAccessRule(desktop.owner, ClientRights, AccessControlType.Allow));
            }
            return security;
        }

        private static void GrantProcessQuery(SecurityIdentifier account)
        {
            // The ordinary Bridge verifies our image with QueryFullProcessImageName. Preserve the other process ACEs.
            IntPtr process = GetCurrentProcess();
            uint length;
            GetKernelObjectSecurity(process, DaclSecurityInformation, null, 0, out length);
            if (length == 0 || length > 65536) throw new Win32Exception();
            var bytes = new byte[length];
            if (!GetKernelObjectSecurity(process, DaclSecurityInformation, bytes, length, out length))
                throw new Win32Exception();
            var descriptor = new CommonSecurityDescriptor(false, false, bytes, 0);
            descriptor.DiscretionaryAcl.AddAccess(AccessControlType.Allow, account, QueryLimitedInformation,
                InheritanceFlags.None, PropagationFlags.None);
            bytes = new byte[descriptor.BinaryLength];
            descriptor.GetBinaryForm(bytes, 0);
            if (!SetKernelObjectSecurity(process, DaclSecurityInformation, bytes)) throw new Win32Exception();
        }

        internal bool Accepts(NamedPipeServerStream pipe, out uint session)
        {
            session = 0;
            uint pid;
            if (!GetNamedPipeClientSessionId(pipe.SafePipeHandle, out session) || !IsActive(session) ||
                !GetNamedPipeClientProcessId(pipe.SafePipeHandle, out pid)) return false;
            // An alternate account can start a process inside the foreground user's session.
            // Check the desktop token and the connecting process's primary token independently.
            IntPtr desktopToken;
            if (!WTSQueryUserToken(session, out desktopToken)) return false;
            try { if (!TokenMatches(desktopToken, owner)) return false; }
            finally { CloseHandle(desktopToken); }
            IntPtr process = OpenProcess(QueryLimitedInformation, false, pid);
            if (process == IntPtr.Zero) return false;
            try
            {
                var image = new StringBuilder(32768);
                uint size = (uint)image.Capacity;
                return QueryFullProcessImageName(process, 0, image, ref size) && SamePath(image.ToString(), bridgeImage)
                    && ProcessTokenMatches(process, owner);
            }
            finally { CloseHandle(process); }
        }

        internal static bool IsTrustedHelper(Process helper)
        {
            using (var identity = WindowsIdentity.GetCurrent())
                return !identity.IsSystem || (helper.SessionId == 0 && ProcessTokenMatches(helper.Handle, SystemSid));
        }

        private static bool ProcessTokenMatches(IntPtr process, SecurityIdentifier sid)
        {
            IntPtr token;
            if (!OpenProcessToken(process, TokenQuery, out token)) return false;
            try { return TokenMatches(token, sid); }
            finally { CloseHandle(token); }
        }

        private static bool TokenMatches(IntPtr token, SecurityIdentifier sid)
        {
            using (var identity = new WindowsIdentity(token)) return sid.Equals(identity.User);
        }

        private static bool SamePath(string left, string right)
        {
            return String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsActive(uint session)
        {
            return SameConsole(session, WTSGetActiveConsoleSessionId());
        }

        internal static bool SameConsole(uint session, uint active)
        {
            return session != 0 && session != UInt32.MaxValue && session == active;
        }

        internal static string ReadWhileActive(StreamReader reader, uint session)
        {
            Task<string> pending = ReadBoundedAsync(reader);
            while (!pending.IsCompleted && Task.WaitAny(new Task[] { pending }, 1000) < 0)
            {
                if (IsActive(session)) continue;
                pending.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                return null; // The caller restores the hardware and closes the old session's pipe.
            }
            return pending.GetAwaiter().GetResult();
        }

        private static async Task<string> ReadBoundedAsync(StreamReader reader)
        {
            var line = new StringBuilder();
            var next = new char[1];
            for (int i = 0; i < 8192; i++)
            {
                if (await reader.ReadAsync(next, 0, 1).ConfigureAwait(false) == 0)
                    return line.Length == 0 ? null : line.ToString();
                if (next[0] == '\n') return line.ToString();
                if (next[0] != '\r') line.Append(next[0]);
            }
            throw new InvalidDataException("Request too long");
        }

        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetKernelObjectSecurity(IntPtr obj,
            int info, [Out] byte[] descriptor, uint capacity, out uint needed);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SetKernelObjectSecurity(IntPtr obj,
            int info, byte[] descriptor);
        [DllImport("kernel32.dll")] private static extern bool GetNamedPipeClientSessionId(SafePipeHandle pipe, out uint session);
        [DllImport("kernel32.dll")] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
        [DllImport("kernel32.dll")] private static extern uint WTSGetActiveConsoleSessionId();
        [DllImport("wtsapi32.dll")] private static extern bool WTSQueryUserToken(uint session, out IntPtr token);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(int rights, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(
            IntPtr process, uint flags, StringBuilder image, ref uint length);
        [DllImport("advapi32.dll")] private static extern bool OpenProcessToken(IntPtr process, int rights, out IntPtr token);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
