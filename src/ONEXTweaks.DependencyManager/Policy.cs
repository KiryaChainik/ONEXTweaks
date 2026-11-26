// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
namespace ONEXTweaks.DependencyManager
{
 internal sealed class Definition
 {
  internal string Id,Name,Purpose,Repository,AssetPattern,Publisher;internal int Major,Minor=-1;
  internal Definition(string id,string name,string purpose,string repo,string asset,string publisher,int major,int minor=-1)
  {Id=id;Name=name;Purpose=purpose;Repository=repo;AssetPattern=asset;Publisher=publisher;Major=major;Minor=minor;}
 }
 internal static class Policy
 {
  internal static readonly Definition[] Definitions={
   new Definition("pawnio","PawnIO","Hardware access for TDP, fan and lighting.","namazso/PawnIO.Setup",@"^PawnIO_setup\.exe$","namazso.eu",2),
   new Definition("hidhide","HidHide","Hides the physical controller while virtual remapping is active.","nefarius/HidHide",@"^HidHide_[0-9.]+_x64\.exe$","Nefarius Software Solutions e.U.",1),
   new Definition("usbip","usbip-win2","Signed virtual USB host used by VIIPER.","vadimgrn/usbip-win2",@"^USBip-[0-9.]+-x64\.exe$","Cloudyne Systems (Scheibling Consulting AB)",0,9),
   new Definition("viiper","VIIPER library","Bundled virtual-controller library; updated with a compatible ONEXTweaks release.","Alia5/VIIPER",@"^viiper-libVIIPER-windows-amd64\.zip$","",0,9)
  };
  internal static Version VersionOf(string text)
  {
   var m=Regex.Match(text??"",@"^(?:v\.?)?(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?$",RegexOptions.IgnoreCase);
   if(!m.Success)throw new InvalidOperationException("Unrecognised release version.");
   return new Version(int.Parse(m.Groups[1].Value),int.Parse(m.Groups[2].Value),m.Groups[3].Success?int.Parse(m.Groups[3].Value):0,m.Groups[4].Success?int.Parse(m.Groups[4].Value):0);
  }
  internal static bool Compatible(Definition d,Version v)=>v.Major==d.Major&&(d.Minor<0||v.Minor==d.Minor);
  internal static bool AssetAllowed(Definition d,string name,string url,long size)
  {
   Uri u;return size>=4096&&size<=150*1024*1024&&Regex.IsMatch(name??"",d.AssetPattern,RegexOptions.CultureInvariant)&&
    Uri.TryCreate(url,UriKind.Absolute,out u)&&u.Scheme=="https"&&u.Host=="github.com"&&
    u.AbsolutePath.StartsWith("/"+d.Repository+"/releases/download/",StringComparison.Ordinal)&&
    Uri.UnescapeDataString(u.Segments[u.Segments.Length-1])==name;
  }
  internal static bool RedirectAllowed(Uri uri)=>uri.Scheme=="https"&&(uri.Host=="github.com"||uri.Host=="release-assets.githubusercontent.com"||uri.Host=="objects.githubusercontent.com");
  internal static string Compare(string installed,string latest)
  {if(string.IsNullOrEmpty(installed))return "Not installed";return VersionOf(latest)>VersionOf(installed)?"New version detected":"Up to date";}
  internal static void CanInstall(Definition d,string installed,string latest,bool captureLease)
  {
   if(d.Id=="viiper")throw new InvalidOperationException("The bundled VIIPER library needs a compatible ONEXTweaks update.");
   if(!Compatible(d,VersionOf(latest)))throw new InvalidOperationException("This release needs compatibility review before installation.");
   if(!string.IsNullOrEmpty(installed)&&VersionOf(latest)<=VersionOf(installed))throw new InvalidOperationException("The installed component is already up to date.");
   if(captureLease)throw new InvalidOperationException("Select Hardware controller mode and stop remapping before updating a driver. Input recovery remains enabled.");
  }
 }
 public sealed class Row
 {
  public string id,name,purpose,current,latest,released,lastChecked,status,detail,source,asset,url,digest,service;
  public long size;public bool installed,canUpdate,bundled,catalogVerified;
 }
 public sealed class State
 {
  public string requestId,operation,phase,message,utc;public bool done,ok,rebootRequired;
  public List<Row> rows=new List<Row>();
 }
 public sealed class Request {public string id,action,component,expectedVersion,utc;}
}
