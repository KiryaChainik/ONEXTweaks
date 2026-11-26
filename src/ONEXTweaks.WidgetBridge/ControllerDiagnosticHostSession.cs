// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace ONEXTweaks.WidgetBridge;

// The widget owns activation. The independent foreground application owns the read
// lease. Suspending or disconnecting the widget therefore cannot starve the test.
internal sealed class ControllerDiagnosticHostSession(ControllerProfiles? controller, SemaphoreSlim profileGate, GameBarEntry entry) : IAsyncDisposable
{
    private sealed record State(bool Active=true, bool Ready=false, bool Released=false, bool Foreground=false,
        uint HostPid=0,long RenderedSequence=0, bool Captured=false,bool ReturnRequested=false,
        ControllerDiagnosticState? Displayed=null,string? Error=null,bool HostExited=false,string? ReturnStatus=null);
    private readonly object gate=new();
    private readonly CancellationTokenSource lifetime=new();
    private Guid current;
    private Task run=Task.CompletedTask;
    private State state=new(Active:false);

    internal void Start(Guid owner,bool verify,string kind="sticks")
    {
        if(kind is not("sticks" or "gyro"))throw new InvalidDataException("Unknown controller test");
        lock(gate)
        {
            if(!run.IsCompleted)
            {
                if(current==owner)return;
                throw new InvalidOperationException("Close the current controller test first");
            }
            if(controller==null)throw new InvalidOperationException("Controller module unavailable");
            if(verify&&!File.Exists(Path.Combine(WidgetPackageIdentity.LocalState,"onex-focus-verification-armed.json")))
                throw new InvalidOperationException("Controller verification is not armed");
            var pipe=WidgetPipe.Create("ONEXTweaks.ControllerDiagnostic."+owner.ToString("N"));
            bool fromGameBar=NativeGameBarState.IsInputRedirected();
            current=owner;state=new();
            run=Task.Run(()=>Serve(owner,pipe,fromGameBar,verify,kind));
        }
    }
    internal string Status(Guid owner)
    {
        lock(gate)
        {
            if(owner!=current)throw new InvalidOperationException("Controller test owner changed");
            return JsonSerializer.Serialize(state,Backend.Program.Json);
        }
    }
    private void Update(Func<State,State> change){lock(gate)state=change(state);}

    private async Task Serve(Guid owner,NamedPipeServerStream pipe,bool fromGameBar,bool verify,string kind)
    {
        string? lease=null;
        bool released=false,ready=false,explicitClose=false,restoreAllowed=fromGameBar,hadForeground=false;
        uint pid=0;long readyTick=0;bool returnRequested=false;
        nint returnWindow=0;
        ControllerDiagnosticState? sent=null;
        using var duration=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        duration.CancelAfter(TimeSpan.FromMinutes(15));
        using(pipe)
        try
        {
            // Like the existing Bluetooth host, launch the foreground application
            // only after leaving Game Bar. A visible, authenticated host is still
            // required before any physical diagnostic lease is acquired.
            if(NativeGameBarState.IsInputRedirected())
            {
                if(!fromGameBar)throw new InvalidOperationException("Game Bar ownership changed");
                await DesktopActions.DismissOverlay();
            }
            if(NativeGameBarState.IsInputRedirected())throw new InvalidOperationException("Game Bar retained input");
            nint observed=0;int stable=0;
            for(int n=0;n<25;n++)
            {
                if(NativeGameBarState.IsInputRedirected())throw new InvalidOperationException("Game Bar reacquired input before the controller test");
                nint window=DesktopActions.ForegroundHandle;
                if(window!=0&&!DesktopActions.IsGameBarForeground())
                {
                    stable=window==observed?stable+1:1;observed=window;
                    if(stable>=3){returnWindow=window;break;}
                }
                else{observed=0;stable=0;}
                await Task.Delay(20,duration.Token);
            }
            released=true;Update(s=>s with{Released=true});
            using(var launched=Process.Start(new ProcessStartInfo("onextweaks-controller-test://"+kind+"/"+owner.ToString("N")){UseShellExecute=true})){}
            using(var startup=CancellationTokenSource.CreateLinkedTokenSource(duration.Token))
            {
                startup.CancelAfter(TimeSpan.FromSeconds(15));
                await pipe.WaitForConnectionAsync(startup.Token);
            }
            pid=WidgetPipe.VerifyApplication(pipe,"ControllerDiagnosticHost");
            using var host=Process.GetProcessById((int)pid);
            _=host.Handle; // Bind the authenticated process now, before its PID can be reused.
            using var reader=new StreamReader(pipe,Encoding.UTF8,false,4096,true);
            using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};
            while(!duration.IsCancellationRequested)
            {
                using var idle=CancellationTokenSource.CreateLinkedTokenSource(duration.Token);
                idle.CancelAfter(TimeSpan.FromSeconds(7));
                string? line=await reader.ReadLineAsync(idle.Token);
                if(line==null)break;
                if(line.Length>2048)throw new InvalidDataException("Invalid controller test request");
                using var json=JsonDocument.Parse(line);
                var req=json.RootElement;
                var reply=new Dictionary<string,object?>{["RequestId"]=req.GetProperty("RequestId").GetInt32()};
                string? op=req.GetProperty("operation").GetString();
                if(op=="ready"&&released&&!ready)
                {
                    if(NativeGameBarState.IsInputRedirected())
                        throw new InvalidOperationException("Game Bar reacquired input while the controller test was launching");
                    ready=true;readyTick=Environment.TickCount64;
                    Update(s=>s with{Ready=true,HostPid=pid});reply["verify"]=verify;
                }
                else if(ready && released && (op=="read" || op=="close"))
                {
                    bool redirected=NativeGameBarState.IsInputRedirected();
                    bool foreground=!redirected&&Program.DiagnosticForegroundBelongsToClient(DesktopActions.ForegroundHandle,pid);
                    if(hadForeground&&!foreground)restoreAllowed=false;
                    if(foreground)hadForeground=true;
                    if(sent is {Available:true} displayed &&
                        (kind=="sticks"?displayed.Physical!=null&&displayed.Tuned!=null:displayed.Motion!=null&&displayed.GyroPreview!=null) &&
                        req.TryGetProperty("rendered",out var sequence) && sequence.GetInt64()==displayed.Sequence)
                    {
                        bool captured=verify&&req.TryGetProperty("captured",out var png)&&png.ValueKind==JsonValueKind.True;
                        Update(s=>s with{RenderedSequence=displayed.Sequence,Displayed=displayed,Captured=s.Captured||captured});
                    }
                    if(op=="close"){explicitClose=true;restoreAllowed&=foreground;}
                    else if(foreground)
                    {
                        if(lease==null)
                        {
                            await profileGate.WaitAsync(idle.Token);
                            try{
                                if(NativeGameBarState.IsInputRedirected() ||
                                    !Program.DiagnosticForegroundBelongsToClient(DesktopActions.ForegroundHandle,pid))
                                    throw new InvalidOperationException("Controller test lost foreground before acquiring input");
                                lease=controller!.BeginHostDiagnostic(kind);
                            }finally{profileGate.Release();}
                        }
                        sent=controller!.DiagnosticRead(lease);
                        if(!sent.Active)throw new InvalidOperationException("Controller test expired");
                        reply["sample"]=JsonSerializer.Serialize(sent,Backend.Program.Json);
                        Update(s=>s with{Foreground=true});
                    }
                    else
                    {
                        sent=null;reply["waiting"]=true;Update(s=>s with{Foreground=false});
                        if(!hadForeground&&Environment.TickCount64-readyTick>10000)
                        {
                            const string focusError="Controller test window did not acquire foreground input";
                            Update(s=>s with{Error=focusError});reply["error"]=focusError;
                        }
                        // A different newly opened overlay owns this input. Leave it in place.
                        if(redirected)reply["end"]=true;
                        if(lease!=null){controller!.EndHostSticks(lease);lease=null;}
                    }
                }
                else throw new InvalidDataException("Unexpected controller test transition");
                nint hostWindow=DesktopActions.ForegroundHandle;
                if(explicitClose)
                {
                    if(lease!=null){controller!.EndHostSticks(lease);lease=null;}
                    // Acknowledge close before opening Game Bar: exiting a still-foreground
                    // UWP host after the overlay opens can immediately dismiss that overlay.
                    restoreAllowed&=!lifetime.IsCancellationRequested&&!NativeGameBarState.IsInputRedirected()&&
                        Program.DiagnosticForegroundBelongsToClient(hostWindow,pid);
                }
                await writer.WriteLineAsync(JsonSerializer.Serialize(reply).AsMemory(),idle.Token);
                if(explicitClose&&restoreAllowed)
                {
                    var returning=new ControllerDiagnosticReturnGate(returnWindow,hostWindow);
                    var timer=Stopwatch.StartNew();string outcome="host-exit-timeout";
                    while(timer.ElapsedMilliseconds<2500&&!duration.IsCancellationRequested)
                    {
                        var action=returning.Observe(host.HasExited,NativeGameBarState.IsInputRedirected(),DesktopActions.ForegroundHandle);
                        if(action!=ControllerDiagnosticReturnAction.Wait)
                        {
                            outcome=action.ToString();
                            if(action==ControllerDiagnosticReturnAction.Open)
                            {entry.EnqueueKeyboardOpen();returnRequested=true;}
                            break;
                        }
                        await Task.Delay(20,duration.Token);
                    }
                    Update(s=>s with{HostExited=host.HasExited,ReturnStatus=outcome});
                }
                if(explicitClose||reply.ContainsKey("error")||reply.ContainsKey("end"))break;
            }
        }
        catch(Exception error){Update(s=>s with{Error=error.Message});}
        finally
        {
            if(lease!=null)controller!.EndHostSticks(lease);
            Update(s=>s with{Active=false,ReturnRequested=returnRequested});
        }
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();Task running;lock(gate)running=run;
        try{await running;}finally{lifetime.Dispose();}
    }
}
