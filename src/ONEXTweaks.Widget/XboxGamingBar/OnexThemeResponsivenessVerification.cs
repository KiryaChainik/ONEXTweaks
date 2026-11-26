using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexThemeResponsivenessAsync(JsonObject report)
  {
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   var values=ApplicationData.Current.LocalSettings.Values;
   string saved=values.TryGetValue("WidgetTheme",out var raw)?raw as string:null;
   bool initialized=isThemeInitialized,projection=_kbmReadOnlyProjection;
   var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
   var samples=new JsonArray();var renders=new JsonArray();
   try
   {
    EnsureOnexKeyboardMouseFrontend();await ReadOnexKeyboardMouseAsync();
    Check(KbmPolicy!=null,"Real KBM policy unavailable");
    Check(!_kbmBusy&&_kbmTuning.Count==0,"Protect pending owner KBM edit");
    _kbmReadOnlyProjection=true;isThemeInitialized=false;
    var sliders=new[]{_kbmMouseSpeed,_kbmScrollSpeed,_kbmDeadzone,_kbmHoldAcceleration,_kbmNudgeStep};
    foreach(string name in new[]{OnexPalette.Name,"Dracula","Windows 11 Light"})
    {
     ApplyTheme(name);
     _onexKeyboardMouseNav.IsChecked=true;ApplyThemeToCurrentTab();UpdateLayout();
     // Force a policy refresh through the production renderer without any write command.
     string live=_kbmState.Stringify();_kbmState=null;ApplyOnexKeyboardMouseState(live);UpdateLayout();
     var expected=OnexKeyboardMouseAccent;
     foreach(var slider in sliders)
     {
      SetKbmSettingsCard(_kbmSections[Array.IndexOf(sliders,slider)<2?0:1]);UpdateLayout();
      Check(ReferenceEquals(slider.Style,OnexOwnedStyle("slider")),"KBM lost owned rounded slider: "+slider.Name);
      Check(slider.Foreground is SolidColorBrush fill&&fill.Color==expected,"KBM refresh overrides palette: "+slider.Name);
      Check(slider.ActualWidth>80&&slider.ActualHeight>=22&&slider.ActualHeight<=52,"KBM slider geometry invalid: "+slider.Name);
     }
     await Task.Delay(120);
     foreach(var slider in sliders)Check((slider.Foreground as SolidColorBrush)?.Color==expected,"Delayed colour repaint survived: "+slider.Name);
     string label=name==OnexPalette.Name?"base":name=="Dracula"?"dracula":"light";
     SetKbmSettingsCard(_kbmSections[0]);UpdateLayout();
     await SaveOnexImage("onex-ui370-kbm-"+label+".png",_kbmSections[0].Detail,60);
     SetKbmSettingsCard(_kbmSections[1]);UpdateLayout();
     await SaveOnexImage("onex-ui370-precision-"+label+".png",_kbmPrecisionCard,60);
     renders.Add(JsonValue.CreateStringValue(name));
    }
    ApplyTheme(saved??OnexPalette.Name);
    var tabs=GetVisibleNavigationItems();
    foreach(var tab in tabs){tab.IsChecked=true;UpdateLayout();await Task.Delay(20);}
    int whole=_onexWholeThemePasses,pages=_onexPageThemePasses;
    for(int lap=0;lap<2;lap++)foreach(var tab in tabs)
    {
     var sw=Stopwatch.StartNew();tab.IsChecked=true;UpdateLayout();double navigationMs=sw.Elapsed.TotalMilliseconds;
     var frame=new TaskCompletionSource<bool>();EventHandler<object> onFrame=null;
     onFrame=(s,e)=>{CompositionTarget.Rendering-=onFrame;frame.TrySetResult(true);};
     CompositionTarget.Rendering+=onFrame;
     var completed=await Task.WhenAny(frame.Task,Task.Delay(500));CompositionTarget.Rendering-=onFrame;
     samples.Add(new JsonObject{{"tab",JsonValue.CreateStringValue(tab.Tag as string??"")},{"navigationAndLayoutMs",JsonValue.CreateNumberValue(navigationMs)},{"nextRenderingCallbackMs",JsonValue.CreateNumberValue(sw.Elapsed.TotalMilliseconds)},{"renderCallbackObserved",JsonValue.CreateBooleanValue(completed==frame.Task)}});
     Check(GetActiveScrollViewer()?.Visibility==Visibility.Visible,"Navigation failed to reveal tab");
    }
    Check(_onexWholeThemePasses==whole,"Warm navigation repaints entire widget");
    Check(_onexPageThemePasses==pages,"Warm navigation repeats theme traversal");
    var durations=samples.Select(x=>x.GetObject().GetNamedNumber("navigationAndLayoutMs")).OrderBy(x=>x).ToArray();
    Check(durations.Last()<250,"Warm navigation/layout still blocks UI over250ms");
    Check((values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:null)==saved,"Theme regression wrote owner preference");
    report["checks"]=JsonValue.CreateNumberValue(checks);
    report["actualOwnUwpRendering"]=JsonValue.CreateBooleanValue(true);
    report["timings"]=samples;report["renderedThemes"]=renders;
    report["navigationMedianMs"]=JsonValue.CreateNumberValue(durations[durations.Length/2]);
    report["navigationMaxMs"]=JsonValue.CreateNumberValue(durations.Last());
    report["warmWholeWidgetPaints"]=JsonValue.CreateNumberValue(_onexWholeThemePasses-whole);
    report["warmPageThemeTraversals"]=JsonValue.CreateNumberValue(_onexPageThemePasses-pages);
    report["backendWritesRequested"]=JsonValue.CreateNumberValue(0);
    report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally
   {
    _kbmReadOnlyProjection=projection;_kbmState=null;
    isThemeInitialized=false;ApplyTheme(values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:OnexPalette.Name);
    isThemeInitialized=initialized;
    if(!projection)await ReadOnexKeyboardMouseAsync();
    if(original!=null&&original.Visibility==Visibility.Visible)original.IsChecked=true;
   }
  }
 }
}
