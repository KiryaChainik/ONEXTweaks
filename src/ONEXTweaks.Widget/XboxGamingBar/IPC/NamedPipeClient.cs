using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using NLog;
using Windows.Data.Json;
using Windows.Foundation.Collections;

namespace XboxGamingBar.IPC
{
    /// <summary>
    /// Named Pipe client for IPC with the helper.
    /// Uses P/Invoke to bypass UWP NamedPipeClientStream limitations.
    /// </summary>
    public class NamedPipeClient : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Pipe name - must match the helper's pipe name
        /// </summary>
        private readonly string PipeName = @"\\.\pipe\ONEXTweaks.Widget.v1";

        // Win32 constants
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const int ERROR_PIPE_BUSY = 231;

        // P/Invoke declarations
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WaitNamedPipeW(
            [MarshalAs(UnmanagedType.LPWStr)] string lpNamedPipeName,
            uint nTimeOut);

        private SafeFileHandle _pipeHandle;
        private FileStream _pipeStream;
        private StreamReader _reader;
        private StreamWriter _writer;
        private readonly object _writeLock = new object();
        private readonly OnexAsyncRequestWriter _requestWriter;

        internal NamedPipeClient(Guid diagnosticOwner) : this()
        {
            PipeName = @"\\.\pipe\ONEXTweaks.ControllerDiagnostic." + diagnosticOwner.ToString("N");
        }

        public NamedPipeClient()
        {
            _requestWriter = new OnexAsyncRequestWriter(SendMessage);
        }
        private CancellationTokenSource _cancellationTokenSource;
        private Task _readerTask;
        private bool _isDisposed;
        private int _connectionOpen;
        private int _lastError; // Track last Win32 error for diagnostics

        // Request/response tracking
        private int _nextRequestId = 1;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<ValueSet>> _pendingRequests =
            new ConcurrentDictionary<int, TaskCompletionSource<ValueSet>>();

        /// <summary>
        /// Event raised when a message is received from the helper
        /// </summary>
        public event EventHandler<PipeMessageEventArgs> MessageReceived;

        /// <summary>
        /// Event raised when connected to the helper
        /// </summary>
        public event EventHandler Connected;

        /// <summary>
        /// Event raised when disconnected from the helper
        /// </summary>
        public event EventHandler Disconnected;

        /// <summary>
        /// Whether currently connected to the helper
        /// </summary>
        public bool IsConnected
        {
            get
            {
                if (Volatile.Read(ref _connectionOpen) == 0)
                    return false;

                var pipeHandle = _pipeHandle;
                return pipeHandle != null && !pipeHandle.IsInvalid && !pipeHandle.IsClosed;
            }
        }

        /// <summary>
        /// Attempts to connect to the helper's named pipe
        /// </summary>
        /// <param name="timeoutMs">Connection timeout in milliseconds</param>
        /// <returns>True if connected successfully</returns>
        public async Task<bool> ConnectAsync(int timeoutMs = 5000)
        {
            if (IsConnected)
            {
                Logger.Debug("Already connected to helper pipe");
                return true;
            }

            Logger.Info($"Connecting to helper pipe: {PipeName}");

            try
            {
                // Try to connect with timeout
                var connected = await Task.Run(() => TryConnect(timeoutMs));

                if (!connected)
                {
                    Logger.Warn($"Failed to connect to helper pipe (timeout or not available) - last Win32 error: {_lastError}");
                    return false;
                }

                // Create streams for reading/writing
                _pipeStream = new FileStream(_pipeHandle, FileAccess.ReadWrite, 4096, true);
                _reader = new StreamReader(_pipeStream, Encoding.UTF8);
                _writer = new StreamWriter(_pipeStream, Encoding.UTF8) { AutoFlush = false };

                Volatile.Write(ref _connectionOpen, 1);
                _cancellationTokenSource = new CancellationTokenSource();

                // Start reading messages in background
                _readerTask = Task.Run(() => ReadMessagesAsync(_cancellationTokenSource.Token));

                Logger.Info("Connected to helper pipe successfully");
                Connected?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error connecting to helper pipe: {ex.Message}");
                Cleanup();
                return false;
            }
        }

        /// <summary>
        /// Tries to connect to the pipe using Win32 API
        /// </summary>
        private bool TryConnect(int timeoutMs)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);

            while (DateTime.Now < deadline)
            {
                // Try to open the pipe
                _pipeHandle = CreateFileW(
                    PipeName,
                    GENERIC_READ | GENERIC_WRITE,
                    0, // No sharing
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_FLAG_OVERLAPPED,
                    IntPtr.Zero);

                if (!_pipeHandle.IsInvalid)
                {
                    Logger.Info("Pipe handle acquired successfully");
                    return true;
                }

                int error = Marshal.GetLastWin32Error();
                _lastError = error; // Track last error for logging

                if (error == ERROR_PIPE_BUSY)
                {
                    // Pipe is busy, wait for it
                    if (!WaitNamedPipeW(PipeName, (uint)Math.Min(1000, (deadline - DateTime.Now).TotalMilliseconds)))
                    {
                        // Timeout waiting for pipe
                        continue;
                    }
                }
                else
                {
                    // Pipe doesn't exist or access denied, wait a bit and retry
                    Thread.Sleep(100);
                }
            }

            return false;
        }

        /// <summary>
        /// Sends a message to the helper
        /// </summary>
        public bool SendMessage(string message) => SendMessage(message, null);

        private bool SendMessage(string message, SafeFileHandle expectedHandle)
        {
            if (!IsConnected)
            {
                Logger.Debug("Cannot send message - not connected to helper");
                return false;
            }

            try
            {
                lock (_writeLock)
                {
                    // A request queued before a reconnect must never reach
                    // the replacement connection or replay an old mutation.
                    if (expectedHandle != null && !ReferenceEquals(expectedHandle, _pipeHandle)) return false;
                    _writer.WriteLine(message);
                    _writer.Flush();
                }
                if(message.Contains("OnexWifiConnect"))Logger.Debug("Sent: Wi-Fi connection request (credentials omitted)");
                else if(message.Contains("OnexBluetoothDisconnect"))Logger.Debug("Sent: Bluetooth disconnect request (device address omitted)");
                else Logger.Debug($"Sent: {message.Substring(0, Math.Min(100, message.Length))}...");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending message to helper: {ex.Message}");
                if (expectedHandle == null || ReferenceEquals(expectedHandle, _pipeHandle)) HandleDisconnection();
                return false;
            }
        }

        /// <summary>
        /// Sends a ValueSet as JSON to the helper (fire and forget)
        /// </summary>
        public bool SendValueSet(ValueSet valueSet)
        {
            try
            {
                var json = ValueSetToJson(valueSet, 0);
                return SendMessage(json);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error serializing ValueSet: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sends a request and waits for the response.
        /// Similar to AppServiceConnection.SendMessageAsync.
        /// </summary>
        /// <param name="request">The request ValueSet to send</param>
        /// <param name="timeoutMs">Timeout in milliseconds (default 10 seconds)</param>
        /// <returns>The response ValueSet, or null on timeout/error</returns>
        public async Task<ValueSet> SendRequestAsync(ValueSet request, int timeoutMs = 10000)
        {
            var requestConnection = _pipeHandle;
            if (Volatile.Read(ref _connectionOpen) == 0 || requestConnection == null || requestConnection.IsInvalid || requestConnection.IsClosed)
            {
                Logger.Debug("Cannot send request - not connected to helper pipe");
                return null;
            }

            // Assign a unique request ID
            int requestId = Interlocked.Increment(ref _nextRequestId);

            // Create a TaskCompletionSource to wait for the response
            var tcs = new TaskCompletionSource<ValueSet>();
            _pendingRequests[requestId] = tcs;

            try
            {
                // Disconnection can finish canceling the previous requests before this one is registered.
                if (Volatile.Read(ref _connectionOpen) == 0 || !ReferenceEquals(requestConnection, _pipeHandle))
                    return null;

                // Send the request with the request ID
                var json = ValueSetToJson(request, requestId);
                Task<bool> write = _requestWriter.SendAsync(json, line => SendMessage(line, requestConnection));
                // While the write is pending, only disconnection can cancel the response task.
                // A reply that arrives early still requires a successful write.
                if (!write.IsCompleted &&
                    await Task.WhenAny(write, tcs.Task).ConfigureAwait(false) == tcs.Task &&
                    tcs.Task.IsCanceled)
                {
                    _ = ObserveDisconnectedWriteAsync(requestId, write);
                    return null;
                }

                if (!await write.ConfigureAwait(false))
                {
                    _pendingRequests.TryRemove(requestId, out _);
                    return null;
                }

                // Wait for the response with timeout
                using (var cts = new CancellationTokenSource(timeoutMs))
                {
                    cts.Token.Register(() => tcs.TrySetCanceled());
                    try
                    {
                        return await tcs.Task.ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        Logger.Warn($"Request {requestId} timed out after {timeoutMs}ms");
                        return null;
                    }
                }
            }
            finally
            {
                _pendingRequests.TryRemove(requestId, out _);
            }
        }

        private static async Task ObserveDisconnectedWriteAsync(int requestId, Task<bool> write)
        {
            try
            {
                bool sent = await write.ConfigureAwait(false);
                Logger.Debug($"Write for disconnected request {requestId} completed (sent={sent}).");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Write for disconnected request {requestId} failed: {ex.GetType().Name}.");
            }
        }

        /// <summary>
        /// Converts a ValueSet to JSON string with optional request ID
        /// </summary>
        private string ValueSetToJson(ValueSet valueSet, int requestId)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"RequestId\":{requestId}");
            foreach (var kvp in valueSet)
            {
                sb.Append(",");
                sb.Append($"\"{EscapeJson(kvp.Key)}\":");
                sb.Append(ValueToJson(kvp.Value));
            }
            sb.Append("}");
            return sb.ToString();
        }

        private string ValueToJson(object value)
        {
            if (value == null) return "null";
            if (value is string s) return $"\"{EscapeJson(s)}\"";
            if (value is bool b) return b ? "true" : "false";
            if (value is int || value is long || value is double || value is float) return ((IFormattable)value).ToString(null, System.Globalization.CultureInfo.InvariantCulture);
            return $"\"{EscapeJson(value.ToString())}\"";
        }

        private string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        /// <summary>
        /// Reads messages from the pipe
        /// </summary>
        private async Task ReadMessagesAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && IsConnected)
                {
                    var line = await _reader.ReadLineAsync();

                    if (line == null)
                    {
                        // Helper disconnected
                        Logger.Info("Helper disconnected (end of stream)");
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        Logger.Debug($"Received from helper: {line.Substring(0, Math.Min(100, line.Length))}...");

                        if (!TryReadRequestId(line, out int requestId)) continue;
                        if (requestId == 0)
                        {
                            MessageReceived?.Invoke(this, new PipeMessageEventArgs(line));
                            continue;
                        }

                        if (!_pendingRequests.ContainsKey(requestId))
                        {
                            Logger.Debug($"Dropped late or unknown reply {requestId}.");
                            continue;
                        }

                        var response = ParseJsonToValueSet(line);
                        if (response != null && _pendingRequests.TryRemove(requestId, out var tcs))
                            tcs.TrySetResult(response);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                Logger.Debug($"Pipe read error: {ex.Message}");
            }
            finally
            {
                HandleDisconnection();
            }
        }

        private static bool TryReadRequestId(string json, out int requestId)
        {
            requestId = 0;
            if (!JsonObject.TryParse(json, out _)) return false;

            // Inspect the original tokens because JsonObject folds duplicate names
            // and its numeric projection does not preserve the original spelling.
            const string requestIdName = "RequestId";
            bool found = false;
            int depth = 0;
            for (int index = 0; index < json.Length; index++)
            {
                char token = json[index];
                if (token == '{' || token == '[') { depth++; continue; }
                if (token == '}' || token == ']') { depth--; continue; }
                if (token != '"') continue;

                int openingQuote = index;
                bool escaped = false;
                while (++index < json.Length && json[index] != '"')
                {
                    if (json[index] == '\\') { escaped = true; index++; }
                }
                if (index >= json.Length) return false;
                if (depth != 1) continue;

                int colon = SkipJsonWhitespace(json, index + 1);
                if (colon >= json.Length || json[colon] != ':') continue;

                bool matches;
                if (escaped)
                {
                    string quotedName = json.Substring(openingQuote, index - openingQuote + 1);
                    if (!JsonValue.TryParse(quotedName, out var name) || name.ValueType != JsonValueType.String)
                        return false;
                    matches = string.Equals(name.GetString(), requestIdName, StringComparison.Ordinal);
                }
                else
                {
                    matches = index - openingQuote - 1 == requestIdName.Length &&
                        string.CompareOrdinal(json, openingQuote + 1, requestIdName, 0, requestIdName.Length) == 0;
                }

                if (!matches) continue;
                if (found || !TryReadCanonicalRequestId(json, colon + 1, out requestId)) return false;
                found = true;
            }
            return true;
        }

        private static bool TryReadCanonicalRequestId(string json, int index, out int requestId)
        {
            requestId = 0;
            int start = SkipJsonWhitespace(json, index);
            int end = start;
            while (end < json.Length && json[end] >= '0' && json[end] <= '9') end++;
            int length = end - start;
            if (length == 0 || length > 10 || (length > 1 && json[start] == '0')) return false;

            int separator = SkipJsonWhitespace(json, end);
            return separator < json.Length && (json[separator] == ',' || json[separator] == '}') &&
                int.TryParse(json.Substring(start, length), NumberStyles.None, CultureInfo.InvariantCulture, out requestId);
        }

        private static int SkipJsonWhitespace(string json, int index)
        {
            while (index < json.Length)
            {
                char current = json[index];
                if (current != ' ' && current != '\t' && current != '\r' && current != '\n') break;
                index++;
            }
            return index;
        }

        /// <summary>
        /// Parse JSON to ValueSet using proper JSON parsing
        /// </summary>
        private ValueSet ParseJsonToValueSet(string json)
        {
            return OnexFrontend.ParseMessage(json);
        }

        /// <summary>
        /// Handles disconnection from the helper
        /// </summary>
        private void HandleDisconnection()
        {
            if (Interlocked.Exchange(ref _connectionOpen, 0) == 0) return;
            Logger.Info("Disconnected from helper pipe");

            // Cancel all pending requests
            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.TrySetCanceled();
            }
            _pendingRequests.Clear();

            Cleanup();
            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Disconnects from the helper
        /// </summary>
        public void Disconnect()
        {
            Logger.Info("Disconnecting from helper pipe...");
            _cancellationTokenSource?.Cancel();

            try
            {
                _readerTask?.Wait(TimeSpan.FromSeconds(1));
            }
            catch { }

            HandleDisconnection();
        }

        /// <summary>
        /// Cleans up resources
        /// </summary>
        private void Cleanup()
        {
            try { _reader?.Dispose(); } catch { }
            try { _writer?.Dispose(); } catch { }
            try { _pipeStream?.Dispose(); } catch { }
            try { _pipeHandle?.Dispose(); } catch { }

            _reader = null;
            _writer = null;
            _pipeStream = null;
            _pipeHandle = null;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Disconnect();
            _cancellationTokenSource?.Dispose();
        }
    }

    /// <summary>
    /// Event args for pipe messages
    /// </summary>
    public class PipeMessageEventArgs : EventArgs
    {
        public string Message { get; }

        public PipeMessageEventArgs(string message)
        {
            Message = message;
        }
    }
}
