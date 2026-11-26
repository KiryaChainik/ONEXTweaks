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
  private async Task VerifyOnexControllerRumbleProfilesAsync(JsonObject report)
  {
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   await FlushOnexControllerPolicyAsync();for(int n=0;n<100&&_onexControllerBusy;n++)await Task.Delay(30);Check(!_onexControllerBusy&&_onexControllerPending==null,"Protect pending vibration settings");
   await ReadOnexControllerAsync();string before=ControllerEditorPolicy.Stringify();var tab=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);string open=_onexControllerOpen;bool guard=_onexControllerReadOnlyProjection;_onexControllerReadOnlyProjection=true;
   try{
    LegionNavItem.IsChecked=true;var state=JsonObject.Parse(_onexControllerState.Stringify());var policy=state.GetNamedObject("editor").GetNamedObject("policy");policy["vibration"]=new JsonObject{{"intensity",JsonValue.CreateNumberValue(45)},{"profile",JsonValue.CreateStringValue("Impact")},{"swapMotors",JsonValue.CreateBooleanValue(true)}};
    ApplyOnexControllerState(state.Stringify());SetOnexControllerCard("vibration");UpdateLayout();await Task.Delay(120);
    Check(_onexControllerRumbleProfile.Items.OfType<ComboBoxItem>().Select(item=>(string)item.Tag).SequenceEqual(new[]{"Balanced","Sharp","Soft","Impact","Boosted"}),"Preset list or English names wrong");
    Check((string)((ComboBoxItem)_onexControllerRumbleProfile.SelectedItem).Tag=="Impact"&&_onexControllerSwapMotors.IsChecked==true&&_onexControllerVibrationIntensity.Value==45,"Preset/swap/intensity projection wrong");
    Check(DescribeOnexControllerVibration(policy).Contains("Impact")&&DescribeOnexControllerVibration(policy).Contains("45%")&&DescribeOnexControllerVibration(policy).Contains("Swapped"),"Profile summary incomplete");
    foreach(var control in new Control[]{_onexControllerRumbleProfile,_onexControllerSwapMotors,_onexControllerVibrationIntensity,_onexControllerVibrationTest,_onexControllerVibrationReset}){
     Check(OnexControllerRows().SelectMany(row=>row).Contains(control),"Vibration control missing from navigation");control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(60);Check(control.Focus(FocusState.Keyboard),"Vibration native focus failed");
    }
    Check(_onexControllerVibrationIntensity.StepFrequency==1&&_onexControllerVibrationIntensity.SmallChange==5,"Existing intensity input steps changed");
    var bitmap=new Windows.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(_onexControllerRumbleProfile);var pixels=await bitmap.GetPixelsAsync();var bytes=new byte[pixels.Length];using(var reader=Windows.Storage.Streams.DataReader.FromBuffer(pixels))reader.ReadBytes(bytes);int dark=0,white=0,total=0;for(int n=0;n+3<bytes.Length;n+=4){if(bytes[n+3]<200)continue;total++;if(bytes[n]<110&&bytes[n+1]<110&&bytes[n+2]<110)dark++;if(bytes[n]>220&&bytes[n+1]>220&&bytes[n+2]>220)white++;}report["presetDarkPixels"]=JsonValue.CreateNumberValue(dark);report["presetWhitePixels"]=JsonValue.CreateNumberValue(white);Check(total>100&&dark>total/2&&white<total/3,"Preset chooser is still white/unreadable");await SaveOnexImage("onex-rumble317-preset.png",_onexControllerRumbleProfile,40);
    await SaveOnexImage("onex-rumble316-card.png",(FrameworkElement)_onexControllerSections.Single(section=>section.Id=="vibration").Detail.Child,80);
    policy["vibration"]=new JsonObject{{"intensity",JsonValue.CreateNumberValue(70)}};ApplyOnexControllerState(state.Stringify());Check((string)((ComboBoxItem)_onexControllerRumbleProfile.SelectedItem).Tag=="Balanced"&&_onexControllerSwapMotors.IsChecked!=true&&_onexControllerVibrationIntensity.Value==70,"Legacy intensity-only projection changed");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["projectionOnly"]=JsonValue.CreateBooleanValue(true);report["settingsWrites"]=JsonValue.CreateNumberValue(0);report["physicalOwnerPerceptionTested"]=JsonValue.CreateBooleanValue(false);report["motorTestInvokedHere"]=JsonValue.CreateBooleanValue(false);
   }finally{_onexControllerReadOnlyProjection=guard;await ReadOnexControllerAsync();Check(ControllerEditorPolicy.Stringify()==before,"Actual vibration profile changed during read-only UI check");SetOnexControllerCard(open);if(tab!=null)tab.IsChecked=true;}
  }
 }
}
