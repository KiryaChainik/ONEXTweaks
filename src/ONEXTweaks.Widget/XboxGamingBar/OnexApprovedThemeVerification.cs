// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexApprovedThemesAsync(JsonObject report)
        {
            EnsureOnexThemeCatalog();
            var originalTheme = currentThemeName;
            var originalSelection = ThemeComboBox.SelectedItem;
            var originalStoreFlag = _onexThemeQaStore;
            var artwork = CreateOnexControllerDeviceArt();
            var diagram = (ControllerSketchSurface)artwork.Tag;
            var lighting = new LightingDiagram(OnexDialogAccent, OnexCurrentPalette);
            var pad = new AssignmentDiagram(Ds4AssignmentArt, id => id, _ => { });
            var keyboard = new AssignmentDiagram(KeyboardAssignmentArt, id => id, _ => { });
            var mouse = new AssignmentDiagram(MouseAssignmentArt, id => id, _ => { });
            var xbox = new AssignmentDiagram(GamepadAssignmentArt, id => id, _ => { });
            var results = new JsonArray();
            var timings = new JsonArray();
            var consoleView = new Viewbox { Child = artwork };
            var consoleButton = diagram.Add("A","OnexThemeTestConsoleA",_=>{});
            diagram.SetSelected(consoleButton,true);
            pad.Select("TouchpadClickLeft");
            xbox.Select("LT");
            void Check(bool ok, string message)
            {
                if (!ok) throw new InvalidOperationException(message);
            }
            try
            {
                _onexThemeQaStore = true;
                foreach (string name in OnexAllowedThemeNames)
                {
                    var elapsed = System.Diagnostics.Stopwatch.StartNew();
                    ApplyTheme(name);
                    timings.Add(new JsonObject { { "name", JsonValue.CreateStringValue(name) },
                        { "applyMs", JsonValue.CreateNumberValue(elapsed.Elapsed.TotalMilliseconds) } });
                    var palette = OnexApprovedTheme.Find(name);
                    Check(_onexAppliedTheme.Name == name && _onexAppliedTheme.PageBackground == palette.Page, name + " did not apply");
                    Check(OnexContrast(palette.Text, palette.Raised) >= 4.5, name + " label contrast");
                    Check(OnexContrast(palette.DiagramOutline, palette.Inset) >= 5.3, name + " outline contrast");
                    Check(OnexContrast(palette.SelectedLine, palette.Selected) >= 3, name + " selection contrast");
                    var track=OnexInactiveSliderTrack(_onexAppliedTheme);
                    Check(OnexThemeContrastSurfaces(_onexAppliedTheme).All(surface=>OnexContrast(track,surface)>=3),name+" inactive slider contrast");
                    foreach (var view in new Windows.UI.Xaml.FrameworkElement[] { consoleView, lighting.View, pad.View, keyboard.View, mouse.View, xbox.View })
                        ApplyOnexCatalogSurfaces(view, _onexAppliedTheme);
                    foreach(var button in pad.Controls.Concat(xbox.Controls).Cast<Button>())
                    {
                        Check(OnexApprovedTheme.OwnsTarget(button),name+" unowned diagram button");
                        Check(button.Background is SolidColorBrush fill&&fill.Color.A==0&&button.BorderThickness.Left==0,name+" diagram covered by generic button chrome");
                    }
                    Check(diagram.HighlightedId=="A"&&consoleButton.Opacity==0,name+" console highlight changed with theme");
                    Check(diagram.HighlightShape is Windows.UI.Xaml.Shapes.Ellipse face &&
                        ((SolidColorBrush)face.Stroke).Color == palette.SelectedLine, name + " console face selection");
                    Check(xbox.Legends.Select(label => label.Text).OrderBy(text => text)
                        .SequenceEqual(new[] { "A", "B", "L3", "LB", "LT", "R3", "RB", "RT", "X", "Y" }),
                        name + " Xbox button legends");
                    Check(xbox.HighlightUri.AbsolutePath.EndsWith("/gamepad-LT.png",StringComparison.Ordinal),name+" Xbox shoulder outline was lost");
                    var sources = new[]
                    {
                        lighting.ArtworkUri, pad.ArtworkUri, mouse.ArtworkUri, xbox.ArtworkUri
                    };
                    foreach (var uri in sources) await StorageFile.GetFileFromApplicationUriAsync(uri);
                    var selection = pad.HighlightUri;
                    Check(selection.AbsolutePath.EndsWith("/ds4-TouchpadClickLeft.png", StringComparison.Ordinal) &&
                        selection.Segments[3] == pad.ArtworkUri.Segments[3], name + " selection geometry");
                    await StorageFile.GetFileFromApplicationUriAsync(selection);
                    Check(((SolidColorBrush)lighting.View.BorderBrush).Color == palette.DiagramFrame, name + " lighting frame");
                    Check(sources.Select(uri => uri.Segments[3]).Distinct().Count() == 1, name + " artwork folder mismatch");
                    Check(keyboard.ArtworkUri == null && keyboard.Legends.Count() == KeyboardAssignmentArt.Regions.Length &&
                        keyboard.Legends.Any(key => key.Text == "A" && key.FontSize == AssignmentDiagram.KeyboardAlphanumericSize &&
                            ((SolidColorBrush)key.Foreground).Color == palette.Text), name + " keyboard vector legends");
                    results.Add(JsonValue.CreateStringValue(name));
                }
                var dracula = OnexApprovedTheme.Find("Dracula");
                Check(dracula.Accent(OnexThemeRole.Network) == Color.FromArgb(255, 80, 250, 123), "Dracula Wi-Fi mark");
                Check(dracula.Accent(OnexThemeRole.Controller) != dracula.Accent(OnexThemeRole.Display), "Dracula roles collapsed");
                report["themes"] = results;
                report["checkedArtUris"] = JsonValue.CreateNumberValue(results.Count * 5);
                report["themeTimings"] = timings;
                await CaptureOnexRemapArtworkAsync(report, consoleView, diagram, xbox, pad, keyboard);
            }
            finally
            {
                ApplyTheme(originalTheme);
                ThemeComboBox.SelectedItem = originalSelection;
                _onexThemeQaStore = originalStoreFlag;
            }
        }

        private async Task CaptureOnexRemapArtworkAsync(JsonObject report, Viewbox sketch, ControllerSketchSurface diagram,
            AssignmentDiagram xbox, AssignmentDiagram ds4, AssignmentDiagram keyboard)
        {
            var palette = OnexApprovedTheme.Find("Dracula");
            var background = new SolidColorBrush(palette.Inset);
            var host = new Grid { Background = background, Padding = new Thickness(8), IsHitTestVisible = false };
            Canvas.SetZIndex(host, 200);
            sketch.Stretch = Stretch.Uniform;
            sketch.StretchDirection = StretchDirection.DownOnly;
            var trigger = diagram.Add("RT", "OnexThemeTestConsoleRT", _ => { });
            var images = new JsonArray();

            async Task Capture(string name, FrameworkElement content)
            {
                if (host.Children.Count == 0 || !ReferenceEquals(host.Children[0], content))
                {
                    host.Children.Clear();
                    host.Children.Add(content);
                }
                OnexStartupRoot.UpdateLayout();
                await SaveOnexImage(name, host, 240);
                images.Add(JsonValue.CreateStringValue(name));
            }

            OnexStartupRoot.Children.Add(host);
            try
            {
                OnexApprovedTheme.Repaint((Canvas)sketch.Child, palette);
                diagram.SetSelected(diagram.Hit(ControllerSketchTargets["A"].Center), false);
                await Capture("onex-remap-console-base.png", sketch);
                diagram.StartPress(trigger);
                if (!(diagram.HighlightShape is Windows.UI.Xaml.Shapes.Path) || diagram.HighlightedId != "RT")
                    throw new InvalidOperationException("Console RT did not receive its shaped press highlight");
                await Capture("onex-remap-console-rt.png", sketch);
                diagram.Cancel();
                var light = OnexApprovedTheme.Find("Catppuccin Latte Soft (adapted)");
                OnexApprovedTheme.Repaint((Canvas)sketch.Child, light);
                diagram.SetSelected(trigger, true);
                background.Color = light.Inset;
                await Capture("onex-remap-console-latte.png", sketch);
                background.Color = palette.Inset;
                OnexApprovedTheme.Repaint(xbox.View, palette);
                xbox.Select("A");
                await Capture("onex-remap-xbox-a.png", xbox.View);
                xbox.Select("RT");
                await Capture("onex-remap-xbox-rt.png", xbox.View);
                OnexApprovedTheme.Repaint(ds4.View, palette);
                ds4.Select("TouchpadClickRight");
                await Capture("onex-remap-ds4.png", ds4.View);
                OnexApprovedTheme.Repaint(keyboard.View, light);
                keyboard.PaintKeys(new[] { 65, 66, 67 });
                background.Color = light.Inset;
                await Capture("onex-remap-keyboard-latte.png", keyboard.View);
                report["renderedRemapArtwork"] = images;
            }
            finally
            {
                host.Children.Clear();
                OnexStartupRoot.Children.Remove(host);
            }
        }
    }
}
