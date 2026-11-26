// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private StackPanel _onexControllerMacroPanel;private ComboBox _onexControllerGamepadMode,_onexControllerMacroMode,_onexControllerMacroStep,_onexControllerMacroTarget;
  private TextBlock _onexControllerSequencePreview;
  private int _onexControllerPaintedGamepadMode;
  private Slider _onexControllerMacroInterval,_onexControllerMacroPress;private TextBlock _onexControllerMacroIntervalLabel,_onexControllerMacroPressLabel,_onexControllerMacroHelp;
  private Button _onexControllerMacroAdd,_onexControllerMacroRemove,_onexControllerMacroUp,_onexControllerMacroDown;
  private readonly List<string> _onexControllerMacroSteps=new List<string>{"A"};private string _onexControllerMacroSource;private int? _onexControllerMacroNextStep;
  private static readonly string[] OnexMacroModes={"once","repeatHeld","holdToggle","repeatToggle"};
  private bool OnexControllerEditingSequence=>_onexControllerKind?.SelectedIndex==2&&_onexControllerGamepadMode?.SelectedIndex==1;
  private void CreateOnexControllerMacroEditor(StackPanel editor)
  {
   _onexControllerGamepadMode=ControllerChoice(OnexStrings.Get("Gamepad mode"),"OnexControllerGamepadMode",new[]{"Single button","Sequence"});
   _onexControllerGamepadMode.Visibility=Visibility.Collapsed;
   _onexControllerGamepadMode.SelectedIndex=0;
   editor.Children.Insert(editor.Children.IndexOf(_onexControllerPadTarget),_onexControllerGamepadMode);
   void ChooseMode()
   {
    if(_onexControllerSync||!OnexControllerEditingVirtual||_onexControllerGamepadMode.IsDropDownOpen||
       !OnexInputGuard.IsActive(_onexControllerGamepadMode)||_onexControllerPaintedGamepadMode==_onexControllerGamepadMode.SelectedIndex)return;
    _onexControllerPaintedGamepadMode=_onexControllerGamepadMode.SelectedIndex;
    if(OnexControllerEditingSequence)
    {
     _onexControllerMacroSteps.Clear();
     _onexControllerMacroSteps.Add((_onexControllerPadTarget.SelectedItem as ComboBoxItem)?.Tag as string??"A");
     _onexControllerMacroNextStep=0;
     RefreshOnexControllerMacroSteps();
    }
    else
    {
     var first=_onexControllerPadTarget.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>item.IsEnabled&&
      (string)item.Tag==_onexControllerMacroSteps.FirstOrDefault());
     if(first!=null)
     {
      _onexControllerSync=true;
      try{_onexControllerPadTarget.SelectedItem=first;}finally{_onexControllerSync=false;}
     }
    }
    UpdateOnexControllerMappingVisibility();
    QueueOnexControllerBinding();
   }
   _onexControllerGamepadMode.SelectionChanged+=(s,e)=>ChooseMode();
   _onexControllerGamepadMode.DropDownClosed+=(s,e)=>ChooseMode();
   _onexControllerMacroPanel=new StackPanel{Spacing=10,Visibility=Visibility.Collapsed};editor.Children.Insert(editor.Children.IndexOf(_onexControllerMappingHint),_onexControllerMacroPanel);
   _onexControllerSequencePreview=ControllerHelp("");
   _onexControllerMacroPanel.Children.Add(_onexControllerSequencePreview);
   _onexControllerMacroMode=ControllerChoice(OnexStrings.Get("Activation"),"OnexControllerMacroMode",new[]{"Once","Repeat while held","Hold (press again to release)","Hold + Repeat (press again to stop)"});
   _onexControllerMacroStep=ControllerChoice(global::XboxGamingBar.OnexStrings.Get("Sequence · choose a step"),"OnexControllerMacroStep",new string[0]);
   _onexControllerMacroTarget=ControllerChoice(global::XboxGamingBar.OnexStrings.Get("Gamepad button for this step"),"OnexControllerMacroTarget",ControllerInputs.Concat(new[]{"Guide"}).Concat(Ds4TouchpadTargets).ToArray());
   foreach(var choice in new[]{_onexControllerMacroMode,_onexControllerMacroStep,_onexControllerMacroTarget}){choice.RequestedTheme=ElementTheme.Dark;choice.Background=OnexSettingsBrush(39,47,60);choice.Foreground=OnexSettingsBrush(238,241,246);choice.ItemTemplate=null;_onexControllerMacroPanel.Children.Add(choice);}
   foreach(var item in _onexControllerMacroTarget.Items.OfType<ComboBoxItem>())item.Content=OnexControllerSourceName((string)item.Tag);
   Button Action(string name,string caption,Action edit){var button=new Button{Name=name,Content=OnexStrings.Get(caption),Style=(Style)Resources["KeyPickerButtonStyle"],HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Center,MinWidth=0};button.Click+=(s,e)=>{if(!OnexControllerEditingVirtual)return;edit();RefreshOnexControllerMacroSteps();QueueOnexControllerBinding();};ApplyOnexTileWindowControlAccent(button);_onexControllerControls.Add(button);return button;}
   _onexControllerMacroAdd=Action("OnexControllerMacroAdd",OnexStrings.Get("Add step"),()=>{if(_onexControllerMacroSteps.Count>=32)return;int at=Math.Max(0,_onexControllerMacroStep.SelectedIndex)+1;_onexControllerMacroSteps.Insert(at,(_onexControllerMacroTarget.SelectedItem as ComboBoxItem)?.Tag as string??"A");_onexControllerMacroNextStep=at;});
   _onexControllerMacroRemove=Action("OnexControllerMacroRemove",OnexStrings.Get("Remove step"),()=>{if(_onexControllerMacroSteps.Count>1)_onexControllerMacroSteps.RemoveAt(Math.Max(0,_onexControllerMacroStep.SelectedIndex));});
   void Move(int delta){int at=_onexControllerMacroStep.SelectedIndex,next=at+delta;if(at<0||next<0||next>=_onexControllerMacroSteps.Count)return;string value=_onexControllerMacroSteps[at];_onexControllerMacroSteps[at]=_onexControllerMacroSteps[next];_onexControllerMacroSteps[next]=value;_onexControllerMacroNextStep=next;}
   _onexControllerMacroUp=Action("OnexControllerMacroUp",OnexStrings.Get("Move earlier"),()=>Move(-1));_onexControllerMacroDown=Action("OnexControllerMacroDown",OnexStrings.Get("Move later"),()=>Move(1));
   _onexControllerMacroPanel.Children.Add(OnexSettingsColumns(_onexControllerMacroAdd,_onexControllerMacroRemove));_onexControllerMacroPanel.Children.Add(OnexSettingsColumns(_onexControllerMacroUp,_onexControllerMacroDown));
   Slider Time(string name,out TextBlock label,string caption,int initial){var text=label=ControllerHelp(caption+" · "+initial+global::XboxGamingBar.OnexStrings.Get(" ms"));_onexControllerMacroPanel.Children.Add(text);var slider=new Slider{Name=name,Minimum=10,Maximum=500,StepFrequency=10,SmallChange=10,Value=initial,Style=(Style)Resources["OnexPreciseSliderStyle"]};Action commit=()=>{if(!_onexControllerSync&&OnexControllerEditingVirtual)QueueOnexControllerBinding();};OnexInputGuard.Wire(slider,commit);slider.ValueChanged+=(s,e)=>{text.Text=caption+" · "+Math.Round(e.NewValue)+global::XboxGamingBar.OnexStrings.Get(" ms");if(!_onexControllerSync&&OnexInputGuard.IsActive(slider))commit();};_onexControllerMacroPanel.Children.Add(slider);_onexControllerControls.Add(slider);return slider;}
   _onexControllerMacroInterval=Time("OnexControllerMacroInterval",out _onexControllerMacroIntervalLabel,OnexStrings.Get("Step interval"),80);_onexControllerMacroPress=Time("OnexControllerMacroPress",out _onexControllerMacroPressLabel,OnexStrings.Get("Press duration"),40);_onexControllerMacroHelp=ControllerHelp("");_onexControllerMacroPanel.Children.Add(_onexControllerMacroHelp);
   _onexControllerMacroMode.SelectedIndex=0;_onexControllerMacroMode.SelectionChanged+=(s,e)=>{if(!_onexControllerSync&&OnexInputGuard.IsActive(_onexControllerMacroMode)){UpdateOnexControllerMacroVisibility();QueueOnexControllerBinding();}};
   _onexControllerMacroStep.SelectionChanged+=(s,e)=>{if(!_onexControllerSync&&_onexControllerMacroStep.SelectedIndex>=0){bool old=_onexControllerSync;_onexControllerSync=true;try{SelectOnexControllerMacroTarget();}finally{_onexControllerSync=old;}}};
   _onexControllerMacroTarget.SelectionChanged+=(s,e)=>{int at=_onexControllerMacroStep.SelectedIndex;if(!_onexControllerSync&&OnexInputGuard.IsActive(_onexControllerMacroTarget)&&at>=0&&at<_onexControllerMacroSteps.Count){_onexControllerMacroSteps[at]=(string)((ComboBoxItem)_onexControllerMacroTarget.SelectedItem).Tag;RefreshOnexControllerMacroSteps();QueueOnexControllerBinding();}};
   RefreshOnexControllerMacroSteps();
  }
  private void AppendOnexControllerMacroStep(string id)
  {
   if(!OnexControllerEditingSequence||!OnexControllerEditingVirtual||_onexControllerMacroSteps.Count>=32)return;
   _onexControllerMacroNextStep=_onexControllerMacroSteps.Count;
   _onexControllerMacroSteps.Add(id);
   RefreshOnexControllerMacroSteps();
   QueueOnexControllerBinding();
  }
  private void PaintOnexControllerPadSelection()
  {
   var selected=OnexControllerEditingSequence
    ?_onexControllerMacroSteps.ElementAtOrDefault(_onexControllerMacroStep?.SelectedIndex??0)
    :(_onexControllerPadTarget.SelectedItem as ComboBoxItem)?.Tag as string;
   _controllerPadArt?.Select(selected);
  }
  private void SelectOnexControllerMacroTarget()
  {int at=_onexControllerMacroStep.SelectedIndex;if(at>=0&&at<_onexControllerMacroSteps.Count)_onexControllerMacroTarget.SelectedItem=_onexControllerMacroTarget.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>(string)item.Tag==_onexControllerMacroSteps[at]);UpdateOnexControllerMacroButtons();PaintOnexControllerPadSelection();}
  private void RefreshOnexControllerMacroSteps()
  {bool old=_onexControllerSync;_onexControllerSync=true;try{int selected=Math.Max(0,Math.Min(_onexControllerMacroSteps.Count-1,_onexControllerMacroNextStep??_onexControllerMacroStep.SelectedIndex));_onexControllerMacroNextStep=null;_onexControllerMacroStep.Items.Clear();for(int n=0;n<_onexControllerMacroSteps.Count;n++)_onexControllerMacroStep.Items.Add(new ComboBoxItem{Content=(n+1).ToString("00")+" · "+AssignmentPadLabel(_onexControllerMacroSteps[n]),Tag=n});_onexControllerMacroStep.SelectedIndex=selected;_onexControllerSequencePreview.Text=string.Join(" → ",_onexControllerMacroSteps.Select(AssignmentPadLabel));SelectOnexControllerMacroTarget();}finally{_onexControllerSync=old;}WireOnexControllerGestureNavigation();}
  private void UpdateOnexControllerMacroButtons()
  {bool active=App.IsConnected&&_onexControllerState?.ContainsKey("entries")==true&&OnexControllerEditingVirtual;int at=_onexControllerMacroStep.SelectedIndex;_onexControllerMacroAdd.IsEnabled=active&&_onexControllerMacroSteps.Count<32;_onexControllerMacroRemove.IsEnabled=active&&_onexControllerMacroSteps.Count>1;_onexControllerMacroUp.IsEnabled=active&&at>0;_onexControllerMacroDown.IsEnabled=active&&at>=0&&at<_onexControllerMacroSteps.Count-1;}
  private void RefreshOnexControllerMacroEditor(JsonObject binding)
  {
   if(_onexControllerMacroPanel==null)return;var macro=binding?.GetNamedObject("macro",null);string source=ControllerSourceId+":"+ControllerEditingDouble;
   if(source!=_onexControllerMacroSource){_onexControllerMacroSource=source;_onexControllerMacroStep.SelectedIndex=0;}
   _onexControllerMacroSteps.Clear();var steps=macro?.GetNamedArray("buttons",null);if(steps!=null)foreach(var item in steps)_onexControllerMacroSteps.Add(item.GetString());if(_onexControllerMacroSteps.Count==0)_onexControllerMacroSteps.Add("A");
   _onexControllerMacroMode.SelectedIndex=Math.Max(0,Array.IndexOf(OnexMacroModes,ControllerText(macro,"mode","once")));
   if(!OnexInputGuard.IsEditing(_onexControllerMacroInterval))_onexControllerMacroInterval.Value=macro?.GetNamedNumber("intervalMs",80)??80;
   if(!OnexInputGuard.IsEditing(_onexControllerMacroPress))_onexControllerMacroPress.Value=macro?.GetNamedNumber("pressMs",40)??40;
   RefreshOnexControllerMacroSteps();
  }
  private JsonObject OnexControllerMacroBinding()
  {var buttons=new JsonArray();foreach(string target in _onexControllerMacroSteps)buttons.Add(JsonValue.CreateStringValue(target));return new JsonObject{{"buttons",buttons},{"mode",JsonValue.CreateStringValue(OnexMacroModes[Math.Max(0,_onexControllerMacroMode.SelectedIndex)])},{"intervalMs",JsonValue.CreateNumberValue(Math.Round(_onexControllerMacroInterval.Value))},{"pressMs",JsonValue.CreateNumberValue(Math.Round(_onexControllerMacroPress.Value))}};}
  private void UpdateOnexControllerMacroVisibility()
  {
   if(_onexControllerMacroPanel==null)return;_onexControllerMacroPanel.Visibility=OnexControllerEditingSequence?Visibility.Visible:Visibility.Collapsed;bool timed=_onexControllerMacroMode.SelectedIndex==1||_onexControllerMacroMode.SelectedIndex==3;
   _onexControllerMacroInterval.Visibility=_onexControllerMacroPress.Visibility=_onexControllerMacroIntervalLabel.Visibility=_onexControllerMacroPressLabel.Visibility=timed?Visibility.Visible:Visibility.Collapsed;
   string[] hints={OnexStrings.Get("Runs the steps once, even after you release the source button."),OnexStrings.Get("Repeats the steps while the source button is held. Releasing it stops the macro."),OnexStrings.Get("Holds all selected buttons together. Press the source button again to release them."),OnexStrings.Get("Repeats the sequence until you press the source button again.")};
   _onexControllerMacroHelp.Text=hints[Math.Max(0,_onexControllerMacroMode.SelectedIndex)]+global::XboxGamingBar.OnexStrings.Get("\nVirtual Controller only. Up to 32 gamepad steps. Opening ONEXTweaks, changing profile or leaving Virtual stops the macro. Press duration is capped by the step interval; a release sample is always emitted.");UpdateOnexControllerMacroButtons();RefreshOnexRemappingWindows();
  }
  private Control[] OnexControllerMacroControls()=>_onexControllerMacroPanel?.Visibility==Visibility.Visible?new Control[]{_onexControllerMacroMode,_onexControllerMacroStep,_onexControllerMacroTarget,_onexControllerMacroAdd,_onexControllerMacroRemove,_onexControllerMacroUp,_onexControllerMacroDown,_onexControllerMacroInterval,_onexControllerMacroPress}.Where(control=>control.Visibility==Visibility.Visible&&control.IsEnabled).ToArray():new Control[0];
  private bool OnexControllerMacroPopupOpen=>new[]{_onexControllerGamepadMode,_onexControllerMacroMode,_onexControllerMacroStep,_onexControllerMacroTarget}.Any(choice=>choice?.IsDropDownOpen==true);
  private Control[] OnexControllerEditorFocusControls()
  {
   var close=_onexRemapWindows.FirstOrDefault(view=>view.Flyout==_onexControllerMappingFlyout&&view.Flyout.IsOpen)?.Close;
   return new Control[]{close,_onexControllerDoubleEnabled,_onexControllerDoubleWindow,_onexControllerGesture,_onexControllerKind,_onexControllerGamepadMode,_onexControllerPadTarget,_onexControllerKeyboard,_onexControllerRecord,_onexControllerMouse}
    .Concat(_controllerPadArt?.Controls??Enumerable.Empty<Control>()).Concat(_controllerKeyboardArt?.Controls??Enumerable.Empty<Control>()).Concat(_controllerMouseArt?.Controls??Enumerable.Empty<Control>()).Concat(OnexControllerMacroControls()).Concat(new[]{(Control)_onexControllerBindingReset})
    .Where(control=>control!=null&&control.Visibility==Visibility.Visible&&control.IsEnabled).ToArray();
  }
 }
}
