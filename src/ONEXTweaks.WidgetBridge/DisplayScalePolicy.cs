// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;
internal sealed record DisplayScaleValues(int Current,int Recommended,int[] Choices);
internal static class DisplayScalePolicy
{
    internal static readonly int[] Steps=[100,125,150,175,200,225,250,300,350,400,450,500];
    internal static DisplayScaleValues Decode(int minimum,int current,int maximum)
    {
        int recommended=-minimum,selected=recommended+current,last=recommended+maximum;
        if(minimum>0||maximum<0||current<minimum||current>maximum||recommended<0||recommended>=Steps.Length||selected<0||selected>=Steps.Length||last<recommended||last>=Steps.Length)throw new InvalidDataException("Windows reported an unsupported DPI scale range.");
        return new(Steps[selected],Steps[recommended],Steps.Take(last+1).ToArray());
    }
    internal static int Relative(int percent,int minimum,int current,int maximum)
    {
        var values=Decode(minimum,current,maximum);if(!values.Choices.Contains(percent))throw new ArgumentOutOfRangeException(nameof(percent),"This scale is not reported by Windows.");return Array.IndexOf(Steps,percent)+minimum;
    }
}
