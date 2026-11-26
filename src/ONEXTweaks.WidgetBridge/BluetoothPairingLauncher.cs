// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Text.Json;

namespace ONEXTweaks.WidgetBridge;

internal static class BluetoothPairingLauncher
{
    internal sealed record Request(string Token,string Folder,bool Probe);
    private static readonly object gate=new();
    private static string? activeToken;
    internal static Request Reserve(string token)
    {
        if(!Guid.TryParseExact(token,"N",out _))throw new ArgumentException("Invalid Bluetooth pairing request");
        string local=WidgetPackageIdentity.LocalState;
        string folder=Path.Combine(local,"onex-bluetooth-pairing"),path=Path.Combine(folder,token+".request.json");
        if(!File.Exists(path)||new FileInfo(path).Length>16384)throw new ArgumentException("Bluetooth pairing request is missing or invalid");
        using var document=JsonDocument.Parse(File.ReadAllText(path));var data=document.RootElement;
        if(data.GetProperty("schema").GetInt32()!=1||data.GetProperty("token").GetString()!=token||!DateTimeOffset.TryParse(data.GetProperty("createdUtc").GetString(),out var created)||DateTimeOffset.UtcNow-created>TimeSpan.FromSeconds(45)||created>DateTimeOffset.UtcNow.AddSeconds(5))throw new ArgumentException("Bluetooth pairing request expired");
        string mode=data.GetProperty("mode").GetString()??"";bool probe=mode=="probe";
        if(mode is not ("pair" or "probe")||probe&&!File.Exists(Path.Combine(local,"onex-focus-verification-armed.json")))throw new ArgumentException("Bluetooth pairing mode not permitted");
        string deviceId=data.GetProperty("deviceId").GetString()??"";if(!probe&&(string.IsNullOrWhiteSpace(deviceId)||deviceId.Length>4096))throw new ArgumentException("Invalid Bluetooth pairing endpoint");
        lock(gate){if(activeToken!=null)throw new InvalidOperationException("Предыдущее сопряжение ещё завершается. Попробуй через несколько секунд.");activeToken=token;}
        return new(token,folder,probe);
    }
    internal static async Task RunAsync(Request request,GameBarEntry entry)
    {
        string prefix=Path.Combine(request.Folder,request.Token),status="Starting";bool suppressed=false,final=false;
        try
        {
            await DesktopActions.DismissOverlay();
            using(var launched=Process.Start(new ProcessStartInfo("onextweaks-bluetooth://pair/"+request.Token){UseShellExecute=true})){}
            var deadline=DateTimeOffset.UtcNow.AddSeconds(90);
            while(DateTimeOffset.UtcNow<deadline)
            {
                try
                {
                    if(File.Exists(prefix+".cancel.json")){using var cancel=JsonDocument.Parse(File.ReadAllText(prefix+".cancel.json"));suppressed|=cancel.RootElement.TryGetProperty("suppressReturn",out var flag)&&flag.ValueKind==JsonValueKind.True;}
                    if(File.Exists(prefix+".result.json"))
                    {
                        using var result=JsonDocument.Parse(File.ReadAllText(prefix+".result.json"));var data=result.RootElement;
                        if(data.GetProperty("token").GetString()==request.Token&&data.GetProperty("isFinal").GetBoolean())
                        {
                            status=data.GetProperty("status").GetString()??"Unknown";suppressed|=data.TryGetProperty("suppressReturn",out var flag)&&flag.ValueKind==JsonValueKind.True;final=true;break;
                        }
                    }
                }
                catch(IOException){}catch(JsonException){}
                await Task.Delay(200);
            }
            if(!final)
            {
                if(!File.Exists(prefix+".cancel.json"))await File.WriteAllTextAsync(prefix+".cancel.json","{\"suppressReturn\":false}");
                throw new TimeoutException("Истекло время ожидания окна сопряжения Bluetooth");
            }
        }
        catch(Exception error)
        {
            status="LaunchError";
            await File.WriteAllTextAsync(prefix+".result.json",JsonSerializer.Serialize(new{schema=1,token=request.Token,applicationId="launcher",processId=Environment.ProcessId,status,isFinal=true,paired=false,error=error.Message,suppressReturn=suppressed,utc=DateTimeOffset.UtcNow},Backend.Program.Json));
        }
        finally
        {
            if(!suppressed)entry.EnqueueKeyboardOpen();
            if(final)foreach(string suffix in new[]{".request.json",".running.json",".cancel.json"})try{File.Delete(prefix+suffix);}catch(IOException){}catch(UnauthorizedAccessException){}
            lock(gate){if(activeToken==request.Token)activeToken=null;}
        }
    }
}
