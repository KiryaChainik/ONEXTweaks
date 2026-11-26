// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Text.Json;
using ONEXTweaks.Panel;

namespace ONEXTweaks.WidgetBridge;

internal sealed record DisplayRecoverySnapshot(DisplayPreferences Values, int Pl1, int Pl2, string SettingsFile, byte[]? NormalScaling=null, byte[]? RetroScaling=null);
internal static class DisplayNativeVerification
{
    internal static async Task Restore(string file, bool colorsChanged)
    {
        var original = JsonSerializer.Deserialize<DisplayRecoverySnapshot>(File.ReadAllText(file), Backend.Program.Json) ?? throw new InvalidDataException("Missing display recovery snapshot");
        using var display = new DisplayController(original.SettingsFile);
        display.ApplyProfile(original.Values);
        if (colorsChanged && original.Values.Colors == null) display.RestoreUnownedColors();
        using(var native=new IntelDisplayNative())
        {
            if(original.RetroScaling is {} retro&&!native.ReadRetro().SequenceEqual(retro))native.WriteRetro(retro);
            if(original.NormalScaling is {} scaling&&!native.ReadScaling().SequenceEqual(scaling)){var restore=(byte[])scaling.Clone();restore[20]=1;native.WriteScaling(restore);}
        }
        using var power = new PowerChannel(); var read = await power.ReadAsync();
        if (!read.Ok || read.State == null) throw new IOException(read.Error);
        if (read.State.Pl1Watts != original.Pl1 || read.State.Pl2Watts != original.Pl2)
        { var result = await power.SetAsync(original.Pl1, original.Pl2); if (!result.Ok) throw new IOException(result.Error); }
    }
    internal static async Task<int> Watchdog(string originalFile, string completeFile)
    {
        await Task.Delay(45000);
        if (File.Exists(completeFile)) return 0;
        try { await Restore(originalFile, File.Exists(originalFile + ".colors-started")); File.WriteAllText(originalFile + ".watchdog-restored", "restored"); return 0; }
        catch (Exception error) { File.WriteAllText(originalFile + ".watchdog-error", error.ToString()); return 1; }
    }
    internal static async Task<int> Run(string resultFile)
    {
        string originalFile = resultFile + ".original-private.json", completeFile = resultFile + ".complete";
        string settingsFile = resultFile + ".settings-private.json";
        var cases = new List<object>(); var failures = new List<object>(); bool restored = false;
        string? error = null; DisplayPreferences? before = null, after = null;
        File.Delete(completeFile); File.Delete(originalFile + ".colors-started"); File.Delete(settingsFile);
        PowerReply powerBefore; using(var power = new PowerChannel())powerBefore = await power.ReadAsync();
        if (!powerBefore.Ok || powerBefore.State == null) throw new IOException(powerBefore.Error);
        using var display = new DisplayController(settingsFile);
        before = display.Capture();byte[]? normalScaling=null,retroScaling=null;
        using(var native=new IntelDisplayNative()){try{normalScaling=native.ReadScaling();retroScaling=native.ReadRetro();}catch{}}
        await File.WriteAllTextAsync(originalFile, JsonSerializer.Serialize(new DisplayRecoverySnapshot(before, (int)powerBefore.State.Pl1Watts, (int)powerBefore.State.Pl2Watts, settingsFile,normalScaling,retroScaling), Backend.Program.Json));
        var watchdog = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) watchdog.ArgumentList.Add(typeof(DisplayNativeVerification).Assembly.Location);
        watchdog.ArgumentList.Add("--display-native-watchdog"); watchdog.ArgumentList.Add(originalFile); watchdog.ArgumentList.Add(completeFile);
        using var recovery = Process.Start(watchdog) ?? throw new IOException("Display recovery process did not start");
        try
        {
            void Change(string key, int value)
            {
                try
                {
                    var actual = display.Set(key, JsonSerializer.SerializeToElement(value));
                    if (!actual.Settings.TryGetValue(key, out var setting) || setting.Value != value) throw new IOException("Display UI value differs from request");
                    cases.Add(new { key, value, accepted = true, setting.Reported, actual.ColorReadback });
                }
                catch (Exception failure) { failures.Add(new { key, value, error = failure.Message }); }
            }
            Change("lowLatency", 1); if (before.LowLatency is int latency) Change("lowLatency", latency);
            Change("frameSync", 2); if (before.FrameSync is int sync) Change("frameSync", sync);
            Change("vrr", 0); if (before.Vrr is int vrr) Change("vrr", vrr);
            Change("frameGeneration", 1); Change("frameGeneration", 2); Change("frameGeneration", 3); Change("frameGeneration", before.FrameGeneration??0);
            File.WriteAllText(originalFile + ".colors-started", "test");
            Change("saturation", 51); Change("gamma", 101);
            var modes = display.Read().Modes;
            if(modes.Current is {} nativeMode&&modes.Modes.Any(mode=>mode.Width==1280&&mode.Height==800))
            {
                try
                {
                    var lower=display.Set("resolution",JsonSerializer.SerializeToElement("1280x800"));if(lower.Modes.Current?.Width!=1280||lower.Modes.Current.Height!=800)throw new IOException("Resolution readback failed");cases.Add(new{key="resolution",value="1280x800",accepted=true});
                    Change("scalingMode",1);Change("scalingMethod",1);Change("scalingMethod",2);Change("scalingMode",2);Change("scalingMethod",1);
                }
                catch(Exception failure){failures.Add(new{key="resolutionScaling",error=failure.Message});}
                finally{display.Set("resolution",JsonSerializer.SerializeToElement(nativeMode.Width+"x"+nativeMode.Height));display.Set("refreshRate",JsonSerializer.SerializeToElement(nativeMode.Hz));}
            }
            if (modes.Current is { } current && modes.Modes.Contains(current with { Hz = 60 }))
            {
                try
                { var actual = display.Set("refreshRate", JsonSerializer.SerializeToElement(60)); if (actual.Modes.Current?.Hz != 60) throw new IOException("Refresh rate readback failed"); cases.Add(new { key = "refreshRate", value = 60, accepted = true }); display.Set("refreshRate", JsonSerializer.SerializeToElement(current.Hz)); }
                catch (Exception failure) { failures.Add(new { key = "refreshRate", error = failure.Message }); }
            }
        }
        catch (Exception failure) { error = failure.ToString(); }
        finally
        {
            try
            {
                await Restore(originalFile, File.Exists(originalFile + ".colors-started")); after = display.Capture();
                // This instance holds pre-recovery preferences; use a fresh reader for confirmation.
                using var fresh = new DisplayController(settingsFile); after = fresh.Capture();
                PowerReply finalPower;using(var power = new PowerChannel())finalPower = await power.ReadAsync();
                restored = after.Mode == before.Mode && after.LowLatency == before.LowLatency && after.FrameSync == before.FrameSync && after.Vrr == before.Vrr && after.VrrMode == before.VrrMode && (after.FrameGeneration??0)==(before.FrameGeneration??0) && after.Colors == before.Colors && finalPower.Ok && finalPower.State?.Pl1Watts == powerBefore.State.Pl1Watts && finalPower.State?.Pl2Watts == powerBefore.State.Pl2Watts;
                if (!restored) throw new IOException("Original display/power state was not restored");
                File.WriteAllText(completeFile, "restored");
            }
            catch (Exception failure) { error = (error ?? "") + "\nRecovery: " + failure; }
            await File.WriteAllTextAsync(resultFile, JsonSerializer.Serialize(new { passed = error == null && restored && failures.Count == 0, error, cases, failures, originalEffectiveStateRestored = restored, originalPower = new { powerBefore.State.Pl1Watts, powerBefore.State.Pl2Watts }, before, after, physicalColorEffectTested = false, originalNativeColorReadbackAvailable = false, fallbackColorRecovery = "Restore default for our two blocks when the original driver configuration is not readable" }, Backend.Program.Json));
        }
        return error == null && restored ? 0 : 1;
    }
}
