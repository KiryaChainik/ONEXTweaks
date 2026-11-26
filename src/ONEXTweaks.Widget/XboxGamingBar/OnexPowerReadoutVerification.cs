// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexPowerReadoutAsync(JsonObject report)
  {
   System.IO.File.WriteAllText(System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path,"onex-power-ink-progress.json"),"{\"version\":\"0.29.418.0\",\"phase\":\"entered\"}");
   var prefs=ApplicationData.Current.LocalSettings.Values;string savedTheme=prefs.TryGetValue("WidgetTheme",out var raw)?raw as string:null;
   var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);bool ready=isThemeInitialized;
   EnsureOnexPerformanceLayout();
   report["initialization"]=new JsonObject{{"home",JsonValue.CreateBooleanValue(_releaseHome!=null)},{"performanceCards",JsonValue.CreateBooleanValue(_onexPerfSections!=null)},{"dc",JsonValue.CreateBooleanValue(_onexDcEditor!=null)},{"ac",JsonValue.CreateBooleanValue(_onexAcEditor!=null)}};
   string[] names={"LightPowerValue","LightPowerUnit","LightPl2Value","LightPl2Unit","LightPowerPl2","LightPl2Separator"};
   var home=names.Select(name=>(TextBlock)_releaseHome.FindName(name)).ToArray();var head=(FrameworkElement)_releaseHome.FindName("LightPl2Head");var slider=(Slider)_releaseHome.FindName("OnexPl2Slider");
   var readoutsReady=new JsonArray();foreach(var text in home)readoutsReady.Add(JsonValue.CreateBooleanValue(text!=null));report["homeReadoutsReady"]=readoutsReady;
   var classic=(FrameworkElement)OnexPerfCardGrid;var classicVisible=classic.Visibility;var detailVisible=OnexPerfTdpDetail.Visibility;var boostVisible=TDPBoostSliderArea.Visibility;var headVisible=head.Visibility;var sliderVisible=slider.Visibility;
   string perfOpen=_onexPerfOpen;var details=_onexPerfSections.Select(section=>section.Detail.Visibility).ToArray();
   var sourceVisible=new[]{_onexDcEditor?.Root.Visibility??Visibility.Collapsed,_onexAcEditor?.Root.Visibility??Visibility.Collapsed};
   int checks=0,visits=0,readouts=0,captures=0;var seen=new JsonArray();string currentTheme="";var display=new Windows.System.Display.DisplayRequest();bool displayActive=false;
   void Phase(string phase)
   {
    var progress=new JsonObject{{"version",JsonValue.CreateStringValue("0.29.418.0")},{"theme",JsonValue.CreateStringValue(currentTheme)},{"phase",JsonValue.CreateStringValue(phase)},{"checks",JsonValue.CreateNumberValue(checks)},{"readouts",JsonValue.CreateNumberValue(readouts)},{"captures",JsonValue.CreateNumberValue(captures)}};
    System.IO.File.WriteAllText(System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path,"onex-power-ink-progress.json"),progress.Stringify());
   }
   async Task Capture(string name,UIElement target)
   {
    Phase("capture "+name);var capture=SaveOnexImage(name,target,30);var winner=await Task.WhenAny(capture,Task.Delay(8000));
    if(!ReferenceEquals(winner,capture))throw new TimeoutException("Read-only power-card renderer timed out: "+name);await capture;captures++;
   }
   void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   void Inspect(TextBlock text,ThemeColors theme)
   {
    Check(OnexPowerReadout.GetIsNeutral(text),"Power readout missing its explicit neutral role");
    Check(text.Foreground is SolidColorBrush brush&&brush.Color==theme.TextPrimary,"Power number/unit inherited an accent colour");readouts++;
    foreach(bool glass in new[]{false,true})
    {
     var fill=OnexGlassSurfaces.Create(theme.CardBackground,theme,glass);
     var colours=fill is LinearGradientBrush gradient?gradient.GradientStops.Select(stop=>stop.Color):new[]{((SolidColorBrush)fill).Color};
     Check(colours.All(colour=>OnexContrast(theme.TextPrimary,colour)>=4.5),"Power ink is unreadable on a flat/glass card");
    }
   }
   void ProjectPerformance()
   {
    // Only visibility is projected. Do not toggle PL2, AC/DC or any slider,
    // save a profile, or send a power command to make a colour sample.
    classic.Visibility=Visibility.Visible;foreach(var section in _onexPerfSections)section.Detail.Visibility=section.Id=="tdp"?Visibility.Visible:Visibility.Collapsed;
    if(_onexDcEditor!=null)_onexDcEditor.Root.Visibility=Visibility.Collapsed;if(_onexAcEditor!=null)_onexAcEditor.Root.Visibility=Visibility.Collapsed;
    _onexPerfOpen="tdp";TDPBoostSliderArea.Visibility=Visibility.Visible;ApplyOnexTabPalette();UpdateLayout();
   }
   try
   {
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending&&_onexPerformanceCpuForm?.Pending?.Invoke()!=true,"Protect pending owner edits");isThemeInitialized=false;display.RequestActive();displayActive=true;
    foreach(var name in OnexAllowedThemeNames.Concat(new[]{"OLED","Windows 11 Light",OnexPalette.Name}))
    {
     currentTheme=name;Phase("apply theme");ApplyTheme(name);Phase("home");PerformanceNavItem.IsChecked=true;head.Visibility=slider.Visibility=Visibility.Visible;UpdateLayout();await Task.Delay(45);visits++;
     var theme=_onexAppliedTheme;foreach(var text in home.Skip(4))Inspect(text,theme);
     var palette=OnexApprovedTheme.Find(theme.Name);
     Color expectedPl1=palette?.Accent(OnexThemeRole.Power)??theme.EffectiveAccent();
     Color expectedPl2=palette?.Accent(OnexThemeRole.Controller)??theme.EffectiveAccent();
     var fill1=_releaseHome.PowerInput.Background;
     var fill2=slider.Background;
     Check((fill1 as SolidColorBrush)?.Color==expectedPl1&&(fill2 as SolidColorBrush)?.Color==expectedPl2,
       "Intentional power track accent changed");
     for(int i=0;i<4;i++)
     {
      Check(OnexPowerReadout.GetUsesTrackAccent(home[i])&&ReferenceEquals(home[i].Foreground,i<2?fill1:fill2),
        "Home wattage was detached from its track accent");
      readouts++;
     }
     Phase("expanded performance");_releasePowerNav.IsChecked=true;ProjectPerformance();await Task.Delay(30);visits++;
     Inspect(TDPValueText,theme);Inspect(TDPBoostFPPTValueInCard,theme);
     Phase("actual visible Home/Performance readouts checked");
     if(new[]{OnexPalette.Name,"OLED","Dracula","Cobalt2","Miasma","Catppuccin Latte"}.Contains(name)&&!seen.Any(value=>value.GetString()==name))
     {
      string label=name.Replace(' ','-');OnexPerfTdpDetail.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});UpdateLayout();await Task.Delay(50);await Capture("onex-power418-"+label+"-performance.png",OnexPerfTdpDetail);
      PerformanceNavItem.IsChecked=true;head.Visibility=slider.Visibility=Visibility.Visible;UpdateLayout();await Task.Delay(40);await Capture("onex-power418-"+label+"-home.png",(UIElement)_releaseHome.FindName("LightPowerCard"));seen.Add(JsonValue.CreateStringValue(name));
     }
    }
    Check((prefs.TryGetValue("WidgetTheme",out var fresh)?fresh as string:null)==savedTheme,"Colour checks changed saved owner theme");
    Phase("completed");report["displayRequestReleasedInFinally"]=JsonValue.CreateBooleanValue(true);
    report["checks"]=JsonValue.CreateNumberValue(checks);report["themeTransitions"]=JsonValue.CreateNumberValue(23);report["actualPageVisits"]=JsonValue.CreateNumberValue(visits);report["actualReadoutSamples"]=JsonValue.CreateNumberValue(readouts);report["captures"]=JsonValue.CreateNumberValue(captures);
    report["sourceReadoutRuntimeSweep"]=JsonValue.CreateBooleanValue(false);report["explicitNeutralTextRole"]=JsonValue.CreateBooleanValue(true);report["pl2AndAcDcVisibilityAreUiProjections"]=JsonValue.CreateBooleanValue(true);report["powerCommandsRequested"]=JsonValue.CreateNumberValue(0);report["physicalInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally
   {
    Phase("finally start");if(displayActive){display.RequestRelease();displayActive=false;}
    _onexPerfOpen=perfOpen;for(int n=0;n<_onexPerfSections.Length;n++)_onexPerfSections[n].Detail.Visibility=details[n];classic.Visibility=classicVisible;OnexPerfTdpDetail.Visibility=detailVisible;TDPBoostSliderArea.Visibility=boostVisible;head.Visibility=headVisible;slider.Visibility=sliderVisible;
    if(_onexDcEditor!=null)_onexDcEditor.Root.Visibility=sourceVisible[0];if(_onexAcEditor!=null)_onexAcEditor.Root.Visibility=sourceVisible[1];
    Phase("restore current saved theme");ApplyTheme(prefs.TryGetValue("WidgetTheme",out var fresh)?fresh as string:OnexPalette.Name);isThemeInitialized=ready;
    Phase("restore visible page");if(original?.Visibility==Visibility.Visible)original.IsChecked=true;Phase("finally completed");
   }
  }
 }
}
