// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class DisplayLayoutNativeVerification
{
    internal static async Task<int> Run(string reportPath)
    {
        string folder=Path.GetDirectoryName(reportPath)!;string settings=Path.Combine(folder,"..","display-settings.json");using var display=new DisplayController(settings);var modes=new WindowsDisplayModes();var system=new WindowsDisplaySystem();var hdr=new WindowsHdrDisplay();using var intel=new IntelDisplayNative();
        var before=display.Read();var originalMode=before.Modes.Current;var originalSystem=system.Read();var originalHdr=hdr.Snapshot();int originalSync=intel.Read3D(9).Value??-1,originalScope=intel.Read3D(14).Value??-1,originalArc=BitConverter.ToInt32(intel.ReadArcSync(),8);var cases=new List<object>();bool restored=false;string? error=null;string? restoreError=null;
        if(originalMode==null||originalSystem.Orientation==null||!originalSystem.Scale.Available||originalSystem.Scale.Current==null){await File.WriteAllTextAsync(reportPath,JsonSerializer.Serialize(new{passed=false,error="Supported initial layout unavailable",hardwareWrites=0},Backend.Program.Json));return 1;}
        try
        {
            foreach(var size in new[]{(1680,1050),(1440,900),(1280,800),(1920,1200)})
            {
                if(!before.Modes.Modes.Any(value=>value.Width==size.Item1&&value.Height==size.Item2))continue;
                var actual=display.Set("resolution",JsonSerializer.SerializeToElement(size.Item1+"x"+size.Item2));if(actual.Modes.Current?.Width!=size.Item1||actual.Modes.Current.Height!=size.Item2||actual.Modes.Current.Hz!=originalMode.Hz)throw new IOException("Resolution/refresh was not acknowledged.");
                if(hdr.Snapshot()!=originalHdr)throw new IOException("Resolution changed HDR/calibration.");cases.Add(new{key="resolution",width=size.Item1,height=size.Item2,hz=actual.Modes.Current.Hz});
            }
            display.Set("resolution",JsonSerializer.SerializeToElement(originalMode.Width+"x"+originalMode.Height));if(modes.Read().Current?.Hz!=originalMode.Hz)display.Set("refreshRate",JsonSerializer.SerializeToElement(originalMode.Hz));
            int rotated=(originalSystem.Orientation.Value+1)%4;var portrait=display.Set("displayOrientation",JsonSerializer.SerializeToElement(rotated));
            if(system.Read().Orientation!=rotated||portrait.Modes.Current?.Width!=originalMode.Height||portrait.Modes.Current.Height!=originalMode.Width||portrait.Modes.Current.Hz!=originalMode.Hz)throw new IOException("Orientation did not apply with the original dimensions/refresh.");
            cases.Add(new{key="orientation",value=rotated});
            // A legacy landscape profile must respect the current global portrait orientation.
            modes.Set(originalMode);if(system.Read().Orientation!=rotated||modes.Read().Current?.Width!=originalMode.Height)throw new IOException("Profile resolution changed global orientation.");
            display.Set("displayOrientation",JsonSerializer.SerializeToElement(originalSystem.Orientation.Value));if(modes.Read().Current!=originalMode)throw new IOException("Orientation round trip did not restore the display mode.");
            int scale=originalSystem.Scale.Choices.Where(value=>value!=originalSystem.Scale.Current).OrderBy(value=>Math.Abs(value-originalSystem.Scale.Current.Value)).First();var scaled=display.Set("displayScale",JsonSerializer.SerializeToElement(scale));if(scaled.System?.Scale.Current!=scale||modes.Read().Current!=originalMode)throw new IOException("Windows UI scale was not applied independently.");cases.Add(new{key="windowsScale",value=scale});display.Set("displayScale",JsonSerializer.SerializeToElement(originalSystem.Scale.Current.Value));
            if(hdr.Snapshot()!=originalHdr||intel.Read3D(9).Value!=originalSync||intel.Read3D(14).Value!=originalScope||BitConverter.ToInt32(intel.ReadArcSync(),8)!=originalArc)throw new IOException("Layout operations changed HDR or unrelated Intel settings.");
        }
        catch(Exception failure){error=failure.Message;}
        finally
        {
            try
            {
                if(system.Read().Orientation!=originalSystem.Orientation)display.Set("displayOrientation",JsonSerializer.SerializeToElement(originalSystem.Orientation.Value));if(modes.Read().Current!=originalMode){display.Set("resolution",JsonSerializer.SerializeToElement(originalMode.Width+"x"+originalMode.Height));display.Set("refreshRate",JsonSerializer.SerializeToElement(originalMode.Hz));}
                if(system.Read().Scale.Current!=originalSystem.Scale.Current)display.Set("displayScale",JsonSerializer.SerializeToElement(originalSystem.Scale.Current.Value));if(hdr.Snapshot()!=originalHdr)hdr.Restore(originalHdr);
                restored=modes.Read().Current==originalMode&&system.Read().Orientation==originalSystem.Orientation&&system.Read().Scale.Current==originalSystem.Scale.Current&&hdr.Snapshot()==originalHdr&&intel.Read3D(9).Value==originalSync&&intel.Read3D(14).Value==originalScope&&BitConverter.ToInt32(intel.ReadArcSync(),8)==originalArc;
            }
            catch(Exception failure){restoreError=failure.Message;}
        }
        await File.WriteAllTextAsync(reportPath,JsonSerializer.Serialize(new{passed=error==null&&restored,restored,error,restoreError,cases,actualMode=modes.Read().Current,system=system.Read(),tdpWrites=0,profileWrites=0,physicalOwnerInputTested=false,kind="native-hardware-API"},Backend.Program.Json));return error==null&&restored?0:1;
    }
}
