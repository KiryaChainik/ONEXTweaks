// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;
/// <summary>Game Bar's generic DS4 navigation omits X. Relay one recoverable context-key pulse per press.</summary>
internal sealed class ControllerDs4NavigationRelay
{
 private bool held;private long releaseAt;
 internal bool NeedsTick=>releaseAt!=0;
 internal void Reset(){held=true;releaseAt=0;}
 internal ControllerOutput Apply(ControllerOutput value,bool ds4AndRedirected,long now){
  if(!ds4AndRedirected){held=(value.Pad.Buttons&0x4000)!=0;releaseAt=0;return value;}
  bool down=(value.Pad.Buttons&0x4000)!=0;if(down&&!held)releaseAt=now+80;held=down;
  bool pulse=down&&now<releaseAt;if(!down||now>=releaseAt)releaseAt=0;
  return value with{Pad=value.Pad with{Buttons=(ushort)(value.Pad.Buttons&~0x4000)},Keys=pulse?value.Keys.Add(93):value.Keys};
 }
}
