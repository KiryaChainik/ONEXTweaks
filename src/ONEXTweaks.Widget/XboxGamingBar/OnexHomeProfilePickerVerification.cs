// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexHomePerformancePickerAsync(JsonObject report)
  {
   void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
   async Task Wait(Func<bool> ready,string text){for(int n=0;n<120;n++){if(ready())return;await Task.Delay(60);}throw new TimeoutException(text);}
   void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);Button anchor=null;
   try
   {
    QuickNavItem.IsChecked=true;await ReadOnexPerformanceProfilesAsync();ApplyOnexTileAvailability();Check(qsTileMap.TryGetValue("Profile",out var tile)&&tile.TileButton!=null,"Owner layout has no visible Perf Profile tile");anchor=tile.TileButton;Check(anchor.IsEnabled&&OnexHasTileDetails("Profile"),"Perf Profile is unavailable without running game or has no held action");
    Invoke(anchor);await Wait(()=>_onexProfileTileMenu?.IsOpen==true,"Tap did not open compact Performance list");var choices=_onexProfileTileMenu.Items.OfType<MenuFlyoutItem>().ToArray();Check(choices.Length==OnexProfiles.Count()&&choices[0].Tag as string=="global"&&choices.All(choice=>choice.Text.Contains(" W · Boost ")&&choice.Text.Contains("FPS ")),"Compact list omits profiles or short details");Check(choices.Count(choice=>choice.Icon!=null)==1,"Compact selection marker is inconsistent");_onexProfileTileMenu.Hide();await Task.Delay(80);
    anchor.Focus(FocusState.Keyboard);Check(OpenFocusedOnexTileDetails(),"Focused gamepad X path does not open Perf Profile details");await Wait(()=>_onexDetailsDialog!=null&&_onexProfileTileSelection!=null,"Held/X detailed selector did not open");var dialog=_onexDetailsDialog;var section=_onexProfileTileSelection;await Task.Delay(100);UpdateLayout();
    Check(section.Ids.Count==OnexProfiles.Count()&&section.Ids[0]=="global"&&section.Rows.Values.All(row=>row.Rename.Visibility==Visibility.Collapsed&&row.Remove.Visibility==Visibility.Collapsed),"Detailed selector omits profiles or permits editing/deletion");Check(dialog.PrimaryButtonText=="Go to Performance Profile Settings"&&dialog.CloseButtonText=="Close","Detailed selector navigation footer is missing");
    for(int index=0;index<3;index++){section.Sort.SelectedIndex=index;Check(section.Ids[0]=="global"&&section.Ids.Count==OnexProfiles.Count(),"Detailed selector sort loses profiles");}
    await SaveOnexImage("onex-perf-tile259.png",section.Card,120);Button primary=null;
    for(int attempt=0;attempt<10&&primary==null;attempt++){await Task.Delay(60);dialog.ApplyTemplate();primary=FindDescendantByName(dialog,"PrimaryButton") as Button;if(primary==null)foreach(var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)){primary=FindDescendantByName(popup.Child,"PrimaryButton") as Button;if(primary!=null)break;}}
    Check(primary!=null,"Own Go-to-settings button is not ready");Invoke(primary);await Wait(()=>_onexDetailsDialog==null&&_releasePowerNav.IsChecked==true,"Go-to-settings did not open Performance");
    report["actualTapOpensAllProfilesAndShortDetails"]=JsonValue.CreateBooleanValue(true);report["gamepadXHandlerOpensReadOnlyDetailedSelector"]=JsonValue.CreateBooleanValue(true);report["threeSortsAndGoToPerformanceSettings"]=JsonValue.CreateBooleanValue(true);report["profileSelectionsWrittenByFixture"]=JsonValue.CreateNumberValue(0);report["backendSelectionAlreadySharedWithAcceptedPerformancePicker"]=JsonValue.CreateBooleanValue(true);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally{CloseOnexMenus();_onexDetailsDialog?.Hide();for(int n=0;n<20&&_onexProfileTileOpening;n++)await Task.Delay(20);if(original!=null)original.IsChecked=true;}
  }
  private async Task VerifyOnexHomeFanPickerAsync(JsonObject report)
  {
   var perf=new JsonObject();await VerifyOnexHomePerformancePickerAsync(perf);report["performancePickerRegression"]=perf;
   void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
   async Task Wait(Func<bool> ready,string text){for(int n=0;n<120;n++){if(ready())return;await Task.Delay(60);}throw new TimeoutException(text);}
   void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);
   try
   {
    QuickNavItem.IsChecked=true;await ReadOnexFanProfilesAsync();ApplyOnexTileAvailability();Check(qsTileMap.TryGetValue("OnexFanMode",out var tile)&&tile.TileButton!=null&&tile.TileButton.IsEnabled,"Fan tile is missing/unavailable");var anchor=tile.TileButton;Invoke(anchor);await Wait(()=>_onexProfileTileMenu?.IsOpen==true,"Fan tap did not open compact selector");var choices=_onexProfileTileMenu.Items.OfType<MenuFlyoutItem>().ToArray();Check(choices.Length==_onexFanProfiles.GetNamedArray("entries").Count&&choices.Take(3).All(item=>((string)item.Tag).StartsWith("firmware-",StringComparison.Ordinal)),"Fan compact selector omits profiles or does not pin system curves");Check(choices.All(item=>item.Text.Contains(" · "))&&choices.Count(item=>item.Icon!=null)==1,"Fan compact summaries/selection marker missing");_onexProfileTileMenu.Hide();await Task.Delay(60);anchor.Focus(FocusState.Keyboard);Check(OpenFocusedOnexTileDetails(),"Fan gamepad X path missing");await Wait(()=>_onexDetailsDialog!=null&&_onexProfileTileSelection!=null,"Fan detailed selector did not open");var section=_onexProfileTileSelection;var dialog=_onexDetailsDialog;Check(section.Ids.Count==choices.Length&&section.Rows.Values.All(row=>row.Rename.Visibility==Visibility.Collapsed&&row.Remove.Visibility==Visibility.Collapsed),"Fan detailed list incomplete or editable");Check(dialog.PrimaryButtonText=="Go to Fan Profile Settings","Fan settings footer missing");for(int i=0;i<3;i++){section.Sort.SelectedIndex=i;Check(section.Ids.Count==choices.Length,"Fan sort loses profiles");}await SaveOnexImage("onex-fan-tile263.png",section.Card,100);
    Button primary=null;for(int attempt=0;attempt<10&&primary==null;attempt++){await Task.Delay(60);dialog.ApplyTemplate();primary=FindDescendantByName(dialog,"PrimaryButton") as Button;if(primary==null)foreach(var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)){primary=FindDescendantByName(popup.Child,"PrimaryButton") as Button;if(primary!=null)break;}}Check(primary!=null,"Fan Go-to-settings native button missing");Invoke(primary);await Wait(()=>_onexDetailsDialog==null&&FanNavItem.IsChecked==true,"Fan Go-to-settings failed");
    report["fanTapCompleteCompactList"]=JsonValue.CreateBooleanValue(true);report["fanHoldXReadOnlyDetailsAndThreeSorts"]=JsonValue.CreateBooleanValue(true);report["fanGoToSettingsNativeButton"]=JsonValue.CreateBooleanValue(true);report["profileSelectionsWrittenByFixture"]=JsonValue.CreateNumberValue(0);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally{CloseOnexMenus();_onexDetailsDialog?.Hide();for(int n=0;n<20&&_onexProfileTileOpening;n++)await Task.Delay(20);if(original!=null)original.IsChecked=true;}
  }
  private async Task VerifyOnexHomeScreenPickerAsync(JsonObject report)
  {
   void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
   async Task Wait(Func<bool> ready,string text){for(int n=0;n<120;n++){if(ready())return;await Task.Delay(60);}throw new TimeoutException(text);}
   void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);
   try
   {
    QuickNavItem.IsChecked=true;await ReadOnexScreenProfilesAsync();ApplyOnexTileAvailability();Check(_onexScreenProfiles.GetNamedBoolean("sourceIndependent",false),"Screen is linked to Performance");Check(qsTileMap.TryGetValue("ReShade",out var tile)&&tile.TileButton!=null&&tile.TileButton.IsEnabled&&OnexHasTileDetails("ReShade"),"Screen tile missing/unavailable");var anchor=tile.TileButton;Invoke(anchor);await Wait(()=>_onexProfileTileMenu?.IsOpen==true,"Screen tap did not open compact list");var choices=_onexProfileTileMenu.Items.OfType<MenuFlyoutItem>().ToArray();Check(choices.Length==_onexScreenProfiles.GetNamedArray("entries").Count&&choices[0].Tag as string=="global"&&choices.All(item=>item.Text.Contains("HDR "))&&choices.Count(item=>item.Icon!=null)==1,"Screen compact list/details/active marker incomplete");_onexProfileTileMenu.Hide();await Task.Delay(60);anchor.Focus(FocusState.Keyboard);Check(OpenFocusedOnexTileDetails(),"Screen X path missing");await Wait(()=>_onexDetailsDialog!=null&&_onexProfileTileSelection!=null,"Screen detailed selector missing");var section=_onexProfileTileSelection;var dialog=_onexDetailsDialog;Check(section.Ids.Count==choices.Length&&section.Rows.Values.All(row=>row.Rename.Visibility==Visibility.Collapsed&&row.Remove.Visibility==Visibility.Collapsed&&row.Details.Text.Contains("Auto HDR ")),"Screen detailed list permits editing or misses HDR details");Check(dialog.PrimaryButtonText=="Go to Screen Color Settings","Screen settings footer missing");for(int i=0;i<3;i++){section.Sort.SelectedIndex=i;Check(section.Ids.Count==choices.Length,"Screen sorting loses profiles");}await SaveOnexImage("onex-screen-tile264.png",section.Card,100);
    Button primary=null;for(int attempt=0;attempt<10&&primary==null;attempt++){await Task.Delay(60);dialog.ApplyTemplate();primary=FindDescendantByName(dialog,"PrimaryButton") as Button;if(primary==null)foreach(var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)){primary=FindDescendantByName(popup.Child,"PrimaryButton") as Button;if(primary!=null)break;}}Check(primary!=null,"Screen Go-to-settings native button missing");Invoke(primary);await Wait(()=>_onexDetailsDialog==null&&DisplayNavItem.IsChecked==true,"Screen Go-to-settings failed");
    report["screenTapCompactAllProfilesAndHdrDetails"]=JsonValue.CreateBooleanValue(true);report["screenHoldXReadOnlyDetailsAndThreeSorts"]=JsonValue.CreateBooleanValue(true);report["screenGoToSettingsNativeButton"]=JsonValue.CreateBooleanValue(true);report["screenIndependent"]=JsonValue.CreateBooleanValue(true);report["profileSelectionsAndScreenWritesByFixture"]=JsonValue.CreateNumberValue(0);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally{CloseOnexMenus();_onexDetailsDialog?.Hide();for(int n=0;n<20&&_onexProfileTileOpening;n++)await Task.Delay(20);if(original!=null)original.IsChecked=true;}
  }
 }
}
