using System.Text.Json;
using ONEXTweaks.WidgetBridge;

var checks=new List<string>();string? error=null;
void Check(bool value,string text){if(!value)throw new IOException(text);checks.Add(text);}
try
{
    var api=new FakeCpu();CpuLeaseDocument? saved=null;bool failActiveSave=false,failJournal=false;
    void Save(CpuLeaseDocument? value){if(failJournal&&value?.Phase=="applying")throw new IOException("Injected journal failure");if(failActiveSave&&value?.Phase=="active"){failActiveSave=false;throw new IOException("Injected final persistence failure");}saved=value;}
    var lease=new CpuTuningLease(api,null,Save);var baseline=api.Fingerprint();
    lease.Apply(new(PerfFrequency:2200,PerfEpp:30));
    Check(api.Value("freq1",true)==2200&&api.Value("epp1",true)==30,"P frequency and EPP are applied to the selected AC side");
    Check(api.Value("freq1",false)==0&&saved?.Owned.Count==2,"Inactive DC settings stay unchanged");
    lease.Apply(new(PerfFrequency:1500,EffMinimum:20));
    Check(api.Value("freq1",true)==1500&&api.Value("epp1",true)==50&&api.Value("min0",true)==20,"Profile switch restores removed fields while retaining the original baseline");
    Check(saved!.Owned.Single(value=>value.Alias=="freq1").Original==0,"A second profile cannot overwrite the first pre-override frequency");
    api.OnAc=false;lease.Apply(new(EffFrequency:1000));
    Check(api.Value("freq0",false)==1000&&api.Value("freq1",true)==1500,"AC and DC own separate native values");
    lease.Apply(null);Check(api.Fingerprint()==baseline&&saved==null,"Unmanaged scope restores both sides and releases the lease");
    api.OnAc=true;lease.Apply(new(EffAwakeMinimum:60));lease.Apply(null);
    Check(api.Value("parkMin0",true)==50,"Original non-default parking50 is preserved instead of factory75");
    lease.Apply(new(PerfFrequency:2000));api.External("epp0",true,70);lease.Apply(null);
    Check(api.Value("epp0",true)==70,"Unowned external settings survive cleanup");api.External("epp0",true,50);
    lease.Apply(new(PerfFrequency:2000));api.External("freq1",true,1800);
    Check(lease.ActualPolicy()!.PerfFrequency==1800,"Capture reports actual drift instead of pretending the requested cap is active");
    lease.Apply(new(PerfFrequency:2000));Check(api.Value("freq1",true)==2000,"The saved intent can be reapplied after a Windows reset");lease.Apply(null);
    failJournal=true;int writes=api.WriteCalls;bool rejected=false;try{lease.Apply(new(PerfEpp:10));}catch(IOException){rejected=true;}failJournal=false;
    Check(rejected&&api.WriteCalls==writes&&saved==null,"Journal persistence must succeed before any native write");
    failActiveSave=true;rejected=false;try{lease.Apply(new(PerfFrequency:2400,EffMinimum:25));}catch(IOException){rejected=true;}
    Check(rejected&&api.Fingerprint()==baseline&&saved==null&&lease.Document==null,"Final persistence failure restores exact native values and ownership");
    api.FailWrite=true;rejected=false;try{lease.Apply(new(PerfFrequency:2300,PerfEpp:20));}catch(IOException){rejected=true;}
    Check(rejected&&api.Fingerprint()==baseline&&saved==null,"Partially failed native group is rolled back without retaining a false policy");
    api.SetAvailable("epp1",false);writes=api.WriteCalls;rejected=false;try{lease.Apply(new(PerfEpp:25));}catch(InvalidOperationException){rejected=true;}api.SetAvailable("epp1",true);
    Check(rejected&&api.WriteCalls==writes&&saved==null,"Unsupported fields are rejected before saving or writing");
    api.SetAvailable("parkMax0",false);lease.Apply(new(PerfEpp:20));lease.Apply(null);api.SetAvailable("parkMax0",true);
    Check(saved==null&&api.Fingerprint()==baseline,"An unrelated unsupported parking pair cannot block an available EPP change");
    rejected=false;try{lease.Apply(new(PerfMinimum:90,PerfMaximum:50));}catch(InvalidDataException){rejected=true;}
    Check(rejected&&api.Fingerprint()==baseline,"Invalid explicit min/max policy cannot touch native state");
    rejected=false;try{lease.Apply(new(PerfMinimum:101));}catch(InvalidDataException){rejected=true;}
    Check(rejected&&api.Fingerprint()==baseline,"Out-of-range percentages are rejected");
    lease.Apply(new(PerfFrequency:2100));Guid old=api.Active;api.SwitchScheme();Guid second=api.Active;lease.Apply(new(PerfEpp:35));
    Check(api.Active==second&&api.Value("freq1",true,old)==0&&api.Value("epp1",true)==35,"A plan transition restores old owned values without reactivating the old plan");lease.Apply(null);
    Check(api.Value("epp1",true)==50&&saved==null,"The new plan also returns to its own fresh baseline");
    api.Active=old;var journal=new CpuLeaseDocument(1,"applying",[new(old,"freq1",true,0,2500)],[new(old,new(PerfFrequency:2500),null)]);api.External("freq1",true,1300);
    var recovered=new CpuTuningLease(api,journal,Save);Check(recovered.ActualPolicy()!.PerfFrequency==1300,"A recovered journal reads the actual incomplete transition");recovered.Apply(null);
    Check(api.Value("freq1",true)==0&&saved==null,"Crash recovery can release only the journaled fields");
    lease=new(api,null,Save);lease.Apply(new(PerfFrequency:2200));var restarted=new CpuTuningLease(api,saved,Save);restarted.Apply(null);
    Check(api.Value("freq1",true)==0,"Original values survive a helper reconstruction");
    api.BreakRecovery=true;rejected=false;try{restarted.Apply(new(PerfFrequency:2000));}catch(IOException){rejected=true;}
    Check(rejected&&saved?.Phase=="applying"&&restarted.Document?.Phase=="applying","A failed native recovery retains a durable journal instead of clearing ownership");
    api.BreakRecovery=false;restarted.Apply(null);Check(saved==null&&api.Value("freq1",true)==0,"A later recovery can release the retained journal exactly");
}
catch(Exception failure){error=failure.ToString();}
Console.WriteLine(JsonSerializer.Serialize(new{passed=error==null,checks,error,modelOnly=true,hardwareWrites=0,realWindowsApiTested=false}));return error==null?0:1;

sealed class FakeCpu:ICpuPowerPolicies
{
    internal Guid Active=Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");internal bool OnAc=true,FailWrite,BreakRecovery;internal int WriteCalls;
    private readonly Dictionary<Guid,Dictionary<string,CpuIndexPair>> plans=[];
    internal FakeCpu(){plans[Active]=Initial();}
    private static Dictionary<string,CpuIndexPair> Initial()=>CpuTuningPolicy.Fields.ToDictionary(field=>field.Alias,field=>
    {
        uint value=field.Frequency?0u:field.Alias.StartsWith("epp")?50u:field.Alias.StartsWith("max")||field.Alias.StartsWith("parkMax")?100u:field.Alias.StartsWith("min")?5u:field.Alias=="parkMin0"?50u:0u;
        return new CpuIndexPair(value,value,field.Alias=="parkMin0"?75u:value,value);
    });
    public CpuNativeSnapshot Read(Guid? scheme=null){Guid id=scheme??Active;return new(id,OnAc,new(plans[id]));}
    internal uint Value(string alias,bool ac,Guid? scheme=null)=>plans[scheme??Active][alias].Side(ac);
    internal void External(string alias,bool ac,uint value){var entry=plans[Active][alias];plans[Active][alias]=ac?entry with{Ac=value}:entry with{Dc=value};}
    internal void SetAvailable(string alias,bool value)=>plans[Active][alias]=plans[Active][alias] with{Available=value};
    internal string Fingerprint()=>JsonSerializer.Serialize(plans.OrderBy(plan=>plan.Key).Select(plan=>new{plan.Key,Values=plan.Value.OrderBy(value=>value.Key)}));
    internal void SwitchScheme(){Active=Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a");plans.TryAdd(Active,Initial());}
    public void Write(CpuNativeSnapshot before,IReadOnlyList<CpuIndexWrite> writes)
    {
        WriteCalls++;var rollback=new Dictionary<string,CpuIndexPair>(plans[before.Scheme]);
        try{foreach(var change in writes){var value=plans[before.Scheme][change.Alias];plans[before.Scheme][change.Alias]=change.OnAc?value with{Ac=change.Value}:value with{Dc=change.Value};if(BreakRecovery)throw new IOException("Injected native recovery failure");if(FailWrite){FailWrite=false;throw new IOException("Injected native failure");}}}
        catch{if(!BreakRecovery)plans[before.Scheme]=rollback;throw;}
    }
}
