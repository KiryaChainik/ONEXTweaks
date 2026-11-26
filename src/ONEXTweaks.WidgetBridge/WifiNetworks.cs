// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
namespace ONEXTweaks.WidgetBridge;
internal sealed record WifiNetwork(string Token,string Name,bool Connected,bool Known,bool Secure,int Signal,bool CanConnect,bool PasswordRequired,string Security);
internal sealed record WifiNetworks(WifiState Radio,WifiNetwork[] Networks,string? Error);
internal sealed partial class WifiController
{
    private sealed record Network(Guid Adapter,string Token,string Name,string SsidHex,string Profile,int Bss,int Auth,int Cipher,bool Connected,bool Known,bool Secure,int Signal,bool Connectable)
    {
        internal bool Personal=>Auth is 4 or 7 or 9;
        internal bool NewSupported=>Bss==1&&((Auth==1&&Cipher==0)||((Auth is 4 or 7)&&Cipher is 2 or 4)||(Auth==9&&Cipher==4)||(Auth==10&&Cipher==4));
        internal WifiNetwork View=>new(Token,Name,Connected,Known,Secure,Signal,Connectable&&(Known||NewSupported),!Known&&Personal,Auth switch{1=>Secure?"WEP":"Открытая",4=>"WPA",7=>"WPA2",9=>"WPA3",10=>"OWE",_=>"802.1X / профиль Windows"});
    }
    private sealed record PendingProfile(Guid Adapter,string Ssid,string Xml,DateTime Expires);
    private PendingProfile? pendingProfile;
    private string? connectionWarning;
    internal WifiNetworks ReadNetworks(bool scan=false)
    {
        lock(gate)
        {
            var radio=Read();if(!radio.Available||!radio.On)return new(radio,[],radio.Error);nint client=0;
            try
            {
                Check(WlanOpenHandle(2,0,out _,out client),"Open Wi-Fi service");CompletePendingProfile(client);
                if(scan)foreach(var adapter in Adapters(client)){var id=adapter.Id;uint scanning=WlanScan(client,ref id,0,0,0);if(scanning!=170)Check(scanning,"Scan Wi-Fi networks");}
                return new(Read(),Networks(client).Select(n=>n.View).ToArray(),connectionWarning);
            }
            catch(Exception e){return new(radio,[],e.Message);}
            finally{if(client!=0)WlanCloseHandle(client,0);}
        }
    }
    internal WifiNetworks ConnectNetwork(string token,string? password,bool remember)
    {
        if(token.Length!=64||!token.All(Uri.IsHexDigit))throw new ArgumentException("Invalid Wi-Fi network selection");
        lock(gate)
        {
            if(!Read().On)throw new IOException("Сначала включи Wi-Fi");nint client=0;
            try
            {
                Check(WlanOpenHandle(2,0,out _,out client),"Open Wi-Fi service");var network=Networks(client).SingleOrDefault(n=>n.Token==token)??throw new IOException("Сеть больше не видна. Обнови список.");
                if(network.Connected)return ReadNetworks();if(!network.View.CanConnect)throw new IOException("Для этой сети нужен заранее настроенный профиль Windows");
                pendingProfile=null;connectionWarning=null;var id=network.Adapter;var parameters=new ConnectionParameters{Mode=0,Profile=network.Profile,Bss=network.Bss};
                if(!network.Known)
                {
                    string xml=BuildProfile(network,password,remember);parameters.Mode=1;parameters.Profile=xml;
                    if(remember)pendingProfile=new(network.Adapter,Encoding.UTF8.GetString(Convert.FromHexString(network.SsidHex)),xml,DateTime.UtcNow.AddSeconds(35));
                }
                try{Check(WlanConnectNetwork(client,ref id,ref parameters,0),"Connect Wi-Fi network");}catch{pendingProfile=null;throw;}
                return ReadNetworks();
            }
            finally{if(client!=0)WlanCloseHandle(client,0);}
        }
    }
    internal WifiNetworks DisconnectNetwork(string token)
    {
        if(token.Length!=64||!token.All(Uri.IsHexDigit))throw new ArgumentException("Invalid Wi-Fi network selection");
        lock(gate)
        {
            nint client=0;
            try
            {
                Check(WlanOpenHandle(2,0,out _,out client),"Open Wi-Fi service");var network=Networks(client).SingleOrDefault(n=>n.Token==token&&n.Connected)??throw new IOException("Выбранная сеть уже отключена");
                pendingProfile=null;var id=network.Adapter;Check(WlanDisconnect(client,ref id,0),"Disconnect Wi-Fi network");return ReadNetworks();
            }
            finally{if(client!=0)WlanCloseHandle(client,0);}
        }
    }
    private void CompletePendingProfile(nint client)
    {
        var pending=pendingProfile;if(pending==null)return;
        if(DateTime.UtcNow>pending.Expires){pendingProfile=null;return;}
        var current=ConnectionName(client,pending.Adapter);if(current.Ssid!=pending.Ssid)return;
        // Store only after successful association; failed passwords never create a saved profile.
        var id=pending.Adapter;uint saved=WlanSetProfile(client,ref id,2,pending.Xml,null,false,0,out _);pendingProfile=null;if(saved!=0)connectionWarning="Подключено, но Windows не сохранила сеть: "+new System.ComponentModel.Win32Exception((int)saved).Message;
    }
    private static string BuildProfile(Network network,string? password,bool remember)
    {
        if(!network.NewSupported)throw new IOException("Для этой сети нужен настроенный профиль Windows");
        bool personal=network.Personal;bool raw=personal&&network.Auth!=9&&password is {Length:64}&&password.All(Uri.IsHexDigit);
        if(personal&&!raw&&(password==null||password.Length is <8 or >63||password.Any(c=>c<32||c>126)))throw new ArgumentException("Пароль должен содержать от 8 до 63 символов; WPA/WPA2 также принимает 64 шестнадцатеричных символа");
        XNamespace ns="http://www.microsoft.com/networking/WLAN/profile/v1";
        var auth=new XElement(ns+"authEncryption",new XElement(ns+"authentication",network.Auth switch{1=>"open",4=>"WPAPSK",7=>"WPA2PSK",9=>"WPA3SAE",10=>"OWE",_=>throw new IOException("Unsupported network authentication")}),new XElement(ns+"encryption",network.Cipher switch{0=>"none",2=>"TKIP",4=>"AES",_=>throw new IOException("Unsupported network encryption")}),new XElement(ns+"useOneX","false"));
        var security=new XElement(ns+"security",auth);
        if(personal)security.Add(new XElement(ns+"sharedKey",new XElement(ns+"keyType",raw?"networkKey":"passPhrase"),new XElement(ns+"protected","false"),new XElement(ns+"keyMaterial",password)));
        return new XElement(ns+"WLANProfile",new XElement(ns+"name","ONEXTweaks-"+Guid.NewGuid().ToString("N")),new XElement(ns+"SSIDConfig",new XElement(ns+"SSID",new XElement(ns+"hex",network.SsidHex))),new XElement(ns+"connectionType","ESS"),new XElement(ns+"connectionMode",remember?"auto":"manual"),new XElement(ns+"MSM",security)).ToString(SaveOptions.DisableFormatting);
    }
    private static Network[] Networks(nint client)
    {
        var result=new List<Network>();
        foreach(var adapter in Adapters(client))
        {
            nint data=0;var id=adapter.Id;
            try
            {
                Check(WlanGetAvailableNetworkList(client,ref id,2,0,out data),"Read available Wi-Fi networks");int count=Marshal.ReadInt32(data);if(count is <0 or >4096)throw new IOException("Invalid available network count");
                for(int i=0;i<count;i++)
                {
                    var row=data+8+i*628;int length=Marshal.ReadInt32(row,512);if(length is <1 or >32)continue;
                    var ssid=new byte[length];Marshal.Copy(row+516,ssid,0,length);string hex=Convert.ToHexString(ssid),profile=Marshal.PtrToStringUni(row,256)?.TrimEnd('\0')??"";
                    int bss=Marshal.ReadInt32(row,548),auth=Marshal.ReadInt32(row,612),cipher=Marshal.ReadInt32(row,616),flags=Marshal.ReadInt32(row,620);
                    string token=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id+"/"+hex+"/"+bss+"/"+auth+"/"+cipher)));
                    result.Add(new(id,token,Clean(Encoding.UTF8.GetString(ssid)),hex,profile,bss,auth,cipher,(flags&1)!=0,(flags&2)!=0&&profile.Length>0,Marshal.ReadInt32(row,608)!=0,Math.Clamp(Marshal.ReadInt32(row,604),0,100),Marshal.ReadInt32(row,556)!=0));
                }
            }
            finally{if(data!=0)WlanFreeMemory(data);}
        }
        return result.GroupBy(n=>n.Token).Select(g=>g.OrderByDescending(n=>n.Connected).ThenByDescending(n=>n.Known).First()).OrderByDescending(n=>n.Connected).ThenByDescending(n=>n.Signal).ThenBy(n=>n.Name,StringComparer.CurrentCultureIgnoreCase).Take(256).ToArray();
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct ConnectionParameters
    {public int Mode;[MarshalAs(UnmanagedType.LPWStr)]public string Profile;public nint Ssid,Bssid;public int Bss;public uint Flags;}
    [DllImport("wlanapi.dll")]private static extern uint WlanGetAvailableNetworkList(nint client,ref Guid id,uint flags,nint reserved,out nint list);
    [DllImport("wlanapi.dll")]private static extern uint WlanScan(nint client,ref Guid id,nint ssid,nint extra,nint reserved);
    [DllImport("wlanapi.dll",EntryPoint="WlanConnect",CharSet=CharSet.Unicode)]private static extern uint WlanConnectNetwork(nint client,ref Guid id,ref ConnectionParameters parameters,nint reserved);
    [DllImport("wlanapi.dll")]private static extern uint WlanDisconnect(nint client,ref Guid id,nint reserved);
    [DllImport("wlanapi.dll",CharSet=CharSet.Unicode)]private static extern uint WlanSetProfile(nint client,ref Guid id,uint flags,string xml,string? security,[MarshalAs(UnmanagedType.Bool)]bool overwrite,nint reserved,out uint reason);
}
