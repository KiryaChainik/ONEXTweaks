// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        // The inner LCD outline in the 640 × 506 controller artwork.
        private static readonly Rect OnexSketchScreen = new Rect(132, 251, 338, 213);
        private OnexRemapWindow _onexInlineRemapWindow;

        private Grid HostOnexSketch(Viewbox artwork)
        {
            var surface = (ControllerSketchSurface)((Canvas)artwork.Child).Tag;
            var root = new Grid();
            var overlay = new Canvas();
            var frame = new Border
            {
                Name = artwork.Name + "Editor",
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(9, 7, 9, 6),
                Visibility = Visibility.Collapsed
            };
            var content = new Grid();
            var close = CreateOnexRemapCloseButton(() => CloseOnexSketchEditor());
            content.Children.Add(close);
            frame.Child = content;
            overlay.Children.Add(frame);
            root.Children.Add(artwork);
            root.Children.Add(overlay);

            var host = new SketchRemapHost(root, artwork, frame, content);
            surface.RemapHost = host;
            artwork.SizeChanged += (sender, args) =>
            {
                RefreshOnexSketchEditor(host);
                var popup = _onexRemapWindows.FirstOrDefault(view => view.WideSketch == host && view.Flyout.IsOpen);
                if (popup != null) ConfigureOnexRemappingWindow(popup);
            };
            frame.SizeChanged += (sender, args) => PositionOnexSketchEditor(host);
            root.Unloaded += (sender, args) =>
            {
                if (ReferenceEquals(_onexInlineRemapWindow?.Sketch, host)) CloseOnexSketchEditor(restoreFocus: false);
            };
            return root;
        }

        private Button CreateOnexRemapCloseButton(Action onClose)
        {
            var close = new Button
            {
                Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
                Style = (Style)Resources["StandaloneButtonStyle"],
                Width = 32, Height = 32, MinWidth = 0, MinHeight = 0,
                Padding = new Thickness(0), CornerRadius = new CornerRadius(6),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            };
            AutomationProperties.SetName(close, OnexStrings.Get("Close"));
            close.Click += (sender, args) => onClose();
            close.KeyDown += (sender, args) =>
            {
                if (args.Key != VirtualKey.GamepadB && args.Key != VirtualKey.Escape) return;
                args.Handled = true;
                onClose();
            };
            return close;
        }

        private sealed class SketchRemapHost
        {
            internal readonly Grid Root, Content;
            internal readonly Viewbox Artwork;
            internal readonly Border Frame;
            internal SketchRemapHost(Grid root, Viewbox artwork, Border frame, Grid content)
            { Root = root; Artwork = artwork; Frame = frame; Content = content; }
            internal Rect Screen => ((Canvas)Artwork.Child).TransformToVisual(Root).TransformBounds(OnexSketchScreen);
            internal void ShowSummary(bool show)
            {
                foreach (var label in ((Canvas)Artwork.Child).Children.OfType<TextBlock>())
                    label.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private bool IsOnexRemapOpen(Flyout flyout) =>
            flyout?.IsOpen == true || (flyout != null && ReferenceEquals(flyout, _onexInlineRemapWindow?.Flyout));

        private bool TryOpenOnexSketchEditor(Flyout flyout, Button anchor, Control returnFocus)
        {
            if (!_onexSketchOwners.TryGetValue(anchor, out var surface) || surface.RemapHost == null || flyout.IsOpen)
                return false;
            var host = surface.RemapHost;
            var view = _onexRemapWindows.Single(item => ReferenceEquals(item.Flyout, flyout));
            if (_onexInlineRemapWindow != null && !ReferenceEquals(_onexInlineRemapWindow, view))
                CloseOnexSketchEditor(restoreFocus: false);
            if (view.Sketch != null && !ReferenceEquals(view.Sketch, host)) DetachOnexSketchEditor(view);
            if (view.Sketch == null)
            {
                if (view.PopupRoot == null) flyout.Content = null;
                else view.PopupRoot.Children.Remove(view.Scroll);
                Grid.SetRow(view.Scroll, 0);
                if (view.InlineTitle != null) view.InlineTitle.Visibility = Visibility.Visible;
                host.Content.Children.Insert(0, view.Scroll);
                view.Sketch = host;
                _onexInlineRemapWindow = view;
                host.Frame.Visibility = Visibility.Visible;
                host.ShowSummary(false);
            }
            view.Anchor = anchor;
            view.ReturnFocus = returnFocus;
            RefreshOnexSketchEditor(host);
            view.Scroll.ChangeView(null, 0, null, true);
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                if (ReferenceEquals(view, _onexInlineRemapWindow) && ReferenceEquals(view.Anchor, anchor))
                    view.Focus?.Invoke();
            });
            return true;
        }

        private void RefreshOnexSketchEditor(SketchRemapHost host)
        {
            var view = _onexInlineRemapWindow;
            if (!ReferenceEquals(view?.Sketch, host) || host.Artwork.ActualWidth <= 0 || host.Artwork.ActualHeight <= 0) return;
            var screen = host.Screen;
            var theme = _onexAppliedTheme ?? OnexPalette.CreateTheme();
            host.Frame.Background = OnexDialogSurface;
            host.Frame.BorderBrush = OnexSolidBrush(theme.EffectiveAccent());
            host.Frame.Width = Math.Max(1, Math.Min(500, screen.Width - 16));
            host.Frame.MaxHeight = Math.Max(1, screen.Height - 14);
            view.Scroll.MinWidth = 0;
            view.Scroll.Width = double.NaN;
            view.Scroll.MaxWidth = double.PositiveInfinity;
            view.Scroll.MaxHeight = Math.Max(1, host.Frame.MaxHeight - 15);
            view.Scroll.Padding = new Thickness(3, 2, 8, 3);
            // Keep the close button clear of the title without taking width from the form.
            if (view.Editor.Children.FirstOrDefault() is TextBlock title) title.Padding = new Thickness(0, 0, 35, 0);
            var canvasWidth = Math.Max(1, host.Frame.Width - 33);
            ConfigureOnexRemappingControls(view, canvasWidth, Math.Max(72, Math.Min(300, OnexRemapViewport().Height - 64)));
            PositionOnexSketchEditor(host);
        }

        private static void PositionOnexSketchEditor(SketchRemapHost host)
        {
            if (host.Frame.Visibility != Visibility.Visible || host.Artwork.ActualWidth <= 0) return;
            var screen = host.Screen;
            Canvas.SetLeft(host.Frame, screen.Left + (screen.Width - host.Frame.Width) / 2);
            Canvas.SetTop(host.Frame, screen.Top + Math.Max(7, (screen.Height - host.Frame.ActualHeight) / 2));
        }

        private void DetachOnexSketchEditor(OnexRemapWindow view)
        {
            var host = view.Sketch;
            view.Sketch = null;
            host.Frame.Visibility = Visibility.Collapsed;
            host.Content.Children.Remove(view.Scroll);
            host.ShowSummary(true);
            if (view.Editor.Children.FirstOrDefault() is TextBlock title) title.Padding = new Thickness(0);
            view.Scroll.Padding = new Thickness(4, 4, 12, 4);
            if (view.PopupRoot == null) view.Flyout.Content = view.Scroll;
            else
            {
                Grid.SetRow(view.Scroll, 1);
                view.PopupRoot.Children.Add(view.Scroll);
            }
            _onexInlineRemapWindow = null;
        }

        private void CloseOnexSketchEditor(bool restoreFocus = true)
        {
            var view = _onexInlineRemapWindow;
            if (view == null) return;
            var focus = view.ReturnFocus;
            DetachOnexSketchEditor(view);
            view.Closed?.Invoke();
            if (restoreFocus) focus?.Focus(FocusState.Programmatic);
        }

        private void CloseOnexRemapEditor(Flyout flyout)
        {
            if (flyout != null && ReferenceEquals(flyout, _onexInlineRemapWindow?.Flyout)) CloseOnexSketchEditor();
            else if (flyout != null)
            {
                var view = _onexRemapWindows.FirstOrDefault(item => item.Flyout == flyout);
                if (view != null) view.ReturningToSketch = false;
                flyout.Hide();
            }
        }

        private void PrepareFullOnexRemap(Flyout flyout)
        {
            var inline = _onexInlineRemapWindow;
            if (inline == null) return;
            if (!ReferenceEquals(inline.Flyout, flyout)) CloseOnexSketchEditor(restoreFocus: false);
            else DetachOnexSketchEditor(inline);
        }

        private void MatchOnexControllerSketchEditorToAction()
        {
            var flyout = _onexControllerMappingFlyout;
            var kind = _onexControllerKind;
            if (flyout == null || kind == null || kind.SelectedIndex < 0 || kind.IsDropDownOpen) return;
            if (OnexControllerWideEditor(kind.SelectedIndex))
            {
                var inline = _onexInlineRemapWindow;
                if (inline?.Flyout != flyout) return;
                var anchor = inline.Anchor;
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    if (ReferenceEquals(inline, _onexInlineRemapWindow) && ReferenceEquals(anchor, inline.Anchor) &&
                        OnexControllerWideEditor(_onexControllerKind?.SelectedIndex ?? -1) && !_onexControllerKind.IsDropDownOpen)
                        ShowOnexControllerMappingFlyout(anchor);
                });
                return;
            }
            if (!flyout.IsOpen) return;
            var view = _onexRemapWindows.FirstOrDefault(item => item.Flyout == flyout);
            if (view?.WideSketch == null || view.ReturningToSketch) return;
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                if (flyout.IsOpen && _onexControllerMappingFlyout == flyout && _onexControllerKind == kind &&
                    kind.SelectedIndex >= 0 && !OnexControllerWideEditor(kind.SelectedIndex) && !kind.IsDropDownOpen && !view.ReturningToSketch)
                {
                    view.ReturningToSketch = true;
                    flyout.Hide();
                }
            });
        }

        private bool TryReturnOnexEditorToSketch(OnexRemapWindow view)
        {
            if (!view.ReturningToSketch) return false;
            view.ReturningToSketch = false;
            var sketch = view.WideSketch;
            view.WideSketch = null;
            _onexOpenMenus.Remove(view.Flyout);
            return sketch != null && LegionNavItem?.IsChecked == true && view.Anchor?.IsLoaded == true &&
                string.Equals(ControllerSourceId, view.Anchor.Tag as string, StringComparison.Ordinal) &&
                TryOpenOnexSketchEditor(view.Flyout, view.Anchor, view.ReturnFocus);
        }
    }
}
