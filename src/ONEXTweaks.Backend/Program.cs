using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace ONEXTweaks.Backend;

internal static class Program
{
    internal const string PipeName = "ONEXTweaks.Telemetry.v1";
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
        { Console.Error.WriteLine("ONEXTweaks backend requires 64-bit Windows."); return 1; }
        try
        {
            string command = args.FirstOrDefault() ?? "snapshot";
            if (command == "client") return await Client(args.Length > 1 ? ParseCount(args[1], 100) : 3);
            using var sampler = new TelemetrySampler();
            sampler.Read();
            await Task.Delay(300);
            if (command == "snapshot")
            {
                Console.WriteLine(JsonSerializer.Serialize(sampler.Read(), Json));
                return 0;
            }
            if (command == "capture" && args.Length == 3)
            {
                int count = ParseCount(args[1], 3600);
                using var file = new StreamWriter(args[2], false, new UTF8Encoding(false));
                for (int i = 0; i < count; i++)
                {
                    await file.WriteLineAsync(JsonSerializer.Serialize(sampler.Read(), Json));
                    if (i + 1 < count) await Task.Delay(1000);
                }
                return 0;
            }
            if (command == "serve")
            {
                int seconds = args.Length > 1 ? ParseCount(args[1], 86400) : 3600;
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
                await Serve(sampler, cancellation.Token);
                return 0;
            }
            Console.Error.WriteLine("Usage: ONEXTweaks.Backend [snapshot | serve [seconds] | client [samples] | capture samples file.jsonl]");
            return 2;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message }, Json));
            return 1;
        }
    }

    private static int ParseCount(string value, int max)
    {
        if (!int.TryParse(value, out int number) || number < 1 || number > max)
            throw new ArgumentException($"Count must be between 1 and {max}.");
        return number;
    }

    private static async Task Serve(TelemetrySampler sampler, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.Out, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            Console.Error.WriteLine("{\"event\":\"pipeReady\",\"pipe\":\"" + PipeName + "\"}");
            await pipe.WaitForConnectionAsync(cancellation);
            try
            {
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
                while (pipe.IsConnected)
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(sampler.Read(), Json).AsMemory(), cancellation);
                    await Task.Delay(1000, cancellation);
                }
            }
            catch (IOException) when (!cancellation.IsCancellationRequested) { /* Client disconnected; accept a new client. */ }
        }
    }

    private static async Task<int> Client(int count)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.In, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(count + 10));
        await pipe.ConnectAsync(5000, cancellation.Token);
        using var reader = new StreamReader(pipe, Encoding.UTF8);
        long previous = 0;
        for (int i = 0; i < count; i++)
        {
            string line = await reader.ReadLineAsync(cancellation.Token) ?? throw new EndOfStreamException("Telemetry stream ended early");
            var snapshot = JsonSerializer.Deserialize<TelemetrySnapshot>(line, Json) ?? throw new InvalidDataException("Empty snapshot");
            if (snapshot.SchemaVersion != 1 || snapshot.Sequence <= previous) throw new InvalidDataException("Unsupported or out-of-order snapshot");
            previous = snapshot.Sequence;
            Console.WriteLine(line);
        }
        return 0;
    }
}
