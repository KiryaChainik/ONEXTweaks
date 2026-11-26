// SPDX-License-Identifier: AGPL-3.0-only
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ONEXTweaks.WidgetBridge;

internal sealed record DesktopState(bool Available, bool CanClose, string? TargetToken, string? TargetApp);
internal sealed record DesktopResult(string Action, bool Ok, string? TargetApp, bool CloseRequested, string? Error, TouchKeyboardState? Keyboard = null,long? TargetWindow = null,bool? ForegroundVerified = null);

internal sealed partial class DesktopActions : IDisposable
{
    private sealed record Target(nint Window, int Pid, uint Thread, long Started, string App, string Token);
    private readonly CancellationTokenSource stopping = new();
    private readonly SemaphoreSlim actions = new(1, 1);
    private readonly Task tracker;
    private readonly object targetLock = new();
    private Target? target;
    internal Func<ProfileSession?> RetainedApplication { private get; set; } = () => null;
    private static readonly HashSet<string> excluded = new(StringComparer.OrdinalIgnoreCase)
    { "GameBar", "GameBarFTServer", "XboxGamingBar", "ONEXTweaks.WidgetBridge", "ONEXTweaks.PowerAgent", "ONEXTweaks.Panel",
      "osk", "TabTip", "TextInputHost", "StartMenuExperienceHost", "ShellExperienceHost", "SearchHost", "LockApp", "LogonUI", "dwm", "csrss", "winlogon" };

    internal DesktopActions(bool trackForeground=true)
    {
        if(!trackForeground){tracker=Task.CompletedTask;return;}
        CaptureForeground();
        tracker = Task.Run(async () =>
        {
            try { while (!stopping.IsCancellationRequested) { CaptureForeground(); await Task.Delay(100, stopping.Token); } }
            catch (OperationCanceledException) { }
        });
    }
    internal DesktopState Read()
    {
        CaptureForeground();
        lock (targetLock)
        {
            if (target is { } t && !Valid(t)) target = null;
            return new(true, target is not null, target?.Token, target?.App);
        }
    }
    private void CaptureForeground()
        =>ObserveForeground(GetAncestor(GetForegroundWindow(),2),NativeGameBarState.IsInputRedirected());
    private void ObserveForeground(nint window,bool redirected)
    {
        try
        {
            if(!redirected&&Eligible(window,out int pid,out uint thread,out string app,out long started))
            {
                Remember(window,pid,thread,started,app);
                return;
            }
            if((!redirected&&!IsOverlayWindow(window))||RetainedApplication() is not {} retained)return;
            lock(targetLock)if(target is {} current&&current.Pid==retained.ProcessId&&current.Started==retained.StartedUtcTicks&&Valid(current))return;
            nint replacement=FindRetainedWindow(retained);
            if(Eligible(replacement,out pid,out thread,out app,out started)&&pid==retained.ProcessId&&started==retained.StartedUtcTicks)
                Remember(replacement,pid,thread,started,app);
            else lock(targetLock)
            {
                if(target is not {} existing||existing.Pid!=retained.ProcessId||existing.Started!=retained.StartedUtcTicks)target=null;
            }
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException) { }
    }
    private void Remember(nint window,int pid,uint thread,long started,string app)
    {
        lock(targetLock)
        {
            if(target is {} current&&current.Window==window&&current.Pid==pid&&current.Thread==thread&&current.Started==started)return;
            target=new(window,pid,thread,started,app,Guid.NewGuid().ToString("N"));
        }
    }
    private static nint FindRetainedWindow(ProfileSession session)
    {
        var windows=new List<nint>();
        EnumWindows((window,unused)=>{
            GetWindowThreadProcessId(window,out uint pid);
            if(pid!=(uint)session.ProcessId||GetWindow(window,4)!=0)return true;
            try
            {
                if(Eligible(window,out int actual,out _,out _,out long started)&&actual==session.ProcessId&&started==session.StartedUtcTicks)
                    windows.Add(window);
            }
            catch(Exception error) when(error is Win32Exception or InvalidOperationException or ArgumentException) { }
            return true;
        },0);
        return windows.Count==1?windows[0]:0;
    }
    private static bool Eligible(nint window, out int pid, out uint thread, out string app, out long started)
    {
        pid = 0; thread=0; app = ""; started = 0;
        if (window == 0 || !IsWindow(window) || (!IsWindowVisible(window)&&!IsIconic(window)) || window == GetShellWindow() || window == GetDesktopWindow()) return false;
        var className = new StringBuilder(256); GetClassName(window, className, className.Capacity);
        string cls = className.ToString();
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        thread=GetWindowThreadProcessId(window, out uint id);
        if(thread==0||id==0)return false;
        using var process = Process.GetProcessById((int)id);
        using var bridge = Process.GetCurrentProcess();
        if (process.SessionId != bridge.SessionId || excluded.Contains(process.ProcessName) ||
            process.ProcessName.Equals("ApplicationFrameHost",StringComparison.OrdinalIgnoreCase)&&IsOverlayWindow(window)) return false;
        if (process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) && cls is not ("CabinetWClass" or "ExploreWClass")) return false;
        pid = process.Id; app = process.ProcessName; started = process.StartTime.ToUniversalTime().Ticks;
        return true;
    }
    private static bool Valid(Target t)
    {
        try { return Eligible(t.Window, out int pid, out uint thread, out _, out long started) && pid == t.Pid && thread==t.Thread && started == t.Started; }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException) { return false; }
    }
    private static bool IsOverlayWindow(nint root)
    {
        if (root == 0 || !IsWindow(root)) return false;
        var pending = new Queue<nint>();
        pending.Enqueue(root);
        int visited = 0;
        while (pending.Count > 0 && visited++ < 256)
        {
            nint current = pending.Dequeue();
            GetWindowThreadProcessId(current, out uint id);
            try
            {
                using var process = Process.GetProcessById((int)id);
                if (process.ProcessName is "GameBar" or "XboxGamingBar") return true;
            }
            catch (ArgumentException) { }
            for (nint child = GetWindow(current, 5); child != 0; child = GetWindow(child, 2)) pending.Enqueue(child);
        }
        return false;
    }
    internal static bool IsGameBarForeground() => IsOverlayWindow(GetForegroundWindow());
    internal static nint ForegroundHandle => GetForegroundWindow();
    internal async Task<DesktopResult> Execute(string action, string? targetToken, bool inGameBar,string? shortcut=null,string? launchTarget=null)
    {
        await actions.WaitAsync();
        try
        {
            if(HomeActions.Contains(action))return await ExecuteHomeAction(action,targetToken,shortcut,launchTarget,inGameBar);
            Target? selected = null;
            if (action == "closeWindow")
            {
                lock (targetLock) selected = target;
                if (selected is null || selected.Token != targetToken || !Valid(selected))
                    throw new InvalidOperationException("The previous active window changed or is no longer available. Reopen the overlay and try again.");
            }
            else if (action is not ("taskManager" or "explorer" or "keyboard")) throw new InvalidOperationException("Unknown desktop action");

            if (inGameBar) await DismissOverlay();
            if(action=="keyboard")
            {
                var keyboard = await TouchKeyboard.ShowAsync();
                return new(action,true,"Windows touch keyboard",false,null,keyboard);
            }
            if (action == "closeWindow")
            {
                // Validate again after focus changes. Never send Alt+F4 blindly to whatever happens
                // to be foreground now, and never terminate a process to bypass its save dialog.
                if (!Valid(selected!)) throw new InvalidOperationException("The selected window has already closed.");
                if (!PostMessage(selected!.Window, 0x10, 0, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                lock (targetLock) { if (target?.Token == selected.Token) target = null; }
                return new(action, true, selected.App, true, null);
            }
            nint focused=await LaunchForegroundApp(action);
            return new(action,true,action=="taskManager"?"Taskmgr":"explorer",false,null,null,focused.ToInt64(),true);
        }
        catch (Exception e) { return new(action, false, null, false, e.Message); }
        finally { actions.Release(); }
    }
    internal void Validate(string action, string? token)
    {
        if (!HomeActions.Contains(action)&&action is not ("taskManager" or "explorer" or "keyboard" or "closeWindow")) throw new InvalidOperationException("Unknown desktop action");
        if(action is "fse" or "fseEnter" or "fseExit")GamingExperience.Validate(action=="fse"?null:action=="fseEnter");
        if (action == "closeWindow")
        {
            lock (targetLock)
                if (target is null || target.Token != token || !Valid(target)) throw new InvalidOperationException("The previous active window is no longer available.");
        }
    }
    internal static async Task DismissOverlay()
    {
        // Compact Game Bar can redirect input without becoming GetForegroundWindow().
        if (!NativeGameBarState.IsInputRedirected()) return;
        // Don't combine a synthetic Win+G with a modifier the user is currently holding.
        foreach (int key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
            if ((GetAsyncKeyState(key) & 0x8000) != 0) throw new InvalidOperationException("Release the keyboard modifier keys and try again.");
        var input = new[] { Key(0x5B, false), Key(0x47, false), Key(0x47, true), Key(0x5B, true) };
        if (SendInput((uint)input.Length, input, Marshal.SizeOf<Input>()) != input.Length) throw new Win32Exception(Marshal.GetLastWin32Error());
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(50);
            if (!NativeGameBarState.IsInputRedirected()) return;
        }
        throw new InvalidOperationException("Game Bar did not release input; try again");
    }
    private static Input Key(ushort code, bool up) => new() { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { VirtualKey = code, Flags = (up ? 2u : 0)|(code is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2d or 0x2e ? 1u:0) } } };
    public void Dispose() { stopping.Cancel(); try { tracker.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } stopping.Dispose(); actions.Dispose(); VirtualGuide.Disconnect(); }
    [StructLayout(LayoutKind.Sequential)] private struct Input { internal uint Type; internal InputData Data; }
    [StructLayout(LayoutKind.Explicit, Size=32)] private struct InputData { [FieldOffset(0)] internal KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { internal ushort VirtualKey, Scan; internal uint Flags, Time; internal nuint Extra; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd,uint command);
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(nint hwnd,StringBuilder name,int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd,out uint pid);
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint hwnd,uint message,nint w,nint l);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll",SetLastError=true)] private static extern uint SendInput(uint count,Input[] input,int size);
}
