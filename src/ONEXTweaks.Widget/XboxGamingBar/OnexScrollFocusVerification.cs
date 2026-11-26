using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexScrollFocusAsync(JsonObject report)
  {
   int checks=0,samples=0,focused=0;
   void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   var originalPage=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
   var originalFocus=FocusManager.GetFocusedElement() as Control;Border fixture=null;
   string marker="focus432-"+Guid.NewGuid().ToString("N");var phaseFile=await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-focus432-progress.json",CreationCollisionOption.ReplaceExisting);
   async Task Phase(string phase)
   {
    await FileIO.WriteTextAsync(phaseFile,new JsonObject{{"version",JsonValue.CreateStringValue("0.29.432.0")},{"run",JsonValue.CreateStringValue(marker)},{"phase",JsonValue.CreateStringValue(phase)},{"utc",JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("o"))},{"physicalInput",JsonValue.CreateBooleanValue(false)}}.Stringify());
   }
   try
   {
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending,"Protect pending owner edits");
    SystemNavItem.IsChecked=true;EnsureOnexUnifiedFocus();
    var body=new StackPanel{Spacing=12};
    body.Children.Add(new TextBlock{Text="Focus scroll preview · synthetic controls",FontSize=18,Foreground=OnexSolidBrush(Colors.White)});
    body.Children.Add(new Border{Height=100});
    var button=new Button{Name="OnexFocus432Button",Height=64,HorizontalAlignment=HorizontalAlignment.Stretch,Style=OnexSettingsButtonStyle(false),Content=new Border{Width=180,Height=26,Background=OnexSolidBrush(Color.FromArgb(255,28,127,163)),Child=new TextBlock{Text="Focus tracking marker",HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Foreground=OnexSolidBrush(Colors.White)}}};
    var check=new CheckBox{Name="OnexFocus432Check",Content="CheckBox · synthetic preview",IsChecked=true};
    var toggle=new ToggleSwitch{Name="OnexFocus432Toggle",Header="ToggleSwitch · synthetic preview",IsOn=true};
    var slider=new Slider{Name="OnexFocus432Slider",Minimum=0,Maximum=100,Value=50,Header="Slider · synthetic preview"};
    var text=new TextBox{Name="OnexFocus432Text",Text="TextBox · synthetic preview"};
    var choice=OnexSettingsChoice("");choice.Name="OnexFocus432Choice";choice.Items.Add("First preview");choice.Items.Add("Second preview");choice.SelectedIndex=0;
    var last=new Button{Name="OnexFocus432Last",Content="Second button · synthetic preview",Style=OnexSettingsButtonStyle(false)};
    var controls=new Control[]{button,check,toggle,slider,text,choice,last};
    foreach(var control in controls)body.Children.Add(control);
    body.Children.Add(new Border{Height=540});
    var host=WrapOnexFocusContent(body);
    var scroll=new ScrollViewer{Name="OnexFocus432Scroll",Content=host,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(12),ZoomMode=ZoomMode.Disabled};
    fixture=new Border{Background=OnexSolidBrush(Color.FromArgb(255,23,33,43)),BorderBrush=OnexSolidBrush(Color.FromArgb(255,48,68,86)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Margin=new Thickness(12),Child=scroll};
    Canvas.SetZIndex(fixture,200000);OnexStartupRoot.Children.Add(fixture);
    UpdateLayout();await Task.Delay(100);RegisterOnexFocusLayerContent(host,_onexFocusHosts.GetValue(host as Grid,key=>throw new InvalidOperationException("Missing fixture scope")));
    foreach(var control in controls)
    {
     ConfigureOnexFocus(control);control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(60);
     Check(control.Focus(FocusState.Keyboard),"Cannot focus "+control.Name);FocusOnexOwnedControl(control);
     var layer=_onexControlLayers.TryGetValue(control,out var found)?found:null;
     Check(IsOnexFocusInkVisible(layer),"Target-local focus missing for "+control.GetType().Name);
     Check(ReferenceEquals(ElementCompositionPreview.GetElementChildVisual(control),layer.Ink.Root),"Focus visual is not attached to its control");
     Check(layer.Ink.Visual.Offset==System.Numerics.Vector3.Zero,"Focus visual uses a drifting global offset");
     Check(Math.Abs(layer.Ink.Width-control.ActualWidth)<.5&&Math.Abs(layer.Ink.Height-control.ActualHeight)<.5&&layer.Ink.Radius>=8,"Focus dimensions/corner mismatch");
     Check(!control.UseSystemFocusVisuals,"Stock focus enabled alongside owned focus");
     focused++;
    }
    scroll.ChangeView(null,0,null,true);await Task.Delay(100);button.Focus(FocusState.Keyboard);FocusOnexOwnedControl(button);await Task.Delay(40);
    var active=_onexControlLayers.GetValue(button,key=>throw new InvalidOperationException("Missing target scope"));var ink=active.Ink;int geometryBefore=_onexFocusGeometryWrites;
    await Phase("steady");await Task.Delay(500);
    Check(scroll.ChangeView(null,180,null,false),"Native animated scroll refused");
    await Phase("animated");
    for(int frame=0;frame<30;frame++){
     await Task.Delay(16);
     Check(ReferenceEquals(ElementCompositionPreview.GetElementChildVisual(button),ink.Root)&&ink.Visual.Offset==System.Numerics.Vector3.Zero,"Animated scroll detached or repositioned focus independently");
     Check(ReferenceEquals(FocusManager.GetFocusedElement(),button)&&IsOnexFocusInkVisible(active),"Scrolling stole or hid keyboard focus");
     samples++;
     if(frame==2){await Phase("ui-stall");System.Threading.Thread.Sleep(140);await Phase("animated");}
    }
    Check(_onexFocusGeometryWrites==geometryBefore,"Scroll still drives UI-thread focus geometry updates");
    Check(scroll.VerticalOffset>100,"Actual ScrollViewer content did not move");
    await Phase("clipped");await Task.Delay(400);
    scroll.ChangeView(null,0,null,false);await Phase("returning");await Task.Delay(500);
    Check(ReferenceEquals(ElementCompositionPreview.GetElementChildVisual(button),ink.Root)&&IsOnexFocusInkVisible(active),"Focus lost after animated return");
    last.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(60);last.Focus(FocusState.Keyboard);FocusOnexOwnedControl(last);
    Check(ink.Visual.Opacity==0,"Previous target still has a visible focus ring");
    Check(_onexFocusLayers.Count(x=>x.TryGetTarget(out var layer)&&IsOnexFocusInkVisible(layer))==1,"Multiple focus rings are active");
    await Phase("complete");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["focusedControlTypes"]=JsonValue.CreateNumberValue(focused);report["animatedScrollSamples"]=JsonValue.CreateNumberValue(samples);
    report["targetLocalCompositionInk"]=JsonValue.CreateBooleanValue(true);report["globalScrollPositionUpdates"]=JsonValue.CreateNumberValue(_onexFocusGeometryWrites-geometryBefore);
    report["nativeAnimatedScrollTested"]=JsonValue.CreateBooleanValue(true);report["uiThreadStallMilliseconds"]=JsonValue.CreateNumberValue(140);
    report["scopeHasNoBackendHandlers"]=JsonValue.CreateBooleanValue(true);report["physicalMouseTouchTested"]=JsonValue.CreateBooleanValue(false);
    report["run"]=JsonValue.CreateStringValue(marker);
   }
   finally
   {
    HideOnexAllFocusLayers();if(fixture!=null)OnexStartupRoot.Children.Remove(fixture);
    if(originalPage!=null&&originalPage.Visibility==Visibility.Visible)originalPage.IsChecked=true;
    if(originalFocus?.IsLoaded==true&&originalFocus.IsEnabled)originalFocus.Focus(FocusState.Programmatic);
   }
  }
 }
}
