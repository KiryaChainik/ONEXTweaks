// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private Grid KbmBindingLine(string label,out TextBlock value)
  {
   var row=new Grid{ColumnSpacing=4};
   row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   row.ColumnDefinitions.Add(new ColumnDefinition());
   var caption=new TextBlock{FontSize=11,Text=label};
   value=new TextBlock{FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis,MaxLines=1};
   Grid.SetColumn(value,1);
   row.Children.Add(caption);
   row.Children.Add(value);
   return row;
  }

  private void BuildKbmMapping(StackPanel host)
  {
   const string layer="base";
   var canvas=CreateOnexControllerDeviceArt();
   _kbmDiagramNames[layer]=AddOnexControllerSketchLabel(canvas,344,23,OnexDialogAccent);
   _kbmDiagramValues[layer]=AddOnexControllerSketchLabel(canvas,383,15,OnexSettingsBrush(210,217,226));
   foreach(string id in ControllerInputs.Concat(OnexControllerOemSources))
   {
    string key=layer+":"+id;
    var button=AddOnexControllerSketchButton(canvas,id,"OnexKbmbaseSketch",anchor=>OpenKbmEditor(id,layer,anchor,true));
    button.GettingFocus+=(sender,args)=>
    {
     if(args.InputDevice!=FocusInputDeviceKind.Mouse&&args.InputDevice!=FocusInputDeviceKind.Touch&&_kbmTiles.TryGetValue(key,out var tile))
      args.TrySetNewFocusedElement(tile);
    };
    _kbmDiagramKeys.Add(key,button);
   }
   var diagram=new Viewbox{Name="OnexKbmbaseDiagram",Stretch=Stretch.Uniform,Child=canvas,HorizontalAlignment=HorizontalAlignment.Stretch};
   _kbmDiagrams[layer]=diagram;
   host.Children.Add(HostOnexSketch(diagram));
   void Inputs(string[] ids,int columns)
   {
    var grid=new Grid{ColumnSpacing=8,RowSpacing=8};
    for(int c=0;c<columns;c++)grid.ColumnDefinitions.Add(new ColumnDefinition());
    for(int r=0;r<(ids.Length+columns-1)/columns;r++)grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
    for(int n=0;n<ids.Length;n++)
    {
     string id=ids[n],key=layer+":"+id;
     var labels=new StackPanel{Spacing=3};
     labels.Children.Add(new TextBlock{Text=OnexControllerSourceName(id),FontSize=13,
      FontWeight=Windows.UI.Text.FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,MaxLines=1});
     labels.Children.Add(KbmBindingLine(OnexStrings.Get("Main:"),out var main));
     labels.Children.Add(KbmBindingLine(OnexStrings.Get("Fn layer:"),out var fn));
     var button=new Button{Name="OnexKbmbase"+id.Replace(":",""),Tag=id,Content=labels,MinHeight=62,
      Padding=new Thickness(10,7,10,7),HorizontalAlignment=HorizontalAlignment.Stretch,
      HorizontalContentAlignment=HorizontalAlignment.Stretch,CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),
      Style=(Style)Resources["StandaloneButtonStyle"]};
     AutomationProperties.SetName(button,OnexControllerSourceName(id));
     button.GotFocus+=(sender,args)=>OpenKbmEditor(id,layer,button,false);
     button.Click+=(sender,args)=>OpenKbmEditor(id,layer,button,true);
     Grid.SetColumn(button,n%columns);
     Grid.SetRow(button,n/columns);
     grid.Children.Add(button);
     _kbmTiles.Add(key,button);
     _kbmSummaries.Add(key,main);
     _kbmFnSummaries.Add(key,fn);
    }
    for(int r=0;r<(ids.Length+columns-1)/columns;r++)
     _kbmBaseRows.Add(ids.Skip(r*columns).Take(columns).Select(id=>(Control)_kbmTiles[layer+":"+id]).ToArray());
    host.Children.Add(grid);
   }
   host.Children.Add(ControllerHelp(OnexStrings.Get("Extra buttons")));
   Inputs(OnexControllerOemSources,2);
   host.Children.Add(ControllerHelp(OnexStrings.Get("Gamepad controls → keyboard & mouse")));
   Inputs(ControllerInputs,4);
  }

  private bool KbmHasOverride(string source,string layer,bool twice=false)
  {
   var policy=KbmPolicy;
   if(twice)
   {
    var assigned=KbmObject(policy,layer=="fn"?"fnStartDouble":"startDouble");
    return assigned!=null&&(layer=="fn"||ControllerText(assigned,"kind")!="keyboard"||
     !ReadAssignmentNumbers(assigned,"keys").SequenceEqual(new[]{91,9}));
   }
   if(source==ControllerText(policy,"fnButton"))return layer=="base";
   return KbmObject(policy,layer=="fn"?"fnBindings":"bindings")?.ContainsKey(source)==true;
  }

  private string KbmLayerSummary(string id,string layer,bool includeGesture)
  {
   if(layer=="fn"&&id==ControllerText(KbmPolicy,"fnButton"))return "—";
   var summary=KbmSummary(KbmEffective(id,layer));
   if(includeGesture&&id=="Start")summary+=OnexStrings.Format(" · Double: {0}",KbmSummary(KbmEffective(id,layer,true)));
   if(includeGesture&&layer=="base"&&id=="OEM:KeyboardMouse")summary+=OnexStrings.Get(" · Double: switch mode");
   return summary;
  }

  private void PaintKbmLayerValue(TextBlock value,string source,string layer,bool includeGesture,bool selected=false)
  {
   var palette=OnexCurrentPalette;
   var summary=KbmLayerSummary(source,layer,includeGesture);
   if(value.Text!=summary)value.Text=summary;
   var ink=KbmHasOverride(source,layer,includeGesture&&source=="Start")||
    includeGesture&&source=="Start"&&KbmHasOverride(source,layer)
    ?OnexRoleAccent(layer=="fn"?2:1,true):selected?palette.SelectedText:palette.Text;
   if(!(value.Foreground is SolidColorBrush brush)||brush.Color!=ink)value.Foreground=OnexSolidBrush(ink);
  }

  private void RefreshKbmMapping()
  {
   const string layer="base";
   if(!_kbmDiagrams.ContainsKey(layer))return;
   _kbmDiagramNames[layer].Text=OnexControllerSourceName(_kbmSource);
   _kbmDiagramValues[layer].Text=KbmLayerSummary(_kbmSource,layer,false);
   foreach(string id in ControllerInputs.Concat(OnexControllerOemSources))
   {
    string key=layer+":"+id;
    bool selected=id==_kbmSource;
    var palette=OnexCurrentPalette;
    var tile=_kbmTiles[key];
    tile.BorderBrush=OnexSolidBrush(selected?palette.SelectedLine:palette.Line);
    tile.BorderThickness=new Thickness(selected?2:1);
    tile.Background=OnexSolidBrush(selected?palette.Selected:palette.Raised);
    tile.Foreground=OnexSolidBrush(selected?palette.SelectedText:palette.Text);
    var main=_kbmSummaries[key];
    var fn=_kbmFnSummaries[key];
    PaintKbmLayerValue(main,id,"base",true,selected);
    PaintKbmLayerValue(fn,id,"fn",true,selected);
    PaintOnexControllerSketchButton(_kbmDiagramKeys[key],selected);
    var summary=OnexStrings.Get("Main:")+" "+main.Text+". "+OnexStrings.Get("Fn layer:")+" "+fn.Text;
    ToolTipService.SetToolTip(tile,summary);
    AutomationProperties.SetHelpText(tile,summary);
    AutomationProperties.SetHelpText(_kbmDiagramKeys[key],summary);
   }
  }
  private List<Control[]> KbmRows()
  {
   var rows=new List<Control[]>{new Control[]{_kbmGamepad,_kbmActivate}};
   var speed=_kbmSections[0];
   var precision=_kbmSections[1];
   var mapping=_kbmSections[2];
   rows.Add(new Control[]{speed.Header,precision.Header});
   if(ReferenceEquals(_kbmOpenSection,speed))
   {
    rows.Add(new Control[]{_kbmMouseSpeed,_kbmScrollSpeed});
    rows.Add(new Control[]{_kbmAcceleration,_kbmAdaptive});
   }
   if(ReferenceEquals(_kbmOpenSection,precision))
   {
    foreach(var control in new Control[]{_kbmDeadzone,_kbmHoldAcceleration,_kbmNudgeStep,_kbmPrecisionReset})
     rows.Add(new[]{control});
   }
   rows.Add(new Control[]{mapping.Header});
   if(ReferenceEquals(_kbmOpenSection,mapping))
   {
    rows.Add(new Control[]{_kbmFn});
    rows.AddRange(_kbmBaseRows);
   }
   rows.Add(new Control[]{_kbmReset});
   return rows;
  }
 }
}
