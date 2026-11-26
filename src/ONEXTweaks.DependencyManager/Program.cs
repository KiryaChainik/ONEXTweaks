// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;
namespace ONEXTweaks.DependencyManager
{
 internal static class Program
 {
  private static readonly string Dev=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ONEXTweaks","dev");
  private static readonly string WidgetFamily=typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
   .Single(attribute=>attribute.Key=="OnexWidgetPackageFamily").Value;
  internal static readonly string Local=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Packages",WidgetFamily,"LocalState");
  private static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=4*1024*1024};
  private static State state;
  [STAThread]private static int Main(string[] args)
  {
   try{
    ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
    if(args.Length==2&&args[0]=="--models"){Models.Run(args[1]);return 0;}
    if(args.Length==2&&args[0]=="--verify-downloads"){VerifyDownloads(args[1]).GetAwaiter().GetResult();return 0;}
    using(var mutex=new Mutex(false,@"Local\ONEXTweaks.DependencyManager.v1")){
     bool owned=false;try{try{owned=mutex.WaitOne(0);}catch(AbandonedMutexException){owned=true;}if(!owned)return 0;
      Directory.CreateDirectory(Local);string last="";
      try{
       var previous=Json.Deserialize<State>(File.ReadAllText(Path.Combine(Local,"onex-drivers-result.json")));
       if(previous!=null){last=previous.requestId??"";if(!previous.done){state=previous;state.rows=Inventory();Publish("interrupted","The previous component manager stopped. Check any open Windows installer, then refresh installed components. No update was restarted automatically.",true,false);}}
      }catch(IOException){}catch(ArgumentException){}
      DateTime idle=DateTime.UtcNow.AddSeconds(70);
      while(DateTime.UtcNow<idle){
       try{var path=Path.Combine(Local,"onex-drivers-request.json");if(File.Exists(path)){
        var request=Json.Deserialize<Request>(File.ReadAllText(path));Guid id;DateTime stamp;
        if(request!=null&&Guid.TryParseExact(request.id,"N",out id)&&request.id!=last&&DateTime.TryParse(request.utc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out stamp)&&Math.Abs((DateTime.UtcNow-stamp.ToUniversalTime()).TotalSeconds)<20){
         last=request.id;idle=DateTime.UtcNow.AddSeconds(70);ProcessRequest(request).GetAwaiter().GetResult();
        }
       }}catch(IOException){}catch(ArgumentException){}
       Thread.Sleep(150);
      }
     }finally{if(owned)mutex.ReleaseMutex();}
    }return 0;
   }catch{return 1;}
  }
  internal static void Atomic(string path,string text)
  {string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(tmp,text,new UTF8Encoding(false));try{if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}finally{if(File.Exists(tmp))File.Delete(tmp);}}
  private static void Publish(string phase,string message,bool done=false,bool ok=true)
  {
   foreach(var row in state.rows)foreach(var field in typeof(Row).GetFields())if(field.FieldType==typeof(string)&&field.GetValue(row)==null)field.SetValue(row,"");
   state.phase=phase;state.message=message;state.done=done;state.ok=ok;state.utc=DateTimeOffset.UtcNow.ToString("O");
   Atomic(Path.Combine(Local,"onex-drivers-result.json"),Json.Serialize(state));
  }
  private static async Task ProcessRequest(Request request)
  {
   state=new State{requestId=request.id,operation=request.action};
   try{
    if(request.action!="inventory"&&request.action!="check"&&request.action!="update")throw new InvalidOperationException("Unsupported component operation.");
    state.rows=Inventory();Publish("inventory","Reading installed components…");
    if(request.action=="check"){
     foreach(var d in Policy.Definitions){
      var row=state.rows.Single(r=>r.id==d.Id);Publish("checking","Checking "+d.Name+"…");
      try{await Check(d,row);}catch(Exception e){row.status="Check failed";row.detail=Safe(e);row.canUpdate=false;row.lastChecked=DateTimeOffset.UtcNow.ToString("O");}
     }
     var oem=state.rows.Single(x=>x.id=="onexdrivers");
     try{
      using(var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=true}){Timeout=TimeSpan.FromSeconds(25)}){
       client.DefaultRequestHeaders.UserAgent.ParseAdd("ONEXTweaks/0.29.385");client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
       using(var response=await client.GetAsync(VendorProviders.OnexSource)){
       response.EnsureSuccessStatusCode();string page=await response.Content.ReadAsStringAsync();
       bool listed=page.Contains("197i2xoRkOVQjRfURqhbAprVZKd6WC_oV");
       oem.source=listed&&SupportsOnex3()?VendorProviders.Onex3Pack:VendorProviders.OnexSource;
       oem.latest=listed&&SupportsOnex3()?"1.1":"";oem.lastChecked=DateTimeOffset.UtcNow.ToString("O");
       oem.catalogVerified=listed;
       oem.detail=listed?"Official model package confirmed. Package version is not an installed driver version. Download and installation continue on the vendor page; Google Drive may limit large downloads.":"The model catalog changed. Select your exact model on the official page.";
      }}
     }catch(Exception){bool supported=SupportsOnex3();oem.source=supported?VendorProviders.Onex3Pack:VendorProviders.OnexSource;oem.latest=supported?"1.1":"";oem.lastChecked=DateTimeOffset.UtcNow.ToString("O");oem.catalogVerified=false;oem.detail="Live catalog unavailable. The linked model pack is bundled catalog information, not a confirmed latest release. Download and installation continue on the vendor page.";}
     SaveCache();Publish("complete","GitHub versions checked; OEM catalog checked separately. Intel updates continue in Intel DSA. Vendor-managed rows do not report an installed-driver update result.",true);return;
    }
    if(request.action=="update"){
     var d=Policy.Definitions.SingleOrDefault(x=>x.Id==request.component)??throw new InvalidOperationException("Unknown component.");
     var row=state.rows.Single(x=>x.id==d.Id);
     Publish("checking","Checking the official release again…");await Check(d,row);
     if(request.expectedVersion!=row.latest)throw new InvalidOperationException("The release changed. Run Check for updates again.");
     Policy.CanInstall(d,row.current,row.latest,CaptureActive());
     Publish("downloading","Downloading "+d.Name+" from its official release…");
     string file=await Download(d,row,p=>Publish("downloading","Downloading "+d.Name+": "+p+"%"));
     try{
      using(var held=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read)){
      Publish("verifying","Checking the downloaded file and publisher…");VerifySignature(file,d);
      Policy.CanInstall(d,Installed(d.Id).Item1,row.latest,CaptureActive());
      Publish("installing","Opening the Windows installer. Administrator permission may be requested. Keep this device powered on.");
      // Official interactive installer; do not invent silent switches or initiate a restart.
      var preferences=PreferenceHashes();string power=PowerSignature();
      int code;using(var process=Process.Start(new ProcessStartInfo(file){UseShellExecute=true,Verb="runas",WorkingDirectory=Path.GetDirectoryName(file)})){
       if(process==null)throw new IOException("Windows did not start the installer.");while(!process.WaitForExit(1000))Publish("installing","Waiting for the Windows installer. Finish or cancel its window to continue.");code=process.ExitCode;
      }
      if(code!=0&&code!=3010&&code!=1641)throw new IOException(code==1602?"Installation was cancelled.":"Installer returned error "+code+".");
      state.rebootRequired=code==3010||code==1641;state.rows=Inventory();
      var after=state.rows.Single(x=>x.id==d.Id);
      if(!state.rebootRequired&&(string.IsNullOrEmpty(after.current)||Policy.VersionOf(after.current)<Policy.VersionOf(row.latest)))
       throw new IOException("The installed version did not change. If the installer requested a restart, restart Windows and refresh.");
      bool prefsSame=preferences.OrderBy(x=>x.Key).SequenceEqual(PreferenceHashes().OrderBy(x=>x.Key));
      bool powerSame=power==PowerSignature();
      string preservation=prefsSame&&powerSame?"":" Current settings changed while the installer was open; the latest settings were kept and no old snapshot was restored.";
      SaveCache();Publish("complete",state.rebootRequired?"Installer completed. Restart Windows to finish, then refresh the installed state."+preservation:"Component updated. Installed version verified."+preservation,true);
      }
     }finally{TryDelete(file);}return;
    }
    Publish("complete","Installed components refreshed.",true);
   }catch(Exception e){Publish("error",Safe(e),true,false);}
  }
  private static Dictionary<string,string> PreferenceHashes()
  {return Directory.Exists(Dev)?Directory.GetFiles(Dev,"*.json").ToDictionary(Path.GetFileName,Hash):new Dictionary<string,string>();}
  private static string PowerSignature()
  {
   try{var report=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Dev,"Reports","widget-bridge.json")));var power=(Dictionary<string,object>)report["state"];return Json.Serialize(power["rawLimits"]);}
   catch{return "unavailable";}
  }
  private static string Safe(Exception e)
  {return e is System.ComponentModel.Win32Exception&&((System.ComponentModel.Win32Exception)e).NativeErrorCode==1223?"Administrator permission was cancelled.":e.Message.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>");}
  private static bool CaptureActive()=>File.Exists(Path.Combine(Dev,"privateReports","controller-capture-lease.json"));
  private static List<Row> Inventory()
  {
   var cached=new List<Row>();try{cached=Json.Deserialize<List<Row>>(File.ReadAllText(Path.Combine(Local,"onex-drivers-cache.json")))??cached;}catch(IOException){}catch(ArgumentException){}
   var rows=new List<Row>();
   foreach(var d in Policy.Definitions){
    var current=Installed(d.Id);var row=new Row{id=d.Id,name=d.Name,purpose=d.Purpose,current=current.Item1,installed=!string.IsNullOrEmpty(current.Item1),service=current.Item2,source="https://github.com/"+d.Repository+"/releases",bundled=d.Id=="viiper",status="Not checked",detail=d.Id=="viiper"?current.Item2:"Installed state: "+current.Item2};
    var old=cached.FirstOrDefault(x=>x.id==row.id);
    if(old!=null){row.latest=old.latest;row.released=old.released;row.lastChecked=old.lastChecked;row.asset=old.asset;row.url=old.url;row.size=old.size;row.digest=old.digest;
     try{row.status=string.IsNullOrEmpty(old.latest)?"Not checked":Policy.Compare(row.current,old.latest);}catch{row.status="Version unknown";}
     try{row.canUpdate=old.canUpdate&&(string.IsNullOrEmpty(row.current)||Policy.VersionOf(row.latest)>Policy.VersionOf(row.current));}catch{row.canUpdate=false;}
     if(old.status=="Check failed"){row.status="Check failed";row.detail=old.detail;row.canUpdate=false;}}
    if(!row.installed)row.status="Not installed";rows.Add(row);
   }
   var rtss=Uninstall("rtss");rows.Add(new Row{id="rtss",name="RivaTuner Statistics Server",purpose="Optional RTSS overlay and FPS limiter.",current=rtss,installed=rtss!=null,status="Vendor-managed",source="https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/",detail="RTSS is distributed outside GitHub. Use its official download page."});
   string dsa=Uninstall("inteldsa");string dsaSource=VendorProviders.IntelSource(!string.IsNullOrEmpty(dsa));
   rows.Add(new Row{id="inteldsa",name="Intel Driver & Support Assistant",purpose="Checks supported Intel graphics, Wi-Fi and Bluetooth drivers.",current=dsa,installed=dsa!=null,status="Vendor-managed",source=dsaSource,service=IntelDsaServices(),detail=dsa==null?"Not installed. Select to download the official Intel installer, then refresh installed components.":"Installed. Select to check Intel drivers on Intel's page. Your browser may ask to connect to the local Intel service."});
   string onex=Uninstall("onexconsole");
   rows.Add(new Row{id="onexconsole",name="ONEXConsole",purpose="Installed OEM application; official model driver catalog.",current=onex,installed=onex!=null,status="Vendor-managed",source=VendorProviders.OnexSource,detail="This is the ONEXConsole application version, not the OEM driver-pack version. The catalog opens without launching ONEXConsole or changing its settings."});
   var oldOem=cached.FirstOrDefault(x=>x.id=="onexdrivers");bool modelSupported=SupportsOnex3();
   rows.Add(new Row{id="onexdrivers",name=modelSupported?"ONEXPLAYER 3 OEM driver pack":"ONEXPLAYER OEM driver catalog",purpose="Model-specific chipset, audio and device drivers.",status="Vendor-managed",source=modelSupported?VendorProviders.Onex3Pack:VendorProviders.OnexSource,latest=modelSupported?"1.1":"",lastChecked=oldOem==null?"":oldOem.lastChecked,catalogVerified=oldOem!=null&&oldOem.catalogVerified,detail=oldOem==null?"Bundled model-specific catalog link. Run Check for updates to verify the live catalog. Individual installed driver versions are managed by Windows.":oldOem.detail});
   try{using(var search=new ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController"))foreach(ManagementObject item in search.Get()){
    var name=Convert.ToString(item["Name"]);if(name.IndexOf("Intel",StringComparison.OrdinalIgnoreCase)<0)continue;
    string date=null;try{date=ManagementDateTimeConverter.ToDateTime(Convert.ToString(item["DriverDate"])).ToUniversalTime().ToString("O");}catch(ArgumentOutOfRangeException){}
    rows.Add(new Row{id="graphics",name=name,purpose="Display colour, scaling, HDR and Intel FPS settings.",current=Convert.ToString(item["DriverVersion"]),released=date,installed=true,status="Vendor-managed",source=dsaSource,detail=dsa==null?"Install Intel Driver & Support Assistant first to check this driver. Select to download its official installer.":"Windows reports this driver date. Select to check for updates through the installed Intel Driver & Support Assistant."});
   }}catch(ManagementException){}catch(System.Runtime.InteropServices.COMException){}
   using(var root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))using(var runtime=root.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App")){
    string installed=runtime==null?null:runtime.GetValueNames().Where(x=>x.StartsWith("10.",StringComparison.Ordinal)).OrderByDescending(x=>Policy.VersionOf(x)).FirstOrDefault();
    if(installed==null){
     string bundled=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"ONEXTweaks\toolchain\dotnet-10.0.403\shared\Microsoft.NETCore.App");
     if(Directory.Exists(bundled))installed=Directory.GetDirectories(bundled).Select(Path.GetFileName).Where(x=>x.StartsWith("10.",StringComparison.Ordinal)).OrderByDescending(x=>Policy.VersionOf(x)).FirstOrDefault();
    }
    rows.Add(new Row{id="dotnet",name="Microsoft .NET 10 runtime",purpose="Runs the ONEXTweaks Bridge.",current=installed,installed=installed!=null,status="Vendor-managed",source="https://dotnet.microsoft.com/download/dotnet/10.0",detail="Microsoft runtime; update through its official installer or Windows servicing."});
   }
   return rows;
  }
  private static bool SupportsOnex3()
  {
   try{using(var query=new ManagementObjectSearcher("SELECT Model FROM Win32_ComputerSystem"))foreach(ManagementObject item in query.Get())if(VendorProviders.SupportsOnex3(Convert.ToString(item["Model"])))return true;}catch(ManagementException){}catch(System.Runtime.InteropServices.COMException){}return false;
  }
  private static string IntelDsaServices()
  {
   var states=new List<string>();try{using(var query=new ManagementObjectSearcher("SELECT Name,State FROM Win32_Service WHERE Name='DSAService' OR Name='DSAUpdateService'"))foreach(ManagementObject item in query.Get())states.Add((Convert.ToString(item["Name"])=="DSAService"?"DSA: ":"Updater: ")+Convert.ToString(item["State"]));}catch(ManagementException){}catch(System.Runtime.InteropServices.COMException){}
   return states.Count==0?"Services not found / unavailable":string.Join(" · ",states.OrderBy(x=>x));
  }
  private static Tuple<string,string> Installed(string id)
  {
   if(id=="viiper"){
    string file=Path.Combine(Dev,@"project\src\ONEXTweaks.WidgetBridge\bin\Release\net10.0-windows\libVIIPER.dll");
    if(File.Exists(file)&&Hash(file)==ViiperHash)return Tuple.Create("0.9.0","Bundled library verified");
    return Tuple.Create((string)null,File.Exists(file)?"Library changed or unrecognised":"Not installed");
   }
   string service=id=="pawnio"?"PawnIO":id=="hidhide"?"HidHide":"usbip2_ude";string version=Uninstall(id),status="Not installed";
   using(var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+service)){
    if(key!=null){status=Convert.ToInt32(key.GetValue("Start",3))==4?"Disabled":"Installed";
     try{using(var query=new ManagementObjectSearcher("SELECT State FROM Win32_SystemDriver WHERE Name='"+service+"'"))foreach(ManagementObject o in query.Get())status=Convert.ToString(o["State"]);}catch(ManagementException){}
    }
   }return Tuple.Create(version,status);
  }
  private const string ViiperHash="879B4DD556A8A5374957995406C749CD92CBB865D4E0632BD9C37E88A28C12C9";
  internal static string Uninstall(string id)
  {
   foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})using(var root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view))using(var all=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")){
    if(all==null)continue;foreach(string name in all.GetSubKeyNames())using(var key=all.OpenSubKey(name)){
     if(key==null)continue;string display=Convert.ToString(key.GetValue("DisplayName",""));bool match=VendorProviders.MatchesInstalled(id,display);
     if(match){string v=Convert.ToString(key.GetValue("DisplayVersion",""));if(!string.IsNullOrEmpty(v))return v;}
    }
   }return null;
  }
  private static void SaveCache()=>Atomic(Path.Combine(Local,"onex-drivers-cache.json"),Json.Serialize(state.rows));
  private static async Task Check(Definition d,Row row)
  {
   using(var client=Client())using(var response=await client.GetAsync("https://api.github.com/repos/"+d.Repository+"/releases/latest")){
    if(!response.IsSuccessStatusCode)throw new IOException((int)response.StatusCode==403?"GitHub rate limit reached. Try again later.":"GitHub returned HTTP "+(int)response.StatusCode+".");
    var data=Json.Deserialize<Dictionary<string,object>>(await response.Content.ReadAsStringAsync());
    if((bool)data["draft"]||(bool)data["prerelease"])throw new IOException("GitHub did not return a stable public release.");
    row.latest=(string)data["tag_name"];var version=Policy.VersionOf(row.latest);
    row.released=(string)data["published_at"];row.lastChecked=DateTimeOffset.UtcNow.ToString("O");row.status=Policy.Compare(row.current,row.latest);row.canUpdate=false;
    var assets=((IEnumerable)data["assets"]).Cast<Dictionary<string,object>>().Where(x=>Policy.AssetAllowed(d,(string)x["name"],(string)x["browser_download_url"],Convert.ToInt64(x["size"]))).ToArray();
    if(assets.Length!=1){row.detail="No unique compatible Windows x64 package in this release.";return;}
    var asset=assets[0];row.asset=(string)asset["name"];row.url=(string)asset["browser_download_url"];row.size=Convert.ToInt64(asset["size"]);object digest;row.digest=asset.TryGetValue("digest",out digest)?digest as string:null;
    row.canUpdate=d.Id!="viiper"&&Policy.Compatible(d,version)&&(string.IsNullOrEmpty(row.current)||version>Policy.VersionOf(row.current));
    row.detail=d.Id=="viiper"?"Bundled library: updates are included in compatible ONEXTweaks releases.":!Policy.Compatible(d,version)?"New release requires ONEXTweaks compatibility review.":"Official stable Windows x64 release. The Windows installer may request administrator permission.";
   }
  }
  private static HttpClient Client()
  {var c=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(30)};c.DefaultRequestHeaders.UserAgent.ParseAdd("ONEXTweaks/0.29.367");c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");return c;}
  private static async Task<string> Download(Definition d,Row row,Action<int> progress)
  {
   if(!Policy.AssetAllowed(d,row.asset,row.url,row.size))throw new IOException("Untrusted release asset.");
   string directory=Path.Combine(Local,"DriverDownloads");Directory.CreateDirectory(directory);string target=Path.Combine(directory,Guid.NewGuid().ToString("N")+"-"+row.asset);
   try{Uri uri=new Uri(row.url);using(var client=Client()){
    for(int n=0;n<6;n++){
     if(!Policy.RedirectAllowed(uri))throw new IOException("Untrusted download redirect.");
     using(var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead)){
      if((int)response.StatusCode>=300&&(int)response.StatusCode<400){uri=new Uri(uri,response.Headers.Location??throw new IOException("Missing redirect destination."));continue;}
      response.EnsureSuccessStatusCode();using(var input=await response.Content.ReadAsStreamAsync())using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
       byte[] buffer=new byte[65536];long total=0;int read,last=-1;using(var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(5)))while((read=await input.ReadAsync(buffer,0,buffer.Length,deadline.Token))>0){total+=read;if(total>row.size)throw new IOException("Download exceeds official size.");await output.WriteAsync(buffer,0,read);int p=(int)(total*100/row.size);if(p!=last){last=p;progress(p);}}if(total!=row.size)throw new IOException("Download is incomplete.");
      }
      if(!string.IsNullOrEmpty(row.digest)&&(!row.digest.StartsWith("sha256:",StringComparison.Ordinal)||!Hash(target).Equals(row.digest.Substring(7),StringComparison.OrdinalIgnoreCase)))throw new IOException("Official SHA256 does not match.");
      return target;
     }
    }throw new IOException("Too many download redirects.");
   }}catch{TryDelete(target);throw;}
  }
  private static string Hash(string file){using(var sha=SHA256.Create())using(var stream=File.OpenRead(file))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
  private static void TryDelete(string file){try{File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}}
  private static void VerifySignature(string file,Definition d)
  {
   // Static script and a controlled generated filename; no release text is evaluated as code.
   string script="$s=Get-AuthenticodeSignature -LiteralPath '"+file.Replace("'","''")+"';if($s.Status -ne 'Valid'){exit 2};if($s.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName,$false) -ne '"+d.Publisher.Replace("'","''")+"'){exit 3};exit 0";
   var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe"),"-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script))){UseShellExecute=false,CreateNoWindow=true};
   using(var process=Process.Start(start)){if(!process.WaitForExit(30000))throw new IOException("Publisher check timed out.");if(process.ExitCode!=0)throw new IOException(process.ExitCode==3?"Installer publisher is not the expected publisher.":"Installer digital signature is not valid.");}
  }
  private static async Task VerifyDownloads(string report)
  {
   Directory.CreateDirectory(Local);var results=new List<object>();
   foreach(var d in Policy.Definitions.Where(d=>d.Id!="viiper")){
    var row=new Row{id=d.Id};await Check(d,row);string file=await Download(d,row,p=>{});
    try{VerifySignature(file,d);results.Add(new{id=d.Id,version=row.latest,sha256=Hash(file),signatureValid=true,installerExecuted=false});}finally{TryDelete(file);}
   }Atomic(report,Json.Serialize(new{passed=true,realOfficialMetadataAndDownloads=true,checks=results,installerExecuted=false}));
  }
 }
}
