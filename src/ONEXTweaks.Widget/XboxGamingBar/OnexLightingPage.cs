// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private RadioButton _onexLightingNav;
        private ScrollViewer _onexLightingPage;
        private ContentControl _onexLightingPageContent;
        private TextBlock _onexLightingPageStatus;
        private Button _onexLightingApply, _onexLightingCancel;
        private OnexLightingEdit _onexLightingPageEditor;
        private bool _onexLightingPageSaving;

        private void EnsureOnexLightingPage()
        {
            if (_onexLightingPage != null || !(_releaseHome?.Parent is Panel host)) return;
            _onexLightingNav = new RadioButton
            {
                Tag = "Lighting", GroupName = "MainNav", Style = (Style)Resources["NavRadioButtonStyle"],
                Content = new FontIcon { Glyph = "\uE706", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18 },
            };
            AutomationProperties.SetName(_onexLightingNav, OnexStrings.Get("Lighting"));
            ToolTipService.SetToolTip(_onexLightingNav, OnexStrings.Get("Lighting"));
            _onexLightingNav.Checked += NavRadioButton_Checked;

            _onexLightingPageContent = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            _onexLightingPageStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            _onexLightingApply = new Button
            {
                Name = "OnexLightingPageApply", Content = OnexStrings.Get("Apply"),
                HorizontalAlignment = HorizontalAlignment.Stretch, Style = OnexSettingsButtonStyle(true),
            };
            _onexLightingCancel = new Button
            {
                Name = "OnexLightingPageCancel", Content = OnexStrings.Get("Cancel"),
                HorizontalAlignment = HorizontalAlignment.Stretch, Style = OnexSettingsButtonStyle(false),
            };
            _onexLightingApply.Click += async (sender, args) => await ApplyOnexLightingPageAsync(SetOnexLightingAsync);
            _onexLightingCancel.Click += (sender, args) =>
            {
                _onexLightingPageEditor?.Refresh(OnexSavedLighting, true);
                ShowOnexLightingPageStatus(null);
            };
            var body = new StackPanel
            {
                Padding = new Thickness(16, 8, 16, 16), Spacing = 12,
                Children = { _onexLightingPageContent, _onexLightingPageStatus,
                    OnexSettingsColumns(_onexLightingApply, _onexLightingCancel) },
            };
            _onexLightingPage = new ScrollViewer
            {
                Name = "OnexLightingPage", Content = body, Visibility = Visibility.Collapsed,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                BringIntoViewOnFocusChange = true,
            };
            Grid.SetRow(_onexLightingPage, Grid.GetRow(_releaseHome));
            Grid.SetColumn(_onexLightingPage, Grid.GetColumn(_releaseHome));
            host.Children.Add(_onexLightingPage);
        }

        private void OpenOnexLightingPage()
        {
            _onexLightingPage.Visibility = Visibility.Visible;
            _onexLightingPage.ChangeView(null, 0, null, true);
            RefreshOnexLightingPage();
        }

        private void CloseOnexLightingPage()
        {
            if (_onexLightingPage?.Visibility != Visibility.Visible) return;
            _onexLightingPage.Visibility = Visibility.Collapsed;
            UnmountOnexLightingPageEditor();
        }

        private void UnmountOnexLightingPageEditor()
        {
            var editor = _onexLightingPageEditor;
            _onexLightingPageEditor = null;
            ReleaseOnexLightingEdit(editor);
            _onexLightingPageContent.Content = null;
        }

        private void RefreshOnexLightingPage()
        {
            if (_onexLightingPage?.Visibility != Visibility.Visible || _onexLightingPageSaving) return;
            var live = OnexSavedLighting;
            bool ready = App.IsConnected && live?.GetNamedBoolean("available", false) == true;
            _onexLightingApply.IsEnabled = _onexLightingCancel.IsEnabled = ready;
            if (!ready)
            {
                UnmountOnexLightingPageEditor();
                ShowOnexLightingPageStatus(OnexStrings.Get("Lighting controller unavailable"));
                return;
            }
            if (_onexLightingPageEditor != null)
            {
                _onexLightingPageEditor.Refresh(live, false);
                return;
            }
            if (_onexLightingEdit != null) return;
            ShowOnexLightingPageStatus(null);
            var editor = CreateOnexLightingEdit(live, ShowOnexLightingPageStatus);
            _onexLightingPageEditor = _onexLightingEdit = editor;
            _onexLightingPageContent.Content = OnexSettingsCard(editor.Panel);
            ApplyOnexUiFonts(_onexLightingPageContent);
            PaintOnexPageContent(_onexLightingPage, OnexNavigationAccent("Lighting"));
        }

        private void ShowOnexLightingPageStatus(string text)
        {
            _onexLightingPageStatus.Text = text ?? "";
            _onexLightingPageStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private async Task<bool> ApplyOnexLightingPageAsync(Func<JsonObject, Task<bool>> save)
        {
            var editor = _onexLightingPageEditor;
            if (editor == null || _onexLightingPageSaving) return false;
            bool restoreFocus = _onexLightingApply.FocusState == FocusState.Keyboard;
            _onexLightingPageSaving = true;
            _onexLightingPageContent.IsEnabled = _onexLightingApply.IsEnabled = _onexLightingCancel.IsEnabled = false;
            ShowOnexLightingPageStatus(null);
            try
            {
                bool applied = await editor.Apply(save);
                if (!applied && editor == _onexLightingPageEditor && string.IsNullOrEmpty(_onexLightingPageStatus.Text))
                    ShowOnexLightingPageStatus(OnexStrings.Get("Could not apply lighting settings."));
                return applied;
            }
            catch (Exception error)
            {
                if (editor == _onexLightingPageEditor) ShowOnexLightingPageStatus(error.Message);
                return false;
            }
            finally
            {
                _onexLightingPageSaving = false;
                _onexLightingPageContent.IsEnabled = true;
                RefreshOnexLightingPage();
                if (restoreFocus && editor == _onexLightingPageEditor && _onexLightingApply.IsEnabled)
                    _onexLightingApply.Focus(FocusState.Keyboard);
            }
        }
    }
}
