// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private JsonObject _onexFpsState;private bool _onexFpsBusy,_onexFpsSyncing;private DispatcherTimer _onexFpsTimer;private int _onexFpsPending;private string _onexFpsPendingScope;
        private string OnexFpsProvider=>_onexFpsState?.GetNamedString("provider","Intel")??"Intel";
        private bool OnexFpsEnabled=>_onexFpsState?.GetNamedBoolean("enabled",false)==true;
        private int OnexFpsValue=>(int)(_onexFpsState?.GetNamedNumber("value",60)??60);
        private bool OnexFpsAvailable=>App.IsConnected&&_onexFpsState!=null&&!_onexFpsBusy;
        private void ApplyOnexFpsState(string json){if(JsonObject.TryParse(json,out var state))_onexFpsState=state;SyncOnexFpsControls();ApplyOnexTileAvailability();}
        private void SyncOnexFpsControls()
        {
            if(_onexFpsState==null||_onexFpsSyncing)return;_onexFpsSyncing=true;
            try
            {
                bool intel=OnexFpsProvider=="Intel";
                if(fpsCapMode!=null){fpsCapMode.SuppressRemoteSync=true;try{fpsCapMode.SetValue(intel?1:0,DateTimeOffset.Now.Ticks);}finally{fpsCapMode.SuppressRemoteSync=false;}}
                if(fpsLimit!=null){fpsLimit.SuppressRemoteSync=true;try{fpsLimit.SetValue(!intel&&OnexFpsEnabled?OnexFpsValue:0,DateTimeOffset.Now.Ticks);}finally{fpsLimit.SuppressRemoteSync=false;}}
                if(intelFpsTier!=null){intelFpsTier.SuppressRemoteSync=true;try{intelFpsTier.SetValue(intel&&OnexFpsEnabled?OnexFpsValue:0,DateTimeOffset.Now.Ticks);}finally{intelFpsTier.SuppressRemoteSync=false;}}
                FPSLimitToggle.IsOn=OnexFpsEnabled;FPSStateCycleButton.IsOn=OnexFpsEnabled;FPSModeToggle.IsOn=intel;FPSStateCycleText.Text=OnexFpsProvider;
                FPSModeIntelRadio.IsChecked=intel;FPSModeRTSSRadio.IsChecked=!intel;
                FPSLimitSlider.Minimum=30;FPSLimitSlider.Maximum=300;FPSLimitSlider.StepFrequency=1;FPSLimitSlider.SmallChange=1;FPSLimitSlider.LargeChange=10;
                if(!OnexInputGuard.IsEditing(FPSLimitSlider)&&_onexFpsTimer?.IsEnabled!=true)FPSLimitSlider.Value=OnexFpsValue;
                FPSLimitSlider.IsEnabled=App.IsConnected&&_onexFpsState!=null&&OnexFpsEnabled&&(!_onexFpsBusy||FPSLimitSlider.FocusState!=FocusState.Unfocused);
                int displayed=OnexInputGuard.IsEditing(FPSLimitSlider)||_onexFpsTimer?.IsEnabled==true?(int)Math.Round(FPSLimitSlider.Value):OnexFpsValue;
                FPSLimitValue.Text=OnexFpsEnabled?displayed+global::XboxGamingBar.OnexStrings.Get(" FPS"):global::XboxGamingBar.OnexStrings.Get("Off");
                FPSStateCycleButton.IsEnabled=OnexFpsAvailable;FPSModeToggle.IsEnabled=OnexFpsAvailable;FPSLimitToggle.IsEnabled=OnexFpsAvailable;
                FPSModeIntelRadio.IsEnabled=OnexFpsAvailable&&_onexFpsState.GetNamedObject("intel").GetNamedBoolean("available",false);FPSModeRTSSRadio.IsEnabled=OnexFpsAvailable&&_onexFpsState.GetNamedBoolean("rtssAvailable",false);
                OnexInputGuard.Wire(FPSLimitSlider,()=>QueueOnexFpsSlider());BuildFpsScaleLabels(OnexFpsEnabled?OnexFpsValue:-1);
            }
            finally{_onexFpsSyncing=false;}
        }
        private async Task SetOnexFpsAsync(string provider,bool enabled,int value)
        {
            if(_onexFpsBusy||!App.IsConnected)return;_onexFpsBusy=true;ApplyOnexTileAvailability();
            try{var data=new JsonObject{{"provider",JsonValue.CreateStringValue(provider)},{"enabled",JsonValue.CreateBooleanValue(enabled)},{"value",JsonValue.CreateNumberValue(Math.Max(30,Math.Min(300,value)))}};var r=await SendOnexHomeRequestAsync(new ValueSet{["OnexFpsSet"]=data.Stringify()});ApplyOnexFpsState((string)r["OnexFpsState"]);}
            catch(Exception ex){ShowOnexDesktopStatus(ex.Message);}
            finally{_onexFpsBusy=false;SyncOnexFpsControls();ApplyOnexTileAvailability();}
        }
        private void ToggleOnexFps()
        {
            if(!OnexFpsAvailable)return;
            // A delayed slider write must not re-enable a limit just switched off.
            _onexFpsTimer?.Stop();
            _=SetOnexFpsAsync(OnexFpsProvider,!OnexFpsEnabled,OnexFpsValue);
        }
        private void SwitchOnexFpsProvider()=>_=SetOnexFpsAsync(OnexFpsProvider=="Intel"?"RTSS":"Intel",OnexFpsEnabled,OnexFpsValue);
        private void QueueOnexFpsSlider()
        {
            if(_onexFpsSyncing||isApplyingHelperUpdate||!OnexFpsEnabled||!OnexInputGuard.IsActive(FPSLimitSlider))return;
            _onexFpsPendingScope=OnexFrontend.ProfileId;_onexFpsPending=(int)Math.Round(FPSLimitSlider.Value);FPSLimitValue.Text=_onexFpsPending+global::XboxGamingBar.OnexStrings.Get(" FPS");
            if(_onexFpsTimer==null){_onexFpsTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};_onexFpsTimer.Tick+=async(s,e)=>{_onexFpsTimer.Stop();if(_onexFpsPendingScope!=OnexFrontend.ProfileId)return;if(_onexFpsBusy){_onexFpsTimer.Start();return;}await SetOnexFpsAsync(OnexFpsProvider,true,_onexFpsPending);};}_onexFpsTimer.Stop();_onexFpsTimer.Start();
        }
        private MenuFlyout ShowOnexFpsMenu(Button anchor)
        {
            var menu=CreateOnexMenu();foreach(string provider in new[]{"Intel","RTSS"}){string p=provider;var item=new MenuFlyoutItem{Text=p+global::XboxGamingBar.OnexStrings.Get(" limiter"),IsEnabled=p=="Intel"?_onexFpsState?.GetNamedObject("intel").GetNamedBoolean("available",false)==true:_onexFpsState?.GetNamedBoolean("rtssAvailable",false)==true};if(p==OnexFpsProvider)item.Icon=new SymbolIcon(Symbol.Accept);item.Click+=async(s,e)=>await SetOnexFpsAsync(p,OnexFpsEnabled,OnexFpsValue);menu.Items.Add(item);}
            menu.Items.Add(new MenuFlyoutSeparator());foreach(int number in new[]{0,30,40,60,90,120,144,165}){int fps=number;var item=new MenuFlyoutItem{Text=fps==0?global::XboxGamingBar.OnexStrings.Get("Off"):fps+global::XboxGamingBar.OnexStrings.Get(" FPS")};if((OnexFpsEnabled?OnexFpsValue:0)==fps)item.Icon=new SymbolIcon(Symbol.Accept);item.Click+=async(s,e)=>await SetOnexFpsAsync(OnexFpsProvider,fps>0,fps>0?fps:OnexFpsValue);menu.Items.Add(item);}
            var settings=new MenuFlyoutItem{Text=global::XboxGamingBar.OnexStrings.Get("Limiter settings"),Icon=new SymbolIcon(Symbol.Setting)};settings.Click+=async(s,e)=>await ShowOnexFpsSettingsAsync(anchor);menu.Items.Add(settings);menu.ShowAt(anchor);return menu;
        }
        private async Task ShowOnexFpsSettingsAsync(Button anchor)
        {
            if(_onexDetailsDialog!=null)return;
            var panel=new StackPanel{Spacing=12};var provider=OnexSettingsChoice(global::XboxGamingBar.OnexStrings.Get("Limiter"));provider.ItemsSource=new[]{"Intel","RTSS"};provider.SelectedIndex=OnexFpsProvider=="Intel"?0:1;
            var enabled=new ToggleSwitch{Header=global::XboxGamingBar.OnexStrings.Get("FPS limit"),IsOn=OnexFpsEnabled};var value=new TextBlock{Text=OnexFpsValue+global::XboxGamingBar.OnexStrings.Get(" FPS"),FontSize=32,HorizontalAlignment=HorizontalAlignment.Center};var slider=new Slider{Minimum=30,Maximum=300,StepFrequency=1,SmallChange=1,LargeChange=10,Value=OnexFpsValue,IsThumbToolTipEnabled=true};slider.ValueChanged+=(s,e)=>value.Text=((int)e.NewValue)+global::XboxGamingBar.OnexStrings.Get(" FPS");
            panel.Children.Add(provider);panel.Children.Add(enabled);panel.Children.Add(value);panel.Children.Add(slider);panel.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Intel: ограничение в драйвере. RTSS: ограничение через внедрение в игру. При смене режима другой лимитер отключается. Настройка глобальная; профиль самой игры может иметь приоритет."),TextWrapping=TextWrapping.Wrap,FontSize=13});
            var dialog=CreateOnexSettingsDialog(global::XboxGamingBar.OnexStrings.Get("FPS Limit"),global::XboxGamingBar.OnexStrings.Get("30–300 FPS · step 1"),panel);_onexDetailsDialog=dialog;
            try{if(await dialog.ShowAsync()==ContentDialogResult.Primary)await SetOnexFpsAsync(provider.SelectedIndex==0?"Intel":"RTSS",enabled.IsOn,(int)slider.Value);}
            finally{_onexDetailsDialog=null;anchor.Focus(FocusState.Programmatic);}
        }
        private void ApplyOnexFpsTile(TileDefinition tile)
        {if(tile.StateText==null)return;tile.StateText.Text=_onexFpsBusy?global::XboxGamingBar.OnexStrings.Get("Applying…"):OnexFpsProvider+" · "+(OnexFpsEnabled?OnexFpsValue.ToString():global::XboxGamingBar.OnexStrings.Get("Off"));ApplyOnexToggleTileVisual(tile,OnexFpsEnabled,_onexFpsState!=null);}
    }
}
