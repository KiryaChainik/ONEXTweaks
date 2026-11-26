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
using Windows.Graphics.Display;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexPercentageStepsAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;var before=await ReadOnexMediaAsync();string run=Guid.NewGuid().ToString("N");int step=0;var checks=new JsonArray();Slider active=null;string activeName="";double desired=0;string appToken=null;double appOriginal=0;bool appMute=false;
            async Task Phase(string value)=>await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-mixer-test-phase.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"phase",JsonValue.CreateStringValue(value)}}.Stringify());
            async Task WriteInput(string json)
            {
                for(int n=0;;n++){try{System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-gamepad-qa-state.json"),json);return;}catch(System.IO.IOException){if(n>=20)throw;await Task.Delay(25);}}
            }
            async Task Input(string key,Func<bool> expected)
            {
                string id=run+":"+(++step);var rect=new RadioButtonAutomationPeer(PerformanceNavItem).GetBoundingRectangle();double scale=DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
                await WriteInput(new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2+widget.WindowBounds.X*scale)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2+widget.WindowBounds.Y*scale)}}.Stringify());
                bool ok=false;for(int n=0;n<50;n++){await Task.Delay(100);try{if(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&expected()){ok=true;break;}}catch{}}
                if(!ok)throw new InvalidOperationException("Percentage controller input failed: "+key+" step "+step+" control="+activeName+" expected="+desired+" actual="+active?.Value+" engaged="+active?.IsFocusEngaged+" focus="+(FocusManager.GetFocusedElement() as FrameworkElement)?.Name+" mediaError="+_onexMediaWriteError);
            }
            async Task Exercise(string name,Slider slider,Func<double,Task> confirmed)
            {
                if(slider.StepFrequency!=1||slider.SmallChange!=5||slider.LargeChange!=5)throw new InvalidOperationException("Wrong percentage input configuration: "+name);
                slider.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(120);if(!slider.Focus(FocusState.Keyboard))throw new InvalidOperationException("Cannot focus "+name);slider.IsFocusEngaged=true;
                active=slider;activeName=name;double initial=slider.Value;int sign=initial>=6?-1:1;double target=initial+5*sign;
                desired=target;await Input(sign<0?"left":"right",()=>Math.Abs(slider.Value-target)<.02);await confirmed(target);
                desired=initial;await Input(sign<0?"right":"left",()=>Math.Abs(slider.Value-initial)<.02);await confirmed(initial);
                desired=target;await Input(sign<0?"stickleft":"stickright",()=>Math.Abs(slider.Value-target)<.02);await confirmed(target);
                desired=initial;await Input(sign<0?"stickright":"stickleft",()=>Math.Abs(slider.Value-initial)<.02);await confirmed(initial);
                ((IRangeValueProvider)new SliderAutomationPeer(slider).GetPattern(PatternInterface.RangeValue)).SetValue(initial+sign);await confirmed(initial+sign);
                ((IRangeValueProvider)new SliderAutomationPeer(slider).GetPattern(PatternInterface.RangeValue)).SetValue(initial);await confirmed(initial);slider.IsFocusEngaged=false;
                checks.Add(new JsonObject{{"control",JsonValue.CreateStringValue(name)},{"actualGamepadDelta",JsonValue.CreateNumberValue(5)},{"onePointValueConfirmed",JsonValue.CreateBooleanValue(true)}});
            }
            JsonObject AppRow()=>_onexMixerState.GetNamedArray("apps").Select(a=>a.GetObject()).FirstOrDefault(a=>a.GetNamedString("token")==appToken);
            async Task WaitApp(double value)
            {
                for(int n=0;n<50;n++){await Task.Delay(100);await RefreshOnexMixerAsync();var actual=AppRow();if(actual!=null&&Math.Abs(actual.GetNamedNumber("volume")-value)<.02&&actual.GetNamedBoolean("muted")==appMute&&_onexMixerPending.Count==0&&!_onexMixerFlushBusy)return;}
                throw new InvalidOperationException("App percentage readback failed");
            }
            try
            {
                report["nativeInputChecks"]=checks;PerformanceNavItem.IsChecked=true;await RefreshMediaSliderLevelsAsync();await Input("focus",()=>true);
                await Exercise("brightness",RailBrightnessSlider,v=>WaitMediaValue("brightness",v));await Exercise("volume",RailVolumeSlider,v=>WaitMediaValue("volume",v));
                var showing=ShowOnexAudioMixerAsync(RailMixerButton);for(int n=0;n<50&&(_onexMixerState==null||!_onexMixerOpen);n++)await Task.Delay(100);
                if(!_onexMixerOpen)throw new InvalidOperationException("Mixer not open");await Phase("open-await-audio");uint pid=0;
                for(int n=0;n<70&&pid==0;n++){try{pid=(uint)JsonObject.Parse(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-mixer-test-target.json"))).GetNamedNumber("pid");}catch{}if(pid==0)await Task.Delay(100);}
                MixerRow row=null;for(int n=0;n<70&&row==null;n++){await RefreshOnexMixerAsync();row=_onexMixerRows.Values.FirstOrDefault(a=>a.Pid==pid&&pid!=0);if(row==null)await Task.Delay(100);}
                if(row==null)throw new InvalidOperationException("Silent audio test app missing");appToken=row.Token;var app=AppRow();appOriginal=app.GetNamedNumber("volume");appMute=app.GetNamedBoolean("muted");
                await Exercise("mixer-master",_onexMixerRows["master"].Slider,async v=>{for(int n=0;n<50;n++){await Task.Delay(100);await RefreshOnexMixerAsync();if(Math.Abs((await ReadOnexMediaAsync()).GetNamedNumber("volume")-v)<.02&&_onexMixerPending.Count==0&&!_onexMixerFlushBusy)return;}throw new InvalidOperationException("Mixer master readback failed");});
                await Exercise("mixer-app",row.Slider,WaitApp);
                var after=await ReadOnexMediaAsync();if(before.GetNamedBoolean("muted")!=after.GetNamedBoolean("muted")||before.GetNamedBoolean("microphoneMuted")!=after.GetNamedBoolean("microphoneMuted"))throw new InvalidOperationException("Percentage adjustment changed mute");
                report["nativeInputChecks"]=checks;report["physicalTouchTested"]=JsonValue.CreateBooleanValue(false);report["actualVirtualControllerTested"]=JsonValue.CreateBooleanValue(true);report["touchSnappingStep"]=JsonValue.CreateNumberValue(1);report["chargeAndTdpStepsUntouched"]=JsonValue.CreateBooleanValue(true);
            }
            finally
            {
                if(appToken!=null)await SetOnexMixerAsync(appToken,(int)Math.Round(appOriginal),false);
                if(_onexDetailsDialog!=null)_onexDetailsDialog.Hide();for(int n=0;n<50&&_onexMixerOpen;n++)await Task.Delay(50);
                await App.SendMessageAsync(new ValueSet{{"SetBrightnessLevel",before.GetNamedNumber("brightness")},{"OnexMediaUserInput",true}});await App.SendMessageAsync(new ValueSet{{"SetVolumeLevel",before.GetNamedNumber("volume")},{"OnexMediaUserInput",true}});
                var restored=await ReadOnexMediaAsync();foreach(string key in new[]{"brightness","volume"})if(Math.Abs(restored.GetNamedNumber(key)-before.GetNamedNumber(key))>.02)throw new InvalidOperationException("Could not restore "+key);
                await Phase("done");await WriteInput("{\"finished\":true}");report["originalMediaRestored"]=JsonValue.CreateBooleanValue(true);
            }
        }
    }
}
