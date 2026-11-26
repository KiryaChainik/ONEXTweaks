// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
namespace ONEXTweaks.WidgetBridge;
internal sealed record AudioDeviceEntry(string Token,string Name,bool Active,bool DefaultConsole,bool DefaultMultimedia,bool DefaultCommunications,int FormFactor,double? Volume,bool? Muted);
internal sealed record AudioDevicesState(bool Available,AudioDeviceEntry[] Devices,string? Error);
internal sealed class AudioDevicesController
{
    private readonly object gate=new();
    private readonly int flow;
    internal AudioDevicesController(int flow=0){if(flow is not (0 or 1))throw new ArgumentOutOfRangeException(nameof(flow));this.flow=flow;}
    private readonly Dictionary<string,string> tokens=new(StringComparer.Ordinal);
    internal AudioDevicesState Read()
    {
        lock(gate){try{using var session=new ComSession();return ReadCore();}catch(Exception e){return new(false,[],e.Message);}}
    }
    internal AudioDevicesState SetDefault(string token)
    {
        lock(gate)
        {
            using var session=new ComSession();var before=ReadCore();if(!before.Available)throw new IOException(before.Error);
            var candidate=before.Devices.SingleOrDefault(d=>d.Token==token);if(candidate is not {Active:true})throw new IOException("Устройство больше не доступно. Обнови список.");
            string id=tokens.Single(p=>p.Value==token).Key;object? enumeration=null,policy=null;
            try
            {
                enumeration=Create(new("BCDE0395-E52F-467C-8E3D-C4579291692E"));var enumerator=(IMMDeviceEnumerator)enumeration;
                string?[] previous=Enumerable.Range(0,flow==0?2:3).Select(role=>DefaultId(enumerator,role)).ToArray();
                if(previous.All(p=>p==id))return before;
                Check(enumerator.GetDevice(id,out var target));try{Check(target.GetState(out uint state));if(state!=1)throw new IOException("Устройство отключено");}finally{Release(target);}
                policy=Create(new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"));var setter=(IPolicyConfig)policy;
                var changed=new List<int>();
                try
                {
                    // Output: general system/game roles, preserve separate communications output.
                    // Input: selected system microphone is also used for communications.
                    for(int role=0;role<previous.Length;role++){if(previous[role]==id)continue;Check(setter.SetDefaultEndpoint(id,role));changed.Add(role);}
                    for(int n=0;n<40;n++){if(Enumerable.Range(0,previous.Length).All(role=>DefaultId(enumerator,role)==id))return ReadCore();Thread.Sleep(50);}
                    throw new IOException("Windows не подтвердил выбор устройства");
                }
                catch
                {
                    foreach(int role in changed.AsEnumerable().Reverse())
                        try{if(previous[role] is string original&&DefaultId(enumerator,role)==id)Check(setter.SetDefaultEndpoint(original,role));}catch{}
                    throw;
                }
            }
            finally{Release(policy);Release(enumeration);}
        }
    }
    internal object VerifySameDefaults()
    {
        lock(gate)
        {
            using var session=new ComSession();object? enumeration=null,policy=null;
            try
            {
                enumeration=Create(new("BCDE0395-E52F-467C-8E3D-C4579291692E"));var enumerator=(IMMDeviceEnumerator)enumeration;
                var before=new[]{DefaultId(enumerator,0),DefaultId(enumerator,1),DefaultId(enumerator,2)};if(before[0]==null||before[1]==null)throw new IOException("No original audio output");
                var levels=ReadCore();policy=Create(new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"));var setter=(IPolicyConfig)policy;
                for(int role=0;role<(flow==0?2:3);role++)Check(setter.SetDefaultEndpoint(before[role]!,role));
                if(Enumerable.Range(0,3).Any(r=>DefaultId(enumerator,r)!=before[r]))throw new IOException("Same-default API probe changed a selected endpoint");
                var after=ReadCore();foreach(var device in levels.Devices.Where(d=>d.Active)){var actual=after.Devices.Single(d=>d.Token==device.Token);if(actual.Muted!=device.Muted||actual.Volume!=device.Volume)throw new IOException("Same-default probe changed volume/mute");}
                return new{passed=true,actualPolicySetterCalls=flow==0?2:3,defaultRolesPreserved=true,communicationsRolePreserved=true,perDeviceVolumeMutePreserved=true,crossEndpointSwitchTested=false};
            }
            finally{Release(policy);Release(enumeration);}
        }
    }
    private AudioDevicesState ReadCore()
    {
        object? enumeration=null,collectionObject=null;
        try
        {
            enumeration=Create(new("BCDE0395-E52F-467C-8E3D-C4579291692E"));var enumerator=(IMMDeviceEnumerator)enumeration;
            string? console=DefaultId(enumerator,0),multimedia=DefaultId(enumerator,1),communications=DefaultId(enumerator,2);
            Check(enumerator.EnumAudioEndpoints(flow,9,out var collection));collectionObject=collection;Check(collection.GetCount(out uint count));var result=new List<AudioDeviceEntry>();
            for(uint i=0;i<count;i++)
            {
                IMMDevice? device=null;IPropertyStore? properties=null;
                try
                {
                    Check(collection.Item(i,out device));Check(device.GetId(out string id));Check(device.GetState(out uint state));Check(device.OpenPropertyStore(0,out properties));
                    if(!tokens.TryGetValue(id,out var token))tokens[id]=token=Guid.NewGuid().ToString("N");
                    string name=PropertyString(properties,new(new("A45C254E-DF1C-4EFD-8020-67D146A850E0"),14))??"Аудиоустройство";
                    int form=PropertyInt(properties,new(new("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"),0));
                    double? volume=null;bool? muted=null;if(state==1){try{(volume,muted)=ReadLevels(device);}catch{}}
                    result.Add(new(token,name,state==1,id==console,id==multimedia,id==communications,form,volume,muted));
                }
                finally{Release(properties);Release(device);}
            }
            return new(true,result.OrderByDescending(d=>d.DefaultMultimedia).ThenByDescending(d=>d.Active).ThenBy(d=>d.Name,StringComparer.CurrentCultureIgnoreCase).ToArray(),null);
        }
        finally{Release(collectionObject);Release(enumeration);}
    }
    private string? DefaultId(IMMDeviceEnumerator enumerator,int role)
    {
        int code=enumerator.GetDefaultAudioEndpoint(flow,role,out var device);if(code==unchecked((int)0x80070490))return null;Check(code);
        try{Check(device.GetId(out var id));return id;}finally{Release(device);}
    }
    private static (double,bool) ReadLevels(IMMDevice device)
    {
        var iid=new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");Check(device.Activate(ref iid,23,0,out var raw));
        try{var volume=(IAudioEndpointVolume)raw;Check(volume.GetMasterVolumeLevelScalar(out float level));Check(volume.GetMute(out bool muted));return(level*100.0,muted);}finally{Release(raw);}
    }
    private static string? PropertyString(IPropertyStore properties,PropertyKey key)
    {Check(properties.GetValue(ref key,out var v));try{return v.Type==31?Marshal.PtrToStringUni(v.Pointer):null;}finally{PropVariantClear(ref v);}}
    private static int PropertyInt(IPropertyStore properties,PropertyKey key)
    {Check(properties.GetValue(ref key,out var v));try{return v.Type==19?(int)v.Number:-1;}finally{PropVariantClear(ref v);}}
    private static object Create(Guid clsid)=>Activator.CreateInstance(Type.GetTypeFromCLSID(clsid,true)!)!;
    private static void Release(object? value){if(value is not null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
    private static void Check(int code)=>Marshal.ThrowExceptionForHR(code);
    private sealed class ComSession:IDisposable
    {private readonly bool release;internal ComSession(){int code=CoInitializeEx(0,0);if(code<0&&code!=unchecked((int)0x80010106))Check(code);release=code>=0;}public void Dispose(){if(release)CoUninitialize();}}
    [StructLayout(LayoutKind.Sequential)]private struct PropertyKey{internal Guid Format;internal uint Id;internal PropertyKey(Guid format,uint id){Format=format;Id=id;}}
    [StructLayout(LayoutKind.Explicit,Size=24)]private struct PropVariant{[FieldOffset(0)]internal ushort Type;[FieldOffset(8)]internal nint Pointer;[FieldOffset(8)]internal uint Number;}
    [DllImport("ole32.dll")]private static extern int PropVariantClear(ref PropVariant variant);
    [DllImport("ole32.dll")]private static extern int CoInitializeEx(nint reserved,uint mode);
    [DllImport("ole32.dll")]private static extern void CoUninitialize();
    [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IMMDeviceEnumerator
    {[PreserveSig]int EnumAudioEndpoints(int flow,uint mask,out IMMDeviceCollection devices);[PreserveSig]int GetDefaultAudioEndpoint(int flow,int role,out IMMDevice device);[PreserveSig]int GetDevice([MarshalAs(UnmanagedType.LPWStr)]string id,out IMMDevice device);}
    [ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IMMDeviceCollection
    {[PreserveSig]int GetCount(out uint count);[PreserveSig]int Item(uint index,out IMMDevice device);}
    [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IMMDevice
    {[PreserveSig]int Activate(ref Guid iid,uint context,nint args,[MarshalAs(UnmanagedType.IUnknown)]out object result);[PreserveSig]int OpenPropertyStore(uint mode,out IPropertyStore store);[PreserveSig]int GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);[PreserveSig]int GetState(out uint state);}
    [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IPropertyStore
    {[PreserveSig]int GetCount(out uint count);[PreserveSig]int GetAt(uint index,out PropertyKey key);[PreserveSig]int GetValue(ref PropertyKey key,out PropVariant value);}
    // Internal Windows policy ABI, also used by EarTrumpet (MIT):
    // github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/Interop/MMDeviceAPI/IPolicyConfig.cs
    [ComImport,Guid("F8679F50-850A-41CF-9C72-430F290290C8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IPolicyConfig
    {void Unused1();void Unused2();void Unused3();void Unused4();void Unused5();void Unused6();void Unused7();void Unused8();void Unused9();void Unused10();[PreserveSig]int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)]string id,int role);}
    [ComImport,Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IAudioEndpointVolume
    {[PreserveSig]int RegisterControlChangeNotify(nint notify);[PreserveSig]int UnregisterControlChangeNotify(nint notify);[PreserveSig]int GetChannelCount(out uint channels);[PreserveSig]int SetMasterVolumeLevel(float value,ref Guid context);[PreserveSig]int SetMasterVolumeLevelScalar(float value,ref Guid context);[PreserveSig]int GetMasterVolumeLevel(out float value);[PreserveSig]int GetMasterVolumeLevelScalar(out float value);[PreserveSig]int SetChannelVolumeLevel(uint channel,float value,ref Guid context);[PreserveSig]int SetChannelVolumeLevelScalar(uint channel,float value,ref Guid context);[PreserveSig]int GetChannelVolumeLevel(uint channel,out float value);[PreserveSig]int GetChannelVolumeLevelScalar(uint channel,out float value);[PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool muted,ref Guid context);[PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool muted);}
}
