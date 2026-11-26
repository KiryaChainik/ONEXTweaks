// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private sealed class PowerReleaseObservation
        {
            internal string Name;
            internal double Heartbeat,Queue,Frame,LastHeartbeat,LastFrame,Elapsed,HomeHeight;
            internal int Heartbeats,Frames,BusySamples,ToggleDisabledSamples,SliderDisabledSamples,HeightChanges;
            internal bool ApplyingSeen;
            internal readonly List<KeyValuePair<string,double>> Phases=new List<KeyValuePair<string,double>>();
        }
        private PowerReleaseObservation _powerReleaseObservation;
        private void ObservePowerReleaseAck(string phase,Action action)
        {
            var row=App.OnexTileRevisionStage==221?_powerReleaseObservation:null;
            if(row==null){action();return;}
            var watch=Stopwatch.StartNew();try{action();}finally{row.Phases.Add(new KeyValuePair<string,double>(phase,watch.Elapsed.TotalMilliseconds));}
        }
        private async Task VerifyOnexPowerReleaseReadbackAsync(JsonObject report,bool strict=false)
        {
            // 221 records latency;222 applies the unchanged150ms UI/normal budget.
            // Actual production releases, synthetic pointer state, no retries.
            EnsureOnexReleaseLayout();var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
            var baseline=await ReadOnexStateAsync();string scope=OnexPerformancePl2Scope;
            int first=(int)baseline.GetNamedNumber("pl1Watts"),second=(int)baseline.GetNamedNumber("pl2Watts");
            if(!baseline.GetNamedBoolean("canWrite")||!baseline.GetNamedBoolean("separatePl2")||first<=8||first>=40||second<=first||second>41)throw new InvalidOperationException("Fresh writable PL2 On baseline required");
            int nextFirst=first+1<second?first+1:first-1,nextSecond=second<41?second+1:second-1;
            int expectedFirst=first,expectedSecond=second;
            long ackBatches=_onexHomePowerAckBatches,ackPaints=_onexHomePowerAckSynchronousPaints;
            var clock=Stopwatch.StartNew();var ui=Dispatcher;var rows=new List<PowerReleaseObservation>();
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(16)};
            timer.Tick+=(s,e)=>
            {
                var row=_powerReleaseObservation;if(row==null)return;double now=clock.Elapsed.TotalMilliseconds;
                if(row.LastHeartbeat>0)row.Heartbeat=Math.Max(row.Heartbeat,now-row.LastHeartbeat);row.LastHeartbeat=now;row.Heartbeats++;
                bool home=row.Name.StartsWith("Home");bool busy=home?_releaseHome.PowerRequestInFlight:_onexPerformancePl1Busy||_onexPerformancePl2ValueBusy;
                if(home){row.ApplyingSeen|=_releaseHome.PowerStatusText==OnexStrings.Get("Applying…");if(Math.Abs(_releaseHome.PowerSection.ActualHeight-row.HomeHeight)>0.5)row.HeightChanges++;}
                if(!busy)return;row.BusySamples++;
                if(home?!_releaseHome.PowerToggle.IsEnabled:!TDPBoostToggle.IsEnabled)row.ToggleDisabledSamples++;
                if(home?(!_releaseHome.PowerInput.IsEnabled||!_releaseHome.Pl2Input.IsEnabled):(!TDPSlider.IsEnabled||!TDPBoostFPPTSliderCard.IsEnabled))row.SliderDisabledSamples++;
            };
            EventHandler<object> render=(s,e)=>{var row=_powerReleaseObservation;if(row==null)return;double now=clock.Elapsed.TotalMilliseconds;if(row.LastFrame>0)row.Frame=Math.Max(row.Frame,now-row.LastFrame);row.LastFrame=now;row.Frames++;};
            var stop=new CancellationTokenSource();Task probe=Task.CompletedTask;
            bool Idle()=>!_releaseHome.HasPendingPowerInput&&!_onexPerformancePl1Busy&&!_onexPerformancePl1Pending&&!_onexPerformancePl2ValueBusy&&!_onexPerformancePl2ValuePending;
            async Task Settle(int a,int b)
            {
                for(int i=0;i<240;i++){await Task.Delay(50);if(scope!=OnexPerformancePl2Scope)throw new InvalidOperationException("Owner context changed; no stale restore");if(!Idle())continue;var actual=await ReadOnexStateAsync();if(actual.GetNamedNumber("pl1Watts")==a&&actual.GetNamedNumber("pl2Watts")==b)return;}
                throw new InvalidOperationException("Actual release did not settle");
            }
            async Task Release(bool home,bool pl2,int target,int a,int b,string name)
            {
                if(home)PerformanceNavItem.IsChecked=true;else _releasePowerNav.IsChecked=true;
                ApplyOnexReleasePowerState((await ReadOnexStateAsync()).Stringify());await Task.Delay(150);
                Slider slider=home?(pl2?_releaseHome.Pl2Input:_releaseHome.PowerInput):(pl2?TDPBoostFPPTSliderCard:TDPSlider);
                if(home)_releaseHome.VerifyPowerPointer(slider,true);else{OnexInputGuard.VerifyPointerHeld(slider,true);if(pl2)BeginOnexPerformancePl2Pointer(slider);else BeginOnexPerformancePl1Pointer();}
                slider.Value=target;await Task.Delay(180);
                var row=new PowerReleaseObservation{Name=name,LastHeartbeat=clock.Elapsed.TotalMilliseconds,LastFrame=clock.Elapsed.TotalMilliseconds,HomeHeight=_releaseHome.PowerSection.ActualHeight};rows.Add(row);_powerReleaseObservation=row;double started=clock.Elapsed.TotalMilliseconds;
                expectedFirst=a;expectedSecond=b;
                if(home)_releaseHome.VerifyPowerPointer(slider,false);else OnexInputGuard.VerifyPointerHeld(slider,false);
                await Settle(a,b);await Task.Delay(250);row.Elapsed=clock.Elapsed.TotalMilliseconds-started;_powerReleaseObservation=null;
            }
            try
            {
                timer.Start();CompositionTarget.Rendering+=render;
                probe=Task.Run(async()=>{while(!stop.IsCancellationRequested){var row=_powerReleaseObservation;var queued=Stopwatch.StartNew();await ui.RunAsync(CoreDispatcherPriority.Normal,()=>{if(row!=null&&ReferenceEquals(row,_powerReleaseObservation))row.Queue=Math.Max(row.Queue,queued.Elapsed.TotalMilliseconds);});await Task.Delay(25);}});
                foreach(bool home in new[]{true,false})foreach(bool pl2 in new[]{false,true})
                {
                    string name=(home?"Home":"Performance")+(pl2?"PL2":"PL1");
                    await Release(home,pl2,pl2?nextSecond:nextFirst,pl2?first:nextFirst,pl2?nextSecond:second,name);
                    await Release(home,pl2,pl2?second:first,first,second,name+"Restore");
                }
                report["actualProductionReleaseWrites"]=JsonValue.CreateNumberValue(4);report["narrowRestoreWrites"]=JsonValue.CreateNumberValue(4);
                if(strict)
                {
                    foreach(var row in rows)
                    {
                        if(row.Heartbeats<5||row.BusySamples==0||row.Heartbeat>=150||row.Queue>=150||row.Frame>=250)throw new InvalidOperationException("First release-window responsiveness failed: "+row.Name);
                        if(row.ToggleDisabledSamples!=0||row.SliderDisabledSamples!=0||row.ApplyingSeen||row.Name.StartsWith("Home")&&row.HeightChanges!=0)throw new InvalidOperationException("Release disabled input/status/layout changed: "+row.Name);
                    }
                    if(_onexHomePowerAckBatches-ackBatches!=4||_onexHomePowerAckSynchronousPaints!=ackPaints)throw new InvalidOperationException("Home ACK painted synchronously instead of one deferred frame");
                    long writes=_onexHomePowerWriteRpcs;report["queuedToggleFixture"]=await VerifyOnexQueuedPowerToggleAsync();if(_onexHomePowerWriteRpcs!=writes)throw new InvalidOperationException("Isolated fixture reached production power");
                }
                report["releaseWindowResponsivenessAccepted"]=JsonValue.CreateBooleanValue(strict);report["homeAckBatches"]=JsonValue.CreateNumberValue(_onexHomePowerAckBatches-ackBatches);report["homeAckSynchronousPaints"]=JsonValue.CreateNumberValue(_onexHomePowerAckSynchronousPaints-ackPaints);
                report["diagnosticOnlyNoLatencyAcceptanceClaimed"]=JsonValue.CreateBooleanValue(!strict);report["physicalInputTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                _powerReleaseObservation=null;timer.Stop();CompositionTarget.Rendering-=render;stop.Cancel();await probe;stop.Dispose();
                OnexInputGuard.VerifyPointerHeld(TDPSlider,false);OnexInputGuard.VerifyPointerHeld(TDPBoostFPPTSliderCard,false);_releaseHome.VerifyPowerPointer(_releaseHome.PowerInput,false);_releaseHome.VerifyPowerPointer(_releaseHome.Pl2Input,false);
                CancelOnexPerformancePl1Value();CancelOnexPerformancePl2Value();_releaseHome.CancelPendingPower();
                for(int i=0;i<240&&(_onexPerformancePl1Busy||_onexPerformancePl2ValueBusy||_releaseHome.PowerRequestInFlight);i++)await Task.Delay(50);
                if(_onexPerformancePl1Busy||_onexPerformancePl2ValueBusy||_releaseHome.PowerRequestInFlight)throw new InvalidOperationException("Own request still running; inspect child before recovery");
                var actual=await ReadOnexStateAsync();
                if(actual.GetNamedString("rawLimits")!=baseline.GetNamedString("rawLimits"))
                {
                    if(scope!=OnexPerformancePl2Scope||actual.GetNamedNumber("pl1Watts")!=expectedFirst||actual.GetNamedNumber("pl2Watts")!=expectedSecond)throw new InvalidOperationException("Owner limits changed; no stale restore");
                    PerformanceNavItem.IsChecked=true;ApplyOnexReleasePowerState(actual.Stringify());
                    if(expectedFirst!=first){_releaseHome.VerifyPowerPointer(_releaseHome.PowerInput,true);_releaseHome.PowerInput.Value=first;_releaseHome.VerifyPowerPointer(_releaseHome.PowerInput,false);await Settle(first,expectedSecond);}
                    if(expectedSecond!=second){_releaseHome.VerifyPowerPointer(_releaseHome.Pl2Input,true);_releaseHome.Pl2Input.Value=second;_releaseHome.VerifyPowerPointer(_releaseHome.Pl2Input,false);await Settle(first,second);}
                }
                ApplyOnexReleasePowerState((await ReadOnexStateAsync()).Stringify());if((await ReadOnexStateAsync()).GetNamedString("rawLimits")!=baseline.GetNamedString("rawLimits"))throw new InvalidOperationException("Fresh baseline not restored");
                var output=new JsonArray();foreach(var row in rows){var phases=new JsonArray();foreach(var phase in row.Phases)phases.Add(new JsonObject{{"phase",JsonValue.CreateStringValue(phase.Key)},{"milliseconds",JsonValue.CreateNumberValue(phase.Value)}});output.Add(new JsonObject{{"name",JsonValue.CreateStringValue(row.Name)},{"releaseToSettledMs",JsonValue.CreateNumberValue(row.Elapsed)},{"heartbeatMaxMs",JsonValue.CreateNumberValue(row.Heartbeat)},{"normalQueueMaxMs",JsonValue.CreateNumberValue(row.Queue)},{"frameGapMaxMs",JsonValue.CreateNumberValue(row.Frame)},{"heartbeatSamples",JsonValue.CreateNumberValue(row.Heartbeats)},{"frameSamples",JsonValue.CreateNumberValue(row.Frames)},{"inFlightSamples",JsonValue.CreateNumberValue(row.BusySamples)},{"toggleDisabledSamples",JsonValue.CreateNumberValue(row.ToggleDisabledSamples)},{"sliderDisabledSamples",JsonValue.CreateNumberValue(row.SliderDisabledSamples)},{"applyingSeen",JsonValue.CreateBooleanValue(row.ApplyingSeen)},{"homeHeightChanges",JsonValue.CreateNumberValue(row.HeightChanges)},{"ackPhases",phases}});}
                report["releaseObservations"]=output;report["freshBaselineRestored"]=JsonValue.CreateBooleanValue(true);if(original?.Visibility==Visibility.Visible)original.IsChecked=true;
            }
        }
        private async Task<JsonObject> VerifyOnexQueuedPowerToggleAsync()
        {
            // Actual isolated Home control and dispatcher timers; controlled ACKs,
            // illustrative watts, no production API or owner preferences.
            var fixture=new OnexReleaseHome(false);fixture.PowerScope=()=>"isolated-queue";
            var sent=new List<Tuple<int,int,bool>>();fixture.PowerRequested+=(a,b)=>sent.Add(Tuple.Create(a,b,fixture.SeparatePl2Enabled));
            int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
            async Task Sent(int count){for(int i=0;i<40&&sent.Count<count;i++)await Task.Delay(25);Check(sent.Count==count,"Isolated queued request count");}
            try
            {
                fixture.ApplyPower(23,29,true,true,29);fixture.VerifyPowerPointer(fixture.PowerInput,true);fixture.PowerInput.Value=24;fixture.VerifyPowerPointer(fixture.PowerInput,false);await Sent(1);
                Check(fixture.PowerRequestInFlight&&fixture.PowerToggle.IsEnabled,"Toggle locked while isolated request busy");
                fixture.VerifyInvokeActualPl2Input();Check(!fixture.SeparatePl2Enabled&&fixture.HasPendingPowerInput,"Busy toggle ignored");await Task.Delay(180);Check(sent.Count==1,"Queued toggle started concurrent request");
                fixture.ApplyPower(24,29,true,true,29);Check(!fixture.SeparatePl2Enabled,"Old readback reverted queued toggle");fixture.CompletePower(24,29,true,null,true,29);await Sent(2);
                Check(sent[1].Item1==24&&sent[1].Item2==25&&!sent[1].Item3,"Queued Off did not preserve latest pair/mode");fixture.CompletePower(24,25,true,null,false,29);Check(!fixture.SeparatePl2Enabled&&fixture.PowerToggle.IsEnabled,"Off completion not interactive");
                fixture.VerifyInvokeActualPl2Input();await Sent(3);Check(sent[2].Item3&&sent[2].Item2==29,"Queued On lost remembered PL2");fixture.CompletePower(24,25,true,"Isolated rejected ACK",false,29);Check(!fixture.SeparatePl2Enabled&&fixture.PowerStatusText=="Isolated rejected ACK","Failed toggle did not restore actual mode/error");
                fixture.ApplyPower(24,29,true,true,29);fixture.VerifyPowerPointer(fixture.PowerInput,true);fixture.PowerInput.Value=25;fixture.VerifyPowerPointer(fixture.PowerInput,false);await Sent(4);fixture.VerifyInvokeActualPl2Input();fixture.CancelPendingPower();fixture.CompletePower(25,29,true,null,true,29);
                await Task.Delay(180);Check(sent.Count==4&&fixture.SeparatePl2Enabled&&!fixture.HasPendingPowerInput,"Cancelled queued toggle survived actual ACK");
                fixture.ApplyPower(25,29,false,true,29);Check(!fixture.PowerToggle.IsEnabled&&!fixture.PowerInput.IsEnabled&&!fixture.Pl2Input.IsEnabled,"Unavailable control became writable");
                return new JsonObject{{"checks",JsonValue.CreateNumberValue(checks)},{"controlledRequests",JsonValue.CreateNumberValue(sent.Count)},{"latestTogglePreserved",JsonValue.CreateBooleanValue(true)},{"noConcurrentRequests",JsonValue.CreateBooleanValue(true)},{"staleAckProtected",JsonValue.CreateBooleanValue(true)},{"errorAndCancelProtected",JsonValue.CreateBooleanValue(true)},{"ownerPreferencesWritten",JsonValue.CreateBooleanValue(false)},{"productionPowerRpcs",JsonValue.CreateNumberValue(0)}};
            }
            finally{fixture.CancelPendingPower();fixture.VerifyPowerPointer(fixture.PowerInput,false);fixture.VerifyPowerPointer(fixture.Pl2Input,false);fixture.CompletePower(25,29,false,null,true,29);}
        }
    }
}
