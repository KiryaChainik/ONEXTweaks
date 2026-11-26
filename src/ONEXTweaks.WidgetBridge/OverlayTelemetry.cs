// SPDX-License-Identifier: AGPL-3.0-only
using ONEXTweaks.Backend;
using ONEXTweaks.Panel;

namespace ONEXTweaks.WidgetBridge;

/// <summary>Sampling belongs to the controller, so closing Game Bar does not freeze in-game OSD.</summary>
internal sealed class OverlayTelemetry:IAsyncDisposable
{
    private readonly CancellationTokenSource stopping=new();
    private readonly Task worker;
    private OverlaySample current=new(new(null,null,null,null,null,null),null,null,null);
    internal OverlaySample Sample=>Volatile.Read(ref current);
    internal OverlayTelemetry(Func<Task> refreshPower,Func<PowerState?> power,Func<double?> fan)
    {
        worker=Task.Run(async()=>
        {
            var windows=new WindowsSampler();using var gpu=new DeviceTelemetry();
            try
            {
                while(!stopping.IsCancellationRequested)
                {
                    try{await refreshPower();Volatile.Write(ref current,new(windows.Read(),power(),gpu.Read(),fan(),OverlayExtraSensors.ReadClocks(),OverlayExtraSensors.ReadGpuMemory()));}
                    catch(IOException){}
                    await Task.Delay(1000,stopping.Token);
                }
            }
            catch(OperationCanceledException){}
        });
    }
    public async ValueTask DisposeAsync(){stopping.Cancel();try{await worker;}catch(OperationCanceledException){}stopping.Dispose();}
}
