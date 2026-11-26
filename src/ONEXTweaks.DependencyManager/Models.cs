// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
namespace ONEXTweaks.DependencyManager
{
 internal static class Models
 {
  internal static void Run(string file)
  {
   int checks=0;Action<bool,string> check=(ok,why)=>{checks++;if(!ok)throw new InvalidOperationException(why);};
   Action<Action> reject=action=>{bool caught=false;try{action();}catch(InvalidOperationException){caught=true;}check(caught,"Unsafe update accepted");};
   var pawn=Policy.Definitions.Single(x=>x.Id=="pawnio");var usb=Policy.Definitions.Single(x=>x.Id=="usbip");var hide=Policy.Definitions.Single(x=>x.Id=="hidhide");
   check(Policy.Compare("1.5.230","v1.5.230.0")=="Up to date","Equivalent release versions differ");
   check(Policy.Compare("2.2.0.0","2.2.0")=="Up to date","Trailing zero mismatch");
   check(Policy.Compare("2.1","2.2.0")=="New version detected","New release not detected");
   check(Policy.Compare(null,"2.2.0")=="Not installed","Missing install misreported");
   check(Policy.Compare("2.3.0","2.2.0")=="Up to date","Installed newer version would downgrade");
   foreach(string invalid in new[]{"v2.2.0-beta","latest","2.2.0.exe","", "2.2.0.1.2"})reject(()=>Policy.VersionOf(invalid));
   check(Policy.AssetAllowed(usb,"USBip-0.9.8.1-x64.exe","https://github.com/vadimgrn/usbip-win2/releases/download/v.0.9.8.1/USBip-0.9.8.1-x64.exe",26083632),"Official x64 asset rejected");
   check(!Policy.AssetAllowed(usb,"USBip-0.9.8.1-arm64.exe","https://github.com/vadimgrn/usbip-win2/releases/download/v.0.9.8.1/USBip-0.9.8.1-arm64.exe",26083632),"ARM installer accepted");
   foreach(string url in new[]{"http://github.com/vadimgrn/usbip-win2/releases/download/v.0.9.8.1/USBip-0.9.8.1-x64.exe","https://github.com.evil.test/vadimgrn/usbip-win2/releases/download/v.0.9.8.1/USBip-0.9.8.1-x64.exe","https://github.com/other/repo/releases/download/v.0.9.8.1/USBip-0.9.8.1-x64.exe"})
    check(!Policy.AssetAllowed(usb,"USBip-0.9.8.1-x64.exe",url,26083632),"Unofficial source accepted");
   check(!Policy.AssetAllowed(pawn,"PawnIO_setup.exe","https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe",151L*1024*1024),"Oversized asset accepted");
   check(!Policy.RedirectAllowed(new Uri("https://example.invalid/file.exe")),"Foreign download redirect accepted");
   check(!Policy.RedirectAllowed(new Uri("http://release-assets.githubusercontent.com/file")),"Insecure redirect accepted");
   check(Policy.RedirectAllowed(new Uri("https://release-assets.githubusercontent.com/file")),"Official asset host rejected");
   reject(()=>Policy.CanInstall(usb,"0.9.8.1","0.10.0",false));
   reject(()=>Policy.CanInstall(pawn,"2.2.0","3.0.0",false));
   reject(()=>Policy.CanInstall(hide,"1.5.230","1.5.231",true));
   reject(()=>Policy.CanInstall(pawn,"2.2.0","2.2.0",false));
   reject(()=>Policy.CanInstall(pawn,"2.3.0","2.2.0",false));
   reject(()=>Policy.CanInstall(Policy.Definitions.Single(x=>x.Id=="viiper"),"0.9.0","0.9.1",false));
   Policy.CanInstall(hide,"1.5.230","1.5.231",false);check(true,"Compatible update rejected");
   Policy.CanInstall(pawn,null,"2.2.0",false);check(true,"Compatible fresh install rejected");
   check(VendorProviders.MatchesInstalled("inteldsa","Intel Driver && Support Assistant"),"Intel DSA inventory match failed");
   check(!VendorProviders.MatchesInstalled("inteldsa","Intel Computing Improvement Program"),"Telemetry package misidentified as DSA");
   check(VendorProviders.MatchesInstalled("onexconsole","OneXConsole"),"ONEXConsole inventory match failed");
   check(!VendorProviders.MatchesInstalled("unknown","RivaTuner Statistics Server"),"Unknown component matched RTSS");
   check(new Uri(VendorProviders.IntelSource(false)).Host=="dsadata.intel.com","Missing DSA does not offer official prerequisite");
   check(new Uri(VendorProviders.IntelSource(true)).AbsolutePath.EndsWith("/detect.html",StringComparison.Ordinal),"Installed DSA does not open driver check");
   check(new Uri(VendorProviders.OnexSource).Host=="onexplayerstore.com","OEM source is not official");
   check(VendorProviders.SupportsOnex3("ONEXPLAYER 3")&&VendorProviders.SupportsOnex3("Gi78P"),"Supported OEM model not recognised");
   check(!VendorProviders.SupportsOnex3("ONEXPLAYER 3 other")&&!VendorProviders.SupportsOnex3("ONEXPLAYER X1")&&!VendorProviders.SupportsOnex3(null),"A different/unknown model selected ONEXPLAYER3 drivers");
   check(new Uri(VendorProviders.Onex3Pack).Host=="drive.google.com"&&new Uri(VendorProviders.Onex3Pack).AbsolutePath=="/file/d/197i2xoRkOVQjRfURqhbAprVZKd6WC_oV/view","OEM model pack source changed");
   Program.Atomic(file,new JavaScriptSerializer().Serialize(new{passed=true,checks,modelOnly=true,installerExecuted=false,driverChanges=0,physicalOwnerInputTested=false}));
  }
 }
}
