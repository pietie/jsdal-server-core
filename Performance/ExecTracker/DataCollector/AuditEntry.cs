using System;

namespace jsdal_server_core.Performance.DataCollector
{
    public class AuditEntry
    {
        public DateTime? Created { get; set; }
        public string Message { get; set; }
        public AuditEntry()
        {
            this.Created = DateTime.Now;
        }
    }
}