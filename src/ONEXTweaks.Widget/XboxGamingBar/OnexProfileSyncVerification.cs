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
        private async Task VerifyOnexProfileSyncUiAsync(JsonObject report)
        {
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            await ReadOnexPerformanceProfilesAsync();await ReadOnexFanProfilesAsync();await ReadOnexScreenProfilesAsync();await RefreshOnexDisplayAsync(false);RefreshOnexProfileSummaries();
            _releasePowerNav.IsChecked=true;await Task.Delay(300);PerformanceScrollViewer.ChangeView(null,0,null,true);
            Check(_onexPerfSections.Select(section=>section.Id).SequenceEqual(new[]{"tdp","cpu","mode","fps","intel" ,"resolution"}),"Performance is not the requested six-card layout");
            Check(OnexPerfIntelCard.Visibility==Visibility.Visible&&OnexPerfResolutionCard.Visibility==Visibility.Visible&&OnexPerfScreenCard.Visibility==Visibility.Collapsed,"Performance controls were not moved correctly");
            Check(ContainsOnexProfileElement(OnexPerfIntelContent,DisplayGpuTabHost)&&ContainsOnexProfileElement(OnexPerfResolutionContent,DisplayResTabHost),"Actual Intel/mode editors are absent from Performance");
            Check(!ContainsOnexProfileElement(DisplayMainStack,DisplayGpuTabHost)&&!ContainsOnexProfileElement(DisplayMainStack,DisplayResTabHost),"Moved controls are duplicated on Screen Color");
            Check(_onexScreenProfilePicker.Split.Visibility==Visibility.Collapsed&&!ContainsOnexProfileElement(DisplayMainStack,_onexScreenProfilePicker.Footer),"Screen AC/DC footer is still present");
            var summary=(TextBlock)_onexPerformanceProfilePicker.Root.Children[2];Check(summary.Text.Contains("PL1")&&!summary.Text.Contains("Fan:")&&!summary.Text.Contains("Screen:"),"Performance profile brief is missing useful values");
            Check(OnexPerfResolutionSummary.Text.Contains("Hz"),"Selected preset cards lack name/settings summaries");
            await SaveOnexImage("onex-profile-sync-performance-210.png",PerformanceScrollViewer,900);
            await SetOnexPerfCardAsync("intel");await Task.Delay(250);Check(OnexPerfIntelDetail.Visibility==Visibility.Visible&&IntelFetchStateButton.IsEnabled,"Intel editor does not open with working controls");
            await SaveOnexImage("onex-profile-sync-intel-210.png",OnexPerfIntelDetail,900);await SetOnexPerfCardAsync(null);
            await SetOnexPerfCardAsync("resolution");await Task.Delay(250);Check(OnexPerfResolutionDetail.Visibility==Visibility.Visible&&ResolutionComboBox.IsEnabled&&RefreshRatesComboBox.IsEnabled,"Resolution editor is unavailable");
            await SaveOnexImage("onex-profile-sync-resolution-210.png",OnexPerfResolutionDetail,500);await SetOnexPerfCardAsync(null);
            DisplayNavItem.IsChecked=true;DisplayScrollViewer.ChangeView(null,0,null,true);await Task.Delay(250);
            var displaySummary=(TextBlock)_onexScreenProfilePicker.Root.Children[2];Check(displaySummary.Text.Contains("HDR")&&displaySummary.Text.Contains("Gamma"),"Display profile brief lacks color/HDR values");
            Check(_onexIntelSettingsSummary!=null&&_onexIntelSettingsSummary.Text!="Ожидание данных Intel"&&!string.IsNullOrWhiteSpace(_onexIntelSettingsSummary.Text),"Intel active settings summary is empty");
            Check(ContainsOnexProfileElement(DisplayGpuTabHost,_onexIntelSettingsSummary),"Intel summary is not in the Display Intel settings block");
            await SaveOnexImage("onex-profile-sync-display-210.png",DisplayScrollViewer,900);
            FanNavItem.IsChecked=true;FanScrollViewer.ChangeView(null,0,null,true);await Task.Delay(200);var fanSummary=(TextBlock)_onexFanProfilePicker.Root.Children[2];Check(!string.IsNullOrWhiteSpace(fanSummary.Text),"Fan profile brief is empty");
            await SaveOnexImage("onex-profile-sync-fan-210.png",FanScrollViewer,900);_releasePowerNav.IsChecked=true;
            report["sixCardsAndNoScreenSelector"]=JsonValue.CreateBooleanValue(true);report["fanSelectorIndependent"]=JsonValue.CreateBooleanValue(true);report["profileAndCardSummariesPresent"]=JsonValue.CreateBooleanValue(true);report["intelActualSummaryInPerformance"]=JsonValue.CreateBooleanValue(true);report["readOnlyUiVerification"]=JsonValue.CreateBooleanValue(true);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
        }
    }
}
