// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private sealed class LightingDiagram
        {
            private const int DrawingWidth = 1280;
            private const int DrawingHeight = 502;
            private readonly Image _image;
            private readonly TextBlock _deviceCaption, _bridgeCaption;
            private readonly Dictionary<int, SolidColorBrush> _lights = new Dictionary<int, SolidColorBrush>();
            internal readonly Dictionary<int, Button> Zones = new Dictionary<int, Button>();
            internal readonly Border View;
            internal Uri ArtworkUri => ((BitmapImage)_image.Source).UriSource;

            internal LightingDiagram(Brush focus, OnexApprovedTheme theme)
            {
                var canvas = new Canvas { Width = DrawingWidth, Height = DrawingHeight };
                _image = new Image { Width = DrawingWidth, Height = DrawingHeight, IsHitTestVisible = false };
                canvas.Children.Add(_image);
                AddRing(canvas, 1, 109, 163, 31.5, 9);
                AddRing(canvas, 2, 879, 252, 31.5, 9);
                AddRing(canvas, 5, 183, 383, 14, 4);
                var bar = new PathFigure { StartPoint = new Point(561, 41) };
                bar.Segments.Add(new LineSegment { Point = new Point(834, 41) });
                bar.Segments.Add(new QuadraticBezierSegment { Point1 = new Point(863, 41), Point2 = new Point(879, 44) });
                var trace = new PathGeometry();
                trace.Figures.Add(bar);
                AddLight(canvas, 6, new Windows.UI.Xaml.Shapes.Path
                {
                    Data = trace, Stroke = Ink(6), StrokeThickness = 9,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                });
                AddLight(canvas, 7, new Polygon
                {
                    Fill = Ink(7),
                    Points = new PointCollection {
                        new Point(1117,129), new Point(1171,97), new Point(1171,162),
                        new Point(1158,155), new Point(1158,119), new Point(1139,130),
                        new Point(1148,136), new Point(1148,148),
                    },
                });
                AddZone(canvas, 1, 109, 163, 145, 145, "Left stick", focus);
                AddZone(canvas, 2, 879, 252, 145, 145, "Right stick", focus);
                AddZone(canvas, 5, 183, 383, 145, 145, "G button", focus);
                AddZone(canvas, 6, 720, 49, 350, 98, "Top light bar", focus);
                AddZone(canvas, 7, 1146, 130, 145, 145, "Controller Bridge logo", focus);

                var content = new StackPanel();
                content.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = canvas });
                var captions = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                captions.ColumnDefinitions.Add(new ColumnDefinition());
                captions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                _deviceCaption = Caption("ONEXPLAYER 3");
                _bridgeCaption = Caption("Controller Bridge");
                Grid.SetColumn(_bridgeCaption, 1);
                captions.Children.Add(_deviceCaption);
                captions.Children.Add(_bridgeCaption);
                content.Children.Add(captions);
                View = new Border
                {
                    Name = "OnexLightingDiagram", Child = content, CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromArgb(255, 19, 25, 31)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(255, 57, 71, 82)), BorderThickness = new Thickness(1),
                    Padding = new Thickness(6, 4, 6, 0),
                };
                OnexApprovedTheme.Bind(View, theme, ApplyTheme);
            }

            private static TextBlock Caption(string text)
            {
                return new TextBlock
                {
                    Text = text, FontSize = 11, TextAlignment = TextAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 164, 181, 190)),
                };
            }

            private void ApplyTheme(OnexApprovedTheme theme)
            {
                View.Background = OnexSolidBrush(theme.Inset);
                View.BorderBrush = OnexSolidBrush(theme.DiagramFrame);
                _deviceCaption.Foreground = _bridgeCaption.Foreground = OnexSolidBrush(theme.Text);
                _image.Source = theme.Artwork("OnexLightingConsole");
            }

            internal void UpdateZone(int id, int rgb, bool illuminated, int brightness)
            {
                var brush = _lights[id];
                brush.Color = illuminated
                    ? Color.FromArgb((byte)(120 + 33 * brightness), (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
                    : Colors.Transparent;
            }

            internal void UpdateSelection(bool all, int selected, Brush focus)
            {
                foreach (var pair in Zones)
                {
                    var button = pair.Value;
                    // The hit target is separate from the LED mark. A disabled
                    // button can paint its background over the mark.
                    button.Visibility = all ? Visibility.Collapsed : Visibility.Visible;
                    button.BorderBrush = !all && pair.Key == selected ? focus : new SolidColorBrush(Colors.Transparent);
                }
            }

            internal void BindSelection(Action<int> select)
            {
                foreach (var zone in Zones)
                    zone.Value.Click += (sender, args) => select(zone.Key);
            }

            private SolidColorBrush Ink(int id)
            {
                var ink = new SolidColorBrush(Colors.Transparent);
                _lights.Add(id, ink);
                return ink;
            }

            private static void AddLight(Canvas canvas, int id, Shape shape)
            {
                shape.Name = "OnexLightingMark" + id;
                shape.IsHitTestVisible = false;
                canvas.Children.Add(shape);
            }

            private void AddRing(Canvas canvas, int id, double x, double y, double radius, double stroke)
            {
                var ring = new Ellipse { Width = 2 * radius, Height = 2 * radius, Stroke = Ink(id), StrokeThickness = stroke };
                Canvas.SetLeft(ring, x - radius);
                Canvas.SetTop(ring, y - radius);
                AddLight(canvas, id, ring);
            }

            private void AddZone(Canvas canvas, int id, double x, double y, double width, double height,
                string label, Brush focus)
            {
                var button = new Button
                {
                    Name = "OnexLightingZone" + id, Width = width, Height = height, MinWidth = 0, MinHeight = 0,
                    Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(3),
                    BorderBrush = new SolidColorBrush(Colors.Transparent), CornerRadius = new CornerRadius(18),
                    FocusVisualPrimaryBrush = focus, FocusVisualPrimaryThickness = new Thickness(4),
                    FocusVisualMargin = new Thickness(2), Padding = new Thickness(0),
                };
                AutomationProperties.SetName(button, OnexStrings.Get(label));
                ToolTipService.SetToolTip(button, OnexStrings.Get(label));
                Canvas.SetLeft(button, x - width / 2);
                Canvas.SetTop(button, y - height / 2);
                Zones.Add(id, button);
                canvas.Children.Add(button);
            }
        }
    }
}
