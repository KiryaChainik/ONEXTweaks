// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Windows.Data.Json;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
namespace XboxGamingBar
{
 internal sealed class OnexGyroDashboard
 {
  internal readonly StackPanel Panel=new StackPanel{Spacing=10};
  private static SolidColorBrush Brush(Color color) => new SolidColorBrush(color);
  private readonly OnexApprovedTheme palette;
  private readonly Brush Ink, Muted, GridInk, Purple, Cyan, Amber;
  private readonly Gauge[] gauges=new Gauge[3];private readonly TextBlock[] acceleration=new TextBlock[3];private readonly Rectangle[] accelBars=new Rectangle[3];
  private readonly TextBlock stage, poseLabel, gravityLabel, target;
  private readonly TextBlock[] axisValues=new TextBlock[6];
  private readonly Dictionary<string,TextBlock> effects=new Dictionary<string,TextBlock>();
  private readonly OnexGyroConsoleView model;
  private readonly OnexGyroPose pose=new OnexGyroPose();
  private readonly List<Flyout> help=new List<Flyout>();
  private readonly Stopwatch clock=Stopwatch.StartNew();private bool valid;
  internal bool HasSample{get;private set;}internal bool Fresh=>valid;internal string StageText=>stage.Text;
  internal Quaternion PresentedPose=>model.Presented;
  internal int ModelFrames=>model.FrameCount;
  internal double ModelRenderMilliseconds=>model.RenderMilliseconds;
  internal bool ArtworkReady=>model.FrameCount>0;internal string AxisValue(int index)=>axisValues[index].Text;
  internal string Effect(string id)=>effects[id].Text;
  internal void Recenter()
  {
   pose.Recenter();
   model.Recenter();
   UpdatePoseLabel();
  }
  private TextBlock Label(string text,double size=13)=>new TextBlock{Text=OnexStrings.Get(text),FontSize=size,Foreground=Ink,TextWrapping=TextWrapping.Wrap};
  private static JsonObject Obj(JsonObject o,string key)
  {
   IJsonValue value;if(o==null||!o.TryGetValue(key,out value)||value.ValueType!=JsonValueType.Object)return null;
   return value.GetObject();
  }
  private static double N(JsonObject o,string key,double fallback=0)=>o?.GetNamedNumber(key,fallback)??fallback;
  private static string S(JsonObject o,string key,string fallback="")=>o?.GetNamedString(key,fallback)??fallback;
  private static bool B(JsonObject o,string key)=>o?.GetNamedBoolean(key,false)??false;
  private static string F(double v,string format="+0.00;-0.00;0.00")=>v.ToString(format,System.Globalization.CultureInfo.InvariantCulture);
  private Button Question(string title,string text)
  {
   var button=new Button{Content="?",Width=32,Height=32,MinWidth=0,Padding=new Thickness(0),VerticalAlignment=VerticalAlignment.Center};
   Windows.UI.Xaml.Automation.AutomationProperties.SetName(button,OnexStrings.Get(title));
   var content=new StackPanel{Spacing=8,MaxWidth=420};content.Children.Add(Label(title,16));content.Children.Add(Label(text));
   var popup=new Flyout{Content=content};help.Add(popup);button.Click+=(s,e)=>popup.ShowAt(button);return button;
  }
  internal bool CloseHelp(){foreach(var popup in help)if(popup.IsOpen){popup.Hide();return true;}return false;}
  private Grid Heading(string title,string explanation)
  {
   var row=new Grid{ColumnSpacing=6};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.Children.Add(Label(title,16));var q=Question(title,explanation);Grid.SetColumn(q,1);row.Children.Add(q);return row;
  }
  private Border Card(UIElement content)=>new Border{Child=content,Padding=new Thickness(12),CornerRadius=new CornerRadius(10),Background=Brush(palette.Surface),BorderBrush=GridInk,BorderThickness=new Thickness(1)};
  internal OnexGyroDashboard(OnexApprovedTheme theme = null)
  {
   palette = theme ?? OnexApprovedTheme.Current;
   Ink = Brush(palette.Text);
   Muted = Brush(palette.Muted);
   GridInk = Brush(palette.DiagramFrame);
   Purple = Brush(palette.Accent(OnexThemeRole.Controller));
   Cyan = Brush(palette.Accent(OnexThemeRole.Cooling));
   Amber = Brush(palette.Accent(OnexThemeRole.Power));
   stage = Label("Connecting…",14);
   poseLabel = Label("Relative orientation",12);
   gravityLabel = Label("—",12);
   target = Label("—",14);
   model = new OnexGyroConsoleView(palette);
   Panel.Children.Add(stage);
   var top=new Grid{ColumnSpacing=12};top.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(2,GridUnitType.Star)});top.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(3,GridUnitType.Star)});
   var art=new StackPanel{Spacing=6};
   art.Children.Add(Heading("Console movement","The 3D model follows the calibrated rotation of the console. Gravity stabilizes tilt. Relative rotation shows the angle components along X, Y and Z. Rotation about gravity can drift. Recenter sets a new reference for the view."));
   art.Children.Add(model.View);
   var recenter=new Button{Content=OnexStrings.Get("Recenter view"),MinHeight=32,HorizontalAlignment=HorizontalAlignment.Left};recenter.Click+=(s,e)=>{Recenter();};art.Children.Add(recenter);art.Children.Add(poseLabel);top.Children.Add(Card(art));
   var rates=new StackPanel{Spacing=4};rates.Children.Add(Heading("Angular speed · Raw / calibrated","The dials show angular speed in °/s on the gyroscope’s own axes. The dashed needle shows the raw value. Colour shows the value after idle calibration. The dials span ±180°/s and the numbers show the full reading."));
   var dials=new Grid{ColumnSpacing=8};for(int a=0;a<3;a++){dials.ColumnDefinitions.Add(new ColumnDefinition());gauges[a]=new Gauge(this,"XYZ"[a].ToString(),a==0?Cyan:a==1?Purple:Amber);Grid.SetColumn(gauges[a].View,a);dials.Children.Add(gauges[a].View);}rates.Children.Add(dials);var rateCard=Card(rates);Grid.SetColumn(rateCard,1);top.Children.Add(rateCard);Panel.Children.Add(top);
   var bottom=new Grid{ColumnSpacing=12};bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(3,GridUnitType.Star)});bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(2,GridUnitType.Star)});
   var aiming=new StackPanel{Spacing=5};aiming.Children.Add(Heading("Processed aiming preview","This independent test uses the selected profile and the same processing code as gameplay, but sends no test output to games. Hold/Toggle/Boost follow your physical buttons. Off or inactive activation gives no output. Mouse counts are per measured tick, not an angle. Aiming settings do not rotate the console drawing."));aiming.Children.Add(target);
   var table=new Grid{ColumnSpacing=10,RowSpacing=4};for(int c=0;c<3;c++)table.ColumnDefinitions.Add(new ColumnDefinition{Width=c==0?new GridLength(2,GridUnitType.Star):new GridLength(1,GridUnitType.Star)});
   table.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});for(int c=1;c<3;c++){var heading=Label(c==1?"Horizontal":"Vertical",12);Grid.SetColumn(heading,c);table.Children.Add(heading);}
   var names=new[]{"Resolved °/s","Filtered °/s","Target contribution"};for(int row=0;row<3;row++){table.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});var caption=Label(names[row]);Grid.SetRow(caption,row+1);table.Children.Add(caption);for(int c=0;c<2;c++){var value=axisValues[row*2+c]=Label("—");value.FontFamily=new FontFamily("Consolas");Grid.SetRow(value,row+1);Grid.SetColumn(value,c+1);table.Children.Add(value);}}aiming.Children.Add(table);
   var effectsGrid=new Grid{ColumnSpacing=8,RowSpacing=2};effectsGrid.ColumnDefinitions.Add(new ColumnDefinition());effectsGrid.ColumnDefinitions.Add(new ColumnDefinition());effectsGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});effectsGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});aiming.Children.Add(effectsGrid);
   AddEffect(effectsGrid,"calibration","Calibration","Subtracts the saved idle angular bias on each sensor axis. Acceleration still includes gravity.");
   AddEffect(effectsGrid,"axes","Axes","Gravity-relative or local yaw/roll selection resolves two aiming channels. Rotation and inversion transform those output channels.");
   AddEffect(effectsGrid,"gate","Gate / curve","Gate rejects small 2D rates and clears the filter tail. A custom curve can reject input below its own deadzone and shapes stick output. Anti-deadzone applies only to nonzero standard stick output.");
   AddEffect(effectsGrid,"filter","Filter / gain","Linear is direct; Adaptive smooths changing rates; Precision also uses speed-based gain. Sensitivity and a held Boost button scale the output. The table shows the actual stages reached, not invented values for inactive stages.");
   bottom.Children.Add(Card(aiming));
   var accel=new StackPanel{Spacing=8};accel.Children.Add(Heading("Accelerometer · g","Acceleration includes gravity. When held at an angle, nonzero X/Y/Z is normal. The magnitude is usually around 1g at rest; dynamic movement adds acceleration. Gyro calibration does not remove gravity."));
   for(int a=0;a<3;a++){var block=new StackPanel{Spacing=3};acceleration[a]=Label("—",14);block.Children.Add(acceleration[a]);var canvas=new Canvas{Width=300,Height=12,Background=Brush(palette.Inset)};canvas.Children.Add(new Line{X1=150,X2=150,Y1=0,Y2=12,Stroke=Muted,StrokeThickness=1});var bar=accelBars[a]=new Rectangle{Width=0,Height=8,Fill=a==0?Cyan:a==1?Purple:Amber};Canvas.SetTop(bar,2);Canvas.SetLeft(bar,150);canvas.Children.Add(bar);block.Children.Add(new Viewbox{Child=canvas,Stretch=Stretch.Uniform,MaxHeight=12});accel.Children.Add(block);}accel.Children.Add(gravityLabel);accel.Children.Add(Label("Raw sensor and profile preview · no settings are changed",12));var accelCard=Card(accel);Grid.SetColumn(accelCard,1);bottom.Children.Add(accelCard);Panel.Children.Add(bottom);
   top.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});top.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});bottom.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});bottom.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
   Panel.SizeChanged+=(s,e)=>{bool narrow=Panel.ActualWidth<820;Grid.SetColumnSpan(top.Children[0] as FrameworkElement,narrow?2:1);Grid.SetColumnSpan(rateCard,narrow?2:1);Grid.SetColumn(rateCard,narrow?0:1);Grid.SetRow(rateCard,narrow?1:0);Grid.SetColumnSpan(bottom.Children[0] as FrameworkElement,narrow?2:1);Grid.SetColumnSpan(accelCard,narrow?2:1);Grid.SetColumn(accelCard,narrow?0:1);Grid.SetRow(accelCard,narrow?1:0);};
   ClearSample();
  }
  private void AddEffect(Grid panel,string key,string title,string description)
  {
   var line=new Grid{ColumnSpacing=8};line.ColumnDefinitions.Add(new ColumnDefinition());line.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var value=effects[key]=Label(title+": —",12);line.Children.Add(value);var q=Question(title,description);Grid.SetColumn(q,1);line.Children.Add(q);int index=panel.Children.Count;Grid.SetColumn(line,index%2);Grid.SetRow(line,index/2);panel.Children.Add(line);
  }
  private void UpdatePoseLabel()
  {
   Vector3 angles = pose.RotationDegrees;
   poseLabel.Text = OnexStrings.Get("Relative rotation · X / Y / Z") + " · " +
    F(angles.X,"0.0") + "° / " + F(angles.Y,"0.0") + "° / " + F(angles.Z,"0.0") + "°";
  }
  internal void ClearSample()
  {
   if(HasSample)Recenter();HasSample=valid=false;poseLabel.Text="—";stage.Text=OnexStrings.Get("Waiting for fresh motion data");target.Text="—";
   foreach(var gauge in gauges)gauge.Clear();foreach(var text in acceleration)text.Text="—";foreach(var bar in accelBars)bar.Width=0;foreach(var text in axisValues)text.Text="—";foreach(var text in effects.Values)text.Text="—";gravityLabel.Text="—";
  }
  internal void ApplySample(JsonObject state, double? receivedAt = null)
  {
   var sample=Obj(state,"motion");var preview=Obj(state,"gyroPreview");var raw=Obj(preview,"raw")??Obj(sample,"angular");var corrected=Obj(preview,"calibrated");var accel=Obj(sample,"acceleration");
   if(!B(state,"available")||raw==null||corrected==null||accel==null){ClearSample();return;}
   double gyroAge=N(sample,"gyroAgeMilliseconds",99999),accelAge=N(sample,"accelerationAgeMilliseconds",99999);
   bool gyroFresh=gyroAge>=0&&gyroAge<=500,accelFresh=accelAge>=0&&accelAge<=250;
   HasSample=true;valid=gyroFresh&&accelFresh;string currentStage=S(preview,"stage","SensorUnavailable");stage.Text=OnexStrings.Get("Profile preview")+" · "+OnexStrings.Get(currentStage)+(valid?"":" · "+OnexStrings.Get("Held sensor values"));
   double[] a={N(raw,"x"),N(raw,"y"),N(raw,"z")},c={N(corrected,"x"),N(corrected,"y"),N(corrected,"z")};for(int i=0;i<3;i++)gauges[i].Set(a[i],c[i]);
   double norm=0;for(int i=0;i<3;i++){double value=N(accel,("xyz"[i]).ToString());norm+=value*value;acceleration[i].Text="XYZ"[i]+"  "+F(value)+" g";double width=Math.Min(1,Math.Abs(value)/2)*150;accelBars[i].Width=width;Canvas.SetLeft(accelBars[i],value<0?150-width:150);}
   gravityLabel.Text=OnexStrings.Get("Magnitude")+": "+Math.Sqrt(norm).ToString("0.000",System.Globalization.CultureInfo.InvariantCulture)+" g";
   var settings=Obj(preview,"settings");var processing=Obj(settings,"processing");string t=S(settings,"target","Disabled");target.Text=OnexStrings.Get(t)+" · "+OnexStrings.Get(S(settings,"activation","Hold"))+" / "+S(settings,"activationButton","None");
   if(!B(preview,"virtualMode"))target.Text+=" · "+OnexStrings.Get("Preview only · hardware mode");
   var resolved=Obj(preview,"axes");var filtered=Obj(preview,"filtered");var output=Obj(preview,"output");string unit=S(preview,"outputUnit","none");
   for(int i=0;i<2;i++){string key=i==0?"x":"y";axisValues[i].Text=resolved==null?"—":F(N(resolved,key));axisValues[2+i].Text=filtered==null?"—":F(N(filtered,key));axisValues[4+i].Text=output==null?"—":unit=="stickCounts"?F(N(output,key)/(N(output,key)<0?32768:32767)*100)+"%":F(N(output,key),"+0;-0;0")+" Δ";}
   if(unit=="mouseCountsPerTick")target.Text+=" · "+N(preview,"intervalMilliseconds").ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+" ms/tick";
   effects["calibration"].Text=OnexStrings.Get("Calibration")+": "+OnexStrings.Get(B(preview,"calibrationApplied")?"Saved correction":"Not calibrated");
   effects["axes"].Text=OnexStrings.Get("Axes")+": "+OnexStrings.Get(B(processing,"worldSpace")?"Gravity-relative":"Console axes")+(B(processing,"worldSpace")?"":" · "+OnexStrings.Get(t=="Mouse"?"Yaw":S(processing,"stickConversion","Yaw")))+" · "+N(processing,"outputRotation")+"°"+(B(processing,"invertX")?" · X−":"")+(B(processing,"invertY")?" · Y−":"");
   effects["gate"].Text=OnexStrings.Get("Gate / curve")+": "+(N(processing,"gateTenths")/10).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"°/s · "+OnexStrings.Get(t=="Mouse"?"Mouse output":B(Obj(processing,"stickCurve"),"enabled")?"Custom curve":"Standard response")+ (t!="Mouse"&&!B(Obj(processing,"stickCurve"),"enabled")?" · "+OnexStrings.Format("anti-deadzone {0}%",N(processing,"antiDeadzone")):"")+" · "+OnexStrings.Get(currentStage);
   var gain=Obj(preview,"gain");effects["filter"].Text=OnexStrings.Get("Filter / gain")+": "+S(processing,"engine","Linear")+" · "+(gain==null?"—":F(N(gain,"x"),"0.00")+"× / "+F(N(gain,"y"),"0.00")+"×");
   var gravity = new Vector3((float)N(accel,"x"),(float)N(accel,"y"),(float)N(accel,"z"));
   var rate = new Vector3((float)c[0],(float)c[1],(float)c[2]);
   if (gyroFresh || accelFresh)
   {
    pose.Observe(accelFresh ? gravity : (Vector3?)null,rate,(long)N(sample,"sequence",-1),gyroAge,receivedAt ?? clock.Elapsed.TotalSeconds);
    model.SetPose(pose.Orientation);
   }
   else pose.Pause();
   UpdatePoseLabel();
  }
  private sealed class Gauge
  {
   internal readonly Viewbox View;private readonly Line raw,corrected;private readonly Rectangle rawBar,correctedBar;private readonly TextBlock numbers;
   internal Gauge(OnexGyroDashboard owner,string caption,Brush color)
   {
    var canvas=new Canvas{Width=180,Height=244};canvas.Children.Add(new Ellipse{Width=158,Height=158,Margin=new Thickness(11,6,0,0),Fill=Brush(owner.palette.Inset),Stroke=owner.GridInk,StrokeThickness=1.5});
    for(int i=-2;i<=2;i++){double angle=i*67.5*Math.PI/180;canvas.Children.Add(new Line{X1=90+Math.Sin(angle)*68,Y1=85-Math.Cos(angle)*68,X2=90+Math.Sin(angle)*75,Y2=85-Math.Cos(angle)*75,Stroke=owner.Muted,StrokeThickness=1});}
    raw=new Line{X1=90,Y1=85,X2=90,Y2=20,Stroke=owner.Ink,StrokeThickness=1,StrokeDashArray=new DoubleCollection{3,3}};corrected=new Line{X1=90,Y1=85,X2=90,Y2=20,Stroke=color,StrokeThickness=3,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};canvas.Children.Add(raw);canvas.Children.Add(corrected);canvas.Children.Add(new Ellipse{Width=8,Height=8,Fill=color,Margin=new Thickness(86,81,0,0)});
    var label=owner.Label(caption,14);label.Width=180;label.TextAlignment=TextAlignment.Center;Canvas.SetTop(label,168);canvas.Children.Add(label);numbers=owner.Label("—",12);numbers.Width=180;numbers.TextAlignment=TextAlignment.Center;Canvas.SetTop(numbers,190);canvas.Children.Add(numbers);
    var range=new Canvas{Width=158,Height=15,Background=Brush(owner.palette.Raised)};Canvas.SetLeft(range,11);Canvas.SetTop(range,224);canvas.Children.Add(range);range.Children.Add(new Line{X1=79,X2=79,Y1=0,Y2=15,Stroke=owner.Muted,StrokeThickness=1});
    rawBar=new Rectangle{Height=4,Fill=owner.Ink};correctedBar=new Rectangle{Height=5,Fill=color};Canvas.SetTop(rawBar,2);Canvas.SetTop(correctedBar,8);range.Children.Add(rawBar);range.Children.Add(correctedBar);View=new Viewbox{Child=canvas,Stretch=Stretch.Uniform,MaxHeight=244};
   }
   internal void Set(double a,double b){raw.Visibility=corrected.Visibility=Visibility.Visible;void Needle(Line l,double value){double angle=Math.Max(-180,Math.Min(180,value))*135/180*Math.PI/180;l.X2=90+Math.Sin(angle)*64;l.Y2=85-Math.Cos(angle)*64;}Needle(raw,a);Needle(corrected,b);void Bar(Rectangle bar,double value){double length=Math.Min(1,Math.Abs(value)/180)*79;bar.Width=length;Canvas.SetLeft(bar,value<0?79-length:79);}Bar(rawBar,a);Bar(correctedBar,b);numbers.Text=F(a)+" / "+F(b)+"°/s";}
   internal void Clear(){raw.Visibility=corrected.Visibility=Visibility.Collapsed;numbers.Text="—";rawBar.Width=correctedBar.Width=0;}
  }
 }
}
