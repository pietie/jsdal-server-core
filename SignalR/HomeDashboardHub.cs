using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace jsdal_server_core.Hubs
{
    public class HomeDashboardHub : Hub
    {
        private MainStatsMonitor mainStatsObs;
        public HomeDashboardHub()
        {
            mainStatsObs = new MainStatsMonitor();
            // ThreadPool.QueueUserWorkItem((state) =>
            // {
            //     while (true)
            //     {
            //         mainStatsObs.OnNext(new MainStats());
            //         // TODO: Provide way to exit this thread?
            //         Thread.Sleep(2000);
            //     }
            // });
        }

        public MainStats Init()
        {
            return new MainStats();
        }

        public IObservable<MainStats> StreamMainStats()
        {
            return mainStatsObs;
        }
    }

    public class MainStatsMonitor : IObservable<MainStats>
    {
        List<IObserver<MainStats>> observers;

        public MainStatsMonitor()
        {
            observers = new List<IObserver<MainStats>>();

            ThreadPool.QueueUserWorkItem((state) =>
            {
                while (true)
                {
                    this.NotifyObservers();
                    // TODO: Provide way to exit this thread?
                    Thread.Sleep(2000);
                }
            });
        }

        private class Unsubscriber : IDisposable
        {
            private List<IObserver<MainStats>> _observers;
            private IObserver<MainStats> _observer;

            public Unsubscriber(List<IObserver<MainStats>> observers, IObserver<MainStats> observer)
            {
                this._observers = observers;
                this._observer = observer;
            }

            public void Dispose()
            {
                if (!(_observer == null)) _observers.Remove(_observer);
            }
        }

        public IDisposable Subscribe(IObserver<MainStats> observer)
        {
            if (!observers.Contains(observer))
                observers.Add(observer);

            return new Unsubscriber(observers, observer);
        }

        public void NotifyObservers()
        {
            var stats = new MainStats();

            foreach (var observer in observers.ToArray())
            {
                if (observer != null)
                {
                    try
                    {
                        observer.OnNext(stats);
                    }
                    catch (System.OperationCanceledException)
                    {
                        // ignore the case where the Observer has been cancelled since
                    }
                }
            }

            // foreach (var temp in temps)
            // {
            //     System.Threading.Thread.Sleep(2500);
            //     if (temp.HasValue)
            //     {
            //         if (start || (Math.Abs(temp.Value - previous.Value) >= 0.1m))
            //         {

            //             previous = temp;
            //             if (start) start = false;
            //         }
            //     }
            //     // else
            //     // {
            //     //     foreach (var observer in observers.ToArray())
            //     //     {
            //     //         if (observer != null) observer.OnCompleted();
            //     //     }

            //     observers.Clear();
            //     break;
            // }

        }
    }

    public class MainStats
    {
        private MainStatsPerformance performance;
        public MainStats()
        {
            this.performance = new MainStatsPerformance();
        }
        public DateTime? WebServerStartDate { get { return Program.StartDate; } }
        public int TickCount
        {
            get { return Environment.TickCount; }
        }

        public int ProcessorCount
        {
            get { return Environment.ProcessorCount; }
        }

        public MainStatsPerformance Performance
        {
            get { return performance; }
        }

    }

    public class MainStatsPerformance
    {
        System.Diagnostics.Process process;
        public MainStatsPerformance()
        {
            process = System.Diagnostics.Process.GetCurrentProcess();
        }
        public long WorkingSet { get { return process.WorkingSet64; } }
        public long PeakWorkingSet { get { return process.PeakWorkingSet64; } }
        public long PrivateMemorySize { get { return process.PrivateMemorySize64; } }
    }
}