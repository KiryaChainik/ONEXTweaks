// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private ToggleSwitch _onexControllerStandardEnabled;
  private Button _onexControllerStandardReset,_onexControllerOemReset,_onexControllerBindingReset,_onexControllerSticksReset,_onexControllerVibrationReset;
  private Border _onexControllerStandardBlock;private FrameworkElement _onexControllerOemGrid;
  private TextBlock _onexControllerVibrationValue,_onexControllerVibrationHint;private Slider _onexControllerVibrationIntensity;private Button _onexControllerVibrationTest;private bool _onexControllerTestingVibration;
  private bool OnexControllerStandardRemapOn=>(_onexControllerPending??ControllerEditorPolicy)?.GetNamedBoolean("gamepadRemappingDisabled",false)!=true;
  private bool OnexControllerSourceGroupOn(string id)=>id!=null&&(id.StartsWith("OEM:",StringComparison.Ordinal)?_onexControllerState?.GetNamedBoolean("oemEnabled",false)==true:OnexControllerStandardRemapOn&&OnexControllerEditingVirtual);
  private FrameworkElement OnexControllerGroupCaption(string title,ToggleSwitch toggle,string help)
  {
   var panel=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,VerticalAlignment=VerticalAlignment.Center};panel.Children.Add(OnexHelpHeader(title,help));panel.Children.Add(toggle);return panel;
  }
  private Button CreateOnexControllerGroupReset(string group,string name)
  {
   bool mappingGroup=group=="standard"||group=="oem";
   string caption=OnexStrings.Get(mappingGroup?"Reset group…":"Restore Default");
   var button=new Button{Name=name,Content=caption,Style=(Style)Resources["StandaloneButtonStyle"],MinHeight=38,Padding=new Thickness(10,6,10,6)};
   Task Reset(string profile)=>ControllerActionAsync(new JsonObject
   {
    {"operation",JsonValue.CreateStringValue("resetGroup")},
    {"id",JsonValue.CreateStringValue(profile)},
    {"group",JsonValue.CreateStringValue(group)}
   });
   button.Click+=async(s,e)=>
   {
    string profile=ControllerEditorId;
    if(!mappingGroup){await Reset(profile);return;}
    if(IsOnexRemapOpen(_onexControllerMappingFlyout)||_onexControllerResetMenu?.IsOpen==true)return;
    var menu=CreateOnexMenu(FlyoutPlacementMode.Bottom);
    _onexControllerResetMenu=menu;
    menu.Closed+=(sender,args)=>{if(ReferenceEquals(_onexControllerResetMenu,menu))_onexControllerResetMenu=null;};
    var confirm=new MenuFlyoutItem{Text=OnexStrings.Get(group=="standard"?"Reset all gamepad buttons":"Reset all extra buttons")};
    confirm.Click+=async(sender,args)=>
    {
     if(profile==ControllerEditorId&&!IsOnexRemapOpen(_onexControllerMappingFlyout))await Reset(profile);
    };
    menu.Items.Add(confirm);
    menu.ShowAt(button);
   };
   _onexControllerControls.Add(button);
   return button;
  }
  private Border CreateOnexControllerRemapGroup(string name,FrameworkElement caption,Button reset,FrameworkElement grid)
  {
   var header=new Grid{ColumnSpacing=12};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.Children.Add(caption);Grid.SetColumn(reset,1);header.Children.Add(reset);var body=new StackPanel{Spacing=10};body.Children.Add(header);body.Children.Add(grid);
   return new Border{Name=name,Child=body,Padding=new Thickness(12),CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),BorderBrush=(Windows.UI.Xaml.Media.Brush)Resources["CardBorderBrush"],Background=OnexSettingsBrush(24,29,37)};
  }
  private void InitializeOnexControllerMappingGroups(StackPanel mapping,FrameworkElement oemCaption,FrameworkElement oemGrid,Grid standardGrid)
  {
   _onexControllerOemGrid=oemGrid;_onexControllerOemReset=CreateOnexControllerGroupReset("oem","OnexControllerExtraRestore");_onexControllerOemBlock=CreateOnexControllerRemapGroup("OnexControllerExtraButtons",oemCaption,_onexControllerOemReset,oemGrid);_onexControllerExtraPanel.Children.Add(_onexControllerOemBlock);
   _onexControllerStandardEnabled=new ToggleSwitch{Name="OnexControllerStandardEnabled",OnContent="",OffContent="",MinWidth=0};OnexInputGuard.Wire(_onexControllerStandardEnabled);ApplyOnexTileWindowControlAccent(_onexControllerStandardEnabled);_onexControllerControls.Add(_onexControllerStandardEnabled);
   _onexControllerStandardEnabled.Toggled+=(s,e)=>{if(!_onexControllerSync&&OnexInputGuard.ConsumeToggleInput(_onexControllerStandardEnabled))QueueOnexControllerPolicy(policy=>policy["gamepadRemappingDisabled"]=JsonValue.CreateBooleanValue(!_onexControllerStandardEnabled.IsOn));};
   var caption=OnexControllerGroupCaption(global::XboxGamingBar.OnexStrings.Get("Gamepad buttons"),_onexControllerStandardEnabled,global::XboxGamingBar.OnexStrings.Get("Off отключает назначения основных кнопок и направлений стиков: передаются их обычные игровые функции. Назначения сохраняются в выбранном профиле. Зоны нечувствительности и остальные отдельные настройки стиков остаются активны. Для применения ремапа требуется Virtual Controller."));_onexControllerStandardReset=CreateOnexControllerGroupReset("standard","OnexControllerGamepadRestore");_onexControllerStandardBlock=CreateOnexControllerRemapGroup("OnexControllerGamepadButtons",caption,_onexControllerStandardReset,standardGrid);mapping.Children.Add(_onexControllerStandardBlock);
  }
  private void RefreshOnexControllerGroupAvailability()
  {
   if(_onexControllerStandardEnabled==null)return;bool ready=App.IsConnected&&_onexControllerState?.ContainsKey("entries")==true;bool standard=OnexControllerStandardRemapOn,oem=_onexControllerState?.GetNamedBoolean("oemEnabled",false)==true;
   bool before=_onexControllerSync;_onexControllerSync=true;try{_onexControllerStandardEnabled.IsOn=standard;}finally{_onexControllerSync=before;}
   _onexControllerOemGrid.Visibility=oem?Visibility.Visible:Visibility.Collapsed;_onexControllerStandardInputs.Visibility=standard?Visibility.Visible:Visibility.Collapsed;
   foreach(var pair in _onexControllerInputTiles){bool enabled=ready&&OnexControllerSourceGroupOn(pair.Key);pair.Value.IsEnabled=enabled;var hotspot=_onexControllerDiagramButtons[pair.Key];hotspot.IsEnabled=enabled;hotspot.IsHitTestVisible=enabled;}
   if(!OnexControllerSourceGroupOn(ControllerSourceId))CloseOnexRemapEditor(_onexControllerMappingFlyout);WireOnexControllerNavigation();
  }
  private Panel CreateOnexControllerVibrationSection()
  {
   var panel=new StackPanel{Spacing=12};BuildOnexControllerRumbleProfiles(panel);_onexControllerVibrationValue=ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Intensity · 100%"));panel.Children.Add(_onexControllerVibrationValue);
   _onexControllerVibrationIntensity=new Slider{Name="OnexControllerVibrationIntensity",Minimum=0,Maximum=100,StepFrequency=1,SmallChange=5,Style=(Style)Resources["OnexPreciseSliderStyle"]};
   Action commit=()=>{if(!_onexControllerSync)QueueOnexControllerVibration("intensity",JsonValue.CreateNumberValue(Math.Round(_onexControllerVibrationIntensity.Value)));};OnexInputGuard.Wire(_onexControllerVibrationIntensity,commit);OnexInputGuard.ConfigurePercentage(_onexControllerVibrationIntensity);_onexControllerVibrationIntensity.ValueChanged+=(s,e)=>{_onexControllerVibrationValue.Text=global::XboxGamingBar.OnexStrings.Format("Intensity · {0}%",Math.Round(e.NewValue));if(!_onexControllerSync&&OnexInputGuard.IsActive(_onexControllerVibrationIntensity))commit();};_onexControllerControls.Add(_onexControllerVibrationIntensity);panel.Children.Add(_onexControllerVibrationIntensity);
   panel.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("100% — полный уровень выбранного пресета, 50% — вдвое слабее, 0% — выключена. Для регулировки силы в играх выбери Virtual Controller. Настройка сохраняется в выбранном профиле. Test Vibration работает и в Hardware Controller.")));
   _onexControllerVibrationTest=new Button{Name="OnexControllerVibrationTest",Content=global::XboxGamingBar.OnexStrings.Get("Test Vibration"),Style=(Style)Resources["StandaloneButtonStyle"],MinHeight=40};_onexControllerControls.Add(_onexControllerVibrationTest);_onexControllerVibrationHint=ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Короткий импульс 0,4 секунды с выбранным пресетом, перестановкой моторов и интенсивностью. Работает и в режиме Hardware Controller."));
   _onexControllerVibrationTest.Click+=async(s,e)=>
   {
    if(_onexControllerTestingVibration)return;_onexControllerTestingVibration=true;_onexControllerVibrationTest.Content=global::XboxGamingBar.OnexStrings.Get("Testing…");_onexControllerVibrationHint.Text=global::XboxGamingBar.OnexStrings.Get("Testing vibration…");double before=_onexControllerState?.GetNamedNumber("vibrationTestSerial",0)??0;string profile=ControllerEditorId;
    try{for(int n=0;n<160&&_onexControllerBusy;n++)await Task.Delay(25);await ControllerActionAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("vibrationTest")},{"id",JsonValue.CreateStringValue(profile)}});bool success=(_onexControllerState?.GetNamedNumber("vibrationTestSerial",0)??0)>before;_onexControllerVibrationHint.Text=success?global::XboxGamingBar.OnexStrings.Get("Тест завершён: контроллер принял сигнал."):_onexControllerStatus.Text;}
    finally{_onexControllerTestingVibration=false;_onexControllerVibrationTest.Content=global::XboxGamingBar.OnexStrings.Get("Test Vibration");}
   };
   _onexControllerVibrationReset=CreateOnexControllerGroupReset("vibration","OnexControllerVibrationRestore");panel.Children.Add(OnexSettingsColumns(_onexControllerVibrationTest,_onexControllerVibrationReset));panel.Children.Add(_onexControllerVibrationHint);return panel;
  }

 }
}
