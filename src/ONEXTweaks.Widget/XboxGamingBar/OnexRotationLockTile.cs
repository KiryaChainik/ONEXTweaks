// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using XboxGamingBar.QuickSettings;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private JsonObject _onexRotationLockState;
        private bool _onexRotationLockBusy;
        private bool OnexRotationLockAvailable => App.IsConnected &&
            _onexRotationLockState?.GetNamedBoolean("available", false) == true;
        private bool OnexRotationLocked => OnexRotationLockAvailable && _onexRotationLockState.GetNamedBoolean("locked", false);

        private void ApplyOnexRotationLockState(JsonObject state)
        {
            if (state == null) return;
            if (_onexRotationLockState != null &&
                state.GetNamedNumber("observedAt", 0) < _onexRotationLockState.GetNamedNumber("observedAt", 0)) return;
            _onexRotationLockState = state;
        }

        private async Task ToggleOnexRotationLockAsync()
        {
            if (_onexRotationLockBusy || !OnexRotationLockAvailable) return;
            _onexRotationLockBusy = true;
            ApplyOnexTileAvailability();
            try
            {
                bool requested = !OnexRotationLocked;
                var reply = await SendOnexHomeRequestAsync(new ValueSet { ["OnexRotationLockSet"] = requested });
                var state = JsonObject.Parse((string)reply["OnexRotationLockState"]);
                ApplyOnexRotationLockState(state);
                if (!state.GetNamedBoolean("available", false) || state.GetNamedBoolean("locked", !requested) != requested)
                    ShowOnexDesktopStatus(OnexStrings.Get(state.GetNamedString("reason", "Rotation state unavailable")));
            }
            catch (Exception error) { ShowOnexDesktopStatus(OnexStrings.Get(error.Message)); }
            finally
            {
                _onexRotationLockBusy = false;
                ApplyOnexTileAvailability();
            }
        }

        private void ApplyOnexRotationLockTile(TileDefinition tile)
        {
            if (tile.StateText == null || tile.TileButton == null) return;
            string reason = _onexRotationLockState?.GetNamedString("reason", "") ?? "";
            bool available = OnexRotationLockAvailable;
            string state = _onexRotationLockBusy ? "Applying…" : !available ? "Unavailable" :
                OnexRotationLocked ? "Locked" : reason.Length != 0 ? "Paused" : "Unlocked";
            tile.StateText.Text = OnexStrings.Get(state);
            string details = reason.Length != 0 ? reason : !available ? "Rotation state unavailable" :
                OnexRotationLocked ? "Unlock screen rotation" : "Lock screen rotation";
            details = OnexStrings.Get(details);
            ToolTipService.SetToolTip(tile.TileButton, details);
            AutomationProperties.SetHelpText(tile.TileButton, details);
            ApplyOnexToggleTileVisual(tile, available && OnexRotationLocked, available);
            UpdateTileScrollAnimation(tile);
        }

        private void InsertOnexRotationLockTile()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            if (values.ContainsKey("QS_OnexRotationLock_Order") ||
                !qsTileMap.TryGetValue("OnexRotationLock", out var rotation)) return;
            var ordered = qsTileDefinitions.Where(tile => tile != rotation).OrderBy(tile => tile.Order).ToList();
            int fullscreen = ordered.FindIndex(tile => tile.Id == "Fullscreen");
            ordered.Insert(fullscreen < 0 ? ordered.Count : fullscreen, rotation);
            for (int i = 0; i < ordered.Count; i++)
            {
                var tile = ordered[i];
                tile.Order = i;
                values[$"QS_{tile.Id}_Order"] = i;
            }
            rotation.IsVisible = true;
            values["QS_OnexRotationLock_Visible"] = true;
        }

        private async Task VerifyOnexRotationLockTileAsync(JsonObject report)
        {
            var previous = GetVisibleNavigationItems().FirstOrDefault(item => item.IsChecked == true);
            try
            {
                QuickNavItem.IsChecked = true;
                await RefreshOnexDisplayAsync(false);
                ApplyOnexTileAvailability();
                if (!qsTileMap.TryGetValue("OnexRotationLock", out var tile) ||
                    tile.Order + 1 != qsTileMap["Fullscreen"].Order || !tile.IsVisible || tile.StateText == null)
                    throw new InvalidOperationException("Rotation Lock is missing from Home before Fullscreen.");
                string reason = _onexRotationLockState?.GetNamedString("reason", "") ?? "";
                string expected = OnexRotationLocked ? "Locked" : reason.Length != 0 ? "Paused" : "Unlocked";
                if (!OnexRotationLockAvailable || !tile.IsActionable || tile.StateText.Text != OnexStrings.Get(expected))
                    throw new InvalidOperationException("The built-in display's rotation state did not agree with Home.");
                report["rotationLockReadbackPassed"] = JsonValue.CreateBooleanValue(true);
                report["rotationLockToggled"] = JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                if (previous != null) previous.IsChecked = true;
            }
        }
    }
}
