// SPDX-License-Identifier: AGPL-3.0-only
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace ONEXTweaks.WidgetBridge;
internal static class KeyboardMouseWindowsSecurity
{
 internal const string ServiceName="ONEXTweaks-SecureAttention";
 private static string Image=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"ONEXTweaks","SecureAttention","ONEXTweaks.SecureAttention.exe");
 [StructLayout(LayoutKind.Sequential)]private struct Status{internal uint Type,State,Controls,Win32Exit,ServiceExit,Checkpoint,Wait,Pid,Flags;}
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint OpenSCManagerW(string? machine,string? database,uint access);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint OpenServiceW(nint scm,string name,uint access);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool StartServiceW(nint service,int count,nint arguments);
 [DllImport("advapi32.dll",SetLastError=true)]private static extern bool QueryServiceStatusEx(nint service,int info,out Status status,int length,out int required);
 [DllImport("advapi32.dll")]private static extern bool CloseServiceHandle(nint service);
 internal static bool Available()
 {
  try{using var policy=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");using var own=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\ONEXTweaks\SecureAttention");using var service=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+ServiceName);return own?.GetValue("Ready") is int ready&&ready==1&&policy?.GetValue("SoftwareSASGeneration") is int mode&&(mode&1)!=0&&File.Exists(Image)&&service?.GetValue("ImagePath") is string command&&command.Equals("\""+Image+"\" --service",StringComparison.OrdinalIgnoreCase)&&service.GetValue("ObjectName") is string account&&account.Equals("LocalSystem",StringComparison.OrdinalIgnoreCase);}
  catch(System.Security.SecurityException){return false;}
 }
 internal static async Task Show()
 {
  if(!Available())throw new IOException("Ctrl+Alt+Delete: the protected Windows service is unavailable or not allowed by Windows policy.");
  nint scm=OpenSCManagerW(null,null,1);if(scm==0)throw new Win32Exception(Marshal.GetLastWin32Error());nint handle=0;
  try{handle=OpenServiceW(scm,ServiceName,0x14);if(handle==0)throw new Win32Exception(Marshal.GetLastWin32Error());if(!StartServiceW(handle,0,0)&&Marshal.GetLastWin32Error()!=1056)throw new Win32Exception(Marshal.GetLastWin32Error());for(int n=0;n<160;n++){await Task.Delay(50);if(!QueryServiceStatusEx(handle,0,out var status,Marshal.SizeOf<Status>(),out _))throw new Win32Exception(Marshal.GetLastWin32Error());if(status.State==1){if(status.Win32Exit!=0)throw new IOException("Windows secure-attention service returned an error");return;}}throw new IOException("Windows secure-attention service timed out");}
  finally{if(handle!=0)CloseServiceHandle(handle);CloseServiceHandle(scm);}
 }
}
