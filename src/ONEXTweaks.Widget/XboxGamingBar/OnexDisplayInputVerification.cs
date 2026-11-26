// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Input;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexDisplayInputsAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;
            string run=Guid.NewGuid().ToString("N"),owned=null,label="initial";int step=0;Slider active=null;
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            void Progress(){System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-display-input-progress.json"),label+" / "+step);}
            async Task Input(string key,Func<bool> expected)
            {
                string id=run+":"+(++step);Progress();var bounds=new RadioButtonAutomationPeer(DisplayNavItem).GetBoundingRectangle();
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(bounds.X+bounds.Width/2)},{"y",JsonValue.CreateNumberValue(bounds.Y+bounds.Height/2)}}.Stringify());
                for(int n=0;n<70;n++){await Task.Delay(100);try{if(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&expected())return;}catch(System.IO.FileNotFoundException){}}
                throw new InvalidOperationException(label+" failed "+key+"; focus="+(FocusManager.GetFocusedElement() as FrameworkElement)?.Name+"; value="+active?.Value);
            }
            bool Focused()=>ReferenceEquals(FocusManager.GetFocusedElement(),active);
            async Task Focus(Slider slider)
            {active=slider;slider.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);Check(slider.Focus(FocusState.Keyboard),"Cannot focus "+label);}
            async Task NativeSdr(int expected)
            {
                for(int n=0;n<50;n++)
                {
                    await Task.Delay(120);await RefreshOnexDisplayAsync(false);await ReadOnexScreenProfilesAsync();
                    var side=OnexSelectedScreenProfile.GetNamedObject(_onexScreenProfiles.GetNamedBoolean("onAc")?"ac":"dc");
                    if(_onexDisplayState.GetNamedObject("hdr").GetNamedNumber("sdrBrightness")==expected&&side.GetNamedObject("hdr").GetNamedNumber("sdrBrightness")==expected&&_onexDisplayPending.Count==0&&!_onexDisplayBusy)return;
                }
                throw new InvalidOperationException(label+" did not apply/save actual CCD SDR brightness");
            }
            var beforePower=await ReadOnexStateAsync();DisplayNavItem.IsChecked=true;await RefreshOnexDisplayAsync(false);await ReadOnexScreenProfilesAsync();
            string initialId=OnexScreenProfileId,initialEntries=_onexScreenProfiles.GetNamedArray("entries").Stringify();var initialHdr=JsonObject.Parse(_onexDisplayState.GetNamedObject("hdr").Stringify());
            Check(initialId=="global"&&!_onexScreenProfiles.GetNamedBoolean("manualOverride",false),"Display input QA requires unheld Global");
            var checks=new JsonArray();report["controls"]=checks;
            try
            {
                // Do not click UIA coordinates: that can select a different compact
                // Game Bar page when desktop scaling differs. Activate via the URI,
                // warm controller mode with a direction, then focus XAML controls.
                await Input("up",()=>true);report["controllerModeWarmupSteps"]=JsonValue.CreateNumberValue(1);
                Check(widget?.Visible==true,"Our widget is not the visible Game Bar page");
                var capture=SaveOnexImage("onex-screen-profile-picker-177.png",_onexScreenProfilePicker.Root,0);
                bool captured=await Task.WhenAny(capture,Task.Delay(3000))==capture;report["pickerCaptured"]=JsonValue.CreateBooleanValue(captured);if(captured)await capture;
                await MutateOnexScreenProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"name",JsonValue.CreateStringValue("ONEX Display input QA")}});owned=OnexScreenProfileId;
                Check(owned!="global","QA profile was not created");
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-display-input-owned.json"),new JsonObject{{"id",JsonValue.CreateStringValue(owned)},{"run",JsonValue.CreateStringValue(run)}}.Stringify());
                foreach(var pair in _onexDisplaySliders.Concat(new[]{new System.Collections.Generic.KeyValuePair<Slider,string>(_onexSdrBrightness,"sdrBrightness")}))
                {
                    label=pair.Value;var slider=pair.Key;
                    if(!slider.IsEnabled){checks.Add(new JsonObject{{"control",JsonValue.CreateStringValue(label)},{"skippedUnavailable",JsonValue.CreateBooleanValue(true)}});continue;}
                    await Focus(slider);double original=slider.Value,delta=label=="hue"||label=="gamma"?Math.Max(slider.SmallChange,slider.StepFrequency):5;int sign=original+delta<=slider.Maximum?1:-1;
                    await Input("a",()=>Focused()&&OnexInputGuard.IsPreviewing(slider));
                    await Input(sign>0?"right":"left",()=>Focused()&&OnexInputGuard.IsPreviewing(slider)&&Math.Abs(slider.Value-original-sign*delta)<.01&&DisplayNavItem.IsChecked==true);
                    await Input("b",()=>Focused()&&!OnexInputGuard.IsPreviewing(slider)&&Math.Abs(slider.Value-original)<.01);
                    await Input("a",()=>OnexInputGuard.IsPreviewing(slider));await Input("a",()=>Focused()&&!OnexInputGuard.IsPreviewing(slider));
                    checks.Add(new JsonObject{{"control",JsonValue.CreateStringValue(label)},{"previewDelta",JsonValue.CreateNumberValue(delta)},{"bCancelled",JsonValue.CreateBooleanValue(true)},{"aRetainedFocus",JsonValue.CreateBooleanValue(true)}});
                }
                Check(_onexSdrBrightness.IsEnabled,"HDR SDR slider is unavailable for actual apply verification");
                label="Screen profile switch cancels an open preview";
                await Focus(_onexSdrBrightness);
                double originalSdr=active.Value;
                await Input("a",()=>OnexInputGuard.IsPreviewing(active));
                await Input(originalSdr+5<=100?"right":"left",()=>active.Value!=originalSdr&&OnexInputGuard.IsPreviewing(active));
                var switched=JsonObject.Parse(_onexScreenProfiles.Stringify());
                switched["activeId"]=JsonValue.CreateStringValue("global");
                ApplyOnexScreenProfilesState(switched.Stringify());
                Check(!OnexInputGuard.IsPreviewing(active)&&active.Value==originalSdr&&_onexDisplayPending.Count==0&&_onexDisplayProfileTransition,
                    "The previous Screen Color gesture survived the new profile state");
                await ReadOnexScreenProfilesAsync();
                Check(OnexScreenProfileId==owned&&!_onexDisplayProfileTransition,"Paired refresh did not reopen the active profile");
                report["profileSwitchCancelledPreview"]=JsonValue.CreateBooleanValue(true);
                label="SDR actual A commit";await Focus(_onexSdrBrightness);int initial=(int)active.Value,signSdr=initial+5<=100?1:-1,target=initial+5*signSdr;
                await Input("a",()=>OnexInputGuard.IsPreviewing(active));await Input(signSdr>0?"right":"left",()=>active.Value==target&&Focused());
                await RefreshOnexDisplayAsync(false);Check(_onexDisplayState.GetNamedObject("hdr").GetNamedNumber("sdrBrightness")==initial,"Preview wrote native brightness");
                await Input("a",()=>Focused()&&!OnexInputGuard.IsPreviewing(active));await NativeSdr(target);Check(Focused(),"A acknowledgement moved focus");
                label="SDR Down save";await Input("a",()=>OnexInputGuard.IsPreviewing(active));await Input(signSdr>0?"stickleft":"stickright",()=>active.Value==initial&&Focused());
                var downTarget=active.XYFocusDown;await Input("down",()=>!OnexInputGuard.IsPreviewing(active)&&ReferenceEquals(FocusManager.GetFocusedElement(),downTarget));await NativeSdr(initial);Check(ReferenceEquals(FocusManager.GetFocusedElement(),downTarget),"Down acknowledgement moved focus");
                if(FocusManager.GetFocusedElement() is Slider next)Check(!OnexInputGuard.IsPreviewing(next),"Down auto-engaged next slider");
                label="SDR Up save";await Focus(_onexSdrBrightness);await Input("a",()=>OnexInputGuard.IsPreviewing(active));await Input(signSdr>0?"right":"left",()=>active.Value==target&&Focused());
                var upTarget=active.XYFocusUp;await Input("up",()=>!OnexInputGuard.IsPreviewing(active)&&ReferenceEquals(FocusManager.GetFocusedElement(),upTarget));await NativeSdr(target);Check(ReferenceEquals(FocusManager.GetFocusedElement(),upTarget),"Up acknowledgement moved focus");
                report["actualCcdACommit"]=JsonValue.CreateBooleanValue(true);report["actualCcdUpDownSaveAndNavigate"]=JsonValue.CreateBooleanValue(true);report["previewDidNotApply"]=JsonValue.CreateBooleanValue(true);report["pointerStepOne"]=JsonValue.CreateBooleanValue(_onexSdrBrightness.StepFrequency==1);report["virtualControllerSteps"]=JsonValue.CreateNumberValue(step);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                try
                {
                    OnexInputGuard.Cancel(active);
                    if(owned!=null)await MutateOnexScreenProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(owned)}});
                    await MutateOnexScreenProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("select")},{"id",JsonValue.CreateStringValue(initialId)}});await ReadOnexScreenProfilesAsync();await RefreshOnexDisplayAsync(false);
                    Check(_onexScreenProfiles.GetNamedArray("entries").Stringify()==initialEntries,"Original profile entries changed");
                    foreach(string key in new[]{"enabled","autoHdr","currentProfile","sdrBrightness"})Check(_onexDisplayState.GetNamedObject("hdr").GetNamedValue(key).Stringify()==initialHdr.GetNamedValue(key).Stringify(),"Native HDR restoration differs: "+key);
                    Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==beforePower.GetNamedString("rawLimits"),"Display inputs changed TDP");
                    report["originalEntriesAndPowerRestored"]=JsonValue.CreateBooleanValue(true);
                    System.IO.File.Delete(System.IO.Path.Combine(folder.Path,"onex-display-input-owned.json"));
                }
                finally{await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");}
            }
        }
    }
}
