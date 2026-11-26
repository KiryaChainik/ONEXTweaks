// SPDX-License-Identifier: AGPL-3.0-only
using System.Management;
using System.Runtime.InteropServices;
namespace ONEXTweaks.WidgetBridge;
internal static class OverlayExtraSensors
{
    [StructLayout(LayoutKind.Sequential)] private struct ProcessorPower{public uint Number,MaxMhz,CurrentMhz,MhzLimit,MaxIdle,CurrentIdle;}
    [DllImport("powrprof.dll")] private static extern uint CallNtPowerInformation(int level,IntPtr input,uint inputBytes,[Out]ProcessorPower[] output,uint outputBytes);
    internal static IReadOnlyList<uint>? ReadClocks()
    {
        var values=new ProcessorPower[Environment.ProcessorCount];
        if(CallNtPowerInformation(11,IntPtr.Zero,0,values,(uint)(values.Length*Marshal.SizeOf<ProcessorPower>()))!=0)return null;
        return values.Select(v=>v.CurrentMhz).Where(v=>v>0&&v<10000).ToArray();
    }
    internal static double? ReadGpuMemory()
    {
        try
        {
            using var search=new ManagementObjectSearcher("SELECT DedicatedUsage,SharedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory");
            using var rows=search.Get();if(rows.Count==0)return null;double bytes=0;
            foreach(ManagementObject row in rows)bytes+=Convert.ToDouble(row["DedicatedUsage"])+Convert.ToDouble(row["SharedUsage"]);
            return bytes/1073741824.0;
        }
        catch(ManagementException){return null;}catch(COMException){return null;}
    }
}
