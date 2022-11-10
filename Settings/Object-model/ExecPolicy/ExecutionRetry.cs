using System;

namespace jsdal_server_core.Settings.ObjectModel
{
    [Serializable]
    public class ExecutionRetry
    {
        public bool Enabled { get; set; }
        public decimal MaxRetries { get; set; }

        public string Type { get; set; }

        public decimal Value { get; set; }
    }
}