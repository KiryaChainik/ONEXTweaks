// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private ComboBox _onexLanguage;
        private Button _onexApplyLanguage;
        private TextBlock _onexLanguageStatus;
        private bool _onexLanguageRestarting;
        private void EnsureOnexLanguageSettings()
        {
            if (_onexLanguage != null || ThemeContent == null) return;
            var section = new StackPanel { Name = "OnexLanguageSection", Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(new TextBlock { Name = "OnexLanguageSettingTitle", Text = OnexStrings.Get("Language"), Style = (Style)Resources["OnexAppearanceTitleStyle"] });
            labels.Children.Add(new TextBlock { Text = OnexStrings.Get("Restart ONEXTweaks to apply the selected language."), Style = (Style)Resources["OnexAppearanceCaptionStyle"] });
            row.Children.Add(labels);
            _onexLanguage = OnexSettingsChoice("");
            _onexLanguage.Width = 280;
            _onexLanguage.Header = null;
            _onexLanguage.Style = (Style)Resources["ModernComboBoxStyle"];
            _onexLanguage.ClearValue(Control.FontSizeProperty);
            _onexLanguage.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_onexLanguage, 1);
            row.Children.Add(_onexLanguage);
            _onexLanguage.Name = "OnexLanguageChoice";
            _onexLanguage.Items.Add(new ComboBoxItem { Content = global::XboxGamingBar.OnexStrings.Get("English"), Tag = "en" });
            _onexLanguage.Items.Add(new ComboBoxItem { Content = global::XboxGamingBar.OnexStrings.Get("Русский"), Tag = "ru" });
            _onexLanguage.SelectedIndex = OnexStrings.Language == "ru" ? 1 : 0;
            _onexApplyLanguage = new Button { Name = "OnexApplyLanguage", Content = new TextBlock { Text = OnexStrings.Get("Apply & restart widget"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 240 }, Style = OnexSettingsButtonStyle(false), HorizontalAlignment = HorizontalAlignment.Right, Width = 280, IsEnabled = false };
            _onexLanguageStatus = new TextBlock { Text = "", FontSize = 12, TextWrapping = TextWrapping.Wrap };
            _onexLanguage.SelectionChanged += (sender, args) =>
            {
                _onexApplyLanguage.IsEnabled = !_onexLanguageRestarting && (_onexLanguage.SelectedItem as ComboBoxItem)?.Tag as string != OnexStrings.Language;
            };
            _onexApplyLanguage.Click += async (sender, args) => await ApplyOnexLanguageAsync();
            OnexInputGuard.Wire(_onexLanguage);
            ApplyOnexTileWindowControlAccent(_onexLanguage);
            section.Children.Add(row);
            section.Children.Add(_onexApplyLanguage);
            section.Children.Add(_onexLanguageStatus);
            ThemeContent.Children.Add(section);
        }
        private async Task ApplyOnexLanguageAsync()
        {
            if (_onexLanguageRestarting || !((_onexLanguage.SelectedItem as ComboBoxItem)?.Tag is string language) || language == OnexStrings.Language) return;
            var values = ApplicationData.Current.LocalSettings.Values;
            bool had = values.TryGetValue(OnexStrings.Setting, out var previous);
            _onexLanguageRestarting = true;
            _onexLanguage.IsEnabled = false;
            _onexApplyLanguage.IsEnabled = false;
            try
            {
                FlushPendingSliderProfileCommits();
                values[OnexStrings.Setting] = OnexStrings.Normalize(language);
                var reports = await ApplicationData.Current.LocalFolder.CreateFolderAsync("Reports", CreationCollisionOption.OpenIfExists);
                string nonce = Guid.NewGuid().ToString("N");
                var ticket = await reports.CreateFileAsync("language-restart.txt", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(ticket, Windows.System.Diagnostics.ProcessDiagnosticInfo.GetForCurrentProcess().ProcessId + "\n" + DateTime.UtcNow.Ticks + "\n" + nonce);
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync("RestartWidget");
                bool ready = false;
                for (int attempt = 0; attempt < 40 && !ready; attempt++)
                {
                    await Task.Delay(50);
                    try { var ack = await reports.GetFileAsync("language-restart-ready.txt"); ready = (await FileIO.ReadTextAsync(ack)) == nonce; }
                    catch (System.IO.FileNotFoundException) { }
                }
                if (!ready) throw new InvalidOperationException("Widget restart launcher did not acknowledge the request");
                // Let final UI commits leave the process. The full-trust launcher
                // waits for this widget's original PIDs and requests only our slot.
                await Task.Delay(250);
                Application.Current.Exit();
            }
            catch
            {
                if (values.TryGetValue(OnexStrings.Setting, out var current) && Equals(current, language))
                {
                    if (had) values[OnexStrings.Setting] = previous;
                    else values.Remove(OnexStrings.Setting);
                }
                _onexLanguageStatus.Text = OnexStrings.Get("Could not restart ONEXTweaks. Please try again.");
                _onexLanguageRestarting = false;
                _onexLanguage.IsEnabled = true;
                _onexApplyLanguage.IsEnabled = true;
            }
        }
    }
}
