// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexControllerMacrosAsync(JsonObject report)
  {
   int checks=0,invokes=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);string source=ControllerSourceId,real=null;bool oldSync=_onexControllerSync;
   void Invoke(Button button){Check(button.IsEnabled,"Macro UI button is disabled");var provider=new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider;Check(provider!=null,"Macro UI invoke provider missing");provider.Invoke();invokes++;}
   try{
    LegionNavItem.IsChecked=true;await ReadOnexControllerAsync();Check(_onexControllerPending==null&&!_onexControllerBusy,"Protect owner pending controller edits");real=_onexControllerState.Stringify();_onexControllerReadOnlyProjection=true;
    // Synchronous read-only DTO projection. _sync blocks policy persistence;
    // the actual backend/capture mode and all saved profiles remain untouched.
    _onexControllerSync=true;var projected=JsonObject.Parse(real);projected["enabled"]=JsonValue.CreateBooleanValue(true);var policy=projected.GetNamedObject("editor").GetNamedObject("policy");policy["virtualTarget"]=JsonValue.CreateStringValue("Xbox360");policy["useHardwareController"]=JsonValue.CreateBooleanValue(false);policy["gamepadRemappingDisabled"]=JsonValue.CreateBooleanValue(false);
    var binding=JsonObject.Parse("{\"kind\":\"macro\",\"macro\":{\"buttons\":[\"B\",\"Y\"],\"mode\":\"repeatHeld\",\"intervalMs\":160,\"pressMs\":60}}");var bindings=policy.GetNamedObject("bindings",null)??new JsonObject();bindings["A"]=binding;policy["bindings"]=bindings;
    ApplyOnexControllerState(projected.Stringify());SetOnexControllerCard("mapping");SelectOnexControllerMappingSource("A",_onexControllerInputTiles["A"],true);
    await Task.Delay(160);
    Check(_onexControllerKind.Items.Count==10&&OnexControllerEditingSequence&&_onexControllerGamepadMode.IsEnabled,"Macro assignment does not load on standard source");Check(_onexControllerMacroPanel.Visibility==Visibility.Visible,"Macro panel hidden");Check(_onexControllerMacroSteps.SequenceEqual(new[]{"B","Y"})&&_onexControllerMacroMode.SelectedIndex==1,"Stored macro sequence/mode not loaded");Check(_onexControllerMacroInterval.Value==160&&_onexControllerMacroPress.Value==60,"Stored timings not loaded");Check(_onexControllerInputSummaries["A"].Text.Contains("Macro"),"Macro source summary absent");
    var popup=_onexControllerMappingFlyout;
    Check(popup.IsOpen&&_controllerPadArt?.View.Visibility==Visibility.Visible&&_onexControllerPadTarget.Visibility==Visibility.Collapsed,
      "Gamepad sequence did not reuse the normal button editor and diagram");
    _onexControllerGamepadMode.SelectedIndex=0;UpdateOnexControllerMappingVisibility();
    Check(ReferenceEquals(popup,_onexControllerMappingFlyout)&&popup.IsOpen&&_onexControllerPadTarget.Visibility==Visibility.Visible&&
      _onexControllerMacroPanel.Visibility==Visibility.Collapsed,"Single mode replaced the existing Gamepad editor");
    _onexControllerGamepadMode.SelectedIndex=1;UpdateOnexControllerMappingVisibility();
    Invoke((Button)_controllerPadArt.Controls.Single(control=>Equals(control.Tag,"X")));
    await Task.Delay(90);
    Check(_onexControllerMacroSteps.SequenceEqual(new[]{"B","Y","X"})&&_onexControllerMacroStep.SelectedIndex==2&&
      _controllerPadArt.HighlightUri.AbsolutePath.EndsWith("/gamepad-X.png",StringComparison.Ordinal),
      "Diagram did not append and highlight the selected step");
    Invoke(_onexControllerMacroRemove);
    _onexControllerMacroStep.SelectedIndex=1;SelectOnexControllerMacroTarget();Invoke(_onexControllerMacroAdd);Check(_onexControllerMacroSteps.SequenceEqual(new[]{"B","Y","Y"})&&_onexControllerMacroStep.SelectedIndex==2,"Add step/selection failed");Invoke(_onexControllerMacroUp);Check(_onexControllerMacroStep.SelectedIndex==1,"Move earlier selection failed");Invoke(_onexControllerMacroRemove);Check(_onexControllerMacroSteps.SequenceEqual(new[]{"B","Y"}),"Remove/reorder changed wrong step");
    foreach(int mode in new[]{0,1,2,3}){_onexControllerMacroMode.SelectedIndex=mode;UpdateOnexControllerMacroVisibility();Check((_onexControllerMacroInterval.Visibility==Visibility.Visible)==(mode==1||mode==3),"Timing visibility differs from Claw activation modes");Check(OnexControllerMacroBinding().GetNamedString("mode")==OnexMacroModes[mode],"Macro activation serialization wrong");}
    _onexControllerMacroSteps.Clear();_onexControllerMacroSteps.AddRange(Enumerable.Repeat("A",32));RefreshOnexControllerMacroSteps();Check(!_onexControllerMacroAdd.IsEnabled,"32-step limit not reflected in UI");_onexControllerMacroSteps.RemoveRange(1,31);RefreshOnexControllerMacroSteps();Check(!_onexControllerMacroRemove.IsEnabled&&!_onexControllerMacroUp.IsEnabled&&!_onexControllerMacroDown.IsEnabled,"Single-step toolbar bounds wrong");
    _onexControllerMacroMode.SelectedIndex=1;UpdateOnexControllerMacroVisibility();WireOnexControllerGestureNavigation();var focus=OnexControllerEditorFocusControls();Check(focus.Contains(_onexControllerMacroMode)&&focus.Contains(_onexControllerMacroInterval)&&focus.Contains(_onexControllerBindingReset),"Macro controls missing from gamepad focus graph");Check(_onexControllerMacroInterval.SmallChange==10&&_onexControllerMacroInterval.StepFrequency==10&&_onexControllerMacroPress.Minimum==10&&_onexControllerMacroPress.Maximum==500,"Timing UI bounds wrong");
    var oem=policy.GetNamedObject("oem",null)??new JsonObject();var extra=oem.GetNamedObject("bindings",null)??new JsonObject();extra["M1"]=JsonObject.Parse(binding.Stringify());oem["bindings"]=extra;oem["doublePresses"]=JsonObject.Parse("{\"M1\":{\"enabled\":true,\"windowMs\":300,\"binding\":"+binding.Stringify()+"}}");policy["oem"]=oem;ApplyOnexControllerState(projected.Stringify());SetOnexControllerCard("extra");_onexControllerSource.SelectedItem=_onexControllerSource.Items.OfType<ComboBoxItem>().First(item=>(string)item.Tag=="OEM:M1");RefreshOnexControllerMapping();Check(OnexControllerEditingSequence,"Extra Single macro missing");_onexControllerGesture.SelectedIndex=1;RefreshOnexControllerMapping();Check(ControllerEditingDouble&&OnexControllerEditingSequence&&_onexControllerMacroSteps.SequenceEqual(new[]{"B","Y"}),"Extra Double macro missing");
    projected["enabled"]=JsonValue.CreateBooleanValue(false);ApplyOnexControllerState(projected.Stringify());Check(!_onexControllerGamepadMode.IsEnabled&&OnexControllerMacroControls().Length==0,"Hardware exposes macro editing");
    Check(_onexControllerReadOnlyProjection&&_onexControllerPending==null,"Read-only UI projection queued owner profile write");report["checks"]=JsonValue.CreateNumberValue(checks);report["automationInvokes"]=JsonValue.CreateNumberValue(invokes);report["uiEvidence"]=JsonValue.CreateStringValue("synchronous UI DTO projections and AutomationPeer button invokes; persistence blocked by sync guard");report["productionModeChanged"]=JsonValue.CreateBooleanValue(false);report["ownerProfileEdits"]=JsonValue.CreateNumberValue(0);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
   }finally{_onexControllerMappingFlyout?.Hide();if(_onexControllerReadOnlyProjection){_onexControllerSaveTimer.Stop();Check(_onexControllerPending==null,"Visual projection unexpectedly queued a controller edit");}_onexControllerSync=oldSync;if(real!=null)ApplyOnexControllerState(real);_onexControllerReadOnlyProjection=false;await ReadOnexControllerAsync();if(source!=null)_onexControllerSource.SelectedItem=_onexControllerSource.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>(string)item.Tag==source);if(_onexControllerOpen!=null)SetOnexControllerCard(null);if(original!=null)original.IsChecked=true;}
  }
 }
}
