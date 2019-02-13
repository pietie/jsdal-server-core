using System;
using System.Collections.Generic;
using System.Linq;
using jsdal_server_core.Performance;
using Microsoft.AspNetCore.SignalR;

namespace jsdal_server_core.Hubs.Performance
{
    public class RealtimeHub : Hub
    {
        public RealtimeHub()
        {

        }

        public List<RealtimeInfo> Init()
        {
            return RealtimeTracker.RealtimeItems;
        }

        public IObservable<List<RealtimeInfo>> StreamRealtimeList()
        {
            return RealtimeMonitor.Instance;
        }
        
        public int GetInitProgress()
        {
            return 10;
        }

    }

}