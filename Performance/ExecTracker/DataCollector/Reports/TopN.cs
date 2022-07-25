using System;
using System.Collections.Generic;
using System.Linq;

namespace jsdal_server_core.Performance.DataCollector.Reports
{
    public static class TopN
    {
        public static dynamic AllStatsList(IEnumerable<DataCollectorDataAgg> baseQuery, int topN)
        {
            var totals = (from e in baseQuery
                          group e by $"{e.Schema}.{e.Routine}" into g
                          select new
                          {
                              Routine = g.Key,
                              TotalExecutions = g.Sum(x => (decimal)x.Executions),
                              SumDurationInMS = g.Sum(x => (decimal)x.DurationInMS.Sum),
                              SumNetworkServerTimeInMS = g.Sum(x => (decimal)x.NetworkServerTimeInMS.Sum),
                              SumKBReceived = g.Sum(x => (decimal)x.BytesReceived.Sum / 1024M),
                              TotalExceptions = g.Sum(x => x.ExceptionCnt),
                              TotalTimeouts = g.Sum(x => x.TimeoutCnt)
                          });

            var query = (from e in totals
                         select new
                         {
                             e.Routine,
                             e.TotalExecutions,

                             TotalDurationInMins = Math.Round(e.SumDurationInMS / 60000M, 2),
                             AvgDurationInMS = e.TotalExecutions == 0 ? 0 : (int)((e.SumDurationInMS / e.TotalExecutions) + 0.5M),

                             TotalNetworkServerTimeInMins = Math.Round(e.SumNetworkServerTimeInMS / 60000M, 2),
                             AvgSumNetworkServerTimeInMS = e.TotalExecutions == 0 ? 0 : (int)((e.SumNetworkServerTimeInMS / e.TotalExecutions) + 0.5M),

                             TotalMBReceived = Math.Round(e.SumKBReceived / 1024M, 2),
                             AvgKBReceived = e.TotalExecutions == 0 ? 0 : (int)((e.SumKBReceived / e.TotalExecutions) + 0.5M),

                             e.TotalExceptions,
                             e.TotalTimeouts
                         })
            .OrderByDescending(x => x.TotalExecutions) // TODO: Make order by configurable?
            .Take(topN)
            ;

            return query.ToList();
        }

        public static dynamic TotalExecutions(IEnumerable<DataCollectorDataAgg> baseQuery, int topN)
        {
            var query = (from e in baseQuery
                         group e by $"{e.Schema}.{e.Routine}" into g
                         select new
                         {
                             Routine = g.Key,
                             // BracketMin  = g.Min(x=>x.Bracket),
                             // BracketMax  = g.Max(x=>x.Bracket),
                             TotalExecutions = g.Sum(x => x.Executions)
                         })
                                .OrderByDescending(x => x.TotalExecutions)
                                .Take(topN)
                                ;

            dynamic ret = new
            {
                labels = query.Select(x => x.Routine).ToArray(),
                data = query.Select(x => x.TotalExecutions).ToArray()
            };

            return ret;
        }

        public static dynamic AvgDuration(IEnumerable<DataCollectorDataAgg> baseQuery, int topN)
        {
            var totals = (from e in baseQuery
                          group e by $"{e.Schema}.{e.Routine}" into g
                          select new
                          {
                              Routine = g.Key,
                              SumDurationInMS = g.Sum(x => (decimal)x.DurationInMS.Sum),
                              TotalExecutions = g.Sum(x => (decimal)x.Executions)
                          });

            var query = (from e in totals
                         where e.TotalExecutions > 0
                         select new
                         {
                             e.Routine,
                             AvgDurationInMS = (int)((e.SumDurationInMS / e.TotalExecutions) + 0.5M)
                         })
                .OrderByDescending(x => x.AvgDurationInMS)
                .Take(topN)
                ;

            dynamic ret = new
            {
                labels = query.Select(x => x.Routine).ToArray(),
                data = query.Select(x => x.AvgDurationInMS).ToArray()
            };

            return ret;
        }

        public static dynamic AvgNetworkServerTime(IEnumerable<DataCollectorDataAgg> baseQuery, int topN)
        {
            var totals = (from e in baseQuery
                          group e by $"{e.Schema}.{e.Routine}" into g
                          select new
                          {
                              Routine = g.Key,
                              SumNetworkServerTimeInMS = g.Sum(x => (decimal)x.NetworkServerTimeInMS.Sum),
                              TotalExecutions = g.Sum(x => (decimal)x.Executions)
                          });

            var query = (from e in totals
                         where e.TotalExecutions > 0
                         select new
                         {
                             e.Routine,
                             AvgSumNetworkServerTimeInMS = (int)((e.SumNetworkServerTimeInMS / e.TotalExecutions) + 0.5M)
                         })
                .OrderByDescending(x => x.AvgSumNetworkServerTimeInMS)
                .Take(topN)
                ;

            dynamic ret = new
            {
                labels = query.Select(x => x.Routine).ToArray(),
                data = query.Select(x => x.AvgSumNetworkServerTimeInMS).ToArray()
            };

            return ret;
        }

        public static dynamic AvgKBReceived(IEnumerable<DataCollectorDataAgg> baseQuery, int topN)
        {
            var totals = (from e in baseQuery
                          group e by $"{e.Schema}.{e.Routine}" into g
                          select new
                          {
                              Routine = g.Key,
                              SumBytesReceived = g.Sum(x => (decimal)x.BytesReceived.Sum),
                              TotalExecutions = g.Sum(x => (decimal)x.Executions)
                          });

            var query = (from e in totals
                         where e.TotalExecutions > 0
                         select new
                         {
                             e.Routine,
                             AvgKBReceived = (int)((e.SumBytesReceived / e.TotalExecutions / 1024M) + 0.5M)
                         })
                .OrderByDescending(x => x.AvgKBReceived)
                .Take(topN)
                ;

            dynamic ret = new
            {
                labels = query.Select(x => x.Routine).ToArray(),
                data = query.Select(x => x.AvgKBReceived).ToArray()
            };

            return ret;
        }

        public static dynamic TotalExceptionCnt(IEnumerable<DataCollectorDataAgg> baseQuery, int topN)
        {
            var query = (from e in baseQuery
                         group e by $"{e.Schema}.{e.Routine}" into g
                         select new
                         {
                             Routine = g.Key,
                             TotalExceptions = g.Sum(x => x.ExceptionCnt),
                             LastExceptions = g.SelectMany(x => x.LastExceptions).TakeLast(3)
                         })
                               .OrderByDescending(x => x.TotalExceptions)
                               .Where(x => x.TotalExceptions > 0)
                               .Take(topN)
                               ;

            dynamic ret = new
            {
                labels = query.Select(x => x.Routine).ToArray(),
                data = query.Select(x => x.TotalExceptions).ToArray(),
                lastExceptions = query.Select(x => new { x.Routine, x.LastExceptions }).ToArray()
            };

            return ret;
        }

        public static dynamic TotalTimeouts(IEnumerable<DataCollectorDataAgg> baseQuery, int topN)
        {
            var query = (from e in baseQuery
                         group e by $"{e.Schema}.{e.Routine}" into g
                         select new
                         {
                             Routine = g.Key,
                             TotalTimeouts = g.Sum(x => x.TimeoutCnt)
                         })
                               .OrderByDescending(x => x.TotalTimeouts)
                               .Where(x => x.TotalTimeouts > 0)
                               .Take(topN)
                               ;

            dynamic ret = new
            {
                labels = query.Select(x => x.Routine).ToArray(),
                data = query.Select(x => x.TotalTimeouts).ToArray()
            };

            return ret;
        }
    }


}