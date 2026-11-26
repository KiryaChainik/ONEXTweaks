// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json.Nodes;
using ONEXTweaks.Fans;
using System.Text.Json.Serialization;
namespace ONEXTweaks.WidgetBridge;
internal sealed record FanCurve(int At45,int At55,int At65,int At75,int At85,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]int? At25=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]int? At95=null)
{
    internal int[] Points()=>[At25??At45,At45,At55,At65,At75,At85,At95??FanCurvePolicy.MaximumDuty];
    internal void Validate()
    {
        try { FanCurvePolicy.Validate(Points()); }
        catch(ArgumentException error) { throw new InvalidDataException(error.Message,error); }
    }
    internal static FanCurve FromPoints(int[] points)
    {
        var current=FanCurvePolicy.Expand(points);
        return new FanCurve(current[1],current[2],current[3],current[4],current[5],current[0],current[6]);
    }
}
internal sealed record FanPreferences(int Mode,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]FanCurve? Curve=null)
{
    internal const string AutoId = "firmware-0";
    internal static bool IsRetiredPreset(string? id) => id is "firmware-1" or "firmware-2";
    internal static string? SupportedId(string? id) => IsRetiredPreset(id) ? AutoId : id;
    internal static int SupportedMode(int mode) => mode is 1 or 2 ? 0 : mode;
    internal FanPreferences WithSupportedMode() => Mode is 1 or 2 ? this with { Mode = 0 } : this;

    internal void Validate(){if(Mode is <0 or >3||Mode==3&&Curve==null)throw new InvalidDataException("Unsupported ONEX fan mode");Curve?.Validate();}
    internal static FanPreferences Capture(JsonNode? fan)
    {
        int mode=fan?["profile"]?.GetValue<int>()??0;
        var value=new FanPreferences(mode,mode==3?FanCurve.FromPoints(fan?["customCurve"]?.AsArray().Select(point=>point!.GetValue<int>()).ToArray()??[]):null);value.Validate();return value;
    }
}

internal sealed record FanParentContext(string Id,string Name,FanPreferences Seed,bool Active);
