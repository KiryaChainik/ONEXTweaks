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
  private StackPanel _onexControllerGyroAxesPanel;private CheckBox _onexControllerGyroWorld,_onexControllerGyroInvertX,_onexControllerGyroInvertY;private ComboBox _onexControllerGyroRotation,_onexControllerGyroConversion;
  private void BuildOnexControllerGyroAxes(StackPanel parent){
   var panel=_onexControllerGyroAxesPanel=GyroSectionBody(parent,"axes","Axes & orientation","Console axes · 0°","Gravity-relative aiming uses acceleration to find vertical. Console axes use Yaw, Roll or both. Invert reverses direction; Output rotation changes the aiming frame. These settings change aiming output, not the physical orientation display.");
   CheckBox Flag(string name,string caption,string key){var box=new CheckBox{Name=name,Content=OnexStrings.Get(caption)};OnexInputGuard.Wire(box);OnexAccentResources(box,OnexPalette.Magenta);box.Click+=(s,e)=>{if(!_onexControllerSync)QueueOnexControllerGyroProcessing(key,JsonValue.CreateBooleanValue(box.IsChecked==true));};return box;}
   _onexControllerGyroWorld=Flag("OnexControllerGyroWorld",OnexStrings.Get("Gravity-relative aiming (accelerometer)"),"worldSpace");panel.Children.Add(_onexControllerGyroWorld);
   _onexControllerGyroConversion=GyroChoice(global::XboxGamingBar.OnexStrings.Get("Gyro-to-stick horizontal input"),"OnexControllerGyroConversion",new[]{"Yaw","Roll","YawRoll"},new[]{OnexStrings.Get("Yaw · turn left / right"),OnexStrings.Get("Roll · tilt left / right"),OnexStrings.Get("Yaw + Roll · combine both")});panel.Children.Add(_onexControllerGyroConversion);
   _onexGyroConversionHint=ControllerHelp("");panel.Children.Add(_onexGyroConversionHint);
   void CommitConversion(){if(!_onexControllerSync&&GyroChoice(_onexControllerGyroConversion)!=null)QueueOnexControllerGyroProcessing("stickConversion",JsonValue.CreateStringValue(GyroChoice(_onexControllerGyroConversion)));}
   _onexControllerGyroConversion.SelectionChanged+=(s,e)=>{if(!_onexControllerGyroConversion.IsDropDownOpen&&OnexInputGuard.IsActive(_onexControllerGyroConversion))CommitConversion();};_onexControllerGyroConversion.DropDownClosed+=(s,e)=>{if(OnexInputGuard.IsActive(_onexControllerGyroConversion))CommitConversion();};
   _onexControllerGyroInvertX=Flag("OnexControllerGyroInvertX","Invert X","invertX");_onexControllerGyroInvertY=Flag("OnexControllerGyroInvertY","Invert Y","invertY");panel.Children.Add(OnexSettingsColumns(_onexControllerGyroInvertX,_onexControllerGyroInvertY));
   _onexControllerGyroRotation=GyroChoice(global::XboxGamingBar.OnexStrings.Get("Output rotation"),"OnexControllerGyroRotation",new[]{"0","90","180","270"},new[]{OnexStrings.Get("0° · normal grip"),"90° clockwise","180°","270° clockwise"});panel.Children.Add(_onexControllerGyroRotation);
   void Commit(){if(!_onexControllerSync&&GyroChoice(_onexControllerGyroRotation)!=null)QueueOnexControllerGyroProcessing("outputRotation",JsonValue.CreateNumberValue(int.Parse(GyroChoice(_onexControllerGyroRotation))));}
   _onexControllerGyroRotation.SelectionChanged+=(s,e)=>{if(!_onexControllerGyroRotation.IsDropDownOpen&&OnexInputGuard.IsActive(_onexControllerGyroRotation))Commit();};_onexControllerGyroRotation.DropDownClosed+=(s,e)=>{if(OnexInputGuard.IsActive(_onexControllerGyroRotation))Commit();};
  }
  private void ApplyOnexControllerGyroAxes(JsonObject p){
   if(_onexControllerGyroWorld==null)return;_onexControllerGyroWorld.IsChecked=p?.GetNamedBoolean("worldSpace",false)??false;_onexControllerGyroInvertX.IsChecked=p?.GetNamedBoolean("invertX",false)??false;_onexControllerGyroInvertY.IsChecked=p?.GetNamedBoolean("invertY",false)??false;
   if(!_onexControllerGyroConversion.IsDropDownOpen){string conversion=ControllerText(p,"stickConversion","Yaw");_onexControllerGyroConversion.SelectedItem=_onexControllerGyroConversion.Items.OfType<ComboBoxItem>().First(i=>(string)i.Tag==conversion);}
   if(!_onexControllerGyroRotation.IsDropDownOpen){string value=((int)(p?.GetNamedNumber("outputRotation",0)??0)).ToString();_onexControllerGyroRotation.SelectedItem=_onexControllerGyroRotation.Items.OfType<ComboBoxItem>().First(i=>(string)i.Tag==value);}
  }
  private void RefreshOnexControllerGyroAxes(bool enabled){if(_onexControllerGyroWorld==null)return;foreach(var control in new Control[]{_onexControllerGyroWorld,_onexControllerGyroInvertX,_onexControllerGyroInvertY,_onexControllerGyroRotation})control.IsEnabled=enabled;_onexControllerGyroConversion.IsEnabled=enabled&&GyroChoice(_onexControllerGyroTarget)!="Mouse"&&_onexControllerGyroWorld.IsChecked!=true;}
 }
}
