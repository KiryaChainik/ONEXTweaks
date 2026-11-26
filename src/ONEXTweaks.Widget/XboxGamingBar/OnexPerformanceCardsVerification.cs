// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexPerformanceCardsAsync(JsonObject report)
        {
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            async Task Wait(Func<bool> test,string message){for(int n=0;n<150;n++){if(test())return;await Task.Delay(100);}throw new TimeoutException(message);}
            _releasePowerNav.IsChecked=true;EnsureOnexPerformanceCards();await ReadOnexPerformanceProfilesAsync();await RefreshOnexDisplayAsync(false);await ReadOnexScreenProfilesAsync();RefreshOnexPerformanceCards();
            string[] expected={"tdp","cpu","mode","fps","fan","screen"};
            Check(_onexPerfSections.Select(section=>section.Id).SequenceEqual(expected),"Card order differs from owner order");
            for(int n=0;n<_onexPerfSections.Length;n++)
            {
                var section=_onexPerfSections[n];Check(Grid.GetRow(section.Header)==n/2*2&&Grid.GetColumn(section.Header)==n%2,"Card grid location mismatch");
                Check(Grid.GetRow(section.Detail)==n/2*2+1&&Grid.GetColumnSpan(section.Detail)==2,"Expanded section does not span its full row");
            }
            Check(PerformanceScrollViewer.Content is Panel body&&body.Children.IndexOf(_onexPerformanceProfilePickerCard)<body.Children.IndexOf(OnexPerfCardGrid),"Profile workflow is below the card grid");
            await SetOnexPerfCardAsync(null);PerformanceScrollViewer.ChangeView(null,0,null,true);await Task.Delay(120);
            await SaveOnexImage("onex-performance-grid-197.png",OnexPerfCardGrid,0);
            var sections=new JsonArray();report["sections"]=sections;
            try
            {
                foreach(var section in _onexPerfSections)
                {
                    section.Header.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);
                    ((IInvokeProvider)new ButtonAutomationPeer(section.Header).GetPattern(PatternInterface.Invoke)).Invoke();
                    await Wait(()=>!_onexPerfChangingCard&&_onexPerfOpen==section.Id&&section.Detail.Visibility==Visibility.Visible,"Section did not open: "+section.Id);
                    await Task.Delay(180);
                    Check(_onexPerfSections.Count(item=>item.Detail.Visibility==Visibility.Visible)==1,"Multiple expanded sections overlap");
                    if(section.Id=="cpu")Check(_onexPerformanceCpuForm!=null,"CPU form failed to load: "+string.Join(" / ",CpuSectionContent.Children.OfType<TextBlock>().Select(text=>text.Text)));
                    if(section.Id=="tdp")Check(TDPValueText.Text==((int)TDPSlider.Value)+"W","PL1 caption disagrees with the slider value");
                    Check(OnexPerfSectionControls(section.Id).Any(control=>control!=section.Close),"Expanded section has no available settings: "+section.Id);
                    var header=section.Header.TransformToVisual(OnexPerfCardGrid).TransformBounds(new Windows.Foundation.Rect(0,0,section.Header.ActualWidth,section.Header.ActualHeight));
                    var detail=section.Detail.TransformToVisual(OnexPerfCardGrid).TransformBounds(new Windows.Foundation.Rect(0,0,section.Detail.ActualWidth,section.Detail.ActualHeight));
                    Check(detail.Top>=header.Bottom-1&&Math.Abs(detail.Width-OnexPerfCardGrid.ActualWidth)<2,"Expanded panel bounds are misplaced");
                    sections.Add(new JsonObject{{"id",JsonValue.CreateStringValue(section.Id)},{"availableInputs",JsonValue.CreateNumberValue(OnexPerfSectionControls(section.Id).Count-1)},{"fullWidth",JsonValue.CreateBooleanValue(true)}});
                    if(section.Id=="tdp")await SaveOnexImage("onex-performance-tdp-197.png",OnexPerfCardGrid,0);
                    ((IInvokeProvider)new ButtonAutomationPeer(section.Close).GetPattern(PatternInterface.Invoke)).Invoke();await Wait(()=>!_onexPerfChangingCard&&_onexPerfOpen==null,"Section did not close");
                }
                report["cardOrderAndRows"]=JsonValue.CreateBooleanValue(true);report["nativeExpandClose"]=JsonValue.CreateBooleanValue(true);report["profileWorkflowAboveGrid"]=JsonValue.CreateBooleanValue(true);report["tdpCaptionMatchesSlider"]=JsonValue.CreateBooleanValue(true);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally{await SetOnexPerfCardAsync(null);}
        }
    }
}
