// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexSavedProfilesAsync(JsonObject report)
        {
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            async Task Wait(Func<bool> ready,string message){for(int n=0;n<100;n++){if(ready())return;await Task.Delay(80);}throw new TimeoutException(message);}
            void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            var owned=new List<string>();var settings=ApplicationData.Current.LocalSettings;
            bool hadSort=settings.Values.TryGetValue("ProfileSortMode",out object oldSort);int oldIndex=ProfileSortComboBox.SelectedIndex;string a=null,b=null;
            await ReadOnexPerformanceProfilesAsync();Check(_onexProfilesState.GetNamedString("activeId")=="global"&&_onexProfilesState.GetNamedValue("currentSession").ValueType==JsonValueType.Null,"Owner active process/profile is protected; mutating QA deferred");
            _releasePowerNav.IsChecked=true;PerfSavedProfilesContent.Visibility=Visibility.Visible;
            try
            {
                await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"name",JsonValue.CreateStringValue("ONEXQA Metadata213 A")}});a=OnexActiveProfile.GetNamedString("id");owned.Add(a);
                Check(_onexSavedProfileIds.Count==OnexProfiles.Count()&&_onexSavedProfileIds.Distinct().Count()==_onexSavedProfileIds.Count&&_onexSavedProfileIds[0]=="global"&&_onexSavedProfileIds.Contains(a),"Saved list omits Global/active profiles or duplicates identities");
                Check(_onexPerformanceProfilePicker.Rename.Visibility==Visibility.Visible&&Grid.GetColumn(_onexPerformanceProfilePicker.Rename)==1&&Grid.GetColumn(_onexPerformanceProfilePicker.Remove)==2,"Manual header pencil/X placement is wrong");
                Invoke(_onexPerformanceProfilePicker.Rename);await Wait(()=>_onexProfileRenameInput!=null&&_onexDetailsDialog!=null,"Header pencil did not open rename dialog");
                Check(_onexDetailsDialog.PrimaryButtonText=="Rename"&&_onexDetailsDialog.CloseButtonText=="Cancel","Rename dialog actions are unclear");_onexDetailsDialog.Hide();await Wait(()=>_onexProfileRenameInput==null,"Rename dialog did not close");
                Check(OnexActiveProfile.GetNamedString("name")=="ONEXQA Metadata213 A","Cancelling rename changed the profile");
                await _onexPerformanceProfilePicker.RenameAsync(a,"ONEXQA Metadata213 renamed");Check(OnexActiveProfile.GetNamedString("name")=="ONEXQA Metadata213 renamed","Shared rename did not update selected name");
                await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"name",JsonValue.CreateStringValue("ONEXQA Metadata213 B")}});b=OnexActiveProfile.GetNamedString("id");owned.Add(b);
                await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("rename")},{"id",JsonValue.CreateStringValue(a)},{"name",JsonValue.CreateStringValue("ONEXQA Metadata213 inactive")}});
                Check(OnexActiveProfile.GetNamedString("id")==b,"Renaming inactive profile changed active scope");
                var remove=_onexSavedProfileRows.SelectMany(row=>row).OfType<Button>().Single(button=>button.Name=="OnexSavedDelete_"+a);Invoke(remove);
                await Wait(()=>_onexDetailsDialog!=null,"Saved profile X did not open confirmation");Check(_onexDetailsDialog.PrimaryButtonText=="Delete"&&_onexDetailsDialog.CloseButtonText=="Keep It","Delete confirmation lacks Delete/Keep It");
                _onexDetailsDialog.Hide();await Wait(()=>_onexDetailsDialog==null,"Delete confirmation did not close");Check(OnexProfiles.Any(entry=>entry.GetNamedString("id")==a)&&OnexActiveProfile.GetNamedString("id")==b,"Keep It/cancel changed profiles or active selection");
                foreach(string mode in new[]{"modified","tdp","name"})
                {ProfileSortComboBox.SelectedItem=ProfileSortComboBox.Items.OfType<ComboBoxItem>().Single(item=>item.Tag as string==mode);RenderOnexSavedProfiles();Check(_onexSavedProfileIds[0]=="global"&&_onexSavedProfileIds.Count==OnexProfiles.Count(),"Sorting loses profiles or unpins Global");}
                int ownIndex=_onexSavedProfileIds.IndexOf(b);var card=(FrameworkElement)AllGameProfilesContainer.Children[ownIndex];await SaveOnexImage("onex-saved-profile-213.png",card,600);
                var details=_onexSavedProfileRows.SelectMany(row=>row).OfType<Button>().Single(button=>button.Name=="OnexSavedDetails_"+b);Invoke(details);await SaveOnexImage("onex-saved-profile-details-213.png",card,700);
                await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(a)}});Check(OnexActiveProfile.GetNamedString("id")==b&&!OnexProfiles.Any(entry=>entry.GetNamedString("id")==a),"Deleting inactive profile changed active scope");owned.Remove(a);
                report["allProfilesIncludingGlobalAndActive"]=JsonValue.CreateBooleanValue(true);report["headerPencilAndRenameDialog"]=JsonValue.CreateBooleanValue(true);report["renameAndInactiveDeleteKeepActiveScope"]=JsonValue.CreateBooleanValue(true);report["cancelKeepsProfile"]=JsonValue.CreateBooleanValue(true);report["threeSortModesKeepGlobalFirst"]=JsonValue.CreateBooleanValue(true);report["detailedSummaryAvailable"]=JsonValue.CreateBooleanValue(true);report["injectedUiAndProtocolActions"]=JsonValue.CreateBooleanValue(true);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                _onexDetailsDialog?.Hide();
                foreach(string id in owned.AsEnumerable().Reverse())
                {var entry=OnexProfiles.FirstOrDefault(value=>value.GetNamedString("id")==id);if(entry!=null&&entry.GetNamedString("name").StartsWith("ONEXQA Metadata213",StringComparison.Ordinal))await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(id)}});}
                if(hadSort)settings.Values["ProfileSortMode"]=oldSort;else settings.Values.Remove("ProfileSortMode");
                ProfileSortComboBox.SelectedIndex=oldIndex;
                if(hadSort)settings.Values["ProfileSortMode"]=oldSort;else settings.Values.Remove("ProfileSortMode");
            }
        }
    }
}
