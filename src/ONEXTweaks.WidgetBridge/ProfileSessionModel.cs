// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;

// A PID alone is insufficient: Windows reuses it after process termination.
// Keep ticks as a JSON string so the UWP JavaScript-number boundary loses no bits.
internal sealed record ProfileSession(int ProcessId,[property:System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString|System.Text.Json.Serialization.JsonNumberHandling.WriteAsString)]long StartedUtcTicks,string Path,string Name);
