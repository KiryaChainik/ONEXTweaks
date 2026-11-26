// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexFanSavedProfilesAsync(JsonObject report)
  {
   void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
   async Task Wait(Func<bool> ready,string text){for(int n=0;n<120;n++){if(ready())return;await Task.Delay(60);}throw new TimeoutException(text);}
   void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
   async Task Confirm(string text)
   {
    Button button=null;
    for(int attempt=0;attempt<8&&button==null;attempt++)
    {
     await Task.Delay(80);_onexDetailsDialog.ApplyTemplate();button=FindDescendantByName(_onexDetailsDialog,"PrimaryButton") as Button;
     if(button==null)foreach(var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot))
     {button=FindDescendantByName(popup.Child,"PrimaryButton") as Button;if(button!=null)break;}
    }
    Check(button!=null&&button.Content as string==text,"Own dialog primary button is not ready: "+text);Invoke(button);
   }
   var perf=new List<string>();var fans=new List<string>();string run=Guid.NewGuid().ToString("N");var folder=ApplicationData.Current.LocalFolder;
   async Task Manifest(){var a=new JsonArray();foreach(string id in perf)a.Add(JsonValue.CreateStringValue(id));var b=new JsonArray();foreach(string id in fans)b.Add(JsonValue.CreateStringValue(id));File.WriteAllText(Path.Combine(folder.Path,"onex-fan-sources-qa-owned.json"),new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"performance",a},{"fans",b}}.Stringify());await Task.CompletedTask;}
   await ReadOnexPerformanceProfilesAsync();await ReadOnexFanProfilesAsync();Check(OnexFrontend.ProfileId=="global"&&_onexFanProfiles.GetNamedString("naturalActiveId")=="global","Owner profile context protected");string selection=_onexProfilesState.GetNamedString("selection");var originalTab=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);bool open=_onexFanSavedProfiles.Body.Visibility==Visibility.Visible;int sort=_onexFanSavedProfiles.Sort.SelectedIndex;string applied=OnexAppliedFanId;
   try
   {
    foreach(string suffix in new[]{" A"," B"}){await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"name",JsonValue.CreateStringValue("ONEXQA FanSaved256 "+run.Substring(0,8)+suffix)}});perf.Add(OnexFrontend.ProfileId);await Manifest();}
    FanNavItem.IsChecked=true;await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"performanceProfileId",JsonValue.CreateStringValue(perf[0])}});string own=_onexFanProfiles.GetNamedString("activeId");fans.Add(own);await Manifest();
    await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("split")},{"id",JsonValue.CreateStringValue(own)},{"enabled",JsonValue.CreateBooleanValue(true)}});
    var curve=new JsonObject();foreach(string key in OnexFanCurveKeys)curve[key]=JsonValue.CreateNumberValue(60);
    foreach(bool ac in new[]{false,true})await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("values")},{"id",JsonValue.CreateStringValue(own)},{"onAc",_onexFanProfiles["onAc"]},{"source",JsonValue.CreateStringValue(ac?"ac":"dc")},{"values",new JsonObject{{"mode",JsonValue.CreateNumberValue(ac?0:3)},{"curve",curve}}}});
    var section=_onexFanSavedProfiles;if(section.Body.Visibility!=Visibility.Visible)Invoke(section.Header);await Wait(()=>section.Body.Visibility==Visibility.Visible,"Saved block did not open");section.Apply(true);
    Check(section.Ids.Count==_onexFanProfiles.GetNamedArray("entries").Count&&section.Ids.Distinct().Count()==section.Ids.Count&&section.Ids[0]=="global","Saved list omits/duplicates profiles or loses Global");
    foreach(string id in new[]{"global",OnexAutoFanId})Check(!section.Rows[id].Rename.IsEnabled&&!section.Rows[id].Remove.IsEnabled,"Global/system preset is editable");
    Check(section.Rows[own].Details.Text.Contains("DC ·")&&section.Rows[own].Details.Text.Contains("AC ·")&&section.Rows[own].Details.Text.Contains("60 / 60")&&section.Rows[own].Details.Text.Contains(OnexStrings.Get("Auto · управление системой")),"DC/AC detailed summaries are incomplete");
    Invoke(section.Rows[own].Rename);await Wait(()=>_onexDetailsDialog!=null&&_onexProfileRenameInput!=null,"Saved pencil did not open rename");string renamed="ONEXQA FanSaved256 "+run.Substring(0,8)+" Renamed";_onexProfileRenameInput.Text=renamed;await Confirm("Rename");await Wait(()=>_onexDetailsDialog==null&&OnexPreset(_onexFanProfiles,own)?.GetNamedString("name")==renamed,"Rename through dialog did not persist");
    Check(OnexFrontend.ProfileId==perf[1]&&OnexAppliedFanId==applied,"Renaming inactive Fan changed applied context");
    for(int index=0;index<3;index++){section.Sort.SelectedIndex=index;section.Apply(true);Check(section.Ids[0]=="global"&&section.Ids.Count==_onexFanProfiles.GetNamedArray("entries").Count,"Sort drops profiles/Global");}
    Invoke(section.Rows[own].Remove);await Wait(()=>_onexDetailsDialog!=null,"Saved delete did not open confirmation");Check(_onexDetailsDialog.PrimaryButtonText=="Delete"&&_onexDetailsDialog.CloseButtonText=="Keep It"&&_onexDetailsDialog.DefaultButton==ContentDialogButton.Close,"Delete confirmation is unsafe or unclear");_onexDetailsDialog.Hide();await Wait(()=>_onexDetailsDialog==null,"Keep It did not close dialog");Check(OnexPreset(_onexFanProfiles,own)!=null,"Keep It deleted the Fan profile");
    section.Header.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await SaveOnexImage("onex-fan-saved257.png",section.Card,160);
    Invoke(section.Rows[own].Remove);await Wait(()=>_onexDetailsDialog!=null,"Delete confirmation did not reopen");await Confirm("Delete");await Wait(()=>_onexDetailsDialog==null&&OnexPreset(_onexFanProfiles,own)==null,"Confirmed delete did not remove requested profile");Check(OnexFrontend.ProfileId==perf[1]&&OnexAppliedFanId==applied,"Inactive deletion changed live context");
    report["allProfilesAndProtectedSystemPresets"]=JsonValue.CreateBooleanValue(true);report["threeSortModesGlobalFirst"]=JsonValue.CreateBooleanValue(true);report["detailedDcAcSummaries"]=JsonValue.CreateBooleanValue(true);report["realRenameDialogPersists"]=JsonValue.CreateBooleanValue(true);report["keepItCancelsAndDeleteConfirms"]=JsonValue.CreateBooleanValue(true);report["injectedNativeAutomationAndRealApi"]=JsonValue.CreateBooleanValue(true);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally
   {
    _onexDetailsDialog?.Hide();foreach(string id in fans.AsEnumerable().Reverse())if(OnexPreset(_onexFanProfiles,id)!=null)await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(id)}});
    foreach(string id in perf.AsEnumerable().Reverse())if(OnexProfiles.Any(entry=>entry.GetNamedString("id")==id&&entry.GetNamedString("name").StartsWith("ONEXQA FanSaved256 "+run.Substring(0,8),StringComparison.Ordinal)))await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(id)}});
    if(selection=="auto")await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("automatic")}});await ReadOnexFanProfilesAsync();_onexFanSavedProfiles.Sort.SelectedIndex=sort;_onexFanSavedProfiles.Body.Visibility=open?Visibility.Visible:Visibility.Collapsed;var manifest=await folder.TryGetItemAsync("onex-fan-sources-qa-owned.json");if(manifest!=null)await manifest.DeleteAsync();if(originalTab!=null)originalTab.IsChecked=true;report["ownQaProfilesDeletedAfterRun"]=JsonValue.CreateBooleanValue(true);
   }
  }
 }
}
