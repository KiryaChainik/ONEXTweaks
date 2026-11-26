// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private sealed class OnexMetricPaintStamp{internal int Theme=-1,Font=-1;internal OnexFocusLayer Scope;}
  private readonly ConditionalWeakTable<Grid,OnexMetricPaintStamp> _onexMetricPaintStamps=new ConditionalWeakTable<Grid,OnexMetricPaintStamp>();
  private void OnexOverlayMetricRow_Loaded(object sender,RoutedEventArgs args)
  {if(sender is Grid row&&row.Name=="OnexMetricRow")PaintOnexOverlayMetricRow(row);}
  private void PaintOnexOverlayMetricRows()
  {
   if(OSDItemsControl==null)return;
   for(int index=0;index<OSDItemsControl.Items.Count;index++){var row=OnexOverlayMetricRow(OSDItemsControl,index);if(row!=null)PaintOnexOverlayMetricRow(row);}
  }
  private void PaintOnexOverlayMetricRow(Grid row)
  {
   _onexControlLayers.TryGetValue(OSDItemsControl,out var scope);scope=scope??_onexMainFocusLayer;
   var stamp=_onexMetricPaintStamps.GetValue(row,_=>new OnexMetricPaintStamp());
   if(stamp.Theme==_onexThemeEpoch&&stamp.Font==_onexUiFontEpoch&&ReferenceEquals(stamp.Scope,scope))return;
   var theme=_onexAppliedTheme??OnexPalette.CreateTheme();var accent=OnexRoleAccent(1);
   foreach(var element in row.Children)
   {
    if(element is TextBlock label){SetOnexThemeBrush(label,TextBlock.ForegroundProperty,theme.TextPrimary);ApplyOnexUiFontNode(label);}
    if(!(element is Control control))continue;
    if(scope!=null&&(!_onexControlLayers.TryGetValue(control,out var prior)||!ReferenceEquals(prior,scope))){_onexControlLayers.Remove(control);_onexControlLayers.Add(control,scope);}
    if(control is CheckBox check){ConfigureOnexFocus(check);OnexAccentResources(check,accent);}
    else if(control is Button button)
    {
     // These glyph-only row actions need no eager input delegates/resources.
     // The owned host configures them when they first get focus. The graph
     // also configures its actual reachable controls when gamepad input begins.
     button.CornerRadius=new CornerRadius(8);button.UseSystemFocusVisuals=false;
     SetOnexThemeBrush(button,Control.ForegroundProperty,button.Content as string=="?"?accent:theme.TextPrimary);
    }
   }
   stamp.Theme=_onexThemeEpoch;stamp.Font=_onexUiFontEpoch;stamp.Scope=scope;
  }
 }
}
