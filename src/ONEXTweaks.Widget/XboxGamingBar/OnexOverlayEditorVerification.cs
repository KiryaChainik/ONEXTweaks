// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexOverlayPreviewReuseAsync(JsonObject report)
        {
            int checks=0,requests=0,reused=0,changed=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
            var original=GetVisibleNavigationItems().FirstOrDefault(item=>item.IsChecked==true);bool expanded=isOSDCustomizeExpanded;
            async Task<string> Preferences(){var r=await App.SendMessageAsync(new ValueSet{{"OnexOverlayGet",true}});return (string)r["OnexOverlayPreferences"];}
            async Task<JsonObject> Model(int level,string draft){var r=await App.SendMessageAsync(new ValueSet{{"OnexOverlayPreviewGet",level},{"OnexOverlayPreviewConfig",draft}});requests++;Check(!r.ContainsKey("Error"),"Read-only production preview RPC failed");return JsonObject.Parse((string)r["OnexOverlayPreview"]);}
            Check(_onexDetailsDialog==null&&!_onexOverlayBusy&&!OnexSourceEditorsPending,"Protect pending owner edits");
            string before=await Preferences();
            try
            {
                _releaseOsdNav.IsChecked=true;await OpenOnexOverlaySettingsAsync();EnsureOnexOverlayEditor();
                for(int i=0;i<100&&(_onexOverlayOpening||_onexOverlayPreviewBusy);i++)await Task.Delay(10);
                _onexOverlayPreviewDelay?.Stop();Check(!_onexOverlayOpening&&!_onexOverlayPreviewBusy,"Protect pending preview operation");
                foreach(int level in new[]{1,2,3,4})
                {
                    var first=await Model(level,"");Check(first.GetNamedBoolean("exampleData"),"Preview fixture telemetry must be labelled");RenderOnexOverlayPreview(first);UpdateLayout();
                    Check(_onexOverlayPreviewText.Blocks.Count==1&&_onexOverlayPreviewText.ActualHeight>0,"Real preview UI did not render");var paragraph=_onexOverlayPreviewText.Blocks[0];int builds=_onexOverlayPreviewBuilds;
                    RenderOnexOverlayPreview(await Model(level,""));Check(ReferenceEquals(paragraph,_onexOverlayPreviewText.Blocks[0])&&_onexOverlayPreviewBuilds==builds,"Identical complete model rebuilt native paragraph");reused++;
                    var second=await Model(level,"TextSize:200;Font:Unispace|700;Background:1|75;Opacity:50");Check(second.Stringify()!=first.Stringify(),"Different read-only format draft must produce a different preview model");
                    RenderOnexOverlayPreview(second);Check(!ReferenceEquals(paragraph,_onexOverlayPreviewText.Blocks[0])&&_onexOverlayPreviewBuilds==builds+1,"Changed native font/size/background model was incorrectly reused");
                    Check(_onexOverlayPreviewText.FontFamily.Source==second.GetNamedString("fontFace"),"Changed preview font not applied");changed++;paragraph=_onexOverlayPreviewText.Blocks[0];
                    RenderOnexOverlayPreview(first);Check(!ReferenceEquals(paragraph,_onexOverlayPreviewText.Blocks[0])&&_onexOverlayPreviewBuilds==builds+2,"Returning to an earlier model did not render again");changed++;
                }
                Check(await Preferences()==before,"Read-only previews changed persisted overlay settings");
                report["previewReuseChecks"]=JsonValue.CreateNumberValue(checks);report["actualReadOnlyProductionRpcRequests"]=JsonValue.CreateNumberValue(requests);report["identicalParagraphsReused"]=JsonValue.CreateNumberValue(reused);report["changedModelsRendered"]=JsonValue.CreateNumberValue(changed);
                report["fixtureTelemetryNotRealSensors"]=JsonValue.CreateBooleanValue(true);report["actualNativeUiObjects"]=JsonValue.CreateBooleanValue(true);report["ownerSettingsWritten"]=JsonValue.CreateBooleanValue(false);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["rasterCapture"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                // Refresh current production data; never replay stored owner settings.
                await ReadOnexOverlayConfigurationAsync();if(_onexOverlayPreviewModel!=null)RenderOnexOverlayPreview(await Model(osdCustomizeLevel,BuildOSDConfigString(true)));
                report["readOnlyRestorationPreviewRequests"]=JsonValue.CreateNumberValue(1);
                if(isOSDCustomizeExpanded!=expanded)OSDCustomizeExpandButton_Click(OSDCustomizeExpandButton,null);if(original?.Visibility==Visibility.Visible)original.IsChecked=true;
            }
        }
        private async Task VerifyOnexOverlayEditorAsync(JsonObject report)
        {
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            var folder=ApplicationData.Current.LocalFolder;var values=ApplicationData.Current.LocalSettings.Values;
            var localBackup=values.Where(p=>p.Key.StartsWith("OSD")).ToDictionary(p=>p.Key,p=>p.Value);
            var originalReply=await App.SendMessageAsync(new ValueSet{{"OnexOverlayGet",true}});
            string originalPrefs=JsonObject.Parse((string)originalReply["OnexOverlayPreferences"]).Stringify();var original=JsonObject.Parse(originalPrefs);
            int originalEditor=osdCustomizeLevel;bool originalExpanded=isOSDCustomizeExpanded;
            var cases=new JsonArray();int directDrafts=0;
            async Task SetConfig(string config)
            {
                var reply=await App.SendMessageAsync(new ValueSet{{"Command",(int)Shared.Enums.Command.Set},{"Function",(int)Shared.Enums.Function.OSDConfig},{"Content",config},{"OnexUserInput",true}});
                Check(!reply.ContainsKey("Error"),"RTSS rejected test formatting");
            }
            async Task<JsonObject> Preferences()
            {var response=await App.SendMessageAsync(new ValueSet{{"OnexOverlayGet",true}});return JsonObject.Parse((string)response["OnexOverlayPreferences"]);}
            async Task WaitSaved(string key,string expected)
            {
                for(int i=0;i<80;i++){var current=await Preferences();var config=current.GetNamedString("config");if(config.Split(';').Contains(key+":"+expected))return;await Task.Delay(50);}
                throw new InvalidOperationException("UI change was not saved: "+key);
            }
            async Task Capture(string id)
            {
                await Task.Delay(700);
                for(int n=0;n<80;n++){await RefreshOnexOverlayPreviewAsync();if(_onexOverlayPreviewRenderedConfig==BuildOSDConfigString(true)&&_onexOverlayPreviewModel?.GetNamedNumber("level")==osdCustomizeLevel)break;await Task.Delay(50);}
                Check(_onexOverlayPreviewRenderedConfig==BuildOSDConfigString(true)&&_onexOverlayPreviewModel?.GetNamedNumber("level")==osdCustomizeLevel,"Visible preview did not catch up with edited controls");
                await Task.Delay(150);UpdateLayout();
                _onexOverlayPreviewCard.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(150);
                await SaveOnexImage("onex-overlay-preview-"+id+"-165.png",_onexOverlayPreviewCard);
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-osd-native-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)}}.Stringify());
                for(int i=0;i<200;i++)
                {
                    try{var ack=JsonObject.Parse(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-osd-native-qa-ack.json")));if(ack.GetNamedString("id","")==id){Check(ack.GetNamedBoolean("passed",false),"Native renderer capture failed");_releaseOsdNav.IsChecked=true;return;}}
                    catch(System.IO.FileNotFoundException){}
                    await Task.Delay(50);
                }
                throw new TimeoutException("Native renderer host did not acknowledge "+id);
            }
            try
            {
                await ReadOnexOverlayConfigurationAsync();Check(_onexOverlayConfigReady,"Authoritative overlay settings did not load");Check(!isLoadingOSDConfig,"Initialization guard still blocks user formatting");
                _releaseOsdNav.IsChecked=true;if(!isOSDCustomizeExpanded)OSDCustomizeExpandButton_Click(OSDCustomizeExpandButton,null);
                await Task.Delay(300);
                var drafts=new[]{
                    "", "TextSize:200;Font:Unispace|700;Background:1|75;Opacity:50", "L3:Battery,Blank1,CPU;L3_Order:Battery,Blank1,CPU;L4:CPU,Blank1,Battery;L4_Order:CPU,Blank1,Battery;L4_Columns:1",
                    "TextColor:FF8000;LabelColor:AABBCC;L3:FPS,FrametimeGraph;L3_Order:FPS,FrametimeGraph;L4:FPS,FrametimeGraph;L4_Order:FPS,FrametimeGraph",
                    "L3_Custom:<Unknown=1>Extra;L4_Custom:<Unknown=1>Extra"};
                foreach(int level in new[]{1,2,3,4})foreach(string draft in drafts)
                {
                    var response=await App.SendMessageAsync(new ValueSet{{"OnexOverlayPreviewGet",level},{"OnexOverlayPreviewConfig",draft}});
                    Check(!response.ContainsKey("Error"),"Read-only preview failed");var model=JsonObject.Parse((string)response["OnexOverlayPreview"]);
                    Check(model.GetNamedBoolean("exampleData"),"Example telemetry was not identified");RenderOnexOverlayPreview(model);
                    Check(_onexOverlayPreviewText.Blocks.Count==1,"Preview did not render a paragraph");directDrafts++;
                    if(level>=3&&draft.Contains("Unknown"))Check(_onexOverlayPreviewNotice.Text.Contains("не поддерживаются"),"Unknown RTSS tags need an honest preview warning");
                }
                Check((await Preferences()).Stringify()==originalPrefs,"Read-only drafts changed the active overlay or stored formatting");
                report["readOnlyDrafts"]=JsonValue.CreateNumberValue(directDrafts);report["draftsWereDirectRpcAndUiRendering"]=JsonValue.CreateBooleanValue(true);
                var bad=await App.SendMessageAsync(new ValueSet{{"Command",(int)Shared.Enums.Command.Set},{"Function",(int)Shared.Enums.Function.OSDConfig},{"Content","Font:"+new string('x',81)},{"OnexUserInput",true}});
                Check(bad.ContainsKey("Error")&&(await Preferences()).Stringify()==originalPrefs,"Rejected font mutated saved settings");
                var noInput=await App.SendMessageAsync(new ValueSet{{"Command",(int)Shared.Enums.Command.Set},{"Function",(int)Shared.Enums.Function.OSDConfig},{"Content","Opacity:10"}});
                Check(noInput.ContainsKey("Error")&&(await Preferences()).Stringify()==originalPrefs,"Startup replay without user input was accepted");
                await Capture("read-only-check");
                const string testConfig="Font:Bahnschrift|400;TextSize:150;TextColor:FF8000;LabelColor:FFFFFF;Opacity:100;Background:0|60;L3:Time;L3_Order:Time;L4:FPS,FrametimeGraph,CPU,Battery;L4_Order:FPS,FrametimeGraph,CPU,Battery;L4_Columns:1;FuturePreserved:untouched";
                await SetConfig(testConfig);await ReadOnexOverlayConfigurationAsync();
                OSDCustomizeLevelComboBox.SelectedItem=OSDCustomizeLevelComboBox.Items.OfType<ComboBoxItem>().Single(i=>i.Tag as string=="3");
                await SetOnexOverlayLevelAsync(3);
                OSDTextSizeComboBox.SelectedItem=OSDTextSizeComboBox.Items.OfType<ComboBoxItem>().Single(i=>i.Tag as string=="175");await WaitSaved("TextSize","175");
                Check((await Preferences()).GetNamedString("config").Contains("FuturePreserved:untouched"),"Unrecognized settings were discarded by UI editing");
                Check(!OnexOverlayControlVisible(OSDBackgroundOpacitySlider),"Hidden background slider entered navigation");
                Check(MoveOsdPanelFocus(OSDCustomizeLevelComboBox,+1)&&ReferenceEquals(FocusManager.GetFocusedElement(),OSDTextSizeComboBox),"Preset Down did not enter the size control");
                await Capture("bright");cases.Add(new JsonObject{{"id",JsonValue.CreateStringValue("bright")},{"level",JsonValue.CreateNumberValue(3)},{"brightness",JsonValue.CreateNumberValue(100)}});
                OSDOpacitySlider.Value=50;await WaitSaved("Opacity","50");await Capture("dim");cases.Add(new JsonObject{{"id",JsonValue.CreateStringValue("dim")},{"level",JsonValue.CreateNumberValue(3)},{"brightness",JsonValue.CreateNumberValue(50)}});
                OSDBackgroundToggle.IsOn=true;OSDBackgroundOpacitySlider.Value=75;await WaitSaved("Background","1|75");
                Check(OnexOverlayControlVisible(OSDBackgroundOpacitySlider),"Enabled background slider stayed hidden");
                await Capture("fill");cases.Add(new JsonObject{{"id",JsonValue.CreateStringValue("fill")},{"level",JsonValue.CreateNumberValue(3)},{"fillOpacity",JsonValue.CreateNumberValue(75)}});
                OSDFontComboBox.SelectedItem=OSDFontComboBox.Items.OfType<ComboBoxItem>().Single(i=>i.Tag as string=="Unispace|400");await WaitSaved("Font","Unispace|400");
                OSDCustomizeLevelComboBox.SelectedItem=OSDCustomizeLevelComboBox.Items.OfType<ComboBoxItem>().Single(i=>i.Tag as string=="4");await SetOnexOverlayLevelAsync(4);
                await Capture("vertical");cases.Add(new JsonObject{{"id",JsonValue.CreateStringValue("vertical")},{"level",JsonValue.CreateNumberValue(4)},{"graph",JsonValue.CreateBooleanValue(true)}});
                var item=osdItemViewModels.First(i=>i.Id=="TDPLimits");Check(item.Description.Contains("8 W")&&item.Description.Contains("15 W"),"APU explanation lacks the cap-versus-usage example");
                var help=ShowOnexOverlayItemHelpAsync(item);await Task.Delay(200);Check(_onexDetailsDialog!=null,"Metric explanation did not open");_onexDetailsDialog.Hide();await help;
                report["nativeCases"]=cases;report["uiSettingsSavedThroughRealHandlers"]=JsonValue.CreateBooleanValue(true);report["unknownSettingsPreserved"]=JsonValue.CreateBooleanValue(true);report["helpDialogOpened"]=JsonValue.CreateBooleanValue(true);report["programmaticUiInputs"]=JsonValue.CreateBooleanValue(true);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                _onexOverlayConfigReady=false;osdBackgroundDebounceTimer?.Stop();_onexOverlayPreviewDelay?.Stop();
                await SetConfig(original.GetNamedString("config"));await SetOnexOverlayLevelAsync((int)original.GetNamedNumber("level"));
                await ReadOnexOverlayConfigurationAsync();LoadOSDOptionsForLevel(originalEditor);
                OSDCustomizeLevelComboBox.SelectedItem=OSDCustomizeLevelComboBox.Items.OfType<ComboBoxItem>().First(i=>i.Tag as string==originalEditor.ToString());
                if(isOSDCustomizeExpanded!=originalExpanded)OSDCustomizeExpandButton_Click(OSDCustomizeExpandButton,null);
                foreach(string key in values.Keys.Where(k=>k.StartsWith("OSD")).ToArray())values.Remove(key);foreach(var pair in localBackup)values[pair.Key]=pair.Value;
                PerformanceNavItem.IsChecked=true;
                Check((await Preferences()).Stringify()==originalPrefs,"Original overlay preferences were not restored");report["originalOverlayPreferencesRestored"]=JsonValue.CreateBooleanValue(true);
            }
        }
    }
}
