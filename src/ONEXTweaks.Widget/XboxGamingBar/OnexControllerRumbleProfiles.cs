// SPDX-License-Identifier: AGPL-3.0-only
using System.Linq;
using Windows.Data.Json;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private ComboBox _onexControllerRumbleProfile;private CheckBox _onexControllerSwapMotors;
  private void QueueOnexControllerVibration(string key,IJsonValue value)=>QueueOnexControllerPolicy(policy=>{var vibration=policy.GetNamedObject("vibration",null)??new JsonObject();vibration[key]=value;policy["vibration"]=vibration;});
  private void BuildOnexControllerRumbleProfiles(StackPanel panel)
  {
   _onexControllerRumbleProfile=ControllerChoice(global::XboxGamingBar.OnexStrings.Get("Rumble profile"),"OnexControllerRumbleProfile",new[]{"Balanced","Sharp","Soft","Impact","Boosted"});_onexControllerRumbleProfile.RequestedTheme=ElementTheme.Dark;_onexControllerRumbleProfile.Background=OnexSettingsBrush(39,47,60);_onexControllerRumbleProfile.Foreground=OnexSettingsBrush(238,241,246);_onexControllerRumbleProfile.MaxDropDownHeight=280;
   foreach(string name in new[]{"Default","Dark","Light"}){var theme=new ResourceDictionary();foreach(string key in new[]{"ComboBoxBackground","ComboBoxBackgroundPointerOver","ComboBoxBackgroundPressed","ComboBoxDropDownBackground"})theme[key]=OnexSettingsBrush(39,47,60);foreach(string key in new[]{"ComboBoxForeground","ComboBoxForegroundPointerOver","ComboBoxForegroundPressed","ComboBoxDropDownForeground"})theme[key]=OnexSettingsBrush(238,241,246);_onexControllerRumbleProfile.Resources.ThemeDictionaries[name]=theme;}
   foreach(var item in _onexControllerRumbleProfile.Items.OfType<ComboBoxItem>()){item.RequestedTheme=ElementTheme.Dark;item.Foreground=OnexSettingsBrush(238,241,246);item.Background=OnexSettingsBrush(39,47,60);}
   panel.Children.Add(_onexControllerRumbleProfile);
   void Commit(){if(!_onexControllerSync&&_onexControllerRumbleProfile.SelectedItem is ComboBoxItem item)QueueOnexControllerVibration("profile",JsonValue.CreateStringValue((string)item.Tag));}
   _onexControllerRumbleProfile.SelectionChanged+=(s,e)=>{if(!_onexControllerRumbleProfile.IsDropDownOpen&&OnexInputGuard.IsActive(_onexControllerRumbleProfile))Commit();};_onexControllerRumbleProfile.DropDownClosed+=(s,e)=>{if(OnexInputGuard.IsActive(_onexControllerRumbleProfile))Commit();};
   panel.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Balanced — обычный отклик. Sharp усиливает мелкую вибрацию и ослабляет тяжёлую; Soft делает обе мягче; Impact усиливает тяжёлую; Boosted усиливает обе. Это наши ограниченные уровнем мощности варианты обработки, а не аппаратные пресеты прошивки.")));
   _onexControllerSwapMotors=new CheckBox{Name="OnexControllerSwapMotors",Content=global::XboxGamingBar.OnexStrings.Get("Swap rumble motors")};OnexInputGuard.Wire(_onexControllerSwapMotors);OnexAccentResources(_onexControllerSwapMotors,OnexPalette.Violet);foreach(string name in new[]{"Default","Dark","Light"}){var theme=new ResourceDictionary();foreach(string key in new[]{"CheckBoxCheckBackgroundFillChecked","CheckBoxCheckBackgroundFillCheckedPointerOver","CheckBoxCheckBackgroundFillCheckedPressed","CheckBoxCheckBackgroundStrokeChecked","CheckBoxCheckBackgroundStrokeCheckedPointerOver","CheckBoxCheckBackgroundStrokeCheckedPressed"})theme[key]=new SolidColorBrush(OnexPalette.Violet);_onexControllerSwapMotors.Resources.ThemeDictionaries[name]=theme;}_onexControllerControls.Add(_onexControllerSwapMotors);_onexControllerSwapMotors.Click+=(s,e)=>{if(!_onexControllerSync)QueueOnexControllerVibration("swapMotors",JsonValue.CreateBooleanValue(_onexControllerSwapMotors.IsChecked==true));};panel.Children.Add(_onexControllerSwapMotors);
   panel.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Меняет каналы тяжёлой и мелкой вибрации местами после выбранного пресета. Интенсивность остаётся общим пределом; 0% всегда выключает оба мотора.")));
  }
  private void ApplyOnexControllerRumbleProfiles(JsonObject policy)
  {
   if(_onexControllerRumbleProfile==null)return;var vibration=policy.GetNamedObject("vibration",null);
   if(!_onexControllerRumbleProfile.IsDropDownOpen)_onexControllerRumbleProfile.SelectedItem=_onexControllerRumbleProfile.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>(string)item.Tag==ControllerText(vibration,"profile","Balanced"))??_onexControllerRumbleProfile.Items[0];
   _onexControllerSwapMotors.IsChecked=vibration?.GetNamedBoolean("swapMotors",false)==true;
  }
  private string DescribeOnexControllerVibration(JsonObject policy){var vibration=policy?.GetNamedObject("vibration",null);return ControllerText(vibration,"profile","Balanced")+" · "+(vibration?.GetNamedNumber("intensity",100)??100)+"%"+(vibration?.GetNamedBoolean("swapMotors",false)==true?global::XboxGamingBar.OnexStrings.Get(" · Swapped"):"");}
 }
}
