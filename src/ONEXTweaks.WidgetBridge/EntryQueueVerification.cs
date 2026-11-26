// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class EntryQueueVerification
{
    internal static async Task<int> Run(string output)
    {
        string folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"entry-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        int open=0,restores=0,dismisses=0,hostGeneration=0;GameBarEntry? owner=null;var delayed=new List<Task>();var checks=new List<string>();
        void SetOpen(bool value){Volatile.Write(ref open,value?1:0);owner!.SetWidgetVisible(value);}
        var runtime=new GameBarEntryRuntime(()=>Volatile.Read(ref open)!=0,()=>true,()=>{},()=>
        {
            Interlocked.Increment(ref restores);int generation=Volatile.Read(ref hostGeneration);
            lock(delayed)delayed.Add(Task.Run(async()=>{await Task.Delay(140);if(generation==Volatile.Read(ref hostGeneration))SetOpen(true);}));
        },()=>{Interlocked.Increment(ref dismisses);SetOpen(false);return Task.CompletedTask;});
        object? result=null;
        try
        {
            int startupCalls=0;
            var startupRuntime=new GameBarEntryRuntime(()=>{Interlocked.Increment(ref startupCalls);return false;},()=>false,()=>Interlocked.Increment(ref startupCalls),()=>{},()=>Task.CompletedTask);
            using(var deferred=new GameBarEntry(folder,startupRuntime,deferStart:true))
            {
                await Task.Delay(100);
                if(Volatile.Read(ref startupCalls)!=0)throw new InvalidOperationException("Game Bar accessed before native Guide policy ownership");
                deferred.EnableStartup();
                for(int n=0;n<50&&Volatile.Read(ref startupCalls)==0;n++)await Task.Delay(10);
                if(Volatile.Read(ref startupCalls)==0)throw new InvalidOperationException("Game Bar did not start after policy ownership");
                checks.Add("No Game Bar access or prewarm before explicit native Guide policy ownership");
            }
            using var entry=new GameBarEntry(folder,runtime);owner=entry;entry.SetWidgetReady(true);
            bool Press()
            {
                bool before=Volatile.Read(ref open)!=0;Interlocked.Increment(ref hostGeneration);
                if(before)SetOpen(false); // Windows may dismiss before our XInput poll.
                return entry.BeginGuidePress(before);
            }
            async Task<JsonElement> Wait(long id)
            {
                var timer=Stopwatch.StartNew();while(timer.ElapsedMilliseconds<1800)
                {
                    try{using var stream=new FileStream(Path.Combine(folder,"gamebar-entry.json"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var reader=new StreamReader(stream);using var doc=JsonDocument.Parse(await reader.ReadToEndAsync());var r=doc.RootElement;if(r.GetProperty("id").GetInt64()==id){if(r.GetProperty("error").ValueKind!=JsonValueKind.Null)throw new InvalidOperationException(r.GetProperty("error").GetString());return r.Clone();}}
                    catch(IOException){}catch(JsonException){}await Task.Delay(5);
                }
                throw new TimeoutException("Dispatcher did not complete the current request promptly");
            }
            async Task Settle(){Task[] copy;lock(delayed)copy=delayed.ToArray();await Task.WhenAll(copy);await Task.Delay(35);}
            void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);checks.Add(message);}
            Require(WindowsDisplayRotation.RelativeOrientation(1, 3, 3) == 1 &&
                WindowsDisplayRotation.RelativeOrientation(1, 3, 0) == 0 &&
                WindowsDisplayRotation.RelativeOrientation(1, 3, 2) == 2 &&
                WindowsDisplayRotation.RelativeOrientation(1, 3, 1) == 3,
                "Display counter-rotates through both portrait directions and returns to its landscape reference");
            Require(WindowsDisplayRotation.RelativeOrientation(0, 2, 2) == 0 &&
                WindowsDisplayRotation.RelativeOrientation(0, 2, 3) == 3 &&
                WindowsDisplayRotation.RelativeOrientation(0, 2, 1) == 1,
                "Rotation uses the same direction when unlocked in portrait");
            int? reference = WindowsDisplayRotation.KeepOrCaptureReference(null, true, 1, false, 0);
            Require(reference is int recovered && recovered == 0 && WindowsDisplayRotation.RelativeOrientation(recovered, 0, 0) == 0,
                "A fresh sensor session can recover a stuck portrait through the saved natural landscape reference");
            reference = WindowsDisplayRotation.KeepOrCaptureReference(reference, false, 3, false, 1);
            Require(reference is int retained && retained == 0 &&
                WindowsDisplayRotation.RelativeOrientation(retained, 0, 1) == 3 &&
                WindowsDisplayRotation.RelativeOrientation(retained, 0, 3) == 1,
                "Relocking in portrait keeps the established orientation reference for both sides");
            Require(WindowsDisplayRotation.KeepOrCaptureReference(null, false, 3, false, 1) == null,
                "An unverified portrait does not become the initial calibration");
            Require(WindowsDisplayRotation.FallbackStatus("Windows could not rotate the built-in display") is
                { Available:true, Locked:false, Reason.Length:>0 },
                "A failed sensor-driven rotation pauses tracking without turning on the user's lock");
            long first=entry.EnqueueGuideAction(Press(),0,1);
            for(int n=0;n<100&&Volatile.Read(ref restores)==0;n++)await Task.Delay(5);
            bool retryClosing=Press();Require(!retryClosing,"repeat press before visible remains an open request");
            long retry=entry.EnqueueGuideAction(retryClosing,0,2);var shown=await Wait(retry);await Settle();
            Require(shown.GetProperty("widgetVisible").GetBoolean()&&Volatile.Read(ref restores)==2,"Guide cancels old host activation and fresh release restores successfully");
            bool close=Press();Require(close,"pre-Down snapshot closes correctly even after native dismissal");long closed=entry.EnqueueGuideAction(close,0,3);await Wait(closed);await Settle();
            Require(Volatile.Read(ref dismisses)==0,"Guide dismissal never sends a synthetic Win+G");
            SetOpen(true);long oldClose=entry.EnqueueGuideAction(entry.BeginGuidePress(true),0,4);await Task.Delay(25);SetOpen(false);
            var timer=Stopwatch.StartNew();long freshOpen=entry.EnqueueGuideAction(Press(),0,5);await Wait(freshOpen);await Settle();Require(timer.ElapsedMilliseconds<600,"newer open interrupts obsolete close wait");
            SetOpen(false);int before=Volatile.Read(ref restores);long latest=0;
            for(int n=0;n<64;n++)latest=entry.EnqueueGuideAction(Press(),0,6+n);
            var final=await Wait(latest);await Settle();Require(Volatile.Read(ref restores)-before<=4&&final.GetProperty("widgetVisible").GetBoolean(),"64 rapid Guide presses cancel stale host activations and finish open");
            SetOpen(true);int beforeHomeActivation=Volatile.Read(ref restores);
            long homeOpen=entry.EnqueueGuideAction(false,0,70);await Wait(homeOpen);await Settle();
            Require(Volatile.Read(ref restores)==beforeHomeActivation+1,"Explicit Guide open activates ours even when Home/another page opened and widget reports visible");
            var audit=(await File.ReadAllLinesAsync(Path.Combine(folder,"overlay-actions.jsonl"))).Where(l=>!string.IsNullOrWhiteSpace(l)).Select(l=>JsonSerializer.Deserialize<JsonElement>(l)).ToArray();
            Require(audit.Length==70&&audit.All(r=>r.GetProperty("error").ValueKind==JsonValueKind.Null),"all 70 requests acknowledged without timeout");
            Require(audit.Single(r=>r.GetProperty("id").GetInt64()==first).GetProperty("superseded").GetBoolean(),"unconfirmed old open recorded as superseded");
            Require(audit.Single(r=>r.GetProperty("id").GetInt64()==oldClose).GetProperty("superseded").GetBoolean(),"old close recorded as superseded");
            int beforeCompanion=restores;
            await Wait(entry.EnqueueCompanionAction(false));
            await Wait(entry.EnqueueCompanionAction(false));
            Require(restores==beforeCompanion+2&&Volatile.Read(ref open)!=0,"Companion Open always activates instead of toggling closed");
            int beforeDismiss=dismisses;
            await Wait(entry.EnqueueCompanionAction(true));
            Require(dismisses==beforeDismiss+1&&Volatile.Read(ref open)==0,"Companion Close dismisses the visible widget");
            Volatile.Write(ref open,1);
            entry.SetWidgetVisible(false);
            await Wait(entry.EnqueueCompanionAction(true));
            Require(dismisses==beforeDismiss+1,"Companion Close leaves unrelated visible Game Bar content alone");
            SetOpen(false);

            bool Swipe(params (uint Count,bool Tip,uint Id,double X,double Y,long Time)[] frames)
            {
                var touch=new TouchCornerSwipe();
                return frames.Select(f=>touch.Observe(f.Count,f.Tip,f.Id,f.X,f.Y,f.Time,0)).ToArray().Count(result=>result)==1;
            }
            Require(Swipe((1,true,7,.04,.01,100),(1,true,7,.05,.13,235),(0,false,0,0,0,255)),
                "A deliberate down swipe from the top-left opens on release");
            Require(!Swipe((1,true,7,.7,.01,100),(1,true,7,.7,.22,235),(0,false,0,0,0,255))&&
                !Swipe((1,true,7,.04,.15,100),(1,true,7,.05,.31,235),(0,false,0,0,0,255)),
                "Swipes elsewhere on the edge or interior do not open the widget");
            Require(!Swipe((1,true,7,.04,.01,100),(1,true,7,.05,.03,235),(0,false,0,0,0,255))&&
                !Swipe((1,true,7,.04,.01,100),(1,true,7,.17,.15,235),(0,false,0,0,0,255))&&
                !Swipe((1,true,7,.04,.01,100),(1,true,7,.04,.19,1040),(0,false,0,0,0,1140)),
                "Taps, diagonal drags and holds do not open the widget");
            Require(!Swipe((1,true,7,.04,.01,100),(2,true,7,.04,.1,200),(0,true,8,.12,.04,210),
                (1,true,7,.04,.22,270),(0,false,0,0,0,300)), "Multi-touch and hybrid continuation frames do not open");
            Require(Swipe((1,true,7,.04,.01,100),(1,true,7,.05,.13,235),(0,false,0,0,0,255),
                (1,true,9,.04,.01,300),(1,true,9,.05,.13,435),(0,false,0,0,0,455)), "A repeated swipe during the cooldown does not open twice");
            bool RawSwipe(int? initialOrientation, int? releaseOrientation, double startX, double startY,
                double endX, double endY, bool displayChanged = false)
            {
                var touch = new TouchCornerSwipe();
                touch.Observe(1, true, 7, startX, startY, 100, initialOrientation);
                touch.Observe(1, true, 7, endX, endY, 235, null);
                if (displayChanged) touch.Invalidate();
                return touch.Observe(0, false, 0, 0, 0, 255, releaseOrientation);
            }
            Require(RawSwipe(1, 1, .01, .96, .13, .95) &&
                RawSwipe(2, 2, .96, .99, .95, .87) &&
                RawSwipe(3, 3, .99, .04, .87, .05),
                "A downward swipe from the visible top-left maps correctly in both portraits and flipped landscape");
            Require(Enumerable.Range(1, 3).All(angle => !RawSwipe(angle, angle, .04, .01, .05, .13)),
                "The natural panel corner does not open the widget after rotation");
            Require(!RawSwipe(1, 1, .01, .96, .02, .81) && !RawSwipe(3, 3, .99, .04, .98, .19),
                "A horizontal swipe in the visible portrait layout does not activate");
            Require(!RawSwipe(0, 2, .04, .01, .05, .13) &&
                !RawSwipe(0, 0, .04, .01, .05, .13, displayChanged: true) &&
                !RawSwipe(null, 0, .04, .01, .05, .13) && !RawSwipe(0, null, .04, .01, .05, .13),
                "The swipe is discarded when the display changes mid-contact or the current angle is unknown");
            var touchOpened=await Wait(entry.EnqueueTouchCornerOpen());
            Require(!touchOpened.GetProperty("closing").GetBoolean()&&
                touchOpened.GetProperty("source").GetString()=="touch-top-left"&&touchOpened.GetProperty("widgetVisible").GetBoolean(),
                "Touch requests explicitly activate ONEXTweaks through the ordinary Game Bar queue");

            string recoveryFolder=Path.Combine(folder,"recovery");Directory.CreateDirectory(recoveryFolder);
            int reopened=0,running=0,recoveries=0;GameBarEntry? recoveryOwner=null;
            var recoveryRuntime=new GameBarEntryRuntime(
                ()=>Volatile.Read(ref reopened)!=0,()=>Volatile.Read(ref running)!=0,()=>{},
                ()=>{
                    Volatile.Write(ref running,1);Volatile.Write(ref reopened,1);
                    recoveryOwner!.SetWidgetReady(true);recoveryOwner.SetWidgetVisible(true);
                },
                ()=>Task.CompletedTask,Recover:(stillNeeded,token)=>
                {
                    if(!stillNeeded())return Task.FromResult(false);
                    Interlocked.Increment(ref recoveries);
                    return Task.FromResult(true);
                });
            using(var recoveryEntry=new GameBarEntry(recoveryFolder,recoveryRuntime))
            {
                recoveryOwner=recoveryEntry;
                recoveryEntry.EnqueueOemAction(false,"Home");
                var deadline=Stopwatch.StartNew();
                string receipt=Path.Combine(recoveryFolder,"gamebar-entry.json");
                while(!File.Exists(receipt)&&deadline.ElapsedMilliseconds<14000)await Task.Delay(20);
                using var document=JsonDocument.Parse(await File.ReadAllTextAsync(receipt));
                var response=document.RootElement;
                Require(recoveries==1&&response.GetProperty("error").ValueKind==JsonValueKind.Null&&
                    response.GetProperty("route").GetString()=="recover-gamebar-and-activate"&&response.GetProperty("widgetVisible").GetBoolean(),
                    "One qualified host restart recovers an explicit OEM open after failed background activation");
            }
            string transientFolder=Path.Combine(folder,"transient");Directory.CreateDirectory(transientFolder);
            int observerCalls=0,prewarmCalls=0;
            var transientRuntime=new GameBarEntryRuntime(
                ()=>Interlocked.Increment(ref observerCalls)==1?throw new IOException("transient Game Bar fault"):false,
                ()=>false,()=>Interlocked.Increment(ref prewarmCalls),()=>{},()=>Task.CompletedTask);
            using(var transientEntry=new GameBarEntry(transientFolder,transientRuntime))
            {
                var deadline=Stopwatch.StartNew();
                while(Volatile.Read(ref prewarmCalls)==0&&deadline.ElapsedMilliseconds<4000)await Task.Delay(20);
                Require(Volatile.Read(ref prewarmCalls)>0,"A transient observation error must not disable background prewarm");
            }
            string fseFolder=Path.Combine(folder,"fse");Directory.CreateDirectory(fseFolder);
            int fseOpen=0,uriActivations=0,hiddenSelections=0;GameBarEntry? fseOwner=null;
            var fseRuntime=new GameBarEntryRuntime(()=>Volatile.Read(ref fseOpen)!=0,()=>true,()=>{},()=>{Interlocked.Increment(ref uriActivations);Volatile.Write(ref fseOpen,1);fseOwner!.SetWidgetVisible(true);},()=>Task.CompletedTask,()=>true,()=>{if(Volatile.Read(ref fseOpen)!=0)throw new InvalidOperationException("Hidden selection called while open");Interlocked.Increment(ref hiddenSelections);});
            using(var fseEntry=new GameBarEntry(fseFolder,fseRuntime))
            {
                fseOwner=fseEntry;fseEntry.SetWidgetReady(true);
                async Task<JsonElement> FseWait(long id)
                {
                    for(int i=0;i<150;i++)
                    {
                        try{using var stream=new FileStream(Path.Combine(fseFolder,"gamebar-entry.json"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var doc=await JsonDocument.ParseAsync(stream);if(doc.RootElement.GetProperty("id").GetInt64()==id)return doc.RootElement.Clone();}catch(IOException){}catch(JsonException){}
                        await Task.Delay(10);
                    }
                    throw new TimeoutException("FSE activation queue did not respond");
                }
                await Task.Delay(200);Require(hiddenSelections==1,"FSE selects ours once while hidden after readiness, with no foreground request");Volatile.Write(ref fseOpen,1);await Task.Delay(150);
                Require(uriActivations==0&&!File.Exists(Path.Combine(fseFolder,"gamebar-entry.json")),"FSE native opening does not activate or switch pages after Home; native start-page preference owns it");
                Volatile.Write(ref fseOpen,0);await Task.Delay(150);Volatile.Write(ref fseOpen,1);
                await Task.Delay(150);
                Require(uriActivations==0,"FSE repeated native openings remain observer-only");
                Require(hiddenSelections==2,"FSE prepares the next opening only after dismissal, never while Home or Settings is visible");
                Volatile.Write(ref fseOpen,0);await Task.Delay(150);
                await FseWait(fseEntry.EnqueueGuideAction(false,0,1));await Task.Delay(150);
                Require(uriActivations==1,"FSE explicit opening is not duplicated by the native visibility observer");
            }
            result=new{passed=true,checks,requests=audit.Length,fseNativeOpensObserved=2,fseRequests=1,superseded=audit.Count(r=>r.GetProperty("superseded").GetBoolean()),restores,dismisses,realGameBarTouched=false,hardwareTouched=false};
        }
        catch(Exception e){result=new{passed=false,checks,error=e.ToString(),restores,dismisses,realGameBarTouched=false,hardwareTouched=false};}
        finally{await File.WriteAllTextAsync(output,JsonSerializer.Serialize(result,Backend.Program.Json));}
        using var parsed=JsonDocument.Parse(await File.ReadAllTextAsync(output));return parsed.RootElement.GetProperty("passed").GetBoolean()?0:1;
    }
}
