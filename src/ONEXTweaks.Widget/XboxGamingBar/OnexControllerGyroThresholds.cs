// SPDX-License-Identifier: AGPL-3.0-only
using System;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private StackPanel _onexControllerGyroThresholdsPanel;private Slider _onexControllerGyroGate,_onexControllerGyroFloor;
  private void BuildOnexControllerGyroThresholds(StackPanel parent){
   var panel=_onexControllerGyroThresholdsPanel=GyroSectionBody(parent,"thresholds","Small movements","Gate 0.0°/s · anti-deadzone 0%","Gate ignores small rotations before filtering. Anti-deadzone is for stick targets only: it raises a nonzero output above the game deadzone. Zero input remains zero.");
   var left=new StackPanel{Spacing=4};var right=new StackPanel{Spacing=4};
   Slider Make(StackPanel column,string caption,string name,string key,int max,bool percentage){
    var text=ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Off"));text.HorizontalAlignment=HorizontalAlignment.Right;column.Children.Add(OnexSettingsColumns(ControllerHelp(caption),text));
    var slider=new Slider{Name=name,Minimum=0,Maximum=max,StepFrequency=1,SmallChange=percentage?5:1,LargeChange=5,Style=(Style)Resources["OnexPreciseSliderStyle"]};column.Children.Add(slider);
    Action commit=()=>{if(!_onexControllerSync)QueueOnexControllerGyroProcessing(key,JsonValue.CreateNumberValue(slider.Value));};
    OnexInputGuard.Wire(slider,commit);if(percentage)OnexInputGuard.ConfigurePercentage(slider);
    slider.ValueChanged+=(s,e)=>{text.Text=slider.Value==0?global::XboxGamingBar.OnexStrings.Get("Off"):percentage?Math.Round(slider.Value)+"%":(slider.Value/10).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+global::XboxGamingBar.OnexStrings.Get("°/s");if(!_onexControllerSync&&OnexInputGuard.IsActive(slider))commit();};_onexControllerControls.Add(slider);return slider;
   }
   _onexControllerGyroGate=Make(left,"Gyro gate","OnexControllerGyroGate","gateTenths",100,false);
   _onexControllerGyroFloor=Make(right,"Anti-deadzone","OnexControllerGyroFloor","antiDeadzone",50,true);
   panel.Children.Add(OnexSettingsColumns(left,right));
  }
  private void ApplyOnexControllerGyroThresholds(JsonObject processing){
   if(_onexControllerGyroGate==null)return;
   if(!OnexInputGuard.IsEditing(_onexControllerGyroGate))_onexControllerGyroGate.Value=processing?.GetNamedNumber("gateTenths",0)??0;
   if(!OnexInputGuard.IsEditing(_onexControllerGyroFloor))_onexControllerGyroFloor.Value=processing?.GetNamedNumber("antiDeadzone",0)??0;
  }
  private void RefreshOnexControllerGyroThresholds(bool enabled){
   if(_onexControllerGyroGate==null)return;_onexControllerGyroGate.IsEnabled=enabled;_onexControllerGyroFloor.IsEnabled=enabled&&GyroChoice(_onexControllerGyroTarget)!="Mouse";
  }
 }
}
