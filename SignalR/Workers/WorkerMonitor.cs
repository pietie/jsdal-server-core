using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;

namespace jsdal_server_core.Hubs
{
    public class WorkerMonitor : IObservable<List<WorkerInfo>>
    {
        private static WorkerMonitor _singleton;
        List<IObserver<List<WorkerInfo>>> observers;

        public static WorkerMonitor Instance
        {
            get
            {
                if (_singleton == null) _singleton = new WorkerMonitor();

                return _singleton;
            }
        }

        private WorkerMonitor()
        {
            observers = new List<IObserver<List<WorkerInfo>>>();

            /*
                        ThreadPool.QueueUserWorkItem((state) =>
                        {
                            while (true)
                            {
                                this.NotifyObservers();
                                // TODO: Provide way to exit this thread?
                                Thread.Sleep(2000);
                            }
                        }); */
        }

        private class Unsubscriber : IDisposable
        {
            private List<IObserver<List<WorkerInfo>>> _observers;
            private IObserver<List<WorkerInfo>> _observer;

            public Unsubscriber(List<IObserver<List<WorkerInfo>>> observers, IObserver<List<WorkerInfo>> observer)
            {
                this._observers = observers;
                this._observer = observer;
            }

            public void Dispose()
            {
                if (!(_observer == null)) _observers.Remove(_observer);
            }
        }

        public IDisposable Subscribe(IObserver<List<WorkerInfo>> observer)
        {
            if (!observers.Contains(observer))
                observers.Add(observer);

            return new Unsubscriber(observers, observer);
        }

        public void NotifyObservers()
        {
            var packet = WorkSpawner.workerList.Select(wl =>
                {
                    return new WorkerInfo()
                    {
                        id = wl.ID,
                        name = wl.DBSource.Name,
                        desc = wl.Description,
                        status = wl.Status,
                        /*lastProgress = wl.lastProgress,
                        lastProgressMoment = wl.lastProgressMoment,
                        lastConnectMoment = wl.lastConnectedMoment,*/
                        isRunning = wl.IsRunning
                    };
                }).ToList();

            foreach (var observer in observers.ToArray())
            {
                if (observer != null)
                {
                    try
                    {
                        observer.OnNext(packet);
                    }
                    catch (System.OperationCanceledException)
                    {
                        // ignore the case where the Observer has been cancelled since
                    }
                }
            }
        }
    }


}