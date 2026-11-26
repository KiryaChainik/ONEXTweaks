// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private StackPanel _onexControllerGyroPanel;private TextBlock _onexControllerGyroModeHint,_onexControllerGyroRuntime;
  private ComboBox _onexControllerGyroTarget,_onexControllerGyroButton,_onexControllerGyroActivation,_onexControllerGyroBoostButton;
  private Slider _onexControllerGyroX,_onexControllerGyroY,_onexControllerGyroBoost;private Button _onexControllerGyroReset;
  private TextBlock _onexGyroButtonHint,_onexGyroStickHint,_onexGyroConversionHint;
  private const string GyroTargetReason="Gyro aiming is Off. You can edit its settings now. Choose Left Stick, Right Stick or Mouse under Use gyro as to apply them.";
  private const string GyroAlwaysReason="Always keeps aiming active. Choose Hold or Toggle to select an activation button.";
  private const string GyroBoostReason="Choose a Boost button to set Hold-boost sensitivity.";
  private const string GyroOemReason="Turn on Extra Buttons to use M1 or M2 for activation or boost.";
  private const string GyroMouseReason="Mouse produces pointer movement. Choose Left Stick or Right Stick to enable Anti-deadzone, gyro-to-stick horizontal input and the stick response curve.";
  private const string GyroCurveFloorReason="The custom response curve controls minimum stick output. Adjust Min stick output in the curve, or turn the curve off to use Anti-deadzone.";
  private const string GyroWorldReason="Gravity-relative aiming sets the horizontal axis. Turn off Gravity-relative aiming to choose yaw, roll or both.";
  private const string GyroLinearReason="Linear uses direct input. Choose Adaptive or Precision under Gyro Engine to enable Smoothing.";
  private static void SetGyroHint(TextBlock hint,string text)
  {
   if(hint==null)return;
   hint.Text=text??"";hint.Visibility=string.IsNullOrEmpty(text)?Visibility.Collapsed:Visibility.Visible;
  }
  private string GyroChoice(ComboBox choice)=>(choice?.SelectedItem as ComboBoxItem)?.Tag as string;
  private ComboBox GyroChoice(string title,string name,string[] tags,string[] labels=null){
   var choice=ControllerChoice(title,name,tags);choice.RequestedTheme=ElementTheme.Dark;choice.MaxDropDownHeight=280;choice.Background=OnexSettingsBrush(39,47,60);choice.Foreground=OnexSettingsBrush(238,241,246);
   foreach(string themeName in new[]{"Default","Dark","Light"}){
    var theme=new ResourceDictionary();foreach(string key in new[]{"ComboBoxBackground","ComboBoxBackgroundPointerOver","ComboBoxBackgroundPressed","ComboBoxDropDownBackground"})theme[key]=OnexSettingsBrush(39,47,60);
    foreach(string key in new[]{"ComboBoxForeground","ComboBoxForegroundPointerOver","ComboBoxForegroundPressed","ComboBoxDropDownForeground"})theme[key]=OnexSettingsBrush(238,241,246);choice.Resources.ThemeDictionaries[themeName]=theme;
   }
   for(int n=0;n<choice.Items.Count;n++){var item=(ComboBoxItem)choice.Items[n];if(labels!=null)item.Content=OnexStrings.Get(labels[n]);item.RequestedTheme=ElementTheme.Dark;item.Background=OnexSettingsBrush(39,47,60);item.Foreground=OnexSettingsBrush(238,241,246);}
   return choice;
  }
  private void BuildOnexControllerGyro(StackPanel parent){
   var panel=_onexControllerGyroPanel=new StackPanel{Name="OnexControllerGyroPanel",Spacing=12};parent.Children.Add(panel);panel.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Gyro aiming · stick / mouse"),FontSize=18,FontWeight=Windows.UI.Text.FontWeights.SemiBold});
   panel.Children.Add(ControllerHelp(OnexStrings.Get("Turns console motion into stick or mouse input. Works with virtual Xbox 360 and DualShock 4. Direct sensor input for games is a separate option below.")));
   _onexControllerGyroModeHint=ControllerHelp("");panel.Children.Add(_onexControllerGyroModeHint);_onexControllerGyroRuntime=ControllerHelp("");panel.Children.Add(_onexControllerGyroRuntime);
   var aim=GyroSectionBody(panel,"aim","Activation & sensitivity","Settings only · aiming is Off","Hold uses an activation button while held; Toggle arms on press; Always needs no button. Sensitivity sets horizontal and vertical speed. A boost button temporarily uses Hold-boost sensitivity.");var left=new StackPanel{Spacing=10};var right=new StackPanel{Spacing=10};aim.Children.Add(OnexSettingsColumns(left,right));
   _onexControllerGyroTarget=GyroChoice(global::XboxGamingBar.OnexStrings.Get("Use gyro as"),"OnexControllerGyroTarget",new[]{"Disabled","LeftStick","RightStick","Mouse"},new[]{"Off · settings only","Left Stick","Right Stick","Mouse"});
   _onexControllerGyroButton=GyroChoice(global::XboxGamingBar.OnexStrings.Get("Activate button"),"OnexControllerGyroButton",new[]{"None","LB","LT","RB","RT","OEM:M1","OEM:M2"},new[]{"None","LB","LT","RB","RT","M1","M2"});
   _onexControllerGyroActivation=GyroChoice(global::XboxGamingBar.OnexStrings.Get("Activation"),"OnexControllerGyroActivation",new[]{"Always","Hold","Toggle"});
   _onexControllerGyroBoostButton=GyroChoice(global::XboxGamingBar.OnexStrings.Get("Boost button"),"OnexControllerGyroBoostButton",new[]{"None","LB","LT","RB","RT","OEM:M1","OEM:M2"},new[]{"None","LB","LT","RB","RT","M1","M2"});
   foreach(var pair in new[]{Tuple.Create(_onexControllerGyroTarget,"target"),Tuple.Create(_onexControllerGyroButton,"activationButton"),Tuple.Create(_onexControllerGyroActivation,"activation"),Tuple.Create(_onexControllerGyroBoostButton,"boostButton")}){
    var choice=pair.Item1;string key=pair.Item2;if(key=="target")panel.Children.Insert(2,choice);else left.Children.Add(choice);
    void Commit(){if(!_onexControllerSync&&GyroChoice(choice)!=null)QueueOnexControllerGyro(key,JsonValue.CreateStringValue(GyroChoice(choice)));}
    choice.SelectionChanged+=(s,e)=>{if(!choice.IsDropDownOpen&&OnexInputGuard.IsActive(choice))Commit();};choice.DropDownClosed+=(s,e)=>{if(OnexInputGuard.IsActive(choice))Commit();};
   }
   Slider Percent(string caption,string name,string key,int minimum,int maximum,int initial){
    var value=new TextBlock{Tag="OnexControllerTuningValue",Text=initial+"%",HorizontalAlignment=HorizontalAlignment.Right};right.Children.Add(OnexSettingsColumns(ControllerHelp(caption),value));
    var slider=new Slider{Name=name,Minimum=minimum,Maximum=maximum,Value=initial,SmallChange=5,StepFrequency=1,Style=(Style)Resources["OnexPreciseSliderStyle"]};
    Action commit=()=>{if(!_onexControllerSync)QueueOnexControllerGyro(key,JsonValue.CreateNumberValue(slider.Value));};OnexInputGuard.Wire(slider,commit);OnexInputGuard.ConfigurePercentage(slider);
    slider.ValueChanged+=(s,e)=>{value.Text=Math.Round(slider.Value)+"%";if(!_onexControllerSync&&OnexInputGuard.IsActive(slider))commit();};_onexControllerControls.Add(slider);right.Children.Add(slider);return slider;
   }
   _onexControllerGyroX=Percent("Sensitivity X","OnexControllerGyroX","sensitivityX",1,100,70);_onexControllerGyroY=Percent("Sensitivity Y","OnexControllerGyroY","sensitivityY",1,100,70);_onexControllerGyroBoost=Percent("Hold-boost","OnexControllerGyroBoost","boostPercent",10,300,100);
   _onexGyroButtonHint=ControllerHelp("");aim.Children.Add(_onexGyroButtonHint);
   BuildOnexControllerGyroProcessing(panel);
   BuildOnexControllerGyroThresholds(panel);
   _onexGyroStickHint=ControllerHelp("");panel.Children.Add(_onexGyroStickHint);
   BuildOnexControllerGyroAxes(panel);
   BuildOnexControllerGyroCurve(panel);
   _onexControllerGyroReset=CreateOnexControllerGroupReset("gyro","OnexControllerGyroRestore");_onexControllerGyroReset.Content=global::XboxGamingBar.OnexStrings.Get("Restore Gyro Defaults");panel.Children.Add(_onexControllerGyroReset);
  }
  private void QueueOnexControllerGyro(string key,IJsonValue value){
   QueueOnexControllerPolicy(policy=>{var gyro=OnexMotionObject(policy,"gyro")??new JsonObject();gyro[key]=value;policy["gyro"]=gyro;});
   bool was=_onexControllerSync;_onexControllerSync=true;try{ApplyOnexControllerGyro(_onexControllerPending??ControllerEditorPolicy);}finally{_onexControllerSync=was;}RefreshOnexControllerGyroAvailability();
  }
  private void ApplyOnexControllerGyro(JsonObject policy){
   if(_onexControllerGyroTarget==null)return;var gyro=OnexMotionObject(policy,"gyro");
   void Select(ComboBox choice,string key,string fallback){if(!choice.IsDropDownOpen)choice.SelectedItem=choice.Items.OfType<ComboBoxItem>().First(item=>(string)item.Tag==ControllerText(gyro,key,fallback));}
   Select(_onexControllerGyroTarget,"target","Disabled");Select(_onexControllerGyroButton,"activationButton","None");Select(_onexControllerGyroActivation,"activation","Hold");Select(_onexControllerGyroBoostButton,"boostButton","None");
   if(!OnexInputGuard.IsEditing(_onexControllerGyroX))_onexControllerGyroX.Value=gyro?.GetNamedNumber("sensitivityX",70)??70;
   if(!OnexInputGuard.IsEditing(_onexControllerGyroY))_onexControllerGyroY.Value=gyro?.GetNamedNumber("sensitivityY",70)??70;
   if(!OnexInputGuard.IsEditing(_onexControllerGyroBoost))_onexControllerGyroBoost.Value=gyro?.GetNamedNumber("boostPercent",100)??100;
   ApplyOnexControllerGyroProcessing(gyro);
   var runtime=OnexMotionObject(OnexMotionObject(_onexControllerState,"runtime"),"gyro");_onexControllerGyroRuntime.Text=global::XboxGamingBar.OnexStrings.Format("Current aiming: {0}",ControllerText(runtime,"status","Disabled"));
   RefreshOnexControllerGyroAvailability();
  }
  private void RefreshOnexControllerGyroAvailability(){
   if(_onexControllerGyroTarget==null)return;bool ready=App.IsConnected&&_onexControllerState?.ContainsKey("entries")==true&&OnexControllerEditingVirtual;
   // Editing a saved profile does not require emitting gyro input. In particular,
   // Off must not disable the axes/engine controls needed to prepare that profile.
   bool enabled=ready;bool aiming=GyroChoice(_onexControllerGyroTarget)!="Disabled";_onexControllerGyroTarget.IsEnabled=ready;
   _onexControllerGyroActivation.IsEnabled=enabled;_onexControllerGyroButton.IsEnabled=enabled&&GyroChoice(_onexControllerGyroActivation)!="Always";_onexControllerGyroBoostButton.IsEnabled=enabled;
   _onexControllerGyroX.IsEnabled=enabled;_onexControllerGyroY.IsEnabled=enabled;_onexControllerGyroBoost.IsEnabled=enabled&&GyroChoice(_onexControllerGyroBoostButton)!="None";_onexControllerGyroReset.IsEnabled=ready;
   RefreshOnexControllerGyroProcessing(enabled);RefreshOnexControllerGyroThresholds(enabled);RefreshOnexControllerGyroAxes(enabled);RefreshOnexControllerGyroCurve(enabled);
   bool rear=_onexControllerState?.GetNamedBoolean("oemEnabled",false)==true;foreach(var choice in new[]{_onexControllerGyroButton,_onexControllerGyroBoostButton})foreach(var item in choice.Items.OfType<ComboBoxItem>())item.IsEnabled=!((string)item.Tag).StartsWith("OEM:",StringComparison.Ordinal)||rear;
   _onexControllerGyroModeHint.Text=!ready?OnexControllerSoftwareTuningLockReason():
    !aiming?OnexStrings.Get(GyroTargetReason):OnexStrings.Get("Settings save automatically in the selected controller profile.");
   // Explain only the restrictions that can be addressed at the current target.
   var buttons=new List<string>();
   if(enabled&&!_onexControllerGyroButton.IsEnabled)buttons.Add(OnexStrings.Get(GyroAlwaysReason));
   if(enabled&&!_onexControllerGyroBoost.IsEnabled)buttons.Add(OnexStrings.Get(GyroBoostReason));
   if(enabled&&!rear)buttons.Add(OnexStrings.Get(GyroOemReason));
   SetGyroHint(_onexGyroButtonHint,string.Join("\n",buttons));
   _onexControllerGyroEngineHint.Visibility=enabled?Visibility.Visible:Visibility.Collapsed;
   bool mouse=GyroChoice(_onexControllerGyroTarget)=="Mouse";
   SetGyroHint(_onexGyroStickHint,!enabled?null:mouse?OnexStrings.Get(GyroMouseReason):
    _onexControllerGyroCurveEnable.IsChecked==true?OnexStrings.Get(GyroCurveFloorReason):null);
   SetGyroHint(_onexGyroConversionHint,enabled&&!mouse&&_onexControllerGyroWorld.IsChecked==true?OnexStrings.Get(GyroWorldReason):null);
   RefreshOnexGyroNativeMotionAvailability();RefreshOnexGyroSectionSummaries();
   WireOnexControllerNavigation();
  }
  private string DescribeOnexControllerGyro(JsonObject policy){
   var gyro=OnexMotionObject(policy,"gyro");string target=ControllerText(gyro,"target","Disabled");if(target=="Disabled")return global::XboxGamingBar.OnexStrings.Get("Off");
   string button=ControllerText(gyro,"activationButton","None"),mode=ControllerText(gyro,"activation","Hold");return (target=="LeftStick"?global::XboxGamingBar.OnexStrings.Get("Left Stick"):target=="RightStick"?global::XboxGamingBar.OnexStrings.Get("Right Stick"):OnexStrings.Get(target))+" · "+(button=="None"||mode=="Always"?global::XboxGamingBar.OnexStrings.Get("Always"):OnexStrings.Get(mode)+" "+OnexControllerSourceName(button))+" · "+OnexStrings.Get(ControllerText(OnexMotionObject(gyro,"processing"),"engine","Linear"))+(target=="Mouse"||OnexMotionObject(gyro,"processing")?.GetNamedBoolean("worldSpace",false)==true?"":" · "+(ControllerText(OnexMotionObject(gyro,"processing"),"stickConversion","Yaw")=="YawRoll"?OnexStrings.Get("Yaw + Roll"):OnexStrings.Get(ControllerText(OnexMotionObject(gyro,"processing"),"stickConversion","Yaw"))));
  }
  private IEnumerable<Control[]> OnexControllerGyroRows(){
   yield return new Control[]{_onexControllerGyroTarget};
   foreach(var row in OnexGyroSectionRows("aim",new[]{new Control[]{_onexControllerGyroButton,_onexControllerGyroX},new Control[]{_onexControllerGyroActivation,_onexControllerGyroY},new Control[]{_onexControllerGyroBoostButton,_onexControllerGyroBoost}}))yield return row;
   foreach(var row in OnexGyroSectionRows("processing",new[]{new Control[]{_onexControllerGyroEngine,_onexControllerGyroSmoothing}}))yield return row;
   foreach(var row in OnexGyroSectionRows("thresholds",new[]{new Control[]{_onexControllerGyroGate,_onexControllerGyroFloor}}))yield return row;
   foreach(var row in OnexGyroSectionRows("axes",new[]{new Control[]{_onexControllerGyroWorld},new Control[]{_onexControllerGyroConversion},new Control[]{_onexControllerGyroInvertX,_onexControllerGyroInvertY},new Control[]{_onexControllerGyroRotation}}))yield return row;
   foreach(var row in OnexGyroSectionRows("curve",OnexControllerGyroCurveRows()))yield return row;
   yield return new Control[]{_onexControllerGyroReset};yield return new Control[]{_onexGyroNativeMotion};
   foreach(var row in OnexGyroSectionRows("calibration",new[]{new Control[]{_onexControllerMotionCheck,_onexControllerMotionCalibrate},new Control[]{_onexControllerMotionReset}}))yield return row;
  }
 }
}
