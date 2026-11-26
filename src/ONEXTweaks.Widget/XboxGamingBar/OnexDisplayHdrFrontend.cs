// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private ToggleSwitch _onexHdrEnabled,_onexAutoHdr;
        private ComboBox _onexHdrCalibration;
        private Slider _onexSdrBrightness;
        private Button _onexHdrReset,_onexHdrCalibrate;
        private TextBlock _onexHdrStatus,_onexSdrValue;
        private Border _onexHdrCard;
        private bool _onexHdrSyncing;
        private bool OnexHdrReady=>App.IsConnected&&OnexDisplayObject(_onexDisplayState,"hdr")?.GetNamedBoolean("available",false)==true;
        private void EnsureOnexHdrEditor()
        {
            if(_onexHdrCard!=null)return;
            var panel=new StackPanel{Spacing=10};
            panel.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Windows HDR"),FontSize=18,FontWeight=Windows.UI.Text.FontWeights.SemiBold});
            _onexHdrEnabled=new ToggleSwitch{Name="OnexHdrEnabled",Header=global::XboxGamingBar.OnexStrings.Get("HDR")};
            _onexAutoHdr=new ToggleSwitch{Name="OnexAutoHdr",Header=global::XboxGamingBar.OnexStrings.Get("Auto HDR")};
            foreach(var toggle in new[]{_onexHdrEnabled,_onexAutoHdr}){OnexInputGuard.Wire(toggle);ApplyOnexTileWindowControlAccent(toggle);}
            _onexHdrEnabled.Toggled+=async(s,e)=>{if(!_onexHdrSyncing&&OnexInputGuard.ConsumeToggleInput(_onexHdrEnabled))await SetOnexDisplayAsync("hdrEnabled",JsonValue.CreateBooleanValue(_onexHdrEnabled.IsOn));};
            _onexAutoHdr.Toggled+=async(s,e)=>{if(!_onexHdrSyncing&&OnexInputGuard.ConsumeToggleInput(_onexAutoHdr))await SetOnexDisplayAsync("autoHdr",JsonValue.CreateBooleanValue(_onexAutoHdr.IsOn));};
            panel.Children.Add(OnexSettingsColumns(_onexHdrEnabled,_onexAutoHdr));
            panel.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("HDR расширяет диапазон яркости. Auto HDR добавляет HDR в совместимые SDR-игры. Например, старая DirectX-игра может получить более яркие блики. После смены Auto HDR перезапусти игру; её собственные настройки могут иметь приоритет."),FontSize=12,Opacity=.75,TextWrapping=TextWrapping.Wrap});
            _onexHdrCalibration=new ComboBox{Name="OnexHdrCalibration",Header=global::XboxGamingBar.OnexStrings.Get("Калибровка HDR / Advanced Color"),HorizontalAlignment=HorizontalAlignment.Stretch,PlaceholderText=global::XboxGamingBar.OnexStrings.Get("Нет установленной калибровки HDR")};
            OnexInputGuard.Wire(_onexHdrCalibration);ApplyOnexTileWindowControlAccent(_onexHdrCalibration);
            _onexHdrCalibration.SelectionChanged+=async(s,e)=>
            {
                if(!_onexHdrSyncing&&!_onexDisplayBusy&&OnexInputGuard.IsActive(_onexHdrCalibration)&&_onexHdrCalibration.SelectedItem is ComboBoxItem item&&item.Tag is string id)
                    await SetOnexDisplayAsync("hdrCalibration",JsonValue.CreateStringValue(id));
            };
            panel.Children.Add(_onexHdrCalibration);
            _onexSdrValue=new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Яркость SDR-контента в HDR"),FontSize=13};panel.Children.Add(_onexSdrValue);
            _onexSdrBrightness=new Slider{Name="OnexSdrBrightness",Minimum=0,Maximum=100,StepFrequency=1,SmallChange=5,LargeChange=5,Style=(Style)Resources["LightMediaSliderStyle"],Foreground=OnexDialogAccent};
            WireOnexDisplayThumb(_onexSdrBrightness);
            OnexInputGuard.ConfigurePercentage(_onexSdrBrightness);
            OnexInputGuard.SetCommit(_onexSdrBrightness,()=>QueueOnexDisplaySlider("sdrBrightness",(int)_onexSdrBrightness.Value));
            _onexSdrBrightness.ValueChanged+=(s,e)=>
            {UpdateOnexSdrValue();if(!_onexHdrSyncing&&OnexInputGuard.IsActive(_onexSdrBrightness))QueueOnexDisplaySlider("sdrBrightness",(int)_onexSdrBrightness.Value);};
            _onexSdrBrightness.AddHandler(UIElement.PointerPressedEvent,new Windows.UI.Xaml.Input.PointerEventHandler((s,e)=>{if(!_onexHdrSyncing&&_onexSdrBrightness.IsEnabled){_onexDisplayDragging.Add(_onexSdrBrightness);QueueOnexDisplaySlider("sdrBrightness",(int)_onexSdrBrightness.Value);}}),true);
            Windows.UI.Xaml.Input.PointerEventHandler released=(s,e)=>{_onexDisplayDragging.Remove(_onexSdrBrightness);if(_onexDisplayPending.Count>0){_onexDisplayDebounce.Stop();_onexDisplayDebounce.Start();}};
            _onexSdrBrightness.AddHandler(UIElement.PointerReleasedEvent,released,true);_onexSdrBrightness.AddHandler(UIElement.PointerCanceledEvent,released,true);_onexSdrBrightness.AddHandler(UIElement.PointerCaptureLostEvent,released,true);
            panel.Children.Add(_onexSdrBrightness);
            panel.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Меняет яркость обычных приложений внутри HDR, как ползунок Windows. Например, 25% делает рабочий стол темнее, не меняя максимальную яркость HDR-бликов. Это не ползунок подсветки экрана."),FontSize=12,Opacity=.75,TextWrapping=TextWrapping.Wrap});
            _onexHdrCalibrate=new Button{Name="OnexHdrCalibrate",Content=global::XboxGamingBar.OnexStrings.Get("Калибровка HDR"),HorizontalAlignment=HorizontalAlignment.Stretch};
            _onexHdrReset=new Button{Name="OnexHdrReset",Content=global::XboxGamingBar.OnexStrings.Get("Reset to default"),HorizontalAlignment=HorizontalAlignment.Stretch};
            _onexHdrCalibrate.Click+=async(s,e)=>await InvokeOnexDesktopActionAsync("hdrCalibration");
            _onexHdrReset.Click+=async(s,e)=>await SetOnexDisplayAsync("hdrReset",JsonValue.CreateBooleanValue(true));
            panel.Children.Add(OnexSettingsColumns(_onexHdrCalibrate,_onexHdrReset));
            panel.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Default: HDR On, Auto HDR On, самая новая установленная калибровка по дате файла. Яркость SDR-контента не сбрасывается. Калибровка открывается в приложении Windows HDR Calibration."),FontSize=12,Opacity=.75,TextWrapping=TextWrapping.Wrap});
            _onexHdrStatus=new TextBlock{Name="OnexHdrStatus",FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=OnexDialogAccent};panel.Children.Add(_onexHdrStatus);
            _onexHdrCard=OnexSettingsCard(panel);
            DisplayMainStack.Children.Insert(DisplayMainStack.Children.IndexOf(DisplayTabHost)+1,_onexHdrCard);
        }
        private void UpdateOnexSdrValue()=>_onexSdrValue.Text=global::XboxGamingBar.OnexStrings.Format("Яркость SDR-контента в HDR · {0}%",((int)_onexSdrBrightness.Value));
        private void ApplyOnexHdrState()
        {
            if(_onexHdrCard==null)return;
            var state=OnexDisplayObject(_onexDisplayState,"hdr");_onexHdrSyncing=true;
            try
            {
                _onexHdrEnabled.IsOn=state?.GetNamedBoolean("enabled",false)==true;
                _onexHdrCalibrate.Content=state?.GetNamedBoolean("calibrationAppInstalled",false)==true?global::XboxGamingBar.OnexStrings.Get("Калибровка HDR"):global::XboxGamingBar.OnexStrings.Get("Установить HDR Calibration");
                var auto=state?.GetNamedValue("autoHdr",JsonValue.CreateNullValue());
                _onexAutoHdr.IsOn=auto?.ValueType==JsonValueType.Boolean&&auto.GetBoolean();
                var brightness=state?.GetNamedValue("sdrBrightness",JsonValue.CreateNullValue());
                if(brightness?.ValueType==JsonValueType.Number&&!OnexInputGuard.IsEditing(_onexSdrBrightness)&&!_onexDisplayPending.ContainsKey("sdrBrightness"))_onexSdrBrightness.Value=brightness.GetNumber();
                if(!_onexHdrCalibration.IsDropDownOpen||_onexDisplayBusy)
                {
                    var profiles=state?.GetNamedArray("profiles",null);string signature=profiles?.Stringify()??"[]";
                    if((_onexHdrCalibration.Tag as string)!=signature)
                    {
                        _onexHdrCalibration.Items.Clear();
                        if(profiles!=null)foreach(var entry in profiles)
                        {var profile=entry.GetObject();string date=OnexDisplayText(profile,"date");if(DateTimeOffset.TryParse(date,out var parsed))date=parsed.ToLocalTime().ToString("dd.MM.yyyy");_onexHdrCalibration.Items.Add(new ComboBoxItem{Tag=OnexDisplayText(profile,"id"),Content=OnexDisplayText(profile,"name")+" · "+date});}
                        _onexHdrCalibration.Tag=signature;
                    }
                    string current=OnexDisplayText(state,"currentProfile");
                    _onexHdrCalibration.SelectedItem=_onexHdrCalibration.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>string.Equals(item.Tag as string,current,StringComparison.OrdinalIgnoreCase));
                }
                _onexHdrStatus.Text=!OnexHdrReady?OnexDisplayText(state,"error",global::XboxGamingBar.OnexStrings.Get("Состояние HDR пока недоступно")):
                    state.GetNamedBoolean("supported",false)?(_onexHdrEnabled.IsOn&&!state.GetNamedBoolean("active",false)?global::XboxGamingBar.OnexStrings.Get("Windows сохранила HDR On, но сейчас HDR не активен: проверь питание и настройки Windows."):"")+(auto?.ValueType==JsonValueType.Null?global::XboxGamingBar.OnexStrings.Get(" Auto HDR: используются настройки Windows по умолчанию."):"")+(_onexHdrCalibration.Items.Count==0?global::XboxGamingBar.OnexStrings.Get(" Установленных калибровок HDR не найдено."):""):global::XboxGamingBar.OnexStrings.Get("Этот дисплей не сообщает о поддержке HDR.");
                _onexHdrStatus.Visibility=string.IsNullOrWhiteSpace(_onexHdrStatus.Text)?Visibility.Collapsed:Visibility.Visible;
                UpdateOnexSdrValue();
            }
            finally{_onexHdrSyncing=false;UpdateOnexHdrAvailability();}
        }
        private void UpdateOnexHdrAvailability()
        {
            if(_onexHdrCard==null)return;
            var state=OnexDisplayObject(_onexDisplayState,"hdr");bool ready=OnexHdrReady&&state.GetNamedBoolean("supported",false);
            bool Available(Control control)=>ready&&!_onexDisplayProfileTransition&&(!_onexDisplayBusy||control.FocusState!=FocusState.Unfocused);
            _onexHdrEnabled.IsEnabled=Available(_onexHdrEnabled);_onexAutoHdr.IsEnabled=Available(_onexAutoHdr);
            _onexHdrCalibration.IsEnabled=Available(_onexHdrCalibration)&&_onexHdrCalibration.Items.Count>0;
            _onexHdrReset.IsEnabled=Available(_onexHdrReset);_onexHdrCalibrate.IsEnabled=Available(_onexHdrCalibrate);
            var brightness=state?.GetNamedValue("sdrBrightness",JsonValue.CreateNullValue());
            _onexSdrBrightness.IsEnabled=!_onexDisplayProfileTransition&&OnexHdrReady&&state.GetNamedBoolean("enabled",false)&&brightness?.ValueType==JsonValueType.Number&&(!_onexDisplayBusy||_onexSdrBrightness.FocusState!=FocusState.Unfocused);
        }
    }
}
