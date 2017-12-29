using System;
using System.Diagnostics;

namespace jsdal_server_core.Performance
{
    public class ExecutionBase
    {
        protected bool IsOpen { get { return this._stopwatch?.IsRunning ?? false; } }
        //?public DateTime StartedUtc { get; protected set; }
        //?public DateTime EndedUtc { get; protected set; }

        private Stopwatch _stopwatch;
        

        public string Name { get; protected set; }
        public long? DurationInMS { get; private set; }

        public ExecutionBase(string name)
        {
            _stopwatch =  Stopwatch.StartNew();
            this.Name = name;
            //this.StartedUtc = DateTime.UtcNow;
        }

        public void End()
        {
            this._stopwatch.Stop();
            
            this.DurationInMS = this._stopwatch.ElapsedMilliseconds;
            //!?this.EndedUtc = DateTime.UtcNow;
        }

        public void Exception(Exception e)
        {
            this.End();
            // TODO: Record error
        }
    }

}