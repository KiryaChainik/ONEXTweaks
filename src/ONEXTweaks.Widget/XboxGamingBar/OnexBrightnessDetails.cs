// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private bool _onexBrightnessDimBusy;
        private DateTime _onexBrightnessHeldUntil;
        private Line _onexBrightnessSlash;
        private bool OnexBrightnessDimmed=>_onexMediaState?.GetNamedBoolean("brightnessDimmed",false)==true;
        private void ApplyOnexBrightnessVisual()
        {
            if(RailBrightnessIcon==null)return;bool dimmed=OnexBrightnessDimmed;
            RailBrightnessIcon.Foreground=dimmed?new SolidColorBrush(Color.FromArgb(255,255,107,107)):MediaAccentBrush();
            if(_onexBrightnessSlash==null)_onexBrightnessSlash=new Line{Width=24,Height=28,X1=3,Y1=5,X2=21,Y2=23,StrokeThickness=1.8,Stroke=new SolidColorBrush(Color.FromArgb(255,255,107,107)),IsHitTestVisible=false,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            if(RailBrightnessIcon.Parent is Grid host&&!host.Children.Contains(_onexBrightnessSlash)){if(_onexBrightnessSlash.Parent is Panel old)old.Children.Remove(_onexBrightnessSlash);Grid.SetColumn(_onexBrightnessSlash,Grid.GetColumn(RailBrightnessIcon));host.Children.Add(_onexBrightnessSlash);}
            _onexBrightnessSlash.Visibility=dimmed?Visibility.Visible:Visibility.Collapsed;RailBrightnessIcon.Opacity=_onexBrightnessDimBusy?.5:1;RailBrightnessSlider.Opacity=dimmed?.45:1;
            string hint=(dimmed?global::XboxGamingBar.OnexStrings.Get("Восстановить яркость"):global::XboxGamingBar.OnexStrings.Get("Минимальная яркость"))+global::XboxGamingBar.OnexStrings.Get(" · удержание: Windows → Система → Дисплей");ToolTipService.SetToolTip(RailBrightnessIcon,hint);Windows.UI.Xaml.Automation.AutomationProperties.SetName(RailBrightnessIcon,hint);
        }
        private async Task ToggleOnexBrightnessDimAsync()
        {
            if(!App.IsConnected||!_onexBrightnessAvailable||_onexBrightnessDimBusy)return;_onexBrightnessDimBusy=true;ApplyOnexBrightnessVisual();
            try
            {
                // A tap immediately after a drag must remember the last acknowledged slider value.
                if(_onexMediaTimer!=null)FlushOnexMedia(null,null);
                for(int n=0;n<250&&OnexMediaBusy("SetBrightnessLevel");n++)await Task.Delay(20);
                if(OnexMediaBusy("SetBrightnessLevel"))throw new TimeoutException(global::XboxGamingBar.OnexStrings.Get("Не удалось дождаться изменения яркости"));
                if(_onexMediaWriteError!=null)throw new InvalidOperationException(_onexMediaWriteError);
                var reply=await App.SendMessageAsync(new ValueSet{{"ToggleBrightnessDim",true},{"OnexMediaUserInput",true}});
                if(reply==null||reply.ContainsKey("Error"))throw new InvalidOperationException(reply?.ContainsKey("Error")==true?reply["Error"].ToString():global::XboxGamingBar.OnexStrings.Get("Нет соединения с помощником"));
                ApplyOnexMediaLevels((string)reply["MediaLevels"]);
            }
            catch(Exception error){ShowOnexDesktopStatus(error.Message);await RefreshMediaSliderLevelsAsync();}
            finally{_onexBrightnessDimBusy=false;ApplyOnexBrightnessVisual();}
        }
        private Task OpenOnexBrightnessSettingsAsync()=>InvokeOnexDesktopActionAsync("displaySettings");
        private void WireOnexBrightnessDetails()
        {
            var icon=RailBrightnessIcon;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(650)};bool pressed=false;Point origin=new Point();
            void Open(){timer.Stop();if(!pressed)return;pressed=false;_onexBrightnessHeldUntil=DateTime.UtcNow.AddSeconds(1);if(widget!=null&&(!widget.Visible||widget.GameBarDisplayMode!=Microsoft.Gaming.XboxGameBar.XboxGameBarDisplayMode.Foreground))return;_=OpenOnexBrightnessSettingsAsync();}
            timer.Tick+=(s,e)=>Open();icon.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((s,e)=>{origin=e.GetCurrentPoint(icon).Position;pressed=true;timer.Start();}),true);
            icon.AddHandler(UIElement.PointerMovedEvent,new PointerEventHandler((s,e)=>{var point=e.GetCurrentPoint(icon).Position;if(Math.Abs(point.X-origin.X)>10||Math.Abs(point.Y-origin.Y)>10){pressed=false;timer.Stop();}}),true);
            PointerEventHandler release=(s,e)=>{pressed=false;timer.Stop();};icon.AddHandler(UIElement.PointerReleasedEvent,release,true);icon.AddHandler(UIElement.PointerCanceledEvent,release,true);icon.AddHandler(UIElement.PointerCaptureLostEvent,release,true);
            icon.Tapped+=async(s,e)=>{if(DateTime.UtcNow<_onexBrightnessHeldUntil){e.Handled=true;return;}await ToggleOnexBrightnessDimAsync();e.Handled=true;};
            icon.RightTapped+=(s,e)=>{pressed=false;timer.Stop();_onexBrightnessHeldUntil=DateTime.UtcNow.AddSeconds(1);_=OpenOnexBrightnessSettingsAsync();e.Handled=true;};icon.Unloaded+=(s,e)=>{pressed=false;timer.Stop();};
            ApplyOnexBrightnessVisual();
        }
    }
}
