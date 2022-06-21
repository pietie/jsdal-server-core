using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using jsdal_server_core.Settings.ObjectModel;
using Serilog;

namespace jsdal_server_core.Performance
{

    // TODO: Deprecated in favour of the DataCollector
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
                // TODO: Deprecated in favour of the DataCollector
                // _executionQueue = new ConcurrentQueue<StatsRoutineExecution>();
                // _database = new LiteDB.LiteDatabase("data/stats.db");

                // // reset stats on startup
                // _database.DropCollection("TotalCount");

                // SessionLog.Info("Starting up StatsDB thread");
                // _winThread = new Thread(new ThreadStart(ProcessMessagesLoop));
                // _winThread.Start();

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

                var flushTimeoutInSeconds = 25;
                var checkpointTimeoutInSeconds = 3 * 60;

                var nextFlush = DateTime.Now.AddSeconds(flushTimeoutInSeconds);
                var nextCheckpoint = DateTime.Now.AddSeconds(checkpointTimeoutInSeconds);

                System.Threading.Thread.CurrentThread.Name = "Stats DB";

                while (IsRunning && !Program.IsShuttingDown)
                {
                    // timeout or count trigger check 
                    if (DateTime.Now >= nextFlush || _executionQueue.Count >= 100)
                    {
                        while (!_executionQueue.IsEmpty)
                        {
                            if (_executionQueue.TryDequeue(out var statsRoutineExecution))
                            {
                                InsertUpdate(statsRoutineExecution);
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
                while (!_executionQueue.IsEmpty)
                {
                    if (_executionQueue.TryDequeue(out var statsRoutineExecution))
                    {
                        InsertUpdate(statsRoutineExecution);
                    }
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
                if (!_winThread.Join(TimeSpan.FromSeconds(10)))
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

        public static List<StatsRoutineExecution> GetTotalCountsTopN(int topN)
        {
            if (_database == null) return null;
            var totalCounts = _database.GetCollection<StatsRoutineExecution>("TotalCount");

            if (topN == 0)
            {
                return totalCounts.FindAll().ToList();
            }
            else
            {
                return totalCounts.FindAll().Take(Math.Min(topN, totalCounts.Count())).ToList();
            }
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
        public ulong? TotalDuration { get; set; } // in milliseconds

        public ulong? TotalRows { get; set; }

        public string EndpointId { get; set; }

        public string RoutineFullName { get; set; }
    }


}