// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerGyroProcessingVerification
{
 private static ControllerOutput Basis()=>new(new(),[],[],0,0,0,[]);
 private static ControllerGyroReading Fresh(double x,double y=0)=>new(true,new(y,-x,0),new(0,0,-1),0,0,1);
 internal static async Task<int> Models(string file){
  int checks=0;void Check(bool value,string why){checks++;if(!value)throw new IOException(why);}
  try{
   var legacy=JsonSerializer.Deserialize<ControllerPolicy>("""{"gyro":{"target":"Mouse","sensitivityX":83}}""",Backend.Program.Json)!.Validate();
   Check(legacy.Gyro!.Processing==null&&!JsonSerializer.Serialize(legacy,Backend.Program.Json).Contains("processing"),"Legacy JSON acquired gyro processing");
   foreach(var invalid in new[]{new ControllerGyroProcessing("bad"),new(AdaptiveSmoothing:-1),new(AdaptiveSmoothing:101),new(PrecisionSmoothing:-1),new(PrecisionSmoothing:101),new(GateTenths:-1),new(GateTenths:101),new(AntiDeadzone:-1),new(AntiDeadzone:51),new(OutputRotation:-1),new(OutputRotation:45),new(OutputRotation:360)}){
    bool rejected=false;try{new ControllerGyroPolicy(Processing:invalid).Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"Invalid filter policy accepted");
   }
   foreach(double dt in new[]{.008,.01,.02,.03}){
    var f=new ControllerGyroFilter();foreach(var v in new[]{(0.0,0.0),(15.0,-3.0),(-90.0,120.0)}){
     Check(f.Apply(v.Item1,v.Item2,null,dt)==v,"Legacy linear mapping changed");Check(f.Apply(v.Item1,v.Item2,new("Linear",100,100),dt)==v,"Linear smoothing modified output");
    }
   }
   var zero=new ControllerGyroFilter();Check(zero.Apply(20,-40,new("Adaptive",0),.01)==(20.0,-40.0),"Zero adaptive smoothing not bypassed");
   var precision=new ControllerGyroFilter();var slow=precision.Apply(15,0,new("Precision",PrecisionSmoothing:0),.01);var fast=precision.Apply(180,0,new("Precision",PrecisionSmoothing:0),.01);
   Check(slow.X>0&&slow.X<15&&fast.X>180,"Precision slow/fast behaviour wrong");
   var xy=precision.Apply(30,-40,new("Precision",PrecisionSmoothing:0),.01);Check(Math.Abs(xy.X/xy.Y+ .75)<1e-12,"Precision distorted direction");
   double Step(int smoothing){var f=new ControllerGyroFilter();var p=new ControllerGyroProcessing("Adaptive",smoothing);f.Apply(0,0,p,.01);return f.Apply(90,0,p,.01).X;}
   Check(Step(100)>0&&Step(100)<Step(20)&&Step(20)<90,"Smoothing does not reduce initial step");
   var input=Enumerable.Range(0,400).Select(n=>30+(n%2==0?2.0:-2.0)).ToArray();var filter=new ControllerGyroFilter();var result=input.Select(v=>filter.Apply(v,0,new("Adaptive",75),.01).X).Skip(100).ToArray();
   double variance(IEnumerable<double> v){var a=v.ToArray();double mean=a.Average();return a.Select(x=>(x-mean)*(x-mean)).Average();}
   Check(variance(result)<variance(input)*.1,"Adaptive filter did not suppress jitter");
   foreach(string engine in new[]{"Adaptive","Precision"}){
    var f=new ControllerGyroFilter();var p=new ControllerGyroProcessing(engine,100,100);f.Apply(100,0,p,.01);
    double last=0;for(int n=0;n<600;n++)last=f.Apply(-10,0,p,.01).X;
    Check(last<0,"Filter retained opposite-direction motion indefinitely");
    f.Reset();var expected=new ControllerGyroFilter().Apply(-10,3,p,.01);Check(f.Apply(-10,3,p,.01)==expected,"Reset leaked old motion");
    f.Apply(100,100,p,.01);Check(f.Apply(3,-2,new("Linear"),.01)==(3.0,-2.0),"Engine switch leaked smoothing");
   }
   var bad=new ControllerGyroFilter();Check(bad.Apply(double.NaN,0,new("Adaptive"),.01)==(0.0,0.0)&&bad.Apply(4,0,new("Adaptive"),0)==(0.0,0.0),"Invalid sample/delta propagated");
   var p1=new ControllerGyroPolicy("RightStick","Always",Processing:new("Adaptive",20,90));var saved=JsonSerializer.Deserialize<ControllerGyroPolicy>(JsonSerializer.Serialize(p1,Backend.Program.Json),Backend.Program.Json)!.Validate();
   Check(saved.Processing==new ControllerGyroProcessing("Adaptive",20,90),"Per-engine values failed JSON roundtrip");
   Check(ControllerRemappingGroups.ResetGroup(new ControllerPolicy(Tuning:new(LeftDeadzone:7),Gyro:p1),"gyro") is{Gyro:null,Tuning.LeftDeadzone:7},"Gyro reset altered other profile settings");
   var oldRouter=new ControllerGyroRouter();var directRouter=new ControllerGyroRouter();var legacySettings=new ControllerGyroPolicy("Mouse","Always");var direct=legacySettings with{Processing=new("Linear")};
   for(int n=0;n<100;n++)Check(oldRouter.Apply(Basis(),new(),legacySettings,Fresh(13,-4),null,0,30,false,n*10,.01)==directRouter.Apply(Basis(),new(),direct,Fresh(13,-4),null,0,30,false,n*10,.01),"Legacy output/remainders changed");
   var router=new ControllerGyroRouter();router.Apply(Basis(),new(),p1,Fresh(90),null,0,30,false,0,.01);router.Apply(Basis(),new(),p1,Fresh(-30),null,0,30,true,10,.01);
   var resumed=router.Apply(Basis(),new(),p1,Fresh(-30),null,0,30,false,20,.01);Check(resumed.Pad.Rx<0,"Paused filter resumed old direction");
   var disabled=router.Apply(Basis(),new(),p1 with{Target="Disabled"},Fresh(90),null,0,30,false,30,.01);Check(disabled.Pad==new ControllerFrame(),"Disabled processing emitted motion");
   foreach(string target in new[]{"Mouse","LeftStick","RightStick"}){
    var gateRouter=new ControllerGyroRouter();var gp=new ControllerGyroPolicy(target,"Always",Processing:new(GateTenths:10,AntiDeadzone:20));
    foreach(double rate in new[]{0.0,.9,1.0}){var b=gateRouter.Apply(Basis(),new(),gp,Fresh(rate),null,0,30,false,0,.01);Check(b.Pad==new ControllerFrame()&&b.RelativeMouseX==0,"Gate leaked below-threshold motion");}
    var active=gateRouter.Apply(Basis(),new(),gp,Fresh(target=="Mouse"?20:2),null,0,30,false,10,.01);
    Check(target=="Mouse"?active.RelativeMouseX>0:target=="LeftStick"?active.Pad.Lx>=6500:active.Pad.Rx>=6500,"Above gate failed target/floor");
   }
   var floorRouter=new ControllerGyroRouter();var floorPolicy=new ControllerGyroPolicy("RightStick","Always",Processing:new(AntiDeadzone:20));
   var diagonal=floorRouter.Apply(Basis(),new(),floorPolicy,Fresh(1,1),null,0,30,false,0,.01);
   Check(Math.Abs(Math.Sqrt((double)diagonal.Pad.Rx*diagonal.Pad.Rx+(double)diagonal.Pad.Ry*diagonal.Pad.Ry)-6553.4)<2,"Anti-deadzone is not radial");
   Check(diagonal.Pad.Rx>0&&diagonal.Pad.Ry<0&&Math.Abs(diagonal.Pad.Rx+diagonal.Pad.Ry)<=1,"Floor distorted direction");
   var zeroMotion=floorRouter.Apply(Basis(),new(),floorPolicy,Fresh(0),null,0,30,false,10,.01);Check(zeroMotion.Pad.Rx==0,"Floor created motion at rest");
   var smoothFloor=new ControllerGyroRouter();var smoothPolicy=floorPolicy with{Processing=new("Adaptive",100,100,0,20)};
   smoothFloor.Apply(Basis(),new(),smoothPolicy,Fresh(90),null,0,30,false,0,.01);Check(smoothFloor.Apply(Basis(),new(),smoothPolicy,Fresh(0),null,0,30,false,10,.01).Pad.Rx==0,"Anti-deadzone amplified filter tail atzero");
   var largeMotion=floorRouter.Apply(Basis(),new(),floorPolicy,Fresh(90),null,0,30,false,20,.01);Check(largeMotion.Pad.Rx is >16300 and <16400,"Floor altered already-large movement");
   var tail=new ControllerGyroRouter();var gatedSmooth=new ControllerGyroPolicy("RightStick","Always",Processing:new("Adaptive",100,100,10,20));
   tail.Apply(Basis(),new(),gatedSmooth,Fresh(90),null,0,30,false,0,.01);var stopped=tail.Apply(Basis(),new(),gatedSmooth,Fresh(.1),null,0,30,false,10,.01);Check(stopped.Pad.Rx==0,"Gate retained smoothing tail");
   var freshStart=tail.Apply(Basis(),new(),gatedSmooth,Fresh(-10),null,0,30,false,20,.01);Check(freshStart.Pad.Rx<0,"Gate restart retained old direction");
   var mouseA=new ControllerGyroRouter();var mouseB=new ControllerGyroRouter();var mousePolicy=new ControllerGyroPolicy("Mouse","Always",Processing:new("Adaptive",90,90,0,0));var mouseStoredFloor=mousePolicy with{Processing=new("Adaptive",90,90,0,20)};
   for(int n=0;n<20;n++){var rate=Fresh(n==0?90:0);var a=mouseA.Apply(Basis(),new(),mousePolicy,rate,null,0,30,false,n*10,.01);var b=mouseB.Apply(Basis(),new(),mouseStoredFloor,rate,null,0,30,false,n*10,.01);Check(a.RelativeMouseX==b.RelativeMouseX&&a.RelativeMouseY==b.RelativeMouseY,"Stored inactive stick floor altered Mouse filtering");}
   var angular=new MotionVector(2,-3,4);var upright=new MotionVector(0,-1,0);
   foreach(var entry in new[]{(0,3.0,2.0),(90,-2.0,3.0),(180,-3.0,-2.0),(270,2.0,-3.0)}){
    Check(ControllerGyroAxes.TryResolve(angular,upright,new(OutputRotation:entry.Item1),out double x,out double y)&&x==entry.Item2&&y==entry.Item3,"Output rotation mapping wrong");
    Check(ControllerGyroAxes.TryResolve(angular,upright,new(InvertX:true,InvertY:true,OutputRotation:entry.Item1),out x,out y)&&x==-entry.Item2&&y==-entry.Item3,"Output inversions wrong");
   }
   Check(ControllerGyroAxes.TryResolve(angular,upright,new(WorldSpace:true),out double wx,out double wy)&&wx==3&&wy==2,"Upright gravity frame differs fromdevice mapping");
   Check(ControllerGyroAxes.TryResolve(angular,new(0,0,-1),new(WorldSpace:true),out wx,out wy)&&wx==-4&&wy==2,"Flat worldyaw failed");
   Check(ControllerGyroAxes.TryResolve(angular,new(.001,.001,-1),new(WorldSpace:true),out wx,out wy)&&wy==2,"Near-flat gravity noise rotated thepitch axis");
   Check(ControllerGyroAxes.TryResolve(angular,new(-1,0,0),new(WorldSpace:true),out wx,out wy)&&wx==-2&&wy==3,"Rolled worldpitch failed");
   Check(!ControllerGyroAxes.TryResolve(angular,new(),new(WorldSpace:true),out _,out _)&&!ControllerGyroAxes.TryResolve(angular,new(0,0,10),new(WorldSpace:true),out _,out _),"Unusable gravity accepted");
   var axesRouter=new ControllerGyroRouter();var axesPolicy=new ControllerGyroPolicy("Mouse","Always",Processing:new(InvertX:true));
   Check(axesRouter.Apply(Basis(),new(),axesPolicy,Fresh(90),null,0,30,false,0,.01).RelativeMouseX<0,"Mouse inversion failed");
   var worldPolicy=new ControllerGyroPolicy("RightStick","Always",Processing:new(WorldSpace:true));var invalidGravity=Fresh(90) with{Acceleration=new()};
   Check(axesRouter.Apply(Basis(),new(),worldPolicy,invalidGravity,null,0,30,false,10,.01).Pad.Rx==0&&axesRouter.BlockedReason=="Waiting for stable gravity","Invalid gravity emitted orhid status");
   var worldSaved=JsonSerializer.Deserialize<ControllerGyroProcessing>(JsonSerializer.Serialize(new ControllerGyroProcessing(WorldSpace:true,InvertX:true,OutputRotation:270),Backend.Program.Json),Backend.Program.Json)!;
   Check(worldSaved.WorldSpace&&worldSaved.InvertX&&worldSaved.OutputRotation==270,"Axis profile roundtrip failed");
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,syntheticSamples=true,actualHardware=false,profileWrites=0},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message},Backend.Program.Json));return 2;}
 }
 internal static async Task<int> Native(string file){
  int checks=0;ControllerVirtualPad? pad=null;var slots=ControllerNative.Slots();void Check(bool value,string why){checks++;if(!value)throw new IOException(why);}
  try{
   Check(slots.SequenceEqual(new[]{0})&&ControllerNative.Read(0,out var source)&&ControllerNative.Neutral(source),"Protect physical controller/current capture");
   using var window=new ControllerTestWindow();window.RequireForeground();pad=new ControllerVirtualPad(0,false);
   async Task<short> Show(string engine,int smoothing,double speed,bool prime,int gate=0,int anti=0){
    var r=new ControllerGyroRouter();var p=new ControllerGyroPolicy("RightStick","Always",Processing:new(engine,smoothing,smoothing,gate,anti));
    if(prime)r.Apply(Basis(),new(),p,Fresh(0),null,0,30,false,0,.01);
    var output=r.Apply(Basis(),new(),p,Fresh(speed),null,0,30,false,10,.01);window.RequireForeground();pad.Submit(output.Pad);
    bool seen=false;for(int n=0;n<100;n++){if(ControllerNative.Read(pad.Slot,out var observed)&&observed.Rx==output.Pad.Rx&&observed.Ry==output.Pad.Ry&&observed.Buttons==0){seen=true;break;}await Task.Delay(5);}Check(seen,"Actual VIIPER filtered output mismatch");return output.Pad.Rx;
   }
   short linear=await Show("Linear",100,90,true),adaptive=await Show("Adaptive",70,90,true);Check(linear>16000&&adaptive>0&&adaptive<linear,"Actual adaptive output not smoothed");
   short slow=await Show("Precision",0,15,false),direct=await Show("Linear",0,15,false);Check(slow>0&&slow<direct,"Actual precision slow output wrong");
   short reverse=await Show("Adaptive",90,-90,true);Check(reverse<0,"Actual filtered negative direction wrong");
   short gated=await Show("Linear",0,.5,false,10,20),floored=await Show("Linear",0,2,false,10,20);Check(gated==0&&floored>=6500,"Actual VIIPER gate/floor wrong");
   foreach(int rotation in new[]{0,90,180,270}){
    var r=new ControllerGyroRouter();var p=new ControllerGyroPolicy("RightStick","Always",Processing:new(OutputRotation:rotation));var o=r.Apply(Basis(),new(),p,Fresh(90),null,0,30,false,0,.01);window.RequireForeground();pad.Submit(o.Pad);
    bool seen=false;for(int n=0;n<100;n++){if(ControllerNative.Read(pad.Slot,out var b)&&b.Rx==o.Pad.Rx&&b.Ry==o.Pad.Ry){seen=true;break;}await Task.Delay(5);}Check(seen,"Actual rotatedVIIPER output mismatch");
    Check(rotation==0?o.Pad.Rx>0:rotation==90?o.Pad.Ry<0:rotation==180?o.Pad.Rx<0:o.Pad.Ry>0,"Actual rotation sign wrong");
   }
   {var r=new ControllerGyroRouter();var p=new ControllerGyroPolicy("RightStick","Always",Processing:new(WorldSpace:true));var reading=new ControllerGyroReading(true,new(0,0,-90),new(0,0,-1),0,0,1);var o=r.Apply(Basis(),new(),p,reading,null,0,30,false,0,.01);window.RequireForeground();pad.Submit(o.Pad);bool seen=false;for(int n=0;n<100;n++){if(ControllerNative.Read(pad.Slot,out var b)&&b.Rx==o.Pad.Rx&&b.Rx>16000){seen=true;break;}await Task.Delay(5);}Check(seen,"Actual worldyaw VIIPER output wrong");}
   window.RequireForeground();pad.Submit(new());pad.Dispose();pad=null;Check(ControllerNative.Slots().SequenceEqual(slots),"Temporary VIIPER device lingered");
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks,actualViiperAndXInputReadback=true,syntheticAngularSamples=true,ownForegroundWindowRequired=true,physicalInputRestored=true,mouseKeyInjection=0,ownerPreferenceWrites=0,physicalOwnerAimingTested=false},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>")},Backend.Program.Json));return 2;}
  finally{try{pad?.Dispose();}catch{}}
 }
}
