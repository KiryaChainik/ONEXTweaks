// SPDX-License-Identifier: AGPL-3.0-only
// The adapter contains no IGCL declarations or driver bindings. Hardware calls cross the
// authenticated ONEXTweaks.Power.v1 IPC boundary into the separately installed PowerAgent.
using System.Text.Json;
namespace ONEXTweaks.Backend;
internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
internal sealed record WindowsMetrics(double? CpuLoadPercent, ulong? MemoryUsedBytes, ulong? MemoryTotalBytes,
    int? BatteryPercent, bool? OnAcPower, uint? BatteryRemainingSeconds);
