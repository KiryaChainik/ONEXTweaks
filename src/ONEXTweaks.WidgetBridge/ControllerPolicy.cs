// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Immutable;
using System.Text.Json.Serialization;
namespace ONEXTweaks.WidgetBridge;
internal readonly record struct ControllerFrame(ushort Buttons=0,byte Lt=0,byte Rt=0,short Lx=0,short Ly=0,short Rx=0,short Ry=0,ushort Extras=0,ushort Oem=0);
internal sealed record ControllerBinding(string Kind="default",string Target="",int[]? Keys=null,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]ControllerMacro? Macro=null,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]int[]? Delays=null);
internal sealed record ControllerTuning(int LeftDeadzone=0,int RightDeadzone=0,bool InvertLx=false,bool InvertLy=false,bool InvertRx=false,bool InvertRy=false,bool SwapSticks=false,int TriggerThreshold=30,int LeftTriggerStart=0,int LeftTriggerEnd=100,int RightTriggerStart=0,int RightTriggerEnd=100,bool HairTriggers=false,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]bool NintendoLayout=false);
internal sealed record ControllerPolicy(Dictionary<string,ControllerBinding>? Bindings=null,ControllerTuning? Tuning=null,bool MouseMode=false,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]ControllerOemPolicy? Oem=null,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]bool UseHardwareController=false,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]bool GamepadRemappingDisabled=false,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]ControllerVibration? Vibration=null,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]KeyboardMousePolicy? KeyboardMouse=null,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]ControllerGyroPolicy? Gyro=null,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? VirtualTarget=null,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]bool ForwardMotion=false,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]bool ForwardGameLighting=false)
{
 internal string LightingContext{get;init;}="";
 internal string OutputTarget=>KeyboardMouse!=null?"Xbox360":VirtualTarget??"Xbox360";
 internal static readonly Dictionary<string,ushort> Buttons=new(StringComparer.Ordinal){{"DPadUp",1},{"DPadDown",2},{"DPadLeft",4},{"DPadRight",8},{"Start",16},{"Select",32},{"LSClick",64},{"RSClick",128},{"LB",256},{"RB",512},{"A",4096},{"B",8192},{"X",16384},{"Y",32768}};
 internal static readonly string[] Analog=["LT","RT","LSUp","LSDown","LSLeft","LSRight","RSUp","RSDown","RSLeft","RSRight"];
 internal static readonly string[] Extra=Enumerable.Range(13,12).Select(n=>"F"+n).ToArray();
 internal static readonly string[] Inputs=Buttons.Keys.Concat(Analog).Concat(Extra).ToArray();
 internal static readonly string[] PadTargets=Buttons.Keys.Concat(Analog).Concat(["Guide","TouchpadClick","TouchpadClickLeft","TouchpadClickRight"]).ToArray();
 internal static bool IsDs4TouchpadTarget(string target)=>target is "TouchpadClick" or "TouchpadClickLeft" or "TouchpadClickRight";
 internal ControllerPolicy Validate()
 {
  if(VirtualTarget is not(null or "Xbox360" or "DualShock4"))throw new InvalidDataException("Choose a verified virtual controller type");
  var oem=Oem?.Validate();if(oem==null)_=new ControllerOemPolicy().Validate();
  var t=Tuning??new();if(t.LeftDeadzone is <0 or >50||t.RightDeadzone is <0 or >50||t.TriggerThreshold is <5 or >95||t.LeftTriggerStart is <0 or >99||t.RightTriggerStart is <0 or >99||t.LeftTriggerEnd is <1 or >100||t.RightTriggerEnd is <1 or >100||t.LeftTriggerStart>=t.LeftTriggerEnd||t.RightTriggerStart>=t.RightTriggerEnd)throw new InvalidDataException("Controller thresholds are outside their supported range");
  var bindings=new Dictionary<string,ControllerBinding>(StringComparer.Ordinal);
  if(Bindings?.Count>Inputs.Length)throw new InvalidDataException("Too many controller mappings");
  foreach(var pair in Bindings??[])
  {
   var b=pair.Value;if(!Inputs.Contains(pair.Key)||b==null||b.Kind is not("default" or "disabled" or "gamepad" or "keyboard" or "mouse" or "macro" or "keySequence"))throw new InvalidDataException("Unknown controller mapping");
   if(b.Kind=="gamepad"&&!PadTargets.Contains(b.Target))throw new InvalidDataException("Unsupported virtual gamepad target");
   if(b.Kind=="mouse"&&b.Target is not("left" or "right" or "middle" or "x1" or "x2" or "wheelUp" or "wheelDown" or "wheelLeft" or "wheelRight"))throw new InvalidDataException("Unsupported mouse action");
   if(b.Kind=="keyboard"&&(b.Keys==null||b.Keys.Length is <1 or >5||b.Keys.Distinct().Count()!=b.Keys.Length||b.Keys.Any(k=>!ControllerInputEmitter.IsSupportedKey(k))))throw new InvalidDataException("Choose one to five keyboard keys");
   if(b.Kind=="keySequence")ControllerKeySequence.Validate(b);
   bindings[pair.Key]=b with{Keys=b.Keys?.ToArray(),Delays=b.Kind=="keySequence"?b.Delays!.ToArray():null,Macro=b.Kind=="macro"?(b.Macro??throw new InvalidDataException("Macro steps are missing")).Validate():null};
  }
  return this with{Bindings=bindings,Tuning=t,Oem=oem,Vibration=Vibration?.Validate(),KeyboardMouse=KeyboardMouse?.Validate(),Gyro=Gyro?.Validate()};
 }
 internal bool HasMacros=>(Bindings?.Values.Any(value=>value.Kind=="macro")??false)||(Oem?.Bindings?.Values.Any(value=>value.Kind=="macro")??false)||(Oem?.DoublePresses?.Values.Any(value=>value.Enabled&&value.Binding?.Kind=="macro")??false);
 internal ControllerBinding For(string input){
  var binding=ControllerOemPolicy.ActionInputs.Contains(input)?(Oem??new ControllerOemPolicy()).For(input):GamepadRemappingDisabled&&ControllerRemappingGroups.IsStandard(input)?new():Bindings?.GetValueOrDefault(input)??new();
  return OutputTarget!="DualShock4"&&binding.Kind=="gamepad"&&IsDs4TouchpadTarget(binding.Target)?new():binding;
 }
}
[Flags]
internal enum Ds4TouchContacts { None=0, Left=1, Right=2 }

internal sealed record ControllerOutput(ControllerFrame Pad,ImmutableHashSet<int> Keys,ImmutableHashSet<string> Mouse,int Wheel,short CursorX,short CursorY,string[] Pressed,int HorizontalWheel=0,bool Guide=false,double CursorSpeedScale=1,int CursorNudgeX=0,int CursorNudgeY=0,int RelativeMouseX=0,int RelativeMouseY=0,bool DesktopCursor=false,bool TouchpadClick=false,double VerticalWheelRate=0,double HorizontalWheelRate=0,int ScrollIntervalMilliseconds=0,bool FastDesktopScroll=false,Ds4TouchContacts TouchpadContacts=Ds4TouchContacts.None)
{
 internal const int WheelDeltaPerNotch=120;
 internal const int DesktopWheelMilliseconds=5;
 internal const int DesktopCursorMilliseconds=4;
 internal bool ContinuousScroll=>ScrollIntervalMilliseconds!=0;
 internal bool ContinuousCursor=>CursorX!=0||CursorY!=0;
 internal int DesktopInputInterval
 {
  get
  {
   if(!DesktopCursor)return 0;
   if(CursorX!=0||CursorY!=0)return DesktopCursorMilliseconds;
   return ScrollIntervalMilliseconds!=0?ScrollIntervalMilliseconds:DesktopCursorMilliseconds;
  }
 }
}
internal sealed class ControllerTranslator
{
 private HashSet<string> previous=[];private readonly ControllerMacroRunner macros=new();private readonly ControllerKeySequence sequences=new();private bool suppressSequenceStart;
 internal bool NeedsTick=>macros.NeedsTick||sequences.NeedsTick;
 internal void Reset(){previous.Clear();macros.Reset();sequences.Reset();suppressSequenceStart=true;}
 internal static short Axis(short value,int deadzone,bool invert)
 {
  if(deadzone==0&&!invert)return value;double v=value/32768.0,d=deadzone/100.0;if(Math.Abs(v)<=d)return 0;v=Math.Sign(v)*(Math.Abs(v)-d)/(1-d);if(invert)v=-v;return (short)Math.Clamp(Math.Round(v*32768),short.MinValue,short.MaxValue);
 }
 internal static ushort NintendoButtons(ushort buttons)=>(ushort)((buttons&0x0fff)|((buttons&0x1000)<<1)|((buttons&0x2000)>>1)|((buttons&0x4000)<<1)|((buttons&0x8000)>>1));
 internal static byte Trigger(byte value,int start,int end,bool hair)
 {
  if(start==0&&end==100&&!hair)return value;
  double percent=value*100.0/255;if(percent<=start)return 0;
  if(hair)return percent<Math.Max(start,2)?(byte)0:(byte)255;
  if(percent>=end)return 255;
  return (byte)Math.Clamp(Math.Round((percent-start)*255/(end-start)),0,255);
 }
 private static HashSet<string> Pressed(ControllerFrame frame,int threshold)
 {
  var pressed=ControllerPolicy.Buttons.Where(p=>(frame.Buttons&p.Value)!=0).Select(p=>p.Key).ToHashSet(StringComparer.Ordinal);
  if(frame.Lt>=threshold*255/100)pressed.Add("LT");if(frame.Rt>=threshold*255/100)pressed.Add("RT");
  void Direction(string prefix,short x,short y){if(x>18000)pressed.Add(prefix+"Right");if(x< -18000)pressed.Add(prefix+"Left");if(y>18000)pressed.Add(prefix+"Up");if(y< -18000)pressed.Add(prefix+"Down");}
  for(int n=0;n<ControllerOemPolicy.ActionInputs.Length;n++)if((frame.Oem&(1<<n))!=0)pressed.Add(ControllerOemPolicy.ActionInputs[n]);
  Direction("LS",frame.Lx,frame.Ly);Direction("RS",frame.Rx,frame.Ry);for(int n=0;n<12;n++)if((frame.Extras&(1<<n))!=0)pressed.Add(ControllerPolicy.Extra[n]);
  return pressed;
 }
 internal ControllerOutput Translate(ControllerFrame physical,ControllerPolicy policy,bool overlayOpen=false,long? milliseconds=null)
 {
  if(overlayOpen){Reset();return new(physical with{Buttons=(ushort)(physical.Buttons&0xF3FF),Extras=0,Oem=0},[],[],0,0,0,Pressed(physical,(policy.Tuning??new()).TriggerThreshold).Order().ToArray());}
  var t=policy.Tuning??new();var f=physical with{Buttons=(ushort)(physical.Buttons&0xF3FF),Lt=Trigger(physical.Lt,t.LeftTriggerStart,t.LeftTriggerEnd,t.HairTriggers),Rt=Trigger(physical.Rt,t.RightTriggerStart,t.RightTriggerEnd,t.HairTriggers),Lx=Axis(physical.Lx,t.LeftDeadzone,t.InvertLx),Ly=Axis(physical.Ly,t.LeftDeadzone,t.InvertLy),Rx=Axis(physical.Rx,t.RightDeadzone,t.InvertRx),Ry=Axis(physical.Ry,t.RightDeadzone,t.InvertRy),Extras=0,Oem=0};
  var pressed=Pressed(physical with{Lt=f.Lt,Rt=f.Rt,Lx=f.Lx,Ly=f.Ly,Rx=f.Rx,Ry=f.Ry},t.TriggerThreshold);
  var keys=ImmutableHashSet.CreateBuilder<int>();var mouse=ImmutableHashSet.CreateBuilder<string>();int wheel=0,horizontalWheel=0,dx=0,dy=0,rx=0,ry=0;bool guide=false,touchpadClick=false;
  var touchpadContacts=Ds4TouchContacts.None;
  long now=milliseconds??Environment.TickCount64;short cx=0,cy=0;
  void Gamepad(string target)
  {
     if(target=="Guide"){guide=true;return;}
     if(ControllerPolicy.IsDs4TouchpadTarget(target))
     {
      if(policy.OutputTarget=="DualShock4")
      {
       touchpadClick=true;
       touchpadContacts|=target switch
       {
        "TouchpadClickLeft"=>Ds4TouchContacts.Left,
        "TouchpadClickRight"=>Ds4TouchContacts.Right,
        _=>Ds4TouchContacts.None,
       };
      }
      return;
     }
     if(ControllerPolicy.Buttons.TryGetValue(target,out var mask))f=f with{Buttons=(ushort)(f.Buttons|mask)};
     else switch(target){case "LT":f=f with{Lt=255};return;case "RT":f=f with{Rt=255};return;case "LSUp":dy+=32768;return;case "LSDown":dy-=32768;return;case "LSLeft":dx-=32768;return;case "LSRight":dx+=32768;return;case "RSUp":ry+=32768;return;case "RSDown":ry-=32768;return;case "RSLeft":rx-=32768;return;case "RSRight":rx+=32768;return;}return;
  }
  // Remove remapped physical controls before merging any outputs, so swaps/chords are order independent.
  foreach(var input in ControllerPolicy.Inputs.Concat(ControllerOemPolicy.ActionInputs))if(policy.For(input).Kind!="default")
  {
   if(ControllerPolicy.Buttons.TryGetValue(input,out var mask))f=f with{Buttons=(ushort)(f.Buttons&~mask)};
   else f=input switch{"LT"=>f with{Lt=0},"RT"=>f with{Rt=0},"LSUp" when f.Ly>0=>f with{Ly=0},"LSDown" when f.Ly<0=>f with{Ly=0},"LSLeft" when f.Lx<0=>f with{Lx=0},"LSRight" when f.Lx>0=>f with{Lx=0},"RSUp" when f.Ry>0=>f with{Ry=0},"RSDown" when f.Ry<0=>f with{Ry=0},"RSLeft" when f.Rx<0=>f with{Rx=0},"RSRight" when f.Rx>0=>f with{Rx=0},_=>f};
  }
  if(t.SwapSticks)f=f with{Lx=f.Rx,Ly=f.Ry,Rx=f.Lx,Ry=f.Ly};
  if(policy.MouseMode){cx=f.Rx;cy=f.Ry;f=f with{Lx=0,Ly=0,Rx=0,Ry=0,Lt=0,Rt=0};if(pressed.Contains("LT")&&policy.For("LT").Kind=="default")mouse.Add("left");if(pressed.Contains("RT")&&policy.For("RT").Kind=="default")mouse.Add("right");string up=t.SwapSticks?"RSUp":"LSUp",down=t.SwapSticks?"RSDown":"LSDown";if(policy.For(up).Kind=="default"&&!previous.Contains(up)&&pressed.Contains(up))wheel++;if(policy.For(down).Kind=="default"&&!previous.Contains(down)&&pressed.Contains(down))wheel--;}
  foreach(var input in pressed)
  {
   var b=policy.For(input);switch(b.Kind)
   {
    case "keyboard":foreach(int key in b.Keys??[])keys.Add(key);break;
    case "keySequence":if(!suppressSequenceStart&&!previous.Contains(input))sequences.Start(input,b,now);break;
    case "mouse":if(b.Target.StartsWith("wheel",StringComparison.Ordinal)){if(!previous.Contains(input)){if(b.Target=="wheelLeft")horizontalWheel--;else if(b.Target=="wheelRight")horizontalWheel++;else wheel+=b.Target=="wheelUp"?1:-1;}}else mouse.Add(b.Target);break;
    case "gamepad":Gamepad(b.Target);break;
   }
  }
  sequences.Tick(keys,now);suppressSequenceStart=false;
  foreach(string target in macros.Tick(pressed,policy,now))Gamepad(target);
  f=f with{Lx=(short)Math.Clamp(f.Lx+dx,-32768,32767),Ly=(short)Math.Clamp(f.Ly+dy,-32768,32767),Rx=(short)Math.Clamp(f.Rx+rx,-32768,32767),Ry=(short)Math.Clamp(f.Ry+ry,-32768,32767)};
  if(t.NintendoLayout&&!policy.GamepadRemappingDisabled)f=f with{Buttons=NintendoButtons(f.Buttons)};
  previous=pressed;return new(f,keys.ToImmutable(),mouse.ToImmutable(),wheel,cx,cy,pressed.Order().ToArray(),horizontalWheel,guide,TouchpadClick:touchpadClick,TouchpadContacts:touchpadContacts);
 }
}
