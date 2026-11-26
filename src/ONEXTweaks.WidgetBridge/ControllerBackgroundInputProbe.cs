using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace ONEXTweaks.WidgetBridge;

internal static class ControllerBackgroundInputProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        internal uint Packet;
        internal ushort Buttons;
        internal byte Lt, Rt;
        internal short Lx, Ly, Rx, Ry;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint ReadState(uint slot, out XInputState state);

    [DllImport("kernel32.dll", EntryPoint = "GetProcAddress", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetOrdinal(IntPtr library, IntPtr ordinal);

    private sealed record Reader(string Name, ReadState Read);
    private sealed record Axes(uint Packet, short Lx, short Ly, short Rx, short Ry, byte Lt, byte Rt);
    private sealed record SlotReading(int Slot, uint Result, Axes? Axes);
    private sealed record Reading(long Milliseconds, string Reader, SlotReading[] Slots);

    internal static int Run(string output)
    {
        var handles = new List<IntPtr>();
        try
        {
            string path = Path.GetFullPath(output);
            if (!OperatingSystem.IsWindows() || !Regex.IsMatch(path,
                @"\AD:\\ONEXDI[0-9A-Fa-f]{16}\\[A-Za-z0-9_-]+\.json\z", RegexOptions.IgnoreCase) ||
                Marshal.SizeOf<XInputState>() != 16)
                return 2;
            if ((File.GetAttributes(Path.GetDirectoryName(path)!) & FileAttributes.ReparsePoint) != 0)
                return 2;

            var readers = new List<Reader>();
            var unavailable = new Dictionary<string, string>();
            var versions = new Dictionary<string, string?>();
            foreach (string name in new[] { "xinput1_4.dll", "xinput9_1_0.dll", "xinput1_3.dll" })
            {
                string library = Path.Combine(Environment.SystemDirectory, name);
                if (!NativeLibrary.TryLoad(library, typeof(ControllerBackgroundInputProbe).Assembly,
                    DllImportSearchPath.System32, out IntPtr module))
                {
                    unavailable[name] = "System library not available";
                    continue;
                }
                handles.Add(module);
                versions[name] = FileVersionInfo.GetVersionInfo(library).FileVersion;
                void Add(string export, IntPtr address)
                {
                    string key = name + ":" + export;
                    if (address == IntPtr.Zero) unavailable[key] = "Export not present";
                    else readers.Add(new Reader(key, Marshal.GetDelegateForFunctionPointer<ReadState>(address)));
                }
                NativeLibrary.TryGetExport(module, "XInputGetState", out IntPtr readState);
                Add("XInputGetState", readState);
                if (name != "xinput9_1_0.dll")
                    Add("ordinal100", GetOrdinal(module, new IntPtr(100)));
            }

            var readings = new List<Reading>();
            var start = DateTimeOffset.UtcNow;
            var watch = Stopwatch.StartNew();
            for (int tick = 0; tick < 40 && watch.ElapsedMilliseconds < 4000; tick++)
            {
                foreach (Reader reader in readers)
                {
                    if (unavailable.ContainsKey(reader.Name)) continue;
                    try
                    {
                        var slots = new SlotReading[4];
                        for (int slot = 0; slot < slots.Length; slot++)
                        {
                            uint result = reader.Read((uint)slot, out XInputState s);
                            slots[slot] = new SlotReading(slot, result, result == 0
                                ? new Axes(s.Packet, s.Lx, s.Ly, s.Rx, s.Ry, s.Lt, s.Rt) : null);
                        }
                        readings.Add(new Reading(watch.ElapsedMilliseconds, reader.Name, slots));
                    }
                    catch (Exception error) { unavailable[reader.Name] = error.GetType().Name; }
                }
                long remaining = (tick + 1) * 100L - watch.ElapsedMilliseconds;
                if (remaining > 0) Thread.Sleep((int)remaining);
            }
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(file, new { start, end = DateTimeOffset.UtcNow, versions, unavailable, readings });
            return readings.Count > 0 ? 0 : 2;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name);
            return 2;
        }
        finally { foreach (IntPtr handle in handles) NativeLibrary.Free(handle); }
    }
}
