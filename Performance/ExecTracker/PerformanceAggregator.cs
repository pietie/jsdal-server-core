using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;


namespace jsdal_server_core.Performance
{
    // public static class PerformanceAggregator
    // {
    //     private static ConcurrentDictionary<string/*EndpointId*/, ConcurrentDictionary<string/*RoutineId*/, RoutineExecutionStats>> EndpointStats = new ConcurrentDictionary<string/*EndpointId*/, ConcurrentDictionary<string/*RoutineId*/, RoutineExecutionStats>>();
    //     public static void Add(RoutineExecution re)
    //     {
    //         if (!re.DurationInMS.HasValue) return;

    //         string routineKey = $"[{re.Schema}].[{re.Name}]";

    //         var endpointDict = EndpointStats.GetOrAdd(re.Endpoint.Id, new ConcurrentDictionary<string, RoutineExecutionStats>());

    //         var executionStats = endpointDict.GetOrAdd(routineKey, new RoutineExecutionStats());

    //         executionStats.RecordExectuion(re.RowsAffected, re.DurationInMS.Value);
    //     }


    // }

    // public class RoutineExecutionStats
    // {
    //     private int _executionCnt;
    //     private ulong _maxDurationInMS;
    //     private ulong _avgDurationInMS;

    //     // TODO:Track avg of last (X) calls or (X) seconds/mins/period... ==> Abstract into different structure?
        

    //     public RoutineExecutionStats()
    //     {
    //         this._executionCnt = 0;
    //         this._maxDurationInMS = 0;
    //         this._avgDurationInMS = 0;
    //     }
    //     public void RecordExectuion(int rowsAffected, ulong durationInMS)
    //     {
    //         Interlocked.Increment(ref _executionCnt);

    //         this._maxDurationInMS = Math.Max(durationInMS, this._maxDurationInMS);
    //         this._avgDurationInMS = (ulong)(((((ulong)_executionCnt - 1) * this._avgDurationInMS) + durationInMS) / (double)_executionCnt);
    //     }
    // }
}