// SPDX-License-Identifier: AGPL-3.0-only
using System;
using ONEXTweaks.Power;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using XboxGamingBar.QuickSettings;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private JsonObject _onexWindowsPower,_onexHardwareHome;
        private bool _onexFanBusy,_onexLightingBusy,_onexPowerModeBusy,_onexPowerModeUiUpdating;
        private bool _onexHomeBusy=>_onexFanBusy||_onexLightingBusy;
        private bool OnexWindowsPowerReady=>_onexWindowsPower?.GetNamedBoolean("available",false)==true;
        private void ApplyOnexWindowsPowerState(string json)
        {
            if(JsonObject.TryParse(json,out var state))_onexWindowsPower=state;else _onexWindowsPower=null;
            SyncOnexPowerModeControl();SyncOnexBoostFromWindowsPower();ApplyOnexAvailability();
        }
        private void SyncOnexPowerModeControl()
        {
            if(OSPowerModeComboBox==null||!OnexWindowsPowerReady)return;
            _onexPowerModeUiUpdating=true;
            try{int mode=(int)_onexWindowsPower.GetNamedNumber(_onexWindowsPower.GetNamedBoolean("onAc",false)?"modeAc":"modeDc",1);osPowerMode?.SetValueSilent(mode);OSPowerModeComboBox.SelectedIndex=mode;OSPowerModeValue.Text=OSPowerModeNames[mode];}
            finally{_onexPowerModeUiUpdating=false;}
        }
        private void ApplyOnexHardwareHomeState(string json)
        {
            if(JsonObject.TryParse(json,out var state))_onexHardwareHome=state;else _onexHardwareHome=null;
            RefreshOnexLightingPage();
            if(!_onexNavigationPerfCapture){ApplyOnexTileAvailability();RefreshOnexFanHardwareUi();return;}
            MeasureOnexNavigationWork("hardware-home-tiles",_releaseHome,ApplyOnexTileAvailability);
            MeasureOnexNavigationWork("hardware-fan-live",_onexFanProfilePickerCard,RefreshOnexFanHardwareUi);
        }
        private bool TryOnexHomeAction(Button button,string id)
        {
            if(!qsTileMap.TryGetValue(id,out var tile))return false;
            button=tile.TileButton??button;
            if(!tile.IsActionable)return true;
            if(id=="OnexBluetooth"||tile.ActionType==TileActionType.ToggleBluetooth){_=SetOnexBluetoothRadioAsync(!OnexBluetoothOn);return true;}
            if(id=="OnexAirplane"||tile.ActionType==TileActionType.ToggleAirplane){_=SetOnexAirplaneAsync(!OnexAirplaneOn);return true;}
            if(id=="OnexResolution"){_=CycleOnexResolutionAsync();return true;}
            if(id=="OnexRefreshRate"){_=CycleOnexRefreshRateAsync();return true;}
            if(id=="OnexAwake"){_=SetOnexAwakeAsync(!OnexAwakeOn,simple:true);return true;}
            if(id=="OnexWifi"||tile.ActionType==TileActionType.ToggleWifi){_=ToggleOnexWifiAsync();return true;}
            if(id=="OnexFse"||tile.ActionType==TileActionType.ToggleFse){_=InvokeOnexFseAsync();return true;}
            if(id=="Profile"){_=ShowOnexPerformanceTileMenuAsync(button);return true;}
            if(id=="OnexControllerProfile"){_=ShowOnexControllerHomeMenuAsync(button);return true;}
            if(id=="OnexInputMode"){_=ToggleOnexHomeInputModeAsync();return true;}
            if(id=="FPSCombined"){ToggleOnexFps();return true;}
            if(id=="Overlay"){_=ShowOnexOverlayTileMenuAsync(button);return true;}
            if(id=="PowerMode"){_=CycleOnexPowerModeAsync();return true;}
            if(id=="Power"){ShowOnexPowerMenu(button);return true;}
            if(id=="OnexRotationLock"){_=ToggleOnexRotationLockAsync();return true;}
            if(id=="Fullscreen"){_=InvokeOnexDesktopActionAsync("fullscreen",_onexTargetToken);return true;}
            if(id=="ReShade"){_=ShowOnexScreenTileMenuAsync(button);return true;}
            if(id=="OptiScaler"){_=InvokeOnexDesktopActionAsync("optiScaler",_onexTargetToken);return true;}
            if(id=="OnexFanMode"){_=ShowOnexFanTileMenuAsync(button);return true;}
            if(id=="LedToggle"){_=ToggleOnexLightingAsync();return true;}
            if(!string.IsNullOrWhiteSpace(tile.CustomShortcut)){_=InvokeOnexShortcutAsync(tile.CustomShortcut);return true;}
            var action=tile.ActionType;
            if(action==TileActionType.CycleOverlayMode){_=SetOnexOverlayLevelAsync((OnexOverlayLevel+1)%5);return true;}
            if(action==TileActionType.CycleLimiterMode){ToggleOnexFps();return true;}
            string command=action==TileActionType.OpenChrome?"chrome":action==TileActionType.SteamBpmSteamMenu?"steamMenu":action==TileActionType.SteamBpmQuickAccess?"steamQuickAccess":action==TileActionType.SteamGameOverlay?"steamOverlay":action==TileActionType.SteamMenuContextual?"steamContext":action==TileActionType.EmulateXboxGuide?"xboxGuide":action==TileActionType.AltTab?"altTab":action==TileActionType.AltTabBack?"altTabBack":action==TileActionType.GoToDesktop?"desktop":action==TileActionType.ShowKeyboard?"keyboard":action==TileActionType.MediaNextTrack?"mediaNext":action==TileActionType.MediaPrevTrack?"mediaPrevious":action==TileActionType.MediaPlayPause?"mediaPlayPause":null;
            if(command!=null){_=InvokeOnexDesktopActionAsync(command);return true;}
            if(action==TileActionType.BrightnessUp||action==TileActionType.BrightnessDown){RailBrightnessSlider.Value=Math.Max(0,Math.Min(100,RailBrightnessSlider.Value+(action==TileActionType.BrightnessUp?5:-5)));return true;}
            if(action==TileActionType.VolumeUp||action==TileActionType.VolumeDown){RailVolumeSlider.Value=Math.Max(0,Math.Min(100,RailVolumeSlider.Value+(action==TileActionType.VolumeUp?5:-5)));return true;}
            if(action==TileActionType.TDPIncrBy1W||action==TileActionType.TDPDecrBy1W||action==TileActionType.TDPStepUp||action==TileActionType.TDPStepDown)
            {int delta=action==TileActionType.TDPIncrBy1W||action==TileActionType.TDPStepUp?1:-1;int next=Math.Max(8,Math.Min(40,_releasePl1+delta));_=SetOnexReleasePowerAsync(next,_releaseHome.SeparatePl2Enabled ? OnexReleaseHome.ClampManualPl2(next,_releasePl2+delta) : Pl2ControlPolicy.Automatic(next));return true;}
            if(action==TileActionType.LaunchUserProgram||action==TileActionType.OpenUserWebsite){_=InvokeOnexLaunchAsync(tile.ActionParam);return true;}
            string uri=action==TileActionType.SteamBigPicture?"steam://open/bigpicture":action==TileActionType.XboxApp?"msxbox://":action==TileActionType.OpenWindowsStore?"ms-windows-store://home":action==TileActionType.OpenDefaultBrowser||action==TileActionType.OpenGoogle?"https://www.google.com":action==TileActionType.OpenSpotify?"spotify:":action==TileActionType.OpenYouTube?"https://www.youtube.com":action==TileActionType.OpenRetroAchievements?"https://retroachievements.org":action==TileActionType.OpenExophase?"https://www.exophase.com":null;
            if(uri!=null){_=InvokeOnexLaunchAsync(uri);return true;}
            return false;
        }
        private bool IsOnexCustomActionSupported(TileDefinition tile)
        {
            if(!string.IsNullOrWhiteSpace(tile.CustomShortcut))return true;
            var a=tile.ActionType;
            return a==TileActionType.ToggleAirplane||a==TileActionType.ToggleBluetooth||a==TileActionType.ToggleWifi||a==TileActionType.ToggleFse||a==TileActionType.OpenChrome||a==TileActionType.SteamBpmSteamMenu||a==TileActionType.SteamBpmQuickAccess||a==TileActionType.SteamGameOverlay||a==TileActionType.SteamMenuContextual||a==TileActionType.EmulateXboxGuide||a==TileActionType.CycleOverlayMode||a==TileActionType.CycleLimiterMode||a==TileActionType.AltTab||a==TileActionType.AltTabBack||a==TileActionType.GoToDesktop||a==TileActionType.ShowKeyboard||
              a==TileActionType.MediaNextTrack||a==TileActionType.MediaPrevTrack||a==TileActionType.MediaPlayPause||a==TileActionType.BrightnessUp||a==TileActionType.BrightnessDown||a==TileActionType.VolumeUp||a==TileActionType.VolumeDown||
              a==TileActionType.TDPIncrBy1W||a==TileActionType.TDPDecrBy1W||a==TileActionType.TDPStepUp||a==TileActionType.TDPStepDown||a==TileActionType.LaunchUserProgram||a==TileActionType.OpenUserWebsite||
              a==TileActionType.SteamBigPicture||a==TileActionType.XboxApp||a==TileActionType.OpenWindowsStore||a==TileActionType.OpenDefaultBrowser||a==TileActionType.OpenGoogle||a==TileActionType.OpenSpotify||a==TileActionType.OpenYouTube||a==TileActionType.OpenExophase||a==TileActionType.OpenRetroAchievements;
        }
        private async Task<ValueSet> SendOnexHomeRequestAsync(ValueSet request)
        {
            request["OnexUserInput"]=true;request["OnexPreferActiveProfile"]=true;
            var reply=await App.SendMessageAsync(request);
            if(reply==null)throw new InvalidOperationException("ONEXTweaks disconnected");
            if(reply.TryGetValue("Error",out var error))throw new InvalidOperationException(error.ToString());
            return reply;
        }
        private async Task InvokeOnexShortcutAsync(string shortcut)
        {try{await SendOnexHomeRequestAsync(new ValueSet{["OnexDesktopAction"]="shortcut",["Shortcut"]=shortcut,["TargetToken"]=_onexTargetToken??"",["InGameBar"]=widget!=null});}catch(Exception e){ShowOnexDesktopStatus(e.Message);}}
        private async Task InvokeOnexLaunchAsync(string path)
        {try{await SendOnexHomeRequestAsync(new ValueSet{["OnexDesktopAction"]="launch",["LaunchTarget"]=path??"",["InGameBar"]=widget!=null});}catch(Exception e){ShowOnexDesktopStatus(e.Message);}}
        private void OpenOnexScreenColorProfile()
        {
            CloseOnexMenus();DisplayNavItem.IsChecked=true;
            DisplayProfileModeBadge?.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false,VerticalAlignmentRatio=0});
        }
        private async Task CycleOnexPowerModeAsync()
        {
            if(_onexPowerModeBusy||!OnexWindowsPowerReady)return;
            int current=(int)_onexWindowsPower.GetNamedNumber(_onexWindowsPower.GetNamedBoolean("onAc",false)?"modeAc":"modeDc",1);
            await SetOnexPowerModeAsync((current+1)%3);
        }
        private async Task SetOnexPowerModeAsync(int mode)
        {
            if(_onexPowerModeBusy)return;_onexPowerModeBusy=true;ApplyOnexAvailability();
            try{var r=await SendOnexHomeRequestAsync(new ValueSet{["OnexWindowsPowerMode"]=mode});ApplyOnexWindowsPowerState((string)r["OnexWindowsPowerState"]);}
            catch(Exception ex){ShowOnexDesktopStatus(ex.Message);}
            finally{_onexPowerModeBusy=false;SyncOnexPowerModeControl();ApplyOnexAvailability();}
        }
        private MenuFlyout ShowOnexPowerModeMenu(Button anchor)
        {
            var menu=CreateOnexMenu();string[] names={OnexStrings.Get("Best efficiency"),"Balanced",OnexStrings.Get("Best performance")};
            int active=(int)(_onexWindowsPower?.GetNamedNumber(_onexWindowsPower.GetNamedBoolean("onAc",false)?"modeAc":"modeDc",1)??1);
            for(int i=0;i<names.Length;i++){int mode=i;var item=new MenuFlyoutItem{Text=OnexStrings.Get(names[i])};if(i==active)item.Icon=new SymbolIcon(Symbol.Accept);item.Click+=async(s,e)=>await SetOnexPowerModeAsync(mode);menu.Items.Add(item);}
            menu.ShowAt(anchor);return menu;
        }
        private MenuFlyout ShowOnexPowerMenu(Button anchor)
        {
            var menu=CreateOnexMenu(FlyoutPlacementMode.Top);
            string[] names={OnexStrings.Get("Power Off"),"Reboot","Sleep","Hibernate",OnexStrings.Get("Reboot to BIOS")},actions={"poweroff","reboot","sleep","hibernate","bios"};
            for(int i=0;i<names.Length;i++){string action=actions[i];var item=new MenuFlyoutItem{Text=OnexStrings.Get(names[i])};item.Click+=async(s,e)=>{try{await SendOnexHomeRequestAsync(new ValueSet{["OnexSystemPowerAction"]=action,["InGameBar"]=widget!=null});}catch(Exception ex){ShowOnexDesktopStatus(ex.Message);}};menu.Items.Add(item);}
            menu.ShowAt(anchor);return menu;
        }
        private async Task ShowOnexHardwareDetailsAsync(Button anchor,string id)
        {
            if(id=="OnexResolution"){await ShowOnexResolutionSettingsAsync(anchor);return;}
            if(id=="OnexRefreshRate"){await ShowOnexRefreshRateSettingsAsync(anchor);return;}
            if(id=="OnexBluetooth"){await ShowOnexBluetoothWindowAsync(anchor);return;}
            if(id=="OnexWifi"){await ShowOnexWifiWindowAsync(anchor);return;}
            if(id=="OnexAwake"){await ShowOnexAwakeSettingsAsync(anchor);return;}
            if(id=="OnexFse"){ShowOnexFseMenu(anchor);return;}
            if(id=="Overlay"){await ShowOnexOverlayTileDialogAsync(anchor);return;}
            if(id=="FPSCombined"){ShowOnexFpsMenu(anchor);return;}
            if(id=="CPUBoost"){await ShowOnexCpuSettingsAsync(anchor);return;}
            if(id=="PowerMode"){ShowOnexPowerModeMenu(anchor);return;}
            if(id=="OnexFanMode"){_=ShowOnexFanTileMenuAsync(anchor,temporary:true);return;}
            if(id=="LedToggle"){await ShowOnexLightingSettingsAsync(anchor);return;}
        }
        private async Task ShowOnexCpuSettingsAsync(Button anchor)
        {
            try
            {
                var form=await CreateOnexCpuSettingsFormAsync();var dialog=CreateOnexSettingsDialog(global::XboxGamingBar.OnexStrings.Get("CPU Boost"),global::XboxGamingBar.OnexStrings.Get("Separate policies for plugged-in and battery use."),form.Panel);_onexDetailsDialog=dialog;
                if(await dialog.ShowAsync()==ContentDialogResult.Primary)await form.Apply();
            }
            catch(Exception error){ShowOnexDesktopStatus(error.Message);}
            finally{_onexDetailsDialog=null;anchor.Focus(FocusState.Programmatic);}
        }
    }
}
