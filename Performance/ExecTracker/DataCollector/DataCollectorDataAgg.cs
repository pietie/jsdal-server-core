using System.Collections.Generic;

namespace jsdal_server_core.Performance.DataCollector
{
    public class DataCollectorDataAgg
    {
        public int Id { get; set; } // auto set by LiteDB
        public long Bracket { get; set; }
        public string Endpoint { get; set; }
        public string Schema { get; set; }
        public string Routine { get; set; }
        public int Executions { get; set; }
        public DataCollectorAggregateStat<ulong> DurationInMS { get; set; }
        public DataCollectorAggregateStat<ulong> Rows { get; set; }
        public DataCollectorAggregateStat<long> NetworkServerTimeInMS { get; set; }
        public DataCollectorAggregateStat<long> BytesReceived { get; set; }
        public int ExceptionCnt { get; set; }
        public int TimeoutCnt { get; set; }
        public List<string> LastExceptions { get; set; }
    }

    public class DataCollectorAggregateStat<T> where T : struct
    {
        public T? Max { get; set; }
        public T? Min { get; set; }

        public T? Sum { get; set; }
    }
}