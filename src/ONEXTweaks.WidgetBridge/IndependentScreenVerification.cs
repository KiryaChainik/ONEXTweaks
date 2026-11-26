// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class IndependentScreenVerification
{
    private sealed record Value(int Number);
    internal static async Task<int> Run(string report)
    {
        string file=report+".model-store.json";File.Delete(file);
        bool ac=false;Value live=new(2);ProfileSession? current=null;
        var alive=new HashSet<int>();var checks=new List<string>();string? error=null;
        void Check(bool ok,string name){if(!ok)throw new IOException(name);checks.Add(name);}
        void Validate(Value value){if(value.Number is <0 or >100)throw new InvalidDataException("Invalid test value");}
        ScopedProfileCatalog<Value> Open(string? legacy=null)=>new(file,()=>live,value=>live=value,Validate,()=>ac,()=>current,s=>alive.Contains(s.ProcessId),sourceIndependent:true,initialIndependentId:legacy);
        void Change(ScopedProfileCatalog<Value> catalog,object request){using var json=JsonDocument.Parse(JsonSerializer.Serialize(request,Backend.Program.Json));catalog.Mutate(json.RootElement,pid=>current?.ProcessId==pid?current:null);}
        ScopedProfileDocument<Value> Read()=>JsonSerializer.Deserialize<ScopedProfileDocument<Value>>(File.ReadAllText(file),Backend.Program.Json)!;
        try
        {
            var stamp=DateTimeOffset.UtcNow;
            var original=new ScopedProfileDocument<Value>(1,new("global","Global Profile",null,true,new(1),new(2),stamp),[new("qa-linked","QA linked",null,true,new(3),new(4),stamp)],"global","manual",null);
            File.WriteAllText(file,JsonSerializer.Serialize(original,Backend.Program.Json));
            var catalog=Open("qa-linked");catalog.Tick();
            Check(catalog.ActiveId=="qa-linked"&&live.Number==4,"Migration keeps the formerly selected preset and current source values");
            var saved=Read();Check(saved.Global.Ac==original.Global.Ac&&saved.Global.Dc==original.Global.Dc&&saved.Profiles[0].Ac==original.Profiles[0].Ac&&saved.Profiles[0].Dc==original.Profiles[0].Dc,"Legacy AC/DC settings remain archived intact");
            ac=true;catalog.Tick();Check(live.Number==4,"Changing source does not change independent screen settings");
            Change(catalog,new{operation="select",id="global"});Check(live.Number==2,"Independent Global uses its unified values on either source");
            live=new(9);catalog.Record();saved=Read();Check(saved.Global.Unified==new Value(9)&&saved.Global.Ac==new Value(1)&&saved.Global.Dc==new Value(2),"Autosave changes unified values without erasing legacy sides");
            catalog=Open("qa-linked");catalog.Tick();Check(catalog.ActiveId=="global"&&live.Number==9,"Helper restart ignores old Performance link after first migration");
            string before=File.ReadAllText(file);bool rejected=false;try{Change(catalog,new{operation="split",enabled=true});}catch(InvalidOperationException){rejected=true;}
            Check(rejected&&File.ReadAllText(file)==before&&live.Number==9,"Stale split request is rejected without writes");
            Change(catalog,new{operation="create",name="QA manual"});string manual=catalog.ActiveId;live=new(8);catalog.Record();ac=false;catalog.Tick();
            Check(catalog.ActiveId==manual&&live.Number==8&&catalog.CurrentValues.Number==8,"Created manual preset autosaves independently of source");
            current=new(701,1000,@"C:\QA\ScreenProbe.exe","ScreenProbe");alive.Add(701);Change(catalog,new{operation="create",processId=701,startedUtcTicks="1000"});string game=catalog.ActiveId;live=new(7);catalog.Record();
            Change(catalog,new{operation="select",id=manual});catalog.Tick();Check(live.Number==8&&catalog.ActiveId==manual,"Own manual selection suppresses own running process profile");
            alive.Remove(701);current=new(702,2000,@"C:\QA\ScreenProbe.exe","ScreenProbe");alive.Add(702);catalog.Tick();Check(catalog.ActiveId==game&&live.Number==7,"Own process restart still selects its independent screen preset");
            using var state=JsonDocument.Parse(catalog.Read());Check(state.RootElement.GetProperty("sourceIndependent").GetBoolean()&&state.RootElement.GetProperty("entries").EnumerateArray().All(entry=>!entry.GetProperty("split").GetBoolean()&&entry.GetProperty("ac").GetRawText()==entry.GetProperty("dc").GetRawText()),"UI receives unified values and no source split");
        }
        catch(Exception failure){error=failure.ToString();}
        await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed=error==null,checks,error,modelOnly=true,hardwareTested=false},Backend.Program.Json));return error==null?0:1;
    }
}
