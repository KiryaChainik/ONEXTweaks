// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async System.Threading.Tasks.Task VerifyOnexMediaIconsAsync(Windows.Data.Json.JsonObject report)
        {
            bool original=_onexMediaState?.GetNamedBoolean("muted",false)??false;
            string Color(Windows.UI.Xaml.Media.Brush brush)=>(brush as Windows.UI.Xaml.Media.SolidColorBrush)?.Color.ToString();
            var states=new Windows.Data.Json.JsonArray();report["states"]=states;
            try
            {
                foreach(bool muted in new[]{false,true,false})
                {
                    ApplyMuteVisual(muted);UpdateLayout();
                    string color=Color(RailMuteIcon.Foreground),brightness=Color(RailBrightnessIcon.Foreground);
                    if(RailMuteIcon.FontFamily.Source!=RailBrightnessIcon.FontFamily.Source||RailMuteIcon.FontSize!=RailBrightnessIcon.FontSize)throw new System.InvalidOperationException("Media icon font styles differ");
                    if(!muted&&(color!=brightness||RailMuteIcon.Glyph!="\uE767"||RailVolumeSlider.Opacity!=1))throw new System.InvalidOperationException("Unmuted speaker does not match brightness");
                    if(muted&&(color==brightness||RailMuteIcon.Glyph!="\uE74F"||System.Math.Abs(RailVolumeSlider.Opacity-.45)>.001))throw new System.InvalidOperationException("Mute state is not distinct: "+color+"/"+brightness+" glyph="+(int)RailMuteIcon.Glyph[0]+" opacity="+RailVolumeSlider.Opacity);
                    states.Add(new Windows.Data.Json.JsonObject{{"muted",Windows.Data.Json.JsonValue.CreateBooleanValue(muted)},{"speakerColor",Windows.Data.Json.JsonValue.CreateStringValue(color)},{"brightnessColor",Windows.Data.Json.JsonValue.CreateStringValue(brightness)},{"fontFamily",Windows.Data.Json.JsonValue.CreateStringValue(RailMuteIcon.FontFamily.Source)},{"fontSize",Windows.Data.Json.JsonValue.CreateNumberValue(RailMuteIcon.FontSize)}});
                    if(!muted)await SaveOnexImage("onex-media-icons-normal.png",_releaseHome.MediaSection);
                }
            }
            finally{ApplyMuteVisual(original);}
            report["states"]=states;report["normalIconsMatch"]=Windows.Data.Json.JsonValue.CreateBooleanValue(true);report["actualAudioChanged"]=Windows.Data.Json.JsonValue.CreateBooleanValue(false);
        }

        private async Task VerifyOnexMediaActionsAsync(JsonObject report)
        {
            var before=await ReadOnexMediaAsync();ApplyOnexMediaLevels(before.Stringify());
            if(!before.GetNamedBoolean("microphoneAvailable",false))throw new InvalidOperationException("Default microphone unavailable: "+before.GetNamedString("microphoneError","No capture endpoint"));
            bool original=before.GetNamedBoolean("microphoneMuted");
            async Task Tap()
            {
                double writes=_onexMediaState.GetNamedNumber("microphoneMuteWrites",0);
                var peer=new Windows.UI.Xaml.Automation.Peers.ButtonAutomationPeer(RailMicButton);
                ((Windows.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                bool done=false;for(int n=0;n<100;n++){await Task.Delay(50);if(!_onexMicrophoneBusy&&_onexMediaState.GetNamedNumber("microphoneMuteWrites",0)>writes){done=true;break;}}
                if(!done)throw new InvalidOperationException("Microphone toggle timed out");
            }
            try
            {
                if(!RailMixerButton.IsEnabled||!RailMicButton.IsEnabled)throw new InvalidOperationException("New media buttons disabled");
                RailMixerButton.Visibility=Visibility.Collapsed;RailMicButton.Visibility=Visibility.Collapsed;UpdateLayout();double oldBrightness=RailBrightnessSlider.ActualWidth,oldVolume=RailVolumeSlider.ActualWidth;
                RailMixerButton.Visibility=Visibility.Visible;RailMicButton.Visibility=Visibility.Visible;UpdateLayout();double brightness=RailBrightnessSlider.ActualWidth,volume=RailVolumeSlider.ActualWidth;
                if(Math.Abs(brightness-volume)>.5||Math.Abs((oldBrightness-brightness)-(oldVolume-volume))>.5||oldBrightness-brightness<20)throw new InvalidOperationException("Media tracks did not shorten equally");
                var widths=new JsonArray();string brightnessText=RailBrightnessValue.Text,volumeText=RailVolumeValue.Text;
                try{foreach(string label in new[]{"0%","9%","50%","100%"}){RailBrightnessValue.Text=label;RailVolumeValue.Text=label;UpdateLayout();if(Math.Abs(RailBrightnessSlider.ActualWidth-brightness)>.1||Math.Abs(RailVolumeSlider.ActualWidth-volume)>.1)throw new InvalidOperationException("Media track width changes with percent labels");widths.Add(JsonValue.CreateStringValue(label));}}
                finally{RailBrightnessValue.Text=brightnessText;RailVolumeValue.Text=volumeText;}
                var slider=RailVolumeSlider.TransformToVisual(_releaseHome).TransformBounds(new Windows.Foundation.Rect(0,0,RailVolumeSlider.ActualWidth,RailVolumeSlider.ActualHeight));var mixer=RailMixerButton.TransformToVisual(_releaseHome).TransformBounds(new Windows.Foundation.Rect(0,0,RailMixerButton.ActualWidth,RailMixerButton.ActualHeight));var mic=RailMicButton.TransformToVisual(_releaseHome).TransformBounds(new Windows.Foundation.Rect(0,0,RailMicButton.ActualWidth,RailMicButton.ActualHeight));
                if(mixer.Left<slider.Right||mic.Left<mixer.Right||RailVolumeSlider.XYFocusRight!=RailMixerButton||RailMixerButton.XYFocusRight!=RailMicButton||!RailMixerButton.Focus(FocusState.Keyboard)||!RailMicButton.Focus(FocusState.Keyboard))throw new InvalidOperationException("Media action order or controller focus incorrect");
                var denied=await App.SendMessageAsync(new ValueSet{{"ToggleMicrophoneMute",true}});if(denied==null||!denied.ContainsKey("Error"))throw new InvalidOperationException("Microphone write without input marker accepted");
                var toggles=new JsonArray();
                // Preserve an already-muted microphone; never enable it for a test.
                for(int n=0;!original&&n<2;n++)
                {
                    await Tap();var actual=await ReadOnexMediaAsync();ApplyOnexMediaLevels(actual.Stringify());bool expected=n==0?!original:original;
                    if(actual.GetNamedBoolean("microphoneMuted")!=expected||actual.GetNamedBoolean("muted")!=before.GetNamedBoolean("muted")||Math.Abs(actual.GetNamedNumber("volume")-before.GetNamedNumber("volume"))>.02||actual.GetNamedNumber("brightness")!=before.GetNamedNumber("brightness"))throw new InvalidOperationException("Microphone toggle changed the wrong audio controls");
                    if(RailMicIcon.Glyph!=(expected?"\uEC54":"\uE720"))throw new InvalidOperationException("Microphone icon is stale");
                    toggles.Add(actual);await SaveOnexImage(expected?"onex-media-microphone-muted.png":"onex-media-microphone-on.png",_releaseHome.MediaSection,0);
                }
                if(original)await SaveOnexImage("onex-media-microphone-muted.png",_releaseHome.MediaSection,0);
                report["originallyMutedToggleTestSkipped"]=JsonValue.CreateBooleanValue(original);
                report["microphoneToggles"]=toggles;report["unmarkedWriteRejected"]=JsonValue.CreateBooleanValue(true);report["oldSliderWidth"]=JsonValue.CreateNumberValue(oldBrightness);report["newSliderWidth"]=JsonValue.CreateNumberValue(brightness);report["equalReduction"]=JsonValue.CreateNumberValue(oldBrightness-brightness);report["stableReadoutLabels"]=widths;report["controllerActionChain"]=JsonValue.CreateStringValue("volume,mixer,microphone");
            }
            finally
            {
                RailMixerButton.Visibility=Visibility.Visible;RailMicButton.Visibility=Visibility.Visible;
                var current=await ReadOnexMediaAsync();if(current.GetNamedBoolean("microphoneMuted")!=original){await Tap();current=await ReadOnexMediaAsync();}
                if(current.GetNamedBoolean("microphoneMuted")!=original)throw new InvalidOperationException("Could not restore original microphone mute");
                ApplyOnexMediaLevels(current.Stringify());report["originalMicrophoneRestored"]=JsonValue.CreateBooleanValue(true);report["finalMediaState"]=current;
            }
            var mixerPeer=new Windows.UI.Xaml.Automation.Peers.ButtonAutomationPeer(RailMixerButton);
            ((Windows.UI.Xaml.Automation.Provider.IInvokeProvider)mixerPeer.GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            report["mixerInvokedViaNativeButton"]=JsonValue.CreateBooleanValue(true);
        }
        private async Task VerifyOnexCompactMediaAsync(JsonObject report)
        {
            var before=await ReadOnexMediaAsync();ApplyOnexMediaLevels(before.Stringify());UpdateLayout();
            Windows.Foundation.Rect Bounds(FrameworkElement e)=>e.TransformToVisual(this).TransformBounds(new Windows.Foundation.Rect(0,0,e.ActualWidth,e.ActualHeight));
            var geometry=new JsonArray();string oldBrightness=RailBrightnessValue.Text,oldVolume=RailVolumeValue.Text;
            double width=RailBrightnessSlider.ActualWidth;
            try
            {
                foreach(string label in new[]{"0%","9%","50%","100%"})
                {
                    RailBrightnessValue.Text=label;RailVolumeValue.Text=label;UpdateLayout();
                    foreach(var pair in new[]{System.Tuple.Create(RailBrightnessSlider,RailBrightnessValue,(FrameworkElement)RailBrightnessIcon),System.Tuple.Create(RailVolumeSlider,RailVolumeValue,(FrameworkElement)RailMuteButton)})
                    {
                        var track=(FrameworkElement)FindDescendantByName(pair.Item1,"HorizontalTrackRect");var line=Bounds(track);var icon=Bounds(pair.Item3);var readout=Bounds(pair.Item2);
                        double iconGap=line.Left-icon.Right,percentGap=readout.Left-line.Right;
                        if(Math.Abs(pair.Item1.ActualWidth-width)>.1||iconGap<0||iconGap>8||percentGap<0||percentGap>8||pair.Item2.TextAlignment!=TextAlignment.Left)throw new InvalidOperationException("Media controls still have oversized gaps or unstable widths");
                        var natural=new TextBlock{Text=label,FontFamily=pair.Item2.FontFamily,FontSize=pair.Item2.FontSize,FontWeight=pair.Item2.FontWeight,FontStretch=pair.Item2.FontStretch,UseLayoutRounding=false};natural.Measure(new Windows.Foundation.Size(double.PositiveInfinity,double.PositiveInfinity));
                        if(natural.DesiredSize.Width>pair.Item2.ActualWidth+.5)throw new InvalidOperationException("Media percentage does not fit: "+label);
                        geometry.Add(new JsonObject{{"label",JsonValue.CreateStringValue(label)},{"sliderWidth",JsonValue.CreateNumberValue(pair.Item1.ActualWidth)},{"iconSlotGap",JsonValue.CreateNumberValue(iconGap)},{"percentGap",JsonValue.CreateNumberValue(percentGap)}});
                    }
                }
            }
            finally{RailBrightnessValue.Text=oldBrightness;RailVolumeValue.Text=oldVolume;}
            await SaveOnexImage("onex-compact-media.png",_releaseHome.MediaSection,0);
            var after=await ReadOnexMediaAsync();foreach(string key in new[]{"brightness","volume","brightnessWrites","volumeWrites","muteWrites","microphoneMuteWrites"})if(Math.Abs(after.GetNamedNumber(key)-before.GetNamedNumber(key))>.02)throw new InvalidOperationException("Visual check changed media settings: "+key);
            if(after.GetNamedBoolean("muted")!=before.GetNamedBoolean("muted")||after.GetNamedBoolean("microphoneMuted")!=before.GetNamedBoolean("microphoneMuted"))throw new InvalidOperationException("Visual check changed a mute state");
            report["compactGeometry"]=geometry;report["mediaStateUnchanged"]=JsonValue.CreateBooleanValue(true);
            var mixer=new Windows.UI.Xaml.Automation.Peers.ButtonAutomationPeer(RailMixerButton);
            ((Windows.UI.Xaml.Automation.Provider.IInvokeProvider)mixer.GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();report["mixerInvokedViaNativeButton"]=JsonValue.CreateBooleanValue(true);
        }
        private static async Task<JsonObject> ReadOnexMediaAsync()
        {
            var reply = await App.SendMessageAsync(new ValueSet { ["GetMediaLevels"] = "1" });
            return JsonObject.Parse((string)reply["MediaLevels"]);
        }
        private async Task WaitMediaValue(string key, double target)
        {
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(100);
                if (_onexMediaWriteError != null) throw new InvalidOperationException(_onexMediaWriteError);
                var actual = await ReadOnexMediaAsync();
                if (Math.Abs(actual.GetNamedNumber(key) - target) < .02 && _onexMediaInFlight == null && _onexPendingMedia.Count == 0) return;
            }
            throw new InvalidOperationException("Windows did not confirm the UI change: " + key);
        }
        private async Task ExerciseOnexMediaAsync()
        {
            if (!System.IO.File.Exists(System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, "onex-media-verification-armed.json"))) return;
            var report = new JsonObject();
            var before = await ReadOnexMediaAsync();
            int brightness = (int)before.GetNamedNumber("brightness");
            int volume = (int)Math.Round(before.GetNamedNumber("volume"));
            try
            {
                await RefreshMediaSliderLevelsAsync();
                if (!RailBrightnessSlider.IsEnabled || !RailVolumeSlider.IsEnabled) throw new InvalidOperationException("Media sliders are disabled");
                var rejected = await App.SendMessageAsync(new ValueSet { ["SetBrightnessLevel"] = brightness - 1 });
                if (rejected == null || !rejected.ContainsKey("Error")) throw new InvalidOperationException("Unmarked brightness write was accepted");
                rejected = await App.SendMessageAsync(new ValueSet { ["SetVolumeLevel"] = 101, ["OnexMediaUserInput"] = true });
                if (rejected == null || !rejected.ContainsKey("Error")) throw new InvalidOperationException("Invalid volume percentage was accepted");
                report["invalidWritesRejected"] = JsonValue.CreateBooleanValue(true);
                int brightnessTarget = brightness > 2 ? brightness - 1 : brightness + 1;
                int volumeTarget = volume > 2 ? volume - 2 : volume + 1;
                RailBrightnessSlider.Value = brightnessTarget; // Imported ValueChanged -> coalesced pipe request -> WMI.
                await WaitMediaValue("brightness", brightnessTarget);
                report["brightnessApplied"] = await ReadOnexMediaAsync();
                RailVolumeSlider.Value = volumeTarget; // Imported ValueChanged -> Core Audio endpoint.
                await WaitMediaValue("volume", volumeTarget);
                var volumeApplied = await ReadOnexMediaAsync();
                if (volumeApplied.GetNamedBoolean("muted") != before.GetNamedBoolean("muted")) throw new InvalidOperationException("Volume slider changed the mute state");
                report["volumeApplied"] = volumeApplied;
                RailBrightnessSlider.Value = brightness;
                RailVolumeSlider.Value = volume;
                await WaitMediaValue("brightness", brightness);
                await WaitMediaValue("volume", volume);
                var beforeExternal = await ReadOnexMediaAsync();
                report["beforeExternal"] = beforeExternal;
                var ready = await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-media-external-ready.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(ready, beforeExternal.Stringify());
                StorageFile signal = null;
                for (int i = 0; i < 250 && signal == null; i++)
                {
                    signal = await ApplicationData.Current.LocalFolder.TryGetItemAsync("onex-media-external-applied.json") as StorageFile;
                    if (signal == null) await Task.Delay(100);
                }
                if (signal == null) throw new TimeoutException("External Windows media verification did not arrive");
                var expected = JsonObject.Parse(await FileIO.ReadTextAsync(signal));
                bool synchronized = false;
                for (int i = 0; i < 60; i++)
                {
                    if (_onexMediaState != null && Math.Abs(RailBrightnessSlider.Value - expected.GetNamedNumber("brightness")) < .02 &&
                        Math.Abs(RailVolumeSlider.Value - expected.GetNamedNumber("volume")) < .02 &&
                        RailBrightnessValue.Text == expected.GetNamedNumber("brightness") + "%" &&
                        RailVolumeValue.Text == expected.GetNamedNumber("volume") + "%" &&
                        _onexMediaState.GetNamedBoolean("muted") == expected.GetNamedBoolean("muted") &&
                        RailMuteIcon.Glyph == (expected.GetNamedBoolean("muted") ? "\uE74F" : "\uE767"))
                    { synchronized = true; break; }
                    await Task.Delay(100);
                }
                if (!synchronized) throw new InvalidOperationException("External Windows changes did not synchronize to the original rail");
                var afterExternal = await ReadOnexMediaAsync();
                foreach (var counter in new[] { "brightnessWrites", "volumeWrites", "muteWrites" })
                    if (afterExternal.GetNamedNumber(counter) != beforeExternal.GetNamedNumber(counter)) throw new InvalidOperationException("OS readback echoed as a media write");
                report["afterExternal"] = afterExternal;
                report["externalSyncPassed"] = JsonValue.CreateBooleanValue(true);
                report["noWriteEcho"] = JsonValue.CreateBooleanValue(true);
                report["passed"] = JsonValue.CreateBooleanValue(true);
            }
            catch (Exception ex)
            {
                report["passed"] = JsonValue.CreateBooleanValue(false);
                report["error"] = JsonValue.CreateStringValue(ex.ToString());
            }
            finally
            {
                // Restore even if the independent verifier exits early. Keep the endpoint's real
                // fractional percentage rather than rounding it to the integer thumb position.
                try
                {
                    await App.SendMessageAsync(new ValueSet { ["SetBrightnessLevel"] = brightness, ["OnexMediaUserInput"] = true });
                    await App.SendMessageAsync(new ValueSet { ["SetVolumeLevel"] = before.GetNamedNumber("volume"), ["OnexMediaUserInput"] = true });
                }
                catch (Exception ex) { report["restoreError"] = JsonValue.CreateStringValue(ex.Message); }
                // The independent Windows test process restores exact raw endpoint volume, including
                // fractional values which a percentage slider cannot reproduce, even on UI failure.
                report["before"] = before;
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-media-verification.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, report.Stringify());
                await Task.Delay(1500);
            }
        }
    }
}
