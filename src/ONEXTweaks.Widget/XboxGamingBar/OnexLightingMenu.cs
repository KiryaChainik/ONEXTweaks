// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ONEXTweaks.Lighting;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Text;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private const int AllZonesBreathing = -2;
        private static readonly Dictionary<int, string> LightingModeLabels = new Dictionary<int, string>
        {
            { LightingEffects.Solid, "Solid color" }, { LightingEffects.Breathing, "Breathing" },
            { AllZonesBreathing, "Breathing (G + top light bar)" },
            { LightingEffects.ChromaBreathing, "Chroma Breathing" },
            { 13, "Monster Woke" }, { 3, "Flowing Light" }, { 11, "Sunset afterglow" },
            { 5, "Colorful Neon" }, { 7, "Dreamy" }, { 9, "Cyberpunk" }, { 12, "Colourful" },
            { 2, "Aurora" }, { 8, "Warm Sun" }, { 1000, "OXP classic" }, { LightingEffects.LegacyAokClassic, "AOK classic" },
        };
        private JsonObject _onexLightingDraft;
        private StackPanel _onexLightingPrimaryRow;
        private ToggleSwitch _onexLightingAllZones;
        private ComboBox _onexLightingEffect;
        private LightingDiagram _onexLightingDiagram;
        private Button _onexLightingColorButton;
        private Flyout _onexLightingColorFlyout;

        private static int LightingMode(JsonObject zone) => (int)zone.GetNamedNumber("mode") == 1
            ? (int)zone.GetNamedNumber("preset")
            : zone.GetNamedBoolean("breathing", false) ? LightingEffects.Breathing : LightingEffects.Solid;

        private static int LightingRgb(JsonObject zone) => (int)zone.GetNamedNumber("r") << 16 |
            (int)zone.GetNamedNumber("g") << 8 | (int)zone.GetNamedNumber("b");
        private static Color LightingColor(int rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        private static string LightingHex(Color color) => "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");

        private static void SetLightingMode(JsonObject zone, int mode)
        {
            zone["mode"] = JsonValue.CreateNumberValue(mode > 0 ? 1 : 2);
            zone["breathing"] = JsonValue.CreateBooleanValue(mode == LightingEffects.Breathing);
            if (mode > 0) zone["preset"] = JsonValue.CreateNumberValue(mode);
        }

        private static void SetLightingColor(JsonObject zone, Color color)
        {
            zone["r"] = JsonValue.CreateNumberValue(color.R);
            zone["g"] = JsonValue.CreateNumberValue(color.G);
            zone["b"] = JsonValue.CreateNumberValue(color.B);
        }

        private Slider CreateLightingSlider(string name, string label, int min, int max)
        {
            var slider = new Slider
            {
                Name = name, Minimum = min, Maximum = max, Height = 34,
                StepFrequency = 1, SmallChange = 1, LargeChange = 1, IsFocusEngagementEnabled = true,
                SnapsTo = SliderSnapsTo.StepValues, IsThumbToolTipEnabled = false,
            };
            AutomationProperties.SetName(slider, OnexStrings.Get(label));
            ConfigureOnexFocus(slider);
            return slider;
        }

        private static FrameworkElement LightingSliderRow(string label, Slider slider, TextBlock value)
        {
            var heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.Children.Add(new TextBlock { Text = OnexStrings.Get(label), FontSize = 12 });
            Grid.SetColumn(value, 1);
            heading.Children.Add(value);
            return new StackPanel { Children = { heading, slider } };
        }

        private sealed class OnexLightingEdit
        {
            internal StackPanel Panel;
            internal LightingDiagram Diagram;
            internal ToggleSwitch Scope;
            internal Action<JsonObject, bool> Refresh;
            internal Func<Func<JsonObject, Task<bool>>, Task<bool>> Apply;
            internal Action Preview;
            internal Action Stop;
        }

        private OnexLightingEdit _onexLightingEdit;
        private JsonObject OnexSavedLighting => _onexHardwareHome?.GetNamedObject("lighting", null);
        private static string OnexLightingSettingsVersion(JsonObject light) =>
            light == null ? null : light.GetNamedBoolean("enabled", false) + ":" + light.GetNamedArray("zones").Stringify();

        private async Task ShowOnexLightingSettingsAsync(Button anchor)
        {
            if (_onexLightingEdit != null) return;
            OnexLightingEdit editor = null;
            try
            {
                if (OnexSavedLighting?.GetNamedBoolean("available", false) != true)
                    throw new InvalidOperationException(OnexStrings.Get("Lighting controller unavailable"));
                editor = _onexLightingEdit = CreateOnexLightingEdit(OnexSavedLighting, ShowOnexDesktopStatus);
                var dialog = CreateOnexSettingsDialog(null, null, editor.Panel);
                _onexDetailsDialog = dialog;
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                    await editor.Apply(SetOnexLightingAsync);
            }
            catch (Exception error) { ShowOnexDesktopStatus(error.Message); }
            finally
            {
                _onexDetailsDialog = null;
                ReleaseOnexLightingEdit(editor);
                RefreshOnexLightingPage();
                anchor.Focus(FocusState.Programmatic);
            }
        }

        private OnexLightingEdit CreateOnexLightingEdit(JsonObject light, Action<string> showStatus)
        {
            JsonObject request = null;
            Dictionary<int, JsonObject> byId = null;
            JsonObject[] allZoneData = null;
            string savedVersion = null, cleanDraft = null;
            int ZoneMode(int id, int mode) => mode != AllZonesBreathing ? mode
                : LightingEffects.SupportsFactoryBreathing(id) ? LightingEffects.Breathing : LightingEffects.Solid;
            bool MatchesGroupMode(int mode) => byId.All(part => LightingMode(part.Value) == ZoneMode(part.Key, mode));
            void LoadDraft(JsonObject saved)
            {
                request = new JsonObject
                {
                    ["lightingEnabled"] = JsonValue.CreateBooleanValue(saved.GetNamedBoolean("enabled", false)),
                    ["zones"] = JsonArray.Parse(saved.GetNamedArray("zones").Stringify()),
                };
                byId = request.GetNamedArray("zones").Select(item => item.GetObject())
                    .ToDictionary(zone => (int)zone.GetNamedNumber("id"));
                allZoneData = new[] { 1, 2, 5, 6, 7 }.Select(id => byId[id]).ToArray();
                foreach (var part in byId)
                {
                    part.Value.Remove("animationSpeed");
                    if (LightingMode(part.Value) == LightingEffects.Breathing && !LightingEffects.SupportsFactoryBreathing(part.Key))
                        SetLightingMode(part.Value, LightingEffects.ChromaBreathing);
                }
                _onexLightingDraft = request;
                savedVersion = OnexLightingSettingsVersion(saved);
                cleanDraft = request.Stringify();
            }
            LoadDraft(light);
            JsonObject[] scope = allZoneData;
            int selection = 1;
            bool rendering = false, colorDirty = false, recentChanged = false, updatingColorControls = false, stopped = false;
            bool CanEditAll() =>
                (MatchesGroupMode(LightingEffects.Solid) || MatchesGroupMode(AllZonesBreathing)) && allZoneData.All(zone =>
                    zone.GetNamedBoolean("enabled") &&
                    LightingRgb(zone) == LightingRgb(byId[1]) &&
                    zone.GetNamedNumber("brightness") == byId[1].GetNamedNumber("brightness"));

            var panel = new StackPanel { Spacing = 8 };
            var all = new ToggleSwitch
            {
                Name = "OnexLightingAllZones", MinWidth = 0,
                IsOn = CanEditAll(),
            };
            _onexLightingAllZones = all;
            var heading = new TextBlock { Text = OnexStrings.Get("Lighting"), FontSize = 20,
                FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var allRow = OnexSettingsInlineToggle(new TextBlock { Text = OnexStrings.Get("All Zones") }, all);
            AutomationProperties.SetName(all, OnexStrings.Get("All Zones"));
            var header = new Grid { ColumnSpacing = 12 };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(heading);
            Grid.SetColumn(allRow, 1);
            header.Children.Add(allRow);
            panel.Children.Add(header);
            var focus = OnexDialogAccent;
            var diagram = new LightingDiagram(focus, OnexCurrentPalette);
            _onexLightingDiagram = diagram;
            panel.Children.Add(diagram.View);

            var enabled = new ToggleSwitch
            {
                Name = "OnexLightingEnabled", MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                OnContent = OnexStrings.Get("On"), OffContent = OnexStrings.Get("Off"),
            };
            ApplyOnexTileWindowControlAccent(enabled);
            var brightness = CreateLightingSlider("OnexLightingBrightness", "Brightness", 1, 4);
            var brightnessValue = new TextBlock { FontSize = 12 };
            var powerRow = OnexSettingsColumns(enabled, LightingSliderRow("Brightness", brightness, brightnessValue));
            powerRow.ColumnDefinitions[0].Width = GridLength.Auto;
            panel.Children.Add(powerRow);
            var effect = OnexSettingsChoice("");
            effect.Header = null;
            effect.Name = "OnexLightingEffect";
            effect.ItemTemplate = null;
            AutomationProperties.SetName(effect, OnexStrings.Get("Mode"));
            _onexLightingEffect = effect;
            var sample = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), IsHitTestVisible = false };
            var colorButton = new Button
            {
                Name = "OnexLightingColor", Content = sample, Width = 32, Height = 32,
                MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
            };
            ApplyOnexTileWindowControlAccent(colorButton);
            AutomationProperties.SetName(colorButton, OnexStrings.Get("Choose color"));
            _onexLightingColorButton = colorButton;
            var colorCode = new TextBlock
            {
                Name = "OnexLightingColorCode", FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas"),
            };
            var colorRow = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center, Spacing = 6, Children = { colorCode, colorButton },
            };
            var effectRow = OnexSettingsColumns(effect, colorRow);
            effectRow.ColumnDefinitions[1].Width = GridLength.Auto;
            effectRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            effectRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            effectRow.RowSpacing = 6;
            var modeCaption = new TextBlock { Text = OnexStrings.Get("Mode"), FontSize = 13 };
            var colorCaption = new TextBlock { Text = OnexStrings.Get("Color"), FontSize = 13 };
            Grid.SetColumn(colorCaption, 1);
            Grid.SetRow(effect, 1);
            Grid.SetRow(colorRow, 1);
            effectRow.Children.Add(modeCaption);
            effectRow.Children.Add(colorCaption);
            panel.Children.Add(effectRow);

            var palette = new StackPanel { Spacing = 5 };
            var spectrum = new ColorSpectrum
            {
                Name = "OnexLightingPalette", Components = ColorSpectrumComponents.SaturationValue,
                Height = 104, MinHeight = 0, MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var hue = CreateLightingSlider("OnexLightingHue", "Hue", 0, 359);
            hue.SmallChange = 5;
            var hex = new TextBox { MaxLength = 7, PlaceholderText = "#RRGGBB", MinWidth = 96, FontSize = 13 };
            AutomationProperties.SetName(hex, OnexStrings.Get("Hex color"));
            var colorLabel = new TextBlock { Text = OnexStrings.Get("Hex color"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            palette.Children.Add(OnexSettingsColumns(colorLabel, hex));
            palette.Children.Add(spectrum);
            palette.Children.Add(hue);
            var primary = new StackPanel { Name = "OnexLedPrimaryColors", Orientation = Orientation.Horizontal, Spacing = 4 };
            _onexLightingPrimaryRow = primary;
            var recentButtons = new StackPanel { Name = "OnexRecentLedColors", Orientation = Orientation.Horizontal, Spacing = 4 };
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { primary, recentButtons } };
            palette.Children.Add(swatches);
            var paletteStyle = new Style(typeof(FlyoutPresenter));
            paletteStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12)));
            paletteStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(10)));
            paletteStyle.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0d));
            var paletteFlyout = new Flyout
            {
                Content = palette, Placement = FlyoutPlacementMode.Bottom, FlyoutPresenterStyle = paletteStyle,
            };
            _onexLightingColorFlyout = paletteFlyout;

            var settings = ApplicationData.Current.LocalSettings.Values;
            var recent = new List<Color>();
            void ReadRecent()
            {
                recent.Clear();
                if (settings.TryGetValue("OnexRecentLedColors", out var existing) &&
                    existing is string saved && JsonArray.TryParse(saved, out var savedColors))
                {
                    foreach (var entry in savedColors.Where(item => item.ValueType == JsonValueType.String))
                    {
                        if (ReadHex(entry.GetString(), out int rgb))
                            recent.Add(LightingColor(rgb));
                    }
                }
            }
            var defaults = new[] { Colors.White, Colors.Gold, Colors.Turquoise, Colors.DeepSkyBlue, Colors.Violet, Colors.OrangeRed };
            var previewClock = Stopwatch.StartNew();
            bool showMotion = new UISettings().AnimationsEnabled;
            var animation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(66) };
            bool IsPreviewVisible() => !stopped && widget?.Visible != false;
            animation.Tick += (sender, args) =>
            {
                if (IsPreviewVisible()) PaintDiagram();
                else animation.Stop();
            };

            void PaintDiagram()
            {
                bool lightingOn = request.GetNamedBoolean("lightingEnabled");
                bool previewBrightness = OnexInputGuard.IsPreviewing(brightness);
                foreach (var part in byId)
                {
                    var zone = part.Value;
                    int rgb = LightingRgb(zone);
                    bool on = lightingOn && zone.GetNamedBoolean("enabled");
                    bool inScope = scope.Contains(zone);
                    int level = inScope && previewBrightness ? (int)brightness.Value : (int)zone.GetNamedNumber("brightness");
                    if (on)
                        rgb = LightingEffects.PreviewColor(LightingMode(zone), rgb,
                            showMotion ? previewClock.ElapsedMilliseconds : 0);
                    diagram.UpdateZone(part.Key, rgb, on, level);
                }
            }

            void UpdatePreview()
            {
                if (!IsPreviewVisible()) return;
                PaintDiagram();
                bool animated = request.GetNamedBoolean("lightingEnabled") &&
                    allZoneData.Any(zone => zone.GetNamedBoolean("enabled") && LightingMode(zone) != LightingEffects.Solid);
                if (showMotion && animated) animation.Start(); else animation.Stop();
            }

            int? ChosenMode() => (effect.SelectedItem as ComboBoxItem)?.Tag as int?;

            string BrightnessLabel(int value) => value + "/4";

            void RenderModeControls(int? mode)
            {
                colorCaption.Visibility = colorRow.Visibility =
                    mode.GetValueOrDefault() > 0 ? Visibility.Collapsed : Visibility.Visible;
                spectrum.Color = LightingColor(LightingRgb(scope[0]));
                RefreshColorControls();
            }

            void Render()
            {
                rendering = true;
                try
                {
                    scope = all.IsOn ? allZoneData : new[] { byId[selection] };
                    AutomationProperties.SetName(enabled, OnexStrings.Get("Lighting"));
                    enabled.IsOn = request.GetNamedBoolean("lightingEnabled") && (all.IsOn || scope[0].GetNamedBoolean("enabled"));
                    diagram.UpdateSelection(all.IsOn, selection, focus);
                    int level = (int)scope[0].GetNamedNumber("brightness");
                    brightness.Value = level;
                    brightnessValue.Text = BrightnessLabel(level);
                    int current = LightingMode(scope[0]);
                    var modes = new List<int> { LightingEffects.Solid };
                    if (all.IsOn)
                    {
                        modes.Add(AllZonesBreathing);
                        current = MatchesGroupMode(AllZonesBreathing) ? AllZonesBreathing : LightingEffects.Solid;
                    }
                    else if (LightingEffects.SupportsFactoryBreathing(selection))
                        modes.Add(LightingEffects.Breathing);
                    if (!all.IsOn && LightingEffects.SupportsPresets(selection))
                    {
                        modes.AddRange(LightingEffects.PresetIds());
                        if (current == LightingEffects.LegacyAokClassic)
                            modes.Add(LightingEffects.LegacyAokClassic);
                    }
                    effect.IsEnabled = modes.Count > 1;
                    var listed = effect.Items.OfType<ComboBoxItem>().Select(item => (int)item.Tag);
                    // Changing the scope can change which modes are supported. Keep the same
                    // native items when only the saved selection changes.
                    if (!listed.SequenceEqual(modes))
                    {
                        effect.Items.Clear();
                        foreach (int mode in modes)
                            effect.Items.Add(new ComboBoxItem { Tag = mode, Content = OnexStrings.Get(LightingModeLabels[mode]) });
                    }
                    effect.SelectedItem = effect.Items.OfType<ComboBoxItem>()
                        .FirstOrDefault(item => item.Tag as int? == current);
                    RenderModeControls(ChosenMode());
                }
                finally { rendering = false; }
                UpdatePreview();
            }

            void SelectZone(int id)
            {
                if (all.IsOn) return;
                CommitColor();
                selection = id;
                Render();
            }

            void RefreshColorControls(bool updateHex = true)
            {
                updatingColorControls = true;
                try
                {
                    colorCode.Text = LightingHex(spectrum.Color);
                    if (updateHex) hex.Text = colorCode.Text;
                    sample.Background = OnexSolidBrush(spectrum.Color);
                    hue.Value = spectrum.HsvColor.X;
                }
                finally { updatingColorControls = false; }
            }

            void EditColor(Color color, bool keepHex = false)
            {
                if (rendering || updatingColorControls) return;
                foreach (var zone in scope) SetLightingColor(zone, color);
                colorDirty = true;
                RefreshColorControls(!keepHex);
                UpdatePreview();
            }

            Button ColorButton(Color color)
            {
                var button = new Button
                {
                    Width = 24, Height = 28, MinWidth = 0, MinHeight = 0,
                    Background = new SolidColorBrush(color), Padding = new Thickness(0), CornerRadius = new CornerRadius(4),
                };
                var text = LightingHex(color);
                AutomationProperties.SetName(button, text);
                ToolTipService.SetToolTip(button, text);
                ConfigureOnexFocus(button);
                button.Click += (sender, args) =>
                {
                    updatingColorControls = true;
                    try { spectrum.Color = color; }
                    finally { updatingColorControls = false; }
                    EditColor(color);
                    CommitColor();
                };
                return button;
            }

            void BuildRecent()
            {
                recentButtons.Children.Clear();
                foreach (var color in recent.Concat(defaults).Distinct().Take(6))
                    recentButtons.Children.Add(ColorButton(color));
            }

            void CommitColor()
            {
                if (!colorDirty) return;
                colorDirty = false;
                recentChanged = true;
                var accepted = spectrum.Color;
                recent.RemoveAll(color => color.Equals(accepted));
                recent.Insert(0, accepted);
                if (recent.Count > 6) recent.RemoveRange(6, recent.Count - 6);
                BuildRecent();
            }

            void SetZonePower(JsonObject[] zones, bool on)
            {
                foreach (var zone in zones) zone["enabled"] = JsonValue.CreateBooleanValue(on);
                request["lightingEnabled"] = JsonValue.CreateBooleanValue(allZoneData.Any(zone => zone.GetNamedBoolean("enabled")));
            }

            all.Toggled += (sender, args) =>
            {
                if (rendering) return;
                CommitColor();
                if (all.IsOn)
                {
                    var source = byId[selection];
                    var color = LightingColor(LightingRgb(source));
                    double level = source.GetNamedNumber("brightness");
                    int mode = LightingMode(source) == LightingEffects.Breathing ? AllZonesBreathing : LightingEffects.Solid;
                    foreach (var part in byId)
                    {
                        SetLightingMode(part.Value, ZoneMode(part.Key, mode));
                        SetLightingColor(part.Value, color);
                        part.Value["brightness"] = JsonValue.CreateNumberValue(level);
                    }
                    SetZonePower(allZoneData, true);
                }
                Render();
            };
            enabled.Toggled += (sender, args) =>
            {
                if (rendering) return;
                if (all.IsOn)
                {
                    // Global power leaves the shared zone settings available when the LEDs are off.
                    request["lightingEnabled"] = JsonValue.CreateBooleanValue(enabled.IsOn);
                }
                else
                {
                    if (!request.GetNamedBoolean("lightingEnabled")) SetZonePower(allZoneData, false);
                    SetZonePower(scope, enabled.IsOn);
                }
                UpdatePreview();
            };
            effect.SelectionChanged += (sender, args) =>
            {
                var mode = ChosenMode();
                if (rendering || !mode.HasValue) return;
                CommitColor();
                foreach (var zone in scope) SetLightingMode(zone, ZoneMode((int)zone.GetNamedNumber("id"), mode.Value));
                // WinRT is still completing its selection transaction here. Replacing
                // Items or SelectedItem inside this event can terminate Game Bar.
                rendering = true;
                try { RenderModeControls(mode); }
                finally { rendering = false; }
                UpdatePreview();
            };
            effect.DropDownClosed += (sender, args) =>
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, RefreshOnexLightingPage);
            void ChangeBrightness()
            {
                if (rendering) return;
                if (!OnexInputGuard.IsPreviewing(brightness))
                    foreach (var zone in scope) zone["brightness"] = JsonValue.CreateNumberValue(brightness.Value);
                brightnessValue.Text = BrightnessLabel((int)brightness.Value);
                UpdatePreview();
            }
            brightness.ValueChanged += (sender, args) => ChangeBrightness();
            OnexInputGuard.SetCommit(brightness, ChangeBrightness);
            OnexInputGuard.SetPreviewVisual(brightness, editing =>
            {
                if (!editing) brightnessValue.Text = BrightnessLabel((int)scope[0].GetNamedNumber("brightness"));
                UpdatePreview();
                if (!editing) RefreshOnexLightingPage();
            });
            spectrum.ColorChanged += (sender, args) => EditColor(args.NewColor);
            spectrum.AddHandler(UIElement.PointerReleasedEvent,
                new Windows.UI.Xaml.Input.PointerEventHandler((sender, args) => CommitColor()), true);
            spectrum.KeyUp += (sender, args) => CommitColor();
            hue.ValueChanged += (sender, args) =>
            {
                if (rendering || updatingColorControls) return;
                var hsv = spectrum.HsvColor;
                hsv.X = (float)args.NewValue;
                spectrum.HsvColor = hsv;
            };
            hue.KeyUp += (sender, args) => CommitColor();
            hex.TextChanged += (sender, args) =>
            {
                if (rendering || updatingColorControls || !ReadHex(hex.Text, out int rgb)) return;
                updatingColorControls = true;
                try { spectrum.Color = LightingColor(rgb); }
                finally { updatingColorControls = false; }
                EditColor(spectrum.Color, true);
            };
            hex.LostFocus += (sender, args) => { CommitColor(); RefreshColorControls(); };
            colorButton.Click += (sender, args) =>
            {
                _onexOpenMenus.Add(paletteFlyout);
                paletteFlyout.ShowAt(colorButton);
            };
            paletteFlyout.Closed += (sender, args) =>
            {
                _onexOpenMenus.Remove(paletteFlyout);
                if (!stopped)
                {
                    CommitColor();
                    RefreshOnexLightingPage();
                }
            };

            void Refresh(JsonObject live, bool reset)
            {
                if (stopped || live?.GetNamedBoolean("available", false) != true) return;
                if (!reset && (OnexLightingSettingsVersion(live) == savedVersion || request.Stringify() != cleanDraft ||
                    effect.IsDropDownOpen || paletteFlyout.IsOpen || OnexInputGuard.IsPreviewing(brightness))) return;
                LoadDraft(live);
                colorDirty = false;
                if (reset)
                {
                    recentChanged = false;
                    ReadRecent();
                    BuildRecent();
                }
                rendering = true;
                try { all.IsOn = CanEditAll(); }
                finally { rendering = false; }
                Render();
            }

            async Task<bool> Apply(Func<JsonObject, Task<bool>> save)
            {
                var latest = OnexSavedLighting;
                if (latest?.GetNamedBoolean("available", false) != true) return false;
                if (OnexLightingSettingsVersion(latest) != savedVersion)
                {
                    showStatus(OnexStrings.Get("Lighting changed. Cancel to reload, then try again."));
                    return false;
                }
                CommitColor();
                if (!await save(request)) return false;
                if (recentChanged)
                {
                    var history = new JsonArray();
                    foreach (var color in recent.Take(6)) history.Add(JsonValue.CreateStringValue(LightingHex(color)));
                    settings["OnexRecentLedColors"] = history.Stringify();
                }
                Refresh(OnexSavedLighting, true);
                return true;
            }

            ReadRecent();
            foreach (var color in new[] { Colors.Red, Colors.Lime, Colors.Blue })
                primary.Children.Add(ColorButton(color));
            BuildRecent();
            diagram.BindSelection(SelectZone);
            Render();
            return new OnexLightingEdit
            {
                Panel = panel,
                Diagram = diagram,
                Scope = all,
                Refresh = Refresh,
                Apply = Apply,
                Preview = UpdatePreview,
                Stop = () =>
                {
                    stopped = true;
                    animation.Stop();
                    paletteFlyout.Hide();
                    _onexOpenMenus.Remove(paletteFlyout);
                    OnexInputGuard.CancelInteraction(brightness);
                }
            };
        }

        private void ReleaseOnexLightingEdit(OnexLightingEdit editor)
        {
            if (editor == null || editor != _onexLightingEdit) return;
            _onexLightingEdit = null;
            editor.Stop();
            _onexLightingDraft = null;
            _onexLightingPrimaryRow = null;
            _onexLightingAllZones = null;
            _onexLightingEffect = null;
            _onexLightingDiagram = null;
            _onexLightingColorButton = null;
            _onexLightingColorFlyout = null;
        }

        private static bool ReadHex(string text, out int rgb)
        {
            text = text.Trim().TrimStart('#');
            return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb) && text.Length == 6;
        }
    }
}
