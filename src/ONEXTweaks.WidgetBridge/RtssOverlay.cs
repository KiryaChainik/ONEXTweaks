// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Text.Json;
using ONEXTweaks.Backend;
using ONEXTweaks.Panel;

namespace ONEXTweaks.WidgetBridge;

internal sealed record OverlayPreferences(int Level=0,string Config="");
internal sealed record OverlaySample(WindowsMetrics Windows,PowerState? Power,DeviceMetrics? Gpu,double? FanPercent,IReadOnlyList<uint>? CpuClocksMhz=null,double? GpuMemoryGb=null,RtssApp? Application=null);
internal sealed record OverlayStatus(bool Installed,bool Running,bool Available,int Level,int Slot,string? Error,string? Text,uint? Frame,IReadOnlyList<RtssApp> Applications);

internal sealed class RtssOverlay:IDisposable
{
    private readonly string settingsPath,reports;
    private readonly Func<OverlaySample> sample;
    private readonly object gate=new();
    private readonly CancellationTokenSource stopping=new();
    private readonly Task loop;
    private OverlayPreferences preferences;
    private OverlayStatus status;
    private long nextStart;
    internal static string? DirectoryPath
    {
        get
        {
            foreach(string folder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)})
            {string candidate=Path.Combine(folder,"RivaTuner Statistics Server");if(File.Exists(Path.Combine(candidate,"RTSS.exe")))return candidate;}
            return null;
        }
    }
    internal RtssOverlay(string folder,Func<OverlaySample> sample)
    {
        this.sample=sample;reports=folder;settingsPath=Path.Combine(folder,"..","overlay-preferences.json");
        preferences=new();try{if(File.Exists(settingsPath))preferences=JsonSerializer.Deserialize<OverlayPreferences>(File.ReadAllText(settingsPath),Backend.Program.Json)??new();Validate(preferences);}catch(Exception ex)when(ex is JsonException or IOException or InvalidDataException){preferences=new();File.WriteAllText(Path.Combine(folder,"overlay-preferences-error.txt"),ex.Message);}
        status=new(DirectoryPath!=null,false,false,preferences.Level,-1,null,null,null,[]);
        loop=Task.Run(async()=>{while(!stopping.IsCancellationRequested){try{Refresh();}catch(Exception ex){lock(gate)status=status with{Available=false,Error=ex.Message};}try{await Task.Delay(500,stopping.Token);}catch(OperationCanceledException){break;}}});
    }
    internal OverlayStatus Read(){lock(gate)return status;}
    internal OverlayPreferences Preferences{get{lock(gate)return preferences;}}
    internal OverlayStatus SetLevel(int level)
    {
        lock(gate)
        {
            var next=preferences with{Level=level};Validate(next);
            if(level>0){EnsureRunning();RtssProfile.EnableOsd();}
            var old=preferences;preferences=next;
            try{Refresh();if(level>0&&!status.Available)throw new IOException(status.Error);Save();return status;}
            catch{preferences=old;Refresh();throw;}
        }
    }
    internal OverlayStatus SetConfig(string config)
    {
        lock(gate){var next=preferences with{Config=config};Validate(next);var old=preferences;preferences=next;try{RtssProfile.Configure(Parse(config));Refresh();Save();return status;}catch{preferences=old;Refresh();throw;}}
    }
    internal static void ValidatePreset(OverlayPreferences value)=>Validate(value);
    internal void SetPreferences(OverlayPreferences value)
    {
        lock(gate)
        {
            Validate(value);if(value.Level>0){EnsureRunning();RtssProfile.EnableOsd();}var old=preferences;preferences=value;
            try{RtssProfile.Configure(Parse(value.Config));Refresh();if(value.Level>0&&!status.Available)throw new IOException(status.Error);Save();}
            catch{preferences=old;try{RtssProfile.Configure(Parse(old.Config));Refresh();Save();}catch(Exception recovery){try{File.WriteAllText(Path.Combine(reports,"overlay-preset-rollback-error.txt"),recovery.Message);}catch(IOException){}}throw;}
        }
    }
    private void Save(){string temp=settingsPath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(preferences,Backend.Program.Json));File.Move(temp,settingsPath,true);}
    private static void Validate(OverlayPreferences value)
    {
        if(value.Level is <0 or >4||value.Config.Length>16000||value.Config.Contains('\0'))throw new InvalidDataException("Invalid overlay settings");
        // Reject malformed drafts before changing either the native profile or saved preferences.
        for(int level=1;level<=4;level++)OverlayFormatting.Preview(level,value.Config);
    }
    internal static bool IsRunning(){foreach(var process in Process.GetProcessesByName("RTSS")){process.Dispose();return true;}return false;}
    internal static void EnsureRunning()
    {
        string directory=DirectoryPath??throw new FileNotFoundException("Install RivaTuner Statistics Server first");
        if(!IsRunning())
        {
            // The one-purpose elevated task is installed once; the widget/controller stay unelevated.
            using var task=Process.Start(new ProcessStartInfo("schtasks.exe"){ArgumentList={"/Run","/TN","ONEXTweaks-RTSS"},UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true});
            if(task==null||!task.WaitForExit(3000)||task.ExitCode!=0)
                using(Process.Start(new ProcessStartInfo(Path.Combine(directory,"RTSS.exe")){WorkingDirectory=directory,UseShellExecute=true,WindowStyle=ProcessWindowStyle.Minimized})){}
        }
        for(int i=0;i<50;i++){try{RtssMemory.Write("",false,true);return;}catch(FileNotFoundException){Thread.Sleep(100);}}
        throw new IOException("RTSS did not create its shared memory");
    }
    private void Refresh()
    {
        lock(gate)
        {
            bool installed=DirectoryPath!=null,running=IsRunning();
            if(installed&&!running&&preferences.Level>0&&Environment.TickCount64>=nextStart)
            {nextStart=Environment.TickCount64+10000;try{EnsureRunning();RtssProfile.EnableOsd();RtssProfile.Configure(Parse(preferences.Config));running=true;}catch(Exception ex){status=new(installed,false,false,preferences.Level,-1,ex.Message,null,null,[]);return;}}
            if(!installed||!running){status=new(installed,running,installed,preferences.Level,-1,preferences.Level>0?"RTSS is not running":null,null,null,[]);return;}
            try
            {
                var config=Parse(preferences.Config);var app=status.Applications.LastOrDefault(a=>a.Fps>0&&(a.Flags&0xffff)!=0);
                var result=Build(preferences.Level,config,sample() with{Application=app});
                var written=RtssMemory.Write(result.Text,result.Graph,preferences.Level==0);
                status=new(installed,true,true,preferences.Level,written.Slot,null,written.Text,written.Frame,written.Applications);
                File.WriteAllText(Path.Combine(reports,"rtss-overlay.json"),JsonSerializer.Serialize(status,Backend.Program.Json));
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {status=new(installed,running,false,preferences.Level,-1,ex.Message,null,null,[]);}
        }
    }
    internal static Dictionary<string,string> Parse(string config)=>OverlayFormatting.Parse(config);
    internal static (string Text,bool Graph) Build(int level,IReadOnlyDictionary<string,string> config,OverlaySample sample)
    {
        var win=sample.Windows;var power=sample.Power;var gpu=sample.Gpu;var stats=sample.Application?.Statistics;
        return OverlayFormatting.Build(level,config,new OverlayValues{CpuLoadPercent=win.CpuLoadPercent,CpuTemperatureC=power?.CpuTemperatureC,
            MemoryUsedGb=win.MemoryUsedBytes/1073741824.0,MemoryTotalGb=win.MemoryTotalBytes/1073741824.0,
            GpuLoadPercent=gpu?.GpuLoadPercent,GpuTemperatureC=gpu?.GpuTemperatureC,GpuPowerWatts=gpu?.GpuPowerWatts,
            ApuPowerWatts=power?.ApuPowerWatts,Pl1Watts=power?.Pl1Watts,Pl2Watts=power?.Pl2Watts,FanPercent=sample.FanPercent,
            CpuClocksMhz=sample.CpuClocksMhz,GpuClockMhz=gpu?.GpuClockMhz,GpuMemoryGb=sample.GpuMemoryGb,BatteryPercent=win.BatteryPercent,
            OnAcPower=win.OnAcPower,Statistics=stats==null?null:new OverlayValueStatistics(stats.Minimum,stats.Average,stats.Maximum,stats.OnePercentLow),Clock=DateTime.Now});
    }
    internal OverlayPreview Preview(int level,string config)
    {
        int zoom=2,height=9;string color="FFFFFF";
        if(IsRunning())try
        {
            zoom=(int)BitConverter.ToUInt32(RtssProfile.Read("ZoomRatio"),0);
            height=(int)Math.Min(96,Math.Abs((long)BitConverter.ToInt32(RtssProfile.Read("FontHeight"),0)));
            uint raw=BitConverter.ToUInt32(RtssProfile.Read("BaseColor"),0);color=((raw&255)<<16|(raw&0xff00)|(raw>>16&255)).ToString("X6");
        }catch(IOException){}catch(UnauthorizedAccessException){}
        return OverlayFormatting.Preview(level,config,zoom,height,color);
    }
    public void Dispose(){stopping.Cancel();try{loop.Wait(1500);}catch(AggregateException){}try{RtssMemory.Write("",false,true);}catch(IOException){}catch(UnauthorizedAccessException){}stopping.Dispose();}
}
