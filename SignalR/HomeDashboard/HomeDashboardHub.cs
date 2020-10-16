using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Channels;
using jsdal_server_core.SignalR.HomeDashboard;
using System.Collections.ObjectModel;

namespace jsdal_server_core.Hubs
{
    public class HomeDashboardHub : Hub
    {
        public static readonly string GROUP_NAME = "MainDashboard.Stats";
        public static readonly string GROUP_NAME_CLR_COUNTERS = "MainDashboard.ClrCounters";

        public HomeDashboardHub()
        {
        }

        public MainStats Init()
        {
            this.Groups.AddToGroupAsync(this.Context.ConnectionId, GROUP_NAME);
            return new MainStats();
        }

        public int ForceGCCollect()
        {
            int tick = Environment.TickCount;
            GC.Collect();
            return Environment.TickCount - tick;
        }

        public Dictionary<string, Dictionary<string, jsdal_server_core.Performance.dotnet.CounterEventArgs>> SubscribeToDotnetCorePerfCounters()
        {
            this.Groups.AddToGroupAsync(this.Context.ConnectionId, GROUP_NAME_CLR_COUNTERS);

            return DotNetCoreCounterListener.Instance.CounterValues.ToDictionary((kv) => kv.Key, kv => kv.Value);
        }

        public void UnsubscribeFromDotnetCorePerfCounters()
        {
            this.Groups.RemoveFromGroupAsync(this.Context.ConnectionId, GROUP_NAME_CLR_COUNTERS);
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            //this.Clients.Group(GROUP_NAME_CLR_COUNTERS).
            //this.Clients.Group().
            return base.OnDisconnectedAsync(exception);
        }

        public static void SendStats(IHubContext<HomeDashboardHub> ctx)
        {
            ctx.Clients.Group(HomeDashboardHub.GROUP_NAME).SendAsync("updateStats", new MainStats());
        }

    }




}