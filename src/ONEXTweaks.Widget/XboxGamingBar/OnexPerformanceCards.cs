// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private sealed class OnexPerfSection
        {
            internal string Id;internal Button Header,Close;internal Border Detail;internal TextBlock Summary;internal FontIcon Chevron;
        }
        private OnexPerfSection[] _onexPerfSections;
        private string _onexPerfOpen;
        private bool _onexPerfChangingCard;
        private bool _onexCpuContextReloading;
        private DispatcherTimer _onexPerfSummaryTimer;
        private readonly Dictionary<string,List<Control>> _onexPerfExtraControls=new Dictionary<string,List<Control>>();
        private void EnsureOnexPerformanceCards()
        {
            if(_onexPerfSections!=null)return;
            string[] ids={"Tdp","Cpu","Mode","Fps","Intel" ,"Resolution"};
            _onexPerfSections=ids.Select(key=>new OnexPerfSection{Id=key.ToLowerInvariant(),Header=(Button)FindName("OnexPerf"+key+"Card"),Close=(Button)FindName("OnexPerf"+key+"Close"),Detail=(Border)FindName("OnexPerf"+key+"Detail"),Summary=(TextBlock)FindName("OnexPerf"+key+"Summary"),Chevron=(FontIcon)FindName("OnexPerf"+key+"Chevron")}).ToArray();
            foreach(var section in _onexPerfSections){section.Header.CornerRadius=new CornerRadius(12);section.Detail.CornerRadius=new CornerRadius(12);}
            EnsureOnexDisplayWiring();
            foreach(var section in _onexPerfSections)_onexPerfExtraControls[section.Id]=new List<Control>();
            DisplayGpuTabHost.Margin=DisplayResTabHost.Margin=new Thickness(0);
            _onexPerfExtraControls["intel"].AddRange(new Control[]{IntelFrameGenerationComboBox,IntelVrrComboBox,IntelVrrModeComboBox,IntelLowLatencyComboBox,IntelFrameSyncComboBox,IntelScalingModeComboBox,IntelScalingMethodComboBox,IntelFetchStateButton});
            _onexPerfExtraControls["resolution"].AddRange(new Control[]{ResolutionComboBox,RefreshRatesComboBox});
            _onexPerfSummaryTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};
            _onexPerfSummaryTimer.Tick+=(s,e)=>{if(_releasePowerNav?.IsChecked==true)try{RefreshOnexPerformanceCards();}catch(Exception error){Logger.Warn("Performance summary: "+error.Message);}};_onexPerfSummaryTimer.Start();
            RefreshOnexPerformanceCards();
        }
        private static void SyncOnexPerfChoice(ComboBox choice,IEnumerable<Tuple<string,object>> source,object selected)
        {
            if(choice.IsDropDownOpen)return;var items=source.ToArray();
            if(!choice.Items.OfType<ComboBoxItem>().Select(item=>item.Content+"|"+item.Tag).SequenceEqual(items.Select(item=>item.Item1+"|"+item.Item2)))
            {choice.Items.Clear();foreach(var item in items)choice.Items.Add(new ComboBoxItem{Content=item.Item1,Tag=item.Item2});}
            choice.SelectedItem=choice.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>Equals(item.Tag,selected));
        }
        private void RefreshOnexPerformanceCards()
        {
            if(_onexPerfSections==null)return;
            if(_onexPerfOpen=="cpu"&&_onexPerformanceCpuForm?.AutoSave==true&&!_onexPerformanceCpuLoading&&!_onexPerformanceCpuApplying&&!_onexCpuContextReloading&&(_onexPerformanceCpuForm.ProfileId!=OnexFrontend.ProfileId||_onexPerformanceCpuForm.OnAc!=_onexProfilesState?.GetNamedBoolean("onAc",false)))
                _=ReloadOnexPerformanceCpuContextAsync();
            {
                var side=OnexActiveProfile?.GetNamedObject(_onexProfilesState?.GetNamedBoolean("onAc",false)==true?"effectiveAc":"effectiveDc",null);
                string mode=OSPowerModeComboBox.SelectedIndex>=0?OnexStrings.Get(new[]{"Efficiency","Balanced","Performance"}[Math.Min(2,OSPowerModeComboBox.SelectedIndex)]):"—";
                string[] captions={_releasePl1+" W · PL2 "+(_releaseHome?.SeparatePl2Enabled==true?_releasePl2+" W":OnexStrings.Get("Off")),OnexCpuCardBrief(side),mode,OnexFpsProvider+" · "+(OnexFpsEnabled?OnexFpsValue.ToString():OnexStrings.Get("Off")),OnexIntelBrief(),OnexResolutionBrief()};
                for(int n=0;n<_onexPerfSections.Length;n++){var section=_onexPerfSections[n];if(section.Summary.Text!=captions[n])section.Summary.Text=captions[n];section.Header.IsEnabled=App.IsConnected;}
            }
        }
        private async Task ReloadOnexPerformanceCpuContextAsync()
        {
            _onexCpuContextReloading=true;
            try{_onexPerformanceCpuForm?.Cancel?.Invoke();if(_cpuCardExpanded)await ToggleOnexPerformanceCpuAsync();if(_onexPerfOpen=="cpu"&&!_cpuCardExpanded)await ToggleOnexPerformanceCpuAsync();}
            catch(Exception error){ShowOnexDesktopStatus(error.Message);}
            finally{_onexCpuContextReloading=false;}
        }
        private async void OnexPerfCard_Click(object sender,RoutedEventArgs args)
        {try{await SetOnexPerfCardAsync((string)((Button)sender).Tag);}catch(Exception error){ShowOnexDesktopStatus(error.Message);}}
        private async void OnexPerfClose_Click(object sender,RoutedEventArgs args)
        {try{await SetOnexPerfCardAsync(null);}catch(Exception error){ShowOnexDesktopStatus(error.Message);}}
        private async Task SetOnexPerfCardAsync(string id)
        {
            if(_onexPerfChangingCard||_onexPerformanceCpuApplying)return;_onexPerfChangingCard=true;
            try
            {
                EnsureOnexPerformanceCards();string previous=_onexPerfOpen;if(id==previous)id=null;
                if(previous=="cpu"&&_cpuCardExpanded)await ToggleOnexPerformanceCpuAsync();
                _onexPerfOpen=id;
                foreach(var section in _onexPerfSections)
                {bool open=section.Id==id;section.Detail.Visibility=open?Visibility.Visible:Visibility.Collapsed;section.Chevron.Glyph=open?"\uE70E":"\uE70D";section.Header.BorderBrush=open?OnexPerformanceAccentBrush(section.Id):(Brush)Resources["CardBorderBrush"];}
                // Prepare the joined frame in this UI turn, before CPU's async state read.
                // Otherwise the visible header/editor briefly retain two closed outlines.
                ApplyOnexTabPalette();
                if(id=="cpu"&&!_cpuCardExpanded)await ToggleOnexPerformanceCpuAsync();
                ApplyOnexPageFocus();
                var active=_onexPerfSections.FirstOrDefault(section=>section.Id==id);
                if(active!=null){var transition=new Storyboard();var fade=new DoubleAnimation{From=.25,To=1,Duration=TimeSpan.FromMilliseconds(140)};Storyboard.SetTarget(fade,active.Detail);Storyboard.SetTargetProperty(fade,"Opacity");transition.Children.Add(fade);transition.Begin();active.Header.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});}
                else{var closed=_onexPerfSections.FirstOrDefault(section=>section.Id==previous);if(closed!=null)_=Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,()=>{if(_onexPerfOpen==null)closed.Header.Focus(FocusState.Keyboard);});}
                RefreshOnexPerformanceCards();ApplyOnexTabPalette();
            }
            finally{_onexPerfChangingCard=false;}
        }
        private List<Control> OnexPerfSectionControls(string id)
        {
            var controls=new List<Control>();
            if(id=="tdp"){controls.Add(TDPSlider);controls.Add(TDPBoostToggle);if(TDPBoostSliderArea.Visibility==Visibility.Visible)controls.Add(TDPBoostFPPTSliderCard);}
            else if(id=="cpu"){controls.Add(CPUBoostToggle);if(_onexPerformanceCpuForm!=null)controls.AddRange(_onexPerformanceCpuForm.NavigationControls);controls.Add(_onexPerformanceCpuCancel);controls.Add(_onexPerformanceCpuApply);}
            else if(id=="mode")controls.Add(OSPowerModeComboBox);
            else if(id=="fps"){controls.Add(FPSLimitSlider);controls.Add(FPSModeToggle);controls.Add(FPSStateCycleButton);}
            else if(id!=null)controls.AddRange(_onexPerfExtraControls[id]);
            if(id!=null)controls.Add(_onexPerfSections.Single(section=>section.Id==id).Close);
            return controls.Where(control=>control!=null&&control.IsEnabled&&control.IsTabStop&&control.Visibility==Visibility.Visible).ToList();
        }
        // Game Bar may translate physical B to Escape. Only the header of the
        // currently open card owns that translated form; other controls retain
        // their existing Escape behavior and slider/popup cancellation priority.
        private static bool IsOnexPerformanceBackKey(VirtualKey key,bool onOpenHeader)
            => key==VirtualKey.GamepadB || key==VirtualKey.Escape&&onOpenHeader;
        private bool HandleOnexPerformanceCardsKey(KeyRoutedEventArgs args)
        {
            if(HandleOnexSourceKey(args))return true;
            if(args.Handled||_onexPerfSections==null||_releasePowerNav?.IsChecked!=true||_onexDetailsDialog!=null||_onexOpenMenus.Any(menu=>menu.IsOpen))return false;
            var focus=FocusManager.GetFocusedElement();if(focus is ComboBox combo&&combo.IsDropDownOpen)return false;
            if(_onexPerfOpen!=null&&IsOnexPerformanceBackKey(args.Key,_onexPerfSections.Any(section=>section.Id==_onexPerfOpen&&ReferenceEquals(section.Header,focus)))){args.Handled=true;_=SetOnexPerfCardAsync(null);return true;}
            if(!IsOnexProfileVertical(args.Key))return false;int direction=IsOnexProfileDown(args.Key)?1:-1;
            int header=Array.FindIndex(_onexPerfSections,section=>ReferenceEquals(section.Header,focus));Control target=null;
            if(header>=0)
            {
                int row=header/2,open=Array.FindIndex(_onexPerfSections,section=>section.Id==_onexPerfOpen),openRow=open<0?-2:open/2;
                if((direction>0&&openRow==row)||(direction<0&&openRow==row-1)){var inputs=OnexPerfSectionControls(_onexPerfOpen);target=direction>0?inputs.FirstOrDefault():inputs.LastOrDefault();}
                if(target==null){int nextRow=row+direction;int next=nextRow*2+header%2;target=nextRow>=0&&nextRow<=(_onexPerfSections.Length-1)/2?_onexPerfSections[Math.Min(next,_onexPerfSections.Length-1)].Header:direction>0?(Control)PerfSavedProfilesExpandButton:_onexPerformanceProfilePicker?.Remove?.IsEnabled==true?_onexPerformanceProfilePicker.Remove:_onexPerformanceProfilePicker?.Choose;}
            }
            else if(_onexPerfOpen!=null)
            {
                var inputs=OnexPerfSectionControls(_onexPerfOpen);int index=inputs.FindIndex(control=>ReferenceEquals(control,focus));
                if(index>=0){int next=index+direction;var section=_onexPerfSections.Single(value=>value.Id==_onexPerfOpen);int sectionIndex=Array.IndexOf(_onexPerfSections,section);target=next>=0&&next<inputs.Count?inputs[next]:direction<0?section.Header:sectionIndex/2<(_onexPerfSections.Length-1)/2?_onexPerfSections[Math.Min(sectionIndex+2,_onexPerfSections.Length-1)].Header:(Control)PerfSavedProfilesExpandButton;}
            }
            if(_onexPerfOpen=="cpu"&&_onexPerformanceCpuForm?.NavigationRows!=null)
            {
                var rows=_onexPerformanceCpuForm.NavigationRows;
                for(int row=0;row<rows.Length;row++){int column=Array.FindIndex(rows[row],control=>ReferenceEquals(control,focus));if(column<0)continue;int next=row+direction;target=next<0?CPUBoostToggle:next>=rows.Length?OnexPerfCpuClose:rows[next][Math.Min(column,rows[next].Length-1)];break;}
            }
            if(focus is Control profileControl)
            {var profileTarget=OnexProfileNext(_onexPerformanceProfilePicker,profileControl,direction>0,_onexDcEditor?.Root.Visibility==Visibility.Visible?_onexDcEditor.Cards[0].Header:OnexPerfTdpCard);if(profileTarget!=null)target=profileTarget;}
            if(ReferenceEquals(focus,_onexPerformanceProfilePicker?.Split))target=direction<0?(Control)ProfileSettingsExpandToggle:_onexPerformanceProfilePicker.Split;
            if(direction<0&&ReferenceEquals(focus,PerfSavedProfilesExpandButton))
            {var openIndex=System.Array.FindIndex(_onexPerfSections,section=>section.Id==_onexPerfOpen);target=openIndex>=((_onexPerfSections.Length-1)/2)*2?OnexPerfSectionControls(_onexPerfOpen).LastOrDefault():_onexPerfSections[((_onexPerfSections.Length-1)/2)*2].Header;}
            if(target==null)return false;args.Handled=true;var selected=target;_=Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,()=>{if(selected.IsEnabled){selected.Focus(FocusState.Keyboard);selected.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});}});return true;
        }
    }
}
