// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Globalization;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace XboxGamingBar
{
    // Native XInput X and Y are independent. The square is their domain; the circle
    // marks unit radius. Neither diagonal input nor the data table is radially clamped.
    internal sealed class OnexControllerStickPlot
    {
        internal const double Radius=175,CenterY=213,TriggerHeight=270;
        internal static double CenterX(int side)=>side==0?262:738;
        private static readonly int[] Levels={1,2,5,10};
        private readonly Canvas canvas=new Canvas{Width=1000,Height=440};
        private readonly Brush white;
        private readonly Brush grid;
        private readonly Brush axes;
        private readonly Brush shade;
        private readonly Brush panel;
        private readonly Brush[] accents;
        private readonly ToggleButton[] zoomButtons=new ToggleButton[4];
        private readonly Rectangle[] deadzone=new Rectangle[2],fills=new Rectangle[2];
        private readonly Line[] triggerRaw=new Line[2];
        private readonly Ellipse[] ring=new Ellipse[2];
        private readonly TextBlock[] edge=new TextBlock[2];
        private readonly TextBlock[,] bounds=new TextBlock[2,4];
        internal double RangeHeight=>range.ActualHeight;
        internal double LegendHeight=>legend.ActualHeight;
        private readonly TextBlock legend=new TextBlock{FontSize=13,TextWrapping=TextWrapping.Wrap,
            Text=OnexStrings.Get("Dashed white: Raw · Solid colour: Output (profile preview)")};
        private readonly TextBlock range=new TextBlock{VerticalAlignment=VerticalAlignment.Center,FontSize=14,TextWrapping=TextWrapping.Wrap};
        // Raw and Output have separate marker sizes so a shared endpoint stays visible.
        private readonly Trace[] raw=new Trace[2],output=new Trace[2];
        private int zoom=1;
        private JsonObject last;
        internal readonly TextBlock[] Numbers=new TextBlock[4];
        internal StackPanel Panel {get;}=new StackPanel{Spacing=3};
        internal Viewbox Chart {get;}
        internal StackPanel Toolbar {get;}=new StackPanel{Orientation=Orientation.Horizontal,Spacing=5};

        private sealed class Trace
        {
            internal Ellipse Dot;
            internal Line Vector;
            internal Polygon Arrow;
            internal void Hide(){Dot.Visibility=Vector.Visibility=Arrow.Visibility=Visibility.Collapsed;}
        }

        internal OnexControllerStickPlot()
        {
            var palette = OnexApprovedTheme.Current;
            white = new SolidColorBrush(palette.Text);
            grid = new SolidColorBrush(palette.DiagramFrame);
            axes = new SolidColorBrush(palette.DiagramInk);
            shade = new SolidColorBrush(OnexApprovedTheme.Blend(palette.Inset, palette.Focus, palette.ChipWash));
            panel = new SolidColorBrush(palette.Inset);
            accents = new[] {new SolidColorBrush(palette.Accent(OnexThemeRole.Cooling)),
                new SolidColorBrush(palette.Accent(OnexThemeRole.Controller))};
            Toolbar.Children.Add(new TextBlock{Text=OnexStrings.Get("Zoom"),VerticalAlignment=VerticalAlignment.Center});
            for(int i=0;i<Levels.Length;i++)
            {
                int level=Levels[i];
                var button=new ToggleButton{Content=level+"×",MinWidth=42,MinHeight=32,Padding=new Thickness(8,2,8,2)};
                button.Click+=(s,e)=>SetZoom(level);zoomButtons[i]=button;Toolbar.Children.Add(button);
            }
            // A separate line allows the toolbar to fit narrow widget dialogs.
            Panel.Children.Add(Toolbar);
            Panel.Children.Add(range);Panel.Children.Add(legend);
            canvas.Background=panel;
            for(int i=0;i<2;i++)BuildSide(i);
            Chart=new Viewbox{Child=canvas,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Stretch};
            Panel.Children.Add(Chart);SetZoom(1);ClearSample();
        }

        private T Place<T>(T element,double x,double y) where T:FrameworkElement
        {
            Canvas.SetLeft(element,x);Canvas.SetTop(element,y);canvas.Children.Add(element);return element;
        }
        private TextBlock Label(string text,double x,double y,double width,double size)
        {
            return Place(new TextBlock{Text=text,Width=width,FontSize=size,TextAlignment=TextAlignment.Center,
                Foreground=white},x,y);
        }
        private Line Segment(double x1,double y1,double x2,double y2,Brush brush,double thickness)
        {
            var line=new Line{X1=x1,Y1=y1,X2=x2,Y2=y2,Stroke=brush,StrokeThickness=thickness};
            canvas.Children.Add(line);return line;
        }
        private void BuildSide(int side)
        {
            double cx=CenterX(side),top=CenterY-Radius,tx=side==0?22:960;
            var color=accents[side];
            Label(OnexStrings.Get(side==0?"Left stick":"Right stick"),cx-110,3,220,19);
            Place(new Rectangle{Width=Radius*2,Height=Radius*2,Stroke=grid,StrokeThickness=1},cx-Radius,top);
            deadzone[side]=Place(new Rectangle{Fill=shade,Stroke=axes,StrokeThickness=1},cx,CenterY);
            for(int tick=-3;tick<=3;tick++)
            {
                double d=tick*Radius/4;
                var brush=tick==0?axes:grid;double thickness=tick==0?1.6:1;
                Segment(cx+d,top,cx+d,CenterY+Radius,brush,thickness);
                Segment(cx-Radius,CenterY+d,cx+Radius,CenterY+d,brush,thickness);
            }
            ring[side]=Place(new Ellipse{Width=2*Radius,Height=2*Radius,Stroke=white,StrokeThickness=1.5},cx-Radius,top);
            bounds[side,0]=Label("",cx-Radius-42,CenterY-9,41,13);
            bounds[side,1]=Label("",cx+Radius+1,CenterY-9,41,13);
            bounds[side,2]=Label("",cx+3,top+1,37,13);
            bounds[side,3]=Label("",cx+3,CenterY+Radius-19,37,13);
            raw[side]=BuildTrace(cx,white,true);output[side]=BuildTrace(cx,color,false);
            Numbers[side]=Label("—",cx-122,398,244,15);
            edge[side]=Label("",cx-169,420,338,13);edge[side].Foreground=color;
            Label(side==0?"LT":"RT",tx-16,42,50,16);
            Place(new Rectangle{Width=18,Height=TriggerHeight,RadiusX=5,RadiusY=5,Stroke=axes,StrokeThickness=1},tx,77);
            fills[side]=Place(new Rectangle{Width=18,RadiusX=4,RadiusY=4,Fill=color},tx,77+TriggerHeight);
            triggerRaw[side]=Segment(tx-4,77+TriggerHeight,tx+22,77+TriggerHeight,white,2);
            Numbers[side+2]=Label("—",tx-19,354,60,14);
        }
        private Trace BuildTrace(double cx,Brush color,bool physical)
        {
            double diameter=physical?13:6;
            var vector=Segment(cx,CenterY,cx,CenterY,color,physical?1.5:2);
            if(physical)vector.StrokeDashArray=new DoubleCollection{4,3};
            var dot=Place(new Ellipse{Width=diameter,Height=diameter,Fill=physical?null:color,
                Stroke=physical?color:panel,StrokeThickness=physical?2:1},cx-diameter/2,CenterY-diameter/2);
            var arrow=new Polygon{Stroke=color,StrokeThickness=1.5,Fill=physical?panel:color,
                Points=new PointCollection{new Point(),new Point(),new Point()}};
            canvas.Children.Add(arrow);return new Trace{Dot=dot,Vector=vector,Arrow=arrow};
        }

        internal void SetZoom(int level)
        {
            if(Array.IndexOf(Levels,level)<0)throw new ArgumentOutOfRangeException(nameof(level));
            zoom=level;
            for(int i=0;i<Levels.Length;i++)zoomButtons[i].IsChecked=Levels[i]==zoom;
            range.Text=OnexStrings.Format("Visible range ±{0} · Shaded square: centre deadzone",
                (1.0/zoom).ToString("0.##",CultureInfo.InvariantCulture));
            string limit=(1.0/zoom).ToString("0.##",CultureInfo.InvariantCulture);
            for(int i=0;i<2;i++)
            {
                bounds[i,0].Text=bounds[i,3].Text="−"+limit;
                bounds[i,1].Text=bounds[i,2].Text="+"+limit;
            }
            foreach(var circle in ring)circle.Visibility=zoom==1?Visibility.Visible:Visibility.Collapsed;
            if(last!=null)ApplySample(last);
        }
        private static double? Percent(JsonObject obj,string key)
        {
            if(obj==null||!obj.TryGetValue(key,out var field)||field.ValueType!=JsonValueType.Number)return null;
            double v=field.GetNumber();return double.IsNaN(v)||double.IsInfinity(v)?(double?)null:v;
        }
        private bool PlotTrace(Trace trace,double cx,double? x,double? y,bool physical)
        {
            if(!x.HasValue||!y.HasValue){trace.Hide();return false;}
            double vx=x.Value*zoom/100,vy=-y.Value*zoom/100;
            double magnitude=Math.Max(Math.Abs(vx),Math.Abs(vy));
            bool outside=magnitude>1;
            double ex=cx+Radius*vx/Math.Max(1,magnitude),ey=CenterY+Radius*vy/Math.Max(1,magnitude);
            trace.Vector.X2=ex;trace.Vector.Y2=ey;trace.Vector.Visibility=Visibility.Visible;
            trace.Dot.Visibility=outside?Visibility.Collapsed:Visibility.Visible;
            trace.Arrow.Visibility=outside?Visibility.Visible:Visibility.Collapsed;
            if(!outside)
            {
                Canvas.SetLeft(trace.Dot,ex-trace.Dot.Width/2);Canvas.SetTop(trace.Dot,ey-trace.Dot.Height/2);
            }
            else
            {
                // Keep the direction when a vector crosses the zoomed square. The arrow
                // replaces the endpoint; its border position is not a measured coordinate.
                double length=Math.Sqrt(vx*vx+vy*vy),ux=vx/length,uy=vy/length;
                double back=physical?15:9,half=physical?7:4;
                trace.Arrow.Points[0]=new Point(ex,ey);
                trace.Arrow.Points[1]=new Point(ex-back*ux-half*uy,ey-back*uy+half*ux);
                trace.Arrow.Points[2]=new Point(ex-back*ux+half*uy,ey-back*uy-half*ux);
            }
            return outside;
        }
        internal void ApplySample(JsonObject state)
        {
            last=state;
            var physical=state.GetNamedObject("physical");var tuned=state.GetNamedObject("tuned");
            var tuning=state.GetNamedObject("tuning",null);
            for(int i=0;i<2;i++)
            {
                string prefix=i==0?"l":"r";double cx=CenterX(i);
                double? rawX=Percent(physical,prefix+"x"),rawY=Percent(physical,prefix+"y");
                double? outX=Percent(tuned,prefix+"x"),outY=Percent(tuned,prefix+"y");
                bool rawClipped=PlotTrace(raw[i],cx,rawX,rawY,true),outClipped=PlotTrace(output[i],cx,outX,outY,false);
                edge[i].Text=rawClipped&&outClipped?OnexStrings.Get("Raw and Output outside view"):
                    rawClipped?OnexStrings.Get("Raw outside view"):outClipped?OnexStrings.Get("Output outside view"):"";
                double? dz=Percent(tuning,i==0?"leftDeadzone":"rightDeadzone");
                double half=Math.Min(1,Math.Max(0,(dz??0)*zoom/100))*Radius;
                var square=deadzone[i];square.Width=square.Height=2*half;
                Canvas.SetLeft(square,cx-half);Canvas.SetTop(square,CenterY-half);
                square.Visibility=half>0?Visibility.Visible:Visibility.Collapsed;
                Numbers[i].Text=outX.HasValue&&outY.HasValue?OnexStrings.Format("X {0}% · Y {1}%",
                    outX.Value.ToString("0.0",CultureInfo.InvariantCulture),outY.Value.ToString("0.0",CultureInfo.InvariantCulture)):"—";
                double? rawTrigger=Percent(physical,prefix+"t"),outTrigger=Percent(tuned,prefix+"t");
                var fill=fills[i];fill.Visibility=outTrigger.HasValue?Visibility.Visible:Visibility.Collapsed;
                fill.Height=TriggerHeight*Math.Max(0,Math.Min(100,outTrigger??0))/100;
                Canvas.SetTop(fill,77+TriggerHeight-fill.Height);
                var marker=triggerRaw[i];marker.Visibility=rawTrigger.HasValue?Visibility.Visible:Visibility.Collapsed;
                marker.Y1=marker.Y2=77+TriggerHeight*(1-Math.Max(0,Math.Min(100,rawTrigger??0))/100);
                Numbers[i+2].Text=outTrigger.HasValue?outTrigger.Value.ToString("0",CultureInfo.InvariantCulture)+"%":"—";
            }
        }
        internal void ClearSample()
        {
            last=null;
            foreach(var text in Numbers)text.Text="—";
            for(int i=0;i<2;i++)
            {
                raw[i].Hide();output[i].Hide();edge[i].Text="";
                fills[i].Visibility=triggerRaw[i].Visibility=deadzone[i].Visibility=Visibility.Collapsed;
            }
        }

        internal Point Endpoint(int side,bool physical)
        {
            var trace=physical?raw[side]:output[side];return new Point(trace.Vector.X2,trace.Vector.Y2);
        }
        internal bool Outside(int side,bool physical)=>(physical?raw[side]:output[side]).Arrow.Visibility==Visibility.Visible;
        internal bool HasPoint(int side,bool physical)=>(physical?raw[side]:output[side]).Dot.Visibility==Visibility.Visible;
        internal double PresentedCircleDiameter(FrameworkElement page)
        {
            var circle=ring[0];
            return circle.TransformToVisual(page).TransformBounds(new Rect(0,0,circle.ActualWidth,circle.ActualHeight)).Width;
        }
        internal double DeadzoneWidth(int side)=>deadzone[side].Width;
        internal double TriggerFill(int side)=>fills[side].Height;
    }
}
