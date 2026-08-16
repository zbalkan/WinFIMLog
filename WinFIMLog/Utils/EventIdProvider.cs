using System;
using Serilog.Events;
using Serilog.Sinks.EventLog;

namespace WinFIMLog.Utils
{
    internal sealed class EventIdProvider : IEventIdProvider
    {
        /// <summary>
        ///     Returns event ID based on log content
        /// </summary>
        /// <para> Event ID 7770 - An exception occurred </para>
        /// <para> Event ID 7776 – File / Directory creation </para>
        /// <para> Event ID 7777 – File modification </para>
        /// <para> Event ID 7778 – File / Directory deletion </para>
        /// <para> Event ID 7786 – Registry key creation </para>
        /// <para> Event ID 7787 – Registry key/value modification </para>
        /// <para> Event ID 7788 – Registry key deletion </para>
        /// <para> Event ID 7780 – Other events </para>
        /// <param name="logEvent">
        ///     Log event to return the Event ID
        /// </param>
        /// <returns>
        ///     Event ID
        /// </returns>
        public ushort ComputeEventId(LogEvent logEvent)
        {
            ushort eventId = 7780;
            switch (logEvent)
            {
                case { Level: LogEventLevel.Error }:
                    eventId = 7770;
                    break;

                case { Level: LogEventLevel.Information }:
                    {
                        if (!logEvent.Properties.TryGetValue("changeType", out var changeType))
                        {
                            break;
                        }
                        if (Equals(changeType, "FileSystem"))
                        {
                            if (!logEvent.Properties.TryGetValue("category", out var category))
                            {
                                break;
                            }
                            if (Equals(category, "Created"))
                            {
                                eventId = 7776;
                                break;
                            }

                            if (Equals(category, "Changed"))
                            {
                                eventId = 7777;
                                break;
                            }

                            if (Equals(category, "Deleted"))
                            {
                                eventId = 7778;
                                break;
                            }
                            break;
                        }
                        else
                        {
                            if (!logEvent.Properties.TryGetValue("category", out var category))
                            {
                                break;
                            }
                            if (Equals(category, "Created"))
                            {
                                eventId = 7786;
                                break;
                            }

                            if (Equals(category, "Changed"))
                            {
                                eventId = 7787;
                                break;
                            }

                            if (Equals(category, "Deleted"))
                            {
                                eventId = 7788;
                                break;
                            }
                            break;
                        }
                    }
            }

            return eventId;
        }

        private static bool Equals(LogEventPropertyValue a, string b) =>
            string.Equals(Sanitize(a), b, StringComparison.OrdinalIgnoreCase);

        private static string Sanitize(LogEventPropertyValue? result) => result?.ToString().Replace("\"", "") ?? string.Empty;
    }
}