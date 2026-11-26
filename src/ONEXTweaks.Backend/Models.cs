namespace ONEXTweaks.Backend;

public sealed record WindowsMetrics(
    double? CpuLoadPercent, ulong? MemoryUsedBytes, ulong? MemoryTotalBytes,
    int? BatteryPercent, bool? OnAcPower, uint? BatteryRemainingSeconds);
public sealed record PowerDomain(
    int Index, bool? DriverReportsCanControl, double? MinimumWatts, double? MaximumWatts,
    double? SustainedLimitWatts, double? BurstLimitWatts, double? MeasuredWatts,
    string Status, string? Detail);
public sealed record FrequencyDomain(int Index, uint Type, double? ActualMHz, string Status);
public sealed record TemperatureSensor(int Index, uint Type, double? Celsius, string Status);
public sealed record IntelAdapter(
    string Name, uint DeviceType, string PciDeviceId,
    IReadOnlyList<PowerDomain> PowerDomains,
    IReadOnlyList<FrequencyDomain> Frequencies,
    IReadOnlyList<TemperatureSensor> Temperatures,
    IReadOnlyList<string> Notices);
public sealed record IntelMetrics(string Status, string? Detail, uint? ApiVersion, IReadOnlyList<IntelAdapter> Adapters);
public sealed record TelemetrySnapshot(
    int SchemaVersion, long Sequence, DateTimeOffset TimestampUtc,
    WindowsMetrics Windows, IntelMetrics Intel, bool HardwareWritesImplemented = false);
