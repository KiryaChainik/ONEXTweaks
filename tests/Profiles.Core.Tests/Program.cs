using ONEXTweaks.WidgetBridge;
using System.Text.Json;
int checks=0;
void Expect(bool value,string message){checks++;if(!value)throw new Exception(message);}
var globalAc=new ProfileValues(17,19,2,0,0,1,new("Intel",false,60),0,0);
var globalDc=globalAc with{Pl1=11,Pl2=13,Boost=0,PowerMode=0};
var gameAc=globalAc with{Pl1=30,Pl2=40,Boost=4,Scheduling=2,ClockMhz=3000,PowerMode=2,Fps=new("RTSS",true,60),Overlay=4};
var gameDc=gameAc with{Pl1=9,Pl2=10,Fps=new("Intel",true,30),Overlay=1};
string[] groups={"TDP","CPUBoost","FPSLimit","OSPowerMode","OverlayLevel"};
var global=new ProfileEntry("global","global","",false,true,globalAc,globalDc,DateTimeOffset.UtcNow);
var game=new ProfileEntry("game","test","C:/test.exe",true,true,gameAc,gameDc,DateTimeOffset.UtcNow);
var flags=groups.ToDictionary(g=>g,_=>true);
flags["Fan"]=false;
var doc=new ProfileDocument(1,global,new(){game},flags);
Expect(ProfileResolution.Resolve(doc,game,true)==gameAc,"AC resolution");Expect(ProfileResolution.Resolve(doc,game,false)==gameDc,"DC resolution");
foreach(string group in groups){var shared=new Dictionary<string,bool>(doc.Flags){[group]=false};var changed=doc with{Flags=shared};Expect(ProfileResolution.Resolve(changed,game,true)==gameAc.GroupFrom(group,globalAc),"AC global fallback "+group);Expect(ProfileResolution.Resolve(changed,game,false)==gameDc.GroupFrom(group,globalDc),"DC global fallback "+group);}
foreach(var bad in new[]{gameAc with{Pl1=7},gameAc with{Pl1=41},gameAc with{Pl2=45},gameAc with{Pl2=29,SeparatePl2=true},gameAc with{Fan=3},gameAc with{Boost=7},gameAc with{ClockMhz=3050},gameAc with{Fps=new("other",true,60)}}){bool rejected=false;try{bad.Validate();}catch(InvalidDataException){rejected=true;}Expect(rejected,"Invalid profile accepted");}
var roundtrip=JsonSerializer.Deserialize<ProfileDocument>(JsonSerializer.Serialize(doc));Expect(roundtrip?.Games.Single().Ac==gameAc&&roundtrip.Global.Dc==globalDc,"Roundtrip lost AC/DC values");
Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks,physicalPowerSourceChanged=false}));
