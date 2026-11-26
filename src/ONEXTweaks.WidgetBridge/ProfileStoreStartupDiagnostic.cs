using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace ONEXTweaks.WidgetBridge;

internal enum ProfileStoreStartupFailure
{
    InvalidJson,
    EmptyDocument,
    InvalidProfileDocument,
    InterruptedSave,
    StoreIoFailure,
}

internal static class ProfileStoreStartupDiagnostic
{
    internal const int MaxBytes = 512;

    internal static string PathForAttempt(string reportDirectory, int pid, long startTicks)
    {
        return Path.Combine(reportDirectory, FormattableString.Invariant($"profile-store-startup-failure-{pid}-{startTicks}.json"));
    }

    internal static void TryWrite(string reportDirectory, ProfileStoreStartupFailure failure)
    {
        string? stagedPath = null;
        bool owned = false;
        try
        {
            using var process = Process.GetCurrentProcess();
            long processStartUtcTicks = process.StartTime.ToUniversalTime().Ticks;
            DateTimeOffset observedUtc = DateTimeOffset.UtcNow;
            if (processStartUtcTicks <= 0 || processStartUtcTicks > observedUtc.UtcTicks)
                return;

            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema = 1,
                phase = "profile-store-startup",
                reason = failure.ToString(),
                pid = process.Id,
                processStartUtcTicks = processStartUtcTicks.ToString(CultureInfo.InvariantCulture),
                observedUtc,
            });
            if (bytes.Length > MaxBytes)
                return;

            string target = PathForAttempt(reportDirectory, process.Id, processStartUtcTicks);
            // CreateNew serializes publishers for this attempt until the stage is moved or removed.
            stagedPath = target + ".tmp";
            using (var stream = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                owned = true;
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(stagedPath, target, overwrite: false);
            owned = false;
        }
        catch (Exception failureToReport) when (failureToReport is IOException or UnauthorizedAccessException or
                                               Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // Keep the original load failure and its input files intact.
        }
        finally
        {
            if (owned)
            {
                try { File.Delete(stagedPath!); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
