// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class FanProfileVerification
{
    internal static async Task<int> Run(string report)
    {
        var checks=new List<string>();string? error=null;string file=report+".fan-model.json";File.Delete(file);
        void Check(bool value,string name){if(!value)throw new IOException(name);checks.Add(name);}
        try
        {
            var valid=new HashSet<string>{"global","manual","game",FanPreferences.AutoId};bool Exists(string id)=>valid.Contains(id);
            Check(FanProfiles.ResolveId("manual",true,false,FanPreferences.AutoId,Exists)==FanPreferences.AutoId,"Explicit Performance selection replaces an earlier manual hold");
            Check(FanProfiles.ResolveId("game",false,true,FanPreferences.AutoId,Exists)==FanPreferences.AutoId,"Explicit Performance preset is selected consistently on the Fan page");
            Check(FanProfiles.ResolveId("game",false,true,"manual",Exists)=="manual","Explicit Performance preset wins after the manual session hold ends");
            Check(FanProfiles.ResolveId("manual",false,false,null,Exists)=="manual","Follow Fan retains its independent manual choice");
            Check(FanProfiles.ResolveId("manual",false,false,"missing",Exists)=="global","Deleted linked preset safely falls back to Global");
            FanPreferences live=new(0);bool ac=true;ProfileSession? session=null;var alive=new HashSet<long>();
            ScopedProfileCatalog<FanPreferences> Open()=>new(file,()=>live,value=>live=value,value=>value.Validate(),()=>ac,()=>session,value=>alive.Contains(value.StartedUtcTicks),rememberManualFallback:true);
            void Change(ScopedProfileCatalog<FanPreferences> catalog,object request){using var doc=JsonDocument.Parse(JsonSerializer.Serialize(request,Backend.Program.Json));catalog.Mutate(doc.RootElement,pid=>session?.ProcessId==pid?session:null);}
            var catalog=Open();catalog.Tick();Change(catalog,new{operation="create",name="Manual QA"});string manual=catalog.ActiveId;live=new(1);catalog.Record();
            session=new(101,1000,@"C:\ONEXTweaksQA\FanGame.exe","FanGame");alive.Add(1000);Change(catalog,new{operation="create",processId=101,startedUtcTicks="1000"});string game=catalog.ActiveId;live=new(2);catalog.Record();
            Change(catalog,new{operation="select",id=manual});catalog=Open();catalog.Tick();Check(catalog.ActiveId==manual&&live.Mode==1,"Manual Fan selection survives catalog reload during the held game session");
            alive.Remove(1000);session=session with{StartedUtcTicks=2000};alive.Add(2000);catalog.Tick();Check(catalog.ActiveId==game&&live.Mode==2,"Restarted process restores its own Fan profile");
            alive.Remove(2000);session=null;catalog.Tick();Check(catalog.ActiveId==manual&&live.Mode==1,"An app ending falls back to the last manual Fan profile");
            Change(catalog,new{operation="split",enabled=true});live=new(2);catalog.Record();ac=false;catalog.Tick();Check(live.Mode==1,"Fan AC autosave leaves DC unchanged");ac=true;catalog.Tick();Check(live.Mode==2,"Fan source change restores its saved side");
            string saved=File.ReadAllText(file);live=new(3);bool rejected=false;try{catalog.Record();}catch(InvalidDataException){rejected=true;}Check(rejected&&File.ReadAllText(file)==saved,"Unsupported hardware mode cannot overwrite the selected Fan profile");live=new(2);
            Change(catalog,new{operation="keep",id=manual});using(var doc=JsonDocument.Parse(catalog.Read()))Check(catalog.ActiveId=="global"&&doc.RootElement.GetProperty("entries").EnumerateArray().Any(entry=>entry.GetProperty("id").GetString()==manual),"Keep It retains the Fan preset and selects Global");
            Change(catalog,new{operation="delete",id=game});using(var doc=JsonDocument.Parse(catalog.Read()))Check(!doc.RootElement.GetProperty("entries").EnumerateArray().Any(entry=>entry.GetProperty("id").GetString()==game),"Delete removes only the requested Fan preset");
        }
        catch(Exception failure){error=failure.ToString();}
        finally{File.Delete(file);}
        await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed=error==null,checks,error,modelOnly=true,hardwareTested=false,physicalOwnerInputTested=false},Backend.Program.Json));return error==null?0:1;
    }
}
