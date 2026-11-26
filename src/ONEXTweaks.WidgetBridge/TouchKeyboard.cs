// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ONEXTweaks.WidgetBridge;

internal sealed record TouchKeyboardState(bool Visible, bool Docked, int Left, int Top, int Right, int Bottom,
    int ScreenLeft, int ScreenTop, int ScreenRight, int ScreenBottom);

internal static class TouchKeyboard
{
    private static readonly Guid PaneClass = new("D5120AA3-46BA-44C5-822D-CA8092C1FC72");
    private static readonly Guid TipClass = new("4CE576FA-83DC-4F88-951C-9D0782B4E376");
    private static readonly SemaphoreSlim visibilityGate = new(1, 1);

    // Windows' touch-keyboard COM interfaces are called on their own STA. No COM object crosses
    // apartments, and a fresh invocation object is created for each explicit user operation.
    internal static Task<TouchKeyboardState> ReadAsync() => OnSta(() => Read(GetForegroundWindow()));
    internal static Task<TouchKeyboardState> ShowAsync() => ChangeVisibilityAsync(requestedVisibility: true, requireDocked: true);
    internal static Task<TouchKeyboardState> ToggleAsync(Func<bool> current, CancellationToken cancellationToken) =>
        ChangeVisibilityAsync(requestedVisibility: null, requireDocked: false, current, cancellationToken);
    internal static Task<TouchKeyboardState> SetVisibleAsync(bool visible, Func<bool> current, CancellationToken cancellationToken) =>
        ChangeVisibilityAsync(visible, requireDocked: false, current, cancellationToken);

    private static async Task<TouchKeyboardState> ChangeVisibilityAsync(bool? requestedVisibility, bool requireDocked,
        Func<bool>? current = null, CancellationToken cancellationToken = default)
    {
        await visibilityGate.WaitAsync(cancellationToken);
        try { return await OnSta(() => ChangeVisibility(requestedVisibility, requireDocked, current, cancellationToken)); }
        finally { visibilityGate.Release(); }
    }

    private static TouchKeyboardState ChangeVisibility(bool? requestedVisibility, bool requireDocked, Func<bool>? current, CancellationToken cancellationToken)
    {
        void CheckCurrent()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current?.Invoke() == false) throw new OperationCanceledException();
        }

        CheckCurrent();
        nint window = GetForegroundWindow();
        TouchKeyboardState before = Read(window);
        bool show = requestedVisibility ?? !before.Visible;
        if (before.Visible == show) return requireDocked ? RequireDocked(before) : before;

        if (show && !Process.GetProcessesByName("TabTip").Any(InCurrentSession))
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                "microsoft shared", "ink", "TabTip.exe");
            if (!File.Exists(path)) throw new FileNotFoundException("Windows touch keyboard is unavailable", path);
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            Thread.Sleep(400);
            CheckCurrent();
            var started = Read(window);
            if (started.Visible) return requireDocked ? RequireDocked(started) : started;
        }

        CheckCurrent();
        object invocation = Activator.CreateInstance(Type.GetTypeFromCLSID(TipClass, throwOnError: true)!)!;
        try { ((ITipInvocation)invocation).Toggle(GetDesktopWindow()); }
        finally { Marshal.FinalReleaseComObject(invocation); }

        for (int i = 0; i < 60; i++)
        {
            Thread.Sleep(100);
            TouchKeyboardState state = Read(window);
            if (state.Visible == show) return requireDocked ? RequireDocked(state) : state;
        }
        throw new InvalidOperationException(show
            ? "Windows did not show its touch keyboard."
            : "Windows did not hide its touch keyboard.");
    }

    private static bool InCurrentSession(Process process)
    {
        using (process)
            try { return process.SessionId == Process.GetCurrentProcess().SessionId; }
            catch (InvalidOperationException) { return false; }
    }

    private static TouchKeyboardState RequireDocked(TouchKeyboardState state)
    {
        if (!state.Docked)
            throw new InvalidOperationException("Windows touch keyboard is floating. Select Dock in its layout menu; the next invocation will keep that layout.");
        return state;
    }

    private static TouchKeyboardState Read(nint window)
    {
        object pane = Activator.CreateInstance(Type.GetTypeFromCLSID(PaneClass, throwOnError: true)!)!;
        Rect rect;
        try { Marshal.ThrowExceptionForHR(((IFrameworkInputPane)pane).Location(out rect)); }
        finally { Marshal.FinalReleaseComObject(pane); }
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(window, 2), ref monitor))
            throw new InvalidOperationException("Cannot determine the target display for the touch keyboard");
        bool visible = rect.Right > rect.Left && rect.Bottom > rect.Top;
        bool docked = visible && Math.Abs(rect.Bottom - monitor.Monitor.Bottom) <= 12 &&
            Math.Abs(rect.Left - monitor.Monitor.Left) <= 12 && Math.Abs(rect.Right - monitor.Monitor.Right) <= 12 &&
            rect.Top > monitor.Monitor.Top;
        return new(visible, docked, rect.Left, rect.Top, rect.Right, rect.Bottom,
            monitor.Monitor.Left, monitor.Monitor.Top, monitor.Monitor.Right, monitor.Monitor.Bottom);
    }

    private static Task<TouchKeyboardState> OnSta(Func<TouchKeyboardState> operation)
    {
        var completion = new TaskCompletionSource<TouchKeyboardState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            nint previousDpi = SetThreadDpiAwarenessContext(-4);
            try { completion.SetResult(operation()); }
            catch (Exception error) { completion.SetException(error); }
            finally { if (previousDpi != 0) SetThreadDpiAwarenessContext(previousDpi); }
        }) { IsBackground = true, Name = "ONEXTweaks.TouchKeyboard" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [ComImport, Guid("37C994E7-432B-4834-A2F7-DCE1F13B834B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITipInvocation { void Toggle(nint window); }
    [ComImport, Guid("5752238B-24F0-495A-82F1-2FD593056796"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFrameworkInputPane
    {
        [PreserveSig] int Advise(nint window, nint handler, out uint cookie);
        [PreserveSig] int AdviseWithHWND(nint window, nint handler, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int Location(out Rect rect);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { internal int Size; internal Rect Monitor, Work; internal uint Flags; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
}
