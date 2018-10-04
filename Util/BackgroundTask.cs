using System;
using System.Collections.Generic;
using System.Threading;
using jsdal_server_core.Hubs;
using Microsoft.Extensions.Caching.Memory;

namespace jsdal_server_core
{
    public class BackgroundTask
    {
        private static List<BackgroundWorker> _workers = new List<BackgroundWorker>();

        public static List<BackgroundWorker> Workers { get { return _workers; }}
        public static Guid Queue(string name, Func<object> action)
        {
            Guid g = Guid.NewGuid();

            var worker = new BackgroundWorker(g, name, action);
            var thr = new Thread(new ThreadStart(worker.Run));

            thr.Start();

            return g;
        }

    }

    public class BackgroundWorker
    {
        private Func<object> _action;
        public Guid Guid { get; private set; }

        public object ReturnValue { get; private set; }

        public string Exception { get; private set; }

        public string Name { get; private set; }
        public bool IsDone { get; private set; }
        public DateTime? Created { get; private set; }
        public BackgroundWorker(Guid g, string name, Func<object> action)
        {
            this.Guid = g;
            this.Name = name;
            this._action = action;
            this.IsDone = false;
            this.Created = DateTime.Now;
        }
        public void Run()
        {
            try
            {
                Thread.CurrentThread.Name = "BackgroundWoker_" + this.Guid.ToString();

                if (this._action != null)
                {
                    this.ReturnValue = this._action.Invoke();
                }
            }
            catch(Exception ex)
            {
                this.Exception = ex.Message;
            }
            finally
            {
                this.IsDone = true;

                BackgroundTaskMonitor.Instance.NotifyOfChange(this);
            }
        }
    }

}