using System;
using LiteDB;

namespace jsdal_server_core.Performance.DataCollector
{
    public class DataCollectorDataEntry
    {
        public DataCollectorDataEntry(string shortId = null)
        {
            if (shortId == null) this.ShortId = shortid.ShortId.Generate(true, false, 7);
            else this.ShortId = shortId;

            this.Created = DateTime.Now;
        }

        public int Id { get; set; } // auto set by LiteDB
        public string ShortId { get; set; }

        public string Endpoint { get; set; }
        public string Schema { get; set; }
        public string Routine { get; set; }
        public ulong? DurationInMS { get; set; }
        public ulong? Rows { get; set; }
        public bool HasException { get; set; }
        public string Exception { get; set; }

        public bool IsTimeout { get; set; }

        public long? NetworkServerTimeInMS { get; set; }
        public long? BytesReceived { get; set; }

        public DateTime? Created { get; set; }
        public DateTime? EndDate { get; set; }

        [BsonIgnore]
        public bool IsDbRecordUpdate { get;set;}

    }
}