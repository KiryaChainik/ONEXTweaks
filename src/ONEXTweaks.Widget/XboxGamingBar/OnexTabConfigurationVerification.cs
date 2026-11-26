// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private static void InvokeOnexTabButton(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
  private async Task VerifyOnexTabConfigurationAsync(JsonObject report,bool restart)
  {
   var values=ApplicationData.Current.LocalSettings.Values;string owner=ReadOnexTabValue(OnexTabsKey);var tab=GetVisibleNavigationItems().FirstOrDefault(item=>item.IsChecked==true);int checks=0;
   void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   var folder=ApplicationData.Current.LocalFolder;
   try
   {
    _onexTabsQaStore=true;EnsureOnexReleaseLayout();SystemNavItem.IsChecked=true;_onexTabsHeader.IsChecked=true;UpdateLayout();await Task.Delay(120);
    if(!restart)
    {
     Check(await folder.TryGetItemAsync("onex-tabs353-restart.json")==null,"Protect existing unfinished tab QA operation");
     values[OnexTabsQaKey]="{\"order\":[\"System\",\"Legion\",\"Legion\",\"Unknown\",\"Performance\"],\"hidden\":[\"Performance\",\"System\",\"Legion\",\"Unknown\"],\"open\":\"Legion\"}";
     var normalized=LoadOnexTabs();Check(normalized.Order.Count==OnexTabIds.Length&&normalized.Order.Distinct().Count()==OnexTabIds.Length,"Duplicate/missing tab normalization failed");Check(normalized.Order.First()=="Performance"&&normalized.Order.Last()=="System","Required endpoints were reordered");Check(normalized.Hidden.SetEquals(new[]{"Legion"})&&normalized.Open=="","Required/unknown hidden tabs or hidden default accepted");
     EnsureOnexReleaseLayout();Check(GetVisibleNavigationItems().Count==OnexTabIds.Length-1,"Hidden tab still in navigation");
     values[OnexTabsQaKey]="invalid json";EnsureOnexReleaseLayout();Check(GetVisibleNavigationItems().Count==OnexTabIds.Length,"Malformed preference did not fall back safely");
     var minimal=new OnexTabsPreference();foreach(string id in OnexTabIds)minimal.Hidden.Add(id);values[OnexTabsQaKey]=minimal.Serialize();EnsureOnexReleaseLayout();Check(GetVisibleNavigationItems().Select(item=>item.Tag as string).SequenceEqual(new[]{"Performance","System"}),"Hiding all optional tabs lost reachable Home/Settings");
     InvokeOnexTabButton(_onexTabsRestore);await Task.Delay(80);Check(GetVisibleNavigationItems().Select(item=>item.Tag as string).SequenceEqual(OnexTabIds),"Restore Default did not restore actual navigation");Check(LoadOnexTabs().Open=="","Default-open option survived reset");
     Check(!_onexTabRows["Performance"].Visible.IsEnabled&&!_onexTabRows["System"].Visible.IsEnabled,"Required tabs can be hidden");Check(!_onexTabRows["Performance"].Up.IsEnabled&&!_onexTabRows["System"].Down.IsEnabled,"Required endpoint move buttons active");
     var box=_onexTabRows["Legion"].Visible;((IToggleProvider)new CheckBoxAutomationPeer(box).GetPattern(PatternInterface.Toggle)).Toggle();await Task.Delay(80);
     Check(LegionNavItem.Visibility==Visibility.Collapsed&&GetVisibleNavigationItems().Count==OnexTabIds.Length-1,"Checkbox did not hide Controller");Check(!_onexTabsOpen.Items.OfType<ComboBoxItem>().Any(item=>item.Tag as string=="Legion"),"Hidden tab remains in open picker");
     ((IToggleProvider)new CheckBoxAutomationPeer(_onexTabRows["Fan"].Visible).GetPattern(PatternInterface.Toggle)).Toggle();InitializeMsiFanCard();Check(FanNavItem.Visibility==Visibility.Collapsed,"Hardware callback surfaced a hidden Fan tab");
     InvokeOnexTabButton(_onexTabRows["Fan"].Up);await Task.Delay(80);var configured=LoadOnexTabs();Check(configured.Order.IndexOf("Fan")<configured.Order.IndexOf("OnexOsd"),"Move up did not reorder the actual tabs");Check(MainNavPanel.Children.OfType<RadioButton>().Select(item=>item.Tag as string).SequenceEqual(configured.Order),"Adapter overwrote saved order");
     _onexTabsOpen.SelectedItem=_onexTabsOpen.Items.OfType<ComboBoxItem>().Single(item=>item.Tag as string=="Display");Check(LoadOnexTabs().Open=="Display","On-open selection not persisted");
     PerformanceNavItem.IsChecked=true;var visible=GetVisibleNavigationItems().ToArray();for(int n=1;n<=visible.Length;n++){Check(HandleOnexTriggerPress(VirtualKey.GamepadRightTrigger,false),"RT routing refused");await Task.Delay(25);Check(ReferenceEquals(visible[n%visible.Length],GetVisibleNavigationItems().Single(item=>item.IsChecked==true)),"RT did not follow visible configured order");}
     Check(HandleOnexTriggerPress(VirtualKey.GamepadLeftTrigger,false),"LT routing refused");Check(SystemNavItem.IsChecked==true,"LT did not wrap to reachable Settings");
     UpdateLayout();var focus=_onexTabRows["Fan"].Up;Check(focus.Focus(FocusState.Keyboard),"Move button cannot receive focus");var before=MainNavPanel.Children.ToArray();for(int n=0;n<3;n++)EnsureOnexReleaseLayout();Check(before.SequenceEqual(MainNavPanel.Children)&&ReferenceEquals(FocusManager.GetFocusedElement(),focus),"Routine layout refresh discarded order or focus");
     var rows=OnexPageNavigationRows();Check(rows.SelectMany(row=>row).Any(cell=>ReferenceEquals(cell.Control,focus)),"Tab settings controls omitted from gamepad content navigation");Check(!rows.SelectMany(row=>row).Any(cell=>MainNavPanel.Children.Contains(cell.Control)),"Top tabs leaked into D-pad navigation");
     _onexTabsCard.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);await SaveOnexImage("onex-tabs353-expanded.png",_onexTabsCard,50);
     var expected=new JsonObject{{"value",JsonValue.CreateStringValue((string)values[OnexTabsQaKey])},{"ownerValue",owner==null?JsonValue.CreateNullValue():JsonValue.CreateStringValue(owner)}};await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-tabs353-restart.json",CreationCollisionOption.FailIfExists),expected.Stringify());
     report["restartProofPrepared"]=JsonValue.CreateBooleanValue(true);
    }
    else
    {
     var expected=JsonObject.Parse(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-tabs353-restart.json")));string raw=values[OnexTabsQaKey] as string;Check(raw==expected.GetNamedString("value"),"Isolated tab preferences did not survive UWP restart");
     var previous=expected.GetNamedValue("ownerValue");Check(owner==(previous.ValueType==JsonValueType.String?previous.GetString():null),"Owner tab preferences changed during isolated QA; fresh value retained");
     var preferences=LoadOnexTabs();Check(preferences.Hidden.SetEquals(new[]{"Legion","Fan"})&&preferences.Open=="Display","Restart lost visibility/default choice");Check(preferences.Order.IndexOf("Fan")<preferences.Order.IndexOf("OnexOsd"),"Restart lost reordered tab");Check(GetVisibleNavigationItems().Select(item=>item.Tag as string).SequenceEqual(preferences.Order.Where(id=>!preferences.Hidden.Contains(id))),"Restarted adapter did not apply saved visible order");
     Check(ApplyDefaultTabOnOpen("own-tab-qa-reopen")&&DisplayNavItem.IsChecked==true,"On-open route did not select the visible saved tab");
     // Selecting a hidden page through an explicit Go to Settings action reveals it.
     LegionNavItem.IsChecked=true;Check(LegionNavItem.Visibility==Visibility.Visible&&!LoadOnexTabs().Hidden.Contains("Legion"),"Explicit hidden-page request cannot reveal its navigation entry");
     SystemNavItem.IsChecked=true;InvokeOnexTabButton(_onexTabsRestore);await Task.Delay(80);Check(GetVisibleNavigationItems().Count==OnexTabIds.Length&&LoadOnexTabs().Open=="","Reset after restart did not restore all tabs");
     values.Remove(OnexTabsQaKey);await(await folder.GetFileAsync("onex-tabs353-restart.json")).DeleteAsync();report["actualUwpRestartPersistence"]=JsonValue.CreateBooleanValue(true);report["ownQaStoreRemoved"]=JsonValue.CreateBooleanValue(true);
    }
    Check(ReadOnexTabValue(OnexTabsKey)==owner,"Owner tab preferences changed during isolated test");report["checks"]=JsonValue.CreateNumberValue(checks);report["isolatedRealLocalSettings"]=JsonValue.CreateBooleanValue(true);report["ownerTabPreferencesWrites"]=JsonValue.CreateNumberValue(0);report["injectedControlAndTriggerRouting"]=JsonValue.CreateBooleanValue(true);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally{_onexTabsQaStore=false;EnsureOnexReleaseLayout();_onexTabsHeader.IsChecked=false;if(tab!=null&&tab.Visibility==Visibility.Visible)tab.IsChecked=true;}
  }
 }
}
