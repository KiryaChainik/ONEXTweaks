// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ONEXTweaks.CompanionLauncher
{
 internal static class SupportLogActivation
 {
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]
  private static extern int GetCurrentPackageFamilyName(ref int length,StringBuilder family);
  internal static bool TryNonce(string[] args,out string nonce)
  {
   nonce=null;int index=Array.IndexOf(args,"--export-logs");Guid parsed;
   if(index<0||index+1>=args.Length||Array.IndexOf(args,"--restart-widget")>=0||Array.IndexOf(args,"--driver-manager")>=0||!Guid.TryParseExact(args[index+1],"N",out parsed))return false;
   nonce=parsed.ToString("N");return nonce==args[index+1];
  }
  private static void Publish(string path,object value)
  {
   string temporary=path+".partial";bool owned=false;
   try
   {
    using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
    {owned=true;using(var text=new StreamWriter(file,new UTF8Encoding(false)))text.Write(SupportLogExport.Serializer().Serialize(value));}
    File.Move(temporary,path);owned=false;
   }
   finally{if(owned&&File.Exists(temporary))File.Delete(temporary);}
  }
  internal static int Run(string[] args)
  {
   string nonce;if(!TryNonce(args,out nonce))return 1;
   string request=null,claim=null,reply=null,body=null;FileStream ownedClaim=null;bool completed=false,mutexOwned=false,canReply=false;
   using(var mutex=new Mutex(false,@"Local\ONEXTweaks.SupportLogExport.v1"))
   {
    try
    {
     int length=0;GetCurrentPackageFamilyName(ref length,null);if(length<2)return 1;
     var family=new StringBuilder(length);if(GetCurrentPackageFamilyName(ref length,family)!=0)return 1;
     string local=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Packages",family.ToString(),"LocalState","Reports");
     if(!Directory.Exists(local)||(File.GetAttributes(local)&FileAttributes.ReparsePoint)!=0)return 1;
     string stem="support-export-"+nonce;request=Path.Combine(local,stem+".request");claim=Path.Combine(local,stem+".claim");reply=Path.Combine(local,stem+".json");
     if(!File.Exists(request)||(File.GetAttributes(request)&FileAttributes.ReparsePoint)!=0||new FileInfo(request).Length>512)return 1;
     if(File.Exists(reply)||File.Exists(claim))return 1;
     body=File.ReadAllText(request);string[] lines=body.Split(new[]{'\n'},StringSplitOptions.None);int pid;long ticks;
     canReply=true;
     if(lines.Length!=3||!int.TryParse(lines[0],out pid)||!long.TryParse(lines[1],out ticks)||lines[2]!=nonce)throw new InvalidDataException("Invalid export request");
     double age=(DateTime.UtcNow-new DateTime(ticks,DateTimeKind.Utc)).TotalSeconds;if(age<0||age>30)throw new InvalidDataException("Expired export request");
     string package=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."))+Path.DirectorySeparatorChar;
     using(var caller=Process.GetProcessById(pid))if(caller.HasExited||!caller.MainModule.FileName.StartsWith(package,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid export caller");
     ownedClaim=new FileStream(claim,FileMode.CreateNew,FileAccess.Write,FileShare.None);
     byte[] identity=Encoding.UTF8.GetBytes(nonce+"\n"+Process.GetCurrentProcess().Id);ownedClaim.Write(identity,0,identity.Length);ownedClaim.Flush();
     try{mutexOwned=mutex.WaitOne(0);}catch(AbandonedMutexException){mutexOwned=true;}
     if(!mutexOwned){Publish(reply,new{nonce,success=false,pending=true,error="busy"});completed=true;return 0;}
     string reports=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ONEXTweaks","dev","Reports");
     var result=SupportLogExport.Create(reports,Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),nonce);
     Publish(reply,new{nonce,success=true,pending=false,archiveFileName=Path.GetFileName(result.Path),archiveSha256=result.Sha256,sourceBytes=result.SourceBytes,capturedFiles=result.Files,helperVersion=typeof(SupportLogActivation).Assembly.GetName().Version.ToString(),mode="support-export",normalBootstrapEntered=false});
     completed=true;return 0;
    }
    catch(Exception error)
    {
     // Never fall through to normal companion/bootstrap on any export failure.
     if(canReply&&reply!=null)try{Publish(reply,new{nonce,success=false,pending=false,error=error.GetType().Name});completed=true;}catch{}
     return 1;
    }
    finally
    {
     if(ownedClaim!=null)ownedClaim.Dispose();
     if(completed)
     {
      try{if(request!=null&&File.Exists(request)&&File.ReadAllText(request)==body)File.Delete(request);}catch{}
      try{if(ownedClaim!=null&&claim!=null&&File.Exists(claim)&&File.ReadAllText(claim).StartsWith(nonce+"\n",StringComparison.Ordinal))File.Delete(claim);}catch{}
     }
     // Unconfirmed per-nonce data remains available for diagnosis; it cannot
     // block future nonces. No process is killed and no unrelated data is swept.
     if(mutexOwned)mutex.ReleaseMutex();
    }
   }
  }
 }
}
