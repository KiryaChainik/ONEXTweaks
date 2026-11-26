// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;
// IGCL SupportedTypes uses the gaming-flip flag values themselves, not 1 << value.
// Intel's public igcl_api.h: ctl_gaming_flip_mode_flag_t and ctl_property_info_enum_t.
internal static class IntelSyncPolicy
{
    internal static readonly int[] Values=[1,2,4,8,16,32];
    internal static bool Supports(ulong mask,int value)=>value>0&&(mask&(ulong)value)==(ulong)value;
    internal static int[] Choices(ulong mask)=>Enumerable.Range(0,Values.Length).Where(index=>Supports(mask,Values[index])).ToArray();
    internal static int Resolve(int index,ulong mask)
    {
        if(index<0||index>=Values.Length)throw new ArgumentOutOfRangeException(nameof(index));
        if(!Supports(mask,Values[index]))throw new InvalidOperationException("This Intel Sync mode is not supported by the current graphics driver.");
        return Values[index];
    }
}
