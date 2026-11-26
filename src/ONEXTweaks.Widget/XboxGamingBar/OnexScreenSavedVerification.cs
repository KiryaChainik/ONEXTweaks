// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexScreenSavedProfilesAsync(JsonObject report)
  {
   void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
   async Task Wait(Func<bool> ready,string text){for(int n=0;n<100;n++){if(ready())return;await Task.Delay(60);}throw new TimeoutException(text);}
   void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);DisplayNavItem.IsChecked=true;await ReadOnexScreenProfilesAsync();var section=_onexScreenSavedProfiles;int sort=section.Sort.SelectedIndex;bool open=section.Body.Visibility==Visibility.Visible;int cancelledDialogs=0;
   try
   {
    Check(_onexScreenProfiles.GetNamedBoolean("sourceIndependent",false)&&_onexScreenProfilePicker.Split.Visibility==Visibility.Collapsed&&_onexScreenProfilePicker.Footer.Visibility==Visibility.Collapsed,"Screen profiles are not independent or show AC/DC");
    if(!open)Invoke(section.Header);await Wait(()=>section.Body.Visibility==Visibility.Visible,"Screen Saved Profiles did not open");section.Header.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(150);UpdateLayout();section.Apply(true);UpdateLayout();
    Check(section.Ids.Count==_onexScreenProfiles.GetNamedArray("entries").Count&&section.Ids.Distinct().Count()==section.Ids.Count&&section.Ids[0]=="global","Screen list omits/duplicates profiles or unpins Global");Check(!section.Rows["global"].Rename.IsEnabled&&!section.Rows["global"].Remove.IsEnabled,"Screen Global can be renamed/deleted");
    foreach(var row in section.Rows.Values)
    {
     Check(row.Details.Text.Contains("HDR ")&&row.Details.Text.Contains("Auto HDR ")&&!row.Details.Text.StartsWith("DC ·"),"Screen summary is incomplete or source-split");
     var left=row.Rename.TransformToVisual(section.Card).TransformPoint(new Point());var right=row.Remove.TransformToVisual(section.Card).TransformPoint(new Point());Check(left.X>=0&&right.X+row.Remove.ActualWidth<=section.Card.ActualWidth+1,"Screen row actions are clipped");
    }
    for(int index=0;index<3;index++){section.Sort.SelectedIndex=index;section.Apply(true);Check(section.Ids[0]=="global"&&section.Ids.Count==_onexScreenProfiles.GetNamedArray("entries").Count,"Screen sort loses profiles");}
    UpdateLayout();var controls=OnexPageNavigationRows().SelectMany(row=>row).Select(cell=>cell.Control).ToArray();Check(controls.Contains(section.Header)&&controls.Contains(section.Sort)&&section.Rows.Values.Where(row=>row.Choose.IsEnabled).All(row=>controls.Contains(row.Choose)),"Screen Saved controls are missing from content navigation");
    var editable=section.Rows.Values.FirstOrDefault(row=>row.Editable);
    if(editable!=null)
    {
     Invoke(editable.Rename);await Wait(()=>_onexDetailsDialog!=null&&_onexProfileRenameInput!=null,"Screen pencil did not open shared rename dialog");_onexDetailsDialog.Hide();await Wait(()=>_onexDetailsDialog==null,"Screen rename cancel did not close");cancelledDialogs++;
     Invoke(section.Rows[editable.Id].Remove);await Wait(()=>_onexDetailsDialog!=null,"Screen X did not open shared confirmation");Check(_onexDetailsDialog.PrimaryButtonText=="Delete"&&_onexDetailsDialog.CloseButtonText=="Keep It","Screen confirmation actions differ from Fan");_onexDetailsDialog.Hide();await Wait(()=>_onexDetailsDialog==null,"Screen Keep It did not close");cancelledDialogs++;
    }
    section.Header.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await SaveOnexImage("onex-screen-saved258.png",section.Card,150);
    report["completeScreenListAndThreeSorts"]=JsonValue.CreateBooleanValue(true);report["colorAndHdrDetails"]=JsonValue.CreateBooleanValue(true);report["screenIndependentNoAcDc"]=JsonValue.CreateBooleanValue(true);report["savedControlsInNavigationAndNotClipped"]=JsonValue.CreateBooleanValue(true);report["sharedRenameDeleteImplementationAcceptedByFan96"]=JsonValue.CreateBooleanValue(true);report["cancelledScreenDialogs"]=JsonValue.CreateNumberValue(cancelledDialogs);report["screenSettingsWritesByFixture"]=JsonValue.CreateNumberValue(0);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally{_onexDetailsDialog?.Hide();section.Sort.SelectedIndex=sort;section.Body.Visibility=open?Visibility.Visible:Visibility.Collapsed;if(original!=null)original.IsChecked=true;}
  }
 }
}
