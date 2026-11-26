using System;

namespace ONEXTweaks.Power
{
    // C# 5 compatible so the privileged agent can use the Windows .NET Framework runtime.
    public static class PowerRules
    {
        public const int Minimum = Pl2ControlPolicy.MinimumPl1;
        public const int MaximumPl1 = Pl2ControlPolicy.MaximumPl1;
        public const int MaximumPl2 = Pl2ControlPolicy.ExtendedManualCeiling;
        public static void Validate(int pl1, int pl2)
        {
            if (!Pl2ControlPolicy.IsWritable(pl1, pl2))
                throw new ArgumentException(string.Format("PL1: 8–40 Вт. PL2: от PL1 до {0} Вт.", Pl2ControlPolicy.ManualCeiling(pl1)));
        }
        public static double UnitWatts(ulong units) { return Math.Pow(2, -(int)(units & 15)); }
        public static double Pl1(ulong units, ulong limits) { return (limits & 32767) * UnitWatts(units); }
        public static double Pl2(ulong units, ulong limits) { return ((limits >> 32) & 32767) * UnitWatts(units); }
        public static bool IsEnabled(ulong limits) { return (limits & 0x8000) != 0 && ((limits >> 32) & 0x8000) != 0; }
        public static bool IsLocked(ulong limits) { return (limits & (1UL << 63)) != 0; }
        public static bool CanRestore(double pl1, double pl2)
        {
            return Pl2ControlPolicy.IsWritable(pl1, pl2);
        }
        public static bool CanRestoreCaptured(double pl1, double pl2)
        {
            // Two real cold boots of the validated OXP3/BIOS5.01 exposed this
            // factory pair. Only a captured hardware state may use 25/52.
            return CanRestore(pl1, pl2) || Pl2ControlPolicy.IsFactoryReadback(pl1, pl2);
        }
    }
}
