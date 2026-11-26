// SPDX-License-Identifier: AGPL-3.0-only
using System;
using ONEXTweaks.Power;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private Button _onexProfileGlobalEdit,_onexProfileGameEdit,_onexProfilesShortcut;
        private string _onexProfilesRenderError;
        private JsonObject _onexProfilesState;private string _onexProfilesJson;private bool _onexProfilesSyncing,_onexProfilesBusy,_onexProfilesPageReady;
        private IEnumerable<JsonObject> OnexProfiles=>_onexProfilesState?.GetNamedArray("entries").Select(v=>v.GetObject())??Enumerable.Empty<JsonObject>();
        private JsonObject OnexProfileByName(string name)=>OnexProfiles.FirstOrDefault(p=>p.GetNamedString("name")==name);
        private bool OnexHasGame=>_onexProfilesState!=null&&_onexProfilesState.TryGetValue("currentGame",out var g)&&g.ValueType==JsonValueType.Object;
        private JsonObject OnexActiveProfile=>OnexProfiles.FirstOrDefault(p=>p.GetNamedString("id")==_onexProfilesState?.GetNamedString("activeId","global"));
        private static IEnumerable<DependencyObject> OnexProfileLogicalChildren(DependencyObject element)
        {
            if(element is Panel panel)return panel.Children.Cast<DependencyObject>();
            if(element is Border border&&border.Child!=null)return new DependencyObject[]{border.Child};
            if(element is ContentControl content&&content.Content is DependencyObject child)return new[]{child};
            return Enumerable.Empty<DependencyObject>();
        }
        private static bool ContainsOnexProfileElement(DependencyObject root,DependencyObject wanted)=>root==wanted||OnexProfileLogicalChildren(root).Any(child=>ContainsOnexProfileElement(child,wanted));
        private static Panel OnexProfileLogicalPanel(DependencyObject root,DependencyObject wanted)
        {
            if(root is Panel panel&&panel.Children.Any(child=>child==wanted))return panel;
            foreach(var child in OnexProfileLogicalChildren(root)){var found=OnexProfileLogicalPanel(child,wanted);if(found!=null)return found;}return null;
        }
        private void EnsureOnexProfilesFrontend()
        {
            if(_onexProfilesPageReady||!(PerformanceScrollViewer.Content is Panel target))return;
            string setup="cards";
            try
            {
            foreach(string name in new[]{"ActiveProfileCard","GlobalProfilePairs","GameProfileCard","AllGameProfilesContainer","PowerSourceProfileCard","ProfileSettingsContent"})
            {
                setup=name;var element=FindName(name) as FrameworkElement;if(element==null)throw new InvalidOperationException("Missing XAML element "+name);
                if(!ContainsOnexProfileElement(target,element))throw new InvalidOperationException("Profile card missing from Performance: "+name);
            }
            setup="supported flags";
            foreach(var pair in new[]{Tuple.Create("TDP",ProfileSaveTDPCheckBox),Tuple.Create("CPUBoost",ProfileSaveCPUBoostCheckBox),Tuple.Create("FPSLimit",ProfileSaveFPSLimitCheckBox),Tuple.Create("OSPowerMode",ProfileSaveOSPowerModeCheckBox),Tuple.Create("OverlayLevel",ProfileSaveOverlayLevelCheckBox)})pair.Item2.IsEnabled=true;
            ProfileSaveFanCheckBox.Visibility=Visibility.Collapsed;
            setup="unsupported flags";
            foreach(var checkbox in new[]{ProfileSaveCPUEPPCheckBox,ProfileSaveCPUStateCheckBox,ProfileSaveAMDFeaturesCheckBox,ProfileSaveHDRCheckBox,ProfileSaveCPUAffinityCheckBox}){checkbox.IsEnabled=false;checkbox.IsChecked=false;ToolTipService.SetToolTip(checkbox,global::XboxGamingBar.OnexStrings.Get("Not connected to ONEXPLAYER yet"));}
            setup="single profile workflow";
            LegacyGlobalProfileCard.Visibility=Visibility.Collapsed;
            GameProfileCard.Visibility=Visibility.Collapsed;
            ActiveProfileCard.Visibility=Visibility.Collapsed;PowerSourceProfileCard.Visibility=Visibility.Collapsed;
            EnsureOnexPerformanceProfilePicker();_onexProfilesPageReady=true;
            // Pipe state may arrive before the cards are attached during a cold start.
            // Populate them now even if the backend snapshot has not changed.
            if(_onexProfilesState!=null){UpdateOnexProfileDisplay();UpdateAllGameProfilesDisplay();}
            }catch(Exception ex){Logger.Error("Profiles setup failed at "+setup+": "+ex);throw new InvalidOperationException("Profiles setup "+setup+": "+ex.Message,ex);}
        }
        private static bool IsOnexProfileDown(Windows.System.VirtualKey key)=>key==Windows.System.VirtualKey.Down||key==Windows.System.VirtualKey.GamepadDPadDown||key==Windows.System.VirtualKey.GamepadLeftThumbstickDown;
        private static bool IsOnexProfileVertical(Windows.System.VirtualKey key)=>IsOnexProfileDown(key)||key==Windows.System.VirtualKey.Up||key==Windows.System.VirtualKey.GamepadDPadUp||key==Windows.System.VirtualKey.GamepadLeftThumbstickUp;
        private bool MoveOnexProfileFocus(Control source,bool down)
        {
            Control game=GameProfileCard.Visibility==Visibility.Visible?(Control)_onexProfileGameEdit:null;Control next=null;
            if(source==PerGameProfileToggle)next=down?(Control)TDPSlider:PerGameProfileToggle;
            else if(source==_onexProfileGlobalEdit)next=down?(game??PerfSavedProfilesExpandButton):(Control)FPSStateCycleButton;
            else if(source==_onexProfileGameEdit)next=down?(Control)PerfSavedProfilesExpandButton:_onexProfileGlobalEdit;
            else if(source==PerfSavedProfilesExpandButton)next=down?(PerfSavedProfilesContent.Visibility==Visibility.Visible?(Control)ProfileSortComboBox:ProfileSettingsExpandToggle):(game??_onexProfileGlobalEdit);
            else if(source==PowerSourceProfileToggle)next=down?(Control)ProfileSettingsExpandToggle:(FirstGameProfileFocusTarget()??(PerfSavedProfilesContent.Visibility==Visibility.Visible?(Control)ProfileSortComboBox:PerfSavedProfilesExpandButton));
            return next?.Focus(FocusState.Keyboard)==true;
        }
        private void ApplyOnexProfilesState(string json)
        {
            if(_onexNavigationAuditHoldingState)return;
            if(json==_onexProfilesJson){if(_onexProfilesPageReady&&GlobalProfilePairs.Children.Count==0)UpdateOnexProfileDisplay();return;}
            if(!JsonObject.TryParse(json,out var state))return;OnexPerformanceScopeChanged(state.GetNamedString("activeId","global"),state.GetNamedBoolean("onAc",false));_onexProfilesJson=json;_onexProfilesState=state;_onexProfilesSyncing=true;isLoadingProfileSettings=true;
            try
            {
                // Own cards already render the JSON entries below. Mirroring their compatibility
                // XML into the legacy snapshot triggered XmlSerializer code generation on the UI
                // thread (captured during the input freeze), rebuilding a second representation.
                string name="No game detected";
                if(OnexHasGame){var game=state.GetNamedObject("currentGame");name=OnexProfiles.FirstOrDefault(p=>p.GetNamedString("path")==game.GetNamedString("path"))?.GetNamedString("name")??game.GetNamedString("name");}
                currentGameName=name;DetectedGameText.Text=OnexHasGame?name:global::XboxGamingBar.OnexStrings.Get("No app/game detected");PerGameProfileToggle.IsOn=OnexHasGame&&state.GetNamedString("activeId")==OnexProfiles.FirstOrDefault(p=>p.GetNamedString("path")==state.GetNamedObject("currentGame").GetNamedString("path"))?.GetNamedString("id");
                var flags=state.GetNamedObject("flags");ProfileSaveTDPCheckBox.IsChecked=flags.GetNamedBoolean("TDP");ProfileSaveCPUBoostCheckBox.IsChecked=flags.GetNamedBoolean("CPUBoost");ProfileSaveFPSLimitCheckBox.IsChecked=flags.GetNamedBoolean("FPSLimit");ProfileSaveOSPowerModeCheckBox.IsChecked=flags.GetNamedBoolean("OSPowerMode");ProfileSaveFanCheckBox.IsChecked=flags.GetNamedBoolean("Fan");ProfileSaveOverlayLevelCheckBox.IsChecked=flags.GetNamedBoolean("OverlayLevel");ProfileSaveResolutionCheckBox.IsChecked=flags.GetNamedBoolean("Resolution",true);ProfileSaveRefreshRateCheckBox.IsChecked=flags.GetNamedBoolean("RefreshRate",true);SyncProfileSettingsBackingFields();
                PowerSourceProfileToggle.IsOn=OnexActiveProfile?.GetNamedBoolean("split",false)==true;
                _onexPerformanceProfilePicker?.Apply(state);RefreshOnexSourceEditors();UpdateActiveProfileIndicator();UpdateOnexProfileDisplay();UpdateAllGameProfilesDisplay();
            }
            finally{isLoadingProfileSettings=false;_onexProfilesSyncing=false;ApplyOnexProfileAvailability();ApplyOnexTileAvailability();}
        }
        private void ApplyOnexProfileAvailability(){if(PerGameProfileToggle!=null)PerGameProfileToggle.IsEnabled=App.IsConnected&&OnexHasGame&&!_onexProfilesBusy;if(PowerSourceProfileToggle!=null)PowerSourceProfileToggle.IsEnabled=App.IsConnected&&_onexProfilesState!=null&&!_onexProfilesBusy;}
        private async Task MutateOnexProfilesAsync(JsonObject mutation)
        {
            if(_onexProfilesBusy||_onexProfilesSyncing||!App.IsConnected)return;_onexProfilesBusy=true;ApplyOnexProfileAvailability();
            try{var r=await SendOnexHomeRequestAsync(new ValueSet{["OnexProfilesSet"]=mutation.Stringify()});ApplyOnexProfilesState((string)r["OnexProfilesState"]);}
            catch(Exception ex){ShowOnexDesktopStatus(ex.Message);_onexProfilesJson=null;if(_onexProfilesState!=null)ApplyOnexProfilesState(_onexProfilesState.Stringify());}
            finally{_onexProfilesBusy=false;ApplyOnexProfileAvailability();ApplyOnexTileAvailability();}
        }
        private Task SetOnexProfileUseAsync(bool enabled)=>MutateOnexProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("use")},{"enabled",JsonValue.CreateBooleanValue(enabled)}});
        private Task SetOnexProfileSplitAsync(bool enabled)=>MutateOnexProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("split")},{"enabled",JsonValue.CreateBooleanValue(enabled)}});
        private void SendOnexProfileFlags()
        {
            if(_onexProfilesState==null||_onexProfilesSyncing)return;var flags=new JsonObject{{"TDP",JsonValue.CreateBooleanValue(_saveTDP)},{"CPUBoost",JsonValue.CreateBooleanValue(_saveCPUBoost)},{"FPSLimit",JsonValue.CreateBooleanValue(_saveFPSLimit)},{"OSPowerMode",JsonValue.CreateBooleanValue(_saveOSPowerMode)},{"Fan",JsonValue.CreateBooleanValue(_saveFan)},{"OverlayLevel",JsonValue.CreateBooleanValue(_saveOverlayLevel)},{"Resolution",JsonValue.CreateBooleanValue(_saveResolution)},{"RefreshRate",JsonValue.CreateBooleanValue(_saveRefreshRate)},{"Display",JsonValue.CreateBooleanValue(true)}};_=MutateOnexProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("flags")},{"flags",flags}});
        }
        private Task DeleteOnexProfileAsync(string name){var entry=OnexProfileByName(name);return entry==null?Task.CompletedTask:MutateOnexProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",entry["id"]}});}
        private static bool OnexProfileSeparatePl2(JsonObject values)=>values.TryGetValue("separatePl2",out var mode)&&mode.ValueType==JsonValueType.Boolean?mode.GetBoolean():values.GetNamedNumber("pl2")>values.GetNamedNumber("pl1");
        private List<(string Label,string Value)> OnexProfilePairs(JsonObject entry,bool ac)
        {
            var values=entry.GetNamedObject(ac?"ac":"dc");var flags=_onexProfilesState.GetNamedObject("flags");var pairs=new List<(string,string)>();bool Show(string key)=>!entry.GetNamedBoolean("useSharedPolicy",true)||flags.GetNamedBoolean(key,true)||entry.GetNamedString("id")=="global"||entry.GetNamedArray("localGroups",new JsonArray()).Any(item=>item.GetString()==key);
            if(Show("TDP")){pairs.Add(("TDP",values.GetNamedNumber("pl1")+" W"));pairs.Add(("PL2",OnexProfileSeparatePl2(values)?values.GetNamedNumber("pl2")+" W":"Off ("+Pl2ControlPolicy.Automatic((int)values.GetNamedNumber("pl1"))+" W)"));}
            if(Show("CPUBoost")){pairs.Add(("CPU Boost",new[]{"Off","Enabled","Aggressive","Efficient","Efficient aggr.","Guaranteed","Eff. guaranteed"}[(int)values.GetNamedNumber("boost")]));pairs.Add(("Cores",new[]{"All","Only P","Prefer P","Only E","Prefer E","Auto"}[(int)values.GetNamedNumber("scheduling")]));int clock=(int)values.GetNamedNumber("clockMhz");if(clock>0)pairs.Add(("CPU limit",clock+" MHz"));}
            if(Show("FPSLimit")){var fps=values.GetNamedObject("fps");pairs.Add(("FPS Limit",fps.GetNamedBoolean("enabled")?fps.GetNamedNumber("value")+" ("+fps.GetNamedString("provider")+")":"Off"));}
            if(Show("OSPowerMode"))pairs.Add(("Power Mode",new[]{"Efficiency","Balanced","Performance"}[(int)values.GetNamedNumber("powerMode")]));
            if(Show("OverlayLevel"))pairs.Add(("Overlay",OnexOverlayNames[(int)values.GetNamedNumber("overlay")]));
            if(values.TryGetValue("display",out var displayValue)&&displayValue.ValueType==JsonValueType.Object)
            {
                var display=displayValue.GetObject();var displayMode=OnexDisplayObject(display,"mode");
                if(displayMode!=null){if(Show("Resolution"))pairs.Add(("Resolution",displayMode.GetNamedNumber("width")+" x "+displayMode.GetNamedNumber("height")));if(Show("RefreshRate"))pairs.Add(("Refresh rate",displayMode.GetNamedNumber("hz")+" Hz"));}
                string Intel(string key,string[] names)=>display.TryGetValue(key,out var token)&&token.ValueType==JsonValueType.Number&&token.GetNumber()>=0&&token.GetNumber()<names.Length?names[(int)token.GetNumber()]:"Windows";
                pairs.Add(("Intel Frame Gen",Intel("frameGeneration",new[]{"App Choice","2X","3X","4X"})));
                pairs.Add(("Intel VRR",Intel("vrr",new[]{"Off","On"})));
                pairs.Add(("Intel latency",Intel("lowLatency",new[]{"Off","On","On + Boost"})));

            }
            return pairs;
        }
        private FrameworkElement RenderOnexProfileValues(JsonObject entry)
        {
            if(!entry.GetNamedBoolean("split"))return RenderPairsGrid(OnexProfilePairs(entry,true));
            var ac=OnexProfilePairs(entry,true);var dc=OnexProfilePairs(entry,false);var grid=new Grid{Margin=new Thickness(0,4,0,0)};grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition());
            var labels=ac.Select(p=>p.Label).Union(dc.Select(p=>p.Label)).ToArray();for(int i=0;i<=labels.Length;i++)grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});AddTextBlock(grid,0,1,"Plugged",10,"#FFD700");AddTextBlock(grid,0,2,"Battery",10,"#FF6B6B");
            for(int i=0;i<labels.Length;i++){AddTextBlock(grid,i+1,0,labels[i],10,"#AAAAAA",margin:new Thickness(0,3,8,0));AddTextBlock(grid,i+1,1,ac.FirstOrDefault(p=>p.Label==labels[i]).Value??"—",10,"#FFFFFF");AddTextBlock(grid,i+1,2,dc.FirstOrDefault(p=>p.Label==labels[i]).Value??"—",10,"#FFFFFF");}return grid;
        }
        private void UpdateOnexProfileDisplay()
        {
            if(_onexProfilesState==null||!_onexProfilesPageReady)return;
            try
            {
            var global=OnexProfileByName("global");var globalValues=RenderOnexProfileValues(global);GlobalProfileSimple.Visibility=Visibility.Collapsed;GlobalProfileACDC.Visibility=Visibility.Collapsed;GlobalProfileACDCShared.Visibility=Visibility.Collapsed;GlobalProfilePairs.Visibility=Visibility.Visible;GlobalProfilePairs.Children.Clear();GlobalProfilePairs.Children.Add(globalValues);
            var active=OnexActiveProfile;bool game=active!=null&&active.GetNamedString("id")!="global";GameProfileCard.Visibility=Visibility.Collapsed;LegacyGlobalProfileCard.Visibility=Visibility.Collapsed;GameProfileWithPowerSource.Visibility=Visibility.Collapsed;GameProfileWithoutPowerSource.Visibility=Visibility.Visible;
            if(GameProfileTDPModeLabel.Parent is FrameworkElement legacy)legacy.Visibility=Visibility.Collapsed;GameProfileACDCShared.Visibility=Visibility.Collapsed;GameProfilePairs.Children.Clear();if(game){GameProfileTitleNoPower.Text=active.GetNamedString("name");GameProfilePairs.Children.Add(RenderOnexProfileValues(active));}
            UpdatePowerSourceProfileScopeText();_onexProfilesRenderError=null;
            }catch(Exception ex){_onexProfilesRenderError=ex.ToString();_onexProfilesJson=null;Logger.Error("ONEX profile rendering: "+ex);}
        }
        private async Task ShowOnexProfileEditorAsync(JsonObject entry,Button anchor)
        {
            if(entry==null||_onexDetailsDialog!=null)return;
            var ac=JsonObject.Parse(entry.GetNamedObject("ac").Stringify());var dc=JsonObject.Parse(entry.GetNamedObject("dc").Stringify());bool loading=true;int selectedSide=0;
            var panel=new StackPanel{Spacing=10};var split=new ToggleSwitch{Header=global::XboxGamingBar.OnexStrings.Get("Separate values for plugged in and battery"),IsOn=entry.GetNamedBoolean("split")};var side=OnexSettingsChoice(global::XboxGamingBar.OnexStrings.Get("Editing power state"));side.ItemsSource=new[]{OnexStrings.Get("Plugged in"),OnexStrings.Get("Battery")};side.SelectedIndex=0;side.IsEnabled=split.IsOn;panel.Children.Add(split);panel.Children.Add(side);
            var pl1=new Slider{Name="OnexProfilePl1",Minimum=8,Maximum=40,StepFrequency=1,IsThumbToolTipEnabled=true};
            var pl2=new Slider{Name="OnexProfilePl2",Minimum=8,Maximum=41,StepFrequency=1,IsThumbToolTipEnabled=true};
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(pl1,OnexStrings.Get("TDP · PL1"));
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(pl2,OnexStrings.Get("PL2"));
            var separate=new ToggleSwitch{Name="OnexProfileSeparatePl2",Header=OnexStrings.Get("Separate PL2")};
            var primaryTitle=new TextBlock();
            var secondaryTitle=new TextBlock();
            var primaryTrack=new Grid();
            var secondaryTrack=new Grid();
            primaryTrack.Children.Add(pl1);
            secondaryTrack.Children.Add(pl2);
            var powerCard=new StackPanel{Spacing=8};
            powerCard.Children.Add(separate);
            powerCard.Children.Add(primaryTitle);
            powerCard.Children.Add(primaryTrack);
            powerCard.Children.Add(secondaryTitle);
            powerCard.Children.Add(secondaryTrack);
            panel.Children.Add(OnexSettingsCard(powerCard));
            var powerScale=new OnexPowerScale(primaryTrack,pl1,secondaryTrack,pl2,12,
                (Windows.UI.Xaml.Media.Brush)Resources["OnexInactiveSliderTrackBrush"]);
            ComboBox Choice(string header,string[] values){var c=OnexSettingsChoice(header);c.ItemsSource=values.Select(OnexStrings.Get).ToArray();return c;}
            var boost=Choice("CPU Boost",new[]{"Disabled","Enabled","Aggressive","Efficient enabled","Efficient aggressive","Aggressive at guaranteed","Efficient at guaranteed"});var scheduling=Choice("Core preference",new[]{"All cores","Only performance cores","Prefer performance cores","Only efficiency cores","Prefer efficiency cores","Automatic"});panel.Children.Add(OnexSettingsCard(OnexSettingsColumns(boost,scheduling)));
            var clockOn=new ToggleSwitch{Header=global::XboxGamingBar.OnexStrings.Get("CPU frequency limit")};var clock=new Slider{Minimum=1000,Maximum=6000,StepFrequency=100,SmallChange=100,LargeChange=500,IsThumbToolTipEnabled=true};var clockPanel=new StackPanel{Spacing=4};clockPanel.Children.Add(clockOn);clockPanel.Children.Add(clock);panel.Children.Add(OnexSettingsCard(clockPanel));
            var fpsOn=new ToggleSwitch{Header=global::XboxGamingBar.OnexStrings.Get("FPS Limit")};var fpsProvider=Choice("Limiter",new[]{"Intel","RTSS"});var fps=new Slider{Name="OnexProfileFps",Minimum=30,Maximum=300,StepFrequency=1,IsThumbToolTipEnabled=true};var fpsPanel=new StackPanel{Spacing=4};fpsPanel.Children.Add(OnexSettingsColumns(fpsOn,fpsProvider));fpsPanel.Children.Add(fps);panel.Children.Add(OnexSettingsCard(fpsPanel));
            var mode=Choice("Power Mode",new[]{"Efficiency","Balanced","Performance"});var overlay=Choice("Overlay",OnexOverlayNames);panel.Children.Add(OnexSettingsCard(mode));panel.Children.Add(overlay);
            void Load(JsonObject values)
            {
                loading=true;separate.IsOn=OnexProfileSeparatePl2(values);pl1.Value=values.GetNamedNumber("pl1");pl2.Maximum=Pl2ControlPolicy.ManualCeiling((int)pl1.Value);pl2.Minimum=separate.IsOn?pl1.Value:8;pl2.Value=values.GetNamedNumber("pl2");pl2.IsEnabled=separate.IsOn;boost.SelectedIndex=(int)values.GetNamedNumber("boost");scheduling.SelectedIndex=(int)values.GetNamedNumber("scheduling");int mhz=(int)values.GetNamedNumber("clockMhz");clockOn.IsOn=mhz>0;clock.Value=mhz>0?mhz:2500;clock.IsEnabled=clockOn.IsOn;
                var f=values.GetNamedObject("fps");fpsOn.IsOn=f.GetNamedBoolean("enabled");fpsProvider.SelectedIndex=f.GetNamedString("provider")=="Intel"?0:1;fps.Value=f.GetNamedNumber("value");mode.SelectedIndex=(int)values.GetNamedNumber("powerMode");overlay.SelectedIndex=(int)values.GetNamedNumber("overlay");powerScale.Update(separate.IsOn);primaryTitle.Text=OnexStrings.Format("TDP · PL1 · {0} W",(int)pl1.Value);secondaryTitle.Text=OnexStrings.Format("PL2 · {0} W",(int)pl2.Value);clock.Header=(int)clock.Value+global::XboxGamingBar.OnexStrings.Get(" MHz");fps.Header=(int)fps.Value+global::XboxGamingBar.OnexStrings.Get(" FPS");loading=false;
            }
            JsonObject Collect()=>new JsonObject{{"separatePl2",JsonValue.CreateBooleanValue(separate.IsOn)},{"pl1",JsonValue.CreateNumberValue(pl1.Value)},{"pl2",JsonValue.CreateNumberValue(pl2.Value)},{"boost",JsonValue.CreateNumberValue(boost.SelectedIndex)},{"scheduling",JsonValue.CreateNumberValue(scheduling.SelectedIndex)},{"clockMhz",JsonValue.CreateNumberValue(clockOn.IsOn?clock.Value:0)},{"powerMode",JsonValue.CreateNumberValue(mode.SelectedIndex)},{"overlay",JsonValue.CreateNumberValue(overlay.SelectedIndex)},{"fps",new JsonObject{{"provider",JsonValue.CreateStringValue(fpsProvider.SelectedIndex==0?"Intel":"RTSS")},{"enabled",JsonValue.CreateBooleanValue(fpsOn.IsOn)},{"value",JsonValue.CreateNumberValue(fps.Value)}}}};
            void SaveSide(){var collected=Collect();var original=selectedSide==0?ac:dc;if(original.TryGetValue("display",out var display))collected["display"]=display;
                collected["fan"]=original.GetNamedValue("fan",JsonValue.CreateNumberValue(0));
                if(original.TryGetValue("fanProfile",out var fanProfile))collected["fanProfile"]=fanProfile;if(selectedSide==0)ac=collected;else dc=collected;}
            Load(ac);separate.Toggled+=(s,e)=>{if(loading)return;pl2.Minimum=separate.IsOn?pl1.Value:8;pl2.IsEnabled=separate.IsOn;powerScale.Update(separate.IsOn);};pl1.ValueChanged+=(s,e)=>{primaryTitle.Text=OnexStrings.Format("TDP · PL1 · {0} W",(int)pl1.Value);if(!loading)
                {
                    int sustained=(int)pl1.Value;
                    int adjusted=Pl2ControlPolicy.AdjustManual(sustained,(int)pl2.Value);
                    pl2.Maximum=Pl2ControlPolicy.ManualCeiling(sustained);
                    pl2.Minimum=separate.IsOn?pl1.Value:8;
                    pl2.Value=adjusted;
                    powerScale.Update(separate.IsOn);
                }};pl2.ValueChanged+=(s,e)=>secondaryTitle.Text=OnexStrings.Format("PL2 · {0} W",(int)pl2.Value);clock.ValueChanged+=(s,e)=>clock.Header=(int)clock.Value+global::XboxGamingBar.OnexStrings.Get(" MHz");fps.ValueChanged+=(s,e)=>fps.Header=(int)fps.Value+global::XboxGamingBar.OnexStrings.Get(" FPS");clockOn.Toggled+=(s,e)=>clock.IsEnabled=clockOn.IsOn;split.Toggled+=(s,e)=>{side.IsEnabled=split.IsOn;if(!split.IsOn){SaveSide();ac=JsonObject.Parse((selectedSide==0?ac:dc).Stringify());dc=JsonObject.Parse(ac.Stringify());if(side.SelectedIndex!=0)side.SelectedIndex=0;}};side.SelectionChanged+=(s,e)=>{if(loading)return;SaveSide();selectedSide=side.SelectedIndex;Load(selectedSide==0?ac:dc);};
            var dialog=CreateOnexSettingsDialog(entry.GetNamedString("id")=="global"?global::XboxGamingBar.OnexStrings.Get("Global profile"):entry.GetNamedString("name"),global::XboxGamingBar.OnexStrings.Get("Saved by ONEXTweaks · values are applied when this profile is active"),panel);_onexDetailsDialog=dialog;
            try{if(await dialog.ShowAsync()==ContentDialogResult.Primary){SaveSide();await MutateOnexProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("edit")},{"id",entry["id"]},{"split",JsonValue.CreateBooleanValue(split.IsOn)},{"ac",ac},{"dc",split.IsOn?dc:ac}});}}
            finally{_onexDetailsDialog=null;anchor.Focus(FocusState.Programmatic);}
        }
    }
}
