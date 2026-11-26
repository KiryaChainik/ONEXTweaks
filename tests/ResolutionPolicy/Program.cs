using XboxGamingBar;
int checks=0;void Check(bool value){checks++;if(!value)throw new InvalidOperationException("Resolution policy "+checks);}
var all=OnexResolutionPolicy.TapOrder;foreach(var value in new[]{(1920,1200,"1680x1050"),(1680,1050,"1440x900"),(1440,900,"1280x800"),(1280,800,"1920x1200"),(1200,1920,"1680x1050"),(1600,900,"1920x1200")})Check(OnexResolutionPolicy.Next(value.Item1,value.Item2,all)==value.Item3);
Check(OnexResolutionPolicy.Next(1920,1200,new[]{"1920x1200","1280x800"})=="1280x800");Check(OnexResolutionPolicy.Next(1280,800,new[]{"1920x1200"})=="1920x1200");Check(OnexResolutionPolicy.Key(800,1280)=="1280x800");
foreach(var supported in new[]{Array.Empty<string>(),new[]{"1600x900"},(string[])null}){bool rejected=false;try{OnexResolutionPolicy.Next(1920,1200,supported);}catch(InvalidOperationException){rejected=true;}Check(rejected);}
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{passed=true,checks,kind="pure-model",hardwareWrites=0}));
