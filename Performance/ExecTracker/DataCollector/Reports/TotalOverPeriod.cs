using System;
using System.Collections.Generic;
using System.Linq;

namespace jsdal_server_core.Performance.DataCollector.Reports
{
    public static class TotalOverPeriod
    {
        public static dynamic TotalVitals(IEnumerable<DataCollectorDataAgg> baseQuery, DateTime from, DateTime to)
        {
            var totals = (from e in baseQuery
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
                                // .Take(topN)
                                ;

            var tttt = totals.ToList();

            var labels = totals.Select(x => x.BracketDate.ToString("dd MMM yyyy HH:mm")).ToArray();

            if (labels.Count() > 0)
            {
                int n = 0;

            }

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


            // TODO: JOIN with labels and consider some data points might not have an entry at all?

            // var numOfDays = to.Subtract(from).TotalDays;

            // var days = new List<DateTime>();

            // for (var i = 0; i < numOfDays; i++)
            // {
            //     days.Add(from.AddDays(i));
            // }
            // var days = new List<DateTime>();
            // var dt = from;

            // while (dt <= to)
            // {
            //     days.Add(dt);
            //     dt = dt.AddDays(1); // TODO: Make increment configurable? If range is only a couple of hours I might want to see this over minutes (lowest possible resolution is actual bracket sizes!)
            // }

            dynamic ret = new
            {
                labels = labels,
                datasets = new object[] { executionsDataset, avgDurationDataset }
                // datasets = 
                //             (select new
                //             {
                //                 label =  "Total executions",
                //                 data = totals.SelectMany(x=>x.TotalExecutions).ToArray()
                //             }).ToArray()
                // { label, data: [1,2,3] }[] 
            };

            return ret;
        }

    }

}