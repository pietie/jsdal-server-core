using System;
using jsdal_server_core.Performance;

namespace jsdal_server_core.Hubs
{
    public class RealtimeInfo
    {
        public RealtimeInfo(RoutineExecution re)
        {
            this.created = DateTime.Now;
            this.routineExecution = re;
        }

        private DateTime created;
        private RoutineExecution routineExecution;

        public DateTime? RoutineExectionEndedUtc()
        {
            return this.routineExecution?.EndedUtc;
        }

        public string name
        {
            get
            {
                if (this.routineExecution == null) return null;
                return $"[{this.routineExecution.Name}].[{this.routineExecution.Schema}]";
            }
        }

        public long? createdEpoch { get { return this.routineExecution?.CreateDate.ToEpochMS(); } }
        public long? durationMS { get { return this.routineExecution?.DurationInMS; } }

        public int? rowsAffected { get { return this.routineExecution?.RowsAffected; } }
    }

}