// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerDs4Observer
{
 internal static async Task<int> Run(string file,string serial){
  try{
   if(serial.Length!=16||!serial.All(Uri.IsHexDigit))throw new InvalidDataException("Invalid owned endpoint challenge");
   bool whitelisted;using(var hide=new ControllerHidHide())whitelisted=hide.Read().Applications.Contains(ControllerHidHide.ImagePath(Environment.ProcessPath!),StringComparer.OrdinalIgnoreCase);
   int[] slots=ControllerNative.Slots();if(whitelisted||slots.Length!=0)throw new IOException("Independent DS4 observer still sees a physical XInput controller or is whitelisted");
   using var hid=await ControllerDs4HidReader.OpenOwned(serial);try{hid.SendOutput(1,0,1,0,0,0);await Task.Delay(180);}finally{hid.SendOutput(1,0,0,0,0,0);}
   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,ownDs4=true,slots,pid=Environment.ProcessId,whitelisted,challenge=true},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,kind=e.GetType().Name,nativeCode=(e as System.ComponentModel.Win32Exception)?.NativeErrorCode},Backend.Program.Json));return 2;}
 }
 internal static async Task Confirm(string reports,string serial){
  string image=Path.Combine(AppContext.BaseDirectory,"ONEXTweaks.ControllerObserver.exe"),file=Path.Combine(reports,"..","privateReports","controller-ds4-visibility-"+Guid.NewGuid().ToString("N")+".json");Directory.CreateDirectory(Path.GetDirectoryName(file)!);
  try{
   var start=new ProcessStartInfo(image){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--controller-ds4-visibility-probe");start.ArgumentList.Add(file);start.ArgumentList.Add(serial);using var process=Process.Start(start)??throw new IOException("Independent DS4 observer did not start");
   using var deadline=new CancellationTokenSource(10000);try{await process.WaitForExitAsync(deadline.Token);}catch(OperationCanceledException){process.Kill();throw new IOException("Independent DS4 observer timed out");}
   using var json=JsonDocument.Parse(await File.ReadAllTextAsync(file));var root=json.RootElement;if(process.ExitCode!=0||!root.GetProperty("passed").GetBoolean())throw new IOException("Independent DS4 observer did not confirm exclusive owned output");
   if(root.GetProperty("pid").GetInt32()==Environment.ProcessId||root.GetProperty("whitelisted").GetBoolean()||root.GetProperty("slots").GetArrayLength()!=0||!root.GetProperty("challenge").GetBoolean()||!root.GetProperty("ownDs4").GetBoolean())throw new IOException("Independent DS4 observer identity mismatch");
  }finally{if(File.Exists(file))File.Delete(file);}
 }
}
