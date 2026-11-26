// SPDX-License-Identifier: AGPL-3.0-only
using System;
namespace XboxGamingBar
{
    internal enum OnexBatteryTone { White, EnergySaverYellow, AtCapGreen }
    internal static class OnexBatteryCapPolicy
    {
        internal static OnexBatteryTone Select(double? battery,bool capKnown,int? acknowledgedCap,bool energySaver)
        {
            if(!battery.HasValue||double.IsNaN(battery.Value)||double.IsInfinity(battery.Value)||battery.Value<0||battery.Value>100)return OnexBatteryTone.White;
            if(capKnown&&acknowledgedCap.HasValue&&acknowledgedCap.Value>=30&&acknowledgedCap.Value<=100&&battery.Value>=acknowledgedCap.Value)return OnexBatteryTone.AtCapGreen;
            return energySaver?OnexBatteryTone.EnergySaverYellow:OnexBatteryTone.White;
        }
    }
}
