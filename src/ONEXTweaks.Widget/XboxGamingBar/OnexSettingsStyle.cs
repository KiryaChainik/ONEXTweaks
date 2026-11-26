// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private readonly HashSet<FlyoutBase> _onexOpenMenus = new HashSet<FlyoutBase>();
        private T TrackOnexMenu<T>(T menu) where T : FlyoutBase
        {
            _onexOpenMenus.Add(menu); menu.Closed += (s,e) => _onexOpenMenus.Remove(menu);
            ConfigureOnexCatalogFlyout(menu);menu.Opened+=(s,e)=>ConfigureOnexCatalogFlyout(menu);return menu;
        }
        private void CloseOnexMenus(bool systemTransition = false, Microsoft.Gaming.XboxGameBar.XboxGameBarWidget displayModeSender = null)
        {
            if (!OnexFrontend.UsesOwnBackend) return;
            if (!Dispatcher.HasThreadAccess) { _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.High, () => CloseOnexMenus(systemTransition, displayModeSender)); return; }
            // A queued display-mode notification may outlive its widget or its transition.
            if (displayModeSender != null && (!ReferenceEquals(displayModeSender, widget) ||
                (displayModeSender.Visible && displayModeSender.GameBarDisplayMode == Microsoft.Gaming.XboxGameBar.XboxGameBarDisplayMode.Foreground)))
                return;
            CloseOnexSketchEditor(restoreFocus: false);
            if(qsEditMode)QuickSettingsCustomizeDone_Click(QuickSettingsCustomizeCloseTopButton,null);
            // Windows displays its own consent dialog for every custom Bluetooth pairing.
            // Losing Game Bar foreground to that dialog is not a user cancellation.
            if (systemTransition && PreserveOnexBluetoothPairingDialog())
            {
                foreach (var menu in _onexOpenMenus.ToArray()) menu.Hide();
                _onexOpenMenus.Clear();
                return;
            }
            _onexDetailsDialog?.Hide();
            _onexCloseDialog?.Hide();
            foreach (var menu in _onexOpenMenus.ToArray()) menu.Hide();
            _onexOpenMenus.Clear();
            foreach (var popup in VisualTreeHelper.GetOpenPopups(Window.Current).ToArray()) popup.IsOpen = false;
            // The startup picker can still report open after its Popup has left the window's list.
            if (systemTransition && _onexTabsOpen?.IsDropDownOpen == true && widget != null &&
                (!widget.Visible || widget.GameBarDisplayMode != Microsoft.Gaming.XboxGameBar.XboxGameBarDisplayMode.Foreground))
            {
                _onexTabsOpen.IsDropDownOpen = false;
            }
        }
        private FrameworkElement OnexHelpHeader(string label, string explanation)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new TextBlock { Text = OnexStrings.Get(label), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            var help = new Button { Content = "?", Width = 24, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(12), Foreground = OnexDialogAccent, Background = OnexSettingsBrush(48,58,69) };
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(help,global::XboxGamingBar.OnexStrings.Format("Пояснение: {0}",label));
            var text = new TextBlock { Text = OnexStrings.Get(explanation), TextWrapping = TextWrapping.Wrap, FontSize = 13, MaxWidth = Math.Max(240, Math.Min(340, ActualWidth - 80)), Foreground = new SolidColorBrush(Colors.White) };
            var presenter = new Style { TargetType = typeof(FlyoutPresenter) };
            presenter.Setters.Add(new Setter(Control.BackgroundProperty, OnexDialogSurface));
            presenter.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(16)));
            presenter.Setters.Add(new Setter(Control.CornerRadiusProperty,new CornerRadius(12)));
            var flyout = new Flyout { Content = text, FlyoutPresenterStyle = presenter };
            flyout.Closed += (s,e) => _onexOpenMenus.Remove(flyout);
            help.Click += (s,e) => { _onexOpenMenus.Add(flyout); flyout.ShowAt(help); };
            row.Children.Add(help); return row;
        }
        private void ApplyOnexTileWindowControlAccent(Control control)
        {
            var accent=OnexDialogAccent;
            SetOnexThemeResourceGroup(control,"accent",new[]{"ToggleSwitchFillOn","ToggleSwitchFillOnPointerOver","ToggleSwitchFillOnPressed","ToggleSwitchStrokeOn","ToggleSwitchStrokeOnPointerOver","ToggleSwitchStrokeOnPressed","ListViewItemSelectionIndicatorBrush","ListViewItemSelectionIndicatorPointerOverBrush","ListViewItemSelectionIndicatorPressedBrush","CheckBoxCheckBackgroundFillChecked","CheckBoxCheckBackgroundFillCheckedPointerOver","CheckBoxCheckBackgroundFillCheckedPressed","CheckBoxCheckBackgroundStrokeChecked","CheckBoxCheckBackgroundStrokeCheckedPointerOver","CheckBoxCheckBackgroundStrokeCheckedPressed","SystemControlHighlightAccentBrush"},accent.Color);
            if(control is ComboBox choice){if(OnexBrandTheme)OnexAccentResources(choice,OnexPalette.Orange);}
            if(control is ToggleSwitch)SetOnexThemeBrush(control,Control.BackgroundProperty,accent.Color);
            if(control is CheckBox ownCheck)UpdateOnexOwnedCheck(ownCheck,accent.Color);
            ConfigureOnexFocus(control);
        }
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement,Dictionary<string,SolidColorBrush>> _onexOwnedResourceGroups =
            new System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement,Dictionary<string,SolidColorBrush>>();
        private int _onexOwnedResourceBrushesCreated;
        private SolidColorBrush OnexOwnedResourceBrush(FrameworkElement scope,string role,Color colour)
        {
            var groups=_onexOwnedResourceGroups.GetValue(scope,_=>new Dictionary<string,SolidColorBrush>());
            if(!groups.TryGetValue(role,out var brush)){groups[role]=brush=new SolidColorBrush(colour);_onexOwnedResourceBrushesCreated++;}
            else if(brush.Color!=colour)brush.Color=colour;
            return brush;
        }
        private void SetOnexThemeResourceGroup(Control control,string role,IEnumerable<string> keys,Color colour)
        {
            // One mutable brush per semantic role and control. Native templates
            // keep that reference through theme changes; other controls and the
            // immutable shared colour cache never receive its mutations.
            var brush=OnexOwnedResourceBrush(control,role,colour);
            foreach(string key in keys)try{if(!control.Resources.TryGetValue(key,out var current)||!ReferenceEquals(current,brush))control.Resources[key]=brush;}
            catch(Exception ex){Logger.Warn("Optional ONEX grouped resource rejected: "+key+" / "+ex.HResult.ToString("X8"));}
        }
        [ThreadStatic] private static Dictionary<uint,SolidColorBrush> _onexSolidBrushes;
        private static SolidColorBrush OnexSolidBrush(Color color)
        {
            // Brushes are XAML objects tied to their dispatcher. Cache per UI thread;
            // callers treat these shared color values as immutable.
            uint key=((uint)color.A<<24)|((uint)color.R<<16)|((uint)color.G<<8)|color.B;
            if(_onexSolidBrushes==null)_onexSolidBrushes=new Dictionary<uint,SolidColorBrush>();
            if(!_onexSolidBrushes.TryGetValue(key,out var brush))
            {
                if(_onexSolidBrushes.Count>=256)_onexSolidBrushes.Clear();
                _onexSolidBrushes[key]=brush=new SolidColorBrush(color);
            }
            return brush;
        }
        private static SolidColorBrush OnexSettingsBrush(byte r, byte g, byte b) => OnexSolidBrush(Color.FromArgb(255,r,g,b));
        private Grid OnexSettingsColumns(FrameworkElement left, FrameworkElement right)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(left); Grid.SetColumn(right, 1); row.Children.Add(right);
            return row;
        }
        private Border OnexSettingsCard(UIElement content)
        {
            var card=new Border { Background = OnexCatalogTheme&&_onexAppliedTheme!=null?new SolidColorBrush(_onexAppliedTheme.ButtonBackground):OnexBrandTheme?new SolidColorBrush(OnexPalette.Button):OnexSettingsBrush(40,45,52),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10), Child = content };
            if(_onexAppliedTheme!=null)ApplyOnexCatalogSurfaces(card,_onexAppliedTheme);
            return card;
        }
        private Grid OnexSettingsInlineToggle(FrameworkElement label, ToggleSwitch toggle)
        {
            var row = new Grid { ColumnSpacing = 8, MinHeight = 36 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if(label is TextBlock caption)caption.FontSize = 13;
            label.VerticalAlignment = VerticalAlignment.Center;
            toggle.Header = null; toggle.OnContent = ""; toggle.OffContent = ""; toggle.MinWidth = 0;
            row.Children.Add(label); Grid.SetColumn(toggle, 1); row.Children.Add(toggle);
            return row;
        }
        private DataTemplate _onexChoiceTextTemplate;
        private ComboBox OnexSettingsChoice(string label)
        {
            if(_onexChoiceTextTemplate==null)_onexChoiceTextTemplate=(DataTemplate)Windows.UI.Xaml.Markup.XamlReader.Load(
                "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><TextBlock Text=\"{Binding}\" TextWrapping=\"Wrap\" MaxWidth=\"320\" FontSize=\"13\" /></DataTemplate>");
            var choice=new ComboBox {
            Header = OnexStrings.Get(label), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 13, FocusVisualMargin = new Thickness(2),
            MinHeight = 38, MaxDropDownHeight = 420,
            ItemTemplate = _onexChoiceTextTemplate
        };ConfigureOnexFocus(choice);return choice;
        }
        private ContentDialog CreateOnexSettingsDialog(string title, string description, FrameworkElement content)
        {
            double width = Math.Max(280, Math.Min(560, ActualWidth - 24));
            // The ScrollViewer reserves space for its bar; a fixed child width clips focused controls.
            var body = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
            if (!string.IsNullOrEmpty(description)) body.Children.Add(new TextBlock {
                Text = description, TextWrapping = TextWrapping.Wrap, FontSize = 12,
                Foreground = OnexSettingsBrush(170, 181, 193)
            });
            body.Children.Add(content);
            var dialog = new ContentDialog {
                Title = title, RequestedTheme = ElementTheme.Dark, FontFamily = FontFamily,
                Background = OnexDialogSurface, Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new ScrollViewer { Content = body, MaxHeight = Math.Max(220, ActualHeight - 170),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    BringIntoViewOnFocusChange = true },
                PrimaryButtonText = global::XboxGamingBar.OnexStrings.Get("Apply"), CloseButtonText = global::XboxGamingBar.OnexStrings.Get("Cancel"), DefaultButton = ContentDialogButton.Primary
            };
            dialog.Resources[typeof(Slider)] = Resources["OnexPreciseSliderStyle"];
            dialog.PreviewKeyDown+=(sender,args)=>OnexInputGuard.HandleFocusedSlider(args);
            dialog.Resources["ContentDialogMinWidth"] = width;
            dialog.Resources["ContentDialogMaxWidth"] = width;
            dialog.Resources["ContentDialogCornerRadius"] = new CornerRadius(12);
            // Dialog chrome is a surface, not a selected control. Keep the
            // native template's own border resource consistent with the property.
            dialog.Resources["ContentDialogBorderBrush"] = new SolidColorBrush(Colors.Transparent);
            dialog.Resources["ContentDialogBorderThickness"] = new Thickness(0);
            dialog.Resources["ControlCornerRadius"] = new CornerRadius(8);
            dialog.Resources["ContentDialogBackground"] = dialog.Background;
            dialog.Resources["SystemControlHighlightAccentBrush"] = OnexDialogAccent;
            dialog.PrimaryButtonStyle = OnexDialogFocusButtonStyle(true); dialog.CloseButtonStyle = OnexDialogFocusButtonStyle(false);
            ApplyOnexDialogAccents(dialog);
            return dialog;
        }
        private Style OnexSettingsButtonStyle(bool primary)
        {
            var theme=_onexAppliedTheme??OnexPalette.CreateTheme();var accent=OnexDialogAccent;
            var ink=OnexContrast(Colors.Black,accent.Color)>OnexContrast(Colors.White,accent.Color)?Colors.Black:Colors.White;
            var style = new Style { TargetType = typeof(Button) };
            style.Setters.Add(new Setter(Control.TemplateProperty,OnexInlineFocusTemplate(typeof(Button))));
            style.Setters.Add(new Setter(Control.BackgroundProperty, primary ? accent : OnexSolidBrush(theme.ButtonBackground)));
            style.Setters.Add(new Setter(Control.ForegroundProperty,OnexSolidBrush(primary?ink:theme.TextPrimary)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty,OnexSolidBrush(primary?accent.Color:theme.CardBorder)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(18, 10, 18, 10)));
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 42d));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
            style.Setters.Add(new Setter(Control.FocusVisualPrimaryBrushProperty,OnexSolidBrush(OnexFocusColour)));
            style.Setters.Add(new Setter(Control.FocusVisualSecondaryBrushProperty,OnexSolidBrush(Colors.Transparent)));
            style.Setters.Add(new Setter(Control.FocusVisualPrimaryThicknessProperty,new Thickness(2)));
            style.Setters.Add(new Setter(Control.FocusVisualSecondaryThicknessProperty,new Thickness(0)));
            style.Setters.Add(new Setter(Control.FocusVisualMarginProperty,new Thickness(2)));
            return style;
        }
        private MenuFlyout CreateOnexMenu(FlyoutPlacementMode placement = FlyoutPlacementMode.Top)
        {
            var presenter = new Style { TargetType = typeof(MenuFlyoutPresenter) };
            presenter.Setters.Add(new Setter(Control.BackgroundProperty, OnexDialogSurface));
            presenter.Setters.Add(new Setter(Control.BorderBrushProperty, OnexDialogBorder));
            presenter.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            presenter.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(12)));
            presenter.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6)));
            presenter.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, Math.Min(280, Math.Max(220, ActualWidth - 32))));
            var menu = new MenuFlyout { Placement = placement, MenuFlyoutPresenterStyle = presenter };
            menu.Opening += (s, e) => {
                foreach (var item in menu.Items) { if(item is MenuFlyoutSeparator){item.MinHeight=0;continue;} item.FontFamily = FontFamily; item.FontSize = 14; item.MinHeight = 44; item.Foreground = OnexSolidBrush(_onexAppliedTheme?.TextPrimary??Colors.White);ConfigureOnexOwnedMenuItem(item); }
            };
            return TrackOnexMenu(menu);
        }
        private void ApplyOnexToggleTileVisual(TileDefinition tile, bool on, bool known)
        {
            var accent = OnexSolidBrush(OnexSemanticAccent(OnexTileColorRole(tile.Id), true));
            var muted = OnexMutedTileBrush;
            if (tile.StateText != null) tile.StateText.Foreground = known && on ? accent : muted;
            if (tile.TileButton != null) {
                PaintOnexTileColors(tile, on, known);
                tile.TileButton.Foreground = known && on ? OnexSolidBrush(Colors.White) : muted;
            }
            if (tile.NameText != null) tile.NameText.Foreground = known && on ? OnexSolidBrush(Colors.White) : muted;
            var iconBrush = known && on ? OnexSolidBrush(Colors.White) : muted;
            if (tile.IconElement is IconElement icon) icon.Foreground = iconBrush;
            if (tile.IconElement is Viewbox view && view.Child is IconElement childIcon) childIcon.Foreground = iconBrush;
            if (tile.IconElement != null) tile.IconElement.Opacity = known && on ? 1 : 0.5;
        }
    }
}
