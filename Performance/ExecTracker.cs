using System.Collections.Generic;

namespace jsdal_server_core.Performance
{

    public class ExecTracker
    {

        // TODO: Come up with a much smarter structure to use
        public static List<RoutineExecution> ExecutionList = new List<RoutineExecution>();
        public static RoutineExecution Begin(string endpointId, string schema, string routine)
        {
            var ret = new RoutineExecution(endpointId, schema, routine);

            ExecutionList.Add(ret);
            RealtimeTracker.Add(ret);

            return ret;
        }
    }
}