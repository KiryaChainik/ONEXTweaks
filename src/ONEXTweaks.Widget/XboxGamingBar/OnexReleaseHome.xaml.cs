// Own backend wiring for ClawTweaks 1.0.1 release-derived markup.
// SPDX-License-Identifier: AGPL-3.0-only
using System;
using ONEXTweaks.Power;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Input;
using Windows.System;

namespace XboxGamingBar
{
    public sealed partial class OnexReleaseHome : UserControl
    {
        public event Action<int,int> PowerRequested;
        internal event Action PowerEditing;
        internal Func<string> PowerScope;
        internal Func<bool> CanStartPowerRequest;
        internal bool PowerRequestInFlight=>busy;
        internal bool HasPendingPowerInput=>busy||powerPending;
        internal string CapturedPowerScope=>requestedPowerScope;
        internal Slider Pl2Input=>OnexPl2Slider;
        internal ToggleSwitch PowerToggle=>LightPl2Toggle;
        internal long PowerRequestCount=>powerRequestCount;
        private bool powerPending,powerPointerCancelled;
        private string pendingPowerScope,pointerPowerScope,requestedPowerScope;
        private long powerRequestCount;
        private int actualReadbackPl1;private bool lastPowerAvailable;
        private string CurrentPowerScope=>PowerScope?.Invoke()??"";
        internal void CancelPendingPower(){timer.Stop();powerPending=false;powerPointerCancelled=true;OnexInputGuard.Cancel(LightPowerSlider);OnexInputGuard.Cancel(OnexPl2Slider);}
        internal void VerifyPowerPointer(Slider slider,bool held)
        {
            if(App.OnexTileRevisionStage!=219&&App.OnexTileRevisionStage!=220&&App.OnexTileRevisionStage!=221&&App.OnexTileRevisionStage!=222)throw new InvalidOperationException("Native power diagnostic only");
            OnexInputGuard.VerifyPointerHeld(slider,held);if(held)BeginPowerPointer(slider);
        }
        private void BeginPowerPointer(Slider slider)
        {
            powerPointerCancelled=false;pointerPowerScope=CurrentPowerScope;
            if(loading||!slider.IsEnabled)return;
            int pl1=ReferenceEquals(slider,LightPowerSlider)?Math.Min(40,(int)Math.Round(slider.Value)):desiredPl1;
            int pl2=ReferenceEquals(slider,OnexPl2Slider)?ClampManualPl2(pl1,(int)Math.Round(slider.Value)):Pl2ForSustainedPower(pl1);
            Queue(pl1,pl2);
        }
        public event Action<bool> BoostRequested;
        private bool loading=true,busy,pl2ToggleInput,confirmedSeparatePl2;
        private readonly bool persistPowerPreferences;
        private bool? persistedSeparatePl2;
        private int? persistedRememberedPl2;
        private long powerPreferenceWrites;
        internal long PowerPreferenceWriteCount=>powerPreferenceWrites;
        internal string PowerStatusText=>OnexPowerStatus.Text;
        private void PersistSeparatePl2(bool value)
        {
            if(!persistPowerPreferences||persistedSeparatePl2==value)return;
            Windows.Storage.ApplicationData.Current.LocalSettings.Values["OnexSeparatePl2"]=value;persistedSeparatePl2=value;powerPreferenceWrites++;
        }
        private int actualReadbackPl2;
        private int cachedReadoutPl1=-1,cachedReadoutPl2=-1,powerReadoutCacheBuilds;
        private bool cachedReadoutSeparate;
        private string cachedReadoutLanguage,cachedPl1Text,cachedPl2Text,cachedPl2Summary,cachedExternalPowerStatus;
        private int? lastStandardPl2,lastBoostPl2;
        private string manualPl2Scope;
        private int rememberedPl2;
        public int RememberedPl2Watts=>rememberedPl2;
        public bool SeparatePl2Enabled=>LightPl2Toggle.IsOn;
        private int desiredPl1=15,desiredPl2=16;
        private readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
        private bool powerError;
        private Grid _mediaBrightnessHost,_mediaVolumeHost;
        public ScrollViewer Scroll => LightScrollViewer;
        public Slider PowerInput => LightPowerSlider;
        public Panel StatsHost => OnexStatsHost;
        public Panel TilesHost => OnexTilesHost;
        public Panel FooterHost => OnexFooterHost;
        public FrameworkElement MediaSection => LightTopGroup;
        public FrameworkElement PowerSection => LightPowerCard;
        public bool HasMedia(Slider brightness,Slider volume) => ReferenceEquals(brightness.Parent,RailHBrightnessHost)&&ReferenceEquals(volume.Parent,RailHVolumeHost);
        public bool DuplicateCardsHidden => OnexRetiredCards.Visibility==Visibility.Collapsed && LightProfileCard.Visibility==Visibility.Collapsed;
        private Control _headerFocus,_mediaEntry,_tileFocus;
        public void WireNavigation(Control header,Control mediaEntry,Control tile)
        {
            _headerFocus=header;_mediaEntry=mediaEntry;_tileFocus=tile;if(header!=null)header.XYFocusDown=LightPl2Toggle;
            bool separate=LightPl2Toggle.IsOn;
            LightPowerSlider.XYFocusUp=LightPl2Toggle;LightPowerSlider.XYFocusDown=separate?(Control)OnexPl2Slider:mediaEntry;
            OnexPl2Slider.XYFocusUp=LightPowerSlider;OnexPl2Slider.XYFocusDown=mediaEntry;
            LightPowerSlider.XYFocusLeft=LightPl2Toggle;LightPl2Toggle.XYFocusRight=LightPowerSlider;
            LightPl2Toggle.XYFocusUp=header;LightPl2Toggle.XYFocusDown=LightPowerSlider;
            mediaEntry.XYFocusUp=separate?(Control)OnexPl2Slider:LightPowerSlider;
            if(_mediaVolumeHost!=null)foreach(var child in _mediaVolumeHost.Children)if(child is Control c)c.XYFocusUp=mediaEntry.XYFocusUp;
            if(tile!=null)tile.XYFocusUp=mediaEntry;
        }
        public OnexReleaseHome():this(true){}
        internal OnexReleaseHome(bool persistPreferences)
        {
            persistPowerPreferences=persistPreferences;
            InitializeComponent();
            RefreshPowerScale(desiredPl1);
            LightPl2Head.Visibility=Visibility.Visible;
            LightGameName.Text=global::XboxGamingBar.OnexStrings.Get("ONEXPLAYER 3");
            LightProfileState.Text=global::XboxGamingBar.OnexStrings.Get("Current device settings");LightProfileState.Visibility=Visibility.Visible;
            LightPowerSlider.ValueChanged+=(s,e)=>
            {
                if(loading||!LightPowerSlider.IsEnabled)return;
                int pl1=(int)Math.Round(e.NewValue);
                if(OnexInputGuard.IsPreviewing(LightPowerSlider))
                {
                    int pl2=Pl2ForSustainedPower(pl1);
                    ProjectPl2(pl1,pl2);
                    if(pl1==actualReadbackPl1)ShowAcknowledgedPower(pl1,actualReadbackPl2);
                    else ShowPowerSelection(pl1,pl2);
                    return;
                }
                if(OnexInputGuard.IsActive(LightPowerSlider))Queue(pl1,Pl2ForSustainedPower(pl1));
            };
            OnexInputGuard.Wire(LightPowerSlider,()=>BeginPowerPointer(LightPowerSlider));
            OnexInputGuard.Wire(OnexPl2Slider,()=>BeginPowerPointer(OnexPl2Slider));
            OnexInputGuard.SetCommit(LightPowerSlider,()=>{if(!loading&&LightPowerSlider.IsEnabled){int pl1=(int)Math.Round(LightPowerSlider.Value);Queue(pl1,Pl2ForSustainedPower(pl1));}});
            OnexInputGuard.SetCommit(OnexPl2Slider,()=>{if(!loading&&OnexPl2Slider.IsEnabled)Queue(desiredPl1,(int)Math.Round(OnexPl2Slider.Value));});
            LightCpuBoostToggle.Toggled+=(s,e)=>{if(!loading&&LightCpuBoostToggle.IsEnabled){LightCpuBoostToggle.IsEnabled=false;BoostRequested?.Invoke(LightCpuBoostToggle.IsOn);}};
            // Release-derived minimal ToggleSwitch template has no native SwitchThumb part.
            // Explicit tap handling makes the visible control actionable for touch/mouse.
            LightPl2Toggle.PreviewKeyDown+=(s,e)=>
            {
                if(_headerFocus==null&&(e.Key==VirtualKey.Up||e.Key==VirtualKey.GamepadDPadUp)){e.Handled=true;return;}
                if(e.Key==VirtualKey.GamepadA||e.Key==VirtualKey.Space||e.Key==VirtualKey.Enter)
                {
                    e.Handled=true;
                    if(!e.KeyStatus.WasKeyDown&&LightPl2Toggle.IsEnabled)TogglePl2FromInput();
                }
            };
            // Suppress ToggleSwitch's built-in release toggle: our minimal template uses
            // the explicit press handler above, so forwarding release would toggle twice.
            LightPl2Toggle.PreviewKeyUp+=(s,e)=>
            {
                if(e.Key==VirtualKey.GamepadA||e.Key==VirtualKey.Space||e.Key==VirtualKey.Enter)e.Handled=true;
            };
            LightPl2Toggle.IsTapEnabled=true;
            LightPl2Toggle.Tapped+=(s,e)=>{if(LightPl2Toggle.IsEnabled){TogglePl2FromInput();e.Handled=true;}};
            LightPl2Toggle.Toggled+=(s,e)=>
            {
                OnexPl2Slider.Visibility=LightPl2Toggle.IsOn?Visibility.Visible:Visibility.Collapsed;
                RefreshPowerScale(desiredPl1);
                if(_mediaEntry!=null)WireNavigation(_headerFocus,_mediaEntry,_tileFocus);
                if(loading)return;
                if(pl2ToggleInput)
                {
                    if(!LightPl2Toggle.IsOn)RememberPl2(desiredPl2);
                    int target=LightPl2Toggle.IsOn?ClampManualPl2(desiredPl1,rememberedPl2>0?rememberedPl2:desiredPl2):Pl2ControlPolicy.Automatic(desiredPl1);
                    Queue(desiredPl1,target);
                }
                else if(!pl2ToggleInput)
                {
                    // Programmatic visual/configuration updates never apply hardware limits.
                    confirmedSeparatePl2=LightPl2Toggle.IsOn;PersistSeparatePl2(LightPl2Toggle.IsOn);
                }
            };
            OnexPl2Slider.ValueChanged+=(s,e)=>
            {
                if(loading||!OnexPl2Slider.IsEnabled)return;
                int pl2=ClampManualPl2(desiredPl1,(int)Math.Round(e.NewValue));
                if(OnexPl2Slider.Value!=pl2)
                {
                    loading=true;
                    try{OnexPl2Slider.Value=pl2;}
                    finally{loading=false;}
                }
                if(OnexInputGuard.IsPreviewing(OnexPl2Slider))ShowPowerSelection(desiredPl1,pl2);
                else if(OnexInputGuard.IsActive(OnexPl2Slider))Queue(desiredPl1,pl2);
            };
            ReloadPowerPreferences();
            timer.Tick+=(s,e)=>
            {
                timer.Stop();if(!powerPending)return;
                if(!App.IsConnected){CancelPendingPower();ApplyPower(actualReadbackPl1,actualReadbackPl2,false);return;}
                if(busy||OnexInputGuard.IsEditing(LightPowerSlider)||OnexInputGuard.IsEditing(OnexPl2Slider)||CanStartPowerRequest?.Invoke()==false){timer.Start();return;}
                if(pendingPowerScope!=CurrentPowerScope){CancelPendingPower();return;}
                powerPending=false;
                if(desiredPl1==actualReadbackPl1&&desiredPl2==actualReadbackPl2&&LightPl2Toggle.IsOn==confirmedSeparatePl2){ApplyPower(actualReadbackPl1,actualReadbackPl2,lastPowerAvailable);return;}
                busy=true;requestedPowerScope=pendingPowerScope;powerRequestCount++;
                // An acknowledgment may arrive after a new touch has already started.
                // Keep both sliders interactive and preserve the newer queued intent.
                // The request is serialized by busy; new slider/toggle intent stays interactive.
                PowerRequested?.Invoke(desiredPl1,desiredPl2);
            };
            foreach(string id in new[]{"LightFpsStepped","LightFpsFree","LightFpsToggle","LightFpsIntel","LightFpsRtss","LightFpsMinus","LightFpsPlus","LightOverlay0","LightOverlay8","LightOverlay3","LightOverlay4","LightPowerMode0","LightPowerMode1","LightPowerMode2","LightFan0","LightFan1","LightFan2","LightFan3","LightTilesToggleInTab"})
                if(FindName(id) is Control c){c.IsEnabled=false;ToolTipService.SetToolTip(c,global::XboxGamingBar.OnexStrings.Get("Not connected to ONEXPLAYER yet"));}
            foreach(var control in new Control[]{LightCpuBoostToggle,LightPl2Toggle})
                control.KeyDown+=(sender,e)=>
                {
                    var current=(Control)sender;
                    Control target=null;
                    if(e.Key==VirtualKey.Up||e.Key==VirtualKey.GamepadDPadUp)target=current.XYFocusUp as Control;
                    else if(e.Key==VirtualKey.Down||e.Key==VirtualKey.GamepadDPadDown)target=current.XYFocusDown as Control;
                    if(target?.IsEnabled==true){e.Handled=target.Focus(FocusState.Keyboard);}
                };
            LightFpsValue.Text="—";
            LightPowerSlider.IsEnabled=false;OnexPl2Slider.IsEnabled=false;LightPl2Toggle.IsEnabled=false;LightCpuBoostToggle.IsEnabled=false;
            loading=false;
        }
        public void ReloadPowerPreferences()
        {
            if(busy||timer.IsEnabled)return;bool prior=loading;loading=true;
            try
            {
                var prefs=Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                if(prefs.TryGetValue("OnexSeparatePl2",out var separate)&&separate is bool enabled)LightPl2Toggle.IsOn=enabled;
                rememberedPl2=prefs.TryGetValue("OnexRememberedPl2Watts",out var saved)&&saved is int watts&&watts>=8&&watts<=Pl2ControlPolicy.ExtendedManualCeiling?watts:0;
                confirmedSeparatePl2=LightPl2Toggle.IsOn;
                persistedSeparatePl2=prefs.TryGetValue("OnexSeparatePl2",out var storedMode)&&storedMode is bool storedSeparate?(bool?)storedSeparate:null;
                persistedRememberedPl2=prefs.TryGetValue("OnexRememberedPl2Watts",out var storedWatts)&&storedWatts is int wattsValue?(int?)wattsValue:null;
            }
            finally{loading=prior;}
        }
        private void TogglePl2FromInput()
        {
            if(loading||!LightPl2Toggle.IsEnabled)return;pl2ToggleInput=true;
            try{LightPl2Toggle.IsOn=!LightPl2Toggle.IsOn;}finally{pl2ToggleInput=false;}
        }
        private void RememberPl2(int watts)
        {rememberedPl2=Math.Max(8,Math.Min(Pl2ControlPolicy.ExtendedManualCeiling,watts));if(persistPowerPreferences&&persistedRememberedPl2!=rememberedPl2){Windows.Storage.ApplicationData.Current.LocalSettings.Values["OnexRememberedPl2Watts"]=rememberedPl2;persistedRememberedPl2=rememberedPl2;powerPreferenceWrites++;}}

        private static bool ExtendedPl2(int pl1)=>pl1==Pl2ControlPolicy.MaximumPl1;
        private static int ManualPl2Maximum(int pl1)=>ExtendedPl2(pl1)?Pl2ControlPolicy.ExtendedManualCeiling:Pl2ControlPolicy.MaximumPl1;
        internal static int ClampManualPl2(int pl1,int pl2)=>Math.Max(Pl2ControlPolicy.Automatic(pl1),Math.Min(ManualPl2Maximum(pl1),pl2));
        private int Pl2ForSustainedPower(int pl1)
        {
            if(!LightPl2Toggle.IsOn)return Pl2ControlPolicy.Automatic(pl1);
            if(ExtendedPl2(pl1)==ExtendedPl2(desiredPl1))return ClampManualPl2(pl1,desiredPl2);
            int? remembered=manualPl2Scope==CurrentPowerScope?(ExtendedPl2(pl1)?lastBoostPl2:lastStandardPl2):null;
            return ClampManualPl2(pl1,remembered??Pl2ControlPolicy.Automatic(pl1));
        }

        private void CacheManualPowerForScope(int pl1,int pl2)
        {
            string scope=CurrentPowerScope;
            if(manualPl2Scope!=scope)
            {
                manualPl2Scope=scope;
                lastStandardPl2=lastBoostPl2=null;
            }
            if(!LightPl2Toggle.IsOn||ClampManualPl2(pl1,pl2)!=pl2)return;
            if(ExtendedPl2(pl1))lastBoostPl2=pl2;
            else lastStandardPl2=pl2;
        }

        private void Queue(int pl1,int pl2)
        {
            bool pointer=OnexInputGuard.IsPointerActive(LightPowerSlider)||OnexInputGuard.IsPointerActive(OnexPl2Slider);
            if(pointer&&(powerPointerCancelled||pointerPowerScope!=CurrentPowerScope))return;
            pendingPowerScope=pointer?pointerPowerScope:CurrentPowerScope;powerPending=true;PowerEditing?.Invoke();
            desiredPl1=Math.Max(8,Math.Min(40,pl1));desiredPl2=LightPl2Toggle.IsOn?ClampManualPl2(desiredPl1,pl2):Pl2ControlPolicy.Automatic(desiredPl1);
            ProjectPl2(desiredPl1,desiredPl2);
            ShowPowerSelection(desiredPl1,desiredPl2);
            powerError=false;
            timer.Stop();timer.Start();
        }
        private void ProjectPl2(int pl1,int pl2)
        {
            bool prior=loading;loading=true;
            try
            {
                int min=ExtendedPl2(pl1)?Pl2ControlPolicy.Automatic(pl1):Pl2ControlPolicy.MinimumPl1;
                int max=ManualPl2Maximum(pl1);
                if(min>OnexPl2Slider.Maximum)OnexPl2Slider.Maximum=max;
                OnexPl2Slider.Minimum=min;
                OnexPl2Slider.Maximum=max;
                OnexPl2Slider.Value=ClampManualPl2(pl1,pl2);
                RefreshPowerScale(pl1);
            }
            finally{loading=prior;}
        }

        private void RefreshPowerScale(int pl1)
        {
            bool boost=LightPl2Toggle.IsOn&&ExtendedPl2(pl1);
            LightPowerMin.Text=OnexStrings.Format("{0} W",boost?Pl2ControlPolicy.Automatic(pl1):Pl2ControlPolicy.MinimumPl1);
            LightPowerMax.Text=OnexStrings.Format("{0} W",boost?Pl2ControlPolicy.ExtendedManualCeiling:Pl2ControlPolicy.MaximumPl1);
        }

        private string PowerSummary(int pl1,int pl2)=>LightPl2Toggle.IsOn&&ExtendedPl2(pl1)
            ?OnexStrings.Format("PL2 {0} W",pl2):OnexStrings.Format("PL1 {0} W / PL2 {1} W",pl1,pl2);

        private void ShowPowerSelection(int pl1,int pl2)
        {
            LightPowerValue.Text=pl1.ToString();
            LightPl2Value.Text=pl2.ToString();
            LightPowerPl2.Text=PowerSummary(pl1,pl2);
        }

        private void ShowAcknowledgedPower(int pl1,int pl2)
        {
            PreparePowerReadoutText(pl1,pl2);
            LightPowerValue.Text=cachedPl1Text;
            LightPl2Value.Text=cachedPl2Text;
            LightPowerPl2.Text=cachedPl2Summary;
        }

        public void ApplyPower(int pl1,int pl2,bool available,bool? separate=null,int? remembered=null)
        {
            lastPowerAvailable=available;
            if(busy||powerPending)return;
            if(OnexInputGuard.IsPreviewing(LightPowerSlider)||OnexInputGuard.IsPreviewing(OnexPl2Slider))return;
            if(!powerPointerCancelled&&(OnexInputGuard.IsEditing(LightPowerSlider)||OnexInputGuard.IsEditing(OnexPl2Slider)))return;
            if(pl1<8||pl1>40||pl2<pl1)
            {
                LightPowerValue.Text="—";LightPl2Value.Text="—";LightPowerPl2.Text="";
                LightPowerSlider.IsEnabled=false;OnexPl2Slider.IsEnabled=false;LightPl2Toggle.IsEnabled=false;
                ShowPowerStatus(OnexStrings.Get("Power control unavailable"));return;
            }
            // Show a firmware readback without turning it into a manual setting.
            actualReadbackPl1=pl1;actualReadbackPl2=pl2;
            if(rememberedPl2==0){if(!Pl2ControlPolicy.IsWritable(pl1,pl2))rememberedPl2=Pl2ControlPolicy.Automatic(pl1);else RememberPl2(pl2);}
            loading=true;
            try
            {
                if(separate.HasValue)
                {
                    LightPl2Toggle.IsOn=separate.Value;
                    confirmedSeparatePl2=separate.Value;
                    PersistSeparatePl2(separate.Value);
                }
                if(remembered>=8&&remembered<=Pl2ControlPolicy.ExtendedManualCeiling)RememberPl2(remembered.Value);
                CacheManualPowerForScope(pl1,pl2);
                desiredPl1=pl1;desiredPl2=ClampManualPl2(pl1,pl2);
                LightPowerSlider.Value=pl1;
                ProjectPl2(pl1,desiredPl2);
                ShowAcknowledgedPower(pl1,pl2);
                LightPowerSlider.IsEnabled=available;LightPl2Toggle.IsEnabled=available;OnexPl2Slider.IsEnabled=available;
            }
            finally{loading=false;}
            if(!available)ShowPowerStatus(OnexStrings.Get("Power control unavailable"));else if(!powerError)ShowPowerStatus(ExternalPowerStatus());
        }
        private void PreparePowerReadoutText(int pl1,int pl2)
        {
            string language=OnexStrings.Language;
            bool separate=LightPl2Toggle.IsOn;
            if(cachedReadoutPl1==pl1&&cachedReadoutPl2==pl2&&cachedReadoutLanguage==language&&cachedReadoutSeparate==separate)return;
            cachedReadoutPl1=pl1;cachedReadoutPl2=pl2;cachedReadoutLanguage=language;cachedReadoutSeparate=separate;
            int selectable=ClampManualPl2(pl1,pl2);
            cachedPl1Text=pl1.ToString();cachedPl2Text=pl2.ToString();
            cachedPl2Summary=PowerSummary(pl1,separate?selectable:pl2);
            cachedExternalPowerStatus=!Pl2ControlPolicy.IsWritable(pl1,pl2)||separate&&selectable!=pl2
                ?OnexStrings.Format("Device PL2 is {0} W. Manual changes use PL2 up to {1} W.",pl2,ManualPl2Maximum(pl1)):null;
            powerReadoutCacheBuilds++;
        }
        private string ExternalPowerStatus()=>cachedExternalPowerStatus;
        private void ShowPowerStatus(string text)
        {
            OnexPowerStatus.Text=text??"";OnexPowerStatus.Visibility=string.IsNullOrEmpty(text)?Visibility.Collapsed:Visibility.Visible;
        }
        public void CompletePower(int pl1,int pl2,bool available,string error,bool? actualSeparate=null,int? actualRemembered=null)
        {
            busy=false;actualReadbackPl1=pl1;actualReadbackPl2=pl2;lastPowerAvailable=available;
            if(actualSeparate.HasValue)confirmedSeparatePl2=actualSeparate.Value;
            if(powerPending)
            {
                if(error!=null){powerError=true;ShowPowerStatus(error);ToolTipService.SetToolTip(LightPowerSlider,error);}
                timer.Start();return;
            }
            if(error!=null){loading=true;try{LightPl2Toggle.IsOn=confirmedSeparatePl2;}finally{loading=false;}}
            ApplyPower(pl1,pl2,available,actualSeparate,actualRemembered);
            if(error==null&&available)
            {
                confirmedSeparatePl2=LightPl2Toggle.IsOn;PersistSeparatePl2(confirmedSeparatePl2);
                if(confirmedSeparatePl2&&Pl2ControlPolicy.IsWritable(pl1,pl2))RememberPl2(pl2);
            }
            powerError=error!=null;
            if(powerError)ShowPowerStatus(error);else if(available)ShowPowerStatus(ExternalPowerStatus());
            ToolTipService.SetToolTip(LightPowerSlider,error??OnexStrings.Get("PL1 / PL2 read from the device"));
        }
        private string paletteName;
        private readonly OnexGlassSurfaces glassSurfaces=new OnexGlassSurfaces();
        public void ApplyPalette(ThemeColors theme,bool force=false)
        {
            string key=theme.Name+theme.EffectiveAccent().ToString()+theme.CardBackground.ToString()+theme.CardBorder.ToString()+theme.TextPrimary.ToString()+OnexGlassSurfaces.Enabled;if(!force&&paletteName==key)return;paletteName=key;
            ((SolidColorBrush)Resources["CardBackgroundBrush"]).Color=theme.CardBackground;((SolidColorBrush)Resources["CardBorderBrush"]).Color=theme.CardBorder;((SolidColorBrush)Resources["ThemeAccentBrush"]).Color=theme.EffectiveAccent();
            ((SolidColorBrush)Resources["OnexInactiveSliderTrackBrush"]).Color=GamingWidget.OnexInactiveSliderTrack(theme);
            var palette=OnexApprovedTheme.Find(theme.Name);
            ((SolidColorBrush)Resources["OnexHomePl1Brush"]).Color=palette?.Accent(OnexThemeRole.Power)??theme.EffectiveAccent();
            ((SolidColorBrush)Resources["OnexHomePl2Brush"]).Color=palette?.Accent(OnexThemeRole.Controller)??theme.EffectiveAccent();
            var ink=new SolidColorBrush(theme.TextPrimary);
            LightPowerPl2.Foreground=ink;LightPl2Separator.Foreground=ink;
            var fill=glassSurfaces.Get(theme.CardBackground,theme,OnexGlassSurfaces.Enabled);
            LightPowerCard.Background=fill;
            MediaRailH.Background=fill;MediaRailH.BorderBrush=new SolidColorBrush(theme.CardBorder);
        }
        public void ApplyBoost(bool enabled,bool available)
        {
            loading=true;try{LightCpuBoostToggle.IsOn=enabled;LightCpuBoostToggle.IsEnabled=available;}finally{loading=false;}
        }
        public void AttachMedia(Slider brightness,TextBlock brightnessValue,FontIcon brightnessIcon,Slider volume,TextBlock volumeValue,Button mute)
        {
            AttachMedia(RailHBrightnessHost,RailHVolumeHost,brightness,brightnessValue,brightnessIcon,volume,volumeValue,mute);
        }
        public void AttachMedia(Grid brightnessHost,Grid volumeHost,Slider brightness,TextBlock brightnessValue,FontIcon brightnessIcon,Slider volume,TextBlock volumeValue,Button mute)
        {
            if(_mediaBrightnessHost!=null&&_mediaBrightnessHost!=brightnessHost)
            {_mediaBrightnessHost.Children.Remove(brightness);_mediaBrightnessHost.Children.Remove(brightnessValue);_mediaBrightnessHost.Children.Remove(brightnessIcon);}
            if(_mediaVolumeHost!=null&&_mediaVolumeHost!=volumeHost)
            {_mediaVolumeHost.Children.Remove(volume);_mediaVolumeHost.Children.Remove(volumeValue);_mediaVolumeHost.Children.Remove(mute);}
            // Fixed icon/readout slots prevent percent text and mute glyph changes from
            // resizing either track while dragging. Both hosts reserve the same width.
            foreach(var host in new[]{brightnessHost,volumeHost})
            {host.UseLayoutRounding=false;if(host.Parent is Grid mediaRow)mediaRow.UseLayoutRounding=false;host.ColumnSpacing=4;host.ColumnDefinitions[0].Width=new GridLength(24);host.ColumnDefinitions[2].Width=new GridLength(36);}
            AddMedia(brightnessHost,brightness,brightnessValue,brightnessIcon);
            AddMedia(volumeHost,volume,volumeValue,mute);
            _mediaBrightnessHost=brightnessHost;_mediaVolumeHost=volumeHost;
        }
        public void AttachMediaActions(Button mixer,Button microphone)
        {
            RailHCenterHost.Spacing=6;
            foreach(var button in new[]{mixer,microphone})
            {
                if(!RailHCenterHost.Children.Contains(button)){Detach(button);RailHCenterHost.Children.Add(button);}
                button.Width=32;button.Height=32;button.Padding=new Thickness(0);button.Visibility=Visibility.Visible;
            }
        }
        private void AddMedia(Grid host,Slider slider,TextBlock value,FrameworkElement icon)
        {
            if(host.Children.Contains(slider)&&host.Children.Contains(value)&&host.Children.Contains(icon))return;
            Detach(slider);Detach(value);Detach(icon);
            slider.Orientation=Orientation.Horizontal;slider.Height=double.NaN;slider.Width=double.NaN;
            slider.HorizontalAlignment=HorizontalAlignment.Stretch;slider.Style=(Style)Resources["LightMediaSliderStyle"];
            // Pointer snapping stays precise; native keyboard/gamepad increments are independent.
            OnexInputGuard.ConfigurePercentage(slider);slider.MinWidth=70;
            value.Width=36;value.TextAlignment=TextAlignment.Left;value.HorizontalAlignment=HorizontalAlignment.Stretch;
            value.FontSize=14;value.VerticalAlignment=VerticalAlignment.Center;value.Foreground=new SolidColorBrush(Colors.White);
            icon.Width=24;icon.Height=28;icon.HorizontalAlignment=HorizontalAlignment.Center;
            if(icon is Button mediaButton){mediaButton.Padding=new Thickness(0);mediaButton.MinWidth=0;}
            icon.VerticalAlignment=VerticalAlignment.Center;icon.Margin=new Thickness(0);
            Grid.SetColumn(icon,0);Grid.SetColumn(slider,1);Grid.SetColumn(value,2);
            host.Children.Add(icon);host.Children.Add(slider);host.Children.Add(value);
        }
        private static void Detach(FrameworkElement element)
        {
            if(element.Parent is Panel p)p.Children.Remove(element);
            else if(element.Parent is Border b)b.Child=null;
            else if(element.Parent is ContentControl c)c.Content=null;
        }
    }
}
