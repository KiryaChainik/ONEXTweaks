// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json.Serialization;
namespace ONEXTweaks.WidgetBridge;

internal sealed record ControllerOemDoublePress(bool Enabled=false,int WindowMs=300,ControllerBinding? Binding=null);
internal sealed record ControllerOemPolicy(Dictionary<string,ControllerBinding>? Bindings=null,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]Dictionary<string,ControllerOemDoublePress>? DoublePresses=null)
{
 internal int MinimumPulseMs{get;init;}
 internal static readonly string[] Front=["Home","GKey","KeyboardMouse","Onex"];
 internal static readonly string[] Rear=["M1","M2"];
 internal static readonly string[] Inputs=Front.Concat(Rear).ToArray();
 internal static readonly string[] ActionInputs=Inputs.Concat(Inputs.Select(id=>id+":Double")).ToArray();
 internal ControllerOemDoublePress DoubleFor(string button)=>DoublePresses?.GetValueOrDefault(button)??new();
 internal static ControllerBinding Default(string button)=>button switch
 {
  "Home" or "GKey" or "Onex"=>new("widget"),
  "KeyboardMouse"=>new("keyboardPopup"),
  "M1" or "M2"=>new("disabled"),
  _=>throw new InvalidDataException("Unknown physical OEM button")
 };
 internal ControllerBinding For(string button)
 {
  if(button.EndsWith(":Double",StringComparison.Ordinal))return DoubleFor(button[..^7]).Binding??new("disabled");
  var binding=Bindings?.GetValueOrDefault(button);
  return binding==null||binding.Kind=="default"?Default(button):binding;
 }
 internal ControllerOemPolicy Validate()
 {
  if(Bindings?.Count>Inputs.Length)throw new InvalidDataException("Too many OEM button assignments");
  var copy=new Dictionary<string,ControllerBinding>(StringComparer.Ordinal);
  foreach(var pair in Bindings??[])
  {
   var binding=pair.Value;
   if(!Inputs.Contains(pair.Key)||binding==null||binding.Kind is not("default" or "disabled" or "widget" or "keyboardPopup" or "steamBigPicture" or "steamQuickAccess" or "inputMode" or "gamepad" or "keyboard" or "mouse" or "macro" or "keySequence"))throw new InvalidDataException("Unknown OEM button assignment");
   if(binding.Kind is "gamepad" or "keyboard" or "mouse" or "keySequence")
    _=new ControllerPolicy(new(){{"A",binding}}).Validate();
   copy[pair.Key]=binding with{Keys=binding.Keys?.ToArray(),Delays=binding.Kind=="keySequence"?binding.Delays!.ToArray():null,Macro=binding.Kind=="macro"?(binding.Macro??throw new InvalidDataException("Macro steps are missing")).Validate():null};
  }
  Dictionary<string,ControllerOemDoublePress>? doubles=null;
  if(DoublePresses!=null){if(DoublePresses.Count>Inputs.Length)throw new InvalidDataException("Too many double-press assignments");doubles=new(StringComparer.Ordinal);foreach(var pair in DoublePresses){var value=pair.Value;if(!Inputs.Contains(pair.Key)||value==null||value.WindowMs is <100 or >600)throw new InvalidDataException("Double-press window must be100–600ms");var binding=value.Binding??new("disabled");if(binding.Kind is not("disabled" or "widget" or "keyboardPopup" or "steamBigPicture" or "steamQuickAccess" or "inputMode" or "gamepad" or "keyboard" or "mouse" or "macro" or "keySequence"))throw new InvalidDataException("Unknown double-press action");if(binding.Kind is "gamepad" or "keyboard" or "mouse" or "keySequence")_=new ControllerPolicy(new(){{"A",binding}}).Validate();doubles[pair.Key]=value with{Binding=binding with{Keys=binding.Keys?.ToArray(),Delays=binding.Kind=="keySequence"?binding.Delays!.ToArray():null,Macro=binding.Kind=="macro"?(binding.Macro??throw new InvalidDataException("Macro steps are missing")).Validate():null}};}}
  var result=this with{Bindings=copy,DoublePresses=doubles};
  if(!Inputs.Any(button=>result.For(button).Kind=="widget"))throw new InvalidDataException("Keep at least one button assigned to Open ONEXTweaks");
  return result;
 }
}

internal readonly record struct ControllerOemSignal(string Button,bool Pressed,bool Pulse=false,bool Cancelled=false);
internal static class ControllerOemVendorDecoder
{
 // Protocol facts checked against OXP3 owner captures and the pinned HC source.
 // This accepts button reports only; configuration/status packets are ignored.
 internal static ControllerOemSignal? Decode(ReadOnlySpan<byte> report)
 {
  if(report.Length==65&&report[0]==0)report=report[1..];
  if(report.Length!=64||report[0]!=0xB2||report[1]!=0x3F||report[^2]!=0x3F||report[^1]!=0xB2||report[12] is not(1 or 2))return null;
  string? button=report[6] switch{0x22=>"M1",0x23=>"M2",0x24=>"Home",0x25=>"GKey",_=>null};
  return button==null?null:new(button,report[12]==1);
 }
}

// Feed only events whose raw device identity was already verified as the OEM
// keyboard. No global keyboard recording and no guessed M5/M6 aliases.
internal sealed class ControllerOemKeyboardDecoder
{
 private readonly HashSet<int> held=[];private bool onex,keyboard;
 internal ControllerOemSignal[] Feed(int key,bool pressed)
 {
  var signals=new List<ControllerOemSignal>();
  if(pressed)held.Add(key);
  bool ctrl=held.Contains(17)||held.Contains(162),win=held.Contains(91),alt=held.Contains(18)||held.Contains(164);
  if(ctrl&&win&&key==79)
  {
   if(pressed&&!keyboard){keyboard=true;signals.Add(new("KeyboardMouse",true));}
   else if(!pressed){signals.Add(keyboard?new("KeyboardMouse",false):new("KeyboardMouse",true,true));keyboard=false;}
  }
  if(ctrl&&win&&alt&&!onex){onex=true;signals.Add(new("Onex",true));}
  if(!pressed)
  {
   held.Remove(key);
   if(onex&&key is 17 or 162 or 91 or 18 or 164){onex=false;signals.Add(new("Onex",false));}
   if(keyboard&&key is 17 or 162 or 91){keyboard=false;signals.Add(new("KeyboardMouse",false));}
  }
  return signals.ToArray();
 }
 internal void Reset(){held.Clear();onex=false;keyboard=false;}
}
