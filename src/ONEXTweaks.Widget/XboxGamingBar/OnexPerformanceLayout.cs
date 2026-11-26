// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private bool _onexPerformanceLayoutReady,_onexPerformanceCpuLoading,_onexPerformanceCpuApplying;
        private FrameworkElement[] _onexPerformancePriority;
        private OnexCpuSettingsForm _onexPerformanceCpuForm;
        private Button _onexPerformanceCpuApply,_onexPerformanceCpuCancel;
        private const string OnexPowerModeHelp="Выбирает, чему Windows отдаёт приоритет: автономности или производительности.\n\nBest power efficiency — экономия энергии. Balanced — баланс быстродействия и автономности. Best performance — приоритет быстродействия; расход энергии и нагрев обычно выше. Результат зависит от игры, процессора и других системных ограничений.\n\nСтрока меняет режим для текущего питания: зарядки или батареи. Выбранные PL1/PL2 в ONEXTweaks сохраняются.\n\nПример: в лёгкой игре на батарее при PL1=11 Вт сравни Efficiency и Balanced. В игре с большой нагрузкой на CPU от зарядки сравни Balanced и Performance с теми же PL1/PL2 и посмотри на FPS и расход батареи.";
        private static IEnumerable<T> OnexLogicalElements<T>(DependencyObject root) where T:DependencyObject
        {if(root is T item)yield return item;foreach(var child in OnexProfileLogicalChildren(root))foreach(var nested in OnexLogicalElements<T>(child))yield return nested;}
        private void EnsureOnexPerformanceLayout()
        {
            if(_onexPerformanceLayoutReady||!(PerformanceScrollViewer.Content is StackPanel body))return;
            var pl1=OnexPerfTdpBaseCard;var mode=OnexPerfModeBaseCard;var cpu=OnexPerfCpuBaseCard;var fps=OnexPerfFpsBaseCard;
            EnsureOnexPerformanceCards();
            var pl2=OnexPerfPl2Card;
            var caption=OnexPl2HelpText;if(caption!=null)caption.Text=global::XboxGamingBar.OnexStrings.Get("Off: PL2 = PL1 + 1 W. At PL1 40 W, manual PL2 goes up to 52 W.");
            if(mode.Child is Grid modeRow)
            {
                var previous=OnexPowerModeHeading;modeRow.Children.Remove(previous);var heading=OnexHelpHeader(global::XboxGamingBar.OnexStrings.Get("OS Power Mode"),OnexPowerModeHelp);var headingText=((Panel)heading).Children.OfType<TextBlock>().First();headingText.FontSize=18;headingText.FontWeight=FontWeights.Bold;heading.VerticalAlignment=VerticalAlignment.Center;modeRow.Children.Add(heading);
            }
            CpuSectionContent.Children.Clear();CpuCardCaptionText.Text=global::XboxGamingBar.OnexStrings.Get("Expand for boost policies, core preference and frequency limit · plugged in / battery.");
            _onexPerformancePriority=new FrameworkElement[]{ActiveProfileCard,pl1,pl2,mode,cpu,fps,LegionFanCurveCard};
            // Cards are already in their final order in XAML. Reparenting templated
            // input controls at runtime can invalidate UWP's native focus references.
            _onexPerformanceLayoutReady=true;
        }
        private async Task ToggleOnexPerformanceCpuAsync()
        {
            if(_onexPerformanceCpuLoading||_onexPerformanceCpuApplying)return;_cpuCardExpanded=!_cpuCardExpanded;CpuCardExpandButton.IsChecked=_cpuCardExpanded;CpuCardExpandIcon.Glyph=_cpuCardExpanded?"\uE70E":"\uE70D";CpuSectionContent.Visibility=_cpuCardExpanded?Visibility.Visible:Visibility.Collapsed;
            if(!_cpuCardExpanded){if(_onexPerformanceCpuForm?.Pending?.Invoke()==true)await _onexPerformanceCpuForm.Apply();_onexPerformanceCpuForm?.Cancel?.Invoke();CpuSectionContent.Children.Clear();_onexPerformanceCpuForm=null;return;}
            _onexPerformanceCpuLoading=true;CpuCardExpandButton.IsEnabled=false;CpuSectionContent.Children.Clear();var status=new TextBlock{Name="OnexPerformanceCpuStatus",Text=global::XboxGamingBar.OnexStrings.Get("Loading CPU settings…"),TextWrapping=TextWrapping.Wrap,FontSize=12};CpuSectionContent.Children.Add(status);
            try
            {
                var form=await CreateOnexCpuAdvancedFormAsync();_onexPerformanceCpuForm=form;CpuSectionContent.Children.Clear();var formHost=new ContentControl{Content=form.Panel,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Top,IsTabStop=false};CpuSectionContent.Children.Add(formHost);
                if(form.AutoSave){_onexPerformanceCpuApply=null;_onexPerformanceCpuCancel=null;return;}
                status.Text=global::XboxGamingBar.OnexStrings.Get("Changes apply when you press Apply.");var cancel=new Button{Content=global::XboxGamingBar.OnexStrings.Get("Cancel"),Style=OnexSettingsButtonStyle(false)};_onexPerformanceCpuCancel=cancel;_onexPerformanceCpuApply=new Button{Name="OnexPerformanceCpuApply",Content=global::XboxGamingBar.OnexStrings.Get("Apply"),Style=OnexSettingsButtonStyle(true)};var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right};buttons.Children.Add(cancel);buttons.Children.Add(_onexPerformanceCpuApply);CpuSectionContent.Children.Add(status);CpuSectionContent.Children.Add(buttons);
                var apply=_onexPerformanceCpuApply;cancel.Click+=async(s,e)=>await ToggleOnexPerformanceCpuAsync();apply.Click+=async(s,e)=>{_onexPerformanceCpuApplying=true;apply.IsEnabled=false;cancel.IsEnabled=false;formHost.IsEnabled=false;ApplyOnexAvailability();status.Text=global::XboxGamingBar.OnexStrings.Get("Applying…");try{await form.Apply();status.Text=global::XboxGamingBar.OnexStrings.Get("Saved.");}catch(Exception error){status.Text=error.Message;}finally{_onexPerformanceCpuApplying=false;formHost.IsEnabled=true;cancel.IsEnabled=true;apply.IsEnabled=App.IsConnected;ApplyOnexAvailability();}};
            }
            catch(Exception error){status.Text=error.Message;}
            finally{_onexPerformanceCpuLoading=false;CpuCardExpandButton.IsEnabled=App.IsConnected&&OnexWindowsPowerReady;}
        }
        private bool HandleOnexPerformanceVertical(KeyRoutedEventArgs args)
        {
            if(_onexPerfSections!=null)return HandleOnexPerformanceCardsKey(args);
            if(args.Handled||!OnexFrontend.UsesOwnBackend||_releasePowerNav?.IsChecked!=true||!_onexPerformanceLayoutReady||!IsOnexProfileVertical(args.Key)||_onexDetailsDialog!=null||_onexOpenMenus.Any(menu=>menu.IsOpen))return false;
            // Only our named input controls participate. Never enumerate private XAML templates
            // from a routed input callback: some UWP template objects are transient during focus.
            var controls=new List<Control>{PerGameProfileToggle,TDPSlider,TDPBoostToggle};
            if(TDPBoostSliderArea.Visibility==Visibility.Visible)controls.Add(TDPBoostFPPTSliderCard);
            controls.Add(OSPowerModeComboBox);controls.Add(CPUBoostToggle);controls.Add(CpuCardExpandButton);
            if(_cpuCardExpanded&&_onexPerformanceCpuForm!=null)
            {controls.AddRange(_onexPerformanceCpuForm.NavigationControls);controls.Add(_onexPerformanceCpuCancel);controls.Add(_onexPerformanceCpuApply);}
            controls.Add(FPSLimitSlider);controls.Add(FPSModeToggle);controls.Add(FPSStateCycleButton);
            if(_onexPerformanceProfilePicker!=null){controls.Add(_onexPerformanceProfilePicker.Choose);controls.Add(_onexPerformanceProfilePicker.Create);controls.Add(_onexPerformanceProfilePicker.Remove);controls.Add(_onexPerformanceProfilePicker.Split);}
            var focus=FocusManager.GetFocusedElement();var current=controls.FirstOrDefault(control=>control!=null&&control==focus);
            if(current==null||current is Slider slider&&slider.IsFocusEngaged||current is ComboBox combo&&combo.IsDropDownOpen)return false;
            if(App.OnexTileRevisionStage==45)Logger.Info("[PerformanceNav] source="+current.Name+" key="+args.Key);
            int index=controls.IndexOf(current),direction=IsOnexProfileDown(args.Key)?1:-1;
            for(int nextIndex=index+direction;nextIndex>=0&&nextIndex<controls.Count;nextIndex+=direction)
            {
                var next=controls[nextIndex];if(next==null||!next.IsEnabled||!next.IsTabStop||next.Visibility!=Visibility.Visible||next.ActualHeight<=0)continue;
                if(App.OnexTileRevisionStage==45)Logger.Info("[PerformanceNav] target="+next.Name);
                args.Handled=true;
                // Finish the key route before changing focus. UWP can invalidate native
                // focus state when a ToggleSwitch is focused inside a Slider's preview route.
                _=Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,()=>
                {
                    if(_releasePowerNav?.IsChecked!=true||!next.IsEnabled)return;
                    bool moved=next.Focus(FocusState.Keyboard);
                    if(App.OnexTileRevisionStage==45)Logger.Info("[PerformanceNav] focused="+next.Name+" result="+moved);
                    // PerformanceScrollViewer already brings focused controls into view.
                    // Avoid a redundant native scroll request while focus is settling.
                });
                return true;
            }
            return false;
        }
    }
}
