using System;
using System.Collections.Generic;
using System.Linq;

namespace jsdal_server_core.Performance.DataCollector.Reports
{
    public static class TotalOverPeriod
    {
        public static dynamic TotalVitals(IEnumerable<DataCollectorDataAgg> baseQuery, DateTime from, DateTime to, string schema = null, string routine = null)
        {
            var totals = (from e in baseQuery
                          where schema == null || (e.Schema.Equals(schema, StringComparison.InvariantCultureIgnoreCase) && e.Routine.Equals(routine, StringComparison.InvariantCultureIgnoreCase))
                          group e by e.Bracket into g
                          select new
                          {
                              BracketDate = DateTime.ParseExact(g.Key.ToString(), "yyyyMMddHHmm", null),
                              TotalExecutions = g.Sum(x => x.Executions),
                              SumDurationInMS = g.Sum(x => (decimal)x.DurationInMS.Sum),
                              SumNetworkServerTimeInMS = g.Sum(x => (decimal)x.NetworkServerTimeInMS.Sum),
                              SumBytesReceived = g.Sum(x => (decimal)x.BytesReceived.Sum),
                              TotalExceptions = g.Sum(x => x.ExceptionCnt),
                              TotalTimeouts = g.Sum(x => x.TimeoutCnt)
                          })
                        .OrderBy(x => x.BracketDate)
                                ;

            var labels = totals.Select(x => x.BracketDate.ToString("dd MMM yyyy HH:mm")).ToArray();

            var executionsDataset = new
            {
                label = "Total executions",
                data = (from e in totals
                        select e.TotalExecutions)
            };

            var avgDurationDataset = new
            {
                label = "Avg duration(ms)",
                data = (from e in totals
                        select e.TotalExecutions > 0 ? ((int?)((e.SumDurationInMS / e.TotalExecutions) + 0.5M)) : null)
            };

            var avgNetworkSystemTime = new
            {
                label = "Avg network time(ms)",
                data = (from e in totals
                        select e.TotalExecutions > 0 ? (int?)((e.SumNetworkServerTimeInMS / e.TotalExecutions) + 0.5M) : null)
            };

            var avgKBReceived = new
            {
                label = "Avg KB received",
                data = (from e in totals
                        select e.TotalExecutions > 0 ? (int?)((e.SumBytesReceived / e.TotalExecutions / 1024M) + 0.5M) : null)
            };

            var exceptionCnt = new
            {
                label = "Exception count",
                data = (from e in totals
                        select e.TotalExceptions)
            };

            var timeoutCnt = new
            {
                label = "Timeout count",
                data = (from e in totals
                        select e.TotalTimeouts)
            };

            dynamic ret = new
            {
                labels = labels,
                datasets = new object[] { executionsDataset, avgDurationDataset, avgNetworkSystemTime, avgKBReceived, exceptionCnt, timeoutCnt }
            };

            return ret;
        }

    }

}