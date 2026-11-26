// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private OnexScopedProfilePicker _onexControllerPicker;private Border _onexControllerPickerCard;private OnexSavedCatalogSection _onexControllerSavedProfiles;
  private JsonObject _onexControllerState,_onexControllerPending;private string _onexControllerPendingId;
  private bool _onexControllerUiPending;private long _onexControllerUiApplyCount,_onexControllerHiddenStateUpdates;
  private bool _onexControllerReadOnlyProjection;private bool _onexControllerSync,_onexControllerBusy,_onexControllerRecording;private int _onexControllerOwnedKey=-1;private int[] _onexControllerRecordedKeys=new[]{32};private readonly HashSet<int> _onexControllerRecordModifiers=new HashSet<int>();
  private ComboBox _onexControllerSource,_onexControllerKind,_onexControllerPadTarget,_onexControllerKeyboard,_onexControllerMouse;
  private ComboBox _onexControllerTransport,_onexControllerTarget;private ToggleSwitch _onexControllerOemEnabled,_onexControllerUseHardware,_onexControllerForwardMotion,_onexControllerForwardLighting;private Border _onexControllerHardwareProfileCard;private TextBlock _onexControllerHardwareProfileHint;private Slider _onexControllerLeftDeadzone,_onexControllerRightDeadzone,_onexControllerThreshold;
  private CheckBox _onexControllerInvertLx,_onexControllerInvertLy,_onexControllerInvertRx,_onexControllerInvertRy,_onexControllerSwap;
  private Button _onexControllerRecord,_onexControllerRecover,_onexControllerAutomatic,_onexControllerReset;
  private MenuFlyout _onexControllerResetMenu;
  private StackPanel _onexControllerMappingPanel;private TextBlock _onexControllerLightingStatus,_onexControllerStatus,_onexControllerLive,_onexControllerMappingHint;private DispatcherTimer _onexControllerSaveTimer;
  private readonly List<Control> _onexControllerControls=new List<Control>();
  private static string ControllerText(JsonObject value,string key,string fallback="")=>value!=null&&value.TryGetValue(key,out var field)&&field.ValueType==JsonValueType.String?field.GetString():fallback;
  private JsonObject ControllerEditorPolicy=>_onexControllerState?.GetNamedObject("editor",null)?.GetNamedObject("policy",null);
  private string ControllerEditorId=>ControllerText(_onexControllerState,"editorId","global");
  private string ControllerSourceId=>(_onexControllerSource?.SelectedItem as ComboBoxItem)?.Tag as string;
  private bool ControllerIsOem=>ControllerSourceId?.StartsWith("OEM:",StringComparison.Ordinal)==true;
  private string ControllerOemId=>ControllerIsOem?ControllerSourceId.Substring(4):null;
  private string[] ControllerInputs=>new[]{"A","B","X","Y","LB","RB","LT","RT","Start","Select","LSClick","RSClick","DPadUp","DPadDown","DPadLeft","DPadRight","LSUp","LSDown","LSLeft","LSRight","RSUp","RSDown","RSLeft","RSRight"};
  private ComboBox ControllerChoice(string title,string name,string[] values)
  {var choice=OnexSettingsChoice(title);choice.Name=name;foreach(string value in values)choice.Items.Add(new ComboBoxItem{Content=OnexStrings.Get(value),Tag=value});OnexInputGuard.Wire(choice);ApplyOnexTileWindowControlAccent(choice);_onexControllerControls.Add(choice);return choice;}
  private TextBlock ControllerHelp(string text)=>new TextBlock{Text=OnexStrings.Get(text),TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=tileTextBrush};
  private void EnsureOnexControllerFrontend()
  {
   if(_onexControllerPicker!=null||!(LegionScrollViewer.Content is Panel body))return;
   foreach(var item in body.Children.OfType<FrameworkElement>())HideOnexUnusedLegacy(item);
   _onexControllerPicker=new OnexScopedProfilePicker(ReadOnexControllerAsync,ChangeOnexControllerAsync,()=>CreateOnexMenu(FlyoutPlacementMode.Bottom),DescribeOnexControllerPolicy,(Style)Resources["StandaloneButtonStyle"],tileTextBrush,ApplyOnexTileWindowControlAccent){IncludeRunningProcesses=true};
   _onexControllerPickerCard=OnexSharedProfileCard(_onexControllerPicker,"Controller",global::XboxGamingBar.OnexStrings.Get("Изменения сохраняются автоматически. Для игры создай профиль на основе её Performance-профиля."));body.Children.Add(_onexControllerPickerCard);
   body.Children.Add(CreateOnexControllerModeStrip());var main=new StackPanel{Spacing=10};
   _onexControllerOemEnabled=new ToggleSwitch{Name="OnexControllerOemEnabled",OnContent="",OffContent="",MinWidth=0};OnexInputGuard.Wire(_onexControllerOemEnabled);ApplyOnexTileWindowControlAccent(_onexControllerOemEnabled);
   var oemRow=OnexControllerGroupCaption(global::XboxGamingBar.OnexStrings.Get("Extra buttons"),_onexControllerOemEnabled,global::XboxGamingBar.OnexStrings.Get("Подготавливает четыре передние и две задние кнопки для переназначения. Off возвращает исходные аппаратные назначения. Сохранённые назначения профиля остаются; вызов ONEXTweaks доступен через штатную кнопку Game Bar."));
   _onexControllerOemEnabled.Toggled+=async(s,e)=>{if(!_onexControllerSync&&OnexInputGuard.ConsumeToggleInput(_onexControllerOemEnabled))await ControllerActionAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("oemEnable")},{"enabled",JsonValue.CreateBooleanValue(_onexControllerOemEnabled.IsOn)}});};
   _onexControllerStatus=ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Подключение к помощнику…"));main.Children.Add(_onexControllerStatus);_onexControllerLive=ControllerHelp("");main.Children.Add(_onexControllerLive);
   _onexControllerAutomatic=new Button{Name="OnexControllerAutomatic",Content=global::XboxGamingBar.OnexStrings.Get("Auto profiles"),Style=(Style)Resources["StandaloneButtonStyle"]};_onexControllerRecover=new Button{Name="OnexControllerRecover",Content=global::XboxGamingBar.OnexStrings.Get("Restore physical input"),Style=(Style)Resources["StandaloneButtonStyle"]};
   _onexControllerAutomatic.Click+=async(s,e)=>await ControllerActionAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("automatic")}});_onexControllerRecover.Click+=async(s,e)=>await ControllerActionAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("recover")}});main.Children.Add(OnexSettingsColumns(_onexControllerAutomatic,_onexControllerRecover));
   _onexControllerTarget=ControllerChoice(global::XboxGamingBar.OnexStrings.Get("Virtual controller type"),"OnexControllerTarget",new[]{"Xbox360","DualShock4"});((ComboBoxItem)_onexControllerTarget.Items[0]).Content=global::XboxGamingBar.OnexStrings.Get("Xbox 360");((ComboBoxItem)_onexControllerTarget.Items[1]).Content=global::XboxGamingBar.OnexStrings.Get("DualShock 4");
   _onexControllerTarget.SelectionChanged+=async(s,e)=>{if(!_onexControllerSync&&OnexInputGuard.IsActive(_onexControllerTarget)&&_onexControllerTarget.SelectedItem is ComboBoxItem chosen){QueueOnexControllerPolicy(policy=>policy["virtualTarget"]=JsonValue.CreateStringValue((string)chosen.Tag));await FlushOnexControllerPolicyAsync();}};
   main.Children.Add(_onexControllerTarget);main.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Saved in this controller profile. Changing type briefly reconnects the virtual controller. Xbox 360 offers the widest game compatibility. Keyboard & mouse temporarily uses Xbox 360; the saved choice is retained.")));
   _onexControllerForwardMotion=new ToggleSwitch{Name="OnexControllerForwardMotion",Header=global::XboxGamingBar.OnexStrings.Get("Forward motion to DS4 games"),MinWidth=0,HorizontalAlignment=HorizontalAlignment.Stretch};
   OnexInputGuard.Wire(_onexControllerForwardMotion);OnexAccentResources(_onexControllerForwardMotion,OnexPalette.Violet);_onexControllerControls.Add(_onexControllerForwardMotion);
    _onexControllerForwardMotion.Toggled+=(s,e)=>{if(!_onexControllerSync&&OnexInputGuard.ConsumeToggleInput(_onexControllerForwardMotion)&&_onexControllerForwardMotion.IsEnabled)QueueOnexControllerNativeMotion(_onexControllerForwardMotion.IsOn);};
   main.Children.Add(_onexControllerForwardMotion);main.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Requires Virtual Controller + DualShock 4. Sends the sensor's rotation and acceleration axes to the game. Configure motion controls in the game or Steam Input. Separate from gyro-to-stick/mouse; forwarding pauses while ONEXTweaks is open.")));
   _onexControllerForwardLighting=new ToggleSwitch{Name="OnexControllerForwardLighting",Header=global::XboxGamingBar.OnexStrings.Get("Forward game lighting to LEDs"),MinWidth=0,HorizontalAlignment=HorizontalAlignment.Stretch};OnexInputGuard.Wire(_onexControllerForwardLighting);OnexAccentResources(_onexControllerForwardLighting,OnexPalette.Violet);_onexControllerControls.Add(_onexControllerForwardLighting);
   _onexControllerForwardLighting.Toggled+=(s,e)=>{if(!_onexControllerSync&&OnexInputGuard.ConsumeToggleInput(_onexControllerForwardLighting)&&_onexControllerForwardLighting.IsEnabled)QueueOnexControllerPolicy(policy=>policy["forwardGameLighting"]=JsonValue.CreateBooleanValue(_onexControllerForwardLighting.IsOn));};
   main.Children.Add(_onexControllerForwardLighting);_onexControllerLightingStatus=ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Off"));main.Children.Add(_onexControllerLightingStatus);main.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Requires DualShock 4 and the updated hardware helper. Temporarily uses game-requested colors while keeping your zone brightness and calibration. Saved LED colors stay unchanged. Editing LED settings stops the current game override; re-enable this option or select another profile to accept new game colors.")));
   main.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Global и ручные профили используют тот же Choose / Create. Профиль, связанный с Performance, выбирается при его активации; ручной выбор действует до смены или перезапуска игрового контекста. После сна или отключения контроллера может потребоваться снова включить режим.")));
   _onexControllerExtraPanel=new StackPanel{Spacing=12};var mapping=_onexControllerMappingPanel=new StackPanel{Spacing=0};var editor=new StackPanel{Spacing=12};
   _onexControllerSource=ControllerChoice(global::XboxGamingBar.OnexStrings.Get("Physical control"),"OnexControllerSource",new[]{"OEM:Home","OEM:GKey","OEM:KeyboardMouse","OEM:Onex","OEM:M1","OEM:M2"}.Concat(ControllerInputs).Concat(Enumerable.Range(13,12).Select(n=>"F"+n)).ToArray());foreach(var item in _onexControllerSource.Items.OfType<ComboBoxItem>())if(((string)item.Tag).StartsWith("F",StringComparison.Ordinal))item.Content=global::XboxGamingBar.OnexStrings.Format("Extra {0} (firmware)",item.Tag);
   foreach(var item in _onexControllerSource.Items.OfType<ComboBoxItem>()){string id=(string)item.Tag;switch(id){case "OEM:Home":item.Content=global::XboxGamingBar.OnexStrings.Get("Front · Home");break;case "OEM:GKey":item.Content=global::XboxGamingBar.OnexStrings.Get("Front · G-Key / Xbox");break;case "OEM:KeyboardMouse":item.Content=global::XboxGamingBar.OnexStrings.Get("Front · Keyboard / mouse");break;case "OEM:Onex":item.Content=global::XboxGamingBar.OnexStrings.Get("Front · ONEX");break;case "OEM:M1":item.Content=global::XboxGamingBar.OnexStrings.Get("Rear · M1");break;case "OEM:M2":item.Content=global::XboxGamingBar.OnexStrings.Get("Rear · M2");break;}}
   _onexControllerSource.SelectionChanged+=(s,e)=>RefreshOnexControllerMapping();
   BuildOnexControllerMappingOverview(mapping,oemRow,editor);
    var sticks=new StackPanel{Spacing=12};
   Slider Percent(Panel host,string name,string caption,string key,int maximum,int minimum=0)
   {
    var value=new TextBlock{Tag="OnexControllerTuningValue",Text=minimum+"%",HorizontalAlignment=HorizontalAlignment.Right,Foreground=OnexSolidBrush(OnexPalette.Violet)};host.Children.Add(OnexSettingsColumns(ControllerHelp(OnexStrings.Get(caption)),value));
    var slider=new Slider{Name=name,Minimum=minimum,Maximum=maximum,StepFrequency=1,SmallChange=5,Style=(Style)Resources["OnexPreciseSliderStyle"]};Action commit=()=>{if(!_onexControllerSync)QueueOnexControllerTuning(key,JsonValue.CreateNumberValue(slider.Value));};OnexInputGuard.Wire(slider,commit);OnexInputGuard.ConfigurePercentage(slider);slider.ValueChanged+=(s,e)=>{value.Text=Math.Round(slider.Value)+"%";if(!_onexControllerSync&&OnexInputGuard.IsActive(slider))commit();};_onexControllerControls.Add(slider);host.Children.Add(slider);return slider;
   }
   var leftStick=new StackPanel{Spacing=8};var rightStick=new StackPanel{Spacing=8};
   _onexControllerLeftDeadzone=Percent(leftStick,"OnexControllerLeftDeadzone",OnexStrings.Get("Left stick · deadzone"),"leftDeadzone",50);_onexControllerRightDeadzone=Percent(rightStick,"OnexControllerRightDeadzone",OnexStrings.Get("Right stick · deadzone"),"rightDeadzone",50);sticks.Children.Add(OnexSettingsColumns(leftStick,rightStick));
   BuildOnexControllerTriggers(sticks,Percent,FlagTrigger);
   CheckBox FlagTrigger(string caption,string key){var box=new CheckBox{Name="OnexControllerHairTriggers",Content=OnexStrings.Get(caption)};OnexInputGuard.Wire(box);OnexAccentResources(box,OnexPalette.Cyan);box.Click+=(a,e)=>{if(!_onexControllerSync)QueueOnexControllerTuning(key,JsonValue.CreateBooleanValue(box.IsChecked==true));};_onexControllerControls.Add(box);return box;}
   _onexControllerThreshold=Percent(sticks,"OnexControllerTriggerThreshold",OnexStrings.Get("Button / gyro activation threshold (%)"),"triggerThreshold",95,5);
   sticks.Children.Add(ControllerHelp(OnexStrings.Get("Used for button/key mappings and gyro activation. This threshold does not change analogue trigger travel; use LT / RT deadzone above.")));
   CheckBox Flag(string caption,string key){var box=new CheckBox{Content=OnexStrings.Get(caption)};OnexInputGuard.Wire(box);OnexAccentResources(box,OnexPalette.Cyan);box.Click+=(s,e)=>{if(!_onexControllerSync)QueueOnexControllerTuning(key,JsonValue.CreateBooleanValue(box.IsChecked==true));};_onexControllerControls.Add(box);return box;}
   _onexControllerInvertLx=Flag(OnexStrings.Get("Invert left X"),"invertLx");_onexControllerInvertLy=Flag(OnexStrings.Get("Invert left Y"),"invertLy");_onexControllerInvertRx=Flag(OnexStrings.Get("Invert right X"),"invertRx");_onexControllerInvertRy=Flag(OnexStrings.Get("Invert right Y"),"invertRy");_onexControllerSwap=Flag(OnexStrings.Get("Swap left / right sticks"),"swapSticks");sticks.Children.Add(OnexSettingsColumns(_onexControllerInvertLx,_onexControllerInvertLy));sticks.Children.Add(OnexSettingsColumns(_onexControllerInvertRx,_onexControllerInvertRy));sticks.Children.Add(_onexControllerSwap);
   BuildOnexControllerLayout(sticks);
   sticks.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Deadzone убирает движение около центра: например, 5% помогает при небольшом дрейфе. Порог триггера используется только при назначении ему кнопки/клавиши; аналоговый диапазон задаётся отдельно для LT и RT выше. Порог назначения сравнивается с настроенным выходом триггера. Тач: 1%, геймпад: 5%.")));_onexControllerSticksReset=CreateOnexControllerGroupReset("sticks","OnexControllerSticksRestore");sticks.Children.Add(_onexControllerSticksReset);_onexControllerSticksTest=CreateOnexControllerDiagnosticButton("sticks");sticks.Children.Insert(0,_onexControllerSticksTest);
   var bottom=new StackPanel{Spacing=12};bottom.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Хотя бы одна из шести дополнительных кнопок должна открывать ONEXTweaks. Системные действия доступны на дополнительных кнопках; игровые назначения, клавиши и мышь работают через Virtual Controller.")));
   _onexControllerReset=new Button{Name="OnexControllerReset",Content=OnexStrings.Get("Reset this profile"),Style=(Style)Resources["StandaloneButtonStyle"]};
   _onexControllerReset.Click+=(s,e)=>
   {
    string profileId=ControllerEditorId;
    var menu=CreateOnexMenu();
    _onexControllerResetMenu=menu;
    menu.Closed+=(a,b)=>{if(ReferenceEquals(_onexControllerResetMenu,menu))_onexControllerResetMenu=null;};
    var reset=new MenuFlyoutItem{Text=OnexStrings.Get("Reset mappings & stick settings")};
    reset.Click+=async(a,b)=>
    {
     if(profileId!=ControllerEditorId)return;
     await ControllerActionAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("reset")},{"id",JsonValue.CreateStringValue(profileId)}});
    };
    menu.Items.Add(reset);
    menu.Items.Add(new MenuFlyoutItem{Text=OnexStrings.Get("Keep It")});
    menu.ShowAt(_onexControllerReset);
   };
   bottom.Children.Add(_onexControllerReset);main.Children.Add(bottom);var firmware=OnexControllerHardwareCard();firmware.Padding=new Thickness(0);firmware.Background=null;main.Children.Add(firmware);
   AddOnexControllerBackendStatus(main);ComposeOnexControllerCards(body,_onexControllerExtraPanel,mapping,sticks,main);
   body.Children.Add(CreateOnexControllerHardwareProfileCard());body.Children.Add(CreateOnexControllerCaptureCard());
   _onexControllerSavedProfiles=new OnexSavedCatalogSection(this,"Controller",()=>_onexControllerState,ChangeOnexControllerAsync,DescribeOnexControllerPolicy);body.Children.Add(_onexControllerSavedProfiles.Card);
   this.PreviewKeyUp+=(sender,args)=>{int key=(int)args.Key;if(key==_onexControllerOwnedKey){args.Handled=true;_onexControllerOwnedKey=-1;}if(_onexControllerRecording&&_onexControllerRecordModifiers.Remove(key))args.Handled=true;};
   _onexControllerSaveTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};_onexControllerSaveTimer.Tick+=async(s,e)=>{_onexControllerSaveTimer.Stop();await FlushOnexControllerPolicyAsync();};_onexControllerSource.SelectedIndex=0;UpdateOnexControllerEnabled();WireOnexControllerNavigation();
  }
  private static string OnexControllerTargetName(string value)=>value=="DualShock4"?"DualShock 4":"Xbox 360";
  private string DescribeOnexControllerPolicy(JsonObject policy)
  {var bindings=policy.GetNamedObject("bindings",null);int count=bindings?.Count(pair=>ControllerText(pair.Value.GetObject(),"kind","default")!="default")??0;var tuning=policy.GetNamedObject("tuning",null);return global::XboxGamingBar.OnexStrings.Format("Output: {0} · {1}{2} remaps · {3}\nDeadzone L / R: {4}% / {5}%{6}\nVibration: {7}\nGyro: {8}{9}",OnexControllerTargetName(ControllerText(policy,"virtualTarget","Xbox360")),(policy.GetNamedBoolean("useHardwareController",false)?global::XboxGamingBar.OnexStrings.Get("Hardware controller · "):""),count,(policy.GetNamedBoolean("mouseMode",false)?global::XboxGamingBar.OnexStrings.Get("Mouse mode"):global::XboxGamingBar.OnexStrings.Get("Gamepad")),(tuning?.GetNamedNumber("leftDeadzone",0)??0),(tuning?.GetNamedNumber("rightDeadzone",0)??0),DescribeOnexControllerTriggers(tuning),DescribeOnexControllerVibration(policy),DescribeOnexControllerGyro(policy),(policy.GetNamedBoolean("forwardMotion",false)?global::XboxGamingBar.OnexStrings.Get(" · DS4 motion On"):""));}
  private void ApplyOnexControllerState(string json)
  {
   if(!JsonObject.TryParse(json,out var state))return;
   if(ControllerEditorId!=ControllerText(state,"editorId","global"))CancelControllerProfileEdits();
   _onexControllerState=state;
   // Keep every fresh model and the Home tiles current. Hidden editor controls
   // do not need to be reprojected on each runtime heartbeat; doing so allocates
   // and invalidates native XAML while the owner is dragging another page's slider.
   if(LegionNavItem?.IsChecked!=true&&!_onexControllerReadOnlyProjection&&!_onexControllerBusy&&_onexControllerPending==null)
   {_onexControllerUiPending=true;_onexControllerHiddenStateUpdates++;RefreshOnexControllerHomeTiles();return;}
   EnsureOnexControllerFrontend();if(_onexControllerPicker==null){_onexControllerUiPending=true;return;}
   _onexControllerUiPending=false;_onexControllerUiApplyCount++;
   if(!state.ContainsKey("entries")){_onexControllerStatus.Text=OnexStrings.Get(ControllerText(state,"error",OnexStrings.Get("Controller settings unavailable")));return;}
   _onexControllerPicker.Apply(state);_onexControllerSavedProfiles?.Apply();bool previousSync=_onexControllerSync;_onexControllerSync=true;
   try
   {
    if(!_onexControllerTransport.IsDropDownOpen)_onexControllerTransport.SelectedIndex=state.GetNamedBoolean("enabled",false)?1:0;_onexControllerOemEnabled.IsOn=state.GetNamedBoolean("oemEnabled",false);var runtime=state.GetNamedObject("runtime",null);string error=ControllerText(runtime,"error");bool running=runtime?.GetNamedBoolean("running",false)==true;
    _onexControllerStatus.Text=(running?global::XboxGamingBar.OnexStrings.Format("On · {0}{1}",OnexControllerTargetName(ControllerText(runtime,"outputTarget","Xbox360")),(ControllerText(runtime,"outputTarget","Xbox360")=="Xbox360"?global::XboxGamingBar.OnexStrings.Format(" · Player {0}",OnexControllerGamePlayer(runtime)):global::XboxGamingBar.OnexStrings.Get(" · HID"))):global::XboxGamingBar.OnexStrings.Get("Off · physical controller"))+global::XboxGamingBar.OnexStrings.Get("\nApplied: ")+(ControllerText(state,"appliedId")=="global"?OnexStrings.Get("Global Profile"):ControllerText(state,"activeName","Global Profile"))+(ControllerEditorId!=ControllerText(state,"appliedId")?global::XboxGamingBar.OnexStrings.Get(" · editing an inactive profile"):"")+(string.IsNullOrEmpty(error)?"":"\n"+error);
    _onexControllerLive.Text=global::XboxGamingBar.OnexStrings.Format("Last input: {0}",ControllerText(runtime,"lastInput","—"));
    var policy=_onexControllerPending??ControllerEditorPolicy;if(policy==null)return;var tuning=policy.GetNamedObject("tuning",null);
    void SliderValue(Slider slider,string key,int fallback=0){if(!OnexInputGuard.IsEditing(slider))slider.Value=tuning?.GetNamedNumber(key,fallback)??fallback;}
    if(!_onexControllerTarget.IsDropDownOpen)_onexControllerTarget.SelectedIndex=ControllerText(policy,"virtualTarget","Xbox360")=="DualShock4"?1:0;
    _onexControllerLightingStatus.Text=ControllerText(state,"appliedId")!=ControllerEditorId?global::XboxGamingBar.OnexStrings.Get("Saved for this profile"):OnexStrings.Get(ControllerText(runtime,"lightingForwarding",OnexStrings.Get("Waiting for Virtual Controller")));
    _onexControllerForwardLighting.IsOn=policy.GetNamedBoolean("forwardGameLighting",false);
    _onexControllerForwardMotion.IsOn=policy.GetNamedBoolean("forwardMotion",false);
    ApplyOnexControllerMotion();ApplyOnexControllerGyro(policy);ApplyOnexControllerRumbleProfiles(policy);
    ApplyOnexControllerTriggers(tuning);ApplyOnexControllerLayout(tuning);
    SliderValue(_onexControllerLeftDeadzone,"leftDeadzone");SliderValue(_onexControllerRightDeadzone,"rightDeadzone");SliderValue(_onexControllerThreshold,"triggerThreshold",30);if(_onexControllerVibrationIntensity!=null&&!OnexInputGuard.IsEditing(_onexControllerVibrationIntensity))_onexControllerVibrationIntensity.Value=policy.GetNamedObject("vibration",null)?.GetNamedNumber("intensity",100)??100;
    _onexControllerInvertLx.IsChecked=tuning?.GetNamedBoolean("invertLx",false)==true;_onexControllerInvertLy.IsChecked=tuning?.GetNamedBoolean("invertLy",false)==true;_onexControllerInvertRx.IsChecked=tuning?.GetNamedBoolean("invertRx",false)==true;_onexControllerInvertRy.IsChecked=tuning?.GetNamedBoolean("invertRy",false)==true;_onexControllerSwap.IsChecked=tuning?.GetNamedBoolean("swapSticks",false)==true;
    RefreshOnexControllerHardwareProfile(policy);RefreshOnexControllerCaptureGroups();RefreshOnexControllerMapping();RefreshOnexControllerCards();RefreshOnexControllerBackendStatus();
   }
   finally{_onexControllerSync=previousSync;UpdateOnexControllerEnabled();ApplyOnexTabPalette();RefreshOnexControllerHomeTiles();}
  }
  private void FlushOnexControllerUi()
  {
   if(_onexControllerUiPending&&_onexControllerState!=null)ApplyOnexControllerState(_onexControllerState.Stringify());
  }
  private static readonly string[] ControllerBindingKinds={"default","disabled","gamepad","keyboard","mouse","widget","keyboardPopup","steamBigPicture","steamQuickAccess","inputMode"};
  private void RefreshOnexControllerMapping()
  {
   RefreshOnexControllerMappingOverview();
    if(_onexControllerKind==null)return;
   if(_controllerKeyboardArt?.HasOpenDropdown==true||OnexControllerMacroPopupOpen||new[]{_onexControllerKind,_onexControllerPadTarget,_onexControllerKeyboard,_onexControllerMouse,_onexControllerGesture}.Any(choice=>choice?.IsDropDownOpen==true))return;
   bool before=_onexControllerSync;_onexControllerSync=true;
   try
   {
    var policy=_onexControllerPending??ControllerEditorPolicy;string source=ControllerSourceId;if(policy==null||source==null)return;RefreshOnexControllerGestureEditor(policy);
    bool reserved=ControllerIsOem&&ControllerOemId=="KeyboardMouse"&&ControllerEditingDouble&&_onexControllerState.GetNamedBoolean("keyboardDoubleReserved",false);
    // Periodic Bridge updates describe the saved binding. The keyboard editor has an explicit Apply action.
    if(_controllerKeyboardDraftContext!=null&&KeepControllerKeyboardDraft(ReadOnexControllerBinding(ControllerEditorPolicy,source),reserved))
    {
     _controllerKeyboardArt?.SetEnabled(_onexControllerKeyboard.IsEnabled);
     return;
    }
    var binding=ReadOnexControllerBinding(policy,source);
    foreach(var special in _onexControllerKind.Items.OfType<ComboBoxItem>().Skip(5))special.IsEnabled=ControllerIsOem;string kind=reserved?"inputMode":ControllerText(binding,"kind",ControllerEditingDouble?"disabled":"default");_controllerKeyboardBinding=binding;if(kind=="keySequence")kind="keyboard";((ComboBoxItem)_onexControllerKind.Items[9]).IsEnabled=reserved;_onexControllerKind.IsEnabled=!reserved;bool sequence=kind=="macro";_onexControllerKind.SelectedIndex=Array.IndexOf(ControllerBindingKinds,sequence?"gamepad":kind);
    _onexControllerGamepadMode.SelectedIndex=_onexControllerPaintedGamepadMode=sequence?1:0;
    _onexControllerPadTarget.SelectedItem=_onexControllerPadTarget.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>(string)item.Tag==ControllerText(binding,"target","A"))??_onexControllerPadTarget.Items[0];
    JsonArray keys=null;if(binding!=null&&binding.TryGetValue("keys",out var keyValue)&&keyValue.ValueType==JsonValueType.Array)keys=keyValue.GetArray();if(keys!=null&&keys.Count>0)_onexControllerRecordedKeys=keys.Select(value=>(int)value.GetNumber()).ToArray();
    var match=_onexControllerKeyboard.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>((int[])item.Tag).SequenceEqual(_onexControllerRecordedKeys));if(match==null){match=new ComboBoxItem{Content=global::XboxGamingBar.OnexStrings.Format("Custom: {0}",OnexKeyboardCaption(_onexControllerRecordedKeys)),Tag=_onexControllerRecordedKeys.ToArray()};_onexControllerKeyboard.Items.Add(match);}_onexControllerKeyboard.SelectedItem=match;
    _onexControllerMouse.SelectedIndex=Math.Max(0,Array.IndexOf(AssignmentMouseTargets,ControllerText(binding,"target","left")));
    RefreshOnexControllerMacroEditor(binding);ApplyOnexControllerActionAvailability();UpdateOnexControllerMappingVisibility();
   }
   finally{_onexControllerSync=before;}
  }
  private JsonObject ReadOnexControllerBinding(JsonObject policy,string source)
  {
   if(policy==null)return null;
   if(ControllerEditingDouble)return ControllerDoubleConfig(policy)?.GetNamedObject("binding",null);
   if(ControllerIsOem)return policy.GetNamedObject("oem",null)?.GetNamedObject("bindings",null)?.GetNamedObject(ControllerOemId,null)
    ??_onexControllerState?.GetNamedObject("oemDefaults",null)?.GetNamedObject(ControllerOemId,null);
   return policy.GetNamedObject("bindings",null)?.GetNamedObject(source,null);
  }
  private void UpdateOnexControllerMappingVisibility()
  {
   int kind=_onexControllerKind.SelectedIndex;_onexControllerGamepadMode.Visibility=kind==2?Visibility.Visible:Visibility.Collapsed;
   _onexControllerPadTarget.Visibility=kind==2&&!OnexControllerEditingSequence?Visibility.Visible:Visibility.Collapsed;_onexControllerKeyboard.Visibility=_onexControllerRecord.Visibility=kind==3?Visibility.Visible:Visibility.Collapsed;_onexControllerMouse.Visibility=kind==4?Visibility.Visible:Visibility.Collapsed;
   RefreshControllerAssignmentArt(kind);
   UpdateOnexControllerMacroVisibility();_onexControllerMappingHint.Text=OnexControllerEditingSequence?OnexStrings.Get("Tap the gamepad diagram to add buttons in order."):ControllerIsOem?(kind==5?global::XboxGamingBar.OnexStrings.Get("Открывает и закрывает ONEXTweaks. Последнее такое назначение удалить нельзя."):kind==6?global::XboxGamingBar.OnexStrings.Get("Открывает штатную сенсорную клавиатуру Windows снизу."):kind==7?global::XboxGamingBar.OnexStrings.Get("Открывает Steam Big Picture. Если Big Picture уже открыт — вызывает его главное меню. Без Xbox / Guide и Win+G. Работает при включённом Extra button remapping."):kind==9?global::XboxGamingBar.OnexStrings.Get("Двойной Keyboard переключает Gamepad ↔ Keyboard & mouse для любого профиля. Это общее назначение настраивается на отдельной вкладке клавомыши."):kind==8?global::XboxGamingBar.OnexStrings.Get("Открывает правое меню Quick Access в Steam Big Picture. При необходимости сначала запускает Big Picture. Без Xbox / Guide и Win+G."):global::XboxGamingBar.OnexStrings.Get("Одна из шести дополнительных кнопок. Включи Extra button remapping; для игрового/клавиатурного назначения включи Virtual Controller.")):ControllerSourceId?.StartsWith("F",StringComparison.Ordinal)==true?global::XboxGamingBar.OnexStrings.Get("Команда F13–F24 от прошивки или клавиатуры. Пока физический сигнал не проверен, не считай его отдельной подтверждённой кнопкой ONEX."):kind==0?global::XboxGamingBar.OnexStrings.Get("Оригинальная функция сохраняется."):kind==1?global::XboxGamingBar.OnexStrings.Get("Этот контрол не передаётся в виртуальный геймпад."):kind==2?(IsSplitDs4TouchpadTarget((_onexControllerPadTarget.SelectedItem as ComboBoxItem)?.Tag as string)?OnexStrings.Get("Sends a touch and click on the selected half of the DualShock 4 touchpad."):((_onexControllerPadTarget.SelectedItem as ComboBoxItem)?.Tag as string)=="TouchpadClick"?global::XboxGamingBar.OnexStrings.Get("Sends the DS4 touchpad button while held. Requires Virtual Controller + DualShock 4; Xbox keeps the original control. Touch coordinates are not emulated."):(((_onexControllerPadTarget.SelectedItem as ComboBoxItem)?.Tag as string)=="Guide"?global::XboxGamingBar.OnexStrings.Get("Отправляет Xbox / Guide. Реакция зависит от Windows и Steam; для прямого вызова нашего виджета выбирай Open ONEXTweaks на дополнительной кнопке."):global::XboxGamingBar.OnexStrings.Get("Удержание физического контрола удерживает выбранный виртуальный."))):kind==3?global::XboxGamingBar.OnexStrings.Get("Клавиши удерживаются до отпускания контрола; при отключении режима освобождаются автоматически."):global::XboxGamingBar.OnexStrings.Get("Кнопка мыши удерживается; колесо делает один шаг на нажатие.");if(OnexControllerNintendoActive&&(kind==0||kind==2))_onexControllerMappingHint.Text+=OnexStrings.Get("\nNintendo layout swaps the final A/B and X/Y output, including gamepad mappings and macros.");NormalizeOnexControllerMappingSpacing();RefreshOnexRemappingWindows();WireOnexControllerNavigation();WireOnexControllerGestureNavigation();
   MatchOnexControllerSketchEditorToAction();
  }
  private void QueueOnexControllerBinding()
  {
   string source=ControllerSourceId;if(source==null||ControllerEditorPolicy==null)return;if(ControllerIsOem&&ControllerOemId=="KeyboardMouse"&&ControllerEditingDouble&&_onexControllerState.GetNamedBoolean("keyboardDoubleReserved",false))return;int kind=_onexControllerKind.SelectedIndex;bool sequence=OnexControllerEditingSequence;string code=sequence?"macro":ControllerBindingKinds[Math.Max(0,kind)];var binding=new JsonObject{{"kind",JsonValue.CreateStringValue(code)}};
   if(kind==2&&!sequence){if(!(_onexControllerPadTarget.SelectedItem is ComboBoxItem target)||!target.IsEnabled)return;binding["target"]=JsonValue.CreateStringValue((string)target.Tag);}
   if(kind==3){var keys=(_onexControllerKeyboard.SelectedItem as ComboBoxItem)?.Tag as int[]??_onexControllerRecordedKeys;binding["keys"]=AssignmentNumbers(keys);SubmitOnexControllerKeyboardBinding(binding);return;}
   if(kind==4)binding["target"]=JsonValue.CreateStringValue(AssignmentMouseTargets[Math.Max(0,_onexControllerMouse.SelectedIndex)]);
   if(sequence){if(!OnexControllerEditingVirtual)return;binding["macro"]=OnexControllerMacroBinding();}
   QueueOnexControllerAssignment(binding);
  }
  private void QueueOnexControllerAssignment(JsonObject binding)
  {
   string source=ControllerSourceId;
   if(source==null||ControllerEditorPolicy==null||_onexControllerReadOnlyProjection)return;
   bool isDouble=ControllerEditingDouble;
   if(isDouble&&ControllerOemId=="KeyboardMouse"&&_onexControllerState.GetNamedBoolean("keyboardDoubleReserved",false))return;
   QueueOnexControllerPolicy(policy=>{if(source.StartsWith("OEM:",StringComparison.Ordinal)&&isDouble){EditControllerDouble(policy,source.Substring(4),value=>value["binding"]=binding);}else if(source.StartsWith("OEM:",StringComparison.Ordinal)){var oem=policy.GetNamedObject("oem",null)??new JsonObject();var bindings=oem.GetNamedObject("bindings",null)??new JsonObject();bindings[source.Substring(4)]=binding;oem["bindings"]=bindings;policy["oem"]=oem;}else{var bindings=policy.GetNamedObject("bindings",null)??new JsonObject();bindings[source]=binding;policy["bindings"]=bindings;}});
  }
  private void QueueOnexControllerTuning(string key,IJsonValue value)
  {
   QueueOnexControllerPolicy(policy=>{var tuning=policy.GetNamedObject("tuning",null)??new JsonObject();tuning[key]=value;policy["tuning"]=tuning;});
   if(_onexControllerReadOnlyProjection||_onexControllerPending==null)return;
   bool was=_onexControllerSync;_onexControllerSync=true;try{ApplyOnexControllerTriggers(_onexControllerPending.GetNamedObject("tuning",null));ApplyOnexControllerLayout(_onexControllerPending.GetNamedObject("tuning",null));}finally{_onexControllerSync=was;}
   RefreshOnexControllerTriggerAvailability();RefreshOnexControllerLayoutAvailability();WireOnexControllerNavigation();
  }
  private void CancelControllerProfileEdits()
  {
   _onexControllerResetMenu?.Hide();
   CloseOnexRemapEditor(_onexControllerMappingFlyout);
   _onexControllerPending=null;
   _onexControllerPendingId=null;
   _onexControllerSaveTimer?.Stop();
   var sliders=new[]{_onexControllerLeftDeadzone,_onexControllerRightDeadzone,_onexControllerThreshold,
    _onexControllerLeftTriggerStart,_onexControllerLeftTriggerEnd,_onexControllerRightTriggerStart,_onexControllerRightTriggerEnd,
    _onexControllerVibrationIntensity,_onexControllerDoubleWindow,_onexControllerMacroInterval,_onexControllerMacroPress,
    _onexControllerGyroX,_onexControllerGyroY,_onexControllerGyroBoost,_onexControllerGyroSmoothing,
    _onexControllerGyroGate,_onexControllerGyroFloor};
   foreach(var slider in sliders.Concat(_onexControllerGyroCurveSliders.Values))OnexInputGuard.CancelInteraction(slider);
  }
  private void QueueOnexControllerPolicy(Action<JsonObject> edit)
  {if(_onexControllerReadOnlyProjection||_onexControllerSync||ControllerEditorPolicy==null)return;var policy=JsonObject.Parse((_onexControllerPending??ControllerEditorPolicy).Stringify());edit(policy);_onexControllerPending=policy;RefreshOnexControllerMappingOverview();RefreshOnexControllerCards();_onexControllerPendingId=ControllerEditorId;_onexControllerSaveTimer.Stop();_onexControllerSaveTimer.Start();}
  private async Task FlushOnexControllerPolicyAsync()
  {
   if(_onexControllerPending==null)return;if(_onexControllerBusy){_onexControllerSaveTimer.Start();return;}var policy=_onexControllerPending;string id=_onexControllerPendingId;_onexControllerPending=null;
   await ControllerActionAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("edit")},{"id",JsonValue.CreateStringValue(id)},{"policy",policy}},false);
  }
  private async Task ControllerActionAsync(JsonObject request,bool flush=true)
  {
   if(_onexControllerBusy)return;
   var owner=App.PipeClient;
   try
   {
    if(flush&&_onexControllerPending!=null)await FlushOnexControllerPolicyAsync();
    RequireOnexProfileConnection(owner);
    await ChangeOnexControllerAsync(request);
   }
   catch(Exception error){try{await ReadOnexControllerAsync();}catch{} _onexControllerStatus.Text=error.Message;if(_onexControllerOpen=="mapping"&&_onexControllerMappingHint!=null)_onexControllerMappingHint.Text=error.Message;}
  }
  private async Task<JsonObject> ReadOnexControllerAsync()
  {if(_onexControllerReadOnlyProjection)return _onexControllerState;var reply=await App.SendMessageAsync(new ValueSet{{"OnexControllerGet",true}});if(reply==null||reply.ContainsKey("Error"))throw new InvalidOperationException("Controller settings are unavailable");var state=JsonObject.Parse((string)reply["OnexControllerState"]);if(_onexControllerReadOnlyProjection)return _onexControllerState;ApplyOnexControllerState(state.Stringify());return state;}
  private async Task ChangeOnexControllerAsync(JsonObject request)
  {
   if(_onexControllerReadOnlyProjection)throw new InvalidOperationException("Read-only controller UI projection cannot change saved profiles");
   var owner=App.PipeClient;
   if(ControllerText(request,"operation")!="edit"&&_onexControllerPending!=null)await FlushOnexControllerPolicyAsync();
   RequireOnexProfileConnection(owner);
   _onexControllerBusy=true;UpdateOnexControllerEnabled();
   try{var reply=await SendOnexHomeRequestAsync(new ValueSet{{"OnexControllerSet",request.Stringify()}});ApplyOnexControllerState((string)reply["OnexControllerState"]);}
   finally{_onexControllerBusy=false;UpdateOnexControllerEnabled();if(_onexControllerPending!=null)_onexControllerSaveTimer.Start();}
  }
  private void UpdateOnexControllerEnabled()
  {bool ready=App.IsConnected&&_onexControllerState?.ContainsKey("entries")==true;foreach(var control in _onexControllerControls)control.IsEnabled=ready;foreach(var control in new Control[]{_onexControllerTransport,_onexControllerOemEnabled,_onexControllerUseHardware,_onexControllerRecord,_onexControllerAutomatic,_onexControllerRecover,_onexControllerReset,_onexControllerPicker?.Choose,_onexControllerPicker?.Create,_onexControllerPicker?.Rename,_onexControllerPicker?.Remove})if(control!=null)control.IsEnabled=ready&&(control!=_onexControllerPicker?.Rename&&control!=_onexControllerPicker?.Remove||ControllerEditorId!="global");if(_onexControllerUseHardware!=null)_onexControllerUseHardware.IsEnabled=ready&&ControllerEditorId!="global";if(_onexControllerTarget!=null)_onexControllerTarget.IsEnabled=ready&&OnexControllerEditingVirtual;if(_onexControllerForwardMotion!=null)_onexControllerForwardMotion.IsEnabled=ready&&_onexControllerState?.GetNamedBoolean("ds4AuxiliarySupported",false)==true&&OnexControllerEditingVirtual&&ControllerText(_onexControllerPending??ControllerEditorPolicy,"virtualTarget","Xbox360")=="DualShock4";if(_onexControllerForwardLighting!=null)_onexControllerForwardLighting.IsEnabled=ready&&_onexControllerState?.GetNamedBoolean("ds4LightingSupported",false)==true&&OnexControllerEditingVirtual&&ControllerText(_onexControllerPending??ControllerEditorPolicy,"virtualTarget","Xbox360")=="DualShock4";RefreshOnexControllerCaptureGroups();RefreshOnexControllerDs4Actions();RefreshOnexControllerGroupAvailability();ApplyOnexControllerActionAvailability();if(_onexControllerDoubleEnabled!=null&&ControllerIsOem&&ControllerOemId=="KeyboardMouse"&&_onexControllerState?.GetNamedBoolean("keyboardDoubleReserved",false)==true){_onexControllerDoubleEnabled.IsEnabled=false;_onexControllerDoubleWindow.IsEnabled=false;if(ControllerEditingDouble)_onexControllerKind.IsEnabled=false;}RefreshOnexControllerTriggerAvailability();RefreshOnexControllerLayoutAvailability();RefreshOnexControllerMotionAvailability();RefreshOnexControllerGyroAvailability();}
  private bool HandleOnexControllerRecordKey(KeyRoutedEventArgs args)
  {
   if(!_onexControllerRecording)return false;if(LegionNavItem?.IsChecked!=true||!ReferenceEquals(FocusManager.GetFocusedElement(),_onexControllerRecord)){_onexControllerRecording=false;_onexControllerRecord.Content=global::XboxGamingBar.OnexStrings.Get("Record keyboard shortcut");return false;}if(args.Key==VirtualKey.Escape||args.Key==VirtualKey.GamepadB){_onexControllerRecording=false;_onexControllerOwnedKey=(int)args.Key;_onexControllerRecord.Content=global::XboxGamingBar.OnexStrings.Get("Record keyboard shortcut");args.Handled=true;return true;}
   int key=(int)args.Key;if(key>=195&&key<=218)return false;if(key<8||key>254)return false;args.Handled=true;if(args.KeyStatus.WasKeyDown)return true;
   if(key==16||key==17||key==18||key==91||key==92||key>=160&&key<=165){_onexControllerRecordModifiers.Add(key);return true;}
   _onexControllerOwnedKey=key;_onexControllerRecordedKeys=_onexControllerRecordModifiers.Take(4).Concat(new[]{key}).Distinct().ToArray();_onexControllerRecording=false;_onexControllerRecord.Content=global::XboxGamingBar.OnexStrings.Get("Record keyboard shortcut");var item=new ComboBoxItem{Content=global::XboxGamingBar.OnexStrings.Format("Custom: {0}",OnexKeyboardCaption(_onexControllerRecordedKeys)),Tag=_onexControllerRecordedKeys.ToArray()};_onexControllerSync=true;_onexControllerKeyboard.Items.Add(item);_onexControllerKeyboard.SelectedItem=item;_onexControllerSync=false;QueueOnexControllerBinding();return true;
  }
  private Control[][] OnexControllerRows()=>OnexControllerCardRows();
  private void WireOnexControllerNavigation(){var rows=OnexControllerRows();for(int r=0;r<rows.Length;r++)for(int c=0;c<rows[r].Length;c++){var control=rows[r][c];control.XYFocusLeft=rows[r][Math.Max(0,c-1)];control.XYFocusRight=rows[r][Math.Min(rows[r].Length-1,c+1)];control.XYFocusUp=r>0?rows[r-1][Math.Min(c,rows[r-1].Length-1)]:control;control.XYFocusDown=r+1<rows.Length?rows[r+1][Math.Min(c,rows[r+1].Length-1)]:control;}}
  private bool HandleOnexControllerVertical(KeyRoutedEventArgs args)
  {if(HandleOnexControllerDiagramNavigation(args)||HandleOnexControllerCardBack(args))return true;if(args.Handled||LegionNavItem?.IsChecked!=true||!IsOnexProfileVertical(args.Key)||_onexDetailsDialog!=null||_onexOpenMenus.Any(menu=>menu.IsOpen))return false;var current=FocusManager.GetFocusedElement() as Control;if(current==null||current is ComboBox combo&&combo.IsDropDownOpen||!OnexControllerRows().SelectMany(row=>row).Contains(current))return false;var next=(IsOnexProfileDown(args.Key)?current.XYFocusDown:current.XYFocusUp) as Control;if(next==null)return false;args.Handled=true;_=Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,()=>{next.Focus(FocusState.Keyboard);next.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});});return true;}
 }
}
