using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using jsdal_server_core.Settings.ObjectModel;
using Serilog;

namespace jsdal_server_core.Performance
{

    public class ExecTracker
    {


        // TODO: Come up with a much smarter structure to use
        //public static List<RoutineExecution> ExecutionList = new List<RoutineExecution>();
        public static RoutineExecution Begin(Endpoint endpoint, string schema, string routine)
        {
            //StatsDB.RecordExecutionStart(endpointId, schema, routine);

            var ret = new RoutineExecution(endpoint, schema, routine);

            //ExecutionList.Add(ret);//? not used
            //PerformanceAggregator.Add(ret);
            RealtimeTracker.Add(ret);

            return ret;
        }
    }

    public class StatsDB
    {
        private static ConcurrentQueue<StatsRoutineExecution> _executionQueue;
        private static long _totalUniqueExecutions = 0;
        private static Thread _winThread;
        private static bool IsRunning;

        private static LiteDB.LiteDatabase _database;

        static StatsDB()
        {
            try
            {
                _executionQueue = new ConcurrentQueue<StatsRoutineExecution>();
                _database = new LiteDB.LiteDatabase("data/stats.db");

                // reset stats on startup
                _database.DropCollection("TotalCount");

                _winThread = new Thread(new ThreadStart(ProcessMessagesLoop));
                _winThread.Start();
            }
            catch (Exception ex)
            {
                SessionLog.Exception(ex);
                Log.Error(ex, "Failed to initiate Stats DB");
            }
        }


        public static void ProcessMessagesLoop()
        {
            try
            {
                IsRunning = true;
                var lastFlush = DateTime.Now;

                while (IsRunning)
                {
                    // timeout or count trigger check 
                    if (DateTime.Now.Subtract(lastFlush).TotalSeconds >= 60 || _executionQueue.Count >= 100)
                    {

                        while (!_executionQueue.IsEmpty)
                        {
                            if (_executionQueue.TryDequeue(out var statsRoutineExecution))
                            {
                                InsertUpdate(statsRoutineExecution);
                            }
                        }
                        // TODO: Don't checkpoint this often
                        _database.Checkpoint();

                        lastFlush = DateTime.Now;
                    }

                    Thread.Sleep(60);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "StatsDB::ProcessMessagesLoop failed");
                SessionLog.Error("StatsDB::ProcessMessagesLoop failed");
                SessionLog.Exception(ex);
            }
            finally
            {
                IsRunning = false;
            }
        }

        private static void InsertUpdate(StatsRoutineExecution rec)
        {
            var s = _database.BeginTrans();

            var totalCounts = _database.GetCollection<StatsRoutineExecution>("TotalCount");

            totalCounts.EnsureIndex("RoutineId", unique: true);

            var existing = totalCounts.FindOne(r => r.RoutineId.Equals(rec.RoutineId));

            if (existing != null)
            {
                existing.ExecutionCount += 1;
                existing.TotalDuration += rec.TotalDuration.Value;
                existing.TotalRows += (ulong)rec.TotalRows;

                totalCounts.Update(existing);
            }
            else
            {
                totalCounts.Insert(rec);
                Interlocked.Increment(ref _totalUniqueExecutions);
            }

            _database.Commit();
        }

        public static void Shutdown()
        {
            IsRunning = false;
            if (_winThread != null)
            {
                if (!_winThread.Join(TimeSpan.FromSeconds(3)))
                {
                    Log.Error("StatsDB failed to shutdown in time");
                }

                _winThread = null;
            }

            if (_database != null)
            {
                _database.Dispose();
            }
        }

        // public static void RecordExecutionStart(string endpoint, string schema, string routine)
        // {
        //     try
        //     {
        //         var s = database.BeginTrans();

        //         var routineId = $"{endpoint}.{schema}.{routine}".ToLower();
        //         var totalCounts = database.GetCollection<StatsRoutineExecution>("TotalCount");

        //         totalCounts.EnsureIndex("RoutineId", unique: true);

        //         var existing = totalCounts.FindOne(r => r.RoutineId.Equals(routineId));

        //         if (existing != null)
        //         {
        //             existing.ExecutionCount += 1;
        //             totalCounts.Update(existing);
        //         }
        //         else
        //         {
        //             totalCounts.Insert(new StatsRoutineExecution() { RoutineId = routineId, ExecutionCount = 1 });
        //         }

        //         database.Commit();
        //     }
        //     catch (Exception ex)
        //     {
        //         //ignore excpetion for now...in future log to sessionlog perhaps (but not too frequently)
        //     }
        // }

        public static void QueueRecordExecutionEnd(string endpointId, string schema, string routine, ulong? durationInMilliseconds, int rowsAffected)
        {
            if (!durationInMilliseconds.HasValue) return;

            _executionQueue.Enqueue(new StatsRoutineExecution()
            {
                RoutineId = $"{endpointId}/{schema.ToLower()}.{routine.ToLower()}",
                ExecutionCount = 1,
                TotalDuration = durationInMilliseconds.Value,
                TotalRows = (ulong)rowsAffected,
                RoutineFullName = $"{schema}.{routine}",
                EndpointId = endpointId
            });
        }
        // public static void RecordExecutionEnd(Endpoint endpoint, string schema, string routine, ulong? durationInMilliseconds, int rowsAffected)
        // {
        //     if (!durationInMilliseconds.HasValue) return;

        //     try
        //     {
        //         var s = database.BeginTrans();

        //         var routineId = $"{endpoint.Id}/{schema.ToLower()}.{routine.ToLower()}";
        //         var totalCounts = database.GetCollection<StatsRoutineExecution>("TotalCount");

        //         totalCounts.EnsureIndex("RoutineId", unique: true);


        //         var existing = totalCounts.FindOne(r => r.RoutineId.Equals(routineId));

        //         if (existing != null)
        //         {
        //             existing.ExecutionCount += 1;
        //             existing.TotalDuration += durationInMilliseconds.Value;
        //             existing.TotalRows += (ulong)rowsAffected;

        //             totalCounts.Update(existing);
        //         }
        //         else
        //         {
        //             totalCounts.Insert(new StatsRoutineExecution()
        //             {
        //                 RoutineId = routineId,
        //                 ExecutionCount = 1,
        //                 TotalDuration = durationInMilliseconds.Value,
        //                 TotalRows = (ulong)rowsAffected,
        //                 RoutineFullName = $"{schema}.{routine}",
        //                 EndpointId = endpoint.Id
        //             });
        //         }

        //         database.Commit();
        //     }
        //     catch (Exception ex)
        //     {
        //         //ignore excpetion for now...in future log to sessionlog perhaps (but not too frequently)
        //     }
        // }

        public static List<StatsRoutineExecution> GetTotalCountsCollection()
        {
            var totalCounts = _database.GetCollection<StatsRoutineExecution>("TotalCount");

            return totalCounts.FindAll().ToList();
        }

        public static long GetTotalUniqueExecutionsCount()
        {
            return _totalUniqueExecutions;
        }
    }

    public class StatsRoutineExecution
    {
        public int Id { get; set; } // auto set by LiteDB
        public string RoutineId { get; set; }
        public int ExecutionCount { get; set; }
        public ulong? TotalDuration { get; set; }

        public ulong? TotalRows { get; set; }

        public string EndpointId { get; set; }

        public string RoutineFullName { get; set; }
    }
}