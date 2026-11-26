// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace ONEXTweaks.WidgetBridge;

internal static class HdrNativeVerification
{
    internal static async Task<int> Restore(string snapshot)
    {
        var before=JsonSerializer.Deserialize<HdrRecovery>(await File.ReadAllTextAsync(snapshot),Backend.Program.Json)??throw new InvalidDataException("HDR recovery snapshot is missing");
        new WindowsHdrDisplay().Restore(before);return 0;
    }
    internal static async Task<int> Watchdog(string snapshot,string complete)
    {
        await Task.Delay(90000);if(File.Exists(complete))return 0;
        try{await Restore(snapshot);await File.WriteAllTextAsync(complete+".watchdog-restored","restored");return 0;}
        catch(Exception error){await File.WriteAllTextAsync(complete+".watchdog-error",error.ToString());return 1;}
    }
    internal static async Task<int> Run(string report)
    {
        try{return await RunCore(report);}
        catch(Exception error){await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed=false,error=error.ToString().Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>"),completed=false},Backend.Program.Json));return 1;}
    }
    private static (string Raw,int Writes) PowerTelemetry(string report)
    {
        string path=Path.Combine(Path.GetDirectoryName(report)!,"widget-bridge.json");
        for(int attempt=0;attempt<40;attempt++)
        {
            try
            {
                if(DateTime.UtcNow-File.GetLastWriteTimeUtc(path)>TimeSpan.FromSeconds(12))throw new IOException("Bridge hardware telemetry is stale");
                using var json=JsonDocument.Parse(File.ReadAllText(path));var root=json.RootElement;
                return(root.GetProperty("state").GetProperty("rawLimits").GetString()!,root.GetProperty("hardwareWrites").GetInt32());
            }
            catch(JsonException){Thread.Sleep(25);}
        }
        throw new IOException("Bridge hardware telemetry did not settle");
    }
    private static async Task<int> RunCore(string report)
    {
        string snapshot=report+".recovery-private.json",complete=report+".complete";
        var hdr=new WindowsHdrDisplay();var before=hdr.Snapshot();var initial=hdr.Read();
        if(!initial.Available||!initial.Supported)throw new IOException(initial.Error??"HDR is unavailable");
        var folder=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(report)!,".."));
        var names=new[]{"power-preferences.json","performance-profiles.json","display-settings.json","brightness-dim.json","awake-settings.json","overlay-preferences.json","fps-preferences.json"};
        string? Hash(string name)=>File.Exists(Path.Combine(folder,name))?Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder,name)))):null;
        var hashes=names.ToDictionary(name=>name,Hash);
        // The live Bridge owns the PowerAgent connection. Read its fresh real
        // hardware telemetry; this display-only test never opens a second client.
        var powerBefore=PowerTelemetry(report);
        await File.WriteAllTextAsync(snapshot,JsonSerializer.Serialize(before,Backend.Program.Json));File.Delete(complete);
        var watchdog=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};watchdog.ArgumentList.Add("--hdr-native-watchdog");watchdog.ArgumentList.Add(snapshot);watchdog.ArgumentList.Add(complete);
        using var recovery=Process.Start(watchdog)??throw new IOException("HDR recovery watchdog did not start");
        var checks=new List<object>();string? error=null;bool restored=false;bool calibrationChanged=false;
        try
        {
            hdr.SetEnabled(false);var off=hdr.Read();if(off.Enabled||off.Active)throw new IOException("HDR Off was not applied");checks.Add(new{check="HDR Off",nativeReadback=true});
            hdr.SetEnabled(true);var on=hdr.Read();if(!on.Enabled||!on.Active)throw new IOException("HDR On was not active");checks.Add(new{check="HDR On",nativeReadback=true});
            hdr.SetAutoHdr(false);if(hdr.Read().AutoHdr!=false)throw new IOException("Auto HDR Off preference failed");
            hdr.SetAutoHdr(true);if(hdr.Read().AutoHdr!=true)throw new IOException("Auto HDR On preference failed");checks.Add(new{check="Auto HDR On/Off",windowsPreferenceReadback=true,gameRenderingTested=false});
            if(initial.SdrBrightness is int percent)
            {
                int selected=percent>=100?99:percent+1;hdr.SetSdrBrightness(selected);var actual=hdr.Read();if(actual.SdrBrightness!=selected)throw new IOException("SDR brightness change failed");
                checks.Add(new{check="SDR brightness",nativeReadback=true,percent=selected,nits=actual.SdrNits});
            }
            if(initial.CurrentProfile!=null)
            {hdr.SetCalibration(initial.CurrentProfile);if(hdr.Read().CurrentProfile!=initial.CurrentProfile)throw new IOException("Current HDR calibration selection failed");checks.Add(new{check="Current calibration remains selected",nativeReadback=true,changed=false});}
            calibrationChanged=initial.NewestProfile!=null&&initial.NewestProfile!=initial.CurrentProfile;
            hdr.Reset();var reset=hdr.Read();if(!reset.Enabled||reset.AutoHdr!=true||initial.NewestProfile!=null&&reset.CurrentProfile!=initial.NewestProfile)throw new IOException("HDR default reset failed");
            checks.Add(new{check="HDR reset: On,Auto On,newest calibration",nativeReadback=true,calibrationChanged,newestByDate=initial.Profiles.Zip(initial.Profiles.Skip(1),(a,b)=>a.Date>=b.Date).All(value=>value)});
        }
        catch(Exception failure){error=failure.ToString();}
        finally
        {
            try
            {
                hdr.Restore(before);var after=hdr.Snapshot();restored=after==before;
                if(!restored)throw new IOException("Exact fresh HDR/Auto HDR/calibration/white-level snapshot was not restored");
                await File.WriteAllTextAsync(complete,"restored");
            }
            catch(Exception failure){error=(error??"")+"\nRecovery: "+failure;}
        }
        var files=hashes.ToDictionary(pair=>pair.Key,pair=>Hash(pair.Key)==pair.Value);
        var powerAfter=PowerTelemetry(report);bool powerPreserved=powerAfter.Raw==powerBefore.Raw;
        int tdpWrites=powerAfter.Writes-powerBefore.Writes;
        bool passed=error==null&&restored&&files.Values.All(value=>value)&&powerPreserved&&tdpWrites==0;
        await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed,error,checks,profileCount=initial.Profiles.Length,exactNativeStateRestored=restored,filesPreserved=files,powerPreserved,tdpWrites,calibrationFilesWritten=false,calibrationAppInstalled=WindowsHdrDisplay.CalibrationInstalled(),calibrationAppLaunched=false,physicalImageQualityTested=false,realCompatibleGameAutoHdrTested=false},Backend.Program.Json));
        return passed?0:1;
    }
}
