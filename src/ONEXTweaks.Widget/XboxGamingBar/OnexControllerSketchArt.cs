// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private sealed class ControllerSketchArt
        {
            private readonly SolidColorBrush body = new SolidColorBrush();
            private readonly SolidColorBrush inset = new SolidColorBrush();
            private readonly SolidColorBrush shell = new SolidColorBrush();
            private readonly SolidColorBrush seam = new SolidColorBrush();
            private readonly SolidColorBrush cap = new SolidColorBrush();
            private readonly SolidColorBrush edge = new SolidColorBrush();
            private readonly SolidColorBrush ink = new SolidColorBrush();
            private readonly SolidColorBrush accent = new SolidColorBrush();
            private readonly SolidColorBrush selectedFill = new SolidColorBrush();
            private readonly SolidColorBrush selectedLine = new SolidColorBrush();
            private readonly Dictionary<string, Shape> targets = new Dictionary<string, Shape>(StringComparer.Ordinal);
            private readonly List<FrameworkElement> marks = new List<FrameworkElement>();
            private string selected;

            internal Canvas Layer { get; } = new Canvas
            {
                Width = 1280, Height = 1012, IsHitTestVisible = false,
                RenderTransform = new ScaleTransform { ScaleX = .5, ScaleY = .5 }
            };
            internal Shape Selection => selected == null ? null : targets[selected];

            internal ControllerSketchArt()
            {
                DrawPath("M143,48H1062C1114,48 1141,65 1150,90C1159,113 1133,160 1108,173C1071,190 1046,141 1011,128H194C160,141 134,190 97,173C72,160 46,113 54,90C64,65 91,48 143,48Z", body, shell, 3);
                DrawPath("M237,112H968", null, seam, 2);
                DrawPath("M124,500V444C124,303 179,240 295,240H1063C1166,240 1231,295 1231,436V496C1231,595 1214,652 1150,703L922,703Z", body, shell, 3);
                DrawPath("M140,333C194,329 234,300 267,266Q291,251 311,251H1043Q1064,251 1087,266C1119,300 1160,329 1212,333M312,240V477M1043,240V495M168,359Q147,397 153,498M1187,359Q1214,416 1192,572", null, seam, 2);
                var vents = new Canvas { Width = 274, Height = 203, Clip = new RectangleGeometry { Rect = new Rect(0, 0, 274, 203) } };
                Canvas.SetLeft(vents, 552); Canvas.SetTop(vents, 272);
                Layer.Children.Add(vents);
                for (int x = -198; x < 274; x += 18)
                    vents.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x + 203, Y2 = 203, Stroke = seam, StrokeThickness = 1.6 });
                DrawPath("M284,288L297,274Q299,272 302,275L308,280Q310,282 307,285L297,297Q295,299 292,296L285,291Z M1048,280L1054,274Q1056,272 1059,275L1071,287Q1073,290 1070,292L1065,297Q1063,299 1060,296L1048,285Q1046,282 1048,280Z", null, seam, 2);
                Round(284, 341, 21, 34, 10, null, seam, 2);
                Round(1050, 341, 21, 34, 10, null, seam, 2);

                AddCap("LT", "M237,55H142C104,55 77,65 61,86Q55,94 61,100C104,98 134,108 174,116C192,103 212,94 237,90Z");
                AddCap("LB", "M61,100C104,98 134,108 174,116C154,136 145,153 126,163C100,176 77,155 65,130Q55,112 61,100Z");
                AddCap("RT", "M968,55H1063C1101,55 1128,65 1144,86Q1150,94 1144,100C1101,98 1071,108 1031,116C1013,103 993,94 968,90Z");
                AddCap("RB", "M1144,100C1101,98 1071,108 1031,116C1051,136 1060,153 1079,163C1105,176 1128,155 1140,130Q1150,112 1144,100Z");
                AddCap("OEM:M1", "M262,396H296Q308,396 308,408V452Q308,465 296,465H262C242,465 225,452 225,430S242,396 262,396Z");
                AddCap("OEM:M2", "M1093,396H1059Q1047,396 1047,408V452Q1047,465 1059,465H1093C1113,465 1130,452 1130,430S1113,396 1093,396Z");

                DrawPath("M241,476H963C1088,476 1154,547 1154,679V748C1154,850 1124,932 1084,952C1068,960 1050,962 1020,962H184C154,962 136,960 120,952C80,932 50,850 50,748V679C50,547 116,476 241,476Z", body, shell, 3);
                DrawPath("M241,477V961M964,477V961M241,484Q132,487 71,557M964,484Q1073,487 1134,557", null, seam, 2);
                Round(249, 486, 706, 467, 4, body, shell, 2);
                Round(264, 502, 677, 426, 3, inset, seam, 2);

                targets.Add("Select", Round(211, 512, 22, 44, 11, cap, edge));
                targets.Add("Start", Round(970, 512, 22, 44, 11, cap, edge));
                foreach (string id in new[] { "A", "B", "X", "Y" }) AddCircle(id, 20);
                foreach (string id in new[] { "LSClick", "RSClick" })
                {
                    var center = Center(id);
                    Circle(center, 40, inset, edge);
                    AddCircle(id, 29);
                }
                foreach (string id in new[] { "OEM:Home", "OEM:GKey", "OEM:KeyboardMouse", "OEM:Onex" }) AddCircle(id, 18);

                DrawPath("M176,681H191Q198,681 198,690V706Q198,713 205,713H222Q229,713 229,721V733Q229,741 221,741H206Q198,741 198,747V763Q198,771 191,771H176Q169,771 169,763V748Q169,741 162,741H145Q137,741 137,733V721Q137,713 145,713H162Q169,713 169,706V690Q169,681 176,681Z", cap, edge, 3);
                AddDirection("DPadUp", "M176,681H191Q198,681 198,690V711L183.5,726L169,711V690Q169,681 176,681Z");
                AddDirection("DPadLeft", "M145,713H168L182,727L168,741H145Q137,741 137,733V721Q137,713 145,713Z");
                AddDirection("DPadRight", "M199,713H222Q229,713 229,721V733Q229,741 221,741H199L185,727Z");
                AddDirection("DPadDown", "M183.5,728L198,743V763Q198,771 191,771H176Q169,771 169,763V743Z");
                foreach (string stick in new[] { "LS", "RS" })
                    foreach (string direction in new[] { "Up", "Left", "Right", "Down" })
                    {
                        string id = stick + direction;
                        var center = Center(id);
                        var marker = Round(center.X - 15, center.Y - 14, 30, 28, 6, selectedFill, selectedLine, 3);
                        marker.Visibility = Visibility.Collapsed;
                        targets.Add(id, marker);
                        Arrow(id, direction, true);
                    }
                foreach (string direction in new[] { "Up", "Left", "Right", "Down" }) Arrow("DPad" + direction, direction, false);

                foreach (string id in new[] { "LT", "LB", "RT", "RB", "A", "B", "X", "Y" }) Label(id, id, 21);
                Label("OEM:M1", "M1", 21); Label("OEM:M2", "M2", 21);
                Label("LSClick", "L3", 21); Label("RSClick", "R3", 21);
                Label("Select", "View", 16, -36); Label("Start", "Menu", 16, 39);
                Mark("M207,829L216,822L225,829M209,828V837H223V828", ink);
                Mark("M208,878L216,863L225,877H219L216,873H211", accent);
                Mark("M980,824H996V836H980Z M983,828H985M987,828H989M991,828H993M984,832H992", ink);
                Mark("M982,866H994M982,871H994M982,876H994", ink);
                foreach (var mark in marks) Canvas.SetZIndex(mark, 1);
            }

            internal void ApplyTheme(OnexApprovedTheme theme)
            {
                body.Color = theme.Inset;
                inset.Color = OnexApprovedTheme.Blend(theme.Inset, theme.Page, .56);
                shell.Color = theme.DiagramOutline;
                seam.Color = OnexApprovedTheme.Blend(theme.Inset, theme.DiagramOutline, .46);
                var tone = theme.Accent(OnexThemeRole.Controller);
                cap.Color = OnexApprovedTheme.Blend(theme.Inset, tone, theme.ChipWash);
                edge.Color = OnexApprovedTheme.Blend(theme.DiagramOutline, tone, .40);
                ink.Color = theme.Text;
                accent.Color = theme.Accent(OnexThemeRole.Controller, true);
                selectedFill.Color = OnexApprovedTheme.Blend(theme.Inset, theme.SelectedLine, .24);
                selectedLine.Color = theme.SelectedLine;
            }

            internal void ShowSelection(string id)
            {
                if (id == selected) return;
                if (selected != null) PaintTarget(selected, false);
                selected = id;
                if (selected != null) PaintTarget(selected, true);
            }

            private void PaintTarget(string id, bool active)
            {
                var target = targets[id];
                if (target.Tag as string == "overlay")
                    target.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                else
                {
                    target.Fill = active ? selectedFill : cap;
                    target.Stroke = active ? selectedLine : edge;
                    target.StrokeThickness = active ? 4 : 3;
                }
            }

            private static Point Center(string id)
            {
                var center = ControllerSketchTargets[id].Center;
                return new Point(center.X * 2, center.Y * 2);
            }

            private void AddCap(string id, string geometry) => targets.Add(id, DrawPath(geometry, cap, edge, 3));
            private void AddCircle(string id, double radius) => targets.Add(id, Circle(Center(id), radius, cap, edge));
            private void AddDirection(string id, string geometry)
            {
                var path = DrawPath(geometry, selectedFill, selectedLine, 3);
                path.Visibility = Visibility.Collapsed;
                path.Tag = "overlay";
                targets.Add(id, path);
            }

            private Path DrawPath(string data, Brush fill, Brush stroke, double width)
            {
                var path = (Path)XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='" + data + "'/>");
                path.Fill = fill;
                path.Stroke = stroke;
                path.StrokeThickness = width;
                path.StrokeLineJoin = PenLineJoin.Round;
                path.StrokeStartLineCap = PenLineCap.Round;
                path.StrokeEndLineCap = PenLineCap.Round;
                Layer.Children.Add(path);
                return path;
            }

            private Rectangle Round(double x, double y, double width, double height, double radius, Brush fill, Brush stroke, double line = 3)
            {
                var shape = new Rectangle { Width = width, Height = height, RadiusX = radius, RadiusY = radius, Fill = fill, Stroke = stroke, StrokeThickness = line };
                Canvas.SetLeft(shape, x); Canvas.SetTop(shape, y); Layer.Children.Add(shape);
                return shape;
            }

            private Ellipse Circle(Point point, double radius, Brush fill, Brush stroke)
            {
                var shape = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = fill, Stroke = stroke, StrokeThickness = 3 };
                Canvas.SetLeft(shape, point.X - radius); Canvas.SetTop(shape, point.Y - radius); Layer.Children.Add(shape);
                return shape;
            }

            private void Label(string id, string text, double size, double offset = 0)
            {
                var center = Center(id);
                var caption = new TextBlock
                {
                    Text = text, FontSize = size, FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Width = 70, TextAlignment = TextAlignment.Center, Foreground = ink
                };
                Canvas.SetLeft(caption, center.X + offset - 35); Canvas.SetTop(caption, center.Y - size * .65);
                Layer.Children.Add(caption); marks.Add(caption);
            }

            private void Mark(string geometry, Brush color) => marks.Add(DrawPath(geometry, null, color, 2.8));
            private void Arrow(string id, string direction, bool stem)
            {
                var p = Center(id);
                var rotation = direction == "Right" ? 90 : direction == "Down" ? 180 : direction == "Left" ? -90 : 0;
                var path = DrawPath(stem ? "M-6,-1L0,-7L6,-1M0,-7V8" : "M-5,2L0,-3L5,2", null, ink, 3);
                path.RenderTransform = new CompositeTransform { Rotation = rotation, TranslateX = p.X, TranslateY = p.Y };
                marks.Add(path);
                if (stem) targets[id].Tag = "overlay";
            }
        }
    }
}
