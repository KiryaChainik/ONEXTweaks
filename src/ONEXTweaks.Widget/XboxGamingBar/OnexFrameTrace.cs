// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Diagnostics;
using Windows.Foundation.Diagnostics;
using Windows.Foundation.Metadata;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private static readonly LoggingChannel OnexFrameTrace = new LoggingChannel(
            "ONEXTweaks-Widget-Frame", new LoggingChannelOptions(),
            new Guid("76463203-a830-426a-995a-5323ba7f8a53"));
        private static readonly LoggingOptions OnexFrameTraceOptions = new LoggingOptions(1);
        private static readonly LoggingOptions OnexRenderedTraceOptions = new LoggingOptions(2);
        private bool _onexRenderedProbeAttached;

        private void TraceOnexFrame(string phase, string detail = "", long queuedQpc = 0, bool includeUi = true, bool rendered = false)
        {
            var options = rendered ? OnexRenderedTraceOptions : OnexFrameTraceOptions;
            if (!OnexFrameTrace.IsEnabled(LoggingLevel.Verbose, options.Keywords)) return;

            var fields = new LoggingFields();
            fields.AddInt64("Qpc", Stopwatch.GetTimestamp());
            fields.AddInt64("Frequency", Stopwatch.Frequency);
            fields.AddInt64("QueuedQpc", queuedQpc);
            fields.AddInt32("PageInstance", System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this));
            fields.AddString("Phase", phase);
            fields.AddString("Detail", detail);
            if (includeUi && Dispatcher.HasThreadAccess)
            {
                fields.AddBoolean("HomeSelected", PerformanceNavItem?.IsChecked == true);
                fields.AddBoolean("ControllerSelected", LegionNavItem?.IsChecked == true);
                fields.AddBoolean("HomeVisible", _releaseHome?.Visibility == Visibility.Visible);
                fields.AddBoolean("ControllerVisible", LegionScrollViewer?.Visibility == Visibility.Visible);
                fields.AddString("CpuText", _onexStatsValues.TryGetValue("cpuUsage", out var cpu) ? cpu.Text : "");
            }
            OnexFrameTrace.LogEvent("Checkpoint", fields, LoggingLevel.Verbose, options);
        }

        private void SetOnexRenderedProbe(bool visible)
        {
            // The passive session uses keyword 1. A separate session may request
            // a single Rendered callback with bit 2 if the system trace is ambiguous.
            bool attach = visible && OnexFrameTrace.IsEnabled(LoggingLevel.Verbose, 2) &&
                ApiInformation.IsEventPresent("Windows.UI.Xaml.Media.CompositionTarget", "Rendered");
            if (attach == _onexRenderedProbeAttached) return;
            if (attach) CompositionTarget.Rendered += OnexFirstRendered;
            else CompositionTarget.Rendered -= OnexFirstRendered;
            _onexRenderedProbeAttached = attach;
        }

        private void OnexFirstRendered(object sender, RenderedEventArgs args)
        {
            SetOnexRenderedProbe(false);
            TraceOnexFrame("FirstXamlRendered", rendered: true);
        }
    }
}
