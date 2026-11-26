using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Onex.RecoveryExecutor128;

namespace ONEXTweaks.WidgetBridge;

// Standalone early CLI. The registration and all readings use the same GameInput v4 context.
internal static unsafe class GameInputEnumerationProbe
{
    private const uint Gamepad = 0x00040000;
    private const string RuntimeSha = "156030ED7928791DF36AB9033061A151BE61251054C45CF3AE65CD460509B04E";

    private sealed class Devices
    {
        internal readonly nint[] Items = new nint[8];
        internal int Count;
        internal bool Overflow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct State { internal uint Buttons; internal float Lt, Rt, Lx, Ly, Rx, Ry; }

    private sealed record Identity(ushort Vid, ushort Pid, string? PathSha, bool Connected, bool GamepadCapable,
        bool Match, string Status);
    private sealed record Axes(float Lx, float Ly, float Rx, float Ry, float Lt, float Rt);
    private sealed record Sample(long Milliseconds, ulong ClockUs, ulong? ReadingUs, int HResult, string Status, Axes? Axes);

    internal static int Run(string output, string rootSha)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            !Regex.IsMatch(output, @"\AD:\\ONEXDI[0-9a-fA-F]{16}\\[A-Za-z0-9_-]+\.json\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(rootSha, @"\A[0-9A-Fa-f]{64}\z")) return 2;
        rootSha = rootSha.ToUpperInvariant();
        try
        {
            if ((File.GetAttributes(Path.GetDirectoryName(output)!) & FileAttributes.ReparsePoint) != 0) return 2;
            using var report = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var startUtc = DateTimeOffset.UtcNow;
            string stage = "runtime", error = "";
            var identities = new List<Identity>();
            var samples = new List<Sample>(40);
            var devices = new Devices();
            GCHandle owner = default;
            nint library = 0, input = 0;
            ulong token = 0;
            bool registered = false, unregistered = false;
            try
            {
                string file = Path.Combine(Environment.SystemDirectory, "GameInputRedist.dll");
                using var held = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (Convert.ToHexString(SHA256.HashData(held)) != RuntimeSha || sizeof(State) != 28)
                    throw new InvalidDataException("runtime-abi");
                library = NativeLibrary.Load(file, typeof(GameInputEnumerationProbe).Assembly, DllImportSearchPath.System32);
                stage = "initialize";
                Guid iid = new("F2DAAE4C-4D21-4250-8953-4F791E6279AB");
                var initialize = (delegate* unmanaged[Stdcall]<Guid*, nint*, int>)NativeLibrary.GetExport(library, "GameInputInitialize");
                int hr = initialize(&iid, &input);
                if (hr < 0 || input == 0) throw new InvalidDataException($"initialize-0x{hr:X8}");
                ((delegate* unmanaged[Stdcall]<nint, byte, void>)Slot(input, 13))(input, 1);

                stage = "enumerate";
                owner = GCHandle.Alloc(devices);
                hr = ((delegate* unmanaged[Stdcall]<nint, nint, uint, nint,
                    delegate* unmanaged[Stdcall]<ulong, nint, int, nint, void>, ulong*, int>)Slot(input, 8))(
                        input, 0, Gamepad, GCHandle.ToIntPtr(owner), &OnDevice, &token);
                if (hr < 0) throw new InvalidDataException($"register-0x{hr:X8}");
                registered = true;
                try { Thread.Sleep(2000); }
                finally
                {
                    unregistered = ((delegate* unmanaged[Stdcall]<nint, ulong, byte>)Slot(input, 9))(input, token) != 0;
                }
                if (!unregistered) throw new InvalidDataException("unregister-pending");

                stage = "identity";
                nint physical = 0;
                int matches = 0;
                for (int i = 0; i < devices.Count; i++)
                {
                    var identity = Identify(devices.Items[i], rootSha);
                    identities.Add(identity);
                    if (!identity.Match) continue;
                    physical = devices.Items[i];
                    matches++;
                }
                if (devices.Overflow || matches != 1)
                    throw new InvalidDataException(devices.Overflow ? "device-limit" : $"physical-matches-{matches}");
                stage = "sample";
                Stopwatch watch = Stopwatch.StartNew();
                for (int i = 0; i < 40 && watch.ElapsedMilliseconds < 4000; i++)
                {
                    samples.Add(Read(input, physical, watch.ElapsedMilliseconds));
                    long delay = (i + 1) * 100L - watch.ElapsedMilliseconds;
                    if (delay > 0) Thread.Sleep((int)delay);
                }
                stage = "recheck";
                if (!Identify(physical, rootSha).Match) throw new InvalidDataException("physical-lost");
                stage = "complete";
            }
            catch (Exception ex) { error = ex is InvalidDataException ? ex.Message : ex.GetType().Name; }
            finally
            {
                // If unregistration is pending, this dedicated process retains native code,
                // callback context and references until its early CLI entry point returns.
                if (!registered || unregistered)
                {
                    if (owner.IsAllocated) owner.Free();
                    for (int i = 0; i < devices.Count; i++) Release(devices.Items[i]);
                    Release(input);
                    if (library != 0) NativeLibrary.Free(library);
                }
            }
            bool success = stage == "complete" && samples.Any(s => s.Axes != null);
            JsonSerializer.Serialize(report, new { startUtc, endUtc = DateTimeOffset.UtcNow, stage, error, success,
                registered, unregistered, overflow = unregistered && devices.Overflow, rootSha, identities, samples });
            return success ? 0 : 2;
        }
        catch (IOException) { return 2; }
        catch (UnauthorizedAccessException) { return 2; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnDevice(ulong token, nint context, int deviceEvent, nint device)
    {
        if (deviceEvent != 0 || device == 0) return;
        var devices = (Devices)GCHandle.FromIntPtr(context).Target!;
        lock (devices)
        {
            for (int i = 0; i < devices.Count; i++)
                if (device == devices.Items[i]) return;
            if (devices.Count == devices.Items.Length) { devices.Overflow = true; return; }
            ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(device, 1))(device);
            devices.Items[devices.Count++] = device;
        }
    }

    private static Identity Identify(nint device, string rootSha)
    {
        ushort vid = ((delegate* unmanaged[Stdcall]<nint, ushort>)Slot(device, 3))(device);
        ushort pid = ((delegate* unmanaged[Stdcall]<nint, ushort>)Slot(device, 4))(device);
        bool gamepad = (((delegate* unmanaged[Stdcall]<nint, uint>)Slot(device, 12))(device) & Gamepad) != 0;
        bool connected = ((delegate* unmanaged[Stdcall]<nint, byte>)Slot(device, 43))(device) != 0;
        Span<byte> bytes = stackalloc byte[1024];
        bytes.Fill(0xff);
        uint count;
        fixed (byte* buffer = bytes)
            count = ((delegate* unmanaged[Stdcall]<nint, uint, byte*, uint>)Slot(device, 19))(device, (uint)bytes.Length, buffer);
        int end = bytes.IndexOf((byte)0);
        if (end < 1 || (count != end && count != end + 1))
            return new(vid, pid, null, connected, gamepad, false, "no-device-path");
        string path = Encoding.UTF8.GetString(bytes[..end]);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
        if (!connected || !gamepad || vid != 0x045e || pid != 0x028e)
            return new(vid, pid, hash, connected, gamepad, false, "different-device-or-capabilities");
        try
        {
            PhysicalGamepadPnp128.RequireInterface(path, rootSha);
            return new(vid, pid, hash, true, true, true, "physical-root-verified");
        }
        catch (InvalidOperationException ex) { return new(vid, pid, hash, connected, gamepad, false, ex.Message); }
    }

    private static Sample Read(nint input, nint device, long ms)
    {
        nint reading = 0;
        try
        {
            int hr = ((delegate* unmanaged[Stdcall]<nint, uint, nint, nint*, int>)Slot(input, 4))(input, Gamepad, device, &reading);
            ulong now = ((delegate* unmanaged[Stdcall]<nint, ulong>)Slot(input, 3))(input);
            if (hr < 0 || reading == 0) return new(ms, now, null, hr, "no-reading", null);
            ulong timestamp = ((delegate* unmanaged[Stdcall]<nint, ulong>)Slot(reading, 4))(reading);
            State state = default;
            if (((delegate* unmanaged[Stdcall]<nint, State*, byte>)Slot(reading, 18))(reading, &state) == 0)
                return new(ms, now, timestamp, hr, "no-gamepad-state", null);
            if (!float.IsFinite(state.Lx) || !float.IsFinite(state.Ly) || !float.IsFinite(state.Rx) ||
                !float.IsFinite(state.Ry) || !float.IsFinite(state.Lt) || !float.IsFinite(state.Rt))
                return new(ms, now, timestamp, hr, "invalid-axes", null);
            return new(ms, now, timestamp, hr, "ok", new(state.Lx, state.Ly, state.Rx, state.Ry, state.Lt, state.Rt));
        }
        finally { Release(reading); }
    }

    private static void* Slot(nint actor, int index) => (*(void***)actor)[index];
    private static void Release(nint actor)
    {
        if (actor != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(actor, 2))(actor);
    }
}
