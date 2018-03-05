using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using jsdal_server_core;
using jsdal_server_core.Performance;

namespace jsdal_server_core.Hubs.Performance
{
    public class RealtimeMonitor : IObservable<List<RealtimeInfo>>
    {
        private static RealtimeMonitor _singleton;
        List<IObserver<List<RealtimeInfo>>> observers;

        public static RealtimeMonitor Instance
        {
            get
            {
                if (_singleton == null) _singleton = new RealtimeMonitor();

                return _singleton;
            }
        }

        private RealtimeMonitor()
        {
            observers = new List<IObserver<List<RealtimeInfo>>>();
        }

        private class Unsubscriber : IDisposable
        {
            private List<IObserver<List<RealtimeInfo>>> _observers;
            private IObserver<List<RealtimeInfo>> _observer;

            public Unsubscriber(List<IObserver<List<RealtimeInfo>>> observers, IObserver<List<RealtimeInfo>> observer)
            {
                this._observers = observers;
                this._observer = observer;
            }

            public void Dispose()
            {
                if (!(_observer == null)) _observers.Remove(_observer);
            }
        }

        public IDisposable Subscribe(IObserver<List<RealtimeInfo>> observer)
        {
            if (!observers.Contains(observer))
                observers.Add(observer);

            return new Unsubscriber(observers, observer);
        }

        public void NotifyObservers()
        {
            var packet = ExecTracker.ExecutionList.Select(e =>
                {
                    return new RealtimeInfo()
                    {
                        name = $"[{e.Schema}].[{e.Name}]",
                        createdEpoch = e.CreateDate.ToEpochMS(),
                        durationMS = e.DurationInMS,
                        rowsAffected = e.RowsAffected

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