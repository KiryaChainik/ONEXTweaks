// SPDX-License-Identifier: AGPL-3.0-only
using ONEXTweaks.Power;

namespace ONEXTweaks.WidgetBridge;
internal sealed record ProfileValues(int Pl1,int Pl2,int Boost,int Scheduling,int ClockMhz,int PowerMode,FpsPreferences Fps,int Fan,int Overlay,bool? SeparatePl2=null,DisplayPreferences? Display=null,string? ScreenProfile=null,CpuTuningPolicy? CpuTuning=null,[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]string? FanProfile=null)
{
    internal bool SeparateEnabled=>SeparatePl2??Pl2>Pl1;
    internal int EffectivePl2=>SeparateEnabled?Math.Max(Pl1,Pl2):Pl2ControlPolicy.Automatic(Pl1);
    internal void Validate(){if(Pl1 is <Pl2ControlPolicy.MinimumPl1 or >Pl2ControlPolicy.MaximumPl1||Pl2<Pl2ControlPolicy.MinimumPl1||Pl2>Pl2ControlPolicy.ManualCeiling(Pl1)||(SeparateEnabled&&Pl2<Pl1)||Boost is <0 or >6||Scheduling is <0 or >5||ClockMhz!=0&&(ClockMhz is <1000 or >6000||ClockMhz%100!=0)||PowerMode is <0 or >2||Fan is <0 or >2||Overlay is <0 or >4||Fps.Provider is not("Intel" or "RTSS")||Fps.Value is <30 or >300||ScreenProfile!=null&&(ScreenProfile.Length is <1 or >64||ScreenProfile.Any(char.IsControl)))throw new InvalidDataException("Profile contains unsupported device settings");if(FanProfile!=null&&(FanProfile.Length is <1 or >64||FanProfile.Any(char.IsControl)))throw new InvalidDataException("Invalid fan profile reference");Display?.Validate();CpuTuning?.Validate();}
    internal ProfileValues GroupFrom(string group,ProfileValues source)=>group switch{"TDP"=>this with{Pl1=source.Pl1,Pl2=source.Pl2,SeparatePl2=source.SeparatePl2},"CPUBoost"=>this with{Boost=source.Boost,Scheduling=source.Scheduling,ClockMhz=source.ClockMhz,CpuTuning=source.CpuTuning?.Empty==true?null:source.CpuTuning},"ScreenProfile"=>this with{ScreenProfile=source.ScreenProfile},"OSPowerMode"=>this with{PowerMode=source.PowerMode},"FPSLimit"=>this with{Fps=source.Fps},"Fan"=>this with{Fan=source.Fan,FanProfile=source.FanProfile},"OverlayLevel"=>this with{Overlay=source.Overlay},"Display"=>this with{Display=source.Display==null?Display:source.Display with{Mode=Display?.Mode??source.Display.Mode}},"Resolution"=>this with{Display=ModeFrom(source,false)},"RefreshRate"=>this with{Display=ModeFrom(source,true)},_=>throw new InvalidDataException("Unknown profile setting")};
    private DisplayPreferences? ModeFrom(ProfileValues source,bool refresh)
    {
        if(source.Display?.Mode is not{} incoming)return Display;
        var basis=Display??source.Display;var mode=basis.Mode??incoming;
        return basis with{Mode=refresh?mode with{Hz=incoming.Hz}:mode with{Width=incoming.Width,Height=incoming.Height}};
    }
}
internal sealed record ProfileEntry(string Id,string Name,string Path,bool Use,bool Split,ProfileValues Ac,ProfileValues Dc,DateTimeOffset ModifiedUtc,bool UseSharedPolicy=true,[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]bool AcInitialized=false,[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]string[]? LocalGroups=null);
internal sealed record ProfileDocument(int Schema,ProfileEntry Global,List<ProfileEntry> Games,Dictionary<string,bool> Flags,PerformanceSelection? Scope=null);
internal sealed record DetectedGame(int ProcessId,string Name,string Path);

internal static class ProfileResolution
{
    internal static bool HasLocalGroup(ProfileEntry entry,string group)=>entry.LocalGroups?.Contains(group,StringComparer.Ordinal)==true;
    internal static ProfileValues Resolve(ProfileDocument data, ProfileEntry entry, bool ac)
    {
        var global = ac && data.Global.Split ? data.Global.Ac : data.Global.Dc;
        var value = ac && entry.Split ? entry.Ac : entry.Dc;
        if (value.Display == null) value = value with { Display = global.Display };
        foreach (string group in PerformanceProfiles.Groups)
        {
            if (entry.Id != "global" && entry.UseSharedPolicy && !HasLocalGroup(entry, group) &&
                !data.Flags.GetValueOrDefault(group, true) && !(group == "Fan" && value.FanProfile != null) &&
                !(group == "ScreenProfile" && value.ScreenProfile != null))
                value = value.GroupFrom(group, global);
        }
        string? fanProfile = FanPreferences.SupportedId(value.FanProfile);
        if (fanProfile == null && (value.Fan is 1 or 2 ||
            entry.Id != "global" && entry.UseSharedPolicy && data.Flags.GetValueOrDefault("Fan", true)))
            fanProfile = FanPreferences.AutoId;
        return value with { Fan = FanPreferences.SupportedMode(value.Fan), FanProfile = fanProfile };
    }
}
