// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XboxGamingBar.IPC;
using Windows.UI.Core;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private static readonly Regex OnexSnapshotKey=new Regex(@"(?:^|[,{])\s*""(?<key>OnexKeyboardMouseState|OnexControllerState|OnexStartupState|OnexProfilesState|OnexDisplayProfilesState|OnexFanProfilesState|OnexWifiState|OnexEnergySaverState|OnexAirplaneState|OnexAwakeState|OnexFseState|OnexDisplayState|OnexFpsState|OnexOverlayState|OnexWindowsPowerState|OnexHardwareHomeState|OnexBoostState|OnexChargeState|OnexDesktopState|MediaLevels|OnexPowerAvailable)""\s*:");
        private readonly OnexLatestStates _onexStatePushes=new OnexLatestStates();
        private readonly object _onexPushScheduleGate=new object();
        private bool _onexPushScheduled,_onexPushVisible=true;
        private NamedPipeClient _onexStatePushOwner;
        private NamedPipeClient _onexProfileEditOwner;
        private long _onexPushApplied;

        internal bool AcceptsOnexProfileRequest(NamedPipeClient owner, Windows.Foundation.Collections.ValueSet request)
        {
            if(ReferenceEquals(Volatile.Read(ref _onexProfileEditOwner),owner))return true;
            if (OnexProfileWriteGate.RequiresProfileSnapshot(request.Keys)) return false;
            if(request.TryGetValue("Command",out var command)&&Convert.ToInt32(command)==1&&request.TryGetValue("Function",out var id))
            {
                var function=(Shared.Enums.Function)Convert.ToInt32(id);
                if(function==Shared.Enums.Function.TDP||function==Shared.Enums.Function.TDPBoostEnabled||
                   function==Shared.Enums.Function.TDPBoostFPPT)return false;
            }
            return true;
        }

        private void ResetOnexProfileEdits()
        {
            Volatile.Write(ref _onexProfileEditOwner,null);
            CancelOnexPerformanceScopeEdits();
            CancelOnexDisplayProfileEdits();
            CancelControllerProfileEdits();
            _onexOverlayCommandError = null;
            foreach(var form in OnexFanForms)
            {
                form.Timer.Stop();
                form.Draft=null;
                form.CustomPoints=null;
                foreach(var slider in form.Sliders)OnexInputGuard.CancelInteraction(slider);
            }
            bool previousLoading=isLoadingOSDConfig;
            isLoadingOSDConfig=true;
            try
            {
                _onexOverlayConfigReady=false;
                osdBackgroundDebounceTimer?.Stop();
                OnexInputGuard.CancelInteraction(OSDOpacitySlider);
                OnexInputGuard.CancelInteraction(OSDBackgroundOpacitySlider);
            }
            finally { isLoadingOSDConfig=previousLoading; }
        }

        private static void RequireOnexProfileConnection(NamedPipeClient owner)
        {
            if(!App.IsCurrentConnectedPipeClient(owner))throw new InvalidOperationException("ONEXTweaks reconnected. Repeat the profile adjustment.");
        }
        private bool QueueOnexStatePush(NamedPipeClient owner, string raw)
        {
            var match = OnexSnapshotKey.Match(raw);
            if (!match.Success) return false;
            if (TryRunForPipePush(owner, () =>
            {
                SelectOnexStatePushOwner(owner);
                _onexStatePushes.Put(match.Groups["key"].Value, raw);
            })) ScheduleOnexStatePushes();
            return true;
        }

        private void SelectOnexStatePushOwner(NamedPipeClient owner)
        {
            // The caller holds the pipe publication gate. Drop the previous owner's cache and queue.
            if (ReferenceEquals(_onexStatePushOwner, owner)) return;
            _onexStatePushes.ResetAfterReply();
            Volatile.Write(ref _onexStatePushOwner, owner);
        }
        internal void ResetOnexStatePushesAfterReply(NamedPipeClient owner,
            Windows.Foundation.Collections.ValueSet request, Windows.Foundation.Collections.ValueSet response)
        {
            if (response == null) return;
            TryRunForPipePush(owner, () =>
            {
                SelectOnexStatePushOwner(owner);
                if (OnexLatestStates.IsScopedRead(request.Keys)) _onexStatePushes.ResetAfterReadReply(response.Keys);
                else _onexStatePushes.ResetAfterReply();
            });
        }
        private void SetOnexStatePushVisibility(bool visible)
        {
            lock(_onexPushScheduleGate)_onexPushVisible=visible;
            if(visible){_onexStatePushes.Invalidate();ScheduleOnexStatePushes();}
        }
        private void ScheduleOnexStatePushes()
        {
            var owner = Volatile.Read(ref _onexStatePushOwner);
            lock (_onexPushScheduleGate)
            {
                if (isUnloading || _onexPushScheduled || !_onexPushVisible || _onexStatePushes.PendingCount == 0 ||
                    !App.IsCurrentConnectedPipeClient(owner)) return;
                _onexPushScheduled = true;
            }
            _ = DrainOnexStatePushesAsync(owner);
        }
        private async Task DrainOnexStatePushesAsync(NamedPipeClient owner)
        {
            try
            {
                // Low priority allows pointer, controller and window events to run first.
                // Four snapshots per turn bounds the work even after a long closed period.
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low,()=>
                {
                    if(App.GetActiveGamingWidget()!=this){lock(_onexPushScheduleGate)_onexPushVisible=false;return;}
                    if(widget!=null&&!widget.Visible){lock(_onexPushScheduleGate)_onexPushVisible=false;return;}
                    var turn=System.Diagnostics.Stopwatch.StartNew();
                    for(int snapshot=0;snapshot<4;snapshot++)
                    {
                        bool processed = false;
                        if (!TryRunForPipePush(owner, () =>
                        {
                            if (!ReferenceEquals(_onexStatePushOwner, owner)) return;
                            var next = _onexStatePushes.Take(1);
                            if (next.Length == 0) return;
                            processed = true;
                            var pair = next[0];
                            var measured=_onexNavigationPerfCapture?System.Diagnostics.Stopwatch.StartNew():null;
                            long correlationStart=_onexNavigationPerfCapture?System.Diagnostics.Stopwatch.GetTimestamp():0;RecordOnexNavigationPoint("push-start",pair.Key);
                            bool previousPush=_onexApplyingStatePush;_onexApplyingStatePush=true;
                            try
                            {
                                var message=ParsePipeMessageToValueSet(pair.Value);if(message==null){_onexStatePushes.Complete(pair.Key,pair.Value,false);return;}
                                string payload=message.TryGetValue(pair.Key,out var value)?value as string:null;
                                switch(pair.Key)
                                {
                                    case "OnexStartupState":ApplyOnexStartupState(payload);break;
                                    case "OnexProfilesState":ApplyOnexProfilesState(payload);break;
                                    case "OnexDisplayProfilesState":ApplyOnexScreenProfilesPush(payload,owner);break;
                                    case "OnexFanProfilesState":ApplyOnexFanProfilesState(payload);break;
                                    case "OnexKeyboardMouseState":if(!_kbmReadOnlyProjection)ApplyOnexKeyboardMouseState(payload);break;
                                    case "OnexControllerState":if(!_onexControllerReadOnlyProjection)ApplyOnexControllerState(payload);break;
                                    case "OnexWifiState":ApplyOnexWifiState(payload);break;
                                    case "OnexEnergySaverState":ApplyOnexEnergySaverState(payload);break;
                                    case "OnexAirplaneState":ApplyOnexAirplaneState(payload);break;
                                    case "OnexAwakeState":ApplyOnexAwakeState(payload);break;
                                    case "OnexFseState":ApplyOnexFseState(payload);break;
                                    case "OnexDisplayState":ApplyOnexDisplayState(payload);break;
                                    case "OnexFpsState":ApplyOnexFpsState(payload);break;
                                    case "OnexOverlayState":ApplyOnexOverlayState(payload);break;
                                    case "OnexWindowsPowerState":ApplyOnexWindowsPowerState(payload);break;
                                    case "OnexHardwareHomeState":ApplyOnexHardwareHomeState(payload);break;
                                    case "OnexBoostState":ApplyOnexBoostState(payload);break;
                                    case "OnexChargeState":ApplyOnexChargeState(payload);break;
                                    case "OnexDesktopState":ApplyOnexDesktopState(payload);break;
                                    case "MediaLevels":ApplyOnexMediaLevels(payload,true);break;
                                    case "OnexPowerAvailable":
                                        ApplyOnexPowerAvailability(value is bool ready&&ready);
                                        if(message.TryGetValue("OnexPowerState",out var power)&&power is string json)ApplyOnexReleasePowerState(json);break;
                                }
                                _onexPushApplied++;
                                _onexStatePushes.Complete(pair.Key,pair.Value,CanCacheOnexStatePush(pair.Key));
                            }
                            catch(Exception error){_onexStatePushes.Complete(pair.Key,pair.Value,false);Logger.Error("ONEX state push: "+pair.Key+": "+error.Message);}
                            finally{_onexApplyingStatePush=previousPush;if(correlationStart!=0)RecordOnexNavigationPoint("push",pair.Key,correlationStart);if(measured!=null&&measured.Elapsed.TotalMilliseconds>_onexPushLongestMs){_onexPushLongestMs=measured.Elapsed.TotalMilliseconds;_onexPushLongestKey=pair.Key;}}
                        }) || !processed) break;
                        if(turn.ElapsedMilliseconds>=8)break;
                    }
                });
            }
            catch(Exception error){lock(_onexPushScheduleGate)_onexPushVisible=false;Logger.Warn("ONEX state dispatcher: "+error.Message);}
            finally{lock(_onexPushScheduleGate)_onexPushScheduled=false;ScheduleOnexStatePushes();}
        }
        private bool CanCacheOnexStatePush(string key)
        {
            // A setter can temporarily decline a snapshot. Do not remember it as applied.
            if(!App.IsConnected||_kbmBusy||_kbmTuning.Count>0||_onexControllerBusy||_onexStartupBusy||_onexProfilesBusy||_onexWifiBusy||_onexEnergySaverBusy||_onexAirplaneBusy||_onexFseBusy||_onexDisplayBusy||_onexDisplayPending.Count>0||_onexFpsBusy||_onexOverlayBusy||_onexPowerModeBusy||_onexHomeBusy||_onexBoostBusy||_onexChargeBusy||(_onexChargeTimer?.IsEnabled??false)||_onexDesktopBusy||_onexMicrophoneBusy||_onexMediaInFlight!=null||_onexPendingMedia.Count>0||_onexPerformanceCpuApplying)return false;
            return key!="OnexPowerAvailable"; // actual package telemetry changes independently of PL1/PL2
        }
    }
}
