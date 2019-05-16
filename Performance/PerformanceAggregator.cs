using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;

// TODO: Sum, Avg executions...track interesting stats etc
//  TODO: transactions/per minute/second
namespace jsdal_server_core.Performance
{
    public static class PerformanceAggregator
    {
        /*
            Stats:

                Top (n) worst/best/something lists

                Top (n) execution history
        
        
        
         */

        private static ConcurrentDictionary<string/*EndpointId*/, ConcurrentDictionary<string/*RoutineId*/, RoutineExecutionStats>> EndpointStats = new ConcurrentDictionary<string/*EndpointId*/, ConcurrentDictionary<string/*RoutineId*/, RoutineExecutionStats>>();
        public static void Add(RoutineExecution re)
        {
            if (!re.DurationInMS.HasValue) return;

            string routineKey = $"[{re.Schema}].[{re.Name}]";

            var endpointDict = EndpointStats.GetOrAdd(re.EndpointId, new ConcurrentDictionary<string, RoutineExecutionStats>());

            var executionStats = endpointDict.GetOrAdd(routineKey, new RoutineExecutionStats());

            executionStats.RecordExectuion(re.RowsAffected, re.DurationInMS.Value);

            // TODO: Use a separate list/struct to track things like Total executions per/second (per Endpoint) ??

            //re.EndpointId
        }

        public static List<RoutineExecution> GetTopN(int maxNumberOfRows)
        {
            // TODO: Get limiting EndPoint : specific vs ALL

            //?EndpointStats.Values

            return null;
        }

    }

    public class RoutineExecutionStats
    {
        private int _executionCnt;
        private long _maxDurationInMS;
        private long _avgDurationInMS;

        // TODO:Track avg of last (X) calls or (X) seconds/mins/period... ==> Abstract into different structure?
        

        public RoutineExecutionStats()
        {
            this._executionCnt = 0;
            this._maxDurationInMS = 0;
            this._avgDurationInMS = 0;
        }
        public void RecordExectuion(int rowsAffected, long durationInMS)
        {
            Interlocked.Increment(ref _executionCnt);

            this._maxDurationInMS = Math.Max(durationInMS, this._maxDurationInMS);
            this._avgDurationInMS = (long)((((_executionCnt - 1) * this._avgDurationInMS) + durationInMS) / (double)_executionCnt);
        }
    }
}