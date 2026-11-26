// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private StackPanel _onexControllerGyroProcessingPanel;private ComboBox _onexControllerGyroEngine;private Slider _onexControllerGyroSmoothing;private TextBlock _onexControllerGyroEngineHint;
  private void BuildOnexControllerGyroProcessing(StackPanel parent){
   var panel=_onexControllerGyroProcessingPanel=GyroSectionBody(parent,"processing","Motion processing","Linear","Adaptive steadies slow motion and follows quick turns. Precision adds speed-based gain. Linear is direct; Smoothing applies only to Adaptive or Precision. Higher smoothing reduces jitter but adds delay.");
   _onexControllerGyroEngine=GyroChoice(global::XboxGamingBar.OnexStrings.Get("Gyro Engine"),"OnexControllerGyroEngine",new[]{"Adaptive","Linear","Precision"},new[]{"Adaptive","Linear",OnexStrings.Get("Precision (MA inspired)")});
   var right=new StackPanel{Spacing=4};var value=ControllerHelp("50%");value.HorizontalAlignment=HorizontalAlignment.Right;right.Children.Add(OnexSettingsColumns(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Smoothing")),value));
   var slider=_onexControllerGyroSmoothing=new Slider{Name="OnexControllerGyroSmoothing",Minimum=0,Maximum=100,Value=50,StepFrequency=1,SmallChange=5,LargeChange=5,Style=(Style)Resources["OnexPreciseSliderStyle"]};right.Children.Add(slider);_onexControllerControls.Add(slider);
   void CommitSmoothing(){if(!_onexControllerSync)QueueOnexControllerGyroProcessing(GyroChoice(_onexControllerGyroEngine)=="Precision"?"precisionSmoothing":"adaptiveSmoothing",JsonValue.CreateNumberValue(slider.Value));}
   OnexInputGuard.Wire(slider,CommitSmoothing);OnexInputGuard.ConfigurePercentage(slider);slider.ValueChanged+=(s,e)=>{value.Text=slider.Value==0?global::XboxGamingBar.OnexStrings.Get("Off"):Math.Round(slider.Value)+"%";if(!_onexControllerSync&&OnexInputGuard.IsActive(slider))CommitSmoothing();};
   void CommitEngine(){if(!_onexControllerSync&&GyroChoice(_onexControllerGyroEngine)!=null)QueueOnexControllerGyroProcessing("engine",JsonValue.CreateStringValue(GyroChoice(_onexControllerGyroEngine)));}
   _onexControllerGyroEngine.SelectionChanged+=(s,e)=>{if(!_onexControllerGyroEngine.IsDropDownOpen&&OnexInputGuard.IsActive(_onexControllerGyroEngine))CommitEngine();};
   _onexControllerGyroEngine.DropDownClosed+=(s,e)=>{if(OnexInputGuard.IsActive(_onexControllerGyroEngine))CommitEngine();};
   panel.Children.Add(OnexSettingsColumns(_onexControllerGyroEngine,right));_onexControllerGyroEngineHint=ControllerHelp("");panel.Children.Add(_onexControllerGyroEngineHint);
  }
  private void QueueOnexControllerGyroProcessing(string key,IJsonValue value){
   QueueOnexControllerPolicy(policy=>{var gyro=OnexMotionObject(policy,"gyro")??new JsonObject();var processing=OnexMotionObject(gyro,"processing")??new JsonObject();processing[key]=value;gyro["processing"]=processing;policy["gyro"]=gyro;});
   bool was=_onexControllerSync;_onexControllerSync=true;try{ApplyOnexControllerGyro(_onexControllerPending??ControllerEditorPolicy);}finally{_onexControllerSync=was;}RefreshOnexControllerGyroAvailability();
  }
  private void ApplyOnexControllerGyroProcessing(JsonObject gyro){
   if(_onexControllerGyroEngine==null)return;var processing=OnexMotionObject(gyro,"processing");string engine=ControllerText(processing,"engine","Linear");
   if(!_onexControllerGyroEngine.IsDropDownOpen)_onexControllerGyroEngine.SelectedItem=_onexControllerGyroEngine.Items.OfType<ComboBoxItem>().First(i=>(string)i.Tag==engine);
   ApplyOnexControllerGyroThresholds(processing);ApplyOnexControllerGyroAxes(processing);ApplyOnexControllerGyroCurve(processing);
   if(!OnexInputGuard.IsEditing(_onexControllerGyroSmoothing))_onexControllerGyroSmoothing.Value=processing?.GetNamedNumber(engine=="Precision"?"precisionSmoothing":"adaptiveSmoothing",50)??50;
   _onexControllerGyroEngineHint.Text=engine=="Linear"?OnexStrings.Get(GyroLinearReason):engine=="Adaptive"?global::XboxGamingBar.OnexStrings.Get("Adaptive · steadies slow motion and follows quick changes more closely. Example: less reticle jitter while aiming, faster response for a turn."):global::XboxGamingBar.OnexStrings.Get("Precision · slower output for small movements, faster output for large turns, plus adaptive smoothing. Example: line up a distant target, then turn quickly.");
  }
  private void RefreshOnexControllerGyroProcessing(bool enabled){
   if(_onexControllerGyroEngine==null)return;_onexControllerGyroEngine.IsEnabled=enabled;_onexControllerGyroSmoothing.IsEnabled=enabled&&GyroChoice(_onexControllerGyroEngine)!="Linear";
  }
 }
}
