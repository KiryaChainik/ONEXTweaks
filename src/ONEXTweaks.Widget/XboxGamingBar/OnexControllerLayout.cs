// SPDX-License-Identifier: AGPL-3.0-only
using Windows.Data.Json;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private ToggleSwitch _onexControllerNintendo;
  private TextBlock _onexControllerLayoutAvailabilityHint;
  private bool OnexControllerNintendoActive=>OnexControllerEditingVirtual&&OnexControllerStandardRemapOn&&(_onexControllerPending??ControllerEditorPolicy)?.GetNamedObject("tuning",null)?.GetNamedBoolean("nintendoLayout",false)==true;
  private string NintendoTarget(string target){if(!OnexControllerNintendoActive)return target;switch(target){case "A":return "B";case "B":return "A";case "X":return "Y";case "Y":return "X";default:return target;}}
  private void BuildOnexControllerLayout(StackPanel host)
  {
   _onexControllerNintendo=new ToggleSwitch{Name="OnexControllerNintendoLayout",OnContent="",OffContent="",MinWidth=0};OnexInputGuard.Wire(_onexControllerNintendo);_onexControllerNintendo.Foreground=OnexSolidBrush(OnexPalette.Cyan);OnexAccentResources(_onexControllerNintendo,OnexPalette.Cyan);
   foreach(string themeName in new[]{"Default","Dark","Light"}){
    var theme=new ResourceDictionary();
    foreach(string key in new[]{"ToggleSwitchFillOn","ToggleSwitchFillOnPointerOver","ToggleSwitchFillOnPressed","ToggleSwitchStrokeOn","ToggleSwitchStrokeOnPointerOver","ToggleSwitchStrokeOnPressed","SystemControlHighlightAccentBrush"})theme[key]=new SolidColorBrush(OnexPalette.Cyan);
    theme["SystemAccentColor"]=OnexPalette.Cyan;_onexControllerNintendo.Resources.ThemeDictionaries[themeName]=theme;
   }
   _onexControllerNintendo.RequestedTheme=ElementTheme.Dark;_onexControllerControls.Add(_onexControllerNintendo);
   host.Children.Add(OnexControllerGroupCaption(global::XboxGamingBar.OnexStrings.Get("Nintendo Switch layout"),_onexControllerNintendo,global::XboxGamingBar.OnexStrings.Get("Меняет A ↔ B и X ↔ Y в конечном виртуальном выходе — после назначений кнопок и макросов. Сохраняется в текущий профиль. Нужны Virtual Controller и Full Remapping On. Навигация самого виджета остаётся обычной Xbox; клавомышь не меняется.")));
   _onexControllerLayoutAvailabilityHint=ControllerHelp("");host.Children.Add(_onexControllerLayoutAvailabilityHint);
   _onexControllerNintendo.Toggled+=(s,e)=>{if(!_onexControllerSync&&OnexInputGuard.ConsumeToggleInput(_onexControllerNintendo))QueueOnexControllerTuning("nintendoLayout",JsonValue.CreateBooleanValue(_onexControllerNintendo.IsOn));};
  }
  private void ApplyOnexControllerLayout(JsonObject tuning){if(_onexControllerNintendo!=null)_onexControllerNintendo.IsOn=tuning?.GetNamedBoolean("nintendoLayout",false)==true;}
  private void RefreshOnexControllerLayoutAvailability(){
   if(_onexControllerNintendo==null)return;
   _onexControllerNintendo.IsEnabled=App.IsConnected&&OnexControllerEditingVirtual&&OnexControllerStandardRemapOn;
   if(_onexControllerLayoutAvailabilityHint!=null){
    _onexControllerLayoutAvailabilityHint.Text=OnexStrings.Get("Nintendo Switch layout requires Full Remapping. Turn Full Remapping on to enable it.");
    _onexControllerLayoutAvailabilityHint.Visibility=App.IsConnected&&OnexControllerEditingVirtual&&!OnexControllerStandardRemapOn?Visibility.Visible:Visibility.Collapsed;
   }
  }
 }
}
