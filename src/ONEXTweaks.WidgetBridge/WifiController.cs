// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
namespace ONEXTweaks.WidgetBridge;
internal sealed record WifiState(bool Available,bool On,bool Connected,string? NetworkName,string? NetworkNameSource,string? Error,string? NameError,int AdapterCount);
internal sealed partial class WifiController
{
    private readonly object gate=new();
    private readonly AirplaneController airplane;
    internal WifiController(AirplaneController? airplane=null){this.airplane=airplane??new AirplaneController();}
    private sealed record Phy(uint Index,uint Software,uint Hardware);
    private sealed record Adapter(Guid Id,int State,Phy[] Phys);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct InterfaceInfo
    {public Guid Id;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Description;public int State;}
    [StructLayout(LayoutKind.Sequential)] private struct RadioState{public uint Index,Software,Hardware;}
    internal WifiState Read()
    {
        lock(gate)
        {
            nint client=0;
            try
            {
                Check(WlanOpenHandle(2,0,out _,out client),"Open Wi-Fi service");CompletePendingProfile(client);var adapters=Adapters(client);
                bool on=adapters.Any(a=>a.Phys.Any(p=>p.Software==1&&p.Hardware!=2)),connected=adapters.Any(a=>(a.State is 1 or 2)&&a.Phys.Any(p=>p.Software==1&&p.Hardware!=2));
                string? name=null,source=null,nameError=null;
                foreach(var adapter in adapters.Where(a=>a.State is 1 or 2))
                {
                    var named=ConnectionName(client,adapter.Id);nameError=named.Error;
                    if(!string.IsNullOrWhiteSpace(named.Ssid)){name=Clean(named.Ssid);source="Connected Wi-Fi SSID";break;}
                    try{var friendly=NetworkName(adapter.Id);if(!string.IsNullOrWhiteSpace(friendly)){name=Clean(friendly);source="Windows connected network profile";break;}}
                    catch(Exception e){nameError=e.Message;}
                }
                return new(adapters.Length>0,on,connected,name,source,adapters.Length==0?"No wireless adapter":null,nameError,adapters.Length);
            }
            catch(Exception e){return new(false,false,false,null,null,e.Message,null,0);}
            finally{if(client!=0)WlanCloseHandle(client,0);}
        }
    }
    internal WifiState Set(bool on)
    {
        bool clearedAirplane=false;
        if(on){var mode=airplane.Read();if(mode.Available&&mode.On){airplane.Set(false);clearedAirplane=true;}}
        lock(gate)
        {
            nint client=0;
            try
            {
                Check(WlanOpenHandle(2,0,out _,out client),"Open Wi-Fi service");if(!on)pendingProfile=null;var before=Adapters(client);
                if(before.Length==0)throw new IOException("No wireless adapter");
                if(on&&clearedAirplane)for(int attempt=0;attempt<40&&before.All(a=>a.Phys.All(p=>p.Hardware==2));attempt++){Thread.Sleep(50);before=Adapters(client);}
                if(on&&before.All(a=>a.Phys.All(p=>p.Hardware==2)))throw new IOException("Wi-Fi is disabled by a hardware switch or system policy");
                var changed=new List<(Guid Id,Phy State)>();
                try
                {
                    foreach(var adapter in before)foreach(var phy in adapter.Phys)
                    {
                        if(phy.Software==(on?1u:2u))continue;
                        SetPhy(client,adapter.Id,phy.Index,on?1u:2u);changed.Add((adapter.Id,phy));
                    }
                    bool confirmed=false;
                    for(int n=0;n<30;n++){var actual=Adapters(client);if(actual.Length>0&&actual.All(a=>a.Phys.Length>0&&a.Phys.All(p=>p.Software==(on?1u:2u)))){confirmed=true;break;}Thread.Sleep(50);}
                    if(!confirmed)throw new IOException("Windows did not confirm the requested Wi-Fi radio state");
                }
                catch
                {
                    foreach(var item in changed)try{SetPhy(client,item.Id,item.State.Index,item.State.Software);}catch{}
                    throw;
                }
            }
            finally{if(client!=0)WlanCloseHandle(client,0);}
            return Read();
        }
    }
    private static void SetPhy(nint client,Guid id,uint index,uint software)
    {
        var state=new RadioState{Index=index,Software=software,Hardware=0};
        Check(WlanSetInterface(client,ref id,4,12,ref state,0),"Change Wi-Fi radio");
    }
    private static Adapter[] Adapters(nint client)
    {
        nint list=0;
        try
        {
            Check(WlanEnumInterfaces(client,0,out list),"Enumerate Wi-Fi adapters");int count=Marshal.ReadInt32(list);if(count is <0 or >64)throw new IOException("Invalid Wi-Fi adapter count");
            var result=new List<Adapter>();int stride=Marshal.SizeOf<InterfaceInfo>();
            for(int i=0;i<count;i++)
            {
                var info=Marshal.PtrToStructure<InterfaceInfo>(list+8+i*stride);nint data=0;
                try
                {
                    var id=info.Id;Check(WlanQueryInterface(client,ref id,4,0,out uint size,out data,out _),"Read Wi-Fi radio");
                    if(size<4)throw new IOException("Invalid Wi-Fi radio data");int phys=Marshal.ReadInt32(data);if(phys is <1 or >64||size<4+phys*12)throw new IOException("Invalid Wi-Fi PHY count");
                    var radios=new Phy[phys];for(int p=0;p<phys;p++){int at=4+p*12;radios[p]=new((uint)Marshal.ReadInt32(data,at),(uint)Marshal.ReadInt32(data,at+4),(uint)Marshal.ReadInt32(data,at+8));}
                    result.Add(new(info.Id,info.State,radios));
                }
                finally{if(data!=0)WlanFreeMemory(data);}
            }
            return result.ToArray();
        }
        finally{if(list!=0)WlanFreeMemory(list);}
    }
    private static (string? Ssid,string? Profile,string? Error) ConnectionName(nint client,Guid id)
    {
        nint data=0;
        try
        {
            uint result=WlanQueryInterface(client,ref id,7,0,out uint size,out data,out _);
            if(result!=0)return(null,null,"Current Wi-Fi name: "+new Win32Exception((int)result).Message);
            if(size<556)return(null,null,"Invalid Wi-Fi connection data");
            int length=Marshal.ReadInt32(data,520);if(length is <0 or >32)return(null,null,"Invalid Wi-Fi SSID length");
            var bytes=new byte[length];Marshal.Copy(data+524,bytes,0,length);
            return(Encoding.UTF8.GetString(bytes),Marshal.PtrToStringUni(data+8,256)?.TrimEnd('\0'),null);
        }
        finally{if(data!=0)WlanFreeMemory(data);}
    }
    private static string? NetworkName(Guid adapter)
    {
        object? manager=null,networks=null;
        try
        {
            manager=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"),true)!);networks=((dynamic)manager!).GetNetworks(1);
            foreach(object network in (IEnumerable)networks)
            {
                object? connections=null;
                try
                {
                    connections=((dynamic)network).GetNetworkConnections();
                    foreach(object connection in (IEnumerable)connections)
                    try{if((Guid)((dynamic)connection).GetAdapterId()==adapter)return (string)((dynamic)network).GetName();}
                    finally{Release(connection);}
                }
                finally{Release(connections);Release(network);}
            }
            return null;
        }
        finally{Release(networks);Release(manager);}
    }
    private static string Clean(string value)=>new(value.Select(c=>char.IsControl(c)?' ':c).Take(128).ToArray());
    private static void Release(object? value){if(value!=null&&Marshal.IsComObject(value))Marshal.FinalReleaseComObject(value);}
    private static void Check(uint code,string operation){if(code!=0)throw new IOException(operation+": "+new Win32Exception((int)code).Message);}
    [DllImport("wlanapi.dll")]private static extern uint WlanOpenHandle(uint version,nint reserved,out uint negotiated,out nint client);
    [DllImport("wlanapi.dll")]private static extern uint WlanCloseHandle(nint client,nint reserved);
    [DllImport("wlanapi.dll")]private static extern uint WlanEnumInterfaces(nint client,nint reserved,out nint list);
    [DllImport("wlanapi.dll")]private static extern uint WlanQueryInterface(nint client,ref Guid id,int opcode,nint reserved,out uint size,out nint data,out int type);
    [DllImport("wlanapi.dll")]private static extern uint WlanSetInterface(nint client,ref Guid id,int opcode,uint size,ref RadioState data,nint reserved);
    [DllImport("wlanapi.dll")]private static extern void WlanFreeMemory(nint data);
}
