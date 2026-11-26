// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json.Nodes;

namespace ONEXTweaks.WidgetBridge;

internal sealed class FanConnectionRecovery
{
    private const string ChannelReplaced = "The OEM channel changed before the command could be sent";
    private const string LeaseExpired = "Returned to Auto: overlay telemetry stopped";
    private static readonly TimeSpan HealthyInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumSampleAge = TimeSpan.FromSeconds(5);
    private string? previousSelection;
    private DateTimeOffset? healthySince;
    private DateTimeOffset? lastSample;
    private bool armed;

    internal bool ShouldRestore(string selection, FanPreferences desired, JsonNode? observed, DateTimeOffset now)
    {
        if (selection != previousSelection)
        {
            previousSelection = selection;
            armed = false;
            healthySince = null;
            lastSample = null;
        }
        if (desired.Mode != 3 || observed?["available"]?.GetValue<bool>() != true ||
            !DateTimeOffset.TryParse(observed["rpmSampleUtc"]?.GetValue<string>(), out var sampled))
        {
            healthySince = null;
            return false;
        }
        if (sampled > now || now - sampled > MaximumSampleAge)
        {
            healthySince = null;
            return false;
        }
        if (sampled <= lastSample)
            return false;
        if (sampled - lastSample > MaximumSampleAge)
            healthySince = null;
        lastSample = sampled;

        string? error = observed["error"]?.GetValue<string>();
        if (string.IsNullOrEmpty(error) && observed["percent"]?.GetValue<int>() is >= 0 and <= 100 &&
            Program.FanStateMatches(observed, desired))
        {
            healthySince ??= sampled;
            if (sampled - healthySince.Value >= HealthyInterval)
                armed = true;
            return false;
        }

        healthySince = null;
        // Consume recovery before writing. A rejected duty or another loss before the
        // curve has settled must leave the EC in firmware Auto.
        if (!Program.FanStateMatches(observed, new FanPreferences(0)))
            return false;
        bool restore = armed && CanRecoverFrom(observed);
        armed = false;
        return restore;
    }

    internal static bool CanRecoverFrom(JsonNode? fan)
        => (fan?["error"]?.GetValue<string>() is ChannelReplaced or LeaseExpired) &&
            Program.FanStateMatches(fan, new FanPreferences(0));
}
