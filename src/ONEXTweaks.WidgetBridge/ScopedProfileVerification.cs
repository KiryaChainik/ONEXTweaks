// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;

namespace ONEXTweaks.WidgetBridge;

internal static class ScopedProfileVerification
{
    private sealed record Value(int Number);
    internal static async Task<int> Run(string report)
    {
        string file=report+".model-store.json";File.Delete(file);
        var checks=new List<string>();Value live=new(1);bool ac=true,fail=false;ProfileSession? current=null;
        var alive=new HashSet<int>();
        void Apply(Value value){if(fail){fail=false;throw new IOException("Injected actuator failure");}live=value;}
        void Validate(Value value){if(value.Number is <0 or >100)throw new InvalidDataException("Value is out of range");}
        ScopedProfileCatalog<Value> Open()=>new(file,()=>live,Apply,Validate,()=>ac,()=>current,s=>alive.Contains(s.ProcessId));
        void Check(bool ok,string name){if(!ok)throw new IOException(name);checks.Add(name);}
        void Change(ScopedProfileCatalog<Value> catalog,object request){using var json=JsonDocument.Parse(JsonSerializer.Serialize(request,Backend.Program.Json));catalog.Mutate(json.RootElement,pid=>current?.ProcessId==pid?current:null);}
        string? error=null;
        try
        {
            var catalog=Open();catalog.Tick();Check(live.Number==1&&catalog.ActiveId=="global","Fresh catalog does not apply a different value");
            Change(catalog,new{operation="create",name="Manual test"});string manual=catalog.ActiveId;live=new(2);catalog.Record();
            live=new(0);catalog=Open();catalog.Tick();Check(live.Number==2&&catalog.ActiveId==manual,"Manual choice and autosaved values survive reload");
            current=new(101,1000,@"C:\Games\ProfileProbe.exe","ProfileProbe");alive.Add(101);
            Change(catalog,new{operation="create",processId=101,startedUtcTicks="1000"});string game=catalog.ActiveId;live=new(3);catalog.Record();
            Change(catalog,new{operation="select",id=manual});catalog.Tick();Check(catalog.ActiveId==manual&&live.Number==2,"Manual selection suppresses a running process profile");
            live=new(0);catalog=Open();catalog.Tick();Check(catalog.ActiveId==manual&&live.Number==2,"Manual process-session override survives helper reload");
            alive.Remove(101);current=null;catalog.Tick();current=new(102,2000,@"C:\Games\ProfileProbe.exe","ProfileProbe");alive.Add(102);catalog.Tick();
            Check(catalog.ActiveId==game&&live.Number==3,"New process session activates its own profile again");
            Change(catalog,new{operation="split",enabled=true});live=new(4);catalog.Record();ac=false;catalog.Tick();Check(live.Number==3,"AC edit does not overwrite DC values");
            ac=true;catalog.Tick();Check(live.Number==4,"Power source switch reapplies its saved side");
            string saved=File.ReadAllText(file);fail=true;bool rejected=false;
            try{Change(catalog,new{operation="select",id="global"});}catch(IOException){rejected=true;}
            Check(rejected&&catalog.ActiveId==game&&live.Number==4&&File.ReadAllText(file)==saved,"Failed apply preserves active choice,file and actuator value");
            rejected=false;try{Change(catalog,new{operation="create",processId=102,startedUtcTicks="1000"});}catch(IOException){rejected=true;}
            Check(rejected&&File.ReadAllText(file)==saved,"Stale process start time is rejected before writing");
            rejected=false;try{Change(catalog,new{operation="create",processId=102,startedUtcTicks="2000"});}catch(InvalidOperationException){rejected=true;}
            Check(rejected&&File.ReadAllText(file)==saved,"Duplicate executable binding is rejected");
            current=null;Change(catalog,new{operation="select",id=manual});Change(catalog,new{operation="keep",id=manual});
            using(var state=JsonDocument.Parse(catalog.Read()))Check(catalog.ActiveId=="global"&&state.RootElement.GetProperty("entries").EnumerateArray().Any(entry=>entry.GetProperty("id").GetString()==manual),"Keep It returns Global and preserves the saved profile");
            Change(catalog,new{operation="delete",id=game});using(var state=JsonDocument.Parse(catalog.Read()))Check(!state.RootElement.GetProperty("entries").EnumerateArray().Any(entry=>entry.GetProperty("id").GetString()==game),"Delete removes only the requested profile");
            saved=File.ReadAllText(file);live=new(200);rejected=false;try{catalog.Record();}catch(InvalidDataException){rejected=true;}
            Check(rejected&&File.ReadAllText(file)==saved,"Invalid autosave cannot replace the saved catalog");
        }
        catch(Exception failure){error=failure.ToString();}
        await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed=error==null,checks,error,modelOnly=true,hardwareTested=false,realProcessTested=false},Backend.Program.Json));
        return error==null?0:1;
    }
}
