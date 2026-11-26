// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using ONEXTweaks.Fans;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private sealed class OnexFanCurveForm
  {
   internal bool Ac,Sync,Writing,ContextAc,ContextSplit,ContextInactive;internal string ProfileId,DraftId,DrawingKey;internal bool DraftOnAc;
   internal StackPanel Root;internal TextBlock Heading,Context,Status;internal ToggleSwitch Custom;
   internal Slider[] Sliders;internal TextBlock[] Labels;internal Grid Chart;internal Canvas Canvas;internal Button Readback,Reset;
   internal DispatcherTimer Timer;internal JsonObject Draft;
   internal int[] CustomPoints;
  }
  private OnexFanCurveForm _onexFanDcForm,_onexFanAcForm;
  private IEnumerable<OnexFanCurveForm> OnexFanForms=>new[]{_onexFanDcForm,_onexFanAcForm}.Where(form=>form!=null);
  // Preserve the original DC control references for existing input diagnostics.
  private Slider[] _onexFanCurveSliders=>_onexFanDcForm?.Sliders;
  private ToggleSwitch _onexFanCustomToggle=>_onexFanDcForm?.Custom;
  private Button _onexFanReadback=>_onexFanDcForm?.Readback;
  private Button _onexFanResetCurve=>_onexFanDcForm?.Reset;
  private TextBlock _onexFanCurveStatus=>_onexFanDcForm?.Status;
  private DispatcherTimer _onexFanCurveTimer=>_onexFanDcForm?.Timer;
  private JsonObject _onexFanCurveDraft{get=>_onexFanDcForm?.Draft;set{if(_onexFanDcForm!=null)_onexFanDcForm.Draft=value;}}
  private bool _onexFanCurveWriting=>OnexFanForms.Any(form=>form.Writing);
  private bool OnexFanCurvePending=>OnexFanForms.Any(form=>form.Writing||form.Draft!=null||form.Sliders.Any(OnexInputGuard.IsPreviewing));
  private double? _onexFanCpuTemperature;
  private static readonly string[] OnexFanCurveKeys=FanCurvePolicy.TemperaturePoints().Select(value=>"at"+value).ToArray();
  private TextBlock _onexFanLiveRpm,_onexFanLiveTemperature,_onexFanLiveMode,_onexFanLiveDuty;
  private const double FanPlotTop=32, FanPlotHeight=220, FanThumbSize=16;
  private const string FanEditorHelp="A starts editing or saves. ↑/↓ adjusts by 5 percentage points. B cancels. Outside editing, ←/→ selects a point. Touch adjusts by 1 percentage point. Fan duty cannot decrease as temperature rises.";
  private const string FanProtectionHelp="Minimum: 15% at 25°C, 50% at 95°C. Full speed at 100°C.";
  private JsonObject OnexSelectedFanEntry=>OnexPreset(_onexFanProfiles,_onexFanProfiles?.GetNamedString("activeId","global")??"global");
  private JsonObject OnexFanFormValues(OnexFanCurveForm form)=>OnexSelectedFanEntry?.GetNamedObject(form.Ac?"ac":"dc",null);
  private bool OnexFanFormLive(OnexFanCurveForm form)=>_onexFanProfiles!=null&&!_onexFanProfiles.GetNamedBoolean("editingInactiveProfile",false)&&(OnexSelectedFanEntry?.GetNamedBoolean("split",false)!=true||form.Ac==_onexFanProfiles.GetNamedBoolean("onAc",false));
  private OnexFanCurveForm BuildOnexFanCurveEditor(bool ac)
  {
   var f=new OnexFanCurveForm{Ac=ac,Root=new StackPanel{Spacing=12,Name=ac?"OnexFanAcEditor":"OnexFanDcEditor",Visibility=ac?Visibility.Collapsed:Visibility.Visible}};string prefix=ac?"OnexFanAc":"OnexFanDc";
   f.Heading=new TextBlock{Text=ac?global::XboxGamingBar.OnexStrings.Get("AC (Alternating Current): Power from a wall outlet"):global::XboxGamingBar.OnexStrings.Get("DC (Direct Current): Power from a battery"),FontSize=17,FontWeight=Windows.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};f.Heading.Visibility=Visibility.Collapsed;f.Root.Children.Add(f.Heading);
   var body=new StackPanel{Spacing=12};f.Root.Children.Add(OnexSettingsCard(body));
   body.Children.Add(new TextBlock{Text=OnexStrings.Get("Fan Control"),FontSize=16,
    FontWeight=Windows.UI.Text.FontWeights.SemiBold,Margin=new Thickness(0,0,0,4)});
   var customLabel=new TextBlock{Text=OnexStrings.Get("Custom curve"),TextWrapping=TextWrapping.Wrap,
    VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,16,0)};
   f.Custom=new ToggleSwitch{Name=prefix+"CustomCurve",OnContent=OnexStrings.Get("On"),OffContent=OnexStrings.Get("Off (Auto)")};
   AutomationProperties.SetName(f.Custom,customLabel.Text);
   OnexInputGuard.Wire(f.Custom);ApplyOnexTileWindowControlAccent(f.Custom);
   f.Custom.Toggled+=(sender,args)=>
   {
    if(f.Sync||!OnexInputGuard.ConsumeToggleInput(f.Custom))return;
    if(f.Custom.IsOn)
    {
     SetOnexFanCurvePoints(f,f.CustomPoints??OnexDefaultFanCurve());
     QueueOnexFanCurve(f);
    }
    else
    {
     foreach(var slider in f.Sliders)OnexInputGuard.Cancel(slider);
     QueueOnexFanCurve(f);
     SetOnexFanCurvePoints(f,OnexDefaultFanCurve());
    }
   };
   body.Children.Add(OnexSettingsInlineToggle(customLabel,f.Custom));
   f.Context=new TextBlock{Name=prefix+"Context",FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=tileTextBrush};body.Children.Add(f.Context);
   f.Chart=new Grid{Name=prefix+"Chart",Height=292,Padding=new Thickness(8,0,8,0)};
   f.Canvas=new Canvas{IsHitTestVisible=false};
   f.Chart.Children.Add(f.Canvas);
   var bars=new Grid();
   f.Chart.Children.Add(bars);
   int segments=(FanCurvePolicy.FullSpeedTemperature-FanCurvePolicy.AxisStart)/10;
   for(int i=0;i<segments;i++)bars.ColumnDefinitions.Add(new ColumnDefinition());
   f.Sliders=new Slider[FanCurvePolicy.Count];
   f.Labels=new TextBlock[FanCurvePolicy.Count];
   for(int index=0;index<FanCurvePolicy.Count;index++)
   {
    int degrees=FanCurvePolicy.TemperatureAt(index);
    int minimum=FanCurvePolicy.MinimumAt(index);
    var column=new StackPanel{Spacing=4,HorizontalAlignment=HorizontalAlignment.Stretch};
    Grid.SetColumn(column,(degrees-FanCurvePolicy.AxisStart)/10);
    bars.Children.Add(column);
    f.Labels[index]=new TextBlock{Height=20,FontSize=14,FontWeight=Windows.UI.Text.FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center};
    column.Children.Add(f.Labels[index]);
    double travel=FanPlotHeight*(FanCurvePolicy.MaximumDuty-minimum)/(FanCurvePolicy.MaximumDuty-FanCurvePolicy.AxisMinimumDuty);
    var slider=new Slider{Name=prefix+"Point"+degrees,Minimum=minimum,Maximum=FanCurvePolicy.MaximumDuty,Value=minimum,
     Orientation=Orientation.Vertical,Height=travel+FanThumbSize,Width=30,MinWidth=0,Margin=new Thickness(0),
     VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center,Style=(Style)Resources["ModernSliderStyle"],
     Foreground=new SolidColorBrush(OnexFanPointColor(degrees)),IsThumbToolTipEnabled=false,IsTabStop=true,UseSystemFocusVisuals=true};
    f.Sliders[index]=slider;OnexInputGuard.ConfigurePercentage(slider, editVertically: true);OnexInputGuard.SetCommit(slider,()=>QueueOnexFanCurve(f));OnexInputGuard.Wire(slider,()=>QueueOnexFanCurve(f));
    var sliderCell=new Grid{Height=FanPlotHeight+FanThumbSize,HorizontalAlignment=HorizontalAlignment.Center};
    var editOutline=new Border{CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(2),
     IsHitTestVisible=false,Height=slider.Height,VerticalAlignment=VerticalAlignment.Top,Visibility=Visibility.Collapsed};
    sliderCell.Children.Add(slider);sliderCell.Children.Add(editOutline);column.Children.Add(sliderCell);
    OnexInputGuard.SetPreviewVisual(slider,editing=>{
     editOutline.BorderBrush=OnexDialogAccent;editOutline.Visibility=editing?Visibility.Visible:Visibility.Collapsed;
    });
    string temperature=degrees+OnexStrings.Get("°C");
    AutomationProperties.SetName(slider,temperature);
    slider.ValueChanged+=(s,e)=>{if(f.Sliders.All(value=>value!=null))DrawOnexFanCurve(f);if(!f.Sync&&OnexInputGuard.IsActive(slider))QueueOnexFanCurve(f);};
    column.Children.Add(new TextBlock{Text=temperature,FontSize=12,HorizontalAlignment=HorizontalAlignment.Center});
   }
   f.Chart.SizeChanged+=(s,e)=>DrawOnexFanCurve(f);
   var graph=new StackPanel();
   graph.Children.Add(f.Chart);
   var axis=new Grid{Padding=new Thickness(8,0,8,4)};
   axis.Children.Add(new TextBlock{Text=FanCurvePolicy.AxisStart+OnexStrings.Get("°C"),FontSize=12});
   axis.Children.Add(new TextBlock{Text=FanCurvePolicy.FullSpeedTemperature+OnexStrings.Get("°C"),FontSize=12,HorizontalAlignment=HorizontalAlignment.Right});
   graph.Children.Add(axis);
   body.Children.Add(OnexSettingsCard(graph));
   body.Children.Add(new TextBlock{Text=OnexStrings.Get(FanProtectionHelp),FontSize=12,Foreground=tileTextBrush,TextWrapping=TextWrapping.Wrap});
   f.Readback=new Button{Name=prefix+"Readback",Content=OnexStrings.Get("Check applied values"),Style=OnexSettingsButtonStyle(false)};
   f.Readback.Click+=async(s,e)=>
   {
    var owner=App.PipeClient;
    try
    {
     await FlushOnexFanCurveAsync(f);
     RequireOnexProfileConnection(owner);
     var reply=await SendOnexHomeRequestAsync(new ValueSet{{"OnexHardwareHomeGet",true}});
     ApplyOnexHardwareHomeState((string)reply["OnexHardwareHomeState"]);
     var fan=_onexHardwareHome?.GetNamedObject("fan",null);
     string error=OnexFanReadbackError(fan);
     int? mode=OnexFanReadbackMode(fan);
     double? duty=OnexFanReadbackDuty(fan,mode);
     f.Status.Text=!string.IsNullOrWhiteSpace(error)?error:OnexStrings.Get(
      mode==0?"Auto is active. The firmware controls the fan.":
      duty.HasValue?"Показан процент, прочитанный из контроллера ONEXPLAYER.":"Unavailable");
    }
    catch(Exception error){f.Status.Text=error.Message;}
   };
   f.Reset=new Button{Name=prefix+"Reset",Content=global::XboxGamingBar.OnexStrings.Get("Reset fan curve"),Style=OnexSettingsButtonStyle(false)};f.Reset.Click+=(s,e)=>{SetOnexFanCurvePoints(f,OnexDefaultFanCurve());QueueOnexFanCurve(f);};body.Children.Add(OnexSettingsColumns(f.Readback,f.Reset));
   body.Children.Add(new TextBlock{Text=OnexStrings.Get(FanEditorHelp),FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=tileTextBrush,Opacity=.8});
   f.Status=new TextBlock{Name=prefix+"Status",FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=tileTextBrush};body.Children.Add(f.Status);
   f.Timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(220)};f.Timer.Tick+=async(s,e)=>await FlushOnexFanCurveAsync(f);return f;
  }
  private int[] OnexDefaultFanCurve()
  {
   var preset=_onexHardwareHome?.GetNamedObject("fan",null)?.GetNamedArray("preset1",null);
   var legacy=new[]{40,50,65,80,100};
   if(preset?.Count==legacy.Length)
    for(int i=0;i<legacy.Length;i++)
     legacy[i]=Math.Max(20,Math.Min(100,(int)Math.Round(preset[i].GetArray()[1].GetNumber()*100)));
   for(int i=1;i<legacy.Length;i++)legacy[i]=Math.Max(legacy[i],legacy[i-1]);
   return FanCurvePolicy.Expand(legacy);
  }
  private static int[] OnexFanPoints(JsonObject curve)
  {
   var points=new int[FanCurvePolicy.Count];
   for(int i=0;i<points.Length;i++)
   {
    int fallback=i==0?(int)curve.GetNamedNumber("at45"):FanCurvePolicy.MaximumDuty;
    points[i]=(int)curve.GetNamedNumber(OnexFanCurveKeys[i],fallback);
   }
   return FanCurvePolicy.Expand(points);
  }
  private void SetOnexFanCurvePoints(OnexFanCurveForm f,int[] values)
  {
   bool sync=f.Sync;
   f.Sync=true;
   try{for(int i=0;i<values.Length;i++)f.Sliders[i].Value=values[i];}
   finally{f.Sync=sync;}
   DrawOnexFanCurve(f);
  }
  private void QueueOnexFanCurve(OnexFanCurveForm f)
  {
   var entry=OnexSelectedFanEntry;if(f.Sync||!App.IsConnected||entry==null)return;string id=entry.GetNamedString("id");if(id.StartsWith("firmware-",StringComparison.Ordinal)||f.Ac&&!entry.GetNamedBoolean("split",false))return;
   var points=f.Sliders.Select(slider=>(int)Math.Round(slider.Value)).ToArray();for(int i=1;i<points.Length;i++)points[i]=Math.Max(points[i],points[i-1]);f.CustomPoints=points;SetOnexFanCurvePoints(f,points);var curve=new JsonObject();for(int i=0;i<points.Length;i++)curve[OnexFanCurveKeys[i]]=JsonValue.CreateNumberValue(points[i]);
   f.Draft=new JsonObject{{"mode",JsonValue.CreateNumberValue(f.Custom.IsOn?3:0)},{"curve",curve}};f.DraftId=id;f.DraftOnAc=_onexFanProfiles.GetNamedBoolean("onAc",false);f.Status.Text=global::XboxGamingBar.OnexStrings.Get("Ожидает сохранения…");f.Timer.Stop();f.Timer.Start();RefreshOnexFanCurveEnabled();
  }
  private async Task FlushOnexFanCurveAsync(OnexFanCurveForm f)
  {
   f.Timer.Stop();if(f.Draft==null)return;if(_onexFanCurveWriting){f.Timer.Start();return;}
   var values=f.Draft;string id=f.DraftId;bool actualAc=f.DraftOnAc;f.Draft=null;if(id!=_onexFanProfiles.GetNamedString("activeId"))return;f.Writing=true;_onexFanBusy=true;RefreshOnexFanCurveEnabled();ApplyOnexTileAvailability();
   try
   {
    var payload=new JsonObject{{"operation",JsonValue.CreateStringValue("values")},{"id",JsonValue.CreateStringValue(id)},{"onAc",JsonValue.CreateBooleanValue(actualAc)},{"source",JsonValue.CreateStringValue(f.Ac?"ac":"dc")},{"values",values}};
    var reply=await SendOnexHomeRequestAsync(new ValueSet{{"OnexFanProfilesSet",payload.Stringify()}});
    ApplyOnexFanProfilesState((string)reply["OnexFanProfilesState"]);
    ApplyOnexHardwareHomeState((string)reply["OnexHardwareHomeState"]);
    if(reply.TryGetValue("OnexFanApplyError",out object applyError))
     f.Status.Text=OnexStrings.Format("Fan settings saved, but could not be applied: {0}",applyError);
    else
     f.Status.Text=OnexFanFormLive(f)?OnexStrings.Get("Сохранено и применено."):OnexStrings.Get("Сохранено. Применится для этого профиля и источника питания.");
   }
   catch(Exception error){f.Status.Text=error.Message;try{await ReadOnexFanProfilesAsync();}catch{}}
   finally{f.Writing=false;_onexFanBusy=_onexFanCurveWriting;RefreshOnexFanProfileEditor();ApplyOnexTileAvailability();if(f.Draft!=null)f.Timer.Start();}
  }
  private void RefreshOnexFanProfileEditor()
  {
   var entry=OnexSelectedFanEntry;if(entry==null)return;string id=entry.GetNamedString("id");bool split=entry.GetNamedBoolean("split",false),actualAc=_onexFanProfiles.GetNamedBoolean("onAc",false),inactive=_onexFanProfiles.GetNamedBoolean("editingInactiveProfile",false);
   bool visible=FanScrollViewer?.Visibility==Visibility.Visible;
   foreach(var f in OnexFanForms)
   {
    if(f.ProfileId!=id||f.ContextAc!=actualAc||f.ContextSplit!=split||f.ContextInactive!=inactive){f.Timer.Stop();f.Draft=null;foreach(var slider in f.Sliders)OnexInputGuard.CancelInteraction(slider);f.CustomPoints=null;f.ProfileId=id;f.ContextAc=actualAc;f.ContextSplit=split;f.ContextInactive=inactive;f.Status.Text="";}
    if(!visible)continue; // Keep draft/context cancellation current; defer hidden controls/charts.
    f.Root.Visibility=f.Ac&&!split?Visibility.Collapsed:Visibility.Visible;f.Heading.Visibility=split?Visibility.Visible:Visibility.Collapsed;f.Root.Spacing=split?12:0;var values=OnexFanFormValues(f);f.Sync=true;
    try
    {
     if(values!=null&&!f.Writing&&f.Draft==null&&!f.Sliders.Any(OnexInputGuard.IsEditing))
     {
      f.Custom.IsOn=values.GetNamedNumber("mode",0)==3;var curve=values.GetNamedObject("curve",null);
      f.CustomPoints=curve==null?null:OnexFanPoints(curve);
      SetOnexFanCurvePoints(f,f.Custom.IsOn?(f.CustomPoints??OnexDefaultFanCurve()):OnexDefaultFanCurve());
     }
     f.Context.Text=id.StartsWith("firmware-",StringComparison.Ordinal)?global::XboxGamingBar.OnexStrings.Get("Системный preset доступен для просмотра. Создай Manual Profile, чтобы менять кривую."):OnexFanFormLive(f)?global::XboxGamingBar.OnexStrings.Get("Сейчас применяется этот блок. Изменения сохраняются автоматически."):global::XboxGamingBar.OnexStrings.Get("Этот блок сейчас не применяется. Изменения только сохраняются и включатся с соответствующим профилем и источником питания.");
     string error=OnexFanReadbackError(_onexHardwareHome?.GetNamedObject("fan",null));if(error.Length>0)f.Context.Text+="\n"+error;
    }
    finally{f.Sync=false;}
   }
   if(!visible)return;
   RefreshOnexFanCurveEnabled();RefreshOnexFanLiveValues();ApplyOnexPageSpacing();
  }
  private void RefreshOnexFanHardwareUi()
  {
   // Profile/source packets still own draft cancellation, selectors and layout.
   // Hardware polling only changes measured values, the graph and readback errors.
   if(FanScrollViewer?.Visibility!=Visibility.Visible)return;
   RefreshOnexFanLiveValues();
   foreach(var form in OnexFanForms.Where(form=>!form.Custom.IsOn))SetOnexFanCurvePoints(form,OnexDefaultFanCurve());
   string id=OnexSelectedFanEntry?.GetNamedString("id","")??"";
   foreach(var f in OnexFanForms)
   {
    string message=id.StartsWith("firmware-",StringComparison.Ordinal)?global::XboxGamingBar.OnexStrings.Get("Системный preset доступен для просмотра. Создай Manual Profile, чтобы менять кривую."):OnexFanFormLive(f)?global::XboxGamingBar.OnexStrings.Get("Сейчас применяется этот блок. Изменения сохраняются автоматически."):global::XboxGamingBar.OnexStrings.Get("Этот блок сейчас не применяется. Изменения только сохраняются и включатся с соответствующим профилем и источником питания.");
    string error=OnexFanReadbackError(_onexHardwareHome?.GetNamedObject("fan",null));if(error.Length>0)message+="\n"+error;
    if(f.Context.Text!=message)f.Context.Text=message;
   }
  }
  private void RefreshOnexFanCurveEnabled()
  {
   bool immutable=(_onexFanProfiles?.GetNamedString("activeId","")??"").StartsWith("firmware-",StringComparison.Ordinal);bool editable=App.IsConnected&&!immutable;
   foreach(var f in OnexFanForms){f.Custom.IsEnabled=editable;f.Reset.IsEnabled=editable&&f.Custom.IsOn;f.Readback.IsEnabled=App.IsConnected&&!_onexFanCurveWriting;foreach(var slider in f.Sliders)slider.IsEnabled=editable&&f.Custom.IsOn;}WireOnexFanNavigation();
  }
  private void DrawOnexFanCurve(OnexFanCurveForm f)
  {
   if(FanScrollViewer?.Visibility!=Visibility.Visible||f.Root.Visibility!=Visibility.Visible||f.Sliders==null||f.Sliders.Any(slider=>slider==null))return;
   for(int i=0;i<f.Sliders.Length;i++)f.Labels[i].Text=(int)f.Sliders[i].Value+"%";
   if(f.Chart.ActualWidth<=16)return;
   double width=f.Chart.ActualWidth-16;
   double X(double degrees)=>width*(degrees-FanCurvePolicy.AxisStart)/(FanCurvePolicy.FullSpeedTemperature-FanCurvePolicy.AxisStart);
   double Y(double duty)=>FanPlotTop+(FanCurvePolicy.MaximumDuty-duty)*FanPlotHeight/(FanCurvePolicy.MaximumDuty-FanCurvePolicy.AxisMinimumDuty);
   string drawingKey=width+"|"+string.Join(",",f.Sliders.Select(slider=>slider.Value))+"|"+_onexFanCpuTemperature+"|"+currentThemeName+"|"+_onexThemeEpoch;
   if(f.DrawingKey==drawingKey&&f.Canvas.Children.Count>0)return;
   f.DrawingKey=drawingKey;
   f.Canvas.Children.Clear();
   foreach(int duty in new[]{15,20,40,60,80,100})
    f.Canvas.Children.Add(new Line{X1=0,X2=width,Y1=Y(duty),Y2=Y(duty),Stroke=tileTextBrush,Opacity=.14,StrokeThickness=1});
   var line=new Polyline{Stroke=tileTextBrush,Opacity=.45,StrokeThickness=2};
   line.Points.Add(new Point(0,Y(f.Sliders[0].Value)));
   for(int i=0;i<f.Sliders.Length;i++)line.Points.Add(new Point(X(FanCurvePolicy.TemperatureAt(i)),Y(f.Sliders[i].Value)));
   line.Points.Add(new Point(width,Y(FanCurvePolicy.MaximumDuty)));
   f.Canvas.Children.Add(line);
   if(_onexFanCpuTemperature.HasValue)
   {
    double x=X(Math.Max(FanCurvePolicy.AxisStart,Math.Min(FanCurvePolicy.FullSpeedTemperature,_onexFanCpuTemperature.Value)));
    f.Canvas.Children.Add(new Line{X1=x,X2=x,Y1=FanPlotTop,Y2=FanPlotTop+FanPlotHeight,Stroke=OnexDialogAccent,StrokeThickness=2,StrokeDashArray=new DoubleCollection{3,3},Opacity=.85});
   }
  }
  private static int? OnexFanReadbackMode(bool available,int? control,int profile)
  {
   if(!available||!control.HasValue)return null;
   if((control.Value&1)==0)return 0;
   return profile==3?3:1;
  }
  private static int? OnexFanReadbackMode(JsonObject fan)
  {
   var value=fan?.GetNamedValue("ecControl",JsonValue.CreateNullValue());
   int? control=value?.ValueType==JsonValueType.Number?(int?)value.GetNumber():null;
   return OnexFanReadbackMode(fan?.GetNamedBoolean("available",false)==true,control,(int)(fan?.GetNamedNumber("profile",0)??0));
  }
  private string OnexFanReadbackError(JsonObject fan)
  {
   if(!string.IsNullOrEmpty(_onexFanSelectionError))return _onexFanSelectionError;
   var saved=OnexPresetValues(_onexFanProfiles,OnexAppliedFanId);
   if(saved?.GetNamedNumber("mode",0)==0&&OnexFanReadbackMode(fan)==0)return "";
   return OnexPresetLink(fan,"error")??"";
  }
  private static double? OnexFanReadbackDuty(JsonObject fan,int? mode)
  {
   if(mode!=1&&mode!=3)return null;
   var value=fan?.GetNamedValue("percent",JsonValue.CreateNullValue());
   if(value?.ValueType!=JsonValueType.Number)return null;
   double duty=value.GetNumber();
   return duty>=0&&duty<=100?(double?)duty:null;
  }
  private FrameworkElement BuildOnexFanLiveStats()
  {
   var stats=new Grid{Name="OnexFanLiveStats",ColumnSpacing=12,Padding=new Thickness(4,2,4,2)};
   for(int i=0;i<3;i++)stats.ColumnDefinitions.Add(new ColumnDefinition());
   TextBlock AddMetric(int column,string title,string name,TextBlock detail=null)
   {
    var cell=new StackPanel{Spacing=4};
    Grid.SetColumn(cell,column);
    stats.Children.Add(cell);
    cell.Children.Add(new TextBlock{Text=OnexStrings.Get(title),FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=tileTextBrush});
    var value=new TextBlock{Name=name,Text="—",FontSize=21,FontWeight=Windows.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
    cell.Children.Add(value);
    if(detail!=null)cell.Children.Add(detail);
    return value;
   }
   _onexFanLiveRpm=AddMetric(0,"Fan Speed","OnexFanLiveRpm");
   _onexFanLiveTemperature=AddMetric(1,"CPU temperature","OnexFanLiveTemperature");
   _onexFanLiveDuty=new TextBlock{Name="OnexFanLiveDuty",FontSize=12,Foreground=tileTextBrush,Visibility=Visibility.Collapsed};
   _onexFanLiveMode=AddMetric(2,"Fan Mode","OnexFanLiveMode",_onexFanLiveDuty);
   return OnexSettingsCard(stats);
  }
  private static int? OnexFanFreshRpm(JsonObject fan)
  {
   if(!DateTimeOffset.TryParse(fan?.GetNamedString("rpmSampleUtc",""),out DateTimeOffset sampled))return null;
   var age=DateTimeOffset.UtcNow-sampled;
   if(age.TotalSeconds<-1||age.TotalSeconds>8)return null;
   var value=fan?.GetNamedValue("rpm",JsonValue.CreateNullValue());
   if(value?.ValueType!=JsonValueType.Number)return null;
   double rpm=value.GetNumber();
   return rpm>=0&&rpm<=8000?(int?)rpm:null;
  }
  private void RefreshOnexFanLiveValues()
  {
   var fan=App.IsConnected?_onexHardwareHome?.GetNamedObject("fan",null):null;
   var temperature=fan?.GetNamedValue("temperatureC",JsonValue.CreateNullValue());
   _onexFanCpuTemperature=temperature?.ValueType==JsonValueType.Number?(double?)temperature.GetNumber():null;
   int? mode=OnexFanReadbackMode(fan);
   double? duty=OnexFanReadbackDuty(fan,mode);
   int? rpm=OnexFanFreshRpm(fan);
   if(_onexFanLiveRpm!=null)
   {
    _onexFanLiveRpm.Text=rpm.HasValue?OnexStrings.Format("{0} RPM",rpm.Value.ToString("N0")):"—";
    _onexFanLiveTemperature.Text=_onexFanCpuTemperature.HasValue?Math.Round(_onexFanCpuTemperature.Value)+OnexStrings.Get("°C"):"—";
    _onexFanLiveMode.Text=OnexStrings.Get(mode==0?"Auto":mode==3?"Custom":mode==1?"Manual":"Unavailable");
    _onexFanLiveDuty.Text=duty.HasValue?OnexStrings.Format("PWM {0}%",duty.Value):"";
    _onexFanLiveDuty.Visibility=duty.HasValue?Visibility.Visible:Visibility.Collapsed;
   }
   foreach(var f in OnexFanForms)DrawOnexFanCurve(f);
  }
 }
}
