// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Generic;

namespace XboxGamingBar
{
    internal static class OnexProfileWriteGate
    {
        // Overlay and controller requests validate their own state. They are available
        // even when the performance or display catalog is still loading.
        private static readonly HashSet<string> ProfileMutations = new HashSet<string>
        {
            "OnexProfilesSet", "OnexDisplayProfilesSet", "OnexDisplaySet", "OnexFanProfilesSet",
            "OnexFanSet", "OnexCpuSettings", "OnexCpuTuningSet", "OnexCpuClockSet",
            "OnexBoostSet", "OnexWindowsPowerMode", "OnexFpsSet"
        };

        internal static bool RequiresProfileSnapshot(IEnumerable<string> keys)
        {
            return ProfileMutations.Overlaps(keys);
        }
    }
}
