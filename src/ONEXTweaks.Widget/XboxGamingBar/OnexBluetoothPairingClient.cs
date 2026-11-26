// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Devices.Enumeration;
using Windows.Storage;
using Windows.System.Diagnostics;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private string _onexBluetoothHostToken;
        private void CancelOnexBluetoothHostPairing(bool suppressReturn)
        {
            if(_onexBluetoothHostToken==null)return;
            try
            {
                string path=System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path,OnexBluetoothPairingHost.FolderName,_onexBluetoothHostToken+".cancel.json");
                System.IO.File.WriteAllText(path,suppressReturn?"{\"suppressReturn\":true}":"{\"suppressReturn\":false}");
            }
            catch(Exception error){Logger.Warn("ONEX Bluetooth cancel request: "+error.Message);}
        }
        private async Task<JsonObject> PairThroughOnexBluetoothHostAsync(DeviceInformation device,bool probe,int generation)
        {
            string token=Guid.NewGuid().ToString("N");_onexBluetoothHostToken=token;
            var folder=await ApplicationData.Current.LocalFolder.CreateFolderAsync(OnexBluetoothPairingHost.FolderName,CreationCollisionOption.OpenIfExists);
            var request=new JsonObject{["schema"]=JsonValue.CreateNumberValue(1),["token"]=JsonValue.CreateStringValue(token),["createdUtc"]=JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("o")),["mode"]=JsonValue.CreateStringValue(probe?"probe":"pair"),["deviceId"]=JsonValue.CreateStringValue(probe?"":device.Id)};
            await FileIO.WriteTextAsync(await folder.CreateFileAsync(token+".request.json",CreationCollisionOption.FailIfExists),request.Stringify());
            try
            {
                Logger.Info("ONEX Bluetooth pairing: starting independent confirmation host, probe="+probe);
                await SendOnexHomeRequestAsync(new Windows.Foundation.Collections.ValueSet{{"OnexBluetoothPairHost",token}});
                var deadline=DateTimeOffset.UtcNow.AddSeconds(110);
                while(DateTimeOffset.UtcNow<deadline)
                {
                    if(!_onexBluetoothWindowOpen||generation!=_onexBluetoothWindowGeneration){CancelOnexBluetoothHostPairing(true);throw new OperationCanceledException();}
                    JsonObject result=null;
                    try{var file=await folder.TryGetItemAsync(token+".result.json") as StorageFile;if(file!=null)result=JsonObject.Parse(await FileIO.ReadTextAsync(file));}catch(System.Runtime.InteropServices.COMException){}catch(ArgumentException){}
                    if(result!=null&&result.GetNamedString("token","")==token&&result.GetNamedBoolean("isFinal",false))
                    {
                        await FileIO.WriteTextAsync(await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-bluetooth-last-host-result.json",CreationCollisionOption.ReplaceExisting),result.Stringify());
                        Logger.Info("ONEX Bluetooth pairing: independent host result="+result.GetNamedString("status","Unknown"));
                        if(result.GetNamedString("applicationId","")!="BluetoothPairing"||result.GetNamedNumber("processId",0)==ProcessDiagnosticInfo.GetForCurrentProcess().ProcessId)throw new InvalidOperationException(result.GetNamedString("error",global::XboxGamingBar.OnexStrings.Get("Не удалось создать независимое окно сопряжения")));
                        return result;
                    }
                    await Task.Delay(250);
                }
                CancelOnexBluetoothHostPairing(false);throw new TimeoutException(global::XboxGamingBar.OnexStrings.Get("Истекло время ожидания сопряжения"));
            }
            finally{if(_onexBluetoothHostToken==token)_onexBluetoothHostToken=null;}
        }
    }
}
