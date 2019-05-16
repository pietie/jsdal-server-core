using System;


namespace jsdal_server_core.Performance
{
    public class RoutineExecution : ExecutionBase
    {
        private static long ExecutionSequence;
        private long _executionId;
        public long ExecutionId { get { return _executionId; } }
        public string Schema { get; set; }
        public string EndpointId { get; set; }
        public ExecutionRoutineType ExecutionRoutineType { get; set; }

        public int RowsAffected { get; private set; }

        public RoutineExecution(string endpointId, string schema, string routine) : base(routine)
        {
            this._executionId = System.Threading.Interlocked.Increment(ref RoutineExecution.ExecutionSequence);
            this.Schema = schema;
            this.EndpointId = endpointId;
        }

        public void End(int rowsAffected)
        {
            base.End();
            this.RowsAffected = rowsAffected;

            PerformanceAggregator.Add(this);
        }
    }


    public enum ExecutionRoutineType
    {
        Sproc = 1,
        UDF = 2,
        TVF = 3
    }

    public enum X
    {
        Query = 1,
        NonQuery = 2,
        Scalar = 3
    }
}