// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;

internal sealed record PerformanceSelectable(string Id,string? ProcessPath,bool Automatic);
internal sealed record PerformanceSelection(string ActiveId="global",string Selection="auto",ProfileSession? ManualHold=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]string? LastManualId=null);

// Pure selection policy for the next Performance workflow. Persistence, actuator
// changes and candidate-process access stay in its owner, under the shared gate.
// Not connected to the live Performance manager until the migration/UI is ready.
internal static class PerformanceSelectionPolicy
{
    internal static void Validate(PerformanceSelection state,IReadOnlyList<PerformanceSelectable> entries)
    {
        if(entries.Count is <1 or >513||entries.Any(entry=>string.IsNullOrWhiteSpace(entry.Id)||entry.Id.Length>64||entry.Id.Any(char.IsControl))||
           entries.Select(entry=>entry.Id).Distinct(StringComparer.Ordinal).Count()!=entries.Count||
           entries.Count(entry=>entry.Id=="global")!=1||entries.Single(entry=>entry.Id=="global").ProcessPath!=null||
           !entries.Any(entry=>entry.Id==state.ActiveId)||state.Selection is not("auto" or "manual"))
            throw new InvalidDataException("Invalid Performance profile selection");
        var bound=entries.Where(entry=>!string.IsNullOrEmpty(entry.ProcessPath)).ToArray();
        if(bound.Any(entry=>!Path.IsPathFullyQualified(entry.ProcessPath!)||!entry.ProcessPath!.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))||
           bound.Select(entry=>entry.ProcessPath!).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=bound.Length)
            throw new InvalidDataException("Duplicate or invalid Performance process binding");
        if(state.ManualHold!=null&&(state.Selection!="manual"||state.ManualHold.ProcessId<=0||state.ManualHold.StartedUtcTicks<=0||!Path.IsPathFullyQualified(state.ManualHold.Path)))
            throw new InvalidDataException("Invalid manual process-session override");
    }
    private static bool IsManualBase(string? id,IReadOnlyList<PerformanceSelectable> entries)=>
        entries.Any(entry=>entry.Id==id&&string.IsNullOrEmpty(entry.ProcessPath));

    internal static string ManualBase(PerformanceSelection state,IReadOnlyList<PerformanceSelectable> entries)
    {
        if(IsManualBase(state.LastManualId,entries))return state.LastManualId!;
        return state.Selection=="manual"&&IsManualBase(state.ActiveId,entries)?state.ActiveId:"global";
    }

    internal static PerformanceSelection Select(PerformanceSelection previous,string id,ProfileSession? current,IReadOnlyList<PerformanceSelectable> entries)
    {
        var result=new PerformanceSelection(id,"manual",current,IsManualBase(id,entries)?id:ManualBase(previous,entries));
        Validate(result,entries);return result;
    }
    internal static PerformanceSelection Tick(PerformanceSelection state,ProfileSession? current,Func<ProfileSession,bool> alive,IReadOnlyList<PerformanceSelectable> entries)
    {
        Validate(state,entries);
        // Switching foreground windows, opening Game Bar or another live app does
        // not revoke a human choice while the overridden process is still alive.
        if(state.ManualHold!=null&&alive(state.ManualHold))return state;
        var match=current==null?null:entries.FirstOrDefault(entry=>entry.Automatic&&entry.ProcessPath?.Equals(current.Path,StringComparison.OrdinalIgnoreCase)==true);
        string fallback=ManualBase(state,entries);
        string? savedBase=fallback=="global"&&state.LastManualId==null?null:fallback;
        if(match!=null)return new(match.Id,"auto",null,savedBase);
        if(state.Selection=="manual"&&state.ManualHold==null)
            return state;
        return new(fallback,fallback=="global"?"auto":"manual",null,savedBase);
    }
}
