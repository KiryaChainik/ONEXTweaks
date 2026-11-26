using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using ONEXTweaks.Backend;

namespace ONEXTweaks.Panel;

internal static class Entry
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--backend")
            return Backend.Program.Main(["serve", "3600"]).GetAwaiter().GetResult();
        string? verify = args.Length >= 2 && args[0] == "--verify" ? args[1] : null;
        bool exercisePower = args.Contains("--exercise-power");
        using var singleton = new Mutex(true, "Local\\ONEXTweaks.Panel", out bool first);
        if (!first) return 0;
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        Process? backend = null;
        var cancellation = new CancellationTokenSource();
        var power = new PowerChannel();
        var window = new MonitorWindow(verify, exercisePower, power);
        bool closing = false, closeReady = false;
        window.Closing += async (_, e) =>
        {
            if (closeReady || verify is not null) return;
            e.Cancel = true;
            if (closing) return;
            closing = true;
            try { await power.ShutdownAsync(); closeReady = true; window.Close(); }
            catch (Exception ex) { closing = false; window.PowerFailed("Восстановление лимитов: " + ex.Message); }
        };
        app.Exit += (_, _) =>
        {
            cancellation.Cancel();
            power.Dispose();
            if (backend is not null)
            {
                try { if (!backend.HasExited) backend.Kill(); } catch (InvalidOperationException) { }
                backend.Dispose();
            }
            cancellation.Dispose();
        };
        window.Loaded += async (_, _) =>
        {
            try
            {
                string executable = Environment.ProcessPath ?? throw new InvalidOperationException("No process path");
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
                // When launched as 'dotnet Panel.dll', the child needs the entry assembly as well.
                if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                start.ArgumentList.Add("--backend");
                backend = Process.Start(start) ?? throw new InvalidOperationException("Backend did not start");
                _ = ReadPower(window, power, cancellation.Token);
                await ReadStream(window, cancellation.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { window.ConnectionFailed(ex.GetType().Name); }
        };
        return app.Run(window);
    }

    private static async Task ReadPower(MonitorWindow window, PowerChannel power, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested && !power.ShuttingDown)
        {
            try
            {
                var result = await power.ReadAsync();
                if (result.State is not null) window.UpdatePower(result.State);
                if (!result.Ok) window.PowerFailed(result.Error ?? "Нет данных управления");
            }
            catch (Exception ex) when (!cancellation.IsCancellationRequested) { window.PowerFailed(ex.Message); }
            try { await Task.Delay(1000, cancellation); } catch (OperationCanceledException) { return; }
        }
    }

    private static async Task ReadStream(MonitorWindow window, CancellationToken cancellation)
    {
        using var pipe = new NamedPipeClientStream(".", Backend.Program.PipeName, PipeDirection.In,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(10000, cancellation);
        using var reader = new StreamReader(pipe, Encoding.UTF8);
        long previous = 0;
        while (!cancellation.IsCancellationRequested)
        {
            string line = await reader.ReadLineAsync(cancellation) ?? throw new EndOfStreamException();
            var snapshot = JsonSerializer.Deserialize<TelemetrySnapshot>(line, Backend.Program.Json)
                ?? throw new InvalidDataException("No telemetry");
            if (snapshot.SchemaVersion != 1 || snapshot.Sequence <= previous)
                throw new InvalidDataException("Unexpected telemetry sequence");
            previous = snapshot.Sequence;
            window.Update(snapshot);
        }
    }
}
