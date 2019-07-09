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
        public static RealtimeMonitor Instance
        {
            get; set;
        }

        private readonly IHubContext<RealtimeHub> _hubContext;
        public RealtimeMonitor(IHubContext<RealtimeHub> ctx)
        {
            this._hubContext = ctx;
        }

        public void NotifyObservers()
        {
            var packet = RealtimeTracker.RealtimeItems.OrderByDescending(r=>r.createdEpoch).ToList();

            _hubContext.Clients.Group(RealtimeHub.GROUP_NAME).SendAsync("update", packet);
        }
    }

}