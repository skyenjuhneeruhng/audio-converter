using System;
using System.Diagnostics;
using System.ServiceProcess;


namespace AudioConverterService
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main()
        {
            var eventLog = GetEventLog();

            try
            {
                var service = new ZyAudioConverter(eventLog);

                if (Environment.UserInteractive)
                {
                    service.StartConsole();
                    Console.WriteLine("Press any key to stop program");
                    Console.ReadKey();
                    service.StopConsole();
                }
                else
                {
                    ServiceBase.Run(service);
                }
            }
            catch (Exception ex)
            {
                Shared.WriteEventLogEntry(eventLog, ex.ToString(), EventLogEntryType.Error);
                throw;
            }
        }

        /// <summary>
        /// Returns an EventLog configured for logging information and errors for the service. Creates the event source if it doesn't already exist.
        /// </summary>
        /// <returns></returns>
        private static EventLog GetEventLog()
        {
            const string logName = "ZyDoc";
            const string eventSourceName = "TrackDoc Audio Processor Service";
            var eventLog = new EventLog();

            if (!EventLog.SourceExists(eventSourceName))
            {
                EventLog.CreateEventSource(eventSourceName, logName);
            }

            eventLog.Source = eventSourceName;
            eventLog.Log = logName;
            eventLog.MaximumKilobytes = 200000;
            eventLog.ModifyOverflowPolicy(OverflowAction.OverwriteAsNeeded, 90);

            return eventLog;
        }
    }
}
