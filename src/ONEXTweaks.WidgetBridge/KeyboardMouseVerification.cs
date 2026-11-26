// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class KeyboardMouseVerification
{
 private sealed class Capture:IControllerCaptureRuntime
 {
  internal ControllerPolicy? Applied;private bool running;internal int Starts,Stops;
  public ControllerRuntimeState Read()=>new(running,false,0,running?1:-1,null,[],0);
  public void Policy(ControllerPolicy next)=>Applied=next.Validate();public Task Start(){running=true;Starts++;return Task.CompletedTask;}public Task Stop(){running=false;Stops++;return Task.CompletedTask;}public ValueTask DisposeAsync(){running=false;return ValueTask.CompletedTask;}
 }
 private sealed class Oem:IControllerOemRuntime
 {public bool Prepared{get;private set;}public object Read()=>new{prepared=Prepared};public Task Prepare(){Prepared=true;return Task.CompletedTask;}public Task Release(){Prepared=false;return Task.CompletedTask;}}
 internal static async Task<int> Models(string file)
 {
  int checks=0;void Check(bool value,string message){checks++;if(!value)throw new IOException(message);}
  try
  {
   var fast=new ControllerOemGestureState();fast.Update(new ControllerOemPolicy(){MinimumPulseMs=80}.Validate(),true,0);fast.Feed(new("Onex",true),10);fast.Feed(new("Onex",false),11);Check((fast.Mask&8)!=0,"Short OEM system chord disappeared before keyboard/mouse poll");fast.Tick(90);Check(fast.Mask==0,"Minimum OEM pulse did not release");
   var defaults=new KeyboardMousePolicy().Validate();Check(defaults.For("OEM:Onex").Kind=="keyboard"&&defaults.For("OEM:Onex").Keys!.Order().SequenceEqual(new[]{16,17,27}),"ONEX must directly invoke Task Manager");Check(KeyboardMousePolicy.Inputs.Length==30,"All30 controls required");
   var assignment=new ControllerBinding("keySequence",Keys:[65,66,269],Delays:[73,0]);
   var profile=new ControllerPolicy(new(){{"A",assignment},{"B",new("mouse","x2")}}).Validate();
   var desktop=(defaults with{Bindings=new(){{"A",assignment},{"B",new("mouse","x1")}}}).Validate();
   var padMap=new ControllerTranslator();var desktopMap=new KeyboardMouseTranslator();
   ControllerFrame aFrame=new(ControllerPolicy.Buttons["A"]);
   ControllerFrame bFrame=new(ControllerPolicy.Buttons["B"]);
   ControllerOutput PadAt(ControllerFrame frame,long time,bool overlay=false)=>padMap.Translate(frame,profile,overlay,time);
   ControllerOutput DesktopAt(ControllerFrame frame,long time,bool overlay=false)=>desktopMap.Translate(frame,desktop,.004,time,overlayOpen:overlay);
   foreach(var handle in new Func<ControllerFrame,long,bool,ControllerOutput>[]{PadAt,DesktopAt})
   {
    Check(handle(new(),0,false).Keys.Count==0,"Sequence started without input");
    Check(handle(aFrame,10,false).Keys.SetEquals(new[]{65}),"First key missing");
    Check(handle(new(),42,false).Keys.Count==0,"First key was not released");
    Check(handle(new(),114,false).Keys.Count==0,"Second key began before the recorded delay");
    Check(handle(new(),115,false).Keys.SetEquals(new[]{66}),"Second key timing mismatch");
    Check(handle(new(),147,false).Keys.Count==0&&handle(new(),148,false).Keys.SetEquals(new[]{269}),"Zero gap must still release the preceding key");
    Check(handle(new(),180,false).Keys.Count==0,"Numpad Enter stayed pressed");
    Check(handle(aFrame,190,false).Keys.SetEquals(new[]{65}),"Sequence did not re-arm");
    Check(handle(aFrame,191,true).Keys.Count==0,"Opening the overlay failed to cancel the sequence");
    Check(handle(aFrame,380,false).Keys.Count==0,"Holding the source through the overlay restarted a sequence");
   }
   Check(PadAt(bFrame,400).Mouse.Contains("x2")&&PadAt(new(),410).Mouse.Count==0,"Virtual profile lost the side click");
   Check(DesktopAt(bFrame,400).Mouse.Contains("x1")&&DesktopAt(new(),410).Mouse.Count==0,"Desktop profile lost the side click");
   Check(ControllerInputEmitter.MouseButton("x1",false)==(0x80u,1u)&&ControllerInputEmitter.MouseButton("x2",true)==(0x100u,2u),"Wrong XBUTTON report");
   foreach(var pair in new[]{("A",32),("B",27),("X",13),("Y",8),("LB",9),("LT",160),("RSClick",162)})
   {
    ControllerFrame frame=ControllerPolicy.Buttons.TryGetValue(pair.Item1,out var bits)?new(bits):new(Lt:255);var engine=new KeyboardMouseTranslator();engine.Translate(new(),defaults,.01,0);var output=engine.Translate(frame,defaults,.01,10);Check(output.Keys.SetEquals(new[]{pair.Item2})&&output.Pad.Buttons==0,"Default key mapping "+pair.Item1);Check(engine.Translate(new(),defaults,.01,20).Keys.Count==0,"Key not released");
   }
   foreach(var pair in new[]{("RB","left"),("RT","right"),("LSClick","middle")})
   {var engine=new KeyboardMouseTranslator();engine.Translate(new(),defaults,.01,0);var frame=pair.Item1=="RT"?new ControllerFrame(Rt:255):new(ControllerPolicy.Buttons[pair.Item1]);Check(engine.Translate(frame,defaults,.01,10).Mouse.SetEquals(new[]{pair.Item2}),"Default mouse button "+pair.Item1);Check(engine.Translate(new(),defaults,.01,20).Mouse.Count==0,"Mouse stuck");}
   ControllerOutput Axis(ControllerFrame frame,KeyboardMousePolicy? policy=null){var engine=new KeyboardMouseTranslator();engine.Translate(new(),policy??defaults,.01,0);return engine.Translate(frame,policy??defaults,.01,10);}
   Check(Axis(new(Rx:32767)).CursorX>32000&&Axis(new(Ry:32767)).CursorY>32000,"Right stick movement missing");Check(Axis(new(Rx:500)).CursorX==0,"Centered stick drift");Check(Axis(new(Rx:16000)).CursorX<Axis(new(Rx:16000),defaults with{MouseAcceleration=false}).CursorX,"Acceleration precision near center");
   foreach(int speed in new[]{100,144})foreach(var frame in new[]{new ControllerFrame(Ly:32767),new(Ly:-32768),new(Lx:32767),new(Lx:-32768)})
   {
    var scrollPolicy=defaults with{ScrollSpeed=speed};
    var engine=new KeyboardMouseTranslator();engine.Translate(new(),defaults,.01,0);
    var wheelMotion=new KeyboardMouseWheelMotion();
    int interval=ControllerOutput.DesktopWheelMilliseconds,samples=1000/interval;
    double seconds=interval/1000.0;
    int delta=0;
    int expected=(int)Math.Floor(9.6*speed);
    for(int n=1;n<=samples;n++)
    {
     var output=engine.Translate(frame,scrollPolicy,seconds,n*interval);
     var wheel=wheelMotion.Next(output,seconds);
     int tick=frame.Lx!=0?wheel.Horizontal:wheel.Vertical;
     Check(output.ScrollIntervalMilliseconds==interval&&Math.Abs(tick)>=expected/samples&&Math.Abs(tick)<=expected/samples+1&&output.Wheel==0&&output.HorizontalWheel==0,
      "Analog scrolling emitted a detent or an irregular delta");
     delta+=tick;
    }
    int direction=Math.Sign(frame.Lx!=0?frame.Lx:frame.Ly);
    Check(delta*direction>=expected-2&&delta*direction<=expected,"Continuous scroll direction/total rate");
    var afterStall=engine.Translate(frame,scrollPolicy,.035,1035);
    var caughtUp=wheelMotion.Next(afterStall,.035);
    Check(caughtUp==(0,0),"A stale scroll sample replayed motion");
    var neutral=engine.Translate(new(),defaults,.008,1008);
    Check(wheelMotion.Next(neutral,.008)==(0,0)&&!neutral.ContinuousScroll,"Scroll continued after neutral");
   }
   var slow=new KeyboardMouseTranslator();slow.Translate(new(),defaults,.008,0);
   var slowWheel=new KeyboardMouseWheelMotion();
   var slowStart=slow.Translate(new(Ly:6000),defaults,.008,8);
   Check(slowStart.ScrollIntervalMilliseconds==ControllerOutput.DesktopWheelMilliseconds&&slowWheel.Next(slowStart,.008).Vertical==0,
    "Slow stick did not keep precise timing while the first fractional step accumulated");
   int positive=0;for(int i=2;i<=24;i++)positive+=slowWheel.Next(slow.Translate(new(Ly:6000),defaults,.008,i*8),.008).Vertical;
   Check(positive>0&&positive<120,"Light stick pressure did not produce sub-notch movement");
   Check(Axis(new(Ly:19000),defaults with{ScrollSpeed=144}).ScrollIntervalMilliseconds==ControllerOutput.DesktopWheelMilliseconds,"Moderate stick pressure changed cadence");
   Check(Axis(new(Lx:7600,Ly:23200)) is {HorizontalWheelRate:0,VerticalWheelRate:>0},"A straight scroll picked up sideways stick drift");
   Check(Axis(new(Lx:17500,Ly:23200)) is {HorizontalWheelRate:>0,VerticalWheelRate:>0},"Intentional diagonal scrolling lost an axis");
   Check(Axis(new(Lx:23200,Ly:7600)) is {HorizontalWheelRate:>0,VerticalWheelRate:0},"A horizontal scroll picked up vertical stick drift");
   var steady=new KeyboardMouseTranslator();steady.Translate(new(),defaults,0,0);
   double cardinal=Axis(new(Lx:32000)).HorizontalWheelRate;
   for(int degrees=0;degrees<360;degrees+=5)
   {
    double angle=degrees*Math.PI/180;
    var edge=Axis(new(Lx:(short)Math.Round(32000*Math.Cos(angle)),Ly:(short)Math.Round(32000*Math.Sin(angle))));
    double magnitude=Math.Sqrt(edge.HorizontalWheelRate*edge.HorizontalWheelRate+edge.VerticalWheelRate*edge.VerticalWheelRate);
    Check(edge.ScrollIntervalMilliseconds==ControllerOutput.DesktopWheelMilliseconds&&Math.Abs(magnitude-cardinal)<.1,
     "Rotating a fully deflected stick changed the total wheel rate or cadence");
   }
   var full=Axis(new(Ly:32767));
   int Sum(IEnumerable<int> ticks)
   {
    var integral=new KeyboardMouseWheelMotion();
    return ticks.Sum(ms=>integral.Next(full,ms/1000.0).Vertical);
   }
   Check(Math.Abs(Sum(Enumerable.Repeat(5,200))-Sum(Enumerable.Repeat(new[]{3,6,11,20,10},20).SelectMany(x=>x)))<=1,
    "Timer jitter discarded scroll distance");
   steady.Reset();steady.Translate(new(),defaults,0,0);
   var recoveryWheel=new KeyboardMouseWheelMotion();
   int recovered=recoveryWheel.Next(steady.Translate(new(Ly:32767),defaults,.012,12),.012).Vertical;
   for(int n=1;n<=5;n++)recovered+=recoveryWheel.Next(steady.Translate(new(Ly:32767),defaults,.005,12+n*5),.005).Vertical;
   Check(recovered is >=34 and <=36,"A short wheel interruption did not recover smoothly");
   recoveryWheel.Next(steady.Translate(new(Ly:32767),defaults,.018,55),.018);
   Check(recoveryWheel.Next(steady.Translate(new(),defaults,.005,60),.005)==(0,0)&&
    recoveryWheel.Next(steady.Translate(new(Ly:-32768),defaults,.005,65),.005).Vertical<0,"Pending wheel input leaked through neutral or reversal");
   Check(slowWheel.Next(slow.Translate(new(Ly:-32768),defaults,.008,200),.008).Vertical<0,"Scroll reversal retained the former direction");
   var buttonScroll=defaults with{Bindings=new(){{"A",new("mouse","wheelUp")}}};
   Check(Axis(new(4096),buttonScroll) is {Wheel:1,VerticalWheelRate:0,ContinuousScroll:false},"Button scroll did not retain one detent");
   var tuning=new KeyboardMouseTranslator();tuning.Translate(new(),defaults,0,0);
   var lowPolicy=(defaults with{MouseSpeed=65,ScrollSpeed=75,MouseAcceleration=false}).Validate();
   var highPolicy=(lowPolicy with{MouseSpeed=130,ScrollSpeed=150}).Validate();
   var tilted=new ControllerFrame(Rx:18000,Ry:13500,Ly:28000);
   var low=tuning.Translate(tilted,lowPolicy,.004,4);var high=tuning.Translate(tilted,highPolicy,.004,8);
   Check(lowPolicy.SameBindings(highPolicy)&&high.CursorSpeedScale==low.CursorSpeedScale*2&&high.VerticalWheelRate==low.VerticalWheelRate*2&&
    high.CursorX==low.CursorX&&high.CursorY==low.CursorY,"Live scalar tuning did not reach held analog input");
   Check(!lowPolicy.SameBindings(lowPolicy with{Bindings=new(){{"A",new("keyboard",Keys:[80])}}}),"Changed bindings were mistaken for live scalar tuning");
   Check(Axis(tilted,defaults with{AdaptiveScroll=false}).VerticalWheelRate>Axis(tilted).VerticalWheelRate,"Adaptive scroll did not change the effective rate at partial travel");
   var tap=new KeyboardMouseTranslator();tap.Translate(new(),defaults,.01,0);Check(tap.Translate(new(16),defaults,.01,10).Keys.Count==0,"Start single leaked before double decision");tap.Translate(new(),defaults,.01,30);Check(tap.Translate(new(),defaults,.01,340).Keys.SetEquals(new[]{91}),"Start single Win missing");Check(tap.Translate(new(),defaults,.01,430).Keys.Count==0,"Start Win stuck");tap.Reset();tap.Translate(new(),defaults,.01,500);tap.Translate(new(16),defaults,.01,510);tap.Translate(new(),defaults,.01,530);Check(tap.Translate(new(16),defaults,.01,560).Keys.SetEquals(new[]{91,9}),"Start double Win+Tab missing");tap.Translate(new(),defaults,.01,580);Check(tap.Translate(new(),defaults,.01,700).Keys.Count==0,"Start double stuck");
   var fn=(defaults with{FnButton="OEM:M1",FnBindings=new(){{"A",new("keyboard",Keys:[17,67])}}}).Validate();var layered=new KeyboardMouseTranslator();layered.Translate(new(),fn,.01,0);Check(layered.Translate(new(4096),fn,.01,10).Keys.Contains(32),"Base layer lost");Check(layered.Translate(new(4096),fn,.01,20,16).Keys.Count==0,"FN transition replayed held button");layered.Translate(new(),fn,.01,30,16);Check(layered.Translate(new(4096),fn,.01,40,16).Keys.SetEquals(new[]{17,67}),"FN layer failed");Check(layered.Translate(new(4096),fn,.01,50).Keys.Count==0,"Leaving FN kept chord held");
   var combo=(defaults with{Bindings=new(){{"A",new("combo","left",[17])}}}).Validate();Check(Axis(new(4096),combo) is { } mixed&&mixed.Keys.Contains(17)&&mixed.Mouse.Contains("left"),"Keyboard+mouse combination lost");
   bool rejected=false;try{(defaults with{FnButton="A"}).Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"Ordinary control accepted as FN");rejected=false;try{(defaults with{Bindings=new(){{"A",new("gamepad","B")}}}).Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"Gamepad output accepted in keyboard/mouse settings");
   rejected=false;try{(defaults with{Bindings=new(){{"OEM:Home",new("disabled")},{"OEM:GKey",new("disabled")}}}).Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"Last widget access removed");
   var overlay=new KeyboardMouseTranslator();overlay.Translate(new(),defaults,.01,0);Check(overlay.Translate(new(4096,Rx:30000),defaults,.01,10,overlayOpen:true) is {} bypass&&bypass.Keys.Count==0&&bypass.CursorX==0&&bypass.Pad.Buttons==4096,"Overlay input not bypassed");Check(overlay.Translate(new(4096),defaults,.01,20).Keys.Count==0,"Held overlay button leaked after closing");
   string own=Path.Combine(Path.GetDirectoryName(file)!,"keyboard-mode-model-"+Guid.NewGuid().ToString("N")),reports=Path.Combine(own,"Reports");Directory.CreateDirectory(reports);
   try
   {
    var capture=new Capture();var extra=new Oem();var settings=new KeyboardMouseSettings(reports);await using var profiles=new ControllerProfiles(reports,()=>"fixed-session",()=>"global",()=>[("global","Global Profile")],capture:capture,keyboardSettings:settings);profiles.BindOemManager(extra);
    Check(profiles.CurrentOem.DoubleFor("KeyboardMouse").Enabled&&profiles.CurrentOem.For("KeyboardMouse:Double").Kind=="inputMode","Global Keyboard shortcut not reserved");
    using(var create=JsonDocument.Parse("{\"operation\":\"create\",\"name\":\"ONEXQA Keyboard Model\"}"))await profiles.Mutate(create.RootElement);string id=profiles.EditingId;string controllerFile=Path.Combine(own,"controller-profiles.json"),before=File.ReadAllText(controllerFile);
    await profiles.SetKeyboardMouseMode(true);Check(profiles.KeyboardMouseActive&&capture.Read().Running&&capture.Applied!.KeyboardMouse!=null,"Mode did not start independent capture");Check(profiles.EditingId==id&&profiles.AppliedId==id,"Mode changed active gamepad profile");
    using(var change=JsonDocument.Parse("{\"operation\":\"tuning\",\"key\":\"mouseSpeed\",\"value\":125}"))await profiles.MutateKeyboardMouse(change.RootElement);Check(capture.Applied!.KeyboardMouse!.MouseSpeed==125,"Tuning not applied to active mode");Check(File.ReadAllText(controllerFile)==before,"Keyboard/mouse tuning overwrote gamepad profiles");
    await profiles.SetKeyboardMouseMode(false);Check(!capture.Read().Running&&!extra.Prepared&&profiles.EditingId==id&&File.ReadAllText(controllerFile)==before,"Returning to hardware changed gamepad preferences");
    using(var enable=JsonDocument.Parse("{\"operation\":\"enable\",\"enabled\":true}"))await profiles.Mutate(enable.RootElement);Check(capture.Read().Running&&capture.Applied!.KeyboardMouse==null,"Original virtual gamepad preference unavailable");
    await profiles.SetKeyboardMouseMode(true);await profiles.SetKeyboardMouseMode(false);Check(capture.Read().Running&&capture.Applied!.KeyboardMouse==null,"Returning to enabled virtual gamepad lost preference");
    var hardware=new ControllerPolicy(UseHardwareController:true).Validate();using(var edit=JsonDocument.Parse(JsonSerializer.Serialize(new{operation="edit",id,policy=hardware},Backend.Program.Json)))await profiles.Mutate(edit.RootElement);Check(!capture.Read().Running,"Hardware game profile not respected");before=File.ReadAllText(controllerFile);await profiles.SetKeyboardMouseMode(true);Check(capture.Read().Running&&capture.Applied!.KeyboardMouse!=null,"Hardware profile blocked keyboard/mouse mode");await profiles.SetKeyboardMouseMode(false);Check(!capture.Read().Running&&before==File.ReadAllText(controllerFile),"Hardware profile not restored after mode");
   }
   finally{if(Directory.Exists(own))Directory.Delete(own,true);}
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,modelOnly=true,physicalOwnerInputTested=false},Backend.Program.Json));return 0;
  }catch(Exception error){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=error.Message},Backend.Program.Json));return 2;}
 }
}
