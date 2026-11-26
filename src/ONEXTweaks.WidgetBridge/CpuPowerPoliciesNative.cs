// SPDX-License-Identifier: AGPL-3.0-only
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ONEXTweaks.WidgetBridge;

internal sealed class CpuPowerPoliciesNative:ICpuPowerPolicies
{
    private static readonly Guid Processor=new("54533251-82be-4824-96c1-47b60b740d00"),Balanced=new("381b4222-f694-41f0-9685-ff5bb260df2e");
    internal static readonly Dictionary<string,Guid> Settings=new(StringComparer.Ordinal)
    {
        ["freq0"]=new("75b0ae3f-bce0-45a7-8c89-c9611c25e100"),["freq1"]=new("75b0ae3f-bce0-45a7-8c89-c9611c25e101"),
        ["min0"]=new("893dee8e-2bef-41e0-89c6-b55d0929964c"),["min1"]=new("893dee8e-2bef-41e0-89c6-b55d0929964d"),
        ["max0"]=new("bc5038f7-23e0-4960-96da-33abaf5935ec"),["max1"]=new("bc5038f7-23e0-4960-96da-33abaf5935ed"),
        // Confirmed against the installed Microsoft SDK winnt.h and native readback.
        ["epp0"]=new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863"),["epp1"]=new("36687f9e-e3a5-4dbf-b1dc-15eb381c6864"),
        ["parkMin0"]=new("0cc5b647-c1df-4637-891a-dec35c318583"),["parkMin1"]=new("0cc5b647-c1df-4637-891a-dec35c318584"),
        ["parkMax0"]=new("ea062031-0e34-4ff1-9b6d-eb1059334028"),["parkMax1"]=new("ea062031-0e34-4ff1-9b6d-eb1059334029")
    };
    private readonly Dictionary<string,(uint? Ac,uint? Dc)> defaults=[];
    private static void Check(uint code){if(code!=0)throw new Win32Exception((int)code);}
    private static Guid Active()
    {
        Check(PowerGetActiveScheme(0,out var pointer));
        try{return Marshal.PtrToStructure<Guid>(pointer);}finally{LocalFree(pointer);}
    }
    private static bool OnAc()
    {if(!GetSystemPowerStatus(out var value)||value.Ac>1)throw new IOException("Power source unavailable");return value.Ac==1;}
    public CpuNativeSnapshot Read(Guid? scheme=null)
    {
        Guid selected=scheme??Active();bool ac=OnAc();var values=new Dictionary<string,CpuIndexPair>();
        foreach(var entry in Settings)
        {
            Guid sub=Processor,key=entry.Value;
            uint a=PowerReadACValueIndex(0,ref selected,ref sub,ref key,out uint av),d=PowerReadDCValueIndex(0,ref selected,ref sub,ref key,out uint dv);
            if(!defaults.TryGetValue(entry.Key,out var baseline))
            {
                Guid balanced=Balanced;uint ba=PowerReadACDefaultIndex(0,ref balanced,ref sub,ref key,out uint bav),bd=PowerReadDCDefaultIndex(0,ref balanced,ref sub,ref key,out uint bdv);
                defaults[entry.Key]=baseline=(ba==0?bav:null,bd==0?bdv:null);
            }
            values[entry.Key]=new(av,dv,baseline.Ac,baseline.Dc,a==0&&d==0,a==0&&d==0?null:"Windows setting not exposed ("+a+"/"+d+")");
        }
        if(values.All(value=>!value.Value.Available))throw new IOException("CPU power plan is unavailable");
        return new(selected,ac,values);
    }
    private static void WriteIndex(Guid scheme,CpuIndexWrite change)
    {
        Guid sub=Processor,key=Settings[change.Alias];
        Check(change.OnAc?PowerWriteACValueIndex(0,ref scheme,ref sub,ref key,change.Value):PowerWriteDCValueIndex(0,ref scheme,ref sub,ref key,change.Value));
    }
    public void Write(CpuNativeSnapshot before,IReadOnlyList<CpuIndexWrite> writes)
    {
        var live=Read(before.Scheme);var changed=new List<CpuIndexWrite>();Guid active=Active();
        foreach(var change in writes)
        {
            if(!Settings.ContainsKey(change.Alias)||!live.Values[change.Alias].Available)throw new InvalidOperationException("CPU setting is unavailable");
            uint maximum=change.Alias.StartsWith("freq",StringComparison.Ordinal)?64000u:100u;
            if(change.Value>maximum)throw new ArgumentOutOfRangeException(nameof(writes));
            if(live.Values[change.Alias].Side(change.OnAc)!=before.Values[change.Alias].Side(change.OnAc))throw new IOException("CPU setting changed while applying; reopen the settings");
            if(live.Values[change.Alias].Side(change.OnAc)!=change.Value)changed.Add(change);
        }
        if(changed.Count==0)return;
        try
        {
            foreach(var change in changed)WriteIndex(before.Scheme,change);
            if(Active()!=active)throw new IOException("Active power plan changed during CPU update");
            if(active==before.Scheme){Guid same=active;Check(PowerSetActiveScheme(0,ref same));}
            var actual=Read(before.Scheme);
            if(changed.Any(change=>actual.Values[change.Alias].Side(change.OnAc)!=change.Value))throw new IOException("CPU setting readback mismatch");
        }
        catch(Exception failure)
        {
            try
            {
                foreach(var change in changed)WriteIndex(before.Scheme,change with{Value=live.Values[change.Alias].Side(change.OnAc)});
                if(Active()==active&&active==before.Scheme){Guid same=active;Check(PowerSetActiveScheme(0,ref same));}
                var restored=Read(before.Scheme);
                if(changed.Any(change=>restored.Values[change.Alias].Side(change.OnAc)!=live.Values[change.Alias].Side(change.OnAc)))throw new IOException("CPU rollback readback mismatch");
            }
            catch(Exception recovery){throw new IOException(failure.Message+"; CPU native recovery failed: "+recovery.Message,failure);}
            throw;
        }
    }
    internal static (int Performance,int Efficiency) Topology()
    {
        GetSystemCpuSetInformation(0,0,out uint bytes,0,0);if(bytes==0||bytes>1024*1024)throw new IOException("CPU topology unavailable");
        nint buffer=Marshal.AllocHGlobal((int)bytes);var cores=new HashSet<(ushort Group,byte Core,byte Class)>();
        try
        {
            if(!GetSystemCpuSetInformation(buffer,bytes,out bytes,0,0))throw new Win32Exception();
            for(int offset=0;offset<bytes;)
            {
                int size=Marshal.ReadInt32(buffer,offset);if(size<24||offset+size>bytes)throw new IOException("Invalid CPU topology record");
                if(Marshal.ReadInt32(buffer,offset+4)==0)cores.Add(((ushort)Marshal.ReadInt16(buffer,offset+12),Marshal.ReadByte(buffer,offset+15),Marshal.ReadByte(buffer,offset+18)));
                offset+=size;
            }
            return(cores.Count(value=>value.Class>0),cores.Count(value=>value.Class==0));
        }
        finally{Marshal.FreeHGlobal(buffer);}
    }
    [StructLayout(LayoutKind.Sequential)]private struct PowerStatus{internal byte Ac,Battery,Percent,System;internal uint Remaining,Full;}
    [DllImport("kernel32.dll")]private static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("kernel32.dll")]private static extern nint LocalFree(nint value);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetSystemCpuSetInformation(nint information,uint length,out uint returned,nint process,uint flags);
    [DllImport("powrprof.dll")]private static extern uint PowerGetActiveScheme(nint key,out nint value);
    [DllImport("powrprof.dll")]private static extern uint PowerSetActiveScheme(nint key,ref Guid value);
    [DllImport("powrprof.dll")]private static extern uint PowerReadACValueIndex(nint key,ref Guid scheme,ref Guid sub,ref Guid setting,out uint value);
    [DllImport("powrprof.dll")]private static extern uint PowerReadDCValueIndex(nint key,ref Guid scheme,ref Guid sub,ref Guid setting,out uint value);
    [DllImport("powrprof.dll")]private static extern uint PowerReadACDefaultIndex(nint key,ref Guid personality,ref Guid sub,ref Guid setting,out uint value);
    [DllImport("powrprof.dll")]private static extern uint PowerReadDCDefaultIndex(nint key,ref Guid personality,ref Guid sub,ref Guid setting,out uint value);
    [DllImport("powrprof.dll")]private static extern uint PowerWriteACValueIndex(nint key,ref Guid scheme,ref Guid sub,ref Guid setting,uint value);
    [DllImport("powrprof.dll")]private static extern uint PowerWriteDCValueIndex(nint key,ref Guid scheme,ref Guid sub,ref Guid setting,uint value);
}
