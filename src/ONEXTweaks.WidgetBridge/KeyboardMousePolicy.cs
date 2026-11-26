// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;

internal sealed record KeyboardMousePolicy(int Schema=1,Dictionary<string,ControllerBinding>? Bindings=null,Dictionary<string,ControllerBinding>? FnBindings=null,string? FnButton=null,int MouseSpeed=100,bool MouseAcceleration=true,int ScrollSpeed=100,bool AdaptiveScroll=true,ControllerBinding? StartDouble=null,ControllerBinding? FnStartDouble=null,int StickDeadzone=10,int HoldAcceleration=0,int NudgeStep=10)
{
 internal static readonly string[] Inputs=ControllerPolicy.Buttons.Keys.Concat(ControllerPolicy.Analog).Concat(ControllerOemPolicy.Inputs.Select(id=>"OEM:"+id)).ToArray();
 internal static ControllerBinding Default(string source)=>source switch
 {
  "A"=>new("keyboard",Keys:[32]),"B"=>new("keyboard",Keys:[27]),"X"=>new("keyboard",Keys:[13]),"Y"=>new("keyboard",Keys:[8]),
  "Select"=>new("keyboard",Keys:[18,9]),"Start"=>new("keyboard",Keys:[91]),"LB"=>new("keyboard",Keys:[9]),"LT"=>new("keyboard",Keys:[160]),
  "RB"=>new("mouse","left"),"RT"=>new("mouse","right"),"LSClick"=>new("mouse","middle"),"RSClick"=>new("keyboard",Keys:[162]),
  "DPadUp"=>new("keyboard",Keys:[38]),"DPadDown"=>new("keyboard",Keys:[40]),"DPadLeft"=>new("keyboard",Keys:[37]),"DPadRight"=>new("keyboard",Keys:[39]),
  "LSUp"=>new("scroll","up"),"LSDown"=>new("scroll","down"),"LSLeft"=>new("scroll","left"),"LSRight"=>new("scroll","right"),
  "RSUp"=>new("cursor","up"),"RSDown"=>new("cursor","down"),"RSLeft"=>new("cursor","left"),"RSRight"=>new("cursor","right"),
  "OEM:KeyboardMouse"=>new("keyboardPopup"),"OEM:Onex"=>new("keyboard",Keys:[17,16,27]),"OEM:Home" or "OEM:GKey"=>new("widget"),
  "OEM:M1" or "OEM:M2"=>new("disabled"),_=>throw new InvalidDataException("Unknown keyboard/mouse control")
 };
 internal ControllerBinding For(string source,bool fn=false)
 {
  if(source==FnButton)return new("fn");
  return fn&&FnBindings?.TryGetValue(source,out var layer)==true?layer:Bindings?.GetValueOrDefault(source)??Default(source);
 }
 internal ControllerBinding DoubleStart(bool fn)=>fn?FnStartDouble??StartDouble??new("keyboard",Keys:[91,9]):StartDouble??new("keyboard",Keys:[91,9]);
 internal bool SameBindings(KeyboardMousePolicy previous)
 {
  static bool Same(ControllerBinding a,ControllerBinding b)=>a.Kind==b.Kind&&a.Target==b.Target&&
   (a.Keys??[]).SequenceEqual(b.Keys??[])&&(a.Delays??[]).SequenceEqual(b.Delays??[]);
  if(FnButton!=previous.FnButton)return false;
  foreach(bool fn in new[]{false,true})
  {
   if(!Same(DoubleStart(fn),previous.DoubleStart(fn)))return false;
   foreach(string source in Inputs)if(!Same(For(source,fn),previous.For(source,fn)))return false;
  }
  return true;
 }
 internal KeyboardMousePolicy Validate()
 {
  if(Schema!=1||MouseSpeed is <1 or >400||ScrollSpeed is <25 or >300||StickDeadzone is <0 or >20||HoldAcceleration is <0 or >100||NudgeStep is <1 or >50||FnButton!=null&&!new[]{"OEM:Home","OEM:GKey","OEM:M1","OEM:M2"}.Contains(FnButton))throw new InvalidDataException("Invalid keyboard/mouse settings");
  Dictionary<string,ControllerBinding> Copy(Dictionary<string,ControllerBinding>? source)
  {
   if(source?.Count>Inputs.Length)throw new InvalidDataException("Too many keyboard/mouse bindings");var result=new Dictionary<string,ControllerBinding>(StringComparer.Ordinal);
   foreach(var pair in source??[]){ValidateBinding(pair.Key,pair.Value);result[pair.Key]=pair.Value with{Keys=pair.Value.Keys?.ToArray(),Delays=pair.Value.Delays?.ToArray()};}return result;
  }
  var doubleStart=StartDouble??new("keyboard",Keys:[91,9]);var next=this with{Bindings=Copy(Bindings),FnBindings=Copy(FnBindings),StartDouble=doubleStart with{Keys=doubleStart.Keys?.ToArray(),Delays=doubleStart.Delays?.ToArray()},FnStartDouble=FnStartDouble is {} twice?twice with{Keys=twice.Keys?.ToArray(),Delays=twice.Delays?.ToArray()}:null};
  ValidateBinding("Start",next.StartDouble!);if(next.FnStartDouble!=null)ValidateBinding("Start",next.FnStartDouble);
  foreach(bool fn in new[]{false,true})if(!ControllerOemPolicy.Inputs.Any(id=>next.For("OEM:"+id,fn).Kind=="widget"))throw new InvalidDataException("Keep one extra button assigned to Open ONEXTweaks in each layer");
  return next;
 }
 private static void ValidateBinding(string source,ControllerBinding binding)
 {
  if(!Inputs.Contains(source)||binding==null||binding.Kind is not("disabled" or "keyboard" or "keySequence" or "mouse" or "combo" or "cursor" or "scroll" or "nudge" or "widget" or "keyboardPopup" or "security"))throw new InvalidDataException("Choose a keyboard or mouse action");
  if(binding.Kind=="keySequence")ControllerKeySequence.Validate(binding);
  if(binding.Kind is "keyboard" or "combo")
  {
   _=new ControllerPolicy(new(){{"A",new("keyboard",Keys:binding.Keys)}}).Validate();
   var keys=binding.Keys!;if(keys.Any(key=>key is 17 or 162 or 163)&&keys.Any(key=>key is 18 or 164 or 165)&&keys.Contains(46))throw new InvalidDataException("Use Windows Security for Ctrl+Alt+Delete; ordinary simulated keys cannot invoke it");
  }
  if(binding.Kind is "mouse" or "combo"&&binding.Target is not("left" or "right" or "middle" or "x1" or "x2" or "wheelUp" or "wheelDown" or "wheelLeft" or "wheelRight"))throw new InvalidDataException("Unknown mouse action");
  if(binding.Kind is "cursor" or "scroll" or "nudge"&&binding.Target is not("up" or "down" or "left" or "right"))throw new InvalidDataException("Unknown movement direction");
  if(binding.Kind is "widget" or "keyboardPopup" or "security"&&!source.StartsWith("OEM:",StringComparison.Ordinal))throw new InvalidDataException("System actions are available on extra buttons");
 }
}

internal sealed class KeyboardMouseSettings
{
 private readonly string file;private KeyboardMousePolicy value;private int revision;internal string Generation{get;}=Guid.NewGuid().ToString("N");
 internal KeyboardMousePolicy Value=>Volatile.Read(ref value);internal int Revision=>Volatile.Read(ref revision);
 internal KeyboardMouseSettings(string reports)
 {file=Path.Combine(reports,"..","keyboard-mouse-settings.json");value=(File.Exists(file)?JsonSerializer.Deserialize<KeyboardMousePolicy>(File.ReadAllText(file),Backend.Program.Json)??throw new InvalidDataException("Keyboard/mouse settings unreadable"):new()).Validate();if(!File.Exists(file))Save(value);}
 private void Save(KeyboardMousePolicy next){string temp=file+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(next,Backend.Program.Json));File.Move(temp,file,true);Volatile.Write(ref value,next);Interlocked.Increment(ref revision);}
 internal void Set(KeyboardMousePolicy next)=>Save(next.Validate());
}
