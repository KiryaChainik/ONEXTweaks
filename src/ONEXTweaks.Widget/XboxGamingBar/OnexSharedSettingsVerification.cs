// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexSharedSettingsAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");int step=0,powerWrites=0;
            bool touchedPower=false,touchedCpu=false,touchedMode=false,touchedFps=false,originalCpuExpanded=_cpuCardExpanded;
            async Task<JsonObject> Read(string request,string field){var r=await App.SendMessageAsync(new ValueSet{{request,true}});if(r==null||r.ContainsKey("Error"))throw new InvalidOperationException("Read failed: "+request);return JsonObject.Parse((string)r[field]);}
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            async Task Wait(Func<bool> check,string message){for(int n=0;n<100;n++){if(check())return;await Task.Delay(100);}throw new InvalidOperationException(message);}
            async Task Focus(Control control){control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);Check(control.Focus(FocusState.Keyboard),"Control cannot receive focus: "+control.Name);await Wait(()=>ReferenceEquals(FocusManager.GetFocusedElement(),control),"Focus did not settle on "+control.Name);await Task.Delay(80);}
            async Task Input(string key,Func<bool> expected,Control pointerTarget=null)
            {
                string id=run+":"+(++step);var rect=pointerTarget==null?new RadioButtonAutomationPeer(PerformanceNavItem).GetBoundingRectangle():FrameworkElementAutomationPeer.CreatePeerForElement(pointerTarget).GetBoundingRectangle();
                double x=rect.X+rect.Width/2,y=rect.Y+rect.Height/2;
                if(pointerTarget!=null&&widget!=null)
                {
                    double scale=Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
                    x+=widget.WindowBounds.X*scale;y+=widget.WindowBounds.Y*scale;
                }
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(x)},{"y",JsonValue.CreateNumberValue(y)}}.Stringify());
                for(int n=0;n<65;n++){await Task.Delay(100);try{if(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&expected())return;}catch{}}
                throw new InvalidOperationException("Shared settings controller input failed: "+key+" step "+step);
            }
            void Progress(string phase)=>System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-shared-settings-progress.json"),new JsonObject{{"phase",JsonValue.CreateStringValue(phase)},{"virtualSteps",JsonValue.CreateNumberValue(step)}}.Stringify());
            var originalPower=await ReadOnexStateAsync();var originalWindows=await Read("OnexWindowsPowerGet","OnexWindowsPowerState");var originalFps=await Read("OnexFpsGet","OnexFpsState");
            var profiles=await Read("OnexProfilesGet","OnexProfilesState");Check(profiles.GetNamedString("activeId")=="global","Shared verification requires Global/no active game");
            string baselineFile="onex-shared-settings-baseline-"+run+".json";
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,baselineFile),new JsonObject{{"power",originalPower},{"windows",originalWindows},{"fps",originalFps},{"profiles",profiles}}.Stringify());
            report["privateBaselineFile"]=JsonValue.CreateStringValue(baselineFile);
            var originalGlobal=profiles.GetNamedArray("entries").Select(v=>v.GetObject()).Single(v=>v.GetNamedString("id")=="global");
            Check(!originalGlobal.GetNamedBoolean("split")&&originalGlobal.GetNamedObject("ac").Stringify()==originalGlobal.GetNamedObject("dc").Stringify(),"This hardware QA requires the current unified Global baseline");
            JsonObject Side()=>_onexProfilesState?.GetNamedArray("entries").Select(v=>v.GetObject()).Single(v=>v.GetNamedString("id")=="global").GetNamedObject(originalWindows.GetNamedBoolean("onAc")?"ac":"dc");
            int pl1=(int)originalPower.GetNamedNumber("pl1Watts"),manual=(int)originalPower.GetNamedNumber("rememberedPl2Watts");bool separate=originalPower.GetNamedBoolean("separatePl2");
            var home=(ToggleSwitch)_releaseHome.FindName("LightPl2Toggle");var homePl2=(Slider)_releaseHome.FindName("OnexPl2Slider");
            var keys=new JsonArray();report["keyRoutes"]=keys;
            Windows.UI.Xaml.Input.KeyEventHandler trace=(s,e)=>
            {
                string key=e.Key.ToString();if(keys.Count>=64||!new[]{"Space","Enter","GamepadA","Up","Down","Left","Right"}.Contains(key))return;
                keys.Add(new JsonObject{{"key",JsonValue.CreateStringValue(key)},{"focus",JsonValue.CreateStringValue((FocusManager.GetFocusedElement() as FrameworkElement)?.Name??"none")},{"homePl2",JsonValue.CreateBooleanValue(home.IsOn)},{"performancePl2",JsonValue.CreateBooleanValue(TDPBoostToggle.IsOn)}});
            };
            OnexStartupRoot.PreviewKeyDown+=trace;
            async Task Power(bool on,int expected1,int expected2,int expectedManual)
            {
                await Wait(()=>!_onexPerformancePl2Busy&&home.IsEnabled&&home.IsOn==on&&TDPBoostToggle.IsOn==on&&_releaseHome.SeparatePl2Enabled==on&&_releaseHome.RememberedPl2Watts==expectedManual&&(int)_releaseHome.PowerInput.Value==expected1&&(int)TDPSlider.Value==expected1&&(int)homePl2.Value==expected2&&(int)TDPBoostFPPTSliderCard.Value==expectedManual&&Side()!=null&&OnexProfileSeparatePl2(Side())==on&&Side().GetNamedNumber("pl1")==expected1&&Side().GetNamedNumber("pl2")==expectedManual,"Power controls/profile did not converge");
                var actual=await ReadOnexStateAsync();Check(actual.GetNamedBoolean("separatePl2")==on&&actual.GetNamedNumber("pl1Watts")==expected1&&actual.GetNamedNumber("pl2Watts")==expected2&&actual.GetNamedNumber("rememberedPl2Watts")==expectedManual,"Actual power acknowledgement mismatch");
            }
            async Task Boost(bool enabled)
            {
                await Wait(()=>!_onexBoostBusy&&!_onexPerformanceCpuApplying&&_onexBoostOn==enabled&&CPUBoostToggle.IsOn==enabled&&qsTileMap["CPUBoost"].StateText.Text==(enabled?"On":"Off")&&Side()!=null&&(Side().GetNamedNumber("boost")>0)==enabled,"Boost views/profile did not converge");
                var actual=await Read("OnexBoostGet","OnexBoostState");Check(actual.GetNamedBoolean("enabled")==enabled,"Actual boost acknowledgement mismatch");
                await Wait(()=>_onexProfilesState.GetNamedArray("entries").Select(v=>v.GetObject()).Single(v=>v.GetNamedString("id")=="global").GetNamedObject("ac").GetNamedNumber("boost")==actual.GetNamedNumber("acMode")&&_onexProfilesState.GetNamedArray("entries").Select(v=>v.GetObject()).Single(v=>v.GetNamedString("id")=="global").GetNamedObject("dc").GetNamedNumber("boost")==actual.GetNamedNumber("dcMode"),"Saved AC/DC boost policies do not match actual state");
                if(_onexPerformanceCpuForm!=null)Check(((ComboBox)_onexPerformanceCpuForm.NavigationControls[0]).SelectedIndex==actual.GetNamedNumber("acMode")&&((ComboBox)_onexPerformanceCpuForm.NavigationControls[1]).SelectedIndex==actual.GetNamedNumber("dcMode"),"Expanded CPU editor retained an old policy");
            }
            async Task Mode(int mode)
            {
                await Wait(()=>!_onexPowerModeBusy&&OSPowerModeComboBox.SelectedIndex==mode&&qsTileMap["PowerMode"].StateText.Text==new[]{"Efficiency","Balanced","Performance"}[mode]&&Side()!=null&&Side().GetNamedNumber("powerMode")==mode,"Power mode views/profile did not converge");
                var actual=await Read("OnexWindowsPowerGet","OnexWindowsPowerState");Check(actual.GetNamedNumber(actual.GetNamedBoolean("onAc")?"modeAc":"modeDc")==mode,"Actual Windows power mode mismatch");
            }
            async Task Fps(bool enabled,int value)
            {
                await Wait(()=>!_onexFpsBusy&&_onexFpsTimer?.IsEnabled!=true&&OnexFpsEnabled==enabled&&OnexFpsValue==value&&FPSLimitToggle.IsOn==enabled&&FPSStateCycleButton.IsOn==enabled&&FPSModeToggle.IsOn==(OnexFpsProvider=="Intel")&&qsTileMap["FPSCombined"].StateText.Text==OnexFpsProvider+" · "+(enabled?value.ToString():"Off")&&(!enabled||(int)FPSLimitSlider.Value==value)&&Side()!=null&&Side().GetNamedObject("fps").GetNamedBoolean("enabled")==enabled&&Side().GetNamedObject("fps").GetNamedNumber("value")==value&&Side().GetNamedObject("fps").GetNamedString("provider")==OnexFpsProvider,"FPS views/profile did not converge");
                var actual=await Read("OnexFpsGet","OnexFpsState");Check(actual.GetNamedBoolean("enabled")==enabled&&actual.GetNamedNumber("value")==value&&actual.GetNamedString("provider")==OnexFpsProvider,"Actual FPS acknowledgement mismatch");
            }
            try
            {
                Progress("theme and tab stability");
                for(int n=0;n<12;n++)
                {
                    PerformanceNavItem.IsChecked=true;await Task.Delay(75);
                    _releasePowerNav.IsChecked=true;await Task.Delay(75);
                }
                GC.Collect();await Task.Delay(150);
                report["themeNavigationSwitches"]=JsonValue.CreateNumberValue(24);
                Progress("Performance PL2 mouse and touch");touchedPower=true;
                foreach(string pointer in new[]{"focus","touch"})
                {
                    _releasePowerNav.IsChecked=true;await Focus(TDPBoostToggle);
                    await Input(pointer,()=>TDPBoostToggle.IsOn!=separate,TDPBoostToggle);powerWrites++;
                    int restoredManual=Math.Max(pl1,manual);
                    await Power(!separate,pl1,separate?Math.Min(41,pl1+1):restoredManual,separate?manual:restoredManual);
                    await Wait(()=>!_onexPerformancePl2Busy&&TDPBoostToggle.IsEnabled,"PL2 toggle remained busy after pointer input");
                    await Input(pointer,()=>TDPBoostToggle.IsOn==separate,TDPBoostToggle);powerWrites++;
                    await Power(separate,pl1,separate?restoredManual:Math.Min(41,pl1+1),restoredManual);
                    manual=restoredManual;
                    report[pointer=="touch"?"performancePl2InjectedTouchRoundtrip":"performancePl2NativeMouseRoundtrip"]=JsonValue.CreateBooleanValue(true);
                }
                Progress("PL2 bidirectional");PerformanceNavItem.IsChecked=true;await Task.Delay(200);FocusFirstTabContentElement();await Input("up",()=>true);
                await Power(separate,pl1,(int)originalPower.GetNamedNumber("pl2Watts"),manual);
                await Focus(home);touchedPower=true;await Input("a",()=>home.IsOn!=separate);powerWrites++;
                int onManual=Math.Max(pl1,manual);await Power(!separate,pl1,separate?Math.Min(41,pl1+1):onManual,separate?manual:onManual);
                _releasePowerNav.IsChecked=true;await Task.Delay(150);await Focus(TDPBoostToggle);await Input("a",()=>TDPBoostToggle.IsOn==separate);powerWrites++;
                manual=onManual;await Power(separate,pl1,separate?manual:Math.Min(41,pl1+1),manual);
                if(!separate){await Focus(TDPBoostToggle);await Input("a",()=>TDPBoostToggle.IsOn);powerWrites++;await Power(true,pl1,manual,manual);}
                int target=manual>pl1?manual-1:Math.Min(41,manual+1);Check(target!=manual,"No room for PL2 test");
                await Focus(TDPBoostFPPTSliderCard);await Input("a",()=>OnexInputGuard.IsPreviewing(TDPBoostFPPTSliderCard));await Input(target>manual?"right":"left",()=>TDPBoostFPPTSliderCard.Value==target);
                await Task.Delay(1200);Check(OnexInputGuard.IsPreviewing(TDPBoostFPPTSliderCard)&&TDPBoostFPPTSliderCard.Value==target,"Power snapshot overwrote PL2 preview");
                await Input("a",()=>!OnexInputGuard.IsPreviewing(TDPBoostFPPTSliderCard));powerWrites++;await Power(true,pl1,target,target);report["pl2BidirectionalAndActualGamepadCommit"]=JsonValue.CreateBooleanValue(true);
                PerformanceNavItem.IsChecked=true;await Task.Delay(150);int changed=pl1>8?pl1-1:pl1+1;
                await Focus(_releaseHome.PowerInput);await Input("a",()=>OnexInputGuard.IsPreviewing(_releaseHome.PowerInput));await Input(changed>pl1?"right":"left",()=>_releaseHome.PowerInput.Value==changed);await Input("a",()=>!OnexInputGuard.IsPreviewing(_releaseHome.PowerInput));powerWrites++;
                await Power(true,changed,Math.Max(changed,target),Math.Max(changed,target));report["pl1HomeUpdatesPerformance"]=JsonValue.CreateBooleanValue(true);
                Progress("CPU boost");_releasePowerNav.IsChecked=true;if(!_cpuCardExpanded)await ToggleOnexPerformanceCpuAsync();Check(_onexPerformanceCpuForm!=null,"CPU editor unavailable");touchedCpu=true;
                bool boost=originalWindows.GetNamedObject("cpu").GetNamedNumber(originalWindows.GetNamedBoolean("onAc")?"boostAc":"boostDc")>0;
                PerformanceNavItem.IsChecked=true;QuickSettingsTile_Click(new Button{Tag="CPUBoost"},new RoutedEventArgs());await Boost(!boost);
                _releasePowerNav.IsChecked=true;await Focus(CPUBoostToggle);await Input("a",()=>CPUBoostToggle.IsOn==boost);await Boost(boost);
                var form=_onexPerformanceCpuForm;var ac=(ComboBox)form.NavigationControls[0];var dc=(ComboBox)form.NavigationControls[1];ac.SelectedIndex=1;dc.SelectedIndex=2;
                await Task.Delay(1200);Check(ac.SelectedIndex==1&&dc.SelectedIndex==2,"Incoming CPU state overwrote an unsubmitted draft");
                ((IInvokeProvider)new ButtonAutomationPeer(_onexPerformanceCpuApply).GetPattern(PatternInterface.Invoke)).Invoke();await Boost(true);
                report["boostHomePerformanceAndExpandedEditor"]=JsonValue.CreateBooleanValue(true);report["cpuDraftPreservedUntilApply"]=JsonValue.CreateBooleanValue(true);
                Progress("OS power mode");bool onAc=originalWindows.GetNamedBoolean("onAc");int initialMode=(int)originalWindows.GetNamedNumber(onAc?"modeAc":"modeDc");int next=(initialMode+1)%3;touchedMode=true;
                PerformanceNavItem.IsChecked=true;await CycleOnexPowerModeAsync();await Mode(next);
                _releasePowerNav.IsChecked=true;await Focus(OSPowerModeComboBox);await Input("a",()=>OSPowerModeComboBox.IsDropDownOpen);int second=next==2?1:next+1;await Input(second>next?"down":"up",()=>true);await Input("a",()=>!OSPowerModeComboBox.IsDropDownOpen);await Mode(second);
                report["powerModeBidirectional"]=JsonValue.CreateBooleanValue(true);
                Progress("FPS");bool fps=originalFps.GetNamedBoolean("enabled");int fpsValue=(int)originalFps.GetNamedNumber("value");PerformanceNavItem.IsChecked=true;touchedFps=true;ToggleOnexFps();await Fps(!fps,fpsValue);
                _releasePowerNav.IsChecked=true;await Focus(FPSStateCycleButton);await Input("a",()=>FPSStateCycleButton.IsOn==fps);await Fps(fps,fpsValue);
                if(!fps){ToggleOnexFps();await Fps(true,fpsValue);}
                await Focus(FPSLimitSlider);await Input("a",()=>OnexInputGuard.IsPreviewing(FPSLimitSlider));int fpsTarget=fpsValue<300?fpsValue+1:fpsValue-1;await Input(fpsTarget>fpsValue?"right":"left",()=>FPSLimitSlider.Value==fpsTarget);await Input("a",()=>!OnexInputGuard.IsPreviewing(FPSLimitSlider));await Fps(true,fpsTarget);
                report["fpsBidirectionalAndSliderCommit"]=JsonValue.CreateBooleanValue(true);
                string alternate=originalFps.GetNamedString("provider")=="Intel"?"RTSS":"Intel";
                bool alternateAvailable=alternate=="Intel"?originalFps.GetNamedObject("intel",null)?.GetNamedBoolean("available",false)==true:originalFps.GetNamedBoolean("rtssAvailable",false);
                if(alternateAvailable){await SetOnexFpsAsync(alternate,false,fpsValue);await Fps(false,fpsValue);report["alternateDisabledProviderSelection"]=JsonValue.CreateBooleanValue(true);}
                else report["alternateDisabledProviderSelection"]=JsonValue.CreateStringValue("Unavailable; skipped");
                report["virtualControllerSteps"]=JsonValue.CreateNumberValue(step);report["physicalControllerTested"]=JsonValue.CreateBooleanValue(false);
            }
            catch(Exception primary)
            {
                report["primaryError"]=JsonValue.CreateStringValue(primary.ToString());
                report["failureUi"]=new JsonObject{{"homePl2",JsonValue.CreateBooleanValue(home.IsOn)},{"homeEnabled",JsonValue.CreateBooleanValue(home.IsEnabled)},{"performancePl2",JsonValue.CreateBooleanValue(TDPBoostToggle.IsOn)},{"focus",JsonValue.CreateStringValue((FocusManager.GetFocusedElement() as FrameworkElement)?.Name??"none")}};
                throw;
            }
            finally
            {
                OnexStartupRoot.PreviewKeyDown-=trace;
                Progress("restore original settings");OnexInputGuard.Cancel(_releaseHome.PowerInput);OnexInputGuard.Cancel(TDPBoostFPPTSliderCard);OnexInputGuard.Cancel(FPSLimitSlider);
                try
                {
                    if(touchedFps){_onexFpsTimer?.Stop();await SetOnexFpsAsync(originalFps.GetNamedString("provider"),originalFps.GetNamedBoolean("enabled"),(int)originalFps.GetNamedNumber("value"));}
                    if(touchedCpu||touchedMode)
                    {
                        var fresh=await Read("OnexWindowsPowerGet","OnexWindowsPowerState");Check(fresh.GetNamedBoolean("onAc")==originalWindows.GetNamedBoolean("onAc"),"Power source changed during verification");
                        var cpu=fresh.GetNamedObject("cpu");var original=originalWindows.GetNamedObject("cpu");Check(cpu.GetNamedString("scheme")==original.GetNamedString("scheme"),"Power plan changed during verification");
                        if(touchedCpu){foreach(string key in new[]{"boostAc","boostDc","schedulingAc","schedulingDc","shortSchedulingAc","shortSchedulingDc"})cpu[key]=original[key];var restoredCpu=await SendOnexHomeRequestAsync(new ValueSet{{"OnexCpuSettings",cpu.Stringify()}});ApplyOnexWindowsPowerState((string)restoredCpu["OnexWindowsPowerState"]);}
                        if(touchedMode)await SetOnexPowerModeAsync((int)originalWindows.GetNamedNumber(originalWindows.GetNamedBoolean("onAc")?"modeAc":"modeDc"));
                    }
                }
                finally
                {
                    if(touchedPower){var prefs=ApplicationData.Current.LocalSettings.Values;prefs["OnexSeparatePl2"]=originalPower.GetNamedBoolean("separatePl2");prefs["OnexRememberedPl2Watts"]=(int)originalPower.GetNamedNumber("rememberedPl2Watts");_releaseHome.ReloadPowerPreferences();await SetOnexReleasePowerAsync((int)originalPower.GetNamedNumber("pl1Watts"),(int)originalPower.GetNamedNumber("pl2Watts"));powerWrites++;}
                    var savedGlobal=_onexProfilesState.GetNamedArray("entries").Select(v=>v.GetObject()).Single(v=>v.GetNamedString("id")=="global");
                    if(touchedCpu&&savedGlobal.GetNamedBoolean("split"))await SetOnexProfileSplitAsync(false);
                    var after=await ReadOnexStateAsync();Check(after.GetNamedString("rawLimits")==originalPower.GetNamedString("rawLimits")&&after.GetNamedBoolean("separatePl2")==originalPower.GetNamedBoolean("separatePl2")&&after.GetNamedNumber("rememberedPl2Watts")==originalPower.GetNamedNumber("rememberedPl2Watts"),"Original power settings were not restored");
                    if(_cpuCardExpanded!=originalCpuExpanded)await ToggleOnexPerformanceCpuAsync();
                    report["originalPowerRestored"]=JsonValue.CreateBooleanValue(true);report["expectedPowerWrites"]=JsonValue.CreateNumberValue(powerWrites);
                    await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");
                }
                // OEM automatic clock caps settle after restoring power/plan state.
                // Compare the settled readback; never overwrite them from this QA path.
                await Task.Delay(2500);
                var finalWindows=await Read("OnexWindowsPowerGet","OnexWindowsPowerState");var finalFps=await Read("OnexFpsGet","OnexFpsState");
                var differences=new JsonArray();var finalCpu=finalWindows.GetNamedObject("cpu");
                foreach(var pair in originalWindows.GetNamedObject("cpu"))if(pair.Value.Stringify()!=finalCpu.GetNamedValue(pair.Key).Stringify())
                    differences.Add(pair.Key=="scheme"?(IJsonValue)new JsonObject{{"field",JsonValue.CreateStringValue(pair.Key)}}:new JsonObject{{"field",JsonValue.CreateStringValue(pair.Key)},{"before",pair.Value},{"after",finalCpu[pair.Key]}});
                foreach(string key in new[]{"modeAc","modeDc"})if(finalWindows.GetNamedNumber(key)!=originalWindows.GetNamedNumber(key))differences.Add(new JsonObject{{"field",JsonValue.CreateStringValue(key)},{"before",originalWindows[key]},{"after",finalWindows[key]}});
                report["windowsPolicyDifferences"]=differences;
                Check(differences.Count==0,"Original Windows policies were not restored");
                Check(finalFps.GetNamedString("provider")==originalFps.GetNamedString("provider")&&finalFps.GetNamedBoolean("enabled")==originalFps.GetNamedBoolean("enabled")&&finalFps.GetNamedNumber("value")==originalFps.GetNamedNumber("value"),"Original FPS settings were not restored");
                report["originalCpuModeAndFpsRestored"]=JsonValue.CreateBooleanValue(true);
                report["savedProfileSynchronizationChecked"]=JsonValue.CreateBooleanValue(true);
            }
        }
    }
}
