// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private sealed class BluetoothWindowRow
        {internal DeviceInformation Device;internal OnexBluetoothGroup<DeviceInformation> Physical;internal ListViewItem Item;internal TextBlock Name,State;internal FontIcon Icon;}
        private readonly Dictionary<string,BluetoothWindowRow> _onexBluetoothRows=new Dictionary<string,BluetoothWindowRow>();
        private readonly List<DeviceWatcher> _onexBluetoothWatchers=new List<DeviceWatcher>();
        private readonly Dictionary<string,List<IDisposable>> _onexBluetoothConnections=new Dictionary<string,List<IDisposable>>();
        private readonly Dictionary<string,HashSet<string>> _onexBluetoothConnectionKeys=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private bool _onexBluetoothWindowOpen,_onexBluetoothWindowBusy,_onexBluetoothWindowUpdating;
        private int _onexBluetoothWindowGeneration;
        private ListView _onexBluetoothList;
        private ToggleSwitch _onexBluetoothWindowRadio;
        private Button _onexBluetoothRefresh,_onexBluetoothSettings,_onexBluetoothClose,_onexBluetoothPairButton;
        private TextBlock _onexBluetoothStatus;
        private Grid _onexBluetoothActionPane;
        private StackPanel _onexBluetoothConfirmPane;
        private Border _onexBluetoothConfirmCard,_onexBluetoothDeviceCard;
        private bool _onexBluetoothConfirmRequiresPin;
        private TextBlock _onexBluetoothConfirmText,_onexBluetoothPin;
        private TextBox _onexBluetoothPinInput;
        private Button _onexBluetoothConfirmAccept,_onexBluetoothConfirmCancel;
        private HashSet<string> _onexBluetoothSelectedKeys;
        private string _onexBluetoothSelectedId,_onexBluetoothSelectedIdentity,_onexBluetoothWindowError;
        private TaskCompletionSource<string> _onexBluetoothAnswer;
        private bool _onexBluetoothPairingActive;
        private ContentDialog _onexBluetoothDialog;
        private bool PreserveOnexBluetoothPairingDialog()
        {
            bool preserve=_onexBluetoothPairingActive&&_onexBluetoothWindowOpen&&_onexBluetoothDialog!=null&&_onexDetailsDialog==_onexBluetoothDialog;
            if(preserve)Logger.Info("ONEX Bluetooth pairing: system foreground transition preserved pending pairing");
            return preserve;
        }
        private DeviceInformation SelectedBluetoothDevice()=>_onexBluetoothSelectedId!=null&&_onexBluetoothRows.TryGetValue(_onexBluetoothSelectedId,out var row)?row.Device:null;
        private void UpdateOnexBluetoothWindow()
        {
            if(!_onexBluetoothWindowOpen||_onexBluetoothList==null)return;_onexBluetoothWindowUpdating=true;
            try
            {
                _onexBluetoothDeviceCard.Visibility=_onexBluetoothConfirmCard.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;
                _onexBluetoothWindowRadio.IsOn=OnexBluetoothOn;_onexBluetoothWindowRadio.IsEnabled=OnexBluetoothReady&&!_onexBluetoothWindowBusy;
                _onexBluetoothRefresh.IsEnabled=OnexBluetoothOn&&!_onexBluetoothWindowBusy;_onexBluetoothList.IsEnabled=OnexBluetoothOn&&!_onexBluetoothWindowBusy;
                _onexBluetoothSettings.IsEnabled=!_onexBluetoothWindowBusy;
                var devices=OnexBluetoothPhysicalDevices().Where(group=>group.Endpoints.Any(endpoint=>!string.IsNullOrWhiteSpace(BluetoothName(endpoint)))).OrderByDescending(group=>group.Connected&&group.Paired).ThenByDescending(group=>group.Paired).ThenBy(OnexBluetoothPhysicalName).Take(128).ToArray();var present=new HashSet<string>();
                foreach(var physical in devices)
                {
                    var device=physical.Preferred;present.Add(physical.Id);
                    if(!_onexBluetoothRows.TryGetValue(physical.Id,out var row))
                    {
                        var text=new StackPanel{Spacing=2};var name=new TextBlock{FontSize=14,FontWeight=FontWeights.SemiBold,MaxLines=1,TextTrimming=TextTrimming.CharacterEllipsis};var state=new TextBlock{FontSize=12,Foreground=OnexSettingsBrush(170,181,193),MaxLines=1,TextTrimming=TextTrimming.CharacterEllipsis};text.Children.Add(name);text.Children.Add(state);
                        var content=new Grid{ColumnSpacing=10};content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24)});content.ColumnDefinitions.Add(new ColumnDefinition());var icon=new FontIcon{FontFamily=new FontFamily("Segoe Fluent Icons"),Glyph="\uE702",FontSize=18,VerticalAlignment=VerticalAlignment.Center};content.Children.Add(icon);Grid.SetColumn(text,1);content.Children.Add(text);
                        var item=new ListViewItem{Tag=physical.Id,Content=content,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(16,8,16,8),MinHeight=56};ApplyOnexTileWindowControlAccent(item);row=new BluetoothWindowRow{Item=item,Name=name,State=state,Icon=icon};_onexBluetoothRows[physical.Id]=row;_onexBluetoothList.Items.Add(item);
                        item.RightTapped+=(s,e)=>{if(row.Device?.Pairing.IsPaired==true&&!_onexBluetoothWindowBusy){ShowOnexBluetoothDeviceMenu(row);e.Handled=true;}};
                    }
                    row.Device=device;row.Physical=physical;bool connected=physical.Connected;row.Name.Text=OnexBluetoothPhysicalName(physical);row.State.Text=connected?global::XboxGamingBar.OnexStrings.Get("Подключено"):device.Pairing.IsPaired?global::XboxGamingBar.OnexStrings.Get("Сопряжено"):global::XboxGamingBar.OnexStrings.Get("Доступно для сопряжения");row.Icon.Foreground=connected?OnexDialogAccent:OnexSettingsBrush(170,181,193);
                }
                foreach(var id in _onexBluetoothRows.Keys.Where(id=>!present.Contains(id)).ToArray()){_onexBluetoothList.Items.Remove(_onexBluetoothRows[id].Item);_onexBluetoothRows.Remove(id);}
                // A classic/BLE endpoint can be replaced during discovery. Retain the
                // user's chosen physical device instead of silently selecting a neighbour.
                var selection=OnexBluetoothIdentity.FindSelection(devices,_onexBluetoothSelectedId,_onexBluetoothSelectedKeys);_onexBluetoothSelectedId=selection?.Id;
                _onexBluetoothList.SelectedItem=selection!=null?_onexBluetoothRows[selection.Id].Item:null;
                var selected=SelectedBluetoothDevice();if(selection!=null){_onexBluetoothSelectedIdentity=selection.Id;if(_onexBluetoothSelectedKeys==null)_onexBluetoothSelectedKeys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);_onexBluetoothSelectedKeys.UnionWith(selection.Keys);}
                bool connectedSelected=selection?.Paired==true&&selection.Connected;bool awaitingAnswer=_onexBluetoothAnswer!=null&&!_onexBluetoothAnswer.Task.IsCompleted;bool showAction=!awaitingAnswer&&_onexBluetoothConfirmCard.Visibility!=Visibility.Visible;
                // Keep the action discoverable before selection and stable across refreshes.
                _onexBluetoothPairButton.Visibility=showAction?Visibility.Visible:Visibility.Collapsed;_onexBluetoothPairButton.Content=connectedSelected?global::XboxGamingBar.OnexStrings.Get("Отключить"):selected?.Pairing.IsPaired==true?global::XboxGamingBar.OnexStrings.Get("Подключить"):global::XboxGamingBar.OnexStrings.Get("Сопрячь устройство");_onexBluetoothPairButton.IsEnabled=OnexBluetoothOn&&!_onexBluetoothWindowBusy&&selected!=null&&(selected.Pairing.IsPaired||selected.Pairing.CanPair);
                _onexBluetoothPairButton.Background=_onexBluetoothPairButton.IsEnabled?OnexDialogAccent:OnexSettingsBrush(48,55,64);_onexBluetoothPairButton.Foreground=_onexBluetoothPairButton.IsEnabled?OnexSettingsBrush(12,24,32):OnexSettingsBrush(139,145,154);
                _onexBluetoothStatus.Foreground=_onexBluetoothWindowError!=null||_onexBluetoothError!=null?OnexSettingsBrush(255,150,120):OnexSettingsBrush(170,181,193);
                _onexBluetoothStatus.Text=_onexBluetoothWindowError??_onexBluetoothError??(!OnexBluetoothOn?global::XboxGamingBar.OnexStrings.Get("Bluetooth выключен"):_onexBluetoothWindowBusy?global::XboxGamingBar.OnexStrings.Get("Выполнение…"):devices.Length==0?global::XboxGamingBar.OnexStrings.Get("Поиск устройств… Включи режим сопряжения на устройстве."):selected!=null&&!selected.Pairing.IsPaired&&!selected.Pairing.CanPair?global::XboxGamingBar.OnexStrings.Get("Устройство пока не готово. Включи на нём режим сопряжения."):"");_onexBluetoothStatus.Visibility=string.IsNullOrEmpty(_onexBluetoothStatus.Text)?Visibility.Collapsed:Visibility.Visible;
                _onexBluetoothStatus.Measure(new Windows.Foundation.Size(Math.Max(100,ActualWidth-110),double.PositiveInfinity));double statusHeight=_onexBluetoothStatus.Visibility==Visibility.Visible?_onexBluetoothStatus.DesiredSize.Height:0;
                _onexBluetoothList.MaxHeight=Math.Max(112,Math.Min(520,ActualHeight-280-statusHeight));
            }
            finally{_onexBluetoothWindowUpdating=false;}
        }
        private void StopOnexBluetoothDiscovery()
        {
            foreach(var watcher in _onexBluetoothWatchers)try{if(watcher.Status==DeviceWatcherStatus.Started||watcher.Status==DeviceWatcherStatus.EnumerationCompleted)watcher.Stop();}catch{} _onexBluetoothWatchers.Clear();
        }
        private async Task StartOnexBluetoothDiscoveryAsync()
        {
            if(!_onexBluetoothWindowOpen)return;StopOnexBluetoothDiscovery();int generation=++_onexBluetoothWindowGeneration;
            _onexBluetoothDiscoveryDevices.Clear();ReconcileOnexBluetoothDevices();
            await RefreshOnexBluetoothAsync(true);if(!_onexBluetoothWindowOpen||generation!=_onexBluetoothWindowGeneration||!OnexBluetoothOn)return;
            foreach(string query in new[]{BluetoothDevice.GetDeviceSelectorFromPairingState(false),BluetoothLEDevice.GetDeviceSelectorFromPairingState(false)})
            {
                var watcher=DeviceInformation.CreateWatcher(query,OnexBluetoothProperties,DeviceInformationKind.AssociationEndpoint);
                async Task Apply(Action action){try{await Dispatcher.RunAsync(CoreDispatcherPriority.Normal,()=>{if(_onexBluetoothWindowOpen&&generation==_onexBluetoothWindowGeneration){action();ReconcileOnexBluetoothDevices();UpdateOnexBluetoothWindow();}});}catch(Exception error){Logger.Warn("Bluetooth discovery update: "+error.Message);}}
                watcher.Added+=async(s,device)=>await Apply(()=>_onexBluetoothDiscoveryDevices[device.Id]=device);
                watcher.Updated+=async(s,update)=>await Apply(()=>{if(_onexBluetoothDiscoveryDevices.TryGetValue(update.Id,out var device))device.Update(update);if(update.Properties.ContainsKey("System.Devices.Aep.IsPaired"))_=RefreshOnexBluetoothAsync(true);});
                watcher.Removed+=async(s,update)=>await Apply(()=>_onexBluetoothDiscoveryDevices.Remove(update.Id));
                watcher.Stopped+=async(s,args)=>{if(s.Status==DeviceWatcherStatus.Aborted)await Apply(()=>_onexBluetoothWindowError=global::XboxGamingBar.OnexStrings.Get("Поиск Bluetooth остановлен Windows. Нажми обновление."));};
                _onexBluetoothWatchers.Add(watcher);watcher.Start();
            }
            UpdateOnexBluetoothWindow();
        }
        private async Task<string> ConfirmOnexBluetoothAsync(string text,string pin,bool input)
        {
            if(!_onexBluetoothWindowOpen)return null;int generation=_onexBluetoothWindowGeneration;var answer=new TaskCompletionSource<string>();_onexBluetoothConfirmRequiresPin=input;_onexBluetoothAnswer=answer;_onexBluetoothConfirmAccept.Visibility=Visibility.Visible;_onexBluetoothConfirmText.Text=text;_onexBluetoothPin.Text=pin??"";_onexBluetoothPin.Visibility=string.IsNullOrEmpty(pin)?Visibility.Collapsed:Visibility.Visible;_onexBluetoothPinInput.Text="";_onexBluetoothPinInput.Visibility=input?Visibility.Visible:Visibility.Collapsed;_onexBluetoothConfirmAccept.Content=input?global::XboxGamingBar.OnexStrings.Get("Продолжить"):global::XboxGamingBar.OnexStrings.Get("Подтвердить");_onexBluetoothConfirmCard.Visibility=Visibility.Visible;UpdateOnexBluetoothWindow();
            try{var completed=await Task.WhenAny(answer.Task,Task.Delay(TimeSpan.FromSeconds(60)));return completed==answer.Task?await answer.Task:null;}
            finally
            {
                // Dialog close advances the generation before resolving this answer.
                // The old answer must still be detached, without touching a newer ceremony.
                if(_onexBluetoothAnswer==answer)_onexBluetoothAnswer=null;
                if(generation==_onexBluetoothWindowGeneration)_onexBluetoothConfirmCard.Visibility=Visibility.Collapsed;
            }
        }
        private void ReleaseOnexBluetoothConnections(OnexBluetoothGroup<DeviceInformation> physical,DeviceInformation device)
        {
            var keys=physical?.Keys??OnexBluetoothIdentity.Keys(DescribeOnexBluetooth(device));
            foreach(string id in _onexBluetoothConnections.Keys.Where(id=>id==physical?.Id||id==device.Id||_onexBluetoothConnectionKeys.TryGetValue(id,out var ownedKeys)&&ownedKeys.Overlaps(keys)).ToArray())
            {foreach(var resource in _onexBluetoothConnections[id])resource.Dispose();_onexBluetoothConnections.Remove(id);_onexBluetoothConnectionKeys.Remove(id);}
        }
        private async Task ConnectOnexBluetoothDeviceAsync(DeviceInformation device)
        {
            var owned=new List<IDisposable>();var physical=OnexBluetoothPhysicalDevices().FirstOrDefault(group=>group.Endpoints.Any(endpoint=>endpoint.Id==device.Id));var keys=physical?.Keys??OnexBluetoothIdentity.Keys(DescribeOnexBluetooth(device));
            try
            {
                BluetoothDevice classic=null;try{classic=await BluetoothDevice.FromIdAsync(device.Id);}catch(ArgumentException){}
                if(classic!=null){owned.Add(classic);var services=await classic.GetRfcommServicesAsync(BluetoothCacheMode.Uncached);if(services.Error!=BluetoothError.Success)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Format("Windows не установила соединение Bluetooth: {0}",services.Error));owned.AddRange(services.Services.Cast<IDisposable>());}
                else{var low=await BluetoothLEDevice.FromIdAsync(device.Id);if(low==null)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Устройство недоступно"));owned.Add(low);var services=await low.GetGattServicesAsync(BluetoothCacheMode.Uncached);if(services.Status!=Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Format("Windows не установила BLE-соединение: {0}",services.Status));owned.AddRange(services.Services.Cast<IDisposable>());}
                for(int attempt=0;attempt<30;attempt++){await RefreshOnexBluetoothAsync(true);var actual=OnexBluetoothIdentity.FindSelection(OnexBluetoothPhysicalDevices(),physical?.Id,keys);if(actual!=null&&actual.Connected){ReleaseOnexBluetoothConnections(actual,device);_onexBluetoothConnections[actual.Id]=owned;_onexBluetoothConnectionKeys[actual.Id]=new HashSet<string>(actual.Keys,StringComparer.OrdinalIgnoreCase);owned=null;return;}await Task.Delay(300);}
                throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Windows не подтвердила соединение. Убедись, что устройство включено и готово к подключению."));
            }
            finally{if(owned!=null)foreach(var resource in owned)resource.Dispose();}
        }
        private async Task SetOnexBluetoothOverlayAsync(bool restore)
        {
            if(!Dispatcher.HasThreadAccess)
            {
                Task transition=null;await Dispatcher.RunAsync(CoreDispatcherPriority.Normal,()=>transition=SetOnexBluetoothOverlayAsync(restore));await transition;return;
            }
            if(widget==null)return;
            await SendOnexHomeRequestAsync(new Windows.Foundation.Collections.ValueSet{{"OnexBluetoothUi",restore?"restore":"release"}});
            for(int attempt=0;attempt<75;attempt++)
            {
                if(Windows.Gaming.UI.GameBar.IsInputRedirected==restore&&(!restore||widget.Visible))return;
                await Task.Delay(40);
            }
            throw new InvalidOperationException(restore?global::XboxGamingBar.OnexStrings.Get("Не удалось вернуть окно Bluetooth"):global::XboxGamingBar.OnexStrings.Get("Game Bar не освободил ввод для системного подтверждения"));
        }
        private async Task DisconnectOnexBluetoothDeviceAsync(DeviceInformation device)
        {
            var physical=OnexBluetoothPhysicalDevices().FirstOrDefault(group=>group.Endpoints.Any(endpoint=>endpoint.Id==device.Id));var keys=physical?.Keys??OnexBluetoothIdentity.Keys(DescribeOnexBluetooth(device));
            var endpoints=physical?.Endpoints??new[]{device};var peers=endpoints.Where(OnexBluetoothConnected).Select(endpoint=>{var state=DescribeOnexBluetooth(endpoint);return OnexBluetoothIdentity.Address(state.Address)??OnexBluetoothIdentity.RemoteAddress(state.Id);}).Where(address=>address!=null).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if(peers.Length==0)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Windows не сообщила адрес подключённого устройства"));ReleaseOnexBluetoothConnections(physical,device);
            foreach(string address in peers)await SendOnexHomeRequestAsync(new Windows.Foundation.Collections.ValueSet{{"OnexBluetoothDisconnect",address}});
            for(int attempt=0;attempt<40;attempt++)
            {
                await RefreshOnexBluetoothAsync(true);
                var actual=OnexBluetoothIdentity.FindSelection(OnexBluetoothPhysicalDevices(),physical?.Id,keys);
                if(actual!=null&&!actual.Connected&&actual.Paired)return;
                await Task.Delay(150);
            }
            throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Windows не подтвердила отключение устройства"));
        }
        private async Task PairSelectedOnexBluetoothAsync()
        {
            var device=SelectedBluetoothDevice();if(device==null||_onexBluetoothWindowBusy)return;int generation=_onexBluetoothWindowGeneration;_onexBluetoothWindowBusy=true;_onexBluetoothWindowError=null;UpdateOnexBluetoothWindow();
            try
            {
                if(device.Pairing.IsPaired){if(_onexBluetoothRows[_onexBluetoothSelectedId].Physical.Connected)await DisconnectOnexBluetoothDeviceAsync(device);else await ConnectOnexBluetoothDeviceAsync(device);}
                else
                {
                    _onexBluetoothPairingActive=true;
                    try
                    {
                        var result=await PairThroughOnexBluetoothHostAsync(device,false,generation);
                        if(!result.GetNamedBoolean("paired",false))throw new InvalidOperationException(result.GetNamedString("error",global::XboxGamingBar.OnexStrings.Get("Сопряжение не завершено")));
                    }
                    finally{if(generation==_onexBluetoothWindowGeneration)_onexBluetoothPairingActive=false;}
                }
                await RefreshOnexBluetoothAsync(true);
            }
            catch(Exception error){if(_onexBluetoothWindowOpen&&generation==_onexBluetoothWindowGeneration)_onexBluetoothWindowError=error.Message;}
            finally{if(generation==_onexBluetoothWindowGeneration){_onexBluetoothWindowBusy=false;if(_onexBluetoothWindowOpen){_onexBluetoothConfirmCard.Visibility=Visibility.Collapsed;UpdateOnexBluetoothWindow();try{await SetOnexBluetoothOverlayAsync(true);}catch(Exception error){Logger.Warn("ONEX Bluetooth return: "+error.Message);}}}}
        }
        private void ShowOnexBluetoothDeviceMenu(BluetoothWindowRow row)
        {
            var menu=CreateOnexMenu();var forget=new MenuFlyoutItem{Text=global::XboxGamingBar.OnexStrings.Get("Удалить сопряжение…")};forget.Click+=async(s,e)=>
            {
                if(_onexBluetoothWindowBusy)return;_onexBluetoothWindowBusy=true;UpdateOnexBluetoothWindow();
                try{string confirmed=await ConfirmOnexBluetoothAsync(global::XboxGamingBar.OnexStrings.Get("Удалить сопряжение с «")+BluetoothName(row.Device)+global::XboxGamingBar.OnexStrings.Get("»? Для следующего подключения понадобится сопряжение заново."),null,false);if(confirmed!=null){ReleaseOnexBluetoothConnections(row.Physical,row.Device);var result=await row.Device.Pairing.UnpairAsync();if(result.Status!=DeviceUnpairingResultStatus.Unpaired&&result.Status!=DeviceUnpairingResultStatus.AlreadyUnpaired)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Format("Windows не удалила сопряжение: {0}",result.Status));await RefreshOnexBluetoothAsync(true);}}
                catch(Exception error){_onexBluetoothWindowError=error.Message;}
                finally{_onexBluetoothWindowBusy=false;if(_onexBluetoothWindowOpen)UpdateOnexBluetoothWindow();}
            };menu.Items.Add(forget);menu.ShowAt(row.Item);
        }
        private async Task ShowOnexBluetoothWindowAsync(Button anchor)
        {
            if(_onexDetailsDialog!=null||_onexBluetoothWindowOpen)return;_onexBluetoothWindowOpen=true;_onexBluetoothWindowBusy=false;_onexBluetoothWindowError=null;_onexBluetoothSelectedId=null;_onexBluetoothSelectedIdentity=null;_onexBluetoothSelectedKeys=null;_onexBluetoothAnswer=null;_onexBluetoothConfirmRequiresPin=false;_onexBluetoothRows.Clear();_onexBluetoothWindowGeneration++;
            var panel=new StackPanel{Spacing=0};var header=new Grid{Margin=new Thickness(0,0,0,8)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var radioLine=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};radioLine.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Bluetooth"),FontSize=13,VerticalAlignment=VerticalAlignment.Center});_onexBluetoothWindowRadio=new ToggleSwitch{IsOn=OnexBluetoothOn,OnContent="",OffContent="",MinWidth=0,Width=48,VerticalAlignment=VerticalAlignment.Center};ApplyOnexTileWindowControlAccent(_onexBluetoothWindowRadio);radioLine.Children.Add(_onexBluetoothWindowRadio);header.Children.Add(radioLine);
            _onexBluetoothRefresh=new Button{Content=new FontIcon{FontFamily=new FontFamily("Segoe Fluent Icons"),Glyph="\uE72C",FontSize=16},Width=40,Height=40,MinWidth=0,MinHeight=0,Padding=new Thickness(0)};_onexBluetoothSettings=new Button{Content=new FontIcon{FontFamily=new FontFamily("Segoe Fluent Icons"),Glyph="\uE713",FontSize=16},Width=40,Height=40,MinWidth=0,MinHeight=0,Padding=new Thickness(0)};var headerActions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};headerActions.Children.Add(_onexBluetoothSettings);headerActions.Children.Add(_onexBluetoothRefresh);Grid.SetColumn(headerActions,1);header.Children.Add(headerActions);panel.Children.Add(header);
            _onexBluetoothList=new ListView{SelectionMode=ListViewSelectionMode.Single,MaxHeight=Math.Max(150,ActualHeight-280),HorizontalContentAlignment=HorizontalAlignment.Stretch};ApplyOnexTileWindowControlAccent(_onexBluetoothList);_onexBluetoothDeviceCard=OnexSettingsCard(_onexBluetoothList);_onexBluetoothDeviceCard.Padding=new Thickness(4,8,4,8);_onexBluetoothDeviceCard.Margin=new Thickness(0,0,0,8);panel.Children.Add(_onexBluetoothDeviceCard);
            _onexBluetoothConfirmPane=new StackPanel{Spacing=8};_onexBluetoothConfirmText=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=13};_onexBluetoothPin=new TextBlock{FontSize=28,Foreground=OnexDialogAccent,FontWeight=FontWeights.SemiBold};_onexBluetoothPinInput=new TextBox{PlaceholderText=global::XboxGamingBar.OnexStrings.Get("PIN"),MaxLength=16};ApplyOnexTileWindowControlAccent(_onexBluetoothPinInput);_onexBluetoothConfirmAccept=new Button{Content=global::XboxGamingBar.OnexStrings.Get("Подтвердить")};_onexBluetoothConfirmCancel=new Button{Content=global::XboxGamingBar.OnexStrings.Get("Отмена")};_onexBluetoothConfirmPane.Children.Add(_onexBluetoothConfirmText);_onexBluetoothConfirmPane.Children.Add(_onexBluetoothPin);_onexBluetoothConfirmPane.Children.Add(_onexBluetoothPinInput);_onexBluetoothConfirmPane.Children.Add(OnexSettingsColumns(_onexBluetoothConfirmCancel,_onexBluetoothConfirmAccept));_onexBluetoothConfirmCard=OnexSettingsCard(_onexBluetoothConfirmPane);_onexBluetoothConfirmCard.Visibility=Visibility.Collapsed;_onexBluetoothConfirmCard.Margin=new Thickness(0,0,0,8);panel.Children.Add(_onexBluetoothConfirmCard);
            _onexBluetoothStatus=new TextBlock{FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=OnexSettingsBrush(255,150,120),Margin=new Thickness(0,0,0,8)};panel.Children.Add(_onexBluetoothStatus);
            var dialog=CreateOnexSettingsDialog(global::XboxGamingBar.OnexStrings.Get("Bluetooth"),global::XboxGamingBar.OnexStrings.Get("Устройства и сопряжение"),panel);dialog.PrimaryButtonText="";dialog.CloseButtonText="";dialog.DefaultButton=ContentDialogButton.None;if(dialog.Content is ScrollViewer viewport){viewport.MaxHeight=Math.Max(240,ActualHeight-130);viewport.HorizontalContentAlignment=HorizontalAlignment.Stretch;if(viewport.Content is FrameworkElement body){body.Width=double.NaN;body.HorizontalAlignment=HorizontalAlignment.Stretch;}}
            _onexBluetoothSettings.Style=dialog.CloseButtonStyle;_onexBluetoothRefresh.Style=dialog.CloseButtonStyle;_onexBluetoothPairButton=new Button{Content=global::XboxGamingBar.OnexStrings.Get("Сопрячь устройство"),Style=dialog.PrimaryButtonStyle,IsEnabled=false,Background=OnexSettingsBrush(48,55,64),Foreground=OnexSettingsBrush(139,145,154),Height=34,MinWidth=0,MinHeight=0,Padding=new Thickness(12,6,12,6),HorizontalAlignment=HorizontalAlignment.Left};_onexBluetoothConfirmAccept.Style=dialog.PrimaryButtonStyle;_onexBluetoothConfirmCancel.Style=dialog.CloseButtonStyle;_onexBluetoothClose=new Button{Content=global::XboxGamingBar.OnexStrings.Get("Закрыть"),Style=dialog.CloseButtonStyle,Width=104,Height=34,MinWidth=0,MinHeight=0,Padding=new Thickness(12,6,12,6),HorizontalAlignment=HorizontalAlignment.Right};_onexBluetoothActionPane=new Grid{ColumnSpacing=12};_onexBluetoothActionPane.ColumnDefinitions.Add(new ColumnDefinition());_onexBluetoothActionPane.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});_onexBluetoothActionPane.Children.Add(_onexBluetoothPairButton);Grid.SetColumn(_onexBluetoothClose,1);_onexBluetoothActionPane.Children.Add(_onexBluetoothClose);panel.Children.Add(_onexBluetoothActionPane);_onexDetailsDialog=dialog;
            ToolTipService.SetToolTip(_onexBluetoothSettings,global::XboxGamingBar.OnexStrings.Get("Windows: Bluetooth и устройства"));Windows.UI.Xaml.Automation.AutomationProperties.SetName(_onexBluetoothSettings,global::XboxGamingBar.OnexStrings.Get("Открыть настройки Bluetooth и устройств Windows"));_onexBluetoothSettings.Click+=async(s,e)=>await InvokeOnexDesktopActionAsync("bluetoothSettings");ToolTipService.SetToolTip(_onexBluetoothRefresh,global::XboxGamingBar.OnexStrings.Get("Обновить список устройств"));Windows.UI.Xaml.Automation.AutomationProperties.SetName(_onexBluetoothRefresh,global::XboxGamingBar.OnexStrings.Get("Обновить список устройств"));Windows.UI.Xaml.Automation.AutomationProperties.SetName(_onexBluetoothWindowRadio,global::XboxGamingBar.OnexStrings.Get("Bluetooth"));
            _onexBluetoothList.SelectionChanged+=(s,e)=>{if(_onexBluetoothWindowUpdating)return;_onexBluetoothSelectedId=(_onexBluetoothList.SelectedItem as ListViewItem)?.Tag as string;var selected=SelectedBluetoothDevice();_onexBluetoothSelectedIdentity=_onexBluetoothSelectedId;_onexBluetoothSelectedKeys=_onexBluetoothSelectedId!=null?new HashSet<string>(_onexBluetoothRows[_onexBluetoothSelectedId].Physical.Keys,StringComparer.OrdinalIgnoreCase):null;_onexBluetoothWindowError=null;UpdateOnexBluetoothWindow();};_onexBluetoothPairButton.Click+=async(s,e)=>await PairSelectedOnexBluetoothAsync();_onexBluetoothClose.Click+=(s,e)=>dialog.Hide();_onexBluetoothRefresh.Click+=async(s,e)=>{_onexBluetoothWindowError=null;await StartOnexBluetoothDiscoveryAsync();};
            _onexBluetoothWindowRadio.Toggled+=async(s,e)=>{if(_onexBluetoothWindowUpdating||_onexBluetoothWriteBusy||_onexBluetoothWindowRadio.IsOn==OnexBluetoothOn)return;int generation=_onexBluetoothWindowGeneration;await SetOnexBluetoothRadioAsync(_onexBluetoothWindowRadio.IsOn);if(_onexBluetoothWindowOpen&&generation==_onexBluetoothWindowGeneration){if(OnexBluetoothOn)await StartOnexBluetoothDiscoveryAsync();else StopOnexBluetoothDiscovery();}};
            _onexBluetoothConfirmAccept.Click+=(s,e)=>{string value=_onexBluetoothConfirmRequiresPin?_onexBluetoothPinInput.Text.Trim():"accept";if(value.Length>0)_onexBluetoothAnswer?.TrySetResult(value);};_onexBluetoothConfirmCancel.Click+=(s,e)=>{_onexBluetoothAnswer?.TrySetResult(null);CancelOnexBluetoothHostPairing(false);_onexBluetoothConfirmCard.Visibility=Visibility.Collapsed;};
            _onexBluetoothDialog=dialog;
            try{var shown=dialog.ShowAsync();await StartOnexBluetoothDiscoveryAsync();await shown;}
            finally{_onexBluetoothWindowOpen=false;_onexBluetoothWindowGeneration++;StopOnexBluetoothDiscovery();var pendingAnswer=_onexBluetoothAnswer;_onexBluetoothAnswer=null;if(_onexBluetoothPairingActive)Logger.Info("ONEX Bluetooth pairing: cancelled by dialog close");_onexBluetoothPairingActive=false;CancelOnexBluetoothHostPairing(true);_onexBluetoothDialog=null;if(_onexDetailsDialog==dialog)_onexDetailsDialog=null;anchor.Focus(FocusState.Programmatic);pendingAnswer?.TrySetResult(null);}
        }
    }
}
