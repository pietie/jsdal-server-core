using System;
using System.Collections.Generic;

namespace jsdal_server_core
{
    public static class SessionLog
    {
        private static readonly int MAX_ENTRIES = 2000;
        private static MemoryLog _log = new MemoryLog(MAX_ENTRIES);

        public static void Info(string info, params object[] args) { _log.Info(info, args); }
        public static void Error(string info, params object[] args) { _log.Error(info, args); }
        public static void Warning(string info, params object[] args) { _log.Warning(info, args); }
        public static void Exception(Exception ex, params object[] args) { 
            _log.Exception(ex, args); 
            
        }

        public static void Exception(Exception ex, Controllers.ExecController.ExecOptions execOptions, params object[] args) { 
            _log.Exception(ex,execOptions,  args); 
        }

        public static List<LogEntry> Entries { get { return _log.Entries; } }
    }
    public class MemoryLog
    {

        private int? _maxEntries;

        public MemoryLog(int? maxEntries = 1000)
        {
            _maxEntries = maxEntries;
        }

        private List<LogEntry> _entries = new List<LogEntry>();

        // 02/11/2015, PL: Created.
        private LogEntry AddEntry(LogEntryType type, string entry)
        {
            lock (_entries)
            {
                // cull from the front
                if (_maxEntries.HasValue && _entries.Count >= _maxEntries.Value)
                {
                    _entries.RemoveRange(0, _entries.Count - _maxEntries.Value + 1);
                }

                var newEntry = new LogEntry() { Type = type, Message = entry };

                _entries.Add(newEntry);

                return newEntry;
            }
        }

        public LogEntry Info(string info, params object[] args)
        {
            var line = string.Format(info, args);

            return AddEntry(LogEntryType.Info, line);
        }

        public void Warning(string info, params object[] args)
        {
            var line = string.Format(info, args);
            AddEntry(LogEntryType.Warning, line);
        }

        public void Error(string info, params object[] args)
        {
            var line = string.Format(info, args);
            AddEntry(LogEntryType.Error, line);
        }

        public void Exception(Exception ex, params object[] args)
        {
            //var line = string.Format(ex.ToString(), args);

            var line = ex.ToString();

            if (args != null && args.Length > 0)
            {
                line = string.Join(";", args) + "; " + ex.ToString();
            }

            AddEntry(LogEntryType.Exception, line);
        }

       


        // 03/11/2015, PL: Created.
        public List<LogEntry> Entries { get { return _entries; } }
    }

    public class LogEntry
    {
        public LogEntry() { this.CreateDate = DateTime.Now; }
        public DateTime? CreateDate { get; set; }

        public string Message { get; set; }

  
        public LogEntryType Type { get; set; }

        private DateTime? LastAppend { get; set; }

        public void Append(string msg, bool reportTime = true)
        {
            if (msg == null) return;
            if (this.Message == null) this.Message = "";
            var durationMS = "";

            if (reportTime)
            {
                var startDate = this.LastAppend.HasValue ? this.LastAppend.Value : this.CreateDate.Value;

                this.LastAppend = DateTime.Now;

                durationMS = " (" + (int)this.LastAppend.Value.Subtract(startDate).TotalMilliseconds + "ms)";
            }

            this.Message += durationMS + "; " + msg;


        }
    }

    public enum LogEntryType
    {
        Info = 10,
        Warning = 20,
        Error = 30,
        Exception = 40
    }
}