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
  private Button _onexControllerFirmwareRead;
  private TextBlock _onexControllerFirmwareSummary;
  private Border _onexControllerFirmwareCard;
  private JsonObject _onexControllerFirmwareState;
  private bool _onexControllerFirmwareBusy;
  private Border OnexControllerHardwareCard()
  {
   var body=new StackPanel{Spacing=10};
   body.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Controller firmware"),FontSize=18,FontWeight=Windows.UI.Text.FontWeights.SemiBold});
   _onexControllerFirmwareSummary=ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Read current hardware assignments and stick / trigger settings. Existing assignments are preserved."));
   body.Children.Add(_onexControllerFirmwareSummary);
   _onexControllerFirmwareRead=new Button{Name="OnexControllerFirmwareRead",Content=global::XboxGamingBar.OnexStrings.Get("Read firmware settings"),Style=(Style)Resources["StandaloneButtonStyle"]};
   _onexControllerFirmwareRead.Click+=async(s,e)=>{try{await ReadOnexControllerHardwareAsync();}catch(Exception error){_onexControllerFirmwareSummary.Text=error.Message;}};
   body.Children.Add(_onexControllerFirmwareRead);
   return _onexControllerFirmwareCard=OnexSettingsCard(body);
  }
  private static JsonObject ControllerFirmwareObject(JsonObject state,string name)
  {
   if(state==null||!state.TryGetValue(name,out var value))return null;
   if(value.ValueType==JsonValueType.Object)return value.GetObject();
   if(value.ValueType==JsonValueType.String&&JsonObject.TryParse(value.GetString(),out var parsed))return parsed;
   return null;
  }
  private static string ControllerFirmwareBinding(JsonObject map,string id)
  {
   if(map==null||!map.TryGetValue(id,out var item)||item.ValueType!=JsonValueType.Object)return OnexStrings.Get("Not reported");
   var binding=item.GetObject();string type=ControllerText(binding,"funcCode");
   switch(type){case "01":return OnexStrings.Get("Gamepad");case "02":return OnexStrings.Get("Keyboard / shortcut");case "03":return OnexStrings.Get("Macro");case "04":return OnexStrings.Get("Turbo");case "05":return OnexStrings.Get("Second function");default:return OnexStrings.Get("Hardware assignment");}
  }
  private async Task<JsonObject> ReadOnexControllerHardwareAsync()
  {
   if(_onexControllerFirmwareBusy)throw new InvalidOperationException(OnexStrings.Get("Firmware read already in progress"));
   _onexControllerFirmwareBusy=true;_onexControllerFirmwareRead.IsEnabled=false;
   try
   {
    var reply=await SendOnexHomeRequestAsync(new ValueSet{{"OnexControllerHardwareGet",true}});
    if(!reply.ContainsKey("OnexControllerHardwareState"))throw new InvalidOperationException(OnexStrings.Get("Firmware settings were not returned"));
    var state=JsonObject.Parse((string)reply["OnexControllerHardwareState"]);
    var first=ControllerFirmwareObject(state,"mapping1");var second=ControllerFirmwareObject(state,"mapping2");
    if(!state.GetNamedBoolean("readOnly",false)||first==null||second==null||first.Count==0||second.Count==0||ControllerFirmwareObject(state,"analog")==null)throw new InvalidOperationException(OnexStrings.Get("Firmware readback is incomplete; no settings were changed"));
    _onexControllerFirmwareState=state;
    _onexControllerFirmwareSummary.Text=global::XboxGamingBar.OnexStrings.Format("Hardware layers: {0} / {1} assignments\nM1: {2} · M2: {3}\nHome: {4} · G-Key: {5}\nStick / trigger settings read. Firmware assignments are unchanged.",first.Count,second.Count,ControllerFirmwareBinding(first,"22"),ControllerFirmwareBinding(first,"23"),ControllerFirmwareBinding(first,"24"),ControllerFirmwareBinding(first,"25"));
    return state;
   }
   finally{_onexControllerFirmwareBusy=false;_onexControllerFirmwareRead.IsEnabled=App.IsConnected;}
  }
  private async Task VerifyOnexControllerFirmwareReadAsync(JsonObject report)
  {
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);
   try
   {
    LegionNavItem.IsChecked=true;await ReadOnexControllerAsync();var state=await ReadOnexControllerHardwareAsync();
    var first=ControllerFirmwareObject(state,"mapping1");var second=ControllerFirmwareObject(state,"mapping2");
    var file=await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-controller-firmware273-private.json",CreationCollisionOption.ReplaceExisting);await FileIO.WriteTextAsync(file,state.Stringify());
    report["controllerFirmwareReadback"]=new JsonObject{{"available",JsonValue.CreateBooleanValue(true)},{"readOnly",JsonValue.CreateBooleanValue(true)},{"mapping1Count",JsonValue.CreateNumberValue(first.Count)},{"mapping2Count",JsonValue.CreateNumberValue(second.Count)},{"analogRead",JsonValue.CreateBooleanValue(true)}};
    report["hardwareFirmwareReadTested"]=JsonValue.CreateBooleanValue(true);report["controllerFirmwareWrites"]=JsonValue.CreateNumberValue(0);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);
    _onexControllerFirmwareCard.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await SaveOnexImage("onex-controller-firmware273.png",_onexControllerFirmwareCard,150);
   }
   finally{if(original!=null)original.IsChecked=true;}
  }
 }
}
