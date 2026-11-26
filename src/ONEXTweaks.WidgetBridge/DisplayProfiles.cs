// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;

namespace ONEXTweaks.WidgetBridge;

internal sealed class DisplayProfiles:IAsyncDisposable
{
    private readonly ScopedProfileCatalog<DisplayPreferences> catalog;
    private readonly ProfileSessions sessions;
    private readonly DisplayController display;
    private readonly SemaphoreSlim gate;
    private readonly CancellationTokenSource stopping=new();
    private readonly Task worker;
    private readonly Action<Exception> log;
    internal static bool ImageSetting(string key)=>key is "saturation" or "hue" or "contrast" or "brightness" or "gamma" or "sharpness" or "reset" or "hdrEnabled" or "autoHdr" or "hdrCalibration" or "sdrBrightness" or "hdrReset";
    private static DisplayPreferences Image(DisplayPreferences value)=>new(){Colors=value.Colors,Sharpness=value.Sharpness,Hdr=value.Hdr};
    internal DisplayPreferences Capture()=>Image(display.Capture(includeHdr:true));
    internal DisplayProfiles(string folder,DisplayController display,SemaphoreSlim gate,ProfileSessions sessions,Action<Exception> log,Action<DisplayPreferences> applyImage,string? legacySelection=null)
    {
        this.display=display;this.gate=gate;this.sessions=sessions;this.log=log;
        string file=Path.Combine(folder,"..","screen-color-profiles.json");ScopedProfile<DisplayPreferences>? seed=null;var inherited=new List<ScopedProfile<DisplayPreferences>>();
        if(!File.Exists(file))
        {
            var live=Capture();string legacy=Path.Combine(folder,"..","performance-profiles.json");
            if(File.Exists(legacy))
            {
                var data=JsonSerializer.Deserialize<ProfileDocument>(File.ReadAllText(legacy),Backend.Program.Json)??throw new InvalidDataException("Existing Performance profiles are unreadable");
                DisplayPreferences Side(DisplayPreferences? value)=>value==null?live:Image(value) with{Hdr=live.Hdr};
                var ac=Side(data.Global.Ac.Display);var dc=Side(data.Global.Dc.Display);
                seed=new("global","Global Profile",null,data.Global.Split,ac,dc,DateTimeOffset.UtcNow);
                if(data.Flags.GetValueOrDefault("Display",true))foreach(var game in data.Games.Where(entry=>entry.Use&&(entry.Ac.Display!=null||entry.Dc.Display!=null)))
                {
                    var gameAc=Side(game.Ac.Display??data.Global.Ac.Display);var gameDc=Side(game.Dc.Display??data.Global.Dc.Display);
                    inherited.Add(new("legacy-"+game.Id,game.Name,game.Path,game.Split,gameAc,gameDc,game.ModifiedUtc));
                }
            }
        }
        catalog=new(file,Capture,applyImage,value=>value.Validate(),WindowsPowerSettings.IsOnAc,sessions.Current,ProfileSessions.Alive,seed,inherited,rememberManualFallback:true,sourceIndependent:true,initialIndependentId:legacySelection,needsInitialReapply:display.NeedsInitialProfileColorReapply);
        worker=Task.Run(Run);
    }
    internal bool Exists(string id)=>catalog.Find(id)!=null;
    internal string ActiveId=>catalog.ActiveId;
    internal string Read()=>catalog.Read();
    internal string Candidates()=>JsonSerializer.Serialize(sessions.Candidates(),Backend.Program.Json);
    internal void Reapply()=>catalog.Reapply();
    // A display mode change can invalidate the independent image preset.
    internal void ApplyAfterGraphicsChange()
    {
        try{display.ApplyProfile(catalog.CurrentValues);}
        catch(DisplayController.ProfileColorReadbackUnavailableException failure)
        {
            // This refusal precedes image writes. Keep the preset selected for
            // Screen Color to report without rolling back the power profile.
            catalog.ReportError(failure);
            log(failure);
        }
    }
    internal void Record()=>catalog.Record();
    internal void Mutate(JsonElement request)
    {
        var candidates=sessions.Candidates();
        catalog.Mutate(request,pid=>candidates.FirstOrDefault(candidate=>candidate.Session.ProcessId==pid)?.Session);
    }
    private async Task Run()
    {
        try
        {
            while(!stopping.IsCancellationRequested)
            {
                await gate.WaitAsync(stopping.Token);
                try{catalog.Tick();}
                catch(Exception failure){catalog.ReportError(failure);log(failure);}
                finally{gate.Release();}
                await Task.Delay(650,stopping.Token);
            }
        }
        catch(OperationCanceledException){}
    }
    public async ValueTask DisposeAsync(){stopping.Cancel();await worker;stopping.Dispose();}
}
