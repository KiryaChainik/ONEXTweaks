// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using XboxGamingBar.QuickSettings;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexTileRevisionAsync()
        {
            var folder=ApplicationData.Current.LocalFolder;var values=ApplicationData.Current.LocalSettings.Values;var report=new JsonObject{{"stage",JsonValue.CreateNumberValue(App.OnexTileRevisionStage)}};var backup=new JsonObject();string before=null;
            foreach(var pair in values.Where(x=>x.Key.StartsWith("QS_")||x.Key=="QuickSettingsTiles").OrderBy(x=>x.Key,StringComparer.Ordinal).ToArray())backup[pair.Key]=pair.Value is bool b?JsonValue.CreateBooleanValue(b):pair.Value is int n?JsonValue.CreateNumberValue(n):JsonValue.CreateStringValue(pair.Value.ToString());
            await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-tile-revision-backup.json",CreationCollisionOption.ReplaceExisting),backup.Stringify());
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-revision-progress.json"),"before initial power read; stage="+App.OnexTileRevisionStage);
            try
            {
                before=(await ReadOnexStateAsync()).GetNamedString("rawLimits");if(App.OnexTileRevisionStage!=249)PerformanceNavItem.IsChecked=true;
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-revision-progress.json"),"initial power read complete; stage="+App.OnexTileRevisionStage);
                if(App.OnexTileRevisionStage==1)
                {
                    report["startupKeyboardRecoveries"]=JsonValue.CreateNumberValue(_onexRecoveredKeyboardCount);
                    if(!qsTileMap.TryGetValue("Keyboard",out var keyboard)||CanRemoveOnexTile(keyboard))throw new InvalidOperationException("Keyboard is not protected");
                    QuickSettingsCustomize_Click(QuickSettingsCustomizeButton,null);await Task.Delay(250);
                    Func<Border> Card=()=>TileSortableGrid.Children.OfType<Grid>().SelectMany(row=>row.Children.OfType<Border>()).Single(b=>b.Tag as string=="Keyboard");
                    bool Trash()=>FindOnexSettingsControls<Button>(Card()).Any(b=>b.Content is FontIcon icon&&icon.Glyph=="\uE74D");
                    if(Trash())throw new InvalidOperationException("Keyboard still has a trash button");
                    SortableTileDelete_Click(new Button{Tag="Keyboard"},null);DeleteCustomShortcutTile(keyboard);
                    if(!qsTileMap.ContainsKey("Keyboard")||!qsTileDefinitions.Contains(keyboard))throw new InvalidOperationException("A delete handler removed built-in Keyboard");
                    UpdateSelectedTileIndicator(keyboard);if(DeleteSelectedTileButton.Visibility!=Visibility.Collapsed)throw new InvalidOperationException("Selected Keyboard exposes deletion");
                    QuickSettingsCustomizeDone_Click(QuickSettingsCustomizeCloseTopButton,null);
                    int position=qsTileDefinitions.Max(t=>t.Order)+10;var config=QuickSettingsConfig.Instance;var replacement=new QuickSettingsTile{Id=Guid.NewGuid().ToString(),Type=TileType.CustomShortcut,Name="ONEX keyboard recovery fixture",ActionType=TileActionType.ShowKeyboard,IsVisible=true,Order=position,ControllerHotkey=keyboard.ControllerHotkey};config.Tiles.Add(replacement);config.Save();values["QS_"+replacement.Id+"_Order"]=position;
                    DefineQuickSettingsTiles();LoadQuickSettingsConfig();RebuildQuickSettingsTiles();
                    if(qsTileMap["Keyboard"].Order!=position||config.GetTile(replacement.Id)!=null||qsCustomShortcuts.Any(t=>t.ActionType==TileActionType.ShowKeyboard)||CanRemoveOnexTile(qsTileMap["Keyboard"]))throw new InvalidOperationException("Keyboard replacement did not migrate to protected built-in");
                    config.Load();DefineQuickSettingsTiles();LoadQuickSettingsConfig();RebuildQuickSettingsTiles();
                    if(qsTileMap["Keyboard"].Order!=position||qsCustomShortcuts.Any(t=>t.ActionType==TileActionType.ShowKeyboard))throw new InvalidOperationException("Keyboard migration did not survive saved-settings reload");
                    QuickSettingsCustomize_Click(QuickSettingsCustomizeButton,null);await Task.Delay(200);var card=Card();FindOnexSettingsControls<Button>(card).First(b=>b.Tag as string=="Keyboard").Focus(FocusState.Programmatic);await Task.Delay(150);await SaveOnexImage("onex-keyboard-protected.png",card);QuickSettingsCustomizeDone_Click(QuickSettingsCustomizeCloseTopButton,null);
                    report["trashAbsent"]=JsonValue.CreateBooleanValue(true);report["deleteHandlersProtected"]=JsonValue.CreateBooleanValue(true);report["replacementMigrated"]=JsonValue.CreateBooleanValue(true);report["savedSettingsReload"]=JsonValue.CreateBooleanValue(true);
                }
                else if(App.OnexTileRevisionStage==2)
                {
                    var config=QuickSettingsConfig.Instance;string chord=Enumerable.Range(1,12).Select(n=>"Ctrl+Alt+Shift+F"+n).First(k=>!config.IsDuplicateOnexAction(TileActionType.None,shortcut:k));
                    int rejects=0;
                    void Reject(System.Action add)
                    {int count=config.Tiles.Count;bool rejected=false;try{add();}catch(InvalidOperationException){rejected=true;}if(!rejected||config.Tiles.Count!=count)throw new InvalidOperationException("Repeated tile was accepted");rejects++;}
                    Reject(()=>config.AddActionTile("duplicate keyboard",TileActionType.ShowKeyboard));Reject(()=>config.AddActionTile("duplicate overlay",TileActionType.CycleOverlayMode));Reject(()=>config.AddCustomTile("duplicate task manager","", "Control+Shift+Escape"));
                    var primary=config.AddCustomTile("ONEX identity fixture","",chord);
                    string reversed=string.Join(" + ",chord.Split('+').Reverse()).ToLowerInvariant().Replace("ctrl","control");Reject(()=>config.AddCustomTile("same shortcut different name","",reversed));
                    string unique=Guid.NewGuid().ToString("N");string programPath=@"C:\Programs\"+unique+";p|q.exe";
                    var program=config.AddActionTile("ONEX program fixture",TileActionType.LaunchUserProgram,programPath);Reject(()=>config.AddActionTile("same program",TileActionType.LaunchUserProgram,"c:/programs/tools/../"+unique+";p|q.exe"));
                    var differentProgram=config.AddActionTile("ONEX different program",TileActionType.LaunchUserProgram,programPath+".other.exe");
                    string url="https://example.invalid/ONEX-QA/"+unique;var website=config.AddActionTile("ONEX URL fixture",TileActionType.OpenUserWebsite,url);Reject(()=>config.AddActionTile("same URL",TileActionType.OpenUserWebsite,"HTTPS://EXAMPLE.INVALID:443/ONEX-QA/"+unique));
                    var differentWebsite=config.AddActionTile("ONEX different URL",TileActionType.OpenUserWebsite,"https://example.invalid/onex-qa/"+unique);
                    var importedDuplicate=new QuickSettingsTile{Id=Guid.NewGuid().ToString(),Type=TileType.CustomShortcut,Name="ONEX rejected import",CustomShortcut=reversed,IsVisible=true};config.Tiles.Add(importedDuplicate);string invalidExport=config.ExportConfig();config.Tiles.Remove(importedDuplicate);config.Save();string validExport=config.ExportConfig();
                    if(config.ImportConfig(invalidExport)||config.ExportConfig()!=validExport)throw new InvalidOperationException("Duplicate import changed existing tiles");
                    var legacy=new QuickSettingsTile{Id=Guid.NewGuid().ToString(),Type=TileType.CustomShortcut,Name="ONEX old duplicate fixture",CustomShortcut=reversed,IsVisible=false};config.Tiles.Add(legacy);config.Save();
                    DefineQuickSettingsTiles();LoadQuickSettingsConfig();RebuildQuickSettingsTiles();
                    if(config.GetTile(legacy.Id)!=null||config.GetTile(primary.Id)==null)throw new InvalidOperationException("Legacy duplicate was not merged into existing tile");
                    var repeated=qsTileDefinitions.Where(t=>!ShouldSkipTile(t)).Select(OnexTileIdentity).Where(k=>k.Length>0).GroupBy(k=>k).Any(g=>g.Count()>1);if(repeated)throw new InvalidOperationException("Repeated actions remain in tile grid");
                    QuickSettingsCustomize_Click(QuickSettingsCustomizeButton,null);FillActionComboBox(CustomActionTypeComboBox);
                    foreach(var action in new[]{TileActionType.ShowKeyboard,TileActionType.CycleOverlayMode})if(CustomActionTypeComboBox.Items.OfType<ComboBoxItem>().First(i=>i.Tag is ActionChoice c&&c.Type==action).IsEnabled)throw new InvalidOperationException("Repeated picker action remains selectable");
                    QuickSettingsCustomizeDone_Click(QuickSettingsCustomizeCloseTopButton,null);
                    if(!config.ImportConfig(config.ExportConfig())||config.GetTile(program.Id)?.ActionParam!=programPath||config.GetTile(differentProgram.Id)==null||config.GetTile(differentWebsite.Id)==null)throw new InvalidOperationException("Distinct action parameters or export metadata lost");
                    config.Load();DefineQuickSettingsTiles();LoadQuickSettingsConfig();RebuildQuickSettingsTiles();
                    if(qsCustomShortcuts.Count(t=>t.CustomShortcut!=null&&TileActionIdentity.Shortcut(t.CustomShortcut)==TileActionIdentity.Shortcut(chord))!=1||!qsTileMap.ContainsKey(primary.Id)||config.GetTile(primary.Id)==null||!qsTileMap.ContainsKey(program.Id)||config.GetTile(program.Id)==null)throw new InvalidOperationException("Deduplication did not survive reload");
                    report["addRejections"]=JsonValue.CreateNumberValue(rejects);report["atomicImportRejection"]=JsonValue.CreateBooleanValue(true);report["legacyDuplicatesMerged"]=JsonValue.CreateBooleanValue(true);report["duplicateChoicesDisabled"]=JsonValue.CreateBooleanValue(true);report["distinctParametersPreserved"]=JsonValue.CreateBooleanValue(true);report["savedSettingsReload"]=JsonValue.CreateBooleanValue(true);
                }
                else if(App.OnexTileRevisionStage==3)
                {
                    var config=QuickSettingsConfig.Instance;var actions=new[]{TileActionType.OpenChrome,TileActionType.SteamBpmSteamMenu,TileActionType.SteamBpmQuickAccess,TileActionType.SteamGameOverlay,TileActionType.SteamMenuContextual,TileActionType.EmulateXboxGuide};
                    // Isolate add/picker verification from the owner's existing actions.
                    foreach(var item in config.Tiles.Where(t=>t.Type==TileType.CustomShortcut&&actions.Contains(t.ActionType)).ToArray())config.Tiles.Remove(item);config.Save();DefineQuickSettingsTiles();LoadQuickSettingsConfig();RebuildQuickSettingsTiles();
                    QuickSettingsCustomize_Click(QuickSettingsCustomizeButton,null);FillActionComboBox(CustomActionTypeComboBox);
                    foreach(var action in actions)
                    {
                        var option=CustomActionTypeComboBox.Items.OfType<ComboBoxItem>().First(i=>i.Tag is ActionChoice choice&&choice.Type==action);
                        if(!option.IsEnabled||!IsOnexCustomActionSupported(new TileDefinition{ActionType=action}))throw new InvalidOperationException("Action remains disabled: "+action);
                        CustomActionTypeComboBox.SelectedItem=option;if(CustomActionTypeComboBox.SelectedItem!=option)throw new InvalidOperationException("Cannot select action: "+action);
                        config.AddActionTile("ONEX action fixture "+action,action);
                    }
                    QuickSettingsCustomizeDone_Click(QuickSettingsCustomizeCloseTopButton,null);config.Load();DefineQuickSettingsTiles();LoadQuickSettingsConfig();RebuildQuickSettingsTiles();
                    foreach(var action in actions)if(qsCustomShortcuts.Count(t=>t.ActionType==action)!=1)throw new InvalidOperationException("Action did not persist: "+action);
                    report["selectableActions"]=JsonArray.Parse("["+string.Join(",",actions.Select(a=>"\""+a.ToString()+"\""))+"]");report["savedSettingsReload"]=JsonValue.CreateBooleanValue(true);report["actionsExecuted"]=JsonValue.CreateBooleanValue(false);
                }
                else if(App.OnexTileRevisionStage==4)
                {
                    var config=QuickSettingsConfig.Instance;var custom=config.AddActionTile("ONEX very long custom action title for ellipsis verification",TileActionType.OpenUserWebsite,"https://example.invalid/"+Guid.NewGuid().ToString("N"));
                    DefineQuickSettingsTiles();LoadQuickSettingsConfig();foreach(var tile in qsTileDefinitions)tile.IsVisible=true;
                    qsColumnCount=5;RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();await Task.Delay(400);QuickSettingsTilesContainer.UpdateLayout();
                    var geometry=new JsonArray();double? iconCenter=null;
                    foreach(var tile in qsTileDefinitions.Where(t=>!ShouldSkipTile(t)&&t.IsVisible&&t.NameText!=null&&t.TileButton!=null))
                    {
                        var label=tile.NameText;var button=tile.TileButton;
                        if(label.TextWrapping!=TextWrapping.NoWrap||label.MaxLines!=1||label.ActualHeight>20.1||label.ActualHeight<15||label.TextTrimming!=TextTrimming.CharacterEllipsis)throw new InvalidOperationException("Wrapped/invalid title: "+tile.Id);
                        double center=tile.IconElement.TransformToVisual(button).TransformPoint(new Windows.Foundation.Point(0,tile.IconElement.ActualHeight/2)).Y;
                        if(iconCenter.HasValue&&Math.Abs(center-iconCenter.Value)>1)throw new InvalidOperationException("Shifted icon center: "+tile.Id+" "+center+" vs "+iconCenter);iconCenter=center;
                        geometry.Add(new JsonObject{{"id",JsonValue.CreateStringValue(tile.Id)},{"label",JsonValue.CreateStringValue(label.Text)},{"height",JsonValue.CreateNumberValue(label.ActualHeight)},{"iconCenter",JsonValue.CreateNumberValue(center)}});
                    }
                    if(qsTileMap["PowerMode"].NameText.Text!="Power Mode")throw new InvalidOperationException("Power Mode did not retain its full title");
                    await SaveOnexImage("onex-five-columns.png",QuickSettingsTilesContainer);
                    qsColumnCount=4;RebuildQuickSettingsTiles();await Task.Delay(200);if(qsTileMap["PowerMode"].NameText.Text!=qsTileMap["PowerMode"].Name)throw new InvalidOperationException("Full title did not return in four columns");
                    report["geometry"]=geometry;report["fiveColumnTitlesSingleLine"]=JsonValue.CreateBooleanValue(true);report["iconsAligned"]=JsonValue.CreateBooleanValue(true);report["fullTitleRestoredAtFourColumns"]=JsonValue.CreateBooleanValue(true);
                }
                else if(App.OnexTileRevisionStage==5)
                {
                    var config=QuickSettingsConfig.Instance;var added=config.AddActionTile("ONEX ordinary action fixture",TileActionType.OpenUserWebsite,"https://example.invalid/"+Guid.NewGuid().ToString("N"));
                    DefineQuickSettingsTiles();LoadQuickSettingsConfig();RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();await Task.Delay(200);
                    Func<TileDefinition,string> Color=t=>(t.StateText.Foreground as Windows.UI.Xaml.Media.SolidColorBrush)?.Color.ToString();
                    string normal=Color(qsTileMap["Power"]);var custom=qsTileMap[added.Id];if(custom.StateText.Text!="Run"||Color(custom)!=normal||Color(qsTileMap["Keyboard"])!=normal||Color(qsTileMap["ReShade"])!=normal||Color(qsTileMap["PowerMode"])!=normal)throw new InvalidOperationException("Ordinary/custom captions do not match Power Menu");
                    var legacyKeyboard=new TileDefinition{Id=Guid.NewGuid().ToString(),Name="Keyboard",ActionType=TileActionType.ShowKeyboard,IsAction=true,IsTrigger=true};CreateTileButton(legacyKeyboard);if(Color(legacyKeyboard)!=normal)throw new InvalidOperationException("New custom Keyboard starts with an accent caption");ApplyOnexTileCaption(legacyKeyboard,true);if(Color(legacyKeyboard)!=normal)throw new InvalidOperationException("Custom Keyboard caption is not neutral");
                    ApplyOnexTileCaption(custom,false);string inactive=Color(custom);if(inactive!="#FF707070")throw new InvalidOperationException("Unavailable caption is not dark gray");ApplyOnexTileCaption(custom,true);
                    bool oldBoost=_onexBoostOn;string off=null,on=null;
                    try{_onexBoostOn=false;ApplyOnexTileCaption(qsTileMap["CPUBoost"],true);off=Color(qsTileMap["CPUBoost"]);_onexBoostOn=true;ApplyOnexTileCaption(qsTileMap["CPUBoost"],true);on=Color(qsTileMap["CPUBoost"]);if(off!=inactive||on==normal||on==inactive)throw new InvalidOperationException("Toggle state colors do not distinguish active/off/ordinary");}
                    finally{_onexBoostOn=oldBoost;UpdateQuickSettingsTileStates();}
                    RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();if(Color(qsTileMap[added.Id])!=normal)throw new InvalidOperationException("Rebuild lost ordinary color");config.Load();DefineQuickSettingsTiles();LoadQuickSettingsConfig();RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();await Task.Delay(200);if(Color(qsTileMap[added.Id])!=normal)throw new InvalidOperationException("Reload lost ordinary color");
                    await SaveOnexImage("onex-semantic-captions.png",QuickSettingsTilesContainer);report["ordinary"]=JsonValue.CreateStringValue(normal);report["inactive"]=JsonValue.CreateStringValue(inactive);report["active"]=JsonValue.CreateStringValue(on);report["newCustomKeyboardNeutral"]=JsonValue.CreateBooleanValue(true);report["customRunNeutral"]=JsonValue.CreateBooleanValue(true);report["rebuildAndReload"]=JsonValue.CreateBooleanValue(true);report["hardwareTogglesExecuted"]=JsonValue.CreateBooleanValue(false);
                }
                else if(App.OnexTileRevisionStage==6)await VerifyOnexProfilePlacementAsync(report);
                else if(App.OnexTileRevisionStage==7)await VerifyOnexPowerModeAsync(report);
                else if(App.OnexTileRevisionStage==8)
                {
                    var geometry=new JsonArray();int index=0;
                    foreach(var tab in GetVisibleNavigationItems())
                    {
                        tab.IsChecked=true;tab.Focus(FocusState.Keyboard);await Task.Delay(200);tab.UpdateLayout();
                        foreach(bool selected in new[]{true,false})
                        {
                            tab.IsChecked=selected;await Task.Delay(150);tab.Focus(FocusState.Keyboard);tab.UpdateLayout();
                            var icon=(FrameworkElement)tab.Content;var focus=FindOnexNavFocusBorder(tab);var selection=(Border)FindDescendantByName(tab,"SelectionBackground");var frame=focus.TransformToVisual(tab).TransformBounds(new Windows.Foundation.Rect(0,0,focus.ActualWidth,focus.ActualHeight));var shape=icon.TransformToVisual(tab).TransformBounds(new Windows.Foundation.Rect(0,0,icon.ActualWidth,icon.ActualHeight));
                            double dx=shape.X+shape.Width/2-frame.X-frame.Width/2,dy=shape.Y+shape.Height/2-frame.Y-frame.Height/2;
                            if(Math.Abs(dx)>.4||Math.Abs(dy)>.4||Math.Abs(tab.ActualWidth-tab.ActualHeight)>.1||frame.X<-.1||frame.Y<-.1||frame.Right>tab.ActualWidth+.1||frame.Bottom>tab.ActualHeight+.1)throw new InvalidOperationException("Tab icon/frame not centered: "+tab.Tag+" "+dx+","+dy);
                            var pill=selection.TransformToVisual(tab).TransformBounds(new Windows.Foundation.Rect(0,0,selection.ActualWidth,selection.ActualHeight));if(selected&&(Math.Abs(pill.X+pill.Width/2-frame.X-frame.Width/2)>.4||Math.Abs(pill.Y+pill.Height/2-frame.Y-frame.Height/2)>.4))throw new InvalidOperationException("Selection pill not centered");
                            geometry.Add(new JsonObject{{"tab",JsonValue.CreateStringValue(tab.Tag.ToString())},{"selected",JsonValue.CreateBooleanValue(selected)},{"dx",JsonValue.CreateNumberValue(dx)},{"dy",JsonValue.CreateNumberValue(dy)},{"size",JsonValue.CreateNumberValue(tab.ActualWidth)}});
                            if(selected&&(index==0||index==1||index==OnexTabIds.Length-1))await SaveOnexImage("onex-tab-center-"+index+".png",MainNavPanel);
                        }
                        index++;
                    }
                    if(index!=OnexTabIds.Length)throw new InvalidOperationException("Expected all configured tabs");PerformanceNavItem.IsChecked=true;report["geometry"]=geometry;report["allTabsCentered"]=JsonValue.CreateBooleanValue(true);report["focusAndSelectionWithinBounds"]=JsonValue.CreateBooleanValue(true);
                }
                else if(App.OnexTileRevisionStage==9)await VerifyOnexPaletteAsync(report);
                else if(App.OnexTileRevisionStage==10)await VerifyOnexFseAsync(report);
                else if(App.OnexTileRevisionStage==11)await VerifyOnexWidthAsync(report);
                else if(App.OnexTileRevisionStage==12)await VerifyOnexPl2GamepadAsync(report);
                else if(App.OnexTileRevisionStage==13)await VerifyOnexNavigationFocusAsync(report);
                else if(App.OnexTileRevisionStage==14)await VerifyOnexTileXAsync(report);
                else if(App.OnexTileRevisionStage==15)await VerifyOnexLogoAsync(report);
                else if(App.OnexTileRevisionStage==16)await VerifyOnexNavInkAsync(report);
                else if(App.OnexTileRevisionStage==18)await VerifyOnexTriggersAsync(report);
                else if(App.OnexTileRevisionStage==19)await VerifyOnexToggleAlignmentAsync(report);
                else if(App.OnexTileRevisionStage==20)await VerifyOnexSliderAlignmentAsync(report);
                else if(App.OnexTileRevisionStage==22)await VerifyOnexHintAlignmentAsync(report);
                else if(App.OnexTileRevisionStage==23)await VerifyOnexHeaderConnectionAsync(report);
                else if(App.OnexTileRevisionStage==24)await VerifyOnexMediaIconsAsync(report);
                else if(App.OnexTileRevisionStage==25)await VerifyOnexFpsAsync(report);
                else if(App.OnexTileRevisionStage==26)await VerifyOnexChargingMetricsAsync(report);
                else if(App.OnexTileRevisionStage==27)await VerifyOnexSixColumnsAsync(report);
                else if(App.OnexTileRevisionStage==28){await VerifyOnexCompactMediaAsync(report);await VerifyOnexAudioMixerAsync(report);}
                else if(App.OnexTileRevisionStage==29)await VerifyOnexWifiAsync(report);
                else if(App.OnexTileRevisionStage==30)await VerifyOnexBluetoothAsync(report);
                else if(App.OnexTileRevisionStage==31)await VerifyOnexAirplaneAsync(report);
                else if(App.OnexTileRevisionStage==32)await VerifyOnexEnergySaverAsync(report);
                else if(App.OnexTileRevisionStage==34)await VerifyOnexAudioOutputAsync(report);
                else if(App.OnexTileRevisionStage==35)await VerifyOnexAudioInputAsync(report);
                else if(App.OnexTileRevisionStage==36)await VerifyOnexGpuLoadAsync(report);
                else if(App.OnexTileRevisionStage==37)await VerifyOnexTimeUnitsAsync(report);
                else if(App.OnexTileRevisionStage==39)await VerifyOnexBrightnessDetailsAsync(report);
                else if(App.OnexTileRevisionStage==40)await VerifyOnexPercentageStepsAsync(report);
                else if(App.OnexTileRevisionStage==41)await VerifyOnexBrightnessDimAsync(report);
                else if(App.OnexTileRevisionStage==42)await VerifyOnexPl2RestoreAsync(report);
                else if(App.OnexTileRevisionStage==43)await VerifyOnexWifiSettingsAsync(report);
                else if(App.OnexTileRevisionStage==44)await VerifyOnexPl2ProfileSyncAsync(report);
                else if(App.OnexTileRevisionStage==45)await VerifyOnexPerformanceLayoutAsync(report);
                else if(App.OnexTileRevisionStage==46)await VerifyOnexDisplayAsync(report);
                else if(App.OnexTileRevisionStage==47)await VerifyOnexBluetoothIdentityAsync(report);
                else if(App.OnexTileRevisionStage==49)await VerifyOnexInputResponsivenessAsync(report);
                else if(App.OnexTileRevisionStage==50)await VerifyOnexStartupAsync(report);
                else if(App.OnexTileRevisionStage==51)await VerifyOnexSliderEngagementAsync(report);
                else if(App.OnexTileRevisionStage==52)await VerifyOnexAllSliderInputsAsync(report);
                else if(App.OnexTileRevisionStage==53)await VerifyOnexSliderTransactionsAsync(report);
                else if(App.OnexTileRevisionStage==54)await VerifyOnexSliderTransactionsAsync(report,fixtureOnly:true);
                else if(App.OnexTileRevisionStage==55)await VerifyOnexActualSliderCommitsAsync(report);
                else if(App.OnexTileRevisionStage==56)await VerifyOnexFanLedIsolationAsync(report);
                else if(App.OnexTileRevisionStage==57)await VerifyOnexStandardTileLayoutAsync(report);
                else if(App.OnexTileRevisionStage==58)await VerifyOnexSharedSettingsAsync(report);
                else if(App.OnexTileRevisionStage==59)await VerifyOnexAwakeAsync(report);
                else if(App.OnexTileRevisionStage==60)await VerifyOnexBatteryUiAsync(report);
                else if(App.OnexTileRevisionStage==61){await VerifyOnexLatestDefaultLayoutAsync(report);await VerifyOnexReadableTileNamesAsync(report);}
                else if(App.OnexTileRevisionStage==62)await VerifyOnexTileIconPaletteAsync(report);
                else if(App.OnexTileRevisionStage==63)await VerifyOnexOverlayEditorAsync(report);
                else if(App.OnexTileRevisionStage==64)await VerifyOnexDisplayHdrAsync(report);
                else if(App.OnexTileRevisionStage==65)await VerifyOnexScreenProfilesAsync(report);
                else if(App.OnexTileRevisionStage==66)await VerifyOnexDisplayInputsAsync(report);
                else if(App.OnexTileRevisionStage==67)await VerifyOnexSharpnessUiAsync(report);
                else if(App.OnexTileRevisionStage==68)await VerifyOnexDisplayThumbAsync(report);
                else if(App.OnexTileRevisionStage==69)await VerifyOnexResolutionPlacementAsync(report);
                else if(App.OnexTileRevisionStage==70)await VerifyOnexScalingModesAsync(report);
                else if(App.OnexTileRevisionStage==71)await VerifyOnexScalingModesAsync(report,8);
                else if(App.OnexTileRevisionStage==72)
                {
                    await VerifyOnexLightingMixedTransitionAsync(report);
                    await VerifyOnexLedPrimaryPaletteAsync(report);
                }
                else if(App.OnexTileRevisionStage==73)await VerifyOnexDefaultLayoutButtonAsync(report);
                else if(App.OnexTileRevisionStage==74)await VerifyOnexPerformanceProfilesAsync(report);
                else if(App.OnexTileRevisionStage==75)await VerifyOnexPerformanceCardsAsync(report);
                else if(App.OnexTileRevisionStage==76)await VerifyOnexCpuAdvancedAsync(report);
                else if(App.OnexTileRevisionStage==77||App.OnexTileRevisionStage==78)await VerifyOnexProfileWorkflowAsync(report);
                else if(App.OnexTileRevisionStage==79)await VerifyOnexProfileSyncUiAsync(report);
                else if(App.OnexTileRevisionStage==80)await VerifyOnexOverlayRestoredAsync(report);
                else if(App.OnexTileRevisionStage==90)await VerifyOnexPageNavigationAsync(report);
                else if(App.OnexTileRevisionStage==91)await VerifyOnexPageSpacingAsync(report);
                else if(App.OnexTileRevisionStage==92)await VerifyOnexAccentConsistencyAsync(report);
                else if(App.OnexTileRevisionStage==93)await VerifyOnexConnectedCardsAsync(report);
                else if(App.OnexTileRevisionStage==94)await VerifyOnexCpuOutlineFirstFrameAsync(report);
                else if(App.OnexTileRevisionStage==95)await VerifyOnexFanSourceEditorsAsync(report);
                else if(App.OnexTileRevisionStage==96)await VerifyOnexFanSavedProfilesAsync(report);
                else if(App.OnexTileRevisionStage==97)await VerifyOnexScreenSavedProfilesAsync(report);
                else if(App.OnexTileRevisionStage==98)await VerifyOnexHomePerformancePickerAsync(report);
                else if(App.OnexTileRevisionStage==99)await VerifyOnexProfileSpacingAsync(report);
                else if(App.OnexTileRevisionStage==100)await VerifyOnexHomeFanPickerAsync(report);
                else if(App.OnexTileRevisionStage==101)await VerifyOnexHomeScreenPickerAsync(report);
                else if(App.OnexTileRevisionStage==102)await VerifyOnexHomeProfilePersistenceAsync(report);
                else if(App.OnexTileRevisionStage==103)await VerifyOnexCleanNavigationAsync(report);
                else if(App.OnexTileRevisionStage==104)await VerifyOnexOverlayPresetsAsync(report);
                else if(App.OnexTileRevisionStage==105)await VerifyOnexProfileListVisualsAsync(report);
                else if(App.OnexTileRevisionStage==106)await VerifyOnexControllerSavedListAsync(report);
                else if(App.OnexTileRevisionStage==107)await VerifyOnexControllerFirmwareReadAsync(report);
                else if(App.OnexTileRevisionStage==108)await VerifyOnexControllerSixAsync(report);
                else if(App.OnexTileRevisionStage==109)await VerifyOnexControllerDesignAsync(report);
                else if(App.OnexTileRevisionStage==110)await VerifyOnexControllerProfileModeAsync(report);
                else if(App.OnexTileRevisionStage==111)await VerifyOnexControllerDiagramAsync(report);
                else if(App.OnexTileRevisionStage==112)await RecoverOnexControllerDiagramQaAsync(report);
                else if(App.OnexTileRevisionStage==113)await VerifyOnexControllerGroupsAsync(report);
                else if(App.OnexTileRevisionStage==114)await VerifyOnexControllerVibrationAsync(report);
                else if(App.OnexTileRevisionStage==115)await VerifyOnexControllerActionsAsync(report);
                else if(App.OnexTileRevisionStage==116)await VerifyOnexControllerGesturesAsync(report);
                else if(App.OnexTileRevisionStage==117)await VerifyOnexControllerSteamAsync(report);
                else if(App.OnexTileRevisionStage==118)await VerifyOnexKeyboardMouseAsync(report);
                else if(App.OnexTileRevisionStage==119)await VerifyOnexKeyboardMouseModeAsync(report);
                else if(App.OnexTileRevisionStage==120)await VerifyOnexControllerSplitAsync(report);
                else if(App.OnexTileRevisionStage==121)await VerifyOnexControllerMacrosAsync(report);
                else if(App.OnexTileRevisionStage==122)await VerifyOnexRemappingWindowsAsync(report);
                else if(App.OnexTileRevisionStage==246)
                {
                    await VerifyOnexLightingMixedTransitionAsync(report);
                    await VerifyOnexLedPrimaryPaletteAsync(report);
                    await VerifyOnexControllerSketchAsync(report);
                    await VerifyOnexRotationLockTileAsync(report);
                    await VerifyOnexLightingPageAsync(report);
                }
                else if(App.OnexTileRevisionStage==123)await VerifyOnexKeyboardMouseIconAsync(report);
                else if(App.OnexTileRevisionStage==124)await VerifyOnexKeyboardMousePrecisionAsync(report);
                else if(App.OnexTileRevisionStage==251)await VerifyOnexPowerScaleAsync(report);
                else if(App.OnexTileRevisionStage==253)await VerifyOnexKeyboardLegendsAsync(report);
                else if(App.OnexTileRevisionStage==252)
                {
                    await VerifyOnexHomeRoutedPowerAsync(report);
                    await VerifyOnexKbmFnDeferredAsync(report);
                }
                else if(App.OnexTileRevisionStage==125)await VerifyOnexControllerTriggersAsync(report);
                else if(App.OnexTileRevisionStage==126)await VerifyOnexControllerLayoutAsync(report);
                else if(App.OnexTileRevisionStage==127)await VerifyOnexControllerRumbleProfilesAsync(report);
                else if(App.OnexTileRevisionStage==128)await VerifyOnexControllerMotionAsync(report);
                else if(App.OnexTileRevisionStage==134)await VerifyOnexKeyboardMouseIconAsync(report);
                else if(App.OnexTileRevisionStage==146)await VerifyOnexKeyboardMouseInputAsync(report);
                else if(App.OnexTileRevisionStage==147)await VerifyOnexKeyboardMouseInputAsync(report,true);
                else if(App.OnexTileRevisionStage==148)await VerifyOnexTabConfigurationAsync(report,false);
                else if(App.OnexTileRevisionStage==149)await VerifyOnexTabConfigurationAsync(report,true);
                else if(App.OnexTileRevisionStage==150)await VerifyOnexSettingsDesignAsync(report);
                else if(App.OnexTileRevisionStage==153)await VerifyOnexNavigationGuideAsync(report);
                else if(App.OnexTileRevisionStage==159)await VerifyOnexRetainedThemesAsync(report);
                else if(App.OnexTileRevisionStage==160)await VerifyOnexPopupThemesAsync(report);
                else if(App.OnexTileRevisionStage==163)await VerifyOnexCheckboxStabilityAsync(report);
                else if(App.OnexTileRevisionStage==164)await VerifyOnexDriverSettingsAsync(report);
                else if(App.OnexTileRevisionStage==165)await VerifyOnexSettingsSpacingAsync(report);
                else if(App.OnexTileRevisionStage==166)await VerifyOnexThemeSelectionAsync(report);
                else if(App.OnexTileRevisionStage==167)await VerifyOnexThemeSwitchSpeedAsync(report);
                else if(App.OnexTileRevisionStage==168)await VerifyOnexRichPalettesAsync(report);
                else if(App.OnexTileRevisionStage==188)await VerifyOnexLanguageApplyAsync(report);
                else if(App.OnexTileRevisionStage==187)await VerifyOnexLocalizationAsync(report);
                else if(App.OnexTileRevisionStage==189)await VerifyOnexFontPickerAsync(report);
                else if(App.OnexTileRevisionStage==190)await VerifyOnexStartupTabPickerAsync(report);
                else if(App.OnexTileRevisionStage==191)await VerifyOnexStartupTabPickerAsync(report,true);
                else if(App.OnexTileRevisionStage==192)await VerifyOnexScrollFocusAsync(report);
                else if(App.OnexTileRevisionStage==193)await VerifyOnexOverlayTilePickerAsync(report);
                else if(App.OnexTileRevisionStage==194)await VerifyOnexOverlayTilePersistenceAsync(report,false);
                else if(App.OnexTileRevisionStage==195)await VerifyOnexOverlayTilePersistenceAsync(report,true);
                else if(App.OnexTileRevisionStage==196)await VerifyOnexControllerHomeUiAsync(report);
                else if(App.OnexTileRevisionStage==197)await VerifyOnexControllerHomeLiveAsync(report,false);
                else if(App.OnexTileRevisionStage==198)await VerifyOnexControllerHomeLiveAsync(report,true);
                else if(App.OnexTileRevisionStage==199){await VerifyOnexDialogFramesAsync(report);await VerifyOnexKbmFnDeferredAsync(report);await VerifyOnexControllerBindingFirstUseAsync(report);await VerifyOnexSavedCatalogLifetimeAsync(report);}
                else if(App.OnexTileRevisionStage==200)await VerifyOnexNavigationResponseAsync(report);
                else if(App.OnexTileRevisionStage==202){await VerifyOnexControllerBindingFirstUseAsync(report);await VerifyOnexNavigationResponseAsync(report);}
                else if(App.OnexTileRevisionStage==203){await VerifyOnexSavedCatalogLifetimeAsync(report);await VerifyOnexNavigationResponseAsync(report);}
                else if(App.OnexTileRevisionStage==213)await VerifyOnexHomePowerAvailabilityAsync(report);
                else if(App.OnexTileRevisionStage==214)await VerifyOnexPl2ProductionActionsAsync(report);
                else if(App.OnexTileRevisionStage==215)await VerifyOnexPerformancePl2GestureAsync(report);
                else if(App.OnexTileRevisionStage==216)await VerifyOnexHiddenControllerProjectionAsync(report);
                else if(App.OnexTileRevisionStage==217)await VerifyOnexSnapshotTilePaintAsync(report);
                else if(App.OnexTileRevisionStage==218)await VerifyOnexPowerAvailabilityReuseAsync(report);
                else if(App.OnexTileRevisionStage==219)await VerifyOnexPowerSliderParityAsync(report);
                else if(App.OnexTileRevisionStage==223)await VerifyOnexApprovedThemesAsync(report);
                else if(App.OnexTileRevisionStage==222)await VerifyOnexPowerReleaseReadbackAsync(report,true);
                else if(App.OnexTileRevisionStage==221)await VerifyOnexPowerReleaseReadbackAsync(report);
                else if(App.OnexTileRevisionStage==220)await VerifyOnexHomeReleaseAckAsync(report);
                else if(App.OnexTileRevisionStage==212)await VerifyOnexStartupPickerReadbackAsync(report);
                else if(App.OnexTileRevisionStage==211)await ReadOnexNavigationInkAsync(report);
                else if(App.OnexTileRevisionStage==210)await VerifyOnexOverlayPreviewReuseAsync(report);
                else if(App.OnexTileRevisionStage==209)await VerifyOnexAboutContrastAsync(report);
                else if(App.OnexTileRevisionStage==208)await VerifyOnexChoiceContrastAsync(report);
                else if(App.OnexTileRevisionStage==207)await VerifyOnexControlSpacingAsync(report);
                else if(App.OnexTileRevisionStage==206)await VerifyOnexAppearanceChoiceRoutingAsync(report);
                else if(App.OnexTileRevisionStage==249)await VerifyWarmOpenLayoutAsync(report);
                else if(App.OnexTileRevisionStage==248)await VerifyAssignmentPickersAsync(report);
                else if(App.OnexTileRevisionStage==247){await VerifyOnexNativeChoiceFocusAsync(report);await VerifyOnexAppearanceChoiceRoutingAsync(report);await VerifyOnexHomeInputModeDetailsAsync(report);VerifyOnexTileEditorColumns(report);}
                else if(App.OnexTileRevisionStage==205)await VerifyOnexAppearanceChoiceInputAsync(report);
                else if(App.OnexTileRevisionStage==204)await VerifyOnexFocusBoundsAsync(report);
                else if(App.OnexTileRevisionStage==201)await VerifyOnexAboutAsync(report);
                else if(App.OnexTileRevisionStage==186)await VerifyOnexPowerReadoutAsync(report);
                else if(App.OnexTileRevisionStage==185)await VerifyOnexSettingsHeadersAsync(report);
                else if(App.OnexTileRevisionStage==184)await VerifyOnexSettingsOrderAsync(report);
                else if(App.OnexTileRevisionStage==183)await VerifyOnexOwnerPalettesAsync(report);
                else if(App.OnexTileRevisionStage==182)await VerifyOnexGlassAsync(report);
                else if((App.OnexTileRevisionStage==170||App.OnexTileRevisionStage==171||App.OnexTileRevisionStage==172||App.OnexTileRevisionStage==173||App.OnexTileRevisionStage==174||App.OnexTileRevisionStage==175||App.OnexTileRevisionStage==176||App.OnexTileRevisionStage==177||App.OnexTileRevisionStage==178||App.OnexTileRevisionStage==179||App.OnexTileRevisionStage==180||App.OnexTileRevisionStage==181))await VerifyOnexUiFontsAsync(report);
                else if(App.OnexTileRevisionStage==169)await VerifyOnexNavigationInkAsync(report);
                else if(App.OnexTileRevisionStage==161)await VerifyOnexSystemRegressionAsync(report);
                else if(App.OnexTileRevisionStage==158)await VerifyOnexThemeResponsivenessAsync(report);
                else if(App.OnexTileRevisionStage==154)await VerifyOnexAboutAsync(report);
                else if(App.OnexTileRevisionStage==155)await VerifyOnexDriverSettingsAsync(report);
                else if(App.OnexTileRevisionStage==156||App.OnexTileRevisionStage==157)await VerifyOnexDriverSettingsAsync(report,false);
                else if(App.OnexTileRevisionStage==151){await VerifyOnexThemeCatalogAsync(report,false);if(_onexThemeQaStore)throw new InvalidOperationException("Theme QA store escaped verification scope");report["scopedThemeStoreReleased"]=JsonValue.CreateBooleanValue(true);}
                else if(App.OnexTileRevisionStage==152){await VerifyOnexThemeCatalogAsync(report,true);if(_onexThemeQaStore)throw new InvalidOperationException("Theme QA store escaped restart verification scope");report["scopedThemeStoreReleased"]=JsonValue.CreateBooleanValue(true);}
                else if(App.OnexTileRevisionStage==145)await VerifyOnexControllerInputHealthAsync(report);
                else if(App.OnexTileRevisionStage==144)await VerifyOnexControllerCaptureGroupsAsync(report);
                else if(App.OnexTileRevisionStage==143)await VerifyOnexControllerTargetAsync(report);
                else if(App.OnexTileRevisionStage==142)await VerifyOnexDs4AuxiliaryAsync(report);
                else if(App.OnexTileRevisionStage==141)await VerifyOnexControllerTargetAsync(report);
                else if(App.OnexTileRevisionStage==140)await VerifyOnexControllerTargetAsync(report);
                else if(App.OnexTileRevisionStage==139)await VerifyOnexControllerTargetAsync(report);
                else if(App.OnexTileRevisionStage==138)await VerifyOnexControllerTargetAsync(report);
                else if(App.OnexTileRevisionStage==137)await VerifyOnexDs4NavigationAsync(report);
                else if(App.OnexTileRevisionStage==136)await VerifyOnexControllerGyroConversionAsync(report);
                else if(App.OnexTileRevisionStage==135)await VerifyOnexControllerGyroCurveAsync(report);
                else if(App.OnexTileRevisionStage==133)await VerifyOnexControllerGyroAxesAsync(report);
                else if(App.OnexTileRevisionStage==132)await VerifyOnexControllerGyroThresholdsAsync(report);
                else if(App.OnexTileRevisionStage==131)await VerifyOnexControllerGyroProcessingAsync(report);
                else if(App.OnexTileRevisionStage==130)await VerifyOnexControllerDiagnosticsAsync(report);
                else if(App.OnexTileRevisionStage==129)await VerifyOnexControllerGyroAsync(report);
                else if(App.OnexTileRevisionStage==81)await VerifyOnexSavedProfilesAsync(report);
                else if(App.OnexTileRevisionStage==82)await VerifyOnexPowerSourcesAsync(report);
                else if(App.OnexTileRevisionStage==83)await VerifyOnexFanEditorAsync(report);
                else if(App.OnexTileRevisionStage==84)await VerifyOnexRefreshRateTileAsync(report);
                else if(App.OnexTileRevisionStage==85)await VerifyOnexRefreshRateAppearanceAsync(report);
                else if(App.OnexTileRevisionStage==86)await VerifyOnexResolutionTileAsync(report);
                else if(App.OnexTileRevisionStage==87)await VerifyOnexResolutionAppearanceAsync(report);
                else if(App.OnexTileRevisionStage==88)await VerifyOnexTabPaletteAsync(report);
                else if(App.OnexTileRevisionStage==89)await VerifyOnexControllerAsync(report);
                else throw new InvalidOperationException("Verification stage not implemented yet");
                report["passed"]=JsonValue.CreateBooleanValue(true);
            }
            catch(Exception ex){report["passed"]=JsonValue.CreateBooleanValue(false);report["error"]=JsonValue.CreateStringValue(ex.ToString());}
            finally
            {
                try
                {
                    if(qsEditMode)QuickSettingsCustomizeDone_Click(QuickSettingsCustomizeCloseTopButton,null);
                    if(App.OnexTileRevisionStage<90)
                    {
                    foreach(string key in values.Keys.Where(k=>k.StartsWith("QS_")||k=="QuickSettingsTiles").ToArray())values.Remove(key);
                    foreach(var pair in backup){var v=pair.Value;values[pair.Key]=v.ValueType==JsonValueType.Boolean?(object)v.GetBoolean():v.ValueType==JsonValueType.Number?(object)(int)v.GetNumber():v.GetString();}
                    }
                    else
                    {
                        // Recent UI tests never edit layouts. Never replay an old snapshot
                        // over a layout the owner may have just changed during the test.
                        var current=new JsonObject();foreach(var pair in values.Where(x=>x.Key.StartsWith("QS_")||x.Key=="QuickSettingsTiles").OrderBy(x=>x.Key,StringComparer.Ordinal)){current[pair.Key]=pair.Value is bool flag?JsonValue.CreateBooleanValue(flag):pair.Value is int number?JsonValue.CreateNumberValue(number):JsonValue.CreateStringValue(pair.Value.ToString());}
                        report["layoutSnapshotReapplied"]=JsonValue.CreateBooleanValue(false);
                        if(current.Stringify()!=backup.Stringify())throw new InvalidOperationException("Layout changed during read-only UI test; fresh values retained");
                    }
                    QuickSettingsConfig.Instance.Load();DefineQuickSettingsTiles();LoadQuickSettingsConfig();RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();
                    var actual=await ReadOnexStateAsync();if(before!=null&&actual.GetNamedString("rawLimits")!=before)throw new InvalidOperationException("Tile verification changed power limits");report["rawLimitsPreserved"]=actual["rawLimits"];report["originalLayoutRestored"]=JsonValue.CreateBooleanValue(true);
                    await(await folder.GetFileAsync("onex-tile-revision-backup.json")).DeleteAsync();
                }
                catch(Exception ex){report["passed"]=JsonValue.CreateBooleanValue(false);report["restoreError"]=JsonValue.CreateStringValue(ex.ToString());}
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-tile-revision-"+App.OnexTileRevisionStage+".json",CreationCollisionOption.ReplaceExisting),report.Stringify());var armed=await folder.TryGetItemAsync("onex-focus-verification-armed.json");if(armed!=null)await armed.DeleteAsync();
            }
        }
    }
}
