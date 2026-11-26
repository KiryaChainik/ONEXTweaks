// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexControllerSavedListAsync(JsonObject report)
  {
   void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);bool expanded=false;int sort=0;
   try
   {
    LegionNavItem.IsChecked=true;await ReadOnexControllerAsync();var section=_onexControllerSavedProfiles;Check(section!=null,"Controller Saved Profiles missing");expanded=section.Body.Visibility==Visibility.Visible;sort=section.Sort.SelectedIndex;section.Body.Visibility=Visibility.Visible;section.Apply(true);
    Check(LegionScrollViewer.Content is Panel body&&ReferenceEquals(body.Children.Last(),section.Card),"Controller Saved Profiles must be at bottom, not a top tile");
    Check(section.Ids.Count==_onexControllerState.GetNamedArray("entries").Count&&section.Ids.First()=="global","Controller list omits profiles/global");
    foreach(var row in section.Rows.Values){Check(row.Choose.IsEnabled,"Controller profile selection is disabled");Check(row.Name.Foreground is SolidColorBrush brush&&brush.Color==(_onexAppliedTheme?.TextPrimary??Windows.UI.Colors.White),"Controller profile title is muted");Check(row.Details.Text.Contains("remaps")&&row.Details.Text.Contains("Deadzone L / R"),"Controller profile summary is incomplete");Check(row.Rename.IsEnabled==row.Editable&&row.Remove.IsEnabled==row.Editable,"Controller name/delete protection differs from other pages");}
    var expected=section.Ids.OrderBy(id=>id).ToArray();for(int i=0;i<3;i++){section.Sort.SelectedIndex=i;Check(section.Ids.OrderBy(id=>id).SequenceEqual(expected),"Controller profile sort loses rows");}
    await Task.Delay(100);UpdateLayout();Check(OnexNavigableContent(LegionScrollViewer.Content as DependencyObject).Contains(section.Sort),"Controller Saved sort absent from gamepad graph");section.Header.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await SaveOnexImage("onex-controller-saved272.png",section.Card,150);
    report["controllerSavedProfilesAtBottom"]=JsonValue.CreateBooleanValue(true);report["threeSortsSummariesAndProtectedActions"]=JsonValue.CreateBooleanValue(true);report["actualProfileSelectionWrites"]=JsonValue.CreateNumberValue(0);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally{if(_onexControllerSavedProfiles!=null){_onexControllerSavedProfiles.Sort.SelectedIndex=sort;_onexControllerSavedProfiles.Body.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;}if(original!=null)original.IsChecked=true;}
  }
  private async Task VerifyOnexProfileListVisualsAsync(JsonObject report)
  {
   void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
   async Task Wait(Func<bool> ready){for(int n=0;n<150;n++){if(ready())return;await Task.Delay(60);}throw new TimeoutException("Profile list visual readiness timeout");}
   bool Primary(TextBlock text)=>text.Foreground is SolidColorBrush brush&&brush.Color==(_onexAppliedTheme?.TextPrimary??Windows.UI.Colors.White)&&text.Opacity==1;
   bool Secondary(TextBlock text)=>text.Foreground is SolidColorBrush brush&&brush.Color==Windows.UI.Color.FromArgb(255,194,201,211)&&text.Opacity==1;
   int choices=0,protectedActions=0,selected=0;
   void Inspect(OnexSavedCatalogSection section,bool picker)
   {
    Check(section.Rows.Count>0,"Empty actual catalog");
    foreach(var row in section.Rows.Values)
    {
     Check(row.Choose.IsEnabled==(!picker||!row.Active),"Selection availability confused with edit protection");
     if(row.Choose.IsEnabled){Check(Primary(row.Name)&&Secondary(row.Details),"Available profile is visually muted");choices++;}
     else{Check(picker&&row.Active&&row.Kind.Text.Contains("✓ Active"),"Disabled picker choice lacks active marker");selected++;}
     if(!picker&&!row.Editable){Check(!row.Rename.IsEnabled&&!row.Remove.IsEnabled,"Protected actions became available");protectedActions++;}
     if(picker)Check(row.Rename.Visibility==Visibility.Collapsed&&row.Remove.Visibility==Visibility.Collapsed,"Picker exposes editing actions");
    }
   }
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);var sections=new List<Tuple<OnexSavedCatalogSection,Visibility>>();
   try
   {
    _releasePowerNav.IsChecked=true;await ReadOnexPerformanceProfilesAsync();RenderOnexSavedProfiles();
    var perfChoices=OnexOwnedContent(AllGameProfilesContainer).OfType<Button>().Where(button=>button.Name.StartsWith("OnexSavedChoose_",StringComparison.Ordinal)).ToArray();
    Check(perfChoices.Length==OnexProfiles.Count(),"Performance Saved Profiles incomplete");
    foreach(var button in perfChoices){var labels=((StackPanel)button.Content).Children.OfType<TextBlock>().ToArray();Check(button.IsEnabled&&Primary(labels[0]),"Performance choice looks disabled");choices++;}
    var protectedPerf=OnexOwnedContent(AllGameProfilesContainer).OfType<Button>().Where(button=>button.Name=="OnexSavedRename_global"||button.Name=="OnexSavedDelete_global").ToArray();Check(protectedPerf.Length==2&&protectedPerf.All(button=>!button.IsEnabled),"Performance Global protection changed");protectedActions++;
    FanNavItem.IsChecked=true;await ReadOnexFanProfilesAsync();_onexFanSavedProfiles.Apply(true);Inspect(_onexFanSavedProfiles,false);sections.Add(Tuple.Create(_onexFanSavedProfiles,_onexFanSavedProfiles.Body.Visibility));_onexFanSavedProfiles.Body.Visibility=Visibility.Visible;_onexFanSavedProfiles.Header.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await SaveOnexImage("onex-fan-saved270.png",_onexFanSavedProfiles.Card,150);
    DisplayNavItem.IsChecked=true;await ReadOnexScreenProfilesAsync();_onexScreenSavedProfiles.Apply(true);Inspect(_onexScreenSavedProfiles,false);
    _releaseOsdNav.IsChecked=true;await OpenOnexOverlaySettingsAsync();await ReadOnexOverlayPresetsAsync();await Wait(()=>!_onexOverlayOpening&&!isLoadingOSDConfig&&_onexOverlayConfigReady);_onexOverlaySavedPresets.Apply(true);Inspect(_onexOverlaySavedPresets,false);sections.Add(Tuple.Create(_onexOverlaySavedPresets,_onexOverlaySavedPresets.Body.Visibility));_onexOverlaySavedPresets.Body.Visibility=Visibility.Visible;_onexOverlaySavedPresets.Header.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await SaveOnexImage("onex-overlay-saved270.png",_onexOverlaySavedPresets.Card,150);
    foreach(var catalog in new[]{OnexPerformanceTileCatalog(),OnexFanTileCatalog(),OnexScreenTileCatalog()})
    {
     QuickNavItem.IsChecked=true;await catalog.Read();var task=ShowOnexHomeProfileDialogAsync(null,catalog);await Wait(()=>_onexDetailsDialog!=null&&_onexProfileTileSelection!=null);var section=_onexProfileTileSelection;
     for(int sort=0;sort<3;sort++){section.Sort.SelectedIndex=sort;Inspect(section,true);}
     Check(section.Rows.Values.Count(row=>row.Active)==1,"Picker current selection is ambiguous");
     if(catalog.Scope=="Fan")await SaveOnexImage("onex-fan-picker270.png",section.Card,150);
     _onexDetailsDialog.Hide();await task;
    }
    report["actualSavedListsInspected"]=JsonValue.CreateNumberValue(4);report["actualDetailedPickersInspected"]=JsonValue.CreateNumberValue(3);report["availableChoicesChecked"]=JsonValue.CreateNumberValue(choices);report["protectedPageGroupsChecked"]=JsonValue.CreateNumberValue(protectedActions);report["selectedPickerRowsChecked"]=JsonValue.CreateNumberValue(selected);report["actualProfileSelectionWrites"]=JsonValue.CreateNumberValue(0);report["directSoftwareDialogOpen"]=JsonValue.CreateBooleanValue(true);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally{_onexDetailsDialog?.Hide();for(int n=0;n<50&&_onexProfileTileOpening;n++)await Task.Delay(20);foreach(var pair in sections)pair.Item1.Body.Visibility=pair.Item2;if(original!=null)original.IsChecked=true;}
  }
 }
}
