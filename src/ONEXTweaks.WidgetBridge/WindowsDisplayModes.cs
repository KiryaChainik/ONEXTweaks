// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;

namespace ONEXTweaks.WidgetBridge;

internal sealed record DisplayModeValue(int Width, int Height, int Hz);
internal sealed record DisplayModesState(bool Available, DisplayModeValue? Current, DisplayModeValue[] Modes, string? Error = null);

internal sealed class WindowsDisplayModes
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        internal uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Description;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Key;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DeviceMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string DeviceName;
        internal ushort SpecVersion, DriverVersion, Size, DriverExtra;
        internal uint Fields;
        internal int PositionX, PositionY;
        internal uint Orientation, FixedOutput;
        internal short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string FormName;
        internal ushort LogPixels;
        internal uint BitsPerPixel, Width, Height, Flags, Frequency, IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice info, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsEx(string device, int mode, ref DeviceMode info, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string device, ref DeviceMode info, IntPtr window, uint flags, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
    private static DeviceMode NewMode() => new() { Size = (ushort)Marshal.SizeOf<DeviceMode>(), DeviceName = "", FormName = "" };
    internal static string Device()
    {
        var devices = new List<string>();
        for (uint index = 0; index < 64; index++)
        {
            var info = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>(), Name = "", Description = "", Id = "", Key = "" };
            if (!EnumDisplayDevices(null, index, ref info, 0)) break;
            if ((info.Flags & 1) != 0 && (info.Id.Contains("VEN_8086&DEV_B080", StringComparison.OrdinalIgnoreCase) || info.Description.Contains("B390", StringComparison.OrdinalIgnoreCase))) devices.Add(info.Name);
        }
        if (devices.Count != 1) throw new IOException(devices.Count == 0 ? "ONEXPLAYER display is not active" : "Select one active ONEXPLAYER display before changing its mode");
        return devices[0];
    }
    private static DeviceMode Current(string device, bool saved = false)
    {
        var value = NewMode();
        if (!EnumDisplaySettingsEx(device, saved ? -2 : -1, ref value, 0))
            throw new IOException(saved ? "Windows did not report the saved display mode" : "Windows did not report the current display mode");
        return value;
    }
    private static DisplayModeValue Value(DeviceMode mode) => new((int)mode.Width, (int)mode.Height, (int)mode.Frequency);
    private static DisplayModeValue[] Modes(string device)
    {
        var values = new HashSet<DisplayModeValue>();
        for (int index = 0; index < 8192; index++)
        {
            var mode = NewMode();
            if (!EnumDisplaySettingsEx(device, index, ref mode, 0)) break;
            if (mode.BitsPerPixel == 32 && DisplayModeOrientationPolicy.ValidSize((int)mode.Width,(int)mode.Height) && mode.Frequency is >=24 and <=360 && (mode.Flags & 2) == 0) values.Add(Value(mode));
        }
        return values.OrderByDescending(value => (long)value.Width * value.Height).ThenByDescending(value => value.Hz).ToArray();
    }
    internal int ReadOrientation()=>checked((int)Current(Device()).Orientation);
    internal static int ReadSingleDisplayRefresh()
    {
        const int monitorCount = 80;
        return GetSystemMetrics(monitorCount) == 1 ? checked((int)Current(Device()).Frequency) : 0;
    }
    internal (int Orientation, bool IsLandscape) ReadOrientationLayout(bool saved = false)
    {
        var mode = Current(Device(), saved);
        return (checked((int)mode.Orientation), mode.Width > mode.Height);
    }
    internal DisplayModesState Read()
    {
        try { string device = Device(); return new(true, Value(Current(device)), Modes(device)); }
        catch (Exception error) { return new(false, null, Array.Empty<DisplayModeValue>(), error.Message); }
    }
    private static DisplayModeValue Apply(string device,DeviceMode before,DeviceMode changed)
    {
        int test=ChangeDisplaySettingsEx(device,ref changed,IntPtr.Zero,2,IntPtr.Zero);
        if(test!=0)throw new IOException(test==1?"Windows requires a restart for this display change.":"Windows rejected the display mode test ("+test+").");
        try
        {
            int result=ChangeDisplaySettingsEx(device,ref changed,IntPtr.Zero,0,IntPtr.Zero);
            if(result!=0)throw new IOException(result==1?"Windows requires a restart; the requested display change is not active.":"Windows rejected the display mode ("+result+").");
            for(int attempt=0;attempt<40;attempt++){var actual=Current(device);if(Value(actual)==Value(changed)&&actual.Orientation==changed.Orientation)return Value(actual);Thread.Sleep(25);}
            throw new IOException("The actual display mode/orientation differs from the requested one.");
        }
        catch
        {
            int restored=ChangeDisplaySettingsEx(device,ref before,IntPtr.Zero,0,IntPtr.Zero);var actual=Current(device);
            if(restored!=0||Value(actual)!=Value(before)||actual.Orientation!=before.Orientation)throw new IOException("Display change failed and Windows could not restore its fresh previous mode.");throw;
        }
    }
    internal DisplayModeValue Set(DisplayModeValue desired)
    {
        string device=Device();var before=Current(device);desired=DisplayModeOrientationPolicy.PreserveOrientation(desired,Value(before));
        if(Value(before)==desired)return desired;
        if(!Modes(device).Contains(desired))throw new ArgumentException("This resolution and refresh rate are not reported by the display.");
        var changed=before;changed.Fields=0x00080000|0x00100000|0x00400000|0x80; // Keep the actual current orientation explicitly.
        changed.Width=(uint)desired.Width;changed.Height=(uint)desired.Height;changed.Frequency=(uint)desired.Hz;
        return Apply(device,before,changed);
    }
    internal DisplayModeValue SetOrientation(int orientation, bool requireSingleInternal = false)
    {
        if(orientation is <0 or >3)throw new ArgumentOutOfRangeException(nameof(orientation));
        if (requireSingleInternal) WindowsHdrDisplay.RequireSingleInternalDisplayForRotation();
        string device=Device();var before=Current(device);if(before.Orientation==(uint)orientation)return Value(before);
        var changed=before;changed.Fields=0x00080000|0x00100000|0x00400000|0x80;
        if((before.Orientation%2)!=(orientation%2))(changed.Width,changed.Height)=(before.Height,before.Width);
        changed.Orientation=(uint)orientation;return Apply(device,before,changed);
    }
}
