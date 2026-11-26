// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace ONEXTweaks.WidgetBridge;

internal static class SharpnessNativeVerification
{
    private sealed record Recovery(HdrRecovery Hdr,byte[]? Sharpness);
    internal static async Task<int> Watchdog(string snapshot,string complete)
    {
        await Task.Delay(90000);if(File.Exists(complete))return 0;
        try{var value=JsonSerializer.Deserialize<Recovery>(await File.ReadAllTextAsync(snapshot),Backend.Program.Json)??throw new IOException("Sharpness recovery is missing");Restore(value);await File.WriteAllTextAsync(complete+".watchdog-restored","restored");return 0;}
        catch(Exception failure){await File.WriteAllTextAsync(complete+".watchdog-error",failure.Message);return 1;}
    }
    private static void Restore(Recovery value)
    {
        var hdr=new WindowsHdrDisplay();
        if(value.Sharpness!=null){hdr.SetEnabled(false);using var display=new IntelDisplayNative();display.WriteSharpnessRaw(value.Sharpness);}
        hdr.Restore(value.Hdr);
    }
    internal static async Task<int> Run(string report)
    {
        string snapshot=report+".recovery-private.json",complete=report+".complete";
        var hdr=new WindowsHdrDisplay();var original=hdr.Snapshot();string root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(report)!,".."));
        string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        var hashes=Directory.GetFiles(root,"*.json").ToDictionary(path=>Path.GetFileName(path)!,path=>Hash(path));
        (string Raw,int Writes) Power(){using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(report)!,"widget-bridge.json")));return(doc.RootElement.GetProperty("state").GetProperty("rawLimits").GetString()!,doc.RootElement.GetProperty("hardwareWrites").GetInt32());}
        var powerBefore=Power();bool fse=GamingExperience.IsActive(),restored=false,sharpnessSetAttempted=false;int sharpnessAcknowledgements=0;string? error=null;var checks=new List<object>();
        var saved=new Recovery(original,null);
        async Task SaveRecovery(){await File.WriteAllTextAsync(snapshot+".tmp",JsonSerializer.Serialize(saved,Backend.Program.Json));File.Move(snapshot+".tmp",snapshot,true);}
        await SaveRecovery();File.Delete(complete);
        var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};info.ArgumentList.Add("--sharpness-native-watchdog");info.ArgumentList.Add(snapshot);info.ArgumentList.Add(complete);
        using var watchdog=Process.Start(info)??throw new IOException("HDR restoration watchdog did not start");
        try
        {
            foreach(bool enabled in new[]{false,true})
            {
                hdr.SetEnabled(enabled);await Task.Delay(900);var actual=hdr.Read();
                if(actual.Enabled!=enabled)throw new IOException("HDR transition did not reach the requested state");
                using var display=new IntelDisplayNative();checks.Add(new{hdrEnabled=actual.Enabled,hdrActive=actual.Active,sharpness=display.ProbeSharpness()});
                if(!enabled)
                {
                    var feature=display.ReadSharpness();if(!feature.Available||feature.Minimum!=1||feature.Maximum!=5||feature.Step!=1)throw new IOException("G3 sharpness range was not acknowledged as1..5");
                    saved=saved with{Sharpness=display.ReadSharpnessRaw()};await SaveRecovery();
                    foreach(int level in new[]{1,3,5,0}){sharpnessSetAttempted=true;display.SetSharpness(level);var read=display.ReadSharpness();if(read.Value!=level)throw new IOException("Actual sharpness readback differs");sharpnessAcknowledgements++;checks.Add(new{check="Actual SDR sharpness",level,readback=read.Value,nativeReadback=true});}
                    bool rejected=false;try{display.SetSharpness(6);}catch(ArgumentOutOfRangeException){rejected=true;}if(!rejected)throw new IOException("Out-of-range sharpness was accepted");
                    display.WriteSharpnessRaw(saved.Sharpness!);checks.Add(new{check="Exact SDR sharpness restoration",nativeReadback=true,outOfRangeRejectedBeforeSet=true});
                }
            }
        }
        catch(Exception failure){error=failure.ToString().Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>");}
        finally
        {
            try{Restore(saved);restored=hdr.Snapshot()==original;if(!restored)throw new IOException("Exact HDR state was not restored");await File.WriteAllTextAsync(complete,"restored");}
            catch(Exception failure){error=(error??"")+"; restoration: "+failure.Message;}
        }
        await Task.Delay(900);var powerAfter=Power();var files=hashes.ToDictionary(pair=>pair.Key,pair=>Hash(Path.Combine(root,pair.Key!))==pair.Value);
        bool passed=error==null&&restored&&powerAfter.Raw==powerBefore.Raw&&powerAfter.Writes==powerBefore.Writes&&files.Values.All(value=>value)&&GamingExperience.IsActive()==fse;
        await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed,error,checks,exactHdrStateRestored=restored,powerPreserved=powerAfter.Raw==powerBefore.Raw,powerWrites=powerAfter.Writes-powerBefore.Writes,filesPreserved=files,fsePreserved=GamingExperience.IsActive()==fse,sharpnessSetAttempted,sharpnessAcknowledgements,physicalImageQualityTested=false},Backend.Program.Json));return passed?0:1;
    }
}
