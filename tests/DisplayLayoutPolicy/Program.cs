using ONEXTweaks.WidgetBridge;
int checks=0;void Check(bool value){checks++;if(!value)throw new InvalidOperationException("Display layout policy "+checks);}
Check(DisplayScalePolicy.Decode(-2,0,2).Current==150&&DisplayScalePolicy.Decode(-2,0,2).Recommended==150);
Check(DisplayScalePolicy.Decode(-2,0,2).Choices.SequenceEqual(new[]{100,125,150,175,200}));Check(DisplayScalePolicy.Decode(-3,-1,1).Current==150);Check(DisplayScalePolicy.Decode(-3,-1,1).Recommended==175);
foreach(int value in new[]{100,125,150,175,200})Check(DisplayScalePolicy.Relative(value,-2,0,2)==Array.IndexOf(DisplayScalePolicy.Steps,value)-2);
foreach(var range in new[]{(1,1,2),(-20,0,2),(-2,-3,2),(-2,3,2),(-2,0,-1),(-2,0,30)}){bool rejected=false;try{DisplayScalePolicy.Decode(range.Item1,range.Item2,range.Item3);}catch(InvalidDataException){rejected=true;}Check(rejected);}
foreach(int value in new[]{99,137,225}){bool rejected=false;try{DisplayScalePolicy.Relative(value,-2,0,2);}catch(ArgumentOutOfRangeException){rejected=true;}Check(rejected);}
var wide=new DisplayModeValue(1920,1200,144);var tall=new DisplayModeValue(1200,1920,144);Check(DisplayModeOrientationPolicy.PreserveOrientation(wide,tall)==tall);Check(DisplayModeOrientationPolicy.PreserveOrientation(tall,wide)==wide);Check(DisplayModeOrientationPolicy.PreserveOrientation(wide,wide)==wide);Check(DisplayModeOrientationPolicy.PreserveOrientation(new(800,800,60),tall)==new DisplayModeValue(800,800,60));
foreach(var size in new[]{(640,480),(480,640),(1920,1200),(1200,1920),(640,360)})Check(DisplayModeOrientationPolicy.ValidSize(size.Item1,size.Item2));foreach(var size in new[]{(320,200),(8001,600),(600,8001),(0,640)})Check(!DisplayModeOrientationPolicy.ValidSize(size.Item1,size.Item2));
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{passed=true,checks,kind="pure-model",hardwareWrites=0}));
