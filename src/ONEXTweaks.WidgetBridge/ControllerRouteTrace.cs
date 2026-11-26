// SPDX-License-Identifier: AGPL-3.0-only
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace ONEXTweaks.WidgetBridge;

/// <summary>Bounded, opt-in trace of D-pad routing without recording other buttons or keys.</summary>
internal sealed class ControllerRouteTrace : IDisposable
{
    private sealed record Sample(long Ms, string Stage, int? Value, string? Detail);
    private const int Limit = 240;
    private const string ArmName = "controller-route-trace.arm";
    private readonly List<Sample> samples = new(Limit);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromMinutes(5));
    private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
    private readonly int pid = Environment.ProcessId;
    private readonly string output;
    private (ushort Dpad, bool Redirected)? input;
    private ushort? submitted;
    private int physicalReaderStarted;
    private bool finished;
    private bool truncated;
    private int phase;
    private long phaseStartMs, nextIdentityMs;
    private bool? lastRedirected;
    private string? lastSlots;

    // Same DLL, caller and XInput slot namespace as ControllerNative.Read.
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct CapabilitiesEx
    {
        [FieldOffset(20)] internal ushort Vendor;
        [FieldOffset(22)] internal ushort Product;
        [FieldOffset(24)] internal ushort Revision;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "#108", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetCapabilitiesEx(uint reserved, uint slot, uint flags, out CapabilitiesEx caps);

    internal void Slots(int source, string occasion)
    {
        if (lifetime.IsCancellationRequested) return;
        try
        {
            var rows = new List<string>(4);
            for (uint slot = 0; slot < 4; slot++)
            {
                uint status = GetCapabilitiesEx(1, slot, 0, out var caps);
                if (status == 0)
                    rows.Add($"{slot}:{caps.Vendor:X4}:{caps.Product:X4}:{caps.Revision:X4}");
                else if (status != 1167)
                    rows.Add($"{slot}:error-{status}");
            }
            string identity = string.Join(",", rows);
            if (occasion != "periodic" || identity != lastSlots)
                Point("source-slot-map", source, $"{occasion}; slots={identity}");
            lastSlots = identity;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            if (lastSlots != "unavailable") Point("source-slot-map-unavailable", source, e.GetType().Name);
            lastSlots = "unavailable";
        }
    }

    private ControllerRouteTrace(string output)
    {
        this.output = output;
        _ = SaveAfterCapture(lifetime.Token);
    }

    internal static ControllerRouteTrace? TryStart(string reports)
    {
        try
        {
            var armed = new FileInfo(Path.Combine(reports, "..", "privateReports", ArmName));
            var age = DateTime.UtcNow - armed.LastWriteTimeUtc;
            if (!armed.Exists || age < TimeSpan.FromSeconds(-10) || age > TimeSpan.FromMinutes(12))
                return null;

            var name = "controller-route-" + Guid.NewGuid().ToString("N") + ".json";
            return new(Path.Combine(reports, name));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal void Point(string stage, int? value = null, string? detail = null)
    {
        if (lifetime.IsCancellationRequested) return;
        lock (samples)
        {
            if (finished) return;
            if (samples.Count == Limit)
            {
                truncated = true;
                return;
            }
            samples.Add(new(clock.ElapsedMilliseconds, stage, value, detail));
        }
    }

    internal void Input(ControllerFrame frame, uint packet, bool redirected, int source)
    {
        if (lifetime.IsCancellationRequested) return;
        long now = clock.ElapsedMilliseconds;
        if (lastRedirected != redirected)
        {
            lastRedirected = redirected;
            // Odd epochs are open. A read that spans an epoch change is ambiguous.
            int next = Volatile.Read(ref phase) + 1;
            if (((next & 1) != 0) != redirected) next++;
            Volatile.Write(ref phaseStartMs, now);
            Volatile.Write(ref phase, next);
            Slots(source, redirected ? "redirect-open" : "redirect-closed");
            nextIdentityMs = now + 1000;
        }
        else if (now >= nextIdentityMs)
        {
            Slots(source, "periodic");
            nextIdentityMs = now + 1000;
        }
        var value = ((ushort)(frame.Buttons & 15), redirected);
        if (input == value) return;
        input = value;
        Point("xinput", value.Item1, $"packet={packet}; redirected={redirected}");
    }

    internal void Submitted(ControllerFrame frame)
    {
        ushort dpad = (ushort)(frame.Buttons & 15);
        if (submitted == dpad) return;
        submitted = dpad;
        Point("submitted", dpad);
    }

    internal void StartPhysicalReader(ControllerSource source)
    {
        if (lifetime.IsCancellationRequested || Interlocked.Exchange(ref physicalReaderStarted, 1) != 0)
            return;
        string[] endpoints = source.InstanceIds.Where(id => id.StartsWith(
            @"HID\VID_045E&PID_028E&IG_", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (endpoints.Length != 1)
        {
            Point("physical-source-unavailable", endpoints.Length);
            return;
        }
        string path = @"\\?\" + endpoints[0].Replace('\u005c', '#') + "#{4d1e55b2-f16f-11cf-88cb-001111000030}";
        var token = lifetime.Token;
        _ = Task.Run(() => WatchPhysical(path, token), CancellationToken.None);
    }

    private async Task WatchPhysical(string path, CancellationToken token)
    {
        try
        {
            using SafeFileHandle handle = CreateFileW(path, 0x80000000, 3, 0, 3, 0x40000000, 0);
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!HidD_GetPreparsedData(handle, out nint preparsed))
                throw new IOException("Could not query physical HID capabilities");
            try
            {
                const int hidSuccess = 0x110000;
                if (HidP_GetCaps(preparsed, out var caps) != hidSuccess || caps.Page != 1 ||
                    caps.Usage != 5 || caps.InputLength != 15)
                    throw new InvalidDataException("Unexpected physical gamepad HID report format");

                using var stream = new FileStream(handle, FileAccess.Read, 1, true);
                var report = new byte[caps.InputLength];
                uint? previous = null;
                Point("physical-handle-open", detail: path);
                int observedPhase = -1;
                bool seenNeutral = false;
                uint pressed = 0;
                while (!token.IsCancellationRequested)
                {
                    int before = Volatile.Read(ref phase);
                    int count = await stream.ReadAsync(report.AsMemory(), token);
                    int after = Volatile.Read(ref phase);
                    if (count != report.Length) throw new EndOfStreamException("Incomplete physical HID report");
                    var status = HidP_GetUsageValue(0, 1, 0, 0x39, out uint hat, preparsed, report, (uint)count);
                    if (status != hidSuccess) throw new IOException($"Physical HID hat read failed ({status})");
                    if (observedPhase != after)
                    {
                        observedPhase = after;
                        seenNeutral = false;
                        pressed = 0;
                    }
                    if (before == after && (after & 1) != 0 &&
                        clock.ElapsedMilliseconds - Volatile.Read(ref phaseStartMs) >= 350)
                    {
                        if (hat == 0)
                        {
                            if (pressed != 0)
                                Point("open-hid-press-release", (int)pressed, $"epoch={after}");
                            seenNeutral = true;
                            pressed = 0;
                        }
                        else if (hat is >= 1 and <= 8 && seenNeutral)
                        {
                            pressed = hat;
                            seenNeutral = false;
                        }
                    }
                    if (previous == hat) continue;
                    previous = hat;
                    Point("physical-hat", checked((int)hat), $"epochs={before}/{after}");
                }
            }
            finally { HidD_FreePreparsedData(preparsed); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Point("physical-read-failed", (error as Win32Exception)?.NativeErrorCode, error.Message);
        }
    }

    private async Task SaveAfterCapture(CancellationToken token)
    {
        try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }

        Sample[] captured;
        bool wasTruncated;
        lock (samples)
        {
            finished = true;
            captured = samples.ToArray();
            wasTruncated = truncated;
        }

        try
        {
            string json = JsonSerializer.Serialize(new
            {
                started, pid, elapsedMs = clock.ElapsedMilliseconds, truncated = wasTruncated, samples = captured
            }, Backend.Program.Json);
            await File.WriteAllTextAsync(output + ".tmp", json);
            File.Move(output + ".tmp", output);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose() => lifetime.Cancel();

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct HidCaps
    {
        [FieldOffset(0)] internal ushort Usage;
        [FieldOffset(2)] internal ushort Page;
        [FieldOffset(4)] internal ushort InputLength;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security,
        uint disposition, uint flags, nint template);

    [DllImport("hid.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out nint preparsed);

    [DllImport("hid.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool HidD_FreePreparsedData(nint preparsed);

    [DllImport("hid.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int HidP_GetCaps(nint preparsed, out HidCaps caps);

    [DllImport("hid.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int HidP_GetUsageValue(int type, ushort page, ushort collection, ushort usage,
        out uint value, nint preparsed, byte[] report, uint length);
}
