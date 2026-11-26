// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexProfileWorkflowAsync(JsonObject report)
        {
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            async Task Wait(Func<bool> test,string message){for(int n=0;n<100;n++){if(test())return;await Task.Delay(100);}throw new TimeoutException(message);}
            void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            EnsureOnexProfilesFrontend();EnsureOnexFanProfiles();EnsureOnexProfileFooters();await ReadOnexPerformanceProfilesAsync();await ReadOnexScreenProfilesAsync();await ReadOnexFanProfilesAsync();
            Check(_onexFanProfiles.GetNamedString("activeId")=="global","Owner has an active Fan preset; mutating QA deferred");
            var headers=new[]{_onexPerformanceProfilePickerCard,_onexScreenProfilePickerCard,_onexFanProfilePickerCard};
            var pickers=new[]{_onexPerformanceProfilePicker,_onexScreenProfilePicker,_onexFanProfilePicker};
            var views=new[]{PerformanceScrollViewer,DisplayScrollViewer,FanScrollViewer};
            var footers=new Border[]{_onexPerformanceProfileFooter,null,_onexFanProfileFooter};
            var tabs=new[]{_releasePowerNav,DisplayNavItem,FanNavItem};
            for(int n=0;n<3;n++)
            {
                tabs[n].IsChecked=true;views[n].ChangeView(null,0,null,true);await Task.Delay(180);views[n].UpdateLayout();
                var root=pickers[n].Root;var choose=pickers[n].Choose;var create=pickers[n].Create;var remove=pickers[n].Remove;
                Check(ReferenceEquals(((Panel)headers[n].Parent).Children[0],headers[n]),"Profile header is not the first block");
                Check(root.Children[0] is Grid buttons&&buttons.Children.Contains(choose)&&buttons.Children.Contains(create),"Choose/Create are not the first shared row");
                Check(root.Children[1] is Grid active&&active.Children.Contains(remove)&&Grid.GetColumn(remove)==2,"X is not at the right of the title");
                if(n==1)Check(pickers[n].Footer.Parent==null&&pickers[n].Split.Visibility==Visibility.Collapsed,"Independent Screen Color still has an AC/DC footer");
                else Check(ReferenceEquals(((Panel)footers[n].Parent).Children.Last(),footers[n]),"AC/DC footer is not after the settings");
                Check(root.Children[2] is TextBlock brief&&!string.IsNullOrWhiteSpace(brief.Text),"Active profile summary is absent below the title");
                Check(Math.Abs(choose.ActualWidth-create.ActualWidth)<1&&choose.ActualWidth>200,"Shared buttons have inconsistent widths");
                FocusFirstTabContentElement();Check(ReferenceEquals(Windows.UI.Xaml.Input.FocusManager.GetFocusedElement(),choose),"Tab first focus is not Choose Profile");
                await SaveOnexImage("onex-profile-workflow-"+n+"-208.png",views[n],1100);
            }
            _releasePowerNav.IsChecked=true;await Task.Delay(100);
            Check(LegacyGlobalProfileCard.Visibility==Visibility.Collapsed&&GameProfileCard.Visibility==Visibility.Collapsed&&ActiveProfileCard.Visibility==Visibility.Collapsed&&PowerSourceProfileCard.Visibility==Visibility.Collapsed,"Legacy profile summary/editor still duplicates the picker");
            Check(_onexPerformanceProfilePicker.Choose.IsEnabled&&OnexPerfTdpCard.IsEnabled,"Global cannot be selected/edited inline");
            Check(ReferenceEquals(OnexProfileNext(_onexPerformanceProfilePicker,_onexPerformanceProfilePicker.Remove.IsEnabled?(Control)_onexPerformanceProfilePicker.Remove:_onexPerformanceProfilePicker.Choose,true,OnexPerfTdpCard),OnexPerfTdpCard),"Profile block does not lead into settings");
            if(App.OnexTileRevisionStage==78)
            {
                report["sharedLayoutAllThreeTabs"]=JsonValue.CreateBooleanValue(true);report["legacyDuplicateHidden"]=JsonValue.CreateBooleanValue(true);report["firstFocusAndSettingsTransition"]=JsonValue.CreateBooleanValue(true);report["acDcFooterPerformanceAndFanOnly"]=JsonValue.CreateBooleanValue(true);report["settledCaptureDelayMs"]=JsonValue.CreateNumberValue(1100);report["readOnlyRenderVerification"]=JsonValue.CreateBooleanValue(true);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);return;
            }
            FanNavItem.IsChecked=true;string qa=null;
            int originalMode=(int)_onexHardwareHome.GetNamedObject("fan").GetNamedNumber("profile");
            try
            {
                await _onexFanProfilePicker.OpenCreateAsync();var menu=(MenuFlyout)_onexOpenMenus.Last(item=>item.IsOpen);var manual=menu.Items.OfType<MenuFlyoutItem>().First(item=>item.Tag as string=="manual");menu.Hide();
                ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(manual).GetPattern(PatternInterface.Invoke)).Invoke();
                await Wait(()=>_onexFanProfiles.GetNamedString("activeId")!="global","Fan manual profile was not created");qa=_onexFanProfiles.GetNamedString("activeId");
                int changed=originalMode==3?0:3;
                var curve=new JsonObject();foreach(string key in OnexFanCurveKeys)curve[key]=JsonValue.CreateNumberValue(60);
                await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("values")},{"id",JsonValue.CreateStringValue(qa)},{"onAc",_onexFanProfiles["onAc"]},{"values",new JsonObject{{"mode",JsonValue.CreateNumberValue(changed)},{"curve",curve}}}});
                await Wait(()=>!_onexFanBusy,"Fan mode is pending");
                await ReadOnexFanProfilesAsync();var entry=_onexFanProfiles.GetNamedArray("entries").Select(value=>value.GetObject()).Single(value=>value.GetNamedString("id")==qa);
                Check(entry.GetNamedObject(_onexFanProfiles.GetNamedBoolean("onAc")?"ac":"dc").GetNamedNumber("mode")==changed,"Fan change did not autosave in the selected profile");
                Check(_onexHardwareHome.GetNamedObject("fan").GetNamedNumber("profile")==changed,"Fan mode did not reach PowerAgent");
                Invoke(_onexFanProfilePicker.Remove);menu=(MenuFlyout)_onexOpenMenus.Last(item=>item.IsOpen);Check(menu.Items.OfType<MenuFlyoutItem>().Select(item=>item.Text).SequenceEqual(new[]{"Delete","Keep It"}),"X menu options differ");menu.Hide();
                await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("keep")},{"id",JsonValue.CreateStringValue(qa)}});
                Check(_onexFanProfiles.GetNamedString("activeId")=="global"&&_onexFanProfiles.GetNamedArray("entries").Any(value=>value.GetObject().GetNamedString("id")==qa),"Keep It did not retain the preset and select Global");
                Check(_onexHardwareHome.GetNamedObject("fan").GetNamedNumber("profile")==originalMode,"Global Fan was not restored after Keep It");
                report["fanModeNativeRoundtrip"]=JsonValue.CreateBooleanValue(true);report["fanModeAutosavedToManual"]=JsonValue.CreateBooleanValue(true);report["sharedLayoutAllThreeTabs"]=JsonValue.CreateBooleanValue(true);report["legacyDuplicateHidden"]=JsonValue.CreateBooleanValue(true);report["firstFocusAndSettingsTransition"]=JsonValue.CreateBooleanValue(true);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["fullFanCurveEditorImplemented"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                CloseOnexMenus();if(qa!=null)await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(qa)}});
                _releasePowerNav.IsChecked=true;PerformanceScrollViewer.ChangeView(null,0,null,true);
            }
        }
    }
}
