// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Xaml;
using XboxGamingBar.QuickSettings;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private bool _onexDefaultLayoutBusy;
        private void QuickSettingsDefault_Click(object sender,RoutedEventArgs args)
        {
            if(!OnexFrontend.UsesOwnBackend||!qsEditMode||_onexDefaultLayoutBusy)return;
            _onexDefaultLayoutBusy=true;QuickSettingsDefaultButton.IsEnabled=false;
            try
            {
                ApplicationData.Current.LocalSettings.Values[OnexStandardLayoutRequest]=true;
                ApplyOnexRequestedStandardLayout();
                var applied=JsonObject.Parse(File.ReadAllText(Path.Combine(ApplicationData.Current.LocalFolder.Path,"onex-standard-layout-migration.json")));
                if(!applied.GetNamedBoolean("passed",false))throw new InvalidOperationException(applied.GetNamedString("error","Could not restore the standard tile layout"));
                qsSelectedTileForMove=null;UpdateSelectedTileIndicator(null);
                BuildSortableGrid();RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();
                QuickSettingsDefaultButton.Focus(FocusState.Programmatic);
            }
            catch(Exception failure){ShowOnexDesktopStatus(failure.Message);}
            finally{_onexDefaultLayoutBusy=false;QuickSettingsDefaultButton.IsEnabled=true;}
        }
        private async Task VerifyOnexDefaultLayoutButtonAsync(JsonObject report)
        {
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            string[] expected={"OnexWifi","OnexBluetooth","OnexAirplane","PowerMode","ChargeLimiter","CPUBoost","Profile","OnexFanMode","ReShade","LedToggle","OnexControllerProfile","OnexInputMode","FPSCombined","Overlay","OptiScaler","OnexFse","OnexRotationLock","Fullscreen","ActionTaskManager","ActionExplorer","Keyboard","ActionEndTask","OnexAwake","Power"};
            var config=QuickSettingsConfig.Instance;string chord=Enumerable.Range(1,12).Select(n=>"Ctrl+Alt+Shift+F"+n).First(k=>!config.IsDuplicateOnexAction(TileActionType.None,shortcut:k));
            var custom=config.AddCustomTile("ONEX Default preservation fixture","\uE768",chord,"#ABCDEF");custom.IsVisible=false;config.Save();
            DefineQuickSettingsTiles();LoadQuickSettingsConfig();RebuildQuickSettingsTiles();
            int priorColumns=qsColumnCount;qsColumnCount=priorColumns==6?5:6;int selectedColumns=qsColumnCount;
            int first=qsTileMap["OnexWifi"].Order;qsTileMap["OnexWifi"].Order=qsTileMap["OnexBluetooth"].Order;qsTileMap["OnexBluetooth"].Order=first;qsTileMap["LedToggle"].IsVisible=false;SaveQuickSettingsConfig();
            string metadata=OnexCustomMetadata(),hotkeys=string.Join("|",qsTileDefinitions.OrderBy(t=>t.Id).Select(t=>t.Id+":"+t.ControllerHotkey));
            QuickSettingsCustomize_Click(QuickSettingsCustomizeButton,null);await Task.Delay(200);UpdateLayout();
            Check(QuickSettingsDefaultButton.Visibility==Visibility.Visible&&QuickSettingsDefaultButton.IsEnabled&&QuickSettingsDefaultButton.ActualWidth>=100,"Default button is missing or clipped");
            qsSelectedTileForMove=qsTileMap["OnexWifi"];UpdateSelectedTileIndicator(qsSelectedTileForMove);
            ((Windows.UI.Xaml.Automation.Provider.IInvokeProvider)new Windows.UI.Xaml.Automation.Peers.ButtonAutomationPeer(QuickSettingsDefaultButton).GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();await Task.Delay(150);UpdateLayout();
            var actual=qsTileDefinitions.Where(t=>t.IsVisible&&!ShouldSkipTile(t)).OrderBy(t=>t.Order).Take(OnexFirstInstallTileIds.Length).Select(t=>t.Id).ToArray();
            Check(actual.SequenceEqual(expected),"Default did not restore the requested24 standard tiles");
            Check(qsEditMode&&QuickSettingsCustomizePanel.Visibility==Visibility.Visible&&qsSelectedTileForMove==null,"Default closed the editor or retained a stale move selection");
            Check(qsColumnCount==selectedColumns&&OnexCustomMetadata()==metadata&&config.GetTile(custom.Id)!=null&&!config.GetTile(custom.Id).IsVisible,"Default changed columns or deleted/customized owner tile metadata");
            Check(hotkeys==string.Join("|",qsTileDefinitions.OrderBy(t=>t.Id).Select(t=>t.Id+":"+t.ControllerHotkey)),"Default changed tile bindings");
            Check(qsTileMap[custom.Id].Order>=OnexFirstInstallTileIds.Length,"Custom tile is interleaved with the standard order");
            var capture=SaveOnexImage("onex-layout-default191.png",QuickSettingsCustomizeActions,0);bool captured=await Task.WhenAny(capture,Task.Delay(3000))==capture;if(captured)await capture;
            QuickSettingsCustomizeDone_Click(QuickSettingsCustomizeCloseTopButton,null);config.Load();DefineQuickSettingsTiles();LoadQuickSettingsConfig();RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();
            Check(qsColumnCount==selectedColumns&&qsTileDefinitions.Where(t=>t.IsVisible&&!ShouldSkipTile(t)).OrderBy(t=>t.Order).Take(OnexFirstInstallTileIds.Length).Select(t=>t.Id).SequenceEqual(expected)&&config.GetTile(custom.Id)!=null,"Default result did not survive saved reload");
            report["defaultButtonInvoked"]=JsonValue.CreateBooleanValue(true);report["swappedOrderAndHiddenLedRestored"]=JsonValue.CreateBooleanValue(true);report["customMetadataHotkeysAndColumnsPreserved"]=JsonValue.CreateBooleanValue(true);report["editorRemainedOpenAndSelectionCleared"]=JsonValue.CreateBooleanValue(true);report["savedReloadPassed"]=JsonValue.CreateBooleanValue(true);report["captureCompleted"]=JsonValue.CreateBooleanValue(captured);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
        }
        private const string OnexStandardLayoutRequest="OnexApplyStandardTileOrderV1";
        private static bool IsOnexLayoutSetting(string key)=>key.StartsWith("QS_")||key=="QuickSettingsTiles";
        private string OnexCustomMetadata()
        {
            var values=new JsonArray();
            foreach(var tile in QuickSettingsConfig.Instance.Tiles.Where(t=>t.Type==TileType.CustomShortcut).OrderBy(t=>t.Id))
                values.Add(JsonValue.CreateStringValue(string.Join("\u001f",new[]{tile.Id,tile.Name,tile.Icon,tile.CurrentState.ToString(),tile.Type.ToString(),tile.CustomShortcut,tile.CustomColor,tile.ActionType.ToString(),tile.ControllerHotkey,tile.ActionParam,tile.IsVisible.ToString()})));
            return values.Stringify();
        }
        private void ApplyOnexRequestedStandardLayout()
        {
            var values=ApplicationData.Current.LocalSettings.Values;
            if(!values.TryGetValue(OnexStandardLayoutRequest,out object armed)||!(armed is bool requested)||!requested)return;
            var saved=values.Where(p=>IsOnexLayoutSetting(p.Key)).ToDictionary(p=>p.Key,p=>p.Value);
            var report=new JsonObject{{"passed",JsonValue.CreateBooleanValue(false)}};
            var backup=new JsonObject();
            foreach(var pair in saved)backup[pair.Key]=pair.Value is bool b?JsonValue.CreateBooleanValue(b):pair.Value is int n?JsonValue.CreateNumberValue(n):JsonValue.CreateStringValue(pair.Value.ToString());
            string path=ApplicationData.Current.LocalFolder.Path;
            try
            {
                File.WriteAllText(Path.Combine(path,"onex-standard-layout-before-"+DateTime.UtcNow.Ticks+".json"),backup.Stringify());
                string custom=OnexCustomMetadata();
                string hotkeys=string.Join("|",qsTileDefinitions.OrderBy(t=>t.Id).Select(t=>t.Id+":"+t.ControllerHotkey));
                var remaining=qsTileDefinitions.Where(t=>!OnexFirstInstallTileIds.Contains(t.Id)).OrderBy(t=>t.Order).ThenBy(t=>t.Id).ToArray();
                for(int i=0;i<OnexFirstInstallTileIds.Length;i++)
                {
                    if(!qsTileMap.TryGetValue(OnexFirstInstallTileIds[i],out var tile))throw new InvalidOperationException("Standard tile missing: "+OnexFirstInstallTileIds[i]);
                    tile.Order=i;tile.IsVisible=true;
                }
                for(int i=0;i<remaining.Length;i++)remaining[i].Order=OnexFirstInstallTileIds.Length+i;
                SaveQuickSettingsConfig();
                foreach(var tile in qsTileDefinitions)
                    if(!(values["QS_"+tile.Id+"_Order"] is int order)||order!=tile.Order||!(values["QS_"+tile.Id+"_Visible"] is bool visible)||visible!=tile.IsVisible)throw new IOException("Tile layout was not saved");
                if(custom!=OnexCustomMetadata()||hotkeys!=string.Join("|",qsTileDefinitions.OrderBy(t=>t.Id).Select(t=>t.Id+":"+t.ControllerHotkey)))throw new InvalidOperationException("Custom metadata/hotkeys changed");
                report["passed"]=JsonValue.CreateBooleanValue(true);
                report["standardCount"]=JsonValue.CreateNumberValue(OnexFirstInstallTileIds.Length);
                report["customCount"]=JsonValue.CreateNumberValue(qsCustomShortcuts.Count);
                report["customMetadataAndVisibilityPreserved"]=JsonValue.CreateBooleanValue(true);
                report["hotkeysPreserved"]=JsonValue.CreateBooleanValue(true);
                values["OnexStandardTileOrderAppliedV1"]=DateTime.UtcNow.ToString("o");
            }
            catch(Exception error)
            {
                foreach(string key in values.Keys.Where(IsOnexLayoutSetting).ToArray())values.Remove(key);
                foreach(var pair in saved)values[pair.Key]=pair.Value;
                report["error"]=JsonValue.CreateStringValue(error.Message);
                QuickSettingsConfig.Instance.Load();
                foreach(var tile in qsTileDefinitions)
                {
                    if(saved.TryGetValue("QS_"+tile.Id+"_Order",out object order)&&order is int n)tile.Order=n;
                    if(saved.TryGetValue("QS_"+tile.Id+"_Visible",out object visibility)&&visibility is bool b)tile.IsVisible=b;
                }
            }
            finally
            {
                values.Remove(OnexStandardLayoutRequest);
                File.WriteAllText(Path.Combine(path,"onex-standard-layout-migration.json"),report.Stringify());
            }
        }
        private async Task VerifyOnexLatestDefaultLayoutAsync(JsonObject report)
        {
            // Independent owner requirement; do not use the production template as its own oracle.
            string[] expected={"OnexWifi","OnexBluetooth","OnexAirplane","PowerMode","ChargeLimiter","CPUBoost","Profile","OnexFanMode","ReShade","LedToggle","OnexControllerProfile","OnexInputMode","FPSCombined","Overlay","OptiScaler","OnexFse","OnexRotationLock","Fullscreen","ActionTaskManager","ActionExplorer","Keyboard","ActionEndTask","OnexAwake","Power"};
            var values=ApplicationData.Current.LocalSettings.Values;
            var saved=values.Where(p=>IsOnexLayoutSetting(p.Key)).ToDictionary(p=>p.Key,p=>p.Value);
            string Fingerprint()=>string.Join("|",qsTileDefinitions.OrderBy(t=>t.Id).Select(t=>t.Id+":"+t.Order+":"+t.IsVisible+":"+t.ControllerHotkey));
            string original=Fingerprint(),custom=OnexCustomMetadata();
            ApplyOnexFirstInstallLayout();
            if(Fingerprint()!=original||saved.Count!=values.Count(p=>IsOnexLayoutSetting(p.Key))||saved.Any(p=>!values.ContainsKey(p.Key)||!Equals(values[p.Key],p.Value)))throw new InvalidOperationException("New defaults overwrite an existing saved layout");
            report["existingLayoutNotOverwrittenByDefaults"]=JsonValue.CreateBooleanValue(true);
            try
            {
                foreach(string key in saved.Keys)values.Remove(key);
                QuickSettingsConfig.Instance.Load();DefineQuickSettingsTiles();LoadQuickSettingsConfig();RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();await Task.Delay(150);UpdateLayout();
                void CheckOrder()
                {
                    var actual=qsTileDefinitions.Where(t=>t.IsVisible&&!ShouldSkipTile(t)).OrderBy(t=>t.Order).ToArray();
                    if(!actual.Select(t=>t.Id).SequenceEqual(expected))throw new InvalidOperationException("First-install default does not match the requested23 tiles");
                    double y=-1,x=-1;
                    foreach(var tile in actual)
                    {
                        var button=tile.TileButton;if(button==null||button.ActualWidth<=0||button.ActualHeight<=0)throw new InvalidOperationException("Default tile is not rendered: "+tile.Id);
                        var p=button.TransformToVisual(QuickSettingsTilesContainer).TransformPoint(new Point());
                        if(p.Y<y-.5||(Math.Abs(p.Y-y)<.5&&p.X<=x))throw new InvalidOperationException("Default rendered row/column order mismatch: "+tile.Id);
                        y=p.Y;x=p.X;
                    }
                }
                CheckOrder();
                QuickSettingsConfig.Instance.Load();DefineQuickSettingsTiles();LoadQuickSettingsConfig();RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();await Task.Delay(150);UpdateLayout();CheckOrder();
                var order=new JsonArray();foreach(string id in expected)order.Add(JsonValue.CreateStringValue(id));report["latestDefaultRenderedOrder"]=order;
                report["isolatedFreshSettingsAndReloadPassed"]=JsonValue.CreateBooleanValue(true);
                report["freshWindowsAccountTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                foreach(string key in values.Keys.Where(IsOnexLayoutSetting).ToArray())values.Remove(key);
                foreach(var pair in saved)values[pair.Key]=pair.Value;
                QuickSettingsConfig.Instance.Load();DefineQuickSettingsTiles();LoadQuickSettingsConfig();RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();await Task.Delay(150);UpdateLayout();
            }
            if(Fingerprint()!=original||OnexCustomMetadata()!=custom||saved.Count!=values.Count(p=>IsOnexLayoutSetting(p.Key))||saved.Any(p=>!values.ContainsKey(p.Key)||!Equals(values[p.Key],p.Value)))throw new InvalidOperationException("Default-layout QA did not restore owner layout/metadata");
            report["latestDefaultQaRestoredOwnerLayout"]=JsonValue.CreateBooleanValue(true);
        }
        private async Task VerifyOnexStandardTileLayoutAsync(JsonObject report)
        {
            string Order()=>string.Join("|",qsTileDefinitions.Where(t=>t.IsVisible&&!ShouldSkipTile(t)).OrderBy(t=>t.Order).Select(t=>t.Id));
            void CheckStandard()
            {
                var actual=qsTileDefinitions.Where(t=>t.IsVisible&&!ShouldSkipTile(t)).OrderBy(t=>t.Order).Take(OnexFirstInstallTileIds.Length).ToArray();
                if(!actual.Select(t=>t.Id).SequenceEqual(OnexFirstInstallTileIds))throw new InvalidOperationException("Visible standard tile order mismatch");
                double y=-1,x=-1;
                foreach(var tile in actual)
                {
                    var button=tile.TileButton;
                    if(button==null||button.ActualWidth<=0||button.ActualHeight<=0)throw new InvalidOperationException("Standard tile is not rendered: "+tile.Id);
                    var p=button.TransformToVisual(QuickSettingsTilesContainer).TransformPoint(new Point());
                    if(p.Y<y-.5||(Math.Abs(p.Y-y)<.5&&p.X<=x))throw new InvalidOperationException("Rendered tile sequence mismatch: "+tile.Id);
                    y=p.Y;x=p.X;
                }
            }
            PerformanceNavItem.IsChecked=true;UpdateLayout();CheckStandard();
            string original=Order(),custom=OnexCustomMetadata();
            QuickSettingsConfig.Instance.Load();DefineQuickSettingsTiles();LoadQuickSettingsConfig();RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();await Task.Delay(150);UpdateLayout();
            CheckStandard();if(Order()!=original||OnexCustomMetadata()!=custom)throw new InvalidOperationException("Standard order/custom metadata did not survive reload");
            report["renderedStandardOrder"]=new JsonArray();foreach(string id in OnexFirstInstallTileIds)report.GetNamedArray("renderedStandardOrder").Add(JsonValue.CreateStringValue(id));
            report["savedLayoutReloadPassed"]=JsonValue.CreateBooleanValue(true);
            var values=ApplicationData.Current.LocalSettings.Values;var saved=values.Where(p=>IsOnexLayoutSetting(p.Key)).ToDictionary(p=>p.Key,p=>p.Value);
            try
            {
                foreach(string key in saved.Keys)values.Remove(key);
                QuickSettingsConfig.Instance.Load();DefineQuickSettingsTiles();LoadQuickSettingsConfig();RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();await Task.Delay(150);UpdateLayout();
                CheckStandard();report["isolatedFreshSettingsRenderedOrderPassed"]=JsonValue.CreateBooleanValue(true);
            }
            finally
            {
                foreach(string key in values.Keys.Where(IsOnexLayoutSetting).ToArray())values.Remove(key);
                foreach(var pair in saved)values[pair.Key]=pair.Value;
                QuickSettingsConfig.Instance.Load();DefineQuickSettingsTiles();LoadQuickSettingsConfig();RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();await SendTileHotkeysToHelper();await Task.Delay(150);UpdateLayout();
            }
            CheckStandard();if(Order()!=original||OnexCustomMetadata()!=custom)throw new InvalidOperationException("Fresh-settings check did not restore owner layout");
            report["ownerLayoutAndCustomMetadataRestored"]=JsonValue.CreateBooleanValue(true);
            report["customCount"]=JsonValue.CreateNumberValue(qsCustomShortcuts.Count);
            report["columns"]=JsonValue.CreateNumberValue(qsColumnCount);
        }
    }
}
