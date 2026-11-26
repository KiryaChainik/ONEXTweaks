// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
using Windows.Devices.Sensors;

namespace ONEXTweaks.WidgetBridge;

internal sealed record RotationLockState(bool Available, bool? Locked, string Reason = "")
{
    public long ObservedAt { get; } = Environment.TickCount64;
}

internal sealed class WindowsDisplayRotation(object gate, Func<Func<bool>, Task>? applyWithPowerReadback,
    Action<Exception>? log) : IDisposable
{
    [Flags]
    private enum AutoRotation : uint
    {
        Disabled = 1, Suppressed = 2, Remote = 4, MultiMonitor = 8,
        NoSensor = 16, Unsupported = 32, Docked = 64, Laptop = 128,
    }

    private const int SettleMilliseconds = 400;
    private readonly Lazy<SimpleOrientationSensor?> sensor = new(SimpleOrientationSensor.GetDefault);
    private readonly WindowsDisplayModes display = new();
    private Timer? timer;
    private bool enabled, disposed, applying;
    private int pendingSensor = -1, generation;
    private int? reference;
    private long lastChange;
    private string? sensorFailure;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetAutoRotationState(out AutoRotation state);

    internal RotationLockState Read()
    {
        lock (gate)
        {
            if (enabled)
                return FallbackStatus(FallbackPauseReason() ?? sensorFailure);
            if (!GetAutoRotationState(out var platform))
                return Unavailable("Rotation state unavailable");
            if ((platform & (AutoRotation.Remote | AutoRotation.MultiMonitor | AutoRotation.Docked)) != 0)
                return Unavailable("Rotation needs a single built-in display");
            if (UsesWindows(platform))
            {
                bool locked = platform.HasFlag(AutoRotation.Disabled);
                return new(true, locked, !locked && platform.HasFlag(AutoRotation.Suppressed) ?
                    "The active app is holding the orientation" : "");
            }
            if (applyWithPowerReadback == null || GetSensor() == null)
                return Unavailable("Orientation sensor unavailable");
            if (!HasSingleInternalDisplay())
                return Unavailable("Rotation needs a single built-in display");
            return new(true, true);
        }
    }

    internal RotationLockState SetLocked(bool locked)
    {
        lock (gate)
        {
            if (locked && enabled)
            {
                StopSensor();
                return Read();
            }
            var before = Read();
            if (!before.Available || before.Locked == locked) return before;
            if (!GetAutoRotationState(out var platform))
                return Unavailable("Rotation state unavailable");
            if ((platform & (AutoRotation.Remote | AutoRotation.MultiMonitor | AutoRotation.Docked)) != 0)
                return Unavailable("Rotation needs a single built-in display");
            if (UsesWindows(platform))
            {
                if (platform.HasFlag(AutoRotation.Disabled) == locked) return Read();
                DesktopActions.ToggleWindowsRotationLock();
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    var actual = Read();
                    if (!actual.Available || actual.Locked == locked) return actual;
                    Thread.Sleep(50);
                }
                throw new IOException("Windows did not confirm Rotation Lock");
            }
            if (locked) return Read();
            var activeSensor = GetSensor() ?? throw new IOException("Orientation sensor unavailable");
            int position = (int)activeSensor.GetCurrentOrientation();
            if (!CanRotate(position))
                throw new InvalidOperationException("Hold the console upright, then unlock rotation");
            if (!reference.HasValue)
            {
                var current = display.ReadOrientationLayout();
                bool naturalLandscape = false;
                try
                {
                    var saved = display.ReadOrientationLayout(saved: true);
                    // The built-in panel and its chassis sensor share the normal landscape zero.
                    naturalLandscape = saved is { Orientation: 0, IsLandscape: true };
                }
                catch (IOException) { }
                reference = KeepOrCaptureReference(reference, naturalLandscape,
                    current.Orientation, current.IsLandscape, position);
                if (!reference.HasValue)
                    throw new InvalidOperationException("Return the screen to landscape in Windows, then unlock rotation");
            }
            sensorFailure = null;
            timer ??= new Timer(ApplyOrientation);
            activeSensor.OrientationChanged += SensorChanged;
            enabled = true;
            QueuePosition(position);
            return Read();
        }
    }

    private void SensorChanged(SimpleOrientationSensor sender, SimpleOrientationSensorOrientationChangedEventArgs args)
    {
        lock (gate)
        {
            if (!enabled || disposed) return;
            QueuePosition((int)args.Orientation);
        }
    }

    private void QueuePosition(int position)
    {
        pendingSensor = position;
        lastChange = Environment.TickCount64;
        timer!.Change(CanRotate(position) ? SettleMilliseconds : Timeout.Infinite, Timeout.Infinite);
    }

    private async void ApplyOrientation(object? unused)
    {
        int watchedGeneration, candidate;
        long candidateTime;
        lock (gate)
        {
            if (!enabled || disposed || applying || !CanRotate(pendingSensor)) return;
            var remaining = SettleMilliseconds - (Environment.TickCount64 - lastChange);
            if (remaining > 0)
            {
                timer!.Change((int)remaining, Timeout.Infinite);
                return;
            }
            applying = true;
            watchedGeneration = generation;
            candidate = pendingSensor;
            candidateTime = lastChange;
        }
        try
        {
            lock (gate)
            {
                if (!CanApplyCandidate(watchedGeneration, candidateTime, candidate)) return;
                if (display.ReadOrientation() == RelativeOrientation(reference!.Value, 0, candidate))
                {
                    sensorFailure = null;
                    return;
                }
            }
            await applyWithPowerReadback!(() =>
            {
                lock (gate)
                {
                    if (!CanApplyCandidate(watchedGeneration, candidateTime, candidate)) return false;
                    var orientation = RelativeOrientation(reference!.Value, 0, candidate);
                    display.SetOrientation(orientation, requireSingleInternal: true);
                    sensorFailure = null;
                    return true;
                }
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            lock (gate)
            {
                if (!disposed && enabled && generation == watchedGeneration)
                    sensorFailure = "Windows could not rotate the built-in display";
            }
            log?.Invoke(new IOException("Sensor-driven display rotation failed: " + error.Message, error));
        }
        finally
        {
            lock (gate)
            {
                applying = false;
                if (!disposed && enabled && CanRotate(pendingSensor) &&
                    (candidateTime != lastChange || candidate != pendingSensor || generation != watchedGeneration))
                    timer!.Change((int)Math.Max(1, SettleMilliseconds - (Environment.TickCount64 - lastChange)), Timeout.Infinite);
            }
        }
    }

    private bool CanApplyCandidate(int watchedGeneration, long candidateTime, int candidate) =>
        !disposed && enabled && generation == watchedGeneration && candidateTime == lastChange &&
        pendingSensor == candidate && FallbackPauseReason() == null &&
        (int)sensor.Value!.GetCurrentOrientation() == candidate;

    internal static RotationLockState FallbackStatus(string? reason) => new(true, false, reason ?? "");

    private string? FallbackPauseReason()
    {
        if (!GetAutoRotationState(out var platform) || UsesWindows(platform))
            return "Rotation state unavailable";
        if ((platform & (AutoRotation.Remote | AutoRotation.MultiMonitor | AutoRotation.Docked)) != 0 ||
            !HasSingleInternalDisplay())
            return "Rotation needs a single built-in display";
        return null;
    }

    internal static int? KeepOrCaptureReference(int? reference, bool naturalLandscape,
        int displayOrientation, bool isLandscape, int sensorPosition) =>
        reference ?? (naturalLandscape ? 0 : isLandscape ? (displayOrientation + sensorPosition) % 4 : (int?)null);

    private static bool CanRotate(int position) => position is >= 0 and <= 3;
    // The rendered image counter-rotates to stay upright as the chassis turns.
    internal static int RelativeOrientation(int displayAtUnlock, int sensorAtUnlock, int sensorNow) =>
        (displayAtUnlock + sensorAtUnlock - sensorNow + 4) % 4;
    private static bool UsesWindows(AutoRotation state) =>
        (state & (AutoRotation.NoSensor | AutoRotation.Unsupported | AutoRotation.Laptop)) == 0;

    private static bool HasSingleInternalDisplay()
    {
        try { WindowsHdrDisplay.RequireSingleInternalDisplayForRotation(); return true; }
        catch (IOException) { return false; }
    }

    private SimpleOrientationSensor? GetSensor()
    {
        try { return sensor.Value; }
        catch (Exception) { return null; }
    }

    private RotationLockState Unavailable(string reason)
    {
        return new(false, null, reason);
    }

    private void StopSensor()
    {
        if (!enabled) return;
        enabled = false;
        ++generation;
        pendingSensor = -1;
        timer?.Change(Timeout.Infinite, Timeout.Infinite);
        sensor.Value!.OrientationChanged -= SensorChanged;
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            StopSensor();
            timer?.Dispose();
        }
    }
}
