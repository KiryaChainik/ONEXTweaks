// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private sealed class MixerRow
        {internal string Token;internal uint Pid;internal Slider Slider;internal Button Mute;internal FontIcon Icon;internal TextBlock Value;internal FrameworkElement Card;}
        private readonly Dictionary<string,MixerRow> _onexMixerRows=new Dictionary<string,MixerRow>();
        private readonly Dictionary<string,int> _onexMixerPending=new Dictionary<string,int>();
        private readonly System.Threading.SemaphoreSlim _onexMixerWriteGate=new System.Threading.SemaphoreSlim(1,1);
        private bool _onexMixerOpen,_onexMixerUpdating,_onexMixerReadBusy,_onexMixerFlushBusy;
        private int _onexMixerGeneration;
        private DispatcherTimer _onexMixerWriteTimer;
        private StackPanel _onexMixerApps;
        private TextBlock _onexMixerStatus,_onexMixerEmpty;
        private JsonObject _onexMixerState;
        private MixerRow CreateOnexMixerRow(string token,string name,uint pid=0)
        {
            var panel=new StackPanel{Spacing=4};panel.Children.Add(new TextBlock{Text=name,FontSize=14,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxLines=1});
            var line=new Grid{ColumnSpacing=8};line.ColumnDefinitions.Add(new ColumnDefinition());line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(36)});line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(32)});
            var slider=new Slider{Minimum=0,Maximum=100,StepFrequency=1,SmallChange=5,LargeChange=5,Style=(Style)_releaseHome.Resources["LightMediaSliderStyle"],Foreground=new SolidColorBrush(OnexPalette.Cyan),MinWidth=80};
            var value=new TextBlock{Text="—",FontSize=14,Width=36,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Left};
            var icon=new FontIcon{FontFamily=new FontFamily("Segoe Fluent Icons"),Glyph="\uE767",FontSize=16,Foreground=new SolidColorBrush(OnexPalette.Cyan)};
            var mute=new Button{Content=icon,Width=32,Height=32,MinWidth=0,MinHeight=0,Padding=new Thickness(0),Background=new SolidColorBrush(Colors.Transparent),BorderThickness=new Thickness(0)};
            line.Children.Add(slider);Grid.SetColumn(value,1);line.Children.Add(value);Grid.SetColumn(mute,2);line.Children.Add(mute);panel.Children.Add(line);
            var row=new MixerRow{Token=token,Pid=pid,Slider=slider,Value=value,Mute=mute,Icon=icon,Card=OnexSettingsCard(panel)};
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(slider,global::XboxGamingBar.OnexStrings.Format("Громкость: {0}",name));Windows.UI.Xaml.Automation.AutomationProperties.SetName(mute,global::XboxGamingBar.OnexStrings.Format("Включить/выключить звук: {0}",name));
            OnexInputGuard.ConfigurePercentage(slider);
            void QueueVolume(){if(_onexMixerUpdating||!_onexMixerOpen)return;_onexMixerPending[token]=(int)Math.Round(slider.Value);_onexMixerWriteTimer.Stop();_onexMixerWriteTimer.Start();}
            OnexInputGuard.SetCommit(slider,QueueVolume);
            slider.ValueChanged+=(sender,args)=>{row.Value.Text=((int)Math.Round(slider.Value))+"%";if(!OnexInputGuard.IsPreviewing(slider))QueueVolume();};
            mute.Click+=async(sender,args)=>{mute.IsEnabled=false;try{await FlushOnexMixerAsync();await SetOnexMixerAsync(token,null,true);}finally{mute.IsEnabled=true;}};
            return row;
        }
        private void ApplyOnexMixerRow(MixerRow row,double volume,bool muted,bool available=true)
        {
            if(!_onexMixerPending.ContainsKey(row.Token)&&!OnexInputGuard.IsEditing(row.Slider))row.Slider.Value=Math.Max(0,Math.Min(100,volume));
            row.Slider.IsEnabled=available;row.Mute.IsEnabled=available;row.Value.Text=available?((int)Math.Round(row.Slider.Value))+"%":"—";
            row.Slider.Opacity=muted ? 0.5 : 1;row.Icon.Glyph=muted?"\uE74F":"\uE767";row.Icon.Foreground=muted?OnexSettingsBrush(255,107,107):new SolidColorBrush(OnexPalette.Cyan);
            ToolTipService.SetToolTip(row.Mute,muted?global::XboxGamingBar.OnexStrings.Get("Включить звук"):global::XboxGamingBar.OnexStrings.Get("Выключить звук"));
        }
        private void ApplyOnexAudioMixerState(string json)
        {
            if(!_onexMixerOpen||!JsonObject.TryParse(json,out var state))return;_onexMixerState=state;_onexMixerUpdating=true;
            try
            {
                var master=state.GetNamedObject("master");ApplyOnexMediaLevels(master.Stringify());bool masterReady=master.GetNamedBoolean("volumeAvailable",false);
                ApplyOnexMixerRow(_onexMixerRows["master"],masterReady?master.GetNamedNumber("volume"):0,masterReady&&master.GetNamedBoolean("muted"),masterReady);
                var present=new HashSet<string>{"master"};
                foreach(var entry in state.GetNamedArray("apps",new JsonArray()))
                {
                    var app=entry.GetObject();string token=app.GetNamedString("token");present.Add(token);
                    if(!_onexMixerRows.TryGetValue(token,out var row)){row=CreateOnexMixerRow(token,app.GetNamedString("name"),(uint)app.GetNamedNumber("pid",0));_onexMixerRows[token]=row;_onexMixerApps.Children.Add(row.Card);}
                    ApplyOnexMixerRow(row,app.GetNamedNumber("volume"),app.GetNamedBoolean("muted"));
                }
                foreach(var key in _onexMixerRows.Keys.Where(k=>!present.Contains(k)).ToArray()){_onexMixerApps.Children.Remove(_onexMixerRows[key].Card);_onexMixerRows.Remove(key);_onexMixerPending.Remove(key);}
                _onexMixerEmpty.Visibility=present.Count==1?Visibility.Visible:Visibility.Collapsed;
                _onexMixerStatus.Text=state.TryGetValue("error",out var error)&&error.ValueType==JsonValueType.String?error.GetString():"";
                _onexMixerStatus.Visibility=string.IsNullOrEmpty(_onexMixerStatus.Text)?Visibility.Collapsed:Visibility.Visible;
            }
            finally{_onexMixerUpdating=false;}
        }
        private async Task RefreshOnexMixerAsync()
        {
            if(!_onexMixerOpen||_onexMixerReadBusy)return;_onexMixerReadBusy=true;int generation=_onexMixerGeneration;
            await _onexMixerWriteGate.WaitAsync();
            try{if(!_onexMixerOpen||generation!=_onexMixerGeneration)return;var reply=await App.SendMessageAsync(new ValueSet{{"OnexAudioMixerGet",true}});if(reply==null||reply.ContainsKey("Error"))throw new InvalidOperationException(reply?.ContainsKey("Error")==true?reply["Error"].ToString():global::XboxGamingBar.OnexStrings.Get("Нет соединения с помощником"));if(_onexMixerOpen&&generation==_onexMixerGeneration)ApplyOnexAudioMixerState((string)reply["OnexAudioMixerState"]);}
            catch(Exception error){if(_onexMixerOpen&&generation==_onexMixerGeneration){_onexMixerStatus.Text=error.Message;_onexMixerStatus.Visibility=Visibility.Visible;}}
            finally{_onexMixerWriteGate.Release();_onexMixerReadBusy=false;}
        }
        private async Task SetOnexMixerAsync(string token,int? volume,bool toggle)
        {
            await _onexMixerWriteGate.WaitAsync();int generation=_onexMixerGeneration;
            try
            {
                var edit=new JsonObject{{"token",JsonValue.CreateStringValue(token)}};if(volume.HasValue)edit["volume"]=JsonValue.CreateNumberValue(volume.Value);else edit["toggleMute"]=JsonValue.CreateBooleanValue(toggle);
                var reply=await App.SendMessageAsync(new ValueSet{{"OnexAudioMixerSet",edit.Stringify()},{"OnexMediaUserInput",true}});
                if(reply==null||reply.ContainsKey("Error"))throw new InvalidOperationException(reply?.ContainsKey("Error")==true?reply["Error"].ToString():global::XboxGamingBar.OnexStrings.Get("Нет соединения с помощником"));
                if(_onexMixerOpen&&generation==_onexMixerGeneration)ApplyOnexAudioMixerState((string)reply["OnexAudioMixerState"]);
            }
            catch(Exception error){if(_onexMixerOpen){_onexMixerStatus.Text=error.Message;_onexMixerStatus.Visibility=Visibility.Visible;}}
            finally{_onexMixerWriteGate.Release();}
        }
        private async Task FlushOnexMixerAsync()
        {
            while(_onexMixerFlushBusy)await Task.Delay(10);_onexMixerFlushBusy=true;_onexMixerWriteTimer.Stop();
            try{var pending=_onexMixerPending.ToArray();_onexMixerPending.Clear();foreach(var item in pending)await SetOnexMixerAsync(item.Key,item.Value,false);}
            finally{_onexMixerFlushBusy=false;if(_onexMixerPending.Count>0&&_onexMixerOpen)_onexMixerWriteTimer.Start();}
        }
        private async Task ShowOnexAudioMixerAsync(Button anchor)
        {
            if(_onexDetailsDialog!=null||_onexMixerOpen)return;_onexMixerOpen=true;_onexMixerGeneration++;_onexMixerRows.Clear();_onexMixerPending.Clear();
            _onexMixerWriteTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(140)};_onexMixerWriteTimer.Tick+=async(s,e)=>await FlushOnexMixerAsync();
            var panel=new StackPanel{Spacing=12};var master=CreateOnexMixerRow("master",global::XboxGamingBar.OnexStrings.Get("Общая громкость"));master.Slider.IsEnabled=false;_onexMixerRows["master"]=master;panel.Children.Add(master.Card);
            panel.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Приложения"),FontSize=15,FontWeight=FontWeights.SemiBold});
            _onexMixerApps=new StackPanel{Spacing=8};_onexMixerEmpty=new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Запусти звук в игре или приложении — оно появится здесь."),FontSize=13,TextWrapping=TextWrapping.Wrap,Foreground=OnexSettingsBrush(170,181,193)};
            var list=new StackPanel{Spacing=8};list.Children.Add(_onexMixerEmpty);list.Children.Add(_onexMixerApps);panel.Children.Add(new ScrollViewer{Content=list,MaxHeight=Math.Max(120,ActualHeight-360),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
            _onexMixerStatus=new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Загрузка…"),TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=OnexSettingsBrush(255,150,120)};panel.Children.Add(_onexMixerStatus);
            var dialog=CreateOnexSettingsDialog(global::XboxGamingBar.OnexStrings.Get("Микшер громкости"),global::XboxGamingBar.OnexStrings.Get("Громкость и мьют отдельно для каждого приложения. Изменения применяются сразу."),panel);dialog.PrimaryButtonText="";dialog.CloseButtonText=global::XboxGamingBar.OnexStrings.Get("Закрыть");dialog.DefaultButton=ContentDialogButton.None;_onexDetailsDialog=dialog;
            var refresh=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};refresh.Tick+=async(s,e)=>await RefreshOnexMixerAsync();
            try{var shown=dialog.ShowAsync();await RefreshOnexMixerAsync();refresh.Start();await shown;}
            finally{refresh.Stop();_onexMixerWriteTimer.Stop();await FlushOnexMixerAsync();_onexMixerOpen=false;if(_onexDetailsDialog==dialog)_onexDetailsDialog=null;_onexMixerRows.Clear();anchor.Focus(FocusState.Programmatic);}
        }
    }
}
