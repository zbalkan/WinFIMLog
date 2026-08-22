using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace WinFIMLog.Utils
{
    internal sealed class EventIdEventLogLoggerProvider(string sourceName, string logName) : ILoggerProvider
    {
        private readonly EventIdProvider eventIds = new();
        private readonly object sourceLock = new();

        public ILogger CreateLogger(string categoryName) =>
            new EventIdEventLogLogger(sourceName, logName, eventIds, sourceLock);

        public void Dispose()
        { }

        internal static LogLevel EffectiveLogLevel(LogLevel logLevel, Exception? exception) =>
            exception is OperationCanceledException ? LogLevel.Information : logLevel;

        private sealed class EventIdEventLogLogger(string sourceName, string logName,
            EventIdProvider eventIds, object sourceLock) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                var effectiveLogLevel = EventIdEventLogLoggerProvider.EffectiveLogLevel(logLevel, exception);
                var isLifecycleCancellation = exception is OperationCanceledException;
                var message = isLifecycleCancellation
                    ? $"Lifecycle cancellation observed: {exception!.Message}"
                    : formatter(state, exception);
                if (exception is not null && !isLifecycleCancellation)
                {
                    message = $"{message}{Environment.NewLine}{exception}";
                }

                EnsureSource();
                EventLog.WriteEntry(sourceName, message, EntryType(effectiveLogLevel),
                    eventIds.ComputeEventId(effectiveLogLevel, eventId, state));
            }

            private static EventLogEntryType EntryType(LogLevel level) => level switch
            {
                LogLevel.Warning => EventLogEntryType.Warning,
                LogLevel.Error or LogLevel.Critical => EventLogEntryType.Error,
                _ => EventLogEntryType.Information,
            };

            private void EnsureSource()
            {
                if (EventLog.SourceExists(sourceName))
                {
                    return;
                }

                lock (sourceLock)
                {
                    if (!EventLog.SourceExists(sourceName))
                    {
                        EventLog.CreateEventSource(new EventSourceCreationData(sourceName, logName));
                    }
                }
            }
        }
    }
}
