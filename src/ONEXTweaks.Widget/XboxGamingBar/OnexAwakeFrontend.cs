// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private JsonObject _onexAwakeState;
        private bool _onexAwakeBusy;
        private TextBlock _onexAwakeMenuStatus;
        private ComboBox _onexAwakeHours,_onexAwakeMinutes;
        private bool OnexAwakeOn=>_onexAwakeState?.GetNamedBoolean("on",false)==true;
        private bool OnexAwakeReady=>App.IsConnected&&!_onexAwakeBusy&&_onexAwakeState?.GetNamedBoolean("available",false)==true;
        private string OnexAwakeCaption
        {
            get
            {
                if(_onexAwakeBusy)return OnexStrings.Get("Applying…");
                if(!OnexAwakeOn)return OnexStrings.Get("Off");
                if(_onexAwakeState.TryGetValue("remainingSeconds",out var seconds)&&seconds.ValueType==JsonValueType.Number)
                {int minutes=(int)Math.Ceiling(seconds.GetNumber()/60);return OnexStrings.Format("On · {0}:{1:00}h",minutes/60,minutes%60);}
                return OnexStrings.Get("On");
            }
        }
        private void ApplyOnexAwakeState(string json)
        {
            if(!JsonObject.TryParse(json,out var state))return;_onexAwakeState=state;
            if(qsTileMap.TryGetValue("OnexAwake",out var tile))
            {SetTileActionable(tile,OnexAwakeReady);ApplyOnexAwakeTile(tile);ApplyOnexTileCaption(tile,OnexAwakeReady);}
            if(_onexAwakeMenuStatus!=null)_onexAwakeMenuStatus.Text=OnexAwakeCaption;
        }
        private void ApplyOnexAwakeTile(TileDefinition tile)
        {
            if(tile.StateText==null)return;
            bool available=_onexAwakeState?.GetNamedBoolean("available",false)==true;
            tile.StateText.Text=available?OnexAwakeCaption:global::XboxGamingBar.OnexStrings.Get("Unavailable");tile.StateText.MaxLines=1;tile.StateText.TextWrapping=TextWrapping.NoWrap;
            ApplyOnexToggleTileVisual(tile,OnexAwakeOn,available);UpdateTileScrollAnimation(tile);
            if(tile.TileButton!=null)ToolTipService.SetToolTip(tile.TileButton,global::XboxGamingBar.OnexStrings.Format("Amphetamine · {0}\nТап: On/Off без опций · Удержание / X: активация с настройками",tile.StateText.Text));
        }
        private async Task<bool> SetOnexAwakeAsync(bool enabled,JsonObject settings=null,bool simple=false)
        {
            if(!OnexAwakeReady)return false;_onexAwakeBusy=true;ApplyOnexTileAvailability();
            try
            {
                var value=new JsonObject{{"enabled",JsonValue.CreateBooleanValue(enabled)},{"simple",JsonValue.CreateBooleanValue(simple)},{"settings",settings??_onexAwakeState.GetNamedObject("settings")}};
                var reply=await SendOnexHomeRequestAsync(new ValueSet{{"OnexAwakeSet",value.Stringify()}});ApplyOnexAwakeState((string)reply["OnexAwakeState"]);return true;
            }
            catch(Exception error){ShowOnexDesktopStatus(error.Message);return false;}
            finally{_onexAwakeBusy=false;ApplyOnexTileAvailability();}
        }
        private void InsertOnexAwakeTile()
        {
            var values=ApplicationData.Current.LocalSettings.Values;
            if(values.TryGetValue("OnexAwakeTileInstalledV1",out var installed)&&installed is bool done&&done)return;
            if(!qsTileMap.TryGetValue("OnexAwake",out var awake))return;
            var backup=new JsonObject();
            foreach(var p in values.Where(p=>IsOnexLayoutSetting(p.Key)))backup[p.Key]=p.Value is bool b?JsonValue.CreateBooleanValue(b):p.Value is int n?JsonValue.CreateNumberValue(n):JsonValue.CreateStringValue(p.Value.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path,"onex-before-awake-layout-"+DateTime.UtcNow.Ticks+".json"),backup.Stringify());
            var ordered=qsTileDefinitions.Where(t=>t.Id!="OnexAwake").OrderBy(t=>t.Order).ToList();
            int power=ordered.FindIndex(t=>t.Id=="Power");ordered.Insert(power<0?ordered.Count:power,awake);awake.IsVisible=true;
            for(int i=0;i<ordered.Count;i++)ordered[i].Order=i;
            SaveQuickSettingsConfig();
            values["OnexAwakeTileInstalledV1"]=true;
        }
        private async Task ShowOnexAwakeSettingsAsync(Button anchor)
        {
            if(_onexDetailsDialog!=null)return;
            var reply=await App.SendMessageAsync(new ValueSet{{"OnexAwakeGet",true}});
            if(reply==null||!reply.TryGetValue("OnexAwakeState",out var json))return;ApplyOnexAwakeState((string)json);
            var settings=_onexAwakeState.GetNamedObject("settings");int duration=(int)settings.GetNamedNumber("durationMinutes");
            var panel=new StackPanel{Spacing=12};_onexAwakeMenuStatus=new TextBlock{Text=OnexAwakeCaption,FontSize=18,Foreground=OnexDialogAccent};panel.Children.Add(_onexAwakeMenuStatus);
            _onexAwakeHours=OnexSettingsChoice(global::XboxGamingBar.OnexStrings.Get("Часы"));_onexAwakeMinutes=OnexSettingsChoice(global::XboxGamingBar.OnexStrings.Get("Минуты"));
            for(int i=0;i<100;i++)_onexAwakeHours.Items.Add(i.ToString("00"));for(int i=0;i<60;i++)_onexAwakeMinutes.Items.Add(i.ToString("00"));
            _onexAwakeHours.SelectedIndex=duration/60;_onexAwakeMinutes.SelectedIndex=duration%60;
            panel.Children.Add(OnexSettingsColumns(_onexAwakeHours,_onexAwakeMinutes));
            var screen=new CheckBox{Content=global::XboxGamingBar.OnexStrings.Get("Уменьшить яркость до 0%"),IsChecked=settings.GetNamedBoolean("screenOff")};
            var shutdown=new CheckBox{Content=global::XboxGamingBar.OnexStrings.Get("Выключить консоль после истечения таймера"),IsChecked=settings.GetNamedBoolean("shutdownAtEnd")};
            ApplyOnexTileWindowControlAccent(screen);ApplyOnexTileWindowControlAccent(shutdown);panel.Children.Add(screen);panel.Children.Add(shutdown);
            panel.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Ручной сон кнопкой питания или закрытием крышки отключает режим. При заряде 10% или ниже режим выключается автоматически."),TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=OnexSettingsBrush(170,181,193)});
            panel.Children.Add(OnexHelpHeader(global::XboxGamingBar.OnexStrings.Get("Как вернуть яркость?"),global::XboxGamingBar.OnexStrings.Get("Если выбрать уменьшение яркости и нажать «Активировать», яркость снизится до 0%, а значок у ползунка станет перечёркнутым. Нажми на него, чтобы вернуть прежнюю яркость: Amphetamine продолжит работать. При отключении режима или окончании таймера яркость тоже восстановится, если ты ещё не изменил её сам. Обычный тап по плитке включает режим без таймера и затемнения.")));
            var error=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=OnexSettingsBrush(255,110,110),FontSize=12};panel.Children.Add(error);
            var dialog=CreateOnexSettingsDialog(global::XboxGamingBar.OnexStrings.Get("Amphetamine"),global::XboxGamingBar.OnexStrings.Get("«Активировать» запускает режим с выбранными настройками. 0:00 — без таймера. Повторная активация начинает отсчёт заново."),panel);dialog.PrimaryButtonText=global::XboxGamingBar.OnexStrings.Get("Активировать");dialog.CloseButtonText=global::XboxGamingBar.OnexStrings.Get("Закрыть");_onexDetailsDialog=dialog;
            dialog.PrimaryButtonClick+=async(s,e)=>
            {
                int minutes=_onexAwakeHours.SelectedIndex*60+_onexAwakeMinutes.SelectedIndex;
                if(shutdown.IsChecked==true&&minutes==0){error.Text=global::XboxGamingBar.OnexStrings.Get("Для выключения консоли выбери время таймера.");e.Cancel=true;return;}
                var deferral=e.GetDeferral();
                try{e.Cancel=!await SetOnexAwakeAsync(true,new JsonObject{{"durationMinutes",JsonValue.CreateNumberValue(minutes)},{"screenOff",JsonValue.CreateBooleanValue(screen.IsChecked==true)},{"shutdownAtEnd",JsonValue.CreateBooleanValue(shutdown.IsChecked==true)}});}
                finally{deferral.Complete();}
            };
            try{await dialog.ShowAsync();}
            finally{if(ReferenceEquals(_onexDetailsDialog,dialog))_onexDetailsDialog=null;_onexAwakeMenuStatus=null;_onexAwakeHours=null;_onexAwakeMinutes=null;}
        }
    }
}
