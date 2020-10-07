using System;
using jsdal_server_core.Settings.ObjectModel;

namespace jsdal_server_core.Performance
{
    public class RoutineExecution : ExecutionBase
    {
        private static long ExecutionSequence;
        private long _executionId;

        public long ExecutionId { get { return _executionId; } }

        public string ShortId { get; private set; }
        public string Schema { get; set; }
        //public Endpoint Endpoint { get; set; }
        public string EndpointPedigree { get; set; }
        public ExecutionRoutineType ExecutionRoutineType { get; set; }

        public int? RowsAffected { get; private set; }

        public RoutineExecution(Endpoint endpoint, string schema, string routine) : base(routine)
        {
            this._executionId = System.Threading.Interlocked.Increment(ref RoutineExecution.ExecutionSequence);
            this.Schema = schema;
            this.EndpointPedigree = endpoint.Pedigree;

            this.ShortId = shortid.ShortId.Generate(true, false, 6);
        }
        public void End(int rowsAffected)
        {
            base.End();

            this.RowsAffected = rowsAffected;

            if (rowsAffected < 0)
            {
                rowsAffected = 0;
            }

            //StatsDB.QueueRecordExecutionEnd(this.Endpoint.Id, this.Schema, this.Name, base.DurationInMS, rowsAffected);
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