// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class PowerSelectionVerification
{
    internal static async Task<int> Run(string output)
    {
        var checks=new List<string>();string temporary=output+".temporary-preference.json";
        void Check(bool condition,string name){if(!condition)throw new InvalidDataException(name);checks.Add(name);}
        try
        {
            var on=new ProfileValues(16,28,0,0,0,1,new("RTSS",false,60),0,0,true);var off=on with{SeparatePl2=false};off.Validate();Check(off.EffectivePl2==17&&off.Pl2==28,"off keeps manual28 and applies17");
            var below=off with{Pl1=30,Pl2=20};below.Validate();Check(below.EffectivePl2==31,"off keeps manual below changed PL1");
            bool rejected=false;try{(below with{SeparatePl2=true}).Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"enabled PL2 below PL1 rejected");
            var old=JsonSerializer.SerializeToNode(on with{SeparatePl2=null},Backend.Program.Json)!.AsObject();old.Remove("separatePl2");var legacy=JsonSerializer.Deserialize<ProfileValues>(old.ToJsonString(),Backend.Program.Json)!;legacy.Validate();Check(legacy.SeparatePl2 is null&&legacy.SeparateEnabled&&legacy.EffectivePl2==28,"legacy profile imports without invented selector");
            var flags=PerformanceProfiles.Groups.ToDictionary(g=>g,_=>true);var global=new ProfileEntry("global","global","",false,true,on,off,DateTimeOffset.UtcNow);var game=new ProfileEntry("game","fixture","C:\\onex-fixture.exe",true,false,off,off,DateTimeOffset.UtcNow);var document=new ProfileDocument(2,global,[game],flags);
            Check(!ProfileResolution.Resolve(document,game,true).SeparateEnabled,"game owns disabled PL2 when TDP group enabled");flags["TDP"]=false;Check(ProfileResolution.Resolve(document,game,true).SeparateEnabled,"global PL2 selector wins when game TDP group excluded");Check(ProfileResolution.Resolve(document,global,true).SeparateEnabled&&!ProfileResolution.Resolve(document,global,false).SeparateEnabled,"AC and DC selectors independent");
            await File.WriteAllTextAsync(temporary,"{\"schemaVersion\":1,\"pl1Watts\":16,\"pl2Watts\":17,\"updatedUtc\":\"2026-10-28T00:00:00Z\"}");var store=new PowerPreferenceStore(temporary);Check(store.Load()?.SeparatePl2 is null,"legacy power preference remains unknown before UI migration");
            store.Save(16,17,false,28);var loaded=new PowerPreferenceStore(temporary).Load()!;Check(loaded.SeparatePl2==false&&loaded.ManualPl2Watts==28,"new controller reloads disabled mode and manual28");store.Save(17,18);Check(store.Load()?.ManualPl2Watts==28&&store.Load()?.SeparatePl2==false,"unrelated numeric preservation retains selector");
            string unchanged=await File.ReadAllTextAsync(temporary);store.Save(17,18);Check(await File.ReadAllTextAsync(temporary)==unchanged,"Numeric-only preservation does not rewrite identical preference bytes or timestamps");
            store.Save(17,18,false,28);Check(await File.ReadAllTextAsync(temporary)==unchanged,"Identical explicit PL2 metadata leaves the owner modification time intact");
            rejected=false;try{store.Save(17,20,false,28);}catch(InvalidDataException){rejected=true;}Check(rejected&&store.Load()?.Pl2Watts==18,"inconsistent disabled preference rejected atomically");
            await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=true,checks,hardwareWrites=0,actualUserPreferenceFilesTouched=false},Backend.Program.Json));return 0;
        }
        catch(Exception error){await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=false,checks,error=error.ToString()},Backend.Program.Json));return 1;}
        finally{File.Delete(temporary);File.Delete(temporary+".tmp");}
    }
}
