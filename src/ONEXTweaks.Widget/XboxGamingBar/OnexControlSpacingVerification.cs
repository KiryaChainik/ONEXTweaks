using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexControlSpacingAsync(JsonObject report)
  {
   int checks=0,toggles=0,metricCount=0,directions=0,activations=0;var inspectedToggles=new System.Collections.Generic.HashSet<ToggleSwitch>();var toggleCases=new JsonArray();var headerCases=new JsonArray();
   var original=GetVisibleNavigationItems().FirstOrDefault(tab=>tab.IsChecked==true);bool appearance=ThemeExpandButton.IsChecked==true;bool savedOpen=_savedProfilesExpanded;string perfOpen=_onexPerfOpen;
   void Check(bool ok,string why){checks++;report["controlSpacingChecks"]=JsonValue.CreateNumberValue(checks);if(!ok)throw new InvalidOperationException(why);}
   async Task Wait(Func<bool> ready,string why){for(int n=0;n<160;n++){if(ready())return;await Task.Delay(25);}throw new TimeoutException(why);}
   bool Visible(FrameworkElement value){if(value==null||!value.IsLoaded||value.ActualWidth<2||value.ActualHeight<2)return false;for(DependencyObject node=value;node!=null;node=VisualTreeHelper.GetParent(node))if(node is FrameworkElement element&&element.Visibility!=Visibility.Visible)return false;return true;}
   Rect Bounds(FrameworkElement child,UIElement parent)=>child.TransformToVisual(parent).TransformBounds(new Rect(0,0,child.ActualWidth,child.ActualHeight));
   async Task<Rect> Ring(Control control,string role)
   {
    control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(15);UpdateLayout();ConfigureOnexFocus(control);
    Check(control.Focus(FocusState.Keyboard),role+" focus failed");FocusOnexOwnedControl(control);
    Check(_onexControlLayers.TryGetValue(control,out var layer)&&IsOnexFocusInkVisible(layer),role+" focus ring missing");var ink=layer.Ink;
    var ring=new Rect(ink.Geometry.Offset.X-1,ink.Geometry.Offset.Y-1,ink.Geometry.Size.X+2,ink.Geometry.Size.Y+2);
    Check(!control.UseSystemFocusVisuals,role+" duplicate native outline");Check(ReferenceEquals(ElementCompositionPreview.GetElementChildVisual(control),ink.Root),role+" ring not attached locally");
    Check(ring.Left>=-.5&&ring.Top>=-.5&&ring.Right<=control.ActualWidth+.5&&ring.Bottom<=control.ActualHeight+.5,role+" outline outside control");return ring;
   }
   async Task Toggle(ToggleSwitch control,string role)
   {
    if(!inspectedToggles.Add(control))return;
    var ring=await Ring(control,role);var body=FindDescendantByName(control,"SwitchAreaGrid")??FindDescendantByName(control,"TrackOff");Check(body!=null,role+" actual toggle body missing");var box=Bounds(body,control);
    Check(box.Left-ring.Left>=7&&box.Top-ring.Top>=7&&ring.Right-box.Right>=7&&ring.Bottom-box.Bottom>=7,role+" toggle content has less than7px outline clearance");
    Check(ring.Width>=control.ActualWidth-.5&&ring.Height>=control.ActualHeight-.5,role+" whole padded toggle not focused");toggles++;report["toggleCases"]=toggleCases;toggleCases.Add(new JsonObject{{"name",JsonValue.CreateStringValue(role)},{"width",JsonValue.CreateNumberValue(ring.Width)},{"height",JsonValue.CreateNumberValue(ring.Height)},{"leftGap",JsonValue.CreateNumberValue(box.Left-ring.Left)},{"topGap",JsonValue.CreateNumberValue(box.Top-ring.Top)}});
   }
   async Task Header(Control header,string role)
   {
    Check(Visible(header)&&header.IsEnabled,role+" saved header unavailable");var ring=await Ring(header,role);Check(header.HorizontalContentAlignment==HorizontalAlignment.Stretch,role+" content not stretched");
    Check(header.ActualWidth>200&&header.Padding.Left>=10&&header.Padding.Right>=10,role+" still arrow-only target");
    Check(header is ContentControl content&&content.Content is Grid,role+" full caption not inside focus target");
    var grid=(Grid)((ContentControl)header).Content;var box=Bounds(grid,header);Check(box.Left-ring.Left>=9&&ring.Right-box.Right>=9&&box.Top-ring.Top>=7&&ring.Bottom-box.Bottom>=7,role+" caption crowds outline");
    Check(OnexPageNavigationRows().SelectMany(row=>row).Any(cell=>ReferenceEquals(cell.Control,header)),role+" header absent from production navigation");report["savedHeaderCases"]=headerCases;headerCases.Add(new JsonObject{{"scope",JsonValue.CreateStringValue(role)},{"width",JsonValue.CreateNumberValue(header.ActualWidth)},{"wholeCaptionInsideFocusTarget",JsonValue.CreateBooleanValue(true)}});
   }
   try
   {
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending,"Protect pending owner edits");await _onexUiFontApplyTask;EnsureOnexUnifiedFocus();
    foreach(var nav in GetVisibleNavigationItems().ToArray())
    {
     nav.IsChecked=true;if(ReferenceEquals(nav,SystemNavItem))ThemeExpandButton.IsChecked=true;await Task.Delay(75);UpdateLayout();ApplyOnexPageFocus();
     var root=OnexNavigationRoot();if(root==null)continue;
     foreach(var toggle in OnexOwnedContent(root).OfType<ToggleSwitch>().Where(value=>value.IsEnabled&&Visible(value)).Distinct().ToArray())await Toggle(toggle,toggle.Name.Length>0?toggle.Name:"page-toggle");
    }
    _releasePowerNav.IsChecked=true;await ReadOnexPerformanceProfilesAsync();
    foreach(var section in _onexPerfSections)
    {
     if(_onexPerfOpen!=section.Id)await SetOnexPerfCardAsync(section.Id);await Task.Delay(50);UpdateLayout();ApplyOnexPageFocus();
     foreach(var toggle in OnexOwnedContent(section.Detail).OfType<ToggleSwitch>().Where(value=>value.IsEnabled&&Visible(value)).ToArray())await Toggle(toggle,toggle.Name.Length>0?toggle.Name:section.Id+"-toggle");
    }
    if(_onexPerfOpen!=perfOpen)await SetOnexPerfCardAsync(perfOpen);
    Check(inspectedToggles.Contains(CPUBoostToggle),"Actual CPU Boost toggle not covered");
    Check(toggles>=5,"Too few actual page toggles inspected");
    _releasePowerNav.IsChecked=true;await ReadOnexPerformanceProfilesAsync();await Task.Delay(50);UpdateLayout();await Header(PerfSavedProfilesExpandButton,"Performance");
    if(!_savedProfilesExpanded){PerfSavedProfilesExpandButton.IsChecked=true;PerfSavedProfilesExpandButton_Click(PerfSavedProfilesExpandButton,null);}await Task.Delay(50);UpdateLayout();
    var perfRows=OnexPageNavigationRows();Check(ReferenceEquals(OnexPageNavigationTarget(perfRows,PerfSavedProfilesExpandButton,2),ProfileSortComboBox),"Performance header Down cannot enter sort");
    foreach(var item in new[]{new{Nav=LegionNavItem,Scope="Controller",Section=_onexControllerSavedProfiles},new{Nav=DisplayNavItem,Scope="Screen",Section=_onexScreenSavedProfiles},new{Nav=FanNavItem,Scope="Fan",Section=_onexFanSavedProfiles},new{Nav=_releaseOsdNav,Scope="Overlay",Section=_onexOverlaySavedPresets}})
    {
     item.Nav.IsChecked=true;await Task.Delay(80);UpdateLayout();await Header(item.Section.Header,item.Scope);bool open=item.Section.Body.Visibility==Visibility.Visible;
     try{item.Section.SetExpanded(true);await Task.Delay(30);UpdateLayout();var rows=OnexPageNavigationRows();Check(rows.SelectMany(row=>row).Any(cell=>ReferenceEquals(cell.Control,item.Section.Sort)),item.Scope+" expanded sort unreachable");Check(item.Section.Sort.Focus(FocusState.Keyboard),item.Scope+" expanded sort focus fails");}finally{item.Section.SetExpanded(open);}
    }
    _releaseOsdNav.IsChecked=true;await OpenOnexOverlaySettingsAsync();await Wait(()=>!_onexOverlayOpening&&!_onexOverlayBusy&&!isLoadingOSDConfig&&_onexOverlayConfigReady,"Overlay read did not finish");
    int level=osdCustomizeLevel;bool loading=_isLoadingOSDConfig;isLoadingOSDConfig=true;
    try
    {
     osdCustomizeLevel=4;RefreshOSDItemsControl();UpdateOnexOverlayPresetUi();UpdateLayout();await Task.Delay(80);UpdateLayout();ApplyOnexPageFocus();
     Check(!OSDItemsControl.IsTabStop,"Metrics wrapper is still a tab stop");var graphWatch=System.Diagnostics.Stopwatch.StartNew();var rows=OnexPageNavigationRows();graphWatch.Stop();report["firstMetricGraphBuildMs"]=JsonValue.CreateNumberValue(graphWatch.Elapsed.TotalMilliseconds);Check(graphWatch.Elapsed.TotalMilliseconds<150,"First metric navigation graph exceeded150ms");var flat=rows.SelectMany(row=>row).Select(cell=>cell.Control).ToArray();
     Check(!flat.Contains(OSDItemsControl),"Metrics wrapper still in navigation graph");Check(OSDItemsControl.Items.Count>=5,"Actual metric catalog empty");
     Check(FocusFirstOsdItemCheckBox(),"Cannot enter actual metric checkboxes");
     Check(ReferenceEquals(FocusManager.GetFocusedElement(),OnexOverlayMetricRow(OSDItemsControl,0)?.Children.OfType<CheckBox>().Single()),"Metrics entry point is not first checkbox");
     var graph=new JsonArray();foreach(var cell in rows.SelectMany(row=>row))graph.Add(new JsonObject{{"name",JsonValue.CreateStringValue(cell.Control.Name??"")},{"x",JsonValue.CreateNumberValue(cell.X)},{"y",JsonValue.CreateNumberValue(cell.Y)}});report["overlayNavigationGeometry"]=graph;
     Check(OnexPageNavigationTarget(rows,OsdPanelNavOrder().Last(control=>control.IsEnabled&&OnexOverlayControlVisible(control)),2) is CheckBox,"Down from final overlay setting does not enter a metric checkbox");
     Control prior=null;
     for(int index=0;index<OSDItemsControl.Items.Count;index++)
     {
      var row=OnexOverlayMetricRow(OSDItemsControl,index);Check(row!=null,"Own realised metric row missing");var check=row.Children.OfType<CheckBox>().Single();Check(flat.Contains(check),"Metric checkbox missing from graph");
      foreach(var action in row.Children.OfType<Button>())
      {
       Check(ReferenceEquals(action.Template,Resources["OnexMetricActionTemplate"]),"Metric action replaced its initial shared template");
       Check(VisualStateManager.GoToState(action,"PointerOver",false)&&VisualStateManager.GoToState(action,"Pressed",false)&&VisualStateManager.GoToState(action,"Normal",false),"Metric action pointer states missing");
       Check(VisualStateManager.GoToState(action,"Disabled",false)&&VisualStateManager.GoToState(action,action.IsEnabled?"Normal":"Disabled",false),"Metric action disabled state missing");
      }
      if(prior!=null){Check(ReferenceEquals(OnexPageNavigationTarget(rows,prior,2),check),"Down skips a metric checkbox");Check(ReferenceEquals(OnexPageNavigationTarget(rows,check,-2),prior),"Up skips a metric checkbox");directions+=2;}
      await Ring(check,"metric-checkbox");bool before=check.IsChecked==true;var vm=check.DataContext as OSDItemViewModel;Check(vm!=null,"Metric view model binding missing");
      var peer=new CheckBoxAutomationPeer(check);var toggle=(IToggleProvider)peer.GetPattern(PatternInterface.Toggle);toggle.Toggle();await Task.Delay(5);Check(check.IsChecked!=before&&vm.IsEnabled==check.IsChecked,"Native checkbox activation did not update its binding");toggle.Toggle();await Task.Delay(5);Check(check.IsChecked==before&&vm.IsEnabled==before,"Native checkbox test did not restore in-memory projection");activations+=2;
      var right=OnexPageNavigationTarget(rows,check,1);Check(right!=null&&(!ReferenceEquals(right,check)||index==0),"Row actions inaccessible to right");metricCount++;report["metricCheckboxes"]=JsonValue.CreateNumberValue(metricCount);report["productionDirectionalEdges"]=JsonValue.CreateNumberValue(directions);report["nativeCheckboxTogglePatternActivations"]=JsonValue.CreateNumberValue(activations);prior=check;
     }
     Check(isLoadingOSDConfig,"Read-only overlay handler guard changed during fixture");OSDItemsControl.IsEnabled=false;Check(!OnexPageNavigationRows().SelectMany(row=>row).Any(cell=>cell.Control.Name=="OsdItemCheckBox"),"Read-only standard metrics remain navigable");
    }
    finally{osdCustomizeLevel=level;RefreshOSDItemsControl();UpdateOnexOverlayPresetUi();isLoadingOSDConfig=loading;}
    report["toggleCases"]=toggleCases;report["savedHeaderCases"]=headerCases;report["metricCheckboxes"]=JsonValue.CreateNumberValue(metricCount);report["productionDirectionalEdges"]=JsonValue.CreateNumberValue(directions);report["nativeCheckboxTogglePatternActivations"]=JsonValue.CreateNumberValue(activations);report["checkboxWrapperExcluded"]=JsonValue.CreateBooleanValue(true);report["ownerSettingsWrites"]=JsonValue.CreateNumberValue(0);report["physicalOwnerGamepadTested"]=JsonValue.CreateBooleanValue(false);report["osKeyInjection"]=JsonValue.CreateBooleanValue(false);report["rasterPixelsCaptured"]=JsonValue.CreateBooleanValue(false);report["verificationKind"]=JsonValue.CreateStringValue("Actual UWP focus, local composition geometry, production directional graph and native TogglePattern; custom level projected under read-only handler guard, no physical gamepad input");
   }
   finally
   {
    if(_onexPerfOpen!=perfOpen)await SetOnexPerfCardAsync(perfOpen);
    ThemeExpandButton.IsChecked=appearance;if(_savedProfilesExpanded!=savedOpen){PerfSavedProfilesExpandButton.IsChecked=savedOpen;PerfSavedProfilesExpandButton_Click(PerfSavedProfilesExpandButton,null);}if(original!=null)original.IsChecked=true;
   }
  }
 }
}
