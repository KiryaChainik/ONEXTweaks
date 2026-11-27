using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Threading;

namespace ONEXTweaks.Power
{
    internal static class FanFirmwareControl
    {
        internal const ushort ApplicationRegister = 0x04EB;
        internal const int ApplicationMask = 0x40;
        private const ushort ModeRegister = 0x044A;
        private static readonly string Journal = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fan-control-journal");
        internal static readonly string Owner = CreateOwner();

        internal static bool Pending { get { return File.Exists(Journal); } }

        private static string CreateOwner()
        {
            using (var process = Process.GetCurrentProcess())
                return Identity(process.Id, process.StartTime.ToUniversalTime().Ticks);
        }

        internal static string Identity(int pid, long startTicks)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}/{1}", pid, startTicks);
        }

        internal static void Acquire()
        {
            WithLock(delegate
            {
                EnsureExclusive();
                using (var ec = Open())
                {
                    int flags = Read(ec, ApplicationRegister);
                    if (Pending)
                    {
                        var lease = ReadJournal();
                        if (lease[0] != Owner) throw new IOException("Previous fan control has not been released");
                        if ((flags & ApplicationMask) == 0) throw new IOException("Embedded controller released application fan control");
                        return;
                    }
                    if ((Read(ec, ModeRegister) & 1) != 0)
                        throw new IOException("Another application already controls the fan");
                    bool restoreBit = (flags & ApplicationMask) == 0;
                    string staged = Journal + ".new";
                    using (var file = new FileStream(staged, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] value = System.Text.Encoding.ASCII.GetBytes(Owner + (restoreBit ? "/clear" : "/keep"));
                        file.Write(value, 0, value.Length);
                        file.Flush(true);
                    }
                    File.Move(staged, Journal);
                    if (restoreBit) SetApplicationBit(ec, true);
                }
            });
        }

        // Only the active service may recover an orphan without an expected owner.
        internal static void Restore(string expectedOwner)
        {
            if (!Pending) return;
            WithLock(delegate
            {
                if (!Pending) return;
                var lease = ReadJournal();
                if (expectedOwner != null && lease[0] != expectedOwner) return;
                EnsureExclusive();
                using (var ec = Open())
                {
                    if ((Read(ec, ModeRegister) & 1) != 0)
                        Access(ec, "WriteECReg", ModeRegister, 0);
                    if ((Read(ec, ModeRegister) & 1) != 0)
                        throw new IOException("EC did not confirm firmware fan control");
                    if (lease[1] == "clear") SetApplicationBit(ec, false);
                    if ((Read(ec, ModeRegister) & 1) != 0)
                        throw new IOException("Firmware fan control changed during release");
                }
                File.Delete(Journal);
            });
        }

        private static string[] ReadJournal()
        {
            var value = File.ReadAllText(Journal).Split('/');
            int pid;
            long ticks;
            if (value.Length != 3 || !int.TryParse(value[0], NumberStyles.None, CultureInfo.InvariantCulture, out pid) ||
                !long.TryParse(value[1], NumberStyles.None, CultureInfo.InvariantCulture, out ticks) ||
                pid <= 0 || ticks <= 0 || (value[2] != "clear" && value[2] != "keep"))
                throw new IOException("Invalid fan control journal");
            return new[] { Identity(pid, ticks), value[2] };
        }

        private static void WithLock(Action action)
        {
            using (var mutex = new Mutex(false, @"Global\ONEXTweaks.FanFirmwareControl"))
            {
                bool held = false;
                try
                {
                    try { held = mutex.WaitOne(3000); }
                    catch (AbandonedMutexException) { held = true; }
                    if (!held) throw new IOException("Fan recovery is still running");
                    action();
                }
                finally { if (held) mutex.ReleaseMutex(); }
            }
        }

        private static void EnsureExclusive()
        {
            var apps = Process.GetProcessesByName("OneXConsole");
            try { if (apps.Length != 0) throw new IOException("Close OneXConsole before controlling the fan"); }
            finally { foreach (var app in apps) app.Dispose(); }
        }

        private static ManagementObject Open()
        {
            Hardware.ValidateOemHardware();
            using (var query = new ManagementObjectSearcher("SELECT EmbeddedControllerMajorVersion, EmbeddedControllerMinorVersion FROM Win32_BIOS"))
            using (var items = query.Get())
            {
                foreach (ManagementObject bios in items)
                    if (Convert.ToInt32(bios["EmbeddedControllerMajorVersion"]) != 0 ||
                        Convert.ToInt32(bios["EmbeddedControllerMinorVersion"]) != 11)
                        throw new IOException("EC firmware is not qualified for application fan control");
                if (items.Count != 1) throw new IOException("EC firmware could not be determined");
            }
            using (var query = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM SuRwECRegInterface"))
            using (var devices = query.Get())
            {
                foreach (ManagementObject device in devices)
                    if ((bool)device["Active"] && string.Equals((string)device["InstanceName"],
                        @"ACPI\PNP0C14\RWECREGWMI_0", StringComparison.OrdinalIgnoreCase))
                        return new ManagementObject(query.Scope, device.Path, null);
            }
            throw new IOException("The board fan EC interface is unavailable");
        }

        private static int Read(ManagementObject ec, ushort address)
        {
            return Access(ec, "ReadECReg", address, 0);
        }

        private static int Access(ManagementObject ec, string method, ushort address, int value)
        {
            uint packed = (uint)((address >> 8) | ((address & 0xff) << 8) | (value << 16));
            object[] arguments = { packed, null };
            ec.InvokeMethod(method, arguments);
            var response = (arguments[1] as string ?? "").Split(',');
            byte data;
            if (response.Length != 8 || !string.Equals(response[0], "0x00", StringComparison.OrdinalIgnoreCase) ||
                response[1].Length != 4 || !byte.TryParse(response[1].Substring(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out data))
                throw new IOException("Invalid fan EC response");
            return data;
        }

        private static void SetApplicationBit(ManagementObject ec, bool enabled)
        {
            int before = Read(ec, ApplicationRegister);
            int expected = enabled ? before | ApplicationMask : before & ~ApplicationMask;
            if (expected == before) return;
            if (Access(ec, "WriteReadECReg", ApplicationRegister, expected) != expected ||
                (Read(ec, ApplicationRegister) & ApplicationMask) != (expected & ApplicationMask))
                throw new IOException("EC did not confirm the application fan flag");
        }
    }
}
