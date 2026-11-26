// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexPerformanceProfilesAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");int sequence=0;
            void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);}
            void Progress(string text){report["phase"]=JsonValue.CreateStringValue(text);System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-performance-profile-qa-progress.json"),text);}
            async Task Wait(Func<bool> check,string text){for(int n=0;n<120;n++){if(check())return;await Task.Delay(100);}throw new TimeoutException(text);}
            async Task Idle(){await Wait(()=>_onexPerformanceProfilePicker.Choose.IsEnabled&&OnexPerformancePendingDetails().Length==0,"Profile action did not settle: "+OnexPerformancePendingDetails());}
            string Active()=>_onexProfilesState?.GetNamedString("activeId","global")??"global";
            JsonObject Entry(string id)=>_onexProfilesState.GetNamedArray("entries").Select(v=>v.GetObject()).Single(v=>v.GetNamedString("id")==id);
            JsonObject Side(string id)=>Entry(id).GetNamedObject(_onexProfilesState.GetNamedBoolean("onAc")?"ac":"dc");
            async Task Phase(string phase)
            {
                string id=run+":"+(++sequence);System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-performance-profile-qa-state.json"),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"phase",JsonValue.CreateStringValue(phase)}}.Stringify());
                for(int n=0;n<240;n++){await Task.Delay(100);try{var ack=JsonObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder.Path,"onex-performance-profile-qa-ack.json")));if(ack.GetNamedString("id")==id){Check(ack.GetNamedBoolean("passed"),"Host phase failed: "+phase);await ReadOnexPerformanceProfilesAsync();return;}}catch(System.IO.IOException){}}
                throw new TimeoutException("Host did not finish "+phase);
            }
            async Task<MenuFlyout> Popup(){await Wait(()=>_onexOpenMenus.OfType<MenuFlyout>().Any(menu=>menu.IsOpen),"Profile menu did not open: "+_onexPerformanceProfilePicker.DiagnosticError);return _onexOpenMenus.OfType<MenuFlyout>().Last(menu=>menu.IsOpen);}
            async Task Invoke(MenuFlyoutItem item){((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();await Task.Delay(100);await Idle();}
            async Task Choose(string id){await _onexPerformanceProfilePicker.OpenChooseAsync();await Invoke((await Popup()).Items.OfType<MenuFlyoutItem>().Single(item=>item.Tag as string==id));await Wait(()=>Active()==id,"Choose did not select the profile");}
            async Task Remove(string action)
            {((IInvokeProvider)new ButtonAutomationPeer(_onexPerformanceProfilePicker.Remove).GetPattern(PatternInterface.Invoke)).Invoke();await Task.Delay(100);await Invoke((await Popup()).Items.OfType<MenuFlyoutItem>().Single(item=>item.Text==action));await Wait(()=>Active()=="global",action+" did not return Global");}
            async Task Power(int pl1)
            {
                string id=Active();bool separate=_releaseHome.SeparatePl2Enabled;int manual=_releaseHome.RememberedPl2Watts;
                await SetOnexReleasePowerAsync(pl1,separate?Math.Max(pl1,manual):Math.Min(41,pl1+1));await ReadOnexPerformanceProfilesAsync();var actual=await ReadOnexStateAsync();
                Check(Active()==id&&actual.GetNamedNumber("pl1Watts")==pl1&&Side(id).GetNamedNumber("pl1")==pl1,"Actual TDP and selected profile did not converge");
            }
            Progress("initial read");var powerBefore=await ReadOnexStateAsync();_releasePowerNav.IsChecked=true;await ReadOnexPerformanceProfilesAsync();
            var initial=JsonObject.Parse(_onexProfilesState.Stringify());string initialId=Active(),initialEntries=initial.GetNamedArray("entries").Stringify();var created=new List<string>();bool hostStopped=false;
            void SaveOwned(){var ids=new JsonArray();foreach(string id in created)ids.Add(JsonValue.CreateStringValue(id));System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-performance-profile-qa-owned.json"),new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"initial",initial},{"power",powerBefore},{"created",ids}}.Stringify());}
            SaveOwned();
            Check(initialId=="global"&&!initial.GetNamedBoolean("manualOverride",false),"Native profile QA requires Global without an owner override");
            int original=(int)powerBefore.GetNamedNumber("pl1Watts"),manualTarget=original>9?original-1:original==9?8:9,appTarget=original>10?original-2:10;
            try
            {
                Progress("create Manual through menu");await _onexPerformanceProfilePicker.OpenCreateAsync();var first=(await Popup()).Items.OfType<MenuFlyoutItem>().First();Check(first.Tag as string=="manual","Manual must be the first Create entry");await Invoke(first);await Wait(()=>Active()!="global","Manual was not created");string manualId=Active();created.Add(manualId);SaveOwned();
                await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("split")},{"enabled",JsonValue.CreateBooleanValue(true)}});
                Check(_onexPerformanceProfilePicker.Split.IsOn,"AC/DC selector did not reflect committed split");string inactive=Entry(manualId).GetNamedObject(initial.GetNamedBoolean("onAc")?"dc":"ac").Stringify();
                await Power(manualTarget);Check(Entry(manualId).GetNamedObject(initial.GetNamedBoolean("onAc")?"dc":"ac").Stringify()==inactive,"Saving current power side changed the other side");
                Progress("create process profile through menu");await _onexPerformanceProfilePicker.OpenCreateAsync();var process=(await Popup()).Items.OfType<MenuFlyoutItem>().FirstOrDefault(item=>item.Text.StartsWith("ONEXProfileProbe"));Check(process!=null,"Own real process was absent from Create list");await Invoke(process);await Wait(()=>Active()!=manualId&&Active()!="global","Process profile not created/selected");string appId=Active();created.Add(appId);SaveOwned();await Power(appTarget);
                Check(Entry(appId).GetNamedObject("ac").GetNamedNumber("pl1")==appTarget&&Entry(appId).GetNamedObject("dc").GetNamedNumber("pl1")==appTarget,"Unified profile did not save both power sides");
                var beforeStale=await ReadOnexStateAsync();var stale=await App.SendMessageAsync(new ValueSet{{"Command",1},{"Function",(int)Function.TDP},{"Content",Math.Min(40,appTarget+1)},{"OnexUserInput",true},{"OnexProfileId",manualId}});
                Check(stale.ContainsKey("Error")&&(await ReadOnexStateAsync()).GetNamedString("rawLimits")==beforeStale.GetNamedString("rawLimits"),"Stale profile write reached the hardware");
                Progress("manual override through Choose");await Choose(manualId);Check((await ReadOnexStateAsync()).GetNamedNumber("pl1Watts")==manualTarget,"Manual profile was selected without applying actual TDP");
                await Phase("same-session");Check(Active()==manualId&&(await ReadOnexStateAsync()).GetNamedNumber("pl1Watts")==manualTarget,"Normal focus/overlay cycle replaced the manual choice");
                Progress("helper restart");await Phase("restart-bridge");await Wait(()=>App.IsConnected,"Widget did not reconnect");await ReadOnexPerformanceProfilesAsync();Check(Active()==manualId&&(await ReadOnexStateAsync()).GetNamedNumber("pl1Watts")==manualTarget,"Helper restart lost the manual override");
                Progress("real process restart");await Phase("restart-process");Check(Active()==appId&&(await ReadOnexStateAsync()).GetNamedNumber("pl1Watts")==appTarget,"Real process restart did not apply its saved profile");
                await _onexPerformanceProfilePicker.OpenChooseAsync();var choices=(await Popup()).Items.OfType<MenuFlyoutItem>().Where(item=>item.Tag is string).ToArray();Check(choices[0].Tag as string=="global","Global is not first in Choose");Check(Array.FindIndex(choices,item=>item.Tag as string==manualId)<Array.FindIndex(choices,item=>item.Tag as string==appId),"Manual group must precede Per-app");foreach(var menu in _onexOpenMenus.ToArray())menu.Hide();
                _onexPerformanceProfilePicker.Root.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);if(Windows.Gaming.UI.GameBar.IsInputRedirected){var capture=SaveOnexImage("onex-performance-profile-picker-197.png",_onexPerformanceProfilePicker.Root,0);if(await Task.WhenAny(capture,Task.Delay(3000))==capture)await capture;}
                Progress("Keep It");await Remove("Keep It");Check(_onexProfilesState.GetNamedArray("entries").Select(v=>v.GetObject()).Any(v=>v.GetNamedString("id")==appId),"Keep It deleted its profile");
                await Phase("stop-process");hostStopped=true;await Choose(manualId);await Remove("Delete");Check(!_onexProfilesState.GetNamedArray("entries").Select(v=>v.GetObject()).Any(v=>v.GetNamedString("id")==manualId),"Delete left its profile in the catalog");
                report["nativeCreateChooseKeepDelete"]=JsonValue.CreateBooleanValue(true);report["actualRaplProfileValues"]=JsonValue.CreateBooleanValue(true);report["separateAcDcAutosave"]=JsonValue.CreateBooleanValue(true);report["physicalAcDcSwitchTested"]=JsonValue.CreateBooleanValue(false);report["staleProfileWriteRejected"]=JsonValue.CreateBooleanValue(true);report["sameProcessManualOverride"]=JsonValue.CreateBooleanValue(true);report["helperRestartPreservesOverride"]=JsonValue.CreateBooleanValue(true);report["realProcessRestartAutoSelection"]=JsonValue.CreateBooleanValue(true);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                report["pendingAtCleanup"]=JsonValue.CreateStringValue(OnexPerformancePendingDetails());foreach(var menu in _onexOpenMenus.ToArray())menu.Hide();if(!hostStopped)try{await Phase("stop-process");}catch{}await Idle();
                foreach(string id in created){await ReadOnexPerformanceProfilesAsync();if(_onexProfilesState.GetNamedArray("entries").Select(v=>v.GetObject()).Any(v=>v.GetNamedString("id")==id))await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(id)}});}
                await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue(initial.GetNamedString("selection","auto")=="auto"?"automatic":"select")},{"id",JsonValue.CreateStringValue(initialId)}});await ReadOnexPerformanceProfilesAsync();
                Check(_onexProfilesState.GetNamedArray("entries").Stringify()==initialEntries,"Original profiles were changed by QA");
                var after=await ReadOnexStateAsync();Check(after.GetNamedString("rawLimits")==powerBefore.GetNamedString("rawLimits")&&after.GetNamedBoolean("separatePl2")==powerBefore.GetNamedBoolean("separatePl2")&&after.GetNamedNumber("rememberedPl2Watts")==powerBefore.GetNamedNumber("rememberedPl2Watts"),"Original TDP/PL2 selection did not return");report["originalProfilesPowerRestored"]=JsonValue.CreateBooleanValue(true);
            }
        }
    }
}
