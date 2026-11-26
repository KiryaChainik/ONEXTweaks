// SPDX-License-Identifier: AGPL-3.0-only
using System;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private Border _kbmPrecisionCard;
  private void BuildKbmPrecision()
  {
   var panel=new StackPanel{Spacing=12};
   panel.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Общие настройки клавомыши. Они не меняют игровые профили и не включают этот режим сами.")));
   Slider Setting(string name,string title,string explanation,string key,int minimum,int maximum,int initial,bool percent)
   {
    var block=new StackPanel{Spacing=6};block.Children.Add(ControllerHelp(title));block.Children.Add(ControllerHelp(explanation));
    var caption=ControllerHelp("");var slider=new Slider{Name=name,Minimum=minimum,Maximum=maximum,Value=initial,StepFrequency=1,SmallChange=percent?5:1,LargeChange=percent?5:1,Style=(Style)Resources["LightMediaSliderStyle"]};
    if(percent)OnexInputGuard.ConfigurePercentage(slider);
    void Caption(){caption.Text=key=="holdAcceleration"&&slider.Value==0?global::XboxGamingBar.OnexStrings.Get("Off"):Math.Round(slider.Value)+(percent?"%":global::XboxGamingBar.OnexStrings.Get(" px"));}
    void Commit(){if(!_kbmSync&&!_kbmReadOnlyProjection)QueueKbmTuning(key,JsonValue.CreateNumberValue(Math.Round(slider.Value)));}
    OnexInputGuard.SetCommit(slider,Commit);slider.ValueChanged+=(s,e)=>{Caption();if(!_kbmSync&&OnexInputGuard.IsActive(slider))Commit();};Caption();
    block.Children.Add(slider);block.Children.Add(caption);panel.Children.Add(block);return slider;
   }
   _kbmDeadzone=Setting("OnexKbmDeadzone",OnexStrings.Get("Stick deadzone"),global::XboxGamingBar.OnexStrings.Get("Игнорирует небольшое отклонение обоих стиков около центра — для курсора, прокрутки и назначений на стики. Например, 10% помогает убрать самопроизвольное движение."),"stickDeadzone",0,20,10,true);
   _kbmHoldAcceleration=Setting("OnexKbmHoldAcceleration",OnexStrings.Get("Hold acceleration"),global::XboxGamingBar.OnexStrings.Get("Плавно ускоряет курсор, пока держишь одно направление. 0% — выключено; 100% — за секунду скорость вырастет до 2×. Отпусти стик — ускорение сбросится."),"holdAcceleration",0,100,0,true);
   _kbmNudgeStep=Setting("OnexKbmNudgeStep",OnexStrings.Get("Cursor nudge step"),global::XboxGamingBar.OnexStrings.Get("Размер точного шага для действия Nudge cursor в меню назначения кнопок. Например, 10 px — одно нажатие сдвинет курсор на 10 пикселей."),"nudgeStep",1,50,10,false);
   _kbmPrecisionReset=KbmButton("OnexKbmPrecisionRestore",global::XboxGamingBar.OnexStrings.Get("Restore precision defaults"));_kbmPrecisionReset.Click+=async(s,e)=>await KbmChange(new JsonObject{{"operation",JsonValue.CreateStringValue("resetPrecision")}});
   panel.Children.Add(_kbmPrecisionReset);panel.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Сбросит только эти три параметра: 10% / Off / 10 px. Скорости, FN и назначения кнопок сохранятся.")));
   _kbmPrecisionCard=KbmCard(panel);
  }
  private void ApplyKbmPrecision(JsonObject policy)
  {
   void Apply(Slider slider,string key,int fallback){if(!OnexInputGuard.IsEditing(slider)&&!_kbmTuning.ContainsKey(key))slider.Value=policy.GetNamedNumber(key,fallback);}
   Apply(_kbmDeadzone,"stickDeadzone",10);Apply(_kbmHoldAcceleration,"holdAcceleration",0);Apply(_kbmNudgeStep,"nudgeStep",10);
  }
 }
}
