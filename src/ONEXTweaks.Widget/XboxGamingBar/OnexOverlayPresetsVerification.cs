// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
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
  private async Task VerifyOnexOverlayPresetsAsync(JsonObject report)
  {
   void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
   async Task Wait(Func<bool> ready,string text){for(int n=0;n<120;n++){if(ready())return;await Task.Delay(60);}throw new TimeoutException(text);}
   void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
   async Task Confirm(string caption){Button button=null;for(int n=0;n<10&&button==null;n++){await Task.Delay(60);_onexDetailsDialog.ApplyTemplate();button=FindDescendantByName(_onexDetailsDialog,"PrimaryButton") as Button;if(button==null)foreach(var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)){button=FindDescendantByName(popup.Child,"PrimaryButton") as Button;if(button!=null)break;}}Check(button!=null&&button.Content as string==caption,"Preset dialog primary button missing");Invoke(button);}
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);string own=null,run=Guid.NewGuid().ToString("N"),name="ONEXQA Overlay269 "+Guid.NewGuid().ToString("N").Substring(0,10);string manifest=Path.Combine(ApplicationData.Current.LocalFolder.Path,"onex-overlay-preset-qa-owned.json");bool open=false;int sort=0;string originalActive=null;
   try
   {
    Check(!File.Exists(manifest),"Unfinished owned preset QA must be recovered first");_releaseOsdNav.IsChecked=true;await OpenOnexOverlaySettingsAsync();await ReadOnexOverlayPresetsAsync();await Wait(()=>!_onexOverlayOpening&&!_onexOverlayBusy&&!isLoadingOSDConfig&&_onexOverlayConfigReady,"Overlay settings read did not finish");originalActive=_onexOverlayPresets.GetNamedString("activeId");var section=_onexOverlaySavedPresets;open=section.Body.Visibility==Visibility.Visible;sort=section.Sort.SelectedIndex;
    Check((string)_onexOverlayPresetPicker.Choose.Content=="Choose Preset"&&(string)_onexOverlayPresetPicker.Create.Content=="+ Create Preset","Preset header terminology is wrong");Check(_onexOverlayPresetPicker.Footer.Visibility==Visibility.Collapsed&&_onexOverlayPresetPicker.Split.Visibility==Visibility.Collapsed,"Overlay displays AC/DC controls");
    string[] names={"Off","Basic","Horizontal","Horizontal Custom","Vertical Custom"};Check(OnexOverlayFriendlyNames.SequenceEqual(names)&&OnexOverlayNames.SequenceEqual(names),"English original layout names are not exact");Check(names.Skip(1).All(label=>_onexOverlayPresets.GetNamedArray("entries").Any(item=>item.GetObject().GetNamedString("name")==label)),"Standard catalog presets are missing");
    File.WriteAllText(manifest,new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"name",JsonValue.CreateStringValue(name)}}.Stringify());await ChangeOnexOverlayPresetsAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"name",JsonValue.CreateStringValue(name)},{"activate",JsonValue.CreateBooleanValue(false)}});own=_onexOverlayPresets.GetNamedArray("entries").Select(item=>item.GetObject()).Single(item=>item.GetNamedString("name")==name).GetNamedString("id");File.WriteAllText(manifest,new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"name",JsonValue.CreateStringValue(name)},{"id",JsonValue.CreateStringValue(own)}}.Stringify());Check(_onexOverlayPresets.GetNamedString("activeId")==originalActive,"Inactive QA create changed owner preset");
    if(!open)Invoke(section.Header);await Wait(()=>section.Body.Visibility==Visibility.Visible,"Saved Presets did not open");section.Apply(true);Check(section.Ids.Count==_onexOverlayPresets.GetNamedArray("entries").Count&&section.Rows[own].Editable&&!section.Rows["global"].Rename.IsEnabled,"Saved Presets list or protected default incorrect");Check(section.Rows.Values.All(row=>row.Details.Text.Contains("Opacity ")&&row.Details.Text.Contains("%")),"Preset summaries omit useful settings");
    Invoke(section.Rows[own].Rename);await Wait(()=>_onexDetailsDialog!=null&&_onexProfileRenameInput!=null,"Preset pencil did not open rename");Check(_onexDetailsDialog.Title as string=="Rename Preset","Rename dialog still says Profile");_onexProfileRenameInput.Text=name+" Renamed";await Confirm("Rename");await Wait(()=>_onexDetailsDialog==null&&_onexOverlayPresets.GetNamedArray("entries").Any(item=>item.GetObject().GetNamedString("id")==own&&item.GetObject().GetNamedString("name")==name+" Renamed"),"Preset rename did not persist");
    for(int i=0;i<3;i++){section.Sort.SelectedIndex=i;Check(section.Ids.Count==_onexOverlayPresets.GetNamedArray("entries").Count,"Preset sorting loses entries");}
    Invoke(section.Rows[own].Remove);await Wait(()=>_onexDetailsDialog!=null,"Preset delete confirmation missing");Check(_onexDetailsDialog.PrimaryButtonText=="Delete"&&_onexDetailsDialog.CloseButtonText=="Keep It"&&_onexDetailsDialog.Title as string=="Delete Preset?","Preset confirmation labels incorrect");_onexDetailsDialog.Hide();await Wait(()=>_onexDetailsDialog==null,"Keep It did not close");Check(section.Ids.Contains(own),"Keep It removed preset");
    section.Header.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await SaveOnexImage("onex-overlay-saved269.png",section.Card,150);Invoke(section.Rows[own].Remove);await Wait(()=>_onexDetailsDialog!=null,"Preset delete did not reopen");await Confirm("Delete");await Wait(()=>_onexDetailsDialog==null&&!_onexOverlayPresets.GetNamedArray("entries").Any(item=>item.GetObject().GetNamedString("id")==own),"Confirmed preset deletion failed");Check(_onexOverlayPresets.GetNamedString("activeId")==originalActive,"QA cleanup changed selected preset");
    Check(_onexOverlayPreviewText.Blocks.Count>0&&_onexOverlayPreviewModel!=null,"Existing overlay preview disappeared");OnexOverlayScrollViewer.ChangeView(null,0,null,true);ApplyOnexTabPalette();await SaveOnexImage("onex-overlay-page269.png",OnexOverlayScrollViewer,150);
    report["originalEnglishPresetNames"]=JsonValue.CreateBooleanValue(true);report["chooseCreateSavedPresetsNoAcDc"]=JsonValue.CreateBooleanValue(true);report["realInactiveCreateRenameKeepAndDeleteApi"]=JsonValue.CreateBooleanValue(true);report["ownerSelectedPresetUnchanged"]=JsonValue.CreateBooleanValue(true);report["previewRetained"]=JsonValue.CreateBooleanValue(true);report["actualRtssPresetApplyTested"]=JsonValue.CreateBooleanValue(false);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally
   {
    _onexDetailsDialog?.Hide();if(own!=null&&_onexOverlayPresets!=null){var entry=_onexOverlayPresets.GetNamedArray("entries").Select(item=>item.GetObject()).FirstOrDefault(item=>item.GetNamedString("id")==own);if(entry!=null&&entry.GetNamedString("name").StartsWith(name,StringComparison.Ordinal))await ChangeOnexOverlayPresetsAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(own)}});}
    if(_onexOverlaySavedPresets!=null){_onexOverlaySavedPresets.Sort.SelectedIndex=sort;_onexOverlaySavedPresets.Body.Visibility=open?Visibility.Visible:Visibility.Collapsed;}if(File.Exists(manifest)&&(_onexOverlayPresets==null||!_onexOverlayPresets.GetNamedArray("entries").Any(item=>item.GetObject().GetNamedString("name").StartsWith(name,StringComparison.Ordinal))))File.Delete(manifest);if(original!=null)original.IsChecked=true;
   }
  }
 }
}
