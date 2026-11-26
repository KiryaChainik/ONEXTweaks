// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private sealed class WifiWindowRow
        {internal ListViewItem Item;internal TextBlock Name,State,Signal;internal FontIcon Icon;}
        private readonly Dictionary<string,WifiWindowRow> _onexWifiWindowRows=new Dictionary<string,WifiWindowRow>();
        private readonly System.Threading.SemaphoreSlim _onexWifiWindowGate=new System.Threading.SemaphoreSlim(1,1);
        private bool _onexWifiWindowOpen,_onexWifiWindowUpdating,_onexWifiWindowBusy,_onexWifiWindowReadBusy;
        private int _onexWifiWindowGeneration;
        private JsonObject _onexWifiWindowState;
        private ListView _onexWifiNetworkList;
        private ToggleSwitch _onexWifiWindowRadio;
        private TextBlock _onexWifiWindowStatus,_onexWifiSelectionName;
        private PasswordBox _onexWifiPassword;
        private CheckBox _onexWifiRemember;
        private Button _onexWifiConnectButton,_onexWifiRefreshButton,_onexWifiCloseButton,_onexWifiSettingsButton;
        private Border _onexWifiNetworkCard;
        private StackPanel _onexWifiSelection;
        private Border _onexWifiSelectionCard;
        private string _onexWifiSelectedToken,_onexWifiWindowError,_onexWifiWindowReadError,_onexWifiWindowBusyText;
        private static string WifiWindowText(JsonObject value,string key)=>value!=null&&value.TryGetValue(key,out var text)&&text.ValueType==JsonValueType.String?text.GetString():null;
        private JsonObject SelectedWifiNetwork()=>_onexWifiWindowState?.GetNamedArray("networks",new JsonArray()).Select(n=>n.GetObject()).FirstOrDefault(n=>n.GetNamedString("token")==_onexWifiSelectedToken);
        private void UpdateOnexWifiWindowControls()
        {
            var selected=SelectedWifiNetwork();bool connected=selected?.GetNamedBoolean("connected",false)==true,known=selected?.GetNamedBoolean("known",false)==true;
            _onexWifiWindowRadio.IsEnabled=!_onexWifiWindowBusy&&App.IsConnected&&_onexWifiState?.GetNamedBoolean("available",false)==true;
            _onexWifiRefreshButton.IsEnabled=!_onexWifiWindowBusy&&OnexWifiOn;
            _onexWifiSettingsButton.IsEnabled=!_onexWifiWindowBusy&&App.IsConnected;
            _onexWifiNetworkList.IsEnabled=!_onexWifiWindowBusy&&OnexWifiOn;
            _onexWifiSelectionCard.Visibility=selected==null||connected?Visibility.Collapsed:Visibility.Visible;
            _onexWifiNetworkList.MaxHeight=Math.Max(150,Math.Min(520,ActualHeight-(connected||selected==null?280:selected.GetNamedBoolean("passwordRequired",false)?450:360)));
            _onexWifiSelectionName.Text=selected==null?"":selected.GetNamedString("name");
            _onexWifiPassword.Visibility=!connected&&selected?.GetNamedBoolean("passwordRequired",false)==true?Visibility.Visible:Visibility.Collapsed;
            _onexWifiRemember.Visibility=selected!=null&&!connected&&!known?Visibility.Visible:Visibility.Collapsed;
            _onexWifiConnectButton.Content=global::XboxGamingBar.OnexStrings.Get("Подключиться");
            _onexWifiConnectButton.IsEnabled=!_onexWifiWindowBusy&&OnexWifiOn&&selected!=null&&!connected&&selected.GetNamedBoolean("canConnect",false);
            _onexWifiPassword.IsEnabled=!_onexWifiWindowBusy;_onexWifiRemember.IsEnabled=!_onexWifiWindowBusy;
            string status=_onexWifiWindowError??WifiWindowText(_onexWifiWindowState,"error")??_onexWifiWindowReadError??(_onexWifiWindowBusy?_onexWifiWindowBusyText:!OnexWifiOn?global::XboxGamingBar.OnexStrings.Get("Wi-Fi выключен"):_onexWifiWindowRows.Count==0?global::XboxGamingBar.OnexStrings.Get("Сети пока не найдены. Обнови список."):"");
            _onexWifiWindowStatus.Text=status;_onexWifiWindowStatus.Visibility=string.IsNullOrEmpty(status)?Visibility.Collapsed:Visibility.Visible;
        }
        private void ApplyOnexWifiWindowState(string json)
        {
            if(!_onexWifiWindowOpen||!JsonObject.TryParse(json,out var state))return;_onexWifiWindowState=state;ApplyOnexWifiState(state.GetNamedObject("radio").Stringify());_onexWifiWindowUpdating=true;
            try
            {
                _onexWifiWindowRadio.IsOn=OnexWifiOn;var present=new HashSet<string>();
                foreach(var entry in state.GetNamedArray("networks",new JsonArray()))
                {
                    var network=entry.GetObject();string token=network.GetNamedString("token");present.Add(token);
                    if(!_onexWifiWindowRows.TryGetValue(token,out var row))
                    {
                        var text=new StackPanel{Spacing=2};var name=new TextBlock{FontSize=14,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,MaxLines=1};var detail=new TextBlock{FontSize=12,Foreground=OnexSettingsBrush(170,181,193),TextTrimming=TextTrimming.CharacterEllipsis,MaxLines=1};text.Children.Add(name);text.Children.Add(detail);
                        var line=new Grid{ColumnSpacing=10};line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24)});line.ColumnDefinitions.Add(new ColumnDefinition());line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(48)});
                        var icon=new FontIcon{FontFamily=new FontFamily("Segoe Fluent Icons"),Glyph="\uE701",FontSize=18,VerticalAlignment=VerticalAlignment.Center};var signal=new TextBlock{FontSize=12,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right,Foreground=OnexSettingsBrush(170,181,193)};
                        line.Children.Add(icon);Grid.SetColumn(text,1);line.Children.Add(text);Grid.SetColumn(signal,2);line.Children.Add(signal);
                        var item=new ListViewItem{Tag=token,Content=line,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(16,8,16,8),MinHeight=56};ApplyOnexTileWindowControlAccent(item);row=new WifiWindowRow{Item=item,Name=name,State=detail,Signal=signal,Icon=icon};_onexWifiWindowRows[token]=row;_onexWifiNetworkList.Items.Add(item);
                    }
                    bool connected=network.GetNamedBoolean("connected");row.Name.Text=network.GetNamedString("name");row.State.Text=(connected?global::XboxGamingBar.OnexStrings.Get("Подключено · "):network.GetNamedBoolean("known")?global::XboxGamingBar.OnexStrings.Get("Сохранено · "):"")+network.GetNamedString("security");row.Signal.Text=((int)network.GetNamedNumber("signal"))+"%";row.Icon.Foreground=connected?OnexDialogAccent:OnexSettingsBrush(170,181,193);
                }
                foreach(var token in _onexWifiWindowRows.Keys.Where(t=>!present.Contains(t)).ToArray()){_onexWifiNetworkList.Items.Remove(_onexWifiWindowRows[token].Item);_onexWifiWindowRows.Remove(token);}
                if(_onexWifiSelectedToken==null||!present.Contains(_onexWifiSelectedToken))
                {
                    var first=state.GetNamedArray("networks",new JsonArray()).Select(n=>n.GetObject()).FirstOrDefault();_onexWifiSelectedToken=first?.GetNamedString("token");_onexWifiNetworkList.SelectedItem=_onexWifiSelectedToken!=null?_onexWifiWindowRows[_onexWifiSelectedToken].Item:null;_onexWifiPassword.Password="";
                }
            }
            finally{_onexWifiWindowUpdating=false;UpdateOnexWifiWindowControls();}
        }
        private async Task<string> RequestOnexWifiWindowAsync(ValueSet request,bool input=false)
        {
            int generation=_onexWifiWindowGeneration;await _onexWifiWindowGate.WaitAsync();
            try
            {
                if(input)request["OnexUserInput"]=true;var reply=await App.SendMessageAsync(request);
                if(reply==null||reply.ContainsKey("Error"))throw new InvalidOperationException(reply?.ContainsKey("Error")==true?reply["Error"].ToString():global::XboxGamingBar.OnexStrings.Get("Нет соединения с помощником"));
                string json=(string)reply["OnexWifiNetworksState"];if(_onexWifiWindowOpen&&generation==_onexWifiWindowGeneration)ApplyOnexWifiWindowState(json);return json;
            }
            finally{_onexWifiWindowGate.Release();}
        }
        private async Task RefreshOnexWifiWindowAsync(bool scan=false)
        {
            if(!_onexWifiWindowOpen||_onexWifiWindowReadBusy)return;_onexWifiWindowReadBusy=true;int generation=_onexWifiWindowGeneration;
            try{await RequestOnexWifiWindowAsync(new ValueSet{{scan?"OnexWifiScan":"OnexWifiNetworksGet",true}},scan);if(generation==_onexWifiWindowGeneration)_onexWifiWindowReadError=null;}
            catch(Exception error){if(generation==_onexWifiWindowGeneration)_onexWifiWindowReadError=error.Message;}
            finally{_onexWifiWindowReadBusy=false;if(_onexWifiWindowOpen&&generation==_onexWifiWindowGeneration)UpdateOnexWifiWindowControls();}
        }
        private async Task ConnectSelectedOnexWifiAsync()
        {
            var selected=SelectedWifiNetwork();if(selected==null||_onexWifiWindowBusy||selected.GetNamedBoolean("connected",false))return;int generation=_onexWifiWindowGeneration;string token=_onexWifiSelectedToken;bool disconnect=selected.GetNamedBoolean("connected");
            _onexWifiWindowBusy=true;_onexWifiWindowError=null;_onexWifiWindowBusyText=disconnect?global::XboxGamingBar.OnexStrings.Get("Отключение…"):OnexStrings.Get("Подключение…");UpdateOnexWifiWindowControls();
            try
            {
                var request=new ValueSet();
                if(disconnect)request["OnexWifiDisconnect"]=token;
                else
                {
                    var change=new JsonObject{{"token",JsonValue.CreateStringValue(token)},{"remember",JsonValue.CreateBooleanValue(_onexWifiRemember.IsChecked==true)}};
                    if(selected.GetNamedBoolean("passwordRequired",false))change["password"]=JsonValue.CreateStringValue(_onexWifiPassword.Password);
                    request["OnexWifiConnect"]=change.Stringify();
                }
                await RequestOnexWifiWindowAsync(request,true);if(generation==_onexWifiWindowGeneration)_onexWifiPassword.Password="";
                bool reached=false;
                for(int n=0;n<40&&_onexWifiWindowOpen&&generation==_onexWifiWindowGeneration;n++)
                {
                    await RefreshOnexWifiWindowAsync();var actual=_onexWifiWindowState.GetNamedArray("networks").Select(entry=>entry.GetObject()).FirstOrDefault(entry=>entry.GetNamedString("token")==token);
                    if((actual?.GetNamedBoolean("connected",false)==true)==!disconnect){reached=true;break;}await Task.Delay(500);
                }
                if(!reached&&_onexWifiWindowOpen&&generation==_onexWifiWindowGeneration)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Не удалось подтвердить подключение. Проверь пароль и доступность сети."));
            }
            catch(Exception error){if(generation==_onexWifiWindowGeneration)_onexWifiWindowError=error.Message;}
            finally{if(generation==_onexWifiWindowGeneration){_onexWifiWindowBusy=false;if(_onexWifiWindowOpen)UpdateOnexWifiWindowControls();}}
        }
        private async Task ShowOnexWifiWindowAsync(Button anchor)
        {
            if(_onexDetailsDialog!=null||_onexWifiWindowOpen)return;_onexWifiWindowOpen=true;_onexWifiWindowGeneration++;_onexWifiWindowBusy=false;_onexWifiWindowError=null;_onexWifiWindowReadError=null;_onexWifiWindowState=null;_onexWifiSelectedToken=null;_onexWifiWindowRows.Clear();
            var panel=new StackPanel{Spacing=0};_onexWifiWindowRadio=new ToggleSwitch{IsOn=OnexWifiOn,Header=null,OnContent="",OffContent="",MinWidth=0,Width=48,VerticalAlignment=VerticalAlignment.Center};_onexWifiRefreshButton=new Button{Content=new FontIcon{FontFamily=new FontFamily("Segoe Fluent Icons"),Glyph="\uE72C",FontSize=16},HorizontalAlignment=HorizontalAlignment.Right,Width=40,Height=40,MinWidth=0,MinHeight=0,Padding=new Thickness(0)};Windows.UI.Xaml.Automation.AutomationProperties.SetName(_onexWifiWindowRadio,global::XboxGamingBar.OnexStrings.Get("Wi-Fi"));ToolTipService.SetToolTip(_onexWifiRefreshButton,global::XboxGamingBar.OnexStrings.Get("Обновить список сетей"));Windows.UI.Xaml.Automation.AutomationProperties.SetName(_onexWifiRefreshButton,global::XboxGamingBar.OnexStrings.Get("Обновить список сетей"));
            _onexWifiSettingsButton=new Button{Content=new FontIcon{FontFamily=new FontFamily("Segoe Fluent Icons"),Glyph="\uE713",FontSize=16},Width=40,Height=40,MinWidth=0,MinHeight=0,Padding=new Thickness(0)};ToolTipService.SetToolTip(_onexWifiSettingsButton,global::XboxGamingBar.OnexStrings.Get("Windows: Сеть и Интернет"));Windows.UI.Xaml.Automation.AutomationProperties.SetName(_onexWifiSettingsButton,global::XboxGamingBar.OnexStrings.Get("Открыть настройки сети и Интернета Windows"));_onexWifiSettingsButton.Click+=async(s,e)=>await InvokeOnexDesktopActionAsync("networkSettings");
            var header=new Grid{Margin=new Thickness(0,0,0,8)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var wireless=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,VerticalAlignment=VerticalAlignment.Center};wireless.Children.Add(new TextBlock{Name="OnexWifiHeaderLabel",Text=global::XboxGamingBar.OnexStrings.Get("Wi-Fi"),FontSize=13,VerticalAlignment=VerticalAlignment.Center});wireless.Children.Add(_onexWifiWindowRadio);header.Children.Add(wireless);var headerActions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};headerActions.Children.Add(_onexWifiSettingsButton);headerActions.Children.Add(_onexWifiRefreshButton);Grid.SetColumn(headerActions,1);header.Children.Add(headerActions);_onexWifiWindowRadio.XYFocusRight=_onexWifiSettingsButton;_onexWifiSettingsButton.XYFocusLeft=_onexWifiWindowRadio;_onexWifiSettingsButton.XYFocusRight=_onexWifiRefreshButton;_onexWifiRefreshButton.XYFocusLeft=_onexWifiSettingsButton;panel.Children.Add(header);
            _onexWifiNetworkList=new ListView{SelectionMode=ListViewSelectionMode.Single,MaxHeight=Math.Max(150,Math.Min(420,ActualHeight-360)),HorizontalContentAlignment=HorizontalAlignment.Stretch};_onexWifiNetworkCard=OnexSettingsCard(_onexWifiNetworkList);_onexWifiNetworkCard.Padding=new Thickness(4,8,4,8);_onexWifiNetworkCard.Margin=new Thickness(0,0,0,8);panel.Children.Add(_onexWifiNetworkCard);
            _onexWifiSelection=new StackPanel{Spacing=8};_onexWifiSelectionName=new TextBlock{FontSize=14,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,MaxLines=1};_onexWifiPassword=new PasswordBox{Header=global::XboxGamingBar.OnexStrings.Get("Пароль сети"),MaxLength=64};_onexWifiRemember=new CheckBox{Content=global::XboxGamingBar.OnexStrings.Get("Запомнить сеть"),IsChecked=true};_onexWifiConnectButton=new Button{Content=global::XboxGamingBar.OnexStrings.Get("Подключиться"),HorizontalAlignment=HorizontalAlignment.Stretch};
            var selection=new Grid{ColumnSpacing=12};selection.ColumnDefinitions.Add(new ColumnDefinition());selection.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});_onexWifiSelectionName.VerticalAlignment=VerticalAlignment.Center;_onexWifiConnectButton.Width=156;selection.Children.Add(_onexWifiSelectionName);Grid.SetColumn(_onexWifiConnectButton,1);selection.Children.Add(_onexWifiConnectButton);
            _onexWifiSelection.Children.Add(selection);_onexWifiSelection.Children.Add(_onexWifiPassword);_onexWifiSelection.Children.Add(_onexWifiRemember);_onexWifiSelectionCard=OnexSettingsCard(_onexWifiSelection);_onexWifiSelectionCard.Margin=new Thickness(0,0,0,8);panel.Children.Add(_onexWifiSelectionCard);
            _onexWifiWindowStatus=new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Загрузка…"),Margin=new Thickness(0,0,0,8),FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=OnexSettingsBrush(255,150,120)};panel.Children.Add(_onexWifiWindowStatus);
            var dialog=CreateOnexSettingsDialog(global::XboxGamingBar.OnexStrings.Get("Wi-Fi"),global::XboxGamingBar.OnexStrings.Get("Выбор сети и управление подключением"),panel);dialog.PrimaryButtonText="";dialog.CloseButtonText="";dialog.DefaultButton=ContentDialogButton.None;if(dialog.Content is ScrollViewer viewport){viewport.MaxHeight=Math.Max(240,ActualHeight-130);viewport.HorizontalContentAlignment=HorizontalAlignment.Stretch;if(viewport.Content is FrameworkElement body){body.Width=double.NaN;body.HorizontalAlignment=HorizontalAlignment.Stretch;}}_onexWifiConnectButton.Style=dialog.PrimaryButtonStyle;_onexWifiRefreshButton.Style=dialog.CloseButtonStyle;_onexWifiSettingsButton.Style=dialog.CloseButtonStyle;_onexWifiCloseButton=new Button{Name="OnexWifiCloseButton",Content=global::XboxGamingBar.OnexStrings.Get("Закрыть"),Style=dialog.CloseButtonStyle,Width=104,MinWidth=0,Height=34,MinHeight=0,Padding=new Thickness(12,6,12,6),HorizontalAlignment=HorizontalAlignment.Right};_onexWifiCloseButton.Click+=(s,e)=>dialog.Hide();panel.Children.Add(_onexWifiCloseButton);_onexDetailsDialog=dialog;ApplyOnexTileWindowControlAccent(_onexWifiWindowRadio);ApplyOnexTileWindowControlAccent(_onexWifiNetworkList);ApplyOnexTileWindowControlAccent(_onexWifiRemember);ApplyOnexTileWindowControlAccent(_onexWifiPassword);
            _onexWifiNetworkList.SelectionChanged+=(s,e)=>{if(_onexWifiWindowUpdating)return;_onexWifiSelectedToken=(_onexWifiNetworkList.SelectedItem as ListViewItem)?.Tag as string;_onexWifiPassword.Password="";_onexWifiWindowError=null;UpdateOnexWifiWindowControls();};
            _onexWifiConnectButton.Click+=async(s,e)=>await ConnectSelectedOnexWifiAsync();_onexWifiRefreshButton.Click+=async(s,e)=>{_onexWifiWindowError=null;await RefreshOnexWifiWindowAsync(true);};
            _onexWifiWindowRadio.Toggled+=async(s,e)=>{if(_onexWifiWindowUpdating||_onexWifiWindowBusy||_onexWifiWindowRadio.IsOn==OnexWifiOn)return;int generation=_onexWifiWindowGeneration;_onexWifiWindowBusy=true;_onexWifiWindowBusyText="Применение…";UpdateOnexWifiWindowControls();try{await ToggleOnexWifiAsync();await RefreshOnexWifiWindowAsync();}finally{if(generation==_onexWifiWindowGeneration){_onexWifiWindowBusy=false;if(_onexWifiWindowOpen)UpdateOnexWifiWindowControls();}}};
            var refresh=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};refresh.Tick+=async(s,e)=>await RefreshOnexWifiWindowAsync();
            try{var shown=dialog.ShowAsync();await RefreshOnexWifiWindowAsync(true);refresh.Start();await shown;}
            finally{refresh.Stop();_onexWifiWindowOpen=false;_onexWifiPassword.Password="";_onexWifiWindowRows.Clear();if(_onexDetailsDialog==dialog)_onexDetailsDialog=null;anchor.Focus(FocusState.Programmatic);}
        }
    }
}
