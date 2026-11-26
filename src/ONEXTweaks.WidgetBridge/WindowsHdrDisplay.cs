// SPDX-License-Identifier: AGPL-3.0-only
// CCD/WCS declarations: Microsoft win32metadata wingdi.h and Icm.h.
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ONEXTweaks.WidgetBridge;

internal sealed record HdrCalibrationProfile(string Id, string Name, DateTimeOffset Date, bool AdvancedColor);
internal sealed record HdrDisplayState(bool Available, bool Supported, bool Enabled, bool Active,
    bool? AutoHdr, int? SdrBrightness, double? SdrNits, HdrCalibrationProfile[] Profiles,
    string? CurrentProfile, string? NewestProfile, bool CalibrationAvailable, string? Error = null, bool CalibrationAppInstalled = false);
internal sealed record HdrPreferences(bool? Enabled = null, bool? AutoHdr = null,
    string? CalibrationProfile = null, int? SdrBrightness = null)
{
    internal void Validate()
    {
        if (SdrBrightness is <0 or >100 || CalibrationProfile != null &&
            (CalibrationProfile.Length > 260 || Path.GetFileName(CalibrationProfile) != CalibrationProfile))
            throw new InvalidDataException("Invalid HDR profile");
    }
}
internal sealed record HdrRecovery(bool Enabled, uint? WhiteLevel, string? Profile, int Scope, string? AutoHdrRaw);

internal sealed class WindowsHdrDisplay
{
    private const string DirectXKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string DirectXValue = "DirectXUserGlobalSettings";
    private const int HdrProfileSubtype = 8; // CPST_EXTENDED_DISPLAY_COLOR_MODE, not the SDR association.
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Luid
    {
        internal readonly uint Low;
        internal readonly int High;
        internal Luid(byte[] bytes, int offset) { Low = BitConverter.ToUInt32(bytes, offset); High = BitConverter.ToInt32(bytes, offset + 4); }
    }
    private sealed record Target(Luid SourceAdapter, uint Source, Luid Adapter, uint Id);
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, IntPtr pathArray, ref uint modes, IntPtr modeArray, IntPtr topology);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(IntPtr packet);
    [DllImport("user32.dll")] private static extern int DisplayConfigSetDeviceInfo(IntPtr packet);
    [DllImport("mscms.dll")] private static extern int ColorProfileGetDisplayUserScope(Luid adapter, uint source, out int scope);
    [DllImport("mscms.dll")] private static extern int ColorProfileGetDisplayList(int scope, Luid adapter, uint source, out IntPtr profiles, out uint count);
    [DllImport("mscms.dll")] private static extern int ColorProfileGetDisplayDefault(int scope, Luid adapter, uint source, int type, int subtype, out IntPtr name);
    [DllImport("mscms.dll", CharSet = CharSet.Unicode)] private static extern int ColorProfileSetDisplayDefaultAssociation(int scope, string name, int type, int subtype, Luid adapter, uint source);
    [DllImport("mscms.dll", CharSet = CharSet.Unicode)] private static extern int ColorProfileAddDisplayAssociation(int scope, string name, Luid adapter, uint source, [MarshalAs(UnmanagedType.Bool)] bool setAsDefault, [MarshalAs(UnmanagedType.Bool)] bool advancedColor);
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetColorDirectory(string? machine, StringBuilder directory, ref uint length);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackagesByPackageFamily(string family, ref uint count, IntPtr names, ref uint length, IntPtr buffer);
    internal static bool CalibrationInstalled()
    {
        uint count=0,length=0;int result=GetPackagesByPackageFamily("Microsoft.WindowsHDRCalibration_8wekyb3d8bbwe",ref count,IntPtr.Zero,ref length,IntPtr.Zero);
        return result is 0 or 122 && count>0;
    }

    private sealed class Packet : IDisposable
    {
        internal IntPtr Pointer { get; }
        internal Packet(uint type, int size, Luid adapter, uint id)
        {
            Pointer = Marshal.AllocHGlobal(size);
            Marshal.Copy(new byte[size], 0, Pointer, size);
            Marshal.WriteInt32(Pointer, unchecked((int)type)); Marshal.WriteInt32(Pointer, 4, size);
            Marshal.WriteInt32(Pointer, 8, unchecked((int)adapter.Low)); Marshal.WriteInt32(Pointer, 12, adapter.High);
            Marshal.WriteInt32(Pointer, 16, unchecked((int)id));
        }
        internal uint UInt(int offset) => unchecked((uint)Marshal.ReadInt32(Pointer, offset));
        internal void UInt(int offset, uint value) => Marshal.WriteInt32(Pointer, offset, unchecked((int)value));
        public void Dispose() => Marshal.FreeHGlobal(Pointer);
    }
    private static void Check(int result, string action)
    { if (result != 0) throw new IOException(action + ": 0x" + result.ToString("X8")); }
    internal static (uint Low,int High,uint Source) ActiveSourceForDisplaySettings()
    {var target=FindTarget("display settings");return(target.SourceAdapter.Low,target.SourceAdapter.High,target.Source);}
    internal static void RequireSingleInternalDisplayForRotation() => FindTarget("rotation", requireSingleInternal: true);
    private static Target FindTarget(string action="HDR", bool requireSingleInternal=false)
    {
        string device = WindowsDisplayModes.Device();
        for (int attempt = 0; attempt < 4; attempt++)
        {
            Check(GetDisplayConfigBufferSizes(2, out uint pathCount, out uint modeCount), "Windows display paths");
            if (pathCount is 0 or >64 || modeCount >256) throw new IOException("Invalid active display topology");
            IntPtr paths = Marshal.AllocHGlobal(checked((int)pathCount * 72)), modes = Marshal.AllocHGlobal(Math.Max(64, checked((int)modeCount * 64)));
            try
            {
                uint capacity = pathCount;
                int result = QueryDisplayConfig(2, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (result == 122) continue;
                Check(result, "Windows display topology");
                if (pathCount > capacity) throw new IOException("Display topology changed");
                if (requireSingleInternal && pathCount != 1) throw new IOException("Rotation needs a single built-in display");
                var targets = new List<Target>();
                for (int index = 0; index < pathCount; index++)
                {
                    var bytes = new byte[72]; Marshal.Copy(paths + index * 72, bytes, 0, bytes.Length);
                    var sourceAdapter = new Luid(bytes, 0); uint source = BitConverter.ToUInt32(bytes, 8);
                    using var name = new Packet(1, 84, sourceAdapter, source);
                    if (DisplayConfigGetDeviceInfo(name.Pointer) != 0) continue;
                    if (!string.Equals(Marshal.PtrToStringUni(name.Pointer + 20), device, StringComparison.OrdinalIgnoreCase)) continue;
                    if (requireSingleInternal && BitConverter.ToUInt32(bytes, 36) is not (6 or 11 or 13 or 0x80000000))
                        throw new IOException("Rotation needs a single built-in display");
                    targets.Add(new(sourceAdapter, source, new Luid(bytes, 20), BitConverter.ToUInt32(bytes, 28)));
                }
                if (targets.Count != 1) throw new IOException("Select one active ONEXPLAYER display before changing "+action);
                return targets[0];
            }
            finally { Marshal.FreeHGlobal(paths); Marshal.FreeHGlobal(modes); }
        }
        throw new IOException("The display topology keeps changing");
    }
    private static (bool Supported, bool Enabled, bool Active) ColorInfo(Target target)
    {
        using var modern = new Packet(15, 36, target.Adapter, target.Id);
        if (DisplayConfigGetDeviceInfo(modern.Pointer) == 0)
            return ((modern.UInt(20) & 16) != 0, (modern.UInt(20) & 32) != 0, modern.UInt(32) == 2);
        using var legacy = new Packet(9, 32, target.Adapter, target.Id);
        Check(DisplayConfigGetDeviceInfo(legacy.Pointer), "Windows HDR state");
        uint flags = legacy.UInt(20);
        return ((flags & 1) != 0, (flags & 2) != 0, (flags & 2) != 0);
    }
    private static uint? WhiteLevel(Target target)
    {
        using var packet = new Packet(11, 24, target.Adapter, target.Id);
        return DisplayConfigGetDeviceInfo(packet.Pointer) == 0 ? packet.UInt(20) : null;
    }
    private static int Scope(Target target)
    { Check(ColorProfileGetDisplayUserScope(target.Adapter, target.Source, out int scope), "Windows color profile scope"); return scope; }
    private static string? DefaultProfile(Target target, int scope)
    {
        IntPtr pointer = IntPtr.Zero;
        try
        {
            int result = ColorProfileGetDisplayDefault(scope, target.Adapter, target.Source, 0, HdrProfileSubtype, out pointer);
            if (result == unchecked((int)0x80070002) || result == unchecked((int)0x80070490)) return null;
            Check(result, "Windows HDR calibration profile");
            return pointer == IntPtr.Zero ? null : Path.GetFileName(Marshal.PtrToStringUni(pointer));
        }
        finally { if (pointer != IntPtr.Zero) LocalFree(pointer); }
    }
    private static string ColorDirectory()
    {
        uint length = 4096; var directory = new StringBuilder((int)length);
        if (!GetColorDirectory(null, directory, ref length)) throw new IOException("Windows color directory is unavailable");
        return directory.ToString();
    }
    private static bool HasMhc2(byte[] bytes)
    {
        if (bytes.Length <132) return false;
        uint count = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(128, 4));
        if (count >4096 || 132L + count * 12L >bytes.Length) return false;
        for (int index = 0; index <count; index++)
            if (Encoding.ASCII.GetString(bytes, 132 + index * 12, 4) == "MHC2") return true;
        return false;
    }
    private static HdrCalibrationProfile[] Profiles(Target target, int scope, string? current)
    {
        IntPtr pointer = IntPtr.Zero;
        try
        {
            Check(ColorProfileGetDisplayList(scope, target.Adapter, target.Source, out pointer, out uint count), "Windows calibration profiles");
            if (count >4096 || count >0 && pointer == IntPtr.Zero) throw new IOException("Invalid Windows calibration profile list");
            string directory = ColorDirectory(); var result = new List<HdrCalibrationProfile>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index <count; index++)
            {
                string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, index * IntPtr.Size));
                if (!string.IsNullOrWhiteSpace(name)) names.Add(Path.GetFileName(name));
            }
            // WCS GetDisplayList can omit Advanced Color associations (observed on
            // this Windows build: list empty while the default is a valid MHC2 ICC).
            // Include the actual default and installed MHC2 files in the OS color
            // directory. Never enumerate arbitrary user folders or ordinary SDR ICCs.
            if (current != null) names.Add(current);
            foreach (string path in Directory.EnumerateFiles(directory).Take(4096))
                if (Path.GetExtension(path).Equals(".icc", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".icm", StringComparison.OrdinalIgnoreCase)) names.Add(Path.GetFileName(path));
            foreach (string id in names)
            {
                string path = Path.Combine(directory, id);
                if (!File.Exists(path) || new FileInfo(path).Length >16 * 1024 * 1024) continue;
                bool advanced = HasMhc2(File.ReadAllBytes(path));
                // Ordinary SDR profiles are never offered as HDR calibration profiles.
                if (!advanced && !string.Equals(id, current, StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(new(id, Path.GetFileNameWithoutExtension(id), File.GetLastWriteTimeUtc(path), advanced));
            }
            return result.DistinctBy(value => value.Id, StringComparer.OrdinalIgnoreCase).OrderByDescending(value => value.Date).ThenBy(value => value.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        finally { if (pointer != IntPtr.Zero) LocalFree(pointer); }
    }
    internal static string? ReadAutoHdrRaw()
    { using var key = Registry.CurrentUser.OpenSubKey(DirectXKey); return key?.GetValue(DirectXValue) as string; }
    internal static bool? AutoHdr(string? raw)
    {
        string? value = raw?.Split(";", StringSplitOptions.RemoveEmptyEntries).Select(part => part.Split("=", 2)).LastOrDefault(parts => parts.Length == 2 && parts[0].Equals("AutoHDREnable", StringComparison.OrdinalIgnoreCase))?[1];
        return value == "1" ? true : value == "0" ? false : null;
    }
    internal HdrDisplayState Read()
    {
        try
        {
            var target = FindTarget(); var color = ColorInfo(target); uint? white = WhiteLevel(target);
            int scope = Scope(target); string? current = DefaultProfile(target, scope);
            var profiles = Profiles(target, scope, current);
            int? percent = white is >=1000 and <=6000 ? (int)Math.Round((white.Value - 1000) / 50d) : null;
            return new(true, color.Supported, color.Enabled, color.Active, AutoHdr(ReadAutoHdrRaw()), percent, white * .08,
                profiles, current, profiles.FirstOrDefault()?.Id, true, CalibrationAppInstalled: CalibrationInstalled());
        }
        catch (Exception error) { return new(false, false, false, false, AutoHdr(ReadAutoHdrRaw()), null, null, [], null, null, false, error.Message); }
    }
    internal HdrRecovery Snapshot()
    {
        var target = FindTarget(); int scope = Scope(target);
        return new(ColorInfo(target).Enabled, WhiteLevel(target), DefaultProfile(target, scope), scope, ReadAutoHdrRaw());
    }
    internal object ProfileDiagnostics()
    {
        var target=FindTarget();int scope=Scope(target);string? current=DefaultProfile(target,scope);IntPtr pointer=IntPtr.Zero;
        try
        {
            Check(ColorProfileGetDisplayList(scope,target.Adapter,target.Source,out pointer,out uint count),"Profile diagnostic");
            string directory=ColorDirectory();string path=Path.Combine(directory,Path.GetFileName(current??""));
            var entries=new List<object>();
            for(int index=0;index<Math.Min(count,128);index++)
            {
                string name=Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer,index*IntPtr.Size))??"";
                string file=Path.IsPathRooted(name)?name:Path.Combine(directory,Path.GetFileName(name));
                entries.Add(new{rooted=Path.IsPathRooted(name),exists=File.Exists(file),advancedColor=File.Exists(file)&&HasMhc2(File.ReadAllBytes(file)),isCurrent=string.Equals(Path.GetFileName(name),Path.GetFileName(current),StringComparison.OrdinalIgnoreCase)});
            }
            return new{scope,associated=count,currentRooted=Path.IsPathRooted(current??""),currentExistsInColorDirectory=File.Exists(path),currentAdvancedColor=File.Exists(path)&&HasMhc2(File.ReadAllBytes(path)),entries};
        }
        finally{if(pointer!=IntPtr.Zero)LocalFree(pointer);}
    }
    internal HdrPreferences Capture()
    {
        var value = Read();
        return value.Available ? new(value.Enabled, value.AutoHdr, value.CurrentProfile, value.SdrBrightness) : new();
    }
    internal void SetEnabled(bool enabled)
    {
        var target = FindTarget(); var before = ColorInfo(target);
        if (!before.Supported) throw new IOException("The active display does not support HDR");
        if (before.Enabled == enabled) return;
        void Write(bool value)
        {
            using var packet = new Packet(16, 24, target.Adapter, target.Id); packet.UInt(20, value ? 1u : 0);
            int result = DisplayConfigSetDeviceInfo(packet.Pointer);
            if (result is 50 or 87)
            { using var legacy = new Packet(10, 24, target.Adapter, target.Id); legacy.UInt(20, value ? 1u : 0); result = DisplayConfigSetDeviceInfo(legacy.Pointer); }
            Check(result, "Windows HDR change");
        }
        try
        {
            Write(enabled);
            for (int attempt = 0; attempt <40; attempt++)
            { if (ColorInfo(FindTarget()).Enabled == enabled) return; Thread.Sleep(50); }
            throw new IOException("Windows HDR readback differs from the requested state");
        }
        catch { Write(before.Enabled); throw; }
    }
    internal void SetAutoHdr(bool enabled)
    {
        string? before = ReadAutoHdrRaw();
        if (AutoHdr(before) == enabled) return;
        // Preserve every unrelated DirectX setting and its spelling/order.
        string[] parts = (before ?? "").Split(";", StringSplitOptions.RemoveEmptyEntries);
        string changed = string.Join(";", parts.Where(part => !part.Split("=", 2)[0].Equals("AutoHDREnable", StringComparison.OrdinalIgnoreCase))) + ";AutoHDREnable=" + (enabled ? "1;" : "0;");
        using var key = Registry.CurrentUser.CreateSubKey(DirectXKey);
        try
        {
            key.SetValue(DirectXValue, changed.TrimStart(";".ToCharArray()), RegistryValueKind.String);
            if (AutoHdr(ReadAutoHdrRaw()) != enabled) throw new IOException("Windows Auto HDR preference readback failed");
        }
        catch { if (before == null) key.DeleteValue(DirectXValue, false); else key.SetValue(DirectXValue, before, RegistryValueKind.String); throw; }
    }
    private static void SetWhiteLevel(Target target, uint raw)
    {
        // Windows has a public GET but no public SET for this slider. The guarded
        // Windows setter is also used by res2k/HDRTray (common/HDR.cpp).
        // Never report success without reading the actual public CCD value back.
        using var packet = new Packet(unchecked((uint)-18), 28, target.Adapter, target.Id);
        packet.UInt(20, raw); Marshal.WriteByte(packet.Pointer, 24, 1);
        Check(DisplayConfigSetDeviceInfo(packet.Pointer), "Windows SDR brightness change");
        if (WhiteLevel(target) != raw) throw new IOException("Windows SDR brightness readback differs from the requested value");
    }
    internal void SetSdrBrightness(int percent)
    {
        if (percent is <0 or >100) throw new ArgumentOutOfRangeException(nameof(percent));
        var target = FindTarget(); if (!ColorInfo(target).Enabled) throw new IOException("Enable HDR before adjusting SDR content brightness");
        uint before = WhiteLevel(target) ?? throw new IOException("Windows SDR brightness is unavailable");
        uint requested=(uint)(1000+50*percent);if(before==requested)return;
        try { SetWhiteLevel(target, requested); }
        catch { if (WhiteLevel(target) != before) SetWhiteLevel(target, before); throw; }
    }
    internal void SetCalibration(string id)
    {
        var target = FindTarget(); int scope = Scope(target); string? before = DefaultProfile(target, scope);
        if (string.Equals(before, id, StringComparison.OrdinalIgnoreCase)) return;
        if (!Profiles(target, scope, before).Any(profile => profile.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) throw new IOException("This HDR calibration profile is not associated with the active display");
        try
        {
            Check(ColorProfileAddDisplayAssociation(scope, id, target.Adapter, target.Source, false, true), "Windows HDR calibration association");
            Check(ColorProfileSetDisplayDefaultAssociation(scope, id, 0, HdrProfileSubtype, target.Adapter, target.Source), "Windows HDR calibration selection");
            if (!string.Equals(DefaultProfile(target, scope), id, StringComparison.OrdinalIgnoreCase)) throw new IOException("Windows calibration profile readback differs from the selection");
        }
        catch { if (before != null) Check(ColorProfileSetDisplayDefaultAssociation(scope, before, 0, HdrProfileSubtype, target.Adapter, target.Source), "Restore HDR calibration"); throw; }
    }
    internal void Restore(HdrRecovery before)
    {
        SetEnabled(before.Enabled);
        var target = FindTarget();
        if (before.WhiteLevel is uint white && WhiteLevel(target) != white) SetWhiteLevel(target, white);
        if (before.Profile != null && !string.Equals(DefaultProfile(target, before.Scope), before.Profile, StringComparison.OrdinalIgnoreCase))
            Check(ColorProfileSetDisplayDefaultAssociation(before.Scope, before.Profile, 0, HdrProfileSubtype, target.Adapter, target.Source), "Restore HDR profile");
        // Test/recovery only: restore the exact fresh raw string, including absent values.
        using var key = Registry.CurrentUser.CreateSubKey(DirectXKey);
        if (before.AutoHdrRaw == null) key.DeleteValue(DirectXValue, false); else key.SetValue(DirectXValue, before.AutoHdrRaw, RegistryValueKind.String);
    }
    internal void Apply(HdrPreferences? desired)
    {
        if (desired == null) return;
        desired.Validate(); var before = Snapshot();
        try
        {
            if (desired.Enabled is bool enabled) SetEnabled(enabled);
            if (desired.AutoHdr is bool auto) SetAutoHdr(auto);
            if (desired.CalibrationProfile is string profile) SetCalibration(profile);
            if (desired.SdrBrightness is int brightness && ColorInfo(FindTarget()).Enabled) SetSdrBrightness(brightness);
        }
        catch (Exception actionError)
        {
            try { Restore(before); }
            catch (Exception recoveryError)
            {
                throw new AggregateException("Windows HDR application and restoration failed; actual HDR state requires verification.", actionError, recoveryError);
            }
            throw;
        }
    }
    internal void Reset()
    {
        var state = Read(); if (!state.Available || !state.Supported) throw new IOException(state.Error ?? "HDR is unavailable");
        Apply(new(true, true, state.NewestProfile));
    }
}
