// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using Microsoft.Win32;
namespace ONEXTweaks.SecureAttention
{
 internal static class Program
 {
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  [DllImport("sas.dll",ExactSpelling=true)]private static extern void SendSAS([MarshalAs(UnmanagedType.Bool)]bool asUser);
  [DllImport("kernel32.dll")]private static extern uint WTSGetActiveConsoleSessionId();
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
  [DllImport("kernel32.dll",CharSet=CharSet.Ansi,ExactSpelling=true)]private static extern IntPtr GetProcAddress(IntPtr library,string name);
  [DllImport("kernel32.dll")]private static extern bool FreeLibrary(IntPtr library);
  private static bool Api(){IntPtr library=LoadLibraryEx(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"sas.dll"),IntPtr.Zero,0x800);try{return library!=IntPtr.Zero&&GetProcAddress(library,"SendSAS")!=IntPtr.Zero;}finally{if(library!=IntPtr.Zero)FreeLibrary(library);}}
  private static bool Allowed(){using(var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System")){object value=key==null?null:key.GetValue("SoftwareSASGeneration");return value is int&&(((int)value&1)!=0);}}
  private static int Main(string[] args)
  {
   // Direct/task invocations cannot send SAS. SCM must launch the fixed service.
   if(args.Length!=1||(args[0]!="--service"&&args[0]!="--probe-service"))return 5;
   if(!WindowsIdentity.GetCurrent().IsSystem)return 5;
   bool probe=args[0]=="--probe-service";ServiceBase.Run(new AttentionService(probe));return 0;
  }
  private sealed class AttentionService:ServiceBase
  {
   private readonly bool probe;
   internal AttentionService(bool probe){this.probe=probe;ServiceName=probe?"ONEXTweaks-Keyboard298-SasProbe":"ONEXTweaks-SecureAttention";AutoLog=false;CanStop=true;}
   protected override void OnStart(string[] args)
   {
    if(args.Length!=0)throw new InvalidOperationException("Arguments are not accepted");
    ThreadPool.QueueUserWorkItem(delegate{
     try{
      Thread.Sleep(200);bool system=WindowsIdentity.GetCurrent().IsSystem,api=Api(),console=WTSGetActiveConsoleSessionId()!=uint.MaxValue;
      if(probe){string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"ONEXTweaks","SecureAttention");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"probe.json"),"{\"system\":"+system.ToString().ToLowerInvariant()+",\"api\":"+api.ToString().ToLowerInvariant()+",\"service\":true,\"servicesAllowed\":"+Allowed().ToString().ToLowerInvariant()+",\"consoleAvailable\":"+console.ToString().ToLowerInvariant()+",\"secureAttentionSent\":false}");if(!system||!api||!console)ExitCode=5;}
      else if(system&&api&&console&&Allowed())SendSAS(false);else ExitCode=5;
     }catch{ExitCode=6;}finally{Stop();}
    });
   }
  }
 }
}
