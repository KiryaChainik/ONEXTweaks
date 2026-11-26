// SPDX-License-Identifier: AGPL-3.0-only
// Native signatures and property names from RTSS SDK's RTSSHooksInterface/Overlay.
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ONEXTweaks.WidgetBridge;

internal static class RtssProfile
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Ansi)] private delegate void Profile([MarshalAs(UnmanagedType.LPStr)]string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Ansi)] [return:MarshalAs(UnmanagedType.Bool)] private delegate bool Property([MarshalAs(UnmanagedType.LPStr)]string name,[In,Out]byte[] data,uint size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Update();
    private static readonly object gate=new();
    private static IntPtr library;
    private static Profile? load,save;
    private static Property? get,set;
    private static Update? update;
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool WritePrivateProfileString(string section,string key,string value,string path);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern uint GetPrivateProfileInt(string section,string key,int fallback,string path);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern uint GetPrivateProfileString(string section,string key,string fallback,StringBuilder value,uint size,string path);
    private static void Initialize()
    {
        if(library!=IntPtr.Zero)return;
        string directory=RtssOverlay.DirectoryPath??throw new FileNotFoundException("RTSS is not installed");
        library=NativeLibrary.Load(Path.Combine(directory,"RTSSHooks64.dll"));
        T Export<T>(string name)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));
        load=Export<Profile>("LoadProfile");save=Export<Profile>("SaveProfile");get=Export<Property>("GetProfileProperty");set=Export<Property>("SetProfileProperty");update=Export<Update>("UpdateProfiles");
    }
    internal static byte[] Read(string name,int size=4,string profile="")
    {
        lock(gate){Initialize();load!(profile);var data=new byte[size];if(!get!(name,data,(uint)size))throw new IOException("RTSS property is not supported: "+name);return data;}
    }
    internal static void Apply(IReadOnlyDictionary<string,byte[]> values,string profile="")
    {
        lock(gate)
        {
            Initialize();load!(profile);var before=new Dictionary<string,byte[]>();
            foreach(var pair in values){var old=new byte[pair.Value.Length];if(!get!(pair.Key,old,(uint)old.Length))throw new IOException("RTSS property is not supported: "+pair.Key);before[pair.Key]=old;}
            string backup=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ONEXTweaks","dev","references","rtss-original-properties.json");
            if(!File.Exists(backup))File.WriteAllText(backup,JsonSerializer.Serialize(before.ToDictionary(x=>x.Key,x=>Convert.ToBase64String(x.Value))));
            try
            {
                foreach(var pair in values)
                {
                    if(!set!(pair.Key,pair.Value,PropertySize(pair.Key,pair.Value)))throw new IOException("RTSS rejected "+pair.Key);
                    var actual=new byte[pair.Value.Length];if(!get!(pair.Key,actual,(uint)actual.Length)||!actual.SequenceEqual(pair.Value))throw new IOException("RTSS readback failed for "+pair.Key);
                }
                save!(profile);update!();
            }
            catch{load!(profile);foreach(var pair in before)set!(pair.Key,pair.Value,PropertySize(pair.Key,pair.Value));save!(profile);update!();throw;}
        }
    }
    private static uint PropertySize(string name,byte[] value)=>name=="FontFace"?(uint)Math.Max(1,Array.IndexOf(value,(byte)0)):(uint)value.Length;
    internal static void EnableOsd()=>Apply(new Dictionary<string,byte[]>{{"EnableOSD",BitConverter.GetBytes(1u)}});
    internal static (uint Enabled,uint Color) ReadBackground()
    {
        string file=Path.Combine(RtssOverlay.DirectoryPath??throw new FileNotFoundException("RTSS is missing"),"Profiles","Global");
        var color=new StringBuilder(32);GetPrivateProfileString("OSD","FillColor","80000000",color,32,file);
        return(GetPrivateProfileInt("OSD","EnableFill",0,file),Convert.ToUInt32(color.ToString(),16));
    }
    internal static void SetBackground(uint enabled,uint color)
    {
        lock(gate)
        {
            Initialize();string file=Path.Combine(RtssOverlay.DirectoryPath!,"Profiles","Global");
            // RTSS 7.3.5 keeps fill in the INI, but does not export it via GetProfileProperty.
            if(!WritePrivateProfileString("OSD","EnableFill",enabled.ToString(),file)||!WritePrivateProfileString("OSD","FillColor",color.ToString("X8"),file))throw new IOException("Cannot save RTSS background",new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
            if(ReadBackground()!=(enabled,color))throw new IOException("RTSS background readback failed");
            load!("");update!();
        }
    }
    internal static void Configure(IReadOnlyDictionary<string,string> config)
    {
        if(RtssOverlay.DirectoryPath==null)return;
        var changes=new Dictionary<string,byte[]>();
        if(config.TryGetValue("Font",out string? font))
        {
            string[] parts=font.Split('|');if(parts[0].Length is <1 or >80||parts[0].Any(c=>c<32||c>126))throw new InvalidDataException("Invalid RTSS font");
            var name=new byte[260];Encoding.ASCII.GetBytes(parts[0]).CopyTo(name,0);changes["FontFace"]=name;
            int weight=parts.Length>1&&int.TryParse(parts[1],out int parsed)?Math.Clamp(parsed,100,900):400;
            changes["FontWeight"]=BitConverter.GetBytes(weight);changes["FontHeight"]=BitConverter.GetBytes(-9);
        }
        var before=new Dictionary<string,byte[]>();foreach(var pair in changes)before[pair.Key]=Read(pair.Key,pair.Value.Length);
        // Metric/color edits should not rebuild RTSS's unchanged font and backdrop every drag tick.
        foreach(string key in changes.Keys.Where(key=>changes[key].SequenceEqual(before[key])).ToArray())changes.Remove(key);
        var backgroundBefore=ReadBackground();
        try
        {
        if(changes.Count>0)Apply(changes);
        if(config.TryGetValue("Background",out string? background))
        {
            string[] parts=background.Split('|');uint alpha=parts.Length>1&&uint.TryParse(parts[1],out uint parsed)?Math.Clamp(parsed,10,100)*255/100:153;
            uint enabled=parts[0]=="1"?1u:0u,color=alpha<<24;
            if(backgroundBefore!=(enabled,color))SetBackground(enabled,color);
        }
        }
        catch{if(before.Count>0)Apply(before);SetBackground(backgroundBefore.Enabled,backgroundBefore.Color);throw;}
    }
}
