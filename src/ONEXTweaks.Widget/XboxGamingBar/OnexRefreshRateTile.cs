// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private readonly Dictionary<int,Button> _onexRateButtons=new Dictionary<int,Button>();
        private ComboBox _onexRateSync,_onexRateAll;
        private ToggleSwitch _onexRateVrr;
        private TextBlock _onexRateCurrent,_onexRateStatus;
        private bool _onexRateSyncing,_onexRateTileBusy;
        private JsonObject OnexCurrentDisplayMode=>OnexDisplayObject(OnexDisplayObject(_onexDisplayState,"modes"),"current");
        private int OnexCurrentRefreshRate=>(int)(OnexCurrentDisplayMode?.GetNamedNumber("hz",0)??0);
        private int[] OnexSupportedRefreshRates()
        {
            var modes=OnexDisplayObject(_onexDisplayState,"modes")?.GetNamedArray("modes",null);var current=OnexCurrentDisplayMode;
            if(modes==null||current==null)return new int[0];
            return modes.Select(item=>item.GetObject()).Where(mode=>mode.GetNamedNumber("width",0)==current.GetNamedNumber("width",0)&&mode.GetNamedNumber("height",0)==current.GetNamedNumber("height",0)).Select(mode=>(int)mode.GetNamedNumber("hz",0)).Distinct().OrderBy(hz=>hz).ToArray();
        }
        private string OnexRefreshTileCaption=>_onexRateTileBusy||_onexDisplayBusy?"Applying…":OnexDisplayModesReady&&OnexCurrentRefreshRate>0?OnexCurrentRefreshRate+" Hz":"Unavailable";
        private async Task CycleOnexRefreshRateAsync()
        {
            if(_onexRateTileBusy||_onexDisplayBusy||!OnexDisplayModesReady)return;
            _onexRateTileBusy=true;ApplyOnexTileAvailability();
            try
            {
                await RefreshOnexDisplayAsync(false);
                int next=OnexRefreshRatePolicy.Next(OnexCurrentRefreshRate,OnexSupportedRefreshRates());
                if(!await SetOnexDisplayAsync("refreshRate",JsonValue.CreateNumberValue(next)))throw new InvalidOperationException(_onexDisplayStatus.Text);
                if(OnexCurrentRefreshRate!=next)throw new InvalidOperationException("Windows did not confirm "+next+" Hz.");
            }
            catch(Exception error){ShowOnexDesktopStatus(error.Message);}
            finally{_onexRateTileBusy=false;ApplyOnexTileAvailability();}
        }
        private void RefreshOnexRateWindow()
        {
            if(_onexRateCurrent==null)return;
            _onexRateSyncing=true;
            try
            {
                var current=OnexCurrentDisplayMode;var supported=OnexSupportedRefreshRates();
                _onexRateCurrent.Text=current==null?global::XboxGamingBar.OnexStrings.Get("Display mode unavailable"):global::XboxGamingBar.OnexStrings.Format("Current: {0} × {1} • {2} Hz",current.GetNamedNumber("width",0),current.GetNamedNumber("height",0),OnexCurrentRefreshRate);
                foreach(var pair in _onexRateButtons)
                {
                    bool active=pair.Key==OnexCurrentRefreshRate,ready=OnexDisplayModesReady&&supported.Contains(pair.Key);
                    pair.Value.Content=pair.Key+global::XboxGamingBar.OnexStrings.Get(" Hz")+(active?" ✓":"");pair.Value.IsEnabled=ready&&(!_onexDisplayBusy||pair.Value.FocusState!=FocusState.Unfocused);
                    pair.Value.BorderBrush=active?OnexDialogAccent:OnexSettingsBrush(65,73,85);pair.Value.BorderThickness=new Thickness(active?2:1);
                    ToolTipService.SetToolTip(pair.Value,ready?null:global::XboxGamingBar.OnexStrings.Get("Not reported by Windows for the current resolution."));
                }
                if(!_onexRateAll.Items.OfType<ComboBoxItem>().Select(item=>(int)item.Tag).SequenceEqual(supported)){_onexRateAll.Items.Clear();foreach(int hz in supported)_onexRateAll.Items.Add(new ComboBoxItem{Content=hz+global::XboxGamingBar.OnexStrings.Get(" Hz"),Tag=hz});}
                if(!_onexRateAll.IsDropDownOpen)_onexRateAll.SelectedItem=_onexRateAll.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>(int)item.Tag==OnexCurrentRefreshRate);
                _onexRateAll.IsEnabled=OnexDisplayModesReady&&(!_onexDisplayBusy||_onexRateAll.FocusState!=FocusState.Unfocused);
                var settings=OnexDisplayObject(_onexDisplayState,"settings");
                void Choice(ComboBox control,string key)
                {
                    var state=OnexDisplayObject(settings,key);var value=state?.GetNamedValue("value",JsonValue.CreateNullValue());
                    if(!control.IsDropDownOpen)control.SelectedIndex=value?.ValueType==JsonValueType.Number?(int)value.GetNumber():-1;
                    control.IsEnabled=OnexDisplayReady&&(!_onexDisplayBusy||control.FocusState!=FocusState.Unfocused)&&state?.GetNamedBoolean("available",false)==true;
                    ToolTipService.SetToolTip(control,control.IsEnabled?null:OnexDisplayText(state,"error","Unavailable"));
                }
                Choice(_onexRateSync,"frameSync");ApplyOnexDisplayChoiceSupport(_onexRateSync,OnexDisplayObject(settings,"frameSync"));
                var vrr=OnexDisplayObject(settings,"vrr");var vrrValue=vrr?.GetNamedValue("value",JsonValue.CreateNullValue());_onexRateVrr.IsOn=vrrValue?.ValueType==JsonValueType.Number&&vrrValue.GetNumber()==1;
                _onexRateVrr.IsEnabled=OnexDisplayReady&&(!_onexDisplayBusy||_onexRateVrr.FocusState!=FocusState.Unfocused)&&vrr?.GetNamedBoolean("available",false)==true;
                ToolTipService.SetToolTip(_onexRateVrr,OnexDisplayText(vrr,"error","Intel Arc Sync"));
            }
            finally{_onexRateSyncing=false;}
        }
        private async Task SetOnexRateWindowValueAsync(string key,int value)
        {
            if(_onexRateSyncing||_onexDisplayBusy)return;
            if(_onexRateStatus!=null)_onexRateStatus.Text=global::XboxGamingBar.OnexStrings.Get("Applying…");
            bool applied=await SetOnexDisplayAsync(key,JsonValue.CreateNumberValue(value));
            RefreshOnexRateWindow();
            if(_onexRateStatus!=null)_onexRateStatus.Text=applied?global::XboxGamingBar.OnexStrings.Get("Applied • read back from Windows / Intel"):_onexDisplayStatus.Text;
        }
        private async Task ShowOnexRefreshRateSettingsAsync(Button anchor)
        {
            if(_onexDetailsDialog!=null||_onexRateTileBusy)return;
            var body=new StackPanel{Spacing=12};_onexRateCurrent=new TextBlock{FontSize=14,TextWrapping=TextWrapping.Wrap};body.Children.Add(_onexRateCurrent);
            var rates=new Grid{ColumnSpacing=8,RowSpacing=8};for(int i=0;i<3;i++)rates.ColumnDefinitions.Add(new ColumnDefinition());for(int i=0;i<2;i++)rates.RowDefinitions.Add(new RowDefinition());
            _onexRateButtons.Clear();for(int i=0;i<OnexRefreshRatePolicy.MenuRates.Length;i++)
            {
                int hz=OnexRefreshRatePolicy.MenuRates[i];var button=new Button{Name="OnexRate"+hz,Content=hz+global::XboxGamingBar.OnexStrings.Get(" Hz"),HorizontalAlignment=HorizontalAlignment.Stretch,Style=OnexSettingsButtonStyle(false),Padding=new Thickness(8,10,8,10)};
                button.Click+=async(s,e)=>await SetOnexRateWindowValueAsync("refreshRate",hz);_onexRateButtons.Add(hz,button);Grid.SetColumn(button,i%3);Grid.SetRow(button,i/3);rates.Children.Add(button);
            }
            body.Children.Add(rates);body.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Недоступные частоты не заявлены Windows для текущего разрешения. Частота экрана и ограничение FPS — отдельные настройки."),FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=OnexSettingsBrush(170,181,193)});
            _onexRateAll=OnexSettingsChoice(global::XboxGamingBar.OnexStrings.Get("All Windows refresh rates"));OnexInputGuard.Wire(_onexRateAll);ApplyOnexTileWindowControlAccent(_onexRateAll);
            _onexRateAll.SelectionChanged+=async(s,e)=>{if(!_onexRateSyncing&&OnexInputGuard.IsActive(_onexRateAll)&&_onexRateAll.SelectedItem is ComboBoxItem item)await SetOnexRateWindowValueAsync("refreshRate",(int)item.Tag);};body.Children.Add(_onexRateAll);
            _onexRateSync=OnexSettingsChoice(global::XboxGamingBar.OnexStrings.Get("Frame Sync (Intel)"));foreach(var name in new[]{OnexStrings.Get("Application Default"),OnexStrings.Get("VSync Off"),OnexStrings.Get("VSync On"),"Smooth Sync","Speed Sync","Match refresh rate (FPS cap)"})_onexRateSync.Items.Add(new ComboBoxItem{Content=name});
            _onexRateSync.SelectionChanged+=async(s,e)=>{if(!_onexRateSyncing&&OnexInputGuard.IsActive(_onexRateSync)&&_onexRateSync.SelectedIndex>=0)await SetOnexRateWindowValueAsync("frameSync",_onexRateSync.SelectedIndex);};OnexInputGuard.Wire(_onexRateSync);ApplyOnexTileWindowControlAccent(_onexRateSync);body.Children.Add(_onexRateSync);
            _onexRateVrr=new ToggleSwitch{Name="OnexRateVrr"};OnexInputGuard.Wire(_onexRateVrr);ApplyOnexTileWindowControlAccent(_onexRateVrr);_onexRateVrr.Toggled+=async(s,e)=>{if(!_onexRateSyncing&&OnexInputGuard.IsActive(_onexRateVrr))await SetOnexRateWindowValueAsync("vrr",_onexRateVrr.IsOn?1:0);};
            body.Children.Add(OnexSettingsInlineToggle(OnexHelpHeader(global::XboxGamingBar.OnexStrings.Get("Intel VRR"),global::XboxGamingBar.OnexStrings.Get("Экран подстраивает частоту под кадры игры, когда режим поддерживается. Например, игра выдаёт меньше 144 FPS — частота может изменяться вместе с ней. На этом дисплее Intel VRR доступен при родном разрешении 1920×1200.")),_onexRateVrr));
            _onexRateStatus=new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Changes apply immediately to the active Performance profile."),FontSize=12,TextWrapping=TextWrapping.Wrap};body.Children.Add(_onexRateStatus);
            var dialog=CreateOnexSettingsDialog(global::XboxGamingBar.OnexStrings.Get("Refresh Rate"),global::XboxGamingBar.OnexStrings.Get("Tap the tile: 60 ↔ 144 Hz. Hold / X: frequency, Sync and VRR."),body);dialog.PrimaryButtonText="";dialog.CloseButtonText=global::XboxGamingBar.OnexStrings.Get("Close");dialog.DefaultButton=ContentDialogButton.Close;if(dialog.Content is ScrollViewer viewport)viewport.Background=OnexDialogSurface;_onexDetailsDialog=dialog;
            try{await RefreshOnexDisplayAsync(false);RefreshOnexRateWindow();await dialog.ShowAsync();}
            catch(Exception error){ShowOnexDesktopStatus(error.Message);}
            finally{_onexDetailsDialog=null;_onexRateButtons.Clear();_onexRateCurrent=_onexRateStatus=null;_onexRateSync=_onexRateAll=null;_onexRateVrr=null;anchor?.Focus(FocusState.Programmatic);}
        }
    }
}
