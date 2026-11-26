// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ONEXTweaks.WidgetBridge;
internal static class AwakeNativeVerification
{
    private sealed class Clock:TimeProvider
    {
        private long ticks;public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>ticks;
        internal void Advance(int seconds)=>ticks+=TimeSpan.FromSeconds(seconds).Ticks;
    }
    private sealed class Platform(NativeAwakePlatform actual):IAwakePlatform
    {
        internal int? Battery=80;public int? BatteryPercent=>Battery;
        public IDisposable Hold(bool displayRequired)=>actual.Hold(displayRequired);
        public IDisposable SubscribeSuspend(Action stop)=>actual.SubscribeSuspend(stop);
        public IDisposable DimBrightness()=>actual.DimBrightness();public Task Shutdown()=>actual.Shutdown();
    }
    internal static async Task<int> Run(string reportPath)
    {
        string reason="ONEXTweaks Amphetamine QA "+Guid.NewGuid().ToString("N");
        string folder=Path.Combine(Path.GetDirectoryName(reportPath)!,"..","privateReports","awake-qa-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var result=new Dictionary<string,object?>{{"passed",false},{"actualWindowsPowerRequests",true},{"physicalSuspendTested",false},{"actualScreenOffTested",false},{"shutdownExecuted",false}};
        int screenPlans=0,dimHeld=0,shutdownPlans=0;AwakeController? controller=null;
        void Check(bool ok,string message){if(!ok)throw new IOException(message);}
        async Task<int> Requests()
        {
            var start=new ProcessStartInfo("powercfg.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add("/requests");
            using var process=Process.Start(start)??throw new IOException("Powercfg did not start");string text=await process.StandardOutput.ReadToEndAsync();string error=await process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
            if(process.ExitCode!=0)throw new IOException("Powercfg /requests failed: "+process.ExitCode);
            return Regex.Matches(text,Regex.Escape(reason)).Count;
        }
        try
        {
            var native=new NativeAwakePlatform(reason:reason,dim:()=>{screenPlans++;dimHeld++;return new Cleanup(()=>dimHeld--);},shutdown:()=>{shutdownPlans++;return Task.CompletedTask;});
            if(native.BatteryPercent is <=10)throw new IOException("Native keep-awake QA skipped at low battery");
            var platform=new Platform(native);var time=new Clock();controller=new AwakeController(Path.Combine(folder,"settings.json"),platform,time,false);
            Check(controller.Read().Available,"Native suspend notification registration failed");Check(await Requests()==0,"Unexpected initial QA request");
            controller.Set(true,new(1));int display=await Requests();Check(display==3,"System/display/execution requests were not all visible in Windows");
            time.Advance(20);Check(controller.Read().RemainingSeconds==40,"Remaining seconds mismatch");
            platform.Battery=10;controller.Tick();Check(!controller.Read().On&&await Requests()==0,"Low battery did not release actual requests");
            platform.Battery=80;controller.Set(true,new(1,true,true));int dimmedDisplay=await Requests();Check(dimmedDisplay==3&&screenPlans==1&&dimHeld==1,"Dim mode request/action plan mismatch");
            time.Advance(60);controller.Tick();Check(!controller.Read().On&&await Requests()==0&&shutdownPlans==1&&dimHeld==0,"Expiry did not release actual requests/dim ownership/select shutdown plan");
            controller.Set(true,new());controller.Suspend();Check(!controller.Read().On&&await Requests()==0,"Suspend handler did not release actual requests");
            controller.Set(true,new());controller.Dispose();controller=null;Check(await Requests()==0,"Dispose leaked power requests");
            result["passed"]=true;result["visibleDisplaySystemExecutionRequests"]=display;result["visibleRequestsWithBrightnessDim"]=dimmedDisplay;
            result["nativeSuspendRegistration"]=true;result["syntheticLowBatteryAndSuspendEvents"]=true;result["expiryClockAdvancedByFixture"]=true;
            result["screenOffPlans"]=screenPlans;result["shutdownPlans"]=shutdownPlans;result["allRequestsReleased"]=true;return 0;
        }
        catch(Exception error){result["error"]=error.ToString();return 1;}
        finally{controller?.Dispose();Directory.Delete(folder,true);await File.WriteAllTextAsync(reportPath,JsonSerializer.Serialize(result,Backend.Program.Json));}
    }
    private sealed class Cleanup(Action action):IDisposable
    {private Action? clean=action;public void Dispose()=>Interlocked.Exchange(ref clean,null)?.Invoke();}
}
