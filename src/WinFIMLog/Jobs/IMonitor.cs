using System;
using System.Threading;
using System.Threading.Tasks;

namespace WinFIMLog.Jobs
{
    internal interface IMonitor : IDisposable
    {
        Task RunAsync(CancellationToken cancellationToken);
    }
}
