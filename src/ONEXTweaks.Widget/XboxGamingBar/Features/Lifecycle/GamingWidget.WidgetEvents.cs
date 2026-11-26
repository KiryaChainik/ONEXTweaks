using Microsoft.Gaming.XboxGameBar;
using Microsoft.Gaming.XboxGameBar.Input;
using Microsoft.UI.Xaml.Controls;
using NLog;
using Shared.Data;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.System.Power;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.UI;
using XboxGamingBar.Data;
using XboxGamingBar.Event;
using XboxGamingBar.IPC;
using XboxGamingBar.QuickSettings;
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {

        private async void GamingWidget_RequestedThemeChanged(XboxGameBarWidget sender, object args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                SetBackgroundColor();
            });
        }

        private async void GamingWidget_SettingsClicked(XboxGameBarWidget sender, object args)
        {
            await widget.ActivateSettingsAsync();
        }

        private bool hasAppliedInitialSize = false;

        // Rising-edge tracker for the default-tab-on-open trigger (see UpdateGameBarForegroundSignal).
        // Mirrors the helper's own _lastGameBarForeground edge detection for the RB auto-hop.
        private bool _lastGameBarForegroundForTab = false;
        private bool _onexEntryForeground;

        private async Task RestoreOnexOnGameBarOpenAsync(XboxGameBarWidget sender)
        {
            bool foreground = sender?.GameBarDisplayMode == XboxGameBarDisplayMode.Foreground;
            if (!foreground) { _onexEntryForeground = false; return; }
            if (_onexEntryForeground) return;
            _onexEntryForeground = true;
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // The bridge owns ONEXTweaks activation. A second RestoreAsync here
                // races the direct Guide URI and can expose the host's Home page.
                bool restored = !OnexFrontend.UsesOwnBackend && !sender.Visible;
                if (restored) await new XboxGameBarWidgetControl(sender).RestoreAsync("GamingWidget");
                for (int i = 0; i < 50 && !sender.Visible && sender.GameBarDisplayMode == XboxGameBarDisplayMode.Foreground; i++)
                    await Task.Delay(10);
                await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet { ["OnexWidgetVisible"] = sender.Visible });
                var report = new JsonObject {
                    ["visible"] = JsonValue.CreateBooleanValue(sender.Visible),
                    ["restoreRequested"] = JsonValue.CreateBooleanValue(restored),
                    ["activationOwner"] = JsonValue.CreateStringValue(OnexFrontend.UsesOwnBackend ? "bridge" : "sdk"),
                    ["elapsedMs"] = JsonValue.CreateNumberValue(elapsed.ElapsedMilliseconds),
                    ["utc"] = JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("o"))
                };
                await FileIO.WriteTextAsync(await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-fast-entry.json", CreationCollisionOption.ReplaceExisting), report.Stringify());
            }
            catch (Exception e) { Logger.Warn("ONEX fast entry: " + e.Message); }
        }

        private async void GamingWidget_VisibleChanged(XboxGameBarWidget sender, object args)
        {
            try
            {
                if(!Dispatcher.HasThreadAccess)
                {
                    if(OnexFrontend.UsesOwnBackend)TraceOnexFrame("VisibleChangedDispatch", includeUi:false);
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,()=>GamingWidget_VisibleChanged(sender,args));return;
                }
                bool isVisible = sender?.Visible ?? false;
                if(OnexFrontend.UsesOwnBackend)
                {
                    TraceOnexFrame(isVisible?"VisibleChangedEnter":"HiddenChangedEnter");
                    SetOnexRenderedProbe(isVisible);
                }
                if(OnexFrontend.UsesOwnBackend){if(isVisible)QueueOnexOverlayPreview();else{_onexOverlayPreviewRevision++;_onexOverlayPreviewDelay?.Stop();}}
                if(OnexFrontend.UsesOwnBackend)SetOnexStatePushVisibility(isVisible);
                if (!isVisible)
                {
                    OnexControllerDiagnosticReturnHidden();
                    CloseOnexMenus(systemTransition: true);
                    // Prepare the next open while the host is hidden. A foreground
                    // callback can otherwise arrive after the previous page was painted.
                    if(OnexFrontend.UsesOwnBackend)ApplyDefaultTabOnOpen("HiddenPreparation");
                    if(OnexFrontend.UsesOwnBackend)TraceOnexFrame("HiddenPreparationComplete");
                }
                _ = App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet { ["OnexWidgetVisible"] = isVisible });
                Logger.Info($"GamingWidget_VisibleChanged: Visible={isVisible}, DisplayMode={sender?.GameBarDisplayMode.ToString() ?? "Unknown"}");
                UpdateGameBarForegroundSignal("VisibleChanged");

                // Apply the user's tab order/visibility (idempotent) and, if enabled, jump to their
                // chosen default tab. Default behaviour (option off) keeps the last position.
                if (isVisible)
                {
                    ApplyTabPrefsOnWidgetOpen();
                    ApplyDefaultTabOnOpen("VisibleChanged");
                    if(OnexFrontend.UsesOwnBackend)TraceOnexFrame("VisibleTabReady");
                    RefreshOnexLightingPage();
                    _onexLightingPageEditor?.Preview();

                    // Re-read current brightness/volume so the media sliders reflect changes made
                    // outside the app (hardware keys, Windows quick settings) while the Game Bar was
                    // closed. Retrying variant: VisibleChanged can fire before the pipe reconnects.
                    _ = RefreshMediaSliderLevelsSoonAsync();
                }

                // Resize to full height on first activation.
                // Delay to let Game Bar finish restoring its cached layout first.
                if (isVisible && !hasAppliedInitialSize && sender != null)
                {
                    if (!OnexFrontend.UsesOwnBackend) hasAppliedInitialSize = true;
                    await Task.Delay(500);
                    if (OnexFrontend.UsesOwnBackend)
                    {
                        await _onexWindowPlacement;
                        if (hasAppliedInitialSize || isUnloading || !sender.Visible ||
                            !ReferenceEquals(widget, sender)) return;
                        hasAppliedInitialSize = true;
                    }
                    var success = await sender.TryResizeWindowAsync(new Windows.Foundation.Size(OnexFrontend.UsesOwnBackend ? 720 : 464, OnexFrontend.UsesOwnBackend ? sender.WindowBounds.Height : 1080));
                    Logger.Info($"Initial TryResizeWindowAsync: width={sender.WindowBounds.Width}, success={success}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"GamingWidget_VisibleChanged failed: {ex.Message}");
            }
        }

        private void GamingWidget_GameBarDisplayModeChanged(XboxGameBarWidget sender, object args)
        {
            try
            {
                if (sender != null && sender.GameBarDisplayMode != XboxGameBarDisplayMode.Foreground)
                    CloseOnexMenus(systemTransition: true, displayModeSender: sender);
                Logger.Info($"GamingWidget_GameBarDisplayModeChanged: DisplayMode={sender?.GameBarDisplayMode.ToString() ?? "Unknown"}, Visible={sender?.Visible ?? false}");
                UpdateGameBarForegroundSignal("GameBarDisplayModeChanged");
                _ = RestoreOnexOnGameBarOpenAsync(sender);
            }
            catch (Exception ex)
            {
                Logger.Error($"GamingWidget_GameBarDisplayModeChanged failed: {ex.Message}");
            }
        }

        /// <summary>Game Bar told us our home-bar favorite state changed (user added/removed CTW). Re-read
        /// and report it so Center onboarding's "add CTW to the Game Bar" step updates live.</summary>
        private void GamingWidget_FavoritedChanged(XboxGameBarWidget sender, object args)
        {
            ReportGameBarFavorited();
        }

        /// <summary>Read <c>XboxGameBarWidget.Favorited</c> (is CTW in the Game Bar home bar?) and push it
        /// to the helper. The ONLY reliable presence signal — the Game Bar profile files don't persist it
        /// (see reverse_engineered/RE_GameBar_WidgetBar_Order.md). The exact slot is not obtainable.</summary>
        private void ReportGameBarFavorited()
        {
            try
            {
                bool fav = widget?.Favorited ?? false;
                gameBarWidgetFavorited?.SetValue(fav);

                // Send DIRECTLY too: WidgetProperty only pushes on a value *change* while connected, and
                // the first read usually happens before the pipe connects — so the value would otherwise
                // never reach the helper (it stays True locally and never "changes" again). A direct,
                // idempotent Set guarantees delivery. Also re-sent on pipe connect (see OnPipeConnectedAsync).
                if (App.IsConnected)
                {
                    var msg = new Windows.Foundation.Collections.ValueSet
                    {
                        { "Command", (int)Shared.Enums.Command.Set },
                        { "Function", (int)Shared.Enums.Function.GameBarWidgetFavorited },
                        { "Content", fav ? "true" : "false" },
                    };
                    _ = App.SendMessageAsync(msg);
                }
                Logger.Info($"[GameBarFavorited] widget.Favorited = {fav} → reported to helper (connected={App.IsConnected})");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[GameBarFavorited] read/report failed: {ex.Message}");
            }
        }

        private void UpdateGameBarForegroundSignal(string source)
        {
            try
            {
                bool widgetVisible = widget?.Visible ?? false;
                XboxGameBarDisplayMode displayMode = widget?.GameBarDisplayMode ?? XboxGameBarDisplayMode.Foreground;

                // Full Game Bar visibility signal:
                // - true when the overlay is foreground (even if this specific widget tab is not selected)
                // - false when app is backgrounded or Game Bar is not in foreground mode
                bool gameBarForeground = !appIsInBackground && displayMode == XboxGameBarDisplayMode.Foreground;
                isForeground.ForceSetValue(gameBarForeground);

                Logger.Info($"GameBar foreground signal update ({source}): value={gameBarForeground}, displayMode={displayMode}, widgetVisible={widgetVisible}, appBackground={appIsInBackground}");

                // Default-tab-on-open: fire on the foreground RISING edge — the SAME false→true edge the
                // helper's RB auto-hop rides. This edge lands ~2 s BEFORE our own VisibleChanged=True (the
                // RB-hop has to bring us into view first), so switching the tab here means the correct tab
                // is already shown by the time ONEXTweaks scrolls into view — instead of only reacting once
                // Visible flips true. NavRadioButton_Checked only toggles ScrollViewer visibility, so it is
                // safe to run while this widget is still off-screen. Idempotent (VisibleChanged still calls
                // it too); the earliest successful call wins and the rest are cheap no-ops.
                if (gameBarForeground && !_lastGameBarForegroundForTab)
                {
                    ApplyDefaultTabOnOpen("ForegroundEdge:" + source);

                    // Re-read brightness/volume on EVERY return-to-foreground edge, not only on
                    // VisibleChanged. When the widget is merely backgrounded (Windows brightness/volume
                    // flyout, hardware keys, switching away and back) it stays "Visible", so
                    // VisibleChanged doesn't re-fire and the sliders kept their stale value. This edge
                    // (which also rides LeavingBackground/DisplayModeChanged) covers those cases so an
                    // external change is reflected as soon as ONEXTweaks is foreground again.
                    _ = RefreshMediaSliderLevelsSoonAsync();
                }
                _lastGameBarForegroundForTab = gameBarForeground;
            }
            catch (Exception ex)
            {
                Logger.Warn($"UpdateGameBarForegroundSignal failed ({source}): {ex.Message}");
            }
        }

    }
}
