using System;

namespace jsdal_server_core.Settings.ObjectModel
{
    [Serializable]
    public class ExecutionPolicy
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool Default { get; set; }
        public bool Enabled { get; set; }
        public decimal CommandTimeoutInSeconds { get; set; }
        public ExecutionRetry DeadlockRetry { get; set; }

        public void UpdateFrom(ExecutionPolicy policy)
        {
            this.Name = policy.Name;
            this.Enabled = policy.Enabled;
            this.CommandTimeoutInSeconds = policy.CommandTimeoutInSeconds;
            this.DeadlockRetry = policy.DeadlockRetry;
        }
    }
}