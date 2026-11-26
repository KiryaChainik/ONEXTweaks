using ONEXTweaks.WidgetBridge;
using System.Text.Json;
var checks=new List<string>();string directory=Path.Combine(Path.GetTempPath(),"onex-awake-core-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
void Check(bool value,string message){if(!value)throw new Exception(message);checks.Add(message);}
void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Invalid settings accepted");}
try
{
    var platform=new Platform();var time=new Clock();string path=Path.Combine(directory,"preferences.json");
    using(var awake=new AwakeController(path,platform,time,false))
    {
        Check(!awake.Read().On&&platform.Held==0,"default Off without a lease");
        awake.Set(true,new());time.Advance(TimeSpan.FromDays(1));awake.Tick();
        Check(awake.Read().On&&awake.Read().RemainingSeconds==null&&platform.Display,"indefinite session holds system/display and has no deadline");
        awake.Set(false,new());Check(platform.Held==0,"tap Off releases request");
        awake.Set(true,new(75));time.Advance(TimeSpan.FromSeconds(61));
        Check(awake.Read().RemainingSeconds==4439,"timer uses exact remaining seconds");
        time.WallOffset=TimeSpan.FromDays(3);Check(awake.Read().RemainingSeconds==4439,"wall-clock jump does not change elapsed deadline");
        awake.Set(true,new(1,true,true));Check(platform.Held==1&&platform.Display&&platform.DimStarts==1&&platform.DimHeld==1,"dim session still holds display/system and owns one brightness dim");
        awake.Set(true,new(1,true,true));Check(platform.DimStarts==2&&platform.DimHeld==1,"configured reactivation reapplies options without leaking a dim lease");
        platform.Battery=10;time.Advance(TimeSpan.FromMinutes(1));awake.Tick();
        Check(!awake.Read().On&&platform.Held==0&&platform.DimHeld==0&&platform.Shutdowns==0&&awake.Read().StopReason=="LowBattery","10percent cutoff restores brightness and takes precedence over timed shutdown");
        try{awake.Set(true,new());throw new Exception("Low battery start accepted");}catch(InvalidOperationException){}
        Check(!awake.Read().On&&platform.Held==0,"cannot start at10percent");
        platform.Battery=11;awake.Set(true,new(1,false,true));platform.Notify!();time.Advance(TimeSpan.FromMinutes(1));awake.Tick();
        Check(!awake.Read().On&&platform.Held==0&&platform.Shutdowns==0,"manual suspend cancels the session and shutdown timer");
        awake.Set(true,new(1,false,true));time.Advance(TimeSpan.FromSeconds(60));awake.Tick();awake.Tick();
        Check(!awake.Read().On&&platform.Held==0&&platform.Shutdowns==1,"deadline releases requests and schedules shutdown once");
        awake.Set(true,new(1));awake.Set(false,new(1));time.Advance(TimeSpan.FromHours(3));awake.Tick();
        Check(platform.Shutdowns==1&&!awake.Read().On,"explicit Off cancels expiry action");
        awake.Set(true,new(1,true));awake.Set(true,new(1,false));Check(platform.DimHeld==0&&awake.Read().On,"unchecking brightness restores it without stopping keep-awake");
        awake.Set(true,new(1,true));time.Advance(TimeSpan.FromMinutes(1));awake.Tick();Check(platform.DimHeld==0&&!awake.Read().On,"timer expiry releases brightness ownership");
        awake.Set(true,new(1,true));platform.Notify!();Check(platform.DimHeld==0&&!awake.Read().On,"manual sleep releases brightness ownership");
        Reject(()=>awake.Set(true,new(0,false,true)));Reject(()=>awake.Set(true,new(-1)));Reject(()=>awake.Set(true,new(6000)));
        Check(platform.Held==0,"invalid duration and shutdown-without-timer leave no requests");
        var configured=new AwakePreferences(1,true,true);awake.Set(false,configured);
        string saved=File.ReadAllText(path);int dimStarts=platform.DimStarts,shutdowns=platform.Shutdowns;
        awake.SetSimple(true);time.Advance(TimeSpan.FromMinutes(2));awake.Tick();
        Check(awake.Read().On&&awake.Read().RemainingSeconds==null&&awake.Read().ActiveSettings==new AwakePreferences(),"plain activation ignores saved timer/dim/shutdown settings");
        Check(platform.DimStarts==dimStarts&&platform.Shutdowns==shutdowns,"plain activation neither dims nor triggers saved timed shutdown");
        Check(awake.Read().Settings==configured&&File.ReadAllText(path)==saved,"plain activation preserves saved menu preferences exactly");
        awake.SetSimple(false);Check(!awake.Read().On&&awake.Read().Settings==configured&&File.ReadAllText(path)==saved,"plain Off stops session without overwriting configured preferences");
        awake.Set(true,configured);Check(awake.Read().On&&awake.Read().RemainingSeconds==60&&awake.Read().ActiveSettings==configured,"configured activation starts from Off with selected options");
        time.Advance(TimeSpan.FromSeconds(20));awake.Set(true,configured);
        Check(awake.Read().RemainingSeconds==60&&platform.DimHeld==1,"configured reactivation restarts timer and retains exactly one dim lease");
        awake.SetSimple(false);time.Advance(TimeSpan.FromMinutes(2));awake.Tick();
        Check(platform.Shutdowns==shutdowns&&platform.DimHeld==0,"plain Off cancels configured shutdown and releases dim ownership");
        platform.Battery=null;awake.Set(true,new(5999));Check(awake.Read().On,"unknown battery percentage is not fabricated as0percent");
    }
    Check(platform.Held==0&&platform.Notify==null,"dispose releases request and suspend registration");
    using(var restarted=new AwakeController(path,platform,time,false))
        Check(!restarted.Read().On&&restarted.Read().Settings.DurationMinutes==5999,"settings persist while helper restart starts Off");
    Console.WriteLine(JsonSerializer.Serialize(new{passed=true,actualWindowsHardware=false,shutdownExecuted=false,checks},Backend.Program.Json));
}
finally{Directory.Delete(directory,true);}
sealed class Clock:TimeProvider
{
    private long ticks;internal TimeSpan WallOffset;
    public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
    public override long GetTimestamp()=>ticks;
    public override DateTimeOffset GetUtcNow()=>DateTimeOffset.UnixEpoch+TimeSpan.FromTicks(ticks)+WallOffset;
    internal void Advance(TimeSpan value)=>ticks+=value.Ticks;
}
sealed class Platform:IAwakePlatform
{
    internal int? Battery=80;internal int Held,Shutdowns,DimStarts,DimHeld;internal bool Display;internal Action? Notify;
    public int? BatteryPercent=>Battery;
    public IDisposable Hold(bool displayRequired){Held++;Display=displayRequired;return new Cleanup(()=>Held--);}
    public IDisposable SubscribeSuspend(Action stop){Notify=stop;return new Cleanup(()=>Notify=null);}
    public IDisposable DimBrightness(){DimStarts++;DimHeld++;return new Cleanup(()=>DimHeld--);}
    public Task Shutdown(){Shutdowns++;return Task.CompletedTask;}
}
sealed class Cleanup(Action clean):IDisposable{private bool done;public void Dispose(){if(!done){done=true;clean();}}}
namespace Backend{internal static class Program{internal static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);}}
