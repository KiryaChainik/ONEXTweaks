// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using ONEXTweaks.Power;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    // Each native slider keeps its writable range. Its track occupies the
    // corresponding segment of the shared watt axis, including for touch.
    internal sealed class OnexPowerScale
    {
        private sealed class Track
        {
            private readonly Grid host;
            private readonly Slider slider;
            private readonly Border inactive;
            private readonly Thickness originalMargin;

            internal Track(Grid host, Slider slider, double inset, double thumbWidth, Brush inactiveBrush)
            {
                this.host = host;
                this.slider = slider;
                originalMargin = slider.Margin;
                inactive = new Border
                {
                    Background = inactiveBrush,
                    CornerRadius = new CornerRadius(2),
                    Height = 4,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                    Visibility = Visibility.Collapsed,
                    Margin = new Thickness(inset, originalMargin.Top, thumbWidth-inset, originalMargin.Bottom)
                };
                host.Children.Insert(0, inactive);
            }

            internal void Place(double minimum, double maximum, double thumbWidth, bool shown)
            {
                // XAML computes drag travel from the control width minus the full thumb width.
                // The (possibly smaller) template inset controls only the left endpoint.
                double width = Math.Max(0, host.ActualWidth - thumbWidth);
                double span = Math.Max(1, maximum - minimum);
                double left = Math.Max(0, slider.Minimum - minimum) / span;
                double right = Math.Max(0, maximum - slider.Maximum) / span;
                var margin = new Thickness(originalMargin.Left + left * width, originalMargin.Top,
                    originalMargin.Right + right * width, originalMargin.Bottom);
                if (!slider.Margin.Equals(margin)) slider.Margin = margin;
                inactive.Visibility = shown && (left > 0 || right > 0) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private readonly Slider primary, secondary;
        private readonly Track sustained, boost;
        private readonly double thumbWidth;
        private bool separate;
        internal double Minimum => primary.Minimum;
        // Keep the axis stable while PL1 is being dragged through the 40 W threshold.
        internal double Maximum => separate ? Math.Max(primary.Maximum,Pl2ControlPolicy.ExtendedManualCeiling) : primary.Maximum;
        internal double Inset { get; }

        internal OnexPowerScale(Grid primaryHost, Slider primary, Grid secondaryHost, Slider secondary,
            double inset, Brush inactiveBrush, double thumbWidth=24)
        {
            this.primary = primary;
            this.secondary = secondary;
            this.thumbWidth = thumbWidth;
            Inset = inset;
            sustained = new Track(primaryHost, primary, inset, thumbWidth, inactiveBrush);
            boost = new Track(secondaryHost, secondary, inset, thumbWidth, inactiveBrush);
            primaryHost.SizeChanged += (s, e) => Refresh();
            secondaryHost.SizeChanged += (s, e) => Refresh();
        }

        internal void Update(bool showSeparate)
        {
            separate = showSeparate;
            Refresh();
        }

        internal double Travel(double width) => Math.Max(0,width-thumbWidth);

        private void Refresh()
        {
            sustained.Place(Minimum, Maximum, thumbWidth, true);
            boost.Place(Minimum, separate ? Maximum : secondary.Maximum, thumbWidth, separate);
        }
    }

    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexHomeRoutedPowerAsync(JsonObject report)
        {
            var root=(Grid)Content;
            var home=new OnexReleaseHome(false);
            var host=new Border
            {
                Child=home,Width=620,Height=440,
                HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top
            };
            Grid.SetRowSpan(host,20);Canvas.SetZIndex(host,10000);
            root.Children.Add(host);
            var folder=ApplicationData.Current.LocalFolder;
            var run=Guid.NewGuid().ToString("N");
            const string requestName="onex-power-1106-input.json",ackName="onex-power-1106-ack.txt";
            int events=0,writes=0;
            void Check(bool pass,string message){if(!pass)throw new InvalidOperationException(message);}
            async Task Send(string key,Func<bool> expected)
            {
                string id=run+":"+(++events);
                var request=new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"key",JsonValue.CreateStringValue(key)},
                    {"utc",JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("o"))}};
                await FileIO.WriteTextAsync(await folder.CreateFileAsync(requestName,CreationCollisionOption.ReplaceExisting),request.Stringify());
                for(int attempt=0;attempt<110;attempt++)
                {
                    await Task.Delay(45);
                    var ack=await folder.TryGetItemAsync(ackName) as StorageFile;
                    if(ack!=null&&await FileIO.ReadTextAsync(ack)==id&&expected())return;
                }
                throw new InvalidOperationException("Routed power input did not settle: "+key+" step "+events);
            }
            try
            {
                home.ApplyPower(27,30,true,true);
                home.CancelPendingPower();
                UpdateLayout();
                await Task.Delay(200);
                home.PowerRequested+=(pl1,pl2)=>
                {
                    writes++;
                    home.CompletePower(pl1,pl2,true,null,true);
                };
                int sustained=27,boost=30;
                foreach(bool secondary in new[]{false,true})
                {
                    var slider=secondary?home.Pl2Input:home.PowerInput;
                    Check(slider.Focus(FocusState.Keyboard),"Home power test could not focus slider");
                    int before=(int)slider.Value,writesBefore=writes;
                    await Send("a",()=>OnexInputGuard.IsPreviewing(slider));
                    await Send("right",()=>slider.Value==before+1);
                    for(int n=0;n<4;n++)
                    {
                        home.ApplyPower(sustained,boost,true,true);
                        Check(slider.Value==before+1&&OnexInputGuard.IsPreviewing(slider)&&writes==writesBefore,
                            "Late power readback rewound a gamepad preview after cancellation");
                        await Task.Delay(25);
                    }
                    await Send("b",()=>!OnexInputGuard.IsPreviewing(slider)&&slider.Value==before);
                    Check(writes==writesBefore,"B committed power");
                    await Send("a",()=>OnexInputGuard.IsPreviewing(slider));
                    await Send("right",()=>slider.Value==before+1);
                    home.ApplyPower(sustained,boost,true,true);
                    Check(slider.Value==before+1,"Readback erased the next adjustment");
                    await Send("a",()=>writes==writesBefore+1&&!home.HasPendingPowerInput&&slider.Value==before+1);
                    if(secondary)boost++;else sustained++;
                    Check(ReferenceEquals(FocusManager.GetFocusedElement(),slider),"A moved focus away from the slider");
                }
                report["powerRoutedKeySteps"]=JsonValue.CreateNumberValue(events);
                report["fixturePowerCommits"]=JsonValue.CreateNumberValue(writes);
                report["staleReadbackNoJump"]=JsonValue.CreateBooleanValue(true);
            }
            finally
            {
                home.CancelPendingPower();
                root.Children.Remove(host);
                await FileIO.WriteTextAsync(await folder.CreateFileAsync(requestName,CreationCollisionOption.ReplaceExisting),
                    "{\"finished\":true}");
            }
        }

        private async Task VerifyOnexPowerScaleAsync(JsonObject report)
        {
            var root=(Grid)Content;
            var home=new OnexReleaseHome(false);
            var homeTrack=(Grid)home.FindName("OnexPowerControlHost");
            var host=new Border
            {
                Child=home, Width=620, Height=440,
                HorizontalAlignment=HorizontalAlignment.Left, VerticalAlignment=VerticalAlignment.Top,
                IsHitTestVisible=false
            };
            Grid.SetRowSpan(host,20);
            Canvas.SetZIndex(host,10000);
            var samples=new JsonArray();
            report["homeThumbPositions"]=samples;
            root.Children.Add(host);
            try
            {
                var card=(FrameworkElement)home.FindName("LightPowerCard");
                var scaleLabel=(TextBlock)home.FindName("LightPowerPl2");
                var minimumLabel=(TextBlock)home.FindName("LightPowerMin");
                var maximumLabel=(TextBlock)home.FindName("LightPowerMax");
                var currentTheme=_onexAppliedTheme??OnexPalette.CreateTheme();
                home.ApplyPalette(currentTheme,true);
                int checks=0;
                void Check(bool ok,string message){checks++;if(!ok)throw new InvalidOperationException(message);}
                Windows.Foundation.Rect Bounds(Slider input,string partName)
                {
                    var part=(FrameworkElement)FindDescendantByName(input,partName);
                    Check(part!=null,"Missing native power part: "+partName);
                    return part.TransformToVisual(homeTrack).TransformBounds(new Windows.Foundation.Rect(0,0,part.ActualWidth,part.ActualHeight));
                }
                foreach(var point in new[]{(8,9),(20,21),(25,28),(39,40),(40,41),(40,46),(40,52)})
                {
                    int pl1=point.Item1,pl2=point.Item2;
                    bool extended=pl1==40;
                    int min=extended?41:8,max=extended?52:40;
                    home.ApplyPower(pl1,pl2,true,true);
                    home.UpdateLayout();
                    await Task.Delay(100);
                    var first=Bounds(home.PowerInput,"HorizontalTrackRect");
                    var second=Bounds(home.Pl2Input,"HorizontalTrackRect");
                    var firstThumb=Bounds(home.PowerInput,"HorizontalThumb");
                    var secondThumb=Bounds(home.Pl2Input,"HorizontalThumb");
                    var firstFill=Bounds(home.PowerInput,"HorizontalDecreaseRect");
                    var secondFill=Bounds(home.Pl2Input,"HorizontalDecreaseRect");
                    double x1=firstThumb.Left+firstThumb.Width/2,x2=secondThumb.Left+secondThumb.Width/2;
                    double expected1=first.Width*(pl1-8)/32,expected2=second.Width*(pl2-min)/(max-min);
                    samples.Add(new JsonObject
                    {
                        {"pl1",JsonValue.CreateNumberValue(pl1)},{"pl2",JsonValue.CreateNumberValue(pl2)},
                        {"firstTrack",JsonValue.CreateNumberValue(first.Width)},{"secondTrack",JsonValue.CreateNumberValue(second.Width)},
                        {"firstFill",JsonValue.CreateNumberValue(firstFill.Width)},{"secondFill",JsonValue.CreateNumberValue(secondFill.Width)},
                        {"pl1X",JsonValue.CreateNumberValue(x1)},{"pl2X",JsonValue.CreateNumberValue(x2)},
                        {"summary",JsonValue.CreateStringValue(scaleLabel.Text)}
                    });
                    Check(Math.Abs(first.Left-second.Left)<1&&Math.Abs(first.Width-second.Width)<1&&first.Width>300,
                        "Power track start or length differs");
                    Check(Math.Abs(x1-first.Left-expected1)<=2&&Math.Abs(x2-second.Left-expected2)<=2,
                        "Native thumb did not follow its displayed scale: "+pl1+"/"+pl2);
                    Check(Math.Abs(firstFill.Width-expected1)<=2&&Math.Abs(secondFill.Width-expected2)<=2,
                        "Native fill did not follow its displayed scale: "+pl1+"/"+pl2);
                    Check(home.PowerInput.Minimum==8&&home.PowerInput.Maximum==40&&home.Pl2Input.Minimum==min&&home.Pl2Input.Maximum==max,
                        "Power slider boundaries changed");
                    Check(minimumLabel.Text==OnexStrings.Format("{0} W",min)&&maximumLabel.Text==OnexStrings.Format("{0} W",max),
                        "The visible power ticks differ from the active scale");
                    Check(scaleLabel.Text==(extended?OnexStrings.Format("PL2 {0} W",pl2):OnexStrings.Format("PL1 {0} W / PL2 {1} W",pl1,pl2)),
                        "Centered summary did not track the layout mode");
                    if(pl1==25)await SaveOnexImage("onex-power-home-standard.png",card,80);
                    if(pl2==46)await SaveOnexImage("onex-power-home-boost.png",card,80);
                }

                home.ApplyPower(39,40,true,true);
                int requests=0;
                home.PowerRequested+=(a,b)=>requests++;
                var slider=home.PowerInput;
                Check(OnexInputGuard.HandleSliderKey(slider,Windows.System.VirtualKey.GamepadA,false),"PL1 preview did not start");
                OnexInputGuard.HandleSliderKey(slider,Windows.System.VirtualKey.GamepadDPadRight,false);
                Check(slider.Value==40&&home.Pl2Input.Minimum==41&&home.Pl2Input.Value>=41&&
                    scaleLabel.Text.StartsWith("PL2 ",StringComparison.Ordinal),"Preview did not switch to extended PL2");
                OnexInputGuard.HandleSliderKey(slider,Windows.System.VirtualKey.GamepadB,false);
                Check(slider.Value==39&&home.Pl2Input.Minimum==8&&home.Pl2Input.Maximum==40&&
                    scaleLabel.Text.StartsWith("PL1 ",StringComparison.Ordinal),"Cancel did not restore the normal scale");
                home.Pl2Input.Value=8;
                Check(home.Pl2Input.Value==40&&!home.HasPendingPowerInput&&requests==0,"PL2 went below PL1 + 1 or wrote during preview");
                home.ApplyPower(25,52,true,false);
                Check(home.Pl2Input.Visibility==Visibility.Collapsed&&maximumLabel.Text==OnexStrings.Format("{0} W",40)
                    &&((TextBlock)home.FindName("LightPl2Value")).Text=="52","Factory readback or separate-off changed");
                home.ApplyPower(25,52,true,true);
                Check(home.Pl2Input.Maximum==40&&home.Pl2Input.Value==40&&home.PowerStatusText.Contains("52")&&requests==0,
                    "Factory readback escaped the manual range or caused a power write");

                var primary=(TextBlock)home.FindName("LightPowerValue");
                var primaryUnit=(TextBlock)home.FindName("LightPowerUnit");
                var secondary=(TextBlock)home.FindName("LightPl2Value");
                var secondaryUnit=(TextBlock)home.FindName("LightPl2Unit");
                int themes=0;
                foreach(var theme in WidgetThemes.Values)
                {
                    home.ApplyPalette(theme,true);
                    ApplyOnexCatalogSurfaces(home,theme);
                    var approved=OnexApprovedTheme.Find(theme.Name);
                    var expectedPrimary=approved?.Accent(OnexThemeRole.Power)??theme.EffectiveAccent();
                    var expectedSecondary=approved?.Accent(OnexThemeRole.Controller)??theme.EffectiveAccent();
                    Check(ReferenceEquals(home.PowerInput.Background,primary.Foreground)&&ReferenceEquals(primary.Foreground,primaryUnit.Foreground)
                        &&(primary.Foreground as SolidColorBrush)?.Color==expectedPrimary,"PL1 label/fill accent differs in "+theme.Name);
                    Check(ReferenceEquals(home.Pl2Input.Background,secondary.Foreground)&&ReferenceEquals(secondary.Foreground,secondaryUnit.Foreground)
                        &&(secondary.Foreground as SolidColorBrush)?.Color==expectedSecondary,"PL2 label/fill accent differs in "+theme.Name);
                    themes++;
                }
                report["checks"]=JsonValue.CreateNumberValue(checks);
                report["themesChecked"]=JsonValue.CreateNumberValue(themes);
                report["powerRequests"]=JsonValue.CreateNumberValue(requests);
                report["factoryReadbackAccepted"]=JsonValue.CreateBooleanValue(true);
            }
            finally{root.Children.Remove(host);}
        }
    }
}
