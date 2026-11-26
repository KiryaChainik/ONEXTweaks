// SPDX-License-Identifier: AGPL-3.0-only
using System;
using ONEXTweaks.Power;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private OnexReleaseHome _releaseHome;
        private ScrollViewer _releaseOsd;
        private RadioButton _releaseOsdNav,_releasePowerNav;
        private int _releasePl1,_releasePl2;
        private bool _releaseLayoutUpdating,_onexPl2MigrationAttempted;
        private Grid _onexLegacyPowerGate;
        private int _onexLayoutThemeEpoch = -1;
        private string _onexLayoutLanguage;

        // Only the warm visibility callback uses this shortcut. Explicit layout, theme,
        // language and tab configuration changes retain their ordinary update paths.
        private bool ApplyTabPrefsOnWidgetOpen()
        {
            if (OnexFrontend.UsesOwnBackend && _releaseHome != null && _onexLayoutLanguage == OnexStrings.Language &&
                _onexLayoutThemeEpoch == _onexThemeEpoch && MainNavPanel != null)
            {
                var preferences = LoadOnexTabs();
                var navigation = OnexOrderedNavigation(preferences);
                if (_onexTabsUiVersion == OnexTabsActiveKey + preferences.Serialize() &&
                    navigation.Length == OnexTabIds.Length && MainNavPanel.Children.SequenceEqual(navigation) &&
                    navigation.All(tab => (tab.Visibility == Visibility.Visible) == !preferences.Hidden.Contains(tab.Tag as string)) &&
                    navigation.Any(tab => tab.IsChecked == true && tab.Visibility == Visibility.Visible))
                {
                    StartOnexInputDiagnosticsIfArmed();
                    return true;
                }
            }
            ApplyTabPrefs();
            return false;
        }

        private void EnsureOnexReleaseLayout()
        {
            if(_releaseLayoutUpdating || !(PerformanceScrollViewer?.Parent is Panel) || MainNavPanel==null)return;
            _releaseLayoutUpdating=true;
            string stage="start";
            try
            {
            if(_releaseHome==null)
            {
                stage="create home";
                _releaseHome=new OnexReleaseHome{Visibility=Visibility.Collapsed,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch};
                _releaseHome.PowerScope=()=>OnexPerformancePl2Scope;
                _releaseHome.CanStartPowerRequest=()=>!_onexPerformancePl1Busy&&!_onexPerformancePl2ValueBusy;
                _releaseHome.PowerEditing+=()=>{CancelOnexPerformancePl1Value();CancelOnexPerformancePl2Value();};
                _releaseHome.PowerRequested+=async(pl1,pl2)=>await SetOnexReleasePowerAsync(pl1,pl2);
                _releaseHome.BoostRequested+=enabled=>OnexBoostToggleChanged(enabled);
                stage="attach home";
                var host=(Panel)PerformanceScrollViewer.Parent;
                Grid.SetRow(_releaseHome,Grid.GetRow(PerformanceScrollViewer));
                Grid.SetColumn(_releaseHome,Grid.GetColumn(PerformanceScrollViewer));host.Children.Add(_releaseHome);
                stage="attach media";
                _releaseHome.AttachMedia(RailBrightnessSlider,RailBrightnessValue,RailBrightnessIcon,RailVolumeSlider,RailVolumeValue,RailMuteButton);
                _releaseHome.AttachMediaActions(RailMixerButton,RailMicButton);
                OnexInputGuard.SetCommit(RailBrightnessSlider,()=>{_=SendMediaLevelAsync("SetBrightnessLevel",(int)Math.Round(RailBrightnessSlider.Value));});
                OnexInputGuard.SetCommit(RailVolumeSlider,()=>{_=SendMediaLevelAsync("SetVolumeLevel",(int)Math.Round(RailVolumeSlider.Value));});
                WireOnexAudioOutputHold();
                WireOnexBrightnessDetails();
                OnexInputGuard.ConfigurePercentage(OSDOpacitySlider);OnexInputGuard.ConfigurePercentage(OSDBackgroundOpacitySlider);
                OnexInputGuard.SetCommit(OSDOpacitySlider,()=>{osdOpacity=(int)Math.Round(OSDOpacitySlider.Value);SaveOSDConfigToStorage();SendOSDConfigToHelper();});
                OnexInputGuard.SetCommit(OSDBackgroundOpacitySlider,()=>{osdBackgroundOpacity=(int)Math.Round(OSDBackgroundOpacitySlider.Value);StartOsdBackgroundDebounce();});
                OnexInputGuard.SetCommit(MsiChargeLimitSlider,()=>QueueOnexChargeSlider((int)Math.Round(MsiChargeLimitSlider.Value)));
                MediaRail.Visibility=Visibility.Collapsed;
                MediaRailColumn.Width=new GridLength(0);MediaRailGapColumn.Width=new GridLength(0);
                stage="attach combined home";
                MoveOnexHomeElement(OnexDesktopStatus,_releaseHome.FooterHost);
                MoveOnexHomeElement(QuickSettingsTilesContainer,_releaseHome.TilesHost);
                MoveOnexHomeElement(QuickMetricsRow,_releaseHome.StatsHost);
                MoveOnexHomeElement(QuickSettingsCustomizeButton,_releaseHome.FooterHost);
                MoveOnexHomeElement(QuickSettingsCustomizePanel,_releaseHome.FooterHost);
                QuickSettingsScrollViewer.Visibility=Visibility.Collapsed;
                var preferences=Windows.Storage.ApplicationData.Current.LocalSettings;
                if(!preferences.Values.ContainsKey("OnexStatsFirstV1"))
                {QuickMetricsToggle.IsOn=true;QuickMetricsRow.Visibility=Visibility.Visible;preferences.Values["OnexStatsFirstV1"]=true;}
                if(!preferences.Values.ContainsKey("OnexCombinedHomeV1"))
                {
                    // Joining the pages must preserve the loaded layout, including
                    // the standard first-install order and any user customization.
                    preferences.Values["OnexCombinedHomeV1"]=true;
                }
                RebuildQuickSettingsTiles();
                stage="attach profiles";EnsureOnexProfilesFrontend();
                stage="create OSD";
                _releaseOsd=OnexOverlayScrollViewer;
                _onexOverlayCaption=OnexOverlayConnectionCaption;
                _onexOverlayCaption.Foreground=tileTextBrush;
                OnexInputGuard.Wire(PerformanceOverlayComboBox,null);OnexInputGuard.Wire(OSPowerModeComboBox,null);
                OsdRendererComboBox.SelectedIndex=0;OsdRendererComboBox.IsEnabled=false;
                OsdPositionComboBox.IsEnabled=false;
                OSDPositionShiftToggle.IsEnabled=false;
                AdaptiveBrightnessToggle.IsEnabled=false;
                stage="create navigation";
                _releaseOsdNav=new RadioButton{Tag="OnexOsd",GroupName="MainNav",Style=(Style)Resources["NavRadioButtonStyle"],Content=new FontIcon{Glyph="\uE9D2",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=18}};
                ToolTipService.SetToolTip(_releaseOsdNav,global::XboxGamingBar.OnexStrings.Get("Overlay"));_releaseOsdNav.Checked+=NavRadioButton_Checked;
                var powerIcon=PerformanceNavItem.Content;PerformanceNavItem.Content=null;
                _releasePowerNav=new RadioButton{Tag="OnexPower",GroupName="MainNav",Style=(Style)Resources["NavRadioButtonStyle"],Content=powerIcon};
                PerformanceNavItem.Content=null;
                ToolTipService.SetToolTip(_releasePowerNav,global::XboxGamingBar.OnexStrings.Get("Power / profiles"));_releasePowerNav.Checked+=NavRadioButton_Checked;
                var homeIcon=QuickNavItem.Content;QuickNavItem.Content=null;
                PerformanceNavItem.Content=homeIcon;
                QuickNavItem.Content=new FontIcon{Glyph="\uECA5",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=18};
                ToolTipService.SetToolTip(PerformanceNavItem,global::XboxGamingBar.OnexStrings.Get("Performance"));
                ToolTipService.SetToolTip(ProfilesNavItem,global::XboxGamingBar.OnexStrings.Get("Profiles shortcut"));
                
                EnsureOnexOverlayEditor();EnsureOnexOverlayPresets();
                EnsureOnexDisplayWiring();
                EnsureOnexFanProfiles();
                EnsureOnexControllerFrontend();EnsureOnexKeyboardMouseFrontend();
                EnsureOnexLightingPage();
                FontFamily=_onexUiTypeface??new FontFamily(DefaultAppFont);
                ApplyOnexUiFonts(this);
                PerformanceNavItem.IsChecked=true;
                NavRadioButton_Checked(PerformanceNavItem,null);
                _onexLegacyPowerGate = GateOnexLegacyPage(PerformanceScrollViewer, _releasePowerNav);
                GateOnexLegacyPage(QuickSettingsScrollViewer, null);
                GateOnexLegacyPage(LegionScrollViewer, LegionNavItem);
                GateOnexLegacyPage(DisplayScrollViewer, DisplayNavItem);
                GateOnexLegacyPage(FanScrollViewer, FanNavItem);
                GateOnexLegacyPage(GameScrollViewer, ProfilesNavItem);
                GateOnexLegacyPage(SystemScrollViewer, SystemNavItem);
                OnexInputGuard.Wire(OnexStartupToggle);
                GateOnexLegacyPage(MediaRail, null);
                GateOnexLegacyPage(MediaRailH, null);
            }
            EnsureOnexProfilesFrontend();EnsureOnexPerformanceLayout();EnsureOnexProfileFooters();
            stage="reorder navigation";
            var tabPreferences=LoadOnexTabs();var navigation=OnexOrderedNavigation(tabPreferences);
            if(MainNavPanel.Children.Count!=navigation.Length||!MainNavPanel.Children.SequenceEqual(navigation))
            {
                foreach(var item in navigation)if(item.Parent is Panel parent)parent.Children.Remove(item);
                MainNavPanel.Children.Clear();
                foreach(var item in navigation)
                    if(!MainNavPanel.Children.Contains(item))MainNavPanel.Children.Add(item);
            }
            foreach(var item in navigation)
            {
                item.Visibility=tabPreferences.Hidden.Contains(item.Tag as string)?Visibility.Collapsed:Visibility.Visible;item.IsTabStop=false;TrackOnexTabPointer(item);
                item.GettingFocus-=OnexNavGettingFocus;item.GettingFocus+=OnexNavGettingFocus;
                if(item.Content is FrameworkElement icon){double iconSize=ReferenceEquals(item,_onexKeyboardMouseNav)?24:18;icon.Width=iconSize;icon.Height=iconSize;icon.Margin=new Thickness(0);icon.HorizontalAlignment=HorizontalAlignment.Center;icon.VerticalAlignment=VerticalAlignment.Center;AlignOnexNavInk(item,icon);}
            }
            EnsureValidActiveTab();EnsureOnexTabSettings();EnsureOnexNavigationGuide();EnsureOnexAbout();EnsureOnexDriverSettings();EnsureOnexLanguageSettings();EnsureOnexSettingsSectionOrder();EnsureOnexUnifiedFocus();
            QuickNavItem.Visibility=Visibility.Collapsed;ProfilesNavItem.Visibility=Visibility.Collapsed;ProfilesNavItem.IsTabStop=false;
            ToolTipService.SetToolTip(PerformanceNavItem,global::XboxGamingBar.OnexStrings.Get("Home"));
            if(WidgetThemes.TryGetValue(currentThemeName,out var homeTheme))_releaseHome.ApplyPalette(_onexAppliedTheme?.Name==currentThemeName?_onexAppliedTheme:homeTheme);
            StartOnexInputDiagnosticsIfArmed();
            stage="apply state";
            _releaseHome.ApplyPower(_releasePl1,_releasePl2,App.IsConnected&&OnexFrontend.PowerAvailable);
            _releaseHome.ApplyBoost(_onexBoostOn,App.IsConnected&&_onexBoostKnown&&!_onexBoostBusy);
            // The inherited page is large and can render before the replacement layout exists.
            // Keep the opaque cover until the complete ONEXTweaks page is attached.
            if (OnexStartupRoot != null && OnexStartupCover.Visibility != Visibility.Collapsed)
            {
                // Cold start: choose the configured page under the opaque cover,
                // before a first visible frame can expose the temporary Home page.
                ApplyOnexDefaultTab();
                _onexFirstRevealTab=OnexOrderedNavigation(LoadOnexTabs()).FirstOrDefault(item=>item.IsChecked==true)?.Tag as string;
                OnexStartupCover.Visibility = Visibility.Collapsed;
                OnexStartupRoot.IsHitTestVisible = true;
            }
            _onexLayoutLanguage=OnexStrings.Language;
            _onexLayoutThemeEpoch=_onexThemeEpoch;
            }
            catch(Exception ex){Logger.Error("Release layout failed at "+stage+": "+ex);throw;}
            finally{_releaseLayoutUpdating=false;}
        }
        private static void AlignOnexNavInk(RadioButton tab,FrameworkElement icon)
        {
            // Native transparent captures measure the visible glyph, not the Viewbox.
            // PathIcon's internal drawing origin differs by glyph. Preserve artwork,
            // size and the parent's selected scale; correct only its optical position.
            double x,y;
            switch(tab.Tag as string)
            {
                case "Performance": x=-1; y=-2d/3; break;
                case "OnexPower": x=-2d/3; y=-2d/3; break;
                case "Display": x=-2d/3; y=-1; break;
                case "Fan": x=-1; y=-2d/3; break;
                case "Game": x=-1; y=-1; break;
                case "System": x=-2d/3; y=-2d/3; break;
                default:return; // Bitmap controller and FontIcon OSD already center correctly.
            }
            if(icon.RenderTransform is TranslateTransform old&&old.X==x&&old.Y==y)return;
            icon.RenderTransform=new TranslateTransform{X=x,Y=y};
        }
        private void OnexNavGettingFocus(UIElement sender,Windows.UI.Xaml.Input.GettingFocusEventArgs e){e.Cancel=true;}
        private static Grid GateOnexLegacyPage(FrameworkElement page, RadioButton selectedTab)
        {
            if (!(page.Parent is Panel parent)) return null;
            int index = parent.Children.IndexOf(page);
            var gate = new Grid { Visibility = Visibility.Collapsed };
            Grid.SetRow(gate, Grid.GetRow(page)); Grid.SetColumn(gate, Grid.GetColumn(page));
            Grid.SetRowSpan(gate, Grid.GetRowSpan(page)); Grid.SetColumnSpan(gate, Grid.GetColumnSpan(page));
            parent.Children.Remove(page);
            Grid.SetRow(page, 0); Grid.SetColumn(page, 0); Grid.SetRowSpan(page, 1); Grid.SetColumnSpan(page, 1);
            gate.Children.Add(page); parent.Children.Insert(index, gate);
            if (selectedTab != null)
                gate.SetBinding(VisibilityProperty, new Windows.UI.Xaml.Data.Binding {
                    Source = selectedTab, Path = new PropertyPath("IsChecked"),
                    Converter = new XboxGamingBar.Converter.BooleanToVisibilityConverter()
                });
            return gate;
        }
        private static void MoveOnexHomeElement(FrameworkElement element,Panel host)
        {
            if(host.Children.Contains(element))return;
            if(element.Parent is Panel old)old.Children.Remove(element);
            else if(element.Parent is Border border)border.Child=null;
            element.Margin=new Thickness(0);host.Children.Add(element);
        }
        private void PlaceOnexMedia(bool home)
        {
            if(_releaseHome==null)return;
            _releaseHome.AttachMedia(RailBrightnessSlider,RailBrightnessValue,RailBrightnessIcon,RailVolumeSlider,RailVolumeValue,RailMuteButton);
                _releaseHome.AttachMediaActions(RailMixerButton,RailMicButton);
            MediaRailH.Visibility=Visibility.Collapsed;WireOnexMediaFocus(true);
        }
        private Control OnexFirstQuickControl()
        {
            if(_firstQuickTile is Control c)return c;
            if(_firstQuickTile is Panel p)return p.Children.OfType<Control>().FirstOrDefault();
            return null;
        }
        private void WireOnexMediaFocus(bool home)
        {
            var first=OnexFirstQuickControl();
            var secondary=_releaseHome?.FindName("OnexPl2Slider") as Slider;
            var mode=_releaseHome?.FindName("LightPl2Toggle") as ToggleSwitch;
            Control above=mode?.IsOn==true?(Control)secondary:_releaseHome?.PowerInput;
            RailBrightnessSlider.XYFocusUp=above;RailMuteButton.XYFocusUp=above;RailVolumeSlider.XYFocusUp=above;
            RailBrightnessSlider.XYFocusRight=RailMuteButton;RailMuteButton.XYFocusLeft=RailBrightnessSlider;
            RailMuteButton.XYFocusRight=RailVolumeSlider;RailVolumeSlider.XYFocusLeft=RailMuteButton;
            RailVolumeSlider.XYFocusRight=RailMixerButton;RailMixerButton.XYFocusLeft=RailVolumeSlider;
            RailMixerButton.XYFocusRight=RailMicButton;RailMicButton.XYFocusLeft=RailMixerButton;
            RailMixerButton.XYFocusUp=above;RailMicButton.XYFocusUp=above;
            RailMixerButton.XYFocusDown=first;RailMicButton.XYFocusDown=first;
            var power=_releaseHome?.PowerInput;
            RailBrightnessSlider.XYFocusDown=first;RailMuteButton.XYFocusDown=first;RailVolumeSlider.XYFocusDown=first;
            _releaseHome?.WireNavigation(null,RailBrightnessSlider,first);
            if(first!=null)first.XYFocusLeft=null;
        }
        private static bool ContainsHeading(DependencyObject root,string[] labels)
        {
            if(root is TextBlock text&&labels.Contains(text.Text))return true;
            foreach(var child in OnexProfileLogicalChildren(root))if(ContainsHeading(child,labels))return true;
            return false;
        }
        private static void AddPendingCaption(ScrollViewer viewer,string message)
        {
            if(viewer==null)return;
            var old=viewer.Content as UIElement;
            viewer.Content=null;
            var panel=new StackPanel{Padding=new Thickness(16,8,16,16),Spacing=12};
            panel.Children.Add(new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Colors.White)});
            if(old!=null)panel.Children.Add(new ContentControl{Content=old,IsEnabled=false,HorizontalContentAlignment=HorizontalAlignment.Stretch,HorizontalAlignment=HorizontalAlignment.Stretch});viewer.Content=panel;
        }
        private void ApplyOnexReleasePowerState(string payload)
        {
            if(JsonObject.TryParse(payload,out var state))
            {
                var fanTemperature=state.GetNamedValue("cpuTemperatureC",JsonValue.CreateNullValue());_onexFanCpuTemperature=fanTemperature.ValueType==JsonValueType.Number?(double?)fanTemperature.GetNumber():null;RefreshOnexFanLiveValues();
                _releasePl1=(int)state.GetNamedNumber("pl1Watts",0);
                _releasePl2=(int)state.GetNamedNumber("pl2Watts",0);
                bool? separate=state.TryGetValue("separatePl2",out var mode)&&mode.ValueType==JsonValueType.Boolean?(bool?)mode.GetBoolean():null;
                int? remembered=state.TryGetValue("rememberedPl2Watts",out var manual)&&manual.ValueType==JsonValueType.Number?(int?)manual.GetNumber():null;
                var local=Windows.Storage.ApplicationData.Current.LocalSettings.Values;if(separate.HasValue)local["TDPBoostEnabled"]=separate.Value;if(remembered.HasValue)local["TDPBoostFPPT"]=remembered.Value;
                SyncOnexPerformancePowerState(_releasePl1,_releasePl2,separate,remembered);
                _releaseHome?.ApplyPower(_releasePl1,_releasePl2,App.IsConnected&&OnexFrontend.PowerAvailable,separate,remembered);
                if(Pl2ControlPolicy.IsWritable(_releasePl1,_releasePl2)&&!separate.HasValue&&!_onexPl2MigrationAttempted&&_releaseHome!=null&&App.IsConnected){_onexPl2MigrationAttempted=true;_=InitializeOnexPl2SelectionAsync();}
            }
        }
        private async Task InitializeOnexPl2SelectionAsync()
        {
            try
            {
                var reply=await App.SendMessageAsync(new ValueSet{{"OnexPl2SelectionInitialize",_releaseHome.SeparatePl2Enabled},{"OnexRememberedPl2Watts",_releaseHome.RememberedPl2Watts}});
                if(reply==null||reply.ContainsKey("Error"))throw new InvalidOperationException(reply?.ContainsKey("Error")==true?reply["Error"].ToString():"PL2 migration disconnected");
                ApplyOnexReleasePowerState((string)reply["OnexState"]);ApplyOnexProfilesState((string)reply["OnexProfilesState"]);
            }
            catch(Exception error){Logger.Warn("PL2 metadata migration: "+error.Message);}
        }
        private long _onexHomePowerWriteRpcs,_onexHomePowerFallbackReads;
        private async Task SetOnexReleasePowerAsync(int pl1,int pl2)
        {
            string error=null;JsonObject acknowledged=null;string requestScope=_releaseHome.CapturedPowerScope,requestProfile=OnexFrontend.ProfileId;
            try
            {
                if(requestScope!=OnexPerformancePl2Scope)throw new InvalidOperationException("Power input context changed");
                _onexHomePowerWriteRpcs++;
                var reply=await App.SendMessageAsync(new ValueSet{["Command"]=(int)Command.Set,["Function"]=(int)Function.TDP,["Content"]=pl1,["OnexPl2Watts"]=pl2,["OnexUserInput"]=true,["OnexPreferActiveProfile"]=true,["OnexPowerSource"]="home-slider",["OnexProfileId"]=requestProfile,["OnexPowerOnAc"]=requestScope.EndsWith(":ac",StringComparison.Ordinal),["OnexSeparatePl2"]=_releaseHome.SeparatePl2Enabled,["OnexRememberedPl2Watts"]=_releaseHome.RememberedPl2Watts});
                if(reply==null)throw new InvalidOperationException("Power connection unavailable");
                if(reply.TryGetValue("Error",out var failure))throw new InvalidOperationException(failure.ToString());
                // The production Set reply contains PowerAgent's verified actual
                // readback. App already applied its OnexProfilesState acknowledgment.
                // Do not issue two redundant read RPCs and repaint the profile again.
                if(reply.TryGetValue("OnexState",out var readback)&&JsonObject.TryParse(readback as string,out var actual))acknowledged=actual;
                else{_onexHomePowerFallbackReads++;acknowledged=await ReadOnexStateAsync();}
                _releasePl1=(int)acknowledged.GetNamedNumber("pl1Watts");_releasePl2=(int)acknowledged.GetNamedNumber("pl2Watts");
            }
            catch(Exception e){error=e.Message;try{_onexHomePowerFallbackReads++;acknowledged=await ReadOnexStateAsync();_releasePl1=(int)acknowledged.GetNamedNumber("pl1Watts");_releasePl2=(int)acknowledged.GetNamedNumber("pl2Watts");}catch{}}
            finally
            {
                bool? mode=acknowledged!=null&&acknowledged.TryGetValue("separatePl2",out var m)&&m.ValueType==JsonValueType.Boolean?(bool?)m.GetBoolean():null;
                int? remembered=acknowledged!=null&&acknowledged.TryGetValue("rememberedPl2Watts",out var v)&&v.ValueType==JsonValueType.Number?(int?)v.GetNumber():null;
                _releaseHome.CompletePower(_releasePl1,_releasePl2,App.IsConnected&&OnexFrontend.PowerAvailable,error,mode,remembered);
                if(acknowledged!=null)ApplyOnexReleasePowerState(acknowledged.Stringify());
            }
        }
    }
}
