// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;

internal sealed record CpuTuningPolicy(int? PerfFrequency=null,int? EffFrequency=null,int? PerfMaximum=null,int? EffMaximum=null,int? PerfMinimum=null,int? EffMinimum=null,int? PerfEpp=null,int? EffEpp=null,int? PerfAwakeMinimum=null,int? EffAwakeMinimum=null,int? PerfAwakeMaximum=null,int? EffAwakeMaximum=null)
{
    internal static readonly (string Name,string Alias,bool Frequency)[] Fields=[
        (nameof(PerfFrequency),"freq1",true),(nameof(EffFrequency),"freq0",true),
        (nameof(PerfMaximum),"max1",false),(nameof(EffMaximum),"max0",false),
        (nameof(PerfMinimum),"min1",false),(nameof(EffMinimum),"min0",false),
        (nameof(PerfEpp),"epp1",false),(nameof(EffEpp),"epp0",false),
        (nameof(PerfAwakeMinimum),"parkMin1",false),(nameof(EffAwakeMinimum),"parkMin0",false),
        (nameof(PerfAwakeMaximum),"parkMax1",false),(nameof(EffAwakeMaximum),"parkMax0",false)];
    internal int? Value(string alias)=>alias switch{"freq1"=>PerfFrequency,"freq0"=>EffFrequency,"max1"=>PerfMaximum,"max0"=>EffMaximum,"min1"=>PerfMinimum,"min0"=>EffMinimum,"epp1"=>PerfEpp,"epp0"=>EffEpp,"parkMin1"=>PerfAwakeMinimum,"parkMin0"=>EffAwakeMinimum,"parkMax1"=>PerfAwakeMaximum,"parkMax0"=>EffAwakeMaximum,_=>throw new InvalidDataException("Unknown CPU tuning field")};
    internal CpuTuningPolicy With(string alias,int? value)=>alias switch{"freq1"=>this with{PerfFrequency=value},"freq0"=>this with{EffFrequency=value},"max1"=>this with{PerfMaximum=value},"max0"=>this with{EffMaximum=value},"min1"=>this with{PerfMinimum=value},"min0"=>this with{EffMinimum=value},"epp1"=>this with{PerfEpp=value},"epp0"=>this with{EffEpp=value},"parkMin1"=>this with{PerfAwakeMinimum=value},"parkMin0"=>this with{EffAwakeMinimum=value},"parkMax1"=>this with{PerfAwakeMaximum=value},"parkMax0"=>this with{EffAwakeMaximum=value},_=>throw new InvalidDataException("Unknown CPU tuning field")};
    internal bool Empty=>Fields.All(item=>Value(item.Alias)==null);
    internal void Validate()
    {
        foreach(var field in Fields)if(Value(field.Alias) is{} value&&(value<0||value>(field.Frequency?64000:100)))throw new InvalidDataException("CPU tuning value is out of range");
        foreach(var pair in new[]{("min0","max0"),("min1","max1"),("parkMin0","parkMax0"),("parkMin1","parkMax1")})if(Value(pair.Item1) is{} minimum&&Value(pair.Item2) is{} maximum&&minimum>maximum)throw new InvalidDataException("CPU minimum cannot exceed maximum");
    }
}
internal sealed record CpuIndexPair(uint Ac,uint Dc,uint? DefaultAc,uint? DefaultDc,bool Available=true,string? Error=null)
{internal uint Side(bool onAc)=>onAc?Ac:Dc;}
internal sealed record CpuNativeSnapshot(Guid Scheme,bool OnAc,Dictionary<string,CpuIndexPair> Values);
internal sealed record CpuIndexWrite(string Alias,bool OnAc,uint Value);
internal interface ICpuPowerPolicies
{
    CpuNativeSnapshot Read(Guid? scheme=null);
    // Implementations validate the scheme, write/read back only listed aliases and
    // roll a partially failed group back without activating another power plan.
    void Write(CpuNativeSnapshot before,IReadOnlyList<CpuIndexWrite> writes);
}
internal sealed record CpuOwnedIndex(Guid Scheme,string Alias,bool OnAc,uint Original,uint Applied);
internal sealed record CpuLeasePlan(Guid Scheme,CpuTuningPolicy? Ac,CpuTuningPolicy? Dc);
internal sealed record CpuLeaseDocument(int Schema,string Phase,List<CpuOwnedIndex> Owned,List<CpuLeasePlan> Plans);

internal sealed class CpuTuningLease
{
    private readonly ICpuPowerPolicies native;
    private readonly Action<CpuLeaseDocument?> persist;
    private CpuLeaseDocument? document;
    internal CpuTuningLease(ICpuPowerPolicies native,CpuLeaseDocument? initial,Action<CpuLeaseDocument?> persist)
    {this.native=native;this.persist=persist;document=initial;Validate(initial);}
    internal CpuLeaseDocument? Document=>document;
    private static void Validate(CpuLeaseDocument? value)
    {
        if(value==null)return;
        if(value.Schema!=1||value.Phase is not("active" or "applying")||value.Owned.Count>96||value.Owned.GroupBy(item=>(item.Scheme,item.Alias,item.OnAc)).Any(group=>group.Count()!=1))throw new InvalidDataException("Invalid CPU tuning lease");
        foreach(var entry in value.Owned)if(!CpuTuningPolicy.Fields.Any(field=>field.Alias==entry.Alias))throw new InvalidDataException("Unknown CPU lease alias");
        foreach(var plan in value.Plans){plan.Ac?.Validate();plan.Dc?.Validate();}
    }
    internal CpuTuningPolicy? ActualPolicy(CpuNativeSnapshot? snapshot=null)
    {
        snapshot??=native.Read();var plan=document?.Plans.FirstOrDefault(value=>value.Scheme==snapshot.Scheme);var intent=snapshot.OnAc?plan?.Ac:plan?.Dc;
        if(intent==null)return document?.Owned.Count>0?new CpuTuningPolicy():null;
        var result=intent;
        foreach(var field in CpuTuningPolicy.Fields)if(intent.Value(field.Alias)!=null)
        {if(!snapshot.Values.TryGetValue(field.Alias,out var actual)||!actual.Available)throw new IOException("CPU setting unavailable: "+field.Name);result=result.With(field.Alias,checked((int)actual.Side(snapshot.OnAc)));}
        return result;
    }
    internal uint OsValue(CpuNativeSnapshot snapshot,string alias,bool onAc)=>document?.Owned.FirstOrDefault(value=>value.Scheme==snapshot.Scheme&&value.Alias==alias&&value.OnAc==onAc)?.Original??snapshot.Values[alias].Side(onAc);
    internal void Apply(CpuTuningPolicy? requested)
    {
        requested?.Validate();if(requested?.Empty==true)requested=null;
        var before=document;var current=native.Read();var snapshots=new Dictionary<Guid,CpuNativeSnapshot>{{current.Scheme,current}};
        foreach(var scheme in before?.Owned.Select(value=>value.Scheme).Distinct()??[])
            if(!snapshots.ContainsKey(scheme))snapshots[scheme]=native.Read(scheme);
        var owned=before?.Owned.ToList()??[];var plans=before?.Plans.ToList()??[];
        var changes=new Dictionary<Guid,Dictionary<(string Alias,bool OnAc),uint>>();
        void Change(Guid scheme,string alias,bool onAc,uint value)
        {if(!changes.TryGetValue(scheme,out var values))changes[scheme]=values=[];values[(alias,onAc)]=value;}
        foreach(var entry in owned.ToArray())
        {
            bool remove=requested==null||entry.Scheme!=current.Scheme||entry.OnAc==current.OnAc&&requested.Value(entry.Alias)==null;
            if(remove){Change(entry.Scheme,entry.Alias,entry.OnAc,entry.Original);owned.Remove(entry);}
        }
        plans=plans.Where(plan=>requested!=null&&plan.Scheme==current.Scheme).ToList();
        if(requested!=null)
        {
            foreach(var field in CpuTuningPolicy.Fields)if(requested.Value(field.Alias) is{} value)
            {
                if(!current.Values.TryGetValue(field.Alias,out var state)||!state.Available)throw new InvalidOperationException("CPU setting unavailable: "+field.Name);
                var previous=owned.FirstOrDefault(entry=>entry.Scheme==current.Scheme&&entry.Alias==field.Alias&&entry.OnAc==current.OnAc);
                if(previous!=null)owned.Remove(previous);
                owned.Add(new(current.Scheme,field.Alias,current.OnAc,previous?.Original??state.Side(current.OnAc),(uint)value));Change(current.Scheme,field.Alias,current.OnAc,(uint)value);
            }
            var plan=plans.FirstOrDefault(value=>value.Scheme==current.Scheme)??new(current.Scheme,null,null);plans.RemoveAll(value=>value.Scheme==current.Scheme);
            plans.Add(current.OnAc?plan with{Ac=requested}:plan with{Dc=requested});
        }
        foreach(var group in changes)
        {
            var state=snapshots[group.Key];
            foreach(bool ac in new[]{false,true})foreach(var pair in new[]{("min0","max0"),("min1","max1"),("parkMin0","parkMax0"),("parkMin1","parkMax1")})
            {
                if(!group.Value.ContainsKey((pair.Item1,ac))&&!group.Value.ContainsKey((pair.Item2,ac)))continue;
                if(!state.Values[pair.Item1].Available||!state.Values[pair.Item2].Available)throw new InvalidOperationException("Cannot validate the CPU min/max pair");
                uint Value(string alias)=>group.Value.TryGetValue((alias,ac),out var value)?value:state.Values[alias].Side(ac);
                if(Value(pair.Item1)>Value(pair.Item2))throw new InvalidOperationException("CPU minimum would exceed its maximum");
            }
        }
        var next=owned.Count==0?null:new CpuLeaseDocument(1,"active",owned,plans);Validate(next);
        if(changes.Count==0&&before==null&&next==null)return;
        var recovery=(before?.Owned??[]).Concat(owned).GroupBy(value=>(value.Scheme,value.Alias,value.OnAc)).Select(group=>group.First()).ToList();
        var journal=new CpuLeaseDocument(1,"applying",recovery,plans);Validate(journal);
        persist(journal);document=journal;var applied=new List<Guid>();
        try
        {
            foreach(var group in changes)
            {applied.Add(group.Key);native.Write(snapshots[group.Key],group.Value.Select(value=>new CpuIndexWrite(value.Key.Alias,value.Key.OnAc,value.Value)).ToArray());}
            persist(next);document=next;
        }
        catch(Exception failure)
        {
            try
            {
                foreach(var scheme in applied.AsEnumerable().Reverse())
                {var original=snapshots[scheme];var actual=native.Read(scheme);native.Write(actual,changes[scheme].Keys.Select(key=>new CpuIndexWrite(key.Alias,key.OnAc,original.Values[key.Alias].Side(key.OnAc))).ToArray());}
                persist(before);document=before;
            }
            catch(Exception recoveryFailure){throw new IOException(failure.Message+"; CPU recovery remains journaled: "+recoveryFailure.Message,failure);}
            throw;
        }
    }
}
