// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Automation.Peers;
using Windows.Graphics.Display;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexActualSliderCommitsAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;var before=await ReadOnexStateAsync();
            int original=(int)before.GetNamedNumber("pl1Watts"),originalPl2=(int)before.GetNamedNumber("pl2Watts"),target=original>8?original-1:original+1;
            int lastTarget=original,step=0;string run=Guid.NewGuid().ToString("N"),label="";Slider active=null;
            var results=new JsonArray();report["actualHardwareChecks"]=results;
            async Task Input(string key,Func<bool> expected)
            {
                string id=run+":"+(++step);var rect=new RadioButtonAutomationPeer(PerformanceNavItem).GetBoundingRectangle();
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2)}}.Stringify());
                bool ok=false;for(int n=0;n<60;n++){await Task.Delay(100);try{if(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&expected()){ok=true;break;}}catch{}}
                if(!ok)throw new InvalidOperationException("Actual hardware "+label+" failed "+key+" value="+active?.Value+" preview="+OnexInputGuard.IsPreviewing(active)+" focus="+(FocusManager.GetFocusedElement() as FrameworkElement)?.Name);
            }
            async Task Select(int value)
            {
                for(int n=0;n<45&&Math.Abs(active.Value-value)>.02;n++){double next=active.Value+(active.Value<value?1:-1);await Input(active.Value<value?"right":"left",()=>OnexInputGuard.IsPreviewing(active)&&Math.Abs(active.Value-next)<.02);}
                if(Math.Abs(active.Value-value)>.02)throw new InvalidOperationException("Could not select requested TDP preview");
            }
            async Task WaitPower(int value)
            {
                for(int n=0;n<80;n++){await Task.Delay(100);var actual=await ReadOnexStateAsync();if(actual.GetNamedNumber("pl1Watts")==value){await Task.Delay(200);if((await ReadOnexStateAsync()).GetNamedNumber("pl1Watts")==value)return;}}
                throw new InvalidOperationException("Hardware acknowledgement did not reach "+value+" W");
            }
            try
            {
                if(_onexProfilesState?.GetNamedString("activeId","global")!="global")throw new InvalidOperationException("Actual TDP QA requires Global profile, no game session");
                PerformanceNavItem.IsChecked=true;await Task.Delay(250);FocusFirstTabContentElement();await Input("up",()=>true);
                foreach(bool home in new[]{true,false})
                {
                    label=home?"Home":"Performance";(home?PerformanceNavItem:_releasePowerNav).IsChecked=true;await Task.Delay(250);active=home?_releaseHome.PowerInput:TDPSlider;
                    active.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(80);if(!active.Focus(FocusState.Keyboard))throw new InvalidOperationException("Cannot focus "+label);
                    await Input("a",()=>OnexInputGuard.IsPreviewing(active));await Select(target);
                    if((await ReadOnexStateAsync()).GetNamedNumber("pl1Watts")!=original)throw new InvalidOperationException("Preview applied before confirmation");
                    lastTarget=target;await Input("a",()=>!OnexInputGuard.IsPreviewing(active));await WaitPower(target);
                    if(!ReferenceEquals(FocusManager.GetFocusedElement(),active))throw new InvalidOperationException(label+" A moved focus during actual write");
                    await Input("a",()=>OnexInputGuard.IsPreviewing(active));await Select(original);lastTarget=original;
                    await Input("down",()=>!OnexInputGuard.IsPreviewing(active)&&!ReferenceEquals(FocusManager.GetFocusedElement(),active));await WaitPower(original);
                    if(FocusManager.GetFocusedElement() is Slider next&&OnexInputGuard.IsPreviewing(next))throw new InvalidOperationException("Next slider auto-engaged");
                    var restored=await ReadOnexStateAsync();if(restored.GetNamedNumber("pl2Watts")!=originalPl2)throw new InvalidOperationException("TDP roundtrip did not preserve PL2");
                    results.Add(new JsonObject{{"page",JsonValue.CreateStringValue(label)},{"previewNoHardwareWrite",JsonValue.CreateBooleanValue(true)},{"aAcknowledgedOnHardware",JsonValue.CreateBooleanValue(true)},{"aFocusRetained",JsonValue.CreateBooleanValue(true)},{"downAcknowledgedAndNavigated",JsonValue.CreateBooleanValue(true)},{"originalPowerRestored",JsonValue.CreateBooleanValue(true)}});
                }
                report["actualVirtualControllerSteps"]=JsonValue.CreateNumberValue(step);report["physicalControllerTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                OnexInputGuard.Cancel(active);var current=await ReadOnexStateAsync();
                // Restore only a value this test selected; never overwrite a different
                // fresh user value if the console was used concurrently.
                if(current.GetNamedNumber("pl1Watts")==lastTarget&&current.GetNamedString("rawLimits")!=before.GetNamedString("rawLimits"))await SetOnexReleasePowerAsync(original,originalPl2);
                report["nativePowerRestored"]=JsonValue.CreateBooleanValue((await ReadOnexStateAsync()).GetNamedString("rawLimits")==before.GetNamedString("rawLimits"));
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");
            }
        }
        private async Task VerifyOnexSliderTransactionsAsync(JsonObject report,bool fixtureOnly=false)
        {
            var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N"),label="";int step=0;Slider active=null;
            var checks=new JsonArray();report["controls"]=checks;var media=await ReadOnexMediaAsync();var power=await ReadOnexStateAsync();
            async Task Input(string key,Func<bool> expected,Control pointerTarget=null,double fraction=.5)
            {
                string id=run+":"+(++step);var rect=pointerTarget is Slider targetSlider?new SliderAutomationPeer(targetSlider).GetBoundingRectangle():pointerTarget is ToggleSwitch targetToggle?new ToggleSwitchAutomationPeer(targetToggle).GetBoundingRectangle():pointerTarget is CheckBox targetCheck?new CheckBoxAutomationPeer(targetCheck).GetBoundingRectangle():new RadioButtonAutomationPeer(PerformanceNavItem).GetBoundingRectangle();
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-slider-transaction-progress.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"phase",JsonValue.CreateStringValue(label+" / "+key)},{"step",JsonValue.CreateNumberValue(step)}}.Stringify());
                if(pointerTarget!=null)report["pointerTarget"]=new JsonObject{{"x",JsonValue.CreateNumberValue(rect.X+rect.Width*fraction)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2)},{"width",JsonValue.CreateNumberValue(rect.Width)},{"height",JsonValue.CreateNumberValue(rect.Height)}};
                string frame=new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"kind",JsonValue.CreateStringValue(pointerTarget?.GetType().Name??"")},{"fraction",JsonValue.CreateNumberValue(fraction)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width*fraction)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2)}}.Stringify();
                for(int attempt=0;;attempt++){try{await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),frame);break;}catch(System.IO.IOException)when(attempt<20){await Task.Delay(15);}}
                bool ok=false;for(int n=0;n<60;n++){await Task.Delay(100);try{if(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&expected()){ok=true;break;}}catch{}}
                if(!ok)throw new InvalidOperationException("Transaction "+label+" failed "+key+" step "+step+" value="+active?.Value+" preview="+OnexInputGuard.IsPreviewing(active)+" engaged="+active?.IsFocusEngaged+" focus="+(FocusManager.GetFocusedElement() as FrameworkElement)?.Name);
            }
            bool Focused()=>ReferenceEquals(FocusManager.GetFocusedElement(),active);
            async Task Tab(RadioButton tab)
            {
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-slider-transaction-progress.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"phase",JsonValue.CreateStringValue("switch / "+tab.Tag)},{"step",JsonValue.CreateNumberValue(step)}}.Stringify());
                tab.IsChecked=true;await Task.Delay(250);
            }
            bool Visible(Slider slider)=>slider.Visibility==Visibility.Visible&&slider.ActualWidth>0&&slider.ActualHeight>0;
            async Task CancelAndConfirmNoOp(string name,Slider slider)
            {
                label=name;active=slider;
                if(!slider.IsEnabled||!Visible(slider)){checks.Add(new JsonObject{{"control",JsonValue.CreateStringValue(name)},{"skippedUnavailableOrHidden",JsonValue.CreateBooleanValue(true)}});return;}
                slider.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(70);if(!slider.Focus(FocusState.Keyboard))throw new InvalidOperationException("Cannot focus "+name);
                double initial=slider.Value,delta=Math.Max(slider.SmallChange,slider.StepFrequency);int sign=initial-delta>=slider.Minimum?-1:1;double target=initial+sign*delta;
                await Input("a",()=>Focused()&&OnexInputGuard.IsPreviewing(slider));
                await Input(sign<0?"left":"right",()=>Focused()&&OnexInputGuard.IsPreviewing(slider)&&Math.Abs(slider.Value-target)<.02);
                await Input("b",()=>Focused()&&!OnexInputGuard.IsPreviewing(slider)&&Math.Abs(slider.Value-initial)<.02&&!OnexInputGuard.IsPreviewing(slider));
                await Input("a",()=>Focused()&&OnexInputGuard.IsPreviewing(slider));await Input("a",()=>Focused()&&!OnexInputGuard.IsPreviewing(slider)&&Math.Abs(slider.Value-initial)<.02);
                checks.Add(new JsonObject{{"control",JsonValue.CreateStringValue(name)},{"actualPreviewDelta",JsonValue.CreateNumberValue(delta)},{"bRestoresOriginal",JsonValue.CreateBooleanValue(true)},{"aConfirmationKeepsFocus",JsonValue.CreateBooleanValue(true)}});
            }
            async Task Dialog(Task showing)
            {
                try{for(int n=0;n<60&&_onexDetailsDialog==null;n++)await Task.Delay(100);if(_onexDetailsDialog==null)throw new InvalidOperationException("Dialog did not open");await Task.Delay(120);int count=0;foreach(var slider in FindOnexSettingsControls<Slider>(_onexDetailsDialog).ToArray())await CancelAndConfirmNoOp(_onexDetailsDialog.Title+" slider "+(++count),slider);}
                finally{_onexDetailsDialog?.Hide();await showing;}
            }
            try
            {
                if(!fixtureOnly)
                {
                await Tab(_releasePowerNav);report["coldPerformanceEntry"]=JsonValue.CreateBooleanValue(true);
                await Tab(PerformanceNavItem);FocusFirstTabContentElement();await Input("up",()=>true);
                await CancelAndConfirmNoOp("Home PL1",_releaseHome.PowerInput);
                await CancelAndConfirmNoOp("Home PL2",(Slider)_releaseHome.FindName("OnexPl2Slider"));
                await CancelAndConfirmNoOp("Brightness",RailBrightnessSlider);await CancelAndConfirmNoOp("Volume",RailVolumeSlider);
                await Tab(_releasePowerNav);await CancelAndConfirmNoOp("Performance PL1",TDPSlider);await CancelAndConfirmNoOp("Performance PL2",TDPBoostFPPTSliderCard);
                await Tab(DisplayNavItem);foreach(var pair in _onexDisplaySliders)await CancelAndConfirmNoOp("Display "+pair.Value,pair.Key);
                await Tab(_releaseOsdNav);await CancelAndConfirmNoOp("Overlay opacity",OSDOpacitySlider);await CancelAndConfirmNoOp("Overlay background opacity",OSDBackgroundOpacitySlider);
                await Tab(PerformanceNavItem);await Dialog(ShowOnexChargeDetailsAsync(qsTileMap["ChargeLimiter"].TileButton));await Dialog(ShowOnexFpsSettingsAsync(qsTileMap["FPSCombined"].TileButton));
                var mixer=ShowOnexAudioMixerAsync(RailMixerButton);
                try{for(int n=0;n<60&&(!_onexMixerRows.ContainsKey("master")||!_onexMixerRows["master"].Slider.IsEnabled);n++)await Task.Delay(100);foreach(var row in _onexMixerRows.Values.Take(2).ToArray())await CancelAndConfirmNoOp(row.Token=="master"?"Mixer master":"Mixer app",row.Slider);}
                finally{_onexDetailsDialog?.Hide();await mixer;}
                }
                else{await Tab(PerformanceNavItem);FocusFirstTabContentElement();await Input("up",()=>true);}
                // Native slider fixture exercises actual changed-value commits without
                // writing device settings: its commit sink only counts invocations.
                int commits=0;var fixture=new Slider{Name="OnexCommitFixture",Minimum=0,Maximum=100,Value=43};OnexInputGuard.ConfigurePercentage(fixture);OnexInputGuard.SetCommit(fixture,()=>commits++);
                var next=new Slider{Name="OnexNextFixture",Minimum=0,Maximum=100,Value=67};var panel=new StackPanel();panel.Children.Add(fixture);panel.Children.Add(next);fixture.XYFocusDown=next;next.XYFocusUp=fixture;
                var toggle=new ToggleSwitch{Header=null,OnContent="",OffContent=""};var check=new CheckBox{Content="Mouse acceleration · input preview"};OnexInputGuard.Wire(toggle);panel.Children.Add(toggle);panel.Children.Add(check);
                int togglePointers=0,checkPointers=0;toggle.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((sender,args)=>{togglePointers++;report["togglePointerPresses"]=JsonValue.CreateNumberValue(togglePointers);}),true);check.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((sender,args)=>{checkPointers++;report["checkPointerPresses"]=JsonValue.CreateNumberValue(checkPointers);}),true);
                var dialog=CreateOnexSettingsDialog("Controller transaction verification",null,panel);dialog.PrimaryButtonText="";dialog.DefaultButton=ContentDialogButton.None;_onexDetailsDialog=dialog;
                var opened=new TaskCompletionSource<bool>();dialog.Opened+=(s,e)=>opened.TrySetResult(true);var opening=dialog.ShowAsync();
                try
                {
                    await Task.WhenAny(opened.Task,Task.Delay(5000));if(!opened.Task.IsCompleted)throw new InvalidOperationException("Commit fixture dialog did not open");
                    active=fixture;label="Commit fixture";bool focused=false;
                    for(int n=0;n<30;n++){if(fixture.Focus(FocusState.Keyboard)){await Task.Delay(80);if(Focused()){focused=true;break;}}await Task.Delay(100);}
                    if(!focused)throw new InvalidOperationException("Commit fixture could not acquire native focus");
                    await Input("tap",()=>toggle.IsOn,toggle,.35);await Input("tap",()=>!toggle.IsOn,toggle,.35);
                    await Input("tap",()=>check.IsChecked==true,check,.2);await Input("tap",()=>check.IsChecked==false,check,.2);
                    await Input("tap",()=>fixture.Value>65&&fixture.Value<85,fixture,.75);
                    fixture.Value=43;fixture.Focus(FocusState.Keyboard);
                    await Input("a",()=>OnexInputGuard.IsPreviewing(fixture));await Input("right",()=>fixture.Value==48&&commits==0);
                    await Input("a",()=>Focused()&&!OnexInputGuard.IsPreviewing(fixture)&&fixture.Value==48&&commits==1);
                    await Input("a",()=>OnexInputGuard.IsPreviewing(fixture));await Input("stickleft",()=>fixture.Value==43&&commits==1);
                    await Input("down",()=>ReferenceEquals(FocusManager.GetFocusedElement(),next)&&!OnexInputGuard.IsPreviewing(next)&&fixture.Value==43&&commits==2);
                    await Input("up",()=>Focused()&&fixture.Value==43&&!OnexInputGuard.IsPreviewing(fixture)&&commits==2);
                    await Input("a",()=>OnexInputGuard.IsPreviewing(fixture));await Input("right",()=>fixture.Value==48&&commits==2);await Input("b",()=>fixture.Value==43&&!OnexInputGuard.IsPreviewing(fixture)&&commits==2);
                    report["afterEscapeInputRedirected"]=JsonValue.CreateBooleanValue(Windows.Gaming.UI.GameBar.IsInputRedirected);
                    report["pointerToggleRoundTrip"]=JsonValue.CreateBooleanValue(true);report["pointerCheckRoundTrip"]=JsonValue.CreateBooleanValue(true);report["pointerSliderChangesValue"]=JsonValue.CreateBooleanValue(true);report["bCancelsWithoutCommit"]=JsonValue.CreateBooleanValue(true);
                    report["changedACommitExactlyOnce"]=JsonValue.CreateBooleanValue(true);report["verticalCommitsThenNavigates"]=JsonValue.CreateBooleanValue(true);report["nextSliderNotAutoEngaged"]=JsonValue.CreateBooleanValue(true);report["commitSinkIsTestFixture"]=JsonValue.CreateBooleanValue(true);
                }
                finally{dialog.Hide();await opening;_onexDetailsDialog=null;}
                var after=await ReadOnexMediaAsync();report["nativeMediaUnchanged"]=JsonValue.CreateBooleanValue(after.GetNamedNumber("brightness")==media.GetNamedNumber("brightness")&&after.GetNamedNumber("volume")==media.GetNamedNumber("volume"));
                if(!report.GetNamedBoolean("nativeMediaUnchanged"))throw new InvalidOperationException("Cancelled edits changed media levels");
                report["actualVirtualControllerSteps"]=JsonValue.CreateNumberValue(step);report["touchMediaStepOne"]=JsonValue.CreateBooleanValue(RailBrightnessSlider.StepFrequency==1&&RailVolumeSlider.StepFrequency==1);report["physicalControllerTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                if(active!=null&&OnexInputGuard.IsPreviewing(active))OnexInputGuard.Cancel(active);
                _onexDetailsDialog?.Hide();report["nativePowerUnchanged"]=JsonValue.CreateBooleanValue((await ReadOnexStateAsync()).GetNamedString("rawLimits")==power.GetNamedString("rawLimits"));
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");
            }
        }
        private async Task VerifyOnexAllSliderInputsAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;var checks=new JsonArray();report["controls"]=checks;
            string run=Guid.NewGuid().ToString("N"),name="";int step=0;Slider active=null;
            var media=await ReadOnexMediaAsync();var power=await ReadOnexStateAsync();
            async Task Input(string key,Func<bool> expected)
            {
                string id=run+":"+(++step);var rect=new RadioButtonAutomationPeer(PerformanceNavItem).GetBoundingRectangle();
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2)}}.Stringify());
                bool ok=false;for(int n=0;n<60;n++){await Task.Delay(100);try{if(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&expected()){ok=true;break;}}catch{}}
                if(!ok)throw new InvalidOperationException("Slider "+name+" failed "+key+" step "+step+" value="+active?.Value+" engaged="+active?.IsFocusEngaged+" focus="+(FocusManager.GetFocusedElement() as FrameworkElement)?.Name);
            }
            bool Editing()=>active.IsFocusEngaged&&ReferenceEquals(FocusManager.GetFocusedElement(),active);
            async Task Focus(string label,Slider slider)
            {
                name=label;active=slider;if(!slider.IsEnabled)throw new InvalidOperationException("Slider unavailable: "+label);
                slider.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(80);slider.IsFocusEngaged=false;
                if(!slider.Focus(FocusState.Keyboard))throw new InvalidOperationException("Cannot focus "+label);
                await Input("a",Editing);
            }
            async Task ValueRoundTrip(string label,Slider slider,double delta,Func<double,Task> confirm)
            {
                await Focus(label,slider);double initial=slider.Value;int sign=initial-delta>=slider.Minimum?-1:1;double target=initial+sign*delta;
                try
                {
                    await Input(sign<0?"down":"up",()=>Editing()&&Math.Abs(slider.Value-target)<.02);await confirm(target);
                    if(!Editing())throw new InvalidOperationException(label+" lost focus after backend acknowledgement");
                    await Input(sign<0?"stickright":"stickleft",()=>Editing()&&Math.Abs(slider.Value-initial)<.02);await confirm(initial);
                    if(!Editing())throw new InvalidOperationException(label+" lost focus after restoring the value");
                    await Input("b",()=>!slider.IsFocusEngaged&&ReferenceEquals(FocusManager.GetFocusedElement(),slider));
                    checks.Add(new JsonObject{{"control",JsonValue.CreateStringValue(label)},{"delta",JsonValue.CreateNumberValue(delta)},{"actualValueRoundTrip",JsonValue.CreateBooleanValue(true)},{"focusAfterAcknowledgement",JsonValue.CreateBooleanValue(true)},{"bExitsAdjustment",JsonValue.CreateBooleanValue(true)}});
                }
                finally{slider.IsFocusEngaged=false;}
            }
            async Task Bounds(string label,Slider slider)
            {
                if(!slider.IsEnabled){checks.Add(new JsonObject{{"control",JsonValue.CreateStringValue(label)},{"skippedDisabled",JsonValue.CreateBooleanValue(true)}});return;}
                await Focus(label,slider);double min=slider.Minimum,max=slider.Maximum,initial=slider.Value;
                try
                {
                    slider.Minimum=initial;slider.Maximum=initial;
                    await Input("down",()=>Editing()&&slider.Value==initial);
                    await Input("stickright",()=>Editing()&&slider.Value==initial);
                    await Input("a",()=>!slider.IsFocusEngaged&&ReferenceEquals(FocusManager.GetFocusedElement(),slider));
                    checks.Add(new JsonObject{{"control",JsonValue.CreateStringValue(label)},{"boundedNativeDirections",JsonValue.CreateNumberValue(2)},{"aExitsAdjustment",JsonValue.CreateBooleanValue(true)}});
                }
                finally{slider.Maximum=max;slider.Minimum=min;slider.IsFocusEngaged=false;}
            }
            async Task Dialog(Task showing)
            {
                try
                {
                    for(int n=0;n<60&&_onexDetailsDialog==null;n++)await Task.Delay(100);
                    if(_onexDetailsDialog==null)throw new InvalidOperationException("Slider dialog did not open");
                    await Task.Delay(150);int index=0;
                    foreach(var slider in FindOnexSettingsControls<Slider>(_onexDetailsDialog).ToArray())await Bounds(_onexDetailsDialog.Title+" slider "+(++index),slider);
                }
                finally{_onexDetailsDialog?.Hide();await showing;}
            }
            async Task PowerValue(double value)
            {
                for(int n=0;n<70;n++){await Task.Delay(100);var actual=await ReadOnexStateAsync();if(actual.GetNamedNumber("pl1Watts")==value&&_releaseHome.PowerInput.IsEnabled)return;}
                throw new InvalidOperationException("Native PL1 readback did not reach "+value);
            }
            try
            {
                PerformanceNavItem.IsChecked=true;FocusFirstTabContentElement();await Input("up",()=>true);
                if(_onexProfilesState?.GetNamedString("activeId","global")=="global")await ValueRoundTrip("Home PL1",_releaseHome.PowerInput,1,PowerValue);
                if(_releaseHome.SeparatePl2Enabled)await Bounds("Home PL2",(Slider)_releaseHome.FindName("OnexPl2Slider"));
                if(!media.GetNamedBoolean("brightnessDimmed",false))await ValueRoundTrip("Brightness",RailBrightnessSlider,5,v=>WaitMediaValue("brightness",v));
                await ValueRoundTrip("Volume",RailVolumeSlider,5,v=>WaitMediaValue("volume",v));
                _releasePowerNav.IsChecked=true;await Task.Delay(100);await Bounds("Performance PL1",TDPSlider);
                if(TDPBoostFPPTSliderCard.Visibility==Visibility.Visible&&TDPBoostFPPTSliderCard.IsEnabled)await Bounds("Performance PL2",TDPBoostFPPTSliderCard);
                DisplayNavItem.IsChecked=true;await Task.Delay(100);foreach(var pair in _onexDisplaySliders)await Bounds("Display "+pair.Value,pair.Key);
                _releaseOsdNav.IsChecked=true;await Bounds("Overlay opacity",OSDOpacitySlider);await Bounds("Overlay background opacity",OSDBackgroundOpacitySlider);
                PerformanceNavItem.IsChecked=true;
                await Dialog(ShowOnexChargeDetailsAsync(qsTileMap["ChargeLimiter"].TileButton));
                await Dialog(ShowOnexFpsSettingsAsync(qsTileMap["FPSCombined"].TileButton));
                await Dialog(ShowOnexProfileEditorAsync(OnexProfileByName("global"),qsTileMap["Profile"].TileButton));
                var mixer=ShowOnexAudioMixerAsync(RailMixerButton);
                try
                {
                    for(int n=0;n<60&&(!_onexMixerRows.ContainsKey("master")||!_onexMixerRows["master"].Slider.IsEnabled);n++)await Task.Delay(100);
                    await ValueRoundTrip("Mixer master",_onexMixerRows["master"].Slider,5,async v=>{await WaitMediaValue("volume",v);await RefreshOnexMixerAsync();});
                    foreach(var row in _onexMixerRows.Values.Where(row=>row.Token!="master").Take(1).ToArray())await Bounds("Mixer app",row.Slider);
                }
                finally{_onexDetailsDialog?.Hide();await mixer;}
                report["actualVirtualControllerSteps"]=JsonValue.CreateNumberValue(step);report["physicalTouchTested"]=JsonValue.CreateBooleanValue(false);
                report["touchMediaStepUnchanged"]=JsonValue.CreateBooleanValue(RailBrightnessSlider.StepFrequency==1&&RailVolumeSlider.StepFrequency==1);
            }
            finally
            {
                if(active!=null)active.IsFocusEngaged=false;_onexDetailsDialog?.Hide();
                var current=await ReadOnexStateAsync();if(current.GetNamedString("rawLimits")!=power.GetNamedString("rawLimits"))await SetOnexReleasePowerAsync((int)power.GetNamedNumber("pl1Watts"),(int)power.GetNamedNumber("pl2Watts"));
                if(!media.GetNamedBoolean("brightnessDimmed",false))await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet{{"SetBrightnessLevel",media.GetNamedNumber("brightness")},{"OnexMediaUserInput",true}});
                await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet{{"SetVolumeLevel",media.GetNamedNumber("volume")},{"OnexMediaUserInput",true}});
                report["originalMediaAndPowerRestored"]=JsonValue.CreateBooleanValue((await ReadOnexStateAsync()).GetNamedString("rawLimits")==power.GetNamedString("rawLimits"));
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");
            }
        }
        private async Task VerifyOnexSliderEngagementAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");int step=0;
            var keys=new JsonArray();report["keyTrace"]=keys;
            void Trace(object sender,KeyRoutedEventArgs args)
            {
                var focused=FocusManager.GetFocusedElement() as Control;
                keys.Add(new JsonObject{{"key",JsonValue.CreateStringValue(args.Key.ToString())},{"handled",JsonValue.CreateBooleanValue(args.Handled)},
                    {"focus",JsonValue.CreateStringValue(focused?.Name??"")},{"engaged",JsonValue.CreateBooleanValue(focused?.IsFocusEngaged==true)}});
            }
            var trace=new KeyEventHandler(Trace);OnexStartupRoot.AddHandler(UIElement.KeyDownEvent,trace,true);
            async Task Input(string key)
            {
                string id=run+":"+(++step);var rect=new RadioButtonAutomationPeer(PerformanceNavItem).GetBoundingRectangle();
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2)}}.Stringify());
                bool ack=false;for(int n=0;n<60;n++){await Task.Delay(100);try{if(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id){ack=true;break;}}catch{}}
                if(!ack)throw new InvalidOperationException("Slider test controller acknowledgement missing: "+key);await Task.Delay(150);
            }
            try
            {
                PerformanceNavItem.IsChecked=true;FocusFirstTabContentElement();await Input("up");
                var slider=_releaseHome.PowerInput;slider.IsFocusEngaged=false;slider.Focus(FocusState.Keyboard);await Input("a");
                report["homePl1AEngaged"]=JsonValue.CreateBooleanValue(slider.IsFocusEngaged);
                report["homePl1FocusRetained"]=JsonValue.CreateBooleanValue(ReferenceEquals(FocusManager.GetFocusedElement(),slider));
                report["homePl1EngagementEnabled"]=JsonValue.CreateBooleanValue(slider.IsFocusEngagementEnabled);
                if(!slider.IsFocusEngaged)throw new InvalidOperationException("Actual gamepad A did not engage Home PL1");
                // Move toward the current value without a hardware write: at minimum
                // Down must still be consumed, retain focus and remain engaged.
                double minimum=slider.Minimum;slider.Minimum=slider.Value;
                try
                {
                    await Input("down");
                    report["homePl1DownRetainsFocus"]=JsonValue.CreateBooleanValue(ReferenceEquals(FocusManager.GetFocusedElement(),slider));
                    report["homePl1DownRetainsEngagement"]=JsonValue.CreateBooleanValue(slider.IsFocusEngaged);
                    if(!ReferenceEquals(FocusManager.GetFocusedElement(),slider)||!slider.IsFocusEngaged)throw new InvalidOperationException("Engaged Home PL1 lost focus on D-pad Down at lower bound");
                }
                finally{slider.Minimum=minimum;}
                await Input("a");if(slider.IsFocusEngaged)throw new InvalidOperationException("Second A did not disengage Home PL1");
            }
            finally
            {
                OnexStartupRoot.RemoveHandler(UIElement.KeyDownEvent,trace);
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");
            }
        }
        private async Task VerifyOnexPl2GamepadAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;var prefs=ApplicationData.Current.LocalSettings.Values;
            var toggle=(ToggleSwitch)_releaseHome.FindName("LightPl2Toggle");var pl1=_releaseHome.PowerInput;var pl2=(Slider)_releaseHome.FindName("OnexPl2Slider");
            var originalPower=await ReadOnexStateAsync();bool hadManual=prefs.TryGetValue("OnexRememberedPl2Watts",out var oldManual);
            bool original=toggle.IsOn,had=prefs.TryGetValue("OnexSeparatePl2",out var old);string run=Guid.NewGuid().ToString("N");int step=0;
            bool HasFocus(Control target){var focus=FocusManager.GetFocusedElement() as DependencyObject;while(focus!=null){if(ReferenceEquals(focus,target))return true;focus=VisualTreeHelper.GetParent(focus);}return false;}
            async Task Input(string button,Func<bool> expected)
            {
                string id=run+":"+(++step);var rect=new RadioButtonAutomationPeer(PerformanceNavItem).GetBoundingRectangle();double scale=DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(button)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2+widget.WindowBounds.X*scale)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2+widget.WindowBounds.Y*scale)}}.Stringify());
                bool ok=false;for(int i=0;i<50;i++){await Task.Delay(100);try{if(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&expected()){ok=true;break;}}catch{}}
                if(!ok)throw new InvalidOperationException("Controller input did not reach expected target: "+button+" step "+step+" focus="+(FocusManager.GetFocusedElement() as FrameworkElement)?.Name+" PL1engaged="+pl1.IsFocusEngaged+" PL2engaged="+pl2.IsFocusEngaged+" PL2enabled="+pl2.IsEnabled+" PL2visible="+pl2.Visibility+" PL1down="+(pl1.XYFocusDown as FrameworkElement)?.Name);
                await Task.Delay(250);
            }
            try
            {
                PerformanceNavItem.IsChecked=true;toggle.IsOn=false;await Task.Delay(100);await Input("focus",()=>true);pl1.IsFocusEngaged=false;if(!pl1.Focus(FocusState.Keyboard))throw new InvalidOperationException("PL1 focus failed");
                await Input("up",()=>HasFocus(toggle));
                await Input("a",()=>toggle.IsOn&&pl2.Visibility==Visibility.Visible&&HasFocus(toggle));
                await SaveOnexImage("onex-pl2-controller-focus.png",_releaseHome.PowerSection);
                await Input("down",()=>HasFocus(pl1));await Input("down",()=>HasFocus(pl2));await Input("up",()=>HasFocus(pl1));await Input("up",()=>HasFocus(toggle));await Input("a",()=>!toggle.IsOn&&pl2.Visibility==Visibility.Collapsed&&ReferenceEquals(pl1.XYFocusDown,RailBrightnessSlider));
                await Input("down",()=>HasFocus(pl1));await Input("down",()=>HasFocus(RailBrightnessSlider));await Input("up",()=>HasFocus(pl1));await Input("up",()=>HasFocus(toggle));await Input("a",()=>toggle.IsOn&&pl2.Visibility==Visibility.Visible&&ReferenceEquals(pl1.XYFocusDown,pl2));
                report["actualVirtualControllerSteps"]=JsonValue.CreateNumberValue(step);report["pl2ToggleReachable"]=JsonValue.CreateBooleanValue(true);report["aShowsAndHidesPl2"]=JsonValue.CreateBooleanValue(true);report["bothSliderDirections"]=JsonValue.CreateBooleanValue(true);
            }
            finally
            {
                toggle.IsOn=original;
                if(had)prefs["OnexSeparatePl2"]=old;else prefs.Remove("OnexSeparatePl2");if(hadManual)prefs["OnexRememberedPl2Watts"]=oldManual;else prefs.Remove("OnexRememberedPl2Watts");_releaseHome.ReloadPowerPreferences();
                await SetOnexReleasePowerAsync((int)originalPower.GetNamedNumber("pl1Watts"),(int)originalPower.GetNamedNumber("pl2Watts"));
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");
            }
        }
        private async Task VerifyOnexNavigationFocusAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");int step=0;
            async Task Input(string key,Func<bool> expected,RadioButton tab)
            {
                // AutomationPeer already returns physical screen coordinates. Adding the
                // Game Bar window origin again can click its own Home/Settings tabs.
                string id=run+":"+(++step);var rect=new RadioButtonAutomationPeer(tab).GetBoundingRectangle();
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2)}}.Stringify());
                bool ok=false;for(int n=0;n<50;n++){await Task.Delay(100);try{if(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&expected()){ok=true;break;}}catch{}}
                if(!ok)throw new InvalidOperationException("Navigation input failed: "+key+" step "+step+" focus="+(FocusManager.GetFocusedElement() as FrameworkElement)?.Name);await Task.Delay(250);
            }
            bool NavFocused()=>IsInNavigationArea(FocusManager.GetFocusedElement() as FrameworkElement);
            try
            {
                int count=0;
                foreach(var tab in GetVisibleNavigationItems())
                {
                    if(tab.IsTabStop||tab.Focus(FocusState.Keyboard))throw new InvalidOperationException("Top tab is still keyboard focusable: "+tab.Tag);
                    await Input("focus",()=>tab.IsChecked==true&&!NavFocused(),tab);count++;
                }
                PerformanceNavItem.IsChecked=true;FocusFirstTabContentElement();if(!ReferenceEquals(FocusManager.GetFocusedElement(),_releaseHome.PowerInput))throw new InvalidOperationException("Home does not start at TDP");
                var toggle=(ToggleSwitch)_releaseHome.FindName("LightPl2Toggle");
                await Input("up",()=>ReferenceEquals(FocusManager.GetFocusedElement(),toggle)&&!NavFocused(),PerformanceNavItem);
                await Input("up",()=>ReferenceEquals(FocusManager.GetFocusedElement(),toggle)&&!NavFocused(),PerformanceNavItem);
                await Input("rt",()=>_releasePowerNav.IsChecked==true&&!NavFocused(),_releasePowerNav);
                await Input("lt",()=>PerformanceNavItem.IsChecked==true&&ReferenceEquals(FocusManager.GetFocusedElement(),_releaseHome.PowerInput),PerformanceNavItem);
                report["eightTabsSkipDirectionalFocus"]=JsonValue.CreateNumberValue(count);report["nativePointerSelectionAllTabs"]=JsonValue.CreateBooleanValue(true);report["triggersSwitchAndFocusContent"]=JsonValue.CreateBooleanValue(true);report["homeStartsAtTdp"]=JsonValue.CreateBooleanValue(true);report["upAtBoundaryStaysInContent"]=JsonValue.CreateBooleanValue(true);report["inputCoordinateSpace"]=JsonValue.CreateStringValue("physical screen, AutomationPeer bounds without extra window offset");
                await SaveOnexImage("onex-navigation-content-focus.png",_releaseHome.PowerSection);
            }
            finally{await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");}
        }
        private async Task VerifyOnexTileXAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");int step=0;var opened=new JsonArray();
            string Settings()=>string.Join("|",_onexBoostOn,_onexChargeOn,_onexChargePercent,OnexLightingIsOn,_onexHardwareHome.GetNamedObject("lighting").GetNamedArray("zones").Stringify(),_onexHardwareHome.GetNamedObject("fan").GetNamedNumber("profile",0),_onexWindowsPower.GetNamedNumber("modeAc",-1),_onexWindowsPower.GetNamedNumber("modeDc",-1),OnexFseActive,OnexFpsEnabled,OnexFpsValue,OnexFpsProvider,OnexOverlayLevel);
            async Task Input(string key,Func<bool> expected)
            {
                string id=run+":"+(++step);var rect=new RadioButtonAutomationPeer(PerformanceNavItem).GetBoundingRectangle();double scale=DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2+widget.WindowBounds.X*scale)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2+widget.WindowBounds.Y*scale)}}.Stringify());
                bool ok=false;for(int n=0;n<50;n++){await Task.Delay(100);try{if(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&expected()){ok=true;break;}}catch{}}
                if(!ok)throw new InvalidOperationException("Tile X input failed: "+key+" step "+step);await Task.Delay(200);
            }
            try
            {
                PerformanceNavItem.IsChecked=true;await Input("focus",()=>true);string before=Settings();
                foreach(string id in new[]{"CPUBoost","ChargeLimiter","LedToggle","OnexFanMode","PowerMode","Overlay","FPSCombined","OnexFse"})
                {
                    var tile=qsTileMap[id];if(!tile.IsVisible)throw new InvalidOperationException("QA tile hidden: "+id);
                    tile.TileButton.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(120);if(!tile.TileButton.Focus(FocusState.Keyboard))throw new InvalidOperationException("Tile focus failed: "+id);
                    await Input("x",()=>_onexDetailsDialog!=null||_onexOpenMenus.Any(m=>m.IsOpen));
                    if(Settings()!=before)throw new InvalidOperationException("X executed a primary action: "+id);
                    string title=_onexDetailsDialog?.Title?.ToString()??string.Join("/",_onexOpenMenus.OfType<Windows.UI.Xaml.Controls.MenuFlyout>().SelectMany(m=>m.Items.OfType<Windows.UI.Xaml.Controls.MenuFlyoutItem>()).Select(i=>i.Text));
                    opened.Add(new JsonObject{{"tile",JsonValue.CreateStringValue(id)},{"menu",JsonValue.CreateStringValue(title)}});
                    if(id=="CPUBoost")await SaveOnexImage("onex-controller-x-cpu.png",_onexDetailsDialog);
                    if(id=="OnexFanMode"&&_onexOpenMenus.OfType<Windows.UI.Xaml.Controls.MenuFlyout>().FirstOrDefault() is Windows.UI.Xaml.Controls.MenuFlyout fan&&VisualTreeHelper.GetParent(fan.Items[0]) is UIElement presenter)await SaveOnexImage("onex-controller-x-fan.png",presenter);
                    CloseOnexMenus();for(int n=0;n<25&&(_onexDetailsDialog!=null||_onexOpenMenus.Any(m=>m.IsOpen));n++)await Task.Delay(50);await Task.Delay(200);
                }
                await Task.Delay(550);if(Settings()!=before)throw new InvalidOperationException("Menu opening changed a setting");
                report["menusOpenedByActualX"]=opened;report["primaryActionsNotExecuted"]=JsonValue.CreateBooleanValue(true);report["repeatXAfterDialogWorks"]=JsonValue.CreateBooleanValue(true);
            }
            finally{CloseOnexMenus();_releaseHome.Scroll.ChangeView(null,0,null,true);_releaseHome.PowerInput.Focus(FocusState.Keyboard);await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");}
        }
        private async Task VerifyOnexTriggersAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");int step=0;
            var tabs=GetVisibleNavigationItems();var results=new JsonArray();
            string Active()=>tabs.First(t=>t.IsChecked==true).Tag.ToString();
            async Task Input(string key, string[] expectedKeys=null, int holdMs=120)
            {
                string id=run+":"+(++step);var rect=new RadioButtonAutomationPeer(PerformanceNavItem).GetBoundingRectangle();double scale=DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
                int before=_onexTriggerNavigationCount,traceBefore=_onexTriggerVerificationTabs.Count;
                var packet=new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2+widget.WindowBounds.X*scale)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2+widget.WindowBounds.Y*scale)},{"holdMs",JsonValue.CreateNumberValue(holdMs)}};
                var expected=new System.Collections.Generic.List<string>();int index=tabs.FindIndex(t=>t.IsChecked==true);
                foreach(string pressed in expectedKeys??new string[0]){index=(index+(pressed=="rt"?1:tabs.Count-1))%tabs.Count;expected.Add(tabs[index].Tag.ToString());}
                if(key=="sequence")
                {
                    var sequence=new JsonArray();foreach(string pressed in expectedKeys)sequence.Add(new JsonObject{{"key",JsonValue.CreateStringValue(pressed)},{"holdMs",JsonValue.CreateNumberValue(40)},{"releaseMs",JsonValue.CreateNumberValue(40)}});packet["sequence"]=sequence;
                }
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),packet.Stringify());
                bool ack=false;for(int n=0;n<100;n++){await Task.Delay(100);try{if(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id){ack=true;break;}}catch{}}
                await Task.Delay(100);
                var actual=_onexTriggerVerificationTabs.Skip(traceBefore).ToArray();
                if(!ack||_onexTriggerNavigationCount-before!=expected.Count||!actual.SequenceEqual(expected))throw new InvalidOperationException("LT/RT sequence failed at "+step+" "+key+": expected="+string.Join(",",expected)+" actual="+string.Join(",",actual)+" focus="+(FocusManager.GetFocusedElement() as FrameworkElement)?.Name);
                if(expected.Count>0&&(Active()!=expected.Last()||IsInNavigationArea(FocusManager.GetFocusedElement() as FrameworkElement)))throw new InvalidOperationException("LT/RT final selection or content focus failed");
                results.Add(new JsonObject{{"phase",JsonValue.CreateStringValue(key)},{"freshPresses",JsonValue.CreateNumberValue(expected.Count)},{"selected",JsonValue.CreateStringValue(Active())}});
            }
            try
            {
                PerformanceNavItem.IsChecked=true;await Task.Delay(150);await Input("focus");
                for(int n=0;n<8;n++)await Input("rt",new[]{"rt"});
                for(int n=0;n<8;n++)await Input("lt",new[]{"lt"});
                await Input("sequence",Enumerable.Repeat("rt",16).ToArray());
                await Input("sequence",Enumerable.Range(0,24).Select(n=>n%2==0?"lt":"rt").ToArray());
                await Input("rt",new[]{"rt"},1600);
                // Simulate stale page state after a release was taken by another focus route.
                ltTriggerHeld=true;rtTriggerHeld=true;lastTriggerNavigateUtc=DateTime.UtcNow;
                await Input("lt",new[]{"lt"});
                PerformanceNavItem.IsChecked=true;await ShowOnexFanTileMenuAsync(qsTileMap["OnexFanMode"].TileButton,temporary:true);await Task.Delay(180);
                await Input("rt");CloseOnexMenus();await Task.Delay(200);await Input("rt",new[]{"rt"});
                PerformanceNavItem.IsChecked=true;await Input("reopen");await Input("rt",new[]{"rt"});await Input("lt",new[]{"lt"});
                report["phases"]=results;report["allEightTabsBothDirections"]=JsonValue.CreateBooleanValue(true);report["rapid80msPresses"]=JsonValue.CreateNumberValue(40);report["held1600msSingleStep"]=JsonValue.CreateBooleanValue(true);report["stalePageHeldStateIgnored"]=JsonValue.CreateBooleanValue(true);report["popupAndOverlayReopen"]=JsonValue.CreateBooleanValue(true);report["topTabsNotFocused"]=JsonValue.CreateBooleanValue(true);
            }
            finally{CloseOnexMenus();PerformanceNavItem.IsChecked=true;FocusFirstTabContentElement();await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");}
        }
    }
}
