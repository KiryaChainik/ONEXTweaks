// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexScalingModesAsync(JsonObject report,int phases=3)
        {
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            string folder=Windows.Storage.ApplicationData.Current.LocalFolder.Path;DisplayNavItem.IsChecked=true;var checks=new JsonArray();report["nativeModes"]=checks;
            for(int phase=1;phase<=phases;phase++)
            {
                JsonObject ready=null;
                for(int n=0;n<350;n++)
                {
                    await Task.Delay(100);
                    try{var candidate=JsonObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder,"onex-scaling-native-ready.json")));if(candidate.GetNamedNumber("phase")==phase){ready=candidate;break;}}catch(System.IO.IOException){}catch(System.Runtime.InteropServices.COMException){}
                }
                Check(ready!=null,"Native Scaling Mode phase did not arrive");await RefreshOnexDisplayAsync(false);UpdateLayout();
                int mode=(int)ready.GetNamedNumber("mode"),method=(int)ready.GetNamedNumber("method");
                Check(!_onexDisplayState.GetNamedBoolean("atNativeResolution"),"Lower-resolution scaling was gated as native");
                Check(IntelScalingModeComboBox.IsEnabled&&IntelScalingMethodComboBox.IsEnabled,"Scaling controls are disabled below native resolution");
                Check(SelectedScalingMode()==mode&&IntelScalingMethodComboBox.SelectedIndex==method,"Actual driver state differs from selected native controls");
                Check(IntelScalingMethodComboBox.Items.Count==(mode==1?3:2),"Methods from different scaling groups were mixed");
                IntelScalingCard.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);
                Check(IntelScalingModeComboBox.ActualWidth>100&&IntelScalingMethodComboBox.ActualWidth>100,"Native scaling controls have no usable width");
                var capture=SaveOnexImage("onex-scaling-mode186-"+phase+".png",IntelScalingCard,0);bool captured=await Task.WhenAny(capture,Task.Delay(3000))==capture;if(captured)await capture;
                checks.Add(new JsonObject{{"mode",JsonValue.CreateNumberValue(mode)},{"method",JsonValue.CreateNumberValue(method)},{"nativeDriverStateMatchesUi",JsonValue.CreateBooleanValue(true)},{"methods",JsonValue.CreateNumberValue(IntelScalingMethodComboBox.Items.Count)},{"captureCompleted",JsonValue.CreateBooleanValue(captured)}});
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder,"onex-scaling-native-ack.txt"),phase.ToString());
            }
            report["uiReadOnly"]=JsonValue.CreateBooleanValue(true);report["actualHardwareActor"]=JsonValue.CreateStringValue("Separate native API probe with fresh recovery/watchdog");report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
        }
        private async Task VerifyOnexResolutionPlacementAsync(JsonObject report)
        {
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            var before=await ReadOnexStateAsync();DisplayNavItem.IsChecked=true;await RefreshOnexDisplayAsync(false);UpdateLayout();
            int color=DisplayMainStack.Children.IndexOf(DisplayTabHost),hdr=DisplayMainStack.Children.IndexOf(_onexHdrCard),resolution=DisplayMainStack.Children.IndexOf(DisplayResTabHost),intel=DisplayMainStack.Children.IndexOf(DisplayGpuTabHost);
            Check(hdr==color+1&&resolution==hdr+1&&intel>resolution,"Display color/HDR/resolution/Intel visual order differs");
            var hdrBounds=_onexHdrCard.TransformToVisual(DisplayMainStack).TransformBounds(new Windows.Foundation.Rect(0,0,_onexHdrCard.ActualWidth,_onexHdrCard.ActualHeight));
            var resBounds=DisplayResTabHost.TransformToVisual(DisplayMainStack).TransformBounds(new Windows.Foundation.Rect(0,0,DisplayResTabHost.ActualWidth,DisplayResTabHost.ActualHeight));
            report["actualGapDip"]=JsonValue.CreateNumberValue(resBounds.Top-hdrBounds.Bottom);
            Check(resBounds.Top>=hdrBounds.Bottom&&resBounds.Top-hdrBounds.Bottom<=16,"Resolution card is not immediately below HDR in actual layout");
            Check(ResolutionComboBox.ActualWidth>100&&RefreshRatesComboBox.ActualWidth>100&&ResolutionComboBox.Items.Count>1&&RefreshRatesComboBox.Items.Count>1,"Native mode controls are missing or clipped");
            var current=_onexDisplayState.GetNamedObject("modes").GetNamedObject("current");
            Check((string)ResolutionComboBox.SelectedItem==current.GetNamedNumber("width")+"x"+current.GetNamedNumber("height")&&(string)RefreshRatesComboBox.SelectedItem==current.GetNamedNumber("hz")+" Hz","Selected resolution/rate differ from actual Windows mode");
            DisplayResTabHost.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);
            var capture=SaveOnexImage("onex-display-resolution182.png",DisplayResTabHost,0);bool captured=await Task.WhenAny(capture,Task.Delay(3000))==capture;if(captured)await capture;
            Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==before.GetNamedString("rawLimits"),"Read-only resolution layout changed TDP");
            report["colorHdrResolutionIntelOrder"]=JsonValue.CreateBooleanValue(true);report["actualGapDip"]=JsonValue.CreateNumberValue(resBounds.Top-hdrBounds.Bottom);report["nativeModeMatchesSelectedUi"]=JsonValue.CreateBooleanValue(true);report["captureCompleted"]=JsonValue.CreateBooleanValue(captured);report["readOnly"]=JsonValue.CreateBooleanValue(true);
        }
        private async Task VerifyOnexSharpnessUiAsync(JsonObject report)
        {
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            var before=await ReadOnexStateAsync();DisplayNavItem.IsChecked=true;await RefreshOnexDisplayAsync(false);
            var state=_onexDisplayState.GetNamedObject("settings").GetNamedObject("sharpness");bool hdr=_onexDisplayState.GetNamedObject("hdr").GetNamedBoolean("enabled");
            Check(DisplaySharpnessSlider.Minimum==0&&DisplaySharpnessSlider.Maximum==5&&DisplaySharpnessSlider.StepFrequency==1&&DisplaySharpnessSlider.SmallChange==1,"Sharpness is not0Off/1..5 with one-level input");
            Check(DisplaySharpnessSlider.IsEnabled==state.GetNamedBoolean("available"),"Sharpness availability differs from driver");
            if(hdr)Check(!DisplaySharpnessSlider.IsEnabled&&OnexSharpnessHint.Text.Contains("HDR")&&OnexSharpnessHint.Text.Contains("SDR"),"HDR limitation is not explained");
            DisplaySharpnessRow.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);
            var capture=SaveOnexImage("onex-sharpness-180.png",DisplaySharpnessRow,0);bool captured=await Task.WhenAny(capture,Task.Delay(3000))==capture;if(captured)await capture;
            Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==before.GetNamedString("rawLimits"),"Read-only sharpness UI changed TDP");
            report["readOnly"]=JsonValue.CreateBooleanValue(true);report["hdrEnabled"]=JsonValue.CreateBooleanValue(hdr);report["driverAvailabilityMatchesUi"]=JsonValue.CreateBooleanValue(true);report["actualRangeOneToFivePlusOff"]=JsonValue.CreateBooleanValue(true);report["captureCompleted"]=JsonValue.CreateBooleanValue(captured);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["physicalImageQualityTested"]=JsonValue.CreateBooleanValue(false);
        }
        private async Task VerifyOnexDisplayHdrAsync(JsonObject report)
        {
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            var before=await ReadOnexStateAsync();
            var profiles=await App.SendMessageAsync(new ValueSet{{"OnexProfilesGet",true}});string original=(string)profiles["OnexProfilesState"];
            DisplayNavItem.IsChecked=true;await Task.Delay(200);await RefreshOnexDisplayAsync(false);UpdateLayout();
            Check(OnexHdrReady,"Windows HDR state is unavailable");var hdr=_onexDisplayState.GetNamedObject("hdr");
            Check(_onexHdrEnabled.IsOn==hdr.GetNamedBoolean("enabled"),"HDR toggle differs from Windows readback");
            Check(_onexAutoHdr.IsOn==hdr.GetNamedBoolean("autoHdr"),"Auto HDR toggle differs from Windows preference");
            Check(_onexHdrCalibration.Items.Count==hdr.GetNamedArray("profiles").Count,"Installed calibration profiles missing from selector");
            Check(_onexHdrCalibration.Items.Count>0&&_onexHdrCalibration.SelectedItem is ComboBoxItem selected&&(selected.Tag as string)==hdr.GetNamedString("currentProfile"),"Current calibration is absent or not selected");
            Check((int)_onexSdrBrightness.Value==(int)hdr.GetNamedNumber("sdrBrightness"),"SDR brightness differs from native CCD readback");
            Check(DisplayMainStack.Children.IndexOf(_onexHdrCard)==DisplayMainStack.Children.IndexOf(DisplayTabHost)+1,"HDR card is not immediately below the color sliders");
            Check(_onexHdrCalibrate.Content.ToString()==(hdr.GetNamedBoolean("calibrationAppInstalled")?"Калибровка HDR":"Установить HDR Calibration"),"Missing calibration application is not explained");
            _onexHdrCard.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(150);UpdateLayout();
            await SaveOnexImage("onex-display-hdr-166.png",_onexHdrCard);
            foreach(var control in new Control[]{_onexHdrEnabled,_onexAutoHdr,_onexHdrCalibration,_onexSdrBrightness,_onexHdrCalibrate,_onexHdrReset})
                Check(control.ActualWidth>20&&control.ActualHeight>15,"HDR control has no visible layout: "+control.Name);
            await RefreshOnexDisplayAsync(false);var afterProfiles=await App.SendMessageAsync(new ValueSet{{"OnexProfilesGet",true}});
            Check((string)afterProfiles["OnexProfilesState"]==original,"Passive HDR UI wrote performance profile state");
            Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==before.GetNamedString("rawLimits"),"HDR UI initialization changed TDP");
            report["nativeHdrReadbackMatchesUi"]=JsonValue.CreateBooleanValue(true);report["calibrationProfiles"]=JsonValue.CreateNumberValue(_onexHdrCalibration.Items.Count);
            report["hdrControlsVisible"]=JsonValue.CreateNumberValue(6);report["readOnlyUiInitialization"]=JsonValue.CreateBooleanValue(true);
            report["calibrationAppInstalled"]=hdr["calibrationAppInstalled"];report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
        }
    }
}
