// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Input;
using Windows.Storage;
using Windows.Graphics.Display;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexDisplayAsync(JsonObject report)
        {
            void Check(bool ok,string error){if(!ok)throw new InvalidOperationException(error);}
            var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");int step=0;
            async Task Input(string key,Func<bool> expected)
            {
                string id=run+":"+(++step);var rect=new RadioButtonAutomationPeer(DisplayNavItem).GetBoundingRectangle();double scale=DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
                var packet=new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2+widget.WindowBounds.X*scale)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2+widget.WindowBounds.Y*scale)}};
                for(int n=0;;n++){try{System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-gamepad-qa-state.json"),packet.Stringify());break;}catch(System.IO.IOException){if(n>=20)throw;await Task.Delay(25);}}
                bool ok=false;for(int n=0;n<60;n++){await Task.Delay(100);string ack=null;try{ack=await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"));}catch{}if(ack==id&&expected()){ok=true;break;}}Check(ok,"Display controller navigation failed at "+key+" step "+step);
            }
            var powerBefore=await ReadOnexStateAsync();
            var profileResponse=await App.SendMessageAsync(new ValueSet{{"OnexProfilesGet",true}});var original=JsonObject.Parse((string)profileResponse["OnexProfilesState"]);ApplyOnexProfilesState(original.Stringify());
            Check(original.GetNamedString("activeId")=="global","Display QA needs Global active");var global=original.GetNamedArray("entries").Select(value=>value.GetObject()).Single(value=>value.GetNamedString("id")=="global");
            try
            {
                DisplayNavItem.IsChecked=true;await Task.Delay(300);await RefreshOnexDisplayAsync(false);UpdateLayout();
                Check(OnexDisplayReady,"Display backend unavailable: "+OnexDisplayText(_onexDisplayState,"error"));
                Check(DisplayScrollViewer.Visibility==Visibility.Visible&&DisplayTabHost.Child==PerfColorDetail&&DisplayGpuTabHost.Child==PerfGpuDetail&&DisplayResTabHost.Child==PerfResDetail,"Release display sections missing");
                Check(_onexDisplaySliders.Count==6&&_onexDisplayChoices.Count==7,"Release control inventory differs");
                var before=JsonObject.Parse(_onexDisplayState.Stringify());var settings=before.GetNamedObject("settings");
                foreach(var pair in _onexDisplaySliders){var field=settings.GetNamedObject(pair.Value);Check(pair.Key.IsEnabled==field.GetNamedBoolean("available"),pair.Value+" availability mismatch");if(field.GetNamedValue("value").ValueType==JsonValueType.Number)Check((int)pair.Key.Value==(int)field.GetNamedNumber("value"),pair.Value+" actual value mismatch");}
                foreach(var pair in _onexDisplayChoices)Check(pair.Key.IsEnabled==settings.GetNamedObject(pair.Value).GetNamedBoolean("available"),pair.Value+" availability mismatch");
                Check(ResolutionComboBox.Items.Count>1&&RefreshRatesComboBox.Items.Count>1,"Supported mode lists absent");
                int image=_displayRefIndex;for(int n=0;n<3;n++){((IInvokeProvider)new ButtonAutomationPeer(DisplayRefNextButton).GetPattern(PatternInterface.Invoke)).Invoke();await Task.Delay(70);}Check(_displayRefIndex==image,"Three-image carousel did not wrap");
                await Input("focus",()=>true);DisplaySaturationSlider.IsFocusEngaged=false;Check(DisplaySaturationSlider.Focus(FocusState.Keyboard),"Saturation not focusable");
                await Input("down",()=>FocusManager.GetFocusedElement()==DisplayHueSlider);await Input("down",()=>FocusManager.GetFocusedElement()==DisplayContrastSlider);await Input("down",()=>FocusManager.GetFocusedElement()==DisplayResetButton);await Input("down",()=>FocusManager.GetFocusedElement()==IntelFrameGenerationComboBox);
                await Input("a",()=>IntelFrameGenerationComboBox.IsDropDownOpen);await Task.Delay(5600);Check(IntelFrameGenerationComboBox.IsDropDownOpen,"Periodic driver sync closed the dropdown");IntelFrameGenerationComboBox.IsDropDownOpen=false;
                report["openDropdownSurvivesPeriodicSync"]=JsonValue.CreateBooleanValue(true);
                DisplayScrollViewer.ChangeView(null,0,null,true);await Task.Delay(100);
                await SaveOnexImage("onex-display-colors-private.png",DisplayScrollViewer,0);
                IntelFetchStateButton.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false,VerticalAlignmentRatio=.5});await Task.Delay(120);((IInvokeProvider)new ButtonAutomationPeer(IntelFetchStateButton).GetPattern(PatternInterface.Invoke)).Invoke();for(int n=0;n<60&&string.IsNullOrEmpty(IntelDriverStateText.Text);n++)await Task.Delay(100);
                Check(IntelDriverStateText.Visibility==Visibility.Visible&&IntelDriverStateText.Text.Contains("Display:"),"Fetch driver state missing actual resolution");
                await SaveOnexImage("onex-display-gpu-private.png",DisplayScrollViewer,0);ResolutionComboBox.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false,VerticalAlignmentRatio=1});await Task.Delay(120);await SaveOnexImage("onex-display-modes-private.png",DisplayScrollViewer,0);
                // Passive state sync and carousel/fetch must not write any profile setting.
                var passive=await App.SendMessageAsync(new ValueSet{{"OnexProfilesGet",true}});var passiveGlobal=JsonObject.Parse((string)passive["OnexProfilesState"]).GetNamedArray("entries").Select(value=>value.GetObject()).Single(value=>value.GetNamedString("id")=="global");
                Check(passiveGlobal.Stringify()==global.Stringify(),"Read-only display initialization wrote the global profile");
                int hz=(int)before.GetNamedObject("modes").GetNamedObject("current").GetNamedNumber("hz");await SetOnexDisplayAsync("refreshRate",JsonValue.CreateNumberValue(hz));Check(_onexDisplayStatus.Visibility==Visibility.Collapsed,"Same-value display IPC failed");
                Check((int)_onexDisplayState.GetNamedObject("modes").GetNamedObject("current").GetNamedNumber("hz")==hz,"Actual refresh rate changed unexpectedly");
                var committed=await App.SendMessageAsync(new ValueSet{{"OnexProfilesGet",true}});var saved=JsonObject.Parse((string)committed["OnexProfilesState"]).GetNamedArray("entries").Select(value=>value.GetObject()).Single(value=>value.GetNamedString("id")=="global");Check(saved.GetNamedObject("ac").GetNamedObject("display").GetNamedObject("mode").GetNamedNumber("hz")==hz,"Display change not saved in profile");
                Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==powerBefore.GetNamedString("rawLimits"),"Display setting changed TDP");
                report["actualVirtualXboxActions"]=JsonValue.CreateNumberValue(step);report["releaseSections"]=JsonValue.CreateNumberValue(3);report["colorSliders"]=JsonValue.CreateNumberValue(6);report["gpuSelectors"]=JsonValue.CreateNumberValue(7);report["referenceImages"]=JsonValue.CreateNumberValue(3);report["resolutionChoices"]=JsonValue.CreateNumberValue(ResolutionComboBox.Items.Count);report["refreshRateChoices"]=JsonValue.CreateNumberValue(RefreshRatesComboBox.Items.Count);report["passiveSyncDoesNotWrite"]=JsonValue.CreateBooleanValue(true);report["actualSameRefreshIpcAndProfileSave"]=JsonValue.CreateBooleanValue(true);report["sharpnessAvailable"]=settings.GetNamedObject("sharpness")["available"];report["nativeColorReadbackAvailable"]=before["colorReadback"];report["physicalColorEffectTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                await MutateOnexProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("edit")},{"id",global["id"]},{"split",global["split"]},{"ac",global["ac"]},{"dc",global["dc"]}});
                Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==powerBefore.GetNamedString("rawLimits"),"Original TDP not preserved");report["originalProfileAndPowerRestored"]=JsonValue.CreateBooleanValue(true);System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-gamepad-qa-state.json"),"{\"finished\":true}");
            }
        }
    }
}
