// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;

internal sealed class PowerSourceChangedException : InvalidOperationException
{
    internal PowerSourceChangedException() : base("Power source changed. Repeat the adjustment on the current profile.") { }

    internal static void Require(bool? expected, bool actual)
    {
        if (expected.HasValue && expected.Value != actual) throw new PowerSourceChangedException();
    }

    internal static bool? ExpectedFrom(JsonElement request)
    {
        if (!request.TryGetProperty("OnexPowerOnAc", out var source)) return null;
        if (source.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Power source must be a boolean.");
        return source.GetBoolean();
    }
}
// This receipt belongs to one explicit request. Never infer its facts from
// the shared hardware-write counter, an exception, or another request.
internal sealed class PowerCommitOutcome877
{
    internal bool HardwareConfirmed { get; private set; }
    internal bool PreferencesSaved { get; private set; }
    internal bool ProfileSaved { get; private set; }
    internal void HardwareAcknowledged() => HardwareConfirmed=true;
    internal void PreferencesPersisted() { if(HardwareConfirmed)PreferencesSaved=true; }
    internal void ProfilePersisted() { if(HardwareConfirmed&&PreferencesSaved)ProfileSaved=true; }
    internal string DescribeFailure(Exception failure)
    {
        if(!HardwareConfirmed)return failure.Message;
        string phase=!PreferencesSaved?"saving power preferences":!ProfileSaved?"completing performance-profile persistence":"preparing the reply";
        return "Power limits were applied; "+phase+" failed: "+failure.Message;
    }
}
