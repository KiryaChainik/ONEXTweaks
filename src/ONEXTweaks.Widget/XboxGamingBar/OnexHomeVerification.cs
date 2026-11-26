// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Automation.Peers;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private DispatcherTimer _onexPointerProbe;
        private async Task VerifyOnexHomeControlsAsync(JsonObject report)
        {
            foreach(string id in new[]{"ChargeLimiter","CPUBoost","PowerMode","Power","Fullscreen","OptiScaler","ReShade"})
                if(!qsTileMap[id].IsActionable)throw new InvalidOperationException("Home tile not actionable: "+id);
            var tile=qsTileMap["ChargeLimiter"];
            int before=_onexChargePercent;bool enabled=_onexChargeOn;
            var open=ShowOnexTileDetailsAsync(tile.TileButton,"ChargeLimiter");
            for(int i=0;i<50&&_onexDetailsDialog==null;i++)await Task.Delay(100);
            if(_onexDetailsDialog==null)throw new InvalidOperationException("Charge details did not open");
            var slider=FindOnexSettingsControl<Slider>(_onexDetailsDialog.Content as DependencyObject);
            if(slider.Minimum!=30||slider.Maximum!=100||slider.StepFrequency!=5||slider.SmallChange!=5)throw new InvalidOperationException("Charge range/step mismatch");
            _onexDetailsDialog.Hide();await open;
            if(_onexChargePercent!=before||_onexChargeOn!=enabled)throw new InvalidOperationException("Opening/cancelling details changed charge limit");
            report["chargeDetailsRange"]=JsonValue.CreateStringValue("30–100%, step 5");
            report["chargeCancelUnchanged"]=JsonValue.CreateBooleanValue(true);
            await VerifyOnexSettingsPanelsAsync(report);
            report["fan"]=_onexHardwareHome?.GetNamedObject("fan",null)??new JsonObject();
            report["lighting"]=JsonValue.CreateBooleanValue(_onexHardwareHome?.GetNamedObject("lighting",null)?.GetNamedBoolean("available",false)==true);
            report["powerMode"]=JsonValue.CreateBooleanValue(OnexWindowsPowerReady);
            report["pointerProbeArmed"]=JsonValue.CreateBooleanValue(true);
            _onexPointerProbe?.Stop();int ticks=0;
            _onexPointerProbe=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};
            bool writing=false;
            _onexPointerProbe.Tick+=async(s,e)=>
            {
                if(writing)return;if(++ticks>600){_onexPointerProbe.Stop();return;}writing=true;
                try
                {
                    var toggle=(ToggleSwitch)_releaseHome.FindName("LightPl2Toggle");var secondary=(Slider)_releaseHome.FindName("OnexPl2Slider");
                    Rect tr=new ToggleSwitchAutomationPeer(toggle).GetBoundingRectangle();Rect cr=new ButtonAutomationPeer(qsTileMap["ChargeLimiter"].TileButton).GetBoundingRectangle();
                    var state=new JsonObject{{"pl2On",JsonValue.CreateBooleanValue(toggle.IsOn)},{"pl2Visible",JsonValue.CreateBooleanValue(secondary.Visibility==Visibility.Visible)},{"pl2ToggleRect",RectJson(tr)},{"chargeRect",RectJson(cr)},{"chargeDialog",JsonValue.CreateBooleanValue(_onexDetailsDialog?.Title as string=="Charge Limit")},{"chargePercent",JsonValue.CreateNumberValue(_onexChargePercent)},{"chargeOn",JsonValue.CreateBooleanValue(_onexChargeOn)}};
                    if(widget!=null){state["gameBarBounds"]=RectJson(widget.WindowBounds);state["clickThrough"]=JsonValue.CreateBooleanValue(widget.ClickThroughEnabled);state["displayMode"]=JsonValue.CreateStringValue(widget.GameBarDisplayMode.ToString());state["widgetVisible"]=JsonValue.CreateBooleanValue(widget.Visible);}
                    if(FindOnexSettingsControl<Slider>(_onexDetailsDialog?.Content as DependencyObject) is Slider charge)
                    {state["chargeMinimum"]=JsonValue.CreateNumberValue(charge.Minimum);state["chargeMaximum"]=JsonValue.CreateNumberValue(charge.Maximum);state["chargeStep"]=JsonValue.CreateNumberValue(charge.StepFrequency);}
                    if(secondary.Visibility==Visibility.Visible)
                    {double top=secondary.TransformToVisual(_releaseHome.PowerInput).TransformPoint(new Point()).Y;state["pl2SeparateTrack"]=JsonValue.CreateBooleanValue(top>=_releaseHome.PowerInput.ActualHeight);}
                    var file=await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-home-pointer-state.json",CreationCollisionOption.ReplaceExisting);await FileIO.WriteTextAsync(file,state.Stringify());
                }
                catch(Exception ex){Logger.Warn("Pointer probe: "+ex.Message);}
                finally{writing=false;}
            };
            _onexPointerProbe.Start();
        }
        private static T FindOnexSettingsControl<T>(DependencyObject root) where T:DependencyObject
        {
            if(root==null)return null;if(root is T match)return match;
            for(int i=0;i<Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);i++)
            {var child=FindOnexSettingsControl<T>(Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(root,i));if(child!=null)return child;}
            return null;
        }
        private static JsonArray RectJson(Rect r)=>new JsonArray{JsonValue.CreateNumberValue(r.X),JsonValue.CreateNumberValue(r.Y),JsonValue.CreateNumberValue(r.Width),JsonValue.CreateNumberValue(r.Height)};
    }
}
