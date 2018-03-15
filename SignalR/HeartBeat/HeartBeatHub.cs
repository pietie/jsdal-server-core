using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.SignalR;

namespace jsdal_server_core.Hubs.HeartBeat
{
    public class HeartBeatHub : Hub
    {
        public HeartBeatHub()
        {
            System.Timers.Timer t = new System.Timers.Timer(10000);
            t.Elapsed += (s, e) => { HeartBeatMonitor.Instance.NotifyObservers(); };
            t.Start();
        }

        public int Init()
        {
            return Environment.TickCount;
        }

        public IObservable<int> StreamTick()
        {
            return HeartBeatMonitor.Instance;
        }
    }

}