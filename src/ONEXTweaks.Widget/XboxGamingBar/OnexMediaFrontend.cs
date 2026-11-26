// SPDX-License-Identifier: AGPL-3.0-only
// Connect the original ClawTweaks media rail to the ONEXTweaks Windows media controller.
using System;
using System.Collections.Generic;
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
        private bool _onexBrightnessAvailable, _onexVolumeAvailable, _onexMicrophoneAvailable, _onexMicrophoneBusy;
        private string _onexMediaEpoch, _onexMediaInFlight;
        private long _onexMediaRevision;
        private readonly Dictionary<string, int> _onexPendingMedia = new Dictionary<string, int>();
        private DispatcherTimer _onexMediaTimer;
        private JsonObject _onexMediaState;
        private string _onexMediaWriteError;

        private void ApplyOnexMediaAvailability()
        {
            if (RailBrightnessSlider != null) RailBrightnessSlider.IsEnabled = App.IsConnected && _onexBrightnessAvailable;
            if (RailVolumeSlider != null) RailVolumeSlider.IsEnabled = App.IsConnected && _onexVolumeAvailable;
            if (RailMuteButton != null) RailMuteButton.IsEnabled = App.IsConnected;
            if(RailMixerButton!=null)RailMixerButton.IsEnabled=App.IsConnected&&_onexVolumeAvailable;
            if(RailMicButton!=null)RailMicButton.IsEnabled=App.IsConnected&&!_onexMicrophoneBusy;
            if (!_onexBrightnessAvailable && RailBrightnessValue != null) RailBrightnessValue.Text = "—";
            if (!_onexVolumeAvailable && RailVolumeValue != null) RailVolumeValue.Text = "—";
        }

        private void ApplyOnexMediaLevels(string payload, bool allowEpochChange = false)
        {
            if (!JsonObject.TryParse(payload, out var levels)) return;
            string epoch = levels.GetNamedString("epoch", "");
            long revision = (long)levels.GetNamedNumber("revision", 0);
            if (_onexMediaEpoch != epoch)
            {
                if (_onexMediaEpoch != null && !allowEpochChange) return; // Old in-flight reply after reconnect.
                _onexMediaEpoch = epoch;
                _onexMediaRevision = 0;
                _onexPendingMedia.Clear();
                _onexMediaTimer?.Stop();
            }
            if (revision <= _onexMediaRevision) return;
            _onexMediaRevision = revision;
            _onexMediaState = levels;
            _onexBrightnessAvailable = levels.GetNamedBoolean("brightnessAvailable", false);
            _onexVolumeAvailable = levels.GetNamedBoolean("volumeAvailable", false);
            _onexMicrophoneAvailable=levels.GetNamedBoolean("microphoneAvailable",false);
            _mediaSlidersUpdating = true; // Readback and OS-button changes must never echo back as writes.
            try
            {
                if (_onexBrightnessAvailable && !OnexMediaBusy("SetBrightnessLevel") && !OnexInputGuard.IsEditing(RailBrightnessSlider))
                {
                    double value = levels.GetNamedBoolean("brightnessDimmed",false)?levels.GetNamedNumber("brightnessRestore"):levels.GetNamedNumber("brightness");
                    RailBrightnessSlider.Value = value;
                    RailBrightnessValue.Text = Math.Round(value) + "%";
                }
                if (_onexVolumeAvailable)
                {
                    if (!OnexMediaBusy("SetVolumeLevel") && !OnexInputGuard.IsEditing(RailVolumeSlider))
                    {
                        double value = levels.GetNamedNumber("volume");
                        RailVolumeSlider.Value = Math.Round(value);
                        RailVolumeValue.Text = Math.Round(value) + "%";
                    }
                    ApplyMuteVisual(levels.GetNamedBoolean("muted"));
                    ToolTipService.SetToolTip(RailMuteButton,(levels.GetNamedBoolean("muted")?global::XboxGamingBar.OnexStrings.Get("Включить звук"):global::XboxGamingBar.OnexStrings.Get("Выключить звук"))+global::XboxGamingBar.OnexStrings.Get(" · удержание / X: устройство вывода"));
                }
                ApplyOnexBrightnessVisual();
                ApplyOnexMicrophoneVisual();
                ToolTipService.SetToolTip(RailBrightnessSlider, _onexBrightnessAvailable ? global::XboxGamingBar.OnexStrings.Get("Screen brightness") : ErrorText(levels, "brightnessError"));
                ToolTipService.SetToolTip(RailVolumeSlider, _onexVolumeAvailable ? global::XboxGamingBar.OnexStrings.Get("System volume") : ErrorText(levels, "volumeError"));
            }
            finally { _mediaSlidersUpdating = false; }
            ApplyOnexMediaAvailability();
        }
        private void ApplyOnexMicrophoneVisual()
        {
            if(RailMicIcon==null)return;
            bool muted=_onexMicrophoneAvailable&&_onexMediaState?.GetNamedBoolean("microphoneMuted",false)==true;
            RailMicIcon.FontFamily=RailBrightnessIcon.FontFamily;RailMicIcon.FontSize=RailBrightnessIcon.FontSize;
            RailMicIcon.Glyph=muted?"\uEC54":"\uE720";
            RailMicIcon.Foreground=!_onexMicrophoneAvailable?new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,112,112,112)):muted?new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,255,107,107)):MediaAccentBrush();
            if(RailMixerButton?.Content is FontIcon mixer)mixer.Foreground=MediaAccentBrush();
            string hint=!_onexMicrophoneAvailable?OnexStrings.Get("Default microphone unavailable"):muted?OnexStrings.Get("Unmute default microphone"):OnexStrings.Get("Mute default microphone");
            ToolTipService.SetToolTip(RailMicButton,hint+global::XboxGamingBar.OnexStrings.Get(" · удержание / X: устройство ввода"));Windows.UI.Xaml.Automation.AutomationProperties.SetName(RailMicButton,hint);
        }
        private async void RailMicButton_Click(object sender,RoutedEventArgs e)
        {
            if(SuppressOnexHeldClick(RailMicButton))return;
            if(App.IsConnected&&!_onexMicrophoneAvailable){await ShowOnexAudioInputAsync(RailMicButton);return;}
            if(!App.IsConnected||!_onexMicrophoneAvailable||_onexMicrophoneBusy)return;
            _onexMicrophoneBusy=true;ApplyOnexMediaAvailability();
            try{var response=await App.SendMessageAsync(new ValueSet{{"ToggleMicrophoneMute",true},{"OnexMediaUserInput",true}});if(response==null||response.ContainsKey("Error"))throw new InvalidOperationException(response?.ContainsKey("Error")==true?response["Error"].ToString():"ONEXTweaks disconnected");ApplyOnexMediaLevels((string)response["MediaLevels"]);}
            catch(Exception error){ShowOnexDesktopStatus(error.Message);}
            finally{_onexMicrophoneBusy=false;ApplyOnexMediaAvailability();}
        }
        private async void RailMixerButton_Click(object sender,RoutedEventArgs e)=>await ShowOnexAudioMixerAsync(RailMixerButton);
        private static string ErrorText(JsonObject levels, string key)
            => levels.TryGetValue(key, out var value) && value.ValueType == JsonValueType.String ? value.GetString() : OnexStrings.Get("Windows control unavailable");
        private bool OnexMediaBusy(string key) => _onexMediaInFlight == key || _onexPendingMedia.ContainsKey(key);

        private Task QueueOnexMediaLevelAsync(string key, int value)
        {
            if (!App.IsConnected || _mediaSlidersUpdating) return Task.CompletedTask;
            if (key == "SetBrightnessLevel" ? !_onexBrightnessAvailable : !_onexVolumeAvailable) return Task.CompletedTask;
            _onexMediaWriteError = null;
            _onexPendingMedia[key] = value;
            if (_onexMediaTimer == null)
            {
                _onexMediaTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                _onexMediaTimer.Tick += FlushOnexMedia;
            }
            _onexMediaTimer.Stop();
            _onexMediaTimer.Start();
            return Task.CompletedTask;
        }
        private async void FlushOnexMedia(object sender, object e)
        {
            _onexMediaTimer.Stop();
            if (_onexMediaInFlight != null) return;
            try
            {
                while (_onexPendingMedia.Count > 0 && App.IsConnected)
                {
                    var next = _onexPendingMedia.First();
                    _onexPendingMedia.Remove(next.Key);
                    _onexMediaInFlight = next.Key;
                    var reply = await App.SendMessageAsync(new ValueSet { [next.Key] = next.Value, ["OnexMediaUserInput"] = true });
                    _onexMediaInFlight = null;
                    _onexMediaWriteError = reply != null && reply.TryGetValue("Error", out var error) ? error?.ToString() : null;
                    if (reply == null) _onexMediaWriteError = OnexStrings.Get("Media connection unavailable");
                    if (reply != null && reply.TryGetValue("MediaLevels", out var payload) && payload is string json)
                        ApplyOnexMediaLevels(json);
                    if (_onexMediaWriteError != null)
                    {
                        Control slider = next.Key == "SetBrightnessLevel" ? RailBrightnessSlider : RailVolumeSlider;
                        ToolTipService.SetToolTip(slider, _onexMediaWriteError);
                        Logger.Warn("Media change failed: " + _onexMediaWriteError);
                    }
                }
            }
            catch (Exception ex) { _onexMediaWriteError = ex.Message; Logger.Warn("Media slider: " + ex.Message); }
            finally
            {
                _onexMediaInFlight = null;
                if (!App.IsConnected) _onexPendingMedia.Clear();
                ApplyOnexMediaAvailability();
                if (_onexPendingMedia.Count > 0) _onexMediaTimer.Start();
            }
        }
    }
}
