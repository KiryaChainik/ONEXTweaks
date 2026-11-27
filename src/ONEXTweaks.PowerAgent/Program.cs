using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

[assembly: AssemblyVersion("0.29.348.0")]
[assembly: AssemblyFileVersion("0.29.1110.0")]

namespace ONEXTweaks.Power
{
    internal static class Program
    {
        internal const string VendorRoot = @"C:\Program Files\OneXConsole\resources\resources";
        internal const string VendorHash = "505DB9A04EB5F23D8D0DE31307ADB9D57D5DA1900908D9D995EDFC49EA5C7328";
        internal const string VendorFix3Hash = "B82263110012916610A5FC3C1EA69E79D1AF1CA631727CFD08278098172EC845";
        internal const string PipeName = "ONEXTweaks.Power.v1";
        internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 12 };
        [STAThread]
        public static int Main(string[] args)
        {
            bool installed = args.Length > 0 && args[0] == "serve-desktop";
            string report = !installed && args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "power-agent-result.json");
            try
            {
                if (!Environment.Is64BitProcess || !new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    throw new InvalidOperationException("Аппаратный модуль требует прав администратора.");
                if(args.Length==3&&args[0]=="fan-watchdog")return Hardware.RunFanWatchdog(int.Parse(args[1]),long.Parse(args[2]));
                DesktopAccess desktop = installed ? DesktopAccess.FromScheduledAction(args) : null;
                using (var singleton = new Mutex(true, @"Global\ONEXTweaks.PowerAgent", out first))
                {
                    if (!first)
                    {
                        if (installed) throw new IOException("Another PowerAgent already owns the hardware");
                        return 0;
                    }
                    if (installed) PowerAgentHealthLog.Start();
                    using (var hardware = new Hardware())
                    {
                        string command = args.Length == 0 ? "serve" : args[0];
                        if (command == "snapshot")
                        {
                            State snapshot = hardware.State();
                            WriteReport(report, snapshot);
                            return snapshot.available ? 0 : 1;
                        }
                        if (command == "verify-preferred" || command == "verify-captured-preferred")
                        {
                            if(command=="verify-captured-preferred")
                            {
                                if(args.Length!=3)throw new ArgumentException("Same-boot captured snapshot required");
                                hardware.RestoreSameBootFactorySnapshot(File.ReadAllText(args[2]));
                            }
                            string preferencePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ONEXTweaks","dev","power-preferences.json");
                            var saved=Json.DeserializeObject(File.ReadAllText(preferencePath)) as Dictionary<string,object>;
                            if(saved==null||!saved.ContainsKey("pl1Watts")||!saved.ContainsKey("pl2Watts")||!(saved["pl1Watts"] is int)||!(saved["pl2Watts"] is int))throw new InvalidDataException("No valid current saved power preference");
                            int pl1=(int)saved["pl1Watts"],pl2=(int)saved["pl2Watts"];PowerRules.Validate(pl1,pl2);
                            hardware.BeginSession();var before=hardware.State();State applied=null;
                            WriteReport(report+".checkpoint.json",new{stage="before",before});
                            try{hardware.Apply(pl1,pl2);applied=hardware.State();WriteReport(report+".checkpoint.json",new{stage="applied",before,applied});if(applied.pl1Watts!=pl1||applied.pl2Watts!=pl2)throw new IOException("Preferred power readback failed");}
                            finally{try{hardware.ControllerHardwareRelease();}finally{try{hardware.StopHomeControl();}finally{hardware.Restore();}}}
                            var restored=hardware.State();bool rawRestored=before.rawLimits==restored.rawLimits;
                            WriteReport(report,new{ok=rawRestored,before,applied,restored,rawRestored});return rawRestored?0:1;
                        }
                        if (command == "verify")
                        {
                            hardware.BeginSession();
                            var before = hardware.State();
                            object applied = null;
                            try
                            {
                                int pl1 = (int)before.pl1Watts - 1, pl2 = (int)before.pl2Watts - 1;
                                PowerRules.Validate(pl1, pl2);
                                hardware.Apply(pl1, pl2);
                                applied = hardware.State();
                            }
                            finally { try{hardware.ControllerHardwareRelease();}finally{try{hardware.StopHomeControl();}finally{hardware.Restore();}} }
                            var restored = hardware.State();
                            WriteReport(report, new { ok = true, before, applied, restored, rawRestored = before.rawLimits == restored.rawLimits });
                            return 0;
                        }
                        if (!installed && command != "serve") throw new ArgumentException("Unknown command");
                        if (hardware.OemHardwareQualified) hardware.RecoverFanAtStartup();
                        Serve(hardware, desktop);
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                WriteReport(report, new { ok = false, error = ex.Message, type = ex.GetType().Name });
                return 1;
            }
        }
        private static bool first;
        private static readonly DateTime InstallerProbeExpiresUtc = DateTime.UtcNow.AddMinutes(4);
        private static DateTime nextInstallerProbeUtc;
        private static int installerProbeSamples;
        private static string InstallerProbePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "installer-hardware-ready.json"); }
        }

        private static void ResetInstallerHardwareProbe()
        {
            if (installerProbeSamples == 0) return;
            installerProbeSamples = 0;
            nextInstallerProbeUtc = DateTime.MinValue;
            try { File.Delete(InstallerProbePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void ReportInstallerHardware(uint session, bool fanRequired, bool fanAvailable, State state)
        {
            DateTime now = DateTime.UtcNow;
            if (now >= InstallerProbeExpiresUtc) return;
            if (!state.available || (fanRequired ? !fanAvailable : !state.canWrite))
            {
                ResetInstallerHardwareProbe();
                return;
            }
            if (now < nextInstallerProbeUtc) return;
            if (now - nextInstallerProbeUtc > TimeSpan.FromSeconds(10)) ResetInstallerHardwareProbe();
            nextInstallerProbeUtc = now.AddSeconds(5);
            installerProbeSamples = Math.Min(installerProbeSamples + 1, 3);
            PowerAgentHealthLog.Record("installer-probe", new { samples = installerProbeSamples, state.pl1Watts, state.pl2Watts });
            if (installerProbeSamples < 3) return;
            string path = InstallerProbePath;
            string temporary = path + ".tmp";
            try
            {
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                    WriteReport(temporary, new { schema = 3, processId = process.Id, ownerSession = session, utc = now.ToString("o"), samples = installerProbeSamples, fan = fanAvailable, tdp = state.available, tdpWritable = state.canWrite });
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        private static void WriteReport(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, Json.Serialize(value), new UTF8Encoding(false));
        }
        private static void Serve(Hardware hardware, DesktopAccess desktop)
        {
            bool ending = false;
            while (!ending)
            {
                var security = DesktopAccess.PipeAcl(desktop);
                using (var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security))
                {
                    pipe.WaitForConnection();
                    uint session = 0;
                    if (desktop != null && !desktop.Accepts(pipe, out session)) continue;
                    hardware.BeginSession();
                    PowerAgentHealthLog.Record("desktop-channel-open", new { session });
                    string endReason = "closed";
                    try
                    {
                        using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true))
                        using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
                        {
                            while (pipe.IsConnected)
                            {
                                string line = desktop == null ? ReadBoundedLine(reader) : DesktopAccess.ReadWhileActive(reader, session);
                                if (line == null) { endReason = "reader-ended"; break; }
                                if (desktop != null && !DesktopAccess.IsActive(session)) { endReason = "session-inactive"; break; }
                                Monitor.Enter(hardware.SyncRoot);
                                try
                                {
                                object reply;
                                string action = null;
                                try
                                {
                                    var request = Json.DeserializeObject(line) as Dictionary<string, object>;
                                    if (request == null || !request.ContainsKey("action") || !(request["action"] is string)) throw new ArgumentException("Invalid request");
                                    action = (string)request["action"];
                                    if (!hardware.OemHardwareQualified && (action == "chargeRead" || action == "chargeSet" ||
                                        action == "fanSet" || action == "fanCurveSet" || action == "lightingSet" || action == "lightingFeedback" ||
                                        action == "controllerRead" || action == "controllerPrepare" || action == "smallMaskSet" || action == "cpuClockSet"))
                                        throw new InvalidOperationException(Hardware.UnqualifiedOemHardware);
                                    if (action == "read") { }
                                    else if (action == "set")
                                    {
                                        if (!request.ContainsKey("pl1") || !request.ContainsKey("pl2") || !(request["pl1"] is int) || !(request["pl2"] is int)) throw new ArgumentException("Integer limits required");
                                        hardware.Apply((int)request["pl1"], (int)request["pl2"]);
                                    }
                                    else if (action == "boostRead" || action == "boostSet")
                                    {
                                        if (action == "boostSet")
                                        {
                                            if (!request.ContainsKey("enabled") || !(request["enabled"] is bool)) throw new ArgumentException("Boolean boost state required");
                                            BoostPolicy.Set((bool)request["enabled"]);
                                        }
                                        reply = new {ok=true,boost=BoostPolicy.Read(),state=hardware.State()};
                                        writer.WriteLine(Json.Serialize(reply));
                                        continue;
                                    }
                                    else if (action == "chargeRead" || action == "chargeSet")
                                    {
                                        if (action == "chargeSet")
                                        {
                                            if (!request.ContainsKey("percent") || !(request["percent"] is int)) throw new ArgumentException("Integer charge percent required");
                                            hardware.SetCharge((int)request["percent"]);
                                        }
                                        reply = new { ok = true, charge = hardware.Charge(), state = hardware.State() };
                                        writer.WriteLine(Json.Serialize(reply));
                                        continue;
                                    }
                                    else if(action=="controllerPrepare")
                                    {
                                        reply=new{ok=true,controller=hardware.ControllerHardwarePrepare(request),state=hardware.State()};writer.WriteLine(Json.Serialize(reply));continue;
                                    }
                                    else if(action=="controllerRelease")
                                    {
                                        hardware.ControllerHardwareRelease();reply=new{ok=true,controller=new{prepared=false},state=hardware.State()};writer.WriteLine(Json.Serialize(reply));continue;
                                    }
                                    else if(action=="controllerRead")
                                    {
                                        reply=new{ok=true,controller=hardware.ControllerHardwareRead(),state=hardware.State()};writer.WriteLine(Json.Serialize(reply));continue;
                                    }
                                    else if(action=="lightingFeedback")
                                    {
                                        reply=new{ok=true,lightingFeedback=hardware.GameLightingFeedback((string)request["operation"],(string)request["leaseId"],Convert.ToInt32(request["epoch"]),Convert.ToInt32(request["red"]),Convert.ToInt32(request["green"]),Convert.ToInt32(request["blue"]))};writer.WriteLine(Json.Serialize(reply));continue;
                                    }
                                    else if(action=="homeRead"||action=="fanCurveSet"||action=="fanSet"||action=="lightingSet"||action=="smallMaskSet"||action=="cpuClockSet")
                                    {
                                        if(action=="fanSet")hardware.SetFan(Convert.ToInt32(request["profile"]));
                                        if(action=="fanCurveSet")hardware.SetFanCurve(Program.Json.Deserialize<int[]>(Program.Json.Serialize(request["points"])));
                                        if(action=="cpuClockSet")hardware.SetCpuClock(Convert.ToInt32(request["mhz"]));
                                        if(action=="smallMaskSet")hardware.SetSmallMask((bool)request["enabled"]);
                                        if(action=="lightingSet")
                                        {
                                            string settings=(string)request["settings"];var fields=Program.Json.Deserialize<Dictionary<string,object>>(settings);object raw;
                                            bool calibrationRaw=fields.TryGetValue("calibrationRaw",out raw)&&raw is bool&&(bool)raw;
                                            // This request-only marker is never persisted in HomeSettings.
                                            hardware.SetLighting(Program.Json.Deserialize<HomeSettings>(settings),calibrationRaw);
                                        }
                                        bool fanAvailable;
                                        var homeState=hardware.HomeState(out fanAvailable);
                                        var powerState=hardware.State();
                                        if(desktop!=null&&action=="homeRead")ReportInstallerHardware(session,hardware.OemHardwareQualified,fanAvailable,powerState);
                                        reply=new{ok=true,home=homeState,state=powerState};writer.WriteLine(Json.Serialize(reply));continue;
                                    }
                                    else if (action == "captureFactoryPower")
                                    {
                                        string token = hardware.CaptureFactoryPower((string)request["expected"]);
                                        writer.WriteLine(Json.Serialize(new { ok = true, factoryPowerToken = token }));
                                        continue;
                                    }
                                    else if (action == "restoreFactoryPower") hardware.RestoreFactoryPower((string)request["token"]);
                                    else if (action == "restore") hardware.Restore();
                                    else if (action == "shutdown") { hardware.Restore(); ending = true; }
                                    else throw new ArgumentException("Unknown action");
                                    reply = new { ok = true, state = hardware.State() };
                                }
                                catch (Exception ex)
                                {
                                    if (desktop != null && action == "homeRead")
                                    {
                                        ResetInstallerHardwareProbe();
                                        PowerAgentHealthLog.Record("home-read-error", new { session }, ex);
                                    }
                                    reply = new { ok = false, error = ex.Message, state = hardware.State() };
                                }
                                writer.WriteLine(Json.Serialize(reply));
                                if (ending) break;
                                }
                                finally{Monitor.Exit(hardware.SyncRoot);}
                            }
                        }
                    }
                    catch (IOException ex) { endReason = "io"; PowerAgentHealthLog.Record("desktop-channel-error", new { session }, ex); }
                    catch (InvalidDataException ex) { endReason = "invalid-data"; PowerAgentHealthLog.Record("desktop-channel-error", new { session }, ex); }
                    finally
                    {
                        ResetInstallerHardwareProbe();
                        PowerAgentHealthLog.Record("desktop-channel-end", new { session, reason = endReason });
                        try { hardware.ControllerHardwareRelease(); }
                        finally { try { hardware.StopHomeControl(); } finally { hardware.Restore(); } }
                    }
                }
            }
        }
        private static string ReadBoundedLine(StreamReader reader)
        {
            var line = new StringBuilder();
            for (int i = 0; i < 8192; i++)
            {
                int value = reader.Read();
                if (value < 0) return line.Length == 0 ? null : line.ToString();
                if (value == '\n') return line.ToString();
                if (value != '\r') line.Append((char)value);
            }
            throw new InvalidDataException("Request too long");
        }
    }

    public sealed class State
    {
        public bool available { get; set; }
        public bool canWrite { get; set; }
        public double pl1Watts { get; set; }
        public double pl2Watts { get; set; }
        public double initialPl1Watts { get; set; }
        public double initialPl2Watts { get; set; }
        public double? cpuTemperatureC { get; set; }
        public double? apuPowerWatts { get; set; }
        public string rawLimits { get; set; }
        public string status { get; set; }
    }
    internal sealed class Sample
    {
        internal ulong Raw;
        internal double Pl1, Pl2;
        internal bool Enabled, Locked;
        internal bool Matches(int pl1, int pl2) { return Enabled && Pl1 == pl1 && Pl2 == pl2; }
    }
    internal sealed partial class Hardware : IDisposable
    {
        internal const string UnqualifiedOemHardware = "Board and firmware have not been qualified for this hardware control.";
        internal readonly bool OemHardwareQualified;
        private readonly Type msr;
        private readonly MethodInfo init, read, write, close;
        private readonly OemPipe oem;
        private bool powerReaderInitialized;
        private Sample baseline;
        private Sample factoryPowerSnapshot;
        private string factoryPowerToken;
        private bool changed;
        private long lastEnergyStamp;
        private uint lastPackageEnergy;
        private double? measuredApuWatts;
        private MethodInfo ecRead, ecClose;
        private bool chargeInitialized;
        private Assembly vendorAssembly;
        internal Hardware()
        {
            string boardRevision = ReadDeviceBoardRevision();
            OemHardwareQualified = HasQualifiedOemHardware(boardRevision);
            string path = Path.Combine(Program.VendorRoot, "CompatLayerCT.exe");
            ValidateHash(path, Program.VendorHash, Program.VendorFix3Hash);
            ValidateHash(Path.Combine(Program.VendorRoot, "PawnIOLib.dll"), "D71F62627D66983BB9F5B1C269F27BCD8C1B8A46E794377A6330F84C198F4443");
            ValidateHash(Path.Combine(Program.VendorRoot, @"tools\pawnio\IntelMSR.bin"), "D6ED85D65AB17A22F813EF98207D6D537155EE2DED5976A21CB48413C9B92E5F");
            Environment.CurrentDirectory = Program.VendorRoot;
            var assembly = Assembly.LoadFrom(path);
            vendorAssembly = assembly;
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            assembly.GetType("CompatLayerCT.Global", true).GetField("rootPath", flags).SetValue(null, Program.VendorRoot);
            msr = assembly.GetType("CompatLayerCT.pawnio.IntelMSRSingleModule", true);
            init = msr.GetMethod("Init", flags);
            read = msr.GetMethod("ioctl_read_msr", flags);
            write = msr.GetMethod("ioctl_write_msr", flags);
            close = msr.GetMethod("Close", flags);
            oem = new OemPipe();
            if (OemHardwareQualified) InitializeGameLightingRecovery();
        }
        internal static void ValidateHash(string path, params string[] expected)
        {
            using (var file = File.OpenRead(path))
            using (var hash = SHA256.Create())
                if (Array.IndexOf(expected, BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "")) < 0)
                    throw new InvalidOperationException("Версия аппаратного модуля изменилась. Нужна повторная проверка.");
        }
        private static string ReadDeviceBoardRevision()
        {
            using (var search = new ManagementObjectSearcher("SELECT Manufacturer,Product,Version FROM Win32_BaseBoard"))
            using (var items = search.Get())
            {
                if (items.Count != 1) throw new InvalidOperationException("Не удалось определить модель платы.");
                foreach (ManagementObject board in items)
                    if (IsSupportedModel((string)board["Manufacturer"], (string)board["Product"])) return (string)board["Version"];
            }
            throw new InvalidOperationException("Модель платы не проверена.");
        }
        internal static bool IsSupportedModel(string manufacturer, string product)
        {
            return manufacturer == "ONE-NETBOOK" && product == "ONEXPLAYER 3";
        }
        internal static bool IsQualifiedOemHardware(string boardRevision, string biosVersion)
        {
            return boardRevision == "1002-B" && biosVersion == "5.01";
        }
        internal static void ValidateOemHardware()
        {
            if (!HasQualifiedOemHardware(ReadDeviceBoardRevision())) throw new InvalidOperationException(UnqualifiedOemHardware);
        }
        private static bool HasQualifiedOemHardware(string boardRevision)
        {
            if (boardRevision != "1002-B") return false;
            try
            {
                using (var search = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS"))
                using (var items = search.Get())
                {
                    if (items.Count == 1)
                        foreach (ManagementObject bios in items)
                            return IsQualifiedOemHardware(boardRevision, (string)bios["SMBIOSBIOSVersion"]);
                }
            }
            catch (ManagementException) { }
            catch (UnauthorizedAccessException) { }
            catch (COMException) { }
            return false;
        }
        private Sample Read()
        {
            if (!powerReaderInitialized)
            {
                if (!(bool)init.Invoke(null, new object[0])) throw new InvalidOperationException("Не удалось открыть модуль чтения лимитов.");
                powerReaderInitialized = true;
            }
            ulong units = (ulong)read.Invoke(null, new object[] { (uint)0x606 });
            ulong raw = (ulong)read.Invoke(null, new object[] { (uint)0x610 });
            var sample = new Sample { Raw = raw, Pl1 = PowerRules.Pl1(units, raw), Pl2 = PowerRules.Pl2(units, raw), Enabled = PowerRules.IsEnabled(raw), Locked = PowerRules.IsLocked(raw) };
            if (sample.Pl1 <= 0 || sample.Pl2 <= 0 || raw == ulong.MaxValue || units == ulong.MaxValue) throw new IOException("Invalid power limit readback.");
            return sample;
        }
        internal void BeginSession()
        {
            factoryPowerSnapshot = null;
            factoryPowerToken = null;
            if (!changed) baseline = null;
        }
        private double? ReadApuWatts()
        {
            try
            {
                long stamp=Stopwatch.GetTimestamp();
                if(lastEnergyStamp!=0&&stamp-lastEnergyStamp<Stopwatch.Frequency/4)return measuredApuWatts;
                ulong unit=(ulong)read.Invoke(null,new object[]{(uint)0x606});
                uint energy=(uint)((ulong)read.Invoke(null,new object[]{(uint)0x611})&0xffffffffUL);
                measuredApuWatts=null;
                if(lastEnergyStamp!=0)
                {
                    double seconds=(double)(stamp-lastEnergyStamp)/Stopwatch.Frequency;
                    if(seconds>=0.2&&seconds<=10)
                    {
                        uint delta=unchecked(energy-lastPackageEnergy);
                        double watts=delta*Math.Pow(2,-(int)((unit>>8)&31))/seconds;
                        if(!double.IsNaN(watts)&&!double.IsInfinity(watts)&&watts>=0&&watts<=120)measuredApuWatts=watts;
                    }
                }
                lastPackageEnergy=energy;lastEnergyStamp=stamp;return measuredApuWatts;
            }
            catch{lastEnergyStamp=0;measuredApuWatts=null;return null;}
        }
        internal State State()
        {
            Sample current;
            try { current = Read(); }
            catch (Exception failure) { return new State { status = "Power limits unavailable: " + failure.GetBaseException().Message }; }
            if (baseline == null) baseline = current;
            double? temperature = null;
            try
            {
                object value = oem.Call("/func/getOXPSensorCpuTemp");
                double number = Convert.ToDouble(value);
                if (number >= 1 && number <= 110) temperature = number;
            }
            catch { }
            bool supported = CanWriteLimits(current);
            return new State { available = true, canWrite = supported, pl1Watts = current.Pl1, pl2Watts = current.Pl2,
                initialPl1Watts = baseline.Pl1, initialPl2Watts = baseline.Pl2, cpuTemperatureC = temperature, apuPowerWatts=ReadApuWatts(),
                rawLimits = "0x" + current.Raw.ToString("X16"), status = supported ? "Лимиты прочитаны из MSR" :
                    !oem.IsTrusted ? "Аппаратный канал ещё не подтверждён" : "Запись недоступна для текущего состояния лимитов" };
        }
        internal static bool CanRestoreLimits(Sample sample)
        {
            return sample != null && sample.Enabled && !sample.Locked && PowerRules.CanRestoreCaptured(sample.Pl1, sample.Pl2);
        }
        private bool CanWriteLimits(Sample current)
        {
            return write != null && oem.IsTrusted && CanRestoreLimits(baseline) && CanRestoreLimits(current);
        }
        internal void Apply(int pl1, int pl2)
        {
            PowerRules.Validate(pl1, pl2);
            if (!oem.IsTrusted)
            {
                State state = State();
                if (!state.canWrite) throw new InvalidOperationException(state.status);
            }
            Sample previous = Read();
            if (baseline == null) baseline = previous;
            if (!CanWriteLimits(previous)) throw new InvalidOperationException("Нельзя подтвердить восстановление текущих лимитов.");
            changed = true;
            try { SetAndVerify(pl1, pl2); }
            catch (Exception failure)
            {
                try { RestoreCaptured(previous); }
                catch (Exception rollback) { throw new InvalidOperationException("Ошибка записи и восстановления: " + failure.Message + "; " + rollback.Message); }
                throw new InvalidOperationException("Запись не подтверждена. Предыдущие лимиты восстановлены. " + failure.Message);
            }
        }
        private void SetAndVerify(int pl1, int pl2)
        {
            PowerRules.Validate(pl1, pl2);
            WriteAndVerify(pl1,pl2);
        }
        private void RestoreCaptured(Sample captured)
        {
            if(!captured.Enabled||captured.Locked||!PowerRules.CanRestoreCaptured(captured.Pl1,captured.Pl2))throw new InvalidOperationException("Captured power state cannot be restored");
            // The OEM high-level setter clamps the factory PL2. Restore only the
            // exact captured RAPL register, through the already hash-pinned module.
            // No register address or raw value is exposed through the client IPC.
            if(write==null||!(bool)write.Invoke(null,new object[]{(uint)0x610,captured.Raw}))throw new IOException("Captured RAPL rollback was not accepted");
            for(int i=0;i<20;i++){if(Read().Raw==captured.Raw){Thread.Sleep(100);if(Read().Raw==captured.Raw)return;}Thread.Sleep(50);}
            throw new IOException("Captured RAPL rollback readback mismatch");
        }
        internal string CaptureFactoryPower(string expected)
        {
            Sample current = Read();
            if (write == null || !oem.IsTrusted || !current.Enabled || current.Locked
                || !Pl2ControlPolicy.IsFactoryReadback(current.Pl1, current.Pl2)
                || !string.Equals(expected, "0x" + current.Raw.ToString("X16"), StringComparison.Ordinal))
                throw new InvalidOperationException("The factory power state changed before capture.");

            factoryPowerSnapshot = current;
            factoryPowerToken = Guid.NewGuid().ToString("N");
            return factoryPowerToken;
        }

        internal void RestoreFactoryPower(string token)
        {
            if (factoryPowerSnapshot == null || !string.Equals(token, factoryPowerToken, StringComparison.Ordinal))
                throw new InvalidOperationException("The factory power snapshot is not owned by this session.");

            Sample captured = factoryPowerSnapshot;
            Sample previous = Read();
            if (!previous.Enabled || previous.Locked || !PowerRules.CanRestoreCaptured(previous.Pl1, previous.Pl2))
                throw new InvalidOperationException("The current power state cannot be restored.");

            if (previous.Raw != captured.Raw)
            {
                changed = true;
                try { RestoreCaptured(captured); }
                catch (Exception failure)
                {
                    try { RestoreCaptured(previous); }
                    catch (Exception rollback) { throw new AggregateException("Factory restoration failed and the previous state could not be recovered.", failure, rollback); }
                    throw;
                }
            }
            factoryPowerSnapshot = null;
            factoryPowerToken = null;
        }

        internal void RestoreSameBootFactorySnapshot(string json)
        {
            // Diagnostic recovery for the failed high-level rollback test. Accept
            // only a real saved snapshot from this OS boot and the known25/52 pair.
            var root=Program.Json.DeserializeObject(json) as Dictionary<string,object>;
            if(root==null||!root.ContainsKey("state")||!root.ContainsKey("updatedUtc"))throw new InvalidDataException("Invalid captured snapshot");
            DateTimeOffset stamp;if(!DateTimeOffset.TryParse((string)root["updatedUtc"],out stamp)||stamp> DateTimeOffset.UtcNow.AddSeconds(5))throw new InvalidDataException("Invalid snapshot time");
            using(var search=new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem"))
            using(var items=search.Get())foreach(ManagementObject os in items)if(stamp.UtcDateTime<ManagementDateTimeConverter.ToDateTime((string)os["LastBootUpTime"]).ToUniversalTime())throw new InvalidDataException("Snapshot belongs to an earlier Windows boot");
            var state=root["state"] as Dictionary<string,object>;ulong raw;
            if(state==null||!state.ContainsKey("rawLimits")||!ulong.TryParse(((string)state["rawLimits"]).Replace("0x",""),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out raw))throw new InvalidDataException("Captured RAPL value missing");
            ulong units=(ulong)read.Invoke(null,new object[]{(uint)0x606});var current=Read();
            var captured=new Sample{Raw=raw,Pl1=PowerRules.Pl1(units,raw),Pl2=PowerRules.Pl2(units,raw),Enabled=PowerRules.IsEnabled(raw),Locked=PowerRules.IsLocked(raw)};
            const ulong mask=0x00007FFF00007FFFUL;
            if(captured.Pl1!=25||captured.Pl2!=52||!captured.Enabled||captured.Locked||current.Locked||(current.Raw&~mask)!=(raw&~mask))throw new InvalidDataException("Snapshot is not the validated same-boot factory state");
            try{RestoreCaptured(captured);baseline=Read();changed=false;}
            catch{RestoreCaptured(current);throw;}
        }
        private void WriteAndVerify(int pl1,int pl2)
        {
            // A new OneXConsole helper defaults to 35 W until its TDP range is initialized.
            const int oemMinimum = 3;
            int ceiling = pl2 > Pl2ControlPolicy.StandardManualCeiling
                ? Pl2ControlPolicy.ExtendedManualCeiling : Pl2ControlPolicy.StandardManualCeiling;
            oem.Call(string.Format("/tdp/init/{0}/{1}/{2}", oemMinimum, Pl2ControlPolicy.MaximumPl1, ceiling));
            int generation = oem.ConnectionGeneration;
            oem.Call(string.Format("/msr/setCpuPl/{0}/{1}/4", pl1, pl2), requiredGeneration: generation);
            Sample first = Read();
            Sample observed = first;
            for (int i = 0; i < 20; i++)
            {
                if (observed.Matches(pl1, pl2))
                {
                    Thread.Sleep(100);
                    observed = Read();
                    if (observed.Matches(pl1, pl2)) return;
                }
                Thread.Sleep(100);
                observed = Read();
            }
            throw new InvalidOperationException(string.Format(
                "Драйвер не подтвердил {0}/{1} Вт. Первое чтение: {2}/{3} Вт, последнее: {4}/{5} Вт.",
                pl1, pl2, first.Pl1, first.Pl2, observed.Pl1, observed.Pl2));
        }
        private void InitializeCharge()
        {
            if (!OemHardwareQualified) throw new InvalidOperationException(UnqualifiedOemHardware);
            if (chargeInitialized) return;
            ValidateHash(Path.Combine(Program.VendorRoot, @"tools\pawnio\LpcIO.bin"), "B3896A1CAB0D808FCA31FE2EBCAE045D59DAC690DA87B17C858BB8DA357EB45E");
            ValidateHash(Path.Combine(Program.VendorRoot, @"tools\pawnio\LpcACPIEC.bin"), "C38FD116E7AFF4D1FDB0A494E296BE0A6708E5A22FC72F14587442FB7F8F7906");
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            Type global = vendorAssembly.GetType("CompatLayerCT.Global", true);
            global.GetField("forcePawnIOMode", flags).SetValue(null, true);
            global.GetField("isPawnIOMode", flags).SetValue(null, true);
            global.GetField("ecAccessType", flags).SetValue(null, Enum.ToObject(vendorAssembly.GetType("CompatLayerCT.ECAccessType", true), 2));
            global.GetMethod("InitBaseEc", flags).Invoke(null, new object[] {1259,1112,1113});
            Type wrapper = vendorAssembly.GetType("CompatLayerCT.pawnio.ECWrapperSingleModule", true);
            if (!(bool)wrapper.GetMethod("Init", flags).Invoke(null, new object[0])) throw new InvalidOperationException("EC reader unavailable");
            global.GetField("pawnIOInstallSuccess", flags).SetValue(null, true);
            Type ec = vendorAssembly.GetType("CompatLayerCT.EC", true);
            ecRead = ec.GetMethod("ECRamReadByte", flags);
            ecClose = ec.GetMethod("FreeEC", flags);
            // These are the installed ONEXPLAYER 3 profile's initialization parameters.
            // They configure the OEM service; bypass/force-charge registers are never written here.
            oem.Call("/func/initBaseEc/1259/1112/1113");
            oem.Call("/battery/initEc", new {addrChargeLimit=1187,addrBypassPower=1188,addrForceChargeMin=1189,
                valueBypassPowerModeOff=0,valueBypassPowerMode1=1,valueBypassPowerMode2=3});
            chargeInitialized = true;
        }
        internal object Charge()
        {
            InitializeCharge();
            int percent = ReadChargeByte(1187);
            if (percent < 20 || percent > 100) throw new InvalidOperationException("Unrecognized EC charge limit: " + percent);
            return new {available=true,percent,enabled=percent<100,bypassMode=ReadChargeByte(1188),forceChargeMin=ReadChargeByte(1189)};
        }
        private int ReadChargeByte(ushort address) { return (byte)ecRead.Invoke(null, new object[] {address}); }
        internal void SetCharge(int percent)
        {
            if (percent < 20 || percent > 100) throw new ArgumentOutOfRangeException("percent", "Charge limit must be 20..100 percent");
            InitializeCharge();
            int previous = ReadChargeByte(1187);
            if (previous < 20 || previous > 100) throw new InvalidOperationException("Cannot restore unknown charge limit");
            if (previous == percent) return;
            try { SetChargeAndVerify(percent); }
            catch (Exception failure)
            {
                try { SetChargeAndVerify(previous); }
                catch (Exception rollback) { throw new InvalidOperationException("Charge write and rollback failed: " + failure.Message + "; " + rollback.Message); }
                throw new InvalidOperationException("Charge write failed; previous value restored. " + failure.Message);
            }
        }
        private void SetChargeAndVerify(int percent)
        {
            oem.Call("/battery/setChargeLimit/" + percent);
            for (int i=0;i<10;i++)
            {
                Thread.Sleep(100);
                if (ReadChargeByte(1187)==percent) {Thread.Sleep(100);if(ReadChargeByte(1187)==percent)return;}
            }
            throw new IOException("EC did not confirm charge limit");
        }
        internal void Restore()
        {
            if (!changed) return;
            RestoreCaptured(baseline);
            changed = false;
        }
        public void Dispose()
        {
            try { try { DisposeHome(); } finally { Restore(); } }
            finally { if (oem != null) oem.Dispose(); if (ecClose != null) ecClose.Invoke(null, new object[0]); if (powerReaderInitialized && close != null) close.Invoke(null, new object[0]); }
        }
    }

    internal sealed class BoostState
    {
        public bool available {get;set;}
        public string scheme {get;set;}
        public uint acMode {get;set;}
        public uint dcMode {get;set;}
        public bool enabled {get;set;}
        public bool onAc {get;set;}
    }
    internal static class BoostPolicy
    {
        private static readonly Guid Processor=new Guid("54533251-82be-4824-96c1-47b60b740d00");
        private static readonly Guid Mode=new Guid("be337238-0d82-4146-a960-4f3749d470c7");
        private static string SavedPath {get {return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"boost-modes.json");}}
        private static Guid Active()
        {
            IntPtr p;Check(PowerGetActiveScheme(IntPtr.Zero,out p));
            try{return (Guid)Marshal.PtrToStructure(p,typeof(Guid));}finally{LocalFree(p);}
        }
        private static void Check(uint code){if(code!=0)throw new System.ComponentModel.Win32Exception((int)code);}
        internal static BoostState Read()
        {
            Guid scheme=Active(),sub=Processor,setting=Mode;uint ac,dc;
            Check(PowerReadACValueIndex(IntPtr.Zero,ref scheme,ref sub,ref setting,out ac));
            Check(PowerReadDCValueIndex(IntPtr.Zero,ref scheme,ref sub,ref setting,out dc));
            if(ac>6||dc>6)throw new InvalidOperationException("Unsupported boost policy mode");
            PowerStatus status;
            if(!GetSystemPowerStatus(out status)||status.AcLineStatus>1)throw new InvalidOperationException("Current power source unavailable");
            bool onAc=status.AcLineStatus==1;
            return new BoostState{available=true,scheme=scheme.ToString(),acMode=ac,dcMode=dc,onAc=onAc,enabled=(onAc?ac:dc)>0};
        }
        internal static void Set(bool enabled)
        {
            BoostState before=Read();
            Guid scheme=new Guid(before.scheme);
            var saved=new Dictionary<string,uint[]>();
            if(File.Exists(SavedPath))saved=Program.Json.Deserialize<Dictionary<string,uint[]>>(File.ReadAllText(SavedPath));
            uint[] modes;
            if(!saved.TryGetValue(before.scheme,out modes)||modes==null||modes.Length!=2||modes[0]<1||modes[0]>6||modes[1]<1||modes[1]>6)modes=new uint[]{1,1};
            if(before.acMode>0)modes[0]=before.acMode;
            if(before.dcMode>0)modes[1]=before.dcMode;
            saved[before.scheme]=modes;
            // Persist nonzero modes before disabling, so a later process restart retains them.
            string temp=SavedPath+".tmp";
            File.WriteAllText(temp,Program.Json.Serialize(saved),new UTF8Encoding(false));
            if(File.Exists(SavedPath))File.Replace(temp,SavedPath,null);else File.Move(temp,SavedPath);
            uint ac=enabled?modes[0]:0,dc=enabled?modes[1]:0;
            if(before.acMode==ac&&before.dcMode==dc)return;
            try
            {
                if(Active()!=scheme)throw new InvalidOperationException("Power plan changed; retry");
                Write(scheme,ac,dc,true);
                BoostState actual=Read();
                if(actual.scheme!=before.scheme||actual.acMode!=ac||actual.dcMode!=dc)throw new IOException("Boost policy readback mismatch");
            }
            catch(Exception failure)
            {
                try{Write(scheme,before.acMode,before.dcMode,Active()==scheme);}
                catch(Exception rollback){throw new InvalidOperationException("Boost write and rollback failed: "+failure.Message+"; "+rollback.Message);}
                throw new InvalidOperationException("Boost policy unchanged/restored. "+failure.Message);
            }
        }
        private static void Write(Guid scheme,uint ac,uint dc,bool activate)
        {
            Guid sub=Processor,setting=Mode;
            Check(PowerWriteACValueIndex(IntPtr.Zero,ref scheme,ref sub,ref setting,ac));
            Check(PowerWriteDCValueIndex(IntPtr.Zero,ref scheme,ref sub,ref setting,dc));
            if(activate)
            {
                if(Active()!=scheme)throw new InvalidOperationException("Power plan changed while applying boost");
                Check(PowerSetActiveScheme(IntPtr.Zero,ref scheme));
            }
        }
        [StructLayout(LayoutKind.Sequential)]private struct PowerStatus{public byte AcLineStatus,BatteryFlag,BatteryLifePercent,SystemStatusFlag;public uint BatteryLifeTime,BatteryFullLifeTime;}
        [DllImport("kernel32.dll")]private static extern IntPtr LocalFree(IntPtr p);
        [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetSystemPowerStatus(out PowerStatus status);
        [DllImport("powrprof.dll")]private static extern uint PowerGetActiveScheme(IntPtr key,out IntPtr scheme);
        [DllImport("powrprof.dll")]private static extern uint PowerReadACValueIndex(IntPtr key,ref Guid scheme,ref Guid sub,ref Guid setting,out uint value);
        [DllImport("powrprof.dll")]private static extern uint PowerReadDCValueIndex(IntPtr key,ref Guid scheme,ref Guid sub,ref Guid setting,out uint value);
        [DllImport("powrprof.dll")]private static extern uint PowerWriteACValueIndex(IntPtr key,ref Guid scheme,ref Guid sub,ref Guid setting,uint value);
        [DllImport("powrprof.dll")]private static extern uint PowerWriteDCValueIndex(IntPtr key,ref Guid scheme,ref Guid sub,ref Guid setting,uint value);
        [DllImport("powrprof.dll")]private static extern uint PowerSetActiveScheme(IntPtr key,ref Guid scheme);
    }

    internal static class PowerAgentHealthLog
    {
        private const long MaximumSize = 128 * 1024;
        private static readonly object Gate = new object();
        private static readonly UTF8Encoding Encoding = new UTF8Encoding(false);
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static int processId;

        internal static void Start()
        {
            using (var process = Process.GetCurrentProcess()) processId = process.Id;
            Record("start", null);
        }

        internal static void Record(string name, object data, Exception error = null)
        {
            if (processId == 0) return;
            lock (Gate)
            {
                try
                {
                    var cause = error == null ? null : error.GetBaseException();
                    var win32 = cause as System.ComponentModel.Win32Exception;
                    var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hardware-health.jsonl");
                    var file = new FileInfo(path);
                    if (file.Exists && file.Length >= MaximumSize)
                    {
                        string previous = path + ".prev";
                        if (File.Exists(previous)) File.Replace(path, previous, null);
                        else File.Move(path, previous);
                    }
                    string line = Json.Serialize(new
                    {
                        utc = DateTime.UtcNow.ToString("o"), processId, name, data,
                        error = cause == null ? null : new
                        {
                            outer = error.GetType().Name, type = cause.GetType().Name,
                            hresult = cause.HResult, nativeError = win32 == null ? (int?)null : win32.NativeErrorCode
                        }
                    });
                    File.AppendAllText(path, line + Environment.NewLine, Encoding);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    internal sealed class OemPipe : IDisposable
    {
        private NamedPipeClientStream pipe;
        private StreamReader reader;
        private StreamWriter writer;
        private Process ownedHelper;
        internal int ConnectionGeneration { get; private set; }
        internal int ServerId { get; private set; }
        internal bool IsTrusted { get { return pipe != null && pipe.IsConnected && reader != null && writer != null; } }
        private void Connect()
        {
            Disconnect();
            string qualifiedRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"ONEXTweaks\PowerAgent\OneXConsole-fix3");
            bool useQualifiedRoot = Directory.Exists(qualifiedRoot);
            string root = useQualifiedRoot ? qualifiedRoot : Program.VendorRoot;
            string executable = Path.Combine(root, "CompatLayerCT.exe");
            if (useQualifiedRoot)
                Hardware.ValidateHash(executable, Program.VendorFix3Hash);
            else
                Hardware.ValidateHash(executable, Program.VendorHash, Program.VendorFix3Hash);
            pipe = new NamedPipeClientStream(".", "CompatLayerCT", PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            string stage = "connect";
            var clock = Stopwatch.StartNew();
            try
            {
                try { pipe.Connect(300); }
                catch (TimeoutException) { stage = "start-helper"; StartHelper(root, executable); }
                stage = "verify-owner";
                using (var server = PipeOwner())
                {
                    ServerId = server.Id;
                    if (!string.Equals(server.MainModule.FileName, executable, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!useQualifiedRoot || !RetireStockHelper(server))
                            throw new InvalidOperationException("Неизвестный аппаратный сервер.");
                        StartHelper(root, executable);
                        using (var replacement = PipeOwner())
                        {
                            if (!string.Equals(replacement.MainModule.FileName, executable, StringComparison.OrdinalIgnoreCase) || !DesktopAccess.IsTrustedHelper(replacement))
                                throw new InvalidOperationException("Аппаратный канал повторно занял другой сервер.");
                            ServerId = replacement.Id;
                        }
                    }
                    else if (!DesktopAccess.IsTrustedHelper(server)) throw new IOException("Unexpected OEM helper account or session");
                }
                reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
                writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true, NewLine = "\n" };
                ConnectionGeneration++;
                PowerAgentHealthLog.Record("oem-connected", new { ServerId, ConnectionGeneration, ms = clock.ElapsedMilliseconds });
            }
            catch (Exception error)
            {
                PowerAgentHealthLog.Record("oem-connect-error", new { stage, ServerId, ConnectionGeneration, ms = clock.ElapsedMilliseconds }, error);
                Disconnect();
                throw;
            }
        }
        private Process PipeOwner()
        {
            uint processId;
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out processId))
                throw new IOException("Не удалось проверить аппаратный канал.");
            return Process.GetProcessById((int)processId);
        }
        private void StartHelper(string root, string executable)
        {
            Disconnect();
            var helpers = Process.GetProcessesByName("CompatLayerCT");
            foreach (var helper in helpers) helper.Dispose();
            if (helpers.Length != 0) throw new InvalidOperationException("Аппаратный канал занят. Закройте OneXConsole.");
            if (ownedHelper != null) ownedHelper.Dispose();
            var start = new ProcessStartInfo(executable, "1013 ONEXTweaks.PowerAgent 2 pioforce")
            { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true };
            ownedHelper = Process.Start(start);
            pipe = new NamedPipeClientStream(".", "CompatLayerCT", PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            pipe.Connect(8000);
        }
        private bool RetireStockHelper(Process server)
        {
            string stock = Path.Combine(Program.VendorRoot, "CompatLayerCT.exe");
            if (!string.Equals(server.MainModule.FileName, stock, StringComparison.OrdinalIgnoreCase)) return false;
            using (var self = Process.GetCurrentProcess())
                if (server.SessionId != self.SessionId) return false;
            Hardware.ValidateHash(stock, Program.VendorHash);
            string command = null;
            using (var search = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId=" + server.Id))
            using (var results = search.Get())
                foreach (ManagementObject process in results)
                    using (process) command = process["CommandLine"] as string;
            if (!System.Text.RegularExpressions.Regex.IsMatch(command ?? "", @"\s1013\s+OneXConsole\s+2\s+pioforce\s*$")) return false;
            // Recover only the pinned stock helper left behind after the OEM GUI
            // has closed. Never terminate an active console, service, or driver.
            var consoles = Process.GetProcessesByName("OneXConsole");
            foreach (var process in consoles) process.Dispose();
            if (consoles.Length != 0) return false;
            Disconnect();
            if (!server.HasExited) server.Kill();
            if (!server.WaitForExit(3000)) throw new IOException("Штатный аппаратный помощник не завершился.");
            return true;
        }
        internal object Call(string path, object parameters = null, int timeoutMs = 6000, int? requiredGeneration = null, bool allowConnect = true)
        {
            if (!IsTrusted)
            {
                if (!allowConnect) throw new IOException("The verified OEM connection is no longer open");
                Connect();
            }
            if (requiredGeneration.HasValue && requiredGeneration.Value != ConnectionGeneration)
                throw new IOException("The OEM channel changed before the command could be sent");
            string id = Guid.NewGuid().ToString();
            var responseClock = Stopwatch.StartNew();
            string stage = "write";
            try
            {
                writer.WriteLine(Program.Json.Serialize(new { id, path, parameters }));
                while (responseClock.ElapsedMilliseconds < timeoutMs)
                {
                    stage = "read";
                    var pending = reader.ReadLineAsync();
                    int remaining = Math.Max(0, timeoutMs - (int)responseClock.ElapsedMilliseconds);
                    if (!pending.Wait(remaining)) throw new TimeoutException("Аппаратный сервер не ответил.");
                    if (pending.Result == null) throw new EndOfStreamException("Аппаратный канал закрыт.");
                    stage = "decode";
                    var response = Program.Json.DeserializeObject(pending.Result) as Dictionary<string, object>;
                    if (response == null || !response.ContainsKey("id") || !id.Equals(response["id"])) continue;
                    if (!response.ContainsKey("code") || Convert.ToInt32(response["code"]) != 1) throw new IOException("Аппаратный сервер отклонил команду.");
                    object result;
                    return response.TryGetValue("result", out result) && result != null ? Program.Json.DeserializeObject((string)result) : null;
                }
                throw new TimeoutException("Нет подтверждения аппаратной команды.");
            }
            catch (Exception error)
            {
                string[] parts = path.Split('/');
                string route = parts.Length > 2 ? parts[1] + "/" + parts[2] : path;
                PowerAgentHealthLog.Record("oem-request-error", new { route, stage, ServerId, ConnectionGeneration, ms = responseClock.ElapsedMilliseconds }, error);
                Disconnect();
                throw;
            }
        }
        private void Disconnect()
        {
            if (pipe != null) pipe.Dispose();
            pipe = null; reader = null; writer = null;
            ServerId = 0;
        }
        public void Dispose()
        {
            Disconnect();
            if (ownedHelper != null) { try { if (!ownedHelper.HasExited) ownedHelper.Kill(); } finally { ownedHelper.Dispose(); } }
        }
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint processId);
    }
}
