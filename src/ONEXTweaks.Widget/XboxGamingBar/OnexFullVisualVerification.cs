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
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexFullVisualsAsync(JsonObject report)
  {
   int checks=0,focused=0,pages=0,panels=0,snapshots=0;report["xamlCropsAreNotCompositionFocusPixelEvidence"]=JsonValue.CreateBooleanValue(true);
   void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   var prefs=ApplicationData.Current.LocalSettings.Values;string saved=prefs.TryGetValue("WidgetTheme",out var themeValue)?themeValue as string:null;
   bool initialized=isThemeInitialized;var originalTab=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
   string perfOpen=_onexPerfOpen,controllerOpen=_onexControllerOpen;var openedBodies=new Dictionary<FrameworkElement,Visibility>();Border fixture=null;var phases=new JsonArray();report["visualPhases"]=phases;
   async Task FocusOne(Control control,string name,bool capture=false)
   {
    ConfigureOnexFocus(control);UpdateLayout();control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(60);
    Check(control.Focus(FocusState.Keyboard),"Cannot focus "+name);await Task.Delay(25);FocusOnexOwnedControl(control);
    Check(!control.UseSystemFocusVisuals,"Stock focus still enabled: "+name);
    var layer=_onexControlLayers.TryGetValue(control,out var registered)?registered:_onexMainFocusLayer;RefreshOnexFocusLayer(layer);
    if(layer==null||!IsOnexFocusInkVisible(layer)||!ReferenceEquals(layer.Target,control))
    {
     report["failedFocus"]=new JsonObject{{"scene",JsonValue.CreateStringValue(name)},{"type",JsonValue.CreateStringValue(control.GetType().Name)},{"controlName",JsonValue.CreateStringValue(control.Name??"")},{"loaded",JsonValue.CreateBooleanValue(control.IsLoaded)},{"enabled",JsonValue.CreateBooleanValue(control.IsEnabled)},{"isTabStop",JsonValue.CreateBooleanValue(control.IsTabStop)},{"focusState",JsonValue.CreateStringValue(control.FocusState.ToString())},{"focusRetained",JsonValue.CreateBooleanValue(ReferenceEquals(FocusManager.GetFocusedElement(),control))},{"hasLayer",JsonValue.CreateBooleanValue(layer!=null)},{"hasTarget",JsonValue.CreateBooleanValue(ReferenceEquals(layer?.Target,control))},{"frameVisible",JsonValue.CreateBooleanValue(IsOnexFocusInkVisible(layer))},{"width",JsonValue.CreateNumberValue(control.ActualWidth)},{"height",JsonValue.CreateNumberValue(control.ActualHeight)}};
     throw new InvalidOperationException("Owned focus layer missing: "+name);
    }
    var visible=_onexFocusLayers.Count(x=>x.TryGetTarget(out var active)&&IsOnexFocusInkVisible(active));
    Check(visible==1,"More than one owned focus frame: "+name);
    Check(layer.Ink.Radius>=8,"Square focus corner: "+name);
    Check(Math.Abs(layer.Ink.Width-control.ActualWidth)<.5&&Math.Abs(layer.Ink.Height-control.ActualHeight)<.5,"Focus bounds differ: "+name);focused++;
    if(capture){await SaveOnexOwnedFocusCropAsync(control,"onex-visual382-"+name+".png");snapshots++;}
   }
   async Task Visit(string name,FrameworkElement root)
   {
    UpdateLayout();await Task.Delay(70);ApplyOnexPageFocus();
    var controls=OnexNavigableContent(root).Where(x=>!(x is ComboBoxItem)&&!(x is MenuFlyoutItemBase)).Distinct().ToArray();
    Check(controls.Length>0,"Visible scene has no focusable controls: "+name);
    foreach(var control in controls)await FocusOne(control,name+"-"+focused,false);
    phases.Add(new JsonObject{{"scene",JsonValue.CreateStringValue(name)},{"focusedControls",JsonValue.CreateNumberValue(controls.Length)}});pages++;
   }
   try
   {
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending&&_onexControllerPending==null&&_kbmTuning.Count==0,"Protect pending owner input/dialog");
    EnsureOnexUnifiedFocus();isThemeInitialized=false;
    // Public-only fixture uses the real production styles and input controls. Values have no backend handlers.
    foreach(string theme in new[]{OnexPalette.Name,"Dracula","Windows 11 Light"})
    {
     ApplyTheme(theme);var body=new StackPanel{Spacing=12};
     var pl1=new Slider{Minimum=8,Maximum=40,Value=20,Header="TDP PL1 · visual preview",Foreground=OnexSolidBrush(OnexBrandTheme?OnexPalette.Orange:_onexAppliedTheme.EffectiveAccent())};
     var pl2=new Slider{Minimum=8,Maximum=41,Value=21,Header="TDP PL2 · visual preview",Foreground=OnexSolidBrush(OnexBrandTheme?OnexPalette.Violet:_onexAppliedTheme.EffectiveAccent())};
     var percent=new Slider{Minimum=0,Maximum=100,Value=50,Header="Volume · visual preview",Foreground=OnexSolidBrush(OnexBrandTheme?OnexPalette.Cyan:_onexAppliedTheme.EffectiveAccent())};
     var cpu=new ToggleSwitch{Header="CPU Boost",OnContent="",OffContent="",IsOn=true};
     var acceleration=new CheckBox{Content="Mouse acceleration",IsChecked=true};
     var choice=OnexSettingsChoice("Profile · visual preview");choice.Items.Add(new ComboBoxItem{Content="Preview profile"});choice.Items.Add(new ComboBoxItem{Content="Second preview profile"});choice.SelectedIndex=0;
     var button=new Button{Content="Restore Default",Style=OnexSettingsButtonStyle(false)};
     var vertical=new Slider{Minimum=0,Maximum=100,Value=50,Orientation=Orientation.Vertical,Height=160,Width=48,HorizontalAlignment=HorizontalAlignment.Center,Foreground=OnexDialogAccent};
     foreach(var element in new UIElement[]{pl1,pl2,percent,cpu,acceleration,choice,button,vertical})body.Children.Add(element);
     var host=WrapOnexFocusContent(body);var scroll=new ScrollViewer{Content=host,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(12)};
     fixture=new Border{Background=OnexDialogSurface,BorderBrush=OnexDialogBorder,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Margin=new Thickness(20),Child=scroll};Canvas.SetZIndex(fixture,200000);OnexStartupRoot.Children.Add(fixture);UpdateLayout();await Task.Delay(90);
     foreach(var control in new Control[]{pl1,pl2,percent,cpu,acceleration,choice,button,vertical})ConfigureOnexFocus(control);
     var label=theme==OnexPalette.Name?"base":theme=="Dracula"?"dracula":"light";
     foreach(var pair in new[]{Tuple.Create((Control)pl1,"pl1"),Tuple.Create((Control)pl2,"pl2"),Tuple.Create((Control)percent,"percentage"),Tuple.Create((Control)cpu,"cpu-on"),Tuple.Create((Control)acceleration,"acceleration-on"),Tuple.Create((Control)choice,"choice"),Tuple.Create((Control)button,"button"),Tuple.Create((Control)vertical,"vertical")})
      await FocusOne(pair.Item1,label+"-"+pair.Item2,true);
     cpu.IsOn=false;await FocusOne(cpu,label+"-cpu-off",true);cpu.IsOn=true;await FocusOne(cpu,label+"-cpu-on-again",true);
     acceleration.IsChecked=false;await FocusOne(acceleration,label+"-acceleration-off",true);
     void SliderInk()
     {
      // Only inspect the root and named parts of our own stable template.
      var owned=VisualTreeHelper.GetChild(percent,0) as Grid;Check(owned?.Name=="OwnedSliderRoot","Owned slider root unavailable");
      var thumb=owned.FindName("HorizontalThumb") as Windows.UI.Xaml.Controls.Primitives.Thumb;Check(thumb!=null&&thumb.Visibility==Visibility.Visible,"Slider thumb unavailable");
      var bounds=thumb.TransformToVisual(percent).TransformBounds(new Rect(0,0,thumb.ActualWidth,thumb.ActualHeight));
      Check(bounds.Left>=-.5&&bounds.Right<=percent.ActualWidth+.5&&bounds.Top>=-.5&&bounds.Bottom<=percent.ActualHeight+.5,"Slider thumb outside control at "+percent.Value);
      var header=owned.FindName("HeaderContentPresenter") as ContentPresenter;Check(header!=null&&header.Visibility==Visibility.Visible&&header.ActualHeight>0,"Owned slider header disappeared");
     }
     double width=percent.ActualWidth;percent.Value=0;UpdateLayout();await FocusOne(percent,label+"-percentage-min",true);Check(percent.ActualWidth==width,"Percentage track width changed at minimum");SliderInk();percent.Value=100;UpdateLayout();await FocusOne(percent,label+"-percentage-max",true);Check(percent.ActualWidth==width,"Percentage track width changed at maximum");SliderInk();
     choice.IsDropDownOpen=true;await Task.Delay(100);
     foreach(var item in choice.Items.OfType<ComboBoxItem>()){ConfigureOnexFocus(item);Check(!item.UseSystemFocusVisuals&&ReferenceEquals(item.Template,OnexInlineFocusTemplate(typeof(ComboBoxItem))),"Dropdown retains stock focus template");}
     choice.IsDropDownOpen=false;
     HideOnexAllFocusLayers();OnexStartupRoot.Children.Remove(fixture);fixture=null;panels++;
    }
    ApplyTheme(saved??OnexPalette.Name);
    // Walk actual visible pages without activating their actions or changing any value.
    foreach(var tab in GetVisibleNavigationItems().ToArray()){tab.IsChecked=true;await Visit("page-"+(tab.Tag as string??""),OnexNavigationRoot());}
    _releasePowerNav.IsChecked=true;await SetOnexPerfCardAsync(null);
    foreach(var section in _onexPerfSections)
    {
     await SetOnexPerfCardAsync(section.Id);await Visit("performance-"+section.Id,OnexNavigationRoot());panels++;
     if(section.Id=="tdp"){if(TDPSlider.IsLoaded&&TDPSlider.IsEnabled)await FocusOne(TDPSlider,"actual-performance-pl1",true);if(TDPBoostFPPTSliderCard.IsLoaded&&TDPBoostFPPTSliderCard.IsEnabled&&TDPBoostSliderArea.Visibility==Visibility.Visible)await FocusOne(TDPBoostFPPTSliderCard,"actual-performance-pl2",true);}
     if(section.Id=="cpu"&&CPUBoostToggle.IsLoaded&&CPUBoostToggle.IsEnabled)await FocusOne(CPUBoostToggle,"actual-cpu-boost",true);
    }
    await SetOnexPerfCardAsync(null);
    LegionNavItem.IsChecked=true;SetOnexControllerCard(null);
    foreach(var card in _onexControllerSections.Where(x=>x.Header.IsEnabled))
    {SetOnexControllerCard(card.Id);await Visit("controller-"+card.Id,OnexNavigationRoot());panels++;}
    SetOnexControllerCard(null);
    _onexKeyboardMouseNav.IsChecked=true;await Visit("actual-keyboard-mouse",OnexNavigationRoot());if(_kbmAcceleration.IsEnabled)await FocusOne(_kbmAcceleration,"actual-mouse-acceleration",true);
    PerformanceNavItem.IsChecked=true;await Visit("actual-home",OnexNavigationRoot());if(_releaseHome.PowerInput.IsEnabled)await FocusOne(_releaseHome.PowerInput,"actual-home-pl1",true);
    var actualPl2=_releaseHome.FindName("OnexPl2Slider") as Slider;if(actualPl2.IsEnabled&&actualPl2.Visibility==Visibility.Visible)await FocusOne(actualPl2,"actual-home-pl2",true);
    // Expand each remaining production page section by changing only its UI visibility.
    // No profile/mode/value toggles or action buttons are invoked.
    foreach(var pair in new[]{Tuple.Create(DisplayNavItem,(FrameworkElement)_onexScreenSavedProfiles?.Body),Tuple.Create(FanNavItem,(FrameworkElement)_onexFanSavedProfiles?.Body),Tuple.Create(LegionNavItem,(FrameworkElement)_onexControllerSavedProfiles?.Body),Tuple.Create(_releaseOsdNav,(FrameworkElement)_onexOverlaySavedPresets?.Body),Tuple.Create(_releasePowerNav,(FrameworkElement)PerfSavedProfilesContent),Tuple.Create(SystemNavItem,(FrameworkElement)_onexTabsBody),Tuple.Create(SystemNavItem,(FrameworkElement)ThemeContent),Tuple.Create(SystemNavItem,(FrameworkElement)AboutContent),Tuple.Create(SystemNavItem,(FrameworkElement)_onexDriversBody),Tuple.Create(SystemNavItem,(FrameworkElement)ProTipsContent)})
    {
     if(pair.Item1==null||pair.Item2==null)continue;pair.Item1.IsChecked=true;openedBodies[pair.Item2]=pair.Item2.Visibility;pair.Item2.Visibility=Visibility.Visible;ApplyOnexPageSpacing();await Visit("expanded-section-"+panels,OnexNavigationRoot());panels++;
    }
    // The identical original gamepad silhouette is retained across all themes; inspect actual nested icon tint.
    int themeIndex=0;foreach(string theme in OnexAllowedThemeNames)
    {
     ApplyTheme(theme);var navBackground=MainNavPanel.Background;MainNavPanel.Background=OnexSolidBrush(_onexAppliedTheme.PageBackground);try{await SaveOnexImage("onex-visual382-nav-"+(themeIndex++)+".png",OnexNavRow,50);}finally{MainNavPanel.Background=navBackground;}var artwork=LegionNavItem.Content as Viewbox;Check(artwork?.Child is BitmapIcon bitmap&&bitmap.ShowAsMonochrome&&bitmap.UriSource.AbsoluteUri.EndsWith("/controller_xboxseries.png"),"Alternate gamepad artwork: "+theme);Check(OnexNavigationIcons(LegionNavItem.Content as DependencyObject).Count()==1,"Gamepad tint audit missed Viewbox.Child");
     if(OnexLight(_onexAppliedTheme)&&_onexNavigationGuide!=null)
     {SystemNavItem.IsChecked=true;ProTipsContent.Visibility=Visibility.Visible;ApplyOnexSettingsDesign();UpdateLayout();await SaveOnexImage("onex-visual382-guide-"+themeIndex+".png",_onexNavigationGuide,50);}
     foreach(var hint in new[]{OnexLtHint,OnexRtHint})
     {Check(hint.Background is SolidColorBrush hintBg&&hint.Child is TextBlock hintText&&hintText.Foreground is SolidColorBrush hintInk&&OnexContrast(hintInk.Color,hintBg.Color)>=4.5&&hint.Opacity==1&&hintText.Opacity==1,"Navigation hint contrast failed: "+theme);}
     if(OnexCatalogTheme)foreach(var nav in GetVisibleNavigationItems())foreach(var icon in OnexNavigationIcons(nav.Content as DependencyObject))Check(icon.Foreground is SolidColorBrush ink&&ink.Color==_onexAppliedTheme.TextPrimary,"Inherited foreign nav icon tint: "+theme);
    }
    Check((prefs.TryGetValue("WidgetTheme",out var fresh)?fresh as string:null)==saved,"Visual audit wrote owner theme");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["actualFocusedControls"]=JsonValue.CreateNumberValue(focused);report["actualPageScenes"]=JsonValue.CreateNumberValue(pages);report["expandedPanelsAndFixtures"]=JsonValue.CreateNumberValue(panels);report["croppedFocusImages"]=JsonValue.CreateNumberValue(snapshots);
    report["fixtureValuesAreSynthetic"]=JsonValue.CreateBooleanValue(true);report["productionPagesAlsoVisited"]=JsonValue.CreateBooleanValue(true);report["backendWritesRequested"]=JsonValue.CreateNumberValue(0);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally
   {
    HideOnexAllFocusLayers();if(fixture!=null)OnexStartupRoot.Children.Remove(fixture);foreach(var prior in openedBodies)prior.Key.Visibility=prior.Value;
    isThemeInitialized=false;ApplyTheme(prefs.TryGetValue("WidgetTheme",out var fresh)?fresh as string:OnexPalette.Name);isThemeInitialized=initialized;
    if(_onexPerfOpen!=perfOpen)await SetOnexPerfCardAsync(perfOpen);if(_onexControllerOpen!=controllerOpen)SetOnexControllerCard(controllerOpen);if(originalTab!=null&&originalTab.Visibility==Visibility.Visible)originalTab.IsChecked=true;
   }
  }
 }
}
