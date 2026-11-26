// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Data.Json;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private sealed class OnexControllerSection
  {
   internal string Id;internal Button Header;internal Border Detail;internal TextBlock Summary;internal FrameworkElement Icon;internal FontIcon Chevron;
  }
  private Grid _onexControllerCardGrid;private OnexControllerSection[] _onexControllerSections;
  private OnexConnectedCardOutline _onexControllerOutline;private string _onexControllerOpen;
  private Border _onexControllerModeCard;private Button _onexControllerStatusShortcut;private TextBlock _onexControllerModeHint;private Border _onexControllerModeDot;
  private Border CreateOnexControllerModeStrip()
  {
   var row=new Grid{ColumnSpacing=12};row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var caption=new StackPanel{Spacing=4,VerticalAlignment=VerticalAlignment.Center};caption.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Controller Mode"),FontSize=16,FontWeight=Windows.UI.Text.FontWeights.SemiBold});_onexControllerModeHint=ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Physical controller"));caption.Children.Add(_onexControllerModeHint);row.Children.Add(caption);
   _onexControllerTransport=ControllerChoice("","OnexControllerTransport",new[]{"Hardware Controller","Virtual Controller"});_onexControllerTransport.Header=null;_onexControllerTransport.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(_onexControllerTransport,1);row.Children.Add(_onexControllerTransport);
   _onexControllerTransport.SelectionChanged+=async(s,e)=>{if(!_onexControllerSync&&OnexInputGuard.IsActive(_onexControllerTransport))await ControllerActionAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("enable")},{"enabled",JsonValue.CreateBooleanValue(_onexControllerTransport.SelectedIndex==1)}});};
   var status=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,VerticalAlignment=VerticalAlignment.Center};_onexControllerModeDot=new Border{Width=8,Height=8,CornerRadius=new CornerRadius(4),VerticalAlignment=VerticalAlignment.Center,Background=OnexSettingsBrush(125,132,145)};status.Children.Add(_onexControllerModeDot);
   _onexControllerStatusShortcut=new Button{Name="OnexControllerStatusShortcut",Style=(Style)Resources["StandaloneButtonStyle"],Padding=new Thickness(10),Content=new FontIcon{Glyph="\uE713",FontSize=18}};ToolTipService.SetToolTip(_onexControllerStatusShortcut,global::XboxGamingBar.OnexStrings.Get("Controller status & recovery"));_onexControllerStatusShortcut.Click+=(s,e)=>{SetOnexControllerCard("status");if(_onexControllerOpen=="status")_onexControllerSections.Last().Header.Focus(FocusState.Keyboard);};status.Children.Add(_onexControllerStatusShortcut);Grid.SetColumn(status,2);row.Children.Add(status);
   return _onexControllerModeCard=OnexSettingsCard(row);
  }
  private void ComposeOnexControllerCards(Panel body,Panel extra,Panel mapping,Panel sticks,Panel status)
  {
   _onexControllerCardGrid=new Grid{Name="OnexControllerCardGrid",ColumnSpacing=12};for(int n=0;n<2;n++)_onexControllerCardGrid.ColumnDefinitions.Add(new ColumnDefinition());for(int n=0;n<6;n++)_onexControllerCardGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
   var gyro=CreateOnexControllerMotionSection();
   string[] ids={"extra","mapping","sticks","gyro","vibration","status"},titles={"Extra Buttons","Full Remapping","Sticks & Triggers","Gyro","Vibration","Controller Status"},glyphs={"\uE7FC","\uE7FC","","","","\uE9D9"};Panel[] contents={extra,mapping,sticks,gyro,CreateOnexControllerVibrationSection(),status};var sections=new List<OnexControllerSection>();
   for(int n=0;n<ids.Length;n++)
   {
    string id=ids[n];var content=new Grid{ColumnSpacing=12};content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24)});content.ColumnDefinitions.Add(new ColumnDefinition());content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(18)});
    var icon=CreateOnexControllerSectionIcon(id,glyphs[n]);content.Children.Add(icon);
    var label=new StackPanel{Spacing=4,VerticalAlignment=VerticalAlignment.Center};label.Children.Add(new TextBlock{Text=OnexStrings.Get(titles[n]),FontSize=18,FontWeight=Windows.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.WrapWholeWords});var summary=new TextBlock{Text="—",FontSize=12,Foreground=tileTextBrush,Opacity=.85,TextWrapping=TextWrapping.Wrap,MaxLines=2,TextTrimming=TextTrimming.CharacterEllipsis};label.Children.Add(summary);Grid.SetColumn(label,1);content.Children.Add(label);
    var chevron=new FontIcon{Glyph="\uE70D",FontSize=13,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(chevron,2);content.Children.Add(chevron);
    var header=new Button{Name="OnexControllerCard"+id,Tag=id,Content=content,Style=(Style)Resources["StandaloneButtonStyle"],HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Center,MinHeight=104,Padding=new Thickness(14,12,14,12),Margin=new Thickness(0,0,0,n<4?12:0),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12)};
    var detail=OnexSettingsCard(contents[n]);detail.Name="OnexControllerDetail"+id;detail.BorderThickness=new Thickness(1);detail.Visibility=Visibility.Collapsed;detail.Margin=new Thickness(0,0,0,n<4?12:0);
    int row=(n/2)*2;Grid.SetRow(header,row);Grid.SetColumn(header,n%2);Grid.SetRow(detail,row+1);Grid.SetColumnSpan(detail,2);_onexControllerCardGrid.Children.Add(header);_onexControllerCardGrid.Children.Add(detail);header.Click+=(s,e)=>SetOnexControllerCard(id);sections.Add(new OnexControllerSection{Id=id,Header=header,Detail=detail,Summary=summary,Icon=icon,Chevron=chevron});
   }
   _onexControllerSections=sections.ToArray();_onexControllerOutline=new OnexConnectedCardOutline(_onexControllerCardGrid);body.Children.Add(_onexControllerCardGrid);
  }
  private Color OnexControllerAccent(string id,bool text=false)=>OnexRoleAccent(id=="sticks"?1:id=="gyro"?3:id=="mapping"||id=="extra"?0:2,text);
  private void PaintOnexControllerCards()
  {
   if(_onexControllerSections==null)return;
   foreach(var card in _onexControllerSections)
   {var color=card.Header.IsEnabled?OnexControllerAccent(card.Id):Color.FromArgb(255,141,149,163);PaintOnexControllerIcon(card.Icon,color);card.Icon.Opacity=card.Header.IsEnabled ? .85 : .35;card.Header.BorderBrush=card.Id==_onexControllerOpen?OnexSolidBrush(color):(Brush)Resources["CardBorderBrush"];card.Detail.BorderBrush=OnexSolidBrush(color);PaintOnexPageContent(card.Detail,color);foreach(var value in OnexOwnedContent(card.Detail).OfType<TextBlock>().Where(text=>(text.Tag as string)=="OnexControllerTuningValue"))value.Foreground=OnexSolidBrush(OnexControllerAccent(card.Id,true));}
   RefreshOnexControllerMappingOverview();
    var active=_onexControllerSections.FirstOrDefault(card=>card.Id==_onexControllerOpen);_onexControllerOutline.Set(active?.Header,active?.Detail,active==null?null:OnexSolidBrush(OnexControllerAccent(active.Id)));
  }
  private void SetOnexControllerCard(string id)
  {
   if(_onexControllerSections==null||id=="mapping"&&!OnexControllerEditingVirtual)return;string previous=_onexControllerOpen;if(id==previous)id=null;
    if(_onexControllerDiagnosticReturnKind!=null&&id!=_onexControllerDiagnosticReturnKind)ClearOnexControllerDiagnosticReturn();
   if(_onexControllerRecording){_onexControllerRecording=false;_onexControllerRecordModifiers.Clear();_onexControllerRecord.Content=global::XboxGamingBar.OnexStrings.Get("Record keyboard shortcut");}
   CloseOnexRemapEditor(_onexControllerMappingFlyout);if(id=="mapping"&&ControllerIsOem)_onexControllerSource.SelectedItem=_onexControllerSource.Items.OfType<ComboBoxItem>().First(item=>(string)item.Tag=="A");if(id=="extra"&&!ControllerIsOem)_onexControllerSource.SelectedItem=_onexControllerSource.Items.OfType<ComboBoxItem>().First(item=>(string)item.Tag=="OEM:Home");_onexControllerOpen=id;foreach(var card in _onexControllerSections){bool open=card.Id==id;card.Detail.Visibility=open?Visibility.Visible:Visibility.Collapsed;card.Header.UseSystemFocusVisuals=false;card.Header.Padding=new Thickness(14,12,14,open&&Grid.GetColumnSpan(card.Header)==1&&Grid.GetRow(card.Header)<4?24:12);card.Chevron.Glyph=open?"\uE711":"\uE70D";}
   PaintOnexControllerCards();ApplyOnexTabPalette();WireOnexControllerNavigation();
   var target=_onexControllerSections.FirstOrDefault(card=>card.Id==(id??previous));if(target!=null){target.Header.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});if(id==null)target.Header.Focus(FocusState.Keyboard);}
   ApplyOnexPageFocus();
  }
  private void RefreshOnexControllerCards()
  {
   if(_onexControllerSections==null||_onexControllerState==null)return;var policy=_onexControllerPending??ControllerEditorPolicy;var tuning=policy?.GetNamedObject("tuning",null);var runtime=_onexControllerState.GetNamedObject("runtime",null);bool running=runtime?.GetNamedBoolean("running",false)==true;
   string Oem(string id){var binding=policy?.GetNamedObject("oem",null)?.GetNamedObject("bindings",null)?.GetNamedObject(id,null);string kind=ControllerText(binding,"kind","default");if(kind=="default")kind=ControllerText(_onexControllerState.GetNamedObject("oemDefaults",null)?.GetNamedObject(id,null),"kind","disabled");return kind=="disabled"?"Off":kind=="widget"?"ONEXTweaks":kind=="gamepad"?ControllerText(binding,"target","Gamepad"):kind=="keyboardPopup"?"Keyboard":kind=="steamBigPicture"?"Steam Menu":kind=="steamQuickAccess"?"Steam Quick Access":kind=="keyboard"?"Keys":kind=="mouse"?"Mouse":kind;}
   foreach(var card in _onexControllerSections)
   {
    card.Summary.Text=card.Id=="extra"?global::XboxGamingBar.OnexStrings.Format("6 buttons · {0} · {1}",(_onexControllerState.GetNamedBoolean("oemEnabled",false)?global::XboxGamingBar.OnexStrings.Get("On"):global::XboxGamingBar.OnexStrings.Get("Off")),(OnexControllerEditingVirtual?global::XboxGamingBar.OnexStrings.Get("All actions"):global::XboxGamingBar.OnexStrings.Get("System actions only"))):card.Id=="mapping"?(OnexControllerEditingVirtual?global::XboxGamingBar.OnexStrings.Format("24 controls · {0}",(OnexControllerStandardRemapOn?global::XboxGamingBar.OnexStrings.Get("On"):global::XboxGamingBar.OnexStrings.Get("Off"))):global::XboxGamingBar.OnexStrings.Get("Requires Virtual Controller")):card.Id=="sticks"?global::XboxGamingBar.OnexStrings.Format("Deadzone L / R: {0}% / {1}%",(tuning?.GetNamedNumber("leftDeadzone",0)??0),(tuning?.GetNamedNumber("rightDeadzone",0)??0)):card.Id=="gyro"?DescribeOnexControllerMotion():card.Id=="vibration"?DescribeOnexControllerVibration(policy):running?global::XboxGamingBar.OnexStrings.Format("VIIPER · {0}",OnexControllerTargetName(ControllerText(runtime,"outputTarget","Xbox360"))):global::XboxGamingBar.OnexStrings.Get("Hardware controller");card.Header.IsEnabled=card.Id!="mapping"||OnexControllerEditingVirtual;card.Header.Opacity=card.Header.IsEnabled?1:.38;
   }
   if(_onexControllerOpen=="mapping"&&!OnexControllerEditingVirtual){SetOnexControllerCard(null);_onexControllerTransport.Focus(FocusState.Programmatic);}
   if(_onexControllerVibrationValue!=null&&(_onexControllerVibrationIntensity==null||!OnexInputGuard.IsEditing(_onexControllerVibrationIntensity)))_onexControllerVibrationValue.Text=global::XboxGamingBar.OnexStrings.Format("Intensity · {0}%",(policy?.GetNamedObject("vibration",null)?.GetNamedNumber("intensity",100)??100));
   bool enableRequested=_onexControllerState.GetNamedBoolean("effectiveEnabled",false);
   string captureError=ControllerText(_onexControllerState,"modeError","");
   _onexControllerModeHint.Text=running
    ?global::XboxGamingBar.OnexStrings.Format("VIIPER · {0}",OnexControllerTargetName(ControllerText(runtime,"outputTarget","Xbox360")))
    :_onexControllerState.GetNamedBoolean("hardwareProfileOverride",false)
    ?global::XboxGamingBar.OnexStrings.Get("Hardware · profile exception")
    :enableRequested
    ?global::XboxGamingBar.OnexStrings.Get(string.IsNullOrWhiteSpace(captureError)?"Starting virtual controller":"Physical input · virtual controller paused")
    :global::XboxGamingBar.OnexStrings.Get("Physical input");
   _onexControllerModeDot.Background=OnexSolidBrush(running?OnexPalette.Cyan:Color.FromArgb(255,125,132,145));
  }
  private Control[][] OnexControllerCardRows()
  {
   var rows=new List<Control[]>();rows.AddRange(OnexProfileRows(_onexControllerPicker));rows.Add(new Control[]{_onexControllerTransport,_onexControllerStatusShortcut});
   if(_onexControllerSections!=null)foreach(int gridRow in new[]{0,2,4})
   {
    var group=_onexControllerSections.Where(card=>Grid.GetRow(card.Header)==gridRow).ToArray();rows.Add(group.Select(card=>(Control)card.Header).ToArray());var open=group.FirstOrDefault(card=>card.Id==_onexControllerOpen);if(open==null)continue;
    if(open.Id=="extra"){rows.Add(new Control[]{_onexControllerOemEnabled,_onexControllerOemReset});if(_onexControllerState?.GetNamedBoolean("oemEnabled",false)==true)rows.AddRange(_onexControllerMappingRows.Take(3));}
    if(open.Id=="mapping")
    {
     rows.Add(new Control[]{_onexControllerStandardEnabled,_onexControllerStandardReset});if(OnexControllerStandardRemapOn)rows.AddRange(_onexControllerMappingRows.Skip(3));
    }
    if(open.Id=="sticks"){rows.Add(new Control[]{_onexControllerSticksTest});rows.Add(new Control[]{_onexControllerLeftDeadzone,_onexControllerRightDeadzone});rows.AddRange(OnexControllerTriggerRows());rows.AddRange(new[]{new Control[]{_onexControllerThreshold},new Control[]{_onexControllerInvertLx,_onexControllerInvertLy},new Control[]{_onexControllerInvertRx,_onexControllerInvertRy},new Control[]{_onexControllerSwap},new Control[]{_onexControllerNintendo},new Control[]{_onexControllerSticksReset}});}
    if(open.Id=="gyro"){rows.Add(new Control[]{_onexControllerGyroTest});rows.AddRange(OnexControllerGyroRows());}
    if(open.Id=="vibration")rows.AddRange(new[]{new Control[]{_onexControllerRumbleProfile},new Control[]{_onexControllerSwapMotors},new Control[]{_onexControllerVibrationIntensity},new Control[]{_onexControllerVibrationTest,_onexControllerVibrationReset}});
    if(open.Id=="status")rows.AddRange(new[]{new Control[]{_onexControllerRefreshStatus},new Control[]{_onexControllerAutomatic,_onexControllerRecover},new Control[]{_onexControllerTarget},new Control[]{_onexControllerForwardMotion},new Control[]{_onexControllerForwardLighting},new Control[]{_onexControllerReset},new Control[]{_onexControllerFirmwareRead}});
   }
   if(_onexControllerHardwareProfileCard?.Visibility==Visibility.Visible)rows.Add(new Control[]{_onexControllerUseHardware});
   if(_onexControllerCaptureCard?.Visibility==Visibility.Visible){rows.Add(new Control[]{_onexControllerCaptureBoxes[0],_onexControllerCaptureBoxes[1]});rows.Add(new Control[]{_onexControllerCaptureBoxes[2],_onexControllerCaptureBoxes[3]});}
   if(_onexControllerSavedProfiles!=null)rows.AddRange(_onexControllerSavedProfiles.FocusRows());
   return rows.Select(row=>row.Where(control=>control!=null&&control.IsEnabled&&control.IsTabStop&&control.Visibility==Visibility.Visible).ToArray()).Where(row=>row.Length>0).ToArray();
  }
  private void NormalizeOnexControllerMappingSpacing()
  {
   if(_onexControllerMappingPanel==null)return;bool first=true;foreach(var child in _onexControllerMappingPanel.Children.OfType<FrameworkElement>()){bool visible=OnexVisiblePageSection(child);child.Margin=new Thickness(0,visible&&!first?12:0,0,0);if(visible)first=false;}
  }
  private bool HandleOnexControllerCardBack(KeyRoutedEventArgs args)
  {
   if(args.Handled||LegionNavItem?.IsChecked!=true||_onexControllerOpen==null||(args.Key!=VirtualKey.GamepadB&&args.Key!=VirtualKey.Escape)||_onexDetailsDialog!=null||_onexOpenMenus.Any(menu=>menu.IsOpen))return false;
   // Game Bar can translate B to Escape. Native ComboBox popups own either form.
   if(_onexControllerControls.OfType<ComboBox>().Any(choice=>choice.IsDropDownOpen))return false;
   for(var node=FocusManager.GetFocusedElement() as DependencyObject;node!=null;node=VisualTreeHelper.GetParent(node))if(ReferenceEquals(node,_onexControllerSavedProfiles?.Card))return false;
    if(_onexControllerOpen=="gyro"&&_onexGyroOpenSection!=null)
    {var section=_onexGyroSections.FirstOrDefault(s=>s.Id==_onexGyroOpenSection);args.Handled=true;OpenOnexGyroSection(null);section?.Header.Focus(FocusState.Keyboard);return true;}
   args.Handled=true;SetOnexControllerCard(null);return true;
  }
  private FrameworkElement CreateOnexControllerSectionIcon(string id,string glyph)
  {
   if(!string.IsNullOrEmpty(glyph))return new FontIcon{Glyph=glyph,FontSize=23,Width=24,Height=28,VerticalAlignment=VerticalAlignment.Center};
   var canvas=new Canvas{Width=24,Height=28,VerticalAlignment=VerticalAlignment.Center};
   void Ellipse(double left,double top,double width,double height,bool dot=false,double angle=0)
   {var shape=new Windows.UI.Xaml.Shapes.Ellipse{Width=width,Height=height,StrokeThickness=1.6,Tag=dot?"dot":null};Canvas.SetLeft(shape,left);Canvas.SetTop(shape,top);if(angle!=0)shape.RenderTransform=new RotateTransform{Angle=angle,CenterX=width/2,CenterY=height/2};canvas.Children.Add(shape);}
   void Line(double x1,double y1,double x2,double y2){canvas.Children.Add(new Windows.UI.Xaml.Shapes.Line{X1=x1,Y1=y1,X2=x2,Y2=y2,StrokeThickness=1.6,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round});}
   if(id=="vibration"){canvas.Children.Add(new Windows.UI.Xaml.Shapes.Rectangle{Width=8,Height=15,RadiusX=2,RadiusY=2,StrokeThickness=1.7,Margin=new Thickness(8,7,0,0)});foreach(double x in new double[]{2,5,19,22}){Line(x,7,x==2||x==5?x-1:x+1,11);Line(x==2||x==5?x-1:x+1,11,x==2||x==5?x-1:x+1,19);Line(x==2||x==5?x-1:x+1,19,x,23);}}
   else if(id=="gyro"){Ellipse(1,3,22,22);Ellipse(2,9,20,10,angle:45);Ellipse(2,9,20,10,angle:-45);Ellipse(10,12,4,4,true);}
   else{foreach(double x in new double[]{6,18}){Ellipse(x-3,5,6,6);Line(x,11,x,19);Line(x-4,21,x+4,21);}}
   PaintOnexControllerIcon(canvas,OnexControllerAccent(id));return canvas;
  }
  private void PaintOnexControllerIcon(FrameworkElement icon,Color color)
  {
   var brush=OnexSolidBrush(color);if(icon is FontIcon font)font.Foreground=brush;
   if(icon is Canvas canvas)foreach(var shape in canvas.Children.OfType<Windows.UI.Xaml.Shapes.Shape>()){if((string)shape.Tag=="dot")shape.Fill=brush;else shape.Stroke=brush;}
  }
 }
}
