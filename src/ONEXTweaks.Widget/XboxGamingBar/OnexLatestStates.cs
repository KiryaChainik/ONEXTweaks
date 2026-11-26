// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Generic;
namespace XboxGamingBar
{
    // Only unsolicited snapshots use this buffer. Command acknowledgements bypass it.
    internal sealed class OnexLatestStates
    {
        private readonly object gate=new object();
        private readonly Dictionary<string,string> pending=new Dictionary<string,string>();
        private readonly Dictionary<string,string> applied=new Dictionary<string,string>();
        private readonly HashSet<string> inFlight=new HashSet<string>();
        private static readonly string[] firstPaintKeys =
        {
            "OnexPowerAvailable", "OnexHardwareHomeState", "MediaLevels", "OnexProfilesState"
        };
        internal int PendingCount { get { lock(gate)return pending.Count; } }
        internal long Replaced { get; private set; }
        internal long Duplicates { get; private set; }
        internal void Put(string key,string value)
        {
            lock(gate)
            {
                if(applied.TryGetValue(key,out var previous)&&previous==value&&!pending.ContainsKey(key)&&!inFlight.Contains(key)){Duplicates++;return;}
                if(pending.ContainsKey(key))Replaced++;
                pending[key]=value;
            }
        }
        internal KeyValuePair<string,string>[] Take(int count)
        {
            lock(gate)
            {
                var result=new List<KeyValuePair<string,string>>();
                foreach (string key in firstPaintKeys)
                {
                    if (result.Count == count) break;
                    if (!pending.TryGetValue(key, out string value)) continue;
                    result.Add(new KeyValuePair<string,string>(key, value));
                }
                foreach (var pair in pending)
                {
                    if (result.Count == count) break;
                    if (System.Array.IndexOf(firstPaintKeys, pair.Key) < 0) result.Add(pair);
                }
                foreach(var pair in result){pending.Remove(pair.Key);inFlight.Add(pair.Key);}
                return result.ToArray();
            }
        }
        internal void Complete(string key,string value,bool cache)
        {lock(gate){if(!inFlight.Remove(key))return;if(cache)applied[key]=value;else applied.Remove(key);}}
        internal void Invalidate(){lock(gate)applied.Clear();}
        // A fresh command reply supersedes queued telemetry. An old in-flight batch
        // must not restore a cached value after the reply updated a control.
        internal void ResetAfterReply(){lock(gate){pending.Clear();applied.Clear();inFlight.Clear();}}
        // A read-only reply cannot supersede unrelated hardware/profile events.
        // Preserve their pending latest value and duplicate-suppression cache.
        internal void ResetAfterReadReply(IEnumerable<string> keys)
        {lock(gate)foreach(string key in keys){pending.Remove(key);applied.Remove(key);inFlight.Remove(key);}}
        private static readonly HashSet<string> scopedReads=new HashSet<string>{
            "OnexKeyboardMouseGet","OnexStartupGet","OnexOverlayGet","OnexOverlayPresetsGet",
            "OnexProfilesGet","OnexDisplayProfilesGet","OnexFanProfilesGet","OnexControllerGet",
            "OnexControllerDiagnosticGet","OnexHardwareHomeGet","OnexDisplayGet","OnexFpsGet",
            "OnexWindowsPowerGet","OnexChargeGet","OnexPowerGet","OnexAirplaneGet",
            "OnexWifiGet","OnexAwakeGet","OnexFseGet","GetMediaLevels",
            "OnexWidgetReady","OnexWidgetVisible"};
        internal static bool IsScopedRead(IEnumerable<string> keys)
        {
            var request=new HashSet<string>(keys);
            return request.Count==1&&scopedReads.Contains(new List<string>(request)[0])||
                request.Count==2&&request.Contains("OnexOverlayPreviewGet")&&request.Contains("OnexOverlayPreviewConfig");
        }
    }
}
