using System.Runtime.InteropServices;
using System.Text.Json;
using ONEXTweaks.Backend;
using static ONEXTweaks.Backend.IgclNative;

int assertions = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    assertions++;
}
// Binary layout, especially C++ bool size and 64-bit alignment, is part of the driver ABI.
Check(Marshal.SizeOf<InitArgs>() == 36, "init ABI size");
Check(Marshal.OffsetOf<InitArgs>("AppVersion").ToInt32() == 8, "init version offset");
Check(Marshal.SizeOf<AdapterProperties>() == 320, "adapter ABI size");
Check(Marshal.OffsetOf<AdapterProperties>("Name").ToInt32() == 88, "adapter name offset");
Check(Marshal.SizeOf<PowerProperties>() == 20, "power ABI size");
Check(Marshal.OffsetOf<PowerProperties>("DefaultLimit").ToInt32() == 8, "C++ bool packing");
Check(Marshal.SizeOf<PowerLimits>() == 36, "power limit ABI size");
Check(Marshal.OffsetOf<PowerLimits>("Burst").ToInt32() == 20, "burst offset");
Check(Marshal.SizeOf<EnergyCounter>() == 24, "energy ABI size");
Check(Marshal.SizeOf<FrequencyProperties>() == 32, "frequency properties ABI size");
Check(Marshal.SizeOf<FrequencyState>() == 56, "frequency state ABI size");
Check(Marshal.SizeOf<TemperatureProperties>() == 24, "temperature properties ABI size");
Check(IntelProvider.CounterWatts(100, 1000, 30100, 2000) == 30, "microjoule/microsecond conversion");
Check(IntelProvider.CounterWatts(100, 2000, 50, 3000) is null, "energy reset invalidates delta");
Check(IntelProvider.CounterWatts(100, 2000, 200, 2000) is null, "zero time invalidates delta");
Check(IntelProvider.CounterWatts(100, 2000, 200, 1000) is null, "time reset invalidates delta");
Check(WindowsSampler.CpuPercent(100, 200, 100, 150, 300, 200) == 75, "kernel includes idle");
Check(WindowsSampler.CpuPercent(100, 200, 100, 110, 200, 100) is null, "impossible CPU counters");
Check(WindowsSampler.CpuPercent(100, 200, 100, 50, 300, 200) is null, "CPU counter reset");
var unknown = new TelemetrySnapshot(1, 1, DateTimeOffset.UtcNow,
    new(null, null, null, null, null, null), new("unavailable", "fixture", null, []));
var json = JsonSerializer.Serialize(unknown, ONEXTweaks.Backend.Program.Json);
using var document = JsonDocument.Parse(json);
Check(document.RootElement.GetProperty("windows").GetProperty("cpuLoadPercent").ValueKind == JsonValueKind.Null, "unknown is not zero");
Check(!document.RootElement.GetProperty("hardwareWritesImplemented").GetBoolean(), "write support not implied by driver capability");
Check(JsonSerializer.Deserialize<TelemetrySnapshot>(json, ONEXTweaks.Backend.Program.Json)?.Intel.Status == "unavailable", "snapshot contract roundtrip");
Check(ONEXTweaks.Power.PowerRules.Pl1(0xA0E03, 0x0000806800DD8060) == 12, "verified G3 PL1 decoding");
Check(ONEXTweaks.Power.PowerRules.Pl2(0xA0E03, 0x0000806800DD8060) == 13, "verified G3 PL2 decoding");
Check(ONEXTweaks.Power.PowerRules.IsEnabled(0x0000806800DD8060), "enabled flags");
Check(!ONEXTweaks.Power.PowerRules.IsLocked(0x0000806800DD8060), "unlocked flag");
Check(ONEXTweaks.Power.PowerRules.IsLocked(0x8000806800DD8060), "firmware lock flag");
Check(!ONEXTweaks.Power.PowerRules.CanRestore(12.5, 13), "fractional baseline must not be rounded during restore");
foreach (var pair in new[] { (7, 13), (41, 41), (12, 11), (12, 42), (-1, 13) })
{
    bool rejected = false;
    try { ONEXTweaks.Power.PowerRules.Validate(pair.Item1, pair.Item2); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "invalid power bounds rejected");
}
ONEXTweaks.Power.PowerRules.Validate(8, 9);
ONEXTweaks.Power.PowerRules.Validate(40, 41);
var guide = new ONEXTweaks.WidgetBridge.GuidePressFilter();
Check(guide.Observe(true, true, 0), "first Guide press accepted");
Check(!guide.Observe(false, false, 50), "input redirection is not a Guide release");
Check(!guide.Observe(true, true, 80), "reconnected held Guide does not trigger again");
Check(!guide.Observe(true, false, 100), "release must settle");
Check(guide.IsHeld, "no release action before settled physical up");
Check(!guide.Observe(true, true, 150), "brief valid false reading does not rearm Guide");
Check(!guide.Observe(true, false, 250), "physical release begins");
Check(!guide.Observe(true, false, 380), "settled physical release rearms");
Check(!guide.IsHeld, "release action becomes possible after settled up");
Check(guide.Observe(true, true, 500), "next physical press accepted");
Check(!guide.Observe(false, false, 1000), "long disconnection is not release");
Check(!guide.Observe(true, true, 1500), "held Guide after long disconnection stays suppressed");
Check(!guide.Observe(true, false, 1600), "next release begins");
Check(!guide.Observe(true, false, 1730), "next release settles");
Check(guide.Observe(true, true, 1740), "next deliberate press works after settling");
var quickGuide = new ONEXTweaks.WidgetBridge.GuidePressFilter();
Check(quickGuide.Observe(true, true, 0), "quick Guide press accepted");
quickGuide.Observe(true, false, 50);
quickGuide.Observe(true, false, 89);
Check(quickGuide.IsHeld, "release waits the full 40ms");
quickGuide.Observe(true, false, 90);
Check(!quickGuide.IsHeld, "release completes at 40ms");
Check(quickGuide.Observe(true, true, 180), "next deliberate press accepted at 180ms");
quickGuide.Observe(true, false, 200);
quickGuide.Observe(false, false, 225);
quickGuide.Observe(true, false, 230);
quickGuide.Observe(true, false, 269);
Check(quickGuide.IsHeld, "failed XInput read restarts release settling");
quickGuide.Observe(true, false, 270);
Check(!quickGuide.IsHeld, "40ms of valid release rearms after disconnection");
var closed=new ONEXTweaks.WidgetBridge.GuideCloseState(0);
Check(closed.Observe(false,0)==ONEXTweaks.WidgetBridge.GuideCloseResult.Waiting,"native Guide close must settle");
Check(closed.Observe(false,79)==ONEXTweaks.WidgetBridge.GuideCloseResult.Waiting,"do not acknowledge a transient native closed frame");
Check(closed.Observe(false,80)==ONEXTweaks.WidgetBridge.GuideCloseResult.Closed,"native closed confirmation after stable interval");
var nativeBounce=new ONEXTweaks.WidgetBridge.GuideCloseState(0);
nativeBounce.Observe(false,0);nativeBounce.Observe(false,50);
Check(nativeBounce.Observe(true,62)==ONEXTweaks.WidgetBridge.GuideCloseResult.Waiting,"recorded native false-to-true rebound is not a completed dismissal");
nativeBounce.Observe(false,100);
Check(nativeBounce.Observe(false,179)==ONEXTweaks.WidgetBridge.GuideCloseResult.Waiting,"rebound resets stable close interval");
Check(nativeBounce.Observe(false,180)==ONEXTweaks.WidgetBridge.GuideCloseResult.Closed,"close can complete after rebound");
var guideStuck=new ONEXTweaks.WidgetBridge.GuideCloseState(0);
Check(guideStuck.Observe(true,899)==ONEXTweaks.WidgetBridge.GuideCloseResult.Waiting,"wait for native dismissal within deadline");
Check(guideStuck.Observe(true,900)==ONEXTweaks.WidgetBridge.GuideCloseResult.TimedOut,"stuck native dismissal is bounded and does not inject another toggle");
var preferencesPath=Path.Combine(Path.GetTempPath(),"onex-prefs-"+Guid.NewGuid().ToString("N"),"power.json");
try
{
    var store=new ONEXTweaks.WidgetBridge.PowerPreferenceStore(preferencesPath);
    Check(store.Load() is null,"first launch has no invented power preference");
    store.Save(17,22);
    Check(new ONEXTweaks.WidgetBridge.PowerPreferenceStore(preferencesPath).Load() is {Pl1Watts:17,Pl2Watts:22},"chosen PL1/PL2 survive a new store instance");
    store.Save(19,24);
    Check(new ONEXTweaks.WidgetBridge.PowerPreferenceStore(preferencesPath).Load() is {Pl1Watts:19,Pl2Watts:24},"latest choice replaces prior choice atomically");
    File.WriteAllText(preferencesPath,"{\"schemaVersion\":1,\"pl1Watts\":8,\"pl2Watts\":100}");
    bool invalidRejected=false;try{store.Load();}catch(InvalidDataException){invalidRejected=true;}
    Check(invalidRejected,"corrupted persisted power bounds are rejected");
}
finally{if(Directory.Exists(Path.GetDirectoryName(preferencesPath)))Directory.Delete(Path.GetDirectoryName(preferencesPath)!,true);}
Console.WriteLine($"PASS: {assertions} ABI, counter, availability, contract and Guide lifecycle assertions.");
