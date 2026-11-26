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
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private Task VerifyOnexAudioOutputAsync(JsonObject report)=>VerifyOnexAudioDeviceAsync(report,0);
        private Task VerifyOnexAudioInputAsync(JsonObject report)=>VerifyOnexAudioDeviceAsync(report,1);
        private async Task VerifyOnexAudioDeviceAsync(JsonObject report,int flow)
        {
            var button=flow==0?RailMuteButton:RailMicButton;string get=flow==0?"OnexAudioOutputGet":"OnexAudioInputGet",set=flow==0?"OnexAudioOutputSet":"OnexAudioInputSet",mute=flow==0?"muted":"microphoneMuted",muteWritesKey=flow==0?"muteWrites":"microphoneMuteWrites",muteAction=flow==0?"ToggleMute":"ToggleMicrophoneMute";

            async Task<JsonObject> Read(){var r=await App.SendMessageAsync(new ValueSet{{get,true}});return JsonObject.Parse((string)r[flow==0?"OnexAudioOutputState":"OnexAudioInputState"]);}
            void Invoke(Button b)=>((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke();
            async Task WaitPopup(){for(int i=0;i<100;i++){if(_onexAudioOutputList!=null&&_onexAudioOutputState?.GetNamedBoolean("available",false)==true&&_onexAudioOutputList.Items.Count>0&&!_onexAudioOutputReading)return;await Task.Delay(100);}throw new InvalidOperationException("Own output popup did not load actual endpoints");}
            string OtherDefaults(JsonObject state)=>string.Join("|",state.GetNamedArray("devices").Select(d=>d.GetObject()).Where(d=>d.GetNamedBoolean("defaultConsole")||d.GetNamedBoolean("defaultMultimedia")||d.GetNamedBoolean("defaultCommunications")).OrderBy(d=>d.GetNamedString("token")).Select(d=>d.GetNamedString("token")+d.GetNamedBoolean("defaultConsole")+d.GetNamedBoolean("defaultMultimedia")+d.GetNamedBoolean("defaultCommunications")));
            string otherGet=flow==0?"OnexAudioInputGet":"OnexAudioOutputGet",otherKey=flow==0?"OnexAudioInputState":"OnexAudioOutputState";
            var counterpart=await App.SendMessageAsync(new ValueSet{{otherGet,true}});string otherBefore=OtherDefaults(JsonObject.Parse((string)counterpart[otherKey]));var mediaBefore=JsonObject.Parse((string)counterpart["MediaLevels"]);
            var before=await Read();if(!before.GetNamedBoolean("available"))throw new InvalidOperationException("Output enumeration unavailable");
            var devices=before.GetNamedArray("devices").Select(d=>d.GetObject()).ToArray();var original=devices.Single(d=>d.GetNamedBoolean("defaultMultimedia"));string token=original.GetNamedString("token");bool switched=false;
            try
            {
                foreach(var bad in new[]{new ValueSet{{set,token}},new ValueSet{{set,Guid.NewGuid().ToString("N")},{"OnexMediaUserInput",true}}}){var rejected=await App.SendMessageAsync(bad);if(rejected==null||!rejected.ContainsKey("Error"))throw new InvalidOperationException("Invalid output change accepted");}
                if(!button.Focus(FocusState.Keyboard)||!OpenFocusedOnexTileDetails())throw new InvalidOperationException("Focused speaker X action did not open own popup");await WaitPopup();if(_onexAudioDeviceFlow!=flow)throw new InvalidOperationException("Wrong audio device flow popup");
                report["actualDeviceCount"]=JsonValue.CreateNumberValue(devices.Length);report["activeDeviceCount"]=JsonValue.CreateNumberValue(devices.Count(d=>d.GetNamedBoolean("active")));report["focusXOpensOwnPopup"]=JsonValue.CreateBooleanValue(true);report["invalidWritesRejected"]=JsonValue.CreateBooleanValue(true);
                if(_onexAudioOutputList.Items.Count!=devices.Length)throw new InvalidOperationException("Popup lost actual endpoints");
                if(_onexAudioOutputApply.IsEnabled)throw new InvalidOperationException("Already-default output action must be disabled");
                foreach(var item in _onexAudioOutputList.Items.Cast<ListViewItem>().Where(i=>!((JsonObject)i.Tag).GetNamedBoolean("active"))){_onexAudioOutputList.SelectedItem=item;if(_onexAudioOutputApply.IsEnabled||item.IsEnabled)throw new InvalidOperationException("Disconnected output selectable");}
                var alternate=(original.GetNamedBoolean("defaultConsole")&&(flow==0||original.GetNamedBoolean("defaultCommunications")))?devices.FirstOrDefault(d=>d.GetNamedBoolean("active")&&d.GetNamedString("token")!=token):null;
                if(alternate!=null)
                {
                    string other=alternate.GetNamedString("token");_onexAudioOutputList.SelectedItem=_onexAudioOutputList.Items.Cast<ListViewItem>().Single(i=>((JsonObject)i.Tag).GetNamedString("token")==other);if(!_onexAudioOutputApply.IsEnabled)throw new InvalidOperationException("Alternate output action disabled");switched=true;Invoke(_onexAudioOutputApply);
                    bool confirmed=false;for(int i=0;i<100;i++){await Task.Delay(100);var actual=await Read();if(!_onexAudioOutputWriting&&actual.GetNamedArray("devices").Any(d=>d.GetObject().GetNamedString("token")==other&&d.GetObject().GetNamedBoolean("defaultMultimedia"))){confirmed=true;break;}}if(!confirmed)throw new InvalidOperationException("Actual alternate endpoint selection failed");
                    report["crossEndpointSwitchTested"]=JsonValue.CreateBooleanValue(true);
                }
                else report["crossEndpointSwitchTested"]=JsonValue.CreateBooleanValue(false);
                _onexAudioOutputList.SelectedItem=_onexAudioOutputList.Items.Cast<ListViewItem>().Single(i=>((JsonObject)i.Tag).GetNamedString("token")==token);
                if(switched){Invoke(_onexAudioOutputApply);while(_onexAudioOutputWriting)await Task.Delay(100);}
                await SaveOnexImage(flow==0?"onex-audio-output-devices-private.png":"onex-audio-input-devices-private.png",_onexDetailsDialog,0);
                CloseOnexMenus();for(int i=0;i<30&&_onexAudioOutputOpen;i++)await Task.Delay(50);if(_onexDetailsDialog!=null||_onexAudioOutputList!=null)throw new InvalidOperationException("Output popup survived close");
                long muteWrites=(long)(_onexMediaState?.GetNamedNumber(muteWritesKey,0)??0);_onexHeldTiles[button]=DateTimeOffset.UtcNow;Invoke(button);await Task.Delay(300);
                if((_onexMediaState?.GetNamedNumber(muteWritesKey,0)??0)!=muteWrites)throw new InvalidOperationException("Held-release click toggled mute");report["heldReleaseClickSuppressed"]=JsonValue.CreateBooleanValue(true);
                if(!button.Focus(FocusState.Keyboard)||!OpenFocusedOnexTileDetails())throw new InvalidOperationException("Second speaker X failed");await WaitPopup();_onexHeldTiles[button]=DateTimeOffset.UtcNow;CloseOnexMenus();for(int i=0;i<30&&_onexAudioOutputOpen;i++)await Task.Delay(50);
                if(_onexHeldTiles.ContainsKey(button))throw new InvalidOperationException("Closed popup retained old hold suppression");
                report["staleHoldSuppressionClearedOnClose"]=JsonValue.CreateBooleanValue(true);
                async Task<JsonObject> Media(){var r=await App.SendMessageAsync(new ValueSet{{get,true}});return JsonObject.Parse((string)r["MediaLevels"]);}
                var originalMedia=await Media();bool muted=originalMedia.GetNamedBoolean(mute);
                async Task WaitMute(bool expected){for(int n=0;n<50;n++){var m=await Media();if(m.GetNamedBoolean(mute)==expected&&(flow==0||!_onexMicrophoneBusy))return;await Task.Delay(50);}throw new InvalidOperationException("Short speaker tap mute did not apply");}
                try{Invoke(button);await WaitMute(!muted);Invoke(button);await WaitMute(muted);report["shortTapMutesAndRestores"]=JsonValue.CreateBooleanValue(true);}
                finally{if((await Media()).GetNamedBoolean(mute)!=muted){await App.SendMessageAsync(new ValueSet{{muteAction,true},{"OnexMediaUserInput",true}});await WaitMute(muted);}}
                report["popupReopensCleanly"]=JsonValue.CreateBooleanValue(true);
            }
            finally
            {
                CloseOnexMenus();for(int i=0;i<30&&_onexAudioOutputOpen;i++)await Task.Delay(50);
                var after=await Read();var final=after.GetNamedArray("devices").Select(d=>d.GetObject()).ToArray();
                if(!final.Any(d=>d.GetNamedString("token")==token&&d.GetNamedBoolean("defaultMultimedia"))){await App.SendMessageAsync(new ValueSet{{set,token},{"OnexMediaUserInput",true}});after=await Read();final=after.GetNamedArray("devices").Select(d=>d.GetObject()).ToArray();}
                if(!final.Any(d=>d.GetNamedString("token")==token&&d.GetNamedBoolean("defaultMultimedia")))throw new InvalidOperationException("Original default output not restored");
                foreach(var device in devices.Where(d=>d.GetNamedBoolean("active")))
                {var actual=final.Single(d=>d.GetNamedString("token")==device.GetNamedString("token"));foreach(string key in new[]{"muted","defaultCommunications","defaultConsole","defaultMultimedia"})if(actual[key].Stringify()!=device[key].Stringify())throw new InvalidOperationException("Unexpected endpoint mutation: "+key);if(Math.Abs(actual.GetNamedNumber("volume")-device.GetNamedNumber("volume"))>.02)throw new InvalidOperationException("Endpoint volume changed");}
                counterpart=await App.SendMessageAsync(new ValueSet{{otherGet,true}});if(OtherDefaults(JsonObject.Parse((string)counterpart[otherKey]))!=otherBefore)throw new InvalidOperationException("Other audio direction defaults changed");
                var mediaAfter=JsonObject.Parse((string)counterpart["MediaLevels"]);string otherMute=flow==0?"microphoneMuted":"muted";if(mediaBefore[otherMute].Stringify()!=mediaAfter[otherMute].Stringify()||(flow==1&&Math.Abs(mediaBefore.GetNamedNumber("volume")-mediaAfter.GetNamedNumber("volume"))>.02))throw new InvalidOperationException("Other audio direction volume/mute changed");report["otherAudioDirectionPreserved"]=JsonValue.CreateBooleanValue(true);
                report[flow==0?"defaultOutputRestored":"defaultInputRestored"]=JsonValue.CreateBooleanValue(true);report["perEndpointVolumeMutePreserved"]=JsonValue.CreateBooleanValue(true);if(flow==1)report["audioRecordingStarted"]=JsonValue.CreateBooleanValue(false);report[flow==0?"communicationsOutputPreserved":"communicationsInputRestored"]=JsonValue.CreateBooleanValue(true);
            }
        }
    }
}
