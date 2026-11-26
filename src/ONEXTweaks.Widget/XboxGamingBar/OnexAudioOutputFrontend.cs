// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private ListView _onexAudioOutputList;
        private Button _onexAudioOutputApply;
        private TextBlock _onexAudioOutputStatus;
        private JsonObject _onexAudioOutputState;
        private bool _onexAudioOutputOpen,_onexAudioOutputReading,_onexAudioOutputWriting;
        private int _onexAudioOutputGeneration;
        private string _onexAudioOutputSignature;
        private int _onexAudioDeviceFlow;
        private readonly System.Threading.SemaphoreSlim _onexAudioOutputGate=new System.Threading.SemaphoreSlim(1,1);
        private void WireOnexAudioOutputHold(){WireOnexAudioDeviceHold(RailMuteButton,0);WireOnexAudioDeviceHold(RailMicButton,1);}
        private void WireOnexAudioDeviceHold(Button button,int flow)
        {
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(650)};bool pressed=false;Point origin=new Point();
            void Open(){timer.Stop();if(!pressed)return;pressed=false;if(widget!=null&&(!widget.Visible||widget.GameBarDisplayMode!=Microsoft.Gaming.XboxGameBar.XboxGameBarDisplayMode.Foreground))return;_onexHeldTiles[button]=DateTimeOffset.UtcNow;_=ShowOnexAudioDeviceAsync(button,flow);}
            timer.Tick+=(s,e)=>Open();
            button.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((s,e)=>{origin=e.GetCurrentPoint(button).Position;pressed=true;timer.Start();}),true);
            button.AddHandler(UIElement.PointerMovedEvent,new PointerEventHandler((s,e)=>{var point=e.GetCurrentPoint(button).Position;if(Math.Abs(point.X-origin.X)>10||Math.Abs(point.Y-origin.Y)>10){pressed=false;timer.Stop();}}),true);
            PointerEventHandler release=(s,e)=>{pressed=false;timer.Stop();};button.AddHandler(UIElement.PointerReleasedEvent,release,true);button.AddHandler(UIElement.PointerCanceledEvent,release,true);button.AddHandler(UIElement.PointerCaptureLostEvent,release,true);
            button.RightTapped+=(s,e)=>{pressed=false;timer.Stop();_onexHeldTiles[button]=DateTimeOffset.UtcNow;_=ShowOnexAudioDeviceAsync(button,flow);e.Handled=true;};
            button.KeyDown+=(s,e)=>{
                if(e.Key==VirtualKey.GamepadX||e.Key==VirtualKey.Application||(e.Key==VirtualKey.F10&&(Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift)&Windows.UI.Core.CoreVirtualKeyStates.Down)!=0)){pressed=false;timer.Stop();_=ShowOnexAudioDeviceAsync(button,flow);e.Handled=true;}
                else if((e.Key==VirtualKey.GamepadA||e.Key==VirtualKey.Enter||e.Key==VirtualKey.Space)&&!pressed){pressed=true;timer.Start();}
            };
            button.KeyUp+=(s,e)=>{pressed=false;timer.Stop();};button.Unloaded+=(s,e)=>{pressed=false;timer.Stop();};
        }
        private void UpdateOnexAudioOutputAction()
        {
            if(_onexAudioOutputApply==null)return;var selected=(_onexAudioOutputList.SelectedItem as ListViewItem)?.Tag as JsonObject;
            _onexAudioOutputApply.IsEnabled=!_onexAudioOutputWriting&&selected?.GetNamedBoolean("active",false)==true&&(!selected.GetNamedBoolean("defaultMultimedia",false)||!selected.GetNamedBoolean("defaultConsole",false)||(_onexAudioDeviceFlow==1&&!selected.GetNamedBoolean("defaultCommunications",false)));
            _onexAudioOutputList.IsEnabled=!_onexAudioOutputWriting;
        }
        private void ApplyOnexAudioOutputState(string json)
        {
            if(!_onexAudioOutputOpen||!JsonObject.TryParse(json,out var state))return;_onexAudioOutputState=state;
            var devices=state.GetNamedArray("devices",new JsonArray());
            string signature=string.Join("|",devices.Select(d=>{var a=d.GetObject();return a.GetNamedString("token")+a.GetNamedString("name")+a.GetNamedBoolean("active")+a.GetNamedBoolean("defaultConsole")+a.GetNamedBoolean("defaultMultimedia")+a.GetNamedBoolean("defaultCommunications");}));
            if(signature!=_onexAudioOutputSignature)
            {
                _onexAudioOutputSignature=signature;string selected=((_onexAudioOutputList.SelectedItem as ListViewItem)?.Tag as JsonObject)?.GetNamedString("token");_onexAudioOutputList.Items.Clear();ListViewItem current=null,previous=null;
                foreach(var value in devices)
                {
                    var device=value.GetObject();bool active=device.GetNamedBoolean("active"),chosen=device.GetNamedBoolean("defaultMultimedia");
                    var row=new Grid{ColumnSpacing=10,Padding=new Thickness(8,8,8,8)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24)});
                    int form=(int)device.GetNamedNumber("formFactor",-1);row.Children.Add(new FontIcon{Glyph=_onexAudioDeviceFlow==1?"\uE720":form==3||form==5?"\uE7F6":"\uE767",FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=20,Foreground=active?new SolidColorBrush(OnexPalette.Cyan):OnexSettingsBrush(136,136,136),VerticalAlignment=VerticalAlignment.Center});
                    var text=new StackPanel{Spacing=3};text.Children.Add(new TextBlock{Text=device.GetNamedString("name"),FontSize=14,MaxLines=1,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,Foreground=active?new SolidColorBrush(Colors.White):OnexSettingsBrush(136,136,136)});
                    text.Children.Add(new TextBlock{Text=chosen?(_onexAudioDeviceFlow==1?global::XboxGamingBar.OnexStrings.Get("Системный микрофон"):global::XboxGamingBar.OnexStrings.Get("Системный вывод")):active?global::XboxGamingBar.OnexStrings.Get("Доступно"):global::XboxGamingBar.OnexStrings.Get("Не подключено"),FontSize=12,Foreground=chosen?new SolidColorBrush(OnexPalette.Cyan):OnexSettingsBrush(160,160,160)});Grid.SetColumn(text,1);row.Children.Add(text);
                    if(chosen){var check=new FontIcon{Glyph="\uE73E",FontSize=16,Foreground=new SolidColorBrush(OnexPalette.Cyan),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(check,2);row.Children.Add(check);}
                    var item=new ListViewItem{Content=row,Tag=device,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(8,0,8,0),Margin=new Thickness(0,2,0,2),IsEnabled=active};
                    Windows.UI.Xaml.Automation.AutomationProperties.SetName(item,device.GetNamedString("name")+", "+(chosen?global::XboxGamingBar.OnexStrings.Get("системный вывод"):active?global::XboxGamingBar.OnexStrings.Get("доступно"):global::XboxGamingBar.OnexStrings.Get("не подключено")));
                    _onexAudioOutputList.Items.Add(item);if(chosen)current=item;if(device.GetNamedString("token")==selected)previous=item;
                }
                _onexAudioOutputList.SelectedItem=previous??current;
            }
            bool available=state.GetNamedBoolean("available",false);_onexAudioOutputStatus.Text=!available?OnexStrings.Get(state.GetNamedString("error",global::XboxGamingBar.OnexStrings.Get("Устройства недоступны"))):devices.Count==0?(_onexAudioDeviceFlow==0?global::XboxGamingBar.OnexStrings.Get("Доступных устройств вывода нет. Подключи наушники и обнови список."):global::XboxGamingBar.OnexStrings.Get("Доступных микрофонов нет. Подключи микрофон и обнови список.")):"";
            _onexAudioOutputStatus.Visibility=string.IsNullOrEmpty(_onexAudioOutputStatus.Text)?Visibility.Collapsed:Visibility.Visible;UpdateOnexAudioOutputAction();
        }
        private async Task RefreshOnexAudioOutputAsync()
        {
            if(!_onexAudioOutputOpen||_onexAudioOutputReading)return;_onexAudioOutputReading=true;int generation=_onexAudioOutputGeneration;int flow=_onexAudioDeviceFlow;
            await _onexAudioOutputGate.WaitAsync();
            try{var r=await App.SendMessageAsync(new ValueSet{{flow==0?"OnexAudioOutputGet":"OnexAudioInputGet",true}});if(r==null||r.ContainsKey("Error"))throw new InvalidOperationException(r?.ContainsKey("Error")==true?r["Error"].ToString():global::XboxGamingBar.OnexStrings.Get("Нет соединения"));if(_onexAudioOutputOpen&&generation==_onexAudioOutputGeneration){ApplyOnexAudioOutputState((string)r[flow==0?"OnexAudioOutputState":"OnexAudioInputState"]);ApplyOnexMediaLevels((string)r["MediaLevels"]);}}
            catch(Exception error){if(_onexAudioOutputOpen&&generation==_onexAudioOutputGeneration){_onexAudioOutputStatus.Text=error.Message;_onexAudioOutputStatus.Visibility=Visibility.Visible;}}
            finally{_onexAudioOutputGate.Release();_onexAudioOutputReading=false;}
        }
        private async Task SelectOnexAudioOutputAsync()
        {
            var device=(_onexAudioOutputList?.SelectedItem as ListViewItem)?.Tag as JsonObject;if(device==null||!device.GetNamedBoolean("active")||_onexAudioOutputWriting)return;
            _onexAudioOutputWriting=true;UpdateOnexAudioOutputAction();int generation=_onexAudioOutputGeneration;int flow=_onexAudioDeviceFlow;await _onexAudioOutputGate.WaitAsync();string error=null;
            try{var r=await App.SendMessageAsync(new ValueSet{{flow==0?"OnexAudioOutputSet":"OnexAudioInputSet",device.GetNamedString("token")},{"OnexMediaUserInput",true}});if(r==null||r.ContainsKey("Error"))throw new InvalidOperationException(r?.ContainsKey("Error")==true?r["Error"].ToString():global::XboxGamingBar.OnexStrings.Get("Нет соединения"));ApplyOnexMediaLevels((string)r["MediaLevels"]);if(_onexAudioOutputOpen&&generation==_onexAudioOutputGeneration)ApplyOnexAudioOutputState((string)r[flow==0?"OnexAudioOutputState":"OnexAudioInputState"]);}
            catch(Exception failure){error=failure.Message;}
            finally{_onexAudioOutputGate.Release();_onexAudioOutputWriting=false;if(_onexAudioOutputOpen)UpdateOnexAudioOutputAction();}
            if(error!=null&&_onexAudioOutputOpen&&generation==_onexAudioOutputGeneration){await RefreshOnexAudioOutputAsync();_onexAudioOutputStatus.Text=error;_onexAudioOutputStatus.Visibility=Visibility.Visible;}
        }
        private Task ShowOnexAudioOutputAsync(Button anchor)=>ShowOnexAudioDeviceAsync(anchor,0);
        private Task ShowOnexAudioInputAsync(Button anchor)=>ShowOnexAudioDeviceAsync(anchor,1);
        private async Task ShowOnexAudioDeviceAsync(Button anchor,int flow)
        {
            if(_onexDetailsDialog!=null||_onexAudioOutputOpen)return;_onexAudioOutputOpen=true;_onexAudioOutputGeneration++;_onexAudioOutputSignature=null;_onexAudioOutputState=null;_onexAudioDeviceFlow=flow;
            var panel=new StackPanel{Spacing=8};var header=new Grid();header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.Children.Add(new TextBlock{Text=flow==0?global::XboxGamingBar.OnexStrings.Get("Динамики и наушники"):global::XboxGamingBar.OnexStrings.Get("Микрофоны"),FontSize=15,VerticalAlignment=VerticalAlignment.Center});
            var refreshButton=new Button{Content=new FontIcon{Glyph="\uE72C",FontSize=17},Foreground=new SolidColorBrush(OnexPalette.Cyan),Width=36,Height=36,MinWidth=0,MinHeight=0,Padding=new Thickness(0)};ToolTipService.SetToolTip(refreshButton,flow==0?global::XboxGamingBar.OnexStrings.Get("Обновить устройства вывода"):global::XboxGamingBar.OnexStrings.Get("Обновить микрофоны"));refreshButton.Click+=async(s,e)=>await RefreshOnexAudioOutputAsync();Grid.SetColumn(refreshButton,1);header.Children.Add(refreshButton);panel.Children.Add(header);
            _onexAudioOutputList=new ListView{Name="OnexAudioOutputList",SelectionMode=ListViewSelectionMode.Single,HorizontalContentAlignment=HorizontalAlignment.Stretch,MaxHeight=Math.Max(140,Math.Min(330,ActualHeight-280))};
            foreach(string key in new[]{"ListViewItemSelectionIndicatorBrush","ListViewItemSelectionIndicatorPointerOverBrush","ListViewItemSelectionIndicatorPressedBrush","SystemControlHighlightAccentBrush","SystemControlForegroundAccentBrush"})_onexAudioOutputList.Resources[key]=new SolidColorBrush(OnexPalette.Cyan);
            _onexAudioOutputList.SelectionChanged+=(s,e)=>UpdateOnexAudioOutputAction();panel.Children.Add(_onexAudioOutputList);
            _onexAudioOutputStatus=new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Загрузка…"),FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=OnexSettingsBrush(255,150,120)};panel.Children.Add(_onexAudioOutputStatus);
            var footer=new Grid{ColumnSpacing=12};footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition());
            _onexAudioOutputApply=new Button{Name="OnexAudioOutputApply",Content=global::XboxGamingBar.OnexStrings.Get("Выбрать"),MinWidth=0,Width=112,Height=34,MinHeight=0,Padding=new Thickness(12,6,12,6),HorizontalAlignment=HorizontalAlignment.Left,Background=new SolidColorBrush(OnexPalette.Cyan),Foreground=OnexSettingsBrush(12,24,32),CornerRadius=new CornerRadius(8),IsEnabled=false};_onexAudioOutputApply.Click+=async(s,e)=>await SelectOnexAudioOutputAsync();footer.Children.Add(_onexAudioOutputApply);
            var dialog=CreateOnexSettingsDialog(flow==0?global::XboxGamingBar.OnexStrings.Get("Вывод звука"):global::XboxGamingBar.OnexStrings.Get("Ввод звука"),flow==0?global::XboxGamingBar.OnexStrings.Get("Изменится системный вывод. Приложения с отдельным устройством сохранят свой выбор."):global::XboxGamingBar.OnexStrings.Get("Микрофон для системы и связи. Приложения с отдельным микрофоном сохранят свой выбор."),panel);dialog.PrimaryButtonText="";dialog.CloseButtonText="";dialog.DefaultButton=ContentDialogButton.None;
            foreach(string key in OnexControlAccentKeys)dialog.Resources[key]=new SolidColorBrush(OnexPalette.Cyan);
            if(dialog.Content is ScrollViewer viewport){viewport.HorizontalContentAlignment=HorizontalAlignment.Stretch;if(viewport.Content is FrameworkElement body){body.Width=double.NaN;body.HorizontalAlignment=HorizontalAlignment.Stretch;}}
            var close=new Button{Content=global::XboxGamingBar.OnexStrings.Get("Закрыть"),Style=dialog.CloseButtonStyle,Width=104,Height=34,MinWidth=0,MinHeight=0,Padding=new Thickness(12,6,12,6),HorizontalAlignment=HorizontalAlignment.Right};close.Click+=(s,e)=>dialog.Hide();Grid.SetColumn(close,1);footer.Children.Add(close);panel.Children.Add(footer);_onexDetailsDialog=dialog;
            var refresh=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};refresh.Tick+=async(s,e)=>await RefreshOnexAudioOutputAsync();
            try{var opening=dialog.ShowAsync();await RefreshOnexAudioOutputAsync();refresh.Start();await opening;}
            finally{refresh.Stop();_onexHeldTiles.Remove(anchor);_onexAudioOutputOpen=false;_onexAudioOutputGeneration++;_onexAudioOutputList=null;_onexAudioOutputApply=null;_onexAudioOutputStatus=null;if(ReferenceEquals(_onexDetailsDialog,dialog))_onexDetailsDialog=null;if(widget!=null&&widget.Visible)anchor.Focus(FocusState.Programmatic);}
        }
    }
}
