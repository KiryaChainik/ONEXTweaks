// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ONEXTweaks.CompanionLauncher
{
 internal static class SupportLogExport
 {
  internal const int FileLimit=1024*1024, TotalLimit=2*1024*1024;
  private static readonly string[] Sources={"widget-bridge.json","widget-ready.json","widget-bridge-errors.log","power-writes.jsonl"};
  internal sealed class Result
  {
   internal string Path, Sha256;
   internal int SourceBytes, Files;
  }
  private sealed class Captured { internal string Name;internal byte[] Bytes; }
  internal static JavaScriptSerializer Serializer()
  {return new JavaScriptSerializer{MaxJsonLength=FileLimit,RecursionLimit=64};}
  internal static string Sha256(string path)
  {using(var hash=SHA256.Create())using(var file=File.OpenRead(path))return BitConverter.ToString(hash.ComputeHash(file)).Replace("-","");}
  internal static Result Create(string reports,string destination,string nonce)
  {
   Guid id;if(!Guid.TryParseExact(nonce,"N",out id)||id.ToString("N")!=nonce)throw new ArgumentException("Invalid export token");
   if(string.IsNullOrWhiteSpace(reports)||!Directory.Exists(reports)||(File.GetAttributes(reports)&FileAttributes.ReparsePoint)!=0)throw new IOException("Diagnostics folder unavailable");
   if(string.IsNullOrWhiteSpace(destination)||!Directory.Exists(destination))throw new IOException("Export destination unavailable");
   if(Path.GetFullPath(reports).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))throw new IOException("Distinct export destination required");
   var captured=new List<Captured>();var notes=new List<object>();int total=0;
   foreach(string name in Sources)
   {
    string path=Path.Combine(reports,name);
    try
    {
     if(!File.Exists(path)){notes.Add(new{name,state="missing"});continue;}
     if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0){notes.Add(new{name,state="link-skipped"});continue;}
     byte[] content;
     using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
     {
      if(input.Length>FileLimit){notes.Add(new{name,state="file-limit"});continue;}
      using(var bytes=new MemoryStream())
      {
       byte[] buffer=new byte[16384];int count;
       while((count=input.Read(buffer,0,Math.Min(buffer.Length,FileLimit+1-(int)bytes.Length)))>0)
       {bytes.Write(buffer,0,count);if(bytes.Length>FileLimit)break;}
       if(bytes.Length>FileLimit){notes.Add(new{name,state="file-limit"});continue;}
       content=bytes.ToArray();
      }
     }
     if(content.Length+total>TotalLimit){notes.Add(new{name,state="total-limit"});continue;}
     if(name.EndsWith(".json",StringComparison.Ordinal))
     {
      try
      {
       string text=new UTF8Encoding(false,true).GetString(content);
       if(text.Length>0&&text[0]=='\uFEFF')text=text.Substring(1);
       if(!(Serializer().DeserializeObject(text) is Dictionary<string,object>)){notes.Add(new{name,state="invalid-json"});continue;}
      }
      catch(ArgumentException){notes.Add(new{name,state="invalid-json"});continue;}
      catch(InvalidOperationException){notes.Add(new{name,state="invalid-json"});continue;}
     }
     captured.Add(new Captured{Name=name,Bytes=content});total+=content.Length;
     notes.Add(new{name,state="included",bytes=content.Length});
    }
    catch(IOException){notes.Add(new{name,state="read-unavailable"});}
    catch(UnauthorizedAccessException){notes.Add(new{name,state="read-unavailable"});}
   }
   if(captured.Count==0)throw new IOException("No readable published diagnostics");
   string stem="ONEXTweaks-logs-"+nonce;
   string pending=Path.Combine(destination,stem+".partial"),final=Path.Combine(destination,stem+".zip");bool owned=false;
   try
   {
    // Both names must be new. Replaying a nonce cannot replace an existing export.
    if(File.Exists(final))throw new IOException("Export already exists");
    using(var output=new FileStream(pending,FileMode.CreateNew,FileAccess.Write,FileShare.None))
    {
     owned=true;
     using(var archive=new ZipArchive(output,ZipArchiveMode.Create,false))
     {
      foreach(var item in captured)using(var entry=archive.CreateEntry("reports/"+item.Name,CompressionLevel.Fastest).Open())entry.Write(item.Bytes,0,item.Bytes.Length);
      using(var writer=new StreamWriter(archive.CreateEntry("manifest.json",CompressionLevel.Fastest).Open(),new UTF8Encoding(false)))
       writer.Write(Serializer().Serialize(new{format=1,createdUtc=DateTimeOffset.UtcNow.ToString("o"),product="ONEXTweaks",scope="Existing published statuses/logs only; no live capture, saved settings or memory dumps",fileLimitBytes=FileLimit,totalLimitBytes=TotalLimit,capturedSourceBytes=total,capturedFiles=captured.Count,files=notes}));
      using(var writer=new StreamWriter(archive.CreateEntry("README.txt",CompressionLevel.Fastest).Open(),new UTF8Encoding(false)))
       writer.Write("ONEXTweaks diagnostic logs\nExisting published Bridge files only, bounded by manifest.json. Snapshots are captured independently, not as one atomic hardware read. Missing/unreadable/oversized files are listed in the manifest. No memory dumps, full saved settings or live controller capture. Logs may contain local process/device details.\nONEXTweaks is based on ClawTweaks by enterTheVoidCode (Riff_Lash), AGPL-3.0-only.\n");
     }
    }
    File.Move(pending,final);owned=false;
    return new Result{Path=final,Sha256=Sha256(final),SourceBytes=total,Files=captured.Count};
   }
   finally{if(owned&&File.Exists(pending))File.Delete(pending);}
  }
 }
}
