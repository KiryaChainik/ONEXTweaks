using Shared.Enums;
using System;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class WidgetControlProperty<ValueType, UIType> : WidgetUIProperty<ValueType, UIType> where UIType : Control
    {
        public WidgetControlProperty(ValueType inValue, Function inFunction, UIType inUI, Page inOwner) : base(inValue, inFunction, inUI, inOwner)
        {
        }

        public override async Task Sync()
        {

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => SetControlEnabled(false));
            }

            await base.Sync();

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => SetControlEnabled(true));
            }
        }

        protected virtual void SetControlEnabled(bool isEnabled)
        {
            Logger.Debug($"{GetType().Name} Set {UI.Name} {(isEnabled ? "enabled" : "disabled")}.");
            if (UI != null)
            {
                UI.IsEnabled = isEnabled && OnexFrontend.IsWriteSupported(Function);
                if (!OnexFrontend.IsWriteSupported(Function)) ToolTipService.SetToolTip(UI, global::XboxGamingBar.OnexStrings.Get("Not connected to ONEXPLAYER yet"));
            }
        }

        /// <summary>
        /// Called after batch sync completes. Enables the control since batch sync
        /// bypasses individual Sync() which normally handles enable/disable.
        /// </summary>
        public override async Task OnBatchSyncCompleted()
        {
            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => SetControlEnabled(true));
            }
        }

        public override async Task SyncForConnectionAsync(PropertySyncConnection connection)
        {
            await UpdateSyncControlAsync(connection, false);
            await base.SyncForConnectionAsync(connection);
            await UpdateSyncControlAsync(connection, true);
        }

        public override Task OnBatchSyncCompletedForConnectionAsync(PropertySyncConnection connection) =>
            UpdateSyncControlAsync(connection, true);

        private async Task UpdateSyncControlAsync(PropertySyncConnection connection, bool isEnabled)
        {
            if (UI != null && Owner != null && connection.IsCurrent)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    connection.TryRun(() => SetControlEnabled(isEnabled));
                });
            }
        }
    }
}
