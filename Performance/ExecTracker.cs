namespace jsdal_server_core.Performance
{

    public class ExecTracker
    {
        public static RoutineExecution Begin(string name)
        {
            return new RoutineExecution(name);
        }
    }
}