// SPDX-License-Identifier: AGPL-3.0-only
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace ONEXTweaks.WidgetBridge;
internal sealed record CpuPowerSettings(string Scheme,uint BoostAc,uint BoostDc,uint FrequencyAc,uint FrequencyDc,uint Frequency1Ac,uint Frequency1Dc,uint SchedulingAc,uint SchedulingDc,uint ShortSchedulingAc,uint ShortSchedulingDc);
internal sealed record WindowsPowerState(bool Available,bool OnAc,int ModeAc,int ModeDc,CpuPowerSettings? Cpu,string? Error);
internal static class WindowsPowerSettings
{
    private static readonly Guid Processor=new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid Boost=new("be337238-0d82-4146-a960-4f3749d470c7"),Frequency=new("75b0ae3f-bce0-45a7-8c89-c9611c25e100"),Frequency1=new("75b0ae3f-bce0-45a7-8c89-c9611c25e101");
    private static readonly Guid Scheduling=new("93b8b6dc-0698-4d1c-9ee4-0644e900c85d"),ShortScheduling=new("bae08b81-2d5e-4688-ad6a-13243356654b");
    private static readonly Guid[] Modes=[new("961cc777-2547-4f9d-8174-7d86181b8a7a"),Guid.Empty,new("ded574b5-45a0-4f42-8737-46345c09c238")];
    private static readonly object Gate=new();
    private static void Check(uint code){if(code!=0)throw new Win32Exception((int)code);}
    private static Guid Active(){Check(PowerGetActiveScheme(0,out var p));try{return Marshal.PtrToStructure<Guid>(p);}finally{LocalFree(p);}}
    private static (uint Ac,uint Dc) Values(Guid scheme,Guid setting){var sub=Processor;Check(PowerReadACValueIndex(0,ref scheme,ref sub,ref setting,out var ac));Check(PowerReadDCValueIndex(0,ref scheme,ref sub,ref setting,out var dc));return(ac,dc);}
    private static CpuPowerSettings Cpu()
    {
        var scheme=Active();var b=Values(scheme,Boost);var f=Values(scheme,Frequency);var f1=Values(scheme,Frequency1);var s=Values(scheme,Scheduling);var ss=Values(scheme,ShortScheduling);
        return new(scheme.ToString(),b.Ac,b.Dc,f.Ac,f.Dc,f1.Ac,f1.Dc,s.Ac,s.Dc,ss.Ac,ss.Dc);
    }
    internal static bool IsOnAc(){if(!GetSystemPowerStatus(out var power)||power.Ac>1)throw new IOException("Power source unavailable");return power.Ac==1;}
    internal static WindowsPowerState Read()
    {
        lock(Gate)
        {
            try
            {
                Check(PowerGetUserConfiguredACPowerMode(out var ac));Check(PowerGetUserConfiguredDCPowerMode(out var dc));
                if(!GetSystemPowerStatus(out var power)||power.Ac>1)throw new InvalidOperationException("Power source unavailable");
                int a=Array.IndexOf(Modes,ac),d=Array.IndexOf(Modes,dc);if(a<0||d<0)throw new InvalidOperationException("Unsupported Windows power mode");
                return new(true,power.Ac==1,a,d,Cpu(),null);
            }
            catch(Exception e){return new(false,false,-1,-1,null,e.Message);}
        }
    }
    internal static WindowsPowerState SetMode(int mode)
    {
        if(mode<0||mode>=Modes.Length)throw new ArgumentOutOfRangeException(nameof(mode));
        lock(Gate)
        {
            var before=Read();if(!before.Available)throw new InvalidOperationException(before.Error);
            var requested=Modes[mode];var old=Modes[before.OnAc?before.ModeAc:before.ModeDc];
            try
            {
                Check(before.OnAc?PowerSetUserConfiguredACPowerMode(ref requested):PowerSetUserConfiguredDCPowerMode(ref requested));
                var actual=Read();if(!actual.Available||(before.OnAc?actual.ModeAc:actual.ModeDc)!=mode)throw new IOException("Power mode readback mismatch");return actual;
            }
            catch{if(before.OnAc)PowerSetUserConfiguredACPowerMode(ref old);else PowerSetUserConfiguredDCPowerMode(ref old);throw;}
        }
    }
    private static void Write(Guid scheme,Guid setting,uint ac,uint dc)
    {
        var sub=Processor;Check(PowerWriteACValueIndex(0,ref scheme,ref sub,ref setting,ac));Check(PowerWriteDCValueIndex(0,ref scheme,ref sub,ref setting,dc));
    }
    private static void Apply(CpuPowerSettings s,bool activate=true)
    {
        var scheme=Guid.Parse(s.Scheme);if(activate&&Active()!=scheme)throw new InvalidOperationException("The active power plan changed; reopen CPU settings");
        Write(scheme,Boost,s.BoostAc,s.BoostDc);
        Write(scheme,Scheduling,s.SchedulingAc,s.SchedulingDc);Write(scheme,ShortScheduling,s.ShortSchedulingAc,s.ShortSchedulingDc);
        if(activate){if(Active()!=scheme)throw new InvalidOperationException("The active power plan changed while applying CPU settings");Check(PowerSetActiveScheme(0,ref scheme));}
    }
    internal static WindowsPowerState SetCpu(CpuPowerSettings settings)
    {
        if(settings.BoostAc>6||settings.BoostDc>6||settings.SchedulingAc>5||settings.SchedulingDc>5||settings.ShortSchedulingAc>5||settings.ShortSchedulingDc>5)throw new ArgumentException("Invalid CPU policy");
        lock(Gate)
        {
            var before=Cpu();if(before.Scheme!=settings.Scheme)throw new InvalidOperationException("The power plan changed; reopen CPU settings");
            try{Apply(settings);var actual=Cpu();if(actual.BoostAc!=settings.BoostAc||actual.BoostDc!=settings.BoostDc||actual.SchedulingAc!=settings.SchedulingAc||actual.SchedulingDc!=settings.SchedulingDc||actual.ShortSchedulingAc!=settings.ShortSchedulingAc||actual.ShortSchedulingDc!=settings.ShortSchedulingDc)throw new IOException("CPU settings readback mismatch: requested "+System.Text.Json.JsonSerializer.Serialize(settings)+"; actual "+System.Text.Json.JsonSerializer.Serialize(actual));return Read();}
            catch{Apply(before,Active()==Guid.Parse(before.Scheme));throw;}
        }
    }
    [StructLayout(LayoutKind.Sequential)]private struct PowerStatus{internal byte Ac,Battery,Percent,System;internal uint Remaining,Full;}
    [DllImport("kernel32.dll")]private static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("kernel32.dll")]private static extern nint LocalFree(nint p);
    [DllImport("powrprof.dll")]private static extern uint PowerGetActiveScheme(nint key,out nint scheme);
    [DllImport("powrprof.dll")]private static extern uint PowerSetActiveScheme(nint key,ref Guid scheme);
    [DllImport("powrprof.dll")]private static extern uint PowerReadACValueIndex(nint key,ref Guid scheme,ref Guid sub,ref Guid setting,out uint value);
    [DllImport("powrprof.dll")]private static extern uint PowerReadDCValueIndex(nint key,ref Guid scheme,ref Guid sub,ref Guid setting,out uint value);
    [DllImport("powrprof.dll")]private static extern uint PowerWriteACValueIndex(nint key,ref Guid scheme,ref Guid sub,ref Guid setting,uint value);
    [DllImport("powrprof.dll")]private static extern uint PowerWriteDCValueIndex(nint key,ref Guid scheme,ref Guid sub,ref Guid setting,uint value);
    [DllImport("powrprof.dll")]private static extern uint PowerGetUserConfiguredACPowerMode(out Guid mode);
    [DllImport("powrprof.dll")]private static extern uint PowerGetUserConfiguredDCPowerMode(out Guid mode);
    [DllImport("powrprof.dll")]private static extern uint PowerSetUserConfiguredACPowerMode(ref Guid mode);
    [DllImport("powrprof.dll")]private static extern uint PowerSetUserConfiguredDCPowerMode(ref Guid mode);
}
