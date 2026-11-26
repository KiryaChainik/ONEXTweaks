// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace ONEXTweaks.WidgetBridge;

/// <summary>Route the measured OXP3 Guide button directly to the device widget.</summary>
internal sealed class GuideButtonLauncher : IAsyncDisposable
{
    private const string NativeKey = @"Software\Microsoft\GameBar";
    private const string ConfigKey = @"Software\ONEXTweaks\Overlay";
    private readonly CancellationTokenSource stopping = new();
    private readonly Task worker;
    private readonly nint library;
    private readonly int? nativeDefault;
    private bool ownsNativeSetting;

    [StructLayout(LayoutKind.Sequential)] private struct Pad
    { internal ushort Buttons; internal byte Left, Right; internal short Lx, Ly, Rx, Ry; }
    [StructLayout(LayoutKind.Sequential)] private struct State { internal uint Packet; internal Pad Pad; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReadState(uint index, out State state);
    [DllImport("kernel32.dll", EntryPoint = "GetProcAddress")] private static extern nint GetOrdinal(nint module, nint ordinal);

    internal GuideButtonLauncher(string reports, GameBarEntry entry)
    {
        using var config = Registry.CurrentUser.OpenSubKey(ConfigKey);
        if (config?.GetValue("DirectGuideEnabled") is not int enabled || enabled != 1)
        { worker = Task.CompletedTask; return; }
        try
        {
            library = NativeLibrary.Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "xinput1_4.dll"));
            nint address = GetOrdinal(library, 100);
            if (address == 0) throw new InvalidOperationException("This XInput runtime does not expose Guide state");
            var read = Marshal.GetDelegateForFunctionPointer<ReadState>(address);
            int index = Math.Clamp(config.GetValue("GuideControllerIndex") as int? ?? 0, 0, 3);
            nativeDefault = config.GetValue("NativeNexusDefault") as int?;
            using (var native = Registry.CurrentUser.CreateSubKey(NativeKey))
            {
                native.SetValue("UseNexusForGameBarEnabled", 0, RegistryValueKind.DWord);
                if(native.GetValue("UseNexusForGameBarEnabled") is not int policy||policy!=0)throw new IOException("Native Guide policy was not confirmed");
            }
            ownsNativeSetting = true;
            File.WriteAllText(Path.Combine(reports,"guide-policy-ready.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,pid=Environment.ProcessId,nexus=0,confirmed=true,beforeWidgetPrewarm=true},Backend.Program.Json));
            worker = Task.Run(async () =>
            {
                var pressFilter = new GuidePressFilter(initiallyHeld: true);
                bool lastUpOpen = false;
                bool pending = false, closeAfterRelease = false;
                int presses = 0;
                try
                {
                    long reportAt=0;
                    while (!stopping.IsCancellationRequested)
                    {
                        uint status = read((uint)index, out var state);
                        bool down = index!=VirtualGuide.OwnIndex && (state.Pad.Buttons & 0x400) != 0;
                        // Capture the actual overlay state while Guide is UP. Native dismissal
                        // can precede our next poll, so state read after Down can be too late.
                        if(status==0&&!down)lastUpOpen=NativeGameBarState.IsInputRedirected();
                        if(Environment.TickCount64>=reportAt)
                        {
                            reportAt=Environment.TickCount64+2000;
                            try{await File.WriteAllTextAsync(Path.Combine(reports,"guide-handler.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,pid=Environment.ProcessId,enabled=true,index,connected=status==0,releaseMs=40,presses},Backend.Program.Json),stopping.Token);}
                            catch(IOException){}catch(UnauthorizedAccessException){}
                        }
                        bool wasHeld = pressFilter.IsHeld;
                        if (pressFilter.Observe(status == 0, down, Environment.TickCount64))
                        {
                            pending = true;
                            closeAfterRelease = entry.BeginGuidePress(lastUpOpen);
                        }
                        // Guide remains a dismissal key inside Game Bar even when its system
                        // opening action is disabled. Launch only after the physical release.
                        if (pending && wasHeld && !pressFilter.IsHeld)
                        {
                            pending = false;
                            presses++;
                            entry.EnqueueGuideAction(closeAfterRelease,index,presses);
                        }
                        await Task.Delay(12, stopping.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    RestoreNativeSetting();
                    try { await File.WriteAllTextAsync(Path.Combine(reports, "guide-direct-entry.json"), JsonSerializer.Serialize(new { error = e.ToString(), nativeGuideRestored = true }, Backend.Program.Json)); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            });
        }
        catch
        {
            if (library != 0) NativeLibrary.Free(library);
            RestoreNativeSetting();
            stopping.Dispose();
            throw;
        }
    }
    private void RestoreNativeSetting()
    {
        if (!ownsNativeSetting) return;
        using var native = Registry.CurrentUser.CreateSubKey(NativeKey);
        if (native.GetValue("UseNexusForGameBarEnabled") is int current && current == 0)
        {
            if (nativeDefault is int value) native.SetValue("UseNexusForGameBarEnabled", value, RegistryValueKind.DWord);
            else native.DeleteValue("UseNexusForGameBarEnabled", false);
        }
        ownsNativeSetting = false;
    }
    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        try { await worker; }
        finally
        {
            using (var configuration = Registry.CurrentUser.OpenSubKey(ConfigKey))
                if (configuration?.GetValue("DirectGuideEnabled") is not int enabled || enabled != 1)
                    RestoreNativeSetting();
            if (library != 0) NativeLibrary.Free(library);
            stopping.Dispose();
        }
    }
}
