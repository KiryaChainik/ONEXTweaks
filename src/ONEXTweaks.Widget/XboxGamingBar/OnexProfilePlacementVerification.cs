// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexProfilePlacementAsync(JsonObject report)
        {
            if(!OnexHasGame)throw new InvalidOperationException("Dedicated D3D11 game fixture not detected");
            var originalGlobal=JsonObject.Parse(OnexProfileByName("global").Stringify());var originalFlags=JsonObject.Parse(_onexProfilesState.GetNamedObject("flags").Stringify());string gamePath=_onexProfilesState.GetNamedObject("currentGame").GetNamedString("path");
            var existing=OnexProfiles.FirstOrDefault(p=>p.GetNamedString("path","")==gamePath);var originalGame=existing==null?null:JsonObject.Parse(existing.Stringify());string created=null;
            try
            {
                _releasePowerNav.IsChecked=true;await Task.Delay(250);PerformanceScrollViewer.ChangeView(null,0,null,true);
                foreach(string name in new[]{"ActiveProfileCard","GlobalProfilePairs","GameProfileCard","AllGameProfilesContainer","PowerSourceProfileCard","ProfileSettingsContent"})
                {var card=FindName(name) as DependencyObject;if(!ContainsOnexProfileElement(PerformanceScrollViewer,card)||ContainsOnexProfileElement(GameScrollViewer,card))throw new InvalidOperationException("Profile card outside Performance: "+name);}
                if(PerformanceScrollViewer.Visibility!=Visibility.Visible||_onexLegacyPowerGate.Visibility!=Visibility.Visible||GlobalProfilePairs.Children.Count==0||!PerGameProfileToggle.IsEnabled)throw new InvalidOperationException("Performance profile controls not ready");
                await SetOnexProfileUseAsync(true);if(OnexActiveProfile.GetNamedString("id")=="global"||GameProfileCard.Visibility!=Visibility.Visible||GameProfilePairs.Children.Count==0)throw new InvalidOperationException("Game profile not selected/rendered in Performance");created=OnexActiveProfile.GetNamedString("id");
                await SaveOnexImage("onex-performance-profiles-top.png",PerformanceScrollViewer);
                if(!MoveOnexProfileFocus(PerGameProfileToggle,true)||Windows.UI.Xaml.Input.FocusManager.GetFocusedElement()!=FPSStateCycleButton)throw new InvalidOperationException("Per-game focus skips Performance controls");
                var active=JsonObject.Parse(OnexActiveProfile.Stringify());var side=active.GetNamedObject(_onexProfilesState.GetNamedBoolean("onAc")?"ac":"dc");int old1=(int)side.GetNamedNumber("pl1"),old2=(int)side.GetNamedNumber("pl2");int next1=old1<40?old1+1:old1-1;int next2=Math.Max(next1,Math.Min(41,old2+(next1>old1?1:-1)));
                var flags=JsonObject.Parse(originalFlags.Stringify());flags["TDP"]=JsonValue.CreateBooleanValue(true);await MutateOnexProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("flags")},{"flags",flags}});
                GameProfileCard.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(200);
                var editing=ShowOnexProfileEditorAsync(OnexActiveProfile,_onexProfileGameEdit);await Task.Delay(300);var dialog=_onexDetailsDialog;if(dialog==null)throw new InvalidOperationException("Game profile editor missing");
                var sideChoice=FindOnexSettingsControls<ComboBox>(dialog).First(c=>c.Header as string=="Editing power state");if(active.GetNamedBoolean("split")&&!_onexProfilesState.GetNamedBoolean("onAc"))sideChoice.SelectedIndex=1;
                FindOnexSettingsNamedControl<Slider>(dialog,"OnexProfilePl1").Value=next1;FindOnexSettingsNamedControl<Slider>(dialog,"OnexProfilePl2").Value=next2;
                var apply=FindOnexSettingsControls<Button>(dialog).First(c=>c.Content as string=="Apply");((Windows.UI.Xaml.Automation.Provider.IInvokeProvider)new Windows.UI.Xaml.Automation.Peers.ButtonAutomationPeer(apply).GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();await editing;
                var actual=await ReadOnexStateAsync();if(actual.GetNamedNumber("pl1Watts")!=next1||actual.GetNamedNumber("pl2Watts")!=next2)throw new InvalidOperationException("Performance profile editor did not reach hardware");
                report["appliedPl1"]=JsonValue.CreateNumberValue(next1);report["appliedPl2"]=JsonValue.CreateNumberValue(next2);
                await MutateOnexProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("edit")},{"id",active["id"]},{"split",active["split"]},{"ac",active["ac"]},{"dc",active["dc"]}});
                bool split=PowerSourceProfileToggle.IsOn;await SetOnexProfileSplitAsync(!split);if(OnexActiveProfile.GetNamedBoolean("split")==split)throw new InvalidOperationException("AC/DC control not connected");await SetOnexProfileSplitAsync(split);
                await SaveOnexImage("onex-performance-game-profile.png",GameProfileCard);await SetOnexProfileUseAsync(false);if(OnexActiveProfile.GetNamedString("id")!="global")throw new InvalidOperationException("Global profile selection failed");
                PerfSavedProfilesExpandButton.IsChecked=true;PerfSavedProfilesExpandButton_Click(PerfSavedProfilesExpandButton,null);await Task.Delay(150);if(_profileGroupExpanders.Count==0)throw new InvalidOperationException("Saved game profile not listed");
                GlobalProfilePairs.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(200);await SaveOnexImage("onex-performance-profile-cards.png",PerformanceScrollViewer);
                ProfilesNavItem.IsChecked=true;await Task.Delay(100);((Windows.UI.Xaml.Automation.Provider.IInvokeProvider)new Windows.UI.Xaml.Automation.Peers.ButtonAutomationPeer(_onexProfilesShortcut).GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();await Task.Delay(100);if(_releasePowerNav.IsChecked!=true||PerformanceScrollViewer.Visibility!=Visibility.Visible)throw new InvalidOperationException("Profiles shortcut did not return to Performance");
                report["originalCardsInPerformance"]=JsonValue.CreateBooleanValue(true);report["gameSelectedAndRendered"]=JsonValue.CreateBooleanValue(true);report["acDcConnected"]=JsonValue.CreateBooleanValue(true);report["savedGameVisible"]=JsonValue.CreateBooleanValue(true);report["shortcutConnected"]=JsonValue.CreateBooleanValue(true);
            }
            finally
            {
                CloseOnexMenus();await MutateOnexProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("flags")},{"flags",originalFlags}});
                if(created!=null)
                {
                    if(originalGame==null)await MutateOnexProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(created)}});
                    else{await MutateOnexProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("edit")},{"id",originalGame["id"]},{"split",originalGame["split"]},{"ac",originalGame["ac"]},{"dc",originalGame["dc"]}});await SetOnexProfileUseAsync(originalGame.GetNamedBoolean("use"));}
                }
                await MutateOnexProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("edit")},{"id",originalGlobal["id"]},{"split",originalGlobal["split"]},{"ac",originalGlobal["ac"]},{"dc",originalGlobal["dc"]}});
            }
        }
    }
}
