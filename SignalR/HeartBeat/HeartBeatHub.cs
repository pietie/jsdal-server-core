using System;
using Microsoft.AspNetCore.SignalR;
using System.Threading;
using System.Linq;

namespace jsdal_server_core.Hubs.HeartBeat
{
    public class HeartBeatHub : Hub
    {
        public static readonly string GROUP_NAME = "HeartBeatHub.Tick";

        public HeartBeatHub()
        {
            
        }

        public int Init()
        {
            this.Groups.AddToGroupAsync(this.Context.ConnectionId, GROUP_NAME);
            return Environment.TickCount;
        }

        public static void Beat(IHubContext<HeartBeatHub> ctx)
        {
            ctx.Clients.Group(GROUP_NAME).SendAsync("tick", Environment.TickCount);
        }
    }

}