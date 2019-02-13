using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using jsdal_server_core.Hubs;
using Microsoft.Extensions.Caching.Memory;

namespace jsdal_server_core
{
    public class BackgroundTask
    {
        //private static List<BackgroundWorker> _workers = new List<BackgroundWorker>();
        private static Dictionary<string, BackgroundWorker> _workers = new Dictionary<string, BackgroundWorker>();

        public static List<BackgroundWorker> Workers { get { return _workers.Values.ToList(); } }
        public static BackgroundWorker Queue(string key, string name, Func<object> action)
        {
            if (_workers.ContainsKey(key))
            {
                // return existing? delete/close off existing -- make that an input parameter so each task context can decide
                return _workers[key];
            }

            var worker = new BackgroundWorker(key, name, action);
            var thr = new Thread(new ThreadStart(worker.Run));

            lock (_workers)
            {
                _workers.Add(key, worker);
            }

            thr.Start();

            return worker;
        }

        public static void Cleanup(BackgroundWorker bw)
        {
            lock(_workers)
            {
                _workers.Remove(bw.Key);
            }
        }

        // public static BackgroundWorker GetWorker(string key)
        // {
        //     return _workers[key];
        // }

    }

    public class BackgroundWorker
    {
        private Func<object> _action;


        public string Key { get; private set; }

        public object ReturnValue { get; private set; }

        public string Exception { get; private set; }

        public string Name { get; private set; }
        public bool IsDone { get; private set; }
        public DateTime? Created { get; private set; }

        public double Progress { get; set; }
        public BackgroundWorker(string key, string name, Func<object> action)
        {
            this.Key = key;
            this.Name = name;
            this._action = action;
            this.IsDone = false;
            this.Created = DateTime.Now;
            this.Progress = 0;
        }
        public void Run()
        {
            try
            {
                Thread.CurrentThread.Name = "BackgroundWoker_" + this.Key.ToString();

                if (this._action != null)
                {
                    this.ReturnValue = this._action.Invoke();
                }
            }
            catch (Exception ex)
            {
                this.Exception = ex.Message;
            }
            finally
            {
                this.IsDone = true;

                BackgroundTaskMonitor.Instance.NotifyOfChange(this);

                BackgroundTask.Cleanup(this);
            }
        }
    }

}