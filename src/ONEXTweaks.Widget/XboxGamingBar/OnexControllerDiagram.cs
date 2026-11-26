// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Data.Json;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  // Source IDs remain exactly those accepted by the tested policy/firmware layer.
  // The unparented legacy selector preserves that adapter, never enters the visual/focus tree.
  private readonly Dictionary<string,Button> _onexControllerInputTiles=new Dictionary<string,Button>();
  private readonly Dictionary<string,Button> _onexControllerDiagramButtons=new Dictionary<string,Button>();
  private readonly Dictionary<string,TextBlock> _onexControllerInputSummaries=new Dictionary<string,TextBlock>();
  private readonly List<Control[]> _onexControllerMappingRows=new List<Control[]>();
  private Viewbox _onexControllerDiagram;private Border _onexControllerOemBlock;private Grid _onexControllerStandardInputs;
  private TextBlock _onexControllerDiagramName,_onexControllerDiagramBinding,_onexControllerMappingTitle;
  private Flyout _onexControllerMappingFlyout;private StackPanel _onexControllerMappingEditor;private bool _onexControllerDiagramReady;private string _onexControllerDiagramError;
  private static readonly string[] OnexControllerOemSources={"OEM:Home","OEM:KeyboardMouse","OEM:GKey","OEM:Onex","OEM:M1","OEM:M2"};
  private string OnexControllerSourceName(string id)
  {
   switch(id){case "TouchpadClickLeft":return OnexStrings.Get("Touchpad click (left)");case "TouchpadClickRight":return OnexStrings.Get("Touchpad click (right)");case "TouchpadClick":return global::XboxGamingBar.OnexStrings.Get("DS4 touchpad click");case "Guide":return global::XboxGamingBar.OnexStrings.Get("Xbox / Guide");case "OEM:Home":return global::XboxGamingBar.OnexStrings.Get("Front · Home");case "OEM:GKey":return global::XboxGamingBar.OnexStrings.Get("Front · G-Key / Xbox");case "OEM:KeyboardMouse":return global::XboxGamingBar.OnexStrings.Get("Front · Keyboard / mouse");case "OEM:Onex":return global::XboxGamingBar.OnexStrings.Get("Front · ONEX");case "OEM:M1":return global::XboxGamingBar.OnexStrings.Get("Rear · M1");case "OEM:M2":return global::XboxGamingBar.OnexStrings.Get("Rear · M2");case "Start":return global::XboxGamingBar.OnexStrings.Get("Menu / Start");case "Select":return global::XboxGamingBar.OnexStrings.Get("View / Back");case "LSClick":return global::XboxGamingBar.OnexStrings.Get("Left stick · L3");case "RSClick":return global::XboxGamingBar.OnexStrings.Get("Right stick · R3");}
   if(id.StartsWith("DPad",StringComparison.Ordinal))return global::XboxGamingBar.OnexStrings.Format("D-pad {0}",OnexStrings.Get(id.Substring(4).ToLowerInvariant()));
   if(id.StartsWith("LS",StringComparison.Ordinal))return global::XboxGamingBar.OnexStrings.Format("L stick {0}",OnexStrings.Get(id.Substring(2).ToLowerInvariant()));
   if(id.StartsWith("RS",StringComparison.Ordinal))return global::XboxGamingBar.OnexStrings.Format("R stick {0}",OnexStrings.Get(id.Substring(2).ToLowerInvariant()));return id;
  }
  private void RefreshOnexControllerDs4Actions(){
   bool ds4=ControllerAssignmentTarget=="DualShock4";
   bool auxiliary=ds4&&OnexControllerEditingVirtual&&_onexControllerState?.GetNamedBoolean("ds4AuxiliarySupported",false)==true;
   bool zones=auxiliary&&_onexControllerState?.GetNamedBoolean("ds4TouchpadZonesSupported",false)==true;
   foreach(var choice in new[]{_onexControllerPadTarget,_onexControllerMacroTarget})
   {
    if(choice==null)continue;
    foreach(var item in choice.Items.OfType<ComboBoxItem>())
    {
     var target=item.Tag as string;
     item.Content=AssignmentPadLabel(target,ds4);
     if(Ds4TouchpadTargets.Contains(target))
     {
      item.IsEnabled=target=="TouchpadClick"?auxiliary:zones;
      if(target=="TouchpadClick"&&!ds4)item.Content=OnexStrings.Get("DS4 touchpad click · requires DS4");
     }
    }
   }
  }
  private string OnexControllerBindingSummary(string id)
  {
   var policy=_onexControllerPending??ControllerEditorPolicy;bool oem=id.StartsWith("OEM:",StringComparison.Ordinal);string key=oem?id.Substring(4):id;
   var binding=oem?policy?.GetNamedObject("oem",null)?.GetNamedObject("bindings",null)?.GetNamedObject(key,null):policy?.GetNamedObject("bindings",null)?.GetNamedObject(key,null);string kind=ControllerText(binding,"kind","default");
   if(oem&&kind=="default"){binding=_onexControllerState?.GetNamedObject("oemDefaults",null)?.GetNamedObject(key,null);kind=ControllerText(binding,"kind","disabled");}
   switch(kind){case "default":return NintendoTarget(id)!=id?NintendoTarget(id)+global::XboxGamingBar.OnexStrings.Get(" · Nintendo"):global::XboxGamingBar.OnexStrings.Get("Original");case "disabled":return global::XboxGamingBar.OnexStrings.Get("Off");case "widget":return global::XboxGamingBar.OnexStrings.Get("ONEXTweaks");case "keyboardPopup":return global::XboxGamingBar.OnexStrings.Get("Windows keyboard");case "steamBigPicture":return global::XboxGamingBar.OnexStrings.Get("Steam Big Picture / Menu");case "steamQuickAccess":return global::XboxGamingBar.OnexStrings.Get("Steam Quick Access");case "macro":return global::XboxGamingBar.OnexStrings.Format("Macro · {0} steps",(binding?.GetNamedObject("macro",null)?.GetNamedArray("buttons",null)?.Count??0));case "gamepad":string target=ControllerText(binding,"target","A"),output=NintendoTarget(target);return AssignmentPadLabel(output)+(target!=output?global::XboxGamingBar.OnexStrings.Get(" · Nintendo"):"");case "keySequence":return OnexKeyboardCaption(ReadAssignmentNumbers(binding,"keys"),true);case "keyboard":var keys=binding?.GetNamedArray("keys",null);return keys==null?global::XboxGamingBar.OnexStrings.Get("Keyboard"):OnexKeyboardCaption(ReadAssignmentNumbers(binding,"keys"));case "mouse":return KbmMouseCaption(ControllerText(binding,"target"));default:return kind;}
  }
  private void BuildOnexControllerMappingOverview(StackPanel mapping,FrameworkElement oemToggle,StackPanel editor)
  {
   _onexControllerMappingEditor=editor;
   var canvas=CreateOnexControllerDeviceArt();_onexControllerDiagramReady=true;
   _onexControllerDiagramName=AddOnexControllerSketchLabel(canvas,344,23,OnexDialogAccent);
   _onexControllerDiagramBinding=AddOnexControllerSketchLabel(canvas,383,15,OnexSettingsBrush(210,217,226));
   foreach(string id in ControllerInputs)
   {
    var button=AddOnexControllerSketchButton(canvas,id,"OnexDiagram",anchor=>SelectOnexControllerMappingSource(id,anchor,true));
    _onexControllerDiagramButtons.Add(id,button);_onexControllerControls.Add(button);
   }
   BuildOnexControllerExtraDiagram(_onexControllerExtraPanel);
   _onexControllerDiagram=new Viewbox{Name="OnexControllerDiagram",Stretch=Stretch.Uniform,Child=canvas,HorizontalAlignment=HorizontalAlignment.Stretch};mapping.Children.Add(HostOnexSketch(_onexControllerDiagram));
   Grid Inputs(string[] ids,int columns)
   {
    var grid=new Grid{ColumnSpacing=8,RowSpacing=8};for(int c=0;c<columns;c++)grid.ColumnDefinitions.Add(new ColumnDefinition());for(int r=0;r<(ids.Length+columns-1)/columns;r++)grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
    for(int n=0;n<ids.Length;n++)
    {
     string id=ids[n];var labels=new StackPanel{Spacing=3};labels.Children.Add(new TextBlock{Text=OnexControllerSourceName(id),FontSize=13,FontWeight=Windows.UI.Text.FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,MaxLines=1});var summary=new TextBlock{FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis,MaxLines=1};labels.Children.Add(summary);
     var button=new Button{Name="OnexInput"+id.Replace(":",""),Tag=id,Content=labels,MinHeight=52,Padding=new Thickness(10,7,10,7),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),Style=(Style)Resources["StandaloneButtonStyle"]};
     AutomationProperties.SetName(button,OnexControllerSourceName(id));button.GotFocus+=(s,e)=>SelectOnexControllerMappingSource(id,button,false);button.Click+=(s,e)=>SelectOnexControllerMappingSource(id,button,true);Grid.SetColumn(button,n%columns);Grid.SetRow(button,n/columns);grid.Children.Add(button);_onexControllerInputTiles.Add(id,button);_onexControllerInputSummaries.Add(id,summary);_onexControllerControls.Add(button);
    }
    for(int r=0;r<(ids.Length+columns-1)/columns;r++)_onexControllerMappingRows.Add(ids.Skip(r*columns).Take(columns).Select(id=>(Control)_onexControllerInputTiles[id]).ToArray());return grid;
   }
   var extraGrid=Inputs(OnexControllerOemSources,2);_onexControllerStandardInputs=Inputs(ControllerInputs,4);InitializeOnexControllerMappingGroups(mapping,oemToggle,extraGrid,_onexControllerStandardInputs);
   mapping.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Выбери кнопку на схеме или в сетке. Под ней указано текущее назначение. A / тап — изменить; B — закрыть настройки. Изменения сохраняются в выбранном профиле.")));
   NormalizeOnexControllerMappingSpacing();RefreshOnexControllerMappingOverview();
  }
  private void SelectOnexControllerMappingSource(string id,Button anchor,bool edit)
  {
   if(!edit&&IsOnexRemapOpen(_onexControllerMappingFlyout))return;
   if(!OnexControllerSourceGroupOn(id))return;
   if(edit)try{EnsureOnexControllerBindingEditor();}catch(Exception error){_onexControllerStatus.Text=OnexStrings.Format("Button editor: {0}",error.Message);return;}
   if(ControllerSourceId!=id){_onexControllerRecording=false;_onexControllerRecordModifiers.Clear();if(_onexControllerRecord!=null)_onexControllerRecord.Content=global::XboxGamingBar.OnexStrings.Get("Record keyboard shortcut");_onexControllerSource.SelectedItem=_onexControllerSource.Items.OfType<ComboBoxItem>().Single(item=>(string)item.Tag==id);}
   RefreshOnexControllerMappingOverview();
   if(!edit)return;
   RefreshOnexControllerMapping();
   if(!OnexControllerWideEditor(_onexControllerKind.SelectedIndex)&&TryOpenOnexSketchEditor(_onexControllerMappingFlyout,anchor,_onexControllerInputTiles[id]))return;
   ShowOnexControllerMappingFlyout(anchor);
  }
  private void ShowOnexControllerMappingFlyout(Button anchor)
  {
   var flyout=_onexControllerMappingFlyout;
   PrepareFullOnexRemap(flyout);
   var view=_onexRemapWindows.Single(item=>item.Flyout==flyout);
   view.Anchor=anchor;
   view.ReturnFocus=_onexControllerInputTiles[ControllerSourceId];
   if(OnexControllerWideEditor(_onexControllerKind.SelectedIndex)&&_onexSketchOwners.TryGetValue(anchor,out var surface))
    view.WideSketch=surface.RemapHost;
   _onexOpenMenus.Add(flyout);flyout.XamlRoot=anchor.XamlRoot;try{flyout.ShowAt(anchor);}catch(ArgumentException){try{flyout.ShowAt(_onexControllerSections[0].Header);}catch(Exception error){_onexOpenMenus.Remove(flyout);_onexControllerStatus.Text=global::XboxGamingBar.OnexStrings.Format("Button editor: {0}",error.Message);_onexControllerDiagramError=error.ToString();}}
  }
  private void RefreshOnexControllerMappingOverview()
  {
   if(_onexControllerDiagramName==null)return;RefreshOnexControllerGroupAvailability();string selected=ControllerSourceId;if(_onexControllerMappingTitle!=null)_onexControllerMappingTitle.Text=selected==null?global::XboxGamingBar.OnexStrings.Get("Choose a button"):OnexControllerSourceName(selected);bool extra=selected?.StartsWith("OEM:",StringComparison.Ordinal)==true;_onexControllerDiagramName.Text=selected==null||extra?global::XboxGamingBar.OnexStrings.Get("Gamepad buttons"):OnexControllerSourceName(selected);_onexControllerDiagramBinding.Text=selected==null||extra?global::XboxGamingBar.OnexStrings.Get("Tap a button to edit"):!OnexControllerSourceGroupOn(selected)?global::XboxGamingBar.OnexStrings.Get("Remapping off"):OnexControllerBindingSummary(selected);_onexControllerExtraName.Text=extra?OnexControllerSourceName(selected):global::XboxGamingBar.OnexStrings.Get("Extra buttons");_onexControllerExtraBinding.Text=extra?(!OnexControllerSourceGroupOn(selected)?global::XboxGamingBar.OnexStrings.Get("Remapping off"):OnexControllerBindingSummary(selected)):global::XboxGamingBar.OnexStrings.Get("Tap a button to edit");ApplyOnexControllerActionAvailability();
   foreach(var pair in _onexControllerInputTiles){bool active=pair.Key==selected&&OnexControllerSourceGroupOn(pair.Key);pair.Value.BorderBrush=OnexSolidBrush(active?OnexCurrentPalette.SelectedLine:OnexCurrentPalette.Line);pair.Value.Background=OnexSolidBrush(active?OnexCurrentPalette.Selected:OnexCurrentPalette.Raised);pair.Value.Foreground=OnexSolidBrush(OnexCurrentPalette.Text);string summary=OnexControllerBindingSummary(pair.Key);_onexControllerInputSummaries[pair.Key].Text=summary;_onexControllerInputSummaries[pair.Key].Foreground=OnexSolidBrush(summary==OnexStrings.Get("Off")?OnexCurrentPalette.Muted:active?OnexCurrentPalette.SelectedText:OnexCurrentPalette.Text);AutomationProperties.SetHelpText(pair.Value,summary);}
   foreach(var pair in _onexControllerDiagramButtons)
    PaintOnexControllerSketchButton(pair.Value,pair.Key==selected&&OnexControllerSourceGroupOn(pair.Key));
  }
  private void HandleOnexControllerMappingEditorKey(object sender,KeyRoutedEventArgs args)
  {
   if(args.Handled||HandleOnexControllerRecordKey(args)||OnexInputGuard.HandleFocusedSlider(args))return;
   if(_controllerKeyboardArt?.HasOpenDropdown==true||OnexControllerMacroPopupOpen||new[]{_onexControllerKind,_onexControllerPadTarget,_onexControllerKeyboard,_onexControllerMouse,_onexControllerGesture}.Any(choice=>choice.IsDropDownOpen))return;
   if(args.Key==VirtualKey.GamepadB||args.Key==VirtualKey.Escape){args.Handled=true;CloseOnexRemapEditor(_onexControllerMappingFlyout);return;}
   if(!IsOnexProfileVertical(args.Key))return;var controls=OnexControllerEditorFocusControls();int index=Array.IndexOf(controls,FocusManager.GetFocusedElement() as Control);if(index<0)return;int next=Math.Max(0,Math.Min(controls.Length-1,index+(IsOnexProfileDown(args.Key)?1:-1)));args.Handled=true;controls[next].Focus(FocusState.Keyboard);controls[next].StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});
  }
  private bool HandleOnexControllerDiagramNavigation(KeyRoutedEventArgs args)
  {
   if(args.Handled||LegionNavItem?.IsChecked!=true||_onexOpenMenus.Any(menu=>menu.IsOpen)||!IsOnexProfileVertical(args.Key))return false;
   var button=FocusManager.GetFocusedElement() as Button;if(button==null||!_onexControllerDiagramButtons.Values.Contains(button)||!_onexControllerInputTiles.TryGetValue((string)button.Tag,out var tile))return false;args.Handled=true;tile.Focus(FocusState.Keyboard);tile.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});return true;
  }
 }
}
