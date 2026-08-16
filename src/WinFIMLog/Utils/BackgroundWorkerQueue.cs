using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace WinFIMLog.Utils
{
    /// <summary>
    ///     A queue for long running jobs
    /// </summary>
    public partial class BackgroundWorkerQueue : IDisposable
    {
        private readonly SemaphoreSlim _signal = new(0);

        private readonly ConcurrentQueue<Func<CancellationToken, Task>> _workItems = new();

        private bool disposedValue;

        /// <summary>
        ///     Dequeue a job if cancellation token exists
        /// </summary>
        /// <param name="cancellationToken">
        ///     A cancellation token to stop the jobs and dequeue
        /// </param>
        /// <returns>
        ///     return job to stop
        /// </returns>
        /// <exception cref="OperationCanceledException">
        /// </exception>
        public async Task<Func<CancellationToken, Task>?> DequeueAsync(CancellationToken cancellationToken)
        {
            if (!_workItems.IsEmpty)
            {
                await _signal.WaitAsync(cancellationToken);
                _workItems.TryDequeue(out var workItem);

                return workItem;
            }
            return null;
        }

        /// <summary>
        ///     Add a new job to the queue
        /// </summary>
        /// <param name="workItem">
        ///     New job as <see cref="Task" />
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// </exception>
        /// <exception cref="SemaphoreFullException">
        /// </exception>
        public void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);

            _workItems.Enqueue(workItem);
            _signal.Release();
        }

        #region Dispose

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _signal.Dispose();
                }

                disposedValue = true;
            }
        }

        #endregion Dispose
    }
}