// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using System.Text.Json.Nodes;
using ONEXTweaks.Fans;
namespace ONEXTweaks.WidgetBridge;
internal static class FanPreferencesVerification
{
    internal static async Task<int> Run(string report)
    {
        var checks=new List<string>();string? error=null;
        void Check(bool ok,string name){if(!ok)throw new IOException(name);checks.Add(name);}
        void Reject(FanPreferences value,string name){bool rejected=false;try{value.Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,name);}
        try
        {
            foreach(int mode in Enumerable.Range(0,3)){var value=JsonSerializer.Deserialize<FanPreferences>("{\"mode\":"+mode+"}",Backend.Program.Json)!;value.Validate();Check(value.Mode==mode&&value.Curve==null,"Legacy mode"+mode+" remains readable");}
            var curve=new FanCurve(20,40,60,80,100);var custom=new FanPreferences(3,curve);custom.Validate();
            Check(JsonSerializer.Deserialize<FanPreferences>(JsonSerializer.Serialize(custom,Backend.Program.Json),Backend.Program.Json)==custom,"Custom curve survives JSON and has value equality");
            Check(new FanPreferences(0,curve).Curve==curve,"Disabled custom curve can retain its settings");
            Reject(new(3),"Custom mode requires a curve");Reject(new(4),"Unknown mode rejected");Reject(new(3,new(10,40,60,80,100)),"Unsafe low duty rejected");Reject(new(3,new(20,40,60,80,101)),"Over100 duty rejected");Reject(new(3,new(20,60,40,80,100)),"Falling duty rejected");
            var captured=FanPreferences.Capture(JsonNode.Parse("{\"profile\":3,\"customCurve\":[20,40,60,80,100]}"));
            Check(captured.Mode==3&&captured.Curve!.Points().SequenceEqual(curve.Points()),"Legacy hardware response retains all points and defaults");
            var legacy=JsonSerializer.Deserialize<FanPreferences>("{\"mode\":3,\"curve\":{\"at45\":31,\"at55\":40,\"at65\":50,\"at75\":60,\"at85\":70}}",Backend.Program.Json)!;
            legacy.Validate();
            Check(legacy.Curve!.Points().SequenceEqual(new[]{31,31,40,50,60,70,100}),"Legacy saved JSON preserves the five setpoints and prior endpoints");
            var extended=new FanPreferences(3,new FanCurve(20,25,30,35,40,15,50));
            extended.Validate();
            var saved=JsonSerializer.Deserialize<FanPreferences>(JsonSerializer.Serialize(extended,Backend.Program.Json),Backend.Program.Json)!;
            Check(saved==extended,"Both new slider values survive reopening the saved profile");
            Check(FanCurvePolicy.PercentAt(20,saved.Curve!.Points())==15&&FanCurvePolicy.PercentAt(25,saved.Curve.Points())==15&&
                FanCurvePolicy.PercentAt(95,saved.Curve.Points())==50&&FanCurvePolicy.PercentAt(97.5,saved.Curve.Points())==75&&
                FanCurvePolicy.PercentAt(100,saved.Curve.Points())==100,"Minimums and full-speed limit apply at the correct temperatures");
            Reject(new(3,new FanCurve(20,25,30,35,40,14,50)),"25 C refuses 14 percent");
            Reject(new(3,new FanCurve(20,25,30,35,40,15,49)),"95 C refuses 49 percent");
            Check(FanCurvePolicy.PercentAt(44,legacy.Curve.Points())==31&&FanCurvePolicy.PercentAt(90,legacy.Curve.Points())==85,"Legacy interpolation remains unchanged");
            Check(FanPreferences.Capture(JsonNode.Parse("{\"profile\":0,\"customCurve\":[20,40,60,80,100]}"))==new FanPreferences(0),"Auto response is still Auto with previous custom memory");
            var manual=JsonNode.Parse("{\"available\":true,\"profile\":3,\"ecControl\":1,\"percent\":20,\"customCurve\":[20,40,60,80,100]}");
            Check(!Program.CanReuseFanState(manual,custom),"A matching saved curve and manual bit cannot bypass live duty verification");
            manual!["percent"]=80;
            Check(!Program.CanReuseFanState(manual,custom),"Reapplying the same custom curve confirms live duty through the Agent");
            var auto=JsonNode.Parse("{\"available\":true,\"profile\":0,\"ecControl\":0}");
            Check(Program.CanReuseFanState(auto,new FanPreferences(0)),"Already verified firmware Auto needs no extra command");
            auto!["ecControl"]=1;
            Check(!Program.CanReuseFanState(auto,new FanPreferences(0)),"Auto requires the independent firmware mode bit to be clear");

            var start=DateTimeOffset.Parse("2026-01-01T12:00:00Z");
            const string channelChanged="The OEM channel changed before the command could be sent";
            const string leaseExpired="Returned to Auto: overlay telemetry stopped";
            JsonObject Sample(int time,bool runningManual,string? failure=null) => new()
            {
                ["available"]=true,["profile"]=runningManual?3:0,["ecControl"]=runningManual?1:0,
                ["percent"]=runningManual?JsonValue.Create(40):null,
                ["customCurve"]=new JsonArray(curve.Points().Select(n=>(JsonNode?)JsonValue.Create(n)).ToArray()),
                ["error"]=failure,["rpmSampleUtc"]=start.AddSeconds(time).ToString("o")
            };
            bool Observe(FanConnectionRecovery recovery,int now,bool runningManual,string? failure=null,string selected="custom",int? sampled=null,FanPreferences? desired=null)
                =>recovery.ShouldRestore(selected,desired??custom,Sample(sampled??now,runningManual,failure),start.AddSeconds(now));
            void ObserveHealthy(FanConnectionRecovery recovery,int time)
            {
                Observe(recovery,time,true);
                Observe(recovery,time+3,true);
                Observe(recovery,time+6,true);
            }

            var resume=new FanConnectionRecovery();
            Check(!Observe(resume,0,false,channelChanged),"An unconfirmed saved curve is not applied by recovery");
            ObserveHealthy(resume,1);
            Check(Observe(resume,8,false,channelChanged),"A verified active curve recovers once after a replacement channel returns firmware Auto");
            Check(!Observe(resume,9,false,channelChanged),"A persistent channel fallback does not loop");
            ObserveHealthy(resume,10);
            Check(Observe(resume,17,false,leaseExpired),"A settled curve can recover after a later telemetry lease expires");
            Check(!Observe(resume,18,false,leaseExpired),"An immediate failed reapply leaves Auto alone");

            var rejected=new FanConnectionRecovery();
            ObserveHealthy(rejected,0);
            Check(!Observe(rejected,7,false,"Fan output readback failed: requested 100%, measured 20%") &&
                !Observe(rejected,8,false,channelChanged),"A physical PWM rejection cannot be converted into an automatic retry");

            var changed=new FanConnectionRecovery();
            ObserveHealthy(changed,0);
            Check(!Observe(changed,7,false,channelChanged,selected:"custom-new-revision"),"Editing or switching a profile invalidates prior manual proof");
            ObserveHealthy(changed,8);
            Check(!Observe(changed,15,false,channelChanged,desired:new FanPreferences(0)),"An explicit Auto configuration never re-enters manual");

            var stale=new FanConnectionRecovery();
            ObserveHealthy(stale,0);
            Check(!Observe(stale,14,false,channelChanged,sampled:7) &&
                Observe(stale,15,false,channelChanged),"Recovery waits for a fresh Auto report from the reconnected Agent");

            var intermittent=new FanConnectionRecovery();
            Observe(intermittent,0,true);
            Observe(intermittent,10,true);
            Check(!Observe(intermittent,11,false,channelChanged),"Separated manual samples do not prove a settled curve");
            Check(FanConnectionRecovery.CanRecoverFrom(Sample(7,false,channelChanged)) &&
                !FanConnectionRecovery.CanRecoverFrom(Sample(8,false,"Fan output readback failed")) &&
                !FanConnectionRecovery.CanRecoverFrom(Sample(8,true,channelChanged)),
                "A fresh snapshot must still show eligible firmware Auto before writing");
        }
        catch(Exception failure){error=failure.ToString();}
        await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed=error==null,checks,error,modelOnly=true,hardwareTested=false},Backend.Program.Json));return error==null?0:1;
    }
}
