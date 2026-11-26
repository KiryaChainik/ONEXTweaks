// SPDX-License-Identifier: AGPL-3.0-only
// Isolated Windows CCD DPI packets. The -3/-4 extension is capability-probed;
// unavailable/unknown layouts are rejected. No registry writes or logoff guessing.
// ABI reference: github.com/lihas/windows-DPI-scaling-sample, DPIHelper/DpiHelper.h.
using System.Runtime.InteropServices;
namespace ONEXTweaks.WidgetBridge;
internal sealed record DisplayScaleState(bool Available,int? Current,int? Recommended,int[] Choices,string? Error=null);
internal sealed record DisplaySystemState(bool OrientationAvailable,int? Orientation,DisplayScaleState Scale,string? Error=null);
internal sealed class WindowsDisplaySystem
{
    [StructLayout(LayoutKind.Sequential)]private struct DpiGet
    {internal uint Type,Size,AdapterLow;internal int AdapterHigh;internal uint Source;internal int Minimum,Current,Maximum;}
    [StructLayout(LayoutKind.Sequential)]private struct DpiSet
    {internal uint Type,Size,AdapterLow;internal int AdapterHigh;internal uint Source;internal int Relative;}
    [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo")]private static extern int GetDpi(ref DpiGet value);
    [DllImport("user32.dll",EntryPoint="DisplayConfigSetDeviceInfo")]private static extern int SetDpi(ref DpiSet value);
    private static DpiGet Dpi()
    {
        if(Marshal.SizeOf<DpiGet>()!=32||Marshal.SizeOf<DpiSet>()!=24)throw new IOException("Unsupported Windows DPI packet layout.");
        var source=WindowsHdrDisplay.ActiveSourceForDisplaySettings();var value=new DpiGet{Type=unchecked((uint)-3),Size=32,AdapterLow=source.Low,AdapterHigh=source.High,Source=source.Source};
        int result=GetDpi(ref value);if(result!=0)throw new IOException("Windows DPI scale is unavailable ("+result+").");return value;
    }
    internal DisplaySystemState Read()
    {
        int? orientation=null;string? error=null;try{orientation=new WindowsDisplayModes().ReadOrientation();if(orientation is <0 or >3)throw new IOException("Windows reported an unknown orientation.");}catch(Exception failure){orientation=null;error=failure.Message;}
        DisplayScaleState scale;
        try{var dpi=Dpi();var values=DisplayScalePolicy.Decode(dpi.Minimum,dpi.Current,dpi.Maximum);scale=new(true,values.Current,values.Recommended,values.Choices);}
        catch(Exception failure){scale=new(false,null,null,[],failure.Message);}
        return new(orientation!=null,orientation,scale,error);
    }
    internal DisplaySystemState SetScale(int percent)
    {
        var before=Dpi();int relative=DisplayScalePolicy.Relative(percent,before.Minimum,before.Current,before.Maximum);if(relative==before.Current)return Read();
        DpiSet Packet(int target)=>new(){Type=unchecked((uint)-4),Size=24,AdapterLow=before.AdapterLow,AdapterHigh=before.AdapterHigh,Source=before.Source,Relative=target};
        void Write(int target){var value=Packet(target);int result=SetDpi(ref value);if(result!=0)throw new IOException("Windows did not apply the display scale ("+result+").");}
        bool SameSource(DpiGet current)=>current.AdapterLow==before.AdapterLow&&current.AdapterHigh==before.AdapterHigh&&current.Source==before.Source;
        try
        {
            Write(relative);
            for(int attempt=0;attempt<40;attempt++){var actual=Dpi();if(!SameSource(actual))throw new IOException("Display topology changed while applying scale.");if(DisplayScalePolicy.Decode(actual.Minimum,actual.Current,actual.Maximum).Current==percent)return Read();Thread.Sleep(50);}
            throw new IOException("Windows did not confirm the requested scale; no sign-out/restart was assumed.");
        }
        catch
        {
            var actual=Dpi();if(!SameSource(actual))throw new IOException("Display source changed; a different monitor will not be adjusted.");if(actual.Current!=before.Current)Write(before.Current);
            var restored=Dpi();if(!SameSource(restored)||restored.Current!=before.Current)throw new IOException("Windows could not restore the fresh previous display scale.");throw;
        }
    }
    internal DisplaySystemState SetOrientation(int orientation){new WindowsDisplayModes().SetOrientation(orientation);return Read();}

}
