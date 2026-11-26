// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexBluetoothIdentityAsync(JsonObject report)
        {
            void Check(bool ok,string error){if(!ok)throw new InvalidOperationException(error);}
            await RefreshOnexBluetoothAsync(true);bool radio=OnexBluetoothOn;var tile=qsTileMap["OnexBluetooth"];var showing=ShowOnexTileDetailsAsync(tile.TileButton,tile.Id);
            try
            {
                for(int n=0;n<100&&!_onexBluetoothWindowOpen;n++)await Task.Delay(100);Check(_onexBluetoothWindowOpen,"Bluetooth window did not open");await Task.Delay(5000);
                for(int n=0;n<100&&_onexBluetoothReadBusy;n++)await Task.Delay(100);UpdateOnexBluetoothWindow();
                var groups=OnexBluetoothPhysicalDevices();var rows=_onexBluetoothRows.Values.ToArray();
                for(int i=0;i<rows.Length;i++)for(int j=i+1;j<rows.Length;j++)Check(!rows[i].Physical.Keys.Overlaps(rows[j].Physical.Keys),"One physical identity has two visible rows");
                Check(rows.All(row=>!row.Physical.Paired||row.Device.Pairing.IsPaired),"Unpaired advertisement replaced a paired action target");
                Check(_onexBluetoothPairButton.Visibility==Visibility.Visible&&!_onexBluetoothPairButton.IsEnabled&&_onexBluetoothList.SelectedItem==null,"Initial selection/action changed");
                var selected=rows.FirstOrDefault();
                if(selected!=null)
                {
                    _onexBluetoothList.SelectedItem=selected.Item;var keys=new HashSet<string>(selected.Physical.Keys,StringComparer.OrdinalIgnoreCase);await RefreshOnexBluetoothAsync(true);UpdateOnexBluetoothWindow();
                    Check(_onexBluetoothSelectedId!=null&&_onexBluetoothRows[_onexBluetoothSelectedId].Physical.Keys.Overlaps(keys),"Refresh moved selection to another physical device");
                    var actual=_onexBluetoothRows[_onexBluetoothSelectedId];string expected=actual.Physical.Paired&&actual.Physical.Connected?"Отключить":actual.Physical.Paired?"Подключить":"Сопрячь устройство";
                    Check((string)_onexBluetoothPairButton.Content==expected,"Action does not match physical paired/connected state");report["liveSelectionSurvivesRefresh"]=JsonValue.CreateBooleanValue(true);report["liveActionMatchesPhysicalState"]=JsonValue.CreateBooleanValue(true);
                }
                else report["selectionCheckSkipped"]=JsonValue.CreateStringValue("No nearby endpoints; no physical pairing attempted");
                var known=groups.SelectMany(group=>group.Endpoints).Select(DescribeOnexBluetooth).ToArray();int validAddresses=known.Count(value=>OnexBluetoothIdentity.Address(value.Address)!=null),containers=known.Count(value=>Guid.TryParse(value.Container,out var id)&&id!=Guid.Empty);
                report["liveEndpoints"]=JsonValue.CreateNumberValue(known.Length);report["physicalGroups"]=JsonValue.CreateNumberValue(groups.Length);report["visibleRows"]=JsonValue.CreateNumberValue(rows.Length);report["endpointsWithAddress"]=JsonValue.CreateNumberValue(validAddresses);report["endpointsWithAepContainer"]=JsonValue.CreateNumberValue(containers);report["duplicateRowsPresent"]=JsonValue.CreateNumberValue(0);report["endpointsCollapsed"]=JsonValue.CreateNumberValue(known.Length-groups.Length);report["pairedGroups"]=JsonValue.CreateNumberValue(groups.Count(group=>group.Paired));report["pairedTargetsPreferred"]=JsonValue.CreateBooleanValue(true);report["actualPhysicalPairingTested"]=JsonValue.CreateBooleanValue(false);report["pairingChanges"]=JsonValue.CreateNumberValue(0);report["radioChanges"]=JsonValue.CreateNumberValue(0);
                await SaveOnexImage("onex-bluetooth-deduplicated-private.png",_onexDetailsDialog,0);
                CloseOnexMenus();await showing;var reopened=ShowOnexTileDetailsAsync(tile.TileButton,tile.Id);for(int n=0;n<100&&!_onexBluetoothWindowOpen;n++)await Task.Delay(100);await Task.Delay(200);Check(_onexBluetoothSelectedId==null&&_onexBluetoothSelectedKeys==null&&!_onexBluetoothPairButton.IsEnabled,"Reopening restored a stale selection");CloseOnexMenus();await reopened;report["reopenClearsSelection"]=JsonValue.CreateBooleanValue(true);
                Check(OnexBluetoothOn==radio,"Bluetooth radio changed during identity inspection");report["radioPreserved"]=JsonValue.CreateBooleanValue(true);
            }
            finally{CloseOnexMenus();await showing;}
        }
    }
}
