using System;
using jsdal_server_core.Performance;
using Newtonsoft.Json;

namespace jsdal_server_core.Hubs
{
    public class RealtimeInfo
    {
        public RealtimeInfo(RoutineExecution re)
        {
            this.createdEpoch = DateTime.Now.ToEpochMS();
            this.routineExecution = re;
        }

        private RoutineExecution routineExecution;

        public DateTime? RoutineExectionEndedUtc()
        {
            return this.routineExecution?.EndedUtc;
        }

        [JsonProperty("id")]
        public long id
        {
            get
            {
                return this.routineExecution.ExecutionId;
            }
        }

        [JsonProperty("n")]
        public string name
        {
            get
            {
                if (this.routineExecution == null) return null;
                return $"[{this.routineExecution.Schema}].[{this.routineExecution.Name}]";
            }
        }

        [JsonProperty("ce")]
        public long createdEpoch { get; private set; }
        [JsonProperty("ee")]
        public long? endedEpoch { get { return this.routineExecution.EndedUtc.ToEpochMS(); } }
        [JsonProperty("ex")]
        public string exception  { get { return this.routineExecution.ExceptionError?.Message; } }

        [JsonProperty("r")]
        public int? rowsAffected { get { return this.routineExecution?.RowsAffected; } }
    }

}