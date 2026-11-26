// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Automation.Peers;
using Windows.Graphics.Display;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexPl2RestoreAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;var prefs=ApplicationData.Current.LocalSettings.Values;var before=await ReadOnexStateAsync();bool hadFlag=prefs.TryGetValue("OnexSeparatePl2",out var flag),hadManual=prefs.TryGetValue("OnexRememberedPl2Watts",out var manual);
            var toggle=(ToggleSwitch)_releaseHome.FindName("LightPl2Toggle");var slider=(Slider)_releaseHome.FindName("OnexPl2Slider");string run=Guid.NewGuid().ToString("N");int step=0;var checks=new JsonArray();
            async Task<JsonObject> Profiles(){var reply=await App.SendMessageAsync(new ValueSet{{"OnexProfilesGet",true}});return JsonObject.Parse((string)reply["OnexProfilesState"]);}
            string ProfileValues(JsonObject state)=>string.Join("|",state.GetNamedString("activeId"),state.GetNamedObject("flags").Stringify(),string.Join(";",state.GetNamedArray("entries").Select(v=>{var row=v.GetObject();return row.GetNamedString("id")+":"+row.GetNamedBoolean("split")+":"+row.GetNamedObject("ac").Stringify()+":"+row.GetNamedObject("dc").Stringify();})));
            var profileBefore=await Profiles();
            async Task WaitPower(int pl1,int pl2)
            {for(int n=0;n<80;n++){await Task.Delay(100);var actual=await ReadOnexStateAsync();if(actual.GetNamedNumber("pl1Watts")==pl1&&actual.GetNamedNumber("pl2Watts")==pl2&&toggle.IsEnabled&&_releaseHome.PowerInput.IsEnabled&&((TextBlock)_releaseHome.FindName("LightPowerPl2")).Text=="PL2 "+pl2+" W")return;}throw new InvalidOperationException("PL2 first-action state not acknowledged: "+pl1+"/"+pl2);}
            async Task Input(string key,Func<bool> expected)
            {
                string id=run+":"+(++step);var rect=new RadioButtonAutomationPeer(PerformanceNavItem).GetBoundingRectangle();double scale=DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
                var packet=new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2+widget.WindowBounds.X*scale)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2+widget.WindowBounds.Y*scale)}};
                for(int n=0;;n++){try{System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-gamepad-qa-state.json"),packet.Stringify());break;}catch(System.IO.IOException){if(n>=20)throw;await Task.Delay(25);}}
                bool ok=false;for(int n=0;n<70;n++){await Task.Delay(100);try{if(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&expected()){ok=true;break;}}catch{}}if(!ok)throw new InvalidOperationException("PL2 controller action failed: "+key+" step "+step);
            }
            try
            {
                PerformanceNavItem.IsChecked=true;await Input("focus",()=>true);
                foreach(var pair in new[]{Tuple.Create(12,24),Tuple.Create(15,28),Tuple.Create(12,13)})
                {
                    toggle.IsOn=true;await SetOnexReleasePowerAsync(pair.Item1,pair.Item2);await WaitPower(pair.Item1,pair.Item2);toggle.Focus(FocusState.Keyboard);
                    await Input("a",()=>!toggle.IsOn&&slider.Visibility==Visibility.Collapsed);await WaitPower(pair.Item1,pair.Item1+1);
                    if(_releaseHome.RememberedPl2Watts!=pair.Item2)throw new InvalidOperationException("Disabled readback overwrote remembered PL2");
                    var recreated=new OnexReleaseHome();recreated.ApplyPower(pair.Item1,pair.Item1+1,true);if(((ToggleSwitch)recreated.FindName("LightPl2Toggle")).IsOn||recreated.RememberedPl2Watts!=pair.Item2)throw new InvalidOperationException("PL2 preference did not survive view reconstruction");
                    int changedPl1=pair.Item1,delta=pair.Item2==pair.Item1+1?2:1;
                    for(int i=0;i<delta;i++){_releaseHome.PowerInput.Focus(FocusState.Keyboard);_releaseHome.PowerInput.IsFocusEngaged=true;int next=changedPl1+1;await Input("right",()=>_releaseHome.PowerInput.Value==next);changedPl1=next;await WaitPower(changedPl1,changedPl1+1);_releaseHome.PowerInput.IsFocusEngaged=false;}
                    toggle.Focus(FocusState.Keyboard);await Input("a",()=>toggle.IsOn&&slider.Visibility==Visibility.Visible);int restored=Math.Max(changedPl1,pair.Item2);await WaitPower(changedPl1,restored);
                    if(_releaseHome.RememberedPl2Watts!=restored)throw new InvalidOperationException("Restored bounded PL2 was not remembered");
                    checks.Add(new JsonObject{{"pl1",JsonValue.CreateNumberValue(pair.Item1)},{"manualPl2",JsonValue.CreateNumberValue(pair.Item2)},{"offPl2",JsonValue.CreateNumberValue(pair.Item1+1)},{"pl1ChangedWhileOff",JsonValue.CreateNumberValue(changedPl1)},{"restoredPl2",JsonValue.CreateNumberValue(restored)}});
                }
                report["offOnCases"]=checks;report["actualControllerActions"]=JsonValue.CreateNumberValue(step);report["durableViewReconstruction"]=JsonValue.CreateBooleanValue(true);report["firstActionUiAndHardwareConsistent"]=JsonValue.CreateBooleanValue(true);report["physicalControllerTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                toggle.IsOn=hadFlag&&flag is bool originalFlag?originalFlag:false;
                if(hadFlag)prefs["OnexSeparatePl2"]=flag;else prefs.Remove("OnexSeparatePl2");if(hadManual)prefs["OnexRememberedPl2Watts"]=manual;else prefs.Remove("OnexRememberedPl2Watts");_releaseHome.ReloadPowerPreferences();
                await SetOnexReleasePowerAsync((int)before.GetNamedNumber("pl1Watts"),(int)before.GetNamedNumber("pl2Watts"));
                var profileAfter=await Profiles();if(ProfileValues(profileAfter)!=ProfileValues(profileBefore))throw new InvalidOperationException("PL2 verification changed saved profile values");
                report["originalPowerPreferencesAndProfilesRestored"]=JsonValue.CreateBooleanValue(true);System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-gamepad-qa-state.json"),"{\"finished\":true}");
            }
        }
    }
}
