// ONEXTweaks fork additions. The surrounding screens, styles and navigation are ClawTweaks.
// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shared.Enums;
using Windows.Foundation.Collections;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace XboxGamingBar
{
    internal static class OnexFrontend
    {
        internal static bool UsesOwnBackend => true;
        internal static bool PowerAvailable;
        internal static volatile string ProfileId="global";
        internal static volatile string OverlayPresetId;
        private static readonly HashSet<Function> userInput = new HashSet<Function>();
        private static readonly Dictionary<Function,string> userScopes=new Dictionary<Function,string>();
        internal static bool IsOwnDisplayFunction(Function f) => f == Function.IntelColorSaturation || f == Function.IntelColorHue || f == Function.IntelDisplayContrast || f == Function.IntelDisplayBrightness || f == Function.IntelDisplayGamma || f == Function.IntelAdaptiveSharpness || f == Function.IntelFrameGeneration || f == Function.IntelVrr || f == Function.IntelVrrMode || f == Function.IntelLowLatency || f == Function.IntelFrameSync || f == Function.IntelScalingMode || f == Function.IntelScalingMethod || f == Function.Resolution || f == Function.RefreshRate;
        internal static bool IsWriteSupported(Function f) => f == Function.None || f == Function.TDP ||
            f == Function.TDPBoostFPPT || f == Function.TDPBoostEnabled || f == Function.QuickMetricsEnabled;
        internal static void MarkUserInput(Function f, Control control=null, bool confirmed=false)
        {
            if (f != Function.TDP && f != Function.TDPBoostFPPT && f != Function.TDPBoostEnabled) return;
            if(UsesOwnBackend&&!confirmed&&!OnexInputGuard.IsActive(control))return;
            lock (userInput){userInput.Add(f);userScopes[f]=ProfileId;}
        }
        internal static void PrepareMessage(ValueSet message)
        {
            if(OverlayPresetId!=null&&(message.ContainsKey("OnexOverlayLevel")||message.TryGetValue("Function",out var overlayFunction)&&Convert.ToInt32(overlayFunction)==(int)Function.OSDConfig))message["OnexOverlayPresetId"]=OverlayPresetId;
            if(new[]{"OnexWindowsPowerMode","OnexCpuSettings","OnexCpuTuningSet","OnexBoostSet","OnexFpsSet","OnexFanSet","OnexCpuClockSet","OnexOverlayLevel","OnexFanProfilesSet","OnexOverlayPresetsSet"}.Any(message.ContainsKey)&&!message.ContainsKey("OnexProfileId"))message["OnexProfileId"]=ProfileId;
            if(message.TryGetValue("OnexProfilesSet",out var mutation)&&Windows.Data.Json.JsonObject.TryParse(mutation as string,out var change)&&change.GetNamedString("operation","")=="screen")message["OnexProfileId"]=ProfileId;
            if (!message.TryGetValue("Command", out object command) || Convert.ToInt32(command) != 1 ||
                !message.TryGetValue("Function", out object id)) return;
            lock (userInput)
                if (userInput.Remove((Function)Convert.ToInt32(id)))
                {
                    var function=(Function)Convert.ToInt32(id);message["OnexUserInput"]=true;
                    if(userScopes.TryGetValue(function,out var scope)){message["OnexProfileId"]=scope;userScopes.Remove(function);}
                }
            if(message.TryGetValue("OnexUserInput",out var explicitInput)&&explicitInput is bool direct&&direct&&!message.ContainsKey("OnexProfileId"))
            {var function=(Function)Convert.ToInt32(id);if(function==Function.TDP||function==Function.TDPBoostFPPT||function==Function.TDPBoostEnabled)message["OnexProfileId"]=ProfileId;}
        }
        internal static void CancelPendingPowerInput()
        {lock(userInput)foreach(var function in new[]{Function.TDP,Function.TDPBoostFPPT,Function.TDPBoostEnabled}){userInput.Remove(function);userScopes.Remove(function);}}
        internal static ValueSet ParseMessage(string json)
        {
            // A JSON parser is required here: the inherited regex unescaper drops \uXXXX escapes
            // emitted by System.Text.Json, including quotes inside the metrics JSON string.
            if (!Windows.Data.Json.JsonObject.TryParse(json, out var root)) return null;
            var result = new ValueSet();
            foreach (var pair in root)
            {
                var value = pair.Value;
                switch (value.ValueType)
                {
                    case Windows.Data.Json.JsonValueType.String: result[pair.Key] = value.GetString(); break;
                    case Windows.Data.Json.JsonValueType.Boolean: result[pair.Key] = value.GetBoolean(); break;
                    case Windows.Data.Json.JsonValueType.Number:
                        double number = value.GetNumber();
                        if (pair.Key == "UpdatedTime") result[pair.Key] = (long)number;
                        else if (number >= int.MinValue && number <= int.MaxValue && Math.Truncate(number) == number) result[pair.Key] = (int)number;
                        else result[pair.Key] = number;
                        break;
                    case Windows.Data.Json.JsonValueType.Object:
                    case Windows.Data.Json.JsonValueType.Array: result[pair.Key] = value.Stringify(); break;
                }
            }
            return result;
        }
    }

    public sealed partial class GamingWidget
    {
        private bool _onexVerifying;
        private bool _onexPowerAvailabilityKnown,_onexPowerAvailabilityReady,_onexPowerAvailabilityConnected;
        private long _onexFullAvailabilityApplies,_onexPowerAvailabilityReuses;
        private void ApplyOnexPowerAvailability(bool ready)
        {
            bool unchanged=_onexPowerAvailabilityKnown&&_onexPowerAvailabilityReady==ready&&_onexPowerAvailabilityConnected==App.IsConnected&&OnexFrontend.PowerAvailable==ready;
            OnexFrontend.PowerAvailable=ready;
            if(!unchanged){ApplyOnexAvailability();return;}
            // Identical availability does not require querying every unrelated editor.
            // Reassert the actual power controls; power telemetry is still applied below
            // by the snapshot caller, and all command acknowledgments bypass this cache.
            bool enabled=App.IsConnected&&ready;TDPSlider.IsEnabled=enabled;TDPBoostToggle.IsEnabled=enabled;
            TDPBoostFPPTSliderCard.IsEnabled=enabled&&TDPBoostToggle.IsOn;_onexPowerAvailabilityReuses++;
        }
        private void ApplyOnexAvailability()
        {
            if(_releaseHome==null)EnsureOnexReleaseLayout();
            // Keep the imported controls and navigation. A feature becomes actionable as soon as
            // its ONEXPLAYER implementation is verified; it must never show an imaginary hardware state.
            foreach (string name in new[] {
                "RailCenterButton",
                
                "TdpMethodComboBox",
                "SchedulingPolicyComboBox", "MaxPCoreFreqComboBox", "MaxECoreFreqComboBox",
                "TDPModeComboBox", "TDPLimitsMinSlider", "TDPLimitsMaxSlider" })
                if (FindName(name) is Control control)
                {
                    control.IsEnabled = false;
                    ToolTipService.SetToolTip(control, global::XboxGamingBar.OnexStrings.Get("Not connected to ONEXPLAYER yet"));
                }
            MetricsSelectionPanel.Visibility=Visibility.Collapsed;
            OSPowerModeComboBox.IsEnabled=App.IsConnected&&OnexWindowsPowerReady&&!_onexPowerModeBusy;
            ToolTipService.SetToolTip(OSPowerModeComboBox,OSPowerModeComboBox.IsEnabled?null:global::XboxGamingBar.OnexStrings.Get("Windows power mode unavailable"));
            ApplyOnexProfileAvailability();
            ApplyOnexMediaAvailability();
            ApplyOnexChargeAvailability();
            CPUBoostToggle.IsEnabled=App.IsConnected&&_onexBoostKnown&&!_onexBoostBusy&&!_onexPerformanceCpuApplying;
            if(CpuCardExpandButton!=null){CpuCardExpandButton.IsEnabled=App.IsConnected&&OnexWindowsPowerReady&&!_onexPerformanceCpuLoading&&!_onexPerformanceCpuApplying;CpuCardExpandButton.IsTabStop=true;}
            TDPSlider.IsEnabled = App.IsConnected && OnexFrontend.PowerAvailable;
            TDPBoostToggle.IsEnabled = TDPSlider.IsEnabled;
            TDPBoostFPPTSliderCard.IsEnabled = TDPSlider.IsEnabled && TDPBoostToggle.IsOn;
            ApplyOnexTileAvailability();
            UpdateOnexDisplayAvailability();
            _onexPowerAvailabilityKnown=true;_onexPowerAvailabilityReady=OnexFrontend.PowerAvailable;_onexPowerAvailabilityConnected=App.IsConnected;_onexFullAvailabilityApplies++;
        }
        private bool _onexApplyingStatePush,_onexTileAvailabilityPaintQueued;
        private Windows.UI.Xaml.DispatcherTimer _onexTileAvailabilityPaintTimer;
        private long _onexTileAvailabilityPaints,_onexTileAvailabilityPaintRequests;
        private void QueueOnexTileAvailabilityPaint()
        {
            _onexTileAvailabilityPaintRequests++;
            if(_onexTileAvailabilityPaintQueued)return;
            _onexTileAvailabilityPaintQueued=true;
            if(_onexTileAvailabilityPaintTimer==null)
            {
                _onexTileAvailabilityPaintTimer=new Windows.UI.Xaml.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(16)};
                _onexTileAvailabilityPaintTimer.Tick+=(sender,args)=>
                {
                    _onexTileAvailabilityPaintTimer.Stop();_onexTileAvailabilityPaintQueued=false;
                    if(!isUnloading&&App.GetActiveGamingWidget()==this)ApplyOnexTileAvailability();
                };
            }
            _onexTileAvailabilityPaintTimer.Start();
        }
        private int _onexHomePowerAckPaintDepth;
        private long _onexHomePowerAckBatches,_onexHomePowerAckSynchronousPaints;
        private void ApplyOnexTileAvailability()
        {
            bool homeVisible=_releaseHome==null||_releaseHome.Visibility==Visibility.Visible||qsEditMode;
            // Keep every logical action gate current. Push snapshots and the sections
            // of one Home power ACK share one native paint on the next frame.
            bool deferPaint=_onexApplyingStatePush||_onexHomePowerAckPaintDepth>0;
            bool paint=homeVisible&&!deferPaint;
            if(homeVisible&&deferPaint)QueueOnexTileAvailabilityPaint();
            if(paint){_onexTileAvailabilityPaintTimer?.Stop();_onexTileAvailabilityPaintQueued=false;_onexTileAvailabilityPaints++;}
            foreach (var tile in qsTileDefinitions)
            {
                // Enable only operations implemented by the ONEXPLAYER backend.
                bool available = false;
                if(tile.Id=="Profile")available=App.IsConnected&&_onexProfilesState!=null&&!_onexProfilesBusy;
                if(tile.Id=="FPSCombined")available=OnexFpsAvailable;
                if(tile.Id=="Overlay")available=App.IsConnected&&!_onexOverlayBusy&&!_onexOverlayPresetChanging&&_onexOverlayState?.GetNamedBoolean("installed",false)==true;
                if(tile.Id=="CPUBoost") available=App.IsConnected&&_onexBoostKnown&&!_onexBoostBusy;
                if (tile.Id == "ChargeLimiter") available = App.IsConnected && _onexChargeKnown && !_onexChargeBusy;
                if (tile.Id == "ActionTaskManager" || tile.Id == "ActionExplorer" || tile.Id=="Keyboard") available = _onexDesktopAvailable && !_onexDesktopBusy;
                if (tile.Id == "ActionEndTask") available = _onexDesktopAvailable && _onexCanClose && !_onexDesktopBusy;
                if(tile.Id=="PowerMode")available=App.IsConnected&&OnexWindowsPowerReady&&!_onexPowerModeBusy;
                if(tile.Id=="Power"||tile.Id=="Fullscreen"||tile.Id=="OptiScaler"||IsOnexCustomActionSupported(tile))available=App.IsConnected&&_onexDesktopAvailable&&!_onexDesktopBusy;
                if(tile.Id=="ReShade")available=App.IsConnected&&OnexDisplayReady&&_onexScreenProfiles!=null;
                if(tile.Id=="OnexFanMode")available=App.IsConnected&&!_onexFanBusy&&_onexHardwareHome?.GetNamedObject("fan",null)?.GetNamedBoolean("available",false)==true;
                if(tile.Id=="LedToggle")available=App.IsConnected&&!_onexLightingBusy&&_onexHardwareHome?.GetNamedObject("lighting",null)?.GetNamedBoolean("available",false)==true;
                if(tile.Id=="OnexResolution"||tile.Id=="OnexRefreshRate")available=OnexDisplayModesReady;
                if(tile.Id=="OnexFse")available=OnexFseReady;
                if(tile.Id=="OnexRotationLock")available=OnexRotationLockAvailable&&!_onexRotationLockBusy;
                if(tile.Id=="OnexWifi")available=OnexWifiReady;
                if(tile.Id=="OnexBluetooth")available=OnexBluetoothReady;
                if(tile.Id=="OnexAirplane")available=OnexAirplaneReady;
                if(tile.Id=="OnexAwake")available=OnexAwakeReady;
                if(tile.Id=="OnexControllerProfile"||tile.Id=="OnexInputMode")available=OnexControllerHomeReady;
                // Logical action gating remains current for hotkeys; hidden Home
                // visuals are refreshed once when the page is entered.
                tile.IsActionable=available;if(!paint)continue;
                SetTileActionable(tile, available);
                if (!available && tile.StateText != null)
                {
                    tile.StateText.Text = global::XboxGamingBar.OnexStrings.Get("Unavailable");
                    if(tile.TileButton!=null)ToolTipService.SetToolTip(tile.TileButton,global::XboxGamingBar.OnexStrings.Get("This feature is not connected to ONEXPLAYER yet"));
                    UpdateTileScrollAnimation(tile);
                }
                if(tile.Id=="CPUBoost" && tile.StateText!=null)tile.StateText.Text=_onexBoostBusy?global::XboxGamingBar.OnexStrings.Get("Applying…"):!_onexBoostKnown?global::XboxGamingBar.OnexStrings.Get("Unavailable"):_onexBoostOn?global::XboxGamingBar.OnexStrings.Get("On"):global::XboxGamingBar.OnexStrings.Get("Off");
                if(tile.Id=="OnexFanMode"&&_onexFanBusy&&tile.StateText!=null)tile.StateText.Text=global::XboxGamingBar.OnexStrings.Get("Applying…");
                if(tile.Id=="LedToggle"&&_onexLightingBusy&&tile.StateText!=null)tile.StateText.Text=global::XboxGamingBar.OnexStrings.Get("Applying…");
                if (tile.Id == "ChargeLimiter" && tile.StateText != null)
                {
                    tile.StateText.Text = _onexChargeBusy ? global::XboxGamingBar.OnexStrings.Get("Applying…") : !_onexChargeKnown ? global::XboxGamingBar.OnexStrings.Get("Unavailable") : _onexChargeOn ? global::XboxGamingBar.OnexStrings.Format("On {0}%",_onexChargePercent) : global::XboxGamingBar.OnexStrings.Get("Off");
                    UpdateTileScrollAnimation(tile);
                }
                if (tile.StateText != null && (tile.Id == "ActionTaskManager" || tile.Id == "ActionExplorer" || tile.Id=="Keyboard")) tile.StateText.Text = _onexDesktopBusy?global::XboxGamingBar.OnexStrings.Get("Opening…"):_onexDesktopAvailable?global::XboxGamingBar.OnexStrings.Get("Open"):global::XboxGamingBar.OnexStrings.Get("Unavailable");
                if (tile.StateText != null && tile.Id == "ActionEndTask") tile.StateText.Text = _onexCanClose ? (_onexTargetApp ?? "Close") : global::XboxGamingBar.OnexStrings.Get("No window");
                if(available&&tile.StateText!=null)
                {
                    if(tile.Id=="OnexResolution")tile.StateText.Text=OnexResolutionTileCaption;
                    else if(tile.Id=="OnexRefreshRate")tile.StateText.Text=OnexRefreshTileCaption;
                    else if(tile.Id=="PowerMode")tile.StateText.Text=OnexStrings.Get(new[]{"Efficiency","Balanced","Performance"}[(int)_onexWindowsPower.GetNamedNumber(_onexWindowsPower.GetNamedBoolean("onAc",false)?"modeAc":"modeDc",1)]);
                    else if(tile.Id=="Power")tile.StateText.Text=global::XboxGamingBar.OnexStrings.Get("Menu");
                    else if(tile.Id=="Fullscreen"||tile.Id=="OptiScaler")tile.StateText.Text=global::XboxGamingBar.OnexStrings.Get("Toggle");
                    else if(tile.Id=="OnexFanMode")tile.StateText.Text=OnexFanProfileCaption;
                    else if(tile.Id=="ReShade")tile.StateText.Text=OnexScreenProfileCaption;
                    else if(tile.Id=="LedToggle")tile.StateText.Text=OnexLightingIsOn?global::XboxGamingBar.OnexStrings.Get("On"):global::XboxGamingBar.OnexStrings.Get("Off");
                    else if(IsOnexCustomActionSupported(tile))tile.StateText.Text=global::XboxGamingBar.OnexStrings.Get("Run");
                }
                if(tile.Id=="OnexBluetooth")ApplyOnexBluetoothTile(tile);
                if(tile.Id=="OnexWifi")ApplyOnexWifiTile(tile);
                if(tile.Id=="OnexAirplane")ApplyOnexAirplaneTile(tile);
                if(tile.Id=="OnexAwake")ApplyOnexAwakeTile(tile);
                if(tile.Id=="OnexFse")ApplyOnexFseTile(tile);
                if(tile.Id=="OnexRotationLock")ApplyOnexRotationLockTile(tile);
                if(tile.Id=="Overlay")ApplyOnexOverlayTile(tile);
                if(tile.Id=="OnexControllerProfile"||tile.Id=="OnexInputMode")ApplyOnexControllerHomeTile(tile);
                if(tile.Id=="FPSCombined")ApplyOnexFpsTile(tile);
                if(tile.Id=="Profile"&&tile.StateText!=null){tile.StateText.Text=OnexProfileCaption(OnexActiveProfile);ApplyOnexToggleTileVisual(tile,PerGameProfileToggle.IsOn,_onexProfilesState!=null);}
                if(tile.Id=="ReShade"&&available)ToolTipService.SetToolTip(tile.TileButton,global::XboxGamingBar.OnexStrings.Format("Screen color profile: {0} · Tap to choose; hold/X for details",OnexScreenProfileCaption));
                if(tile.Id=="CPUBoost")ApplyOnexToggleTileVisual(tile,_onexBoostOn,_onexBoostKnown);
                if(tile.Id=="ChargeLimiter")ApplyOnexToggleTileVisual(tile,_onexChargeOn,_onexChargeKnown);
                if(tile.Id=="LedToggle")ApplyOnexToggleTileVisual(tile,OnexLightingIsOn,_onexHardwareHome?.GetNamedObject("lighting",null)?.GetNamedBoolean("available",false)==true);
                ApplyOnexTileCaption(tile,available);
                if(tile.Id=="ChargeLimiter")UpdateOnexEnergySaverIndicators();
                if(tile.StateText!=null)UpdateTileScrollAnimation(tile);
            }
        }

        internal void ScheduleOnexArmedVerification()
        {
            if(!App.OnexVerify||App.OnexTileRevisionStage<=0||!App.IsConnected)return;
            _=Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,async()=>
            {
                if(App.OnexVerify&&App.IsConnected&&App.GetActiveGamingWidget()==this&&System.IO.File.Exists(System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path,"onex-focus-verification-armed.json")))await CaptureOnexVerificationAsync();
            });
        }
        private async Task CaptureOnexVerificationAsync()
        {
            if (_onexVerifying) return;
            if(App.OnexTileRevisionStage>0&&(App.GetActiveGamingWidget()!=this||!App.TryClaimOnexDiagnostic()))return;
            _onexVerifying = true;
            try
            {
                await Task.Delay(1500);
                if(App.OnexTileRevisionStage>0){await VerifyOnexTileRevisionAsync();return;}
                if(App.OnexProfilesTest){await VerifyOnexProfilesAsync();return;}
                if(App.OnexTileLayoutTest){await VerifyOnexTileLayoutAsync();return;}
                if(App.OnexFpsTest){await VerifyOnexFpsAsync();return;}
                if(App.OnexRtssTest){await VerifyOnexOverlayAsync();return;}
                if (App.OnexDesktopTestAction != null) { await VerifyOnexDesktopTileAsync(App.OnexDesktopTestAction); return; }
                if(App.OnexExerciseFocus){await VerifyOnexReleaseFocusAsync();return;}
                if(App.OnexExerciseBoost){await VerifyOnexBoostAsync();return;}
                if (App.OnexExerciseCharge) { await VerifyOnexChargeAsync(); return; }
                if (App.OnexExercisePower) await ExerciseOnexPowerAsync();
                if (App.OnexExerciseMedia) await ExerciseOnexMediaAsync();
                QuickNavItem.IsChecked = true;
                ApplyOnexAvailability();
                await SaveOnexImage("onex-widget-home.png");
                PerformanceNavItem.IsChecked = true;
                ApplyOnexAvailability();
                await SaveOnexImage("onex-widget-performance.png");
                var report = new Windows.Data.Json.JsonObject
                {
                    ["frontend"] = Windows.Data.Json.JsonValue.CreateStringValue("ClawTweaks 1.0.1 release home + public source screens; own ONEXPLAYER backend"),
                    ["upstreamCommit"] = Windows.Data.Json.JsonValue.CreateStringValue("7eaaf0573a9e783735ef7a84dad0f59d23230261"),
                    ["capturedUtc"] = Windows.Data.Json.JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("o")),
                    ["combinedHome"] = Windows.Data.Json.JsonValue.CreateBooleanValue(QuickNavItem.Visibility==Visibility.Collapsed&&ReferenceEquals(QuickSettingsTilesContainer.Parent,_releaseHome.TilesHost)),
                    ["duplicateCardsHidden"] = Windows.Data.Json.JsonValue.CreateBooleanValue(_releaseHome.DuplicateCardsHidden),
                    ["releaseHomeLoaded"] = Windows.Data.Json.JsonValue.CreateBooleanValue(_releaseHome != null),
                    ["navigationCount"] = Windows.Data.Json.JsonValue.CreateNumberValue(MainNavPanel.Children.Count),
                    ["horizontalMedia"] = Windows.Data.Json.JsonValue.CreateBooleanValue(RailBrightnessSlider.Orientation == Orientation.Horizontal && RailVolumeSlider.Orientation == Orientation.Horizontal),
                    ["releasePl1"] = Windows.Data.Json.JsonValue.CreateNumberValue(_releasePl1),
                    ["releasePl2"] = Windows.Data.Json.JsonValue.CreateNumberValue(_releasePl2),
                    ["connected"] = Windows.Data.Json.JsonValue.CreateBooleanValue(App.IsConnected),
                    ["isGameBar"] = Windows.Data.Json.JsonValue.CreateBooleanValue(widget != null),
                    ["connectionBannerVisible"] = Windows.Data.Json.JsonValue.CreateBooleanValue(ConnectionStatusBanner.Visibility == Visibility.Visible),
                    ["metricsReceived"] = Windows.Data.Json.JsonValue.CreateNumberValue(currentMetricsData.Count),
                    ["unicodeProtocolPassed"] = Windows.Data.Json.JsonValue.CreateBooleanValue((string)OnexFrontend.ParseMessage("{\"Content\":\"PL1 \\u00b7 PL2\"}")["Content"] == "PL1 · PL2"),
                    ["brightnessEnabled"] = Windows.Data.Json.JsonValue.CreateBooleanValue(RailBrightnessSlider.IsEnabled),
                    ["volumeEnabled"] = Windows.Data.Json.JsonValue.CreateBooleanValue(RailVolumeSlider.IsEnabled),
                    ["brightnessLabel"] = Windows.Data.Json.JsonValue.CreateStringValue(RailBrightnessValue.Text),
                    ["volumeLabel"] = Windows.Data.Json.JsonValue.CreateStringValue(RailVolumeValue.Text),
                    ["pl1"] = Windows.Data.Json.JsonValue.CreateNumberValue(tdp.Value),
                    ["pl2"] = Windows.Data.Json.JsonValue.CreateNumberValue(tdpBoostFPPT.Value),
                    ["maximumPl1"] = Windows.Data.Json.JsonValue.CreateNumberValue(TDPSlider.Maximum),
                    ["maximumPl2"] = Windows.Data.Json.JsonValue.CreateNumberValue(TDPBoostFPPTSliderCard.Maximum),
                    ["width"] = Windows.Data.Json.JsonValue.CreateNumberValue(ActualWidth),
                    ["height"] = Windows.Data.Json.JsonValue.CreateNumberValue(ActualHeight)
                };
                var colors=new Windows.Data.Json.JsonObject();
                foreach(var tile in qsTileDefinitions.Where(t=>t.StateText!=null))
                    if(tile.StateText.Foreground is Windows.UI.Xaml.Media.SolidColorBrush brush)colors[tile.Id]=Windows.Data.Json.JsonValue.CreateStringValue(brush.Color.ToString());
                report["tileCaptionColors"]=colors;
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-widget-verification.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, report.Stringify());
                PerformanceNavItem.IsChecked = true;
            }
            catch (Exception ex)
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-widget-verification-error.txt", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, ex.ToString());
            }
        }
        private async Task ExerciseOnexPowerAsync()
        {
            var before=await ReadOnexStateAsync();
            var report=new Windows.Data.Json.JsonObject();
            var toggle=(ToggleSwitch)_releaseHome.FindName("LightPl2Toggle");
            var primary=(Slider)_releaseHome.FindName("LightPowerSlider");
            var secondary=(Slider)_releaseHome.FindName("OnexPl2Slider");
            bool originalToggle=toggle.IsOn;
            async Task<Windows.Data.Json.JsonObject> Check(int pl1,int pl2)
            {
                for(int i=0;i<40;i++)
                {
                    await Task.Delay(150);var state=await ReadOnexStateAsync();
                    if(state.GetNamedNumber("pl1Watts")==pl1&&state.GetNamedNumber("pl2Watts")==pl2&&primary.IsEnabled&&secondary.IsEnabled)return state;
                }
                throw new InvalidOperationException("Power readback or input availability mismatch");
            }
            try
            {
                int baseline=(int)before.GetNamedNumber("pl1Watts"),pl2=(int)before.GetNamedNumber("pl2Watts");
                if(baseline<=10||baseline>40||pl2<=baseline)throw new InvalidOperationException("Verification needs room to lower the current limits");
                PerformanceNavItem.IsChecked=true;toggle.IsOn=true;_releaseHome.UpdateLayout();
                var p1=(Windows.UI.Xaml.Automation.Provider.IRangeValueProvider)new Windows.UI.Xaml.Automation.Peers.SliderAutomationPeer(primary).GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.RangeValue);
                var p2=(Windows.UI.Xaml.Automation.Provider.IRangeValueProvider)new Windows.UI.Xaml.Automation.Peers.SliderAutomationPeer(secondary).GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.RangeValue);
                if(p1.Minimum!=8||p1.Maximum!=40||p2.Minimum!=baseline||p2.Maximum!=41)throw new InvalidOperationException("Power range mismatch");
                p1.SetValue(baseline-1);
                report["primary"]=await Check(baseline-1,pl2);
                p2.SetValue(pl2-1);
                report["secondary"]=await Check(baseline-1,pl2-1);
                p2.SetValue(baseline-1); // The native minimum is the current PL1.
                report["secondaryClamp"]=await Check(baseline-1,baseline-1);
                await SaveOnexImage("onex-power-dual-knob.png");
                toggle.IsOn=false;p1.SetValue(baseline-2);
                report["linked"]=await Check(baseline-2,baseline-1);
                report["input"]=Windows.Data.Json.JsonValue.CreateStringValue("Native SliderAutomationPeer RangeValue for both visible controls");
                report["passed"]=Windows.Data.Json.JsonValue.CreateBooleanValue(true);
            }
            catch(Exception ex){report["passed"]=Windows.Data.Json.JsonValue.CreateBooleanValue(false);report["error"]=Windows.Data.Json.JsonValue.CreateStringValue(ex.ToString());}
            finally
            {
                await SetOnexReleasePowerAsync((int)before.GetNamedNumber("pl1Watts"),(int)before.GetNamedNumber("pl2Watts"));
                var restored=await ReadOnexStateAsync();report["before"]=before;report["restored"]=restored;
                bool exact=before.GetNamedString("rawLimits")==restored.GetNamedString("rawLimits");
                report["rawRestored"]=Windows.Data.Json.JsonValue.CreateBooleanValue(exact);
                if(!exact)report["passed"]=Windows.Data.Json.JsonValue.CreateBooleanValue(false);
                toggle.IsOn=originalToggle;
                var file=await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-widget-power-test.json",CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file,report.Stringify());
                await(await ApplicationData.Current.LocalFolder.GetFileAsync("onex-layout-verification-armed.json")).DeleteAsync();
            }
        }
        private static async Task<Windows.Data.Json.JsonObject> ReadOnexStateAsync()
        {
            var reply = await App.SendMessageAsync(new ValueSet { ["OnexReadState"] = true });
            return Windows.Data.Json.JsonObject.Parse((string)reply["OnexState"]);
        }
        private async Task SaveOnexImage(string name, UIElement target = null, int settleMs = 700)
        {
            if(settleMs>0)await Task.Delay(settleMs);
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(target ?? this);
            var pixels = await bitmap.GetPixelsAsync();
            if(bitmap.PixelWidth<=0||bitmap.PixelHeight<=0||pixels.Length==0)throw new InvalidOperationException("XAML target has no capturable pixels (Popup content is unsupported)");
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
            using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
                await encoder.FlushAsync();
            }
        }
    }
}
