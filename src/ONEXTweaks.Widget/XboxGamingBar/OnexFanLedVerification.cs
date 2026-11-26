using Windows.UI.Xaml.Controls;
﻿// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using ONEXTweaks.Lighting;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using XboxGamingBar.QuickSettings;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private void SelectLightingZoneForVerification(int id)
        {
            _onexLightingAllZones.IsOn = false;
            InvokeLightingButton(_onexLightingDiagram.Zones[id]);
        }

        private static void InvokeLightingButton(Button button)
        {
            var peer = new Windows.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
            var invoke = (Windows.UI.Xaml.Automation.Provider.IInvokeProvider)
                peer.GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.Invoke);
            invoke.Invoke();
        }

        private async Task VerifyOnexLightingMixedTransitionAsync(JsonObject report)
        {
            var response = await SendOnexHomeRequestAsync(new ValueSet { { "OnexHardwareHomeGet", true } });
            ApplyOnexHardwareHomeState((string)response["OnexHardwareHomeState"]);
            string liveLighting = _onexHardwareHome.GetNamedObject("lighting").Stringify();
            var realHome = _onexHardwareHome;
            var fixture = JsonObject.Parse(realHome.Stringify());
            var fixtureLighting = fixture.GetNamedObject("lighting");
            fixtureLighting["enabled"] = JsonValue.CreateBooleanValue(true);
            foreach (var item in fixtureLighting.GetNamedArray("zones"))
            {
                var zone = item.GetObject();
                int id = (int)zone.GetNamedNumber("id");
                zone["enabled"] = JsonValue.CreateBooleanValue(id == 1 || id == 6);
                zone["brightness"] = JsonValue.CreateNumberValue(id == 1 ? 2 : 4);
                SetLightingColor(zone, id == 1 ? Windows.UI.Colors.CornflowerBlue : Windows.UI.Colors.Gold);
                SetLightingMode(zone, id == 1 ? 13 : LightingEffects.Solid);
            }
            Task opening;
            _onexHardwareHome = fixture;
            try { opening = ShowOnexLightingSettingsAsync(qsTileMap["LedToggle"].TileButton); }
            finally { _onexHardwareHome = realHome; }

            try
            {
                for (int n = 0; n < 40 && _onexDetailsDialog == null; n++) await Task.Delay(50);
                if (_onexDetailsDialog == null || _onexLightingAllZones.IsOn)
                    throw new InvalidOperationException("The editor unexpectedly activated the individually disabled zones.");
                _onexLightingAllZones.IsOn = true;
                await Task.Delay(150);
                var draftZones = _onexLightingDraft.GetNamedArray("zones").Select(item => item.GetObject()).ToArray();
                if (!_onexLightingDraft.GetNamedBoolean("lightingEnabled") || draftZones.Any(zone => !zone.GetNamedBoolean("enabled")))
                    throw new InvalidOperationException("All Zones left individually disabled zones off.");
                if (_onexLightingEffect.Items.Count != 2 || _onexLightingEffect.SelectedIndex != 0 ||
                    draftZones.Any(zone => LightingMode(zone) != LightingEffects.Solid ||
                        LightingRgb(zone) != 0x6495ED || zone.GetNamedNumber("brightness") != 2))
                    throw new InvalidOperationException("All Zones did not synchronize the selected color, brightness and common mode.");
                report["allZonesEnabledPreviouslyDisabled"] = JsonValue.CreateBooleanValue(true);
                var groupModes = _onexLightingEffect.Items.OfType<ComboBoxItem>().ToArray();
                _onexLightingEffect.SelectedItem = groupModes.Single(item => (int)item.Tag == AllZonesBreathing);
                if (draftZones.Any(zone => LightingRgb(zone) != 0x6495ED || zone.GetNamedNumber("brightness") != 2 ||
                    LightingMode(zone) != (LightingEffects.SupportsFactoryBreathing((int)zone.GetNamedNumber("id"))
                        ? LightingEffects.Breathing : LightingEffects.Solid)))
                    throw new InvalidOperationException("All Zones Breathing did not retain one color and restrict breathing to G and the light bar.");
                report["allZonesBreathingSharedColorPassed"] = JsonValue.CreateBooleanValue(true);
                _onexLightingEffect.SelectedItem = groupModes.Single(item => (int)item.Tag == LightingEffects.Solid);
                SelectLightingZoneForVerification(5);
                await Task.Delay(80);
                var picker = _onexLightingEffect;
                var options = picker.Items.OfType<ComboBoxItem>().ToArray();
                picker.SelectedItem = options.Single(item => item.Tag is int mode && mode == LightingEffects.Breathing);
                if (LightingMode(draftZones.Single(zone => zone.GetNamedNumber("id") == 5)) != LightingEffects.Breathing ||
                    draftZones.Where(zone => zone.GetNamedNumber("id") != 5).Any(zone => LightingMode(zone) != LightingEffects.Solid))
                    throw new InvalidOperationException("Selected-color Breathing changed a different lighting zone.");
                SelectLightingZoneForVerification(1);
                await Task.Delay(80);
                var ringModes = picker.Items.OfType<ComboBoxItem>().Select(item => (int)item.Tag).ToArray();
                if (ringModes.Contains(LightingEffects.Breathing) || !ringModes.Contains(LightingEffects.ChromaBreathing))
                    throw new InvalidOperationException("Ring effect options misrepresent selected-color Breathing.");
                report["unifiedLightingModesPassed"] = JsonValue.CreateBooleanValue(true);
            }
            finally { CloseOnexMenus(); await opening; }

            response = await SendOnexHomeRequestAsync(new ValueSet { { "OnexHardwareHomeGet", true } });
            ApplyOnexHardwareHomeState((string)response["OnexHardwareHomeState"]);
            if (_onexHardwareHome.GetNamedObject("lighting").Stringify() != liveLighting)
                throw new InvalidOperationException("The canceled transition changed live lighting settings.");
            report["mixedBreathingCancelPreservedLighting"] = JsonValue.CreateBooleanValue(true);
        }

        private async Task VerifyOnexLedPrimaryPaletteAsync(JsonObject report)
        {
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            var response=await SendOnexHomeRequestAsync(new ValueSet{{"OnexHardwareHomeGet",true}});ApplyOnexHardwareHomeState((string)response["OnexHardwareHomeState"]);
            string original=_onexHardwareHome.GetNamedObject("lighting").Stringify();var settings=Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            bool hadHistory=settings.TryGetValue("OnexRecentLedColors",out object history);var opening=ShowOnexLightingSettingsAsync(qsTileMap["LedToggle"].TileButton);
            try
            {
                for(int n=0;n<40&&_onexLightingPrimaryRow==null;n++)await Task.Delay(100);Check(_onexLightingPrimaryRow!=null,"Pure RGB row is missing");await Task.Delay(150);
                _onexLightingAllZones.IsOn = true;
                var modeGrid = (Grid)_onexLightingEffect.Parent;
                var captions = modeGrid.Children.OfType<TextBlock>().OrderBy(Grid.GetColumn).ToArray();
                Check(captions.Length == 2 && captions[0].Text == OnexStrings.Get("Mode") &&
                      captions[1].Text == OnexStrings.Get("Color"), "The lighting field captions are missing.");
                modeGrid.UpdateLayout();
                Windows.Foundation.Rect Bounds(Windows.UI.Xaml.FrameworkElement element) =>
                    element.TransformToVisual(modeGrid).TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
                var modeBounds = Bounds(_onexLightingEffect);
                var swatch = Bounds(_onexLightingColorButton);
                var color = Bounds((StackPanel)_onexLightingColorButton.Parent);
                Check(modeBounds.Width > 0 && swatch.Width > 0 && captions.All(caption => caption.ActualHeight > 0) &&
                      Math.Abs(Bounds(captions[0]).Top - Bounds(captions[1]).Top) < 1 &&
                      Math.Abs(Bounds(captions[0]).Left - modeBounds.Left) < 1 &&
                      Math.Abs(Bounds(captions[1]).Left - color.Left) < 1 &&
                      Math.Abs(modeBounds.Top + modeBounds.Height / 2 - swatch.Top - swatch.Height / 2) < 1 &&
                      swatch.Left > modeBounds.Right, "The lighting captions and controls are not aligned by row.");
                try { await SaveOnexImage("onex-lighting-mode-color-1021.png", modeGrid, 0); }
                catch (InvalidOperationException) { }
                Check(!_onexLightingColorFlyout.IsOpen, "The color palette opened by default");
                InvokeLightingButton(_onexLightingColorButton);await Task.Delay(100);
                Check(_onexLightingColorFlyout.IsOpen, "The color swatch did not open the palette");
                var buttons=_onexLightingPrimaryRow.Children.OfType<Windows.UI.Xaml.Controls.Button>().ToArray();Check(buttons.Length==3,"Pure RGB must remain available independently of recent colors");
                foreach(var button in buttons)
                {
                    ((Windows.UI.Xaml.Automation.Provider.IInvokeProvider)new Windows.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button).GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();await Task.Delay(80);
                    var c=((SolidColorBrush)button.Background).Color;
                    Check(new[]{c.R,c.G,c.B}.Count(v=>v==255)==1&&new[]{c.R,c.G,c.B}.Count(v=>v==0)==2,"RGB shortcut contains unintended color channels");
                    foreach(var item in _onexLightingDraft.GetNamedArray("zones")){var z=item.GetObject();Check(z.GetNamedNumber("r")==c.R&&z.GetNamedNumber("g")==c.G&&z.GetNamedNumber("b")==c.B&&z.GetNamedNumber("mode")==2,"Pure RGB shortcut did not reach all zone drafts");}
                }
                // A same-value Color assignment emits no property notification. Deliberately vary
                // one draft zone, then repeat Blue to prove the explicit All action is still applied.
                _onexLightingDraft.GetNamedArray("zones")[1].GetObject()["r"]=JsonValue.CreateNumberValue(17);
                ((Windows.UI.Xaml.Automation.Provider.IInvokeProvider)new Windows.UI.Xaml.Automation.Peers.ButtonAutomationPeer(buttons[2]).GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();await Task.Delay(80);
                Check(_onexLightingDraft.GetNamedArray("zones").All(item=>item.GetObject().GetNamedNumber("r")==0&&item.GetObject().GetNamedNumber("b")==255),"Same-color All selection missed a zone");
                var left = _onexLightingDraft.GetNamedArray("zones").Select(item => item.GetObject()).Single(zone => zone.GetNamedNumber("id") == 1);
                _onexLightingColorFlyout.Hide();await Task.Delay(100);
                SelectLightingZoneForVerification(1);await Task.Delay(80);
                _onexLightingEffect.SelectedItem=_onexLightingEffect.Items.OfType<ComboBoxItem>().First(item=>item.Tag is int mode&&mode>0);await Task.Delay(80);
                Check(LightingMode(left)>0&&((Windows.UI.Xaml.FrameworkElement)_onexLightingColorButton.Parent).Visibility==Windows.UI.Xaml.Visibility.Collapsed,"A firmware effect did not hide the color swatch");
                Check(captions[1].Visibility == Windows.UI.Xaml.Visibility.Collapsed, "A firmware effect left an orphaned color caption.");
                _onexLightingEffect.SelectedItem=_onexLightingEffect.Items.OfType<ComboBoxItem>().Single(item=>item.Tag is int mode&&mode==0);await Task.Delay(80);
                InvokeLightingButton(_onexLightingColorButton);await Task.Delay(100);
                ((Windows.UI.Xaml.Automation.Provider.IInvokeProvider)new Windows.UI.Xaml.Automation.Peers.ButtonAutomationPeer(buttons[0]).GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();await Task.Delay(80);
                Check(LightingMode(left)==0&&left.GetNamedNumber("r")==255,"The schematic did not scope the selected RGB color to the left stick");
                _onexLightingPrimaryRow.StartBringIntoView(new Windows.UI.Xaml.BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);
                var capture=SaveOnexImage("onex-led-pure-rgb190.png",_onexLightingPrimaryRow,0);bool captured=await Task.WhenAny(capture,Task.Delay(3000))==capture;if(captured)await capture;
                report["pureRgbDraftCases"]=JsonValue.CreateNumberValue(3);report["sameValueAllDraftFixturePassed"]=JsonValue.CreateBooleanValue(true);report["presetToSolidDraftPassed"]=JsonValue.CreateBooleanValue(true);report["captureCompleted"]=JsonValue.CreateBooleanValue(captured);report["hardwareCommandsIssued"]=JsonValue.CreateNumberValue(0);report["physicalColorFidelityTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally{CloseOnexMenus();await opening;}
            response=await SendOnexHomeRequestAsync(new ValueSet{{"OnexHardwareHomeGet",true}});ApplyOnexHardwareHomeState((string)response["OnexHardwareHomeState"]);
            Check(_onexHardwareHome.GetNamedObject("lighting").Stringify()==original,"Draft-only palette verification changed saved lighting");
            report["lightingColorAlignmentPassed"] = JsonValue.CreateBooleanValue(true);
            Check(settings.TryGetValue("OnexRecentLedColors",out object after)==hadHistory&&(!hadHistory||Equals(history,after)),"Cancelled palette draft overwrote owner history");report["originalLightingAndHistoryPreserved"]=JsonValue.CreateBooleanValue(true);
        }
        private async Task VerifyOnexFanLedIsolationAsync(JsonObject report)
        {
            async Task Refresh()
            {
                var reply=await SendOnexHomeRequestAsync(new ValueSet{["OnexHardwareHomeGet"]=true});
                ApplyOnexHardwareHomeState((string)reply["OnexHardwareHomeState"]);
            }
            JsonObject Lighting()=>new JsonObject{{"lightingEnabled",JsonValue.CreateBooleanValue(_onexHardwareHome.GetNamedObject("lighting").GetNamedBoolean("enabled"))},{"zones",JsonArray.Parse(_onexHardwareHome.GetNamedObject("lighting").GetNamedArray("zones").Stringify())}};
            string Visual(TileDefinition tile)
            {
                string Brush(Windows.UI.Xaml.Media.Brush value)=>(value as SolidColorBrush)?.Color.ToString()??value?.GetType().Name??"none";
                return tile.IsActionable+"|"+tile.TileButton.IsEnabled+"|"+tile.IconElement.Opacity+"|"+tile.NameText.Opacity+"|"+tile.StateText.Text+"|"+Brush(tile.StateText.Foreground)+"|"+Brush(tile.TileButton.Background)+"|"+Brush(tile.NameText.Foreground);
            }
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            await Refresh();
            var fan=qsTileMap["OnexFanMode"];var led=qsTileMap["LedToggle"];
            Check(!ReferenceEquals(fan,led)&&!ReferenceEquals(fan.TileButton,led.TileButton),"Fan and LED share a tile/button");
            Check(fan.IsActionable&&led.IsActionable,"Fan/LED hardware is unavailable");
            var original=Lighting();int originalFan=(int)_onexHardwareHome.GetNamedObject("fan").GetNamedNumber("profile");
            Check(originalFan==0,"Fan/LED verification requires the Auto starting mode");
            int fanCommands=0,ledCommands=0,peerChecks=0;
            async Task Fan(int profile)
            {
                string lightBefore=Lighting().Stringify(),peer=Visual(led);
                var pending=SetOnexFanAsync(profile);fanCommands++;
                Check(_onexFanBusy&&!_onexLightingBusy&&!fan.IsActionable&&fan.StateText.Text=="Applying…","Fan busy feedback is missing");
                Check(peer==Visual(led),"Fan request changed LED visual state");peerChecks++;
                await pending;await Refresh();
                Check((int)_onexHardwareHome.GetNamedObject("fan").GetNamedNumber("profile")==profile,"Fan profile acknowledgement mismatch");
                Check(lightBefore==Lighting().Stringify(),"Fan request changed lighting settings");
                Check(fan.IsActionable&&led.IsActionable,"Fan request left a tile busy");
            }
            async Task Light(JsonObject settings,bool toggle=false)
            {
                int fanBefore=(int)_onexHardwareHome.GetNamedObject("fan").GetNamedNumber("profile");string peer=Visual(fan);
                var pending=toggle?ToggleOnexLightingAsync():SetOnexLightingAsync(settings);ledCommands++;
                Check(_onexLightingBusy&&!_onexFanBusy&&!led.IsActionable&&led.StateText.Text=="Applying…","LED busy feedback is missing");
                Check(peer==Visual(fan),"LED request changed Fan visual state");peerChecks++;
                await pending;await Refresh();
                Check(settings.Stringify()==Lighting().Stringify(),"LED command acknowledgement mismatch");
                Check((int)_onexHardwareHome.GetNamedObject("fan").GetNamedNumber("profile")==fanBefore,"LED request changed Fan profile");
                Check(fan.IsActionable&&led.IsActionable,"LED request left a tile busy");
            }
            try
            {
                await Fan(0);
                var all=JsonObject.Parse(original.Stringify());all["lightingEnabled"]=JsonValue.CreateBooleanValue(true);
                foreach(var item in all.GetNamedArray("zones"))
                {
                    var z=item.GetObject();z["enabled"]=JsonValue.CreateBooleanValue(true);z["mode"]=JsonValue.CreateNumberValue(2);z["breathing"]=JsonValue.CreateBooleanValue(false);
                    z["r"]=JsonValue.CreateNumberValue(18);z["g"]=JsonValue.CreateNumberValue(116);z["b"]=JsonValue.CreateNumberValue(216);
                }
                await Light(all);
                var off=JsonObject.Parse(all.Stringify());off["lightingEnabled"]=JsonValue.CreateBooleanValue(false);await Light(off,true);await Light(all,true);
                var individual=JsonObject.Parse(all.Stringify());var zone=individual.GetNamedArray("zones")[0].GetObject();zone["enabled"]=JsonValue.CreateBooleanValue(false);await Light(individual);
                zone["enabled"]=JsonValue.CreateBooleanValue(true);zone["mode"]=JsonValue.CreateNumberValue(1);zone["preset"]=JsonValue.CreateNumberValue(5);await Light(individual);
                var allOff=JsonObject.Parse(all.Stringify());allOff["lightingEnabled"]=JsonValue.CreateBooleanValue(false);foreach(var item in allOff.GetNamedArray("zones"))item.GetObject()["enabled"]=JsonValue.CreateBooleanValue(false);
                await Light(allOff);await Light(all,true);
                report["distinctTileReferences"]=JsonValue.CreateBooleanValue(true);
                report["peerVisualChecks"]=JsonValue.CreateNumberValue(peerChecks);
                report["fanProfileCommands"]=JsonValue.CreateNumberValue(fanCommands);
                report["lightingCommands"]=JsonValue.CreateNumberValue(ledCommands);
                report["allAndIndividualZoneAcknowledgements"]=JsonValue.CreateBooleanValue(true);
                report["allOffThenTileOnRecovered"]=JsonValue.CreateBooleanValue(true);
                report["physicalLightingReadback"]=JsonValue.CreateBooleanValue(false);
                report["ownerVisualConfirmation"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                await SetOnexLightingAsync(original);
                if((int)_onexHardwareHome.GetNamedObject("fan").GetNamedNumber("profile")!=originalFan)await SetOnexFanAsync(originalFan);
                await Refresh();
                bool restored=Lighting().Stringify()==original.Stringify()&&(int)_onexHardwareHome.GetNamedObject("fan").GetNamedNumber("profile")==originalFan;
                report["originalFanAndLightingRestored"]=JsonValue.CreateBooleanValue(restored);
                Check(restored,"Original Fan/LED settings were not restored");
            }
        }

        private async Task<bool> VerifyOnexLightingZoneControlsAsync(string imageName = null)
        {
            void Check(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
            }

            var diagram = _onexLightingDiagram;
            _onexLightingAllZones.IsOn = false;
            var targets = diagram.Zones.Values;
            Check(targets.Count == 5 && targets.All(button => button.Visibility == Visibility.Visible && button.IsEnabled),
                "The per-zone controls are not available.");
            InvokeLightingButton(diagram.Zones[1]);
            await Task.Delay(80);
            var left = _onexLightingDraft.GetNamedArray("zones").Select(item => item.GetObject())
                .Single(zone => zone.GetNamedNumber("id") == 1);
            SetLightingMode(left, LightingEffects.Solid);
            var color = Windows.UI.Colors.CornflowerBlue;
            SetLightingColor(left, color);
            _onexLightingAllZones.IsOn = true;
            diagram.View.UpdateLayout();
            Check(targets.All(button => button.Visibility == Visibility.Collapsed),
                "All Zones left buttons covering the diagram.");
            var viewbox = (Viewbox)((StackPanel)diagram.View.Child).Children[0];
            var marks = ((Canvas)viewbox.Child).Children.OfType<Shape>()
                .Where(shape => shape.Name.StartsWith("OnexLightingMark", StringComparison.Ordinal))
                .Select(shape => (SolidColorBrush)(shape is Polygon ? shape.Fill : shape.Stroke)).ToArray();
            Check(marks.Length == 5 && marks.All(ink => ink.Color.A > 0 && ink.Color.R == color.R &&
                ink.Color.G == color.G && ink.Color.B == color.B), "All Zones did not draw the shared color on all five marks.");
            var power = OnexOwnedContent(_onexLightingEdit.Panel).OfType<ToggleSwitch>()
                .Single(control => control.Name == "OnexLightingEnabled");
            string settingsBeforePowerToggle = _onexLightingDraft.GetNamedArray("zones").Stringify();
            power.IsOn = false;
            Check(_onexLightingAllZones.IsOn && !_onexLightingDraft.GetNamedBoolean("lightingEnabled") &&
                marks.All(ink => ink.Color.A == 0) && targets.All(button => button.Visibility == Visibility.Collapsed) &&
                _onexLightingDraft.GetNamedArray("zones").Stringify() == settingsBeforePowerToggle,
                "Turning off the lighting reset All Zones or its saved settings.");
            power.IsOn = true;
            Check(_onexLightingAllZones.IsOn && _onexLightingDraft.GetNamedBoolean("lightingEnabled") &&
                marks.All(ink => ink.Color.A > 0 && ink.Color.R == color.R && ink.Color.G == color.G && ink.Color.B == color.B) &&
                _onexLightingDraft.GetNamedArray("zones").Stringify() == settingsBeforePowerToggle,
                "Turning on the lighting did not restore the same five zones.");
            bool captured = false;
            if (imageName != null)
            {
                try { await SaveOnexImage(imageName, diagram.View, 80); captured = true; }
                catch (InvalidOperationException) { }
            }
            _onexLightingAllZones.IsOn = false;
            Check(targets.All(button => button.Visibility == Visibility.Visible && button.IsEnabled),
                "Leaving All Zones did not restore the hit targets.");
            InvokeLightingButton(diagram.Zones[2]);
            await Task.Delay(80);
            Check(diagram.Zones[2].BorderBrush is SolidColorBrush selection && selection.Color.A > 0 &&
                diagram.Zones[1].BorderBrush is SolidColorBrush previous && previous.Color.A == 0,
                "Selecting an individual zone after All Zones did not update the outline.");
            return captured;
        }

        private async Task VerifyOnexLightingPageAsync(JsonObject report)
        {
            void Check(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
            }

            var live = await SendOnexHomeRequestAsync(new ValueSet { ["OnexHardwareHomeGet"] = true });
            var original = (string)live["OnexHardwareHomeState"];
            var originalLight = JsonObject.Parse(original).GetNamedObject("lighting").Stringify();
            var tile = qsTileMap["LedToggle"];
            var tab = GetVisibleNavigationItems().FirstOrDefault(item => item.IsChecked == true);

            void Acknowledge(JsonObject settings)
            {
                var packet = JsonObject.Parse(original);
                var light = packet.GetNamedObject("lighting");
                light["enabled"] = settings["lightingEnabled"];
                light["zones"] = JsonArray.Parse(settings.GetNamedArray("zones").Stringify());
                ApplyOnexHardwareHomeState(packet.Stringify());
            }

            ToggleSwitch Power() => OnexOwnedContent(_onexLightingPageEditor.Panel).OfType<ToggleSwitch>()
                .Single(control => control.Name == "OnexLightingEnabled");
            Slider Brightness() => OnexOwnedContent(_onexLightingPageEditor.Panel).OfType<Slider>()
                .Single(control => control.Name == "OnexLightingBrightness");

            try
            {
                var defaults = OnexOrderedNavigation(new OnexTabsPreference());
                Check(defaults.Skip(defaults.Length - 3).SequenceEqual(new[] { FanNavItem, _onexLightingNav, SystemNavItem }),
                    "Lighting is not between Fan and Settings.");
                _onexLightingNav.IsChecked = true;
                await Task.Delay(180);
                Check(GetActiveScrollViewer() == _onexLightingPage && _onexLightingPageEditor != null,
                    "Lighting did not open as an independent page.");
                FocusFirstTabContentElement();
                Check(ReferenceEquals(Windows.UI.Xaml.Input.FocusManager.GetFocusedElement(), _onexLightingPageEditor.Scope),
                    "The Lighting page is not reachable by gamepad focus.");
                try
                {
                    await SaveOnexImage("onex-lighting-tab-1022.png", _onexLightingPage, 0);
                    report["lightingPageImageCaptured"] = JsonValue.CreateBooleanValue(true);
                }
                catch (InvalidOperationException) { }
                report["lightingAllZonesImageCaptured"] = JsonValue.CreateBooleanValue(
                    await VerifyOnexLightingZoneControlsAsync("onex-lighting-all-zones-1023.png"));

                PerformanceNavItem.IsChecked = true;
                var fixture = CreateOnexLightingToggleRequest(JsonObject.Parse(originalLight));
                fixture["lightingEnabled"] = JsonValue.CreateBooleanValue(true);
                foreach (var value in fixture.GetNamedArray("zones"))
                {
                    var zone = value.GetObject();
                    zone["enabled"] = JsonValue.CreateBooleanValue(true);
                    zone["brightness"] = JsonValue.CreateNumberValue(2);
                    SetLightingMode(zone, LightingEffects.Solid);
                    SetLightingColor(zone, Windows.UI.Colors.CornflowerBlue);
                }
                Acknowledge(fixture);
                Acknowledge(CreateOnexLightingToggleRequest(OnexSavedLighting));
                Check(tile.StateText.Text == OnexStrings.Get("Off"), "The Home tile missed its acknowledged toggle.");
                _onexLightingNav.IsChecked = true;
                Check(!Power().IsOn && _onexLightingAllZones.IsOn && Brightness().Value == 2,
                    "The page did not load the Home tile's saved power and scope.");
                Acknowledge(CreateOnexLightingToggleRequest(OnexSavedLighting));
                Check(Power().IsOn && _onexLightingAllZones.IsOn, "The open Lighting page missed a clean state refresh.");

                Brightness().Value = 3;
                Power().IsOn = false;
                Check(OnexLightingIsOn, "A page draft changed the shared lighting state before Apply.");
                int writes = 0;
                JsonObject savedFixture = null;
                Task<bool> SaveFixture(JsonObject request)
                {
                    writes++;
                    savedFixture = JsonObject.Parse(request.Stringify());
                    Acknowledge(savedFixture);
                    return Task.FromResult(true);
                }
                Check(await ApplyOnexLightingPageAsync(SaveFixture) && writes == 1 && !OnexLightingIsOn,
                    "Applying the page did not update the shared lighting state.");
                Check(_onexLightingAllZones.IsOn && !Power().IsOn, "All Zones was lost when saving the LEDs off.");
                PerformanceNavItem.IsChecked = true;
                Check(tile.StateText.Text == OnexStrings.Get("Off"), "Home did not paint the page's saved power state.");
                var dialog = ShowOnexLightingSettingsAsync(tile.TileButton);
                try
                {
                    bool loadedPageSettings = _onexLightingDraft != null && _onexLightingAllZones.IsOn &&
                        !_onexLightingDraft.GetNamedBoolean("lightingEnabled") &&
                        _onexLightingDraft.GetNamedArray("zones").All(value => value.GetObject().GetNamedNumber("brightness") == 3);
                    for (int attempt = 0; attempt < 30 && _onexDetailsDialog == null; attempt++) await Task.Delay(50);
                    Check(_onexDetailsDialog != null, "The tile settings dialog did not open.");
                    Check(loadedPageSettings, "Tile settings did not load the page's saved power and brightness.");
                    await VerifyOnexLightingZoneControlsAsync();
                }
                finally { CloseOnexMenus(); await dialog; }

                Acknowledge(savedFixture);
                _onexLightingNav.IsChecked = true;
                // Keep an edit open while the same shared state receives a newer acknowledgement.
                _onexLightingAllZones.IsOn = true;
                Brightness().Value = 4;
                var remoteToggle = CreateOnexLightingToggleRequest(OnexSavedLighting);
                Acknowledge(remoteToggle);
                Check(Brightness().Value == 4 && !await ApplyOnexLightingPageAsync(SaveFixture) && writes == 1,
                    "A stale page draft overwrote a newer shared lighting state.");
                InvokeLightingButton(_onexLightingCancel);
                await Task.Delay(100);
                Check(Power().IsOn && Brightness().Value == 3 && OnexLightingIsOn,
                    "Cancel did not restore the latest shared lighting settings.");
                PerformanceNavItem.IsChecked = true;
                Check(tile.StateText.Text == OnexStrings.Get("On"), "Home did not repaint from the newer acknowledgement.");
                report["lightingTabBetweenFanAndSettings"] = JsonValue.CreateBooleanValue(true);
                report["lightingTileToPagePassed"] = JsonValue.CreateBooleanValue(true);
                report["lightingPageToTilePassed"] = JsonValue.CreateBooleanValue(true);
                report["lightingAllZonesPreviewParity"] = JsonValue.CreateBooleanValue(true);
                report["lightingGroupPowerPreserved"] = JsonValue.CreateBooleanValue(true);
            }
            finally
            {
                PerformanceNavItem.IsChecked = true;
                var restored = await SendOnexHomeRequestAsync(new ValueSet { ["OnexHardwareHomeGet"] = true });
                ApplyOnexHardwareHomeState((string)restored["OnexHardwareHomeState"]);
                if (tab != null) tab.IsChecked = true;
            }
            Check(OnexSavedLighting.Stringify() == originalLight, "The simulated sync check changed actual lighting.");
            report["lightingSyncTransport"] = JsonValue.CreateStringValue("projected Bridge acknowledgements");
            report["lightingPageSavedStatePreserved"] = JsonValue.CreateBooleanValue(true);
        }
    }
}
