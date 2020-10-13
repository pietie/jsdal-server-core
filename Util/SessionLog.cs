using System;
using System.Collections.Generic;
using System.IO;

namespace jsdal_server_core
{
    public static class SessionLog
    {
        private static readonly int MAX_ENTRIES = 2000;
        private static MemoryLog _log = new MemoryLog(MAX_ENTRIES);

        private static FileStream _fs = null;

        static SessionLog()
        {
            try
            {
                var path = $"./log/session-log";

                if (!Directory.Exists(path)) Directory.CreateDirectory(path);

                var filename = $"{DateTime.Now:yyyy-MM-dd HHmm}.txt";
                _fs = File.Open(Path.Combine(path, filename), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            }
            catch (Exception ex)
            {
                ExceptionLogger.LogException(ex);
            }
        }

        public static void Shutdown()
        {
            if (_fs != null)
            {
                _fs.Flush();
                _fs.Close();
                _fs = null;
            }
        }

        public static void InfoToFileOne(string info, params object[] args)
        {
            LogToStream("INF", info, args);
        }

        public static void Info(string info, params object[] args)
        {
            _log.Info(info, args);
            LogToStream("INF", info, args);
        }

        public static void Error(string info, params object[] args)
        {
            _log.Error(info, args);
            LogToStream("ERR", info, args);
        }

        public static void Warning(string info, params object[] args)
        {
            _log.Warning(info, args);
            LogToStream("WRN", info, args);
        }

        public static void Exception(Exception ex, params object[] args)
        {
            _log.Exception(ex, args);
            LogToStream("FTL", ex.ToString(), args);

        }

        private static void LogToStream(string type, string line, object[] args)
        {
            try
            {
                if (_fs != null && _fs.CanWrite)
                {
                    var formatted = line;

                    if (args != null && args.Length > 0)
                    {
                        formatted = string.Format(line, args);
                    }

                    var final = $"[{DateTime.Now:HH:mm:ss}] {type} {formatted}\r\n";
                    var data = System.Text.Encoding.UTF8.GetBytes(final);

                    lock (_fs)
                    {
                        _fs.Write(data, 0, data.Length);
                        _fs.Flush(true);
                    }
                }
            }
            catch (Exception ex)
            {
                ExceptionLogger.LogExceptionThrottled(ex, "SessionLog", 5);
            }
        }

        public static void Exception(Exception ex, Controllers.ExecController.ExecOptions execOptions, params object[] args)
        {
            _log.Exception(ex, execOptions, args);
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