using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading;
using LiteDB;
using Newtonsoft.Json;
using Serilog;
using shortid;

namespace jsdal_server_core
{

    public static class ExceptionLogger
    {
        private static readonly int MAX_ENTRIES_PER_ENDPOINT = 1000;
        private static LiteDatabase _database;
        private static ConcurrentQueue<ExceptionWrapper> _exceptionQueue;

        private static Thread _winThread;
        private static bool IsRunning;

        static ExceptionLogger()
        {
        }

        public static void Init()
        {
            try
            {
                _exceptionQueue = new ConcurrentQueue<ExceptionWrapper>();
                _database = new LiteDB.LiteDatabase("data/exceptions.db");

                var exceptionCollection = _database.GetCollection<ExceptionWrapper>("Exceptions");

                exceptionCollection.EnsureIndex("sId", unique: true);
                exceptionCollection.EnsureIndex("EndpointKey", unique: false);

                _winThread = new Thread(new ThreadStart(ProcessMessagesLoop));
                _winThread.Start();
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                Log.Error(ex, "Failed to initiate Exceptions DB");
            }
        }

        public static void ProcessMessagesLoop()
        {
            try
            {
                IsRunning = true;
                var flushTimeoutInSeconds = 25;
                var checkpointTimeoutInSeconds = 3 * 60;

                var nextFlush = DateTime.Now.AddSeconds(flushTimeoutInSeconds);
                var nextCheckpoint = DateTime.Now.AddSeconds(checkpointTimeoutInSeconds);

                DateTime? lastFailedToLogToStoreDate = null;

                while (IsRunning)
                {
                    // timeout or count trigger check 
                    if (DateTime.Now >= nextFlush || _exceptionQueue.Count >= 100)
                    {
                        while (!_exceptionQueue.IsEmpty)
                        {
                            if (_exceptionQueue.TryDequeue(out var ew))
                            {
                                try
                                {
                                    AddExceptionToDB(ew);
                                }
                                catch (Exception ee)
                                {
                                    // prevent logging failures too often
                                    if (!lastFailedToLogToStoreDate.HasValue || DateTime.Now.Subtract(lastFailedToLogToStoreDate.Value).TotalSeconds >= 25)
                                    {
                                        Log.Error(ee, $"Failed to log exception to DB store. EP={ew.EndpointKey}; sID={ew.sId};");
                                        lastFailedToLogToStoreDate = DateTime.Now;
                                    }
                                }
                            }
                        }

                        nextFlush = DateTime.Now.AddSeconds(flushTimeoutInSeconds);
                    }

                    // checkpoint 
                    if (DateTime.Now >= nextCheckpoint)
                    {
                        _database.Checkpoint();

                        nextCheckpoint = DateTime.Now.AddSeconds(checkpointTimeoutInSeconds);
                    }

                    Thread.Sleep(60);
                }

                // flush any remaining items out
                while (!_exceptionQueue.IsEmpty)
                {
                    if (_exceptionQueue.TryDequeue(out var ew))
                    {
                        try
                        {
                            AddExceptionToDB(ew);
                        }
                        catch (Exception ee)
                        {
                            // prevent logging failures too often
                            if (!lastFailedToLogToStoreDate.HasValue || DateTime.Now.Subtract(lastFailedToLogToStoreDate.Value).TotalSeconds >= 25)
                            {
                                Log.Error(ee, $"Failed to log exception to DB store. EP={ew.EndpointKey}; sID={ew.sId};");
                                lastFailedToLogToStoreDate = DateTime.Now;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ExceptionLogger::ProcessMessagesLoop failed");
                SessionLog.Error("ExceptionLogger::ProcessMessagesLoop failed");
                SessionLog.Exception(ex);
            }
            finally
            {
                IsRunning = false;
            }
        }

        private static void AddExceptionToDB(ExceptionWrapper ew)
        {
            _database.BeginTrans();

            var exceptionCollection = _database.GetCollection<ExceptionWrapper>("Exceptions");

            if (ew.EndpointKey != null)
            {
                // cull from the front
                int currentCount = exceptionCollection.Count(e => e.EndpointKey == ew.EndpointKey);
                int countToRemove = (currentCount - 4/*ExceptionLogger.MAX_ENTRIES_PER_ENDPOINT*/) + 1;

                if (countToRemove > 0)
                {
                    var idsToDelete = exceptionCollection
                                .Find(e => e.EndpointKey.Equals(ew.EndpointKey))
                                .Take(countToRemove)
                                .Select(e=>e.Id)
                                ;

                    exceptionCollection.DeleteMany(x=>idsToDelete.Contains(x.Id));
                }
            }

            ExceptionWrapper parent = null;

            // TODO: think of other ways to find "related". Message might not match 100% so apply "like" search of match on ErrorType(e.g. group Timeouts)

            // look at last (n) exceptions for an exact match
            if (parent == null)
            {
                parent = exceptionCollection
                                .Query()
                                .OrderByDescending(e => e.Id)
                                .Limit(5)
                                .ToEnumerable()
                                .Where(e => e.EndpointKey == ew.EndpointKey)
                                .Where(e => e.server == null && ew.server == null || (e.server?.Equals(ew.server, StringComparison.OrdinalIgnoreCase) ?? false))
                                .Where(e => e.message.Equals(ew.message, StringComparison.OrdinalIgnoreCase))
                                .Where(e => e.innerException == null && ew.innerException == null || (e.innerException?.message.Equals(ew.innerException?.message, StringComparison.OrdinalIgnoreCase) ?? false))
                                .FirstOrDefault();
            }

            // look for recent similiar entry, if found just tag it onto that rather than logging a new main entry
            if (parent == null)
            {
                DateTime thresholdDate = DateTime.Now.AddMinutes(-2.0); // look for last 2mins

                parent = exceptionCollection.Find(e => e.EndpointKey == ew.EndpointKey
                                     && (e.created >= thresholdDate)
                                     && (e.server != null && e.server.Equals(ew.server, StringComparison.OrdinalIgnoreCase))
                                     && (e.message.Equals(ew.message, StringComparison.OrdinalIgnoreCase))
                )
                .OrderByDescending(e => e.Id)
                .FirstOrDefault();
            }

            // group recent timeouts (to the same server) together under common parent as a bunch of timeouts tend to occur shortly after each other
            if (parent == null && ew.sqlErrorType.HasValue && ew.sqlErrorType.Value == SqlErrorType.Timeout)
            {
                DateTime thresholdDate = DateTime.Now.AddMinutes(-2.0); // look for last 2mins

                parent = exceptionCollection.Find(e => e.EndpointKey == ew.EndpointKey
                                    && (e.created >= thresholdDate)
                                    && (e.server != null && e.server.Equals(ew.server, StringComparison.OrdinalIgnoreCase))
                                    && (e.sqlErrorType.HasValue && e.sqlErrorType.Value == SqlErrorType.Timeout)
                )
                .OrderByDescending(e => e.Id)
                .FirstOrDefault();

            }

            if (parent == null)
            {
                exceptionCollection.Insert(ew);
            }
            else
            {
                // TODO: Not sure how locking is going to work here...do we lock the parent?
                // TODO: Update existing item
                if (parent.AddRelated(ew))
                {
                    exceptionCollection.Update(parent);
                }
                else
                {
                    exceptionCollection.Insert(ew);
                }
            }

            _database.Commit();
        }

        public static string LogException(Exception ex, string additionalInfo = null, string appTitle = null, string appVersion = null)
        {
            return QueueException("Global", ex, null, additionalInfo, appTitle, appVersion);
        }

        public static string LogException(Exception ex, Controllers.ExecController.ExecOptions execOptions, string additionalInfo = null, string appTitle = null, string appVersion = null)
        {
            string endpointKey = "Global";

            if (execOptions != null)
            {
                endpointKey = $"{execOptions.project}/{execOptions.application}/{execOptions.endpoint}".ToUpper();
            }

            return QueueException(endpointKey, ex, execOptions, additionalInfo, appTitle, appVersion);
        }

        private static string QueueException(string endpointKey, Exception ex, Controllers.ExecController.ExecOptions execOptions, string additionalInfo, string appTitle, string appVersion = null)
        {
            var ew = new ExceptionWrapper(ex, execOptions, additionalInfo, appTitle, appVersion) { EndpointKey = endpointKey };

            _exceptionQueue.Enqueue(ew);

            return null;
        }

        public static void Shutdown()
        {
            IsRunning = false;
            if (_winThread != null)
            {
                if (!_winThread.Join(TimeSpan.FromSeconds(10)))
                {
                    Log.Error("ExceptionsDB failed to shutdown in time");
                }
                _winThread = null;
            }

            if (_database != null)
            {
                _database.Dispose();
            }
        }

        public static void ClearAll()
        {
            _database.BeginTrans();
            var exceptionCollection = _database.GetCollection<ExceptionWrapper>("Exceptions");
            exceptionCollection.DeleteAll();
            _database.Commit();
        }

        public static ExceptionWrapper GetException(string sId)
        {
            var ew = _database.GetCollection<ExceptionWrapper>("Exceptions").FindOne(e => e.sId.Equals(sId));
            return ew;
        }

        public static ExceptionWrapper DeepFindRelated(string sId)
        {
            var topLevelCollection = _database.GetCollection<ExceptionWrapper>("Exceptions").FindAll();

            foreach (var topLevelException in topLevelCollection)
            {
                var ew = topLevelException.GetRelated(sId);
                if (ew != null) return ew;
            }

            return null;
        }

        public static IEnumerable<ExceptionWrapper> GetAll(string[] endpointLookup)
        {
            // return ALL
            if (endpointLookup == null || endpointLookup.Length == 0)
            {
                return _database.GetCollection<ExceptionWrapper>("Exceptions").FindAll();
            }
            else
            {
                var exceptionCollection = _database.GetCollection<ExceptionWrapper>("Exceptions");

                var finalList = new List<ExceptionWrapper>();

                foreach (var ep in endpointLookup)
                {
                    var m = exceptionCollection.Find(e => e.EndpointKey == ep);

                    finalList.AddRange(m);
                }

                return finalList;
            }
        }

        public static int TotalCnt
        {
            get
            {
                return _database.GetCollection<ExceptionWrapper>("Exceptions").Count();
            }
        }

        public static List<string> Endpoints
        {
            get
            {
                return _database.GetCollection<ExceptionWrapper>("Exceptions").FindAll().Select(e => e.EndpointKey).Distinct().OrderBy(k => k).ToList();
            }
        }

        public static List<string> AppTitles
        {
            get
            {
                return _database.GetCollection<ExceptionWrapper>("Exceptions").FindAll().Select(e => e.appTitle).Where(at => !string.IsNullOrWhiteSpace(at)).Distinct().OrderBy(at => at).ToList();
            }
        }

    }

}