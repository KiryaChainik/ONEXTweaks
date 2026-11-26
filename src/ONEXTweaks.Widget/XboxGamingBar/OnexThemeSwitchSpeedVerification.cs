using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
  private string[] OnexThemeSettingsFingerprint()
  {
   string Ink(Brush brush)=>brush is SolidColorBrush solid?solid.Color.ToString():brush?.GetType().Name??"none";
   return OnexOwnedContent(SystemScrollViewer).Select(node=>
   {
    string name=(node as FrameworkElement)?.Name??"";
    if(node is TextBlock text)return "Text|"+name+"|"+Ink(text.Foreground)+"|"+text.Opacity;
    if(node is IconElement icon)return "Icon|"+name+"|"+Ink(icon.Foreground);
    if(node is Control control)return node.GetType().Name+"|"+name+"|"+Ink(control.Foreground)+"|"+Ink(control.Background)+"|"+Ink(control.BorderBrush);
    if(node is Border border)return "Border|"+name+"|"+Ink(border.Background)+"|"+Ink(border.BorderBrush);
    return null;
   }).Where(x=>x!=null).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
  }
  private async Task VerifyOnexThemeSwitchSpeedAsync(JsonObject report)
  {
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   var values=ApplicationData.Current.LocalSettings.Values;string saved=values.TryGetValue("WidgetTheme",out var raw)?raw as string:null;
   bool initialized=isThemeInitialized,measure=_onexMeasureThemeWork,appearance=ThemeExpandButton.IsChecked==true;
   var themeVisibility=ThemeContent.Visibility;var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
   double scroll=SystemScrollViewer.VerticalOffset;var samples=new JsonArray();int pageVisits=0;var signatures=new Dictionary<string,string[]>();
   void Choose(string name){bool before=isThemeInitialized;isThemeInitialized=false;try{ThemeComboBox.SelectedItem=ThemeComboBox.Items.OfType<ComboBoxItem>().First(x=>x.Content as string==name);}finally{isThemeInitialized=before;}}
   async Task WaitFrame(Stopwatch clock,JsonObject row)
   {
    var frame=new TaskCompletionSource<bool>();EventHandler<object> handler=null;handler=(s,e)=>{CompositionTarget.Rendering-=handler;frame.TrySetResult(true);};
    CompositionTarget.Rendering+=handler;var completed=await Task.WhenAny(frame.Task,Task.Delay(800));CompositionTarget.Rendering-=handler;
    row["nextRenderingCallbackMs"]=JsonValue.CreateNumberValue(clock.Elapsed.TotalMilliseconds);row["renderCallbackObserved"]=JsonValue.CreateBooleanValue(completed==frame.Task);
    Check(completed==frame.Task,"Foreground rendering callback unavailable");
   }
   try
   {
    Check(_onexDetailsDialog==null,"Protect owner dialog");Check(quickSettingsInitialized,"Actual legacy grid baseline unavailable");
    isThemeInitialized=true;SystemNavItem.IsChecked=true;
    // Exercise the first base-theme initialization branch as well as switches.
    _onexCatalogPainted=false;int cold=_onexWholeThemePasses;Choose(OnexPalette.Name);ApplyTheme(OnexPalette.Name);
    Check(_onexCatalogPainted&&_onexWholeThemePasses==cold+1,"Cold base theme skipped its owned palette");
    ThemeExpandButton.IsChecked=true;ThemeContent.Visibility=Visibility.Visible;_onexMeasureThemeWork=true;
    // Both paths execute on this same installed UWP process and device. The
    // previous-work replay includes its hidden grid rebuild and full sweep.
    for(int lap=0;lap<2;lap++)foreach(string name in new[]{OnexPalette.Name,"Dracula","Windows 11 Light"})
    {
     foreach(bool previous in lap==0?new[]{true,false}:new[]{false,true})
     {
      string reset=name==OnexPalette.Name?"OLED":OnexPalette.Name;Choose(reset);ApplyTheme(reset);UpdateLayout();await Task.Delay(35);
      Choose(name);int legacy=_onexLegacyThemeNodes,owned=_onexOwnedThemeNodes,grids=_onexThemeGridRebuilds;
      var clock=Stopwatch.StartNew();ApplyOnexThemeWork(name,previous);double applyMs=clock.Elapsed.TotalMilliseconds;UpdateLayout();
      var row=new JsonObject{{"theme",JsonValue.CreateStringValue(name)},{"lap",JsonValue.CreateNumberValue(lap)},{"previousWorkReplay",JsonValue.CreateBooleanValue(previous)},
       {"applyMs",JsonValue.CreateNumberValue(applyMs)},{"applyAndLayoutMs",JsonValue.CreateNumberValue(clock.Elapsed.TotalMilliseconds)},
       {"legacyNodes",JsonValue.CreateNumberValue(_onexLegacyThemeNodes-legacy)},{"ownedNodes",JsonValue.CreateNumberValue(_onexOwnedThemeNodes-owned)},
       {"hiddenGridRebuilds",JsonValue.CreateNumberValue(_onexThemeGridRebuilds-grids)}};
      if(_onexThemePhaseMs!=null)row["phaseMs"]=_onexThemePhaseMs;
      samples.Add(row);await WaitFrame(clock,row);
      Check(previous?row.GetNamedNumber("hiddenGridRebuilds")==1:row.GetNamedNumber("hiddenGridRebuilds")==0,"Theme switch rebuilt the hidden legacy grid unexpectedly");
      Check(previous?row.GetNamedNumber("legacyNodes")>0:row.GetNamedNumber("legacyNodes")==0,"Duplicate legacy painting still occurs in production path");
      string key=name+lap;var signature=OnexThemeSettingsFingerprint();
      if(signatures.TryGetValue(key,out var before)){Check(signature.SequenceEqual(before),"Visible Settings palette changed between previous and production work: "+name);}
      else signatures[key]=signature;
      report["timings"]=samples;var progress=await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-theme-switch-progress.json",CreationCollisionOption.ReplaceExisting);await FileIO.WriteTextAsync(progress,report.Stringify());
     }
    }
    var oldTimes=samples.Select(x=>x.GetObject()).Where(x=>x.GetNamedBoolean("previousWorkReplay")).Select(x=>x.GetNamedNumber("applyAndLayoutMs")).OrderBy(x=>x).ToArray();
    var newTimes=samples.Select(x=>x.GetObject()).Where(x=>!x.GetNamedBoolean("previousWorkReplay")).Select(x=>x.GetNamedNumber("applyAndLayoutMs")).OrderBy(x=>x).ToArray();
    double Median(double[] a)=>(a[(a.Length-1)/2]+a[a.Length/2])/2;
    report["previousWorkMedianMs"]=JsonValue.CreateNumberValue(Median(oldTimes));report["productionMedianMs"]=JsonValue.CreateNumberValue(Median(newTimes));report["productionMaxMs"]=JsonValue.CreateNumberValue(newTimes.Last());
    Check(Median(newTimes)<Median(oldTimes),"Measured production theme switching did not improve");
    // Every page must receive the new palette on its first visit; revisits
    // in the same theme epoch must remain traversal-free.
    foreach(string name in OnexAllowedThemeNames)
    {
     Choose(name);SystemNavItem.IsChecked=true;ApplyTheme(name);UpdateLayout();var tabs=GetVisibleNavigationItems();
     foreach(var tab in tabs){tab.IsChecked=true;UpdateLayout();Check(_onexAppliedTheme.Name==name,"Incoming page lost selected theme");var page=OnexNavigationRoot();if(page!=null){Check(page.ActualTheme==(OnexLight(_onexAppliedTheme)?ElementTheme.Light:ElementTheme.Dark),"Incoming page inherits the wrong light/dark theme");Check(_onexPageThemeEpochs.TryGetValue(page,out int applied)&&applied==_onexThemeEpoch,"Incoming page palette epoch is stale");foreach(var choice in OnexOwnedContent(page).OfType<ComboBox>())Check(choice.Foreground is SolidColorBrush text&&text.Color==_onexAppliedTheme.TextPrimary,"Incoming choice still has old text ink");}Check(GetActiveScrollViewer()?.Visibility==Visibility.Visible||_releaseHome?.Visibility==Visibility.Visible||_releaseOsd?.Visibility==Visibility.Visible,"Incoming page not visible");pageVisits++;}
     int paints=_onexPageThemePasses;foreach(var tab in tabs){tab.IsChecked=true;UpdateLayout();pageVisits++;}Check(_onexPageThemePasses==paints,"Warm tab revisit repaints after theme switch");
    }
    foreach(string name in new[]{OnexPalette.Name,"Windows 11 Light"}){SystemNavItem.IsChecked=true;Choose(name);ApplyTheme(name);UpdateLayout();ThemeContent.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(40);await SaveOnexImage("onex-theme-speed390-"+name.Replace(' ','-')+".png",ThemeContent,30);}
    Check((values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:null)==saved,"Theme benchmark wrote owner theme preference");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["actualPageVisits"]=JsonValue.CreateNumberValue(pageVisits);report["themesVisited"]=JsonValue.CreateNumberValue(OnexAllowedThemeNames.Length);
    report["baselineKind"]=JsonValue.CreateStringValue("Previous production work replayed on the same installed candidate; actual UWP calls, not synthetic timing");
    report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["hardwareWritesRequested"]=JsonValue.CreateNumberValue(0);
   }
   finally
   {
    _onexReplayPreviousThemeWork=false;_onexMeasureThemeWork=measure;ThemeExpandButton.IsChecked=appearance;ThemeContent.Visibility=themeVisibility;
    string fresh=values.TryGetValue("WidgetTheme",out var stored)?stored as string:OnexPalette.Name;
    isThemeInitialized=false;Choose(NormalizeOnexThemeName(fresh));ApplyTheme(fresh);isThemeInitialized=initialized;
    if(original!=null&&original.Visibility==Visibility.Visible)original.IsChecked=true;SystemScrollViewer.ChangeView(null,scroll,null,true);
   }
  }
 }
}
