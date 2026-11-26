// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using ONEXTweaks.Power;
namespace ONEXTweaks.WidgetBridge;
internal static class ProfileSourcePatch
{
    internal static string Group(string key)=>key switch
    {
        "pl1" or "pl2" or "separatePl2"=>"TDP",
        "boost" or "scheduling" or "clockMhz" or "cpuReset"=>"CPUBoost",
        "powerMode"=>"OSPowerMode",
        "fpsProvider" or "fpsEnabled" or "fpsValue"=>"FPSLimit",
        "fanProfile"=>"Fan",
        "mode"=>"Resolution",
        "frameGeneration" or "vrr" or "vrrMode" or "lowLatency" or "frameSync" or "scalingMode" or "scalingMethod"=>"Display",
        _ when key.StartsWith("cpu:",StringComparison.Ordinal)&&CpuTuningPolicy.Fields.Any(field=>field.Alias==key[4..])=>"CPUBoost",
        _=>throw new InvalidDataException("Unknown source-scoped profile field")
    };
    private static ProfileValues SetPl1(ProfileValues current, int pl1)
    {
        int pl2 = current.SeparateEnabled || current.Pl2 > Pl2ControlPolicy.ManualCeiling(pl1)
            ? Pl2ControlPolicy.AdjustManual(pl1, current.Pl2) : current.Pl2;
        return current with { Pl1 = pl1, Pl2 = pl2, SeparatePl2 = current.SeparateEnabled };
    }

    internal static ProfileValues Apply(ProfileValues current,string key,JsonElement value)
    {
        _=Group(key);var display=current.Display??new DisplayPreferences();
        var next=key switch
        {
            "pl1"=>SetPl1(current,value.GetInt32()),
            "pl2"=>current with{Pl2=value.GetInt32()},
            "separatePl2"=>current with{SeparatePl2=value.GetBoolean(),Pl2=value.GetBoolean()?Pl2ControlPolicy.AdjustManual(current.Pl1,current.Pl2):current.Pl2},
            "boost"=>current with{Boost=value.GetInt32()},
            "scheduling"=>current with{Scheduling=value.GetInt32()},
            "clockMhz"=>current with{ClockMhz=value.GetInt32()},
            "cpuReset"=>current with{CpuTuning=null,Boost=0},
            "powerMode"=>current with{PowerMode=value.GetInt32()},
            "fpsProvider"=>current with{Fps=current.Fps with{Provider=value.GetString()??""}},
            "fpsEnabled"=>current with{Fps=current.Fps with{Enabled=value.GetBoolean()}},
            "fpsValue"=>current with{Fps=current.Fps with{Value=value.GetInt32()}},
            "fanProfile"=>current with{FanProfile=value.GetString()},
            "mode"=>current with{Display=display with{Mode=JsonSerializer.Deserialize<DisplayModeValue>(value,Backend.Program.Json)??throw new InvalidDataException("Missing mode")}},
            "frameGeneration"=>current with{Display=display with{FrameGeneration=value.GetInt32()}},
            "vrr"=>current with{Display=display with{Vrr=value.GetInt32()}},
            "vrrMode"=>current with{Display=display with{VrrMode=value.GetInt32()}},
            "lowLatency"=>current with{Display=display with{LowLatency=value.GetInt32()}},
            "frameSync"=>current with{Display=display with{FrameSync=value.GetInt32()}},
            "scalingMode"=>current with{Display=display with{ScalingMode=value.GetInt32(),ScalingMethod=value.GetInt32()==2&&display.ScalingMethod>1?0:display.ScalingMethod}},
            "scalingMethod"=>current with{Display=display with{ScalingMethod=value.GetInt32()}},
            _=>current with{CpuTuning=(current.CpuTuning??new()).With(key[4..],value.ValueKind==JsonValueKind.Null?null:value.GetInt32())}
        };
        if(next.CpuTuning?.Empty==true)next=next with{CpuTuning=null};next.Validate();return next;
    }
}
