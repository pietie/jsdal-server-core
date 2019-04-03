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
        public DateTime? EndedUtc { get; protected set; }

        public Exception ExceptionError { get; protected set; }

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

        public string ToServerTimingEntry(string ix)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("v");
            sb.Append(ix);
            sb.Append(";dur=");
            sb.Append(this.DurationInMS);
            sb.Append(";desc=");
            sb.Append(ix);
            sb.Append(".");
            sb.Append(this.Name.Replace(" ", "_"));

            if (this._childStages != null)
            {
                for (var i = 0; i < this._childStages.Count; i++)
                {
                    sb.Append(',');
                    sb.Append(this._childStages[i].ToServerTimingEntry(ix + "." + i));
                }
            }


            return sb.ToString();
        }

        public void End()
        {
            this._stopwatch.Stop();

            this.DurationInMS = this._stopwatch.ElapsedMilliseconds;
            this.EndedUtc = DateTime.UtcNow;

            Hubs.Performance.RealtimeMonitor.Instance.NotifyObservers();
        }

        public void Exception(Exception e)
        {
            this.ExceptionError = e;
            this.End();
        }

        public string GetServerTimeHeader()
        {
            if (this._childStages == null || this._childStages.Count == 0) return null;
            //"meh;dur=123.4;desc=1.Some%20description,anot;dur=345.32;desc=2.Explain"
            var sb = new System.Text.StringBuilder();

            for (var i = 0; i < this._childStages.Count; i++)
            {
                var stage = this._childStages[i];
                if (sb.Length > 0) sb.Append(',');

                sb.Append(stage.ToServerTimingEntry(i.ToString()));
                // sb.Append("v");
                // sb.Append(i);
                // sb.Append(";dur=");
                // sb.Append(stage.DurationInMS);
                // sb.Append(";desc=");
                // sb.Append(i);
                // sb.Append(".");
                // sb.Append(stage.Name.Replace(" ", "_"));

            };

            return sb.ToString();
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