using System;
using System.Collections.Generic;
using System.Linq;
using jsdal_server_core.Performance.DataCollector.Reports;
using jsdal_server_core.Settings.ObjectModel;
using jsdal_server_core.Util;
using LiteDB;

namespace jsdal_server_core.Performance.DataCollector
{
    public class DataCollectorThread : QueueThread<DataCollectorDataEntry>
    {
        System.Diagnostics.Stopwatch _aggStopwatch = new System.Diagnostics.Stopwatch();
        System.Diagnostics.Stopwatch _insUpdStopwatch = new System.Diagnostics.Stopwatch();
        private static LiteDatabase _database;

        private DataCollectorThread() : base()
        {
            _database = new LiteDB.LiteDatabase("data/datacollector.db");
        }

        public static DataCollectorThread Instance { get; private set; }

        static DataCollectorThread()
        {
            Instance = new DataCollectorThread();
        }

        protected override void ProcessQueueEntries(List<DataCollectorDataEntry> collectionToProcess)
        {
            try
            {
                _insUpdStopwatch.Restart();

                var dbCollection = _database.GetCollection<DataCollectorDataEntry>($"Execution");

                dbCollection.EnsureIndex("ShortId", unique: true);
                dbCollection.EnsureIndex("Endpoint", unique: false);

                var allNew = collectionToProcess.Where(p => !p.IsDbRecordUpdate);

                dbCollection.InsertBulk(allNew);

                collectionToProcess.Where(p => p.IsDbRecordUpdate)
                            .ToList()
                            .ForEach(toProcess =>
                                {
                                    var existing = dbCollection.FindOne(x => x.ShortId == toProcess.ShortId);

                                    if (existing != null)
                                    {
                                        if (toProcess.IsTimeout)
                                        {
                                            toProcess.DurationInMS = (ulong?)toProcess.EndDate?.Subtract(existing.Created.Value).TotalMilliseconds;
                                        }

                                        existing.Rows = toProcess.Rows;
                                        existing.DurationInMS = toProcess.DurationInMS;
                                        existing.NetworkServerTimeInMS = toProcess.NetworkServerTimeInMS;
                                        existing.BytesReceived = toProcess.BytesReceived;
                                        existing.HasException = toProcess.HasException;
                                        existing.Exception = toProcess.Exception;
                                        existing.IsTimeout = toProcess.IsTimeout;
                                        existing.EndDate = toProcess.EndDate;

                                        dbCollection.Update(existing);
                                    }
                                    else
                                    {
                                        // TODO: Error. Expected to find a record to update!
                                    }
                                });

                _insUpdStopwatch.Stop();

                var newCount = allNew.Count();

                Audit($"Execution entries: {newCount} new & {collectionToProcess.Count - newCount} updated in {_insUpdStopwatch.ElapsedMilliseconds:N0}ms");
            }
            catch (Exception ex)
            {
                ExceptionLogger.LogExceptionThrottled(ex, "DataCollector::ProcessQueueEntry", 5);
            }
        }

        protected override void HandleProcessException(Exception ex)
        {
            ExceptionLogger.LogExceptionThrottled(ex, "DataCollector::HandleProcessException", 5);
        }

        private DateTime? _nextAggregate;
        private int _aggregateBracketInMins = 5; // e.g. 15 means data is aggregated on hh:00, hh:15, hh:30 and hh:45 minutes
        private DateTime? _nextDbCheckpoint;
        private const int DbCheckpointInSeconds = 3 * 60;
        protected override void DoWork()
        {
            try
            {
                if (!IsRunning) return;

                if (!_nextDbCheckpoint.HasValue) _nextDbCheckpoint = DateTime.Now.AddSeconds(DbCheckpointInSeconds);

                if (DateTime.Now >= _nextDbCheckpoint)
                {
                    _database.Checkpoint();
                    _nextDbCheckpoint = DateTime.Now.AddSeconds(DbCheckpointInSeconds);
                }

                var now = DateTime.Now;

                if (!_nextAggregate.HasValue)
                {
                    _nextAggregate = CalculateNextAggregateTime(now);
                }

                if (now > _nextAggregate.Value)
                {
                    _nextAggregate = CalculateNextAggregateTime(now);

                    this.ProcessQueueUntilEmpty();

                    var collection = _database.GetCollection<DataCollectorDataEntry>($"Execution");

                    var tick = Environment.TickCount;

                    _aggStopwatch.Restart();

                    // TODO: Handle old items where they never reached the end. Can we record the original CommandTimeout and base it off that + ~10% threshold. Then close off those items as a special kind of DataCollector timeout? We have to close them off for the *next* interval though otherwise we run the risk of conflicting with the prev group
                    var itemsToAggregate = collection.Find(e => e.EndDate.HasValue && e.EndDate.Value < _nextAggregate.Value).ToList();

                    if (itemsToAggregate.Count > 0)
                    {
                        var aggregateCount = Aggregate(itemsToAggregate);

                        _aggStopwatch.Stop();

                        var s = itemsToAggregate.Count == 1 ? "" : "s";

                        Audit($"Aggregated {itemsToAggregate.Count} executions into {aggregateCount} group{s} in {_aggStopwatch.ElapsedMilliseconds:N0}ms for bracket < { _nextAggregate.Value:HH:mm}");
                    }
                }
            }
            catch (Exception ex)
            {
                ExceptionLogger.LogExceptionThrottled(ex, "DataCollector::DoWork", 5);
            }
        }

        protected override void DoFinalWork()
        {
            if (_database != null)
            {
                _database.Checkpoint();
                _database.Dispose();
            }
        }

        private DateTime CalculateNextAggregateTime(DateTime now)
        {
            var div = (double)now.Minute / (double)_aggregateBracketInMins;
            var nextMin = ((int)div + 1) * _aggregateBracketInMins;

            return (new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0)).AddMinutes(nextMin);
        }

        private long CalcStartOfBracket(DateTime dt)
        {
            int min = (int)((double)dt.Minute / (double)_aggregateBracketInMins) * _aggregateBracketInMins;

            return long.Parse($"{dt:yyyyMMddHH}{min.ToString().PadLeft(2, '0')}");
        }

        private int Aggregate(List<DataCollectorDataEntry> dataList)
        {
            var executions = _database.GetCollection<DataCollectorDataEntry>($"Execution");
            var executionAggregates = _database.GetCollection<DataCollectorDataAgg>($"ExecutionAgg");

            executionAggregates.EnsureIndex("RoutineOncePerEndpointBracket", "{ ep: $.Endpoint, s: $.Schema, r: $.Routine, b: $.Bracket }", true);

            var aggregatedStats = from row in dataList
                                  group row by new
                                  {
                                      row.Endpoint,
                                      row.Schema,
                                      row.Routine,
                                      Bracket = CalcStartOfBracket(row.EndDate.Value)
                                  } into grp1
                                  select new DataCollectorDataAgg()
                                  {
                                      Endpoint = grp1.Key.Endpoint,
                                      Schema = grp1.Key.Schema,
                                      Routine = grp1.Key.Routine,
                                      Bracket = grp1.Key.Bracket,
                                      Executions = grp1.Count(x => x.DurationInMS.HasValue),

                                      DurationInMS = new DataCollectorAggregateStat<ulong>()
                                      {
                                          Min = grp1.Min(x => x.DurationInMS),
                                          Max = grp1.Max(x => x.DurationInMS),
                                          Sum = (ulong?)grp1.Where(x => x.DurationInMS.HasValue).Sum(x => (decimal)x.DurationInMS)
                                      },

                                      Rows = new DataCollectorAggregateStat<ulong>()
                                      {
                                          Min = (ulong?)grp1.Where(x => x.Rows < ulong.MaxValue).Min(x => (long?)x.Rows),
                                          Max = (ulong?)grp1.Where(x => x.Rows < ulong.MaxValue).Max(x => (long?)x.Rows),
                                          Sum = (ulong?)grp1.Where(x => x.Rows.HasValue && x.Rows < ulong.MaxValue).Sum(x => (decimal)x.Rows)
                                      },

                                      BytesReceived = new DataCollectorAggregateStat<long>()
                                      {
                                          Min = grp1.Min(x => x.BytesReceived),
                                          Max = grp1.Max(x => x.BytesReceived),
                                          Sum = grp1.Sum(x => x.BytesReceived)
                                      },

                                      NetworkServerTimeInMS = new DataCollectorAggregateStat<long>()
                                      {
                                          Min = grp1.Min(x => x.NetworkServerTimeInMS),
                                          Max = grp1.Max(x => x.NetworkServerTimeInMS),
                                          Sum = grp1.Sum(x => x.NetworkServerTimeInMS)
                                      },

                                      TimeoutCnt = grp1.Sum(x => x.IsTimeout ? 1 : 0),
                                      ExceptionCnt = grp1.Sum(x => x.HasException ? 1 : 0),
                                      LastExceptions = grp1.Where(x => x.HasException).TakeLast(3).Select(x => x.Exception).ToList()
                                  };

            if (aggregatedStats.Count() > 0)
            {
                var createdTrans = _database.BeginTrans();

                executionAggregates.InsertBulk(aggregatedStats);

                var idLookup = dataList.Select(d => d.Id).ToHashSet();

                executions.DeleteMany(x => idLookup.Contains(x.Id));

                if (createdTrans)
                {
                    _database.Commit();
                }
            }

            return aggregatedStats.Count();
        }

        public static string Enqueue(Endpoint endpoint, Controllers.ExecController.ExecOptions execOptions)
        {
            var entry = new DataCollectorDataEntry()
            {
                Endpoint = endpoint.Pedigree.ToUpper(),
                Schema = execOptions.schema,
                Routine = execOptions.routine
            };

            Instance.Enqueue(entry);

            return entry.ShortId;
        }

        public static void End(string shortId, ulong? rowsAffected = null, ulong? durationInMS = null, long? bytesReceived = null, long? networkServerTimeMS = null, Exception ex = null)
        {
            var isTimeout = false;

            var sqlEx = ex as System.Data.SqlClient.SqlException;

            if (sqlEx != null)
            {
                if (sqlEx.Number == -2/*Timeout*/)
                {
                    isTimeout = true;
                    ex = null; // timeout exceptions are treated special
                }
            }

            // build and update an "update" packet
            var entry = new DataCollectorDataEntry(shortId)
            {
                IsDbRecordUpdate = true,
                ShortId = shortId,
                Rows = rowsAffected ?? 0,
                DurationInMS = durationInMS,
                NetworkServerTimeInMS = networkServerTimeMS,
                BytesReceived = bytesReceived,
                HasException = ex != null,
                Exception = ex?.Message,
                IsTimeout = isTimeout,
                EndDate = DateTime.Now
            };

            Instance.Enqueue(entry);
        }

        public int ClearExecutions()
        {
            var gotTrans = _database.BeginTrans();

            var collection = _database.GetCollection<DataCollectorDataEntry>($"Execution");

            var n = collection.DeleteAll();

            if (gotTrans)
            {
                _database.Commit();
            }

            return n;
        }

        public void Audit(string msg)
        {
            var auditCollection = _database.GetCollection<AuditEntry>($"Audit");

            auditCollection.Insert(new AuditEntry() { Message = msg });
        }

        // tmp test method
        public dynamic GetAllDataTmp()
        {
            var collection1 = _database.GetCollection<DataCollectorDataEntry>($"Execution");
            var collection2 = _database.GetCollection<DataCollectorDataAgg>($"ExecutionAgg");
            var collection3 = _database.GetCollection<AuditEntry>($"Audit");

            var x = new
            {
                Executions = collection1.FindAll().ToList(),
                Agg = collection2.FindAll().TakeLast(20).ToList(),
                Audit = collection3.FindAll().ToList()
            };

            return x;
        }

        public dynamic GetSampleData()
        {
            var collection = _database.GetCollection<DataCollectorDataAgg>($"ExecutionAgg");

            return collection
                    .FindAll()
                    .Select(x => new { x.Bracket, AvgDurationInMS = ((double)(x.DurationInMS.Sum ?? 0d) / (double)x.Executions), Routine = $"{x.Schema}.{x.Routine}" })
                    .OrderByDescending(x => x.AvgDurationInMS)
                    .Take(10)
                    .ToList();
        }

        public dynamic GetTopNResource(int topN, DateTime fromDate, DateTime toDate, string[] endpoints, TopNResourceType type)
        {
            var collection = _database.GetCollection<DataCollectorDataAgg>($"ExecutionAgg");

            long bracketStart = long.Parse(fromDate.ToString("yyyyMMddHHmm"));
            long bracketEnd = long.Parse(toDate.ToString("yyyyMMddHHmm"));

            var baseQuery = collection
                    .Query()
                    .Where(x => x.Bracket >= bracketStart
                              && x.Bracket <= bracketEnd
                              && endpoints.Contains(x.Endpoint))
                    .ToEnumerable()
                    ;

            switch (type)
            {
                case TopNResourceType.Executions:
                    return TopN.TotalExecutions(baseQuery, topN);
                case TopNResourceType.Duration:
                    return TopN.AvgDuration(baseQuery, topN);
                case TopNResourceType.NetworkServerTime:
                    return TopN.AvgNetworkServerTime(baseQuery, topN);
                case TopNResourceType.BytesReceived:
                    return TopN.AvgKBReceived(baseQuery, topN);
                case TopNResourceType.ExceptionCnt:
                    return TopN.TotalExceptionCnt(baseQuery, topN);
                case TopNResourceType.TimeoutCnt:
                    return TopN.TotalTimeouts(baseQuery, topN);

                case TopNResourceType.TotalsVitals:
                    return TotalOverPeriod.TotalVitals(baseQuery, fromDate, toDate);
                default:
                    throw new Exception($"Type {type} not supported");
            }
        }
    }

    public enum TopNResourceType
    {
        Executions = 1,
        Duration = 2,
        NetworkServerTime = 3,
        BytesReceived = 4,
        ExceptionCnt = 20,
        TimeoutCnt = 30,


        TotalsVitals = 500

    }

}