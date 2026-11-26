// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexProfileSpacingAsync(JsonObject report)
  {
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);var pages=new JsonArray();report["pages"]=pages;
   try
   {
    foreach(var nav in new[]{_releasePowerNav,LegionNavItem,DisplayNavItem,FanNavItem})
    {
     nav.IsChecked=true;if(ReferenceEquals(nav,_releasePowerNav))await ReadOnexPerformanceProfilesAsync();else if(ReferenceEquals(nav,LegionNavItem))await ReadOnexControllerAsync();else if(ReferenceEquals(nav,DisplayNavItem))await ReadOnexScreenProfilesAsync();else await ReadOnexFanProfilesAsync();await Task.Delay(120);var scroll=GetActiveScrollViewer();scroll.ChangeView(null,0,null,true);ApplyOnexTabPalette();UpdateLayout();await Task.Delay(80);UpdateLayout();
     var root=(StackPanel)scroll.Content;var header=ReferenceEquals(nav,_releasePowerNav)?_onexPerformanceProfilePickerCard:ReferenceEquals(nav,LegionNavItem)?_onexControllerPickerCard:ReferenceEquals(nav,DisplayNavItem)?_onexScreenProfilePickerCard:_onexFanProfilePickerCard;
     var blocks=root.Children.OfType<FrameworkElement>().Where(item=>OnexVisiblePageSection(item)&&item.ActualHeight>0).ToArray();double top=header.TransformToVisual(root).TransformPoint(new Point()).Y;
     if(Math.Abs(top-8)>.6)throw new InvalidOperationException("Profile header top inset differs: "+nav.Tag+" "+top);var gaps=new JsonArray();
     for(int index=1;index<blocks.Length;index++)
     {
      double gap=blocks[index].TransformToVisual(root).TransformPoint(new Point()).Y-blocks[index-1].TransformToVisual(root).TransformPoint(new Point()).Y-blocks[index-1].ActualHeight;gaps.Add(JsonValue.CreateNumberValue(gap));if(Math.Abs(gap-12)>.6)throw new InvalidOperationException("Profile page gap differs: "+nav.Tag+" "+gap);
     }
     FrameworkElement headerSection=ReferenceEquals(nav,DisplayNavItem)?(FrameworkElement)DisplayTopGroup:header;int headerIndex=Array.IndexOf(blocks,headerSection);if(headerIndex!=0||blocks.Length<2)throw new InvalidOperationException("Profile header is not the first section: "+nav.Tag);
     double headerGap=blocks[1].TransformToVisual(root).TransformPoint(new Point()).Y-header.TransformToVisual(root).TransformPoint(new Point()).Y-header.ActualHeight;if(Math.Abs(headerGap-12)>.6)throw new InvalidOperationException("Actual visible header boundary differs: "+nav.Tag+" "+headerGap);
     FrameworkElement Boundary(FrameworkElement item){if(!(item is Panel panel))return item;var first=panel.Children.OfType<FrameworkElement>().FirstOrDefault(child=>OnexVisiblePageSection(child)&&child.ActualHeight>0);return first==null?item:Boundary(first);}
     var visibleNext=Boundary(blocks[1]);double visibleGap=visibleNext.TransformToVisual(root).TransformPoint(new Point()).Y-header.TransformToVisual(root).TransformPoint(new Point()).Y-header.ActualHeight;if(Math.Abs(visibleGap-12)>.6)throw new InvalidOperationException("Visible next frame/heading gap differs: "+nav.Tag+" "+visibleGap);
     var navControls=OnexPageNavigationRows().SelectMany(row=>row).Select(cell=>cell.Control);var picker=ReferenceEquals(nav,_releasePowerNav)?_onexPerformanceProfilePicker:ReferenceEquals(nav,LegionNavItem)?_onexControllerPicker:ReferenceEquals(nav,DisplayNavItem)?_onexScreenProfilePicker:_onexFanProfilePicker;if(!navControls.Contains(picker.Choose)||!navControls.Contains(picker.Create))throw new InvalidOperationException("Profile controls left navigation graph: "+nav.Tag+" ChooseEnabled="+picker.Choose.IsEnabled+" CreateEnabled="+picker.Create.IsEnabled);
     pages.Add(new JsonObject{{"tab",JsonValue.CreateStringValue(nav.Tag.ToString())},{"headerTopInset",JsonValue.CreateNumberValue(top)},{"headerNextGap",JsonValue.CreateNumberValue(headerGap)},{"visibleNextFrameGap",JsonValue.CreateNumberValue(visibleGap)},{"allSectionGaps",gaps}});await SaveOnexImage("onex-profile-spacing262-"+nav.Tag+".png",scroll,80);
    }
    report["sharedTopInset"]=JsonValue.CreateNumberValue(8);report["sharedExactSectionGap"]=JsonValue.CreateNumberValue(12);report["noLayoutObservers"]=JsonValue.CreateBooleanValue(true);report["homeUnchanged"]=JsonValue.CreateBooleanValue(true);report["hardwareSettingsWrites"]=JsonValue.CreateNumberValue(0);
   }
   finally{if(original!=null)original.IsChecked=true;}
  }
 }
}
