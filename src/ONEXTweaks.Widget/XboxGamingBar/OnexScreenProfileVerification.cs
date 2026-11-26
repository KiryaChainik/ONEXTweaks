// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexScreenProfilesAsync(JsonObject report)
        {
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            async Task Wait(Func<bool> predicate,string message){for(int n=0;n<100;n++){if(predicate())return;await Task.Delay(80);}throw new InvalidOperationException(message);}
            var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");int sequence=0;
            void Progress(string phase){report["phase"]=JsonValue.CreateStringValue(phase);System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-screen-profile-qa-progress.json"),phase);}
            async Task Phase(string phase)
            {
                string id=run+":"+(++sequence);string path=System.IO.Path.Combine(folder.Path,"onex-screen-profile-qa-state.json");
                System.IO.File.WriteAllText(path,new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"phase",JsonValue.CreateStringValue(phase)}}.Stringify());
                for(int n=0;n<180;n++){await Task.Delay(100);try{var ack=JsonObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder.Path,"onex-screen-profile-qa-ack.json")));if(ack.GetNamedString("id")==id){Check(ack.GetNamedBoolean("passed"),"Host phase failed: "+phase);await ReadOnexScreenProfilesAsync();return;}}catch(System.IO.FileNotFoundException){}catch(System.IO.IOException){}}
                throw new TimeoutException("Native process host did not complete "+phase);
            }
            async Task<MenuFlyout> Popup()
            {await Wait(()=>_onexOpenMenus.OfType<MenuFlyout>().Any(menu=>menu.IsOpen),"Profile menu did not open: "+_onexScreenProfilePicker.DiagnosticError);return _onexOpenMenus.OfType<MenuFlyout>().Last(menu=>menu.IsOpen);}
            async Task Invoke(MenuFlyoutItem item)
            {((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();await Task.Delay(100);}
            async Task Choose(string id)
            {await _onexScreenProfilePicker.OpenChooseAsync();await Invoke((await Popup()).Items.OfType<MenuFlyoutItem>().Single(item=>item.Tag as string==id));await Wait(()=>OnexScreenProfileId==id,"Profile choice did not settle");}
            async Task Brightness(int value)
            {await SetOnexDisplayAsync("sdrBrightness",JsonValue.CreateNumberValue(value));Check(_onexDisplayStatus.Visibility==Visibility.Collapsed,"SDR edit failed");await ReadOnexScreenProfilesAsync();var entry=OnexSelectedScreenProfile;var side=entry.GetNamedObject(_onexScreenProfiles.GetNamedBoolean("onAc")?"ac":"dc");Check(side.GetNamedObject("hdr").GetNamedNumber("sdrBrightness")==value,"Native edit did not autosave to selected profile");Check(_onexDisplayState.GetNamedObject("hdr").GetNamedNumber("sdrBrightness")==value,"Native CCD brightness differs from profile");}
            Progress("before screen QA power read");var powerBefore=await ReadOnexStateAsync();Progress("before display activation");DisplayNavItem.IsChecked=true;await RefreshOnexDisplayAsync(false);Progress("before screen catalog read");await ReadOnexScreenProfilesAsync();Progress("catalog ready");
            var initial=JsonObject.Parse(_onexScreenProfiles.Stringify());string initialId=OnexScreenProfileId;var initialEntries=initial.GetNamedArray("entries").Stringify();
            var created=new List<string>();bool hostStopped=false;
            Check(initialId=="global"&&!initial.GetNamedBoolean("manualOverride",false),"Profile QA requires Global without an owner manual override");
            try
            {
                Progress("manual create menu");await _onexScreenProfilePicker.OpenCreateAsync();var manual=(await Popup()).Items.OfType<MenuFlyoutItem>().First();Check(manual.Tag as string=="manual","Manual Profile is not the first Create entry");await Invoke(manual);
                await Wait(()=>OnexScreenProfileId!=initialId,"Manual profile was not created/selected");string manualId=OnexScreenProfileId;created.Add(manualId);await Brightness(1);
                Progress("process create menu");await _onexScreenProfilePicker.OpenCreateAsync();var app=(await Popup()).Items.OfType<MenuFlyoutItem>().FirstOrDefault(item=>item.Text.StartsWith("ONEXProfileProbe"));Check(app!=null,"Own native probe is absent from Create processes");await Invoke(app);
                await Wait(()=>OnexScreenProfileId!=manualId&&OnexScreenProfileId!="global","Process profile was not created/selected");string gameId=OnexScreenProfileId;created.Add(gameId);await Brightness(2);
                Progress("manual override");await Choose(manualId);Progress("same session host phase");await Phase("same-session");Progress("same session catalog check");await ReadOnexScreenProfilesAsync();Check(OnexScreenProfileId==manualId,"Same process session replaced the manual profile");await RefreshOnexDisplayAsync(false);Check(_onexDisplayState.GetNamedObject("hdr").GetNamedNumber("sdrBrightness")==1,"Manual profile did not retain actual SDR brightness");
                Progress("real process restart");await Phase("restart-process");await ReadOnexScreenProfilesAsync();Check(OnexScreenProfileId==gameId,"Real process restart did not reactivate its profile");await RefreshOnexDisplayAsync(false);Check(_onexDisplayState.GetNamedObject("hdr").GetNamedNumber("sdrBrightness")==2,"Automatic profile did not apply its actual native brightness");
                _onexScreenProfilePicker.Remove.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});((IInvokeProvider)new ButtonAutomationPeer(_onexScreenProfilePicker.Remove).GetPattern(PatternInterface.Invoke)).Invoke();await Task.Delay(100);await Invoke((await Popup()).Items.OfType<MenuFlyoutItem>().Single(item=>item.Text=="Keep It"));await Wait(()=>OnexScreenProfileId=="global","Keep It did not return Global");Check(_onexScreenProfiles.GetNamedArray("entries").Select(item=>item.GetObject()).Any(item=>item.GetNamedString("id")==gameId),"Keep It deleted the process profile");
                await Phase("stop-process");hostStopped=true;
                await Choose(manualId);((IInvokeProvider)new ButtonAutomationPeer(_onexScreenProfilePicker.Remove).GetPattern(PatternInterface.Invoke)).Invoke();await Task.Delay(100);await Invoke((await Popup()).Items.OfType<MenuFlyoutItem>().Single(item=>item.Text=="Delete"));await Wait(()=>OnexScreenProfileId=="global","Delete did not return Global");
                Check(!_onexScreenProfiles.GetNamedArray("entries").Select(item=>item.GetObject()).Any(item=>item.GetNamedString("id")==manualId),"Delete left its profile in the selector");
                report["realProcessSameSessionManualOverride"]=JsonValue.CreateBooleanValue(true);report["realProcessRestartAutoActivation"]=JsonValue.CreateBooleanValue(true);report["actualNativeSdrProfileValues"]=JsonValue.CreateBooleanValue(true);report["menuCreateChooseKeepDelete"]=JsonValue.CreateBooleanValue(true);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
                Progress("functional checks complete; optional capture");
                bool visible=Windows.Gaming.UI.GameBar.IsInputRedirected;
                report["captureSkippedHidden"]=JsonValue.CreateBooleanValue(!visible);
                if(visible)
                {
                    _onexScreenProfilePicker.Root.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);
                    var capture=SaveOnexImage("onex-screen-profile-picker-168.png",_onexScreenProfilePicker.Root,0);
                    bool completed=await Task.WhenAny(capture,Task.Delay(3000))==capture;
                    report["captureTimedOut"]=JsonValue.CreateBooleanValue(!completed);
                    if(completed)await capture;
                }

            }
            finally
            {
                foreach(var menu in _onexOpenMenus.ToArray())menu.Hide();
                if(!hostStopped)try{Progress("cleanup: stop process");await Phase("stop-process");}catch{}
                foreach(string id in created)
                {
                    Progress("cleanup: read catalog for owned profile "+created.IndexOf(id));
                    await ReadOnexScreenProfilesAsync();
                    if(_onexScreenProfiles.GetNamedArray("entries").Select(item=>item.GetObject()).Any(item=>item.GetNamedString("id")==id))
                        await MutateOnexScreenProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(id)}});
                }
                Progress("cleanup: select initial profile");await MutateOnexScreenProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("select")},{"id",JsonValue.CreateStringValue(initialId)}});await ReadOnexScreenProfilesAsync();
                Check(_onexScreenProfiles.GetNamedArray("entries").Stringify()==initialEntries,"Existing profile values/entries were changed by QA");
                Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==powerBefore.GetNamedString("rawLimits"),"Profile QA changed TDP");
                report["originalEntriesAndPowerRestored"]=JsonValue.CreateBooleanValue(true);
            }
        }
    }
}
