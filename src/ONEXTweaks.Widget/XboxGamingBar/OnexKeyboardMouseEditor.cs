// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private KeyboardAssignmentPicker _kbmKeyboardArt;
  private AssignmentDiagram _kbmMouseArt;
  private ComboBox _kbmDiagramMouseAction;
  private ComboBoxItem _kbmKeyboardType,_kbmMouseType;
  private string _kbmKeyboardContext;
  private Button _kbmMainLayerButton,_kbmFnLayerButton;
  private TextBlock _kbmMainLayerValue,_kbmFnLayerValue,_kbmFnUnavailableNote;

  private string KbmEditorKind
  {
   get
   {
    var selected=_kbmAction?.SelectedItem as ComboBoxItem;
    if(ReferenceEquals(selected,_kbmKeyboardType))return "keyboard";
    if(ReferenceEquals(selected,_kbmMouseType))return "mouse";
    return ControllerText(selected?.Tag as JsonObject,"kind","");
   }
  }

  private StackPanel _kbmBindingEditor;
  private void UpdateKbmAssignmentArt(JsonObject binding,bool force=false)
  {
   var editor=_kbmBindingEditor;
   string kind=KbmEditorKind;
   if(kind=="keyboard"&&_kbmKeyboardArt==null)
   {
    _kbmKeyboardArt=new KeyboardAssignmentPicker(this,KbmSaveBinding);
    editor.Children.Insert(editor.Children.IndexOf(_kbmAction)+1,_kbmKeyboardArt.View);
   }
   if(kind=="mouse"&&_kbmMouseArt==null)
   {
    _kbmDiagramMouseAction=KbmChoice("OnexKbmMouseAction",OnexStrings.Get("Mouse action"));
    foreach(string target in AssignmentMouseTargets)
     _kbmDiagramMouseAction.Items.Add(new ComboBoxItem{Tag=target,Content=KbmMouseCaption(target)});
    async Task Choose(string target)
    {
     if(_kbmBusy||!_kbmDiagramMouseAction.IsEnabled||string.IsNullOrEmpty(target))return;
     _kbmSync=true;
     try{_kbmDiagramMouseAction.SelectedIndex=Array.IndexOf(AssignmentMouseTargets,target);}
     finally{_kbmSync=false;}
     _kbmMouseArt.Select(target);
     await KbmSaveBinding(KbmBinding("mouse",target));
    }
    void SelectFromList()
    {
     if(_kbmSync||_kbmDiagramMouseAction.IsDropDownOpen||!OnexInputGuard.IsActive(_kbmDiagramMouseAction))return;
     _=Choose((_kbmDiagramMouseAction.SelectedItem as ComboBoxItem)?.Tag as string);
    }
    _kbmDiagramMouseAction.SelectionChanged+=(s,e)=>SelectFromList();
    _kbmDiagramMouseAction.DropDownClosed+=(s,e)=>SelectFromList();
    _kbmMouseArt=new AssignmentDiagram(MouseAssignmentArt,KbmMouseCaption,async target=>await Choose(target),205);
    int next=editor.Children.IndexOf(_kbmAction)+1;
    editor.Children.Insert(next,_kbmDiagramMouseAction);
    editor.Children.Insert(next+1,_kbmMouseArt.View);
   }
   if(_kbmKeyboardArt!=null)
   {
    _kbmKeyboardArt.View.Visibility=kind=="keyboard"?Visibility.Visible:Visibility.Collapsed;
    if(kind=="keyboard")
    {
     string context=_kbmSource+"|"+_kbmLayer+"|"+KbmDouble;
     _kbmKeyboardArt.Load(binding,force||context!=_kbmKeyboardContext);
     _kbmKeyboardContext=context;
     _kbmKeyboardArt.SetEnabled(_kbmAction.IsEnabled&&!_kbmReadOnlyProjection);
    }
   }
   if(_kbmMouseArt!=null)
   {
    _kbmDiagramMouseAction.Visibility=_kbmMouseArt.View.Visibility=kind=="mouse"?Visibility.Visible:Visibility.Collapsed;
    _kbmDiagramMouseAction.IsEnabled=_kbmAction.IsEnabled&&!_kbmReadOnlyProjection;
    if(kind=="mouse")
    {
     var target=ControllerText(binding,"target");
     _kbmDiagramMouseAction.SelectedIndex=Array.IndexOf(AssignmentMouseTargets,target);
     _kbmMouseArt.Select(target);
     foreach(string id in AssignmentMouseTargets)_kbmMouseArt.SetAvailable(id,_kbmDiagramMouseAction.IsEnabled,null);
    }
   }
   RefreshOnexRemappingWindows();
  }

  private async Task KbmActionSelected()
  {
   if(_kbmSync||_kbmAction.IsDropDownOpen||!OnexInputGuard.IsActive(_kbmAction))return;
   var selected=_kbmAction.SelectedItem as ComboBoxItem;
   _kbmCustom.Visibility=selected?.Tag==null?Visibility.Visible:Visibility.Collapsed;
   if(selected?.Tag is JsonObject preset)
   {
    await KbmSaveBinding(preset);
   }
   else
   {
    var saved=KbmEffective(_kbmSource,_kbmLayer,KbmDouble);
    var savedKind=ControllerText(saved,"kind");
    UpdateKbmAssignmentArt(savedKind==KbmEditorKind||savedKind=="keySequence"&&KbmEditorKind=="keyboard"?saved:null,true);
   }
   RefreshOnexRemappingWindows();
  }

  private void AddKbmLayerChoices(StackPanel editor)
  {
   Button Choice(string layer,string name,string label,out TextBlock value)
   {
    var button=KbmButton(name,label);
    button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
    button.BorderThickness=new Thickness(1);
    button.Content=KbmBindingLine(OnexStrings.Get(label),out value);
    button.Click+=(sender,args)=>SelectKbmEditorLayer(layer);
    return button;
   }
   _kbmMainLayerButton=Choice("base","OnexKbmMainLayer","Main:",out _kbmMainLayerValue);
   _kbmFnLayerButton=Choice("fn","OnexKbmFnLayer","Fn layer:",out _kbmFnLayerValue);
   _kbmMainLayerButton.XYFocusRight=_kbmFnLayerButton;
   _kbmFnLayerButton.XYFocusLeft=_kbmMainLayerButton;
   editor.Children.Add(OnexSettingsColumns(_kbmMainLayerButton,_kbmFnLayerButton));
   _kbmFnUnavailableNote=ControllerHelp(OnexStrings.Get("Choose a Fn modifier in Full Remapping to use this layer."));
   editor.Children.Add(_kbmFnUnavailableNote);
  }

  private void SelectKbmEditorLayer(string layer)
  {
   if(_kbmBusy||layer==_kbmLayer||layer=="fn"&&_kbmFnLayerButton?.IsEnabled!=true)return;
   _kbmLayer=layer;
   bool previous=_kbmSync;
   _kbmSync=true;
   try{RefreshKbmEditor();}
   finally{_kbmSync=previous;}
  }

  private void PaintKbmEditorLayers(bool modifier)
  {
   if(_kbmMainLayerButton==null)return;
   _kbmFnLayerButton.IsEnabled=!modifier;
   var palette=OnexCurrentPalette;
   void Paint(Button button,TextBlock value,string layer,string label)
   {
    bool selected=_kbmLayer==layer;
    button.Background=OnexSolidBrush(selected?palette.Selected:palette.Raised);
    button.BorderBrush=OnexSolidBrush(selected?palette.SelectedLine:palette.Line);
    PaintKbmLayerValue(value,_kbmSource,layer,true,selected);
    Windows.UI.Xaml.Automation.AutomationProperties.SetName(button,OnexStrings.Get(label)+" "+value.Text);
   }
   Paint(_kbmMainLayerButton,_kbmMainLayerValue,"base","Main:");
   Paint(_kbmFnLayerButton,_kbmFnLayerValue,"fn","Fn layer:");
   _kbmFnUnavailableNote.Visibility=_kbmLayer=="fn"&&string.IsNullOrEmpty(ControllerText(KbmPolicy,"fnButton"))?Visibility.Visible:Visibility.Collapsed;
   _kbmBindingReset.Content=OnexStrings.Get(_kbmLayer=="fn"?"Use Main action":"Restore Default");
  }

  private void BuildKbmEditor()
  {
   if(_kbmFlyout!=null)return;
   var editor=_kbmBindingEditor=new StackPanel{Spacing=12,RequestedTheme=ElementTheme.Dark};
   _kbmEditorTitle=new TextBlock{FontSize=18,TextWrapping=TextWrapping.Wrap};
   editor.Children.Add(_kbmEditorTitle);
   AddKbmLayerChoices(editor);
   _kbmGesture=KbmChoice("OnexKbmGesture",OnexStrings.Get("Gesture"));
   _kbmGesture.Items.Add(new ComboBoxItem{Content=OnexStrings.Get("Single"),Tag="single"});
   _kbmGesture.Items.Add(new ComboBoxItem{Content=OnexStrings.Get("Double"),Tag="double"});
   _kbmGesture.SelectedIndex=0;
   _kbmGesture.SelectionChanged+=(s,e)=>{if(!_kbmSync){_kbmSync=true;try{RefreshKbmEditor();}finally{_kbmSync=false;}}};
   editor.Children.Add(_kbmGesture);
   _kbmAction=KbmChoice("OnexKbmAction",global::XboxGamingBar.OnexStrings.Get("Action"));
   void Action(string text,JsonObject binding)=>_kbmAction.Items.Add(new ComboBoxItem{Content=OnexStrings.Get(text),Tag=binding});
   void Key(string text,params int[] keys)=>Action(text,KbmBinding("keyboard","",keys));
   Action(global::XboxGamingBar.OnexStrings.Get("Disabled"),KbmBinding("disabled"));
   _kbmKeyboardType=new ComboBoxItem{Content=OnexStrings.Get("Keyboard"),Tag="keyboard"};
   _kbmMouseType=new ComboBoxItem{Content=OnexStrings.Get("Mouse"),Tag="mouse"};
   _kbmAction.Items.Add(_kbmKeyboardType);_kbmAction.Items.Add(_kbmMouseType);
   foreach(var item in new[]{new[]{"left","Left click"},new[]{"right","Right click"},new[]{"middle","Middle click"},new[]{"wheelUp","Wheel up · one step"},new[]{"wheelDown","Wheel down · one step"},new[]{"wheelLeft","Wheel left · one step"},new[]{"wheelRight","Wheel right · one step"}})Action(OnexStrings.Format("Mouse · {0}",OnexStrings.Get(item[1])),KbmBinding("mouse",item[0]));
   foreach(string direction in new[]{"up","down","left","right"}){Action(OnexStrings.Format("Cursor · {0}",OnexStrings.Get(direction)),KbmBinding("cursor",direction));Action(OnexStrings.Format("Nudge cursor · {0} · one step",OnexStrings.Get(direction)),KbmBinding("nudge",direction));Action(OnexStrings.Format("Scroll · {0} · while held",OnexStrings.Get(direction)),KbmBinding("scroll",direction));}
   foreach(var item in new[]{new[]{32,0},new[]{27,0},new[]{13,0},new[]{8,0},new[]{9,0},new[]{160,0},new[]{162,0},new[]{164,0},new[]{91,0},new[]{46,0},new[]{36,0},new[]{35,0},new[]{33,0},new[]{34,0},new[]{37,0},new[]{38,0},new[]{39,0},new[]{40,0},new[]{45,0},new[]{44,0}})Key(global::XboxGamingBar.OnexStrings.Format("Keyboard · {0}",OnexVirtualKeyDisplayName(item[0])),item[0]);
   Key(global::XboxGamingBar.OnexStrings.Get("Switch app · Alt+Tab"),18,9);Key(global::XboxGamingBar.OnexStrings.Get("Switch app backward · Alt+Shift+Tab"),18,16,9);Key(global::XboxGamingBar.OnexStrings.Get("Task view · Win+Tab"),91,9);Key(global::XboxGamingBar.OnexStrings.Get("Show desktop · Win+D"),91,68);Key(global::XboxGamingBar.OnexStrings.Get("Start menu · Win"),91);Key(global::XboxGamingBar.OnexStrings.Get("Explorer · Win+E"),91,69);Key(global::XboxGamingBar.OnexStrings.Get("Windows settings · Win+I"),91,73);Key(global::XboxGamingBar.OnexStrings.Get("Run · Win+R"),91,82);Key(global::XboxGamingBar.OnexStrings.Get("Search · Win+S"),91,83);Key(global::XboxGamingBar.OnexStrings.Get("Task Manager · Ctrl+Shift+Esc"),17,16,27);Key(global::XboxGamingBar.OnexStrings.Get("Close window · Alt+F4"),18,115);Key(global::XboxGamingBar.OnexStrings.Get("Lock Windows · Win+L"),91,76);Key(global::XboxGamingBar.OnexStrings.Get("Quick settings · Win+A"),91,65);Key(global::XboxGamingBar.OnexStrings.Get("Power user menu · Win+X"),91,88);Key(global::XboxGamingBar.OnexStrings.Get("Project display · Win+P"),91,80);Key(global::XboxGamingBar.OnexStrings.Get("Cast / connect · Win+K"),91,75);Key(global::XboxGamingBar.OnexStrings.Get("Window menu · Alt+Space"),18,32);Key(global::XboxGamingBar.OnexStrings.Get("Context menu · Shift+F10"),16,121);Key(global::XboxGamingBar.OnexStrings.Get("Context menu key"),93);Key(global::XboxGamingBar.OnexStrings.Get("Persistent app switcher · Ctrl+Alt+Tab"),17,18,9);Key(global::XboxGamingBar.OnexStrings.Get("New folder · Ctrl+Shift+N"),17,16,78);Key(global::XboxGamingBar.OnexStrings.Get("Properties / fullscreen · Alt+Enter"),18,13);Key(global::XboxGamingBar.OnexStrings.Get("Change language · Alt+Shift"),18,16);Key(global::XboxGamingBar.OnexStrings.Get("Xbox Game Bar · Win+G"),91,71);Key(global::XboxGamingBar.OnexStrings.Get("Notifications · Win+N"),91,78);Key(global::XboxGamingBar.OnexStrings.Get("Volume mixer · Win+Ctrl+V"),91,17,86);Key(global::XboxGamingBar.OnexStrings.Get("Screenshot · Win+Shift+S"),91,16,83);Key(global::XboxGamingBar.OnexStrings.Get("Clipboard history · Win+V"),91,86);Key(global::XboxGamingBar.OnexStrings.Get("Emoji picker · Win+."),91,190);Key(global::XboxGamingBar.OnexStrings.Get("Change language · Win+Space"),91,32);
   Key(global::XboxGamingBar.OnexStrings.Get("New desktop · Win+Ctrl+D"),91,17,68);Key(global::XboxGamingBar.OnexStrings.Get("Previous desktop · Win+Ctrl+Left"),91,17,37);Key(global::XboxGamingBar.OnexStrings.Get("Next desktop · Win+Ctrl+Right"),91,17,39);Key(global::XboxGamingBar.OnexStrings.Get("Close desktop · Win+Ctrl+F4"),91,17,115);Key(global::XboxGamingBar.OnexStrings.Get("Snap left · Win+Left"),91,37);Key(global::XboxGamingBar.OnexStrings.Get("Snap right · Win+Right"),91,39);Key(global::XboxGamingBar.OnexStrings.Get("Maximize · Win+Up"),91,38);Key(global::XboxGamingBar.OnexStrings.Get("Minimize · Win+Down"),91,40);
   foreach(var item in new[]{new[]{"Copy","67"},new[]{"Paste","86"},new[]{"Cut","88"},new[]{"Undo","90"},new[]{"Redo","89"},new[]{"Select all","65"},new[]{"Save","83"},new[]{"Find","70"},new[]{"New tab","84"},new[]{"Close tab","87"}})Key(OnexStrings.Get(item[0])+global::XboxGamingBar.OnexStrings.Get(" · Ctrl+")+(char)int.Parse(item[1]),17,int.Parse(item[1]));Key(global::XboxGamingBar.OnexStrings.Get("Reopen tab · Ctrl+Shift+T"),17,16,84);
   Key(global::XboxGamingBar.OnexStrings.Get("Volume up"),175);Key(global::XboxGamingBar.OnexStrings.Get("Volume down"),174);Key(global::XboxGamingBar.OnexStrings.Get("Mute sound"),173);Key(global::XboxGamingBar.OnexStrings.Get("Play / pause"),179);Key(global::XboxGamingBar.OnexStrings.Get("Next track"),176);Key(global::XboxGamingBar.OnexStrings.Get("Previous track"),177);Key(global::XboxGamingBar.OnexStrings.Get("Browser back"),166);Key(global::XboxGamingBar.OnexStrings.Get("Browser forward"),167);
   Action(global::XboxGamingBar.OnexStrings.Get("Open ONEXTweaks"),KbmBinding("widget"));Action(global::XboxGamingBar.OnexStrings.Get("Windows touch keyboard"),KbmBinding("keyboardPopup"));Action(global::XboxGamingBar.OnexStrings.Get("Windows Security · Ctrl+Alt+Delete"),KbmBinding("security"));_kbmAction.Items.Add(new ComboBoxItem{Content=global::XboxGamingBar.OnexStrings.Get("Custom keys / keyboard + mouse…"),Tag=null});editor.Children.Add(_kbmAction);
   _kbmCustom=new StackPanel{Spacing=10};_kbmCustom.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Выбери модификаторы и обычные клавиши — всего не более пяти. Можно добавить кнопку мыши или шаг колеса.")));var modifiers=new Grid{ColumnSpacing=10,RowSpacing=4};modifiers.ColumnDefinitions.Add(new ColumnDefinition());modifiers.ColumnDefinitions.Add(new ColumnDefinition());modifiers.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});modifiers.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});foreach(var item in new[]{new[]{"Ctrl","17"},new[]{"Alt","18"},new[]{"Shift","16"},new[]{"Win","91"}}){var check=new CheckBox{Content=item[0],Tag=int.Parse(item[1])};int index=_kbmModifiers.Count;_kbmModifiers.Add(check);Grid.SetColumn(check,index%2);Grid.SetRow(check,index/2);modifiers.Children.Add(check);}_kbmCustom.Children.Add(modifiers);
   ComboBox Keys(string name,string title){var box=KbmChoice(name,OnexStrings.Get(title));PopulateOnexKeyboardChoices(box);return box;}
   _kbmKey1=Keys("OnexKbmKey1","Key");_kbmKey2=Keys("OnexKbmKey2","Additional key");_kbmCustom.Children.Add(OnexSettingsColumns(_kbmKey1,_kbmKey2));_kbmKeys.AddRange(new[]{_kbmKey1,_kbmKey2});for(int n=3;n<=5;n++){var extra=Keys("OnexKbmKey"+n,OnexStrings.Format("Additional key {0}",n));_kbmKeys.Add(extra);_kbmCustom.Children.Add(extra);}_kbmMouse=KbmChoice("OnexKbmCustomMouse",global::XboxGamingBar.OnexStrings.Get("Mouse action · optional"));_kbmMouse.Items.Add(new ComboBoxItem{Content=global::XboxGamingBar.OnexStrings.Get("None"),Tag=""});foreach(string target in AssignmentMouseTargets)_kbmMouse.Items.Add(new ComboBoxItem{Content=KbmMouseCaption(target),Tag=target});_kbmMouse.SelectedIndex=0;_kbmCustom.Children.Add(_kbmMouse);_kbmApplyCustom=KbmButton("OnexKbmApplyCustom",global::XboxGamingBar.OnexStrings.Get("Apply action"));_kbmApplyCustom.Click+=async(s,e)=>{var keys=_kbmModifiers.Where(check=>check.IsChecked==true).Select(check=>(int)check.Tag).Concat(_kbmKeys.Select(SelectedOnexKeyboardChoice)).Where(key=>key!=0).Distinct().ToArray();string mouse=(string)((ComboBoxItem)_kbmMouse.SelectedItem).Tag;if(keys.Length>5){_kbmHelp.Text=global::XboxGamingBar.OnexStrings.Get("Выбери не более пяти клавиш.");return;}await KbmSaveBinding(KbmBinding(keys.Length==0?(mouse==""?"disabled":"mouse"):(mouse==""?"keyboard":"combo"),mouse,keys));};_kbmCustom.Children.Add(_kbmApplyCustom);editor.Children.Add(_kbmCustom);
   _kbmHelp=ControllerHelp("");editor.Children.Add(_kbmHelp);_kbmBindingReset=KbmButton("OnexKbmBindingReset",global::XboxGamingBar.OnexStrings.Get("Restore Default"));_kbmBindingReset.Click+=async(s,e)=>await KbmChange(new JsonObject{{"operation",JsonValue.CreateStringValue("resetBinding")},{"source",JsonValue.CreateStringValue(_kbmSource)},{"layer",JsonValue.CreateStringValue(_kbmLayer)},{"double",JsonValue.CreateBooleanValue(KbmDouble)}});editor.Children.Add(_kbmBindingReset);
   _kbmAction.SelectionChanged+=async(s,e)=>await KbmActionSelected();
   _kbmAction.DropDownClosed+=async(s,e)=>await KbmActionSelected();
   _kbmFlyout=CreateOnexRemappingWindow(editor,OnexPalette.Cyan,()=> (_kbmAction.IsEnabled?(Control)_kbmAction:_kbmBindingReset).Focus(FocusState.Keyboard),()=>{_kbmKeyboardContext=null;_onexOpenMenus.Remove(_kbmFlyout);});editor.PreviewKeyDown+=(s,e)=>HandleOnexKeyboardMouseKey(e);
   _kbmFlyout.Opened+=(s,e)=>PaintKbmEditorLayers(ControllerText(KbmPolicy,"fnButton")==_kbmSource);
  }
  private async Task KbmSaveBinding(JsonObject binding)=>await KbmChange(new JsonObject{{"operation",JsonValue.CreateStringValue("bind")},{"source",JsonValue.CreateStringValue(_kbmSource)},{"layer",JsonValue.CreateStringValue(_kbmLayer)},{"double",JsonValue.CreateBooleanValue(KbmDouble)},{"binding",binding}});
  private void RefreshKbmEditor()
  {
   if(_kbmFlyout==null)return;
   bool fn=ControllerText(KbmPolicy,"fnButton")==_kbmSource;
   if(fn)_kbmLayer="base";
   _kbmEditorTitle.Text=OnexControllerSourceName(_kbmSource);
   _kbmGesture.Visibility=_kbmSource=="Start"?Visibility.Visible:Visibility.Collapsed;
   var binding=KbmEffective(_kbmSource,_kbmLayer,KbmDouble);
   string currentKind=ControllerText(binding,"kind");
   _kbmAction.SelectedItem=currentKind=="keyboard"||currentKind=="keySequence"?_kbmKeyboardType:currentKind=="mouse"?_kbmMouseType:
    (_kbmAction.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>item.Tag is JsonObject value&&ControllerText(value,"kind")==currentKind&&ControllerText(value,"target")==ControllerText(binding,"target"))??_kbmAction.Items[_kbmAction.Items.Count-1]);
   _kbmAction.IsEnabled=!fn;_kbmApplyCustom.IsEnabled=!fn;
   foreach(var item in _kbmAction.Items.OfType<ComboBoxItem>())if(item.Tag is JsonObject value&&new[]{"widget","keyboardPopup","security"}.Contains(ControllerText(value,"kind")))item.IsEnabled=_kbmSource.StartsWith("OEM:",StringComparison.Ordinal)&&(ControllerText(value,"kind")!="security"||_kbmState.GetNamedBoolean("securityAvailable",false));
   var keys=KbmKeys(binding)?.Select(value=>(int)value.GetNumber()).ToArray()??new int[0];foreach(var check in _kbmModifiers)check.IsChecked=keys.Contains((int)check.Tag);var ordinary=keys.Where(key=>!new[]{17,18,16,91}.Contains(key)).ToArray();for(int n=0;n<_kbmKeys.Count;n++)SelectOnexKeyboardChoice(_kbmKeys[n],ordinary.Skip(n).FirstOrDefault());_kbmMouse.SelectedItem=_kbmMouse.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>(string)item.Tag==ControllerText(binding,"target"))??_kbmMouse.Items[0];_kbmCustom.Visibility=(_kbmAction.SelectedItem as ComboBoxItem)?.Tag==null?Visibility.Visible:Visibility.Collapsed;
   UpdateKbmAssignmentArt(binding);
   PaintKbmEditorLayers(fn);
   _kbmHelp.Text=fn?global::XboxGamingBar.OnexStrings.Get("Эта кнопка удерживает FN. Restore Default снимет FN и вернёт её обычное действие."):_kbmSource=="Start"?global::XboxGamingBar.OnexStrings.Get("Одиночное действие ждёт 300 мс, чтобы не сработать вместе с двойным. По умолчанию: Win и Win+Tab."):_kbmSource=="OEM:KeyboardMouse"?global::XboxGamingBar.OnexStrings.Get("Двойное нажатие зарезервировано для переключения Gamepad ↔ Keyboard & mouse. Здесь меняется только одиночное."):ControllerText(binding,"kind")=="nudge"?global::XboxGamingBar.OnexStrings.Format("Одно нажатие сдвигает курсор на {0} пикселей. Удержание не повторяет шаг; размер меняется в Mouse precision.",KbmPolicy.GetNamedNumber("nudgeStep",10)):ControllerText(binding,"kind")=="security"?global::XboxGamingBar.OnexStrings.Get("Ctrl+Alt+Delete использует защищённый механизм Windows. Обычная имитация клавиш не заменяет его; доступность зависит от системной политики."):global::XboxGamingBar.OnexStrings.Get("Назначение сохраняется автоматически в выбранном слое. Комбинации клавиш и кнопка мыши удерживаются до отпускания; шаг колеса выполняется один раз.");
  }
  private void OpenKbmEditor(string source,string layer,Button anchor,bool show)
  {
   if(!show&&IsOnexRemapOpen(_kbmFlyout))return;
   if(KbmPolicy==null)return;_kbmSource=source;_kbmLayer=layer;RefreshKbmMapping();if(!show)return;
   // Navigation/telemetry only need the mapping model. Construct the complete
   // action/key editor once, when it is actually requested, using fresh state.
   bool priorSync=_kbmSync;_kbmSync=true;
   try{BuildKbmEditor();_kbmGesture.SelectedIndex=0;RefreshKbmEditor();}finally{_kbmSync=priorSync;}

   PrepareFullOnexRemap(_kbmFlyout);
   _kbmFlyout.XamlRoot=anchor.XamlRoot;_onexOpenMenus.Add(_kbmFlyout);_kbmFlyout.ShowAt(anchor);
  }
  private bool HandleOnexKeyboardMouseKey(KeyRoutedEventArgs args)
  {
   if(args.Handled||_onexKeyboardMouseNav?.IsChecked!=true)return false;if(OnexInputGuard.HandleFocusedSlider(args))return true;
   if(IsOnexRemapOpen(_kbmFlyout))
   {
    if(_kbmKeyboardArt?.HasOpenDropdown==true||_kbmDiagramMouseAction?.IsDropDownOpen==true||
       new[]{_kbmAction,_kbmGesture,_kbmMouse}.Concat(_kbmKeys).Any(box=>box?.IsDropDownOpen==true))return false;
    var key=OnexInputGuard.SliderKey(args);
    if(key==VirtualKey.Escape)
    {
     args.Handled=true;
     CloseOnexRemapEditor(_kbmFlyout);
     return true;
    }
    var focused=FocusManager.GetFocusedElement() as Control;
    if((key==VirtualKey.Left||key==VirtualKey.Right)&&(focused==_kbmMainLayerButton||focused==_kbmFnLayerButton))
    {
     var choice=key==VirtualKey.Left?_kbmMainLayerButton:_kbmFnLayerButton;
     if(choice.IsEnabled)choice.Focus(FocusState.Keyboard);
     args.Handled=true;
     return true;
    }
    if(!IsOnexProfileVertical(key))return false;
    var layerButton=_kbmLayer=="fn"?_kbmFnLayerButton:_kbmMainLayerButton;
    var controls=new System.Collections.Generic.List<Control>{layerButton};
    if(_kbmGesture.Visibility==Visibility.Visible)controls.Add(_kbmGesture);
    if(_kbmAction.IsEnabled)controls.Add(_kbmAction);
    if(_kbmCustom.Visibility==Visibility.Visible)
    {
     controls.AddRange(_kbmModifiers);
     controls.AddRange(_kbmKeys);
     controls.AddRange(new Control[]{_kbmMouse,_kbmApplyCustom});
    }
    if(_kbmKeyboardArt!=null)controls.AddRange(_kbmKeyboardArt.Controls.Where(control=>control.IsEnabled));
    if(_kbmDiagramMouseAction?.Visibility==Visibility.Visible)
    {
     controls.Add(_kbmDiagramMouseAction);
     controls.AddRange(_kbmMouseArt.Controls.Where(control=>control.IsEnabled));
    }
    controls.Add(_kbmBindingReset);
    if(focused==_kbmMainLayerButton||focused==_kbmFnLayerButton)focused=layerButton;
    int index=controls.IndexOf(focused);
    if(index<0)return false;
    int next=Math.Max(0,Math.Min(controls.Count-1,index+(IsOnexProfileDown(key)?1:-1)));
    args.Handled=true;
    controls[next].Focus(FocusState.Keyboard);
    controls[next].StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});
    return true;
   }
   if(_kbmFn.IsDropDownOpen)return false;
   if(_kbmOpenSection!=null&&(args.Key==VirtualKey.GamepadB||args.Key==VirtualKey.Escape))
   {
    var header=_kbmOpenSection.Header;
    args.Handled=true;
    SetKbmSettingsCard(null);
    header.Focus(FocusState.Keyboard);
    header.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});
    return true;
   }
   int direction=OnexNavigationDirection(args.Key);if(direction==0)return false;var rows=KbmRows();var current=FocusManager.GetFocusedElement() as Control;for(int r=0;r<rows.Count;r++){int c=Array.IndexOf(rows[r],current);if(c<0)continue;int next=Math.Abs(direction)==2?Math.Max(0,Math.Min(rows.Count-1,r+(direction>0?1:-1))):r;int column=Math.Abs(direction)==1?Math.Max(0,Math.Min(rows[next].Length-1,c+direction)):Math.Min(c,rows[next].Length-1);var target=rows[next][column];args.Handled=true;target.Focus(FocusState.Keyboard);target.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});return true;}return false;
  }
 }
}
