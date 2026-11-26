// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;

internal sealed record FpsState(string Provider,bool Enabled,int Value,IntelFpsState Intel,bool RtssAvailable,int RtssValue,string? Error=null);
internal sealed class FrameLimiter:IDisposable
{
    private readonly object gate=new();private readonly IntelFrameLimit intel=new();private readonly string file;private FpsPreferences preferences=new();private string? error;
    internal FrameLimiter(string folder)
    {
        file=Path.Combine(folder,"..","fps-preferences.json");
        if(File.Exists(file))try{var saved=JsonSerializer.Deserialize<FpsPreferences>(File.ReadAllText(file))??throw new InvalidDataException("Invalid FPS preferences");Change(saved,false);}catch(Exception ex){error="FPS restore: "+ex.Message;}
    }
    internal FpsState Read(){lock(gate){int rtss=0;bool available=RtssOverlay.DirectoryPath!=null;try{if(available)rtss=BitConverter.ToInt32(RtssProfile.Read("FramerateLimit"));}catch(Exception ex){available=false;error=ex.Message;}return new(preferences.Provider,preferences.Enabled,preferences.Value,intel.Read(),available,rtss,error);}}
    internal FpsState Set(FpsPreferences value){lock(gate){Change(value,true);return Read();}}
    private void Change(FpsPreferences next,bool save)
    {
        if(next.Provider is not ("Intel" or "RTSS")||next.Value is <30 or >300)throw new InvalidDataException("FPS must be 30–300, provider Intel or RTSS");
        var before=intel.Read();bool hasRtss=RtssOverlay.DirectoryPath!=null;int oldRtss=hasRtss?BitConverter.ToInt32(RtssProfile.Read("FramerateLimit")):0;
        if(next.Provider=="Intel"&&next.Enabled&&!before.Available)throw new IOException(before.Error);
        if(next.Provider=="RTSS"&&next.Enabled&&!hasRtss)throw new IOException("RTSS is not installed");
        void Rtss(int fps){if(hasRtss)RtssProfile.Apply(new Dictionary<string,byte[]>{{"FramerateLimit",BitConverter.GetBytes(fps)}});}
        try
        {
            // Disable the other provider before enabling the requested one; never stack caps.
            if(next.Provider=="Intel"){Rtss(0);if(before.Available)intel.Set(next.Enabled,next.Value);}
            else{if(before.Available)intel.Set(false,before.Value);if(next.Enabled)RtssOverlay.EnsureRunning();Rtss(next.Enabled?next.Value:0);}
            if(save){string temp=file+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(next));File.Move(temp,file,true);}
            preferences=next;error=null;
        }
        catch(Exception ex)
        {
            var rollback=new List<string>();try{if(before.Available)intel.Set(before.Enabled,before.Value);}catch(Exception e){rollback.Add(e.Message);}try{Rtss(oldRtss);}catch(Exception e){rollback.Add(e.Message);}error=ex.Message+(rollback.Count>0?"; restore: "+string.Join("; ",rollback):"");throw new IOException(error,ex);
        }
    }
    public void Dispose()=>intel.Dispose();
}
