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
            this.RoutineExecution = re;
        }

        public RoutineExecution RoutineExecution { get; set; }

        public DateTime? RoutineExectionEndedUtc()
        {
            return this.RoutineExecution?.EndedUtc;
        }

        [JsonProperty("id")]
        public long id
        {
            get
            {
                return this.RoutineExecution.ExecutionId;
            }
        }

        [JsonProperty("n")]
        public string name
        {
            get
            {
                if (this.RoutineExecution == null) return null;
                return $"[{this.RoutineExecution.Schema}].[{this.RoutineExecution.Name}]";
            }
        }

        [JsonProperty("ce")]
        public long createdEpoch { get; private set; }
        [JsonProperty("ee")]
        public long? endedEpoch { get { return this.RoutineExecution.EndedUtc.ToEpochMS(); } }
        [JsonProperty("ex")]
        public string exception  { get { return this.RoutineExecution.ExceptionError?.Message; } }

        [JsonProperty("r")]
        public int? rowsAffected { get { return this.RoutineExecution?.RowsAffected; } }
    }

}