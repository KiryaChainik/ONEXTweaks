using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ONEXTweaks.Panel;

internal sealed record PowerState(bool Available, bool CanWrite, double Pl1Watts, double Pl2Watts,
    double InitialPl1Watts, double InitialPl2Watts, double? CpuTemperatureC, string RawLimits, string Status, double? ApuPowerWatts = null);
internal sealed record ChargeState(bool Available, int Percent, bool Enabled, int BypassMode, int ForceChargeMin);
internal sealed record BoostState(bool Available, string Scheme, uint AcMode, uint DcMode, bool Enabled, bool OnAc);
internal sealed record PowerReply(bool Ok, string? Error, PowerState? State, ChargeState? Charge = null, BoostState? Boost = null,JsonObject? Home = null,JsonObject? Controller = null,JsonObject? LightingFeedback=null, string? FactoryPowerToken=null);

internal sealed class PowerChannel : IDisposable
{
    private static readonly string Product = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ONEXTweaks");
    private static readonly string AgentImage = Path.Combine(Product, "PowerAgent", "ONEXTweaks.PowerAgent.exe");
    private static readonly bool InstalledBridge = string.Equals(Environment.ProcessPath,
        Path.Combine(Product, "WidgetBridge", "ONEXTweaks.WidgetBridge.exe"), StringComparison.OrdinalIgnoreCase);
    private readonly SemaphoreSlim gate = new(1, 1);
    private NamedPipeClientStream? pipe;
    private StreamReader? reader;
    private StreamWriter? writer;
    private long sessionGeneration, nextSessionGeneration;
    private DateTimeOffset lastStart;private bool disposed;
    internal bool SupportsGameLighting {get;private set;}
    internal bool HardwareConfigured { get; } = !InstalledBridge || File.Exists(AgentImage);
    internal bool Connected => pipe?.IsConnected == true;
    internal long SessionGeneration => Volatile.Read(ref sessionGeneration);
    internal bool ShuttingDown { get; private set; }
    internal Task<PowerReply> SetCpuClockAsync(int mhz)=>Request(new{action="cpuClockSet",mhz});
    internal Task<PowerReply> PrepareControllerHardwareAsync(JsonObject mapping1,JsonObject mapping2,Action<long> onSession)=>Request(new{action="controllerPrepare",routingReady=true,mapping1,mapping2},onSession);
    internal Task<PowerReply> ReleaseControllerHardwareAsync()=>Request(new{action="controllerRelease"});
    internal Task<PowerReply> ReadControllerHardwareAsync()=>Request(new{action="controllerRead"});
    internal Task<PowerReply> ReadHomeAsync()=>Request(new{action="homeRead"});
    internal Task<PowerReply> SetFanCurveAsync(int[] points)=>Request(new{action="fanCurveSet",points});
    internal Task<PowerReply> SetFanAsync(int profile)=>Request(new{action="fanSet",profile});
    internal Task<PowerReply> GameLightingAsync(string operation,string leaseId,int epoch,int red,int green,int blue)=>Request(new{action="lightingFeedback",operation,leaseId,epoch,red,green,blue});
    internal Task<PowerReply> SetLightingAsync(string settings)=>Request(new{action="lightingSet",settings});
    internal Task<PowerReply> SetSmallMaskAsync(bool enabled)=>Request(new{action="smallMaskSet",enabled});
    internal Task<PowerReply> ReadAsync() => Request(new { action = "read" });
    internal Task<PowerReply> SetAsync(int pl1, int pl2) => Request(new { action = "set", pl1, pl2 });
    internal Task<PowerReply> ReadBoostAsync() => Request(new { action = "boostRead" });
    internal Task<PowerReply> SetBoostAsync(bool enabled) => Request(new { action = "boostSet", enabled });
    internal Task<PowerReply> ReadChargeAsync() => Request(new { action = "chargeRead" });
    internal Task<PowerReply> SetChargeAsync(int percent) => Request(new { action = "chargeSet", percent });
    internal Task<PowerReply> RestoreAsync() => Request(new { action = "restore" });
    internal Task<PowerReply> CaptureFactoryPowerAsync(string expected) => Request(new { action = "captureFactoryPower", expected });
    internal Task<PowerReply> RestoreFactoryPowerAsync(string token) => Request(new { action = "restoreFactoryPower", token });
    internal async Task ShutdownAsync()
    {
        if (!Connected) return;
        ShuttingDown = true;
        try
        {
            var result = await Request(new { action = "shutdown" });
            if (!result.Ok) throw new InvalidOperationException(result.Error);
        }
        catch { ShuttingDown = false; throw; }
    }
    private async Task<PowerReply> Request(object request,Action<long>? onSession=null)
    {
        await gate.WaitAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            ObjectDisposedException.ThrowIf(disposed,this);if (!Connected) await Connect(timeout.Token);
            onSession?.Invoke(SessionGeneration);
            await writer!.WriteLineAsync(JsonSerializer.Serialize(request, Backend.Program.Json).AsMemory(), timeout.Token);
            await writer.FlushAsync(timeout.Token);
            string line = await reader!.ReadLineAsync(timeout.Token) ?? throw new EndOfStreamException("Аппаратный канал закрыт");
            if (line.Length > 65536) throw new InvalidDataException("Unexpected power response size");
            return JsonSerializer.Deserialize<PowerReply>(line, Backend.Program.Json) ?? throw new InvalidDataException("Empty power response");
        }
        catch { Disconnect(); throw; }
        finally { gate.Release(); }
    }
    private async Task Connect(CancellationToken cancellation)
    {
        if (!HardwareConfigured)
            throw new IOException("Hardware control is unavailable on this installation.");
        Disconnect();
        pipe = OpenPowerPipe();
        try { await pipe.ConnectAsync(600, cancellation); }
        catch (TimeoutException)
        {
            pipe.Dispose();
            if (DateTimeOffset.UtcNow - lastStart > TimeSpan.FromSeconds(10))
            {
                lastStart = DateTimeOffset.UtcNow;
                var start = new ProcessStartInfo("schtasks.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add("/Run"); start.ArgumentList.Add("/TN"); start.ArgumentList.Add("ONEXTweaks-PowerAgent");
                using var process = Process.Start(start);
                if (process is not null) await process.WaitForExitAsync(cancellation);
            }
            pipe = OpenPowerPipe();
            await pipe.ConnectAsync(6000, cancellation);
        }
        SupportsGameLighting=VerifyServer(pipe);
        reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        Volatile.Write(ref sessionGeneration,++nextSessionGeneration);
    }
    private static NamedPipeClientStream OpenPowerPipe() => new(".", "ONEXTweaks.Power.v1",
        PipeAccessRights.ReadData | PipeAccessRights.WriteData | PipeAccessRights.ReadAttributes |
        PipeAccessRights.ReadPermissions | PipeAccessRights.Synchronize,
        PipeOptions.Asynchronous, TokenImpersonationLevel.Identification, HandleInheritability.None);

    private static bool VerifyServer(NamedPipeClientStream pipe)
    {
        if (InstalledBridge)
        {
            var owner = (SecurityIdentifier?)pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
            if (owner?.IsWellKnown(WellKnownSidType.LocalSystemSid) != true)
                throw new IOException("Аппаратный канал не принадлежит системному модулю ONEXTweaks");
        }
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint id)) throw new IOException("Не удалось проверить аппаратный модуль");
        IntPtr process = OpenProcess(0x1000, false, id);
        if (process == IntPtr.Zero) throw new IOException("Нет сведений об аппаратном модуле");
        try
        {
            var path = new StringBuilder(1024); uint length = 1024;
            if (!QueryFullProcessImageName(process, 0, path, ref length)) throw new IOException("Не удалось проверить путь аппаратного модуля");
            if (!string.Equals(path.ToString(), AgentImage, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Неизвестный аппаратный модуль");
            return Version.TryParse(FileVersionInfo.GetVersionInfo(AgentImage).FileVersion,out var version)&&version>=new Version(0,29,341,0);
        }
        finally { CloseHandle(process); }
    }
    private void Disconnect() { Volatile.Write(ref sessionGeneration,0);SupportsGameLighting=false;pipe?.Dispose(); pipe = null; reader = null; writer = null; }
    public void Dispose() { disposed=true;Disconnect(); }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint id);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint length);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
