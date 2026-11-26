// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.Foundation.Metadata;
using Windows.Storage;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private static readonly SemaphoreSlim SupportExportGate=new SemaphoreSlim(1,1);
  private int _onexSupportExportStarts;
  private JsonObject _onexSupportExportLastResult;
  private async Task<ValueSet> ExportOnexSupportLogsAsync()
  {
   // Keep the older platform route explicit. Never launch the normal companion
   // without the export arguments when this optional desktop API is unavailable.
   if(!ApiInformation.IsMethodPresent("Windows.ApplicationModel.FullTrustProcessLauncher","LaunchFullTrustProcessForCurrentAppWithArgumentsAsync"))
    return App.IsConnected?await App.SendMessageAsync(new ValueSet{{"ExportLogs",true}},60000):new ValueSet{{"Error","Log export is unavailable on this platform."}};
   if(!SupportExportGate.Wait(0))return new ValueSet{{"Pending",true}};
   string nonce=Guid.NewGuid().ToString("N"),body=null;StorageFolder reports=null;bool launched=false,received=false;
   _onexSupportExportLastResult=null;
   try
   {
    reports=await ApplicationData.Current.LocalFolder.CreateFolderAsync("Reports",CreationCollisionOption.OpenIfExists);
    var request=await reports.CreateFileAsync("support-export-"+nonce+".request",CreationCollisionOption.FailIfExists);
    body=Windows.System.Diagnostics.ProcessDiagnosticInfo.GetForCurrentProcess().ProcessId+"\n"+DateTime.UtcNow.Ticks+"\n"+nonce;
    await FileIO.WriteTextAsync(request,body);
    _onexSupportExportStarts++;
    var activation=await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppWithArgumentsAsync("--export-logs "+nonce);
    if(activation.LaunchResult!=FullTrustLaunchResult.Success)throw new InvalidOperationException("Support export activation failed.");
    launched=true;
    for(int i=0;i<250;i++)
    {
     var file=await reports.TryGetItemAsync("support-export-"+nonce+".json") as StorageFile;
     if(file!=null)
     {
      JsonObject reply;
      if(!JsonObject.TryParse(await FileIO.ReadTextAsync(file),out reply)||reply.GetNamedString("nonce","")!=nonce)throw new InvalidOperationException("Invalid support export reply.");
      received=true;_onexSupportExportLastResult=reply;
      if(reply.GetNamedBoolean("pending",false))return new ValueSet{{"Pending",true}};
      if(!reply.GetNamedBoolean("success",false))return new ValueSet{{"Error",reply.GetNamedString("error","Export failed")}};
      if(reply.GetNamedString("archiveFileName","")!="ONEXTweaks-logs-"+nonce+".zip"||reply.GetNamedString("archiveSha256","").Length!=64||reply.GetNamedNumber("sourceBytes",-1)<0||reply.GetNamedNumber("sourceBytes",-1)>2097152||reply.GetNamedNumber("capturedFiles",0)<1||reply.GetNamedNumber("capturedFiles",0)>4||reply.GetNamedString("mode","")!="support-export")throw new InvalidOperationException("Invalid support export receipt.");
      return new ValueSet{{"Success",true},{"Path","Desktop"}};
     }
     await Task.Delay(100);
    }
    _onexSupportExportLastResult=new JsonObject{{"nonce",JsonValue.CreateStringValue(nonce)},{"success",JsonValue.CreateBooleanValue(false)},{"pending",JsonValue.CreateBooleanValue(true)},{"error",JsonValue.CreateStringValue("timeout")}};
    return new ValueSet{{"Pending",true}};
   }
   catch(Exception error)
   {
    _onexSupportExportLastResult=new JsonObject{{"nonce",JsonValue.CreateStringValue(nonce)},{"success",JsonValue.CreateBooleanValue(false)},{"error",JsonValue.CreateStringValue(error.GetType().Name)}};
    return new ValueSet{{"Error","Support export failed."}};
   }
   finally
   {
    // Completed replies prove this exact job is known. A timeout never kills the
    // helper or sweeps a claim that might still be active. Other nonces stay free.
    if(reports!=null&&(received||!launched))
    {
     try
     {
      var claim=await reports.TryGetItemAsync("support-export-"+nonce+".claim");
      var request=await reports.TryGetItemAsync("support-export-"+nonce+".request") as StorageFile;
      if(request!=null&&(received||claim==null)&&await FileIO.ReadTextAsync(request)==body)await request.DeleteAsync();
     }catch{}
     if(received)try{var reply=await reports.TryGetItemAsync("support-export-"+nonce+".json") as StorageFile;if(reply!=null&&JsonObject.Parse(await FileIO.ReadTextAsync(reply)).GetNamedString("nonce","")==nonce)await reply.DeleteAsync();}catch{}
    }
    SupportExportGate.Release();
   }
  }
 }
}
