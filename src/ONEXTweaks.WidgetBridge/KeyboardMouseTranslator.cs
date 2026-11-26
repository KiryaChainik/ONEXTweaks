// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Immutable;
namespace ONEXTweaks.WidgetBridge;

internal sealed class KeyboardMouseTranslator
{
 private readonly HashSet<string> blocked=[],previous=[];private bool initialized,lastFn,overlayWasOpen,holdLeftStick,holdRightStick;private readonly StartTap start=new();private double lastCursorX,lastCursorY;private long cursorBegan,cursorAt;
 private readonly Dictionary<string,double> magnitudes=new(StringComparer.Ordinal);
 private KeyboardMousePolicy? bindingPolicy;
 private readonly ControllerKeySequence sequences=new();
 private readonly Dictionary<string,ControllerBinding> baseBindings=new(StringComparer.Ordinal),fnBindings=new(StringComparer.Ordinal);
 internal void Reset(){blocked.Clear();previous.Clear();initialized=overlayWasOpen=holdLeftStick=holdRightStick=false;lastCursorX=lastCursorY=0;cursorBegan=cursorAt=0;start.Reset();sequences.Reset();}
 private static double CurveCursor(double radius)=>.35*radius+.65*Math.Pow(radius,1.7);
 private static bool StickCentered(short x,short y,int deadzone)=>((double)x*x+(double)y*y)<=Math.Pow(32768*deadzone/100.0,2);
 private sealed class StartTap
 {
  private int phase;private bool previous;private long due,began;
  internal void Reset(){phase=0;previous=false;due=began=0;}
  internal (bool Single,bool Double) Tick(bool held,long now)
  {
   bool down=held&&!previous,up=!held&&previous;previous=held;
   if(phase==5&&now>=due)phase=0;
   if(phase==6&&now>=due)phase=0;
   if(phase==1&&now>=due){phase=3;began=now;}
   if(phase==2&&down){phase=4;began=now;}
   else if(phase==2&&now>=due){phase=5;due=now+80;}
   if(phase==0&&down){phase=1;due=now+300;}
   if(phase==1&&up){phase=2;due=now+300;}
   else if(phase==3&&up){if(now-began<80){phase=5;due=began+80;}else phase=0;}
   else if(phase==4&&up){if(now-began<80){phase=6;due=began+80;}else phase=0;}
   return(phase is 3 or 5,phase is 4 or 6);
  }
 }
 internal ControllerOutput Translate(ControllerFrame frame,KeyboardMousePolicy policy,double seconds,long now,ushort rawOem=0,bool overlayOpen=false)
 {
  if(overlayOpen)
  {
   if(!overlayWasOpen)Reset();
   overlayWasOpen=true;
   holdLeftStick=!StickCentered(frame.Lx,frame.Ly,policy.StickDeadzone);
   holdRightStick=!StickCentered(frame.Rx,frame.Ry,policy.StickDeadzone);
   return new(frame with{Buttons=(ushort)(frame.Buttons&0xF3FF),Extras=0,Oem=0},[],[],0,0,0,[]);
  }
  if(!ReferenceEquals(bindingPolicy,policy))
  {
   foreach(string input in KeyboardMousePolicy.Inputs){baseBindings[input]=policy.For(input);fnBindings[input]=policy.For(input,true);}
   bindingPolicy=policy;
  }
  magnitudes.Clear();
  bool fn=policy.FnButton!=null&&(rawOem&(1<<Array.IndexOf(ControllerOemPolicy.Inputs,policy.FnButton[4..])))!=0;
  var bindings=fn?fnBindings:baseBindings;
  foreach(var pair in ControllerPolicy.Buttons)if((frame.Buttons&pair.Value)!=0)magnitudes[pair.Key]=1;
  if(frame.Lt>=51)magnitudes["LT"]=1;if(frame.Rt>=51)magnitudes["RT"]=1;
  string? leftCurve=null,rightCurve=null;
  double scrollDeflection=0;
  string? Axis(string prefix,short x,short y)
  {
   if(x==0&&y==0)return null;
   var horizontal=bindings[prefix+"Right"];
   string? paired=horizontal.Kind is "cursor" or "scroll" && horizontal.Target=="right" &&
    bindings[prefix+"Left"] is {} left && left.Kind==horizontal.Kind && left.Target=="left" &&
    bindings[prefix+"Up"] is {} up && up.Kind==horizontal.Kind && up.Target=="up" &&
    bindings[prefix+"Down"] is {} down && down.Kind==horizontal.Kind && down.Target=="down"?horizontal.Kind:null;
   double X,Y;
   if(paired is "cursor" or "scroll")
   {
    double length=Math.Sqrt((double)x*x+(double)y*y)/32768.0,deadzone=policy.StickDeadzone/100.0;
    double radius=Math.Clamp((length-deadzone)/(1-deadzone),0,1);
    if(radius==0)return paired;
    X=x/(length*32768);Y=y/(length*32768);
    double speed;
    if(paired=="cursor")
    {
     double remaining=1-radius;
     // Quicker fine motion just outside the deadzone without an initial jump.
     // The correction fades before the outer stick range.
     speed=policy.MouseAcceleration
      ? CurveCursor(radius)+.38*radius*remaining*remaining*remaining*remaining
      : radius;
    }
    else
    {
     scrollDeflection=Math.Max(scrollDeflection,radius);
     // Restrict side drift on nearly axial strokes without changing the total
     // scroll speed as a stick moves around its circular edge.
     const double sideDriftThreshold=.33, sideDriftBlend=.15;
     if(Math.Abs(X)<Math.Abs(Y))X*=Math.Clamp((Math.Abs(X/Y)-sideDriftThreshold)/sideDriftBlend,0,1);
     else if(Math.Abs(Y)<Math.Abs(X))Y*=Math.Clamp((Math.Abs(Y/X)-sideDriftThreshold)/sideDriftBlend,0,1);
     double directionLength=Math.Sqrt(X*X+Y*Y);
     X/=directionLength;Y/=directionLength;
     speed=policy.AdaptiveScroll?Math.Pow(radius,1.5):1;
    }
    X*=speed;Y*=speed;
   }
   else
   {
    X=ControllerTranslator.Axis(x,policy.StickDeadzone,false)/32768.0;
    Y=ControllerTranslator.Axis(y,policy.StickDeadzone,false)/32768.0;
   }
   if(X!=0)magnitudes[prefix+(X>0?"Right":"Left")]=Math.Abs(X);if(Y!=0)magnitudes[prefix+(Y>0?"Up":"Down")]=Math.Abs(Y);
   return paired;
  }
  leftCurve=Axis("LS",frame.Lx,frame.Ly);rightCurve=Axis("RS",frame.Rx,frame.Ry);
  for(int n=0;n<6;n++)if((frame.Oem&(1<<n))!=0)magnitudes["OEM:"+ControllerOemPolicy.Inputs[n]]=1;
  if(!initialized||fn!=lastFn)
  {
   blocked.UnionWith(magnitudes.Keys.Where(input=>bindings[input].Kind is not ("cursor" or "scroll")));
   if(!overlayWasOpen)
   {
    holdLeftStick=!StickCentered(frame.Lx,frame.Ly,policy.StickDeadzone);
    holdRightStick=!StickCentered(frame.Rx,frame.Ry,policy.StickDeadzone);
   }
   start.Reset();sequences.Reset();lastCursorX=lastCursorY=0;initialized=true;lastFn=fn;cursorBegan=now;
  }
  overlayWasOpen=false;
  if(holdLeftStick&&StickCentered(frame.Lx,frame.Ly,policy.StickDeadzone))holdLeftStick=false;
  if(holdRightStick&&StickCentered(frame.Rx,frame.Ry,policy.StickDeadzone))holdRightStick=false;
  blocked.RemoveWhere(id=>!magnitudes.ContainsKey(id));
  var keys=ImmutableHashSet.CreateBuilder<int>();var mouse=ImmutableHashSet.CreateBuilder<string>();double cursorX=0,cursorY=0,rateX=0,rateY=0;int wheel=0,horizontal=0,nudgeX=0,nudgeY=0;
  void Apply(string source,ControllerBinding binding,double magnitude,bool first)
  {
   if(binding.Kind is "cursor" or "scroll" && source is not ("LSClick" or "RSClick") &&
    (holdLeftStick&&source.StartsWith("LS",StringComparison.Ordinal)||holdRightStick&&source.StartsWith("RS",StringComparison.Ordinal)))return;
   if(binding.Kind=="keySequence"&&first)sequences.Start(source,binding,now);
   if(binding.Kind is "keyboard" or "combo")foreach(int key in binding.Keys??[])keys.Add(key);
   if(binding.Kind is "mouse" or "combo")
   {
    if(binding.Target.StartsWith("wheel",StringComparison.Ordinal)){if(first){if(binding.Target=="wheelLeft")horizontal--;else if(binding.Target=="wheelRight")horizontal++;else wheel+=binding.Target=="wheelUp"?1:-1;}}
    else mouse.Add(binding.Target);
   }
   if(binding.Kind=="nudge"&&first){if(binding.Target=="up")nudgeY-=policy.NudgeStep;else if(binding.Target=="down")nudgeY+=policy.NudgeStep;else if(binding.Target=="left")nudgeX-=policy.NudgeStep;else nudgeX+=policy.NudgeStep;}
   if(binding.Kind=="cursor")
   {bool radial=source.StartsWith("LS",StringComparison.Ordinal)&&leftCurve=="cursor"||source.StartsWith("RS",StringComparison.Ordinal)&&rightCurve=="cursor";
    double amount=policy.MouseAcceleration&&!radial?CurveCursor(magnitude):magnitude;if(binding.Target=="up")cursorY+=amount;else if(binding.Target=="down")cursorY-=amount;else if(binding.Target=="left")cursorX-=amount;else cursorX+=amount;}
   if(binding.Kind=="scroll")
   {
    bool radial=source.StartsWith("LS",StringComparison.Ordinal)&&leftCurve=="scroll"||source.StartsWith("RS",StringComparison.Ordinal)&&rightCurve=="scroll";
    double amount=radial?magnitude:policy.AdaptiveScroll?Math.Pow(magnitude,1.5):1;
    if(binding.Target=="up")rateY+=amount;else if(binding.Target=="down")rateY-=amount;else if(binding.Target=="left")rateX-=amount;else rateX+=amount;
   }
  }
  foreach(var pair in magnitudes)
  {if(pair.Key=="Start"||blocked.Contains(pair.Key))continue;Apply(pair.Key,bindings[pair.Key],pair.Value,!previous.Contains(pair.Key));}
  var tap=start.Tick(magnitudes.ContainsKey("Start")&&!blocked.Contains("Start"),now);
  if(tap.Single)Apply("Start",policy.For("Start",fn),1,!previous.Contains("Start:Single"));if(tap.Double)Apply("Start",policy.DoubleStart(fn),1,!previous.Contains("Start:Double"));
  sequences.Tick(keys,now);
  int scrollInterval=rateX!=0||rateY!=0?ControllerOutput.DesktopWheelMilliseconds:0;
  double rate=8*ControllerOutput.WheelDeltaPerNotch*policy.ScrollSpeed/100.0;
  previous.Clear();previous.UnionWith(magnitudes.Keys);if(tap.Single)previous.Add("Start:Single");if(tap.Double)previous.Add("Start:Double");
  double length=Math.Sqrt(cursorX*cursorX+cursorY*cursorY);if(length>1){cursorX/=length;cursorY/=length;}
  if(cursorX*lastCursorX+cursorY*lastCursorY<=0||now<cursorAt||now-cursorAt>1000)cursorBegan=now;
  lastCursorX=cursorX;lastCursorY=cursorY;cursorAt=now;
  double boost=1+policy.HoldAcceleration/100.0*Math.Clamp((now-cursorBegan)/1000.0,0,1);
  return new(new(),keys.ToImmutable(),mouse.ToImmutable(),wheel,(short)Math.Clamp(Math.Round(cursorX*32767),-32768,32767),(short)Math.Clamp(Math.Round(cursorY*32767),-32768,32767),magnitudes.Keys.Order().ToArray(),horizontal,false,policy.MouseSpeed/100.0*boost,nudgeX,nudgeY,DesktopCursor:true,
   VerticalWheelRate:rateY*rate,HorizontalWheelRate:rateX*rate,ScrollIntervalMilliseconds:scrollInterval,
   FastDesktopScroll:scrollDeflection>=.85&&Math.Sqrt(rateX*rateX+rateY*rateY)*rate>=720);
 }
}
