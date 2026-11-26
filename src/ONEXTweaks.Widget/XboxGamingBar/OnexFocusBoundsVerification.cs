using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexFocusBoundsAsync(JsonObject report)
  {
   int checks=0,focused=0;var pages=new JsonArray();var catalogs=new JsonArray();var captureRequests=new JsonArray();
   var original=GetVisibleNavigationItems().FirstOrDefault(tab=>tab.IsChecked==true);
   var originalTheme=currentThemeName;var originalPalette=_onexAppliedTheme;
   object fnHeader=null;bool fnSaved=false;var progress=await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-focus479-progress.json",CreationCollisionOption.ReplaceExisting);
   void Check(bool ok,string why){checks++;report["focusBoundsChecks"]=JsonValue.CreateNumberValue(checks);if(!ok)throw new InvalidOperationException(why);}
   async Task Wait(Func<bool> ready,string why){for(int n=0;n<160;n++){if(ready())return;await Task.Delay(25);}throw new TimeoutException(why);}
   bool Visible(FrameworkElement value){for(DependencyObject node=value;node!=null;node=VisualTreeHelper.GetParent(node))if(node is FrameworkElement element&&element.Visibility!=Visibility.Visible)return false;return value.IsLoaded&&value.ActualWidth>2&&value.ActualHeight>2;}
   Rect Bounds(FrameworkElement element,UIElement relative)=>element.TransformToVisual(relative).TransformBounds(new Rect(0,0,element.ActualWidth,element.ActualHeight));
   async Task Inspect(Control control,string role,bool textClearance=false,bool capture=false)
   {
    Check(control!=null&&control.IsEnabled&&Visible(control),role+" control unavailable");
    control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(25);UpdateLayout();
    ConfigureOnexFocus(control);Check(control.Focus(FocusState.Keyboard),role+" cannot receive keyboard/controller focus");FocusOnexOwnedControl(control);
    Check(_onexControlLayers.TryGetValue(control,out var layer)&&IsOnexFocusInkVisible(layer),role+" owned ring missing");
    var ink=layer.Ink;var ring=new Rect(ink.Geometry.Offset.X-1,ink.Geometry.Offset.Y-1,ink.Geometry.Size.X+2,ink.Geometry.Size.Y+2);
    Check(ReferenceEquals(ElementCompositionPreview.GetElementChildVisual(control),ink.Root)&&ink.Visual.Offset==System.Numerics.Vector3.Zero,role+" ring detached from compositor target");
    Check(ring.Left>=-.5&&ring.Top>=-.5&&ring.Right<=control.ActualWidth+.5&&ring.Bottom<=control.ActualHeight+.5,role+" ring escapes target bounds");
    Check(!control.UseSystemFocusVisuals,role+" duplicate stock focus");
    if(control is ComboBox)
    {
     var body=FindDescendantByName(control,"Background");Check(body!=null,role+" native input body missing");var expected=Bounds(body,control);
     Check(Math.Abs(ring.X-expected.X)<.6&&Math.Abs(ring.Y-expected.Y)<.6&&Math.Abs(ring.Width-expected.Width)<.6&&Math.Abs(ring.Height-expected.Height)<.6,role+" ring covers header instead of input body");
     var header=FindDescendantByName(control,"HeaderContentPresenter");if(header!=null&&Visible(header)&&header.ActualHeight>0)Check(Bounds(header,control).Bottom<=ring.Top+.6,role+" ring overlaps header");
     Check(FindDescendantByName(control,"HighlightBackground")?.Visibility==Visibility.Collapsed,role+" outward native focus halo remains");
    }
    if(textClearance)
    {
     foreach(var text in OnexOwnedContent(control).OfType<TextBlock>().Where(text=>Visible(text)&&!string.IsNullOrEmpty(text.Text)))
     {
      var rect=Bounds(text,control);if(rect.Bottom<=ring.Top+.6)continue;
      Check(rect.Left>=ring.Left+3&&rect.Right<=ring.Right-3&&rect.Top>=ring.Top+3&&rect.Bottom<=ring.Bottom-3,role+" text intersects inward focus stroke");
     }
    }
    focused++;
    if(capture)
    {
     var content=Window.Current.Content as FrameworkElement;var rect=Bounds(control,content);var color=ink.Brush.Color;
     var phase=new JsonObject{{"phase",JsonValue.CreateStringValue("capture")},{"role",JsonValue.CreateStringValue(role)},{"theme",JsonValue.CreateStringValue(currentThemeName)},{"utc",JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("o"))},{"x",JsonValue.CreateNumberValue(rect.X)},{"y",JsonValue.CreateNumberValue(rect.Y)},{"width",JsonValue.CreateNumberValue(rect.Width)},{"height",JsonValue.CreateNumberValue(rect.Height)},{"rootWidth",JsonValue.CreateNumberValue(content.ActualWidth)},{"rootHeight",JsonValue.CreateNumberValue(content.ActualHeight)},{"r",JsonValue.CreateNumberValue(color.R)},{"g",JsonValue.CreateNumberValue(color.G)},{"b",JsonValue.CreateNumberValue(color.B)}};
     var peer=FrameworkElementAutomationPeer.FromElement(control)??FrameworkElementAutomationPeer.CreatePeerForElement(control);var screen=peer?.GetBoundingRectangle()??new Rect();
     phase["rasterizationScale"]=JsonValue.CreateNumberValue(XamlRoot?.RasterizationScale??1);
     phase["controlAutomationId"]=JsonValue.CreateStringValue(control.Name??"");
     phase["screenX"]=JsonValue.CreateNumberValue(screen.X);phase["screenY"]=JsonValue.CreateNumberValue(screen.Y);phase["screenWidth"]=JsonValue.CreateNumberValue(screen.Width);phase["screenHeight"]=JsonValue.CreateNumberValue(screen.Height);
     captureRequests.Add(phase);await FileIO.WriteTextAsync(progress,phase.Stringify());await Task.Delay(500);
    }
   }
   try
   {
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending,"Protect pending owner edits");
    await ReadOnexControllerAsync();await ReadOnexScreenProfilesAsync();await ReadOnexFanProfilesAsync();await ReadOnexOverlayPresetsAsync();
    EnsureOnexUnifiedFocus();
    // Read-only focus sweep: no control activation and no backend setting writes.
    foreach(var nav in GetVisibleNavigationItems().ToArray())
    {
     nav.IsChecked=true;await Task.Delay(80);UpdateLayout();var root=OnexNavigationRoot();if(root==null)continue;ApplyOnexPageFocus();
     var controls=OnexOwnedContent(root).OfType<Control>().Where(control=>control.IsTabStop&&control.IsEnabled&&Visible(control)&&(control is Button||control is Windows.UI.Xaml.Controls.Primitives.ToggleButton||control is ComboBox||control is CheckBox||control is ToggleSwitch||control is Slider||control is TextBox)).Distinct().ToArray();
     int count=focused;foreach(var control in controls)await Inspect(control,"page"+pages.Count+"-"+(focused-count));
     pages.Add(new JsonObject{{"pageIndex",JsonValue.CreateNumberValue(pages.Count)},{"visibleControlsFocused",JsonValue.CreateNumberValue(focused-count)}});
    }
    report["focusSweepPages"]=pages;
    SystemNavItem.IsChecked=true;await Task.Delay(50);
    var settingsHeaders=new[]{_onexTabsHeader,ThemeExpandButton,ProTipsExpandButton,_onexDriversExpand,DebugExpandButton,AboutExpandButton};var sectionCounts=new JsonArray();
    foreach(var header in settingsHeaders)
    {
     bool was=header.IsChecked==true;
     try
     {
      header.IsChecked=true;await Task.Delay(70);UpdateLayout();ApplyOnexPageFocus();await Inspect(header,"settings-header",true);
      var controls=OnexOwnedContent(OnexNavigationRoot()).OfType<Control>().Where(control=>control.IsTabStop&&control.IsEnabled&&Visible(control)&&(control is Button||control is Windows.UI.Xaml.Controls.Primitives.ToggleButton||control is ComboBox||control is CheckBox||control is ToggleSwitch||control is Slider||control is TextBox)).Distinct().ToArray();
      foreach(var control in controls)await Inspect(control,"settings-expanded");sectionCounts.Add(JsonValue.CreateNumberValue(controls.Length));
     }
     finally{header.IsChecked=was;}
    }
    report["settingsSectionFocusedControlCounts"]=sectionCounts;
    _releasePowerNav.IsChecked=true;await ReadOnexPerformanceProfilesAsync();bool savedOpen=_savedProfilesExpanded;
    try
    {
     if(!savedOpen)PerfSavedProfilesExpandButton_Click(PerfSavedProfilesExpandButton,null);await Task.Delay(55);UpdateLayout();ApplyOnexPageFocus();
     await Inspect(ProfileSortComboBox,"performance-saved-sort");foreach(var control in _onexSavedProfileRows.SelectMany(row=>row).Where(control=>control.IsEnabled&&Visible(control)))await Inspect(control,"performance-saved-action",true);
     report["performanceSavedFocusedActions"]=JsonValue.CreateNumberValue(_onexSavedProfileRows.SelectMany(row=>row).Count(control=>control.IsEnabled&&Visible(control)));
    }
    finally{if(_savedProfilesExpanded!=savedOpen)PerfSavedProfilesExpandButton_Click(PerfSavedProfilesExpandButton,null);}

    var cases=new[]{new{Scope="Controller",Nav=LegionNavItem,Section=_onexControllerSavedProfiles},new{Scope="Screen",Nav=DisplayNavItem,Section=_onexScreenSavedProfiles},new{Scope="Fan",Nav=FanNavItem,Section=_onexFanSavedProfiles},new{Scope="Overlay",Nav=_releaseOsdNav,Section=_onexOverlaySavedPresets}};
    foreach(var item in cases)
    {
     item.Nav.IsChecked=true;await Wait(()=>item.Section.Header.IsLoaded,"Catalog header unavailable");var section=item.Section;bool open=section.Body.Visibility==Visibility.Visible;int sort=section.Sort.SelectedIndex;
     try
     {
      section.SetExpanded(true);await Task.Delay(50);UpdateLayout();ApplyOnexPageFocus();
      await Inspect(section.Header,item.Scope+"-saved-header",true,true);await Inspect(section.Sort,item.Scope+"-saved-sort",false,true);
      var rows=section.FocusRows().Select(row=>row.Where(control=>control.IsEnabled&&control.Visibility==Visibility.Visible).ToArray()).Where(row=>row.Length>0).ToArray();
      if(item.Scope!="Overlay")
      {
       Check(ReferenceEquals(section.Header.XYFocusDown,section.Sort),item.Scope+" expanded header has no down edge to sort");
       if(rows.Length>2)Check(ReferenceEquals(section.Sort.XYFocusDown,rows[2][0]),item.Scope+" sort has no down edge into saved entries");
       for(int r=1;r<rows.Length;r++)
       {
        Check(ReferenceEquals(rows[r][0].XYFocusUp,rows[r-1][0]),item.Scope+" upward saved-list edge incorrect");
        for(int c=0;c<rows[r].Length;c++)
        {
         var target=rows[r][c];Check(target.Focus(FocusState.Keyboard)&&ReferenceEquals(FocusManager.GetFocusedElement(),target),item.Scope+" real focus cannot traverse saved row");
         if(c+1<rows[r].Length)Check(ReferenceEquals(target.XYFocusRight,rows[r][c+1]),item.Scope+" horizontal saved-row edge incorrect");
        }
       }
      }
      foreach(var row in section.Rows.Values)foreach(var control in new[]{row.Choose,row.Rename,row.Remove}.Where(control=>control.IsEnabled&&control.Visibility==Visibility.Visible))await Inspect(control,item.Scope+"-saved-action",true);
      var first=section.Rows.Values.FirstOrDefault(row=>row.Choose.IsEnabled);
      if(first!=null)
      {
       string id=first.Id;first.Choose.Focus(FocusState.Keyboard);section.Apply(true);await Task.Delay(35);
       Check(ReferenceEquals(first.Choose,section.Rows[id].Choose),item.Scope+" unchanged repaint recreated row controls");
       section.Sort.SelectedIndex=(section.Sort.SelectedIndex+1)%3;await Task.Delay(55);
       Check(ReferenceEquals(FocusManager.GetFocusedElement(),section.Rows[id].Choose),item.Scope+" row rebuild lost real focus");
      }
      for(int order=0;order<3;order++){section.Sort.SelectedIndex=order;section.Apply(true);Check(section.Rows.Count==section.EntryCount,item.Scope+" sorting lost entries");if(item.Scope!="Overlay")Check(ReferenceEquals(section.Header.XYFocusDown,section.Sort),item.Scope+" sorting broke graph");}
      if(section.Rows.Count>0&&section.Rows.Values.First().Choose.IsEnabled)section.Rows.Values.First().Choose.Focus(FocusState.Keyboard);
      section.SetExpanded(false);Check(section.Rows.Count==0&&section.List.Children.Count==0,item.Scope+" closed list retained controls");
      Check(!section.FocusRows().Skip(1).Any(),item.Scope+" collapsed graph includes hidden descendants");
      catalogs.Add(new JsonObject{{"scope",JsonValue.CreateStringValue(item.Scope)},{"entries",JsonValue.CreateNumberValue(section.EntryCount)},{"programmaticRealControlsAndNavigationGraph",JsonValue.CreateBooleanValue(true)}});
     }
     finally{section.Sort.SelectedIndex=sort;section.SetExpanded(open);}
    }
    foreach(string name in new[]{"ONEXPLAYER","Windows 11 Light"})
    {
     // Theme is an in-memory UI preview; no saved theme/language is changed.
     currentThemeName=name;_onexAppliedTheme=name==OnexPalette.Name?OnexPalette.CreateTheme():WidgetThemes[name];
     _onexKeyboardMouseNav.IsChecked=true;await Wait(()=>_kbmFn.IsLoaded,"FN control unavailable");if(!fnSaved){fnHeader=_kbmFn.Header;fnSaved=true;}
     foreach(string language in new[]{"en","ru"})
     {
      SetKbmSettingsCard(_kbmSections[2]);
      _kbmFn.Header=OnexStrings.GetForLanguage("FN modifier · hold",language);ApplyOnexCatalogSurfaces(_kbmBody,_onexAppliedTheme);UpdateLayout();await Inspect(_kbmFn,"fn-"+language,false,true);
     }
     _kbmFn.Header=fnHeader;LegionNavItem.IsChecked=true;await Wait(()=>_onexControllerTransport.IsLoaded,"Controller Mode unavailable");ApplyOnexCatalogSurfaces(OnexNavigationRoot(),_onexAppliedTheme);UpdateLayout();await Inspect(_onexControllerTransport,"controller-mode",false,true);
     DisplayNavItem.IsChecked=true;await Wait(()=>_onexHdrCalibration.IsLoaded,"HDR selector unavailable");ApplyOnexCatalogSurfaces(OnexNavigationRoot(),_onexAppliedTheme);UpdateLayout();await Inspect(_onexHdrCalibration,"hdr-selector",false,true);
    }
    report["focusCaptureRequests"]=captureRequests;report["focusChromeMeasurements"]=JsonValue.CreateNumberValue(_onexFocusChromeMeasurements);report["focusChromeCacheHits"]=JsonValue.CreateNumberValue(_onexFocusChromeCacheHits);
    report["savedCatalogFocusCases"]=catalogs;report["actualFocusedControls"]=JsonValue.CreateNumberValue(focused);
    report["ownerActionsInvoked"]=JsonValue.CreateBooleanValue(false);report["themeOrLanguagePersisted"]=JsonValue.CreateBooleanValue(false);
    report["physicalGamepadMouseTouchTested"]=JsonValue.CreateBooleanValue(false);report["inMemoryThemeAndTranslatedHeaderPreview"]=JsonValue.CreateBooleanValue(true);
   }
   finally
   {
    if(fnSaved)_kbmFn.Header=fnHeader;currentThemeName=originalTheme;_onexAppliedTheme=originalPalette;HideOnexAllFocusLayers();
    if(original!=null)original.IsChecked=true;ApplyOnexCatalogSurfaces(OnexNavigationRoot(),_onexAppliedTheme);ApplyOnexTabPalette();ApplyOnexPageFocus();
    await FileIO.WriteTextAsync(progress,new JsonObject{{"phase",JsonValue.CreateStringValue("complete")},{"utc",JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("o"))}}.Stringify());
   }
  }
 }
}
