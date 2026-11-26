// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexReleaseFocusAsync()
        {
            var report=new JsonObject();
            var toggle=(ToggleSwitch)_releaseHome.FindName("LightPl2Toggle");
            bool original=toggle.IsOn;
            void Assert(bool valid,string message){if(!valid)throw new InvalidOperationException(message);}
            void Focus(Control control,string name)
            {
                Assert(control!=null&&control.IsEnabled&&control.Focus(FocusState.Keyboard),"Cannot focus "+name);
                Assert(ReferenceEquals(FocusManager.GetFocusedElement(),control),"Wrong focus for "+name);
            }
            try
            {
                if(App.OnexPowerGuardTest){await VerifyOnexPowerGuardAsync(report);report["passed"]=JsonValue.CreateBooleanValue(true);return;}
                if(App.OnexLedOnly){await VerifyOnexSettingsPanelsAsync(report,true);report["passed"]=JsonValue.CreateBooleanValue(true);return;}
                var tags=new JsonArray();
                var expected=OnexTabIds;
                var items=GetVisibleNavigationItems();
                Assert(items.Count==expected.Length&&items.Select(v=>v.Tag.ToString()).SequenceEqual(expected),"Navigation order mismatch");
                foreach(var item in items)
                {
                    item.IsChecked=true;await Task.Delay(120);UpdateLayout();
                    var page=GetActiveScrollViewer();Assert(page!=null&&page.Visibility==Visibility.Visible,"No visible content for "+item.Tag);
                    if(item.Tag.ToString()=="Legion"||item.Tag.ToString()=="Display"||item.Tag.ToString()=="Fan")
                        Assert(FocusManager.FindFirstFocusableElement(page)==null,"Unconnected panel still contains actionable focus targets: "+item.Tag);
                    tags.Add(JsonValue.CreateStringValue(item.Tag.ToString()));
                }
                report["tabs"]=tags;
                Assert(QuickMetricsGrid.ColumnDefinitions.Count==7&&QuickMetricsGrid.RowDefinitions.Count==1&&_onexStatsValues.Count==7,"Seven-stat single row missing");
                var savedPacket=_onexStatsPacket;
                try
                {
                    ApplyOnexStatsPacket(new JsonObject{{"timeRemaining",JsonValue.CreateNumberValue(125)},{"isCharging",JsonValue.CreateBooleanValue(false)}});
                    Assert(_onexStatsValues["timeRemaining"].Text=="~2:05","Battery time format incorrect");
                    ApplyOnexStatsPacket(new JsonObject{{"timeRemaining",JsonValue.CreateNumberValue(125)},{"isCharging",JsonValue.CreateBooleanValue(true)}});
                    Assert(_onexStatsValues["timeRemaining"].Text==OnexChargingIndicator,"External power indicator missing on AC");
                    ApplyOnexStatsPacket(new JsonObject());
                    Assert(_onexStatsValues["timeRemaining"].Text=="—","Stale battery estimate survived missing reading");
                }
                finally{if(savedPacket!=null)ApplyOnexStatsPacket(savedPacket);}
                report["batteryTimeFormatAndUnavailable"]=JsonValue.CreateBooleanValue(true);
                var stats=new JsonArray();
                foreach(var spec in OnexStats)stats.Add(new JsonObject{{"label",JsonValue.CreateStringValue(spec[1])},{"value",JsonValue.CreateStringValue(_onexStatsValues[spec[0]].Text)}});
                report["stats"]=stats;
                report["powerTabRestored"]=JsonValue.CreateBooleanValue(_releasePowerNav!=null&&MainNavPanel.Children.Contains(_releasePowerNav));
                PerformanceNavItem.IsChecked=true;await Task.Delay(150);UpdateLayout();
                double? rowTop=null;
                var dimensions=new JsonArray();
                foreach(var spec in OnexStats)
                {
                    var cell=(StackPanel)_onexStatsCells[spec[0]];
                    var value=(TextBlock)cell.Children[0];var label=(TextBlock)cell.Children[1];
                    var natural=new TextBlock{Text=label.Text,FontSize=label.FontSize};
                    natural.Measure(new Windows.Foundation.Size(double.PositiveInfinity,double.PositiveInfinity));
                    double top=cell.TransformToVisual(QuickMetricsGrid).TransformPoint(new Windows.Foundation.Point()).Y;
                    Assert(value.FontSize==16&&label.FontSize==11,"Stats font size changed");
                    Assert(!rowTop.HasValue||Math.Abs(top-rowTop.Value)<1,"Stats wrapped to another row");rowTop=top;
                    Assert(natural.DesiredSize.Width<=cell.ActualWidth+1,"Stats label clipped: "+label.Text);
                    dimensions.Add(new JsonObject{{"label",JsonValue.CreateStringValue(label.Text)},{"width",JsonValue.CreateNumberValue(cell.ActualWidth)},{"naturalLabelWidth",JsonValue.CreateNumberValue(natural.DesiredSize.Width)},{"top",JsonValue.CreateNumberValue(top)}});
                }
                report["singleRow"]=JsonValue.CreateBooleanValue(true);report["statsDimensions"]=dimensions;
                report["valueFontSize"]=JsonValue.CreateNumberValue(16);report["labelFontSize"]=JsonValue.CreateNumberValue(11);
                report["statsColumnSpacing"]=JsonValue.CreateNumberValue(QuickMetricsGrid.ColumnSpacing);
                Focus(PerformanceNavItem,"home tab");FocusFirstTabContentElement();
                Assert(ReferenceEquals(FocusManager.GetFocusedElement(),_releaseHome.PowerInput),"Home entry missed TDP");
                toggle.IsOn=true;UpdateLayout();
                var secondary=(Slider)_releaseHome.FindName("OnexPl2Slider");
                var first=OnexFirstQuickControl();
                Assert(FocusXYTarget(_releaseHome.PowerInput.XYFocusDown)&&ReferenceEquals(FocusManager.GetFocusedElement(),secondary),"PL1 -> PL2 failed");
                Assert(FocusXYTarget(secondary.XYFocusDown)&&ReferenceEquals(FocusManager.GetFocusedElement(),RailBrightnessSlider),"PL2 -> media failed");
                toggle.IsOn=false;Focus(_releaseHome.PowerInput,"PL1");
                Assert(FocusXYTarget(_releaseHome.PowerInput.XYFocusDown)&&ReferenceEquals(FocusManager.GetFocusedElement(),RailBrightnessSlider),"Hidden PL2 was not skipped");
                report["homeEntryAndChain"]=JsonValue.CreateBooleanValue(true);
                QuickNavItem.IsChecked=true;await Task.Delay(150);UpdateLayout();
                Assert(PerformanceNavItem.IsChecked==true&&QuickNavItem.Visibility==Visibility.Collapsed,"Legacy tiles route did not select the merged home");
                Focus(first,"first tile");
                Assert(FocusXYTarget(first.XYFocusUp)&&ReferenceEquals(FocusManager.GetFocusedElement(),RailBrightnessSlider),"Tile -> media failed");
                Assert(FocusXYTarget(RailBrightnessSlider.XYFocusUp)&&ReferenceEquals(FocusManager.GetFocusedElement(),_releaseHome.PowerInput),"Media -> TDP failed");
                Focus(RailBrightnessSlider,"brightness");
                Assert(FocusXYTarget(RailBrightnessSlider.XYFocusRight)&&ReferenceEquals(FocusManager.GetFocusedElement(),RailMuteButton),"Brightness -> mute failed");
                Assert(FocusXYTarget(RailMuteButton.XYFocusRight)&&ReferenceEquals(FocusManager.GetFocusedElement(),RailVolumeSlider),"Mute -> volume failed");
                Assert(FocusXYTarget(RailVolumeSlider.XYFocusDown)&&ReferenceEquals(FocusManager.GetFocusedElement(),first),"Volume -> tiles failed");
                Assert(_releaseHome.HasMedia(RailBrightnessSlider,RailVolumeSlider),"Merged home media host mismatch");
                Assert(ReferenceEquals(QuickSettingsTilesContainer.Parent,_releaseHome.TilesHost),"Tiles were not moved into home");
                Assert(_releaseHome.DuplicateCardsHidden,"Duplicate cards are visible");
                double mediaBottom=_releaseHome.MediaSection.TransformToVisual(_releaseHome).TransformPoint(new Windows.Foundation.Point()).Y+_releaseHome.MediaSection.ActualHeight;
                double powerTop=_releaseHome.PowerSection.TransformToVisual(_releaseHome).TransformPoint(new Windows.Foundation.Point()).Y;
                double powerBottom=powerTop+_releaseHome.PowerSection.ActualHeight;
                double tilesTop=QuickSettingsTilesContainer.TransformToVisual(_releaseHome).TransformPoint(new Windows.Foundation.Point()).Y;
                double statsTop=QuickMetricsRow.TransformToVisual(_releaseHome).TransformPoint(new Windows.Foundation.Point()).Y;
                double statsBottom=statsTop+QuickMetricsRow.ActualHeight;
                double mediaTop=_releaseHome.MediaSection.TransformToVisual(_releaseHome).TransformPoint(new Windows.Foundation.Point()).Y;
                Assert(ReferenceEquals(QuickMetricsRow.Parent,_releaseHome.StatsHost)&&QuickMetricsRow.Visibility==Visibility.Visible,"Stats are not visible at the top");
                Assert(powerTop>=statsBottom&&powerTop-statsBottom<=12&&mediaTop>=powerBottom&&mediaTop-powerBottom<=12&&tilesTop>=mediaBottom,"Expected stats -> TDP -> media -> tiles");
                report["order"]=JsonValue.CreateStringValue("stats,tdp,brightness-volume,tiles");
                report["statsTop"]=JsonValue.CreateNumberValue(statsTop);report["statsBottom"]=JsonValue.CreateNumberValue(statsBottom);report["mediaTop"]=JsonValue.CreateNumberValue(mediaTop);
                foreach(string id in new[]{"CPUBoost","Overlay","PowerMode","FPSCombined","OnexFanMode"})
                    Assert(qsTileMap.TryGetValue(id,out var tile)&&tile.IsVisible&&!ShouldSkipTile(tile)&&tile.TileButton!=null,"Required home tile missing: "+id);
                report["combinedHome"]=JsonValue.CreateBooleanValue(true);
                report["duplicateCardsHidden"]=JsonValue.CreateBooleanValue(true);
                report["mediaBottom"]=JsonValue.CreateNumberValue(mediaBottom);report["tdpTop"]=JsonValue.CreateNumberValue(powerTop);report["tdpBottom"]=JsonValue.CreateNumberValue(powerBottom);report["tilesTop"]=JsonValue.CreateNumberValue(tilesTop);
                Assert(qsTileDefinitions.All(t=>t.StateText==null||t.StateText.Text!="Pending"),"Pending state survived");
                report["tileEntryAndMediaChain"]=JsonValue.CreateBooleanValue(true);
                report["noPending"]=JsonValue.CreateBooleanValue(true);
                Assert(_onexLegacyPowerGate != null && _onexLegacyPowerGate.Visibility == Visibility.Collapsed, "Legacy power page is not isolated from Home");
                PerformanceScrollViewer.Visibility = Visibility.Visible;
                UpdateLayout();
                Assert(_onexLegacyPowerGate.Visibility == Visibility.Collapsed, "Legacy update exposed the old page behind Home");
                PerformanceScrollViewer.Visibility = Visibility.Collapsed;
                report["legacyPageIsolated"] = JsonValue.CreateBooleanValue(true);
                await VerifyOnexHomeControlsAsync(report);
                var navFocus = new JsonArray();
                foreach (var item in GetVisibleNavigationItems())
                {
                    Focus(item, "navigation focus ring"); item.UpdateLayout();
                    var border = FindOnexNavFocusBorder(item);
                    Assert(border != null, "Navigation focus border missing");
                    var origin = border.TransformToVisual(item).TransformPoint(new Windows.Foundation.Point());
                    Assert(origin.X >= -0.1 && origin.Y >= -0.1 && origin.X + border.ActualWidth <= item.ActualWidth + 0.1 && origin.Y + border.ActualHeight <= item.ActualHeight + 0.1, "Navigation focus border leaves button bounds");
                    navFocus.Add(new JsonObject { ["tab"] = JsonValue.CreateStringValue(item.Tag.ToString()), ["left"] = JsonValue.CreateNumberValue(origin.X), ["top"] = JsonValue.CreateNumberValue(origin.Y) });
                }
                report["navFocusWithinBounds"] = navFocus;
                PerformanceNavItem.IsChecked = true; Focus(PerformanceNavItem, "home navigation");
                await SaveOnexImage("onex-focus-tiles.png");
                report["passed"]=JsonValue.CreateBooleanValue(true);
            }
            catch(Exception ex){report["passed"]=JsonValue.CreateBooleanValue(false);report["error"]=JsonValue.CreateStringValue(ex.ToString());}
            finally
            {
                toggle.IsOn=original;PerformanceNavItem.IsChecked=true;FocusActiveTab();
                report["physicalControllerVerified"]=JsonValue.CreateBooleanValue(false);
                report["inputScope"]=JsonValue.CreateStringValue("Native UWP focus targets and the production navigation helpers; physical gamepad and in-game focus remain pending");
                var folder=ApplicationData.Current.LocalFolder;
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-focus-verification.json",CreationCollisionOption.ReplaceExisting),report.Stringify());
                await(await folder.GetFileAsync("onex-focus-verification-armed.json")).DeleteAsync();
            }
        }
        private async Task VerifyOnexHintAlignmentAsync(JsonObject report)
        {
            PerformanceNavItem.IsChecked=true;await Task.Delay(250);UpdateLayout();
            Windows.Foundation.Rect Bounds(FrameworkElement element)=>element.TransformToVisual(this).TransformBounds(new Windows.Foundation.Rect(0,0,element.ActualWidth,element.ActualHeight));
            var card=Bounds(QuickMetricsRow);var checks=new JsonArray();int index=0;
            foreach(var tab in GetVisibleNavigationItems())
            {
                tab.IsChecked=true;await Task.Delay(150);UpdateLayout();
                var left=Bounds(OnexLtHint);var right=Bounds(OnexRtHint);var group=Bounds(OnexRightNavHints);var nav=Bounds(MainNavPanel);
                if(Math.Abs(left.Left-card.Left)>.5||Math.Abs(group.Right-card.Right)>.5||left.Right>nav.Left||right.Left<nav.Right||right.Right>card.Right+.5||Math.Abs(nav.Left+nav.Width/2-ActualWidth/2)>.5)throw new InvalidOperationException("Trigger hints leave shared content edges or shift navigation at "+tab.Tag);
                double center=nav.Top+nav.Height/2;
                if(Math.Abs(left.Top+left.Height/2-center)>.5||Math.Abs(right.Top+right.Height/2-center)>.5)throw new InvalidOperationException("Trigger hints are not vertically centered");
                checks.Add(new JsonObject{{"tab",JsonValue.CreateStringValue(tab.Tag.ToString())},{"cardLeft",JsonValue.CreateNumberValue(card.Left)},{"cardRight",JsonValue.CreateNumberValue(card.Right)},{"ltLeft",JsonValue.CreateNumberValue(left.Left)},{"rtRight",JsonValue.CreateNumberValue(right.Right)},{"rightGroupRight",JsonValue.CreateNumberValue(group.Right)},{"navigationCenter",JsonValue.CreateNumberValue(nav.Left+nav.Width/2)}});
                if(index==0||index==1||index==7)await SaveOnexImage("onex-aligned-hints-"+index+".png",this);index++;
            }
            if(index!=7)throw new InvalidOperationException("Expected seven visible tabs");
            PerformanceNavItem.IsChecked=true;report["visibleTabs"]=checks;report["hintsInsideSharedContentEdges"]=JsonValue.CreateBooleanValue(true);report["navigationCentered"]=JsonValue.CreateBooleanValue(true);
        }
        private async Task VerifyOnexHeaderConnectionAsync(JsonObject report)
        {
            if(FindName("HelperStatusIndicator")!=null||OnexRightNavHints.Children.Count!=1||OnexRightNavHints.Children[0]!=OnexRtHint)throw new InvalidOperationException("Redundant helper dot remains in the header");
            await VerifyOnexHintAlignmentAsync(report);
            var states=new JsonArray();
            try
            {
                foreach(var state in new[]{BannerState.Disconnected,BannerState.Reconnecting,BannerState.Syncing})
                {
                    ShowConnectionBanner(state);UpdateLayout();
                    if(ConnectionStatusBanner.Visibility!=Visibility.Visible||BannerDimOverlay.Visibility!=Visibility.Visible||string.IsNullOrWhiteSpace(ConnectionStatusText.Text)||string.IsNullOrWhiteSpace(ConnectionStatusIcon.Glyph))throw new InvalidOperationException("Connection error feedback was lost");
                    var box=ConnectionStatusBanner.TransformToVisual(this).TransformBounds(new Windows.Foundation.Rect(0,0,ConnectionStatusBanner.ActualWidth,ConnectionStatusBanner.ActualHeight));
                    var hit=Windows.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(new Windows.Foundation.Point(box.Left+box.Width/2,box.Top+box.Height/2),this).FirstOrDefault();
                    DependencyObject node=hit;while(node!=null&&node!=ConnectionStatusBanner)node=Windows.UI.Xaml.Media.VisualTreeHelper.GetParent(node);
                    if(node!=ConnectionStatusBanner)throw new InvalidOperationException("Connection message is covered by another screen");
                    states.Add(new JsonObject{{"state",JsonValue.CreateStringValue(state.ToString())},{"message",JsonValue.CreateStringValue(ConnectionStatusText.Text)},{"foregroundHitTestPassed",JsonValue.CreateBooleanValue(true)}});
                    if(state==BannerState.Disconnected)await SaveOnexImage("onex-connection-feedback.png",this);
                }
            }
            finally{HideConnectionBanner();}
            await SaveOnexImage("onex-header-without-dot.png",this);
            report["headerDotRemoved"]=JsonValue.CreateBooleanValue(true);report["connectionFeedback"]=states;report["feedbackStateSimulated"]=JsonValue.CreateBooleanValue(true);report["helperDisconnectedForTest"]=JsonValue.CreateBooleanValue(false);
        }
        private async Task VerifyOnexChargingMetricsAsync(JsonObject report)
        {
            var original=_onexStatsPacket;double originalHeight=QuickMetricsRow.ActualHeight;var cases=new JsonArray();
            async Task Check(string name,JsonObject packet,string power,string time)
            {
                bool captured=false;
                for(int attempt=0;attempt<5&&!captured;attempt++)
                {
                    ApplyOnexStatsPacket(packet);UpdateLayout();
                    if(_onexStatsValues["systemPowerWatts"].Text!=power||_onexStatsValues["timeRemaining"].Text!=time)throw new InvalidOperationException("Wrong charging metrics state: "+name);
                    if(Math.Abs(QuickMetricsRow.ActualHeight-originalHeight)>.5||_onexStatsValues.Values.Any(t=>t.FontSize!=16))throw new InvalidOperationException("Charging glyph changed metrics size");
                    if(name=="charging-missing"||name=="discharging")await SaveOnexImage("onex-metrics-"+name+".png",QuickMetricsRow,0);
                    captured=ReferenceEquals(_onexStatsPacket,packet);
                }
                if(!captured)throw new InvalidOperationException("Live telemetry interrupted scenario rendering");
                cases.Add(new JsonObject{{"state",JsonValue.CreateStringValue(name)},{"power",JsonValue.CreateStringValue(power)},{"time",JsonValue.CreateStringValue(time)},{"height",JsonValue.CreateNumberValue(QuickMetricsRow.ActualHeight)}});
            }
            JsonObject Packet(bool charging)=>new JsonObject{{"isCharging",JsonValue.CreateBooleanValue(charging)},{"cpuUsage",JsonValue.CreateNumberValue(20)},{"cpuTemp",JsonValue.CreateNumberValue(50)},{"memoryUsage",JsonValue.CreateNumberValue(38)},{"apuPowerWatts",JsonValue.CreateNumberValue(4.2)},{"batteryLevel",JsonValue.CreateNumberValue(55)}};
            try
            {
                await Check("charging-missing",Packet(true),OnexChargingIndicator,OnexChargingIndicator);
                var ac=Packet(true);ac["systemPowerWatts"]=JsonValue.CreateNumberValue(30);ac["timeRemaining"]=JsonValue.CreateNumberValue(125);await Check("charging-available-power",ac,"30.0 W",OnexChargingIndicator);
                var dc=Packet(false);dc["systemPowerWatts"]=JsonValue.CreateNumberValue(17.4);dc["timeRemaining"]=JsonValue.CreateNumberValue(125);await Check("discharging",dc,"17.4 W","~2:05");
                await Check("unknown",new JsonObject(),"—","—");
            }
            finally{ApplyOnexStatsPacket(original??new JsonObject());}
            report["telemetryScenarios"]=cases;report["sensorDataSimulated"]=JsonValue.CreateBooleanValue(true);report["chargingHardwareChanged"]=JsonValue.CreateBooleanValue(false);report["metricsSizePreserved"]=JsonValue.CreateBooleanValue(true);
        }
        private static Border FindOnexNavFocusBorder(DependencyObject root)
        {
            if (root is Border border && border.Name == "FocusBorder") return border;
            for (int i = 0; i < Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var found = FindOnexNavFocusBorder(Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }
    }
}
