// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;

namespace ONEXTweaks.WidgetBridge;

// The Bridge calls this service under powerGate. Merely constructing or reading
// it never changes Windows policy; the durable lease only exists after a write.
internal sealed class CpuTuningService
{
    private readonly CpuPowerPoliciesNative native=new();
    private readonly CpuTuningLease? lease;
    private readonly string? loadError;
    private readonly Lazy<(int Performance,int Efficiency)> topology=new(CpuPowerPoliciesNative.Topology);
    private long userWrites,repairWrites;
    internal CpuTuningService(string reports)
    {
        string path=Path.Combine(reports,"..","cpu-tuning-lease.json");
        try
        {
            var initial=File.Exists(path)?JsonSerializer.Deserialize<CpuLeaseDocument>(File.ReadAllText(path),Backend.Program.Json)??throw new InvalidDataException("Empty CPU ownership lease"):null;
            void Persist(CpuLeaseDocument? value)
            {
                if(value==null){if(File.Exists(path))File.Delete(path);return;}
                string temporary=path+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(value,Backend.Program.Json));File.Move(temporary,path,true);
            }
            lease=new(native,initial,Persist);
        }
        catch(Exception error){loadError=error.Message;}
    }
    internal CpuTuningPolicy? CapturePolicy()
    {if(lease?.Document==null)return null;return lease.ActualPolicy();}
    internal bool HasLease=>lease?.Document!=null;
    internal bool Matches(CpuTuningPolicy? policy)
    {if(policy?.Empty==true)policy=null;if(policy==null&&!HasLease)return true;return lease!=null&&lease.ActualPolicy()==policy;}
    internal void Apply(CpuTuningPolicy? policy,bool userInput=false)
    {
        if(lease==null){if(policy==null)return;throw new IOException(loadError??"CPU tuning is unavailable");}
        if(policy==null&&lease.Document==null)return;
        lease.Apply(policy);
        if(userInput)Interlocked.Increment(ref userWrites);
    }
    internal void Repair(CpuTuningPolicy? policy){Apply(policy);Interlocked.Increment(ref repairWrites);}
    internal void ReapplyIntent()
    {
        if(lease?.Document==null)return;var state=native.Read();var plan=lease.Document.Plans.FirstOrDefault(value=>value.Scheme==state.Scheme);
        var policy=state.OnAc?plan?.Ac:plan?.Dc;
        // Empty current-side ownership still needs a release of an older plan/side.
        lease.Apply(policy);
    }
    internal object Read()
    {
        try
        {
            if(lease==null)throw new IOException(loadError??"CPU tuning is unavailable");
            var state=native.Read();var cores=topology.Value;var plan=lease.Document?.Plans.FirstOrDefault(value=>value.Scheme==state.Scheme);
            var fields=CpuTuningPolicy.Fields.ToDictionary(field=>field.Alias,field=>
            {
                var value=state.Values[field.Alias];bool classExists=field.Alias.EndsWith("1",StringComparison.Ordinal)?cores.Performance>0:cores.Efficiency>0;
                return new{field.Name,available=value.Available&&classExists,value=value.Side(state.OnAc),ac=value.Ac,dc=value.Dc,osValue=lease.OsValue(state,field.Alias,state.OnAc),defaultValue=state.OnAc?value.DefaultAc:value.DefaultDc,minimum=0,maximum=field.Frequency?64000:100,frequency=field.Frequency,error=classExists?value.Error:"This CPU core class is not present"};
            });
            return new{available=true,onAc=state.OnAc,scheme=state.Scheme.ToString(),performanceCores=cores.Performance,efficiencyCores=cores.Efficiency,policy=state.OnAc?plan?.Ac:plan?.Dc,actualPolicy=lease.ActualPolicy(state),userWrites=Interlocked.Read(ref userWrites),repairWrites=Interlocked.Read(ref repairWrites),fields,error=(string?)null};
        }
        catch(Exception error){return new{available=false,error=error.Message};}
    }
}
