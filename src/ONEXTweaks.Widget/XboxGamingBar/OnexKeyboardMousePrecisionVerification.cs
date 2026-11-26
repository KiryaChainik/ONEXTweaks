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
  private async Task VerifyOnexKeyboardMousePrecisionAsync(JsonObject report)
  {
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   EnsureOnexKeyboardMouseFrontend();await FlushKbmTuning();for(int n=0;n<100&&_kbmBusy;n++)await Task.Delay(30);Check(!_kbmBusy&&_kbmTuning.Count==0,"Protect pending keyboard/mouse changes");
   var tab=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);double offset=_onexKeyboardMousePage.VerticalOffset;
   var originalCard=_kbmOpenSection;
   await ReadOnexKeyboardMouseAsync();Check(KbmPolicy!=null,"Live keyboard/mouse state unavailable");string before=KbmPolicy.Stringify();bool projection=_kbmReadOnlyProjection;_kbmReadOnlyProjection=true;
   try{
    var state=JsonObject.Parse(_kbmState.Stringify());state["generation"]=JsonValue.CreateStringValue("ONEXQA-MousePrecision309");var policy=state.GetNamedObject("policy");policy["stickDeadzone"]=JsonValue.CreateNumberValue(5);policy["holdAcceleration"]=JsonValue.CreateNumberValue(75);policy["nudgeStep"]=JsonValue.CreateNumberValue(23);
    ApplyOnexKeyboardMouseState(state.Stringify());_onexKeyboardMouseNav.IsChecked=true;SetKbmSettingsCard(null);await Task.Delay(120);
    var speed=_kbmSections[0];
    var precision=_kbmSections[1];
    var mapping=_kbmSections[2];
    Check(Grid.GetRow(speed.Header)==0&&Grid.GetColumn(speed.Header)==0&&
      Grid.GetRow(precision.Header)==0&&Grid.GetColumn(precision.Header)==1&&Grid.GetRow(mapping.Header)==2&&
      Grid.GetColumnSpan(mapping.Header)==2,"Keyboard/mouse tile order or full-width remap changed");
    Check(Math.Abs(mapping.Header.ActualWidth-_kbmCards.ActualWidth)<2&&_kbmSections.All(card=>card.Detail.Visibility==Visibility.Collapsed),
      "Initial card geometry or collapsed state is wrong");
    await SaveOnexImage("onex-kbm-tiles.png",_kbmCards,60);
    SetKbmSettingsCard(speed);
    Check(_kbmOutline.Connected&&KbmRows().SelectMany(row=>row).Contains(_kbmScrollSpeed)&&
      !KbmRows().SelectMany(row=>row).Contains(_kbmDeadzone),"Mouse speed card has incorrect focus contents");
    SetKbmSettingsCard(precision);
    Check(_kbmOutline.Connected&&speed.Detail.Visibility==Visibility.Collapsed,"Precision must supersede mouse speed");
    report["precisionValues"]=JsonArray.Parse("["+_kbmDeadzone.Value+","+_kbmHoldAcceleration.Value+","+_kbmNudgeStep.Value+"]");Check(_kbmDeadzone.Value==5&&_kbmHoldAcceleration.Value==75&&_kbmNudgeStep.Value==23,"Projected advanced values not displayed");
    Check(_kbmMouseSpeed.Minimum==1&&_kbmMouseSpeed.Maximum==400&&_kbmScrollSpeed.Minimum==25&&_kbmScrollSpeed.Maximum==300,"Sensitivity range or old scroll range changed");
    foreach(var slider in new[]{_kbmDeadzone,_kbmHoldAcceleration,_kbmNudgeStep}){
     Check(slider.StepFrequency==1,"Touch precision is not1");
     Check(slider.SmallChange==(ReferenceEquals(slider,_kbmNudgeStep)?1:5),"Gamepad percent/pixel step wrong");
     slider.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(80);Check(slider.Focus(FocusState.Keyboard),"Advanced control cannot receive focus");Check(KbmRows().SelectMany(row=>row).Contains(slider),"Advanced slider missing from navigation");
     Check(slider.ActualWidth>200&&slider.ActualHeight>0,"Advanced slider has no usable native geometry");
    }
    Check(_kbmDeadzone.Minimum==0&&_kbmDeadzone.Maximum==20&&_kbmHoldAcceleration.Minimum==0&&_kbmHoldAcceleration.Maximum==100&&_kbmNudgeStep.Minimum==1&&_kbmNudgeStep.Maximum==50,"Advanced boundaries wrong");
    Check(KbmRows().SelectMany(row=>row).Contains(_kbmPrecisionReset),"Precision Restore Default missing from navigation");
    var precisionCard=_kbmPrecisionCard;
    Check(precisionCard.Child is StackPanel&&precisionCard.ActualWidth>300,"Precision card missing");
    await SaveOnexImage("onex-mouse309-precision.png",precisionCard,80);
    SetKbmSettingsCard(mapping);
    Check(_kbmOutline.Connected&&KbmRows().SelectMany(row=>row).Contains(_kbmFn)&&
      KbmRows().SelectMany(row=>row).Contains(_kbmTiles["base:A"]),"Full remapping must contain FN and base actions");
    _kbmSync=true;try{BuildKbmEditor();}finally{_kbmSync=false;}
    var actions=_kbmAction.Items.OfType<ComboBoxItem>().Where(item=>item.Tag is JsonObject value&&ControllerText(value,"kind")=="nudge").ToArray();Check(actions.Length==4&&actions.All(item=>item.IsEnabled),"Four nudge actions unavailable");
    foreach(string layer in new[]{"base","fn"}){
     _kbmLayer=layer;_kbmSource="A";_kbmSync=true;try{RefreshKbmEditor();}finally{_kbmSync=false;}
     foreach(var action in actions){_kbmSync=true;try{_kbmAction.SelectedItem=action;}finally{_kbmSync=false;}Check(_kbmAction.SelectedItem==action,"Nudge cannot be selected in each layer");}
    }
    report["checks"]=JsonValue.CreateNumberValue(checks);report["nativeFocusControls"]=JsonValue.CreateNumberValue(3);report["projectionOnly"]=JsonValue.CreateBooleanValue(true);report["settingsWrites"]=JsonValue.CreateNumberValue(0);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["gamepadEventsInjectedHere"]=JsonValue.CreateBooleanValue(false);
   }finally{
    _kbmFlyout?.Hide();SetKbmSettingsCard(originalCard);_kbmReadOnlyProjection=projection;_kbmState=null;await ReadOnexKeyboardMouseAsync();Check(KbmPolicy.Stringify()==before,"Actual keyboard/mouse preferences changed during read-only UI check");if(tab!=null)tab.IsChecked=true;_onexKeyboardMousePage.ChangeView(null,offset,null,true);
   }
  }
 }
}
