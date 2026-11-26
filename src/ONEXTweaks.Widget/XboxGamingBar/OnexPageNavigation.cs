// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private sealed class OnexFocusCell { internal Control Control;internal Rect Bounds;internal double X=>Bounds.Left+Bounds.Width/2;internal double Y=>Bounds.Top+Bounds.Height/2; }
  private FrameworkElement OnexNavigationRoot()=>GetActiveScrollViewer()?.Content as FrameworkElement;
  private static Grid OnexOverlayMetricRow(ItemsControl items,int index)
  {
   if(items==null||index<0||index>=items.Items.Count)return null;
   var presenter=items.ContainerFromIndex(index) as ContentPresenter;
   if(presenter==null||!presenter.IsLoaded||Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(presenter)==0)return null;
   var row=Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(presenter,0) as Grid;
   return row?.Name=="OnexMetricRow"?row:null;
  }
  private static IEnumerable<DependencyObject> OnexNavigationChildren(DependencyObject root)
  {
   if(root is ItemsControl items&&items.Name=="OSDItemsControl")
   {
    for(int index=0;index<items.Items.Count;index++){var row=OnexOverlayMetricRow(items,index);if(row!=null)yield return row;}
   }
   else foreach(var child in ThemeContentChildren(root))yield return child;
  }
  private static IEnumerable<Control> OnexNavigableContent(DependencyObject root)
  {
   if(root==null||root is FrameworkElement hidden&&(hidden.Visibility!=Visibility.Visible||!hidden.IsHitTestVisible))yield break;
   if(root is Control control){if(!control.IsEnabled)yield break;if(control.IsLoaded&&control.IsTabStop&&control.ActualWidth>0&&control.ActualHeight>0&&!(control is ScrollViewer)&&!(control is UserControl))yield return control;
    // A native control owns its template and selection. Only traverse containers' public content.
    if(control is ButtonBase||control is Slider||control is ToggleSwitch||control is ComboBox||control is TextBox)yield break;}
   foreach(var child in OnexNavigationChildren(root))foreach(var item in OnexNavigableContent(child))yield return item;
  }
  private sealed class OnexNavigationCache
  {
   internal bool Dirty=true;internal double Width,Height;internal List<List<OnexFocusCell>> Rows;
  }
  private readonly Dictionary<FrameworkElement,OnexNavigationCache> _onexNavigationCaches=new Dictionary<FrameworkElement,OnexNavigationCache>();
  private readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement,HashSet<OnexNavigationCache>> _onexNavigationWatches=new System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement,HashSet<OnexNavigationCache>>();
  private int _onexNavigationBuilds;
  private void WatchOnexNavigation(DependencyObject node,OnexNavigationCache cache)
  {
   if(node is FrameworkElement element)
   {
    if(!_onexNavigationWatches.TryGetValue(element,out var watchers))
    {
     watchers=new HashSet<OnexNavigationCache>();_onexNavigationWatches.Add(element,watchers);var observed=watchers;
     void Invalidate(){foreach(var value in observed)value.Dirty=true;}
     element.RegisterPropertyChangedCallback(UIElement.VisibilityProperty,(s,p)=>Invalidate());
     element.RegisterPropertyChangedCallback(UIElement.IsHitTestVisibleProperty,(s,p)=>Invalidate());
     if(element is Control control)
     {
      control.RegisterPropertyChangedCallback(Control.IsEnabledProperty,(s,p)=>Invalidate());
      control.RegisterPropertyChangedCallback(Control.IsTabStopProperty,(s,p)=>Invalidate());
      control.SizeChanged+=(s,e)=>Invalidate();
      control.Loaded+=(s,e)=>Invalidate();control.Unloaded+=(s,e)=>Invalidate();
     }
    }
    watchers.Add(cache);
   }
   foreach(var child in OnexNavigationChildren(node))WatchOnexNavigation(child,cache);
  }
  private List<List<OnexFocusCell>> OnexPageNavigationRows()
  {
   var root=OnexNavigationRoot();var rows=new List<List<OnexFocusCell>>();if(root==null||root.Visibility!=Visibility.Visible)return rows;
   if(!_onexNavigationCaches.TryGetValue(root,out var cache)){cache=new OnexNavigationCache();_onexNavigationCaches.Add(root,cache);}
   if(!cache.Dirty&&cache.Rows!=null&&cache.Width==root.ActualWidth&&cache.Height==root.ActualHeight&&cache.Rows.SelectMany(x=>x).All(x=>x.Control.IsLoaded&&x.Control.IsEnabled&&x.Control.IsTabStop))return cache.Rows;
   root.UpdateLayout();WatchOnexNavigation(root,cache);var cells=new List<OnexFocusCell>();
   foreach(var control in OnexNavigableContent(root).Distinct())try{ConfigureOnexFocus(control);var box=control.TransformToVisual(root).TransformBounds(new Rect(0,0,control.ActualWidth,control.ActualHeight));if(box.Width>0&&box.Height>0)cells.Add(new OnexFocusCell{Control=control,Bounds=box});}catch(ArgumentException){}
   foreach(var cell in cells.OrderBy(value=>value.Y).ThenBy(value=>value.X))
   {var row=rows.LastOrDefault();if(row==null||Math.Abs(row.Average(value=>value.Y)-cell.Y)>16){row=new List<OnexFocusCell>();rows.Add(row);}row.Add(cell);}
   foreach(var row in rows)row.Sort((a,b)=>a.X.CompareTo(b.X));cache.Rows=rows;cache.Width=root.ActualWidth;cache.Height=root.ActualHeight;cache.Dirty=false;_onexNavigationBuilds++;return rows;
  }
  private static int OnexNavigationDirection(VirtualKey key)
  {
   switch(key){case VirtualKey.Up:case VirtualKey.GamepadDPadUp:case VirtualKey.GamepadLeftThumbstickUp:return -2;
    case VirtualKey.Down:case VirtualKey.GamepadDPadDown:case VirtualKey.GamepadLeftThumbstickDown:return 2;
    case VirtualKey.Left:case VirtualKey.GamepadDPadLeft:case VirtualKey.GamepadLeftThumbstickLeft:return -1;
    case VirtualKey.Right:case VirtualKey.GamepadDPadRight:case VirtualKey.GamepadLeftThumbstickRight:return 1;default:return 0;}
  }
  private Control OnexPageNavigationTarget(List<List<OnexFocusCell>> rows,Control source,int direction)
  {
   if(_releaseOsdNav?.IsChecked==true&&Math.Abs(direction)==2&&OSDItemsControl.IsEnabled)
   {
    // The last full-width editor and the right-aligned metric checkboxes
    // have different centres. Enter the list at its checkbox, not the help
    // button that happens to be closest to the editor's centre.
    var first=OnexOverlayMetricRow(OSDItemsControl,0)?.Children.OfType<CheckBox>().FirstOrDefault();
    var last=OsdPanelNavOrder().LastOrDefault(control=>control.IsEnabled&&OnexOverlayControlVisible(control));
    if(first?.IsEnabled==true&&last!=null)
    {
     if(direction>0&&ReferenceEquals(source,last))return first;
     if(direction<0&&ReferenceEquals(source,first))return last;
    }
   }
   for(int r=0;r<rows.Count;r++){int c=rows[r].FindIndex(cell=>ReferenceEquals(cell.Control,source));if(c<0)continue;
    if(Math.Abs(direction)==1)return rows[r][Math.Max(0,Math.Min(rows[r].Count-1,c+direction))].Control;
    int next=r+(direction>0?1:-1);if(next<0||next>=rows.Count)return source;double x=rows[r][c].X;return rows[next].OrderBy(cell=>Math.Abs(cell.X-x)).First().Control;}
   return null;
  }
  private bool HandleOnexPageNavigation(KeyRoutedEventArgs args)
  {
   if(OnexChoiceOwnsInput(args.Key))return false;
   if(args.Handled||!OnexFrontend.UsesOwnBackend||_onexDetailsDialog!=null||_onexOpenMenus.Any(menu=>menu.IsOpen))return false;
   int direction=OnexNavigationDirection(args.Key);if(direction==0)return false;var source=FocusManager.GetFocusedElement() as Control;if(source==null)return false;
   var rows=OnexPageNavigationRows();if(rows.SelectMany(row=>row).Any(cell=>cell.Control is ComboBox choice&&choice.IsDropDownOpen))return false;
   if(Math.Abs(direction)==1&&source is TextBox)return false;
   var target=OnexPageNavigationTarget(rows,source,direction);if(target==null)return false;
   // Directions are consumed even at boundaries: focus never escapes into hidden legacy controls or top tabs.
   args.Handled=true;if(!ReferenceEquals(target,source)){ConfigureOnexFocus(target);target.Focus(FocusState.Keyboard);target.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});}return true;
  }
 }
}
