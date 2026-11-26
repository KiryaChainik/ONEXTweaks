// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
namespace ONEXTweaks.WidgetBridge;
internal sealed record AudioMixerApp(string Token,string Name,uint Pid,double Volume,bool Muted,int SessionCount,bool Active);
internal sealed record AudioMixerApps(bool Available,AudioMixerApp[] Apps,string? Error=null);
internal sealed class AudioMixerController
{
    private readonly object gate=new();
    private static readonly Guid context=new("48ED0874-7153-412A-97E3-1CB1A86A7CF1");
    private sealed record Session(string Token,string Name,uint Pid,double Volume,bool Muted,bool Active);
    internal AudioMixerApps Read()
    {
        lock(gate)try
        {
            var rows=Scan(null).GroupBy(s=>s.Token).Select(g=>new AudioMixerApp(g.Key,g.First().Name,g.First().Pid,g.Average(s=>s.Volume),g.All(s=>s.Muted),g.Count(),g.Any(s=>s.Active))).OrderByDescending(a=>a.Active).ThenBy(a=>a.Pid==0).ThenBy(a=>a.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
            return new(true,rows);
        }
        catch(Exception e){return new(false,[],e.Message);}
    }
    internal AudioMixerApps Set(string token,double? volume,bool toggleMute)
    {
        if(token.Length!=64||!token.All(Uri.IsHexDigit))throw new ArgumentException("Invalid mixer app token");
        if(volume.HasValue&&(!double.IsFinite(volume.Value)||volume.Value is <0 or >100)||volume.HasValue==toggleMute)throw new ArgumentException("Choose a valid volume or mute action");
        lock(gate)
        {
            var before=Read();if(!before.Available)throw new IOException(before.Error);var row=before.Apps.SingleOrDefault(a=>a.Token==token)??throw new IOException("The app audio session ended or the output device changed. Refresh the mixer.");
            int changed=0;bool muted=!row.Muted;
            Scan((session,simple)=>
            {
                if(session.Token!=token)return;var source=context;
                if(volume.HasValue)Check(simple.SetMasterVolume((float)(volume.Value/100),ref source));else Check(simple.SetMute(muted,ref source));changed++;
            });
            if(changed==0)throw new IOException("The app audio session ended. Refresh the mixer.");
            var actual=Read();var checkedRow=actual.Apps.SingleOrDefault(a=>a.Token==token)??throw new IOException("The app audio session ended before confirmation.");
            if(volume.HasValue&&Math.Abs(checkedRow.Volume-volume.Value)>.02||toggleMute&&checkedRow.Muted!=muted)throw new IOException("Windows did not confirm the app volume change");return actual;
        }
    }
    private static List<Session> Scan(Action<Session,ISimpleAudioVolume>? action)
    {
        object? enumerator=null,device=null,manager=null,sessions=null;
        try
        {
            enumerator=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"),true)!);
            Check(((IMMDeviceEnumerator)enumerator!).GetDefaultAudioEndpoint(0,1,out var endpoint));device=endpoint;Check(endpoint.GetId(out string endpointId));
            var iid=new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");Check(endpoint.Activate(ref iid,23,0,out manager));
            Check(((IAudioSessionManager2)manager).GetSessionEnumerator(out var list));sessions=list;Check(list.GetCount(out int count));if(count is <0 or >2048)throw new IOException("Invalid audio session count");
            var result=new List<Session>();
            for(int i=0;i<count;i++)
            {
                object? raw=null;
                try
                {
                    Check(list.GetSession(i,out raw));var control=(IAudioSessionControl2)raw;Check(control.GetState(out int state));if(state==2)continue;
                    Check(control.GetProcessId(out uint pid));Check(control.GetSessionInstanceIdentifier(out string instance));bool system=control.IsSystemSoundsSession()==0;
                    var meta=Identity(pid,instance,system);string token=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpointId+"\0"+meta.Key)));
                    var simple=(ISimpleAudioVolume)raw;Check(simple.GetMasterVolume(out float level));Check(simple.GetMute(out bool mute));
                    var row=new Session(token,meta.Name,pid,level*100.0,mute,state==1);result.Add(row);action?.Invoke(row,simple);
                }
                // Ended/protected sessions can disappear while enumerating. Writes must fail
                // visibly; a read may omit a session Windows no longer exposes.
                catch(COMException)when(action==null){}
                finally{Release(raw);} // Control and volume are aliases of this one RCW.
            }
            return result;
        }
        finally{Release(sessions);Release(manager);Release(device);Release(enumerator);}
    }
    private static (string Key,string Name) Identity(uint pid,string instance,bool system)
    {
        if(system)return("system-sounds","Системные звуки");
        try
        {
            using var p=Process.GetProcessById((int)pid);string name=p.ProcessName;
            try{var version=p.MainModule?.FileVersionInfo;var product=version?.ProductName;if(!string.IsNullOrWhiteSpace(product))name=product.IndexOf("Windows",StringComparison.OrdinalIgnoreCase)>=0?version?.FileDescription??p.ProcessName:product;}catch(System.ComponentModel.Win32Exception){}
            return(pid+"/"+p.StartTime.ToUniversalTime().Ticks,new string(name.Where(c=>!char.IsControl(c)).Take(80).ToArray()));
        }
        catch(Exception e)when(e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){return("instance/"+instance,"Audio app");}
    }
    private static void Release(object? value){if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
    private static void Check(int result)=>Marshal.ThrowExceptionForHR(result);
    [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IMMDeviceEnumerator
    {[PreserveSig]int EnumAudioEndpoints(int flow,uint mask,out nint devices);[PreserveSig]int GetDefaultAudioEndpoint(int flow,int role,out IMMDevice device);}
    [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IMMDevice
    {[PreserveSig]int Activate(ref Guid iid,uint context,nint parameters,[MarshalAs(UnmanagedType.IUnknown)]out object value);[PreserveSig]int OpenPropertyStore(uint access,out nint store);[PreserveSig]int GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);}
    [ComImport,Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IAudioSessionManager2
    {[PreserveSig]int GetAudioSessionControl(ref Guid id,uint flags,out nint control);[PreserveSig]int GetSimpleAudioVolume(ref Guid id,uint flags,out nint volume);[PreserveSig]int GetSessionEnumerator(out IAudioSessionEnumerator list);}
    [ComImport,Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IAudioSessionEnumerator
    {[PreserveSig]int GetCount(out int count);[PreserveSig]int GetSession(int index,[MarshalAs(UnmanagedType.IUnknown)]out object session);}
    [ComImport,Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IAudioSessionControl2
    {
        [PreserveSig]int GetState(out int state);[PreserveSig]int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)]out string name);[PreserveSig]int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)]string name,ref Guid context);
        [PreserveSig]int GetIconPath([MarshalAs(UnmanagedType.LPWStr)]out string path);[PreserveSig]int SetIconPath([MarshalAs(UnmanagedType.LPWStr)]string path,ref Guid context);
        [PreserveSig]int GetGroupingParam(out Guid group);[PreserveSig]int SetGroupingParam(ref Guid group,ref Guid context);[PreserveSig]int RegisterAudioSessionNotification(nint events);[PreserveSig]int UnregisterAudioSessionNotification(nint events);
        [PreserveSig]int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)]out string id);[PreserveSig]int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)]out string id);[PreserveSig]int GetProcessId(out uint pid);[PreserveSig]int IsSystemSoundsSession();[PreserveSig]int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)]bool optOut);
    }
    [ComImport,Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface ISimpleAudioVolume
    {[PreserveSig]int SetMasterVolume(float volume,ref Guid context);[PreserveSig]int GetMasterVolume(out float volume);[PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool muted,ref Guid context);[PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool muted);}
}
