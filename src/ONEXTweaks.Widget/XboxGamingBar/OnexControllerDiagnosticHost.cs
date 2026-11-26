// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using XboxGamingBar.IPC;

namespace XboxGamingBar
{
    internal sealed class OnexControllerDiagnosticHost : Page
    {
        internal static bool IsHostApplication => AppInfo.Current.Id=="ControllerDiagnosticHost";
        private static OnexControllerDiagnosticHost current;
        private readonly Guid owner;
        private readonly string kind;private readonly OnexGyroDashboard gyro;
        private readonly OnexControllerStickPlot plot=new OnexControllerStickPlot();
        private readonly OnexControllerAnalogReadouts readouts=new OnexControllerAnalogReadouts();
        private readonly ScrollViewer viewport=new ScrollViewer();
        private readonly TextBlock status=new TextBlock{TextWrapping=TextWrapping.Wrap};
        private readonly Button close=new Button{Content=OnexStrings.Get("Close")};
        private bool closing,finished;

        internal static void Activate(Uri uri)
        {
            // A prior failed connection may leave its error page visible. A fresh
            // scoped activation replaces that finished page rather than timing out.
            if(current!=null&&!current.finished){Window.Current.Activate();return;}
            if(uri?.Scheme!="onextweaks-controller-test"||(uri.Host!="sticks"&&uri.Host!="gyro")||
                !Guid.TryParseExact(uri.AbsolutePath.Trim('/'),"N",out var owner)||owner==Guid.Empty)
            {CoreApplication.Exit();return;}
            ApplicationView.PreferredLaunchViewSize=new Size(1100,730);
            ApplicationView.PreferredLaunchWindowingMode=ApplicationViewWindowingMode.PreferredLaunchViewSize;
            current=new OnexControllerDiagnosticHost(owner,uri.Host);
            Window.Current.Content=current;Window.Current.Activate();
            var view=ApplicationView.GetForCurrentView();view.Title=OnexStrings.Get(uri.Host=="gyro"?"Gyro & motion · live test":"Sticks & triggers · live test");
            view.SetPreferredMinSize(new Size(320,240));view.TryResizeView(new Size(1100,730));
        }

        private OnexControllerDiagnosticHost(Guid owner,string kind)
        {
            var palette = OnexApprovedTheme.Current;
            this.owner=owner;this.kind=kind;gyro=kind=="gyro"?new OnexGyroDashboard(palette):null;
            RequestedTheme=palette.Light?ElementTheme.Light:ElementTheme.Dark;
            Foreground=new SolidColorBrush(palette.Text);
            close.Background=new SolidColorBrush(palette.Raised);
            close.Foreground=new SolidColorBrush(palette.Text);
            close.FocusVisualPrimaryBrush=new SolidColorBrush(palette.Focus);
            var frame=new Grid{Padding=new Thickness(16),RowSpacing=6,Background=new SolidColorBrush(palette.Page)};
            frame.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
            frame.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var panel=new StackPanel{Spacing=4,MaxWidth=1100};
            var title=new TextBlock{Text=OnexStrings.Get(kind=="gyro"?"Gyro & motion · live test":"Sticks & triggers · live test"),FontSize=20};
            panel.Children.Add(title);panel.Children.Add(status);if(gyro!=null)panel.Children.Add(gyro.Panel);else{panel.Children.Add(plot.Panel);panel.Children.Add(readouts.Panel);}
            // Use the available height for the plot, while keeping zoom, measurements
            // and the pinned Close action reachable on small windows.
            void SizeChart()
            {
                if(gyro!=null||viewport.ActualHeight<=0)return;
                double remainder=viewport.ActualHeight-title.ActualHeight-status.ActualHeight-readouts.Panel.ActualHeight
                    -plot.Toolbar.ActualHeight-plot.RangeHeight-plot.LegendHeight-24;
                double height=Math.Max(190,Math.Min(470,Math.Floor(remainder)));
                if(plot.Chart.MaxHeight!=height)plot.Chart.MaxHeight=height;
            }
            viewport.SizeChanged+=(s,e)=>SizeChart();readouts.Panel.SizeChanged+=(s,e)=>SizeChart();
            title.SizeChanged+=(s,e)=>SizeChart();status.SizeChanged+=(s,e)=>SizeChart();
            plot.Toolbar.SizeChanged+=(s,e)=>SizeChart();plot.Panel.SizeChanged+=(s,e)=>SizeChart();
            viewport.Content=panel;frame.Children.Add(viewport);
            close.Click+=(s,e)=>RequestClose();close.HorizontalAlignment=HorizontalAlignment.Left;
            Grid.SetRow(close,1);frame.Children.Add(close);Content=frame;
            PreviewKeyDown+=(s,e)=>{
                if(IsAnalogNavigation(e.OriginalKey)||IsAnalogNavigation(e.Key)){e.Handled=true;return;}
                if(e.Key==VirtualKey.Escape||e.Key==VirtualKey.GamepadB||e.OriginalKey==VirtualKey.GamepadB){e.Handled=true;if(gyro?.CloseHelp()!=true)RequestClose();}
            };
            Window.Current.Closed+=(s,e)=>RequestClose();
            Loaded+=OnLoaded;
            SetUnavailable(OnexStrings.Get("Connecting…"));
        }

        internal static bool IsAnalogNavigation(VirtualKey key)=>
            key==VirtualKey.GamepadLeftTrigger||key==VirtualKey.GamepadRightTrigger||
            (key>=VirtualKey.GamepadLeftThumbstickUp&&key<=VirtualKey.GamepadRightThumbstickLeft);

        private void RequestClose()
        {
            closing=true;if(finished)CoreApplication.Exit();
        }
        private void SetUnavailable(string message)
        {
            status.Text=message;readouts.ClearSample();
            plot.ClearSample();gyro?.ClearSample();
        }

        private async void OnLoaded(object sender,RoutedEventArgs args)
        {
            Loaded-=OnLoaded;
            bool verify=false;
            using(var pipe=new NamedPipeClient(owner))
            try
            {
                if(!await pipe.ConnectAsync(4000))throw new InvalidOperationException("Controller test connection unavailable");
                for(int n=0;n<80&&!Window.Current.Visible&&!closing;n++)await Task.Delay(50);
                if(closing||!Window.Current.Visible)throw new InvalidOperationException("Controller test window was not visible");
                var ready=await pipe.SendRequestAsync(new ValueSet{{"operation","ready"}},3500);
                if(ready==null||!ready.ContainsKey("verify"))throw new InvalidOperationException("Game Bar did not release the controller test");
                verify=ready["verify"] is bool v&&v;
                var deadline=DateTimeOffset.UtcNow.AddSeconds(12);
                long rendered=0;bool captured=false;
                while(true)
                {
                    if(verify&&DateTimeOffset.UtcNow>deadline)closing=true;
                    bool sendingClose=closing;
                    var reply=await pipe.SendRequestAsync(new ValueSet{{"operation",sendingClose?"close":"read"},{"rendered",rendered},{"captured",captured}},2500);
                    if(reply==null)throw new InvalidOperationException("Controller test connection ended");
                    if(reply.TryGetValue("error",out var error))throw new InvalidOperationException(error.ToString());
                    if(sendingClose||reply.ContainsKey("end"))break;
                    if(reply.TryGetValue("sample",out var payload))
                    {
                        var state=JsonObject.Parse((string)payload);
                        if(state.GetNamedBoolean("available",false))
                        {
                            if(gyro!=null)gyro.ApplySample(state);else{readouts.ApplySample(state);plot.ApplySample(state);}
                            status.Text=OnexStrings.Get(state.GetNamedString("status"));
                            rendered=(long)state.GetNamedNumber("sequence");
                            if(verify&&rendered>=3)
                            {
                                await CaptureAsync();captured=true;closing=true;
                            }
                        }
                        else{rendered=0;SetUnavailable(OnexStrings.Get(state.GetNamedString("status")));}
                    }
                    else{rendered=0;SetUnavailable(OnexStrings.Get("Waiting for controller test focus"));}
                    await Task.Delay(50);
                }
                CoreApplication.Exit();
            }
            catch(Exception error)
            {
                finished=true;SetUnavailable(error.Message);
                if(verify||closing)CoreApplication.Exit();
            }
        }

        private static bool Fits(FrameworkElement element,FrameworkElement container)
        {
            var bounds=element.TransformToVisual(container).TransformBounds(new Rect(0,0,element.ActualWidth,element.ActualHeight));
            return bounds.Width>0&&bounds.Height>0&&bounds.X>=-1&&bounds.Y>=-1&&
                bounds.Right<=container.ActualWidth+1&&bounds.Bottom<=container.ActualHeight+1;
        }

        private async Task CaptureAsync()
        {
            UpdateLayout();
            if(gyro!=null){for(int n=0;n<40&&!gyro.ArtworkReady;n++)await Task.Delay(25);if(!gyro.ArtworkReady||!Fits(close,this))throw new InvalidOperationException("Textured 3D console or Close is unavailable");if(!gyro.HasSample)throw new InvalidOperationException("Gyro dashboard has no coherent raw/processed sample");}
            else{
            for(int index=0;index<6;index++)
                if(readouts.RawAt(index)=="—"||readouts.OutputAt(index)=="—")
                    throw new InvalidOperationException("Controller test capture is missing a measured axis");
            if(readouts.TravelDescription=="—")throw new InvalidOperationException("Controller test capture is missing the active trigger range");
            if(ActualWidth>=950&&ActualHeight>=610&&plot.PresentedCircleDiameter(this)<275)
                throw new InvalidOperationException("The stick plot did not expand in the wide test window");
            if(!Fits(readouts.LastCell,viewport)||!Fits(close,this))
                throw new InvalidOperationException("Controller test readouts or Close are outside the visible viewport");
            }
            var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(this);
            var pixels=await bitmap.GetPixelsAsync();
            if(pixels.Length==0)throw new InvalidOperationException("Controller test frame has no pixels");
            var file=await ApplicationData.Current.LocalFolder.CreateFileAsync(kind=="gyro"?"onex-live1081-gyro-host.png":"onex-live1074-sticks-host.png",CreationCollisionOption.ReplaceExisting);
            using(var stream=await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels.ToArray());
                await encoder.FlushAsync();
            }
        }
    }
}
