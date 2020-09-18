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
            
            // TODO: Rework into deferred queue model
            RealtimeTracker.Add(ret);

            return ret;
        }
    }

    

}