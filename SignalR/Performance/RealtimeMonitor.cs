using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using jsdal_server_core;
using jsdal_server_core.Performance;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Channels;

namespace jsdal_server_core.Hubs.Performance
{
    public class RealtimeMonitor
    {
        private static RealtimeMonitor _singleton;

        private Channel<List<RealtimeInfo>> realtimeInfoChannel; // TODO: Instead of a List, can we reduce this to single worker info updates -- initially get a list and then just update, or add all new ones to a list

        public Channel<List<RealtimeInfo>> RealtimeInfoChannel { get { return this.realtimeInfoChannel; } }

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
            realtimeInfoChannel = Channel.CreateUnbounded<List<RealtimeInfo>>();
        }

        public void NotifyObservers()
        {
            var packet = RealtimeTracker.RealtimeItems.OrderByDescending(r=>r.createdEpoch).ToList();
            // var packet = MonitorSpawner.MonitorList.Select(mon =>
            //     {
            //         return new MonitorInfo()
            //         {
            //             Server = mon.Server.Url,
            //             Status = mon.Status,
            //             IsRunning = mon.IsRunning,
            //             ServerGuid = mon.Server.Guid
            //         };
            //     }).ToList();

            realtimeInfoChannel.Writer.WriteAsync(packet);
        }
    }

    // public class RealtimeMonitorOld : IObservable<List<RealtimeInfo>>
    // {
    //     private static RealtimeMonitorOld _singleton;
    //     List<IObserver<List<RealtimeInfo>>> observers;

    //     public static RealtimeMonitorOld Instance
    //     {
    //         get
    //         {
    //             if (_singleton == null) _singleton = new RealtimeMonitorOld();

    //             return _singleton;
    //         }
    //     }

    //     private RealtimeMonitorOld()
    //     {
    //         observers = new List<IObserver<List<RealtimeInfo>>>();
    //     }

    //     private class Unsubscriber : IDisposable
    //     {
    //         private List<IObserver<List<RealtimeInfo>>> _observers;
    //         private IObserver<List<RealtimeInfo>> _observer;

    //         public Unsubscriber(List<IObserver<List<RealtimeInfo>>> observers, IObserver<List<RealtimeInfo>> observer)
    //         {
    //             this._observers = observers;
    //             this._observer = observer;
    //         }

    //         public void Dispose()
    //         {
    //             if (!(_observer == null)) _observers.Remove(_observer);
    //         }
    //     }

    //     public IDisposable Subscribe(IObserver<List<RealtimeInfo>> observer)
    //     {
    //         if (!observers.Contains(observer))
    //             observers.Add(observer);

    //         return new Unsubscriber(observers, observer);
    //     }

    //     public void NotifyObservers()
    //     {

    //         // var hubContext = context.RequestServices
    //         //                             .GetRequiredService<IHubContext<RealtimeHub>>();
    //         return;
    //         var packet = RealtimeTracker.RealtimeItems;

    //         foreach (var observer in observers.ToArray())
    //         {
    //             if (observer != null)
    //             {
    //                 try
    //                 {
    //                     observer.OnNext(packet);
    //                 }
    //                 catch (System.OperationCanceledException)
    //                 {
    //                     // ignore the case where the Observer has been cancelled since
    //                 }
    //             }
    //         }
    //     }
    // }


}