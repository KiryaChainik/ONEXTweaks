// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private JsonObject _onexEnergySaverState;
        private ToggleSwitch _onexEnergySaverToggle;
        private TextBlock _onexEnergySaverStatus;
        private bool _onexEnergySaverBusy,_onexEnergySaverLoading;
        private bool _onexBatteryConfirmOpening;
        private Button _onexBatteryStatButton;
        private long _onexEnergySaverCommands;
        private bool OnexEnergySaverKnown=>_onexEnergySaverState?.GetNamedBoolean("available",false)==true;
        private bool OnexEnergySaverOn=>_onexEnergySaverState?.GetNamedBoolean("on",false)==true;
        private void ApplyOnexEnergySaverState(string json)
        {
            if(JsonObject.TryParse(json,out var state))_onexEnergySaverState=state;
            UpdateOnexEnergySaverControl();
            UpdateOnexEnergySaverIndicators();
        }
        private void UpdateOnexEnergySaverIndicators()
        {
            bool on=OnexEnergySaverKnown&&OnexEnergySaverOn;
            double? battery=currentMetricsData.TryGetValue("batteryLevel",out var percent)?percent:(double?)null;
            if(_onexStatsValues.TryGetValue("batteryLevel",out var value))OnexBatteryCapIndicator.Apply(value,battery,App.IsConnected&&_onexChargeKnown,_onexChargeAcknowledgedPercent,on,_onexAppliedTheme?.TextPrimary,_onexAppliedTheme!=null&&OnexLight(_onexAppliedTheme));
            if(_onexBatteryStatButton!=null)
            {
                _onexBatteryStatButton.IsEnabled=App.IsConnected&&OnexEnergySaverKnown&&!_onexEnergySaverBusy&&!_onexBatteryConfirmOpening;
                string action=on?global::XboxGamingBar.OnexStrings.Get("Выключить режим энергосбережения"):global::XboxGamingBar.OnexStrings.Get("Включить режим энергосбережения");
                ToolTipService.SetToolTip(_onexBatteryStatButton,action);
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(_onexBatteryStatButton,global::XboxGamingBar.OnexStrings.Format("Батарея · {0} · {1}",(value?.Text??"—"),action));
            }
            UpdateChargeLimitBatteryVisual();
            if(qsTileMap.TryGetValue("ChargeLimiter",out var tile)&&tile.IconElement!=null)
                tile.IconElement.Opacity=on?1:_onexChargeKnown&&_onexChargeOn?1:.5;
        }
        private void UpdateOnexEnergySaverControl()
        {
            if(_onexEnergySaverToggle==null)return;
            bool ready=App.IsConnected&&_onexEnergySaverState?.GetNamedBoolean("available",false)==true;
            _onexEnergySaverLoading=true;
            try{if(!_onexEnergySaverBusy)_onexEnergySaverToggle.IsOn=OnexEnergySaverOn;_onexEnergySaverToggle.IsEnabled=ready&&!_onexEnergySaverBusy;}
            finally{_onexEnergySaverLoading=false;}
            if(_onexEnergySaverStatus!=null)_onexEnergySaverStatus.Text=_onexEnergySaverBusy?global::XboxGamingBar.OnexStrings.Get("Применение…"):!ready?global::XboxGamingBar.OnexStrings.Get("Состояние Windows недоступно"):(OnexEnergySaverOn?global::XboxGamingBar.OnexStrings.Get("Включено"):global::XboxGamingBar.OnexStrings.Get("Выключено"))+(_onexEnergySaverState.GetNamedBoolean("onAc",false)?global::XboxGamingBar.OnexStrings.Get(" · питание от зарядки"):global::XboxGamingBar.OnexStrings.Get(" · питание от батареи"));
        }
        private async Task QueryOnexEnergySaverAsync()
        {
            try{var reply=await App.SendMessageAsync(new ValueSet{{"OnexEnergySaverGet",true}});if(reply!=null&&reply.TryGetValue("OnexEnergySaverState",out var json))ApplyOnexEnergySaverState((string)json);}
            catch{_onexEnergySaverState=null;UpdateOnexEnergySaverControl();UpdateOnexEnergySaverIndicators();}
        }
        private async Task<bool> SetOnexEnergySaverAsync(bool on)
        {
            if(_onexEnergySaverBusy)return false;_onexEnergySaverBusy=true;UpdateOnexEnergySaverControl();UpdateOnexEnergySaverIndicators();string error=null;
            try{_onexEnergySaverCommands++;var reply=await SendOnexHomeRequestAsync(new ValueSet{{"OnexEnergySaverSet",on}});ApplyOnexEnergySaverState((string)reply["OnexEnergySaverState"]);}
            catch(Exception failure){error=failure.Message;await QueryOnexEnergySaverAsync();}
            finally{_onexEnergySaverBusy=false;UpdateOnexEnergySaverControl();UpdateOnexEnergySaverIndicators();}
            if(error!=null){if(_onexEnergySaverStatus!=null)_onexEnergySaverStatus.Text=error;ShowOnexDesktopStatus(error);}
            return error==null;
        }
        private async Task ShowOnexBatterySaverConfirmationAsync()
        {
            if(_onexDetailsDialog!=null||_onexBatteryConfirmOpening||_onexEnergySaverBusy)return;
            _onexBatteryConfirmOpening=true;UpdateOnexEnergySaverIndicators();
            ContentDialog dialog=null;
            try
            {
                await QueryOnexEnergySaverAsync();if(!OnexEnergySaverKnown){ShowOnexDesktopStatus(global::XboxGamingBar.OnexStrings.Get("Состояние энергосбережения Windows недоступно"));return;}
                bool target=!OnexEnergySaverOn;
                dialog=CreateOnexSettingsDialog(global::XboxGamingBar.OnexStrings.Get("Режим энергосбережения"),null,new TextBlock{Text=target?global::XboxGamingBar.OnexStrings.Get("Включить режим энергосбережения?"):global::XboxGamingBar.OnexStrings.Get("Выключить режим энергосбережения?"),FontSize=18,TextWrapping=TextWrapping.Wrap});
                dialog.PrimaryButtonText=global::XboxGamingBar.OnexStrings.Get("Да");dialog.CloseButtonText=global::XboxGamingBar.OnexStrings.Get("Нет");_onexDetailsDialog=dialog;
                dialog.PrimaryButtonClick+=async(s,e)=>
                {var deferral=e.GetDeferral();try{e.Cancel=!await SetOnexEnergySaverAsync(target);}finally{deferral.Complete();}};
                await dialog.ShowAsync();
            }
            finally
            {
                if(ReferenceEquals(_onexDetailsDialog,dialog))_onexDetailsDialog=null;
                _onexBatteryConfirmOpening=false;UpdateOnexEnergySaverIndicators();
            }
        }
        private async Task ShowOnexChargeDetailsAsync(Button anchor)
        {
            await QueryOnexChargeAsync();await QueryOnexEnergySaverAsync();
            var panel=new StackPanel{Spacing=14};
            var enabled=new ToggleSwitch{IsOn=_onexChargeOn,IsEnabled=_onexChargeKnown};
            ApplyOnexTileWindowControlAccent(enabled);
            panel.Children.Add(OnexSettingsInlineToggle(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Charge limit")},enabled));
            var value=new TextBlock{Text=_onexChargePercent+"%",FontSize=36,FontWeight=Windows.UI.Text.FontWeights.SemiBold,Foreground=OnexDialogAccent,HorizontalAlignment=HorizontalAlignment.Center};
            var slider=new Slider{Minimum=30,Maximum=100,StepFrequency=5,SmallChange=5,LargeChange=10,SnapsTo=SliderSnapsTo.StepValues,Value=_onexChargePercent,IsThumbToolTipEnabled=true,Header=global::XboxGamingBar.OnexStrings.Get("Maximum charge"),IsEnabled=_onexChargeKnown};
            foreach(string suffix in new[]{"","PointerOver","Pressed"}){slider.Resources["SliderTrackValueFill"+suffix]=OnexDialogAccent;slider.Resources["SliderThumbBackground"+suffix]=OnexDialogAccent;}
            slider.ValueChanged+=(s,e)=>value.Text=((int)e.NewValue)+"%";
            panel.Children.Add(value);panel.Children.Add(slider);
            var ticks=new Grid();for(int i=0;i<3;i++)ticks.ColumnDefinitions.Add(new ColumnDefinition());
            int index=0;foreach(string label in new[]{"30%","65%","100%"}){var tick=new TextBlock{Text=label,FontSize=12,Foreground=OnexSettingsBrush(150,160,173),HorizontalAlignment=index==0?HorizontalAlignment.Left:index==2?HorizontalAlignment.Right:HorizontalAlignment.Center};Grid.SetColumn(tick,index++);ticks.Children.Add(tick);}panel.Children.Add(ticks);
            var saver=new ToggleSwitch{Name="OnexEnergySaverToggle"};ApplyOnexTileWindowControlAccent(saver);
            var status=new TextBlock{Name="OnexEnergySaverStatus",FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=OnexSettingsBrush(170,181,193)};
            var saverPanel=new StackPanel{Spacing=6};
            saverPanel.Children.Add(OnexSettingsInlineToggle(OnexHelpHeader(global::XboxGamingBar.OnexStrings.Get("Windows Energy Saver"),global::XboxGamingBar.OnexStrings.Get("Экономит заряд: Windows ограничивает часть фоновой работы и снижает расход энергии. Например, синхронизация почты может выполняться реже, а игра — работать медленнее. Экран может стать темнее, если это разрешено в настройках Windows. Работает и от зарядки. Изменение применяется сразу; лимит заряда и ваши PL1/PL2 сохраняются.")),saver));
            saverPanel.Children.Add(status);panel.Children.Add(OnexSettingsCard(saverPanel));
            _onexEnergySaverToggle=saver;_onexEnergySaverStatus=status;UpdateOnexEnergySaverControl();
            saver.Toggled+=(s,e)=>{if(!_onexEnergySaverLoading&&!_onexEnergySaverBusy)_=SetOnexEnergySaverAsync(saver.IsOn);};
            var dialog=CreateOnexSettingsDialog(global::XboxGamingBar.OnexStrings.Get("Charge Limit"),global::XboxGamingBar.OnexStrings.Get("Предел заряда — шаг 5%. Energy Saver применяется сразу."),panel);
            if(dialog.Content is ScrollViewer viewport){viewport.HorizontalContentAlignment=HorizontalAlignment.Stretch;if(viewport.Content is FrameworkElement body){body.Width=double.NaN;body.HorizontalAlignment=HorizontalAlignment.Stretch;}}
            dialog.IsPrimaryButtonEnabled=_onexChargeKnown;_onexDetailsDialog=dialog;
            try
            {
                if(await dialog.ShowAsync()==ContentDialogResult.Primary&&_onexChargeKnown)
                {int selected=(int)slider.Value;if(selected<100){_onexChargePercent=selected;Windows.Storage.ApplicationData.Current.LocalSettings.Values["OnexChargeTarget"]=selected;}await SetOnexChargeAsync(enabled.IsOn?selected:100);}
            }
            finally
            {
                if(ReferenceEquals(_onexEnergySaverToggle,saver)){_onexEnergySaverToggle=null;_onexEnergySaverStatus=null;}
                if(ReferenceEquals(_onexDetailsDialog,dialog))_onexDetailsDialog=null;
                if(widget!=null&&widget.Visible)anchor.Focus(FocusState.Programmatic);
            }
        }
    }
}
