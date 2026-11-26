// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json.Nodes;
using ONEXTweaks.Panel;

namespace ONEXTweaks.WidgetBridge;

/// <summary>The privileged fan lease belongs to this controller, not the visibility of the UWP.
/// Closing Game Bar must leave the selected fan profile active while playing.</summary>
internal sealed class HomeControlHeartbeat : IAsyncDisposable
{
    private readonly CancellationTokenSource stopping = new();
    private readonly Task worker;
    internal HomeControlHeartbeat(PowerChannel power, SemaphoreSlim gate, Action<JsonObject?> update, Action<Exception> log)
    {
        if (!power.HardwareConfigured)
        {
            worker = Task.CompletedTask;
            return;
        }
        worker = Task.Run(async () =>
        {
            try
            {
                while (!stopping.IsCancellationRequested)
                {
                    await gate.WaitAsync(stopping.Token);
                    try
                    {
                        var reply = await power.ReadHomeAsync();
                        update(reply.Ok ? reply.Home : null);
                    }
                    catch (Exception e) { update(null); log(e); }
                    finally { gate.Release(); }
                    await Task.Delay(2000, stopping.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
    }
    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        try { await worker; } catch (OperationCanceledException) { }
        stopping.Dispose();
    }
}
