// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexCleanNavigationAsync(JsonObject report)
  {
   void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);string[] expected={"Performance","OnexPower","Legion","KeyboardMouse","Display","OnexOsd","Fan","Lighting","System"};
   try
   {
    EnsureOnexReleaseLayout();var navs=GetVisibleNavigationItems();Check(navs.Count==expected.Length&&navs.Select(nav=>(string)nav.Tag).SequenceEqual(expected),"Visible navigation still contains empty profile shortcut");Check(ProfilesNavItem.Parent==null&&ProfilesNavItem.Visibility==Visibility.Collapsed&&!ProfilesNavItem.IsTabStop,"Hidden shortcut can still receive focus");Check(_onexProfilesShortcut==null&&_onexPerformanceProfilePickerCard!=null&&ContainsOnexProfileElement(PerformanceScrollViewer,_onexPerformanceProfilePickerCard),"Performance editor is missing or shortcut UI is still created");
    PerformanceNavItem.IsChecked=true;int transitions=0;
    for(int i=1;i<=7;i++){NavigateToNextTab();await Task.Delay(80);Check(navs[i%7].IsChecked==true,"Next-tab route follows stale eight-tab order");transitions++;}
    for(int i=1;i<=7;i++){NavigateToPreviousTab();await Task.Delay(80);Check(navs[(7-i)%7].IsChecked==true,"Previous-tab route follows stale eight-tab order");transitions++;}
    foreach(var nav in navs){nav.IsChecked=true;await Task.Delay(60);Check(GetActiveScrollViewer()?.Visibility==Visibility.Visible,"Visible page is missing after shortcut removal");Check(!nav.IsTabStop,"Top tab returned to D-pad content focus");}
    _releasePowerNav.IsChecked=true;await ReadOnexPerformanceProfilesAsync();Check(_onexPerformanceProfilePicker.Choose.IsEnabled&&_onexPerformanceProfilePicker.Create.IsEnabled&&PerfSavedProfilesExpandButton!=null,"Actual Performance profile controls are unavailable");await SaveOnexImage("onex-navigation267.png",MainNavPanel,100);
    report["sevenUsefulTabsInOwnerOrder"]=JsonValue.CreateBooleanValue(true);report["emptyShortcutRemoved"]=JsonValue.CreateBooleanValue(true);report["softwareNextPreviousTransitions"]=JsonValue.CreateNumberValue(transitions);report["performanceEditorAndSavedProfilesRetained"]=JsonValue.CreateBooleanValue(true);report["physicalTriggerInputTested"]=JsonValue.CreateBooleanValue(false);report["hardwareSettingsWritesByFixture"]=JsonValue.CreateNumberValue(0);
   }
   finally{if(original!=null)original.IsChecked=true;}
  }
 }
}
