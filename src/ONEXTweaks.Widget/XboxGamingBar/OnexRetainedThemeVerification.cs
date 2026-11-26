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
  private async Task VerifyOnexRetainedThemesAsync(JsonObject report)
  {
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   var values=ApplicationData.Current.LocalSettings.Values;string saved=values.TryGetValue("WidgetTheme",out var raw)?raw as string:null;
   bool initialized=isThemeInitialized,projection=_kbmReadOnlyProjection;
   var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);var rendered=new JsonArray();var mismatches=new JsonArray();var iconMismatches=new JsonArray();
   try
   {
    await ReadOnexKeyboardMouseAsync();Check(KbmPolicy!=null,"KBM policy unavailable");_kbmReadOnlyProjection=true;
    EnsureOnexThemeCatalog();var allowed=OnexAllowedThemeNames;
    Check(allowed.Length==20&&ThemeComboBox.Items.Count==20,"Theme picker still contains legacy choices");
    Check(WidgetThemes.Keys.OrderBy(x=>x).SequenceEqual(allowed.OrderBy(x=>x)),"Removed palettes remain active");
    Check(ThemeComboBox.Items.OfType<ComboBoxItem>().Select(x=>x.Content as string).SequenceEqual(allowed),"Retained theme order differs");
    Check(NormalizeOnexThemeName("Candy")==OnexPalette.Name&&NormalizeOnexThemeName("Catppuccin")=="Catppuccin Mocha"&&NormalizeOnexThemeName("Windows")=="Windows 11 Dark","Legacy migration failed");
    int index=0,pageVisits=0,colourChecks=0,unstableMaps=0;var navigationTimings=new JsonArray();
    foreach(string name in allowed)
    {
     isThemeInitialized=true;ApplyTheme(name);var theme=WidgetThemes[name];
     _onexKeyboardMouseNav.IsChecked=true;ApplyThemeToCurrentTab();UpdateLayout();await Task.Delay(30);
     foreach(var toggle in new[]{_kbmAcceleration,_kbmAdaptive})
      Check(toggle.Resources.TryGetValue("CheckBoxCheckBackgroundFillChecked",out var fill)&&fill is SolidColorBrush checkBrush&&checkBrush.Color==OnexKeyboardMouseAccent,"Checkbox fill ignores theme: "+name);
     foreach(var slider in new[]{_kbmMouseSpeed,_kbmScrollSpeed,_kbmDeadzone,_kbmHoldAcceleration,_kbmNudgeStep})
      Check(slider.Foreground is SolidColorBrush sliderBrush&&sliderBrush.Color==OnexKeyboardMouseAccent,"Slider accent ignores theme: "+name);
     if(name=="Dracula"){SetKbmSettingsCard(_kbmSections[0]);UpdateLayout();await SaveOnexImage("onex-ui375-kbm-dracula.png",_kbmSections[0].Detail,60);}
     PerformanceNavItem.IsChecked=true;ApplyThemeToCurrentTab();UpdateLayout();await Task.Delay(35);
     var navs=GetVisibleNavigationItems();
     foreach(var nav in navs)
     {
      Check(nav.Foreground is SolidColorBrush fore&&OnexContrast(fore.Color,theme.PageBackground)>=3,"Navigation icon contrast failed: "+name);
      Check(nav.Content is FrameworkElement art&&art.ActualWidth>0&&art.ActualHeight>0&&art.ActualWidth<=28&&art.ActualHeight<=28,"Navigation artwork clipped: "+name);
     }
     foreach(var tile in qsTileDefinitions.Where(x=>x.IconElement!=null&&x.TileButton?.IsEnabled==true))
     {
      Color? ink=tile.IconElement is IconElement icon?(icon.Foreground as SolidColorBrush)?.Color:tile.IconElement is Control control?(control.Foreground as SolidColorBrush)?.Color:null;
      if(ink.HasValue&&OnexContrast(ink.Value,theme.TileOff)<3)iconMismatches.Add(new JsonObject{{"theme",JsonValue.CreateStringValue(name)},{"tileOrdinal",JsonValue.CreateNumberValue(qsTileDefinitions.IndexOf(tile))},{"ink",JsonValue.CreateStringValue(ink.Value.ToString())}});
     }
     var map=OnexPageNavigationRows();UpdateLayout();map=OnexPageNavigationRows();int builds=_onexNavigationBuilds;
     var watch=System.Diagnostics.Stopwatch.StartNew();for(int n=0;n<12;n++)OnexPageNavigationRows();double cacheMs=watch.Elapsed.TotalMilliseconds/12;
     if(_onexNavigationBuilds!=builds)unstableMaps++;
     navigationTimings.Add(new JsonObject{{"theme",JsonValue.CreateStringValue(name)},{"cachedMapMs",JsonValue.CreateNumberValue(cacheMs)}});
     if(name==OnexPalette.Name)
     {
      Check((_releaseHome.FindName("LightPl2Value") as TextBlock)?.Foreground is SolidColorBrush pl2&&pl2.Color==OnexPalette.Violet,"Base PL2 value lost violet");
      Check((_releaseHome.FindName("LightPowerPl2") as TextBlock)?.Foreground is SolidColorBrush pl2Hint&&pl2Hint.Color==OnexPalette.Violet,"Base PL2 hint lost violet");
      Check((_releaseHome.FindName("LightPowerSlider") as Slider)?.Foreground is SolidColorBrush pl1&&pl1.Color==OnexPalette.Orange,"Base PL1 lost orange");
      Check(RailBrightnessSlider.Foreground is SolidColorBrush brightness&&brightness.Color==OnexPalette.Cyan&&RailVolumeSlider.Foreground is SolidColorBrush volume&&volume.Color==OnexPalette.Cyan,"Base media sliders lost cyan");
     }
     // Read actual public logical controls on every visible tab. Do not walk native templates.
     foreach(var tab in navs)
     {
      tab.IsChecked=true;ApplyThemeToCurrentTab();UpdateLayout();await Task.Delay(40);pageVisits++;
      var page=OnexNavigationRoot();Check(page!=null,"Visible page has no logical theme root: "+name+"/"+tab.Tag);
      int ordinal=0;foreach(var caption in OnexOwnedContent(page).OfType<TextBlock>().Where(x=>x.ActualWidth>0&&x.ActualHeight>0))
      {
       ordinal++;if(qsTileDefinitions.Any(tile=>ReferenceEquals(tile.StateText,caption)||ReferenceEquals(tile.NameText,caption)))continue;
       if(!_onexSurfaceRoles.TryGetValue(caption,out var role)||role.Text==0||!(caption.Foreground is SolidColorBrush ink))continue;
       if(!OnexBrandTheme&&role.Text<3)
       {if(ink.Color!=theme.TextPrimary&&ink.Color!=theme.TextSecondary)mismatches.Add(new JsonObject{{"theme",JsonValue.CreateStringValue(name)},{"page",JsonValue.CreateStringValue(tab.Tag as string??"")},{"ordinal",JsonValue.CreateNumberValue(ordinal)},{"role",JsonValue.CreateNumberValue(role.Text)},{"ink",JsonValue.CreateStringValue(ink.Color.ToString())}});colourChecks++;}
       if(!OnexBrandTheme&&role.Text==3)
       {if(ink.Color!=theme.EffectiveAccent())mismatches.Add(new JsonObject{{"theme",JsonValue.CreateStringValue(name)},{"page",JsonValue.CreateStringValue(tab.Tag as string??"")},{"ordinal",JsonValue.CreateNumberValue(ordinal)},{"role",JsonValue.CreateNumberValue(role.Text)},{"ink",JsonValue.CreateStringValue(ink.Color.ToString())}});colourChecks++;}
      }
      var focused=OnexPageNavigationRows().SelectMany(x=>x).Select(x=>x.Control).FirstOrDefault();
      if(focused!=null)
      {
       Check(focused.Focus(FocusState.Keyboard),"Logical page focus failed: "+name+"/"+tab.Tag);
       Check(focused.FocusVisualPrimaryBrush is SolidColorBrush stroke&&stroke.Color==OnexFocusColour&&focused.FocusVisualSecondaryThickness.Equals(new Thickness(0))&&focused.FocusVisualMargin.Equals(new Thickness(2)),"Mixed focus colours on page: "+name+"/"+tab.Tag+"/"+focused.GetType().Name);
      }
     }
     UpdateOnexStatsDisplay();
     foreach(var stat in _onexStatsValues.Where(x=>x.Key!="batteryLevel"))
      Check(stat.Value.Foreground is SolidColorBrush statInk&&statInk.Color==theme.TextPrimary,"Metrics value ignores theme: "+name);
     foreach(var sample in new[]{Tuple.Create(50,false),Tuple.Create(50,true),Tuple.Create(90,true)})
     {
      var colour=OnexBatteryCapIndicator.ColorFor(sample.Item1,true,90,sample.Item2,theme.TextPrimary,OnexLight(theme));
      Check(OnexContrast(colour,theme.MetricsBackground??theme.CardBackground)>=3,"Battery status contrast failed: "+name);
     }
     var panel=MainNavPanel as Panel;var background=panel.Background;panel.Background=new SolidColorBrush(theme.PageBackground);
     try{await SaveOnexImage("onex-ui375-nav-"+index+".png",panel,70);}finally{panel.Background=background;}
     rendered.Add(JsonValue.CreateStringValue(name));index++;
    }
    // Exercise the exact catalog -> base -> tab path that turned the PL2 watt labels orange.
    foreach(string name in new[]{"Dracula","Windows 11 Light","OLED"})
    {
     isThemeInitialized=false;ApplyTheme(name);ApplyTheme(OnexPalette.Name);
     _onexKeyboardMouseNav.IsChecked=true;PerformanceNavItem.IsChecked=true;UpdateLayout();
     Check((_releaseHome.FindName("LightPl2Value") as TextBlock)?.Foreground is SolidColorBrush watts&&watts.Color==OnexPalette.Violet,"PL2 colour broken by round trip: "+name);
    }
    _releasePowerNav.IsChecked=true;UpdateLayout();var choose=_onexPerformanceProfilePicker.Choose;choose.Focus(FocusState.Keyboard);await SaveOnexImage("onex-ui375-choose-focus.png",choose,70);
    foreach(var card in _onexPerfSections)Check(card.Header.CornerRadius.Equals(new CornerRadius(12)),"Performance card corner inconsistency");
    foreach(var card in _onexControllerSections)Check(card.Header.CornerRadius.Equals(new CornerRadius(12)),"Controller card corner inconsistency");
    SystemNavItem.IsChecked=true;await QueryOnexStartupAsync();UpdateLayout();
    Check(OnexStartupToggle.Focus(FocusState.Keyboard),"Startup control cannot focus");
    ConfigureOnexFocus(OnexStartupToggle);
    Check(ReferenceEquals(OnexStartupToggle.Style,Resources["OnexCompactToggleStyle"])&&OnexStartupToggle.ActualWidth==48&&OnexStartupToggle.ActualHeight==28,"Startup focus uses oversized native switch bounds");
    Check(OnexStartupToggle.FocusVisualSecondaryThickness==new Thickness(0)&&OnexStartupToggle.FocusVisualPrimaryThickness==new Thickness(2),"Startup has two native focus strokes");
    Check(OnexStartupToggle.FocusVisualPrimaryBrush is SolidColorBrush focus&&focus.Color==OnexFocusColour,"Startup focus colour differs");
    await SaveOnexImage("onex-ui375-startup-focus.png",OnexStartupCard,70);
    _onexKeyboardMouseNav.IsChecked=true;UpdateLayout();var probe=new Button{Content="QA navigation invalidation",MinHeight=32};_kbmBody.Children.Add(probe);
    try
    {
     UpdateLayout();await Task.Delay(40);Check(OnexPageNavigationRows().SelectMany(x=>x).Any(x=>ReferenceEquals(x.Control,probe)),"Navigation misses new content");
     probe.IsEnabled=false;Check(!OnexPageNavigationRows().SelectMany(x=>x).Any(x=>ReferenceEquals(x.Control,probe)),"Navigation cache retains disabled control");
     probe.IsEnabled=true;probe.Visibility=Visibility.Collapsed;Check(!OnexPageNavigationRows().SelectMany(x=>x).Any(x=>ReferenceEquals(x.Control,probe)),"Navigation cache retains hidden control");
    }
    finally{_kbmBody.Children.Remove(probe);}
    report["unstableNavigationMaps"]=JsonValue.CreateNumberValue(unstableMaps);report["iconMismatches"]=iconMismatches;report["colourMismatches"]=mismatches;report["navigationCacheTimings"]=navigationTimings;report["pageVisits"]=JsonValue.CreateNumberValue(pageVisits);report["actualTextColourChecks"]=JsonValue.CreateNumberValue(colourChecks);
    Check((values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:null)==saved,"Theme/icon test wrote owner choice");
    Check(unstableMaps==0,"Stable direction navigation recomputes geometry in "+unstableMaps+" themes");
    Check(iconMismatches.Count==0,"Icon audit found "+iconMismatches.Count+" contrast failures; see non-private coordinates in report");
    Check(mismatches.Count==0,"Theme audit found "+mismatches.Count+" stale text colours; see non-private coordinates in report");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["renderedThemes"]=rendered;
    report["retainedChoices"]=JsonValue.CreateNumberValue(allowed.Length);report["ownerThemeWrites"]=JsonValue.CreateNumberValue(0);
    report["actualOwnUwpRendering"]=JsonValue.CreateBooleanValue(true);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally
   {
    _kbmReadOnlyProjection=projection;_kbmState=null;isThemeInitialized=false;
    ApplyTheme(values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:OnexPalette.Name);
    isThemeInitialized=initialized;if(!projection)await ReadOnexKeyboardMouseAsync();
    if(original!=null&&original.Visibility==Visibility.Visible)original.IsChecked=true;
   }
  }
 }
}
