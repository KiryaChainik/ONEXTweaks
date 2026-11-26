// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;

namespace ONEXTweaks.WidgetBridge;
internal sealed record EnergySaverState(bool Available,bool On,bool OnAc,int ManualOverride,uint RawStatus,string? Error);
internal static class EnergySaverController
{
    // Windows 11 24H2/25H2 SettingsHandlers_OneCore_BatterySaver consumes this
    // exact eight-byte V2 payload and mask, and publishes DWORD 1/2 to override.
    // Do not use SYSTEM_POWER_STATUS.SystemStatusFlag: it misses Standard savings.
    private const ulong StateName=0x41840B3EA3BDF075,OverrideName=0x41C6013DA3BC3075;
    private const uint SavingsMask=0x003FC000;
    private static readonly object Gate=new();
    private static (byte[] Data,uint Stamp) Query(ulong name,uint expected)
    {
        uint size=64;var buffer=new byte[64];int status=NtQueryWnfStateData(ref name,0,0,out uint stamp,buffer,ref size);
        if(status<0)throw new IOException($"Energy saver query failed: 0x{status:X8}");
        if(size!=expected)throw new IOException("Unsupported Windows energy saver payload");
        return(buffer,stamp);
    }
    internal static EnergySaverState Read()
    {
        lock(Gate)
        {
            try
            {
                int build=Environment.OSVersion.Version.Build;
                if(build<26100||build>26200)throw new PlatformNotSupportedException("This Windows energy saver implementation has not been validated for this OS build");
                var state=Query(StateName,8);var selection=Query(OverrideName,4);
                uint raw=BitConverter.ToUInt32(state.Data,0),manual=BitConverter.ToUInt32(selection.Data,0);
                if((raw&0x3FFF)!=1||manual>2)throw new IOException("Unknown Windows energy saver state");
                return new(true,(raw&SavingsMask)!=0,WindowsPowerSettings.IsOnAc(),(int)manual,raw,null);
            }
            catch(Exception error){return new(false,false,false,-1,0,error.Message);}
        }
    }
    internal static EnergySaverState Set(bool on)
    {
        lock(Gate)
        {
            var before=Read();if(!before.Available)throw new IOException(before.Error);
            var selectionBefore=Query(OverrideName,4);
            if(BitConverter.ToUInt32(selectionBefore.Data,0)!=(uint)before.ManualOverride)
                throw new IOException("Windows energy saver changed before the requested action");
            uint requested=on?1u:2u;uint? ownChangeStamp=null;
            bool changedChoice=requested!=(uint)before.ManualOverride;
            Publish(requested);
            try
            {
                var selectionAfter=Query(OverrideName,4);
                if(BitConverter.ToUInt32(selectionAfter.Data,0)!=requested||(changedChoice&&selectionAfter.Stamp!=unchecked(selectionBefore.Stamp+1)))
                    throw new IOException("Windows energy saver write ownership could not be confirmed");
                // An unchanged choice needs no old-choice rollback, even if Windows does not advance its stamp.
                if(changedChoice)ownChangeStamp=selectionAfter.Stamp;
                for(int i=0;i<80;i++)
                {
                    var actual=Read();
                    if(actual.Available&&actual.On==on&&actual.ManualOverride==requested)return actual;
                    if(actual.Available&&actual.ManualOverride!=requested)throw new IOException("Windows energy saver was changed by another application");
                    Thread.Sleep(50);
                }
                throw new IOException("Windows did not confirm the requested energy saver state");
            }
            catch(Exception actionError)
            {
                // Equality of the value alone is insufficient: a later owner may
                // have deliberately published the SAME value with a newer stamp.
                // Unknown acknowledgement never authorizes an old-choice replay.
                try
                {
                    if(ownChangeStamp.HasValue)
                    {
                        var current=Query(OverrideName,4);
                        if(current.Stamp==ownChangeStamp.Value&&BitConverter.ToUInt32(current.Data,0)==requested)
                            Publish((uint)before.ManualOverride);
                    }
                }
                catch(Exception recoveryError)
                {
                    throw new AggregateException("Energy saver action failed and recovery could not be confirmed",actionError,recoveryError);
                }
                throw;
            }
        }
    }
    private static void Publish(uint value)
    {
        int status=RtlPublishWnfStateData(OverrideName,0,ref value,4,0);
        if(status<0)throw new IOException($"Windows rejected energy saver change: 0x{status:X8}");
    }
    [DllImport("ntdll.dll")]private static extern int NtQueryWnfStateData(ref ulong name,nint type,nint scope,out uint stamp,byte[] buffer,ref uint size);
    [DllImport("ntdll.dll")]private static extern int RtlPublishWnfStateData(ulong name,nint type,ref uint buffer,uint size,nint scope);
}
