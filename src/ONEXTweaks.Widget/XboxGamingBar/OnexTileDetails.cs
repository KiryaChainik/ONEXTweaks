// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private readonly Dictionary<Button,DateTimeOffset> _onexHeldTiles=new Dictionary<Button,DateTimeOffset>();
        private ContentDialog _onexDetailsDialog;
        private static bool OnexHasTileDetails(string id)=>id=="OnexControllerProfile"||id=="OnexInputMode"||id=="ReShade"||id=="Profile"||id=="OnexResolution"||id=="OnexRefreshRate"||id=="OnexAwake"||id=="OnexBluetooth"||id=="OnexWifi"||id=="OnexFse"||id=="ChargeLimiter"||id=="CPUBoost"||id=="LedToggle"||id=="OnexFanMode"||id=="PowerMode"||id=="Overlay"||id=="FPSCombined";
        private bool OpenFocusedOnexTileDetails()
        {
            if(!OnexFrontend.UsesOwnBackend||qsEditMode||_onexDetailsDialog!=null)return false;
            var focus=FocusManager.GetFocusedElement() as DependencyObject;
            if(ReferenceEquals(focus,RailMuteButton)){_=ShowOnexAudioOutputAsync(RailMuteButton);return true;}
            if(ReferenceEquals(focus,RailMicButton)){_=ShowOnexAudioInputAsync(RailMicButton);return true;}
            while(focus!=null)
            {
                if(focus is Button button&&button.Tag is string id&&OnexHasTileDetails(id)&&qsTileMap.TryGetValue(id,out var tile)&&ReferenceEquals(tile.TileButton,button)&&button.IsEnabled)
                {_=ShowOnexTileDetailsAsync(button,id);return true;}
                focus=VisualTreeHelper.GetParent(focus);
            }
            return false;
        }
        private void AttachOnexTileDetails(Button button,string id)
        {
            if(!OnexHasTileDetails(id))return;
            var inner=button.Content as UIElement;button.Content=null;
            var overlay=new Grid();if(inner!=null)overlay.Children.Add(inner);
            overlay.Children.Add(new FontIcon{Glyph="\uE712",FontSize=11,Foreground=tileTextBrush??OnexSettingsBrush(160,160,160),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,-7,-4,0),IsHitTestVisible=false});
            button.Content=overlay;
            ToolTipService.SetToolTip(button,id=="Overlay"?OnexStrings.Get("Tap to choose a preset. Hold for details · Gamepad X / Shift+F10"):(id=="Profile"||id=="OnexFanMode"||id=="ReShade"||id=="OnexControllerProfile")?global::XboxGamingBar.OnexStrings.Get("Tap to choose a profile. Hold for details · Gamepad X / Shift+F10"):global::XboxGamingBar.OnexStrings.Get("Tap to toggle. Hold for settings · Gamepad X / Shift+F10"));
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(650)};
            Point origin=new Point();bool pressed=false;
            void Open(){timer.Stop();if(!pressed)return;pressed=false;if(widget!=null&&(!widget.Visible||widget.GameBarDisplayMode!=Microsoft.Gaming.XboxGameBar.XboxGameBarDisplayMode.Foreground))return;_onexHeldTiles[button]=DateTimeOffset.UtcNow;_=ShowOnexTileDetailsAsync(button,id);}
            timer.Tick+=(s,e)=>Open();
            button.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((s,e)=>{origin=e.GetCurrentPoint(button).Position;pressed=true;timer.Start();}),true);
            button.AddHandler(UIElement.PointerMovedEvent,new PointerEventHandler((s,e)=>{var p=e.GetCurrentPoint(button).Position;if(Math.Abs(p.X-origin.X)>10||Math.Abs(p.Y-origin.Y)>10){pressed=false;timer.Stop();}}),true);
            PointerEventHandler release=(s,e)=>{pressed=false;timer.Stop();};
            button.AddHandler(UIElement.PointerReleasedEvent,release,true);button.AddHandler(UIElement.PointerCanceledEvent,release,true);button.AddHandler(UIElement.PointerCaptureLostEvent,release,true);
            button.RightTapped+=(s,e)=>{pressed=false;timer.Stop();_onexHeldTiles[button]=DateTimeOffset.UtcNow;_=ShowOnexTileDetailsAsync(button,id);e.Handled=true;};
            button.KeyDown+=(s,e)=>{
                if(e.Key==VirtualKey.GamepadX||e.Key==VirtualKey.Application||(e.Key==VirtualKey.F10&&(Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift)&Windows.UI.Core.CoreVirtualKeyStates.Down)!=0))
                {pressed=false;timer.Stop();_=ShowOnexTileDetailsAsync(button,id);e.Handled=true;}
                else if((e.Key==VirtualKey.GamepadA||e.Key==VirtualKey.Space||e.Key==VirtualKey.Enter)&&!pressed){pressed=true;timer.Start();}
            };
            button.KeyUp+=(s,e)=>{pressed=false;timer.Stop();};
            button.Unloaded+=(s,e)=>{pressed=false;timer.Stop();_onexHeldTiles.Remove(button);};
        }
        private bool SuppressOnexHeldClick(Button button)
        {
            if(!_onexHeldTiles.TryGetValue(button,out var at))return false;
            _onexHeldTiles.Remove(button);return DateTimeOffset.UtcNow-at<TimeSpan.FromSeconds(3);
        }
        private async System.Threading.Tasks.Task ShowOnexTileDetailsAsync(Button anchor,string id)
        {
            if(_onexDetailsDialog!=null)return;
            if(id=="Profile"){await ShowOnexPerformanceTileDialogAsync(anchor);return;}
            if(id=="OnexControllerProfile"){await ShowOnexControllerHomeDialogAsync(anchor);return;}
            if(id=="OnexInputMode"){OpenOnexHomeInputModeSettings();return;}
            if(id=="ReShade"){await ShowOnexScreenTileDialogAsync(anchor);return;}
            if(id=="OnexFanMode"){await ShowOnexFanTileDialogAsync(anchor);return;}
            if(id=="Overlay"){await ShowOnexOverlayTileDialogAsync(anchor);return;}
            if(id=="ChargeLimiter"){await ShowOnexChargeDetailsAsync(anchor);return;}
            await ShowOnexHardwareDetailsAsync(anchor,id);
        }
    }
}
