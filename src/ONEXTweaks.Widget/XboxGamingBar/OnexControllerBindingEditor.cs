using System;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Core;
using Windows.Data.Json;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private bool _onexControllerBindingEditorBuilding;
  private AssignmentDiagram _controllerPadArt,_controllerMouseArt;
  private KeyboardAssignmentPicker _controllerKeyboardArt;
  private JsonObject _controllerKeyboardBinding;
  private string _controllerKeyboardContext;
  private string _controllerKeyboardDraftContext;
  private JsonObject _controllerKeyboardSubmittedBinding;

  private string ControllerKeyboardContext=>ControllerEditorId+"|"+ControllerSourceId+"|"+ControllerEditingDouble;

  private void ClearControllerKeyboardDraft()
  {
   _controllerKeyboardDraftContext=null;
   _controllerKeyboardSubmittedBinding=null;
  }

  private bool KeepControllerKeyboardDraft(JsonObject saved, bool reserved)
  {
   if(_controllerKeyboardDraftContext==null)return false;
   if(_controllerKeyboardDraftContext==ControllerKeyboardContext&&OnexControllerEditingVirtual&&!reserved)
   {
    var submitted=_controllerKeyboardSubmittedBinding;
    if(submitted==null||ControllerText(saved,"kind")!=ControllerText(submitted,"kind")||
     !ReadAssignmentNumbers(saved,"keys").SequenceEqual(ReadAssignmentNumbers(submitted,"keys"))||
     !ReadAssignmentNumbers(saved,"delays").SequenceEqual(ReadAssignmentNumbers(submitted,"delays")))return true;
   }
   ClearControllerKeyboardDraft();
   return false;
  }

  private void ChooseOnexControllerAction()
  {
   ClearControllerKeyboardDraft();
   if(_onexControllerKind.SelectedIndex==3)
   {
    _controllerKeyboardDraftContext=ControllerKeyboardContext;
    _controllerKeyboardBinding=null;
    _controllerKeyboardContext=null;
   }
   UpdateOnexControllerMappingVisibility();
   if(_onexControllerKind.SelectedIndex!=3)QueueOnexControllerBinding();
  }

  private void SubmitOnexControllerKeyboardBinding(JsonObject binding)
  {
   _controllerKeyboardDraftContext=ControllerKeyboardContext;
   _controllerKeyboardSubmittedBinding=binding;
   _controllerKeyboardBinding=binding;
   QueueOnexControllerAssignment(binding);
  }

  private void RefreshControllerAssignmentArt(int kind)
  {
   if(kind==2)RefreshOnexControllerDs4Actions();
   var padArt=ControllerPadAssignmentArt;
   bool showPad=kind==2&&padArt!=null;
   if(showPad&&_controllerPadArt?.Art!=padArt)
   {
    if(_controllerPadArt!=null)_onexControllerMappingEditor.Children.Remove(_controllerPadArt.View);
    _controllerPadArt=new AssignmentDiagram(padArt,AssignmentPadLabel,id=>
    {
     var choice=_onexControllerPadTarget.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>(string)item.Tag==id&&item.IsEnabled);
     if(choice==null||!_onexControllerPadTarget.IsEnabled)return;
     if(OnexControllerEditingSequence){AppendOnexControllerMacroStep(id);return;}
     _onexControllerSync=true;try{_onexControllerPadTarget.SelectedItem=choice;}finally{_onexControllerSync=false;}
     QueueOnexControllerBinding();RefreshControllerAssignmentArt(2);
    });
    _onexControllerMappingEditor.Children.Insert(_onexControllerMappingEditor.Children.IndexOf(_onexControllerPadTarget)+1,_controllerPadArt.View);
   }
   if(kind==4&&_controllerMouseArt==null)
   {
    _controllerMouseArt=new AssignmentDiagram(MouseAssignmentArt,KbmMouseCaption,id=>
    {
     if(!_onexControllerMouse.IsEnabled)return;
     _onexControllerSync=true;try{_onexControllerMouse.SelectedIndex=Array.IndexOf(AssignmentMouseTargets,id);}finally{_onexControllerSync=false;}
     QueueOnexControllerBinding();RefreshControllerAssignmentArt(4);
    },205);
    _onexControllerMappingEditor.Children.Insert(_onexControllerMappingEditor.Children.IndexOf(_onexControllerMouse)+1,_controllerMouseArt.View);
   }
   if(kind==3&&_controllerKeyboardArt==null)
   {
    _controllerKeyboardArt=new KeyboardAssignmentPicker(this,binding=>
    {
     if(_onexControllerKeyboard.IsEnabled)
     {
      SubmitOnexControllerKeyboardBinding(binding);
     }
     return System.Threading.Tasks.Task.CompletedTask;
    });
    _onexControllerMappingEditor.Children.Insert(_onexControllerMappingEditor.Children.IndexOf(_onexControllerRecord)+1,_controllerKeyboardArt.View);
   }
   if(_controllerPadArt!=null)
   {
    _controllerPadArt.View.Visibility=showPad?Visibility.Visible:Visibility.Collapsed;
    foreach(var item in _onexControllerPadTarget.Items.OfType<ComboBoxItem>())
    {
     string id=(string)item.Tag;
     _controllerPadArt.SetAvailable(id,item.IsEnabled&&_onexControllerPadTarget.IsEnabled,null);
    }
    PaintOnexControllerPadSelection();
   }
   if(_controllerMouseArt!=null)
   {
    _controllerMouseArt.View.Visibility=kind==4?Visibility.Visible:Visibility.Collapsed;
    foreach(string id in AssignmentMouseTargets)_controllerMouseArt.SetAvailable(id,_onexControllerMouse.IsEnabled,null);
    _controllerMouseArt.Select(AssignmentMouseTargets.ElementAtOrDefault(_onexControllerMouse.SelectedIndex));
   }
   if(_controllerKeyboardArt!=null)
   {
    _controllerKeyboardArt.View.Visibility=kind==3?Visibility.Visible:Visibility.Collapsed;
    if(kind==3)
    {
     string context=ControllerKeyboardContext;
     _controllerKeyboardArt.Load(_controllerKeyboardBinding,context!=_controllerKeyboardContext);
     _controllerKeyboardContext=context;
     _controllerKeyboardArt.SetEnabled(_onexControllerKeyboard.IsEnabled);
    }
   }
  }
  private string _onexControllerLastEditorSource;private int _onexControllerLastEditorGesture;
  private Control[] _onexControllerBindingEditorControls=Array.Empty<Control>();
  private void EnsureOnexControllerBindingEditor(Func<JsonObject,System.Threading.Tasks.Task> restoreBinding=null)
  {
   if(_onexControllerMappingFlyout!=null||_onexControllerBindingEditorBuilding)return;
   bool previousSync=_onexControllerSync;_onexControllerSync=true;_onexControllerBindingEditorBuilding=true;
   int controlsBefore=_onexControllerControls.Count;var editor=new StackPanel{Spacing=12};_onexControllerMappingEditor=editor;
   try
   {
   _onexControllerKind=ControllerChoice(global::XboxGamingBar.OnexStrings.Get("Action"),"OnexControllerAction",new[]{"Default / original","Disabled","Gamepad","Keyboard","Mouse","Open ONEXTweaks","Windows keyboard","Steam Big Picture / Menu","Steam Quick Access","Switch input mode · global"});editor.Children.Add(_onexControllerKind);
   _onexControllerPadTarget=ControllerChoice(global::XboxGamingBar.OnexStrings.Get("Virtual control"),"OnexControllerPadTarget",ControllerInputs.Concat(new[]{"Guide"}).Concat(Ds4TouchpadTargets).ToArray());
   editor.Children.Add(_onexControllerPadTarget);
   _onexControllerKeyboard=ControllerChoice(global::XboxGamingBar.OnexStrings.Get("Keyboard shortcut"),"OnexControllerKeyboard",new string[0]);
   void Chord(string name,params int[] keys){_onexControllerKeyboard.Items.Add(new ComboBoxItem{Content=name,Tag=keys});}
   Chord("Space",32);Chord("Enter",13);Chord("Escape",27);Chord("Tab",9);Chord("Shift",16);Chord("Ctrl",17);Chord("Alt",18);Chord("Alt + Enter",18,13);Chord("Shift + Tab",16,9);Chord("Ctrl + Shift + Esc",17,16,27);Chord("Ctrl + 1",17,49);Chord("Ctrl + 2",17,50);for(int n=1;n<=12;n++)Chord("F"+n,111+n);for(int n=65;n<=90;n++)Chord(((char)n).ToString(),n);for(int n=48;n<=57;n++)Chord(((char)n).ToString(),n);
   editor.Children.Add(_onexControllerKeyboard);_onexControllerRecord=new Button{Name="OnexControllerRecordKeys",Content=global::XboxGamingBar.OnexStrings.Get("Record keyboard shortcut"),Style=(Style)Resources["StandaloneButtonStyle"]};editor.Children.Add(_onexControllerRecord);_onexControllerRecord.Click+=(s,e)=>{_onexControllerRecording=!_onexControllerRecording;_onexControllerRecordModifiers.Clear();_onexControllerRecord.Content=_onexControllerRecording?global::XboxGamingBar.OnexStrings.Get("Press keyboard keys · B / Esc cancels"):global::XboxGamingBar.OnexStrings.Get("Record keyboard shortcut");_onexControllerRecord.Focus(FocusState.Keyboard);};
   _onexControllerMouse=ControllerChoice(global::XboxGamingBar.OnexStrings.Get("Mouse action"),"OnexControllerMouse",AssignmentMouseLabels);editor.Children.Add(_onexControllerMouse);_onexControllerMappingHint=ControllerHelp("");editor.Children.Add(_onexControllerMappingHint);
   _onexControllerKind.SelectionChanged+=(s,e)=>{if(!_onexControllerSync&&OnexInputGuard.IsActive(_onexControllerKind))ChooseOnexControllerAction();};
   foreach(var control in new[]{_onexControllerPadTarget,_onexControllerKeyboard,_onexControllerMouse})control.SelectionChanged+=(s,e)=>{if(!_onexControllerSync&&OnexInputGuard.IsActive((Control)s))
   {
    QueueOnexControllerBinding();
    if(ReferenceEquals(s,_onexControllerKeyboard))_controllerKeyboardArt?.Load(_controllerKeyboardBinding,true);
    RefreshControllerAssignmentArt(_onexControllerKind.SelectedIndex);
   }};
   editor.RequestedTheme=ElementTheme.Dark;
   foreach(var choice in new[]{_onexControllerKind,_onexControllerPadTarget,_onexControllerKeyboard,_onexControllerMouse}){choice.RequestedTheme=ElementTheme.Dark;choice.Background=OnexSettingsBrush(39,47,60);choice.Foreground=OnexSettingsBrush(238,241,246);choice.ItemTemplate=null;}_onexControllerMappingTitle=new TextBlock{FontSize=18,FontWeight=Windows.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};editor.Children.Insert(0,_onexControllerMappingTitle);
   var popup=CreateOnexRemappingWindow(editor,OnexPalette.Orange,
    ()=>_onexControllerKind?.Focus(FocusState.Keyboard),OnexControllerBindingEditorClosed,_onexControllerMappingTitle);
   _onexControllerMappingFlyout=popup;
   _onexControllerKind.DropDownClosed+=(s,e)=>MatchOnexControllerSketchEditorToAction();
   editor.PreviewKeyDown+=HandleOnexControllerMappingEditorKey;
   editor.PreviewKeyUp+=(s,e)=>{int key=(int)e.Key;if(key==_onexControllerOwnedKey){e.Handled=true;_onexControllerOwnedKey=-1;}if(_onexControllerRecording&&_onexControllerRecordModifiers.Remove(key))e.Handled=true;};
   _onexControllerBindingReset=new Button
   {
    Name="OnexControllerBindingRestore",Content=OnexStrings.Get("Restore this button"),
    Style=(Style)Resources["StandaloneButtonStyle"],HorizontalAlignment=HorizontalAlignment.Stretch,MinHeight=40
   };
   _onexControllerBindingReset.Click+=async(s,e)=>
   {
    if(!IsOnexRemapOpen(popup)||!ReferenceEquals(popup,_onexControllerMappingFlyout))return;
    string source=_onexRemapWindows.FirstOrDefault(view=>view.Flyout==popup)?.Anchor?.Tag as string;
    if(source==null||source!=ControllerSourceId)return;
    ClearControllerKeyboardDraft();
    var request=new JsonObject
    {
     {"operation",JsonValue.CreateStringValue("resetBinding")},
     {"id",JsonValue.CreateStringValue(ControllerEditorId)},
     {"source",JsonValue.CreateStringValue(source)}
    };
    if(restoreBinding==null)await ControllerActionAsync(request);
    else await restoreBinding(request);
   };
   _onexControllerControls.Add(_onexControllerBindingReset);
   _onexControllerMappingEditor.Children.Add(_onexControllerBindingReset);
   CreateOnexControllerGestureEditor(editor);CreateOnexControllerMacroEditor(editor);
   foreach(var item in _onexControllerPadTarget.Items.OfType<ComboBoxItem>())item.Content=AssignmentPadLabel((string)item.Tag);
   }
   catch
   {
    _onexControllerBindingEditorControls=_onexControllerControls.Skip(controlsBefore).ToArray();
    ReleaseOnexControllerBindingEditor();throw;
   }
   finally{_onexControllerSync=previousSync;_onexControllerBindingEditorBuilding=false;}
   _onexControllerBindingEditorControls=_onexControllerControls.Skip(controlsBefore).ToArray();
   bool selectionSync=_onexControllerSync;_onexControllerSync=true;try{_onexControllerGestureSource=ControllerSourceId;_onexControllerGesture.SelectedIndex=_onexControllerLastEditorSource==ControllerSourceId?_onexControllerLastEditorGesture:0;}finally{_onexControllerSync=selectionSync;}
   RefreshOnexControllerMapping();UpdateOnexControllerEnabled();
  }
  private void OnexControllerBindingEditorClosed()
  {
   if(_onexControllerMacroInterval!=null)OnexInputGuard.Cancel(_onexControllerMacroInterval);
   if(_onexControllerMacroPress!=null)OnexInputGuard.Cancel(_onexControllerMacroPress);
   _onexControllerRecording=false;
   _onexControllerRecordModifiers.Clear();
   ReleaseOnexControllerBindingEditor();
  }
  private void ReleaseOnexControllerBindingEditor()
  {
   bool previousSync=_onexControllerSync;_onexControllerSync=true;try{
   if(_onexControllerGesture!=null){_onexControllerLastEditorSource=ControllerSourceId;_onexControllerLastEditorGesture=_onexControllerGesture.SelectedIndex;}
   var editor=_onexControllerMappingEditor;var popup=_onexControllerMappingFlyout;
   if(popup!=null)_onexOpenMenus.Remove(popup);
   _onexRemapWindows.RemoveAll(view=>ReferenceEquals(view.Editor,editor));
   foreach(var control in _onexControllerBindingEditorControls)_onexControllerControls.Remove(control);
   _onexControllerBindingEditorControls=Array.Empty<Control>();editor?.Children.Clear();
   _onexControllerMappingFlyout=null;_onexControllerMappingEditor=null;_onexControllerMappingTitle=null;
   _onexControllerKind=null;_onexControllerPadTarget=null;_onexControllerKeyboard=null;_onexControllerMouse=null;
   _controllerPadArt=null;_controllerMouseArt=null;_controllerKeyboardArt=null;_controllerKeyboardBinding=null;_controllerKeyboardContext=null;
   ClearControllerKeyboardDraft();
   _onexControllerRecord=null;_onexControllerBindingReset=null;_onexControllerMappingHint=null;
   _onexControllerGesture=null;_onexControllerGestureSource=null;_onexControllerDoubleEnabled=null;_onexControllerDoubleWindow=null;
   _onexControllerDoubleValue=null;_onexControllerDetection=null;_onexControllerDoubleCaption=null;
   _onexControllerMacroSource=null;_onexControllerMacroNextStep=null;
   _onexControllerGamepadMode=null;_onexControllerSequencePreview=null;
   _onexControllerMacroPanel=null;_onexControllerMacroMode=null;_onexControllerMacroStep=null;_onexControllerMacroTarget=null;
   _onexControllerMacroAdd=null;_onexControllerMacroRemove=null;_onexControllerMacroUp=null;_onexControllerMacroDown=null;
   _onexControllerMacroInterval=null;_onexControllerMacroPress=null;_onexControllerMacroIntervalLabel=null;_onexControllerMacroPressLabel=null;_onexControllerMacroHelp=null;
   }finally{_onexControllerSync=previousSync;}
  }

 }
}
