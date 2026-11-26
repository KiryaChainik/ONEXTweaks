// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;
internal static class DisplayModeOrientationPolicy
{
    internal static bool ValidSize(int width,int height)=>Math.Min(width,height)>=360&&Math.Max(width,height)>=640&&width<=8000&&height<=8000;
    internal static DisplayModeValue PreserveOrientation(DisplayModeValue desired,DisplayModeValue current)
        =>desired.Width!=desired.Height&&current.Width!=current.Height&&(desired.Width>desired.Height)!=(current.Width>current.Height)?desired with{Width=desired.Height,Height=desired.Width}:desired;
}
