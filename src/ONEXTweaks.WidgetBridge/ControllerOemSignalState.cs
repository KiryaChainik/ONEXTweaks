// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;

internal sealed class ControllerOemSignalState
{
 private readonly Dictionary<string,HashSet<string>> held=[];
 private readonly Dictionary<(string Button,string Origin),long> pulses=[];
 private readonly Dictionary<string,(string Origin,long Seen)> preferred=[];
 internal string[] Held=>held.Where(pair=>pair.Value.Count>0).Select(pair=>pair.Key).Order().ToArray();
 internal ushort Mask{get{ushort value=0;for(int n=0;n<ControllerOemPolicy.Inputs.Length;n++)if(held.TryGetValue(ControllerOemPolicy.Inputs[n],out var origins)&&origins.Count>0)value|=(ushort)(1<<n);return value;}}
 internal ushort RemapMask(bool enabled)=>enabled?Mask:(ushort)0;
 internal ControllerOemSignal? Apply(ControllerOemSignal signal,string origin,long now,bool aliases)
 {
  if(!ControllerOemPolicy.Inputs.Contains(signal.Button))return null;
  if(!held.TryGetValue(signal.Button,out var owners))held[signal.Button]=owners=[];
  if(aliases&&signal.Button is "M1" or "M2" or "Home" or "GKey")
  {
   if(preferred.TryGetValue(signal.Button,out var previous)&&previous.Origin!=origin&&(now-previous.Seen<=500||owners.Count>0))return null;
   if(signal.Pressed||preferred.TryGetValue(signal.Button,out previous)&&previous.Origin==origin)preferred[signal.Button]=(origin,now);
  }
  bool before=owners.Count>0;if(signal.Pressed)owners.Add(origin);else owners.Remove(origin);bool after=owners.Count>0;
  var key=(signal.Button,origin);if(signal.Pulse)pulses[key]=now+80;else if(!signal.Pressed)pulses.Remove(key);
  return before==after?null:signal with{Pressed=after};
 }
 internal ControllerOemSignal[] Expire(long now)
 {
  var output=new List<ControllerOemSignal>();foreach(var key in pulses.Where(pair=>now>=pair.Value).Select(pair=>pair.Key).ToArray()){var change=Apply(new(key.Button,false),key.Origin,now,false);if(change!=null)output.Add(change.Value);}return output.ToArray();
 }
 internal ControllerOemSignal[] ReleaseSource(string origin,long now)
 {
  var output=new List<ControllerOemSignal>();foreach(string button in held.Where(pair=>pair.Value.Contains(origin)).Select(pair=>pair.Key).ToArray()){var change=Apply(new(button,false),origin,now,false);if(change!=null)output.Add(change.Value);}
  foreach(string button in preferred.Where(pair=>pair.Value.Origin==origin).Select(pair=>pair.Key).ToArray())preferred.Remove(button);
  foreach(var key in pulses.Keys.Where(key=>key.Origin==origin).ToArray())pulses.Remove(key);
  return output.ToArray();
 }
 internal ControllerOemSignal[] Reset()
 {var output=Held.Select(button=>new ControllerOemSignal(button,false)).ToArray();held.Clear();pulses.Clear();preferred.Clear();return output;}
}
