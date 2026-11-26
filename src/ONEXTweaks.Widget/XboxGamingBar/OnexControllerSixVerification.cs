// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexControllerSixAsync(JsonObject report)
  {
   void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);object source=null;bool syncing=_onexControllerSync;string previous=_onexControllerOpen;
   try
   {
    LegionNavItem.IsChecked=true;await ReadOnexControllerAsync();SetOnexControllerCard("mapping");source=_onexControllerSource.SelectedItem;_onexControllerSync=true;
    var inputs=_onexControllerState.GetNamedArray("oemInputs");Check(inputs.Count==6&&inputs.Count(value=>ControllerText(value.GetObject(),"location")=="front")==4,"Must expose four front and two rear controls");
    string[] expected={"Home","GKey","KeyboardMouse","Onex","M1","M2"};
    foreach(string button in expected)
    {
     var item=_onexControllerSource.Items.OfType<ComboBoxItem>().Single(entry=>(string)entry.Tag=="OEM:"+button);_onexControllerSource.SelectedItem=item;RefreshOnexControllerMapping();
     Check(_onexControllerKind.Items.OfType<ComboBoxItem>().Skip(5).All(entry=>entry.IsEnabled),"OEM system actions unavailable");
     Check(((string)item.Content).StartsWith(button=="M1"||button=="M2"?"Rear":"Front",StringComparison.Ordinal),"Physical location label wrong");
    }
    _onexControllerSource.SelectedItem=_onexControllerSource.Items.OfType<ComboBoxItem>().Single(item=>(string)item.Tag=="A");RefreshOnexControllerMapping();Check(_onexControllerKind.Items.OfType<ComboBoxItem>().Skip(5).All(item=>!item.IsEnabled),"Normal gamepad controls expose OEM-only actions");
    _onexControllerSource.SelectedItem=_onexControllerSource.Items.OfType<ComboBoxItem>().Single(item=>(string)item.Tag=="OEM:Home");RefreshOnexControllerMapping();
    await Task.Delay(100);UpdateLayout();Check(OnexNavigableContent(LegionScrollViewer.Content as DependencyObject).Contains(_onexControllerOemEnabled),"OEM enable missing from gamepad navigation");
    FrameworkElement card=_onexControllerMappingHint.Parent as FrameworkElement;while(card!=null&&!(card is Border))card=card.Parent as FrameworkElement;Check(card!=null,"Mapping card is missing");
    card.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await SaveOnexImage("onex-controller-six274.png",card,150);
    report["sixPhysicalSources"]=JsonValue.CreateBooleanValue(true);report["fourFrontTwoRear"]=JsonValue.CreateBooleanValue(true);report["oemActionsAndFocusGraph"]=JsonValue.CreateBooleanValue(true);report["oemEnableChanged"]=JsonValue.CreateBooleanValue(false);report["controllerFirmwareWrites"]=JsonValue.CreateNumberValue(0);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally{_onexControllerSync=true;if(source!=null){_onexControllerSource.SelectedItem=source;RefreshOnexControllerMapping();}_onexControllerSync=syncing;SetOnexControllerCard(null);if(previous!=null)SetOnexControllerCard(previous);if(original!=null)original.IsChecked=true;}
  }
 }
}
