using System;
using System.Diagnostics;
using System.Collections.Generic;

namespace jsdal_server_core.Performance
{
    public class ExecutionBase
    {
        public List<ExecutionBase> _childStages;

        public DateTime? CreateDate { get; set; }
        protected bool IsOpen { get { return this._stopwatch?.IsRunning ?? false; } }
        //?public DateTime StartedUtc { get; protected set; }
        //?public DateTime EndedUtc { get; protected set; }

        private Stopwatch _stopwatch;


        public string Name { get; protected set; }
        public long? DurationInMS { get; private set; }

        public ExecutionBase(string name)
        {
            this.CreateDate = DateTime.Now;
            this._childStages = new List<ExecutionBase>();
            _stopwatch = Stopwatch.StartNew();
            this.Name = name;
            //this.StartedUtc = DateTime.UtcNow;
        }

        public void End()
        {
            this._stopwatch.Stop();

            this.DurationInMS = this._stopwatch.ElapsedMilliseconds;
            //!?this.EndedUtc = DateTime.UtcNow;

            Hubs.Performance.RealtimeMonitor.Instance.NotifyObservers();
        }

        public void Exception(Exception e)
        {
            this.End();
            // TODO: Record error
        }

        public ExecutionBase BeginChildStage(string name)
        {
            if (!this.IsOpen) return null;

            var re = new ExecutionBase(name);

            this._childStages.Add(re);

            return re;
        }
    }

}