// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private Button _onexControllerSticksTest,_onexControllerGyroTest;
  private string _onexControllerDiagnosticKind="",_onexControllerDiagnosticToken="";
  private bool _onexControllerDiagnosticReadBusy;
  private string _onexSticksHostOwner;
  private OnexControllerAnalogReadouts _onexAnalogReadouts;
  private JsonObject _onexSticksHostLast,_onexGyroHostLast;
  private string _onexControllerDiagnosticReturnKind;
  private bool _onexControllerDiagnosticReturnVisible;
  private int _onexControllerDiagnosticReturnRevision;
  private void ClearOnexControllerDiagnosticReturn()
  {_onexControllerDiagnosticReturnKind=null;_onexControllerDiagnosticReturnVisible=false;_onexControllerDiagnosticReturnRevision++;}
  private void PrepareOnexControllerDiagnosticReturn(string kind)
  {_onexControllerDiagnosticReturnKind=kind;_onexControllerDiagnosticReturnVisible=false;_onexControllerDiagnosticReturnRevision++;}
  private void OnexControllerDiagnosticReturnHidden()
  {
   // Keep the originating page while a separate test owns the screen. Once
   // returned, the next ordinary close/open uses the saved startup tab again.
   if(_onexControllerDiagnosticReturnVisible)ClearOnexControllerDiagnosticReturn();
  }
  private bool RestoreOnexControllerDiagnosticReturn()
  {
   string kind=_onexControllerDiagnosticReturnKind;
   if(kind==null||_releaseHome==null||_onexControllerSections==null)return false;
   if(LegionNavItem.IsChecked!=true)LegionNavItem.IsChecked=true;
   if(_onexControllerOpen!=kind)SetOnexControllerCard(kind);
   if(widget?.Visible==true&&widget.GameBarDisplayMode==Microsoft.Gaming.XboxGameBar.XboxGameBarDisplayMode.Foreground)
   {
    _onexControllerDiagnosticReturnVisible=true;int revision=_onexControllerDiagnosticReturnRevision;
    var anchor=kind=="gyro"?_onexControllerGyroTest:_onexControllerSticksTest;
    // Page-entry focus is also queued at Low priority. Run after it, and cancel
    // this work if the user chooses another page or the widget closes meanwhile.
    _=Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,()=>{
     if(revision!=_onexControllerDiagnosticReturnRevision||LegionNavItem.IsChecked!=true||_onexControllerOpen!=kind||
        widget?.Visible!=true||widget.GameBarDisplayMode!=Microsoft.Gaming.XboxGameBar.XboxGameBarDisplayMode.Foreground||_onexDetailsDialog!=null)return;
     anchor.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});anchor.Focus(FocusState.Keyboard);
    });
   }
   return true;
  }
  private TextBlock _onexControllerDiagnosticStatus;private JsonObject _onexControllerDiagnosticLast;
  private OnexControllerStickPlot _onexStickPlot;
  private readonly List<TextBlock> _onexControllerDiagnosticNumbers=new List<TextBlock>();
  private readonly List<Rectangle> _onexControllerDiagnosticBars=new List<Rectangle>();
  private readonly List<TextBlock> _onexControllerDiagnosticAxes=new List<TextBlock>();
  private Border _onexControllerDiagnosticConsole;private PlaneProjection _onexControllerDiagnosticTilt;
  private bool _onexControllerDiagnosticOrigin;private double _onexControllerDiagnosticPitch,_onexControllerDiagnosticRoll;
  private Button CreateOnexControllerDiagnosticButton(string kind)
  {
   var button=new Button{Name=kind=="sticks"?"OnexControllerSticksTest":"OnexControllerGyroTest",Content=kind=="sticks"?global::XboxGamingBar.OnexStrings.Get("Test sticks & triggers"):global::XboxGamingBar.OnexStrings.Get("Test gyro"),Style=(Style)Resources["StandaloneButtonStyle"],HorizontalAlignment=HorizontalAlignment.Stretch};
   button.Click+=async(s,e)=>await ShowOnexControllerDiagnosticAsync(kind,button);
   return button;
  }
  private static TextBlock OnexDiagnosticLabel(Canvas canvas,string text,double x,double y,double width,double size=14)
  {
   var block=new TextBlock{Text=text,Width=width,FontSize=size,TextAlignment=TextAlignment.Center,Foreground=new SolidColorBrush(Colors.White)};
   Canvas.SetLeft(block,x);Canvas.SetTop(block,y);canvas.Children.Add(block);return block;
  }
  private static Ellipse OnexDiagnosticCircle(Canvas canvas,double x,double y,double size,Brush fill,Brush stroke,double thickness=1)
  {
   var circle=new Ellipse{Width=size,Height=size,Fill=fill,Stroke=stroke,StrokeThickness=thickness};Canvas.SetLeft(circle,x);Canvas.SetTop(circle,y);canvas.Children.Add(circle);return circle;
  }
  private static void OnexDiagnosticLine(Canvas canvas,double x1,double y1,double x2,double y2,Brush brush,double thickness=1)
  {canvas.Children.Add(new Line{X1=x1,Y1=y1,X2=x2,Y2=y2,Stroke=brush,StrokeThickness=thickness});}
  private Panel BuildOnexControllerDiagnosticPanel(string kind)
  {
   _onexControllerDiagnosticNumbers.Clear();_onexControllerDiagnosticBars.Clear();_onexControllerDiagnosticAxes.Clear();_onexControllerDiagnosticOrigin=false;_onexControllerDiagnosticLast=null;
   var panel=new StackPanel{Name="OnexControllerDiagnosticPanel",Spacing=12};
   _onexControllerDiagnosticStatus=ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Connecting…"));panel.Children.Add(_onexControllerDiagnosticStatus);
   var canvas=new Canvas{Width=600,Height=250,Background=OnexSettingsBrush(19,24,31)};
   var cyan=new SolidColorBrush(OnexPalette.Cyan);var purple=new SolidColorBrush(OnexPalette.Violet);var faint=OnexSettingsBrush(69,81,95);var white=new SolidColorBrush(Colors.White);
   _onexStickPlot=null;
   if(kind=="sticks")
   {
    _onexStickPlot=new OnexControllerStickPlot();panel.Children.Add(_onexStickPlot.Panel);
    _onexControllerDiagnosticNumbers.AddRange(_onexStickPlot.Numbers);
   }
   else
   {
    var console=new Border{Width=330,Height=151,CornerRadius=new CornerRadius(44),Background=OnexSettingsBrush(37,44,55),BorderBrush=OnexSettingsBrush(117,132,149),BorderThickness=new Thickness(2),RenderTransformOrigin=new Windows.Foundation.Point(.5,.5)};
    _onexControllerDiagnosticConsole=console;_onexControllerDiagnosticTilt=new PlaneProjection();console.Projection=_onexControllerDiagnosticTilt;var face=new Canvas();
    var screen=new Border{Width=215,Height=123,CornerRadius=new CornerRadius(5),Background=OnexSettingsBrush(10,16,24),BorderBrush=faint,BorderThickness=new Thickness(2)};Canvas.SetLeft(screen,56);Canvas.SetTop(screen,12);face.Children.Add(screen);
    OnexDiagnosticLabel(face,global::XboxGamingBar.OnexStrings.Get("ONEXPLAYER"),56,63,215,16);
    OnexDiagnosticCircle(face,16,28,25,OnexSettingsBrush(10,16,24),cyan,2);OnexDiagnosticCircle(face,284,91,25,OnexSettingsBrush(10,16,24),purple,2);
    OnexDiagnosticLine(face,19,99,43,99,white,4);OnexDiagnosticLine(face,31,87,31,111,white,4);
    foreach(var point in new[]{new Windows.Foundation.Point(296,29),new Windows.Foundation.Point(284,41),new Windows.Foundation.Point(308,41),new Windows.Foundation.Point(296,53)})OnexDiagnosticCircle(face,point.X-4,point.Y-4,8,faint,null);
    console.Child=face;Canvas.SetLeft(console,135);Canvas.SetTop(console,51);canvas.Children.Add(console);
    foreach(string axis in new[]{"−X","＋X","−Y","＋Y","−Z","＋Z"}){
     int i=_onexControllerDiagnosticAxes.Count;double[] px={15,505,250,250,45,455},py={110,110,6,218,22,216};
     var label=OnexDiagnosticLabel(canvas,axis,px[i],py[i],80,20);label.Foreground=i<2?new SolidColorBrush(OnexPalette.Orange):i<4?cyan:purple;label.Opacity=.4;_onexControllerDiagnosticAxes.Add(label);
    }
    OnexDiagnosticLine(canvas,102,125,126,125,new SolidColorBrush(OnexPalette.Orange),2);OnexDiagnosticLine(canvas,474,125,500,125,new SolidColorBrush(OnexPalette.Orange),2);
    OnexDiagnosticLine(canvas,290,33,290,43,cyan,2);OnexDiagnosticLine(canvas,290,207,290,216,cyan,2);
    OnexDiagnosticLine(canvas,115,46,132,59,purple,2);OnexDiagnosticLine(canvas,469,197,486,211,purple,2);
    void Head(double x,double y,double dx,double dy,Brush brush){OnexDiagnosticLine(canvas,x,y,x-dx*7+dy*4,y-dy*7-dx*4,brush,2);OnexDiagnosticLine(canvas,x,y,x-dx*7-dy*4,y-dy*7+dx*4,brush,2);}
    Head(102,125,-1,0,new SolidColorBrush(OnexPalette.Orange));Head(500,125,1,0,new SolidColorBrush(OnexPalette.Orange));Head(290,33,0,-1,cyan);Head(290,216,0,1,cyan);Head(115,46,-.8,-.6,purple);Head(486,211,.8,.6,purple);
   }
   if(kind=="gyro")panel.Children.Add(new Viewbox{Child=canvas,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Stretch});
   _onexAnalogReadouts=kind=="sticks"?new OnexControllerAnalogReadouts():null;
   if(_onexAnalogReadouts!=null)panel.Children.Add(_onexAnalogReadouts.Panel);
   if(kind=="gyro")
   {
    panel.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Raw gyro (°/s) · Accelerometer (g). Tilt is relative to the starting position.")));
    panel.Children.Add(ControllerHelp(OnexStrings.Get("Acceleration includes gravity. A steady tilt can give a non-zero Accel value. Calibration corrects rotation drift; this test shows raw sensor readings.")));
    for(int axis=0;axis<3;axis++){
     var colour=axis==0?new SolidColorBrush(OnexPalette.Orange):axis==1?cyan:purple;
     var row=new StackPanel{Spacing=3};var label=new TextBlock{Text=("XYZ"[axis])+global::XboxGamingBar.OnexStrings.Get("   Gyro — °/s    ·    Accel — g"),FontSize=14,Foreground=colour};_onexControllerDiagnosticNumbers.Add(label);row.Children.Add(label);
     var bar=new Canvas{Width=500,Height=14,Background=OnexSettingsBrush(32,40,50)};
     OnexDiagnosticLine(bar,250,0,250,14,OnexSettingsBrush(120,131,145));
     for(int channel=0;channel<2;channel++){var fill=new Rectangle{Height=5,Width=0,Fill=colour,Opacity=channel==0?1:.5};Canvas.SetTop(fill,channel==0?1:8);Canvas.SetLeft(fill,250);bar.Children.Add(fill);_onexControllerDiagnosticBars.Add(fill);}
     row.Children.Add(new Viewbox{Child=bar,Stretch=Stretch.Uniform});panel.Children.Add(row);
    }
    panel.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Bars: angular speed ±180°/s (top), acceleration ±2g (bottom). Numbers keep the full measured values.")));
   }
   panel.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Move the sticks / triggers or tilt the console. B / Esc closes this test; other gamepad controls stay inside it. No settings are changed.")));
   return panel;
  }
  private static double OnexDiagnosticNumber(JsonObject value,string key)=>value?.GetNamedNumber(key,0)??0;
  private static string OnexDiagnosticValue(double value)=>value.ToString("+0.0;-0.0;0.0",CultureInfo.InvariantCulture);
  private void ApplyOnexControllerDiagnostic(JsonObject state)
  {
   bool available=state?.GetNamedBoolean("available",false)==true;
   if(available&&_onexControllerDiagnosticKind=="sticks"&&(OnexMotionObject(state,"physical")==null||OnexMotionObject(state,"tuned")==null))throw new InvalidOperationException("Live controller sample is incomplete.");
   _onexControllerDiagnosticLast=state;
   _onexControllerDiagnosticStatus.Text=OnexStrings.Get(ControllerText(state,"status",available?OnexStrings.Get("Live input"):OnexStrings.Get("No data")));
   if(!available){_onexAnalogReadouts?.ClearSample();_onexStickPlot?.ClearSample();foreach(var number in _onexControllerDiagnosticNumbers)number.Text="—";foreach(var axis in _onexControllerDiagnosticAxes)axis.Opacity=.25;foreach(var bar in _onexControllerDiagnosticBars)bar.Width=0;return;}
   if(_onexControllerDiagnosticKind=="sticks")
   {
    _onexAnalogReadouts?.ApplySample(state);
    _onexStickPlot.ApplySample(state);
   }
   else
   {
    var motion=OnexMotionObject(state,"motion");var angular=OnexMotionObject(motion,"angular");var acceleration=OnexMotionObject(motion,"acceleration");
    for(int axis=0;axis<3;axis++){
     string key=("xyz"[axis]).ToString();double g=OnexDiagnosticNumber(angular,key),a=OnexDiagnosticNumber(acceleration,key);
     _onexControllerDiagnosticNumbers[axis].Text=key.ToUpperInvariant()+global::XboxGamingBar.OnexStrings.Get("   Gyro ")+OnexDiagnosticValue(g)+global::XboxGamingBar.OnexStrings.Get(" °/s    ·    Accel ")+OnexDiagnosticValue(a)+global::XboxGamingBar.OnexStrings.Get(" g");
     for(int channel=0;channel<2;channel++){double signed=Math.Max(-1,Math.Min(1,channel==0?g/180:a/2))*250;var bar=_onexControllerDiagnosticBars[axis*2+channel];bar.Width=Math.Abs(signed);Canvas.SetLeft(bar,250+Math.Min(0,signed));}
     _onexControllerDiagnosticAxes[axis*2].Opacity=g<-.5?1:.4;_onexControllerDiagnosticAxes[axis*2+1].Opacity=g>.5?1:.4;
    }
    double ax=OnexDiagnosticNumber(acceleration,"x"),ay=OnexDiagnosticNumber(acceleration,"y"),az=OnexDiagnosticNumber(acceleration,"z");
    double pitch=Math.Atan2(ay,az)*180/Math.PI,roll=Math.Atan2(-ax,Math.Sqrt(ay*ay+az*az))*180/Math.PI;
    if(!_onexControllerDiagnosticOrigin){_onexControllerDiagnosticPitch=pitch;_onexControllerDiagnosticRoll=roll;_onexControllerDiagnosticOrigin=true;}
    double Difference(double a,double b){double d=a-b;return d>180?d-360:d< -180?d+360:d;}
    _onexControllerDiagnosticTilt.RotationX=Math.Max(-28,Math.Min(28,Difference(pitch,_onexControllerDiagnosticPitch)*.5));
    _onexControllerDiagnosticTilt.RotationY=Math.Max(-28,Math.Min(28,Difference(roll,_onexControllerDiagnosticRoll)*.5));
   }
  }
  private static bool IsOnexControllerDiagnosticInput(VirtualKey key)=>key>=VirtualKey.GamepadA&&key<=VirtualKey.GamepadRightThumbstickLeft||key==VirtualKey.Enter||key==VirtualKey.Space||key==VirtualKey.Up||key==VirtualKey.Down||key==VirtualKey.Left||key==VirtualKey.Right||key==VirtualKey.Escape;
  private bool HandleOnexControllerDiagnosticKey(KeyRoutedEventArgs e)
  {
   if(string.IsNullOrEmpty(_onexControllerDiagnosticKind)||!IsOnexControllerDiagnosticInput(e.Key))return false;
   e.Handled=true;if(e.Key==VirtualKey.GamepadB||e.Key==VirtualKey.Escape)_onexDetailsDialog?.Hide();return true;
  }
  private ContentDialog CreateOnexControllerDiagnosticDialog(string kind,Panel panel)
  {
   double width=Math.Max(280,Math.Min(660,ActualWidth-32));
   panel.MaxWidth=Math.Max(232,width-48);panel.HorizontalAlignment=HorizontalAlignment.Stretch;
   var dialog=CreateOnexSettingsDialog(kind=="sticks"?global::XboxGamingBar.OnexStrings.Get("Sticks & triggers · live test"):global::XboxGamingBar.OnexStrings.Get("Gyro · six-axis live test"),"",panel);
   dialog.PrimaryButtonText="";dialog.CloseButtonText=global::XboxGamingBar.OnexStrings.Get("Close");dialog.DefaultButton=ContentDialogButton.None;
   dialog.Resources["ContentDialogMinWidth"]=width;dialog.Resources["ContentDialogMaxWidth"]=width;
   if(dialog.Content is ScrollViewer viewport){viewport.HorizontalContentAlignment=HorizontalAlignment.Stretch;viewport.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;viewport.VerticalScrollMode=ScrollMode.Enabled;viewport.MaxHeight=Math.Max(160,ActualHeight-176);if(viewport.Content is FrameworkElement body){body.MaxWidth=panel.MaxWidth;body.HorizontalAlignment=HorizontalAlignment.Stretch;}}
   return dialog;
  }
  private static async Task<JsonObject> RequestOnexSticksHostAsync(string owner,string operation,bool verify,string kind="sticks")
  {
   var reply=await App.SendMessageAsync(new ValueSet{{"OnexControllerDiagnosticHost",operation},{"OnexDiagnosticWindowOwner",owner},{"OnexVerifyHost",verify},{"OnexDiagnosticKind",kind}},5000);
   if(reply==null||!reply.TryGetValue("OnexControllerHostState",out var state))
    throw new InvalidOperationException(reply!=null&&reply.TryGetValue("Error",out var error)?error.ToString():"Controller test host did not respond");
   return JsonObject.Parse((string)state);
  }
  private async Task ShowOnexControllerDiagnosticAsync(string kind,Button anchor,Func<ValueSet,Task<ValueSet>> transport=null,bool verifyHost=false)
  {
   if(_onexDetailsDialog!=null||_onexControllerBusy||_onexSticksHostOwner!=null)return;await FlushOnexControllerPolicyAsync();
   if(_onexDetailsDialog!=null||_onexControllerBusy||_onexSticksHostOwner!=null)return;
   if(transport==null)PrepareOnexControllerDiagnosticReturn(kind);
   if((kind=="sticks"||kind=="gyro")&&transport==null)
   {
    string hostOwner=Guid.NewGuid().ToString("N");_onexSticksHostOwner=hostOwner;
    try
    {
     _onexSticksHostLast=await RequestOnexSticksHostAsync(hostOwner,"start",verifyHost,kind);
     var deadline=DateTimeOffset.UtcNow.AddSeconds(35);
     while(_onexSticksHostLast.GetNamedBoolean("active")&&(verifyHost||!_onexSticksHostLast.GetNamedBoolean("foreground")))
     {
      if(DateTimeOffset.UtcNow>deadline)throw new TimeoutException("Controller test host did not acquire focus");
      await Task.Delay(250);_onexSticksHostLast=await RequestOnexSticksHostAsync(hostOwner,"status",false);
     }
     if(_onexSticksHostLast.GetNamedValue("error").ValueType==JsonValueType.String)
      throw new InvalidOperationException(_onexSticksHostLast.GetNamedString("error"));
    }
    catch(Exception error){ClearOnexControllerDiagnosticReturn();Logger.Warn("Controller test host: "+error.Message);if(verifyHost)throw;ShowOnexDesktopStatus(error.Message);}
    finally{if(kind=="gyro")_onexGyroHostLast=_onexSticksHostLast;_onexSticksHostOwner=null;}
    return;
   }
   var owner=App.PipeClient;var panel=BuildOnexControllerDiagnosticPanel(kind);var dialog=CreateOnexControllerDiagnosticDialog(kind,panel);
   _onexDetailsDialog=dialog;_onexControllerDiagnosticKind=kind;_onexControllerDiagnosticToken="";
   string token="";bool closed=false,busy=false;int failures=0;DateTimeOffset nextRead=DateTimeOffset.MinValue;
   var refresh=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
   bool OwnsDialog()=>!closed&&ReferenceEquals(_onexDetailsDialog,dialog);
   bool OwnsConnection()=>transport!=null||App.IsCurrentConnectedPipeClient(owner);
   Task<ValueSet> Send(ValueSet request)=>transport!=null?transport(request):OwnsConnection()?App.SendMessageAsync(request,request.ContainsKey("OnexControllerDiagnosticGet")?1500:5000):Task.FromResult<ValueSet>(null);
   ValueSet SessionRequest(string operation,string lease=""){
    var change=new JsonObject{{"operation",JsonValue.CreateStringValue(operation)}};
    if(operation=="diagnosticsStart")change["kind"]=JsonValue.CreateStringValue(kind);else change["token"]=JsonValue.CreateStringValue(lease);
    return new ValueSet{{"OnexControllerSet",change.Stringify()},{"OnexUserInput",true}};
   }
   async Task Stop(string lease){if(!string.IsNullOrEmpty(lease)&&OwnsConnection())try{await Send(SessionRequest("diagnosticsStop",lease));}catch{}}
   void Stale(string status){
    if(OwnsDialog())ApplyOnexControllerDiagnostic(new JsonObject{{"active",JsonValue.CreateBooleanValue(true)},{"available",JsonValue.CreateBooleanValue(false)},{"status",JsonValue.CreateStringValue(status)}});
   }
   void CheckReply(ValueSet reply){
    if(reply==null)throw new InvalidOperationException("Live test lost its connection.");
    if(reply.TryGetValue("Error",out var error))throw new InvalidOperationException(error?.ToString()??"Live test request failed.");
   }
   async Task Read(){
    if(busy||!OwnsDialog()||DateTimeOffset.UtcNow<nextRead)return;
    if(!OwnsConnection()){Stale("Connection changed. Close and reopen this test.");refresh.Stop();return;}
    busy=true;_onexControllerDiagnosticReadBusy=true;
    try{
     if(string.IsNullOrEmpty(token)){
      // Transient diagnostics own their lease directly. Periodic controller
      // pushes must not replace it or reproject the entire profile editor.
      var started=await Send(SessionRequest("diagnosticsStart"));CheckReply(started);
      if(!started.TryGetValue("OnexControllerState",out var payload)||!(payload is string json))throw new InvalidOperationException("Live test session is missing.");
      var session=OnexMotionObject(JsonObject.Parse(json),"diagnostics");string lease=ControllerText(session,"token","");
      if(session?.GetNamedBoolean("active",false)!=true||string.IsNullOrEmpty(lease))throw new InvalidOperationException("Live test did not start.");
      if(!OwnsDialog()||!OwnsConnection()){await Stop(lease);return;}
      token=lease;_onexControllerDiagnosticToken=token;
     }
     var reply=await Send(new ValueSet{{"OnexControllerDiagnosticGet",token}});
     if(!OwnsDialog()||!OwnsConnection())return;CheckReply(reply);
     if(!reply.TryGetValue("OnexControllerDiagnosticState",out var sample)||!(sample is string sampleJson))throw new InvalidOperationException("Live controller sample is missing.");
     var state=JsonObject.Parse(sampleJson);
     if(!state.GetNamedBoolean("active",false)){
      token="";_onexControllerDiagnosticToken="";
      throw new InvalidOperationException("Live test session expired.");
     }
     ApplyOnexControllerDiagnostic(state);failures=0;nextRead=DateTimeOffset.MinValue;
    }catch(Exception error){
     if(OwnsDialog()){
      failures++;Logger.Warn("ONEX live controller test "+kind+" attempt "+failures+": "+error.Message);
      Stale(failures>=5?"Live test could not reconnect. Close and reopen it.":"Reconnecting live test…");
      // A lost Begin acknowledgement can leave an unknown five-second
      // helper lease. Let it expire before asking for another token.
      nextRead=DateTimeOffset.UtcNow.AddMilliseconds(string.IsNullOrEmpty(token)?5250:Math.Min(1000,250*failures));
      if(failures>=5)refresh.Stop();
     }
    }finally{busy=false;_onexControllerDiagnosticReadBusy=false;}
   }
   dialog.PreviewKeyDown+=(s,e)=>HandleOnexControllerDiagnosticKey(e);
   dialog.Closed+=(s,e)=>{closed=true;refresh.Stop();};
   refresh.Tick+=async(s,e)=>await Read();
   try{
    var shown=dialog.ShowAsync();refresh.Start();await Read();await shown;
   }
   finally{
    closed=true;refresh.Stop();string lease=token;token="";
    if(ReferenceEquals(_onexDetailsDialog,dialog)){
     _onexDetailsDialog=null;_onexControllerDiagnosticToken="";_onexControllerDiagnosticKind="";
    }
    await Stop(lease);
    if(_onexDetailsDialog==null)
    {
     if(transport==null)RestoreOnexControllerDiagnosticReturn();
     else anchor.Focus(FocusState.Programmatic);
    }
   }
  }

 }
}
