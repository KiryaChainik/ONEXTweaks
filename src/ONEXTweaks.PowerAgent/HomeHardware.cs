using System;
using ONEXTweaks.Fans;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using ONEXTweaks.Lighting;
namespace ONEXTweaks.Power
{
    public sealed class LightZone
    {
        public int id {get;set;} public string name {get;set;} public bool enabled {get;set;} public int brightness {get;set;}
        public int mode {get;set;} public int preset {get;set;} public int r {get;set;} public int g {get;set;} public int b {get;set;} public bool breathing {get;set;}
        public int animationSpeed {get;set;}
    }
    public sealed class HomeSettings
    {
        public int cpuClockMhz {get;set;} public bool lightingEnabled {get;set;} public LightZone[] zones {get;set;}
        public double[][] fanPreset1 {get;set;} public double[][] fanPreset2 {get;set;}
    }
    internal sealed partial class Hardware
    {
        internal readonly object SyncRoot=new object();
        private bool homeLoaded,fanInitialized,rgbInitialized,homeDisposed;
        private int fanConnectionGeneration,rgbConnectionGeneration;
        private HomeSettings home;
        private Timer fanTimer;
        private int fanProfile,lastFanPercent=-1;
        private int[] customFanCurve;
        private double? fanTemperature;
        private DateTime fanLease;
        private string fanError,rgbError;
        private object[] acknowledgedLedColors;
        private LedCalibrationProfile ledProfile;
        private bool ledProfileLoaded,lastLightingWasRaw;
        private const int LightingWriteGapMs = 20;
        private readonly Stopwatch lightingClock = Stopwatch.StartNew();
        private long nextLightingWrite;
        private void LoadLedProfile()
        {
            if(ledProfileLoaded)return;
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"led-color-profile.json");
            if(File.Exists(path))ledProfile=LedCalibrationProfile.Parse(File.ReadAllText(path));
            ledProfileLoaded=true;
        }
        private static string HomePath {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"home-oem-settings.json");}}
        private void LoadHome()
        {
            if(homeLoaded)return;
            if(!File.Exists(HomePath))throw new IOException("OEM home settings have not been imported");
            home=Program.Json.Deserialize<HomeSettings>(File.ReadAllText(HomePath));ValidateHome(home);
            homeLoaded=true;fanTimer=new Timer(FanTick,null,1000,1000);
        }
        private static void ValidateHome(HomeSettings value)
        {
            if(value==null||value.zones==null||value.zones.Length!=5||value.zones.Any(z=>z==null)||!value.zones.Select(z=>z.id).OrderBy(x=>x).SequenceEqual(new[]{1,2,5,6,7}))throw new ArgumentException("Unknown ONEXPLAYER lighting zones");
            foreach(var z in value.zones)
            {
                if(z.brightness<1||z.brightness>4||z.mode<1||z.mode>2||z.r<0||z.r>255||z.g<0||z.g>255||z.b<0||z.b>255)
                    throw new ArgumentException("Invalid lighting settings");
                if (z.mode == 1 && (!LightingEffects.SupportsPresets(z.id) || !LightingEffects.IsPreset(z.preset) || z.breathing))
                    throw new ArgumentException("The lighting preset is not supported in this zone");
            }
            foreach(var curve in new[]{value.fanPreset1,value.fanPreset2})
            {
                if(curve==null||curve.Length!=5)throw new ArgumentException("Invalid OEM fan curve");
                for(int i=0;i<5;i++)if(curve[i].Length!=2||curve[i][0]!=45+10*i||curve[i][1]<0.2||curve[i][1]>1||double.IsNaN(curve[i][1]))throw new ArgumentException("Invalid OEM fan point");
            }
        }
        private void InitFan()
        {
            LoadHome();InitializeCharge();
            if(fanInitialized&&oem.IsTrusted&&fanConnectionGeneration==oem.ConnectionGeneration)return;
            fanInitialized=false;
            FanFirmwareControl.Probe(ReadChargeByte(1098));
            // The helper process has its own EC access mode. Match the ONEXPLAYER 3
            // OEM initialization before enabling manual control on this connection.
            oem.Call("/func/setECAccessType/2");
            fanConnectionGeneration=oem.ConnectionGeneration;
            oem.Call("/func/initBaseEc/1259/1112/1113",requiredGeneration:fanConnectionGeneration);
            oem.Call("/fan/init/common/1098/0/1/1099/184",requiredGeneration:fanConnectionGeneration);
            lastFanPercent=-1;
            fanInitialized=true;
        }
        private object CallInitializedFan(string path)
        {
            if(!fanInitialized)throw new IOException("The OEM fan channel is not initialized");
            return oem.Call(path,requiredGeneration:fanConnectionGeneration);
        }
        private void InitRgb()
        {
            LoadHome();
            // A cached flag cannot outlive a suspended HID device or a new OEM pipe session.
            if(rgbInitialized)
            {
                bool open=Convert.ToBoolean(oem.Call("/programhandle/isOpen"));
                if(rgbConnectionGeneration==oem.ConnectionGeneration&&open)return;
            }
            rgbInitialized=false;
            oem.Call("/programhandle/setType/CommonHid/true/2");oem.Call("/programhandle/connect");
            if(!Convert.ToBoolean(oem.Call("/programhandle/isOpen")))throw new IOException("The ONEXPLAYER lighting controller is not connected");
            rgbConnectionGeneration=oem.ConnectionGeneration;rgbInitialized=true;
        }
        internal object HomeState(out bool fanAvailable)
        {
            lock(SyncRoot)
            {
                bool fanOk=false,rgbOk=false;int? actualPercent=null,control=null,rpm=null;bool? smallMask=null;
                string fanStage = "init";
                var fanClock = Stopwatch.StartNew();
                try
                {
                    InitFan();
                    fanStage = "read-ec-control";
                    control=ReadChargeByte(1098);
                    // The firmware can leave the last manual duty in this register after switching to Auto.
                    if ((control.Value & 1) != 0)
                    {
                        fanStage = "read-helper-duty";
                        actualPercent = ReadFanPercent();
                    }
                    fanLease=DateTime.UtcNow;
                    fanOk=true;
                }
                catch (Exception e)
                {
                    fanError = e.Message;
                    PowerAgentHealthLog.Record("fan-read-error", new
                    {
                        stage = fanStage, oem.ServerId, oem.ConnectionGeneration, ms = fanClock.ElapsedMilliseconds
                    }, e);
                }
                if(fanOk)rpm=ReadFanRpm();
                fanAvailable=fanOk && rpm.HasValue;
                try{double temperature=Convert.ToDouble(oem.Call("/func/getOXPSensorCpuTemp"));fanTemperature=temperature>=1&&temperature<=110?(double?)temperature:null;}catch{fanTemperature=null;}
                try
                {
                    InitRgb();
                    LoadLedProfile();
                    RestoreNativeLighting();
                    rgbOk=true;
                }
                catch(Exception e){rgbInitialized=false;rgbError=e.Message;}
                try{smallMask=Convert.ToBoolean(oem.Call("/handykit/isSmallProcessorMask",new{logicCoreCount=14}));}catch{}
                return new {fan=new{available=fanOk,profile=fanProfile,percent=actualPercent,temperatureC=fanTemperature,ecControl=control,customCurve=customFanCurve,rpm,rpmSampleUtc=rpm.HasValue?DateTime.UtcNow.ToString("o"):null,temperaturePoints=FanCurvePolicy.TemperaturePoints(),preset1=home==null?null:home.fanPreset1,preset2=home==null?null:home.fanPreset2,error=fanError,profiles=new[]{new{id=0,name="Auto"},new{id=1,name="Profile 1"},new{id=2,name="Profile 2"}}},lighting=new{gameFeedback=GameLightingState(),available=rgbOk,enabled=home!=null&&home.lightingEnabled,zones=home==null?null:home.zones,error=rgbError,softwareAnimationSupported=false,colorTransform=ledProfile==null?"srgb-to-linear":"owner-calibrated-v1",calibrationProfileId=ledProfile==null?null:ledProfile.ProfileId,calibrationSourceSha256=ledProfile==null?null:ledProfile.SourceSha256,rawCalibrationBypass=lastLightingWasRaw,acknowledgedWireColors=acknowledgedLedColors,source="Saved sRGB settings and acknowledged OEM commands; device has no zone readback API"},smallProcessorMask=smallMask,cpuClockMhz=home==null?0:home.cpuClockMhz};
            }
        }
        internal void SetCpuClock(int mhz)
        {
            if(mhz!=0&&(mhz<1000||mhz>6000))throw new ArgumentOutOfRangeException("mhz");
            lock(SyncRoot){LoadHome();oem.Call("/powerplan/init/2000");oem.Call("/powerplan/setCpuMaxClock/"+mhz);home.cpuClockMhz=mhz;string temp=HomePath+".tmp";File.WriteAllText(temp,Program.Json.Serialize(home));File.Replace(temp,HomePath,null);}
        }
        internal void SetSmallMask(bool enabled)
        {
            lock(SyncRoot)
            {
                bool before=Convert.ToBoolean(oem.Call("/handykit/isSmallProcessorMask",new{logicCoreCount=14}));if(before==enabled)return;
                oem.Call("/handykit/setSmallProcessorMask",new{enable=enabled,logicCoreCount=14});
                if(Convert.ToBoolean(oem.Call("/handykit/isSmallProcessorMask",new{logicCoreCount=14}))!=enabled){oem.Call("/handykit/setSmallProcessorMask",new{enable=before,logicCoreCount=14});throw new IOException("CPU scheduling mask readback failed");}
            }
        }
        internal void SetFan(int profile)
        {
            lock(SyncRoot)
            {
                if(profile<0||profile>2)throw new ArgumentOutOfRangeException("profile");InitFan();
                if(profile==0){ForceFanAuto();fanError=null;return;}
                EnsureFanWatchdog();fanLease=DateTime.UtcNow;fanProfile=profile;fanError=null;
                try{EnterManualFanControl();UpdateFan();}
                catch(Exception requestFailure)
                {
                    fanError=requestFailure.Message;
                    try{ReturnFanAfterFailure();}
                    catch(Exception autoFailure){throw new AggregateException("Fan request failed and firmware Auto recovery also failed",requestFailure,autoFailure);}
                    throw;
                }
            }
        }
        internal void SetFanCurve(int[] points)
        {
            var verifiedPoints=FanCurvePolicy.Expand(points);
            lock(SyncRoot)
            {
                InitFan();EnsureFanWatchdog();
                bool alreadyManual=fanProfile!=0;
                customFanCurve=verifiedPoints;fanLease=DateTime.UtcNow;fanProfile=3;fanError=null;
                try
                {
                    // Enter firmware manual mode only when needed. On entry the old
                    // target is stale even if the new curve computes the same duty.
                    bool manual=alreadyManual&&(ReadChargeByte(1098)&1)!=0;
                    if(!manual)EnterManualFanControl();
                    UpdateFan();
                }
                catch(Exception requestFailure)
                {
                    fanError=requestFailure.Message;
                    try{ReturnFanAfterFailure();}
                    catch(Exception autoFailure){throw new AggregateException("Fan request failed and firmware Auto recovery also failed",requestFailure,autoFailure);}
                    throw;
                }
            }
        }
        private void EnterManualFanControl()
        {
            lastFanPercent=-1;
            FanFirmwareControl.Acquire();
            CallInitializedFan("/fan/automate/false");
            // OneXConsole waits before its first duty write even when the mode bit
            // already reads as manual. The EC can ignore duty during that transition.
            Thread.Sleep(500);
            if((ReadChargeByte(1098)&1)==0)throw new IOException("EC did not confirm manual fan control");
        }
        internal void ForceFanAuto()
        {
            if(FanFirmwareControl.Pending)FanFirmwareControl.Restore(null);
            else
            {
                InitFan();CallInitializedFan("/fan/automate/true");
                if((ReadChargeByte(1098)&1)!=0)throw new IOException("EC did not confirm firmware fan control");
            }
            fanProfile=0;
            lastFanPercent=-1;
        }
        private void ReturnFanAfterFailure()
        {
            if(FanFirmwareControl.Pending)ForceFanAuto();
            else{fanProfile=0;lastFanPercent=-1;}
        }
        internal void RecoverFanAtStartup()
        {
            try{FanFirmwareControl.Restore(null);}
            catch(Exception failure){fanError=failure.Message;}
        }
        private void FanTick(object unused)
        {
            if(!Monitor.TryEnter(SyncRoot))return;
            try
            {
                if(homeDisposed||fanProfile==0)return;
                if(DateTime.UtcNow-fanLease>TimeSpan.FromSeconds(8)){ForceFanAuto();fanError="Returned to Auto: overlay telemetry stopped";return;}
                UpdateFan();
            }
            catch(Exception e){fanError=e.Message;try{ForceFanAuto();}catch(Exception autoFailure){fanError+="; Auto: "+autoFailure.Message;}}
            finally{Monitor.Exit(SyncRoot);}
        }
        private void UpdateFan()
        {
            double temp=Convert.ToDouble(oem.Call("/func/getOXPSensorCpuTemp"));if(double.IsNaN(temp)||double.IsInfinity(temp)||temp<1||temp>110)throw new IOException("Fan CPU sensor unavailable");
            if(fanProfile==3){WriteFanPercent(FanCurvePolicy.PercentAt(temp,customFanCurve));return;}
            var curve=fanProfile==1?home.fanPreset1:home.fanPreset2;
            int percent=(int)(curve[0][1]*100);
            if(temp>=95)percent=100;
            else for(int i=0;i<5;i++)
            {
                double x=curve[i][0],y=curve[i][1]*100;
                if(temp<=x){if(i>0){double px=curve[i-1][0],py=curve[i-1][1]*100;percent=(int)(py+(temp-px)/(x-px)*(y-py));}break;}
                percent=i==4?(int)(y+(temp-x)/(95-x)*(100-y)):(int)y;
            }
            percent=Math.Max(20,Math.Min(100,percent));
            WriteFanPercent(percent);
        }
        private int? ReadFanRpm()
        {
            // ONEXPLAYER 3 exposes the tachometer as a big-endian 16-bit EC word.
            const ushort highAddress = 0x458, lowAddress = 0x459;
            try
            {
                int high = ReadChargeByte(highAddress);
                int low = ReadChargeByte(lowAddress);
                if (high != ReadChargeByte(highAddress)) return null;
                int value = (high << 8) | low;
                return value <= 8000 ? (int?)value : null;
            }
            catch (Exception error)
            {
                PowerAgentHealthLog.Record("fan-tach-error", new { oem.ServerId, oem.ConnectionGeneration }, error);
                return null;
            }
        }

        private int ReadFanPercent()
        {
            return FanReplyPolicy.Percent(CallInitializedFan("/fan/getFanSpeed"));
        }
        private void WriteFanPercent(int percent)
        {
            if((ReadChargeByte(FanFirmwareControl.ApplicationRegister)&FanFirmwareControl.ApplicationMask)==0)
                throw new IOException("Embedded controller released application fan control");
            bool rejected=false;
            if(percent!=lastFanPercent)
            {
                if((ReadChargeByte(1098)&1)!=1)throw new IOException("Firmware left manual fan control before the duty write");
                object reply=CallInitializedFan("/fan/setFanSpeed/"+percent);
                rejected=reply is bool&&!(bool)reply;
                // Match the OEM effect-settle interval before trusting a readback.
                Thread.Sleep(200);
            }
            // An unchanged target still needs hardware verification on each fan tick.
            int actual=ReadFanPercent();
            int rawDuty=ReadChargeByte(1099);
            for(int attempt=0;!rejected&&attempt<3&&!FanOutputMatches(percent,actual,rawDuty);attempt++)
            {
                Thread.Sleep(100);
                if((ReadChargeByte(1098)&1)==0)throw new IOException("Firmware left manual fan control during duty verification");
                actual=ReadFanPercent();
                rawDuty=ReadChargeByte(1099);
            }
            if(rejected||!FanOutputMatches(percent,actual,rawDuty))
                throw new IOException(string.Format("Fan output readback failed: requested {0}%, measured {1}%{2}{3}",
                    percent,actual,rejected?" (OEM rejected the duty write)":"",FanFailureReadback()));
            if((ReadChargeByte(1098)&1)!=1)throw new IOException("Firmware left manual fan control during duty verification");
            if((ReadChargeByte(FanFirmwareControl.ApplicationRegister)&FanFirmwareControl.ApplicationMask)==0)
                throw new IOException("Embedded controller released application fan control");
            lastFanPercent=percent;
        }
        private static bool FanOutputMatches(int requested,int reported,int rawDuty)
        {
            return Math.Abs(reported-requested)<=1 && rawDuty<=184 && Math.Abs(rawDuty*100.0/184-requested)<=1;
        }
        private string FanFailureReadback()
        {
            // Read before the caller returns to Auto so this sample describes the failed write.
            try
            {
                int manual=ReadChargeByte(1098)&1;
                int duty=ReadChargeByte(1099);
                return string.Format(" (EC manual={0}, PWM byte={1}, 184 full scale)",manual,duty);
            }
            catch(Exception error)
            {
                return " (EC fan readback unavailable: "+error.Message+")";
            }
        }
        private Process fanWatchdog;
        private void EnsureFanWatchdog()
        {
            using(var parent=Process.GetCurrentProcess())
            {
                int pid=parent.Id;
                long ticks=parent.StartTime.ToUniversalTime().Ticks;
                using(var ready=new EventWaitHandle(false,EventResetMode.ManualReset,FanWatchdogReadyName(pid,ticks)))
                {
                    if(fanWatchdog==null||fanWatchdog.HasExited)
                    {
                        if(fanWatchdog!=null)fanWatchdog.Dispose();
                        ready.Reset();
                        fanWatchdog=Process.Start(new ProcessStartInfo(parent.MainModule.FileName,
                            "fan-watchdog "+pid+" "+ticks){UseShellExecute=false,CreateNoWindow=true});
                    }
                    for(int i=0;i<40;i++)
                    {
                        if(ready.WaitOne(100)&&!fanWatchdog.HasExited)return;
                        if(fanWatchdog.HasExited)throw new IOException("Fan watchdog did not start");
                    }
                }
            }
            throw new IOException("Fan watchdog is not ready");
        }
        private static string FanWatchdogReadyName(int pid,long ticks)
        {
            return @"Local\ONEXTweaks.PowerAgent.FanWatchdog."+FanFirmwareControl.Identity(pid,ticks).Replace('/','.');
        }
        internal static int RunFanWatchdog(int pid,long ticks)
        {
            try
            {
                using(var parent=Process.GetProcessById(pid))
                using(var current=Process.GetCurrentProcess())
                {
                    if(parent.StartTime.ToUniversalTime().Ticks!=ticks||
                       !string.Equals(parent.MainModule.FileName,current.MainModule.FileName,StringComparison.OrdinalIgnoreCase))return 1;
                    using(var ready=EventWaitHandle.OpenExisting(FanWatchdogReadyName(pid,ticks)))
                    {
                        ready.Set();
                        parent.WaitForExit();
                    }
                }
            }
            catch(ArgumentException){}
            catch(WaitHandleCannotBeOpenedException){}
            string owner=FanFirmwareControl.Identity(pid,ticks);
            for(int i=0;i<3;i++)try{FanFirmwareControl.Restore(owner);return 0;}catch{Thread.Sleep(1000);}
            return 1;
        }
        private static bool NativeBreathing(LightZone zone)
        {
            return zone.breathing && LightingEffects.SupportsFactoryBreathing(zone.id);
        }

        private static bool LegacyBreathing(LightZone zone)
        {
            return zone.breathing && !NativeBreathing(zone);
        }

        private static void NormalizeLighting(HomeSettings settings)
        {
            foreach(var zone in settings.zones)
            {
                zone.animationSpeed = 0;
                if(LegacyBreathing(zone))
                {
                    zone.mode = 1;
                    zone.preset = LightingEffects.ChromaBreathing;
                    zone.breathing = false;
                }
            }
        }

        private void RestoreNativeLighting()
        {
            if(gameLightingApplied || gameLightingRestore) return;
            var legacyZones = home.zones.Where(zone => zone.animationSpeed != 0 || LegacyBreathing(zone)).ToArray();
            if(legacyZones.Length == 0) return;
            // Keep the saved document recoverable until the normal settings save.
            if(home.lightingEnabled && legacyZones.Any(zone => zone.enabled))
                ApplyLighting(home,lastLightingWasRaw);
            NormalizeLighting(home);
            rgbError=null;
        }

        private int[] LightingWireColor(LightZone zone, bool rawCalibration)
        {
            if(ledProfile != null && !rawCalibration)
                return ledProfile.Transform(zone.id, zone.brightness, zone.r, zone.g, zone.b);
            return new[] { LedColorTransform.ToLinearByte(zone.r), LedColorTransform.ToLinearByte(zone.g), LedColorTransform.ToLinearByte(zone.b) };
        }

        private void ApplyLighting(HomeSettings value,bool rawCalibration=false)
        {
            InitRgb();LoadLedProfile();
            var colors=new List<object>();
            foreach(var z in value.zones)
            {
                bool enable=value.lightingEnabled&&z.enabled;
                if(enable)
                {
                    if(z.mode==1 || LegacyBreathing(z))
                    {
                        int preset = z.mode==1 ? z.preset : LightingEffects.ChromaBreathing;
                        WriteRgbCommand("/rgbPartition/setPreset/"+z.id+"/"+preset);
                    }
                    else
                    {
                        var rgb = LightingWireColor(z, rawCalibration);
                        WriteRgbCommand(LedColorTransform.CommandRaw(z.id,rgb,NativeBreathing(z)));
                        colors.Add(new{id=z.id,r=rgb[0],g=rgb[1],b=rgb[2]});
                    }
                }
                WriteRgbCommand(string.Format("/rgbPartition/setOpen/{0}/{1}/{2}",z.id,enable?"true":"false",z.brightness));
            }
            acknowledgedLedColors=colors.ToArray();
        }
        private void WriteRgbCommand(string path)
        {
            long remaining = nextLightingWrite - lightingClock.ElapsedMilliseconds;
            if (remaining > 0) Thread.Sleep((int)remaining);
            try
            {
                object result = oem.Call(path);
                if(result is bool&&!(bool)result)throw new IOException("The lighting controller rejected a command");
            }
            finally
            {
                // OneXConsole waits 20ms after every physical RGB report, including setOpen.
                nextLightingWrite = lightingClock.ElapsedMilliseconds + LightingWriteGapMs;
            }
        }
        internal void SetLighting(HomeSettings settings,bool rawCalibration=false)
        {
            lock(SyncRoot)
            {
                LoadHome();if(settings==null)throw new ArgumentException("Lighting settings missing");settings.fanPreset1=home.fanPreset1;settings.fanPreset2=home.fanPreset2;settings.cpuClockMhz=home.cpuClockMhz;ValidateHome(settings);var before=home;gameLighting.Manual();
                NormalizeLighting(settings);
                try
                {
                    ApplyLighting(settings,rawCalibration);
                    // Finish fallible journal cleanup before committing saved settings and publishing them.
                    ClearGameLightingJournal();
                    string temp=HomePath+".tmp";File.WriteAllText(temp,Program.Json.Serialize(settings));
                    if(File.Exists(HomePath))File.Replace(temp,HomePath,null);else File.Move(temp,HomePath);
                    home=settings;lastLightingWasRaw=rawCalibration;rgbError=null;
                }
                catch(Exception failure)
                {rgbInitialized=false;rgbError=failure.Message;try{ApplyLighting(before,lastLightingWasRaw);NormalizeLighting(before);ClearGameLightingJournal();}catch(Exception rollback){throw new IOException("Lighting write and rollback failed: "+failure.Message+"; "+rollback.Message);}throw;}
            }
        }
        internal void StopHomeControl()
        {
            lock(SyncRoot)
            {
                try{StopGameLighting();}
                finally{if(fanProfile!=0||FanFirmwareControl.Pending)ForceFanAuto();}
            }
        }
        private void DisposeHome()
        {
            lock(SyncRoot)
            {
                homeDisposed=true;
                if(gameLightingTimer!=null)gameLightingTimer.Dispose();
                if(fanTimer!=null)fanTimer.Dispose();
                try{StopGameLighting();}
                finally{if(fanProfile!=0||FanFirmwareControl.Pending)ForceFanAuto();}
            }
        }
    }
}
