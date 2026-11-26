// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace ONEXTweaks.WidgetBridge;

/// <summary>One explicit activation queue. Native observation never changes pages.</summary>
internal sealed class GameBarEntry : IDisposable
{
    private static string Widget => WidgetPackageIdentity.GameBarWidget;
    private readonly CancellationTokenSource stopping=new();
    private readonly TaskCompletionSource<bool> startupReady=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Channel<Request> actions=Channel.CreateUnbounded<Request>(new UnboundedChannelOptions{SingleReader=true});
    private readonly Task monitor,dispatcher,signalReader;
    private readonly EventWaitHandle? toggleSignal,openSignal,closeSignal;
    private readonly GameBarEntryRuntime runtime;
    private readonly string reports;
    private int widgetVisible,widgetReady,desiredOpen,guideHeld,pending;
    private long readyGeneration,confirmedHomeGeneration;
    private long nextId,lastPrewarm,settleUntil,lastPublishedId;
    private bool restorePending;
    private long restoreStarted;
    private readonly object warmGate=new();
    private readonly object readinessGate=new();
    private sealed record Request(long Id,bool Closing,string Source,int Index,int Presses);
    internal void SetWidgetVisible(bool visible)=>Volatile.Write(ref widgetVisible,visible?1:0);
    internal void SetWidgetReady(bool ready)
    {
        lock(readinessGate)
        {
            if(ready&&widgetReady==0)Interlocked.Increment(ref readyGeneration);
            Volatile.Write(ref widgetReady,ready?1:0);
        }
        _=Report("widget-ready.json",new{utc=DateTimeOffset.UtcNow,pid=Environment.ProcessId,ready});
    }
    internal bool BeginGuidePress(bool? inputBeforeGuide=null)
    {
        Volatile.Write(ref guideHeld,1);
        // An earlier URI can still be loading. Pressing again then must request our
        // widget, rather than queue a close based only on that unfinished intention.
        return inputBeforeGuide??runtime.IsOpen();
    }
    internal long EnqueueGuideAction(bool closing,int index,int presses)=>Enqueue(closing,"guide",index,presses);
    internal long EnqueueVerificationToggle(){bool closing=BeginGuidePress();return Enqueue(closing,"verification",0,0);}
    internal long EnqueueOemAction(bool closing,string button)=>Enqueue(closing,"oem-"+button,-1,0);
    internal long EnqueueKeyboardOpen()=>Enqueue(false,"keyboard-win-g",-1,0);
    internal long EnqueueTouchCornerOpen()=>Enqueue(false,"touch-top-left",-1,0);
    internal long EnqueueCompanionAction(bool closing)=>Enqueue(closing,"companion",-1,0);
    private long Enqueue(bool closing,string source,int index,int presses)
    {
        Volatile.Write(ref desiredOpen,closing?0:1);Volatile.Write(ref guideHeld,0);
        Interlocked.Increment(ref pending);long id=Interlocked.Increment(ref nextId);
        actions.Writer.TryWrite(new Request(id,closing,source,index,presses));return id;
    }
    internal GameBarEntry(string reportDirectory,GameBarEntryRuntime? verificationRuntime=null,bool deferStart=false)
    {
        reports=reportDirectory;
        runtime=verificationRuntime??new GameBarEntryRuntime(
            NativeGameBarState.IsInputRedirected,WidgetProcessExists,
            ()=>Launch("ms-gamebar://launch/activateMinimized/"+Widget),
            ()=>Launch("ms-gamebar://launchforeground/activate/"+Widget),DesktopActions.DismissOverlay,GamingExperience.IsActive,
            ()=>Launch("ms-gamebar://launch/activate/"+Widget),
            NativeGameBarRecovery.StopUnresponsiveHost);
        if(!deferStart)EnableStartup();
        monitor=Task.Run(MonitorAsync);dispatcher=Task.Run(DispatchAsync);
        if(verificationRuntime==null)
        {
            toggleSignal=new(false,EventResetMode.AutoReset,@"Local\ONEXTweaks.ToggleOverlay.v1");
            openSignal=new(false,EventResetMode.AutoReset,@"Local\ONEXTweaks.OpenOverlay.v1");
            closeSignal=new(false,EventResetMode.AutoReset,@"Local\ONEXTweaks.CloseOverlay.v1");
            signalReader=Task.Run(async()=>{
                await startupReady.Task.WaitAsync(stopping.Token);
                WaitHandle[] signals=[toggleSignal,openSignal,closeSignal,stopping.Token.WaitHandle];
                int index;
                while((index=WaitHandle.WaitAny(signals))!=3)
                {
                    if(index==0){bool closing=BeginGuidePress();Enqueue(closing,"local-toggle",0,0);}
                    else EnqueueCompanionAction(index==2);
                }
            });
        }
        else signalReader=Task.CompletedTask;
    }
    internal void EnableStartup()=>startupReady.TrySetResult(true);
    private async Task MonitorAsync()
    {
        bool candidate=false,selectedWhileHidden=false;long candidateAt=Environment.TickCount64,checkedAt=0,hiddenRetryAt=0;
        try
        {
            await startupReady.Task.WaitAsync(stopping.Token);
            while(!stopping.IsCancellationRequested)
            {
                try
                {
                long now=Environment.TickCount64;bool current=runtime.IsOpen();
                if(current!=candidate){candidate=current;candidateAt=now;}
                if(current||Volatile.Read(ref widgetReady)==0)selectedWhileHidden=false;
                // FSE reserves Guide for Windows. With forced Home disabled the host
                // restores its last page. Select ours after dismissal, while hidden,
                // using public launch/activate (no foreground request or key injection).
                if(!current&&!selectedWhileHidden&&now>=hiddenRetryAt&&now-candidateAt>=70&&Volatile.Read(ref guideHeld)==0&&Volatile.Read(ref pending)==0&&Volatile.Read(ref widgetReady)!=0&&runtime.IsFseActive?.Invoke()==true&&runtime.SelectWidget!=null)
                {
                    if(!runtime.IsOpen()&&Volatile.Read(ref pending)==0)
                    {
                        try
                        {
                            runtime.SelectWidget();selectedWhileHidden=true;
                            await Report("fse-hidden-selection.json",new{utc=DateTimeOffset.UtcNow,pid=Environment.ProcessId,selected=true,foregroundRequested=false,inputRedirectedAfter=runtime.IsOpen()});
                        }
                        catch(Exception e){hiddenRetryAt=now+1000;await Report("fse-hidden-selection.json",new{utc=DateTimeOffset.UtcNow,pid=Environment.ProcessId,selected=false,error=e.Message});}
                    }
                }
                if(Volatile.Read(ref guideHeld)==0&&Volatile.Read(ref pending)==0&&now>=Volatile.Read(ref settleUntil)&&now-candidateAt>=70)
                {
                    Volatile.Write(ref desiredOpen,current?1:0);
                    if(!current)SetWidgetVisible(false);
                }
                if(now-checkedAt>=1500)
                {
                    checkedAt=now;
                    bool exists=runtime.ProcessExists();
                    if(!exists)SetWidgetReady(false);
                    if((!exists||Volatile.Read(ref widgetReady)==0)&&!current&&Volatile.Read(ref pending)==0)Prewarm();
                }
                }
                catch(OperationCanceledException) when(stopping.IsCancellationRequested){throw;}
                catch(Exception e)
                {
                    await Report("gamebar-entry-observer.json",new{utc=DateTimeOffset.UtcNow,error=e.Message});
                    await Task.Delay(1000,stopping.Token);
                }
                await Task.Delay(20,stopping.Token);
            }
        }
        catch(OperationCanceledException){}
    }
    private void Prewarm()
    {
        lock(warmGate)
        {
            long now=Environment.TickCount64;if(now-lastPrewarm<1000)return;lastPrewarm=now;
            runtime.Prewarm();
        }
    }
    private static bool WidgetProcessExists()
    {
        using var bridge = Process.GetCurrentProcess();
        var processes = Process.GetProcessesByName("XboxGamingBar");
        try
        {
            foreach (var process in processes)
            {
                try { if (process.SessionId == bridge.SessionId && WidgetPipe.IsApplication(process, "App")) return true; }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
            }
            return false;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }
    internal Task PrepareWidgetAsync()=>EnsureReady();
    private async Task EnsureReady(Func<bool>? continueWaiting=null)
    {
        await startupReady.Task.WaitAsync(stopping.Token);
        if(!runtime.ProcessExists())SetWidgetReady(false);
        if(Volatile.Read(ref widgetReady)!=0)return;
        Prewarm();var timer=Stopwatch.StartNew();long retryAt=1500;
        while(Volatile.Read(ref widgetReady)==0&&timer.ElapsedMilliseconds<10000)
        {
            if(continueWaiting?.Invoke()==false)return;
            // Windows can discard an activation while the previous UWP process is terminating.
            // Retry only when no process exists; a process that is loading must finish its handshake.
            if(timer.ElapsedMilliseconds>=retryAt)
            {
                if(!runtime.ProcessExists())Prewarm();
                retryAt=timer.ElapsedMilliseconds+1500;
            }
            await Task.Delay(20,stopping.Token);
        }
        if(Volatile.Read(ref widgetReady)==0)throw new TimeoutException("Widget did not finish its background handshake");
    }
    private static void Launch(string uri)
    {using var process=Process.Start(new ProcessStartInfo(uri){UseShellExecute=true});}
    private bool IsCurrent(Request request)=>request.Id==Volatile.Read(ref nextId);
    private async Task DispatchAsync()
    {
        try
        {
            await startupReady.Task.WaitAsync(stopping.Token);
            await foreach(var request in actions.Reader.ReadAllAsync(stopping.Token))
            {
                var timer=Stopwatch.StartNew();long readyMs=0,dispatchMs=0;string? error=null;bool superseded=false,reusedRestore=false,fseActive=false;
                string activationRoute="activate-ready-widget";
                bool selectionRetried=false,nativeDismissed=false;long stableVisibilityMs=0;
                try
                {
                    if(!IsCurrent(request))superseded=true;
                    else if(request.Closing)
                    {
                        bool activationInFlight=restorePending;
                        restorePending=false;
                        // Guide itself dismisses Game Bar. Never add a second Win+G.
                        if(request.Source=="guide")superseded=!await WaitForNativeGuideClose(request);
                        else if(request.Source=="companion")
                        {
                            while(activationInFlight&&IsCurrent(request)&&(!runtime.IsOpen()||Volatile.Read(ref widgetVisible)==0)&&Environment.TickCount64-restoreStarted<2200)
                                await Task.Delay(20,stopping.Token);
                            if(IsCurrent(request)&&runtime.IsOpen()&&Volatile.Read(ref widgetVisible)!=0)await runtime.Dismiss();
                        }
                        else if(IsCurrent(request))await runtime.Dismiss();
                    }
                    else
                    {
                        bool activatedFromCold=false;
                        try{await EnsureReady(()=>IsCurrent(request));}
                        catch(TimeoutException) when(IsCurrent(request))
                        {
                            bool CanRecover()=>IsCurrent(request)&&!runtime.IsOpen()&&!runtime.ProcessExists()&&runtime.IsFseActive?.Invoke()!=true;
                            bool recovered=runtime.Recover!=null&&CanRecover()&&await runtime.Recover(CanRecover,stopping.Token);
                            if(IsCurrent(request))
                            {
                                activationRoute=recovered?"recover-gamebar-and-activate":"activate-after-background-timeout";
                                restorePending=false;
                                // Honor the explicit Open even when the background host
                                // exists but has not completed its first handshake.
                                if(runtime.IsOpen()&&runtime.SelectWidget!=null)runtime.SelectWidget();
                                else runtime.Restore();
                                restoreStarted=Environment.TickCount64;restorePending=true;
                                await EnsureReady(()=>IsCurrent(request));
                                activatedFromCold=true;
                            }
                        }
                        readyMs=timer.ElapsedMilliseconds;
                        if(!IsCurrent(request))superseded=true;
                        else
                        {
                            // Visible widgets can coexist with Home being the selected compact
                            // page. Every explicit open must activate ours, not just restore visibility.
                            if(activatedFromCold)restoreStarted=Environment.TickCount64;
                            else if(runtime.IsOpen()&&Volatile.Read(ref widgetVisible)!=0)restorePending=false;
                            fseActive=runtime.IsFseActive?.Invoke()==true;
                            long generation=Volatile.Read(ref readyGeneration);
                            bool qualifyFirstHome=request.Source=="oem-Home"&&!fseActive&&
                                generation!=0&&generation!=confirmedHomeGeneration&&runtime.SelectWidget!=null;
                            if(!activatedFromCold&&(!restorePending||request.Source=="guide"))
                            {
                                if(!runtime.IsOpen())SetWidgetVisible(false);
                                runtime.Restore();
                                restoreStarted=Environment.TickCount64;restorePending=true;
                                dispatchMs=timer.ElapsedMilliseconds-readyMs;
                            }
                            else reusedRestore=true;
                            long visibleSince=-1;
                            bool sawOverlay=false,sawVisible=false;
                            while(true)
                            {
                                if(!IsCurrent(request)){superseded=true;break;}
                                long now=Environment.TickCount64;
                                if(now-restoreStarted>=2200)throw new TimeoutException("Restored widget did not confirm foreground visibility");
                                bool gameBarOpen=runtime.IsOpen();
                                bool visible=gameBarOpen&&Volatile.Read(ref widgetVisible)!=0;
                                if(!qualifyFirstHome)
                                {
                                    if(visible){restorePending=false;break;}
                                }
                                else
                                {
                                    if(!gameBarOpen&&sawOverlay)
                                    {
                                        nativeDismissed=true;restorePending=false;break;
                                    }
                                    sawOverlay|=gameBarOpen;
                                    bool sameReady=Volatile.Read(ref widgetReady)!=0&&
                                        Volatile.Read(ref readyGeneration)==generation;
                                    if(visible&&sameReady)
                                    {
                                        sawVisible=true;
                                        if(visibleSince<0)visibleSince=now;
                                        stableVisibilityMs=now-visibleSince;
                                        if(stableVisibilityMs>=120)
                                        {
                                            confirmedHomeGeneration=generation;
                                            restorePending=false;break;
                                        }
                                    }
                                    else
                                    {
                                        visibleSince=-1;stableVisibilityMs=0;
                                        if(sawVisible&&!selectionRetried&&gameBarOpen&&sameReady&&
                                            Volatile.Read(ref guideHeld)==0&&Volatile.Read(ref desiredOpen)!=0&&
                                            IsCurrent(request)&&runtime.IsFseActive?.Invoke()!=true&&runtime.IsOpen())
                                        {
                                            // The foreground host is already open: select only our widget.
                                            // Do not send another foreground launch or extend its deadline.
                                            runtime.SelectWidget!();
                                            selectionRetried=true;activationRoute="reselect-widget-in-open-gamebar";
                                        }
                                    }
                                }
                                await Task.Delay(10,stopping.Token);
                            }
                        }
                    }
                }
                catch(OperationCanceledException) when(stopping.IsCancellationRequested){throw;}
                catch(Exception e){error=e.Message;restorePending=false;}
                finally{Volatile.Write(ref settleUntil,Environment.TickCount64+100);Interlocked.Decrement(ref pending);}
                bool? redirected=null;
                try{redirected=runtime.IsOpen();}catch(Exception failure){error??=failure.Message;}
                var result=new{utc=DateTimeOffset.UtcNow,pid=Environment.ProcessId,id=request.Id,source=request.Source,closing=request.Closing,index=request.Index,presses=request.Presses,afterRelease=true,route=superseded?"superseded":nativeDismissed?"native-dismissed-before-confirmation":request.Closing?(request.Source=="guide"?"native-guide-dismiss":"dismiss"):activationRoute,fseActive,readyMs,dispatchMs,totalMs=timer.ElapsedMilliseconds,widgetVisible=Volatile.Read(ref widgetVisible)!=0,inputRedirected=redirected,superseded,supersededBy=superseded?Volatile.Read(ref nextId):0,reusedRestore,selectionRetried,nativeDismissed,stableVisibilityMs,error};
                if(request.Id>=lastPublishedId)
                {lastPublishedId=request.Id;await Report("gamebar-entry.json",result);await Report("guide-direct-entry.json",result);}
                try{await File.AppendAllTextAsync(Path.Combine(reports,"overlay-actions.jsonl"),JsonSerializer.Serialize(result,Backend.Program.Json)+Environment.NewLine,stopping.Token);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
        catch(OperationCanceledException){}
    }
    private async Task<bool> WaitForNativeGuideClose(Request request)
    {
        var state=new GuideCloseState(Environment.TickCount64);
        while(true)
        {
            if(!IsCurrent(request))return false;
            var result=state.Observe(runtime.IsOpen(),Environment.TickCount64);
            if(result==GuideCloseResult.Closed){SetWidgetVisible(false);return true;}
            if(result==GuideCloseResult.TimedOut)throw new TimeoutException("Native Guide did not confirm a stable dismissal");
            await Task.Delay(10,stopping.Token);
        }
    }
    private async Task Report(string file,object value)
    {try{await File.WriteAllTextAsync(Path.Combine(reports,file),JsonSerializer.Serialize(value,Backend.Program.Json),stopping.Token);}catch(IOException){}catch(UnauthorizedAccessException){}catch(OperationCanceledException){}}
    public void Dispose()
    {
        stopping.Cancel();actions.Writer.TryComplete();
        try{Task.WhenAll(monitor,dispatcher,signalReader).GetAwaiter().GetResult();}catch(OperationCanceledException){}
        toggleSignal?.Dispose();openSignal?.Dispose();closeSignal?.Dispose();stopping.Dispose();
    }
}

// A verification instance uses an isolated simulated window and never subscribes to
// the real toggle event or touches Game Bar, the keyboard, controllers, or hardware.
internal sealed record GameBarEntryRuntime(Func<bool> IsOpen,Func<bool> ProcessExists,Action Prewarm,Action Restore,Func<Task> Dismiss,Func<bool>? IsFseActive=null,Action? SelectWidget=null,
    Func<Func<bool>,CancellationToken,Task<bool>>? Recover=null);
