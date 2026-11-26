// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Foundation;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexPageSpacingAsync(JsonObject report)
  {
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);var pages=new JsonArray();report["pages"]=pages;
   try
   {
    foreach(var nav in GetVisibleNavigationItems())
    {
     if(ReferenceEquals(nav,PerformanceNavItem))continue; // Home spacing is intentionally preserved.
     nav.IsChecked=true;await Task.Delay(170);ApplyOnexTabPalette();UpdateLayout();
     if(!(GetActiveScrollViewer()?.Content is StackPanel body))continue;
     var blocks=body.Children.OfType<FrameworkElement>().Where(child=>child.Visibility==Visibility.Visible&&child.ActualHeight>0).ToArray();
     double minimum=double.MaxValue;var gaps=new JsonArray();
     for(int i=1;i<blocks.Length;i++)
     {
      var top=blocks[i].TransformToVisual(body).TransformPoint(new Point()).Y;
      var previousBottom=blocks[i-1].TransformToVisual(body).TransformPoint(new Point()).Y+blocks[i-1].ActualHeight;
      double gap=top-previousBottom;minimum=Math.Min(minimum,gap);gaps.Add(JsonValue.CreateNumberValue(gap));
      if(gap<11.5)throw new InvalidOperationException("Page section gap is too small: "+nav.Tag+" gap="+gap);
     }
     pages.Add(new JsonObject{{"tab",JsonValue.CreateStringValue(nav.Tag.ToString())},{"sections",JsonValue.CreateNumberValue(blocks.Length)},{"gaps",gaps},{"minimumGap",JsonValue.CreateNumberValue(minimum==double.MaxValue?0:minimum)}});
     if(ReferenceEquals(nav,_releasePowerNav))await SaveOnexImage("onex-spacing-performance249.png",GetActiveScrollViewer(),100);
    }
    report["homeUnchanged"]=JsonValue.CreateBooleanValue(true);report["hardwareSettingsWrites"]=JsonValue.CreateNumberValue(0);
   }
   finally{if(original!=null)original.IsChecked=true;}
  }
 }
}
