// SPDX-License-Identifier: AGPL-3.0-only
using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ONEXTweaks.WidgetBridge;

internal sealed record MediaLevels(string Epoch, long Revision, int? Brightness, double? Volume, bool? Muted,
    bool BrightnessAvailable, bool VolumeAvailable, string? BrightnessError, string? VolumeError,
    long BrightnessWrites, long VolumeWrites, long MuteWrites, bool? MicrophoneMuted, bool MicrophoneAvailable, string? MicrophoneError, long MicrophoneMuteWrites, int? BrightnessMinimum, bool BrightnessDimmed, int? BrightnessRestore);

internal sealed class MediaController
{
    private sealed record DimState(int SchemaVersion,string Panel,int Restore,int Minimum,string? Owner=null);
    private readonly string? dimPath;
    private DimState? dim;
    internal MediaController(string? brightnessDimPath=null)
    {
        dimPath=brightnessDimPath;
        if(dimPath is not null&&File.Exists(dimPath))
        {
            try{var saved=JsonSerializer.Deserialize<DimState>(File.ReadAllText(dimPath));if(saved is {SchemaVersion:1,Restore:>=0 and <=100,Minimum:>=0 and <=100}&&!string.IsNullOrEmpty(saved.Panel))dim=saved;}
            catch(Exception e) when(e is IOException or JsonException){/* A malformed preference must not disable ordinary brightness control. */}
        }
    }
    private void SaveDim(DimState? value)
    {
        if(dimPath is not null)
        {
            if(value is null){if(File.Exists(dimPath))File.Delete(dimPath);}
            else{Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dimPath))!);string temp=dimPath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(value));File.Move(temp,dimPath,true);}
        }
        dim=value;
    }
    private void ReconcileDim(string panel,int value)
    {if(dim is not null&&(!string.Equals(dim.Panel,panel,StringComparison.Ordinal)||value!=dim.Minimum))SaveDim(null);}
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string epoch = Guid.NewGuid().ToString("N");
    private long revision;
    private long brightnessWrites, volumeWrites, muteWrites, microphoneMuteWrites;
    internal async Task<MediaLevels> ReadAsync() => await Execute(null, null);
    internal async Task<MediaLevels> SetBrightnessAsync(int value) => await Execute("brightness", ValidatePercent(value));
    internal async Task<MediaLevels> ToggleBrightnessDimAsync() => await Execute("brightnessDim", null);
    internal async Task<string> DimForAwakeAsync()
    {
        string owner=Guid.NewGuid().ToString("N");
        try{await Execute("awakeDim",null,owner);return owner;}
        catch{try{await RestoreAwakeDimAsync(owner);}catch{}throw;}
    }
    internal Task<MediaLevels> RestoreAwakeDimAsync(string? owner) => Execute("awakeRestore",null,owner);
    internal async Task<MediaLevels> SetVolumeAsync(double value) => await Execute("volume", ValidatePercent(value));
    internal async Task<MediaLevels> ToggleMuteAsync() => await Execute("mute", null);
    internal async Task<MediaLevels> ToggleMicrophoneMuteAsync() => await Execute("microphoneMute", null);

    private async Task<MediaLevels> Execute(string? action, double? value,string? owner=null)
    {
        await gate.WaitAsync();
        try
        {
            // Synchronous COM calls and RCWs stay on this thread. Every call discovers the active
            // panel/default endpoint again, so docking or plugging headphones cannot leave stale handles.
            int? expectedBrightness = null,brightnessBefore=null;string? brightnessPanel=null;bool clearDimAfterConfirmation=false;
            if(action is "brightness" or "brightnessDim" or "awakeDim" or "awakeRestore")
            {
                var panel=BrightnessProvider.ReadPanel();ReconcileDim(panel.Instance,panel.Brightness);
                brightnessBefore=panel.Brightness;brightnessPanel=panel.Instance;
                if(action=="awakeRestore")
                {
                    // An icon tap, slider change or newer dim session relinquishes the old owner.
                    // A null owner is used only at helper startup to recover an interrupted session.
                    if(dim?.Owner is not null&&(owner is null||dim.Owner==owner))
                    {expectedBrightness=BrightnessProvider.Set(dim.Restore,panel.Instance);clearDimAfterConfirmation=true;}
                }
                else if(action is "brightnessDim" or "awakeDim")
                {
                    if(dim is not null&&action=="brightnessDim"){expectedBrightness=BrightnessProvider.Set(dim.Restore,panel.Instance);clearDimAfterConfirmation=true;}
                    else if(dim?.Owner is not null&&action=="awakeDim")
                    {
                        // Re-activating configured mode takes over an existing own dim without
                        // flashing the display. A manual dim (no owner) remains manual.
                        SaveDim(dim with{Owner=owner});
                    }
                    else if(dim is null)
                    {
                        if(panel.Steps.Length==0)throw new InvalidOperationException("Windows did not report a supported minimum brightness.");
                        int minimum=panel.Steps.Min();SaveDim(new DimState(1,panel.Instance,panel.Brightness,minimum,owner));
                        expectedBrightness=BrightnessProvider.Set(minimum,panel.Instance);
                    }
                }
                else{expectedBrightness=BrightnessProvider.Set((int)value!.Value,panel.Instance);clearDimAfterConfirmation=true;}
                if(expectedBrightness.HasValue)brightnessWrites++;
            }
            else if (action == "volume") { AudioEndpoint.SetVolume(value!.Value); volumeWrites++; }
            else if (action == "mute") { AudioEndpoint.ToggleMute(); muteWrites++; }
            else if (action == "microphoneMute") { AudioEndpoint.ToggleMute(1); microphoneMuteWrites++; }
            int? brightness = null, minimumBrightness=null; double? volume = null; bool? muted = null;
            string? brightnessError = null, volumeError = null;
            try
            {
                var panel=BrightnessProvider.ReadPanel();brightness=panel.Brightness;
                // WMI may acknowledge before CurrentBrightness catches up, especially after a
                // quick dim/restore sequence. Poll the actual value without issuing another write.
                for (int i = 0; expectedBrightness.HasValue && brightness != expectedBrightness && i < 30; i++)
                {
                    await Task.Delay(50);panel=BrightnessProvider.ReadPanel();brightness=panel.Brightness;
                }
                if(expectedBrightness.HasValue&&brightness!=expectedBrightness&&brightness==brightnessBefore&&
                    string.Equals(panel.Instance,brightnessPanel,StringComparison.Ordinal))
                {
                    // This provider occasionally drops a small change despite successful WMI
                    // transport. Retry once only while the same panel still holds its PRE-write
                    // value. A different/new external value is never replaced by this retry.
                    expectedBrightness=BrightnessProvider.Set(expectedBrightness.Value,brightnessPanel);brightnessWrites++;
                    for(int i=0;i<20;i++)
                    {await Task.Delay(50);panel=BrightnessProvider.ReadPanel();brightness=panel.Brightness;if(brightness==expectedBrightness)break;}
                }
                if (expectedBrightness.HasValue && brightness != expectedBrightness) throw new InvalidOperationException("Windows did not confirm brightness "+expectedBrightness+"% (reported "+brightness+"%).");
                if(clearDimAfterConfirmation)SaveDim(null);else ReconcileDim(panel.Instance,panel.Brightness);
                minimumBrightness=panel.Steps.Length>0?panel.Steps.Min():null;
            }
            catch (Exception e) when (!expectedBrightness.HasValue) { brightnessError = e.Message; }
            try { (volume, muted) = AudioEndpoint.Read(); } catch (Exception e) { volumeError = e.Message; }
            bool? microphoneMuted=null;string? microphoneError=null;
            try { (_, microphoneMuted)=AudioEndpoint.Read(1); } catch(Exception e) { microphoneError=e.Message; }
            return new(epoch, ++revision, brightness, volume, muted, brightness.HasValue, volume.HasValue, brightnessError, volumeError, brightnessWrites, volumeWrites, muteWrites, microphoneMuted, microphoneMuted.HasValue, microphoneError, microphoneMuteWrites, minimumBrightness, dim is not null, dim?.Restore);
        }
        finally { gate.Release(); }
    }
    internal static double ValidatePercent(double value) => double.IsFinite(value) && value is >= 0 and <= 100 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Expected a percentage from 0 to 100.");

    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

    private static class BrightnessProvider
    {
        internal sealed record Panel(string Instance, int Brightness, int[] Steps);
        private static ManagementObjectSearcher Query(string query) => new(new ManagementScope(@"root\wmi", new ConnectionOptions { Impersonation = ImpersonationLevel.Impersonate, Authentication = AuthenticationLevel.PacketPrivacy }),
            new ObjectQuery(query), new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(2) });
        internal static Panel ReadPanel() => Detect();
        internal static int Set(int value,string? expectedPanel=null)
        {
            Panel panel = Detect();
            if(expectedPanel is not null&&!string.Equals(expectedPanel,panel.Instance,StringComparison.Ordinal))throw new InvalidOperationException("The active brightness panel changed; retry the action.");
            int target = panel.Steps.Length == 0 ? value : panel.Steps.OrderBy(step => Math.Abs(step - value)).ThenBy(step => step).First();
            using var search = Query("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE");
            using var methods = search.Get();
            foreach (ManagementObject method in methods)
            using (method)
            {
                if (!string.Equals((string)method["InstanceName"], panel.Instance, StringComparison.Ordinal)) continue;
                using var input = method.GetMethodParameters("WmiSetBrightness");
                input["Timeout"] = (uint)0;
                input["Brightness"] = (byte)target;
                using var output = method.InvokeMethod("WmiSetBrightness", input, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(2) });
                // Some Windows brightness providers expose a void method (null output). Transport
                // success + independent CurrentBrightness readback, not an invented return code, wins.
                object? returned = output?.Properties.Cast<PropertyData>().FirstOrDefault(property => property.Name == "ReturnValue")?.Value;
                if (returned is not null && Convert.ToUInt32(returned) != 0) throw new InvalidOperationException("Windows brightness provider returned error " + returned);
                return target;
            }
            throw new InvalidOperationException("No writable brightness provider for the active panel.");
        }
        private static Panel Detect()
        {
            using var search = Query("SELECT * FROM WmiMonitorBrightness WHERE Active=TRUE");
            using var panels = search.Get();
            Panel? detected = null;
            foreach (ManagementObject panel in panels)
            using (panel)
            {
                if (detected is not null) throw new InvalidOperationException("Multiple active brightness providers; no panel was selected.");
                var steps = panel["Level"] is byte[] values ? values.Select(value => (int)value).Where(value=>value is >=0 and <=100).Distinct().ToArray() : [];
                detected = new((string)panel["InstanceName"], Convert.ToInt32(panel["CurrentBrightness"]), steps);
            }
            return detected ?? throw new InvalidOperationException("Windows does not expose brightness for an active internal panel.");
        }
    }

    private static class AudioEndpoint
    {
        private static readonly Guid volumeIid = new("5CDF2C82-841E-4546-9722-0CF74078229A");
        private static readonly Guid context = new("A5C28226-4641-4C76-AC72-A590002D7A1A");
        internal static (double Volume, bool Muted) Read(int flow=0) => WithEndpoint(volume =>
        {
            Check(volume.GetMasterVolumeLevelScalar(out float level));
            Check(volume.GetMute(out bool muted));
            return (level * 100.0, muted);
        },flow);
        internal static void SetVolume(double value) => WithEndpoint(volume =>
        {
            var eventContext = context;
            Check(volume.SetMasterVolumeLevelScalar((float)(value / 100.0), ref eventContext));
            return 0; // Set volume only. Preserve the Windows mute state.
        });
        internal static void ToggleMute(int flow=0) => WithEndpoint(volume =>
        {
            var eventContext = context;
            Check(volume.GetMute(out bool muted));
            Check(volume.SetMute(!muted, ref eventContext));
            Check(volume.GetMute(out bool actual));
            if(actual==muted)throw new IOException("Windows did not confirm the endpoint mute change.");
            return 0;
        },flow);
        private static T WithEndpoint<T>(Func<IAudioEndpointVolume, T> action,int flow=0)
        {
            object? enumerator = null, device = null, volume = null;
            try
            {
                enumerator = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true)!)!;
                Check(((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(flow, 1, out var endpoint)); // default multimedia: render0 / capture1
                device = endpoint;
                var iid = volumeIid;
                Check(endpoint.Activate(ref iid, 23, IntPtr.Zero, out volume));
                return action((IAudioEndpointVolume)volume);
            }
            finally { Release(volume); Release(device); Release(enumerator); }
        }
        private static void Check(int result) { Marshal.ThrowExceptionForHR(result); }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        }
        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out uint channels);
            [PreserveSig] int SetMasterVolumeLevel(float value, ref Guid context);
            [PreserveSig] int SetMasterVolumeLevelScalar(float value, ref Guid context);
            [PreserveSig] int GetMasterVolumeLevel(out float value);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float value);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float value, ref Guid context);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float value, ref Guid context);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float value);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float value);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        }
    }
}
