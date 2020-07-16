using System;
using System.Diagnostics;
using Serilog;

namespace jsdal_server_core
{
    public class EventLogWrapper
    {
        private EventLog _eventLog = new EventLog();
        private bool _isService;

        private const string EventSourceName = "jsDALServerSource";
        private const string EventLogName = "jsDAL Server Log";
        public EventLogWrapper(bool isService)
        {
            try
            {
                this._isService = isService;

                if (isService)
                {

                    if (!System.Diagnostics.EventLog.SourceExists(EventSourceName))
                    {
                        var sourceData = new EventSourceCreationData(EventSourceName, EventLogName);

                        Log.Information("Registering new source for event log.");
                        EventLog.CreateEventSource(sourceData);
                        Log.Information("EventSource created.");
                    }
                    else
                    {
                        Log.Information($"Event source '{EventSourceName}' already exists");
                    }
                }

                _eventLog.Source = EventSourceName;
                _eventLog.Log = EventLogName;
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                Log.Fatal(ex.ToString());
            }
        }

        public void Info(string msg)
        {
            Write(msg, EventLogEntryType.Information);
        }

        public void Error(string msg)
        {
            Write(msg, EventLogEntryType.Error);
        }

        public void Warning(string msg)
        {
            Write(msg, EventLogEntryType.Warning);
        }

        private void Write(string msg, EventLogEntryType type)
        {
            try
            {
                if (_isService) _eventLog.WriteEntry(msg, type);
                else Console.WriteLine($"{type}\t{msg}");
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                Console.WriteLine(ex.ToString());
            }
        }
    }
}