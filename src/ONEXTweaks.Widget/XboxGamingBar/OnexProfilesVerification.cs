// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexProfilesAsync()
        {
            var report=new JsonObject();var originalGlobal=JsonObject.Parse(OnexProfileByName("global").Stringify());var originalFlags=JsonObject.Parse(_onexProfilesState.GetNamedObject("flags").Stringify());bool originalUse=PerGameProfileToggle.IsOn;var originalSort=ProfileSortComboBox.SelectedItem;
            async Task Wait(){for(int n=0;n<150&&_onexProfilesBusy;n++)await Task.Delay(50);}
            try
            {
                _releasePowerNav.IsChecked=true;await Task.Delay(250);
                report["startup"]=new JsonObject{{"pageReady",JsonValue.CreateBooleanValue(_onexProfilesPageReady)},{"hasGame",JsonValue.CreateBooleanValue(OnexHasGame)},{"visible",JsonValue.CreateStringValue(PerformanceScrollViewer.Visibility.ToString())},{"toggleEnabled",JsonValue.CreateBooleanValue(PerGameProfileToggle.IsEnabled)},{"globalRows",JsonValue.CreateNumberValue(GlobalProfilePairs.Children.Count)},{"renderError",JsonValue.CreateStringValue(_onexProfilesRenderError??"")},{"state",_onexProfilesState}};
                if(!_onexProfilesPageReady||!OnexHasGame||PerformanceScrollViewer.Visibility!=Visibility.Visible||!PerGameProfileToggle.IsEnabled||GlobalProfilePairs.Children.Count==0)throw new InvalidOperationException("Profile tab/card not connected");
                if(!MoveOnexProfileFocus(PerGameProfileToggle,true)||Windows.UI.Xaml.Input.FocusManager.GetFocusedElement()!=FPSStateCycleButton||!MoveOnexProfileFocus(_onexProfileGlobalEdit,true)||Windows.UI.Xaml.Input.FocusManager.GetFocusedElement()!=_onexProfileGameEdit||!MoveOnexProfileFocus(PerfSavedProfilesExpandButton,false)||Windows.UI.Xaml.Input.FocusManager.GetFocusedElement()!=_onexProfileGameEdit)throw new InvalidOperationException("Profile focus crosses into another tab");
                if(DetectedGameText.Text!=currentGameName||currentGameName.Length<2)throw new InvalidOperationException("Detected game caption missing");
                await SaveOnexImage("onex-profiles-active.png");
                PerGameProfileToggle.IsOn=false;await Wait();if(_onexProfilesState.GetNamedString("activeId")!="global")throw new InvalidOperationException("Per-game toggle did not apply global");
                bool split=PowerSourceProfileToggle.IsOn;PowerSourceProfileToggle.IsOn=!split;await Wait();if(OnexProfileByName("global").GetNamedBoolean("split")==split)throw new InvalidOperationException("Split toggle did not reach backend");PowerSourceProfileToggle.IsOn=split;await Wait();
                bool fps=ProfileSaveFPSLimitCheckBox.IsChecked==true;ProfileSaveFPSLimitCheckBox.IsChecked=!fps;await Wait();if(_onexProfilesState.GetNamedObject("flags").GetNamedBoolean("FPSLimit")==fps)throw new InvalidOperationException("Save flag did not reach backend");ProfileSaveFPSLimitCheckBox.IsChecked=fps;await Wait();
                PerfSavedProfilesExpandButton.IsChecked=true;PerfSavedProfilesExpandButton_Click(PerfSavedProfilesExpandButton,null);await Task.Delay(200);if(_profileGroupExpanders.Count<1)throw new InvalidOperationException("Saved profile groups missing");foreach(var group in _profileGroupExpanders)group.IsExpanded=true;
                foreach(var item in ProfileSortComboBox.Items.OfType<ComboBoxItem>()){ProfileSortComboBox.SelectedItem=item;await Task.Delay(50);if(AllGameProfilesContainer.Children.Count<1)throw new InvalidOperationException("Profile sort removed entries");}
                foreach(var group in _profileGroupExpanders)group.IsExpanded=true;PerformanceScrollViewer.ChangeView(null,GameScrollViewer.ScrollableHeight*.45,null,true);await Task.Delay(200);await SaveOnexImage("onex-profiles-saved.png");
                var editing=ShowOnexProfileEditorAsync(OnexProfileByName("global"),qsTileMap["Profile"].TileButton);await Task.Delay(300);if(_onexDetailsDialog==null)throw new InvalidOperationException("Profile editor missing");await SaveOnexImage("onex-profile-editor.png",_onexDetailsDialog);var primary=FindOnexSettingsNamedControl<Slider>(_onexDetailsDialog,"OnexProfilePl1");var secondary=FindOnexSettingsNamedControl<Slider>(_onexDetailsDialog,"OnexProfilePl2");primary.Value=18;secondary.Value=20;
                var apply=FindOnexSettingsControls<Button>(_onexDetailsDialog).First(b=>b.Content as string=="Apply");((Windows.UI.Xaml.Automation.Provider.IInvokeProvider)new Windows.UI.Xaml.Automation.Peers.ButtonAutomationPeer(apply).GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();await editing;
                var applied=OnexProfileByName("global").GetNamedObject("ac");if(applied.GetNamedNumber("pl1")!=18||applied.GetNamedNumber("pl2")!=20)throw new InvalidOperationException("Profile editor Apply did not save slider values");var actual=await ReadOnexStateAsync();if(actual.GetNamedNumber("pl1Watts")!=18||actual.GetNamedNumber("pl2Watts")!=20)throw new InvalidOperationException("Profile editor Apply did not reach hardware");report["editorApplyVerified"]=JsonValue.CreateBooleanValue(true);
                if(ProfileSaveResolutionCheckBox.IsEnabled||ProfileSaveAMDFeaturesCheckBox.IsEnabled)throw new InvalidOperationException("Unsupported hardware fields enabled");
                report["passed"]=JsonValue.CreateBooleanValue(true);report["checks"]=new JsonArray{JsonValue.CreateStringValue("original cards retained in Performance tab"),JsonValue.CreateStringValue("per-game toggle"),JsonValue.CreateStringValue("AC/DC split"),JsonValue.CreateStringValue("save flags"),JsonValue.CreateStringValue("saved groups and all sort modes"),JsonValue.CreateStringValue("editor Apply reaches hardware"),JsonValue.CreateStringValue("unsupported fields disabled")};
            }
            catch(Exception ex){report["passed"]=JsonValue.CreateBooleanValue(false);report["error"]=JsonValue.CreateStringValue(ex.ToString());}
            finally
            {
                CloseOnexMenus();ProfileSortComboBox.SelectedItem=originalSort;await MutateOnexProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("flags")},{"flags",originalFlags}});
                await MutateOnexProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("edit")},{"id",originalGlobal["id"]},{"split",originalGlobal["split"]},{"ac",originalGlobal["ac"]},{"dc",originalGlobal["dc"]}});await SetOnexProfileUseAsync(originalUse);
                var folder=ApplicationData.Current.LocalFolder;await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-profiles-verification.json",CreationCollisionOption.ReplaceExisting),report.Stringify());var armed=await folder.TryGetItemAsync("onex-focus-verification-armed.json");if(armed!=null)await armed.DeleteAsync();
            }
        }
    }
}
