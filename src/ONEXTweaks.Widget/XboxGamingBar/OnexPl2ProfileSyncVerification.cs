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
using Windows.UI.Xaml.Automation.Provider;
using Windows.Graphics.Display;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexPl2ProfileSyncAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;var prefs=ApplicationData.Current.LocalSettings.Values;var home=(ToggleSwitch)_releaseHome.FindName("LightPl2Toggle");string run=Guid.NewGuid().ToString("N");int step=0;
            async Task<JsonObject> Profiles(){var reply=await App.SendMessageAsync(new ValueSet{{"OnexProfilesGet",true}});var value=JsonObject.Parse((string)reply["OnexProfilesState"]);ApplyOnexProfilesState(value.Stringify());return value;}
            JsonObject Known=null;for(int n=0;n<100;n++){Known=await ReadOnexStateAsync();if(Known.TryGetValue("separatePl2",out var mode)&&mode.ValueType==JsonValueType.Boolean)break;await Task.Delay(100);}
            if(!Known.TryGetValue("separatePl2",out var initialMode)||initialMode.ValueType!=JsonValueType.Boolean)throw new InvalidOperationException("Existing PL2 selector was not migrated");
            var before=Known;var originalProfiles=await Profiles();if(originalProfiles.GetNamedString("activeId")!="global")throw new InvalidOperationException("Global profile verification requires no active game");
            var originalGlobal=JsonObject.Parse(originalProfiles.GetNamedArray("entries").Select(v=>v.GetObject()).Single(v=>v.GetNamedString("id")=="global").Stringify());
            bool originalOn=before.GetNamedBoolean("separatePl2");int originalManual=(int)before.GetNamedNumber("rememberedPl2Watts");int pl1=Math.Min(35,(int)before.GetNamedNumber("pl1Watts")),manual=Math.Min(41,Math.Max(pl1+5,originalManual));
            async Task Input(string key,Func<bool> expected)
            {
                string id=run+":"+(++step);var rect=new RadioButtonAutomationPeer(PerformanceNavItem).GetBoundingRectangle();double scale=DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
                var packet=new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2+widget.WindowBounds.X*scale)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2+widget.WindowBounds.Y*scale)}};
                for(int n=0;;n++){try{System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-gamepad-qa-state.json"),packet.Stringify());break;}catch(System.IO.IOException){if(n>=20)throw;await Task.Delay(25);}}
                bool ok=false;for(int n=0;n<70;n++){await Task.Delay(100);try{if(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&expected()){ok=true;break;}}catch{}}if(!ok)throw new InvalidOperationException("PL2 sync controller input failed: "+key+" step "+step);
            }
            async Task WaitMode(bool expected)
            {
                for(int n=0;n<100;n++)
                {
                    await Task.Delay(100);var actual=await ReadOnexStateAsync();var profiles=await Profiles();var global=profiles.GetNamedArray("entries").Select(v=>v.GetObject()).Single(v=>v.GetNamedString("id")=="global");var side=global.GetNamedObject(profiles.GetNamedBoolean("onAc")?"ac":"dc");
                    if(actual.GetNamedNumber("pl1Watts")==pl1&&actual.GetNamedNumber("pl2Watts")== (expected?manual:pl1+1)&&actual.GetNamedBoolean("separatePl2")==expected&&actual.GetNamedNumber("rememberedPl2Watts")==manual&&home.IsOn==expected&&_releaseHome.RememberedPl2Watts==manual&&TDPBoostToggle.IsOn==expected&&OnexProfileSeparatePl2(side)==expected&&side.GetNamedNumber("pl2")==manual&&home.IsEnabled)
                    {string caption=OnexProfilePairs(global,profiles.GetNamedBoolean("onAc")).First(p=>p.Label=="PL2").Value;if(!expected&&!caption.StartsWith("Off"))throw new InvalidOperationException("Profile card still shows PL2 On");return;}
                }
                var state=await ReadOnexStateAsync();throw new InvalidOperationException("PL2 state did not synchronize: expected="+expected+" home="+home.IsOn+" performance="+TDPBoostToggle.IsOn+" actual="+state.Stringify());
            }
            try
            {
                PerformanceNavItem.IsChecked=true;await Input("focus",()=>true);home.IsOn=true;await SetOnexReleasePowerAsync(pl1,manual);await WaitMode(true);home.Focus(FocusState.Keyboard);await Input("a",()=>!home.IsOn);await WaitMode(false);report["homeOffUpdatesPerformanceAndProfile"]=JsonValue.CreateBooleanValue(true);
                _releasePowerNav.IsChecked=true;TDPBoostToggle.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(150);if(!TDPBoostToggle.Focus(FocusState.Keyboard))throw new InvalidOperationException("Performance PL2 toggle not focusable");await Input("a",()=>TDPBoostToggle.IsOn);await WaitMode(true);
                TDPBoostToggle.Focus(FocusState.Keyboard);await Input("a",()=>!TDPBoostToggle.IsOn);await WaitMode(false);report["performanceOnOffUpdatesHomeAndProfile"]=JsonValue.CreateBooleanValue(true);
                var editing=ShowOnexProfileEditorAsync(OnexProfileByName("global"),_onexProfileGlobalEdit);for(int n=0;n<50&&_onexDetailsDialog==null;n++)await Task.Delay(100);var dialog=_onexDetailsDialog;if(dialog==null)throw new InvalidOperationException("Profile editor not open");await Task.Delay(300);dialog.UpdateLayout();
                var sideChoice=FindOnexSettingsControls<ComboBox>(dialog).First(c=>c.Header as string=="Editing power state");if(originalGlobal.GetNamedBoolean("split")&&!_onexProfilesState.GetNamedBoolean("onAc"))sideChoice.SelectedIndex=1;
                var separate=FindOnexSettingsNamedControl<ToggleSwitch>(dialog,"OnexProfileSeparatePl2");var pl2=FindOnexSettingsNamedControl<Slider>(dialog,"OnexProfilePl2");if(separate.IsOn||pl2.IsEnabled||pl2.Value!=manual)throw new InvalidOperationException("Profile editor did not load Off with remembered manual PL2");
                separate.IsOn=true;var apply=FindOnexSettingsControls<Button>(dialog).First(b=>b.Content as string=="Apply");((IInvokeProvider)new ButtonAutomationPeer(apply).GetPattern(PatternInterface.Invoke)).Invoke();await editing;await WaitMode(true);
                report["homeOffUpdatesPerformanceAndProfile"]=JsonValue.CreateBooleanValue(true);report["performanceOnOffUpdatesHomeAndProfile"]=JsonValue.CreateBooleanValue(true);report["profileEditorAppliesSelectorAndRetainsManual"]=JsonValue.CreateBooleanValue(true);report["existingModeMigrated"]=JsonValue.CreateBooleanValue(true);report["actualVirtualControllerActions"]=JsonValue.CreateNumberValue(step);report["physicalControllerTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                try
                {
                    CloseOnexMenus();await MutateOnexProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("edit")},{"id",originalGlobal["id"]},{"split",originalGlobal["split"]},{"ac",originalGlobal["ac"]},{"dc",originalGlobal["dc"]}});
                    prefs["OnexSeparatePl2"]=originalOn;prefs["OnexRememberedPl2Watts"]=originalManual;_releaseHome.ReloadPowerPreferences();await SetOnexReleasePowerAsync((int)before.GetNamedNumber("pl1Watts"),(int)before.GetNamedNumber("pl2Watts"));
                    var restored=await ReadOnexStateAsync();if(restored.GetNamedString("rawLimits")!=before.GetNamedString("rawLimits")||restored.GetNamedBoolean("separatePl2")!=originalOn||restored.GetNamedNumber("rememberedPl2Watts")!=originalManual)throw new InvalidOperationException("Original PL2 state not restored");
                    var after=await Profiles();var global=after.GetNamedArray("entries").Select(v=>v.GetObject()).Single(v=>v.GetNamedString("id")=="global");if(global.GetNamedBoolean("split")!=originalGlobal.GetNamedBoolean("split")||global.GetNamedObject("ac").Stringify()!=originalGlobal.GetNamedObject("ac").Stringify()||global.GetNamedObject("dc").Stringify()!=originalGlobal.GetNamedObject("dc").Stringify())throw new InvalidOperationException("Original AC/DC profile values not restored");
                    report["originalHardwareModeManualAndAcDcRestored"]=JsonValue.CreateBooleanValue(true);
                }
                finally{System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-gamepad-qa-state.json"),"{\"finished\":true}");}
            }
        }
    }
}
