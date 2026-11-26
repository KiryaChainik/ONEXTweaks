using System;
using System.Threading;
using System.Threading.Tasks;

namespace XboxGamingBar.IPC
{
    // StreamWriter's synchronous flush can wait on the helper. Serialize request
    // writes away from the UI thread, retaining SendMessage's shared write lock
    // so legacy fire-and-forget messages cannot interleave a JSON line.
    internal sealed class OnexAsyncRequestWriter
    {
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly Func<string, bool> _write;

        internal OnexAsyncRequestWriter(Func<string, bool> write)
        {
            _write = write ?? throw new ArgumentNullException(nameof(write));
        }

        internal Task<bool> SendAsync(string message) => SendAsync(message, _write);

        internal async Task<bool> SendAsync(string message, Func<string, bool> write)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                return await Task.Run(() => write(message)).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
