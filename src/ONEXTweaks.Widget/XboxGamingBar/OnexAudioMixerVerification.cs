// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexAudioMixerAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;
            async Task Phase(string phase)=>await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-mixer-test-phase.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"phase",JsonValue.CreateStringValue(phase)}}.Stringify());
            void Invoke(Windows.UI.Xaml.Controls.Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            JsonObject AppRow(string appToken)=>_onexMixerState.GetNamedArray("apps").Select(a=>a.GetObject()).FirstOrDefault(a=>a.GetNamedString("token")==appToken);
            async Task Wait(string appToken,double volume,bool mute)
            {
                for(int n=0;n<70;n++){await Task.Delay(100);await RefreshOnexMixerAsync();var actual=AppRow(appToken);if(actual!=null&&Math.Abs(actual.GetNamedNumber("volume")-volume)<.02&&actual.GetNamedBoolean("muted")==mute&&_onexMixerPending.Count==0&&!_onexMixerFlushBusy)return;}
                throw new InvalidOperationException("App mixer did not confirm volume/mute");
            }
            string token=null;double originalVolume=0;bool originalMute=false;var before=await ReadOnexMediaAsync();
            try
            {
                for(int n=0;n<70&&(_onexDetailsDialog==null||_onexMixerState==null);n++)await Task.Delay(100);
                if(!_onexMixerOpen||_onexDetailsDialog==null||!Windows.Gaming.UI.GameBar.IsInputRedirected)throw new InvalidOperationException("Mixer did not open inside our widget");
                await Phase("open-await-audio");uint pid=0;
                for(int n=0;n<150&&pid==0;n++){var target=await folder.TryGetItemAsync("onex-mixer-test-target.json") as StorageFile;if(target!=null)pid=(uint)JsonObject.Parse(await FileIO.ReadTextAsync(target)).GetNamedNumber("pid");else await Task.Delay(100);}
                if(pid==0)throw new InvalidOperationException("Silent test audio process was not started");
                MixerRow row=null;
                for(int n=0;n<100&&row==null;n++){await RefreshOnexMixerAsync();row=_onexMixerRows.Values.FirstOrDefault(a=>a.Pid==pid);if(row==null)await Task.Delay(100);}
                if(row==null)throw new InvalidOperationException("New audio app did not appear while mixer remained open");
                token=row.Token;var initial=AppRow(token);originalVolume=initial.GetNamedNumber("volume");originalMute=initial.GetNamedBoolean("muted");
                var others=new JsonArray();foreach(var item in _onexMixerState.GetNamedArray("apps")){if(item.GetObject().GetNamedString("token")!=token)others.Add(JsonObject.Parse(item.Stringify()));}
                var rejected=await App.SendMessageAsync(new ValueSet{{"OnexAudioMixerSet",new JsonObject{{"token",JsonValue.CreateStringValue(token)},{"volume",JsonValue.CreateNumberValue(43)}}.Stringify()}});
                if(rejected==null||!rejected.ContainsKey("Error"))throw new InvalidOperationException("Unmarked mixer write accepted");
                int targetVolume=Math.Abs(originalVolume-43)<1?61:43;
                ((IRangeValueProvider)new SliderAutomationPeer(row.Slider).GetPattern(PatternInterface.RangeValue)).SetValue(targetVolume);
                await Wait(token,targetVolume,originalMute);report["appSliderReadback"]=JsonValue.CreateNumberValue(AppRow(token).GetNamedNumber("volume"));
                Invoke(row.Mute);await Wait(token,targetVolume,!originalMute);report["appMuteReadback"]=JsonValue.CreateBooleanValue(AppRow(token).GetNamedBoolean("muted"));
                row.Slider.Focus(Windows.UI.Xaml.FocusState.Keyboard);row.Card.StartBringIntoView(new Windows.UI.Xaml.BringIntoViewOptions{AnimationDesired=false});await Task.Delay(200);
                await SaveOnexImage("onex-custom-mixer-muted.png",_onexDetailsDialog,0);
                Invoke(row.Mute);await Wait(token,targetVolume,originalMute);
                foreach(var item in others){var expected=item.GetObject();var actual=AppRow(expected.GetNamedString("token"));if(actual!=null&&(Math.Abs(actual.GetNamedNumber("volume")-expected.GetNamedNumber("volume"))>.02||actual.GetNamedBoolean("muted")!=expected.GetNamedBoolean("muted")))throw new InvalidOperationException("Mixer test changed another app");}
                var after=await ReadOnexMediaAsync();foreach(string key in new[]{"brightness","volume","brightnessWrites","volumeWrites","muteWrites","microphoneMuteWrites"})if(Math.Abs(after.GetNamedNumber(key)-before.GetNamedNumber(key))>.02)throw new InvalidOperationException("App mixer changed master/media controls: "+key);
                if(after.GetNamedBoolean("muted")!=before.GetNamedBoolean("muted")||after.GetNamedBoolean("microphoneMuted")!=before.GetNamedBoolean("microphoneMuted"))throw new InvalidOperationException("App mixer changed master/microphone mute");
                report["masterVolumeBefore"]=JsonValue.CreateNumberValue(before.GetNamedNumber("volume"));report["masterVolumeAfter"]=JsonValue.CreateNumberValue(after.GetNamedNumber("volume"));
                report["customDialogInsideGameBar"]=JsonValue.CreateBooleanValue(true);report["newSessionAppearedWhileOpen"]=JsonValue.CreateBooleanValue(true);report["unmarkedMixerWriteRejected"]=JsonValue.CreateBooleanValue(true);report["otherAudioUnchanged"]=JsonValue.CreateBooleanValue(true);
            }
            finally
            {
                if(token!=null){await SetOnexMixerAsync(token,(int)Math.Round(originalVolume),false);await RefreshOnexMixerAsync();if(AppRow(token)?.GetNamedBoolean("muted")!=originalMute)await SetOnexMixerAsync(token,null,true);var restored=AppRow(token);if(restored==null||Math.Abs(restored.GetNamedNumber("volume")-originalVolume)>.51||restored.GetNamedBoolean("muted")!=originalMute)throw new InvalidOperationException("Silent test session could not be restored");report["silentTestAppRestored"]=JsonValue.CreateBooleanValue(true);}
                if(_onexDetailsDialog!=null){await SaveOnexImage("onex-custom-mixer.png",_onexDetailsDialog,0);_onexDetailsDialog.Hide();}
                for(int n=0;n<70&&_onexMixerOpen;n++)await Task.Delay(50);
                if(_onexMixerOpen||_onexDetailsDialog!=null)throw new InvalidOperationException("Mixer dialog did not close");
                report["customDialogClosed"]=JsonValue.CreateBooleanValue(true);await Phase("done");
            }
        }
    }
}
