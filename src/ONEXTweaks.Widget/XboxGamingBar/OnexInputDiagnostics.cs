// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Input;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private bool _onexInputProbeStarted;
        private long _onexProbePointers,_onexProbeKeys;
        private void StartOnexInputDiagnosticsIfArmed()
        {
            if(_onexInputProbeStarted)return;
            string path=System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path,"onex-input-diagnostics-armed.json");
            if(!System.IO.File.Exists(path)||DateTime.UtcNow-System.IO.File.GetLastWriteTimeUtc(path)>TimeSpan.FromMinutes(3))return;
            _onexInputProbeStarted=true;
            OnexStartupRoot.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((s,e)=>_onexProbePointers++),true);
            OnexStartupRoot.AddHandler(UIElement.KeyDownEvent,new KeyEventHandler((s,e)=>_onexProbeKeys++),true);
            var clock=Stopwatch.StartNew();long previous=0,maxGap=0;int ticks=0;bool writing=false;
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
            timer.Tick+=async(s,e)=>
            {
                // The foreground handshake already completed. This observer
                // writes brokered files and must not perturb the timed pass.
                if(App.OnexTileRevisionStage==200){timer.Stop();return;}
                long now=clock.ElapsedMilliseconds;if(previous>0)maxGap=Math.Max(maxGap,now-previous);previous=now;
                if(now>120000||App.GetActiveGamingWidget()!=this){timer.Stop();return;}
                if(++ticks%8!=0||writing)return;writing=true;
                try
                {
                    var report=new JsonObject{{"utc",JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("o"))},{"elapsedMs",JsonValue.CreateNumberValue(now)},{"maxDispatcherGapMs",JsonValue.CreateNumberValue(maxGap)},{"pointerPresses",JsonValue.CreateNumberValue(_onexProbePointers)},{"keys",JsonValue.CreateNumberValue(_onexProbeKeys)},{"inputRedirected",JsonValue.CreateBooleanValue(Windows.Gaming.UI.GameBar.IsInputRedirected)},{"rootHitTest",JsonValue.CreateBooleanValue(OnexStartupRoot.IsHitTestVisible)},{"rootEnabled",JsonValue.CreateBooleanValue(IsEnabled)},{"coverVisible",JsonValue.CreateBooleanValue(OnexStartupCover.Visibility==Visibility.Visible)},{"queuedStates",JsonValue.CreateNumberValue(_onexStatePushes.PendingCount)},{"appliedStates",JsonValue.CreateNumberValue(_onexPushApplied)},{"duplicateStates",JsonValue.CreateNumberValue(_onexStatePushes.Duplicates)},{"gc0",JsonValue.CreateNumberValue(GC.CollectionCount(0))},{"gc2",JsonValue.CreateNumberValue(GC.CollectionCount(2))},{"managedBytes",JsonValue.CreateNumberValue(GC.GetTotalMemory(false))}};
                    if(widget!=null){report["visible"]=JsonValue.CreateBooleanValue(widget.Visible);report["displayMode"]=JsonValue.CreateStringValue(widget.GameBarDisplayMode.ToString());report["pinned"]=JsonValue.CreateBooleanValue(widget.Pinned);report["clickThrough"]=JsonValue.CreateBooleanValue(widget.ClickThroughEnabled);}
                    await FileIO.WriteTextAsync(await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-input-diagnostics.json",CreationCollisionOption.ReplaceExisting),report.Stringify());
                }
                catch(Exception error){Logger.Warn("ONEX input diagnostic: "+error.Message);}
                finally{writing=false;}
            };
            timer.Start();
        }
        private async Task VerifyOnexInputResponsivenessAsync(JsonObject report)
        {
            if(!Windows.Gaming.UI.GameBar.IsInputRedirected||!OnexStartupRoot.IsHitTestVisible||!IsEnabled||OnexStartupCover.Visibility==Visibility.Visible)throw new InvalidOperationException("The widget is visible but its input path is blocked");
            var pushOwner = App.PipeClient;
            var charge=await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet{{"OnexChargeGet",true}});
            if (!App.IsCurrentConnectedPipeClient(pushOwner)) throw new InvalidOperationException("State push diagnostic connection changed");
            string packet=new JsonObject{{"RequestId",JsonValue.CreateNumberValue(0)},{"OnexChargeState",JsonValue.CreateStringValue((string)charge["OnexChargeState"])}}.Stringify();
            long before=_onexPushApplied;_onexStatePushes.Invalidate();
            await Task.Run(()=>{for(int n=0;n<50000;n++)QueueOnexStatePush(pushOwner, packet);});await Task.Delay(1000);
            if(_onexPushApplied-before>100||_onexStatePushes.PendingCount>15)throw new InvalidOperationException("State pushes flooded the dispatcher");
            report["stressPackets"]=JsonValue.CreateNumberValue(50000);report["stressUiApplies"]=JsonValue.CreateNumberValue(_onexPushApplied-before);
            var collections=new JsonArray();
            for(int n=0;n<8;n++)
            {
                var elapsed=Stopwatch.StartNew();await Task.Run(()=>{GC.Collect();GC.WaitForPendingFinalizers();});
                collections.Add(JsonValue.CreateNumberValue(elapsed.ElapsedMilliseconds));await Task.Delay(100);
            }
            report["collectionMs"]=collections;
            // Test input delivery without mouse positioning: the full tab-pointer
            // scenario is independent and must not obscure dispatcher responsiveness.
            await VerifyOnexResponsiveControllerAsync(report);
            report["inputRedirectedAfterStress"]=JsonValue.CreateBooleanValue(Windows.Gaming.UI.GameBar.IsInputRedirected);
            report["rootHitTestAfterStress"]=JsonValue.CreateBooleanValue(OnexStartupRoot.IsHitTestVisible);
        }
        private async Task VerifyOnexResponsiveControllerAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");int step=0;
            var toggle=(Windows.UI.Xaml.Controls.ToggleSwitch)_releaseHome.FindName("LightPl2Toggle");
            async Task Input(string key,Func<bool> expected)
            {
                string id=run+":"+(++step);
                var rect=new Windows.UI.Xaml.Automation.Peers.RadioButtonAutomationPeer(PerformanceNavItem).GetBoundingRectangle();
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2)}}.Stringify());
                bool ok=false;for(int n=0;n<60;n++){await Task.Delay(100);try{if(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&expected()){ok=true;break;}}catch{}}
                if(!ok)throw new InvalidOperationException("Input delivery failed: "+key+" step "+step+" focus="+(FocusManager.GetFocusedElement() as FrameworkElement)?.Name);
                await Task.Delay(150);
            }
            try
            {
                PerformanceNavItem.IsChecked=true;_releaseHome.PowerInput.IsFocusEngaged=false;FocusFirstTabContentElement();
                // Game Bar may consume the first direction to enter controller mode.
                // Record that warmup separately; the next three inputs must reach XAML.
                await Input("up",()=>true);long before=_onexProbeKeys;
                await Input("up",()=>ReferenceEquals(FocusManager.GetFocusedElement(),toggle));
                await Input("down",()=>ReferenceEquals(FocusManager.GetFocusedElement(),_releaseHome.PowerInput));
                await Input("up",()=>ReferenceEquals(FocusManager.GetFocusedElement(),toggle));
                if(_onexProbeKeys-before<3)throw new InvalidOperationException("Controller keys did not reach the widget after stress");
                report["controllerModeWarmupSteps"]=JsonValue.CreateNumberValue(1);report["actualVirtualControllerSteps"]=JsonValue.CreateNumberValue(step);
                report["checkedControllerSteps"]=JsonValue.CreateNumberValue(3);report["deliveredControllerKeys"]=JsonValue.CreateNumberValue(_onexProbeKeys-before);
                report["pointerFocusClicks"]=JsonValue.CreateNumberValue(0);
            }
            finally{await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");}
        }
    }
}
