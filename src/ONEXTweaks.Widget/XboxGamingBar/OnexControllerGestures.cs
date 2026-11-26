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
  private ComboBox _onexControllerGesture;private ToggleSwitch _onexControllerDoubleEnabled;private Slider _onexControllerDoubleWindow;private TextBlock _onexControllerDoubleValue,_onexControllerDetection;private FrameworkElement _onexControllerDoubleCaption;private string _onexControllerGestureSource;
  private bool ControllerEditingDouble=>ControllerIsOem&&_onexControllerGesture?.SelectedIndex==1;
  private JsonObject ControllerDoubleConfig(JsonObject policy)=>policy?.GetNamedObject("oem",null)?.GetNamedObject("doublePresses",null)?.GetNamedObject(ControllerOemId??"",null);
  private void EditControllerDouble(JsonObject policy,string source,Action<JsonObject> change)
  {
   var oem=policy.GetNamedObject("oem",null)??new JsonObject();var all=oem.GetNamedObject("doublePresses",null)??new JsonObject();var value=all.GetNamedObject(source,null)??new JsonObject{{"enabled",JsonValue.CreateBooleanValue(false)},{"windowMs",JsonValue.CreateNumberValue(300)},{"binding",new JsonObject{{"kind",JsonValue.CreateStringValue("disabled")}}}};change(value);all[source]=value;oem["doublePresses"]=all;policy["oem"]=oem;
  }
  private void CreateOnexControllerGestureEditor(StackPanel editor)
  {
   _onexControllerDoubleEnabled=new ToggleSwitch{Name="OnexControllerDoubleEnabled",OnContent="",OffContent="",MinWidth=0};OnexInputGuard.Wire(_onexControllerDoubleEnabled);ApplyOnexTileWindowControlAccent(_onexControllerDoubleEnabled);_onexControllerControls.Add(_onexControllerDoubleEnabled);
   _onexControllerDoubleCaption=OnexControllerGroupCaption(global::XboxGamingBar.OnexStrings.Get("Double press"),_onexControllerDoubleEnabled,global::XboxGamingBar.OnexStrings.Get("Выключено — обычное действие срабатывает сразу. Включено — два быстрых нажатия запускают отдельное действие. Одиночное ждёт выбранный интервал после отпускания; длительное удержание начинает обычное действие после этого интервала. Например, Single=A, Double=B. Смена профиля, сон и выключение режима отменяют ожидающее нажатие."));editor.Children.Insert(1,_onexControllerDoubleCaption);
   _onexControllerDoubleValue=ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Double-press interval · 300 ms"));editor.Children.Insert(2,_onexControllerDoubleValue);
   _onexControllerDoubleWindow=new Slider{Name="OnexControllerDoubleWindow",Minimum=100,Maximum=600,StepFrequency=10,SmallChange=50,Style=(Style)Resources["OnexPreciseSliderStyle"]};Action commit=()=>{if(!_onexControllerSync&&ControllerIsOem){string source=ControllerOemId;double value=_onexControllerDoubleWindow.Value;QueueOnexControllerPolicy(policy=>EditControllerDouble(policy,source,config=>config["windowMs"]=JsonValue.CreateNumberValue(value)));}};OnexInputGuard.Wire(_onexControllerDoubleWindow,commit);_onexControllerDoubleWindow.ValueChanged+=(s,e)=>{_onexControllerDoubleValue.Text=global::XboxGamingBar.OnexStrings.Format("Double-press interval · {0} ms",Math.Round(e.NewValue));if(!_onexControllerSync&&OnexInputGuard.IsActive(_onexControllerDoubleWindow))commit();};_onexControllerControls.Add(_onexControllerDoubleWindow);editor.Children.Insert(3,_onexControllerDoubleWindow);
   _onexControllerGesture=ControllerChoice(global::XboxGamingBar.OnexStrings.Get("Edit action"),"OnexControllerGesture",new[]{OnexStrings.Get("Single press action"),OnexStrings.Get("Double press action")});_onexControllerGesture.RequestedTheme=ElementTheme.Dark;_onexControllerGesture.Background=OnexSettingsBrush(39,47,60);_onexControllerGesture.Foreground=OnexSettingsBrush(238,241,246);_onexControllerGesture.ItemTemplate=null;editor.Children.Insert(4,_onexControllerGesture);_onexControllerGesture.SelectedIndex=0;_onexControllerGesture.SelectionChanged+=(s,e)=>{if(!_onexControllerSync&&!_onexControllerGesture.IsDropDownOpen)RefreshOnexControllerMapping();};_onexControllerGesture.DropDownClosed+=(s,e)=>{if(!_onexControllerSync)RefreshOnexControllerMapping();};
   _onexControllerDoubleEnabled.Toggled+=(s,e)=>{if(_onexControllerSync||!ControllerIsOem||!OnexInputGuard.ConsumeToggleInput(_onexControllerDoubleEnabled))return;string source=ControllerOemId;bool enabled=_onexControllerDoubleEnabled.IsOn;OnexInputGuard.Cancel(_onexControllerDoubleWindow);QueueOnexControllerPolicy(policy=>EditControllerDouble(policy,source,value=>value["enabled"]=JsonValue.CreateBooleanValue(enabled)));_onexControllerGesture.SelectedIndex=enabled?1:0;RefreshOnexControllerMapping();};
   _onexControllerDetection=ControllerHelp("");editor.Children.Add(_onexControllerDetection);
   foreach(var button in new[]{_onexControllerRecord,_onexControllerBindingReset}){button.RequestedTheme=ElementTheme.Dark;button.Background=OnexSettingsBrush(39,47,60);button.Foreground=OnexSettingsBrush(238,241,246);button.BorderBrush=OnexSettingsBrush(80,91,106);button.Style=(Style)Resources["KeyPickerButtonStyle"];button.HorizontalAlignment=HorizontalAlignment.Stretch;button.HorizontalContentAlignment=HorizontalAlignment.Center;}
  }

  private void WireOnexControllerGestureNavigation()
  {
   if(_onexControllerGesture==null)return;var controls=OnexControllerEditorFocusControls();for(int n=0;n<controls.Length;n++){controls[n].XYFocusUp=controls[Math.Max(0,n-1)];controls[n].XYFocusDown=controls[Math.Min(controls.Length-1,n+1)];}
  }
  private void RefreshOnexControllerGestureEditor(JsonObject policy)
  {
   if(_onexControllerGesture==null)return;bool oem=ControllerIsOem;if(_onexControllerGestureSource!=ControllerSourceId){OnexInputGuard.Cancel(_onexControllerDoubleWindow);_onexControllerGestureSource=ControllerSourceId;_onexControllerGesture.SelectedIndex=0;}
   var config=oem?ControllerDoubleConfig(policy):null;bool reserved=oem&&ControllerOemId=="KeyboardMouse"&&_onexControllerState.GetNamedBoolean("keyboardDoubleReserved",false);bool enabled=reserved||config?.GetNamedBoolean("enabled",false)==true;_onexControllerDoubleEnabled.IsEnabled=!reserved;_onexControllerDoubleWindow.IsEnabled=!reserved;_onexControllerDoubleCaption.Visibility=_onexControllerDoubleEnabled.Visibility=oem?Visibility.Visible:Visibility.Collapsed;_onexControllerDoubleEnabled.IsOn=enabled;_onexControllerDoubleValue.Visibility=_onexControllerDoubleWindow.Visibility=_onexControllerGesture.Visibility=oem&&enabled?Visibility.Visible:Visibility.Collapsed;if(!enabled)_onexControllerGesture.SelectedIndex=0;if(!OnexInputGuard.IsEditing(_onexControllerDoubleWindow))_onexControllerDoubleWindow.Value=reserved?300:config?.GetNamedNumber("windowMs",300)??300;
   ((ComboBoxItem)_onexControllerKind.Items[0]).IsEnabled=!ControllerEditingDouble;
   var detection=_onexControllerState?.GetNamedObject("oemInputState",null)?.GetNamedObject("gestures",null);_onexControllerDetection.Visibility=oem?Visibility.Visible:Visibility.Collapsed;_onexControllerDetection.Text=ControllerText(detection,"lastButton")==ControllerOemId?global::XboxGamingBar.OnexStrings.Format("Last detected: {0} press",ControllerText(detection,"lastGesture")):global::XboxGamingBar.OnexStrings.Get("Нажми выбранную допкнопку, чтобы увидеть Single или Double. Выполняется назначенное ей действие.");
  }
 }
}
