using System;
using System.Collections.Generic;

namespace jsdal_server_core.Performance
{
    public class RoutineExecution : ExecutionBase
    {
        public string Schema { get; set; }
        public string Routine { get; set; }
        public ExecutionRoutineType ExecutionRoutineType { get; set; }

        private List<RoutineExecution> _childStages;

        public RoutineExecution(string name) : base(name)
        {
            this._childStages = new List<RoutineExecution>();
        }

        public RoutineExecution BeginChildStage(string name)
        {
            if (!this.IsOpen) return null;

            var re = new RoutineExecution(name);

            this._childStages.Add(re);

            return re;
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