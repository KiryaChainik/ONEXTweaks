using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexStartupPickerReadbackAsync(JsonObject report)
  {
   var values=ApplicationData.Current.LocalSettings.Values;bool hadQa=values.TryGetValue(OnexTabsQaKey,out var originalQa),oldQa=_onexTabsQaStore;
   string owner=ReadOnexTabValue(OnexTabsKey),lastQa=null;var originalPage=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);bool expanded=_onexTabsHeader.IsChecked==true;
   var states=new JsonArray();int opened=0,closed=0;EventHandler<object> onOpened=(s,e)=>opened++,onClosed=(s,e)=>closed++;
   void Sample(string phase)
   {
    var c=_onexTabsOpen;var focus=FocusManager.GetFocusedElement() as FrameworkElement;
    var state=new JsonObject{{"phase",JsonValue.CreateStringValue(phase)},{"loaded",JsonValue.CreateBooleanValue(c.IsLoaded)},{"enabled",JsonValue.CreateBooleanValue(c.IsEnabled)},{"open",JsonValue.CreateBooleanValue(c.IsDropDownOpen)},{"width",JsonValue.CreateNumberValue(c.ActualWidth)},{"height",JsonValue.CreateNumberValue(c.ActualHeight)},{"opacity",JsonValue.CreateNumberValue(c.Opacity)},{"focusedName",JsonValue.CreateStringValue(focus?.Name??"")},{"focusedType",JsonValue.CreateStringValue(focus?.GetType().Name??"")},{"focusState",JsonValue.CreateStringValue(c.FocusState.ToString())},{"visibleHost",JsonValue.CreateBooleanValue(widget?.Visible==true)},{"inputRedirected",JsonValue.CreateBooleanValue(Windows.Gaming.UI.GameBar.IsInputRedirected)},{"appearanceOwnsRouter",JsonValue.CreateBooleanValue(OnexOwnsAppearanceChoice(c))},{"pageYieldsDown",JsonValue.CreateBooleanValue(OnexChoiceOwnsInput(Windows.System.VirtualKey.Down))},{"openedEvents",JsonValue.CreateNumberValue(opened)},{"closedEvents",JsonValue.CreateNumberValue(closed)},{"selectedIndex",JsonValue.CreateNumberValue(c.SelectedIndex)},{"qaOpen",JsonValue.CreateStringValue(LoadOnexTabs().Open)},{"ownerTabsUnchanged",JsonValue.CreateBooleanValue(ReadOnexTabValue(OnexTabsKey)==owner)}};
    var ancestors=new JsonArray();foreach(var e in new FrameworkElement[]{c,_onexTabsBody,_onexTabsCard,SystemScrollViewer})if(e!=null)ancestors.Add(new JsonObject{{"name",JsonValue.CreateStringValue(e.Name??"")},{"type",JsonValue.CreateStringValue(e.GetType().Name)},{"visibility",JsonValue.CreateStringValue(e.Visibility.ToString())},{"width",JsonValue.CreateNumberValue(e.ActualWidth)},{"height",JsonValue.CreateNumberValue(e.ActualHeight)}});state["ancestors"]=ancestors;states.Add(state);
   }
   report["startupReadbackStates"]=states;
   try
   {
    if(_onexDetailsDialog!=null||OnexSourceEditorsPending)throw new InvalidOperationException("Protect pending edits");
    _onexTabsQaStore=true;values[OnexTabsQaKey]=new OnexTabsPreference().Serialize();lastQa=ReadOnexTabValue(OnexTabsQaKey);
    EnsureOnexReleaseLayout();SystemNavItem.IsChecked=true;_onexTabsHeader.IsChecked=true;
    _onexTabsOpen.DropDownOpened+=onOpened;_onexTabsOpen.DropDownClosed+=onClosed;
    _onexTabsOpen.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});UpdateLayout();Sample("legacy-layout-ready");
    _onexTabsOpen.IsDropDownOpen=true;await Task.Delay(100);Sample("legacy-property-open-without-explicit-focus");
    _onexTabsOpen.IsDropDownOpen=false;await Task.Delay(100);
    report["focusSucceeded"]=JsonValue.CreateBooleanValue(_onexTabsOpen.Focus(FocusState.Keyboard));await Task.Delay(100);Sample("focused");
    _onexTabsOpen.IsDropDownOpen=true;await Task.Delay(100);Sample("focused-property-open");_onexTabsOpen.IsDropDownOpen=false;await Task.Delay(100);
    var peer=new Windows.UI.Xaml.Automation.Peers.ComboBoxAutomationPeer(_onexTabsOpen);var pattern=peer.GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.ExpandCollapse) as Windows.UI.Xaml.Automation.Provider.IExpandCollapseProvider;
    report["nativeExpandPatternAvailable"]=JsonValue.CreateBooleanValue(pattern!=null);
    if(pattern!=null){try{pattern.Expand();await Task.Delay(100);Sample("native-automation-expand");report["nativeExpandState"]=JsonValue.CreateStringValue(pattern.ExpandCollapseState.ToString());}catch(Exception error){report["nativeExpandErrorType"]=JsonValue.CreateStringValue(error.GetType().Name);Sample("native-automation-expand-error");}}
    if(_onexTabsOpen.IsDropDownOpen)
    {
     var item=_onexTabsOpen.Items.OfType<ComboBoxItem>().First(x=>x.Tag as string=="Display");var itemPeer=new Windows.UI.Xaml.Automation.Peers.ComboBoxItemAutomationPeer(item);var select=itemPeer.GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.SelectionItem) as Windows.UI.Xaml.Automation.Provider.ISelectionItemProvider;
     report["nativeSelectionPatternAvailable"]=JsonValue.CreateBooleanValue(select!=null);
     if(select!=null){select.Select();await Task.Delay(120);lastQa=ReadOnexTabValue(OnexTabsQaKey);Sample("native-selection-qa-display");}
    }
    if(ReadOnexTabValue(OnexTabsKey)!=owner)throw new InvalidOperationException("OwnerTabs changed during isolated readback");
    report["collectionComplete"]=JsonValue.CreateBooleanValue(true);report["ownerTabPreferenceWrites"]=JsonValue.CreateNumberValue(0);report["physicalOwnerGamepadTested"]=JsonValue.CreateBooleanValue(false);report["actualWindowsKeyInjectionSteps"]=JsonValue.CreateNumberValue(0);report["productStartupAcceptanceClaimed"]=JsonValue.CreateBooleanValue(false);
   }
   finally
   {
    _onexTabsOpen.IsDropDownOpen=false;_onexTabsOpen.DropDownOpened-=onOpened;_onexTabsOpen.DropDownClosed-=onClosed;
    if(ReadOnexTabValue(OnexTabsQaKey)==lastQa){if(hadQa)values[OnexTabsQaKey]=originalQa;else values.Remove(OnexTabsQaKey);}
    _onexTabsQaStore=oldQa;_onexTabsUiVersion=null;EnsureOnexReleaseLayout();_onexTabsHeader.IsChecked=expanded;if(originalPage?.Visibility==Visibility.Visible)originalPage.IsChecked=true;
    report["ownerTabPreferencePreservedInFinally"]=JsonValue.CreateBooleanValue(ReadOnexTabValue(OnexTabsKey)==owner);
   }
  }
  private async Task VerifyOnexStartupTabPickerAsync(JsonObject report,bool actualHost=false)
  {
   int checks=0,transitions=0,visibilityChanges=0;
   void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   var values=ApplicationData.Current.LocalSettings.Values;
   bool hadQa=values.TryGetValue(OnexTabsQaKey,out var originalQa);
   string ownerTabs=ReadOnexTabValue(OnexTabsKey),ownerOpen=LoadOnexTabs().Open,lastQa=null;
   bool oldQaStore=_onexTabsQaStore;var originalPage=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
   bool tabsExpanded=_onexTabsHeader.IsChecked==true;var appearance=ThemeContent.Visibility;
   var watches=new Dictionary<FrameworkElement,long>();
   try
   {
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending,"Protect pending owner edits");
    Check(ownerOpen==""||_onexFirstRevealTab==ownerOpen,"Cold start exposed a page before the configured tab");
    _onexTabsQaStore=true;values[OnexTabsQaKey]=new OnexTabsPreference().Serialize();lastQa=ReadOnexTabValue(OnexTabsQaKey);
    EnsureOnexReleaseLayout();SystemNavItem.IsChecked=true;_onexTabsHeader.IsChecked=true;
    _onexTabsOpen.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});UpdateLayout();
    var items=_onexTabsOpen.Items.Cast<object>().ToArray();var rows=_onexTabsRows.Children.ToArray();var navigation=MainNavPanel.Children.ToArray();
    async Task Choose(ComboBoxItem item)
    {
     // Diagnostic212 observed the legacy fixture opening before Loaded and
     // without native focus; production UIA/focused opening works normally.
     // Readiness is fixture setup, not a relaxation of navigation budgets.
     for(int n=0;n<120&&(!_onexTabsOpen.IsLoaded||_onexTabsOpen.ActualWidth<=0||_onexTabsOpen.ActualHeight<=0);n++)await Task.Delay(25);
     Check(_onexTabsOpen.IsLoaded&&_onexTabsOpen.ActualWidth>0&&_onexTabsOpen.ActualHeight>0,"Startup picker not loaded for native popup");
     Check(_onexTabsOpen.Focus(FocusState.Keyboard),"Startup picker did not receive native focus");
     await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,()=>{});
     _onexTabsOpen.IsDropDownOpen=true;await Task.Delay(80);
     Check(_onexTabsOpen.IsDropDownOpen,"Actual startup-tab popup opened");
     _onexTabsOpen.SelectedItem=item;lastQa=ReadOnexTabValue(OnexTabsQaKey);
     Check(LoadOnexTabs().Open==(item.Tag as string),"Startup choice did not persist");
     Check(SystemNavItem.IsChecked==true,"Choosing a future startup tab navigated the current page");
     Check(_onexTabsOpen.Items.Cast<object>().SequenceEqual(items),"Startup popup items were rebuilt during selection");
     Check(_onexTabsRows.Children.SequenceEqual(rows)&&MainNavPanel.Children.SequenceEqual(navigation),"Selection rebuilt the owner/navigation tree");
     _onexTabsOpen.IsDropDownOpen=false;await Task.Delay(100);transitions++;
     Check(_onexTabsOpen.Items.Cast<object>().SequenceEqual(items),"Startup items changed after popup close");
    }
    foreach(var item in items.OfType<ComboBoxItem>())if(!ReferenceEquals(item,_onexTabsOpen.SelectedItem))await Choose(item);
    await Choose((ComboBoxItem)items[0]);
    var preference=LoadOnexTabs();preference.Open="Display";values[OnexTabsQaKey]=preference.Serialize();lastQa=ReadOnexTabValue(OnexTabsQaKey);
    SystemNavItem.IsChecked=true;
    Check(ApplyDefaultTabOnOpen("own-hidden-preparation")&&DisplayNavItem.IsChecked==true&&DisplayScrollViewer.Visibility==Visibility.Visible,"Hidden preparation did not select the configured visible page");
    var pages=new FrameworkElement[]{_releaseHome,PerformanceScrollViewer,LegionScrollViewer,_onexKeyboardMousePage,DisplayScrollViewer,_releaseOsd,FanScrollViewer,_onexLightingPage,SystemScrollViewer}.Where(x=>x!=null).Distinct().ToArray();
    foreach(var page in pages)watches[page]=page.RegisterPropertyChangedCallback(UIElement.VisibilityProperty,(s,p)=>visibilityChanges++);
    Check(ApplyDefaultTabOnOpen("own-foreground-after-preparation")&&ApplyDefaultTabOnOpen("own-visible-after-preparation"),"Prepared tab was not retained");
    Check(visibilityChanges==0,"Repeated open signals collapsed/showed an already prepared page");
    foreach(var pair in watches)pair.Key.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty,pair.Value);watches.Clear();
    int restoredVisibilityChanges=0;
    if(actualHost)
    {
     Check(widget!=null&&widget.Visible,"Actual Game Bar widget must be visible before SDK lifecycle check");
     SystemNavItem.IsChecked=true;
     // Official own-widget methods; this is a real host hide/restore,
     // with the isolated QA tab store retained across lifecycle callbacks.
     await widget.MinimizeAsync();
     for(int n=0;n<100&&widget.Visible;n++)await Task.Delay(20);
     Check(!widget.Visible,"Game Bar did not minimize the actual own widget");
     for(int n=0;n<100&&DisplayNavItem.IsChecked!=true;n++)await Task.Delay(20);
     Check(DisplayNavItem.IsChecked==true&&DisplayScrollViewer.Visibility==Visibility.Visible,"Configured page was not prepared while actually hidden");
     foreach(var page in pages)watches[page]=page.RegisterPropertyChangedCallback(UIElement.VisibilityProperty,(s,p)=>restoredVisibilityChanges++);
     await widget.RestoreAsync();
     for(int n=0;n<100&&!widget.Visible;n++)await Task.Delay(20);
     Check(widget.Visible&&DisplayNavItem.IsChecked==true,"Real restored widget exposed the wrong selected page");
     await Task.Delay(200);
     Check(restoredVisibilityChanges==0,"Actual restore collapsed/showed a prepared page");
     foreach(var pair in watches)pair.Key.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty,pair.Value);watches.Clear();
    }
    preference=LoadOnexTabs();preference.Open="";values[OnexTabsQaKey]=preference.Serialize();lastQa=ReadOnexTabValue(OnexTabsQaKey);
    SystemNavItem.IsChecked=true;Check(!ApplyDefaultTabOnOpen("own-last-used")&&SystemNavItem.IsChecked==true,"Last used tab mode navigated unexpectedly");
    ThemeContent.Visibility=Visibility.Visible;EnsureOnexLanguageSettings();ApplyOnexSettingsDesign();UpdateLayout();
    var titles=OnexOwnedContent(ThemeContent).OfType<TextBlock>().Where(t=>new[]{"OnexThemeSettingTitle","OnexAppFontSettingTitle","OnexGlassSettingTitle","OnexLanguageSettingTitle"}.Contains(t.Name)).ToArray();
    Check(titles.Length==4&&titles.All(t=>t.FontSize==14&&t.FontWeight.Weight==600&&t.TextWrapping==TextWrapping.Wrap),"Appearance title hierarchy is inconsistent");
    Check(ThemeComboBox.Width==280&&AppFontComboBox.Width==280&&_onexLanguage.Width==280,"Appearance control columns differ");
    Check(_onexLanguage.Header==null&&_onexLanguage.FontSize==ThemeComboBox.FontSize&&ReferenceEquals(_onexLanguage.Style,ThemeComboBox.Style),"Language picker differs from the shared appearance controls");
    Check(_onexApplyLanguage.Content is TextBlock actionText&&actionText.TextWrapping==TextWrapping.Wrap,"Localized apply label cannot wrap");
    OnexAppearanceCard.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);
    await SaveOnexImage("onex-settings431-"+OnexStrings.Language+"-appearance.png",OnexAppearanceCard,50);
    Check(ReadOnexTabValue(OnexTabsKey)==ownerTabs,"Owner tab preference changed during isolated QA");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["language"]=JsonValue.CreateStringValue(OnexStrings.Language);
    report["actualStartupPopupTransitions"]=JsonValue.CreateNumberValue(transitions);
    report["coldStartBeforeCoverTab"]=JsonValue.CreateStringValue(_onexFirstRevealTab??"");
    report["repeatedOpenPageVisibilityChanges"]=JsonValue.CreateNumberValue(visibilityChanges);
    report["hiddenPreparationInvokedProgrammatically"]=JsonValue.CreateBooleanValue(true);
    report["realGameBarHideReopenTested"]=JsonValue.CreateBooleanValue(actualHost);
    report["actualRestorePageVisibilityChanges"]=JsonValue.CreateNumberValue(restoredVisibilityChanges);
    report["appearanceSharedHeadingCount"]=JsonValue.CreateNumberValue(titles.Length);
    report["ownerTabPreferenceWrites"]=JsonValue.CreateNumberValue(0);
    report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally
   {
    _onexTabsOpen.IsDropDownOpen=false;
    if(actualHost&&widget!=null&&!widget.Visible){try{await widget.RestoreAsync();}catch(Exception error){report["hostRestoreErrorType"]=JsonValue.CreateStringValue(error.GetType().Name);}}
    foreach(var pair in watches)pair.Key.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty,pair.Value);
    if(ReadOnexTabValue(OnexTabsQaKey)==lastQa){if(hadQa)values[OnexTabsQaKey]=originalQa;else values.Remove(OnexTabsQaKey);}
    _onexTabsQaStore=oldQaStore;_onexTabsUiVersion=null;EnsureOnexReleaseLayout();
    _onexTabsHeader.IsChecked=tabsExpanded;ThemeContent.Visibility=appearance;
    if(originalPage!=null&&originalPage.Visibility==Visibility.Visible)originalPage.IsChecked=true;
   }
  }
 }
}
