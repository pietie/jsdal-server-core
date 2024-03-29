using System;
using System.Collections.Generic;
using System.Linq;
using jsdal_plugin.Util;
using jsdal_server_core.Performance.DataCollector.Reports;
using jsdal_server_core.Settings.ObjectModel;
using jsdal_server_core.Util;
using LiteDB;
using Microsoft.Data.SqlClient;

namespace jsdal_server_core.Performance.DataCollector
{
    public class DataCollectorThread : QueueThread<DataCollectorDataEntry>
    {
        System.Diagnostics.Stopwatch _aggStopwatch = new System.Diagnostics.Stopwatch();
        //System.Diagnostics.Stopwatch _insUpdStopwatch = new System.Diagnostics.Stopwatch();
        private LiteDatabase _dbRunningValues;
        //?private LiteDatabase _dbAggregates;

        private readonly string Collection_RunningExections = "Execution";
        private readonly string Collection_Audit = "Audit";
        private readonly string Collection_AggregateRegister = "AggregateRegister";
        private object _aggregateRegisterLock = new object();

        private readonly string Collection_Agg_IntraHour = "ExecutionAggH";
        // private readonly string Collection_Agg_Daily = "ExecutionAggD";
        // private readonly string Collection_Agg_Weekly = "ExecutionAggW";
        // private readonly string Collection_Agg_Monthly = "ExecutionAggM";

        private DataCollectorThread() : base(threadName: "DataCollectorThread")
        {
        }

        public override void Init()
        {
            _dbRunningValues = new LiteDB.LiteDatabase("data/datacollector.db");

            ClearAudit();

            base.Init();
        }

        public static DataCollectorThread Instance { get; private set; }

        static DataCollectorThread()
        {
            Instance = new DataCollectorThread();
        }

        public override void Shutdown()
        {
            base.Shutdown();

            if (_dbRunningValues != null)
            {
                _dbRunningValues.Checkpoint();
                _dbRunningValues.Dispose();
                _dbRunningValues = null;
            }
        }

        protected override void ProcessQueueEntries(List<DataCollectorDataEntry> collectionToProcess)
        {
            try
            {
                //_insUpdStopwatch.Restart();

                var dbCollection = _dbRunningValues.GetCollection<DataCollectorDataEntry>(Collection_RunningExections);

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

                // _insUpdStopwatch.Stop();

                // var newCount = allNew.Count();

                //Audit($"Execution entries: {newCount} new & {collectionToProcess.Count - newCount} updated in {_insUpdStopwatch.ElapsedMilliseconds:N0}ms");
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

        private DateTime? _nextIntraHourAggregate;
        private int _aggregateBracketInMins = 15; // e.g. 15 means data is aggregated on hh:00, hh:15, hh:30 and hh:45 minutes
        private DateTime? _nextDbCheckpoint;
        private const int DbCheckpointInSeconds = 3 * 60;

        //private DateTime? _nextOnceADayAggregate;
        protected override void DoWork()
        {
            try
            {
                if (!IsRunning) return;

                if (!_nextDbCheckpoint.HasValue) _nextDbCheckpoint = DateTime.Now.AddSeconds(DbCheckpointInSeconds);

                if (DateTime.Now >= _nextDbCheckpoint)
                {
                    _dbRunningValues.Checkpoint();
                    _nextDbCheckpoint = DateTime.Now.AddSeconds(DbCheckpointInSeconds);
                }

                var now = DateTime.Now;

                if (!_nextIntraHourAggregate.HasValue)
                {
                    _nextIntraHourAggregate = CalculateNextAggregateTime(now);
                }
                /*
                                var collection2 = _dbRunningValues.GetCollection<DataCollectorDataEntry>(Collection_RunningExections);

                                var c = collection2.Count();
                                var c2 = collection2.Find(e => !e.EndDate.HasValue && e.Created.HasValue && e.Created.Value < DateTime.Now.AddHours(-1)).Count();
                                var c3 = collection2.FindAll().ToList().Select(x=>new { x.Id, Created = x.Created.Value, x.EndDate }).ToList();
                                var c4 = collection2.Find(e => e.EndDate.HasValue).ToList();

                */
                bool force = false;

                if (force || now.AddSeconds(-15)/*give stragglers a chance to catch up*/ > _nextIntraHourAggregate.Value)
                {
                    var maxBracketDate = _nextIntraHourAggregate.Value;
                    var prevMaxBracketDate = maxBracketDate.AddMinutes(-_aggregateBracketInMins);

                    _nextIntraHourAggregate = CalculateNextAggregateTime(now);

                    this.ProcessQueueUntilEmpty();

                    var collection = _dbRunningValues.GetCollection<DataCollectorDataEntry>(Collection_RunningExections);

                    var tick = Environment.TickCount;

                    _aggStopwatch.Restart();

                    // TODO: Handle old items where they never reached the end. Can we record the original CommandTimeout and base it off that + ~10% threshold. 
                    //       Then close off those items as a special kind of DataCollector timeout? We have to close them off for the *next* interval though 
                    //       otherwise we run the risk of conflicting with the prev group

                    // TODO: Delete everything less than PREVIOUS aggregate

                    var itemsToAggregate = collection.Find(e => e.EndDate.HasValue && e.EndDate.Value < maxBracketDate).Take(50000).ToList(); // if we get behind limit to reasonable amount at at time

                    if (itemsToAggregate.Count > 0)
                    {
                        var aggregateCount = AggregateIntraHour(itemsToAggregate);

                        _aggStopwatch.Stop();

                        var s = itemsToAggregate.Count == 1 ? "" : "s";

                        Audit($"Aggregated {itemsToAggregate.Count} executions into {aggregateCount} group{s} in {_aggStopwatch.ElapsedMilliseconds:N0}ms for bracket <= { maxBracketDate:HH:mm}");
                    }

                    UpdateAggregateRegister(intraHour: _nextIntraHourAggregate);
                }
            }
            catch (Exception ex)
            {
                ExceptionLogger.LogExceptionThrottled(ex, "DataCollector::DoWork", 5);
            }
        }

        protected override void DoFinalWork()
        {
            if (_dbRunningValues != null)
            {
                _dbRunningValues.Checkpoint();
                _dbRunningValues.Dispose();
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

        private void UpdateAggregateRegister(DateTime? intraHour = null)
        {
            lock (_aggregateRegisterLock)
            {
                var collection = _dbRunningValues.GetCollection<AggregateRegister>(Collection_AggregateRegister);

                var register = collection.FindOne(Query.All());

                if (register == null) register = new AggregateRegister();

                register.IntraHourLast = intraHour ?? register.IntraHourLast;

                collection.Upsert(register);
            }
        }

        private int AggregateIntraHour(List<DataCollectorDataEntry> dataList)
        {
            var executions = _dbRunningValues.GetCollection<DataCollectorDataEntry>(Collection_RunningExections);
            var executionAggregates = _dbRunningValues.GetCollection<DataCollectorDataAgg>(Collection_Agg_IntraHour);

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
                var createdTrans = _dbRunningValues.BeginTrans();

                // Index("RoutineOncePerEndpointBracket", "{ ep: $.Endpoint, s: $.Schema, r: $.Routine, b: $.Bracket }", true);
                // find duplicates and merge them
                // foreach (var a in aggregatedStats)
                // {
                //     var expr = Query.And(
                //                Query.And(
                //                Query.And(
                //                     Query.EQ("Endpoint", a.Endpoint),
                //                     Query.EQ("Schema", a.Schema)),
                //                     Query.EQ("Routine", a.Routine)),
                //                     Query.EQ("Bracket", a.Bracket));

                //     var find = executionAggregates.Find(expr);

                //     var n = find.Count();
                // }




                executionAggregates.InsertBulk(aggregatedStats);


                var idLookup = dataList.Select(d => d.Id).ToHashSet();

                executions.DeleteMany(x => idLookup.Contains(x.Id));

                if (createdTrans)
                {
                    _dbRunningValues.Commit();
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

            var sqlEx = ex as SqlException;

            if (sqlEx == null)
            {
                sqlEx = ex?.InnerException as SqlException;
            }

            if (sqlEx != null)
            {
                if (sqlEx.Number == -2/*Timeout*/)
                {
                    isTimeout = true;
                    ex = null; // timeout exceptions are treated special
                }
            }

            // TODO: Consider having an EndDate cut off here. If EndDate is PAST the last Aggegrate date then then we have missed the bus. Either log an error (REJECTED ROW) or maybe adjust EndDate slightly so that it falls in NEXT slot?

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
            var gotTrans = _dbRunningValues.BeginTrans();

            var collection = _dbRunningValues.GetCollection<DataCollectorDataEntry>(Collection_RunningExections);

            var n = collection.DeleteAll();

            if (gotTrans)
            {
                _dbRunningValues.Commit();
            }

            return n;
        }

        public dynamic GetAggregateStats()
        {
            if (_dbRunningValues == null) return null;

            var executionAggregates = _dbRunningValues.GetCollection<DataCollectorDataAgg>(Collection_Agg_IntraHour);

            var minBracket = executionAggregates.Min(x => x.Bracket);
            var maxBracket = executionAggregates.Max(x => x.Bracket);

            DateTime? minBracketDate = null;
            DateTime? maxBracketDate = null;

            if (DateTime.TryParseExact(minBracket.ToString(), "yyyyMMddHHmm", null, System.Globalization.DateTimeStyles.None, out var dt))
            {
                minBracketDate = dt;
            }

            if (DateTime.TryParseExact(maxBracket.ToString(), "yyyyMMddHHmm", null, System.Globalization.DateTimeStyles.None, out dt))
            {
                maxBracketDate = dt;
            }

            return new
            {
                TotalCount = executionAggregates.Count(),
                minBracketDate = minBracketDate,
                maxBracketDate = maxBracketDate
            };
        }

        public int PurgeOldAggregates(int daysOld)
        {
            var executionAggregates = _dbRunningValues.GetCollection<DataCollectorDataAgg>(Collection_Agg_IntraHour);

            long uptoDate = long.Parse(DateTime.Today.AddDays(-daysOld).ToString("yyyyMMddHHmm"));


            return executionAggregates.DeleteMany(x => x.Bracket <= uptoDate);
        }

        public void Audit(string msg)
        {
            var auditCollection = _dbRunningValues.GetCollection<AuditEntry>(Collection_Audit);

            auditCollection.Insert(new AuditEntry() { Message = msg });
        }

        public void ClearAudit()
        {
            var auditCollection = _dbRunningValues.GetCollection<AuditEntry>(Collection_Audit);

            auditCollection.DeleteAll();
        }

        // tmp test method
        public dynamic GetAllDataTmp()
        {
            var collection1 = _dbRunningValues?.GetCollection<DataCollectorDataEntry>(Collection_RunningExections);
            var collection2 = _dbRunningValues?.GetCollection<DataCollectorDataAgg>(Collection_Agg_IntraHour);
            var collection3 = _dbRunningValues?.GetCollection<AuditEntry>(Collection_Audit);

            var x = new
            {
                Executions = collection1?.FindAll().TakeLast(50).ToList(),
                Agg = collection2?.FindAll().TakeLast(20).ToList(),
                Audit = collection3?.FindAll().TakeLast(10).ToList(),
                ExecutionCnt = collection1?.Count(),
                AggCnt = collection2?.Count(),
                AuditCnt = collection3?.Count()
            };

            return x;
        }

        private IEnumerable<DataCollectorDataAgg> BuildBaseQuery(DateTime fromDate, DateTime toDate, string[] endpoints)
        {
            var collection = _dbRunningValues.GetCollection<DataCollectorDataAgg>(Collection_Agg_IntraHour);

            long bracketStart = long.Parse(fromDate.ToString("yyyyMMddHHmm"));
            long bracketEnd = long.Parse(toDate.ToString("yyyyMMddHHmm"));

            var upperEndpoints = endpoints.Select(ep => ep.ToUpper()).ToArray();

            var baseQuery = collection
                    .Query()
                    .Where(x => x.Bracket >= bracketStart
                              && x.Bracket <= bracketEnd
                              //&& endpoints.FirstOrDefault(ep => ep.Equals(x.Endpoint, StringComparison.OrdinalIgnoreCase)) != null
                              && upperEndpoints.Contains(x.Endpoint.ToUpper())

                              )
                    .ToEnumerable()
                    ;


            return baseQuery;
        }

        public dynamic AllStatsList(int topN, DateTime fromDate, DateTime toDate, string[] endpoints)
        {
            var baseQuery = BuildBaseQuery(fromDate, toDate, endpoints);
            return TopN.AllStatsList(baseQuery, topN);
        }

        public dynamic GetTopNResource(int topN, DateTime fromDate, DateTime toDate, string[] endpoints, TopNResourceType type)
        {
            if (_dbRunningValues == null) return null;
            
            var baseQuery = BuildBaseQuery(fromDate, toDate, endpoints);

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

        public dynamic GetRoutineAllStats(string schema, string routine, DateTime fromDate, DateTime toDate, string[] endpoints)
        {
            var baseQuery = BuildBaseQuery(fromDate, toDate, endpoints);

            return TotalOverPeriod.TotalVitals(baseQuery, fromDate, toDate, schema, routine);
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