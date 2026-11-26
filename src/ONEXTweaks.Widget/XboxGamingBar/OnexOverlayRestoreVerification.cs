// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Input;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexOverlayRestoredAsync(JsonObject report)
        {
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            async Task Ready()
            {
                for(int n=0;n<100;n++)
                {
                    if(!_onexOverlayOpening&&_onexOverlayConfigReady&&isOSDCustomizeExpanded)
                    {await RefreshOnexOverlayPreviewAsync();if(_onexOverlayPreviewRenderedConfig==BuildOSDConfigString(true)&&_onexOverlayPreviewModel!=null)return;}
                    await Task.Delay(80);
                }
                throw new TimeoutException("Overlay editor/preview did not become ready");
            }
            var before=await App.SendMessageAsync(new ValueSet{{"OnexOverlayGet",true}});
            string preferences=(string)before["OnexOverlayPreferences"];
            _releaseOsdNav.IsChecked=true;await OpenOnexOverlaySettingsAsync();await Ready();
            Check(ReferenceEquals(OnexOverlaySettingsCard.Parent,OnexOverlayBody),"Overlay card is not in its own page");
            Check(!ContainsOnexProfileElement(OnexPerfLegacyControls,OnexOverlaySettingsCard),"Overlay card remains inside collapsed legacy controls");
            Check(OnexOverlaySettingsCard.Visibility==Visibility.Visible&&OSDCustomizeContent.Visibility==Visibility.Visible,"Detailed editor remains hidden");
            Check(PerformanceOverlayComboBox.IsEnabled&&PerformanceOverlayComboBox.ActualHeight>0,"Overlay mode control is inaccessible");
            Check(OSDTextSizeComboBox.IsEnabled&&OSDFontComboBox.IsEnabled&&OSDOpacitySlider.IsEnabled,"Overlay formatting controls are disabled");
            Check(_onexOverlayPreviewText.Blocks.Count==1&&_onexOverlayPreviewText.ActualHeight>0,"Preview did not render its text");
            FocusFirstTabContentElement();Check(ReferenceEquals(FocusManager.GetFocusedElement(),PerformanceOverlayComboBox),"Overlay first focus does not reach mode selector");
            await SaveOnexImage("onex-overlay-restored-212.png",_releaseOsd,1200);
            await SaveOnexImage("onex-overlay-restored-preview-212.png",_onexOverlayPreviewCard,600);
            PerformanceNavItem.IsChecked=true;_releaseOsdNav.IsChecked=true;await OpenOnexOverlaySettingsAsync();await Ready();
            var after=await App.SendMessageAsync(new ValueSet{{"OnexOverlayGet",true}});
            Check((string)after["OnexOverlayPreferences"]==preferences,"Opening/reopening settings changed saved overlay preferences");
            report["detailsVisibleOnOwnTab"]=JsonValue.CreateBooleanValue(true);
            report["actualFormattingControlsAvailable"]=JsonValue.CreateBooleanValue(true);
            report["previewRenderedFromCurrentConfiguration"]=JsonValue.CreateBooleanValue(true);
            report["reopeningPreservesPreferences"]=JsonValue.CreateBooleanValue(true);
            report["firstFocusCorrect"]=JsonValue.CreateBooleanValue(true);
            report["previewUsesExampleData"]=JsonValue.CreateBooleanValue(true);
            report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
            report["inGameRtssHookRetested"]=JsonValue.CreateBooleanValue(false);
        }
    }
}
