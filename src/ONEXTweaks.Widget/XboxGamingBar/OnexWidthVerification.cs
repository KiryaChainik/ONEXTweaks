// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexWidthAsync(JsonObject report)
        {
            await Task.Delay(900);UpdateLayout();
            report["compactMode"]=JsonValue.CreateBooleanValue(widget.CompactModeEnabled);
            report["beforeResize"]=JsonValue.CreateStringValue(widget.WindowBounds.ToString());
            // Observe startup sizing without a test-driven resize that could hide a regression.
            double width=widget.WindowBounds.Width;
            report["initialWidth"]=JsonValue.CreateNumberValue(width);
            report["testResizeUsed"]=JsonValue.CreateBooleanValue(false);
            report["rawPixelsPerViewPixel"]=JsonValue.CreateNumberValue(Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel);
            if(Math.Abs(width-720)>1||Math.Abs(ActualWidth-720)>1)throw new InvalidOperationException("Game Bar did not apply owner-selected width on startup: "+width+" / "+ActualWidth);
            var tabs=new JsonArray();int index=0;
            foreach(var tab in GetVisibleNavigationItems())
            {
                tab.IsChecked=true;await Task.Delay(180);UpdateLayout();
                var frame=MainNavPanel.TransformToVisual(this).TransformBounds(new Windows.Foundation.Rect(0,0,MainNavPanel.ActualWidth,MainNavPanel.ActualHeight));
                if(Math.Abs(ActualWidth-720)>1)throw new InvalidOperationException("Tab width changed at "+tab.Tag);
                if(frame.X<-.5||frame.Right>ActualWidth+.5)throw new InvalidOperationException("Navigation clipped at "+tab.Tag);
                tabs.Add(new JsonObject{{"tab",JsonValue.CreateStringValue(tab.Tag.ToString())},{"width",JsonValue.CreateNumberValue(ActualWidth)}});
                if(index==0||index==1||index==7)await SaveOnexImage("onex-wide-tab-"+index+".png",this);
                index++;
            }
            PerformanceNavItem.IsChecked=true;
            report["widgetWidth"]=JsonValue.CreateNumberValue(width);report["requestedWidth"]=JsonValue.CreateNumberValue(720);report["eightTabs"]=tabs;
        }
    }
}
