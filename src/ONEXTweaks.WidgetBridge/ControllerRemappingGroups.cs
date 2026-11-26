// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;

// Operations return a validated copy before ControllerProfiles commits anything.
// Group state is independent from transport capture and preserves stored mappings.
internal static class ControllerRemappingGroups
{
 internal static bool IsStandard(string source)=>ControllerPolicy.Buttons.ContainsKey(source)||ControllerPolicy.Analog.Contains(source);
 internal static ControllerPolicy ResetGroup(ControllerPolicy policy,string group)
 {
  var bindings=new Dictionary<string,ControllerBinding>(policy.Bindings??[],StringComparer.Ordinal);
  switch(group)
  {
   case "standard":foreach(string source in bindings.Keys.Where(IsStandard).ToArray())bindings.Remove(source);return (policy with{Bindings=bindings}).Validate();
   case "oem":return (policy with{Oem=new ControllerOemPolicy()}).Validate();
   case "sticks":return (policy with{Tuning=new ControllerTuning()}).Validate();
   case "gyro":return (policy with{Gyro=null}).Validate();
   case "vibration":return (policy with{Vibration=null}).Validate();
   default:throw new InvalidDataException("Unknown controller settings group");
  }
 }
 internal static ControllerPolicy ResetBinding(ControllerPolicy policy,string source)
 {
  if(source.StartsWith("OEM:",StringComparison.Ordinal))
  {
   string id=source[4..];if(!ControllerOemPolicy.Inputs.Contains(id))throw new InvalidDataException("Unknown extra button");
   var oem=policy.Oem??new();var bindings=new Dictionary<string,ControllerBinding>(oem.Bindings??[],StringComparer.Ordinal);bindings.Remove(id);
   Dictionary<string,ControllerOemDoublePress>? doubles=oem.DoublePresses==null?null:new(oem.DoublePresses,StringComparer.Ordinal);doubles?.Remove(id);
   return (policy with{Oem=oem with{Bindings=bindings,DoublePresses=doubles}}).Validate();
  }
  if(!IsStandard(source))throw new InvalidDataException("Unknown gamepad control");
  var standard=new Dictionary<string,ControllerBinding>(policy.Bindings??[],StringComparer.Ordinal);standard.Remove(source);return (policy with{Bindings=standard}).Validate();
 }
}
