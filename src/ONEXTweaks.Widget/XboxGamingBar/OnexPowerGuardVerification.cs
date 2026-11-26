// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexHomeReleaseAckAsync(JsonObject report)
        {
            // Real Home release writes/readback, synthetic held input; no physical
            // touch or pixel test. The success path must not show Applying or requery.
            EnsureOnexReleaseLayout();var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);PerformanceNavItem.IsChecked=true;
            var baseline=await ReadOnexStateAsync();string scope=OnexPerformancePl2Scope;
            int first=(int)baseline.GetNamedNumber("pl1Watts"),second=(int)baseline.GetNamedNumber("pl2Watts");int checks=0,dispatches=0;bool quiet=true,interactive=true;
            void Check(bool ok,string message){checks++;if(!ok)throw new InvalidOperationException(message);}
            Check(baseline.GetNamedBoolean("canWrite")&&baseline.GetNamedBoolean("separatePl2")&&first>8&&first<40&&second>first&&second<=41,"Fresh writable PL2 On baseline required");
            int target1=first+1<second?first+1:first-1,target2=second<41?second+1:second-1;Check(target2>=first,"No conservative target");
            int expected1=first,expected2=second;
            async Task Settle(int a,int b)
            {
                for(int i=0;i<120;i++){await Task.Delay(100);var actual=await ReadOnexStateAsync();Check(scope==OnexPerformancePl2Scope,"Owner context changed");if(actual.GetNamedNumber("pl1Watts")==a&&actual.GetNamedNumber("pl2Watts")==b&&!_releaseHome.HasPendingPowerInput)return;}
                throw new InvalidOperationException("Home release did not settle");
            }
            void Dispatched(int a,int b){dispatches++;quiet&=_releaseHome.PowerStatusText!=OnexStrings.Get("Applying…");interactive&=_releaseHome.PowerInput.IsEnabled&&_releaseHome.Pl2Input.IsEnabled;}
            _releaseHome.PowerRequested+=Dispatched;long writes=_onexHomePowerWriteRpcs,reads=_onexHomePowerFallbackReads;
            try
            {
                ApplyOnexReleasePowerState(baseline.Stringify());await Task.Delay(120);double height=_releaseHome.PowerSection.ActualHeight;Check(height>0,"Actual Home power section not loaded");
                long preferences=_releaseHome.PowerPreferenceWriteCount;
                for(int i=0;i<1000;i++)_releaseHome.ApplyPower(first,second,true,true,(int)baseline.GetNamedNumber("rememberedPl2Watts"));
                Check(_releaseHome.PowerPreferenceWriteCount==preferences,"Unchanged Home readbacks rewrote confirmed preferences");
                foreach(bool pl2 in new[]{false,true})
                {
                    var slider=pl2?_releaseHome.Pl2Input:_releaseHome.PowerInput;int target=pl2?target2:target1,initial=pl2?second:first;long sent=_releaseHome.PowerRequestCount;
                    _releaseHome.VerifyPowerPointer(slider,true);slider.Value=target;await Task.Delay(200);Check(_releaseHome.PowerRequestCount==sent,"Held input wrote power");
                    expected1=pl2?first:target1;expected2=pl2?target2:second;
                    _releaseHome.VerifyPowerPointer(slider,false);await Settle(expected1,expected2);
                    Check(_releaseHome.PowerRequestCount==sent+1,"Release did not send once");Check(_releaseHome.PowerStatusText!=OnexStrings.Get("Applying…")&&Math.Abs(_releaseHome.PowerSection.ActualHeight-height)<0.5,"Home status/layout changed on successful release");
                    var actual=await ReadOnexStateAsync();Check(actual.GetNamedNumber("pl1Watts")==expected1&&actual.GetNamedNumber("pl2Watts")==expected2,"Fresh target ownership lost");
                    _releaseHome.VerifyPowerPointer(slider,true);slider.Value=initial;_releaseHome.VerifyPowerPointer(slider,false);await Settle(first,second);Check(_releaseHome.PowerRequestCount==sent+2,"Narrow restoration emitted extra write");expected1=first;expected2=second;
                }
                Check(dispatches==4&&quiet&&interactive,"Home showed Applying or disabled sliders during actual request");
                Check(_onexHomePowerWriteRpcs==writes+4&&_onexHomePowerFallbackReads==reads,"Success path made redundant postwrite read requests");
                report["homeSlidersVerified"]=JsonValue.CreateNumberValue(2);report["successfulDispatches"]=JsonValue.CreateNumberValue(dispatches);report["applyingShown"]=JsonValue.CreateBooleanValue(!quiet);report["inputsInteractiveInFlight"]=JsonValue.CreateBooleanValue(interactive);report["powerCardHeightStable"]=JsonValue.CreateBooleanValue(true);report["unchangedReadbacks"]=JsonValue.CreateNumberValue(1000);report["repeatedPreferenceWrites"]=JsonValue.CreateNumberValue(0);report["releaseWriteRpcs"]=JsonValue.CreateNumberValue(2);report["narrowRestoreWriteRpcs"]=JsonValue.CreateNumberValue(2);report["successFallbackPowerReads"]=JsonValue.CreateNumberValue(0);report["postWriteProfilesGets"]=JsonValue.CreateNumberValue(0);report["ackContainsVerifiedActualPower"]=JsonValue.CreateBooleanValue(true);report["physicalInputTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                _releaseHome.PowerRequested-=Dispatched;_releaseHome.CancelPendingPower();_releaseHome.VerifyPowerPointer(_releaseHome.PowerInput,false);_releaseHome.VerifyPowerPointer(_releaseHome.Pl2Input,false);
                for(int i=0;i<120&&_releaseHome.PowerRequestInFlight;i++)await Task.Delay(100);Check(!_releaseHome.PowerRequestInFlight,"Own request still running");var actual=await ReadOnexStateAsync();
                if(actual.GetNamedString("rawLimits")!=baseline.GetNamedString("rawLimits"))
                {
                    Check(scope==OnexPerformancePl2Scope&&actual.GetNamedNumber("pl1Watts")==expected1&&actual.GetNamedNumber("pl2Watts")==expected2,"Owner limits changed; no stale restore");ApplyOnexReleasePowerState(actual.Stringify());var slider=expected1!=first?_releaseHome.PowerInput:_releaseHome.Pl2Input;_releaseHome.VerifyPowerPointer(slider,true);slider.Value=expected1!=first?first:second;_releaseHome.VerifyPowerPointer(slider,false);await Settle(first,second);
                }
                ApplyOnexReleasePowerState((await ReadOnexStateAsync()).Stringify());Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==baseline.GetNamedString("rawLimits"),"Fresh baseline not restored");if(original?.Visibility==Visibility.Visible)original.IsChecked=true;report["checks"]=JsonValue.CreateNumberValue(checks);report["freshBaselineRestored"]=JsonValue.CreateBooleanValue(true);
            }
        }
        private async Task VerifyOnexPowerSliderParityAsync(JsonObject report)
        {
            // Real production controls/API; held pointer state is synthetic, not touch.
            EnsureOnexReleaseLayout();EnsureOnexPerformanceLayout();var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
            var baseline=await ReadOnexStateAsync();string profile=OnexFrontend.ProfileId,scope=OnexPerformancePl2Scope;
            int pl1=(int)baseline.GetNamedNumber("pl1Watts"),pl2=(int)baseline.GetNamedNumber("pl2Watts");
            int checks=0,totalSamples=0,staleChallenges=0,homeDispatches=0;bool interactiveInFlight=true;
            void Check(bool ok,string message){checks++;if(!ok)throw new InvalidOperationException(message);}
            Check(baseline.GetNamedBoolean("canWrite")&&baseline.GetNamedBoolean("separatePl2")&&pl1>8&&pl1<40&&pl2>pl1&&pl2<=41,"Fresh known PL2 On/manual-range baseline required");
            int nextPl1=pl1+1<pl2?pl1+1:pl1-1,nextPl2=pl2<41?pl2+1:pl2-1;
            Check(nextPl2>=pl1,"No conservative PL2 target");
            async Task Settle(int first,int second,bool home)
            {
                for(int i=0;i<120;i++)
                {
                    await Task.Delay(100);var actual=await ReadOnexStateAsync();
                    Check(OnexFrontend.ProfileId==profile&&OnexPerformancePl2Scope==scope,"Owner profile/supply changed; no stale restoration");
                    if(actual.GetNamedNumber("pl1Watts")==first&&actual.GetNamedNumber("pl2Watts")==second&&actual.GetNamedBoolean("separatePl2")&&(home?!_releaseHome.HasPendingPowerInput:!_onexPerformancePl1Pending&&!_onexPerformancePl1Busy))return;
                }
                throw new InvalidOperationException("Power gesture did not settle through production API");
            }
            void HomeDispatch(int first,int second)
            {homeDispatches++;interactiveInFlight&=_releaseHome.PowerInput.IsEnabled&&_releaseHome.Pl2Input.IsEnabled;}
            _releaseHome.PowerRequested+=HomeDispatch;
            try
            {
                _releasePowerNav.IsChecked=true;ApplyOnexReleasePowerState(baseline.Stringify());
                long firstCommits=_onexPerformancePl1Commits;
                OnexInputGuard.VerifyPointerHeld(TDPSlider,true);BeginOnexPerformancePl1Pointer();TDPSlider.Value=nextPl1;CancelOnexPerformancePl1Value();OnexInputGuard.VerifyPointerHeld(TDPSlider,false);
                await Task.Delay(200);Check(_onexPerformancePl1Commits==firstCommits&&(await ReadOnexStateAsync()).GetNamedString("rawLimits")==baseline.GetNamedString("rawLimits"),"Cancelled PL1 preview wrote power");ApplyOnexReleasePowerState(baseline.Stringify());
                RefreshOnexWattScale(TDPScaleLabels,TDPSlider);var labels=TDPScaleLabels.Children.ToArray();
                OnexInputGuard.VerifyPointerHeld(TDPSlider,true);BeginOnexPerformancePl1Pointer();
                for(int i=0;i<48;i++)
                {
                    int wanted=i%3==0?Math.Max(8,pl1-3):i%3==1?Math.Min(pl2-1,pl1+3):nextPl1;TDPSlider.Value=wanted;totalSamples++;
                    if(i%6==0){tdp.SetValueSilent(pl1);ApplyOnexReleasePowerState(baseline.Stringify());staleChallenges++;Check(TDPSlider.Value==wanted,"Old helper/readback replaced held Performance PL1");Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==baseline.GetNamedString("rawLimits"),"Held Performance PL1 wrote power");}
                    Check(TDPScaleLabels.Children.Count==labels.Length&&labels.Select((label,n)=>ReferenceEquals(label,TDPScaleLabels.Children[n])).All(x=>x),"PL1 scale recreated native labels");await Task.Delay(16);
                }
                Check(_onexPerformancePl1Commits==firstCommits&&TDPSlider.Value==nextPl1,"Held PL1 emitted a production RPC");
                OnexInputGuard.VerifyPointerHeld(TDPSlider,false);ApplyOnexReleasePowerState(baseline.Stringify());Check(TDPSlider.Value==nextPl1,"Released PL1 intent overwritten before ack");
                await Settle(nextPl1,pl2,false);Check(_onexPerformancePl1Commits==firstCommits+1,"PL1 release did not send exactly once");
                var live=await ReadOnexStateAsync();Check(live.GetNamedNumber("pl1Watts")==nextPl1&&live.GetNamedNumber("pl2Watts")==pl2,"PL1 target ownership lost");
                OnexInputGuard.VerifyPointerHeld(TDPSlider,true);BeginOnexPerformancePl1Pointer();TDPSlider.Value=pl1;OnexInputGuard.VerifyPointerHeld(TDPSlider,false);await Settle(pl1,pl2,false);
                Check(_onexPerformancePl1Commits==firstCommits+2,"PL1 narrow restore emitted unexpected writes");
                PerformanceNavItem.IsChecked=true;ApplyOnexReleasePowerState((await ReadOnexStateAsync()).Stringify());
                foreach(bool secondSlider in new[]{false,true})
                {
                    var slider=secondSlider?_releaseHome.Pl2Input:_releaseHome.PowerInput;int originalValue=secondSlider?pl2:pl1,target=secondSlider?nextPl2:nextPl1;
                    long sent=_releaseHome.PowerRequestCount;
                    _releaseHome.VerifyPowerPointer(slider,true);slider.Value=target;_releaseHome.CancelPendingPower();_releaseHome.VerifyPowerPointer(slider,false);
                    await Task.Delay(200);Check(_releaseHome.PowerRequestCount==sent&&(await ReadOnexStateAsync()).GetNamedString("rawLimits")==baseline.GetNamedString("rawLimits"),"Cancelled Home preview wrote power");ApplyOnexReleasePowerState(baseline.Stringify());
                    _releaseHome.VerifyPowerPointer(slider,true);
                    for(int i=0;i<48;i++)
                    {
                        int wanted=i%3==0?(secondSlider?Math.Max(pl1,pl2-3):Math.Max(8,pl1-3)):i%3==1?(secondSlider?Math.Min(41,pl2+3):Math.Min(pl2-1,pl1+3)):target;
                        slider.Value=wanted;totalSamples++;
                        if(i%6==0){ApplyOnexReleasePowerState(baseline.Stringify());staleChallenges++;Check(slider.Value==wanted,"Old readback replaced held Home thumb");Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==baseline.GetNamedString("rawLimits"),"Held Home slider wrote power");}
                        Check(_releaseHome.PowerRequestCount==sent&&slider.IsEnabled,"Home slider became blocked or wrote while held");await Task.Delay(16);
                    }
                    _releaseHome.VerifyPowerPointer(slider,false);ApplyOnexReleasePowerState(baseline.Stringify());Check(slider.Value==target,"Released Home intent overwritten before ack");
                    await Settle(secondSlider?pl1:nextPl1,secondSlider?nextPl2:pl2,true);Check(_releaseHome.PowerRequestCount==sent+1,"Home release did not send exactly once");
                    live=await ReadOnexStateAsync();Check(live.GetNamedNumber("pl1Watts")== (secondSlider?pl1:nextPl1)&&live.GetNamedNumber("pl2Watts")== (secondSlider?nextPl2:pl2),"Home target ownership lost");
                    _releaseHome.VerifyPowerPointer(slider,true);slider.Value=originalValue;_releaseHome.VerifyPowerPointer(slider,false);await Settle(pl1,pl2,true);Check(_releaseHome.PowerRequestCount==sent+2,"Home narrow restore emitted unexpected writes");
                }
                Check(homeDispatches==4&&interactiveInFlight,"Home disabled sliders during actual production request");
                var projection=new OnexReleaseHome(false);int syntheticWrites=0;projection.PowerRequested+=(a,b)=>syntheticWrites++;projection.ApplyPower(25,52,true,true,41);
                Check(projection.PowerInput.IsEnabled&&projection.Pl2Input.IsEnabled&&projection.PowerSection.Visibility==Visibility.Visible,"External OEM PL2 readback blocked Home power controls");
                Check(syntheticWrites==0,"Read-only external52 projection emitted a request");
                report["newSlidersVerified"]=JsonValue.CreateNumberValue(3);report["heldSamples"]=JsonValue.CreateNumberValue(totalSamples);report["heldProductionRpcs"]=JsonValue.CreateNumberValue(0);report["releasedProductionRpcs"]=JsonValue.CreateNumberValue(3);report["narrowRestoreProductionRpcs"]=JsonValue.CreateNumberValue(3);report["staleReadbackChallenges"]=JsonValue.CreateNumberValue(staleChallenges);report["performancePl1NativeScaleReused"]=JsonValue.CreateBooleanValue(true);report["homeSlidersInteractiveInFlight"]=JsonValue.CreateBooleanValue(interactiveInFlight);report["cancelledPreviewWrites"]=JsonValue.CreateNumberValue(0);report["external52ReadOnlyProjectionActionable"]=JsonValue.CreateBooleanValue(true);report["syntheticPointerStateNotPhysicalTouch"]=JsonValue.CreateBooleanValue(true);report["physicalInputTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                _releaseHome.PowerRequested-=HomeDispatch;OnexInputGuard.VerifyPointerHeld(TDPSlider,false);_releaseHome.VerifyPowerPointer(_releaseHome.PowerInput,false);_releaseHome.VerifyPowerPointer(_releaseHome.Pl2Input,false);CancelOnexPerformancePl1Value();_releaseHome.CancelPendingPower();
                for(int i=0;i<120&&(_onexPerformancePl1Busy||_releaseHome.PowerRequestInFlight);i++)await Task.Delay(100);
                Check(!_onexPerformancePl1Busy&&!_releaseHome.PowerRequestInFlight,"Own request still running; preserve operation for inspection");
                var live=await ReadOnexStateAsync();
                if(live.GetNamedString("rawLimits")!=baseline.GetNamedString("rawLimits"))
                {
                    Check(OnexFrontend.ProfileId==profile&&OnexPerformancePl2Scope==scope&&live.GetNamedBoolean("separatePl2")&&((live.GetNamedNumber("pl1Watts")==nextPl1&&live.GetNamedNumber("pl2Watts")==pl2)||(live.GetNamedNumber("pl1Watts")==pl1&&live.GetNamedNumber("pl2Watts")==nextPl2)),"Owner limits changed; no stale restore");
                    PerformanceNavItem.IsChecked=true;ApplyOnexReleasePowerState(live.Stringify());var restore=_releaseHome.PowerInput;
                    _releaseHome.VerifyPowerPointer(restore,true);restore.Value=pl1;_releaseHome.VerifyPowerPointer(restore,false);
                    if(live.GetNamedNumber("pl2Watts")!=pl2){restore=_releaseHome.Pl2Input;_releaseHome.VerifyPowerPointer(restore,true);restore.Value=pl2;_releaseHome.VerifyPowerPointer(restore,false);}
                    await Settle(pl1,pl2,true);
                }
                ApplyOnexReleasePowerState((await ReadOnexStateAsync()).Stringify());Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==baseline.GetNamedString("rawLimits"),"Fresh baseline not restored");if(original?.Visibility==Visibility.Visible)original.IsChecked=true;report["checks"]=JsonValue.CreateNumberValue(checks);report["freshBaselineRestored"]=JsonValue.CreateBooleanValue(true);
            }
        }
        private async Task VerifyOnexPowerAvailabilityReuseAsync(JsonObject report)
        {
            // Synthetic ready/not-ready snapshots only. Actual limits and the
            // owner's selected supply/profile are read, never written here.
            EnsureOnexReleaseLayout();var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
            _releasePowerNav.IsChecked=true;bool ready=OnexFrontend.PowerAvailable;var actual=await ReadOnexStateAsync();int checks=0;
            void Check(bool ok,string message){checks++;if(!ok)throw new InvalidOperationException(message);}
            Check(App.IsConnected&&ready&&!OnexSourceEditorsPending,"Protect owner power readiness/pending edit");
            try
            {
                ApplyOnexAvailability();long full=_onexFullAvailabilityApplies,reused=_onexPowerAvailabilityReuses;
                ApplyOnexPowerAvailability(false);
                Check(!TDPSlider.IsEnabled&&!TDPBoostToggle.IsEnabled&&!TDPBoostFPPTSliderCard.IsEnabled,"Not-ready snapshot did not disable actual power controls");
                ApplyOnexPowerAvailability(true);
                Check(TDPSlider.IsEnabled&&TDPBoostToggle.IsEnabled&&TDPBoostFPPTSliderCard.IsEnabled==TDPBoostToggle.IsOn,"Ready transition left actual power controls disabled");
                TDPSlider.IsEnabled=false;TDPBoostToggle.IsEnabled=false;TDPBoostFPPTSliderCard.IsEnabled=false;
                for(int i=0;i<1000;i++)ApplyOnexPowerAvailability(true);
                Check(TDPSlider.IsEnabled&&TDPBoostToggle.IsEnabled&&TDPBoostFPPTSliderCard.IsEnabled==TDPBoostToggle.IsOn,"Repeated ready snapshot did not reassert power controls");
                Check(_onexFullAvailabilityApplies==full+2&&_onexPowerAvailabilityReuses==reused+1000,"Unchanged availability repeated the global editor walk");
                var buffer=new OnexLatestStates();buffer.Put("OnexPowerAvailable","power");buffer.Put("OnexAirplaneState","radio");buffer.ResetAfterReadReply(new[]{"OnexAirplaneState","OnexWifiState"});
                Check(OnexLatestStates.IsScopedRead(new[]{"OnexAirplaneGet"})&&buffer.Take(4).Single().Key=="OnexPowerAvailable","Read-only radio status discarded unrelated power update");
                Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==actual.GetNamedString("rawLimits"),"UI availability diagnostic changed power");
                report["readyTransitions"]=JsonValue.CreateNumberValue(2);report["unchangedReadySnapshots"]=JsonValue.CreateNumberValue(1000);
                report["globalAvailabilityWalksForRepeatedReady"]=JsonValue.CreateNumberValue(0);report["actualPowerControlsReasserted"]=JsonValue.CreateBooleanValue(true);
                report["readOnlyReplyRetainsUnrelatedPower"]=JsonValue.CreateBooleanValue(true);report["powerWriteCommands"]=JsonValue.CreateNumberValue(0);
                report["syntheticReadyNotPhysicalInput"]=JsonValue.CreateBooleanValue(true);report["physicalInputTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                ApplyOnexPowerAvailability(ready);ApplyOnexReleasePowerState((await ReadOnexStateAsync()).Stringify());
                if(original?.Visibility==Visibility.Visible)original.IsChecked=true;report["checks"]=JsonValue.CreateNumberValue(checks);
            }
        }
        private async Task VerifyOnexSnapshotTilePaintAsync(JsonObject report)
        {
            // UI-only snapshot burst. AirplaneGet is read-only: no radio, network,
            // controller, power or owner-setting command is issued here.
            EnsureOnexReleaseLayout();var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
            bool previous=_onexApplyingStatePush;int checks=0;
            void Check(bool ok,string message){checks++;if(!ok)throw new InvalidOperationException(message);}
            async Task Refresh()
            {
                var reply=await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet{{"OnexAirplaneGet",true}});
                Check(reply?.ContainsKey("OnexAirplaneState")==true,"Read-only production radio status unavailable");
                ApplyOnexAirplaneState((string)reply["OnexAirplaneState"]);
                if(reply.TryGetValue("OnexWifiState",out var wifi))ApplyOnexWifiState((string)wifi);
            }
            try
            {
                PerformanceNavItem.IsChecked=true;await Refresh();await Task.Delay(100);
                string actual=_onexAirplaneState.Stringify();var tile=qsTileDefinitions.First(x=>x.Id=="OnexAirplane");
                long paints=_onexTileAvailabilityPaints,requests=_onexTileAvailabilityPaintRequests;
                _onexApplyingStatePush=true;
                for(int i=0;i<24;i++)
                {
                    var model=JsonObject.Parse(actual);model["qaSnapshotSequence"]=JsonValue.CreateNumberValue(i);model["available"]=JsonValue.CreateBooleanValue(i%2==0);
                    ApplyOnexAirplaneState(model.Stringify());
                    Check(_onexTileAvailabilityPaints==paints,"Snapshot burst repainted Home synchronously");
                    Check(tile.IsActionable==OnexAirplaneReady,"Logical action gating was deferred");
                }
                _onexApplyingStatePush=previous;
                Check(_onexAirplaneState.GetNamedNumber("qaSnapshotSequence")==23&&_onexTileAvailabilityPaintQueued,"Latest snapshot not retained or paint not scheduled");
                for(int i=0;i<100&&_onexTileAvailabilityPaints==paints;i++)await Task.Delay(2);
                Check(_onexTileAvailabilityPaints==paints+1&&!_onexTileAvailabilityPaintQueued,"Snapshot burst did not coalesce into one current paint");
                Check(tile.StateText.Text==OnexStrings.Get(!(_onexAirplaneState?.GetNamedBoolean("available",false)==true)?"Unavailable":OnexAirplaneOn?"On":"Off"),"Paint did not use current model");
                report["syntheticSnapshotModels"]=JsonValue.CreateNumberValue(24);
                report["immediateLogicalGatingChecks"]=JsonValue.CreateNumberValue(24);
                report["synchronousBurstPaints"]=JsonValue.CreateNumberValue(0);
                report["coalescedNativeHomePaints"]=JsonValue.CreateNumberValue(1);
                report["snapshotPaintRequests"]=JsonValue.CreateNumberValue(_onexTileAvailabilityPaintRequests-requests);
                report["radioOrPowerWriteCommands"]=JsonValue.CreateNumberValue(0);
                report["syntheticModelsNotPhysicalInput"]=JsonValue.CreateBooleanValue(true);
                report["physicalInputTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                _onexApplyingStatePush=previous;await Refresh();if(original?.Visibility==Visibility.Visible)original.IsChecked=true;
                report["checks"]=JsonValue.CreateNumberValue(checks);
            }
        }
        private async Task VerifyOnexHiddenControllerProjectionAsync(JsonObject report)
        {
            // Read-only production model plus synthetic runtime text. No controller,
            // listener, profile, driver or power command is sent by this diagnostic.
            EnsureOnexReleaseLayout();var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
            var actual=await ReadOnexControllerAsync();string editor=ControllerEditorId;int checks=0;
            void Check(bool ok,string message){checks++;if(!ok)throw new InvalidOperationException(message);}
            Check(_onexControllerPending==null&&!_onexControllerBusy,"Protect pending owner controller edits");
            try
            {
                LegionNavItem.IsChecked=true;ApplyOnexControllerState(actual.Stringify());
                Check(_onexControllerPicker!=null&&!_onexControllerUiPending,"Visible controller UI not current");
                _releasePowerNav.IsChecked=true;long renders=_onexControllerUiApplyCount,hidden=_onexControllerHiddenStateUpdates;
                for(int i=0;i<24;i++)
                {
                    var model=JsonObject.Parse(actual.Stringify());var runtime=model.GetNamedObject("runtime",null)??new JsonObject();
                    runtime["lastInput"]=JsonValue.CreateStringValue("QA-216-hidden-"+i);model["runtime"]=runtime;
                    ApplyOnexControllerState(model.Stringify());
                    Check(_onexControllerState.GetNamedObject("runtime").GetNamedString("lastInput")=="QA-216-hidden-"+i,"Fresh hidden controller model lost");
                    Check(_onexControllerUiApplyCount==renders&&_onexControllerUiPending,"Hidden editor projected during Performance interaction");
                }
                Check(_onexControllerHiddenStateUpdates==hidden+24&&ControllerEditorId==editor,"Hidden cache did not retain scope");
                LegionNavItem.IsChecked=true;
                Check(!_onexControllerUiPending&&_onexControllerUiApplyCount==renders+1,"Controller navigation did not flush exactly once");
                Check(_onexControllerLive.Text.Contains("QA-216-hidden-23"),"Visible controller UI did not show latest deferred data");
                var fresh=await ReadOnexControllerAsync();
                Check(ControllerEditorId==editor&&ControllerEditorPolicy!=null&&_onexControllerPending==null,"Read-only navigation changed controller editor");
                report["productionControllerReadRpcs"]=JsonValue.CreateNumberValue(3); // Includes the fresh finally read.
                report["syntheticRuntimeModels"]=JsonValue.CreateNumberValue(24);
                report["hiddenEditorProjections"]=JsonValue.CreateNumberValue(0);
                report["navigationFlushes"]=JsonValue.CreateNumberValue(1);
                report["freshRuntimeDataRetained"]=JsonValue.CreateBooleanValue(true);
                report["syntheticRuntimeTextNotPhysicalInput"]=JsonValue.CreateBooleanValue(true);
                report["controllerWriteCommands"]=JsonValue.CreateNumberValue(0);
                report["physicalInputTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                await ReadOnexControllerAsync();if(original?.Visibility==Visibility.Visible)original.IsChecked=true;
                report["checks"]=JsonValue.CreateNumberValue(checks);
            }
        }
        private async Task VerifyOnexPerformancePl2GestureAsync(JsonObject report)
        {
            // A controlled synthetic held-pointer state exercises the real handler,
            // timer, live RPC and readback gates. Physical touch remains separate.
            EnsureOnexReleaseLayout();EnsureOnexPerformanceLayout();_releasePowerNav.IsChecked=true;
            var slider=TDPBoostFPPTSliderCard;var before=await ReadOnexStateAsync();
            if(!before.GetNamedBoolean("canWrite")||!before.GetNamedBoolean("separatePl2"))throw new InvalidOperationException("Native215 requires current known writable PL2 On");
            int pl1=(int)before.GetNamedNumber("pl1Watts"),initial=(int)before.GetNamedNumber("pl2Watts");
            if(pl1<8||pl1>40||initial<pl1||initial>41)throw new InvalidOperationException("Exact fresh baseline outside manual range");
            int target=initial<41?initial+1:initial-1;if(target<pl1)throw new InvalidOperationException("No conservative PL2 delta available");
            string scope=OnexFrontend.ProfileId;var originalTab=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
            long commits=_onexPerformancePl2ValueCommits;int challenges=0,samples=0;bool ownsTarget=false;
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            async Task Settle(int watts)
            {
                for(int n=0;n<120;n++)
                {
                    await Task.Delay(100);var actual=await ReadOnexStateAsync();
                    Check(OnexFrontend.ProfileId==scope,"Owner profile changed during gesture");
                    if(actual.GetNamedNumber("pl1Watts")==pl1&&actual.GetNamedNumber("pl2Watts")==watts&&actual.GetNamedNumber("rememberedPl2Watts")==watts&&actual.GetNamedBoolean("separatePl2")&&!_onexPerformancePl2ValuePending&&!_onexPerformancePl2ValueBusy&&slider.Value==watts)return;
                }
                throw new InvalidOperationException("Final PL2 gesture did not settle");
            }
            try
            {
                await Task.Delay(200);Check(slider.IsEnabled,"Actual PL2 slider disabled");
                // Cancellation must drop a pending value without touching hardware.
                OnexInputGuard.VerifyPointerHeld(slider,true);BeginOnexPerformancePl2Pointer(slider);slider.Value=target;CancelOnexPerformancePl2Value();OnexInputGuard.VerifyPointerHeld(slider,false);
                await Task.Delay(250);Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==before.GetNamedString("rawLimits")&&_onexPerformancePl2ValueCommits==commits,"Cancelled preview wrote power");
                ApplyOnexReleasePowerState(before.Stringify());report["cancelledPreviewNoPowerWrite"]=JsonValue.CreateBooleanValue(true);
                RefreshOnexPerformancePl2Scale();var labels=TDPBoostScaleLabels.Children.ToArray();
                OnexInputGuard.VerifyPointerHeld(slider,true);BeginOnexPerformancePl2Pointer(slider);
                for(int i=0;i<48;i++)
                {
                    int desired=i%3==0?Math.Max(pl1,initial-4):i%3==1?Math.Min(41,initial+4):target;slider.Value=desired;samples++;
                    if(i%6==0)
                    {
                        tdpBoostFPPT.SetValueSilent(initial);ApplyOnexReleasePowerState(before.Stringify());challenges++;
                        Check(slider.Value==desired,"Stale helper value replaced a held thumb");
                        Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==before.GetNamedString("rawLimits"),"Held pointer caused an early hardware write");
                    }
                    Check(TDPBoostScaleLabels.Children.Count==labels.Length&&labels.Select((label,n)=>ReferenceEquals(label,TDPBoostScaleLabels.Children[n])).All(x=>x),"PL2 scale controls rebuilt during drag");
                    await Task.Delay(16);
                }
                Check(slider.Value==target&&_onexPerformancePl2ValueCommits==commits,"Held drag emitted a production RPC");
                report["heldSamples"]=JsonValue.CreateNumberValue(samples);report["heldProductionRpcs"]=JsonValue.CreateNumberValue(0);
                OnexInputGuard.VerifyPointerHeld(slider,false);ApplyOnexReleasePowerState(before.Stringify());challenges++;
                Check(slider.Value==target,"Pending release value replaced before its acknowledgment");
                await Settle(target);ownsTarget=true;
                Check(_onexPerformancePl2ValueCommits==commits+1,"A released drag emitted more than one RPC");
                report["releasedDragProductionRpcs"]=JsonValue.CreateNumberValue(1);report["actualTargetPl2"]=JsonValue.CreateNumberValue(target);
                report["latestIntentReadbackChallenges"]=JsonValue.CreateNumberValue(challenges);report["scaleNativeChildrenReused"]=JsonValue.CreateBooleanValue(true);
            }
            finally
            {
                OnexInputGuard.VerifyPointerHeld(slider,false);CancelOnexPerformancePl2Value();
                var live=await ReadOnexStateAsync();
                if(live.GetNamedString("rawLimits")!=before.GetNamedString("rawLimits"))
                {
                    if(OnexFrontend.ProfileId!=scope||live.GetNamedNumber("pl1Watts")!=pl1||live.GetNamedNumber("pl2Watts")!=target||!live.GetNamedBoolean("separatePl2"))throw new InvalidOperationException("Owner power changed; no stale gesture restoration");
                    OnexInputGuard.VerifyPointerHeld(slider,true);BeginOnexPerformancePl2Pointer(slider);slider.Value=initial;OnexInputGuard.VerifyPointerHeld(slider,false);await Settle(initial);
                }
                report["productionCommitDelta"]=JsonValue.CreateNumberValue(_onexPerformancePl2ValueCommits-commits);
                report["freshBaselineRestored"]=JsonValue.CreateBooleanValue((await ReadOnexStateAsync()).GetNamedString("rawLimits")==before.GetNamedString("rawLimits"));
                report["syntheticPointerStateNotPhysicalTouch"]=JsonValue.CreateBooleanValue(true);report["physicalInputTested"]=JsonValue.CreateBooleanValue(false);
                report["successfulTargetWasObserved"]=JsonValue.CreateBooleanValue(ownsTarget);
                if(originalTab?.Visibility==Visibility.Visible)originalTab.IsChecked=true;
            }
        }
        private async Task VerifyOnexPl2ProductionActionsAsync(JsonObject report)
        {
            // Diagnostic214 only. These are explicit programmatic production actions,
            // not a physical keyboard/controller test and not a detached factory.
            var cases=new JsonArray();report["cases"]=cases;
            report["physicalInputTested"]=JsonValue.CreateBooleanValue(false);
            report["productionHardwareActions"]=JsonValue.CreateBooleanValue(true);
            report["profileSnapshotReplayed"]=JsonValue.CreateBooleanValue(false);
            var originalTab=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
            EnsureOnexReleaseLayout();EnsureOnexPerformanceLayout();
            async Task<JsonObject> Profiles()
            {
                var reply=await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet{{"OnexProfilesGet",true}});
                return JsonObject.Parse((string)reply["OnexProfilesState"]);
            }
            JsonObject Global(JsonObject state)=>state.GetNamedArray("entries").Select(x=>x.GetObject()).Single(x=>x.GetNamedString("id")=="global");
            string OtherSettings(JsonObject state)
            {
                var clone=JsonObject.Parse(state.Stringify());clone.Remove("snapshot");clone.Remove("currentGame");clone.Remove("currentSession");
                var entry=Global(clone);entry["modifiedUtc"]=JsonValue.CreateStringValue("diagnostic-own-action-time");
                foreach(string side in new[]{"ac","dc","effectiveAc","effectiveDc"})
                {
                    var value=entry.GetNamedObject(side);value["pl1"]=JsonValue.CreateNumberValue(0);value["pl2"]=JsonValue.CreateNumberValue(0);value["separatePl2"]=JsonValue.CreateBooleanValue(false);
                }
                return clone.Stringify();
            }
            var initial=await ReadOnexStateAsync();var originalProfiles=await Profiles();
            if(originalProfiles.GetNamedString("activeId")!="global")throw new InvalidOperationException("Controlled214 requires current Global; owner selection retained");
            if(!initial.GetNamedBoolean("canWrite")||!initial.ContainsKey("separatePl2")||initial["separatePl2"].ValueType!=JsonValueType.Boolean||initial["rememberedPl2Watts"].ValueType!=JsonValueType.Number)throw new InvalidOperationException("Known writable selector required");
            int pl1=(int)initial.GetNamedNumber("pl1Watts"),pl2=(int)initial.GetNamedNumber("pl2Watts"),manual=(int)initial.GetNamedNumber("rememberedPl2Watts");
            bool initialSeparate=initial.GetNamedBoolean("separatePl2"),onAc=originalProfiles.GetNamedBoolean("onAc");
            if(pl1<8||pl1>40||pl2<pl1||pl2>41||manual<pl1||manual>41)throw new InvalidOperationException("Exact fresh baseline outside reversible manual range");
            string other=OtherSettings(originalProfiles),sideName=onAc&&Global(originalProfiles).GetNamedBoolean("split")?"ac":"dc";
            bool expectedSeparate=initialSeparate;int expectedPl2=pl2,requests=0;
            var homeToggle=(Windows.UI.Xaml.Controls.ToggleSwitch)_releaseHome.FindName("LightPl2Toggle");
            bool Matches(JsonObject p,bool separate,int watts)=>p.GetNamedNumber("pl1Watts")==pl1&&p.GetNamedNumber("pl2Watts")==watts&&p.GetNamedBoolean("separatePl2")==separate&&p.GetNamedNumber("rememberedPl2Watts")==manual;
            bool ProfileMatches(JsonObject p,bool separate)
            {
                if(p.GetNamedString("activeId")!="global"||p.GetNamedBoolean("onAc")!=onAc||OtherSettings(p)!=other)return false;
                var side=Global(p).GetNamedObject(sideName);return side.GetNamedNumber("pl1")==pl1&&side.GetNamedNumber("pl2")==manual&&side.GetNamedBoolean("separatePl2")==separate;
            }
            bool UiMatches(bool separate,int watts)=>homeToggle.IsOn==separate&&homeToggle.IsEnabled&&_releaseHome.PowerInput.IsEnabled&&TDPBoostToggle.IsOn==separate&&_releaseHome.RememberedPl2Watts==manual&&TDPSlider.Value==pl1&&TDPBoostFPPTSliderCard.Value==manual&&((Windows.UI.Xaml.Controls.TextBlock)_releaseHome.FindName("LightPowerValue")).Text==pl1.ToString()&&((Windows.UI.Xaml.Controls.TextBlock)_releaseHome.FindName("LightPl2Value")).Text==watts.ToString();
            async Task Wait(bool separate,int watts)
            {
                for(int n=0;n<100;n++)
                {
                    await Task.Delay(100);var p=await ReadOnexStateAsync();var catalog=await Profiles();
                    if(catalog.GetNamedString("activeId")!="global"||catalog.GetNamedBoolean("onAc")!=onAc||OtherSettings(catalog)!=other)throw new InvalidOperationException("Owner context changed; stop without stale restore");
                    if(Matches(p,separate,watts)&&ProfileMatches(catalog,separate)&&UiMatches(separate,watts))return;
                }
                throw new InvalidOperationException("PL2 production readback/profile/native UI did not converge");
            }
            async Task Action(string page)
            {
                var fresh=await ReadOnexStateAsync();var catalog=await Profiles();
                if(!Matches(fresh,expectedSeparate,expectedPl2)||!ProfileMatches(catalog,expectedSeparate))throw new InvalidOperationException("Fresh owner power/profile changed before action");
                if(page=="Home")PerformanceNavItem.IsChecked=true;else _releasePowerNav.IsChecked=true;
                await Task.Delay(100);
                bool target=!expectedSeparate;int watts=target?Math.Max(pl1,manual):Math.Min(41,pl1+1);
                if(!homeToggle.IsEnabled)throw new InvalidOperationException("Home PL2 is still busy/disabled before action");
                expectedSeparate=target;expectedPl2=watts;requests++;
                if(page=="Home")_releaseHome.VerifyInvokeActualPl2Input();
                else await SetOnexPerformancePl2EnabledAsync(target);
                await Wait(target,watts);
                cases.Add(new JsonObject{{"page",JsonValue.CreateStringValue(page)},{"separate",JsonValue.CreateBooleanValue(target)},{"pl1",JsonValue.CreateNumberValue(pl1)},{"actualPl2",JsonValue.CreateNumberValue(watts)},{"rememberedPl2",JsonValue.CreateNumberValue(manual)},{"homeAndPerformanceAndProfileAgree",JsonValue.CreateBooleanValue(true)},{"homeToggleEnabled",JsonValue.CreateBooleanValue(homeToggle.IsEnabled)},{"homeTdpEnabled",JsonValue.CreateBooleanValue(_releaseHome.PowerInput.IsEnabled)},{"physicalInput",JsonValue.CreateBooleanValue(false)}});
            }
            report["baseline"]=new JsonObject{{"pl1",JsonValue.CreateNumberValue(pl1)},{"pl2",JsonValue.CreateNumberValue(pl2)},{"manual",JsonValue.CreateNumberValue(manual)},{"separate",JsonValue.CreateBooleanValue(initialSeparate)},{"onAc",JsonValue.CreateBooleanValue(onAc)}};
            try
            {
                await Wait(initialSeparate,pl2);
                foreach(string page in new[]{"Home","Home","Performance","Performance"})await Action(page);
                if(!Matches(await ReadOnexStateAsync(),initialSeparate,pl2)||!ProfileMatches(await Profiles(),initialSeparate))throw new InvalidOperationException("Exact baseline not retained at end");
                report["fourProductionActionsPassed"]=JsonValue.CreateBooleanValue(true);
            }
            finally
            {
                var fresh=await ReadOnexStateAsync();var catalog=await Profiles();
                if(!Matches(fresh,initialSeparate,pl2))
                {
                    if(Matches(fresh,expectedSeparate,expectedPl2)&&ProfileMatches(catalog,expectedSeparate)&&expectedSeparate!=initialSeparate)
                    {
                        // Revert only this still-owned toggle through today's production
                        // handler. No profile/prefs snapshots are written back.
                        PerformanceNavItem.IsChecked=true;requests++;_releaseHome.VerifyInvokeActualPl2Input();await Wait(initialSeparate,pl2);
                        report["ownToggleRestoredAfterFailure"]=JsonValue.CreateBooleanValue(true);
                    }
                    else{report["ownerConflictPreserved"]=JsonValue.CreateBooleanValue(true);throw new InvalidOperationException("Changed owner state preserved; no stale restoration attempted");}
                }
                report["productionActionInvocations"]=JsonValue.CreateNumberValue(requests);
                report["exactInitialPowerAndSelectorRetained"]=JsonValue.CreateBooleanValue(Matches(await ReadOnexStateAsync(),initialSeparate,pl2));
                report["unrelatedProfilePropertiesRetained"]=JsonValue.CreateBooleanValue(OtherSettings(await Profiles())==other);
                if(originalTab?.Visibility==Visibility.Visible)originalTab.IsChecked=true;
            }
        }
        private async Task VerifyOnexHomePowerAvailabilityAsync(JsonObject report)
        {
            var before=await ReadOnexStateAsync();int pl1=(int)before.GetNamedNumber("pl1Watts"),pl2=(int)before.GetNamedNumber("pl2Watts");
            var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
            try
            {
                EnsureOnexReleaseLayout();PerformanceNavItem.IsChecked=true;await Task.Delay(50);UpdateLayout();
                bool available=App.IsConnected&&OnexFrontend.PowerAvailable;
                _releaseHome.ApplyPower(pl1,pl2,available);
                var slider=(Windows.UI.Xaml.Controls.Slider)_releaseHome.FindName("OnexPl2Slider");var toggle=(Windows.UI.Xaml.Controls.ToggleSwitch)_releaseHome.FindName("LightPl2Toggle");
                report["actualDevice"]=new JsonObject{{"pl1",JsonValue.CreateNumberValue(pl1)},{"pl2",JsonValue.CreateNumberValue(pl2)},{"powerAvailable",JsonValue.CreateBooleanValue(available)},{"homeTdpEnabled",JsonValue.CreateBooleanValue(_releaseHome.PowerInput.IsEnabled)},{"homePl2Enabled",JsonValue.CreateBooleanValue(slider.IsEnabled)},{"homeToggleEnabled",JsonValue.CreateBooleanValue(toggle.IsEnabled)}};
                if(!available||!_releaseHome.PowerInput.IsEnabled||!slider.IsEnabled||!toggle.IsEnabled)throw new InvalidOperationException("Current writable device left Home power controls disabled");
                if(((Windows.UI.Xaml.Controls.TextBlock)_releaseHome.FindName("LightPowerValue")).Text!=pl1.ToString()||((Windows.UI.Xaml.Controls.TextBlock)_releaseHome.FindName("LightPl2Value")).Text!=pl2.ToString())throw new InvalidOperationException("Actual device wattage hidden/clamped in readout");
                var fixture=new OnexReleaseHome(false);report["componentFixture"]=await fixture.VerifyExternalPowerReadbackAsync();
                var after=await ReadOnexStateAsync();if(before.GetNamedString("rawLimits")!=after.GetNamedString("rawLimits"))throw new InvalidOperationException("Readback/fixture wrote actual power");
                report["actualRawLimitsPreserved"]=JsonValue.CreateBooleanValue(true);report["powerCommandsRequested"]=JsonValue.CreateNumberValue(0);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
                report["fixtureRequestsCapturedWithoutHardwareBackend"]=JsonValue.CreateBooleanValue(true);
            }
            finally{if(original?.Visibility==Visibility.Visible)original.IsChecked=true;}
        }
        private async Task VerifyOnexPowerGuardAsync(JsonObject report)
        {
            var before=await ReadOnexStateAsync();
            TDPSlider.Value=8;
            TDPBoostFPPTSlider.Value=14;
            TDPBoostFPPTSliderCard.Value=14;
            _releaseHome.PowerInput.Value=8;
            ((Windows.UI.Xaml.Controls.Slider)_releaseHome.FindName("OnexPl2Slider")).Value=14;
            await Task.Delay(1300);
            var after=await ReadOnexStateAsync();
            if(before.GetNamedString("rawLimits")!=after.GetNamedString("rawLimits"))throw new InvalidOperationException("Programmatic slider change wrote TDP");
            _releasePl1=(int)after.GetNamedNumber("pl1Watts");_releasePl2=(int)after.GetNamedNumber("pl2Watts");
            _releaseHome.ApplyPower(_releasePl1,_releasePl2,true);
            properties.StopPendingUpdates();await properties.Sync();
            report["programmaticClampDidNotWrite"]=JsonValue.CreateBooleanValue(true);
            report["before"]=before;report["after"]=after;
            report["pl1Rect"]=RectJson(new SliderAutomationPeer(_releaseHome.PowerInput).GetBoundingRectangle());
            if(widget!=null)report["widgetBounds"]=RectJson(widget.WindowBounds);
            report["coreWindowBounds"]=RectJson(Window.Current.CoreWindow.Bounds);
            report["rawPixelsPerViewPixel"]=JsonValue.CreateNumberValue(Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel);
        }
    }
}

namespace XboxGamingBar
{
 public sealed partial class OnexReleaseHome
 {
  internal void VerifyInvokeActualPl2Input()=>TogglePl2FromInput();
  internal async Task<JsonObject> VerifyExternalPowerReadbackAsync()
  {
   if(persistPowerPreferences)throw new InvalidOperationException("Readonly factory required");
   var values=Windows.Storage.ApplicationData.Current.LocalSettings.Values;string[] keys={"OnexSeparatePl2","OnexRememberedPl2Watts"};var originals=keys.Select(k=>values.TryGetValue(k,out var value)?value:null).ToArray();int checks=0,calls=0,last1=0,last2=0;
   void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   var cases=new JsonArray();var requested=new JsonArray();Action<int,int> capture=(a,b)=>{calls++;last1=a;last2=b;};PowerRequested+=capture;
   try
   {
    foreach(var pair in new[]{new[]{25,52},new[]{25,100},new[]{8,8},new[]{40,41},new[]{25,26}})
    {
     rememberedPl2=0;ApplyPower(pair[0],pair[1],true,true);
     Check(LightPowerSlider.IsEnabled&&OnexPl2Slider.IsEnabled&&LightPl2Toggle.IsEnabled,"Readable power disabled controls");
     Check(LightPowerValue.Text==pair[0].ToString()&&LightPl2Value.Text==pair[1].ToString(),"Actual readout was clamped");
     int expectedMin=pair[0]==40?41:8,expectedMax=pair[0]==40?52:40;
     Check(LightPowerSlider.Maximum==40&&OnexPl2Slider.Minimum==expectedMin&&OnexPl2Slider.Maximum==expectedMax
         &&OnexPl2Slider.Value>=pair[0]+1&&OnexPl2Slider.Value<=expectedMax,"Manual slider contract changed");
     Check(rememberedPl2>=8&&rememberedPl2<=41,"Manual metadata invalid for Bridge contract");
     if(pair[1]>41)Check(OnexPowerStatus.Visibility==Visibility.Visible&&OnexPowerStatus.Text.Contains(pair[1].ToString()),"External/manual range distinction missing");
     cases.Add(new JsonObject{{"pl1",JsonValue.CreateNumberValue(pair[0])},{"pl2",JsonValue.CreateNumberValue(pair[1])},{"enabled",JsonValue.CreateBooleanValue(LightPowerSlider.IsEnabled)}});
    }
    ApplyPower(25,52,true,true);int buildsBefore=powerReadoutCacheBuilds;string textBefore=cachedPl2Summary,noticeBefore=cachedExternalPowerStatus;
    for(int repeat=0;repeat<1000;repeat++)ApplyPower(25,52,true,true);
    Check(powerReadoutCacheBuilds==buildsBefore&&object.ReferenceEquals(cachedPl2Summary,textBefore)&&object.ReferenceEquals(cachedExternalPowerStatus,noticeBefore),"Stable readback rebuilt formatted UI strings");
    ApplyPower(25,52,false);Check(!LightPowerSlider.IsEnabled&&!OnexPl2Slider.IsEnabled&&!LightPl2Toggle.IsEnabled,"Unavailable backend enabled writes");
    foreach(var pair in new[]{new[]{0,0},new[]{7,8},new[]{41,52},new[]{25,24}}){ApplyPower(pair[0],pair[1],true);Check(!LightPowerSlider.IsEnabled&&!OnexPl2Slider.IsEnabled&&!LightPl2Toggle.IsEnabled,"Invalid readback enabled controls");}
    foreach(bool separate in new[]{false,true})
    {
     rememberedPl2=0;ApplyPower(25,52,true,separate);int beforeCalls=calls;
     // Exercise the real component queue/commit timer with only our capture
     // subscriber. This is a synthetic request, not physical slider input.
     Queue(26,separate?Math.Max(26,desiredPl2):27);
     for(int n=0;n<100&&calls==beforeCalls;n++)await Task.Delay(20);
     Check(calls==beforeCalls+1&&last1==26&&last2==(separate?40:27),"Manual request not bounded/committed exactly once");
     Check(last1>=8&&last1<=40&&last2>last1&&last2<=40&&rememberedPl2>=8&&rememberedPl2<=41,"Unvalidated manual power request");
     requested.Add(new JsonObject{{"pl1",JsonValue.CreateNumberValue(last1)},{"pl2",JsonValue.CreateNumberValue(last2)},{"separate",JsonValue.CreateBooleanValue(separate)}});CompletePower(last1,last2,true,null);
     Check(LightPowerSlider.IsEnabled&&OnexPl2Slider.IsEnabled&&LightPl2Toggle.IsEnabled,"Commit completion left controls blocked");
    }
    for(int n=0;n<keys.Length;n++){var actual=values.TryGetValue(keys[n],out var v)?v:null;Check(object.Equals(actual,originals[n]),"Readonly factory altered owner preferences");}
    return new JsonObject{{"checks",JsonValue.CreateNumberValue(checks)},{"readbackCases",cases},{"capturedManualRequests",requested},{"capturedRequestCount",JsonValue.CreateNumberValue(calls)},{"ownerPreferenceWrites",JsonValue.CreateNumberValue(0)},{"hardwareBackendAttached",JsonValue.CreateBooleanValue(false)},{"stableReadbackRepetitions",JsonValue.CreateNumberValue(1000)},{"stableReadbackTextIdentityPreserved",JsonValue.CreateBooleanValue(true)}};
   }
   finally{timer.Stop();busy=false;PowerRequested-=capture;}
  }
 }
}
