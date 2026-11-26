// SPDX-License-Identifier: AGPL-3.0-only
using System;

namespace ONEXTweaks.Power
{
    public static class Pl2ControlPolicy
    {
        public const int MinimumPl1 = 8;
        public const int MaximumPl1 = 40;
        public const int StandardManualCeiling = 41;
        public const int ExtendedManualCeiling = 52;

        public static int Automatic(int pl1)
        {
            return pl1 + 1;
        }

        public static int ManualCeiling(int pl1)
        {
            return pl1 == MaximumPl1 ? ExtendedManualCeiling : StandardManualCeiling;
        }

        public static int AdjustManual(int pl1, int requested)
        {
            if (pl1 < MaximumPl1 && requested > StandardManualCeiling)
                return Automatic(pl1);
            return Math.Max(pl1, Math.Min(ManualCeiling(pl1), requested));
        }

        public static bool IsWritable(double pl1, double pl2)
        {
            return pl1 == Math.Truncate(pl1) && pl2 == Math.Truncate(pl2)
                && pl1 >= MinimumPl1 && pl1 <= MaximumPl1
                && pl2 >= pl1 && pl2 <= ManualCeiling((int)pl1);
        }

        public static bool IsFactoryReadback(double pl1, double pl2)
        {
            return pl1 == 25 && pl2 == 52;
        }
    }
}
