using System.Collections.Generic;

namespace jsdal_server_core.Performance
{

    public class ExecTracker
    {
        // keep a list of currently running executions (for live tracking!)

        // TODO: Come up with a much smarter structure to use
        public static List<RoutineExecution> ExecutionList = new List<RoutineExecution>();
        public static RoutineExecution Begin(string dbSourceKey, string schema, string routine)
        {
            var ret = new RoutineExecution(dbSourceKey, schema, routine);

            ExecutionList.Add(ret);

            Hubs.Performance.RealtimeMonitor.Instance.NotifyObservers();

            return ret;
        }
    }
}