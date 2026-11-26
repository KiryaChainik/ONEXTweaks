// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal sealed partial class ControllerProfiles
{
 private readonly KeyboardMouseSettings? keyboardMouse;
 private bool keyboardMouseActive;private int keyboardModeRevision,lastRuntimeKeyboardRevision=-1;
 private KeyboardMousePolicy? lastOemKeyboardPolicy;
 internal bool KeyboardMouseActive=>Volatile.Read(ref keyboardMouseActive);
 private int KeyboardRevision=>(keyboardMouse?.Revision??0)+Volatile.Read(ref keyboardModeRevision);
 internal ControllerOemPolicy KeyboardOem(ControllerOemPolicy? original)
 {
  var source=original??new ControllerOemPolicy();if(keyboardMouse==null)return source.Validate();
  if(KeyboardMouseActive)
  {
   var config=keyboardMouse.Value;
   source=new(ControllerOemPolicy.Inputs.ToDictionary(id=>id,id=>config.For("OEM:"+id).Kind=="widget"?new ControllerBinding("widget"):new ControllerBinding("disabled"))){MinimumPulseMs=80};
  }
  var doubles=(source.DoublePresses??[]).ToDictionary(pair=>pair.Key,pair=>pair.Value,StringComparer.Ordinal);
  doubles["KeyboardMouse"]=new(true,300,new("inputMode"));
  return (source with{DoublePresses=doubles}).Validate();
 }
 internal ControllerBinding ResolveOemAction(string id)
 {
  if(keyboardMouse==null)return CurrentOem.For(id);
  if(id=="KeyboardMouse:Double")return new("inputMode");
  if(!KeyboardMouseActive)return id=="KeyboardMouse"&& !store.OemEnabled?new("keyboardPopup"):CurrentOem.For(id);
  if(id.EndsWith(":Double",StringComparison.Ordinal))return new("disabled");
  var config=keyboardMouse.Value;bool fn=config.FnButton!=null&&((oemButtons?.HeldMask??0)&(1<<Array.IndexOf(ControllerOemPolicy.Inputs,config.FnButton[4..])))!=0;
  var value=config.For("OEM:"+id,fn);return value.Kind is "widget" or "keyboardPopup" or "security"?value:new("disabled");
 }
 internal void NeutralizeSystemChord(string source){if(KeyboardMouseActive&&source is "KeyboardMouse" or "Onex")ControllerInputEmitter.ReleaseRecorded([91,164,162],[]);}
 internal object ReadKeyboardMouse()=>new{available=keyboardMouse!=null,generation=keyboardMouse?.Generation,modeRevision=Volatile.Read(ref keyboardModeRevision),mode=KeyboardMouseActive?"keyboardMouse":"gamepad",policy=keyboardMouse?.Value,baseBindings=keyboardMouse==null?null:KeyboardMousePolicy.Inputs.ToDictionary(id=>id,id=>keyboardMouse.Value.For(id)),fnBindings=keyboardMouse==null?null:KeyboardMousePolicy.Inputs.ToDictionary(id=>id,id=>keyboardMouse.Value.For(id,true)),startDouble=keyboardMouse?.Value.DoubleStart(false),fnStartDouble=keyboardMouse?.Value.DoubleStart(true),revision=keyboardMouse?.Revision??0,keyboardDoubleReserved=keyboardMouse!=null,activeGamepadProfileId=Active.Id,activeGamepadProfileName=Active.Name,runtime=runtime.Read(),error=modeError,oemRuntime=oemManager?.Read(),securityAvailable=KeyboardMouseWindowsSecurity.Available(),fnActive=keyboardMouse?.Value.FnButton is {} fn&&((oemButtons?.HeldMask??0)&(1<<Array.IndexOf(ControllerOemPolicy.Inputs,fn[4..])))!=0};
 internal async Task SetKeyboardMouseMode(bool active)
 {
  if(keyboardMouse==null)throw new IOException("Keyboard/mouse mode is unavailable");
  if(active==KeyboardMouseActive)return;
  try
  {
   if(active){if(oemManager==null)throw new IOException("The extra-button listener is unavailable");await oemManager.Prepare();}
   ResetCaptureRetries();Volatile.Write(ref keyboardMouseActive,active);Interlocked.Increment(ref keyboardModeRevision);Refresh();await MaintainRuntime(true);
   if(!active&&!store.OemEnabled&&oemManager!=null)await oemManager.Release();
  }
  catch{Volatile.Write(ref keyboardMouseActive,false);Interlocked.Increment(ref keyboardModeRevision);Refresh();await runtime.Stop();captureWasRunning=false;if(!store.OemEnabled&&oemManager!=null)await oemManager.Release();throw;}
 }
 internal async Task MutateKeyboardMouse(JsonElement change)
 {
  if(keyboardMouse==null)throw new IOException("Keyboard/mouse mode is unavailable");
  if(change.TryGetProperty("expectedRevision",out var expected)&&expected.GetInt32()!=keyboardMouse.Revision)throw new InvalidOperationException("Keyboard/mouse settings changed; stale update refused");
  string operation=change.GetProperty("operation").GetString()??"";
  if(operation=="mode"){string mode=change.GetProperty("mode").GetString()??"";if(mode is not("gamepad" or "keyboardMouse"))throw new InvalidDataException("Unknown input mode");await SetKeyboardMouseMode(mode=="keyboardMouse");return;}
  if(operation=="resetPrecision"){keyboardMouse.Set(keyboardMouse.Value with{StickDeadzone=10,HoldAcceleration=0,NudgeStep=10});Refresh();await MaintainRuntime(true);return;}
  if(operation=="resetBinding"){var policy=keyboardMouse.Value;string source=change.GetProperty("source").GetString()??"",layer=change.GetProperty("layer").GetString()??"";if(!KeyboardMousePolicy.Inputs.Contains(source)||layer is not("base" or "fn"))throw new InvalidDataException("Unknown source/layer");bool twice=change.TryGetProperty("double",out var dual)&&dual.GetBoolean();if(twice){if(source!="Start")throw new InvalidDataException("Only Start has an editable double action");policy=layer=="fn"?policy with{FnStartDouble=null}:policy with{StartDouble=null};}else{var bindings=(layer=="fn"?policy.FnBindings:policy.Bindings)?.ToDictionary(pair=>pair.Key,pair=>pair.Value)??[];bindings.Remove(source);policy=layer=="fn"?policy with{FnBindings=bindings}:policy with{Bindings=bindings};if(policy.FnButton==source)policy=policy with{FnButton=null};}keyboardMouse.Set(policy);Refresh();await MaintainRuntime(true);return;}
  if(operation is "bind" or "fn" or "tuning")
  {
   var policy=keyboardMouse.Value;
   if(operation=="fn"){string? source=change.GetProperty("source").GetString();policy=policy with{FnButton=string.IsNullOrEmpty(source)?null:source};}
   else if(operation=="tuning")
   {
    string key=change.GetProperty("key").GetString()??"";var value=change.GetProperty("value");policy=key switch{"mouseSpeed"=>policy with{MouseSpeed=value.GetInt32()},"mouseAcceleration"=>policy with{MouseAcceleration=value.GetBoolean()},"scrollSpeed"=>policy with{ScrollSpeed=value.GetInt32()},"adaptiveScroll"=>policy with{AdaptiveScroll=value.GetBoolean()},"stickDeadzone"=>policy with{StickDeadzone=value.GetInt32()},"holdAcceleration"=>policy with{HoldAcceleration=value.GetInt32()},"nudgeStep"=>policy with{NudgeStep=value.GetInt32()},_=>throw new InvalidDataException("Unknown keyboard/mouse setting")};
   }
   else
   {
    string source=change.GetProperty("source").GetString()??"",layer=change.GetProperty("layer").GetString()??"";if(!KeyboardMousePolicy.Inputs.Contains(source)||layer is not("base" or "fn"))throw new InvalidDataException("Unknown keyboard/mouse source/layer");var binding=change.GetProperty("binding").Deserialize<ControllerBinding>(Backend.Program.Json)??throw new InvalidDataException("Missing keyboard/mouse binding");bool twice=change.TryGetProperty("double",out var dual)&&dual.GetBoolean();
    if(twice){if(source!="Start")throw new InvalidDataException("Only Start has an editable double action; Keyboard double switches mode");policy=layer=="fn"?policy with{FnStartDouble=binding}:policy with{StartDouble=binding};}
    else{var bindings=(layer=="fn"?policy.FnBindings:policy.Bindings)?.ToDictionary(pair=>pair.Key,pair=>pair.Value,StringComparer.Ordinal)??new(StringComparer.Ordinal);bindings[source]=binding;policy=layer=="fn"?policy with{FnBindings=bindings}:policy with{Bindings=bindings};}
   }
   keyboardMouse.Set(policy);Refresh();await MaintainRuntime(true);return;
  }
  if(operation=="edit")keyboardMouse.Set(change.GetProperty("policy").Deserialize<KeyboardMousePolicy>(Backend.Program.Json)??throw new InvalidDataException("Keyboard/mouse settings are missing"));
  else if(operation=="reset")keyboardMouse.Set(new());else throw new InvalidDataException("Unknown keyboard/mouse operation");
  Refresh();await MaintainRuntime(true);
 }
}
