// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexDisplayThumbAsync(JsonObject report)
        {
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            var before=await ReadOnexStateAsync();DisplayNavItem.IsChecked=true;await RefreshOnexDisplayAsync(false);
            double scale=Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
            var fixture=new Slider{Minimum=0,Maximum=100,Width=300,Style=(Style)Resources["LightMediaSliderStyle"],Foreground=OnexDialogAccent};WireOnexDisplayThumb(fixture);
            var host=new Border{Child=fixture,Width=332,Padding=new Thickness(16),Background=new SolidColorBrush(Windows.UI.Color.FromArgb(255,16,19,22)),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(20,80,0,0)};
            var root=(Grid)Content;Grid.SetRowSpan(host,20);Canvas.SetZIndex(host,10000);root.Children.Add(host);
            var samples=new JsonArray();
            Windows.Foundation.Rect Bounds(FrameworkElement part)=>part.TransformToVisual(fixture).TransformBounds(new Windows.Foundation.Rect(0,0,part.ActualWidth,part.ActualHeight));
            try
            {
                await Task.Delay(120);UpdateLayout();
                foreach(int value in new[]{0,50,100})
                {
                    fixture.Value=value;await Task.Delay(100);UpdateLayout();
                    var ink=OnexDisplayThumbPart(fixture,"OnexDisplayThumbInk");var ring=OnexDisplayThumbPart(fixture,"OnexDisplayThumbRing");var halo=OnexDisplayThumbPart(fixture,"OnexDisplayThumbHalo");
                    var sliderRoot=(FrameworkElement)VisualTreeHelper.GetChild(fixture,0);var track=(FrameworkElement)sliderRoot.FindName("HorizontalTrackRect");
                    Check(ink!=null&&ring!=null&&halo!=null&&track!=null,"Display thumb named parts are missing");
                    ink.RenderTransform=new TranslateTransform();var original=Bounds(ink);var oldRing=Bounds(ring);var oldHalo=Bounds(halo);var oldTrack=Bounds(track);
                    await SaveOnexImage("onex-display-thumb-before-"+value+".png",host,0);
                    AdjustOnexDisplayThumb(fixture);var shifted=Bounds(ink);double delta=(shifted.Y+shifted.Height/2-original.Y-original.Height/2)*scale;
                    Check(Math.Abs(delta+1)<.001,"Inner white thumb did not move exactly one physical pixel");Check(Bounds(ring).Equals(oldRing)&&Bounds(halo).Equals(oldHalo)&&Bounds(track).Equals(oldTrack),"Colored ring/halo/track moved");
                    await SaveOnexImage("onex-display-thumb-after-"+value+".png",host,0);
                    samples.Add(new JsonObject{{"value",JsonValue.CreateNumberValue(value)},{"whiteDeltaPhysicalPixels",JsonValue.CreateNumberValue(delta)},{"ringHaloTrackUnchanged",JsonValue.CreateBooleanValue(true)}});
                }
                int live=0;
                foreach(var slider in _onexDisplaySliders.Keys.Concat(new[]{_onexSdrBrightness}))
                {
                    AdjustOnexDisplayThumb(slider);var ink=OnexDisplayThumbPart(slider,"OnexDisplayThumbInk");
                    Check(ink?.RenderTransform is TranslateTransform shift&&Math.Abs(shift.Y*scale+1)<.001,"Live Display slider did not receive its white-only offset");live++;
                }
                report["samples"]=samples;report["liveDisplaySliders"]=JsonValue.CreateNumberValue(live);report["rawPixelsPerViewPixel"]=JsonValue.CreateNumberValue(scale);report["nativeRenderedFixture"]=JsonValue.CreateBooleanValue(true);report["hardwareSettingsChanged"]=JsonValue.CreateBooleanValue(false);report["physicalOwnerReviewPending"]=JsonValue.CreateBooleanValue(true);
            }
            finally{root.Children.Remove(host);}
            Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==before.GetNamedString("rawLimits"),"Thumb verification changed TDP");
        }
        private async Task VerifyOnexToggleAlignmentAsync(JsonObject report)
        {
            var live=(ToggleSwitch)_releaseHome.FindName("LightPl2Toggle");
            bool original=live.IsOn;var prefs=Windows.Storage.ApplicationData.Current.LocalSettings.Values;bool had=prefs.TryGetValue("OnexSeparatePl2",out var prior);
            var liveGeometry=new JsonArray();report["liveHomePl2"]=liveGeometry;
            report["packageVersion"]=JsonValue.CreateStringValue(Windows.ApplicationModel.Package.Current.Id.Version.Major+"."+Windows.ApplicationModel.Package.Current.Id.Version.Minor+"."+Windows.ApplicationModel.Package.Current.Id.Version.Build+"."+Windows.ApplicationModel.Package.Current.Id.Version.Revision);
            try
            {
                PerformanceNavItem.IsChecked=true;_releaseHome.Scroll.ChangeView(null,0,null,true);
                foreach(bool focused in new[]{false,true})foreach(bool on in new[]{false,true})
                {
                    live.IsOn=on;await Task.Delay(200);(focused?(Control)live:_releaseHome.PowerInput).Focus(FocusState.Keyboard);await Task.Delay(200);UpdateLayout();
                    var track=(FrameworkElement)FindDescendantByName(live,"TrackOn");var knob=(FrameworkElement)FindDescendantByName(live,"Knob");
                    var tr=track.TransformToVisual(live).TransformBounds(new Windows.Foundation.Rect(0,0,track.ActualWidth,track.ActualHeight));
                    var kr=knob.TransformToVisual(live).TransformBounds(new Windows.Foundation.Rect(0,0,knob.ActualWidth,knob.ActualHeight));
                    var entry=new JsonObject{{"on",JsonValue.CreateBooleanValue(on)},{"focused",JsonValue.CreateBooleanValue(focused)},{"dyDip",JsonValue.CreateNumberValue(kr.Y+kr.Height/2-tr.Y-tr.Height/2)},{"trackY",JsonValue.CreateNumberValue(tr.Y)},{"trackHeight",JsonValue.CreateNumberValue(tr.Height)},{"knobY",JsonValue.CreateNumberValue(kr.Y)},{"knobHeight",JsonValue.CreateNumberValue(kr.Height)},{"rounding",JsonValue.CreateBooleanValue(knob.UseLayoutRounding)}};liveGeometry.Add(entry);
                    string name="onex-live-pl2-"+(focused?"focus":"normal")+(on?"-on":"-off");
                    await SaveOnexImage(name+".png",live);await SaveOnexImage(name+"-card.png",_releaseHome.PowerSection);
                    var screen=new Windows.UI.Xaml.Automation.Peers.ToggleSwitchAutomationPeer(live).GetBoundingRectangle();double scale=Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;string capture=Guid.NewGuid().ToString("N");
                    var folder=Windows.Storage.ApplicationData.Current.LocalFolder;
                    await Windows.Storage.FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-toggle-capture-state.json",Windows.Storage.CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(capture)},{"control",JsonValue.CreateStringValue("LightPl2Toggle")},{"name",JsonValue.CreateStringValue(name)},{"x",JsonValue.CreateNumberValue(screen.X+widget.WindowBounds.X*scale)},{"y",JsonValue.CreateNumberValue(screen.Y+widget.WindowBounds.Y*scale)},{"width",JsonValue.CreateNumberValue(screen.Width)},{"height",JsonValue.CreateNumberValue(screen.Height)}}.Stringify());
                    bool captured=false;for(int attempt=0;attempt<50;attempt++){await Task.Delay(100);try{if(await Windows.Storage.FileIO.ReadTextAsync(await folder.GetFileAsync("onex-toggle-capture-ack.txt"))==capture){captured=true;break;}}catch{}}
                    if(!captured)throw new InvalidOperationException("Physical PL2 capture timed out");
                    entry["physicalCapture"]=JsonValue.CreateBooleanValue(true);
                }
            }
            finally{live.IsOn=original;if(had)prefs["OnexSeparatePl2"]=prior;else prefs.Remove("OnexSeparatePl2");}
            var geometry=new JsonArray();report["toggleGeometry"]=geometry;
            var panel=new StackPanel{Spacing=18,Padding=new Thickness(20),Width=400,Background=new SolidColorBrush(Windows.UI.Color.FromArgb(255,16,19,22)),RequestedTheme=ElementTheme.Dark};
            var host=new Border{Child=panel,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(32,70,0,0)};
            var root=(Grid)Content;Grid.SetRowSpan(host,20);Canvas.SetZIndex(host,10000);root.Children.Add(host);
            try
            {
                foreach(string name in new[]{"Home","Modern","Default"})
                {
                    var row=new StackPanel{Spacing=6};row.Children.Add(new TextBlock{Text=name});var toggle=new ToggleSwitch{OnContent="",OffContent="",HorizontalAlignment=HorizontalAlignment.Left};
                    if(name=="Home")toggle.Style=(Style)_releaseHome.Resources["LightToggleStyle"];
                    if(name=="Modern")toggle.Style=(Style)Resources["ModernToggleStyle"];
                    row.Children.Add(toggle);panel.Children.Add(row);await Task.Delay(100);toggle.ApplyTemplate();UpdateLayout();
                    foreach(bool on in new[]{false,true,false,true})
                    {
                        toggle.IsOn=on;await Task.Delay(250);UpdateLayout();
                        var parts=new JsonArray();
                        foreach(var part in FindOnexSettingsControls<FrameworkElement>(toggle).Where(v=>!string.IsNullOrEmpty(v.Name)))
                        {
                            var r=part.TransformToVisual(toggle).TransformBounds(new Windows.Foundation.Rect(0,0,part.ActualWidth,part.ActualHeight));
                            parts.Add(new JsonObject{{"name",JsonValue.CreateStringValue(part.Name)},{"type",JsonValue.CreateStringValue(part.GetType().Name)},{"x",JsonValue.CreateNumberValue(r.X)},{"y",JsonValue.CreateNumberValue(r.Y)},{"w",JsonValue.CreateNumberValue(r.Width)},{"h",JsonValue.CreateNumberValue(r.Height)}});
                        }
                        var track=(FrameworkElement)FindDescendantByName(toggle,name=="Home"?"TrackOn":"SwitchKnobBounds");
                        var knob=(FrameworkElement)FindDescendantByName(toggle,name=="Home"?"Knob":on?"SwitchKnobOn":"SwitchKnobOff");
                        var tr=track.TransformToVisual(toggle).TransformBounds(new Windows.Foundation.Rect(0,0,track.ActualWidth,track.ActualHeight));
                        var kr=knob.TransformToVisual(toggle).TransformBounds(new Windows.Foundation.Rect(0,0,knob.ActualWidth,knob.ActualHeight));
                        double dy=kr.Y+kr.Height/2-tr.Y-tr.Height/2;
                        geometry.Add(new JsonObject{{"style",JsonValue.CreateStringValue(name)},{"on",JsonValue.CreateBooleanValue(on)},{"dyDip",JsonValue.CreateNumberValue(dy)},{"parts",parts}});
                        double expectedDy=name=="Home"&&on?-2d/3:0;
                        if(Math.Abs(dy-expectedDy)>.01)throw new InvalidOperationException("Toggle knob differs from its intended visual position: "+name+" "+on+" "+dy);
                        if(geometry.Count<=12)await SaveOnexImage("onex-toggle-"+name+"-"+geometry.Count+".png",toggle);
                    }
                }
                await SaveOnexImage("onex-toggle-style-probe.png",panel);
                report["alignmentMatchesRequestedOpticalLift"]=JsonValue.CreateBooleanValue(true);
                report["homeOnOpticalLiftDip"]=JsonValue.CreateNumberValue(2d/3);
                report["homeOffOpticalLiftDip"]=JsonValue.CreateNumberValue(0);
                report["repeatedTransitions"]=JsonValue.CreateNumberValue(12);
                report["hardwareTogglesExecuted"]=JsonValue.CreateBooleanValue(false);
            }
            finally{root.Children.Remove(host);}
        }
        private async Task VerifyOnexSliderAlignmentAsync(JsonObject report)
        {
            var geometry=new JsonArray();report["sliderGeometry"]=geometry;bool aligned=true;
            var panel=new StackPanel{Spacing=8,Padding=new Thickness(16),Width=600,Background=new SolidColorBrush(Windows.UI.Color.FromArgb(255,16,19,22)),RequestedTheme=ElementTheme.Dark};
            var host=new Border{Child=panel,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(32,70,0,0)};
            var root=(Grid)Content;Grid.SetRowSpan(host,20);Canvas.SetZIndex(host,10000);root.Children.Add(host);
            try
            {
                foreach(string name in new[]{"Power","Pl2","Media","Modern","Default32","Default36","DefaultAuto"})
                {
                    var row=new StackPanel();row.Children.Add(new TextBlock{Text=name});var slider=new Slider{Minimum=0,Maximum=100,StepFrequency=1,IsThumbToolTipEnabled=false};
                    if(name=="Power")slider.Style=(Style)_releaseHome.Resources["LightPowerSliderStyle"];
                    if(name=="Pl2")slider.Style=(Style)_releaseHome.Resources["OnexPl2SliderStyle"];
                    if(name=="Media")slider.Style=(Style)_releaseHome.Resources["LightMediaSliderStyle"];
                    if(name=="Modern")slider.Style=(Style)Resources["ModernSliderStyle"];
                    if(name=="Default32")slider.Height=32;if(name=="Default36")slider.Height=36;
                    row.Children.Add(slider);panel.Children.Add(row);await Task.Delay(100);slider.ApplyTemplate();UpdateLayout();
                    foreach(int value in new[]{0,50,100})
                    {
                        slider.Value=value;await Task.Delay(200);UpdateLayout();var parts=new JsonArray();
                        foreach(var part in FindOnexSettingsControls<FrameworkElement>(slider).Where(v=>!string.IsNullOrEmpty(v.Name)||v is Windows.UI.Xaml.Shapes.Shape||v is Border))
                        {
                            var r=part.TransformToVisual(slider).TransformBounds(new Windows.Foundation.Rect(0,0,part.ActualWidth,part.ActualHeight));
                            var brush=part is Windows.UI.Xaml.Shapes.Shape shape?shape.Fill:part is Border border?border.Background:null;
                            parts.Add(new JsonObject{{"name",JsonValue.CreateStringValue(part.Name??"")},{"type",JsonValue.CreateStringValue(part.GetType().Name)},{"x",JsonValue.CreateNumberValue(r.X)},{"y",JsonValue.CreateNumberValue(r.Y)},{"w",JsonValue.CreateNumberValue(r.Width)},{"h",JsonValue.CreateNumberValue(r.Height)},{"color",JsonValue.CreateStringValue((brush as SolidColorBrush)?.Color.ToString()??"")}});
                        }
                        var track=(FrameworkElement)FindDescendantByName(slider,"HorizontalTrackRect");
                        var ink=(FrameworkElement)(FindDescendantByName(slider,"OnexPowerThumbInk")??FindDescendantByName(slider,"OnexMediaThumbInk")??FindDescendantByName(slider,"SliderInnerThumb"));
                        if(track==null||ink==null)throw new InvalidOperationException("Slider white ink/track not found: "+name);
                        var tr=track.TransformToVisual(slider).TransformBounds(new Windows.Foundation.Rect(0,0,track.ActualWidth,track.ActualHeight));var kr=ink.TransformToVisual(slider).TransformBounds(new Windows.Foundation.Rect(0,0,ink.ActualWidth,ink.ActualHeight));double dy=kr.Y+kr.Height/2-tr.Y-tr.Height/2;double expected=name=="Power"?-2d/3:name=="Media"?1d/3:0;aligned&=Math.Abs(dy-expected)<.01;
                        var entry=new JsonObject{{"style",JsonValue.CreateStringValue(name)},{"value",JsonValue.CreateNumberValue(value)},{"dyDip",JsonValue.CreateNumberValue(dy)},{"parts",parts}};
                        if(name=="Power"||name=="Media")
                        {
                            string prefix=name=="Power"?"OnexPowerThumb":"OnexMediaThumb";
                            foreach(string partName in new[]{prefix+"Ring",prefix+"Halo"})
                            {
                                var colour=(FrameworkElement)FindDescendantByName(slider,partName);var r=colour.TransformToVisual(slider).TransformBounds(new Windows.Foundation.Rect(0,0,colour.ActualWidth,colour.ActualHeight));double colourDy=r.Y+r.Height/2-tr.Y-tr.Height/2;entry[partName+"DyDip"]=JsonValue.CreateNumberValue(colourDy);aligned&=Math.Abs(colourDy)<.01;
                            }
                        }
                        geometry.Add(entry);
                        await SaveOnexImage("onex-slider-"+name+"-"+value+".png",slider);
                    }
                }
                report["allSevenStylesMatchRequestedAlignment"]=JsonValue.CreateBooleanValue(aligned);
                if(!aligned)throw new InvalidOperationException("Slider white ink differs from owner-requested position");
            }
            finally{root.Children.Remove(host);}
            VerifyOnexMediaWidths(report);
            await VerifyOnexLiveSliderScreensAsync(report);
        }
        private async Task VerifyOnexLiveSliderScreensAsync(JsonObject report)
        {
            var folder=Windows.Storage.ApplicationData.Current.LocalFolder;var captures=new JsonArray();report["liveSliderCaptures"]=captures;
            async Task Capture(Slider slider,string name)
            {
                slider.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(180);slider.IsFocusEngaged=false;slider.Focus(FocusState.Programmatic);await Task.Delay(180);UpdateLayout();
                var rect=new Windows.UI.Xaml.Automation.Peers.SliderAutomationPeer(slider).GetBoundingRectangle();double scale=Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;string id=Guid.NewGuid().ToString("N");
                await Windows.Storage.FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-slider-capture-state.json",Windows.Storage.CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"control",JsonValue.CreateStringValue(slider.Name)},{"name",JsonValue.CreateStringValue("onex-live-slider-"+name)},{"x",JsonValue.CreateNumberValue(rect.X+widget.WindowBounds.X*scale)},{"y",JsonValue.CreateNumberValue(rect.Y+widget.WindowBounds.Y*scale)},{"width",JsonValue.CreateNumberValue(rect.Width)},{"height",JsonValue.CreateNumberValue(rect.Height)}}.Stringify());
                bool done=false;for(int n=0;n<50;n++){await Task.Delay(100);try{if(await Windows.Storage.FileIO.ReadTextAsync(await folder.GetFileAsync("onex-slider-capture-ack.txt"))==id){done=true;break;}}catch{}}
                if(!done)throw new InvalidOperationException("Physical slider capture failed: "+name);
                captures.Add(new JsonObject{{"control",JsonValue.CreateStringValue(name)},{"value",JsonValue.CreateNumberValue(slider.Value)},{"captured",JsonValue.CreateBooleanValue(true)}});
            }
            var toggle=(ToggleSwitch)_releaseHome.FindName("LightPl2Toggle");bool on=toggle.IsOn;var prefs=Windows.Storage.ApplicationData.Current.LocalSettings.Values;bool had=prefs.TryGetValue("OnexSeparatePl2",out var prior);
            try
            {
                PerformanceNavItem.IsChecked=true;toggle.IsOn=true;
                await Capture(_releaseHome.PowerInput,"Power");await Capture((Slider)_releaseHome.FindName("OnexPl2Slider"),"Pl2");await Capture(RailBrightnessSlider,"Brightness");await Capture(RailVolumeSlider,"Volume");
                _releasePowerNav.IsChecked=true;await Capture(TDPSlider,"Performance");
                var test=new Slider{Minimum=0,Maximum=100,Value=50,Height=36,IsThumbToolTipEnabled=false};var dialog=CreateOnexSettingsDialog("Slider alignment verification",null,test);var opening=dialog.ShowAsync();
                try
                {
                    await Task.Delay(600);test.ApplyTemplate();UpdateLayout();
                    var track=(FrameworkElement)FindDescendantByName(test,"HorizontalTrackRect");var ink=(FrameworkElement)FindDescendantByName(test,"SliderInnerThumb");
                    var tr=track.TransformToVisual(test).TransformBounds(new Windows.Foundation.Rect(0,0,track.ActualWidth,track.ActualHeight));var kr=ink.TransformToVisual(test).TransformBounds(new Windows.Foundation.Rect(0,0,ink.ActualWidth,ink.ActualHeight));double dy=kr.Y+kr.Height/2-tr.Y-tr.Height/2;
                    report["realSettingsDialogWhiteInkDyDip"]=JsonValue.CreateNumberValue(dy);if(Math.Abs(dy)>.01)throw new InvalidOperationException("Settings dialog slider is not centered");
                    await SaveOnexImage("onex-slider-settings-dialog.png",dialog);
                }
                finally{dialog.Hide();await opening;}
                PerformanceNavItem.IsChecked=true;await SaveOnexImage("onex-sliders-corrected-home.png",this);
            }
            finally{toggle.IsOn=on;if(had)prefs["OnexSeparatePl2"]=prior;else prefs.Remove("OnexSeparatePl2");PerformanceNavItem.IsChecked=true;FocusFirstTabContentElement();}
        }
        private void VerifyOnexMediaWidths(JsonObject report)
        {
            PerformanceNavItem.IsChecked=true;var geometry=new JsonArray();report["mediaWidthAcrossPercentages"]=geometry;
            string oldBrightness=RailBrightnessValue.Text,oldVolume=RailVolumeValue.Text;double? width=null,bx=null,vx=null;
            try
            {
                foreach(int percent in new[]{0,1,7,9,10,50,99,100})
                {
                    // Exercise the actual readouts/parents without changing hardware levels.
                    RailBrightnessValue.Text=percent+"%";RailVolumeValue.Text=percent+"%";UpdateLayout();
                    var b=RailBrightnessSlider.TransformToVisual(this).TransformBounds(new Windows.Foundation.Rect(0,0,RailBrightnessSlider.ActualWidth,RailBrightnessSlider.ActualHeight));var v=RailVolumeSlider.TransformToVisual(this).TransformBounds(new Windows.Foundation.Rect(0,0,RailVolumeSlider.ActualWidth,RailVolumeSlider.ActualHeight));
                    double bt=((FrameworkElement)FindDescendantByName(RailBrightnessSlider,"HorizontalTrackRect")).ActualWidth,vt=((FrameworkElement)FindDescendantByName(RailVolumeSlider,"HorizontalTrackRect")).ActualWidth;
                    geometry.Add(new JsonObject{{"percent",JsonValue.CreateNumberValue(percent)},{"brightnessWidth",JsonValue.CreateNumberValue(b.Width)},{"volumeWidth",JsonValue.CreateNumberValue(v.Width)},{"brightnessTrackWidth",JsonValue.CreateNumberValue(bt)},{"volumeTrackWidth",JsonValue.CreateNumberValue(vt)}});
                    if(Math.Abs(b.Width-v.Width)>.01||Math.Abs(bt-vt)>.01||(width.HasValue&&(Math.Abs(b.Width-width.Value)>.01||Math.Abs(b.X-bx.Value)>.01||Math.Abs(v.X-vx.Value)>.01)))throw new InvalidOperationException("Media tracks change width/position with percentage text");width=b.Width;bx=b.X;vx=v.X;
                }
                report["mediaWidthsEqualAndStable"]=JsonValue.CreateBooleanValue(true);report["mediaHardwareLevelsChanged"]=JsonValue.CreateBooleanValue(false);
            }
            finally{RailBrightnessValue.Text=oldBrightness;RailVolumeValue.Text=oldVolume;UpdateLayout();}
        }
    }
}
