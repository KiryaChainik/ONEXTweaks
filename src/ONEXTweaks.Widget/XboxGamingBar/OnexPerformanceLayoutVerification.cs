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
using Windows.UI.Xaml.Input;
using Windows.Graphics.Display;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexPerformanceLayoutAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");int step=0;
            async Task<JsonObject> WindowsPower(){var reply=await App.SendMessageAsync(new ValueSet{{"OnexWindowsPowerGet",true}});return JsonObject.Parse((string)reply["OnexWindowsPowerState"]);}
            async Task<JsonObject> Profiles(){var reply=await App.SendMessageAsync(new ValueSet{{"OnexProfilesGet",true}});var value=JsonObject.Parse((string)reply["OnexProfilesState"]);ApplyOnexProfilesState(value.Stringify());return value;}
            var powerBefore=await ReadOnexStateAsync();var windowsBefore=await WindowsPower();var profilesBefore=await Profiles();if(profilesBefore.GetNamedString("activeId")!="global")throw new InvalidOperationException("Performance QA requires Global active");var globalBefore=JsonObject.Parse(profilesBefore.GetNamedArray("entries").Select(v=>v.GetObject()).Single(v=>v.GetNamedString("id")=="global").Stringify());
            void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            bool Within(DependencyObject root,DependencyObject element){return ContainsOnexProfileElement(root,element);}
            string Choices(DependencyObject root)=>string.Join("|",FindOnexSettingsControls<ComboBox>(root).Where(c=>c.Name.StartsWith("OnexCpu")).Select(c=>c.Name+":"+string.Join(",",c.Items.Cast<object>().Select(v=>v.ToString()))));
            void Trace(string stage){Logger.Info("[PerformanceQA] "+stage);System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-performance-qa-progress.txt"),stage);}
            int Rank(DependencyObject control)=>control==PerGameProfileToggle?0:control==TDPSlider?1:control==TDPBoostToggle||control==TDPBoostFPPTSliderCard?2:control==OSPowerModeComboBox?3:control==CPUBoostToggle||control==CpuCardExpandButton?4:control==FPSLimitSlider||control==FPSModeToggle||control==FPSStateCycleButton?5:-1;
            async Task Input(string key,Func<bool> expected)
            {
                string id=run+":"+(++step);var rect=new RadioButtonAutomationPeer(_releasePowerNav).GetBoundingRectangle();double scale=DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;var packet=new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2+widget.WindowBounds.X*scale)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2+widget.WindowBounds.Y*scale)}};
                for(int n=0;;n++){try{System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-gamepad-qa-state.json"),packet.Stringify());break;}catch(System.IO.IOException){if(n>=20)throw;await Task.Delay(25);}}
                Trace("waiting "+key+" step "+step);bool ok=false;for(int n=0;n<60;n++){await Task.Delay(100);string ack=null;try{ack=await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"));}catch{}if(ack==id){Trace("ack "+key+" step "+step);if(expected()){ok=true;Trace("expected matched "+key+" step "+step);break;}}}if(!ok)throw new InvalidOperationException("Performance navigation failed: "+key+" step "+step);

            }
            try
            {
                _releasePowerNav.IsChecked=true;await Task.Delay(250);PerformanceScrollViewer.ChangeView(null,0,null,true);UpdateLayout();var body=(Panel)PerformanceScrollViewer.Content;
                if(!_onexPerformanceLayoutReady||_onexPerformancePriority.Length!=7)throw new InvalidOperationException("Performance priority layout missing");var order=new JsonArray();double prior=-1;
                foreach(var card in _onexPerformancePriority){int index=body.Children.IndexOf(card);var bounds=card.TransformToVisual(body).TransformBounds(new Windows.Foundation.Rect(0,0,card.ActualWidth,card.ActualHeight));if(index!=order.Count||bounds.Top<prior||bounds.Height<=0)throw new InvalidOperationException("Performance card order/visibility wrong at "+index);prior=bounds.Bottom;order.Add(JsonValue.CreateStringValue(index==0?"Per Game Performance":index==1?"PL1":index==2?"PL2":index==3?"OS Power Mode":index==4?"CPU Boost":index==5?"FPS":"Fan Curve"));}
                await SaveOnexImage("onex-performance-priority-top-private.png",PerformanceScrollViewer,0);await Input("focus",()=>true);TDPSlider.IsFocusEngaged=false;if(!TDPSlider.Focus(FocusState.Keyboard))throw new InvalidOperationException("Performance PL1 not focusable");
                int previous=1;for(int n=0;n<8&&previous<5;n++){var old=FocusManager.GetFocusedElement();await Input("down",()=>!ReferenceEquals(old,FocusManager.GetFocusedElement()));var focused=FocusManager.GetFocusedElement() as DependencyObject;Trace("reading focus rank");int index=Rank(focused);Trace("focus rank="+index);if(index<previous||index<0)throw new InvalidOperationException("D-pad did not follow the visual priority order");previous=index;}if(previous!=5)throw new InvalidOperationException("Performance navigation never reached FPS");
                if((await ReadOnexStateAsync()).GetNamedString("rawLimits")!=powerBefore.GetNamedString("rawLimits"))throw new InvalidOperationException("D-pad navigation changed power instead of focus");
                var help=OnexLogicalElements<Button>(_onexPerformancePriority[3]).Single(b=>b.Content as string=="?");help.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});Invoke(help);await Task.Delay(150);var explanation=_onexOpenMenus.OfType<Windows.UI.Xaml.Controls.Flyout>().FirstOrDefault(f=>f.Content is TextBlock text&&text.Text.Contains("Пример"));if(explanation==null)throw new InvalidOperationException("OS Power Mode explanation/example not opened");CloseOnexMenus();
                CpuCardExpandButton.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});CpuCardExpandButton.Focus(FocusState.Keyboard);await Input("a",()=>_cpuCardExpanded);
                for(int n=0;n<80&&(_onexPerformanceCpuLoading||_onexPerformanceCpuForm==null);n++)await Task.Delay(100);await Task.Delay(250);UpdateLayout();if(_onexPerformanceCpuForm==null||CpuSectionContent.Visibility!=Visibility.Visible)throw new InvalidOperationException("Full inline CPU settings did not expand");
                var inline=_onexPerformanceCpuForm.Panel;string inlineChoices=Choices(inline);if(FindOnexSettingsControls<ComboBox>(inline).Count(c=>c.Name.StartsWith("OnexCpu"))!=4)throw new InvalidOperationException("AC/DC CPU policy selectors missing");int helpCount=FindOnexSettingsControls<Button>(inline).Count(b=>b.Content as string=="?");if(helpCount<5)throw new InvalidOperationException("Inline CPU explanations missing");
                CpuCardExpandButton.Focus(FocusState.Keyboard);await Input("down",()=>_onexPerformanceCpuForm.NavigationControls.Any(control=>control!=null&&control==FocusManager.GetFocusedElement()));await SaveOnexImage("onex-performance-cpu-expanded-private.png",PerformanceScrollViewer,0);
                Invoke(_onexPerformanceCpuApply);for(int n=0;n<100&&_onexPerformanceCpuApplying;n++)await Task.Delay(100);var status=FindOnexSettingsNamedControl<TextBlock>(CpuSectionContent,"OnexPerformanceCpuStatus");if(status.Text!="Saved.")throw new InvalidOperationException("Inline CPU apply failed: "+status.Text);
                var actualCpu=(await WindowsPower()).GetNamedObject("cpu");foreach(string key in new[]{"boostAc","boostDc","schedulingAc","schedulingDc"})if(actualCpu.GetNamedNumber(key)!=windowsBefore.GetNamedObject("cpu").GetNamedNumber(key))throw new InvalidOperationException("Same-value CPU apply changed "+key);
                await ToggleOnexPerformanceCpuAsync();PerformanceNavItem.IsChecked=true;var tile=qsTileMap["CPUBoost"];var popup=ShowOnexTileDetailsAsync(tile.TileButton,tile.Id);for(int n=0;n<60&&_onexDetailsDialog==null;n++)await Task.Delay(100);await Task.Delay(300);if(_onexDetailsDialog==null||Choices(_onexDetailsDialog)!=inlineChoices)throw new InvalidOperationException("Tile and inline CPU policy choices differ");CloseOnexMenus();await popup;
                report["visibleCardOrder"]=order;report["dpadFollowsVisibleOrder"]=JsonValue.CreateBooleanValue(true);report["osPowerModeHelpHasExample"]=JsonValue.CreateBooleanValue(true);report["cpuInlineAcDcSelectors"]=JsonValue.CreateNumberValue(4);report["cpuInlineHelpCount"]=JsonValue.CreateNumberValue(helpCount);report["cpuTileAndInlineChoicesIdentical"]=JsonValue.CreateBooleanValue(true);report["actualSameValueCpuApplyConfirmed"]=JsonValue.CreateBooleanValue(true);report["actualControllerActions"]=JsonValue.CreateNumberValue(step);report["gameContextPresent"]=JsonValue.CreateBooleanValue(OnexHasGame);report["fanCurveEditingImplemented"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                try
                {
                    CloseOnexMenus();if(_cpuCardExpanded&&!_onexPerformanceCpuLoading&&!_onexPerformanceCpuApplying)await ToggleOnexPerformanceCpuAsync();
                    var current=(await WindowsPower()).GetNamedObject("cpu");foreach(string key in new[]{"boostAc","boostDc","schedulingAc","schedulingDc","shortSchedulingAc","shortSchedulingDc"})current[key]=windowsBefore.GetNamedObject("cpu")[key];var restored=await SendOnexHomeRequestAsync(new ValueSet{{"OnexCpuSettings",current.Stringify()}});ApplyOnexWindowsPowerState((string)restored["OnexWindowsPowerState"]);
                    await MutateOnexProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("edit")},{"id",globalBefore["id"]},{"split",globalBefore["split"]},{"ac",globalBefore["ac"]},{"dc",globalBefore["dc"]}});
                    var final=await ReadOnexStateAsync();if(final.GetNamedString("rawLimits")!=powerBefore.GetNamedString("rawLimits"))throw new InvalidOperationException("Original power not restored");report["originalWindowsPoliciesProfilesAndPowerRestored"]=JsonValue.CreateBooleanValue(true);
                }
                finally{System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-gamepad-qa-state.json"),"{\"finished\":true}");}
            }
        }
    }
}
