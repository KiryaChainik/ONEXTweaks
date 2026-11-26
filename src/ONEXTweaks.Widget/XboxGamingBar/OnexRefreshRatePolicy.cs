// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
namespace XboxGamingBar
{
    internal static class OnexRefreshRatePolicy
    {
        internal static readonly int[] MenuRates={30,45,60,90,120,144};
        internal static int Next(int current,IEnumerable<int> supported)
        {
            int next=current==60?144:60;
            if(current<=0)throw new InvalidOperationException("Windows has not reported the current refresh rate.");
            if(supported==null||!supported.Contains(next))throw new InvalidOperationException(next+" Hz is not reported by Windows for the current resolution. Hold the tile to choose an available rate.");
            return next;
        }
    }
}
