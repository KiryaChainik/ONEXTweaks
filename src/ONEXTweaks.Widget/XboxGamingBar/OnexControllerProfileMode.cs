// SPDX-License-Identifier: AGPL-3.0-only
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private Border CreateOnexControllerHardwareProfileCard()
  {
   var body=new StackPanel{Spacing=10};_onexControllerUseHardware=new ToggleSwitch{Name="OnexControllerUseHardware",OnContent="",OffContent="",MinWidth=0};OnexInputGuard.Wire(_onexControllerUseHardware);ApplyOnexTileWindowControlAccent(_onexControllerUseHardware);
   body.Children.Add(OnexSettingsInlineToggle(OnexHelpHeader(global::XboxGamingBar.OnexStrings.Get("Use Hardware Controller for this Profile"),global::XboxGamingBar.OnexStrings.Get("Игра получит штатный геймпад напрямую. Переназначения, режим мыши и настройки стиков виртуального контроллера для этого профиля применяться не будут. Например, включи это для игры, которая не принимает виртуальный Xbox-контроллер. Кнопка ONEXTweaks продолжит работать. При выборе другого профиля вернётся общий режим Controller Mode.")),_onexControllerUseHardware));
   _onexControllerHardwareProfileHint=ControllerHelp("");body.Children.Add(_onexControllerHardwareProfileHint);
   _onexControllerUseHardware.Toggled+=async(s,e)=>{if(!_onexControllerSync&&OnexInputGuard.ConsumeToggleInput(_onexControllerUseHardware)){QueueOnexControllerPolicy(policy=>policy["useHardwareController"]=JsonValue.CreateBooleanValue(_onexControllerUseHardware.IsOn));await FlushOnexControllerPolicyAsync();}};
   return _onexControllerHardwareProfileCard=OnexSettingsCard(body);
  }
  private void RefreshOnexControllerHardwareProfile(JsonObject policy)
  {
   if(_onexControllerUseHardware==null)return;bool show=ControllerEditorId!="global";_onexControllerHardwareProfileCard.Visibility=show?Visibility.Visible:Visibility.Collapsed;
   _onexControllerUseHardware.IsOn=policy.GetNamedBoolean("useHardwareController",false);
   bool active=ControllerEditorId==ControllerText(_onexControllerState,"appliedId");
   _onexControllerHardwareProfileHint.Text=!active?global::XboxGamingBar.OnexStrings.Get("Сохранится для этого профиля и применится, когда он станет активным."):!_onexControllerState.GetNamedBoolean("enabled",false)?global::XboxGamingBar.OnexStrings.Get("Общий Controller Mode сейчас Hardware. Исключение сохранится для следующего включения Virtual Controller."):_onexControllerUseHardware.IsOn?global::XboxGamingBar.OnexStrings.Get("Для этого профиля используется штатный геймпад. Общий Virtual Controller включится при выходе из профиля."):global::XboxGamingBar.OnexStrings.Get("Используется общий Controller Mode. Привязанный игровой профиль выбирается автоматически при запуске игры.");
  }
 }
}
