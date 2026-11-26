// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal sealed class OemConsoleStartupGuard:IDisposable
{
    private readonly CancellationTokenSource stop=new();
    private readonly Task worker;
    private int handoffCount;
    internal int HandoffCount=>Volatile.Read(ref handoffCount);
    internal OemConsoleStartupGuard(string reports)
    {
        worker=Task.Run(async()=>
        {
            int closed=0;long start=Environment.TickCount64;
            try
            {
                // Windows can launch the stock tray later than our logon task.
                // Bound this startup handoff; it is not a permanent app watchdog.
                do
                {
                    closed+=CompanionLauncher.OemConsoleHandoff.Close();
                    Volatile.Write(ref handoffCount,closed);
                    if(closed>0)await File.WriteAllTextAsync(Path.Combine(reports,"oem-console-handoff.json"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,guiProcessesClosed=closed,serviceStopped=false,driverStopped=false},Backend.Program.Json),stop.Token);
                    await Task.Delay(2000,stop.Token);
                }while(Environment.TickCount64-start<60000);
            }
            catch(OperationCanceledException){}
            catch(Exception error){try{await File.WriteAllTextAsync(Path.Combine(reports,"oem-console-handoff.json"),JsonSerializer.Serialize(new{error=error.Message,serviceStopped=false,driverStopped=false},Backend.Program.Json));}catch(IOException){}}
        });
    }
    public void Dispose(){stop.Cancel();try{worker.GetAwaiter().GetResult();}catch(OperationCanceledException){}stop.Dispose();}
}
