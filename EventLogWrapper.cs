using System;
using System.Diagnostics;

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

                        // // Set the message resource file that the event source references.
                        // // All event resource identifiers correspond to text in this file.
                        // if (!System.IO.File.Exists(messageFile))
                        // {
                        //     Console.WriteLine("Input message resource file does not exist - {0}",
                        //         messageFile);
                        //     messageFile = "";
                        // }
                        // else
                        // {
                        //     // Set the specified file as the resource
                        //     // file for message text, category text, and 
                        //     // message parameter strings.  

                        //     mySourceData.MessageResourceFile = messageFile;
                        //     mySourceData.CategoryResourceFile = messageFile;
                        //     mySourceData.CategoryCount = CategoryCount;
                        //     mySourceData.ParameterResourceFile = messageFile;

                        //     Console.WriteLine("Event source message resource file set to {0}",
                        //         messageFile);
                        // }

                        Console.WriteLine("Registering new source for event log.");
                        EventLog.CreateEventSource(sourceData);
                        Console.WriteLine("EventSource created.");
                        //EventSourceCreationData mySourceData = new EventSourceCreationData(sourceName, myLogName);


                        //System.Diagnostics.EventLog.CreateEventSource("jsDALServerSource", "jsDAL Server Log");
                    }
                    else
                    {
                        Console.WriteLine($"Event source '{EventSourceName}' already exists");
                    }
                }

                _eventLog.Source = EventSourceName;
                _eventLog.Log = EventLogName;
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                Console.WriteLine(ex.ToString());
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