// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private readonly ConditionalWeakTable<Button, ControllerSketchSurface> _onexSketchOwners =
            new ConditionalWeakTable<Button, ControllerSketchSurface>();

        private Canvas CreateOnexControllerDeviceArt()
        {
            var surface = new ControllerSketchSurface();
            OnexApprovedTheme.Bind(surface.View, OnexCurrentPalette, surface.ApplyTheme);
            return surface.View;
        }

        private TextBlock AddOnexControllerSketchLabel(Canvas canvas, double y, double size, Brush color)
        {
            var label = new TextBlock
            {
                Width = 330, FontSize = size, Foreground = color, TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false,
            };
            Canvas.SetLeft(label, 136);
            Canvas.SetTop(label, y);
            canvas.Children.Add(label);
            return label;
        }

        private Button AddOnexControllerSketchButton(Canvas canvas, string id, string prefix, Action<Button> edit)
        {
            var name = OnexControllerSourceName(id);
            var surface = (ControllerSketchSurface)canvas.Tag;
            var button = surface.Add(id, prefix + id.Replace(":", ""), edit);
            _onexSketchOwners.Add(button, surface);
            AutomationProperties.SetName(button, name);
            return button;
        }

        private void PaintOnexControllerSketchButton(Button button, bool selected)
        {
            if (_onexSketchOwners.TryGetValue(button, out var surface)) surface.SetSelected(button, selected);
        }

        private sealed class ControllerSketchTarget
        {
            private readonly Point[] _polygon;

            public ControllerSketchTarget(Point center, string polygon)
            {
                Center = new Point(center.X / 2, center.Y / 2);
                _polygon = polygon.Split(' ').Select(vertex =>
                {
                    var coordinates = vertex.Split(',');
                    return new Point(double.Parse(coordinates[0], CultureInfo.InvariantCulture) / 2,
                                     double.Parse(coordinates[1], CultureInfo.InvariantCulture) / 2);
                }).ToArray();
                Bounds = new Rect(new Point(_polygon.Min(point => point.X), _polygon.Min(point => point.Y)),
                                  new Point(_polygon.Max(point => point.X), _polygon.Max(point => point.Y)));
            }

            public Point Center { get; }
            public Rect Bounds { get; }

            public bool Contains(Point point)
            {
                if (!Bounds.Contains(point)) return false;
                bool inside = false;
                for (int i = 0, j = _polygon.Length - 1; i < _polygon.Length; j = i++)
                {
                    var a = _polygon[i];
                    var b = _polygon[j];
                    if ((a.Y > point.Y) != (b.Y > point.Y) &&
                        point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                        inside = !inside;
                }
                return inside;
            }
        }

        private sealed class ControllerSketchSurface
        {
            private readonly Dictionary<Button, Action<Button>> _editors = new Dictionary<Button, Action<Button>>();
            private readonly ControllerSketchArt _art = new ControllerSketchArt();
            private readonly Canvas _tapPlane;
            private Button _selected, _pressed;
            private uint? _pointer;
            private Point _origin;
            private int _activation;

            public ControllerSketchSurface()
            {
                View = new Canvas { Width = 640, Height = 506, Tag = this };
                View.Children.Add(_art.Layer);
                // Keep invokable native buttons for accessibility. This plane owns pointer geometry.
                _tapPlane = new Canvas { Width = View.Width, Height = View.Height, Background = OnexSolidBrush(Colors.Transparent) };
                Canvas.SetZIndex(_tapPlane, 2);
                View.Children.Add(_tapPlane);
                _tapPlane.PointerPressed += OnPress;
                _tapPlane.PointerMoved += OnMove;
                _tapPlane.PointerReleased += OnRelease;
                _tapPlane.PointerCanceled += OnCancel;
                _tapPlane.PointerCaptureLost += OnCancel;
                _tapPlane.PointerExited += OnCancel;
                View.Unloaded += (sender, args) => Cancel();
            }

            public void ApplyTheme(OnexApprovedTheme theme) => _art.ApplyTheme(theme);

            public Canvas View { get; }
            internal SketchRemapHost RemapHost { get; set; }
            public Windows.UI.Xaml.Shapes.Shape HighlightShape => _art.Selection;
            public string HighlightedId => HighlightShape == null ? null : (_pressed ?? _selected)?.Tag as string;
            private static ControllerSketchTarget Target(Button button) => ControllerSketchTargets[(string)button.Tag];

            public Button Add(string id, string name, Action<Button> edit)
            {
                var target = ControllerSketchTargets[id];
                var bounds = target.Bounds;
                // Use centered invisible anchors so the editor stays attached to the printed control.
                var button = new Button
                {
                    Name = name, Tag = id, Width = bounds.Width, Height = bounds.Height, MinWidth = 0, MinHeight = 0,
                    Opacity = 0, IsTabStop = false, UseSystemFocusVisuals = false,
                };
                Canvas.SetLeft(button, target.Center.X - button.Width / 2);
                Canvas.SetTop(button, target.Center.Y - button.Height / 2);
                OnexApprovedTheme.OwnTarget(button);
                _editors.Add(button, edit);
                button.Click += (sender, args) => Activate(button);
                View.Children.Add(button);
                return button;
            }

            public void SetSelected(Button button, bool selected)
            {
                if (selected) _selected = button;
                else if (ReferenceEquals(_selected, button)) _selected = null;
                Paint();
            }

            private void Paint()
            {
                var current = _pressed ?? _selected;
                _art.ShowSelection(current?.IsEnabled == true ? (string)current.Tag : null);
            }

            public Button Hit(Point point) => _editors.Keys.FirstOrDefault(button => button.IsEnabled && Target(button).Contains(point));

            private void OnPress(object sender, PointerRoutedEventArgs args)
            {
                if (_pointer.HasValue) return;
                var point = args.GetCurrentPoint(View);
                if (point.PointerDevice.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed) return;
                var button = Hit(point.Position);
                if (button == null) return;
                _pointer = args.Pointer.PointerId;
                _origin = args.GetCurrentPoint(null).Position;
                StartPress(button);
            }

            private void OnMove(object sender, PointerRoutedEventArgs args)
            {
                if (_pointer != args.Pointer.PointerId) return;
                var point = args.GetCurrentPoint(null).Position;
                var dx = point.X - _origin.X;
                var dy = point.Y - _origin.Y;
                if (dx * dx + dy * dy > 100) Cancel();
            }

            private void OnRelease(object sender, PointerRoutedEventArgs args)
            {
                if (_pointer != args.Pointer.PointerId) return;
                _pointer = null;
                var button = Hit(args.GetCurrentPoint(View).Position);
                if (button != null && ReferenceEquals(button, _pressed)) Activate(button);
                else Cancel();
            }

            private void OnCancel(object sender, PointerRoutedEventArgs args)
            {
                if (_pointer == args.Pointer.PointerId) Cancel();
            }

            public void StartPress(Button button)
            {
                ++_activation;
                _pressed = button;
                Paint();
            }

            public void Cancel()
            {
                ++_activation;
                _pointer = null;
                _pressed = null;
                Paint();
            }

            private async void Activate(Button button)
            {
                if (!button.IsEnabled) return;
                StartPress(button);
                int activation = _activation;
                var frame = new TaskCompletionSource<bool>();
                int frames = 0;
                EventHandler<object> rendering = (sender, args) => { if (++frames == 2) frame.TrySetResult(true); };
                CompositionTarget.Rendering += rendering;
                try
                {
                    await Task.WhenAny(frame.Task, Task.Delay(120));
                    if (_activation == activation && button.IsEnabled && IsVisibleInTree())
                        _editors[button](button);
                }
                finally
                {
                    CompositionTarget.Rendering -= rendering;
                    if (_activation == activation)
                    {
                        _pressed = null;
                        Paint();
                    }
                }
            }

            private bool IsVisibleInTree()
            {
                if (View.XamlRoot == null) return false;
                for (DependencyObject node = View; node != null; node = VisualTreeHelper.GetParent(node))
                    if (node is UIElement element && element.Visibility != Visibility.Visible) return false;
                return true;
            }
        }
    }
}
