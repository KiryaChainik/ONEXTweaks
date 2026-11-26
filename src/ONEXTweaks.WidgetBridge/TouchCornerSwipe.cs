// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
using System.Text;

namespace ONEXTweaks.WidgetBridge;

internal sealed class TouchCornerSwipe
{
    private bool touching, candidate, multiple;
    private uint contact;
    private int orientation;
    private double startX, startY, lastY;
    private long started, lastOpen = -1000;

    internal bool NeedsOrientation => !touching;
    internal bool NeedsReleaseOrientation => candidate;
    internal void Invalidate() => candidate = false;

    internal void Reset()
    {
        touching = false;
        candidate = false;
        multiple = false;
    }

    internal bool Observe(uint count, bool tip, uint id, double x, double y, long now, int? displayOrientation)
    {
        if (count > 1)
        {
            touching = multiple = true;
            candidate = false;
            return false;
        }
        if (count == 0 && tip) return false; // A hybrid digitizer continuation packet.
        if (!tip)
        {
            bool activate = touching && !multiple && candidate && displayOrientation == orientation &&
                (count == 0 || id == contact) &&
                now - started is >= 60 and <= 800 &&
                lastY - startY >= .095 && now - lastOpen >= 1000;
            Reset();
            if (activate) lastOpen = now;
            return activate;
        }
        if (multiple) return false;
        if (!touching)
        {
            touching = true;
            contact = id;
            started = now;
            if (displayOrientation is not int activeOrientation || activeOrientation is < 0 or > 3) return false;
            orientation = activeOrientation;
            (x, y) = ToScreen(x, y, orientation);
            startX = x;
            startY = lastY = y;
            candidate = x is >= 0 and <= .16 && y is >= 0 and <= .035;
            return false;
        }
        if (!candidate) return false;
        (x, y) = ToScreen(x, y, orientation);
        if (id != contact || Math.Abs(x - startX) > .055 || y < startY - .018 || y < lastY - .025 ||
            now - started > 800)
            candidate = false;
        lastY = Math.Max(lastY, y);
        return false;
    }

    // The digitizer reports fixed panel axes. Rotate them into the active desktop's axes.
    private static (double X, double Y) ToScreen(double x, double y, int orientation) => orientation switch
    {
        1 => (1 - y, x),
        2 => (1 - x, 1 - y),
        3 => (y, 1 - x),
        _ => (x, y),
    };
}

internal sealed class TouchCornerDigitizer(Action open) : IDisposable
{
    private const uint DeviceName = 0x20000007, PreparsedData = 0x20000005;
    private const int HidSuccess = 0x00110000;
    private const ushort Desktop = 1, Digitizer = 0x0d, X = 0x30, Y = 0x31,
        TipSwitch = 0x42, ContactId = 0x51, ContactCount = 0x54;
    private readonly Dictionary<nint, bool> devices = [];
    private readonly TouchCornerSwipe gesture = new();
    private readonly WindowsDisplayModes display = new();
    private readonly ushort[] usages = new ushort[32];
    private nint device, preparsed;
    private HidValue x, y;
    private int registered, reports, matches;

    internal object Read() => new { registered = Volatile.Read(ref registered) != 0, descriptorReady = Volatile.Read(ref preparsed) != 0,
        reports = Volatile.Read(ref reports), matches = Volatile.Read(ref matches) };

    internal void SetRegistered(bool value) => Volatile.Write(ref registered, value ? 1 : 0);

    internal bool CanRead(nint handle)
    {
        if (handle == 0) return false;
        if (devices.TryGetValue(handle, out bool accepted)) return accepted;
        string name = RawDeviceName(handle);
        bool internalPanel = name.Contains("COL01", StringComparison.OrdinalIgnoreCase) &&
            (name.Contains("GXTP6933", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("VEN_GXTP&DEV_6933", StringComparison.OrdinalIgnoreCase));
        if (internalPanel && preparsed == 0) accepted = OpenDescriptor(handle);
        return devices[handle] = accepted;
    }

    internal void Scan()
    {
        uint size = (uint)Marshal.SizeOf<RawDeviceEntry>();
        uint count = 0;
        if (GetRawInputDeviceList(null, ref count, size) != 0 || count == 0 || count > 512) return;
        var entries = new RawDeviceEntry[count];
        uint found = GetRawInputDeviceList(entries, ref count, size);
        if (found == uint.MaxValue || found > entries.Length) return;
        for (int i = 0; i < found; i++)
            if (entries[i].Type == 2 && CanRead(entries[i].Handle)) return;
    }

    internal void Remove(nint handle)
    {
        devices.Remove(handle);
        if (device == handle) ClearDescriptor();
    }

    internal void Reset() => gesture.Reset();
    internal void OnDisplayChanged() => gesture.Invalidate();

    private int? ReadOrientation()
    {
        try { return display.ReadOrientation(); }
        catch (IOException) { return null; }
    }

    internal void Observe(nint handle, nint body, uint bytes)
    {
        if (handle != device || preparsed == 0 || bytes < 8) return;
        int length = Marshal.ReadInt32(body), count = Marshal.ReadInt32(body + 4);
        if (length is < 2 or > 512 || count is < 1 or > 32 || (long)length * count > bytes - 8) return;
        long now = Environment.TickCount64;
        for (int i = 0; i < count; i++)
        {
            nint report = body + 8 + i * length;
            if (!Value(Digitizer, 0, ContactCount, report, length, out uint contacts)) continue;
            Interlocked.Increment(ref reports);
            if (contacts > 1)
            {
                gesture.Observe(contacts, false, 0, 0, 0, now, null);
                continue;
            }
            ushort link = x.LinkCollection;
            uint usageCount = (uint)usages.Length;
            if (HidP_GetUsages(0, Digitizer, link, usages, ref usageCount, preparsed, report, (uint)length) != HidSuccess ||
                usageCount > usages.Length || !Value(Digitizer, link, ContactId, report, length, out uint id))
            {
                gesture.Reset();
                continue;
            }
            bool tip = Array.IndexOf(usages, TipSwitch, 0, (int)usageCount) >= 0;
            if (!tip || contacts == 0)
            {
                int? releaseOrientation = !tip && gesture.NeedsReleaseOrientation ? ReadOrientation() : null;
                if (gesture.Observe(contacts, tip, id, 0, 0, now, releaseOrientation)) Activate();
                continue;
            }
            if (!Value(Desktop, link, X, report, length, out uint rx) ||
                !Value(Desktop, link, Y, report, length, out uint ry) ||
                rx < x.LogicalMin || rx > x.LogicalMax || ry < y.LogicalMin || ry > y.LogicalMax)
            {
                gesture.Reset();
                continue;
            }
            gesture.Observe(1, true, id, (rx - x.LogicalMin) / (double)(x.LogicalMax - x.LogicalMin),
                (ry - y.LogicalMin) / (double)(y.LogicalMax - y.LogicalMin), now,
                gesture.NeedsOrientation ? ReadOrientation() : null);
        }
    }

    private void Activate()
    {
        Interlocked.Increment(ref matches);
        open();
    }

    private bool OpenDescriptor(nint handle)
    {
        uint bytes = 0;
        if (GetRawInputDeviceInfo(handle, PreparsedData, 0, ref bytes) != 0 || bytes is < 8 or > 65536) return false;
        nint data = Marshal.AllocHGlobal((int)bytes);
        try
        {
            if (GetRawInputDeviceInfo(handle, PreparsedData, data, ref bytes) != bytes) return false;
            if (!TryValueCap(data, Desktop, 0, X, out x) || x.LinkCollection == 0 ||
                !TryValueCap(data, Desktop, x.LinkCollection, Y, out y) ||
                !TryValueCap(data, Digitizer, x.LinkCollection, ContactId, out _) ||
                !TryValueCap(data, Digitizer, 0, ContactCount, out _) ||
                x.LogicalMin < 0 || y.LogicalMin < 0 || x.LogicalMax <= x.LogicalMin || y.LogicalMax <= y.LogicalMin)
                return false;
            device = handle;
            Volatile.Write(ref preparsed, data);
            data = 0;
            return true;
        }
        finally { if (data != 0) Marshal.FreeHGlobal(data); }
    }

    private static bool TryValueCap(nint data, ushort page, ushort link, ushort usage, out HidValue result)
    {
        var values = new HidValue[32];
        ushort count = (ushort)values.Length;
        bool found = HidP_GetSpecificValueCaps(0, page, link, usage, values, ref count, data) == HidSuccess &&
            count > 0 && count <= values.Length;
        result = found ? values[0] : default;
        return found;
    }

    private bool Value(ushort page, ushort link, ushort usage, nint report, int length, out uint value) =>
        HidP_GetUsageValue(0, page, link, usage, out value, preparsed, report, (uint)length) == HidSuccess;

    private static string RawDeviceName(nint handle)
    {
        uint count = 0;
        GetRawInputDeviceName(handle, DeviceName, null, ref count);
        if (count is 0 or > 2048) return "";
        var path = new StringBuilder((int)count + 1);
        return GetRawInputDeviceName(handle, DeviceName, path, ref count) == uint.MaxValue ? "" : path.ToString();
    }

    private void ClearDescriptor()
    {
        nint data = Interlocked.Exchange(ref preparsed, 0);
        if (data != 0) Marshal.FreeHGlobal(data);
        device = 0;
        gesture.Reset();
    }

    public void Dispose()
    {
        ClearDescriptor();
        devices.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawDeviceEntry
    {
        internal nint Handle;
        internal uint Type;
    }

    // HIDP_VALUE_CAPS has identical layout for the tail union's Range and NotRange variants.
    [StructLayout(LayoutKind.Sequential)]
    private struct HidValue
    {
        internal ushort UsagePage;
        internal byte ReportId, IsAlias;
        internal ushort BitField, LinkCollection, LinkUsage, LinkUsagePage;
        internal byte IsRange, IsStringRange, IsDesignatorRange, IsAbsolute, HasNull, Reserved;
        internal ushort BitSize, ReportCount, Reserved0, Reserved1, Reserved2, Reserved3, Reserved4;
        internal uint UnitsExp, Units;
        internal int LogicalMin, LogicalMax, PhysicalMin, PhysicalMax;
        internal ushort MinimumUsage, MaximumUsage, MinimumString, MaximumString, MinimumDesignator, MaximumDesignator,
            MinimumDataIndex, MaximumDataIndex;
    }

    [DllImport("user32.dll")]
    private static extern uint GetRawInputDeviceList([Out] RawDeviceEntry[]? entries, ref uint count, uint size);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", ExactSpelling = true)]
    private static extern uint GetRawInputDeviceInfo(nint device, uint command, nint data, ref uint size);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetRawInputDeviceName(nint device, uint command, StringBuilder? data, ref uint size);
    [DllImport("hid.dll")]
    private static extern int HidP_GetSpecificValueCaps(int reportType, ushort page, ushort link, ushort usage,
        [Out] HidValue[] caps, ref ushort count, nint data);
    [DllImport("hid.dll")]
    private static extern int HidP_GetUsageValue(int reportType, ushort page, ushort link, ushort usage, out uint value,
        nint data, nint report, uint length);
    [DllImport("hid.dll")]
    private static extern int HidP_GetUsages(int reportType, ushort page, ushort link, [Out] ushort[] usages,
        ref uint count, nint data, nint report, uint length);
}
