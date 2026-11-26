// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace ONEXTweaks.WidgetBridge;

internal static class ScalingNativeVerification
{
    private sealed record Recovery(DisplayPreferences Values,HdrRecovery Hdr,byte[] Normal,byte[] Retro,string SettingsFile);
    private static void Restore(Recovery value)
    {
        using var display=new DisplayController(value.SettingsFile);using var native=new IntelDisplayNative();native.RefreshOutput();
        int preferred=BitConverter.ToInt32(value.Normal,value.Normal[4]>=1?24:8);if(preferred==0)preferred=BitConverter.ToInt32(value.Normal,8);
        var current=native.ReadScaling();int actualPreferred=BitConverter.ToInt32(current,current[4]>=1?24:8);
        // At native resolution the current type is Identity. Writing that type does not restore
        // the remembered lower-resolution method; restore that preference before returning native.
        if(actualPreferred!=preferred)
        {
            if((native.ReadScalingCaps()&14)==0){display.Set("resolution",JsonSerializer.SerializeToElement("1280x800"));native.RefreshOutput();}
            var bytes=(byte[])value.Normal.Clone();bytes[5]=1;bytes[20]=1;BitConverter.GetBytes(preferred).CopyTo(bytes,8);native.WriteScaling(bytes);
        }
        if(!native.ReadRetro().SequenceEqual(value.Retro))native.WriteRetro(value.Retro);
        display.ApplyProfile(value.Values);
        new WindowsHdrDisplay().Restore(value.Hdr);
        native.RefreshOutput();
        // With the lower-resolution preference restored, restore the original current type too.
        // Identity at native resolution preserves (rather than replaces) that preference.
        if(!native.ReadScaling().SequenceEqual(value.Normal)){var original=(byte[])value.Normal.Clone();original[20]=1;native.WriteScaling(original);}
    }
    internal static async Task<int> RestoreFile(string path)
    {Restore(JsonSerializer.Deserialize<Recovery>(await File.ReadAllTextAsync(path),Backend.Program.Json)??throw new IOException("Missing scaling snapshot"));return 0;}
    internal static async Task<int> Watchdog(string snapshot,string complete)
    {
        await Task.Delay(90000);if(File.Exists(complete))return 0;
        try{Restore(JsonSerializer.Deserialize<Recovery>(await File.ReadAllTextAsync(snapshot),Backend.Program.Json)??throw new IOException("Scaling recovery snapshot is missing"));await File.WriteAllTextAsync(complete+".watchdog-restored","restored");return 0;}
        catch(Exception error){await File.WriteAllTextAsync(complete+".watchdog-error",error.Message);return 1;}
    }
    internal static async Task<int> Run(string report,bool methods=false,string? uiFolder=null)
    {
        string folder=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(report)!,"..")),settings=report+".settings-private.json",snapshot=report+".recovery-private.json",complete=report+".complete";
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var files=Directory.GetFiles(folder,"*.json").ToDictionary(path=>Path.GetFileName(path)!,Hash);
        (string Raw,int Writes) Power(){string path=Path.Combine(Path.GetDirectoryName(report)!,"widget-bridge.json");if(DateTime.UtcNow-File.GetLastWriteTimeUtc(path)>TimeSpan.FromSeconds(12))throw new IOException("Power telemetry is stale");using var doc=JsonDocument.Parse(File.ReadAllText(path));return(doc.RootElement.GetProperty("state").GetProperty("rawLimits").GetString()!,doc.RootElement.GetProperty("hardwareWrites").GetInt32());}
        File.Copy(Path.Combine(folder,"display-settings.json"),settings,true);using var display=new DisplayController(settings);
        var original=display.Capture(includeHdr:true);var hdr=new WindowsHdrDisplay();var hdrBefore=hdr.Snapshot();
        using var native=new IntelDisplayNative();var normalBefore=native.ReadScaling();var retroBefore=native.ReadRetro();
        var saved=new Recovery(original,hdrBefore,normalBefore,retroBefore,settings);await File.WriteAllTextAsync(snapshot,JsonSerializer.Serialize(saved,Backend.Program.Json));File.Delete(complete);
        var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};info.ArgumentList.Add("--scaling-native-watchdog");info.ArgumentList.Add(snapshot);info.ArgumentList.Add(complete);using var recovery=Process.Start(info)??throw new IOException("Scaling watchdog did not start");
        var powerBefore=Power();bool fse=GamingExperience.IsActive(),restored=false;string? error=null;var checks=new List<object>();
        bool modeRestored=false,hdrRestored=false,normalRestored=false,retroRestored=false,retroRawEqual=false;
        int phase=0;
        async Task Ui(int mode,int? method)
        {
            if(uiFolder==null)return;phase++;string ready=Path.Combine(uiFolder,"onex-scaling-native-ready.json"),ack=Path.Combine(uiFolder,"onex-scaling-native-ack.txt");
            await File.WriteAllTextAsync(ready,JsonSerializer.Serialize(new{phase,mode,method},Backend.Program.Json));bool received=false;
            for(int attempt=0;attempt<350;attempt++){await Task.Delay(100);if(File.Exists(ack)&&await File.ReadAllTextAsync(ack)==phase.ToString()){received=true;break;}}
            if(!received)throw new IOException("Native scaling UI acknowledgement timed out at phase"+phase);
        }
        try
        {
            var lower=display.Set("resolution",JsonSerializer.SerializeToElement("1280x800"));await Task.Delay(700);native.RefreshOutput();
            if(lower.Modes.Current?.Width!=1280||lower.Modes.Current.Height!=800)throw new IOException("Lower Windows resolution was not applied");
            checks.Add(new{check="Windows lower resolution",actual=lower.Modes.Current,atNative=lower.AtNativeResolution,scalingMode=lower.Settings["scalingMode"],scalingMethod=lower.Settings["scalingMethod"],caps=native.ReadScalingCaps()});
            foreach(int mode in new[]{1,2,1})
            {
                var result=display.Set("scalingMode",JsonSerializer.SerializeToElement(mode));var retro=native.ReadRetro();
                if(result.Settings["scalingMode"].Value!=mode||(retro[6]!=0)!=(mode==2))throw new IOException("Scaling Mode differs from direct driver readback");
                checks.Add(new{check="Scaling Mode",mode,retroEnabled=retro[6]!=0,readback=result.Settings["scalingMode"].Value});
                await Ui(mode,result.Settings["scalingMethod"].Value);
            }
            if(methods)
            {
                foreach(int mode in new[]{1,2})
                {
                    display.Set("scalingMode",JsonSerializer.SerializeToElement(mode));
                    foreach(int method in mode==1?new[]{0,1,2}:new[]{0,1})
                    {
                        var result=display.Set("scalingMethod",JsonSerializer.SerializeToElement(method));native.RefreshOutput();var normal=native.ReadScaling();var retro=native.ReadRetro();
                        if(result.Settings["scalingMethod"].Value!=method)throw new IOException("Scaling Method UI state differs from requested value");
                        int expected=mode==1?new[]{2,4,8}[method]:method==1?2:1;
                        bool direct=mode==1?BitConverter.ToInt32(normal,8)==expected||normal[4]>=1&&BitConverter.ToInt32(normal,24)==expected:retro[6]!=0&&BitConverter.ToInt32(retro,8)==expected;
                        if(!direct)throw new IOException("Scaling Method differs from direct driver readback");
                        checks.Add(new{check="Scaling Method",mode,method,driverEnum=expected,current=BitConverter.ToInt32(normal,8),preferred=BitConverter.ToInt32(normal,24),retroType=BitConverter.ToInt32(retro,8),nativeReadback=true});
                        await Ui(mode,method);
                    }
                }
            }
        }
        catch(Exception failure){error=failure.ToString().Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>");}
        finally
        {
            try
            {
                Restore(saved);native.RefreshOutput();var after=display.Capture(includeHdr:true);var normal=native.ReadScaling();var retro=native.ReadRetro();
                modeRestored=after.Mode==original.Mode;hdrRestored=hdr.Snapshot()==hdrBefore;
                normalRestored=BitConverter.ToInt32(normal,8)==BitConverter.ToInt32(normalBefore,8)&&BitConverter.ToInt32(normal,24)==BitConverter.ToInt32(normalBefore,24);
                retroRawEqual=retro.SequenceEqual(retroBefore);retroRestored=retro[6]==retroBefore[6]&&(retroBefore[6]==0||BitConverter.ToInt32(retro,8)==BitConverter.ToInt32(retroBefore,8));
                restored=modeRestored&&hdrRestored&&normalRestored&&retroRestored;if(!restored)throw new IOException("Fresh mode/HDR/scaling state did not restore");await File.WriteAllTextAsync(complete,"restored");
            }
            catch(Exception failure){error=(error??"")+"; restoration: "+failure.Message;}
        }
        await Task.Delay(800);var powerAfter=Power();var preserved=files.ToDictionary(pair=>pair.Key,pair=>Hash(Path.Combine(folder,pair.Key))==pair.Value);
        bool passed=error==null&&restored&&preserved.Values.All(value=>value)&&powerAfter.Raw==powerBefore.Raw&&powerAfter.Writes==powerBefore.Writes&&GamingExperience.IsActive()==fse;
        await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed,error,methodsTested=methods,checks,modeRestored,hdrRestored,normalRestored,retroRestored,retroRawEqual,disabledRetroTypeMayNormalize=retroBefore[6]==0&&!retroRawEqual,filesPreserved=preserved,powerPreserved=powerAfter.Raw==powerBefore.Raw,powerWrites=powerAfter.Writes-powerBefore.Writes,fsePreserved=GamingExperience.IsActive()==fse,actualColorPipelineReadback=false,physicalImageQualityTested=false},Backend.Program.Json));return passed?0:1;
    }
}
