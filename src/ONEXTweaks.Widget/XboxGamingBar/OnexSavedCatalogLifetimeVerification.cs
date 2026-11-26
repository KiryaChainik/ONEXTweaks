using System;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Input;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexSavedCatalogLifetimeAsync(JsonObject report)
  {
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   async Task Wait(Func<bool> ready,string why){for(int n=0;n<160;n++){if(ready())return;await Task.Delay(25);}throw new TimeoutException(why);}
   await ReadOnexControllerAsync();await ReadOnexScreenProfilesAsync();await ReadOnexFanProfilesAsync();await ReadOnexOverlayPresetsAsync();
   var original=GetVisibleNavigationItems().FirstOrDefault(tab=>tab.IsChecked==true);var rows=new JsonArray();
   var cases=new[]{new{Scope="Controller",Nav=LegionNavItem,Section=_onexControllerSavedProfiles},new{Scope="Screen",Nav=DisplayNavItem,Section=_onexScreenSavedProfiles},new{Scope="Fan",Nav=FanNavItem,Section=_onexFanSavedProfiles},new{Scope="Overlay",Nav=_releaseOsdNav,Section=_onexOverlaySavedPresets}};
   try
   {
    foreach(var item in cases)
    {
     var section=item.Section;Check(section!=null&&section.Body.Visibility==Visibility.Collapsed&&section.Rows.Count==0&&section.List.Children.Count==0,"Collapsed saved catalog eagerly built row controls");
     int sort=section.Sort.SelectedIndex;double buildMax=0;item.Nav.IsChecked=true;await Wait(()=>section.Header.IsLoaded,"Saved catalog header not loaded");
     try
     {
      for(int pass=0;pass<2;pass++)
      {
       var watch=Stopwatch.StartNew();section.SetExpanded(true);buildMax=Math.Max(buildMax,watch.Elapsed.TotalMilliseconds);await Task.Delay(45);
       Check(section.Rows.Count==section.EntryCount&&section.Ids.Count==section.EntryCount&&section.Ids.Distinct().Count()==section.EntryCount,"Expanded saved catalog incomplete/duplicated");
       for(int order=0;order<3;order++){section.Sort.SelectedIndex=order;section.Apply(true);Check(section.Rows.Count==section.EntryCount&&section.Ids.Count==section.EntryCount,"Sorting lost saved catalog entries");}
       Check(section.Rows.Values.All(row=>row.Choose!=null&&row.Rename!=null&&row.Remove!=null&&row.Details!=null),"Saved catalog row actions/details missing");
       section.Header.Focus(FocusState.Keyboard);section.SetExpanded(false);Check(section.Rows.Count==0&&section.Ids.Count==0&&section.List.Children.Count==0,"Collapsed saved catalog retained row controls");
      }
      report["savedCatalogCurrentScope"]=JsonValue.CreateStringValue(item.Scope);report["savedCatalogCurrentBuildMaxMs"]=JsonValue.CreateNumberValue(buildMax);
      Check(buildMax<150,"Saved catalog first-use construction exceeded150ms");
      rows.Add(new JsonObject{{"scope",JsonValue.CreateStringValue(item.Scope)},{"entryCount",JsonValue.CreateNumberValue(section.EntryCount)},{"creationMaxMs",JsonValue.CreateNumberValue(buildMax)},{"twoExpansionsAndThreeSortModes",JsonValue.CreateBooleanValue(true)}});
     }
     finally{section.SetExpanded(false);section.Sort.SelectedIndex=sort;section.Apply();}
    }
    report["savedCatalogLifetimeChecks"]=JsonValue.CreateNumberValue(checks);report["savedCatalogLifetimeCases"]=rows;report["savedCatalogOwnerActionsInvoked"]=JsonValue.CreateBooleanValue(false);
   }
   finally{if(original!=null)original.IsChecked=true;}
  }
 }
}
