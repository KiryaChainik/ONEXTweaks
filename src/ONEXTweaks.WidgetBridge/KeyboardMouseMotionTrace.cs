// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ONEXTweaks.WidgetBridge;

/// <summary>Owns one pending, recording or saving request across controller worker restarts.</summary>
internal sealed class KeyboardMouseMotionRecorder : IAsyncDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private KeyboardMouseMotionTrace? current;
    private int segment;
    internal KeyboardMouseMotionTrace? Current => Volatile.Read(ref current);
    internal int NewSegment() => Interlocked.Increment(ref segment);

    internal KeyboardMouseMotionRecorder(string reports, TimeSpan? duration = null)
    {
        string directory = Path.Combine(reports, "..", "privateReports");
        worker = Task.Run(() => Watch(directory, duration ?? TimeSpan.FromSeconds(22)));
    }

    private async Task Watch(string directory, TimeSpan duration)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var trace = Current;
                if (trace == null)
                    Volatile.Write(ref current, KeyboardMouseMotionTrace.Claim(directory, duration));
                else
                {
                    trace.WriteRecordingMarker();
                    if (trace.TryFinish(shutdown: false))
                    {
                        await trace.Save();
                        Volatile.Write(ref current, null);
                    }
                }
                await Task.Delay(250, stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally
        {
            var trace = Interlocked.Exchange(ref current, null);
            if (trace != null && trace.TryFinish(shutdown: true)) await trace.Save();
        }
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        try { await worker; } finally { stop.Dispose(); }
    }
}

/// <summary>A short, single-use trace of the axes and continuous mouse output.</summary>
internal sealed class KeyboardMouseMotionTrace
{
    private static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(5);
    private readonly InputSample[] input = new InputSample[8192];
    private readonly SourceSample[] source = new SourceSample[8192];
    private readonly OutputSample[] output = new OutputSample[32768];
    private readonly string receipt;
    private readonly DateTimeOffset armedUtc;
    private readonly long durationTicks, expires;
    private readonly object lifecycle = new();
    private KeyboardMousePolicy? policy;
    private long start, end;
    private bool finished, markerWritten;
    private string stopReason = "";
    private int inputCount, sourceCount, outputCount, emittedCount;
    private uint firstSender, sender;
    private int senderSegment;
    private bool inputFull, sourceFull, outputFull;

    private readonly record struct InputSample(int Segment, uint ThreadId,long LoopStarted,long DesktopCheckStarted,
        long ReadStarted,long Sampled,long GameBarCheckStarted,long GameBarCheckCompleted,long Published,uint Packet,
        short Lx,short Ly,short Rx,short Ry,bool Paused,int Revision,double TranslatorSeconds,
        short CursorX,short CursorY,double CursorScale,double WheelX,double WheelY,bool FastWheel);
    private readonly record struct OutputSample(int Segment, uint ThreadId,string Kind,long Sampled,long Started,long Completed,int X,int Y,long Deadline);
    private readonly record struct SourceSample(long Published,long CaptureCompleted,long EmitCompleted,
        long WaitStarted,long WaitCompleted,long Deadline,bool WaitSucceeded);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    internal bool IsRecording => Volatile.Read(ref start) != 0 && !Volatile.Read(ref finished);

    private KeyboardMouseMotionTrace(string receipt, DateTimeOffset armedUtc, TimeSpan duration)
    {
        this.receipt = receipt;
        this.armedUtc = armedUtc;
        durationTicks = (long)(duration.TotalSeconds * Stopwatch.Frequency);
        expires = Stopwatch.GetTimestamp() + (long)((armedUtc + RequestLifetime - DateTimeOffset.UtcNow).TotalSeconds * Stopwatch.Frequency);
    }

    internal static KeyboardMouseMotionTrace? Claim(string directory, TimeSpan duration)
    {
        string request = Path.Combine(directory, "keyboard-mouse-motion-trace.request");
        try
        {
            var info = new FileInfo(request);
            if (!info.Exists || info.Length != 32) return null;
            DateTimeOffset armed = info.LastWriteTimeUtc;
            var age = DateTimeOffset.UtcNow - armed;
            if (age < TimeSpan.Zero || age > RequestLifetime) return null;
            if (!Guid.TryParseExact(File.ReadAllText(request), "N", out var id)) return null;
            string receipt = Path.Combine(directory, $"keyboard-mouse-motion-trace-{id:N}.json");
            if (File.Exists(receipt)) return null;
            File.Move(request, receipt + ".claimed");
            return new KeyboardMouseMotionTrace(receipt, armed, duration);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    internal void ObserveIntent(ControllerOutput intent, KeyboardMousePolicy currentPolicy, long sample)
    {
        if (Volatile.Read(ref start) != 0 || !intent.DesktopCursor || !(intent.ContinuousCursor || intent.VerticalWheelRate != 0 || intent.HorizontalWheelRate != 0)) return;
        lock (lifecycle)
        {
            if (finished || start != 0 || sample >= expires) return;
            policy = currentPolicy;
            end = sample + durationTicks;
            Volatile.Write(ref start, sample);
        }
    }

    // Called only by the sender. The runtime joins each old sender before replacing it.
    internal void IdentifySender(int segment, uint threadId)
    {
        senderSegment = segment;
        sender = threadId;
        if (firstSender == 0) firstSender = threadId;
    }

    // Owned by the single diagnostics worker. The controller and sender never access marker files.
    internal void WriteRecordingMarker()
    {
        long first = Volatile.Read(ref start);
        if (first == 0 || markerWritten) return;
        try
        {
            string marker = receipt + ".recording";
            File.WriteAllText(marker + ".tmp", first.ToString(System.Globalization.CultureInfo.InvariantCulture));
            File.Move(marker + ".tmp", marker);
            markerWritten = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    internal bool TryFinish(bool shutdown)
    {
        lock (lifecycle)
        {
            if (finished) return false;
            long now = Stopwatch.GetTimestamp();
            if (!shutdown && now < (start == 0 ? expires : end)) return false;
            stopReason = shutdown ? "shutdown" : start == 0 ? "expired" : "deadline";
            Volatile.Write(ref finished, true);
            return true;
        }
    }

    internal void Sample(int segment,long loopStarted,long desktopCheckStarted,long readStarted,long sampled,
        long gameBarCheckStarted,long gameBarCheckCompleted,long published,uint packet,ControllerFrame frame,
        ControllerOutput intent,bool paused,int revision,double translatorSeconds)
    {
        if(!IsRecording || sampled > end)return;
        int index=inputCount;
        if(index==input.Length){inputFull=true;return;}
        input[index]=new(segment,GetCurrentThreadId(),loopStarted,desktopCheckStarted,readStarted,sampled,
            gameBarCheckStarted,gameBarCheckCompleted,published,packet,frame.Lx,frame.Ly,frame.Rx,frame.Ry,paused,revision,translatorSeconds,
            intent.CursorX,intent.CursorY,intent.CursorSpeedScale,intent.HorizontalWheelRate,intent.VerticalWheelRate,
            intent.FastDesktopScroll);
        Volatile.Write(ref inputCount,index+1);
    }

    internal long BeginOutput()
    {
        if(!IsRecording)return 0;
        long now=Stopwatch.GetTimestamp();
        return now<=end ? now : 0;
    }

    internal void SourcePhases(long published,long captureCompleted,long emitCompleted,long waitStarted,
        long waitCompleted,long deadline,bool waitSucceeded)
    {
        if(!IsRecording || published > end)return;
        int index=sourceCount;
        if(index==source.Length){sourceFull=true;return;}
        source[index]=new(published,captureCompleted,emitCompleted,waitStarted,
            waitCompleted,deadline,waitSucceeded);
        Volatile.Write(ref sourceCount,index+1);
    }

    internal void Emitted(string kind,long sampled,long sendStarted,int x,int y)
    {
        if(sendStarted==0)return;
        Complete(kind,sampled,sendStarted,Stopwatch.GetTimestamp(),x,y);
    }

    internal void Guard(long began,long desktopChecked,bool desktopAvailable,bool redirected)
    {
        if(began==0)return;
        Complete(desktopAvailable?"desktop-guard":"desktop-blocked",0,began,desktopChecked,0,0);
        if(desktopAvailable)
            Complete(redirected?"gamebar-blocked":"gamebar-guard",0,desktopChecked,Stopwatch.GetTimestamp(),0,0);
    }

    internal void Timer(long began,long deadline)
    {
        if(began!=0)Complete("timer",0,began,Stopwatch.GetTimestamp(),0,0,deadline);
    }

    private void Complete(string kind,long sampled,long began,long completed,int x,int y,long deadline=0)
    {
        if (!IsRecording || began > end) return;
        int index=outputCount;
        if(index==output.Length){outputFull=true;return;}
        output[index]=new(senderSegment,sender,kind,sampled,began,completed,x,y,deadline);
        if(kind is "cursor" or "wheel") emittedCount++;
        Volatile.Write(ref outputCount,index+1);
    }

    internal async Task Save()
    {
        int inputs = Volatile.Read(ref inputCount), phases = Volatile.Read(ref sourceCount), outputs = Volatile.Read(ref outputCount);
        string status = start == 0 ? "no-motion" :
            stopReason == "deadline" && inputs > 0 && emittedCount > 0 && !inputFull && !sourceFull && !outputFull ? "complete" : "partial";
        try
        {
            // Writers append only. Their published prefixes stay immutable while JSON streams to disk.
            await using (var stream = new FileStream(receipt + ".tmp", FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, new
                {
                    status, stopReason, armedUtc, start, end, completedUtc = DateTimeOffset.UtcNow, frequency = Stopwatch.Frequency,
                    inputFull, sourceFull, outputFull, pid = Environment.ProcessId, senderThreadId = firstSender,
                    bridge = typeof(KeyboardMouseMotionTrace).Assembly.GetName().Version,
                    policy = policy == null ? null : new { policy.MouseSpeed, policy.MouseAcceleration, policy.HoldAcceleration,
                        policy.ScrollSpeed, policy.AdaptiveScroll, policy.StickDeadzone },
                    axes = input.AsMemory(0, inputs), sourcePhases = source.AsMemory(0, phases), mouse = output.AsMemory(0, outputs)
                }, Backend.Program.Json);
            }
            File.Move(receipt + ".tmp", receipt);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Never disturb controller ownership. Give the retriever a terminal error when the directory is writable.
            try { await File.WriteAllTextAsync(receipt + ".failed", error.GetType().Name); }
            catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException) { }
        }
    }
}
