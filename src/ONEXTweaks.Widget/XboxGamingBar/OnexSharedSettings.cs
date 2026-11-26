// SPDX-License-Identifier: AGPL-3.0-only
using System;
using ONEXTweaks.Power;
using System.Threading.Tasks;
using Shared.Enums;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using XboxGamingBar.Data;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private bool _onexPerformancePl2Busy,_onexPerformancePl2Requested;
        private async Task SetOnexPerformancePl2EnabledAsync(bool enabled)
        {
            if(_onexPerformancePl2Busy)return;
            _onexPerformancePl2Busy=true;_onexPerformancePl2Requested=enabled;
            JsonObject actual=null;
            try
            {
                var reply=await SendOnexHomeRequestAsync(new ValueSet{{"Command",1},{"Function",(int)Function.TDPBoostEnabled},{"Content",enabled},{"OnexPowerSource","performance-pl2-toggle"},{"OnexPowerOnAc",OnexPerformancePl2Scope.EndsWith(":ac",StringComparison.Ordinal)}});
                if(reply.TryGetValue("OnexState",out var state))ApplyOnexReleasePowerState((string)state);
            }
            catch(Exception error){ShowOnexDesktopStatus(error.Message);}
            finally
            {
                try{actual=await ReadOnexStateAsync();}catch(Exception error){Logger.Warn("PL2 readback: "+error.Message);}
                _onexPerformancePl2Busy=false;
                if(actual!=null)ApplyOnexReleasePowerState(actual.Stringify());
                ApplyOnexAvailability();
            }
        }
        internal void MarkOnexHomeProfileOverride(ValueSet request)
        {
            if(!request.ContainsKey("OnexPreferActiveProfile")&&Dispatcher.HasThreadAccess&&(_releaseHome?.Visibility==Windows.UI.Xaml.Visibility.Visible||QuickSettingsScrollViewer?.Visibility==Windows.UI.Xaml.Visibility.Visible)&&request.TryGetValue("OnexUserInput",out var input)&&input is bool direct&&direct)request["OnexPreferActiveProfile"]=true;
        }
        internal async Task ApplyOnexAcknowledgedSettingsAsync(IPC.NamedPipeClient owner,ValueSet request,ValueSet reply)
        {
            if(reply==null||reply.ContainsKey("Error")||!request.TryGetValue("OnexUserInput",out var input)||!(input is bool explicitInput)||!explicitInput)return;
            bool write=request.ContainsKey("OnexOverlayPresetsSet")||request.ContainsKey("OnexFanProfilesSet")||request.ContainsKey("OnexDisplayProfilesSet")||request.ContainsKey("OnexCpuSettings")||request.ContainsKey("OnexCpuTuningSet")||request.ContainsKey("OnexCpuClockSet")||request.ContainsKey("OnexBoostSet")||request.ContainsKey("OnexWindowsPowerMode")||request.ContainsKey("OnexFpsSet")||request.ContainsKey("OnexProfilesSet")||request.ContainsKey("OnexFanSet")||request.ContainsKey("OnexOverlayLevel");
            if(request.TryGetValue("Command",out var command)&&Convert.ToInt32(command)==1&&request.TryGetValue("Function",out var function))
            {var value=(Function)Convert.ToInt32(function);write|=value==Function.TDP||value==Function.TDPBoostEnabled||value==Function.TDPBoostFPPT||value==Function.OSDConfig;}
            if(!write)return;
            void Apply()
            {
                if(request.TryGetValue("Command",out var powerCommand)&&Convert.ToInt32(powerCommand)==1&&request.TryGetValue("Function",out var powerFunction))
                {var changed=(Function)Convert.ToInt32(powerFunction);string source=request.TryGetValue("OnexPowerSource",out var origin)?origin as string:null;bool deferred=source=="performance-pl1-slider"||source=="performance-pl2-slider"||source=="home-slider";if(!deferred&&(changed==Function.TDP||changed==Function.TDPBoostEnabled||changed==Function.TDPBoostFPPT)){CancelOnexPerformancePl2Value();CancelOnexPerformancePl1Value();_releaseHome?.CancelPendingPower();}}
                bool batchHome=request.TryGetValue("OnexPowerSource",out var batchSource)&&batchSource as string=="home-slider";
                long paints=_onexTileAvailabilityPaints;
                if(batchHome){_onexHomePowerAckPaintDepth++;_onexHomePowerAckBatches++;}
                try
                {
                if(reply.TryGetValue("OnexProfilesState",out var selectedProfiles)&&selectedProfiles is string selectedState)ApplyOnexProfilesState(selectedState);
                if(reply.TryGetValue("OnexState",out var power)&&power is string p)ApplyOnexReleasePowerState(p);
                if(reply.TryGetValue("OnexWindowsPowerState",out var windows)&&windows is string w)ApplyOnexWindowsPowerState(w);
                if(reply.TryGetValue("OnexBoostState",out var boost)&&boost is string b)ApplyOnexBoostState(b);
                if(reply.TryGetValue("OnexFpsState",out var fps)&&fps is string f)ApplyOnexFpsState(f);
                if(reply.TryGetValue("OnexHardwareHomeState",out var home)&&home is string hs)ApplyOnexHardwareHomeState(hs);
                if(reply.TryGetValue("OnexFanProfilesState",out var fans)&&fans is string fc)ApplyOnexFanProfilesState(fc);
                if(reply.ContainsKey("OnexDisplayProfilesState")||reply.ContainsKey("OnexDisplayState"))ApplyOnexDisplayReadReply(reply);
                if(reply.TryGetValue("OnexOverlayState",out var overlay)&&overlay is string os)ApplyOnexOverlayState(os);
                if(reply.TryGetValue("OnexOverlayPresetsState",out var presets)&&presets is string op)ApplyOnexOverlayPresetsState(op);
                RefreshOnexProfileSummaries();
                }
                finally
                {
                    if(batchHome){_onexHomePowerAckPaintDepth--;_onexHomePowerAckSynchronousPaints+=_onexTileAvailabilityPaints-paints;}
                }
            }
            void ApplyForOwner()=>TryRunForOnexConnection(owner,Apply);
            if(Dispatcher.HasThreadAccess)ApplyForOwner();else await Dispatcher.RunAsync(CoreDispatcherPriority.Normal,ApplyForOwner);
        }
        private void SyncOnexPerformancePowerState(int pl1,int pl2,bool? separate,int? remembered)
        {
            if(!Pl2ControlPolicy.IsWritable(pl1,pl2)&&!Pl2ControlPolicy.IsFactoryReadback(pl1,pl2))return;
            bool prior=isApplyingHelperUpdate,priorSlider=_syncingBoostSlider;isApplyingHelperUpdate=true;_syncingBoostSlider=true;WidgetSliderProperty.HelperSyncCount++;
            try
            {
                if(CanApplyOnexPerformancePl1Readback(pl1)&&tdp?.HasPendingUserValue!=true)
                {tdp?.SetValueSilent(pl1);TDPSlider.Value=pl1;}
                if(separate.HasValue&&(_onexPerformancePl2Busy?separate.Value==_onexPerformancePl2Requested:!OnexInputGuard.IsActive(TDPBoostToggle)))
                {tdpBoostEnabled?.SetValueSilent(separate.Value);TDPBoostToggle.IsOn=separate.Value;ApplyTdpBoostSliderVisuals();}
                int controlPl1 = (int)Math.Round(TDPSlider.Value);
                int displayed = Pl2ControlPolicy.AdjustManual(controlPl1, remembered ?? pl2);
                if(CanApplyOnexPerformancePl2Readback(displayed))
                {
                    tdpBoostFPPT?.SetValueSilent(displayed);
                    if(TDPBoostFPPTSliderCard!=null)
                    {
                        TDPBoostFPPTSliderCard.Maximum=Pl2ControlPolicy.ManualCeiling(controlPl1);
                        TDPBoostFPPTSliderCard.Minimum=controlPl1;
                    }
                    if(TDPBoostFPPTSlider!=null)TDPBoostFPPTSlider.Maximum=Pl2ControlPolicy.ManualCeiling(controlPl1);
                    if(TDPBoostFPPTSliderCard!=null)TDPBoostFPPTSliderCard.Value=displayed;
                    if(TDPBoostFPPTSlider!=null)TDPBoostFPPTSlider.Value=displayed;
                    if(TDPBoostFPPTValue!=null)TDPBoostFPPTValue.Text=displayed+global::XboxGamingBar.OnexStrings.Get("W");
                    if(TDPBoostFPPTValueInCard!=null)TDPBoostFPPTValueInCard.Text=displayed+global::XboxGamingBar.OnexStrings.Get("W");
                    RefreshOnexPerformancePowerScale();
                }
            }
            finally{WidgetSliderProperty.HelperSyncCount--;_syncingBoostSlider=priorSlider;isApplyingHelperUpdate=prior;}
        }
        private void CommitOnexPerformancePl2(Slider slider)
        {
            if(isApplyingHelperUpdate||_syncingBoostSlider||WidgetSliderProperty.HelperSyncCount>0)return;
            QueueOnexPerformancePl2Value(slider);
        }
        private Windows.UI.Xaml.DispatcherTimer _onexPerformancePl2ValueTimer;
        private bool _onexPerformancePl2ValuePending,_onexPerformancePl2ValueBusy,_onexPerformancePl2PointerCancelled;
        private int _onexPerformancePl2PendingValue,_onexPerformancePl2RequestedValue;
        private string _onexPerformancePl2PendingProfile,_onexPerformancePl2PointerProfile,_onexPerformancePl2PendingScope,_onexPerformancePl2PointerScope;
        private long _onexPerformancePl2ValueCommits;
        private string OnexPerformancePl2Scope=>OnexFrontend.ProfileId+":"+(_onexProfilesState?.GetNamedBoolean("onAc",false)==true?"ac":"dc");
        private bool OnexPerformancePl2Held=>OnexInputGuard.IsEditing(TDPBoostFPPTSliderCard)||OnexInputGuard.IsEditing(TDPBoostFPPTSlider);
        private bool CanApplyOnexPerformancePl2Readback(int value)
        {
            // Acknowledgments for earlier samples must not replace the latest thumb
            // position, including the gap between pointer-up and the final RPC reply.
            if(_onexPerformancePl2PointerCancelled&&(OnexInputGuard.IsPointerActive(TDPBoostFPPTSliderCard)||OnexInputGuard.IsPointerActive(TDPBoostFPPTSlider))&&!_onexPerformancePl2ValuePending&&!_onexPerformancePl2ValueBusy)return true;
            return !OnexPerformancePl2Held&&!_onexPerformancePl2ValuePending&&(!_onexPerformancePl2ValueBusy||value==_onexPerformancePl2RequestedValue);
        }
        private void BeginOnexPerformancePl2Pointer(Slider slider)
        {
            _onexPerformancePl2PointerCancelled=false;_onexPerformancePl2PointerProfile=OnexFrontend.ProfileId;_onexPerformancePl2PointerScope=OnexPerformancePl2Scope;
            CommitOnexPerformancePl2(slider); // Capture a first track tap even when ValueChanged preceded PointerPressed.
        }
        private void QueueOnexPerformancePl2Value(Slider slider)
        {
            if(slider==null||!slider.IsEnabled||!App.IsConnected||TDPBoostToggle?.IsOn!=true)return;
            bool pointer=OnexInputGuard.IsPointerActive(slider);
            if(pointer&&(_onexPerformancePl2PointerCancelled||_onexPerformancePl2PointerProfile!=OnexFrontend.ProfileId||_onexPerformancePl2PointerScope!=OnexPerformancePl2Scope))return;
            CancelOnexPerformancePl1Value();_releaseHome?.CancelPendingPower();
            _onexPerformancePl2PendingValue=Pl2ControlPolicy.AdjustManual((int)Math.Round(TDPSlider.Value),(int)Math.Round(slider.Value));
            _onexPerformancePl2PendingProfile=pointer?_onexPerformancePl2PointerProfile:OnexFrontend.ProfileId;
            _onexPerformancePl2PendingScope=pointer?_onexPerformancePl2PointerScope:OnexPerformancePl2Scope;
            _onexPerformancePl2ValuePending=true;
            if(_onexPerformancePl2ValueTimer==null)
            {
                _onexPerformancePl2ValueTimer=new Windows.UI.Xaml.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
                _onexPerformancePl2ValueTimer.Tick+=async(s,e)=>await FlushOnexPerformancePl2ValueAsync();
            }
            _onexPerformancePl2ValueTimer.Stop();_onexPerformancePl2ValueTimer.Start();
        }
        private void CancelOnexPerformancePl2Value()
        {
            _onexPerformancePl2ValueTimer?.Stop();_onexPerformancePl2ValuePending=false;_onexPerformancePl2PointerCancelled=true;
        }
        private async Task FlushOnexPerformancePl2ValueAsync()
        {
            _onexPerformancePl2ValueTimer?.Stop();
            if(!_onexPerformancePl2ValuePending)return;
            if(OnexPerformancePl2Held||_onexPerformancePl2ValueBusy||_onexPerformancePl1Busy||_releaseHome?.PowerRequestInFlight==true){_onexPerformancePl2ValueTimer.Start();return;}
            if(!App.IsConnected||_onexPerformancePl2PendingProfile!=OnexFrontend.ProfileId||_onexPerformancePl2PendingScope!=OnexPerformancePl2Scope||TDPBoostToggle?.IsOn!=true){CancelOnexPerformancePl2Value();return;}
            int value=_onexPerformancePl2PendingValue;string profile=_onexPerformancePl2PendingProfile,scope=_onexPerformancePl2PendingScope;
            _onexPerformancePl2ValuePending=false;
            if(tdpBoostFPPT?.Value==value&&_releasePl2==value)return;
            _onexPerformancePl2ValueBusy=true;_onexPerformancePl2RequestedValue=value;
            try
            {
                // Preserve the Performance slider's original shared-policy semantics.
                // Its scope belongs to the gesture, not whichever page is now visible.
                var request=new ValueSet{{"Command",1},{"Function",(int)Function.TDPBoostFPPT},{"Content",value},{"OnexUserInput",true},{"OnexProfileId",profile},{"OnexPreferActiveProfile",false},{"OnexPowerSource","performance-pl2-slider"},{"OnexPowerOnAc",scope.EndsWith(":ac",StringComparison.Ordinal)}};
                _onexPerformancePl2ValueCommits++;
                var reply=await App.SendMessageAsync(request);
                if(reply==null)throw new InvalidOperationException("ONEXTweaks disconnected");
                if(reply.TryGetValue("Error",out var error))throw new InvalidOperationException(error.ToString());
                if(reply.TryGetValue("OnexState",out var state))ApplyOnexReleasePowerState((string)state);
                if(scope==OnexPerformancePl2Scope)Windows.Storage.ApplicationData.Current.LocalSettings.Values["TDPBoostFPPT"]=value;
            }
            catch(Exception error){ShowOnexDesktopStatus(error.Message);}
            finally
            {
                JsonObject actual=null;try{actual=await ReadOnexStateAsync();}catch(Exception error){Logger.Warn("PL2 gesture readback: "+error.Message);}
                _onexPerformancePl2ValueBusy=false;
                if(actual!=null)ApplyOnexReleasePowerState(actual.Stringify());
                if(_onexPerformancePl2ValuePending)_onexPerformancePl2ValueTimer.Start();
            }
        }
        private OnexPowerScale _onexPerformancePowerScale;

        private void RefreshOnexPerformancePowerScale()
        {
            if(!OnexFrontend.UsesOwnBackend||OnexPerformancePl1Track==null||OnexPerformancePl2Track==null||
                TDPSlider==null||TDPBoostFPPTSliderCard==null||TDPBoostToggle==null)return;
            if(_onexPerformancePowerScale==null)
                _onexPerformancePowerScale=new OnexPowerScale(OnexPerformancePl1Track,TDPSlider,
                    OnexPerformancePl2Track,TDPBoostFPPTSliderCard,12,(Windows.UI.Xaml.Media.Brush)Resources["OnexInactiveSliderTrackBrush"]);
            _onexPerformancePowerScale.Update(TDPBoostToggle.IsOn);
            RefreshOnexWattScale(TDPScaleLabels,TDPSlider);
            RefreshOnexPerformancePl2Scale();
        }

        private void PreviewOnexPerformancePl2Range()
        {
            if(isApplyingHelperUpdate||_syncingBoostSlider||!OnexInputGuard.IsEditing(TDPSlider)||
                TDPBoostToggle?.IsOn!=true||TDPBoostFPPTSliderCard==null)return;
            int pl1=(int)Math.Round(TDPSlider.Value);
            int manual=tdpBoostFPPT?.Value??(int)Math.Round(TDPBoostFPPTSliderCard.Value);
            _syncingBoostSlider=true;
            try
            {
                TDPBoostFPPTSliderCard.Maximum=Pl2ControlPolicy.ManualCeiling(pl1);
                TDPBoostFPPTSliderCard.Minimum=pl1;
                TDPBoostFPPTSliderCard.Value=Pl2ControlPolicy.AdjustManual(pl1,manual);
                TDPBoostFPPTValueInCard.Text=OnexStrings.Format("{0}W",(int)TDPBoostFPPTSliderCard.Value);
            }
            finally{_syncingBoostSlider=false;}
            RefreshOnexPerformancePowerScale();
        }

        private OnexPowerScale OnexScaleFor(Slider slider)
            => ReferenceEquals(slider,TDPSlider)||ReferenceEquals(slider,TDPBoostFPPTSliderCard)?_onexPerformancePowerScale:null;

        private void RefreshOnexPerformancePl2Scale()=>RefreshOnexWattScale(TDPBoostScaleLabels,TDPBoostFPPTSliderCard);
        private void RefreshOnexWattScale(Windows.UI.Xaml.Controls.Canvas canvas,Slider slider)
        {
            // Dragging changes the highlight, not the scale's structure. Rebuilding
            // every TextBlock/brush per watt also causes needless native layout work.
            if(canvas==null||slider==null)return;
            var scale=OnexScaleFor(slider);
            int lo=(int)Math.Round(scale?.Minimum??slider.Minimum),hi=(int)Math.Round(scale?.Maximum??slider.Maximum);
            int count=2+Math.Max(0,(hi-1)/5-lo/5);
            bool valid=hi>lo&&canvas.Children.Count==count;int oldActive=-1,nextActive=-1,best=int.MaxValue;
            Windows.UI.Xaml.Media.Brush accent=null,dim=null;
            if(valid)for(int i=0;i<count;i++)
            {
                var label=canvas.Children[i] as Windows.UI.Xaml.Controls.TextBlock;int expected=i==0?lo:i==count-1?hi:(lo/5+i)*5;
                if(label==null||!(label.Tag is int watts)||watts!=expected){valid=false;break;}
                if(label.FontWeight.Weight==Windows.UI.Text.FontWeights.SemiBold.Weight){oldActive=i;accent=label.Foreground;}else dim=label.Foreground;
                int distance=Math.Abs(watts-(int)Math.Round(slider.Value));if(distance<best){best=distance;nextActive=i;}
            }
            if(!valid||accent==null||dim==null||!slider.IsEnabled){BuildWattScaleLabels(canvas,slider);return;}
            if(oldActive==nextActive)return;
            for(int i=0;i<count;i++)
            {
                var label=(Windows.UI.Xaml.Controls.TextBlock)canvas.Children[i];
                label.Foreground=i==nextActive?accent:dim;label.FontWeight=i==nextActive?Windows.UI.Text.FontWeights.SemiBold:Windows.UI.Text.FontWeights.Normal;
            }
            PositionWattScaleLabels(canvas,slider);
        }
        private Windows.UI.Xaml.DispatcherTimer _onexPerformancePl1Timer;
        private bool _onexPerformancePl1Pending,_onexPerformancePl1Busy,_onexPerformancePl1PointerCancelled;
        private int _onexPerformancePl1Value,_onexPerformancePl1Requested;
        private string _onexPerformancePl1Scope,_onexPerformancePl1Profile,_onexPerformancePl1PointerScope;
        private long _onexPerformancePl1Commits;
        private bool CanApplyOnexPerformancePl1Readback(int value)
        {
            if(_onexPerformancePl1PointerCancelled&&OnexInputGuard.IsPointerActive(TDPSlider)&&!_onexPerformancePl1Pending&&!_onexPerformancePl1Busy)return true;
            return !OnexInputGuard.IsEditing(TDPSlider)&&!_onexPerformancePl1Pending&&(!_onexPerformancePl1Busy||value==_onexPerformancePl1Requested);
        }
        private void BeginOnexPerformancePl1Pointer()
        {
            _onexPerformancePl1PointerCancelled=false;_onexPerformancePl1PointerScope=OnexPerformancePl2Scope;QueueOnexPerformancePl1Value();
        }
        private void CancelOnexPerformancePl1Value()
        {_onexPerformancePl1Timer?.Stop();_onexPerformancePl1Pending=false;_onexPerformancePl1PointerCancelled=true;tdp?.StopDebounceTimer();}
        private void QueueOnexPerformancePl1Value()
        {
            if(isApplyingHelperUpdate||_syncingBoostSlider||WidgetSliderProperty.HelperSyncCount>0||!App.IsConnected||TDPSlider?.IsEnabled!=true)return;
            bool pointer=OnexInputGuard.IsPointerActive(TDPSlider);
            if(pointer&&(_onexPerformancePl1PointerCancelled||_onexPerformancePl1PointerScope!=OnexPerformancePl2Scope))return;
            tdp?.StopDebounceTimer();CancelOnexPerformancePl2Value();_releaseHome?.CancelPendingPower();
            _onexPerformancePl1Value=Math.Max(8,Math.Min(40,(int)Math.Round(TDPSlider.Value)));
            _onexPerformancePl1Profile=OnexFrontend.ProfileId;_onexPerformancePl1Scope=pointer?_onexPerformancePl1PointerScope:OnexPerformancePl2Scope;_onexPerformancePl1Pending=true;
            if(_onexPerformancePl1Timer==null)
            {_onexPerformancePl1Timer=new Windows.UI.Xaml.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};_onexPerformancePl1Timer.Tick+=async(s,e)=>await FlushOnexPerformancePl1Async();}
            _onexPerformancePl1Timer.Stop();_onexPerformancePl1Timer.Start();
        }
        private async Task FlushOnexPerformancePl1Async()
        {
            _onexPerformancePl1Timer.Stop();if(!_onexPerformancePl1Pending)return;
            if(OnexInputGuard.IsEditing(TDPSlider)||_onexPerformancePl1Busy||_onexPerformancePl2ValueBusy||_releaseHome?.PowerRequestInFlight==true){_onexPerformancePl1Timer.Start();return;}
            if(!App.IsConnected||_onexPerformancePl1Profile!=OnexFrontend.ProfileId||_onexPerformancePl1Scope!=OnexPerformancePl2Scope){CancelOnexPerformancePl1Value();return;}
            int value=_onexPerformancePl1Value;string profile=_onexPerformancePl1Profile;_onexPerformancePl1Pending=false;
            if(tdp?.Value==value&&_releasePl1==value)return;
            _onexPerformancePl1Busy=true;_onexPerformancePl1Requested=value;_onexPerformancePl1Commits++;
            try
            {
                var reply=await App.SendMessageAsync(new ValueSet{{"Command",1},{"Function",(int)Function.TDP},{"Content",value},{"OnexUserInput",true},{"OnexProfileId",profile},{"OnexPreferActiveProfile",false},{"OnexPowerSource","performance-pl1-slider"},{"OnexPowerOnAc",_onexPerformancePl1Scope.EndsWith(":ac",StringComparison.Ordinal)}});
                if(reply==null||reply.ContainsKey("Error"))throw new InvalidOperationException(reply?.ContainsKey("Error")==true?reply["Error"].ToString():"ONEXTweaks disconnected");
                if(reply.TryGetValue("OnexState",out var power))ApplyOnexReleasePowerState((string)power);
            }
            catch(Exception error){ShowOnexDesktopStatus(error.Message);}
            finally
            {
                JsonObject actual=null;try{actual=await ReadOnexStateAsync();}catch(Exception error){Logger.Warn("PL1 gesture readback: "+error.Message);}
                _onexPerformancePl1Busy=false;if(actual!=null)ApplyOnexReleasePowerState(actual.Stringify());
                if(_onexPerformancePl1Pending)_onexPerformancePl1Timer.Start();
            }
        }
        private void SyncOnexBoostFromWindowsPower()
        {
            var cpu=_onexWindowsPower?.GetNamedObject("cpu",null);
            if(!OnexWindowsPowerReady||cpu==null)return;
            bool onAc=_onexWindowsPower.GetNamedBoolean("onAc");
            var state=new JsonObject{{"available",JsonValue.CreateBooleanValue(true)},{"scheme",cpu.GetNamedValue("scheme")},{"acMode",cpu.GetNamedValue("boostAc")},{"dcMode",cpu.GetNamedValue("boostDc")},{"onAc",JsonValue.CreateBooleanValue(onAc)},{"enabled",JsonValue.CreateBooleanValue(cpu.GetNamedNumber(onAc?"boostAc":"boostDc")>0)}};
            ApplyOnexBoostState(state.Stringify());
        }
        private void SyncOnexCpuFormFromBoost(JsonObject state)
        {
            var cpu=_onexWindowsPower?.GetNamedObject("cpu",null);
            if(cpu==null||cpu.GetNamedString("scheme","")!=state.GetNamedString("scheme",""))return;
            cpu["boostAc"]=state.GetNamedValue("acMode");cpu["boostDc"]=state.GetNamedValue("dcMode");
            _onexPerformanceCpuForm?.Refresh?.Invoke(cpu);
        }
    }
}
