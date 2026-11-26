// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal sealed record AwakePreferences(int DurationMinutes=0,bool ScreenOff=false,bool ShutdownAtEnd=false)
{
    internal void Validate()
    {
        if(DurationMinutes is <0 or >5999)throw new ArgumentOutOfRangeException(nameof(DurationMinutes));
        if(ShutdownAtEnd&&DurationMinutes==0)throw new ArgumentException("Для выключения консоли нужен таймер.");
    }
}
internal sealed record AwakeState(bool Available,bool On,AwakePreferences Settings,int? RemainingSeconds,
    bool RequestsHeld,string? StopReason,string? Error,AwakePreferences? ActiveSettings);
internal sealed record AwakeCommand(bool Enabled,AwakePreferences? Settings,bool Simple=false);
internal interface IAwakePlatform
{
    int? BatteryPercent {get;}
    IDisposable Hold(bool displayRequired);
    IDisposable SubscribeSuspend(Action stop);
    IDisposable DimBrightness();
    Task Shutdown();
}
internal sealed class AwakeController:IDisposable
{
    private readonly object gate=new();private readonly string path;private readonly IAwakePlatform platform;
    private readonly TimeProvider clock;private readonly Timer? timer;private readonly IDisposable? notifications;
    private IDisposable? lease,dimLease;private AwakePreferences settings=new(),session=new();private bool available=true,on,disposed;
    private long started;private string? reason,error;
    internal AwakeController(string path,IAwakePlatform platform,TimeProvider? clock=null,bool startTimer=true)
    {
        this.path=path;this.platform=platform;this.clock=clock??TimeProvider.System;
        try{if(File.Exists(path)){settings=JsonSerializer.Deserialize<AwakePreferences>(File.ReadAllText(path),Backend.Program.Json)??new();settings.Validate();}}
        catch(Exception e)when(e is IOException or JsonException or ArgumentException){settings=new();error="Настройки таймера сброшены: "+e.Message;}
        try{notifications=platform.SubscribeSuspend(Suspend);}
        catch(Exception e){available=false;error=e.Message;}
        if(startTimer)timer=new Timer(_=>Tick(),null,1000,1000);
    }
    internal AwakeState Read(){lock(gate)return Snapshot();}
    private AwakeState Snapshot()=>new(available,on,settings,on&&session.DurationMinutes>0?
        (int?)Math.Max(0,Math.Ceiling(session.DurationMinutes*60-clock.GetElapsedTime(started).TotalSeconds)):null,lease!=null,reason,error,on?session:null);
    internal AwakeState SetSimple(bool enabled)=>Set(enabled,new(),false);
    internal AwakeState Set(bool enabled,AwakePreferences next,bool persistSettings=true)
    {
        next.Validate();lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);if(!available)throw new IOException(error);
            if(enabled&&platform.BatteryPercent is <=10)throw new InvalidOperationException("При заряде 10% или ниже Amphetamine отключён.");
            IDisposable? replacement=enabled?platform.Hold(true):null;
            try
            {
                if(persistSettings)
                {
                    string temporary=path+".tmp";Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(temporary,JsonSerializer.Serialize(next,Backend.Program.Json));File.Move(temporary,path,true);
                }
            }
            catch{replacement?.Dispose();throw;}
            lease?.Dispose();lease=replacement;if(persistSettings)settings=next;session=next;on=enabled;started=clock.GetTimestamp();reason=enabled?null:"Off";error=null;
            try
            {
                var replacementDim=enabled&&next.ScreenOff?platform.DimBrightness():null;
                var previous=dimLease;dimLease=replacementDim;previous?.Dispose();
            }
            catch(Exception e){Stop("BrightnessError");error=e.Message;throw;}
            return Snapshot();
        }
    }
    internal void Suspend(){lock(gate)if(!disposed)Stop("Sleep");}
    private void Stop(string why)
    {
        lease?.Dispose();lease=null;on=false;reason=why;
        var previous=dimLease;dimLease=null;
        try{previous?.Dispose();}catch(Exception e){error="Не удалось вернуть яркость: "+e.Message;}
    }
    internal void Tick()
    {
        Task? shutdown=null;lock(gate)
        {
            if(disposed||!on)return;
            try
            {
                if(platform.BatteryPercent is <=10){Stop("LowBattery");return;}
                if(session.DurationMinutes>0&&clock.GetElapsedTime(started)>=TimeSpan.FromMinutes(session.DurationMinutes))
                {
                    bool poweroff=session.ShutdownAtEnd;Stop("Timer");
                    if(poweroff)shutdown=platform.Shutdown();
                }
            }
            catch(Exception e){Stop("Error");error=e.Message;}
        }
        if(shutdown!=null)_=ObserveShutdown(shutdown);
    }
    private async Task ObserveShutdown(Task action){try{await action;}catch(Exception e){lock(gate)error="Не удалось выключить консоль: "+e.Message;}}
    public void Dispose()
    {
        lock(gate){if(disposed)return;disposed=true;Stop("HelperStopped");}
        timer?.Dispose();notifications?.Dispose();
    }
}
