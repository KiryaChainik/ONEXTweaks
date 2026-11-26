// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Immutable;
namespace ONEXTweaks.WidgetBridge;

internal sealed class ControllerKeySequence
{
 private const int TapMilliseconds=32;
 private sealed class Playback(ControllerBinding binding,long now)
 {
  internal readonly ControllerBinding Binding=binding;
  internal int Step;
  internal bool Down=true;
  internal long Due=now+TapMilliseconds;
 }

 private readonly Dictionary<string,Playback> active=new(StringComparer.Ordinal);
 private readonly List<string> finished=[];
 internal bool NeedsTick=>active.Count!=0;

 internal static void Validate(ControllerBinding binding)
 {
  if(binding.Keys is not { Length:>=1 and <=3 } keys||
     keys.Distinct().Count()!=keys.Length||keys.Any(key=>!ControllerInputEmitter.IsSupportedKey(key))||
     binding.Delays is not {} delays||delays.Length!=keys.Length-1||delays.Any(delay=>delay<0))
   throw new InvalidDataException("Choose one to three different keys and a nonnegative delay between each pair");
 }

 internal void Start(string source,ControllerBinding binding,long now)
 {
  if(!active.ContainsKey(source))active.Add(source,new(binding,now));
 }

 internal void Reset()=>active.Clear();

 internal void Tick(ImmutableHashSet<int>.Builder keys,long now)
 {
  finished.Clear();
  foreach(var pair in active)
  {
   var play=pair.Value;
   if(now>=play.Due)
   {
    if(play.Down)
    {
     if(play.Step==play.Binding.Keys!.Length-1){finished.Add(pair.Key);continue;}
     play.Down=false;
     play.Due=now+play.Binding.Delays![play.Step];
    }
    else
    {
     play.Step++;
     play.Down=true;
     play.Due=now+TapMilliseconds;
    }
   }
   if(play.Down)keys.Add(play.Binding.Keys![play.Step]);
  }
  foreach(string source in finished)active.Remove(source);
 }
}
