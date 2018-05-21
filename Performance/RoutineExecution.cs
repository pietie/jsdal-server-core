using System;


namespace jsdal_server_core.Performance
{
    public class RoutineExecution : ExecutionBase
    {
        public string Schema { get; set; }
        public string EndpointId { get; set; }
        public ExecutionRoutineType ExecutionRoutineType { get; set; }

        public int RowsAffected { get;set; }

// TODO: public for now so we can expose it to frontend for testing
        

        public RoutineExecution(string endpointId, string schema, string routine) : base(routine)
        {
            this.Schema = schema;
            this.EndpointId = endpointId;
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