using System;

namespace WinFIMLog.Jobs
{
    internal interface IMonitor : IDisposable
    {
        void Start();

        void Stop();
    }
}