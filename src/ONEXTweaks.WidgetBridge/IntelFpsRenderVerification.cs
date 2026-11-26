// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Text.Json;

namespace ONEXTweaks.WidgetBridge;

internal static class IntelFpsRenderVerification
{
    internal static async Task<int> Run(string executable, string receipt)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(receipt))!;
        var frameReport = Path.Combine(folder, "RTSSRenderProbe-fps.json");
        var samples = new Dictionary<string,double[]>();
        var readbacks = new Dictionary<string,IntelFpsState>();
        using var limiter = new IntelFrameLimit();
        var original = limiter.Read();
        Process? renderer = null;
        IntelFpsState? ownedState = null;
        string? error = null, restoreError = null;
        const int limit = 30;

        void RequireNoRtssLimit()
        {
            if (Process.GetProcessesByName("RTSS").Length==0) return;
            if(BitConverter.ToInt32(RtssProfile.Read("FramerateLimit"))!=0||
                BitConverter.ToInt32(RtssProfile.Read("FramerateLimit",profile:"ONEXRenderProbe.exe"))!=0)
                throw new IOException("An active RTSS limit or fixture profile prevents isolating Intel");
        }

        void StopRenderer()
        {
            if (renderer == null) return;
            using (renderer)
            {
                if (!renderer.HasExited) renderer.Kill();
                if (!renderer.WaitForExit(2000)) throw new IOException("Test renderer did not exit");
            }
            renderer = null;
        }

        void StartRenderer()
        {
            StopRenderer();
            File.Delete(frameReport);
            renderer = Process.Start(new ProcessStartInfo(executable) { WorkingDirectory = folder, UseShellExecute = false })
                ?? throw new IOException("Test renderer did not start");
        }

        async Task Measure(string phase)
        {
            await Task.Delay(4300);
            var values = new List<double>();
            for (int index=0; index<4; index++)
            {
                await Task.Delay(1100);
                if (renderer?.HasExited != false) throw new IOException("Test renderer stopped");
                using var frame = JsonDocument.Parse(await File.ReadAllTextAsync(frameReport));
                double fps = frame.RootElement.GetProperty("fps").GetDouble();
                if (!double.IsFinite(fps)||fps<=1) throw new IOException("Invalid render frame rate");
                values.Add(fps);
            }
            samples[phase] = values.ToArray();
            RequireNoRtssLimit();
            readbacks[phase] = limiter.Read();
        }

        try
        {
            if (!OperatingSystem.IsWindows()||Process.GetCurrentProcess().SessionId==0||
                !Path.GetFileName(executable).Equals("ONEXRenderProbe.exe",StringComparison.OrdinalIgnoreCase)||
                !File.Exists(executable))
                throw new IOException("An interactive Windows session and the dedicated D3D fixture are required");
            if (Process.GetProcessesByName("ONEXRenderProbe").Length!=0)
                throw new IOException("Another test fixture is already running");
            RequireNoRtssLimit();
            if (!original.Available) throw new IOException("A readable Intel limit is required");
            await File.WriteAllTextAsync(receipt+".original",JsonSerializer.Serialize(original,Backend.Program.Json));
            if (original.Enabled)
            {
                ownedState=original with{Enabled=false};
                limiter.Set(false,original.Value);
                ownedState=limiter.Read();
            }
            StartRenderer();
            await Measure("off");
            if (samples["off"].Average()<limit*1.5) throw new IOException("Uncapped test is too slow to establish a limit");
            ownedState=original with{Enabled=true,Value=limit};
            limiter.Set(true,limit);
            await Measure("sameProcess");
            StartRenderer();
            await Measure("restarted");
        }
        catch (Exception failure) { error=failure.Message; }
        finally
        {
            try { StopRenderer(); } catch (Exception failure) { error??=failure.Message; }
            if (ownedState!=null)
            {
                try
                {
                    var current=limiter.Read();
                    if(current==ownedState)limiter.Set(original.Enabled,original.Value);
                    else if(current!=original)throw new IOException("Intel value changed outside the test; restore refused");
                }
                catch(Exception failure) { restoreError=failure.Message; }
            }
        }
        var after=limiter.Read();
        static bool Limited(double[] fps)=>fps.All(value=>value>=27&&value<=32);
        bool same=samples.TryGetValue("sameProcess",out var active)&&Limited(active);
        bool restarted=samples.TryGetValue("restarted",out var fresh)&&Limited(fresh);
        bool completed=error==null&&restoreError==null&&after==original;
        await File.WriteAllTextAsync(receipt,JsonSerializer.Serialize(new
        { completed, limit, original, samples, readbacks, same, restarted, after, error, restoreError },Backend.Program.Json));
        return completed?0:1;
    }
}
