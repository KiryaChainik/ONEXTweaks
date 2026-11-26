// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class DisplayProfileVerification
{
    internal static async Task<int> Run(string output)
    {
        var checks=new List<string>();void Check(bool condition,string name){if(!condition)throw new InvalidDataException(name);checks.Add(name);}
        try
        {
            var values=new ProfileValues(16,28,2,4,2500,1,new("RTSS",true,45),2,1,false);
            var global=new ProfileEntry("global","global","",false,true,values,values with{Pl1=20,Pl2=30,SeparatePl2=true},DateTimeOffset.UtcNow);
            var game=new ProfileEntry("fixture","fixture","C:\\onex-fixture.exe",true,false,values,values,DateTimeOffset.UtcNow);
            var oldFlags=new Dictionary<string,bool>{{"TDP",true},{"CPUBoost",false},{"FPSLimit",true},{"OSPowerMode",false},{"Fan",true},{"OverlayLevel",true}};
            var source=new ProfileDocument(2,global,[game],oldFlags);
            var baseline=new DisplayPreferences{Colors=null,FrameGeneration=0,LowLatency=2,FrameSync=3,Vrr=1,VrrMode=1,Mode=new(1920,1200,144)};
            var upgraded=PerformanceProfiles.UpgradeDisplay(source,baseline);
            Check(upgraded.Schema==3,"schema2 upgrades to3");Check(upgraded.Global.Ac with{Display=null}==global.Ac&&upgraded.Global.Dc with{Display=null}==global.Dc,"migration preserves both AC/DC power and other settings");
            Check(upgraded.Games.Single()==game,"legacy game record unchanged");Check(oldFlags.All(flag=>upgraded.Flags[flag.Key]==flag.Value)&&upgraded.Flags["Display"]&&upgraded.Flags["Resolution"]&&upgraded.Flags["RefreshRate"],"existing save flags preserved and display groups added");
            Check(ProfileResolution.Resolve(upgraded,game,true).Display==baseline,"legacy game inherits actual global display instead of invented defaults");
            var custom=baseline with{Colors=new(60,10,45,55,110),Mode=new(1280,800,60)};var own=game with{Ac=game.Ac with{Display=custom},Dc=game.Dc with{Display=custom}};
            Check(ProfileResolution.Resolve(upgraded,own,true).Display==custom,"game owns display colors and mode");upgraded.Flags["Display"]=false;var inherited=ProfileResolution.Resolve(upgraded,own,true).Display!;
            Check(inherited.Colors==baseline.Colors&&inherited.Mode==custom.Mode,"disabling display group inherits graphics without changing game mode");upgraded.Flags["Resolution"]=false;inherited=ProfileResolution.Resolve(upgraded,own,true).Display!;
            Check(inherited.Mode?.Width==1920&&inherited.Mode.Hz==60,"resolution inheritance keeps independent game refresh rate");upgraded.Flags["RefreshRate"]=false;Check(ProfileResolution.Resolve(upgraded,own,true).Display?.Mode==baseline.Mode,"all mode inheritance resolves global mode");
            var roundtrip=JsonSerializer.Deserialize<ProfileDocument>(JsonSerializer.Serialize(upgraded,Backend.Program.Json),Backend.Program.Json)!;Check(roundtrip.Global==upgraded.Global&&roundtrip.Games.SequenceEqual(upgraded.Games),"schema3 JSON roundtrip preserves profiles");
            bool rejected=false;try{(custom with{Colors=new(101)}).Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"invalid color range rejected");
            await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=true,checks,hardwareWrites=0,userFilesTouched=false},Backend.Program.Json));return 0;
        }
        catch(Exception error){await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=false,checks,error=error.ToString()},Backend.Program.Json));return 1;}
    }
}
