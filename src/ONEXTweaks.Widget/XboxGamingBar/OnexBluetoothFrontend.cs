// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using BtRadio=Windows.Devices.Radios.Radio;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private readonly List<BtRadio> _onexBluetoothRadios=new List<BtRadio>();
        private readonly Dictionary<string,DeviceInformation> _onexBluetoothDevices=new Dictionary<string,DeviceInformation>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,DeviceInformation> _onexBluetoothPairedDevices=new Dictionary<string,DeviceInformation>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,DeviceInformation> _onexBluetoothDiscoveryDevices=new Dictionary<string,DeviceInformation>(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] OnexBluetoothProperties={"System.Devices.Aep.IsConnected","System.Devices.Aep.IsPresent","System.Devices.Aep.DeviceAddress","System.Devices.Aep.IsPaired","System.Devices.Aep.ContainerId"};
        private bool _onexBluetoothReadBusy,_onexBluetoothWriteBusy;
        private RadioAccessStatus? _onexBluetoothAccess;
        private string _onexBluetoothError;
        private DateTimeOffset _onexBluetoothDevicesReadAt;
        private DispatcherTimer _onexBluetoothTimer;
        private bool OnexBluetoothOn{get{try{return _onexBluetoothRadios.Any(r=>r.State==RadioState.On);}catch{return false;}}}
        private bool OnexBluetoothReady=>App.IsConnected&&_onexBluetoothRadios.Count>0&&!_onexBluetoothWriteBusy;
        private static bool OnexBluetoothConnected(DeviceInformation device)=>device.Properties.TryGetValue("System.Devices.Aep.IsConnected",out var value)&&value is bool connected&&connected;
        private static string BluetoothName(DeviceInformation device)=>new string((device.Name??"").Where(c=>!char.IsControl(c)).Take(100).ToArray());
        private static OnexBluetoothEndpoint DescribeOnexBluetooth(DeviceInformation device)
        {
            string Property(string name)=>device.Properties.TryGetValue(name,out var value)?value?.ToString():null;
            return new OnexBluetoothEndpoint{Id=device.Id,Name=BluetoothName(device),Address=Property("System.Devices.Aep.DeviceAddress"),Container=Property("System.Devices.Aep.ContainerId"),Paired=device.Pairing.IsPaired,Connected=OnexBluetoothConnected(device),CanPair=device.Pairing.CanPair,Present=device.Properties.TryGetValue("System.Devices.Aep.IsPresent",out var present)&&present is bool found&&found};
        }
        private void ReconcileOnexBluetoothDevices()
        {
            var values=OnexBluetoothIdentity.Reconcile(_onexBluetoothPairedDevices.Values,_onexBluetoothDiscoveryDevices.Values,DescribeOnexBluetooth);
            _onexBluetoothDevices.Clear();foreach(var value in values)_onexBluetoothDevices[value.Id]=value;
        }
        private static string OnexBluetoothPhysicalName(OnexBluetoothGroup<DeviceInformation> group)=>group.Endpoints.Select(BluetoothName).FirstOrDefault(name=>!string.IsNullOrWhiteSpace(name))??OnexStrings.Get("Bluetooth device");
        private OnexBluetoothGroup<DeviceInformation>[] OnexBluetoothPhysicalDevices()=>OnexBluetoothIdentity.Group(_onexBluetoothDevices.Values,DescribeOnexBluetooth);
        private async void OnexBluetoothRadioChanged(BtRadio sender,object args)
        {try{await Dispatcher.RunAsync(CoreDispatcherPriority.Normal,()=>{ApplyOnexTileAvailability();if(_onexBluetoothWindowOpen)UpdateOnexBluetoothWindow();});}catch(Exception e){Logger.Warn("Bluetooth radio update: "+e.Message);}}
        private async Task RefreshOnexBluetoothAsync(bool devices=false)
        {
            if(_onexBluetoothReadBusy)return;_onexBluetoothReadBusy=true;
            try
            {
                var radios=await BtRadio.GetRadiosAsync();foreach(var radio in _onexBluetoothRadios)radio.StateChanged-=OnexBluetoothRadioChanged;_onexBluetoothRadios.Clear();_onexBluetoothRadios.AddRange(radios.Where(r=>r.Kind==RadioKind.Bluetooth));foreach(var radio in _onexBluetoothRadios)radio.StateChanged+=OnexBluetoothRadioChanged;
                if(devices||DateTimeOffset.UtcNow-_onexBluetoothDevicesReadAt>TimeSpan.FromSeconds(5))
                {
                    var paired=new List<DeviceInformation>();
                    foreach(string query in new[]{BluetoothDevice.GetDeviceSelectorFromPairingState(true),BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)})paired.AddRange(await DeviceInformation.FindAllAsync(query,OnexBluetoothProperties,DeviceInformationKind.AssociationEndpoint));
                    _onexBluetoothPairedDevices.Clear();foreach(var device in paired)_onexBluetoothPairedDevices[device.Id]=device;
                    ReconcileOnexBluetoothDevices();_onexBluetoothDevicesReadAt=DateTimeOffset.UtcNow;
                }
                _onexBluetoothError=null;
            }
            catch(Exception error){_onexBluetoothError=error.Message;}
            finally
            {
                _onexBluetoothReadBusy=false;ApplyOnexTileAvailability();if(_onexBluetoothWindowOpen)UpdateOnexBluetoothWindow();
                if(_onexBluetoothTimer==null){_onexBluetoothTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};_onexBluetoothTimer.Tick+=async(s,e)=>{if(widget==null||(widget.Visible&&widget.GameBarDisplayMode==Microsoft.Gaming.XboxGameBar.XboxGameBarDisplayMode.Foreground))await RefreshOnexBluetoothAsync();};_onexBluetoothTimer.Start();}
            }
        }
        private async Task SetOnexBluetoothRadioAsync(bool on)
        {
            if(_onexBluetoothWriteBusy)return;_onexBluetoothWriteBusy=true;ApplyOnexTileAvailability();if(_onexBluetoothWindowOpen)UpdateOnexBluetoothWindow();
            var changed=new List<Tuple<BtRadio,RadioState>>();
            try
            {
                await RefreshOnexBluetoothAsync();if(_onexBluetoothRadios.Count==0)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Bluetooth-адаптер не найден"));
                if(!_onexBluetoothAccess.HasValue)_onexBluetoothAccess=await BtRadio.RequestAccessAsync();
                if(_onexBluetoothAccess!=RadioAccessStatus.Allowed)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Format("Windows не разрешила управление Bluetooth: {0}",_onexBluetoothAccess));
                foreach(var radio in _onexBluetoothRadios.ToArray())
                {
                    var before=radio.State;var target=on?RadioState.On:RadioState.Off;if(before==target)continue;
                    var result=await radio.SetStateAsync(target);if(result!=RadioAccessStatus.Allowed)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Format("Windows не применила состояние Bluetooth: {0}",result));changed.Add(Tuple.Create(radio,before));
                }
                bool confirmed=false;for(int attempt=0;attempt<50;attempt++){if(_onexBluetoothRadios.All(r=>r.State==(on?RadioState.On:RadioState.Off))){confirmed=true;break;}await Task.Delay(100);}if(!confirmed)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Не удалось подтвердить состояние Bluetooth"));
                await RefreshOnexBluetoothAsync(true);_onexBluetoothError=null;
            }
            catch(Exception error)
            {
                var recoveryErrors=new List<Exception>();
                foreach(var item in changed)
                {
                    try
                    {
                        var restored=await item.Item1.SetStateAsync(item.Item2);
                        if(restored!=RadioAccessStatus.Allowed)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Format("Windows не применила состояние Bluetooth: {0}",restored));
                        bool confirmed=false;
                        for(int attempt=0;attempt<50;attempt++){if(item.Item1.State==item.Item2){confirmed=true;break;}await Task.Delay(100);}
                        if(!confirmed)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Не удалось подтвердить состояние Bluetooth"));
                    }
                    catch(Exception recoveryError){recoveryErrors.Add(recoveryError);}
                }
                Exception reported=recoveryErrors.Count==0?error:new AggregateException("Bluetooth",new[]{error}.Concat(recoveryErrors));
                _onexBluetoothError=reported.Message;ShowOnexDesktopStatus(reported.Message);
            }
            finally{_onexBluetoothWriteBusy=false;ApplyOnexTileAvailability();if(_onexBluetoothWindowOpen)UpdateOnexBluetoothWindow();}
        }
        private void ApplyOnexBluetoothTile(TileDefinition tile)
        {
            if(tile.StateText==null)return;var connected=OnexBluetoothPhysicalDevices().Where(group=>group.Paired&&group.Connected).OrderBy(OnexBluetoothPhysicalName).ToArray();
            string caption=_onexBluetoothWriteBusy?OnexStrings.Get("Applying…"):_onexBluetoothRadios.Count==0?OnexStrings.Get("Unavailable"):!OnexBluetoothOn?OnexStrings.Get("Off"):connected.Length==0?OnexStrings.Get("On"):OnexBluetoothPhysicalName(connected[0])+(connected.Length>1?" +"+(connected.Length-1):"");
            tile.StateText.Text=caption;tile.StateText.MaxLines=1;tile.StateText.TextWrapping=TextWrapping.NoWrap;tile.StateText.TextTrimming=TextTrimming.CharacterEllipsis;ApplyOnexToggleTileVisual(tile,OnexBluetoothOn,_onexBluetoothRadios.Count>0);
            if(tile.TileButton!=null){ToolTipService.SetToolTip(tile.TileButton,_onexBluetoothError!=null?OnexStrings.Get(_onexBluetoothError):OnexStrings.Format("Bluetooth · {0}\nTap to toggle · Hold / Gamepad X: devices",caption));Windows.UI.Xaml.Automation.AutomationProperties.SetName(tile.TileButton,global::XboxGamingBar.OnexStrings.Format("Bluetooth: {0}",caption));}
        }
    }
}
