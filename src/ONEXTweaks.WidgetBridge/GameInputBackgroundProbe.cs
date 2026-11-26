using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Onex.RecoveryExecutor128;

namespace ONEXTweaks.WidgetBridge;

// Dedicated early CLI process. The v4 interfaces here match Microsoft.GameInput 4.0.306 x64.
internal static unsafe class GameInputBackgroundProbe
{
    private const string RuntimeSha = "156030ED7928791DF36AB9033061A151BE61251054C45CF3AE65CD460509B04E";
    private const uint Gamepad = 0x00040000;

    [StructLayout(LayoutKind.Sequential)]
    private struct GamepadState
    {
        internal uint Buttons;
        internal float Lt, Rt, Lx, Ly, Rx, Ry;
    }

    private sealed record Axes(float Lx, float Ly, float Rx, float Ry, float Lt, float Rt);
    private sealed record Sample(long Milliseconds, ulong ClockUs, ulong? ReadingUs, int HResult,
        string Status, Axes? Axes);

    internal static int Run(string output, string interfacePath, string rootSha)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            !Regex.IsMatch(output, @"\AD:\\ONEXDI[0-9a-fA-F]{16}\\[A-Za-z0-9_-]+\.json\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return 2;

        try
        {
            if ((File.GetAttributes(Path.GetDirectoryName(output)!) & FileAttributes.ReparsePoint) != 0)
                return 2;
            using var report = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var rows = new List<Sample>(40);
            var startUtc = DateTimeOffset.UtcNow;
            string stage = "pnp", error = "", interfaceSha = "";
            bool bound = false;
            try
            {
                PhysicalGamepadPnp128.RequireInterface(interfacePath, rootSha);
                interfaceSha = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(interfacePath.ToUpperInvariant())));
                stage = "runtime";
                string library = Path.Combine(Environment.SystemDirectory, "GameInputRedist.dll");
                using var held = new FileStream(library, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (Convert.ToHexString(SHA256.HashData(held)) != RuntimeSha || sizeof(GamepadState) != 28)
                    throw new InvalidDataException("runtime-abi");
                nint module = NativeLibrary.Load(library, typeof(GameInputBackgroundProbe).Assembly, DllImportSearchPath.System32);
                nint input = 0, device = 0;
                try
                {
                    stage = "initialize";
                    Guid iid = new("F2DAAE4C-4D21-4250-8953-4F791E6279AB");
                    var initialize = (delegate* unmanaged[Stdcall]<Guid*, nint*, int>)NativeLibrary.GetExport(module, "GameInputInitialize");
                    int hr = initialize(&iid, &input);
                    if (hr < 0 || input == 0) throw Failure(stage, hr);
                    ((delegate* unmanaged[Stdcall]<nint, byte, void>)Slot(input, 13))(input, 1);

                    stage = "bind";
                    // A new client can initialize before device enumeration completes.
                    fixed (char* name = interfacePath)
                    {
                        for (int attempt = 0; attempt < 11; attempt++)
                        {
                            hr = ((delegate* unmanaged[Stdcall]<nint, char*, nint*, int>)Slot(input, 12))(input, name, &device);
                            if (hr >= 0 && device != 0) break;
                            Release(device);
                            device = 0;
                            if (attempt < 10) Thread.Sleep(100);
                        }
                    }
                    if (device == 0) throw Failure(stage, hr);
                    stage = "identity";
                    if (((delegate* unmanaged[Stdcall]<nint, ushort>)Slot(device, 3))(device) != 0x045E ||
                        ((delegate* unmanaged[Stdcall]<nint, ushort>)Slot(device, 4))(device) != 0x028E ||
                        (((delegate* unmanaged[Stdcall]<nint, uint>)Slot(device, 12))(device) & Gamepad) == 0 ||
                        !SamePath(device, interfacePath))
                        throw new InvalidDataException("device-identity");
                    bound = true;
                    stage = "sample";
                    Stopwatch watch = Stopwatch.StartNew();
                    for (int i = 0; i < 40 && watch.ElapsedMilliseconds < 4000; i++)
                    {
                        rows.Add(Read(input, device, watch.ElapsedMilliseconds));
                        long delay = (i + 1) * 100L - watch.ElapsedMilliseconds;
                        if (delay > 0) Thread.Sleep((int)delay);
                    }
                    stage = "recheck";
                    PhysicalGamepadPnp128.RequireInterface(interfacePath, rootSha);
                    if (!SamePath(device, interfacePath)) throw new InvalidDataException("device-identity");
                    stage = "complete";
                }
                finally
                {
                    Release(device);
                    Release(input);
                    NativeLibrary.Free(module);
                }
            }
            catch (Exception ex)
            {
                error = ex is InvalidDataException or InvalidOperationException ? ex.Message : ex.GetType().Name;
            }
            bool success = stage == "complete" && rows.Exists(row => row.Axes != null);
            JsonSerializer.Serialize(report, new { startUtc, endUtc = DateTimeOffset.UtcNow, stage, error, success,
                physicalDeviceBound = bound, interfaceSha, rootSha, expectedRuntimeSha = RuntimeSha, samples = rows });
            return success ? 0 : 2;
        }
        catch (IOException) { return 2; }
        catch (UnauthorizedAccessException) { return 2; }
    }

    private static Sample Read(nint input, nint device, long ms)
    {
        ulong clock = ((delegate* unmanaged[Stdcall]<nint, ulong>)Slot(input, 3))(input);
        if (((delegate* unmanaged[Stdcall]<nint, byte>)Slot(device, 43))(device) == 0)
            return new Sample(ms, clock, null, 0, "disconnected", null);
        nint reading = 0;
        try
        {
            int hr = ((delegate* unmanaged[Stdcall]<nint, uint, nint, nint*, int>)Slot(input, 4))(input, Gamepad, device, &reading);
            if (hr < 0 || reading == 0) return new Sample(ms, clock, null, hr, "no-reading", null);
            ulong timestamp = ((delegate* unmanaged[Stdcall]<nint, ulong>)Slot(reading, 4))(reading);
            clock = ((delegate* unmanaged[Stdcall]<nint, ulong>)Slot(input, 3))(input);
            GamepadState state = default;
            if (((delegate* unmanaged[Stdcall]<nint, GamepadState*, byte>)Slot(reading, 18))(reading, &state) == 0)
                return new Sample(ms, clock, timestamp, hr, "no-gamepad-state", null);
            return new Sample(ms, clock, timestamp, hr, "ok",
                new Axes(state.Lx, state.Ly, state.Rx, state.Ry, state.Lt, state.Rt));
        }
        finally { Release(reading); }
    }

    private static bool SamePath(nint device, string expected)
    {
        Span<byte> buffer = stackalloc byte[1024];
        buffer.Fill(0xff);
        uint count;
        fixed (byte* bytes = buffer)
            count = ((delegate* unmanaged[Stdcall]<nint, uint, byte*, uint>)Slot(device, 19))(device, (uint)buffer.Length, bytes);
        int end = buffer.IndexOf((byte)0);
        return end > 0 && (count == end || count == end + 1) &&
            string.Equals(Encoding.ASCII.GetString(buffer[..end]), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static InvalidDataException Failure(string stage, int hr) => new($"{stage}-0x{hr:X8}");
    private static void* Slot(nint actor, int index) => (*(void***)actor)[index];
    private static void Release(nint actor)
    {
        if (actor != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(actor, 2))(actor);
    }
}
