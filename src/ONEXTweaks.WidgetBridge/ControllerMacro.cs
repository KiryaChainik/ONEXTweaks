// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;
internal sealed record ControllerMacro(string[]? Buttons=null,string Mode="once",int IntervalMs=80,int PressMs=40)
{
 internal ControllerMacro Validate()
 {
  if(Buttons==null||Buttons.Length is <1 or >32||Buttons.Any(button=>!ControllerPolicy.PadTargets.Contains(button)))throw new InvalidDataException("Choose1–32 supported gamepad steps");
  if(Mode is not("once" or "repeatHeld" or "holdToggle" or "repeatToggle"))throw new InvalidDataException("Unknown macro activation mode");
  if(IntervalMs is <10 or >500||PressMs is <10 or >500)throw new InvalidDataException("Macro interval and press duration must be10–500ms");
  return this with{Buttons=Buttons.ToArray()};
 }
}
internal sealed class ControllerMacroRunner
{
 private sealed class Player
 {
  internal bool Running,Down;internal int Step;internal long Due;
  internal void Stop(){Running=false;Down=false;Step=0;Due=0;}
  internal void Start(long now,ControllerMacro macro){Running=true;Down=true;Step=0;Due=now+Math.Min(macro.PressMs,macro.IntervalMs);}
 }
 private readonly Dictionary<string,Player> players=[];private readonly HashSet<string> previous=[],blocked=[];private bool initialized;private long last;
 internal bool NeedsTick=>players.Values.Any(player=>player.Running);
 internal void Reset(){players.Clear();previous.Clear();blocked.Clear();initialized=false;last=0;}
 internal string[] Tick(HashSet<string> pressed,ControllerPolicy policy,long now)
 {
  if(initialized&&(now<last||now-last>1500&&NeedsTick))Reset();last=now;
  var assigned=ControllerPolicy.Inputs.Concat(ControllerOemPolicy.ActionInputs).Where(source=>policy.For(source).Kind=="macro").ToHashSet(StringComparer.Ordinal);
  if(!initialized){blocked.UnionWith(pressed);initialized=true;}
  blocked.RemoveWhere(source=>!pressed.Contains(source));var output=new List<string>();
  foreach(string stale in players.Keys.Except(assigned).ToArray())players.Remove(stale);
  foreach(string source in assigned)
  {
   var macro=policy.For(source).Macro!;if(!players.TryGetValue(source,out var player))players[source]=player=new();
   bool held=pressed.Contains(source)&&!blocked.Contains(source),rising=held&&!previous.Contains(source);
   if(macro.Mode=="holdToggle"){if(rising)player.Running=!player.Running;if(player.Running)output.AddRange(macro.Buttons!);continue;}
   if(macro.Mode=="repeatHeld"&&!held){player.Stop();continue;}
   if(rising){if(macro.Mode=="repeatToggle"){if(player.Running)player.Stop();else player.Start(now,macro);}else if(!player.Running)player.Start(now,macro);}
   if(!player.Running)continue;
   if(now>=player.Due)
   {
    // One phase per sample: never skip a release or burst missed steps after a stall.
    if(player.Down){player.Down=false;player.Due=now+Math.Max(8,macro.IntervalMs-Math.Min(macro.PressMs,macro.IntervalMs));}
    else{player.Step++;if(player.Step>=macro.Buttons!.Length){if(macro.Mode=="once"){player.Stop();continue;}player.Step=0;}player.Down=true;player.Due=now+Math.Min(macro.PressMs,macro.IntervalMs);}
   }
   if(player.Running&&player.Down)output.Add(macro.Buttons![player.Step]);
  }
  previous.Clear();previous.UnionWith(pressed);return output.ToArray();
 }
}
