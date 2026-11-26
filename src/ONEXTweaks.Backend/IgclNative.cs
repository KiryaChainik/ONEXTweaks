// Interop declarations based on Intel IGCL's public API header.
// IGCL API declarations: Copyright (C) 2025 Intel Corporation.
// See third-party/Intel-IGCL-LICENSE.txt and https://github.com/intel/drivers.gpu.control-library.
using System.Runtime.InteropServices;

namespace ONEXTweaks.Backend;

internal static class IgclNative
{
    private const string Library = "ControlLib.dll";
    [StructLayout(LayoutKind.Sequential)] internal struct InitArgs
    {
        internal uint Size; internal byte Version; internal uint AppVersion;
        internal uint Flags; internal uint SupportedVersion; internal Guid ApplicationUid;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct FirmwareVersion
    { internal ulong Major, Minor, Build; }
    [StructLayout(LayoutKind.Sequential)] internal struct Bdf
    { internal byte Bus, Device, Function; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)] internal struct AdapterProperties
    {
        internal uint Size; internal byte Version; internal IntPtr DeviceId;
        internal uint DeviceIdSize, DeviceType, SupportedFunctions;
        internal ulong DriverVersion; internal FirmwareVersion Firmware;
        internal uint PciVendorId, PciDeviceId, RevisionId, EusPerSubSlice, SubSlicesPerSlice, Slices;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)] internal string Name;
        internal uint Flags, Frequency;
        internal ushort PciSubsystemId, PciSubsystemVendorId;
        internal Bdf Address; internal uint XeCores;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 108)] internal byte[] Reserved;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct PowerProperties
    {
        internal uint Size; internal byte Version;
        [MarshalAs(UnmanagedType.I1)] internal bool CanControl;
        internal int DefaultLimit, MinLimit, MaxLimit;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct SustainedLimit
    { [MarshalAs(UnmanagedType.I1)] internal bool Enabled; internal int Power, Interval; }
    [StructLayout(LayoutKind.Sequential)] internal struct BurstLimit
    { [MarshalAs(UnmanagedType.I1)] internal bool Enabled; internal int Power; }
    [StructLayout(LayoutKind.Sequential)] internal struct PeakLimit { internal int Ac, Dc; }
    [StructLayout(LayoutKind.Sequential)] internal struct PowerLimits
    {
        internal uint Size; internal byte Version;
        internal SustainedLimit Sustained; internal BurstLimit Burst; internal PeakLimit Peak;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct EnergyCounter
    { internal uint Size; internal byte Version; internal ulong Energy, Timestamp; }
    [StructLayout(LayoutKind.Sequential)] internal struct FrequencyProperties
    {
        internal uint Size; internal byte Version; internal uint Type;
        [MarshalAs(UnmanagedType.I1)] internal bool CanControl;
        internal double Minimum, Maximum;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct FrequencyState
    {
        internal uint Size; internal byte Version;
        internal double Voltage, Request, Tdp, Efficient, Actual; internal uint ThrottleReasons;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct TemperatureProperties
    { internal uint Size; internal byte Version; internal uint Type; internal double Maximum; }

    // Load only the driver-provided system library, never a DLL from the current directory.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(Library, ExactSpelling = true)] internal static extern uint ctlInit(ref InitArgs args, out IntPtr api);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(Library, ExactSpelling = true)] internal static extern uint ctlClose(IntPtr api);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(Library, ExactSpelling = true)] internal static extern uint ctlEnumerateDevices(IntPtr api, ref uint count, IntPtr devices);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(Library, ExactSpelling = true)] internal static extern uint ctlGetDeviceProperties(IntPtr adapter, ref AdapterProperties properties);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(Library, ExactSpelling = true)] internal static extern uint ctlEnumPowerDomains(IntPtr adapter, ref uint count, IntPtr domains);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(Library, ExactSpelling = true)] internal static extern uint ctlPowerGetProperties(IntPtr domain, ref PowerProperties properties);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(Library, ExactSpelling = true)] internal static extern uint ctlPowerGetLimits(IntPtr domain, ref PowerLimits limits);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(Library, ExactSpelling = true)] internal static extern uint ctlPowerGetEnergyCounter(IntPtr domain, ref EnergyCounter counter);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(Library, ExactSpelling = true)] internal static extern uint ctlEnumFrequencyDomains(IntPtr adapter, ref uint count, IntPtr domains);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(Library, ExactSpelling = true)] internal static extern uint ctlFrequencyGetProperties(IntPtr domain, ref FrequencyProperties properties);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(Library, ExactSpelling = true)] internal static extern uint ctlFrequencyGetState(IntPtr domain, ref FrequencyState state);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(Library, ExactSpelling = true)] internal static extern uint ctlEnumTemperatureSensors(IntPtr adapter, ref uint count, IntPtr domains);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(Library, ExactSpelling = true)] internal static extern uint ctlTemperatureGetProperties(IntPtr domain, ref TemperatureProperties properties);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(Library, ExactSpelling = true)] internal static extern uint ctlTemperatureGetState(IntPtr domain, out double temperature);
}
