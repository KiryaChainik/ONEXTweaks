// SPDX-License-Identifier: AGPL-3.0-only
// ONEXTweaks Windows counters. No Intel driver or upstream private helper is linked.
using System.Runtime.InteropServices;

namespace ONEXTweaks.Backend;

internal sealed class WindowsSampler
{
    private ulong lastIdle, lastKernel, lastUser;
    private bool hasTimes;
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        internal uint Length, MemoryLoad;
        internal ulong TotalPhys, AvailablePhys, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus
    {
        internal byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        internal uint BatteryLifeTime, BatteryFullLifeTime;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemPowerStatus(out PowerStatus status);

    internal WindowsMetrics Read()
    {
        double? cpu = null;
        if (GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
        {
            if (hasTimes) cpu = CpuPercent(lastIdle, lastKernel, lastUser, idle, kernel, user);
            (lastIdle, lastKernel, lastUser, hasTimes) = (idle, kernel, user, true);
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        bool memoryKnown = GlobalMemoryStatusEx(ref memory) && memory.AvailablePhys <= memory.TotalPhys;
        bool powerKnown = GetSystemPowerStatus(out var power);
        bool batteryPresent = powerKnown && power.BatteryFlag != 128;
        return new(cpu,
            memoryKnown ? memory.TotalPhys - memory.AvailablePhys : null,
            memoryKnown ? memory.TotalPhys : null,
            batteryPresent && power.BatteryLifePercent <= 100 ? power.BatteryLifePercent : null,
            powerKnown && power.AcLineStatus <= 1 ? power.AcLineStatus == 1 : null,
            batteryPresent && power.BatteryLifeTime != uint.MaxValue ? power.BatteryLifeTime : null);
    }

    internal static double? CpuPercent(ulong oldIdle, ulong oldKernel, ulong oldUser, ulong idle, ulong kernel, ulong user)
    {
        if (idle < oldIdle || kernel < oldKernel || user < oldUser) return null;
        double total = (double)(kernel - oldKernel) + (user - oldUser);
        double idleDelta = idle - oldIdle;
        if (total <= 0 || idleDelta > total) return null;
        return 100 * (total - idleDelta) / total;
    }
}

