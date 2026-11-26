// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private RadioButton _onexKeyboardMouseNav;private ScrollViewer _onexKeyboardMousePage;private StackPanel _kbmBody;
  private JsonObject _kbmState;private string _kbmLastWritePolicy;private int _kbmLastWriteRevision;private bool _kbmReadOnlyProjection;private bool _kbmSync,_kbmBusy;private TextBlock _kbmStatus,_kbmHelp,_kbmEditorTitle;
  private Button _kbmGamepad,_kbmActivate,_kbmReset,_kbmApplyCustom,_kbmBindingReset;private ComboBox _kbmFn,_kbmAction,_kbmKey1,_kbmKey2,_kbmMouse,_kbmGesture;
  private Slider _kbmMouseSpeed,_kbmScrollSpeed,_kbmDeadzone,_kbmHoldAcceleration,_kbmNudgeStep;private Button _kbmPrecisionReset;private CheckBox _kbmAcceleration,_kbmAdaptive;private readonly List<CheckBox> _kbmModifiers=new List<CheckBox>();private readonly List<ComboBox> _kbmKeys=new List<ComboBox>();
  private Flyout _kbmFlyout;private StackPanel _kbmCustom;private string _kbmSource="A",_kbmLayer="base";
  private readonly List<Control[]> _kbmBaseRows=new List<Control[]>();
  private readonly Dictionary<string,Viewbox> _kbmDiagrams=new Dictionary<string,Viewbox>();
  private readonly Dictionary<string,Button> _kbmTiles=new Dictionary<string,Button>(),_kbmDiagramKeys=new Dictionary<string,Button>();
  private readonly Dictionary<string,TextBlock> _kbmSummaries=new Dictionary<string,TextBlock>(),_kbmFnSummaries=new Dictionary<string,TextBlock>(),_kbmDiagramNames=new Dictionary<string,TextBlock>(),_kbmDiagramValues=new Dictionary<string,TextBlock>();
  private readonly Dictionary<string,IJsonValue> _kbmTuning=new Dictionary<string,IJsonValue>();private DispatcherTimer _kbmTimer;
  private Grid _kbmCards;
  private OnexConnectedCardOutline _kbmOutline;
  private KbmSection[] _kbmSections;
  private KbmSection _kbmOpenSection;

  private sealed class KbmSection
  {
   internal Button Header;
   internal Border Detail;
   internal FontIcon Icon, Chevron;
  }

  private void AddKbmSettingsCards(Border speed, Border precision, Border mapping)
  {
   _kbmCards=new Grid{Name="OnexKbmSettingsCards",ColumnSpacing=12};
   _kbmCards.ColumnDefinitions.Add(new ColumnDefinition());
   _kbmCards.ColumnDefinitions.Add(new ColumnDefinition());
   for(int row=0;row<4;row++)
    _kbmCards.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

   KbmSection Add(string name,string title,string glyph,Border detail,int row,int column,int span)
   {
    var icon=new FontIcon{Glyph=glyph,FontSize=23,Width=24,VerticalAlignment=VerticalAlignment.Center};
    var caption=new TextBlock{Text=OnexStrings.Get(title),FontSize=18,FontWeight=Windows.UI.Text.FontWeights.SemiBold,
     TextWrapping=TextWrapping.WrapWholeWords,VerticalAlignment=VerticalAlignment.Center};
    var chevron=new FontIcon{Glyph="\uE70D",FontSize=13,VerticalAlignment=VerticalAlignment.Center};
    var content=new Grid{ColumnSpacing=12};
    content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24)});
    content.ColumnDefinitions.Add(new ColumnDefinition());
    content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(18)});
    Grid.SetColumn(caption,1);
    Grid.SetColumn(chevron,2);
    content.Children.Add(icon);
    content.Children.Add(caption);
    content.Children.Add(chevron);

    var header=KbmButton("OnexKbmCard"+name,title);
    header.Content=content;
    header.HorizontalContentAlignment=HorizontalAlignment.Stretch;
    header.MinHeight=104;
    header.Padding=new Thickness(14,12,14,12);
    header.Margin=new Thickness(0,0,0,12);
    header.BorderThickness=new Thickness(1);
    header.CornerRadius=new CornerRadius(12);
    Windows.UI.Xaml.Automation.AutomationProperties.SetName(header,caption.Text);
    Grid.SetRow(header,row);
    Grid.SetColumn(header,column);
    Grid.SetColumnSpan(header,span);
    Grid.SetRow(detail,row+1);
    Grid.SetColumnSpan(detail,2);
    detail.Margin=new Thickness(0,0,0,row==0?12:0);
    detail.Visibility=Visibility.Collapsed;
    _kbmCards.Children.Add(header);
    _kbmCards.Children.Add(detail);
    var section=new KbmSection{Header=header,Detail=detail,Icon=icon,Chevron=chevron};
    header.Click+=(s,e)=>SetKbmSettingsCard(ReferenceEquals(_kbmOpenSection,section)?null:section);
    return section;
   }

   _kbmSections=new[]{
    Add("Speed","Mouse speed","\uEC4A",speed,0,0,1),
    Add("Precision","Mouse precision","\uE81E",precision,0,1,1),
    Add("Mapping","Full Remapping","\uE7FC",mapping,2,0,2)
   };
   _kbmOutline=new OnexConnectedCardOutline(_kbmCards);
   _kbmBody.Children.Add(_kbmCards);
  }

  private void SetKbmSettingsCard(KbmSection section)
  {
   if(ReferenceEquals(_kbmOpenSection,section))return;
   _kbmOpenSection=section;
   foreach(var card in _kbmSections)
   {
    bool open=ReferenceEquals(card,section);
    card.Detail.Visibility=open?Visibility.Visible:Visibility.Collapsed;
    card.Chevron.Glyph=open?"\uE70E":"\uE70D";
   }
   if(section!=null)PaintOnexPageContent(section.Detail,OnexKeyboardMouseAccent);
   PaintKbmSettingsCards();
  }

  private void PaintKbmSettingsCards()
  {
   if(_kbmSections==null)return;
   var accent=OnexSolidBrush(OnexKeyboardMouseAccent);
   foreach(var card in _kbmSections)
   {
    card.Icon.Foreground=accent;
    card.Header.BorderBrush=ReferenceEquals(card,_kbmOpenSection)?accent:(Brush)Resources["CardBorderBrush"];
    card.Detail.BorderBrush=accent;
   }
   _kbmOutline.Set(_kbmOpenSection?.Header,_kbmOpenSection?.Detail,accent);
  }
  private static JsonObject KbmObject(JsonObject parent,string name)
   =>parent!=null&&parent.TryGetValue(name,out var value)&&value.ValueType==JsonValueType.Object?value.GetObject():null;
  private JsonObject KbmPolicy=>KbmObject(_kbmState,"policy");
  private bool KbmDouble=>_kbmSource=="Start"&&_kbmGesture?.SelectedIndex==1;
  private static JsonObject KbmBinding(string kind,string target="",params int[] keys){var value=new JsonObject{{"kind",JsonValue.CreateStringValue(kind)},{"target",JsonValue.CreateStringValue(target)}};if(keys.Length>0){var array=new JsonArray();foreach(int key in keys)array.Add(JsonValue.CreateNumberValue(key));value["keys"]=array;}return value;}
  private ComboBox KbmChoice(string name,string caption)
  {var box=OnexSettingsChoice(caption);box.Name=name;box.Background=OnexSettingsBrush(39,47,60);box.Foreground=OnexSettingsBrush(238,241,246);OnexInputGuard.Wire(box);return box;}
  private Button KbmButton(string name,string text)=>new Button{Name=name,Content=OnexStrings.Get(text),Style=(Style)Resources["StandaloneButtonStyle"],HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Center};
  private Border KbmCard(UIElement child)=>new Border{Child=child,Padding=new Thickness(14),CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),BorderBrush=OnexSolidBrush(OnexPalette.Cyan),Background=OnexSettingsBrush(26,32,42)};
  private void EnsureOnexKeyboardMouseFrontend()
  {
   if(_onexKeyboardMousePage!=null||!(LegionScrollViewer.Parent is Panel parent))return;
   _onexKeyboardMouseNav=new RadioButton{Tag="KeyboardMouse",GroupName="MainNav",Style=(Style)Resources["NavRadioButtonStyle"],IsTabStop=false};_onexKeyboardMouseNav.Content=CreateOnexKeyboardMouseNavIcon(_onexKeyboardMouseNav);Windows.UI.Xaml.Automation.AutomationProperties.SetName(_onexKeyboardMouseNav,global::XboxGamingBar.OnexStrings.Get("Keyboard & mouse"));ToolTipService.SetToolTip(_onexKeyboardMouseNav,global::XboxGamingBar.OnexStrings.Get("Keyboard & mouse"));_onexKeyboardMouseNav.Checked+=NavRadioButton_Checked;
   _kbmBody=new StackPanel{Spacing=12,Padding=new Thickness(16,8,16,16)};_onexKeyboardMousePage=new ScrollViewer{Name="OnexKeyboardMousePage",Content=_kbmBody,Visibility=Visibility.Collapsed,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};parent.Children.Add(_onexKeyboardMousePage);
   var header=new StackPanel{Spacing=10};header.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Keyboard & mouse"),FontSize=21,FontWeight=Windows.UI.Text.FontWeights.SemiBold});_kbmGamepad=KbmButton("OnexKbmGamepad",global::XboxGamingBar.OnexStrings.Get("Gamepad"));_kbmActivate=KbmButton("OnexKbmActivate",global::XboxGamingBar.OnexStrings.Get("Keyboard & mouse"));_kbmGamepad.Click+=async(s,e)=>await KbmChange(new JsonObject{{"operation",JsonValue.CreateStringValue("mode")},{"mode",JsonValue.CreateStringValue("gamepad")}});_kbmActivate.Click+=async(s,e)=>await KbmChange(new JsonObject{{"operation",JsonValue.CreateStringValue("mode")},{"mode",JsonValue.CreateStringValue("keyboardMouse")}});header.Children.Add(OnexSettingsColumns(_kbmGamepad,_kbmActivate));_kbmStatus=ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Подключение…"));header.Children.Add(_kbmStatus);header.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Двойное нажатие Keyboard переключает Gamepad ↔ Keyboard & mouse из любого игрового профиля. Одиночное нажатие открывает клавиатуру. Эти настройки общие и не изменяют игровые профили.")));_kbmBody.Children.Add(KbmCard(header));
   var tuning=new StackPanel{Spacing=12};var pointer=new StackPanel{Spacing=8};var scroll=new StackPanel{Spacing=8};
   Slider Speed(StackPanel host,string name,string title,string key){host.Children.Add(ControllerHelp(OnexStrings.Get(title)));var slider=new Slider{Name=name,Minimum=25,Maximum=300,StepFrequency=1,SmallChange=5,Value=100,Style=(Style)Resources["LightMediaSliderStyle"]};var caption=ControllerHelp("100%");host.Children.Add(slider);host.Children.Add(caption);OnexInputGuard.ConfigurePercentage(slider);void Commit(){if(!_kbmSync)QueueKbmTuning(key,JsonValue.CreateNumberValue(Math.Round(slider.Value)));}OnexInputGuard.SetCommit(slider,Commit);slider.ValueChanged+=(s,e)=>{caption.Text=Math.Round(slider.Value)+"%";if(!_kbmSync&&OnexInputGuard.IsActive(slider))Commit();};return slider;}
   _kbmMouseSpeed=Speed(pointer,"OnexKbmMouseSpeed","Mouse speed","mouseSpeed");_kbmMouseSpeed.Minimum=1;_kbmMouseSpeed.Maximum=400;_kbmScrollSpeed=Speed(scroll,"OnexKbmScrollSpeed","Scroll speed","scrollSpeed");
   _kbmAcceleration=new CheckBox{Name="OnexKbmAcceleration",Content=global::XboxGamingBar.OnexStrings.Get("Mouse acceleration")};_kbmAdaptive=new CheckBox{Name="OnexKbmAdaptive",Content=global::XboxGamingBar.OnexStrings.Get("Adaptive scroll")};foreach(var check in new[]{_kbmAcceleration,_kbmAdaptive})OnexInputGuard.Wire(check);_kbmAcceleration.Click+=(s,e)=>{if(!_kbmSync)QueueKbmTuning("mouseAcceleration",JsonValue.CreateBooleanValue(_kbmAcceleration.IsChecked==true));};_kbmAdaptive.Click+=(s,e)=>{if(!_kbmSync)QueueKbmTuning("adaptiveScroll",JsonValue.CreateBooleanValue(_kbmAdaptive.IsChecked==true));};pointer.Children.Add(_kbmAcceleration);pointer.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Точнее у центра стика, быстрее у края. Off — линейная скорость.")));scroll.Children.Add(_kbmAdaptive);scroll.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Чем сильнее отклонён стик, тем быстрее прокрутка. Off — постоянная скорость.")));tuning.Children.Add(OnexSettingsColumns(pointer,scroll));
   var mapping=new StackPanel{Spacing=12};
   _kbmFn=KbmChoice("OnexKbmFn",global::XboxGamingBar.OnexStrings.Get("FN modifier · hold"));_kbmFn.Items.Add(new ComboBoxItem{Content=global::XboxGamingBar.OnexStrings.Get("None"),Tag=""});foreach(string id in OnexControllerOemSources)_kbmFn.Items.Add(new ComboBoxItem{Content=OnexControllerSourceName(id)+(id=="OEM:KeyboardMouse"||id=="OEM:Onex"?global::XboxGamingBar.OnexStrings.Get(" · system shortcut"):""),Tag=id,IsEnabled=id!="OEM:KeyboardMouse"&&id!="OEM:Onex"});_kbmFn.SelectionChanged+=async(s,e)=>{if(!_kbmSync&&!_kbmFn.IsDropDownOpen&&OnexInputGuard.IsActive(_kbmFn))await KbmChange(new JsonObject{{"operation",JsonValue.CreateStringValue("fn")},{"source",JsonValue.CreateStringValue((string)((ComboBoxItem)_kbmFn.SelectedItem).Tag)}});};_kbmFn.DropDownClosed+=async(s,e)=>{if(!_kbmSync&&OnexInputGuard.IsActive(_kbmFn))await KbmChange(new JsonObject{{"operation",JsonValue.CreateStringValue("fn")},{"source",JsonValue.CreateStringValue((string)((ComboBoxItem)_kbmFn.SelectedItem).Tag)}});};
   mapping.Children.Add(_kbmFn);
   mapping.Children.Add(ControllerHelp(OnexStrings.Get("FN — одна из Home, G-Key, M1 или M2. Keyboard и ONEX передают системные сочетания; удержание для FN на них не подтверждено. Удерживай FN для второго слоя; без FN используются основные назначения. Двойной Keyboard всегда переключает режим.")));
   BuildKbmPrecision();
   BuildKbmMapping(mapping);
   AddKbmSettingsCards(KbmCard(tuning),_kbmPrecisionCard,KbmCard(mapping));
   _kbmReset=KbmButton("OnexKbmRestore",global::XboxGamingBar.OnexStrings.Get("Restore Default"));_kbmReset.Click+=async(s,e)=>await KbmChange(new JsonObject{{"operation",JsonValue.CreateStringValue("reset")}});_kbmBody.Children.Add(_kbmReset);
   _kbmTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(160)};_kbmTimer.Tick+=async(s,e)=>await FlushKbmTuning();
  }
  private Windows.UI.Color OnexKeyboardMouseAccent=>OnexRoleAccent(1);
  private void ApplyOnexKeyboardMouseAppearance()
  {
   if(_kbmBody==null)return;
   if(_onexAppliedTheme!=null)ApplyOnexCatalogSurfaces(_kbmBody,_onexAppliedTheme);
   PaintOnexPageContent(_kbmBody,OnexKeyboardMouseAccent);
   PaintKbmSettingsCards();
   RefreshKbmMapping();
  }
  private JsonObject KbmEffective(string source,string layer,bool twice=false)=>twice
   ?KbmObject(_kbmState,layer=="fn"?"fnStartDouble":"startDouble")
   :KbmObject(KbmObject(_kbmState,layer=="fn"?"fnBindings":"baseBindings"),source);
  private static JsonArray KbmKeys(JsonObject binding)=>binding!=null&&binding.TryGetValue("keys",out var value)&&value.ValueType==JsonValueType.Array?value.GetArray():null;
  private static string KbmMouseCaption(string target)
  {
   switch(target){case "left":return OnexStrings.Get("Left click");case "right":return OnexStrings.Get("Right click");case "middle":return OnexStrings.Get("Middle click");case "wheelUp":return OnexStrings.Get("Wheel up");case "wheelDown":return OnexStrings.Get("Wheel down");case "wheelLeft":return OnexStrings.Get("Scroll left");case "wheelRight":return OnexStrings.Get("Scroll right");case "x1":return OnexStrings.Get("Back (X1)");case "x2":return OnexStrings.Get("Forward (X2)");default:return target;}
  }
  private string KbmSummary(JsonObject binding)
  {
   string kind=ControllerText(binding,"kind","disabled"),target=ControllerText(binding,"target");
   switch(kind)
   {
    case "keyboard":return OnexKeyboardCaption(ReadAssignmentNumbers(binding,"keys"));
    case "keySequence":return OnexKeyboardCaption(ReadAssignmentNumbers(binding,"keys"),true);
    case "combo":return OnexKeyboardCaption(ReadAssignmentNumbers(binding,"keys"))+" + "+KbmMouseCaption(target);
    case "mouse":return KbmMouseCaption(target);
    case "cursor":return OnexStrings.Format("Cursor {0}",OnexStrings.Get(target));
    case "scroll":return OnexStrings.Format("Scroll {0}",OnexStrings.Get(target));
    case "nudge":return OnexStrings.Format("Nudge {0}",OnexStrings.Get(target));
    case "widget":return OnexStrings.Get("ONEXTweaks");
    case "keyboardPopup":return OnexStrings.Get("Windows keyboard");
    case "security":return OnexStrings.Get("Ctrl + Alt + Delete");
    case "fn":return OnexStrings.Get("FN · hold");
    default:return OnexStrings.Get("Off");
   }
  }
  private void ApplyOnexKeyboardMouseState(string json)
  {
   if(!JsonObject.TryParse(json??"",out var state))return;if(_kbmState!=null&&ControllerText(state,"generation")==ControllerText(_kbmState,"generation")&&(state.GetNamedNumber("revision",0)<_kbmState.GetNamedNumber("revision",0)||state.GetNamedNumber("modeRevision",0)<_kbmState.GetNamedNumber("modeRevision",0)))return;EnsureOnexKeyboardMouseFrontend();if(_onexKeyboardMousePage==null)return;bool changed=_kbmState==null||ControllerText(state,"generation")!=ControllerText(_kbmState,"generation")||state.GetNamedNumber("revision",0)!=_kbmState.GetNamedNumber("revision",0);_kbmState=state;_kbmSync=true;
   try{bool active=ControllerText(state,"mode")=="keyboardMouse";_kbmStatus.Text=OnexStrings.Get(active?"Keyboard & mouse active":"Gamepad active")+" · "+(ControllerText(state,"activeGamepadProfileId")=="global"?OnexStrings.Get("Global Profile"):ControllerText(state,"activeGamepadProfileName"))+(state.GetNamedBoolean("fnActive",false)?global::XboxGamingBar.OnexStrings.Get(" · FN"):"");if(state.TryGetValue("error",out var error)&&error.ValueType==JsonValueType.String&&!string.IsNullOrEmpty(error.GetString()))_kbmStatus.Text+="\n"+error.GetString();_kbmGamepad.BorderBrush=!active?OnexSolidBrush(OnexKeyboardMouseAccent):(Brush)Resources["CardBorderBrush"];_kbmActivate.BorderBrush=active?OnexSolidBrush(OnexKeyboardMouseAccent):(Brush)Resources["CardBorderBrush"];
    string systemError=ControllerText(state.GetNamedObject("oemRuntime",null)?.GetNamedObject("system",null),"error");if(!string.IsNullOrEmpty(systemError))_kbmStatus.Text+="\n"+systemError;var policy=KbmPolicy;if(policy==null)return;if(!OnexInputGuard.IsEditing(_kbmMouseSpeed)&&!_kbmTuning.ContainsKey("mouseSpeed"))_kbmMouseSpeed.Value=policy.GetNamedNumber("mouseSpeed",100);if(!OnexInputGuard.IsEditing(_kbmScrollSpeed)&&!_kbmTuning.ContainsKey("scrollSpeed"))_kbmScrollSpeed.Value=policy.GetNamedNumber("scrollSpeed",100);if(!_kbmTuning.ContainsKey("mouseAcceleration"))_kbmAcceleration.IsChecked=policy.GetNamedBoolean("mouseAcceleration",true);if(!_kbmTuning.ContainsKey("adaptiveScroll"))_kbmAdaptive.IsChecked=policy.GetNamedBoolean("adaptiveScroll",true);ApplyKbmPrecision(policy);string fn=ControllerText(policy,"fnButton");if(!_kbmFn.IsDropDownOpen)_kbmFn.SelectedItem=_kbmFn.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>(string)item.Tag==fn)??_kbmFn.Items[0];
    if(changed){ApplyOnexKeyboardMouseAppearance();if(IsOnexRemapOpen(_kbmFlyout))RefreshKbmEditor();}
   }finally{_kbmSync=false;RefreshOnexControllerHomeTiles();}
  }
  private async Task ReadOnexKeyboardMouseAsync(){if(_kbmReadOnlyProjection)return;var reply=await App.SendMessageAsync(new ValueSet{{"OnexKeyboardMouseGet",true}});if(!_kbmReadOnlyProjection&&reply?.ContainsKey("OnexKeyboardMouseState")==true)ApplyOnexKeyboardMouseState((string)reply["OnexKeyboardMouseState"]);}
  private async Task KbmChange(JsonObject request)
  {if(_kbmReadOnlyProjection||_kbmBusy)return;_kbmBusy=true;try{var reply=await SendOnexHomeRequestAsync(new ValueSet{{"OnexKeyboardMouseSet",request.Stringify()}});ApplyOnexKeyboardMouseState((string)reply["OnexKeyboardMouseState"]);_kbmLastWritePolicy=KbmPolicy?.Stringify();_kbmLastWriteRevision=(int)_kbmState.GetNamedNumber("revision");if(_kbmFlyout?.IsOpen==true){_kbmSync=true;try{RefreshKbmEditor();}finally{_kbmSync=false;RefreshOnexControllerHomeTiles();}}if(reply.ContainsKey("OnexControllerState"))ApplyOnexControllerState((string)reply["OnexControllerState"]);}catch(Exception error){_kbmStatus.Text=error.Message;}finally{_kbmBusy=false;if(_kbmTuning.Count>0)_kbmTimer.Start();}}
  private void QueueKbmTuning(string key,IJsonValue value){_kbmTuning[key]=value;_kbmTimer.Stop();_kbmTimer.Start();}
  private async Task FlushKbmTuning(){_kbmTimer.Stop();if(_kbmBusy){_kbmTimer.Start();return;}foreach(var pair in _kbmTuning.ToArray()){_kbmTuning.Remove(pair.Key);await KbmChange(new JsonObject{{"operation",JsonValue.CreateStringValue("tuning")},{"key",JsonValue.CreateStringValue(pair.Key)},{"value",pair.Value}});}}
 }
}
