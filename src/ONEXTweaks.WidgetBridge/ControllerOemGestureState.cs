// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;

// Pure clock-driven recognizer. The owner serializes access. No timers, Windows
// input, firmware writes or user settings are accessed here.
internal sealed class ControllerOemGestureState
{
 private enum Phase { FirstDown, FirstUp, SingleHeld, DoubleHeld }
 private sealed record Pending(Phase Phase,long Due);
 private readonly HashSet<string> physical=[],blocked=[],active=[];
 private readonly Dictionary<string,Pending> pending=[];
 private readonly Dictionary<string,long> pulses=[],started=[];
 private ControllerOemPolicy? policy;private bool enabled;private long? clock;
 internal string? LastButton{get;private set;}internal string? LastGesture{get;private set;}internal long Sequence{get;private set;}internal long LastAt{get;private set;}
 internal ushort Mask{get{ushort mask=0;for(int n=0;n<ControllerOemPolicy.ActionInputs.Length;n++)if(active.Contains(ControllerOemPolicy.ActionInputs[n]))mask|=(ushort)(1<<n);return mask;}}
 internal object Read()=>new{lastButton=LastButton,lastGesture=LastGesture,sequence=Sequence,lastAt=LastAt,pending=pending.Count,active=active.Order().ToArray()};
 internal ControllerOemSignal[] Update(ControllerOemPolicy current,bool on,long now)
 {
  if(ReferenceEquals(policy,current)&&enabled==on)return [];
  var releases=Cancel();policy=current;enabled=on;clock=now;LastButton=null;LastGesture=null;return releases;
 }
 internal ControllerOemSignal[] Reconfigure(ControllerOemPolicy current,bool on,long now,IEnumerable<string> rawHeld)
 {physical.Clear();physical.UnionWith(rawHeld);policy=null;return Update(current,on,now);}
 internal ControllerOemSignal[] Cancel()
 {
  var releases=active.Select(id=>new ControllerOemSignal(id,false,Cancelled:true)).ToArray();active.Clear();pending.Clear();pulses.Clear();started.Clear();blocked.Clear();blocked.UnionWith(physical);return releases;
 }
 private void Output(string button,bool twice,bool down,long now,List<ControllerOemSignal> events)
 {
  string id=twice?button+":Double":button;if(down&&active.Contains(id)){pulses.Remove(id);started[id]=now;LastButton=button;LastGesture=twice?"Double":"Single";LastAt=now;Sequence++;return;}bool changed=down?active.Add(id):active.Remove(id);if(!changed)return;
  events.Add(new(id,down));if(down){started[id]=now;LastButton=button;LastGesture=twice?"Double":"Single";LastAt=now;Sequence++;}else started.Remove(id);
 }
 internal ControllerOemSignal[] Tick(long now)
 {
  if(clock.HasValue&&(now<clock.Value||now-clock.Value>1500)){clock=now;return Cancel();}clock=now;
  var events=new List<ControllerOemSignal>();
  foreach(string id in pulses.Where(pair=>now>=pair.Value).Select(pair=>pair.Key).ToArray()){pulses.Remove(id);if(active.Remove(id)){started.Remove(id);events.Add(new(id,false));}}
  if(!enabled)return events.ToArray();
  foreach(var pair in pending.Where(pair=>pair.Value.Due<=now).ToArray())
  {
   if(pair.Value.Phase==Phase.FirstDown){pending[pair.Key]=new(Phase.SingleHeld,long.MaxValue);Output(pair.Key,false,true,now,events);}
   else if(pair.Value.Phase==Phase.FirstUp){pending.Remove(pair.Key);Output(pair.Key,false,true,now,events);pulses[pair.Key]=now+80;}
  }
  return events.ToArray();
 }
 internal ControllerOemSignal[] Feed(ControllerOemSignal signal,long now)
 {
  if(!ControllerOemPolicy.Inputs.Contains(signal.Button))return [];
  if(signal.Cancelled){physical.Remove(signal.Button);blocked.Remove(signal.Button);return [];}
  var events=Tick(now).ToList();bool changed=signal.Pressed?physical.Add(signal.Button):physical.Remove(signal.Button);if(!changed)return events.ToArray();
  if(!enabled){if(!signal.Pressed)blocked.Remove(signal.Button);return events.ToArray();}
  if(blocked.Contains(signal.Button)){if(!signal.Pressed)blocked.Remove(signal.Button);return events.ToArray();}
  string button=signal.Button;var settings=policy!.DoubleFor(button);
  if(signal.Pressed)
  {
   if(!settings.Enabled){pending[button]=new(Phase.SingleHeld,long.MaxValue);Output(button,false,true,now,events);}
   else if(pending.TryGetValue(button,out var previous)&&previous.Phase==Phase.FirstUp){pending[button]=new(Phase.DoubleHeld,long.MaxValue);Output(button,true,true,now,events);}
   else pending[button]=new(Phase.FirstDown,now+settings.WindowMs);
  }
  else if(pending.TryGetValue(button,out var previous))
  {
   if(previous.Phase==Phase.FirstDown)pending[button]=new(Phase.FirstUp,now+settings.WindowMs);
   else if(previous.Phase is Phase.SingleHeld or Phase.DoubleHeld){pending.Remove(button);string id=previous.Phase==Phase.DoubleHeld?button+":Double":button;int minimum=settings.Enabled?80:policy!.MinimumPulseMs;if(minimum>0&&started.TryGetValue(id,out long downAt)&&now-downAt<minimum)pulses[id]=downAt+minimum;else Output(button,previous.Phase==Phase.DoubleHeld,false,now,events);}
  }
  return events.ToArray();
 }
}
