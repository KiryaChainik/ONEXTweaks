using System.Runtime.InteropServices;
using static ONEXTweaks.Backend.IgclNative;

namespace ONEXTweaks.Backend;

internal sealed class IntelProvider : IDisposable
{
    private IntPtr api;
    private uint? version;
    private string? unavailable;
    private readonly Dictionary<IntPtr, EnergyCounter> lastEnergy = [];
    private delegate uint Enumerator(IntPtr owner, ref uint count, IntPtr handles);

    internal IntelProvider()
    {
        try
        {
            var init = new InitArgs { Size = (uint)Marshal.SizeOf<InitArgs>(), AppVersion = 1u << 16, Flags = 1 };
            uint result = ctlInit(ref init, out api);
            if (result != 0) { unavailable = "ctlInit " + Code(result); api = IntPtr.Zero; }
            else version = init.SupportedVersion;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { unavailable = ex.GetType().Name; }
    }

    internal IntelMetrics Read()
    {
        if (api == IntPtr.Zero) return new("unavailable", unavailable, version, []);
        var handles = Enumerate(api, ctlEnumerateDevices, out uint result);
        if (result != 0) return new("unavailable", "ctlEnumerateDevices " + Code(result), version, []);
        var adapters = new List<IntelAdapter>();
        foreach (IntPtr handle in handles) adapters.Add(ReadAdapter(handle));
        return new(adapters.Count > 0 ? "ok" : "empty", null, version, adapters);
    }

    private IntelAdapter ReadAdapter(IntPtr handle)
    {
        var notices = new List<string>();
        IntPtr luid = Marshal.AllocHGlobal(8);
        var properties = new AdapterProperties
        {
            Size = (uint)Marshal.SizeOf<AdapterProperties>(), DeviceId = luid, DeviceIdSize = 8,
            Name = "", Reserved = new byte[108]
        };
        uint propertyResult;
        try { propertyResult = ctlGetDeviceProperties(handle, ref properties); }
        finally { Marshal.FreeHGlobal(luid); }
        if (propertyResult != 0) notices.Add("ctlGetDeviceProperties " + Code(propertyResult));

        var powers = new List<PowerDomain>();
        IntPtr[] domains = Enumerate(handle, ctlEnumPowerDomains, out uint result);
        if (result != 0) notices.Add("ctlEnumPowerDomains " + Code(result));
        for (int index = 0; index < domains.Length; index++) powers.Add(ReadPower(domains[index], index));

        var frequencies = new List<FrequencyDomain>();
        domains = Enumerate(handle, ctlEnumFrequencyDomains, out result);
        if (result != 0) notices.Add("ctlEnumFrequencyDomains " + Code(result));
        for (int index = 0; index < domains.Length; index++)
        {
            var p = new FrequencyProperties { Size = (uint)Marshal.SizeOf<FrequencyProperties>() };
            var state = new FrequencyState { Size = (uint)Marshal.SizeOf<FrequencyState>() };
            uint pr = ctlFrequencyGetProperties(domains[index], ref p);
            uint sr = ctlFrequencyGetState(domains[index], ref state);
            frequencies.Add(new(index, p.Type, sr == 0 ? Known(state.Actual) : null,
                pr == 0 && sr == 0 ? "ok" : Code(pr != 0 ? pr : sr)));
        }

        var temperatures = new List<TemperatureSensor>();
        domains = Enumerate(handle, ctlEnumTemperatureSensors, out result);
        if (result != 0) notices.Add("ctlEnumTemperatureSensors " + Code(result));
        for (int index = 0; index < domains.Length; index++)
        {
            var p = new TemperatureProperties { Size = (uint)Marshal.SizeOf<TemperatureProperties>() };
            uint pr = ctlTemperatureGetProperties(domains[index], ref p);
            uint sr = ctlTemperatureGetState(domains[index], out double value);
            temperatures.Add(new(index, p.Type, sr == 0 && value is >= -50 and <= 200 && double.IsFinite(value) ? value : null,
                pr == 0 && sr == 0 ? "ok" : Code(pr != 0 ? pr : sr)));
        }
        return new(propertyResult == 0 ? properties.Name : "Intel adapter (name unavailable)",
            properties.DeviceType, $"{properties.PciVendorId:X4}:{properties.PciDeviceId:X4}", powers, frequencies, temperatures, notices);
    }

    private PowerDomain ReadPower(IntPtr handle, int index)
    {
        var p = new PowerProperties { Size = (uint)Marshal.SizeOf<PowerProperties>() };
        var limits = new PowerLimits { Size = (uint)Marshal.SizeOf<PowerLimits>() };
        var counter = new EnergyCounter { Size = (uint)Marshal.SizeOf<EnergyCounter>() };
        uint pr = ctlPowerGetProperties(handle, ref p);
        uint lr = ctlPowerGetLimits(handle, ref limits);
        uint er = ctlPowerGetEnergyCounter(handle, ref counter);
        double? watts = null;
        if (er == 0)
        {
            if (lastEnergy.TryGetValue(handle, out var previous)) watts = CounterWatts(previous.Energy, previous.Timestamp, counter.Energy, counter.Timestamp);
            lastEnergy[handle] = counter;
        }
        string? detail = null;
        if (pr != 0 || lr != 0 || er != 0) detail = $"properties={Code(pr)}; limits={Code(lr)}; energy={Code(er)}";
        return new(index, pr == 0 ? p.CanControl : null,
            pr == 0 ? Milliwatts(p.MinLimit) : null, pr == 0 ? Milliwatts(p.MaxLimit) : null,
            lr == 0 && limits.Sustained.Enabled ? Milliwatts(limits.Sustained.Power) : null,
            lr == 0 && limits.Burst.Enabled ? Milliwatts(limits.Burst.Power) : null,
            watts, pr == 0 && lr == 0 ? "ok" : "partial", detail);
    }

    internal static double? CounterWatts(ulong previousEnergy, ulong previousTime, ulong energy, ulong time)
    {
        if (time <= previousTime || energy < previousEnergy) return null;
        double watts = (double)(energy - previousEnergy) / (time - previousTime);
        return double.IsFinite(watts) && watts is >= 0 and <= 1000 ? watts : null;
    }
    private static double? Known(double value) => double.IsFinite(value) && value >= 0 ? value : null;
    private static double? Milliwatts(int value) => value >= 0 ? value / 1000.0 : null;
    private static string Code(uint result) => $"0x{result:X8}";
    private static IntPtr[] Enumerate(IntPtr owner, Enumerator enumerate, out uint result)
    {
        uint count = 0;
        result = enumerate(owner, ref count, IntPtr.Zero);
        if (result != 0 || count == 0) return [];
        if (count > 64) throw new InvalidDataException("Unexpected IGCL handle count");
        uint capacity = count;
        IntPtr buffer = Marshal.AllocHGlobal(checked((int)capacity * IntPtr.Size));
        try
        {
            result = enumerate(owner, ref count, buffer);
            if (result != 0) return [];
            if (count > capacity) throw new InvalidDataException("IGCL count changed beyond buffer capacity");
            var handles = new IntPtr[count];
            for (int i = 0; i < handles.Length; i++) handles[i] = Marshal.ReadIntPtr(buffer, i * IntPtr.Size);
            return handles;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    public void Dispose()
    {
        if (api != IntPtr.Zero) { ctlClose(api); api = IntPtr.Zero; }
    }
}
