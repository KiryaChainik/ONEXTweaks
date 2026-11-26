// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;
namespace ONEXTweaks.WidgetBridge;
internal sealed record ControllerViiperInstallationState(bool LibraryInstalled,bool LibraryVerified,bool DriverInstalled,string DriverService,string DriverState,bool DriverDisabled,bool CanUseVirtual);
internal static class ControllerViiperInstallation
{
 private static readonly object Gate=new();private static ControllerViiperInstallationState? cached;private static long due;
 [StructLayout(LayoutKind.Sequential)]private struct Status{internal uint Type,State,Controls,Exit,ServiceExit,Checkpoint,Wait;}
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint OpenSCManagerW(string? machine,string? database,uint access);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint OpenServiceW(nint scm,string name,uint access);
 [DllImport("advapi32.dll",SetLastError=true)]private static extern bool QueryServiceStatus(nint service,out Status status);
 [DllImport("advapi32.dll")]private static extern bool CloseServiceHandle(nint handle);
 internal static ControllerViiperInstallationState Read(bool refresh=false)
 {
  lock(Gate){long now=Environment.TickCount64;if(!refresh&&cached!=null&&now<due)return cached;
   string image=Path.Combine(AppContext.BaseDirectory,"libVIIPER.dll");bool present=File.Exists(image),verified=false,installed=false,disabled=false;string state="Not installed";
   try{verified=present&&Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(image))).Equals(ControllerViiperDevice.LibrarySha256,StringComparison.OrdinalIgnoreCase);}catch{}
   try{using var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\usbip2_ude");installed=key!=null;disabled=key?.GetValue("Start") is int start&&start==4;if(installed)state="Unavailable";}catch{state="Unavailable";}
   nint scm=0,service=0;try{scm=OpenSCManagerW(null,null,1);if(scm!=0)service=OpenServiceW(scm,"usbip2_ude",4);if(service!=0&&QueryServiceStatus(service,out var status)){installed=true;state=status.State switch{1=>"Stopped",2=>"Starting",3=>"Stopping",4=>"Running",7=>"Paused",_=>"Unavailable"};}}finally{if(service!=0)CloseServiceHandle(service);if(scm!=0)CloseServiceHandle(scm);}
   cached=new(present,verified,installed,"usbip2_ude",state,disabled,verified&&installed&&!disabled);due=now+10000;return cached;
  }
 }
}
