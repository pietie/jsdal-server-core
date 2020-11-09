using System;

namespace jsdal_server_core.Performance.DataCollector
{
    public class AggregateRegister
    {
        public int Id { get; set; }
        public DateTime? IntraHourLast { get; set; }
        public DateTime? DayLast { get; set; }
        public DateTime? WeekLast { get; set; }
        public DateTime? MonthLast { get; set; }
    }
}