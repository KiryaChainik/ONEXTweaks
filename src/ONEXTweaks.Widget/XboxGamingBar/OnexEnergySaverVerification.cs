// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexBatteryUiAsync(JsonObject report)
        {
            void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
            async Task Wait(Func<bool> expected,string text)
            {for(int i=0;i<120;i++){if(expected())return;await Task.Delay(100);}throw new InvalidOperationException(text);}
            async Task<JsonObject> ReadSaver()
            {var response=await App.SendMessageAsync(new ValueSet{{"OnexEnergySaverGet",true}});return JsonObject.Parse((string)response["OnexEnergySaverState"]);}
            async Task Visuals(bool on)
            {
                await Wait(()=>!_onexEnergySaverBusy&&OnexEnergySaverOn==on,"Saver did not settle");
                await QueryOnexEnergySaverAsync();UpdateQuickSettingsTileStates();UpdateOnexStatsDisplay();
                string battery=(_onexStatsValues["batteryLevel"].Foreground as Windows.UI.Xaml.Media.SolidColorBrush)?.Color.ToString();
                string fill=(_chgBatteryFill.Fill as Windows.UI.Xaml.Media.SolidColorBrush)?.Color.ToString();
                double? soc=currentMetricsData.TryGetValue("batteryLevel",out var level)?level:(double?)null;
                Check(battery==OnexBatteryCapIndicator.ColorFor(soc,App.IsConnected&&_onexChargeKnown,_onexChargeAcknowledgedPercent,on).ToString(),"Top battery color does not match acknowledged cap and actual saver");
                Check(fill==(on?"#FFFFD340":"#E15CC46A"),"Battery tile fill does not match actual saver");
                if(on)Check((_onexChargeBatteryBody.BorderBrush as Windows.UI.Xaml.Media.SolidColorBrush)?.Color.ToString()=="#FFFFD340","Battery outline is not yellow");
                var actual=await ReadSaver();Check(actual.GetNamedBoolean("on")==on,"Actual Windows saver differs from UI");
            }
            var original=await ReadSaver();Check(original.GetNamedBoolean("available"),"Saver unavailable");
            ApplyOnexEnergySaverState(original.Stringify());
            bool originalOn=original.GetNamedBoolean("on");int originalOverride=(int)original.GetNamedNumber("manualOverride");
            Check(originalOverride==1||originalOverride==2,"Original automatic override cannot be exactly restored by this UI QA; no writes performed");
            var media=await ReadOnexMediaAsync();var charge=await App.SendMessageAsync(new ValueSet{{"OnexChargeGet",true}});double cap=JsonObject.Parse((string)charge["OnexChargeState"]).GetNamedNumber("percent");
            var power=await App.SendMessageAsync(new ValueSet{{"OnexWindowsPowerGet",true}});var windows=JsonObject.Parse((string)power["OnexWindowsPowerState"]);
            int columns=qsColumnCount;var folder=Windows.Storage.ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");int step=0;
            async Task TapBattery()
            {
                var nav=new RadioButtonAutomationPeer(PerformanceNavItem).GetBoundingRectangle();
                double scale=Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
                var rect=_onexBatteryStatButton.TransformToVisual(PerformanceNavItem).TransformBounds(new Windows.Foundation.Rect(0,0,_onexBatteryStatButton.ActualWidth,_onexBatteryStatButton.ActualHeight));
                string id=run+":"+(++step);
                await Windows.Storage.FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",Windows.Storage.CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue("focus")},{"x",JsonValue.CreateNumberValue(nav.X+(rect.X+rect.Width/2+widget.WindowBounds.X)*scale)},{"y",JsonValue.CreateNumberValue(nav.Y+(rect.Y+rect.Height/2+widget.WindowBounds.Y)*scale)}}.Stringify());
                for(int i=0;i<120;i++)
                {await Task.Delay(100);try{if(await Windows.Storage.FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&_onexDetailsDialog?.IsLoaded==true)return;}catch{}}
                throw new InvalidOperationException("Actual battery statistic tap did not open confirmation");
            }
            async Task DialogButton(string text)
            {
                var dialog=_onexDetailsDialog;await Wait(()=>dialog?.IsLoaded==true,"Confirmation did not load");
                var button=System.Linq.Enumerable.First(FindOnexSettingsControls<Windows.UI.Xaml.Controls.Button>(dialog),b=>b.Content as string==text);
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                await Wait(()=>_onexDetailsDialog==null&&!_onexEnergySaverBusy,"Confirmation did not close/apply");
            }
            try
            {
                PerformanceNavItem.IsChecked=true;
                if(OnexEnergySaverOn)Check(await SetOnexEnergySaverAsync(false),"Could not prepare Off");await Visuals(false);
                var geometry=new JsonArray();
                foreach(int count in new[]{4,5,6})
                {
                    qsColumnCount=count;RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();await Task.Delay(250);
                    var tile=qsTileMap["ChargeLimiter"];FitOnexChargeTitle(tile);tile.NameText.UpdateLayout();
                    Check(new[]{"Charge Limit","Crg Limit","Limit"}.Contains(tile.NameText.Text),"Unexpected Charge title");
                    Check(!tile.NameText.IsTextTrimmed&&tile.NameText.MaxLines==1&&tile.NameText.ActualHeight<=20.5,"Charge title clips or wraps");
                    if(count==4)Check(tile.NameText.Text=="Charge Limit","Full title did not return in four columns");
                    geometry.Add(new JsonObject{{"columns",JsonValue.CreateNumberValue(count)},{"title",JsonValue.CreateStringValue(tile.NameText.Text)},{"width",JsonValue.CreateNumberValue(tile.NameText.ActualWidth)}});
                    await SaveOnexImage("onex-battery-title-"+count+"-157.png",tile.TileButton);
                }
                report["titlesIn4_5_6Columns"]=geometry;
                qsColumnCount=columns;RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();await Task.Delay(250);
                _onexBatteryStatButton.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(200);
                long requests=_onexEnergySaverCommands;await TapBattery();
                Check(_onexDetailsDialog.PrimaryButtonText=="Да"&&_onexDetailsDialog.CloseButtonText=="Нет","Confirmation does not offer Yes/No");
                await SaveOnexImage("onex-battery-confirm157.png",_onexDetailsDialog);
                await DialogButton("Нет");await Visuals(false);Check(_onexEnergySaverCommands==requests,"No sent an energy-saver write");
                report["actualStatTapAndNoPerformsNoWrite"]=JsonValue.CreateBooleanValue(true);
                await TapBattery();await DialogButton("Да");await Visuals(true);
                Check(_onexEnergySaverCommands==requests+1,"Yes did not send exactly one command");
                await SaveOnexImage("onex-battery-stats-on157.png",QuickMetricsGrid);await SaveOnexImage("onex-charge-tile-on157.png",qsTileMap["ChargeLimiter"].TileButton);
                report["yesEnablesActualWindowsSaverAndYellowBattery"]=JsonValue.CreateBooleanValue(true);
                await TapBattery();await DialogButton("Да");await Visuals(false);
                report["activeSaverCanBeDisabledWithConfirmation"]=JsonValue.CreateBooleanValue(true);
                var opening=ShowOnexChargeDetailsAsync(qsTileMap["ChargeLimiter"].TileButton);
                await Wait(()=>_onexDetailsDialog?.IsLoaded==true&&_onexEnergySaverToggle!=null,"Charge dialog not loaded");
                ((IToggleProvider)new ToggleSwitchAutomationPeer(_onexEnergySaverToggle).GetPattern(PatternInterface.Toggle)).Toggle();await Visuals(true);
                Check(_onexEnergySaverToggle.IsOn,"Popup and battery state disagree");
                CloseOnexMenus();await opening;await Visuals(true);await Task.Delay(300);await Visuals(true);
                report["popupToggleAndTelemetryKeepYellowSynchronized"]=JsonValue.CreateBooleanValue(true);
                Check(!_onexBatteryStatButton.IsTabStop,"Battery action changed arrow focus order");
                report["physicalTouchTested"]=JsonValue.CreateBooleanValue(false);report["injectedPointerSteps"]=JsonValue.CreateNumberValue(step);
            }
            finally
            {
                CloseOnexMenus();if(OnexEnergySaverOn!=originalOn)await SetOnexEnergySaverAsync(originalOn);
                var restored=await ReadSaver();Check(restored.GetNamedBoolean("on")==originalOn&&restored.GetNamedNumber("manualOverride")==originalOverride,"Original saver override not restored");
                qsColumnCount=columns;RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();
                int originalBrightness=(int)media.GetNamedNumber(media.GetNamedBoolean("brightnessDimmed",false)?"brightnessRestore":"brightness");
                var response=await App.SendMessageAsync(new ValueSet{{"SetBrightnessLevel",originalBrightness},{"OnexMediaUserInput",true}});Check(response!=null&&!response.ContainsKey("Error"),"Could not restore original brightness");
                if(media.GetNamedBoolean("brightnessDimmed",false))await ToggleOnexBrightnessDimAsync();
                var afterMedia=await ReadOnexMediaAsync();Check(afterMedia.GetNamedNumber("brightness")==media.GetNamedNumber("brightness")&&afterMedia.GetNamedBoolean("brightnessDimmed")==media.GetNamedBoolean("brightnessDimmed"),"Original brightness/dim not restored");
                charge=await App.SendMessageAsync(new ValueSet{{"OnexChargeGet",true}});Check(JsonObject.Parse((string)charge["OnexChargeState"]).GetNamedNumber("percent")==cap,"Saver QA changed charge limit");
                power=await App.SendMessageAsync(new ValueSet{{"OnexWindowsPowerGet",true}});var afterWindows=JsonObject.Parse((string)power["OnexWindowsPowerState"]);
                foreach(string key in new[]{"modeAc","modeDc"})Check(afterWindows[key].Stringify()==windows[key].Stringify(),"OS power mode changed");
                foreach(string key in new[]{"boostAc","boostDc","schedulingAc","schedulingDc","shortSchedulingAc","shortSchedulingDc"})Check(afterWindows.GetNamedObject("cpu")[key].Stringify()==windows.GetNamedObject("cpu")[key].Stringify(),"CPU policy changed");
                report["saverOverrideChargeCapCpuPolicyAndMediaRestored"]=JsonValue.CreateBooleanValue(true);
                await Windows.Storage.FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",Windows.Storage.CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");
            }
        }
        private async Task VerifyOnexEnergySaverAsync(JsonObject report)
        {
            async Task<JsonObject> Read(){var r=await App.SendMessageAsync(new ValueSet{{"OnexEnergySaverGet",true}});return JsonObject.Parse((string)r["OnexEnergySaverState"]);}
            async Task Wait(bool on)
            {
                for(int i=0;i<120;i++){await QueryOnexEnergySaverAsync();if(!_onexEnergySaverBusy&&OnexEnergySaverOn==on&&_onexEnergySaverToggle?.IsOn==on&&_onexEnergySaverToggle.IsEnabled)return;await Task.Delay(100);}
                throw new InvalidOperationException("Energy saver toggle and actual Windows state disagree");
            }
            void Toggle()=>((IToggleProvider)new ToggleSwitchAutomationPeer(_onexEnergySaverToggle).GetPattern(PatternInterface.Toggle)).Toggle();
            var before=await Read();if(!before.GetNamedBoolean("available"))throw new InvalidOperationException("Energy saver unavailable");
            int originalOverride=(int)before.GetNamedNumber("manualOverride");bool originalOn=before.GetNamedBoolean("on");
            if(originalOverride!=1&&originalOverride!=2)throw new InvalidOperationException("Manual override restoration requires original On/Off for this QA scenario; no writes performed");
            var charge=await App.SendMessageAsync(new ValueSet{{"OnexChargeGet",true}});string originalCharge=JsonObject.Parse((string)charge["OnexChargeState"]).GetNamedNumber("percent").ToString();
            var home=await App.SendMessageAsync(new ValueSet{{"OnexHardwareHomeGet",true}});double clockBefore=JsonObject.Parse((string)home["OnexHardwareHomeState"]).GetNamedNumber("cpuClockMhz");
            var windows=await App.SendMessageAsync(new ValueSet{{"OnexWindowsPowerGet",true}});string originalWindows=(string)windows["OnexWindowsPowerState"];report["windowsPowerBefore"]=JsonObject.Parse(originalWindows);
            foreach(var request in new[]{new ValueSet{{"OnexEnergySaverSet",true}},new ValueSet{{"OnexEnergySaverSet","yes"},{"OnexUserInput",true}}})
            {var rejected=await App.SendMessageAsync(request);if(rejected==null||!rejected.ContainsKey("Error"))throw new InvalidOperationException("Invalid saver write accepted");}
            report["invalidWritesRejected"]=JsonValue.CreateBooleanValue(true);
            var opening=ShowOnexChargeDetailsAsync(qsTileMap["ChargeLimiter"].TileButton);
            try
            {
                for(int i=0;i<80&&(_onexEnergySaverToggle==null||_onexDetailsDialog==null);i++)await Task.Delay(100);
                if(_onexEnergySaverToggle==null||_onexDetailsDialog==null)throw new InvalidOperationException("Charge popup did not expose saver");
                await Wait(originalOn);
                if(!originalOn){Toggle();await Wait(true);}
                report["actualOn"]=await Read();await SaveOnexImage("onex-charge-energy-saver-on.png",_onexDetailsDialog,0);
                Toggle();await Wait(false);report["actualOff"]=await Read();await SaveOnexImage("onex-charge-energy-saver-off.png",_onexDetailsDialog,0);
                if(originalOn){Toggle();await Wait(true);}
                CloseOnexMenus();await opening;
                if(_onexEnergySaverToggle!=null||_onexEnergySaverStatus!=null||_onexDetailsDialog!=null)throw new InvalidOperationException("Stale Charge popup state survived close");
                opening=ShowOnexChargeDetailsAsync(qsTileMap["ChargeLimiter"].TileButton);
                for(int i=0;i<80&&_onexEnergySaverToggle==null;i++)await Task.Delay(100);await Wait(originalOn);
                report["reopenedActualState"]=JsonValue.CreateBooleanValue(true);report["toggleAutomationChangedActualWindowsSaver"]=JsonValue.CreateBooleanValue(true);
            }
            finally
            {
                CloseOnexMenus();await opening;
                if(OnexEnergySaverOn!=originalOn)await SetOnexEnergySaverAsync(originalOn);
                var after=await Read();if(after.GetNamedBoolean("on")!=originalOn||(int)after.GetNamedNumber("manualOverride")!=originalOverride)throw new InvalidOperationException("Original saver state was not restored");
                charge=await App.SendMessageAsync(new ValueSet{{"OnexChargeGet",true}});if(JsonObject.Parse((string)charge["OnexChargeState"]).GetNamedNumber("percent").ToString()!=originalCharge)throw new InvalidOperationException("Charge cap changed during saver QA");
                windows=await App.SendMessageAsync(new ValueSet{{"OnexWindowsPowerGet",true}});report["windowsPowerAfter"]=JsonObject.Parse((string)windows["OnexWindowsPowerState"]);var first=JsonObject.Parse(originalWindows);var last=JsonObject.Parse((string)windows["OnexWindowsPowerState"]);
                foreach(string key in new[]{"modeAc","modeDc"})if(first.GetNamedNumber(key)!=last.GetNamedNumber(key))throw new InvalidOperationException("OS power mode changed");
                var cpuBefore=first.GetNamedObject("cpu");var cpuAfter=last.GetNamedObject("cpu");
                if(cpuBefore.GetNamedString("scheme")!=cpuAfter.GetNamedString("scheme"))throw new InvalidOperationException("Power scheme changed");
                foreach(string key in new[]{"boostAc","boostDc","schedulingAc","schedulingDc","shortSchedulingAc","shortSchedulingDc"})if(cpuBefore.GetNamedNumber(key)!=cpuAfter.GetNamedNumber(key))throw new InvalidOperationException("Saved CPU policy changed: "+key);
                home=await App.SendMessageAsync(new ValueSet{{"OnexHardwareHomeGet",true}});if(JsonObject.Parse((string)home["OnexHardwareHomeState"]).GetNamedNumber("cpuClockMhz")!=clockBefore)throw new InvalidOperationException("Saved CPU clock preference changed");
                report["savedCpuClockPreferencePreserved"]=JsonValue.CreateBooleanValue(true);report["dynamicWindowsOemFrequencyCeilingsComparedAsStaticPreferences"]=JsonValue.CreateBooleanValue(false);
                report["originalSaverRestored"]=JsonValue.CreateBooleanValue(true);report["chargeCapPreserved"]=JsonValue.CreateBooleanValue(true);report["osPowerModeAndCpuSettingsPreserved"]=JsonValue.CreateBooleanValue(true);report["onAc"]=before["onAc"];
            }
        }
    }
}
