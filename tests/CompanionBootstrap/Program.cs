using ONEXTweaks.CompanionLauncher;
var checks=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
var calls=new List<string>();var states=CompanionBootstrap.Start(_=>false,name=>calls.Add(name));Check(calls.SequenceEqual(new[]{"ONEXTweaks-PowerAgent","ONEXTweaks-WidgetBridge"}),"Cold start requests hardware agent before bridge");
calls.Clear();states=CompanionBootstrap.Start(_=>true,name=>calls.Add(name));Check(calls.Count==0,"Already running companions are not restarted");
calls.Clear();states=CompanionBootstrap.Start(name=>name=="ONEXTweaks-PowerAgent",name=>calls.Add(name));Check(calls.SequenceEqual(new[]{"ONEXTweaks-WidgetBridge"}),"A missing bridge starts without restarting the agent");
calls.Clear();states=CompanionBootstrap.Start(_=>false,name=>{calls.Add(name);if(name=="ONEXTweaks-PowerAgent")throw new UnauthorizedAccessException();});Check(calls.Count==2&&states["ONEXTweaks-PowerAgent"].StartsWith("error:")&&states["ONEXTweaks-WidgetBridge"]=="start requested","Agent failure is reported while widget/Windows functions can still recover");
calls.Clear();states=CompanionBootstrap.Start(name=>{if(name=="ONEXTweaks-PowerAgent")throw new IOException();return true;},name=>calls.Add(name));Check(states["ONEXTweaks-PowerAgent"].StartsWith("error:")&&states["ONEXTweaks-WidgetBridge"]=="already running","One task lookup failure cannot restart the other companion");
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{passed=true,checks,windowsTaskSchedulerTested=false}));
