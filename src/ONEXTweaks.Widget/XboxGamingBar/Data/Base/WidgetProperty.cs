using System;
using Shared.Data;
using Shared.Enums;
using System.Threading.Tasks;
using Windows.Foundation.Collections;
using Windows.UI.Core;

namespace XboxGamingBar.Data
{
    internal class WidgetProperty<ValueType> : GenericProperty<ValueType>, IConnectionScopedProperty
    {
        [ThreadStatic]
        private static WidgetProperty<ValueType> readbackProperty;
        [ThreadStatic]
        private static PropertySyncConnection readbackConnection;

        public WidgetProperty(ValueType inValue, IProperty inParentProperty, Function inFunction) : base(inValue, inParentProperty, inFunction)
        {
        }

        /// <summary>
        /// Override Sync to use the unified App.SendMessageAsync for Named Pipe communication.
        /// </summary>
        public override async Task Sync()
        {
            if (!App.IsConnected)
            {
                Logger.Warn($"Can't sync {function} - no connection.");
                return;
            }

            ApplySyncResponse(await App.SendMessageAsync(CreateSyncRequest()));
        }

        public virtual async Task SyncForConnectionAsync(PropertySyncConnection connection)
        {
            await connection.ReadAsync(CreateSyncRequest(), response =>
            {
                bool previousSuppression = SuppressRemoteSync;
                SuppressRemoteSync = true;
                try { ApplySyncResponse(response, connection); }
                finally { SuppressRemoteSync = previousSuppression; }
            });
        }

        public virtual Task OnBatchSyncCompletedForConnectionAsync(PropertySyncConnection connection) => Task.CompletedTask;

        private ValueSet CreateSyncRequest()
        {
            return new ValueSet
            {
                { nameof(Command), (int)Command.Get },
                { nameof(Function), (int)function },
            };
        }

        private void ApplySyncResponse(ValueSet response, PropertySyncConnection connection = null)
        {
            if (response != null)
            {
                if (response.TryGetValue(nameof(Content), out object responseValue))
                {
                    if (response.TryGetValue(nameof(UpdatedTime), out object updatedTimeValue))
                    {
                        var updatedTime = Convert.ToInt64(updatedTimeValue);
                        bool changed = connection == null
                            ? SetValue(responseValue, updatedTime)
                            : SetConnectionValue(connection, responseValue, updatedTime);
                        if (changed)
                        {
                            Logger.Info($"Sync {function} value {responseValue} successfully.");
                        }
                        else
                        {
                            Logger.Warn($"Got {function} value {responseValue} but can't sync.");
                        }
                    }
                    else
                    {
                        Logger.Warn($"Can't get updated time when trying to sync property {function}.");
                    }
                }
                else
                {
                    Logger.Warn($"Got empty response when trying to sync property {function}.");
                }
            }
            else
            {
                Logger.Warn($"Got no response when trying to sync property {function}.");
            }
        }

        public bool SetConnectionValue(PropertySyncConnection connection, object value, long updatedTime)
        {
            var previousProperty = readbackProperty;
            var previousConnection = readbackConnection;
            readbackProperty = this;
            readbackConnection = connection;
            try { return SetValue(value, updatedTime); }
            finally
            {
                readbackProperty = previousProperty;
                readbackConnection = previousConnection;
            }
        }

        protected PropertySyncConnection TakeReadbackConnection()
        {
            if (!ReferenceEquals(this, readbackProperty)) return null;
            var connection = readbackConnection;
            readbackProperty = null;
            readbackConnection = null;
            return connection;
        }

        protected async Task DispatchPropertyNotificationAsync(CoreDispatcher dispatcher,
            PropertySyncConnection connection, Action update)
        {
            if (connection != null && connection.TryDeferNotification(dispatcher, update)) return;

            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (connection == null) update();
                else connection.TryRun(update);
            });
        }

        protected override async Task<object> SendMessageAsync(ValueSet request)
        {
            if (!App.IsConnected)
            {
                Logger.Debug($"Widget property {function} - not connected.");
                return null;
            }

            return await App.SendMessageAsync(request);
        }

        /// <summary>
        /// Override NotifyPropertyChanged to use App.SendMessageAsync for Named Pipe communication.
        /// We call InvokePropertyChanged directly to fire the INotifyPropertyChanged event.
        /// </summary>
        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            // Call InvokePropertyChanged directly to trigger INotifyPropertyChanged events
            TakeReadbackConnection();
            InvokePropertyChanged(propertyName);

            // Skip sending to remote if suppressed (e.g., during batch sync)
            if (SuppressRemoteSync)
            {
                return;
            }

            if (!App.IsConnected)
            {
                Logger.Debug($"Widget property {function} - skipping remote sync (not connected).");
                return;
            }

            try
            {
                var request = new ValueSet
                {
                    { nameof(Command), (int)Command.Set },
                    { nameof(Function), (int)function },
                };
                request = AddValueSetContent(request);

                var response = await App.SendMessageAsync(request);
                if (response != null)
                {
                    if (response.TryGetValue(nameof(Content), out object responseValue))
                    {
                        Logger.Debug($"Notify property {function} changed {responseValue}.");
                    }
                    else if (response.TryGetValue("Error", out object errorValue))
                    {
                        Logger.Warn($"Error notifying property {function}: {errorValue}");
                    }
                    else
                    {
                        if (function != Function.None)
                        {
                            Logger.Debug($"Got response for property {function} (no Content field).");
                        }
                    }
                }
                else
                {
                    Logger.Debug($"Got no response when notifying property {function}.");
                }
            }
            catch (Exception ex)
            {
                // Catch exceptions to prevent async void from crashing the process
                Logger.Debug($"NotifyPropertyChanged failed for {function}: {ex.Message}");
            }
        }
    }
}
