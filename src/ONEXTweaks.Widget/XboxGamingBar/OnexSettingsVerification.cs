// SPDX-License-Identifier: AGPL-3.0-only
using System;
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
        private async Task VerifyOnexSettingsPanelsAsync(JsonObject report, bool ledOnly=false)
        {
            bool boost = _onexBoostOn, boostKnown = _onexBoostKnown, charge = _onexChargeOn, chargeKnown = _onexChargeKnown;
            var originalHardware = _onexHardwareHome;
            var states = new JsonArray();
            try
            {
                _onexBoostKnown = _onexChargeKnown = true;
                _onexHardwareHome = JsonObject.Parse(originalHardware.Stringify());
                foreach (bool on in new[] { false, true })
                {
                    _onexBoostOn = _onexChargeOn = on;
                    _onexHardwareHome.GetNamedObject("lighting")["enabled"] = JsonValue.CreateBooleanValue(on);
                    foreach(var zone in _onexHardwareHome.GetNamedObject("lighting").GetNamedArray("zones"))zone.GetObject()["enabled"]=JsonValue.CreateBooleanValue(on);
                    ApplyOnexTileAvailability();
                    foreach (string id in new[] { "CPUBoost", "ChargeLimiter", "LedToggle" })
                    {
                        var tile = qsTileMap[id];
                        var color = ((SolidColorBrush)tile.StateText.Foreground).Color;
                        var background = ((SolidColorBrush)tile.TileButton.Background).Color;
                        if (on ? color.R == 136 && color.G == 136 && color.B == 136 : color.R != 136 || color.G != 136 || color.B != 136)
                            throw new InvalidOperationException("Wrong On/Off foreground for " + id);
                        states.Add(new JsonObject { ["tile"] = JsonValue.CreateStringValue(id), ["on"] = JsonValue.CreateBooleanValue(on),
                            ["foreground"] = JsonValue.CreateStringValue(color.ToString()), ["background"] = JsonValue.CreateStringValue(background.ToString()) });
                    }
                }
            }
            finally
            {
                _onexBoostOn = boost; _onexBoostKnown = boostKnown; _onexChargeOn = charge; _onexChargeKnown = chargeKnown;
                _onexHardwareHome = originalHardware; ApplyOnexTileAvailability();
            }
            report["toggleVisuals"] = states;
            var panels = new JsonArray(); report["settingsPanels"] = panels;
            var originalLighting = JsonObject.Parse(_onexHardwareHome.GetNamedObject("lighting").Stringify());
            JsonObject allDraft = null;
            foreach (string id in ledOnly?new[] { "LedToggle" }:new[] { "CPUBoost", "ChargeLimiter", "LedToggle" })
            {
                var opening = ShowOnexTileDetailsAsync(qsTileMap[id].TileButton, id);
                for (int i = 0; i < 50 && _onexDetailsDialog == null; i++) await Task.Delay(100);
                if (_onexDetailsDialog == null) throw new InvalidOperationException("Settings did not open: " + id);
                try
                {
                    await Task.Delay(400); _onexDetailsDialog.UpdateLayout();
                    var scroll = (ScrollViewer)_onexDetailsDialog.Content;
                    panels.Add(new JsonObject { ["tile"] = JsonValue.CreateStringValue(id),
                        ["width"] = JsonValue.CreateNumberValue(scroll.ViewportWidth),
                        ["viewportHeight"] = JsonValue.CreateNumberValue(scroll.ViewportHeight),
                        ["contentHeight"] = JsonValue.CreateNumberValue(scroll.ExtentHeight),
                        ["scrollHeight"] = JsonValue.CreateNumberValue(scroll.ScrollableHeight) });
                    if (id == "CPUBoost")
                    {
                        var combos = FindOnexSettingsControls<ComboBox>(scroll).ToArray();
                        int[] selections = combos.Select(c => c.SelectedIndex).ToArray();
                        combos[0].SelectedIndex = combos[1].SelectedIndex = 6;
                        combos[2].SelectedIndex = combos[3].SelectedIndex = 1;
                        await Task.Delay(150); _onexDetailsDialog.UpdateLayout();
                        report["cpuLongOptionsScrollHeight"] = JsonValue.CreateNumberValue(scroll.ScrollableHeight);
                        if (scroll.ScrollableHeight > 1) throw new InvalidOperationException("CPU settings scroll with long selected labels");
                        for (int n = 0; n < combos.Length; n++) combos[n].SelectedIndex = selections[n];
                        await Task.Delay(100);
                    }
                    if (id != "LedToggle" && scroll.ScrollableHeight > 1) throw new InvalidOperationException("Settings require vertical scrolling: " + id);
                    if(id=="CPUBoost"&&FindOnexSettingsControls<Button>(scroll).Count(button=>button.Content as string=="?")<6)throw new InvalidOperationException("CPU help buttons missing");
                    await SaveOnexImage("onex-settings-" + id + ".png", _onexDetailsDialog);
                    if (id == "LedToggle")
                    {
                        if (_onexLightingDraft.GetNamedArray("zones").Stringify() != originalLighting.GetNamedArray("zones").Stringify())
                            throw new InvalidOperationException("Opening All changed the draft");
                        var palette = FindOnexSettingsNamedControl<Windows.UI.Xaml.Controls.Primitives.ColorSpectrum>(scroll, "OnexLightingPalette");
                        if (!_onexLightingAllZones.IsOn || _onexLightingDiagram.Zones.Count != 5)
                            throw new InvalidOperationException("All zones switch or schematic zones missing");
                        _onexLightingEffect.SelectedItem=_onexLightingEffect.Items.OfType<ComboBoxItem>().Single(item=>item.Tag is int mode&&mode==0);
                        palette.Color=Windows.UI.Color.FromArgb(255,18,116,216); await Task.Delay(100);
                        foreach (var value in _onexLightingDraft.GetNamedArray("zones"))
                        {
                            var z = value.GetObject();
                            if (z.GetNamedNumber("r") != 18 || z.GetNamedNumber("g") != 116 || z.GetNamedNumber("b") != 216 || z.GetNamedNumber("mode") != 2 || z.GetNamedBoolean("breathing"))
                                throw new InvalidOperationException("All color did not reach zone " + z.GetNamedNumber("id")+"; zone="+z.Stringify()+"; palette="+palette.Color.ToString());
                        }
                        allDraft = JsonObject.Parse(_onexLightingDraft.Stringify());
                        SelectLightingZoneForVerification(1);await Task.Delay(100);
                        var historyRow=FindOnexSettingsNamedControl<StackPanel>(scroll,"OnexRecentLedColors");
                        if(historyRow.Children.Count!=6||ToolTipService.GetToolTip(historyRow.Children[0]) as string!="#1274D8")throw new InvalidOperationException("Selected color did not become available in another zone");
                        report["recentColorsSharedBetweenZones"]=JsonValue.CreateBooleanValue(true);
                    }
                }
                finally { CloseOnexMenus(); await opening; }
            }
            if(!ledOnly)
            {
            if(qsTileDefinitions.Any(t=>t.Id=="MsiCenter"&&!ShouldSkipTile(t)))throw new InvalidOperationException("MSI Center survived in the UI");
            report["msiCenterRemoved"]=JsonValue.CreateBooleanValue(true);
            foreach(string id in new[]{"OnexFanMode","PowerMode"})
            {
                await ShowOnexTileDetailsAsync(qsTileMap[id].TileButton,id);await Task.Delay(150);
                var menu=_onexOpenMenus.OfType<Windows.UI.Xaml.Controls.MenuFlyout>().SingleOrDefault();
                if(menu==null||!menu.IsOpen||menu.Items.Count!=3)throw new InvalidOperationException("Hold menu did not open: "+id);
                CloseOnexMenus();if(menu.IsOpen)throw new InvalidOperationException("Menu survived overlay close");
            }
            var powerMenu=ShowOnexPowerMenu(qsTileMap["Power"].TileButton);await Task.Delay(100);CloseOnexMenus();
            report["fanAndPowerModeHoldMenus"]=JsonValue.CreateBooleanValue(true);
            report["menusCloseOnOverlayExit"]=JsonValue.CreateBooleanValue(true);
            int beforeMode=(int)_onexWindowsPower.GetNamedNumber(_onexWindowsPower.GetNamedBoolean("onAc",false)?"modeAc":"modeDc",1);
            var beforePower=await ReadOnexStateAsync();
            try
            {
                for(int step=1;step<=3;step++)
                {
                    await CycleOnexPowerModeAsync();
                    int actual=(int)_onexWindowsPower.GetNamedNumber(_onexWindowsPower.GetNamedBoolean("onAc",false)?"modeAc":"modeDc",1);
                    if(actual!=(beforeMode+step)%3)throw new InvalidOperationException("Power Mode did not cycle");
                }
            }
            finally{await SetOnexPowerModeAsync(beforeMode);}
            var afterPower=await ReadOnexStateAsync();
            if(beforePower.GetNamedNumber("pl1Watts")!=afterPower.GetNamedNumber("pl1Watts")||beforePower.GetNamedNumber("pl2Watts")!=afterPower.GetNamedNumber("pl2Watts"))throw new InvalidOperationException("Power mode cycle changed TDP");
            report["powerModeCycleAndRestore"]=JsonValue.CreateBooleanValue(true);
            }
            report["settingsPanels"] = panels;
            if (_onexHardwareHome.GetNamedObject("lighting").GetNamedArray("zones").Stringify() != originalLighting.GetNamedArray("zones").Stringify())
                throw new InvalidOperationException("Cancel changed device lighting");
            report["ledAllDraftAndCancel"] = JsonValue.CreateBooleanValue(true);
            bool hadHistory=Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("OnexRecentLedColors",out var originalHistory);
            try
            {
                var applyOpening=ShowOnexTileDetailsAsync(qsTileMap["LedToggle"].TileButton,"LedToggle");
                for(int i=0;i<50&&_onexDetailsDialog==null;i++)await Task.Delay(100);
                await Task.Delay(300);
                var palette=FindOnexSettingsNamedControl<Windows.UI.Xaml.Controls.Primitives.ColorSpectrum>(_onexDetailsDialog,"OnexLightingPalette");palette.Color=Windows.UI.Color.FromArgb(255,18,116,216);
                var applyButton=FindOnexSettingsControls<Button>(_onexDetailsDialog).First(button=>button.Content as string=="Apply");
                ((Windows.UI.Xaml.Automation.Provider.IInvokeProvider)new Windows.UI.Xaml.Automation.Peers.ButtonAutomationPeer(applyButton).GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                await applyOpening;
                var savedHistory=JsonArray.Parse((string)Windows.Storage.ApplicationData.Current.LocalSettings.Values["OnexRecentLedColors"]);
                if(savedHistory.Count<1||savedHistory.Count>6||savedHistory[0].GetString()!="#1274D8")throw new InvalidOperationException("Recent colors did not persist after Apply");
                report["recentColorsPersistAfterApply"]=JsonValue.CreateBooleanValue(true);
                foreach (var value in _onexHardwareHome.GetNamedObject("lighting").GetNamedArray("zones"))
                    if (value.GetObject().GetNamedNumber("b") != 216) throw new InvalidOperationException("All-zone OEM command was not acknowledged");
                report["ledAllFiveZonesAcknowledged"] = JsonValue.CreateBooleanValue(true);
                var allOff=JsonObject.Parse(allDraft.Stringify());allOff["lightingEnabled"]=JsonValue.CreateBooleanValue(false);foreach(var z in allOff.GetNamedArray("zones"))z.GetObject()["enabled"]=JsonValue.CreateBooleanValue(false);
                await SetOnexLightingAsync(allOff);await ToggleOnexLightingAsync();
                if(!OnexLightingIsOn)throw new InvalidOperationException("LED tile cannot re-enable all-disabled zones");
                await ToggleOnexLightingAsync();if(OnexLightingIsOn)throw new InvalidOperationException("LED tile cannot switch back off");
                report["ledTileRecoversFromAllOff"]=JsonValue.CreateBooleanValue(true);
            }
            finally
            {
                var restore = new JsonObject { ["lightingEnabled"] = originalLighting.GetNamedValue("enabled"), ["zones"] = originalLighting.GetNamedArray("zones") };
                var reply = await SendOnexHomeRequestAsync(new Windows.Foundation.Collections.ValueSet { ["OnexLightingSet"] = restore.Stringify() });
                ApplyOnexHardwareHomeState((string)reply["OnexHardwareHomeState"]);
                if (_onexHardwareHome.GetNamedObject("lighting").GetNamedArray("zones").Stringify() != originalLighting.GetNamedArray("zones").Stringify())
                    throw new InvalidOperationException("Lighting settings did not restore");
                if(hadHistory)Windows.Storage.ApplicationData.Current.LocalSettings.Values["OnexRecentLedColors"]=originalHistory;else Windows.Storage.ApplicationData.Current.LocalSettings.Values.Remove("OnexRecentLedColors");
                report["ledSettingsRestored"] = JsonValue.CreateBooleanValue(true);
            }
        }
        private static System.Collections.Generic.IEnumerable<T> FindOnexSettingsControls<T>(DependencyObject root) where T : FrameworkElement
        {
            if(root is T match)yield return match;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
                foreach(var child in FindOnexSettingsControls<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
        }
        private static T FindOnexSettingsNamedControl<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            if (root is T match && match.Name == name) return match;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            { var found = FindOnexSettingsNamedControl<T>(VisualTreeHelper.GetChild(root, i), name); if (found != null) return found; }
            return null;
        }
    }
}
